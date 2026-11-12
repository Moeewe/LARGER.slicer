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
    public abstract class RobotComponent : GH_Component
    {
        protected RobotComponent(string name, string nick, string description) : base(name, nick, description, "LARGER", "Robot") { }
    }

    // ============================================================== robot
    public class RobotArmComponent : RobotComponent
    {
        public RobotArmComponent()
            : base("Robot", "Robot",
                  "The robot arm. Empty inputs give the KUKA KR120 R2700-2 of the FH Münster cell. For any other six-axis arm connect a file of the Robots plug-in library (.xml): link lengths, axis ranges and speeds are read from it.") { }

        public override Guid ComponentGuid => new Guid("A4F2C1E8-7B35-4D90-9C61-2E8F5A0B7D13");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotArmIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Library", "Lib", "Optional. Path of a robot library file of the Robots plug-in (.xml), or its text. Empty = KUKA KR120 R2700-2", GH_ParamAccess.item);
            p.AddTextParameter("Cell", "Cell", "Optional. Name of the RobotCell in the file. Empty = the first one", GH_ParamAccess.item);
            p.AddPlaneParameter("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot", GH_ParamAccess.item, Plane.WorldXY);
            p[0].Optional = true; p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Robot", "R", "The robot arm, for Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Link lengths, axis ranges and reach", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string lib = null, cell = null; Plane basePlane = Plane.WorldXY;
            da.GetData(0, ref lib); da.GetData(1, ref cell); da.GetData(2, ref basePlane);
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
            var info = new List<string> { arm.ToString() };
            for (int i = 0; i < 6; i++) info.Add($"A{i + 1}: a {arm.A[i]:0.#}, d {arm.D[i]:0.#}, range {arm.MinDeg[i]:0}..{arm.MaxDeg[i]:0} degrees, {arm.MaxSpeed[i]:0} degrees/s");
            da.SetData(0, new GH_ObjectWrapper(arm));
            da.SetDataList(1, info);
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotToolIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Name", "N", "Name of the tool", GH_ParamAccess.item, "Extruder");
            p.AddPlaneParameter("TCP", "TCP", "Tool centre point relative to the flange. Origin = nozzle tip, Z axis = out of the nozzle", GH_ParamAccess.item, Plane.WorldXY);
            p.AddNumberParameter("Weight", "W", "Weight of the tool in kg (0 = not written to the program)", GH_ParamAccess.item, 0.0);
            p.AddPointParameter("Centroid", "C", "Centre of mass relative to the flange", GH_ParamAccess.item, Point3d.Origin);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Tool", "T", "The tool, for Robot Cell", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "TCP as X, Y, Z, A, B, C", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string name = "Extruder"; Plane tcp = Plane.WorldXY; double weight = 0; Point3d centroid = Point3d.Origin;
            da.GetData(0, ref name); da.GetData(1, ref tcp); da.GetData(2, ref weight); da.GetData(3, ref centroid);
            if (!tcp.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The TCP plane is not valid."); return; }
            if (weight < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The weight cannot be negative."); return; }
            var tool = new RobotTool { Name = string.IsNullOrWhiteSpace(name) ? "Extruder" : name.Trim(), Tcp = Frame.FromPlane(tcp), Weight = weight, Centroid = centroid };
            var abc = tool.Tcp.Abc();
            Message = tool.Name;
            da.SetData(0, new GH_ObjectWrapper(tool));
            da.SetData(1, $"{tool.Name}: X {tcp.OriginX:0.###}, Y {tcp.OriginY:0.###}, Z {tcp.OriginZ:0.###}, A {abc[0]:0.###}, B {abc[1]:0.###}, C {abc[2]:0.###}, {weight:0.##} kg");
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotCellIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Robot", "R", "Robot arm. Empty = KUKA KR120 R2700-2 at the world origin", GH_ParamAccess.item);
            p.AddGenericParameter("Tool", "T", "Robot Tool. Empty = tip on the flange", GH_ParamAccess.item);
            p.AddPlaneParameter("Work Base", "WB", "The frame the program counts from, in the Rhino model", GH_ParamAccess.item, Plane.WorldXY);
            p.AddTextParameter("Base Name", "N", "Name of the work base, for the report", GH_ParamAccess.item, "Bed");
            p[0].Optional = true; p[1].Optional = true;
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
            double away = cell.WorkBase.O.DistanceTo(arm.Base.O);
            if (away > arm.Reach) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The work base is {away:0} mm from the robot; its reach is {arm.Reach:0} mm.");
            da.SetData(0, new GH_ObjectWrapper(cell));
            da.SetData(1, $"{cell}; work base '{cell.BaseName}' {away:0} mm from the robot");
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RobotProgramIcon.png");

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
            p[1].Optional = true; p[6].Optional = true;
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
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(file, (string.IsNullOrWhiteSpace(name) ? "LARGER_PRINT" : name.Trim()) + ".src", r.Krl)); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            Message = r.Ok ? r.Moves.Count + " moves" : "not reachable";

            var e = new List<double>(r.Extrusion) { 0 };
            var f = new List<double>(r.Speed) { r.Speed.Count > 0 ? r.Speed[r.Speed.Count - 1] : tp.Settings.Speed };
            da.SetDataList(0, r.Lines);
            da.SetDataList(1, e);
            da.SetDataList(2, f);
            da.SetDataList(3, r.Krl);
            da.SetData(4, r.Ok);
            da.SetDataList(5, r.Report);
            if (check)
            {
                da.SetDataList(6, r.Moves.Select(m => m.Axes == null ? "" : string.Join(", ", m.Axes.Select((v, i) => "A" + (i + 1) + " " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))));
                da.SetDataList(7, Enumerable.Range(0, r.Moves.Count).Where(i => !r.Moves[i].Reachable || !r.Moves[i].InLimits).Select(i => tp.Points[i]));
                int at = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
                if (r.Moves[at].Axes != null)
                {
                    var pts = cell.Arm.Skeleton(r.Moves[at].Axes);
                    pts.Add(tp.Points[at]); // flange to tool tip
                    da.SetData(8, new PolylineCurve(pts));
                }
            }
        }
    }
}
