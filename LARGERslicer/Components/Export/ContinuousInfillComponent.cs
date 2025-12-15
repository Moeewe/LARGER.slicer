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
    /// Continuous Infill - Generates a single continuous toolpath from a boundary curve.
    /// Uses an improved algorithm that handles self-intersecting curves and complex geometries.
    /// Based on offset curves with intelligent path connection and self-intersection suppression.
    /// </summary>
    public class ContinuousInfillComponent : GH_Component
    {
        public ContinuousInfillComponent()
            : base("Continuous Infill", "ContInfill",
                  "Generates a single continuous toolpath from a boundary curve. Handles self-intersecting curves and complex geometries with undercuts. Automatically detects curve orientation to fill inward.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "Boundary", "Closed boundary curve to fill", GH_ParamAccess.item);
            pManager.AddNumberParameter("Spacing", "Spacing", "Distance between pattern lines (layer width in mm)", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Boundary Offset", "Offset", "Additional offset from boundary inward (mm)", GH_ParamAccess.item, 0.0);
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes) to exclude", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager.AddPointParameter("Seam Point", "Seam", "Starting point for the path. If not provided, automatically calculated.", GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager.AddBooleanParameter("Handle Undercuts", "Undercuts", "Handle self-intersecting offset curves (undercuts)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Use ArcWelder", "ArcWelder", "Convert polylines to arcs and lines for optimized GCode", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Arc Tolerance", "ArcTol", "Tolerance for arc fitting (mm)", GH_ParamAccess.item, 0.1);
            pManager.AddNumberParameter("Min Arc Radius", "MinRadius", "Minimum radius for arcs (mm)", GH_ParamAccess.item, 0.1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Continuous toolpath as polyline or arc/line curve", GH_ParamAccess.item);
            pManager.AddCurveParameter("Segments", "Segments", "Individual path segments for preview", GH_ParamAccess.list);
            pManager.AddPointParameter("Points", "Points", "All path points", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Path generation information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null;
            double spacing = 2.0;
            double boundaryOffset = 0.0;
            List<Curve> holes = new List<Curve>();
            Point3d seamPoint = Point3d.Unset;
            bool handleUndercuts = true;
            bool useArcWelder = false;
            double arcTolerance = 0.1;
            double minArcRadius = 0.1;

            if (!DA.GetData(0, ref boundary) || boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary curve.");
                return;
            }

            DA.GetData(1, ref spacing);
            DA.GetData(2, ref boundaryOffset);
            DA.GetDataList(3, holes);
            DA.GetData(4, ref seamPoint);
            DA.GetData(5, ref handleUndercuts);
            DA.GetData(6, ref useArcWelder);
            DA.GetData(7, ref arcTolerance);
            DA.GetData(8, ref minArcRadius);

            if (spacing <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing must be greater than zero.");
                return;
            }

            // Validate and filter holes
            holes = holes?.Where(h => h != null && h.IsValid).ToList() ?? new List<Curve>();

            // Prepare boundary
            if (!boundary.IsClosed)
            {
                boundary = boundary.DuplicateCurve();
                boundary.MakeClosed(0.01);
            }

            // Apply boundary offset if needed
            double totalOffset = spacing + boundaryOffset;
            Curve workingBoundary = PrepareBoundary(boundary, totalOffset, holes);

            // Get seam position
            Point3d? seamNullable = seamPoint.IsValid ? (Point3d?)seamPoint : null;
            Point3d seamPosition = GetSeamPosition(workingBoundary, seamNullable);

            // Generate continuous path
            var (pathPoints, segments) = GenerateContinuousPath(
                workingBoundary, seamPosition, spacing, handleUndercuts, holes);

            if (pathPoints == null || pathPoints.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Path generation resulted in insufficient points.");
                return;
            }

            // Create output curve
            Curve pathCurve;
            List<Curve> segmentCurves;

            if (useArcWelder)
            {
                // Convert to arcs and lines
                var optimizedCurves = ArcWelderHelper.ConvertToLinesAndArcs(pathPoints, arcTolerance, minArcRadius);
                
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
                    // Fallback to polyline
                    Polyline poly = new Polyline(pathPoints);
                    pathCurve = new PolylineCurve(poly);
                    segmentCurves = new List<Curve> { pathCurve };
                }
            }
            else
            {
                // Standard polyline
                Polyline poly = new Polyline(pathPoints);
                pathCurve = new PolylineCurve(poly);
                segmentCurves = segments.Select(s => 
                {
                    if (s.Count >= 2)
                    {
                        Polyline segPoly = new Polyline(s);
                        return (Curve)new PolylineCurve(segPoly);
                    }
                    return (Curve)null;
                }).Where(c => c != null).ToList();
            }

            if (pathCurve == null || !pathCurve.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create output curve.");
                return;
            }

            // Create info
            string info = $"Continuous path: {pathPoints.Count} points, {segments.Count} segments. ";
            info += $"Length: {pathCurve.GetLength():F2}mm. ";
            if (handleUndercuts) info += "Undercuts handled. ";
            if (useArcWelder) info += $"ArcWelder: {segmentCurves.Count} curves.";

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, segmentCurves);
            DA.SetDataList(2, pathPoints);
            DA.SetData(3, info);
        }

        /// <summary>
        /// Prepares boundary with offset, handling orientation automatically.
        /// </summary>
        private Curve PrepareBoundary(Curve boundary, double offset, List<Curve> otherCurves)
        {
            if (offset <= 0)
                return boundary.DuplicateCurve();

            double offsetDirection = PathHelper.GetOffsetDirection(boundary, otherCurves);
            double offsetDistance = offset * offsetDirection;

            var offsetCurves = boundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
            if (offsetCurves != null && offsetCurves.Length > 0)
            {
                var valid = offsetCurves.Where(c => c != null && c.IsValid).ToList();
                if (valid.Count > 0)
                {
                    // Use largest offset curve
                    return valid.OrderByDescending(c => 
                    {
                        var area = AreaMassProperties.Compute(c);
                        return area != null ? area.Area : 0;
                    }).First();
                }
            }

            return boundary.DuplicateCurve();
        }

        /// <summary>
        /// Gets seam position on boundary.
        /// </summary>
        private Point3d GetSeamPosition(Curve boundary, Point3d? seamPoint)
        {
            if (seamPoint.HasValue && seamPoint.Value.IsValid)
            {
                double t;
                boundary.ClosestPoint(seamPoint.Value, out t);
                return boundary.PointAt(t);
            }

            // Auto-calculate: point farthest from center
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d center = bbox.Center;
            
            double maxDist = 0;
            Point3d farthest = boundary.PointAtStart;
            
            int samples = 100;
            for (int i = 0; i <= samples; i++)
            {
                double t = boundary.Domain.ParameterAt((double)i / samples);
                Point3d pt = boundary.PointAt(t);
                double dist = pt.DistanceTo(center);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    farthest = pt;
                }
            }

            return farthest;
        }

        /// <summary>
        /// Generates a continuous path from boundary using improved algorithm.
        /// </summary>
        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GenerateContinuousPath(
            Curve boundary,
            Point3d seamPosition,
            double spacing,
            bool handleUndercuts,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Step 1: Generate offset curves with proper handling
            // PERFORMANCE FIX: Limit max offsets to prevent super long calculation times
            // User feedback: "berechnet super lange" - limit to reasonable number
            int maxOffsets = 50; // Reduced from 1000 to prevent long calculations
            List<Curve> offsetCurves;
            if (handleUndercuts)
            {
                offsetCurves = SelfIntersectionHelper.GenerateOffsetCurvesWithUndercutHandling(
                    boundary, spacing, maxOffsets, 0.01, holes);
            }
            else
            {
                offsetCurves = PathHelper.GenerateOffsetCurves(boundary, spacing, maxOffsets, holes);
            }

            if (offsetCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Process and filter curves
            var validCurves = new List<Curve>();
            foreach (var curve in offsetCurves)
            {
                if (curve == null || !curve.IsValid)
                    continue;

                // Filter curves inside holes
                if (holes != null && holes.Count > 0)
                {
                    Point3d midPt = curve.PointAt(0.5);
                    bool insideHole = false;
                    foreach (var hole in holes)
                    {
                        if (hole != null && hole.IsValid && hole.IsClosed)
                        {
                            PointContainment containment = hole.Contains(midPt, Plane.WorldXY, 0.01);
                            if (containment == PointContainment.Inside)
                            {
                                insideHole = true;
                                break;
                            }
                        }
                    }
                    if (insideHole)
                        continue;
                }

                // Suppress self-intersections if needed
                if (handleUndercuts)
                {
                    var healed = SelfIntersectionHelper.SuppressSelfIntersections(curve, 0.01, false);
                    validCurves.AddRange(healed);
                }
                else
                {
                    validCurves.Add(curve.DuplicateCurve());
                }
            }

            if (validCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 3: Optimize curve order (nearest neighbor from seam)
            List<Curve> connections;
            var orderedCurves = PathHelper.OptimizeCurveOrder(validCurves, seamPosition, out connections);

            // Step 4: Sample curves and create continuous path
            pathPoints.Add(seamPosition);
            Point3d currentEnd = seamPosition;

            foreach (var curve in orderedCurves)
            {
                // Find closest point on curve to current end
                double t;
                curve.ClosestPoint(currentEnd, out t);
                Point3d startPt = curve.PointAt(t);

                // Sample curve starting from closest point
                var curvePoints = SampleCurveFromPoint(curve, spacing * 0.5, startPt, t);

                if (curvePoints.Count >= 2)
                {
                    // Add connection if needed
                    if (pathPoints.Count > 0)
                    {
                        Point3d lastPt = pathPoints[pathPoints.Count - 1];
                        double dist = lastPt.DistanceTo(curvePoints[0]);
                        
                        if (dist > spacing * 0.1)
                        {
                            // Create connection following boundary offset (geometric consistency)
                            // Try to find curves for offset estimation
                            int curveIdx = orderedCurves.IndexOf(curve);
                            Curve prevCurve = curveIdx > 0 ? orderedCurves[curveIdx - 1] : null;
                            
                            var connection = PathHelper.CreateOffsetFollowingConnection(
                                lastPt, curvePoints[0], boundary, prevCurve, curve, spacing);
                            
                            if (connection.Count == 0)
                            {
                                // Fallback to boundary connection
                                connection = CreateBoundaryConnection(
                                    lastPt, curvePoints[0], boundary, spacing);
                            }
                            
                            if (connection.Count > 0)
                            {
                                pathPoints.AddRange(connection);
                            }
                        }
                    }

                    segments.Add(new List<Point3d>(curvePoints));
                    pathPoints.AddRange(curvePoints);
                    currentEnd = curvePoints[curvePoints.Count - 1];
                }
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

            // Start from specified point
            points.Add(startPoint);

            // Sample forward
            double currentDistance = 0;
            double currentParam = startParam;
            double stepSize = spacing * 0.5;

            while (currentDistance < length)
            {
                currentDistance += stepSize;
                double normalizedT = currentDistance / length;

                if (curve.IsClosed)
                {
                    normalizedT = normalizedT % 1.0;
                }

                if (normalizedT <= 1.0)
                {
                    double t;
                    if (curve.NormalizedLengthParameter(normalizedT, out t))
                    {
                        // Adjust for start parameter
                        double adjustedT = (t + startParam) % curve.Domain.Length;
                        if (adjustedT < curve.Domain.T0)
                            adjustedT += curve.Domain.Length;

                        Point3d pt = curve.PointAt(adjustedT);
                        if (pt.DistanceTo(points[points.Count - 1]) > stepSize * 0.1)
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
        /// Creates a connection between two points along the boundary.
        /// </summary>
        private List<Point3d> CreateBoundaryConnection(
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

                // Determine direction (shorter path)
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

                    // Offset inward by spacing
                    Vector3d tangent = boundary.TangentAt(t);
                    tangent.Unitize();
                    Vector3d normal = new Vector3d(-tangent.Y, tangent.X, 0);
                    normal.Unitize();

                    // Determine inward direction
                    BoundingBox bbox = boundary.GetBoundingBox(true);
                    Point3d center = bbox.Center;
                    Vector3d toCenter = center - pt;
                    toCenter.Unitize();
                    if (normal * toCenter < 0)
                        normal = -normal;

                    Point3d offsetPt = pt + normal * spacing;
                    connection.Add(offsetPt);
                }
            }
            catch
            {
                // Fallback: empty connection
            }

            return connection;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousInfillIcon.png");
        public override Guid ComponentGuid => new Guid("e1ba197a-3460-4179-a284-f2bf265bd3be");
    }
}

