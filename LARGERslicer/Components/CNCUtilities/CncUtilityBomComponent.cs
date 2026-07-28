using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityBomComponent : GH_Component
    {
                public CncUtilityBomComponent()
                : base("CNC Utilities 07 Bill of Materials", "CU_07",
                    "Generates a bill of materials for panel display and CSV export.",
                            "LARGER", "CNC Utilities")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", GH_ParamAccess.list);
            pManager.AddNumberParameter("Milling Depths", "Depths", "Milling depths from CNC Utilities 04 Milling Depth", GH_ParamAccess.list);
            pManager.AddNumberParameter("Board Length", "Length", "Board length from CNC Utilities 04 Milling Depth", GH_ParamAccess.item);
            pManager.AddTextParameter("Material", "Material", "Material name used in the BOM", GH_ParamAccess.item, "MDF / Solid Wood");
            pManager.AddBrepParameter("Left Fixture", "Left", "Optional left fixture stop from CNC Utilities 06", GH_ParamAccess.item);
            pManager.AddBrepParameter("Right Fixture", "Right", "Optional right fixture stop from CNC Utilities 06", GH_ParamAccess.item);
            pManager.AddBrepParameter("Left Base", "BaseL", "Optional left base board from CNC Utilities 06", GH_ParamAccess.item);
            pManager.AddBrepParameter("Right Base", "BaseR", "Optional right base board from CNC Utilities 06", GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Panel Text", "Panel", "Formatted BOM text for a GH panel", GH_ParamAccess.list);
            pManager.AddTextParameter("CSV Header", "Header", "CSV header row", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Rows", "CSV", "CSV data rows", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var raw = new List<object>();
            var depths = new List<double>();
            double length = 0;
            string material = "MDF / Vollholz";
            Brep saugL = null;
            Brep saugR = null;
            Brep basisL = null;
            Brep basisR = null;

            if (!DA.GetDataList(0, raw))
                return;
            if (!DA.GetDataList(1, depths))
                return;
            if (!DA.GetData(2, ref length))
                return;
            DA.GetData(3, ref material);
            DA.GetData(4, ref saugL);
            DA.GetData(5, ref saugR);
            DA.GetData(6, ref basisL);
            DA.GetData(7, ref basisR);

            var rows = new List<ThekenBoard>();
            foreach (var o in raw)
                if (TryGetBoard(o, out ThekenBoard t))
                    rows.Add(t);

            if (raw.Count > 0 && rows.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input data could not be parsed as a board list.");
                return;
            }

            if (rows.Count != depths.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Board count and milling depth count must match.");
                return;
            }

            var groups = rows
                .Select((row, index) => new { Row = row, Depth = depths[index] })
                .GroupBy(r => new
                {
                    T = r.Row.Type,
                    L = Math.Round(length, 1),
                    D = Math.Round(r.Depth, 1),
                    H = Math.Round(r.Row.Thickness, 1)
                })
                .OrderBy(g => SortOrder(g.Key.T))
                .ToList();

            var panel = new List<string>();
            var csv = new List<string>();
            string header = "Pos;Type;Length;Depth;Height;Count;Material;Remark";

            int pos = 1;
            panel.Add("Pos | Type | L | D | H | Count | Material | Remark");
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
                AddSaugRow(ref pos, material, "Fixture left", saugL, panel, csv);
            }
            if (saugR != null)
            {
                AddSaugRow(ref pos, material, "Fixture right", saugR, panel, csv);
            }
            if (basisL != null)
            {
                AddSaugRow(ref pos, material, "Base left", basisL, panel, csv);
            }
            if (basisR != null)
            {
                AddSaugRow(ref pos, material, "Base right", basisR, panel, csv);
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

            panel.Add($"{pos} | {type} | {l:F1} | {d:F1} | {h:F1} | 1 | {material} | Fixture geometry");
            csv.Add(string.Join(";", new[]
            {
                pos.ToString(CultureInfo.InvariantCulture),
                type,
                l.ToString("F1", CultureInfo.InvariantCulture),
                d.ToString("F1", CultureInfo.InvariantCulture),
                h.ToString("F1", CultureInfo.InvariantCulture),
                "1",
                material,
                "Fixture geometry"
            }));
            pos++;
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityBomIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E08");
    }
}
