using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    // ============================================================== 2e Support Fins
    /// <summary>
    /// Thin fins outside the part that hold up overhangs. They are hairpins out of the outer
    /// wall, so the path stays one line, and they touch the part only through a narrow neck.
    /// </summary>
    public class SupportFinsComponent : SlicerComponent
    {
        public SupportFinsComponent()
            : base("Support Fins", "Fins",
                  "Thin fins outside the part that hold up overhangs. Each fin stands square to the outer wall, from the bed up to just under the overhang, and is part of the one continuous line: the path leaves the wall, runs out and comes back beside itself. It touches the part through a narrow neck, so it is easy to cut off. Use it after Walls (and after Bottom Fill and infill), right before Continuous Path.") { }

        public override Guid ComponentGuid => new Guid("24D16B84-E160-448E-BEF4-C580F1F4C149");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerFinsIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls (or after Bottom Fill and infill)", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddNumberParameter("Overhang Angle", "A", "Steepest overhang that prints without support, in degrees from vertical. Steeper places get fins. 0 = half a bead width per layer (about 51 degrees for a 5 mm bead and 2 mm layers)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Spacing", "Sp", "Distance between fins along the overhang, in bead widths", GH_ParamAccess.item, 15.0);
            p.AddNumberParameter("Reach", "R", "How far a fin reaches beyond the overhanging wall, in bead widths", GH_ParamAccess.item, 1.0);
            p.AddIntegerParameter("Contact", "C", "How the fin meets the part:\n0 = Neck: the fin touches the part through a neck about one bead wide. Easy to cut\n1 = Neck + gap: also the top fin layer is printed lower by Gap, so the overhang rests on it without fusing. Breaks off\n2 = Full: both legs meet the wall at full bead spacing. Strongest, hardest to remove", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Gap", "G", "Contact 1 only: how much lower the top fin layer is printed, in mm (0.2 to 0.5)", GH_ParamAccess.item, 0.3);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 3, "Fin Spacing (bead widths)", 4m, 60m, 15m, 0, "", new double[] { 5, 10, 15, 20, 30 });
            AddSlider(doc, 4, "Reach (bead widths)", 0m, 5m, 1m, 1, "", new double[] { 0, 0.5, 1, 2, 3 });
            AddValueList(doc, 5, "Contact", "Neck (cut off)", "Neck + gap (break off)", "Full (strongest)");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with the fins joined into the outer wall", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Paths as curves (for checking)", GH_ParamAccess.list);
            p.AddCurveParameter("Fins", "F", "Only the fins, one curve per fin and layer (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            double angle = 0, spacing = 15, reach = 1, gap = 0.3; int contact = 0;
            da.GetData(2, ref angle); da.GetData(3, ref spacing); da.GetData(4, ref reach); da.GetData(5, ref contact); da.GetData(6, ref gap);
            contact = Math.Max(0, Math.Min(2, contact));
            if (contact == 1 && (gap < 0.2 || gap > 0.5))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "A gap of 0.2 to 0.5 mm works best: less fuses, more lets the overhang sag.");

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = SupportFins.Add(layers, settings, angle, spacing, reach, (SupportFins.Contact)contact, gap, log, out int fins, out List<Polyline> finPaths);
            sw.Stop();
            Message = fins == 0 ? "No fins" : fins + (fins == 1 ? " fin" : " fins");
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);

            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetDataList(2, finPaths.Select(c => new PolylineCurve(c)));
            da.SetData(3, $"{fins} fin(s), {finPaths.Count} fin pieces, spacing {spacing * settings.BeadWidth:0} mm, reach {reach * settings.BeadWidth:0.#} mm, {sw.ElapsedMilliseconds} ms");
        }
    }
}
