using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// A machine that prints from G-code: its name, the code it needs before and after the print
    /// and at every layer change, and how it wants the extrusion value. The machines of the
    /// FH Muenster are built in; anyone can add their own as a small text file (ending .machine)
    /// in the folder Documents/LARGER/Machines, or put one together with the Machine component.
    ///
    /// The file reads like this:
    ///
    ///   name = My printer
    ///   prefix = MYP                 (start of the file names, a few letters)
    ///   extension = .gcode
    ///   bed = 1000 x 1000            (mm)
    ///   extrusion factor = 0.21      (E = width x height x length x flow x this)
    ///   first layer factor = 1
    ///   filament diameter = 0        (0 = E is a volume; else E is a length of filament)
    ///   section = rectangle          (rectangle = width x height; bead = squashed bead with round sides)
    ///   travel speed = 0             (mm/min; 0 = the highest speed of the print settings)
    ///   material = PLA               (optional: replaces {material} in the codes)
    ///   bed temperature = 90         (optional: replaces {bed} in the codes)
    ///   model = My printer.3dm       (optional: a Rhino file with the machine in 3D, for the preview;
    ///                                 a .3dm of the same name next to the .machine file is found by itself)
    ///   [start]
    ///   G21
    ///   G28
    ///   [layer]
    ///   ; layer {layer} at Z {z}
    ///   [after layer 2]              (optional: written once, when layer 2 is finished - e.g. the fan on)
    ///   M106 S140
    ///   [end]
    ///   M84
    ///
    /// Lines that begin with # are comments of the file and are not written into the G-code.
    /// In [layer], {layer} is the number of the layer (1, 2, 3 ...) and {z} its height.
    /// In every block, {material} is the material and {bed} the bed temperature.
    /// </summary>
    public class MachineDefinition
    {
        public string Name = "G-code machine";
        public string Prefix = "GCO";
        public string Extension = ".gcode";
        public double BedX = 0, BedY = 0;
        public double TravelSpeed = 0;
        /// <summary>Optional: a Rhino file with the machine in 3D (frame, bed, gantry), drawn in the preview. In the coordinates of the machine: the origin is its 0, 0, 0.</summary>
        public string Model = "";
        public MachineProfile Profile = new MachineProfile { RectangleSection = true };
        public List<string> Start = new List<string>(), Layer = new List<string>(), End = new List<string>();
        /// <summary>Code written once when layer n (1, 2, 3 ...) is finished, e.g. to switch the fan on.</summary>
        public SortedDictionary<int, List<string>> AfterLayer = new SortedDictionary<int, List<string>>();
        /// <summary>Fill {material} and {bed} in the codes.</summary>
        public string Material = "PLA";
        public double BedTemperature = 90;
        /// <summary>Something to know about this machine, shown in the Machine component's info.</summary>
        public string Note = "";
        /// <summary>Where the definition comes from: "built in", or the path of its file.</summary>
        public string Source = "built in";

        public override string ToString() => $"Machine {Name} ({Source})";

        public MachineDefinition Duplicate()
        {
            var d = (MachineDefinition)MemberwiseClone();
            d.Profile = Profile.Duplicate();
            d.Start = new List<string>(Start); d.Layer = new List<string>(Layer); d.End = new List<string>(End);
            d.AfterLayer = new SortedDictionary<int, List<string>>();
            foreach (var kv in AfterLayer) d.AfterLayer[kv.Key] = new List<string>(kv.Value);
            return d;
        }

        /// <summary>A line of code with {material} and {bed} filled in.</summary>
        public string Fill(string line) => (line ?? "").Replace("{material}", Material ?? "").Replace("{bed}", Num(BedTemperature));

        // ---------------------------------------------------------------- the machines of the house

        /// <summary>Ginger One (Marlin on the Ginger board): start and end as in the FH Muenster definition.</summary>
        public static MachineDefinition GingerOne() => new MachineDefinition
        {
            Name = "Ginger One", Prefix = "GIN", BedX = 1000, BedY = 1000,
            Profile = MachineProfile.Preset(4),
            Start = GingerGCode.Start.ToList(), End = GingerGCode.End.ToList(),
        };

        /// <summary>A plain start and end for a Marlin or Klipper machine, as a base for your own.</summary>
        public static MachineDefinition GenericMarlin() => new MachineDefinition
        {
            Name = "Generic G-code machine (Marlin)", Prefix = "GCO",
            Profile = new MachineProfile { Name = "Generic G-code machine", RectangleSection = true, Unit = "mm3" },
            Start = new List<string> { "; START CODE", "G21 ; millimetres", "G90 ; absolute positions", "G28 ; home", "G92 E0", "M83 ; extrusion as relative distances", "; end of start code" },
            Layer = new List<string>(),
            End = new List<string> { "; END CODE", "G91", "G1 F200 Z30 ; lift", "G90", "M84 ; motors off", "; end of end code" },
        };

        /// <summary>Ginger One on Klipper firmware: the START_PRINT and END_PRINT macros of its Klipper config.</summary>
        public static MachineDefinition GingerKlipper() => new MachineDefinition
        {
            Name = "Ginger One (Klipper)", Prefix = "GIK", BedX = 1000, BedY = 1000,
            // E as a volume (mm3) with factor 1.0, as the Klipper script did ("klipper 1")
            Profile = new MachineProfile { Name = "Ginger One (Klipper)", Factor = 1.0, RectangleSection = true, Unit = "mm3" },
            Material = "PLA", BedTemperature = 90,
            Note = "E is the volume in mm3 x 1.0: confirm with the Ginger's Klipper rotation_distance.",
            Start = new List<string>
            {
                "; START CODE (Klipper macro). E = volume in mm3 x 1.0: confirm with the Ginger's Klipper rotation_distance",
                "START_PRINT MATERIAL={material} BED_TEMPERATURE={bed} BED_TEMPERATURE_MIN=80 BED_TEMPERATURE_MAX=110 KAMP_LEVELING=0 PURGE_LAYER_HEIGHT=2 PURGE_PARKING_SPEED=12000 PURGE_LENGHT=300 PURGE_SPEED=500 PURGE_MATERIAL_QUANTITY=3000",
                "M83 ; extrusion as relative distances",
                "; end of start code",
            },
            Layer = new List<string>(),
            AfterLayer = new SortedDictionary<int, List<string>> { [1] = new List<string> { "M106 S140 ; fan on" } },
            End = new List<string> { "; END CODE", "G91", "G1 F2000 Z30 ; lift", "G90", "M107 ; fan off", "END_PRINT", "; end of end code" },
        };

        public static List<MachineDefinition> BuiltIn() => new List<MachineDefinition> { GingerOne(), GenericMarlin(), GingerKlipper() };

        /// <summary>The folder for your own machines: Documents/LARGER/Machines.</summary>
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LARGER", "Machines");

        /// <summary>Built-in machines first, then every .machine file of the folder, by name.</summary>
        public static List<MachineDefinition> All(List<string> log = null)
        {
            var list = BuiltIn();
            try
            {
                if (Directory.Exists(Folder))
                    foreach (string file in Directory.GetFiles(Folder, "*.machine").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        var m = Load(file, out string error);
                        if (m != null) list.Add(m); else log?.Add(Path.GetFileName(file) + ": " + error);
                    }
            }
            catch (Exception ex) { log?.Add("The machines folder could not be read: " + ex.Message); }
            return list;
        }

        // ---------------------------------------------------------------- text form

        private static string Num(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public List<string> ToText()
        {
            var t = new List<string>
            {
                "# LARGER.slicer machine. Put this file into " + Folder,
                "name = " + Name,
                "prefix = " + Prefix,
                "extension = " + Extension,
                "bed = " + Num(BedX) + " x " + Num(BedY),
                "extrusion factor = " + Num(Profile.Factor),
                "first layer factor = " + Num(Profile.FirstLayerFactor),
                "filament diameter = " + Num(Profile.FilamentDiameter),
                "section = " + (Profile.RectangleSection ? "rectangle" : "bead"),
                "travel speed = " + Num(TravelSpeed),
                "material = " + Material,
                "bed temperature = " + Num(BedTemperature),
            };
            if (!string.IsNullOrWhiteSpace(Model)) t.Add("model = " + Path.GetFileName(Model));
            t.Add(""); t.Add("[start]");
            t.AddRange(Start); t.Add(""); t.Add("[layer]"); t.AddRange(Layer); t.Add("");
            foreach (var kv in AfterLayer) { t.Add("[after layer " + kv.Key.ToString(CultureInfo.InvariantCulture) + "]"); t.AddRange(kv.Value); t.Add(""); }
            t.Add("[end]"); t.AddRange(End);
            return t;
        }

        public static MachineDefinition Load(string file, out string error)
        {
            try
            {
                var m = Parse(File.ReadAllLines(file), out error);
                if (m != null)
                {
                    m.Source = file;
                    // the 3D model: as named in the file, next to it; else a .3dm of the same name
                    string dir = Path.GetDirectoryName(Path.GetFullPath(file));
                    if (!string.IsNullOrWhiteSpace(m.Model) && !Path.IsPathRooted(m.Model)) m.Model = Path.Combine(dir, m.Model);
                    if (string.IsNullOrWhiteSpace(m.Model)) { string same = Path.ChangeExtension(Path.GetFullPath(file), ".3dm"); if (File.Exists(same)) m.Model = same; }
                }
                return m;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static MachineDefinition Parse(IEnumerable<string> lines, out string error)
        {
            error = null;
            var m = new MachineDefinition { Name = null };
            List<string> block = null;
            int n = 0;
            foreach (string raw in lines ?? new string[0])
            {
                n++;
                string line = raw ?? "";
                string t = line.Trim();
                if (t.StartsWith("#")) continue;
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    string key = t.Substring(1, t.Length - 2).Trim().ToLowerInvariant();
                    if (key == "start") block = m.Start; else if (key == "layer") block = m.Layer; else if (key == "end") block = m.End;
                    else if (key.StartsWith("after layer") && int.TryParse(key.Substring(11).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int after) && after >= 1)
                    {
                        if (!m.AfterLayer.TryGetValue(after, out block)) m.AfterLayer[after] = block = new List<string>();
                    }
                    else { error = $"line {n}: unknown block [{key}]. Use [start], [layer], [after layer 2] and [end]."; return null; }
                    continue;
                }
                if (block != null) { block.Add(line.TrimEnd()); continue; }
                if (t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq < 0) { error = $"line {n}: expected 'key = value' or a block like [start]."; return null; }
                string k = t.Substring(0, eq).Trim().ToLowerInvariant(), v = t.Substring(eq + 1).Trim();
                double d;
                bool isNumber = double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                switch (k)
                {
                    case "name": m.Name = v; break;
                    case "prefix": m.Prefix = v; break;
                    case "extension": m.Extension = v.StartsWith(".") ? v : "." + v; break;
                    case "bed":
                        var parts = v.ToLowerInvariant().Split('x');
                        double bx = 0, by = 0;
                        if (parts.Length != 2 || !double.TryParse(parts[0].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out bx)
                            || !double.TryParse(parts[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out by)) { error = $"line {n}: bed needs two numbers, like 1000 x 1000."; return null; }
                        m.BedX = bx; m.BedY = by; break;
                    case "extrusion factor": if (!isNumber) goto bad; m.Profile.Factor = d; break;
                    case "first layer factor": if (!isNumber) goto bad; m.Profile.FirstLayerFactor = d; break;
                    case "filament diameter": if (!isNumber) goto bad; m.Profile.FilamentDiameter = d; break;
                    case "travel speed": if (!isNumber) goto bad; m.TravelSpeed = d; break;
                    case "model": m.Model = v.Trim('"'); break;
                    case "material": m.Material = v; break;
                    case "bed temperature": if (!isNumber) goto bad; m.BedTemperature = d; break;
                    case "section":
                        if (v.ToLowerInvariant() == "rectangle") m.Profile.RectangleSection = true;
                        else if (v.ToLowerInvariant() == "bead") m.Profile.RectangleSection = false;
                        else { error = $"line {n}: section is rectangle or bead."; return null; }
                        break;
                    default: error = $"line {n}: unknown key '{k}'."; return null;
                }
                continue;
                bad: error = $"line {n}: '{k}' needs a number, got '{v}'."; return null;
            }
            if (string.IsNullOrWhiteSpace(m.Name)) { error = "The machine has no name (name = ...)."; return null; }
            if (m.Profile.Factor <= 0) { error = "The extrusion factor must be larger than 0."; return null; }
            Trim(m.Start); Trim(m.Layer); Trim(m.End);
            foreach (var l in m.AfterLayer.Values) Trim(l);
            m.Profile.Name = m.Name;
            m.Profile.Unit = m.Profile.FilamentDiameter > 0 ? "mm filament" : "E";
            if (string.IsNullOrWhiteSpace(m.Prefix)) m.Prefix = OutputFile.Clean(m.Name).ToUpperInvariant();
            if (m.Prefix.Length > 6) m.Prefix = m.Prefix.Substring(0, 6);
            return m;
        }

        private static void Trim(List<string> l)
        {
            while (l.Count > 0 && l[l.Count - 1].Trim().Length == 0) l.RemoveAt(l.Count - 1);
            while (l.Count > 0 && l[0].Trim().Length == 0) l.RemoveAt(0);
        }

        /// <summary>Writes the definition into the machines folder; returns the path. With a model it is
        /// written next to it as a .3dm of the same name (one layer: Machine).</summary>
        public string Save(IList<Mesh> model = null)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, OutputFile.Clean(Name) + ".machine");
            if (model != null && model.Count > 0)
            {
                var joined = new Mesh(); foreach (var mm in model) if (mm != null) joined.Append(mm);
                string mp = Path.ChangeExtension(path, ".3dm");
                ModelFile.Write(mp, new Dictionary<string, Mesh> { ["Machine"] = joined }, null);
                Model = mp;
            }
            else if (!string.IsNullOrWhiteSpace(Model) && File.Exists(Model) && !string.Equals(Path.GetDirectoryName(Path.GetFullPath(Model)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            {
                // keep the model with the machine, so the folder can be passed on as it is
                string mp = Path.ChangeExtension(path, ".3dm");
                File.Copy(Model, mp, true); Model = mp;
            }
            File.WriteAllLines(path, ToText());
            return path;
        }

        /// <summary>What the preview shows: the 3D model of the machine if it has one, else its bed as a 10 mm plate below Z 0.</summary>
        public List<Mesh> Preview(out string note)
        {
            note = null;
            var list = new List<Mesh>();
            if (!string.IsNullOrWhiteSpace(Model))
            {
                var c = ModelFile.Read(Model, out string error);
                if (c != null) { list.AddRange(c.Meshes.Values); return list; }
                note = "The 3D model could not be read: " + error;
            }
            if (BedX > 0 && BedY > 0)
                list.Add(Mesh.CreateFromBox(new BoundingBox(0, 0, -10, BedX, BedY, 0), 1, 1, 1));
            return list;
        }
    }

    /// <summary>G-code for any machine that has a definition: its start, the moves, its layer code, its end.</summary>
    public static class MachineGCode
    {
        private static string N(double v, int digits) => Math.Round(v, digits).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);

        public static GingerGCode.Result Build(Toolpath source, MachineDefinition machine)
        {
            var r = new GingerGCode.Result();
            if (source == null || source.Count < 2) { r.Warnings.Add("Empty toolpath."); return r; }
            if (machine == null) { r.Warnings.Add("No machine."); return r; }
            // the extrusion follows the machine, whatever profile the print settings carry
            var tp = source.Duplicate();
            tp.Settings = source.Settings.Duplicate();
            tp.Settings.Machine = machine.Profile.Duplicate();
            var st = tp.Settings;
            var pr = PathEngine.Process(tp, false);
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                r.Warnings.Add("The toolpath has tilted tool axes (angled, non-planar or multi-axis). G-code carries positions only: the nozzle stays as the machine holds it. Check the clearance between nozzle and part.");
            var box = new BoundingBox(tp.Points);
            if (box.Min.X < 0 || box.Min.Y < 0 || box.Min.Z < 0)
                r.Warnings.Add($"Part of the path has negative coordinates (X {box.Min.X:0}, Y {box.Min.Y:0}, Z {box.Min.Z:0}). Move the part onto the bed: the machine origin is at 0, 0, 0.");
            if (machine.BedX > 0 && machine.BedY > 0 && (box.Max.X > machine.BedX || box.Max.Y > machine.BedY))
                r.Warnings.Add($"The path reaches X {box.Max.X:0}, Y {box.Max.Y:0}: beyond the bed of {machine.BedX:0} x {machine.BedY:0} mm.");

            var L = r.Lines;
            L.Add("; LARGER.slicer - " + machine.Name);
            L.Add("; bead " + N(st.BeadWidth, 2) + " x " + N(st.LayerHeight, 2) + " mm, F" + N(st.Speed, 0) + " constant, " + st.Machine);
            L.Add("; path " + N(pr.Length / 1000.0, 2) + " m, about " + N(pr.Minutes, 0) + " min, " + N(OutputFile.Grams(pr.Volume, st), 0) + " g");
            foreach (string code in machine.Start) L.Add(machine.Fill(code));

            double travel = machine.TravelSpeed > 0 ? machine.TravelSpeed : Math.Max(st.Speed, st.MaxSpeed);
            Point3d p0 = tp.Points[0];
            L.Add("G0 X" + N(p0.X, 3) + " Y" + N(p0.Y, 3) + " F" + N(travel, 0));
            L.Add("G0 Z" + N(p0.Z, 3) + " F" + N(travel, 0));
            int layer = int.MinValue, number = 0; double total = 0; int zero = 0;
            for (int i = 1; i < tp.Count; i++)
            {
                if (tp.Layers[i] != layer)
                {
                    layer = tp.Layers[i]; number++;
                    // the hook of the layer just finished (e.g. the fan on after layer 1)
                    if (number > 1 && machine.AfterLayer.TryGetValue(number - 1, out var after))
                    {
                        L.Add("; after layer " + (number - 1).ToString(CultureInfo.InvariantCulture));
                        foreach (string code in after) L.Add(machine.Fill(code));
                    }
                    L.Add(";LAYER:" + number);
                    foreach (string code in machine.Layer) L.Add(machine.Fill(code.Replace("{layer}", number.ToString(CultureInfo.InvariantCulture)).Replace("{z}", N(tp.Points[i].Z, 3))));
                }
                Point3d p = tp.Points[i];
                if (p.DistanceTo(tp.Points[i - 1]) < 1e-6) continue;
                bool print = tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                if (!print) { L.Add("G0 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " F" + N(travel, 0)); continue; }
                double f = pr.Speed[i]; if (f <= 0) { f = st.Speed; zero++; }
                // the feedrate is written on every line: it never changes and is never 0
                L.Add("G1 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " E" + N(pr.Extrusion[i], 5) + " F" + N(f, 0));
                total += pr.Extrusion[i];
            }
            if (zero > 0) r.Warnings.Add($"{zero} move(s) had speed 0 and got the print speed instead.");
            foreach (string code in machine.End) L.Add(machine.Fill(code));
            r.Info = $"{machine.Name}: {number} layers, {pr.PrintLength / 1000.0:0.00} m printed, E total {total:0.0}, about {pr.Minutes:0} min, {OutputFile.Grams(pr.Volume, st):0} g, {L.Count} lines";
            return r;
        }
    }
}
