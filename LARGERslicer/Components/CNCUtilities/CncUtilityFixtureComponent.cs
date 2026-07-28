using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityFixtureComponent : GH_Component
    {
        public CncUtilityFixtureComponent()
                      : base("CNC Utilities 06 Fixtures", "CU_06",
                  "Creates L-shaped fixture elements (stop + base) and cuts insertion pockets into the boards.",
              "LARGER", "CNC Utilities")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Boards", "Boards", "Boards from CNC Utilities 05 Build Raw Block", GH_ParamAccess.list);
            pManager.AddNumberParameter("Insert Depth", "Depth", "How deep the stop is inserted into the boards (mm)", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Stop Thickness", "Thk", "Total thickness of the vertical stop board (mm)", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Base Height", "BaseH", "Thickness of the horizontal base board (mm)", GH_ParamAccess.item, 20.0);
            pManager.AddNumberParameter("Base Overhang", "BaseO", "How far the base extends beyond the stop (mm)", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Left Stop", "Left", "Vertical stop board on the left", GH_ParamAccess.item);
            pManager.AddBrepParameter("Right Stop", "Right", "Vertical stop board on the right", GH_ParamAccess.item);
            pManager.AddCurveParameter("Left Contour", "Cont L", "Left step contour (YZ profile)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Right Contour", "Cont R", "Right step contour (YZ profile)", GH_ParamAccess.item);
            pManager.AddBrepParameter("Left Base", "BaseL", "Horizontal base board on the left", GH_ParamAccess.item);
            pManager.AddBrepParameter("Right Base", "BaseR", "Horizontal base board on the right", GH_ParamAccess.item);
            pManager.AddBrepParameter("Milled Boards", "Boards", "Boards with insertion pockets on both ends", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boards = new List<Brep>();
            double insertDepth = 5;
            double stopThickness = 30;
            double baseHeight = 20;
            double baseExtension = 50;

            if (!DA.GetDataList(0, boards))
                return;
            DA.GetData(1, ref insertDepth);
            DA.GetData(2, ref stopThickness);
            DA.GetData(3, ref baseHeight);
            DA.GetData(4, ref baseExtension);

            if (boards.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No boards provided.");
                return;
            }

            if (insertDepth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Insert depth must be greater than 0.");
                return;
            }

            if (stopThickness <= insertDepth)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stop thickness must be greater than insert depth.");
                return;
            }

            // Bounding boxes aller Bretter sammeln
            BoundingBox allBB = BoundingBox.Empty;
            var boardBoxes = new List<BoundingBox>();
            foreach (var b in boards)
            {
                var bx = b.GetBoundingBox(true);
                boardBoxes.Add(bx);
                allBB.Union(bx);
            }

            double xMin = allBB.Min.X;
            double xMax = allBB.Max.X;
            double yMin = allBB.Min.Y;
            double yMax = allBB.Max.Y;
            double zMin = allBB.Min.Z;
            double zMax = allBB.Max.Z;

            // Pruefen ob alle Bretter lang genug fuer die Taschen sind
            double boardLength = xMax - xMin;
            if (boardLength <= 2.0 * insertDepth)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Boards are shorter than twice the insert depth. Boards must be longer than the insert contour.");
                return;
            }

            // --- Vertikale Anschlagbretter ---
            // Links: insertDepth ragt in die Bretter, Rest steht nach aussen
            double leftOuterX = xMin - (stopThickness - insertDepth);
            double leftInnerX = xMin + insertDepth;

            Box leftStop = new Box(
                Plane.WorldXY,
                new Interval(leftOuterX, leftInnerX),
                new Interval(yMin, yMax),
                new Interval(zMin, zMax));

            // Rechts: gespiegelt
            double rightInnerX = xMax - insertDepth;
            double rightOuterX = xMax + (stopThickness - insertDepth);

            Box rightStop = new Box(
                Plane.WorldXY,
                new Interval(rightInnerX, rightOuterX),
                new Interval(yMin, yMax),
                new Interval(zMin, zMax));

            // --- Horizontale Basisbretter (Aufspannflaeche) ---
            // Links: unter dem Anschlag + Ueberstand nach aussen
            Box leftBase = new Box(
                Plane.WorldXY,
                new Interval(leftOuterX - baseExtension, leftInnerX),
                new Interval(yMin, yMax),
                new Interval(zMin - baseHeight, zMin));

            // Rechts: gespiegelt
            Box rightBase = new Box(
                Plane.WorldXY,
                new Interval(rightInnerX, rightOuterX + baseExtension),
                new Interval(yMin, yMax),
                new Interval(zMin - baseHeight, zMin));

            // --- Bretter mit Taschen (Einstecktiefe abziehen) ---
            // Da alle Bretter einfache Boxen sind, genuegt es sie um
            // die Einstecktiefe an beiden Enden zu kuerzen.
            var milledBoards = new List<Brep>();
            for (int i = 0; i < boards.Count; i++)
            {
                BoundingBox bbb = boardBoxes[i];
                Box shortened = new Box(
                    Plane.WorldXY,
                    new Interval(bbb.Min.X + insertDepth, bbb.Max.X - insertDepth),
                    new Interval(bbb.Min.Y, bbb.Max.Y),
                    new Interval(bbb.Min.Z, bbb.Max.Z));

                Brep sb = shortened.ToBrep();
                if (sb != null)
                    milledBoards.Add(sb);
                else
                    milledBoards.Add(boards[i]);
            }

            // --- Treppenkonturen (zur Kontrolle) ---
            var sortedBoxes = new List<BoundingBox>(boardBoxes);
            sortedBoxes.Sort((a, b) => a.Min.Z.CompareTo(b.Min.Z));

            Polyline leftPoly = BuildStepPolyline(sortedBoxes, xMin);
            Polyline rightPoly = BuildStepPolyline(sortedBoxes, xMax);

            // Ausgaben
            DA.SetData(0, leftStop.ToBrep());
            DA.SetData(1, rightStop.ToBrep());
            DA.SetData(2, leftPoly.ToPolylineCurve());
            DA.SetData(3, rightPoly.ToPolylineCurve());
            DA.SetData(4, leftBase.ToBrep());
            DA.SetData(5, rightBase.ToBrep());
            DA.SetDataList(6, milledBoards);
        }

        private static Polyline BuildStepPolyline(List<BoundingBox> sortedBoxes, double xSide)
        {
            var pts = new List<Point3d>();
            if (sortedBoxes.Count == 0)
                return new Polyline();

            double yFront = sortedBoxes[0].Min.Y;
            pts.Add(new Point3d(xSide, yFront, sortedBoxes[0].Min.Z));

            foreach (var b in sortedBoxes)
            {
                pts.Add(new Point3d(xSide, b.Max.Y, b.Min.Z));
                pts.Add(new Point3d(xSide, b.Max.Y, b.Max.Z));
            }

            double top = sortedBoxes[sortedBoxes.Count - 1].Max.Z;
            pts.Add(new Point3d(xSide, yFront, top));
            pts.Add(new Point3d(xSide, yFront, sortedBoxes[0].Min.Z));

            return new Polyline(pts);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityFixtureIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E07");
    }
}
