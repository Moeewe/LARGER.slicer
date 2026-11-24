using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using LARGERslicer.Types;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Reads G-code of another slicer into the lists the DXR conversion needs (robot lines, extrusion,
    /// speed, type). Used by the DXR GCode Postprocessor of Grasshopper 1 and 2.
    /// The robot gets every position as an absolute one, so relative moves (G91, as in the end code of
    /// Cura: "G91 / G1 Z10") are added up here; written as they stand they would be read as a height
    /// above the bed and drive the nozzle into the part.
    /// </summary>
    public static class GCodeToDxr
    {
        public class Parsed
        {
            public List<string> RobotLines { get; set; } = new List<string>();
            public List<double> ExtrusionAmounts { get; set; } = new List<double>();
            public List<double> PrintSpeeds { get; set; } = new List<double>();
            /// <summary>Layer type for each movement (SKIRT, SKIN, WALL, INFILL).</summary>
            public List<string> LayerTypes { get; set; } = new List<string>();
            /// <summary>True if this is a retraction without movement.</summary>
            public List<bool> IsRetraction { get; set; } = new List<bool>();
            public MachineSettings ExtractedSettings { get; set; }
            /// <summary>What the user has to see on the component.</summary>
            public List<string> Warnings { get; } = new List<string>();
        }

        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static readonly Regex Word = new Regex(@"(?<![A-Z_])([A-Z])\s*([-+]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:E[-+][0-9]{2}(?![0-9.]))?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BedTemp = new Regex(@"^\s*(?:M140|M190)\b.*?\b[SR]\s*([0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NozzleTemp = new Regex(@"^\s*(?:M104|M109)\b.*?\b[SR]\s*([0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FanSpeed = new Regex(@"^\s*M106\b.*?\bS\s*([0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static double Num(string s) => double.Parse(s, NumberStyles.Float, Ci);

        /// <summary>The code without comments: ; to the end of the line and ( ... ).</summary>
        private static string Code(string line)
        {
            int semi = line.IndexOf(';');
            if (semi >= 0) line = line.Substring(0, semi);
            int open;
            while ((open = line.IndexOf('(')) >= 0)
            {
                int close = line.IndexOf(')', open);
                line = close < 0 ? line.Substring(0, open) : line.Substring(0, open) + " " + line.Substring(close + 1);
            }
            return line.Trim();
        }

        public static Parsed Parse(string gCode, List<string> processInfo, MachineSettings providedSettings)
        {
            var result = new Parsed();
            if (processInfo == null) processInfo = new List<string>();
            if (string.IsNullOrEmpty(gCode))
            {
                processInfo.Add("ERROR: GCode input is empty");
                return result;
            }

            string[] lines = gCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            result.ExtractedSettings = Settings(lines, processInfo, providedSettings);

            // ---- state of the machine
            double x = 0, y = 0, z = 0, eAbs = 0, feed = 1000.0, units = 1;
            bool knownX = false, knownY = false, knownZ = false;
            bool posRel = false, m83 = false;
            int motion = -1;
            string type = "";
            int moves = 0, extrusions = 0, retractions = 0, relative = 0, unplaced = 0, arcs = 0, offsets = 0;

            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith(";"))
                {
                    string c = trimmed.ToUpperInvariant();
                    if (c.Contains("TYPE:SKIRT") || c.Contains("TYPE:SUPPORT-INTERFACE")) type = "SKIRT";
                    else if (c.Contains("TYPE:SKIN")) type = "SKIN";
                    else if (c.Contains("TYPE:WALL-OUTER") || c.Contains("TYPE:WALL-INNER") || c.Contains("BRIDGE")) type = "WALL";
                    else if (c.Contains("TYPE:INFILL")) type = "INFILL";
                    continue;
                }
                string code = Code(trimmed);
                if (code.Length == 0) continue;

                // ---- the words of the line
                int g = -1, mWord = -1; bool gSet = false;
                double? wx = null, wy = null, wz = null, we = null, wf = null, wi = null, wj = null, wr = null;
                foreach (Match mt in Word.Matches(code))
                {
                    char k = char.ToUpperInvariant(mt.Groups[1].Value[0]);
                    double v = Num(mt.Groups[2].Value);
                    switch (k)
                    {
                        case 'G': if (!gSet) { g = Math.Abs(v - Math.Round(v)) < 1e-9 ? (int)Math.Round(v) : -2; gSet = true; } break;
                        case 'M': if (mWord < 0) mWord = (int)Math.Round(v); break;
                        case 'X': wx = v; break;
                        case 'Y': wy = v; break;
                        case 'Z': wz = v; break;
                        case 'E': we = v; break;
                        case 'F': wf = v; break;
                        case 'I': wi = v; break;
                        case 'J': wj = v; break;
                        case 'R': wr = v; break;
                    }
                }
                if (mWord == 83) { m83 = true; processInfo.Add("Detected M83: Using relative extrusion mode"); continue; }
                if (mWord == 82) { m83 = false; processInfo.Add("Detected M82: Using absolute extrusion mode"); continue; }
                if (mWord >= 0) continue;
                if (gSet)
                {
                    switch (g)
                    {
                        case 0: case 1: case 2: case 3: motion = g; break;
                        case 20: units = 25.4; continue;
                        case 21: units = 1; continue;
                        case 28:
                            // the machine drives to its end stops; where the robot stands does not change
                            continue;
                        case 90: posRel = false; continue;
                        case 91: posRel = true; continue;
                        case 92:
                            if (we.HasValue) eAbs = we.Value;
                            if (wx.HasValue || wy.HasValue || wz.HasValue) offsets++;
                            continue;
                        default: continue;
                    }
                }
                else if (motion < 0 || "XYZEF".IndexOf(char.ToUpperInvariant(code[0])) < 0) continue; // no move

                if (wf.HasValue && wf.Value > 0) feed = wf.Value * units; // the feed stays in force, also from a line with F only

                double deltaE = 0;
                if (we.HasValue)
                {
                    if (m83 || posRel) { deltaE = we.Value * units; eAbs += we.Value; }
                    else { deltaE = (we.Value - eAbs) * units; eAbs = we.Value; }
                }

                // a full circle may have no X and Y, only its centre (I, J)
                bool isArc = motion == 2 || motion == 3;
                bool hasMovement = wx.HasValue || wy.HasValue || wz.HasValue || isArc && (wi.HasValue || wj.HasValue);
                if (!hasMovement)
                {
                    if (!we.HasValue || Math.Abs(deltaE) < 1e-12) continue;
                    if (!(knownX && knownY && knownZ)) { unplaced++; continue; } // priming before the first position
                    Add(result, x, y, z, deltaE, feed, type, deltaE < 0);
                    if (deltaE < 0) retractions++; else { moves++; extrusions++; }
                    continue;
                }

                double nx = x, ny = y, nz = z;
                if (posRel)
                {
                    relative++;
                    if (wx.HasValue) nx = x + wx.Value * units;
                    if (wy.HasValue) ny = y + wy.Value * units;
                    if (wz.HasValue) nz = z + wz.Value * units;
                }
                else
                {
                    if (wx.HasValue) { nx = wx.Value * units; knownX = true; }
                    if (wy.HasValue) { ny = wy.Value * units; knownY = true; }
                    if (wz.HasValue) { nz = wz.Value * units; knownZ = true; }
                }
                if (!(knownX && knownY && knownZ))
                {
                    // the position is not complete yet (start code before the first real move): sent
                    // to the robot it would be a drive to X0 Y0 or down to Z0
                    x = nx; y = ny; z = nz; unplaced++;
                    continue;
                }

                if (isArc && (wi.HasValue || wj.HasValue || wr.HasValue))
                {
                    arcs++;
                    var pts = Toolpaths.GCodeImport.Arc(x, y, z, nx, ny, nz, (wi ?? 0) * units, (wj ?? 0) * units, (wr ?? 0) * units, wr.HasValue, motion == 2, 1.0);
                    if (pts.Count > 0)
                    {
                        double total = 0, px = x, py = y, pz = z;
                        foreach (var p in pts) { total += Dist(p[0] - px, p[1] - py, p[2] - pz); px = p[0]; py = p[1]; pz = p[2]; }
                        px = x; py = y; pz = z;
                        foreach (var p in pts)
                        {
                            double len = Dist(p[0] - px, p[1] - py, p[2] - pz);
                            Add(result, p[0], p[1], p[2], total > 1e-12 ? deltaE * len / total : deltaE / pts.Count, feed, type, false);
                            moves++; if (Math.Abs(deltaE) > 0.0001) extrusions++;
                            px = p[0]; py = p[1]; pz = p[2];
                        }
                        x = nx; y = ny; z = nz;
                        continue;
                    }
                }
                Add(result, nx, ny, nz, deltaE, feed, type, false);
                moves++; if (Math.Abs(deltaE) > 0.0001) extrusions++;
                x = nx; y = ny; z = nz;
            }

            processInfo.Add(Inv($"Parsed GCode: {moves} movements, {extrusions} extrusions, {retractions} retractions"));
            processInfo.Add($"Extrusion mode: {(m83 ? "Relative (M83)" : "Absolute (M82)")}");
            if (relative > 0) processInfo.Add(Inv($"Relative moves (G91): {relative}, written as absolute positions"));
            if (arcs > 0) processInfo.Add(Inv($"Arcs (G2/G3): {arcs}, written as straight moves of at most 1 mm"));
            if (unplaced > 0) processInfo.Add(Inv($"Left out {unplaced} line(s) of the start code before X, Y and Z were all known"));
            if (units != 1) processInfo.Add("The file is in inches (G20): converted to mm");
            if (offsets > 0) result.Warnings.Add(Inv($"The G-code sets X, Y or Z with G92 ({offsets} time(s)). This shift is not applied: check the positions."));
            return result;
        }

        private static string Inv(FormattableString s) => s.ToString(Ci);
        private static double Dist(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

        private static void Add(Parsed r, double x, double y, double z, double e, double f, string type, bool retraction)
        {
            r.RobotLines.Add(string.Format(Ci, "PTP X{0:F3} Y{1:F3} Z{2:F3}", x, y, z));
            r.ExtrusionAmounts.Add(e);
            r.PrintSpeeds.Add(f);
            r.LayerTypes.Add(type);
            r.IsRetraction.Add(retraction);
        }

        /// <summary>Temperatures and fan of the G-code header (first 50 lines), over the connected Machine Settings.</summary>
        private static MachineSettings Settings(string[] lines, List<string> processInfo, MachineSettings provided)
        {
            double? bed = null, nozzle = null, fan = null;
            for (int i = 0; i < Math.Min(50, lines.Length); i++)
            {
                string code = Code(lines[i]);
                if (code.Length == 0) continue;
                var m = BedTemp.Match(code); if (m.Success) bed = Num(m.Groups[1].Value);
                m = NozzleTemp.Match(code); if (m.Success) nozzle = Num(m.Groups[1].Value);
                m = FanSpeed.Match(code); if (m.Success) fan = Num(m.Groups[1].Value);
            }
            if (!(bed.HasValue || nozzle.HasValue || fan.HasValue))
            {
                processInfo.Add(provided == null ? "No temperature settings found in GCode header - using default values"
                    : "No temperature settings found in GCode header - using provided Machine Settings" + (provided.UseAdvancedFormat ? " (Extended Format, V.E.GLOBAL_*)" : " (Simple Format, V.P.VAR_*)"));
                return null;
            }

            MachineSettings s;
            double bedT = bed ?? (provided?.BedTemperature ?? 60.0);
            double nozzleT = nozzle ?? (provided?.NozzleTemperature ?? 200.0);
            if (provided != null && provided.UseAdvancedFormat)
            {
                double fanRaw = fan ?? provided.FanSpeed;
                s = new MachineSettings(
                    bedTemp: bedT, nozzleTemp: nozzleT, cooling: fanRaw, useAdvancedFormat: true,
                    bedZone1Enabled: provided.BedZone1Enabled, bedZone1Temp: bed.HasValue ? bedT : provided.BedZone1Temperature,
                    bedZone2Enabled: provided.BedZone2Enabled, bedZone2Temp: bed.HasValue ? bedT : provided.BedZone2Temperature,
                    bedZone3Enabled: provided.BedZone3Enabled, bedZone3Temp: bed.HasValue ? bedT : provided.BedZone3Temperature,
                    bedZone4Enabled: provided.BedZone4Enabled, bedZone4Temp: bed.HasValue ? bedT : provided.BedZone4Temperature,
                    fillingZoneEnabled: provided.FillingZoneCoolingEnabled, fillingZoneTemp: provided.FillingZoneTemperature,
                    extruderZone1Enabled: provided.ExtruderZone1Enabled, extruderZone1Temp: nozzle.HasValue ? nozzleT - 10.0 : provided.ExtruderZone1Temperature,
                    extruderZone2Enabled: provided.ExtruderZone2Enabled, extruderZone2Temp: nozzle.HasValue ? nozzleT - 5.0 : provided.ExtruderZone2Temperature,
                    nozzleZoneEnabled: provided.NozzleZoneEnabled, nozzleZoneTemp: nozzle.HasValue ? nozzleT : provided.NozzleZoneTemperature,
                    fanEnabled: provided.FanEnabled, fanSpeed: fanRaw);
                processInfo.Add("Preserved Extended Format settings (V.E.GLOBAL_*)");
            }
            else
            {
                double fanPercent = fan.HasValue ? fan.Value / 255.0 * 100.0 : (provided?.CoolingPercentage ?? 50.0);
                s = new MachineSettings(bedT, nozzleT, fanPercent);
                processInfo.Add("Created Simple Format settings (V.P.VAR_*) from extracted GCode values");
            }

            processInfo.Add("=== GCode Header Analysis ===");
            if (bed.HasValue)
            {
                processInfo.Add(Inv($"Extracted Bed Temperature: {bed.Value}°C"));
                if (provided != null && Math.Abs(bed.Value - provided.BedTemperature) > 0.1) processInfo.Add(Inv($"  → Changed from {provided.BedTemperature}°C to {bed.Value}°C"));
            }
            if (nozzle.HasValue)
            {
                processInfo.Add(Inv($"Extracted Nozzle Temperature: {nozzle.Value}°C"));
                if (provided != null && Math.Abs(nozzle.Value - provided.NozzleTemperature) > 0.1) processInfo.Add(Inv($"  → Changed from {provided.NozzleTemperature}°C to {nozzle.Value}°C"));
            }
            if (fan.HasValue)
            {
                double pct = fan.Value / 255.0 * 100.0; // M106 S is 0-255
                processInfo.Add(Inv($"Extracted Fan Speed: {fan.Value} ({(int)pct}%)"));
                if (provided != null && Math.Abs(pct - provided.CoolingPercentage) > 1.0) processInfo.Add(Inv($"  → Changed from {provided.CoolingPercentage}% to {(int)pct}%"));
            }
            return s;
        }
    }
}
