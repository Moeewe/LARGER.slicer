using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clipper2Lib;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Everything the slicer adds inside a layer: walls and infill ribs. All distances come from
    /// the bead width (PrintSettings.BeadSpacing = width x (1 - overlap)), so neighbouring lines
    /// always fuse.
    /// </summary>
    public static partial class FillEngine
    {
        // ------------------------------------------------------------------ 2D helpers

        private static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d v = p - pl.Origin;
            return new Point2d(v * pl.XAxis, v * pl.YAxis);
        }

        private static Point3d To3d(Plane pl, Point2d p) => pl.Origin + pl.XAxis * p.X + pl.YAxis * p.Y;

        /// <summary>
        /// Where a place from another layer lies on this layer: straight above (or below) it.
        /// On flat layers that is the same as dropping it onto the plane. On tilted layers
        /// (Slice with an angle) it is not: the part stands upright, so its walls move sideways
        /// within the tilted plane from layer to layer. Whatever is carried from one layer to the
        /// next - the start of a pattern, a joint, a wave - has to move with the walls, or it
        /// drifts through the part and the beads lose the bead below.
        /// </summary>
        private static Point2d Above2d(Plane pl, Point3d p)
        {
            double nz = pl.ZAxis.Z;
            if (Math.Abs(nz) > 0.2 && Math.Abs(nz) < 1 - 1e-9)
            {
                double t = ((pl.Origin - p) * pl.ZAxis) / nz;
                p = new Point3d(p.X, p.Y, p.Z + t);
            }
            return To2d(pl, p);
        }

        /// <summary>Closed contour as 2D points without the repeated end point.</summary>
        private static List<Point2d> Ring2d(Plane pl, Polyline c)
        {
            var r = new List<Point2d>(c.Count);
            int n = Geo.Closed(c) ? c.Count - 1 : c.Count;
            for (int i = 0; i < n; i++) r.Add(To2d(pl, c[i]));
            return r;
        }

        private static Polyline Ring3d(Plane pl, List<Point2d> ring)
        {
            var p = new Polyline(ring.Count + 1);
            foreach (var q in ring) p.Add(To3d(pl, q));
            p.Add(p[0]);
            return p;
        }

        private static double Area(List<Point2d> r)
        {
            double a = 0;
            for (int i = 0; i < r.Count; i++)
            {
                Point2d p = r[i], q = r[(i + 1) % r.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        private static bool Inside(List<Point2d> r, Point2d p)
        {
            bool inside = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            {
                if ((r[i].Y > p.Y) != (r[j].Y > p.Y) &&
                    p.X < (r[j].X - r[i].X) * (p.Y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>True if p lies in material: inside an odd number of contours.</summary>
        private static bool InMaterial(List<List<Point2d>> rings, Point2d p)
        {
            int c = 0;
            foreach (var r in rings) if (Inside(r, p)) c++;
            return (c & 1) == 1;
        }

        // ------------------------------------------------------------------ walls

        /// <summary>
        /// Replaces the model contour by print paths: the first wall is moved inward by half a
        /// bead width so the outside of the bead lies on the model, every further wall sits one
        /// bead spacing further in. Holes are handled correctly (they grow instead of shrink).
        /// </summary>
        public static List<SliceLayer> Walls(List<SliceLayer> layers, PrintSettings settings, int count, bool insetHalfBead, List<string> log)
        {
            return Walls(layers, settings, count, insetHalfBead ? 0 : 1, log);
        }

        /// <summary>
        /// position: where the bead lies relative to the geometry.
        ///  0 = inside: the outer face of the print is the geometry (path half a bead width in)
        ///  1 = centred: the geometry is the middle of the bead (the print is half a bead wider)
        ///  2 = outside: the inner face of the print is the geometry (path half a bead width out)
        /// </summary>
        public static List<SliceLayer> Walls(List<SliceLayer> layers, PrintSettings settings, int count, int position, List<string> log)
            => Walls(layers, settings, count, position, log, default, null);

        /// <summary>
        /// As above; token stops the work early (Grasshopper 2 cancels a solution whose inputs changed:
        /// then null is returned), progress gets the share of layers done (0..1) now and then.
        /// </summary>
        public static List<SliceLayer> Walls(List<SliceLayer> layers, PrintSettings settings, int count, int position, List<string> log,
            System.Threading.CancellationToken token, Action<double> progress)
        {
            count = Math.Max(1, count);
            double w = settings.BeadWidth, spacing = settings.BeadSpacing;
            var result = new SliceLayer[layers.Count];
            int openSkipped = 0, vanished = 0, done = 0;
            int every = Math.Max(1, layers.Count / 50);
            // the token given here, or the one of the calculation this runs in (Cancel.Use)
            var options = new ParallelOptions { CancellationToken = token.CanBeCanceled ? token : Cancel.Token };

            try
            {
            Parallel.For(0, layers.Count, options, i =>
            {
                if (progress != null && System.Threading.Interlocked.Increment(ref done) % every == 0) progress((double)done / layers.Count);
                var src = layers[i];
                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp, Rings = new List<int>() };

                var closed = new List<List<Point2d>>();
                foreach (var c in src.Contours)
                {
                    if (Geo.Closed(c) && c.Count > 3) closed.Add(Ring2d(src.Plane, c));
                    else
                    {
                        // an open contour is a surface edge: the bead is centred on it
                        dst.Contours.Add(c); dst.Rings.Add(0);
                        if (count > 1) System.Threading.Interlocked.Increment(ref openSkipped);
                    }
                }

                if (closed.Count > 0)
                {
                    // outer contours counter-clockwise, holes clockwise (by nesting depth)
                    var paths = new PathsD(closed.Count);
                    for (int a = 0; a < closed.Count; a++)
                    {
                        int depth = 0;
                        for (int b = 0; b < closed.Count; b++)
                            if (a != b && Inside(closed[b], closed[a][0])) depth++;
                        bool shouldBeCcw = (depth & 1) == 0;
                        if ((Area(closed[a]) > 0) != shouldBeCcw) closed[a].Reverse();
                        var path = new PathD(closed[a].Count);
                        foreach (var p in closed[a]) path.Add(new PointD(p.X, p.Y));
                        paths.Add(path);
                    }

                    bool any = false;
                    for (int k = 0; k < count; k++)
                    {
                        double delta = -((position == 0 ? w * 0.5 : position == 2 ? -w * 0.5 : 0) + k * spacing);
                        PathsD ring = Math.Abs(delta) < 1e-9 ? paths : Clipper.InflatePaths(paths, delta, JoinType.Miter, EndType.Polygon, 2.0, 4);
                        if (ring == null || ring.Count == 0) break;
                        foreach (var path in ring)
                        {
                            if (path.Count < 3) continue;
                            var pl = new Polyline(path.Count + 1);
                            foreach (var p in path) pl.Add(To3d(src.Plane, new Point2d(p.x, p.y)));
                            pl.Add(pl[0]);
                            dst.Contours.Add(pl); dst.Rings.Add(k);
                            any = true;
                        }
                    }
                    if (!any) System.Threading.Interlocked.Increment(ref vanished);
                }
                result[i] = dst;
            });
            }
            catch (OperationCanceledException) when (token.CanBeCanceled) { return null; } // (an outer calculation's token: the exception goes on)

            if (openSkipped > 0) log?.Add("Open contours keep a single wall centred on the surface; extra walls are only added to closed contours.");
            if (vanished > 0) log?.Add(Inv.F($"{vanished} layer(s) are narrower than one bead width and have no wall."));
            return result.Where(l => l.Contours.Count > 0).ToList();
        }

        // ------------------------------------------------------------------ ribs

        private class Rib
        {
            public double S;            // position on the contour (arc length)
            public Point2d A, B;        // where the two legs leave the wall
            public List<Point2d> Path;  // from the first leg out to the tip and back along the second leg
        }

        /// <summary>
        /// Adds infill ribs to the innermost wall of every layer. A rib is a hairpin: the path
        /// leaves the wall, runs into the part and comes back right next to itself, one bead
        /// spacing apart, so both legs fuse into one solid rib. The tip stops one bead spacing
        /// short of the opposite wall so it fuses with it. Because the rib is part of the contour,
        /// the result is still one closed path per layer and works with Spiral and Layered.
        ///
        /// locations: points (rib at the nearest wall point in every layer), curves (rib where the
        /// curve crosses the layer) or planes (rib where the plane cuts the wall).
        /// autoSpacing > 0 additionally distributes ribs evenly along the wall.
        /// depth = 0: across to the opposite wall; otherwise the rib length in mm.
        /// </summary>
        public static List<SliceLayer> Ribs(List<SliceLayer> layers, PrintSettings settings, List<object> locations,
            double autoSpacing, double depth, bool meet, double stagger, List<string> log, out int ribCount)
        {
            double spacing = settings.BeadSpacing;
            double g = spacing * 0.5;
            // non-planar / multi-axis: what the user drew on the real model is mapped into flat space
            Warp warp = layers.Count > 0 ? layers[0].Warp : null;
            if (warp != null && locations != null) locations = locations.Select(o => warp.FlatObject(o)).ToList();
            var result = new List<SliceLayer>(layers.Count);
            var prevAnchors = new List<Point3d>();
            // Evenly distributed ribs are laid out once, on the first layer that gets ribs. Every
            // layer above takes over the rib feet of the layer below, so a rib always stands on
            // a rib and never starts in mid-air.
            List<Point3d> inherited = null;
            ribCount = 0;
            int skipped = 0;
            var why = new int[5]; // contour too short, too close to a rib on the same wall, no opposite wall, runs into another rib, too short

            foreach (var src in layers.OrderBy(l => l.Index))
            {
                Cancel.Check();
                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Rings = src.Rings != null ? new List<int>(src.Rings) : null;
                dst.Reference = src.Contours.Select(c => new Polyline(c)).ToList();
                dst.Contours.AddRange(src.Contours);
                dst.ContourRoles = src.ContourRoles;
                dst.EntryHints = src.EntryHints;

                // ribs go on the innermost closed wall(s)
                int inner = src.Rings != null && src.Rings.Count > 0 ? src.Rings.Max() : 0;
                var targets = new List<int>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (!Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    if (src.Rings != null && src.Rings[c] != inner) continue;
                    targets.Add(c);
                }
                if (targets.Count == 0) { result.Add(dst); continue; }

                var rings = targets.Select(c => Ring2d(src.Plane, src.Contours[c])).ToList();
                var cum = rings.Select(Cumulative).ToList();

                // 1) collect rib positions per target contour
                // { ring, position on the ring, 1 = placed by the user }. The order matters: where two
                // ribs would cross, the earlier one wins. Inherited ribs keep the order in which they
                // were built on the layer below, so the same rib wins on every layer.
                var candidates = new List<double[]>();
                var anchors = new List<Point3d>();
                for (int t = 0; t < rings.Count; t++)
                {
                    var ring = rings[t];
                    double len = cum[t][ring.Count];
                    // stable start for the even distribution: follow the anchor of the layer below
                    Point2d target = new Point2d(double.MaxValue, 0);
                    if (prevAnchors.Count > 0)
                    {
                        Point2d centre = Centroid(ring);
                        double best = double.MaxValue;
                        foreach (var pa in prevAnchors)
                        {
                            Point2d q = To2d(src.Plane, pa);
                            double d = q.DistanceTo(centre);
                            if (d < best) { best = d; target = q; }
                        }
                    }
                    else
                    {
                        Point2d centre = Centroid(ring);
                        target = new Point2d(centre.X + 1e6, centre.Y);
                    }
                    double anchorS = ClosestS(ring, cum[t], target, out _);
                    anchors.Add(To3d(src.Plane, PointAt(ring, cum[t], anchorS)));

                    if (autoSpacing > 0 && inherited == null)
                    {
                        // Even cells: every straight or smoothly curved side of the wall (from corner to
                        // corner) is divided into equal parts, so the cells at both ends of a side are
                        // as wide as the ones in between. Long sides go first; the ribs they create
                        // occupy the opposite wall, so that side adds none of its own.
                        // A corner is where the wall turns by more than 40 degrees within two bead
                        // spacings. Measuring over that distance ignores the small kinks of a faceted
                        // curve, which would otherwise chop a smooth side into pieces.
                        var corners = new List<int>();
                        double lastCorner = double.MinValue;
                        for (int k = 0; k < ring.Count; k++)
                        {
                            Vector2d vin = ring[k] - PointAt(ring, cum[t], cum[t][k] - spacing), vout = PointAt(ring, cum[t], cum[t][k] + spacing) - ring[k];
                            if (!vin.Unitize() || !vout.Unitize() || vin * vout > 0.766) continue;
                            if (cum[t][k] - lastCorner < 2 * spacing) continue;
                            corners.Add(k); lastCorner = cum[t][k];
                        }
                        if (corners.Count < 2)
                        {
                            int n = Math.Max(1, (int)Math.Round(len / autoSpacing));
                            for (int j = 0; j < n; j++) candidates.Add(new[] { t, (anchorS + j * len / n) % len, 0 });
                        }
                        else
                        {
                            var sides = new List<double[]>(); // { start, length }
                            for (int c = 0; c < corners.Count; c++)
                            {
                                double from = cum[t][corners[c]], to = cum[t][corners[(c + 1) % corners.Count]];
                                if (to <= from) to += len;
                                sides.Add(new[] { from, to - from });
                            }
                            foreach (var side in sides.OrderByDescending(x => x[1]))
                            {
                                int n = (int)Math.Round(side[1] / autoSpacing);
                                for (int j = 1; j < n; j++) candidates.Add(new[] { t, (side[0] + side[1] * j / n) % len, 0 });
                            }
                        }
                    }
                }
                prevAnchors = anchors;
                if (inherited == null && candidates.Count > 1)
                {
                    // short ribs first: where two would cross, the one spanning the narrow way stays
                    // (an ellipse gets ribs across its width, not one long rib down its length)
                    var spans = candidates.Select(c => Span(rings, cum, (int)c[0], c[1], spacing)).ToList();
                    candidates = candidates.Select((c, i) => new { c, i }).OrderBy(x => Math.Round(spans[x.i] / spacing)).ThenBy(x => x.i).Select(x => x.c).ToList();
                }
                if (inherited != null)
                {
                    foreach (Point3d foot in inherited)
                    {
                        Point2d p = To2d(src.Plane, foot);
                        int bestT = -1; double bestD = double.MaxValue, bestS = 0;
                        for (int t = 0; t < rings.Count; t++)
                        {
                            double s = ClosestS(rings[t], cum[t], p, out double d);
                            if (d < bestD) { bestD = d; bestT = t; bestS = s; }
                        }
                        // the wall may move by half a bead width per layer and still carry the rib
                        if (bestT >= 0 && bestD <= settings.BeadWidth) candidates.Add(new[] { bestT, bestS, 2 }); // 2 = taken over from the layer below
                    }
                }
                var feet = new List<Point3d>();

                foreach (Point2d p in LocationPoints(locations, src, rings))
                {
                    int bestT = -1; double bestD = double.MaxValue, bestS = 0;
                    for (int t = 0; t < rings.Count; t++)
                    {
                        double s = ClosestS(rings[t], cum[t], p, out double d);
                        if (d < bestD) { bestD = d; bestT = t; bestS = s; }
                    }
                    if (bestT >= 0) candidates.Add(new[] { bestT, bestS, 1 });
                }

                // 2) build ribs, checking each one against the walls and the ribs already placed
                var axes = new List<Point2d[]>();
                var ribsPer = rings.Select(_ => new List<Rib>()).ToList();
                // wall positions that already carry a rib foot or are hit by a rib tip. Evenly
                // distributed ribs keep half their spacing away from these, so ribs come from one
                // wall at even distances instead of from both walls at random.
                var occupied = rings.Select(_ => new List<double>()).ToList();
                var middles = new List<Point2d>();
                double keepClear = autoSpacing > 0 ? Math.Max(2.5 * spacing, 0.5 * autoSpacing) : 2.5 * spacing;
                GuideRibs(locations, src, rings, cum, spacing, meet, 0.5 + ((src.Index & 1) == 0 ? Math.Max(0, Math.Min(0.35, stagger)) : -Math.Max(0, Math.Min(0.35, stagger))), ribsPer, axes, occupied, ref skipped, why);
                stagger = Math.Max(0, Math.Min(0.35, stagger));
                // the joint of two meeting ribs sits on alternating sides of the middle, layer by layer
                double share = 0.5 + ((src.Index & 1) == 0 ? stagger : -stagger);
                {
                    foreach (double[] cand in candidates)
                    {
                        int t = (int)cand[0];
                        double s = cand[1];
                        bool byUser = cand[2] == 1, takenOver = cand[2] == 2;
                        var ring = rings[t];
                        double len = cum[t][ring.Count];
                        if (len < 6 * spacing) { skipped++; why[0]++; continue; }
                        if (TooClose(ribsPer[t], s, len, 2.5 * spacing)) { skipped++; why[1]++; continue; }
                        if (!byUser)
                        {
                            bool taken = false;
                            foreach (double o in occupied[t]) if (CircularDistance(o, s, len) < keepClear) { taken = true; break; }
                            if (taken) { skipped++; why[1]++; continue; }
                        }

                        Point2d a = PointAt(ring, cum[t], s - g), b = PointAt(ring, cum[t], s + g);
                        Point2d p0 = PointAt(ring, cum[t], s);
                        // a rib foot must not sit on a corner of the wall (its two legs would start on
                        // different sides): slide it along the wall until the wall is straight under it
                        // (only when a rib is first laid out; afterwards it stays where the layer below has it)
                        for (int attempt = 0; attempt < 3 && !takenOver; attempt++)
                        {
                            Vector2d v1 = p0 - a, v2 = b - p0;
                            if (!v1.Unitize() || !v2.Unitize() || v1 * v2 > 0.94) break; // within ~20 degrees
                            s += 3 * spacing;
                            a = PointAt(ring, cum[t], s - g); b = PointAt(ring, cum[t], s + g); p0 = PointAt(ring, cum[t], s);
                        }
                        Vector2d tan = b - a;
                        if (!tan.Unitize()) { skipped++; why[2]++; continue; }
                        var nrm = new Vector2d(-tan.Y, tan.X);
                        if (!InMaterial(rings, p0 + nrm * (spacing * 0.25))) nrm = -nrm;
                        if (!InMaterial(rings, p0 + nrm * (spacing * 0.25))) { skipped++; why[2]++; continue; }

                        // distance to the opposite wall
                        double hit = double.MaxValue;
                        int hitRing = -1, hitSeg = -1;
                        Point2d origin = p0 + nrm * 0.01;
                        for (int o = 0; o < rings.Count; o++)
                        {
                            var other = rings[o];
                            for (int k = 0; k < other.Count; k++)
                            {
                                double tt = RaySegment(origin, nrm, other[k], other[(k + 1) % other.Count]);
                                if (tt > spacing * 0.5 && tt < hit) { hit = tt; hitRing = o; hitSeg = k; }
                            }
                        }
                        if (hitRing < 0) { skipped++; why[2]++; continue; }
                        Point2d hp = origin + nrm * hit;
                        double hitS = cum[hitRing][hitSeg] + rings[hitRing][hitSeg].DistanceTo(hp);
                        double hitLen = cum[hitRing][rings[hitRing].Count];
                        {
                            // a rib must meet the opposite wall roughly square. A ray that only grazes a
                            // wall far away would become a long rib running along the part.
                            Vector2d wall = rings[hitRing][(hitSeg + 1) % rings[hitRing].Count] - rings[hitRing][hitSeg];
                            if (!wall.Unitize() || Math.Abs(nrm.X * wall.Y - nrm.Y * wall.X) < 0.7) { skipped++; why[2]++; continue; }
                        }

                        if (!byUser && !takenOver && autoSpacing > 0)
                        {
                            // no narrow leftover cells: a distributed rib keeps a quarter of the rib
                            // spacing away from walls that run alongside it (e.g. the end of a hull)
                            Point2d mid = p0 + nrm * (hit * 0.5);
                            // ... and from the middle of every rib that is already there, so cells do not
                            // bunch up where the two walls of a curved hull run at different lengths
                            bool crowded = false;
                            foreach (Point2d m in middles) if (m.DistanceTo(mid) < 0.6 * autoSpacing) { crowded = true; break; }
                            if (crowded) { skipped++; why[3]++; continue; }
                            double side = double.MaxValue;
                            foreach (var other in rings)
                                for (int k = 0; k < other.Count; k++)
                                    side = Math.Min(side, PointSegment(mid, other[k], other[(k + 1) % other.Count]));
                            if (side < Math.Min(0.45 * hit, 0.25 * autoSpacing)) { skipped++; why[3]++; continue; }
                        }

                        // ribs that are already there: stop so that this tip keeps one bead spacing of
                        // clearance to the other rib, also when the two meet at a slant
                        double blockedAt = double.MaxValue, stopAt = double.MaxValue;
                        foreach (var ax in axes)
                        {
                            double tt = RaySegment(origin, nrm, ax[0], ax[1]);
                            if (tt == double.MaxValue) continue;
                            Vector2d other = ax[1] - ax[0];
                            other.Unitize();
                            double sine = Math.Max(0.3, Math.Abs(nrm.X * other.Y - nrm.Y * other.X));
                            if (tt < blockedAt) blockedAt = tt;
                            stopAt = Math.Min(stopAt, tt - (spacing + g) / sine);
                        }

                        // --- two ribs, one from each wall, meeting inside the part
                        if (meet && blockedAt == double.MaxValue)
                        {
                            double len1 = share * hit - g, len2 = (1 - share) * hit - g;
                            bool fits = len1 >= 2 * spacing && len2 >= 2 * spacing
                                        && !TooClose(ribsPer[hitRing], hitS, hitLen, 2.5 * spacing)
                                        && !(hitRing == t && CircularDistance(s, hitS, len) < 2.5 * spacing);
                            if (fits)
                            {
                                bool near = false;
                                foreach (var ax in axes)
                                    if (SegmentDistance(p0, hp, ax[0], ax[1]) < 1.2 * spacing) { near = true; break; }
                                if (near) { skipped++; why[3]++; continue; }

                                var ringH = rings[hitRing];
                                Point2d a2 = PointAt(ringH, cum[hitRing], hitS - g), b2 = PointAt(ringH, cum[hitRing], hitS + g);
                                Vector2d across = b2 - a2;
                                across -= nrm * (across * nrm);
                                if (across.Unitize())
                                {
                                    const double lift = 0; // rounding puts the top of the U exactly on the tip
                                    Point2d tip2 = hp - nrm * (len2 + lift);
                                    if (!byUser) feet.Add(To3d(src.Plane, p0));
                                    ribsPer[t].Add(new Rib { S = s, A = a, B = b, Path = new List<Point2d> { a + nrm * (len1 + lift), b + nrm * (len1 + lift) } });
                                    ribsPer[hitRing].Add(new Rib { S = hitS, A = a2, B = b2, Path = new List<Point2d> { tip2 - across * g, tip2 + across * g } });
                                    occupied[t].Add(s); occupied[hitRing].Add(hitS);
                                    axes.Add(new[] { p0, p0 + nrm * len1 });
                                    axes.Add(new[] { hp, hp - nrm * len2 });
                                    middles.Add(p0 + nrm * (hit * 0.5));
                                    continue;
                                }
                            }
                        }

                        // a distributed rib never ends against another rib (that would be a T at an odd
                        // angle); only ribs the user placed may do that
                        if (!byUser && blockedAt < double.MaxValue) { skipped++; why[3]++; continue; }

                        // --- one rib across to the opposite wall (or to the given depth)
                        double length = hit - spacing;
                        if (depth > 0) length = Math.Min(length, depth);
                        if (blockedAt < double.MaxValue) length = Math.Min(length, stopAt);
                        bool clash = false;
                        if (length >= 2 * spacing)
                        {
                            Point2d tip = p0 + nrm * length;
                            foreach (var ax in axes)
                                if (SegmentDistance(p0, tip, ax[0], ax[1]) < 1.2 * spacing) { clash = true; break; }
                        }
                        if (clash || length < 2 * spacing) { skipped++; why[clash ? 3 : 4]++; continue; }

                        double reach = length;
                        if (!byUser) feet.Add(To3d(src.Plane, p0));
                        ribsPer[t].Add(new Rib { S = s, A = a, B = b, Path = new List<Point2d> { a + nrm * reach, b + nrm * reach } });
                        occupied[t].Add(s); if (depth <= 0 && blockedAt == double.MaxValue) occupied[hitRing].Add(hitS);
                        axes.Add(new[] { p0, p0 + nrm * length });
                        middles.Add(p0 + nrm * (hit * 0.5));
                    }
                }

                if (autoSpacing > 0) inherited = feet;

                // 3) splice the hairpins into the contours
                for (int t = 0; t < rings.Count; t++)
                {
                    var ribs = ribsPer[t].OrderBy(r => r.S).ToList();
                    if (ribs.Count == 0) continue;
                    var ring = rings[t];
                    double len = cum[t][ring.Count];
                    var pts = new List<Point2d>(ring.Count + ribs.Count * 4);
                    for (int r = 0; r < ribs.Count; r++)
                    {
                        Rib rib = ribs[r];
                        double from = rib.S + g;
                        double to = r + 1 < ribs.Count ? ribs[r + 1].S - g : ribs[0].S - g + len;
                        pts.AddRange(rib.Path);
                        pts.AddRange(Sub(ring, cum[t], from, to));
                    }
                    dst.Contours[targets[t]] = Ring3d(src.Plane, pts);
                    ribCount += ribs.Count;
                }
                result.Add(dst);
            }

            if (skipped > 0)
            {
                var parts = new List<string>();
                if (why[0] > 0) parts.Add(Inv.F($"{why[0]} on a contour too small for ribs"));
                if (why[1] > 0) parts.Add(Inv.F($"{why[1]} too close to a rib on the same wall"));
                if (why[2] > 0) parts.Add(Inv.F($"{why[2]} without an opposite wall to meet squarely"));
                if (why[3] > 0) parts.Add(Inv.F($"{why[3]} would run into or alongside another rib or wall"));
                if (why[4] > 0) parts.Add(Inv.F($"{why[4]} too short"));
                log?.Add(Inv.F($"{skipped} rib position(s) skipped: {string.Join(", ", parts)}."));
            }
            return result;
        }

        // ------------------------------------------------------------------ drawn ribs

        /// <summary>A curve that runs along the layer (drawn in top view) rather than through it.</summary>
        /// <summary>
        /// Sketched rib lines that cross each other in top view cannot both be printed: the path
        /// would have to run across itself. The line drawn later is left out and named in the log.
        /// </summary>
        public static List<object> WithoutCrossingSketches(List<object> sketch, List<string> log)
        {
            var flat = new List<Curve>();
            foreach (var o in sketch)
            {
                var c = (o as Curve)?.DuplicateCurve();
                if (c != null) c.Transform(Transform.PlanarProjection(Plane.WorldXY));
                flat.Add(c);
            }
            var keep = new List<object>(); var kept = new List<int>();
            for (int i = 0; i < sketch.Count; i++)
            {
                int hit = -1;
                if (flat[i] != null)
                    foreach (int k in kept)
                    {
                        if (flat[k] == null) continue;
                        var x = Rhino.Geometry.Intersect.Intersection.CurveCurve(flat[i], flat[k], 0.01, 0.01);
                        if (x != null && x.Count > 0) { hit = k; break; }
                    }
                if (hit >= 0) log?.Add(Inv.F($"Sketch line {i + 1} crosses line {hit + 1}. A printed line cannot run across itself, so line {i + 1} was left out."));
                else { keep.Add(sketch[i]); kept.Add(i); }
            }
            return keep;
        }

        private static bool IsGuide(Curve crv, SliceLayer layer)
        {
            BoundingBox bb = crv.GetBoundingBox(layer.Plane);
            double along = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y), through = bb.Max.Z - bb.Min.Z;
            return along > 1e-6 && through < 0.25 * along;
        }

        /// <summary>
        /// Ribs along curves the user has drawn. The curve is projected into the layer along the
        /// layer normal (so it also works for tilted and multi-axis layers). Every piece of it
        /// that lies inside the part and touches a wall becomes a rib that follows the curve:
        /// two legs half a bead spacing to either side of it. A piece from wall to wall becomes
        /// one rib across, or with Meet two ribs whose joint alternates from layer to layer.
        /// </summary>
        private static void GuideRibs(List<object> locations, SliceLayer layer, List<List<Point2d>> rings, List<double[]> cum,
            double spacing, bool meet, double share, List<List<Rib>> ribsPer, List<Point2d[]> axes, List<List<double>> occupied, ref int skipped, int[] why)
        {
            if (locations == null) return;
            double g = spacing * 0.5;
            foreach (object o in locations)
            {
                Curve crv = o as Curve;
                if (crv == null && o is Line) crv = new LineCurve((Line)o);
                if (crv == null || !IsGuide(crv, layer)) continue;

                var pc = crv.ToPolyline(0.1, 0.05, 1.0, 0);
                if (pc == null) continue;
                var poly = new List<Point2d>();
                for (int i = 0; i < pc.PointCount; i++)
                {
                    Point2d q = To2d(layer.Plane, pc.Point(i));
                    if (poly.Count == 0 || q.DistanceTo(poly[poly.Count - 1]) > 1e-6) poly.Add(q);
                }
                if (poly.Count < 2) continue;

                // where the curve crosses a wall: { position along the curve, ring, position on the ring }
                var cuts = new List<double[]>();
                for (int i = 0; i + 1 < poly.Count; i++)
                    for (int r = 0; r < rings.Count; r++)
                        for (int k = 0; k < rings[r].Count; k++)
                        {
                            Point2d c = rings[r][k], d = rings[r][(k + 1) % rings[r].Count];
                            Vector2d e1 = poly[i + 1] - poly[i], e2 = d - c;
                            double den = e1.X * e2.Y - e1.Y * e2.X;
                            if (Math.Abs(den) < 1e-12) continue;
                            Vector2d ac = c - poly[i];
                            double t = (ac.X * e2.Y - ac.Y * e2.X) / den, u = (ac.X * e1.Y - ac.Y * e1.X) / den;
                            if (t < 0 || t >= 1 || u < 0 || u >= 1) continue;
                            cuts.Add(new[] { i + t, r, cum[r][k] + u * e2.Length });
                        }
                cuts.Sort((x, y) => x[0].CompareTo(y[0]));

                // pieces between consecutive cuts (and the two free ends)
                var marks = new List<double[]> { new[] { 0.0, -1, 0 } };
                marks.AddRange(cuts);
                marks.Add(new[] { poly.Count - 1.0, -1, 0 });
                for (int m = 0; m + 1 < marks.Count; m++)
                {
                    double u0 = marks[m][0], u1 = marks[m + 1][0];
                    if (u1 - u0 < 1e-6) continue;
                    List<Point2d> piece = SubPolyline(poly, u0, u1);
                    double length = Length(piece);
                    if (length < 3 * spacing || !InMaterial(rings, PointAlong(piece, length * 0.5))) continue;
                    bool wall0 = marks[m][1] >= 0, wall1 = marks[m + 1][1] >= 0;
                    if (!wall0 && !wall1) { skipped++; why[2]++; continue; } // floats inside the part

                    if (wall0 && wall1 && meet)
                    {
                        AddGuideRib(piece, 0, share * length - g, (int)marks[m][1], marks[m][2], rings, cum, g, ribsPer, axes, occupied, ref skipped, why);
                        var back = new List<Point2d>(piece); back.Reverse();
                        AddGuideRib(back, 0, (1 - share) * length - g, (int)marks[m + 1][1], marks[m + 1][2], rings, cum, g, ribsPer, axes, occupied, ref skipped, why);
                    }
                    else if (wall0)
                        AddGuideRib(piece, 0, wall1 ? length - spacing : length, (int)marks[m][1], marks[m][2], rings, cum, g, ribsPer, axes, occupied, ref skipped, why);
                    else
                    {
                        var back = new List<Point2d>(piece); back.Reverse();
                        AddGuideRib(back, 0, length, (int)marks[m + 1][1], marks[m + 1][2], rings, cum, g, ribsPer, axes, occupied, ref skipped, why);
                    }
                }
            }
        }

        /// <summary>One rib that follows 'line' from its start (on the wall) for 'reach' millimetres.</summary>
        private static void AddGuideRib(List<Point2d> line, double from, double reach, int ring, double s, List<List<Point2d>> rings, List<double[]> cum,
            double g, List<List<Rib>> ribsPer, List<Point2d[]> axes, List<List<double>> occupied, ref int skipped, int[] why)
        {
            double len = cum[ring][rings[ring].Count];
            if (reach < 4 * g) { skipped++; why[4]++; return; }
            if (TooClose(ribsPer[ring], s, len, 5 * g)) { skipped++; why[1]++; return; }
            // centre line of the rib, with points close enough to follow a curved drawing
            var centre = new List<Point2d>();
            double step = Math.Max(2 * g, 2.0);
            int n = Math.Max(1, (int)Math.Ceiling((reach - from) / step));
            for (int i = 0; i <= n; i++) centre.Add(PointAlong(line, from + (reach - from) * i / n));

            var left = new List<Point2d>(); var right = new List<Point2d>();
            for (int i = 1; i < centre.Count; i++)
            {
                Vector2d dir = centre[Math.Min(i + 1, centre.Count - 1)] - centre[i - 1];
                if (!dir.Unitize()) continue;
                var nrm = new Vector2d(-dir.Y, dir.X);
                left.Add(centre[i] + nrm * g); right.Add(centre[i] - nrm * g);
            }
            if (left.Count == 0) { skipped++; why[4]++; return; }

            Point2d a = PointAt(rings[ring], cum[ring], s - g), b = PointAt(rings[ring], cum[ring], s + g);
            // the leg that starts next to 'a' goes out first
            var path = new List<Point2d>();
            bool leftFirst = left[0].DistanceTo(a) <= right[0].DistanceTo(a);
            var outLeg = leftFirst ? left : right; var backLeg = leftFirst ? right : left;
            path.AddRange(outLeg);
            for (int i = backLeg.Count - 1; i >= 0; i--) path.Add(backLeg[i]);
            ribsPer[ring].Add(new Rib { S = s, A = a, B = b, Path = path });
            occupied[ring].Add(s);
            for (int i = 1; i < centre.Count; i++) axes.Add(new[] { centre[i - 1], centre[i] });
        }

        private static double Length(List<Point2d> p)
        {
            double l = 0;
            for (int i = 1; i < p.Count; i++) l += p[i].DistanceTo(p[i - 1]);
            return l;
        }

        private static Point2d PointAlong(List<Point2d> p, double at)
        {
            double l = 0;
            for (int i = 1; i < p.Count; i++)
            {
                double seg = p[i].DistanceTo(p[i - 1]);
                if (l + seg >= at && seg > 1e-12) return p[i - 1] + (p[i] - p[i - 1]) * ((at - l) / seg);
                l += seg;
            }
            return p[p.Count - 1];
        }

        /// <summary>Part of a polyline between two positions given as index + fraction.</summary>
        private static List<Point2d> SubPolyline(List<Point2d> p, double u0, double u1)
        {
            Func<double, Point2d> at = u =>
            {
                int i = Math.Min(p.Count - 2, (int)Math.Floor(u));
                return p[i] + (p[i + 1] - p[i]) * (u - i);
            };
            var r = new List<Point2d> { at(u0) };
            for (int i = (int)Math.Floor(u0) + 1; i < u1 && i < p.Count; i++) r.Add(p[i]);
            r.Add(at(u1));
            return r;
        }

        /// <summary>Distance from a wall position straight across to the opposite wall (double.MaxValue if there is none).</summary>
        private static double Span(List<List<Point2d>> rings, List<double[]> cum, int t, double s, double spacing)
        {
            double g = spacing * 0.5;
            Point2d a = PointAt(rings[t], cum[t], s - g), b = PointAt(rings[t], cum[t], s + g), p0 = PointAt(rings[t], cum[t], s);
            Vector2d tan = b - a;
            if (!tan.Unitize()) return double.MaxValue;
            var nrm = new Vector2d(-tan.Y, tan.X);
            if (!InMaterial(rings, p0 + nrm * (spacing * 0.25))) nrm = -nrm;
            double hit = double.MaxValue;
            Point2d origin = p0 + nrm * 0.01;
            foreach (var other in rings)
                for (int k = 0; k < other.Count; k++)
                {
                    double tt = RaySegment(origin, nrm, other[k], other[(k + 1) % other.Count]);
                    if (tt > spacing * 0.5 && tt < hit) hit = tt;
                }
            return hit;
        }

        private static double CircularDistance(double s1, double s2, double len)
        {
            double d = Math.Abs(s1 - s2) % len;
            return Math.Min(d, len - d);
        }

        private static bool TooClose(List<Rib> ribs, double s, double len, double min)
        {
            foreach (var r in ribs) if (CircularDistance(r.S, s, len) < min) return true;
            return false;
        }

        // ------------------------------------------------------------------ bottom fill

        /// <summary>
        /// Fills the first layers solid. The fill lines lie one bead spacing apart and end one
        /// bead spacing from the wall, so everything fuses. Each fill is one continuous line where
        /// the shape allows it; the path engine starts the wall next to a fill end, so the nozzle
        /// turns from the wall straight into the fill.
        ///
        /// pattern 0 = auto: zigzag if a direction exists in which every line crosses the shape only
        ///   once (one continuous line), otherwise contour.
        /// pattern 1 = zigzag: parallel lines, direction turned by 90 degrees every layer. A shape
        ///   that a line crosses more than once (U shapes, holes) is split into parts.
        /// pattern 2 = contour: rings parallel to the wall, joined into one line that never crosses
        ///   itself and whose start and end lie next to each other at the wall (see FermatFill).
        ///   Works for any shape - curved hulls, U shapes, holes.
        /// </summary>
        public static List<SliceLayer> Bottom(List<SliceLayer> layers, PrintSettings settings, int count, int pattern, double angleDeg, List<string> log, out int filled, int topCount = 0)
        {
            double spacing = settings.BeadSpacing;
            var ordered = layers.OrderBy(l => l.Index).ToList();
            var result = new List<SliceLayer>(ordered.Count);
            filled = 0;
            int failed = 0, uncrossed = 0, zigzagReplaced = 0;
            Point3d lastEnd = Point3d.Unset; // where the fill of the layer below stops
            // Layers with the same outline get exactly the same fill line. Recomputing it would give
            // almost the same line, but small differences in the outline (a round hole is cut into
            // slightly different points on every layer) can move a ring change by a few millimetres,
            // and then one layer would not lie squarely on the other.
            List<List<Point2d>> previousRings = null, previousFills = null;
            List<Point2d> previousEntries = null;
            List<int> previousAbsorbed = null;

            for (int li = 0; li < ordered.Count; li++)
            {
                Cancel.Check();
                var src = ordered[li];
                // solid layers: the first 'count' (bottom) and the last 'topCount' (closed top)
                if (li >= count && li < ordered.Count - topCount) { lastEnd = Point3d.Unset; previousRings = null; result.Add(src); continue; }

                int inner = src.Rings != null && src.Rings.Count > 0 ? src.Rings.Max() : 0;
                var closed = new List<List<Point2d>>();
                var closedIndex = new List<int>(); // which contour of the layer each one is
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (!Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    if (src.Rings != null && src.Rings[c] != inner) continue;
                    closed.Add(Ring2d(src.Plane, src.Contours[c]));
                    closedIndex.Add(c);
                }
                if (closed.Count == 0) { failed++; result.Add(src); continue; }
                // walls of holes (nested inside an odd number of other walls)
                var holeWalls = new List<List<Point2d>>();
                var holeWallContour = new List<int>();
                var absorbed = new List<int>();
                for (int a = 0; a < closed.Count; a++)
                {
                    int depth = 0;
                    for (int b = 0; b < closed.Count; b++)
                        if (a != b && Inside(closed[b], closed[a][0])) depth++;
                    if ((depth & 1) == 1) { holeWalls.Add(new List<Point2d>(closed[a])); holeWallContour.Add(closedIndex[a]); }
                }

                var paths = new PathsD(closed.Count);
                for (int a = 0; a < closed.Count; a++)
                {
                    int depth = 0;
                    for (int b = 0; b < closed.Count; b++)
                        if (a != b && Inside(closed[b], closed[a][0])) depth++;
                    if ((Area(closed[a]) > 0) != ((depth & 1) == 0)) closed[a].Reverse();
                    var path = new PathD(closed[a].Count);
                    foreach (var p in closed[a]) path.Add(new PointD(p.X, p.Y));
                    paths.Add(path);
                }
                PathsD region = Clipper.InflatePaths(paths, -spacing, JoinType.Miter, EndType.Polygon, 2.0, 4);
                if (region == null || region.Count == 0) { failed++; result.Add(src); continue; }

                List<List<Point2d>> fills;
                var fermatEntries = new List<Point2d>();
                var regionRings = region.Select(path => path.Select(q => new Point2d(q.x, q.y)).ToList()).ToList();
                bool usedContour = false;
                Func<List<List<Point2d>>> contourFill = () =>
                {
                    usedContour = true;
                    if (previousFills != null && previousAbsorbed.Count <= holeWalls.Count && SameOutline(regionRings, previousRings, 0.1 * spacing))
                    {
                        fermatEntries.AddRange(previousEntries);
                        absorbed.AddRange(previousAbsorbed);
                        return previousFills.Select(f => new List<Point2d>(f)).ToList();
                    }
                    return FermatFill.Build(region, spacing, lastEnd.IsValid ? (Point2d?)To2d(src.Plane, lastEnd) : null, fermatEntries, holeWalls, absorbed);
                };
                if (pattern == 0)
                {
                    // Auto: a zigzag is the quickest fill, but only if every line crosses the shape
                    // once. Try a few directions; if none works, follow the wall instead.
                    fills = null;
                    double preferred = angleDeg + ((li & 1) == 1 ? 90.0 : 0.0);
                    foreach (double turn in new[] { 0.0, 90.0, 45.0, 135.0, 22.5, 67.5, 112.5, 157.5 })
                    {
                        var z = Zigzag(region, spacing, preferred + turn, false);
                        if (z.Count != 1) continue;
                        if (lastEnd.IsValid)
                        {
                            var other = Zigzag(region, spacing, preferred + turn, true);
                            if (other.Count == 1 && NearestEnd(other, src.Plane, lastEnd, out _, out _) < NearestEnd(z, src.Plane, lastEnd, out _, out _)) z = other;
                            // a zigzag only if it starts right where the layer below stopped; otherwise
                            // the nozzle would have to cross the part without printing
                            if (NearestEnd(z, src.Plane, lastEnd, out _, out _) > 3 * settings.BeadWidth) continue;
                        }
                        fills = z;
                        break;
                    }
                    if (fills == null) fills = contourFill();
                }
                else if (pattern == 2) fills = contourFill();
                else
                {
                    // Zigzag as asked, in the given direction (turned 90 degrees every layer) if it
                    // can be one line that starts where the layer below stopped. Otherwise the
                    // nearest direction that can, and if no direction gives one line (openings
                    // split the area), the contour fill: a zigzag in pieces would need moves
                    // without extrusion between them, and never crossing and never stopping come
                    // before the pattern.
                    double a = angleDeg + ((li & 1) == 1 ? 90.0 : 0.0);
                    fills = null;
                    List<List<Point2d>> single = null; double singleGap = double.MaxValue;
                    foreach (double turn in new[] { 0.0, 15.0, -15.0, 30.0, -30.0, 45.0, -45.0, 60.0, -60.0, 75.0, -75.0, 90.0 })
                    {
                        foreach (bool flip in new[] { false, true })
                        {
                            var z = Zigzag(region, spacing, a + turn, flip);
                            if (z.Count != 1) continue;
                            double gap = lastEnd.IsValid ? NearestEnd(z, src.Plane, lastEnd, out _, out _) : 0;
                            if (gap <= 3 * settings.BeadWidth) { fills = z; break; }
                            if (gap < singleGap) { singleGap = gap; single = z; }
                        }
                        if (fills != null) break;
                    }
                    if (fills == null) fills = single;
                    if (fills == null)
                    {
                        fills = contourFill();
                        zigzagReplaced++;
                    }
                }
                if (fills.Count == 0) { failed++; result.Add(src); continue; }
                if (usedContour)
                {
                    // Safety net: where the contour line is joined around several openings a link
                    // can end up across a bead. Never cross is the first rule, so such a place is
                    // re-joined the other way round (the line stays one line).
                    for (int f = 0; f < fills.Count; f++)
                    {
                        if (fills[f].Count < 4 || fills[f].Count > 20000) continue;
                        var pl0 = new Polyline(fills[f].Select(q => new Point3d(q.X, q.Y, 0)));
                        if (PathTools.CountCrossings(pl0) == 0) continue;
                        var fixedLine = PathTools.Uncross(pl0, 1e-6, out int moves, out _);
                        if (moves > 0 && PathTools.CountCrossings(fixedLine) == 0) { fills[f] = fixedLine.Select(q => new Point2d(q.X, q.Y)).ToList(); uncrossed++; }
                    }
                    previousRings = regionRings; previousFills = fills.Select(f => new List<Point2d>(f)).ToList();
                    previousEntries = new List<Point2d>(fermatEntries); previousAbsorbed = new List<int>(absorbed);
                }
                else previousRings = null;
                if (lastEnd.IsValid)
                {
                    NearestEnd(fills, src.Plane, lastEnd, out int nf, out bool atStart);
                    lastEnd = To3d(src.Plane, atStart ? fills[nf][fills[nf].Count - 1] : fills[nf][0]);
                }
                else lastEnd = To3d(src.Plane, fills[fills.Count - 1][fills[fills.Count - 1].Count - 1]);

                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp, Reference = src.Reference };
                // hole walls that are now part of the fill line are not printed a second time
                var skip = new HashSet<int>(absorbed.Select(i => holeWallContour[i]));
                dst.Rings = new List<int>();
                dst.ContourRoles = new List<PathRole>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (skip.Contains(c)) continue;
                    dst.Contours.Add(src.Contours[c]);
                    dst.Rings.Add(src.Rings != null ? src.Rings[c] : 0);
                    dst.ContourRoles.Add(src.ContourRoles != null && c < src.ContourRoles.Count ? src.ContourRoles[c] : PathRole.Wall);
                }
                dst.EntryHints = new List<Point3d>();
                foreach (var f in fills)
                {
                    if (f.Count < 2) continue;
                    var pl = new Polyline(f.Count);
                    foreach (var q in f) pl.Add(To3d(src.Plane, q));
                    dst.Contours.Add(pl);
                    dst.Rings.Add(inner + 1);
                    dst.ContourRoles.Add(PathRole.Bottom);
                    if (fermatEntries.Count == 0)
                    {
                        dst.EntryHints.Add(pl[0]);
                        dst.EntryHints.Add(pl[pl.Count - 1]);
                    }
                }
                foreach (Point2d e in fermatEntries) dst.EntryHints.Add(To3d(src.Plane, e));
                filled++;
                result.Add(dst);
            }
            if (uncrossed > 0) log?.Add(Inv.F($"{uncrossed} bottom layer(s): the fill line was re-joined at a place where it would have crossed itself."));
            if (zigzagReplaced > 0) log?.Add(Inv.F($"{zigzagReplaced} bottom layer(s) are filled following the wall instead of in a zigzag: openings split them, and a zigzag in pieces would need moves without extrusion between them."));
            if (failed > 0) log?.Add(Inv.F($"{failed} bottom layer(s) could not be filled (no closed wall, or too narrow for a fill line)."));
            return result;
        }

        /// <summary>True if two outlines are the same shape within the tolerance.</summary>
        private static bool SameOutline(List<List<Point2d>> a, List<List<Point2d>> b, double tolerance)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            var cumB = b.Select(Cumulative).ToList();
            var cumA = a.Select(Cumulative).ToList();
            for (int i = 0; i < a.Count; i++)
            {
                // the matching ring: same length, and every sampled point of one lies on the other
                bool found = false;
                for (int j = 0; j < b.Count && !found; j++)
                {
                    if (Math.Abs(cumA[i][a[i].Count] - cumB[j][b[j].Count]) > 4 * tolerance) continue;
                    bool close = true;
                    int stride = Math.Max(1, a[i].Count / 40);
                    for (int k = 0; k < a[i].Count && close; k += stride)
                    {
                        ClosestS(b[j], cumB[j], a[i][k], out double d);
                        if (d > tolerance) close = false;
                    }
                    stride = Math.Max(1, b[j].Count / 40);
                    for (int k = 0; k < b[j].Count && close; k += stride)
                    {
                        ClosestS(a[i], cumA[i], b[j][k], out double d);
                        if (d > tolerance) close = false;
                    }
                    found = close;
                }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>Distance from p to the nearest end of any fill line, and which end that is.</summary>
        private static double NearestEnd(List<List<Point2d>> fills, Plane plane, Point3d p, out int fill, out bool atStart)
        {
            double best = double.MaxValue; fill = 0; atStart = true;
            for (int i = 0; i < fills.Count; i++)
            {
                if (fills[i].Count < 2) continue;
                double d0 = To3d(plane, fills[i][0]).DistanceTo(p), d1 = To3d(plane, fills[i][fills[i].Count - 1]).DistanceTo(p);
                if (d0 < best) { best = d0; fill = i; atStart = true; }
                if (d1 < best) { best = d1; fill = i; atStart = false; }
            }
            return best;
        }

        private static List<List<Point2d>> Zigzag(PathsD region, double spacing, double angleDeg, bool rightFirst)
        {
            double ang = angleDeg * Math.PI / 180.0, cs = Math.Cos(ang), sn = Math.Sin(ang);
            // work in a frame where the fill lines are horizontal
            var edges = new List<Point2d[]>();
            double ymin = double.MaxValue, ymax = double.MinValue;
            foreach (var path in region)
                for (int i = 0; i < path.Count; i++)
                {
                    PointD p = path[i], q = path[(i + 1) % path.Count];
                    var a = new Point2d(p.x * cs + p.y * sn, -p.x * sn + p.y * cs);
                    var b = new Point2d(q.x * cs + q.y * sn, -q.x * sn + q.y * cs);
                    edges.Add(new[] { a, b });
                    ymin = Math.Min(ymin, a.Y); ymax = Math.Max(ymax, a.Y);
                }

            const double eps = 1e-3;
            double range = ymax - ymin - 2 * eps;
            int m = range > 0 ? Math.Max(1, (int)Math.Round(range / spacing)) : 0;
            double pitch = m > 0 ? range / m : 0;

            // chains = parts of the shape that one zigzag can fill without lifting
            var chains = new List<List<double[]>>();
            var lastLine = new List<int>();
            for (int j = 0; j <= m; j++)
            {
                double y = m > 0 ? ymin + eps + j * pitch : 0.5 * (ymin + ymax);
                var xs = new List<double>();
                foreach (var e in edges)
                    if ((e[0].Y > y) != (e[1].Y > y))
                        xs.Add(e[0].X + (y - e[0].Y) / (e[1].Y - e[0].Y) * (e[1].X - e[0].X));
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    double x0 = xs[k], x1 = xs[k + 1];
                    if (x1 - x0 < spacing * 0.5) continue;
                    int found = -1;
                    for (int c = 0; c < chains.Count; c++)
                    {
                        if (lastLine[c] != j - 1) continue;
                        double[] prev = chains[c][chains[c].Count - 1];
                        if (Math.Min(prev[1], x1) - Math.Max(prev[0], x0) > 0) { found = c; break; }
                    }
                    if (found < 0) { chains.Add(new List<double[]>()); lastLine.Add(j); found = chains.Count - 1; }
                    chains[found].Add(new[] { x0, x1, y });
                    lastLine[found] = j;
                }
            }

            var result = new List<List<Point2d>>();
            foreach (var chain in chains)
            {
                var pts = new List<Point2d>(chain.Count * 2);
                foreach (var seg in chain)
                {
                    var l = new Point2d(seg[0], seg[2]);
                    var r = new Point2d(seg[1], seg[2]);
                    bool leftFirst = pts.Count == 0 ? !rightFirst : pts[pts.Count - 1].DistanceTo(l) <= pts[pts.Count - 1].DistanceTo(r);
                    pts.Add(leftFirst ? l : r);
                    pts.Add(leftFirst ? r : l);
                }
                // back to the layer frame
                for (int i = 0; i < pts.Count; i++)
                    pts[i] = new Point2d(pts[i].X * cs - pts[i].Y * sn, pts[i].X * sn + pts[i].Y * cs);
                if (pts.Count >= 2) result.Add(pts);
            }
            return result;
        }

        internal static double[] Cumulative(List<Point2d> ring)
        {
            var s = new double[ring.Count + 1];
            for (int i = 0; i < ring.Count; i++) s[i + 1] = s[i] + ring[i].DistanceTo(ring[(i + 1) % ring.Count]);
            return s;
        }

        private static Point2d Centroid(List<Point2d> ring)
        {
            double x = 0, y = 0;
            foreach (var p in ring) { x += p.X; y += p.Y; }
            return new Point2d(x / ring.Count, y / ring.Count);
        }

        internal static Point2d PointAt(List<Point2d> ring, double[] cum, double s)
        {
            double len = cum[ring.Count];
            s %= len; if (s < 0) s += len;
            int i = Array.BinarySearch(cum, s);
            if (i < 0) i = ~i - 1;
            if (i >= ring.Count) i = ring.Count - 1;
            double seg = cum[i + 1] - cum[i];
            Point2d a = ring[i], b = ring[(i + 1) % ring.Count];
            return seg < 1e-12 ? a : a + (b - a) * ((s - cum[i]) / seg);
        }

        /// <summary>Contour points from arc length 'from' to 'to' (to may lie past one lap), ends included.</summary>
        internal static List<Point2d> Sub(List<Point2d> ring, double[] cum, double from, double to)
        {
            double len = cum[ring.Count];
            var r = new List<Point2d> { PointAt(ring, cum, from) };
            int laps = (int)Math.Floor(from / len);
            for (int lap = laps; lap <= laps + 2; lap++)
                for (int i = 0; i < ring.Count; i++)
                {
                    double s = cum[i] + lap * len;
                    if (s > from + 1e-9 && s < to - 1e-9) r.Add(ring[i]);
                }
            r.Add(PointAt(ring, cum, to));
            return r;
        }

        internal static double ClosestS(List<Point2d> ring, double[] cum, Point2d p, out double distance)
        {
            double best = double.MaxValue, bestS = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2d a = ring[i], b = ring[(i + 1) % ring.Count];
                Vector2d ab = b - a;
                double l2 = ab.SquareLength;
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                double d = (a + ab * t).DistanceTo(p);
                if (d < best) { best = d; bestS = cum[i] + t * Math.Sqrt(l2); }
            }
            distance = best;
            return bestS;
        }

        /// <summary>Distance along the ray to the segment, or double.MaxValue if it misses.</summary>
        private static double RaySegment(Point2d o, Vector2d d, Point2d a, Point2d b)
        {
            Vector2d e = b - a;
            double den = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(den) < 1e-12) return double.MaxValue;
            Vector2d ao = a - o;
            double t = (ao.X * e.Y - ao.Y * e.X) / den;
            double u = (ao.X * d.Y - ao.Y * d.X) / den;
            return t >= 0 && u >= 0 && u <= 1 ? t : double.MaxValue;
        }

        internal static double PointSegment(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double l2 = ab.SquareLength;
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
            return (a + ab * t).DistanceTo(p);
        }

        private static double SegmentDistance(Point2d a, Point2d b, Point2d c, Point2d d)
        {
            Vector2d ab = b - a;
            double len = ab.Length;
            if (len > 1e-9)
            {
                Vector2d dir = ab / len;
                double t = RaySegment(a, dir, c, d);
                if (t <= len) return 0;
            }
            return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        /// <summary>Turns the user's rib locations into 2D points in the layer plane.</summary>
        private static IEnumerable<Point2d> LocationPoints(List<object> locations, SliceLayer layer, List<List<Point2d>> rings)
        {
            if (locations == null) yield break;
            foreach (object o in locations)
            {
                if (o is Point3d p3) yield return To2d(layer.Plane, p3);
                else if (o is Point pt) yield return To2d(layer.Plane, pt.Location);
                else if (o is Line ln)
                {
                    var lc = new LineCurve(ln);
                    if (IsGuide(lc, layer)) continue;
                    foreach (var q in CurveHits(lc, layer)) yield return q;
                }
                else if (o is Curve crv)
                {
                    if (IsGuide(crv, layer)) continue; // drawn in the layer plane: handled as a rib path
                    foreach (var q in CurveHits(crv, layer)) yield return q;
                }
                else if (o is Plane cut)
                {
                    // where the cutting plane crosses the walls
                    foreach (var ring in rings)
                        for (int k = 0; k < ring.Count; k++)
                        {
                            Point3d a = To3d(layer.Plane, ring[k]), b = To3d(layer.Plane, ring[(k + 1) % ring.Count]);
                            double da = cut.DistanceTo(a), db = cut.DistanceTo(b);
                            if ((da >= 0) == (db >= 0)) continue;
                            yield return To2d(layer.Plane, a + (b - a) * (da / (da - db)));
                        }
                }
            }
        }

        private static IEnumerable<Point2d> CurveHits(Curve crv, SliceLayer layer)
        {
            var hits = Intersection.CurvePlane(crv, layer.Plane, 0.01);
            if (hits == null) yield break;
            foreach (var h in hits) yield return To2d(layer.Plane, h.PointA);
        }
    }
}
