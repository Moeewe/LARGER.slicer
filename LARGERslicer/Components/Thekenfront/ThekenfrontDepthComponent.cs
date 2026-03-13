using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontDepthComponent : GH_Component
    {
        public ThekenfrontDepthComponent()
          : base("TH Depth", "TH_04",
              "Berechnet Tiefenstufen je Brett/Bretthaelfte aus dem ausgerichteten Solid.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Solid", "OS", "Aus TH_01", GH_ParamAccess.item);
            pManager.AddGenericParameter("Split Boards", "SB", "ThekenBoard-Liste", GH_ParamAccess.list);
            pManager.AddNumberParameter("Depth Base", "DB", "Basistiefe", GH_ParamAccess.item, 150.0);
            pManager.AddNumberParameter("Depth Step", "DS", "Tiefenschritt", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Puffer vorne", "PV", "Puffer vorne", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Puffer hinten", "PH", "Puffer hinten", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Ueberstand", "U", "Ueberstand links/rechts", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards with Depth", "BD", "ThekenBoardWithDepth", GH_ParamAccess.list);
            pManager.AddNumberParameter("Length", "L", "Einheitliche Brettlaenge", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Debug-Infos", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep solid = null;
            var boardsRaw = new List<object>();
            double depthBase = 150;
            double depthStep = 50;
            double pv = 5;
            double ph = 5;
            double overhang = 100;

            if (!DA.GetData(0, ref solid))
                return;
            if (!DA.GetDataList(1, boardsRaw))
                return;
            DA.GetData(2, ref depthBase);
            DA.GetData(3, ref depthStep);
            DA.GetData(4, ref pv);
            DA.GetData(5, ref ph);
            DA.GetData(6, ref overhang);

            if (depthStep <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Depth Step muss > 0 sein.");
                return;
            }

            var boards = new List<ThekenBoard>();
            foreach (var o in boardsRaw)
            {
                if (o is ThekenBoard tb)
                    boards.Add(tb);
            }

            BoundingBox bb = solid.GetBoundingBox(true);
            double length = (bb.Max.X - bb.Min.X) + 2.0 * overhang;

            var result = new List<ThekenBoardWithDepth>();
            var info = new List<string>();

            for (int i = 0; i < boards.Count; i++)
            {
                double rawDepth = GetMaxDepthInZRange(solid, boards[i].ZMin, boards[i].ZMax);
                double need = rawDepth + pv + ph;
                double stepped = StepUp(need, depthBase, depthStep);

                var row = new ThekenBoardWithDepth
                {
                    Board = boards[i],
                    Depth = stepped,
                    Length = length
                };
                result.Add(row);

                info.Add($"{boards[i].Type}: raw={rawDepth:F1} need={need:F1} step={stepped:F1}");
            }

            DA.SetDataList(0, result);
            DA.SetData(1, length);
            DA.SetDataList(2, info);
        }

        private static double GetMaxDepthInZRange(Brep solid, double zMin, double zMax)
        {
            int samples = 5;
            double maxDepth = 0.0;

            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                double z = zMin + (zMax - zMin) * t;
                Plane pl = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
                Curve[] contours = Brep.CreateContourCurves(solid, pl);

                if (contours == null || contours.Length == 0)
                    continue;

                foreach (Curve c in contours)
                {
                    if (c == null)
                        continue;

                    BoundingBox cb = c.GetBoundingBox(true);
                    if (!cb.IsValid)
                        continue;

                    double d = cb.Max.Y - cb.Min.Y;
                    if (d > maxDepth)
                        maxDepth = d;
                }
            }

            return maxDepth;
        }

        private static double StepUp(double value, double baseDepth, double step)
        {
            if (value <= baseDepth)
                return baseDepth;

            double n = Math.Ceiling((value - baseDepth) / step);
            return baseDepth + n * step;
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E05");
    }
}
