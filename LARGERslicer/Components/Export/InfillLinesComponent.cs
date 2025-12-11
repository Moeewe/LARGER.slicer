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
    /// Infill Lines - Generates unidirectional parallel lines fill pattern.
    /// Pattern C: Lines (Unidirectional)
    /// </summary>
    public class InfillLinesComponent : BottomLayerPatternBase
    {
        public InfillLinesComponent()
            : base("Single Path Fill with Lines", "SPF Lines",
                  "Generates parallel lines fill pattern in a single direction. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Angle", "Angle", "Line direction angle in degrees around center point (0 = horizontal, 90 = vertical)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Optimize Order", "Optimize", "True to optimize line connection order for minimal travel", GH_ParamAccess.item, true);
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
            double angle = 0.0;
            bool optimizeOrder = true;
            DA.GetData(3, ref angle);  // Index 3 after base inputs (0-2)
            DA.GetData(4, ref optimizeOrder);

            double spacing = printWidth;
            double boundaryOffset = 0.0;

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Convert angle to radians
            angle = angle * Math.PI / 180.0;

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, angle, optimizeOrder, holes);

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
            double angle,
            bool optimizeOrder,
            List<Curve> holes)
        {

            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Get boundary bounding box and center point
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d centerPoint = bbox.Center;

            // Generate parallel lines (rotated around center point)
            var lines = PathHelper.GenerateParallelLines(boundary, spacing, angle, centerPoint, bbox);

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
                    for (int i = 0; i < intersectionParams.Count - 1; i += 2)
                    {
                        if (i + 1 < intersectionParams.Count)
                        {
                            double t1 = intersectionParams[i];
                            double t2 = intersectionParams[i + 1];
                            
                            if (Math.Abs(t2 - t1) > 0.01)
                            {
                                Curve trimmed = line.Trim(t1, t2);
                                if (trimmed != null && trimmed.IsValid && trimmed.GetLength() > spacing * 0.1)
                                {
                                    // Check for self-intersections (undercuts)
                                    var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(trimmed, 0.01);
                                    if (selfIntersections == null || selfIntersections.Count == 0)
                                    {
                                        trimmedLines.Add(trimmed);
                                    }
                                    else
                                    {
                                        // Handle self-intersection by splitting
                                        var healed = SelfIntersectionHelper.SuppressSelfIntersections(trimmed, 0.01, false);
                                        trimmedLines.AddRange(healed);
                                    }
                                }
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

            // Normalize line directions: ensure all lines start at the edge perpendicular to line direction
            // For horizontal lines (0°): start at bottom or top edge
            // For vertical lines (90°): start at left or right edge
            lines = NormalizeLineDirections(lines, angle, bbox);

            // Convert lines to point lists
            var lineSegments = new List<List<Point3d>>();
            foreach (var line in lines)
            {
                var linePoints = PathHelper.SampleCurve(line, spacing * 0.5, true);
                if (linePoints.Count >= 2)
                {
                    lineSegments.Add(linePoints);
                }
            }

            // Optimize order to minimize travel distance between lines
            if (optimizeOrder && lineSegments.Count > 1)
            {
                // Convert to curves for optimization
                var curves = lineSegments.Select(pts =>
                {
                    if (pts.Count >= 2)
                    {
                        Polyline poly = new Polyline(pts);
                        return (Curve)new PolylineCurve(poly);
                    }
                    return (Curve)null;
                }).Where(c => c != null).Cast<Curve>().ToList();

                // Optimize order starting from seam position
                List<Curve> connections;
                var optimizedCurves = PathHelper.OptimizeCurveOrder(curves, seamPosition, out connections);

                // Convert back to point lists and ensure proper direction
                lineSegments.Clear();
                Point3d currentEnd = seamPosition;
                foreach (var curve in optimizedCurves)
                {
                    var pts = PathHelper.SampleCurve(curve, spacing * 0.5, true);
                    
                    // Filter points that are inside holes
                    if (holes != null && holes.Count > 0)
                    {
                        pts = pts.Where(pt => IsPointValid(pt, boundary, holes)).ToList();
                    }
                    
                    if (pts.Count >= 2)
                    {
                        // Determine which end is closer to current position
                        double distToStart = currentEnd.DistanceTo(pts[0]);
                        double distToEnd = currentEnd.DistanceTo(pts[pts.Count - 1]);
                        
                        // Reverse if end is closer
                        if (distToEnd < distToStart)
                        {
                            pts.Reverse();
                        }
                        
                        lineSegments.Add(pts);
                        currentEnd = pts[pts.Count - 1]; // Update current end position
                    }
                }
            }

            segments = lineSegments;

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

            // Add all line segments with 90° connections
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];

                if (seg.Count > 0)
                {
                    Point3d firstPt = seg[0];
                    
                    // Connect from previous segment end (or seam position for first segment)
                    if (pathPoints.Count > 0)
                    {
                        Point3d lastPt = pathPoints[pathPoints.Count - 1];
                        
                        // Only add connection if there's a gap
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
        /// Normalizes line directions so all lines start at the edge perpendicular to line direction.
        /// For horizontal lines (0°): start at bottom or top edge
        /// For vertical lines (90°): start at left or right edge
        /// Also sorts lines by their position along the perpendicular direction.
        /// </summary>
        private List<Curve> NormalizeLineDirections(List<Curve> lines, double angleRad, BoundingBox bbox)
        {
            if (lines == null || lines.Count == 0)
                return lines;

            // Normalize angle to 0-180 range
            double normalizedAngle = angleRad % Math.PI;
            if (normalizedAngle < 0) normalizedAngle += Math.PI;
            
            // Determine primary direction (closer to horizontal or vertical)
            bool isHorizontal = normalizedAngle < Math.PI / 4 || normalizedAngle > 3 * Math.PI / 4;
            
            // Calculate perpendicular direction for sorting
            Vector3d lineDirection = new Vector3d(Math.Cos(angleRad), Math.Sin(angleRad), 0);
            Vector3d perpDirection = new Vector3d(-lineDirection.Y, lineDirection.X, 0);
            Point3d centerPoint = bbox.Center;
            
            var lineData = new List<(Curve line, double position, Point3d start)>();
            
            foreach (var line in lines)
            {
                if (line == null || !line.IsValid)
                    continue;

                Point3d start = line.PointAtStart;
                Point3d end = line.PointAtEnd;
                
                // Determine which end should be the start based on edge position
                bool shouldReverse = false;
                Point3d normalizedStart;
                
                if (isHorizontal)
                {
                    // For horizontal lines: start should be at bottom (minimum Y) or top (maximum Y)
                    // Use bottom edge (minimum Y) for consistency
                    if (end.Y < start.Y)
                    {
                        shouldReverse = true;
                        normalizedStart = end;
                    }
                    else
                    {
                        normalizedStart = start;
                    }
                }
                else
                {
                    // For vertical lines: start should be at left (minimum X) or right (maximum X)
                    // Use left edge (minimum X) for consistency
                    if (end.X < start.X)
                    {
                        shouldReverse = true;
                        normalizedStart = end;
                    }
                    else
                    {
                        normalizedStart = start;
                    }
                }
                
                Curve normalizedLine = line.DuplicateCurve();
                if (shouldReverse)
                {
                    normalizedLine.Reverse();
                }
                
                // Calculate position along perpendicular direction for sorting
                double position = Vector3d.Multiply(normalizedStart - centerPoint, perpDirection);
                
                lineData.Add((normalizedLine, position, normalizedStart));
            }
            
            // Sort lines by position along perpendicular direction
            lineData.Sort((a, b) => a.position.CompareTo(b.position));
            
            return lineData.Select(ld => ld.line).ToList();
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
                    
                    // For lines patterns, we follow the boundary directly (no offset needed)
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillLinesIcon.png");
        public override Guid ComponentGuid => new Guid("6dbfb0be-af59-4e15-b669-d669d57e6de3");
    }
}

