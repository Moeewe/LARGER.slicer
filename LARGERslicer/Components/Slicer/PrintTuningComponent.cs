using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// What a pellet extruder needs beyond bead and speed: each kind of line at its own speed, the extruder stopped a
    /// little early before a stop (the screw runs on), a full bead at the start after a stop, and the nozzle lifted for
    /// travel moves. Goes between Print Settings and the slicer components; everything 0 = as Print Settings.
    /// </summary>
    public class PrintTuningComponent : SlicerComponent
    {
        public PrintTuningComponent()
            : base("Print Tuning", "Tune",
                  "Fine-tunes the Print Settings for pellet extruders. Each kind of line at its own speed (walls, infill, bottom, skirt, support; 0 = the print speed). Coasting stops the extruder this far before every stop or travel move, because the screw runs on and would leave a blob. Prime gives the first move after a stop the material of this much bead extra, so the bead starts full. Travel Lift lifts the nozzle for travel moves on G-code machines. Put it between Print Settings and the slicer components. Machine G-code, the DXR and the KUKA program follow it.") { }

        public override Guid ComponentGuid => new Guid("5B7E2D19-8C43-4A6F-9E15-3D8C1F0A7B62");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerTuningIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddNumberParameter("Wall Speed", "Vw", "Speed of the walls in mm/s. 0 = the print speed", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Infill Speed", "Vi", "Speed of ribs, cells, waves and chambers in mm/s. 0 = the print speed", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Bottom Speed", "Vb", "Speed of the solid bottom and top layers in mm/s. 0 = the print speed", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Skirt Speed", "Vs", "Speed of skirt and brim in mm/s. 0 = the print speed", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Support Speed", "Vf", "Speed of support fins in mm/s. 0 = the print speed", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Coasting", "Co", "The extruder stops this far (mm) before every stop or travel move; the pressure in the screw prints the rest. 0 = off. Pellet extruders: about 1 to 2 bead widths", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Prime", "Pr", "After a stop the first move gets the material of this much bead (mm) extra, so the bead starts full. 0 = off", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Travel Lift", "Lift", "G-code machines: the nozzle is lifted this far (mm) for travel moves and comes down at print speed (Z-hop). 0 = off", GH_ParamAccess.item, 0.0);
            p[0].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            var speeds = new double[] { 0, 10, 20, 30, 50, 80 };
            foreach (var (i, name) in new[] { (1, "Wall Speed"), (2, "Infill Speed"), (3, "Bottom Speed"), (4, "Skirt Speed") })
            {
                var r = AddSlider(doc, i, name, 0, 200, 0, 0, "mm/s", speeds);
                if (r != null) r.ZeroText = () => "print speed";
            }
            var co = AddSlider(doc, 6, "Coasting", 0, 50, 0, 1, "mm", new double[] { 0, 2, 5, 8, 10, 15 });
            if (co != null) co.ZeroText = () => "off";
            var pr = AddSlider(doc, 7, "Prime", 0, 50, 0, 1, "mm", new double[] { 0, 2, 5, 8, 10 });
            if (pr != null) pr.ZeroText = () => "off";
            var li = AddSlider(doc, 8, "Travel Lift", 0, 20, 0, 1, "mm", new double[] { 0, 1, 2, 5, 10 });
            if (li != null) li.ZeroText = () => "off";
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Settings", "S", "The Print Settings with these values, for the slicer components", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "What is set", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var s = (SlicerIO.Get<PrintSettings>(da, 0) ?? new PrintSettings()).Duplicate();
            var roles = new[] { PathRole.Wall, PathRole.Infill, PathRole.Bottom, PathRole.Skirt, PathRole.Support };
            var said = new List<string>();
            s.RoleSpeeds = s.RoleSpeeds ?? new Dictionary<PathRole, double>();
            for (int k = 0; k < roles.Length; k++)
            {
                double v = 0; da.GetData(1 + k, ref v);
                if (v < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Params.Input[1 + k].Name + " cannot be negative; the print speed is used."); v = 0; }
                if (v > 1000) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"{Params.Input[1 + k].Name} {v:0} mm/s is {v * 60:0} mm/min. Speeds are entered in mm/s."));
                if (v > 0) { s.RoleSpeeds[roles[k]] = v * 60.0; said.Add(Inv.F($"{roles[k].ToString().ToLowerInvariant()} {v:0.#} mm/s")); }
                else s.RoleSpeeds.Remove(roles[k]);
            }
            // the seam ramp is part of the wall
            if (s.RoleSpeeds.TryGetValue(PathRole.Wall, out double wall)) s.RoleSpeeds[PathRole.Seam] = wall; else s.RoleSpeeds.Remove(PathRole.Seam);
            if (s.RoleSpeeds.Count == 0) s.RoleSpeeds = null;
            double coast = 0, prime = 0, lift = 0;
            da.GetData(6, ref coast); da.GetData(7, ref prime); da.GetData(8, ref lift);
            s.Coasting = Math.Max(0, coast); s.Prime = Math.Max(0, prime); s.TravelLift = Math.Max(0, lift);
            if (s.Coasting > 5 * s.BeadWidth) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"Coasting {s.Coasting:0.#} mm is more than five bead widths: the end of every line will be thin."));
            if (s.Coasting > 0) said.Add(Inv.F($"coasting {s.Coasting:0.#} mm"));
            if (s.Prime > 0) said.Add(Inv.F($"prime {s.Prime:0.#} mm"));
            if (s.TravelLift > 0) said.Add(Inv.F($"travel lift {s.TravelLift:0.#} mm"));
            Message = said.Count == 0 ? "as Print Settings" : said.Count + " set";
            da.SetData(0, new GH_PrintSettings(s));
            da.SetData(1, said.Count == 0 ? "Nothing changed: everything as Print Settings." : "Print Tuning: " + string.Join(", ", said) + ".");
        }
    }
}
