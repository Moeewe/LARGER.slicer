using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
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
        public RobotArmComponent(IReader reader) : base(reader)
        {
            // files saved before the choice of robot: add it at the end, the wires stay
            if (Parameters.InputCount == 3) AddModel(new InputAdder(this));
        }
        protected override IIcon IconInternal => Io.Icon("RobotArmIcon");

        internal static readonly string[] Models = new[] { "KUKA KR120 R2700-2 (FH Muenster)" }.Concat(URModel.All.Select(m => "Universal Robots " + m.Name)).ToArray();
        private static void AddModel(InputAdder inputs) =>
            inputs.AddInteger("Robot", "Rb", "The robot:\n" + string.Join("\n", Models.Select((n, i) => i + " = " + n)) + "\nA library file in Library wins.").Set(0).Choices(Models);

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Library", "Lib", "Optional. Path of a robot library file of the Robots plug-in (.xml), or its text. Empty = KUKA KR120 R2700-2.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Cell Name", "CN", "Optional. Name of the RobotCell in the file. Empty = the first one.", Access.Item, Requirement.MayBeMissing);
            inputs.AddPlane("Base", "B", "Where the robot stands in the Rhino model: the plane of its foot.").Set(Plane.WorldXY);
            AddModel(inputs);
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
            int model = 0; if (Parameters.InputCount > 3) access.GetItem(3, out model);
            // a Universal Robots arm: its own kinematics and the model Universal Robots publishes
            if (model > 0 && string.IsNullOrWhiteSpace(lib))
            {
                if (!basePlane.IsValid) { access.AddError("Base", "The base plane is not valid."); return; }
                var um = URModel.All[Math.Min(URModel.All.Length, model) - 1];
                var ua = new URArm { Model = um, Base = Frame.FromPlane(basePlane) };
                access.SetItem(0, ua);
                access.SetTwig(1, new[]
                {
                    string.Format(CultureInfo.InvariantCulture, "Universal Robots {0}: reach about {1:0} mm, payload {2:0.#} kg", um.Name, um.Reach, um.Payload),
                    "3D model by Universal Robots (ur_description, BSD-3-Clause)",
                    "Robot Program writes URScript for it; Upload to robot sends it to the robot.",
                }, null, null);
                var home = URMeshes.Arm(um.Name, ua.Base, URModel.Home);
                if (home.Count > 0) access.SetTwig(2, home.ToArray(), null, null);
                return;
            }
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
        public RobotToolComponent() : base(Io.Name("Robot Tool", "The tool on the flange: where its tip is and what it weighs. The TCP plane is given relative to the flange; its Z axis points out of the nozzle.", Io.Robot, 0)) { }
        public RobotToolComponent(IReader reader) : base(reader)
        {
            // files saved before Centroid and Mesh existed: add them at the end, the wires stay
            if (Parameters.InputCount == 3) AddLoad(new InputAdder(this));
            if (Parameters.InputCount == 5) AddPreset(new InputAdder(this));
        }
        protected override IIcon IconInternal => Io.Icon("RobotToolIcon");

        // 0 = own (the inputs), 1 = AE20 extruder of the Weber DXR25 (KUKA), 2.. = the tools of the FH Muenster UR5
        internal static readonly string[] Presets = { "Own tool (the inputs)", "AE20 extruder (Weber DXR25, KUKA)", "Clay extruder V2 (UR5)", "Pen holder (UR5)", "Wool spinner (UR5)" };
        private static void AddPreset(InputAdder inputs) =>
            inputs.AddInteger("Preset", "Pre", "A tool that is known, instead of Name, TCP and Weight:\n" + string.Join("\n", Presets.Select((n, i) => i + " = " + n))).Set(0).Choices(Presets);

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Name", "N", "Name of the tool.").Set("Extruder");
            inputs.AddPlane("TCP", "TCP", "Tool centre point relative to the flange, as in the Robots plug-in: origin = nozzle tip, Z axis = from the tip back into the tool. The AE20 extruder of the FH Muenster: a YZ plane at -460, 3, 108. Default: a straight tool on the flange.").Set(new Plane(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis));
            inputs.AddNumber("Weight", "W", "Weight of the tool in kg. 0 = not written to the program.").Set(0.0);
            AddLoad(inputs);
            AddPreset(inputs);
        }

        private static void AddLoad(InputAdder inputs)
        {
            inputs.AddPoint("Centroid", "C", "Centre of mass relative to the flange, written to the program with the weight. The AE20 extruder of the FH Muenster: -61, -28, 113.").Set(Point3d.Origin);
            inputs.AddMesh("Mesh", "M", "Optional. The tool as a 3D model, drawn where it sits on the flange: the flange is the world XY plane at the origin, as in the Robots plug-in. Shown on the robot and used by the collision check.", Access.Twig, Requirement.MayBeMissing);
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
            Point3d centroid = Point3d.Origin;
            if (Parameters.InputCount > 3 && (!access.GetItem(3, out centroid) || !centroid.IsValid)) centroid = Point3d.Origin;
            int preset = 0; if (Parameters.InputCount > 5) access.GetItem(5, out preset);
            // a preset takes the place of name, TCP and weight
            if (preset == 1) { name = "AE20_Extruder"; tcp = new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis); weight = 46; centroid = new Point3d(-61, -28, 113); }
            if (preset >= 2 && preset < Presets.Length)
            {
                var ut = URTool.Preset(preset - 2, 0);
                access.SetItem(0, new RobotTool { Name = ut.Name, Tcp = ut.Tcp, Weight = ut.Weight, Centroid = ut.Centroid, UrPreset = preset - 2 });
                access.SetItem(1, string.Format(CultureInfo.InvariantCulture, "{0} for a UR: tip at ({1:0.#}, {2:0.#}, {3:0.#}) mm from the flange, {4:0.##} kg", ut.Name, ut.Tcp.O.X, ut.Tcp.O.Y, ut.Tcp.O.Z, ut.Weight) + (ut.Extrudes ? ", switches the extruder (digital outputs 0 and 1)" : ""));
                return;
            }
            var tool = new RobotTool { Name = string.IsNullOrWhiteSpace(name) ? "Extruder" : name.Trim(), Tcp = Frame.FromPlane(tcp), Weight = weight, Centroid = centroid };
            if (Parameters.InputCount > 4 && access.ItemArray(4, out Mesh[] meshes))
            {
                var joined = new Mesh();
                foreach (var m in meshes) if (m != null && m.IsValid) joined.Append(m);
                if (joined.Vertices.Count > 0) tool.Mesh = joined;
            }
            bool builtIn = false;
            if (tool.Mesh == null) { tool.Mesh = RobotGeometry.BuiltInTool(tool.Name); builtIn = tool.Mesh != null; }
            var abc = tool.Tcp.Abc();
            access.SetItem(0, tool);
            access.SetItem(1, string.Format(CultureInfo.InvariantCulture, "{0}: X {1:0.###}, Y {2:0.###}, Z {3:0.###}, A {4:0.###}, B {5:0.###}, C {6:0.###}, {7:0.##} kg{8}", tool.Name, tcp.OriginX, tcp.OriginY, tcp.OriginZ, abc[0], abc[1], abc[2], weight,
                builtIn ? ", 3D model that comes with the plug-in" : tool.Mesh != null ? ", with your 3D model" : ", no 3D model"));
        }
    }

    // ============================================================== cell
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4003")]
    public sealed class RobotCellComponent : Component
    {
        public RobotCellComponent() : base(Io.Name("Robot Cell", "Robot, tool and the place of the work. The Work Base is the frame the program counts from (KUKA: $BASE), usually a corner of the print bed.", Io.Robot, 0)) { }
        public RobotCellComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("RobotCellIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Robot", "R", "Robot arm. Empty = KUKA KR120 R2700-2 at the world origin.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Tool", "T", "Robot Tool. Empty = tip on the flange.", Access.Item, Requirement.MayBeMissing);
            inputs.AddPlane("Work Base", "WB", "The frame the program counts from, in the Rhino model.").Set(Plane.WorldXY);
            inputs.AddMesh("Environment", "Env", "Optional. What stands around the robot (floor, fence, walls), for the collision check of Robot Program.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("External Axes", "Ext", "Optional. A rail the robot stands on and/or a turntable the part stands on, from External Axis. Robot Program then moves them along the print.", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Cell", "C", "The robot cell, for Robot Program.");
            outputs.AddText("Info", "I", "Summary.");
        }

        protected override void Process(IDataAccess access)
        {
            // a Universal Robots arm: a UR cell (its tool as on the UR pendant, or a UR preset of Robot Tool)
            var ua = Io.Get<URArm>(access, 0);
            if (ua != null)
            {
                var rt = Io.Get<RobotTool>(access, 1);
                URTool ut;
                if (rt == null) ut = URTool.Preset(3, 0);
                else if (rt.UrPreset >= 0) ut = URTool.Preset(rt.UrPreset, 0);
                else ut = new URTool { Name = rt.Name, Tcp = rt.Tcp, Weight = rt.Weight, Centroid = rt.Centroid };
                var uc = new URCell { Model = ua.Model, Base = ua.Base, Tool = ut, StartCode = rt?.StartCode, OnCode = rt?.OnCode, OffCode = rt?.OffCode };
                if (ut.Weight > ua.Model.Payload) access.AddWarning("Too heavy", string.Format(CultureInfo.InvariantCulture, "The tool weighs {0:0.#} kg; the {1} carries {2:0.#} kg.", ut.Weight, ua.Model.Name, ua.Model.Payload));
                access.SetItem(0, uc);
                access.SetItem(1, uc + "; the part is printed where it lies in Rhino");
                return;
            }
            var arm = Io.Get<RobotArm>(access, 0) ?? RobotArm.Kr120R2700();
            var tool = Io.Get<RobotTool>(access, 1) ?? new RobotTool();
            Plane wb; if (!access.GetItem(2, out wb)) wb = Plane.WorldXY;
            var cell = new RobotCell { Arm = arm, Tool = tool, WorkBase = Frame.FromPlane(wb) };
            Mesh[] env; if (access.ItemArray(3, out env) && env != null) {
                // a mesh with broken faces is repaired, never left out without a word
                var notes = new List<string>();
                cell.Environment = RobotGeometry.Usable(env, "Environment mesh", notes);
                foreach (var note in notes) access.AddWarning("Environment", note);
            }
            object[] ext;
            if (access.ItemArray(4, out ext) && ext != null)
                foreach (var o in ext)
                {
                    if (o is ExternalAxis ax) cell.ExternalAxes.Add(ax);
                    else if (o != null) access.AddWarning("Not an external axis", "External Axes takes the output of External Axis only.");
                }
            if (cell.ExternalAxes.Count(a => a.IsRail) > 1 || cell.ExternalAxes.Count(a => !a.IsRail) > 1) { access.AddError("Too many axes", "Use at most one rail and one turntable."); return; }
            if (cell.ExternalAxes.Select(a => a.Name).Distinct().Count() != cell.ExternalAxes.Count) { access.AddError("Same name twice", "Give each external axis its own name: E1, E2 ..."); return; }
            double away = cell.WorkBase.O.DistanceTo(arm.Base.O);
            if (away > arm.Reach) access.AddWarning("Work base far away", $"The work base is {away:0} mm from the robot; its reach is {arm.Reach:0} mm.");
            CollisionModel.Warm(cell); // prepares the bodies for the collision check in the background
            access.SetItem(0, cell);
            access.SetItem(1, $"{cell}; work base {away:0} mm from the robot");
        }
    }

    // ============================================================== program
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4004")]
    public sealed class RobotProgramComponent : PreviewComponent
    {
        public RobotProgramComponent() : base(Io.Name("Robot Program", "Turns the toolpath into robot moves: the tool tip on every path point, the tool along the path's tool axis. Checks reach, axis ranges and the wrist, and writes the moves for the DXR Generator and as a KRL program.", Io.Robot, 1)) { }
        public RobotProgramComponent(IReader reader) : base(reader)
        {
            // files saved before the UR: add its inputs at the end, the wires stay
            if (Parameters.InputCount == 10) AddUR(new InputAdder(this));
        }
        protected override IIcon IconInternal => Io.Icon("RobotProgramIcon");

        private static void AddUR(InputAdder inputs)
        {
            inputs.AddNumber("Blend", "Bl", "Universal Robots only: the moves blend into each other over this many mm (at most 45 % of the neighbouring moves).").Set(1.0);
            inputs.AddText("Robot IP", "IP", "Universal Robots only: the address of the robot for Upload. The FH Muenster UR5: 192.168.1.66 (the laptop on 192.168.1.44, cable straight to the robot).").Set("192.168.1.66");
            inputs.AddBoolean("Upload", "Up", "Universal Robots only: true sends the program to the robot (port 30002); the pendant then asks to Execute. Goes back to false by itself.").Set(false);
        }
        // the robot in its own colours: base dark, arm KUKA orange, tool steel blue (as in Grasshopper 1)
        protected override int[] OwnOutputs => new[] { 9 };
        internal static readonly System.Drawing.Color BaseColor = System.Drawing.Color.FromArgb(60, 60, 64), ArmColor = System.Drawing.Color.FromArgb(240, 110, 20),
            ToolColor = System.Drawing.Color.FromArgb(90, 120, 150), AxisColor = System.Drawing.Color.FromArgb(150, 152, 158);

        /// <summary>Robot bodies (0 base, 1-6 links, 7 and on the tool) and axis models in their colours, what collides red.</summary>
        internal static void Paint(ShadedPreview.Frame f, IList<Mesh> robot, IList<Mesh> axes, ICollection<int> hitRobot = null, ICollection<int> hitAxes = null)
        {
            for (int i = 0; robot != null && i < robot.Count; i++)
                if (hitRobot != null && hitRobot.Contains(i)) f.Hit(robot[i]); else f.Mesh(robot[i], i == 0 ? BaseColor : i >= 7 ? ToolColor : ArmColor);
            for (int i = 0; axes != null && i < axes.Count; i++)
                if (hitAxes != null && hitAxes.Contains(i)) f.Hit(axes[i]); else f.Mesh(axes[i], AxisColor);
        }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path, where the part stands in the Rhino model.");
            inputs.AddGeneric("Cell", "C", "Robot Cell. Empty = KUKA KR120 R2700-2 at the world origin, tip on the flange.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Turn", "Tu", "Turns the tool about its own axis, in degrees.").Set(0.0);
            inputs.AddText("Name", "N", "Name of the program.").Set("LARGER_PRINT");
            inputs.AddNumber("Pose", "P", "Shows the arm at this place of the path: 0 = start, 1 = end.").Set(0.0);
            inputs.AddText("File", "File", "Optional: full path of the .src file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the KRL file.").Set(false);
            inputs.AddBoolean("Check Collisions", "CC", "True = check the robot, the tool, rail and turntable against each other, the part printed so far, the bed and the Environment of the cell.").Set(true);
            inputs.AddNumber("Clearance", "Cl", "Bodies closer than this (mm) are reported as a near miss.").Set(10.0);
            inputs.AddInteger("Collision Poses", "Col", "Fast mode: how many poses along the path are checked (evenly spread), plus the critical ones (travel moves, where the tool turns, the strongest tilt of every layer). -1 = every move.").Set(500);
            AddUR(inputs);
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
            outputs.AddInteger("Collision Moves", "CM", "Numbers of the moves at which something collides (as Move of Robot Simulation).", Access.Twig);
            outputs.AddMesh("Collision Bodies", "CB", "The bodies that collide, where they are at their worst collision, and a ball where they touch.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            Picture.Clear();
            var tp = Io.Path(access, 0); if (tp == null) return;
            var urCell = Io.Get<URCell>(access, 1);
            if (urCell != null) { ProcessUR(access, tp, urCell); return; }
            var cell = Io.Get<RobotCell>(access, 1) ?? new RobotCell();
            double turn, pose; string name, file; bool save;
            access.GetItem(2, out turn); access.GetItem(3, out name); access.GetItem(4, out pose); access.GetItem(5, out file); access.GetItem(6, out save);
            bool collide; double clearance; int poses;
            if (!access.GetItem(7, out collide)) collide = true;
            if (!access.GetItem(8, out clearance)) clearance = 10;
            if (!access.GetItem(9, out poses)) poses = 500;
            if (Io.Step(access, 5)) return; // progress; stop if the inputs changed meanwhile
            var r = RobotProgram.Build(tp, cell, turn, name, true);
            var path = r.PathFor(tp); // with a turntable the program has more points than the toolpath
            if (!r.Ok) access.AddWarning("Not runnable", string.Join("\n", r.Report.Where(l => l.StartsWith("NOT"))));
            foreach (var l in r.Report.Where(l => l.StartsWith("WARNING: "))) access.AddWarning("Robot Program", l.Substring(9));
            if (r.Ok && r.Singular > 0) access.AddRemark("Wrist", r.Report.FirstOrDefault(l => l.StartsWith("Wrist")) ?? "The wrist comes close to its singularity.");
            if (r.Speed.Any(v => v <= 0)) access.AddError("Speed 0", "A move has speed 0; the machine would stop there.");
            if (collide && poses != 0)
            {
                if (Io.Step(access, 50)) return; // progress; stop if the inputs changed meanwhile
                var col = r.CheckCollisions(tp, cell, new CollisionOptions { Clearance = Math.Max(0, clearance), Poses = poses });
                if (col.Hits > 0)
                {
                    access.AddWarning("Collision", string.Join("\n", col.Lines.Take(4)));
                    access.SetTwig(11, col.Moves.Select(i => r.Moves[i].Part.Pt(path.Points[i])).ToArray(), null, null);
                }
                else if (col.Checked) access.AddRemark("No collision", col.Summary);
                else access.AddWarning("Collisions not checked", col.Summary);
                access.SetTwig(12, col.Moves.ToArray(), null, null);
                access.SetTwig(13, col.Bodies.ToArray(), null, null);
            }
            else r.Report.Add("Collisions were not checked (Check Collisions is off).");
            access.SetItem(10, RobotRun.Make(r, cell, tp));
            var krl = r.Krl;
            if (save)
            {
                krl = RobotProgram.KrlForFile(r.Krl, OutputFile.Resolve(file, RobotProgram.ProgramName(name) + ".src"), out string note, out bool bad);
                if (!string.IsNullOrEmpty(note)) { if (bad) access.AddWarning("File name", note); else access.AddRemark("File name", note); }
            }
            Files.Save(access, this, 6, save, file, krl, () => RobotProgram.ProgramName(name) + ".src");
            access.SetItem(8, OutputFile.Name("DXR", name, tp, ""));
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            access.SetTwig(1, r.Extrusion.ToArray(), null, null);
            access.SetTwig(2, r.Speed.ToArray(), null, null);
            access.SetTwig(3, r.Krl.ToArray(), null, null);
            access.SetItem(4, r.Ok);
            access.SetTwig(5, r.Report.ToArray(), null, null);
            access.SetTwig(6, Enumerable.Range(0, r.Moves.Count).Where(i => !r.Moves[i].Reachable || !r.Moves[i].InLimits).Select(i => r.Moves[i].Part.Pt(path.Points[i])).ToArray(), null, null);
            int at = Math.Max(0, Math.Min(r.Moves.Count - 1, (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (r.Moves.Count - 1))));
            if (r.Moves[at].Axes != null)
            {
                var pts = cell.Arm.Skeleton(r.Moves[at].Axes, r.Moves[at].RobotBase);
                pts.Add(r.Moves[at].Part.Pt(path.Points[at]));
                access.SetItem(7, new Polyline(pts));
                var robot = RobotGeometry.Pose(cell, r.Moves[at].Axes, r.Moves[at].External) ?? new List<Mesh>();
                var axesModel = ExternalAxisGeometry.Pose(cell, r.Moves[at].External);
                var model = robot.Concat(axesModel).ToArray();
                if (model.Length > 0) access.SetTwig(9, model, null, null);
                var frame = new ShadedPreview.Frame();
                Paint(frame, robot, axesModel);
                Picture.Show(frame);
            }
        }

        private static readonly System.Drawing.Color URColor = System.Drawing.Color.FromArgb(170, 180, 190);

        /// <summary>A Universal Robots cell: URScript instead of KRL and DXR.</summary>
        private void ProcessUR(IDataAccess access, Toolpath tp, URCell cell)
        {
            double turn, pose, blend = 1; string name, file, ip = null; bool save, upload = false;
            access.GetItem(2, out turn); access.GetItem(3, out name); access.GetItem(4, out pose); access.GetItem(5, out file); access.GetItem(6, out save);
            if (Parameters.InputCount > 12) { access.GetItem(10, out blend); access.GetItem(11, out ip); access.GetItem(12, out upload); }
            if (string.IsNullOrWhiteSpace(name)) name = "LARGER_PRINT";
            cell.Ip = string.IsNullOrWhiteSpace(ip) ? "192.168.1.66" : ip.Trim();
            var prg = URProgram.Build(tp, cell, 0, Math.Max(0, blend), turn, 30, cell.StartCode ?? URProgram.AirCode, cell.OnCode ?? URProgram.OnCode, cell.OffCode ?? URProgram.OffCode, name);
            string fileName = OutputFile.Name("UR", name, tp, "");
            Files.Save(access, this, 6, save, file, prg.Script, () => fileName + ".script");
            if (upload)
            {
                try { access.AddRemark("Uploaded", URProgram.Upload(prg.Script, cell.Ip) + " The pendant now asks: Execute."); }
                catch (Exception ex) { access.AddError("Upload failed", ex.Message); }
                Files.Reset(this, 12);
            }
            if (!prg.Ok) access.AddWarning("Not runnable", string.Join("\n", prg.Report.Where(l => l.StartsWith("NOT") || l.StartsWith("TOO"))));
            access.SetTwig(3, prg.Script.ToArray(), null, null);
            access.SetItem(4, prg.Ok);
            access.SetTwig(5, prg.Report.ToArray(), null, null);
            access.SetTwig(6, prg.Unreachable.ToArray(), null, null);
            access.SetItem(8, fileName);
            access.SetItem(10, prg);
            if (prg.Joints.Count > 0)
            {
                int k = (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (prg.Joints.Count - 1));
                var arm = URDraw.Arm(cell, prg.Joints[k]);
                access.SetTwig(9, arm.ToArray(), null, null);
                var f = cell.Model.Frames(prg.Joints[k]).Select(x => cell.Base.Times(x).O).ToList();
                access.SetItem(7, new Polyline(f));
                var frame = new ShadedPreview.Frame();
                foreach (var m in arm) frame.Mesh(m, URColor);
                Picture.Show(frame);
            }
        }
    }

    // ============================================================== simulation
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4005")]
    public sealed class RobotSimulationComponent : PreviewComponent
    {
        public RobotSimulationComponent() : base(Io.Name("Robot Simulation", "Plays the robot program: the robot with its tool at any moment of the print. Time runs from 0 (start) to 1 (end) and follows the real time of the print, slower where an axis cannot turn faster. Moving the slider only places the robot: the program is not calculated again.", Io.Robot, 1)) { }
        public RobotSimulationComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("RobotSimulationIcon");
        // robot, axes and cell in their colours, what collides red (as in Grasshopper 1)
        protected override int[] OwnOutputs => new[] { 0, 8, 9, 10 };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Program", "Prg", "Program of Robot Program.");
            inputs.AddNumber("Time", "T", "Where in the print: 0 = start, 1 = end.").Set(0.0);
            inputs.AddBoolean("Show Cell", "Cell", "True = put the cell around the robot (floor, fence, glass, walls) into Environment, so it is drawn with the simulation. False = only the robot and its tool. The collision check always uses the cell.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Robot", "Rob", "The robot with its tool at that moment.", Access.Twig);
            outputs.AddNumber("Axes", "A", "Axis angles A1 to A6 in degrees.", Access.Twig);
            outputs.AddPlane("TCP", "TCP", "The tool tip: origin on the path, Z along the tool axis.");
            outputs.AddInteger("Move", "M", "Number of the move the robot is on.");
            outputs.AddNumber("Seconds", "s", "Time since the start of the print.");
            outputs.AddText("Info", "I", "Time, move and axis angles.");
            outputs.AddNumber("External", "E", "Values of the external axes at that moment (rail in mm, turntable in degrees).", Access.Twig);
            outputs.AddPolyline("Printed", "Pr", "The path printed up to that moment, where it is in the room: it turns with a turntable.");
            outputs.AddMesh("Axes Model", "AM", "The rail with its carriage and the turntable at that moment.", Access.Twig);
            outputs.AddMesh("Environment", "Env", "What stands around the robot (the Environment of the cell), when Show Cell is on.", Access.Twig);
            outputs.AddMesh("Colliding", "Col", "What collides at this moment: robot links, tool, rail, table, environment, and a ball where it touches. Empty = clear.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            Picture.Clear();
            var urp = Io.Get<URProgram>(access, 0);
            if (urp != null) { ProcessUR(access, urp); return; }
            var run = Io.Get<RobotRun>(access, 0);
            if (run == null || run.Points.Count == 0) { access.AddError("No program", "Connect Program of Robot Program."); return; }
            double time; access.GetItem(1, out time);
            time = Math.Max(0, Math.Min(1, time));
            double seconds = time * run.Duration;
            var axes = run.AxesAt(seconds, out int move, out double share);
            access.SetItem(3, move); access.SetItem(4, seconds);
            if (axes == null) { access.AddWarning("Not reachable", $"The robot cannot get to move {move}."); return; }
            var arm = run.Cell.Arm;
            var ext = run.ExternalAt(seconds);
            var model = RobotGeometry.Pose(run.Cell, axes, ext);
            if (model != null) access.SetTwig(0, model.ToArray(), null, null);
            else access.AddRemark("No 3D model", "The robot has no 3D model: see the Info of Robot.");
            access.SetTwig(6, ext ?? new double[0], null, null);
            var printed = run.PrintedAt(seconds); if (printed.Count > 1) access.SetItem(7, printed);
            access.SetTwig(8, ExternalAxisGeometry.Pose(run.Cell, ext).ToArray(), null, null);
            bool showCell; if (!access.GetItem(2, out showCell)) showCell = false;
            if (showCell && run.Cell.Environment != null) access.SetTwig(9, run.Cell.Environment.ToArray(), null, null);
            string collision = "";
            var frame = new ShadedPreview.Frame();
            HashSet<int> hitRobot = new HashSet<int>(), hitAxes = new HashSet<int>(), hitEnv = new HashSet<int>();
            if (run.Collider != null)
            {
                PoseCollision pc = null;
                try { pc = run.Collider.Check(axes, ext, move); } catch { }
                if (pc != null && pc.Any)
                {
                    var colliding = new List<Mesh>();
                    foreach (int i in pc.Robot) if (model != null && i < model.Count) colliding.Add(model[i]);
                    var axesModel = ExternalAxisGeometry.Pose(run.Cell, ext);
                    foreach (int i in pc.Axes) if (i < axesModel.Count) colliding.Add(axesModel[i]);
                    var env = run.Collider.Cell.Environment;
                    if (env != null) foreach (int i in pc.Environment) if (i < env.Count) colliding.Add(env[i]);
                    foreach (var w in pc.Where) { var ball = Mesh.CreateFromSphere(new Sphere(w, 12), 12, 8); if (ball != null) { colliding.Add(ball); frame.Hit(ball); } }
                    hitRobot.UnionWith(pc.Robot); hitAxes.UnionWith(pc.Axes); hitEnv.UnionWith(pc.Environment);
                    if (!showCell && env != null) foreach (int i in pc.Environment) if (i < env.Count) frame.Hit(env[i]);
                    access.SetTwig(10, colliding.ToArray(), null, null);
                    collision = " - COLLISION: " + string.Join(", ", pc.Robot.OrderBy(i => i).Select(CollisionModel.RobotName));
                    access.AddWarning("Collision", "Collision at this moment: see Colliding.");
                }
            }
            RobotProgramComponent.Paint(frame, model, ExternalAxisGeometry.Pose(run.Cell, ext), hitRobot, hitAxes);
            if (showCell && run.Cell.Environment != null)
                for (int i = 0; i < run.Cell.Environment.Count; i++)
                    if (hitEnv.Contains(i)) frame.Hit(run.Cell.Environment[i]); else frame.Mesh(run.Cell.Environment[i], MachineLook.Cell, 0.55);
            Picture.Show(frame);
            Frame tip = run.Cell.RobotBaseAt(ext).Times(arm.Forward(axes)).Times(Frame.World.TurnedAboutZ(Math.PI)).Times(run.Cell.Tool.Tcp);
            bool inRange = true;
            for (int k = 0; k < 6; k++) if (axes[k] < arm.MinDeg[k] - 1e-6 || axes[k] > arm.MaxDeg[k] + 1e-6) inRange = false;
            if (!inRange) access.AddWarning("Axis range", "At this moment an axis is outside its range.");
            access.SetTwig(1, axes, null, null);
            access.SetItem(2, tip.ToPlane());
            var ci = CultureInfo.InvariantCulture;
            access.SetItem(5, TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", ci) + " of " + TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci) + $", move {move}, " + string.Join(", ", axes.Select((v, k) => "A" + (k + 1) + " " + v.ToString("0.0", ci)))
                + (ext == null ? "" : string.Concat(ext.Select((v, k) => ", " + run.Cell.ExternalAxes[k].Name + " " + v.ToString("0.0", ci)))) + (inRange ? "" : " - OUTSIDE THE AXIS RANGE") + collision);
        }

        private void ProcessUR(IDataAccess access, URProgram prg)
        {
            if (prg.Joints.Count == 0) { access.AddError("No program", "The UR program has no moves."); return; }
            double time; access.GetItem(1, out time);
            time = Math.Max(0, Math.Min(1, time));
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
            var arm = URDraw.Arm(cell, q);
            var printed = new Polyline();
            for (int i = 0; i <= k && i < prg.PathPoints.Count; i++) if (prg.Printing[i] || (i + 1 < prg.Printing.Count && prg.Printing[i + 1])) printed.Add(prg.PathPoints[i]);
            var tcp = cell.Base.Times(cell.Model.Flange(q)).Times(cell.Tool.Tcp).ToPlane();
            var ci = CultureInfo.InvariantCulture;
            access.SetTwig(0, arm.ToArray(), null, null);
            access.SetTwig(1, q.Select(v => Math.Round(v * 180 / Math.PI, 2)).ToArray(), null, null);
            access.SetItem(2, tcp);
            access.SetItem(3, k + 1);
            access.SetItem(4, at);
            access.SetItem(5, string.Format(ci, "{0}: {1:0.0} of {2:0.0} min, move {3} of {4}, axes ", cell.Model.Name, at / 60, total / 60, k + 1, prg.Joints.Count) + string.Join(" / ", q.Select(v => (v * 180 / Math.PI).ToString("0", ci))));
            if (printed.Count > 1) access.SetItem(7, printed);
            var frame = new ShadedPreview.Frame();
            foreach (var m in arm) frame.Mesh(m, System.Drawing.Color.FromArgb(170, 180, 190));
            Picture.Show(frame);
        }
    }

    // ============================================================== external axis
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4007")]
    public sealed class ExternalAxisComponent : Component
    {
        public ExternalAxisComponent() : base(Io.Name("External Axis", "A rail (linear track) the robot stands on, or a turntable the part stands on. Connect it to External Axes of Robot Cell. Robot Program then moves it along the print: the rail keeps the robot beside the point being printed, the turntable turns the part so that point faces the robot.", Io.Robot, 0)) { }
        public ExternalAxisComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("RobotExternalAxisIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Kind", "K", "0 = rail (the robot moves along it), 1 = turntable (the part turns on it).").Set(0).Choices("Rail", "Turntable");
            inputs.AddPlane("Place", "P", "Rail: the robot moves along the X axis of this plane; at 0 it stands where its Base says. Turntable: origin = centre of the table, Z axis = the axis it turns about, X axis = where 0 degrees is.").Set(Plane.WorldXY);
            inputs.AddNumber("Min", "Min", "Lowest value: mm for a rail, degrees for a turntable. Min and Max both 0 = rail 0 to 3000 mm, turntable -360 to 360 degrees.").Set(0.0);
            inputs.AddNumber("Max", "Max", "Highest value: mm for a rail, degrees for a turntable.").Set(0.0);
            inputs.AddNumber("Max Speed", "V", "Top speed: mm/s for a rail, degrees/s for a turntable. 0 = rail 1000 mm/s, turntable 90 degrees/s.").Set(0.0);
            inputs.AddText("Name", "N", "Name of the axis in the KUKA program: E1 ... E6. Empty = E1 for a rail, E2 for a turntable.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Endless", "End", "Turntable only: it turns on without end (no range).").Set(false);
            inputs.AddNumber("Offset", "Off", "Rail: the robot stands this far ahead of the point being printed, along the rail, in mm. Turntable: the point being printed is turned this many degrees away from the robot side.").Set(0.0);
            inputs.AddNumber("Smoothing", "S", "How much path (mm) the axis averages over: more = calmer, the arm does more of the work.").Set(300.0);
            inputs.AddBoolean("Coupled", "Cpl", "Turntable only: KRL targets counted in a base that turns with the table (KUKA coupled base). Off = counted in the fixed base.").Set(false);
            inputs.AddNumber("Size", "Sz", "Size of the drawing: width of the rail, radius of the table, in mm. 0 = 600 mm.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("External Axis", "Ext", "The axis: connect to External Axes of Robot Cell.");
            outputs.AddText("Info", "I", "What the axis is and its range.");
            outputs.AddMesh("Model", "M", "The axis at 0, as a simple 3D model.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            int kind; Plane place; double min, max, speed, offset, smooth, size; string name; bool endless, coupled;
            access.GetItem(0, out kind);
            if (!access.GetItem(1, out place)) place = Plane.WorldXY;
            access.GetItem(2, out min); access.GetItem(3, out max); access.GetItem(4, out speed); access.GetItem(5, out name);
            access.GetItem(6, out endless); access.GetItem(7, out offset);
            if (!access.GetItem(8, out smooth)) smooth = 300;
            access.GetItem(9, out coupled); access.GetItem(10, out size);
            var ax = ExternalAxisMaker.Make(kind, place, min, max, speed, name, endless, offset, smooth, coupled, size, out string error);
            if (ax == null) { access.AddError("Invalid axis", error); return; }
            access.SetItem(0, ax);
            access.SetItem(1, ax.ToString());
            access.SetTwig(2, ExternalAxisGeometry.Pose(new RobotCell { ExternalAxes = new List<ExternalAxis> { ax } }, null).ToArray(), null, null);
        }
    }

    // ============================================================== cell geometry
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d4006")]
    public sealed class CellGeometryComponent : PreviewComponent
    {
        public CellGeometryComponent() : base(Io.Name("Cell Geometry", "The print cell in 3D and where the robot stands in it, in the coordinates of the print bed. The cell of the FH Muenster is built in; your own cells are .3dm files in Documents/LARGER/Cells, one part per layer: Bed, Robot Base (a point and a line along the robot's X axis), Work Base (optional) and any other layer for the collision check. Export writes the chosen cell there as a start.", Io.Robot, 0)) { }
        public CellGeometryComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("RobotCellGeometryIcon");
        // the cell is drawn here, without the walls unless they are asked for (as in Grasshopper 1)
        protected override int[] OwnOutputs => new[] { 0, 1 };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddBoolean("Walls", "W", "Show the walls of the room. They hide the view from outside, so they are off by default; the collision check knows them either way.").Set(false);
            inputs.AddInteger("Cell", "C", "0 = FH Muenster, then your own cells from Documents/LARGER/Cells in the order of their file names.").Set(0);
            inputs.AddText("File", "File", "Optional. A cell as a .3dm file anywhere on the computer, used instead of the list.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Export", "Exp", "True writes the chosen cell as a .3dm into Documents/LARGER/Cells: open it in Rhino, change it and save it under the name of your cell.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Environment", "Env", "Everything but the bed, the walls too, for the collision check: connect to Environment of Robot Cell.", Access.Twig);
            outputs.AddMesh("Bed", "Bed", "The print bed. Not part of the collision check: the nozzle runs right above it.");
            outputs.AddPlane("Robot Base", "RB", "Where the robot stands: connect to Base of Robot.");
            outputs.AddPlane("Work Base", "WB", "The corner of the bed the print counts from: connect to Work Base of Robot Cell.");
        }

        protected override void Process(IDataAccess access)
        {
            Picture.Clear();
            bool walls, export; int index; string file;
            access.GetItem(0, out walls); access.GetItem(1, out index); access.GetItem(2, out file); access.GetItem(3, out export);
            CellModel cell;
            if (!string.IsNullOrWhiteSpace(file))
            {
                string path = file.Trim().Trim('"');
                if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                cell = CellLibrary.Load(path, out string error);
                if (cell == null) { access.AddError("Cell not read", error); return; }
            }
            else
            {
                var log = new List<string>();
                var all = CellLibrary.All(log);
                foreach (var l in log) access.AddWarning("Cell file left out", l);
                if (all.Count == 0) { access.AddError("No cell model", "The cell model could not be read from the plug-in."); return; }
                if (index < 0 || index >= all.Count) { access.AddWarning("Unknown cell", $"There are {all.Count} cells (0 to {all.Count - 1}); using the first."); index = 0; }
                cell = all[index];
            }
            if (export)
            {
                try { access.AddRemark("Written", CellLibrary.Export(cell)); }
                catch (Exception ex) { access.AddError("Could not write the cell", ex.Message); }
                Files.Reset(this, 3);
            }
            var frame = new ShadedPreview.Frame();
            foreach (var kv in cell.Parts)
            {
                string n = kv.Key.ToLowerInvariant();
                if (n == "walls" && !walls) continue;
                if (n == "bed") frame.Mesh(kv.Value, System.Drawing.Color.FromArgb(70, 72, 78));
                else frame.Mesh(kv.Value, MachineLook.Cell, n == "glass" ? 0.75 : n == "walls" ? 0.6 : 0);
            }
            Picture.Show(frame);
            access.SetTwig(0, CellLibrary.Environment(cell, true).ToArray(), null, null); // the collision check knows the walls even when they are not shown
            if (cell.Parts.TryGetValue("Bed", out Mesh bed)) access.SetItem(1, bed);
            access.SetItem(2, cell.RobotBase);
            access.SetItem(3, cell.WorkBase);
        }
    }
}
