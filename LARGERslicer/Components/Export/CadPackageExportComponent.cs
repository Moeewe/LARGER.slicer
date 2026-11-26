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
        private bool _runWas;
        public CadPackageExportComponent()
          : base("CAD Package Export", "CAD Export",
              "Exports Breps 1:1 (without union) to 3DM, STEP and IGES with PSYYYYMMDD file naming. Open Breps are written too, with a note. Your open Rhino document is not touched.",
              "LARGER", "Export")
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

        // names written in this solution: several branches must not write over each other
        private readonly HashSet<string> _written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected override void BeforeSolveInstance()
        {
            _written.Clear();
            base.BeforeSolveInstance();
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

            // once when Start Export turns True, not again on every recompute while it stays True
            bool rising = run && !_runWas; _runWas = run;
            if (!rising)
            {
                log.Add(run ? "Exported. Set 'Start Export' to False and True again to export once more." : "Export is ready. Set 'Start Export' to True.");
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
                if (!b.IsSolid) log.Add($"NOTE: Geometry #{i + 1} is not closed (an open Brep); it is written as it is.");
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

            string plainName = baseName;
            for (int n = 2; !_written.Add(Path.Combine(folder, baseName)); n++)
                baseName = plainName + " (" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            DA.SetData(1, baseName);

            log.Add($"Input Breps total: {inputBreps.Count}");
            log.Add($"Valid Breps: {validBreps.Count}");
            log.Add("Union: disabled (1:1 export).");

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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "3DM file not written: " + message);
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "STEP file not written: " + message);
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "IGES file not written: " + message);
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

                if (!f3.Write(path, 7) || !File.Exists(path))
                {
                    message = "Rhino could not write " + path;
                    return false;
                }
                message = $"3DM file written ({breps.Count} Breps).";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        /// <summary>STEP and IGES through a document of their own that nobody sees: the open Rhino document, its selection,
        /// layers and undo stay untouched. Only when that fails (an old Rhino) the export command runs in the open document.</summary>
        private static bool WriteViaExportCommand(string path, List<Brep> breps, out string message)
        {
            try
            {
                using (var hd = RhinoDoc.CreateHeadless(null))
                {
                    if (hd != null)
                    {
                        int added = 0;
                        foreach (var b in breps) if (hd.Objects.AddBrep(b.DuplicateBrep()) != Guid.Empty) added++;
                        DateTime was = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                        if (added > 0 && hd.Export(path) && File.Exists(path) && File.GetLastWriteTimeUtc(path) > was)
                        {
                            message = $"File written ({added} Breps, 1:1 without union).";
                            return true;
                        }
                    }
                }
            }
            catch { }
            return WriteViaOpenDocument(path, breps, out message);
        }

        private static bool WriteViaOpenDocument(string path, List<Brep> breps, out string message)
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
                int selected = 0;

                for (int i = 0; i < breps.Count; i++)
                {
                    Guid id = doc.Objects.AddBrep(breps[i].DuplicateBrep());
                    if (id != Guid.Empty)
                    {
                        addedIds.Add(id);
                        if (doc.Objects.Select(id)) selected++;
                    }
                }

                if (addedIds.Count == 0)
                {
                    message = "No geometry could be written into the document for export.";
                    return false;
                }

                if (selected < addedIds.Count)
                {
                    message = "The geometry could not be selected for the export: is the current layer locked or hidden?";
                    return false;
                }

                // the file must be new or newer afterwards: the command also ends well when nothing was written
                DateTime before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                string escapedPath = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                // "!" ends any pending command state; extra Enter confirms exporter options.
                string script = "! _-Export \"" + escapedPath + "\" _Enter _Enter _Enter";

                bool ok = RhinoApp.RunScript(script, false);
                if (!ok)
                {
                    message = "Rhino export command failed.";
                    return false;
                }

                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= before)
                {
                    message = "Rhino did not write " + path;
                    return false;
                }

                message = $"File written ({addedIds.Count} Breps, 1:1 without union).";
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CadPackageExportIcon.png", this);

        public override Guid ComponentGuid => new Guid("A1A1E8AE-58A6-4E6E-8A41-405F8B5D0601");
    }
}