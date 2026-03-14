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
              "Erzeugt den Verleimblock aus Split-Brettern, Tiefenstufen und Brettlaenge.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Orientiertes Solid", "OS", "Referenz-Solid aus TH_01", GH_ParamAccess.item);
            pManager.AddGenericParameter("Split Bretter", "SB", "Bretter bzw. Bretthaelften aus TH_03b", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tiefen", "T", "Tiefenstufe pro Brett aus TH_04", GH_ParamAccess.list);
            pManager.AddNumberParameter("Brettlaenge", "L", "Einheitliche Brettlaenge aus TH_04", GH_ParamAccess.item);
            pManager.AddNumberParameter("Ueberstand links/rechts", "ULR", "Ueberstand fuer die Positionierung in mm", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Bretter", "B", "Bretter/Bretthaelften als Breps", GH_ParamAccess.list);
            pManager.AddBrepParameter("Containerbox", "C", "Gesamt-Bounding-Box des Verleimblocks", GH_ParamAccess.item);
            pManager.AddBrepParameter("Referenz-Solid", "R", "Orientiertes Referenz-Solid", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep refSolid = null;
            var boardsRaw = new List<object>();
            var depths = new List<double>();
            double boardLength = 0;
            double overhang = 100;

            if (!DA.GetData(0, ref refSolid))
                return;
            if (!DA.GetDataList(1, boardsRaw))
                return;
            if (!DA.GetDataList(2, depths))
                return;
            if (!DA.GetData(3, ref boardLength))
                return;
            DA.GetData(4, ref overhang);

            var boards = new List<ThekenBoard>();
            foreach (var o in boardsRaw)
            {
                if (o is ThekenBoard row)
                    boards.Add(row);
            }

            if (boards.Count != depths.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Anzahl Split Bretter und Tiefen muss uebereinstimmen.");
                return;
            }

            BoundingBox sbb = refSolid.GetBoundingBox(true);
            double x0 = sbb.Min.X - overhang;
            double y0 = sbb.Min.Y;

            var breps = new List<Brep>();
            BoundingBox all = BoundingBox.Empty;

            for (int i = 0; i < boards.Count; i++)
            {
                ThekenBoard row = boards[i];
                double depth = depths[i];

                var plane = new Plane(new Point3d(x0, y0, row.ZMin), Vector3d.XAxis, Vector3d.YAxis);
                var box = new Box(plane,
                    new Interval(0, boardLength),
                    new Interval(0, depth),
                    new Interval(0, row.Thickness));

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
