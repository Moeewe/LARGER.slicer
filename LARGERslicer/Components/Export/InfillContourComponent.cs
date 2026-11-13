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
    /// Contour fill (connected Fermat spiral) of a region as one line.
    /// Rewritten on the fill engine of the Slicer tab: one line, bead width apart, no crossings.
    /// </summary>
    public class InfillContourComponent : BottomLayerPatternBase
    {
        public InfillContourComponent()
            : base("Curve Fill Contour", "C Contour",
                  "Fills a region with one line that follows the outline inwards (connected Fermat spiral). Works on shapes with bays and holes; the line never crosses itself. The curve is the outside of the print.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer · Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Use ArcWelder", "ArcWelder", "No longer used: the output is a polyline; use Round Corners (Slicer tab) for smooth corners. Kept so that old definitions still open.", GH_ParamAccess.item, false);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!ValidateInputs(DA, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            var fill = FillEngine.FillRegion(boundary, holes, printWidth, true, 0.0, false, 0.0, null);
            if (fill.Path == null || fill.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, fill.Info.Length > 0 ? fill.Info : "No fill could be made.");
                return;
            }
            if (fill.Links.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Links.Count + " link(s) join separate parts of the fill across open space. The contour fill (Single Line Fill with Offsets) avoids this on shapes with bays or holes.");
            if (fill.Note.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Note);
            Message = (fill.Path.Length / 1000.0).ToString("0.00") + " m";
            DA.SetData(0, new PolylineCurve(fill.Path));
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillContourIcon.png", this);
        public override Guid ComponentGuid => new Guid("a7b8c9d0-e1f2-3456-7890-123456789abc");
    }
}
