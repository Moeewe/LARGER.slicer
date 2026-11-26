using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Path Planner: the whole print path of a part from one component, planned by the optional
    /// planner library (see PlannerCore). Without the library the component says so and does nothing.
    /// </summary>
    public class PathPlannerComponent : SlicerComponent
    {
        public PathPlannerComponent()
            : base("Path Planner", "Plan",
                  "Model in, planned print path out. The planner builds the path of the whole part in several ways (where the seam lies, how the solid layers are filled and entered), measures each one (crossings, material laid twice, unfilled places, travel, time) and hands out the best; the others come out in Variants. Skirt, brim, the way from layer to layer and the exit are part of the path: Start and End is NOT needed after it. Connect Toolpath to Path Check, Machine G-code, Robot Program or Printer Simulation. Bead, layer, speeds, nozzle, overlap and machine profile come from Print Settings. Needs the planner library, an optional file that is not part of the public plug-in; without it the component says so.") { }

        public override Guid ComponentGuid => new Guid("6B0E4C57-93A1-4D2E-8F36-1A5C7E9B0D42");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPlannerIcon.png", this);

        private static string Numbered(string[] names) => string.Join("\n", names.Select((n, i) => i + " = " + n));

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Geometry", "G", "Your part: Breps, surfaces, SubDs or meshes, where it stands on the bed (it is not moved)", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings: bead width, layer height, first layer, speeds, nozzle, overlap and the machine profile come from here. Empty = the house settings (bead 5, layer 2)", GH_ParamAccess.item);
            p.AddIntegerParameter("Walls", "N", "Walls side by side, one line", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Wall Position", "WP", "Where the walls lie:\n" + Numbered(PlannerOptions.WallPositions), GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Bottom Layers", "B", "Solid layers at the bottom. 0 = open (hulls, vases). An even number prints without a travel move", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Top Layers", "T", "Solid layers under the top of the part. They need something to lie on", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Fill", "F", "How solid layers are filled:\n" + Numbered(PlannerOptions.Fills) + "\nThe planner also tries the others and lists them in Scores", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Infill", "I", "Infill along the wall, part of the one line:\n" + Numbered(PlannerOptions.Infills), GH_ParamAccess.item, 0);
            p.AddNumberParameter("Infill Size", "Sz", "Rib distance or wave length (mm). 0 = chosen from the bead width", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Skirt Loops", "Sk", "Loops around the part before it starts. 0 = none", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Brim", "Br", "Beads right against the first layer, so the part holds on to the bed:\n" + Numbered(PlannerOptions.Brims), GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Layers", "L", "The kind of layers:\n" + Numbered(PlannerOptions.LayerKinds) + "\nFollow ... = non-planar, the nozzle stays vertical. Spiral = vase mode (one wall, no infill)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Angle", "A", "Flat layers only: slice angle in degrees. 45 = layers leaning 45 degrees (robot or tilted bed)", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Seam", "Se", "Where the layers change:\n" + Numbered(PlannerOptions.Seams), GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Goal", "Go", "What the best path is:\n0 = Best (balanced)\n1 = Fast (shortest time, least travel)\n2 = Fill (least unfilled area in solid layers)\n3 = Seam (hidden in a corner and stacked)", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Variants", "V", "How many paths come out in Variants (the best first)", GH_ParamAccess.item, 1);
            p.AddBooleanParameter("Strategy Auto", "Auto", "The planner compares the kinds of layers that suit the part and the machine and takes the cheapest (Analysis lists them). Layers and Angle are then not used", GH_ParamAccess.item, false);
            p.AddPointParameter("Seam Point", "SP", "Optional. The seam goes to the place on the wall nearest to this point (Seam = Auto or At Seam Point)", GH_ParamAccess.item);
            p[0].Optional = true; // an empty input shows "Connect your part" instead of a collect error
            p[1].Optional = true;
            p[17].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddCount(doc, 2, "Walls", 1, 6, 1);
            AddValueList(doc, 3, "Wall Position", PlannerOptions.WallPositions);
            AddCount(doc, 4, "Bottom Layers", 0, 10, 0);
            AddCount(doc, 5, "Top Layers", 0, 10, 0);
            AddValueList(doc, 6, "Fill", PlannerOptions.Fills);
            AddValueList(doc, 7, "Infill", PlannerOptions.Infills);
            AddSlider(doc, 8, "Infill Size", 0, 500, 0, 0, "mm", new double[] { 0, 30, 40, 60, 80, 100, 150, 200 });
            AddCount(doc, 9, "Skirt Loops", 0, 5, 1);
            AddValueList(doc, 10, "Brim", PlannerOptions.Brims);
            AddValueList(doc, 11, "Layers", PlannerOptions.LayerKinds);
            AddAngle(doc, 12, "Angle", 0, 90, 0);
            AddValueList(doc, 13, "Seam", PlannerOptions.Seams);
            AddValueList(doc, 14, "Goal", PlannerOptions.Goals);
            AddCount(doc, 15, "Variants", 1, 8, 1);
            AddToggle(doc, 16, "Strategy Auto", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The best path, complete with skirt, brim and exit: connect to Path Check, Machine G-code, Robot Program or Printer Simulation (no Start and End in between)", GH_ParamAccess.item);
            p.AddCurveParameter("Path", "P", "The best path as a curve", GH_ParamAccess.item);
            p.AddBooleanParameter("OK", "OK", "True if the planner's check finds the path printable", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was planned, what the check found and what to do about it", GH_ParamAccess.list);
            p.AddGenericParameter("Variants", "Var", "The paths as Toolpaths, the best first (as many as Variants asks for)", GH_ParamAccess.list);
            p.AddTextParameter("Names", "Nm", "What each variant is best at (best, fill, fast, seam) and the choices it was built with", GH_ParamAccess.list);
            p.AddTextParameter("Scores", "Sc", "All paths the planner built, side by side: a table of their measures", GH_ParamAccess.item);
            p.AddTextParameter("Analysis", "An", "The geometry analysis of the part (and with Strategy Auto the kinds of layers that were compared)", GH_ParamAccess.item);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            if (PlannerCore.PluginFolder == null)
            {
                try { string loc = Grasshopper.Instances.ComponentServer.FindAssemblyByObject(ComponentGuid)?.Location; if (!string.IsNullOrEmpty(loc)) PlannerCore.PluginFolder = System.IO.Path.GetDirectoryName(loc); } catch { }
            }
            if (!PlannerCore.Available)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, PlannerCore.Why);
                Message = "Planner not installed";
                da.SetData(2, false); da.SetDataList(3, new[] { PlannerCore.Why });
                return;
            }
            var geoIn = new List<object>();
            if (!da.GetDataList(0, geoIn) || geoIn.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Connect your part to Geometry: a Brep, surface, SubD or mesh, where it stands on the bed."); Message = "Connect a part"; return; }
            var settings = (SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings()).Duplicate();
            var o = new PlannerOptions();
            bool auto = false; Point3d seamPoint = Point3d.Unset;
            da.GetData(2, ref o.Walls); da.GetData(3, ref o.WallPosition); da.GetData(4, ref o.BottomLayers); da.GetData(5, ref o.TopLayers);
            da.GetData(6, ref o.Fill); da.GetData(7, ref o.Infill); da.GetData(8, ref o.InfillSize); da.GetData(9, ref o.SkirtLoops); da.GetData(10, ref o.Brim);
            da.GetData(11, ref o.Layers); da.GetData(12, ref o.Angle); da.GetData(13, ref o.Seam); da.GetData(14, ref o.Goal); da.GetData(15, ref o.Variants);
            da.GetData(16, ref auto); o.StrategyAuto = auto;
            if (Params.Input.Count > 17 && da.GetData(17, ref seamPoint) && seamPoint.IsValid) o.SeamPoint = seamPoint;
            else if (o.Seam == 3) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Seam = At Seam Point, but Seam Point is empty: the seam goes to the longest side.");
            if (settings.BeadWidth <= 0 || settings.LayerHeight <= 0 || settings.Speed <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bead width, layer height and speed of the Print Settings must be greater than 0."); return; }
            // copies for the background: the geometry stays untouched where it came from
            var geo = LARGERslicer.Components.AsyncSolve.Copy(geoIn);

            Async.Run(da, job =>
            {
                var log = new List<string>();
                bool bent = o.Layers >= 1 && o.Layers <= 3;
                Mesh mesh = SliceEngine.ToMesh(geo, Math.Max(0.01, settings.Preview ? settings.Resolution * 0.25 : settings.Resolution * 0.025), log, bent ? Math.Max(4.0, settings.BeadWidth * 1.5) : 0);
                if (mesh == null) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable geometry. Connect Breps, surfaces, SubDs or meshes."); return; }
                var r = PlannerCore.Run(mesh, settings, o, job.Token);
                if (job.Token.IsCancellationRequested) return;
                var report = r.ReportLines();
                foreach (string l in log) report.Add("Note: " + l);
                string analysis = r.Strategies.Length > 0 ? r.Strategies + "\n" + r.Analysis : r.Analysis;
                if (r.Error != null)
                {
                    job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, r.Error);
                    job.Message = "No path";
                    job.SetData(2, false); job.SetDataList(3, report); job.SetData(7, analysis);
                    return;
                }
                var best = r.Best;
                if (!best.Ok) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The check found something: read Report. " + (best.Lines("check").FirstOrDefault(l => l.Length > 3 && l.Substring(0, 3) == l.Substring(0, 3).ToUpperInvariant() && char.IsLetter(l[0])) ?? ""));
                job.Message = (best.Ok ? "Printable" : "Read the report") + (r.Variants.Count > 1 ? "\n" + r.Variants.Count + " variants" : "");
                job.SetData(0, new GH_Toolpath(best.Path));
                job.SetData(1, new PolylineCurve(best.Path.Points));
                job.SetData(2, best.Ok);
                job.SetDataList(3, report);
                job.SetDataList(4, r.Variants.Select(v => new GH_Toolpath(v.Path)));
                job.SetDataList(5, r.Variants.Select(v => v.Name + (v.Choices.Length > 0 ? ": " + v.Choices : "") + (v.Ok ? "" : " (not printable)")));
                job.SetData(6, r.Table);
                job.SetData(7, analysis);
            });
        }
    }
}
