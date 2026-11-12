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
            inputs.AddText("Name", "N", "Name of the part, for the file name: GIN_2611051216_Test Object_2668g_53min.gcode.").Set("Print");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("G-code", "G", "G-code, line by line.", Access.Twig);
            outputs.AddText("Info", "I", "Layers, length, extrusion and time.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            string file, part; bool save; access.GetItem(1, out file); access.GetItem(2, out save); access.GetItem(3, out part);
            var r = GingerGCode.Build(tp);
            foreach (var w in r.Warnings) access.AddWarning(w.Length > 50 ? w.Substring(0, 50) + "…" : w, w);
            Files.Save(access, save, file, r.Lines, OutputFile.Name("GIN", part, tp, ".gcode"));
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
            inputs.AddNumber("Filament Diameter", "Fil", "Filament diameter in mm.").Set(1.75);
            inputs.AddNumber("Nozzle Temperature", "Tn", "Degrees C.").Set(215.0);
            inputs.AddNumber("Bed Temperature", "Tb", "Degrees C.").Set(60.0);
            inputs.AddNumber("Speed", "V", "Print speed in mm/s. The first layer runs at half of it.").Set(40.0);
            inputs.AddText("File", "File", "Optional: file or folder. Empty = the Desktop.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
            inputs.AddText("Name", "N", "Name of the part, for the file name.").Set("Print");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("G-code", "G", "G-code, line by line.", Access.Twig);
            outputs.AddPolyline("Path", "P", "The scaled path as it lies on the printer bed.");
            outputs.AddText("Info", "I", "Size, bead, filament and time.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            int printer; double scale, nozzle, filament, tn, tb, speed; string file; bool save;
            access.GetItem(1, out printer); access.GetItem(2, out scale); access.GetItem(3, out nozzle); access.GetItem(4, out filament);
            access.GetItem(5, out tn); access.GetItem(6, out tb); access.GetItem(7, out speed); access.GetItem(8, out file); access.GetItem(9, out save);
            string part; access.GetItem(10, out part);
            if (nozzle <= 0 || filament <= 0) { access.AddError("Invalid values", "Nozzle and filament diameter must be larger than 0."); return; }
            printer = Math.Max(0, Math.Min(DesktopPrinter.Presets.Length - 1, printer));
            var r = DesktopGCode.Build(tp, DesktopPrinter.Presets[printer], scale, nozzle, filament, tn, tb, speed, 1.0);
            foreach (var w in r.Warnings) access.AddWarning(w.Length > 50 ? w.Substring(0, 50) + "…" : w, w);
            Files.Save(access, save, file, r.Lines, OutputFile.Name("TEST", part, null, ".gcode"));
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
            inputs.AddInteger("Machine Profile", "MP", string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddInteger("Walls", "N", "Number of walls.").Set(1);
            inputs.AddInteger("Bottom Layers", "B", "Solid layers at the bottom. 0 = open.").Set(0);
            inputs.AddInteger("Infill", "I", string.Join("\n", EasySlicer.Infills.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddNumber("Infill Size", "Sz", "Rib distance, wavelength or cell size in mm. 0 = chosen from the bead width.").Set(0.0);
            inputs.AddInteger("Brim", "Br", "Three beads right against the first layer, so the part holds on to the bed.\n0 = none\n1 = outside\n2 = inside\n3 = both sides").Set(0);
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
            int brim; access.GetItem(9, out brim);
            if (w <= 0 || h <= 0 || f <= 0) { access.AddError("Invalid values", "Bead width, layer height and speed must be larger than 0."); return; }
            machine = Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, machine));
            var st = new PrintSettings { BeadWidth = w, LayerHeight = h, Speed = f, Machine = MachineProfile.Preset(machine) };
            var log = new List<string>();
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), Math.Max(0.01, st.Resolution * 0.025), log);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            var layers = SliceEngine.Slice(mesh, Plane.WorldXY, st.LayerHeight, st.PointSpacing, log);
            var r = EasySlicer.Run(layers, st, new EasySlicer.Options { Walls = walls, BottomLayers = Math.Max(0, bottom), Infill = infill, InfillSize = size, BrimSide = Math.Max(0, Math.Min(3, brim)) });
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

    // ============================================================== machines
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d3004")]
    public sealed class MachineComponent : Component
    {
        public MachineComponent() : base(Io.Name("Machine", "A machine that prints from G-code: its start code, end code, layer code and how it wants the extrusion. The Ginger One is built in. Add your own machine as a .machine text file in Documents/LARGER/Machines, or type its codes into the inputs here and set Save once. Connect the result to Machine G-code. The Weber DXR on the robot has its own components.", Io.Slicer, 11)) { }
        public MachineComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Machine", "M", "0 = Ginger One\n1 = Generic G-code machine (Marlin)\nthen your own machines from Documents/LARGER/Machines, in the order of their file names.").Set(0);
            inputs.AddText("File", "File", "Optional. A .machine file anywhere on the computer: used instead of the list.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Name", "N", "Optional. Name of your own machine: everything you do not set below is taken from the machine chosen above.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Start Code", "Start", "Optional. Your start code, one line per item.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("Layer Code", "Layer", "Optional. Code at every layer change. {layer} is the number of the layer, {z} its height.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("End Code", "End", "Optional. Your end code, one line per item.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Extrusion Factor", "Ef", "E = bead width x layer height x length x flow x this factor. 0 = as the machine chosen above.").Set(0.0);
            inputs.AddBoolean("Save", "Save", "True writes this machine as a .machine file into Documents/LARGER/Machines.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Machine", "M", "The machine: connect to Machine G-code.");
            outputs.AddText("Definition", "Def", "The machine as text, the way it is stored in a .machine file.", Access.Twig);
            outputs.AddText("Info", "I", "Where the machine comes from and what it does.");
        }

        protected override void Process(IDataAccess access)
        {
            int index; string file, name; double factor; bool save; string[] start, layer, end;
            access.GetItem(0, out index); access.GetItem(1, out file); access.GetItem(2, out name);
            bool hasStart = access.GetItemArray(3, out start) && start != null && start.Length > 0;
            bool hasLayer = access.GetItemArray(4, out layer) && layer != null && layer.Length > 0;
            bool hasEnd = access.GetItemArray(5, out end) && end != null && end.Length > 0;
            access.GetItem(6, out factor); access.GetItem(7, out save);

            MachineDefinition m;
            if (!string.IsNullOrWhiteSpace(file))
            {
                string path = file.Trim().Trim('"');
                if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                m = MachineDefinition.Load(path, out string error);
                if (m == null) { access.AddError("Machine file not read", error); return; }
            }
            else
            {
                var log = new List<string>();
                var all = MachineDefinition.All(log);
                foreach (var l in log) access.AddWarning("Machine file left out", l);
                if (index < 0 || index >= all.Count) { access.AddWarning("Unknown machine", $"There are {all.Count} machines (0 to {all.Count - 1}); using the first."); index = 0; }
                m = all[index].Duplicate();
            }
            Func<string[], List<string>> lines = t => t.SelectMany(x => (x ?? "").Replace("\r", "").Split('\n')).ToList();
            bool own = !string.IsNullOrWhiteSpace(name) || hasStart || hasLayer || hasEnd || factor > 0;
            if (!string.IsNullOrWhiteSpace(name)) { m.Name = name.Trim(); m.Prefix = OutputFile.Clean(m.Name).Replace(" ", "").ToUpperInvariant(); if (m.Prefix.Length > 4) m.Prefix = m.Prefix.Substring(0, 4); }
            if (hasStart) m.Start = lines(start);
            if (hasLayer) m.Layer = lines(layer);
            if (hasEnd) m.End = lines(end);
            if (factor > 0) m.Profile.Factor = factor;
            m.Profile.Name = m.Name;
            if (own) m.Source = "made here";
            if (m.Start.Count == 0) access.AddWarning("No start code", "The G-code begins with the first move.");
            if (save)
            {
                if (MachineDefinition.BuiltIn().Any(b => string.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase))) access.AddWarning("Not saved", m.Name + " is built in. Give your machine a Name of its own.");
                else
                {
                    try { string path = m.Save(); m.Source = path; access.AddRemark("Saved", path); }
                    catch (Exception ex) { access.AddError("Could not save", ex.Message); }
                }
            }
            access.SetItem(0, m);
            access.SetTwig(1, m.ToText().ToArray(), null, null);
            access.SetItem(2, $"{m.Name} ({m.Source}): {m.Start.Count} lines of start code, {m.Layer.Count} per layer, {m.End.Count} of end code, {m.Profile}. Your own machines: {MachineDefinition.Folder}");
        }
    }

    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d3005")]
    public sealed class MachineGCodeComponent : Component
    {
        public MachineGCodeComponent() : base(Io.Name("Machine G-code", "Writes the toolpath as G-code for the machine you connect: its start code, the moves with one constant feedrate that is never 0, its layer code at every layer change, its end code.", Io.Slicer, 12)) { }
        public MachineGCodeComponent(IReader reader) : base(reader) { }

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
            inputs.AddGeneric("Machine", "M", "The machine, from the Machine component. Empty = Ginger One.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Name", "N", "Name of the part, for the file name.").Set("Print");
            inputs.AddText("File", "File", "Optional: file or folder. Empty = the Desktop, named with the machine's letters, date and time, the Name, weight and print time.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("G-code", "G", "G-code, line by line.", Access.Twig);
            outputs.AddText("Info", "I", "Layers, length, extrusion, time and weight.");
            outputs.AddText("File Name", "Fn", "The name the file gets on the Desktop.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            var machine = Io.Get<MachineDefinition>(access, 1) ?? MachineDefinition.GingerOne();
            string part, file; bool save; access.GetItem(2, out part); access.GetItem(3, out file); access.GetItem(4, out save);
            var r = MachineGCode.Build(tp, machine);
            foreach (var w in r.Warnings) access.AddWarning(w.Length > 50 ? w.Substring(0, 50) + "…" : w, w);
            var named = tp.Duplicate(); named.Settings = tp.Settings.Duplicate(); named.Settings.Machine = machine.Profile.Duplicate();
            string fileName = OutputFile.Name(machine.Prefix, part, named, machine.Extension);
            Files.Save(access, save, file, r.Lines, fileName);
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            access.SetItem(1, r.Info);
            access.SetItem(2, fileName);
        }
    }
}
