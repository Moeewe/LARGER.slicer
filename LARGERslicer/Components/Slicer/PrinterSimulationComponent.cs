using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components
{
    /// <summary>
    /// The clock of Play, shared by Robot Simulation and Printer Simulation: it runs from where
    /// the slider stands, starts again at the end, and asks Grasshopper for the next picture
    /// about 30 times a second. Moving the slider or changing the film length while it plays
    /// carries on from where it is.
    /// </summary>
    internal sealed class PlayClock
    {
        /// <summary>Milliseconds between two pictures.</summary>
        public const int Interval = 33;
        private bool _playing;
        private DateTime _since;
        private double _from, _slider = double.NaN, _film = double.NaN, _last;

        /// <summary>Film lengths offered on the components (seconds for one run of the whole print).</summary>
        public static readonly double[] Lengths = { 5, 10, 20, 30, 60, 120, 300 };

        public bool Playing => _playing;

        /// <summary>Where the film stands now (0..1): the slider value when not playing.</summary>
        public double Position(GH_Component owner, bool play, double slider, double filmSeconds)
        {
            slider = Math.Max(0, Math.Min(1, slider));
            filmSeconds = Math.Max(1.0, filmSeconds);
            if (!play) { _playing = false; _slider = slider; _film = filmSeconds; _last = slider; return slider; }
            var now = DateTime.UtcNow;
            if (!_playing || Math.Abs(slider - _slider) > 1e-9) { _from = !_playing && slider >= 1 ? 0 : slider; _since = now; }
            else if (Math.Abs(filmSeconds - _film) > 1e-9) { _from = _last; _since = now; } // another speed: on from here
            _playing = true; _slider = slider; _film = filmSeconds;
            double p = _from + (now - _since).TotalSeconds / filmSeconds;
            p -= Math.Floor(p); // start again at the end
            _last = p;
            // the next picture: expire this component in a solution of its own, a moment from now
            var doc = owner.OnPingDocument();
            if (doc != null && !owner.Locked)
                doc.ScheduleSolution(Interval, d => { if (owner.OnPingDocument() == d && !owner.Locked) owner.ExpireSolution(false); });
            return p;
        }
    }

    /// <summary>
    /// What a machine component draws in the viewport: meshes, each with a matte material of its
    /// colour (MachineLook), red where something collides. Robot Program, Robot Simulation,
    /// Machine, Machine G-code and Printer Simulation all draw through this one helper.
    /// </summary>
    internal sealed class MachinePreview
    {
        private readonly List<KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>> _items = new List<KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>>();

        public void Clear() => _items.Clear();
        public int Count => _items.Count;

        public void Add(Mesh m, Color c, double transparency = 0) { if (m != null) _items.Add(new KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>(m, MachineLook.Material(c, transparency))); }
        public void Add(Mesh m, Rhino.Display.DisplayMaterial mat) { if (m != null && mat != null) _items.Add(new KeyValuePair<Mesh, Rhino.Display.DisplayMaterial>(m, mat)); }
        public void Hit(Mesh m) => Add(m, MachineLook.Hit);

        public BoundingBox Box(BoundingBox b)
        {
            foreach (var kv in _items) b.Union(kv.Key.GetBoundingBox(false));
            return b;
        }

        public void Draw(IGH_PreviewArgs args)
        {
            foreach (var kv in _items) args.Display.DrawMeshShaded(kv.Key, kv.Value);
        }
    }
}

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Plays a print on a G-code machine: the machine's 3D model with its gantry, beam and head
    /// (or bed) moved so the nozzle stands on the path, the part printed so far, and what
    /// collides drawn red. Connect Simulation of Machine G-code (as Robot Simulation takes the
    /// Program of Robot Program), or a machine and a toolpath.
    /// </summary>
    public class PrinterSimulationComponent : SlicerComponent
    {
        public PrinterSimulationComponent()
            : base("Printer Simulation", "Simulate",
                  "Plays the print on the machine, as Robot Simulation plays the robot: the Ginger One (or your own machine with a 3D model) with its gantry, beam and head moved so the nozzle tip stands on the path, and the part printed up to that moment. Connect Simulation of Machine G-code (one wire), or a Machine and a Toolpath. Pose runs from 0 (start) to 1 (end) and follows the real print time of the G-code. Move the slider, or click Play and it runs by itself; Film Length sets how many seconds one run takes on the screen. The head, the beam and the frame are checked against the printed part and the bed: what collides turns red. Your own machine moves when its .3dm has the layers Frame, Bed, Z, Y, X and a point on layer TCP at the nozzle tip (see docs/OWN_MACHINES.md); a bed slinger moves its bed.") { }

        public override Guid ComponentGuid => new Guid("3E7B2C91-4A6D-4F18-9B05-C8D1E2F47A36");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPrinterSimulationIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Machine", "M", "The machine, from the Machine component, or Simulation of Machine G-code (then Toolpath may stay empty). Empty = Ginger One", GH_ParamAccess.item);
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End (in the coordinates of the machine: 0, 0, 0 is where the G-code counts from), or Simulation of Machine G-code", GH_ParamAccess.item);
            p.AddNumberParameter("Pose", "P", "Where in the print: 0 = start, 1 = end (by print time)", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Play", "Play", "True = the simulation runs by itself, from where Pose stands, and starts again at the end. False = it stands where Pose says", GH_ParamAccess.item, false);
            p.AddNumberParameter("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds. A print of an hour in 30 seconds runs 120 times faster than the machine", GH_ParamAccess.item, 20.0);
            p.AddBooleanParameter("Check Collisions", "CC", "True = check the head and the beam against the printed part and the bed, and the head against the frame: at this moment (drawn red) and along the whole print (Collisions)", GH_ParamAccess.item, true);
            p[0].Optional = true; p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 2, "Pose", 0m, 1m, 0m, 3);
            AddToggle(doc, 3, "Play", false, "■  Stop", "▶  Play");
            AddSlider(doc, 4, "Film Length (s)", 5m, 300m, 20m, 0);
            AddToggle(doc, 5, "Check collisions", true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Model", "3D", "The machine at that moment: frame standing, gantry (Z), beam (Y) and head (X) moved to the nozzle position (a bed slinger moves its bed)", GH_ParamAccess.list);
            p.AddCurveParameter("Printed", "Pr", "The path printed up to that moment (on a bed slinger: where the bed has carried it)", GH_ParamAccess.item);
            p.AddPointParameter("Tip", "TCP", "The nozzle tip at that moment", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Time, point, layer, X Y Z, and what collides", GH_ParamAccess.item);
            p.AddNumberParameter("Seconds", "s", "Time since the start of the print, from the feedrates of the G-code", GH_ParamAccess.item);
            p.AddMeshParameter("Colliding", "Col", "What collides at this moment (drawn red): head, beam, pieces of the frame, and a ball where it touches. Empty = clear", GH_ParamAccess.list);
            p.AddTextParameter("Collisions", "Rep", "The collision check along the whole print: which bodies collide, where first, suggestions", GH_ParamAccess.list);
            p.HideParameter(0); // drawn by the component in the colours of the model
            p.HideParameter(5);
        }

        private readonly PlayClock _clock = new PlayClock();
        // one Ginger One for all frames when no machine is connected (the run and its collision bodies are kept per machine)
        private static readonly MachineDefinition DefaultMachine = MachineDefinition.GingerOne();
        private readonly MachinePreview _preview = new MachinePreview();
        // the run, kept while the same path and machine are connected
        private PrinterRun _run;
        private object _runPath, _runMachine;

        protected override void BeforeSolveInstance() { _preview.Clear(); base.BeforeSolveInstance(); }

        public override BoundingBox ClippingBox => _preview.Box(base.ClippingBox);

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            if (!Hidden) _preview.Draw(args);
        }

        /// <summary>A shaded material of this colour, as the robot is drawn (kept for older callers).</summary>
        internal static Rhino.Display.DisplayMaterial Material(System.Drawing.Color c) => MachineLook.Material(c);

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object a = null, b = null;
            da.GetData(0, ref a); da.GetData(1, ref b);
            a = (a as Grasshopper.Kernel.Types.IGH_Goo)?.ScriptVariable() ?? a;
            b = (b as Grasshopper.Kernel.Types.IGH_Goo)?.ScriptVariable() ?? b;
            var given = a as PrinterRun ?? b as PrinterRun;
            var machine = a as MachineDefinition ?? given?.Machine ?? DefaultMachine;
            var tp = b as Toolpath ?? given?.Path;
            if (a != null && !(a is MachineDefinition) && !(a is PrinterRun)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Machine takes the Machine component or Simulation of Machine G-code.");
            double pose = 0, film = 20; bool play = false, collide = true;
            da.GetData(2, ref pose); da.GetData(3, ref play);
            if (Params.Input.Count > 4) da.GetData(4, ref film);
            if (Params.Input.Count > 5) da.GetData(5, ref collide);

            var printer = machine.Printer(out string note);
            if (note != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, note);
            if (printer == null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, machine.Name + " has no 3D model: only the path and the bed are shown. Give the machine a .3dm (layers Frame, Bed, Z, Y, X, TCP; see docs/OWN_MACHINES.md), or use the Ginger One.");
            else if (!printer.CanMove) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The 3D model of " + machine.Name + " has no layers Z, Y and X: it stands still. See docs/OWN_MACHINES.md for how to draw a machine that moves.");
            if (machine.Kind == MachineKind.Robot) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, machine.Name + " is a robot: play it with Robot Program and Robot Simulation (Robot tab).");

            if (tp == null || tp.Count < 1)
            {
                if (printer != null) { foreach (var p in printer.Parts) _preview.Add(p.Mesh, p.Color); da.SetDataList(0, printer.Parts.Select(p => p.Mesh)); }
                else foreach (var p in machine.PreviewParts(out _)) _preview.Add(p.Mesh, p.Color);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect Simulation of Machine G-code, or a Toolpath, to play it.");
                return;
            }

            // the run: given by Machine G-code, or made here from the path (kept while path and machine stay the same)
            if (given != null && ReferenceEquals(given.Path, tp) && ReferenceEquals(given.Machine, machine)) _run = given;
            else if (_run == null || !ReferenceEquals(_runPath, tp) || !ReferenceEquals(_runMachine, machine)) _run = PrinterRun.Make(tp, machine);
            _runPath = tp; _runMachine = machine;
            var run = _run;

            pose = _clock.Position(this, play, pose, film);
            double seconds = pose * run.Duration;
            Point3d tip = run.Duration > 0 ? run.At(seconds, out int k) : PoseByLength(tp, pose, out k);

            var printed = new List<Point3d>();
            if (tp.Count > 1) { for (int i = 0; i < k; i++) printed.Add(tp.Points[i]); printed.Add(tip); }

            // what collides at this moment
            GantryPose hit = null;
            if (collide && machine.Kind != MachineKind.Robot)
            {
                try { hit = run.Collider?.Check(tip, k); } catch { hit = null; }
            }

            Vector3d shift = Vector3d.Zero;
            if (printer != null)
            {
                var posed = printer.Pose(tip, machine.Kind, out shift);
                string beam = machine.Kind == MachineKind.BedSlinger ? "Z" : "Y";
                foreach (var part in posed)
                {
                    bool red = hit != null && ((hit.Head && part.Group == "X") || (hit.Beam && part.Group == beam));
                    if (red) _preview.Hit(part.Mesh); else _preview.Add(part.Mesh, part.Color);
                }
                da.SetDataList(0, posed.Select(p => p.Mesh));
                if (printer.CanMove && machine.BedX > 0 && (tip.X < -1 || tip.Y < -1 || tip.X > machine.BedX + 1 || tip.Y > machine.BedY + 1 || tip.Z < -1))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"At this moment the nozzle is outside the bed of {machine.Name} ({machine.BedX:0} x {machine.BedY:0} mm, from 0, 0): {tip.X:0.0}, {tip.Y:0.0}, {tip.Z:0.0}."));
            }
            else foreach (var p in machine.PreviewParts(out _)) _preview.Add(p.Mesh, p.Color);

            var colliding = new List<Mesh>();
            string collision = "";
            if (hit != null && hit.Any)
            {
                if (hit.Head) { var m = printer?.Joined("X"); if (m != null) { m = m.DuplicateMesh(); m.Translate(printer.Move("X", tip, machine.Kind, out _)); colliding.Add(m); } }
                foreach (int i in hit.Frame) if (i < run.Collider.FramePieces.Count) { colliding.Add(run.Collider.FramePieces[i]); _preview.Hit(run.Collider.FramePieces[i]); }
                foreach (var w in hit.Where) { var ball = Mesh.CreateFromSphere(new Sphere(w, 12), 12, 8); if (ball != null) { colliding.Add(ball); _preview.Hit(ball); } }
                var what = hit.Events.Select(e => e.A + " - " + e.B).Distinct().ToList();
                collision = " - COLLISION: " + string.Join(", ", what);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Collision at this moment (drawn red): " + string.Join(", ", what) + ".");
            }
            // the whole print, once per path
            if (collide && Params.Output.Count > 6)
            {
                var report = run.Report;
                if (report != null)
                {
                    da.SetDataList(6, report.Lines);
                    if (report.Events.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", report.Lines.Take(3)));
                }
            }

            if (printed.Count > 1)
            {
                var curve = new PolylineCurve(printed);
                if (!shift.IsZero) curve.Translate(shift);
                da.SetData(1, curve);
            }
            da.SetData(2, tip + shift);
            int layer = k < tp.Layers.Count ? tp.Layers[k] + 1 : 0;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string clock = TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", ci);
            Message = (_clock.Playing ? "> " : "") + clock;
            if (Params.Output.Count > 4) da.SetData(4, seconds);
            if (Params.Output.Count > 5) da.SetDataList(5, colliding);
            da.SetData(3, $"{clock} of {TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci)} ({pose * 100:0.0} %), point {k} of {tp.Count - 1}, layer {layer}, " + Inv.F($"X {tip.X:0.0} Y {tip.Y:0.0} Z {tip.Z:0.0}")
                + ", " + MachineKinds.Label(machine.Kind, machine.BeltAngle) + (printer == null ? "" : ", model " + printer.Name) + collision);
        }

        /// <summary>For a path without times: the place at this share of its length.</summary>
        private static Point3d PoseByLength(Toolpath tp, double pose, out int k)
        {
            k = 0;
            if (tp.Count == 1) return tp.Points[0];
            var along = new double[tp.Count];
            for (int i = 1; i < tp.Count; i++) along[i] = along[i - 1] + tp.Points[i - 1].DistanceTo(tp.Points[i]);
            double s = pose * along[along.Length - 1];
            k = Array.BinarySearch(along, s); if (k < 0) k = ~k;
            k = Math.Max(1, Math.Min(tp.Count - 1, k));
            double seg = along[k] - along[k - 1];
            double t = seg > 1e-9 ? (s - along[k - 1]) / seg : 1;
            return tp.Points[k - 1] + (tp.Points[k] - tp.Points[k - 1]) * Math.Max(0, Math.Min(1, t));
        }
    }
}
