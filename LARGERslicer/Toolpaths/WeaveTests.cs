using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    // ====================================================================== the joint, measured on the path
    /// <summary>The joint of one zone (or of a part of it), measured on a path.</summary>
    public sealed class WeaveJoint
    {
        public int Zone;
        /// <summary>Length of seam measured over all layers, mm, and the area of the joint face (length x layer height), mm2.</summary>
        public double Length, Face;
        public int Layers;
        /// <summary>Contact of the two beads of one layer (neck of the two bead sections x length), mm2, and the same per face.</summary>
        public double Vertical, VerticalShare;
        /// <summary>Contact of a bead with the other bead in the layer above or below (the shelf), mm2, and the same per face.</summary>
        public double Horizontal, HorizontalShare;
        /// <summary>Shelf: mean and largest (99 %) step of the seam from layer to layer, mm.</summary>
        public double Depth, DepthMax;
        public double Spacing, SpacingMin, SpacingMax;
        /// <summary>Sideways overlap of the two beads (negative = gap), mm: mean and worst.</summary>
        public double Overlap, OverlapMin, OverlapMax;
        /// <summary>Places where the two beads were not found side by side.</summary>
        public int Missed, Stations;
        /// <summary>Length of the wavy seam / straight length.</summary>
        public double WaveFactor = 1;

        /// <summary>Contact per metre of joint (all layers together), mm2/m.</summary>
        public double VerticalPerMetre(double meanLength) => meanLength > 0 ? Vertical / meanLength * 1000 : 0;
        public double HorizontalPerMetre(double meanLength) => meanLength > 0 ? Horizontal / meanLength * 1000 : 0;
    }

    /// <summary>Material of the joint. All values are placeholders until they are measured.</summary>
    public sealed class JointMaterial
    {
        public double EAcross = 2400, ELayers = 1800, G = 1100, GLayers = 700, Nu = 0.35;
        /// <summary>Strength of the weld between two beads of a layer (tension, shear) and between layers, MPa.</summary>
        public double SigmaSide = 20, TauSide = 12, SigmaLayer = 18, TauLayer = 12;
        /// <summary>Placeholder values, not measured.</summary>
        public bool Placeholder = true;
    }

    /// <summary>Stiffness and capacity of a joint per mm2 of joint face (springs side by side).</summary>
    public struct JointSprings
    {
        /// <summary>N/mm3: opening (across), shear along the seam, shear up and down.</summary>
        public double KOpen, KAlong, KUp;
        /// <summary>N/mm2 of joint face.</summary>
        public double ROpen, RAlong, RUp;

        public static JointSprings Of(WeaveJoint j, double layerHeight, JointMaterial m)
        {
            double g = Math.Max(0.1, j.Spacing), h = Math.Max(0.1, layerHeight), v = j.VerticalShare, s = j.HorizontalShare;
            return new JointSprings
            {
                KOpen = m.EAcross * v / g + m.GLayers * s / h,
                KAlong = m.G * v / g + m.GLayers * s / h,
                KUp = m.G * v / g + m.ELayers * s / h,
                ROpen = m.SigmaSide * v + m.TauLayer * s,
                RAlong = m.TauSide * v + m.TauLayer * s,
                RUp = m.TauSide * v + m.SigmaLayer * s,
            };
        }
    }

    // ====================================================================== the check
    /// <summary>One path, measured. Areas in mm2 per checked layer.</summary>
    public sealed class WeaveNumbers
    {
        public int Layers;
        public double Covered, Doubled, Voids, Length, Volume;
        /// <summary>Extruded volume / (covered area x layer height).</summary>
        public double Fill;
        public int Crossings;
        /// <summary>Smallest bend radius of the woven beads, and how much of their length is below half a bead, %.</summary>
        public double MinRadius = 1e9, TightShare;
        /// <summary>Sideways distance to the layer below, mm: largest and mean, along the seams.</summary>
        public double StepMax, StepMean;
        /// <summary>Share of the seams that lie more than half a bead beside the layer below, %.</summary>
        public double StepOver;
        public double Points;
        public WeaveJoint Joint = new WeaveJoint();
    }

    public sealed class WeaveReport
    {
        public WeaveNumbers Before, After;
        public List<WeaveJoint> ZonesBefore = new List<WeaveJoint>(), ZonesAfter = new List<WeaveJoint>();
        public List<string> Table = new List<string>();
        public bool Ok = true;
        public string Verdict = "";
        public double Seconds;
    }

    /// <summary>
    /// Measures a path before and after weaving. Every checked layer is drawn as a fine raster of its
    /// bead footprints: area covered twice (too much material), small open places (gaps); plus
    /// crossings, bends, the step to the layer below and the contact areas of the seams.
    /// </summary>
    public static class WeaveCheck
    {
        public static WeaveReport Compare(Toolpath before, Toolpath after, WeaveZoneSet set, int maxLayers = 24)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var r = new WeaveReport();
            var layers = Pick(set, maxLayers);
            r.Before = Measure(before, set, layers); r.After = Measure(after, set, layers);
            r.ZonesBefore = Joints(before, set, null); r.ZonesAfter = Joints(after, set, null);
            r.Before.Joint = Sum(r.ZonesBefore); r.After.Joint = Sum(r.ZonesAfter);
            r.Seconds = clock.Elapsed.TotalSeconds;
            Tell(r, before.Settings);
            return r;
        }

        /// <summary>Pairs of layers spread over the zones.</summary>
        public static List<int> Pick(WeaveZoneSet set, int max)
        {
            var all = set.Zones.SelectMany(z => z.Runs.Select(q => q.Layer)).Distinct().OrderBy(v => v).ToList();
            if (max <= 0 || all.Count <= max) return all;
            var res = new SortedSet<int>(); int pairs = Math.Max(1, max / 2);
            for (int i = 0; i < pairs; i++)
            {
                int k = (int)((all.Count - 2) * (i + 0.5) / pairs);
                res.Add(all[k]); res.Add(all[k + 1]);
            }
            return res.ToList();
        }

        private static void Tell(WeaveReport r, PrintSettings st)
        {
            var b = r.Before; var a = r.After; double w = st.BeadWidth;
            Func<double, double, string> pc = (x, y) => Inv.F($"{(Math.Abs(x) > 1e-9 ? 100 * (y - x) / Math.Abs(x) : 0):+0.0;-0.0;0.0} %");
            r.Table.Add("                                    before      after");
            r.Table.Add(Inv.F($"covered area (mm2, {b.Layers} layers) {b.Covered,12:0} {a.Covered,10:0}   {pc(b.Covered, a.Covered)}"));
            r.Table.Add(Inv.F($"covered twice (mm2)            {b.Doubled,12:0} {a.Doubled,10:0}   {100 * b.Doubled / Math.Max(1, b.Covered):0.00} -> {100 * a.Doubled / Math.Max(1, a.Covered):0.00} % of the covered area"));
            r.Table.Add(Inv.F($"small open places (mm2)        {b.Voids,12:0} {a.Voids,10:0}"));
            r.Table.Add(Inv.F($"extruded volume (cm3)          {b.Volume / 1000,12:0.0} {a.Volume / 1000,10:0.0}   {pc(b.Volume, a.Volume)}"));
            r.Table.Add(Inv.F($"volume / covered space         {b.Fill,12:0.000} {a.Fill,10:0.000}"));
            r.Table.Add(Inv.F($"crossings                      {b.Crossings,12} {a.Crossings,10}"));
            r.Table.Add(Inv.F($"smallest bend on the seams (mm){b.MinRadius,12:0.00} {a.MinRadius,10:0.00}   (half a bead = {0.5 * w:0.0#})"));
            r.Table.Add(Inv.F($"step to the layer below (mm)   {b.StepMax,12:0.00} {a.StepMax,10:0.00}   mean {b.StepMean:0.00} -> {a.StepMean:0.00}, limit {0.5 * w:0.0#}, over it {b.StepOver:0.0} -> {a.StepOver:0.0} %"));
            r.Table.Add(Inv.F($"spacing at the seams (mm)      {b.Joint.Spacing,12:0.00} {a.Joint.Spacing,10:0.00}   overlap {b.Joint.Overlap:0.00} -> {a.Joint.Overlap:0.00} mm (1 % worst {b.Joint.OverlapMin:0.00} to {b.Joint.OverlapMax:0.00} -> {a.Joint.OverlapMin:0.00} to {a.Joint.OverlapMax:0.00})"));
            r.Table.Add(Inv.F($"side contact / joint face      {b.Joint.VerticalShare,12:0.000} {a.Joint.VerticalShare,10:0.000}"));
            r.Table.Add(Inv.F($"interlock contact / joint face {b.Joint.HorizontalShare,12:0.000} {a.Joint.HorizontalShare,10:0.000}   depth mean {a.Joint.Depth:0.00}, up to {a.Joint.DepthMax:0.00} mm"));
            var bad = new List<string>();
            if (a.Crossings > b.Crossings) bad.Add("the path crosses itself more often");
            // closing a gap on purpose (to one bead spacing) counts as wanted overlap
            double closed = a.Joint.Layers > 0 ? a.Joint.Length * a.Layers / a.Joint.Layers * Math.Max(0, w - st.BeadSpacing - Math.Max(0, b.Joint.Overlap)) : 0;
            if (a.Doubled - closed > (b.Doubled / Math.Max(1, b.Covered) + 0.01) * a.Covered) bad.Add("more area is covered twice");
            if (a.Voids > b.Voids + 0.01 * b.Covered) bad.Add("gaps open");
            if (a.StepOver > b.StepOver + 0.3) bad.Add("a bead steps too far from the one below");
            if (a.MinRadius < Math.Min(b.MinRadius, 0.5 * w) * 0.85) bad.Add("a bend is too tight");
            r.Ok = bad.Count == 0;
            r.Verdict = r.Ok
                ? Inv.F($"OK: no new crossings, gaps or double material. Contact of the layers across the seam {b.Joint.HorizontalShare:0.00} -> {a.Joint.HorizontalShare:0.00} mm2 per mm2 of joint face.")
                : "CHECK: " + string.Join(", ", bad) + ".";
            r.Table.Insert(0, r.Verdict);
        }

        /// <summary>The numbers of one path on some layers.</summary>
        public static WeaveNumbers Measure(Toolpath tp, WeaveZoneSet set, List<int> layers)
        {
            var flat = WeaveFinder.Flat(tp);
            var want = new HashSet<int>(layers);
            var ranges = WeaveFinder.Split(flat);
            if (ranges.Count == 1 && ranges[0].Climb) return Spiral(tp, flat, ranges[0], set, layers);
            ranges = ranges.Where(x => want.Contains(x.Layer)).ToList();
            double w = tp.Settings.BeadWidth, step = Math.Min(WeaveFinder.StepOf(tp.Settings), 0.1 * w);
            var parts = new WeaveNumbers[ranges.Count];
            var samples = new WeaveLayer[ranges.Count];
            var byLayer = new Dictionary<int, List<WeaveRun>>();
            foreach (var z in set.Zones) foreach (var r in z.Runs) { if (!byLayer.TryGetValue(r.Layer, out var l)) byLayer[r.Layer] = l = new List<WeaveRun>(); l.Add(r); }
            Parallel.For(0, ranges.Count, i => samples[i] = WeaveFinder.Sample(flat, ranges[i], step));
            Parallel.For(0, ranges.Count, i =>
            {
                var p = parts[i] = new WeaveNumbers { Layers = 1 };
                var l = samples[i];
                Raster(l, w, p);
                double height = tp.Settings.LayerHeight;
                foreach (var c in ranges[i].Chains)
                    for (int k = c[0] + 1; k <= c[1]; k++)
                    {
                        double dx = flat.Points[k].X - flat.Points[k - 1].X, dy = flat.Points[k].Y - flat.Points[k - 1].Y, dz = flat.Points[k].Z - flat.Points[k - 1].Z, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        p.Length += len; p.Points++;
                        p.Volume += PrintSettings.BeadArea(0.5 * (tp.Widths[k] + tp.Widths[k - 1]), 0.5 * (tp.Heights[k] + tp.Heights[k - 1])) * tp.Flows[k] * len;
                    }
                p.Fill = p.Covered > 0 ? p.Volume / (p.Covered * height) : 0;
                // bends and steps near the seams
                if (!byLayer.TryGetValue(l.Layer, out var runs)) return;
                var near = Near(l, runs, w);
                double tight = 0, total = 0;
                for (int k = 1; k + 1 < l.N; k++)
                {
                    if (!near[k] || l.Chain[k] != l.Chain[k - 1] || l.Chain[k] != l.Chain[k + 1]) continue;
                    double rad = Radius(l, k, 0.35 * w);
                    total++;
                    if (rad < 0.5 * w) tight++;
                    if (rad < p.MinRadius) p.MinRadius = rad;
                }
                p.TightShare = total > 0 ? 100 * tight / total : 0;
                // layer below (only when it is the neighbour)
                int at = Array.FindIndex(samples, s => s != null && s.Layer == l.Layer - 1);
                if (at >= 0)
                {
                    var below = samples[at]; var grid = new WeaveFinder.Grid(below, 2 * w);
                    double sum = 0; int cnt = 0, far = 0;
                    for (int k = 0; k < l.N; k += 2)
                    {
                        if (!near[k]) continue;
                        double d = Distance(below, grid, l.X[k], l.Y[k], 2 * w);
                        sum += d; cnt++; if (d > p.StepMax) p.StepMax = d; if (d > 0.52 * w) far++;
                    }
                    p.StepMean = cnt > 0 ? sum / cnt : 0; p.StepOver = cnt > 0 ? 100.0 * far / cnt : 0;
                }
            });
            var res = new WeaveNumbers();
            int steps = 0; double tl = 0;
            foreach (var p in parts)
            {
                if (p == null) continue;
                res.Layers++; res.Covered += p.Covered; res.Doubled += p.Doubled; res.Voids += p.Voids; res.Length += p.Length; res.Volume += p.Volume; res.Points += p.Points;
                res.MinRadius = Math.Min(res.MinRadius, p.MinRadius); res.StepMax = Math.Max(res.StepMax, p.StepMax);
                if (p.StepMean > 0) { res.StepMean += p.StepMean; res.StepOver += p.StepOver; steps++; }
                res.TightShare += p.TightShare; tl++;
            }
            if (steps > 0) { res.StepMean /= steps; res.StepOver /= steps; }
            if (tl > 0) res.TightShare /= tl;
            res.Fill = res.Covered > 0 ? res.Volume / (res.Covered * tp.Settings.LayerHeight) : 0;
            // crossings: the path engine's own test
            var sub = new Toolpath { Settings = tp.Settings };
            for (int i = 0; i < flat.Count; i++) if (want.Contains(flat.Layers[i])) sub.AddFrom(flat, i);
            try { res.Crossings = sub.Count > 2 ? LeadEngine.FindCrossings(sub).Count : 0; } catch (DllNotFoundException) { res.Crossings = -1; }
            return res;
        }

        /// <summary>A spiral: horizontal cuts through the beads, as they really lie at that height.</summary>
        private static WeaveNumbers Spiral(Toolpath tp, Toolpath flat, WeaveFinder.LayerRange range, WeaveZoneSet set, List<int> layers)
        {
            double w = tp.Settings.BeadWidth, lh = tp.Settings.LayerHeight, step = Math.Min(WeaveFinder.StepOf(tp.Settings), 0.1 * w);
            var l = WeaveFinder.Sample(flat, range, step);
            var res = new WeaveNumbers { Layers = layers.Count };
            // the height of the first sample of each layer is the lower side of the cut
            var start = new Dictionary<int, double>();
            for (int i = 0; i < l.N; i++) if (!start.ContainsKey(l.Lap[i])) start[l.Lap[i]] = l.H[i];
            var at = layers.Where(start.ContainsKey).Select(k => start[k]).ToList();
            double moved = lh * 0.37; // not right at the seam of the spiral
            var parts = new WeaveNumbers[at.Count];
            Parallel.For(0, at.Count, q =>
            {
                double z = at[q] + moved;
                parts[q] = new WeaveNumbers();
                var keep = new bool[l.N];
                for (int i = 0; i < l.N; i++) keep[i] = l.H[i] > z && l.H[i] <= z + lh;
                Raster(l, w, parts[q], keep);
            });
            foreach (var p in parts) { res.Covered += p.Covered; res.Doubled += p.Doubled; res.Voids += p.Voids; }
            var want = new HashSet<int>(layers);
            foreach (var c in range.Chains)
                for (int k = c[0] + 1; k <= c[1]; k++)
                {
                    if (!want.Contains(flat.Layers[k])) continue;
                    double dx = flat.Points[k].X - flat.Points[k - 1].X, dy = flat.Points[k].Y - flat.Points[k - 1].Y, dz = flat.Points[k].Z - flat.Points[k - 1].Z, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    res.Length += len; res.Points++;
                    res.Volume += PrintSettings.BeadArea(0.5 * (tp.Widths[k] + tp.Widths[k - 1]), 0.5 * (tp.Heights[k] + tp.Heights[k - 1])) * tp.Flows[k] * len;
                }
            res.Fill = res.Covered > 0 ? res.Volume / (res.Covered * lh) : 0;
            var runs = set.Zones.SelectMany(z => z.Runs).Where(r => want.Contains(r.Layer)).ToList();
            var near = Near(l, runs, w);
            var grid = new WeaveFinder.Grid(l, 2 * w);
            var step0 = new double[l.N]; var gate = new object(); double tight = 0, total = 0, sum = 0; int far = 0, cnt = 0;
            WeaveFinder.Each(l, l.N, k =>
            {
                step0[k] = -1;
                if (k == 0 || k + 1 >= l.N || !near[k] || !want.Contains(l.Lap[k]) || l.Chain[k] != l.Chain[k - 1] || l.Chain[k] != l.Chain[k + 1]) return;
                double rad = Radius(l, k, 0.35 * w), d = Distance(l, grid, l.X[k], l.Y[k], 2 * w, l.Lap[k], l.H[k], -1);
                lock (gate)
                {
                    total++; if (rad < 0.5 * w) tight++; if (rad < res.MinRadius) res.MinRadius = rad;
                    if (d < 1.99 * w) { sum += d; cnt++; if (d > res.StepMax) res.StepMax = d; if (d > 0.52 * w) far++; }
                }
            });
            if (cnt > 0) { res.StepMean = sum / cnt; res.StepOver = 100.0 * far / cnt; }
            if (total > 0) res.TightShare = 100 * tight / total;
            var sub = new Toolpath { Settings = tp.Settings };
            for (int i = 0; i < flat.Count; i++) if (want.Contains(flat.Layers[i])) sub.AddFrom(flat, i);
            try { res.Crossings = sub.Count > 2 ? LeadEngine.FindCrossings(sub).Count : 0; } catch (DllNotFoundException) { res.Crossings = -1; }
            return res;
        }

        private static bool[] Near(WeaveLayer l, List<WeaveRun> runs, double w)
        {
            var near = new bool[l.N];
            var map = new HashSet<long>(); double c = w;
            foreach (var r in runs)
                for (int k = 0; k < r.Count; k += 2)
                    for (int gx = -1; gx <= 1; gx++) for (int gy = -1; gy <= 1; gy++)
                            map.Add(((long)((int)Math.Floor(r.Mx[k] / c) + gx) << 32) ^ (uint)((int)Math.Floor(r.My[k] / c) + gy));
            for (int i = 0; i < l.N; i++) near[i] = map.Contains(((long)(int)Math.Floor(l.X[i] / c) << 32) ^ (uint)(int)Math.Floor(l.Y[i] / c));
            return near;
        }

        internal static double Radius(WeaveLayer l, int i, double half)
        {
            int a = i, b = i, c = l.Chain[i];
            while (a > 0 && l.Chain[a - 1] == c && l.Arc[i] - l.Arc[a] < half) a--;
            while (b + 1 < l.N && l.Chain[b + 1] == c && l.Arc[b] - l.Arc[i] < half) b++;
            if (a == i || b == i) return 1e9;
            double ux = l.X[i] - l.X[a], uy = l.Y[i] - l.Y[a], vx = l.X[b] - l.X[i], vy = l.Y[b] - l.Y[i], wx = l.X[b] - l.X[a], wy = l.Y[b] - l.Y[a];
            double cr = Math.Abs(ux * vy - uy * vx);
            double pr = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy) * (wx * wx + wy * wy));
            return cr < 1e-12 ? 1e9 : pr / (2 * cr);
        }

        /// <summary>In a spiral: distance to the bead one layer below (dir -1) or above (1).</summary>
        internal static double Distance(WeaveLayer l, WeaveFinder.Grid g, double x, double y, double reach, int lap, double h, int dir)
        {
            double best = reach * reach;
            for (int lz = g.Z0(lap, 2); lz <= g.Z1(lap, 2); lz++)
                for (int gy = g.Gy(y - reach); gy <= g.Gy(y + reach); gy++)
                    for (int gx = g.Gx(x - reach); gx <= g.Gx(x + reach); gx++)
                    {
                        int c = (lz * g.Ny + gy) * g.Nx + gx;
                        for (int q = g.Start(c), e = g.End(c); q < e; q++)
                        {
                            int s = g.Items[q], a = g.SegA[s], b = g.SegB[s];
                            double dx = l.X[b] - l.X[a], dy = l.Y[b] - l.Y[a], l2 = dx * dx + dy * dy, u = l2 < 1e-18 ? 0 : ((x - l.X[a]) * dx + (y - l.Y[a]) * dy) / l2;
                            u = u < 0 ? 0 : u > 1 ? 1 : u;
                            double dh = (l.H[a] + u * (l.H[b] - l.H[a]) - h) * dir;
                            if (dh < 0.5 * l.LayerHeight || dh > 1.5 * l.LayerHeight) continue;
                            double ex = l.X[a] + u * dx - x, ey = l.Y[a] + u * dy - y, d = ex * ex + ey * ey;
                            if (d < best) best = d;
                        }
                    }
            return Math.Sqrt(best);
        }

        internal static double Distance(WeaveLayer l, WeaveFinder.Grid g, double x, double y, double reach)
        {
            double best = reach * reach;
            for (int gy = g.Gy(y - reach); gy <= g.Gy(y + reach); gy++)
                for (int gx = g.Gx(x - reach); gx <= g.Gx(x + reach); gx++)
                {
                    int c = gy * g.Nx + gx;
                    for (int q = g.Start(c), e = g.End(c); q < e; q++)
                    {
                        int s = g.Items[q], a = g.SegA[s], b = g.SegB[s];
                        double dx = l.X[b] - l.X[a], dy = l.Y[b] - l.Y[a], l2 = dx * dx + dy * dy, u = l2 < 1e-18 ? 0 : ((x - l.X[a]) * dx + (y - l.Y[a]) * dy) / l2;
                        u = u < 0 ? 0 : u > 1 ? 1 : u;
                        double ex = l.X[a] + u * dx - x, ey = l.Y[a] + u * dy - y, d = ex * ex + ey * ey;
                        if (d < best) best = d;
                    }
                }
            return Math.Sqrt(best);
        }

        /// <summary>Bead footprints of one layer in a fine raster (a tenth of the bead).</summary>
        private static void Raster(WeaveLayer l, double w, WeaveNumbers p, bool[] keep = null)
        {
            double x0 = l.X.Min() - w, y0 = l.Y.Min() - w, x1 = l.X.Max() + w, y1 = l.Y.Max() + w, cell = w / 12;
            while ((x1 - x0) / cell * ((y1 - y0) / cell) > 3e7) cell *= 1.4;
            int nx = (int)((x1 - x0) / cell) + 1, ny = (int)((y1 - y0) / cell) + 1;
            var first = new float[nx * ny]; var twice = new bool[nx * ny];
            float off = 1e5f * 0.5f;
            for (int i = 1; i < l.N; i++)
            {
                if (l.Chain[i] != l.Chain[i - 1] || keep != null && !keep[i]) continue;
                double ax = l.X[i - 1], ay = l.Y[i - 1], bx = l.X[i], by = l.Y[i], ra = 0.5 * l.W[i - 1], rb = 0.5 * l.W[i], rm = Math.Max(ra, rb);
                double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy; if (l2 < 1e-18) continue;
                int gx0 = (int)((Math.Min(ax, bx) - rm - x0) / cell), gx1 = (int)((Math.Max(ax, bx) + rm - x0) / cell) + 1, gy0 = (int)((Math.Min(ay, by) - rm - y0) / cell), gy1 = (int)((Math.Max(ay, by) + rm - y0) / cell) + 1;
                gx0 = Math.Max(0, gx0); gy0 = Math.Max(0, gy0); gx1 = Math.Min(nx - 1, gx1); gy1 = Math.Min(ny - 1, gy1);
                for (int gy = gy0; gy <= gy1; gy++)
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        double px = x0 + (gx + 0.5) * cell, py = y0 + (gy + 0.5) * cell;
                        double u = ((px - ax) * dx + (py - ay) * dy) / l2; u = u < 0 ? 0 : u > 1 ? 1 : u;
                        double ex = ax + u * dx - px, ey = ay + u * dy - py, r = ra + (rb - ra) * u;
                        if (ex * ex + ey * ey > r * r) continue;
                        int c = gy * nx + gx; float arc = (float)(l.Arc[i - 1] + u * (l.Arc[i] - l.Arc[i - 1])) + off;
                        if (first[c] == 0) first[c] = arc;
                        else if (!twice[c] && Math.Abs(arc - first[c]) > 2.2 * w) twice[c] = true;
                        else if (!twice[c]) first[c] = Math.Max(first[c], Math.Min(arc, first[c] + (float)(0.5 * w)));
                    }
            }
            double area = cell * cell; int reach = (int)Math.Round(0.35 * w / cell);
            int cov = 0, dbl = 0, open = 0;
            for (int gy = 0; gy < ny; gy++)
                for (int gx = 0; gx < nx; gx++)
                {
                    int c = gy * nx + gx;
                    if (first[c] != 0) { cov++; if (twice[c]) dbl++; continue; }
                    // an open place: covered on two opposite sides, close by
                    bool hit = false;
                    for (int dir = 0; dir < 4 && !hit; dir++)
                    {
                        int sx = dir == 0 ? 1 : dir == 1 ? 0 : 1, sy = dir == 0 ? 0 : dir == 1 ? 1 : dir == 2 ? 1 : -1;
                        bool p1 = false, p2 = false;
                        for (int k = 1; k <= reach; k++)
                        {
                            int ax = gx + sx * k, ay = gy + sy * k, bx = gx - sx * k, by = gy - sy * k;
                            if (ax >= 0 && ay >= 0 && ax < nx && ay < ny && first[ay * nx + ax] != 0) p1 = true;
                            if (bx >= 0 && by >= 0 && bx < nx && by < ny && first[by * nx + bx] != 0) p2 = true;
                        }
                        hit = p1 && p2;
                    }
                    if (hit) open++;
                }
            p.Covered = cov * area; p.Doubled = dbl * area; p.Voids = open * area;
        }

        // ------------------------------------------------------------------ the joint
        /// <summary>Height of the contact of two beads side by side: the neck of two bead sections (rectangle with round sides).</summary>
        public static double Neck(double wa, double wb, double height, double spacing)
        {
            double ov = 0.5 * (wa + wb) - spacing;
            if (ov <= 0) return 0;
            double r = 0.5 * height, x = Math.Min(r, 0.5 * ov);
            return 2 * Math.Sqrt(Math.Max(0, r * r - (r - x) * (r - x)));
        }

        internal sealed class Seam
        {
            public WeaveRun R; public int[] K; public double[] Off, Space, WA, WB, Neck; public bool[] Ok; public double H;
        }

        /// <summary>Measures every zone on a path (the zones tell where to look). regions: only there.</summary>
        public static List<WeaveJoint> Joints(Toolpath tp, WeaveZoneSet set, IList<WeaveRegion> regions)
        {
            return From(Seams(tp, set), set, regions, tp.Settings);
        }

        internal static List<WeaveJoint> From(List<Seam> seams, WeaveZoneSet set, IList<WeaveRegion> regions, PrintSettings st)
        {
            var res = new List<WeaveJoint>();
            foreach (var z in set.Zones)
            {
                var mine = seams.Where(s => s.R.Zone == z.Id).ToList();
                res.Add(One(z, mine, regions, st));
            }
            return res;
        }

        internal static List<Seam> Seams(Toolpath tp, WeaveZoneSet set)
        {
            var flat = WeaveFinder.Flat(tp);
            var ranges = WeaveFinder.Split(flat);
            var byLayer = new Dictionary<int, List<WeaveRun>>();
            foreach (var z in set.Zones) foreach (var r in z.Runs) { if (!byLayer.TryGetValue(r.Layer, out var l)) byLayer[r.Layer] = l = new List<WeaveRun>(); l.Add(r); }
            double w = tp.Settings.BeadWidth, reach = 2.2 * w;
            int every = Math.Max(1, (int)Math.Round(0.2 * w / set.Step));
            var res = new List<Seam>[ranges.Count];
            Parallel.For(0, ranges.Count, i =>
            {
                List<WeaveRun> runs;
                if (ranges[i].Climb) runs = byLayer.Values.SelectMany(v => v).ToList();
                else if (!byLayer.TryGetValue(ranges[i].Layer, out runs)) return;
                // only the path points themselves: straight moves stay long
                var l = WeaveFinder.Sample(flat, ranges[i], 1e9);
                var grid = new WeaveFinder.Grid(l, reach);
                res[i] = new List<Seam>();
                var gate = new object();
                Action<WeaveRun> one = r =>
                {
                    var hits = new List<double[]>();
                    int m = (r.Count + every - 1) / every;
                    var s = new Seam { R = r, K = new int[m], Off = new double[m], Space = new double[m], WA = new double[m], WB = new double[m], Neck = new double[m], Ok = new bool[m] };
                    double hs = 0; int hc = 0;
                    for (int q = 0; q < m; q++)
                    {
                        int k = Math.Min(r.Count - 1, q * every); s.K[q] = k;
                        double cx = r.Mx[k], cy = r.My[k], nx = r.Nx[k], ny = r.Ny[k], g = r.Gap[k];
                        hits.Clear();
                        double mh = 0;
                        if (l.Climb) { var pm = flat == tp ? r.Mid[k] : tp.Warp.ToFlat(r.Mid[k]); l.Frame.To(pm, out _, out _, out mh); }
                        for (int lz = grid.Z0(r.Layer, 1); lz <= grid.Z1(r.Layer, 2); lz++)
                        for (int gy = grid.Gy(cy - reach); gy <= grid.Gy(cy + reach); gy++)
                            for (int gx = grid.Gx(cx - reach); gx <= grid.Gx(cx + reach); gx++)
                            {
                                int c = (lz * grid.Ny + gy) * grid.Nx + gx;
                                for (int p = grid.Start(c), e = grid.End(c); p < e; p++)
                                {
                                    int sg = grid.Items[p], a = grid.SegA[sg], b = grid.SegB[sg];
                                    double ua = (l.X[a] - cx) * -ny + (l.Y[a] - cy) * nx, ub = (l.X[b] - cx) * -ny + (l.Y[b] - cy) * nx; // along the seam
                                    if (ua * ub > 0 || Math.Abs(ua - ub) < 1e-12) continue;
                                    double t = ua / (ua - ub), va = (l.X[a] - cx) * nx + (l.Y[a] - cy) * ny, vb = (l.X[b] - cx) * nx + (l.Y[b] - cy) * ny, v = va + (vb - va) * t;
                                    if (Math.Abs(v) > reach) continue;
                                    if (l.Climb && Math.Abs(l.H[a] + (l.H[b] - l.H[a]) * t - mh) > 0.45 * l.LayerHeight) continue;
                                    double dx = l.X[b] - l.X[a], dy = l.Y[b] - l.Y[a], cos = Math.Abs(dx * -ny + dy * nx) / Math.Sqrt(dx * dx + dy * dy);
                                    int src = l.Src[b];
                                    double h = src > 0 ? 0.5 * (flat.Heights[src] + (flat.Flows[src - 1] > 0 ? flat.Heights[src - 1] : flat.Heights[src])) : flat.Heights[src];
                                    hits.Add(new[] { v, l.W[a] + (l.W[b] - l.W[a]) * t, h, cos });
                                }
                            }
                        double best = 1e9; double[] A = null, B = null;
                        foreach (var ha in hits)
                            foreach (var hb in hits)
                            {
                                if (hb[0] - ha[0] < 0.4 * g) continue;
                                double cost = Math.Abs(hb[0] - ha[0] - g) + 0.3 * Math.Abs(hb[0] + ha[0]);
                                if (cost < best) { best = cost; A = ha; B = hb; }
                            }
                        if (A == null || best > 0.6 * w) continue;
                        if (A[3] < 0.6 || B[3] < 0.6) continue; // a bend, not the neighbour
                        double cs = 0.5 * (A[3] + B[3]);
                        s.Ok[q] = true;
                        // along the crossing line the widths look larger on a slope
                        s.Off[q] = 0.5 * (A[0] + 0.5 * A[1] / A[3] + B[0] - 0.5 * B[1] / B[3]);
                        s.Space[q] = (B[0] - A[0]) * cs; s.WA[q] = A[1]; s.WB[q] = B[1];
                        s.Neck[q] = Neck(A[1], B[1], 0.5 * (A[2] + B[2]), s.Space[q]);
                        hs += 0.5 * (A[2] + B[2]); hc++;
                    }
                    s.H = hc > 0 ? hs / hc : tp.Settings.LayerHeight;
                    lock (gate) res[i].Add(s);
                };
                if (l.Climb) Parallel.ForEach(runs, one); else foreach (var r in runs) one(r);
            });
            return res.Where(v => v != null).SelectMany(v => v).ToList();
        }

        private static WeaveJoint One(WeaveZone z, List<Seam> seams, IList<WeaveRegion> regions, PrintSettings st)
        {
            var j = new WeaveJoint { Zone = z.Id, SpacingMin = 1e9, OverlapMin = 1e9, OverlapMax = -1e9 };
            var layers = new HashSet<int>();
            double sp = 0, ov = 0, wave = 0, straight = 0, depth = 0, depthL = 0; var ovs = new List<double>(); var shelves = new List<double>();
            var byLayer = seams.GroupBy(s => s.R.Layer).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in seams)
            {
                byLayer.TryGetValue(s.R.Layer + 1, out var above);
                for (int q = 0; q < s.K.Length; q++)
                {
                    if (regions != null && regions.Count > 0 && !regions.Any(g => g.Contains(s.R.Mid[s.K[q]]))) continue;
                    j.Stations++;
                    if (!s.Ok[q]) { j.Missed++; continue; }
                    double ds = 0.5 * (s.R.S[s.K[Math.Min(s.K.Length - 1, q + 1)]] - s.R.S[s.K[Math.Max(0, q - 1)]]);
                    if (ds <= 0) continue;
                    layers.Add(s.R.Layer);
                    int a = Math.Max(0, q - 1), b = Math.Min(s.K.Length - 1, q + 1);
                    double dl = s.Ok[a] && s.Ok[b] ? Math.Sqrt(Math.Pow(s.R.S[s.K[b]] - s.R.S[s.K[a]], 2) + Math.Pow(s.Off[b] - s.Off[a], 2)) * (2 * ds / Math.Max(1e-9, s.R.S[s.K[b]] - s.R.S[s.K[a]])) / 2 : ds;
                    j.Length += ds; j.Face += ds * s.H; wave += dl; straight += ds;
                    j.Vertical += s.Neck[q] * dl;
                    sp += s.Space[q] * ds; j.SpacingMin = Math.Min(j.SpacingMin, s.Space[q]); j.SpacingMax = Math.Max(j.SpacingMax, s.Space[q]);
                    double o = 0.5 * (s.WA[q] + s.WB[q]) - s.Space[q]; ov += o * ds; ovs.Add(o);
                    // the seam of the layer above
                    if (above == null) continue;
                    double shelf = Step(s, q, above);
                    if (shelf < 0) continue;
                    j.Horizontal += shelf * ds; depth += shelf * ds; depthL += ds; shelves.Add(shelf);
                }
            }
            j.Layers = layers.Count;
            // 1 and 99 % (a few stations at the ends of a zone see a bend instead of the neighbour)
            if (shelves.Count > 0) { shelves.Sort(); j.DepthMax = shelves[(int)(0.99 * (shelves.Count - 1))]; }
            if (ovs.Count > 0) { ovs.Sort(); j.OverlapMin = ovs[(int)(0.01 * (ovs.Count - 1))]; j.OverlapMax = ovs[(int)(0.99 * (ovs.Count - 1))]; }
            if (straight > 0) { j.Spacing = sp / straight; j.Overlap = ov / straight; j.WaveFactor = wave / straight; }
            if (depthL > 0) j.Depth = depth / depthL;
            if (j.Face > 0) { j.VerticalShare = j.Vertical / j.Face; j.HorizontalShare = j.Horizontal / j.Face; }
            return j;
        }

        /// <summary>Sideways step of the seam to the layer above, at a station; -1 = there is none.</summary>
        internal static double Step(Seam s, int q, List<Seam> above)
        {
            int k = s.K[q]; double zs = s.R.ZoneS(k), x = s.R.Mx[k], y = s.R.My[k], best = 1e9; Seam bs = null; int bq = -1;
            foreach (var t in above)
            {
                int m = t.K.Length; if (m < 2) continue;
                // the station with the nearest zone coordinate
                double s0 = t.R.ZoneS(t.K[0]), s1 = t.R.ZoneS(t.K[m - 1]);
                int lo = 0, hi = m - 1;
                while (hi - lo > 1) { int mi = (lo + hi) >> 1; if (s1 > s0 ? t.R.ZoneS(t.K[mi]) <= zs : t.R.ZoneS(t.K[mi]) >= zs) lo = mi; else hi = mi; }
                int g = lo;
                for (int p = Math.Max(0, g - 12); p <= Math.Min(m - 1, g + 12); p++)
                {
                    if (!t.Ok[p]) continue;
                    double dx = t.R.Mx[t.K[p]] - x, dy = t.R.My[t.K[p]] - y, d = dx * dx + dy * dy;
                    if (d < best) { best = d; bs = t; bq = p; }
                }
            }
            if (bs == null) return -1;
            int bk = bs.K[bq];
            double nx = s.R.Nx[k], ny = s.R.Ny[k], dot = nx * bs.R.Nx[bk] + ny * bs.R.Ny[bk];
            double lean = (bs.R.Mx[bk] - x) * nx + (bs.R.My[bk] - y) * ny, along = (bs.R.Mx[bk] - x) * -ny + (bs.R.My[bk] - y) * nx;
            if (Math.Abs(dot) < 0.85 || Math.Abs(along) > 2 || Math.Abs(lean) > 0.75 * (s.WA[q] + s.WB[q])) return -1;
            // the offset of the seam above at the same place: between its stations
            double off = bs.Off[bq];
            foreach (int p in new[] { bq - 1, bq + 1 })
                if (p >= 0 && p < bs.K.Length && bs.Ok[p])
                {
                    double al = (bs.R.Mx[bs.K[p]] - x) * -ny + (bs.R.My[bs.K[p]] - y) * nx;
                    if (al * along <= 0) { double t = Math.Abs(al - along) > 1e-9 ? along / (along - al) : 0; off = bs.Off[bq] + (bs.Off[p] - bs.Off[bq]) * t; break; }
                }
            return Math.Abs(lean + (dot > 0 ? off : -off) - s.Off[q]);
        }

        public static WeaveJoint Sum(List<WeaveJoint> list)
        {
            var r = new WeaveJoint { Zone = -1, SpacingMin = 1e9, OverlapMin = 1e9, OverlapMax = -1e9 };
            double sp = 0, ov = 0, wf = 0, d = 0;
            foreach (var j in list)
            {
                r.Length += j.Length; r.Face += j.Face; r.Vertical += j.Vertical; r.Horizontal += j.Horizontal; r.Missed += j.Missed; r.Stations += j.Stations;
                sp += j.Spacing * j.Length; ov += j.Overlap * j.Length; wf += j.WaveFactor * j.Length; d += j.Depth * j.Length;
                r.DepthMax = Math.Max(r.DepthMax, j.DepthMax); r.Layers = Math.Max(r.Layers, j.Layers);
                if (j.Length > 0) { r.SpacingMin = Math.Min(r.SpacingMin, j.SpacingMin); r.SpacingMax = Math.Max(r.SpacingMax, j.SpacingMax); r.OverlapMin = Math.Min(r.OverlapMin, j.OverlapMin); r.OverlapMax = Math.Max(r.OverlapMax, j.OverlapMax); }
            }
            if (r.Length > 0) { r.Spacing = sp / r.Length; r.Overlap = ov / r.Length; r.WaveFactor = wf / r.Length; r.Depth = d / r.Length; }
            if (r.Face > 0) { r.VerticalShare = r.Vertical / r.Face; r.HorizontalShare = r.Horizontal / r.Face; }
            return r;
        }
    }

    // ====================================================================== variants side by side
    public sealed class WeaveVariant
    {
        public string Name; public WeaveSettings Settings; public WeaveResult Result; public WeaveNumbers Numbers; public WeaveJoint Joint;
        public double Length, Volume, Minutes; public int Points; public JointSprings Springs; public string Remark = "";
    }

    /// <summary>The same part woven in different ways, measured the same way, in one table.</summary>
    public static class WeaveVariants
    {
        /// <summary>The standard comparison. a = amplitude in mm (0 = a quarter of the bead).</summary>
        public static List<KeyValuePair<string, WeaveSettings>> Standard(double w, double a = 0)
        {
            if (a <= 0) a = 0.25 * w;
            var r = new List<KeyValuePair<string, WeaveSettings>>();
            Action<string, WeaveSettings> add = (n, v) => r.Add(new KeyValuePair<string, WeaveSettings>(n, v));
            add("V0 as sliced", null);
            add("V1 fit widths only", new WeaveSettings { Pattern = WeavePattern.Plain });
            add("V2 shift, walls keep face", new WeaveSettings { Pattern = WeavePattern.Shift, Amplitude = a });
            add("V3 shift, beads move", new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = a });
            add("V4 shift, beads move, small", new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = 0.5 * a });
            add("V5 shift without fit", new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = a, Fit = false });
            add("V6 width, outer faces keep", new WeaveSettings { Pattern = WeavePattern.Width, Amplitude = a });
            add("V7 snake woven (0.5)", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Move, Amplitude = a, Phase = 0.5 });
            add("V8 snake helix (0.25)", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Move, Amplitude = a, Phase = 0.25 });
            add("V9 snake, faces keep", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Keep, Amplitude = a, Phase = 0.5 });
            add("V10 teeth", new WeaveSettings { Pattern = WeavePattern.Teeth, Faces = WeaveFaces.Move, Amplitude = a, Wavelength = 50 });
            add("V11 teeth, faces keep", new WeaveSettings { Pattern = WeavePattern.Teeth, Faces = WeaveFaces.Keep, Amplitude = a, Wavelength = 50 });
            add("V12 custom 0,1,0,-1", new WeaveSettings { Pattern = WeavePattern.Custom, Faces = WeaveFaces.Move, Amplitude = a });
            add("V13 shift every 2 layers", new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = a, LayerPeriod = 2 });
            return r;
        }

        public static List<WeaveVariant> Run(Toolpath tp, WeaveZoneSet set, IList<KeyValuePair<string, WeaveSettings>> list, int maxLayers, JointMaterial m)
        {
            var res = new List<WeaveVariant>();
            var layers = WeaveCheck.Pick(set, maxLayers);
            foreach (var kv in list)
            {
                Cancel.Check();
                var v = new WeaveVariant { Name = kv.Key, Settings = kv.Value };
                v.Result = kv.Value == null ? new WeaveResult { Path = tp, Set = set, WidthLow = tp.Settings.BeadWidth, WidthHigh = tp.Settings.BeadWidth } : Weave.Apply(tp, set, kv.Value);
                var p = v.Result.Path;
                v.Numbers = WeaveCheck.Measure(p, set, layers);
                v.Joint = v.Numbers.Joint = WeaveCheck.Sum(WeaveCheck.Joints(p, set, null));
                v.Springs = JointSprings.Of(v.Joint, tp.Settings.LayerHeight, m ?? new JointMaterial());
                v.Points = p.Count;
                for (int i = 1; i < p.Count; i++)
                {
                    double x = p.Points[i].X - p.Points[i - 1].X, y = p.Points[i].Y - p.Points[i - 1].Y, z = p.Points[i].Z - p.Points[i - 1].Z, len = Math.Sqrt(x * x + y * y + z * z);
                    bool print = p.Flows[i] > 0;
                    if (print) { v.Length += len; v.Volume += PrintSettings.BeadArea(0.5 * (p.Widths[i] + p.Widths[i - 1]), 0.5 * (p.Heights[i] + p.Heights[i - 1])) * p.Flows[i] * len; }
                    v.Minutes += len / Math.Max(1, print ? p.Settings.Speed : p.Settings.MaxSpeed);
                }
                res.Add(v);
            }
            return res;
        }

        public static List<string> Table(IList<WeaveVariant> list)
        {
            var r = new List<string> { "variant                      | side  inter- depth max | over- worst gap | cover. open | cross bend  | step  over  | length material time | width       points | k_along r_along", "                             | share lock   mm        | lap mm   mm     | twice% mm2  |       mm    | mm    %     | m      cm3      min  | mm                 | N/mm3   N/mm2 (estimate)" };
            foreach (var v in list)
            {
                var j = v.Joint; var n = v.Numbers;
                r.Add(Inv.F($"{v.Name,-28} | {j.VerticalShare,5:0.000} {j.HorizontalShare,5:0.000} {j.Depth,5:0.00} {j.DepthMax,5:0.00} | {j.Overlap,5:0.00} {j.OverlapMax,5:0.00} {Math.Min(0, j.OverlapMin),5:0.00} | {100 * n.Doubled / Math.Max(1, n.Covered),6:0.00} {n.Voids,5:0} | {n.Crossings,4} {Math.Min(99, n.MinRadius),6:0.00} | {n.StepMax,5:0.00} {n.StepOver,4:0.0} | {v.Length / 1000,6:0.0} {v.Volume / 1000,7:0.0} {v.Minutes,6:0.0} | {v.Result.WidthLow,4:0.00}-{v.Result.WidthHigh,4:0.00} {v.Points,8} | {v.Springs.KAlong,6:0} {v.Springs.RAlong,6:0.0}"));
            }
            return r;
        }

        public static List<string> Csv(IList<WeaveVariant> list)
        {
            var r = new List<string> { "variant,side_contact_mm2,interlock_contact_mm2,joint_face_mm2,side_share,interlock_share,depth_mean_mm,depth_max_mm,overlap_mean_mm,overlap_p99_mm,overlap_p1_mm,covered_mm2,covered_twice_mm2,open_mm2,crossings,min_bend_radius_mm,step_max_mm,step_mean_mm,step_over_percent,length_m,material_cm3,time_min,width_min_mm,width_max_mm,points,woven_m,k_open,k_along,k_up,r_open,r_along,r_up" };
            foreach (var v in list)
            {
                var j = v.Joint; var n = v.Numbers; var s = v.Springs;
                r.Add(Inv.F($"\"{v.Name}\",{j.Vertical:0.#},{j.Horizontal:0.#},{j.Face:0.#},{j.VerticalShare:0.####},{j.HorizontalShare:0.####},{j.Depth:0.###},{j.DepthMax:0.###},{j.Overlap:0.###},{j.OverlapMax:0.###},{j.OverlapMin:0.###},{n.Covered:0},{n.Doubled:0},{n.Voids:0},{n.Crossings},{n.MinRadius:0.##},{n.StepMax:0.###},{n.StepMean:0.###},{n.StepOver:0.##},{v.Length / 1000:0.##},{v.Volume / 1000:0.##},{v.Minutes:0.##},{v.Result.WidthLow:0.##},{v.Result.WidthHigh:0.##},{v.Points},{v.Result.WovenLength / 1000:0.##},{s.KOpen:0.##},{s.KAlong:0.##},{s.KUp:0.##},{s.ROpen:0.##},{s.RAlong:0.##},{s.RUp:0.##}"));
            }
            return r;
        }
    }

    // ====================================================================== joint data for IGA and experiments
    public sealed class JointDataOptions
    {
        public JointMaterial Material = new JointMaterial();
        /// <summary>Distance of the point pairs, mm, along the seam and up.</summary>
        public double PairSpacing = 10;
        /// <summary>For alpha of the IGA coupling: shell modulus, Poisson's ratio, thickness (0 = bead width), element size.</summary>
        public double E = 2400, Nu = 0.35, Thickness = 0, ElementSize = 10;
        public string Name = "";
    }

    /// <summary>
    /// A joint as a patch link, the way a shell analysis models near-touching mid surfaces (Detect Patch Link
    /// Regions: contact where d is at most (tA + tB) / 2 + tolerance; a strip between a line on A and a line
    /// on B, both running in build direction). One row per zone and height band: the points of the two
    /// lines and the thickness the strip needs to be as stiff as the joint.
    /// </summary>
    public struct JointLink
    {
        public int Zone, LayerFrom, LayerTo; public Point3d A, B; public double Gap, Height, Length;
        /// <summary>Strip thickness (along the beads) for the opening stiffness, the shear up and down, and the shear along the beads (bending of the strip).</summary>
        public double TOpen, TUp, TAlong;
        /// <summary>Capacity per mm of height, N/mm.</summary>
        public double ROpen, RAlong, RUp;
    }

    /// <summary>One pair of points on the two mid surfaces, with the area it stands for.</summary>
    public struct JointPair { public int Zone, Layer; public Point3d A, B; public double Area, KOpen, KAlong, KUp, ROpen, RAlong, RUp, S; }

    /// <summary>
    /// The joints as data. The surfaces of the analysis stay smooth: a joint is a coupling between
    /// two patches with a stiffness and a capacity per zone.
    /// </summary>
    public sealed class JointData
    {
        public List<WeaveJoint> Joints; public List<JointPair> Pairs = new List<JointPair>(); public List<JointLink> Links = new List<JointLink>();
        /// <summary>Optional: from the print bed back to the model (the points are written there).</summary>
        public Func<Point3d, Point3d> Back;
        public WeaveZoneSet Set; public Toolpath Path; public JointDataOptions Options;

        public static JointData Compute(Toolpath woven, WeaveZoneSet set, JointDataOptions o, Func<Point3d, Point3d> back = null)
        {
            var d = new JointData { Set = set, Path = woven, Options = o ?? new JointDataOptions(), Back = back };
            if (back == null) back = p => p;
            o = d.Options;
            var seams = WeaveCheck.Seams(woven, set);
            d.Joints = WeaveCheck.Joints(woven, set, null);
            double h = woven.Settings.LayerHeight, w = woven.Settings.BeadWidth;
            int everyLayer = Math.Max(1, (int)Math.Round(o.PairSpacing / Math.Max(0.1, h)));
            foreach (var z in set.Zones)
            {
                var mine = seams.Where(s => s.R.Zone == z.Id).ToList();
                var layers = mine.Select(s => s.R.Layer).Distinct().OrderBy(v => v).ToList();
                var by = mine.GroupBy(s => s.R.Layer).ToDictionary(g => g.Key, g => g.ToList());
                for (int band = 0; band < layers.Count; band += everyLayer)
                {
                    // a band of layers: bins along the zone
                    var bins = new SortedDictionary<int, double[]>(); // len, vert, horiz, spacing
                    var rep = new Dictionary<int, double[]>(); // distance to the middle layer, A, B
                    int mid = layers[Math.Min(layers.Count - 1, band + everyLayer / 2)];
                    for (int q = band; q < Math.Min(layers.Count, band + everyLayer); q++)
                        foreach (var s in by[layers[q]])
                        {
                            by.TryGetValue(layers[q] + 1, out var above);
                            for (int i = 0; i < s.K.Length; i++)
                            {
                                if (!s.Ok[i]) continue;
                                double ds = 0.5 * (s.R.S[s.K[Math.Min(s.K.Length - 1, i + 1)]] - s.R.S[s.K[Math.Max(0, i - 1)]]);
                                double zs = s.R.ZoneS(s.K[i]); int bin = (int)Math.Floor(zs / o.PairSpacing);
                                if (!bins.TryGetValue(bin, out var v)) bins[bin] = v = new double[5];
                                double step = above != null ? Math.Max(0, WeaveCheck.Step(s, i, above)) : 0;
                                v[0] += ds; v[1] += ds * s.Neck[i]; v[2] += ds * step; v[3] += ds * s.Space[i]; v[4] += ds * s.H;
                                double far = Math.Abs(layers[q] - mid) * 100 + Math.Abs(zs - (bin + 0.5) * o.PairSpacing);
                                if (!rep.TryGetValue(bin, out var rp) || far < rp[0])
                                    rep[bin] = new[] { far, s.R.PA[s.K[i]].X, s.R.PA[s.K[i]].Y, s.R.PA[s.K[i]].Z, s.R.PB[s.K[i]].X, s.R.PB[s.K[i]].Y, s.R.PB[s.K[i]].Z, layers[q], zs };
                            }
                        }
                    // the whole band as one link, at the place where the beads are nearest
                    double len = 0, vert = 0, hor = 0, spc = 0, face = 0, hh = 0; WeaveRun lr = null; double bestGap = 1e9; int lk = 0;
                    foreach (var v in bins.Values) { len += v[0]; vert += v[1]; hor += v[2]; spc += v[3]; face += v[4]; }
                    foreach (var s in by[mid]) for (int k = 0; k < s.R.Count; k++) { double g = s.R.Gap[k] + 0.002 * Math.Abs(s.R.ZoneS(k) - s.R.Centre); if (g < bestGap) { bestGap = g; lr = s.R; lk = k; } }
                    int lays = Math.Min(layers.Count, band + everyLayer) - band; hh = lays * h;
                    if (face > 0 && lr != null)
                    {
                        var j = new WeaveJoint { VerticalShare = vert / face, HorizontalShare = hor / face, Spacing = spc / len };
                        var sp = JointSprings.Of(j, h, o.Material); double L = len / lays, g = lr.Gap[lk], gl = o.E / (2 * (1 + o.Nu));
                        d.Links.Add(new JointLink
                        {
                            Zone = z.Id, LayerFrom = layers[band], LayerTo = layers[band + lays - 1], A = back(lr.PA[lk]), B = back(lr.PB[lk]), Gap = g, Height = hh, Length = L,
                            TOpen = sp.KOpen * L * g / o.E, TUp = sp.KUp * L * g / gl, TAlong = Math.Pow(sp.KAlong * L * g * g * g * (1 - o.Nu * o.Nu) / o.E, 1.0 / 3),
                            ROpen = sp.ROpen * L, RAlong = sp.RAlong * L, RUp = sp.RUp * L
                        });
                    }
                    foreach (var kv in bins)
                    {
                        var v = kv.Value; if (v[4] <= 0) continue;
                        var j = new WeaveJoint { VerticalShare = v[1] / v[4], HorizontalShare = v[2] / v[4], Spacing = v[3] / v[0] };
                        var sp = JointSprings.Of(j, h, o.Material); var rp = rep[kv.Key];
                        Point3d a = back(new Point3d(rp[1], rp[2], rp[3])), b = back(new Point3d(rp[4], rp[5], rp[6]));
                        d.Pairs.Add(new JointPair { Zone = z.Id, Layer = (int)rp[7], A = a, B = b, Area = v[4], KOpen = sp.KOpen, KAlong = sp.KAlong, KUp = sp.KUp, ROpen = sp.ROpen, RAlong = sp.RAlong, RUp = sp.RUp, S = rp[8] });
                    }
                }
            }
            return d;
        }

        /// <summary>Only these zones.</summary>
        public void Keep(HashSet<int> ids)
        {
            Joints = Joints.Where(j => ids.Contains(j.Zone)).ToList(); Pairs = Pairs.Where(p => ids.Contains(p.Zone)).ToList(); Links = Links.Where(l => ids.Contains(l.Zone)).ToList();
        }

        /// <summary>Penalty alpha of cc_iga for a point pair: k A = alpha E t / ((1 - nu^2) h).</summary>
        public double Alpha(double k, double area)
        {
            double t = Options.Thickness > 0 ? Options.Thickness : Path.Settings.BeadWidth;
            return k * area * (1 - Options.Nu * Options.Nu) * Options.ElementSize / Math.Max(1e-9, Options.E * t);
        }

        private static string P(Point3d p) => Inv.F($"[{p.X:0.###},{p.Y:0.###},{p.Z:0.###}]");

        public List<string> ZoneCsv()
        {
            var r = new List<string> { "zone,roles,class,first_layer,last_layer,layers,mean_length_mm,total_length_mm,gap_mm,spacing_mm,overlap_mm,joint_face_mm2,side_contact_mm2,interlock_contact_mm2,side_contact_per_m_mm2,interlock_contact_per_m_mm2,side_share,interlock_share,interlock_depth_mean_mm,interlock_depth_max_mm,wave_factor,k_open_N_mm3,k_along_N_mm3,k_up_N_mm3,r_open_N_mm2,r_along_N_mm2,r_up_N_mm2,values" };
            double h = Path.Settings.LayerHeight;
            foreach (var j in Joints)
            {
                var z = Set.Zones[j.Zone]; var s = JointSprings.Of(j, h, Options.Material);
                r.Add(Inv.F($"{z.Id},{z.Roles},{z.Class},{z.FirstLayer},{z.LastLayer},{z.LayerCount},{z.MeanLength:0.##},{z.TotalLength:0.##},{z.GapMean:0.###},{j.Spacing:0.###},{j.Overlap:0.###},{j.Face:0.#},{j.Vertical:0.#},{j.Horizontal:0.#},{j.VerticalPerMetre(z.MeanLength):0.#},{j.HorizontalPerMetre(z.MeanLength):0.#},{j.VerticalShare:0.####},{j.HorizontalShare:0.####},{j.Depth:0.###},{j.DepthMax:0.###},{j.WaveFactor:0.####},{s.KOpen:0.###},{s.KAlong:0.###},{s.KUp:0.###},{s.ROpen:0.###},{s.RAlong:0.###},{s.RUp:0.###},{(Options.Material.Placeholder ? "estimate from placeholder material" : "estimate")}"));
            }
            return r;
        }

        public List<string> PairCsv()
        {
            var r = new List<string> { "zone,layer,s_mm,ax,ay,az,bx,by,bz,area_mm2,k_open_N_mm3,k_along_N_mm3,k_up_N_mm3,r_open_N_mm2,r_along_N_mm2,r_up_N_mm2,alpha_along" };
            foreach (var p in Pairs)
                r.Add(Inv.F($"{p.Zone},{p.Layer},{p.S:0.##},{p.A.X:0.###},{p.A.Y:0.###},{p.A.Z:0.###},{p.B.X:0.###},{p.B.Y:0.###},{p.B.Z:0.###},{p.Area:0.##},{p.KOpen:0.###},{p.KAlong:0.###},{p.KUp:0.###},{p.ROpen:0.###},{p.RAlong:0.###},{p.RUp:0.###},{Alpha(p.KAlong, p.Area):0.######}"));
            return r;
        }

        public List<string> LinkCsv()
        {
            var r = new List<string> { "zone,layer_from,layer_to,ax,ay,az,bx,by,bz,gap_mm,height_mm,seam_length_mm,t_link_open_mm,t_link_up_mm,t_link_along_mm,r_open_N_mm,r_along_N_mm,r_up_N_mm" };
            foreach (var l in Links)
                r.Add(Inv.F($"{l.Zone},{l.LayerFrom},{l.LayerTo},{l.A.X:0.###},{l.A.Y:0.###},{l.A.Z:0.###},{l.B.X:0.###},{l.B.Y:0.###},{l.B.Z:0.###},{l.Gap:0.###},{l.Height:0.##},{l.Length:0.##},{l.TOpen:0.##},{l.TUp:0.##},{l.TAlong:0.##},{l.ROpen:0.##},{l.RAlong:0.##},{l.RUp:0.##}"));
            return r;
        }

        public const string Rule =
            "One zone = one coupling between two patches. The patches stay smooth: points a lie on the mid surface of patch 1, points b on patch 2 (bead centre lines before weaving). " +
            "Shell analysis: one Conn of type MP_point per zone (is_region_boundary = true, region_id = zone id, uv_params_patch1/2 = closest points of the pairs a / b on the two patches, " +
            "search_par maximum distance a little above gap_max). Solver (stiff_mat_multipatch_point_surf_pen): spring per pair = alpha E t / ((1 - nu^2) h_el), no integration weight, so alpha = k x area x (1 - nu^2) x h_el / (E t). " +
            "A single alpha holds for all three directions: take k_along (shear governs) or the smallest. Forces: spring per pair x relative displacement of a and b, split into opening / along / up; divide by area and compare with r_open, r_along, r_up (utilisation = sum of squares <= 1). " +
            "As patch links (Detect Patch Link Regions, Fusion Thickness): one strip per zone between the two lines of patch_links (build direction), Fusion Thickness = t_link_open (shell modulus E as given, E multiplier 1), coupled to both patches with the usual penalty; t_link_up and t_link_along show how far the same strip misses the two shear stiffnesses. Today the component takes one thickness for all links: use one value per group of zones, or the mean. Contact criterion of the component: d <= (tA + tB) / 2 + tolerance; with t = bead width and tolerance = Further x bead width it finds the same zones. " +
            "Zones above 1 get a denser or deeper pattern, zones far below can stay plain. All k and r are estimates until the coupons are tested: k = measured slope, r = measured strength per joint type.";

        public string Json()
        {
            var b = new StringBuilder(); double h = Path.Settings.LayerHeight; var m = Options.Material;
            b.Append("{\n \"format\": \"larger-joint-data-1\",\n \"units\": \"mm, N, MPa\",\n");
            b.Append(Inv.F($" \"name\": \"{Esc(Options.Name)}\",\n \"bead_width\": {Path.Settings.BeadWidth:0.###}, \"layer_height\": {h:0.###}, \"bead_spacing\": {Path.Settings.BeadSpacing:0.###},\n"));
            b.Append(Inv.F($" \"material\": {{\"placeholder\": {(m.Placeholder ? "true" : "false")}, \"E_across\": {m.EAcross:0.#}, \"E_layers\": {m.ELayers:0.#}, \"G\": {m.G:0.#}, \"G_layers\": {m.GLayers:0.#}, \"sigma_side\": {m.SigmaSide:0.##}, \"tau_side\": {m.TauSide:0.##}, \"sigma_layer\": {m.SigmaLayer:0.##}, \"tau_layer\": {m.TauLayer:0.##}}},\n"));
            b.Append(" \"stiffness_model\": \"springs side by side per mm2 of joint face: k_open = E_across v / g + G_layers i / h, k_along = G v / g + G_layers i / h, k_up = G v / g + E_layers i / h (v = side contact share, i = interlock share, g = spacing, h = layer height); r = strength x share, added\",\n");
            b.Append(" \"iga_rule\": \"" + Esc(Rule) + "\",\n \"zones\": [\n");
            for (int q = 0; q < Joints.Count; q++)
            {
                var j = Joints[q]; var z = Set.Zones[j.Zone]; var s = JointSprings.Of(j, h, m);
                b.Append(Inv.F($"  {{\"id\": {z.Id}, \"roles\": \"{z.Roles}\", \"class\": \"{z.Class}\", \"layers\": [{z.FirstLayer},{z.LastLayer}], \"layer_count\": {z.LayerCount}, \"length\": {z.MeanLength:0.##}, \"total_length\": {z.TotalLength:0.##}, \"gap\": {z.GapMean:0.###}, \"gap_min\": {z.GapMin:0.###}, \"gap_max\": {z.GapMax:0.###},\n"));
                b.Append(Inv.F($"   \"spacing\": {j.Spacing:0.###}, \"overlap\": {j.Overlap:0.###}, \"joint_face_mm2\": {j.Face:0.#}, \"side_contact_mm2\": {j.Vertical:0.#}, \"interlock_contact_mm2\": {j.Horizontal:0.#}, \"side_contact_per_m\": {j.VerticalPerMetre(z.MeanLength):0.#}, \"interlock_contact_per_m\": {j.HorizontalPerMetre(z.MeanLength):0.#}, \"side_share\": {j.VerticalShare:0.####}, \"interlock_share\": {j.HorizontalShare:0.####}, \"interlock_depth\": {j.Depth:0.###}, \"interlock_depth_max\": {j.DepthMax:0.###}, \"wave_factor\": {j.WaveFactor:0.####},\n"));
                b.Append(Inv.F($"   \"k_open\": {s.KOpen:0.###}, \"k_along\": {s.KAlong:0.###}, \"k_up\": {s.KUp:0.###}, \"r_open\": {s.ROpen:0.###}, \"r_along\": {s.RAlong:0.###}, \"r_up\": {s.RUp:0.###},\n"));
                // the seam polylines of the lowest, middle and highest layer
                var ls = z.Runs.GroupBy(r => r.Layer).OrderBy(g => g.Key).ToList();
                b.Append("   \"seams\": [");
                var pick = new SortedSet<int> { 0, ls.Count / 2, ls.Count - 1 }; bool first = true;
                foreach (int i in pick)
                    foreach (var run in ls[i])
                    {
                        if (!first) b.Append(","); first = false;
                        b.Append(Inv.F($"\n    {{\"layer\": {run.Layer}, \"mid\": ["));
                        int every = Math.Max(1, (int)(Options.PairSpacing / Math.Max(0.1, Set.Step)));
                        for (int k = 0; ; k = Math.Min(run.Count - 1, k + every)) { b.Append(P(Back != null ? Back(run.Mid[k]) : run.Mid[k])); if (k >= run.Count - 1) break; b.Append(","); }
                        b.Append("]}");
                    }
                b.Append("],\n   \"patch_links\": [");
                first = true;
                foreach (var l in Links)
                {
                    if (l.Zone != z.Id) continue;
                    if (!first) b.Append(","); first = false;
                    b.Append(Inv.F($"\n    {{\"layers\": [{l.LayerFrom},{l.LayerTo}], \"a\": ") + P(l.A) + ", \"b\": " + P(l.B) + Inv.F($", \"gap\": {l.Gap:0.###}, \"height\": {l.Height:0.##}, \"seam_length\": {l.Length:0.##}, \"t_link_open\": {l.TOpen:0.##}, \"t_link_up\": {l.TUp:0.##}, \"t_link_along\": {l.TAlong:0.##}, \"r\": [{l.ROpen:0.##},{l.RAlong:0.##},{l.RUp:0.##}]}}"));
                }
                b.Append("],\n   \"pairs\": [");
                first = true;
                foreach (var p in Pairs)
                {
                    if (p.Zone != z.Id) continue;
                    if (!first) b.Append(","); first = false;
                    b.Append("\n    {\"a\": " + P(p.A) + ", \"b\": " + P(p.B) + Inv.F($", \"area\": {p.Area:0.##}, \"k\": [{p.KOpen:0.##},{p.KAlong:0.##},{p.KUp:0.##}], \"r\": [{p.ROpen:0.##},{p.RAlong:0.##},{p.RUp:0.##}], \"alpha\": {Alpha(p.KAlong, p.Area):0.######}}}"));
                }
                b.Append("]}" + (q + 1 < Joints.Count ? "," : "") + "\n");
            }
            b.Append(" ]\n}\n");
            return b.ToString();
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ====================================================================== the test series
    public enum WeaveTestKind { Shear = 0, Peel = 1, Bending = 2 }

    /// <summary>One specimen of the test series.</summary>
    public sealed class WeaveCoupon
    {
        public string Id, Variant; public WeaveSettings Settings; public double Gap; public WeaveTestKind Test; public int Replicate, Order;
        public double Length;
        /// <summary>The specimen on the bed (top view): start and end of its centre line.</summary>
        public Point3d From, To;
        public WeaveRegion Region;
        /// <summary>Predicted on the path.</summary>
        public WeaveJoint Joint;
        /// <summary>The specimen alone, as its own path.</summary>
        public Toolpath Path;
    }

    public sealed class WeaveTestOptions
    {
        public double Length = 80, Waste = 25, Height = 60, BedX = 1000, BedY = 700, Margin = 40;
        public int Replicates = 5, Seed = 1;
        public bool Amplitudes = true, Gaps = true;
        public bool Shear = true, Peel = true, Bending = true;
        /// <summary>Own variants (name, settings or null for plain, gap). Null = the standard series.</summary>
        public List<KeyValuePair<string, KeyValuePair<WeaveSettings, double>>> Variants;
    }

    public sealed class WeaveTestSeries
    {
        public List<WeaveCoupon> Coupons = new List<WeaveCoupon>();
        /// <summary>The beds: every bed is one line (a double wall in rows); the coupons are sawn out of it.</summary>
        public List<Toolpath> Beds = new List<Toolpath>();
        public List<WeaveZoneSet> Sets = new List<WeaveZoneSet>();
        public List<int> BedOf = new List<int>();
        public List<string> Report = new List<string>();
        public string Verdict = "";

        public List<string> Csv(PrintSettings st)
        {
            var r = new List<string> { "order,coupon,bed,variant,test,replicate,pattern,amplitude_mm,wavelength_mm,tooth_mm,phase,layer_period,faces,gap_mm,bead_width_mm,layer_height_mm,length_mm,layers,joint_face_mm2,side_contact_mm2,interlock_contact_mm2,interlock_depth_mm,x_from,y_from,x_to,y_to,material,batch,printed_at,nozzle_C,bed_C,layer_time_s,measured_thickness_mm,measured_length_mm,measured_height_mm,mass_g,test_speed_mm_min,F_max_N,displacement_at_Fmax_mm,stiffness_N_mm,energy_J,failure_place(weld/layer/bead/clamp),failure_picture,notes" };
            for (int i = 0; i < Coupons.Count; i++)
            {
                var c = Coupons[i]; var s = c.Settings; double w = st.BeadWidth, L = 0, b = 0, a = 0;
                if (s != null) { a = Weave.AmplitudeOf(s, w); Weave.Lengths(s, a * s.Peak, w, out L, out b); }
                bool wave = s != null && (s.Pattern == WeavePattern.Snake || s.Pattern == WeavePattern.Teeth);
                var j = c.Joint ?? new WeaveJoint();
                r.Add(Inv.F($"{c.Order},{c.Id},{BedOf[i] + 1},{c.Variant},{c.Test},{c.Replicate},{(s == null ? "Plain" : s.Pattern.ToString())},{a:0.##},{(wave ? L : 0):0.##},{(s != null && s.Pattern == WeavePattern.Teeth ? b : 0):0.##},{(s == null ? 0 : s.PhaseUsed):0.##},{(s == null ? 1 : s.LayerPeriod)},{(s == null ? "" : s.Faces.ToString())},{c.Gap:0.##},{w:0.##},{st.LayerHeight:0.##},{c.Length:0.#},{j.Layers},{j.Face:0.#},{j.Vertical:0.#},{j.Horizontal:0.#},{j.Depth:0.###},{c.From.X:0.#},{c.From.Y:0.#},{c.To.X:0.#},{c.To.Y:0.#},,,,,,,,,,,,,,,,,,"));
            }
            return r;
        }
    }

    /// <summary>
    /// Specimens for tests of the joint: straight pieces of double wall, some plain, some woven. All of
    /// them are printed as one line (two beads side by side, in rows), so they have the same history;
    /// the order is random. The pieces are sawn out of the rows.
    /// </summary>
    public static class WeaveCoupons
    {
        public static List<KeyValuePair<string, KeyValuePair<WeaveSettings, double>>> Standard(PrintSettings st, bool amplitudes, bool gaps)
        {
            double w = st.BeadWidth, s = st.BeadSpacing;
            var r = new List<KeyValuePair<string, KeyValuePair<WeaveSettings, double>>>();
            Action<string, WeaveSettings, double> add = (n, v, g) => r.Add(new KeyValuePair<string, KeyValuePair<WeaveSettings, double>>(n, new KeyValuePair<WeaveSettings, double>(v, g)));
            add("plain", null, s);
            add("shift", new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = 0.25 * w }, s);
            add("width", new WeaveSettings { Pattern = WeavePattern.Width, Amplitude = 0.25 * w }, s);
            add("snake woven", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Move, Amplitude = 0.25 * w, Phase = 0.5 }, s);
            add("snake helix", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Move, Amplitude = 0.25 * w, Phase = 0.25 }, s);
            add("snake stacked", new WeaveSettings { Pattern = WeavePattern.Snake, Faces = WeaveFaces.Move, Amplitude = 0.25 * w, Phase = 0 }, s);
            add("teeth 50", new WeaveSettings { Pattern = WeavePattern.Teeth, Faces = WeaveFaces.Move, Amplitude = 0.25 * w, Wavelength = 50, Centred = true }, s);
            if (amplitudes)
                foreach (double f in new[] { 0.1, 0.175 })
                    add(Inv.F($"shift {f:0.###}w"), new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = f * w }, s);
            if (gaps)
                foreach (double f in new[] { 1.0, 1.1 })
                {
                    add(Inv.F($"plain gap {f:0.##}w"), null, f * w);
                    add(Inv.F($"shift gap {f:0.##}w"), new WeaveSettings { Pattern = WeavePattern.Shift, Faces = WeaveFaces.Move, Amplitude = 0.25 * w, Fit = false }, f * w);
                }
            return r;
        }

        public static WeaveTestSeries Make(PrintSettings st, WeaveTestOptions o)
        {
            var res = new WeaveTestSeries(); double w = st.BeadWidth;
            var variants = o.Variants ?? Standard(st, o.Amplitudes, o.Gaps);
            var tests = new List<WeaveTestKind>();
            if (o.Shear) tests.Add(WeaveTestKind.Shear); if (o.Peel) tests.Add(WeaveTestKind.Peel); if (o.Bending) tests.Add(WeaveTestKind.Bending);
            int vi = 0;
            foreach (var v in variants)
            {
                vi++;
                foreach (var t in tests)
                    for (int n = 1; n <= Math.Max(1, o.Replicates); n++)
                        res.Coupons.Add(new WeaveCoupon { Id = Inv.F($"V{vi:00}{t.ToString()[0]}{n}"), Variant = v.Key, Settings = v.Value.Key, Gap = v.Value.Value, Test = t, Replicate = n, Length = o.Length });
            }
            // blocks: every replicate number is a full set in random order, so a drift during the print hits all variants alike
            var rnd = new Lcg(o.Seed);
            res.Coupons = res.Coupons.OrderBy(c => c.Replicate).ThenBy(c => rnd.Next()).ToList();
            for (int i = 0; i < res.Coupons.Count; i++) res.Coupons[i].Order = i + 1;

            // rows on the beds
            double pitch = Math.Max(20, 6 * w), turn = 0.5 * pitch, rowLen = o.BedX - 2 * o.Margin - 2 * turn - 2 * w, cell = o.Length + o.Waste;
            int perRow = Math.Max(1, (int)((rowLen - o.Waste) / cell)), rows = Math.Max(1, (int)((o.BedY - 2 * o.Margin) / pitch)), perBed = perRow * rows;
            int beds = (res.Coupons.Count + perBed - 1) / perBed;
            int layers = Math.Max(2, (int)Math.Round((o.Height - st.FirstLayer) / st.LayerHeight) + 1);
            for (int b = 0; b < beds; b++)
            {
                var mine = res.Coupons.Skip(b * perBed).Take(perBed).ToList();
                var centre = new List<Point2d>(); var gaps = new List<double>();
                int used = (mine.Count + perRow - 1) / perRow; int at = 0;
                double x0 = o.Margin + turn + w, len = perRow * cell + o.Waste;
                for (int r = 0; r < used; r++)
                {
                    double y = o.Margin + r * pitch; bool right = r % 2 == 0;
                    // the specimens of this row: gap changes softly in the waste between them
                    var ss = new List<double[]>(); // s from, s to, gap
                    for (int q = 0; q < perRow && at < mine.Count; q++, at++)
                    {
                        var c = mine[at]; double s0 = o.Waste + q * cell, s1 = s0 + o.Length;
                        ss.Add(new[] { s0, s1, c.Gap });
                        double xa = right ? x0 + s0 : x0 + len - s0, xb = right ? x0 + s1 : x0 + len - s1;
                        c.From = new Point3d(xa, y, 0); c.To = new Point3d(xb, y, 0);
                        c.Region = WeaveRegion.Rectangle(Math.Min(xa, xb) - 0.35 * o.Waste, y - 0.4 * pitch, Math.Max(xa, xb) + 0.35 * o.Waste, y + 0.4 * pitch);
                        res.BedOf.Add(b);
                    }
                    double step = 2;
                    int m = (int)Math.Ceiling(len / step);
                    for (int i = 0; i <= m; i++)
                    {
                        double s = len * i / m, g = st.BeadSpacing;
                        for (int q = 0; q < ss.Count; q++)
                        {
                            double d = s < ss[q][0] ? ss[q][0] - s : s > ss[q][1] ? s - ss[q][1] : 0;
                            double flat = 0.4 * o.Waste;
                            if (d <= flat) g = ss[q][2];
                            else if (d < 0.5 * o.Waste)
                            {
                                double other = s < ss[q][0] ? (q > 0 ? ss[q - 1][2] : ss[q][2]) : q + 1 < ss.Count ? ss[q + 1][2] : ss[q][2];
                                double t = (d - flat) / (0.2 * o.Waste); g = ss[q][2] + (other - ss[q][2]) * t * t * (3 - 2 * t);
                            }
                            else if (d < 0.6 * o.Waste && (s < ss[q][0] ? q > 0 : q + 1 < ss.Count)) { double other = s < ss[q][0] ? ss[q - 1][2] : ss[q + 1][2]; double t = (d - flat) / (0.2 * o.Waste); g = ss[q][2] + (other - ss[q][2]) * t * t * (3 - 2 * t); }
                        }
                        centre.Add(new Point2d(right ? x0 + s : x0 + len - s, y)); gaps.Add(g);
                    }
                    if (r + 1 < used)
                    {
                        double cx = right ? x0 + len : x0, sg = right ? 1 : -1; int seg = 16;
                        for (int i = 1; i < seg; i++) { double f = -0.5 * Math.PI + Math.PI * i / seg; centre.Add(new Point2d(cx + sg * turn * Math.Cos(f), y + turn + turn * Math.Sin(f))); gaps.Add(st.BeadSpacing); }
                    }
                }
                var plain = DoubleWall(centre, gaps, layers, st);
                var set = WeaveFinder.Find(plain, new WeaveFindOptions { Further = 0.35, MinLength = 2 * w });
                var rules = mine.Where(c => c.Settings != null).Select(c => new WeaveRule { Settings = c.Settings, Regions = new List<WeaveRegion> { c.Region } }).ToList();
                var woven = rules.Count > 0 ? Weave.Apply(plain, set, rules) : new WeaveResult { Path = plain };
                res.Beds.Add(woven.Path); res.Sets.Add(set);
                // what every specimen has in it
                var seams = WeaveCheck.Seams(woven.Path, set);
                foreach (var c in mine)
                {
                    double xa = Math.Min(c.From.X, c.To.X), xb = Math.Max(c.From.X, c.To.X), y = c.From.Y;
                    c.Joint = WeaveCheck.Sum(WeaveCheck.From(seams, set, new List<WeaveRegion> { WeaveRegion.Rectangle(xa, y - 0.4 * pitch, xb, y + 0.4 * pitch) }, st));
                }
            }
            foreach (var g in res.Coupons.GroupBy(c => c.Variant + "|" + c.Test).Select(g => g.First()))
            {
                // one path per variant, with the seam in the middle of the specimen
                var alone = res.Coupons.FirstOrDefault(c => c.Variant == g.Variant && c.Path != null);
                var path = alone != null ? alone.Path : Single(st, g, o.Waste, layers);
                foreach (var c in res.Coupons) if (c.Variant == g.Variant) c.Path = path;
            }
            double min = 0; foreach (var bed in res.Beds) min += bed.Length() / Math.Max(1, st.Speed);
            res.Verdict = Inv.F($"{res.Coupons.Count} specimens: {variants.Count} variants x {tests.Count} test(s) x {Math.Max(1, o.Replicates)}, on {beds} bed(s), {layers} layers, about {min / 60:0.0} h of printing.");
            res.Report.Add(res.Verdict);
            res.Report.Add(Inv.F($"Every bed is one line: two beads side by side in rows of {perRow}, {o.Length:0} mm specimens with {o.Waste:0} mm waste in between. Saw at the marks."));
            foreach (var t in Tests(o, st)) res.Report.Add(t);
            return res;
        }

        /// <summary>What to do with the specimens.</summary>
        public static List<string> Tests(WeaveTestOptions o, PrintSettings st)
        {
            var r = new List<string>();
            if (o.Shear) r.Add(Inv.F($"Shear along the joint: saw two notches from opposite sides down to the seam, {0.4 * o.Length:0} mm apart, and pull or press along the beads. Shear area = notch distance x height. Compare F_max / area and the slope."));
            if (o.Peel) r.Add(Inv.F($"Tension across the joint (peel): glue or clamp both faces ({o.Length:0} x {o.Height:0} mm) to blocks and pull apart; or drive a wedge into the seam at one end. Compare F_max / face."));
            if (o.Bending) r.Add("Bending: four-point bending of the double wall lying flat, beads along the span. If the joint slips, the two beads bend on their own (a quarter of the stiffness): compare the slope and F_max.");
            r.Add("Keep the order of the list while printing and testing (it is random within each set). At least five of each. Write down the failure: weld, between layers, through the bead, at the clamp.");
            return r;
        }

        private static Toolpath Single(PrintSettings st, WeaveCoupon c, double waste, int layers)
        {
            var centre = new List<Point2d>(); var gaps = new List<double>(); double len = c.Length + 2 * waste;
            int m = (int)Math.Ceiling(len / 2);
            for (int i = 0; i <= m; i++) { centre.Add(new Point2d(len * i / m, 0)); gaps.Add(c.Gap); }
            var p = DoubleWall(centre, gaps, layers, st);
            if (c.Settings == null) return p;
            var set = WeaveFinder.Find(p, new WeaveFindOptions { Further = 0.35, MinLength = 2 * st.BeadWidth });
            return Weave.Apply(p, set, new[] { new WeaveRule { Settings = c.Settings, Regions = new List<WeaveRegion> { WeaveRegion.Rectangle(0.65 * waste, -5 * st.BeadWidth, len - 0.65 * waste, 5 * st.BeadWidth) } } }).Path;
        }

        /// <summary>
        /// A double wall along a centre line: out on one side, round the end, back on the other; flat
        /// layers, the step up where the two ends meet. gaps = distance of the two beads per point.
        /// </summary>
        public static Toolpath DoubleWall(List<Point2d> centre, List<double> gaps, int layers, PrintSettings st)
        {
            int n = centre.Count;
            var a = new Point2d[n]; var b = new Point2d[n];
            for (int i = 0; i < n; i++)
            {
                Point2d p = centre[Math.Max(0, i - 1)], q = centre[Math.Min(n - 1, i + 1)];
                double dx = q.X - p.X, dy = q.Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy); dx /= d; dy /= d;
                double g = 0.5 * gaps[i];
                a[i] = new Point2d(centre[i].X + dy * g, centre[i].Y - dx * g); b[i] = new Point2d(centre[i].X - dy * g, centre[i].Y + dx * g);
            }
            // only the points that carry the shape
            var line = new List<Point2d>();
            for (int i = 0; i < n; i++) line.Add(a[i]);
            // round the end
            double ex = centre[n - 1].X - centre[n - 2].X, ey = centre[n - 1].Y - centre[n - 2].Y, el = Math.Sqrt(ex * ex + ey * ey); ex /= el; ey /= el;
            double r = 0.5 * gaps[n - 1];
            for (int i = 1; i < 10; i++)
            {
                double f = Math.PI * i / 10; // from a (right side) round the end to b
                line.Add(new Point2d(centre[n - 1].X + r * (ey * Math.Cos(f) + ex * Math.Sin(f)), centre[n - 1].Y + r * (-ex * Math.Cos(f) + ey * Math.Sin(f))));
            }
            for (int i = n - 1; i >= 0; i--) line.Add(b[i]);
            line = Thin(line);
            var tp = new Toolpath { Settings = st }; double w = st.BeadWidth;
            for (int k = 0; k < layers; k++)
            {
                double z = st.FirstLayer + k * st.LayerHeight, h = k == 0 ? st.FirstLayer : st.LayerHeight;
                for (int i = 0; i < line.Count; i++)
                {
                    var p = new Point3d(line[i].X, line[i].Y, z);
                    if (k == 0 && i == 0) tp.Add(new Point3d(p.X, p.Y, p.Z + 30), Vector3d.ZAxis, h, w, 0, PathRole.Travel, 0);
                    tp.Add(p, Vector3d.ZAxis, h, w, k == 0 && i == 0 ? 0 : 1, k == 0 && i == 0 ? PathRole.Travel : i == 0 ? PathRole.Seam : PathRole.Wall, k);
                }
            }
            return tp;
        }

        private static List<Point2d> Thin(List<Point2d> l)
        {
            var r = new List<Point2d> { l[0] };
            for (int i = 1; i + 1 < l.Count; i++)
            {
                Point2d a = r[r.Count - 1], p = l[i], b = l[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9 || Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / d > 0.002) r.Add(p);
            }
            r.Add(l[l.Count - 1]);
            return r;
        }

        /// <summary>The same random order on every computer and framework.</summary>
        private sealed class Lcg
        {
            private ulong _s;
            public Lcg(int seed) { _s = (ulong)(uint)seed * 2654435761UL + 12345; Next(); }
            public int Next() { _s = _s * 6364136223846793005UL + 1442695040888963407UL; return (int)(_s >> 33); }
        }
    }
}
