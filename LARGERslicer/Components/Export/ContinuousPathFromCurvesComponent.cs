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
    /// Wall loop plus zigzag infill as one line.
    /// Rewritten on the fill engine of the Slicer tab: one line, bead width apart, no crossings.
    /// </summary>
    public class ContinuousPathFromCurvesComponent : BottomLayerPatternBase
    {
        public ContinuousPathFromCurvesComponent()
            : base("Curve Wall and Zigzag", "C Wall",
                  "One continuous print line from a boundary: one wall loop along the outline, then a zigzag infill at the chosen angle inside it. The curve is the outside of the print.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Angle", "Angle", "Direction of the infill lines in degrees (0 = along X)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Random Bridges", "Random", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Bridge Density", "BridgeD", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, 0.3);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!ValidateInputs(DA, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double angle = 0.0;
            DA.GetData(3, ref angle);
            var fill = FillEngine.FillRegion(boundary, holes, printWidth, false, angle, true, 0.0, null);
            if (fill.Path == null || fill.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, fill.Info.Length > 0 ? fill.Info : "No fill could be made.");
                return;
            }
            if (fill.Links.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Links.Count + " link(s) join separate parts of the fill across open space. The contour fill (Single Line Fill with Offsets) avoids this on shapes with bays or holes.");
            if (holes.Count > 0 && fill.Note.Length == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Holes are left open but get no wall of their own here. Continuous Toolpath prints a wall around holes.");
            if (fill.Note.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Note);
            Message = (fill.Path.Length / 1000.0).ToString("0.00") + " m";
            DA.SetData(0, new PolylineCurve(fill.Path));
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousPathFromCurvesIcon.png", this);
        public override Guid ComponentGuid => new Guid("3f6dfdae-b1a3-4a7e-b8b3-981ee021245f");
    }
}
