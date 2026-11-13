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
                  "The robot arm. Empty inputs give the KUKA KR120 R2700-2 of the FH Münster cell. For any other six-axis arm connect a file of the Robots plug-in library (.xml): link lengths, axis ranges and speeds are read from it.") { }

        public override Guid ComponentGuid => new Guid("A4F2C1E8-7B35-4D90-9C61-2E8F5A0B7D13");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotArmIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Library", "Lib", "Optional. Path of a robot library file of the Robots plug-in (.xml), or its text. Empty = KUKA KR120 R2700-2", GH_ParamAccess.item);
            p.AddTextParameter("Cell Name", "CN", "Optional. Name of the RobotCell in the file. Empty = the first one", GH_ParamAccess.item);
            p.AddPlaneParameter("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot", GH_ParamAccess.item, Plane.WorldXY);
            p.AddTextParameter("Geometry File", "Geo", "Optional. The Rhino file (.3dm) with the 3D model of the robot, as the Robots plug-in keeps it beside its library file: a layer RobotArm.MANUFACTURER.MODEL with seven meshes, the base and the six links. Empty = looked for beside the Library file and in Documents/RobotSystems", GH_ParamAccess.item);
            p[0].Optional = true; p[1].Optional = true; p[3].Optional = true;
        }

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
                  "The tool on the flange: where its tip is and what it weighs. The TCP plane is given relative to the flange, as on the robot's pendant or in the Robots plug-in; its Z axis points out of the nozzle.") { }

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

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Tool", "T", "The tool, for Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "TCP as X, Y, Z, A, B, C", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string name = "Extruder"; Plane tcp = new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis); double weight = 0; Point3d centroid = Point3d.Origin;
            da.GetData(0, ref name); da.GetData(1, ref tcp); da.GetData(2, ref weight); da.GetData(3, ref centroid);
            if (!tcp.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The TCP plane is not valid."); return; }
            if (weight < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The weight cannot be negative."); return; }
            var tool = new RobotTool { Name = string.IsNullOrWhiteSpace(name) ? "Extruder" : name.Trim(), Tcp = Frame.FromPlane(tcp), Weight = weight, Centroid = centroid };
            if (Params.Input.Count > 4)
            {
                var meshes = new List<Mesh>();
                if (da.GetDataList(4, meshes) && meshes.Count > 0)
                {
                    var joined = new Mesh();
                    foreach (var m in meshes) if (m != null && m.IsValid) joined.Append(m);
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
            var arm = SlicerIO.Get<RobotArm>(da, 0) ?? RobotArm.Kr120R2700();
            var tool = SlicerIO.Get<RobotTool>(da, 1) ?? new RobotTool();
            Plane wb = Plane.WorldXY; string name = "Bed";
            da.GetData(2, ref wb); da.GetData(3, ref name);
            if (!wb.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The work base plane is not valid."); return; }
            var cell = new RobotCell { Arm = arm, Tool = tool, WorkBase = Frame.FromPlane(wb), BaseName = string.IsNullOrWhiteSpace(name) ? "Bed" : name.Trim() };
            if (Params.Input.Count > 4) { var env = new List<Mesh>(); if (da.GetDataList(4, env)) cell.Environment = env.Where(m => m != null && m.IsValid).ToList(); }
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
            p[1].Optional = true; p[6].Optional = true; p[8].Optional = true;
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(Toggle(4, "Check reach"));
            rows.Add(Toggle(10, "Check collisions"));
            rows.Add(Button(7, "Save KRL"));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Robot Path", "Path", "One PTP line per path point with X, Y, Z, A, B, C: connect to Robot Path of the DXR Generator", GH_ParamAccess.list);
            p.AddNumberParameter("Extrusion", "E", "Extrusion per line, plus one closing value (the DXR Generator drops the last value)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "F", "Speed in mm/min per line, plus one closing value", GH_ParamAccess.list);
            p.AddTextParameter("KRL", "KRL", "The path as a KUKA program (.src): tool, base and linear moves", GH_ParamAccess.list);
            p.AddBooleanParameter("OK", "OK", "True if the robot reaches every point within its axis ranges", GH_ParamAccess.item);
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
        private readonly List<Mesh> _robot = new List<Mesh>();
        private static readonly Rhino.Display.DisplayMaterial BaseMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(60, 60, 64));
        private static readonly Rhino.Display.DisplayMaterial ArmMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(240, 110, 20));
        private static readonly Rhino.Display.DisplayMaterial ToolMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(90, 120, 150));

        private readonly List<Mesh> _axes = new List<Mesh>();
        internal static readonly Rhino.Display.DisplayMaterial AxisMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(150, 152, 158));
        internal static readonly Rhino.Display.DisplayMaterial HitMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(230, 30, 30));
        // what collides at the chosen pose: robot bodies (0 base, 1-6 links, 7 tool), axis models, other meshes
        private readonly HashSet<int> _hitRobot = new HashSet<int>(), _hitAxes = new HashSet<int>();
        private readonly List<Mesh> _hitOther = new List<Mesh>();

        protected override void BeforeSolveInstance() { _robot.Clear(); _axes.Clear(); _hitRobot.Clear(); _hitAxes.Clear(); _hitOther.Clear(); base.BeforeSolveInstance(); }

        public override BoundingBox ClippingBox
        {
            get { var b = base.ClippingBox; foreach (var m in _robot.Concat(_axes)) b.Union(m.GetBoundingBox(false)); return b; }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            for (int i = 0; i < _robot.Count; i++)
                args.Display.DrawMeshShaded(_robot[i], _hitRobot.Contains(i) ? HitMaterial : i == 0 ? BaseMaterial : (i >= 7 ? ToolMaterial : ArmMaterial));
            for (int i = 0; i < _axes.Count; i++) args.Display.DrawMeshShaded(_axes[i], _hitAxes.Contains(i) ? HitMaterial : AxisMaterial);
            foreach (var m in _hitOther) args.Display.DrawMeshShaded(m, HitMaterial);
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

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            var cell = SlicerIO.Get<RobotCell>(da, 1) ?? new RobotCell();
            double turn = 0, pose = 0; string name = "LARGER_PRINT", file = null; bool check = true, save = false;
            da.GetData(2, ref turn); da.GetData(3, ref name); da.GetData(4, ref check); da.GetData(5, ref pose); da.GetData(6, ref file); da.GetData(7, ref save);

            var r = RobotProgram.Build(tp, cell, turn, name, check);
            if (!r.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.FirstOrDefault(l => l.StartsWith("NOT")) ?? "The robot cannot run this path.");
            else if (r.Singular > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, r.Report.First(l => l.StartsWith("Wrist")));
            if (r.Speed.Any(v => v <= 0)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A move has speed 0; the machine would stop there.");
            if (save)
            {
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(file, RobotProgram.ProgramName(name) + ".src", r.Krl)); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            Message = r.Ok ? r.Moves.Count + " moves" : "not reachable";

            var e = new List<double>(r.Extrusion) { 0 };
            var f = new List<double>(r.Speed) { r.Speed.Count > 0 ? r.Speed[r.Speed.Count - 1] : tp.Settings.Speed };
            if (Params.Output.Count > 11) da.SetData(11, OutputFile.Name("DXR", name, tp, ""));
            if (Params.Output.Count > 12) da.SetData(12, new GH_ObjectWrapper(RobotRun.Make(r, cell, tp)));
            // with a Robot Simulation connected, that one shows the robot
            bool simulated = Params.Output.Count > 12 && Params.Output[12].Recipients.Count > 0;
            da.SetDataList(0, r.Lines);
            da.SetDataList(1, e);
            da.SetDataList(2, f);
            da.SetDataList(3, r.Krl);
            if (check)
            {
                da.SetDataList(6, r.Moves.Select(m => m.Axes == null ? "" : string.Join(", ", m.Axes.Select((v, i) => "A" + (i + 1) + " " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
                    + (m.External == null ? "" : string.Concat(m.External.Select((v, i) => ", " + cell.ExternalAxes[i].Name + " " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))))));
                da.SetDataList(7, Enumerable.Range(0, r.Moves.Count).Where(i => !r.Moves[i].Reachable || !r.Moves[i].InLimits).Select(i => r.Moves[i].Part.Pt(tp.Points[i])));
                int at = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
                if (r.Moves[at].Axes != null)
                {
                    var pts = cell.Arm.Skeleton(r.Moves[at].Axes, r.Moves[at].RobotBase);
                    pts.Add(r.Moves[at].Part.Pt(tp.Points[at])); // flange to tool tip
                    da.SetData(8, new PolylineCurve(pts));
                    if (Params.Output.Count > 9)
                    {
                        var model = RobotGeometry.Pose(cell, r.Moves[at].Axes, r.Moves[at].External);
                        var axesModel = ExternalAxisGeometry.Pose(cell, r.Moves[at].External);
                        if (model != null) { da.SetDataList(9, model.Concat(axesModel)); if (!simulated) _robot.AddRange(model); }
                        else if (axesModel.Count > 0) da.SetDataList(9, axesModel);
                        if (!simulated) _axes.AddRange(axesModel);
                    }
                }
                if (Params.Input.Count > 9 && Params.Output.Count > 10)
                {
                    var env = new List<Mesh>(); int poses = 500; bool collide = true; double clearance = 10;
                    da.GetDataList(8, env); da.GetData(9, ref poses);
                    if (Params.Input.Count > 10) da.GetData(10, ref collide);
                    if (Params.Input.Count > 11) da.GetData(11, ref clearance);
                    if (collide && poses != 0)
                    {
                        // the meshes given here are checked as well as those of the cell
                        var checkCell = cell;
                        if (env.Count > 0)
                        {
                            checkCell = new RobotCell { Arm = cell.Arm, Tool = cell.Tool, WorkBase = cell.WorkBase, BaseName = cell.BaseName, ExternalAxes = cell.ExternalAxes, Environment = new List<Mesh>() };
                            if (cell.Environment != null) checkCell.Environment.AddRange(cell.Environment);
                            checkCell.Environment.AddRange(env.Where(m => m != null && m.IsValid));
                        }
                        var col = r.CheckCollisions(tp, checkCell, new CollisionOptions { Poses = poses, Clearance = Math.Max(0, clearance) });
                        if (col.Hits > 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", col.Lines.Take(4)));
                            da.SetDataList(10, col.Moves.Select(i => r.Moves[i].Part.Pt(tp.Points[i])));
                            Message = "collision";
                        }
                        else if (col.Checked) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, col.Summary);
                        if (Params.Output.Count > 14) { da.SetDataList(13, col.Moves); da.SetDataList(14, col.Bodies); }
                        int atPose = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
                        if (!simulated && r.Moves[atPose].Axes != null) MarkHits(col.Model, r.Moves[atPose].Axes, r.Moves[atPose].External, atPose, _hitRobot, _hitAxes, _hitOther);
                    }
                    else r.Report.Add(collide ? "Collisions were not checked (Collision Poses is 0)." : "Collisions were not checked (Check Collisions is off).");
                }
            }
            da.SetData(4, r.Ok);
            da.SetDataList(5, r.Report);
        }
    }

    // ============================================================== simulation
    public class RobotSimulationComponent : RobotComponent
    {
        public RobotSimulationComponent()
            : base("Robot Simulation", "Simulate",
                  "Plays the robot program: the robot with its tool at any moment of the print, as in Program Simulation of the Robots plug-in. Time runs from 0 (start) to 1 (end) and follows the real time of the print, so half way on the slider is half way through the print. Move the slider, or set Play and it runs by itself (Film Length = how many seconds one run takes on the screen). Outputs the axis angles and the place of the tool tip at that moment; with tilted layers (angled, non-planar, multi-axis) you see the tool lean. Moving the slider only places the robot: the program is not calculated again.") { }

        public override Guid ComponentGuid => new Guid("5D2E8B14-9A63-4F70-B1C8-3A6F0D9E2475");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotProgramIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Program", "Prg", "Program of Robot Program", GH_ParamAccess.item);
            p.AddNumberParameter("Time", "T", "Where in the print: 0 = start, 1 = end", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Play", "Play", "True = the simulation runs by itself, from where Time stands, and starts again at the end. False = it stands where Time says", GH_ParamAccess.item, false);
            p.AddNumberParameter("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds. A print of an hour in 30 seconds runs 120 times faster than in the lab", GH_ParamAccess.item, 30.0);
        }

        // Play: the component keeps its own clock and asks Grasshopper for the next picture.
        private bool _playing;
        private DateTime _playSince;
        private double _playFrom;

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(Toggle(2, "Play", "\u25A0  Stop", "\u25B6  Play"));
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
            p.AddMeshParameter("Environment", "Env", "What stands around the robot (the Environment of the cell), drawn with the simulation", GH_ParamAccess.list);
            p.AddMeshParameter("Colliding", "Col", "What collides at this moment (drawn red): robot links, tool, rail, table, environment, and a ball where it touches. Empty = clear", GH_ParamAccess.list);
            p.HideParameter(0); // drawn by the component, in colour
            p.HideParameter(8); p.HideParameter(9); p.HideParameter(10);
        }

        private readonly List<Mesh> _robot = new List<Mesh>();
        private readonly List<Mesh> _axes = new List<Mesh>();
        private static readonly Rhino.Display.DisplayMaterial BaseMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(60, 60, 64));
        private static readonly Rhino.Display.DisplayMaterial ArmMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(240, 110, 20));
        private static readonly Rhino.Display.DisplayMaterial ToolMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(90, 120, 150));

        private readonly List<Mesh> _env = new List<Mesh>();
        private static readonly Rhino.Display.DisplayMaterial EnvMaterial = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(200, 200, 196), 0.55);

        private readonly HashSet<int> _hitRobot = new HashSet<int>(), _hitAxes = new HashSet<int>();
        private readonly List<Mesh> _hitOther = new List<Mesh>();

        protected override void BeforeSolveInstance() { _robot.Clear(); _axes.Clear(); _env.Clear(); _hitRobot.Clear(); _hitAxes.Clear(); _hitOther.Clear(); base.BeforeSolveInstance(); }

        public override BoundingBox ClippingBox
        {
            get { var b = base.ClippingBox; foreach (var m in _robot.Concat(_axes).Concat(_env)) b.Union(m.GetBoundingBox(false)); return b; }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            var hit = RobotProgramComponent.HitMaterial;
            for (int i = 0; i < _robot.Count; i++)
                args.Display.DrawMeshShaded(_robot[i], _hitRobot.Contains(i) ? hit : i == 0 ? BaseMaterial : (i >= 7 ? ToolMaterial : ArmMaterial));
            for (int i = 0; i < _axes.Count; i++) args.Display.DrawMeshShaded(_axes[i], _hitAxes.Contains(i) ? hit : RobotProgramComponent.AxisMaterial);
            foreach (var m in _env) if (!_hitOther.Contains(m)) args.Display.DrawMeshShaded(m, EnvMaterial);
            foreach (var m in _hitOther) args.Display.DrawMeshShaded(m, hit);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var run = SlicerIO.Get<RobotRun>(da, 0);
            if (run == null || run.Points.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect Program of Robot Program."); return; }
            double time = 0; da.GetData(1, ref time);
            time = Math.Max(0, Math.Min(1, time));
            bool play = false; double film = 30;
            if (Params.Input.Count > 3) { da.GetData(2, ref play); da.GetData(3, ref film); }
            if (play)
            {
                if (!_playing) { _playing = true; _playSince = DateTime.UtcNow; _playFrom = time >= 1 ? 0 : time; }
                double ran = (DateTime.UtcNow - _playSince).TotalSeconds / Math.Max(1.0, film);
                time = _playFrom + ran;
                time -= Math.Floor(time); // start again at the end
                var ghDoc = OnPingDocument();
                if (ghDoc != null) ghDoc.ScheduleSolution(40, d => ExpireSolution(false));
            }
            else _playing = false;
            double seconds = time * run.Duration;
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
            if (Params.Output.Count > 9 && run.Cell.Environment != null) { _env.AddRange(run.Cell.Environment); da.SetDataList(9, run.Cell.Environment); }
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
            if (play) Message = "> " + Message;
            da.SetData(5, $"{Message} of {TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci)}, move {move} of {run.Points.Count - 1}, " + string.Join(", ", axes.Select((v, k) => "A" + (k + 1) + " " + v.ToString("0.0", ci)))
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotCellIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotCellIcon.png", this);

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
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Written " + CellLibrary.Export(cell) + ". Open it in Rhino, change it and save it under the name of your cell."); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the cell: " + ex.Message); }
            }
            Func<string, Mesh> part = name => cell.Parts.TryGetValue(name, out Mesh m) ? m : null;
            var env = CellLibrary.Environment(cell, true); // the collision check knows the walls even when they are not shown
            foreach (var kv in cell.Parts)
            {
                string n = kv.Key.ToLowerInvariant();
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
