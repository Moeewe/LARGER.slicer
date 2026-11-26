using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // CNC tab of Grasshopper 1 (LARGERslicer/Components/CNC), used by the Zünd G3 definition.
    // The cutter database is the shared Types/Cutter.cs; the toolpath and PLT logic of CNC - Program
    // is the shared core (Toolpaths/ZundMill.cs, ReliefMill.cs). Only reading the inputs and writing
    // the outputs is Grasshopper 2.

    // ============================================================== cutter selector
    [IoId("46dc7ba9-5c87-4849-8c69-d7abd8702980")]
    public sealed class CutterSelectorComponent : Component
    {
        public CutterSelectorComponent() : base(Io.HiddenName("CNC - Cutter Selector", "Selects a cutter from the database and outputs cutter information for use with CNC Program component. Supports automatic tool changes for different cutting passes.", Io.Cnc, 0)) { }
        public CutterSelectorComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCCutterSelectorIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Cutter Name", "Name", "Cutter name from database (e.g., 'Fräser 4') or tool position (11, 21, 31). Leave empty to list all available cutters.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Spindle Speed", "RPM", "Optional: Override default spindle speed for this cutter (RPM)", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Cutter", "Cutter", "Cutter object with all specifications for CNC Program component");
            outputs.AddText("Info", "Info", "Cutter information and specifications");
            outputs.AddText("Available Cutters", "Available", "List of all available cutters in database", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string cutterName; double rpm;
            if (!access.GetItem(0, out cutterName)) cutterName = "";
            double? customSpindleSpeed = access.GetItem(1, out rpm) ? rpm : (double?)null;

            List<Cutter> cutters = Cutter.GetDefaultCutters();
            access.SetTwig(2, cutters.Select(c => $"{c.Name} (Tool {c.ToolPosition}, Ø{c.Diameter}mm, Max: {c.MaxCuttingDepth}mm)").ToArray(), null, null);

            Cutter selected;
            if (!string.IsNullOrWhiteSpace(cutterName))
            {
                selected = Cutter.FindCutter(cutters, cutterName);
                if (selected == null)
                {
                    access.AddError("Cutter not found", $"Cutter '{cutterName}' not found in database. Available cutters: {string.Join(", ", cutters.Select(c => c.Name))}");
                    access.SetItem(1, "ERROR: Cutter not found. Check 'Available Cutters' output for list.");
                    return;
                }
            }
            else if (cutters.Count > 0)
            {
                selected = cutters[0];
                access.AddWarning("No cutter specified", $"No cutter specified. Using default: {selected.Name}. Specify a cutter name to select a specific tool.");
            }
            else
            {
                access.AddError("No cutters", "No cutters available in database.");
                access.SetItem(1, "ERROR: No cutters in database.");
                return;
            }

            List<Cutter> sameTool = Cutter.Ambiguous(cutters, cutterName);
            if (sameTool.Count > 1)
                access.AddWarning("More than one cutter", $"Tool position {selected.ToolPosition} holds more than one cutter: {string.Join(", ", sameTool.Select(c => c.Name))}. Using {selected.Name}. Enter the cutter name to choose another one.");
            if (customSpindleSpeed.HasValue && customSpindleSpeed.Value > 0) selected.SpindleSpeed = customSpindleSpeed.Value;

            access.SetItem(0, selected);
            string info = $"Cutter: {selected.Name}\n" +
                          $"Tool Position: {selected.ToolPosition}\n" +
                          $"Diameter: {selected.Diameter} mm\n" +
                          $"Max Cutting Depth (through-cut): {selected.MaxCuttingDepth} mm\n" +
                          $"Max Surface Depth: {selected.MaxSurfaceDepth} mm\n" +
                          $"Description: {selected.Description}";
            if (customSpindleSpeed.HasValue) info += $"\nCustom Spindle Speed: {customSpindleSpeed.Value:F0} RPM (used by CNC - Program unless its own Spindle Speed is wired)";
            access.SetItem(1, info);
        }
    }

    // ============================================================== CNC program (Zünd G3)
    [IoId("930962fb-fe18-42cb-805a-a848d4f5c850")]
    public sealed class CncProgramComponent : Component
    {
        private enum GH_RuntimeMessageLevel { Remark, Warning, Error }
        [ThreadStatic] private static IDataAccess _access;

        /// <summary>The messages of the copied Grasshopper 1 code.</summary>
        private static void AddRuntimeMessage(GH_RuntimeMessageLevel level, string text)
            => Gh1.Message(_access, level == GH_RuntimeMessageLevel.Error ? 2 : level == GH_RuntimeMessageLevel.Warning ? 1 : 0, text);

        public CncProgramComponent() : base(Io.HiddenName("CNC - Program", "Generates boustrophedon (zigzag) toolpath from geometry with optional rotation angle and outline contour for Zünd CNC. Includes tool selection, spindle speed, vacuum zones, acceleration settings, and material thickness handling. Outline cuts around the part at bottom to release it from the plate.", Io.Cnc, 0)) { }
        public CncProgramComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCProgramIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "Geo", "Mesh, Brep, or Surface to generate boustrophedon toolpath from");
            inputs.AddNumber("Step X", "dx", "Step size in X direction (mm)").Set(1.0);
            inputs.AddNumber("Step Y", "dy", "Step size in Y direction (mm)").Set(1.0);
            inputs.AddNumber("Margin", "Margin", "Margin around geometry bounding box (mm)").Set(0.0);
            inputs.AddBoolean("Start Left", "StartLeft", "True to start from left, False to start from right", ("Left", "Start from the left"), ("Right", "Start from the right")).Set(true);
            inputs.AddNumber("Angle", "Angle", "Rotation angle in degrees for toolpath lines (0 = horizontal/vertical, default: 0)").Set(0.0);
            inputs.AddBoolean("Add Outline", "Outline", "True to add outline contour at bottom to cut out the part. Its distance from the part is the radius of the Cutter; without a cutter the larger of Step X and Step Y.").Set(true);
            inputs.AddBoolean("Make PLT", "MakePLT", "True to generate PLT output").Set(true);
            inputs.AddNumber("Feed Speed", "VW", "Feed in cm/s, as the Zünd counts (VS, VW): 7 = 70 mm/s = 4.2 m/min. 0.1 to 100.").Set(10.0);
            inputs.AddNumber("Rapid Speed", "VF", "Speed with the tool up in cm/s (VU): 100 = 1 m/s, the most the machine takes.").Set(50.0);
            inputs.AddNumber("Z Up", "Zup", "Not used: the Zünd retracts to Material Thickness + 1 mm (ZP). Kept so that older definitions open unchanged.").Set(5.0);
            inputs.AddNumber("Material Thickness", "MatThick", "Material thickness in mm. Used for: ZP top position calculation, XX306 extraction height (material thickness + 3.3mm), and validation. Geometry Z coordinates are POSITIVE (material remaining, e.g., +5mm = 5mm remain). CNC coordinates: Z=0 = table surface, material top = -material thickness, CNC_Z = -geometry Z (directly negative). Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments. Positive CNC Z values are NOT allowed (would cut into table).").Set(10.0);
            inputs.AddInteger("ZP Offset", "ZPOffset", "ZP offset for through-cutting (0 = cut to 0, +1 etc = offset for through-cutting)").Set(0);
            inputs.AddGeneric("Cutter", "Cutter", "Cutter object from Cutter Selector component. Contains all cutter specifications for validation and tool selection. If not provided, uses Tool parameter.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Tool", "Tool", "Tool selection: 11 (left), 21 (middle), 31 (right) for SP command. Used if Cutter is not provided.").Set(31);
            inputs.AddNumber("Max Cutting Depth", "MaxDepth", "Maximum possible cutting depth of the cutter in mm (cutter capability, NOT the actual cutting depth). This is the maximum depth the cutter can cut through material, limited by Saugglocke position (3.3mm above material surface). Used for validation only. The actual cutting depth comes from geometry Z coordinates. Example: 22.5mm for 6mm Fräser (max material thickness that can be cut through). Default: 22.5mm.").Set(22.5);
            inputs.AddNumber("Spindle Speed", "RPM", "Spindle speed (RPM) for XX150 command. RM-A: 7000 to 50000.").Set(10000.0);
            inputs.AddInteger("Accel Down", "AccDown", "Acceleration for cutting (1-4, default: 2)").Set(2);
            inputs.AddInteger("Accel Up", "AccUp", "Acceleration for rapid (1-4, default: 4)").Set(4);
            inputs.AddInteger("Vacuum Strength", "VacStr", "Vacuum strength level (0-10, 0 = off, 10 = max)").Set(0);
            inputs.AddNumber("Material Width", "MatWidth", "Material width in mm (Y-dimension). Used for vacuum zone calculation (SV command). If 0 or not provided, uses bounding box Y-dimension. For plates >= 430mm, vacuum zones are 80mm wide sections.", Access.Item, Requirement.MayBeMissing).Set(0.0);
            inputs.AddInteger("Underlay Thickness", "Underlay", "Underlay thickness in increments (100 increments = 1mm, default: 200 = 2mm) for XX308 command").Set(200);
            inputs.AddInteger("Tool List", "Tools", "Optional: List of tools (11, 21, 31) for each path segment. If empty, uses single Tool parameter for all segments.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Tool Spindle Speeds", "ToolRPM", "Optional: List of spindle speeds (RPM) for each tool. If empty, uses single Spindle Speed for all tools.", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Path", "Path", "Complete toolpath as polyline curve");
            outputs.AddCurve("Segments", "Segments", "Individual path segments as curves", Access.Twig);
            outputs.AddText("PLT", "PLT", "Zünd PLT file content");
            outputs.AddText("Stats", "Stats", "Toolpath statistics and information");
        }

        protected override void Process(IDataAccess access)
        {
            _access = access;
            try { Solve(access); }
            finally { _access = null; }
        }

        private void Solve(IDataAccess access)
        {
            object geo;
            double dx = 1.0, dy = 1.0, margin = 0.0, angle = 0.0, feedSpeed = 10.0, rapidSpeed = 50.0, zUp = 5.0, materialThickness = 10.0;
            bool startLeft = true, addOutline = true, makePLT = true;
            int zpOffset = 0, tool = 31, accelDown = 2, accelUp = 4, vacuumStrength = 0, underlayThickness = 200;
            double maxCuttingDepth = 22.5, spindleSpeed = 10000.0, materialWidth = 0.0;
            Cutter selectedCutter = null;
            var toolList = new List<int>();
            var toolSpindleSpeeds = new List<double>();

            if (!access.GetItem(0, out geo) || geo == null) return;
            if (!access.GetItem(1, out dx)) return;
            if (!access.GetItem(2, out dy)) return;
            access.GetItem(3, out margin);
            access.GetItem(4, out startLeft);
            access.GetItem(5, out angle);
            access.GetItem(6, out addOutline);
            access.GetItem(7, out makePLT);
            access.GetItem(8, out feedSpeed);
            access.GetItem(9, out rapidSpeed);
            access.GetItem(10, out zUp);
            access.GetItem(11, out materialThickness);
            access.GetItem(12, out zpOffset);
            selectedCutter = Io.Get<Cutter>(access, 13);
            access.GetItem(14, out tool); // only counts without a cutter
            if (!access.GetItem(15, out maxCuttingDepth)) maxCuttingDepth = 22.5;
            access.GetItem(16, out spindleSpeed);
            access.GetItem(17, out accelDown);
            access.GetItem(18, out accelUp);
            access.GetItem(19, out vacuumStrength);
            if (!access.GetItem(20, out materialWidth)) materialWidth = 0.0;
            access.GetItem(21, out underlayThickness);
            int[] tools; double[] rpms;
            if (access.ItemArray(22, out tools) && tools != null) toolList.AddRange(tools.Where(t => t == 11 || t == 21 || t == 31));
            if (access.ItemArray(23, out rpms) && rpms != null) toolSpindleSpeeds.AddRange(rpms);

            // Geometry: Brep (more precise) or mesh; Grasshopper 2 hands over plain geometry
            Mesh mesh = null;
            Brep brep = null;
            object g = Io.Solid(geo);
            if (g is Brep gb) brep = gb;
            else if (g is Surface gs) brep = gs.ToBrep();
            else if (g is Extrusion ge) brep = ge.ToBrep();
            else if (g is Mesh gm)
            {
                mesh = gm.DuplicateMesh();
                mesh.UnifyNormals();
                mesh.Normals.ComputeNormals();
            }
            else if (g is SubD sd) brep = sd.ToBrep(SubDToBrepOptions.Default);
            if (brep == null && mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry could not be converted to Brep or Mesh.");
                access.SetItem(2, "");
                access.SetItem(3, "ERROR: Geometry could not be converted.");
                return;
            }

            var mill = new ZundMill
            {
                Mesh = mesh, Brep = brep, StepX = dx, StepY = dy, Margin = margin, AngleDegrees = angle,
                StartLeft = startLeft, AddOutline = addOutline, MakePlt = makePLT,
                FeedSpeed = feedSpeed, RapidSpeed = rapidSpeed, MaterialThickness = materialThickness, MaterialWidth = materialWidth,
                ZpOffset = zpOffset, Tool = tool, AccelDown = accelDown, AccelUp = accelUp, VacuumStrength = vacuumStrength, UnderlayThickness = underlayThickness,
                Cutter = selectedCutter, MaxCuttingDepth = maxCuttingDepth, SpindleSpeed = spindleSpeed, SpindleWired = Parameters.Input(16).Inputs.Count > 0,
                ToolList = toolList, ToolSpindleSpeeds = toolSpindleSpeeds
            };
            mill.Solve();
            foreach (var m in mill.Messages)
                AddRuntimeMessage(m.Key == 2 ? GH_RuntimeMessageLevel.Error : m.Key == 1 ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, m.Value);

            if (mill.Path != null) access.SetItem(0, mill.Path);
            access.SetTwig(1, mill.SegmentCurves.ToArray(), null, null);
            access.SetItem(2, mill.Plt);
            access.SetItem(3, mill.Stats);
        }

    }
}
