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
    /// Continuous Path from Boundary - Generates a single continuous 3D printing path from a boundary curve.
    /// Automatically generates infill curves, offsets boundary by layer width, trims infill at boundary,
    /// and combines all segments into one continuous path. Similar to Nautilus plugin functionality.
    /// </summary>
    public class ContinuousPathFromCurvesComponent : BottomLayerPatternBase
    {
        public ContinuousPathFromCurvesComponent()
            : base("Continuous Path from Boundary", "ContPath",
                  "Generates a single continuous 3D printing path from a boundary curve. Automatically creates infill, offsets boundary by layer width, trims at intersections, and optimizes order for minimal travel.")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Angle", "Angle", "Infill line direction angle in degrees (0 = horizontal lines)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Random Bridges", "Random", "Use random bridge placement between disconnected segments to prevent overfill.", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Bridge Density", "BridgeD", "Density of bridges between segments (0-1). Higher = more bridges.", GH_ParamAccess.item, 0.3);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate inputs using base class method
            if (!ValidateInputs(DA, out Curve boundary, out Point3d seamPoint, out double spacing, out double boundaryOffset, out List<Curve> holes))
                return;

            double angle = 0.0;
            bool randomBridges = true;
            double bridgeDensity = 0.3;
            DA.GetData(5, ref angle);
            DA.GetData(6, ref randomBridges);
            DA.GetData(7, ref bridgeDensity);

            // IMPORTANT: Offset boundary by layer width (spacing) inward
            // This accounts for the fact that the boundary itself has an extrusion width
            // The boundaryOffset parameter is for additional offset (e.g., for first layer adhesion)
            double totalBoundaryOffset = spacing + boundaryOffset;

            // Prepare boundary with offset (boundary offset = layer width + additional offset, direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, totalBoundaryOffset, out List<Curve> offsetHoles, holes);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate if not provided)
            Point3d? seamPointNullable = seamPoint.IsValid ? (Point3d?)seamPoint : null;
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, seamPointNullable);

            // Convert angle to radians
            angle = angle * Math.PI / 180.0;

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, angle, randomBridges, bridgeDensity, holes);

            if (pathPoints == null || pathPoints.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pattern generation resulted in insufficient points. Count: {pathPoints?.Count ?? 0}");
                return;
            }

            // Create output curves using base class method
            CreateOutputCurves(pathPoints, segments, out Curve pathCurve, out List<Curve> segmentCurves);

            if (pathCurve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to create output curve. Path points: {pathPoints.Count}, Segments: {segments?.Count ?? 0}");
                return;
            }

            // Calculate statistics using base class method
            string stats = CalculateStatistics(pathPoints, closedBoundary, spacing, $"Angle={angle * 180.0 / Math.PI:F1}°");

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
            double angle,
            bool randomBridges,
            double bridgeDensity,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Step 1: Generate infill lines that cover the entire bounding box
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d centerPoint = bbox.Center;

            // Generate parallel infill lines (rotated around center point)
            // These lines extend beyond the boundary
            var infillLines = PathHelper.GenerateParallelLines(boundary, spacing, angle, centerPoint, bbox);

            if (infillLines.Count == 0)
            {
                // Fallback: just return seam position
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Trim infill lines at boundary and find intersection points
            // Also segment the boundary at intersection points
            var trimmedInfillSegments = new List<Curve>();
            var boundarySegments = new List<Curve>();
            var allIntersectionPoints = new List<Point3d>();

            // Find all intersections between infill lines and boundary
            foreach (var infillLine in infillLines)
            {
                var intersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                    infillLine, boundary, 0.01, 0.01);

                if (intersections != null && intersections.Count > 0)
                {
                    // Sort intersection parameters along the infill line
                    var intersectionParams = intersections
                        .Select(ix => ix.ParameterA)
                        .OrderBy(t => t)
                        .ToList();

                    // Trim infill line at boundary intersections
                    for (int i = 0; i < intersectionParams.Count - 1; i += 2)
                    {
                        double t1 = intersectionParams[i];
                        double t2 = intersectionParams[i + 1];

                        // Only create segment if it's long enough
                        if (Math.Abs(t2 - t1) > spacing * 0.1)
                        {
                            Curve trimmed = infillLine.Trim(t1, t2);
                            if (trimmed != null && trimmed.IsValid && trimmed.GetLength() > spacing * 0.1)
                            {
                                // Filter points that are inside holes
                                Point3d midPt = trimmed.PointAt(0.5);
                                if (IsPointValid(midPt, boundary, holes))
                                {
                                    trimmedInfillSegments.Add(trimmed);
                                }
                            }
                        }
                    }

                    // Collect intersection points for boundary segmentation
                    foreach (var intersection in intersections)
                    {
                        Point3d pt = infillLine.PointAt(intersection.ParameterA);
                        allIntersectionPoints.Add(pt);
                    }
                }
            }

            // Step 3: Segment boundary at intersection points
            if (allIntersectionPoints.Count > 0)
            {
                // Find intersection parameters on boundary
                var boundaryIntersectionParams = new List<double>();
                foreach (var intersectionPt in allIntersectionPoints)
                {
                    double t;
                    if (boundary.ClosestPoint(intersectionPt, out t))
                    {
                        double dist = boundary.PointAt(t).DistanceTo(intersectionPt);
                        if (dist <= 0.01)
                        {
                            boundaryIntersectionParams.Add(t);
                        }
                    }
                }

                // Add start and end points
                boundaryIntersectionParams.Add(boundary.Domain.T0);
                boundaryIntersectionParams.Add(boundary.Domain.T1);

                // Sort and remove duplicates
                boundaryIntersectionParams = boundaryIntersectionParams.Distinct().OrderBy(t => t).ToList();

                // Create boundary segments
                for (int i = 0; i < boundaryIntersectionParams.Count - 1; i++)
                {
                    double t1 = boundaryIntersectionParams[i];
                    double t2 = boundaryIntersectionParams[i + 1];

                    // Handle wrap-around for closed curves
                    if (i == boundaryIntersectionParams.Count - 2 && boundary.IsClosed)
                    {
                        // Last segment wraps around
                        Curve seg1 = boundary.Trim(t1, boundary.Domain.T1);
                        Curve seg2 = boundary.Trim(boundary.Domain.T0, t2);
                        if (seg1 != null && seg1.IsValid && seg1.GetLength() > spacing * 0.1)
                        {
                            boundarySegments.Add(seg1);
                        }
                        if (seg2 != null && seg2.IsValid && seg2.GetLength() > spacing * 0.1)
                        {
                            boundarySegments.Add(seg2);
                        }
                    }
                    else
                    {
                        Curve segment = boundary.Trim(t1, t2);
                        if (segment != null && segment.IsValid && segment.GetLength() > spacing * 0.1)
                        {
                            boundarySegments.Add(segment);
                        }
                    }
                }
            }
            else
            {
                // No intersections, use entire boundary
                boundarySegments.Add(boundary.DuplicateCurve());
            }

            // Step 4: Combine all segments (infill + boundary) into one list
            var allSegments = new List<Curve>();
            allSegments.AddRange(trimmedInfillSegments);
            allSegments.AddRange(boundarySegments);

            if (allSegments.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 5: Optimize segment order for minimal travel (greedy nearest-neighbor)
            var orderedSegments = OptimizeSegmentOrder(allSegments, seamPosition);

            // Step 6: Add random bridges between disconnected segments (if enabled)
            if (randomBridges)
            {
                orderedSegments = AddRandomBridges(orderedSegments, bridgeDensity, spacing);
            }

            // Step 7: Convert segments to point lists and create continuous path
            var segmentPointLists = new List<List<Point3d>>();
            foreach (var seg in orderedSegments)
            {
                var segPoints = PathHelper.SampleCurve(seg, spacing * 0.5, true);
                if (segPoints.Count >= 2)
                {
                    segmentPointLists.Add(segPoints);
                }
            }

            segments = segmentPointLists;

            // Step 8: Build continuous path starting from seam position
            pathPoints.Add(seamPosition);
            
            if (segments.Count == 0)
            {
                // If no segments, create a minimal path with at least 2 points
                // Add a point slightly offset from seam position
                Vector3d offset = new Vector3d(spacing * 0.1, 0, 0);
                pathPoints.Add(seamPosition + offset);
                segments.Add(new List<Point3d> { seamPosition, seamPosition + offset });
            }
            else
            {
                foreach (var seg in segments)
                {
                    if (pathPoints.Count > 0 && seg.Count > 0)
                    {
                        Point3d lastPt = pathPoints[pathPoints.Count - 1];
                        Point3d firstPt = seg[0];
                        if (lastPt.DistanceTo(firstPt) > spacing * 0.1)
                        {
                            int steps = Math.Max(2, (int)Math.Ceiling(lastPt.DistanceTo(firstPt) / spacing));
                            for (int s = 1; s < steps; s++)
                            {
                                double t = (double)s / steps;
                                pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                            }
                        }
                    }
                    pathPoints.AddRange(seg);
                }
            }

            // Ensure we have at least 2 points
            if (pathPoints.Count < 2)
            {
                // Add a second point if we only have one
                Vector3d offset = new Vector3d(spacing * 0.1, 0, 0);
                pathPoints.Add(seamPosition + offset);
            }

            // Do NOT return to seam position - end at last point of pattern
            // This prevents unwanted retraction/travel moves across the geometry

            return (pathPoints, segments);
        }

        /// <summary>
        /// Optimizes segment order to minimize travel distance (greedy nearest-neighbor).
        /// </summary>
        private List<Curve> OptimizeSegmentOrder(List<Curve> segments, Point3d startPoint)
        {
            if (segments.Count == 0)
                return segments;

            var ordered = new List<Curve>();
            var remaining = new HashSet<Curve>(segments);
            Point3d currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                Curve bestSegment = null;
                double bestDistance = double.MaxValue;
                bool reverseBest = false;

                foreach (var segment in remaining)
                {
                    Point3d segStart = segment.PointAtStart;
                    Point3d segEnd = segment.PointAtEnd;

                    double distToStart = currentPoint.DistanceTo(segStart);
                    double distToEnd = currentPoint.DistanceTo(segEnd);

                    if (distToStart < bestDistance)
                    {
                        bestDistance = distToStart;
                        bestSegment = segment;
                        reverseBest = false;
                    }

                    if (distToEnd < bestDistance)
                    {
                        bestDistance = distToEnd;
                        bestSegment = segment;
                        reverseBest = true;
                    }
                }

                if (bestSegment != null)
                {
                    Curve segmentToAdd = bestSegment.DuplicateCurve();
                    if (reverseBest)
                    {
                        segmentToAdd.Reverse();
                    }
                    ordered.Add(segmentToAdd);
                    remaining.Remove(bestSegment);
                    currentPoint = segmentToAdd.PointAtEnd;
                }
                else
                {
                    // Fallback: add remaining segments
                    ordered.AddRange(remaining.Select(s => s.DuplicateCurve()));
                    break;
                }
            }

            return ordered;
        }

        /// <summary>
        /// Adds random bridges between disconnected segments to prevent overfill.
        /// </summary>
        private List<Curve> AddRandomBridges(List<Curve> segments, double bridgeDensity, double spacing)
        {
            if (segments.Count < 2)
                return segments;

            var result = new List<Curve> { segments[0] };
            Random random = new Random(42); // Fixed seed for reproducibility

            for (int i = 1; i < segments.Count; i++)
            {
                Curve prevSeg = segments[i - 1];
                Curve currSeg = segments[i];

                Point3d prevEnd = prevSeg.PointAtEnd;
                Point3d currStart = currSeg.PointAtStart;
                double distance = prevEnd.DistanceTo(currStart);

                // Add bridge if segments are disconnected
                if (distance > spacing * 0.1)
                {
                    // Random decision: add bridge based on density
                    if (random.NextDouble() < bridgeDensity)
                    {
                        // Create bridge curve (straight line)
                        Line bridgeLine = new Line(prevEnd, currStart);
                        Curve bridge = new LineCurve(bridgeLine);
                        result.Add(bridge);
                    }
                }

                result.Add(currSeg);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousPathFromCurvesIcon.png");
        public override Guid ComponentGuid => new Guid("3f6dfdae-b1a3-4a7e-b8b3-981ee021245f");
    }
}
