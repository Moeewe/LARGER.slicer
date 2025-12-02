using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for path generation utilities used in bottom layer fill patterns.
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Determines if a curve should be filled inward (outer boundary) or is a hole/island.
        /// Returns true if curve should be filled inward (normal case for outer boundaries).
        /// </summary>
        public static bool ShouldFillInward(Curve curve, List<Curve> otherCurves = null, double tolerance = 0.01)
        {
            if (curve == null || !curve.IsValid || !curve.IsClosed)
                return true; // Default: assume outer boundary

            try
            {
                // Method 1: Check curve orientation (area sign)
                // Counterclockwise curves have positive area (outer boundary)
                // Clockwise curves have negative area (holes)
                var areaProps = AreaMassProperties.Compute(curve);
                if (areaProps != null)
                {
                    double area = areaProps.Area;
                    // Positive area = counterclockwise = outer boundary = fill inward
                    // Negative area = clockwise = hole = don't fill (or fill outward)
                    if (Math.Abs(area) > tolerance * tolerance)
                    {
                        return area > 0; // Positive area means fill inward
                    }
                }

                // Method 2: If area is too small or ambiguous, check relative position
                if (otherCurves != null && otherCurves.Count > 0)
                {
                    Point3d center = curve.GetBoundingBox(true).Center;
                    
                    // Count how many other curves contain this curve's center
                    int containingCount = 0;
                    foreach (var other in otherCurves)
                    {
                        if (other != null && other.IsValid && other.IsClosed)
                        {
                            PointContainment containment = other.Contains(center, Plane.WorldXY, tolerance);
                            if (containment == PointContainment.Inside)
                            {
                                containingCount++;
                            }
                        }
                    }
                    
                    // If this curve is inside another curve, it's likely a hole (don't fill inward)
                    // If this curve contains others or is not inside any, it's likely an outer boundary (fill inward)
                    return containingCount == 0;
                }

                // Default: assume outer boundary (fill inward)
                return true;
            }
            catch
            {
                // On error, default to filling inward
                return true;
            }
        }

        /// <summary>
        /// Determines the correct offset direction for a curve based on its orientation.
        /// Returns negative value for inward offset (outer boundaries) or positive for outward (holes).
        /// </summary>
        public static double GetOffsetDirection(Curve curve, List<Curve> otherCurves = null, double tolerance = 0.01)
        {
            bool fillInward = ShouldFillInward(curve, otherCurves, tolerance);
            return fillInward ? -1.0 : 1.0; // Negative = inward, Positive = outward
        }

        /// <summary>
        /// Generates offset curves inward from a boundary curve.
        /// Automatically detects curve orientation and offsets in the correct direction.
        /// </summary>
        /// <param name="boundary">Boundary curve to offset</param>
        /// <param name="spacing">Offset distance</param>
        /// <param name="maxOffsets">Maximum number of offsets to generate</param>
        /// <param name="otherCurves">Other curves to help determine orientation (e.g., holes or other boundaries)</param>
        /// <returns>List of offset curves (outermost to innermost)</returns>
        public static List<Curve> GenerateOffsetCurves(Curve boundary, double spacing, int maxOffsets = 100, List<Curve> otherCurves = null)
        {
            var offsets = new List<Curve>();
            Curve current = boundary.DuplicateCurve();
            double previousArea = double.MaxValue;

            // Determine offset direction based on curve orientation
            double offsetDirection = GetOffsetDirection(boundary, otherCurves);
            double offsetDistance = spacing * offsetDirection;

            for (int i = 0; i < maxOffsets; i++)
            {
                var offsetCurves = current.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                
                if (offsetCurves == null || offsetCurves.Length == 0)
                    break;

                // Filter valid curves and calculate areas safely
                var validCurves = new List<(Curve curve, double area)>();
                foreach (var c in offsetCurves)
                {
                    if (c != null && c.IsValid)
                    {
                        var areaProp = AreaMassProperties.Compute(c);
                        if (areaProp != null)
                        {
                            validCurves.Add((c, areaProp.Area));
                        }
                    }
                }

                if (validCurves.Count == 0)
                    break;

                // Use the largest offset curve
                var largest = validCurves.OrderByDescending(x => x.area).First();
                
                // Check if offset is still valid and has reasonable area
                if (largest.area < spacing * spacing) // Too small to continue
                    break;

                // Safety check: area should decrease with each offset
                if (largest.area >= previousArea * 0.99) // Allow 1% tolerance
                    break; // Prevent infinite loop

                offsets.Add(largest.curve);
                current = largest.curve;
                previousArea = largest.area;
            }

            return offsets;
        }

        /// <summary>
        /// Finds the closest point on a curve to a given point.
        /// </summary>
        public static Point3d ClosestPointOnCurve(Curve curve, Point3d point)
        {
            double t;
            curve.ClosestPoint(point, out t);
            return curve.PointAt(t);
        }

        /// <summary>
        /// Finds the closest point on a curve to a seam point and returns the parameter.
        /// Used to determine connection positions for path continuity.
        /// </summary>
        public static (Point3d point, double parameter) FindSeamPosition(Curve curve, Point3d seamPoint)
        {
            double t;
            curve.ClosestPoint(seamPoint, out t);
            return (curve.PointAt(t), t);
        }

        /// <summary>
        /// Connects two curves with a smooth transition, ensuring continuity.
        /// </summary>
        public static List<Point3d> ConnectCurves(Curve curve1, Curve curve2, Point3d startPoint, Point3d endPoint, int steps = 10)
        {
            var connection = new List<Point3d>();

            // Find closest points on curves
            Point3d pt1 = ClosestPointOnCurve(curve1, startPoint);
            Point3d pt2 = ClosestPointOnCurve(curve2, endPoint);

            // Create smooth connection
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Point3d pt = pt1 + (pt2 - pt1) * t;
                connection.Add(pt);
            }

            return connection;
        }

        /// <summary>
        /// Samples points along a curve with specified spacing.
        /// </summary>
        public static List<Point3d> SampleCurve(Curve curve, double spacing, bool includeEnds = true)
        {
            var points = new List<Point3d>();
            
            if (curve == null || !curve.IsValid)
                return points;

            double length = curve.GetLength();
            int numPoints = Math.Max(2, (int)Math.Ceiling(length / spacing) + 1);

            if (includeEnds && numPoints > 0)
            {
                points.Add(curve.PointAtStart);
            }

            for (int i = 1; i < numPoints - 1; i++)
            {
                double t;
                curve.NormalizedLengthParameter((double)i / (numPoints - 1), out t);
                points.Add(curve.PointAt(t));
            }

            if (includeEnds && numPoints > 1)
            {
                points.Add(curve.PointAtEnd);
            }

            return points;
        }

        /// <summary>
        /// Generates parallel lines within a boundary, rotated around the center point.
        /// Lines are spaced by the extrusion width (layer width), so each line represents one extrusion pass.
        /// The spacing between line centers equals the layer width, ensuring proper fill coverage.
        /// </summary>
        /// <param name="boundary">Boundary curve</param>
        /// <param name="spacing">Line spacing (extrusion width/layer width in mm). Distance between line centers.</param>
        /// <param name="angle">Rotation angle in radians (around center point)</param>
        /// <param name="centerPoint">Center point for rotation (typically boundary center)</param>
        /// <param name="bbox">Bounding box for reference</param>
        public static List<Curve> GenerateParallelLines(Curve boundary, double spacing, double angle, Point3d centerPoint, BoundingBox bbox)
        {
            var lines = new List<Curve>();
            
            // Calculate line direction (rotated around center)
            Vector3d direction = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            Vector3d perp = new Vector3d(-direction.Y, direction.X, 0);

            // Calculate bounds perpendicular to line direction
            // Sample boundary points and measure distance from center along perpendicular direction
            double minDist = double.MaxValue;
            double maxDist = double.MinValue;

            var boundaryPoints = SampleCurve(boundary, spacing * 0.5);
            foreach (var pt in boundaryPoints)
            {
                // Distance from center along perpendicular direction
                double dist = Vector3d.Multiply(pt - centerPoint, perp);
                minDist = Math.Min(minDist, dist);
                maxDist = Math.Max(maxDist, dist);
            }

            // Generate lines perpendicular to direction, spaced by layer width (spacing)
            // Each line represents one extrusion pass, so spacing = layer width between line centers
            int numLines = (int)Math.Ceiling((maxDist - minDist) / spacing) + 1;
            for (int i = 0; i < numLines; i++)
            {
                // Position line center at i * spacing from minDist
                // This ensures layer width spacing between adjacent lines
                double dist = minDist + i * spacing;
                
                // Create line through center point, offset by dist along perpendicular direction
                Point3d lineCenter = centerPoint + perp * dist;
                
                // Extend line in both directions (long enough to intersect boundary)
                double lineLength = bbox.Diagonal.Length * 1.5;
                Point3d lineStart = lineCenter - direction * lineLength;
                Point3d lineEnd = lineCenter + direction * lineLength;
                
                Line line = new Line(lineStart, lineEnd);
                Curve lineCurve = new LineCurve(line);

                // Trim line to boundary
                var intersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(lineCurve, boundary, 0.01, 0.01);
                if (intersections != null && intersections.Count > 0)
                {
                    // Get intersection parameters and create trimmed curve
                    var intersectionParams = intersections.Select(ix => ix.ParameterA).OrderBy(t => t).ToList();
                    if (intersectionParams.Count >= 2)
                    {
                        double t1 = intersectionParams[0];
                        double t2 = intersectionParams[intersectionParams.Count - 1];
                        Curve trimmed = lineCurve.Trim(t1, t2);
                        if (trimmed != null && trimmed.IsValid)
                        {
                            lines.Add(trimmed);
                        }
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// Optimizes connection order between multiple curves to minimize travel distance.
        /// Ensures each curve is visited exactly once (no duplicates).
        /// </summary>
        public static List<Curve> OptimizeCurveOrder(List<Curve> curves, Point3d startPoint, Point3d endPoint)
        {
            if (curves == null || curves.Count == 0)
                return new List<Curve>();

            if (curves.Count == 1)
                return new List<Curve> { curves[0].DuplicateCurve() };

            // Performance optimization: limit greedy search for very large sets
            if (curves.Count > 200)
            {
                // For large sets, use simpler heuristic
                return curves.Select(c => c.DuplicateCurve()).ToList();
            }

            var ordered = new List<Curve>(curves.Count);
            var remaining = new List<Curve>(curves);
            Point3d currentPoint = startPoint;
            int maxIterations = curves.Count * 2; // Safety limit
            int iterations = 0;

            while (remaining.Count > 0 && iterations < maxIterations)
            {
                iterations++;
                int bestIndex = -1;
                double bestDistance = double.MaxValue;
                bool reverseBest = false;

                // Find nearest curve
                for (int i = 0; i < remaining.Count; i++)
                {
                    var curve = remaining[i];
                    if (curve == null || !curve.IsValid)
                        continue;

                    Point3d start = curve.PointAtStart;
                    Point3d end = curve.PointAtEnd;

                    double distToStart = currentPoint.DistanceToSquared(start); // Faster than DistanceTo
                    double distToEnd = currentPoint.DistanceToSquared(end);

                    if (distToStart < bestDistance)
                    {
                        bestDistance = distToStart;
                        bestIndex = i;
                        reverseBest = false;
                    }

                    if (distToEnd < bestDistance)
                    {
                        bestDistance = distToEnd;
                        bestIndex = i;
                        reverseBest = true;
                    }
                }

                if (bestIndex >= 0 && bestIndex < remaining.Count)
                {
                    Curve curveToAdd = remaining[bestIndex].DuplicateCurve();
                    if (reverseBest)
                    {
                        curveToAdd.Reverse();
                    }
                    ordered.Add(curveToAdd);
                    remaining.RemoveAt(bestIndex); // O(n) but unavoidable
                    currentPoint = curveToAdd.PointAtEnd;
                }
                else
                {
                    // Safety: add remaining curves
                    foreach (var curve in remaining)
                    {
                        if (curve != null && curve.IsValid)
                        {
                            ordered.Add(curve.DuplicateCurve());
                        }
                    }
                    break;
                }
            }

            return ordered;
        }

        /// <summary>
        /// Creates a continuous path from a list of curves, connecting them smoothly.
        /// </summary>
        public static List<Point3d> CreateContinuousPath(List<Curve> curves, Point3d startPoint, Point3d endPoint, double connectionStepSize = 1.0)
        {
            var pathPoints = new List<Point3d>();
            
            if (curves == null || curves.Count == 0)
            {
                // Direct connection if no curves
                pathPoints.Add(startPoint);
                pathPoints.Add(endPoint);
                return pathPoints;
            }

            // Start with start point
            pathPoints.Add(startPoint);

            // Connect to first curve
            if (curves.Count > 0)
            {
                Point3d firstCurveStart = curves[0].PointAtStart;
                if (startPoint.DistanceTo(firstCurveStart) > 0.01)
                {
                    int steps = Math.Max(2, (int)Math.Ceiling(startPoint.DistanceTo(firstCurveStart) / connectionStepSize));
                    for (int i = 1; i < steps; i++)
                    {
                        double t = (double)i / steps;
                        pathPoints.Add(startPoint + (firstCurveStart - startPoint) * t);
                    }
                }
            }

            // Add points from curves
            foreach (var curve in curves)
            {
                var curvePoints = SampleCurve(curve, connectionStepSize, false);
                pathPoints.AddRange(curvePoints);
            }

            // Connect to end point
            if (curves.Count > 0)
            {
                Point3d lastCurveEnd = curves[curves.Count - 1].PointAtEnd;
                if (lastCurveEnd.DistanceTo(endPoint) > 0.01)
                {
                    int steps = Math.Max(2, (int)Math.Ceiling(lastCurveEnd.DistanceTo(endPoint) / connectionStepSize));
                    for (int i = 1; i < steps; i++)
                    {
                        double t = (double)i / steps;
                        pathPoints.Add(lastCurveEnd + (endPoint - lastCurveEnd) * t);
                    }
                }
            }

            pathPoints.Add(endPoint);

            return pathPoints;
        }
    }
}

