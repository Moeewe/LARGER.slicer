using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilitySliceComponent : GH_Component
    {
                public CncUtilitySliceComponent()
                    : base("CNC Utilities 03 Layer Split", "CU_03",
                                                "Splits total height into material layers and separation joints.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Total Height", "H", "Total block height in mm (for example from CNC Utilities 02 Dimensions)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bottom Edge Board", "Bottom", "Thickness of the bottom edge board in mm", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Top Edge Board", "Top", "Thickness of the top edge board in mm", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Middle Board Thickness", "Mid", "Thickness of middle boards in mm", GH_ParamAccess.item, 30.0);
            pManager.AddIntegerParameter("Joint Count", "Joints", "Number of separation joints", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Minimum Joint Width", "MinJoint", "Minimum width per joint in mm", GH_ParamAccess.item, 4.0);
            pManager.AddNumberParameter("Joint Positions", "Positions", "Optional: absolute joint center heights in mm", GH_ParamAccess.list);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards", "Boards", "Generated boards including joint metadata", GH_ParamAccess.list);
            pManager.AddNumberParameter("Joint Centers", "Centers", "Joint center heights in mm", GH_ParamAccess.list);
            pManager.AddNumberParameter("Joint Width", "Width", "Actual joint width in mm", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Notes about board partitioning", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double h = 0;
            double ru = 35;
            double ro = 35;
            double mb = 30;
            int fn = 1;
            double fw = 4;
            var manual = new List<double>();

            if (!DA.GetData(0, ref h))
                return;
            DA.GetData(1, ref ru);
            DA.GetData(2, ref ro);
            DA.GetData(3, ref mb);
            DA.GetData(4, ref fn);
            DA.GetData(5, ref fw);
            DA.GetDataList(6, manual);

            if (h <= 0 || ru <= 0 || ro <= 0 || mb <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "All heights and thicknesses must be greater than 0.");
                return;
            }

            fn = Math.Max(1, fn);
            fw = Math.Max(4.0, fw);

            double inner = h - ru - ro;
            if (inner <= fw * fn)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Total height is too small for boards and joints.");
                return;
            }

            int middleCount = (int)Math.Floor((inner - fw * fn) / mb);
            middleCount = Math.Max(1, middleCount);

            double usedMiddle = middleCount * mb;
            double rest = inner - usedMiddle;
            double actualFugeWidth = rest / fn;

            var fugenMitten = new List<double>();
            if (manual.Count == fn)
            {
                fugenMitten.AddRange(manual);
                fugenMitten.Sort();
            }
            else if (fn == 1)
            {
                fugenMitten.Add(h * 0.5);
            }
            else
            {
                double spacing = inner / (fn + 1);
                for (int i = 1; i <= fn; i++)
                    fugenMitten.Add(ru + i * spacing);
            }

            for (int i = 0; i < fugenMitten.Count; i++)
            {
                if (fugenMitten[i] <= ru || fugenMitten[i] >= (h - ro))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one joint position is outside the usable inner range.");
                    return;
                }
            }

            var boards = new List<ThekenBoard>();
            boards.Add(new ThekenBoard { ZMin = 0, ZMax = ru, Type = "Randbrett unten", SourceIndex = 0 });

            double zCursor = ru;
            int src = 1;
            int consumedMiddleBoards = 0;
            int marked = 0;

            for (int i = 0; i < fugenMitten.Count; i++)
            {
                double fugeCenter = fugenMitten[i];
                double fugeLow = fugeCenter - actualFugeWidth * 0.5;
                double fugeHigh = fugeCenter + actualFugeWidth * 0.5;

                if (fugeLow <= zCursor)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Joint at Z={fugeCenter:F2} is invalid or overlaps another joint.");
                    return;
                }

                double deltaToFugeLow = fugeLow - zCursor;
                int regularBoardsBefore = (int)Math.Floor(deltaToFugeLow / mb);
                double lowerHalfThickness = deltaToFugeLow - regularBoardsBefore * mb;

                for (int j = 0; j < regularBoardsBefore; j++)
                {
                    boards.Add(new ThekenBoard
                    {
                        ZMin = zCursor,
                        ZMax = zCursor + mb,
                        Type = "Mittelbrett",
                        SourceIndex = src++
                    });
                    zCursor += mb;
                    consumedMiddleBoards++;
                }

                double upperHalfThickness = mb - lowerHalfThickness;
                double fugeBoardTop = fugeHigh + upperHalfThickness;

                boards.Add(new ThekenBoard
                {
                    ZMin = zCursor,
                    ZMax = fugeBoardTop,
                    Type = "Fuge-Brett",
                    ContainsFuge = true,
                    FugeCenter = fugeCenter,
                    FugeWidth = actualFugeWidth,
                    SourceIndex = src++
                });

                zCursor = fugeBoardTop;
                consumedMiddleBoards++;
                marked++;
            }

            int remainingMiddleBoards = middleCount - consumedMiddleBoards;
            if (remainingMiddleBoards < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Joint distribution does not match the configured board thicknesses.");
                return;
            }

            for (int i = 0; i < remainingMiddleBoards; i++)
            {
                boards.Add(new ThekenBoard
                {
                    ZMin = zCursor,
                    ZMax = zCursor + mb,
                    Type = "Mittelbrett",
                    SourceIndex = src++
                });
                zCursor += mb;
            }

            double expectedTopStart = h - ro;
            if (Math.Abs(zCursor - expectedTopStart) > 0.01)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Board stack ends at Z={zCursor:F2}, top edge board starts at Z={expectedTopStart:F2}. Please review parameters.");
            }

            boards.Add(new ThekenBoard { ZMin = expectedTopStart, ZMax = h, Type = "Randbrett oben", SourceIndex = src++ });

            var infos = new List<string>
            {
                $"Usable inner height: {inner:F2} mm",
                $"Middle board count: {middleCount}",
                $"Actual joint width: {actualFugeWidth:F2} mm",
                $"Joint-board count: {marked}"
            };

            // Fugen als Marker-Info, die eigentliche Teilung passiert in TH_03b.
            for (int i = 0; i < fugenMitten.Count; i++)
                infos.Add($"Joint {i + 1}: {fugenMitten[i]:F2} +/- {actualFugeWidth * 0.5:F2}");

            DA.SetDataList(0, boards);
            DA.SetDataList(1, fugenMitten);
            DA.SetData(2, actualFugeWidth);
            DA.SetDataList(3, infos);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilitySliceIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E03");
    }
}
