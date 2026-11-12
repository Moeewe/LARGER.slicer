using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== robot
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4001")]
    public sealed class RobotArmComponent : Component
    {
        public RobotArmComponent() : base(Io.Name("Robot", "The robot arm. Empty inputs give the KUKA KR120 R2700-2 of the FH Münster cell. For any other six-axis arm connect a file of the Robots plug-in library (.xml).", Io.Robot, 0)) { }
        public RobotArmComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Library", "Lib", "Optional. Path of a robot library file of the Robots plug-in (.xml), or its text. Empty = KUKA KR120 R2700-2.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Cell", "Cell", "Optional. Name of the RobotCell in the file. Empty = the first one.", Access.Item, Requirement.MayBeMissing);
            inputs.AddPlane("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot.").Set(Plane.WorldXY);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Robot", "R", "The robot arm, for Robot Cell.");
            outputs.AddText("Info", "I", "Link lengths, axis ranges and reach.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string lib, cell; Plane basePlane;
            access.GetItem(0, out lib); access.GetItem(1, out cell);
            if (!access.GetItem(2, out basePlane)) basePlane = Plane.WorldXY;
            RobotArm arm;
            try
            {
                if (string.IsNullOrWhiteSpace(lib)) arm = RobotArm.Kr120R2700();
                else arm = RobotArm.FromXml(lib.TrimStart().StartsWith("<") ? lib : File.ReadAllText(lib.Trim()), cell);
            }
            catch (Exception e) { access.AddError("Robot not readable", e.Message); return; }
            arm.Base = Frame.FromPlane(basePlane).Times(arm.Base);
            var info = new List<string> { arm.ToString() };
            for (int i = 0; i < 6; i++) info.Add(string.Format(CultureInfo.InvariantCulture, "A{0}: a {1:0.#}, d {2:0.#}, range {3:0}..{4:0} degrees, {5:0} degrees/s", i + 1, arm.A[i], arm.D[i], arm.MinDeg[i], arm.MaxDeg[i], arm.MaxSpeed[i]));
            access.SetItem(0, arm);
            access.SetTwig(1, info.ToArray(), null, null);
        }
    }

    // ============================================================== tool
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4002")]
    public sealed class RobotToolComponent : Component
    {
        public RobotToolComponent() : base(Io.Name("Robot Tool", "The tool on the flange: where its tip is and what it weighs. The TCP plane is given relative to the flange; its Z axis points out of the nozzle.", Io.Robot, 1)) { }
        public RobotToolComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Name", "N", "Name of the tool.").Set("Extruder");
            inputs.AddPlane("TCP", "TCP", "Tool centre point relative to the flange, as in the Robots plug-in: origin = nozzle tip, Z axis = from the tip back into the tool. The AE20 extruder of the FH Muenster: a YZ plane at -460, 3, 108. Default: a straight tool on the flange.").Set(new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis));
            inputs.AddNumber("Weight", "W", "Weight of the tool in kg. 0 = not written to the program.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Tool", "T", "The tool, for Robot Cell.");
            outputs.AddText("Info", "I", "TCP as X, Y, Z, A, B, C.");
        }

        protected override void Process(IDataAccess access)
        {
            string name; Plane tcp; double weight;
            access.GetItem(0, out name);
            if (!access.GetItem(1, out tcp)) tcp = new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis);
            access.GetItem(2, out weight);
            if (weight < 0) { access.AddError("Invalid weight", "The weight cannot be negative."); return; }
            var tool = new RobotTool { Name = string.IsNullOrWhiteSpace(name) ? "Extruder" : name.Trim(), Tcp = Frame.FromPlane(tcp), Weight = weight };
            tool.Mesh = RobotGeometry.BuiltInTool(tool.Name);
            var abc = tool.Tcp.Abc();
            access.SetItem(0, tool);
            access.SetItem(1, string.Format(CultureInfo.InvariantCulture, "{0}: X {1:0.###}, Y {2:0.###}, Z {3:0.###}, A {4:0.###}, B {5:0.###}, C {6:0.###}, {7:0.##} kg", tool.Name, tcp.OriginX, tcp.OriginY, tcp.OriginZ, abc[0], abc[1], abc[2], weight));
        }
    }

    // ============================================================== cell
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4003")]
    public sealed class RobotCellComponent : Component
    {
        public RobotCellComponent() : base(Io.Name("Robot Cell", "Robot, tool and the place of the work. The Work Base is the frame the program counts from (KUKA: $BASE), usually a corner of the print bed.", Io.Robot, 2)) { }
        public RobotCellComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Robot", "R", "Robot arm. Empty = KUKA KR120 R2700-2 at the world origin.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Tool", "T", "Robot Tool. Empty = tip on the flange.", Access.Item, Requirement.MayBeMissing);
            inputs.AddPlane("Work Base", "WB", "The frame the program counts from, in the Rhino model.").Set(Plane.WorldXY);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Cell", "C", "The robot cell, for Robot Program.");
            outputs.AddText("Info", "I", "Summary.");
        }

        protected override void Process(IDataAccess access)
        {
            var arm = Io.Get<RobotArm>(access, 0) ?? RobotArm.Kr120R2700();
            var tool = Io.Get<RobotTool>(access, 1) ?? new RobotTool();
            Plane wb; if (!access.GetItem(2, out wb)) wb = Plane.WorldXY;
            var cell = new RobotCell { Arm = arm, Tool = tool, WorkBase = Frame.FromPlane(wb) };
            double away = cell.WorkBase.O.DistanceTo(arm.Base.O);
            if (away > arm.Reach) access.AddWarning("Work base far away", $"The work base is {away:0} mm from the robot; its reach is {arm.Reach:0} mm.");
            access.SetItem(0, cell);
            access.SetItem(1, $"{cell}; work base {away:0} mm from the robot");
        }
    }

    // ============================================================== program
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4004")]
    public sealed class RobotProgramComponent : Component
    {
        public RobotProgramComponent() : base(Io.Name("Robot Program", "Turns the toolpath into robot moves: the tool tip on every path point, the tool along the path's tool axis. Checks reach, axis ranges and the wrist, and writes the moves for the DXR Generator and as a KRL program.", Io.Robot, 3)) { }
        public RobotProgramComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path, where the part stands in the Rhino model.");
            inputs.AddGeneric("Cell", "C", "Robot Cell. Empty = KUKA KR120 R2700-2 at the world origin, tip on the flange.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Turn", "Tu", "Turns the tool about its own axis, in degrees.").Set(0.0);
            inputs.AddText("Name", "N", "Name of the program.").Set("LARGER_PRINT");
            inputs.AddNumber("Pose", "P", "Shows the arm at this place of the path: 0 = start, 1 = end.").Set(0.0);
            inputs.AddText("File", "File", "Optional: full path of the .src file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the KRL file.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Robot Path", "Path", "One PTP line per path point with X, Y, Z, A, B, C: for the DXR Generator.", Access.Twig);
            outputs.AddNumber("Extrusion", "E", "Extrusion per line.", Access.Twig);
            outputs.AddNumber("Speed", "F", "Speed in mm/min per line.", Access.Twig);
            outputs.AddText("KRL", "KRL", "The path as a KUKA program (.src).", Access.Twig);
            outputs.AddBoolean("OK", "OK", "True if the robot reaches every point within its axis ranges.");
            outputs.AddText("Report", "R", "Reach, axis ranges, wrist and axis speed.", Access.Twig);
            outputs.AddPoint("Unreachable", "U", "Points the robot cannot get to.", Access.Twig);
            outputs.AddPolyline("Arm", "Arm", "The arm at the chosen Pose, as a line through its joints.");
            outputs.AddText("File Name", "Fn", "A name for the DXR file, made of DXR, date and time, the Name, weight and print time (DXR_2611051216_Test Object_2668g_53min): connect to File of the DXR Generator.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            var cell = Io.Get<RobotCell>(access, 1) ?? new RobotCell();
            double turn, pose; string name, file; bool save;
            access.GetItem(2, out turn); access.GetItem(3, out name); access.GetItem(4, out pose); access.GetItem(5, out file); access.GetItem(6, out save);
            var r = RobotProgram.Build(tp, cell, turn, name, true);
            if (!r.Ok) access.AddWarning("Not reachable", string.Join("\n", r.Report));
            else if (r.Singular > 0) access.AddRemark("Wrist near singularity", r.Report.First(l => l.StartsWith("Wrist")));
            Files.Save(access, save, file, r.Krl, RobotProgram.ProgramName(name) + ".src");
            access.SetItem(8, OutputFile.Name("DXR", name, tp, ""));
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            access.SetTwig(1, r.Extrusion.ToArray(), null, null);
            access.SetTwig(2, r.Speed.ToArray(), null, null);
            access.SetTwig(3, r.Krl.ToArray(), null, null);
            access.SetItem(4, r.Ok);
            access.SetTwig(5, r.Report.ToArray(), null, null);
            access.SetTwig(6, Enumerable.Range(0, r.Moves.Count).Where(i => !r.Moves[i].Reachable || !r.Moves[i].InLimits).Select(i => tp.Points[i]).ToArray(), null, null);
            int at = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
            if (r.Moves[at].Axes != null)
            {
                var pts = cell.Arm.Skeleton(r.Moves[at].Axes);
                pts.Add(tp.Points[at]);
                access.SetItem(7, new Polyline(pts));
            }
        }
    }
}
