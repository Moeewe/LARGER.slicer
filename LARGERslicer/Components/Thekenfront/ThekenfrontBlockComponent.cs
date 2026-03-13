using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontBlockComponent : GH_Component
    {
        public ThekenfrontBlockComponent()
          : base("TH Block", "TH_05",
              "Erzeugt gestapelte Brettgeometrien und Containerbox.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Solid", "OS", "Referenz-Solid", GH_ParamAccess.item);
            pManager.AddGenericParameter("Boards with Depth", "BD", "ThekenBoardWithDepth", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ueberstand", "U", "Ueberstand links/rechts", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Boards", "B", "Bretter/Bretthaelften als Breps", GH_ParamAccess.list);
            pManager.AddBrepParameter("Container", "C", "Containerbox", GH_ParamAccess.item);
            pManager.AddBrepParameter("Reference", "R", "Referenz-Solid", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep refSolid = null;
            var dataRaw = new List<object>();
            double overhang = 100;

            if (!DA.GetData(0, ref refSolid))
                return;
            if (!DA.GetDataList(1, dataRaw))
                return;
            DA.GetData(2, ref overhang);

            var rows = new List<ThekenBoardWithDepth>();
            foreach (var o in dataRaw)
            {
                if (o is ThekenBoardWithDepth row)
                    rows.Add(row);
            }

            BoundingBox sbb = refSolid.GetBoundingBox(true);
            double x0 = sbb.Min.X - overhang;
            double y0 = sbb.Min.Y;

            var breps = new List<Brep>();
            BoundingBox all = BoundingBox.Empty;

            foreach (var row in rows)
            {
                if (row?.Board == null)
                    continue;

                var plane = new Plane(new Point3d(x0, y0, row.Board.ZMin), Vector3d.XAxis, Vector3d.YAxis);
                var box = new Box(plane,
                    new Interval(0, row.Length),
                    new Interval(0, row.Depth),
                    new Interval(0, row.Board.Thickness));

                Brep b = box.ToBrep();
                if (b != null)
                {
                    breps.Add(b);
                    all.Union(b.GetBoundingBox(true));
                }
            }

            Brep container = all.IsValid ? new Box(all).ToBrep() : null;

            DA.SetDataList(0, breps);
            DA.SetData(1, container);
            DA.SetData(2, refSolid);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E06");
    }
}
