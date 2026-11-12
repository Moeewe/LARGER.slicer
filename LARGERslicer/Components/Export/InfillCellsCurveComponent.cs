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
    /// Wall plus honeycomb or grid cells of one region as a single closed line that never crosses
    /// itself (offset the cells, join the loops by bridges along a spanning tree).
    /// </summary>
    public class InfillCellsCurveComponent : BottomLayerPatternBase
    {
        public InfillCellsCurveComponent()
            : base("Curve Fill Cells", "C Cells",
                  "One closed print line made of the wall and honeycomb or grid cells. Every cell is a loop; neighbouring loops lie one print width apart and fuse into double walls; short bridges join everything into a single line that never crosses itself. The curve is the outside of the print.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddIntegerParameter("Pattern", "P", "0 = Honeycomb, 1 = Grid", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Cell Size", "Size", "Distance between the middles of two neighbouring cells (mm)", GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Angle", "Angle", "Turns the pattern (degrees)", GH_ParamAccess.item, 0.0);
            pManager.AddPointParameter("Origin", "O", "Fixed point of the pattern (middle of one cell), so the cells line up from layer to layer. Empty = start of the plane of the curve.", GH_ParamAccess.item);
            pManager[6].Optional = true;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!ValidateInputs(DA, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            int pattern = 0; double size = 60, angle = 0; Point3d origin = Point3d.Unset;
            DA.GetData(3, ref pattern); DA.GetData(4, ref size); DA.GetData(5, ref angle);
            bool hasOrigin = DA.GetData(6, ref origin) && origin.IsValid;
            var fill = FillEngine.FillCells(boundary, holes, printWidth, pattern, size, angle, hasOrigin ? (Point3d?)origin : null);
            if (fill.Path == null || fill.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, fill.Info.Length > 0 ? fill.Info : "No fill could be made.");
                return;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Info);
            Message = (fill.Path.Length / 1000.0).ToString("0.00") + " m";
            DA.SetData(0, new PolylineCurve(fill.Path));
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillCellsIcon.png", this);
        public override Guid ComponentGuid => new Guid("0538FD7B-6E07-4279-9521-4D79F007FF3C");
    }
}
