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
        public PrinterSimulationComponent() : base(Io.Name("Printer Simulation", "Plays the toolpath on the machine: the Ginger One (or your own machine with a 3D model) with its gantry, beam and head moved so the nozzle tip stands on the path, and the part printed up to that moment. Pose runs from 0 (start) to 1 (end) along the path; the speed is constant, so half way on the slider is about half the print time. Move the slider, or set Play and it runs by itself (Film Length = seconds for one run). The model of your own machine moves when its .3dm has the layers Z, Y and X and a point on layer TCP at the nozzle tip (see GingerOne.README.md); else it stands still.", Io.Slicer, Group.Output)) { }
        public PrinterSimulationComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerPrinterSimulationIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Machine", "M", "The machine, from the Machine component. Empty = Ginger One.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Toolpath", "T", "Toolpath from Start and End (in the coordinates of the machine: 0, 0, 0 is where the G-code counts from).", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Pose", "P", "Where in the print: 0 = start, 1 = end.").Set(0.0).Values(0, 0.25, 0.5, 0.75, 1);
            inputs.AddBoolean("Play", "Play", "True = the simulation runs by itself, from where Pose stands, and starts again at the end. False = it stands where Pose says.", ("Play", "Runs by itself"), ("Stop", "Stands at Pose")).Set(false);
            inputs.AddNumber("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds.").Set(20.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Model", "3D", "The machine at that moment: frame and bed standing, gantry (Z), beam (Y) and head (X) moved to the nozzle position.", Access.Twig);
            outputs.AddPolyline("Printed", "Pr", "The path printed up to that moment.");
            outputs.AddPoint("Tip", "TCP", "The nozzle tip at that moment.");
            outputs.AddText("Info", "I", "Where the head is: point, layer, X Y Z.");
        }

        // Play: the component keeps its own clock and asks for the next picture.
        private bool _playing;
        private DateTime _playSince;
        private double _playFrom;
        private System.Threading.Timer _timer;
        // the lengths along the path, kept while the same path is connected
        private Toolpath _path;
        private double[] _along;

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
            var machine = Io.Get<MachineDefinition>(access, 0) ?? MachineDefinition.GingerOne();
            var tp = Io.Get<Toolpath>(access, 1);
            double pose, film; bool play;
            access.GetItem(2, out pose); access.GetItem(3, out play); access.GetItem(4, out film);
            pose = Math.Max(0, Math.Min(1, pose));

            var printer = machine.Printer(out string note);
            if (note != null) access.AddWarning("3D model", note);
            if (printer == null) access.AddWarning("No 3D model", machine.Name + " has no 3D model: only the path is shown. Connect a model at the Machine component and save the machine, or use the Ginger One.");
            else if (!printer.CanMove) access.AddRemark("Model stands still", "The 3D model of " + machine.Name + " has no layers Z, Y and X: it stands still. See GingerOne.README.md for how to draw a machine that moves.");

            if (tp == null || tp.Count < 1)
            {
                if (printer != null) access.SetTwig(0, printer.Parts.Select(p => p.Mesh).ToArray(), null, null);
                access.AddWarning("No toolpath", "Connect a Toolpath to play it.");
                return;
            }

            if (play)
            {
                if (!_playing) { _playing = true; _playSince = DateTime.UtcNow; _playFrom = pose >= 1 ? 0 : pose; }
                double ran = (DateTime.UtcNow - _playSince).TotalSeconds / Math.Max(1.0, film);
                pose = _playFrom + ran;
                pose -= Math.Floor(pose); // start again at the end
                if (_timer == null) _timer = new System.Threading.Timer(_ => NextFrame(), null, 40, System.Threading.Timeout.Infinite);
                else _timer.Change(40, System.Threading.Timeout.Infinite);
            }
            else { _playing = false; _timer?.Dispose(); _timer = null; }

            // where along the path: by length (constant speed)
            if (!ReferenceEquals(tp, _path) || _along == null || _along.Length != tp.Count)
            {
                _path = tp;
                _along = new double[tp.Count];
                for (int i = 1; i < tp.Count; i++) _along[i] = _along[i - 1] + tp.Points[i - 1].DistanceTo(tp.Points[i]);
            }
            double total = _along[_along.Length - 1];
            double s = pose * total;
            int k = Array.BinarySearch(_along, s);
            if (k < 0) k = ~k;
            k = Math.Max(1, Math.Min(tp.Count - 1, k));
            Point3d tip;
            var printed = new Polyline();
            if (tp.Count == 1) { tip = tp.Points[0]; k = 0; }
            else
            {
                double seg = _along[k] - _along[k - 1];
                double t = seg > 1e-9 ? (s - _along[k - 1]) / seg : 1;
                tip = tp.Points[k - 1] + (tp.Points[k] - tp.Points[k - 1]) * Math.Max(0, Math.Min(1, t));
                for (int i = 0; i < k; i++) printed.Add(tp.Points[i]);
                printed.Add(tip);
            }

            if (printer != null)
            {
                var posed = printer.Pose(tip);
                access.SetTwig(0, posed.Select(p => p.Mesh).ToArray(), null, null);
                if (printer.CanMove && machine.BedX > 0 && (tip.X < -1 || tip.Y < -1 || tip.X > machine.BedX + 1 || tip.Y > machine.BedY + 1 || tip.Z < -1))
                    access.AddWarning("Outside the bed", Inv.F($"At this moment the nozzle is outside the bed of {machine.Name} ({machine.BedX:0} x {machine.BedY:0} mm, from 0, 0): {tip.X:0.0}, {tip.Y:0.0}, {tip.Z:0.0}."));
            }
            if (printed.Count > 1) access.SetItem(1, printed);
            access.SetItem(2, tip);
            int layer = k < tp.Layers.Count ? tp.Layers[k] : 0;
            access.SetItem(3, (play ? "> " : "") + Inv.F($"{pose * 100:0.0} % of {total / 1000:0.0} m path, point {k} of {tp.Count - 1}, layer {layer}, X {tip.X:0.0} Y {tip.Y:0.0} Z {tip.Z:0.0}") + (printer == null ? "" : ", model " + printer.Name));
        }
    }
}
