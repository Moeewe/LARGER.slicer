using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// One continuous fill line for a boundary with a chosen start point (seam).
    /// Rewritten on the fill engine of the Slicer tab: one line, spacing apart, no crossings.
    /// </summary>
    public class ContinuousInfillComponent : GH_Component
    {
        public ContinuousInfillComponent()
            : base("Curve Continuous Infill", "C Infill",
                  "Fills a closed boundary with one continuous line that follows the outline inwards and never crosses itself. The line starts next to the seam point. Holes are walked around.",
                  "LARGER", "Slicer Curves")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "Boundary", "Closed boundary curve to fill. It is the outside of the print.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Spacing", "Spacing", "Distance between the lines = bead width in mm", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Boundary Offset", "Offset", "Additional offset from boundary inward (mm)", GH_ParamAccess.item, 0.0);
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes) to exclude", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager.AddPointParameter("Seam Point", "Seam", "The line starts next to this point. Empty = chosen automatically.", GH_ParamAccess.item);
            pManager[4].Optional = true;
            const string unused = "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.";
            pManager.AddBooleanParameter("Handle Undercuts", "Undercuts", unused, GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Use ArcWelder", "ArcWelder", unused, GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Arc Tolerance", "ArcTol", unused, GH_ParamAccess.item, 0.1);
            pManager.AddNumberParameter("Min Arc Radius", "MinRadius", unused, GH_ParamAccess.item, 0.1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Continuous toolpath as one polyline", GH_ParamAccess.item);
            pManager.AddCurveParameter("Segments", "Segments", "Links across open space, if the fill could not be made in one piece (normally empty)", GH_ParamAccess.list);
            pManager.AddPointParameter("Points", "Points", "All path points", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Path generation information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null; double spacing = 5.0, offset = 0.0; var holes = new List<Curve>(); Point3d seam = Point3d.Unset;
            if (!DA.GetData(0, ref boundary) || boundary == null) return;
            DA.GetData(1, ref spacing); DA.GetData(2, ref offset); DA.GetDataList(3, holes);
            bool hasSeam = DA.GetData(4, ref seam) && seam.IsValid;
            if (spacing <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing must be greater than zero."); return; }
            if (!boundary.IsClosed) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The boundary is open; it is closed with a straight line.");

            var fill = FillEngine.FillRegion(boundary, holes.Where(h => h != null), spacing, true, 0.0, false, Math.Max(0.0, offset), hasSeam ? (Point3d?)seam : null);
            if (fill.Path == null || fill.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, fill.Info.Length > 0 ? fill.Info : "No fill could be made.");
                return;
            }
            if (fill.Note.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Note);
            Message = (fill.Path.Length / 1000.0).ToString("0.00") + " m";
            DA.SetData(0, new PolylineCurve(fill.Path));
            DA.SetDataList(1, fill.Links.Select(l => new LineCurve(l)));
            DA.SetDataList(2, fill.Path);
            DA.SetData(3, fill.Info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousInfillIcon.png", this);
        public override Guid ComponentGuid => new Guid("e1ba197a-3460-4179-a284-f2bf265bd3be");
    }
}
