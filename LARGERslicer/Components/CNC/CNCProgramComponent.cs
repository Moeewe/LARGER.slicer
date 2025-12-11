using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNC
{
    public class CNCProgramComponent : GH_Component
    {
        public CNCProgramComponent()
          : base("CNC - Program", "CNC",
              "Generates boustrophedon (zigzag) toolpath from geometry with optional rotation angle and outline contour for Zünd CNC. Includes tool selection, spindle speed, vacuum zones, acceleration settings, and material thickness handling. Outline cuts around the part at bottom to release it from the plate.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "Geo", "Mesh, Brep, or Surface to generate boustrophedon toolpath from", GH_ParamAccess.item);
            pManager.AddNumberParameter("Step X", "dx", "Step size in X direction (mm)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Step Y", "dy", "Step size in Y direction (mm)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Margin", "Margin", "Margin around geometry bounding box (mm)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Start Left", "StartLeft", "True to start from left, False to start from right", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Angle", "Angle", "Rotation angle in degrees for toolpath lines (0 = horizontal/vertical, default: 0)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Add Outline", "Outline", "True to add outline contour at bottom to cut out the part", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Make PLT", "MakePLT", "True to generate PLT output", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Feed Speed", "VW", "Feed speed VS (mm/s)", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Rapid Speed", "VF", "Rapid speed VU (mm/s)", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Z Up", "Zup", "Z retract height (mm)", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Material Thickness", "MatThick", "Material thickness in mm. Used for: ZP top position calculation, XX306 extraction height (material thickness + 3.3mm), and validation. Geometry Z coordinates are POSITIVE (material remaining, e.g., +5mm = 5mm remain). CNC coordinates: Z=0 = table surface, material top = -material thickness, CNC_Z = -geometry Z (directly negative). Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments. Positive CNC Z values are NOT allowed (would cut into table).", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("ZP Offset", "ZPOffset", "ZP offset for through-cutting (0 = cut to 0, +1 etc = offset for through-cutting)", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Tool", "Tool", "Tool selection: 11 (left), 21 (middle), 31 (right) for SP command", GH_ParamAccess.item, 31);
            pManager.AddNumberParameter("Spindle Speed", "RPM", "Spindle speed (RPM) for XX150 command", GH_ParamAccess.item, 10000.0);
            pManager.AddIntegerParameter("Accel Down", "AccDown", "Acceleration for cutting (1-4, default: 2)", GH_ParamAccess.item, 2);
            pManager.AddIntegerParameter("Accel Up", "AccUp", "Acceleration for rapid (1-4, default: 4)", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Vacuum Strength", "VacStr", "Vacuum strength level (0-10, 0 = off, 10 = max)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Material Width", "MatWidth", "Material width in mm (Y-dimension). Used for vacuum zone calculation (SV command). If 0 or not provided, uses bounding box Y-dimension. For plates >= 430mm, vacuum zones are 80mm wide sections.", GH_ParamAccess.item, 0.0);
            pManager[18].Optional = true;
            pManager.AddIntegerParameter("Underlay Thickness", "Underlay", "Underlay thickness in increments (100 increments = 1mm, default: 200 = 2mm) for XX308 command", GH_ParamAccess.item, 200);
            
            // Tool Change Parameters (optional - for next version)
            pManager.AddIntegerParameter("Tool List", "Tools", "Optional: List of tools (11, 21, 31) for each path segment. If empty, uses single Tool parameter for all segments.", GH_ParamAccess.list);
            pManager[19].Optional = true;
            pManager.AddNumberParameter("Tool Spindle Speeds", "ToolRPM", "Optional: List of spindle speeds (RPM) for each tool. If empty, uses single Spindle Speed for all tools.", GH_ParamAccess.list);
            pManager[20].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Complete toolpath as polyline curve", GH_ParamAccess.item);
            pManager.AddCurveParameter("Segments", "Segments", "Individual path segments as curves", GH_ParamAccess.list);
            pManager.AddTextParameter("PLT", "PLT", "Zünd PLT file content", GH_ParamAccess.item);
            pManager.AddTextParameter("Stats", "Stats", "Toolpath statistics and information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo geo = null;
            double dx = 1.0;
            double dy = 1.0;
            double margin = 0.0;
            bool startLeft = true;
            double angle = 0.0;
            bool addOutline = true;
            bool makePLT = true;
            double feedSpeed = 10.0;
            double rapidSpeed = 50.0;
            double zUp = 5.0;
            double materialThickness = 10.0;
            int zpOffset = 0;
            int tool = 31;
            double spindleSpeed = 10000.0;
            int accelDown = 2;
            int accelUp = 4;
            int vacuumStrength = 0;
            double materialWidth = 0.0;
            int underlayThickness = 200;
            
            // Tool change inputs (optional)
            List<int> toolList = new List<int>();
            List<double> toolSpindleSpeeds = new List<double>();

            // Get inputs
            if (!DA.GetData(0, ref geo)) return;
            if (!DA.GetData(1, ref dx)) return;
            if (!DA.GetData(2, ref dy)) return;
            DA.GetData(3, ref margin);
            DA.GetData(4, ref startLeft);
            DA.GetData(5, ref angle);
            DA.GetData(6, ref addOutline);
            DA.GetData(7, ref makePLT);
            DA.GetData(8, ref feedSpeed);
            DA.GetData(9, ref rapidSpeed);
            DA.GetData(10, ref zUp);
            DA.GetData(11, ref materialThickness);
            DA.GetData(12, ref zpOffset);
            DA.GetData(13, ref tool);
            DA.GetData(14, ref spindleSpeed);
            DA.GetData(15, ref accelDown);
            DA.GetData(16, ref accelUp);
            DA.GetData(17, ref vacuumStrength);
            DA.GetData(18, ref materialWidth);
            DA.GetData(19, ref underlayThickness);
            
            // Get optional tool change inputs
            var toolListData = new List<GH_Integer>();
            DA.GetDataList(20, toolListData);
            foreach (var toolVal in toolListData)
            {
                if (toolVal != null && toolVal.IsValid)
                {
                    int t = toolVal.Value;
                    if (t == 11 || t == 21 || t == 31)
                        toolList.Add(t);
                }
            }
            
            var toolRpmData = new List<GH_Number>();
            DA.GetDataList(21, toolRpmData);
            foreach (var rpmVal in toolRpmData)
            {
                if (rpmVal != null && rpmVal.IsValid)
                {
                    toolSpindleSpeeds.Add(rpmVal.Value);
                }
            }

            // Validate inputs
            // Check for zero or negative step sizes (would cause infinite loops or crashes)
            if (dx <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Step X (dx) must be greater than 0. Current value: {dx}. Using minimum value 0.1 mm.");
                dx = 0.1;
            }
            if (dy <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Step Y (dy) must be greater than 0. Current value: {dy}. Using minimum value 0.1 mm.");
                dy = 0.1;
            }
            
            // Ensure minimum reasonable values (0.1mm minimum to prevent excessive point generation)
            double originalDx = dx;
            double originalDy = dy;
            dx = Math.Max(dx, 0.1); // Minimum 0.1mm to prevent crashes
            dy = Math.Max(dy, 0.1); // Minimum 0.1mm to prevent crashes
            
            // Warn if values were too small
            if (originalDx < 0.1 && originalDx > 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Step X (dx) is very small ({originalDx:F3} mm). This may generate excessive points and slow down processing. Minimum recommended: 0.1 mm.");
            }
            if (originalDy < 0.1 && originalDy > 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Step Y (dy) is very small ({originalDy:F3} mm). This may generate excessive points and slow down processing. Minimum recommended: 0.1 mm.");
            }
            
            feedSpeed = Math.Max(feedSpeed, 0.1);
            rapidSpeed = Math.Max(rapidSpeed, 0.1);
            materialThickness = Math.Max(materialThickness, 0.1); // Minimum 0.1mm
            spindleSpeed = Math.Max(spindleSpeed, 0.0); // Minimum 0 RPM
            accelDown = Math.Max(1, Math.Min(4, accelDown)); // Clamp to 1-4
            accelUp = Math.Max(1, Math.Min(4, accelUp)); // Clamp to 1-4
            vacuumStrength = Math.Max(0, Math.Min(10, vacuumStrength)); // Clamp to 0-10

            // Validate tool selection (must be 11, 21, or 31)
            if (tool != 11 && tool != 21 && tool != 31)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Tool must be 11, 21, or 31. Using default 31.");
                tool = 31;
            }

            // Handle geometry - support both Mesh and Brep/NURBS directly
            Mesh mesh = null;
            Brep brep = null;
            IGH_GeometricGoo geometry = geo;
            
            // Try to get Brep first (more precise than Mesh)
            if (geo is GH_Brep ghBrep)
            {
                brep = ghBrep.Value;
            }
            else if (geo is GH_Surface ghSurface)
            {
                object surfaceValue = ghSurface.Value;
                if (surfaceValue is Brep brepValue)
                {
                    brep = brepValue;
                }
                else if (surfaceValue is Surface surface)
                {
                    brep = surface.ToBrep();
                }
            }
            else if (geo is GH_Mesh ghMesh)
            {
                mesh = ghMesh.Value.DuplicateMesh();
                mesh.UnifyNormals();
                mesh.Normals.ComputeNormals();
            }
            
            // If no Brep or Mesh found, try to convert to Brep
            if (brep == null && mesh == null)
            {
                mesh = EnsureMesh(geo);
                if (mesh == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry could not be converted to Brep or Mesh.");
                    DA.SetData(0, null);
                    DA.SetDataList(1, new List<Curve>());
                    DA.SetData(2, "");
                    DA.SetData(3, "ERROR: Geometry could not be converted.");
                    return;
                }
            }
            
            // Get geometry footprint (projection onto XY plane) instead of bounding box
            // This works for irregular geometries and circles, not just rectangles
            Curve footprintCurve = null;
            BoundingBox originalBbox;
            
            if (brep != null)
            {
                footprintCurve = GetGeometryFootprintFromBrep(brep);
                originalBbox = brep.GetBoundingBox(true);
            }
            else
            {
                footprintCurve = GetGeometryFootprint(mesh);
                originalBbox = mesh.GetBoundingBox(true);
            }
            
            // Get bounding box from footprint curve (for toolpath generation area)
            BoundingBox bbox;
            if (footprintCurve != null && footprintCurve.IsValid)
            {
                bbox = footprintCurve.GetBoundingBox(true);
                
                // Standard offset: 0.01mm inward from geometry boundary to prevent edge connection issues
                double inwardOffset = -0.01; // Negative = inward (shrink)
                bbox.Inflate(inwardOffset, inwardOffset, 0.0);
                
                // Apply user margin (can be positive to expand, but default is 0)
                bbox.Inflate(margin, margin, 0.0);
            }
            else
            {
                // Fallback to bounding box if footprint extraction failed
                bbox = mesh.GetBoundingBox(true);
                double inwardOffset = -0.01;
                bbox.Inflate(inwardOffset, inwardOffset, 0.0);
                bbox.Inflate(margin, margin, 0.0);
            }

            // Convert angle from degrees to radians
            double angleRad = angle * Math.PI / 180.0;

            // Expand area to account for rotation
            // When rotating, we need a larger area to cover all rotated points
            if (Math.Abs(angleRad) > 1e-6)
            {
                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;
                
                // For a rectangle rotated by angle θ, the new bounding box dimensions are:
                // newWidth = width*|cos(θ)| + height*|sin(θ)|
                // newHeight = width*|sin(θ)| + height*|cos(θ)|
                double cosAngle = Math.Abs(Math.Cos(angleRad));
                double sinAngle = Math.Abs(Math.Sin(angleRad));
                double newWidth = width * cosAngle + height * sinAngle;
                double newHeight = width * sinAngle + height * cosAngle;
                
                // Calculate expansion needed
                double expandX = (newWidth - width) / 2.0;
                double expandY = (newHeight - height) / 2.0;
                
                bbox.Inflate(expandX, expandY, 0.0);
            }

            // Generate boustrophedon path
            var result = GenerateBoustrophedonPath(mesh, brep, bbox, dx, dy, startLeft, angleRad, originalBbox.Min.Z, originalBbox.Max.Z);
            List<List<Point3d>> segments = result.segments;
            List<Point3d> pathPoints = result.pathPoints;

            // Add outline contour if requested
            if (addOutline && pathPoints.Count > 0)
            {
                // Get last point of boustrophedon path
                Point3d lastPoint = pathPoints[pathPoints.Count - 1];
                
                // Generate outline from actual geometry (not bounding box)
                // Offset distance: use the larger of dx and dy to ensure complete coverage
                double offsetDistance = Math.Max(dx, dy);
                List<Point3d> outlinePoints;
                if (brep != null)
                {
                    outlinePoints = GenerateOutlineContourFromBrep(brep, originalBbox.Min.Z, offsetDistance);
                }
                else
                {
                    outlinePoints = GenerateOutlineContour(mesh, originalBbox.Min.Z, offsetDistance);
                }
                
                if (outlinePoints.Count > 0)
                {
                    // Move down to bottom (Z = originalBbox.Min.Z) at last point
                    Point3d bottomPoint = new Point3d(lastPoint.X, lastPoint.Y, originalBbox.Min.Z);
                    pathPoints.Add(bottomPoint);
                    
                    // Connection path from last point to outline following surface geometry
                    List<Point3d> connectionPath = FindConnectionPathToOutline(bottomPoint, outlinePoints, mesh, brep, originalBbox.Min.Z, originalBbox.Max.Z);
                    
                    if (connectionPath.Count > 0)
                    {
                        // Add connection path (follows surface, not straight)
                        pathPoints.AddRange(connectionPath);
                        
                        // Find the index of last connection point in outline
                        Point3d connectionEnd = connectionPath[connectionPath.Count - 1];
                        int connectionIndex = FindClosestPointIndex(outlinePoints, connectionEnd);
                        
                        // Add outline contour starting from connection point (go around once)
                        // Add points from connection index to end
                        for (int i = connectionIndex; i < outlinePoints.Count; i++)
                        {
                            pathPoints.Add(outlinePoints[i]);
                        }
                        // Add points from start to connection index (to complete the loop)
                        for (int i = 0; i <= connectionIndex; i++)
                        {
                            pathPoints.Add(outlinePoints[i]);
                        }
                    }
                    else
                    {
                        // Fallback: use closest point
                        Point3d closestOutlinePoint = FindClosestPointOnOutline(outlinePoints, bottomPoint);
                        pathPoints.Add(closestOutlinePoint);
                        
                        int closestIndex = FindClosestPointIndex(outlinePoints, closestOutlinePoint);
                        for (int i = closestIndex; i < outlinePoints.Count; i++)
                        {
                            pathPoints.Add(outlinePoints[i]);
                        }
                        for (int i = 0; i <= closestIndex; i++)
                        {
                            pathPoints.Add(outlinePoints[i]);
                        }
                    }
                    
                    // Add outline as a separate segment for output
                    segments.Add(new List<Point3d>(outlinePoints));
                }
            }
            
            // Validate Z values (after segments are generated)
            // Geometry Z coordinates are POSITIVE (material remaining, e.g., +5mm = 5mm remain)
            // CNC Z coordinates: Z=0 = table surface, negative Z = cutting into material
            // Conversion: CNC_Z = -geometryZ (directly negative)
            // Material top surface in CNC = -materialThickness (e.g., -30mm for 30mm material)
            // Positive CNC Z values are NOT allowed (would cut into table!)
            double maxGeometryZ = 0.0; // Highest geometry Z (most material remaining)
            double minGeometryZ = double.MaxValue; // Lowest geometry Z (least material remaining)
            foreach (var seg in segments)
            {
                foreach (var pt in seg)
                {
                    if (pt.Z > maxGeometryZ) maxGeometryZ = pt.Z;
                    if (pt.Z < minGeometryZ) minGeometryZ = pt.Z;
                }
            }
            if (pathPoints != null && pathPoints.Count > 0)
            {
                foreach (var pt in pathPoints)
                {
                    if (pt.Z > maxGeometryZ) maxGeometryZ = pt.Z;
                    if (pt.Z < minGeometryZ) minGeometryZ = pt.Z;
                }
            }
            
            // Convert to CNC coordinates for validation: CNC_Z = -geometryZ
            double materialTopZ = -materialThickness; // Material top surface in CNC coordinates
            double maxCNCZ = -maxGeometryZ; // Highest cut in CNC coordinates (least negative)
            double minCNCZ = -minGeometryZ; // Lowest cut in CNC coordinates (most negative)
            
            // Check if any CNC Z would be positive (would cut into table)
            // This happens if geometry Z is negative (which shouldn't happen, but check anyway)
            if (maxCNCZ > 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geometry Z values would result in positive CNC Z coordinates! Maximum geometry Z ({maxGeometryZ:F2} mm) would result in CNC Z = {maxCNCZ:F2} mm, which would cut into the table. Geometry Z coordinates must be positive (material remaining).");
            }
            // Check if cutting below material (through-cutting)
            // If geometry Z is very small (close to 0), CNC_Z is close to 0 (cutting through)
            // If geometry Z > materialThickness, CNC_Z < -materialThickness (cutting below material)
            if (minGeometryZ < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Minimum geometry Z ({minGeometryZ:F2} mm) is negative. This would result in positive CNC Z ({minCNCZ:F2} mm), which would cut into the table. Geometry Z coordinates must be positive.");
            }
            // Check if geometry Z exceeds material thickness (would try to leave more material than available)
            if (maxGeometryZ > materialThickness)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Maximum geometry Z ({maxGeometryZ:F2} mm) exceeds material thickness ({materialThickness:F2} mm). This would try to leave more material than available. Adjust geometry Z coordinates or material thickness.");
            }
            
            // Validate tool list if provided (after segments are generated)
            if (toolList.Count > 0)
            {
                if (toolList.Count != segments.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Tool List count ({toolList.Count}) does not match segment count ({segments.Count}). Using single Tool parameter for all segments.");
                    toolList.Clear();
                }
                else
                {
                    // Validate each tool in list
                    for (int i = 0; i < toolList.Count; i++)
                    {
                        if (toolList[i] != 11 && toolList[i] != 21 && toolList[i] != 31)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Tool at index {i} must be 11, 21, or 31. Using default tool {tool}.");
                            toolList[i] = tool;
                        }
                    }
                }
            }

            // Create geometry outputs
            List<Curve> segmentCurves = new List<Curve>();
            foreach (var seg in segments)
            {
                if (seg.Count >= 2)
                {
                    Polyline poly = new Polyline(seg);
                    segmentCurves.Add(new PolylineCurve(poly));
                }
            }

            Curve pathCurve = null;
            if (pathPoints.Count >= 2)
            {
                Polyline pathPoly = new Polyline(pathPoints);
                pathCurve = new PolylineCurve(pathPoly);
            }

            // Calculate statistics
            double totalLength = segments.Sum(s => s.Count >= 2 ? new Polyline(s).Length : 0.0);
            string outlineInfo = addOutline ? " + Outline" : "";
            string stats = $"Boustrophedon{outlineInfo} | Segments={segments.Count} | Points={pathPoints.Count} | Length={totalLength:F2} mm | VS={feedSpeed:F3} mm/s, VU={rapidSpeed:F3} mm/s";

            // Generate PLT
            string plt = "";
            if (makePLT)
            {
                Point3d parkXY = new Point3d(2500.0, 0.0, 0.0); // PU250000,0 like in sample
                // Calculate vacuum zone based on material width parameter (if provided) or footprint width (fallback)
                double vacuumZone = 0.0;
                if (materialWidth > 0.0)
                {
                    // Use provided material width (in mm) - convert to increments (1mm = 100 increments, so 1cm = 1000)
                    vacuumZone = materialWidth * 100.0;
                }
                else
                {
                    // Fallback: use footprint width in Y direction (from footprint curve, not bounding box)
                    Curve footprint = brep != null ? GetGeometryFootprintFromBrep(brep) : GetGeometryFootprint(mesh);
                    if (footprint != null && footprint.IsValid)
                    {
                        BoundingBox footprintBbox = footprint.GetBoundingBox(true);
                        double footprintWidthY = footprintBbox.Max.Y - footprintBbox.Min.Y; // Width in mm
                        vacuumZone = footprintWidthY * 100.0; // Convert to increments
                    }
                    else
                    {
                        // Last resort: use bounding box width in Y direction
                        double bboxWidthY = originalBbox.Max.Y - originalBbox.Min.Y; // Width in mm
                        vacuumZone = bboxWidthY * 100.0; // Convert to increments
                    }
                }
                
                // Prepare tool assignments: if toolList is provided, use it; otherwise use single tool for all segments
                List<int> toolAssignments = new List<int>();
                if (toolList.Count > 0 && toolList.Count == segments.Count)
                {
                    toolAssignments = new List<int>(toolList);
                }
                else
                {
                    // Use single tool for all segments
                    for (int i = 0; i < segments.Count; i++)
                    {
                        toolAssignments.Add(tool);
                    }
                }
                
                // Prepare tool spindle speeds: create a dictionary mapping tool to speed
                Dictionary<int, double> toolSpeedMap = new Dictionary<int, double>();
                if (toolSpindleSpeeds.Count > 0)
                {
                    // Map tools to speeds: assume order is 11, 21, 31
                    int[] toolNumbers = { 11, 21, 31 };
                    for (int i = 0; i < Math.Min(toolSpindleSpeeds.Count, 3); i++)
                    {
                        toolSpeedMap[toolNumbers[i]] = toolSpindleSpeeds[i];
                    }
                }
                // Add default speed for tools not in map
                if (!toolSpeedMap.ContainsKey(11)) toolSpeedMap[11] = spindleSpeed;
                if (!toolSpeedMap.ContainsKey(21)) toolSpeedMap[21] = spindleSpeed;
                if (!toolSpeedMap.ContainsKey(31)) toolSpeedMap[31] = spindleSpeed;
                
                plt = BuildPLTFromPath(segments, pathPoints, feedSpeed, rapidSpeed, zUp, materialThickness, zpOffset, tool, toolAssignments, toolSpeedMap, spindleSpeed, accelDown, accelUp, vacuumZone, vacuumStrength, underlayThickness, parkXY);
            }

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, segmentCurves);
            DA.SetData(2, plt);
            DA.SetData(3, stats);
        }

        // Get geometry footprint from Brep (projection onto XY plane) - works for irregular geometries and circles
        // Returns the outer boundary curve of the Brep projected onto XY plane
        private Curve GetGeometryFootprintFromBrep(Brep brep)
        {
            if (brep == null || !brep.IsValid) return null;
            
            try
            {
                // Extract edge curves from Brep and project them onto XY plane
                Plane projectionPlane = new Plane(new Point3d(0, 0, 0), Vector3d.ZAxis);
                List<Point3d> projectedPoints = new List<Point3d>();
                
                // Get all edge curves from the Brep
                foreach (var edge in brep.Edges)
                {
                    if (edge != null && edge.IsValid)
                    {
                        Curve edgeCurve = edge.EdgeCurve;
                        if (edgeCurve != null && edgeCurve.IsValid)
                        {
                            // Sample points along the edge curve
                            double length = edgeCurve.GetLength();
                            int numSamples = Math.Max(10, (int)(length / 0.5)); // Sample every 0.5mm
                            
                            for (int i = 0; i <= numSamples; i++)
                            {
                                double t = edgeCurve.Domain.ParameterAt((double)i / numSamples);
                                Point3d pt = edgeCurve.PointAt(t);
                                Point3d projected = projectionPlane.ClosestPoint(pt);
                                projectedPoints.Add(new Point3d(projected.X, projected.Y, 0));
                            }
                        }
                    }
                }
                
                if (projectedPoints.Count < 3)
                {
                    return null;
                }
                
                // Remove duplicate points
                List<Point3d> uniquePoints = new List<Point3d>();
                double tolerance = 0.1; // 0.1mm tolerance
                foreach (var pt in projectedPoints)
                {
                    bool isDuplicate = false;
                    foreach (var existing in uniquePoints)
                    {
                        double dist = Math.Sqrt(Math.Pow(pt.X - existing.X, 2) + Math.Pow(pt.Y - existing.Y, 2));
                        if (dist < tolerance)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    if (!isDuplicate)
                    {
                        uniquePoints.Add(pt);
                    }
                }
                
                if (uniquePoints.Count < 3)
                {
                    return null;
                }
                
                // Find outer boundary by selecting outermost points in each direction
                Point3d center = new Point3d(0, 0, 0);
                foreach (var pt in uniquePoints)
                {
                    center += pt;
                }
                center /= uniquePoints.Count;
                
                // Get outermost points in each direction (360 directions, 1 degree resolution)
                Dictionary<int, Point3d> outerPoints = new Dictionary<int, Point3d>();
                int numAngles = 360;
                
                foreach (var pt in uniquePoints)
                {
                    double angle = Math.Atan2(pt.Y - center.Y, pt.X - center.X);
                    int angleIndex = (int)((angle + Math.PI) / (2.0 * Math.PI) * numAngles) % numAngles;
                    
                    double dist = Math.Sqrt(Math.Pow(pt.X - center.X, 2) + Math.Pow(pt.Y - center.Y, 2));
                    
                    if (!outerPoints.ContainsKey(angleIndex) || 
                        dist > Math.Sqrt(Math.Pow(outerPoints[angleIndex].X - center.X, 2) + 
                                        Math.Pow(outerPoints[angleIndex].Y - center.Y, 2)))
                    {
                        outerPoints[angleIndex] = pt;
                    }
                }
                
                if (outerPoints.Count < 3)
                {
                    return null;
                }
                
                // Sort by angle to get ordered boundary
                List<Point3d> sortedPoints = new List<Point3d>(outerPoints.Values);
                sortedPoints.Sort((a, b) =>
                {
                    double angleA = Math.Atan2(a.Y - center.Y, a.X - center.X);
                    double angleB = Math.Atan2(b.Y - center.Y, b.X - center.X);
                    return angleA.CompareTo(angleB);
                });
                
                // Create closed curve from sorted points
                Polyline polyline = new Polyline(sortedPoints);
                if (!polyline.IsClosed && sortedPoints.Count > 0)
                {
                    polyline.Add(sortedPoints[0]); // Close
                }
                
                return new PolylineCurve(polyline);
            }
            catch
            {
                return null;
            }
        }

        // Get geometry footprint (projection onto XY plane) - works for irregular geometries and circles
        // Returns the outer boundary curve of the geometry projected onto XY plane
        private Curve GetGeometryFootprint(Mesh mesh)
        {
            if (mesh == null || !mesh.IsValid) return null;
            
            try
            {
                // Method 1: Project naked edges onto XY plane and create boundary curve
                Polyline[] nakedEdges = mesh.GetNakedEdges();
                
                if (nakedEdges == null || nakedEdges.Length == 0)
                {
                    return null;
                }
                
                // Project all edges onto XY plane (z = 0)
                Plane projectionPlane = new Plane(new Point3d(0, 0, 0), Vector3d.ZAxis);
                List<Point3d> projectedPoints = new List<Point3d>();
                
                foreach (var edge in nakedEdges)
                {
                    if (edge != null && edge.Count >= 2)
                    {
                        foreach (var pt in edge)
                        {
                            Point3d projected = projectionPlane.ClosestPoint(pt);
                            projectedPoints.Add(new Point3d(projected.X, projected.Y, 0));
                        }
                    }
                }
                
                if (projectedPoints.Count < 3)
                {
                    return null;
                }
                
                // Remove duplicate points
                List<Point3d> uniquePoints = new List<Point3d>();
                double tolerance = 0.1; // 0.1mm tolerance
                foreach (var pt in projectedPoints)
                {
                    bool isDuplicate = false;
                    foreach (var existing in uniquePoints)
                    {
                        double dist = Math.Sqrt(Math.Pow(pt.X - existing.X, 2) + Math.Pow(pt.Y - existing.Y, 2));
                        if (dist < tolerance)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    if (!isDuplicate)
                    {
                        uniquePoints.Add(pt);
                    }
                }
                
                if (uniquePoints.Count < 3)
                {
                    return null;
                }
                
                // Find outer boundary by selecting outermost points in each direction
                Point3d center = new Point3d(0, 0, 0);
                foreach (var pt in uniquePoints)
                {
                    center += pt;
                }
                center /= uniquePoints.Count;
                
                // Get outermost points in each direction (360 directions, 1 degree resolution)
                Dictionary<int, Point3d> outerPoints = new Dictionary<int, Point3d>();
                int numAngles = 360;
                
                foreach (var pt in uniquePoints)
                {
                    double angle = Math.Atan2(pt.Y - center.Y, pt.X - center.X);
                    int angleIndex = (int)((angle + Math.PI) / (2.0 * Math.PI) * numAngles) % numAngles;
                    
                    double dist = Math.Sqrt(Math.Pow(pt.X - center.X, 2) + Math.Pow(pt.Y - center.Y, 2));
                    
                    if (!outerPoints.ContainsKey(angleIndex) || 
                        dist > Math.Sqrt(Math.Pow(outerPoints[angleIndex].X - center.X, 2) + 
                                        Math.Pow(outerPoints[angleIndex].Y - center.Y, 2)))
                    {
                        outerPoints[angleIndex] = pt;
                    }
                }
                
                if (outerPoints.Count < 3)
                {
                    return null;
                }
                
                // Sort by angle to get ordered boundary
                List<Point3d> sortedPoints = new List<Point3d>(outerPoints.Values);
                sortedPoints.Sort((a, b) =>
                {
                    double angleA = Math.Atan2(a.Y - center.Y, a.X - center.X);
                    double angleB = Math.Atan2(b.Y - center.Y, b.X - center.X);
                    return angleA.CompareTo(angleB);
                });
                
                // Create closed curve from sorted points
                Polyline polyline = new Polyline(sortedPoints);
                if (!polyline.IsClosed && sortedPoints.Count > 0)
                {
                    polyline.Add(sortedPoints[0]); // Close
                }
                
                return new PolylineCurve(polyline);
            }
            catch
            {
                return null;
            }
        }

        private Mesh EnsureMesh(IGH_GeometricGoo geo)
        {
            if (geo == null) return null;

            // Handle Mesh
            if (geo is GH_Mesh ghMesh)
            {
                Mesh m = ghMesh.Value.DuplicateMesh();
                m.UnifyNormals();
                m.Normals.ComputeNormals();
                return m;
            }

            // Handle Brep
            if (geo is GH_Brep ghBrep)
            {
                Brep brep = ghBrep.Value;
                Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                if (meshes == null || meshes.Length == 0) return null;

                Mesh combined = new Mesh();
                foreach (var m in meshes)
                {
                    combined.Append(m);
                }
                combined.UnifyNormals();
                combined.Normals.ComputeNormals();
                return combined;
            }

            // Handle Surface - treat same as Brep
            if (geo is GH_Surface ghSurface)
            {
                // GH_Surface.Value can be Brep or Surface, handle both cases
                object surfaceValue = ghSurface.Value;
                Brep brep = null;
                
                if (surfaceValue is Brep brepValue)
                {
                    brep = brepValue;
                }
                else if (surfaceValue is Surface surface)
                {
                    brep = surface.ToBrep();
                }
                
                if (brep != null && brep.IsValid)
                {
                    Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                    if (meshes != null && meshes.Length > 0)
                    {
                        Mesh combined = new Mesh();
                        foreach (var m in meshes)
                        {
                            if (m != null) combined.Append(m);
                        }
                        combined.UnifyNormals();
                        combined.Normals.ComputeNormals();
                        return combined;
                    }
                }
            }

            return null;
        }

        // Generic Z sampling function that works with both Mesh and Brep
        private double? ZAtXY(Mesh mesh, Brep brep, double x, double y, double zMin, double zMax)
        {
            if (brep != null)
            {
                return ZAtXYBrep(brep, x, y, zMin, zMax);
            }
            else if (mesh != null)
            {
                return ZAtXYMesh(mesh, x, y, zMin, zMax);
            }
            return null;
        }
        
        private double? ZAtXYMesh(Mesh mesh, double x, double y, double zMin, double zMax)
        {
            Point3d a = new Point3d(x, y, zMax + 1.0);
            Point3d b = new Point3d(x, y, zMin - 1.0);
            Line line = new Line(a, b);

#if NET48
            // Rhino 7: MeshLine requires faceIds parameter
            int[] faceIds;
            var intersections = Rhino.Geometry.Intersect.Intersection.MeshLine(mesh, line, out faceIds);
#else
            // Rhino 8: MeshLine doesn't require faceIds parameter
            var intersections = Rhino.Geometry.Intersect.Intersection.MeshLine(mesh, line);
#endif
            if (intersections != null)
            {
                var intersectionList = intersections.ToList();
                if (intersectionList.Count > 0)
                {
                    double maxZ = double.MinValue;
                    foreach (var pt in intersectionList)
                    {
                        if (pt.Z > maxZ) maxZ = pt.Z;
                    }
                    return maxZ;
                }
            }
            return null;
        }
        
        // Sample Z height from Brep at XY coordinates (more precise than Mesh)
        private double? ZAtXYBrep(Brep brep, double x, double y, double zMin, double zMax)
        {
            if (brep == null || !brep.IsValid) return null;
            
            try
            {
                // Create a vertical line at XY coordinates
                Point3d a = new Point3d(x, y, zMax + 1.0);
                Point3d b = new Point3d(x, y, zMin - 1.0);
                Line line = new Line(a, b);
                Curve lineCurve = new LineCurve(line);
                
                // Intersect line with each face of the Brep
                List<Point3d> allIntersections = new List<Point3d>();
                
                foreach (var face in brep.Faces)
                {
                    if (face == null) continue;
                    
                    // Intersect line with face surface
                    var intersections = Rhino.Geometry.Intersect.Intersection.CurveSurface(
                        lineCurve, face.ToNurbsSurface(), 0.01, 0.01);
                    
                    if (intersections != null)
                    {
                        foreach (var intersection in intersections)
                        {
                            if (intersection != null)
                            {
                                Point3d pt = lineCurve.PointAt(intersection.ParameterA);
                                // Only include points within the Z range
                                if (pt.Z >= zMin - 0.01 && pt.Z <= zMax + 0.01)
                                {
                                    allIntersections.Add(pt);
                                }
                            }
                        }
                    }
                }
                
                if (allIntersections.Count > 0)
                {
                    // Return the highest Z value (topmost intersection)
                    double maxZ = double.MinValue;
                    foreach (var pt in allIntersections)
                    {
                        if (pt.Z > maxZ) maxZ = pt.Z;
                    }
                    return maxZ;
                }
            }
            catch
            {
                // Fallback: return null if intersection fails
            }
            
            return null;
        }

        // Move a point inward by specified distance to ensure it's inside geometry
        // Returns corrected point, or null if correction failed
        private Point3d? MovePointInward(Mesh mesh, Brep brep, Point3d point, double inwardDistance, double zMin, double zMax)
        {
            // Check if point is already inside
            double? z = ZAtXY(mesh, brep, point.X, point.Y, zMin, zMax);
            if (z.HasValue)
            {
                // Already inside, but move 0.01mm further inward for safety
                BoundingBox bbox = mesh.GetBoundingBox(true);
                Point3d center = bbox.Center;
                Vector3d inwardDir = center - point;
                inwardDir.Z = 0; // Keep in XY plane
                inwardDir.Unitize();
                
                Point3d movedPoint = point + inwardDir * inwardDistance;
                double? zMoved = ZAtXY(mesh, brep, movedPoint.X, movedPoint.Y, zMin, zMax);
                if (zMoved.HasValue)
                {
                    return new Point3d(movedPoint.X, movedPoint.Y, zMoved.Value);
                }
                return new Point3d(point.X, point.Y, z.Value);
            }
            
            // Point is outside - find boundary and move inward
            // Calculate direction from geometry center to point
            BoundingBox bbox2 = (mesh != null) ? mesh.GetBoundingBox(true) : brep.GetBoundingBox(true);
            Point3d center2 = bbox2.Center;
            Vector3d outwardDir = point - center2;
            outwardDir.Z = 0;
            outwardDir.Unitize();
            
            // Binary search to find boundary
            Point3d inside = center2;
            Point3d outside = point;
            double tolerance = 0.01;
            
            for (int i = 0; i < 20; i++)
            {
                Point3d mid = new Point3d(
                    (inside.X + outside.X) / 2.0,
                    (inside.Y + outside.Y) / 2.0,
                    (inside.Z + outside.Z) / 2.0
                );
                
                double dist = inside.DistanceTo(outside);
                if (dist < tolerance) break;
                
                double? zMid = ZAtXY(mesh, brep, mid.X, mid.Y, zMin, zMax);
                if (zMid.HasValue)
                {
                    inside = new Point3d(mid.X, mid.Y, zMid.Value);
                }
                else
                {
                    outside = mid;
                }
            }
            
            // Move from boundary point inward
            Vector3d inwardDir2 = center2 - inside;
            inwardDir2.Z = 0;
            inwardDir2.Unitize();
            
            Point3d corrected = inside + inwardDir2 * inwardDistance;
            double? zCorrected = ZAtXY(mesh, brep, corrected.X, corrected.Y, zMin, zMax);
            if (zCorrected.HasValue)
            {
                return new Point3d(corrected.X, corrected.Y, zCorrected.Value);
            }
            
            return new Point3d(inside.X, inside.Y, ZAtXY(mesh, brep, inside.X, inside.Y, zMin, zMax) ?? inside.Z);
        }
        
        // Find intersection point between a line and geometry boundary using binary search
        // lineStart and lineEnd are points in world coordinates (already rotated)
        // Returns the intersection point, or null if not found
        private Point3d? FindBoundaryIntersection(Mesh mesh, Brep brep, Point3d lineStart, Point3d lineEnd, double zMin, double zMax)
        {
            // Binary search for boundary intersection
            Point3d inside = lineStart; // Known inside point
            Point3d outside = lineEnd;  // Known outside point
            double tolerance = 0.01; // 0.01mm tolerance
            
            // Check if we have valid inside/outside points
            double? zInside = ZAtXY(mesh, brep, inside.X, inside.Y, zMin, zMax);
            if (!zInside.HasValue) return null; // lineStart should be inside
            
            // Binary search
            for (int i = 0; i < 20; i++) // Max 20 iterations for binary search
            {
                Point3d mid = new Point3d(
                    (inside.X + outside.X) / 2.0,
                    (inside.Y + outside.Y) / 2.0,
                    (inside.Z + outside.Z) / 2.0
                );
                
                double dist = inside.DistanceTo(outside);
                if (dist < tolerance)
                {
                    // Found boundary - return the inside point (last point on geometry)
                    return new Point3d(inside.X, inside.Y, zInside.Value);
                }
                
                double? zMid = ZAtXY(mesh, brep, mid.X, mid.Y, zMin, zMax);
                if (zMid.HasValue)
                {
                    // Mid point is inside - move inside point to mid
                    inside = new Point3d(mid.X, mid.Y, zMid.Value);
                    zInside = zMid;
                }
                else
                {
                    // Mid point is outside - move outside point to mid
                    outside = mid;
                }
            }
            
            // Return the inside point (closest to boundary)
            return new Point3d(inside.X, inside.Y, zInside.Value);
        }

        private (List<List<Point3d>> segments, List<Point3d> pathPoints) GenerateBoustrophedonPath(
            Mesh mesh, Brep brep, BoundingBox bbox, double dx, double dy, bool startLeft, double angleRad, double zMin, double zMax)
        {
            double xMin = bbox.Min.X;
            double xMax = bbox.Max.X;
            double yMin = bbox.Min.Y;
            double yMax = bbox.Max.Y;
            // zMin and zMax are now parameters passed to the function

            // Calculate rotation center (bounding box center)
            Point3d center = bbox.Center;
            double centerX = center.X;
            double centerY = center.Y;

            // Rotation matrix components
            double cosAngle = Math.Cos(angleRad);
            double sinAngle = Math.Sin(angleRad);

            // Helper function to rotate a point around center
            Func<double, double, Point3d> rotatePoint = (x, y) =>
            {
                // Translate to origin
                double xRel = x - centerX;
                double yRel = y - centerY;
                
                // Rotate
                double xRot = xRel * cosAngle - yRel * sinAngle;
                double yRot = xRel * sinAngle + yRel * cosAngle;
                
                // Translate back
                return new Point3d(xRot + centerX, yRot + centerY, 0);
            };

            // Generate Y rows
            List<double> yVals = new List<double>();
            for (double y = yMin; y <= yMax + 1e-9; y += dy)
            {
                yVals.Add(y);
            }

            List<List<Point3d>> segments = new List<List<Point3d>>();

            for (int row = 0; row < yVals.Count; row++)
            {
                double yy = yVals[row];

                // Generate X samples
                List<double> xs = new List<double>();
                for (double x = xMin; x <= xMax + 1e-9; x += dx)
                {
                    xs.Add(x);
                }

                // Boustrophedon direction
                bool forward = (startLeft && (row % 2 == 0)) || (!startLeft && (row % 2 == 1));
                List<double> xSeq = forward ? xs : xs.AsEnumerable().Reverse().ToList();

                List<Point3d> pts = new List<Point3d>();
                bool currentlyInside = false;
                Point3d? lastValidPoint = null;
                Point3d? firstValidPoint = null;
                
                foreach (double xx in xSeq)
                {
                    // Rotate the sampling point
                    Point3d rotatedPt = rotatePoint(xx, yy);
                    
                    // Sample Z at rotated coordinates
                    double? z = ZAtXY(mesh, brep, rotatedPt.X, rotatedPt.Y, zMin, zMax);
                    bool isInside = z.HasValue;
                    
                    if (isInside)
                    {
                        // Point is inside geometry - add it
                        Point3d pt = new Point3d(rotatedPt.X, rotatedPt.Y, z.Value);
                        pts.Add(pt);
                        if (firstValidPoint == null) firstValidPoint = pt;
                        lastValidPoint = pt;
                        currentlyInside = true;
                    }
                    else
                    {
                        // Point is outside geometry
                        if (currentlyInside && lastValidPoint.HasValue)
                        {
                            // We just transitioned from inside to outside
                            // Extend the line to the boundary using binary search, then move 0.01mm inward
                            Point3d? boundaryPt = FindBoundaryIntersection(mesh, brep, lastValidPoint.Value, rotatedPt, zMin, zMax);
                            if (boundaryPt.HasValue)
                            {
                                // Move boundary point 0.01mm inward to prevent edge connection issues
                                Point3d? inwardPt = MovePointInward(mesh, brep, boundaryPt.Value, 0.01, zMin, zMax);
                                if (inwardPt.HasValue)
                                {
                                    pts.Add(inwardPt.Value);
                                }
                                else
                                {
                                    pts.Add(boundaryPt.Value);
                                }
                            }
                        }
                        currentlyInside = false;
                        
                        // If we have enough points, save the segment
                        if (pts.Count >= 2)
                        {
                            segments.Add(new List<Point3d>(pts));
                            pts.Clear();
                            lastValidPoint = null;
                            firstValidPoint = null;
                        }
                    }
                }
                
                // Handle case where line starts inside geometry - extend to boundary at the start
                if (pts.Count >= 2 && firstValidPoint.HasValue)
                {
                    // Find the first point in the sequence (opposite of forward direction)
                    double firstX = forward ? xMin : xMax;
                    Point3d firstRotatedPt = rotatePoint(firstX, yy);
                    
                    // Check if first point is outside
                    double? zFirst = ZAtXY(mesh, brep, firstRotatedPt.X, firstRotatedPt.Y, zMin, zMax);
                    if (!zFirst.HasValue)
                    {
                        // First point is outside - extend line to boundary, then move 0.01mm inward
                        Point3d? boundaryPt = FindBoundaryIntersection(mesh, brep, firstValidPoint.Value, firstRotatedPt, zMin, zMax);
                        if (boundaryPt.HasValue)
                        {
                            // Move boundary point 0.01mm inward
                            Point3d? inwardPt = MovePointInward(mesh, brep, boundaryPt.Value, 0.01, zMin, zMax);
                            if (inwardPt.HasValue)
                            {
                                pts.Insert(0, inwardPt.Value);
                            }
                            else
                            {
                                pts.Insert(0, boundaryPt.Value);
                            }
                        }
                    }
                }
                
                // Handle case where line ends inside geometry - extend to boundary at the end
                if (pts.Count >= 2 && lastValidPoint.HasValue)
                {
                    // Find the last point in the sequence
                    double lastX = forward ? xMax : xMin;
                    Point3d lastRotatedPt = rotatePoint(lastX, yy);
                    
                    // Check if last point is outside
                    double? zLast = ZAtXY(mesh, brep, lastRotatedPt.X, lastRotatedPt.Y, zMin, zMax);
                    if (!zLast.HasValue)
                    {
                        // Last point is outside - extend line to boundary, then move 0.01mm inward
                        Point3d? boundaryPt = FindBoundaryIntersection(mesh, brep, lastValidPoint.Value, lastRotatedPt, zMin, zMax);
                        if (boundaryPt.HasValue)
                        {
                            // Move boundary point 0.01mm inward
                            Point3d? inwardPt = MovePointInward(mesh, brep, boundaryPt.Value, 0.01, zMin, zMax);
                            if (inwardPt.HasValue)
                            {
                                pts.Add(inwardPt.Value);
                            }
                            else
                            {
                                pts.Add(boundaryPt.Value);
                            }
                        }
                    }
                }

                if (pts.Count >= 2)
                {
                    segments.Add(pts);
                }
            }

            // Connect segments into continuous path
            // All connections follow the surface geometry (no retracts, no vertical movements)
            List<Point3d> pathPoints = new List<Point3d>();
            
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (i == 0)
                {
                    pathPoints.AddRange(seg);
                    continue;
                }

                Point3d prev = pathPoints[pathPoints.Count - 1];
                Point3d first = seg[0];

                // Calculate distance between segment end and next segment start
                double dyAbs = Math.Abs(first.Y - prev.Y);
                double dxAbs = Math.Abs(first.X - prev.X);
                double dist = Math.Sqrt(dxAbs * dxAbs + dyAbs * dyAbs);

                // Always connect following the surface geometry
                // Use smaller step size for smoother connections, especially for curved geometries
                // Step size based on toolpath spacing to maintain precision
                double stepSize = Math.Min(Math.Min(dx, dy) * 0.5, 1.0); // Half of min step size, max 1mm
                int steps = Math.Max((int)(dist / stepSize), 1);
                // No artificial limit - precision is more important than point count

                // Create connection points following the surface
                // Ensure all connection points are inside geometry (at least 0.01mm inward)
                for (int t = 1; t <= steps; t++)
                {
                    double tt = (double)t / steps;
                    double xi = prev.X + tt * (first.X - prev.X);
                    double yi = prev.Y + tt * (first.Y - prev.Y);
                    
                    // Sample Z from geometry to follow surface geometry
                    double? zi = ZAtXY(mesh, brep, xi, yi, zMin, zMax);
                    
                    if (!zi.HasValue)
                    {
                        // Point is outside geometry - move it 0.01mm inward
                        Point3d currentPt = new Point3d(xi, yi, (prev.Z + first.Z) / 2.0);
                        Point3d? correctedPt = MovePointInward(mesh, brep, currentPt, 0.01, zMin, zMax);
                        
                        if (correctedPt.HasValue)
                        {
                            xi = correctedPt.Value.X;
                            yi = correctedPt.Value.Y;
                            zi = correctedPt.Value.Z;
                        }
                        else
                        {
                            // Fallback: interpolate Z if correction failed
                            zi = prev.Z + tt * (first.Z - prev.Z);
                        }
                    }
                    
                    pathPoints.Add(new Point3d(xi, yi, zi.Value));
                }

                pathPoints.AddRange(seg);
            }

            return (segments, pathPoints);
        }

        // Generate outline contour from actual geometry footprint (projection onto XY plane)
        // Uses GetGeometryFootprint to get the actual footprint, then applies an outward offset
        private List<Point3d> GenerateOutlineContour(Mesh mesh, double zBottom, double offsetDistance)
        {
            List<Point3d> outlinePoints = new List<Point3d>();
            
            try
            {
                // Use the existing GetGeometryFootprint function to get the actual footprint
                Curve footprintCurve = GetGeometryFootprint(mesh);
                
                if (footprintCurve == null || !footprintCurve.IsValid)
                {
                    // Fallback: use bounding box if footprint extraction failed
                    return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
                }
                
                // Apply outward offset to the footprint curve
                Plane offsetPlane = new Plane(new Point3d(0, 0, zBottom), Vector3d.ZAxis);
                Curve[] offsetCurves = footprintCurve.Offset(offsetPlane, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                
                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    // Use the first offset curve (should be the outer one)
                    Curve offsetCurve = offsetCurves[0];
                    
                    // Sample points from the offset curve
                    double tolerance = 0.1; // 0.1mm tolerance for point sampling
                    offsetCurve.DivideByLength(tolerance, false, out Point3d[] samplePoints);
                    
                    if (samplePoints != null && samplePoints.Length > 0)
                    {
                        foreach (var pt in samplePoints)
                        {
                            outlinePoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                        }
                        
                        // Ensure the outline is closed
                        if (outlinePoints.Count > 0 && outlinePoints[0].DistanceTo(outlinePoints[outlinePoints.Count - 1]) > tolerance)
                        {
                            outlinePoints.Add(outlinePoints[0]); // Close the loop
                        }
                    }
                    else
                    {
                        // Fallback: sample directly from curve
                        int numSamples = Math.Max(50, (int)(offsetCurve.GetLength() / tolerance));
                        for (int i = 0; i <= numSamples; i++)
                        {
                            double t = offsetCurve.Domain.ParameterAt((double)i / numSamples);
                            Point3d pt = offsetCurve.PointAt(t);
                            outlinePoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                        }
                    }
                }
                else
                {
                    // Offset failed - use original footprint with manual offset or fallback to bounding box
                    return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
                }
            }
            catch (Exception)
            {
                // Fallback: use bounding box with offset
                return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
            }
            
            return outlinePoints;
        }
        
        // Generate outline contour from Brep footprint (projection onto XY plane)
        // Uses GetGeometryFootprintFromBrep to get the actual footprint, then applies an outward offset
        private List<Point3d> GenerateOutlineContourFromBrep(Brep brep, double zBottom, double offsetDistance)
        {
            List<Point3d> outlinePoints = new List<Point3d>();
            
            try
            {
                // Use the existing GetGeometryFootprintFromBrep function to get the actual footprint
                Curve footprintCurve = GetGeometryFootprintFromBrep(brep);
                
                if (footprintCurve == null || !footprintCurve.IsValid)
                {
                    // Fallback: use bounding box if footprint extraction failed
                    BoundingBox bbox = brep.GetBoundingBox(true);
                    return GenerateOutlineFromBoundingBox(bbox, zBottom, offsetDistance);
                }
                
                // Apply outward offset to the footprint curve
                Plane offsetPlane = new Plane(new Point3d(0, 0, zBottom), Vector3d.ZAxis);
                Curve[] offsetCurves = footprintCurve.Offset(offsetPlane, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                
                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    // Use the first offset curve (should be the outer one)
                    Curve offsetCurve = offsetCurves[0];
                    
                    // Sample points from the offset curve
                    double tolerance = 0.1; // 0.1mm tolerance for point sampling
                    offsetCurve.DivideByLength(tolerance, false, out Point3d[] samplePoints);
                    
                    if (samplePoints != null && samplePoints.Length > 0)
                    {
                        foreach (var pt in samplePoints)
                        {
                            outlinePoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                        }
                        
                        // Ensure the outline is closed
                        if (outlinePoints.Count > 0 && outlinePoints[0].DistanceTo(outlinePoints[outlinePoints.Count - 1]) > tolerance)
                        {
                            outlinePoints.Add(outlinePoints[0]); // Close the loop
                        }
                    }
                    else
                    {
                        // Fallback: sample directly from curve
                        int numSamples = Math.Max(50, (int)(offsetCurve.GetLength() / tolerance));
                        for (int i = 0; i <= numSamples; i++)
                        {
                            double t = offsetCurve.Domain.ParameterAt((double)i / numSamples);
                            Point3d pt = offsetCurve.PointAt(t);
                            outlinePoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                        }
                    }
                }
                else
                {
                    // Offset failed - use original footprint with manual offset or fallback to bounding box
                    BoundingBox bbox = brep.GetBoundingBox(true);
                    return GenerateOutlineFromBoundingBox(bbox, zBottom, offsetDistance);
                }
            }
            catch (Exception)
            {
                // Fallback: use bounding box with offset
                BoundingBox bbox = brep.GetBoundingBox(true);
                return GenerateOutlineFromBoundingBox(bbox, zBottom, offsetDistance);
            }
            
            return outlinePoints;
        }
        
        // Helper: Generate outline from naked edges (alternative method)
        private List<Point3d> GenerateOutlineFromNakedEdges(Mesh mesh, double zBottom, double offsetDistance)
        {
            List<Point3d> outlinePoints = new List<Point3d>();
            Polyline[] nakedEdges = mesh.GetNakedEdges();
            
            if (nakedEdges == null || nakedEdges.Length == 0)
            {
                return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
            }
            
            // Project edges onto XY plane
            List<Point3d> projectedPoints = new List<Point3d>();
            foreach (var edge in nakedEdges)
            {
                if (edge != null && edge.Count >= 2)
                {
                    foreach (var pt in edge)
                    {
                        projectedPoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                    }
                }
            }
            
            if (projectedPoints.Count < 3)
            {
                return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
            }
            
            Curve boundaryCurve = CreateBoundaryCurveFromPoints(projectedPoints, zBottom);
            if (boundaryCurve == null || !boundaryCurve.IsValid)
            {
                return GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
            }
            
            // Apply offset
            Plane offsetPlane = new Plane(new Point3d(0, 0, zBottom), Vector3d.ZAxis);
            Curve[] offsetCurves = boundaryCurve.Offset(offsetPlane, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
            
            if (offsetCurves != null && offsetCurves.Length > 0)
            {
                Curve offsetCurve = offsetCurves[0];
                double tolerance = 0.1;
                offsetCurve.DivideByLength(tolerance, false, out Point3d[] samplePoints);
                
                if (samplePoints != null && samplePoints.Length > 0)
                {
                    foreach (var pt in samplePoints)
                    {
                        outlinePoints.Add(new Point3d(pt.X, pt.Y, zBottom));
                    }
                }
            }
            
            return outlinePoints.Count > 0 ? outlinePoints : GenerateOutlineFromBoundingBox(mesh, zBottom, offsetDistance);
        }
        
        // Helper: Create boundary curve from points (find outer boundary)
        private Curve CreateBoundaryCurveFromPoints(List<Point3d> points, double zBottom)
        {
            if (points.Count < 3) return null;
            
            try
            {
                // Calculate center
                Point3d center = new Point3d(0, 0, zBottom);
                foreach (var pt in points)
                {
                    center += pt;
                }
                center /= points.Count;
                
                // Find the outermost points in each direction (to get the actual footprint boundary)
                // Sort points by angle from center, but keep only the outermost point for each angle
                Dictionary<int, Point3d> outerPoints = new Dictionary<int, Point3d>();
                int numAngles = 360; // 1 degree resolution
                
                foreach (var pt in points)
                {
                    double angle = Math.Atan2(pt.Y - center.Y, pt.X - center.X);
                    int angleIndex = (int)((angle + Math.PI) / (2.0 * Math.PI) * numAngles) % numAngles;
                    
                    double dist = Math.Sqrt(Math.Pow(pt.X - center.X, 2) + Math.Pow(pt.Y - center.Y, 2));
                    
                    if (!outerPoints.ContainsKey(angleIndex) || 
                        dist > Math.Sqrt(Math.Pow(outerPoints[angleIndex].X - center.X, 2) + 
                                        Math.Pow(outerPoints[angleIndex].Y - center.Y, 2)))
                    {
                        outerPoints[angleIndex] = pt;
                    }
                }
                
                // Sort by angle to get ordered boundary
                List<Point3d> sortedPoints = new List<Point3d>(outerPoints.Values);
                sortedPoints.Sort((a, b) =>
                {
                    double angleA = Math.Atan2(a.Y - center.Y, a.X - center.X);
                    double angleB = Math.Atan2(b.Y - center.Y, b.X - center.X);
                    return angleA.CompareTo(angleB);
                });
                
                if (sortedPoints.Count < 3)
                {
                    // Fallback: use all points sorted by angle
                    sortedPoints = new List<Point3d>(points);
                    sortedPoints.Sort((a, b) =>
                    {
                        double angleA = Math.Atan2(a.Y - center.Y, a.X - center.X);
                        double angleB = Math.Atan2(b.Y - center.Y, b.X - center.X);
                        return angleA.CompareTo(angleB);
                    });
                }
                
                // Create polyline from sorted points
                Polyline polyline = new Polyline(sortedPoints);
                if (!polyline.IsClosed && sortedPoints.Count > 0)
                {
                    polyline.Add(sortedPoints[0]); // Close
                }
                
                return new PolylineCurve(polyline);
            }
            catch
            {
                // Fallback: create polyline from points directly
                try
                {
                    Polyline directPolyline = new Polyline(points);
                    if (!directPolyline.IsClosed && points.Count > 0)
                    {
                        directPolyline.Add(points[0]);
                    }
                    return new PolylineCurve(directPolyline);
                }
                catch
                {
                    return null;
                }
            }
        }
        
        // Helper: Generate outline from bounding box (fallback)
        // Overload for BoundingBox (used for Brep fallback)
        private List<Point3d> GenerateOutlineFromBoundingBox(BoundingBox bbox, double zBottom, double offsetDistance)
        {
            List<Point3d> outlinePoints = new List<Point3d>();
            
            // Create rectangle from bounding box with offset
            double xMin = bbox.Min.X - offsetDistance;
            double xMax = bbox.Max.X + offsetDistance;
            double yMin = bbox.Min.Y - offsetDistance;
            double yMax = bbox.Max.Y + offsetDistance;
            
            // Create closed rectangle outline
            outlinePoints.Add(new Point3d(xMin, yMin, zBottom));
            outlinePoints.Add(new Point3d(xMax, yMin, zBottom));
            outlinePoints.Add(new Point3d(xMax, yMax, zBottom));
            outlinePoints.Add(new Point3d(xMin, yMax, zBottom));
            outlinePoints.Add(new Point3d(xMin, yMin, zBottom)); // Close
            
            return outlinePoints;
        }
        
        private List<Point3d> GenerateOutlineFromBoundingBox(Mesh mesh, double zBottom, double offsetDistance)
        {
            List<Point3d> outlinePoints = new List<Point3d>();
            BoundingBox bbox = mesh.GetBoundingBox(true);
            double xMin = bbox.Min.X;
            double xMax = bbox.Max.X;
            double yMin = bbox.Min.Y;
            double yMax = bbox.Max.Y;
            
            outlinePoints.Add(new Point3d(xMin - offsetDistance, yMin - offsetDistance, zBottom));
            outlinePoints.Add(new Point3d(xMax + offsetDistance, yMin - offsetDistance, zBottom));
            outlinePoints.Add(new Point3d(xMax + offsetDistance, yMax + offsetDistance, zBottom));
            outlinePoints.Add(new Point3d(xMin - offsetDistance, yMax + offsetDistance, zBottom));
            return outlinePoints;
        }
        
        // Calculate average normal for outward offset
        private Vector3d CalculateAverageNormal(List<Point3d> points)
        {
            if (points.Count < 3) return Vector3d.ZAxis;
            
            // Calculate centroid
            Point3d centroid = new Point3d(0, 0, 0);
            foreach (var pt in points)
            {
                centroid += pt;
            }
            centroid /= points.Count;
            
            // Calculate average outward normal using cross products
            Vector3d avgNormal = new Vector3d(0, 0, 0);
            for (int i = 0; i < points.Count; i++)
            {
                int next = (i + 1) % points.Count;
                Vector3d v1 = points[i] - centroid;
                Vector3d v2 = points[next] - centroid;
                Vector3d normal = Vector3d.CrossProduct(v1, v2);
                normal.Unitize();
                avgNormal += normal;
            }
            avgNormal.Unitize();
            
            // Ensure it points outward (positive Z component for top view)
            if (avgNormal.Z < 0) avgNormal.Reverse();
            
            return avgNormal;
        }
        
        // Find closest point on outline to a given point
        private Point3d FindClosestPointOnOutline(List<Point3d> outlinePoints, Point3d targetPoint)
        {
            if (outlinePoints.Count == 0) return targetPoint;
            
            double minDist = double.MaxValue;
            Point3d closest = outlinePoints[0];
            
            foreach (var pt in outlinePoints)
            {
                double dist = pt.DistanceTo(targetPoint);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = pt;
                }
            }
            
            return closest;
        }
        
        // Find index of closest point in outline
        private int FindClosestPointIndex(List<Point3d> outlinePoints, Point3d targetPoint)
        {
            if (outlinePoints.Count == 0) return 0;
            
            double minDist = double.MaxValue;
            int closestIndex = 0;
            
            for (int i = 0; i < outlinePoints.Count; i++)
            {
                double dist = outlinePoints[i].DistanceTo(targetPoint);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestIndex = i;
                }
            }
            
            return closestIndex;
        }
        
        // Find connection path from last point to outline following the surface geometry
        // Returns a list of points that follow the surface from startPoint to outline
        private List<Point3d> FindConnectionPathToOutline(Point3d startPoint, List<Point3d> outlinePoints, Mesh mesh, Brep brep, double zMin, double zMax)
        {
            List<Point3d> connectionPath = new List<Point3d>();
            
            if (outlinePoints.Count == 0) return connectionPath;
            
            // Find closest point on outline
            Point3d targetPoint = FindClosestPointOnOutline(outlinePoints, startPoint);
            
            // Calculate distance
            double dx = targetPoint.X - startPoint.X;
            double dy = targetPoint.Y - startPoint.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            
            if (dist < 0.1)
            {
                // Already at outline
                connectionPath.Add(targetPoint);
                return connectionPath;
            }
            
            // Create connection path following the surface
            // Use step size based on distance to maintain precision
            // For longer distances, use slightly larger steps, but always maintain precision
            double stepSize = Math.Min(0.5, dist / 1000.0); // 0.5mm steps, or smaller for very long distances
            stepSize = Math.Max(stepSize, 0.1); // Minimum 0.1mm for precision
            int steps = Math.Max((int)(dist / stepSize), 1);
            // No artificial limit - precision is more important than point count
            
            // Sample points along the path, following surface geometry
            for (int t = 1; t <= steps; t++)
            {
                double tt = (double)t / steps;
                double xi = startPoint.X + tt * dx;
                double yi = startPoint.Y + tt * dy;
                
                // Sample Z from geometry to follow surface geometry
                double? zi = ZAtXY(mesh, brep, xi, yi, zMin, zMax);
                if (!zi.HasValue)
                {
                    // If outside geometry, interpolate Z linearly
                    zi = startPoint.Z + tt * (targetPoint.Z - startPoint.Z);
                }
                connectionPath.Add(new Point3d(xi, yi, zi.Value));
            }
            
            // Add target point at end
            connectionPath.Add(targetPoint);
            
            return connectionPath;
        }

        private int UU(double mm)
        {
            // mm -> 1/100 mm (Zünd Units)
            return (int)Math.Round(mm * 100.0);
        }

        private string BuildPLTFromPath(
            List<List<Point3d>> segments,
            List<Point3d> pathPoints,
            double feedSpeed,      // VS (mm/s)
            double rapidSpeed,     // VU (mm/s)
            double zUp,
            double materialThickness, // Material thickness in mm
            int zpOffset,          // ZP offset for through-cutting (0 = cut to 0, +1 etc = offset)
            int defaultTool,       // Default tool: 11, 21, or 31
            List<int> toolAssignments, // Tool for each segment
            Dictionary<int, double> toolSpeedMap, // Spindle speed for each tool
            double defaultSpindleSpeed, // Default RPM for XX150
            int accelDown,         // Acceleration for cutting (1-4)
            int accelUp,           // Acceleration for rapid (1-4)
            double vacuumZone,     // Vacuum zone in increments (based on bounding box width)
            int vacuumStrength,    // Vacuum strength level (0-10)
            int underlayThickness, // Underlay thickness in increments (100 increments = 1mm)
            Point3d parkXY)
        {
            List<string> lines = new List<string>();

            // Header (Sample-compatible)
            // PB2: Vacuum area control - State 1 = on, State 0 = off
            // If vacuumStrength > 0: PB2,1,{level} (set turbine level and switch on)
            // If vacuumStrength = 0: PB2,0 (switch off)
            if (vacuumStrength > 0 && vacuumStrength <= 10)
            {
                lines.Add($"PB2,1,{vacuumStrength};"); // PB2: Switch vacuum area on and set turbine level (1-10)
            }
            else
            {
                lines.Add("PB2,0;"); // PB2: Switch vacuum area off
            }
            lines.Add("ZT1;MA;"); // ZT1: Tool down, MA: Move absolute coordinates
            lines.Add($"VS{feedSpeed:G6};"); // VS: Set feed speed (Vorschubgeschwindigkeit) in mm/s
            lines.Add($"VU{rapidSpeed:G6};"); // VU: Set rapid speed (Eilganggeschwindigkeit) in mm/s
            lines.Add(""); // Empty line like in sample

            // Vacuum zone (SV command) - automatically calculated from bounding box width
            if (vacuumZone > 0)
            {
                int vacuumIncrements = (int)Math.Round(vacuumZone);
                lines.Add($"SV,{vacuumIncrements};"); // SV: Set vacuum zone width (in increments, 1cm = 1000 increments)
            }
            
            // Extraction height (XX306 command) - Materialstärke + 3.3mm (brush length)
            // 100 Inkremente = 1mm, so Materialstärke * 100 + 330 (3.3mm = 330 Inkremente)
            int extractionHeight = (int)Math.Round(materialThickness * 100.0) + 330;
            lines.Add($"XX306,{extractionHeight};"); // XX306: Set extraction position height (distance between router bit and extraction, in increments)
            
            lines.Add($"XX308,1,{underlayThickness};"); // XX308: Enable additional underlay support with thickness (standard: always on at start, thickness in increments)
            
            // AS (Acceleration): AS,unten,oben
            // Clamp values to valid range (1-4)
            accelDown = Math.Max(1, Math.Min(4, accelDown));
            accelUp = Math.Max(1, Math.Min(4, accelUp));
            lines.Add($"AS,{accelDown},{accelUp};"); // AS: Set acceleration (first param: cutting/feed acceleration 1-4, second param: rapid acceleration 1-4)

            // Path with tool changes
            if (segments == null || segments.Count == 0 || pathPoints == null || pathPoints.Count == 0)
            {
                lines.Add("ZT0;"); // ZT0: Tool up (retract tool)
                lines.Add($"PU{UU(parkXY.X)},{UU(parkXY.Y)};"); // PU: Pen up (move to park position without cutting)
                lines.Add("XX308,0;"); // XX308: Disable additional underlay support
                lines.Add("PB2,0;"); // PB2: Switch vacuum area off
                return string.Join("\n", lines);
            }

            // Z-axis coordinate system: Z=0 = table surface (where material is placed)
            // Material top surface = negative value (e.g., -3000 for 30mm material thickness)
            // Cutting into material = more negative (e.g., -500 = 5mm remain, 2500 removed)
            // Positive Z values are NOT allowed (would cut into table!)
            // Geometry Z coordinates are POSITIVE (representing material remaining, e.g., +5mm = 5mm remain)
            // Convert to CNC coordinates: CNC_Z = -geometryZ (directly negative)
            // Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments (5mm remain, 25mm removed for 30mm material)
            
            // Find highest Z value from geometry (most material remaining) to calculate ZP
            double maxGeometryZ = 0.0;
            foreach (var seg in segments)
            {
                foreach (var pt in seg)
                {
                    if (pt.Z > maxGeometryZ) maxGeometryZ = pt.Z;
                }
            }
            if (pathPoints != null && pathPoints.Count > 0)
            {
                foreach (var pt in pathPoints)
                {
                    if (pt.Z > maxGeometryZ) maxGeometryZ = pt.Z;
                }
            }
            
            // Convert geometry Z to CNC Z: CNC_Z = -geometryZ (directly negative)
            // Material top surface in CNC coordinates = -materialThickness
            double materialTopZ = -materialThickness; // Material top surface position (e.g., -30mm)
            
            // Highest cutting point in CNC coordinates (least negative = closest to table)
            double maxCNCZ = -maxGeometryZ; // e.g., geometry Z=5mm → CNC_Z = -5mm
            
            // ZP (Z-Position oben): Positive value in increments, absolute position above table (Z=0)
            // ZP = Materialstärke (in increments) + 100 (1mm safety margin)
            // Example: 30mm material → ZP = 3000 + 100 = 3100 increments (31mm above table)
            // 100 increments = 1mm
            // ZP is independent of geometry, only depends on material thickness
            int zpValue = (int)Math.Round(materialThickness * 100.0) + 100; // Material thickness in increments + 1mm safety (100 increments)
            lines.Add($"ZP{zpValue},{zpOffset};"); // ZP: Set Z-axis position (top position = material thickness + 1mm safety in increments, second param: offset for through-cutting)
            
            // Process segments with tool changes
            int currentTool = toolAssignments.Count > 0 ? toolAssignments[0] : defaultTool;
            
            // Initial tool setup
            if (currentTool == 11 || currentTool == 21 || currentTool == 31)
            {
                lines.Add($"SP{currentTool};"); // SP: Select tool (11 = left, 21 = middle, 31 = right)
            }
            
            // Initial spindle speed
            double currentSpindleSpeed = toolSpeedMap.ContainsKey(currentTool) ? toolSpeedMap[currentTool] : defaultSpindleSpeed;
            lines.Add($"XX150,{currentSpindleSpeed:F0};"); // XX150: Set spindle speed (RPM)
            
            // Process all segments as continuous path (no PU between segments)
            // First segment: Start with PU to first point, then MW for all points
            if (segments.Count > 0 && segments[0].Count > 0)
            {
                Point3d p0 = segments[0][0];
                lines.Add($"PU{UU(p0.X)},{UU(p0.Y)};"); // PU: Pen up (move to first point without cutting)
                // Convert geometry Z (positive = material remaining) to CNC Z (negative = cutting into material)
                // Formula: CNC_Z = -geometryZ (directly negative)
                // Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments (5mm remain, 25mm removed for 30mm material)
                double cncZ0 = -p0.Z;
                int z0Increments = UU(cncZ0);
                lines.Add($"MW{UU(p0.X)},{UU(p0.Y)},{z0Increments};"); // MW: Move with tool down (cutting move, Z negative = cutting into material)
                
                // Add remaining points of first segment
                for (int i = 1; i < segments[0].Count; i++)
                {
                    Point3d p = segments[0][i];
                    // Convert geometry Z to CNC Z: CNC_Z = -geometryZ (directly negative)
                    double cncZ = -p.Z;
                    int zIncrements = UU(cncZ);
                    lines.Add($"MW{UU(p.X)},{UU(p.Y)},{zIncrements};"); // MW: Move with tool down (cutting move, continuous path)
                }
            }
            
            // Process remaining segments with tool change logic (continuous path, no PU between segments)
            for (int segIdx = 1; segIdx < segments.Count; segIdx++)
            {
                if (segments[segIdx].Count == 0) continue;
                
                int segmentTool = segIdx < toolAssignments.Count ? toolAssignments[segIdx] : defaultTool;
                
                // Check if tool change is needed
                if (segmentTool != currentTool && (segmentTool == 11 || segmentTool == 21 || segmentTool == 31))
                {
                    // Tool change sequence
                    // 1. ZP before tool change
                    lines.Add($"ZP{zpValue},{zpOffset};"); // ZP: Retract Z-axis to safe position before tool change
                    
                    // 2. Move to tool change position
                    lines.Add("XX220;"); // XX220: Move to tool change position
                    
                    // 3. Select new tool
                    lines.Add($"SP{segmentTool};"); // SP: Select new tool (11 = left, 21 = middle, 31 = right)
                    
                    // 4. ZP after tool change
                    lines.Add($"ZP{zpValue},{zpOffset};"); // ZP: Set Z-axis position after tool change
                    
                    // 5. Set spindle speed for new tool
                    double newSpindleSpeed = toolSpeedMap.ContainsKey(segmentTool) ? toolSpeedMap[segmentTool] : defaultSpindleSpeed;
                    lines.Add($"XX150,{newSpindleSpeed:F0};"); // XX150: Set spindle speed for new tool (RPM)
                    
                    // 6. Move to first point of next segment (after tool change, we need PU)
                    Point3d segFirstAfterToolChange = segments[segIdx][0];
                    lines.Add($"PU{UU(segFirstAfterToolChange.X)},{UU(segFirstAfterToolChange.Y)};"); // PU: Pen up (move to first point of next segment without cutting)
                    
                    currentTool = segmentTool;
                    currentSpindleSpeed = newSpindleSpeed;
                }
                
                // Add segment points (continuous path: no PU between segments, directly MW)
                // If tool change happened, first point already has PU, so start with MW
                // If no tool change, continue directly with MW (continuous path)
                Point3d segFirst = segments[segIdx][0];
                // Convert geometry Z to CNC Z: CNC_Z = -geometryZ (directly negative)
                double cncZFirst = -segFirst.Z;
                int segZ0 = UU(cncZFirst);
                lines.Add($"MW{UU(segFirst.X)},{UU(segFirst.Y)},{segZ0};"); // MW: Move with tool down (cutting move, continuous path)
                
                // Add remaining points of segment (all MW, continuous path)
                for (int i = 1; i < segments[segIdx].Count; i++)
                {
                    Point3d p = segments[segIdx][i];
                    // Convert geometry Z to CNC Z: CNC_Z = -geometryZ (directly negative)
                    double cncZ = -p.Z;
                    int zIncrements = UU(cncZ);
                    lines.Add($"MW{UU(p.X)},{UU(p.Y)},{zIncrements};"); // MW: Move with tool down (cutting move, continuous path)
                }
            }

            // Pen up at end of cutting (before tool retract) - lift tool from material
            if (segments.Count > 0 && segments[segments.Count - 1].Count > 0)
            {
                Point3d lastPoint = segments[segments.Count - 1][segments[segments.Count - 1].Count - 1];
                lines.Add($"PU{UU(lastPoint.X)},{UU(lastPoint.Y)};"); // PU: Pen up (lift tool from material at end of cutting)
            }

            // Trailer
            lines.Add("ZT0;"); // ZT0: Tool up (retract tool)
            lines.Add($"PU{UU(parkXY.X)},{UU(parkXY.Y)};"); // PU: Pen up (move to park position without cutting)
            lines.Add("XX308,0;"); // XX308: Disable additional underlay support
            lines.Add("PB2,0;"); // PB2: Switch vacuum area off

            return string.Join("\n", lines);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCProgramIcon.png");
        public override Guid ComponentGuid => new Guid("C1D2E3F4-A5B6-7890-CDEF-123456789ABC");
    }
}

