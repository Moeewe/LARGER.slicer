using System;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    // ============================================================== 5c Non-Planar Check
    /// <summary>
    /// Checks a non-planar path for a printer whose nozzle stays vertical: the real bead height
    /// against the extrusion, and whether the head hits what was printed before.
    /// </summary>
    public class NonPlanarCheckComponent : SlicerComponent
    {
        public NonPlanarCheckComponent()
            : base("Non-Planar Check", "NP Check",
                  "Checks a non-planar path for a printer whose nozzle stays vertical. Bead height: the vertical gap to the layer below is measured at every point and compared with the bead width, the layer height and the height the extrusion was worked out for. Head: the nozzle is a cone with a wider head above it; nothing printed before may reach into it.") { }

        public override Guid ComponentGuid => new Guid("8E2DA5D2-C15D-4F27-BB61-A6037ABED240");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerNonPlanarCheckIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            p.AddNumberParameter("Tip Radius", "r0", "Radius of the nozzle tip (mm)", GH_ParamAccess.item, 4.0);
            p.AddNumberParameter("Cone Angle", "A", "Half angle of the nozzle cone, from vertical (degrees)", GH_ParamAccess.item, 30.0);
            p.AddNumberParameter("Cone Length", "Lc", "Height of the cone from the tip up to the head (mm)", GH_ParamAccess.item, 60.0);
            p.AddNumberParameter("Head Radius", "R", "Radius of the head above the cone (mm)", GH_ParamAccess.item, 40.0);
            p.AddNumberParameter("Pose", "P", "Where the head is shown along the path: 0 = start, 1 = end", GH_ParamAccess.item, 1.0);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 5, "Pose (0 = start, 1 = end)", 0m, 1m, 1m, 3, "", new double[] { 0, 0.25, 0.5, 0.75, 1 });
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "OK", "True if the bead heights fit and the head hits nothing", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was checked and what was found", GH_ParamAccess.list);
            p.AddNumberParameter("Bead Height", "H", "Measured bead height (vertical gap to the layer below) for every path point (mm); 0 = not measured. Use it to colour the path", GH_ParamAccess.list);
            p.AddPointParameter("Bad Heights", "Bh", "Points where the bead is higher than 0.8 x bead width or thinner than 0.4 x layer height", GH_ParamAccess.list);
            p.AddPointParameter("Collisions", "X", "Points where the head hits beads printed before, or the path climbs more steeply than the cone. Must be empty", GH_ParamAccess.list);
            p.AddMeshParameter("Head", "Hd", "The head as it is checked (tip, cone, body), standing on the path at Pose. Shows in the viewport", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) return;
            var head = new NonPlanarCheck.Head();
            da.GetData(1, ref head.TipRadius); da.GetData(2, ref head.HalfAngle); da.GetData(3, ref head.ConeLength); da.GetData(4, ref head.Radius);
            if (head.TipRadius < 0 || head.HalfAngle < 0 || head.HalfAngle >= 90 || head.ConeLength <= 0 || head.Radius <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tip Radius must be 0 or more, Cone Angle between 0 and 90 degrees, Cone Length and Head Radius more than 0.");
                return;
            }
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The path has tilted tool axes. This check is for a vertical nozzle.");
            var r = NonPlanarCheck.Run(tp, head);
            Message = r.Ok ? "OK" : "Check!";
            if (!r.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.First(l => l.StartsWith("HEAD") || l.StartsWith("TOO") || l.StartsWith("BEAD") || l.StartsWith("EXTRUSION") || l.StartsWith("Empty")));
            da.SetData(0, r.Ok);
            da.SetDataList(1, r.Report);
            da.SetDataList(2, r.BeadHeights);
            da.SetDataList(3, r.BadHeights);
            da.SetDataList(4, r.Collisions);
            if (Params.Output.Count > 5)
            {
                double pose = 1; if (Params.Input.Count > 5) da.GetData(5, ref pose);
                int at = (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (tp.Count - 1));
                da.SetData(5, head.ToMesh(tp.Points[at]));
            }
        }
    }
}
