using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LARGERslicer.Toolpaths
{
    /// <summary>A temperature or other value given as a range, e.g. 200-210. Min = Max for a single value.</summary>
    public struct ValueRange
    {
        public double Min, Max;
        public ValueRange(double min, double max) { Min = Math.Min(min, max); Max = Math.Max(min, max); }
        public bool IsSet => Max > 0;
        /// <summary>The middle of the range: the value to start with.</summary>
        public double Mid => 0.5 * (Min + Max);

        private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        public override string ToString() => !IsSet ? "-" : Math.Abs(Max - Min) < 1e-9 ? N(Min) : N(Min) + "-" + N(Max);

        /// <summary>Reads "200", "200-210" or "200 - 210" (comma or point as decimal mark).</summary>
        public static bool TryParse(string text, out ValueRange r)
        {
            r = new ValueRange();
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Replace("°C", "").Replace("°", "").Replace("C", "").Replace(',', '.').Trim();
            if (t == "-" || t.Length == 0) return true;
            int dash = t.IndexOf('-', 1);
            double a, b;
            if (dash < 0)
            {
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out a)) return false;
                r = new ValueRange(a, a); return true;
            }
            if (!double.TryParse(t.Substring(0, dash).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                || !double.TryParse(t.Substring(dash + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return false;
            r = new ValueRange(a, b); return true;
        }
    }

    /// <summary>
    /// A print material for a pellet extruder: start temperatures for the zones of the screw,
    /// the nozzle and the bed, how to dry it and how heavy it is. The built-in materials take
    /// their start values from the Weber DXR quick guide (Weber Additive, start package of the
    /// FH Muenster); the densities are typical values from data sheets, not measured.
    /// Anyone can add their own as a small text file (ending .material) in Documents/LARGER/Materials,
    /// or put one together with the Material component and press Save.
    ///
    /// The file reads like this:
    ///
    ///   name = My PETG
    ///   maker = Some company
    ///   type = PETG
    ///   feed zone = 35              (degrees C; the cooled intake of the screw)
    ///   zone 1 = 200-210            (degrees C; one value or a range)
    ///   zone 2 = 210-220
    ///   zone 3 = 220-230
    ///   nozzle = 235-240
    ///   bed = 60-90
    ///   drying = 6 h at 65          (hours at degrees C)
    ///   moisture = 0.03             (largest residual moisture in %, before printing)
    ///   density = 1.27              (g/cm3)
    ///   factor = 1.0                (optional: material factor found with Material Factor; 0 = not known)
    ///   note = any text
    ///
    /// Lines that begin with # are comments.
    /// </summary>
    public class MaterialDefinition
    {
        public string Name = "Material";
        public string Maker = "";
        public string Type = "";
        public double FeedZone;
        public ValueRange Zone1, Zone2, Zone3, Nozzle, Bed;
        public double DryHours, DryTemperature;
        /// <summary>Largest residual moisture before printing, in percent.</summary>
        public double MaxMoisture = 0.03;
        /// <summary>g/cm3.</summary>
        public double Density = 1.25;
        /// <summary>True when the density is a typical value from data sheets, not measured on this material.</summary>
        public bool DensityTypical;
        /// <summary>Material factor (Weber: P1) found with Material Factor. 0 = not known.</summary>
        public double Factor;
        public string Note = "";
        /// <summary>Where the material comes from: "built in" or the path of its file.</summary>
        public string Source = "built in";

        public MaterialDefinition Duplicate() => (MaterialDefinition)MemberwiseClone();

        public override string ToString() => Inv.F($"Material {Name} ({Source})");

        /// <summary>The temperatures in one line, from the feed zone to the nozzle, then the bed.</summary>
        public string TemperatureText =>
            Inv.F($"feed {N(FeedZone)} / zone 1 {Zone1} / zone 2 {Zone2} / zone 3 {Zone3} / nozzle {Nozzle} °C, bed {Bed} °C");

        public string DryingText => DryHours > 0 ? Inv.F($"{N(DryHours)} h at {N(DryTemperature)} °C, residual moisture below {MaxMoisture.ToString("0.###", CultureInfo.InvariantCulture)} %") : "no drying given";

        // ---------------------------------------------------------------- built in

        public const string WeberSource = "start values from the Weber DXR quick guide (Weber Additive)";

        public static List<MaterialDefinition> BuiltIn() => new List<MaterialDefinition>
        {
            new MaterialDefinition
            {
                Name = "Carbon-P PETG (mcpp)", Maker = "mcpp", Type = "PETG-CF",
                FeedZone = 35, Zone1 = new ValueRange(200, 210), Zone2 = new ValueRange(210, 220), Zone3 = new ValueRange(220, 230),
                Nozzle = new ValueRange(235, 240), Bed = new ValueRange(60, 90), DryHours = 6, DryTemperature = 65,
                Density = 1.30, DensityTypical = true, Note = WeberSource + "; density: typical value for PETG with carbon fibre",
            },
            new MaterialDefinition
            {
                Name = "rPETG (Reflow)", Maker = "Reflow", Type = "PETG",
                FeedZone = 60, Zone1 = new ValueRange(190, 200), Zone2 = new ValueRange(200, 210), Zone3 = new ValueRange(210, 215),
                Nozzle = new ValueRange(215, 220), Bed = new ValueRange(70, 90), DryHours = 6, DryTemperature = 60,
                Density = 1.27, DensityTypical = true, Note = WeberSource + "; density: typical value for PETG",
            },
            new MaterialDefinition
            {
                Name = "PLA (UPM Formi)", Maker = "UPM", Type = "PLA",
                FeedZone = 35, Zone1 = new ValueRange(160, 170), Zone2 = new ValueRange(170, 180), Zone3 = new ValueRange(180, 190),
                Nozzle = new ValueRange(180, 190), Bed = new ValueRange(60, 100), DryHours = 4, DryTemperature = 60,
                Density = 1.24, DensityTypical = true, Note = WeberSource + "; density: typical value for PLA",
            },
        };

        /// <summary>The folder for your own materials: Documents/LARGER/Materials.</summary>
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LARGER", "Materials");

        /// <summary>Built-in materials first, then every .material file of the folder, by file name.</summary>
        public static List<MaterialDefinition> All(List<string> log = null)
        {
            var list = BuiltIn();
            try
            {
                if (Directory.Exists(Folder))
                    foreach (string file in Directory.GetFiles(Folder, "*.material").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        var m = Load(file, out string error);
                        if (m != null) list.Add(m); else log?.Add(Path.GetFileName(file) + ": " + error);
                    }
            }
            catch (Exception ex) { log?.Add("The materials folder could not be read: " + ex.Message); }
            return list;
        }

        // ---------------------------------------------------------------- text form

        private static string N(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public List<string> ToText()
        {
            var t = new List<string>
            {
                "# LARGER.slicer material. Put this file into " + Folder,
                "name = " + Name,
            };
            if (!string.IsNullOrWhiteSpace(Maker)) t.Add("maker = " + Maker);
            if (!string.IsNullOrWhiteSpace(Type)) t.Add("type = " + Type);
            t.Add("feed zone = " + N(FeedZone));
            t.Add("zone 1 = " + Zone1);
            t.Add("zone 2 = " + Zone2);
            t.Add("zone 3 = " + Zone3);
            t.Add("nozzle = " + Nozzle);
            t.Add("bed = " + Bed);
            t.Add("drying = " + N(DryHours) + " h at " + N(DryTemperature));
            t.Add("moisture = " + N(MaxMoisture));
            t.Add("density = " + N(Density));
            if (Factor > 0) t.Add("factor = " + N(Factor));
            if (!string.IsNullOrWhiteSpace(Note)) t.Add("note = " + Note.Replace("\r", " ").Replace("\n", " "));
            return t;
        }

        public static MaterialDefinition Load(string file, out string error)
        {
            try
            {
                var m = Parse(File.ReadAllLines(file), out error);
                if (m != null) m.Source = file;
                return m;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static MaterialDefinition Parse(IEnumerable<string> lines, out string error)
        {
            error = null;
            var m = new MaterialDefinition { Name = null };
            bool densityGiven = false;
            int n = 0;
            foreach (string raw in lines ?? new string[0])
            {
                n++;
                string t = (raw ?? "").Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int eq = t.IndexOf('=');
                if (eq < 0) { error = Inv.F($"line {n}: expected 'key = value'."); return null; }
                string k = t.Substring(0, eq).Trim().ToLowerInvariant(), v = t.Substring(eq + 1).Trim();
                double d;
                bool isNumber = double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                ValueRange r;
                switch (k)
                {
                    case "name": m.Name = v; break;
                    case "maker": m.Maker = v; break;
                    case "type": m.Type = v; break;
                    case "note": m.Note = v; break;
                    case "feed zone": if (!isNumber) goto bad; m.FeedZone = d; break;
                    case "zone 1": if (!ValueRange.TryParse(v, out r)) goto badRange; m.Zone1 = r; break;
                    case "zone 2": if (!ValueRange.TryParse(v, out r)) goto badRange; m.Zone2 = r; break;
                    case "zone 3": if (!ValueRange.TryParse(v, out r)) goto badRange; m.Zone3 = r; break;
                    case "nozzle": if (!ValueRange.TryParse(v, out r)) goto badRange; m.Nozzle = r; break;
                    case "bed": if (!ValueRange.TryParse(v, out r)) goto badRange; m.Bed = r; break;
                    case "moisture": if (!isNumber) goto bad; m.MaxMoisture = d; break;
                    case "density": if (!isNumber || d <= 0) goto bad; m.Density = d; densityGiven = true; break;
                    case "factor": if (!isNumber || d < 0) goto bad; m.Factor = d; break;
                    case "drying":
                        {
                            // "6 h at 65", "6h @ 65", "6 at 65"
                            string s = v.ToLowerInvariant().Replace("°c", "").Replace("°", "").Replace("@", " at ").Replace("h", " ").Replace(',', '.');
                            var parts = s.Split(new[] { " at " }, StringSplitOptions.None);
                            double hours, temp;
                            if (parts.Length != 2 || !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out hours)
                                || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out temp))
                            { error = Inv.F($"line {n}: drying is written like 6 h at 65."); return null; }
                            m.DryHours = hours; m.DryTemperature = temp;
                            break;
                        }
                    default: error = Inv.F($"line {n}: unknown key '{k}'."); return null;
                }
                continue;
                bad: error = Inv.F($"line {n}: '{k}' needs a number, got '{v}'."); return null;
                badRange: error = Inv.F($"line {n}: '{k}' needs a value or a range like 200-210, got '{v}'."); return null;
            }
            if (string.IsNullOrWhiteSpace(m.Name)) { error = "The material has no name (name = ...)."; return null; }
            if (!densityGiven) { m.DensityTypical = true; }
            return m;
        }

        /// <summary>Writes the material into the materials folder; returns the path.</summary>
        public string Save()
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, OutputFile.Clean(Name) + ".material");
            File.WriteAllLines(path, ToText());
            return path;
        }
    }

    /// <summary>
    /// The material factor from a test print: the bead comes out too wide or too narrow, so the
    /// extrusion is scaled until it fits. new factor = target width / measured width x current factor.
    /// With several measurements their mean is used. A single-wall test (the Weber "Calibration"
    /// part in spiral mode, one wall) gives the bead width directly as the wall thickness.
    /// </summary>
    public static class MaterialFactor
    {
        public class Result
        {
            public double NewFactor, Mean, Deviation, Spread;
            public int Count;
            public bool Ok = true;
            public List<string> Report = new List<string>();
        }

        private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <param name="target">bead width you want (mm)</param>
        /// <param name="measuredWidths">bead widths measured on the test print (mm)</param>
        /// <param name="current">factor the test was printed with</param>
        /// <param name="wallThickness">optional: wall thicknesses measured on a single-wall test (mm); counted as bead widths</param>
        public static Result Calculate(double target, IEnumerable<double> measuredWidths, double current, IEnumerable<double> wallThickness = null)
        {
            var r = new Result();
            var values = new List<double>();
            int widths = 0, walls = 0, skipped = 0;
            foreach (double v in measuredWidths ?? new double[0]) { if (v > 0 && !double.IsNaN(v)) { values.Add(v); widths++; } else skipped++; }
            foreach (double v in wallThickness ?? new double[0]) { if (v > 0 && !double.IsNaN(v)) { values.Add(v); walls++; } else skipped++; }
            if (target <= 0) { r.Ok = false; r.Report.Add("Target Width must be greater than 0."); return r; }
            if (current <= 0) { r.Ok = false; r.Report.Add("Current Factor must be greater than 0: it is the factor the test was printed with."); return r; }
            if (values.Count == 0) { r.Ok = false; r.NewFactor = current; r.Report.Add("No measurement yet. Print a test, measure the bead width with a caliper at a few places and enter the values."); return r; }

            r.Count = values.Count;
            r.Mean = values.Average();
            double min = values.Min(), max = values.Max();
            r.Spread = values.Count > 1 ? Math.Sqrt(values.Sum(v => (v - r.Mean) * (v - r.Mean)) / (values.Count - 1)) : 0;
            r.Deviation = (r.Mean - target) / target * 100.0;
            r.NewFactor = target / r.Mean * current;

            string what = widths > 0 && walls > 0 ? Inv.F($"{widths} bead width(s) and {walls} wall thickness(es) of a single-wall test")
                        : walls > 0 ? Inv.F($"{walls} wall thickness(es) of a single-wall test") : Inv.F($"{widths} bead width(s)");
            r.Report.Add(Inv.F($"Measured {what}: mean {F(r.Mean, "0.###")} mm") + (values.Count > 1 ? Inv.F($" ({F(min)} to {F(max)} mm, spread {F(r.Spread, "0.###")} mm)") : "") + Inv.F($", target {F(target)} mm."));
            r.Report.Add(Math.Abs(r.Deviation) < 0.05 ? "The bead is exactly as wide as wanted."
                : Inv.F($"The bead is {F(Math.Abs(r.Deviation), "0.#")} % too {(r.Deviation > 0 ? "wide" : "narrow")}."));
            r.Report.Add(Inv.F($"New factor = {F(target)} / {F(r.Mean, "0.###")} x {F(current, "0.###")} = {F(r.NewFactor, "0.###")}."));
            if (skipped > 0) r.Report.Add(Inv.F($"{skipped} value(s) of 0 or less were left out."));
            if (values.Count < 3) r.Report.Add("Tip: measure at three or more places, away from the seam and the start; the mean is more reliable.");
            if (values.Count > 1 && r.Spread > 0.1 * r.Mean)
            {
                r.Ok = false;
                r.Report.Add("The measurements differ by more than 10 %. The extrusion is not even yet: check temperature, drying and pellet feed before you change the factor.");
            }
            if (Math.Abs(r.Deviation) > 30)
            {
                r.Ok = false;
                r.Report.Add("More than 30 % off. Check first that the right nozzle, layer height and machine profile were used; then print the test again with the new factor.");
            }
            else if (Math.Abs(r.Deviation) > 3)
                r.Report.Add("Print the test once more with the new factor and measure again.");
            return r;
        }
    }
}
