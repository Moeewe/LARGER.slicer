using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>Start and end moves of a print, and the printability check of a finished path.</summary>
    public static class LeadEngine
    {
        // ------------------------------------------------------------------ start and end

        /// <summary>
        /// Adds a skirt in front of the path and an exit move behind it.
        ///
        /// Skirt: loops around the first layer, a few bead widths away from the part. It is always
        /// printed first; the extruder gets up to pressure there instead of on the part. From the
        /// skirt the bead runs on into the outer wall without stopping.
        ///
        /// Exit: after the last point the nozzle moves sideways off the part and then lifts, so the
        /// drop that forms when the extruder stops does not end up on the part. Give a curve to
        /// set the exit path yourself. exitFlow is the flow factor on the exit (0 = no extrusion).
        /// </summary>
        public static Toolpath StartEnd(Toolpath tp, int skirtLoops, double skirtDistance,
            double exitLength, double exitLift, Curve exitCurve, double exitFlow, List<string> log)
        {
            var result = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            if (tp.Count < 2) return tp.Duplicate();
            var st = tp.Settings;
            double w = st.BeadWidth, spacing = st.BeadSpacing;

            // ---- skirt
            if (skirtLoops > 0)
            {
                int firstLayer = tp.Layers[0];
                Vector3d n = tp.Normals[0];
                var plane = Geo.PlaneAt(tp.Points[0], n);
                var pts2 = new List<Point2d>();
                for (int i = 0; i < tp.Count && tp.Layers[i] == firstLayer; i++)
                {
                    Vector3d v = tp.Points[i] - plane.Origin;
                    pts2.Add(new Point2d(v * plane.XAxis, v * plane.YAxis));
                }
                var hull = ConvexHull(pts2);
                if (hull.Count >= 3)
                {
                    if (skirtDistance <= 0) skirtDistance = 3 * w;
                    var path = new PathD(hull.Count);
                    foreach (var p in hull) path.Add(new PointD(p.X, p.Y));
                    Point2d start2 = pts2[0];
                    Point3d last = Point3d.Unset;
                    // outermost loop first, so the skirt ends on the loop nearest to the part
                    for (int k = skirtLoops - 1; k >= 0; k--)
                    {
                        PathsD ring = Clipper.InflatePaths(new PathsD { path }, skirtDistance + k * spacing, JoinType.Round, EndType.Polygon, 2.0, 3, 0.05);
                        if (ring == null || ring.Count == 0 || ring[0].Count < 3) continue;
                        var loop = ring[0].Select(p => new Point2d(p.x, p.y)).ToList();
                        // start next to the first point of the part
                        int nearest = 0; double best = double.MaxValue;
                        for (int i = 0; i < loop.Count; i++)
                        {
                            double d = loop[i].DistanceTo(start2);
                            if (d < best) { best = d; nearest = i; }
                        }
                        for (int i = 0; i <= loop.Count; i++)
                        {
                            Point2d q = loop[(nearest + i) % loop.Count];
                            Point3d p3 = plane.Origin + plane.XAxis * q.X + plane.YAxis * q.Y;
                            if (last.IsValid && p3.DistanceTo(last) < 1e-6) continue;
                            result.Add(p3, n, tp.Heights[0] > 0 ? tp.Heights[0] : st.LayerHeight, w, 1, PathRole.Skirt, firstLayer);
                            last = p3;
                        }
                    }
                }
                else log?.Add("Skirt skipped: the first layer is too small.");
            }

            // ---- the part itself. The skirt ends next to the first point of the part and the
            // nozzle keeps printing as it moves across: skirt and outer wall are one continuous
            // bead, the extruder never stops between them.
            int offset = result.Count;
            for (int i = 0; i < tp.Count; i++) result.AddFrom(tp, i);
            if (offset > 0)
            {
                result.Flows[offset] = 1; // the move that arrives at the part's first point prints
                result.Heights[offset] = tp.Heights[0] > 0 ? tp.Heights[0] : st.LayerHeight;
            }

            // ---- exit
            int lastIndex = tp.Count - 1;
            Point3d end = tp.Points[lastIndex];
            Vector3d axis = tp.Normals[lastIndex];
            int lastLayer = tp.Layers[lastIndex];
            double flow = Math.Max(0, exitFlow);
            PathRole exitRole = flow > 0 ? PathRole.Wall : PathRole.Travel;
            double h = st.LayerHeight;

            if (exitCurve != null)
            {
                Polyline pl;
                if (!exitCurve.TryGetPolyline(out pl))
                {
                    var pc = exitCurve.ToPolyline(0.1, 0.05, 0.5, 0);
                    pl = pc != null ? pc.ToPolyline() : null;
                }
                if (pl != null && pl.Count >= 2)
                {
                    // run the curve from the end that is nearer to the last print point
                    if (pl[pl.Count - 1].DistanceTo(end) < pl[0].DistanceTo(end)) pl.Reverse();
                    foreach (var p in pl)
                    {
                        if (p.DistanceTo(result.Points[result.Count - 1]) < 1e-6) continue;
                        result.Add(p, axis, h, w, flow, exitRole, lastLayer);
                    }
                }
                else log?.Add("Exit curve could not be used.");
            }
            else if (exitLength > 0 || exitLift > 0)
            {
                // away from the middle of the last layer = the shortest way off the part
                Point3d centre = Point3d.Origin; int cnt = 0;
                for (int i = lastIndex; i >= 0 && tp.Layers[i] == lastLayer; i--) { centre += tp.Points[i]; cnt++; }
                centre /= Math.Max(1, cnt);
                Vector3d dir = end - centre;
                dir -= axis * (dir * axis);
                if (!dir.Unitize())
                {
                    dir = tp.Points[lastIndex] - tp.Points[lastIndex - 1];
                    dir -= axis * (dir * axis);
                    if (!dir.Unitize()) dir = Vector3d.XAxis;
                }
                Point3d p = end;
                if (exitLength > 0) { p += dir * exitLength; result.Add(p, axis, h, w, flow, exitRole, lastLayer); }
                if (exitLift > 0) { p += axis * exitLift; result.Add(p, axis, h, w, 0, PathRole.Travel, lastLayer); }
            }
            return result;
        }

        private static List<Point2d> ConvexHull(List<Point2d> pts)
        {
            var p = pts.OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
            if (p.Count < 3) return p;
            Func<Point2d, Point2d, Point2d, double> cross = (o, a, b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var hull = new List<Point2d>();
            foreach (var q in p)
            {
                while (hull.Count >= 2 && cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            int lower = hull.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && cross(hull[hull.Count - 2], hull[hull.Count - 1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        // ------------------------------------------------------------------ check

        public class CheckResult
        {
            public List<Point3d> Unsupported = new List<Point3d>();
            public List<double> Offsets = new List<double>(); // per path point: sideways distance to the layer below
            public List<Point3d> Crossings = new List<Point3d>(); // where the path crosses itself within a layer
            public List<string> Report = new List<string>();
            public double MaxOffset;
            public bool Ok = true;
        }

        /// <summary>
        /// Checks whether the path can be printed.
        ///
        /// Support: a bead is only held if it lies on the bead below. The path is the middle of
        /// the bead and the bead reaches half its width to either side, so a path point may sit at
        /// most half a bead width beside the path of the layer below. Points further out hang in
        /// the air. The first layer lies on the bed and is not checked.
        /// </summary>
        public static CheckResult Check(Toolpath tp, double maxOffset)
        {
            var r = new CheckResult();
            if (tp.Warp != null)
            {
                // non-planar / multi-axis: test in flat space, where layers are planes again
                var flat = tp.Duplicate();
                flat.Warp = null;
                for (int i = 0; i < flat.Count; i++)
                {
                    Point3d f = tp.Warp.ToFlat(flat.Points[i]);
                    flat.Heights[i] = flat.Heights[i] / tp.Warp.Scale(f);
                    flat.Points[i] = f;
                    flat.Normals[i] = Vector3d.ZAxis;
                }
                tp = flat;
            }
            var st = tp.Settings;
            double limit = maxOffset > 0 ? maxOffset : 0.5 * st.BeadWidth;
            int n = tp.Count;
            for (int i = 0; i < n; i++) r.Offsets.Add(0);
            if (n < 2) { r.Report.Add("Empty path."); r.Ok = false; return r; }

            // point indices per layer, in print order
            var byLayer = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (tp.Roles[i] == PathRole.Skirt) continue;
                if (!byLayer.TryGetValue(tp.Layers[i], out var list)) byLayer[tp.Layers[i]] = list = new List<int>();
                list.Add(i);
            }

            List<Point2d[]> below = null;
            Plane plane = Plane.WorldXY;
            bool havePlane = false;
            int unsupported = 0, checkedPoints = 0;
            var hangLayers = new List<int>(); // layers on which a good part of the bead hangs in the air
            foreach (var kv in byLayer)
            {
                var idx = kv.Value;
                Vector3d axis = tp.Normals[idx[0]];
                if (!havePlane) { plane = Geo.PlaneAt(tp.Points[idx[0]], axis); havePlane = true; }

                // this layer as 2D segments (what the next layer will rest on)
                var segs = new List<Point2d[]>(idx.Count);
                for (int k = 1; k < idx.Count; k++)
                {
                    int a = idx[k - 1], b = idx[k];
                    if (b != a + 1 || tp.Flows[b] <= 0) continue;
                    segs.Add(new[] { Flat(plane, tp.Points[a]), Flat(plane, tp.Points[b]) });
                }

                int hangCount = 0;
                if (below != null && below.Count > 0)
                {
                    int hint = 0;
                    foreach (int i in idx)
                    {
                        if (tp.Flows[i] <= 0) continue;
                        Point2d p = Flat(plane, tp.Points[i]);
                        double d = Nearest(below, p, ref hint, 40);
                        if (d > limit) { int full = -1; d = Nearest(below, p, ref full, int.MaxValue); if (full >= 0) hint = full; }
                        r.Offsets[i] = d;
                        checkedPoints++;
                        if (d > r.MaxOffset) r.MaxOffset = d;
                        if (d > limit) unsupported++;
                        // shown as unsupported: clearly off the bead below (more than 1.5 x the limit)
                        if (d > 1.5 * limit) { r.Unsupported.Add(tp.Points[i]); hangCount++; }
                    }
                }
                if (hangCount > Math.Max(3, idx.Count / 50)) hangLayers.Add(kv.Key);
                if (segs.Count > 0) below = segs;
            }

            // ---- the path must never cross itself within a layer
            foreach (var kv in byLayer)
            {
                var idx = kv.Value;
                Vector3d axis = tp.Normals[idx[0]];
                double h = Math.Max(0.1, st.LayerHeight);
                var segA = new List<int>(); // index of the end point of every printing move in this layer
                for (int k = 1; k < idx.Count; k++)
                    if (idx[k] == idx[k - 1] + 1 && tp.Flows[idx[k]] > 0) segA.Add(idx[k]);
                if (segA.Count < 3) continue;
                double cell = Math.Max(2 * st.BeadWidth, 5);
                var grid = new Dictionary<long, List<int>>();
                foreach (int e in segA)
                {
                    Point2d p = Flat(plane, tp.Points[e - 1]), q = Flat(plane, tp.Points[e]);
                    int x0 = (int)Math.Floor(Math.Min(p.X, q.X) / cell), x1 = (int)Math.Floor(Math.Max(p.X, q.X) / cell);
                    int y0 = (int)Math.Floor(Math.Min(p.Y, q.Y) / cell), y1 = (int)Math.Floor(Math.Max(p.Y, q.Y) / cell);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 400) { x1 = x0; y1 = y0; } // very long move: register once
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long key = ((long)gx << 32) ^ (uint)gy;
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                            foreach (int o in list)
                            {
                                if (Math.Abs(o - e) <= 2) continue; // neighbours share a point
                                Point2d a = Flat(plane, tp.Points[o - 1]), b = Flat(plane, tp.Points[o]);
                                double d1 = Cross(a, b, p), d2 = Cross(a, b, q), d3 = Cross(p, q, a), d4 = Cross(p, q, b);
                                if (d1 * d2 >= 0 || d3 * d4 >= 0) continue;
                                // Two moves that run almost along each other do not cross; that is the path
                                // turning back over itself at a layer change, one above the other.
                                Vector2d u1 = q - p, u2 = b - a;
                                if (Math.Abs(u1.X * u2.Y - u1.Y * u2.X) < 0.17 * u1.Length * u2.Length) continue; // under ~10 degrees
                                // same height? (the last flat loop of a spiral lies above the loop below it)
                                double zHere = (tp.Points[e] - plane.Origin) * axis, zThere = (tp.Points[o] - plane.Origin) * axis;
                                if (Math.Abs(zHere - zThere) > 0.25 * h) continue;
                                Point3d hit = tp.Points[e - 1] + (tp.Points[e] - tp.Points[e - 1]) * (d3 / (d3 - d4));
                                if (r.Crossings.Count == 0 || r.Crossings[r.Crossings.Count - 1].DistanceTo(hit) > 1e-6) r.Crossings.Add(hit);
                            }
                            list.Add(e);
                        }
                }
            }
            if (r.Crossings.Count == 0) r.Report.Add("No crossings: the path never runs across itself within a layer.");
            else
            {
                r.Ok = false;
                r.Report.Add($"CROSSES ITSELF: the path runs across its own bead at {r.Crossings.Count} place(s). The nozzle would plough through material it has just laid. See the Crossings output.");
            }

            // ---- report
            if (unsupported == 0)
                r.Report.Add($"Support OK: every bead lies on the layer below (largest sideways offset {r.MaxOffset:0.0} mm, allowed {limit:0.0} mm = half the bead width).");
            else if (r.MaxOffset <= 1.75 * limit && unsupported <= 0.01 * Math.Max(1, checkedPoints))
            {
                // a handful of points just over the limit: the bead still rests on most of its width
                r.Report.Add($"Support OK with a note: {unsupported} of {checkedPoints} points lie slightly more than {limit:0.0} mm beside the layer below (up to {r.MaxOffset:0.0} mm). " +
                             "They still rest on part of the bead below - typically where ribs start on the bottom layer or two rib tips meet. Use the Offset output to see where.");
            }
            else
            {
                r.Ok = false;
                r.Report.Add($"NOT SUPPORTED: {unsupported} of {checkedPoints} points lie more than {limit:0.0} mm beside the layer below (largest offset {r.MaxOffset:0.0} mm). " +
                             "These beads hang in the air. Use a wider bead, a lower layer height, a tilted slicing plane or more ribs.");
                if (hangLayers.Count > 0 && st.LayerHeight > 0)
                {
                    // where, and what it means as an overhang angle
                    var ranges = new List<string>();
                    int a0 = hangLayers[0], b0 = a0;
                    for (int k = 1; k <= hangLayers.Count; k++)
                    {
                        if (k < hangLayers.Count && hangLayers[k] <= b0 + 2) { b0 = hangLayers[k]; continue; }
                        ranges.Add(a0 == b0 ? (a0 + 1).ToString() : (a0 + 1) + "-" + (b0 + 1));
                        if (k < hangLayers.Count) { a0 = hangLayers[k]; b0 = a0; }
                    }
                    double allowed = Math.Atan(limit / st.LayerHeight) * 180.0 / Math.PI;
                    double found = Math.Atan(r.MaxOffset / st.LayerHeight) * 180.0 / Math.PI;
                    r.Report.Add($"Overhang: the wall leans up to {found:0} degrees from vertical. With a {st.BeadWidth:0.#} mm bead and {st.LayerHeight:0.#} mm layers about {allowed:0} degrees is the limit. " +
                                 $"Affected layers: {string.Join(", ", ranges.Take(8))}{(ranges.Count > 8 ? " ..." : "")}. " +
                                 "Domes, spheres and flat tops close faster than that: slice them with a Guide surface (non-planar) or a tilted plane, or cut the part where it gets too flat.");
                }
            }

            double ratio = st.LayerHeight / st.BeadWidth;
            if (ratio > 0.8)
            {
                r.Ok = false;
                r.Report.Add($"Bead shape: layer height is {ratio:P0} of the bead width. Above about 80 % the bead is hardly squashed and bonds poorly.");
            }
            else if (ratio < 0.15)
                r.Report.Add($"Bead shape: layer height is only {ratio:P0} of the bead width. Very flat beads need a lot of pressure; check that the extruder can deliver this.");
            else
                r.Report.Add($"Bead shape OK: layer height is {ratio:P0} of the bead width.");

            int travels = 0, tiny = 0; double travelLength = 0;
            for (int i = 1; i < n; i++)
            {
                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                if (tp.Flows[i] <= 0) { travelLength += len; if (tp.Flows[i - 1] > 0 || i == 1) travels++; }
                else if (len < 0.05) tiny++;
            }
            r.Report.Add(travels == 0 ? "Continuous: no travel moves." : $"Travel: {travels} move(s) without extrusion, {travelLength:0} mm in total.");
            if (tiny > n / 20) r.Report.Add($"{tiny} moves are shorter than 0.05 mm. Raise Resolution in the Print Settings to thin out the path.");
            if (st.Speed <= 0 || st.MinSpeed <= 0)
            {
                r.Ok = false;
                r.Report.Add("Speed is 0. A move with F0 never finishes, the machine would stop at that line.");
            }
            return r;
        }

        private static double Cross(Point2d a, Point2d b, Point2d c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static Point2d Flat(Plane plane, Point3d p)
        {
            Vector3d v = p - plane.Origin;
            return new Point2d(v * plane.XAxis, v * plane.YAxis);
        }

        private static double Nearest(List<Point2d[]> segs, Point2d p, ref int hint, int window)
        {
            int from = 0, to = segs.Count;
            if (hint >= 0 && window < segs.Count) { from = Math.Max(0, hint - window); to = Math.Min(segs.Count, hint + window); }
            double best = double.MaxValue; int bi = hint;
            for (int i = from; i < to; i++)
            {
                Point2d a = segs[i][0], b = segs[i][1];
                Vector2d ab = b - a;
                double l2 = ab.SquareLength;
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                double d = (a + ab * t).DistanceTo(p);
                if (d < best) { best = d; bi = i; }
            }
            hint = bi;
            return best;
        }
    }
}
