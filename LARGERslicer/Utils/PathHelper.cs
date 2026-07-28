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

                // CRITICAL: Validate minimum spacing to previous curve
                // Prevents curves from touching in inner regions (visible in spiral centers)
                if (offsets.Count > 0)
                {
                    Curve lastOffset = offsets[offsets.Count - 1];
                    if (!ValidateMinimumCurveSpacing(lastOffset, largest.curve, spacing * 0.95))
                    {
                        // Spacing violation detected - stop here to prevent overlap
                        break;
                    }
                }

                offsets.Add(largest.curve);
                current = largest.curve;
                previousArea = largest.area;
            }

            return offsets;
        }

        /// <summary>
        /// Validates that two curves maintain minimum spacing everywhere.
        /// CRITICAL for large-format printing - prevents material overlap.
        /// </summary>
        private static bool ValidateMinimumCurveSpacing(Curve curve1, Curve curve2, double minSpacing)
        {
            if (curve1 == null || curve2 == null || !curve1.IsValid || !curve2.IsValid)
                return true;

            // Sample first curve and check distances to second curve
            int samples = Math.Max(20, (int)(curve1.GetLength() / (minSpacing * 0.5)));
            samples = Math.Min(samples, 100); // Performance cap

            for (int i = 0; i <= samples; i++)
            {
                double t1 = curve1.Domain.ParameterAt((double)i / samples);
                Point3d pt1 = curve1.PointAt(t1);

                // Find closest point on curve2
                double t2;
                curve2.ClosestPoint(pt1, out t2);
                Point3d pt2 = curve2.PointAt(t2);

                double dist = pt1.DistanceTo(pt2);
                if (dist < minSpacing)
                {
                    return false; // Spacing violation!
                }
            }

            return true;
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

        /// <summary>
        /// Removes self-intersections from curves by splitting at intersection points.
        /// Returns list of non-self-intersecting curve segments sorted by arc length (longest first).
        /// Critical for large-format printing where self-intersections cause material buildup.
        /// </summary>
        public static List<Curve> RemoveSelfIntersections(Curve curve, double tolerance = 0.01)
        {
            if (curve == null || !curve.IsValid)
                return new List<Curve>();

            List<Curve> result = new List<Curve>();

            try
            {
                // Find self-intersection points
                var intersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(curve, tolerance);
                
                if (intersections == null || intersections.Count == 0)
                {
                    // No self-intersections - return original curve
                    result.Add(curve.DuplicateCurve());
                    return result;
                }

                // Collect all intersection parameters
                List<double> tParams = new List<double>();
                foreach (var intersection in intersections)
                {
                    tParams.Add(intersection.ParameterA);
                    tParams.Add(intersection.ParameterB);
                }

                // Add curve start/end parameters
                tParams.Add(curve.Domain.Min);
                tParams.Add(curve.Domain.Max);

                // Sort and remove duplicates
                tParams.Sort();
                var uniqueParams = new List<double>();
                for (int i = 0; i < tParams.Count; i++)
                {
                    if (uniqueParams.Count == 0 || Math.Abs(tParams[i] - uniqueParams[uniqueParams.Count - 1]) > tolerance)
                    {
                        uniqueParams.Add(tParams[i]);
                    }
                }

                // Split curve at all intersection parameters
                for (int i = 0; i < uniqueParams.Count - 1; i++)
                {
                    Curve segment = curve.Trim(uniqueParams[i], uniqueParams[i + 1]);
                    if (segment != null && segment.IsValid && segment.GetLength() > tolerance)
                    {
                        result.Add(segment);
                    }
                }

                // Sort by arc length (longest first) for priority processing
                result.Sort((a, b) => b.GetLength().CompareTo(a.GetLength()));

                return result;
            }
            catch
            {
                // On error, return original curve
                result.Add(curve.DuplicateCurve());
                return result;
            }
        }

        /// <summary>
        /// Optimizes curve order to minimize total travel distance.
        /// Uses nearest-neighbor greedy algorithm for efficiency.
        /// Returns reordered curves with optional connection curves between gaps.
        /// </summary>
        public static List<Curve> OptimizeCurveOrder(List<Curve> curves, Point3d startPoint, out List<Curve> connections, double maxGapDistance = double.MaxValue)
        {
            connections = new List<Curve>();
            
            if (curves == null || curves.Count == 0)
                return new List<Curve>();

            if (curves.Count == 1)
                return new List<Curve> { curves[0].DuplicateCurve() };

            List<Curve> remaining = new List<Curve>(curves);
            List<Curve> ordered = new List<Curve>();
            Point3d currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                // Find nearest curve to current point
                int nearestIndex = -1;
                double minDistance = double.MaxValue;
                bool shouldReverse = false;

                for (int i = 0; i < remaining.Count; i++)
                {
                    Curve c = remaining[i];
                    
                    // Check distance to start
                    double distStart = currentPoint.DistanceTo(c.PointAtStart);
                    if (distStart < minDistance)
                    {
                        minDistance = distStart;
                        nearestIndex = i;
                        shouldReverse = false;
                    }

                    // Check distance to end
                    double distEnd = currentPoint.DistanceTo(c.PointAtEnd);
                    if (distEnd < minDistance)
                    {
                        minDistance = distEnd;
                        nearestIndex = i;
                        shouldReverse = true;
                    }
                }

                if (nearestIndex >= 0)
                {
                    Curve sourceCurve = remaining[nearestIndex];
                    Curve nextCurve = sourceCurve.DuplicateCurve();
                    
                    // Reverse if needed
                    if (shouldReverse)
                    {
                        nextCurve.Reverse();
                    }

                    // Add connection curve if gap is significant
                    if (ordered.Count > 0 && minDistance > 0.01 && minDistance <= maxGapDistance)
                    {
                        Line connectionLine = new Line(currentPoint, nextCurve.PointAtStart);
                        connections.Add(connectionLine.ToNurbsCurve());
                    }

                    ordered.Add(nextCurve);
                    remaining.RemoveAt(nearestIndex);
                    currentPoint = nextCurve.PointAtEnd;
                }
                else
                {
                    break; // Safety - should never happen
                }
            }

            return ordered;
        }

        /// <summary>
        /// Simplifies connection curves by removing unnecessary intermediate connections.
        /// Merges consecutive connection curves that are nearly collinear.
        /// </summary>
        public static List<Curve> SimplifyConnections(List<Curve> connections, double angleToleranceDegrees = 5.0)
        {
            if (connections == null || connections.Count <= 1)
                return connections;

            List<Curve> simplified = new List<Curve>();
            
            try
            {
                for (int i = 0; i < connections.Count; i++)
                {
                    Curve current = connections[i];
                    
                    // Check if we can merge with next connection
                    if (i < connections.Count - 1)
                    {
                        Curve next = connections[i + 1];
                        
                        // Check if curves are connected (end of current touches start of next)
                        double gap = current.PointAtEnd.DistanceTo(next.PointAtStart);
                        
                        if (gap < 0.01)
                        {
                            // Get direction vectors
                            Vector3d dir1 = current.PointAtEnd - current.PointAtStart;
                            Vector3d dir2 = next.PointAtEnd - next.PointAtStart;
                            
                            dir1.Unitize();
                            dir2.Unitize();
                            
                            // Calculate angle between directions
                            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dir1 * dir2)));
                            double angleDegrees = angle * 180.0 / Math.PI;
                            
                            // If nearly collinear, merge them
                            if (angleDegrees < angleToleranceDegrees)
                            {
                                Line merged = new Line(current.PointAtStart, next.PointAtEnd);
                                simplified.Add(merged.ToNurbsCurve());
                                i++; // Skip next curve
                                continue;
                            }
                        }
                    }
                    
                    simplified.Add(current);
                }

                return simplified;
            }
            catch
            {
                return connections;
            }
        }

        /// <summary>
        /// Smooths sharp corners in a path by replacing them with fillet arcs.
        /// CRITICAL for large-format printing (5mm nozzle) to avoid material bunching and blobs.
        /// Based on: CEAD Group guidelines - smooth transitions prevent nozzle slowdown.
        /// </summary>
        /// <param name="path">Path points to smooth</param>
        /// <param name="beadWidth">Extrusion bead width (mm) - determines fillet radius</param>
        /// <param name="minAngleDeg">Minimum angle in degrees - corners sharper than this are smoothed (default 120°)</param>
        /// <returns>Smoothed path with filleted corners</returns>
        public static List<Point3d> SmoothSharpCorners(
            List<Point3d> path,
            double beadWidth,
            double minAngleDeg = 120.0)
        {
            var smoothed = new List<Point3d>();

            if (path == null || path.Count < 3 || beadWidth <= 0)
                return path ?? new List<Point3d>();

            smoothed.Add(path[0]); // Keep first point

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector3d v1 = path[i] - path[i - 1];
                Vector3d v2 = path[i + 1] - path[i];

                // Skip degenerate segments
                if (v1.Length < 0.001 || v2.Length < 0.001)
                {
                    smoothed.Add(path[i]);
                    continue;
                }

                v1.Unitize();
                v2.Unitize();

                double angleDeg = Vector3d.VectorAngle(v1, v2) * (180.0 / Math.PI);

                if (angleDeg < minAngleDeg) // Sharp corner
                {
                    // Replace with fillet arc
                    // Radius = 40% of bead width (tighter curves for better space utilization)
                    double radius = beadWidth * 0.4;
                    var arcPoints = CreateFilletArc(
                        path[i - 1], path[i], path[i + 1], radius);

                    if (arcPoints != null && arcPoints.Count > 0)
                    {
                        smoothed.AddRange(arcPoints);
                    }
                    else
                    {
                        smoothed.Add(path[i]); // Fallback
                    }
                }
                else
                {
                    smoothed.Add(path[i]); // Keep gentle corners
                }
            }

            smoothed.Add(path[path.Count - 1]); // Keep last point
            return smoothed;
        }

        /// <summary>
        /// Creates a fillet arc at a corner point.
        /// Arc ensures smooth material flow without sharp direction changes.
        /// </summary>
        private static List<Point3d> CreateFilletArc(
            Point3d p1, Point3d corner, Point3d p2, double radius)
        {
            var points = new List<Point3d>();

            try
            {
                Vector3d v1 = corner - p1;
                Vector3d v2 = p2 - corner;

                if (v1.Length < 0.001 || v2.Length < 0.001)
                    return points;

                v1.Unitize();
                v2.Unitize();

                double angle = Vector3d.VectorAngle(v1, v2);
                if (angle < 0.01) // Nearly straight
                    return points;

                // Find arc start/end by moving 'radius' back from corner
                double backDist = Math.Min(radius, p1.DistanceTo(corner) * 0.4);
                backDist = Math.Min(backDist, corner.DistanceTo(p2) * 0.4);

                Point3d arcStart = corner - v1 * backDist;
                Point3d arcEnd = corner + v2 * backDist;

                // Arc center calculation
                Vector3d bisector = v1 + v2;
                bisector.Unitize();

                // Perpendicular to bisector (in XY plane)
                Vector3d perpendicular = Vector3d.CrossProduct(bisector, Vector3d.ZAxis);
                if (perpendicular.Length < 0.001)
                {
                    // Bisector is vertical, use different perpendicular
                    perpendicular = Vector3d.CrossProduct(bisector, Vector3d.XAxis);
                }
                perpendicular.Unitize();

                // Calculate offset for arc center
                double halfAngle = angle / 2.0;
                if (Math.Abs(Math.Sin(halfAngle)) < 0.001)
                    return points;

                double offset = backDist / Math.Tan(halfAngle);
                Point3d arcCenter = corner + bisector * offset;

                // Create arc
                Arc arc = new Arc(arcStart, arcCenter, arcEnd);
                if (!arc.IsValid)
                    return points;

                ArcCurve arcCurve = new ArcCurve(arc);

                // Sample arc with appropriate density
                int samples = Math.Max(5, (int)(arcCurve.GetLength() / (radius * 0.2)));
                samples = Math.Min(samples, 20); // Cap at 20 samples

                for (int i = 0; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    double param = arcCurve.Domain.ParameterAt(t);
                    Point3d pt = arcCurve.PointAt(param);
                    points.Add(pt);
                }

                return points;
            }
            catch
            {
                // Fallback: return empty (caller will use corner point)
                return points;
            }
        }

        /// <summary>
        /// Creates a connection between two points following the boundary curve with appropriate offset.
        /// For offset curves (spiral/contour patterns), the connection follows the boundary at the intermediate offset level.
        /// This ensures geometric consistency - connections follow the same offset pattern as the curves themselves.
        /// </summary>
        /// <param name="startPt">Starting point (end of previous curve)</param>
        /// <param name="endPt">Ending point (start of next curve)</param>
        /// <param name="boundary">Original boundary curve</param>
        /// <param name="startCurve">Previous offset curve (for determining offset level)</param>
        /// <param name="endCurve">Next offset curve (for determining offset level)</param>
        /// <param name="spacing">Bead width (spacing between offset curves)</param>
        /// <returns>List of connection points following boundary with appropriate offset</returns>
        public static List<Point3d> CreateOffsetFollowingConnection(
            Point3d startPt,
            Point3d endPt,
            Curve boundary,
            Curve startCurve,
            Curve endCurve,
            double spacing)
        {
            var connection = new List<Point3d>();

            if (boundary == null || !boundary.IsValid)
                return connection;

            try
            {
                // Estimate offset level of start and end curves
                double startOffset = EstimateCurveOffsetLevel(startCurve, boundary, spacing);
                double endOffset = EstimateCurveOffsetLevel(endCurve, boundary, spacing);
                
                // Use average offset for connection (intermediate level)
                double connectionOffset = (startOffset + endOffset) * 0.5;

                // Find closest points on boundary
                double tStart, tEnd;
                boundary.ClosestPoint(startPt, out tStart);
                boundary.ClosestPoint(endPt, out tEnd);

                tStart = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tStart));
                tEnd = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tEnd));

                // Determine direction along boundary (shorter path)
                double distForward = Math.Abs(tEnd - tStart);
                double distBackward = boundary.Domain.Length - distForward;
                bool forward = distForward <= distBackward;

                // Create offset boundary at connection level
                double offsetDirection = GetOffsetDirection(boundary, null);
                double offsetDistance = connectionOffset * offsetDirection;
                
                var offsetCurves = boundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                if (offsetCurves == null || offsetCurves.Length == 0)
                {
                    // Fallback: use boundary directly
                    offsetCurves = new[] { boundary.DuplicateCurve() };
                }

                Curve offsetBoundary = offsetCurves.OrderByDescending(c =>
                {
                    var area = AreaMassProperties.Compute(c);
                    return area != null ? area.Area : 0;
                }).First();

                if (offsetBoundary == null || !offsetBoundary.IsValid)
                    return connection;

                // Find closest points on offset boundary
                double tOffsetStart, tOffsetEnd;
                offsetBoundary.ClosestPoint(startPt, out tOffsetStart);
                offsetBoundary.ClosestPoint(endPt, out tOffsetEnd);

                tOffsetStart = Math.Max(offsetBoundary.Domain.T0, Math.Min(offsetBoundary.Domain.T1, tOffsetStart));
                tOffsetEnd = Math.Max(offsetBoundary.Domain.T0, Math.Min(offsetBoundary.Domain.T1, tOffsetEnd));

                // Determine direction on offset boundary
                double offsetDistForward = Math.Abs(tOffsetEnd - tOffsetStart);
                double offsetDistBackward = offsetBoundary.Domain.Length - offsetDistForward;
                bool offsetForward = offsetDistForward <= offsetDistBackward;

                // Sample offset boundary
                double offsetLength = offsetForward ? offsetDistForward : offsetDistBackward;
                int numSteps = Math.Max(2, (int)Math.Ceiling(offsetLength / (spacing * 0.1)));
                numSteps = Math.Min(numSteps, 200);

                for (int i = 1; i < numSteps; i++)
                {
                    double t;
                    if (offsetForward)
                    {
                        t = tOffsetStart + (tOffsetEnd - tOffsetStart) * ((double)i / numSteps);
                    }
                    else
                    {
                        if (tOffsetStart > tOffsetEnd)
                        {
                            double wrapLength = (offsetBoundary.Domain.T1 - tOffsetStart) + (tOffsetEnd - offsetBoundary.Domain.T0);
                            t = tOffsetStart + wrapLength * ((double)i / numSteps);
                            if (t > offsetBoundary.Domain.T1)
                                t = offsetBoundary.Domain.T0 + (t - offsetBoundary.Domain.T1);
                        }
                        else
                        {
                            double wrapLength = (tOffsetStart - offsetBoundary.Domain.T0) + (offsetBoundary.Domain.T1 - tOffsetEnd);
                            t = tOffsetStart - wrapLength * ((double)i / numSteps);
                            if (t < offsetBoundary.Domain.T0)
                                t = offsetBoundary.Domain.T1 - (offsetBoundary.Domain.T0 - t);
                        }
                    }

                    t = Math.Max(offsetBoundary.Domain.T0, Math.Min(offsetBoundary.Domain.T1, t));
                    Point3d pt = offsetBoundary.PointAt(t);
                    connection.Add(pt);
                }
            }
            catch
            {
                // Fallback: empty connection
            }

            return connection;
        }

        /// <summary>
        /// Estimates the offset level of a curve relative to the original boundary.
        /// Returns the approximate offset distance (in spacing units).
        /// </summary>
        private static double EstimateCurveOffsetLevel(Curve curve, Curve boundary, double spacing)
        {
            if (curve == null || boundary == null || !curve.IsValid || !boundary.IsValid)
                return 0.0;

            try
            {
                // Sample points on curve and find average distance to boundary
                var samplePoints = SampleCurve(curve, spacing * 0.5, false);
                if (samplePoints.Count == 0)
                    return 0.0;

                double totalDistance = 0.0;
                int validSamples = 0;

                foreach (var pt in samplePoints)
                {
                    double t;
                    boundary.ClosestPoint(pt, out t);
                    Point3d closestOnBoundary = boundary.PointAt(t);
                    double dist = pt.DistanceTo(closestOnBoundary);
                    
                    // Determine if point is inside or outside
                    PointContainment containment = boundary.Contains(pt, Plane.WorldXY, 0.01);
                    if (containment == PointContainment.Inside)
                    {
                        // Point is inside, so offset is negative (inward)
                        totalDistance -= dist;
                    }
                    else
                    {
                        // Point is outside, so offset is positive (outward)
                        totalDistance += dist;
                    }
                    validSamples++;
                }

                if (validSamples > 0)
                {
                    double avgDistance = totalDistance / validSamples;
                    // Convert to spacing units (approximate)
                    return avgDistance / spacing;
                }
            }
            catch
            {
                // Fallback
            }

            return 0.0;
        }

        /// <summary>
        /// Creates a smooth tangent-aligned connection between two points on different curves.
        /// Uses cubic Bezier with tangent alignment for continuous flow - NO sharp corners.
        /// Maintains minimum radial clearance to avoid crossing existing paths.
        /// CRITICAL: For large-format continuous toolpaths without direction changes.
        /// </summary>
        /// <param name="startPt">Starting point (end of previous curve)</param>
        /// <param name="endPt">Ending point (start of next curve)</param>
        /// <param name="sourceCurve">Source curve to get tangent from (can be null)</param>
        /// <param name="targetCurve">Target curve to get tangent from (can be null)</param>
        /// <param name="spacing">Bead width for proper point spacing and clearance</param>
        /// <returns>List of connection points forming smooth transition</returns>
        public static List<Point3d> Create90DegreeConnection(
            Point3d startPt, 
            Point3d endPt, 
            Curve sourceCurve, 
            Curve targetCurve, 
            double spacing)
        {
            var connection = new List<Point3d>();
            
            // Short distance: direct connection
            double distance = startPt.DistanceTo(endPt);
            if (distance < spacing * 2.0)
            {
                int steps = Math.Max(2, (int)Math.Ceiling(distance / (spacing * 0.5)));
                for (int i = 1; i < steps; i++)
                {
                    double t = (double)i / steps;
                    connection.Add(startPt + (endPt - startPt) * t);
                }
                return connection;
            }

            // Get tangents at connection points
            Vector3d exitTangent = Vector3d.Unset;
            Vector3d entryTangent = Vector3d.Unset;
            
            if (sourceCurve != null && sourceCurve.IsValid)
            {
                try
                {
                    double tStart;
                    sourceCurve.ClosestPoint(startPt, out tStart);
                    exitTangent = sourceCurve.TangentAt(tStart);
                    exitTangent.Unitize();
                }
                catch { exitTangent = Vector3d.Unset; }
            }
            
            if (targetCurve != null && targetCurve.IsValid)
            {
                try
                {
                    double tEnd;
                    targetCurve.ClosestPoint(endPt, out tEnd);
                    entryTangent = targetCurve.TangentAt(tEnd);
                    entryTangent.Unitize();
                    entryTangent = -entryTangent; // Reverse for entry direction
                }
                catch { entryTangent = Vector3d.Unset; }
            }

            // If no valid tangents, use direction-based defaults
            Vector3d connectionDir = endPt - startPt;
            connectionDir.Unitize();
            
            if (!exitTangent.IsValid || exitTangent.Length < 0.001)
                exitTangent = connectionDir;
            if (!entryTangent.IsValid || entryTangent.Length < 0.001)
                entryTangent = connectionDir;

            // Create cubic Bezier curve with tangent alignment
            // Control points positioned for smooth S-curve with minimum radial clearance
            double controlDistance = distance * 0.4; // Control point influence
            
            // Add small radial offset to avoid crossing - perpendicular to connection
            Vector3d perpendicular = Vector3d.CrossProduct(connectionDir, Vector3d.ZAxis);
            if (perpendicular.Length < 0.001)
                perpendicular = new Vector3d(-connectionDir.Y, connectionDir.X, 0);
            perpendicular.Unitize();
            
            // Determine offset direction (away from center/existing paths)
            double radialOffset = spacing * 0.3; // Small radial clearance
            
            Point3d control1 = startPt + exitTangent * controlDistance + perpendicular * radialOffset;
            Point3d control2 = endPt + entryTangent * controlDistance - perpendicular * radialOffset;
            
            // Sample cubic Bezier curve
            int numSteps = Math.Max(10, (int)Math.Ceiling(distance / (spacing * 0.3)));
            numSteps = Math.Min(numSteps, 50); // Performance cap
            
            for (int i = 1; i < numSteps; i++)
            {
                double t = (double)i / numSteps;
                
                // Cubic Bezier formula: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
                Point3d pt = Math.Pow(1 - t, 3) * startPt +
                             3 * Math.Pow(1 - t, 2) * t * control1 +
                             3 * (1 - t) * t * t * control2 +
                             Math.Pow(t, 3) * endPt;
                
                connection.Add(pt);
            }
            
            return connection;
        }
    }
}

