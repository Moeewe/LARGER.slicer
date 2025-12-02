using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for generating Connected Fermat Spirals (CFS) toolpaths.
    /// CFS provides smooth, continuous curves ideal for large-format 3D printing.
    /// Based on research: Zhao et al., 2016 - Connected Fermat Spirals for Layered Fabrication
    /// </summary>
    public static class FermatSpiralHelper
    {
        /// <summary>
        /// Generates a Fermat spiral (Archimedean spiral variant) from center outward.
        /// Fermat spiral: r = a * sqrt(theta), where a controls spacing.
        /// </summary>
        /// <param name="center">Center point of the spiral</param>
        /// <param name="maxRadius">Maximum radius of the spiral</param>
        /// <param name="spacing">Desired spacing between spiral turns (bead width)</param>
        /// <param name="numTurns">Number of spiral turns</param>
        /// <returns>List of points forming the spiral</returns>
        public static List<Point3d> GenerateFermatSpiral(
            Point3d center, 
            double maxRadius, 
            double spacing, 
            int numTurns = 50)
        {
            var points = new List<Point3d>();
            
            if (maxRadius <= 0 || spacing <= 0 || numTurns <= 0)
                return points;

            // Calculate spiral parameter 'a' from spacing
            // For Fermat spiral: spacing ≈ 2*pi*a / sqrt(theta) at each turn
            // Approximate: a ≈ spacing / (2*pi)
            double a = spacing / (2.0 * Math.PI);

            // Generate spiral points
            int numPoints = numTurns * 100; // High resolution for smooth curves
            double maxTheta = 2.0 * Math.PI * numTurns;

            for (int i = 0; i <= numPoints; i++)
            {
                double theta = (maxTheta * i) / numPoints;
                double r = a * Math.Sqrt(theta);
                
                // Stop if radius exceeds maximum
                if (r > maxRadius)
                    break;

                // Convert to Cartesian coordinates
                double x = center.X + r * Math.Cos(theta);
                double y = center.Y + r * Math.Sin(theta);
                double z = center.Z;

                points.Add(new Point3d(x, y, z));
            }

            return points;
        }

        /// <summary>
        /// Generates a Fermat spiral from boundary inward (for infill).
        /// Starts at boundary and spirals inward to center.
        /// Uses offset curves approach: generates offset boundaries and samples them as spiral.
        /// </summary>
        /// <param name="boundary">Boundary curve to fill</param>
        /// <param name="spacing">Spacing between spiral turns (bead width)</param>
        /// <param name="minRadius">Minimum radius before stopping</param>
        /// <returns>List of points forming the inward spiral</returns>
        public static List<Point3d> GenerateInwardFermatSpiral(
            Curve boundary,
            double spacing,
            double minRadius = 0.5)
        {
            var points = new List<Point3d>();

            if (boundary == null || !boundary.IsValid || spacing <= 0)
                return points;

            // Get boundary center and size
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d center = bbox.Center;
            double maxRadius = Math.Max(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y) * 0.5;

            if (maxRadius < minRadius)
                return points;

            // Better approach: Generate offset curves and sample them as continuous spiral
            // This ensures we follow the boundary shape more accurately
            var offsetCurves = PathHelper.GenerateOffsetCurves(boundary, spacing, 1000, null);
            
            if (offsetCurves.Count == 0)
            {
                // Fallback: use center-based spiral
                var spiralPoints = GenerateFermatSpiral(center, maxRadius, spacing);
                // Filter to stay inside boundary
                foreach (var pt in spiralPoints)
                {
                    if (boundary.Contains(pt, Plane.WorldXY, 0.01) == PointContainment.Inside)
                    {
                        points.Add(pt);
                    }
                }
                // Reverse to go from outer to inner
                points.Reverse();
                return points;
            }

            // Sample each offset curve and connect them continuously
            // Start from outermost curve
            for (int i = 0; i < offsetCurves.Count; i++)
            {
                var curve = offsetCurves[i];
                if (curve == null || !curve.IsValid)
                    continue;

                // Sample curve with appropriate density
                var curvePoints = PathHelper.SampleCurve(curve, spacing * 0.3, true);
                
                if (curvePoints.Count >= 2)
                {
                    // Connect to previous curve if not first
                    if (points.Count > 0 && i > 0)
                    {
                        Point3d lastPt = points[points.Count - 1];
                        Point3d firstPt = curvePoints[0];
                        
                        // Find closest point on current curve to last point
                        double t;
                        curve.ClosestPoint(lastPt, out t);
                        Point3d closestPt = curve.PointAt(t);
                        
                        // Reorder curve points to start from closest
                        int closestIdx = 0;
                        double minDist = double.MaxValue;
                        for (int j = 0; j < curvePoints.Count; j++)
                        {
                            double dist = curvePoints[j].DistanceTo(closestPt);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                closestIdx = j;
                            }
                        }
                        
                        // Rotate points to start from closest
                        if (closestIdx > 0)
                        {
                            var rotated = new List<Point3d>();
                            rotated.AddRange(curvePoints.Skip(closestIdx));
                            rotated.AddRange(curvePoints.Take(closestIdx));
                            curvePoints = rotated;
                        }
                        
                        // Add connection if needed
                        if (lastPt.DistanceTo(curvePoints[0]) > spacing * 0.1)
                        {
                            int steps = Math.Max(2, (int)Math.Ceiling(lastPt.DistanceTo(curvePoints[0]) / spacing));
                            for (int s = 1; s < steps; s++)
                            {
                                double tConn = (double)s / steps;
                                points.Add(lastPt + (curvePoints[0] - lastPt) * tConn);
                            }
                        }
                    }
                    
                    points.AddRange(curvePoints);
                }
            }

            return points;
        }

        /// <summary>
        /// Partitions a polygon into sub-regions suitable for Fermat spiral filling.
        /// For complex shapes, divides into simpler convex regions.
        /// </summary>
        /// <param name="boundary">Boundary curve</param>
        /// <param name="holes">Hole curves</param>
        /// <param name="maxRegionSize">Maximum size for a region before subdividing</param>
        /// <returns>List of sub-region boundaries</returns>
        public static List<Curve> PartitionIntoRegions(
            Curve boundary,
            List<Curve> holes,
            double maxRegionSize = 50.0)
        {
            var regions = new List<Curve>();

            if (boundary == null || !boundary.IsValid)
                return regions;

            // For now, use simple approach: single region if not too large
            // Advanced: could use polygon decomposition algorithms
            BoundingBox bbox = boundary.GetBoundingBox(true);
            double size = Math.Max(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);

            if (size <= maxRegionSize)
            {
                // Single region is fine
                regions.Add(boundary.DuplicateCurve());
            }
            else
            {
                // Subdivide into grid of regions
                // Simple grid subdivision for now
                int gridSize = (int)Math.Ceiling(size / maxRegionSize);
                double cellSize = size / gridSize;

                Point3d minPt = bbox.Min;
                for (int i = 0; i < gridSize; i++)
                {
                    for (int j = 0; j < gridSize; j++)
                    {
                        Point3d cellMin = new Point3d(
                            minPt.X + i * cellSize,
                            minPt.Y + j * cellSize,
                            minPt.Z);
                        Point3d cellMax = new Point3d(
                            cellMin.X + cellSize,
                            cellMin.Y + cellSize,
                            cellMin.Z);

                        // Create cell boundary
                        Polyline cellBoundary = new Polyline(new[]
                        {
                            cellMin,
                            new Point3d(cellMax.X, cellMin.Y, cellMin.Z),
                            cellMax,
                            new Point3d(cellMin.X, cellMax.Y, cellMin.Z),
                            cellMin
                        });

                        // Check if cell intersects with boundary
                        Curve cellCurve = new PolylineCurve(cellBoundary);
                        var intersection = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                            cellCurve, boundary, 0.01, 0.01);

                        if (intersection != null && intersection.Count > 0)
                        {
                            // Cell intersects boundary - use intersection as region
                            // For simplicity, use cell if center is inside boundary
                            Point3d cellCenter = (cellMin + cellMax) * 0.5;
                            if (boundary.Contains(cellCenter, Plane.WorldXY, 0.01) == PointContainment.Inside)
                            {
                                regions.Add(cellCurve);
                            }
                        }
                    }
                }
            }

            return regions;
        }

        /// <summary>
        /// Connects multiple Fermat spirals into a single continuous path.
        /// Finds nearest endpoints between spirals and connects them.
        /// </summary>
        /// <param name="spirals">List of spiral point sequences</param>
        /// <param name="boundary">Boundary curve for connection path</param>
        /// <param name="spacing">Bead width for connection spacing</param>
        /// <returns>Single continuous path connecting all spirals</returns>
        public static List<Point3d> ConnectSpirals(
            List<List<Point3d>> spirals,
            Curve boundary,
            double spacing)
        {
            var continuousPath = new List<Point3d>();

            if (spirals == null || spirals.Count == 0)
                return continuousPath;

            if (spirals.Count == 1)
            {
                return spirals[0];
            }

            // Use nearest-neighbor approach to connect spirals
            var remainingSpirals = new List<List<Point3d>>(spirals);
            var orderedSpirals = new List<List<Point3d>>();

            // Start with first spiral
            orderedSpirals.Add(remainingSpirals[0]);
            remainingSpirals.RemoveAt(0);
            Point3d currentEnd = remainingSpirals[0][remainingSpirals[0].Count - 1];

            // Connect remaining spirals
            while (remainingSpirals.Count > 0)
            {
                // Find nearest spiral
                double minDist = double.MaxValue;
                int nearestIdx = -1;
                bool useStart = false;

                for (int i = 0; i < remainingSpirals.Count; i++)
                {
                    if (remainingSpirals[i].Count == 0)
                        continue;

                    double distStart = currentEnd.DistanceTo(remainingSpirals[i][0]);
                    double distEnd = currentEnd.DistanceTo(remainingSpirals[i][remainingSpirals[i].Count - 1]);

                    if (distStart < minDist)
                    {
                        minDist = distStart;
                        nearestIdx = i;
                        useStart = true;
                    }
                    if (distEnd < minDist)
                    {
                        minDist = distEnd;
                        nearestIdx = i;
                        useStart = false;
                    }
                }

                if (nearestIdx >= 0)
                {
                    var nextSpiral = remainingSpirals[nearestIdx];
                    remainingSpirals.RemoveAt(nearestIdx);

                    // Reverse if needed to start from nearest point
                    if (!useStart)
                    {
                        nextSpiral.Reverse();
                    }

                    // Add connection along boundary
                    if (boundary != null && boundary.IsValid)
                    {
                        var connection = CreateBoundaryConnection(
                            currentEnd, nextSpiral[0], boundary, spacing);
                        continuousPath.AddRange(connection);
                    }
                    else
                    {
                        // Simple linear connection
                        int steps = Math.Max(2, (int)Math.Ceiling(minDist / spacing));
                        for (int s = 1; s < steps; s++)
                        {
                            double t = (double)s / steps;
                            continuousPath.Add(currentEnd + (nextSpiral[0] - currentEnd) * t);
                        }
                    }

                    // Add spiral points
                    continuousPath.AddRange(nextSpiral);
                    currentEnd = nextSpiral[nextSpiral.Count - 1];
                    orderedSpirals.Add(nextSpiral);
                }
                else
                {
                    // No more spirals to connect
                    break;
                }
            }

            return continuousPath;
        }

        /// <summary>
        /// Creates a smooth connection between two points along the boundary.
        /// </summary>
        private static List<Point3d> CreateBoundaryConnection(
            Point3d startPt, Point3d endPt, Curve boundary, double spacing)
        {
            var connection = new List<Point3d>();

            if (boundary == null || !boundary.IsValid)
                return connection;

            try
            {
                double tStart, tEnd;
                boundary.ClosestPoint(startPt, out tStart);
                boundary.ClosestPoint(endPt, out tEnd);

                tStart = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tStart));
                tEnd = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tEnd));

                // Determine shorter path
                double distForward = Math.Abs(tEnd - tStart);
                double distBackward = boundary.Domain.Length - distForward;
                bool forward = distForward <= distBackward;

                double boundaryLength = forward ? distForward : distBackward;
                int numSteps = Math.Max(2, (int)Math.Ceiling(boundaryLength / (spacing * 0.1)));
                numSteps = Math.Min(numSteps, 200);

                for (int i = 1; i < numSteps; i++)
                {
                    double t;
                    if (forward)
                    {
                        t = tStart + (tEnd - tStart) * ((double)i / numSteps);
                    }
                    else
                    {
                        if (tStart > tEnd)
                        {
                            double wrapLength = (boundary.Domain.T1 - tStart) + (tEnd - boundary.Domain.T0);
                            t = tStart + wrapLength * ((double)i / numSteps);
                            if (t > boundary.Domain.T1)
                                t = boundary.Domain.T0 + (t - boundary.Domain.T1);
                        }
                        else
                        {
                            double wrapLength = (tStart - boundary.Domain.T0) + (boundary.Domain.T1 - tEnd);
                            t = tStart - wrapLength * ((double)i / numSteps);
                            if (t < boundary.Domain.T0)
                                t = boundary.Domain.T1 - (boundary.Domain.T0 - t);
                        }
                    }

                    t = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, t));
                    Point3d pt = boundary.PointAt(t);

                    // Offset inward by half spacing
                    Vector3d tangent = boundary.TangentAt(t);
                    tangent.Unitize();
                    Vector3d normal = new Vector3d(-tangent.Y, tangent.X, 0);
                    normal.Unitize();

                    BoundingBox bbox = boundary.GetBoundingBox(true);
                    Point3d center = bbox.Center;
                    Vector3d toCenter = center - pt;
                    toCenter.Unitize();
                    if (normal * toCenter < 0)
                        normal = -normal;

                    Point3d offsetPt = pt + normal * (spacing * 0.5);
                    connection.Add(offsetPt);
                }
            }
            catch
            {
                // Fallback: empty
            }

            return connection;
        }
    }
}

