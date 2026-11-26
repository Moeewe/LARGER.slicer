using System;
using System.Collections.Generic;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;

namespace LARGERslicer.GH2
{
    /// <summary>Print Tuning: speeds by kind of line, coasting, prime and travel lift (the same as in Grasshopper 1).</summary>
    [IoId("7d2c9e41-5a83-4b6f-a1c7-0e9b3f58d214")]
    public sealed class PrintTuningComponent : Component
    {
        public PrintTuningComponent() : base(Io.Name("Print Tuning", "Fine-tunes the Print Settings for pellet extruders. Each kind of line at its own speed (walls, infill, bottom, skirt, support; 0 = the print speed). Coasting stops the extruder this far before every stop or travel move, because the screw runs on and would leave a blob. Prime gives the first move after a stop the material of this much bead extra, so the bead starts full. Travel Lift lifts the nozzle for travel moves on G-code machines. Put it between Print Settings and the slicer components.", Io.Slicer, Group.Start)) { }
        public PrintTuningComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerTuningIcon");

        private static readonly PathRole[] Roles = { PathRole.Wall, PathRole.Infill, PathRole.Bottom, PathRole.Skirt, PathRole.Support };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Wall Speed", "Vw", "Speed of the walls in mm/s. 0 = the print speed.").Set(0.0).Values(0, 10, 20, 30, 50, 80);
            inputs.AddNumber("Infill Speed", "Vi", "Speed of ribs, cells, waves and chambers in mm/s. 0 = the print speed.").Set(0.0).Values(0, 10, 20, 30, 50, 80);
            inputs.AddNumber("Bottom Speed", "Vb", "Speed of the solid bottom and top layers in mm/s. 0 = the print speed.").Set(0.0).Values(0, 10, 20, 30, 50, 80);
            inputs.AddNumber("Skirt Speed", "Vs", "Speed of skirt and brim in mm/s. 0 = the print speed.").Set(0.0).Values(0, 10, 20, 30, 50, 80);
            inputs.AddNumber("Support Speed", "Vf", "Speed of support fins in mm/s. 0 = the print speed.").Set(0.0).Values(0, 10, 20, 30, 50, 80);
            inputs.AddNumber("Coasting", "Co", "The extruder stops this far (mm) before every stop or travel move. 0 = off. Pellet extruders: about 1 to 2 bead widths.").Set(0.0).Values(0, 2, 5, 8, 10, 15);
            inputs.AddNumber("Prime", "Pr", "After a stop the first move gets the material of this much bead (mm) extra. 0 = off.").Set(0.0).Values(0, 2, 5, 8, 10);
            inputs.AddNumber("Travel Lift", "Lift", "G-code machines: the nozzle is lifted this far (mm) for travel moves (Z-hop). 0 = off.").Set(0.0).Values(0, 1, 2, 5, 10);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Settings", "S", "The Print Settings with these values, for the slicer components.");
            outputs.AddText("Info", "I", "What is set.");
        }

        protected override void Process(IDataAccess access)
        {
            var s = Io.Settings(access, 0).Duplicate();
            var said = new List<string>();
            s.RoleSpeeds = s.RoleSpeeds ?? new Dictionary<PathRole, double>();
            for (int k = 0; k < Roles.Length; k++)
            {
                access.GetItem(1 + k, out double v);
                if (v < 0) v = 0;
                if (v > 1000) access.AddWarning("Speed unit", Inv.F($"{v:0} mm/s is {v * 60:0} mm/min. Speeds are entered in mm/s."));
                if (v > 0) { s.RoleSpeeds[Roles[k]] = v * 60.0; said.Add(Inv.F($"{Roles[k].ToString().ToLowerInvariant()} {v:0.#} mm/s")); }
                else s.RoleSpeeds.Remove(Roles[k]);
            }
            if (s.RoleSpeeds.TryGetValue(PathRole.Wall, out double wall)) s.RoleSpeeds[PathRole.Seam] = wall; else s.RoleSpeeds.Remove(PathRole.Seam);
            if (s.RoleSpeeds.Count == 0) s.RoleSpeeds = null;
            access.GetItem(6, out double coast); access.GetItem(7, out double prime); access.GetItem(8, out double lift);
            s.Coasting = Math.Max(0, coast); s.Prime = Math.Max(0, prime); s.TravelLift = Math.Max(0, lift);
            if (s.Coasting > 5 * s.BeadWidth) access.AddWarning("Coasting", Inv.F($"Coasting {s.Coasting:0.#} mm is more than five bead widths: the end of every line will be thin."));
            if (s.Coasting > 0) said.Add(Inv.F($"coasting {s.Coasting:0.#} mm"));
            if (s.Prime > 0) said.Add(Inv.F($"prime {s.Prime:0.#} mm"));
            if (s.TravelLift > 0) said.Add(Inv.F($"travel lift {s.TravelLift:0.#} mm"));
            access.SetItem(0, s);
            access.SetItem(1, said.Count == 0 ? "Nothing changed: everything as Print Settings." : "Print Tuning: " + string.Join(", ", said) + ".");
        }
    }
}
