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
    ///    worked out for.
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
            public override string ToString() => $"head: tip r {TipRadius:0.#} mm, cone {HalfAngle:0.#} deg x {ConeLength:0} mm, body r {Radius:0} mm";
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
            public int Measured, TooHigh, TooThin, Deviating, CollisionMoves, SteepMoves, Ramp;
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
            for (int i = 0; i < n; i++) r.BeadHeights.Add(0);

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
                        if (!ClosestBelow(tp, below, grid, along, along[i], cell, p, 2 * w, 0.25 * w, 0.25 * h, 2 * w, out double zq)) notMeasured++;
                        else
                        {
                            double dz = p.Z - zq;
                            double used = tp.Heights[i];
                            // the start and end of a spiral run out to nothing on purpose, and the extrusion with them
                            bool ramp = (kv.Key == secondLayer || kv.Key == lastLayer) && used < 0.4 * h && Math.Abs(dz - used) <= 0.05 * h;
                            r.BeadHeights[i] = dz;
                            if (ramp) r.Ramp++;
                            else
                            {
                                r.Measured++;
                                if (dz < r.MinHeight) r.MinHeight = dz;
                                if (dz > r.MaxHeight) r.MaxHeight = dz;
                                bool bad = false;
                                if (dz > 0.8 * w) { r.TooHigh++; bad = true; }
                                if (dz < 0.4 * h) { r.TooThin++; bad = true; }
                                if (used > 1e-9)
                                {
                                    double dev = Math.Abs(dz - used) / used;
                                    sumDev += dev;
                                    if (dev > r.MaxDeviation) r.MaxDeviation = dev;
                                    if (dev > 0.10) r.Deviating++;
                                }
                                if (bad) r.BadHeights.Add(p);
                            }
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
            if (r.Measured > 0)
            {
                r.MeanDeviation = sumDev / r.Measured;
                double tooHigh = 100.0 * r.TooHigh / r.Measured, tooThin = 100.0 * r.TooThin / r.Measured, dev = 100.0 * r.Deviating / r.Measured;
                r.Report.Add($"Bead height (vertical gap to the layer below, {r.Measured} points): {r.MinHeight:0.##} to {r.MaxHeight:0.##} mm. " +
                             $"Allowed {0.4 * h:0.##} (0.4 x layer height) to {0.8 * w:0.##} mm (0.8 x bead width).");
                if (r.TooHigh > 0 || r.TooThin > 0)
                {
                    r.Ok = false;
                    r.Report.Add($"BEAD HEIGHT OUT OF RANGE: {r.TooHigh} points ({tooHigh:0.#} %) above 0.8 x bead width (the bead does not squash and bonds poorly), " +
                                 $"{r.TooThin} points ({tooThin:0.#} %) below 0.4 x layer height (pressed flat). See Bad Heights.");
                }
                else r.Report.Add("Bead height OK at every point.");
                if (r.MeanDeviation > 0.10 || r.Deviating > 0.05 * r.Measured)
                {
                    r.Ok = false;
                    r.Report.Add($"EXTRUSION DOES NOT FIT: at {dev:0.#} % of the points the bead height used for E differs by more than 10 % from the measured one (mean {100 * r.MeanDeviation:0.#} %, largest {100 * r.MaxDeviation:0} %).");
                }
                else r.Report.Add($"Extrusion fits the measured bead height: mean difference {100 * r.MeanDeviation:0.#} %, {dev:0.#} % of the points more than 10 % off (largest {100 * r.MaxDeviation:0} %).");
                if (r.Ramp > 0) r.Report.Add($"{r.Ramp} point(s) lie where the spiral starts or ends (second and last layer) and the bead runs out to nothing on purpose; the extrusion follows them. Not counted above.");
                if (notMeasured > 0) r.Report.Add($"{notMeasured} point(s) have no layer below within two bead widths (first bead of an overhang or a bridge): not measured. Path Check tests their support.");
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
                bool hit = false;
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
                            hit = true;
                            r.CollisionMoves++;
                            r.Collisions.Add(p);
                            long pair = ((long)tp.Layers[i] << 32) ^ (uint)tp.Layers[j];
                            if (hits.Count < 20 && shown.Add(pair))
                            {
                                hits.Add($"HEAD COLLISION at move {i}, layer {tp.Layers[i] + 1}, with layer {tp.Layers[j] + 1} (dz {dz:0.#} mm, r {rxy:0.#} mm, needs {need:0.#} mm)");
                            }
                            break;
                        }
                    }
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

            if (r.CollisionMoves == 0 && r.SteepMoves == 0)
                r.Report.Add($"Head clearance OK ({head}): the head never reaches what was printed before; the path climbs at most {Math.Atan(r.MaxSlope) * 180 / Math.PI:0} deg within a layer (allowed {90 - head.HalfAngle:0}).");
            else
            {
                r.Ok = false;
                if (r.CollisionMoves > 0)
                {
                    r.Report.Add($"HEAD COLLISION: at {r.CollisionMoves} move(s) the head ({head}) hits beads printed before. With a vertical nozzle a layer must not lie lower than the layers already printed next to it.");
                    r.Report.AddRange(hits);
                }
                if (r.SteepMoves > 0) r.Report.Add($"TOO STEEP: {r.SteepMoves} move(s) climb within a layer more steeply than the cone's flank allows (up to {Math.Atan(r.MaxSlope) * 180 / Math.PI:0} deg, allowed {90 - head.HalfAngle:0} deg). The cone would scrape the bead beside it.");
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
            double cell, Point3d p, double maxDist, double slack, double above, double gap, out double z)
        {
            z = 0;
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
            if (found) { z = zBest; return true; }
            if (dAny < double.MaxValue) { z = zAny; return true; }
            return false;
        }
    }
}
