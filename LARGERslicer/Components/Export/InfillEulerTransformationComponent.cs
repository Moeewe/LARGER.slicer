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
    /// Single-stroke fill of a region.
    /// Rewritten on the fill engine of the Slicer tab: one line, bead width apart, no crossings.
    /// </summary>
    public class InfillEulerTransformationComponent : BottomLayerPatternBase
    {
        public InfillEulerTransformationComponent()
            : base("Curve Fill Euler", "C Euler",
                  "Fills a region with one line that can be drawn without lifting the pen. Concentric = true follows the outline inwards, false prints a wall loop and a zigzag inside. Offset Distance moves the first bead further inside.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer · Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Offset Distance", "Offset", "Distance of the first bead from the curve (mm). Half the print width puts the outside of the print on the curve.", GH_ParamAccess.item, 2.5);
            pManager.AddBooleanParameter("Patch Odd Vertices", "Patch", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Use Concentric Traversal", "Concentric", "True: follow the outline inwards. False: wall loop and zigzag.", GH_ParamAccess.item, true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!ValidateInputs(DA, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double offset = 2.5; bool concentric = true;
            DA.GetData(3, ref offset); DA.GetData(5, ref concentric);
            var fill = FillEngine.FillRegion(boundary, holes, printWidth, concentric, 0.0, !concentric, Math.Max(0.0, offset - 0.5 * printWidth), null);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillEulerTransformationIcon.png", this);
        public override Guid ComponentGuid => new Guid("5dbd79e8-a7eb-47bd-8f62-11b41f6bfaa8");
    }
}
