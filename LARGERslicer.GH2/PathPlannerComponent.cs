using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;

namespace LARGERslicer.GH2
{
    /// <summary>Path Planner: the whole print path of a part, planned by the optional planner library (the same as in Grasshopper 1).</summary>
    [IoId("c4a7e2d9-5b18-4f63-9e0a-2d7b6c1f8a35")]
    public sealed class PathPlannerComponent : Component
    {
        public PathPlannerComponent() : base(Io.Name("Path Planner", "Model in, planned print path out. The planner builds the path of the whole part in several ways (where the seam lies, how the solid layers are filled and entered), measures each one (crossings, material laid twice, unfilled places, travel, time) and hands out the best; the others come out in Variants. Skirt, brim, the way from layer to layer and the exit are part of the path: Start and End is NOT needed after it. Connect Toolpath to Path Check, Machine G-code, Robot Program or Printer Simulation. Bead, layer, speeds, nozzle, overlap and machine profile come from Print Settings. Needs the planner library, an optional file that is not part of the public plug-in; without it the component says so.", Io.Slicer, Group.Start)) { }
        public PathPlannerComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerPlannerIcon");

        private static string Numbered(string[] names) => string.Join("\n", names.Select((n, i) => i + " = " + n));

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "G", "Your part: Breps, surfaces, SubDs or meshes, where it stands on the bed (it is not moved).", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Settings", "S", "Print Settings: bead width, layer height, first layer, speeds, nozzle, overlap and the machine profile come from here. Empty = the house settings (bead 5, layer 2).", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Walls", "N", "Walls side by side, one line.").Set(1).Counts(1, 2, 3, 4, 5, 6);
            inputs.AddInteger("Wall Position", "WP", "Where the walls lie:\n" + Numbered(PlannerOptions.WallPositions)).Set(0).Choices(PlannerOptions.WallPositions);
            inputs.AddInteger("Bottom Layers", "B", "Solid layers at the bottom. 0 = open (hulls, vases). An even number prints without a travel move.").Set(0).Counts(0, 1, 2, 3, 4, 5, 10);
            inputs.AddInteger("Top Layers", "T", "Solid layers under the top of the part. They need something to lie on.").Set(0).Counts(0, 1, 2, 3, 4, 5, 10);
            inputs.AddInteger("Fill", "F", "How solid layers are filled:\n" + Numbered(PlannerOptions.Fills) + "\nThe planner also tries the others and lists them in Scores.").Set(0).Choices(PlannerOptions.Fills);
            inputs.AddInteger("Infill", "I", "Infill along the wall, part of the one line:\n" + Numbered(PlannerOptions.Infills)).Set(0).Choices(PlannerOptions.Infills);
            inputs.AddNumber("Infill Size", "Sz", "Rib distance or wave length in mm. 0 = chosen from the bead width.").Set(0.0).Values(0, 30, 40, 60, 80, 100, 150, 200);
            inputs.AddInteger("Skirt Loops", "Sk", "Loops around the part before it starts. 0 = none.").Set(1).Counts(0, 1, 2, 3, 4, 5);
            inputs.AddInteger("Brim", "Br", "Beads right against the first layer, so the part holds on to the bed:\n" + Numbered(PlannerOptions.Brims)).Set(0).Choices(PlannerOptions.Brims);
            inputs.AddInteger("Layers", "L", "The kind of layers:\n" + Numbered(PlannerOptions.LayerKinds) + "\nFollow ... = non-planar, the nozzle stays vertical. Spiral = vase mode (one wall, no infill).").Set(0).Choices(PlannerOptions.LayerKinds);
            inputs.AddNumber("Angle", "A", "Flat layers only: slice angle in degrees. 45 = layers leaning 45 degrees (robot or tilted bed).").Set(0.0).Angles();
            inputs.AddInteger("Seam", "Se", "Where the layers change:\n" + Numbered(PlannerOptions.Seams)).Set(0).Choices(PlannerOptions.Seams);
            inputs.AddInteger("Goal", "Go", "What the best path is:\n0 = Best (balanced)\n1 = Fast (shortest time, least travel)\n2 = Fill (least unfilled area in solid layers)\n3 = Seam (hidden in a corner and stacked)").Set(0).Choices(PlannerOptions.Goals);
            inputs.AddInteger("Variants", "V", "How many paths come out in Variants (the best first).").Set(1).Counts(1, 2, 3, 4, 5, 8);
            inputs.AddBoolean("Strategy Auto", "Auto", "The planner compares the kinds of layers that suit the part and the machine and takes the cheapest (Analysis lists them). Layers and Angle are then not used.").Set(false);
            inputs.AddPoint("Seam Point", "SP", "Optional. The seam goes to the place on the wall nearest to this point (Seam = Auto or At Seam Point).", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The best path, complete with skirt, brim and exit: connect to Path Check, Machine G-code, Robot Program or Printer Simulation (no Start and End in between).");
            outputs.AddPolyline("Path", "P", "The best path as a polyline.");
            outputs.AddBoolean("OK", "OK", "True if the planner's check finds the path printable.");
            outputs.AddText("Report", "R", "What was planned, what the check found and what to do about it.", Access.Twig);
            outputs.AddGeneric("Variants", "Var", "The paths as Toolpaths, the best first (as many as Variants asks for).", Access.Twig);
            outputs.AddText("Names", "Nm", "What each variant is best at (best, fill, fast, seam) and the choices it was built with.", Access.Twig);
            outputs.AddText("Scores", "Sc", "All paths the planner built, side by side: a table of their measures.");
            outputs.AddText("Analysis", "An", "The geometry analysis of the part (and with Strategy Auto the kinds of layers that were compared).");
        }

