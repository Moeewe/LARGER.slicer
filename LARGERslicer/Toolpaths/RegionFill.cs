using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>Result of filling one flat region with a single print line.</summary>
    public class RegionFillResult
    {
        public Polyline Path;
        /// <summary>Straight links that had to join separate pieces of the fill (0 = one clean line).</summary>
        public List<Line> Links = new List<Line>();
        public string Info = "";
        /// <summary>Set when the fill had to deviate from what was asked for.</summary>
        public string Note = "";
    }

    public static partial class FillEngine
    {
        /// <summary>
        /// Fills one flat region (boundary curve minus holes) with a single print line, for the
        /// curve-based components of the Toolpaths tab. The curve is the outside of the print:
        /// the first bead lies half a bead width inside it.
        /// contour = true:  the line follows the outline inwards (connected Fermat spiral); holes are walked around.
        /// contour = false: zigzag at the given angle; with wall = true one loop along the outline is printed first.
        /// </summary>
        public static RegionFillResult FillRegion(Curve boundary, IEnumerable<Curve> holes, double width, bool contour,
            double angleDeg, bool wall, double extraInset, Point3d? start)
        {
            var result = new RegionFillResult();
            if (boundary == null || width <= 0) return result;

            Plane plane;
            if (!boundary.TryGetPlane(out plane, 0.1)) plane = new Plane(boundary.PointAtStart, Vector3d.ZAxis);
            if (plane.Normal.Z < 0) plane = new Plane(plane.Origin, plane.XAxis, -plane.YAxis);

            var rings = new List<List<Point2d>>();
            var outer = CurveRing(boundary, plane, width);
            if (outer == null) { result.Info = "The boundary could not be used."; return result; }
            if (Area(outer) < 0) outer.Reverse();
            rings.Add(outer);
            if (holes != null)
                foreach (var h in holes)
                {
                    var r = CurveRing(h, plane, width);
                    if (r == null) continue;
                    if (Area(r) > 0) r.Reverse();
                    rings.Add(r);
                }

            var paths = new PathsD(rings.Count);
            foreach (var r in rings)
            {
                var p = new PathD(r.Count);
                foreach (var q in r) p.Add(new PointD(q.X, q.Y));
                paths.Add(p);
            }

            // centre line of the outermost bead
            PathsD first = Clipper.InflatePaths(paths, -(0.5 * width + Math.Max(0.0, extraInset)), JoinType.Round, EndType.Polygon, 2.0, 4);
            if (first == null || first.Count == 0) { result.Info = "The region is narrower than one bead width."; return result; }

            Point2d? startNear = start.HasValue ? (Point2d?)To2d(plane, start.Value) : null;
            List<List<Point2d>> pieces;
            List<Point2d> wallLoop = null;

            string note = "";
            pieces = null;
            if (!contour)
            {
                PathsD region = first;
                if (wall)
                {
                    // the largest loop is the wall; the zigzag fills what is left inside it
                    PathD biggest = first.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();
                    wallLoop = biggest.Select(q => new Point2d(q.x, q.y)).ToList();
                    region = Clipper.InflatePaths(first, -width, JoinType.Miter, EndType.Polygon, 2.0, 4);
                }
                pieces = region != null && region.Count > 0 ? Zigzag(region, width, angleDeg, false) : new List<List<Point2d>>();
                if (pieces.Count(p => p != null && p.Count >= 2) > 1)
                {
                    // A zigzag only stays one line if every fill line crosses the shape once. With
                    // bays or holes it would fall into pieces joined across open space, so the
                    // line follows the outline instead: never cross is the first rule.
                    contour = true; wallLoop = null; pieces = null;
                    note = "This shape cannot be filled by one zigzag line at this angle, so the line follows the outline instead. ";
                }
            }
            if (contour)
            {
                pieces = FermatFill.Build(first, width, startNear, new List<Point2d>());
                if (startNear.HasValue && pieces.Count > 0 && pieces.Any(pc => pc.Count > 3 && PathTools.CountCrossings(new Polyline(pc.Select(q => new Point3d(q.X, q.Y, 0)))) > 0))
                {
                    pieces = FermatFill.Build(first, width, null, new List<Point2d>());
                    note += "The start point would have made the line cross itself; the start was chosen automatically. ";
                }
            }
            pieces = pieces.Where(p => p != null && p.Count >= 2).ToList();
            if (pieces.Count == 0 && wallLoop == null) { result.Info = "No fill fits into the region."; return result; }

            // one line: pieces in nearest-end order, joined by straight links
            var line = new List<Point2d>();
            if (wallLoop != null)
            {
                Point2d target = pieces.Count > 0 ? pieces[0][0] : (startNear ?? wallLoop[0]);
                if (startNear.HasValue && pieces.Count == 0) target = startNear.Value;
                int at = 0; double best = double.MaxValue;
                for (int i = 0; i < wallLoop.Count; i++)
                {
                    double d = wallLoop[i].DistanceTo(target);
                    if (d < best) { best = d; at = i; }
                }
                for (int i = 0; i <= wallLoop.Count; i++) line.Add(wallLoop[(at + i) % wallLoop.Count]);
            }
            var left = new List<List<Point2d>>(pieces);
            Point2d cursor = line.Count > 0 ? line[line.Count - 1] : (startNear ?? (left.Count > 0 ? left[0][0] : new Point2d(0, 0)));
            bool firstPiece = line.Count == 0;
            while (left.Count > 0)
            {
                int pick = 0; bool flip = false; double best = double.MaxValue;
                for (int i = 0; i < left.Count; i++)
                {
                    double d0 = left[i][0].DistanceTo(cursor), d1 = left[i][left[i].Count - 1].DistanceTo(cursor);
                    if (d0 < best) { best = d0; pick = i; flip = false; }
                    if (d1 < best) { best = d1; pick = i; flip = true; }
                }
                var piece = left[pick];
                left.RemoveAt(pick);
                if (flip) piece.Reverse();
                // a link longer than one bead step runs across open space: report it
                if (!firstPiece && best > 1.6 * width)
                    result.Links.Add(new Line(To3d(plane, cursor), To3d(plane, piece[0])));
                line.AddRange(piece);
                cursor = piece[piece.Count - 1];
                firstPiece = false;
            }

            result.Path = new Polyline(line.Select(p => To3d(plane, p)));
            result.Path.DeleteShortSegments(1e-6);
            double length = result.Path.Length;
            result.Note = note.Trim();
            result.Info = note + Inv.F($"{(contour ? "Contour" : "Zigzag")} fill, bead {width:0.##} mm, {length / 1000.0:0.00} m") +
                          (result.Links.Count > 0 ? Inv.F($", {result.Links.Count} link(s) across open space") : ", one continuous line");
            return result;
        }

        private static List<Point2d> CurveRing(Curve crv, Plane plane, double width)
        {
            if (crv == null) return null;
            Polyline pl;
            if (!crv.TryGetPolyline(out pl))
            {
                var pc = crv.ToPolyline(0.02, Math.PI / 90.0, 0, Math.Max(width * 4.0, 10.0));
                if (pc == null || !pc.TryGetPolyline(out pl)) return null;
            }
            var ring = new List<Point2d>(pl.Count);
            foreach (Point3d p in pl) ring.Add(To2d(plane, p));
            if (ring.Count > 1 && ring[0].DistanceTo(ring[ring.Count - 1]) < 1e-6) ring.RemoveAt(ring.Count - 1);
            return ring.Count >= 3 ? ring : null;
        }
    }

    /// <summary>Repairs for print lines drawn by hand or joined from pieces.</summary>
    public static class PathTools
    {
        /// <summary>
        /// Removes self-crossings from a polyline without cutting it: wherever two segments cross,
        /// the stretch between them is run in the opposite direction (a 2-opt move). The line
        /// stays one line, visits the same points and gets shorter with every move.
        /// </summary>
        public static Polyline Uncross(Polyline pl, double tolerance, out int moves, out List<Point3d> crossings, int maxMoves = 2000)
        {
            moves = 0;
            crossings = new List<Point3d>();
            var p = new List<Point3d>(pl);
            bool closed = p.Count > 2 && p[0].DistanceTo(p[p.Count - 1]) < Math.Max(tolerance, 1e-9);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool found = true;
            while (found && moves < maxMoves && watch.ElapsedMilliseconds < 4000)
            {
                found = false;
                int n = p.Count - 1; // number of segments
                for (int i = 0; i < n - 1 && !found; i++)
                {
                    Point3d a = p[i], b = p[i + 1];
                    double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
                    for (int j = i + 2; j < n; j++)
                    {
                        if (closed && i == 0 && j == n - 1) continue; // neighbours across the closing point
                        Point3d c = p[j], d = p[j + 1];
                        if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX || Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY) continue;
                        Point3d x;
                        if (!Cross(a, b, c, d, out x)) continue;
                        if (crossings.Count < 5000) crossings.Add(x);
                        p.Reverse(i + 1, j - i);
                        moves++;
                        found = true;
                        break;
                    }
                }
            }
            return new Polyline(p);
        }

        /// <summary>Counts proper crossings of a polyline with itself (in plan view).</summary>
        public static int CountCrossings(Polyline pl)
        {
            int count = 0, n = pl.Count - 1;
            bool closed = pl.Count > 2 && pl[0].DistanceTo(pl[pl.Count - 1]) < 1e-6;
            for (int i = 0; i < n - 1; i++)
            {
                Point3d a = pl[i], b = pl[i + 1];
                double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
                for (int j = i + 2; j < n; j++)
                {
                    if (closed && i == 0 && j == n - 1) continue;
                    Point3d c = pl[j], d = pl[j + 1];
                    if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX || Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY) continue;
                    if (Cross(a, b, c, d, out _)) count++;
                }
            }
            return count;
        }

        private static bool Cross(Point3d a, Point3d b, Point3d c, Point3d d, out Point3d x)
        {
            x = Point3d.Unset;
            double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;
            double t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
            double u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
            const double e = 1e-9;
            if (t <= e || t >= 1 - e || u <= e || u >= 1 - e) return false;
            x = a + (b - a) * t;
            return true;
        }
    }
}
