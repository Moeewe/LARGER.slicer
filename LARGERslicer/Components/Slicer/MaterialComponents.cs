using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    // ============================================================== material library
    public class MaterialComponent : SlicerComponent
    {
        public MaterialComponent()
            : base("Material", "Material",
                  "Start values for a pellet material: temperatures of the feed zone, the three heat zones, the nozzle and the bed, drying and density. The built-in materials use the start values of the Weber DXR quick guide; the densities are typical values. Add your own material as a .material text file in Documents/LARGER/Materials, or fill in the inputs and press Save. Wire Density into the Density input of Print Settings, so file names and reports show the right weight.") { }

        public override Guid ComponentGuid => new Guid("431883EB-2D9A-4C53-83B0-AC45FA18F251");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMaterialIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Material", "M", "Which material: the built-in ones first, then your own from Documents/LARGER/Materials in the order of their file names", GH_ParamAccess.item, 0);
            p.AddTextParameter("Name", "N", "Optional. Name of your own material: everything you do not set below is taken from the material chosen above", GH_ParamAccess.item);
            p.AddNumberParameter("Feed Zone", "T0", "Optional. Feed zone (intake of the screw) in degrees C", GH_ParamAccess.item);
            p.AddTextParameter("Zone 1", "T1", "Optional. Heat zone 1 in degrees C: one value or a range like 200-210", GH_ParamAccess.item);
            p.AddTextParameter("Zone 2", "T2", "Optional. Heat zone 2 in degrees C", GH_ParamAccess.item);
            p.AddTextParameter("Zone 3", "T3", "Optional. Heat zone 3 in degrees C", GH_ParamAccess.item);
            p.AddTextParameter("Nozzle", "Tn", "Optional. Nozzle in degrees C", GH_ParamAccess.item);
            p.AddTextParameter("Bed", "Tb", "Optional. Print bed in degrees C", GH_ParamAccess.item);
            p.AddTextParameter("Drying", "Dry", "Optional. Drying, written like 6 h at 65", GH_ParamAccess.item);
            p.AddNumberParameter("Density", "D", "Optional. Density in g/cm3", GH_ParamAccess.item);
            p.AddNumberParameter("Factor", "Fa", "Optional. The material factor you found with Material Factor, so it is kept with the material", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write this material as a .material file into Documents/LARGER/Materials, so it is in the list next time and you can pass it on", GH_ParamAccess.item, false);
            for (int i = 1; i <= 10; i++) p[i].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddButton(doc, 11, "Save material");
            AddValueList(doc, 0, "Material", MaterialDefinition.All().Select(m => m.Name).ToArray());
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Material", "M", "The material", GH_ParamAccess.item);
            p.AddTextParameter("Temperatures", "T", "The temperatures as text, one line per zone", GH_ParamAccess.list);
            p.AddNumberParameter("Zones", "Z", "Start values in degrees C (middle of each range): feed zone, zone 1, zone 2, zone 3, nozzle", GH_ParamAccess.list);
            p.AddNumberParameter("Bed", "Tb", "Bed temperature in degrees C (middle of the range)", GH_ParamAccess.item);
            p.AddNumberParameter("Density", "D", "Density in g/cm3. Wire it into the Density input of Print Settings", GH_ParamAccess.item);
            p.AddTextParameter("Drying", "Dry", "How long and how hot to dry the pellets, and the largest residual moisture", GH_ParamAccess.item);
            p.AddTextParameter("Definition", "Def", "The material as text, the way it is stored in a .material file", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Where the values come from", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int index = 0; da.GetData(0, ref index);
            var log = new List<string>();
            var all = MaterialDefinition.All(log);
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Material file left out. " + l);
            if (index < 0 || index >= all.Count) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"There are {all.Count} materials (0 to {all.Count - 1}); using the first."); index = 0; }
            var m = all[index].Duplicate();

            string name = null, dry = null;
            double feed = 0, density = 0, factor = 0; bool save = false;
            bool own = false;
            if (da.GetData(1, ref name) && !string.IsNullOrWhiteSpace(name)) { m.Name = name.Trim(); own = true; }
            if (da.GetData(2, ref feed)) { m.FeedZone = feed; own = true; }
            if (!Range(da, 3, "Zone 1", ref m.Zone1, ref own)) return;
            if (!Range(da, 4, "Zone 2", ref m.Zone2, ref own)) return;
            if (!Range(da, 5, "Zone 3", ref m.Zone3, ref own)) return;
            if (!Range(da, 6, "Nozzle", ref m.Nozzle, ref own)) return;
            if (!Range(da, 7, "Bed", ref m.Bed, ref own)) return;
            if (da.GetData(8, ref dry) && !string.IsNullOrWhiteSpace(dry))
            {
                var parsed = MaterialDefinition.Parse(new[] { "name = x", "drying = " + dry }, out string error);
                if (parsed == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Drying: write it like 6 h at 65."); return; }
                m.DryHours = parsed.DryHours; m.DryTemperature = parsed.DryTemperature; own = true;
            }
            if (da.GetData(9, ref density))
            {
                if (density <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density must be greater than 0."); return; }
                m.Density = density; m.DensityTypical = false; own = true;
            }
            if (da.GetData(10, ref factor) && factor > 0) { m.Factor = factor; own = true; }
            da.GetData(11, ref save);
            if (own) m.Source = "made here";
            if (own && string.IsNullOrWhiteSpace(name)) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Give your material a Name, so its file and its entry in the list are not called " + m.Name + ".");
            if (save)
            {
                if (MaterialDefinition.BuiltIn().Any(b => string.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase)))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not saved: " + m.Name + " is built in. Give your material a Name of its own.");
                else
                {
                    try { string path = m.Save(); m.Source = path; AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + path + ". New Material components show it in their list."); }
                    catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the material file: " + ex.Message); }
                }
            }

            Message = m.Name;
            var text = new List<string>
            {
                $"Feed zone: {m.FeedZone.ToString("0.#", CultureInfo.InvariantCulture)} °C",
                $"Zone 1: {m.Zone1} °C",
                $"Zone 2: {m.Zone2} °C",
                $"Zone 3: {m.Zone3} °C",
                $"Nozzle: {m.Nozzle} °C",
                $"Bed: {m.Bed} °C",
            };
            da.SetData(0, new GH_ObjectWrapper(m));
            da.SetDataList(1, text);
            da.SetDataList(2, new[] { m.FeedZone, m.Zone1.Mid, m.Zone2.Mid, m.Zone3.Mid, m.Nozzle.Mid });
            da.SetData(3, m.Bed.Mid);
            da.SetData(4, m.Density);
            da.SetData(5, m.DryingText);
            da.SetDataList(6, m.ToText());
            da.SetData(7, $"{m.Name} ({m.Source}). {(m.Source == "built in" ? MaterialDefinition.WeberSource + ". " : "")}Density {m.Density:0.##} g/cm3{(m.DensityTypical ? " (typical value, not measured)" : "")}." +
                          (m.Factor > 0 ? $" Material factor {m.Factor:0.###}." : "") + $" Start in the middle of each range and adjust on the machine. Your own materials: {MaterialDefinition.Folder}");
        }

        private bool Range(IGH_DataAccess da, int input, string label, ref ValueRange target, ref bool own)
        {
            string t = null;
            if (!da.GetData(input, ref t) || string.IsNullOrWhiteSpace(t)) return true;
            if (!ValueRange.TryParse(t, out ValueRange r)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{label}: give one value or a range like 200-210."); return false; }
            target = r; own = true;
            return true;
        }
    }

    // ============================================================== material factor
    public class MaterialFactorComponent : SlicerComponent
    {
        public MaterialFactorComponent()
            : base("Material Factor", "Factor",
                  "Finds the material factor from a test print. Print a test with your current factor, measure the bead width at a few places and enter the values: new factor = target width / measured width x current factor (the mean of all measurements). A single-wall test (the Weber Calibration part with one wall, spiral mode) gives the bead width as the wall thickness: enter it under Wall Thickness. Use the new factor as Factor in Machine Profile (or as P1 on the Weber DXR) and print the test again.") { }

        public override Guid ComponentGuid => new Guid("BB0586D8-7FFE-47B3-8BD7-27C3E84D353A");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMaterialFactorIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Target Width", "W", "The bead width you want (mm), as in Print Settings", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Measured Widths", "M", "Bead widths measured on the test print (mm). Several values: the mean is used", GH_ParamAccess.list);
            p.AddNumberParameter("Current Factor", "F", "The factor the test was printed with", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Wall Thickness", "T", "Optional. Wall thicknesses measured on a single-wall test (mm); counted as bead widths", GH_ParamAccess.list);
            p[1].Optional = true;
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("New Factor", "F", "The factor to print with", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was measured and what it means", GH_ParamAccess.list);
            p.AddNumberParameter("Off", "%", "How far the bead was off, in percent of the target width (+ = too wide)", GH_ParamAccess.item);
            p.AddNumberParameter("Mean", "Mean", "Mean of the measurements (mm)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            double target = 5, current = 1;
            var widths = new List<double>(); var walls = new List<double>();
            da.GetData(0, ref target); da.GetDataList(1, widths); da.GetData(2, ref current); da.GetDataList(3, walls);
            var r = MaterialFactor.Calculate(target, widths, current, walls);
            if (r.Count == 0)
            {
                AddRuntimeMessage(r.NewFactor > 0 ? GH_RuntimeMessageLevel.Remark : GH_RuntimeMessageLevel.Error, r.Report.FirstOrDefault() ?? "No measurement.");
                da.SetDataList(1, r.Report);
                if (r.NewFactor > 0) da.SetData(0, r.NewFactor);
                return;
            }
            if (!r.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.Last());
            Message = $"{r.NewFactor:0.###} ({(r.Deviation >= 0 ? "+" : "")}{r.Deviation:0.#} %)";
            da.SetData(0, Math.Round(r.NewFactor, 4));
            da.SetDataList(1, r.Report);
            da.SetData(2, r.Deviation);
            da.SetData(3, r.Mean);
        }
    }

    // ============================================================== import foreign G-code
    public class ImportGCodeComponent : SlicerComponent
    {
        public ImportGCodeComponent()
            : base("Import G-code", "Import",
                  "Reads G-code from Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build or a Grasshopper script, or a Weber .dxr file, and checks it with the same Path Check as our own paths: starts and stops of the extrusion, travel moves, retractions, feedrate changes and F0, extrusion into the bed, beads that hang in the air, crossings, time and weight. The Toolpath output goes on to Path Check, Robot Program or the DXR components.") { }

        public override Guid ComponentGuid => new Guid("A7460FF2-051E-4F89-9639-B704E6616F10");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerImportGCodeIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("File", "File", "The G-code or .dxr file", GH_ParamAccess.item);
            p.AddNumberParameter("Bead Width", "W", "Bead width (mm). 0 = estimated from E per mm and the layer height", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Layer Height", "H", "Layer height (mm). 0 = estimated from the Z steps", GH_ParamAccess.item, 0.0);
            p.AddGenericParameter("Machine Profile", "MP", "Optional. What E means in the file: a Machine Profile, a Machine, or a preset number of Machine Profile. Empty = E is a volume in mm3 (width x height x length), as Cura writes it with the Weber profiles and as the DXR carries it", GH_ParamAccess.item);
            p.AddNumberParameter("Density", "D", "Density of the material (g/cm3), for the weight. Wire it from Material", GH_ParamAccess.item, 1.25);
            p.AddBooleanParameter("Start Code", "SC", "True = keep the moves of the start and end code (homing, lifting). False = the path begins with the move onto the first bead", GH_ParamAccess.item, false);
            p[3].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddToggle(doc, 5, "Keep start code", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The file as a toolpath: connect to Path Check, Robot Program or the DXR components", GH_ParamAccess.item);
            p.AddCurveParameter("Path", "P", "The whole path as a polyline", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was read and what the check found", GH_ParamAccess.list);
            p.AddCurveParameter("Layers", "L", "One polyline per layer", GH_ParamAccess.list);
        }

        private string _key;
        private GCodeImport.Result _cache;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string file = null; double w = 0, h = 0, density = 1.25; bool keep = false;
            if (!da.GetData(0, ref file) || string.IsNullOrWhiteSpace(file)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a G-code or .dxr file."); return; }
            da.GetData(1, ref w); da.GetData(2, ref h); da.GetData(4, ref density); da.GetData(5, ref keep);
            MachineProfile machine = null;
            IGH_Goo goo = null;
            if (da.GetData(3, ref goo) && goo != null)
            {
                object v = goo.ScriptVariable();
                if (v is MachineProfile mp) machine = mp;
                else if (v is MachineDefinition md) machine = md.Profile;
                else if (v is int i) machine = MachineProfile.Preset(Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, i)));
                else if (v is double d) machine = MachineProfile.Preset(Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, (int)Math.Round(d))));
                else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Machine Profile: connect a Machine Profile, a Machine or a preset number. E is read as a volume.");
            }
            string path = file.Trim().Trim('"');
            if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            if (!File.Exists(path)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The file " + path + " does not exist."); return; }

            var info = new FileInfo(path);
            string key = string.Join("|", path, info.Length, info.LastWriteTimeUtc.Ticks, w, h, density, keep, machine?.ToString() ?? "-", machine?.FilamentDiameter ?? 0);
            GCodeImport.Result r;
            if (key == _key && _cache != null) r = _cache;
            else
            {
                try { r = GCodeImport.Read(path, new GCodeImport.Options { BeadWidth = Math.Max(0, w), LayerHeight = Math.Max(0, h), Machine = machine, Density = density, KeepSetupMoves = keep }); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not read the file: " + ex.Message); return; }
                _key = key; _cache = r;
            }
            foreach (var wmsg in r.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, wmsg);
            if (r.Check != null && !r.Check.Ok)
            {
                string first = r.Check.Report.FirstOrDefault(l => l.StartsWith("CROSSES") || l.StartsWith("NOT") || l.StartsWith("INTO") || l.StartsWith("TOO") || l.StartsWith("INVALID") || l.StartsWith("Bead shape:"));
                if (first != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, first);
            }
            if (r.Path == null || r.Path.Count < 2) { da.SetDataList(2, r.Report); AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No printing move in the file."); return; }
            Message = $"{r.LayerCount} layers, {(r.Starts <= 1 ? "1 line" : r.Starts + " lines")}";
            da.SetData(0, new GH_Toolpath(r.Path));
            da.SetData(1, new PolylineCurve(r.Path.Points));
            da.SetDataList(2, r.Report);
            da.SetDataList(3, r.Layers.Select(l => new PolylineCurve(l)));
        }
    }
}
