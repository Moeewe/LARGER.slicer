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
    /// Curve Boundary Infill Component - Generates boustrophedon (zigzag) infill pattern from curves.
    /// Takes the first curve as outer contour on Z-axis, offsets it inward by layer width,
    /// then generates cross-lines at configurable angle to connect the offset curves.
    /// Similar to CNC Program approach but adapted for 3D printing toolpaths.
    /// </summary>
    public class CurveBoundaryInfillComponent : GH_Component
    {
        public CurveBoundaryInfillComponent()
            : base("Curve Boundary Infill", "CBI",
                  "Generates boustrophedon infill pattern. Takes first curve as outer contour, offsets inward by layer width, then connects with cross-lines at configurable angle. Works for first layer and planar layers.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Individual boundary curves. The first curve will be used as the outer contour on Z-axis.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Width", "LW", "Layer width (extrusion width/bead width in mm). Used for inward offset spacing and cross-line spacing.", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Z Height", "Z", "Z height for planar layers (mm). For first layer, use 0 or layer height.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Angle", "Angle", "Angle for cross-lines (connection lines) in degrees. 0 = horizontal, 90 = vertical.", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Start Left", "StartLeft", "True to start from left, False to start from right", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Boundary Offset", "BO", "Additional offset from boundary (mm). Positive = inward, negative = outward. Default: 0 (uses half layer width).", GH_ParamAccess.item, 0.0);
            pManager[5].Optional = true;
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", GH_ParamAccess.list);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "Boundary", "Outer boundary curve that exactly matches the connection lines of input curves", GH_ParamAccess.item);
            pManager.AddCurveParameter("Infill Path", "Path", "Complete continuous infill path", GH_ParamAccess.item);
            pManager.AddCurveParameter("Offset Curves", "Offsets", "Individual offset curves used for infill", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            List<Curve> inputCurves = new List<Curve>();
            if (!DA.GetDataList(0, inputCurves) || inputCurves == null || inputCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one curve is required.");
                return;
            }

            // Filter valid curves
            inputCurves = inputCurves.Where(c => c != null && c.IsValid).ToList();
            if (inputCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid curves found.");
                return;
            }

            double layerWidth = 5.0;
            if (!DA.GetData(1, ref layerWidth) || layerWidth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer width must be greater than zero.");
                return;
            }

            double zHeight = 0.0;
            DA.GetData(2, ref zHeight);

            double angle = 0.0;
            DA.GetData(3, ref angle);

            bool startLeft = true;
            DA.GetData(4, ref startLeft);

            double boundaryOffset = 0.0;
            DA.GetData(5, ref boundaryOffset);

            List<Curve> holes = new List<Curve>();
            DA.GetDataList(6, holes);
            holes = holes?.Where(h => h != null && h.IsValid).ToList() ?? new List<Curve>();

            // Step 1: Use first curve as boundary (on Z-axis)
            Curve boundary = GetFirstCurveAsBoundary(inputCurves, zHeight);
            if (boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to create boundary from curves.");
                return;
            }

            // Step 2: Apply boundary offset if specified
            if (boundaryOffset != 0.0)
            {
                double offsetDirection = PathHelper.GetOffsetDirection(boundary, holes);
                double offsetDistance = boundaryOffset * offsetDirection;
                var offsetCurves = boundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    var validOffsets = offsetCurves.Where(c => c != null && c.IsValid).ToList();
                    if (validOffsets.Count > 0)
                    {
                        boundary = validOffsets.OrderByDescending(c =>
                        {
                            var area = AreaMassProperties.Compute(c);
                            return area != null ? area.Area : 0;
                        }).First();
                    }
                }
            }
            else
            {
                // Default: offset by half layer width inward
                double offsetDirection = PathHelper.GetOffsetDirection(boundary, holes);
                double offsetDistance = (layerWidth * 0.5) * offsetDirection;
                var offsetCurves = boundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    var validOffsets = offsetCurves.Where(c => c != null && c.IsValid).ToList();
                    if (validOffsets.Count > 0)
                    {
                        boundary = validOffsets.OrderByDescending(c =>
                        {
                            var area = AreaMassProperties.Compute(c);
                            return area != null ? area.Area : 0;
                        }).First();
                    }
                }
            }

            // Step 3: Generate offset curves inward by layer width
            List<Curve> offsetCurvesList = PathHelper.GenerateOffsetCurves(boundary, layerWidth, 1000, holes);
            
            if (offsetCurvesList.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No offset curves generated. Boundary may be too small.");
                // Return boundary as path
                DA.SetData(0, boundary);
                DA.SetData(1, boundary);
                DA.SetDataList(2, new List<Curve> { boundary });
                return;
            }

            // Step 4: Create boustrophedon path with cross-lines (similar to CNC program)
            // FIXED: Simplified approach - generate lines across all offset curves, then connect them
            var (pathPoints, segments) = CreateBoustrophedonPathFixed(offsetCurvesList, boundary, layerWidth, angle, startLeft, zHeight);

            // Step 5: Create output curves
            Curve infillPath = null;
            if (pathPoints != null && pathPoints.Count >= 2)
            {
                // Clean path points
                var cleanedPoints = CleanPointList(pathPoints, 0.01);
                if (cleanedPoints.Count >= 2)
                {
                    Polyline pathPolyline = new Polyline(cleanedPoints);
                    if (pathPolyline.IsValid)
                    {
                        infillPath = new PolylineCurve(pathPolyline);
                    }
                }
            }

            // Convert segments to curves
            List<Curve> segmentCurves = new List<Curve>();
            foreach (var seg in segments)
            {
                if (seg != null && seg.Count >= 2)
                {
                    var cleanedSeg = CleanPointList(seg, 0.01);
                    if (cleanedSeg.Count >= 2)
                    {
                        Polyline segPoly = new Polyline(cleanedSeg);
                        if (segPoly.IsValid)
                        {
                            segmentCurves.Add(new PolylineCurve(segPoly));
                        }
                    }
                }
            }

            // Set outputs
            DA.SetData(0, boundary);
            DA.SetData(1, infillPath);
            DA.SetDataList(2, segmentCurves);
        }

        /// <summary>
        /// Gets the first curve as boundary, normalized to Z height.
        /// The first curve on Z-axis is used as the outer contour.
        /// </summary>
        private Curve GetFirstCurveAsBoundary(List<Curve> curves, double zHeight)
        {
            if (curves == null || curves.Count == 0)
                return null;

            // Use first curve as boundary
            Curve first = curves[0].DuplicateCurve();
            if (!first.IsClosed)
            {
                first.MakeClosed(0.01);
            }

            // Set Z height
            Transform moveZ = Transform.Translation(0, 0, zHeight - first.PointAtStart.Z);
            first.Transform(moveZ);

            return first;
        }

        /// <summary>
        /// Creates a bounding box curve at specified Z height.
        /// </summary>
        private Curve CreateBoundingBoxCurve(BoundingBox bbox, double zHeight)
        {
            double xMin = bbox.Min.X;
            double xMax = bbox.Max.X;
            double yMin = bbox.Min.Y;
            double yMax = bbox.Max.Y;

            Polyline rect = new Polyline();
            rect.Add(new Point3d(xMin, yMin, zHeight));
            rect.Add(new Point3d(xMax, yMin, zHeight));
            rect.Add(new Point3d(xMax, yMax, zHeight));
            rect.Add(new Point3d(xMin, yMax, zHeight));
            rect.Add(new Point3d(xMin, yMin, zHeight)); // Close

            return new PolylineCurve(rect);
        }

        /// <summary>
        /// Creates boustrophedon (zigzag) path with cross-lines connecting offset curves.
        /// FIXED VERSION: Simplified approach - generates parallel lines across entire boundary,
        /// trims them at all offset curves, then connects in zigzag pattern.
        /// Similar to InfillLinesComponent but works with multiple offset curves.
        /// </summary>
        private (List<Point3d> pathPoints, List<List<Point3d>> segments) CreateBoustrophedonPathFixed(
            List<Curve> offsetCurves, Curve boundary, double layerWidth, double angle, bool startLeft, double zHeight)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            if (offsetCurves == null || offsetCurves.Count == 0 || boundary == null || !boundary.IsValid)
                return (pathPoints, segments);

            // Convert angle to radians
            double angleRad = angle * Math.PI / 180.0;

            // Get bounding box of boundary
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d center = bbox.Center;

            // Generate parallel lines across entire boundary (similar to InfillLinesComponent)
            var allLines = PathHelper.GenerateParallelLines(boundary, layerWidth, angleRad, center, bbox);

            if (allLines.Count == 0)
                return (pathPoints, segments);

            // For each line, find intersections with all offset curves and create segments
            bool forward = startLeft;
            foreach (var line in allLines)
            {
                if (line == null || !line.IsValid)
                    continue;

                // Find all intersections with all offset curves
                var allIntersections = new List<(double param, Point3d point, int curveIdx)>();
                
                for (int curveIdx = 0; curveIdx < offsetCurves.Count; curveIdx++)
                {
                    var offsetCurve = offsetCurves[curveIdx];
                    if (offsetCurve == null || !offsetCurve.IsValid)
                        continue;

                    var intersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(line, offsetCurve, 0.01, 0.01);
                    if (intersections != null)
                    {
                        foreach (var ix in intersections)
                        {
                            Point3d pt = line.PointAt(ix.ParameterA);
                            pt.Z = zHeight;
                            allIntersections.Add((ix.ParameterA, pt, curveIdx));
                        }
                    }
                }

                // Sort intersections by parameter along line
                allIntersections.Sort((a, b) => a.param.CompareTo(b.param));

                // Create segments between pairs of intersections (inside offset curves)
                for (int i = 0; i < allIntersections.Count - 1; i += 2)
                {
                    if (i + 1 < allIntersections.Count)
                    {
                        double t1 = allIntersections[i].param;
                        double t2 = allIntersections[i + 1].param;

                        if (Math.Abs(t2 - t1) > 0.01)
                        {
                            // Reverse direction for alternating rows (zigzag)
                            if (!forward)
                            {
                                double temp = t1;
                                t1 = t2;
                                t2 = temp;
                            }

                            Curve trimmed = line.Trim(t1, t2);
                            if (trimmed != null && trimmed.IsValid && trimmed.GetLength() > layerWidth * 0.1)
                            {
                                // Sample points along trimmed line
                                var linePoints = new List<Point3d>();
                                double trimmedLength = trimmed.GetLength();
                                int numPoints = Math.Max(2, (int)Math.Ceiling(trimmedLength / (layerWidth * 0.5)));

                                for (int p = 0; p <= numPoints; p++)
                                {
                                    double t = trimmed.Domain.ParameterAt((double)p / numPoints);
                                    Point3d pt = trimmed.PointAt(t);
                                    pt.Z = zHeight;
                                    linePoints.Add(pt);
                                }

                                if (linePoints.Count >= 2)
                                {
                                    segments.Add(linePoints);
                                }
                            }
                        }
                    }
                }

                // Alternate direction for next line (zigzag pattern)
                forward = !forward;
            }

            // Connect segments into continuous path
            if (segments.Count == 0)
                return (pathPoints, segments);

            // Start from first segment
            if (segments[0].Count > 0)
            {
                pathPoints.AddRange(segments[0]);
            }

            // Connect remaining segments
            for (int i = 1; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Count == 0)
                    continue;

                Point3d lastPt = pathPoints[pathPoints.Count - 1];
                Point3d firstPt = seg[0];
                double distance = lastPt.DistanceTo(firstPt);

                if (distance > layerWidth * 0.1)
                {
                    // Create connection line
                    int steps = Math.Max(2, (int)Math.Ceiling(distance / (layerWidth * 0.5)));
                    for (int s = 1; s < steps; s++)
                    {
                        double t = (double)s / steps;
                        Point3d connPt = new Point3d(
                            lastPt.X + t * (firstPt.X - lastPt.X),
                            lastPt.Y + t * (firstPt.Y - lastPt.Y),
                            zHeight
                        );
                        pathPoints.Add(connPt);
                    }
                }

                pathPoints.AddRange(seg);
            }

            return (pathPoints, segments);
        }

        /// <summary>
        /// Samples a curve starting from a specific point.
        /// </summary>
        private List<Point3d> SampleCurveFromPoint(Curve curve, double spacing, Point3d startPoint, double startParam, double zHeight)
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
                        pt.Z = zHeight; // Ensure Z height
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
        /// Cleans a point list by removing duplicate points and points that are too close together.
        /// </summary>
        private List<Point3d> CleanPointList(List<Point3d> points, double minDistance)
        {
            if (points == null || points.Count == 0)
                return new List<Point3d>();

            var cleaned = new List<Point3d>();
            cleaned.Add(points[0]); // Always keep first point

            for (int i = 1; i < points.Count; i++)
            {
                double dist = points[i].DistanceTo(cleaned[cleaned.Count - 1]);
                if (dist >= minDistance)
                {
                    cleaned.Add(points[i]);
                }
            }

            return cleaned;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillContourIcon.png");
        public override Guid ComponentGuid => new Guid("C1B2A3D4-E5F6-7890-ABCD-EF1234567890");
    }
}





