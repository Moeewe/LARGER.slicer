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
            inputs.AddText("Cell Name", "CN", "Optional. Name of the RobotCell in the file. Empty = the first one.", Access.Item, Requirement.MayBeMissing);
            inputs.AddPlane("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot.").Set(Plane.WorldXY);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Robot", "R", "The robot arm, for Robot Cell.");
            outputs.AddText("Info", "I", "Link lengths, axis ranges and reach.", Access.Twig);
            outputs.AddMesh("Meshes", "M", "The robot in its home position: base and six links, read from the library of the Robots plug-in (Documents/RobotSystems).", Access.Twig);
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
            string libFile = !string.IsNullOrWhiteSpace(lib) && !lib.TrimStart().StartsWith("<") ? lib.Trim() : null;
            string found = RobotGeometry.Load(arm, null, libFile, out string why);
            var info = new List<string> { arm.ToString(), found != null ? "3D model from " + found : "No 3D model: " + why };
            if (found == null) access.AddRemark("No 3D model", why);
            else access.SetTwig(2, RobotGeometry.Pose(new RobotCell { Arm = arm, Tool = new RobotTool() }, RobotGeometry.HomePose).ToArray(), null, null);
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
            inputs.AddMesh("Environment", "Env", "Optional. What stands around the robot (floor, fence, walls), for the collision check of Robot Program.", Access.Twig, Requirement.MayBeMissing);
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
            Mesh[] env; if (access.GetItemArray(3, out env) && env != null) cell.Environment = env.Where(m => m != null && m.IsValid).ToList();
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
            outputs.AddMesh("Robot", "Rob", "The robot with its tool at the chosen Pose, as a 3D model.", Access.Twig);
            outputs.AddGeneric("Program", "Prg", "The finished program with its cell: connect to Robot Simulation to play it.");
            outputs.AddPoint("Collisions", "Hit", "Path points at which the robot collides with itself, the Environment of the cell or the part already printed.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            var cell = Io.Get<RobotCell>(access, 1) ?? new RobotCell();
            double turn, pose; string name, file; bool save;
            access.GetItem(2, out turn); access.GetItem(3, out name); access.GetItem(4, out pose); access.GetItem(5, out file); access.GetItem(6, out save);
            var r = RobotProgram.Build(tp, cell, turn, name, true);
            var col = RobotGeometry.Collisions(r, cell, cell.Environment, 500, tp.Points, tp.Settings.BeadWidth);
            if (col.Checked) r.Report.Add(col.Report);
            if (!r.Ok) access.AddWarning("Not runnable", string.Join("\n", r.Report.Where(l => l.StartsWith("NOT"))));
            if (col.Hits > 0) { access.AddWarning("Collision", col.Report); access.SetTwig(11, col.Moves.Select(i => tp.Points[i]).ToArray(), null, null); }
            access.SetItem(10, RobotRun.Make(r, cell, tp));
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
                var model = RobotGeometry.Pose(cell, r.Moves[at].Axes);
                if (model != null) access.SetTwig(9, model.ToArray(), null, null);
            }
        }
    }

    // ============================================================== simulation
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4005")]
    public sealed class RobotSimulationComponent : Component
    {
        public RobotSimulationComponent() : base(Io.Name("Robot Simulation", "Plays the robot program: the robot with its tool at any moment of the print. Time runs from 0 (start) to 1 (end) and follows the real time of the print, slower where an axis cannot turn faster. Moving the slider only places the robot: the program is not calculated again.", Io.Robot, 4)) { }
        public RobotSimulationComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Program", "Prg", "Program of Robot Program.");
            inputs.AddNumber("Time", "T", "Where in the print: 0 = start, 1 = end.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Robot", "Rob", "The robot with its tool at that moment.", Access.Twig);
            outputs.AddNumber("Axes", "A", "Axis angles A1 to A6 in degrees.", Access.Twig);
            outputs.AddPlane("TCP", "TCP", "The tool tip: origin on the path, Z along the tool axis.");
            outputs.AddInteger("Move", "M", "Number of the move the robot is on.");
            outputs.AddNumber("Seconds", "s", "Time since the start of the print.");
            outputs.AddText("Info", "I", "Time, move and axis angles.");
        }

        protected override void Process(IDataAccess access)
        {
            var run = Io.Get<RobotRun>(access, 0);
            if (run == null || run.Points.Count == 0) { access.AddError("No program", "Connect Program of Robot Program."); return; }
            double time; access.GetItem(1, out time);
            time = Math.Max(0, Math.Min(1, time));
            double seconds = time * run.Duration;
            var axes = run.AxesAt(seconds, out int move, out double share);
            access.SetItem(3, move); access.SetItem(4, seconds);
            if (axes == null) { access.AddWarning("Not reachable", $"The robot cannot get to move {move}."); return; }
            var arm = run.Cell.Arm;
            var model = RobotGeometry.Pose(run.Cell, axes);
            if (model != null) access.SetTwig(0, model.ToArray(), null, null);
            Frame tip = arm.Base.Times(arm.Forward(axes)).Times(Frame.World.TurnedAboutZ(Math.PI)).Times(run.Cell.Tool.Tcp);
            access.SetTwig(1, axes, null, null);
            access.SetItem(2, tip.ToPlane());
            var ci = CultureInfo.InvariantCulture;
            access.SetItem(5, TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", ci) + " of " + TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci) + $", move {move}, " + string.Join(", ", axes.Select((v, k) => "A" + (k + 1) + " " + v.ToString("0.0", ci))));
        }
    }

    // ============================================================== cell geometry
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4006")]
    public sealed class CellGeometryComponent : Component
    {
        public CellGeometryComponent() : base(Io.Name("Cell Geometry", "The print cell of the FH Muenster in 3D: floor, pedestal of the robot, print bed, safety fence, glass and walls, in the coordinates of the print bed. It also says where the robot stands in it, as in the definition of the FH Muenster.", Io.Robot, 5)) { }
        public CellGeometryComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddBoolean("Walls", "W", "Add the walls of the room. They hide the view from outside; the fence stands in front of them, so they rarely matter for the collision check.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Environment", "Env", "Floor, pedestal, fence and glass (and the walls if asked), for the collision check: connect to Environment of Robot Cell.", Access.Twig);
            outputs.AddMesh("Bed", "Bed", "The print bed. Not part of the collision check: the nozzle runs right above it.");
            outputs.AddPlane("Robot Base", "RB", "Where the robot stands: connect to Base of Robot.");
            outputs.AddPlane("Work Base", "WB", "The corner of the bed the print counts from: connect to Work Base of Robot Cell.");
        }

        protected override void Process(IDataAccess access)
        {
            bool walls; access.GetItem(0, out walls);
            var cell = RobotGeometry.FhMuensterCell();
            if (cell == null) { access.AddError("No cell model", "The cell model could not be read from the plug-in."); return; }
            Func<string, Mesh> part = n => cell.TryGetValue(n, out Mesh m) ? m : null;
            var env = new[] { "Floor", "Pedestal", "Fence", "Glass" }.Concat(walls ? new[] { "Walls" } : new string[0]).Select(part).Where(m => m != null).ToArray();
            access.SetTwig(0, env, null, null);
            if (part("Bed") != null) access.SetItem(1, part("Bed"));
            access.SetItem(2, RobotGeometry.FhMuensterRobotBase());
            access.SetItem(3, Plane.WorldXY);
        }
    }
}
