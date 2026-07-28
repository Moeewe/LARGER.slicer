using System;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Components.Export
{
    public class CadPackageExportComponent : GH_Component
    {
        public CadPackageExportComponent()
          : base("CAD Package Export", "CAD Export",
              "Exports closed Breps 1:1 (without union) to 3DM, STEP, and IGES with PSYYYYMMDD file naming.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Export";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Geometries", "Geo", "Closed Breps for export (exported 1:1 as provided).", GH_ParamAccess.list);
            pManager.AddTextParameter("Export Folder", "Folder", "Destination folder for all files.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Project Name", "Project", "Project name used in file naming.", GH_ParamAccess.item, "Project");
            pManager.AddTextParameter("Part Number", "Part", "Part number in file names.", GH_ParamAccess.item, "Part1");
            pManager.AddTextParameter("Subpart Number", "Subpart", "Subpart number, e.g. top/bottom.", GH_ParamAccess.item, "general");
            pManager.AddTextParameter("Revision", "Rev", "Revision, e.g. v1.1 (optional).", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("Write 3DM", "3DM", "Export a 3DM file.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Write STEP", "STP", "Export a STEP file (.stp).", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Write IGES", "IGES", "Export an IGES file (.iges).", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Start Export", "Start", "True = run export.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Files", "Files", "Written file paths.", GH_ParamAccess.list);
            pManager.AddTextParameter("Base Name", "Name", "Resolved base name according to naming scheme.", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log", "Export log.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var inputBreps = new List<Brep>();
            string folder = string.Empty;
            string projectName = "Project";
            string partNumber = "Part1";
            string subPart = "general";
            string revision = string.Empty;
            bool write3dm = true;
            bool writeStep = true;
            bool writeIges = true;
            bool run = false;

            if (!DA.GetDataList(0, inputBreps))
                return;
            DA.GetData(1, ref folder);
            DA.GetData(2, ref projectName);
            DA.GetData(3, ref partNumber);
            DA.GetData(4, ref subPart);
            DA.GetData(5, ref revision);
            DA.GetData(6, ref write3dm);
            DA.GetData(7, ref writeStep);
            DA.GetData(8, ref writeIges);
            DA.GetData(9, ref run);

            var files = new List<string>();
            var log = new List<string>();
            string baseName = ExportNamingHelper.BuildBaseName(DateTime.Now, projectName, partNumber, subPart, revision, "Geometry");

            DA.SetData(1, baseName);

            if (!run)
            {
                log.Add("Export is ready. Set 'Start Export' to True.");
                log.Add("Rule: no union, export remains 1:1 with input.");
                DA.SetDataList(0, files);
                DA.SetDataList(2, log);
                return;
            }

            if (!write3dm && !writeStep && !writeIges)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No output format enabled.");
                log.Add("Canceled: no output format enabled.");
                DA.SetDataList(0, files);
                DA.SetDataList(2, log);
                return;
            }

            var validBreps = new List<Brep>();
            for (int i = 0; i < inputBreps.Count; i++)
            {
                Brep b = inputBreps[i];
                if (b == null)
                {
                    log.Add($"WARNING: Geometry #{i + 1} is null and will be skipped.");
                    continue;
                }
                if (!b.IsValid)
                {
                    log.Add($"WARNING: Geometry #{i + 1} is invalid and will be skipped.");
                    continue;
                }
                validBreps.Add(b);
            }

            if (validBreps.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid Breps found for export.");
                log.Add("Canceled: no valid Breps found for export.");
                DA.SetDataList(0, files);
                DA.SetDataList(2, log);
                return;
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                string docPath = RhinoDoc.ActiveDoc?.Path;
                folder = string.IsNullOrWhiteSpace(docPath)
                    ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
                    : Path.GetDirectoryName(docPath);
                log.Add("Export folder not set: fallback to document folder/Desktop.");
            }

            try
            {
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Export folder is invalid.");
                log.Add("Canceled: export folder is invalid - " + ex.Message);
                DA.SetDataList(0, files);
                DA.SetDataList(2, log);
                return;
            }

            log.Add($"Eingabe-Breps gesamt: {inputBreps.Count}");
            log.Add($"Gueltige Breps: {validBreps.Count}");
            log.Add("Union: deaktiviert (1:1 Export). ");

            if (write3dm)
            {
                string file3dm = Path.Combine(folder, baseName + ".3dm");
                if (Write3dm(file3dm, validBreps, out string message))
                {
                    files.Add(file3dm);
                    log.Add(message);
                }
                else
                {
                    log.Add("Error 3DM: " + message);
                }
            }

            if (writeStep)
            {
                string fileStp = Path.Combine(folder, baseName + ".stp");
                if (WriteViaExportCommand(fileStp, validBreps, out string message))
                {
                    files.Add(fileStp);
                    log.Add("STEP: " + message);
                }
                else
                {
                    log.Add("Error STEP: " + message);
                }
            }

            if (writeIges)
            {
                string fileIges = Path.Combine(folder, baseName + ".iges");
                if (WriteViaExportCommand(fileIges, validBreps, out string message))
                {
                    files.Add(fileIges);
                    log.Add("IGES: " + message);
                }
                else
                {
                    log.Add("Error IGES: " + message);
                }
            }

            DA.SetDataList(0, files);
            DA.SetDataList(2, log);
        }

        private static bool Write3dm(string path, List<Brep> breps, out string message)
        {
            message = string.Empty;
            try
            {
                var f3 = new File3dm();
                for (int i = 0; i < breps.Count; i++)
                {
                    Brep b = breps[i];
                    f3.Objects.AddBrep(b.DuplicateBrep());
                }

                f3.Write(path, 7);
                message = $"3DM file written ({breps.Count} Breps).";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static bool WriteViaExportCommand(string path, List<Brep> breps, out string message)
        {
            message = string.Empty;
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                message = "No active Rhino document available.";
                return false;
            }

            var addedIds = new List<Guid>();
            var previouslySelected = new List<Guid>();

            try
            {
                var selectedObjects = doc.Objects.GetSelectedObjects(false, false);
                if (selectedObjects != null)
                {
                    foreach (RhinoObject obj in selectedObjects)
                    {
                        previouslySelected.Add(obj.Id);
                    }
                }

                doc.Objects.UnselectAll();

                for (int i = 0; i < breps.Count; i++)
                {
                    Guid id = doc.Objects.AddBrep(breps[i].DuplicateBrep());
                    if (id != Guid.Empty)
                    {
                        addedIds.Add(id);
                        doc.Objects.Select(id);
                    }
                }

                if (addedIds.Count == 0)
                {
                    message = "No geometry could be written into the document for export.";
                    return false;
                }

                string escapedPath = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                // "!" beendet ggf. haengende Vorbefehle; extra Enter puffert Exporter-Optionen.
                string script = "! _-Export \"" + escapedPath + "\" _Enter _Enter _Enter";

                bool ok = RhinoApp.RunScript(script, false);
                if (!ok)
                {
                    message = "Rhino export command failed.";
                    return false;
                }

                message = $"Datei geschrieben ({addedIds.Count} Breps, 1:1 ohne Union).";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            finally
            {
                for (int i = 0; i < addedIds.Count; i++)
                {
                    doc.Objects.Delete(addedIds[i], true);
                }

                doc.Objects.UnselectAll();
                for (int i = 0; i < previouslySelected.Count; i++)
                {
                    doc.Objects.Select(previouslySelected[i]);
                }
                doc.Views.Redraw();
            }
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CadPackageExportIcon.png");

        public override Guid ComponentGuid => new Guid("A1A1E8AE-58A6-4E6E-8A41-405F8B5D0601");
    }
}