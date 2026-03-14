using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Grasshopper.Kernel;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontExportComponent : GH_Component
    {
        public ThekenfrontExportComponent()
          : base("TH Export", "TH_08",
                            "Exportiert Bretter, Containerbox und Stueckliste als 3DM bzw. CSV.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Bretter", "B", "Bretter aus TH_05 Block", GH_ParamAccess.list);
            pManager.AddBrepParameter("Containerbox", "C", "Containerbox aus TH_05 Block", GH_ParamAccess.item);
            pManager.AddBrepParameter("Referenz-Solid", "R", "Orientiertes Referenz-Solid aus TH_05 Block", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Header", "H", "CSV-Headerzeile aus TH_07 BOM", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Zeilen", "L", "CSV-Datenzeilen aus TH_07 BOM", GH_ParamAccess.list);
            pManager.AddTextParameter("Export Ordner", "P", "Exportpfad", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Dateiname Basis", "N", "Basisname der Exportdateien", GH_ParamAccess.item, "Thekenfront");
            pManager.AddBooleanParameter("3DM schreiben", "3DM", "3DM-Datei exportieren", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("CSV schreiben", "CSV", "CSV-Datei exportieren", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Export starten", "!", "True = Export ausfuehren", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Dateien", "F", "Geschriebene Dateipfade", GH_ParamAccess.list);
            pManager.AddTextParameter("Log", "Log", "Export-Protokoll", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boards = new List<Brep>();
            Brep container = null;
            Brep reference = null;
            string header = "";
            var lines = new List<string>();
            string path = "";
            string name = "Thekenfront";
            bool write3dm = true;
            bool writeCsv = true;
            bool run = false;

            DA.GetDataList(0, boards);
            DA.GetData(1, ref container);
            DA.GetData(2, ref reference);
            DA.GetData(3, ref header);
            DA.GetDataList(4, lines);
            DA.GetData(5, ref path);
            DA.GetData(6, ref name);
            DA.GetData(7, ref write3dm);
            DA.GetData(8, ref writeCsv);
            DA.GetData(9, ref run);

            var files = new List<string>();
            var log = new List<string>();

            if (!run)
            {
                log.Add("Run=false: Export bereit.");
                DA.SetDataList(0, files);
                DA.SetDataList(1, log);
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                string docPath = RhinoDoc.ActiveDoc?.Path;
                path = string.IsNullOrWhiteSpace(docPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : Path.GetDirectoryName(docPath);
            }

            Directory.CreateDirectory(path);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string basePath = Path.Combine(path, name + "_" + stamp);

            if (write3dm)
            {
                string p3 = basePath + ".3dm";
                try
                {
                    var f3 = new File3dm();
                    foreach (var b in boards)
                        if (b != null) f3.Objects.AddBrep(b);
                    if (container != null) f3.Objects.AddBrep(container);
                    if (reference != null) f3.Objects.AddBrep(reference);

                    f3.Write(p3, 7);
                    files.Add(p3);
                    log.Add("3DM geschrieben");
                }
                catch (Exception ex)
                {
                    log.Add("3DM Fehler: " + ex.Message);
                }
            }

            if (writeCsv)
            {
                string pc = basePath + ".csv";
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(header);
                    foreach (var l in lines)
                        sb.AppendLine(l);
                    File.WriteAllText(pc, sb.ToString(), Encoding.UTF8);
                    files.Add(pc);
                    log.Add("CSV geschrieben");
                }
                catch (Exception ex)
                {
                    log.Add("CSV Fehler: " + ex.Message);
                }
            }

            DA.SetDataList(0, files);
            DA.SetDataList(1, log);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E09");
    }
}
