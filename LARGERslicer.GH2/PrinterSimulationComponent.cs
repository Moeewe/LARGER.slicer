using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Plays a toolpath on a gantry printer: the machine's 3D model with its gantry, beam and head
    /// moved so the nozzle stands on the path, and the part printed so far.
    /// </summary>
    [IoId("f9091a5c-02b5-4792-9761-cae6887b50bb")]
    public sealed class PrinterSimulationComponent : Component
    {
        public PrinterSimulationComponent() : base(Io.Name("Printer Simulation", "Plays the toolpath on the machine: the Ginger One (or your own machine with a 3D model) with its gantry, beam and head moved so the nozzle tip stands on the path, and the part printed up to that moment. Connect Simulation of Machine G-code, or a Machine and a Toolpath. Pose runs from 0 (start) to 1 (end) and follows the print time of the G-code. Move the slider, or set Play and it runs by itself (Film Length = seconds for one run). The model of your own machine moves when its .3dm has the layers Z, Y and X and a point on layer TCP at the nozzle tip (see docs/OWN_MACHINES.md); a bed slinger moves its bed. Head, beam and frame are checked against the printed part and the bed: Colliding shows what collides.", Io.Slicer, Group.Output)) { }
        public PrinterSimulationComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerPrinterSimulationIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Machine", "M", "The machine, from the Machine component, or Simulation of Machine G-code (then Toolpath may stay empty). Empty = Ginger One.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Toolpath", "T", "Toolpath from Start and End (in the coordinates of the machine: 0, 0, 0 is where the G-code counts from), or Simulation of Machine G-code.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Pose", "P", "Where in the print: 0 = start, 1 = end.").Set(0.0).Values(0, 0.25, 0.5, 0.75, 1);
            inputs.AddBoolean("Play", "Play", "True = the simulation runs by itself, from where Pose stands, and starts again at the end. False = it stands where Pose says.", ("Play", "Runs by itself"), ("Stop", "Stands at Pose")).Set(false);
            inputs.AddNumber("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds.").Set(20.0).Values(5, 10, 20, 30, 60, 120, 300);
            inputs.AddBoolean("Check Collisions", "CC", "True = check the head and the beam against the printed part and the bed, and the head against the frame.").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Model", "3D", "The machine at that moment: frame and bed standing, gantry (Z), beam (Y) and head (X) moved to the nozzle position.", Access.Twig);
            outputs.AddPolyline("Printed", "Pr", "The path printed up to that moment.");
            outputs.AddPoint("Tip", "TCP", "The nozzle tip at that moment.");
            outputs.AddText("Info", "I", "Where the head is: time, point, layer, X Y Z, and what collides.");
            outputs.AddNumber("Seconds", "s", "Time since the start of the print, from the feedrates of the G-code.");
            outputs.AddMesh("Colliding", "Col", "What collides at this moment: head, beam, pieces of the frame, and a ball where it touches. Empty = clear.", Access.Twig);
            outputs.AddText("Collisions", "Rep", "The collision check along the whole print.", Access.Twig);
        }

        private static readonly MachineDefinition DefaultMachine = MachineDefinition.GingerOne();
        private PrinterRun _run; private object _runPath, _runMachine;

        // Play: the component keeps its own clock and asks for the next picture.
        private bool _playing;
        private DateTime _playSince;
        private double _playFrom;
        private System.Threading.Timer _timer;

        private void NextFrame()
        {
            try
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try { Document?.Solution?.DelayedExpire(this); } catch { }
                }));
            }
            catch { }
        }

        protected override void Process(IDataAccess access)
        {
            object a, b; access.GetItem(0, out a); access.GetItem(1, out b);
            var given = a as PrinterRun ?? b as PrinterRun;
            var machine = a as MachineDefinition ?? given?.Machine ?? DefaultMachine;
            var tp = b as Toolpath ?? given?.Path;
            double pose, film; bool play, collide;
            access.GetItem(2, out pose); access.GetItem(3, out play); access.GetItem(4, out film);
            if (!access.GetItem(5, out collide)) collide = true;
            pose = Math.Max(0, Math.Min(1, pose));

            var printer = machine.Printer(out string note);
            if (note != null) access.AddWarning("3D model", note);
            if (printer == null) access.AddWarning("No 3D model", machine.Name + " has no 3D model: only the path is shown. Give the machine a .3dm (layers Frame, Bed, Z, Y, X, TCP; see docs/OWN_MACHINES.md), or use the Ginger One.");
            else if (!printer.CanMove) access.AddRemark("Model stands still", "The 3D model of " + machine.Name + " has no layers Z, Y and X: it stands still. See docs/OWN_MACHINES.md for how to draw a machine that moves.");

            if (tp == null || tp.Count < 1)
            {
                if (printer != null) access.SetTwig(0, printer.Parts.Select(p => p.Mesh).ToArray(), null, null);
                access.AddWarning("No toolpath", "Connect Simulation of Machine G-code, or a Toolpath, to play it.");
                return;
            }

            if (play)
            {
                if (!_playing) { _playing = true; _playSince = DateTime.UtcNow; _playFrom = pose >= 1 ? 0 : pose; }
                double ran = (DateTime.UtcNow - _playSince).TotalSeconds / Math.Max(1.0, film);
                pose = _playFrom + ran;
                pose -= Math.Floor(pose); // start again at the end
                if (_timer == null) _timer = new System.Threading.Timer(_ => NextFrame(), null, 33, System.Threading.Timeout.Infinite);
                else _timer.Change(33, System.Threading.Timeout.Infinite);
            }
            else { _playing = false; _timer?.Dispose(); _timer = null; }

            // the run: given by Machine G-code, or made here (kept while path and machine stay the same)
            if (given != null && ReferenceEquals(given.Path, tp) && ReferenceEquals(given.Machine, machine)) _run = given;
            else if (_run == null || !ReferenceEquals(_runPath, tp) || !ReferenceEquals(_runMachine, machine)) _run = PrinterRun.Make(tp, machine);
            _runPath = tp; _runMachine = machine;
            var run = _run;
            double seconds = pose * run.Duration;
            int k;
            Point3d tip;
            if (run.Duration > 0) tip = run.At(seconds, out k);
            else { tip = tp.Points[0]; k = tp.Count > 1 ? 1 : 0; }
            var printed = new Polyline();
            if (tp.Count > 1) { for (int i = 0; i < k; i++) printed.Add(tp.Points[i]); printed.Add(tip); }

            GantryPose hit = null;
            if (collide && machine.Kind != MachineKind.Robot) { try { hit = run.Collider?.Check(tip, k); } catch { hit = null; } }
            Vector3d shift = Vector3d.Zero;
            if (printer != null)
            {
                var posed = printer.Pose(tip, machine.Kind, out shift);
                access.SetTwig(0, posed.Select(p => p.Mesh).ToArray(), null, null);
                if (printer.CanMove && machine.BedX > 0 && (tip.X < -1 || tip.Y < -1 || tip.X > machine.BedX + 1 || tip.Y > machine.BedY + 1 || tip.Z < -1))
                    access.AddWarning("Outside the bed", Inv.F($"At this moment the nozzle is outside the bed of {machine.Name} ({machine.BedX:0} x {machine.BedY:0} mm, from 0, 0): {tip.X:0.0}, {tip.Y:0.0}, {tip.Z:0.0}."));
            }
            string collision = "";
            if (hit != null && hit.Any)
            {
                var colliding = new List<Mesh>();
                if (hit.Head && printer != null) { var m = printer.Joined("X"); if (m != null) { m = m.DuplicateMesh(); m.Translate(printer.Move("X", tip, machine.Kind, out _)); colliding.Add(m); } }
                if (hit.Beam && printer != null) { string g = machine.Kind == MachineKind.BedSlinger ? "Z" : "Y"; var m = printer.Joined(g); if (m != null) { m = m.DuplicateMesh(); m.Translate(printer.Move(g, tip, machine.Kind, out _)); colliding.Add(m); } }
                foreach (int i in hit.Frame) if (i < run.Collider.FramePieces.Count) colliding.Add(run.Collider.FramePieces[i]);
                foreach (var w in hit.Where) { var ball = Mesh.CreateFromSphere(new Sphere(w, 12), 12, 8); if (ball != null) colliding.Add(ball); }
                access.SetTwig(5, colliding.ToArray(), null, null);
                var what = hit.Events.Select(e => e.A + " - " + e.B).Distinct().ToList();
                collision = " - COLLISION: " + string.Join(", ", what);
                access.AddWarning("Collision", "Collision at this moment: " + string.Join(", ", what) + ". See Colliding.");
            }
            if (collide && run.Report != null)
            {
                access.SetTwig(6, run.Report.Lines.ToArray(), null, null);
                if (run.Report.Events.Count > 0) access.AddWarning("Collisions in the print", string.Join("\n", run.Report.Lines.Take(3)));
            }
            if (printed.Count > 1) { if (!shift.IsZero) printed.Transform(Transform.Translation(shift)); access.SetItem(1, printed); }
            access.SetItem(2, tip + shift);
            access.SetItem(4, seconds);
            int layer = k < tp.Layers.Count ? tp.Layers[k] + 1 : 0;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            access.SetItem(3, (play ? "> " : "") + TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", ci) + " of " + TimeSpan.FromSeconds(run.Duration).ToString(@"h\:mm\:ss", ci)
                + Inv.F($" ({pose * 100:0.0} %), point {k} of {tp.Count - 1}, layer {layer}, X {tip.X:0.0} Y {tip.Y:0.0} Z {tip.Z:0.0}, ") + MachineKinds.Label(machine.Kind, machine.BeltAngle) + (printer == null ? "" : ", model " + printer.Name) + collision);
        }
    }
}
