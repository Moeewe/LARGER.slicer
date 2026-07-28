using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class FastDeconstructPointComponent : GH_Component
    {
        public FastDeconstructPointComponent()
          : base("Fast Deconstruct Point", "FastPt",
              "Ultra-fast deconstruction of points into X, Y and Z values.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Points to deconstruct", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("X", "X", "X values", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y", "Y", "Y values", GH_ParamAccess.list);
            pManager.AddNumberParameter("Z", "Z", "Z values", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var points = new List<Point3d>();
            if (!DA.GetDataList(0, points) || points.Count == 0)
            {
                DA.SetDataList(0, Array.Empty<double>());
                DA.SetDataList(1, Array.Empty<double>());
                DA.SetDataList(2, Array.Empty<double>());
                return;
            }

            int count = points.Count;
            var xs = new double[count];
            var ys = new double[count];
            var zs = new double[count];

            for (int i = 0; i < count; i++)
            {
                Point3d p = points[i];
                xs[i] = p.X;
                ys[i] = p.Y;
                zs[i] = p.Z;
            }

            DA.SetDataList(0, xs);
            DA.SetDataList(1, ys);
            DA.SetDataList(2, zs);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("FastDeconstructPointIcon.png");
        public override Guid ComponentGuid => new Guid("7D2E3A7B-5E54-4A3A-B9F3-80A26D77F83C");
    }
}
