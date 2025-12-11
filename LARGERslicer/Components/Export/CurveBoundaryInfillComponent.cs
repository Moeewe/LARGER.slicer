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
            var (pathPoints, segments) = CreateBoustrophedonPath(offsetCurvesList, boundary, layerWidth, angle, startLeft, zHeight);

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
        /// Similar to CNC program approach: offsets curves inward, then connects them with angled cross-lines.
        /// </summary>
        private (List<Point3d> pathPoints, List<List<Point3d>> segments) CreateBoustrophedonPath(
            List<Curve> offsetCurves, Curve boundary, double layerWidth, double angle, bool startLeft, double zHeight)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            if (offsetCurves == null || offsetCurves.Count == 0)
                return (pathPoints, segments);

            // Convert angle to radians
            double angleRad = angle * Math.PI / 180.0;

            // Get bounding box of boundary
            BoundingBox bbox = boundary.GetBoundingBox(true);
            Point3d center = bbox.Center;

            // Calculate rotation matrix components
            double cosAngle = Math.Cos(angleRad);
            double sinAngle = Math.Sin(angleRad);

            // For each offset curve, generate cross-lines at layer width spacing
            foreach (var offsetCurve in offsetCurves)
            {
                if (offsetCurve == null || !offsetCurve.IsValid)
                    continue;

                // Get bounding box of this offset curve
                BoundingBox curveBbox = offsetCurve.GetBoundingBox(true);

                // Calculate line direction (perpendicular to cross-line direction)
                Vector3d lineDirection = new Vector3d(cosAngle, sinAngle, 0);
                Vector3d perpDirection = new Vector3d(-sinAngle, cosAngle, 0);

                // Calculate bounds perpendicular to line direction
                double minDist = double.MaxValue;
                double maxDist = double.MinValue;

                // Sample curve to find bounds
                double curveLength = offsetCurve.GetLength();
                int numSamples = Math.Max(20, (int)Math.Ceiling(curveLength / layerWidth));
                for (int i = 0; i <= numSamples; i++)
                {
                    double t = offsetCurve.Domain.ParameterAt((double)i / numSamples);
                    Point3d pt = offsetCurve.PointAt(t);
                    pt.Z = zHeight;

                    // Distance from center along perpendicular direction
                    double dist = Vector3d.Multiply(pt - center, perpDirection);
                    minDist = Math.Min(minDist, dist);
                    maxDist = Math.Max(maxDist, dist);
                }

                // Generate cross-lines at layer width spacing
                int numLines = (int)Math.Ceiling((maxDist - minDist) / layerWidth) + 1;
                bool forward = startLeft;

                for (int lineIdx = 0; lineIdx < numLines; lineIdx++)
                {
                    double dist = minDist + lineIdx * layerWidth;
                    Point3d lineCenter = center + perpDirection * dist;

                    // Create line through lineCenter in lineDirection
                    double lineLength = curveBbox.Diagonal.Length * 1.5;
                    Point3d lineStart = lineCenter - lineDirection * lineLength;
                    Point3d lineEnd = lineCenter + lineDirection * lineLength;

                    Line line = new Line(lineStart, lineEnd);
                    Curve lineCurve = new LineCurve(line);

                    // Intersect line with offset curve
                    var intersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(lineCurve, offsetCurve, 0.01, 0.01);
                    
                    if (intersections != null && intersections.Count >= 2)
                    {
                        // Get intersection parameters and create trimmed curve
                        var intersectionParams = intersections.Select(ix => ix.ParameterA).OrderBy(t => t).ToList();
                        
                        if (intersectionParams.Count >= 2)
                        {
                            double t1 = intersectionParams[0];
                            double t2 = intersectionParams[intersectionParams.Count - 1];
                            
                            // Reverse direction for alternating rows
                            if (!forward)
                            {
                                double temp = t1;
                                t1 = t2;
                                t2 = temp;
                            }

                            Curve trimmed = lineCurve.Trim(t1, t2);
                            if (trimmed != null && trimmed.IsValid)
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
                    else if (intersections != null && intersections.Count == 1)
                    {
                        // Single intersection - tangent or endpoint
                        double t = intersections[0].ParameterA;
                        Point3d pt = lineCurve.PointAt(t);
                        pt.Z = zHeight;
                        segments.Add(new List<Point3d> { pt });
                    }

                    // Alternate direction for next line
                    forward = !forward;
                }
            }

            // Connect segments into continuous path
            if (segments.Count == 0)
                return (pathPoints, segments);

            // Start from first segment
            pathPoints.AddRange(segments[0]);

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
