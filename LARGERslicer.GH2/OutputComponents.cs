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
        public static void Save(IDataAccess access, bool save, string file, List<string> lines, string defaultName = "larger_print.gcode")
        {
            if (!save) return;
            try { access.AddRemark("Saved", OutputFile.Write(file, defaultName, lines)); }
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
            inputs.AddText("File", "File", "Optional: file or folder. Empty = the Desktop.", Access.Item, Requirement.MayBeMissing);
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
            inputs.AddText("File", "File", "Optional: file or folder. Empty = the Desktop.", Access.Item, Requirement.MayBeMissing);
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
            Files.Save(access, save, file, r.Lines, "larger_testprint.gcode");
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            if (r.Preview.Count > 1) access.SetItem(1, r.Preview);
            access.SetItem(2, r.Info);
        }
    }

    // ============================================================== easy slicer
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d3003")]
    public sealed class EasySlicerComponent : Component
    {
        public EasySlicerComponent() : base(Io.Name("Easy Slicer", "Model in, checked print path out: slices, builds walls, bottom and infill, joins everything into one continuous line, rounds the corners, adds skirt and exit and checks the result. For every option use the single components.", Io.Slicer, 0)) { }
        public EasySlicerComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "G", "Your part: Breps, surfaces, SubDs or meshes, standing on the XY plane.", Access.Twig);
            inputs.AddNumber("Bead Width", "W", "Width of one printed bead in mm. Every dimension follows from it.").Set(5.0);
            inputs.AddNumber("Layer Height", "H", "Layer height in mm: a third to a half of the bead width.").Set(2.0);
            inputs.AddNumber("Speed", "F", "Print speed in mm/min, the same for every move.").Set(3000.0);
            inputs.AddInteger("Machine", "M", string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddInteger("Walls", "N", "Number of walls.").Set(1);
            inputs.AddInteger("Bottom Layers", "B", "Solid layers at the bottom. 0 = open.").Set(0);
            inputs.AddInteger("Infill", "I", string.Join("\n", EasySlicer.Infills.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddNumber("Infill Size", "Sz", "Rib distance, wavelength or cell size in mm. 0 = chosen from the bead width.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The finished path: connect to Ginger G-code, Robot Program or Desktop Test Print.");
            outputs.AddPolyline("Path", "P", "The path as a polyline.");
            outputs.AddBoolean("OK", "OK", "True if the path can be printed.");
            outputs.AddText("Report", "R", "What was done, what the check found and what to do next.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            object[] geo;
            if (!access.GetItemArray(0, out geo) || geo == null || geo.Length == 0) return;
            double w, h, f, size; int machine, walls, bottom, infill;
            access.GetItem(1, out w); access.GetItem(2, out h); access.GetItem(3, out f); access.GetItem(4, out machine);
            access.GetItem(5, out walls); access.GetItem(6, out bottom); access.GetItem(7, out infill); access.GetItem(8, out size);
            if (w <= 0 || h <= 0 || f <= 0) { access.AddError("Invalid values", "Bead width, layer height and speed must be larger than 0."); return; }
            machine = Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, machine));
            var st = new PrintSettings { BeadWidth = w, LayerHeight = h, Speed = f, Machine = MachineProfile.Preset(machine) };
            var log = new List<string>();
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), Math.Max(0.01, st.Resolution * 0.025), log);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            var layers = SliceEngine.Slice(mesh, Plane.WorldXY, st.LayerHeight, st.PointSpacing, log);
            var r = EasySlicer.Run(layers, st, new EasySlicer.Options { Walls = walls, BottomLayers = Math.Max(0, bottom), Infill = infill, InfillSize = size });
            var report = new List<string> { "DONE: " + string.Join(" > ", r.Steps) };
            if (r.Check != null) report.AddRange(r.Check.Report);
            foreach (var note in r.Notes.Concat(log).Distinct()) report.Add("Note: " + note);
            if (r.Path == null || r.Path.Count < 2)
            {
                access.AddError("No print path", r.Notes.FirstOrDefault() ?? "No print path could be made.");
                access.SetItem(2, false); access.SetTwig(3, report.ToArray(), null, null);
                return;
            }
            if (!r.Check.Ok) access.AddWarning("The check found something", string.Join("\n", r.Check.Report));
            report.Add(r.Ok ? "NEXT: connect Toolpath to Ginger G-code, Robot Program or Desktop Test Print." : "NEXT: fix what the check reports, then connect Toolpath to an output component.");
            access.SetItem(0, r.Path);
            access.SetItem(1, r.Path.ToPolyline());
            access.SetItem(2, r.Ok);
            access.SetTwig(3, report.ToArray(), null, null);
        }
    }
}
