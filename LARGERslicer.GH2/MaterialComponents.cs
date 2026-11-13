using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== material library
    [IoId("107e5be8-8283-47d4-b596-6194b031c92c")]
    public sealed class MaterialComponent : Component
    {
        public MaterialComponent() : base(Io.Name("Material", "Start values for a pellet material: temperatures of the feed zone, the three heat zones, the nozzle and the bed, drying and density. The built-in materials use the start values of the Weber DXR quick guide; the densities are typical values. Add your own material as a .material text file in Documents/LARGER/Materials, or fill in the inputs and set Save once. Wire Density into the Density input of Print Settings.", Io.Slicer, Group.Start)) { }
        public MaterialComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerMaterialIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Material", "M", "Which material:\n" + string.Join("\n", MaterialDefinition.BuiltIn().Select((m, i) => i + " = " + m.Name)) + "\nthen your own materials from Documents/LARGER/Materials, in the order of their file names.").Set(0);
            inputs.AddText("Name", "N", "Optional. Name of your own material: everything you do not set below is taken from the material chosen above.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Feed Zone", "T0", "Feed zone in degrees C. 0 = as the material chosen above.").Set(0.0);
            inputs.AddText("Zone 1", "T1", "Optional. Heat zone 1 in degrees C: one value or a range like 200-210.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Zone 2", "T2", "Optional. Heat zone 2 in degrees C.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Zone 3", "T3", "Optional. Heat zone 3 in degrees C.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Nozzle", "Tn", "Optional. Nozzle in degrees C.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Bed", "Tb", "Optional. Print bed in degrees C.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Drying", "Dry", "Optional. Drying, written like 6 h at 65.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Density", "D", "Density in g/cm3. 0 = as the material chosen above.").Set(0.0);
            inputs.AddNumber("Factor", "Fa", "The material factor you found with Material Factor. 0 = none.").Set(0.0);
            inputs.AddBoolean("Save", "Save", "True writes this material as a .material file into Documents/LARGER/Materials.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Material", "M", "The material.");
            outputs.AddText("Temperatures", "T", "The temperatures as text, one line per zone.", Access.Twig);
            outputs.AddNumber("Zones", "Z", "Start values in degrees C (middle of each range): feed zone, zone 1, zone 2, zone 3, nozzle.", Access.Twig);
            outputs.AddNumber("Bed", "Tb", "Bed temperature in degrees C (middle of the range).");
            outputs.AddNumber("Density", "D", "Density in g/cm3. Wire it into the Density input of Print Settings.");
            outputs.AddText("Drying", "Dry", "How long and how hot to dry the pellets, and the largest residual moisture.");
            outputs.AddText("Definition", "Def", "The material as text, the way it is stored in a .material file.", Access.Twig);
            outputs.AddText("Info", "I", "Where the values come from.");
        }

        protected override void Process(IDataAccess access)
        {
            int index; access.GetItem(0, out index);
            var log = new List<string>();
            var all = MaterialDefinition.All(log);
            foreach (var l in log) access.AddWarning("Material file left out", l);
            if (index < 0 || index >= all.Count) { access.AddWarning("Unknown material", $"There are {all.Count} materials (0 to {all.Count - 1}); using the first."); index = 0; }
            var m = all[index].Duplicate();
            bool own = false;
            string name; access.GetItem(1, out name);
            if (!string.IsNullOrWhiteSpace(name)) { m.Name = name.Trim(); own = true; }
            double feed; access.GetItem(2, out feed); if (feed > 0) { m.FeedZone = feed; own = true; }
            string[] labels = { "Zone 1", "Zone 2", "Zone 3", "Nozzle", "Bed" };
            for (int k = 0; k < 5; k++)
            {
                string t; access.GetItem(3 + k, out t);
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (!ValueRange.TryParse(t, out ValueRange r)) { access.AddError("Invalid " + labels[k], labels[k] + ": give one value or a range like 200-210."); return; }
                if (k == 0) m.Zone1 = r; else if (k == 1) m.Zone2 = r; else if (k == 2) m.Zone3 = r; else if (k == 3) m.Nozzle = r; else m.Bed = r;
                own = true;
            }
            string dry; access.GetItem(8, out dry);
            if (!string.IsNullOrWhiteSpace(dry))
            {
                var parsed = MaterialDefinition.Parse(new[] { "name = x", "drying = " + dry }, out string error);
                if (parsed == null) { access.AddError("Invalid drying", "Write it like 6 h at 65."); return; }
                m.DryHours = parsed.DryHours; m.DryTemperature = parsed.DryTemperature; own = true;
            }
            double density, factor; bool save;
            access.GetItem(9, out density); access.GetItem(10, out factor); access.GetItem(11, out save);
            if (density > 0) { m.Density = density; m.DensityTypical = false; own = true; }
            if (factor > 0) { m.Factor = factor; own = true; }
            if (own) m.Source = "made here";
            if (save)
            {
                if (MaterialDefinition.BuiltIn().Any(b => string.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase))) access.AddWarning("Not saved", m.Name + " is built in. Give your material a Name of its own.");
                else
                {
                    try { string path = m.Save(); m.Source = path; access.AddRemark("Saved", path); }
                    catch (Exception ex) { access.AddError("Could not save", ex.Message); }
                }
            }
            var text = new[]
            {
                $"Feed zone: {m.FeedZone.ToString("0.#", CultureInfo.InvariantCulture)} °C", $"Zone 1: {m.Zone1} °C", $"Zone 2: {m.Zone2} °C",
                $"Zone 3: {m.Zone3} °C", $"Nozzle: {m.Nozzle} °C", $"Bed: {m.Bed} °C",
            };
            access.SetItem(0, m);
            access.SetTwig(1, text, null, null);
            access.SetTwig(2, new[] { m.FeedZone, m.Zone1.Mid, m.Zone2.Mid, m.Zone3.Mid, m.Nozzle.Mid }, null, null);
            access.SetItem(3, m.Bed.Mid);
            access.SetItem(4, m.Density);
            access.SetItem(5, m.DryingText);
            access.SetTwig(6, m.ToText().ToArray(), null, null);
            access.SetItem(7, $"{m.Name} ({m.Source}). {(m.Source == "built in" ? MaterialDefinition.WeberSource + ". " : "")}Density {m.Density.ToString("0.##", CultureInfo.InvariantCulture)} g/cm3{(m.DensityTypical ? " (typical value, not measured)" : "")}. Start in the middle of each range and adjust on the machine. Your own materials: {MaterialDefinition.Folder}");
        }
    }

    // ============================================================== material factor
    [IoId("d547afea-d03e-4e35-abd9-265d6e01a846")]
    public sealed class MaterialFactorComponent : Component
    {
        public MaterialFactorComponent() : base(Io.Name("Material Factor", "Finds the material factor from a test print: new factor = target width / measured width x current factor, with the mean of all measurements. A single-wall test (the Weber Calibration part with one wall) gives the bead width as the wall thickness.", Io.Slicer, Group.Start)) { }
        public MaterialFactorComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerMaterialFactorIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Target Width", "W", "The bead width you want, in mm.").Set(5.0);
            inputs.AddNumber("Measured Widths", "M", "Bead widths measured on the test print, in mm. Several values: the mean is used.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Current Factor", "F", "The factor the test was printed with.").Set(1.0);
            inputs.AddNumber("Wall Thickness", "T", "Optional. Wall thicknesses measured on a single-wall test, in mm; counted as bead widths.", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddNumber("New Factor", "F", "The factor to print with.");
            outputs.AddText("Report", "R", "What was measured and what it means.", Access.Twig);
            outputs.AddNumber("Off", "%", "How far the bead was off, in percent of the target width (+ = too wide).");
            outputs.AddNumber("Mean", "Mean", "Mean of the measurements in mm.");
        }

        protected override void Process(IDataAccess access)
        {
            double target, current; double[] widths, walls;
            access.GetItem(0, out target); access.GetItemArray(1, out widths); access.GetItem(2, out current); access.GetItemArray(3, out walls);
            var r = MaterialFactor.Calculate(target, widths ?? new double[0], current, walls ?? new double[0]);
            access.SetTwig(1, r.Report.ToArray(), null, null);
            if (r.Count == 0) { access.AddWarning("No measurement", r.Report.FirstOrDefault() ?? ""); return; }
            if (!r.Ok) access.AddWarning("Check the test", r.Report.Last());
            access.SetItem(0, Math.Round(r.NewFactor, 4));
            access.SetItem(2, r.Deviation);
            access.SetItem(3, r.Mean);
        }
    }

    // ============================================================== import foreign G-code
    [IoId("7d9428d9-918a-458d-9dea-338d37918d20")]
    public sealed class ImportGCodeComponent : Component
    {
        public ImportGCodeComponent() : base(Io.Name("Import G-code", "Reads G-code from Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build or a Grasshopper script, or a Weber .dxr file, and checks it with the same Path Check as our own paths: starts and stops, travel moves, retractions, feedrate changes and F0, extrusion into the bed, unsupported beads, crossings, time and weight.", Io.Slicer, Group.Curves)) { }
        public ImportGCodeComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerImportGCodeIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("File", "File", "The G-code or .dxr file.");
            inputs.AddNumber("Bead Width", "W", "Bead width in mm. 0 = estimated from E per mm and the layer height.").Set(0.0);
            inputs.AddNumber("Layer Height", "H", "Layer height in mm. 0 = estimated from the Z steps.").Set(0.0);
            inputs.AddGeneric("Machine Profile", "MP", "Optional. What E means in the file: a Machine, or a preset number:\n" + string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n)) + "\nEmpty = E is a volume in mm3 (width x height x length), as Cura writes it with the Weber profiles.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Density", "D", "Density of the material in g/cm3, for the weight. Wire it from Material.").Set(1.25);
            inputs.AddBoolean("Start Code", "SC", "True keeps the moves of the start and end code (homing, lifting).").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The file as a toolpath: connect to Path Check, Robot Program or the DXR components.");
            outputs.AddPolyline("Path", "P", "The whole path as a polyline.");
            outputs.AddText("Report", "R", "What was read and what the check found.", Access.Twig);
            outputs.AddPolyline("Layers", "L", "One polyline per layer.", Access.Twig);
        }

        private string _key;
        private GCodeImport.Result _cache;

        protected override void Process(IDataAccess access)
        {
            string file; double w, h, density; bool keep; object mpIn;
            access.GetItem(0, out file); access.GetItem(1, out w); access.GetItem(2, out h); access.GetItem(3, out mpIn); access.GetItem(4, out density); access.GetItem(5, out keep);
            if (string.IsNullOrWhiteSpace(file)) { access.AddWarning("No file", "Connect a G-code or .dxr file."); return; }
            MachineProfile machine = null;
            if (mpIn is MachineProfile mp) machine = mp;
            else if (mpIn is MachineDefinition md) machine = md.Profile;
            else if (mpIn is int i) machine = MachineProfile.Preset(Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, i)));
            else if (mpIn is double d) machine = MachineProfile.Preset(Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, (int)Math.Round(d))));
            else if (mpIn != null) access.AddWarning("Machine Profile", "Connect a Machine or a preset number. E is read as a volume.");
            string path = file.Trim().Trim('"');
            if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            if (!File.Exists(path)) { access.AddError("File not found", "The file " + path + " does not exist."); return; }
            var info = new FileInfo(path);
            string key = string.Join("|", path, info.Length, info.LastWriteTimeUtc.Ticks, w, h, density, keep, machine?.ToString() ?? "-", machine?.FilamentDiameter ?? 0);
            GCodeImport.Result r;
            if (key == _key && _cache != null) r = _cache;
            else
            {
                try { r = GCodeImport.Read(path, new GCodeImport.Options { BeadWidth = Math.Max(0, w), LayerHeight = Math.Max(0, h), Machine = machine, Density = density, KeepSetupMoves = keep }); }
                catch (Exception ex) { access.AddError("Could not read the file", ex.Message); return; }
                _key = key; _cache = r;
            }
            foreach (var wmsg in r.Warnings) access.AddWarning(wmsg.Length > 50 ? wmsg.Substring(0, 50) + "…" : wmsg, wmsg);
            if (r.Check != null && !r.Check.Ok) access.AddWarning("Not printable as it is", string.Join("\n", r.Check.Report));
            access.SetTwig(2, r.Report.ToArray(), null, null);
            if (r.Path == null || r.Path.Count < 2) { access.AddWarning("No print", "No printing move in the file."); return; }
            access.SetItem(0, r.Path);
            access.SetItem(1, r.Path.ToPolyline());
            access.SetTwig(3, r.Layers.ToArray(), null, null);
        }
    }
}
