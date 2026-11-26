using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;
using LARGERslicer.Types;
using LARGERslicer.Toolpaths;

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
            pManager.AddBooleanParameter("Add Outline", "Outline", "True to add outline contour at bottom to cut out the part. Its distance from the part is the radius of the Cutter; without a cutter the larger of Step X and Step Y.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Make PLT", "MakePLT", "True to generate PLT output", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Feed Speed", "VW", "Feed in cm/s, as the Zünd counts (VS, VW): 7 = 70 mm/s = 4.2 m/min. 0.1 to 100.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Rapid Speed", "VF", "Speed with the tool up in cm/s (VU): 100 = 1 m/s, the most the machine takes.", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Z Up", "Zup", "Not used: the Zünd retracts to Material Thickness + 1 mm (ZP). Kept so that older definitions open unchanged.", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Material Thickness", "MatThick", "Material thickness in mm. Used for: ZP top position calculation, XX306 extraction height (material thickness + 3.3mm), and validation. Geometry Z coordinates are POSITIVE (material remaining, e.g., +5mm = 5mm remain). CNC coordinates: Z=0 = table surface, material top = -material thickness, CNC_Z = -geometry Z (directly negative). Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments. Positive CNC Z values are NOT allowed (would cut into table).", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("ZP Offset", "ZPOffset", "ZP offset for through-cutting (0 = cut to 0, +1 etc = offset for through-cutting)", GH_ParamAccess.item, 0);
            pManager.AddGenericParameter("Cutter", "Cutter", "Cutter object from Cutter Selector component. Contains all cutter specifications for validation and tool selection. If not provided, uses Tool parameter.", GH_ParamAccess.item);
            pManager[13].Optional = true;
            pManager.AddIntegerParameter("Tool", "Tool", "Tool selection: 11 (left), 21 (middle), 31 (right) for SP command. Used if Cutter is not provided.", GH_ParamAccess.item, 31);
            pManager.AddNumberParameter("Max Cutting Depth", "MaxDepth", "Maximum possible cutting depth of the cutter in mm (cutter capability, NOT the actual cutting depth). This is the maximum depth the cutter can cut through material, limited by Saugglocke position (3.3mm above material surface). Used for validation only. The actual cutting depth comes from geometry Z coordinates. Example: 22.5mm for 6mm Fräser (max material thickness that can be cut through). Default: 22.5mm.", GH_ParamAccess.item, 22.5);
            pManager.AddNumberParameter("Spindle Speed", "RPM", "Spindle speed (RPM) for XX150 command. RM-A: 7000 to 50000.", GH_ParamAccess.item, 10000.0);
            pManager.AddIntegerParameter("Accel Down", "AccDown", "Acceleration for cutting (1-4, default: 2)", GH_ParamAccess.item, 2);
            pManager.AddIntegerParameter("Accel Up", "AccUp", "Acceleration for rapid (1-4, default: 4)", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Vacuum Strength", "VacStr", "Vacuum strength level (0-10, 0 = off, 10 = max)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Material Width", "MatWidth", "Material width in mm (Y-dimension). Used for vacuum zone calculation (SV command). If 0 or not provided, uses bounding box Y-dimension. For plates >= 430mm, vacuum zones are 80mm wide sections.", GH_ParamAccess.item, 0.0);
            pManager[20].Optional = true;
            pManager.AddIntegerParameter("Underlay Thickness", "Underlay", "Underlay thickness in increments (100 increments = 1mm, default: 200 = 2mm) for XX308 command", GH_ParamAccess.item, 200);
            
            // Tool Change Parameters (optional - for next version)
            pManager.AddIntegerParameter("Tool List", "Tools", "Optional: List of tools (11, 21, 31) for each path segment. If empty, uses single Tool parameter for all segments.", GH_ParamAccess.list);
            pManager[22].Optional = true;
            pManager.AddNumberParameter("Tool Spindle Speeds", "ToolRPM", "Optional: List of spindle speeds (RPM) for each tool. If empty, uses single Spindle Speed for all tools.", GH_ParamAccess.list);
            pManager[23].Optional = true;
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
            Cutter selectedCutter = null;
            int tool = 31;
            double maxCuttingDepth = 22.5; // Default: 22.5mm for 6mm Fräser
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
            
            // Get Cutter object (optional)
            object cutterObj = null;
            if (DA.GetData(13, ref cutterObj))
            {
                if (cutterObj is GH_Cutter ghCutter && ghCutter.IsValid)
                {
                    selectedCutter = ghCutter.Value;
                }
                else if (cutterObj is Cutter directCutter)
                {
                    selectedCutter = directCutter;
                }
            }
            
            DA.GetData(14, ref tool); // only counts without a cutter
            if (!DA.GetData(15, ref maxCuttingDepth)) maxCuttingDepth = 22.5;
            DA.GetData(16, ref spindleSpeed);
            DA.GetData(17, ref accelDown);
            DA.GetData(18, ref accelUp);
            DA.GetData(19, ref vacuumStrength);
            DA.GetData(20, ref materialWidth);
            DA.GetData(21, ref underlayThickness);
            
            // Get optional tool change inputs
            var toolListData = new List<GH_Integer>();
            DA.GetDataList(22, toolListData);
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
            DA.GetDataList(23, toolRpmData);
            foreach (var rpmVal in toolRpmData)
            {
                if (rpmVal != null && rpmVal.IsValid)
                {
                    toolSpindleSpeeds.Add(rpmVal.Value);
                }
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
            
            else
            {
                // Box, extrusion, SubD and other geometry Grasshopper can turn into a Brep or a mesh
                object value = geo.ScriptVariable();
                if (value is Box box) brep = box.ToBrep();
                else if (value is Extrusion extrusion) brep = extrusion.ToBrep();
                else if (value is SubD subD) brep = subD.ToBrep(SubDToBrepOptions.Default);
                else if (value is Surface otherSurface) brep = otherSurface.ToBrep();
                if (brep == null)
                {
                    Brep converted = null;
                    Mesh convertedMesh = null;
                    if (GH_Convert.ToBrep(geo, ref converted, GH_Conversion.Both) && converted != null) brep = converted;
                    else if (GH_Convert.ToMesh(geo, ref convertedMesh, GH_Conversion.Both) && convertedMesh != null)
                    {
                        mesh = convertedMesh.DuplicateMesh();
                        mesh.UnifyNormals();
                        mesh.Normals.ComputeNormals();
                    }
                }
            }

            // If no Brep or Mesh found, try to convert to a mesh
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
            
            var mill = new ZundMill
            {
                Mesh = mesh, Brep = brep, StepX = dx, StepY = dy, Margin = margin, AngleDegrees = angle,
                StartLeft = startLeft, AddOutline = addOutline, MakePlt = makePLT,
                FeedSpeed = feedSpeed, RapidSpeed = rapidSpeed, MaterialThickness = materialThickness, MaterialWidth = materialWidth,
                ZpOffset = zpOffset, Tool = tool, AccelDown = accelDown, AccelUp = accelUp, VacuumStrength = vacuumStrength, UnderlayThickness = underlayThickness,
                Cutter = selectedCutter, MaxCuttingDepth = maxCuttingDepth, SpindleSpeed = spindleSpeed, SpindleWired = Params.Input[16].SourceCount > 0,
                ToolList = toolList, ToolSpindleSpeeds = toolSpindleSpeeds
            };
            mill.Solve();
            foreach (var m in mill.Messages)
                AddRuntimeMessage(m.Key == 2 ? GH_RuntimeMessageLevel.Error : m.Key == 1 ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, m.Value);

            DA.SetData(0, mill.Path);
            DA.SetDataList(1, mill.SegmentCurves);
            DA.SetData(2, mill.Plt);
            DA.SetData(3, mill.Stats);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCProgramIcon.png", this);
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        public override Guid ComponentGuid => new Guid("C1D2E3F4-A5B6-7890-CDEF-123456789ABC");
    }
}

