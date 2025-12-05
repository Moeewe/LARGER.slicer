using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Smart Slicer - Slices geometry with contour offsetting and automatic corner filleting.
    /// Creates smooth polylines optimized for 3D printing with extractable points.
    /// </summary>
    public class SmartSlicerComponent : GH_Component
    {
        public SmartSlicerComponent()
            : base("Smart Slicer", "SmartSlice",
                "Slices geometry into contours with automatic corner filleting. Outputs smooth polylines with points and layer widths for 3D printing.",
                "LARGER", "Toolpaths")
        {
        }

        public override Guid ComponentGuid => new Guid("A8B9C1D2-E3F4-5678-90AB-CDEF12345678");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "Geo", "Geometry to slice (Brep, Mesh, or Surface)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Layer Height", "LH", "Distance between contour slices (layer height in mm)", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Layer Width", "LW", "Print width / bead width for all layers (mm)", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Fillet Radius", "FR", "Radius for filleting sharp corners (mm). 0 = no filleting", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Min Angle", "Angle", "Minimum angle (degrees) to trigger filleting. Corners sharper than this will be filleted.", GH_ParamAccess.item, 120.0);
            pManager.AddIntegerParameter("Segments", "Segs", "Number of segments for fillet arcs (higher = smoother)", GH_ParamAccess.item, 8);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "Pts", "All points from all layers (flattened)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Widths", "LW", "Layer width for each point", GH_ParamAccess.list);
            pManager.AddCurveParameter("Polylines", "Plines", "Smooth polylines for each layer", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Layer Indices", "Layers", "Layer index for each point", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Slicing information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            GeometryBase geometry = null;
            double layerHeight = 2.0;
            double layerWidth = 5.0;
            double filletRadius = 1.0;
            double minAngle = 120.0;
            int filletSegments = 8;

            if (!DA.GetData(0, ref geometry) || geometry == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid geometry provided.");
                return;
            }

            DA.GetData(1, ref layerHeight);
            DA.GetData(2, ref layerWidth);
            DA.GetData(3, ref filletRadius);
            DA.GetData(4, ref minAngle);
            DA.GetData(5, ref filletSegments);

            // Validate inputs
            if (layerHeight <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer height must be greater than zero.");
                return;
            }

            if (layerWidth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer width must be greater than zero.");
                return;
            }

            if (filletRadius < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Fillet radius cannot be negative. Setting to 0.");
                filletRadius = 0;
            }

            if (filletSegments < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Fillet segments must be at least 3. Setting to 3.");
                filletSegments = 3;
            }

            // Convert geometry to Brep if needed
            Brep brep = null;
            if (geometry is Brep)
            {
                brep = geometry as Brep;
            }
            else if (geometry is Surface)
            {
                brep = (geometry as Surface).ToBrep();
            }
            else if (geometry is Mesh)
            {
                var mesh = geometry as Mesh;
                brep = Brep.CreateFromMesh(mesh, false);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry must be a Brep, Surface, or Mesh.");
                return;
            }

            if (brep == null || !brep.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not convert geometry to valid Brep.");
                return;
            }

            // Get bounding box to determine slicing range
            BoundingBox bbox = brep.GetBoundingBox(true);
            double minZ = bbox.Min.Z;
            double maxZ = bbox.Max.Z;
            double height = maxZ - minZ;

            if (height <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry has no height (flat in Z-direction).");
                return;
            }

            // Calculate number of layers
            int numLayers = (int)Math.Ceiling(height / layerHeight);

            // Output collections
            var allPoints = new List<Point3d>();
            var allLayerWidths = new List<double>();
            var allPolylines = new List<Curve>();
            var allLayerIndices = new List<int>();

            int validLayers = 0;
            int totalContours = 0;

            // Slice geometry at each layer height
            for (int i = 0; i < numLayers; i++)
            {
                double z = minZ + (i * layerHeight) + (layerHeight * 0.5); // Slice at middle of layer
                Plane slicePlane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);

                // Intersect brep with plane
                Curve[] intersectionCurves;
                Point3d[] intersectionPoints;

                bool success = Rhino.Geometry.Intersect.Intersection.BrepPlane(
                    brep, slicePlane, 0.001, out intersectionCurves, out intersectionPoints);

                if (!success || intersectionCurves == null || intersectionCurves.Length == 0)
                    continue;

                validLayers++;

                // Process each intersection curve
                foreach (Curve curve in intersectionCurves)
                {
                    if (curve == null || !curve.IsValid)
                        continue;

                    totalContours++;

                    // Close curve if open - ensure proper closure (not diagonal)
                    Curve workingCurve = curve.DuplicateCurve();
                    if (!workingCurve.IsClosed && workingCurve.IsPlanar())
                    {
                        // Check if start and end are close enough to close directly
                        double gap = workingCurve.PointAtStart.DistanceTo(workingCurve.PointAtEnd);
                        if (gap < 0.01)
                        {
                            // Points are close, just close the curve
                            workingCurve.MakeClosed(0.01);
                        }
                        else
                        {
                            // Points are far apart - add a connecting segment
                            // This prevents diagonal closure through the middle
                            Line closingLine = new Line(workingCurve.PointAtEnd, workingCurve.PointAtStart);
                            Curve closingCurve = new LineCurve(closingLine);
                            Curve[] joined = Curve.JoinCurves(new[] { workingCurve, closingCurve }, 0.01);
                            if (joined != null && joined.Length > 0 && joined[0].IsClosed)
                            {
                                workingCurve = joined[0];
                            }
                            else
                            {
                                // Fallback: use MakeClosed
                                workingCurve.MakeClosed(0.01);
                            }
                        }
                    }

                    // Convert to polyline first
                    Polyline polyline;
                    if (!workingCurve.TryGetPolyline(out polyline))
                    {
                        // If not already a polyline, convert it
                        var polylineCurve = workingCurve.ToPolyline(0, 0, 0.1, 0.1, 0, 0, 0.01, 0, true);
                        if (polylineCurve != null && polylineCurve.TryGetPolyline(out polyline))
                        {
                            // Success
                        }
                        else
                        {
                            // Fallback: sample curve
                            int samples = Math.Max(20, (int)(workingCurve.GetLength() / layerWidth));
                            var points = new List<Point3d>();
                            for (int s = 0; s <= samples; s++)
                            {
                                double t = workingCurve.Domain.ParameterAt((double)s / samples);
                                points.Add(workingCurve.PointAt(t));
                            }
                            polyline = new Polyline(points);
                        }
                    }

                    // Unify curve before filleting: normalize point density and ensure consistent orientation
                    if (polyline != null && polyline.Count > 2)
                    {
                        polyline = UnifyPolyline(polyline, layerWidth);
                    }

                    // Apply filleting if radius > 0
                    Polyline smoothPolyline = polyline;
                    bool originalWasClosed = polyline != null && polyline.IsClosed;
                    if (filletRadius > 0 && polyline != null && polyline.Count > 2)
                    {
                        smoothPolyline = FilletPolyline(polyline, filletRadius, minAngle, filletSegments, layerWidth);
                        
                        // Ensure closed curves remain closed after filleting
                        if (originalWasClosed && smoothPolyline != null && !smoothPolyline.IsClosed && smoothPolyline.Count > 2)
                        {
                            // Force closure by adding first point if not already present
                            if (smoothPolyline[smoothPolyline.Count - 1].DistanceTo(smoothPolyline[0]) > 0.01)
                            {
                                smoothPolyline.Add(smoothPolyline[0]);
                            }
                        }
                    }

                    // Extract points and create outputs
                    if (smoothPolyline != null && smoothPolyline.Count > 0)
                    {
                        for (int p = 0; p < smoothPolyline.Count; p++)
                        {
                            allPoints.Add(smoothPolyline[p]);
                            allLayerWidths.Add(layerWidth);
                            allLayerIndices.Add(i);
                        }

                        // Create polyline curve
                        var polylineCurve = smoothPolyline.ToPolylineCurve();
                        if (polylineCurve != null)
                        {
                            // Ensure the curve is closed if original was closed
                            if (originalWasClosed && !polylineCurve.IsClosed && polylineCurve.PointAtStart.DistanceTo(polylineCurve.PointAtEnd) < 0.01)
                            {
                                polylineCurve.MakeClosed(0.01);
                            }
                            allPolylines.Add(polylineCurve);
                        }
                    }
                }
            }

            // Create info string
            string info = $"Sliced {height:F2}mm height into {numLayers} layers ({validLayers} valid). ";
            info += $"Total contours: {totalContours}. ";
            info += $"Total points: {allPoints.Count}. ";
            info += $"Layer height: {layerHeight}mm, Layer width: {layerWidth}mm. ";
            if (filletRadius > 0)
            {
                info += $"Fillet radius: {filletRadius}mm.";
            }

            // Set outputs
            DA.SetDataList(0, allPoints);
            DA.SetDataList(1, allLayerWidths);
            DA.SetDataList(2, allPolylines);
            DA.SetDataList(3, allLayerIndices);
            DA.SetData(4, info);
        }

        /// <summary>
        /// Unifies a polyline by normalizing point density and ensuring consistent orientation.
        /// This prevents curve "explosion" when filleting.
        /// </summary>
        private Polyline UnifyPolyline(Polyline polyline, double targetSpacing)
        {
            if (polyline == null || polyline.Count < 2)
                return polyline;

            bool wasClosed = polyline.IsClosed;
            var unifiedPoints = new List<Point3d>();
            
            // Calculate total length
            double totalLength = 0.0;
            int count = wasClosed ? polyline.Count - 1 : polyline.Count;
            var segmentLengths = new List<double>();
            
            for (int i = 0; i < count; i++)
            {
                int nextIdx = (i + 1) % polyline.Count;
                double segLen = polyline[i].DistanceTo(polyline[nextIdx]);
                segmentLengths.Add(segLen);
                totalLength += segLen;
            }

            if (totalLength < 0.001)
                return polyline;

            // Resample with consistent spacing
            double spacing = Math.Max(targetSpacing * 0.5, totalLength / 100.0); // At least 100 points, or half layer width
            double currentDist = 0.0;
            int currentSeg = 0;
            double segStartDist = 0.0;

            unifiedPoints.Add(polyline[0]);

            while (currentSeg < count && currentDist < totalLength - spacing * 0.5)
            {
                currentDist += spacing;
                
                // Find which segment contains this distance
                while (currentSeg < count && currentDist > segStartDist + segmentLengths[currentSeg])
                {
                    segStartDist += segmentLengths[currentSeg];
                    currentSeg++;
                }

                if (currentSeg >= count)
                    break;

                // Interpolate point on current segment
                double segLocalDist = currentDist - segStartDist;
                double t = segLocalDist / segmentLengths[currentSeg];
                
                int nextIdx = (currentSeg + 1) % polyline.Count;
                Point3d p1 = polyline[currentSeg];
                Point3d p2 = polyline[nextIdx];
                Point3d interpolated = p1 + (p2 - p1) * t;
                
                unifiedPoints.Add(interpolated);
            }

            // Ensure closure for closed curves
            if (wasClosed && unifiedPoints.Count > 2)
            {
                // Check if we need to add the first point
                double gap = unifiedPoints[unifiedPoints.Count - 1].DistanceTo(unifiedPoints[0]);
                if (gap > spacing * 0.5)
                {
                    unifiedPoints.Add(unifiedPoints[0]);
                }
            }

            return new Polyline(unifiedPoints);
        }

        /// <summary>
        /// Fillets sharp corners in a polyline.
        /// Projects original corner points onto the filleted curve to maintain contour consistency.
        /// This ensures that overlapping contours follow the same fillet pattern.
        /// </summary>
        private Polyline FilletPolyline(Polyline polyline, double radius, double minAngleDegrees, int segments, double layerWidth)
        {
            if (polyline == null || polyline.Count < 3)
                return polyline;

            bool wasClosed = polyline.IsClosed;
            int count = wasClosed ? polyline.Count - 1 : polyline.Count;
            double minAngleRad = minAngleDegrees * Math.PI / 180.0;

            // Step 1: Build filleted curve with arcs
            var filletCurvePoints = new List<Point3d>();
            var originalCornerPoints = new List<Point3d>(); // Store original corner points for projection
            
            for (int i = 0; i < count; i++)
            {
                Point3d prev = polyline[(i - 1 + count) % count];
                Point3d current = polyline[i];
                Point3d next = polyline[(i + 1) % count];

                Vector3d v1 = prev - current;
                Vector3d v2 = next - current;

                double dist1 = v1.Length;
                double dist2 = v2.Length;

                if (dist1 < 0.001 || dist2 < 0.001)
                {
                    filletCurvePoints.Add(current);
                    originalCornerPoints.Add(current);
                    continue;
                }

                v1.Unitize();
                v2.Unitize();

                double angle = Vector3d.VectorAngle(v1, v2);

                if (angle < minAngleRad && angle > 0.01)
                {
                    // Calculate fillet
                    double maxRadius = Math.Min(dist1, dist2) * 0.4;
                    double actualRadius = Math.Min(radius, maxRadius);
                    double offset = actualRadius / Math.Tan(angle / 2);

                    if (actualRadius > 0.01 && offset < dist1 * 0.5 && offset < dist2 * 0.5)
                    {
                        // Calculate fillet arc points
                        Point3d p1 = current + v1 * offset;
                        Point3d p2 = current + v2 * offset;

                        // Calculate bisector and plane normal
                        Vector3d bisector = v1 + v2;
                        bisector.Unitize();
                        
                        // Determine plane normal
                        Vector3d planeNormal = Vector3d.ZAxis;
                        Vector3d edge1 = -v1; // From current to prev
                        Vector3d edge2 = v2;  // From current to next
                        Vector3d cross = Vector3d.CrossProduct(edge1, edge2);
                        
                        if (cross.Length > 0.001)
                        {
                            planeNormal = cross;
                            planeNormal.Unitize();
                        }

                        // Calculate arc center
                        double centerOffset = actualRadius / Math.Sin(angle / 2);
                        Point3d arcCenter = current + bisector * centerOffset;

                        // Verify center distance
                        Vector3d toP1 = p1 - arcCenter;
                        Vector3d toP2 = p2 - arcCenter;
                        if (Math.Abs(toP1.Length - actualRadius) > 0.01 || Math.Abs(toP2.Length - actualRadius) > 0.01)
                        {
                            toP1.Unitize();
                            toP2.Unitize();
                            Vector3d centerDir = (toP1 + toP2);
                            centerDir.Unitize();
                            arcCenter = current + centerDir * centerOffset;
                        }

                        // Add start of fillet arc
                        filletCurvePoints.Add(p1);
                        originalCornerPoints.Add(current); // Store original corner for projection

                        // Add fillet arc points
                        toP1 = p1 - arcCenter;
                        toP2 = p2 - arcCenter;
                        toP1.Unitize();
                        toP2.Unitize();

                        double arcAngle = Vector3d.VectorAngle(toP1, toP2);
                        Vector3d testRot = toP1;
                        testRot.Rotate(arcAngle * 0.5, planeNormal);
                        Vector3d expectedMid = (toP1 + toP2);
                        expectedMid.Unitize();
                        
                        if ((testRot * expectedMid) < 0.5)
                        {
                            planeNormal = -planeNormal;
                        }

                        // Add arc points
                        for (int s = 1; s <= segments; s++)
                        {
                            double t = (double)s / segments;
                            double currentAngle = arcAngle * t;
                            Vector3d arcDir = toP1;
                            arcDir.Rotate(currentAngle, planeNormal);
                            Point3d arcPoint = arcCenter + arcDir * actualRadius;
                            filletCurvePoints.Add(arcPoint);
                        }
                    }
                    else
                    {
                        filletCurvePoints.Add(current);
                        originalCornerPoints.Add(current);
                    }
                }
                else
                {
                    filletCurvePoints.Add(current);
                    originalCornerPoints.Add(current);
                }
            }

            // Ensure closed
            if (wasClosed && filletCurvePoints.Count > 0)
            {
                double gap = filletCurvePoints[0].DistanceTo(filletCurvePoints[filletCurvePoints.Count - 1]);
                if (gap > 0.01)
                {
                    filletCurvePoints.Add(filletCurvePoints[0]);
                }
            }

            // Step 2: Create curve from fillet points
            var filletPolyline = new Polyline(filletCurvePoints);
            if (wasClosed && !filletPolyline.IsClosed && filletPolyline.Count > 2)
            {
                filletPolyline.Add(filletPolyline[0]);
            }
            
            Curve filletCurve = filletPolyline.ToPolylineCurve();
            if (filletCurve == null || !filletCurve.IsValid)
            {
                return polyline; // Fallback to original
            }

            // Step 3: Project original corner points onto filleted curve (ClosestPoint)
            var projectedPoints = new List<Point3d>();
            int cornerIndex = 0;

            for (int i = 0; i < count; i++)
            {
                Point3d originalCorner = originalCornerPoints[cornerIndex];
                cornerIndex++;

                // Find closest point on filleted curve
                double t;
                if (filletCurve.ClosestPoint(originalCorner, out t))
                {
                    Point3d projected = filletCurve.PointAt(t);
                    projectedPoints.Add(projected);
                }
                else
                {
                    // Fallback: use original point
                    projectedPoints.Add(originalCorner);
                }

                // Add intermediate points along straight edges
                if (i < count - 1 || !wasClosed)
                {
                    int nextIdx = (i + 1) % count;
                    Point3d edgeStart = projectedPoints[projectedPoints.Count - 1];
                    Point3d edgeEnd = nextIdx < originalCornerPoints.Count ? 
                        (filletCurve.ClosestPoint(originalCornerPoints[nextIdx], out t) ? filletCurve.PointAt(t) : originalCornerPoints[nextIdx]) :
                        polyline[nextIdx];
                    
                    double edgeLength = edgeStart.DistanceTo(edgeEnd);
                    
                    // Add intermediate points if edge is long
                    if (edgeLength > layerWidth * 1.5)
                    {
                        int numPoints = (int)(edgeLength / layerWidth);
                        for (int p = 1; p < numPoints; p++)
                        {
                            double tEdge = (double)p / numPoints;
                            Point3d edgePoint = edgeStart + (edgeEnd - edgeStart) * tEdge;
                            // Project onto fillet curve for consistency
                            if (filletCurve.ClosestPoint(edgePoint, out t))
                            {
                                edgePoint = filletCurve.PointAt(t);
                            }
                            projectedPoints.Add(edgePoint);
                        }
                    }
                }
            }

            // Ensure closed
            if (wasClosed && projectedPoints.Count > 0)
            {
                double gap = projectedPoints[0].DistanceTo(projectedPoints[projectedPoints.Count - 1]);
                if (gap > 0.01)
                {
                    projectedPoints.Add(projectedPoints[0]);
                }
            }

            var result = new Polyline(projectedPoints);
            if (wasClosed && !result.IsClosed && result.Count > 2)
            {
                result.Add(result[0]);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SmartSlicerIcon.png");
    }
}
