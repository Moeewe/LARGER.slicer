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

        // ------------------------------------------------------------------ build

        public static Toolpath Build(List<SliceLayer> layers, PrintSettings settings, ContinuousMode mode,
            GeometryBase seam, double rampLength, bool alternate, List<string> log)
        {
            var tp = new Toolpath { Settings = settings };
            if (layers == null || layers.Count == 0) return tp;

            // non-planar / multi-axis: everything below happens in flat space
            Warp warp = layers[0].Warp;
            if (warp != null && seam != null) seam = warp.FlatObject(seam) as GeometryBase ?? seam;

            double minSpacing = settings.PointSpacing;
            double w = settings.BeadWidth;

            // clean contours once
            var loops = new List<List<Loop>>(layers.Count);
            // spiral can start at the first layer from which every layer is one closed wall
            var plain = new bool[layers.Count]; // exactly one closed wall: can be part of a spiral
            int infillLayers = 0;
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                var list = new List<Loop>();
                for (int ci = 0; ci < layer.Contours.Count; ci++)
                {
                    var pts = Clean(layer.Contours[ci], minSpacing, out bool closed);
                    if (pts.Count < 2) continue;
                    if (closed && !IsCounterClockwise(pts, layer.Plane)) pts.Reverse();
                    var lp = new Loop(pts, closed);
                    if (layer.ContourRoles != null && ci < layer.ContourRoles.Count) lp.Role = layer.ContourRoles[ci];
                    list.Add(lp);
                }
                // a layer with infill spliced into its wall (ribs, cells) stays flat: in a spiral the
                // infill would climb with the wall and no longer lie on the infill below
                bool hasInfill = layer.Reference != null && layer.Reference.Count > 0 && list.Count == 1
                                 && list[0].Len > 1.05 * layer.Reference.Max(r => r.Length);
                plain[li] = list.Count == 1 && list[0].Closed && list[0].Role == PathRole.Wall && !hasInfill;
                if (hasInfill) infillLayers++;
                loops.Add(list);
            }

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
        /// Closed contours change layer over a ramp: at the seam the path climbs one layer height
        /// along the ramp length while it keeps going round. The next layer starts its climb at
        /// the same place, so inside the ramp zone every pass lies exactly one layer height above
        /// the pass below - the layers are simply inclined there. Nothing is printed twice, the
        /// seam stays where it is, and the extrusion stays constant. Only the very first ramp
        /// (which starts on a flat layer) and a closing pass after the last one have a height
        /// that fades in or out. A longer ramp gives a softer seam.
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
                        var cur = new Loop(StartAt(pts, target), true);
                        // the ramp, the closing pass and the move to the next contour run along the plain
                        // wall, never inside a rib or support fin (the move would cut through its legs)
                        double maxRamp = double.MaxValue;
                        if (!usedHint && rampLength > 0) cur = OffExcursions(cur, layer, firstOfLayer, rampLength, tp.Settings.BeadSpacing, w, out maxRamp);

                        bool ramp = firstOfLayer && !usedHint && !hasFill && rampLength > 0 && tp.Count > 0 && i > 0;
                        double lr = ramp ? Math.Min(Math.Min(rampLength, maxRamp), cur.Len * 0.5) : 0;
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

        /// <summary>
        /// Keeps the seam off ribs and support fins spliced into a closed loop: the ramp, the closing
        /// pass over it and the move to the next contour must run along the plain wall, or the move
        /// would cut through the legs. The start slides by up to two bead widths to where the wall is
        /// plain for two bead widths behind it, and the ramp ends two bead widths before the next
        /// excursion (maxRamp). Only where there is no such place does the start move past the excursion.
        /// A later contour of the layer (no ramp, reached by a short move from the contour before)
        /// that would start just before an excursion starts two bead widths before it, so the turn
        /// from the move onto the wall and the turn into the excursion do not run into each other.
        /// </summary>
        private static Loop OffExcursions(Loop cur, SliceLayer layer, bool ramped, double ramp, double spacing, double w, out double maxRamp)
        {
            maxRamp = double.MaxValue;
            if (layer.Reference == null || layer.Reference.Count == 0 || cur.Len < 2 * ramp + 8 * w) return cur;
            var refs = new List<Loop>();
            foreach (var r in layer.Reference)
            {
                var pts = Clean(r, 1e-6, out bool closed);
                if (pts.Count >= 2) refs.Add(new Loop(pts, closed));
            }
            double limit = 0.55 * spacing + 0.5;
            Func<double, bool> away = s =>
            {
                Point3d p = cur.PointAt(s);
                foreach (var lp in refs) { int hint = -1; if (lp.Closest(p, ref hint, int.MaxValue).DistanceTo(p) <= limit) return false; }
                return true;
            };
            double start = 0, step = Math.Max(0.5, 0.25 * w);
            if (!ramped)
            {
                // only an excursion just ahead matters: start two bead widths before it leaves the wall
                // (further back the move from the contour before gets too long to print)
                double back = 2 * w;
                double found = double.NaN;
                for (double s = 0; s <= back; s += step)
                    if (away(s)) { found = s; break; }
                if (double.IsNaN(found) || away(0)) return cur;
                double leave = found;
                while (leave > 0 && away(leave - step)) leave -= step;
                return new Loop(StartAt(cur.P, cur.PointAt(leave - back)), true);
            }
            // The seam may slide by up to two bead widths (the move from the layer below stays short
            // enough to print). Pick the place that leaves the longest ramp before the next excursion.
            double gapBehind = 2 * w, gapAhead = 2 * w, window = 2 * w;
            double bestScore = double.MinValue, bestShift = double.NaN, bestRamp = 0;
            for (int k = 0; k <= (int)(2 * window / step); k++)
            {
                double d = ((k & 1) == 0 ? 1 : -1) * Math.Ceiling(k / 2.0) * step;
                bool blocked = false;
                for (double s = d - gapBehind; s <= d + gapAhead && !blocked; s += step) blocked = away(s);
                if (blocked) continue;
                double room = ramp;
                for (double s = d + gapAhead; s <= d + ramp + gapAhead; s += step)
                    if (away(s)) { room = s - d - gapAhead; break; }
                double score = room - 0.5 * Math.Abs(d);
                if (score > bestScore + 1e-9) { bestScore = score; bestShift = d; bestRamp = room; }
                if (k == 0 && room >= ramp) return cur; // nothing near the seam
            }
            if (!double.IsNaN(bestShift))
            {
                start = bestShift;
                if (bestRamp < ramp) maxRamp = Math.Max(0, bestRamp);
            }
            else
            {
                // no room next to the seam: go past the excursion
                double first = double.NaN;
                for (double s = -gapBehind; s <= gapAhead; s += step)
                    if (away(s)) { first = s; break; }
                if (double.IsNaN(first)) return cur;
                double end = first;
                while (end < first + cur.Len && away(end + step)) end += step;
                start = end + gapAhead;
            }
            return Math.Abs(start) < 1e-9 ? cur : new Loop(StartAt(cur.P, cur.PointAt(start)), true);
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

                // smallest cut distance whose arc reaches the radius
                double t = -1;
                double prevT = 0;
                const int steps = 32;
                var tried = new double[steps + 1];
                for (int k = 1; k <= steps; k++)
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
                if (t < 0)
                {
                    for (int k = 1; k <= steps; k++) bestR = Math.Max(bestR, tried[k]);
                    for (int k = 1; k <= steps; k++)
                        if (tried[k] >= 0.9 * bestR) { bestT = tMax * k / steps; break; }
                }
                if (t < 0)
                {
                    // radius not reachable: take the largest arc. For parallel lines that is a half
                    // circle, which needs a cut distance equal to its own radius.
                    t = bestT;
                    Point3d[] probe = Blend(tp, s, first - bestT, last + bestT, radius, out double turn);
                    if (turn > 2.5)
                    {
                        // U-turn between parallel lines: half circle whose top touches the turn
                        double half = 0.5 * probe[0].DistanceTo(probe[3]) / Math.Sin(turn * 0.5);
                        t = Math.Min(tMax, Math.Max(bestT, half));
                    }
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
                    r.Extrusion.Add(0);
                    r.Speed.Add(st.MaxSpeed);
                    r.Minutes += len / st.MaxSpeed;
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
