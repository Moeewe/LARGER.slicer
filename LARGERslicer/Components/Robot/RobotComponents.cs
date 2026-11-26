using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Components.Slicer;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.Robot
{
    /// <summary>Base of the Robot section: the cell (robot, tool, bed) and the program for it.</summary>
    public abstract class RobotComponent : GH_Component, LARGERslicer.Components.IOptionRows
    {
        protected RobotComponent(string name, string nick, string description) : base(name, nick, description, "LARGER", "Robot") { }

        /// <summary>Settings and buttons drawn on the component (see OptionRows).</summary>
        protected virtual void Options(List<LARGERslicer.Components.OptionRow> rows) { }

        private List<LARGERslicer.Components.OptionRow> _rows;
        public List<LARGERslicer.Components.OptionRow> Rows
        {
            get { if (_rows == null) { _rows = new List<LARGERslicer.Components.OptionRow>(); try { Options(_rows); } catch { } } return _rows; }
        }

        /// <summary>Builds the rows anew (when which rows there are depends on what is connected).</summary>
        protected void ResetRows() { _rows = null; Attributes?.ExpireLayout(); }

        protected static LARGERslicer.Components.OptionRow OwnChoice(string label, string[] items, Func<int> get, Action<int> put) => new LARGERslicer.Components.OptionRow
        {
            Kind = LARGERslicer.Components.RowKind.Choice, Label = label, Input = -1, Items = items,
            Get = () => get(), Put = v => put((int)Math.Round(v)),
        };

        // ---- calculating in the background (see AsyncSolve)
        protected virtual bool CanRunInBackground => false;
        private LARGERslicer.Components.AsyncSolve _async;
        protected LARGERslicer.Components.AsyncSolve Async => CanRunInBackground ? (_async ?? (_async = new LARGERslicer.Components.AsyncSolve(this))) : null;
        protected override void BeforeSolveInstance() { Async?.Before(); base.BeforeSolveInstance(); }
        protected override void AfterSolveInstance() { base.AfterSolveInstance(); Async?.After(); }
        /// <summary>While a component upstream still computes in the background, a missing input is waiting, not an error.</summary>
        public override void ComputeData() { base.ComputeData(); LARGERslicer.Components.AsyncSolve.ShowWaiting(this); }
        public override void ExpireSolution(bool recompute) { Async?.Expired(); base.ExpireSolution(recompute); }
        public override void RemovedFromDocument(GH_Document document) { Async?.Cancel(); base.RemovedFromDocument(document); }
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context) { if (context == GH_DocumentContext.Close) Async?.Cancel(); base.DocumentContextChanged(document, context); }
        public override bool Write(GH_IO.Serialization.GH_IWriter writer) { Async?.Write(writer); return base.Write(writer); }
        public override bool Read(GH_IO.Serialization.GH_IReader reader) { Async?.Read(reader); return base.Read(reader); }
        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu) { base.AppendAdditionalComponentMenuItems(menu); Async?.AppendMenu(menu); }

        public override void CreateAttributes()
        {
            if (Rows.Count > 0) m_attributes = new LARGERslicer.Components.OptionAttributes(this);
            else base.CreateAttributes();
        }

        protected static LARGERslicer.Components.OptionRow Toggle(int input, string label, string on = null, string off = null) => new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Toggle, Input = input, Label = label, On = on, Off = off };
        protected static LARGERslicer.Components.OptionRow Button(int input, string label) => new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Button, Input = input, Label = label, Momentary = true };
    }

    // ============================================================== robot
    public class RobotArmComponent : RobotComponent
    {
        public RobotArmComponent()
            : base("Robot", "Robot",
                  "The robot arm. Model picks the KUKA KR120 R2700-2 of the FH Münster cell or a Universal Robots arm (UR3, UR5, UR10 and their e-Series); Robot Program then writes KRL or URScript. For another six-axis arm connect a file of the Robots plug-in library (.xml): link lengths, axis ranges and speeds are read from it, and it is programmed like a KUKA.") { }

        public override Guid ComponentGuid => new Guid("A4F2C1E8-7B35-4D90-9C61-2E8F5A0B7D13");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotArmIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Library", "Lib", "Optional. Path of a robot library file of the Robots plug-in (.xml), or its text. Empty = KUKA KR120 R2700-2", GH_ParamAccess.item);
            p.AddTextParameter("Cell Name", "CN", "Optional. Name of the RobotCell in the file. Empty = the first one", GH_ParamAccess.item);
            p.AddPlaneParameter("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot", GH_ParamAccess.item, Plane.WorldXY);
            p.AddTextParameter("Geometry File", "Geo", "Optional. The Rhino file (.3dm) with the 3D model of the robot, as the Robots plug-in keeps it beside its library file: a layer RobotArm.MANUFACTURER.MODEL with seven meshes, the base and the six links. Empty = looked for beside the Library file and in the library folders (Documents/RobotSystems, Documents/Robots, ~/Robots)", GH_ParamAccess.item);
            p[0].Optional = true; p[1].Optional = true; p[3].Optional = true;
        }

        // which robot: 0 = the KUKA KR120 of the FH Muenster cell (or the Library), then the Universal Robots arms
        private int _model;
        internal static readonly string[] Models = new[] { "KUKA KR120 R2700-2 (FH Muenster)" }.Concat(URModel.All.Select(m => "Universal Robots " + m.Name)).ToArray();
        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(OwnChoice("Robot", Models, () => _model, v => _model = Math.Max(0, Math.Min(Models.Length - 1, v))));
        }
        public override bool Write(GH_IO.Serialization.GH_IWriter writer) { writer.SetInt32("LARGER_Robot", _model); return base.Write(writer); }
        public override bool Read(GH_IO.Serialization.GH_IReader reader) { int m = 0; if (reader.TryGetInt32("LARGER_Robot", ref m)) _model = m; return base.Read(reader); }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Robot", "R", "The robot arm, for Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Link lengths, axis ranges and reach", GH_ParamAccess.list);
            p.AddMeshParameter("Meshes", "M", "The robot as it stands, in its home position (A2 -90, A3 90): base and six links", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string lib = null, cell = null, geo = null; Plane basePlane = Plane.WorldXY;
            da.GetData(0, ref lib); da.GetData(1, ref cell); da.GetData(2, ref basePlane);
            if (Params.Input.Count > 3) da.GetData(3, ref geo);
            if (_model > 0 && !string.IsNullOrWhiteSpace(lib))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A Library file is connected, so the arm comes from it and the chosen Model is not used. Disconnect Library to use the Universal Robots arm.");
            // a Universal Robots arm: its own kinematics and the model Universal Robots publishes
            if (_model > 0 && string.IsNullOrWhiteSpace(lib))
            {
                if (!basePlane.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The base plane is not valid."); return; }
                var um = URModel.All[Math.Min(URModel.All.Length, _model) - 1];
                var ua = new URArm { Model = um, Base = Frame.FromPlane(basePlane) };
                Message = um.Name;
                da.SetData(0, new GH_ObjectWrapper(ua));
                da.SetDataList(1, new[]
                {
                    Inv.F($"Universal Robots {um.Name}: reach about {um.Reach:0} mm, payload {um.Payload:0.#} kg"),
                    "3D model by Universal Robots (ur_description, BSD-3-Clause)",
                    "Robot Program writes URScript for it; Upload to robot sends it to the robot.",
                });
                if (Params.Output.Count > 2) da.SetDataList(2, URMeshes.Arm(um.Name, ua.Base, URModel.Home));
                return;
            }
            RobotArm arm;
            try
            {
                if (string.IsNullOrWhiteSpace(lib)) arm = RobotArm.Kr120R2700();
                else
                {
                    string text = lib.TrimStart().StartsWith("<") ? lib : System.IO.File.ReadAllText(lib.Trim());
                    arm = RobotArm.FromXml(text, cell);
                }
            }
            catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); return; }
            if (!basePlane.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The base plane is not valid."); return; }
            // the file may carry its own base; the plane in the model is applied on top of it
            arm.Base = Frame.FromPlane(basePlane).Times(arm.Base);
            Message = arm.Name;
            if (arm.ConventionWarning != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, arm.ConventionWarning);
            string libFile = !string.IsNullOrWhiteSpace(lib) && !lib.TrimStart().StartsWith("<") ? lib.Trim() : null;
            string found = RobotGeometry.Load(arm, geo, libFile, out string why);
            var info = new List<string> { arm.ToString() };
            if (found != null) info.Add("3D model from " + found);
            else { info.Add("No 3D model: " + why); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, why + " Without it the robot is shown as a line and collisions are not checked."); }
            for (int i = 0; i < 6; i++) info.Add($"A{i + 1}: a {arm.A[i]:0.#}, d {arm.D[i]:0.#}, range {arm.MinDeg[i]:0}..{arm.MaxDeg[i]:0} degrees, {arm.MaxSpeed[i]:0} degrees/s");
            da.SetData(0, new GH_ObjectWrapper(arm));
            da.SetDataList(1, info);
            if (Params.Output.Count > 2 && arm.Links != null)
                da.SetDataList(2, RobotGeometry.Pose(new RobotCell { Arm = arm, Tool = new RobotTool() }, new double[] { 0, -90, 90, 0, 0, 0 }));
        }
    }

    // ============================================================== tool
    public class RobotToolComponent : RobotComponent
    {
        public RobotToolComponent()
            : base("Robot Tool", "Tool",
                  "The tool on the flange: where its tip is and what it weighs. Preset picks a tool of the FH Muenster cells (AE20 for the KUKA, clay extruder, pen and wool spinner for the UR5); Own tool takes the inputs. The TCP plane is given relative to the flange as in the Robots plug-in: origin at the nozzle tip, Z axis from the tip back into the tool. On a UR it is turned so its Z points out of the tool, as the UR pendant shows it.") { }

        public override Guid ComponentGuid => new Guid("C9E1B4A7-2D68-4F53-8A0C-6B3D9E7F1A24");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotToolIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Name", "N", "Name of the tool. AE20_Extruder brings the 3D model of the extruder of the FH Muenster cell", GH_ParamAccess.item, "Extruder");
            p.AddPlaneParameter("TCP", "TCP", "Tool centre point relative to the flange, as in the Robots plug-in: origin = nozzle tip, Z axis = from the tip back into the tool. A TCP from a Robots definition can be used as it is. The AE20 extruder of the FH Muenster: a YZ plane at -460, 3, 108. Default: a straight tool on the flange", GH_ParamAccess.item, new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis));
            p.AddNumberParameter("Weight", "W", "Weight of the tool in kg (0 = not written to the program)", GH_ParamAccess.item, 0.0);
            p.AddPointParameter("Centroid", "C", "Centre of mass relative to the flange", GH_ParamAccess.item, Point3d.Origin);
            p.AddMeshParameter("Mesh", "M", "Optional. The tool as a 3D model, drawn where it sits on the flange: the flange is the world XY plane at the origin, as in the Robots plug-in. Shown on the robot and used by the collision check", GH_ParamAccess.list);
            p[4].Optional = true;
        }

        // 0 = own (the inputs), 1 = AE20 extruder of the Weber DXR25 (KUKA), 2.. = the tools of the FH Muenster UR5
        private int _preset;
        internal static readonly string[] Presets = { "Own tool (the inputs)", "AE20 extruder (Weber DXR25, KUKA)", "Clay extruder V2 (UR5)", "Pen holder (UR5)", "Wool spinner (UR5)" };
        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(OwnChoice("Preset", Presets, () => _preset, v => _preset = Math.Max(0, Math.Min(Presets.Length - 1, v))));
        }
        public override bool Write(GH_IO.Serialization.GH_IWriter writer) { writer.SetInt32("LARGER_ToolPreset", _preset); return base.Write(writer); }
        public override bool Read(GH_IO.Serialization.GH_IReader reader) { int m = 0; if (reader.TryGetInt32("LARGER_ToolPreset", ref m)) _preset = m; return base.Read(reader); }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Tool", "T", "The tool, for Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "TCP as X, Y, Z, A, B, C", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string name = "Extruder"; Plane tcp = new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis); double weight = 0; Point3d centroid = Point3d.Origin;
            da.GetData(0, ref name); da.GetData(1, ref tcp); da.GetData(2, ref weight); da.GetData(3, ref centroid);
            // a preset takes the place of name, TCP and weight
            if (_preset == 1) { name = "AE20_Extruder"; tcp = new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis); weight = 46; }
            if (_preset >= 2)
            {
                var ut = URTool.Preset(_preset - 2, 0);
                var urTool = new RobotTool { Name = ut.Name, Tcp = ut.Tcp, Weight = ut.Weight, Centroid = ut.Centroid, UrPreset = _preset - 2 };
                Message = ut.Name;
                da.SetData(0, new GH_ObjectWrapper(urTool));
                da.SetData(1, Inv.F($"{ut.Name} for a UR: tip at ({ut.Tcp.O.X:0.#}, {ut.Tcp.O.Y:0.#}, {ut.Tcp.O.Z:0.#}) mm from the flange, {ut.Weight:0.##} kg") + (ut.Extrudes ? ", switches the extruder (digital outputs 0 and 1)" : ""));
                return;
            }
            if (!tcp.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The TCP plane is not valid."); return; }
            if (weight < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The weight cannot be negative."); return; }
            var tool = new RobotTool { Name = string.IsNullOrWhiteSpace(name) ? "Extruder" : name.Trim(), Tcp = Frame.FromPlane(tcp), Weight = weight, Centroid = centroid };
            if (Params.Input.Count > 4)
            {
                var meshes = new List<Mesh>();
                if (da.GetDataList(4, meshes) && meshes.Count > 0)
                {
                    var joined = new Mesh(); var notes = new List<string>();
                    foreach (var m in RobotGeometry.Usable(meshes, "Tool mesh", notes)) joined.Append(m);
                    foreach (var l in notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
                    if (joined.Vertices.Count > 0) tool.Mesh = joined;
                }
            }
            bool builtIn = false;
            if (tool.Mesh == null) { tool.Mesh = RobotGeometry.BuiltInTool(tool.Name); builtIn = tool.Mesh != null; }
            var abc = tool.Tcp.Abc();
            Message = tool.Name;
            da.SetData(0, new GH_ObjectWrapper(tool));
            da.SetData(1, $"{tool.Name}: X {tcp.OriginX:0.###}, Y {tcp.OriginY:0.###}, Z {tcp.OriginZ:0.###}, A {abc[0]:0.###}, B {abc[1]:0.###}, C {abc[2]:0.###}, {weight:0.##} kg" + (builtIn ? ", 3D model that comes with the plug-in" : (tool.Mesh != null ? ", with your 3D model" : ", no 3D model")));
        }
    }

    // ============================================================== cell
    public class RobotCellComponent : RobotComponent
    {
        public RobotCellComponent()
            : base("Robot Cell", "Cell",
                  "Robot, tool and the place of the work. The Work Base is the frame the program counts from (KUKA: $BASE), usually a corner of the print bed. Model the cell in Rhino the way it stands: robot, bed, and the part on the bed.") { }

        public override Guid ComponentGuid => new Guid("E7B3D5F1-4A92-4C06-B8D7-1F6A3C9E5B38");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotCellIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Robot", "R", "Robot arm. Empty = KUKA KR120 R2700-2 at the world origin", GH_ParamAccess.item);
            p.AddGenericParameter("Tool", "T", "Robot Tool. Empty = tip on the flange", GH_ParamAccess.item);
            p.AddPlaneParameter("Work Base", "WB", "The frame the program counts from, in the Rhino model", GH_ParamAccess.item, Plane.WorldXY);
            p.AddTextParameter("Base Name", "N", "Name of the work base, for the report", GH_ParamAccess.item, "Bed");
            p.AddMeshParameter("Environment", "Env", "Optional. What stands around the robot (floor, fence, walls, pedestal), as meshes: Robot Program checks the tool, wrist and forearm against them. Cell Geometry gives the cell of the FH Muenster", GH_ParamAccess.list);
            p.AddGenericParameter("External Axes", "Ext", "Optional. A rail the robot stands on and/or a turntable the part stands on, from External Axis. Robot Program then moves them along the print", GH_ParamAccess.list);
            p[0].Optional = true; p[1].Optional = true; p[4].Optional = true; p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Cell", "C", "The robot cell, for Robot Program", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            // a Universal Robots arm: a UR cell (its tool as on the UR pendant, or a UR preset of Robot Tool)
            var ua = URGeo.Unwrap<URArm>(da, 0);
            if (ua != null)
            {
                var rt = SlicerIO.Get<RobotTool>(da, 1);
                URTool ut;
                if (rt == null) ut = URTool.Preset(3, 0);
                else if (rt.UrPreset >= 0) ut = URTool.Preset(rt.UrPreset, 0);
                else { ut = new URTool { Name = rt.Name, Tcp = URTool.FromKukaConvention(rt.Tcp), Weight = rt.Weight, Centroid = rt.Centroid }; AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Own tool on a UR: its TCP (Z from the tip back into the tool, as for KUKA) is turned so Z points out of the tool, as the UR wants it."); }
                var uc = new URCell { Model = ua.Model, Base = ua.Base, Tool = ut, StartCode = rt?.StartCode, OnCode = rt?.OnCode, OffCode = rt?.OffCode };
                var unused = new List<string>();
                for (int k = 2; k < Params.Input.Count; k++) if (Params.Input[k].SourceCount > 0) unused.Add(Params.Input[k].Name);
                if (unused.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not used for a UR: " + string.Join(", ", unused) + ". The UR prints the part where it lies in Rhino, relative to the robot's base, and is not checked for collisions.");
                if (ut.Weight > ua.Model.Payload) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"The tool weighs {ut.Weight:0.#} kg; the {ua.Model.Name} carries {ua.Model.Payload:0.#} kg."));
                da.SetData(0, new GH_ObjectWrapper(uc));
                da.SetData(1, uc + Inv.F($"; the part is printed where it lies in Rhino"));
                return;
            }
            var arm = SlicerIO.Get<RobotArm>(da, 0) ?? RobotArm.Kr120R2700();
            var tool = SlicerIO.Get<RobotTool>(da, 1) ?? new RobotTool();
            Plane wb = Plane.WorldXY; string name = "Bed";
            da.GetData(2, ref wb); da.GetData(3, ref name);
            if (!wb.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The work base plane is not valid."); return; }
            var cell = new RobotCell { Arm = arm, Tool = tool, WorkBase = Frame.FromPlane(wb), BaseName = string.IsNullOrWhiteSpace(name) ? "Bed" : name.Trim() };
            if (Params.Input.Count > 4) { var env = new List<Mesh>(); if (da.GetDataList(4, env)) { var notes = new List<string>(); cell.Environment = RobotGeometry.Usable(env, "Environment mesh", notes); foreach (var l in notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l); } }
            if (Params.Input.Count > 5)
            {
                var goos = new List<IGH_Goo>();
                da.GetDataList(5, goos);
                foreach (var g in goos)
                {
                    var ax = (g as GH_ObjectWrapper)?.Value as ExternalAxis ?? g?.ScriptVariable() as ExternalAxis;
                    if (ax == null) { if (g != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "External Axes takes the output of External Axis only."); continue; }
                    cell.ExternalAxes.Add(ax);
                }
                string why = ExternalAxisCheck(cell.ExternalAxes);
                if (why != null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, why); return; }
            }
            double away = cell.WorkBase.O.DistanceTo(arm.Base.O);
            if (away > arm.Reach) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The work base is {away:0} mm from the robot; its reach is {arm.Reach:0} mm.");
            CollisionModel.Warm(cell); // prepares the bodies for the collision check in the background
            da.SetData(0, new GH_ObjectWrapper(cell));
            da.SetData(1, $"{cell}; work base '{cell.BaseName}' {away:0} mm from the robot");
        }

        /// <summary>At most one rail and one turntable, each with its own name E1 ... E6. Null = fine.</summary>
        internal static string ExternalAxisCheck(List<ExternalAxis> axes)
        {
            if (axes.Count(a => a.IsRail) > 1) return "Only one rail can be used.";
            if (axes.Count(a => !a.IsRail) > 1) return "Only one turntable can be used.";
            var names = axes.Select(a => a.Name.ToUpperInvariant()).ToList();
            if (names.Distinct().Count() != names.Count) return "Two external axes have the same name. Give each its own: E1, E2 ...";
            return null;
        }
    }

    // ============================================================== program
    public class RobotProgramComponent : RobotComponent
    {
        public RobotProgramComponent()
            : base("Robot Program", "Program",
                  "Turns the toolpath into robot moves: the tool tip on every path point, the tool along the path's tool axis (so angled, non-planar and multi-axis paths tilt the tool). Checks reach, axis ranges and the wrist, and writes the moves for the DXR Generator and as a KRL program.") { }

        public override Guid ComponentGuid => new Guid("F1D8A3C6-5E27-4B49-A3F0-8C2B6D4E9A57");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotProgramIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End, where the part stands in the Rhino model", GH_ParamAccess.item);
            p.AddGenericParameter("Cell", "C", "Robot Cell. Empty = KUKA KR120 R2700-2 at the world origin, tip on the flange", GH_ParamAccess.item);
            p.AddNumberParameter("Turn", "Tu", "Turns the tool about its own axis, in degrees: to keep hoses and cables free", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("Name", "N", "Name of the program", GH_ParamAccess.item, "LARGER_PRINT");
            p.AddBooleanParameter("Check", "Chk", "True = calculate the axis angles of every point and check reach and ranges", GH_ParamAccess.item, true);
            p.AddNumberParameter("Pose", "P", "Shows the arm at this place of the path: 0 = start, 1 = end", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("File", "File", "Optional. File or folder for the KRL program. Empty = the Desktop, named like the program", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the KRL file", GH_ParamAccess.item, false);
            p.AddMeshParameter("Environment", "Env", "Optional. What stands around the robot: bed, table, cage, as meshes. The collision check tests the tool, wrist and forearm against them", GH_ParamAccess.list);
            p.AddIntegerParameter("Collision Poses", "Col", "Fast mode of the collision check: how many poses along the path it looks at (evenly spread). Critical poses come on top: both ends of every move without extrusion, every place where the tool turns by 5 degrees, the strongest tilt of every layer. -1 = every move. 0 = no check. Needs the 3D model of the robot", GH_ParamAccess.item, 500);
            p.AddBooleanParameter("Check Collisions", "CC", "True = check the robot, the tool, rail and turntable against each other, the part printed so far, the bed and the Environment", GH_ParamAccess.item, true);
            p.AddNumberParameter("Clearance", "Cl", "Bodies closer than this (mm) are reported as a near miss", GH_ParamAccess.item, 10.0);
            p.AddBooleanParameter("Show Cell", "Cell", "True = draw the cell around the robot (floor, fence, glass, walls) with the robot. False = only the robot and its tool. The collision check always uses the cell", GH_ParamAccess.item, false);
            p[1].Optional = true; p[6].Optional = true; p[8].Optional = true;
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            if (_isUR)
            {
                rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Blend", Input = -1, Unit = "mm", Decimals = 1, Min = 0, Max = 10, Presets = new double[] { 0, 0.5, 1, 2, 5 }, Get = () => _blend, Put = v => _blend = Math.Max(0, v) });
                rows.Add(Button(7, "Save script"));
                rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Button, Label = "Upload to robot", Input = -1, Click = c => UploadUR() });
                return;
            }
            rows.Add(Toggle(4, "Check reach"));
            rows.Add(Toggle(10, "Check collisions"));
            rows.Add(Toggle(12, "Show cell"));
            rows.Add(Button(7, "Save KRL"));
        }

        // ---- a UR: URScript instead of KRL and DXR
        private bool _isUR;
        private double _blend = 1.0;
        private string _ip = "192.168.1.66";
        private URProgram _lastUR;

        public override bool Write(GH_IO.Serialization.GH_IWriter writer) { writer.SetDouble("LARGER_URBlend", _blend); writer.SetString("LARGER_URIP", _ip ?? ""); return base.Write(writer); }
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            double b = 1; if (reader.TryGetDouble("LARGER_URBlend", ref b)) _blend = b;
            string ip = null; if (reader.TryGetString("LARGER_URIP", ref ip) && !string.IsNullOrWhiteSpace(ip)) _ip = ip;
            return base.Read(reader);
        }
        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            menu.Items.Add(new System.Windows.Forms.ToolStripLabel("UR robot address (for Upload to robot):") { ForeColor = System.Drawing.Color.DimGray });
            Menu_AppendTextItem(menu, _ip, (s2, e2) => { if (e2.KeyCode == System.Windows.Forms.Keys.Enter && s2 is Grasshopper.GUI.GH_MenuTextBox box && !string.IsNullOrWhiteSpace(box.Text)) { RecordUndoEvent("Robot IP"); _ip = box.Text.Trim(); } }, null, true);
        }

        private void UploadUR()
        {
            if (_lastUR == null || _lastUR.Script.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Nothing to send yet."); return; }
            if (!_lastUR.Ok) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not sent: the program is not OK (see Report). Fix it first; a program that cannot run should never reach the robot."); Grasshopper.Instances.RedrawCanvas(); return; }
            try { var msg = URProgram.Upload(_lastUR.Script, _ip); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, msg + " The robot runs it at once (an e-Series only in remote control); the start code first asks for the compressed air."); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Upload failed: " + ex.Message); }
            Grasshopper.Instances.RedrawCanvas();
        }

        private void SolveUR(IGH_DataAccess da, URCell cell)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            double turn = 0, pose = 0; string name = "LARGER_PRINT", file = null; bool save = false;
            da.GetData(2, ref turn); da.GetData(3, ref name); da.GetData(5, ref pose); da.GetData(6, ref file); da.GetData(7, ref save);
            cell.Ip = _ip;
            var prg = URProgram.Build(tp, cell, 0, _blend, turn, 30,
                cell.StartCode ?? LARGERslicer.Components.Robot.URProgramComponent.AirCode,
                cell.OnCode ?? LARGERslicer.Components.Robot.URProgramComponent.OnCode,
                cell.OffCode ?? LARGERslicer.Components.Robot.URProgramComponent.OffCode, name);
            _lastUR = prg;
            string fileName = OutputFile.Name("UR", name, tp, "");
            if (save)
            {
                try { var w = OutputFile.Write(file, fileName + ".script", prg.Script); prg.Report.Add("Saved " + w); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + w); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            if (!prg.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join(" ", prg.Report.Where(l => l.StartsWith("NOT") || l.StartsWith("TOO"))));
            bool wired(int i) => i < Params.Input.Count && Params.Input[i].SourceCount > 0;
            if (wired(8) || wired(10)) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "On a UR no collision check runs: Environment and Check Collisions are not used.");
            Message = Inv.F($"{cell.Model.Name}: {prg.Targets.Count} moves, {prg.Minutes:0} min");
            da.SetDataList(3, prg.Script);
            da.SetData(4, prg.Ok);
            da.SetDataList(5, prg.Report);
            da.SetDataList(6, prg.Joints.Select(q => string.Join(" / ", q.Select(v => (v * 180 / Math.PI).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))));
            da.SetDataList(7, prg.Unreachable);
            if (prg.Joints.Count > 0)
            {
                int k = (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (prg.Joints.Count - 1));
                var arm = URGeo.Arm(cell, prg.Joints[k]);
                if (Params.Output.Count > 9) da.SetDataList(9, arm);
                _robot.AddRange(arm);
            }
            if (Params.Output.Count > 11) da.SetData(11, fileName);
            if (Params.Output.Count > 12) da.SetData(12, new GH_ObjectWrapper(prg));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Robot Path", "Path", "One PTP line per path point with X, Y, Z, A, B, C: connect to Robot Path of the DXR Generator", GH_ParamAccess.list);
            p.AddNumberParameter("Extrusion", "E", "Extrusion per line, plus one closing value (the DXR Generator drops the last value)", GH_ParamAccess.list);
            p.AddNumberParameter("Feed", "F", "Feed F for the machine, in mm/min (mm/s x 60), per line, plus one closing value", GH_ParamAccess.list);
            p.AddTextParameter("Code", "Code", "The program: KRL for a KUKA (.src: tool, base and linear moves), URScript for a Universal Robots arm (.script)", GH_ParamAccess.list);
            p.AddBooleanParameter("OK", "OK", "True if nothing that was checked speaks against the program: the robot reaches every point within its axis ranges, and nothing collides. What was not checked (Check off, no 3D model of the robot) is written in Report", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Reach, axis ranges, wrist and axis speed", GH_ParamAccess.list);
            p.AddTextParameter("Axes", "A", "Axis angles A1 to A6 of every point", GH_ParamAccess.list);
            p.AddPointParameter("Unreachable", "U", "Points the robot cannot get to", GH_ParamAccess.list);
            p.AddCurveParameter("Arm", "Arm", "The arm at the chosen Pose, as a line through its joints", GH_ParamAccess.item);
            p.AddMeshParameter("Robot", "Rob", "The robot with its tool at the chosen Pose, as a 3D model: move the Pose slider to watch it run the path", GH_ParamAccess.list);
            p.AddPointParameter("Collisions", "Hit", "Path points at which the robot collides with itself or the Environment", GH_ParamAccess.list);
            p.HideParameter(9); // the component draws the robot itself, in its colours
            p.AddTextParameter("File Name", "Fn", "A name for the DXR file, made of DXR, date and time, the Name, weight and print time (DXR_2611051216_Test Object_2668g_53min): connect to File of the DXR Generator", GH_ParamAccess.item);
            p.AddGenericParameter("Program", "Prg", "The finished program with its cell: connect to Robot Simulation to play it", GH_ParamAccess.item);
            p.AddIntegerParameter("Collision Moves", "CM", "Numbers of the moves at which something collides (as Move of Robot Simulation)", GH_ParamAccess.list);
            p.AddMeshParameter("Collision Bodies", "CB", "The bodies that collide, where they are at their worst collision, and a ball where they touch", GH_ParamAccess.list);
        }

        // The robot at the chosen pose, drawn in its own colours: base dark, arm KUKA orange, tool steel blue.
        internal static readonly System.Drawing.Color BaseColor = System.Drawing.Color.FromArgb(60, 60, 64), ArmColor = System.Drawing.Color.FromArgb(240, 110, 20),
            ToolColor = System.Drawing.Color.FromArgb(90, 120, 150), AxisColor = System.Drawing.Color.FromArgb(150, 152, 158);
        internal static readonly Rhino.Display.DisplayMaterial HitMaterial = MachineLook.Material(MachineLook.Hit);

        private readonly List<Mesh> _robot = new List<Mesh>();
        private readonly List<Mesh> _axes = new List<Mesh>();
        private readonly List<Mesh> _env = new List<Mesh>();
        // what collides at the chosen pose: robot bodies (0 base, 1-6 links, 7 tool), axis models, other meshes
        private readonly HashSet<int> _hitRobot = new HashSet<int>(), _hitAxes = new HashSet<int>();
        private readonly List<Mesh> _hitOther = new List<Mesh>();
        private readonly LARGERslicer.Components.MachinePreview _preview = new LARGERslicer.Components.MachinePreview();

        protected override void BeforeSolveInstance() { _robot.Clear(); _axes.Clear(); _env.Clear(); _hitRobot.Clear(); _hitAxes.Clear(); _hitOther.Clear(); _preview.Clear(); base.BeforeSolveInstance(); }

        /// <summary>The robot, its axes, the cell (when shown) and what collides, in the order they are drawn: one helper for all machine previews.</summary>
        internal static void Fill(LARGERslicer.Components.MachinePreview preview, List<Mesh> robot, List<Mesh> axes, List<Mesh> env, HashSet<int> hitRobot, HashSet<int> hitAxes, List<Mesh> hitOther)
        {
            for (int i = 0; i < robot.Count; i++)
                if (hitRobot.Contains(i)) preview.Hit(robot[i]); else preview.Add(robot[i], i == 0 ? BaseColor : i >= 7 ? ToolColor : ArmColor);
            for (int i = 0; i < axes.Count; i++)
                if (hitAxes.Contains(i)) preview.Hit(axes[i]); else preview.Add(axes[i], AxisColor);
            foreach (var m in env) if (!hitOther.Contains(m)) preview.Add(m, MachineLook.Cell, 0.55);
            foreach (var m in hitOther) preview.Hit(m);
        }

        protected override void AfterSolveInstance() { Fill(_preview, _robot, _axes, _env, _hitRobot, _hitAxes, _hitOther); base.AfterSolveInstance(); }

        public override BoundingBox ClippingBox => _preview.Box(base.ClippingBox);

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            _preview.Draw(args);
        }

        /// <summary>Marks what collides at this pose, to draw it red.</summary>
        internal static void MarkHits(CollisionModel model, double[] axes, double[] ext, int move, HashSet<int> robot, HashSet<int> axesHit, List<Mesh> other)
        {
            if (model == null || axes == null) return;
            PoseCollision pc;
            try { pc = model.Check(axes, ext, move); } catch { return; }
            if (!pc.Any) return;
            foreach (int i in pc.Robot) robot.Add(i);
            foreach (int a in pc.Axes) axesHit.Add(a);
            if (model.Cell.Environment != null) foreach (int e in pc.Environment) if (e < model.Cell.Environment.Count) other.Add(model.Cell.Environment[e]);
            foreach (var w in pc.Where) { var ball = Mesh.CreateFromSphere(new Sphere(w, 12), 12, 8); if (ball != null) other.Add(ball); }
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            // a Universal Robots cell: URScript, worked out here at once
            var urCell = URGeo.Unwrap<URCell>(da, 1);
            if ((urCell != null) != _isUR) { _isUR = urCell != null; ResetRows(); }
            if (urCell != null) { SolveUR(da, urCell); return; }
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            var cell = SlicerIO.Get<RobotCell>(da, 1) ?? new RobotCell();
            double turn = 0, pose = 0; string name = "LARGER_PRINT", file = null; bool check = true, save = false;
            da.GetData(2, ref turn); da.GetData(3, ref name); da.GetData(4, ref check); da.GetData(5, ref pose); da.GetData(6, ref file); da.GetData(7, ref save);
            var env = new List<Mesh>(); int poses = 500; bool collide = true; double clearance = 10;
            bool collisionIO = Params.Input.Count > 9 && Params.Output.Count > 10;
            if (collisionIO)
            {
                da.GetDataList(8, env); da.GetData(9, ref poses);
                if (Params.Input.Count > 10) da.GetData(10, ref collide);
                if (Params.Input.Count > 11) da.GetData(11, ref clearance);
            }
            var envNotes = new List<string>();
            env = RobotGeometry.Usable(env, "Environment mesh", envNotes).Select(m => m.DuplicateMesh()).ToList(); // copies for the background
            int outputs = Params.Output.Count;
            // with a Robot Simulation connected, that one shows the robot
            bool simulated = outputs > 12 && Params.Output[12].Recipients.Count > 0;
            // the cell around the robot is drawn only with Show Cell (the collision check always uses it)
            bool showCell = false;
            if (Params.Input.Count > 12) da.GetData(12, ref showCell);
            var shownCell = showCell && !simulated && cell.Environment != null ? cell.Environment.Where(m => m != null).ToList() : null;

            Async.Run(da, job =>
            {
                // the preview lists are filled on the UI thread, when the result is handed out
                if (shownCell != null) job.OnApply(() => _env.AddRange(shownCell));
                var r = RobotProgram.Build(tp, cell, turn, name, check);
                if (!r.Ok) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.FirstOrDefault(l => l.StartsWith("NOT")) ?? "The robot cannot run this path.");
                foreach (var l in r.Report.Where(l => l.StartsWith("WARNING: "))) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l.Substring(9));
                foreach (var l in envNotes) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
                var path = r.PathFor(tp); // one point per move
                if (r.Speed.Any(v => v <= 0)) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A move has speed 0; the machine would stop there.");
                if (save)
                {
                    try
                    {
                        string def = RobotProgram.ProgramName(name) + ".src";
                        var lines = RobotProgram.KrlForFile(r.Krl, OutputFile.Resolve(file, def), out string note, out bool bad);
                        job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(file, def, lines));
                        if (note != null) job.AddRuntimeMessage(bad ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, note);
                    }
                    catch (Exception ex) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
                }
                job.Message = r.Ok ? r.Moves.Count + " moves" : "not reachable";

                var e = new List<double>(r.Extrusion) { 0 };
                var f = new List<double>(r.Speed) { r.Speed.Count > 0 ? r.Speed[r.Speed.Count - 1] : tp.Settings.Speed };
                if (outputs > 11) job.SetData(11, OutputFile.Name("DXR", name, tp, ""));
                if (outputs > 12) job.SetData(12, new GH_ObjectWrapper(RobotRun.Make(r, cell, tp)));
                job.SetDataList(0, r.Lines);
                job.SetDataList(1, e);
                job.SetDataList(2, f);
                job.SetDataList(3, r.Krl);
                if (check)
                {
                    job.SetDataList(6, r.Moves.Select(m => m.Axes == null ? "" : string.Join(", ", m.Axes.Select((v, i) => "A" + (i + 1) + " " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
                        + (m.External == null ? "" : string.Concat(m.External.Select((v, i) => ", " + cell.ExternalAxes[i].Name + " " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))))));
                    job.SetDataList(7, Enumerable.Range(0, r.Moves.Count).Where(i => !r.Moves[i].Reachable || !r.Moves[i].InLimits).Select(i => r.Moves[i].Part.Pt(path.Points[i])));
                    int at = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
                    if (r.Moves[at].Axes != null)
                    {
                        var pts = cell.Arm.Skeleton(r.Moves[at].Axes, r.Moves[at].RobotBase);
                        pts.Add(r.Moves[at].Part.Pt(path.Points[at])); // flange to tool tip
                        job.SetData(8, new PolylineCurve(pts));
                        if (outputs > 9)
                        {
                            var model = RobotGeometry.Pose(cell, r.Moves[at].Axes, r.Moves[at].External);
                            var axesModel = ExternalAxisGeometry.Pose(cell, r.Moves[at].External);
                            if (model != null) { job.SetDataList(9, model.Concat(axesModel)); if (!simulated) job.OnApply(() => _robot.AddRange(model)); }
                            else if (axesModel.Count > 0) job.SetDataList(9, axesModel);
                            if (!simulated) job.OnApply(() => _axes.AddRange(axesModel));
                        }
                    }
                    if (collisionIO)
                    {
                        if (collide && poses != 0)
                        {
                            // the meshes given here are checked as well as those of the cell
                            var checkCell = cell;
                            if (env.Count > 0)
                            {
                                checkCell = new RobotCell { Arm = cell.Arm, Tool = cell.Tool, WorkBase = cell.WorkBase, BaseName = cell.BaseName, ExternalAxes = cell.ExternalAxes, Environment = new List<Mesh>() };
                                if (cell.Environment != null) checkCell.Environment.AddRange(cell.Environment);
                                checkCell.Environment.AddRange(env);
                            }
                            var col = r.CheckCollisions(tp, checkCell, new CollisionOptions { Poses = poses, Clearance = Math.Max(0, clearance) });
                            if (col.Hits > 0)
                            {
                                job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", col.Lines.Take(4)));
                                job.SetDataList(10, col.Moves.Select(i => r.Moves[i].Part.Pt(path.Points[i])));
                                job.Message = "collision";
                            }
                            else if (col.Checked) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, col.Summary);
                            else job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, col.Summary);
                            if (outputs > 14) { job.SetDataList(13, col.Moves); job.SetDataList(14, col.Bodies); }
                            int atPose = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
                            if (!simulated && r.Moves[atPose].Axes != null) job.OnApply(() => MarkHits(col.Model, r.Moves[atPose].Axes, r.Moves[atPose].External, atPose, _hitRobot, _hitAxes, _hitOther));
                        }
                        else r.Report.Add(collide ? "Collisions were not checked (Collision Poses is 0)." : "Collisions were not checked (Check Collisions is off).");
                    }
                }
                else
                {
                    // OK then only means that nothing was found, as nothing was looked at: say so
                    if (collisionIO && collide && poses != 0)
                    {
                        const string why = "Collisions were not checked: Check reach is off, and the check needs the axis angles.";
                        r.Report.Add(why); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, why);
                    }
                    job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Check reach is off: OK says nothing about reach and axis ranges.");
                }
                job.SetData(4, r.Ok);
                job.SetDataList(5, r.Report);
            });
        }
    }

    // ============================================================== simulation
    public class RobotSimulationComponent : RobotComponent
    {
        public RobotSimulationComponent()
            : base("Robot Simulation", "RobotSim",
                  "Plays the robot program: the robot with its tool at any moment of the print, as in Program Simulation of the Robots plug-in. Time runs from 0 (start) to 1 (end) and follows the real time of the print, so half way on the slider is half way through the print. Move the slider, or set Play and it runs by itself (Film Length = how many seconds one run takes on the screen). Outputs the axis angles and the place of the tool tip at that moment; with tilted layers (angled, non-planar, multi-axis) you see the tool lean. Moving the slider only places the robot: the program is not calculated again.") { }

        public override Guid ComponentGuid => new Guid("5D2E8B14-9A63-4F70-B1C8-3A6F0D9E2475");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotSimulationIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Program", "Prg", "Program of Robot Program", GH_ParamAccess.item);
            p.AddNumberParameter("Time", "T", "Where in the print: 0 = start, 1 = end", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Play", "Play", "True = the simulation runs by itself, from where Time stands, and starts again at the end. False = it stands where Time says", GH_ParamAccess.item, false);
            p.AddNumberParameter("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds. A print of an hour in 30 seconds runs 120 times faster than in the lab", GH_ParamAccess.item, 30.0);
            p.AddBooleanParameter("Show Cell", "Cell", "True = draw the cell around the robot (floor, fence, glass, walls) with the simulation. False = only the robot and its tool (and what collides, in red). The collision check always uses the cell", GH_ParamAccess.item, false);
        }

        // Play: the clock shared with Printer Simulation keeps the time and asks Grasshopper for the next picture.
        private readonly LARGERslicer.Components.PlayClock _clock = new LARGERslicer.Components.PlayClock();

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(Toggle(2, "Play", "\u25A0  Stop", "\u25B6  Play"));
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Film Length (s)", Input = 3, Presets = LARGERslicer.Components.PlayClock.Lengths, Decimals = 0 });
            rows.Add(Toggle(4, "Show cell"));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Robot", "Rob", "The robot with its tool at that moment: base, six links, tool", GH_ParamAccess.list);
            p.AddNumberParameter("Axes", "A", "Axis angles A1 to A6 in degrees", GH_ParamAccess.list);
            p.AddPlaneParameter("TCP", "TCP", "The tool tip: origin on the path, Z along the tool axis", GH_ParamAccess.item);
            p.AddIntegerParameter("Move", "M", "Number of the move the robot is on (the line of the program)", GH_ParamAccess.item);
            p.AddNumberParameter("Seconds", "s", "Time since the start of the print", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Time, move, axis angles and whether this pose is within the axis ranges", GH_ParamAccess.item);
            p.AddNumberParameter("External", "E", "Values of the external axes at that moment (rail in mm, turntable in degrees), in the order of the cell", GH_ParamAccess.list);
            p.AddCurveParameter("Printed", "Pr", "The path printed up to that moment, where it is in the room: it turns with a turntable", GH_ParamAccess.item);
            p.AddMeshParameter("Axes Model", "AM", "The rail with its carriage and the turntable at that moment", GH_ParamAccess.list);
            p.AddMeshParameter("Environment", "Env", "What stands around the robot (the Environment of the cell); drawn with the simulation when Show Cell is on", GH_ParamAccess.list);
            p.AddMeshParameter("Colliding", "Col", "What collides at this moment (drawn red): robot links, tool, rail, table, environment, and a ball where it touches. Empty = clear", GH_ParamAccess.list);
            p.HideParameter(0); // drawn by the component, in colour
            p.HideParameter(7); // the printed path is drawn by the component, thick and by role
            p.HideParameter(8); p.HideParameter(9); p.HideParameter(10);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // files saved before 2.0 show Printed as a thin line on top of the component's own drawing
            if (Params.Output.Count > 7 && Params.Output[7] is IGH_PreviewObject pr) pr.Hidden = true;
        }

        // ---- the print as it grows: printed part thick and coloured by role, the rest faint
        private RobotRun _drawRun;
        private List<KeyValuePair<PathRole, int[]>> _segs; // role, first and last point index
        private List<Point3d[]> _segPts;
        private int _reached;
        private Point3d _tip = Point3d.Unset;
        private Transform _partX = Transform.Identity;
        private BoundingBox _pathBox = BoundingBox.Empty;
        private static readonly System.Drawing.Color Ahead = System.Drawing.Color.FromArgb(205, 208, 212);

        private void PrepareDrawing(RobotRun run)
        {
            if (ReferenceEquals(run, _drawRun)) return;
            _drawRun = run; _segs = new List<KeyValuePair<PathRole, int[]>>(); _segPts = new List<Point3d[]>();
            int n = run.Points.Count;
            _pathBox = n > 0 ? new BoundingBox(run.Points) : BoundingBox.Empty;
            if (n < 2) return;
            bool roles = run.Roles.Count == n;
            int start = 1;
            for (int i = 2; i <= n; i++)
            {
                if (i < n && (!roles || run.Roles[i] == run.Roles[start])) continue;
                var pts = new Point3d[i - start + 1];
                for (int k = 0; k < pts.Length; k++) pts[k] = run.Points[start - 1 + k];
                _segs.Add(new KeyValuePair<PathRole, int[]>(roles ? run.Roles[start] : PathRole.Wall, new[] { start - 1, i - 1 }));
                _segPts.Add(pts);
                start = i;
            }
        }

        private static Transform ToTransform(Frame f)
        {
            var x = Transform.Identity;
            x.M00 = f.X.X; x.M10 = f.X.Y; x.M20 = f.X.Z;
            x.M01 = f.Y.X; x.M11 = f.Y.Y; x.M21 = f.Y.Z;
            x.M02 = f.Z.X; x.M12 = f.Z.Y; x.M22 = f.Z.Z;
            x.M03 = f.O.X; x.M13 = f.O.Y; x.M23 = f.O.Z;
            return x;
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!Hidden && !Locked && _urPrinted != null) args.Display.DrawPolyline(_urPrinted, GH_Toolpath.RoleColor(PathRole.Wall), Math.Max(3, args.DefaultCurveThickness + 2));
            if (Hidden || Locked || _drawRun == null || _segs == null || _segs.Count == 0) return;
            bool moved = !_partX.Equals(Transform.Identity);
            if (moved) args.Display.PushModelTransform(_partX);
            int thick = Math.Max(3, args.DefaultCurveThickness + 2);
            for (int s = 0; s < _segs.Count; s++)
            {
                var role = _segs[s].Key; int a = _segs[s].Value[0], b = _segs[s].Value[1];
                var pts = _segPts[s];
                var col = GH_Toolpath.RoleColor(role);
                int w = role == PathRole.Travel ? 1 : thick;
                if (b < _reached) { args.Display.DrawPolyline(pts, col, w); continue; }       // printed
                if (a >= _reached) { args.Display.DrawPolyline(pts, Ahead, 1); continue; }    // still to come
                // the run the robot is on: printed up to the tool tip, the rest ahead
                int m = _reached - a; // points a.._reached-1 are printed (m >= 1)
                var done = new Point3d[m + 1];
                Array.Copy(pts, 0, done, 0, m); done[m] = _tip.IsValid ? _tip : pts[m - 1];
                args.Display.DrawPolyline(done, col, w);
                var rest = new Point3d[pts.Length - m + 1];
                rest[0] = done[m]; Array.Copy(pts, m, rest, 1, pts.Length - m);
                if (rest.Length > 1) args.Display.DrawPolyline(rest, Ahead, 1);
            }
            if (moved) args.Display.PopModelTransform();
        }

        private readonly List<Mesh> _robot = new List<Mesh>();
        private readonly List<Mesh> _axes = new List<Mesh>();
        private readonly List<Mesh> _env = new List<Mesh>();
        private readonly HashSet<int> _hitRobot = new HashSet<int>(), _hitAxes = new HashSet<int>();
        private readonly List<Mesh> _hitOther = new List<Mesh>();
        private readonly LARGERslicer.Components.MachinePreview _preview = new LARGERslicer.Components.MachinePreview();

        protected override void BeforeSolveInstance() { _robot.Clear(); _axes.Clear(); _env.Clear(); _hitRobot.Clear(); _hitAxes.Clear(); _hitOther.Clear(); _preview.Clear(); base.BeforeSolveInstance(); }
        protected override void AfterSolveInstance() { RobotProgramComponent.Fill(_preview, _robot, _axes, _env, _hitRobot, _hitAxes, _hitOther); base.AfterSolveInstance(); }

        public override BoundingBox ClippingBox
        {
            get
            {
                var b = _preview.Box(base.ClippingBox);
                if (_drawRun != null && _pathBox.IsValid) { var pb = _pathBox; pb.Transform(_partX); b.Union(pb); }
                foreach (var m in _urArm) b.Union(m.GetBoundingBox(false));
                if (_urPrinted != null) b.Union(_urPrinted.BoundingBox);
                return b;
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            _preview.Draw(args);
            if (!Hidden && !Locked) foreach (var m in _urArm) args.Display.DrawMeshShaded(m, URMaterial);
        }

        // a UR program: the arm by its model, the printed path as a plain line
        private readonly List<Mesh> _urArm = new List<Mesh>();
        private Polyline _urPrinted;
        private static readonly Rhino.Display.DisplayMaterial URMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(170, 180, 190));

        private void SolveUR(IGH_DataAccess da, URProgram prg)
        {
            _drawRun = null;
            if (prg.Joints.Count == 0) return;
            double time = 0, film = 30; bool play = false;
            da.GetData(1, ref time);
            if (Params.Input.Count > 3) { da.GetData(2, ref play); da.GetData(3, ref film); }
            time = _clock.Position(this, play, Math.Max(0, Math.Min(1, time)), film);
            double total = prg.Seconds[prg.Seconds.Count - 1], at = time * total;
            int k = prg.Seconds.BinarySearch(at); if (k < 0) k = Math.Max(0, ~k - 1);
            k = Math.Min(k, prg.Joints.Count - 1);
            var q = prg.Joints[k];
            if (k + 1 < prg.Joints.Count && prg.Seconds[k + 1] > prg.Seconds[k])
            {
                double u = (at - prg.Seconds[k]) / (prg.Seconds[k + 1] - prg.Seconds[k]);
                q = q.Select((v, j) => v + (prg.Joints[k + 1][j] - v) * u).ToArray();
            }
            var cell = prg.Cell;
            _urArm.AddRange(URGeo.Arm(cell, q));
            var printed = new Polyline();
            for (int i = 0; i <= k && i < prg.PathPoints.Count; i++) if (prg.Printing[i] || (i + 1 < prg.Printing.Count && prg.Printing[i + 1])) printed.Add(prg.PathPoints[i]);
            _urPrinted = printed.Count > 1 ? printed : null;
            var tcp = cell.Base.Times(cell.Model.Flange(q)).Times(cell.Tool.Tcp).ToPlane();
            da.SetDataList(0, _urArm);
            da.SetDataList(1, q.Select(v => Math.Round(v * 180 / Math.PI, 2)));
            da.SetData(2, tcp);
            da.SetData(3, k + 1);
            da.SetData(4, at);
            da.SetData(5, Inv.F($"{cell.Model.Name}: {at / 60:0.0} of {total / 60:0.0} min, move {k + 1} of {prg.Joints.Count}, axes ") + string.Join(" / ", q.Select(v => (v * 180 / Math.PI).ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
            if (_urPrinted != null && Params.Output.Count > 7) da.SetData(7, new PolylineCurve(_urPrinted));
            Message = Inv.F($"{100 * time:0} %");
            if (_clock.Playing) Message = "> " + Message;
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            _urArm.Clear(); _urPrinted = null;
            var urp = URGeo.Unwrap<URProgram>(da, 0);
            if (urp != null) { SolveUR(da, urp); return; }
            var run = SlicerIO.Get<RobotRun>(da, 0);
            if (run == null || run.Points.Count == 0) _drawRun = null;
            if (run == null || run.Points.Count == 0)
            {
                // Robot Program calculates in the background: its Program comes when it is done
                if (Params.Input[0].SourceCount > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Waiting for Robot Program (it calculates in the background).");
                else AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect Program of Robot Program.");
                return;
            }
            double time = 0; da.GetData(1, ref time);
            time = Math.Max(0, Math.Min(1, time));
            bool play = false, showCell = false; double film = 30;
            if (Params.Input.Count > 3) { da.GetData(2, ref play); da.GetData(3, ref film); }
            if (Params.Input.Count > 4) da.GetData(4, ref showCell);
            time = _clock.Position(this, play, time, film);
            double seconds = time * run.Duration;
            PrepareDrawing(run);
            _reached = run.ReachedAt(seconds, out _tip);
            _partX = ToTransform(run.Cell.PartAt(run.ExternalAt(seconds)));
            var axes = run.AxesAt(seconds, out int move, out double share);
            if (axes == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The robot cannot get to move {move}: nothing to show here. Read the Report of Robot Program."); da.SetData(3, move); da.SetData(4, seconds); return; }
            var arm = run.Cell.Arm;
            var ext = run.ExternalAt(seconds);
            var model = RobotGeometry.Pose(run.Cell, axes, ext);
            if (model != null) { _robot.AddRange(model); da.SetDataList(0, model); }
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The robot has no 3D model: see the Info of Robot.");
            if (Params.Output.Count > 8)
            {
                var axesModel = ExternalAxisGeometry.Pose(run.Cell, ext);
                _axes.AddRange(axesModel);
                da.SetDataList(6, ext ?? new double[0]);
                var printed = run.PrintedAt(seconds); if (printed.Count > 1) da.SetData(7, new PolylineCurve(printed));
                da.SetDataList(8, axesModel);
            }
            if (Params.Output.Count > 9 && run.Cell.Environment != null) { if (showCell) _env.AddRange(run.Cell.Environment.Where(m => m != null)); da.SetDataList(9, run.Cell.Environment); }
            // what collides at this moment: drawn red
            string collision = "";
            if (run.Collider != null)
            {
                RobotProgramComponent.MarkHits(run.Collider, axes, ext, move, _hitRobot, _hitAxes, _hitOther);
                if (_hitRobot.Count > 0 || _hitAxes.Count > 0 || _hitOther.Count > 0)
                {
                    var colliding = new List<Mesh>();
                    foreach (int i in _hitRobot) if (model != null && i < model.Count) colliding.Add(model[i]);
                    foreach (int i in _hitAxes) if (i < _axes.Count) colliding.Add(_axes[i]);
                    colliding.AddRange(_hitOther);
                    if (Params.Output.Count > 10) da.SetDataList(10, colliding);
                    collision = " - COLLISION: " + string.Join(", ", _hitRobot.OrderBy(i => i).Select(CollisionModel.RobotName));
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Collision at this moment (drawn red)" + (_hitRobot.Count > 0 ? ": " + string.Join(", ", _hitRobot.OrderBy(i => i).Select(CollisionModel.RobotName)) : "") + ".");
                }
            }
            // the tool tip from the axis angles, the way the controller sees it
            Frame flange = run.Cell.RobotBaseAt(ext).Times(arm.Forward(axes)).Times(Frame.World.TurnedAboutZ(Math.PI));
            Frame tip = flange.Times(run.Cell.Tool.Tcp);
            bool inRange = true;
            for (int k = 0; k < 6; k++) if (axes[k] < arm.MinDeg[k] - 1e-6 || axes[k] > arm.MaxDeg[k] + 1e-6) inRange = false;
            if (!inRange) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "At this moment an axis is outside its range.");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Message = TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", ci);
            da.SetDataList(1, axes);
            da.SetData(2, tip.ToPlane());
            da.SetData(3, move);
            da.SetData(4, seconds);
            if (_clock.Playing) Message = "> " + Message;
            string phase = run.PhaseAt(move);
            da.SetData(5, $"{Message} of {TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci)}, move {move} of {run.Points.Count - 1}" + (phase.Length > 0 ? " (" + phase + ")" : "") + ", " + string.Join(", ", axes.Select((v, k) => "A" + (k + 1) + " " + v.ToString("0.0", ci)))
                + (ext == null ? "" : string.Concat(ext.Select((v, k) => ", " + run.Cell.ExternalAxes[k].Name + " " + v.ToString("0.0", ci)))) + (inRange ? "" : " - OUTSIDE THE AXIS RANGE") + collision);
        }
    }

    // ============================================================== external axis
    public class ExternalAxisComponent : RobotComponent
    {
        public ExternalAxisComponent()
            : base("External Axis", "Ext Axis",
                  "A rail (linear track) the robot stands on, or a turntable the part stands on. Connect it to External Axes of Robot Cell. Robot Program then moves it along the print: the rail keeps the robot beside the point being printed, the turntable turns the part so that point faces the robot. Both move smoothly and stay below their top speed.") { }

        public override Guid ComponentGuid => new Guid("8B4D2E61-3F7A-4C95-A0E8-5D1C7B9F2A46");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotExternalAxisIcon.png", this);

        internal static readonly string[] Kinds = { "Rail (the robot moves along it)", "Turntable (the part turns on it)" };

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Kind", "K", "0 = rail, 1 = turntable", GH_ParamAccess.item, 0);
            p.AddPlaneParameter("Place", "P", "Rail: the robot moves along the X axis of this plane (its origin does not matter; at 0 the robot stands where its Base says). Turntable: origin = centre of the table, Z axis = the axis it turns about, X axis = where 0 degrees is", GH_ParamAccess.item, Plane.WorldXY);
            p.AddNumberParameter("Min", "Min", "Lowest value: mm for a rail, degrees for a turntable. Min and Max both 0 = rail 0 to 3000 mm, turntable -360 to 360 degrees", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max", "Max", "Highest value: mm for a rail, degrees for a turntable", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Speed", "V", "Top speed: mm/s for a rail, degrees/s for a turntable. 0 = rail 1000 mm/s, turntable 90 degrees/s", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("Name", "N", "Name of the axis in the KUKA program: E1 ... E6. Empty = E1 for a rail, E2 for a turntable", GH_ParamAccess.item);
            p.AddBooleanParameter("Endless", "End", "Turntable only: it turns on without end (no range)", GH_ParamAccess.item, false);
            p.AddNumberParameter("Offset", "Off", "Rail: the robot stands this far ahead of the point being printed, along the rail, in mm. Turntable: the point being printed is turned this many degrees away from the robot side", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Smoothing", "S", "How much path (mm) the axis averages over: more = calmer, the arm does more of the work", GH_ParamAccess.item, 300.0);
            p.AddBooleanParameter("Coupled", "Cpl", "Turntable only: the KRL targets are counted in a base that turns with the table (KUKA coupled base, EK). Off = counted in the fixed base, the table turns along", GH_ParamAccess.item, false);
            p.AddNumberParameter("Size", "Sz", "Size of the drawing: width of the rail, radius of the table, in mm. 0 = 600 mm", GH_ParamAccess.item, 0.0);
            p[5].Optional = true;
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Choice, Input = 0, Label = "Kind", Items = Kinds });
            rows.Add(Toggle(6, "Endless"));
            rows.Add(Toggle(9, "Coupled"));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("External Axis", "Ext", "The axis: connect to External Axes of Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "What the axis is and its range", GH_ParamAccess.item);
            p.AddMeshParameter("Model", "M", "The axis at 0, as a simple 3D model", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int kind = 0; Plane place = Plane.WorldXY; double min = 0, max = 0, speed = 0, offset = 0, smooth = 300, size = 0; string name = null; bool endless = false, coupled = false;
            da.GetData(0, ref kind); da.GetData(1, ref place); da.GetData(2, ref min); da.GetData(3, ref max); da.GetData(4, ref speed);
            da.GetData(5, ref name); da.GetData(6, ref endless); da.GetData(7, ref offset); da.GetData(8, ref smooth); da.GetData(9, ref coupled); da.GetData(10, ref size);
            var ax = ExternalAxisMaker.Make(kind, place, min, max, speed, name, endless, offset, smooth, coupled, size, out string error);
            if (ax == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            if (ax.IsRail && (endless || coupled)) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Endless and Coupled are for a turntable; a rail leaves them out.");
            Message = ax.Name + (ax.IsRail ? " rail" : " table");
            da.SetData(0, new GH_ObjectWrapper(ax));
            da.SetData(1, ax.ToString());
            da.SetDataList(2, ExternalAxisGeometry.Pose(new RobotCell { ExternalAxes = new List<ExternalAxis> { ax } }, null));
        }
    }

    // ============================================================== cell geometry
    public class CellGeometryComponent : RobotComponent
    {
        public CellGeometryComponent()
            : base("Cell Geometry", "Cell Geo",
                  "The print cell of the FH Muenster in 3D: floor, pedestal of the robot, print bed, safety fence, glass and walls, in the coordinates of the print bed (the origin is the corner the print counts from). It also says where the robot stands in it. Connect Robot Base to Base of Robot, Work Base to Robot Cell and Environment to Robot Cell: the robot then stands where it stands in the lab and the collision check knows the fence, the walls and the floor. The model comes with the plug-in; it is the one drawn in the definition of the FH Muenster.") { }

        public override Guid ComponentGuid => new Guid("C3A7E91D-2F64-4B08-9D53-6E1B8A4F0C72");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotCellGeometryIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBooleanParameter("Walls", "W", "Show the walls of the room. They hide the view from outside, so they are off by default; the collision check knows them either way", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Fence", "F", "Show the safety fence and its glass", GH_ParamAccess.item, true);
            p.AddIntegerParameter("Cell", "C", "Which cell: 0 = FH Muenster, then your own cells from Documents/LARGER/Cells in the order of their file names", GH_ParamAccess.item, 0);
            p.AddTextParameter("File", "File", "Optional. A cell as a .3dm file anywhere on the computer, used instead of the list. Draw it in millimetres in the coordinates of the bed, one part per layer: Bed (not checked for collisions), Robot Base (a point where the robot stands and a line from it along the robot's X axis), Work Base (optional point and line: where the print counts from), and any other layer (Floor, Fence, Table ...) for the collision check", GH_ParamAccess.item);
            p.AddBooleanParameter("Export", "Exp", "True = write the chosen cell as a .3dm into Documents/LARGER/Cells, with its layers as described at File. Open it in Rhino, change it, save it under the name of your cell: it is then in the list", GH_ParamAccess.item, false);
            p[3].Optional = true;
        }


        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Choice, Input = 2, Label = "Cell", Items = CellLibrary.Names() });
            rows.Add(Toggle(0, "Walls"));
            rows.Add(Toggle(1, "Fence"));
            rows.Add(Button(4, "Export cell"));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Environment", "Env", "Floor, pedestal, fence, glass and walls, for the collision check: connect to Environment of Robot Cell", GH_ParamAccess.list);
            p.AddMeshParameter("Bed", "Bed", "The print bed. Not part of the collision check: the nozzle runs right above it", GH_ParamAccess.item);
            p.AddPlaneParameter("Robot Base", "RB", "Where the robot stands: connect to Base of Robot", GH_ParamAccess.item);
            p.AddPlaneParameter("Work Base", "WB", "The corner of the bed the print counts from: connect to Work Base of Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "What the cell is made of and where the robot stands", GH_ParamAccess.list);
            p.HideParameter(0); p.HideParameter(1); p.HideParameter(2); p.HideParameter(3); // the component draws the cell itself
        }

        private readonly List<KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>> _draw = new List<KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>>();

        /// <summary>True when the outputs of this component reach a Robot Program or Robot Simulation (a few components down).</summary>
        internal static bool FeedsRobot(IGH_Component start)
        {
            // components and loose parameters (the wireless relays of Quick Start) both pass data on
            var seen = new HashSet<Guid>();
            var front = new List<IGH_DocumentObject> { start };
            for (int depth = 0; depth < 8 && front.Count > 0; depth++)
            {
                var next = new List<IGH_DocumentObject>();
                foreach (var c in front)
                {
                    var outs = c is IGH_Component comp ? comp.Params.Output : c is IGH_Param prm ? new List<IGH_Param> { prm } : new List<IGH_Param>();
                    foreach (var o in outs)
                        foreach (var r in o.Recipients)
                        {
                            var owner = r.Attributes?.GetTopLevel?.DocObject;
                            if (owner == null || !seen.Add(owner.InstanceGuid)) continue;
                            if (owner is RobotProgramComponent || owner is RobotSimulationComponent) return true;
                            next.Add(owner);
                        }
                }
                front = next;
            }
            return false;
        }
        private static Rhino.Display.DisplayMaterial Mat(int r, int g, int b, double transparency = 0) => new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(r, g, b), transparency);
        private static readonly Rhino.Display.DisplayMaterial FloorMat = Mat(190, 190, 186), BedMat = Mat(70, 72, 78), FenceMat = Mat(235, 190, 40), GlassMat = Mat(150, 200, 230, 0.75), WallMat = Mat(225, 225, 220, 0.6), PedestalMat = Mat(110, 112, 118), OtherMat = Mat(160, 160, 165);

        protected override void BeforeSolveInstance() { _draw.Clear(); base.BeforeSolveInstance(); }

        public override BoundingBox ClippingBox
        {
            get { var b = base.ClippingBox; foreach (var d in _draw) b.Union(d.Key.GetBoundingBox(false)); return b; }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            foreach (var d in _draw) args.Display.DrawMeshShaded(d.Key, d.Value);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool walls = false, fence = true, export = false; int index = 0; string file = null;
            da.GetData(0, ref walls); da.GetData(1, ref fence);
            if (Params.Input.Count > 4) { da.GetData(2, ref index); da.GetData(3, ref file); da.GetData(4, ref export); }
            CellModel cell;
            if (!string.IsNullOrWhiteSpace(file))
            {
                string path = file.Trim().Trim('"');
                if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                cell = CellLibrary.Load(path, out string error);
                if (cell == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The cell could not be read: " + error); return; }
            }
            else
            {
                var log = new List<string>();
                var all = CellLibrary.All(log);
                foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cell file left out: " + l);
                if (all.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The cell model could not be read from the plug-in."); return; }
                if (index < 0 || index >= all.Count) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"There are {all.Count} cells (0 to {all.Count - 1}); using the first."); index = 0; }
                cell = all[index];
            }
            if (export)
            {
                try
                {
                    var before = CellLibrary.Files();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Written " + CellLibrary.Export(cell) + ". Open it in Rhino, change it and save it under the name of your cell.");
                    string moved = CellLibrary.Moved(before);
                    if (moved != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, moved);
                }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the cell: " + ex.Message); }
            }
            foreach (var l in cell.Notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, cell.Name + ": " + l);
            Func<string, Mesh> part = name => cell.Parts.TryGetValue(name, out Mesh m) ? m : null;
            var env = CellLibrary.Environment(cell, true); // the collision check knows the walls even when they are not shown
            // With a Robot Program or Robot Simulation further down, their Show Cell decides whether the
            // cell is drawn: here only the print bed is drawn, so the animation shows the robot and its tool.
            bool simulated = FeedsRobot(this);
            if (simulated) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Only the bed is drawn here: switch Show Cell on Robot Simulation (or Robot Program) to see floor, fence, glass and walls with the robot.");
            foreach (var kv in cell.Parts)
            {
                string n = kv.Key.ToLowerInvariant();
                if (simulated && n != "bed") continue;
                if (n == "walls" && !walls) continue;
                if ((n == "fence" || n == "glass") && !fence) continue;
                var mat = n == "floor" ? FloorMat : n == "pedestal" ? PedestalMat : n == "bed" ? BedMat : n == "fence" ? FenceMat : n == "glass" ? GlassMat : n == "walls" ? WallMat : OtherMat;
                _draw.Add(new KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>(kv.Value, mat));
            }
            var info = new List<string> { $"{cell.Name} ({cell.Source}), in the coordinates of the print bed." };
            foreach (var kv in cell.Parts) { var b = kv.Value.GetBoundingBox(true); info.Add($"{kv.Key}: X {b.Min.X:0}..{b.Max.X:0}, Y {b.Min.Y:0}..{b.Max.Y:0}, Z {b.Min.Z:0}..{b.Max.Z:0} mm{(kv.Key.Equals("Bed", StringComparison.OrdinalIgnoreCase) ? " (not checked for collisions)" : "")}"); }
            info.Add($"Robot: foot at X {cell.RobotBase.OriginX:0.#}, Y {cell.RobotBase.OriginY:0.#}, Z {cell.RobotBase.OriginZ:0.#}.");
            info.Add("Your own cells: " + CellLibrary.Folder + " (set Export to get this cell there as a start).");
            if (part("Bed") == null) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The cell has no layer Bed: nothing is drawn as the print bed.");
            Message = cell.Name.Split(' ')[0];
            da.SetDataList(0, env);
            if (part("Bed") != null) da.SetData(1, part("Bed"));
            da.SetData(2, cell.RobotBase);
            da.SetData(3, cell.WorkBase);
            da.SetDataList(4, info);
        }
    }

}
