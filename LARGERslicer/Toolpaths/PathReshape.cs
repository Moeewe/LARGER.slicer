using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>What to do with a part whose wall closes to a roof by itself.</summary>
    public enum RoofMode { KeepLayers = 0, Stretch = 1, Refuse = 2 }

    public class ReshapeOptions
    {
        /// <summary>True: the tool is tilted square to the new layers (robot); false: it stays vertical (gantry).</summary>
        public bool Tilt = true;
        /// <summary>mm; 0 = automatic (see PathReshape.AutoMin / AutoMax).</summary>
        public double MinLayer, MaxLayer;
        /// <summary>Steepest slope of a layer, degrees; 0 = 30 with a tilted tool, what the tip of the nozzle allows (AutoSlope) with a vertical one.</summary>
        public double MaxSlope;
        /// <summary>The shape is rounded over about this length in plan (mm); 0 = 6 bead widths.</summary>
        public double Smooth;
        /// <summary>Layers at the bottom that stay flat; -1 = the first layer and the solid bottom.</summary>
        public int FlatLayers = -1;
        public RoofMode Roof = RoofMode.KeepLayers;
        /// <summary>Where layers get thicker than Max Layer, they are printed in 3 or 5 thinner passes (forth, back, forth).</summary>
        public bool ExtraPasses;
        /// <summary>For the variants that also change the bead width: largest and smallest width as a factor of the width the path has (0 = automatic).</summary>
        public double MinWidth, MaxWidth;
        /// <summary>0 = the heights as the field says; 100 = as much material as before, 80 = 80 % of it.</summary>
        public double Material;
        /// <summary>mm3/s no move may exceed; 0 = off; below 0 = never more than the path needed before.</summary>
        public double MaxFlow;
        /// <summary>The head, for the way out and the checks.</summary>
        public NonPlanarCheck.Head Head = new NonPlanarCheck.Head();
        /// <summary>False: the shape is bent as the field says, without limits (tests).</summary>
        public bool Limit = true;
        /// <summary>Per path point: a run of passes may not go over the move that ends here (code lines in between).</summary>
        public bool[] BreakBefore;
        /// <summary>False: no check of the result (code files check it themselves).</summary>
        public bool Check = true;
    }

    /// <summary>
    /// The map that bends a part: every point keeps its place in plan (x, y) and moves in z only.
    /// Up to ZFlat nothing moves; between ZFlat and ZKeep heights are stretched by S(x, y); above ZKeep (a roof)
    /// everything moves along with the top below it. z goes up strictly with z, so the map can never fold.
    /// </summary>
    public class ShapeMap
    {
        /// <summary>Bottom of the part, end of the flat layers, start of the kept layers, top of the part.</summary>
        public double ZBase, ZFlat, ZKeep, ZTop;
        internal ShapeField.Spline2 S;
        public double SMin = 1, SMax = 1;
        public double Span => ZKeep - ZFlat;

        public double Factor(double x, double y) => S == null ? 1 : S.At(x, y);

        public double Z(double x, double y, double z)
        {
            if (z <= ZFlat || S == null) return z;
            double s = S.At(x, y);
            return z <= ZKeep ? ZFlat + (z - ZFlat) * s : z + (s - 1) * Span;
        }

        /// <summary>The mapped height, and how the map slopes there: kx, ky = change of height along x and y, kz along z.</summary>
        public double Z(double x, double y, double z, out double kx, out double ky, out double kz)
        {
            kx = ky = 0; kz = 1;
            if (z <= ZFlat || S == null) return z;
            double s = S.At(x, y, out double sx, out double sy);
            double up = Math.Min(z, ZKeep) - ZFlat;
            kx = sx * up; ky = sy * up;
            if (z <= ZKeep) { kz = s; return ZFlat + up * s; }
            return z + (s - 1) * Span;
        }

        public Point3d Map(Point3d p) => new Point3d(p.X, p.Y, Z(p.X, p.Y, p.Z));

        /// <summary>Back to the unbent part.</summary>
        public Point3d Back(Point3d p)
        {
            if (p.Z <= ZFlat || S == null) return p;
            double s = S.At(p.X, p.Y), top = ZFlat + Span * s;
            return new Point3d(p.X, p.Y, p.Z <= top ? ZFlat + (p.Z - ZFlat) / s : p.Z - (s - 1) * Span);
        }

        /// <summary>The direction square to a layer after bending (n = before, unit length).</summary>
        public static Vector3d Normal(Vector3d n, double kx, double ky, double kz)
        {
            double z = n.Z / kz, x = n.X - kx * z, y = n.Y - ky * z, l = Math.Sqrt(x * x + y * y + z * z);
            return l > 1e-12 ? new Vector3d(x / l, y / l, z / l) : Vector3d.ZAxis;
        }
    }

    /// <summary>So that checks after the bending (Path Check, Material Field) know the layers: back to flat is the map the other way.</summary>
    public class ReshapeWarp : Warp
    {
        private readonly ShapeMap _m; private readonly Warp _before;
        public ReshapeWarp(ShapeMap m, Warp before, bool tilt) { _m = m; _before = before; TiltTool = tilt; }
        public ShapeMap Shape => _m;
        public override string Name => "reshaped" + (_before != null ? ", before that " + _before.Name : "");
        public override bool UseBeadLaw => _before != null && _before.UseBeadLaw;
        public override double MinLoopFactor => _before != null ? _before.MinLoopFactor : base.MinLoopFactor;
        public override Point3d ToFlat(Point3d real) { var p = _m.Back(real); return _before != null ? _before.ToFlat(p) : p; }
        public override Point3d ToReal(Point3d flat, out Vector3d normal)
        {
            Vector3d n = Vector3d.ZAxis; Point3d p = flat;
            if (_before != null) p = _before.ToReal(flat, out n);
            double z = _m.Z(p.X, p.Y, p.Z, out double kx, out double ky, out double kz);
            normal = ShapeMap.Normal(n, kx, ky, kz);
            return new Point3d(p.X, p.Y, z);
        }
    }

    /// <summary>
    /// Finds what a bead lies on: the closest point on the beads printed earlier, on the layer below or right
    /// under it on its own layer. Beads clearly above do not count.
    /// </summary>
    public class LapBelow
    {
        private readonly Toolpath _tp; private readonly double _cell, _reach;
        private readonly Dictionary<int, List<int>> _segs = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> _below = new Dictionary<int, int>();
        private readonly Dictionary<int, Dictionary<long, List<int>>> _grids = new Dictionary<int, Dictionary<long, List<int>>>();
        private readonly Queue<int> _made = new Queue<int>();

        public static bool Prints(Toolpath tp, int i) => i > 0 && tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;

        /// <summary>Length of the path up to every point.</summary>
        public readonly double[] Along;

        public LapBelow(Toolpath tp)
        {
            Along = new double[tp.Count]; for (int i = 1; i < tp.Count; i++) Along[i] = Along[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]);
            _tp = tp; _reach = 2 * tp.Settings.BeadWidth; _cell = Math.Max(_reach, 2);
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Prints(tp, i) || tp.Roles[i] == PathRole.Skirt) continue;
                if (!_segs.TryGetValue(tp.Layers[i], out var l)) _segs[tp.Layers[i]] = l = new List<int>();
                l.Add(i);
            }
            int last = int.MinValue;
            foreach (int k in _segs.Keys.OrderBy(k => k)) { _below[k] = last; last = k; }
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        private Dictionary<long, List<int>> Grid(int layer)
        {
            if (_grids.TryGetValue(layer, out var g)) return g;
            g = new Dictionary<long, List<int>>();
            foreach (int i in _segs[layer])
            {
                Point3d a = _tp.Points[i - 1], b = _tp.Points[i];
                double dx = b.X - a.X, dy = b.Y - a.Y; int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / (0.5 * _cell)));
                long had = long.MinValue;
                for (int k = 0; k <= n; k++)
                {
                    long key = Key((int)Math.Floor((a.X + dx * k / n) / _cell), (int)Math.Floor((a.Y + dy * k / n) / _cell));
                    if (key == had) continue;
                    had = key;
                    if (!g.TryGetValue(key, out var l)) g[key] = l = new List<int>();
                    l.Add(i);
                }
            }
            _grids[layer] = g; _made.Enqueue(layer);
            while (_made.Count > 6) _grids.Remove(_made.Dequeue());
            return g;
        }

        /// <summary>
        /// The closest point of the layer below, at most two bead widths beside p. The same layer counts too where it was
        /// printed clearly earlier and lies right under p (the closing lap of a spiral, passes over one another);
        /// along = the length of the path up to p, move = the move p belongs to. On the
        /// same layer a bead that runs the other way close by is a neighbour (strips that nearly touch), not what p lies on.
        /// </summary>
        public bool Find(Point3d p, int layer, double up, double along, int move, out Point3d q)
        {
            q = p; double best = double.MaxValue, w = _tp.Settings.BeadWidth;
            int cx = (int)Math.Floor(p.X / _cell), cy = (int)Math.Floor(p.Y / _cell);
            double mx = move > 0 ? _tp.Points[move].X - _tp.Points[move - 1].X : 0, my = move > 0 ? _tp.Points[move].Y - _tp.Points[move - 1].Y : 0, ml = Math.Sqrt(mx * mx + my * my);
            for (int pass = 0; pass < 2; pass++)
            {
                int lb = layer;
                if (pass == 0 && (!_below.TryGetValue(layer, out lb) || lb == int.MinValue)) continue;
                if (pass == 1 && !_segs.ContainsKey(layer)) continue;
                var g = Grid(lb); double reach2 = pass == 0 ? _reach * _reach : 0.0625 * w * w;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                    {
                        if (!g.TryGetValue(Key(cx + a, cy + b), out var l)) continue;
                        foreach (int i in l)
                        {
                            if (Along[i] > along - 4 * w) continue; // the bead just printed is not what p lies on
                            Point3d s = _tp.Points[i - 1], e = _tp.Points[i];
                            double ex = e.X - s.X, ey = e.Y - s.Y, ez = e.Z - s.Z, L2 = ex * ex + ey * ey + ez * ez;
                            double t = L2 < 1e-18 ? 0 : ((p.X - s.X) * ex + (p.Y - s.Y) * ey + (p.Z - s.Z) * ez) / L2;
                            t = t < 0 ? 0 : t > 1 ? 1 : t;
                            double qx = s.X + ex * t, qy = s.Y + ey * t, qz = s.Z + ez * t;
                            if (qz > p.Z + (pass == 0 ? up : 1e-4)) continue;
                            double dx = p.X - qx, dy = p.Y - qy, d2 = dx * dx + dy * dy;
                            if (d2 > reach2) continue;
                            if (pass == 1 && d2 > 0.0025 * w * w && ml > 1e-6 && ex * mx + ey * my < 0.7 * ml * Math.Sqrt(ex * ex + ey * ey)) continue;
                            d2 += (p.Z - qz) * (p.Z - qz);
                            if (d2 < best) { best = d2; q = new Point3d(qx, qy, qz); }
                        }
                    }
            }
            return best < double.MaxValue;
        }
    }

    /// <summary>
    /// Gives a finished path another shape: more print height where the field asks for it, less elsewhere, with
    /// the same number of layers. The plan of the path does not change, so seam, infill, bridges and the one
    /// continuous line stay. The extrusion of every move is worked out again from its new distance to the lap
    /// below. Skirt, way in and flat layers are not touched; the way out starts at the new end.
    /// </summary>
    public static class PathReshape
    {
        public class Result
        {
            /// <summary>The new path; null when the job was refused (see Refused).</summary>
            public Toolpath Path;
            public string Refused;
            public ShapeMap Map;
            public List<string> Report = new List<string>(), Warnings = new List<string>();
            /// <summary>Per new point: the point of the old path whose move it belongs to.</summary>
            public List<int> Source = new List<int>();
            /// <summary>Per new point: extrusion of its move = extrusion of the source move x this.</summary>
            public List<double> Weight = new List<double>();
            /// <summary>Per new point: true if it was put in (the end of the source move is elsewhere).</summary>
            public List<bool> Inserted = new List<bool>();
            public bool Identity;
            /// <summary>The limits changed the wanted shape clearly (see Warnings).</summary>
            public bool Cut;
            /// <summary>For the variants with wider beads: how much wider at a place.</summary>
            public Func<double, double, double> Wider;
            /// <summary>For cut laps: the share of the height that stays at a place.</summary>
            public Func<double, double, double> Cut01;
            /// <summary>
            /// What was really made (after limits, rounding and material), as a field of factors: the Previous of the next step
            /// of the loop. Taking the wanted field instead would pile up wishes the printer cannot fulfil.
            /// </summary>
            public ShapeField Realised(Toolpath before)
            {
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                foreach (var p in Places(before)) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
                var m = Map; double H = m != null ? Math.Max(1e-9, m.ZTop - m.ZBase) : 1; var wide = Wider; var cut = Cut01;
                Func<double, double, double> f = (x, y) => (m == null ? 1 : (m.Z(x, y, m.ZTop) - m.ZBase) / H) * (wide != null ? wide(x, y) : 1) * (cut != null ? cut(x, y) : 1);
                return ShapeField.FromFunction(f, false, "reshaped path, as made", x0, y0, x1, y1);
            }
            /// <summary>What the same check finds in the path before the reshaping; null = nothing.</summary>
            public string BeforeNote;
            /// <summary>Extruded material of the part (by E, without skirt), before and after, and as a share.</summary>
            public double EBefore, EAfter;
            public double MaterialShare => EBefore > 0 ? EAfter / EBefore : 1;
            public double WantedMin, WantedMax, HeightBefore, HeightAfterMin, HeightAfterMax, Scale = 1, LimitLow, LimitHigh;
            public int PassRuns, RefPoints, RefByColumn, Added, ThickLeft;
            public int FirstMoved = -1, LastPrint = -1;
            public MultiAxisCheck.Result Check;
            public ReshapeVariant Variant; public double WidthMin, WidthMax; public int Travels;
        }

        /// <summary>Smallest layer height that still prints well: 0.8 mm, less only for beads below 8 x 4 (0.4 x layer), never below a tenth of the bead width.</summary>
        public static double AutoMin(PrintSettings st) => Math.Max(0.1 * st.BeadWidth, Math.Min(0.4 * st.LayerHeight, 0.8));
        /// <summary>Largest layer height: half the bead width (the bead is at least twice as wide as high) or the layer height of the print, at most 0.8 x nozzle when the nozzle is known.</summary>
        public static double AutoMax(PrintSettings st) => AutoMax(st, st.BeadWidth);
        /// <summary>The same for a place where the bead has another width.</summary>
        public static double AutoMax(PrintSettings st, double width)
        {
            double m = Math.Max(0.5 * width, st.LayerHeight);
            return st.Nozzle > 0 ? Math.Max(st.LayerHeight, Math.Min(m, 0.8 * st.Nozzle)) : m;
        }

        internal static bool Part(Toolpath tp, int i) => LapBelow.Prints(tp, i) && tp.Roles[i] != PathRole.Skirt;
        private static double Len(Vector3d v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        // ------------------------------------------------------------------ the map

        private class Frame0 { public double Hn, H5, H95; public bool Roof; public double RoofZ; public int Layers; }

        /// <summary>Looks at the path: bottom, flat layers, top, roof, usual layer height. Text = why it cannot be reshaped, null = fine.</summary>
        private static string Look(Toolpath tp, ReshapeOptions o, ShapeMap m, Frame0 f, List<string> rep)
        {
            var st = tp.Settings; int n = tp.Count, first = -1, last = -1;
            for (int i = 1; i < n; i++) if (Part(tp, i)) { if (first < 0) first = i; last = i; }
            if (first < 0) return "The path prints nothing.";
            double lean = 0; int cnt = 0;
            double zTop = double.MinValue, z0 = double.MaxValue;
            var hs = new List<double>();
            var box = new SortedDictionary<int, double[]>(); // x0 x1 y0 y1 zmin zmax bottom
            for (int i = first; i <= last; i++)
            {
                if (!Part(tp, i)) continue;
                var p = tp.Points[i];
                if (double.IsNaN(p.X + p.Y + p.Z) || double.IsInfinity(p.X + p.Y + p.Z)) return Inv.F($"Point {i} of the path is not a number.");
                if (p.Z > zTop) zTop = p.Z; if (p.Z < z0) z0 = p.Z;
                if (!box.TryGetValue(tp.Layers[i], out var b)) box[tp.Layers[i]] = b = new[] { double.MaxValue, double.MinValue, double.MaxValue, double.MinValue, double.MaxValue, double.MinValue, 0 };
                if (p.X < b[0]) b[0] = p.X; if (p.X > b[1]) b[1] = p.X; if (p.Y < b[2]) b[2] = p.Y; if (p.Y > b[3]) b[3] = p.Y;
                if (p.Z < b[4]) b[4] = p.Z; if (p.Z > b[5]) b[5] = p.Z;
                if (tp.Roles[i] == PathRole.Bottom) b[6] = 1;
                if (tp.Roles[i] != PathRole.Seam && tp.Heights[i] > 0.5 * st.LayerHeight) hs.Add(tp.Heights[i]);
                if (tp.Layers[i] <= tp.Layers[first] + 1) { lean += tp.Normals[i].Z; cnt++; }
            }
            if (box.Count < 4) return Inv.F($"The path has only {box.Count} layer(s): there is no height to reshape.");
            if (cnt > 0 && lean / cnt < 0.94) return "The layers of this path lean (angled printing): the print height does not run upwards. Reshaping works on layers that start flat on the bed.";
            hs.Sort(); f.Hn = hs.Count > 0 ? hs[hs.Count / 2] : st.LayerHeight; f.H5 = hs.Count > 0 ? hs[(int)(0.05 * (hs.Count - 1))] : f.Hn; f.H95 = hs.Count > 0 ? hs[(int)(0.95 * (hs.Count - 1))] : f.Hn;
            f.Layers = box.Count;
            double h1 = tp.Heights[first] > 0.05 ? tp.Heights[first] : st.FirstLayer;
            m.ZBase = z0 - h1; m.ZTop = m.ZKeep = zTop;
            var keys = box.Keys.ToList();
            int flat = 1;
            if (o.FlatLayers >= 0) flat = Math.Max(1, o.FlatLayers);
            else while (flat < keys.Count && box[keys[flat]][6] > 0) flat++;
            if (flat >= keys.Count - 2) return Inv.F($"{flat} of {keys.Count} layers stay flat: nothing is left to reshape.");
            m.ZFlat = box[keys[flat - 1]][5];
            if (flat == 1 && m.ZFlat > z0 + 0.5 * f.Hn && box[keys[0]][4] > z0 - 1e-9 && o.FlatLayers < 1) m.ZFlat = Math.Min(m.ZFlat, z0 + 0.02 * f.Hn); // the first layer climbs (spiral without a flat lap)
            // a wall that closes: the box of the layers shrinks to a fraction of its largest size
            double A(double[] b) => Math.Max(0, b[1] - b[0]) * Math.Max(0, b[3] - b[2]);
            double amax = keys.Skip(1).Max(k => A(box[k])), aend = A(box[keys[keys.Count - 1]]);
            var be = box[keys[keys.Count - 1]]; double narrow = Math.Min(be[1] - be[0], be[3] - be[2]);
            // an open funnel or a cone is no roof: it closes only when the last lap is (nearly) a point or a ridge
            if (amax > 1e-6 && aend < 0.35 * amax && (aend < 0.03 * amax || narrow < 8 * st.BeadWidth))
            {
                f.Roof = true;
                int k = keys.Count - 1; while (k > 0 && A(box[keys[k]]) < 0.97 * amax) k--; // (the first layer may carry a skirt or brim that is not marked)
                f.RoofZ = Math.Max(m.ZFlat, box[keys[k]][5]);
                rep.Add(Inv.F($"The wall closes to a roof: it starts to lean in at about Z {f.RoofZ:0.#} of {zTop:0.#} mm."));
            }
            return null;
        }

        /// <summary>The map for a path: the field, kept within the limits, rounded, and fitted to the wanted material.</summary>
        public static ShapeMap Fit(Toolpath tp, ShapeField field, ReshapeOptions o, Result r)
        {
            var m = new ShapeMap(); var f = new Frame0(); var st = tp.Settings;
            r.Refused = Look(tp, o, m, f, r.Report);
            if (r.Refused != null) return null;
            double H = m.ZTop - m.ZBase;
            r.HeightBefore = H;
            if (f.Roof)
            {
                if (o.Roof == RoofMode.Refuse) { r.Refused = Inv.F($"The wall closes to a roof from Z {f.RoofZ:0.#} mm on, and Roof is set to refuse."); return null; }
                if (o.Roof == RoofMode.KeepLayers)
                {
                    if (f.RoofZ - m.ZFlat < 0.15 * H)
                    {
                        r.Refused = Inv.F($"The wall closes to a roof and leans in almost from the bed (Z {f.RoofZ:0.#} mm of {m.ZTop:0.#}): there is no straight wall below the roof whose layers could become thicker. Reshape the model and slice it anew with the guide surface, or set Roof to Stretch and look at the checks.");
                        return null;
                    }
                    m.ZKeep = f.RoofZ;
                    r.Report.Add(Inv.F($"The layers of the roof keep their thickness and move along with the wall below them (Roof = keep layers): only Z {m.ZFlat:0.#} to {m.ZKeep:0.#} is stretched."));
                }
                else r.Warnings.Add("The roof is stretched with the wall: where it becomes lower its laps lie flatter. Look at the checks and print a test piece.");
            }
            double span = m.Span;
            // where the width varies along the path, the narrow places say how high a bead may become
            var ws = new List<double>(); for (int i = 1; i < tp.Count; i++) if (Part(tp, i)) ws.Add(tp.Widths[i]);
            ws.Sort(); double w5 = ws[(int)(0.05 * (ws.Count - 1))], w95 = ws[(int)(0.95 * (ws.Count - 1))]; bool varies = w95 > 1.05 * w5 && o.MaxLayer <= 0 && o.Limit;
            double minL = o.MinLayer > 0 ? o.MinLayer : AutoMin(st), maxL = o.MaxLayer > 0 ? o.MaxLayer : AutoMax(st, w5);
            if (minL > maxL) { r.Refused = Inv.F($"Min Layer ({minL:0.##} mm) is above Max Layer ({maxL:0.##} mm)."); return null; }
            double sLo = Math.Min(1, minL / f.H5), sHi = Math.Max(1, (o.ExtraPasses ? 5 : 1) * maxL / f.H95);
            if (varies) sHi = Math.Max(sHi, (o.ExtraPasses ? 5 : 1) * AutoMax(st, w95) / f.H95); // the narrow places are looked at one by one below
            if (!o.Limit) { sLo = 0.02; sHi = 1e6; }
            r.LimitLow = sLo; r.LimitHigh = sHi;

            // ---- grid over the part
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Part(tp, i)) continue;
                for (int k = i - 1; k <= i; k++) { var p = tp.Points[k]; if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X; if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y; }
            }
            double size = Math.Max(x1 - x0, y1 - y0), cell = Math.Max(Math.Max(0.5, st.BeadWidth), size / 70);
            while ((Math.Ceiling((x1 - x0) / cell) + 1) * (Math.Ceiling((y1 - y0) / cell) + 1) > 4200) cell *= 1.05;
            // the grid ends at the part: what the field says outside must not pull at its edge
            int nx = Math.Max(4, (int)Math.Ceiling((x1 - x0) / cell - 1e-6) + 1), ny = Math.Max(4, (int)Math.Ceiling((y1 - y0) / cell - 1e-6) + 1);
            double gx = 0.5 * (x0 + x1) - 0.5 * cell * (nx - 1), gy = 0.5 * (y0 + y1) - 0.5 * cell * (ny - 1);
            var raw = new double[nx * ny]; double wMin = double.MaxValue, wMax = 0;
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                {
                    double v = field.Factor(gx + i * cell, gy + j * cell, H);
                    if (double.IsNaN(v) || double.IsInfinity(v)) { r.Refused = Inv.F($"The field has no number at x {gx + i * cell:0.#}, y {gy + j * cell:0.#}."); return null; }
                    raw[j * nx + i] = v;
                }
            foreach (var p in Places(tp)) { double v = field.Factor(p.X, p.Y, H); if (v < wMin) wMin = v; if (v > wMax) wMax = v; }
            r.WantedMin = wMin; r.WantedMax = wMax;
            if (raw.All(v => Math.Abs(v - 1) < 1e-9) && !(o.Material > 0 && Math.Abs(o.Material - 100) > 1e-6)) { r.Identity = true; return m; }

            double[] hiAt = null;
            if (varies)
            {
                hiAt = new double[nx * ny]; for (int k = 0; k < hiAt.Length; k++) hiAt[k] = double.NaN;
                for (int i = 1; i < tp.Count; i++)
                {
                    if (!Part(tp, i)) continue;
                    Point3d a = tp.Points[i - 1], b = tp.Points[i]; int k = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / cell));
                    for (int j = 0; j <= k; j++)
                    {
                        double u = j / (double)k, lim = Math.Max(1, (o.ExtraPasses ? 5 : 1) * AutoMax(st, tp.Widths[i - 1] + (tp.Widths[i] - tp.Widths[i - 1]) * u) / f.H95);
                        int gi = (int)Math.Round((a.X + (b.X - a.X) * u - gx) / cell), gj = (int)Math.Round((a.Y + (b.Y - a.Y) * u - gy) / cell); if (gi < 0 || gj < 0 || gi >= nx || gj >= ny) continue;
                        int q = gj * nx + gi; if (double.IsNaN(hiAt[q]) || lim < hiAt[q]) hiAt[q] = lim;
                    }
                }
                ShapeField.Fill(hiAt, nx, ny);
            }
            double slope = o.MaxSlope > 0 ? Math.Min(o.MaxSlope, 80) : AutoSlope(st, o.Tilt, o.Head);
            double smooth = o.Smooth > 0 ? o.Smooth : 6 * st.BeadWidth;
            int blur = o.Limit ? (int)Math.Round(smooth / cell / 2) : 0;
            double lip = Math.Tan(slope * Math.PI / 180) / span;
            // with a tilted tool the top may not curve faster than the tool can turn at the print speed
            double turn = o.Tilt ? Math.Min(1.4, 0.7 * 30.0 * 60.0 / Math.Max(1, st.Speed)) : double.MaxValue;
            bool softened = false;

            Func<double, ShapeMap> make = c =>
            {
                var s = new double[raw.Length];
                for (int k = 0; k < s.Length; k++)
                {
                    // the top wanted is c x factor x height; what is stretched is the part between ZFlat and ZKeep
                    double v = (m.ZBase + c * raw[k] * H - (m.ZTop - m.ZKeep) - m.ZFlat) / span;
                    double top = hiAt != null ? hiAt[k] : sHi; if (v < sLo) v = sLo; else if (v > top) v = top;
                    s[k] = v;
                }
                if (o.Limit)
                {
                    bool steep = false;
                    for (int j = 0; j < ny && !steep; j++) for (int i = 0; i < nx; i++)
                        {
                            double v = s[j * nx + i];
                            if (i + 1 < nx && Math.Abs(s[j * nx + i + 1] - v) > lip * cell * 1.0001 || j + 1 < ny && Math.Abs(s[(j + 1) * nx + i] - v) > lip * cell * 1.0001) { steep = true; break; }
                        }
                    if (steep) s = Envelope(s, nx, ny, lip * cell);
                    for (int pass = 0; pass < 2 && blur > 0; pass++) s = Blur(s, nx, ny, blur);
                    for (int pass = 0; pass < 60 && Curve(s, nx, ny, cell) * span * 180 / Math.PI > turn; pass++) { s = Blur(s, nx, ny, Math.Max(1, blur)); softened = true; }
                    for (int k = 0; k < s.Length; k++) s[k] = Math.Max(sLo, Math.Min(sHi, s[k]));
                }
                var mm = new ShapeMap { ZBase = m.ZBase, ZFlat = m.ZFlat, ZKeep = m.ZKeep, ZTop = m.ZTop, S = new ShapeField.Spline2(gx, gy, cell, nx, ny, s, true), SMin = double.MaxValue, SMax = 0 };
                return mm;
            };
            Cancel.Check();
            var map = make(1);
            if (o.Material > 0)
            {
                var refs = new Refs(tp);
                double want = o.Material / 100.0, lo = 0.1, hi = 8;
                Func<double, double> share = c => { Cancel.Check(); return Share(tp, make(c), refs); };
                double sl = share(lo), sh = share(hi);
                if (want <= sl) { r.Scale = lo; r.Warnings.Add(Inv.F($"{o.Material:0} % of the material is not possible: with every layer at its smallest height it is {100 * sl:0} %.")); }
                else if (want >= sh) { r.Scale = hi; r.Warnings.Add(Inv.F($"{o.Material:0} % of the material is not possible: with every layer at its largest height it is {100 * sh:0} %.")); }
                else
                {
                    double a = lo, b = hi;
                    for (int k = 0; k < 40; k++)
                    {
                        double c = k < 3 ? 0.5 * (a + b) : Math.Sqrt(a * b), v = share(c);
                        r.Scale = c;
                        if (Math.Abs(v - want) < 0.0003) break;
                        if (v > want) b = c; else a = c;
                    }
                }
                map = make(r.Scale);
            }
            double over = 0, under = 0; int cntAll = 0;
            foreach (var p in Places(tp))
            {
                double s = map.Factor(p.X, p.Y);
                if (s < map.SMin) map.SMin = s; if (s > map.SMax) map.SMax = s;
                double want = (m.ZBase + r.Scale * field.Factor(p.X, p.Y, H) * H - (m.ZTop - m.ZKeep) - m.ZFlat) / span;
                cntAll++; if (s < want - 0.03) under++; if (s > want + 0.03) over++;
            }
            if (map.SMin < 0.02) { r.Refused = Inv.F($"At the thinnest place the layers would be pressed to {100 * map.SMin:0.#} % of their height. The field asks for a height at or below the flat layers."); return null; }
            r.HeightAfterMin = m.ZFlat - m.ZBase + map.SMin * span + (m.ZTop - m.ZKeep); r.HeightAfterMax = m.ZFlat - m.ZBase + map.SMax * span + (m.ZTop - m.ZKeep);
            if (softened) r.Report.Add(Inv.F($"The top is rounded more than asked for, so that the tool turns at most about {0.7 * 30:0} degrees per second at {st.Speed / 60:0} mm/s (a slower print speed allows a sharper shape)."));
            string limits = Inv.F($"layer height {minL:0.##} to {maxL:0.##} mm{(o.ExtraPasses ? " per pass" : "")}, slope up to {slope:0} degrees, rounded over {smooth:0} mm");
            r.Cut = o.Limit && under + over > 0.02 * cntAll;
            if (r.Cut)
                r.Warnings.Add(Inv.F($"The limits ({limits}) cut the wanted shape: wanted {wMin * r.Scale * H:0.#} to {wMax * r.Scale * H:0.#} mm high ({wMax / Math.Max(1e-9, wMin):0.0} : 1), possible {r.HeightAfterMin:0.#} to {r.HeightAfterMax:0.#} mm ({r.HeightAfterMax / r.HeightAfterMin:0.0} : 1). {100.0 * under / cntAll:0} % of the part is lower than wanted, {100.0 * over / cntAll:0} % higher."));
            return map;
        }

        /// <summary>Any function of x and y over the part, kept between lo and hi and rounded over about `smooth` mm.</summary>
        internal static ShapeField.Spline2 Smoothed(Toolpath tp, Func<double, double, double> f, double lo, double hi, double smooth)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var p in Places(tp)) { if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X; if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y; }
            double size = Math.Max(x1 - x0, y1 - y0), cell = Math.Max(Math.Max(0.5, tp.Settings.BeadWidth), size / 70);
            int nx = Math.Max(4, (int)Math.Ceiling((x1 - x0) / cell - 1e-6) + 1), ny = Math.Max(4, (int)Math.Ceiling((y1 - y0) / cell - 1e-6) + 1);
            double gx = 0.5 * (x0 + x1) - 0.5 * cell * (nx - 1), gy = 0.5 * (y0 + y1) - 0.5 * cell * (ny - 1); var s = new double[nx * ny];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) s[j * nx + i] = Math.Max(lo, Math.Min(hi, f(gx + i * cell, gy + j * cell)));
            int blur = (int)Math.Round(smooth / cell / 2);
            for (int pass = 0; pass < 2 && blur > 0; pass++) s = Blur(s, nx, ny, blur);
            for (int k = 0; k < s.Length; k++) s[k] = Math.Max(lo, Math.Min(hi, s[k]));
            return new ShapeField.Spline2(gx, gy, cell, nx, ny, s, true);
        }

        /// <summary>The places the part covers in plan: its points, and points every 20 mm on long moves.</summary>
        internal static IEnumerable<Point3d> Places(Toolpath tp)
        {
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Part(tp, i)) continue;
                Point3d a = tp.Points[i - 1], b = tp.Points[i]; double l = a.DistanceTo(b); int k = Math.Max(1, Math.Min(500, (int)(l / 20)));
                for (int j = 1; j <= k; j++) yield return j == k ? b : a + (b - a) * (j / (double)k);
            }
        }

        /// <summary>The steepest slope a vertical nozzle can follow: at a corner the edge of its tip must not reach further into the bead beside it than half a layer.</summary>
        public static double AutoSlope(PrintSettings st, bool tilt, NonPlanarCheck.Head head)
        {
            if (tilt) return 30;
            double tip = (head ?? new NonPlanarCheck.Head()).TipRadius + 0.5 * st.BeadWidth;
            return Math.Max(5, Math.Min(20, Math.Atan(0.5 * st.LayerHeight / Math.Max(0.5, tip)) * 180 / Math.PI));
        }

        /// <summary>Smallest field that is nowhere below s and nowhere steeper than k per cell.</summary>
        internal static double[] Envelope(double[] s, int nx, int ny, double k)
        {
            var r = new double[s.Length];
            double min = s.Min(), max = s.Max();
            int reach = k > 1e-12 ? (int)Math.Min(nx + ny, Math.Ceiling((max - min) / k)) : nx + ny;
            var dist = new double[(reach + 1) * (reach + 1)];
            for (int b = 0; b <= reach; b++) for (int a = 0; a <= reach; a++) dist[b * (reach + 1) + a] = k * Math.Sqrt(a * a + b * b);
            for (int j = 0; j < ny; j++)
            {
                Cancel.Check();
                for (int i = 0; i < nx; i++)
                {
                    double best = s[j * nx + i];
                    for (int y = Math.Max(0, j - reach); y <= Math.Min(ny - 1, j + reach); y++)
                    {
                        int row = Math.Abs(y - j) * (reach + 1);
                        for (int x = Math.Max(0, i - reach); x <= Math.Min(nx - 1, i + reach); x++)
                        {
                            double v = s[y * nx + x];
                            if (v <= best) continue;
                            v -= dist[row + Math.Abs(x - i)];
                            if (v > best) best = v;
                        }
                    }
                    r[j * nx + i] = best;
                }
            }
            return r;
        }

        /// <summary>Largest second difference: how fast the slope changes, per mm.</summary>
        internal static double Curve(double[] s, int nx, int ny, double cell)
        {
            double m = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double v = s[j * nx + i];
                    if (i > 0 && i + 1 < nx) m = Math.Max(m, Math.Abs(s[j * nx + i - 1] - 2 * v + s[j * nx + i + 1]));
                    if (j > 0 && j + 1 < ny) m = Math.Max(m, Math.Abs(s[(j - 1) * nx + i] - 2 * v + s[(j + 1) * nx + i]));
                    if (i + 1 < nx && j + 1 < ny) m = Math.Max(m, Math.Abs(v - s[j * nx + i + 1] - s[(j + 1) * nx + i] + s[(j + 1) * nx + i + 1]));
                }
            return m / (cell * cell);
        }

        /// <summary>Mean over 2 n + 1 cells in both directions; a straight slope stays as it is, also at the edges.</summary>
        internal static double[] Blur(double[] s, int nx, int ny, int n)
        {
            double[] Line(double[] v)
            {
                int c = v.Length; var o = new double[c];
                double At(int i) { if (i < 0) return 2 * v[0] - v[Math.Min(c - 1, -i)]; if (i >= c) return 2 * v[c - 1] - v[Math.Max(0, 2 * (c - 1) - i)]; return v[i]; }
                int m = Math.Min(n, c - 1);
                for (int i = 0; i < c; i++) { double t = 0; for (int k = -m; k <= m; k++) t += At(i + k); o[i] = t / (2 * m + 1); }
                return o;
            }
            var w = new double[s.Length];
            for (int j = 0; j < ny; j++) { var l = new double[nx]; Array.Copy(s, j * nx, l, 0, nx); Array.Copy(Line(l), 0, w, j * nx, nx); }
            for (int i = 0; i < nx; i++) { var l = new double[ny]; for (int j = 0; j < ny; j++) l[j] = w[j * nx + i]; l = Line(l); for (int j = 0; j < ny; j++) w[j * nx + i] = l[j]; }
            return w;
        }

        // ------------------------------------------------------------------ distance to the lap below

        /// <summary>For every point of the old path: what it lies on (closest point of the lap below).</summary>
        internal class Refs
        {
            public readonly LapBelow Laps; public readonly Toolpath Tp; private readonly double _up, _skin;
            public readonly Point3d[] Q; public readonly double[] Law; // Law 0 = not trusted: column
            public int Found, Column;
            public Refs(Toolpath tp)
            {
                Tp = tp; Laps = new LapBelow(tp); _skin = tp.Settings.EffectiveSkin; _up = 0.25 * tp.Settings.LayerHeight;
                Q = new Point3d[tp.Count]; Law = new double[tp.Count];
                for (int i = 1; i < tp.Count; i++)
                {
                    if (!Part(tp, i)) continue;
                    if ((i & 4095) == 0) Cancel.Check();
                    Law[i] = At(tp.Points[i], tp.Layers[i], Laps.Along[i], i, tp.Heights[i], tp.Widths[i], tp.Normals[i], out Q[i]);
                    if (Law[i] > 0) Found++; else Column++;
                }
            }
            /// <summary>The height by the bead law above the lap below; 0 when it does not fit the height the path was made with.</summary>
            public double At(Point3d p, int layer, double along, int move, double used, double w, Vector3d n, out Point3d q)
            {
                q = p;
                double hv = n.Z > 0.2 ? used / n.Z : used;
                if (used < 0.15 * Tp.Settings.LayerHeight || !Laps.Find(p, layer, _up, along, move, out q)) return 0;
                double law = BeadLaw.Height(p, q, w, _skin);
                return Math.Abs(law - hv) <= 0.15 * hv && p.Z > q.Z + 0.02 ? law : 0;
            }
            /// <summary>How much more (or less) high the bead at p becomes: new distance to the lap below / old one.</summary>
            /// The layer may slope now, and the lap below may lie a little to the side: the gap is measured square to the layer and
            /// counted as a height straight up, which is what a wall of constant thickness needs.
            public double Ratio(ShapeMap m, Point3d p, double law, Point3d q, double used, double w, Vector3d n, bool tilt, Vector3d nNew, out bool folds)
            {
                folds = false;
                if (p.Z <= m.ZFlat) return 1;
                if (law > 0)
                {
                    double dz = m.Z(p.X, p.Y, p.Z) - m.Z(q.X, q.Y, q.Z), dx = p.X - q.X, dy = p.Y - q.Y;
                    double raw = dz;
                    if (nNew.Z > 0.2) dz += (nNew.X * dx + nNew.Y * dy) / nNew.Z;
                    double gap = tilt ? dz : raw;
                    if (gap < 0.25 * (p.Z - q.Z) && gap < 0.1 * law) folds = true;
                    double skin = Math.Sqrt(dx * dx + dy * dy) * _skin / Math.Max(1e-9, w);
                    return Math.Max(0.02, Math.Max(dz, skin) / law);
                }
                double hv = Math.Max(0.02, n.Z > 0.2 ? used / n.Z : used);
                return (m.Z(p.X, p.Y, p.Z) - m.Z(p.X, p.Y, p.Z - hv)) / hv;
            }
        }

        internal static double Section(PrintSettings st, double w, double h) => (st.Machine ?? new MachineProfile()).Section(w, h);

        /// <summary>Material after / material before, quickly (for the search). Long moves are looked at in steps.</summary>
        private static double Share(Toolpath tp, ShapeMap m, Refs refs)
        {
            double a = 0, b = 0, last = 1; int lastI = -2;
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Part(tp, i)) continue;
                double R = At(i, tp.Points[i]);
                double prev = lastI == i - 1 ? last : R;
                double h0 = tp.Heights[i - 1], h1 = tp.Heights[i], w = 0.5 * (tp.Widths[i] + tp.Widths[i - 1]), len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                int k = Math.Max(1, (int)Math.Ceiling(len / 20)); double r0 = prev;
                for (int j = 1; j <= k; j++)
                {
                    double u = j / (double)k, r1 = j == k ? R : Column(tp.Points[i - 1] + (tp.Points[i] - tp.Points[i - 1]) * u, h0 + (h1 - h0) * u, tp.Normals[i]);
                    double h = h0 + (h1 - h0) * (j - 0.5) / k;
                    a += Section(tp.Settings, w, h) * len / k * tp.Flows[i]; b += Section(tp.Settings, w, h * 0.5 * (r0 + r1)) * len / k * tp.Flows[i];
                    r0 = r1;
                }
                last = R; lastI = i;
            }
            return a > 0 ? b / a : 1;

            double At(int i, Point3d p)
            {
                if (refs.Law[i] <= 0 || p.Z <= m.ZFlat) return Column(p, tp.Heights[i], tp.Normals[i]);
                m.Z(p.X, p.Y, p.Z, out double kx, out double ky, out double kz);
                return refs.Ratio(m, p, refs.Law[i], refs.Q[i], tp.Heights[i], tp.Widths[i], tp.Normals[i], true, ShapeMap.Normal(tp.Normals[i], kx, ky, kz), out _);
            }
            double Column(Point3d p, double used, Vector3d n)
            {
                if (p.Z <= m.ZFlat) return 1;
                double hv = Math.Max(0.02, n.Z > 0.2 ? used / n.Z : used);
                return (m.Z(p.X, p.Y, p.Z) - m.Z(p.X, p.Y, p.Z - hv)) / hv;
            }
        }

        // ------------------------------------------------------------------ the path

        private struct Sample { public Point3d P; public Vector3d Tool, Layer; public double H, R, W; }

        public static Result Apply(Toolpath tp, ShapeField field, ReshapeOptions o)
        {
            o = o ?? new ReshapeOptions();
            var r = new Result();
            if (tp == null || tp.Count < 3) { r.Refused = "Empty toolpath."; return Verdict(r, o); }
            var map = Fit(tp, field ?? ShapeField.Constant(1), o, r);
            if (map == null || r.Refused != null) return Verdict(r, o);
            r.Map = map;
            int n = tp.Count; var st = tp.Settings;
            if (r.Identity)
            {
                r.Path = tp.Duplicate();
                for (int i = 0; i < n; i++) { r.Source.Add(i); r.Weight.Add(1); r.Inserted.Add(false); }
                r.HeightAfterMin = r.HeightAfterMax = r.HeightBefore;
                Numbers(r, tp, o);
                return Verdict(r, o);
            }
            var refs = new Refs(tp);
            r.RefPoints = refs.Found; r.RefByColumn = refs.Column;
            var outp = new Toolpath { Settings = st, Warp = new ReshapeWarp(map, tp.Warp, o.Tilt) };
            if (o.MaxFlow != 0)
            {
                outp.Settings = st.Duplicate();
                double before = PathEngine.Process(tp, false).MaxVolumeRate;
                double cap = o.MaxFlow > 0 ? o.MaxFlow : before;
                outp.Settings.MaxVolumeRate = st.MaxVolumeRate > 0 ? Math.Min(st.MaxVolumeRate, cap) : cap;
            }
            int firstPrint = -1, lastPrint = -1;
            for (int i = 1; i < n; i++) if (LapBelow.Prints(tp, i)) { if (firstPrint < 0) firstPrint = i; lastPrint = i; }
            r.LastPrint = lastPrint;
            int folds = 0; Point3d foldAt = Point3d.Origin;
            double zMove = map.ZFlat + 1e-9;

            // a place on the old path, bent
            Sample Place(Point3d p, Vector3d n0, double h, double w, int layer, double along, int move, bool print, double law, Point3d q, bool known)
            {
                var s = new Sample { P = p, Tool = n0, Layer = n0, H = h, R = 1, W = w };
                if (p.Z <= zMove) return s;
                double z = map.Z(p.X, p.Y, p.Z, out double kx, out double ky, out double kz);
                s.P = new Point3d(p.X, p.Y, z);
                s.Layer = ShapeMap.Normal(n0, kx, ky, kz);
                s.Tool = o.Tilt ? s.Layer : Vector3d.ZAxis;
                if (!print) return s;
                if (!known) law = refs.At(p, layer, along, move, h, w, n0, out q);
                s.R = refs.Ratio(map, p, law, q, h, w, n0, o.Tilt, s.Layer, out bool fold);
                if (fold) { if (folds++ == 0) foldAt = p; }
                s.H = h * s.R * s.Layer.Z / Math.Max(0.1, n0.Z);
                return s;
            }
            Sample End(int i) => Place(tp.Points[i], tp.Normals[i], tp.Heights[i], tp.Widths[i], tp.Layers[i], refs.Laps.Along[i], i, Part(tp, i) || i + 1 < n && Part(tp, i + 1), refs.Law[i], refs.Q[i], Part(tp, i));
            bool Stays(int i) => i < firstPrint || tp.Roles[i] == PathRole.Skirt || i > lastPrint;

            void Put(Sample s, int src, double weight, double flow, bool inserted)
            {
                outp.Add(s.P, s.Tool, s.H, s.W, flow, tp.Roles[src], tp.Layers[src]);
                r.Source.Add(src); r.Weight.Add(weight); r.Inserted.Add(inserted);
            }
            Sample prev = default(Sample);
            for (int i = 0; i <= lastPrint; i++)
            {
                if ((i & 1023) == 0) Cancel.Check();
                Sample cur = Stays(i) ? new Sample { P = tp.Points[i], Tool = tp.Normals[i], Layer = tp.Normals[i], H = tp.Heights[i], R = 1, W = tp.Widths[i] } : End(i);
                if (i > 0 && r.FirstMoved < 0 && cur.P.Z != tp.Points[i].Z) r.FirstMoved = i;
                if (i == 0 || Stays(i) || tp.Points[i].Z <= zMove && tp.Points[i - 1].Z <= zMove) { Put(cur, i, 1, tp.Flows[i], false); prev = cur; continue; }
                bool print = Part(tp, i);
                Point3d a = tp.Points[i - 1], b = tp.Points[i]; Vector3d na = tp.Normals[i - 1], nb = tp.Normals[i];
                double len = a.DistanceTo(b);
                Sample Mid(double u)
                {
                    Vector3d nn = na == nb ? na : na + (nb - na) * u; double l = Len(nn); if (l > 1e-9) nn = new Vector3d(nn.X / l, nn.Y / l, nn.Z / l); else nn = Vector3d.ZAxis;
                    return Place(a + (b - a) * u, nn, tp.Heights[i - 1] + (tp.Heights[i] - tp.Heights[i - 1]) * u, tp.Widths[i - 1] + (tp.Widths[i] - tp.Widths[i - 1]) * u, tp.Layers[i], refs.Laps.Along[i - 1] + (refs.Laps.Along[i] - refs.Laps.Along[i - 1]) * u, i, print, 0, Point3d.Origin, false);
                }
                // where a straight move would leave the new shape, points are put in
                var pieces = new List<KeyValuePair<double, Sample>>();
                double shortest = Math.Max(1.0, 0.4 * st.BeadWidth);
                void Split(double u0, Sample s0, double u1, Sample s1, int depth)
                {
                    double l = (u1 - u0) * len;
                    if (depth < 14 && l > 2 * shortest)
                    {
                        int k = Math.Max(3, Math.Min(24, (int)(l / 20))); bool off = false;
                        for (int j = 1; j <= k && !off; j++)
                        {
                            double t = j / (k + 1.0), u = u0 + (u1 - u0) * t;
                            var sm = Mid(u);
                            double zl = s0.P.Z + (s1.P.Z - s0.P.Z) * t, hl = s0.H + (s1.H - s0.H) * t;
                            Vector3d nl = s0.Tool + (s1.Tool - s0.Tool) * t;
                            double dn = Len(sm.Tool - nl);
                            off = Math.Abs(sm.P.Z - zl) > 0.03 || dn > 0.006 || print && Math.Abs(sm.H - hl) > 0.01 * Math.Max(0.5, sm.H) && Math.Abs(sm.H - hl) > 0.01;
                        }
                        if (off)
                        {
                            double um = 0.5 * (u0 + u1); var sm = Mid(um);
                            Split(u0, s0, um, sm, depth + 1);
                            Split(um, sm, u1, s1, depth + 1);
                            return;
                        }
                    }
                    pieces.Add(new KeyValuePair<double, Sample>(u1, s1));
                }
                // the edge of the flat layers is a kink: a point exactly there
                double uk = double.NaN;
                if (a.Z < zMove != b.Z < zMove && Math.Abs(b.Z - a.Z) > 1e-6) { uk = (map.ZFlat - a.Z) / (b.Z - a.Z); if (uk * len < shortest || (1 - uk) * len < shortest) uk = double.NaN; }
                if (!double.IsNaN(uk)) { var sk = Mid(uk); Split(0, prev, uk, sk, 0); Split(uk, sk, 1, cur, 0); }
                else Split(0, prev, 1, cur, 0);
                double u0b = 0; Sample last = prev; double hOld = tp.Heights[i - 1];
                foreach (var pc in pieces)
                {
                    double u = pc.Key; Sample s = pc.Value; bool inserted = u < 1 - 1e-12;
                    double hNew1 = tp.Heights[i - 1] + (tp.Heights[i] - tp.Heights[i - 1]) * u;
                    double share = u - u0b, flow = tp.Flows[i], weight = share;
                    if (print)
                    {
                        // material of this piece: the old one x the new distance to the lap below / the old one
                        double wa = 0.5 * (last.W + s.W), ho = 0.5 * (hOld + hNew1), want = 0.5 * (hOld * last.R + hNew1 * s.R);
                        double lOld = share * len, lNew = last.P.DistanceTo(s.P), have = 0.5 * (last.H + s.H) * lNew;
                        // the beads lie along and across the slope; w x height x length must be what the wall needs
                        double k = have > 1e-9 && lOld > 1e-9 ? want * lOld / have : 1;
                        if (Math.Abs(k - 1) > 1e-7) flow *= Math.Max(0.5, Math.Min(2, k));
                        weight = ho > 1e-6 ? share * (want / ho) : share;
                        if (st.Machine != null && !st.Machine.RectangleSection && ho > 1e-6) weight = share * Section(st, wa, want) / Math.Max(1e-12, Section(st, wa, ho));
                    }
                    Put(s, i, weight, flow, inserted);
                    last = s; u0b = u; hOld = hNew1;
                }
                r.Added += pieces.Count - 1;
                prev = cur;
            }
            if (folds > 0)
            {
                r.Refused = Inv.F($"The shape folds for a {(o.Tilt ? "tilted" : "vertical")} tool: at {folds} point(s), first at x {foldAt.X:0}, y {foldAt.Y:0}, z {foldAt.Z:0}, a lap would lie on or below the lap it is printed on. {(o.Tilt ? "Make the slope smaller (Max Slope) or let the roof out." : "Tilt the tool (robot), or make the slope smaller.")}");
                return Verdict(r, o);
            }
            if (o.ExtraPasses) outp = Passes(outp, o, map, st, r);
            else CountThick(outp, o, map, st, r);
            r.Path = outp;
            WayOut(tp, r, lastPrint, o);
            Numbers(r, tp, o);
            return Verdict(r, o);
        }

        // ------------------------------------------------------------------ extra passes

        private static void CountThick(Toolpath p, ReshapeOptions o, ShapeMap m, PrintSettings st, Result r)
        {
            double maxL = o.MaxLayer > 0 ? o.MaxLayer : AutoMax(st);
            for (int i = 1; i < p.Count; i++) if (Part(p, i) && p.Points[i].Z > m.ZFlat && p.Heights[i] > 1.05 * maxL) r.ThickLeft++;
        }

        /// <summary>
        /// A closed spiral cannot get a lap that ends somewhere, but it can go over a stretch three times: forth,
        /// back, forth again, each pass a third of the height. The passes thin out to nothing where the line turns,
        /// so nothing stands up and nothing is left open; the line stays one line and the plan stays as it was.
        /// </summary>
        private static Toolpath Passes(Toolpath p, ReshapeOptions o, ShapeMap m, PrintSettings st, Result r)
        {
            double maxL = o.MaxLayer > 0 ? o.MaxLayer : AutoMax(st), minL = o.MinLayer > 0 ? o.MinLayer : AutoMin(st);
            // a run reaches a little beyond the places that are too thick, so that its ramps lie where one pass is thin enough
            double need = Math.Max(maxL, 2.7 * minL) * 1.01, from = Math.Max(0.9 * maxL, 2.7 * minL);
            int n = p.Count;
            bool[] crowded = Crowded(p, o.Head, st);
            var res = new Toolpath { Settings = p.Settings, Warp = p.Warp };
            var src = new List<int>(); var wgt = new List<double>(); var ins = new List<bool>();
            void Copy(int i) { res.AddFrom(p, i); src.Add(r.Source[i]); wgt.Add(r.Weight[i]); ins.Add(r.Inserted[i]); }
            bool Thick(int i) => Part(p, i) && p.Roles[i] != PathRole.Seam && p.Points[i].Z > m.ZFlat && p.Heights[i] > from && p.Heights[i - 1] > from && !crowded[i] && !crowded[i - 1];
            Copy(0);
            int k = 1;
            while (k < n)
            {
                if (!Thick(k)) { if (Part(p, k) && p.Points[k].Z > m.ZFlat && p.Heights[k] > 1.05 * maxL) r.ThickLeft++; Copy(k); k++; continue; }
                int e = k; double tMax = p.Heights[k];
                while (e + 1 < n && Thick(e + 1) && p.Layers[e + 1] == p.Layers[k] && !(o.BreakBefore != null && o.BreakBefore[r.Source[e + 1]] && r.Source[e + 1] != r.Source[e])) { e++; tMax = Math.Max(tMax, p.Heights[e]); }
                // the run: points k-1 .. e
                double L = 0; for (int i = k; i <= e; i++) L += p.Points[i].DistanceTo(p.Points[i - 1]);
                int N = (int)Math.Ceiling(tMax / maxL - 1e-6); if (N < 3) N = 3; if (N % 2 == 0) N++;
                double ramp = Math.Max(Math.Max(20, 4 * st.BeadWidth), 6 * tMax);
                double stagger = (p.Layers[k] % 4) * 0.5 * ramp; // the ends lie elsewhere on the next layers
                // where it is really too thick: from sa to sb along the run
                double sa = -1, sb = -1; { double at0 = 0; for (int i = k; i <= e; i++) { double l = p.Points[i].DistanceTo(p.Points[i - 1]), h0 = p.Heights[i - 1], h1 = p.Heights[i]; if (Math.Max(h0, h1) > need) { double t0 = h0 > need || h1 <= h0 ? 0 : (need - h0) / (h1 - h0), t1 = h1 > need || h0 <= h1 ? 1 : (h0 - need) / (h0 - h1); if (sa < 0) sa = at0 + t0 * l; sb = at0 + t1 * l; } at0 += l; } }
                if (sa < 0) { for (int i = k; i <= e; i++) Copy(i); k = e + 1; continue; }
                double A = Math.Max(0, sa - stagger), B = Math.Min(L, sb + stagger);
                if (B - A < 4 * ramp) { double c = 0.5 * (A + B); A = Math.Max(0, c - 2 * ramp); B = Math.Min(L, c + 2 * ramp); }
                if (B - A < 3.5 * ramp) { for (int i = k; i <= e; i++) { if (p.Heights[i] > 1.05 * maxL) r.ThickLeft++; Copy(i); } k = e + 1; continue; }
                ramp = Math.Min(ramp, (B - A) / 4);
                r.PassRuns++;
                // the run as a list of places with the way along it; the ends of the ramps become places too
                var S = new List<double>(); var pt = new List<int>(); var us = new List<double>(); // place: on move pt at share us
                double at = 0; var cuts = new List<double> { A, A + ramp, B - ramp, B };
                S.Add(0); pt.Add(k); us.Add(0);
                for (int i = k; i <= e; i++)
                {
                    double l = p.Points[i].DistanceTo(p.Points[i - 1]);
                    foreach (double c in cuts) if (c > at + 0.3 && c < at + l - 0.3) { S.Add(c); pt.Add(i); us.Add((c - at) / l); }
                    at += l; S.Add(at); pt.Add(i); us.Add(1);
                }
                // three points within 0.3 mm of an end of a ramp count as the end
                for (int j = 0; j < S.Count; j++) foreach (double c in cuts) if (Math.Abs(S[j] - c) <= 0.31) S[j] = c;
                double Frac(int pass, double s) // top of the pass as a share of the layer
                {
                    if (pass == N || s <= A) return 1;
                    double c = (double)pass / N;
                    if (s < A + ramp) return 1 + (c - 1) * (s - A) / ramp;
                    if (s > B - ramp) return c * Math.Max(0, B - s) / ramp;
                    return c;
                }
                double Z0(int j, out Point3d top, out Vector3d nrm, out double T, out double W)
                {
                    int i = pt[j]; double u = us[j];
                    top = p.Points[i - 1] + (p.Points[i] - p.Points[i - 1]) * u;
                    nrm = p.Normals[i - 1] + (p.Normals[i] - p.Normals[i - 1]) * u; double l = Len(nrm); if (l > 1e-9) nrm = new Vector3d(nrm.X / l, nrm.Y / l, nrm.Z / l);
                    T = p.Heights[i - 1] + (p.Heights[i] - p.Heights[i - 1]) * u; W = p.Widths[i - 1] + (p.Widths[i] - p.Widths[i - 1]) * u;
                    // the height of the layer straight up: T is measured square to it
                    double nz = o.Tilt ? nrm.Z : LayerZ(m, top);
                    return T / Math.Max(0.3, nz);
                }
                int jA = S.FindIndex(s => s >= A - 1e-6), jB = S.FindLastIndex(s => s <= B + 1e-6);
                void Go(int j, int jPrev, int pass)
                {
                    double dz = Z0(j, out Point3d top, out Vector3d nrm, out double T, out double W);
                    double c = Frac(pass, S[j]), below = pass == 1 ? 0 : Frac(pass - 1, S[j]);
                    int mv = pt[Math.Max(j, jPrev)]; // the move of the old path between the two places
                    res.Add(new Point3d(top.X, top.Y, top.Z - (1 - c) * dz), nrm, T * Math.Max(0, c - below), W, p.Flows[mv], p.Roles[mv], p.Layers[mv]);
                    double c0 = Frac(pass, S[jPrev]), b0 = pass == 1 ? 0 : Frac(pass - 1, S[jPrev]);
                    double lm = p.Points[mv].DistanceTo(p.Points[mv - 1]);
                    double part = lm > 1e-9 ? r.Weight[mv] * Math.Abs(S[j] - S[jPrev]) / lm * 0.5 * ((c - below) + (c0 - b0)) : 0;
                    src.Add(r.Source[mv]); wgt.Add(part); ins.Add(!(us[j] >= 1 - 1e-12 && pass == N && !r.Inserted[pt[j]]));
                }
                // before the first ramp: one pass, as it was (pass N = full height)
                void Full(int j)
                {
                    Z0(j, out Point3d top, out Vector3d nrm, out double T, out double W); int mv = pt[j];
                    res.Add(top, nrm, T, W, p.Flows[mv], p.Roles[mv], p.Layers[mv]);
                    double lm = p.Points[mv].DistanceTo(p.Points[mv - 1]);
                    src.Add(r.Source[mv]); wgt.Add(lm > 1e-9 ? r.Weight[mv] * (S[j] - S[j - 1]) / lm : 0); ins.Add(!(us[j] >= 1 - 1e-12 && !r.Inserted[pt[j]]));
                }
                for (int j = 1; j <= jA; j++) Full(j);
                for (int pass = 1; pass <= N; pass++)
                {
                    if (pass % 2 == 1) for (int j = jA + 1; j <= jB; j++) Go(j, j - 1, pass);
                    else for (int j = jB - 1; j >= jA; j--) Go(j, j + 1, pass);
                }
                for (int j = jB + 1; j < S.Count; j++) Full(j);
                k = e + 1;
            }
            r.Source = src; r.Weight = wgt; r.Inserted = ins;
            if (r.PassRuns > 0) r.Report.Add(Inv.F($"Extra passes: {r.PassRuns} stretch(es) are printed in 3 or 5 passes (forth, back, forth), each at most {maxL:0.##} mm high; the passes thin out where the line turns."));
            if (r.ThickLeft > 0) r.Report.Add(Inv.F($"Extra passes: {r.ThickLeft} point(s) lie on stretches too short for passes, or with another bead right beside them (infill: the nozzle would hit it), and stay thick."));
            return res;
        }

        /// <summary>
        /// Places with another bead of the same layer beside them (infill, hairpins, a second wall): the lower first pass would
        /// drive the nozzle into it, so these are never printed in passes.
        /// </summary>
        private static bool[] Crowded(Toolpath p, NonPlanarCheck.Head head, PrintSettings st)
        {
            int n = p.Count; var r = new bool[n]; double reach = (head ?? new NonPlanarCheck.Head()).TipRadius + st.BeadWidth + 1, back = 6 * reach;
            var along = new double[n]; for (int i = 1; i < n; i++) along[i] = along[i - 1] + p.Points[i].DistanceTo(p.Points[i - 1]);
            int a = 0;
            while (a < n)
            {
                int b = a; while (b + 1 < n && p.Layers[b + 1] == p.Layers[a]) b++;
                var g = new Dictionary<long, List<double[]>>(); // x, y, along, in cells
                for (int i = Math.Max(1, a); i <= b; i++)
                {
                    if (!Part(p, i)) continue;
                    double l = along[i] - along[i - 1]; int k = Math.Max(1, (int)Math.Ceiling(l / (0.5 * reach)));
                    for (int j = 1; j <= k; j++)
                    {
                        Point3d q = p.Points[i - 1] + (p.Points[i] - p.Points[i - 1]) * (j / (double)k);
                        long key = ((long)(int)Math.Floor(q.X / reach) << 32) ^ (uint)(int)Math.Floor(q.Y / reach);
                        if (!g.TryGetValue(key, out var c)) g[key] = c = new List<double[]>();
                        c.Add(new[] { q.X, q.Y, along[i - 1] + l * j / k, i });
                    }
                }
                foreach (var kv in g)
                    foreach (var u in kv.Value)
                    {
                        int cx = (int)Math.Floor(u[0] / reach), cy = (int)Math.Floor(u[1] / reach); bool hit = false;
                        for (int dx = -1; dx <= 1 && !hit; dx++) for (int dy = -1; dy <= 1 && !hit; dy++)
                            {
                                if (!g.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cy + dy), out var c)) continue;
                                foreach (var v in c) if (Math.Abs(v[2] - u[2]) > back && (v[0] - u[0]) * (v[0] - u[0]) + (v[1] - u[1]) * (v[1] - u[1]) < reach * reach) { hit = true; break; }
                            }
                        if (hit) { r[(int)u[3]] = true; r[(int)u[3] - 1] = true; }
                    }
                a = b + 1;
            }
            return r;
        }

        private static double LayerZ(ShapeMap m, Point3d pNew)
        {
            var q = m.Back(pNew);
            m.Z(q.X, q.Y, q.Z, out double kx, out double ky, out double kz);
            return ShapeMap.Normal(Vector3d.ZAxis, kx, ky, kz).Z;
        }

        // ------------------------------------------------------------------ way out

        /// <summary>The moves after the last bead start at the new end, keep clear of what stands higher, and lift as far above the top as before.</summary>
        internal static void WayOut(Toolpath tp, Result r, int lastPrint, ReshapeOptions o)
        {
            var outp = r.Path;
            int n = tp.Count;
            if (lastPrint + 1 >= n) return;
            Point3d e0 = tp.Points[lastPrint], e1 = outp.Points[outp.Count - 1]; Vector3d axis = outp.Normals[outp.Count - 1];
            double up = e1.Z - e0.Z, top = double.MinValue, top0 = double.MinValue;
            for (int i = 0; i < outp.Count; i++) if (LapBelow.Prints(outp, i)) top = Math.Max(top, outp.Points[i].Z);
            for (int i = 0; i < n; i++) if (LapBelow.Prints(tp, i)) top0 = Math.Max(top0, tp.Points[i].Z);
            var grid = new HeadGrid(o.Head, outp.Settings); double along = 0;
            for (int i = 1; i < outp.Count; i++) { along += outp.Points[i].DistanceTo(outp.Points[i - 1]); if (LapBelow.Prints(outp, i)) grid.Lay(outp.Points[i - 1], outp.Points[i], along, i); }
            int layer = outp.Layers[outp.Count - 1]; double w = outp.Settings.BeadWidth;
            Point3d from = e1;
            for (int i = lastPrint + 1; i < n; i++)
            {
                Point3d t = tp.Points[i];
                double z = t.Z <= top0 + 0.01 && t.Z <= e0.Z + 0.01 ? t.Z + up : Math.Max(t.Z + up, top + (t.Z - top0));
                Point3d to = new Point3d(t.X, t.Y, z);
                if (i == lastPrint + 1 && t.DistanceTo(e0) > 1)
                {
                    // does the head touch the part on the way off it? Then up first, only as far as needed
                    bool Free(Point3d a, Point3d b)
                    {
                        int k = Math.Max(2, (int)Math.Ceiling(a.DistanceTo(b) / 5));
                        for (int j = 1; j <= k; j++) if (grid.Depth(a + (b - a) * (j / (double)k), axis, double.MaxValue, 0.5 * outp.Settings.LayerHeight, out _) > o.Head.Graze) return false;
                        return true;
                    }
                    if (!Free(e1, to))
                    {
                        double lift = 0; bool ok = false;
                        while (lift < top - e1.Z + 5 && !ok) { lift += 5; ok = Free(new Point3d(e1.X, e1.Y, e1.Z + lift), new Point3d(to.X, to.Y, to.Z + lift)); }
                        Point3d over = new Point3d(e1.X, e1.Y, e1.Z + lift);
                        outp.Add(over, axis, tp.Heights[i], tp.Widths[i], 0, PathRole.Travel, layer);
                        r.Source.Add(i); r.Weight.Add(0); r.Inserted.Add(true);
                        to.Z += lift;
                        r.Report.Add(Inv.F($"Way out: the part stands higher beside the new end, so the nozzle lifts {lift:0} mm before it moves off."));
                    }
                }
                if (to.Z < from.Z && i > lastPrint + 1 && t.Z >= tp.Points[i - 1].Z - 1e-9) to.Z = from.Z;
                outp.Add(to, axis, tp.Heights[i], tp.Widths[i], tp.Flows[i], tp.Roles[i], tp.Layers[i]);
                r.Source.Add(i); r.Weight.Add(1); r.Inserted.Add(false);
                from = to;
            }
        }

        // ------------------------------------------------------------------ numbers and report

        /// <summary>E of the part (without skirt and travel), as the machine gets it.</summary>
        public static double PartE(Toolpath tp)
        {
            var pr = PathEngine.Process(tp, false); double e = 0;
            for (int i = 1; i < tp.Count; i++) if (Part(tp, i)) e += pr.Extrusion[i];
            return e;
        }

        internal static void Numbers(Result r, Toolpath tp, ReshapeOptions o)
        {
            r.EBefore = PartE(tp); r.EAfter = PartE(r.Path);
            r.WidthMin = double.MaxValue; r.WidthMax = 0; r.Travels = 0;
            int lp = -1, fp = -1; for (int i = 1; i < r.Path.Count; i++) if (Part(r.Path, i)) { r.WidthMin = Math.Min(r.WidthMin, r.Path.Widths[i]); r.WidthMax = Math.Max(r.WidthMax, r.Path.Widths[i]); lp = i; if (fp < 0) fp = i; }
            for (int i = fp + 1; i < lp; i++) if (!LapBelow.Prints(r.Path, i) && LapBelow.Prints(r.Path, i - 1)) r.Travels++;
            if (o.Check) r.Check = MultiAxisCheck.Run(r.Path, new MultiAxisCheck.Options { Head = o.Head, MinLayer = o.MinLayer, MaxLayer = o.MaxLayer });
            // what was wrong before is not the fault of the new shape
            if (r.Check != null && !r.Check.Ok && !r.Identity) { var b = MultiAxisCheck.Run(tp, new MultiAxisCheck.Options { Head = o.Head }); if (!b.Ok) r.BeforeNote = b.First; }
        }

        internal static Result Verdict(Result r, ReshapeOptions o)
        {
            if (r.Refused != null) { r.Path = null; r.Report.Insert(0, "REFUSED: " + r.Refused); return r; }
            var rep = new List<string>();
            string shape = r.Identity ? "the field is 1 everywhere, nothing changes" : Inv.F($"the part was {r.HeightBefore:0.#} mm high and becomes {r.HeightAfterMin:0.#} to {r.HeightAfterMax:0.#} mm ({r.HeightAfterMax / Math.Max(1e-9, r.HeightAfterMin):0.0} : 1) with {100 * r.MaterialShare:0.#} % of the material");
            bool ok = r.Check == null || r.Check.Ok;
            rep.Add((ok ? "OK: " : "CHECK: ") + shape + (ok ? "" : Inv.F($", but {r.Check.First}")) + (r.BeforeNote != null ? " (the path before had: " + r.BeforeNote + ")" : "") + (r.Cut ? ". The limits changed the wanted shape (see Warning)." : "."));
            if (!r.Identity)
            {
                rep.Add(Inv.F($"Layers: {r.Map.SMin:0.##} to {r.Map.SMax:0.##} times as high as before, between Z {r.Map.ZFlat:0.##} and {r.Map.ZKeep:0.##} mm. Below that everything is as it was (first moved point: {r.FirstMoved}); skirt and way in are untouched."));
                if (o.Material > 0) rep.Add(Inv.F($"Material: {o.Material:0.#} % wanted, {100 * r.MaterialShare:0.##} % reached (the field times {r.Scale:0.####})."));
                rep.Add(Inv.F($"Extrusion: worked out again from the distance to the lap below at {r.RefPoints + r.RefByColumn} points ({r.RefByColumn} of them, where no clear lap lies below, from the layer height straight down). {r.Added} points put in where a straight move would leave the new shape."));
                rep.Add(o.Tilt ? "Tool: square to the new layers." : "Tool: vertical.");
                if (r.ThickLeft > 0 && !o.ExtraPasses) rep.Add(Inv.F($"{r.ThickLeft} point(s) are thicker than Max Layer (Extra Passes splits them)."));
            }
            rep.AddRange(r.Report);
            foreach (var w in r.Warnings) rep.Add("Warning: " + w);
            if (r.Check != null) foreach (var l in r.Check.Report) rep.Add("Check: " + l);
            r.Report = rep;
            return r;
        }

        // ------------------------------------------------------------------ log

        public const string LogHeader = "date,part,step,variant,field,tool,height_before_mm,height_min_mm,height_max_mm,ratio,material_percent,scale,layer_min_mm,layer_max_mm,width_min_mm,width_max_mm,travel_moves,tilt_max_deg,tilt_rate_deg_per_mm,tilt_rate_deg_per_s,slope_max_deg,flow_max_mm3_s,collisions,volume_error_percent,minutes,grams,max_utilisation,mean_utilisation,share_over_target,verdict";

        /// <summary>One line of the experiment log.</summary>
        public static string LogLine(string part, int step, ShapeField f, Result r, ReshapeOptions o, DateTime when)
        {
            string T(string s) => "\"" + (s ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"";
            string N(double v, int d = 3) => double.IsNaN(v) || double.IsInfinity(v) ? "" : Inv.N(v, d);
            var c = r.Check; PathEngine.ProcessResult pr = r.Path != null ? PathEngine.Process(r.Path, false) : null;
            return string.Join(",", new[]
            {
                when.ToString("yyyy-MM-dd HH:mm:ss", Inv.C), T(part), step.ToString(Inv.C), T(ReshapeVariants.Short(r.Variant)), T(f?.Source), (r.Path != null ? r.Path.Normals.Any(n => n.Z < 0.9999) : o.Tilt) ? "tilted" : "vertical", N(r.HeightBefore), N(r.HeightAfterMin), N(r.HeightAfterMax),
                N(r.HeightAfterMin > 0 ? r.HeightAfterMax / r.HeightAfterMin : 0), N(100 * r.MaterialShare, 2), N(r.Scale, 4),
                N(c?.MinHeight ?? 0), N(c?.MaxHeight ?? 0), N(r.WidthMin, 2), N(r.WidthMax, 2), r.Travels.ToString(Inv.C), N(c?.MaxTilt ?? 0, 2), N(c?.MaxTiltPerMm ?? 0, 4), N(c?.MaxTiltPerSecond ?? 0, 2), N(c?.MaxSlope ?? 0, 2), N(c?.MaxFlow ?? 0, 1), (c?.Collisions.Count ?? 0).ToString(Inv.C), N(100 * (c?.VolumeError ?? 0), 2),
                N(pr?.Minutes ?? 0, 1), N(pr != null ? OutputFile.Grams(pr.Volume, r.Path.Settings) : 0, 0), N(f?.MaxUtilisation ?? 0), N(f?.MeanUtilisation ?? 0), N(f?.Over ?? 0), T(r.Refused != null ? "refused: " + r.Refused : c == null || c.Ok ? "OK" : c.First),
            });
        }

        /// <summary>One line per layer: length, heights, tilt, material, time.</summary>
        public static List<string> LayerCsv(Toolpath tp)
        {
            var l = new List<string> { "layer,z_min_mm,z_max_mm,length_mm,height_min_mm,height_mean_mm,height_max_mm,tilt_max_deg,e,seconds" };
            var pr = PathEngine.Process(tp, false);
            foreach (var g in Enumerable.Range(1, Math.Max(0, tp.Count - 1)).Where(i => Part(tp, i)).GroupBy(i => tp.Layers[i]).OrderBy(g => g.Key))
            {
                double len = g.Sum(i => tp.Points[i].DistanceTo(tp.Points[i - 1]));
                l.Add(string.Join(",", new[] { g.Key.ToString(Inv.C), Inv.N(g.Min(i => tp.Points[i].Z), 3), Inv.N(g.Max(i => tp.Points[i].Z), 3), Inv.N(len, 1), Inv.N(g.Min(i => tp.Heights[i]), 3), Inv.N(len > 0 ? g.Sum(i => tp.Heights[i] * tp.Points[i].DistanceTo(tp.Points[i - 1])) / len : 0, 3), Inv.N(g.Max(i => tp.Heights[i]), 3),
                    Inv.N(g.Max(i => Math.Acos(Math.Max(-1, Math.Min(1, tp.Normals[i].Z))) * 180 / Math.PI), 2), Inv.N(g.Sum(i => pr.Extrusion[i]), 3), Inv.N(g.Sum(i => pr.Speed[i] > 0 ? 60 * tp.Points[i].DistanceTo(tp.Points[i - 1]) / pr.Speed[i] : 0), 1) }));
            }
            return l;
        }

        /// <summary>The report as a Markdown page for the lab book.</summary>
        public static string Markdown(string part, int step, ShapeField f, Result r, DateTime when) =>
            "# " + (string.IsNullOrWhiteSpace(part) ? "Reshaped part" : part) + ", step " + step.ToString(Inv.C) + "\n\n" + when.ToString("yyyy-MM-dd HH:mm", Inv.C) + ", field: " + (f?.Source ?? "-") + "\n\n" +
            string.Join("\n", (f?.Notes ?? new List<string>()).Select(x => "- " + x)) + (f != null && f.Notes.Count > 0 ? "\n" : "") + string.Join("\n", r.Report.Select(x => "- " + x)) + "\n";
    }

    /// <summary>
    /// What is printed so far, to ask how deep the head reaches into it: the head is a cone (tip radius, half angle,
    /// length) with a cylinder above, along the real tool axis. Per small cell the highest bead counts.
    /// </summary>
    public class HeadGrid
    {
        private readonly NonPlanarCheck.Head _h; private readonly double _w, _fine, _tan, _reach; private const int Per = 5;
        private readonly Dictionary<long, Dictionary<int, double[]>> _cells = new Dictionary<long, Dictionary<int, double[]>>(); // coarse -> fine -> x y z along move
        private readonly Dictionary<long, double> _top = new Dictionary<long, double>();
        private readonly int _rings; private readonly double _coarse;

        public HeadGrid(NonPlanarCheck.Head head, PrintSettings st, double width = 0)
        {
            _h = head ?? new NonPlanarCheck.Head(); _w = width > 0 ? width : st.BeadWidth; _fine = Math.Max(2, 0.5 * _w); _coarse = _fine * Per;
            _tan = Math.Tan(Math.Max(0, Math.Min(89, _h.HalfAngle)) * Math.PI / 180);
            _reach = Math.Max(_h.Radius, _h.TipRadius) + 0.5 * _w; _rings = (int)Math.Ceiling(_reach / _coarse);
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void Lay(Point3d a, Point3d b, double alongB, int move)
        {
            double len = a.DistanceTo(b); int n = Math.Max(1, (int)Math.Ceiling(len / _fine));
            for (int k = 1; k <= n; k++)
            {
                double t = k / (double)n; Point3d p = a + (b - a) * t;
                int fx = (int)Math.Floor(p.X / _fine), fy = (int)Math.Floor(p.Y / _fine);
                int cx = (int)Math.Floor(fx / (double)Per), cy = (int)Math.Floor(fy / (double)Per);
                long key = Key(cx, cy);
                if (!_cells.TryGetValue(key, out var d)) { _cells[key] = d = new Dictionary<int, double[]>(); _top[key] = double.MinValue; }
                int f = (fx - cx * Per) * Per + (fy - cy * Per);
                if (!d.TryGetValue(f, out var v)) d[f] = v = new[] { 0, 0, double.MinValue, 0, 0.0 };
                if (p.Z < v[2]) continue;
                v[0] = p.X; v[1] = p.Y; v[2] = p.Z; v[3] = alongB - len * (1 - t); v[4] = move;
                if (p.Z > _top[key]) _top[key] = p.Z;
            }
        }

        /// <summary>
        /// How deep the head (tip at p, along axis) reaches into what is printed, mm; beads laid after `before`
        /// along the path (the stretch just behind the nozzle) and beads less than `above` over the tip do not count.
        /// </summary>
        public double Depth(Point3d p, Vector3d axis, double before, double above, out int move)
        {
            move = -1; double worst = 0;
            int cx = (int)Math.Floor(p.X / _coarse), cy = (int)Math.Floor(p.Y / _coarse);
            // a tilted head leans out sideways: its body is at most this far from the tip in plan for the height of the cone
            double lean = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y); int rings = _rings + (int)Math.Ceiling(lean * _h.ConeLength / _coarse);
            for (int a = cx - rings; a <= cx + rings; a++)
                for (int b = cy - rings; b <= cy + rings; b++)
                {
                    long key = Key(a, b);
                    if (!_top.TryGetValue(key, out double top)) continue;
                    double mx = axis.X > 0 ? (a + 1) * _coarse : a * _coarse, my = axis.Y > 0 ? (b + 1) * _coarse : b * _coarse;
                    if ((mx - p.X) * axis.X + (my - p.Y) * axis.Y + (top - p.Z) * axis.Z <= above) continue;
                    foreach (var v in _cells[key].Values)
                    {
                        if (v[3] > before) continue;
                        double dx = v[0] - p.X, dy = v[1] - p.Y, dz = v[2] - p.Z, h = dx * axis.X + dy * axis.Y + dz * axis.Z;
                        if (h <= above) continue;
                        double r = Math.Sqrt(Math.Max(0, dx * dx + dy * dy + dz * dz - h * h));
                        double need = (h < _h.ConeLength ? Math.Min(_h.Radius, _h.TipRadius + h * _tan) : _h.Radius) + 0.5 * _w;
                        if (r >= need) continue;
                        double g = Math.Min(need - r, h);
                        if (g > worst) { worst = g; move = (int)v[4]; }
                    }
                }
            return worst;
        }
    }

    /// <summary>
    /// Checks a path with tilted (or vertical) tool: the height of every bead above the lap below, the sideways
    /// step of the laps, tilt and how fast the tool turns, the slope of the path against the tool, what the head
    /// reaches into (along the real tool axis) and how much material per second is asked for. Independent of how
    /// the path was made: everything is measured on the path itself.
    /// </summary>
    public static class MultiAxisCheck
    {
        public class Options
        {
            public NonPlanarCheck.Head Head = new NonPlanarCheck.Head();
            public double MinLayer, MaxLayer;
            /// <summary>Degrees.</summary>
            public double MaxTilt = 45;
            /// <summary>How fast the tool may turn: degrees per second at the print speed, degrees per mm of path.</summary>
            public double MaxTiltPerSecond = 30, MaxTiltPerMm = 2;
            /// <summary>mm3/s; 0 = only told, not judged (Max Volume Rate of the settings is kept by the slicer itself).</summary>
            public double MaxFlow;
        }

        public class Result
        {
            public bool Ok = true;
            /// <summary>The first thing that is wrong, in a few words.</summary>
            public string First = "";
            public List<string> Report = new List<string>();
            /// <summary>Per point: measured bead height square to the layer (0 = not measured), tool tilt in degrees.</summary>
            public List<double> Heights = new List<double>(), Tilts = new List<double>();
            public List<Point3d> Collisions = new List<Point3d>(), BadHeights = new List<Point3d>(), BadFlow = new List<Point3d>(), FastTurns = new List<Point3d>();
            public double MinHeight, MaxHeight, MaxTilt, MaxTiltPerMm, MaxTiltPerSecond, MaxSlope, MaxFlow, MaxOffset, MaxDepth, HeelMax;
            /// <summary>Material extruded / material the measured gaps need, minus 1 (over all measured points), and the share of the length more than 10 % off.</summary>
            public double VolumeError, VolumeOff, ThinShare, ThickShare;
            public int Measured, Thin, Thick, OffsetCount, Steep, Grazes, NotMeasured;
        }

        private static double Deg(double rad) => rad * 180 / Math.PI;
        private static double Angle(Vector3d a, Vector3d b)
        {
            double cx = a.Y * b.Z - a.Z * b.Y, cy = a.Z * b.X - a.X * b.Z, cz = a.X * b.Y - a.Y * b.X;
            return Deg(Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), a.X * b.X + a.Y * b.Y + a.Z * b.Z));
        }

        public static Result Run(Toolpath tp, Options o)
        {
            var r = new Result(); o = o ?? new Options(); var head = o.Head ?? new NonPlanarCheck.Head();
            if (tp == null || tp.Count < 3) { r.Ok = false; r.First = "the path is empty"; r.Report.Add("CHECK: empty path."); return r; }
            var st = tp.Settings; int n = tp.Count; double w = st.BeadWidth, h = Math.Max(0.05, st.LayerHeight);
            double minL = o.MinLayer > 0 ? o.MinLayer : PathReshape.AutoMin(st), maxL = o.MaxLayer > 0 ? o.MaxLayer : PathReshape.AutoMax(st);
            double MaxAt(int i) => o.MaxLayer > 0 ? o.MaxLayer : PathReshape.AutoMax(st, tp.Widths[i]);
            var pr = PathEngine.Process(tp, false);
            bool tilted = false;
            for (int i = 0; i < n; i++)
            {
                r.Heights.Add(0); double t = Deg(Math.Acos(Math.Max(-1, Math.Min(1, tp.Normals[i].Z)))); r.Tilts.Add(t);
                if (t > r.MaxTilt) r.MaxTilt = t; if (t > 0.5) tilted = true;
            }
            // ---- 1 heights, sideways steps, material
            var laps = new LapBelow(tp); double used = 0, need = 0, off = 0, compared = 0, all = 0, thin = 0, thick = 0; var hs = new List<KeyValuePair<double, double>>(); // shares are by length, not by number of points
            int firstLayer = int.MaxValue, lastLayer = int.MinValue, secondLayer = int.MaxValue;
            for (int i = 1; i < n; i++) if (LapBelow.Prints(tp, i) && tp.Roles[i] != PathRole.Skirt) { firstLayer = Math.Min(firstLayer, tp.Layers[i]); lastLayer = Math.Max(lastLayer, tp.Layers[i]); }
            for (int i = 1; i < n; i++) if (LapBelow.Prints(tp, i) && tp.Roles[i] != PathRole.Skirt && tp.Layers[i] > firstLayer) secondLayer = Math.Min(secondLayer, tp.Layers[i]);
            var gap = new double[n]; var gapI = new bool[n];
            // a bead that fades in or out (start and end of a spiral, the turns of extra passes) has full height again close by
            var alongP = new double[n]; var near = new double[n]; { for (int i = 1; i < n; i++) alongP[i] = alongP[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]); double lastGood = double.MinValue; for (int i = 0; i < n; i++) { if (tp.Heights[i] >= 0.95 * minL && i > 0 && LapBelow.Prints(tp, i)) lastGood = alongP[i]; near[i] = lastGood == double.MinValue ? 1e9 : alongP[i] - lastGood; } double next = double.MaxValue; for (int i = n - 1; i >= 0; i--) { if (tp.Heights[i] >= 0.95 * minL && i > 0 && LapBelow.Prints(tp, i)) next = alongP[i]; if (next < double.MaxValue) near[i] = Math.Min(near[i], next - alongP[i]); } }
            for (int i = 1; i < n; i++)
            {
                if ((i & 4095) == 0) Cancel.Check();
                if (!LapBelow.Prints(tp, i) || tp.Roles[i] == PathRole.Skirt || tp.Layers[i] == firstLayer) continue;
                Point3d p = tp.Points[i]; Vector3d a = tp.Normals[i];
                if (!laps.Find(p, tp.Layers[i], 0.25 * h, laps.Along[i], i, out Point3d q)) { r.NotMeasured++; continue; }
                double dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
                // the layer slopes although the tool stands straight: what the bead needs is measured square to the layer
                Vector3d ln = a;
                if (tp.Warp is ReshapeWarp && !tp.Warp.TiltTool) tp.Warp.ToReal(tp.Warp.ToFlat(p), out ln);
                double dn = dx * a.X + dy * a.Y + dz * a.Z, side = Math.Sqrt(Math.Max(0, dx * dx + dy * dy + dz * dz - dn * dn));
                double law = Math.Max(Math.Max(0, (dx * ln.X + dy * ln.Y + dz * ln.Z) / Math.Max(0.2, ln.Z)), Math.Sqrt(dx * dx + dy * dy) * st.EffectiveSkin / Math.Max(1e-9, tp.Widths[i]));
                gap[i] = law; gapI[i] = true; r.Heights[i] = dn;
                bool passes = tp.Heights[i] < 0.6 * dn || tp.Heights[i] < 0.6 * minL && near[i] < 100 && Math.Abs(dn - tp.Heights[i]) < 0.15 * h; // one of several passes over the same lap, or a bead that fades in or out
                double len = p.DistanceTo(tp.Points[i - 1]);
                // the first lap above the flat one and the last lap of a spiral run in and out of nothing on purpose
                bool runs = (tp.Layers[i] == secondLayer || tp.Layers[i] == lastLayer) && Math.Abs(dn - tp.Heights[i]) < 0.15 * h;
                if (tp.Roles[i] == PathRole.Seam || passes || runs) continue;
                r.Measured++; hs.Add(new KeyValuePair<double, double>(dn, len)); all += len;
                if (side > r.MaxOffset) r.MaxOffset = side;
                if (side > 0.5 * w + 0.05) r.OffsetCount++;
                if (dn > 1.05 * MaxAt(i)) { r.Thick++; thick += len; r.BadHeights.Add(p); }
                if (dn < 0.95 * minL && side < 0.25 * w) { r.Thin++; thin += len; r.BadHeights.Add(p); }
            }
            for (int i = 2; i < n; i++)
            {
                if (!gapI[i] || !gapI[i - 1] || tp.Layers[i] != tp.Layers[i - 1]) continue;
                double lp = Math.Sqrt((tp.Points[i].X - tp.Points[i - 1].X) * (tp.Points[i].X - tp.Points[i - 1].X) + (tp.Points[i].Y - tp.Points[i - 1].Y) * (tp.Points[i].Y - tp.Points[i - 1].Y));
                double l3 = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                double have = 0.5 * (tp.Heights[i] + tp.Heights[i - 1]) * tp.Flows[i] * l3, want = 0.5 * (gap[i] + gap[i - 1]) * lp;
                if (tp.Heights[i] < 0.25 * h || want < 1e-6 || have < 0.6 * want || tp.Roles[i] == PathRole.Seam) continue;
                used += have; need += want; compared += l3;
                if (Math.Abs(have / want - 1) > 0.10) { off += l3; r.BadFlow.Add(tp.Points[i]); }
            }
            r.VolumeError = need > 0 ? used / need - 1 : 0; r.VolumeOff = compared > 0 ? off / compared : 0;
            hs.Sort((a, b) => a.Key.CompareTo(b.Key));
            { double run = 0; bool got = false; foreach (var kv in hs) { run += kv.Value; if (!got && run >= 0.002 * all) { r.MinHeight = kv.Key; got = true; } r.MaxHeight = kv.Key; if (run >= 0.998 * all) break; } }
            thin = all > 0 ? thin / all : 0; thick = all > 0 ? thick / all : 0;

            // ---- 2 turning of the tool, slope, flow
            int fast = 0; string fastWhere = "";
            for (int i = 1; i < n; i++)
            {
                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                double ang = Angle(tp.Normals[i - 1], tp.Normals[i]);
                if (ang > 1e-6)
                {
                    double mm = ang / Math.Max(0.5, len), sec = mm * pr.Speed[i] / 60;
                    if (mm > r.MaxTiltPerMm) r.MaxTiltPerMm = mm;
                    if (sec > r.MaxTiltPerSecond) r.MaxTiltPerSecond = sec;
                    if (ang > 0.2 && (mm > o.MaxTiltPerMm || sec > o.MaxTiltPerSecond)) { if (fast++ == 0) fastWhere = Inv.F($"first at point {i}, layer {tp.Layers[i] + 1}"); r.FastTurns.Add(tp.Points[i]); }
                }
                if (!LapBelow.Prints(tp, i) || len < 0.2) continue;
                Vector3d u = tp.Points[i] - tp.Points[i - 1], a = tp.Normals[i];
                double sin = Math.Abs(u.X * a.X + u.Y * a.Y + u.Z * a.Z) / len, sl = Deg(Math.Asin(Math.Min(1, sin)));
                if (tp.Roles[i] != PathRole.Seam)
                {
                    if (sl > r.MaxSlope) r.MaxSlope = sl;
                    if (sl > 90 - head.HalfAngle) r.Steep++;
                    double heel = head.TipRadius * Math.Tan(Math.Min(1.4, sl * Math.PI / 180)); if (heel > r.HeelMax) r.HeelMax = heel;
                }
                double area = PrintSettings.BeadArea(0.5 * (tp.Widths[i] + tp.Widths[i - 1]), 0.5 * (tp.Heights[i] + tp.Heights[i - 1])) * tp.Flows[i] * st.Flow;
                r.MaxFlow = Math.Max(r.MaxFlow, area * pr.Speed[i] / 60);
            }

            // ---- 3 clearance along the real tool axis
            double wide = 0; for (int i = 1; i < n; i++) if (LapBelow.Prints(tp, i)) wide = Math.Max(wide, tp.Widths[i]);
            var grid = new HeadGrid(head, st, wide); double along = 0; bool any = false; var where = new List<string>(); var shown = new HashSet<long>();
            double skip = Math.Max(40, 6 * (head.TipRadius + 0.5 * w));
            for (int i = 1; i < n; i++)
            {
                if ((i & 1023) == 0) Cancel.Check();
                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]); along += len;
                bool print = LapBelow.Prints(tp, i);
                if (!print && !any) continue;
                int parts = len > 5 ? (int)Math.Ceiling(len / 5) : 1; double worst = 0; int with = -1; Point3d at = tp.Points[i];
                for (int k = 1; k <= parts; k++)
                {
                    double u = k / (double)parts; Point3d p = tp.Points[i - 1] + (tp.Points[i] - tp.Points[i - 1]) * u;
                    Vector3d a = tp.Normals[i - 1] * (1 - u) + tp.Normals[i] * u; double l = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z); if (l > 1e-9) a = new Vector3d(a.X / l, a.Y / l, a.Z / l);
                    double d = grid.Depth(p, a, along - len * (1 - u) - skip, 0.5 * Math.Max(h, tp.Heights[i]), out int mv);
                    if (d > worst) { worst = d; with = mv; at = p; }
                }
                if (worst > r.MaxDepth) r.MaxDepth = worst;
                if (worst > head.Graze)
                {
                    r.Collisions.Add(at);
                    long pair = ((long)tp.Layers[i] << 32) ^ (uint)(with >= 0 ? tp.Layers[with] : 0);
                    if (where.Count < 6 && shown.Add(pair)) where.Add(Inv.F($"layer {tp.Layers[i] + 1} at {at.X:0}, {at.Y:0}, {at.Z:0}{(print ? "" : " (no extrusion)")} with layer {(with >= 0 ? tp.Layers[with] + 1 : 0)}, {worst:0.0} mm deep"));
                }
                else if (worst > 0.05) r.Grazes++;
                if (print) { grid.Lay(tp.Points[i - 1], tp.Points[i], along, i); any = true; }
            }

            // ---- verdict
            void Bad(string s) { if (r.Ok) r.First = s; r.Ok = false; }
            double share = r.Measured > 0 ? 100.0 / r.Measured : 0;
            r.ThinShare = thin; r.ThickShare = thick;
            var rep = r.Report;
            rep.Add(Inv.F($"Bead height above the lap below ({(tilted ? "along the tool" : "straight up")}, {r.Measured} points): {r.MinHeight:0.##} to {r.MaxHeight:0.##} mm, allowed {minL:0.##} to {maxL:0.##} mm.") + (r.Thick + r.Thin > 0 ? Inv.F($" {100 * thick:0.#} % of the length is thicker, {100 * thin:0.#} % thinner.") : ""));
            if (thick > 0.02) Bad(Inv.F($"{100 * thick:0.#} % of the beads are thicker than {maxL:0.##} mm"));
            if (thin > 0.01) Bad(Inv.F($"{100 * thin:0.#} % of the beads are thinner than {minL:0.##} mm"));
            rep.Add(Inv.F($"Material against the measured gaps: {100 * r.VolumeError:+0.0;-0.0;0} % in total, {100 * r.VolumeOff:0.#} % of the length more than 10 % off."));
            if (Math.Abs(r.VolumeError) > 0.05 || r.VolumeOff > 0.05) Bad(Inv.F($"the extrusion does not fit the gaps ({100 * r.VolumeError:+0.0;-0.0;0} %, {100 * r.VolumeOff:0} % of the length off)"));
            rep.Add(Inv.F($"Sideways step to the lap below: up to {r.MaxOffset:0.0} mm (half a bead = {0.5 * w:0.0} mm)") + (r.OffsetCount > 0 ? Inv.F($"; {r.OffsetCount} point(s) ({r.OffsetCount * share:0.#} %) step further (infill over gaps, roofs: see Path Check).") : "."));
            rep.Add(Inv.F($"Tool: tilted up to {r.MaxTilt:0.#} degrees; turns at most {r.MaxTiltPerMm:0.###} degrees per mm and {r.MaxTiltPerSecond:0.#} degrees per second at the print speed (allowed {o.MaxTilt:0}, {o.MaxTiltPerMm:0.##} and {o.MaxTiltPerSecond:0}).") + (fast > 0 ? Inv.F($" TOO FAST at {fast} move(s), {fastWhere}.") : ""));
            if (r.MaxTilt > o.MaxTilt + 0.01) Bad(Inv.F($"the tool is tilted {r.MaxTilt:0} degrees"));
            if (fast > 0) Bad(Inv.F($"the tool turns too fast at {fast} move(s)"));
            rep.Add(Inv.F($"Path against the tool: climbs at most {r.MaxSlope:0.#} degrees (allowed {90 - head.HalfAngle:0}); the edge of a {2 * head.TipRadius:0.#} mm wide tip dips {r.HeelMax:0.##} mm below its middle there."));
            if (r.Steep > 0) Bad(Inv.F($"{r.Steep} move(s) climb more steeply than the cone of the nozzle allows"));
            if (r.HeelMax > 0.8 * Math.Max(0.1, r.MinHeight > 0 ? r.MinHeight : h)) rep.Add(Inv.F($"Note: a flat tip of {2 * head.TipRadius:0.#} mm would press into the bead beside it there. Use a pointed nozzle (Tip Radius), a smaller slope or a tilted tool."));
            rep.Add(r.Collisions.Count == 0
                ? Inv.F($"Head clearance OK ({head}, along the real tool axis): reaches at most {r.MaxDepth:0.##} mm into earlier beads (allowed {head.Graze:0.##}).")
                : Inv.F($"HEAD COLLISION at {r.Collisions.Count} move(s), up to {r.MaxDepth:0.0} mm deep ({head}, along the real tool axis): ") + string.Join("; ", where) + ".");
            if (r.Collisions.Count > 0) Bad(Inv.F($"the head hits earlier beads at {r.Collisions.Count} move(s)"));
            rep.Add(Inv.F($"Material flow: up to {r.MaxFlow:0} mm3/s.") + (o.MaxFlow > 0 && r.MaxFlow > 1.001 * o.MaxFlow ? Inv.F($" ABOVE the limit of {o.MaxFlow:0}.") : ""));
            if (o.MaxFlow > 0 && r.MaxFlow > 1.001 * o.MaxFlow) Bad(Inv.F($"the flow goes up to {r.MaxFlow:0} mm3/s"));
            if (r.NotMeasured > 0) rep.Add(Inv.F($"{r.NotMeasured} point(s) have no lap below within two bead widths (infill over gaps, bridges): not measured."));
            rep.Insert(0, r.Ok ? Inv.F($"OK: beads {r.MinHeight:0.##} to {r.MaxHeight:0.##} mm, tilt up to {r.MaxTilt:0.#} degrees, the head stays clear.") : "CHECK: " + r.First + ".");
            return r;
        }
    }
}
