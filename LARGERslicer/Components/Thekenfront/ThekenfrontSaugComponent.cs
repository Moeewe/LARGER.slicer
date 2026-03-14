using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontSaugComponent : GH_Component
    {
                public ThekenfrontSaugComponent()
                    : base("Thekenfront 06 Saugelemente", "TH_06",
                                                        "Erzeugt linkes und rechtes Saugelement mit passender Treppenkontur.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Bretter", "Bretter", "Bretter aus Thekenfront 05 Verleimblock", GH_ParamAccess.list);
            pManager.AddNumberParameter("Outline-Versatz", "Versatz", "Rueckversatz der Kontur in mm", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Vereinfachung", "Toleranz", "Vereinfachungstoleranz der Treppenkontur in mm", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Saugelement-Breite", "Breite", "Breite des Saugelements in mm", GH_ParamAccess.item, 200.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Saugelement links", "Links", "Linkes Saugelement", GH_ParamAccess.item);
            pManager.AddBrepParameter("Saugelement rechts", "Rechts", "Rechtes Saugelement", GH_ParamAccess.item);
            pManager.AddCurveParameter("Kontur links", "Kontur L", "Vereinfachte Treppenkontur links", GH_ParamAccess.item);
            pManager.AddCurveParameter("Kontur rechts", "Kontur R", "Vereinfachte Treppenkontur rechts", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boards = new List<Brep>();
            double outlineDepth = 5;
            double tol = 2;
            double width = 200;

            if (!DA.GetDataList(0, boards))
                return;
            DA.GetData(1, ref outlineDepth);
            DA.GetData(2, ref tol);
            DA.GetData(3, ref width);

            if (boards.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Keine Bretter vorhanden.");
                return;
            }

            BoundingBox bb = BoundingBox.Empty;
            var boardBoxes = new List<BoundingBox>();
            foreach (var b in boards)
            {
                var bx = b.GetBoundingBox(true);
                boardBoxes.Add(bx);
                bb.Union(bx);
            }

            boardBoxes.Sort((a, b) => a.Min.Z.CompareTo(b.Min.Z));

            Polyline left = BuildStepPolyline(boardBoxes, bb.Min.X);
            Polyline right = BuildStepPolyline(boardBoxes, bb.Max.X);

            Curve leftCurve = left.ToPolylineCurve().Simplify(CurveSimplifyOptions.All, tol, tol) ?? left.ToPolylineCurve();
            Curve rightCurve = right.ToPolylineCurve().Simplify(CurveSimplifyOptions.All, tol, tol) ?? right.ToPolylineCurve();

            BoundingBox ol = leftCurve.GetBoundingBox(true);
            BoundingBox orr = rightCurve.GetBoundingBox(true);

            var leftPlane = new Plane(new Point3d(bb.Min.X, ol.Min.Y - outlineDepth, ol.Min.Z), Vector3d.XAxis, Vector3d.YAxis);
            var rightPlane = new Plane(new Point3d(bb.Max.X - width, orr.Min.Y - outlineDepth, orr.Min.Z), Vector3d.XAxis, Vector3d.YAxis);

            Box leftBox = new Box(leftPlane,
                new Interval(0, width),
                new Interval(0, Math.Max(1.0, ol.Max.Y - ol.Min.Y)),
                new Interval(0, Math.Max(1.0, ol.Max.Z - ol.Min.Z)));

            Box rightBox = new Box(rightPlane,
                new Interval(0, width),
                new Interval(0, Math.Max(1.0, orr.Max.Y - orr.Min.Y)),
                new Interval(0, Math.Max(1.0, orr.Max.Z - orr.Min.Z)));

            DA.SetData(0, leftBox.ToBrep());
            DA.SetData(1, rightBox.ToBrep());
            DA.SetData(2, leftCurve);
            DA.SetData(3, rightCurve);
        }

        private static Polyline BuildStepPolyline(List<BoundingBox> boardBoxes, double xSide)
        {
            var pts = new List<Point3d>();
            if (boardBoxes.Count == 0)
                return new Polyline();

            double yFront = boardBoxes[0].Min.Y;
            pts.Add(new Point3d(xSide, yFront, boardBoxes[0].Min.Z));

            foreach (var b in boardBoxes)
            {
                pts.Add(new Point3d(xSide, b.Max.Y, b.Min.Z));
                pts.Add(new Point3d(xSide, b.Max.Y, b.Max.Z));
            }

            var top = boardBoxes[boardBoxes.Count - 1].Max.Z;
            pts.Add(new Point3d(xSide, yFront, top));
            pts.Add(new Point3d(xSide, yFront, boardBoxes[0].Min.Z));

            return new Polyline(pts);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E07");
    }
}
