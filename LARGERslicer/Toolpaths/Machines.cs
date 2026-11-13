using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// How a machine moves. What it can print follows from it (as in LARGER.app):
    ///
    ///   kind         flat layers   non-planar, nozzle vertical   tilted tool (angled, multi-axis)
    ///   gantry       yes           yes                           no
    ///   bedslinger   yes           yes                           no
    ///   delta        yes           yes                           no
    ///   belt 45      yes (lean by construction)   no             only at its belt angle
    ///   robot        yes           yes                           yes
    /// </summary>
    public enum MachineKind { Gantry, BedSlinger, Delta, Belt, Robot }

    public static class MachineKinds
    {
        public static readonly MachineKind[] All = { MachineKind.Gantry, MachineKind.BedSlinger, MachineKind.Delta, MachineKind.Belt, MachineKind.Robot };

        /// <summary>The word in a .machine file: gantry, bedslinger, delta, belt, robot.</summary>
        public static string Id(MachineKind k) => k == MachineKind.BedSlinger ? "bedslinger" : k.ToString().ToLowerInvariant();

        public static string Label(MachineKind k, double beltAngle = 45) =>
            k == MachineKind.BedSlinger ? "Bed slinger (bed moves in Y)" : k == MachineKind.Gantry ? "Gantry (X, Y, Z)" : k == MachineKind.Delta ? "Delta"
            : k == MachineKind.Belt ? Inv.F($"Belt printer ({beltAngle:0.#} degrees)") : "Six-axis robot";

        /// <summary>Reads gantry, bedslinger, delta, belt 45, robot (and a few other words for them, as the app does).</summary>
        public static bool Parse(string text, out MachineKind kind, out double beltAngle)
        {
            kind = MachineKind.Gantry; beltAngle = 45;
            string t = (text ?? "").Trim().ToLowerInvariant();
            int cut = t.IndexOfAny(new[] { ' ', '(' });
            string word = cut < 0 ? t : t.Substring(0, cut), rest = cut < 0 ? "" : t.Substring(cut);
            switch (word)
            {
                case "bedslinger": case "bed-slinger": case "slinger": case "i3": kind = MachineKind.BedSlinger; return true;
                case "gantry": case "portal": case "cartesian": case "corexy": kind = MachineKind.Gantry; return true;
                case "delta": kind = MachineKind.Delta; return true;
                case "robot": case "arm": kind = MachineKind.Robot; return true;
                case "belt": case "conveyor":
                    kind = MachineKind.Belt;
                    string digits = new string(rest.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
                    if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && a > 0 && a < 90) beltAngle = a;
                    return true;
                default: return false;
            }
        }

        /// <summary>Non-planar layers with the nozzle kept vertical: all three axes move at once.</summary>
        public static bool NonPlanar(MachineKind k) => k != MachineKind.Belt;

        /// <summary>Can the layers lean by this angle (degrees), the tool square to them?</summary>
        public static bool Leans(MachineKind k, double beltAngle, double angle) =>
            k == MachineKind.Robot || Math.Abs(angle) < 1 || (k == MachineKind.Belt && Math.Abs(Math.Abs(angle) - beltAngle) < 1);

        /// <summary>What it can print, in a few words.</summary>
        public static string Can(MachineKind k) => k == MachineKind.Robot ? "flat, non-planar, angled" : k == MachineKind.Belt ? "flat on the belt, at its angle" : "flat, non-planar (nozzle vertical)";
    }

    /// <summary>What a path asks of the machine: errors (it cannot print it), warnings, and how its layers lie.</summary>
    public class PathFit
    {
        public List<string> Errors = new List<string>(), Warnings = new List<string>();
        /// <summary>Remarks: what the machine can do and is worth knowing (non-planar on a gantry), no problem.</summary>
        public List<string> Notes = new List<string>();
        /// <summary>The strongest lean of a planar layer (degrees from horizontal).</summary>
        public double MaxLean;
        /// <summary>Layers that are neither flat nor one tilted plane (non-planar).</summary>
        public int Curved;
        public bool Ok => Errors.Count == 0;
    }

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
    ///   kind = gantry                (gantry, bedslinger, delta, belt 45, robot: what it can print and how it moves)
    ///   bed = 1000 x 1000            (mm; a third number is the height: 1000 x 1000 x 800)
    ///   extrusion factor = 0.21      (E = width x height x length x flow x this)
    ///   first layer factor = 1
    ///   filament diameter = 0        (0 = E is a volume; else E is a length of filament)
    ///   section = rectangle          (rectangle = width x height; bead = squashed bead with round sides)
    ///   travel speed = 0             (mm/min; 0 = the highest speed of the print settings)
    ///   material = PLA               (optional: replaces {material} in the codes)
    ///   bed temperature = 90         (optional: replaces {bed} in the codes)
    ///   model = My printer.3dm       (optional: a Rhino file (or .obj) with the machine in 3D, layers
    ///                                 Frame, Bed, Z, Y, X, TCP: Printer Simulation moves it; a .3dm of
    ///                                 the same name next to the .machine file is found by itself)
    ///   [start]
    ///   G21
    ///   G28
    ///   [layer]
    ///   ; layer {layer} at Z {z}
    ///   [after layer 2]              (optional: written once, when layer 2 is finished - e.g. the fan on)
    ///   M106 S153
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
        /// <summary>Who makes it (Ginger Additive, Weber Additive ...); empty for your own. Name is the machine itself, as Bambu Lab and H2C.</summary>
        public string Manufacturer = "";
        /// <summary>"Manufacturer · Machine", as the list shows it.</summary>
        public string Title => string.IsNullOrWhiteSpace(Manufacturer) ? Name : Manufacturer + " \u00B7 " + Name;
        public string Prefix = "GCO";
        public string Extension = ".gcode";
        public double BedX = 0, BedY = 0;
        /// <summary>Height of the build volume in mm; 0 = not known.</summary>
        public double BedZ = 0;
        /// <summary>How it moves; decides what it can print (see MachineKind) and how Printer Simulation moves its model.</summary>
        public MachineKind Kind = MachineKind.Gantry;
        /// <summary>Belt printers: the angle its layers stand at (degrees).</summary>
        public double BeltAngle = 45;
        public double TravelSpeed = 0;
        /// <summary>Optional: a Rhino file with the machine in 3D (frame, bed, gantry), drawn in the preview. In the coordinates of the machine: the origin is its 0, 0, 0.</summary>
        public string Model = "";
        /// <summary>A 3D model that comes with the plug-in (GingerOne), used when Model is empty. See PrinterModel.</summary>
        public string BuiltInModel = "";
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

        public override string ToString() => Inv.F($"Machine {Title} ({Source})");

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
            Name = "Ginger One (Marlin)", Manufacturer = "Ginger Additive", Prefix = "GIN", BedX = 1000, BedY = 1000, BedZ = 1000, BuiltInModel = "GingerOne",
            Profile = MachineProfile.Preset(4),
            Start = GingerGCode.Start.ToList(), End = GingerGCode.End.ToList(),
            // Ginger handbook: no cooling on layers 1 to 4 (adhesion), then 60 % (100 % for heavy overhangs)
            AfterLayer = new SortedDictionary<int, List<string>> { [4] = new List<string> { "M106 S153 ; fan 60 % after layer 4" } },
        };

        /// <summary>A plain start and end for a Marlin or Klipper machine, as a base for your own.</summary>
        public static MachineDefinition GenericMarlin() => new MachineDefinition
        {
            Name = "G-code machine (Marlin, Klipper)", Manufacturer = "Generic", Prefix = "GCO",
            Profile = new MachineProfile { Name = "Generic G-code machine", RectangleSection = true, Unit = "mm3" },
            Start = new List<string> { "; START CODE", "G21 ; millimetres", "G90 ; absolute positions", "G28 ; home", "G92 E0", "M83 ; extrusion as relative distances", "; end of start code" },
            Layer = new List<string>(),
            End = new List<string> { "; END CODE", "G91", "G1 F200 Z30 ; lift", "G90", "M84 ; motors off", "; end of end code" },
        };

        /// <summary>Ginger One on Klipper firmware: the START_PRINT and END_PRINT macros of its Klipper config.</summary>
        public static MachineDefinition GingerKlipper() => new MachineDefinition
        {
            Name = "Ginger One (Klipper)", Manufacturer = "Ginger Additive", Prefix = "GIK", BedX = 1000, BedY = 1000, BedZ = 1000, BuiltInModel = "GingerOne",
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
            // Ginger handbook: no cooling on layers 1 to 4 (adhesion), then 60 % (100 % for heavy overhangs)
            AfterLayer = new SortedDictionary<int, List<string>> { [4] = new List<string> { "M106 S153 ; fan 60 % after layer 4" } },
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
                "manufacturer = " + Manufacturer,
                "prefix = " + Prefix,
                "extension = " + Extension,
                "kind = " + MachineKinds.Id(Kind) + (Kind == MachineKind.Belt ? " " + Num(BeltAngle) : ""),
                "bed = " + Num(BedX) + " x " + Num(BedY) + (BedZ > 0 ? " x " + Num(BedZ) : ""),
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
                    else { error = Inv.F($"line {n}: unknown block [{key}]. Use [start], [layer], [after layer 2] and [end]."); return null; }
                    continue;
                }
                if (block != null) { block.Add(line.TrimEnd()); continue; }
                if (t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq < 0) { error = Inv.F($"line {n}: expected 'key = value' or a block like [start]."); return null; }
                string k = t.Substring(0, eq).Trim().ToLowerInvariant(), v = t.Substring(eq + 1).Trim();
                double d;
                bool isNumber = double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                switch (k)
                {
                    case "name": m.Name = v; break;
                    case "manufacturer": m.Manufacturer = v; break;
                    case "prefix": m.Prefix = v; break;
                    case "extension": m.Extension = v.StartsWith(".") ? v : "." + v; break;
                    case "bed":
                        var parts = v.ToLowerInvariant().Split('x');
                        var size = new double[parts.Length];
                        bool okBed = parts.Length == 2 || parts.Length == 3;
                        for (int q = 0; okBed && q < parts.Length; q++) okBed = double.TryParse(parts[q].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out size[q]) && size[q] >= 0;
                        if (!okBed) { error = Inv.F($"line {n}: bed needs two or three numbers, like 1000 x 1000 or 1000 x 1000 x 800."); return null; }
                        m.BedX = size[0]; m.BedY = size[1]; m.BedZ = size.Length == 3 ? size[2] : 0; break;
                    case "kind":
                        if (!MachineKinds.Parse(v, out m.Kind, out m.BeltAngle)) { error = Inv.F($"line {n}: kind is one of gantry, bedslinger, delta, belt 45, robot; got '{v}'."); return null; }
                        break;
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
                        else { error = Inv.F($"line {n}: section is rectangle or bead."); return null; }
                        break;
                    default: error = Inv.F($"line {n}: unknown key '{k}'."); return null;
                }
                continue;
                bad: error = Inv.F($"line {n}: '{k}' needs a number, got '{v}'."); return null;
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
            else if (string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(BuiltInModel))
            {
                // a machine made from a built-in one keeps its model (layers Frame, Bed, Z, Y, X, TCP)
                string mp = Path.ChangeExtension(path, ".3dm");
                if (PrinterModel.WriteBuiltIn(BuiltInModel, mp)) Model = mp;
            }
            File.WriteAllLines(path, ToText());
            return path;
        }

        /// <summary>
        /// Can this machine print the path? Looks at every layer: flat, one tilted plane (angled or
        /// multi-axis: the tool has to lean), or curved (non-planar: the nozzle can stay vertical).
        /// A gantry, bed slinger or delta cannot lean its nozzle; a belt printer only at its belt
        /// angle and not non-planar; a robot can do all of it (its program comes from the Robot tab).
        /// </summary>
        public PathFit CheckPath(Toolpath tp)
        {
            var fit = new PathFit();
            if (tp == null || tp.Count < 2) return fit;
            var byLayer = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < tp.Count; i++)
            {
                if (i < tp.Roles.Count && tp.Roles[i] == PathRole.Travel) continue;
                int l = i < tp.Layers.Count ? tp.Layers[i] : 0;
                if (!byLayer.TryGetValue(l, out var list)) byLayer[l] = list = new List<int>();
                list.Add(i);
            }
            int leaning = 0, first = int.MaxValue; double h0 = tp.Settings.LayerHeight > 0 ? tp.Settings.LayerHeight : 2;
            foreach (var kv in byLayer)
            {
                var idx = kv.Value; if (idx.Count < 3) continue;
                // the mean tool axis of the layer: for angled and multi-axis layers it is the normal of their plane
                var n = Vector3d.Zero; foreach (int i in idx) if (i < tp.Normals.Count) n += tp.Normals[i];
                if (!n.Unitize()) n = Vector3d.ZAxis;
                Point3d o = tp.Points[idx[0]];
                double lo = double.MaxValue, hi = double.MinValue, zlo = double.MaxValue, zhi = double.MinValue;
                foreach (int i in idx)
                {
                    double d = (tp.Points[i] - o) * n; lo = Math.Min(lo, d); hi = Math.Max(hi, d);
                    zlo = Math.Min(zlo, tp.Points[i].Z); zhi = Math.Max(zhi, tp.Points[i].Z);
                }
                double tol = 1.5 * h0 + 0.5; // the ramp to the next layer lifts a layer by one layer height
                double lean = Math.Acos(Math.Max(-1, Math.Min(1, Math.Abs(n.Z)))) * 180 / Math.PI;
                if (zhi - zlo <= tol) continue; // flat
                if (hi - lo <= tol && lean > 1) { leaning++; fit.MaxLean = Math.Max(fit.MaxLean, lean); first = Math.Min(first, kv.Key); continue; }
                fit.Curved++;
            }
            string who = Name + " [" + MachineKinds.Label(Kind, BeltAngle) + "]";
            if (leaning > 0 && !MachineKinds.Leans(Kind, BeltAngle, fit.MaxLean))
                fit.Errors.Add(Inv.F($"NOT PRINTABLE on {who}: {leaning} layer(s) lean up to {fit.MaxLean:0} degrees (angled or multi-axis slicing, first at layer {first + 1}). This machine cannot tilt its nozzle; {MachineKinds.Can(Kind)}. Slice flat or non-planar, or print it on a robot (Robot tab)."));
            if (fit.Curved > 0 && !MachineKinds.NonPlanar(Kind))
                fit.Errors.Add(Inv.F($"NOT PRINTABLE on {who}: {fit.Curved} layer(s) are non-planar; a belt printer prints flat layers at its belt angle only."));
            else if (fit.Curved > 0 && Kind != MachineKind.Robot)
                // allowed on this machine: a remark, not a warning (Non-Planar Check and Printer Simulation say whether the head clears the part)
                fit.Notes.Add(Inv.F($"{fit.Curved} non-planar layer(s): {Name} keeps its nozzle vertical and moves X, Y and Z at once; G-code carries positions only. Non-Planar Check and Printer Simulation show whether the head clears the part."));
            if (Kind == MachineKind.Robot)
                fit.Warnings.Add(Name + " is a robot: G-code carries positions only. For the tool orientation use Robot Program on the Robot tab.");
            if (BedZ > 0 && tp.Points.Count > 0)
            {
                double top = tp.Points.Max(p => p.Z);
                if (top > BedZ) fit.Warnings.Add(Inv.F($"The path reaches Z {top:0}: higher than the build height of {Name} ({BedZ:0} mm)."));
            }
            return fit;
        }

        /// <summary>What the preview shows: the 3D model of the machine if it has one, else its bed as a 10 mm plate below Z 0.</summary>
        public List<Mesh> Preview(out string note) => PreviewParts(out note).Select(p => p.Mesh).ToList();

        /// <summary>The preview in its colours: the parts of the 3D model as it is drawn, else the bed as a grey plate.</summary>
        public List<PrinterModel.Part> PreviewParts(out string note)
        {
            var printer = Printer(out note);
            if (printer != null) return printer.Parts.Select(p => new PrinterModel.Part { Group = p.Group, Mesh = p.Mesh.DuplicateMesh(), Color = p.Color }).ToList();
            var list = new List<PrinterModel.Part>();
            if (BedX > 0 && BedY > 0)
                list.Add(new PrinterModel.Part { Group = "Bed", Mesh = Mesh.CreateFromBox(new BoundingBox(0, 0, -10, BedX, BedY, 0), 1, 1, 1), Color = System.Drawing.Color.FromArgb(120, 124, 132) });
            return list;
        }

        /// <summary>The 3D model that can be posed: the file at Model, else the one that comes with the plug-in. Null if there is none.</summary>
        public PrinterModel Printer(out string note)
        {
            note = null;
            if (!string.IsNullOrWhiteSpace(Model))
            {
                var m = PrinterModel.FromFile(Model, out string error);
                if (m != null) return m;
                note = "The 3D model could not be read: " + error;
            }
            if (!string.IsNullOrWhiteSpace(BuiltInModel))
            {
                var b = PrinterModel.BuiltIn(BuiltInModel);
                if (b != null) return b;
                note = (note == null ? "" : note + " ") + "The built-in 3D model " + BuiltInModel + " is missing from the plug-in.";
            }
            return null;
        }
    }

    /// <summary>G-code for any machine that has a definition: its start, the moves, its layer code, its end.</summary>
    public static class MachineGCode
    {
        private static string N(double v, int digits) => Math.Round(v, digits).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);

        public static GingerGCode.Result Build(Toolpath source, MachineDefinition machine) => Build(source, machine, out _);

        /// <summary>The G-code, and the print as a run to play in Printer Simulation (the same speeds as in the file).</summary>
        public static GingerGCode.Result Build(Toolpath source, MachineDefinition machine, out PrinterRun run)
        {
            run = null;
            var r = new GingerGCode.Result();
            if (source == null || source.Count < 2) { r.Warnings.Add("Empty toolpath."); return r; }
            if (machine == null) { r.Warnings.Add("No machine."); return r; }
            // the extrusion follows the machine, whatever profile the print settings carry
            var tp = source.Duplicate();
            tp.Settings = source.Settings.Duplicate();
            tp.Settings.Machine = machine.Profile.Duplicate();
            var st = tp.Settings;
            var pr = PathEngine.Process(tp, false);
            run = PrinterRun.Make(tp, machine, pr);
            // what the kind of machine can print: errors start with NOT PRINTABLE
            var fit = machine.CheckPath(tp);
            r.Warnings.AddRange(fit.Errors);
            r.Warnings.AddRange(fit.Warnings);
            r.Notes.AddRange(fit.Notes);
            var box = new BoundingBox(tp.Points);
            if (box.Min.X < 0 || box.Min.Y < 0 || box.Min.Z < 0)
                r.Warnings.Add(Inv.F($"Part of the path has negative coordinates (X {box.Min.X:0}, Y {box.Min.Y:0}, Z {box.Min.Z:0}). Move the part onto the bed: the machine origin is at 0, 0, 0."));
            if (machine.BedX > 0 && machine.BedY > 0 && (box.Max.X > machine.BedX || box.Max.Y > machine.BedY))
                r.Warnings.Add(Inv.F($"The path reaches X {box.Max.X:0}, Y {box.Max.Y:0}: beyond the bed of {machine.BedX:0} x {machine.BedY:0} mm."));

            var L = r.Lines;
            L.Add("; LARGER.slicer - " + machine.Name);
            L.Add("; bead " + N(st.BeadWidth, 2) + " x " + N(st.LayerHeight, 2) + " mm, " + st.Machine);
            L.Add("; " + OutputFile.SpeedNote(st, pr));
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
                // the feedrate is written on every line and is never 0 (it changes where Min Layer Time or Max Volume Rate slow a layer or a move)
                L.Add("G1 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " E" + N(pr.Extrusion[i], 5) + " F" + N(f, 0));
                total += pr.Extrusion[i];
            }
            if (zero > 0) r.Warnings.Add(Inv.F($"{zero} move(s) had speed 0 and got the print speed instead."));
            foreach (string code in machine.End) L.Add(machine.Fill(code));
            r.Info = Inv.F($"{machine.Name}: {number} layers, {pr.PrintLength / 1000.0:0.00} m printed, E total {total:0.0}, about {pr.Minutes:0} min, {OutputFile.Grams(pr.Volume, st):0} g, {L.Count} lines");
            return r;
        }
    }
}
