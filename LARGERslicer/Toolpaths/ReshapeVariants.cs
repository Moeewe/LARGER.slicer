using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>The ways to put more material where it is needed. They can be printed and tested against each other.</summary>
    public enum ReshapeVariant
    {
        /// <summary>The laps fan out (thicker at the high side), bead width as it was, tool square to the layers.</summary>
        FannedSquare = 0,
        /// <summary>The same with a vertical nozzle.</summary>
        FannedVertical = 1,
        /// <summary>The laps fan out half as much (root of the factor) and the beads become wider for the rest; tool square.</summary>
        FannedWiderSquare = 2,
        FannedWiderVertical = 3,
        /// <summary>Flat layers as they were, only the beads become wider or narrower.</summary>
        WiderOnly = 4,
        /// <summary>The laps fan out further; where they get too thick they are printed in 3 or 5 passes. Tool square.</summary>
        FannedPassesSquare = 5,
        FannedPassesVertical = 6,
        /// <summary>Flat layers of constant height; the laps end where the part is lower and turn back (partial laps).</summary>
        CutLaps = 7,
    }

    /// <summary>All variants with the same inputs and the same log.</summary>
    public static class ReshapeVariants
    {
        public static readonly string[] Names =
        {
            "Fanned laps, tool square (robot)",
            "Fanned laps, nozzle vertical",
            "Fanned and wider beads, tool square",
            "Fanned and wider beads, nozzle vertical",
            "Wider beads only, flat layers",
            "Fanned with extra passes, tool square",
            "Fanned with extra passes, nozzle vertical",
            "Flat layers, laps cut and turned back",
        };

        public static string Short(ReshapeVariant v) => (int)v >= 0 && (int)v < Names.Length ? (int)v + " " + Names[(int)v] : v.ToString();
        public static bool Tilts(ReshapeVariant v) => v == ReshapeVariant.FannedSquare || v == ReshapeVariant.FannedWiderSquare || v == ReshapeVariant.FannedPassesSquare;
        /// <summary>The same way with a vertical nozzle.</summary>
        public static ReshapeVariant Upright(ReshapeVariant v) => Tilts(v) ? v + 1 : v;

        private static ReshapeOptions With(ReshapeOptions o, bool tilt, bool passes) => new ReshapeOptions
        {
            Tilt = tilt, ExtraPasses = passes, MinLayer = o.MinLayer, MaxLayer = o.MaxLayer, MaxSlope = o.MaxSlope, Smooth = o.Smooth, FlatLayers = o.FlatLayers, Roof = o.Roof, Material = o.Material,
            MaxFlow = o.MaxFlow, Head = o.Head, Limit = o.Limit, BreakBefore = o.BreakBefore, Check = o.Check, MinWidth = o.MinWidth, MaxWidth = o.MaxWidth,
        };

        /// <summary>Reshapes a path in one of the ways.</summary>
        public static PathReshape.Result Run(Toolpath tp, ShapeField field, ReshapeVariant v, ReshapeOptions o)
        {
            o = o ?? new ReshapeOptions(); field = field ?? ShapeField.Constant(1);
            PathReshape.Result r;
            switch (v)
            {
                case ReshapeVariant.FannedWiderSquare: case ReshapeVariant.FannedWiderVertical: r = Wider(tp, field, With(o, v == ReshapeVariant.FannedWiderSquare, false), 0.5); break;
                case ReshapeVariant.WiderOnly: r = Wider(tp, field, With(o, false, false), 0); break;
                case ReshapeVariant.CutLaps: r = Cut(tp, field, With(o, false, false)); break;
                default: r = PathReshape.Apply(tp, field, With(o, Tilts(v), v == ReshapeVariant.FannedPassesSquare || v == ReshapeVariant.FannedPassesVertical)); break;
            }
            r.Variant = v;
            r.Report.Insert(Math.Min(1, r.Report.Count), "Variant: " + Short(v) + ".");
            return r;
        }

        // ------------------------------------------------------------------ wider beads

        /// <summary>share = how much of the factor goes into the height (as a power): 0 = all into the width, 0.5 = half and half; what the height cannot take goes into the width.</summary>
        private static PathReshape.Result Wider(Toolpath tp, ShapeField field, ReshapeOptions o, double share)
        {
            double pct = o.Material; o.Material = 0;
            bool check = o.Check; o.Check = false;
            if (tp == null || tp.Count < 3) return PathReshape.Verdict(new PathReshape.Result { Refused = "Empty toolpath." }, o);
            var caps = Room(tp);
            Func<double, PathReshape.Result> one = c =>
            {
                double H = 0; { double z0 = double.MaxValue, z1 = double.MinValue; for (int i = 1; i < tp.Count; i++) if (PathReshape.Part(tp, i)) { z0 = Math.Min(z0, tp.Points[i].Z); z1 = Math.Max(z1, tp.Points[i].Z); } H = z1 - z0 + tp.Settings.FirstLayer; }
                var hf = ShapeField.FromFunction((x, y) => share <= 0 ? 1 : Math.Pow(Math.Max(1e-6, c * field.Factor(x, y, H)), share), false, field.Source, field.X0, field.Y0, field.X1, field.Y1);
                var r = PathReshape.Apply(tp, hf, o);
                if (r.Refused != null) return r;
                if (share <= 0) r.Report.Clear();
                Widen(r, tp, (x, y) => c * field.Factor(x, y, H), o, caps);
                return r;
            };
            PathReshape.Result best = one(1);
            if (best.Refused != null) return best;
            double scale = 1;
            if (pct > 0)
            {
                double Share(double c, out PathReshape.Result res) { Cancel.Check(); res = one(c); return res.Refused != null ? double.NaN : PathReshape.PartE(res.Path) / PathReshape.PartE(tp); }
                double lo = 0.15, hi = 6, want = pct / 100;
                for (int k = 0; k < 14; k++)
                {
                    double c = k == 0 ? 1 : Math.Sqrt(lo * hi), val = Share(c, out var res);
                    if (double.IsNaN(val)) break;
                    best = res; scale = c;
                    if (Math.Abs(val - want) < 0.001) break;
                    if (val > want) hi = c; else lo = c;
                }
            }
            best.Scale = scale; o.Material = pct; o.Check = check;
            var rep = best.Report.Where(l => !l.StartsWith("OK:") && !l.StartsWith("CHECK:") && !l.StartsWith("Layers:") && !l.StartsWith("Extrusion:") && !l.StartsWith("Tool:") && !l.StartsWith("Warning:") && !l.StartsWith("Check:") && !l.StartsWith("Material:")).ToList();
            best.Report = rep;
            PathReshape.Numbers(best, tp, o);
            if (share <= 0) { best.Identity = best.WidthMax - best.WidthMin < 1e-6; best.HeightAfterMin = best.HeightAfterMax = best.HeightBefore; }
            if (pct > 0 && Math.Abs(100 * best.MaterialShare - pct) > 0.5) best.Warnings.Add(Inv.F($"{pct:0} % of the material could not be reached within the limits of the bead width and height: it is {100 * best.MaterialShare:0.#} %."));
            return PathReshape.Verdict(best, o);
        }

        /// <summary>Per point: how wide the bead may become before it presses into the bead of the same layer beside it.</summary>
        private static double[] Room(Toolpath tp)
        {
            int n = tp.Count; var r = new double[n]; var st = tp.Settings; double ov = 1 - Math.Max(0, Math.Min(0.5, st.Overlap));
            double wm = 0; for (int i = 1; i < n; i++) if (PathReshape.Part(tp, i)) wm = Math.Max(wm, tp.Widths[i]);
            double reach = 2.2 * Math.Max(wm, st.BeadWidth), back = 4 * Math.Max(wm, st.BeadWidth);
            var s = new double[n]; for (int i = 1; i < n; i++) { s[i] = s[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]); r[i] = double.MaxValue; }
            int a = 0;
            while (a < n)
            {
                int b = a; while (b + 1 < n && tp.Layers[b + 1] == tp.Layers[a]) b++;
                var g = new Dictionary<long, List<double[]>>();
                for (int i = Math.Max(1, a); i <= b; i++)
                {
                    if (!PathReshape.Part(tp, i)) continue;
                    double l = s[i] - s[i - 1]; int k = Math.Max(1, (int)Math.Ceiling(l / (0.5 * st.BeadWidth)));
                    for (int j = 1; j <= k; j++)
                    {
                        Point3d q = tp.Points[i - 1] + (tp.Points[i] - tp.Points[i - 1]) * (j / (double)k);
                        long key = ((long)(int)Math.Floor(q.X / reach) << 32) ^ (uint)(int)Math.Floor(q.Y / reach);
                        if (!g.TryGetValue(key, out var c)) g[key] = c = new List<double[]>();
                        c.Add(new[] { q.X, q.Y, s[i - 1] + l * j / k, i, l > 1e-9 ? (tp.Points[i].X - tp.Points[i - 1].X) / l : 1, l > 1e-9 ? (tp.Points[i].Y - tp.Points[i - 1].Y) / l : 0 });
                    }
                }
                foreach (var kv in g)
                    foreach (var u in kv.Value)
                    {
                        int cx = (int)Math.Floor(u[0] / reach), cy = (int)Math.Floor(u[1] / reach); double best = reach * reach;
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                            {
                                if (!g.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cy + dy), out var c)) continue;
                                foreach (var v in c) { if (Math.Abs(v[2] - u[2]) < back) continue; double side = Math.Abs((v[0] - u[0]) * u[5] - (v[1] - u[1]) * u[4]); if (side < 0.5 * st.BeadWidth) continue; /* the end of a lap over its own start is no neighbour */ double d = (v[0] - u[0]) * (v[0] - u[0]) + (v[1] - u[1]) * (v[1] - u[1]); if (d < best) best = d; }
                            }
                        if (best < reach * reach) { int i = (int)u[3]; double w = Math.Sqrt(best) / ov; if (w < r[i]) r[i] = w; if (w < r[i - 1]) r[i - 1] = w; }
                    }
                a = b + 1;
            }
            return r;
        }

        /// <summary>Sets the bead widths of a (reshaped) path so that width x height follows the wanted factor, within the limits.</summary>
        private static void Widen(PathReshape.Result r, Toolpath tp, Func<double, double, double> factor, ReshapeOptions o, double[] caps)
        {
            var st = tp.Settings; var p = r.Path; var m = r.Map;
            double lo = o.MinWidth > 0 ? o.MinWidth : st.Nozzle > 0 ? Math.Min(1, st.Nozzle / st.BeadWidth) : 0.7;
            double hi = o.MaxWidth > 0 ? o.MaxWidth : st.Nozzle > 0 ? Math.Max(1, (st.NozzleShape == Nozzles.Shape.Pointed ? 1.3 : 1.8) * st.Nozzle / st.BeadWidth) : 1.5;
            double smooth = o.Smooth > 0 ? o.Smooth : 6 * st.BeadWidth;
            bool raised = m != null && m.S != null;
            var sp = PathReshape.Smoothed(tp, (x, y) => factor(x, y) / (raised ? Math.Max(0.05, m.Factor(x, y)) : 1), lo, hi, smooth);
            r.Wider = (x, y) => sp.At(x, y);
            // moves are split where the width would not change evenly along them
            var q = new Toolpath { Settings = p.Settings, Warp = p.Warp }; var src = new List<int>(); var wgt = new List<double>(); var ins = new List<bool>();
            double zFlat = m != null ? m.ZFlat : double.MinValue;
            for (int i = 0; i < p.Count; i++)
            {
                int parts = 1;
                if (i > 0 && PathReshape.Part(p, i) && p.Points[i].Z > zFlat)
                {
                    Point3d a = p.Points[i - 1], b = p.Points[i]; double len = a.DistanceTo(b);
                    if (len > 10)
                    {
                        double f0 = sp.At(a.X, a.Y), f1 = sp.At(b.X, b.Y), worst = 0; int k = Math.Max(3, Math.Min(60, (int)(len / 10)));
                        for (int j = 1; j < k; j++) { double u = j / (double)k; worst = Math.Max(worst, Math.Abs(sp.At(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u) - (f0 + (f1 - f0) * u))); }
                        if (worst > 0.01) parts = (int)Math.Ceiling(len / 20);
                    }
                }
                for (int j = 1; j < parts; j++)
                {
                    double u = j / (double)parts; Vector3d n = p.Normals[i - 1] * (1 - u) + p.Normals[i] * u; double l = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z); if (l > 1e-9) n = new Vector3d(n.X / l, n.Y / l, n.Z / l);
                    q.Add(p.Points[i - 1] + (p.Points[i] - p.Points[i - 1]) * u, n, p.Heights[i - 1] + (p.Heights[i] - p.Heights[i - 1]) * u, p.Widths[i - 1] + (p.Widths[i] - p.Widths[i - 1]) * u, p.Flows[i], p.Roles[i], p.Layers[i]);
                    src.Add(r.Source[i]); wgt.Add(r.Weight[i] / parts); ins.Add(true);
                }
                q.AddFrom(p, i); src.Add(r.Source[i]); wgt.Add(r.Weight[i] / parts); ins.Add(r.Inserted[i]);
                r.Added += parts - 1;
            }
            int cnt = q.Count; var w = new double[cnt]; int capped = 0;
            for (int i = 0; i < cnt; i++)
            {
                w[i] = q.Widths[i];
                bool part = PathReshape.Part(q, i) || i + 1 < cnt && PathReshape.Part(q, i + 1);
                if (!part || q.Points[i].Z <= zFlat + 1e-9) continue;
                double want = q.Widths[i] * sp.At(q.Points[i].X, q.Points[i].Y), cap = Math.Max(q.Widths[i], Math.Min(caps[src[i]], i + 1 < cnt ? caps[src[i + 1]] : double.MaxValue));
                // a bead stays at least twice as wide as high (or as flat as it was before)
                int so = src[i]; want = Math.Max(want, Math.Min(2, tp.Heights[so] > 0.01 ? tp.Widths[so] / tp.Heights[so] : 2) * q.Heights[i]);
                if (want > cap) { want = cap; capped++; }
                w[i] = want;
            }
            // the width changes by at most 1 mm in 10 mm of path: the extruder follows slowly
            for (int i = 1; i < cnt; i++) if (PathReshape.Part(q, i)) w[i] = Math.Min(w[i], w[i - 1] + 0.1 * q.Points[i].DistanceTo(q.Points[i - 1]));
            for (int i = cnt - 1; i > 0; i--) if (PathReshape.Part(q, i)) w[i - 1] = Math.Min(w[i - 1], w[i] + 0.1 * q.Points[i].DistanceTo(q.Points[i - 1]));
            for (int i = 0; i < cnt; i++)
            {
                if (PathReshape.Part(q, i))
                {
                    double w0 = 0.5 * (q.Widths[i] + q.Widths[i - 1]), w1 = 0.5 * (w[i] + w[i - 1]), h = 0.5 * (q.Heights[i] + q.Heights[i - 1]);
                    double s0 = PathReshape.Section(st, w0, h); if (s0 > 1e-12) wgt[i] *= PathReshape.Section(st, w1, h) / s0;
                }
            }
            for (int i = 0; i < cnt; i++) q.Widths[i] = w[i];
            r.Path = q; r.Source = src; r.Weight = wgt; r.Inserted = ins;
            r.Report.Add(Inv.F($"Bead width: {lo:0.##} to {hi:0.##} times as wide as before is allowed.") + (capped > 0 ? Inv.F($" At {capped} point(s) another bead of the same layer lies too close for a wider one (they would press into each other); there the width stays.") : ""));
        }

        // ------------------------------------------------------------------ flat layers, cut laps

        private class Piece { public int From, To, Parent = -1; public List<int> Kids = new List<int>(); public double Len; } // points From .. To of the split path, To included

        /// <summary>
        /// The layers stay flat and as high as they are. Every lap is printed only where the new top lies above it: the
        /// line ends there, steps up one layer and runs back (one lap forth, the next one back). Where the part has
        /// several high places, one is finished before the next; between them the nozzle travels without extrusion.
        /// </summary>
        private static PathReshape.Result Cut(Toolpath tp, ShapeField field, ReshapeOptions o)
        {
            var r = new PathReshape.Result();
            if (tp == null || tp.Count < 3) { r.Refused = "Empty toolpath."; return PathReshape.Verdict(r, o); }
            var st = tp.Settings; double pct = o.Material;
            var fo = With(o, false, false); fo.Material = 0; fo.MinLayer = 0.001 * st.LayerHeight; fo.MaxLayer = st.LayerHeight; if (fo.MaxSlope <= 0) fo.MaxSlope = 35;
            Func<double, ShapeMap> map = c => { var rr = new PathReshape.Result(); var f = ShapeField.FromFunction((x, y) => c * field.Value(x, y), field.IsHeight, field.Source, field.X0, field.Y0, field.X1, field.Y1); var mm = PathReshape.Fit(tp, f, fo, rr); r.Refused = rr.Refused; r.WantedMin = rr.WantedMin; r.WantedMax = rr.WantedMax; r.HeightBefore = rr.HeightBefore; if (rr.Report.Any(l => l.Contains("roof"))) r.Refused = r.Refused ?? "The wall closes to a roof: laps cannot be cut there (the roof would stay open)."; return mm; };
            Func<ShapeMap, int, double> limit = (m, i) => m.ZFlat + (m.ZTop - m.ZFlat) * Math.Min(1, m.Factor(tp.Points[i].X, tp.Points[i].Y));
            double first = 0; for (int i = 1; i < tp.Count; i++) if (PathReshape.Part(tp, i)) { first = tp.Points[i].Z; break; }
            // what of a move stays: the nozzle is at most half a layer above the new top
            bool Keep(ShapeMap m, Point3d p) => p.Z <= m.ZFlat + 1e-9 || m.S == null || p.Z - 0.5 * st.LayerHeight <= m.ZFlat + (m.ZTop - m.ZFlat) * Math.Min(1, m.Factor(p.X, p.Y)) + 1e-6;
            var map1 = map(1);
            if (map1 == null || r.Refused != null) return PathReshape.Verdict(r, o);
            double scale = 1;
            if (pct > 99.9)
            {
                // cutting only takes away: the highest place keeps the height of the part, the rest is cut to the shape
                scale = 1 / Math.Max(1e-6, r.WantedMax); map1 = map(scale); if (map1 == null) return PathReshape.Verdict(r, o);
            }
            else if (pct > 0)
            {
                double Share(ShapeMap m) { double all = 0, kept = 0; for (int i = 1; i < tp.Count; i++) if (PathReshape.Part(tp, i)) { double l = tp.Points[i].DistanceTo(tp.Points[i - 1]) * tp.Heights[i] * tp.Widths[i] * tp.Flows[i]; int k = Math.Max(1, (int)(tp.Points[i].DistanceTo(tp.Points[i - 1]) / 20)); for (int j = 1; j <= k; j++) { all += l / k; if (Keep(m, tp.Points[i - 1] + (tp.Points[i] - tp.Points[i - 1]) * (j / (double)k))) kept += l / k; } } return all > 0 ? kept / all : 1; }
                double lo = 0.2, hi = 6;
                for (int k = 0; k < 18; k++) { double c = k == 0 ? 1 : Math.Sqrt(lo * hi); Cancel.Check(); var m = map(c); if (m == null) break; map1 = m; scale = c; double v = Share(m); if (Math.Abs(v - pct / 100) < 0.0015) break; if (v > pct / 100) hi = c; else lo = c; }
            }
            r.Scale = scale; r.Map = map1;
            if (r.WantedMax * scale > 1.02) r.Warnings.Add(Inv.F($"The field asks for up to {100 * r.WantedMax * scale:0} % of the height; laps can be cut, not added. Slice the part higher first."));
            var M = map1;
            // ---- the path with points exactly where the laps are cut
            var p2 = new Toolpath { Settings = st, Warp = tp.Warp }; var src = new List<int>(); var keep = new List<bool>(); var ins = new List<bool>();
            int firstP = -1, lastP = -1; for (int i = 1; i < tp.Count; i++) if (LapBelow.Prints(tp, i)) { if (firstP < 0) firstP = i; lastP = i; }
            for (int i = 0; i <= lastP; i++)
            {
                if (i > 0 && PathReshape.Part(tp, i))
                {
                    Point3d a = tp.Points[i - 1], b = tp.Points[i]; double len = a.DistanceTo(b); int k = Math.Max(1, (int)Math.Ceiling(len / 10)); bool ka = Keep(M, a); double ua = 0;
                    for (int j = 1; j <= k; j++)
                    {
                        double ub = j / (double)k; bool kb = Keep(M, a + (b - a) * ub);
                        if (kb != ka) { double l0 = ua, l1 = ub; for (int t = 0; t < 12; t++) { double mid = 0.5 * (l0 + l1); if (Keep(M, a + (b - a) * mid) == ka) l0 = mid; else l1 = mid; } double u = 0.5 * (l0 + l1); p2.Add(a + (b - a) * u, tp.Normals[i], tp.Heights[i - 1] + (tp.Heights[i] - tp.Heights[i - 1]) * u, tp.Widths[i - 1] + (tp.Widths[i] - tp.Widths[i - 1]) * u, tp.Flows[i], tp.Roles[i], tp.Layers[i]); src.Add(i); keep.Add(ka); ins.Add(true); }
                        ka = kb; ua = ub;
                    }
                    p2.AddFrom(tp, i); src.Add(i); keep.Add(ka); ins.Add(false);
                }
                else { p2.AddFrom(tp, i); src.Add(i); keep.Add(true); ins.Add(false); }
            }
            int cut = keep.Count(x => !x);
            if (cut == 0) { r.Path = tp.Duplicate(); for (int i = 0; i < tp.Count; i++) { r.Source.Add(i); r.Weight.Add(1); r.Inserted.Add(false); } r.Identity = true; r.HeightAfterMin = r.HeightAfterMax = r.HeightBefore; PathReshape.Numbers(r, tp, o); r.Report.Add("Nothing is cut: the field is nowhere below the height of the part."); return PathReshape.Verdict(r, o); }
            // ---- the pieces that stay: maximal runs of kept moves
            var pieces = new List<Piece>(); int at = 0;
            while (at < p2.Count)
            {
                if (!keep[at]) { at++; continue; }
                int e = at; while (e + 1 < p2.Count && keep[e + 1]) e++;
                int from = at > 0 ? at - 1 : 0; // the piece starts where the move before it was cut
                double len = 0; for (int i = from + 1; i <= e; i++) len += p2.Points[i].DistanceTo(p2.Points[i - 1]);
                if (len > 3 * st.BeadWidth || pieces.Count == 0) pieces.Add(new Piece { From = from, To = e, Len = len });
                at = e + 1;
            }
            // which piece lies on which: the one below that most of it rests on
            var cells = new Dictionary<long, List<int[]>>(); double cell = 2 * st.BeadWidth; // cell -> piece, point
            long Key(Point3d q) => ((long)(int)Math.Floor(q.X / cell) << 32) ^ (uint)(int)Math.Floor(q.Y / cell);
            for (int k = 0; k < pieces.Count; k++)
            {
                var pc = pieces[k];
                if (k > 0)
                {
                    var votes = new Dictionary<int, int>(); int step = Math.Max(1, (pc.To - pc.From) / 60);
                    for (int i = pc.From; i <= pc.To; i += step)
                        if (cells.TryGetValue(Key(p2.Points[i]), out var l)) { int top = -1; double zt = double.MinValue; foreach (var u in l) { double z = p2.Points[u[1]].Z; if (z < p2.Points[i].Z - 0.2 * st.LayerHeight && z > zt) { zt = z; top = u[0]; } } if (top >= 0) { votes.TryGetValue(top, out int c); votes[top] = c + 1; } }
                    pc.Parent = votes.Count > 0 ? votes.OrderByDescending(x => x.Value).ThenByDescending(x => x.Key).First().Key : k - 1;
                    pieces[pc.Parent].Kids.Add(k);
                }
                long had = long.MinValue;
                for (int i = pc.From; i <= pc.To; i++)
                {
                    Point3d a0 = p2.Points[Math.Max(pc.From, i - 1)], b0 = p2.Points[i]; int n = Math.Max(1, (int)(a0.DistanceTo(b0) / (0.5 * cell)));
                    for (int j = 0; j <= n; j++) { long key = Key(a0 + (b0 - a0) * (j / (double)n)); if (key == had) continue; had = key; if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int[]>(); l.Add(new[] { k, i }); }
                }
            }
            // ---- in the order they are printed: one high place after the other, forth and back; if the head does not fit
            // between the high places (strips of a meander), lap by lap with a travel move wherever the lap is interrupted
            Toolpath outp = null; int travels = 0, links = 0; bool byLap = false;
            for (int tryNo = 0; tryNo < 2; tryNo++)
            {
            byLap = tryNo == 1; r.Source.Clear(); r.Weight.Clear(); r.Inserted.Clear(); travels = links = 0;
            outp = new Toolpath { Settings = st, Warp = tp.Warp }; bool started = false; double topSoFar = double.MinValue;
            void Lay(int k, bool back)
            {
                var pc = pieces[k];
                if (!started) { for (int i = 0; i <= pc.To; i++) { outp.AddFrom(p2, i); r.Source.Add(src[i]); r.Weight.Add(1); r.Inserted.Add(ins[i]); if (LapBelow.Prints(p2, i)) topSoFar = Math.Max(topSoFar, p2.Points[i].Z); } started = true; return; }
                Point3d end = outp.Points[outp.Count - 1]; int s0 = back ? pc.To : pc.From; Point3d go = p2.Points[s0]; double gap = end.DistanceTo(go);
                double dx = go.X - end.X, dy = go.Y - end.Y, flat = Math.Sqrt(dx * dx + dy * dy);
                if (flat <= 8 * st.BeadWidth + 4 * st.LayerHeight)
                {
                    // a step up, printed, like the seam between two layers
                    outp.Add(go, p2.Normals[s0], p2.Heights[s0], p2.Widths[s0], p2.Flows[Math.Max(1, s0)] > 0 ? 1 : 1, PathRole.Seam, p2.Layers[s0]); links++;
                }
                else
                {
                    double zt = Math.Max(topSoFar, Math.Max(end.Z, go.Z)) + 2 * st.LayerHeight + 3;
                    outp.Add(new Point3d(end.X, end.Y, zt), p2.Normals[s0], p2.Heights[s0], p2.Widths[s0], 0, PathRole.Travel, p2.Layers[s0]);
                    outp.Add(new Point3d(go.X, go.Y, zt), p2.Normals[s0], p2.Heights[s0], p2.Widths[s0], 0, PathRole.Travel, p2.Layers[s0]);
                    outp.Add(go, p2.Normals[s0], p2.Heights[s0], p2.Widths[s0], 0, PathRole.Travel, p2.Layers[s0]); travels++;
                    for (int q = 0; q < 2; q++) { r.Source.Add(src[s0]); r.Weight.Add(0); r.Inserted.Add(true); }
                }
                r.Source.Add(src[s0]); r.Weight.Add(0); r.Inserted.Add(true);
                for (int i = pc.From; i <= pc.To; i++) topSoFar = Math.Max(topSoFar, p2.Points[i].Z);
                if (!back) for (int i = pc.From + 1; i <= pc.To; i++) { outp.AddFrom(p2, i); r.Source.Add(src[i]); r.Weight.Add(1); r.Inserted.Add(ins[i]); }
                else for (int i = pc.To - 1; i >= pc.From; i--) { outp.Add(p2.Points[i], p2.Normals[i], p2.Heights[i], p2.Widths[i], p2.Flows[i + 1], p2.Roles[i + 1], p2.Layers[i + 1]); r.Source.Add(src[i + 1]); r.Weight.Add(1); r.Inserted.Add(true); }
            }
            var stack = new Stack<int>(); if (!byLap) stack.Push(0); else for (int k = 0; k < pieces.Count; k++) { Cancel.Check(); bool back = started && outp.Points[outp.Count - 1].DistanceToSquared(p2.Points[pieces[k].To]) < outp.Points[outp.Count - 1].DistanceToSquared(p2.Points[pieces[k].From]) && pieces.Count(q => q.From > 0 && p2.Layers[q.To] == p2.Layers[pieces[k].To]) == 1; Lay(k, back); }
            while (stack.Count > 0)
            {
                int k = stack.Pop(); Cancel.Check();
                bool back = false;
                if (started) { Point3d end = outp.Points[outp.Count - 1]; back = end.DistanceToSquared(p2.Points[pieces[k].To]) < end.DistanceToSquared(p2.Points[pieces[k].From]); }
                Lay(k, back);
                // the kid closest to where the nozzle is goes next (it lies on top of the stack)
                Point3d now = outp.Points[outp.Count - 1];
                foreach (int kid in pieces[k].Kids.OrderByDescending(q => Math.Min(now.DistanceToSquared(p2.Points[pieces[q].From]), now.DistanceToSquared(p2.Points[pieces[q].To])))) stack.Push(kid);
            }
            if (byLap || pieces.Count < 3 || MultiAxisCheck.Run(outp, new MultiAxisCheck.Options { Head = o.Head }).Collisions.Count == 0) break;
            }
            // layers in the order of printing must go up for the rest of the program: they keep the number of their lap
            r.Path = outp;
            // ---- way out: up first, then as before
            { double top = outp.Points.Where((q, i) => LapBelow.Prints(outp, i)).Max(q => q.Z); Point3d end = outp.Points[outp.Count - 1]; int lay = outp.Layers[outp.Count - 1];
              if (lastP + 1 < tp.Count)
              {
                  outp.Add(new Point3d(end.X, end.Y, top + 20), Vector3d.ZAxis, st.LayerHeight, st.BeadWidth, 0, PathRole.Travel, lay); r.Source.Add(lastP + 1); r.Weight.Add(0); r.Inserted.Add(true);
                  Point3d x = tp.Points[tp.Count - 1]; outp.Add(new Point3d(x.X, x.Y, top + 20), Vector3d.ZAxis, st.LayerHeight, st.BeadWidth, 0, PathRole.Travel, lay); r.Source.Add(tp.Count - 1); r.Weight.Add(0); r.Inserted.Add(false);
              } }
            if (pct > 99.9) { double sh = PathReshape.PartE(outp) / Math.Max(1e-9, PathReshape.PartE(tp)); r.Warnings.Add(Inv.F($"Cutting laps only takes material away ({100 * sh:0} % stays). For the same material slice the part about {1 / Math.Max(0.05, sh):0.00} times as high first, then cut.")); }
            double hLo = double.MaxValue, hHi = 0; foreach (var q in PathReshape.Places(tp)) { double z = Math.Min(M.ZTop, M.ZFlat + (M.ZTop - M.ZFlat) * Math.Min(1, M.Factor(q.X, q.Y))); hLo = Math.Min(hLo, z - M.ZBase); hHi = Math.Max(hHi, z - M.ZBase); }
            r.HeightAfterMin = hLo; r.HeightAfterMax = hHi; r.Added = ins.Count(x => x); r.FirstMoved = -1;
            r.Report.Add(Inv.F($"Cut laps: {pieces.Count} piece(s), {links} turn(s) with a printed step up, {travels} travel move(s) between separate high places. The top has steps of one layer ({st.LayerHeight:0.##} mm).") + (byLap ? " The head does not fit between the high places when they are printed one after the other, so they grow together, lap by lap: that takes a travel move at every gap. This way is meant for one or two high places, not for a meander." : ""));
            if (travels > 0) r.Warnings.Add(Inv.F($"{travels} travel move(s) without extrusion: a pellet extruder strings there. One single high place needs none."));
            var map2 = r.Map; r.Cut01 = (x, y) => (Math.Min(M.ZTop, M.ZFlat + (M.ZTop - M.ZFlat) * Math.Min(1, M.Factor(x, y))) - M.ZBase) / Math.Max(1e-9, M.ZTop - M.ZBase);
            r.Map = new ShapeMap { ZBase = map2.ZBase, ZFlat = map2.ZFlat, ZKeep = map2.ZKeep, ZTop = map2.ZTop };
            PathReshape.Numbers(r, tp, o);
            return PathReshape.Verdict(r, o);
        }

        // ------------------------------------------------------------------ comparison

        public class Row { public ReshapeVariant Variant; public ShapeField Field; public PathReshape.Result Result; }

        /// <summary>Every field in every variant, with the same settings.</summary>
        public static List<Row> Compare(Toolpath tp, IList<ShapeField> fields, IList<ReshapeVariant> variants, ReshapeOptions o)
        {
            var rows = new List<Row>();
            foreach (var f in fields) foreach (var v in variants) { Cancel.Check(); rows.Add(new Row { Variant = v, Field = f, Result = Run(tp, f, v, o) }); }
            return rows;
        }

        /// <summary>The comparison as a table (Markdown).</summary>
        public static List<string> Table(IList<Row> rows)
        {
            var l = new List<string> { "| Variant | Field | Material % | Height mm | Layer mm | Width mm | Tilt deg | Turn deg/s | Slope deg | Flow mm3/s | Time min | Travel | Head | Verdict |", "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|" };
            foreach (var row in rows)
            {
                var r = row.Result; var c = r.Check;
                if (r.Path == null) { l.Add("| " + Short(row.Variant) + " | " + row.Field.Source + " | | | | | | | | | | | | REFUSED: " + r.Refused + " |"); continue; }
                var pr = PathEngine.Process(r.Path, false);
                l.Add("| " + Short(row.Variant) + " | " + row.Field.Source.Replace('|', '/') + Inv.F($" | {100 * r.MaterialShare:0.0} | {r.HeightAfterMin:0.#} - {r.HeightAfterMax:0.#} | {c?.MinHeight ?? 0:0.00} - {c?.MaxHeight ?? 0:0.00} | {r.WidthMin:0.0} - {r.WidthMax:0.0} | {c?.MaxTilt ?? 0:0.0} | {c?.MaxTiltPerSecond ?? 0:0.0} | {c?.MaxSlope ?? 0:0.0} | {c?.MaxFlow ?? 0:0} | {pr.Minutes:0.0} | {r.Travels} | {(c == null ? "-" : c.Collisions.Count == 0 ? "free" : c.Collisions.Count + " hits")} | {(c == null || c.Ok ? "OK" : c.First)}{(r.BeforeNote != null ? " (before: " + r.BeforeNote + ")" : "")}{(r.Cut ? ", limits cut" : "")} |"));
            }
            return l;
        }
    }
}
