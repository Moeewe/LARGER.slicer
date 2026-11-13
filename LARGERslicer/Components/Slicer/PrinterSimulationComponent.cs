using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Plays a toolpath on a gantry printer: the machine's 3D model with its gantry, beam and head
    /// moved so the nozzle stands on the path, and the part printed so far.
    /// </summary>
    public class PrinterSimulationComponent : SlicerComponent
    {
        public PrinterSimulationComponent()
            : base("Printer Simulation", "Simulate",
                  "Plays the toolpath on the machine: the Ginger One (or your own machine with a 3D model) with its gantry, beam and head moved so the nozzle tip stands on the path, and the part printed up to that moment. Pose runs from 0 (start) to 1 (end) along the path; the speed is constant, so half way on the slider is about half the print time. Move the slider, or set Play and it runs by itself (Film Length = seconds for one run). The model of your own machine moves when its .3dm has the layers Z, Y and X and a point on layer TCP at the nozzle tip (see GingerOne.README.md); else it stands still.") { }

        public override Guid ComponentGuid => new Guid("3E7B2C91-4A6D-4F18-9B05-C8D1E2F47A36");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPrinterSimulationIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Machine", "M", "The machine, from the Machine component. Empty = Ginger One", GH_ParamAccess.item);
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End (in the coordinates of the machine: 0, 0, 0 is where the G-code counts from)", GH_ParamAccess.item);
            p.AddNumberParameter("Pose", "P", "Where in the print: 0 = start, 1 = end", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Play", "Play", "True = the simulation runs by itself, from where Pose stands, and starts again at the end. False = it stands where Pose says", GH_ParamAccess.item, false);
            p.AddNumberParameter("Film Length", "Len", "How long one run of the whole print takes on the screen, in seconds", GH_ParamAccess.item, 20.0);
            p[0].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 2, "Pose", 0m, 1m, 0m, 3);
            AddToggle(doc, 3, "Play", false, "■  Stop", "▶  Play");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Model", "3D", "The machine at that moment: frame and bed standing, gantry (Z), beam (Y) and head (X) moved to the nozzle position", GH_ParamAccess.list);
            p.AddCurveParameter("Printed", "Pr", "The path printed up to that moment", GH_ParamAccess.item);
            p.AddPointParameter("Tip", "TCP", "The nozzle tip at that moment", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Where the head is: point, layer, X Y Z", GH_ParamAccess.item);
            p.HideParameter(0); // drawn by the component in the colours of the model
        }

        // Play: the component keeps its own clock and asks Grasshopper for the next picture.
        private bool _playing;
        private DateTime _playSince;
        private double _playFrom;
        // the lengths along the path, kept while the same path is connected
        private Toolpath _path;
        private double[] _along;

        private readonly List<PrinterModel.Part> _parts = new List<PrinterModel.Part>();
        protected override void BeforeSolveInstance() { _parts.Clear(); base.BeforeSolveInstance(); }

        public override BoundingBox ClippingBox { get { var b = base.ClippingBox; foreach (var p in _parts) b.Union(p.Mesh.GetBoundingBox(false)); return b; } }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            if (Hidden) return;
            foreach (var p in _parts) args.Display.DrawMeshShaded(p.Mesh, Material(p.Color));
        }

        private static readonly Dictionary<int, Rhino.Display.DisplayMaterial> Materials = new Dictionary<int, Rhino.Display.DisplayMaterial>();
        /// <summary>A shaded material of this colour (kept, so drawing does not make new ones every frame).</summary>
        internal static Rhino.Display.DisplayMaterial Material(System.Drawing.Color c)
        {
            lock (Materials)
            {
                if (!Materials.TryGetValue(c.ToArgb(), out var m))
                {
                    m = new Rhino.Display.DisplayMaterial(c) { Shine = 0.3, Specular = System.Drawing.Color.FromArgb(70, 70, 70) };
                    Materials[c.ToArgb()] = m;
                }
                return m;
            }
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var machine = SlicerIO.Get<MachineDefinition>(da, 0) ?? MachineDefinition.GingerOne();
            var tp = SlicerIO.Get<Toolpath>(da, 1);
            double pose = 0; da.GetData(2, ref pose);
            bool play = false; double film = 20;
            da.GetData(3, ref play); da.GetData(4, ref film);
            pose = Math.Max(0, Math.Min(1, pose));

            var printer = machine.Printer(out string note);
            if (note != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, note);
            if (printer == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, machine.Name + " has no 3D model: only the path is shown. Connect a model at the Machine component and save the machine, or use the Ginger One.");
            }
            else if (!printer.CanMove) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The 3D model of " + machine.Name + " has no layers Z, Y and X: it stands still. See GingerOne.README.md for how to draw a machine that moves.");

            if (tp == null || tp.Count < 1)
            {
                if (printer != null) { _parts.AddRange(printer.Parts); da.SetDataList(0, printer.Parts.Select(p => p.Mesh)); }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Toolpath to play it.");
                return;
            }

            if (play)
            {
                if (!_playing) { _playing = true; _playSince = DateTime.UtcNow; _playFrom = pose >= 1 ? 0 : pose; }
                double ran = (DateTime.UtcNow - _playSince).TotalSeconds / Math.Max(1.0, film);
                pose = _playFrom + ran;
                pose -= Math.Floor(pose); // start again at the end
                var ghDoc = OnPingDocument();
                if (ghDoc != null) ghDoc.ScheduleSolution(40, d => ExpireSolution(false));
            }
            else _playing = false;

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
            var printed = new List<Point3d>();
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
                _parts.AddRange(posed);
                da.SetDataList(0, posed.Select(p => p.Mesh));
                if (printer.CanMove && machine.BedX > 0 && (tip.X < -1 || tip.Y < -1 || tip.X > machine.BedX + 1 || tip.Y > machine.BedY + 1 || tip.Z < -1))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"At this moment the nozzle is outside the bed of {machine.Name} ({machine.BedX:0} x {machine.BedY:0} mm, from 0, 0): {tip.X:0.0}, {tip.Y:0.0}, {tip.Z:0.0}."));
            }
            if (printed.Count > 1) da.SetData(1, new PolylineCurve(printed));
            da.SetData(2, tip);
            int layer = k < tp.Layers.Count ? tp.Layers[k] : 0;
            Message = (play ? "> " : "") + Inv.F($"{pose * 100:0.0} %");
            da.SetData(3, Inv.F($"{pose * 100:0.0} % of {total / 1000:0.0} m path, point {k} of {tp.Count - 1}, layer {layer}, X {tip.X:0.0} Y {tip.Y:0.0} Z {tip.Z:0.0}") + (printer == null ? "" : ", model " + printer.Name));
        }
    }
}
