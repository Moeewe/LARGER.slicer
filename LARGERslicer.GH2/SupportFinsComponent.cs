using System;
using System.Collections.Generic;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== support fins
    [IoId("d784c265-d941-4bb2-9cb6-c7fa37067ad2")]
    public sealed class SupportFinsComponent : Component
    {
        public SupportFinsComponent() : base(Io.Name("Support Fins", "Thin fins outside the part that hold up overhangs. Each fin stands square to the outer wall, from the bed up to just under the overhang, and is part of the one continuous line. It touches the part through a narrow neck, so it is easy to cut off. Use it right before Continuous Path.", Io.Slicer, 2)) { }
        public SupportFinsComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers with walls (and bottom, infill).");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Overhang Angle", "A", "Steepest overhang that prints without support, in degrees from vertical. 0 = half a bead width per layer.").Set(0.0);
            inputs.AddNumber("Spacing", "Sp", "Distance between fins along the overhang, in bead widths.").Set(15.0);
            inputs.AddNumber("Reach", "R", "How far a fin reaches beyond the overhanging wall, in bead widths.").Set(1.0);
            inputs.AddInteger("Contact", "C", "0 = neck about one bead wide (cut off). 1 = neck, and the top fin layer is printed lower by Gap (breaks off). 2 = full width (strongest).").Set(0);
            inputs.AddNumber("Gap", "G", "Contact 1 only: how much lower the top fin layer is printed, in mm (0.2 to 0.5).").Set(0.3);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers with the fins joined into the outer wall.");
            outputs.AddPolyline("Contours", "C", "All contours.", Access.Twig);
            outputs.AddPolyline("Fins", "F", "Only the fins, one polyline per fin and layer.", Access.Twig);
            outputs.AddText("Info", "I", "What was built.");
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            double angle, spacing, reach, gap; int contact;
            access.GetItem(2, out angle); access.GetItem(3, out spacing); access.GetItem(4, out reach);
            access.GetItem(5, out contact); access.GetItem(6, out gap);
            contact = Math.Max(0, Math.Min(2, contact));
            var log = new List<string>();
            var result = SupportFins.Add(layers, settings, angle, spacing, reach, (SupportFins.Contact)contact, gap, log, out int fins, out List<Polyline> finPaths);
            Io.Remarks(access, log);
            access.SetItem(0, new LayerStack(result));
            Io.Contours(access, 1, result);
            access.SetTwig(2, finPaths.ToArray(), null, null);
            access.SetItem(3, $"{fins} fin(s), {finPaths.Count} fin pieces");
        }
    }
}
