using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontBBoxComponent : GH_Component
    {
        public ThekenfrontBBoxComponent()
          : base("TH BBox", "TH_02",
              "Ermittelt Axis-Aligned Bounding Box und Grundmasse des ausgerichteten Solids.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Solid", "OS", "Aus Modul TH_01", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Bounding Box", "BB", "Axis-Aligned Bounding Box", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width X", "X", "Laenge/Breite in X", GH_ParamAccess.item);
            pManager.AddNumberParameter("Depth Y", "Y", "Tiefe in Y", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height Z", "Z", "Hoehe in Z", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep solid = null;
            if (!DA.GetData(0, ref solid))
                return;

            if (solid == null || !solid.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ungueltiges Solid.");
                return;
            }

            BoundingBox bb = solid.GetBoundingBox(true);
            if (!bb.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bounding Box ungueltig.");
                return;
            }

            DA.SetData(0, new Box(bb));
            DA.SetData(1, bb.Max.X - bb.Min.X);
            DA.SetData(2, bb.Max.Y - bb.Min.Y);
            DA.SetData(3, bb.Max.Z - bb.Min.Z);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E02");
    }
}
