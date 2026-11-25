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
    internal static class URGeo
    {
        public static T Unwrap<T>(IGH_DataAccess da, int i) where T : class
        {
            IGH_Goo g = null;
            if (!da.GetData(i, ref g) || g == null) return null;
            if (g is GH_ObjectWrapper w) return w.Value as T;
            return g.ScriptVariable() as T;
        }

        private static Mesh Rod(Point3d a, Point3d b, double r)
        {
            var axis = b - a; double len = axis.Length;
            if (len < 1e-6) return null;
            var pl = new Plane(a, axis);
            return Mesh.CreateFromCylinder(new Cylinder(new Circle(pl, r), len), 1, 20);
        }

        /// <summary>The arm drawn plainly: a base, a drum at every joint and a rod for every link, then the tool.</summary>
        public static List<Mesh> Arm(URCell cell, double[] q)
        {
            var res = new List<Mesh>();
            var m = cell.Model; double r = Math.Max(30, Math.Abs(m.A2) * 0.11);
            var f = m.Frames(q).Select(x => cell.Base.Times(x)).ToArray();
            var b0 = Rod(f[0].O, f[0].O + f[0].Z * m.D1 * 0.6, r * 1.3); if (b0 != null) res.Add(b0);
            for (int i = 0; i < 6; i++)
            {
                // the drum of joint i turns about the Z axis of frame i
                var c = f[i].O + f[i].Z * (i == 0 ? m.D1 : 0);
                var d = Rod(c - f[i].Z * r, c + f[i].Z * r, r); if (d != null) res.Add(d);
                var l = Rod(f[i].O, f[i + 1].O, r * 0.8); if (l != null) res.Add(l);
            }
            // the tool: from the flange to its tip
            var tip = f[6].Times(cell.Tool.Tcp);
            var bend = f[6].O + f[6].Z * Math.Max(20, cell.Tool.Tcp.O.Z);
            var t1 = Rod(f[6].O, bend, r * 0.6); if (t1 != null) res.Add(t1);
            var t2 = Rod(bend, tip.O, r * 0.35); if (t2 != null) res.Add(t2);
            return res;
        }
    }

    // ============================================================== UR Cell
    public class URCellComponent : RobotComponent
    {
        public URCellComponent()
            : base("UR Cell", "UR",
                  "A Universal Robots arm with its tool, where it stands and what it prints on: for UR Program. The tools are those of the FH Muenster UR5 (clay extruder V2, pen holder, wool spinner) or your own. The arm stands where Base is (default: its foot at the Rhino origin, X forward as on the pendant); the part is printed where it lies in Rhino, on top of Plate.") { }

        public override Guid ComponentGuid => new Guid("A4C17E52-3B9D-4E80-8F61-2D5C7B3E9A01");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPathIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Model", "M", string.Join("\n", URModel.All.Select((m, i) => i + " = " + m.Name)), GH_ParamAccess.item, 0);
            p.AddPlaneParameter("Base", "B", "Where the robot base is, as on the pendant (Base feature): origin at the foot, X and Y as the robot's own. Default: the Rhino origin", GH_ParamAccess.item, Plane.WorldXY);
            p.AddIntegerParameter("Tool", "T", string.Join("\n", URTool.Names.Select((n, i) => i + " = " + n)), GH_ParamAccess.item, 0);
            p.AddNumberParameter("Tool Length", "L", "Pen holder and own tool: from the flange to the tip in mm (pens: brush 194, marker 197, pencil 179). Wool spinner: 0 = 97 mm", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Plate", "Pl", "Thickness of the plate the print lies on, in mm (baking tray 7, boards 10, 12, 18, 24): the path is lifted by it", GH_ParamAccess.item, 18.0);
            p.AddTextParameter("IP", "IP", "Address of the robot for Upload (the laptop needs a fixed address in the same net, e.g. 192.168.1.44)", GH_ParamAccess.item, "192.168.1.66");
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Choice, Label = "Model", Input = 0, Items = URModel.All.Select(m => m.Name).ToArray() });
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Choice, Label = "Tool", Input = 2, Items = URTool.Names });
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Plate", Input = 4, Unit = "mm", Decimals = 0, Min = 0, Max = 40, Presets = new double[] { 0, 7, 10, 12, 18, 24 } });
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Cell", "C", "Robot, tool and plate for UR Program", GH_ParamAccess.item);
            p.AddMeshParameter("Robot", "R", "The arm at its start pose (elbow up, tool down)", GH_ParamAccess.list);
            p.AddPlaneParameter("Tool Tip", "TCP", "The tool tip at the start pose: Z = where the tool points", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Robot, reach, payload and tool", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int model = 0, tool = 0; Plane b = Plane.WorldXY; double len = 0, plate = 18; string ip = "192.168.1.66";
            da.GetData(0, ref model); da.GetData(1, ref b); da.GetData(2, ref tool); da.GetData(3, ref len); da.GetData(4, ref plate); da.GetData(5, ref ip);
            model = Math.Max(0, Math.Min(URModel.All.Length - 1, model)); tool = Math.Max(0, Math.Min(3, tool));
            var cell = new URCell { Model = URModel.All[model], Tool = URTool.Preset(tool, len), Base = Frame.FromPlane(b), Plate = Math.Max(0, plate), Ip = ip };
            if (tool == 3 && len <= 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Own tool: give its Tool Length (flange to tip, mm).");
            var home = URModel.Home;
            var tip = cell.Base.Times(cell.Model.Flange(home)).Times(cell.Tool.Tcp);
            Message = cell.Model.Name + " · " + cell.Tool.Name;
            da.SetData(0, new GH_ObjectWrapper(cell));
            da.SetDataList(1, URGeo.Arm(cell, home));
            da.SetData(2, tip.ToPlane());
            da.SetDataList(3, new[]
            {
                Inv.F($"{cell.Model.Name}: reach about {cell.Model.Reach:0} mm, payload {cell.Model.Payload:0.#} kg"),
                Inv.F($"Tool {cell.Tool.Name}: {cell.Tool.Weight:0.##} kg, tip at ({cell.Tool.Tcp.O.X:0.#}, {cell.Tool.Tcp.O.Y:0.#}, {cell.Tool.Tcp.O.Z:0.#}) mm from the flange") + (cell.Tool.Extrudes ? ", switches the extruder" : ""),
                Inv.F($"Plate {cell.Plate:0} mm, robot at {cell.Ip}"),
            });
        }
    }

    // ============================================================== UR Program
    public class URProgramComponent : RobotComponent
    {
        public URProgramComponent()
            : base("UR Program", "URProg",
                  "Turns a print path into a URScript program for the UR Cell: every point a movel at the speed of the path, blended over Blend; the tool points along the layer's tool axis (down on flat layers). For the clay extruder the extruder is switched on where printing starts and off where it stops (default: digital outputs 0 and 1 as on the FH Muenster UR5, with a question for the compressed air first). Checks that the arm reaches every point. Save writes a .script file, Upload sends it to the robot; the pendant then asks Execute.") { }

        public override Guid ComponentGuid => new Guid("C2E95B16-7A43-4D2F-B8E0-5F1A6D9C3B72");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPathIcon.png", this);

        public const string AirCode = "popup(\"Bitte kontrolliere, ob die Druckluft eingeschaltet ist!\", \"Druckluft an?\", warning=False, error=False, blocking=True)";
        public const string OnCode = "set_digital_out(1,False)\nset_digital_out(0,True)";
        public const string OffCode = "set_digital_out(0,False)\nset_digital_out(1,True)";

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path (Start and End, Continuous Path, Easy Slicer, Surface Pattern ...)", GH_ParamAccess.item);
            p.AddGenericParameter("Cell", "C", "From UR Cell", GH_ParamAccess.item);
            p.AddNumberParameter("Speed", "V", "Speed in mm/s. 0 = the speed of the path (Print Settings, Min Layer Time)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Blend", "Bl", "Blend radius in mm: the robot rounds every point by this much instead of stopping there (the zone of the Robots plug-in). 0 = it stops at every point", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Turn", "Tu", "Turns the tool about its own axis, in degrees: when the wrist runs out of range or the hose is in the way (180 = the old 'rotate planes')", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Approach", "Ap", "Height in mm above the first and the last point for the way in and out", GH_ParamAccess.item, 30.0);
            p.AddTextParameter("Start Code", "Start", "URScript run before the print (one command per line)", GH_ParamAccess.item, AirCode);
            p.AddTextParameter("Extruder On", "On", "URScript that starts the extruder", GH_ParamAccess.item, OnCode);
            p.AddTextParameter("Extruder Off", "Off", "URScript that stops it", GH_ParamAccess.item, OffCode);
            p.AddTextParameter("Name", "N", "Optional: name of the program (and the file); e.g. File Name of Robot Program", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the .script file to the Desktop (for a USB stick: File > Load program on the pendant)", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Upload", "Up", "True = send the program to the robot at the IP of UR Cell; the pendant asks Execute", GH_ParamAccess.item, false);
            for (int i = 6; i <= 9; i++) p[i].Optional = true;
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Speed", Input = 2, Unit = "mm/s", Decimals = 0, Min = 0, Max = 250, Presets = new double[] { 0, 15, 20, 30, 50, 100 }, ZeroText = () => "as the path" });
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Blend", Input = 3, Unit = "mm", Decimals = 1, Min = 0, Max = 10, Presets = new double[] { 0, 0.5, 1, 2, 5 } });
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Turn", Input = 4, Unit = "°", Decimals = 0, Min = -180, Max = 180, Presets = new double[] { 0, 90, 180, -90 } });
            rows.Add(Button(10, "Save script"));
            rows.Add(Button(11, "Upload to robot"));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Program", "P", "For UR Simulation", GH_ParamAccess.item);
            p.AddTextParameter("Script", "S", "The URScript program", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Reach, payload, time, extruder", GH_ParamAccess.list);
            p.AddBooleanParameter("OK", "OK", "True = the arm reaches every point with this tool", GH_ParamAccess.item);
            p.AddPointParameter("Unreachable", "U", "Points the arm does not get to", GH_ParamAccess.list);
            p.AddTextParameter("File Name", "F", "Name for the file: UR, date and time, weight and minutes", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            var cell = URGeo.Unwrap<URCell>(da, 1);
            if (tp == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Toolpath."); return; }
            if (cell == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect UR Cell."); return; }
            double speed = 0, blend = 1, turn = 0, approach = 30; string start = AirCode, on = OnCode, off = OffCode, name = null; bool save = false, upload = false;
            da.GetData(2, ref speed); da.GetData(3, ref blend); da.GetData(4, ref turn); da.GetData(5, ref approach);
            da.GetData(6, ref start); da.GetData(7, ref on); da.GetData(8, ref off); da.GetData(9, ref name); da.GetData(10, ref save); da.GetData(11, ref upload);

            var prg = URProgram.Build(tp, cell, Math.Max(0, speed), Math.Max(0, blend), turn, Math.Max(0, approach), start, on, off, string.IsNullOrWhiteSpace(name) ? "LARGER" : name);
            string fileName = OutputFile.Name("UR", string.IsNullOrWhiteSpace(name) ? cell.Tool.Name : name, tp, "");
            if (!prg.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join(" ", prg.Report.Where(l => l.StartsWith("NOT") || l.StartsWith("TOO"))));
            if (save)
            {
                try { var w = OutputFile.Write(null, fileName + ".script", prg.Script); prg.Report.Add("Saved " + w); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + w); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            if (upload)
            {
                try { var msg = URProgram.Upload(prg.Script, cell.Ip); prg.Report.Add(msg + " The pendant now asks: Execute."); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, msg); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Upload failed: " + ex.Message); }
            }
            Message = Inv.F($"{prg.Targets.Count} moves, {prg.Minutes:0} min") + (prg.Ok ? "" : " · CHECK");
            da.SetData(0, new GH_ObjectWrapper(prg));
            da.SetDataList(1, prg.Script);
            da.SetDataList(2, prg.Report);
            da.SetData(3, prg.Ok);
            da.SetDataList(4, prg.Unreachable);
            da.SetData(5, fileName);
        }
    }

    // ============================================================== UR Simulation
    public class URSimulationComponent : RobotComponent
    {
        public URSimulationComponent()
            : base("UR Simulation", "URSim",
                  "Plays a UR Program: the arm with its tool at any moment of the print and the path printed up to then. Time runs from 0 (start) to 1 (end) by the real time of the print; Play runs it by itself. The poses are worked out here as the controller would from the start pose; on the robot, run the first layer slowly and watch.") { }

        public override Guid ComponentGuid => new Guid("E7A3C9D4-1F62-4B85-9A07-3C8E2D5F6B14");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPathIcon.png", this);
        private readonly LARGERslicer.Components.PlayClock _clock = new LARGERslicer.Components.PlayClock();

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Program", "P", "From UR Program", GH_ParamAccess.item);
            p.AddNumberParameter("Time", "T", "Where in the print: 0 = start, 1 = end", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Play", "Play", "True = runs by itself", GH_ParamAccess.item, false);
            p.AddNumberParameter("Film Length", "Len", "How long one run takes on the screen, in seconds", GH_ParamAccess.item, 30.0);
        }

        protected override void Options(List<LARGERslicer.Components.OptionRow> rows)
        {
            rows.Add(Toggle(2, "Play", "■  Stop", "▶  Play"));
            rows.Add(new LARGERslicer.Components.OptionRow { Kind = LARGERslicer.Components.RowKind.Number, Label = "Film Length (s)", Input = 3, Presets = LARGERslicer.Components.PlayClock.Lengths, Decimals = 0 });
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Robot", "R", "The arm with its tool at that moment", GH_ParamAccess.list);
            p.AddNumberParameter("Axes", "A", "Joint angles in degrees (base, shoulder, elbow, wrist 1, 2, 3)", GH_ParamAccess.list);
            p.AddPlaneParameter("TCP", "TCP", "The tool tip at that moment", GH_ParamAccess.item);
            p.AddCurveParameter("Printed", "Pr", "The path printed up to that moment", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Time, move and joint angles", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var prg = URGeo.Unwrap<URProgram>(da, 0);
            if (prg == null || prg.Joints.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect UR Program."); return; }
            double time = 0, film = 30; bool play = false;
            da.GetData(1, ref time); da.GetData(2, ref play); da.GetData(3, ref film);
            time = _clock.Position(this, play, time, film);
            double total = prg.Seconds[prg.Seconds.Count - 1], at = Math.Max(0, Math.Min(1, time)) * total;
            int k = prg.Seconds.BinarySearch(at); if (k < 0) k = Math.Max(0, ~k - 1);
            k = Math.Min(k, prg.Joints.Count - 1);
            var q = prg.Joints[k];
            if (k + 1 < prg.Joints.Count && prg.Seconds[k + 1] > prg.Seconds[k])
            {
                double u = (at - prg.Seconds[k]) / (prg.Seconds[k + 1] - prg.Seconds[k]);
                q = q.Select((v, j) => v + (prg.Joints[k + 1][j] - v) * u).ToArray();
            }
            var cell = prg.Cell;
            da.SetDataList(0, URGeo.Arm(cell, q));
            da.SetDataList(1, q.Select(v => Math.Round(v * 180 / Math.PI, 2)));
            da.SetData(2, cell.Base.Times(cell.Model.Flange(q)).Times(cell.Tool.Tcp).ToPlane());
            var printed = new List<Point3d>();
            for (int i = 0; i <= k && i < prg.PathPoints.Count; i++) if (prg.Printing[i] || (i + 1 < prg.Printing.Count && prg.Printing[i + 1])) printed.Add(prg.PathPoints[i]);
            if (printed.Count > 1) da.SetData(3, new PolylineCurve(printed));
            da.SetData(4, Inv.F($"{at / 60:0.0} of {total / 60:0.0} min, move {k + 1} of {prg.Joints.Count}, axes ") + string.Join(" / ", q.Select(v => (v * 180 / Math.PI).ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
            Message = Inv.F($"{100 * time:0} %");
            if (_clock.Playing) Message = "> " + Message;
        }
    }
}
