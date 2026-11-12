using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Cell infill (honeycomb, grid) as part of the one continuous wall line.
    /// Every cell of the pattern becomes a closed loop half a bead spacing inside the cell, so
    /// the loops of two neighbouring cells lie exactly one bead spacing apart and fuse into a
    /// double wall. The loops are then joined into ONE closed line: a spanning tree over the
    /// neighbouring loops (the wall is the root) says which loops are joined, and each join opens
    /// both loops over one bead spacing and connects them crosswise with two short bridges.
    /// The result cannot cross itself, and because it is one closed contour it also prints in
    /// spiral (vase) mode. Same idea as the offset-and-bridge honeycomb fills discussed on the
    /// McNeel forum (L. Delrieu) and the "connected Eulerian graph" honeycomb in the literature.
    /// </summary>
    public static partial class FillEngine
    {
        // ---- closed 2D loops without repeated end point ----
        private static double[] LoopCum(List<Point2d> p)
        {
            var c = new double[p.Count + 1];
            for (int i = 0; i < p.Count; i++) c[i + 1] = c[i] + p[i].DistanceTo(p[(i + 1) % p.Count]);
            return c;
        }

        private static Point2d LoopAt(List<Point2d> p, double[] c, double s)
        {
            double len = c[p.Count];
            s %= len; if (s < 0) s += len;
            int lo = 0, hi = p.Count;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (c[mid] <= s) lo = mid; else hi = mid; }
            double seg = c[lo + 1] - c[lo];
            double t = seg > 1e-12 ? (s - c[lo]) / seg : 0;
            Point2d a = p[lo], b = p[(lo + 1) % p.Count];
            return new Point2d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static double LoopClosest(List<Point2d> p, double[] c, Point2d q, out double distance)
        {
            double best = double.MaxValue, bestS = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Point2d a = p[i], b = p[(i + 1) % p.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 > 1e-18 ? ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / l2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double ex = a.X + dx * t - q.X, ey = a.Y + dy * t - q.Y, d = ex * ex + ey * ey;
                if (d < best) { best = d; bestS = c[i] + Math.Sqrt(l2) * t; }
            }
            distance = Math.Sqrt(best);
            return bestS;
        }

        /// <summary>
        /// The same loop, starting at a place given by its shape alone (the point furthest in one
        /// fixed direction). A slicer hands every layer over with its own start point; whatever is
        /// decided along a loop must not depend on that, or it lands somewhere else on every layer.
        /// </summary>
        private static List<Point2d> LoopRooted(List<Point2d> p)
        {
            if (p == null || p.Count < 3) return p;
            var c = LoopCum(p);
            if (c[p.Count] < 1e-9) return p;
            double s0 = LoopClosest(p, c, new Point2d(1e7, 3.7e6), out _);
            var r = LoopOpen(p, c, s0, s0);
            if (r.Count > 3) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>The loop as an open line from sFrom forward, once around, to sTo.</summary>
        private static List<Point2d> LoopOpen(List<Point2d> p, double[] c, double sFrom, double sTo)
        {
            double len = c[p.Count];
            sFrom %= len; if (sFrom < 0) sFrom += len;
            sTo %= len; if (sTo < 0) sTo += len;
            if (sTo <= sFrom) sTo += len;
            var r = new List<Point2d> { LoopAt(p, c, sFrom) };
            int n = p.Count;
            // first vertex after sFrom
            int i = 0;
            while (i < n && c[i] <= sFrom + 1e-9) i++;
            for (int k = 0; k < 2 * n + 2; k++, i++)
            {
                double s = c[i % n] + (i / n) * len;
                if (s >= sTo - 1e-9) break;
                r.Add(p[i % n]);
            }
            r.Add(LoopAt(p, c, sTo));
            return r;
        }

        /// <summary>
        /// Corners of a closed loop: places where the direction turns by more than 35 degrees
        /// within a short window. Measuring over a window makes the result independent of how
        /// finely the loop is divided, so a smooth curve does not flicker between "has corners"
        /// and "has none" from layer to layer. kind: +1 convex, -1 concave (counter-clockwise loop).
        /// </summary>
        private static List<double> LoopCorners(List<Point2d> p, double[] cum, double win, List<int> kinds)
        {
            double len = cum[p.Count];
            var at = new List<double>(); var strength = new List<double>(); var kind = new List<int>();
            for (int i = 0; i < p.Count; i++)
            {
                Point2d a = LoopAt(p, cum, cum[i] - win), b = p[i], c = LoopAt(p, cum, cum[i] + win);
                double ax = b.X - a.X, ay = b.Y - a.Y, bx = c.X - b.X, by = c.Y - b.Y;
                double cross = ax * by - ay * bx, ang = Math.Atan2(Math.Abs(cross), ax * bx + ay * by);
                if (ang < 35.0 * Math.PI / 180.0) continue;
                // one corner per neighbourhood: keep the sharpest
                int near = -1;
                for (int k = 0; k < at.Count; k++) { double d = Math.Abs(at[k] - cum[i]); d = Math.Min(d, len - d); if (d < 2 * win) { near = k; break; } }
                if (near < 0) { at.Add(cum[i]); strength.Add(ang); kind.Add(cross > 0 ? 1 : -1); }
                else if (ang > strength[near]) { at[near] = cum[i]; strength[near] = ang; kind[near] = cross > 0 ? 1 : -1; }
            }
            var order = Enumerable.Range(0, at.Count).OrderBy(k => at[k]).ToList();
            if (kinds != null) { kinds.Clear(); kinds.AddRange(order.Select(k => kind[k])); }
            return order.Select(k => at[k]).ToList();
        }

        private class CellJoin { public int A, B; public List<double[]> At = new List<double[]>(); } // sA, sB

        /// <summary>
        /// One closed line: the wall with all cell loops joined to it.
        /// wall: centre line of the wall bead, counter-clockwise. material: the area inside the
        /// wall centre line (holes cut out). origin: fixed point of the pattern, so the cells
        /// stand exactly above each other from layer to layer.
        /// </summary>
        internal static List<Point2d> CellLine(List<Point2d> wall, PathsD material, double spacing, int pattern,
            double size, double angleDeg, Point2d origin, out int joined, out int dropped,
            List<List<Point2d>> holeWalls = null, List<int> absorbedHoles = null, double stretch = 1.0, List<List<Point2d>> readyLoops = null, List<PathD> customCells = null,
            List<Point2d> prevJoints = null, List<Point2d> joints = null)
        {
            joined = 0; dropped = 0;
            size = Math.Max(size, 4.0 * spacing);
            stretch = Math.Max(0.25, Math.Min(4.0, stretch));
            PathsD r0 = Clipper.InflatePaths(material, -0.5 * spacing, JoinType.Miter, EndType.Polygon, 2.0, 4);
            if (r0 == null || r0.Count == 0) return null;

            // ---- pattern cells covering the region
            double ang = angleDeg * Math.PI / 180.0, cs = Math.Cos(ang), sn = Math.Sin(ang);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var path in r0)
                foreach (var q in path)
                {
                    double x = q.x - origin.X, y = q.y - origin.Y;
                    double u = x * cs + y * sn, v = -x * sn + y * cs;
                    if (u < minU) minU = u; if (u > maxU) maxU = u;
                    if (v < minV) minV = v; if (v > maxV) maxV = v;
                }
            // stretch > 1 makes the cells longer along the pattern's first direction
            Func<double, double, PointD> world = (u, v) => { u *= stretch; return new PointD(origin.X + u * cs - v * sn, origin.Y + u * sn + v * cs); };
            minU /= stretch; maxU /= stretch;
            var cells = new List<PathD>();
            if (customCells != null) cells.AddRange(customCells);
            else if (readyLoops != null) { }
            else if (pattern == 1)
            {
                int i0 = (int)Math.Floor(minU / size), i1 = (int)Math.Ceiling(maxU / size);
                int j0 = (int)Math.Floor(minV / size), j1 = (int)Math.Ceiling(maxV / size);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        cells.Add(new PathD { world(i * size, j * size), world((i + 1) * size, j * size), world((i + 1) * size, (j + 1) * size), world(i * size, (j + 1) * size) });
            }
            else
            {
                double r = size / Math.Sqrt(3.0), dx = 1.5 * r;
                int i0 = (int)Math.Floor(minU / dx) - 1, i1 = (int)Math.Ceiling(maxU / dx) + 1;
                int j0 = (int)Math.Floor(minV / size) - 1, j1 = (int)Math.Ceiling(maxV / size) + 1;
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        double cu = i * dx, cv = (j + ((i & 1) != 0 ? 0.5 : 0.0)) * size;
                        var hex = new PathD(6);
                        for (int k = 0; k < 6; k++) hex.Add(world(cu + r * Math.Cos(k * Math.PI / 3.0), cv + r * Math.Sin(k * Math.PI / 3.0)));
                        cells.Add(hex);
                    }
            }
            if (cells.Count > 20000) return null;

            // ---- nodes: 0 = wall (ccw), others = cell loops (cw, so neighbours run in opposite directions)
            var nodes = new List<List<Point2d>> { wall };
            // walls of holes join the line too (clockwise, like the cells next to them)
            int holeCount = 0;
            if (holeWalls != null)
                foreach (var h in holeWalls)
                {
                    var loop = new List<Point2d>(h);
                    // a hole that lies inside a ready loop (a wave line runs around it) has to
                    // turn the other way, so that the two run in opposite directions side by side
                    bool enclosed = readyLoops != null && loop.Count > 0 && readyLoops.Any(rl => Inside(rl, loop[0]));
                    if ((Area(loop) > 0) != enclosed) loop.Reverse();
                    nodes.Add(loop); holeCount++;
                }
            double minArea = 2.5 * spacing * spacing;
            if (readyLoops != null)
                foreach (var rl in readyLoops)
                {
                    var loop = new List<Point2d>(rl);
                    if (Area(loop) > 0) loop.Reverse();
                    nodes.Add(loop);
                }
            foreach (var cell in cells)
            {
                PathsD clip = Clipper.Intersect(new PathsD { cell }, r0, FillRule.NonZero, 4);
                if (clip == null || clip.Count == 0) continue;
                PathsD inner = Clipper.InflatePaths(clip, -0.5 * spacing, JoinType.Miter, EndType.Polygon, 2.0, 4);
                if (inner == null) continue;
                foreach (var path in inner)
                {
                    if (path.Count < 3) continue;
                    double area = Clipper.Area(path);
                    if (area < 0) continue; // a hole inside a cell: its wall is printed separately
                    if (area < minArea) { dropped++; continue; }
                    var loop = path.Select(q => new Point2d(q.x, q.y)).ToList();
                    loop.Reverse();
                    nodes.Add(loop);
                }
            }
            if (nodes.Count < 2 + holeCount) return null;
            for (int n = 0; n < nodes.Count; n++) nodes[n] = LoopRooted(nodes[n]);
            wall = nodes[0];
            var cum = nodes.Select(LoopCum).ToList();

            // ---- which loops lie one bead spacing apart, and where
            double step = 0.5 * spacing, reach = 1.3 * spacing;
            // a wave line rarely ends exactly one spacing before a hole wall: there the two may be
            // further apart, and the joint becomes a short double web between wave and hole wall
            double reachHole = readyLoops != null && holeCount > 0 ? 2.6 * spacing : reach, gridSize = Math.Max(reach, reachHole);
            int holes0 = holeCount;
            Func<int, int, double> reachOf = (na, nb) => (Math.Min(na, nb) >= 1 && Math.Min(na, nb) <= holes0 && Math.Max(na, nb) > holes0) ? reachHole : reach;
            var grid = new Dictionary<long, List<int>>();
            var sNode = new List<int>(); var sAt = new List<double>(); var sPt = new List<Point2d>(); var sDir = new List<Vector2d>();
            for (int n = 0; n < nodes.Count; n++)
            {
                double len = cum[n][nodes[n].Count];
                int count = Math.Max(3, (int)Math.Ceiling(len / step));
                for (int k = 0; k < count; k++)
                {
                    double s = len * k / count;
                    Point2d p = LoopAt(nodes[n], cum[n], s), q = LoopAt(nodes[n], cum[n], s + 0.25 * spacing);
                    var d = new Vector2d(q.X - p.X, q.Y - p.Y); double dl = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    if (dl < 1e-9) continue;
                    long key = ((long)Math.Floor(p.X / gridSize) << 32) ^ (uint)(int)Math.Floor(p.Y / gridSize);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(sPt.Count);
                    sNode.Add(n); sAt.Add(s); sPt.Add(p); sDir.Add(new Vector2d(d.X / dl, d.Y / dl));
                }
            }
            var joins = new Dictionary<long, CellJoin>();
            for (int a = 0; a < sPt.Count; a++)
            {
                int gx = (int)Math.Floor(sPt[a].X / gridSize), gy = (int)Math.Floor(sPt[a].Y / gridSize);
                int bestB = -1; double best = double.MaxValue;
                for (int ix = gx - 1; ix <= gx + 1; ix++)
                    for (int iy = gy - 1; iy <= gy + 1; iy++)
                    {
                        if (!grid.TryGetValue(((long)ix << 32) ^ (uint)iy, out var list)) continue;
                        foreach (int b in list)
                        {
                            if (sNode[b] <= sNode[a]) continue;
                            double d = sPt[a].DistanceTo(sPt[b]);
                            if (d < 0.7 * spacing || d > reachOf(sNode[a], sNode[b]) || d >= best) continue;
                            if (sDir[a].X * sDir[b].X + sDir[a].Y * sDir[b].Y > -0.8) continue; // must run in opposite directions
                            // square across: the connection is at right angles to both
                            double along = ((sPt[b].X - sPt[a].X) * sDir[a].X + (sPt[b].Y - sPt[a].Y) * sDir[a].Y);
                            if (Math.Abs(along) > 0.35 * spacing) continue;
                            best = d; bestB = b;
                        }
                    }
                if (bestB < 0) continue;
                long key = ((long)sNode[a] << 32) | (uint)sNode[bestB];
                if (!joins.TryGetValue(key, out var j)) joins[key] = j = new CellJoin { A = sNode[a], B = sNode[bestB] };
                j.At.Add(new[] { sAt[a], sAt[bestB] });
            }
            int joinedCells = 0;
            var byNode = new List<CellJoin>[nodes.Count];
            for (int n = 0; n < nodes.Count; n++) byNode[n] = new List<CellJoin>();
            foreach (var j in joins.Values)
            {
                if (j.At.Count < (readyLoops != null ? 2 : 3)) continue; // too short a stretch to open safely
                // try the middle of the shared stretch first, away from the corners
                // The shared stretch can be one run (two cells side by side) or many (a wave line
                // touches the wall between every two dips). Take the run nearest to the start of
                // the wall and open in its middle - a place given by the geometry, the same on
                // every layer.
                var at = j.At.OrderBy(x => x[0]).ToList();
                double lenA = cum[j.A][nodes[j.A].Count];
                var breaks = new List<int>(); // run starts
                for (int i = 0; i < at.Count; i++)
                {
                    double gapS = i == 0 ? at[0][0] + lenA - at[at.Count - 1][0] : at[i][0] - at[i - 1][0];
                    if (gapS > 2.5 * step) breaks.Add(i);
                }
                Point2d rootPt = nodes[0][0];
                Func<double[], Point2d> ptOf = x => LoopAt(nodes[j.A], cum[j.A], x[0]);
                List<double[]> bestRun = null; double bestD = double.MaxValue;
                if (breaks.Count == 0)
                {
                    // all the way round: start at the sample nearest to the wall's start
                    int i0 = 0; double d0 = double.MaxValue;
                    for (int i = 0; i < at.Count; i++) { double d = ptOf(at[i]).DistanceTo(rootPt); if (d < d0) { d0 = d; i0 = i; } }
                    bestRun = Enumerable.Range(0, at.Count).Select(i => at[(i0 + i + at.Count - at.Count / 2) % at.Count]).ToList();
                }
                else
                    for (int b = 0; b < breaks.Count; b++)
                    {
                        int from = breaks[b], to = breaks[(b + 1) % breaks.Count];
                        var run = new List<double[]>();
                        for (int i = from; ; i = (i + 1) % at.Count) { run.Add(at[i]); if ((i + 1) % at.Count == to) break; if (run.Count > at.Count) break; }
                        double d = ptOf(run[run.Count / 2]).DistanceTo(rootPt) - 1e-3 * run.Count;
                        if (run.Count >= 2 && d < bestD) { bestD = d; bestRun = run; }
                        else if (bestRun == null) bestRun = run;
                    }
                int mid = bestRun.Count / 2; Point2d midPt = ptOf(bestRun[mid]);
                var inRun = new HashSet<double[]>(bestRun);
                j.At = bestRun.Select((x, i) => new { x, k = Math.Abs(i - mid) }).OrderBy(x => x.k).Select(x => x.x)
                    .Concat(at.Where(x => !inRun.Contains(x)).OrderBy(x => ptOf(x).DistanceTo(midPt))).ToList();
                byNode[j.A].Add(j); byNode[j.B].Add(j);
            }

            // ---- spanning tree, joining as we go. The outer wall is opened as rarely as possible
            // (every opening shows on the outside of the print): one cell is joined to the wall,
            // all others are reached from cell to cell. Only cells that cannot be reached that
            // way get an opening of their own.
            var line = new List<Point2d>(wall);
            var done = new bool[nodes.Count]; done[0] = true;
            double gap = spacing;
            Point2d lastJoint = new Point2d(0, 0);
            Func<int, int, CellJoin, bool> tryJoin = (a, b, j) =>
            {
                double[] lc = LoopCum(line);
                foreach (var at in j.At)
                {
                    double sa = j.A == a ? at[0] : at[1], sb = j.A == a ? at[1] : at[0];
                    Point2d pa = LoopAt(nodes[a], cum[a], sa);
                    double s = LoopClosest(line, lc, pa, out double off);
                    if (off > 0.02) continue;
                    Point2d mMinus = LoopAt(line, lc, s - 0.5 * gap), mPlus = LoopAt(line, lc, s + 0.5 * gap);
                    // both ends of the opening must still be on the parent loop itself
                    LoopClosest(nodes[a], cum[a], mMinus, out double o1);
                    LoopClosest(nodes[a], cum[a], mPlus, out double o2);
                    if (o1 > 0.02 || o2 > 0.02) continue;
                    if (mMinus.DistanceTo(mPlus) < 0.9 * gap) continue; // opening runs around a corner
                    Point2d cPlus = LoopAt(nodes[b], cum[b], sb + 0.5 * gap), cMinus = LoopAt(nodes[b], cum[b], sb - 0.5 * gap);
                    double d1 = mMinus.DistanceTo(cPlus), d2 = mPlus.DistanceTo(cMinus);
                    double dMax = 1.15 * reachOf(a, b);
                    if (d1 < 0.6 * spacing || d1 > dMax || d2 < 0.6 * spacing || d2 > dMax) continue;
                    if (cPlus.DistanceTo(cMinus) < 0.9 * gap) continue;

                    var merged = LoopOpen(line, lc, s + 0.5 * gap, s - 0.5 * gap);
                    merged.AddRange(LoopOpen(nodes[b], cum[b], sb + 0.5 * gap, sb - 0.5 * gap));
                    line = merged;
                    lastJoint = pa;
                    return true;
                }
                return false;
            };
            // Walls of openings are joined last and opened once each: a line that runs through
            // an opening's wall to reach the cells behind it would open that wall again and
            // again, at places that move from layer to layer.
            bool holesLater = true;
            Action<int> grow = start =>
            {
                var queue = new Queue<int>(); queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();
                    foreach (var j in byNode[a].OrderBy(x => x.A == a ? x.B : x.A))
                    {
                        int b = j.A == a ? j.B : j.A;
                        if (done[b] || b == 0) continue;
                        if (holesLater && b <= holeCount) continue;
                        if (!tryJoin(a, b, j)) continue;
                        done[b] = true; queue.Enqueue(b);
                        if (b <= holeCount) absorbedHoles?.Add(b - 1); else joinedCells++;
                    }
                }
            };
            // openings in the wall: longest shared stretch first (a full cell, not a sliver)
            // A full cell before a sliver, and among those the one nearest to the start of the
            // wall: the same cell on every layer.
            // From the second layer on: where the layer below has its joints (prevJoints).
            Point2d root0 = nodes[0][0];
            bool useMem = prevJoints != null && prevJoints.Count > 0;
            Func<Point2d, double> mem = q => prevJoints.Min(m => m.DistanceTo(q));
            Func<CellJoin, Point2d> onWall = x => LoopAt(nodes[0], cum[0], x.At[0][0]);
            foreach (var j in useMem ? byNode[0].OrderBy(x => mem(onWall(x))).ToList()
                                     : byNode[0].OrderBy(x => x.At.Count >= 6 ? 0 : 1).ThenBy(x => onWall(x).DistanceTo(root0)).ToList())
            {
                int b = j.A == 0 ? j.B : j.A;
                if (done[b] || b <= holeCount) continue;
                // the same place as on the layer below, not just the same cell
                if (useMem) j.At = j.At.OrderBy(x => mem(LoopAt(nodes[0], cum[0], x[0]))).ToList();
                if (!tryJoin(0, b, j)) continue;
                joints?.Add(lastJoint);
                done[b] = true;
                joinedCells++;
                grow(b);
            }
            for (int h = 1; h <= holeCount; h++)
            {
                int hh = h;
                Func<CellJoin, Point2d> onHole = x => x.A == hh ? LoopAt(nodes[hh], cum[hh], x.At[0][0]) : LoopAt(nodes[hh], cum[hh], x.At[0][1]);
                foreach (var j in byNode[h].Where(x => done[x.A == hh ? x.B : x.A]).OrderBy(x => useMem ? mem(onHole(x)) : onHole(x).DistanceTo(nodes[hh][0])).ToList())
                {
                    if (useMem) { var jj = j; j.At = j.At.OrderBy(x => mem(LoopAt(nodes[hh], cum[hh], jj.A == hh ? x[0] : x[1]))).ToList(); }
                    if (!tryJoin(j.A == h ? j.B : j.A, h, j)) continue;
                    joints?.Add(LoopAt(nodes[hh], cum[hh], LoopClosest(nodes[hh], cum[hh], lastJoint, out _)));
                    done[h] = true; absorbedHoles?.Add(h - 1);
                    break;
                }
            }
            // what can only be reached through the wall of an opening
            holesLater = false;
            for (int h = 1; h <= holeCount; h++) if (done[h]) grow(h);
            joined = joinedCells;
            dropped += nodes.Count - 1 - holeCount - joined;

            // remove doubled points
            var clean = new List<Point2d>(line.Count);
            foreach (var p in line) if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(p) > 1e-6) clean.Add(p);
            if (clean.Count > 1 && clean[0].DistanceTo(clean[clean.Count - 1]) < 1e-6) clean.RemoveAt(clean.Count - 1);
            return joined > 0 ? clean : null;
        }

        /// <summary>
        /// Adds cell infill to every layer that has no bottom fill. The innermost outer wall of
        /// the layer and all cells become one closed contour.
        /// pattern: 0 = honeycomb, 1 = grid. size: distance between the middles of two neighbouring cells.
        /// </summary>
        public static List<SliceLayer> Cells(List<SliceLayer> layers, PrintSettings settings, int pattern, double size,
            double angleDeg, Point3d? origin, List<string> log, out int cellCount, double stretch = 1.0)
        {
            double spacing = settings.BeadSpacing;
            var result = new SliceLayer[layers.Count];
            var counts = new int[layers.Count]; var drops = new int[layers.Count]; var noHost = new int[layers.Count];
            Warp warp = layers.Count > 0 ? layers[0].Warp : null;
            Point3d o3 = origin ?? Point3d.Origin;
            if (warp != null && origin.HasValue) o3 = warp.ToFlat(o3);

            var memory = new List<Point3d>();
            Action<int> one = li =>
            {
                var src = layers[li];
                result[li] = src;
                if (src.ContourRoles != null && src.ContourRoles.Any(r => r == PathRole.Bottom)) return;

                int inner = src.Rings != null && src.Rings.Count > 0 ? src.Rings.Max() : 0;
                var rings = new List<List<Point2d>>(); var index = new List<int>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (!Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    if (src.Rings != null && c < src.Rings.Count && src.Rings[c] != inner) continue;
                    rings.Add(Ring2d(src.Plane, src.Contours[c])); index.Add(c);
                }
                // host = largest outer wall; holes = walls nested directly inside it
                int host = -1; double hostArea = 0;
                for (int a = 0; a < rings.Count; a++)
                {
                    int depth = 0;
                    for (int b = 0; b < rings.Count; b++) if (a != b && Inside(rings[b], rings[a][0])) depth++;
                    if ((depth & 1) != 0) continue;
                    double area = Math.Abs(Area(rings[a]));
                    if (area > hostArea) { hostArea = area; host = a; }
                }
                if (host < 0) { noHost[li] = 1; return; }
                if (Area(rings[host]) < 0) rings[host].Reverse();
                var material = new PathsD();
                material.Add(new PathD(rings[host].Select(p => new PointD(p.X, p.Y))));
                var holeWalls = new List<List<Point2d>>(); var holeIndex = new List<int>(); var absorbed = new List<int>();
                for (int b = 0; b < rings.Count; b++)
                {
                    if (b == host || !Inside(rings[host], rings[b][0])) continue;
                    var hole = new List<Point2d>(rings[b]);
                    if (Area(hole) > 0) hole.Reverse();
                    material.Add(new PathD(hole.Select(p => new PointD(p.X, p.Y))));
                    holeWalls.Add(hole); holeIndex.Add(index[b]);
                }

                var jointsNow = new List<Point2d>();
                var line = CellLine(rings[host], material, spacing, pattern, size, angleDeg, To2d(src.Plane, o3), out int joined, out int dropped, holeWalls, absorbed, stretch, null, null,
                    memory.Select(q => To2d(src.Plane, q)).ToList(), jointsNow);
                if (line != null && jointsNow.Count > 0) memory = jointsNow.Select(q => To3d(src.Plane, q)).ToList();
                drops[li] = dropped;
                if (line == null) { noHost[li] = 1; return; }
                counts[li] = joined;

                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Reference = src.Reference ?? src.Contours.Select(c => new Polyline(c)).ToList();
                dst.EntryHints = src.EntryHints;
                // hole walls that are now part of the line are not printed a second time
                var skip = new HashSet<int>(absorbed.Select(i => holeIndex[i]));
                if (src.Rings != null) dst.Rings = new List<int>();
                if (src.ContourRoles != null) dst.ContourRoles = new List<PathRole>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (skip.Contains(c)) continue;
                    dst.Contours.Add(c == index[host] ? Ring3d(src.Plane, line) : src.Contours[c]);
                    if (src.Rings != null) dst.Rings.Add(c < src.Rings.Count ? src.Rings[c] : 0);
                    if (src.ContourRoles != null) dst.ContourRoles.Add(c < src.ContourRoles.Count ? src.ContourRoles[c] : PathRole.Wall);
                }
                result[li] = dst;
            };
            foreach (int li in Enumerable.Range(0, layers.Count).OrderBy(i => layers[i].Index)) one(li);

            cellCount = counts.Length > 0 ? counts.Max() : 0;
            int without = noHost.Sum();
            if (without > 0) log?.Add($"{without} layer(s) got no cells (no closed wall, or too narrow for a cell).");
            int maxDrop = drops.Length > 0 ? drops.Max() : 0;
            if (maxDrop > 0) log?.Add($"Up to {maxDrop} cell piece(s) per layer are too small to print or could not be joined, and were left out.");
            return result.ToList();
        }

        /// <summary>
        /// Wave infill: one closed inner line that runs along the wall (one bead spacing inside,
        /// so the beads fuse) and dips into the part at regular intervals. Shape 0 = wave (smooth),
        /// 1 = truss (straight flanks), 2 = meander (steep flanks, long flat bottom).
        ///
        /// The pattern comes from thin-walled parts: a wave between two walls only stiffens the
        /// part where it touches both. So with meet = true every dip reaches as far as it can:
        /// up to the wall of a hole (one bead spacing before it), or to the middle of the part,
        /// where it meets the dip coming from the opposite side. Both end in a flat piece two
        /// bead widths long, one bead spacing apart, so they fuse. 'depth' is then the limit.
        ///
        /// Every straight side gets a whole number of equal waves, centred on the side, and
        /// opposite sides get the same number so their dips face each other. Corners stay clear.
        /// Round parts get a whole number of waves around the perimeter.
        /// </summary>
        internal static List<Point2d> WaveLoop(List<Point2d> wall, double spacing, double beadWidth, int shape, double wavelength, double depth, Point2d origin, out int waves)
        {
            return WaveLoop(wall, spacing, beadWidth, shape, wavelength, depth, origin, out waves, 0, 0, out _, out _, false, null);
        }

        /// <summary>prevDepth / prevCount: what the layer below used (0 = none); the wave may only change a little from layer to layer.</summary>
        internal static List<Point2d> WaveLoop(List<Point2d> wall, double spacing, double beadWidth, int shape, double wavelength, double depth, Point2d origin, out int waves,
            double prevDepth, int prevCount, out double usedDepth, out Point2d anchor, bool meet, List<List<Point2d>> otherWalls)
        {
            return WaveLoop(wall, spacing, beadWidth, shape, wavelength, depth, origin, out waves, prevDepth, prevCount, out usedDepth, out anchor, meet, otherWalls, new Vector2d(0, 0));
        }

        /// <summary>
        /// down: for tilted layers (angled printing) the direction in the layer that points to the
        /// bed; zero for flat layers. A side that runs up from the bed grows from layer to layer.
        /// Its waves keep their length and are counted from the bed, so each one stands on the wave
        /// of the layer before; new waves are added at the top. Spreading them evenly over the side
        /// would shift every wave whenever one more fits.
        /// </summary>
        internal static List<Point2d> WaveLoop(List<Point2d> wall, double spacing, double beadWidth, int shape, double wavelength, double depth, Point2d origin, out int waves,
            double prevDepth, int prevCount, out double usedDepth, out Point2d anchor, bool meet, List<List<Point2d>> otherWalls, Vector2d down)
        {
            waves = 0; usedDepth = 0; anchor = origin;
            if (Area(wall) < 0) { wall = new List<Point2d>(wall); wall.Reverse(); }
            double land = 2.0 * beadWidth;
            wavelength = Math.Max(wavelength, shape == 2 ? land + 6 * beadWidth : land + 4 * beadWidth);
            var wallPath = new PathsD { new PathD(wall.Select(q => new PointD(q.X, q.Y))) };
            PathsD crest = Clipper.InflatePaths(wallPath, -spacing, JoinType.Miter, EndType.Polygon, 2.0, 4);
            if (crest == null || crest.Count == 0) return null;
            var C = crest.OrderByDescending(q => Math.Abs(Clipper.Area(q))).First().Select(q => new Point2d(q.x, q.y)).ToList();
            if (Area(C) < 0) C.Reverse();
            var cc = new List<Point2d>();
            foreach (var q in C) if (cc.Count == 0 || cc[cc.Count - 1].DistanceTo(q) > 1e-6) cc.Add(q);
            if (cc.Count > 2 && cc[0].DistanceTo(cc[cc.Count - 1]) < 1e-6) cc.RemoveAt(cc.Count - 1);
            C = cc;
            if (C.Count < 3) return null;
            double[] cum = LoopCum(C);
            double len = cum[C.Count];
            double limit = depth > 0 ? depth : double.MaxValue;   // 0 = as deep as the part allows
            // Tilted layers have nothing below them where a layer first reaches further than the one
            // before: there a wave may only grow a little per layer, so every bead still lies on the last one.
            if (down.X != 0 || down.Y != 0) limit = Math.Min(limit, (prevDepth > 0 ? prevDepth : 2.0 * beadWidth) + 0.4 * beadWidth);
            if (!meet && depth <= 0) return null;

            var cornerKind = new List<int>();
            var cornerAt = LoopCorners(C, cum, 1.5 * beadWidth, cornerKind);

            // inward normal (left of travel on a counter-clockwise loop), smoothed over 'win'
            Func<double, double, double, double, Vector2d> normal = (sAt, win, lo, hi) =>
            {
                double s1 = Math.Max(lo, sAt - win), s2 = Math.Min(hi, sAt + win);
                Point2d q0 = LoopAt(C, cum, s1), q1 = LoopAt(C, cum, s2);
                double dx = q1.X - q0.X, dy = q1.Y - q0.Y, dl = Math.Sqrt(dx * dx + dy * dy);
                return dl < 1e-9 ? new Vector2d(0, 0) : new Vector2d(-dy / dl, dx / dl);
            };
            // How far can a dip go from the crest line at s? Half way to the crest line opposite
            // (the dip from there comes to meet it), or up to one bead spacing before a hole wall.
            // hitS: where on the crest line the ray arrived (-1 = hole wall or nothing).
            Func<double, Vector2d, double[], double> reach = (sAt, nv, hit) =>
            {
                Point2d p = LoopAt(C, cum, sAt);
                double best = double.MaxValue; hit[0] = -1;
                for (int i = 0; i < C.Count; i++)
                {
                    Point2d a = C[i], b = C[(i + 1) % C.Count];
                    double ex = b.X - a.X, ey = b.Y - a.Y, den = nv.X * ey - nv.Y * ex;
                    if (Math.Abs(den) < 1e-12) continue;
                    double t = ((a.X - p.X) * ey - (a.Y - p.Y) * ex) / den, u = ((a.X - p.X) * nv.Y - (a.Y - p.Y) * nv.X) / den;
                    if (t < 0.5 * spacing || u < 0 || u > 1) continue;
                    double d = 0.5 * (t - spacing);
                    if (d < best) { best = d; hit[0] = cum[i] + u * Math.Sqrt(ex * ex + ey * ey); }
                }
                if (otherWalls != null)
                    foreach (var ow in otherWalls)
                        for (int i = 0; i < ow.Count; i++)
                        {
                            Point2d a = ow[i], b = ow[(i + 1) % ow.Count];
                            double ex = b.X - a.X, ey = b.Y - a.Y, den = nv.X * ey - nv.Y * ex;
                            if (Math.Abs(den) < 1e-12) continue;
                            double t = ((a.X - p.X) * ey - (a.Y - p.Y) * ex) / den, u = ((a.X - p.X) * nv.Y - (a.Y - p.Y) * nv.X) / den;
                            if (t < 0.5 * spacing || u < 0 || u > 1) continue;
                            double d = t - spacing;
                            if (d < best) { best = d; hit[0] = -1; }
                        }
                return best;
            };

            // smooth parts: one depth for the whole loop
            double smoothDepth = Math.Min(limit, 1e6);
            if (cornerAt.Count == 0)
            {
                double winK = Math.Max(3 * beadWidth, 0.02 * len), maxK = 0, room = double.MaxValue;
                int probes = Math.Max(24, (int)(len / (2 * beadWidth)));
                var hitTmp = new double[1];
                for (int i = 0; i < probes; i++)
                {
                    double sAt = len * i / probes;
                    Point2d a0 = LoopAt(C, cum, sAt - winK), b0 = LoopAt(C, cum, sAt), c0 = LoopAt(C, cum, sAt + winK);
                    double ax = b0.X - a0.X, ay = b0.Y - a0.Y, bx = c0.X - b0.X, by = c0.Y - b0.Y;
                    double turn = Math.Atan2(ax * by - ay * bx, ax * bx + ay * by); // > 0: convex on a ccw loop
                    if (turn > 0) maxK = Math.Max(maxK, turn / winK);
                    room = Math.Min(room, reach(sAt, normal(sAt, winK, double.MinValue, double.MaxValue), hitTmp));
                }
                if (maxK > 1e-9) smoothDepth = Math.Min(smoothDepth, 0.7 / maxK);
                smoothDepth = Math.Min(smoothDepth, room);
                // a dip may move by less than half a bead between layers, or it loses its support
                if (prevDepth > 0) smoothDepth = Math.Max(prevDepth - 0.4 * beadWidth, Math.Min(prevDepth, smoothDepth));
            }

            // sides: depth and number of waves per side
            int runs = cornerAt.Count;
            var runFrom = new double[runs]; var runLen = new double[runs]; var runDepth = new double[runs]; var runOpp = new int[runs];
            if (runs > 0)
            {
                var hit = new double[1];
                for (int k = 0; k < runs; k++)
                {
                    double sa = cornerAt[k], sb = cornerAt[(k + 1) % runs];
                    if (sb <= sa + 1e-9) sb += len;
                    runFrom[k] = sa; runLen[k] = sb - sa; runOpp[k] = -1;
                    double d = limit, win = Math.Min(Math.Max(2.0, 3 * beadWidth), 0.5 * runLen[k]);
                    var votes = new Dictionary<int, int>();
                    int samples = Math.Max(3, (int)(runLen[k] / (2 * beadWidth)));
                    for (int i = 1; i < samples; i++)
                    {
                        double sAt = sa + runLen[k] * i / samples;
                        if (sAt - sa < land || sb - sAt < land) continue;
                        double r = reach(sAt, normal(sAt, win, sa, sb), hit);
                        if (r < d) d = r;
                        if (hit[0] >= 0)
                        {
                            double hs = hit[0] % len; int which = runs - 1;
                            for (int q = 0; q < runs; q++) { double f0 = cornerAt[q], f1 = cornerAt[(q + 1) % runs]; if (f1 <= f0) { if (hs >= f0 || hs < f1) { which = q; break; } } else if (hs >= f0 && hs < f1) { which = q; break; } }
                            votes[which] = votes.TryGetValue(which, out int v) ? v + 1 : 1;
                        }
                    }
                    runDepth[k] = meet ? d : Math.Min(limit, d);
                    if (votes.Count > 0) runOpp[k] = votes.OrderByDescending(v => v.Value).First().Key;
                }
            }

            // if the line does not work out, try again a little shallower - in small steps, so the
            // depth does not jump between two layers
            var scales = new[] { 1.0, 0.97, 0.94, 0.9, 0.85, 0.8, 0.7, 0.6, 0.5, 0.4 };
            for (int attempt = 0; attempt < scales.Length; attempt++)
            {
                double scale = scales[attempt];
                double step = Math.Max(0.5, 0.25 * beadWidth);
                var loop = new List<Point2d>();
                int count = 0;
                // one wave over u = 0..1: flat along the wall, down, flat inside, up
                Func<double, double, double> profile = (u, lam) =>
                {
                    double a = Math.Min(0.35, land / lam);
                    double b = shape == 2 ? Math.Max(0.2, 1.0 - a - 2.0 * Math.Max(0.07, 1.6 * beadWidth / lam)) : (shape == 1 ? Math.Min(0.15, beadWidth / lam) : 0.0);
                    if (meet) b = Math.Max(b, Math.Min(0.3, land / lam));   // a flat piece to fuse with what it meets
                    double t = 0.5 * (1.0 - a - b);
                    u -= Math.Floor(u);
                    double x;
                    if (u < 0.5 * a || u >= 1.0 - 0.5 * a) return 0;
                    if (u < 0.5 * a + t) x = (u - 0.5 * a) / t;
                    else if (u < 0.5 * a + t + b) return 1;
                    else x = (1.0 - 0.5 * a - u) / t;
                    return shape == 0 ? 0.5 - 0.5 * Math.Cos(Math.PI * x) : x;
                };

                if (runs == 0)
                {
                    double d = smoothDepth * scale;
                    if (d < 2 * beadWidth) return null;
                    count = (int)Math.Round(len / wavelength);
                    // keep the number of waves of the layer below as long as the wavelength stays sensible
                    if (prevCount >= 2 && len / prevCount > 0.6 * wavelength && len / prevCount < 1.6 * wavelength) count = prevCount;
                    if (count < 2) return null;
                    double lam = len / count, s0 = LoopClosest(C, cum, origin, out _);
                    anchor = LoopAt(C, cum, s0);
                    int n = Math.Max(24, (int)Math.Ceiling(len / step));
                    double win = Math.Max(2.0, 0.45 * d);
                    for (int i = 0; i < n; i++)
                    {
                        double sAt = s0 + len * i / n;
                        Point2d p = LoopAt(C, cum, sAt);
                        Vector2d nv = normal(sAt, win, double.MinValue, double.MaxValue);
                        double h = d * profile((sAt - s0) / lam, lam);
                        loop.Add(new Point2d(p.X + nv.X * h, p.Y + nv.Y * h));
                    }
                    usedDepth = d;
                }
                else
                {
                    // number of waves per side; sides that face each other get the same number
                    var nOf = new int[runs]; var ma = new double[runs]; var mb = new double[runs]; var dEff = new double[runs]; var slope = new int[runs];
                    for (int k = 0; k < runs; k++) { nOf[k] = -1; dEff[k] = runDepth[k] * scale; }
                    // Dips of two sides that share a convex corner would run into each other. With
                    // Meet the shallow sides go first (they are the ones that reach the middle) and
                    // a side next to them only gets dips that stay clear (long sides first); without Meet every side
                    // keeps away from its corners by the depth.
                    foreach (int k in Enumerable.Range(0, runs).OrderBy(i => meet ? -runLen[i] : 0).ThenBy(i => i))
                    {
                        int prevRun = (k + runs - 1) % runs, nextRun = (k + 1) % runs;
                        bool cornerA = cornerKind[k] > 0, cornerB = cornerKind[nextRun] > 0;
                        if (meet)
                        {
                            // a neighbour that already has dips limits how deep this side may go
                            if (cornerA && nOf[prevRun] > 0) dEff[k] = Math.Min(dEff[k], mb[prevRun] - land);
                            if (cornerB && nOf[nextRun] > 0) dEff[k] = Math.Min(dEff[k], ma[nextRun] - land);
                            ma[k] = land + (cornerA && nOf[prevRun] > 0 ? dEff[prevRun] : 0);
                            mb[k] = land + (cornerB && nOf[nextRun] > 0 ? dEff[nextRun] : 0);
                        }
                        else
                        {
                            ma[k] = land + (cornerA ? dEff[prevRun] : 0);
                            mb[k] = land + (cornerB ? dEff[nextRun] : 0);
                        }
                        double usable = runLen[k] - ma[k] - mb[k];
                        nOf[k] = dEff[k] >= 2 * beadWidth && usable >= 0.75 * wavelength ? Math.Max(1, (int)Math.Round(usable / wavelength)) : 0;
                        // a side that climbs from the bed: whole waves of fixed length, counted from the bed
                        slope[k] = 0;
                        if (down.X != 0 || down.Y != 0)
                        {
                            Point2d pa = LoopAt(C, cum, runFrom[k]), pb = LoopAt(C, cum, runFrom[k] + runLen[k]);
                            double fall = (pb.X - pa.X) * down.X + (pb.Y - pa.Y) * down.Y; // > 0: the end is lower than the start
                            if (Math.Abs(fall) > 0.3 * runLen[k]) slope[k] = fall > 0 ? 1 : -1;
                        }
                        if (slope[k] != 0) nOf[k] = dEff[k] >= 2 * beadWidth ? (int)Math.Floor(usable / wavelength + 1e-9) : 0;
                    }
                    for (int k = 0; k < runs; k++)
                    {
                        int o = runOpp[k];
                        if (o < 0 || o == k || runOpp[o] != k) continue;
                        if (slope[k] != 0 || slope[o] != 0) continue; // climbing sides keep their own count
                        int m = Math.Min(nOf[k], nOf[o]); nOf[k] = m; nOf[o] = m;
                    }
                    for (int k = 0; k < runs; k++)
                    {
                        double sa = runFrom[k], run = runLen[k], d = dEff[k];
                        int n = nOf[k];
                        double usable = run - ma[k] - mb[k], lam = n > 0 ? usable / n : 0;
                        double w0 = ma[k]; // where the first wave begins
                        if (slope[k] != 0 && n > 0)
                        {
                            lam = wavelength;
                            w0 = slope[k] < 0 ? ma[k] : run - mb[k] - n * lam; // start is on the bed, or the end is
                        }
                        double w1 = w0 + n * lam;
                        count += n;
                        usedDepth = Math.Max(usedDepth, n > 0 ? d : 0);
                        int steps = Math.Max(1, (int)Math.Ceiling(run / step));
                        double win = Math.Min(Math.Max(2.0, 0.45 * d), 0.5 * run);
                        // With Meet every dip goes as far as it can on its own: one over a hole stops
                        // at the hole, the others carry on to the middle of the part.
                        double[] own = null;
                        if (meet && n > 0)
                        {
                            own = new double[n];
                            var hit2 = new double[1];
                            double winN = Math.Min(Math.Max(2.0, 3 * beadWidth), 0.5 * run);
                            for (int j = 0; j < n; j++)
                            {
                                double best = double.MaxValue;
                                // over the whole foot of the dip, flanks included: a meander is
                                // nearly as wide at the bottom as at the top
                                double a0 = 0.5 * Math.Min(0.35, land / lam);
                                for (int q = 0; q <= 12; q++)
                                {
                                    double sAt = sa + w0 + (j + a0 + (1.0 - 2.0 * a0) * q / 12.0) * lam;
                                    best = Math.Min(best, reach(sAt, normal(sAt, winN, sa, sa + run), hit2));
                                }
                                own[j] = Math.Max(d, Math.Min(limit, best) * scale);
                                if (dEff[k] < runDepth[k] * scale - 1e-9) own[j] = d; // limited by a neighbouring side: keep that limit
                            }
                        }
                        for (int i = 0; i < steps; i++)
                        {
                            double dd = run * i / steps;
                            Point2d p = LoopAt(C, cum, sa + dd);
                            bool inWave = n > 0 && dd > w0 && dd < w1;
                            double dj = inWave && own != null ? own[Math.Max(0, Math.Min(n - 1, (int)((dd - w0) / lam)))] : d;
                            double h = inWave ? dj * profile((dd - w0) / lam, lam) : 0;
                            if (h <= 0) { loop.Add(p); continue; }
                            Vector2d nv = normal(sa + dd, win, sa, sa + run);
                            loop.Add(new Point2d(p.X + nv.X * h, p.Y + nv.Y * h));
                        }
                    }
                    if (count == 0) return null;
                }
                // thin out points on straight stretches
                var slim = new List<Point2d>();
                for (int i = 0; i < loop.Count; i++)
                {
                    if (slim.Count >= 2)
                    {
                        Point2d a0 = slim[slim.Count - 2], b0 = slim[slim.Count - 1], c0 = loop[i];
                        double dx = c0.X - a0.X, dy = c0.Y - a0.Y, l = Math.Sqrt(dx * dx + dy * dy);
                        if (l > 1e-9 && Math.Abs((b0.X - a0.X) * dy - (b0.Y - a0.Y) * dx) / l < 0.02 && l < 40) slim[slim.Count - 1] = c0; else slim.Add(c0);
                    }
                    else slim.Add(loop[i]);
                }
                if (slim.Count < 8) continue;
                var test = new Polyline(slim.Select(q => new Point3d(q.X, q.Y, 0))); test.Add(test[0]);
                if (PathTools.CountCrossings(test) > 0) continue;
                // never into an opening
                if (otherWalls != null && otherWalls.Any(ow => ow.Count > 2 && slim.Any(q => Inside(ow, q)))) continue;
                waves = count;
                return slim;
            }
            return null;
        }

        /// <summary>Adds wave infill to every layer without bottom fill; the wave becomes part of the wall contour.</summary>
        public static List<SliceLayer> Waves(List<SliceLayer> layers, PrintSettings settings, int shape, double wavelength,
            double depth, Point3d? origin, List<string> log, out int waveCount, bool meet = true)
        {
            double spacing = settings.BeadSpacing, w = settings.BeadWidth;
            var result = new SliceLayer[layers.Count];
            var counts = new int[layers.Count]; var failed = new int[layers.Count];
            Warp warp = layers.Count > 0 ? layers[0].Warp : null;
            Point3d o3 = origin ?? Point3d.Unset;
            if (warp != null && origin.HasValue) o3 = warp.ToFlat(o3);

            // layer by layer: every wave is placed where the wave of the layer below is
            double prevDepth = 0; int prevCount = 0; Point3d prevAnchor = Point3d.Unset;
            var waveMemory = new List<Point3d>();
            foreach (int li in Enumerable.Range(0, layers.Count).OrderBy(i => layers[i].Index))
            {
                var src = layers[li];
                result[li] = src;
                if (src.ContourRoles != null && src.ContourRoles.Any(r => r == PathRole.Bottom)) continue;
                int inner = src.Rings != null && src.Rings.Count > 0 ? src.Rings.Max() : 0;
                int host = -1; double hostArea = 0; List<Point2d> hostRing = null;
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (!Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    if (src.Rings != null && c < src.Rings.Count && src.Rings[c] != inner) continue;
                    var ring = Ring2d(src.Plane, src.Contours[c]);
                    double area = Math.Abs(Area(ring));
                    if (area > hostArea) { hostArea = area; host = c; hostRing = ring; }
                }
                if (host < 0) { failed[li] = 1; continue; }
                if (Area(hostRing) < 0) hostRing.Reverse();
                // without an origin the pattern starts at the right-most wall point of the first layer's frame
                Point2d o2 = prevAnchor.IsValid ? To2d(src.Plane, prevAnchor) : (o3.IsValid ? To2d(src.Plane, o3) : new Point2d(1e6, 0));
                // walls of holes: a dip may run up to them
                var holes = new List<List<Point2d>>(); var holeIndex = new List<int>(); var absorbed = new List<int>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (c == host || !Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    var hr = Ring2d(src.Plane, src.Contours[c]);
                    if (hr.Count > 2 && Inside(hostRing, hr[0])) { holes.Add(hr); holeIndex.Add(c); }
                }
                // tilted layer: which way is down, seen in the layer?
                Vector3d fallLine = -Vector3d.ZAxis - src.Plane.Normal * (-Vector3d.ZAxis * src.Plane.Normal);
                Vector2d down = new Vector2d(0, 0);
                if (src.Warp == null && fallLine.Length > 0.05) { fallLine.Unitize(); down = new Vector2d(fallLine * src.Plane.XAxis, fallLine * src.Plane.YAxis); }
                var wave = WaveLoop(hostRing, spacing, w, shape, wavelength, depth, o2, out int n, prevDepth, prevCount, out double usedDepth, out Point2d anchor, meet, holes, down);
                if (wave == null) { failed[li] = 1; continue; }

                var material = new PathsD { new PathD(hostRing.Select(q => new PointD(q.X, q.Y))) };
                var jointsNow = new List<Point2d>();
                var line = CellLine(hostRing, material, spacing, 0, 0, 0, o2, out int joined, out _, holes, absorbed, 1.0, new List<List<Point2d>> { wave }, null,
                    waveMemory.Select(q => To2d(src.Plane, q)).ToList(), jointsNow);
                if (line != null && jointsNow.Count > 0) waveMemory = jointsNow.Select(q => To3d(src.Plane, q)).ToList();
                if (line == null || joined == 0) { failed[li] = 2; continue; }
                counts[li] = n;
                prevDepth = usedDepth; prevCount = n; prevAnchor = To3d(src.Plane, anchor);

                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Reference = src.Reference ?? src.Contours.Select(c => new Polyline(c)).ToList();
                dst.EntryHints = src.EntryHints;
                // walls of holes that a dip reaches are part of the line now: no travel move to them
                var skip = new HashSet<int>(absorbed.Select(i => holeIndex[i]));
                if (src.Rings != null) dst.Rings = new List<int>();
                if (src.ContourRoles != null) dst.ContourRoles = new List<PathRole>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (skip.Contains(c)) continue;
                    dst.Contours.Add(c == host ? Ring3d(src.Plane, line) : src.Contours[c]);
                    if (src.Rings != null) dst.Rings.Add(c < src.Rings.Count ? src.Rings[c] : 0);
                    if (src.ContourRoles != null) dst.ContourRoles.Add(c < src.ContourRoles.Count ? src.ContourRoles[c] : PathRole.Wall);
                }
                result[li] = dst;
            }

            waveCount = counts.Length > 0 ? counts.Max() : 0;
            int noFit = failed.Count(f => f == 1), noJoin = failed.Count(f => f == 2);
            if (noFit > 0) log?.Add($"{noFit} layer(s) got no wave: no closed wall, or the part is too narrow for this depth.");
            if (noJoin > 0) log?.Add($"{noJoin} layer(s) got no wave: it could not be joined to the wall.");
            return result.ToList();
        }

        /// <summary>
        /// Chamber wall: a band of closed chambers along the inside of the wall, the classic
        /// double wall with webs. shape 0 = boxes (webs square to the wall), 1 = truss (every box
        /// is split by a diagonal that changes direction from box to box). Every chamber is a
        /// loop; neighbouring loops fuse and all are joined into the wall path (see CellLine).
        /// </summary>
        public static List<SliceLayer> Chambers(List<SliceLayer> layers, PrintSettings settings, int shape, double length, double depth,
            List<string> log, out int chamberCount)
        {
            double spacing = settings.BeadSpacing, w = settings.BeadWidth;
            length = Math.Max(length, 5 * spacing); depth = Math.Max(depth, 4 * spacing);
            var result = new SliceLayer[layers.Count];
            var counts = new int[layers.Count]; var failed = new int[layers.Count];

            Point3d prevStation = Point3d.Unset; int prevN = 0; double prevD = 0;
            var chamberMemory = new List<Point3d>();
            List<Point3d> prevStations = null; var prevIsCorner = new List<bool>();
            foreach (int li in Enumerable.Range(0, layers.Count).OrderBy(i => layers[i].Index))
            {
                var src = layers[li];
                result[li] = src;
                if (src.ContourRoles != null && src.ContourRoles.Any(r => r == PathRole.Bottom)) continue;
                int inner = src.Rings != null && src.Rings.Count > 0 ? src.Rings.Max() : 0;
                int host = -1; double hostArea = 0; List<Point2d> ring = null;
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (!Geo.Closed(src.Contours[c]) || src.Contours[c].Count < 4) continue;
                    if (src.Rings != null && c < src.Rings.Count && src.Rings[c] != inner) continue;
                    var r2 = Ring2d(src.Plane, src.Contours[c]);
                    double area = Math.Abs(Area(r2));
                    if (area > hostArea) { hostArea = area; host = c; ring = r2; }
                }
                if (host < 0) { failed[li] = 1; continue; }
                if (Area(ring) < 0) ring.Reverse();
                var material = new PathsD { new PathD(ring.Select(q => new PointD(q.X, q.Y))) };
                // the band must leave room in the middle of the part
                double d = depth;
                for (int t = 0; t < 6; t++, d *= 0.8)
                {
                    var room = Clipper.InflatePaths(material, -(d + spacing), JoinType.Round, EndType.Polygon, 2.0, 4);
                    if (room != null && room.Count > 0) break;
                }
                if (d < 4 * spacing) { failed[li] = 1; continue; }
                if (prevD > 0) d = Math.Max(prevD - 0.4 * w, Math.Min(prevD + 0.4 * w, d));

                double[] cum = LoopCum(ring);
                double len = cum[ring.Count];
                // stations: every corner, and evenly in between
                var stations = new List<double>();
                var corners = LoopCorners(ring, cum, 1.5 * w, null);
                bool smooth = corners.Count == 0;
                var isCorner = new List<bool>();
                // a round part has no corner to start from: start where the layer below started
                if (smooth) corners.Add(prevStation.IsValid ? LoopClosest(ring, cum, To2d(src.Plane, prevStation), out _) : 0);
                for (int k = 0; k < corners.Count; k++)
                {
                    double sa = corners[k], sb = corners[(k + 1) % corners.Count];
                    if (sb <= sa + 1e-9) sb += len;
                    int n = Math.Max(1, (int)Math.Round((sb - sa) / length));
                    if (smooth && prevN > 0 && len / prevN > 0.6 * length && len / prevN < 1.6 * length) n = prevN;
                    if (smooth) prevN = n;
                    for (int j = 0; j < n; j++) { stations.Add(sa + (sb - sa) * j / n); isCorner.Add(j == 0 && !smooth); }
                }
                // Stand on the chambers of the layer below: if every station of that layer has a place
                // on this wall close by, take those places. The webs then follow a twisting or
                // tapering part instead of being laid out afresh.
                if (prevStations != null && prevStations.Count >= 3)
                {
                    var mapped = new List<double>(); bool ok = true;
                    foreach (var ps in prevStations)
                    {
                        double sm = LoopClosest(ring, cum, To2d(src.Plane, ps), out double dist);
                        if (dist > 1.5 * w) { ok = false; break; }
                        mapped.Add(sm);
                    }
                    if (ok)
                    {
                        // must still run once around in the same order
                        int wraps = 0; for (int q = 0; q < mapped.Count; q++) if (mapped[(q + 1) % mapped.Count] < mapped[q]) wraps++;
                        if (wraps == 1) { stations = mapped; isCorner = prevIsCorner.Count == mapped.Count ? new List<bool>(prevIsCorner) : mapped.Select(_ => false).ToList(); }
                    }
                }
                prevStations = stations.Select(sv => To3d(src.Plane, LoopAt(ring, cum, sv))).ToList(); prevIsCorner = new List<bool>(isCorner);
                prevStation = To3d(src.Plane, LoopAt(ring, cum, stations[0]));
                // inner point of a station: along the inward bisector, so neighbouring chambers share the web
                double reach = d + 0.5 * spacing;
                Func<double, Point2d> innerOf = sAt =>
                {
                    Point2d p = LoopAt(ring, cum, sAt), a = LoopAt(ring, cum, sAt - 0.5), b = LoopAt(ring, cum, sAt + 0.5);
                    double ax = p.X - a.X, ay = p.Y - a.Y, bx = b.X - p.X, by = b.Y - p.Y;
                    double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                    if (la < 1e-9 || lb < 1e-9) return p;
                    double nx = -(ay / la + by / lb), ny = ax / la + bx / lb, nl = Math.Sqrt(nx * nx + ny * ny);
                    if (nl < 1e-9) return p;
                    nx /= nl; ny /= nl;
                    double cosHalf = Math.Max(0.35, (-(ay / la) * nx + (ax / la) * ny));
                    return new Point2d(p.X + nx * reach / cosHalf, p.Y + ny * reach / cosHalf);
                };
                var cells = new List<PathD>();
                for (int k = 0; k < stations.Count; k++)
                {
                    double sa = stations[k], sb = stations[(k + 1) % stations.Count];
                    if (sb <= sa + 1e-9) sb += len;
                    var outer = new List<Point2d> { LoopAt(ring, cum, sa) };
                    for (int i = 0; i < 2 * ring.Count; i++)
                    {
                        double sv = cum[i % ring.Count] + (i / ring.Count) * len;
                        if (sv > sa + 1e-6 && sv < sb - 1e-6) outer.Add(ring[i % ring.Count]);
                    }
                    outer.Add(LoopAt(ring, cum, sb));
                    Point2d qa = innerOf(sa), qb = innerOf(sb);
                    if (shape == 1 && outer.Count == 2 && !isCorner[k] && !isCorner[(k + 1) % stations.Count])
                    {
                        // truss: split the box by a diagonal that alternates
                        if ((k & 1) == 0)
                        {
                            cells.Add(new PathD { new PointD(outer[0].X, outer[0].Y), new PointD(outer[1].X, outer[1].Y), new PointD(qb.X, qb.Y) });
                            cells.Add(new PathD { new PointD(outer[0].X, outer[0].Y), new PointD(qb.X, qb.Y), new PointD(qa.X, qa.Y) });
                        }
                        else
                        {
                            cells.Add(new PathD { new PointD(outer[0].X, outer[0].Y), new PointD(outer[1].X, outer[1].Y), new PointD(qa.X, qa.Y) });
                            cells.Add(new PathD { new PointD(outer[1].X, outer[1].Y), new PointD(qb.X, qb.Y), new PointD(qa.X, qa.Y) });
                        }
                        continue;
                    }
                    var poly = new PathD(outer.Select(q => new PointD(q.X, q.Y)));
                    poly.Add(new PointD(qb.X, qb.Y));
                    // follow a curved wall on the inside as well
                    for (int i = outer.Count - 2; i >= 1; i--)
                    {
                        Point2d q = innerOf(LoopClosest(ring, cum, outer[i], out _));
                        poly.Add(new PointD(q.X, q.Y));
                    }
                    poly.Add(new PointD(qa.X, qa.Y));
                    cells.Add(poly);
                }
                var taken = new PathsD();
                var clean = new List<PathD>();
                foreach (var cell in cells)
                {
                    var one = new PathsD { cell };
                    if (Clipper.Area(cell) < 0) one[0].Reverse();
                    PathsD rest = taken.Count == 0 ? one : Clipper.Difference(one, taken, FillRule.NonZero, 4);
                    if (rest != null) foreach (var piece in rest) if (Clipper.Area(piece) > 0) clean.Add(piece);
                    taken = taken.Count == 0 ? one : Clipper.Union(taken, one, FillRule.NonZero, 4);
                }
                var jointsNow = new List<Point2d>();
                var line = CellLine(ring, material, spacing, 0, 0, 0, new Point2d(0, 0), out int joined, out _, null, null, 1.0, null, clean,
                    chamberMemory.Select(q => To2d(src.Plane, q)).ToList(), jointsNow);
                if (line != null && jointsNow.Count > 0) chamberMemory = jointsNow.Select(q => To3d(src.Plane, q)).ToList();
                if (line == null || joined == 0) { failed[li] = 1; continue; }
                counts[li] = joined; prevD = d;

                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Reference = src.Reference ?? src.Contours.Select(c => new Polyline(c)).ToList();
                dst.EntryHints = src.EntryHints;
                var skip = new HashSet<int>();
                if (src.Rings != null) dst.Rings = new List<int>();
                if (src.ContourRoles != null) dst.ContourRoles = new List<PathRole>();
                for (int c = 0; c < src.Contours.Count; c++)
                {
                    if (skip.Contains(c)) continue;
                    dst.Contours.Add(c == host ? Ring3d(src.Plane, line) : src.Contours[c]);
                    if (src.Rings != null) dst.Rings.Add(c < src.Rings.Count ? src.Rings[c] : 0);
                    if (src.ContourRoles != null) dst.ContourRoles.Add(c < src.ContourRoles.Count ? src.ContourRoles[c] : PathRole.Wall);
                }
                result[li] = dst;
            }

            chamberCount = counts.Length > 0 ? counts.Max() : 0;
            int without = failed.Sum();
            if (without > 0) log?.Add($"{without} layer(s) got no chambers (no closed wall, or the part is too narrow for this depth).");
            return result.ToList();
        }

        /// <summary>Wall plus cell infill of one flat region as a single closed curve (Toolpaths tab).</summary>
        public static RegionFillResult FillCells(Curve boundary, IEnumerable<Curve> holes, double width, int pattern, double size, double angleDeg, Point3d? origin)
        {
            var result = new RegionFillResult();
            if (boundary == null || width <= 0) return result;
            Plane plane;
            if (!boundary.TryGetPlane(out plane, 0.1)) plane = new Plane(boundary.PointAtStart, Vector3d.ZAxis);
            if (plane.Normal.Z < 0) plane = new Plane(plane.Origin, plane.XAxis, -plane.YAxis);
            var outer = CurveRing(boundary, plane, width);
            if (outer == null) { result.Info = "The boundary could not be used."; return result; }
            if (Area(outer) < 0) outer.Reverse();
            var paths = new PathsD { new PathD(outer.Select(p => new PointD(p.X, p.Y))) };
            if (holes != null)
                foreach (var h in holes)
                {
                    var r = CurveRing(h, plane, width);
                    if (r == null) continue;
                    if (Area(r) > 0) r.Reverse();
                    paths.Add(new PathD(r.Select(p => new PointD(p.X, p.Y))));
                }
            PathsD first = Clipper.InflatePaths(paths, -0.5 * width, JoinType.Round, EndType.Polygon, 2.0, 4);
            if (first == null || first.Count == 0) { result.Info = "The region is narrower than one bead width."; return result; }
            PathD biggest = first.Where(p => Clipper.Area(p) > 0).OrderByDescending(p => Clipper.Area(p)).FirstOrDefault();
            if (biggest == null) { result.Info = "The region is narrower than one bead width."; return result; }
            var wall = biggest.Select(q => new Point2d(q.x, q.y)).ToList();
            // material = that wall loop and the hole loops inside it
            var material = new PathsD { biggest };
            var holeWalls = new List<List<Point2d>>(); var absorbed = new List<int>();
            foreach (var p in first)
                if (Clipper.Area(p) < 0 && Inside(wall, new Point2d(p[0].x, p[0].y))) { material.Add(p); holeWalls.Add(p.Select(q => new Point2d(q.x, q.y)).ToList()); }

            var line = CellLine(wall, material, width, pattern, size, angleDeg, To2d(plane, origin ?? plane.Origin), out int joined, out int dropped, holeWalls, absorbed);
            if (line == null) { result.Info = "No cell fits into the region. Reduce the cell size."; return result; }
            result.Path = Ring3d(plane, line);
            result.Info = $"{joined} cell loop(s) joined to the wall in one closed line, {result.Path.Length / 1000.0:0.00} m" +
                          (dropped > 0 ? $", {dropped} piece(s) too small and left out" : "") +
                          (holeWalls.Count > 0 ? $", {absorbed.Count} of {holeWalls.Count} hole wall(s) are part of the line" : "");
            return result;
        }
    }
}