        protected override void Process(IDataAccess access)
        {
            if (!PlannerCore.Available)
            {
                access.AddWarning("Planner not installed", PlannerCore.Why);
                access.SetItem(2, false); access.SetTwig(3, new[] { PlannerCore.Why }, null, null);
                return;
            }
            object[] geo;
            if (!access.ItemArray(0, out geo) || geo == null || geo.Length == 0) { access.AddRemark("Connect a part", "Connect your part to Geometry: a Brep, surface, SubD or mesh, where it stands on the bed."); return; }
            var settings = Io.Settings(access, 1).Duplicate();
            var o = new PlannerOptions();
            access.GetItem(2, out o.Walls); access.GetItem(3, out o.WallPosition); access.GetItem(4, out o.BottomLayers); access.GetItem(5, out o.TopLayers);
            access.GetItem(6, out o.Fill); access.GetItem(7, out o.Infill); access.GetItem(8, out o.InfillSize); access.GetItem(9, out o.SkirtLoops); access.GetItem(10, out o.Brim);
            access.GetItem(11, out o.Layers); access.GetItem(12, out o.Angle); access.GetItem(13, out o.Seam); access.GetItem(14, out o.Goal); access.GetItem(15, out o.Variants);
            access.GetItem(16, out o.StrategyAuto);
            if (access.GetItem(17, out Point3d seamPoint) && seamPoint.IsValid) o.SeamPoint = seamPoint;
            else if (o.Seam == 3) access.AddRemark("Seam Point", "Seam = At Seam Point, but Seam Point is empty: the seam goes to the longest side.");
            if (settings.BeadWidth <= 0 || settings.LayerHeight <= 0 || settings.Speed <= 0) { access.AddError("Invalid values", "Bead width, layer height and speed of the Print Settings must be greater than 0."); return; }
            var log = new List<string>();
            if (Io.Step(access, 5)) return; // progress; stop if the inputs changed meanwhile
            bool bent = o.Layers >= 1 && o.Layers <= 3;
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), Math.Max(0.01, settings.Preview ? settings.Resolution * 0.25 : settings.Resolution * 0.025), log, bent ? Math.Max(4.0, settings.BeadWidth * 1.5) : 0);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            if (Io.Step(access, 20)) return;
            var token = access.Solution != null ? access.Solution.Token : default;
            var r = PlannerCore.Run(mesh, settings, o, token);
            if (token.IsCancellationRequested) return;
            var report = r.ReportLines();
            foreach (string l in log) report.Add("Note: " + l);
            string analysis = r.Strategies.Length > 0 ? r.Strategies + "\n" + r.Analysis : r.Analysis;
            if (r.Error != null)
            {
                access.AddError("No print path", r.Error);
                access.SetItem(2, false); access.SetTwig(3, report.ToArray(), null, null); access.SetItem(7, analysis);
                return;
            }
            var best = r.Best;
            if (!best.Ok) access.AddWarning("The check found something", string.Join("\n", best.Lines("check")));
            Io.Label(this, best.Ok ? "Printable" : "Read the report");
            access.SetItem(0, best.Path);
            access.SetItem(1, best.Path.ToPolyline());
            access.SetItem(2, best.Ok);
            access.SetTwig(3, report.ToArray(), null, null);
            access.SetTwig(4, r.Variants.Select(v => v.Path).ToArray(), null, null);
            access.SetTwig(5, r.Variants.Select(v => v.Name + (v.Choices.Length > 0 ? ": " + v.Choices : "") + (v.Ok ? "" : " (not printable)")).ToArray(), null, null);
            access.SetItem(6, r.Table);
            access.SetItem(7, analysis);
        }
    }
}
