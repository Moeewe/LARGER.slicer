using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Types;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontSliceComponent : GH_Component
    {
        public ThekenfrontSliceComponent()
          : base("TH Slice", "TH_03",
              "Bretteinteilung inkl. Fugenlogik mit Restmassverteilung auf Fugen.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Gesamthoehe", "H", "Blockhoehe in mm", GH_ParamAccess.item);
            pManager.AddNumberParameter("Rand unten", "RU", "Randbrett unten", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Rand oben", "RO", "Randbrett oben", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Mittelbrett", "MB", "Mittelbrettstaerke", GH_ParamAccess.item, 30.0);
            pManager.AddIntegerParameter("Fugenanzahl", "FN", "Anzahl Fugen", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Fugenbreite min", "FW", "Minimale Fugenbreite", GH_ParamAccess.item, 4.0);
            pManager.AddNumberParameter("Fugenpositionen", "FP", "Optional manuelle Fugenmitten in mm (absolut)", GH_ParamAccess.list);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards", "B", "Liste ThekenBoard", GH_ParamAccess.list);
            pManager.AddNumberParameter("Fugenmitten", "FM", "Berechnete Fugenmitten", GH_ParamAccess.list);
            pManager.AddNumberParameter("Fugenbreite", "FW", "Tatsaechliche Fugenbreite nach Restmassverteilung", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Debug-Infos", GH_ParamAccess.list);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Alle Dicken/Hoehen muessen > 0 sein.");
                return;
            }

            fn = Math.Max(1, fn);
            fw = Math.Max(4.0, fw);

            double inner = h - ru - ro;
            if (inner <= fw * fn)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Zu wenig Hoehe fuer Bretter und Fugen.");
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mindestens eine Fugenposition liegt ausserhalb des gueltigen Innenbereichs.");
                    return;
                }
            }

            var boards = new List<ThekenBoard>();
            boards.Add(new ThekenBoard { ZMin = 0, ZMax = ru, Type = "Randbrett unten", SourceIndex = 0 });

            double zCursor = ru;
            int src = 1;
            for (int i = 0; i < middleCount; i++)
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

            boards.Add(new ThekenBoard { ZMin = h - ro, ZMax = h, Type = "Randbrett oben", SourceIndex = src++ });

            // Markiere Bretter, die eine Fuge enthalten.
            int marked = 0;
            foreach (double fm in fugenMitten)
            {
                ThekenBoard hit = null;
                foreach (var b in boards)
                {
                    if (fm > b.ZMin && fm < b.ZMax)
                    {
                        hit = b;
                        break;
                    }
                }

                if (hit == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Fuge bei Z={fm:F2} schneidet kein Brett.");
                    continue;
                }

                if (hit.ContainsFuge)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mehrere Fugen im selben Brett (SourceIndex {hit.SourceIndex}).");
                    continue;
                }

                hit.ContainsFuge = true;
                hit.FugeCenter = fm;
                hit.FugeWidth = actualFugeWidth;
                hit.Type = "Fuge-Brett";
                marked++;
            }

            var infos = new List<string>
            {
                $"inner={inner:F2}",
                $"middleCount={middleCount}",
                $"fugeWidth={actualFugeWidth:F2}",
                $"fugeBoardsMarked={marked}"
            };

            // Fugen als Marker-Info, die eigentliche Teilung passiert in TH_03b.
            for (int i = 0; i < fugenMitten.Count; i++)
                infos.Add($"Fuge {i + 1}: {fugenMitten[i]:F2} +/- {actualFugeWidth * 0.5:F2}");

            DA.SetDataList(0, boards);
            DA.SetDataList(1, fugenMitten);
            DA.SetData(2, actualFugeWidth);
            DA.SetDataList(3, infos);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E03");
    }
}
