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
                    : base("CNC Utilities 03 Schichtaufteilung", "CU_03",
                                                "Teilt die Gesamthoehe in Materialschichten und Trennfugen auf.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Gesamthoehe", "Hoehe", "Gesamthoehe des Blocks in mm (z. B. aus CNC Utilities 02 Abmessungen)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Randbrett unten", "Unten", "Dicke des unteren Randbretts in mm", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Randbrett oben", "Oben", "Dicke des oberen Randbretts in mm", GH_ParamAccess.item, 35.0);
            pManager.AddNumberParameter("Mittelbrett-Staerke", "Mitte", "Dicke der Mittelbretter in mm", GH_ParamAccess.item, 30.0);
            pManager.AddIntegerParameter("Fugenanzahl", "Fugen", "Anzahl der Fugen", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Minimale Fugenbreite", "MinFuge", "Mindestbreite je Fuge in mm", GH_ParamAccess.item, 4.0);
            pManager.AddNumberParameter("Fugenpositionen", "Positionen", "Optional: absolute Hoehen der Fugenmitten in mm", GH_ParamAccess.list);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Bretter", "Bretter", "Erzeugte Bretter inklusive Fuge-Metadaten", GH_ParamAccess.list);
            pManager.AddNumberParameter("Fugenmitten", "Mitten", "Fugenmitten in mm", GH_ParamAccess.list);
            pManager.AddNumberParameter("Fugenbreite", "Breite", "Tatsaechliche Fugenbreite in mm", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Erlaeuterungen zur Bretteinteilung", GH_ParamAccess.list);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Alle Hoehen und Dicken muessen groesser als 0 sein.");
                return;
            }

            fn = Math.Max(1, fn);
            fw = Math.Max(4.0, fw);

            double inner = h - ru - ro;
            if (inner <= fw * fn)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Die Gesamthoehe ist zu klein fuer Bretter und Fugen.");
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mindestens eine Fugenposition liegt ausserhalb des nutzbaren Innenbereichs.");
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fuge bei Z={fugeCenter:F2} ist ungueltig oder ueberschneidet eine andere Fuge.");
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Die Fugenaufteilung passt nicht zu den eingestellten Brettstaerken.");
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
                    $"Brettstapel endet bei Z={zCursor:F2}, Randbrett oben beginnt bei Z={expectedTopStart:F2}. Bitte Parameter pruefen.");
            }

            boards.Add(new ThekenBoard { ZMin = expectedTopStart, ZMax = h, Type = "Randbrett oben", SourceIndex = src++ });

            var infos = new List<string>
            {
                $"Nutzhoehe innen: {inner:F2} mm",
                $"Anzahl Mittelbretter: {middleCount}",
                $"Tatsaechliche Fugenbreite: {actualFugeWidth:F2} mm",
                $"Anzahl Fuge-Bretter: {marked}"
            };

            // Fugen als Marker-Info, die eigentliche Teilung passiert in TH_03b.
            for (int i = 0; i < fugenMitten.Count; i++)
                infos.Add($"Fuge {i + 1}: {fugenMitten[i]:F2} +/- {actualFugeWidth * 0.5:F2}");

            DA.SetDataList(0, boards);
            DA.SetDataList(1, fugenMitten);
            DA.SetData(2, actualFugeWidth);
            DA.SetDataList(3, infos);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilitySliceIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E03");
    }
}
