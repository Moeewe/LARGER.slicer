using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for generating Contour-Zigzag Hybrid toolpaths.
    /// Based on: Bi et al., 2022 - Continuous Contour-zigzag Hybrid Toolpath for Large Format Additive Manufacturing
    /// Combines smooth contour offsets with locally generated zigzag fill patterns to eliminate unfilled pockets.
    /// </summary>
    public static class ContourZigzagHelper
    {
        /// <summary>
        /// Preprocesses the input curve for reliable offsetting.
        /// Projects to XY plane, joins if necessary, converts to NurbsCurve, rebuilds for uniform control points, and forces closed.
        /// </summary>
        public static Curve PreprocessCurve(Curve crv, double tol)
        {
            if (crv == null)
                return null;

            try
            {
                // Project to XY plane
                Curve cProj = Curve.ProjectToPlane(crv, Plane.WorldXY);
                if (cProj == null)
                    cProj = crv.DuplicateCurve();

                // Join segments if polycurve
                Curve[] joined = Curve.JoinCurves(new[] { cProj }, tol);
                Curve c = (joined != null && joined.Length == 1) ? joined[0] : cProj;

                // Convert to NurbsCurve
                NurbsCurve cNurbs = c.ToNurbsCurve();
                if (cNurbs == null)
                    cNurbs = c as NurbsCurve ?? c.ToNurbsCurve();

                // Rebuild for uniform control point spacing
                double length = cNurbs.GetLength();
                int pointCount = Math.Max(50, (int)(length / 5.0));

                Curve cReb = cNurbs.Rebuild(pointCount, 3, false);
                Curve cClean = (cReb != null && cReb.IsValid) ? cReb : cNurbs;

                // Force closed
                if (!cClean.IsClosed)
                {
                    cClean = cClean.DuplicateCurve();
                    cClean.MakeClosed(tol);
                }

                return cClean;
            }
            catch
            {
                return crv;
            }
        }

        /// <summary>
        /// Generates a list of inward offset curves spaced by the bead width.
        /// Offsetting stops when the next offset would produce a curve whose length is smaller than the bead width.
        /// </summary>
        public static List<Curve> GenerateInwardOffsets(Curve crv, double w, double tol)
        {
            var offsets = new List<Curve>();

            if (crv == null || !crv.IsClosed)
                return offsets;

            Curve current = crv.DuplicateCurve();
            offsets.Add(current);

            while (true)
            {
                // Perform inward offset by -w
                try
                {
                    var res = current.Offset(Plane.WorldXY, -w, tol, CurveOffsetCornerStyle.Round);
                    if (res == null || res.Length == 0)
                        break;

                    Curve off = res[0];
                    if (off == null || !off.IsValid)
                        break;

                    // Terminate when offset becomes too small
                    if (off.GetLength() < w)
                        break;

                    offsets.Add(off);
                    current = off;
                }
                catch
                {
                    break;
                }
            }

            return offsets;
        }

        /// <summary>
        /// Computes a 2D bounding box for a list of curves.
        /// </summary>
        private static (double minX, double maxX, double minY, double maxY) BoundingBoxXY(List<Curve> curves)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var c in curves)
            {
                if (c == null || !c.IsValid)
                    continue;

                var ts = c.DivideByCount(50, true);
                foreach (var t in ts)
                {
                    Point3d p = c.PointAt(t);
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }

            return (minX, maxX, minY, maxY);
        }

        /// <summary>
        /// Generates a horizontal zigzag pattern between two curves.
        /// Uses horizontal sweep lines at bead spacing to create segments spanning the region between contours.
        /// Consecutive segments are reversed to form a serpentine path.
        /// </summary>
        public static Polyline GenerateZigzagBetween(Curve curveOuter, Curve curveInner, double w, double tol)
        {
            var pts = new List<Point3d>();

            if (curveOuter == null || curveInner == null || !curveOuter.IsValid || !curveInner.IsValid)
                return new Polyline(pts);

            // Determine bounding box
            var (minX, maxX, minY, maxY) = BoundingBoxXY(new List<Curve> { curveOuter, curveInner });

            // Expand slightly to ensure sweep lines intersect
            double margin = w;
            minX -= margin;
            maxX += margin;

            // Determine number of sweep lines
            int nLines = (int)((maxY - minY) / w) + 1;

            for (int i = 0; i < nLines; i++)
            {
                double y = minY + i * w;

                // Construct horizontal line across the box
                Line line = new Line(new Point3d(minX, y, 0.0), new Point3d(maxX, y, 0.0));
                Curve lnCrv = line.ToNurbsCurve();

                // Find intersections with both curves
                var intersections = new List<Point3d>();

                foreach (var c in new[] { curveOuter, curveInner })
                {
                    try
                    {
                        var xdata = Rhino.Geometry.Intersect.Intersection.CurveCurve(c, lnCrv, tol, tol);
                        if (xdata != null)
                        {
                            foreach (var ev in xdata)
                            {
                                intersections.Add(ev.PointA);
                            }
                        }
                    }
                    catch
                    {
                        // Skip if intersection fails
                    }
                }

                // Sort intersection points along x
                intersections = intersections.OrderBy(p => p.X).ToList();

                // Form segments between pairs of points
                for (int j = 0; j < intersections.Count - 1; j += 2)
                {
                    if (j + 1 >= intersections.Count)
                        break;

                    Point3d pStart = intersections[j];
                    Point3d pEnd = intersections[j + 1];

                    // Alternate reversal for zigzag behavior
                    if (i % 2 == 0)
                    {
                        pts.Add(pStart);
                        pts.Add(pEnd);
                    }
                    else
                    {
                        pts.Add(pEnd);
                        pts.Add(pStart);
                    }
                }
            }

            // Remove duplicate consecutive points
            var simplified = new List<Point3d>();
            foreach (var p in pts)
            {
                if (simplified.Count == 0 || p.DistanceTo(simplified[simplified.Count - 1]) > tol)
                {
                    simplified.Add(p);
                }
            }

            return new Polyline(simplified);
        }

        /// <summary>
        /// Generates contour offsets and zigzag fills for the input curve.
        /// Returns contours (outer to inner) and zigzag polylines (one for each inter-contour region).
        /// </summary>
        public static (List<Curve> contours, List<Polyline> zigzags) GenerateHybridToolpath(
            Curve crv, double w, double tol)
        {
            var contours = new List<Curve>();
            var zigzags = new List<Polyline>();

            if (crv == null)
                return (contours, zigzags);

            // Preprocess curve
            Curve clean = PreprocessCurve(crv, tol);
            if (clean == null || !clean.IsValid)
                return (contours, zigzags);

            // Generate inward offsets
            contours = GenerateInwardOffsets(clean, w, tol);

            // Create zigzag patterns between each pair of contours
            for (int i = 0; i < contours.Count - 1; i++)
            {
                Curve outer = contours[i];
                Curve inner = contours[i + 1];

                if (outer != null && inner != null && outer.IsValid && inner.IsValid)
                {
                    Polyline zig = GenerateZigzagBetween(outer, inner, w, tol);
                    if (zig != null && zig.Count >= 2)
                    {
                        zigzags.Add(zig);
                    }
                }
            }

            return (contours, zigzags);
        }

        /// <summary>
        /// Connects contours and zigzags into a single continuous path using DFS-based reordering.
        /// Based on the paper's layer-wise connection algorithm.
        /// </summary>
        public static List<Point3d> ConnectHybridPath(
            List<Curve> contours,
            List<Polyline> zigzags,
            Point3d startPoint,
            double spacing)
        {
            var pathPoints = new List<Point3d>();

            if (contours == null || contours.Count == 0)
                return pathPoints;

            // Combine all path segments (contours and zigzags)
            var allSegments = new List<List<Point3d>>();

            // Add contours as point lists
            foreach (var contour in contours)
            {
                if (contour != null && contour.IsValid)
                {
                    var contourPoints = PathHelper.SampleCurve(contour, spacing * 0.3, true);
                    if (contourPoints.Count >= 2)
                    {
                        allSegments.Add(contourPoints);
                    }
                }
            }

            // Add zigzags as point lists
            foreach (var zigzag in zigzags)
            {
                if (zigzag != null && zigzag.Count >= 2)
                {
                    var zigzagPoints = zigzag.ToList();
                    allSegments.Add(zigzagPoints);
                }
            }

            if (allSegments.Count == 0)
                return pathPoints;

            // Use DFS-based ordering (nearest neighbor with depth-first approach)
            var orderedSegments = new List<List<Point3d>>();
            var remainingSegments = new List<List<Point3d>>(allSegments);
            Point3d currentPos = startPoint;

            while (remainingSegments.Count > 0)
            {
                // Find nearest segment
                double minDist = double.MaxValue;
                int nearestIdx = -1;
                bool reverseNearest = false;

                for (int i = 0; i < remainingSegments.Count; i++)
                {
                    if (remainingSegments[i] == null || remainingSegments[i].Count == 0)
                        continue;

                    Point3d segStart = remainingSegments[i][0];
                    Point3d segEnd = remainingSegments[i][remainingSegments[i].Count - 1];

                    double distStart = currentPos.DistanceTo(segStart);
                    double distEnd = currentPos.DistanceTo(segEnd);

                    if (distStart < minDist)
                    {
                        minDist = distStart;
                        nearestIdx = i;
                        reverseNearest = false;
                    }
                    if (distEnd < minDist)
                    {
                        minDist = distEnd;
                        nearestIdx = i;
                        reverseNearest = true;
                    }
                }

                if (nearestIdx >= 0)
                {
                    var segment = remainingSegments[nearestIdx];
                    if (reverseNearest)
                    {
                        segment.Reverse();
                    }

                    // Add connection if needed
                    if (pathPoints.Count > 0 && segment.Count > 0)
                    {
                        Point3d lastPt = pathPoints[pathPoints.Count - 1];
                        Point3d firstPt = segment[0];
                        double dist = lastPt.DistanceTo(firstPt);

                        if (dist > spacing * 0.1)
                        {
                            // Create short connection
                            int steps = Math.Max(2, (int)Math.Ceiling(dist / (spacing * 0.5)));
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                            }
                        }
                    }

                    pathPoints.AddRange(segment);
                    orderedSegments.Add(segment);
                    remainingSegments.RemoveAt(nearestIdx);
                    currentPos = segment[segment.Count - 1];
                }
                else
                {
                    // Fallback: add remaining segments
                    foreach (var seg in remainingSegments)
                    {
                        if (seg != null && seg.Count > 0)
                        {
                            pathPoints.AddRange(seg);
                        }
                    }
                    break;
                }
            }

            return pathPoints;
        }
    }
}

