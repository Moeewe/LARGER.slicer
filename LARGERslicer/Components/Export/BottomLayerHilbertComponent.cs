using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Infill Hilbert - Generates space-filling Hilbert curve fill pattern.
    /// Pattern E: Hilbert Curve (space-filling)
    /// </summary>
    public class InfillHilbertComponent : BottomLayerPatternBase
    {
        public InfillHilbertComponent()
            : base("Infill Hilbert", "Infill Hilbert",
                  "Generates space-filling Hilbert curve pattern. Automatically detects curve orientation to fill inward.")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddIntegerParameter("Order", "Order", "Hilbert curve order (recursion depth, typically 3-6)", GH_ParamAccess.item, 4);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate inputs using base class method
            if (!ValidateInputs(DA, out Curve boundary, out Point3d seamPoint, out double spacing, out double boundaryOffset, out List<Curve> holes))
                return;

            int order = 4;
            DA.GetData(5, ref order);

            if (order < 1 || order > 8)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Order should be between 1 and 8. Using clamped value.");
                order = Math.Max(1, Math.Min(8, order));
            }

            // Prepare boundary with offset (direction auto-detected)
            // For large-format: outer path should be ~half bead width from boundary
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate if not provided)
            Point3d? seamPointNullable = seamPoint.IsValid ? (Point3d?)seamPoint : null;
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, seamPointNullable);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, order, holes);

            if (pathPoints == null || pathPoints.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Pattern generation resulted in insufficient points.");
                return;
            }

            // Create output curves using base class method
            CreateOutputCurves(pathPoints, segments, out Curve pathCurve, out List<Curve> segmentCurves);

            if (pathCurve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create output curve.");
                return;
            }

            // Calculate statistics using base class method
            string stats = CalculateStatistics(pathPoints, closedBoundary, spacing, $"Order={order}");

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, segmentCurves);
            DA.SetDataList(2, pathPoints);
            DA.SetData(3, stats);
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double seamParameter,
            double spacing,
            int order,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Get boundary bounding box
            BoundingBox bbox = boundary.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double size = Math.Max(width, height);

            // Calculate required order based on spacing to ensure sufficient resolution
            // Order determines grid resolution: n = 2^order, so grid cell size = size / (2^order)
            // We want grid cell size <= spacing for good coverage
            int calculatedOrder = order;
            if (spacing > 0)
            {
                double minCellSize = size / Math.Pow(2, order);
                // If spacing is smaller than cell size, we might need higher order
                // But order is limited by user input, so we use it as maximum
                // The actual sampling will be done based on spacing
            }

            // Generate Hilbert curve points in normalized space [0,1] x [0,1]
            // Use order to determine resolution, but spacing will control final point density
            var hilbertPoints = GenerateHilbertCurve(calculatedOrder);

            // Scale and transform to boundary space
            var scaledPoints = new List<Point3d>();
            double z = bbox.Center.Z;

            foreach (var pt in hilbertPoints)
            {
                // Scale to bounding box
                double x = bbox.Min.X + pt.X * width;
                double y = bbox.Min.Y + pt.Y * height;

                // Check if point is inside boundary and outside holes
                Point3d worldPt = new Point3d(x, y, z);
                if (boundary.Contains(worldPt, Plane.WorldXY, 0.01) == PointContainment.Inside)
                {
                    // Check if point is outside all holes
                    bool validPoint = true;
                    if (holes != null && holes.Count > 0)
                    {
                        foreach (var hole in holes)
                        {
                            if (hole != null && hole.IsValid)
                            {
                                if (hole.Contains(worldPt, Plane.WorldXY, 0.01) == PointContainment.Inside)
                                {
                                    validPoint = false;
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (validPoint)
                    {
                        scaledPoints.Add(worldPt);
                    }
                }
            }

            if (scaledPoints.Count < 2)
            {
                // Fallback: just return seam position
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Sample points with spacing - this is the key: spacing controls point density
            // The order only determines the maximum resolution of the curve
            // Create a continuous curve from the points and sample it with the desired spacing
            var sampledPoints = SampleCurveWithSpacing(scaledPoints, spacing);

            // Reorder to start from seam position
            if (sampledPoints.Count > 0)
            {
                // Find closest point to seam position
                int startIdx = 0;
                double minStartDist = double.MaxValue;
                for (int i = 0; i < sampledPoints.Count; i++)
                {
                    double dist = seamPosition.DistanceTo(sampledPoints[i]);
                    if (dist < minStartDist)
                    {
                        minStartDist = dist;
                        startIdx = i;
                    }
                }

                // Reorder points starting from closest to seam position
                var reordered = new List<Point3d>();
                for (int i = startIdx; i < sampledPoints.Count; i++)
                    reordered.Add(sampledPoints[i]);
                for (int i = 0; i < startIdx; i++)
                    reordered.Add(sampledPoints[i]);

                sampledPoints = reordered;
            }

            segments.Add(sampledPoints);

            // Build continuous path starting from seam position
            // Find closest point to seam position in sampled points
            if (sampledPoints.Count > 0)
            {
                // Find closest point to seam
                int closestIdx = 0;
                double minDist = double.MaxValue;
                for (int i = 0; i < sampledPoints.Count; i++)
                {
                    double dist = seamPosition.DistanceTo(sampledPoints[i]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestIdx = i;
                    }
                }

                // Reorder to start from closest point
                var reorderedPoints = new List<Point3d>();
                for (int i = closestIdx; i < sampledPoints.Count; i++)
                    reorderedPoints.Add(sampledPoints[i]);
                for (int i = 0; i < closestIdx; i++)
                    reorderedPoints.Add(sampledPoints[i]);

                sampledPoints = reorderedPoints;
                segments[0] = sampledPoints; // Update segments too
            }

            // Start path from seam position
            pathPoints.Add(seamPosition);
            
            if (sampledPoints.Count > 0)
            {
                // Connect to first point
                Point3d firstPt = sampledPoints[0];
                if (seamPosition.DistanceTo(firstPt) > spacing * 0.1)
                {
                    int steps = Math.Max(2, (int)Math.Ceiling(seamPosition.DistanceTo(firstPt) / spacing));
                    for (int s = 1; s < steps; s++)
                    {
                        double t = (double)s / steps;
                        pathPoints.Add(seamPosition + (firstPt - seamPosition) * t);
                    }
                }

                pathPoints.AddRange(sampledPoints);

                // IMPORTANT: Do NOT add a final connection back to seam position
                // The path should end at the last point of the Hilbert curve
                // This prevents the unwanted crossing path through the entire component
            }

            return (pathPoints, segments);
        }

        /// <summary>
        /// Generates Hilbert curve points in normalized space [0,1] x [0,1].
        /// Order determines the resolution: higher order = more points = finer curve.
        /// The actual point spacing in the final path is controlled by SamplePointsWithSpacing.
        /// </summary>
        private List<Point3d> GenerateHilbertCurve(int order)
        {
            var points = new List<Point3d>();
            int n = (int)Math.Pow(2, order);
            int totalPoints = n * n;

            // Generate all points along the Hilbert curve
            for (int i = 0; i < totalPoints; i++)
            {
                var (x, y) = HilbertIndexToXY(i, n);
                // Normalize to [0,1]
                // Use (n-1) to ensure we cover the full range [0,1]
                double nx = n > 1 ? (double)x / (n - 1) : 0.0;
                double ny = n > 1 ? (double)y / (n - 1) : 0.0;
                points.Add(new Point3d(nx, ny, 0));
            }

            return points;
        }

        /// <summary>
        /// Converts Hilbert curve index to x,y coordinates.
        /// </summary>
        private (int x, int y) HilbertIndexToXY(int index, int n)
        {
            int x = 0, y = 0;
            int t = index;

            for (int s = 1; s < n; s *= 2)
            {
                int rx = 1 & (t / 2);
                int ry = 1 & (t ^ rx);
                Rotate(s, ref x, ref y, rx, ry);
                x += s * rx;
                y += s * ry;
                t /= 4;
            }

            return (x, y);
        }

        /// <summary>
        /// Rotates/flips a quadrant.
        /// </summary>
        private void Rotate(int n, ref int x, ref int y, int rx, int ry)
        {
            if (ry == 0)
            {
                // Swap x and y
                int temp = x;
                x = y;
                y = temp;
                
                if (rx == 1)
                {
                    x = n - 1 - x;
                    y = n - 1 - y;
                }
            }
        }

        /// <summary>
        /// Samples a curve (represented as point list) with specified spacing.
        /// Creates a polyline from the points and samples it at regular intervals.
        /// This ensures the spacing parameter directly controls the point density.
        /// </summary>
        private List<Point3d> SampleCurveWithSpacing(List<Point3d> points, double spacing)
        {
            if (points.Count == 0)
                return new List<Point3d>();

            if (spacing <= 0)
                return points; // Return all points if spacing is invalid

            if (points.Count < 2)
                return points;

            // Create a polyline from the points
            Polyline polyline = new Polyline(points);
            if (!polyline.IsValid)
                return points;

            // Sample the polyline at regular intervals based on spacing
            var sampled = new List<Point3d>();
            double totalLength = polyline.Length;
            
            if (totalLength <= 0)
                return points;

            // Start with first point
            sampled.Add(points[0]);

            // Sample along the curve at spacing intervals
            // Convert to PolylineCurve for parameter access
            PolylineCurve polylineCurve = new PolylineCurve(polyline);
            
            double currentDistance = spacing;
            while (currentDistance < totalLength)
            {
                double normalizedT = currentDistance / totalLength;
                double t;
                polylineCurve.NormalizedLengthParameter(normalizedT, out t);
                Point3d pt = polylineCurve.PointAt(t);
                sampled.Add(pt);
                currentDistance += spacing;
            }

            // Always include the last point
            if (sampled.Count == 0 || sampled[sampled.Count - 1].DistanceTo(points[points.Count - 1]) > spacing * 0.1)
            {
                sampled.Add(points[points.Count - 1]);
            }

            return sampled;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillHilbertIcon.png");
        public override Guid ComponentGuid => new Guid("0ab0b987-4373-4370-82b9-02330b192712");
    }
}

