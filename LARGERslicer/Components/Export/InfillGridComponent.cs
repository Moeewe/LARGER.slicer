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
    /// Infill Grid - Generates rectangular grid/zigzag fill pattern.
    /// Pattern B: Rectangular Grid/Zigzag
    /// </summary>
    public class InfillGridComponent : BottomLayerPatternBase
    {
        public InfillGridComponent()
            : base("Single Line Fill with Zigzags", "SLF Zigzags",
                  "Generates rectangular grid/zigzag fill pattern. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Spacing Y", "SpacingY", "Line spacing in Y direction (perpendicular to lines, mm)", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Angle", "Angle", "Grid rotation angle in degrees around center point (0 = horizontal lines)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Start Left", "StartLeft", "True to start from left, False to start from right", GH_ParamAccess.item, true);
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
            double spacingY = 2.0;
            double angle = 0.0;
            bool startLeft = true;
            DA.GetData(3, ref spacingY);  // Index 3 after base inputs (0-2)
            DA.GetData(4, ref angle);
            DA.GetData(5, ref startLeft);

            if (spacingY <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing Y must be greater than zero.");
                return;
            }

            double spacing = printWidth;
            double boundaryOffset = 0.0;

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacingY);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Convert angle to radians
            angle = angle * Math.PI / 180.0;

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, spacingY, angle, startLeft, holes);

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
            double spacingY,
            double angle,
            bool startLeft,
            List<Curve> holes)
        {

            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Get boundary bounding box and center point
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d centerPoint = bbox.Center;

            // Generate parallel lines (rotated around center point)
            var lines = PathHelper.GenerateParallelLines(boundary, spacingY, angle, centerPoint, bbox);

            if (lines.Count == 0)
            {
                // Fallback: just return seam position
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Handle undercuts: Trim lines at boundary and check for self-intersections
            var trimmedLines = new List<Curve>();
            foreach (var line in lines)
            {
                if (line == null || !line.IsValid)
                    continue;

                // Trim line at boundary intersections
                var intersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(line, boundary, 0.01, 0.01);
                
                if (intersections != null && intersections.Count > 0)
                {
                    // Sort intersection parameters
                    var intersectionParams = intersections.Select(ix => ix.ParameterA).OrderBy(t => t).ToList();
                    
                    // Create segments between intersections (inside boundary)
                    // Process pairs of intersections, ensuring we don't go out of bounds
                    for (int i = 0; i + 1 < intersectionParams.Count; i += 2)
                    {
                        double t1 = intersectionParams[i];
                        double t2 = intersectionParams[i + 1];
                        
                        if (Math.Abs(t2 - t1) > 0.01)
                        {
                            try
                            {
                                Curve trimmed = line.Trim(t1, t2);
                                if (trimmed != null && trimmed.IsValid && trimmed.GetLength() > spacing * 0.1)
                                {
                                    // Check for self-intersections (undercuts) with larger tolerance
                                    var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(trimmed, Math.Max(0.01, spacing * 0.01));
                                    if (selfIntersections == null || selfIntersections.Count == 0)
                                    {
                                        trimmedLines.Add(trimmed);
                                    }
                                }
                            }
                            catch
                            {
                                // Skip invalid trim operation
                                continue;
                            }
                        }
                    }
                }
                else
                {
                    // No intersections - check if line is inside boundary
                    Point3d midPt = line.PointAt(0.5);
                    if (boundary.Contains(midPt, Plane.WorldXY, 0.01) == PointContainment.Inside)
                    {
                        // Check for self-intersections
                        var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(line, 0.01);
                        if (selfIntersections == null || selfIntersections.Count == 0)
                        {
                            trimmedLines.Add(line.DuplicateCurve());
                        }
                        else
                        {
                            var healed = SelfIntersectionHelper.SuppressSelfIntersections(line, 0.01, false);
                            trimmedLines.AddRange(healed);
                        }
                    }
                }
            }

            // Replace original lines with trimmed lines
            lines = trimmedLines;

            // Sort lines by perpendicular distance from center
            Vector3d direction = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            Vector3d perp = new Vector3d(-direction.Y, direction.X, 0);

            var sortedLines = lines.OrderBy(line =>
            {
                Point3d midPt = (line.PointAtStart + line.PointAtEnd) * 0.5;
                return Vector3d.Multiply(midPt - centerPoint, perp);
            }).ToList();

            // Generate boustrophedon pattern (zigzag)
            var allSegments = new List<List<Point3d>>();

            for (int row = 0; row < sortedLines.Count; row++)
            {
                Curve line = sortedLines[row];
                bool forward = (startLeft && (row % 2 == 0)) || (!startLeft && (row % 2 == 1));

                // Sample points along line
                var linePoints = PathHelper.SampleCurve(line, spacing * 0.5, true);
                
                // Filter points that are inside holes
                if (holes != null && holes.Count > 0)
                {
                    linePoints = linePoints.Where(pt => IsPointValid(pt, boundary, holes)).ToList();
                }
                
                if (!forward)
                {
                    linePoints.Reverse();
                }

                if (linePoints.Count >= 2)
                {
                    allSegments.Add(linePoints);
                }

                // Connect to next row if not last
                if (row < sortedLines.Count - 1)
                {
                    Curve nextLine = sortedLines[row + 1];
                    bool nextForward = (startLeft && ((row + 1) % 2 == 0)) || (!startLeft && ((row + 1) % 2 == 1));

                    Point3d currentEnd = forward ? line.PointAtEnd : line.PointAtStart;
                    Point3d nextStart = nextForward ? nextLine.PointAtStart : nextLine.PointAtEnd;

                    // Find closest point on next line
                    double minDist = double.MaxValue;
                    Point3d closestNext = nextStart;
                    var nextLinePoints = PathHelper.SampleCurve(nextLine, spacing * 0.1, true);
                    foreach (var pt in nextLinePoints)
                    {
                        double dist = currentEnd.DistanceTo(pt);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestNext = pt;
                        }
                    }

                    // Create 90° U-turn connection (critical for clean boustrophedon)
                    if (currentEnd.DistanceTo(closestNext) > spacing * 0.1)
                    {
                        var connection = PathHelper.Create90DegreeConnection(
                            currentEnd, closestNext, line, nextLine, spacing);
                        
                        if (connection.Count > 0)
                        {
                            allSegments.Add(connection);
                        }
                    }
                }
            }

            // Optimize order to start from seam position
            if (allSegments.Count > 0)
            {
                // Find segment closest to seam position
                int startSegIdx = 0;
                double minStartDist = double.MaxValue;
                for (int i = 0; i < allSegments.Count; i++)
                {
                    if (allSegments[i].Count > 0)
                    {
                        double dist1 = seamPosition.DistanceTo(allSegments[i][0]);
                        double dist2 = seamPosition.DistanceTo(allSegments[i][allSegments[i].Count - 1]);
                        double minDist = Math.Min(dist1, dist2);
                        if (minDist < minStartDist)
                        {
                            minStartDist = minDist;
                            startSegIdx = i;
                            if (dist2 < dist1)
                            {
                                allSegments[i].Reverse();
                            }
                        }
                    }
                }

                // Reorder segments
                var reorderedSegments = new List<List<Point3d>>();
                for (int i = startSegIdx; i < allSegments.Count; i++)
                    reorderedSegments.Add(allSegments[i]);
                for (int i = 0; i < startSegIdx; i++)
                    reorderedSegments.Add(allSegments[i]);

                segments = reorderedSegments;
            }
            else
            {
                segments = allSegments;
            }

            // Build continuous path starting from seam position
            // Find segment closest to seam position to start from
            if (segments.Count > 0)
            {
                int startIdx = 0;
                double minDist = double.MaxValue;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i].Count > 0)
                    {
                        double dist1 = seamPosition.DistanceTo(segments[i][0]);
                        double dist2 = seamPosition.DistanceTo(segments[i][segments[i].Count - 1]);
                        double minSegDist = Math.Min(dist1, dist2);
                        if (minSegDist < minDist)
                        {
                            minDist = minSegDist;
                            startIdx = i;
                            // Reverse if end is closer
                            if (dist2 < dist1)
                            {
                                segments[i].Reverse();
                            }
                        }
                    }
                }

                // Reorder segments to start from closest to seam
                var reordered = new List<List<Point3d>>();
                for (int i = startIdx; i < segments.Count; i++)
                    reordered.Add(segments[i]);
                for (int i = 0; i < startIdx; i++)
                    reordered.Add(segments[i]);
                segments = reordered;
            }

            // Start path from seam position
            pathPoints.Add(seamPosition);
            
            // Add segments with connections
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Count > 0)
                {
                    Point3d firstPt = seg[0];
                    
                    // Connect from previous segment end (or seam position for first segment) along boundary
                    if (pathPoints.Count > 0)
                    {
                        Point3d lastPt = pathPoints[pathPoints.Count - 1];
                        if (lastPt.DistanceTo(firstPt) > spacing * 0.1)
                        {
                            var connection = CreateLayerSpacedConnection(
                                lastPt, firstPt, boundary, spacing);
                            
                            if (connection.Count > 0)
                            {
                                pathPoints.AddRange(connection);
                            }
                            else
                            {
                                // Fallback: simple linear connection with proper spacing
                        int steps = Math.Max(2, (int)Math.Ceiling(lastPt.DistanceTo(firstPt) / spacing));
                        for (int s = 1; s < steps; s++)
                        {
                            double t = (double)s / steps;
                            pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                        }
                    }
                }
                    }
                    
                    // Add segment points
                pathPoints.AddRange(seg);
                }
            }

            // IMPORTANT: Do NOT add a final connection back to seam position
            // The path should end at the last point of the last segment
            // This prevents the unwanted crossing path through the entire component

            return (pathPoints, segments);
        }

        /// <summary>
        /// Creates a U-turn connection for boustrophedon pattern.
        /// Uses a smooth half-circle or similar turnaround to connect adjacent rows.
        /// Maintains proper spacing to avoid overlap at turns.
        /// </summary>
        private List<Point3d> CreateUTurnConnection(
            Point3d startPt, Point3d endPt, Curve boundary, double spacing, double rowSpacing)
        {
            var uTurn = new List<Point3d>();
            
            if (boundary == null || !boundary.IsValid)
                return uTurn;

            try
            {
                // Calculate midpoint between start and end
                Point3d midPt = (startPt + endPt) * 0.5;
                
                // Find direction perpendicular to the line connecting start and end
                Vector3d direction = endPt - startPt;
                if (direction.Length < 0.01)
                    return uTurn;
                    
                direction.Unitize();
                Vector3d perp = new Vector3d(-direction.Y, direction.X, 0);
                perp.Unitize();
                
                // Calculate U-turn radius (should be at least half spacing to avoid overlap)
                double uTurnRadius = Math.Max(spacing * 0.5, rowSpacing * 0.3);
                
                // Offset midpoint outward to create U-turn arc
                Point3d arcCenter = midPt + perp * uTurnRadius;
                
                // Create arc from start to end through center
                // Use a smooth curve (arc or bezier) instead of sharp corner
                int numPoints = Math.Max(8, (int)Math.Ceiling(uTurnRadius * Math.PI / (spacing * 0.2)));
                
                for (int i = 1; i < numPoints; i++)
                {
                    double t = (double)i / numPoints;
                    
                    // Create smooth U-turn using quadratic interpolation
                    // This creates a gentle curve instead of sharp 90° turn
                    Point3d pt1 = startPt + (arcCenter - startPt) * t;
                    Point3d pt2 = arcCenter + (endPt - arcCenter) * t;
                    Point3d finalPt = pt1 + (pt2 - pt1) * t;
                    
                    uTurn.Add(finalPt);
                }
            }
            catch
            {
                // Fallback: empty
            }
            
            return uTurn;
        }

        /// <summary>
        /// Creates a connection between two points along the boundary with proper layer spacing.
        /// The connection follows the boundary curve to avoid crossing existing lines.
        /// </summary>
        private List<Point3d> CreateLayerSpacedConnection(
            Point3d startPt, Point3d endPt, Curve boundary, double spacing)
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
                    
                    // For grid patterns, we follow the boundary directly (no offset needed)
                    // as the connections are between parallel lines, not offset curves
                    connectionPoints.Add(pt);
                }
            }
            catch
            {
                // If boundary connection fails, return empty (will use fallback)
                return new List<Point3d>();
            }
            
            return connectionPoints;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillGridIcon.png");
        public override Guid ComponentGuid => new Guid("e103c6fd-7cd3-45c5-af32-6452f9874089");
    }
}

