using System;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityBBoxComponent : GH_Component
    {
                public CncUtilityBBoxComponent()
                    : base("CNC Utilities 02 Dimensions", "CU_02",
                                                        "Calculates dimensions and bounding box of the oriented solid.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Solid", "Solid", "Solid from CNC Utilities 01 Orient Part", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Bounding Box", "BBox", "Bounding box of the oriented solid", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width X", "W", "Block width in X", GH_ParamAccess.item);
            pManager.AddNumberParameter("Depth Y", "D", "Block depth in Y", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height Z", "H", "Block height in Z", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep solid = null;
            if (!DA.GetData(0, ref solid))
                return;

            if (solid == null || !solid.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The oriented solid is invalid.");
                return;
            }

            BoundingBox bb = solid.GetBoundingBox(true);
            if (!bb.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bounding box could not be computed.");
                return;
            }

            DA.SetData(0, new Box(bb));
            DA.SetData(1, bb.Max.X - bb.Min.X);
            DA.SetData(2, bb.Max.Y - bb.Min.Y);
            DA.SetData(3, bb.Max.Z - bb.Min.Z);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityBBoxIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E02");
    }
}
