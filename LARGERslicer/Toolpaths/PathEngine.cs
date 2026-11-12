using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace LARGERslicer.Toolpaths
{
    public enum ContinuousMode { Spiral = 0, Layered = 1 }

    /// <summary>
    /// Builds one continuous print path from layers.
    ///  - Spiral: vase mode. The path climbs steadily and blends from one layer contour to the
    ///    next, so there is no seam at all. A flat first loop and a flat last loop close the
    ///    bottom and the top; their layer height fades, so the extrusion fades with it.
    ///  - Layered: flat layers that change layer over a ramp at a fixed seam (see BuildLayered).
    ///    Open contours are printed back and forth (zigzag).
    /// </summary>
    public static class PathEngine
    {
        // ------------------------------------------------------------------ contour helpers

        private class Loop
        {
            public List<Point3d> P;   // closed: closing point is not repeated
            public bool Closed;
            public double[] S;        // arc length at each vertex
            public double Len;        // total length (closed: including closing segment)
            public PathRole Role = PathRole.Wall;

            public Loop(List<Point3d> pts, bool closed)
            {
                P = pts; Closed = closed;
                S = new double[pts.Count];
                for (int i = 1; i < pts.Count; i++) S[i] = S[i - 1] + pts[i].DistanceTo(pts[i - 1]);
                Len = S[pts.Count - 1] + (closed ? pts[pts.Count - 1].DistanceTo(pts[0]) : 0);
            }

            public Point3d PointAt(double s)
            {
                if (Closed) { s %= Len; if (s < 0) s += Len; }
                else s = Math.Max(0, Math.Min(Len, s));
                int lo = Array.BinarySearch(S, s);
                if (lo >= 0) return P[lo];
                lo = ~lo - 1;
                if (lo < 0) return P[0];
                Point3d a = P[lo];
                Point3d b = lo + 1 < P.Count ? P[lo + 1] : P[0];
                double end = lo + 1 < P.Count ? S[lo + 1] : Len;
                double seg = end - S[lo];
                return seg < 1e-12 ? a : a + (b - a) * ((s - S[lo]) / seg);
            }

            /// <summary>Closest point on the loop; hint = segment index to search around (-1 = everywhere).</summary>
            public Point3d Closest(Point3d p, ref int hint, int window)
            {
                int segs = Closed ? P.Count : P.Count - 1;
                int from = 0, to = segs;
                if (hint >= 0 && window < segs) { from = hint - 2; to = hint + window; }
                double best = double.MaxValue; Point3d bp = P[0]; int bi = 0;
                for (int k = from; k < to; k++)
                {
                    int i = ((k % segs) + segs) % segs;
                    Point3d a = P[i], b = P[(i + 1) % P.Count];
                    Vector3d ab = b - a;
                    double l2 = ab.SquareLength;
                    double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                    Point3d c = a + ab * t;
                    double d = c.DistanceToSquared(p);
                    if (d < best) { best = d; bp = c; bi = i; }
                }
                hint = bi;
                return bp;
            }
        }

        /// <summary>Removes points closer than minSpacing and reports whether the contour is closed.</summary>
        private static List<Point3d> Clean(Polyline pl, double minSpacing, out bool closed)
        {
            closed = Geo.Closed(pl);
            var pts = new List<Point3d>(pl.Count);
            int last = closed ? pl.Count - 1 : pl.Count;
            for (int i = 0; i < last; i++)
            {
                if (pts.Count == 0 || pl[i].DistanceTo(pts[pts.Count - 1]) >= minSpacing) pts.Add(pl[i]);
                else if (!closed && i == last - 1) pts[pts.Count - 1] = pl[i]; // keep the true end of open contours
            }
            if (closed && pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) < minSpacing) pts.RemoveAt(pts.Count - 1);
            if (closed && pts.Count < 3) closed = false;
            return pts;
        }

        private static bool IsCounterClockwise(List<Point3d> pts, Plane plane)
        {
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3d a = pts[i] - plane.Origin, b = pts[(i + 1) % pts.Count] - plane.Origin;
                area += (a * plane.XAxis) * (b * plane.YAxis) - (b * plane.XAxis) * (a * plane.YAxis);
            }
            return area > 0;
        }

        /// <summary>Rotates a closed contour so that it starts at the point closest to target.</summary>
        private static List<Point3d> StartAt(List<Point3d> pts, Point3d target)
        {
            var lp = new Loop(pts, true);
            int seg = -1;
            Point3d c = lp.Closest(target, ref seg, int.MaxValue);
            var r = new List<Point3d>(pts.Count + 1);
            int next = (seg + 1) % pts.Count;
            if (c.DistanceTo(pts[next]) < 1e-6) { seg = next; next = (seg + 1) % pts.Count; c = pts[seg]; }
            else if (c.DistanceTo(pts[seg]) >= 1e-6) r.Add(c);
            if (r.Count == 0)
            {
                for (int k = 0; k < pts.Count; k++) r.Add(pts[(seg + k) % pts.Count]);
                return r;
            }
            for (int k = 0; k < pts.Count; k++) r.Add(pts[(next + k) % pts.Count]);
            return r;
        }

        /// <summary>
        /// Points along a closed loop from arc length a to b (b may run past one lap), including
        /// every vertex and the given break positions. Returns point and arc length.
        /// </summary>
        private static List<KeyValuePair<Point3d, double>> Traverse(Loop lp, double a, double b, params double[] breaks)
        {
            var ss = new List<double> { a, b };
            foreach (double br in breaks) if (br > a && br < b) ss.Add(br);
            int laps = (int)Math.Ceiling(b / lp.Len) + 1;
            for (int lap = 0; lap < laps; lap++)
                for (int i = 0; i < lp.P.Count; i++)
                {
                    double s = lp.S[i] + lap * lp.Len;
                    if (s > a && s < b) ss.Add(s);
                }
            ss.Sort();
            var r = new List<KeyValuePair<Point3d, double>>(ss.Count);
            double prev = double.NaN;
            foreach (double s in ss)
            {
                if (!double.IsNaN(prev) && s - prev < 1e-6) continue;
                r.Add(new KeyValuePair<Point3d, double>(lp.PointAt(s), s));
                prev = s;
            }
            return r;
        }

        private static Point3d? SeamTarget(GeometryBase seam, SliceLayer layer, Point3d hint)
        {
            if (seam is Point pt) return pt.Location;
            if (seam is Curve crv)
            {
                var ix = Intersection.CurvePlane(crv, layer.Plane, 0.01);
                if (ix != null && ix.Count > 0) return ix[0].PointA;
                if (crv.ClosestPoint(hint, out double t)) return crv.PointAt(t);
            }
            return null;
        }

        // ------------------------------------------------------------------ bridges

        /// <summary>Arc length position of the point of the loop closest to p.</summary>
        private static double ArcAt(Loop lp, Point3d p)
        {
            int segs = lp.Closed ? lp.P.Count : lp.P.Count - 1;
            double best = double.MaxValue, at = 0;
            for (int i = 0; i < segs; i++)
            {
                Point3d a = lp.P[i], b = lp.P[(i + 1) % lp.P.Count];
                Vector3d ab = b - a; double l2 = ab.SquareLength;
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                double d = (a + ab * t).DistanceToSquared(p);
                if (d < best) { best = d; at = lp.S[i] + Math.Sqrt(l2) * t; }
            }
            return at;
        }

        private static Point2d To2(Plane pl, Point3d p) { Vector3d v = p - pl.Origin; return new Point2d(v * pl.XAxis, v * pl.YAxis); }

        private static bool Inside(List<Point2d> poly, Point2d q)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) && q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
            return c;
        }

        private static double Area2(List<Point2d> poly)
        {
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return Math.Abs(0.5 * a);
        }

        private static bool SegmentsCross(Point2d a, Point2d b, Point2d c, Point2d d)
        {
            Func<Point2d, Point2d, Point2d, double> cr = (o, p, q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
            double d1 = cr(a, b, c), d2 = cr(a, b, d), d3 = cr(c, d, a), d4 = cr(c, d, b);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        /// <summary>
        /// Continuous-toolpath connection rule (Wesseler, Herrlein, Kiendl: 45-degree inclined LFAM,
        /// boat hull case study): a closed contour that lies inside another one - the inner shell
        /// inside the outer shell, the wall of an opening inside the outer wall, a second wall
        /// inside the first - is joined to it by a bridge of two parallel lines one bead spacing
        /// apart. Both contours are cut open where the bridge meets them, and outer contour,
        /// bridge, inner contour (the other way round) and bridge become one closed line: no move
        /// without extrusion, no start and stop. The bridge is put where the bridge of the layer
        /// below was, so the bridges stack into a rib that stands on itself.
        /// Returns the joined loops; bridges = where the bridges of this layer are.
        /// </summary>
        private static List<Loop> Bridge(List<Loop> list, Plane plane, double spacing, List<Point3d> below, List<Point3d> bridges, ref int made, ref int notMade, double minGap)
        {
            var loops = list.Where(l => l.Closed).ToList();
            var open = list.Where(l => !l.Closed).ToList();
            if (loops.Count < 2) return list;
            var tried = new HashSet<Tuple<Loop, Loop>>();
            for (int guard = 0; guard < 64 && loops.Count > 1; guard++)
            {
                var polys = loops.Select(l => l.P.Select(q => To2(plane, q)).ToList()).ToList();
                var areas = polys.Select(Area2).ToList();
                // inner contour and the smallest contour around it; smallest first. Two walls one bead
                // apart are joined too: the bridge is then a short turn of one bead spacing.
                int child = -1, parent = -1;
                foreach (int c in Enumerable.Range(0, loops.Count).OrderBy(k => areas[k]))
                {
                    int best = -1;
                    for (int k = 0; k < loops.Count; k++)
                        if (k != c && areas[k] > areas[c] && Inside(polys[k], polys[c][0]) && (best < 0 || areas[k] < areas[best])) best = k;
                    if (best < 0 || tried.Contains(Tuple.Create(loops[best], loops[c]))) continue;
                    double gap = double.MaxValue;
                    foreach (var q in loops[c].P) { int h = -1; gap = Math.Min(gap, loops[best].Closest(q, ref h, int.MaxValue).DistanceTo(q)); }
                    tried.Add(Tuple.Create(loops[best], loops[c]));
                    if (gap < minGap) continue;
                    // a short turn between two walls only on contours long enough to keep it in one place
                    if (gap < 1.8 * spacing && loops[c].Len < 30 * spacing) continue;
                    child = c; parent = best; break;
                }
                if (child < 0) break; // nothing (more) to join: separate pieces stay separate
                Loop P = loops[parent], C = loops[child];

                // candidate places: every point of the inner loop with the nearest point of the outer one;
                // where a bridge stood on the layer below, that place comes first
                var cands = new List<Tuple<double, Point3d, Point3d>>();
                // right above a bridge of the layer below (exactly there, so the rib does not wander)
                if (below != null)
                    foreach (var b in below)
                    {
                        Point3d onPlane = plane.ClosestPoint(b);
                        int hc = -1; Point3d q = C.Closest(onPlane, ref hc, int.MaxValue);
                        double db = To2(plane, q).DistanceTo(To2(plane, onPlane));
                        if (db > 3 * spacing) continue;
                        int hp = -1; Point3d pp = P.Closest(q, ref hp, int.MaxValue);
                        cands.Add(Tuple.Create(-1e6 + db, pp, q));
                    }
                foreach (var q in C.P)
                {
                    int h = -1; Point3d pp = P.Closest(q, ref h, int.MaxValue);
                    cands.Add(Tuple.Create(pp.DistanceTo(q), pp, q));
                }
                bool joined = false;
                foreach (var cand in cands.OrderBy(t => t.Item1).Take(40))
                {
                    double sp = ArcAt(P, cand.Item2), sq = ArcAt(C, cand.Item3);
                    double hs = 0.5 * spacing;
                    if (P.Len < 4 * spacing || C.Len < 4 * spacing) break;
                    Point3d pa = P.PointAt(sp - hs), pb = P.PointAt(sp + hs);
                    Point3d c1 = C.PointAt(sq - hs), c2 = C.PointAt(sq + hs);
                    // the bridge line that leaves pa must end on the inner point on pa's side
                    Vector3d d = cand.Item3 - cand.Item2;
                    if (d.Length < 1e-6) d = c2 - c1;
                    Vector3d e = Vector3d.CrossProduct(plane.ZAxis, d);
                    bool c1WithPa = Math.Sign((c1 - cand.Item2) * e) == Math.Sign((pa - cand.Item2) * e);
                    Point3d qa = c1WithPa ? c1 : c2, qb = c1WithPa ? c2 : c1;
                    double sa = c1WithPa ? sq - hs : sq + hs;
                    int dir = c1WithPa ? -1 : 1; // walk the inner loop away from the bridge
                    // the two bridge lines must not cross any other contour
                    Point2d A1 = To2(plane, pa), A2 = To2(plane, qa), B1 = To2(plane, qb), B2 = To2(plane, pb);
                    // the lines start and end on the contours: test them a little shortened, or the
                    // contour they start on counts as crossed
                    Func<Point2d, Point2d, double, Point2d> lerp = (x, y, t) => new Point2d(x.X + (y.X - x.X) * t, x.Y + (y.Y - x.Y) * t);
                    { Point2d a1 = lerp(A1, A2, 0.02), a2 = lerp(A1, A2, 0.98), b1 = lerp(B1, B2, 0.02), b2 = lerp(B1, B2, 0.98); A1 = a1; A2 = a2; B1 = b1; B2 = b2; }
                    bool clear = !SegmentsCross(A1, A2, B1, B2);
                    for (int k = 0; k < loops.Count && clear; k++)
                    {
                        var poly = polys[k];
                        for (int i = 0; i < poly.Count && clear; i++)
                        {
                            Point2d u = poly[i], v = poly[(i + 1) % poly.Count];
                            if (SegmentsCross(A1, A2, u, v) || SegmentsCross(B1, B2, u, v)) clear = false;
                        }
                    }
                    foreach (var ol in open) for (int i = 0; i + 1 < ol.P.Count && clear; i++)
                        if (SegmentsCross(A1, A2, To2(plane, ol.P[i]), To2(plane, ol.P[i + 1])) || SegmentsCross(B1, B2, To2(plane, ol.P[i]), To2(plane, ol.P[i + 1]))) clear = false;
                    if (!clear) continue;

                    // outer loop from pb round to pa, bridge in, inner loop round, bridge back to pb
                    var pts = new List<Point3d>();
                    double step = Math.Max(0.5, spacing * 0.25);
                    Action<Loop, double, double, int> walk = (lp, from, length, sign) =>
                    {
                        // the vertices passed on the way, from 'from' over 'length' in direction sign
                        var inner = new List<Tuple<double, Point3d>>();
                        for (int i = 0; i < lp.P.Count; i++)
                        {
                            double rel = sign > 0 ? lp.S[i] - from : from - lp.S[i];
                            rel %= lp.Len; if (rel < 0) rel += lp.Len;
                            if (rel > 1e-6 && rel < length - 1e-6) inner.Add(Tuple.Create(rel, lp.P[i]));
                        }
                        foreach (var t in inner.OrderBy(t => t.Item1)) pts.Add(t.Item2);
                    };
                    pts.Add(pb);
                    walk(P, sp + hs, P.Len - spacing, 1);
                    pts.Add(pa);
                    pts.Add(qa);
                    walk(C, sa, C.Len - spacing, dir);
                    pts.Add(qb);
                    var merged = new Loop(pts, true) { Role = P.Role };
                    loops.RemoveAt(Math.Max(parent, child)); loops.RemoveAt(Math.Min(parent, child));
                    loops.Add(merged);
                    bridges.Add(cand.Item3); // where it meets the inner contour: the next layer's bridge stands on it
                    made++; joined = true;
                    break;
                }
                if (!joined) notMade++;
            }
            var result = new List<Loop>(loops);
            result.AddRange(open);
            return result;
        }

        // ------------------------------------------------------------------ build

        public static Toolpath Build(List<SliceLayer> layers, PrintSettings settings, ContinuousMode mode,
            GeometryBase seam, double rampLength, bool alternate, List<string> log, bool bridge = true)
        {
            var tp = new Toolpath { Settings = settings };
            if (layers == null || layers.Count == 0) return tp;

            // non-planar / multi-axis: everything below happens in flat space
            Warp warp = layers[0].Warp;
            if (warp != null && seam != null) seam = warp.FlatObject(seam) as GeometryBase ?? seam;

            double minSpacing = settings.PointSpacing;
            double w = settings.BeadWidth;
            // The seam gap: every layer ends this far from its own start, and the path crosses
            // the gap on a short incline up to the next layer. Nothing set = one bead width.
            if (rampLength <= 0) rampLength = w;

            // clean contours once
            var loops = new List<List<Loop>>(layers.Count);
            // spiral can start at the first layer from which every layer is one closed wall
            var plain = new bool[layers.Count]; // exactly one closed wall: can be part of a spiral
            int infillLayers = 0;
            int slivers = 0;
            int bridgesMade = 0, bridgesNotMade = 0; List<Point3d> bridgesBelow = null;
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                var list = new List<Loop>();
                for (int ci = 0; ci < layer.Contours.Count; ci++)
                {
                    var pts = Clean(layer.Contours[ci], minSpacing, out bool closed);
                    if (pts.Count < 2) continue;
                    // a closed contour that collapses to a line is a sliver, not a wall
                    if (!closed && Geo.Closed(layer.Contours[ci])) { slivers++; continue; }
                    if (closed && !IsCounterClockwise(pts, layer.Plane)) pts.Reverse();
                    var lp = new Loop(pts, closed);
                    // A closed loop shorter than three bead widths is smaller than the bead itself
                    // (the slivers where a tilted layer just touches the part): it cannot be printed.
                    if (closed && lp.Len < 3.0 * w) { slivers++; continue; }
                    if (layer.ContourRoles != null && ci < layer.ContourRoles.Count) lp.Role = layer.ContourRoles[ci];
                    list.Add(lp);
                }
                // a layer with infill spliced into its wall (ribs, cells) stays flat: in a spiral the
                // infill would climb with the wall and no longer lie on the infill below
                bool hasInfill = layer.Reference != null && layer.Reference.Count > 0 && list.Count == 1
                                 && list[0].Len > 1.05 * layer.Reference.Max(r => r.Length);
                // contours inside contours (inner shell, openings, more walls) are joined by a bridge
                bool fill = layer.EntryHints != null && layer.EntryHints.Count > 0;
                bool spliced = layer.Reference != null && layer.Reference.Count > 0;
                if (bridge && !fill && !spliced && list.Count(l => l.Closed) >= 2 && list.All(l => l.Role == PathRole.Wall))
                {
                    // Two walls one bead apart are joined by a short turn only where that makes the whole
                    // layer one line; otherwise the layer keeps several lines and their seams move from
                    // layer to layer, onto the turn. Contours further apart are always joined.
                    var here = new List<Point3d>(); int m1 = 0, n1 = 0;
                    var all = Bridge(list, layer.Plane, settings.BeadSpacing, bridgesBelow, here, ref m1, ref n1, 0.5 * settings.BeadSpacing);
                    if (all.Count(l => l.Closed) > 1)
                    {
                        here = new List<Point3d>(); m1 = 0; n1 = 0;
                        all = Bridge(list, layer.Plane, settings.BeadSpacing, bridgesBelow, here, ref m1, ref n1, 1.8 * settings.BeadSpacing);
                    }
                    list = all; bridgesMade += m1; bridgesNotMade += n1;
                    bridgesBelow = here;
                }
                else bridgesBelow = null;
                plain[li] = list.Count == 1 && list[0].Closed && list[0].Role == PathRole.Wall && !hasInfill;
                if (hasInfill) infillLayers++;
                loops.Add(list);
            }
            if (slivers > 0) log?.Add($"{slivers} contour(s) are smaller than one bead (slivers where a layer just touches the part) and were left out.");
            if (bridgesMade > 0) log?.Add($"{bridgesMade} bridge(s) join inner contours to the ones around them (two lines one bead spacing apart, stacked into a rib): no travel move between them.");
            if (bridgesNotMade > 0) log?.Add($"{bridgesNotMade} layer(s) have a contour inside another that could not be joined (no free way between them): a travel move remains there.");

            // Layers with fill (bottom, top) are printed flat. Every stretch of at least two plain
            // layers in between is printed as a spiral.
            Func<int, int> runEnd = start => { int j = start; while (j < layers.Count && plain[j]) j++; return j; };
            bool anySpiral = false;
            int at = 0;
            while (at < layers.Count)
            {
                if (mode == ContinuousMode.Spiral && plain[at] && runEnd(at) - at >= 2)
                {
                    int to = runEnd(at);
                    BuildSpiral(tp, layers, loops, seam, at, to);
                    anySpiral = true;
                    at = to;
                    continue;
                }
                int flatTo = at + 1;
                while (flatTo < layers.Count && !(mode == ContinuousMode.Spiral && plain[flatTo] && runEnd(flatTo) - flatTo >= 2)) flatTo++;
                BuildLayered(tp, layers, loops, seam, rampLength, alternate, w, at, flatTo);
                at = flatTo;
            }
            if (mode == ContinuousMode.Spiral && infillLayers > 0)
                log?.Add($"{infillLayers} layer(s) with ribs or cells are printed as flat layers; only plain single walls are printed as a spiral.");
            if (mode == ContinuousMode.Spiral && !anySpiral)
                log?.Add("Spiral needs layers with exactly one closed contour and no infill. Printed as flat layers instead.");
            TagInfill(tp, layers);
            return warp != null ? ToReal(tp, warp) : tp;
        }

        /// <summary>
        /// Maps a path built in flat space back onto the real layers. Long straight moves are
        /// split so they follow the bent layer, the tool axis becomes the layer normal and the
        /// layer height becomes the real local height, which the extrusion then follows.
        /// </summary>
        private static Toolpath ToReal(Toolpath flat, Warp warp)
        {
            var real = new Toolpath { Settings = flat.Settings, Warp = warp };
            double maxSegment = Math.Max(3.0, flat.Settings.PointSpacing * 2.0);
            for (int i = 0; i < flat.Count; i++)
            {
                if (i > 0)
                {
                    Point3d a = flat.Points[i - 1], b = flat.Points[i];
                    int parts = (int)Math.Ceiling(a.DistanceTo(b) / maxSegment);
                    for (int k = 1; k < parts; k++)
                    {
                        Point3d f = a + (b - a) * (k / (double)parts);
                        Point3d r = warp.ToReal(f, out Vector3d nk);
                        real.Add(r, nk, flat.Heights[i] * warp.Scale(f), flat.Widths[i], flat.Flows[i], flat.Roles[i], flat.Layers[i]);
                    }
                }
                Point3d p = warp.ToReal(flat.Points[i], out Vector3d n);
                if (real.Count > 0 && p.DistanceTo(real.Points[real.Count - 1]) < 1e-6 && flat.Flows[i] > 0) continue; // no doubled points
                real.Add(p, n, flat.Heights[i] * warp.Scale(flat.Points[i]), flat.Widths[i], flat.Flows[i], flat.Roles[i], flat.Layers[i]);
            }
            return real;
        }

        /// <summary>
        /// Marks path points that lie away from the plain walls as Infill (ribs spliced into the
        /// contour), so extrusion and speed can treat them separately later.
        /// </summary>
        private static void TagInfill(Toolpath tp, List<SliceLayer> layers)
        {
            var refs = new Dictionary<int, List<Loop>>();
            var heights = new Dictionary<int, double>();
            foreach (var l in layers)
            {
                if (l.Reference == null) continue;
                var list = new List<Loop>();
                foreach (var r in l.Reference)
                {
                    var pts = Clean(r, 1e-6, out bool closed);
                    if (pts.Count >= 2) list.Add(new Loop(pts, closed));
                }
                if (list.Count > 0) { refs[l.Index] = list; heights[l.Index] = l.Height; }
            }
            if (refs.Count == 0) return;

            int current = int.MinValue;
            List<Loop> loops = null;
            int[] hints = null;
            double limit = 0;
            for (int i = 0; i < tp.Count; i++)
            {
                if (tp.Roles[i] == PathRole.Travel || tp.Roles[i] == PathRole.Bottom) continue;
                if (tp.Layers[i] != current)
                {
                    current = tp.Layers[i];
                    if (refs.TryGetValue(current, out loops))
                    {
                        hints = Enumerable.Repeat(-1, loops.Count).ToArray();
                        double flat = 0.55 * tp.Settings.BeadSpacing;
                        limit = Math.Sqrt(flat * flat + heights[current] * heights[current]);
                    }
                    else loops = null;
                }
                if (loops == null) continue;

                Point3d p = tp.Points[i];
                double d = double.MaxValue;
                for (int k = 0; k < loops.Count; k++)
                    d = Math.Min(d, loops[k].Closest(p, ref hints[k], 24).DistanceTo(p));
                if (d > limit)
                {
                    // confirm with a full search before calling it infill
                    d = double.MaxValue;
                    for (int k = 0; k < loops.Count; k++)
                    {
                        int h = -1;
                        d = Math.Min(d, loops[k].Closest(p, ref h, int.MaxValue).DistanceTo(p));
                    }
                }
                if (d > limit) tp.Roles[i] = PathRole.Infill;
            }
        }

        private static void BuildSpiral(Toolpath tp, List<SliceLayer> layers, List<List<Loop>> loops, GeometryBase seam, int from, int to)
        {
            double w = tp.Settings.BeadWidth;
            Loop prev = null;
            Point3d prevStart = tp.Count > 0 ? tp.Points[tp.Count - 1] : Point3d.Unset;

            for (int i = from; i < to; i++)
            {
                var layer = layers[i];
                Vector3d n = layer.Plane.Normal;
                double h = layer.Height;
                var src = loops[i][0];

                Point3d target = prevStart.IsValid ? prevStart : src.P[0];
                Point3d? st = SeamTarget(seam, layer, target);
                if (st.HasValue) target = st.Value;
                var cur = new Loop(StartAt(src.P, target), true);

                if (i == from)
                {
                    // flat first loop (on the bed or on the bottom layers)
                    int startIndex = tp.Count;
                    foreach (var kv in Traverse(cur, 0, cur.Len))
                        tp.Add(kv.Key, n, h, w, 1, from == 0 ? PathRole.Bottom : PathRole.Wall, layer.Index);
                    if (startIndex > 0 && tp.Points[startIndex].DistanceTo(tp.Points[startIndex - 1]) > 3 * w)
                    {
                        tp.Flows[startIndex] = 0;
                        tp.Roles[startIndex] = PathRole.Travel;
                    }
                }
                else
                {
                    int hint = -1;
                    int window = Math.Max(12, prev.P.Count / 6);
                    foreach (var kv in Traverse(cur, 0, cur.Len))
                    {
                        double t = kv.Value / cur.Len;
                        Point3d p = kv.Key;
                        Point3d q = prev.Closest(p, ref hint, window);
                        // The layer below is one layer height down and at most half a bead to the side.
                        // Anything further away means the local search looked in the wrong place (ribs
                        // whose length changes from layer to layer), so search the whole loop.
                        if (q.DistanceTo(p) > Math.Sqrt(h * h + 0.25 * w * w) + 0.2) { hint = -1; q = prev.Closest(p, ref hint, int.MaxValue); }
                        // blend from the layer below to this layer: climbs and follows shape changes
                        // Blend sideways only where the layer below is really underneath (a wall that
                        // leans a little). Where the shape itself changes between layers - a rib that is
                        // longer on this layer - keep this layer's shape and only climb. Otherwise rib
                        // tips would end somewhere in between, different on every rib.
                        Vector3d side = p - q;
                        side -= n * (side * n);
                        Point3d o = side.Length > 0.5 * w ? p - n * (h * (1 - t)) : q + (p - q) * t;
                        // the first spiral loop lies on the flat loop, so its height grows from 0
                        double height = i == from + 1 ? h * t : h;
                        if (tp.Count > 0 && o.DistanceTo(tp.Points[tp.Count - 1]) < 1e-6) continue;
                        tp.Add(o, n, height, w, 1, PathRole.Wall, layer.Index);
                    }
                }

                if (i == to - 1 && i > from)
                {
                    // flat last loop: fills the wedge left by the spiral, height fades to 0
                    foreach (var kv in Traverse(cur, 0, cur.Len))
                    {
                        double t = kv.Value / cur.Len;
                        if (t <= 0) continue;
                        tp.Add(kv.Key, n, h * (1 - t), w, 1, PathRole.Wall, layer.Index);
                    }
                }

                prev = cur;
                prevStart = cur.P[0];
            }
        }

        /// <summary>
        /// Flat layers joined into one path.
        ///
        /// Closed contours have an open seam: the flat part of a layer ends one seam gap away
        /// from where it began (one bead width if nothing is set), so the end of a layer never
        /// runs into its own start. The path crosses that gap on a short incline, climbing one
        /// layer height, and is then on the next layer. Every layer does this at the same
        /// place, so inside the gap every pass lies exactly one layer height above the pass
        /// below. Nothing is printed twice, the seam stays where it is, and the extrusion
        /// stays constant. Only the very first incline (which starts on a flat layer) and a
        /// closing pass after the last one have a height that fades in or out. A longer gap
        /// gives a softer seam.
        ///
        /// Layers that carry a fill (bottom, top) change layer with a plain step next to the fill.
        /// Open contours are printed back and forth.
        /// </summary>
        private static void BuildLayered(Toolpath tp, List<SliceLayer> layers, List<List<Loop>> loops,
            GeometryBase seam, double rampLength, bool alternate, double w, int fromLayer, int toLayer)
        {
            bool first = tp.Count == 0;
            Point3d rampStart = Point3d.Unset; // where the layer below began (the next ramp starts above it)
            bool belowRamped = false;          // did the layer below climb over a ramp there?
            var prevStarts = new List<Point3d>(); var curStarts = new List<Point3d>(); // where the closed walls of a layer begin
            for (int i = fromLayer; i < toLayer; i++)
            {
                var layer = layers[i];
                Vector3d n = layer.Plane.Normal;
                double h = layer.Height;
                bool hasFill = layer.EntryHints != null && layer.EntryHints.Count > 0;
                // Several closed walls (a ring, a part with holes): the next layer starts on the
                // wall this layer ended on, so there is one travel move per layer instead of two.
                bool multi = !hasFill && loops[i].Count(l => l.Closed) >= 2;
                // will the next layer climb over the same ramp?
                bool nextRamps = rampLength > 0 && i + 1 < toLayer && loops[i + 1].Any(l => l.Closed)
                                 && !(layers[i + 1].EntryHints != null && layers[i + 1].EntryHints.Count > 0);

                var todo = new List<Loop>(loops[i]);
                bool firstOfLayer = true;
                prevStarts = curStarts; curStarts = new List<Point3d>();
                while (todo.Count > 0)
                {
                    Point3d from = tp.Count > 0 ? tp.Points[tp.Count - 1] : todo[0].P[0];
                    // The layer continues where the ramp of the layer below started (so the ramps
                    // stack); otherwise with the contour closest to the nozzle.
                    Point3d near = firstOfLayer && rampStart.IsValid && !hasFill && !multi ? rampStart + n * h : from;

                    int pick = 0; double best = double.MaxValue;
                    for (int k = 0; k < todo.Count; k++)
                    {
                        int hint = -1;
                        double d = todo[k].Closest(near, ref hint, int.MaxValue).DistanceToSquared(near);
                        if (firstOfLayer && rampStart.IsValid && !hasFill && !todo[k].Closed) d = double.MaxValue;
                        if (d < best) { best = d; pick = k; }
                    }
                    var src = todo[pick];
                    todo.RemoveAt(pick);

                    int startIndex = tp.Count;
                    if (src.Closed)
                    {
                        var pts = new List<Point3d>(src.P);
                        Point3d target = near;
                        bool usedHint = false;
                        Point3d? st = firstOfLayer ? SeamTarget(seam, layer, near) : null;
                        if (st.HasValue && !hasFill) target = st.Value;
                        else if (firstOfLayer && !hasFill && Math.Abs(n.Z) < 0.999)
                        {
                            // Tilted layers (angled printing): every layer begins where it stands on
                            // the bed, in the middle of its lowest edge. The first bead of a layer
                            // then lies on the bed and everything after it leans on the layer before.
                            double zLow = double.MaxValue;
                            foreach (var q in pts) if (q.Z < zLow) zLow = q.Z;
                            Point3d sum = Point3d.Origin; int cnt = 0;
                            foreach (var q in pts) if (q.Z <= zLow + 0.25 * h) { sum += q; cnt++; }
                            if (cnt > 0) target = sum / cnt;
                        }
                        else if (first && !hasFill)
                        {
                            // no seam given: start in the middle of the longest straight piece, so the
                            // seam and its ramp do not sit on a corner
                            double longest = -1;
                            for (int k = 0; k < pts.Count; k++)
                            {
                                Point3d a0 = pts[k], b0 = pts[(k + 1) % pts.Count];
                                double len = a0.DistanceTo(b0);
                                if (len > longest) { longest = len; target = a0 + (b0 - a0) * 0.5; }
                            }
                        }
                        else if (hasFill && src.Role == PathRole.Wall)
                        {
                            // start the wall next to the fill end that is nearest to the nozzle, so the
                            // path can turn straight from the wall into the fill
                            double bestHint = double.MaxValue;
                            foreach (var hp in layer.EntryHints)
                            {
                                double dh = first ? 0 : hp.DistanceToSquared(from);
                                if (dh < bestHint) { bestHint = dh; target = hp; usedHint = true; }
                                if (first) break;
                            }
                        }
                        // A wall the nozzle reaches by a travel move starts where it started on the
                        // layer below, not wherever the nozzle happens to come from: its seam stays
                        // in one place and the bead beside it lies on the one below.
                        if (!firstOfLayer && !usedHint && !hasFill && prevStarts.Count > 0)
                        {
                            double bestStart = w;
                            foreach (var ps in prevStarts)
                            {
                                Point3d up = ps + n * h; int hs = -1;
                                double ds = src.Closest(up, ref hs, int.MaxValue).DistanceTo(up);
                                if (ds < bestStart) { bestStart = ds; target = up; }
                            }
                        }
                        var cur = new Loop(StartAt(pts, target), true);
                        curStarts.Add(cur.P[0]);

                        bool ramp = firstOfLayer && !usedHint && !hasFill && rampLength > 0 && tp.Count > 0 && i > 0;
                        double lr = ramp ? Math.Min(rampLength, cur.Len * 0.5) : 0;
                        // does this ramp lie on the ramp of the layer below?
                        bool stacked = ramp && belowRamped && rampStart.IsValid && (rampStart + n * h).DistanceTo(cur.P[0]) < w;
                        double closeOver = 0;
                        if (usedHint)
                        {
                            // The wall meets the fill on a straight piece of wall, never on a corner:
                            // slide the meeting point along the wall until the wall is straight there.
                            double reach = 2 * w;
                            foreach (double shift in new[] { 0.0, reach, -reach, 2 * reach, -2 * reach, 3 * reach, -3 * reach })
                            {
                                Vector3d before = cur.PointAt(shift) - cur.PointAt(shift - w), after = cur.PointAt(shift + w) - cur.PointAt(shift);
                                if (!before.Unitize() || !after.Unitize() || before * after < 0.985) continue; // bend of more than ~10 degrees
                                Vector3d before2 = cur.PointAt(shift - w) - cur.PointAt(shift - 2 * w), after2 = cur.PointAt(shift + 2 * w) - cur.PointAt(shift + w);
                                if (!before2.Unitize() || !after2.Unitize() || before2 * after2 < 0.985) continue;
                                if (Math.Abs(shift) > 1e-9) cur = new Loop(StartAt(cur.P, cur.PointAt(shift)), true);
                                break;
                            }
                            // Run the wall one bead spacing past its start before turning into the fill.
                            // Rounding the turn takes about that much away again, so the wall ends up
                            // closed instead of showing a gap on the outside.
                            closeOver = Math.Min(tp.Settings.BeadSpacing, cur.Len * 0.25);
                        }
                        // after the last ramp the wedge it leaves at the top is closed with one more
                        // pass over the ramp zone, fading out
                        // (with several walls the next layer ramps on another wall, so close it here too)
                        bool closeWedge = ramp && (!nextRamps || multi);
                        double end = cur.Len + closeOver + (closeWedge ? lr : 0);

                        foreach (var kv in Traverse(cur, 0, end, lr, cur.Len))
                        {
                            double pos = kv.Value;
                            Point3d p = kv.Key;
                            double height = h;
                            PathRole role = src.Role;
                            if (ramp && pos < lr)
                            {
                                double u = pos / lr;
                                p -= n * (h * (1 - u));           // climb from the layer below
                                height = stacked ? h : h * u;     // on a flat layer the bead grows from nothing
                                role = PathRole.Seam;
                            }
                            else if (closeWedge && pos > cur.Len)
                            {
                                height = h * (1 - (pos - cur.Len) / lr);
                                role = PathRole.Seam;
                            }
                            tp.Add(p, n, height, w, 1, role, layer.Index);
                        }
                        if (firstOfLayer)
                        {
                            rampStart = nextRamps && !hasFill && !multi ? cur.P[0] : Point3d.Unset;
                            belowRamped = ramp;
                        }
                    }
                    else
                    {
                        // open contour: start at the end that is nearer -> zigzag between layers
                        var pts = new List<Point3d>(src.P);
                        if (!first && pts[pts.Count - 1].DistanceTo(from) < pts[0].DistanceTo(from)) pts.Reverse();
                        foreach (var p in pts) tp.Add(p, n, h, w, 1, src.Role, layer.Index);
                        if (firstOfLayer) { rampStart = Point3d.Unset; belowRamped = false; }
                    }

                    // the move that reaches this contour: print it if it is short, otherwise travel
                    if (!first && startIndex < tp.Count)
                    {
                        double link = tp.Points[startIndex].DistanceTo(tp.Points[startIndex - 1]);
                        if (link > 3 * w)
                        {
                            tp.Flows[startIndex] = 0;
                            tp.Roles[startIndex] = PathRole.Travel;
                        }
                        else if (!src.Closed)
                        {
                            tp.Heights[startIndex] = h;
                        }
                    }
                    first = false;
                    firstOfLayer = false;
                }
            }
        }

        // ------------------------------------------------------------------ rounding

        /// <summary>
        /// Rounds every corner of the path with a smooth arc.
        ///
        /// A corner is any kink of more than 15 degrees, or a faceted bend tighter than the
        /// radius. Corners that sit close together are treated as one (a zigzag turn or the tip of
        /// a rib is two corners one bead apart). For each corner the path is cut the same distance
        /// before and after it and the piece in between is replaced by an arc that leaves and
        /// rejoins the path tangentially. The cut distance grows until the arc reaches the radius.
        /// Where the path geometry cannot give that radius - two parallel lines one bead apart can
        /// only be joined by a half circle of half a bead - the largest possible arc is used.
        ///
        /// ribRadius applies to corners of infill and bottom fill, where bead spacing rules.
        /// </summary>
        public static Toolpath Round(Toolpath tp, double minRadius, double ribRadius, out int corners, out int tooSharp)
        {
            corners = 0; tooSharp = 0;
            int n = tp.Count;
            if (n < 3 || minRadius <= 0) return tp.Duplicate();
            if (ribRadius <= 0) ribRadius = tp.Settings.BeadSpacing;

            var s = new double[n];
            for (int i = 1; i < n; i++) s[i] = s[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]);
            double total = s[n - 1];

            // 1) corners, merged into clusters: { first s, last s, radius, thin (1 = fill geometry) }
            var clusters = new List<double[]>();
            for (int i = 1; i < n - 1; i++)
            {
                Vector3d a = tp.Points[i] - tp.Points[i - 1], b = tp.Points[i + 1] - tp.Points[i];
                double la = a.Length, lb = b.Length;
                if (la < 1e-9 || lb < 1e-9) continue;
                double angle = Vector3d.VectorAngle(a, b);
                if (angle < 0.05) continue; // ~3 degrees
                // travel moves stay straight: nothing is printed there
                if (tp.Roles[i] == PathRole.Travel || tp.Roles[i + 1] == PathRole.Travel) continue;
                bool thin = IsFill(tp.Roles[i - 1]) || IsFill(tp.Roles[i]) || IsFill(tp.Roles[i + 1]);
                double radius = thin ? Math.Min(minRadius, ribRadius) : minRadius;
                bool sharp = angle > 0.26; // ~15 degrees
                if (!sharp && Math.Min(la, lb) / (2 * Math.Tan(angle * 0.5)) >= radius) continue;

                // corners that belong together: the two corners of a zigzag turn or rib tip lie one
                // bead spacing apart, wall corners count as one within two radii
                double join = thin ? 1.5 * tp.Settings.BeadSpacing : 2 * radius;
                if (clusters.Count > 0 && s[i] - clusters[clusters.Count - 1][1] <= join)
                {
                    var c = clusters[clusters.Count - 1];
                    c[1] = s[i];
                    c[2] = Math.Min(c[2], radius);
                    if (thin) c[3] = 1;
                }
                else clusters.Add(new[] { s[i], s[i], radius, thin ? 1.0 : 0.0 });
            }
            if (clusters.Count == 0) return tp.Duplicate();
            corners = clusters.Count;

            // 2) rebuild the path, replacing each cluster by an arc
            var result = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            int idx = 0;
            for (int c = 0; c < clusters.Count; c++)
            {
                double first = clusters[c][0], last = clusters[c][1], radius = clusters[c][2];
                bool thinCluster = clusters[c][3] > 0;
                // never reach into the neighbouring corner's half of the straight in between
                double lo = c == 0 ? 0 : 0.5 * (clusters[c - 1][1] + first);
                double hi = c == clusters.Count - 1 ? total : 0.5 * (last + clusters[c + 1][0]);
                double tMax = Math.Min(Math.Min(first - lo, hi - last), 4 * radius);
                if (tMax < 1e-3) { tooSharp++; continue; }

                // A U-turn (the path comes back beside itself, one bead away) gets a half circle
                // whose top touches the turn. Its cut follows from the distance of the two lines
                // alone. No search: between two curved lines a long cut can reach the radius with
                // a balloon-shaped loop, on one layer and not on the next, and the turn then
                // sits centimetres beside the one below.
                double tProbe = Math.Min(tMax, 0.5 * tp.Settings.BeadSpacing);
                Point3d[] probe = Blend(tp, s, first - tProbe, last + tProbe, radius, out double turn);
                bool uTurn = turn > 2.5;

                // smallest cut distance whose arc reaches the radius
                double t = -1;
                double prevT = 0;
                const int steps = 32;
                var tried = new double[steps + 1];
                for (int k = 1; k <= steps && !uTurn; k++)
                {
                    double tk = tMax * k / steps;
                    double rk = BlendRadius(tp, s, first - tk, last + tk, radius);
                    tried[k] = rk;
                    if (rk >= radius * 0.97)
                    {
                        double a0 = prevT, b0 = tk;
                        for (int it = 0; it < 6; it++)
                        {
                            double mid = 0.5 * (a0 + b0);
                            if (BlendRadius(tp, s, first - mid, last + mid, radius) >= radius * 0.97) b0 = mid; else a0 = mid;
                        }
                        t = b0;
                        break;
                    }
                    prevT = tk;
                }
                // radius not reachable: the shortest cut that gets within 10 % of the best possible
                // arc. Cutting further back would only shorten the path (e.g. a rib) for nothing.
                double bestR = 0, bestT = tMax;
                if (uTurn)
                {
                    double half = 0.5 * probe[0].DistanceTo(probe[3]) / Math.Sin(turn * 0.5);
                    t = Math.Min(tMax, half);
                    if (!thinCluster) tooSharp++;
                }
                else if (t < 0)
                {
                    for (int k = 1; k <= steps; k++) bestR = Math.Max(bestR, tried[k]);
                    for (int k = 1; k <= steps; k++)
                        if (tried[k] >= 0.9 * bestR) { bestT = tMax * k / steps; break; }
                    // radius not reachable: the largest arc that fits
                    t = bestT;
                    if (!thinCluster) tooSharp++;
                }

                double sA = first - t, sB = last + t;
                while (idx < n && s[idx] < sA - 1e-9) { result.AddFrom(tp, idx); idx++; }

                Point3d[] curve = Blend(tp, s, sA, sB, radius, out double phi);
                int count = Math.Max(3, Math.Min(48, (int)Math.Ceiling(Math.Max(phi, 0.6) / 0.12)));
                for (int j = 0; j <= count; j++)
                {
                    double u = (double)j / count;
                    AddSampled(result, tp, s, sA + (sB - sA) * u, Bezier(curve, u));
                }
                while (idx < n && s[idx] <= sB + 1e-9) idx++;
            }
            while (idx < n) { result.AddFrom(tp, idx); idx++; }
            return result;
        }

        private static bool IsFill(PathRole r) => r == PathRole.Infill || r == PathRole.Bottom;

        private static int SegmentAt(double[] s, double at)
        {
            int i = Array.BinarySearch(s, at);
            if (i < 0) i = ~i;
            return Math.Max(1, Math.Min(s.Length - 1, i));
        }

        private static Point3d At(Toolpath tp, double[] s, double at)
        {
            int i = SegmentAt(s, at);
            double len = s[i] - s[i - 1];
            double t = len < 1e-12 ? 1 : Math.Max(0, Math.Min(1, (at - s[i - 1]) / len));
            return tp.Points[i - 1] + (tp.Points[i] - tp.Points[i - 1]) * t;
        }

        /// <summary>Travel direction just before (forward = false) or just after a position.</summary>
        private static Vector3d Tangent(Toolpath tp, double[] s, double at, double radius, bool forward)
        {
            double d = Math.Max(1e-3, radius * 0.05);
            double total = s[s.Length - 1];
            Point3d p0 = At(tp, s, forward ? at : Math.Max(0, at - d));
            Point3d p1 = At(tp, s, forward ? Math.Min(total, at + d) : at);
            Vector3d v = p1 - p0;
            if (!v.Unitize())
            {
                int i = SegmentAt(s, at);
                v = tp.Points[i] - tp.Points[i - 1];
                v.Unitize();
            }
            return v;
        }

        /// <summary>
        /// The curve that replaces the path between sA and sB: it leaves at sA and arrives at sB
        /// in the direction of the path. For a plain corner this is very close to a circular arc,
        /// for two parallel lines a half circle, and for a sideways step an S curve.
        /// </summary>
        private static Point3d[] Blend(Toolpath tp, double[] s, double sA, double sB, double radius, out double phi)
        {
            Point3d pa = At(tp, s, sA), pb = At(tp, s, sB);
            Vector3d da = Tangent(tp, s, sA, radius, false), db = Tangent(tp, s, sB, radius, true);
            phi = Vector3d.VectorAngle(da, db);
            double chord = pa.DistanceTo(pb);
            double k = phi > 1e-3 ? (4.0 / 3.0) * Math.Tan(phi * 0.25) * chord / (2 * Math.Sin(phi * 0.5)) : chord / 3.0;
            k = Math.Max(k, chord / 3.0);
            return new[] { pa, pa + da * k, pb - db * k, pb };
        }

        private static Point3d Bezier(Point3d[] c, double u)
        {
            double v = 1 - u;
            double b0 = v * v * v, b1 = 3 * v * v * u, b2 = 3 * v * u * u, b3 = u * u * u;
            return new Point3d(
                b0 * c[0].X + b1 * c[1].X + b2 * c[2].X + b3 * c[3].X,
                b0 * c[0].Y + b1 * c[1].Y + b2 * c[2].Y + b3 * c[3].Y,
                b0 * c[0].Z + b1 * c[1].Z + b2 * c[2].Z + b3 * c[3].Z);
        }

        /// <summary>Tightest radius of the curve that would replace the path between sA and sB.</summary>
        private static double BlendRadius(Toolpath tp, double[] s, double sA, double sB, double radius)
        {
            Point3d[] c = Blend(tp, s, sA, sB, radius, out _);
            const int samples = 20;
            double min = double.MaxValue;
            Point3d p0 = c[0], p1 = Bezier(c, 1.0 / samples);
            for (int j = 2; j <= samples; j++)
            {
                Point3d p2 = Bezier(c, (double)j / samples);
                double ab = p0.DistanceTo(p1), bc = p1.DistanceTo(p2), ca = p2.DistanceTo(p0);
                double area = Vector3d.CrossProduct(p1 - p0, p2 - p0).Length * 0.5;
                if (area > 1e-12) min = Math.Min(min, ab * bc * ca / (4 * area));
                p0 = p1; p1 = p2;
            }
            return min;
        }

        /// <summary>Adds a new point, taking height, width, tool axis, flow and role from the original path.</summary>
        private static void AddSampled(Toolpath result, Toolpath tp, double[] s, double at, Point3d p)
        {
            int i = SegmentAt(s, at);
            double len = s[i] - s[i - 1];
            double t = len < 1e-12 ? 1 : Math.Max(0, Math.Min(1, (at - s[i - 1]) / len));
            Vector3d nv = tp.Normals[i - 1] * (1 - t) + tp.Normals[i] * t;
            if (!nv.Unitize()) nv = tp.Normals[i];
            result.Add(p, nv,
                tp.Heights[i - 1] * (1 - t) + tp.Heights[i] * t,
                tp.Widths[i - 1] * (1 - t) + tp.Widths[i] * t,
                tp.Flows[i], tp.Roles[i], tp.Layers[i]);
        }

        // ------------------------------------------------------------------ extrusion and speed

        public class ProcessResult
        {
            public List<Plane> Frames = new List<Plane>();
            public List<double> Extrusion = new List<double>(); // per point: amount for the move that ends here
            public List<double> Speed = new List<double>();     // mm/min for the move that ends here
            public double Length, PrintLength, Volume, Minutes;
        }

        /// <summary>
        /// Extrusion per move from the local layer height and bead width.
        /// compensateWithSpeed = false: speed stays constant, the extrusion amount follows the height.
        /// compensateWithSpeed = true: the extruder keeps its nominal output and the speed changes
        /// instead (thin layer = faster), which suits slow-reacting pellet extruders.
        /// </summary>
        public static ProcessResult Process(Toolpath tp, bool compensateWithSpeed)
        {
            var r = new ProcessResult();
            var st = tp.Settings;
            var m = st.Machine ?? new MachineProfile();
            double dia = m.FilamentDiameter > 0 ? m.FilamentDiameter : st.FilamentDiameter;
            double filament = dia > 0 ? Math.PI * 0.25 * dia * dia : 1.0;
            int firstLayer = int.MaxValue;
            for (int i = 0; i < tp.Count; i++) if (tp.Flows[i] > 0 && tp.Layers[i] < firstLayer) firstLayer = tp.Layers[i];
            double nominalRate = PrintSettings.BeadArea(st.BeadWidth, st.LayerHeight) * st.Speed; // mm3/min

            for (int i = 0; i < tp.Count; i++)
            {
                // tool frame: Z = tool axis, X = travel direction
                Vector3d z = tp.Normals[i];
                Vector3d dir = i + 1 < tp.Count ? tp.Points[i + 1] - tp.Points[i] : (i > 0 ? tp.Points[i] - tp.Points[i - 1] : Vector3d.XAxis);
                Vector3d x = dir - z * (dir * z);
                if (!x.Unitize()) { x = Vector3d.CrossProduct(Vector3d.YAxis, z); if (!x.Unitize()) x = Vector3d.XAxis; }
                r.Frames.Add(Geo.PlaneFromAxes(tp.Points[i], x, Vector3d.CrossProduct(z, x)));

                if (i == 0) { r.Extrusion.Add(0); r.Speed.Add(st.Speed); continue; }

                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                double flow = tp.Flows[i] * st.Flow;
                r.Length += len;

                if (flow <= 0)
                {
                    // a move without extrusion runs fast, except where it goes down towards the bed or
                    // the part (the way in onto the start): there at print speed, so a wrong height
                    // is seen before the nozzle hits
                    double down = tp.Points[i - 1].Z - tp.Points[i].Z;
                    double travel = down > 0.5 * len ? st.Speed : st.MaxSpeed;
                    r.Extrusion.Add(0);
                    r.Speed.Add(travel);
                    r.Minutes += len / travel;
                    continue;
                }

                double height = 0.5 * (tp.Heights[i] + tp.Heights[i - 1]);
                double width = 0.5 * (tp.Widths[i] + tp.Widths[i - 1]);
                double area = PrintSettings.BeadArea(width, height) * flow;
                double volume = area * len;
                // what the machine is told: its own cross section model and calibration factors
                double e = m.Section(width, height) * flow * len * m.Factor;
                if (tp.Layers[i] == firstLayer) e *= m.FirstLayerFactor;

                double speed = st.Speed;
                if (compensateWithSpeed)
                    speed = area > 1e-9 ? Math.Max(st.MinSpeed, Math.Min(st.MaxSpeed, nominalRate / area)) : st.MaxSpeed;

                r.Extrusion.Add(e / filament);
                r.Speed.Add(speed);
                r.PrintLength += len;
                r.Volume += volume;
                r.Minutes += len / speed;
            }
            return r;
        }
    }
}
