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
              "Exportiert Closed Breps 1:1 (ohne Union) nach 3DM, STEP und IGES mit PSYYYYMMDD-Dateinamen.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Export";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Geometrien", "Geo", "Closed Breps fuer den Export (werden 1:1 wie eingegeben exportiert).", GH_ParamAccess.list);
            pManager.AddTextParameter("Exportordner", "Ordner", "Zielordner fuer alle Dateien.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Projektname", "Projekt", "Projektname fuer Dateibenennung.", GH_ParamAccess.item, "Projekt");
            pManager.AddTextParameter("Teilenummer", "Teil", "Teilenummer im Dateinamen.", GH_ParamAccess.item, "Teil1");
            pManager.AddTextParameter("Unterteilenummer", "Unterteil", "Unterteilenummer, z.B. oben/unten.", GH_ParamAccess.item, "allgemein");
            pManager.AddTextParameter("Revision", "Rev", "Revision, z.B. v1.1 (optional).", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("3DM schreiben", "3DM", "3DM-Datei exportieren.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("STEP schreiben", "STP", "STEP-Datei exportieren (.stp).", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("IGES schreiben", "IGES", "IGES-Datei exportieren (.iges).", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Export starten", "Start", "True = Export ausfuehren.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Dateien", "Dateien", "Geschriebene Dateipfade.", GH_ParamAccess.list);
            pManager.AddTextParameter("Basisname", "Name", "Aufgeloester Basisname gemaess Benennungsschema.", GH_ParamAccess.item);
            pManager.AddTextParameter("Protokoll", "Log", "Export-Protokoll.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var inputBreps = new List<Brep>();
            string folder = string.Empty;
            string projectName = "Projekt";
            string partNumber = "Teil1";
            string subPart = "allgemein";
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
            string baseName = ExportNamingHelper.BuildBaseName(DateTime.Now, projectName, partNumber, subPart, revision, "Geometrie");

            DA.SetData(1, baseName);

            if (!run)
            {
                log.Add("Export ist bereit. Setze 'Export starten' auf True.");
                log.Add("Regel: kein Union, Export 1:1 wie Eingabe.");
                DA.SetDataList(0, files);
                DA.SetDataList(2, log);
                return;
            }

            if (!write3dm && !writeStep && !writeIges)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Kein Ausgabeformat aktiviert.");
                log.Add("Abbruch: Kein Ausgabeformat aktiviert.");
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
                    log.Add($"WARNUNG: Geometrie #{i + 1} ist null und wird uebersprungen.");
                    continue;
                }
                if (!b.IsValid)
                {
                    log.Add($"WARNUNG: Geometrie #{i + 1} ist ungueltig und wird uebersprungen.");
                    continue;
                }
                validBreps.Add(b);
            }

            if (validBreps.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Keine gueltigen Breps fuer den Export.");
                log.Add("Abbruch: Keine gueltigen Breps fuer den Export.");
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
                log.Add("Exportordner nicht gesetzt: Fallback auf Dokumentordner/Desktop.");
            }

            try
            {
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Exportordner ungueltig.");
                log.Add("Abbruch: Exportordner ungueltig - " + ex.Message);
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
                    log.Add("Fehler 3DM: " + message);
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
                    log.Add("Fehler STEP: " + message);
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
                    log.Add("Fehler IGES: " + message);
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
                message = $"3DM-Datei geschrieben ({breps.Count} Breps).";
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
                message = "Kein aktives Rhino-Dokument vorhanden.";
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
                    message = "Keine Geometrie konnte fuer den Export in das Dokument geschrieben werden.";
                    return false;
                }

                string escapedPath = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                // "!" beendet ggf. haengende Vorbefehle; extra Enter puffert Exporter-Optionen.
                string script = "! _-Export \"" + escapedPath + "\" _Enter _Enter _Enter";

                bool ok = RhinoApp.RunScript(script, false);
                if (!ok)
                {
                    message = "Rhino Exportkommando fehlgeschlagen.";
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

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A1A1E8AE-58A6-4E6E-8A41-405F8B5D0601");
    }
}