using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Checks for non-planar paths printed with a vertical nozzle (gantry printer, no A/B axes):
    ///
    /// 1. Measured bead height. For every printing point p the closest point q (in plan, XY) on the
    ///    path of the layer below is found; dz = p.z - q.z is the bead height the nozzle really
    ///    leaves. It is compared with the bead width (too high: above 0.8 x width), with the layer
    ///    height (too thin: below 0.4 x layer height), and with the height the extrusion (E) was
    ///    worked out for. Paths whose E follows the shell law (BeadLaw) are measured by it; E that
    ///    follows the 2024 law (over-extrudes leaning laps) is recognised and reported.
    /// 2. Head clearance. The head is a cone (tip radius r0, half angle theta, length Lc) and above
    ///    it a cylinder (radius R). Every point q printed earlier that lies more than half a layer
    ///    above the nozzle tip must stay outside the head: r_xy >= (dz &lt; Lc ? r0 + dz tan theta : R)
    ///    + half a bead width (the stretch printed just before, within twice the head radius along the path,
    ///    is left to the next test). Within a layer the path may climb at most as steeply as the cone's flank:
    ///    |dz/ds| &lt;= tan(90 - theta).
    /// </summary>
    public static class NonPlanarCheck
    {
        public class Head
        {
            public double TipRadius = 4, HalfAngle = 30, ConeLength = 60, Radius = 40;
            /// <summary>A bead printed before may reach this far (mm) into the head's outline before it counts as a
            /// collision: the nozzle grazes the soft flank of the bead beside it. Calibrated on the UNIKAT HEAD V2
            /// (Ginger One, 2024), which printed fine with grazes of up to 0.5 mm (see Run).</summary>
            public double Graze = 0.6;
            public override string ToString() => Inv.F($"head: tip r {TipRadius:0.#} mm, cone {HalfAngle:0.#} deg x {ConeLength:0} mm, body r {Radius:0} mm");

            /// <summary>The head as it is checked, standing with its tip on this point: the cone from the tip
            /// up to where it reaches the body radius (or the cone length), then the body, 150 mm high.</summary>
            public Mesh ToMesh(Point3d tip, int segments = 36)
            {
                double tan = Math.Tan(HalfAngle * Math.PI / 180.0);
                double coneTop = Math.Min(Radius, TipRadius + ConeLength * tan);
                var rings = new List<KeyValuePair<double, double>> // height above the tip, radius
                {
                    new KeyValuePair<double, double>(0, Math.Max(0.5, TipRadius)),
                    new KeyValuePair<double, double>(ConeLength, coneTop),
                    new KeyValuePair<double, double>(ConeLength, Radius),
                    new KeyValuePair<double, double>(ConeLength + 150, Radius),
                };
                var m = new Mesh();
                foreach (var ring in rings)
                    for (int k = 0; k < segments; k++)
                    {
                        double a = 2 * Math.PI * k / segments;
                        m.Vertices.Add(tip.X + ring.Value * Math.Cos(a), tip.Y + ring.Value * Math.Sin(a), tip.Z + ring.Key);
                    }
                for (int r = 0; r + 1 < rings.Count; r++)
                    for (int k = 0; k < segments; k++)
                    {
                        int a = r * segments + k, b = r * segments + (k + 1) % segments;
                        m.Faces.AddFace(a, b, b + segments, a + segments);
                    }
                int bottom = m.Vertices.Add(tip), top = m.Vertices.Add(tip.X, tip.Y, tip.Z + ConeLength + 150);
                int last = (rings.Count - 1) * segments;
                for (int k = 0; k < segments; k++)
                {
                    m.Faces.AddFace(bottom, (k + 1) % segments, k);
                    m.Faces.AddFace(top, last + k, last + (k + 1) % segments);
                }
                m.Normals.ComputeNormals(); m.Compact();
                return m;
            }
        }

        public class Result
        {
            public bool Ok = true;
            public List<string> Report = new List<string>();
            /// <summary>Per path point: measured bead height (vertical gap to the layer below); 0 = not measured.</summary>
            public List<double> BeadHeights = new List<double>();
            /// <summary>Path points where the head hits what was printed before.</summary>
            public List<Point3d> Collisions = new List<Point3d>();
            /// <summary>Path points where the measured bead height is out of range.</summary>
            public List<Point3d> BadHeights = new List<Point3d>();
            public double MinHeight = double.MaxValue, MaxHeight, MaxDeviation, MeanDeviation;
            public int Measured, TooHigh, TooThin, Deviating, CollisionMoves, SteepMoves, Ramp, GrazeMoves;
            /// <summary>Deepest reach of an earlier bead into the head's outline (mm), grazes included.</summary>
            public double MaxIntrusion;
            /// <summary>True when the extrusion follows the shell law (vertical gap to the lap below, or the sideways
            /// step x skin thickness / bead width where the laps step mostly sideways) better than the plain vertical
            /// gap; the heights are then measured by it.</summary>
            public bool Law;
            /// <summary>True when the extrusion follows the law of the 2024 head (UNIKAT HEAD V2, used up to 1.3.0) best:
            /// it over-extrudes leaning laps. The heights are then measured by the shell law, and the excess is reported.</summary>
            public bool LegacyV2;
            /// <summary>With LegacyV2: E above the shell law along the measured path, and per point the 95th percentile (0.1 = 10 %).</summary>
            public double V2Excess, V2ExcessMax;
            /// <summary>Per path point: bead height by the shell law (BeadLaw.Height); 0 = not measured.</summary>
            public List<double> LawHeights = new List<double>();
            /// <summary>Per path point: bead height by the 2024 law (BeadLaw.HeightV2); 0 = not measured.</summary>
            public List<double> V2Heights = new List<double>();
            /// <summary>Of Measured: points compared with the height used for E (Compared), and the ones left out of that comparison because the used height is below a quarter of the layer height (Tiny: run-in, run-out, a bead that fades to nothing; a percentage of almost nothing says nothing).</summary>
            public int Compared, Tiny;
            public double MaxSlope;
        }

        private static bool Printing(Toolpath tp, int i) => i > 0 && tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;

        public static Result Run(Toolpath tp, Head head)
        {
            var r = new Result();
            if (tp == null || tp.Count < 2) { r.Ok = false; r.Report.Add("Empty path."); return r; }
            head = head ?? new Head();
            var st = tp.Settings;
            double w = st.BeadWidth, h = Math.Max(0.05, st.LayerHeight);
            int n = tp.Count;
            for (int i = 0; i < n; i++) { r.BeadHeights.Add(0); r.LawHeights.Add(0); r.V2Heights.Add(0); }
            var measuredAt = new List<int>(); // points measured (not ramp)
            var skinAt = new HashSet<int>();  // points where the skin term of the shell law sets the height

            // ------------------------------------------------ 1. measured bead height
            var byLayer = new SortedDictionary<int, List<int>>();
            for (int i = 1; i < n; i++)
            {
                if (!Printing(tp, i) || tp.Roles[i] == PathRole.Skirt) continue;
                if (!byLayer.TryGetValue(tp.Layers[i], out var list)) byLayer[tp.Layers[i]] = list = new List<int>();
                list.Add(i);
            }
            double cell = Math.Max(2 * w, 5);
            // path length up to every point: an earlier stretch of the same layer counts as "below" only
            // when it is at least two bead widths back along the path (the last flat turn of a spiral
            // lies on the climbing turn of the same layer)
            var along = new double[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]);
            Dictionary<long, List<int>> below = null; // segments of the layer below (end point index), by XY cell
            double sumDev = 0; int notMeasured = 0;
            int secondLayer = byLayer.Count > 1 ? byLayer.Keys.ElementAt(1) : int.MinValue, lastLayer = byLayer.Count > 0 ? byLayer.Keys.Last() : int.MinValue;
            foreach (var kv in byLayer)
            {
                var idx = kv.Value;
                var grid = new Dictionary<long, List<int>>(); // this layer, filled while it is printed
                for (int k = 0; k < idx.Count; k++)
                {
                    int i = idx[k];
                    if (below != null && tp.Roles[i] != PathRole.Seam) // the ramp between two layers climbs on purpose
                    {
                        Point3d p = tp.Points[i];
                        if (!ClosestBelow(tp, below, grid, along, along[i], cell, p, 2 * w, 0.25 * w, 0.25 * h, 2 * w, out double zq, out double law, out double lawV2, out bool skinPart)) notMeasured++;
                        else
                        {
                            double dz = p.Z - zq;
                            double used = tp.Heights[i];
                            // the start and end of a spiral run out to nothing on purpose, and the extrusion with them
                            bool ramp = (kv.Key == secondLayer || kv.Key == lastLayer) && used < 0.4 * h && Math.Min(Math.Abs(dz - used), Math.Abs(law - used)) <= 0.05 * h;
                            r.BeadHeights[i] = dz; r.LawHeights[i] = law; r.V2Heights[i] = lawV2; if (skinPart) skinAt.Add(i);
                            if (ramp) r.Ramp++;
                            else measuredAt.Add(i);
                        }
                    }
                    // this move is now material that later beads may lie on
                    if (k == 0 || idx[k - 1] != i - 1) continue;
                    Point3d p0 = tp.Points[i - 1], p1 = tp.Points[i];
                    int x0 = (int)Math.Floor(Math.Min(p0.X, p1.X) / cell), x1 = (int)Math.Floor(Math.Max(p0.X, p1.X) / cell);
                    int y0 = (int)Math.Floor(Math.Min(p0.Y, p1.Y) / cell), y1 = (int)Math.Floor(Math.Max(p0.Y, p1.Y) / cell);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 400) { x1 = x0; y1 = y0; }
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long key = Key(gx, gy);
                            if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                            l.Add(i);
                        }
                }
                if (grid.Count > 0) below = grid;
            }
            // Which height did the extrusion follow: the vertical gap, the shell law, or the law of the 2024 head?
            // The vertical gap and the shell law are both right (the shell law is the vertical gap plus the skin
            // where the laps step sideways); the one that fits better judges the heights. A path whose E follows
            // the 2024 law (a file made with 1.3.0 or older, the G-code of the printed head) is judged by the
            // shell law, so its extra material on the leaning laps shows.
            double devGap = 0, devLaw = 0, devV2 = 0; int nDev = 0;
            foreach (int i in measuredAt)
            {
                double used = tp.Heights[i];
                if (used < 0.25 * h) continue;
                devGap += Math.Abs(r.BeadHeights[i] - used) / used; devLaw += Math.Abs(r.LawHeights[i] - used) / used;
                devV2 += Math.Abs(r.V2Heights[i] - used) / used; nDev++;
            }
            r.Law = nDev > 0 && devLaw < 0.8 * devGap;
            r.LegacyV2 = nDev > 0 && devV2 < 0.8 * Math.Min(devGap, devLaw);
            if (r.LegacyV2)
            {
                r.Law = true;
                // material: E along the path against E by the shell law; per point: the 95th percentile
                double eUsed = 0, eLaw = 0; var ratios = new List<double>();
                foreach (int i in measuredAt)
                {
                    double used = tp.Heights[i], law = r.LawHeights[i];
                    if (used < 0.25 * h || law < 0.25 * h) continue;
                    double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                    eUsed += used * len; eLaw += law * len;
                    ratios.Add(used / law - 1);
                }
                r.V2Excess = eLaw > 0 ? eUsed / eLaw - 1 : 0;
                if (ratios.Count > 0) { ratios.Sort(); r.V2ExcessMax = ratios[(int)(0.95 * (ratios.Count - 1))]; }
            }
            foreach (int i in measuredAt)
            {
                Point3d p = tp.Points[i];
                double dz = r.Law ? r.LawHeights[i] : r.BeadHeights[i];
                double used = tp.Heights[i];
                r.Measured++;
                if (dz < r.MinHeight) r.MinHeight = dz;
                if (dz > r.MaxHeight) r.MaxHeight = dz;
                bool bad = false;
                if (dz > 0.8 * w) { r.TooHigh++; bad = true; }
                // where the laps step mostly sideways the shell law lays a skin of Skin Thickness (at least 0.4 x layer
                // height by construction): its height t = A / w is an equivalent, not a squashed bead, and is not judged
                if (dz < 0.4 * h && !(r.Law && skinAt.Contains(i))) { r.TooThin++; bad = true; }
                if (used < 0.25 * h) r.Tiny++;
                else
                {
                    r.Compared++;
                    double dev = Math.Abs(dz - used) / used;
                    sumDev += dev;
                    if (dev > r.MaxDeviation) r.MaxDeviation = dev;
                    if (dev > 0.10) r.Deviating++;
                }
                if (bad) r.BadHeights.Add(p);
            }
            if (r.Measured > 0)
            {
                r.MeanDeviation = r.Compared > 0 ? sumDev / r.Compared : 0;
                double tooHigh = 100.0 * r.TooHigh / r.Measured, tooThin = 100.0 * r.TooThin / r.Measured, dev = r.Compared > 0 ? 100.0 * r.Deviating / r.Compared : 0;
                if (r.LegacyV2)
                    r.Report.Add(Inv.F($"OLD EXTRUSION LAW: E matches the law of the 2024 head (t = dz + (h - dz) dxy / w, used up to version 1.3.0) better than the shell law. That law over-extrudes leaning laps: here {100 * r.V2Excess:0.#} % more material than the shell law (w x dz per lap, or the sideways step x skin thickness where the laps step sideways), at 5 % of the points more than {100 * r.V2ExcessMax:0} %. Slice the part again with this version."));
                r.Report.Add(Inv.F($"Bead height ({(r.Law ? "shell law: vertical gap to the lap below, or sideways step x skin thickness / bead width where the laps step sideways" : "vertical gap to the layer below")}, {r.Measured} points): {r.MinHeight:0.##} to {r.MaxHeight:0.##} mm. ") +
                             Inv.F($"Allowed {0.4 * h:0.##} (0.4 x layer height) to {0.8 * w:0.##} mm (0.8 x bead width)") + (r.Law && skinAt.Count > 0 ? Inv.F($"; at {skinAt.Count} points the laps step mostly sideways and form a skin {st.EffectiveSkin:0.##} mm thick (Skin Thickness), which is not judged by the 0.4 x layer height rule.") : "."));
                if (r.TooHigh == 0 && r.TooThin > 0 && r.Law && r.TooThin <= 0.01 * r.Measured)
                {
                    // The last rings that close a crown lie almost on top of each other and step less than a bead
                    // sideways: their beads are thin by nature, and a few of them close the hole. The shell law also
                    // gives its least material where the laps step about as far sideways as t_skin / w times up
                    // (for laps one layer height apart along the surface: h x t_skin / sqrt(w^2 + t_skin^2), 0.4 h
                    // for a 4 mm bead and 1.75 mm layers): a narrow band of points there may read just under 0.4 h. No allowance above
                    // 0.8 x bead width: up to 1.3.0 such points were accepted (0.5 %, up to 1.1 x bead width) because the
                    // 2024 head printed them, but those heights came from its law, which over-extrudes leaning laps; by
                    // the shell law a bead is never higher than the vertical gap or the skin.
                    r.Report.Add(Inv.F($"Bead height OK with a note: {r.TooThin} points ({tooThin:0.#} %) below 0.4 x layer height, where the laps step sideways (towards the crown, where the last rings close it). Accepted up to 1 % of the points. See Bad Heights."));
                }
                else if (r.TooHigh > 0 || r.TooThin > 0)
                {
                    r.Ok = false;
                    r.Report.Add(Inv.F($"BEAD HEIGHT OUT OF RANGE: {r.TooHigh} points ({tooHigh:0.#} %) above 0.8 x bead width (the bead does not squash and bonds poorly), ") +
                                 Inv.F($"{r.TooThin} points ({tooThin:0.#} %) below 0.4 x layer height (pressed flat). See Bad Heights."));
                }
                else r.Report.Add("Bead height OK at every point.");
                string tiny = r.Tiny > 0 ? Inv.F($" {r.Tiny} point(s) where the height used for E is below a quarter of the layer height (the bead fades in or out there) are not part of this comparison.") : "";
                if (r.Compared == 0)
                    r.Report.Add("Extrusion against the measured bead height: no point to compare." + tiny);
                else if (r.MeanDeviation > 0.10 || r.Deviating > 0.05 * r.Compared)
                {
                    r.Ok = false;
                    r.Report.Add(Inv.F($"EXTRUSION DOES NOT FIT: at {dev:0.#} % of the points the bead height used for E differs by more than 10 % from the measured one (mean {100 * r.MeanDeviation:0.#} %, largest {100 * r.MaxDeviation:0} %, {r.Compared} points compared).") + tiny);
                }
                else r.Report.Add(Inv.F($"Extrusion fits the measured bead height: mean difference {100 * r.MeanDeviation:0.#} %, {dev:0.#} % of the points more than 10 % off (largest {100 * r.MaxDeviation:0} %, {r.Compared} points compared).") + tiny);
                if (r.Ramp > 0) r.Report.Add(Inv.F($"{r.Ramp} point(s) lie where the spiral starts or ends (second and last layer) and the bead runs out to nothing on purpose; the extrusion follows them. Not counted above."));
                if (notMeasured > 0) r.Report.Add(Inv.F($"{notMeasured} point(s) have no layer below within two bead widths (first bead of an overhang or a bridge): not measured. Path Check tests their support."));
            }
            else r.Report.Add("Bead height: only one layer, nothing to measure.");

            // ------------------------------------------------ 2. head clearance
            double tan = Math.Tan(Math.Max(0, Math.Min(89, head.HalfAngle)) * Math.PI / 180.0);
            double R = Math.Max(head.Radius, head.TipRadius);
            double reach = R + 0.5 * w;
            double hc = Math.Max(1.0, head.Radius);
            int rings = (int)Math.Ceiling(reach / hc);
            var cells = new Dictionary<long, List<int>>();
            var cellTop = new Dictionary<long, double>();
            var shown = new HashSet<long>();
            var hits = new List<string>();
            for (int i = 1; i < n; i++)
            {
                if (!Printing(tp, i)) continue;
                Point3d p = tp.Points[i];
                int cx = (int)Math.Floor(p.X / hc), cy = (int)Math.Floor(p.Y / hc);
                bool hit = false, grazed = false;
                for (int gx = cx - rings; gx <= cx + rings && !hit; gx++)
                    for (int gy = cy - rings; gy <= cy + rings && !hit; gy++)
                    {
                        long key = Key(gx, gy);
                        if (!cellTop.TryGetValue(key, out double top) || top - p.Z <= 0.5 * h) continue;
                        foreach (int j in cells[key])
                        {
                            Point3d q = tp.Points[j];
                            double dz = q.Z - p.Z;
                            if (dz <= 0.5 * h) continue;
                            double dx = q.X - p.X, dy = q.Y - p.Y;
                            double rxy = Math.Sqrt(dx * dx + dy * dy);
                            // the bead just laid behind the nozzle (also across a layer change): the in-layer slope test below covers it
                            if (along[i] - along[j] < 2 * reach) continue;
                            double need = (dz < head.ConeLength ? head.TipRadius + dz * tan : R) + 0.5 * w;
                            if (rxy >= need) continue;
                            if (need - rxy > r.MaxIntrusion) r.MaxIntrusion = need - rxy;
                            if (need - rxy <= head.Graze) { grazed = true; continue; }
                            hit = true;
                            r.CollisionMoves++;
                            r.Collisions.Add(p);
                            long pair = ((long)tp.Layers[i] << 32) ^ (uint)tp.Layers[j];
                            if (hits.Count < 20 && shown.Add(pair))
                            {
                                hits.Add(Inv.F($"HEAD COLLISION at move {i}, layer {tp.Layers[i] + 1}, with layer {tp.Layers[j] + 1} (dz {dz:0.#} mm, r {rxy:0.#} mm, needs {need:0.#} mm)"));
                            }
                            break;
                        }
                    }
                if (grazed && !hit) r.GrazeMoves++;
                long own = Key(cx, cy);
                if (!cells.TryGetValue(own, out var mine)) { cells[own] = mine = new List<int>(); cellTop[own] = p.Z; }
                mine.Add(i);
                if (p.Z > cellTop[own]) cellTop[own] = p.Z;
            }

            // the path must not climb within a layer more steeply than the flank of the cone
            double maxSlope = Math.Tan((90 - Math.Max(0, Math.Min(89, head.HalfAngle))) * Math.PI / 180.0);
            for (int i = 2; i < n; i++)
            {
                if (!Printing(tp, i) || !Printing(tp, i - 1) || tp.Layers[i] != tp.Layers[i - 1]) continue;
                Vector3d d = tp.Points[i] - tp.Points[i - 1];
                double ds = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (ds < 0.2) continue;
                double slope = Math.Abs(d.Z) / ds;
                if (slope > r.MaxSlope) r.MaxSlope = slope;
                if (slope > maxSlope) { r.SteepMoves++; if (r.Collisions.Count < 5000) r.Collisions.Add(tp.Points[i]); }
            }

            string graze = r.GrazeMoves > 0 ? Inv.F($" At {r.GrazeMoves} move(s) the nozzle grazes the flank of a bead beside it by up to {Math.Min(head.Graze, r.MaxIntrusion):0.##} mm (allowed {head.Graze:0.##} mm: the printed head of 2024 did this at about 70 moves, by up to 0.5 mm, and printed fine).") : "";
            if (r.CollisionMoves == 0 && r.SteepMoves == 0)
                r.Report.Add(Inv.F($"Head clearance OK ({head}): the head never reaches what was printed before; the path climbs at most {Math.Atan(r.MaxSlope) * 180 / Math.PI:0} deg within a layer (allowed {90 - head.HalfAngle:0}).") + graze);
            else
            {
                r.Ok = false;
                if (r.CollisionMoves > 0)
                {
                    r.Report.Add(Inv.F($"HEAD COLLISION: at {r.CollisionMoves} move(s) the head ({head}) hits beads printed before, by more than {head.Graze:0.##} mm (deepest {r.MaxIntrusion:0.##} mm). With a vertical nozzle a layer must not lie lower than the layers already printed next to it.") + graze);
                    r.Report.AddRange(hits);
                }
                if (r.SteepMoves > 0) r.Report.Add(Inv.F($"TOO STEEP: {r.SteepMoves} move(s) climb within a layer more steeply than the cone's flank allows (up to {Math.Atan(r.MaxSlope) * 180 / Math.PI:0} deg, allowed {90 - head.HalfAngle:0} deg). The cone would scrape the bead beside it."));
            }
            return r;
        }

        private static long Key(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;

        /// <summary>The material the bead rests on: among the moves that lie within slack of the closest one
        /// (in plan, at most maxDist away), the highest one that is not above p. Candidates are the layer below
        /// (up to "above" higher than p: a collapsed gap is measured, not skipped) and stretches of this layer
        /// at least gap back along the path that lie clearly lower. (Where a spiral turn ends above its own
        /// start, both lie under the nozzle.)</summary>
        private static bool ClosestBelow(Toolpath tp, Dictionary<long, List<int>> below, Dictionary<long, List<int>> same, double[] along, double alongP,
            double cell, Point3d p, double maxDist, double slack, double above, double gap, out double z, out double law, out double lawV2, out bool skinPart)
        {
            z = 0; law = 0; lawV2 = 0; skinPart = false;
            double best3 = double.MaxValue; Point3d q3 = Point3d.Unset;
            int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell);
            int ring = (int)Math.Ceiling(maxDist / cell);
            var cand = new List<KeyValuePair<double, double>>(); // distance, z
            double best = maxDist;
            for (int pass = 0; pass < 2; pass++)
            {
                var grid = pass == 0 ? below : same;
                if (grid == null) continue;
                for (int gx = cx - ring; gx <= cx + ring; gx++)
                    for (int gy = cy - ring; gy <= cy + ring; gy++)
                    {
                        if (!grid.TryGetValue(Key(gx, gy), out var list)) continue;
                        foreach (int b in list)
                        {
                            Point3d a = tp.Points[b - 1], c = tp.Points[b];
                            double ex = c.X - a.X, ey = c.Y - a.Y;
                            double l2 = ex * ex + ey * ey;
                            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ex + (p.Y - a.Y) * ey) / l2));
                            double qx = a.X + ex * t - p.X, qy = a.Y + ey * t - p.Y;
                            double d = Math.Sqrt(qx * qx + qy * qy);
                            if (d > maxDist) continue;
                            double zq = a.Z + (c.Z - a.Z) * t;
                            // the moves just before p (also across a layer change) are not what it lies on
                            if (alongP - along[b] < gap) continue;
                            // a bead of this layer carries p only where it lies clearly lower (a spiral turn);
                            // the wall next to it at the same height does not (fins, the first bead of an overhang)
                            if (pass == 1 && zq > p.Z - above) continue;
                            if (d < best) best = d;
                            cand.Add(new KeyValuePair<double, double>(d, zq));
                            // the closest point in space (for the law): on the segment, not only in plan
                            if (zq <= p.Z + above)
                            {
                                Vector3d ab = c - a; double L2 = ab.SquareLength;
                                double t3 = L2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / L2));
                                Point3d qq = a + ab * t3; double d3 = qq.DistanceToSquared(p);
                                if (d3 < best3) { best3 = d3; q3 = qq; }
                            }
                        }
                    }
            }
            bool found = false; double zBest = double.MinValue, zAny = 0, dAny = double.MaxValue;
            foreach (var kv in cand)
            {
                if (kv.Key > best + slack) continue;
                if (kv.Key < dAny) { dAny = kv.Key; zAny = kv.Value; }
                if (kv.Value <= p.Z + above && kv.Value > zBest) { zBest = kv.Value; found = true; }
            }
            double bw = tp.Settings.BeadWidth, skin = tp.Settings.EffectiveSkin;
            if (!found && dAny == double.MaxValue) return false;
            z = found ? zBest : zAny;
            law = q3.IsValid ? BeadLaw.Height(p, q3, bw, skin) : p.Z - z;
            skinPart = q3.IsValid && law > Math.Max(0, p.Z - q3.Z) + 1e-9;
            lawV2 = q3.IsValid ? BeadLaw.HeightV2(p, q3, bw) : p.Z - z;
            return true;
        }
    }
}
