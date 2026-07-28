using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityDepthComponent : GH_Component
    {
                public CncUtilityDepthComponent()
                : base("CNC Utilities 04 Milling Depth", "CU_04",
                    "Calculates required milling depth per board in stepped increments.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Body", "Body", "From CNC Utilities 01 Orient Part", GH_ParamAccess.item);
            pManager.AddGenericParameter("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", GH_ParamAccess.list);
            pManager.AddNumberParameter("Start Depth", "Start", "Minimum allowed milling depth in mm", GH_ParamAccess.item, 150.0);
            pManager.AddNumberParameter("Depth Step", "Step", "Step size for milling depth in mm", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Front Buffer", "Front", "Additional front buffer in mm", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Rear Buffer", "Rear", "Additional rear buffer in mm", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Side Overhang", "Overhang", "Additional board length on both sides in mm", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Milling Depths", "Depths", "Calculated milling depth per board in mm", GH_ParamAccess.list);
            pManager.AddNumberParameter("Board Length", "Length", "Unified board length in mm", GH_ParamAccess.item);
            pManager.AddNumberParameter("Raw Depths", "Raw", "Unstepped maximum depth per board in mm", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Depth calculation notes per board", GH_ParamAccess.list);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Depth step must be greater than 0.");
                return;
            }

            var boards = new List<ThekenBoard>();
            foreach (var o in boardsRaw)
            {
                if (TryGetBoard(o, out ThekenBoard tb))
                    boards.Add(tb);
            }

            if (boardsRaw.Count > 0 && boards.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input data could not be parsed as a board list.");
                return;
            }

            BoundingBox bb = solid.GetBoundingBox(true);
            double length = (bb.Max.X - bb.Min.X) + 2.0 * overhang;

            var depths = new List<double>();
            var rawDepths = new List<double>();
            var info = new List<string>();

            for (int i = 0; i < boards.Count; i++)
            {
                double rawDepth = GetMaxDepthInZRange(solid, boards[i].ZMin, boards[i].ZMax);
                double need = rawDepth + pv + ph;
                double stepped = StepUp(need, depthBase, depthStep);

                rawDepths.Add(rawDepth);
                depths.Add(stepped);

                info.Add($"{boards[i].Type}: raw={rawDepth:F1} need={need:F1} step={stepped:F1}");
            }

            DA.SetDataList(0, depths);
            DA.SetData(1, length);
            DA.SetDataList(2, rawDepths);
            DA.SetDataList(3, info);
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

        private static bool TryGetBoard(object input, out ThekenBoard board)
        {
            board = null;

            if (input is ThekenBoard directBoard)
            {
                board = directBoard;
                return true;
            }

            if (input is GH_ObjectWrapper wrapper && wrapper.Value is ThekenBoard wrappedBoard)
            {
                board = wrappedBoard;
                return true;
            }

            return false;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityDepthIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E05");
    }
}
