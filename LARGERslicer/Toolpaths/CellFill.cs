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

        private class CellJoin { public int A, B; public List<double[]> At = new List<double[]>(); } // sA, sB

        /// <summary>
        /// One closed line: the wall with all cell loops joined to it.
        /// wall: centre line of the wall bead, counter-clockwise. material: the area inside the
        /// wall centre line (holes cut out). origin: fixed point of the pattern, so the cells
        /// stand exactly above each other from layer to layer.
        /// </summary>
        internal static List<Point2d> CellLine(List<Point2d> wall, PathsD material, double spacing, int pattern,
            double size, double angleDeg, Point2d origin, out int joined, out int dropped,
            List<List<Point2d>> holeWalls = null, List<int> absorbedHoles = null, double stretch = 1.0, List<List<Point2d>> readyLoops = null)
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
            if (readyLoops != null) { }
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
                    if (Area(loop) > 0) loop.Reverse();
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
            var cum = nodes.Select(LoopCum).ToList();

            // ---- which loops lie one bead spacing apart, and where
            double step = 0.5 * spacing, reach = 1.3 * spacing, gridSize = reach;
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
                            if (d < 0.7 * spacing || d > reach || d >= best) continue;
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
                j.At = j.At.OrderBy(x => x[0]).ToList();
                int mid = j.At.Count / 2;
                j.At = j.At.Select((x, i) => new { x, k = Math.Abs(i - mid) }).OrderBy(x => x.k).Select(x => x.x).ToList();
                byNode[j.A].Add(j); byNode[j.B].Add(j);
            }

            // ---- spanning tree, joining as we go. The outer wall is opened as rarely as possible
            // (every opening shows on the outside of the print): one cell is joined to the wall,
            // all others are reached from cell to cell. Only cells that cannot be reached that
            // way get an opening of their own.
            var line = new List<Point2d>(wall);
            var done = new bool[nodes.Count]; done[0] = true;
            double gap = spacing;
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
                    if (d1 < 0.6 * spacing || d1 > 1.5 * spacing || d2 < 0.6 * spacing || d2 > 1.5 * spacing) continue;
                    if (cPlus.DistanceTo(cMinus) < 0.9 * gap) continue;

                    var merged = LoopOpen(line, lc, s + 0.5 * gap, s - 0.5 * gap);
                    merged.AddRange(LoopOpen(nodes[b], cum[b], sb + 0.5 * gap, sb - 0.5 * gap));
                    line = merged;
                    return true;
                }
                return false;
            };
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
                        if (!tryJoin(a, b, j)) continue;
                        done[b] = true; queue.Enqueue(b);
                        if (b <= holeCount) absorbedHoles?.Add(b - 1); else joinedCells++;
                    }
                }
            };
            // openings in the wall: longest shared stretch first (a full cell, not a sliver)
            foreach (var j in byNode[0].OrderByDescending(x => x.At.Count))
            {
                int b = j.A == 0 ? j.B : j.A;
                if (done[b]) continue;
                if (!tryJoin(0, b, j)) continue;
                done[b] = true;
                if (b <= holeCount) absorbedHoles?.Add(b - 1); else joinedCells++;
                grow(b);
            }
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

            Parallel.For(0, layers.Count, li =>
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

                var line = CellLine(rings[host], material, spacing, pattern, size, angleDeg, To2d(src.Plane, o3), out int joined, out int dropped, holeWalls, absorbed, stretch);
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
            });

            cellCount = counts.Length > 0 ? counts.Max() : 0;
            int without = noHost.Sum();
            if (without > 0) log?.Add($"{without} layer(s) got no cells (no closed wall, or too narrow for a cell).");
            int maxDrop = drops.Length > 0 ? drops.Max() : 0;
            if (maxDrop > 0) log?.Add($"Up to {maxDrop} cell piece(s) per layer are too small to print or could not be joined, and were left out.");
            return result.ToList();
        }

        /// <summary>
        /// Wave infill: one closed inner line that swings between the wall and a depth inside the
        /// part. At every crest it runs along the wall for a short "land" (one bead spacing away,
        /// so the beads fuse); in between it dips inwards. Shape 0 = wave (smooth), 1 = truss
        /// (straight flanks), 2 = meander (steep flanks, long lands). The line is joined to the
        /// wall by one pair of bridges, so the layer stays one closed contour.
        /// The pattern is measured along the wall from the wall point nearest to 'origin', which
        /// keeps the crests above each other from layer to layer.
        /// </summary>
        internal static List<Point2d> WaveLoop(List<Point2d> wall, double spacing, double beadWidth, int shape, double wavelength, double depth, Point2d origin, out int waves)
        {
            waves = 0;
            if (Area(wall) < 0) { wall = new List<Point2d>(wall); wall.Reverse(); }
            double land = 2.0 * beadWidth;
            wavelength = Math.Max(wavelength, shape == 2 ? 2 * land + 4 * beadWidth : 2 * land + 2 * beadWidth);
            for (int attempt = 0; attempt < 5; attempt++, depth *= 0.7)
            {
                double amp = 0.5 * depth;
                if (amp < beadWidth) return null;
                // base line: the wall moved inwards so that crests end one bead spacing from it
                var path = new PathD(wall.Select(q => new PointD(q.X, q.Y)));
                // The base line gets round corners (shrink further, then grow back): the wave swings
                // 'amp' to both sides of it, so no bend of the base may be tighter than that.
                double round = 1.4 * amp;
                PathsD off = Clipper.InflatePaths(new PathsD { path }, -(spacing + amp + round), JoinType.Round, EndType.Polygon, 2.0, 4);
                if (off == null || off.Count == 0) continue;
                off = Clipper.InflatePaths(off, round, JoinType.Round, EndType.Polygon, 2.0, 4);
                if (off == null || off.Count == 0) continue;
                PathD biggest = off.OrderByDescending(q => Math.Abs(Clipper.Area(q))).First();
                var baseLoop = biggest.Select(q => new Point2d(q.x, q.y)).ToList();
                if (Area(baseLoop) < 0) baseLoop.Reverse();
                double[] bc = LoopCum(baseLoop);
                double len = bc[baseLoop.Count];
                int count = (int)Math.Round(len / wavelength);
                if (count < 2) continue;
                double lam = len / count;
                double s0 = LoopClosest(baseLoop, bc, origin, out _);

                double a = Math.Min(0.35, land / lam);                 // share of a wave that lies along the wall
                double t = shape == 2 ? Math.Min(0.5 - a, Math.Max(0.06, 1.5 * beadWidth / lam)) : 0.5 - a;
                double b = 1.0 - a - 2 * t;                              // share along the inside
                Func<double, double> profile = u =>
                {
                    u -= Math.Floor(u);
                    if (u < a) return 1;
                    if (u < a + t) { double x = (u - a) / t; return shape == 0 ? Math.Cos(Math.PI * x) : 1 - 2 * x; }
                    if (u < a + t + b) return -1;
                    double y = (u - a - t - b) / t; return shape == 0 ? -Math.Cos(Math.PI * y) : -1 + 2 * y;
                };
                int steps = Math.Max(24, (int)Math.Ceiling(len / Math.Max(1.0, 0.25 * beadWidth)));
                var loop = new List<Point2d>(steps);
                for (int i = 0; i < steps; i++)
                {
                    double sAt = s0 + len * i / steps;
                    // direction over a wide window: the rounded corners of the base are made of short
                    // straight pieces, and a normal that jumps at each of them would fold the wave
                    double win = Math.Max(2.0, 0.45 * amp);
                    Point2d p = LoopAt(baseLoop, bc, sAt), q0 = LoopAt(baseLoop, bc, sAt - win), q = LoopAt(baseLoop, bc, sAt + win);
                    double dx = q.X - q0.X, dy = q.Y - q0.Y, dl = Math.Sqrt(dx * dx + dy * dy);
                    if (dl < 1e-9) continue;
                    // outward normal of a counter-clockwise loop: to the right of the travel direction
                    double nx = dy / dl, ny = -dx / dl;
                    double h = amp * profile((sAt - s0) / lam + 0.5 * a);
                    loop.Add(new Point2d(p.X + nx * h, p.Y + ny * h));
                }
                if (loop.Count < 12) continue;
                var test = new Polyline(loop.Select(q => new Point3d(q.X, q.Y, 0))); test.Add(test[0]);
                if (PathTools.CountCrossings(test) > 0) continue;       // flanks collide in a tight corner: try less depth
                waves = count;
                return loop;
            }
            return null;
        }

        /// <summary>Adds wave infill to every layer without bottom fill; the wave becomes part of the wall contour.</summary>
        public static List<SliceLayer> Waves(List<SliceLayer> layers, PrintSettings settings, int shape, double wavelength,
            double depth, Point3d? origin, List<string> log, out int waveCount)
        {
            double spacing = settings.BeadSpacing, w = settings.BeadWidth;
            var result = new SliceLayer[layers.Count];
            var counts = new int[layers.Count]; var failed = new int[layers.Count];
            Warp warp = layers.Count > 0 ? layers[0].Warp : null;
            Point3d o3 = origin ?? Point3d.Unset;
            if (warp != null && origin.HasValue) o3 = warp.ToFlat(o3);

            Parallel.For(0, layers.Count, li =>
            {
                var src = layers[li];
                result[li] = src;
                if (src.ContourRoles != null && src.ContourRoles.Any(r => r == PathRole.Bottom)) return;
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
                if (host < 0) { failed[li] = 1; return; }
                if (Area(hostRing) < 0) hostRing.Reverse();
                // without an origin the pattern starts at the right-most wall point of the first layer's frame
                Point2d o2 = o3.IsValid ? To2d(src.Plane, o3) : new Point2d(1e6, 0);
                var wave = WaveLoop(hostRing, spacing, w, shape, wavelength, depth, o2, out int n);
                if (wave == null) { failed[li] = 1; return; }

                var material = new PathsD { new PathD(hostRing.Select(q => new PointD(q.X, q.Y))) };
                var line = CellLine(hostRing, material, spacing, 0, 0, 0, o2, out int joined, out _, null, null, 1.0, new List<List<Point2d>> { wave });
                if (line == null || joined == 0) { failed[li] = 2; return; }
                counts[li] = n;

                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Rings = src.Rings != null ? new List<int>(src.Rings) : null;
                dst.Reference = src.Reference ?? src.Contours.Select(c => new Polyline(c)).ToList();
                dst.ContourRoles = src.ContourRoles;
                dst.EntryHints = src.EntryHints;
                dst.Contours.AddRange(src.Contours);
                dst.Contours[host] = Ring3d(src.Plane, line);
                result[li] = dst;
            });

            waveCount = counts.Length > 0 ? counts.Max() : 0;
            int noFit = failed.Count(f => f == 1), noJoin = failed.Count(f => f == 2);
            if (noFit > 0) log?.Add($"{noFit} layer(s) got no wave: no closed wall, or the part is too narrow for this depth.");
            if (noJoin > 0) log?.Add($"{noJoin} layer(s) got no wave: it could not be joined to the wall.");
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
