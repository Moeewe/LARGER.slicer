using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontSplitComponent : GH_Component
    {
                public ThekenfrontSplitComponent()
                    : base("Thekenfront 03b Fuge teilen", "TH_03b",
                                                        "Teilt jedes Fuge-Brett in unteren und oberen Teil.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Bretter", "Bretter", "Bretter aus Thekenfront 03 Bretteinteilung", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Split aktiv", "Aktiv", "True = Fuge-Bretter teilen", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Split Bretter", "Split", "Bretterliste nach dem Teilen der Fuge-Bretter", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Erlaeuterungen zum Split", GH_ParamAccess.list);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Die Eingabedaten koennen nicht als Brettliste gelesen werden.");
                return;
            }

            var outBoards = new List<ThekenBoard>();
            var info = new List<string>();

            if (!split)
            {
                outBoards.AddRange(inputBoards);
                info.Add("Split ist deaktiviert. Es wird die unveraenderte Brettliste ausgegeben.");
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
                            Type = "Fuge-Brett A",
                            IsSplitPart = true,
                            SourceIndex = b.SourceIndex
                        });
                        outBoards.Add(new ThekenBoard
                        {
                            ZMin = hi,
                            ZMax = b.ZMax,
                            Type = "Fuge-Brett B",
                            IsSplitPart = true,
                            SourceIndex = b.SourceIndex
                        });
                        createdSplits++;
                    }
                    else
                    {
                        outBoards.Add(b);
                        info.Add($"Fuge im Brett {b.SourceIndex} konnte wegen ungueltiger Grenzen nicht geteilt werden.");
                    }
                }
                else
                {
                    outBoards.Add(b);
                }
            }

            info.Add($"Erzeugte gesplittete Fuge-Bretter: {createdSplits}");
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

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E04");
    }
}
