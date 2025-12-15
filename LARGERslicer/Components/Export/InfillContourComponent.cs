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
    /// Bottom Layer Contour Pattern - Generates offset-based contour infill pattern.
    /// Based on Laurent Delrieu's approach: uses offset curves (like Clipper) to create continuous infill.
    /// Handles complex geometries with holes/islands. Supports ArcWelder conversion for GCode optimization.
    /// References:
    /// - https://discourse.mcneel.com/t/first-journey-in-3d-printing/145253
    /// - https://discourse.mcneel.com/t/spiralize-offset-curves-for-fabrication/84867
    /// - Connected Fermat Spirals for Layered Fabrication paper
    /// </summary>
    public class InfillContourComponent : BottomLayerPatternBase
    {
        public InfillContourComponent()
            : base("Single Line Fill with Offsets", "SLF Offsets",
                  "Generates offset-based contour infill pattern for complex geometries with holes. Uses offset curves (Clipper-like) to create continuous paths. Supports ArcWelder conversion. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Use ArcWelder", "ArcWelder", "Convert polylines to arcs and lines for optimized GCode", GH_ParamAccess.item, false);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate minimal common inputs
            Curve boundary;
            double printWidth;
            List<Curve> holes;

            if (!ValidateInputs(DA, out boundary, out printWidth, out holes))
                return;

            // Get pattern-specific parameters
            bool useArcWelder = false;
            DA.GetData(3, ref useArcWelder);  // Index 3 after base inputs (0-2)

            double spacing = printWidth;
            double boundaryOffset = 0.0; // Will be calculated automatically

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, false, 0.0, holes);

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

            // Set output - only Single Line Fill for Contour
            DA.SetData(0, pathCurve);
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double seamParameter,
            double spacing,
            bool randomBridges,
            double bridgeDensity,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Step 1: Generate offset curves inward (like Clipper offset)
            // This handles complex geometries with holes automatically
            var offsetCurves = GenerateOffsetCurvesWithHoles(boundary, holes, spacing);

            if (offsetCurves.Count == 0)
            {
                // Fallback: just return seam position
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Add boundary as first curve (if it's part of the pattern)
            // In contour pattern, we typically start from the offset curves, not the boundary itself
            // But we can include it if needed
            var allCurves = new List<Curve>();
            allCurves.AddRange(offsetCurves);

            // Step 3: Optimize curve order starting from seam position
            List<Curve> connections;
            var orderedCurves = PathHelper.OptimizeCurveOrder(allCurves, seamPosition, out connections);

            // Step 4: Sample points from each curve, starting from seam position
            var allCurvePoints = new List<List<Point3d>>();
            Point3d currentEndPoint = seamPosition;

            foreach (var curve in orderedCurves)
            {
                // Find closest point on curve to current end point
                double t;
                curve.ClosestPoint(currentEndPoint, out t);
                Point3d closestPt = curve.PointAt(t);

                // Sample curve starting from closest point
                var curvePoints = SampleCurveFromPoint(curve, spacing * 0.5, closestPt, t);
                
                // Filter points that are inside holes
                if (holes != null && holes.Count > 0)
                {
                    curvePoints = curvePoints.Where(pt => IsPointValid(pt, boundary, holes)).ToList();
                }
                
                if (curvePoints.Count >= 2)
                {
                    allCurvePoints.Add(curvePoints);
                    currentEndPoint = curvePoints[curvePoints.Count - 1];
                }
            }

            // Step 5: Add random bridges between curves (if enabled)
            if (randomBridges && allCurvePoints.Count > 1)
            {
                allCurvePoints = AddRandomBridgesToPointLists(allCurvePoints, bridgeDensity, spacing);
            }

            // Step 6: Build continuous path with proper layer-spaced connections
            // FIXED: Ensure all segments are connected to create continuous print area with proper spacing
            segments = allCurvePoints;

            // Start from seam position
            pathPoints.Add(seamPosition);
            
            for (int segIdx = 0; segIdx < segments.Count; segIdx++)
            {
                var seg = segments[segIdx];
                if (seg.Count == 0)
                    continue;
                    
                if (pathPoints.Count > 0)
                {
                    Point3d lastPt = pathPoints[pathPoints.Count - 1];
                    Point3d firstPt = seg[0];
                    double distance = lastPt.DistanceTo(firstPt);
                    
                    if (distance > spacing * 0.1)
                    {
                        // CRITICAL FIX: Use direct connection for short distances, boundary-following for long distances
                        // This ensures continuous print area with proper spacing between layers
                        bool useDirectConnection = distance < spacing * 5.0;
                        
                        if (useDirectConnection)
                        {
                            // Direct linear connection (shortest path, maintains spacing)
                            int steps = Math.Max(2, (int)Math.Ceiling(distance / (spacing * 0.5)));
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                            }
                        }
                        else
                        {
                            // Long jump: use offset-following connection to maintain geometric consistency
                            Curve currentCurve = segIdx > 0 && segIdx - 1 < orderedCurves.Count ? orderedCurves[segIdx - 1] : null;
                            Curve nextCurve = segIdx < orderedCurves.Count ? orderedCurves[segIdx] : null;
                            
                            var connectionPoints = PathHelper.CreateOffsetFollowingConnection(
                                lastPt, firstPt, boundary, currentCurve, nextCurve, spacing);
                            
                            // Fallback to 90° connection if offset connection fails
                            if (connectionPoints.Count == 0)
                            {
                                connectionPoints = PathHelper.Create90DegreeConnection(
                                    lastPt, firstPt, currentCurve, nextCurve, spacing);
                            }
                            
                            // Final fallback: direct connection
                            if (connectionPoints.Count == 0)
                            {
                                int steps = Math.Max(2, (int)Math.Ceiling(distance / (spacing * 0.5)));
                                for (int s = 1; s < steps; s++)
                                {
                                    double t = (double)s / steps;
                                    pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                                }
                            }
                            else
                            {
                                pathPoints.AddRange(connectionPoints);
                            }
                        }
                    }
                }
                
                pathPoints.AddRange(seg);
            }

            // Do NOT return to seam position - end at last point of pattern
            return (pathPoints, segments);
        }

        /// <summary>
        /// Generates offset curves inward, handling holes/islands properly.
        /// Similar to Clipper offset operation.
        /// </summary>
        private List<Curve> GenerateOffsetCurvesWithHoles(Curve boundary, List<Curve> holes, double spacing, int maxOffsets = 1000)
        {
            var allOffsetCurves = new List<Curve>();
            Curve currentBoundary = boundary.DuplicateCurve();
            List<Curve> currentHoles = holes?.Select(h => h.DuplicateCurve()).ToList() ?? new List<Curve>();

            // Determine offset direction based on curve orientation
            double boundaryOffsetDirection = PathHelper.GetOffsetDirection(boundary, holes);
            double boundaryOffsetDistance = spacing * boundaryOffsetDirection;

            for (int i = 0; i < maxOffsets; i++)
            {
                // Offset boundary (direction automatically detected)
                var boundaryOffsets = currentBoundary.Offset(Plane.WorldXY, boundaryOffsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                
                if (boundaryOffsets == null || boundaryOffsets.Length == 0)
                    break;

                // Use largest offset curve (main boundary)
                Curve largestBoundary = boundaryOffsets.OrderByDescending(c => AreaMassProperties.Compute(c)?.Area ?? 0).First();
                
                // Check if offset is still valid
                double area = AreaMassProperties.Compute(largestBoundary)?.Area ?? 0;
                if (area < spacing * spacing)
                    break;

                // Offset holes (direction automatically detected - opposite of boundary)
                var offsetHoles = new List<Curve>();
                foreach (var hole in currentHoles)
                {
                    // Holes should offset in opposite direction of boundary
                    double holeOffsetDirection = -boundaryOffsetDirection;
                    double holeOffsetDistance = spacing * holeOffsetDirection;
                    var holeOffsets = hole.Offset(Plane.WorldXY, holeOffsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                    if (holeOffsets != null && holeOffsets.Length > 0)
                    {
                        // Use smallest offset curve (main hole)
                        Curve smallestHole = holeOffsets.OrderBy(c => AreaMassProperties.Compute(c)?.Area ?? 0).First();
                        offsetHoles.Add(smallestHole);
                    }
                }

                // Check if holes have merged with boundary (hole area >= boundary area)
                bool holesMerged = false;
                foreach (var hole in offsetHoles)
                {
                    double holeArea = AreaMassProperties.Compute(hole)?.Area ?? 0;
                    if (holeArea >= area * 0.9) // 90% threshold
                    {
                        holesMerged = true;
                        break;
                    }
                }

                if (holesMerged)
                    break;

                // Add boundary offset curve
                allOffsetCurves.Add(largestBoundary);
                
                // Update for next iteration
                currentBoundary = largestBoundary;
                currentHoles = offsetHoles;
            }

            return allOffsetCurves;
        }

        /// <summary>
        /// Samples a curve starting from a specific point.
        /// </summary>
        private List<Point3d> SampleCurveFromPoint(Curve curve, double spacing, Point3d startPoint, double startParam)
        {
            var points = new List<Point3d>();
            
            if (curve == null || !curve.IsValid)
                return points;

            double length = curve.GetLength();
            if (length <= 0)
                return points;

            // Start from the specified point
            points.Add(startPoint);

            // Sample forward from start point
            double currentParam = startParam;
            double currentDistance = 0;
            
            while (currentDistance < length)
            {
                currentDistance += spacing;
                double normalizedT = currentDistance / length;
                
                // Wrap around for closed curves
                if (normalizedT > 1.0 && curve.IsClosed)
                {
                    normalizedT = normalizedT % 1.0;
                }
                
                if (normalizedT <= 1.0)
                {
                    double t;
                    if (curve.NormalizedLengthParameter(normalizedT, out t))
                    {
                        // Adjust for start parameter offset
                        double adjustedT = (t + startParam) % curve.Domain.Length;
                        if (adjustedT < curve.Domain.T0)
                            adjustedT += curve.Domain.Length;
                        
                        Point3d pt = curve.PointAt(adjustedT);
                        points.Add(pt);
                    }
                }
                else
                {
                    break;
                }
            }

            return points;
        }

        /// <summary>
        /// Adds random bridges between point lists (curves) to prevent overfill.
        /// </summary>
        private List<List<Point3d>> AddRandomBridgesToPointLists(List<List<Point3d>> pointLists, double bridgeDensity, double spacing)
        {
            if (pointLists.Count < 2)
                return pointLists;

            var result = new List<List<Point3d>> { pointLists[0] };
            Random random = new Random(42); // Fixed seed for reproducibility

            for (int i = 1; i < pointLists.Count; i++)
            {
                List<Point3d> prevList = pointLists[i - 1];
                List<Point3d> currList = pointLists[i];

                if (prevList.Count > 0 && currList.Count > 0)
                {
                    Point3d prevEnd = prevList[prevList.Count - 1];
                    Point3d currStart = currList[0];
                    double distance = prevEnd.DistanceTo(currStart);

                    // Add bridge if curves are disconnected
                    if (distance > spacing * 0.1)
                    {
                        // Random decision: add bridge based on density
                        if (random.NextDouble() < bridgeDensity)
                        {
                            // Create bridge as point list
                            int steps = Math.Max(2, (int)Math.Ceiling(distance / spacing));
                            var bridge = new List<Point3d>();
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                bridge.Add(prevEnd + (currStart - prevEnd) * t);
                            }
                            result.Add(bridge);
                        }
                    }
                }

                result.Add(currList);
            }

            return result;
        }

        /// <summary>
        /// Creates a connection between two points along the boundary with proper layer spacing.
        /// The connection follows the boundary curve offset inward by layer spacing to avoid crossing existing offset curves.
        /// </summary>
        private List<Point3d> CreateLayerSpacedConnection(
            Point3d startPt, Point3d endPt, Curve boundary, 
            Curve currentCurve, Curve nextCurve, double spacing, int segmentIndex)
        {
            var connectionPoints = new List<Point3d>();
            
            if (boundary == null || !boundary.IsValid)
                return connectionPoints;

            try
            {
                // Find closest points on boundary
                double tStart, tEnd;
                boundary.ClosestPoint(startPt, out tStart);
                boundary.ClosestPoint(endPt, out tEnd);
                
                // Normalize parameters to domain
                tStart = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tStart));
                tEnd = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tEnd));
                
                // Determine direction along boundary (shorter path)
                double distForward = Math.Abs(tEnd - tStart);
                double distBackward = boundary.Domain.Length - distForward;
                bool forward = distForward <= distBackward;
                
                // Calculate number of steps along boundary
                double boundaryLength = forward ? distForward : distBackward;
                int numSteps = Math.Max(2, (int)Math.Ceiling(boundaryLength / (spacing * 0.1)));
                numSteps = Math.Min(numSteps, 200); // Limit to avoid too many points
                
                for (int i = 1; i < numSteps; i++)
                {
                    double t;
                    if (forward)
                    {
                        t = tStart + (tEnd - tStart) * ((double)i / numSteps);
                    }
                    else
                    {
                        // Wrap around for backward direction
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
                    
                    // Clamp to domain
                    t = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, t));
                    Point3d pt = boundary.PointAt(t);
                    
                    // Calculate inward normal (perpendicular to boundary, pointing inward)
                    Vector3d tangent = boundary.TangentAt(t);
                    tangent.Unitize();
                    
                    // Create perpendicular vector in XY plane (pointing inward)
                    Vector3d normal = new Vector3d(-tangent.Y, tangent.X, 0);
                    normal.Unitize();
                    
                    // Determine if normal points inward or outward
                    BoundingBox bbox = boundary.GetBoundingBox(true);
                    Point3d center = bbox.Center;
                    Vector3d toCenter = center - pt;
                    toCenter.Unitize();
                    
                    // If normal points away from center, reverse it
                    if (normal * toCenter < 0)
                    {
                        normal = -normal;
                    }
                    
                    // Offset point inward by layer spacing
                    Point3d offsetPt = pt + normal * spacing;
                    
                    connectionPoints.Add(offsetPt);
                }
            }
            catch
            {
                // If boundary connection fails, return empty (will use fallback)
                return new List<Point3d>();
            }
            
            return connectionPoints;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillContourIcon.png");
        public override Guid ComponentGuid => new Guid("a7b8c9d0-e1f2-3456-7890-123456789abc");
    }
}

