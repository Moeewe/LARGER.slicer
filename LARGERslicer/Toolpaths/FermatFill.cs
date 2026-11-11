using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Solid fill as ONE line that never crosses itself and starts and ends side by side.
    ///
    /// Idea (after Zhao et al. 2016, "Connected Fermat Spirals"):
    ///  1. Offset the region inward ring by ring, one bead spacing apart.
    ///  2. A run of rings nested one inside the other is printed as a Fermat spiral: the line
    ///     travels inward on every second ring (0, 2, 4 ...), turns at the centre and comes back
    ///     out on the rings in between (... 5, 3, 1). All ring changes happen in one narrow seam,
    ///     and the line ends right next to where it began.
    ///  3. Where the rings split into pockets, or where rings grow around a hole, that group of
    ///     rings is its own spiral. Because a spiral starts and ends side by side, it can be
    ///     spliced into the neighbouring ring like a side trip: leave the ring, run the spiral,
    ///     rejoin the ring a little further on.
    /// The result enters next to the wall and leaves next to the wall, so the walls of this layer
    /// and the next connect to it with one short step.
    /// </summary>
    internal static class FermatFill
    {
        private class Ring
        {
            public List<Point2d> P;         // counter-clockwise, closing point not repeated
            public double[] Cum;
            public double Len;
            public bool Positive;           // true: shrinks inward from an outer boundary; false: grows around a hole
            public Ring Parent;
            public List<Ring> Children = new List<Ring>();
            public List<Ring> Guests = new List<Ring>(); // other groups that are entered from this ring
            public bool Used;
        }

        public static List<List<Point2d>> Build(PathsD region, double spacing, Point2d? startNear, List<Point2d> entries,
            List<List<Point2d>> holeWalls = null, List<int> absorbedHoleWalls = null)
        {
            double seam = 2.5 * spacing;   // length of the gap every ring leaves at the seam
            var levels = new List<List<Ring>>();

            // ---- 1) rings, level by level
            PathsD cur = region;
            int guard = 0;
            while (cur != null && cur.Count > 0 && guard++ < 5000)
            {
                var level = new List<Ring>();
                foreach (var path in cur)
                {
                    if (path.Count < 3) continue;
                    var pts = path.Select(p => new Point2d(p.x, p.y)).ToList();
                    double area = 0;
                    for (int i = 0; i < pts.Count; i++) { Point2d a = pts[i], b = pts[(i + 1) % pts.Count]; area += a.X * b.Y - b.X * a.Y; }
                    bool positive = area > 0;
                    if (!positive) pts.Reverse();
                    var ring = new Ring { P = pts, Cum = FillEngine.Cumulative(pts), Positive = positive };
                    ring.Len = ring.Cum[pts.Count];
                    if (ring.Len < seam + 3 * spacing) continue; // too small to print
                    level.Add(ring);
                }
                if (level.Count == 0) break;
                levels.Add(level);
                // in by 1.5 spacings and back out by 0.5: removes slivers too narrow for a bead
                PathsD inner = Clipper.InflatePaths(cur, -1.5 * spacing, JoinType.Miter, EndType.Polygon, 2.0, 4);
                cur = inner == null || inner.Count == 0 ? null : Clipper.InflatePaths(inner, 0.5 * spacing, JoinType.Round, EndType.Polygon, 2.0, 4, 0.05);
            }
            var result = new List<List<Point2d>>();
            if (levels.Count == 0) return result;

            // ---- 2) which ring lies directly inside which
            for (int k = 1; k < levels.Count; k++)
                foreach (Ring r in levels[k])
                {
                    Ring best = null; double bestD = 1.75 * spacing;
                    foreach (Ring p in levels[k - 1])
                    {
                        if (p.Positive != r.Positive) continue;
                        double d = Distance(r, p);
                        if (d < bestD) { bestD = d; best = p; }
                    }
                    if (best != null) { r.Parent = best; best.Children.Add(r); }
                }

            // ---- 2b) the wall of a hole becomes the innermost ring of the rings around that hole.
            // It is then printed as part of the fill line, so the nozzle never has to travel
            // across the fill to reach it.
            if (holeWalls != null)
                for (int w = 0; w < holeWalls.Count; w++)
                {
                    var pts = new List<Point2d>(holeWalls[w]);
                    double area = 0;
                    for (int i = 0; i < pts.Count; i++) { Point2d a = pts[i], b = pts[(i + 1) % pts.Count]; area += a.X * b.Y - b.X * a.Y; }
                    if (area < 0) pts.Reverse();
                    var wall = new Ring { P = pts, Cum = FillEngine.Cumulative(pts), Positive = false };
                    wall.Len = wall.Cum[pts.Count];
                    if (wall.Len < seam + 3 * spacing) continue;
                    Ring first = null; double bestD = 1.75 * spacing;
                    foreach (Ring r in levels[0])
                    {
                        if (r.Positive || r.Parent != null) continue;
                        double d = Distance(r, wall);
                        if (d < bestD) { bestD = d; first = r; }
                    }
                    if (first == null) continue;
                    first.Parent = wall; wall.Children.Add(first);
                    levels[0].Add(wall); // found by the steps below like any other ring without a parent
                    absorbedHoleWalls?.Add(w);
                }

            // ---- 3) groups that hang in the air (rings around holes, orphans) get a host ring
            var all = levels.SelectMany(l => l).ToList();
            foreach (Ring r in all)
            {
                if (r.Parent != null) continue;
                if (r.Positive && levels[0].Contains(r)) continue; // an island of its own: top level
                Ring entry = Entry(r);
                var own = new HashSet<Ring>(Chain(r));
                Ring host = null; double bestD = double.MaxValue;
                foreach (Ring h in all)
                {
                    if (own.Contains(h) || (!r.Positive && !h.Positive)) continue;
                    double d = Distance(entry, h);
                    if (d < bestD) { bestD = d; host = h; }
                }
                if (host != null) host.Guests.Add(r);
            }

            // ---- 4) one line per island
            foreach (Ring root in levels[0].Where(r => r.Positive))
            {
                Point2d hint = startNear ?? new Point2d(root.P.Max(p => p.X) + 1e6, root.P.Average(p => p.Y));
                var line = Group(root, hint, Vector2d.Zero, spacing, seam);
                if (line.Count < 2) continue;
                result.Add(line);
                // The wall meets the fill at the fill's first point (see FillEngine.Bottom).
                entries?.Add(line[0]);
            }
            // anything not reached (e.g. a hole group whose host was too small to print)
            foreach (Ring r in all.Where(x => !x.Used && x.Parent == null))
            {
                var line = Group(r, r.P[0], Vector2d.Zero, spacing, seam);
                if (line.Count >= 2) result.Add(line);
            }
            return result;
        }

        /// <summary>The run of rings nested one inside the other, starting at 'start'.</summary>
        private static List<Ring> Chain(Ring start)
        {
            var chain = new List<Ring> { start };
            Ring cur = start;
            while (cur.Children.Count == 1) { cur = cur.Children[0]; chain.Add(cur); }
            return chain;
        }

        /// <summary>The ring through which a group is entered: its outer ring, or for rings around a hole the largest one.</summary>
        private static Ring Entry(Ring start) => start.Positive ? start : Chain(start).Last();

        private static double Distance(Ring a, Ring b)
        {
            double best = double.MaxValue;
            int stride = Math.Max(1, a.P.Count / 24);
            for (int i = 0; i < a.P.Count; i += stride)
            {
                FillEngine.ClosestS(b.P, b.Cum, a.P[i], out double d);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// The Fermat spiral of one group, with all its side trips. hostDirection is the travel
        /// direction of the ring this group is entered from (zero for the top level); the group
        /// runs against it so the way in and the way out do not cross.
        /// </summary>
        private static List<Point2d> Group(Ring start, Point2d hint, Vector2d hostDirection, double spacing, double seam)
        {
            var line = new List<Point2d>();
            var chain = Chain(start);
            Ring chainEnd = chain[chain.Count - 1];
            // (a spiral that is cut short below hands the rest of the chain on as a guest)
            var rings = start.Positive ? chain : Enumerable.Reverse(chain).ToList();
            if (rings.Any(r => r.Used)) return line;

            // seam: on every ring the point nearest to the seam point of the ring before
            var s0 = new double[rings.Count];
            Point2d c = hint;
            for (int i = 0; i < rings.Count; i++)
            {
                s0[i] = FillEngine.ClosestS(rings[i].P, rings[i].Cum, c, out _);
                if (i == 0 && hostDirection.Length < 1e-9)
                {
                    // top level: put the seam on a straight piece of the ring, never on a corner
                    Ring r0 = rings[0];
                    foreach (double shift in new[] { 0.0, 2 * seam, -2 * seam, 4 * seam, -4 * seam, 6 * seam, -6 * seam })
                    {
                        Vector2d a = FillEngine.PointAt(r0.P, r0.Cum, s0[0] + shift) - FillEngine.PointAt(r0.P, r0.Cum, s0[0] + shift - 1.5 * seam);
                        Vector2d b = FillEngine.PointAt(r0.P, r0.Cum, s0[0] + shift + 1.5 * seam) - FillEngine.PointAt(r0.P, r0.Cum, s0[0] + shift);
                        if (!a.Unitize() || !b.Unitize() || a * b < 0.966) continue; // bend of more than ~15 degrees
                        s0[0] += shift;
                        break;
                    }
                }
                Point2d next = FillEngine.PointAt(rings[i].P, rings[i].Cum, s0[i]);
                if (i > 0 && start.Positive && next.DistanceTo(c) > 2.2 * spacing)
                {
                    // The next ring is not next to the seam (one arm of the shape has closed and
                    // the rings carry on only in another arm). Stop the spiral here; the rest is
                    // its own group, entered from this ring where the two are side by side.
                    rings[i - 1].Guests.Add(rings[i]);
                    rings = rings.Take(i).ToList();
                    break;
                }
                c = next;
            }
            int m = rings.Count;
            foreach (Ring r in rings) r.Used = true;

            // run against the host so that entering and leaving do not cross
            double dir = 1;
            if (hostDirection.Length > 1e-9)
            {
                Vector2d tangent = FillEngine.PointAt(rings[0].P, rings[0].Cum, s0[0] + 0.5) - FillEngine.PointAt(rings[0].P, rings[0].Cum, s0[0] - 0.5);
                if (tangent * hostDirection > 0) dir = -1;
            }

            // inward on rings 0, 2, 4 ... each from the seam once around to just before the seam
            for (int i = 0; i < m; i += 2)
                Emit(line, rings[i], rings[i] == chainEnd, s0[i], dir, 0, rings[i].Len - seam, spacing, seam);
            // outward on the rings in between, in the opposite direction
            for (int i = (m - 1) % 2 == 1 ? m - 1 : m - 2; i >= 1; i -= 2)
                Emit(line, rings[i], rings[i] == chainEnd, s0[i], dir, rings[i].Len - seam, 0, spacing, seam);
            return line;
        }

        /// <summary>
        /// Adds one ring from x0 to x1 (distance from its seam point, measured in the group's
        /// direction), with the side trips into the groups that are entered from this ring.
        /// </summary>
        private static void Emit(List<Point2d> line, Ring ring, bool isChainEnd, double s0, double dir, double x0, double x1, double spacing, double seam)
        {
            double sign = x1 >= x0 ? 1 : -1;
            Func<double, Point2d> at = x => FillEngine.PointAt(ring.P, ring.Cum, s0 + dir * x);

            // groups entered from here: pockets inside the last ring of a run, and guests
            var trips = new List<KeyValuePair<double, Ring>>();
            var visitors = new List<Ring>(ring.Guests);
            if (isChainEnd && ring.Children.Count != 1) visitors.AddRange(ring.Children);
            foreach (Ring v in visitors)
            {
                if (v.Used) continue;
                Ring entry = Entry(v);
                // Where on this ring the other group is side by side with it. Around a round hole
                // that is true all the way round, so "the nearest point" would jump about from layer
                // to layer. Of all places that are (nearly) nearest, take the one closest to this
                // ring's own seam: the result is the same on every layer and all ring changes of the
                // part collect along one line.
                int stride = Math.Max(1, entry.P.Count / 240);
                var near = new List<double[]>(); // { distance, position on this ring }
                double best = double.MaxValue;
                for (int i = 0; i < entry.P.Count; i += stride)
                {
                    double sHere = FillEngine.ClosestS(ring.P, ring.Cum, entry.P[i], out double d);
                    near.Add(new[] { d, sHere });
                    if (d < best) best = d;
                }
                Point2d seamPoint = at(0);
                double bestS = 0, bestToSeam = double.MaxValue;
                foreach (var cand in near)
                {
                    if (cand[0] > best + 0.25 * spacing) continue;
                    double toSeam = FillEngine.PointAt(ring.P, ring.Cum, cand[1]).DistanceTo(seamPoint);
                    if (toSeam < bestToSeam) { bestToSeam = toSeam; bestS = cand[1]; }
                }
                double x = ((dir * (bestS - s0)) % ring.Len + ring.Len) % ring.Len;
                // keep clear of this ring's own seam
                double lo = Math.Min(x0, x1) + 2 * seam, hi = Math.Max(x0, x1) - 2 * seam;
                if (hi <= lo) continue;
                trips.Add(new KeyValuePair<double, Ring>(Math.Max(lo, Math.Min(hi, x)), v));
            }
            trips = (sign > 0 ? trips.OrderBy(t => t.Key) : trips.OrderByDescending(t => t.Key)).ToList();

            double from = x0;
            double lastTrip = double.NaN;
            foreach (var trip in trips)
            {
                double x = trip.Key;
                if (!double.IsNaN(lastTrip) && Math.Abs(x - lastTrip) < 2 * seam) x = lastTrip + sign * 2 * seam;
                if ((x1 - x) * sign < 2 * seam) continue; // no room left before the end of this ring
                lastTrip = x;

                AddStretch(line, ring, s0, dir, from, x - sign * 0.5 * spacing);
                Vector2d travel = at(x + sign * 0.5) - at(x - sign * 0.5);
                travel.Unitize();
                var side = Group(trip.Value, at(x), travel, spacing, seam);
                line.AddRange(side);
                from = x + sign * 0.5 * seam;
            }
            AddStretch(line, ring, s0, dir, from, x1);
        }

        private static void AddStretch(List<Point2d> line, Ring ring, double s0, double dir, double xFrom, double xTo)
        {
            double a = s0 + dir * xFrom, b = s0 + dir * xTo;
            List<Point2d> pts;
            if (b >= a) pts = FillEngine.Sub(ring.P, ring.Cum, a, b);
            else { pts = FillEngine.Sub(ring.P, ring.Cum, b, a); pts.Reverse(); }
            line.AddRange(pts);
        }
    }
}
