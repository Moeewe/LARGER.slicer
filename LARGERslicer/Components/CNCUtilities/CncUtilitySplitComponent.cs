using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilitySplitComponent : GH_Component
    {
                public CncUtilitySplitComponent()
                    : base("CNC Utilities 03b Split Joint", "CU_03b",
                                                 "Splits each layer with a separation joint into lower and upper parts.",
                            "LARGER", "CNC Utilities")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards", "Boards", "Boards from CNC Utilities 03 Layer Split", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Split Enabled", "On", "True = split joint boards", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Split Boards", "Split", "Board list after splitting joint boards", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Split notes", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boardsObj = new List<object>();
            bool split = true;

            if (!DA.GetDataList(0, boardsObj))
                return;
            DA.GetData(1, ref split);

            var inputBoards = new List<ThekenBoard>();
            foreach (var o in boardsObj)
            {
                if (TryGetBoard(o, out ThekenBoard tb))
                    inputBoards.Add(tb);
            }

            if (boardsObj.Count > 0 && inputBoards.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input data could not be parsed as a board list.");
                return;
            }

            var outBoards = new List<ThekenBoard>();
            var info = new List<string>();

            if (!split)
            {
                outBoards.AddRange(inputBoards);
                    info.Add("Split is disabled. Returning the unchanged board list.");
                DA.SetDataList(0, outBoards);
                DA.SetDataList(1, info);
                return;
            }

            int createdSplits = 0;
            foreach (var b in inputBoards)
            {
                bool doSplit = b.ContainsFuge && b.FugeCenter > b.ZMin && b.FugeCenter < b.ZMax;
                double fw = Math.Max(4.0, b.FugeWidth);

                if (doSplit)
                {
                    double lo = b.FugeCenter - fw * 0.5;
                    double hi = b.FugeCenter + fw * 0.5;

                    if (lo > b.ZMin && hi < b.ZMax)
                    {
                        outBoards.Add(new ThekenBoard
                        {
                            ZMin = b.ZMin,
                            ZMax = lo,
                            Type = "Joint Board A",
                            IsSplitPart = true,
                            SourceIndex = b.SourceIndex
                        });
                        outBoards.Add(new ThekenBoard
                        {
                            ZMin = hi,
                            ZMax = b.ZMax,
                            Type = "Joint Board B",
                            IsSplitPart = true,
                            SourceIndex = b.SourceIndex
                        });
                        createdSplits++;
                    }
                    else
                    {
                        outBoards.Add(b);
                        info.Add($"Joint in board {b.SourceIndex} could not be split because bounds are invalid.");
                    }
                }
                else
                {
                    outBoards.Add(b);
                }
            }

            info.Add($"Created split joint boards: {createdSplits}");
            DA.SetDataList(0, outBoards);
            DA.SetDataList(1, info);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilitySplitIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E04");
    }
}
