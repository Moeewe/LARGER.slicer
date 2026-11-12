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
    /// Zigzag fill of a region as one line.
    /// Rewritten on the fill engine of the Slicer tab: one line, bead width apart, no crossings.
    /// </summary>
    public class InfillLinesComponent : BottomLayerPatternBase
    {
        public InfillLinesComponent()
            : base("Curve Fill Zigzag", "C Zigzag",
                  "Fills a region with one zigzag line at a chosen angle. The curve is the outside of the print; lines are one print width apart. Best for simple shapes - on shapes with bays or holes use Single Line Fill with Offsets.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Angle", "Angle", "Direction of the fill lines in degrees (0 = along X, 90 = along Y)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Optimize Order", "Optimize", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!ValidateInputs(DA, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double angle = 0.0;
            DA.GetData(3, ref angle);
            var fill = FillEngine.FillRegion(boundary, holes, printWidth, false, angle, false, 0.0, null);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillLinesIcon.png");
        public override Guid ComponentGuid => new Guid("6dbfb0be-af59-4e15-b669-d669d57e6de3");
    }
}
