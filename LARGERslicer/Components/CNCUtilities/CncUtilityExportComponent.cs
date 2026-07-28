using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityExportComponent : GH_Component
    {
                public CncUtilityExportComponent()
                    : base("CNC Utilities 08 Data Export", "CU_08",
                                                        "Exports boards and bill of materials as 3DM and CSV.",
                            "LARGER", "CNC Utilities")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Boards", "Boards", "Boards from CNC Utilities 05 Build Raw Block", GH_ParamAccess.list);
            pManager.AddBrepParameter("Container Box", "Box", "Container box from CNC Utilities 05 Build Raw Block", GH_ParamAccess.item);
            pManager.AddBrepParameter("Reference Body", "Ref", "Reference body from CNC Utilities 05 Build Raw Block", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Header", "Header", "CSV header row from CNC Utilities 07 Bill of Materials", GH_ParamAccess.item);
            pManager.AddTextParameter("CSV Rows", "CSV", "CSV data rows from CNC Utilities 07 Bill of Materials", GH_ParamAccess.list);
            pManager.AddTextParameter("Export Folder", "Folder", "Destination folder for export", GH_ParamAccess.item, "");
            pManager.AddTextParameter("File Name", "Name", "Base name for exported files", GH_ParamAccess.item, "CNC_Project");
            pManager.AddBooleanParameter("Write 3DM", "3DM", "Export a 3DM file", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Write CSV", "CSV", "Export a CSV file", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Start Export", "Start", "True = run export", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Files", "Files", "Written file paths", GH_ParamAccess.list);
            pManager.AddTextParameter("Log", "Log", "Export log", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boards = new List<Brep>();
            Brep container = null;
            Brep reference = null;
            string header = "";
            var lines = new List<string>();
            string path = "";
            string name = "CNC_Project";
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
                log.Add("Export is ready. Set 'Start Export' to True.");
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
                    log.Add("3DM file written");
                }
                catch (Exception ex)
                {
                    log.Add("Error writing 3DM file: " + ex.Message);
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
                    log.Add("CSV file written");
                }
                catch (Exception ex)
                {
                    log.Add("Error writing CSV file: " + ex.Message);
                }
            }

            DA.SetDataList(0, files);
            DA.SetDataList(1, log);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityExportIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E09");
    }
}
