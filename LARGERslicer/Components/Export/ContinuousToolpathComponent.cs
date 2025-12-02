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
    /// Continuous Toolpath - Generates a single continuous 3D printing path from boundary curve.
    /// Handles complex geometries with undercuts (hinterschneidungen) and self-intersections.
    /// Based on Nautilus plugin approach: uses offset curves with self-intersection handling.
    /// Similar to Laurent Delrieu's implementation with Euler-Cycle approach for path healing.
    /// </summary>
    public class ContinuousToolpathComponent : BottomLayerPatternBase
    {
        public ContinuousToolpathComponent()
            : base("Continuous Toolpath", "ContToolpath",
                  "Generates a single continuous 3D printing path from boundary curve. Handles undercuts (hinterschneidungen) and self-intersections using offset curves with automatic path healing. Similar to Nautilus plugin.")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Random Bridges", "Random", "Use random bridge placement between offset curves to prevent overfill.", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Bridge Density", "BridgeD", "Density of bridges between curves (0-1). Higher = more bridges.", GH_ParamAccess.item, 0.3);
            pManager.AddBooleanParameter("Handle Undercuts", "Undercuts", "Automatically handle self-intersections in offset curves (undercuts/hinterschneidungen).", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Use ArcWelder", "ArcWeld", "Convert polyline to lines and arcs for optimized GCode.", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Arc Tolerance", "ArcTol", "Tolerance for arc fitting when ArcWelder is enabled (mm).", GH_ParamAccess.item, 0.1);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate inputs using base class method
            if (!ValidateInputs(DA, out Curve boundary, out Point3d seamPoint, out double spacing, out double boundaryOffset, out List<Curve> holes))
                return;

            bool randomBridges = true;
            double bridgeDensity = 0.3;
            bool handleUndercuts = true;
            bool useArcWelder = false;
            double arcTolerance = 0.1;
            DA.GetData(5, ref randomBridges);
            DA.GetData(6, ref bridgeDensity);
            DA.GetData(7, ref handleUndercuts);
            DA.GetData(8, ref useArcWelder);
            DA.GetData(9, ref arcTolerance);

            // IMPORTANT: Offset boundary by layer width (spacing) inward
            double totalBoundaryOffset = spacing + boundaryOffset;

            // Prepare boundary with offset
            Curve closedBoundary = PrepareBoundary(boundary, totalBoundaryOffset, out List<Curve> offsetHoles, holes);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate if not provided)
            Point3d? seamPointNullable = seamPoint.IsValid ? (Point3d?)seamPoint : null;
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, seamPointNullable);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, randomBridges, bridgeDensity, handleUndercuts, holes);

            if (pathPoints == null || pathPoints.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Pattern generation resulted in insufficient points.");
                return;
            }

            // Apply ArcWelder conversion if enabled
            Curve pathCurve = null;
            List<Curve> segmentCurves = new List<Curve>();

            if (useArcWelder)
            {
                var optimizedCurves = ArcWelderHelper.ConvertToLinesAndArcs(pathPoints, arcTolerance, 0.1);
                
                if (optimizedCurves.Count > 0)
                {
                    var joined = Curve.JoinCurves(optimizedCurves, 0.01);
                    if (joined != null && joined.Length > 0)
                    {
                        pathCurve = joined[0];
                    }
                    else
                    {
                        pathCurve = optimizedCurves[0];
                    }
                    segmentCurves = optimizedCurves;
                }
                else
                {
                    Polyline pathPolyline = new Polyline(pathPoints);
                    if (pathPolyline.IsValid)
                    {
                        pathCurve = new PolylineCurve(pathPolyline);
                    }
                }
            }
            else
            {
                CreateOutputCurves(pathPoints, segments, out pathCurve, out segmentCurves);
            }

            if (pathCurve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create output curve.");
                return;
            }

            // Calculate statistics
            string stats = CalculateStatistics(pathPoints, closedBoundary, spacing);
            if (useArcWelder && segmentCurves.Count > 0)
            {
                string arcStats = ArcWelderHelper.GetConversionStats(pathPoints, segmentCurves);
                stats += $" | {arcStats}";
            }
            if (handleUndercuts)
            {
                stats += " | Undercuts handled";
            }

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
            bool randomBridges,
            double bridgeDensity,
            bool handleUndercuts,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Step 1: Generate offset curves with undercut handling
            List<Curve> offsetCurves;
            if (handleUndercuts)
            {
                // Use advanced offset with self-intersection handling (direction auto-detected)
                offsetCurves = SelfIntersectionHelper.GenerateOffsetCurvesWithUndercutHandling(
                    boundary, spacing, 1000, 0.01, holes);
            }
            else
            {
                // Standard offset (may fail with undercuts) - direction automatically detected
                offsetCurves = PathHelper.GenerateOffsetCurves(boundary, spacing, 1000, holes);
            }

            if (offsetCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Process each offset curve to ensure no self-intersections
            var processedCurves = new List<Curve>();
            foreach (var curve in offsetCurves)
            {
                if (handleUndercuts)
                {
                    // Suppress any remaining self-intersections
                    var healed = SelfIntersectionHelper.SuppressSelfIntersections(curve, 0.01, false);
                    processedCurves.AddRange(healed);
                }
                else
                {
                    processedCurves.Add(curve.DuplicateCurve());
                }
            }

            // Step 3: Filter curves that are inside holes
            var validCurves = new List<Curve>();
            foreach (var curve in processedCurves)
            {
                // Check if curve is valid (not inside holes)
                Point3d midPt = curve.PointAt(0.5);
                if (IsPointValid(midPt, boundary, holes))
                {
                    validCurves.Add(curve);
                }
            }

            if (validCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 4: Optimize curve order starting from seam position
            var orderedCurves = PathHelper.OptimizeCurveOrder(validCurves, seamPosition, seamPosition);

            // Store ordered curves for connection generation
            // (We'll use this in CreateLayerSpacedConnection)

            // Step 5: Sample points from each curve
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

            // Step 6: Add random bridges between curves (if enabled)
            if (randomBridges && allCurvePoints.Count > 1)
            {
                allCurvePoints = AddRandomBridgesToPointLists(allCurvePoints, bridgeDensity, spacing);
            }

            // Step 7: Build continuous path with proper layer-spaced connections
            segments = allCurvePoints;

            pathPoints.Add(seamPosition);
            for (int segIdx = 0; segIdx < segments.Count; segIdx++)
            {
                var seg = segments[segIdx];
                if (pathPoints.Count > 0 && seg.Count > 0)
                {
                    Point3d lastPt = pathPoints[pathPoints.Count - 1];
                    Point3d firstPt = seg[0];
                    double distance = lastPt.DistanceTo(firstPt);
                    
                    if (distance > spacing * 0.1)
                    {
                        // Create connection along boundary with proper layer spacing
                        // Find the corresponding curves for context
                        Curve currentCurve = segIdx > 0 && segIdx - 1 < orderedCurves.Count ? orderedCurves[segIdx - 1] : null;
                        Curve nextCurve = segIdx < orderedCurves.Count ? orderedCurves[segIdx] : null;
                        
                        var connectionPoints = CreateLayerSpacedConnection(
                            lastPt, firstPt, boundary, currentCurve, nextCurve, spacing, segIdx);
                        
                        if (connectionPoints.Count > 0)
                        {
                            pathPoints.AddRange(connectionPoints);
                        }
                        else
                        {
                            // Fallback: simple linear connection with proper spacing
                            int steps = Math.Max(2, (int)Math.Ceiling(distance / spacing));
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                            }
                        }
                    }
                }
                pathPoints.AddRange(seg);
            }

            return (pathPoints, segments);
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
            double currentDistance = 0;
            double currentParam = startParam;
            
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
                        if (pt.DistanceTo(points[points.Count - 1]) > spacing * 0.1)
                        {
                            points.Add(pt);
                        }
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
                // Use spacing as step size to maintain layer width
                double boundaryLength = forward ? distForward : distBackward;
                int numSteps = Math.Max(2, (int)Math.Ceiling(boundaryLength / (spacing * 0.1))); // Sample more densely
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
                    // Check by comparing with direction from boundary center
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

                    if (distance > spacing * 0.1)
                    {
                        if (random.NextDouble() < bridgeDensity)
                        {
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousToolpathIcon.png");
        public override Guid ComponentGuid => new Guid("c606afee-06f0-46bf-a5bc-318637743abc");
    }
}

