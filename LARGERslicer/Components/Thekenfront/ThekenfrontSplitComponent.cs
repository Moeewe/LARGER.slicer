using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Types;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontSplitComponent : GH_Component
    {
        public ThekenfrontSplitComponent()
          : base("TH Fuge Split", "TH_03b",
              "Teilt Bretter an Fugenmitten in A/B-Haelften fuer separate Fraeselemente.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards", "B", "ThekenBoard-Liste aus TH_03", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Split aktiv", "S", "True = Split erzeugen", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Split Boards", "SB", "Ausgangs-Bretter inkl. Splitteile", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Split-Infos", GH_ParamAccess.list);
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
                if (o is ThekenBoard tb)
                    inputBoards.Add(tb);
            }

            var outBoards = new List<ThekenBoard>();
            var info = new List<string>();

            if (!split)
            {
                outBoards.AddRange(inputBoards);
                info.Add("Split deaktiviert.");
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
                        info.Add($"Fuge in Board {b.SourceIndex} konnte wegen Geometriegrenzen nicht gesplittet werden.");
                    }
                }
                else
                {
                    outBoards.Add(b);
                }
            }

            info.Add($"Split parts created from fuge boards: {createdSplits}");
            DA.SetDataList(0, outBoards);
            DA.SetDataList(1, info);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E04");
    }
}
