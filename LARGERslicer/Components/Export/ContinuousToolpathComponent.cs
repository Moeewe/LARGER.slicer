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
    /// Wall and inside of an outline as one continuous line.
    /// Rewritten on the fill engine of the Slicer tab: one line, bead width apart, no crossings.
    /// </summary>
    public class ContinuousToolpathComponent : BottomLayerPatternBase
    {
        public ContinuousToolpathComponent()
            : base("Curve Continuous Path", "C Path",
                  "Turns a closed outline into one continuous print line: the wall first, then the inside following the wall inwards, without lifting and without crossing itself. Holes are walked around.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer · Curves";
        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Random Bridges", "Random", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Bridge Density", "BridgeD", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, 0.3);
            pManager.AddBooleanParameter("Handle Undercuts", "Undercuts", "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.", GH_ParamAccess.item, true);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("ContinuousToolpathIcon.png", this);
        public override Guid ComponentGuid => new Guid("c606afee-06f0-46bf-a5bc-318637743abc");
    }
}
