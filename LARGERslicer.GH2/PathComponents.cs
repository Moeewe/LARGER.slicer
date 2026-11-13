using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== round corners
    [IoId("4d6db9e2-7d78-44e6-93b3-ef56a15cc649")]
    public sealed class RoundCornersComponent : Component
    {
        public RoundCornersComponent() : base(Io.Name("Round Corners", "Rounds every corner of the path with an arc that leaves and rejoins the path tangentially. Zigzag turns and rib tips become half circles. Skipped while Preview is on in the Print Settings.", Io.Slicer, Group.Path)) { }
        public RoundCornersComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerRoundIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path.");
            inputs.AddNumber("Min Radius", "R", "Corner radius for walls (mm). 0 = value from the Print Settings (not set = half the bead width).").Set(0.0);
            inputs.AddNumber("Rib Radius", "Rr", "Corner radius where ribs and fill leave the wall (mm). 0 = bead spacing. Larger = softer, wider rib foot.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "Rounded print path.");
            outputs.AddText("Info", "I", "Statistics.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            double r, rr; access.GetItem(1, out r); access.GetItem(2, out rr);
            double rIn = r;
            if (r <= 0) r = tp.Settings.EffectiveMinRadius;
            if (tp.Settings.Preview)
            {
                access.SetItem(0, tp);
                access.SetItem(1, "Preview: corners not rounded");
                return;
            }
            var sw = Stopwatch.StartNew();
            var rounded = PathEngine.Round(tp, r, rr, out int corners, out int tooSharp);
            sw.Stop();
            if (tooSharp > 0) access.AddRemark("Smaller radius", $"{tooSharp} wall corner(s) have too little room for R{r:0.##} (another corner is too close); they got the largest arc that fits.");
            access.AddRemark($"R {r:0.##} mm", $"Wall corners are rounded with R {r:0.##} mm" + (rIn > 0 ? "." : " (Min Radius 0: from the Print Settings, half the bead width unless set there; the Ginger handbook asks for at least half the nozzle)."));
            access.SetItem(0, rounded);
            access.SetItem(1, $"R{r:0.#}: {corners} corner(s) rounded, {tooSharp} with a smaller radius than R{r:0.##}, {rounded.Count} points, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== start and end
    [IoId("4c837415-aead-47ee-8431-71973190f46b")]
    public sealed class StartEndComponent : Component
    {
        public StartEndComponent() : base(Io.Name("Start and End", "Adds a skirt before the part, a brim against its first layer if you want one, and an exit move after it. The skirt is always printed first and runs on into the outer wall as one continuous bead; the exit moves the nozzle off the part before it stops, so no drop is left on it.", Io.Slicer, Group.Path)) { }
        public StartEndComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerStartEndIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path.");
            inputs.AddInteger("Skirt Loops", "Sk", "Loops around the first layer before the part starts. 0 = no skirt.").Set(1);
            inputs.AddNumber("Skirt Distance", "Sd", "Distance between skirt and part (mm). 0 = three bead widths.").Set(0.0);
            inputs.AddNumber("Exit Length", "Ex", "After the last point: move sideways off the part by this length (mm). 0 = none.").Set(50.0);
            inputs.AddNumber("Exit Lift", "Lift", "Then lift the nozzle by this height (mm).").Set(20.0);
            inputs.AddCurve("Exit Curve", "Crv", "Optional: your own exit path. Replaces Exit Length and Exit Lift.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Exit Flow", "Fl", "Flow factor on the exit move. 0 = no extrusion, 1 = full extrusion.").Set(0.0);
            inputs.AddInteger("Brim", "Br", "Beads that lie right against the first layer and widen its foot, so the part holds on to the bed:\n0 = none\n1 = outside\n2 = inside (only where the first layer is open inside)\n3 = both sides\nThe brim is part of the one continuous line: outside it runs into the wall, inside it is printed when the first layer is finished").Set(0).Choices("None", "Outside", "Inside", "Both sides");
            inputs.AddInteger("Brim Loops", "Bl", "Number of brim beads on each side.").Set(3);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "Print path with skirt and exit.");
            outputs.AddText("Info", "I", "Statistics.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            int loops, brim, brimLoops; double dist, exit, lift, flow;
            access.GetItem(1, out loops); access.GetItem(2, out dist); access.GetItem(3, out exit); access.GetItem(4, out lift);
            access.GetItem(5, out Curve crv); access.GetItem(6, out flow);
            access.GetItem(7, out brim); access.GetItem(8, out brimLoops);
            brim = Math.Max(0, Math.Min(3, brim));
            var log = new List<string>();
            var result = LeadEngine.StartEnd(tp, Math.Max(0, loops), dist, Math.Max(0, brimLoops), brim, Math.Max(0, exit), Math.Max(0, lift), crv, flow, log);
            foreach (var l in log)
                if (l.StartsWith("Note: ")) access.AddRemark(l.Length > 60 ? l.Substring(0, 60) + "…" : l, l);
                else access.AddWarning(l.Length > 60 ? l.Substring(0, 60) + "…" : l, l);
            int skirt = result.Roles.Count(x => x == PathRole.Skirt);
            string what = (loops > 0 ? "Skirt" : "No skirt") + (brim > 0 && brimLoops > 0 ? " + brim" : "") + (crv != null ? " + curve exit" : (exit > 0 || lift > 0 ? " + exit" : ""));
            access.SetItem(0, result);
            access.SetItem(1, $"{what}: {skirt} skirt and brim points, {result.Count} points in all ({tp.Count} before; points closer than {Math.Max(0.5, 0.2 * tp.Settings.BeadWidth):0.#} mm to the one before are left out), path now {result.Length() / 1000.0:0.00} m");
        }
    }
}
