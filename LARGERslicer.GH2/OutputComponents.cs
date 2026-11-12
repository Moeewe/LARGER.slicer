using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    internal static class Files
    {
        /// <summary>Writes the lines if Save is on; reports what happened on the component.</summary>
        public static void Save(IDataAccess access, bool save, string file, List<string> lines)
        {
            if (!save) return;
            if (string.IsNullOrWhiteSpace(file)) { access.AddWarning("No file", "Give a file path to save to."); return; }
            try { File.WriteAllLines(file, lines); access.AddRemark("Saved", file); }
            catch (Exception ex) { access.AddError("Could not save", ex.Message); }
        }
    }

    // ============================================================== Ginger G-code
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d3001")]
    public sealed class GingerGCodeComponent : Component
    {
        public GingerGCodeComponent() : base(Io.Name("Ginger G-code", "G-code for the Ginger One in full size: the machine's start and end sequence, absolute positions, relative extrusion, one constant feedrate. Choose the Ginger machine in Print Settings so the E values fit the pellet extruder.", Io.Slicer, 9)) { }
        public GingerGCodeComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
            inputs.AddText("File", "File", "Optional: full path of the .gcode file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("G-code", "G", "G-code, line by line.", Access.Twig);
            outputs.AddText("Info", "I", "Layers, length, extrusion and time.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            string file; bool save; access.GetItem(1, out file); access.GetItem(2, out save);
            var r = GingerGCode.Build(tp);
            foreach (var w in r.Warnings) access.AddWarning(w.Length > 50 ? w.Substring(0, 50) + "…" : w, w);
            Files.Save(access, save, file, r.Lines);
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            access.SetItem(1, r.Info);
        }
    }

    // ============================================================== desktop test print
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d3002")]
    public sealed class DesktopTestPrintComponent : Component
    {
        public DesktopTestPrintComponent() : base(Io.Name("Desktop Test Print", "The same path scaled down as G-code for a desktop filament printer, with that printer's own start, layer change and end. A check of the path before the large print; material behaviour does not scale.", Io.Slicer, 10)) { }
        public DesktopTestPrintComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
            inputs.AddInteger("Printer", "P", string.Join("\n", DesktopPrinter.Presets.Select((x, i) => i + " = " + x.Name))).Set(0);
            inputs.AddNumber("Scale", "Sc", "Scale of the test print (0.1 = one tenth). 0 = automatic: the bead becomes 1.2 times the nozzle diameter.").Set(0.0);
            inputs.AddNumber("Nozzle", "Nz", "Nozzle diameter of the desktop printer in mm.").Set(0.4);
            inputs.AddNumber("Filament", "Fil", "Filament diameter in mm.").Set(1.75);
            inputs.AddNumber("Nozzle Temperature", "Tn", "Degrees C.").Set(215.0);
            inputs.AddNumber("Bed Temperature", "Tb", "Degrees C.").Set(60.0);
            inputs.AddNumber("Speed", "V", "Print speed in mm/s. The first layer runs at half of it.").Set(40.0);
            inputs.AddText("File", "File", "Optional: full path of the .gcode file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("G-code", "G", "G-code, line by line.", Access.Twig);
            outputs.AddPolyline("Preview", "C", "The scaled path as it lies on the printer bed.");
            outputs.AddText("Info", "I", "Size, bead, filament and time.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            int printer; double scale, nozzle, filament, tn, tb, speed; string file; bool save;
            access.GetItem(1, out printer); access.GetItem(2, out scale); access.GetItem(3, out nozzle); access.GetItem(4, out filament);
            access.GetItem(5, out tn); access.GetItem(6, out tb); access.GetItem(7, out speed); access.GetItem(8, out file); access.GetItem(9, out save);
            if (nozzle <= 0 || filament <= 0) { access.AddError("Invalid values", "Nozzle and filament diameter must be larger than 0."); return; }
            printer = Math.Max(0, Math.Min(DesktopPrinter.Presets.Length - 1, printer));
            var r = DesktopGCode.Build(tp, DesktopPrinter.Presets[printer], scale, nozzle, filament, tn, tb, speed, 1.0);
            foreach (var w in r.Warnings) access.AddWarning(w.Length > 50 ? w.Substring(0, 50) + "…" : w, w);
            Files.Save(access, save, file, r.Lines);
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            if (r.Preview.Count > 1) access.SetItem(1, r.Preview);
            access.SetItem(2, r.Info);
        }
    }
}
