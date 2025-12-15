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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate minimal common inputs
            Curve boundary;
            double printWidth;
            List<Curve> holes;

            if (!ValidateInputs(DA, out boundary, out printWidth, out holes))
                return;

            // Get additional pattern-specific parameters
            bool randomBridges = true;
            double bridgeDensity = 0.3;
            bool handleUndercuts = true;
            DA.GetData(3, ref randomBridges);  // Index 3 after base inputs (0-2)
            DA.GetData(4, ref bridgeDensity);
            DA.GetData(5, ref handleUndercuts);

            if (bridgeDensity < 0 || bridgeDensity > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Bridge density should be between 0 and 1. Clamping.");
                bridgeDensity = Math.Max(0, Math.Min(1, bridgeDensity));
            }

            double spacing = printWidth;
            double boundaryOffset = 0.0;

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, randomBridges, bridgeDensity, handleUndercuts, holes);

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

            // Separate closed and open patterns
            var patternsClosed = new List<Curve>();
            var patternsOpened = new List<Curve>();
            var bridges = new List<Curve>();
            var polylines = new List<Curve>();

            // Add main path as polyline
            if (pathCurve != null)
            {
                polylines.Add(pathCurve);
            }

            // Categorize segments
            foreach (var seg in segmentCurves)
            {
                if (seg != null && seg.IsValid)
                {
                    if (seg.IsClosed)
                        patternsClosed.Add(seg);
                    else
                        patternsOpened.Add(seg);
                }
            }

            // Create planes for each segment
            var planes = new List<Plane>();
            foreach (var seg in segmentCurves)
            {
                if (seg != null && seg.IsValid && seg.PointAtStart.IsValid)
                {
                    Point3d pt = seg.PointAtStart;
                    planes.Add(new Plane(pt, Vector3d.ZAxis));
                }
            }

            // Set outputs according to new structure
            DA.SetDataList(0, polylines);  // Polylines
            DA.SetDataList(1, planes);      // Planes
            DA.SetDataList(2, patternsClosed);  // Patterns Closed
            DA.SetDataList(3, patternsOpened);  // Patterns Opened
            DA.SetDataList(4, bridges);     // Bridges
            DA.SetData(5, pathCurve);      // Single Line Fill
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

            // Step 4: Sort curves from outside to inside (by area, largest first)
            // This ensures we process outer curves first, then move inward
            // CRITICAL: Prevents path from ending in middle and going back outward
            var sortedByArea = validCurves.OrderByDescending(c =>
            {
                try
                {
                    var area = AreaMassProperties.Compute(c);
                    return area != null ? area.Area : 0;
                }
                catch
                {
                    return 0;
                }
            }).ToList();

            // Step 5: Optimize curve order starting from seam position (within each area group)
            // Group curves by similar area to maintain outside-to-inside order
            List<Curve> connections;
            var orderedCurves = PathHelper.OptimizeCurveOrder(sortedByArea, seamPosition, out connections);

            // Store ordered curves for connection generation
            // (We'll use this in CreateLayerSpacedConnection)

            // Step 6: Sample points from each curve
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

            // Step 7: Add random bridges between curves (if enabled)
            if (randomBridges && allCurvePoints.Count > 1)
            {
                allCurvePoints = AddRandomBridgesToPointLists(allCurvePoints, bridgeDensity, spacing);
            }

            // Step 8: Build continuous path with proper layer-spaced connections
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
                        // CRITICAL: Use direct connection for short distances to avoid crossing
                        // Only use boundary-following for longer jumps between distant regions
                        bool useDirectConnection = distance < spacing * 5.0;
                        
                        if (useDirectConnection)
                        {
                            // Direct linear connection (shortest path, no crossing)
                            int steps = Math.Max(2, (int)Math.Ceiling(distance / (spacing * 0.5)));
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                            }
                        }
                        else
                        {
                            // Long jump: use offset-following connection (geometric consistency)
                            Curve currentCurve = segIdx > 0 && segIdx - 1 < orderedCurves.Count ? orderedCurves[segIdx - 1] : null;
                            Curve nextCurve = segIdx < orderedCurves.Count ? orderedCurves[segIdx] : null;
                            
                            // Use offset-following connection to maintain geometric consistency
                            var connectionPoints = PathHelper.CreateOffsetFollowingConnection(
                                lastPt, firstPt, boundary, currentCurve, nextCurve, spacing);
                            
                            // Fallback to layer-spaced connection if offset connection fails
                            if (connectionPoints.Count == 0)
                            {
                                connectionPoints = CreateLayerSpacedConnection(
                                    lastPt, firstPt, boundary, currentCurve, nextCurve, spacing, segIdx);
                            }
                            
                            if (connectionPoints.Count > 0)
                            {
                                pathPoints.AddRange(connectionPoints);
                            }
                            else
                            {
                                // Fallback: simple linear connection
                                int steps = Math.Max(2, (int)Math.Ceiling(distance / spacing));
                                for (int s = 1; s < steps; s++)
                                {
                                    double t = (double)s / steps;
                                    pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                                }
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
        /// Adds optimized bridges between point lists using nearest-neighbor approach.
        /// Improved from random bridges: uses greedy nearest-neighbor to minimize travel distance.
        /// For true Eulerian path, would need full graph algorithm - this is a practical approximation.
        /// </summary>
        private List<List<Point3d>> AddRandomBridgesToPointLists(List<List<Point3d>> pointLists, double bridgeDensity, double spacing)
        {
            if (pointLists.Count < 2)
                return pointLists;

            // Improved approach: Use nearest-neighbor ordering instead of sequential
            var result = new List<List<Point3d>>();
            var remaining = new List<List<Point3d>>(pointLists);
            
            // Start with first list
            result.Add(remaining[0]);
            Point3d currentEnd = remaining[0][remaining[0].Count - 1];
            remaining.RemoveAt(0);
            
            // Greedily connect to nearest remaining segment
            while (remaining.Count > 0)
            {
                int nearestIdx = -1;
                double minDist = double.MaxValue;
                bool reverseNearest = false;
                
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (remaining[i].Count == 0) continue;
                    
                    double distToStart = currentEnd.DistanceTo(remaining[i][0]);
                    double distToEnd = currentEnd.DistanceTo(remaining[i][remaining[i].Count - 1]);
                    
                    if (distToStart < minDist)
                    {
                        minDist = distToStart;
                        nearestIdx = i;
                        reverseNearest = false;
                    }
                    if (distToEnd < minDist)
                    {
                        minDist = distToEnd;
                        nearestIdx = i;
                        reverseNearest = true;
                    }
                }
                
                if (nearestIdx >= 0)
                {
                    var nextList = remaining[nearestIdx];
                    remaining.RemoveAt(nearestIdx);
                    
                    if (reverseNearest)
                    {
                        nextList.Reverse();
                    }
                    
                    // Add bridge based on density threshold
                    Point3d nextStart = nextList[0];
                    if (minDist > spacing * 0.1)
                    {
                        // Use distance-based decision instead of random
                        // Short gaps: always bridge, long gaps: use density parameter
                        bool shouldBridge = (minDist < spacing * 3.0) || 
                                           (minDist / spacing) * 0.1 < bridgeDensity;
                        
                        if (shouldBridge)
                        {
                            int steps = Math.Max(2, (int)Math.Ceiling(minDist / (spacing * 0.5)));
                            var bridge = new List<Point3d>();
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                bridge.Add(currentEnd + (nextStart - currentEnd) * t);
                            }
                            result.Add(bridge);
                        }
                    }
                    
                    result.Add(nextList);
                    currentEnd = nextList[nextList.Count - 1];
                }
                else
                {
                    break; // No more segments
                }
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousToolpathIcon.png");
        public override Guid ComponentGuid => new Guid("c606afee-06f0-46bf-a5bc-318637743abc");
    }
}

