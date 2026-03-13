using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontBomComponent : GH_Component
    {
        public ThekenfrontBomComponent()
          : base("TH BOM", "TH_07",
              "Erzeugt Stueckliste fuer GH-Panel und CSV-Linien.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Boards with Depth", "BD", "ThekenBoardWithDepth", GH_ParamAccess.list);
            pManager.AddTextParameter("Material", "M", "Materialname", GH_ParamAccess.item, "MDF / Vollholz");
            pManager.AddBrepParameter("Saug links", "SL", "Optional", GH_ParamAccess.item);
            pManager.AddBrepParameter("Saug rechts", "SR", "Optional", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Panel", "P", "Formatierte BOM", GH_ParamAccess.list);
            pManager.AddTextParameter("CSV Header", "H", "CSV Header", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Lines", "C", "CSV Daten", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var raw = new List<object>();
            string material = "MDF / Vollholz";
            Brep saugL = null;
            Brep saugR = null;

            if (!DA.GetDataList(0, raw))
                return;
            DA.GetData(1, ref material);
            DA.GetData(2, ref saugL);
            DA.GetData(3, ref saugR);

            var rows = new List<ThekenBoardWithDepth>();
            foreach (var o in raw)
                if (o is ThekenBoardWithDepth t)
                    rows.Add(t);

            var groups = rows
                .Where(r => r?.Board != null)
                .GroupBy(r => new
                {
                    T = r.Board.Type,
                    L = Math.Round(r.Length, 1),
                    D = Math.Round(r.Depth, 1),
                    H = Math.Round(r.Board.Thickness, 1)
                })
                .OrderBy(g => SortOrder(g.Key.T))
                .ToList();

            var panel = new List<string>();
            var csv = new List<string>();
            string header = "Pos;Typ;Laenge;Tiefe;Hoehe;Anzahl;Material;Bemerkung";

            int pos = 1;
            panel.Add("Pos | Typ | L | T | H | Anzahl | Material | Bemerkung");
            panel.Add("-------------------------------------------------------");

            foreach (var g in groups)
            {
                int count = g.Count();
                string remark = g.Key.T.Contains("Fuge") ? "Fuge" : "";
                panel.Add($"{pos} | {g.Key.T} | {g.Key.L:F1} | {g.Key.D:F1} | {g.Key.H:F1} | {count} | {material} | {remark}");
                csv.Add(string.Join(";", new[]
                {
                    pos.ToString(CultureInfo.InvariantCulture),
                    g.Key.T,
                    g.Key.L.ToString("F1", CultureInfo.InvariantCulture),
                    g.Key.D.ToString("F1", CultureInfo.InvariantCulture),
                    g.Key.H.ToString("F1", CultureInfo.InvariantCulture),
                    count.ToString(CultureInfo.InvariantCulture),
                    material,
                    remark
                }));
                pos++;
            }

            if (saugL != null)
            {
                AddSaugRow(ref pos, material, "Saugelement links", saugL, panel, csv);
            }
            if (saugR != null)
            {
                AddSaugRow(ref pos, material, "Saugelement rechts", saugR, panel, csv);
            }

            DA.SetDataList(0, panel);
            DA.SetData(1, header);
            DA.SetDataList(2, csv);
        }

        private static int SortOrder(string t)
        {
            if (t.Contains("unten")) return 0;
            if (t.Contains("Mittel")) return 1;
            if (t.Contains("Fuge")) return 2;
            if (t.Contains("oben")) return 3;
            return 4;
        }

        private static void AddSaugRow(ref int pos, string material, string type, Brep b, List<string> panel, List<string> csv)
        {
            BoundingBox bb = b.GetBoundingBox(true);
            double l = bb.Max.X - bb.Min.X;
            double d = bb.Max.Y - bb.Min.Y;
            double h = bb.Max.Z - bb.Min.Z;

            panel.Add($"{pos} | {type} | {l:F1} | {d:F1} | {h:F1} | 1 | {material} | Aufspanngeometrie");
            csv.Add(string.Join(";", new[]
            {
                pos.ToString(CultureInfo.InvariantCulture),
                type,
                l.ToString("F1", CultureInfo.InvariantCulture),
                d.ToString("F1", CultureInfo.InvariantCulture),
                h.ToString("F1", CultureInfo.InvariantCulture),
                "1",
                material,
                "Aufspanngeometrie"
            }));
            pos++;
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E08");
    }
}
