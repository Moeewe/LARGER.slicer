using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper2.Components;
using Grasshopper2.Data;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;
using Path = System.IO.Path;

namespace LARGERslicer.GH2
{
    // DXR tools of Grasshopper 1 (LARGERslicer/Components/Export and Utils): same names, inputs,
    // outputs and behaviour. Their logic lives inside the Grasshopper 1 components, so it is copied
    // here line for line; only reading the inputs and writing the outputs is Grasshopper 2.

    /// <summary>Messages of the copied Grasshopper 1 code: 0 = remark, 1 = warning, 2 = error.</summary>
    internal static class Gh1
    {
        public static void Message(IDataAccess access, int level, string text)
        {
            string title = text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            if (level >= 2) access.AddError(title, text);
            else if (level == 1) access.AddWarning(title, text);
            else access.AddRemark(title, text);
        }

        /// <summary>All texts of an input, whatever its tree, in order (Grasshopper 1 read them as one list).</summary>
        public static List<string> AllTexts(IDataAccess access, int index)
        {
            var result = new List<string>();
            ITree tree;
            if (!access.GetITree(index, out tree) || tree == null) return result;
            for (int i = 0; i < tree.ItemCount; i++)
            {
                var o = tree.Items[i];
                if (o != null) result.Add(o as string ?? o.ToString());
            }
            return result;
        }
    }

    // ============================================================== machine settings extended
    [IoId("fbc15793-28bc-4455-8d5f-19c1c2d9fed6")]
    public sealed class MachineSettingsExtendedComponent : Component
    {
        public MachineSettingsExtendedComponent() : base(Io.Name("Machine Settings Extended", "Configure advanced multi-zone printer settings: 4 bed zones, 4 extruder zones (Extended Mode - V.E.GLOBAL_* format)", Io.Dxr, 0)) { }
        public MachineSettingsExtendedComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("MachineSettingsExtendedIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Bed Temperature", "Bed Temp", "Global bed temperature in °C for all zones (V.P.VAR_heatbedtemp). All 4 zones receive this same temperature.", Access.Item, Requirement.MayBeMissing).Set(60.0);
            inputs.AddBoolean("Bed Zone 1", "BZ1", "Enable/disable bed zone 1 (V.E.GLOBAL_BOOL[72] = TRUE/FALSE)", ("On", "Zone heats"), ("Off", "Zone stays off"), Access.Item, Requirement.MayBeMissing).Set(true);
            inputs.AddBoolean("Bed Zone 2", "BZ2", "Enable/disable bed zone 2 (V.E.GLOBAL_BOOL[74] = TRUE/FALSE)", ("On", "Zone heats"), ("Off", "Zone stays off"), Access.Item, Requirement.MayBeMissing).Set(true);
            inputs.AddBoolean("Bed Zone 3", "BZ3", "Enable/disable bed zone 3 (V.E.GLOBAL_BOOL[76] = TRUE/FALSE)", ("On", "Zone heats"), ("Off", "Zone stays off"), Access.Item, Requirement.MayBeMissing).Set(true);
            inputs.AddBoolean("Bed Zone 4", "BZ4", "Enable/disable bed zone 4 (V.E.GLOBAL_BOOL[78] = TRUE/FALSE)", ("On", "Zone heats"), ("Off", "Zone stays off"), Access.Item, Requirement.MayBeMissing).Set(true);
            inputs.AddNumber("Extruder Zone 1 Temp", "EZ1 Temp", "Heating extruder zone 1 temperature in °C (V.E.GLOBAL_BOOL[24], V.E.GLOBAL[55]). Standard: 220°C. Set to 0 to disable.", Access.Item, Requirement.MayBeMissing).Set(220.0);
            inputs.AddNumber("Extruder Zone 2 Temp", "EZ2 Temp", "Heating extruder zone 2 temperature in °C (V.E.GLOBAL_BOOL[26], V.E.GLOBAL[57]). Standard: 225°C. Set to 0 to disable.", Access.Item, Requirement.MayBeMissing).Set(225.0);
            inputs.AddNumber("Nozzle Zone Temp", "NZ Temp", "Heating nozzle zone temperature in °C (V.E.GLOBAL_BOOL[40], V.E.GLOBAL[71]). Standard: 230°C. Set to 0 to disable.", Access.Item, Requirement.MayBeMissing).Set(230.0);
            inputs.AddNumber("Fan Speed", "Fan Speed", "Fan speed 0-255 (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[3] for Extruder 1). Set to 0 to disable.", Access.Item, Requirement.MayBeMissing).Set(80.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Machine Settings", "Settings", "Machine configuration for DXR output (Extended Mode)");
            outputs.AddText("Settings Info", "Info", "Current machine settings summary", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            double bedTemperature = 60.0, extruderZone1Temp = 220.0, extruderZone2Temp = 225.0, nozzleZoneTemp = 230.0, fanSpeed = 80.0;
            bool bedZone1Enabled = true, bedZone2Enabled = true, bedZone3Enabled = true, bedZone4Enabled = true;
            double d; bool b;
            if (access.GetItem(0, out d)) bedTemperature = d;
            if (access.GetItem(1, out b)) bedZone1Enabled = b;
            if (access.GetItem(2, out b)) bedZone2Enabled = b;
            if (access.GetItem(3, out b)) bedZone3Enabled = b;
            if (access.GetItem(4, out b)) bedZone4Enabled = b;
            if (access.GetItem(5, out d)) extruderZone1Temp = d;
            if (access.GetItem(6, out d)) extruderZone2Temp = d;
            if (access.GetItem(7, out d)) nozzleZoneTemp = d;
            if (access.GetItem(8, out d)) fanSpeed = d;

            double fillingZoneTemp = 45.0;            // the filling zone is always on at 45 °C (cooling)
            bool extruderZone1Enabled = extruderZone1Temp > 0;
            bool extruderZone2Enabled = extruderZone2Temp > 0;
            bool nozzleZoneEnabled = nozzleZoneTemp > 0;
            bool fanEnabled = fanSpeed > 0;

            var settings = new MachineSettings(
                bedTemp: bedTemperature, nozzleTemp: 0, cooling: 0,
                useAdvancedFormat: true,
                bedZone1Enabled: bedZone1Enabled, bedZone1Temp: bedTemperature,
                bedZone2Enabled: bedZone2Enabled, bedZone2Temp: bedTemperature,
                bedZone3Enabled: bedZone3Enabled, bedZone3Temp: bedTemperature,
                bedZone4Enabled: bedZone4Enabled, bedZone4Temp: bedTemperature,
                fillingZoneEnabled: true, fillingZoneTemp: fillingZoneTemp,
                extruderZone1Enabled: extruderZone1Enabled, extruderZone1Temp: extruderZone1Temp,
                extruderZone2Enabled: extruderZone2Enabled, extruderZone2Temp: extruderZone2Temp,
                nozzleZoneEnabled: nozzleZoneEnabled, nozzleZoneTemp: nozzleZoneTemp,
                fanEnabled: fanEnabled, fanSpeed: fanSpeed);

            var info = new List<string>
            {
                "=== Machine Settings (Extended Multi-Zone Mode) ===",
                "Format: V.E.GLOBAL_* (Advanced Format)",
                "",
                "--- Heated Bed Zones (4 subdivided plates) ---",
                $"Global Bed Temperature: {bedTemperature}°C (V.P.VAR_heatbedtemp)",
                $"Zone 1: {(bedZone1Enabled ? "ON" : "OFF")} (V.E.GLOBAL_BOOL[72])",
                $"Zone 2: {(bedZone2Enabled ? "ON" : "OFF")} (V.E.GLOBAL_BOOL[74])",
                $"Zone 3: {(bedZone3Enabled ? "ON" : "OFF")} (V.E.GLOBAL_BOOL[76])",
                $"Zone 4: {(bedZone4Enabled ? "ON" : "OFF")} (V.E.GLOBAL_BOOL[78])",
                "",
                "IMPORTANT: Bed temperature is set GLOBALLY for all 4 zones via V.P.VAR_heatbedtemp",
                $"  → All zones receive the same temperature ({bedTemperature}°C)",
                "  → Only activated zones (V.E.GLOBAL_BOOL[72/74/76/78] = TRUE) will actually heat up",
                "  → Disabled zones have the temperature set but remain OFF",
                $"  → Example: If only Zone 1 is enabled, all zones get {bedTemperature}°C but only Zone 1 heats up",
                "",
                "--- Extruder Zones (4 zones) ---",
                $"Filling Zone: ON @ {fillingZoneTemp}°C (always enabled, cooling) (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[41])",
                $"Heating Zone 1: {(extruderZone1Enabled ? $"ON @ {extruderZone1Temp}°C" : "OFF (0°C)")} (V.E.GLOBAL_BOOL[24], V.E.GLOBAL[55])",
                $"Heating Zone 2: {(extruderZone2Enabled ? $"ON @ {extruderZone2Temp}°C" : "OFF (0°C)")} (V.E.GLOBAL_BOOL[26], V.E.GLOBAL[57])",
                $"Nozzle Zone: {(nozzleZoneEnabled ? $"ON @ {nozzleZoneTemp}°C" : "OFF (0°C)")} (V.E.GLOBAL_BOOL[40], V.E.GLOBAL[71])",
                "",
                "--- Fan Settings ---",
                $"Fan: {(fanEnabled ? $"ON @ {fanSpeed}" : "OFF (0)")} (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[3])",
                "",
                "Note: Zones are enabled if temperature > 0, disabled if = 0",
                "Note: Filling Zone is always enabled at 45°C (cooling)",
                "Note: All settings will automatically turn OFF after print completion"
            };
            var start = settings.GetStartGCode();
            info.Add("");
            if (start.Length > 0)
            {
                info.Add("Start G-code will include:");
                foreach (var line in start) info.Add($"  {line}");
            }
            else info.Add("No start G-code (all settings are OFF)");

            access.SetItem(0, settings);
            access.SetTwig(1, info.ToArray(), null, null);
        }
    }

    // ============================================================== G-code -> DXR
    [IoId("f6cdc242-d3e4-4a0f-a464-6ef60aec6c7a")]
    public sealed class DxrPostprocessorComponent : Component
    {
        public DxrPostprocessorComponent() : base(Io.Name("DXR GCode Postprocessor", "Converts GCode to DXR format. Parses GCode to extract robot path, extrusion amounts, and print speeds.", Io.Dxr, 0)) { }
        public DxrPostprocessorComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("DXRPostprocessorIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("GCode", "GCode", "Complete GCode file content as text (can be tree/list). Extracts robot path, extrusion, and speed from GCode.", Access.Tree);
            inputs.AddGeneric("Machine Settings", "Machine", "Printer configuration (connect Machine Settings component)", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("DXR File", "DXR", "Complete DXR file content ready for export", Access.Twig);
            outputs.AddText("Process Info", "Info", "Generation summary and statistics", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var processInfo = new List<string>();
            var result = new List<string>();
            var gCodeLines = Gh1.AllTexts(access, 0).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (gCodeLines.Count == 0)
            {
                access.AddError("GCode input is required.", "GCode input is required.");
                processInfo.Add("ERROR: GCode input is empty or not connected");
                access.SetTwig(0, result.ToArray(), null, null);
                access.SetTwig(1, processInfo.ToArray(), null, null);
                return;
            }
            try
            {
                string gCodeInput = string.Join("\n", gCodeLines);
                processInfo.Add($"GCode input detected: {gCodeLines.Count} lines, {gCodeInput.Length} characters total");
                var machineSettings = Io.Get<MachineSettings>(access, 1);

                processInfo.Add("Parsing GCode to extract movement data and analyze header/footer");
                var parsedData = ParseGCode(gCodeInput, processInfo, machineSettings);
                processInfo.Add($"Parsed: {parsedData.RobotLines.Count} movements, {parsedData.ExtrusionAmounts.Count} extrusions, {parsedData.PrintSpeeds.Count} speeds");
                var uniqueTypes = parsedData.LayerTypes.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                if (uniqueTypes.Count > 0) processInfo.Add($"Detected layer types: {string.Join(", ", uniqueTypes)}");
                int retractionCount = parsedData.IsRetraction.Count(r => r);
                if (retractionCount > 0) processInfo.Add($"Detected {retractionCount} retraction commands");

                MachineSettings finalSettings = parsedData.ExtractedSettings ?? machineSettings;
                result.AddRange(DXRHelper.ProcessRobotLinesToDXR(parsedData.RobotLines, parsedData.ExtrusionAmounts, parsedData.PrintSpeeds, processInfo, finalSettings, parsedData.LayerTypes, parsedData.IsRetraction));
                if (finalSettings != null)
                {
                    result.AddRange(finalSettings.GetEndGCode());
                    processInfo.Add("Added machine shutdown sequence (all systems OFF)");
                }
                else
                {
                    result.AddRange(new[]
                    {
                        "N9999994 V.P.VAR_heatbedtemp = 0", "N9999995 L heatbedTemp_sub.nc",
                        "N9999996 V.P.VAR_fan = 0", "N9999997 L fan_sub.nc",
                        "N9999998 V.P.VAR_extrudertemp = 0", "N9999999 L extruderTemp_sub.nc", "M29"
                    });
                    processInfo.Add("Added default shutdown sequence");
                }
                result.Add(DXRHelper.GenerateFooterComment());
                processInfo.Add($"Total DXR file lines: {result.Count}");
            }
            catch (Exception ex)
            {
                processInfo.Add($"Error during DXR processing: {ex.Message}");
                processInfo.Add($"Stack trace: {ex.StackTrace}");
                result.Clear();
            }
            access.SetTwig(0, result.ToArray(), null, null);
            access.SetTwig(1, processInfo.ToArray(), null, null);
        }

        private class ParsedGCodeData
        {
            public List<string> RobotLines { get; set; }
            public List<double> ExtrusionAmounts { get; set; }
            public List<double> PrintSpeeds { get; set; }
            public List<string> LayerTypes { get; set; } // Layer type for each movement (SKIRT, SKIN, WALL-OUTER, WALL-INNER, INFILL, etc.)
            public List<bool> IsRetraction { get; set; } // True if this is a retraction without movement
            public MachineSettings ExtractedSettings { get; set; }

            public ParsedGCodeData()
            {
                RobotLines = new List<string>();
                ExtrusionAmounts = new List<double>();
                PrintSpeeds = new List<double>();
                LayerTypes = new List<string>();
                IsRetraction = new List<bool>();
                ExtractedSettings = null;
            }
        }

        private static ParsedGCodeData ParseGCode(string gCode, List<string> processInfo, MachineSettings providedSettings)
        {
            var result = new ParsedGCodeData();
            
            if (string.IsNullOrEmpty(gCode))
            {
                processInfo.Add("ERROR: GCode input is empty");
                return result;
            }

            // Regex patterns for GCode parsing
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var re = new Regex(@"E\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var rf = new Regex(@"F\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var rg = new Regex(@"G\s*([01])", RegexOptions.IgnoreCase);
            var rg92 = new Regex(@"G92\s+E\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);

            // Patterns for extracting temperature settings from GCode header
            var rBedTemp = new Regex(@"(?:M140|M190)\s+S\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var rNozzleTemp = new Regex(@"(?:M104|M109)\s+S\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            var rFanSpeed = new Regex(@"M106\s+S\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);

            string[] lines = gCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Extract settings from GCode header (first 50 lines typically contain setup)
            double? extractedBedTemp = null;
            double? extractedNozzleTemp = null;
            double? extractedFanSpeed = null;
            bool foundHeaderSettings = false;
            
            int headerEndIndex = Math.Min(50, lines.Length);
            for (int i = 0; i < headerEndIndex; i++)
            {
                string line = lines[i].Trim().ToUpper();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                    continue;
                
                // Look for temperature commands (with safe parsing)
                var bedMatch = rBedTemp.Match(lines[i]);
                if (bedMatch.Success && double.TryParse(bedMatch.Groups[1].Value, out double bedTemp))
                {
                    extractedBedTemp = bedTemp;
                    foundHeaderSettings = true;
                }
                
                var nozzleMatch = rNozzleTemp.Match(lines[i]);
                if (nozzleMatch.Success && double.TryParse(nozzleMatch.Groups[1].Value, out double nozzleTemp))
                {
                    extractedNozzleTemp = nozzleTemp;
                    foundHeaderSettings = true;
                }
                
                var fanMatch = rFanSpeed.Match(lines[i]);
                if (fanMatch.Success && double.TryParse(fanMatch.Groups[1].Value, out double fanSpeed))
                {
                    extractedFanSpeed = fanSpeed;
                    foundHeaderSettings = true;
                }
            }
            
            // Create machine settings from extracted values or use provided ones
            if (foundHeaderSettings)
            {
                double bedTemp = extractedBedTemp ?? (providedSettings?.BedTemperature ?? 60.0);
                double nozzleTemp = extractedNozzleTemp ?? (providedSettings?.NozzleTemperature ?? 200.0);
                double fanSpeed = extractedFanSpeed ?? (providedSettings?.CoolingPercentage ?? 50.0);
                
                // Check if provided settings are in advanced format
                if (providedSettings != null && providedSettings.UseAdvancedFormat)
                {
                    // Preserve advanced format settings, but update temperatures if extracted
                    result.ExtractedSettings = new MachineSettings(
                        bedTemp: bedTemp,
                        nozzleTemp: nozzleTemp,
                        cooling: fanSpeed,
                        useAdvancedFormat: true,
                        bedZone1Enabled: providedSettings.BedZone1Enabled,
                        bedZone1Temp: extractedBedTemp.HasValue ? bedTemp : providedSettings.BedZone1Temperature,
                        bedZone2Enabled: providedSettings.BedZone2Enabled,
                        bedZone2Temp: extractedBedTemp.HasValue ? bedTemp : providedSettings.BedZone2Temperature,
                        bedZone3Enabled: providedSettings.BedZone3Enabled,
                        bedZone3Temp: extractedBedTemp.HasValue ? bedTemp : providedSettings.BedZone3Temperature,
                        bedZone4Enabled: providedSettings.BedZone4Enabled,
                        bedZone4Temp: extractedBedTemp.HasValue ? bedTemp : providedSettings.BedZone4Temperature,
                        fillingZoneEnabled: providedSettings.FillingZoneCoolingEnabled,
                        fillingZoneTemp: providedSettings.FillingZoneTemperature,
                        extruderZone1Enabled: providedSettings.ExtruderZone1Enabled,
                        extruderZone1Temp: extractedNozzleTemp.HasValue ? (nozzleTemp - 10.0) : providedSettings.ExtruderZone1Temperature,
                        extruderZone2Enabled: providedSettings.ExtruderZone2Enabled,
                        extruderZone2Temp: extractedNozzleTemp.HasValue ? (nozzleTemp - 5.0) : providedSettings.ExtruderZone2Temperature,
                        nozzleZoneEnabled: providedSettings.NozzleZoneEnabled,
                        nozzleZoneTemp: extractedNozzleTemp.HasValue ? nozzleTemp : providedSettings.NozzleZoneTemperature,
                        fanEnabled: providedSettings.FanEnabled,
                        fanSpeed: extractedFanSpeed.HasValue ? fanSpeed : providedSettings.FanSpeed
                    );
                    processInfo.Add("Preserved Extended Format settings (V.E.GLOBAL_*)");
                }
                else
                {
                    // Simple format: create new simple settings
                    double fanPercent = extractedFanSpeed.HasValue ? ((extractedFanSpeed.Value / 255.0) * 100.0) : fanSpeed;
                    result.ExtractedSettings = new MachineSettings(bedTemp, nozzleTemp, fanPercent);
                    processInfo.Add("Created Simple Format settings (V.P.VAR_*) from extracted GCode values");
                }
                
                processInfo.Add("=== GCode Header Analysis ===");
                if (extractedBedTemp.HasValue)
                {
                    processInfo.Add($"Extracted Bed Temperature: {extractedBedTemp.Value}°C");
                    if (providedSettings != null && Math.Abs(extractedBedTemp.Value - providedSettings.BedTemperature) > 0.1)
                    {
                        processInfo.Add($"  → Changed from {providedSettings.BedTemperature}°C to {extractedBedTemp.Value}°C");
                    }
                }
                if (extractedNozzleTemp.HasValue)
                {
                    processInfo.Add($"Extracted Nozzle Temperature: {extractedNozzleTemp.Value}°C");
                    if (providedSettings != null && Math.Abs(extractedNozzleTemp.Value - providedSettings.NozzleTemperature) > 0.1)
                    {
                        processInfo.Add($"  → Changed from {providedSettings.NozzleTemperature}°C to {extractedNozzleTemp.Value}°C");
                    }
                }
                if (extractedFanSpeed.HasValue)
                {
                    double fanPercent = (extractedFanSpeed.Value / 255.0) * 100.0; // M106 S value is 0-255
                    processInfo.Add($"Extracted Fan Speed: {extractedFanSpeed.Value} ({(int)fanPercent}%)");
                    if (providedSettings != null && Math.Abs(fanPercent - providedSettings.CoolingPercentage) > 1.0)
                    {
                        processInfo.Add($"  → Changed from {providedSettings.CoolingPercentage}% to {(int)fanPercent}%");
                    }
                }
            }
            else if (providedSettings != null)
            {
                processInfo.Add("No temperature settings found in GCode header - using provided Machine Settings");
                if (providedSettings.UseAdvancedFormat)
                {
                    processInfo.Add("  → Using Extended Format (V.E.GLOBAL_*)");
                }
                else
                {
                    processInfo.Add("  → Using Simple Format (V.P.VAR_*)");
                }
            }
            else
            {
                processInfo.Add("No temperature settings found in GCode header - using default values");
            }
            
            // State tracking
            double lastX = 0, lastY = 0, lastZ = 0;
            double cumulativeE = 0; // Track cumulative extrusion for relative mode
            double lastF = 1000.0; // Default speed in mm/min
            bool extrusionModeRelative = false; // Track if M83 (relative) or M82 (absolute) is active
            string currentLayerType = ""; // Current layer type from comments (TYPE:SKIRT, TYPE:SKIN, etc.)

            int movementCount = 0;
            int extrusionCount = 0;
            int speedCount = 0;
            int retractionCount = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                
                // Check for layer type comments (e.g., ;TYPE:SKIRT, ;TYPE:SKIN, ;TYPE:WALL-OUTER)
                if (line.StartsWith(";"))
                {
                    string commentUpper = line.ToUpper();
                    if (commentUpper.Contains("TYPE:SKIRT") || commentUpper.Contains("TYPE:SUPPORT-INTERFACE"))
                    {
                        currentLayerType = "SKIRT";
                        continue;
                    }
                    else if (commentUpper.Contains("TYPE:SKIN"))
                    {
                        currentLayerType = "SKIN";
                        continue;
                    }
                    else if (commentUpper.Contains("TYPE:WALL-OUTER") || commentUpper.Contains("TYPE:WALL-INNER") || commentUpper.Contains("BRIDGE"))
                    {
                        currentLayerType = "WALL";
                        continue;
                    }
                    else if (commentUpper.Contains("TYPE:INFILL"))
                    {
                        currentLayerType = "INFILL";
                        continue;
                    }
                    else if (commentUpper.Contains("LAYER:"))
                    {
                        // Layer change comment - keep current layer type
                        continue;
                    }
                    else
                    {
                        // Other comments - skip
                        continue;
                    }
                }
                
                // Skip empty lines
                if (string.IsNullOrEmpty(line))
                    continue;

                // Check for M83 (relative extrusion) or M82 (absolute extrusion)
                string lineUpper = line.ToUpper();
                if (lineUpper.Contains("M83"))
                {
                    extrusionModeRelative = true;
                    processInfo.Add("Detected M83: Using relative extrusion mode");
                    continue;
                }
                if (lineUpper.Contains("M82"))
                {
                    extrusionModeRelative = false;
                    processInfo.Add("Detected M82: Using absolute extrusion mode");
                    continue;
                }

                // Check for G92 E0 (reset extrusion)
                var g92Match = rg92.Match(line);
                if (g92Match.Success)
                {
                    cumulativeE = 0;
                    processInfo.Add("Detected G92 E0: Reset extrusion counter");
                    continue;
                }

                // Check if this is a G0 or G1 movement command (with safe parsing)
                var gMatch = rg.Match(line);
                if (!gMatch.Success)
                    continue;

                if (!int.TryParse(gMatch.Groups[1].Value, out int gCommand))
                    continue;
                    
                if (gCommand != 0 && gCommand != 1)
                    continue;

                // Extract coordinates (with safe parsing)
                double? x = null, y = null, z = null;
                double? e = null;
                double? f = null;

                var mx = rx.Match(line);
                var my = ry.Match(line);
                var mz = rz.Match(line);
                var me = re.Match(line);
                var mf = rf.Match(line);

                if (mx.Success && double.TryParse(mx.Groups[1].Value, out double xVal)) x = xVal;
                if (my.Success && double.TryParse(my.Groups[1].Value, out double yVal)) y = yVal;
                if (mz.Success && double.TryParse(mz.Groups[1].Value, out double zVal)) z = zVal;
                if (me.Success && double.TryParse(me.Groups[1].Value, out double eVal)) e = eVal;
                if (mf.Success && double.TryParse(mf.Groups[1].Value, out double fVal)) f = fVal;

                // Use previous values if not specified (absolute coordinates)
                double currentX = x ?? lastX;
                double currentY = y ?? lastY;
                double currentZ = z ?? lastZ;
                double currentF = f ?? lastF;

                // Handle extrusion based on mode
                double deltaE = 0;
                if (e.HasValue)
                {
                    if (extrusionModeRelative)
                    {
                        // M83: E values are already relative, use directly
                        deltaE = e.Value;
                        cumulativeE += deltaE;
                    }
                    else
                    {
                        // M82: E values are absolute, calculate delta
                        deltaE = e.Value - cumulativeE;
                        cumulativeE = e.Value;
                    }
                }

                // Check if this is retraction without movement (negative E, no X/Y/Z change)
                bool hasMovement = (x.HasValue || y.HasValue || z.HasValue);
                bool hasExtrusion = e.HasValue;
                bool isRetraction = hasExtrusion && !hasMovement && deltaE < 0;

                if (hasMovement || hasExtrusion)
                {
                    if (isRetraction)
                    {
                        // Retraction without movement: Create special retraction command
                        // Format: PTP with last position, but mark as retraction
                        string robotLine = $"PTP X{lastX:F3} Y{lastY:F3} Z{lastZ:F3}";
                        result.RobotLines.Add(robotLine);
                        result.ExtrusionAmounts.Add(deltaE);
                        result.PrintSpeeds.Add(currentF);
                        result.LayerTypes.Add(currentLayerType);
                        result.IsRetraction.Add(true);
                        retractionCount++;
                    }
                    else
                    {
                        // Normal movement (with or without extrusion)
                    // Create robot path line in PTP format
                    string robotLine = $"PTP X{currentX:F3} Y{currentY:F3} Z{currentZ:F3}";
                    result.RobotLines.Add(robotLine);
                    movementCount++;

                    // Add relative extrusion amount (always relative for DXR output)
                    result.ExtrusionAmounts.Add(deltaE);
                    if (Math.Abs(deltaE) > 0.0001) extrusionCount++;

                    // Add speed value
                    result.PrintSpeeds.Add(currentF);
                    speedCount++;
                        
                        // Add layer type
                        result.LayerTypes.Add(currentLayerType);
                        result.IsRetraction.Add(false);

                    // Update state
                    lastX = currentX;
                    lastY = currentY;
                    lastZ = currentZ;
                    lastF = currentF;
                    }
                }
            }

            processInfo.Add($"Parsed GCode: {movementCount} movements, {extrusionCount} extrusions, {speedCount} speed values, {retractionCount} retractions");
            processInfo.Add($"Extrusion mode: {(extrusionModeRelative ? "Relative (M83)" : "Absolute (M82)")}");
            
            // Remove last values from extrusion and speed lists (matching original behavior)
            if (result.ExtrusionAmounts.Count > 0)
            {
                result.ExtrusionAmounts.RemoveAt(result.ExtrusionAmounts.Count - 1);
            }
            if (result.PrintSpeeds.Count > 0)
            {
                result.PrintSpeeds.RemoveAt(result.PrintSpeeds.Count - 1);
            }

            return result;
        }

    }

    // ============================================================== DXR file health check
    [IoId("a6ed71a4-801f-40f5-bd98-a935144b3fed")]
    public sealed class DxrFileHealthCheckComponent : Component
    {
        private static readonly Regex DxrMoveRegex = new Regex(@"^\s*N\d+\s+G1\b", RegexOptions.IgnoreCase);
        private static readonly Regex GCodeMoveRegex = new Regex(@"^\s*G0?1\b", RegexOptions.IgnoreCase);
        // feed word: an F followed by a number, not part of a longer name
        private static readonly Regex FeedRegex = new Regex(@"(?<![A-Za-z_.])F\s*(-?\d+(?:\.\d+)?)");
        private static readonly Regex ZValueRegex = new Regex(@"(?:^|\s)Z(?<z>[+-]?\d+(?:[\.,]\d+)?)\b", RegexOptions.IgnoreCase);
        private static readonly Regex SafeBaseNameRegex = new Regex(@"^[A-Za-z0-9_-]+$");

        public DxrFileHealthCheckComponent() : base(Io.Name("DXR File Health Check", "Checks DXR/GCode files for readability and printable safety. Supports optional direct code input and reports a single status.", Io.Dxr, 0)) { }
        public DxrFileHealthCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("DXRFileHealthCheckIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("File Path", "Path", "Optional explicit file path to check (.dxr, .gcode, .nc, .tap).", Access.Item, Requirement.MayBeMissing).Set(string.Empty);
            inputs.AddText("Watch Folder", "Folder", "Optional folder to scan for latest file. If empty, common default folders are tried.", Access.Item, Requirement.MayBeMissing).Set(string.Empty);
            inputs.AddBoolean("Use Latest From Folder", "Latest", "If True, checks newest matching file in folder. If False, uses explicit File Path.", ("Latest", "Newest file in the folder"), ("File Path", "The explicit file path")).Set(true);
            inputs.AddText("Allowed Extensions", "Ext", "Extensions considered for auto-scan (e.g. .dxr, .gcode, .nc).", Access.Twig, Requirement.MayBeMissing);
            inputs.AddInteger("Max Filename Length", "NameLen", "Maximum allowed filename length in characters.").Set(64);
            inputs.AddNumber("Large File Threshold MB", "LargeMB", "If file is larger than this and has almost no valid commands, it becomes NO GO.").Set(25.0);
            inputs.AddInteger("Min Command Lines", "MinCmd", "Minimum amount of valid movement command lines.").Set(3);
            inputs.AddText("Code Input", "Code", "Optional DXR/GCode text or line list to validate directly. If provided, file loading is skipped.", Access.Tree, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Checked File", "File", "File that was checked.");
            outputs.AddText("Status", "Status", "Short status text.");
            outputs.AddText("Report", "Report", "Detailed validation report.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string filePath, watchFolder; bool useLatest; int maxFileNameLength, minCommandLines; double largeFileThresholdMb; string[] ext;
            if (!access.GetItem(0, out filePath) || filePath == null) filePath = string.Empty;
            if (!access.GetItem(1, out watchFolder) || watchFolder == null) watchFolder = string.Empty;
            if (!access.GetItem(2, out useLatest)) useLatest = true;
            var allowedExtensionsInput = access.ItemArray(3, out ext) && ext != null ? ext.ToList() : new List<string>();
            if (!access.GetItem(4, out maxFileNameLength)) maxFileNameLength = 64;
            if (!access.GetItem(5, out largeFileThresholdMb)) largeFileThresholdMb = 25.0;
            if (!access.GetItem(6, out minCommandLines)) minCommandLines = 3;
            // Grasshopper 1 joined every item of the Code input, of all branches, into one text
            var codeParts = Gh1.AllTexts(access, 7);
            string codeInput = codeParts.Count == 0 ? string.Empty : string.Join("\n", codeParts);

            if (maxFileNameLength < 8)
                maxFileNameLength = 8;
            if (largeFileThresholdMb < 0.1)
                largeFileThresholdMb = 0.1;
            if (minCommandLines < 1)
                minCommandLines = 1;

            var allowedExtensions = NormalizeExtensions(allowedExtensionsInput);
            if (allowedExtensions.Count == 0)
            {
                allowedExtensions.Add(".dxr");
                allowedExtensions.Add(".gcode");
                allowedExtensions.Add(".nc");
                allowedExtensions.Add(".tap");
            }

            var report = new List<string>();
            var fatalIssues = new List<string>();
            var warnings = new List<string>();
            bool usingCodeInput = !string.IsNullOrWhiteSpace(codeInput);


            report.Add($"Config: Latest={useLatest}, MaxNameLen={maxFileNameLength}, LargeMB={largeFileThresholdMb:0.##}, MinCmd={minCommandLines}, CodeInput={usingCodeInput}");
            report.Add($"Extensions: {string.Join(", ", allowedExtensions)}");

            long fileSizeBytes = 0;
            double fileSizeMb = 0.0;
            string[] lines = Array.Empty<string>();
            int textLineCount = 0;
            int nonEmptyLineCount = 0;
            string resolvedPath = string.Empty;
            string extension = string.Empty;
            bool hasDxrMarker = false;

            if (usingCodeInput)
            {
                resolvedPath = "CODE INPUT";
                report.Add("Source: direct code input");

                lines = SplitLines(codeInput);
                textLineCount = lines.Length;
                nonEmptyLineCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                fileSizeBytes = Encoding.UTF8.GetByteCount(codeInput);
                fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

                report.Add($"Input size: {fileSizeBytes} bytes ({fileSizeMb:0.###} MB)");
                report.Add($"Text lines: {textLineCount}, non-empty lines: {nonEmptyLineCount}");

                if (nonEmptyLineCount == 0)
                    fatalIssues.Add("Code input contains no usable text content.");

                hasDxrMarker = lines.Any(l => (l ?? string.Empty).IndexOf("DXR.KUKA", StringComparison.OrdinalIgnoreCase) >= 0);
                extension = hasDxrMarker ? ".dxr" : ".gcode";
                report.Add($"Code mode type guess: {extension}");
            }
            else
            {
                string resolvedFolder = ResolveFolder(watchFolder, report);
                resolvedPath = ResolveFilePath(filePath, resolvedFolder, useLatest, allowedExtensions, report);

                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    fatalIssues.Add("No file selected or found.");
                    BuildOutputs(access, string.Empty, fatalIssues, warnings, report);
                    Gh1.Message(access, 2, "No file found to check.");
                    return;
                }

                report.Add($"Selected file: {resolvedPath}");

                if (!File.Exists(resolvedPath))
                    fatalIssues.Add("File does not exist.");

                extension = (Path.GetExtension(resolvedPath) ?? string.Empty).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                    fatalIssues.Add($"Extension '{extension}' is not allowed.");

                string fileName = Path.GetFileName(resolvedPath) ?? string.Empty;
                string baseName = Path.GetFileNameWithoutExtension(resolvedPath) ?? string.Empty;

                ValidateFilename(fileName, baseName, maxFileNameLength, fatalIssues, warnings, report);

                if (fatalIssues.Count == 0)
                {
                    try
                    {
                        var fi = new FileInfo(resolvedPath);
                        fileSizeBytes = fi.Length;
                        fileSizeMb = fi.Length / (1024.0 * 1024.0);
                        report.Add($"File size: {fileSizeBytes} bytes ({fileSizeMb:0.###} MB)");

                        if (fileSizeBytes <= 0)
                            fatalIssues.Add("File is empty (0 bytes).");

                        if (LooksBinary(resolvedPath))
                            fatalIssues.Add("File looks binary/corrupted (contains null bytes). DXR/GCode must be plain text.");

                        lines = File.ReadAllLines(resolvedPath, Encoding.UTF8);
                        textLineCount = lines.Length;
                        nonEmptyLineCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                        report.Add($"Text lines: {textLineCount}, non-empty lines: {nonEmptyLineCount}");

                        if (nonEmptyLineCount == 0)
                            fatalIssues.Add("File contains no usable text content.");
                    }
                    catch (Exception ex)
                    {
                        fatalIssues.Add($"File cannot be read: {ex.Message}");
                    }
                }
            }

            int movementLines = 0;
            bool hasM29 = false;
            bool hasGCodeEndMarker = false;
            double? minDetectedZ = null;
            double? firstPositiveZ = null;
            double? currentFeed = null;   // feed is modal: it stays until the next F word
            int zeroFeedMoves = 0, firstZeroFeedLine = -1;
            bool firstMoveSeen = false, firstMoveWithoutFeed = false;

            if (fatalIssues.Count == 0)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    bool isMoveLine = false;

                    if (extension == ".dxr")
                    {
                        if (DxrMoveRegex.IsMatch(line))
                        {
                            movementLines++;
                            isMoveLine = true;
                        }

                        if (!hasM29 && line.Trim().Equals("M29", StringComparison.OrdinalIgnoreCase))
                            hasM29 = true;

                        if (!hasDxrMarker && line.IndexOf("DXR.KUKA", StringComparison.OrdinalIgnoreCase) >= 0)
                            hasDxrMarker = true;
                    }
                    else
                    {
                        if (GCodeMoveRegex.IsMatch(line))
                        {
                            movementLines++;
                            isMoveLine = true;
                        }

                        if (!hasGCodeEndMarker)
                        {
                            string t = line.Trim();
                            if (t.Equals("M2", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("M30", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("M29", StringComparison.OrdinalIgnoreCase))
                            {
                                hasGCodeEndMarker = true;
                            }
                        }
                    }

                    Match feedMatch = FeedRegex.Match(line);
                    if (feedMatch.Success && double.TryParse(feedMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double feed))
                        currentFeed = feed;
                    if (isMoveLine)
                    {
                        if (!firstMoveSeen)
                        {
                            firstMoveSeen = true;
                            if (!currentFeed.HasValue) firstMoveWithoutFeed = true;
                        }
                        if (currentFeed.HasValue && currentFeed.Value <= 0.0)
                        {
                            zeroFeedMoves++;
                            if (firstZeroFeedLine < 0) firstZeroFeedLine = i + 1;
                        }
                    }

                    if (isMoveLine && TryExtractZValue(line, out double z))
                    {
                        if (!minDetectedZ.HasValue || z < minDetectedZ.Value)
                            minDetectedZ = z;

                        if (z > 0.0 && !firstPositiveZ.HasValue)
                            firstPositiveZ = z;
                    }
                }

                report.Add($"Detected movement lines: {movementLines}");
                report.Add(minDetectedZ.HasValue
                    ? $"Detected minimum Z: {minDetectedZ.Value:0.###} mm"
                    : "Detected minimum Z: (no Z value found in movement lines)");
                report.Add(firstPositiveZ.HasValue
                    ? $"Detected first positive layer Z: {firstPositiveZ.Value:0.###} mm"
                    : "Detected first positive layer Z: (none)");

                if (zeroFeedMoves > 0)
                {
                    fatalIssues.Add($"Feed rate 0: {zeroFeedMoves} movement line(s) run with F0, first in line {firstZeroFeedLine}. " +
                                    "A move with F0 never finishes - the machine stops there or reports an error.");
                }
                else if (firstMoveWithoutFeed)
                {
                    warnings.Add("The first movement line has no feed rate (F). The machine then uses whatever feed was active before, possibly 0.");
                }
                else
                {
                    report.Add("Feed rate: every movement line has a feed above 0.");
                }

                if (movementLines < minCommandLines)
                {
                    fatalIssues.Add($"Too few movement lines ({movementLines} < {minCommandLines}). File likely incomplete/invalid.");
                }

                if (extension == ".dxr")
                {
                    if (!hasDxrMarker)
                    {
                        fatalIssues.Add("DXR marker missing ('DXR.KUKA').");
                    }
                    if (!hasM29)
                    {
                        fatalIssues.Add("DXR end marker missing (M29).");
                    }
                }
                else
                {
                    if (!hasGCodeEndMarker)
                        warnings.Add("No clear GCode end marker (M2/M30/M29) detected.");
                }

                if (fileSizeMb >= largeFileThresholdMb && movementLines < minCommandLines * 2)
                {
                    fatalIssues.Add($"Large file ({fileSizeMb:0.##} MB) but almost no valid commands. Suspicious empty/corrupt export.");
                }

                if (fileSizeMb >= largeFileThresholdMb && nonEmptyLineCount < 50)
                {
                    fatalIssues.Add($"Large file ({fileSizeMb:0.##} MB) with very low content ({nonEmptyLineCount} lines). Suspicious export.");
                }

                if (minDetectedZ.HasValue && minDetectedZ.Value <= 0.0)
                {
                    fatalIssues.Add($"Build plate safety: detected Z <= 0 ({minDetectedZ.Value:0.###} mm). This would crash into the build plate.");
                }

                if (firstPositiveZ.HasValue && firstPositiveZ.Value < 1.5)
                {
                    warnings.Add($"First positive layer is below 1.5 mm ({firstPositiveZ.Value:0.###} mm). Verify first layer safety.");
                }
            }

            bool go = fatalIssues.Count == 0;
            BuildOutputs(access, resolvedPath, fatalIssues, warnings, report);

            if (go)
                Gh1.Message(access, 0, "GO: File passed validation.");
            else
                Gh1.Message(access, 2, "NO GO: File failed validation.");
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static bool TryExtractZValue(string line, out double z)
        {
            z = 0.0;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            Match m = ZValueRegex.Match(line);
            if (!m.Success)
                return false;

            string token = (m.Groups["z"].Value ?? string.Empty).Trim().Replace(',', '.');
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }

        private static bool ValidateFilename(
            string fileName,
            string baseName,
            int maxLength,
            List<string> fatalIssues,
            List<string> warnings,
            List<string> report)
        {
            bool ok = true;
            report.Add($"Filename: {fileName}");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fatalIssues.Add("Filename is empty.");
                return false;
            }

            if (fileName.Length > maxLength)
                warnings.Add($"Filename is long ({fileName.Length} > {maxLength}). Use shorter names for machine stability.");

            if (baseName.Contains("."))
            {
                fatalIssues.Add("Filename contains extra dot in basename. Use only one dot before extension.");
                ok = false;
            }

            if (!SafeBaseNameRegex.IsMatch(baseName))
            {
                fatalIssues.Add("Filename contains special characters. Allowed: A-Z, a-z, 0-9, underscore, hyphen.");
                ok = false;
            }

            return ok;
        }

        private static List<string> NormalizeExtensions(List<string> extensions)
        {
            var result = new List<string>();

            if (extensions == null)
                return result;

            for (int i = 0; i < extensions.Count; i++)
            {
                string e = extensions[i] ?? string.Empty;
                e = e.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(e))
                    continue;
                if (!e.StartsWith("."))
                    e = "." + e;
                if (!result.Contains(e))
                    result.Add(e);
            }

            return result;
        }

        private static string ResolveFolder(string watchFolder, List<string> report)
        {
            if (!string.IsNullOrWhiteSpace(watchFolder))
            {
                report.Add($"Watch folder input: {watchFolder}");
                return watchFolder;
            }

            string[] candidates =
            {
                @"D:\Data\Gcode",
                @"C:\ProgramData\Weber\GCode",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (Directory.Exists(candidate))
                {
                    report.Add($"Auto folder: {candidate}");
                    return candidate;
                }
            }

            report.Add("No auto folder found.");
            return string.Empty;
        }

        private static string ResolveFilePath(
            string filePath,
            string folder,
            bool useLatest,
            List<string> allowedExtensions,
            List<string> report)
        {
            if (!useLatest && !string.IsNullOrWhiteSpace(filePath))
                return filePath;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                report.Add("Watch folder missing or not found.");
                return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath;
            }

            string latest = FindLatestFile(folder, allowedExtensions);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                report.Add($"Latest file found: {latest}");
                return latest;
            }

            report.Add("No matching file found in watch folder.");
            return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath;
        }

        private static string FindLatestFile(string folder, List<string> allowedExtensions)
        {
            try
            {
                var di = new DirectoryInfo(folder);
                FileInfo latest = null;

                FileInfo[] files = di.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo f = files[i];
                    string ext = (f.Extension ?? string.Empty).ToLowerInvariant();
                    if (!allowedExtensions.Contains(ext))
                        continue;

                    if (latest == null || f.LastWriteTimeUtc > latest.LastWriteTimeUtc)
                        latest = f;
                }

                return latest?.FullName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksBinary(string path)
        {
            try
            {
                byte[] buffer = new byte[4096];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == 0)
                            return true;
                    }
                }
            }
            catch
            {
                // If byte scan fails, leave decision to text parsing stage.
            }

            return false;
        }


        private static void BuildOutputs(
            IDataAccess access,
            string checkedFile,
            List<string> fatalIssues,
            List<string> warnings,
            List<string> report)
        {
            bool go = fatalIssues.Count == 0;
            string status;

            if (go && warnings.Count > 0)
                status = "GO WITH WARNING";
            else if (go)
                status = "GO - FILE OK";
            else
                status = "NO GO - FILE CHECK FAILED";

            if (fatalIssues.Count > 0)
            {
                for (int i = 0; i < fatalIssues.Count; i++)
                    report.Add("ERROR: " + fatalIssues[i]);
            }

            if (warnings.Count > 0)
            {
                for (int i = 0; i < warnings.Count; i++)
                    report.Add("WARNING: " + warnings[i]);
            }

            access.SetItem(0, checkedFile ?? string.Empty);
            access.SetItem(1, status);
            access.SetTwig(2, report.ToArray(), null, null);
        }
    }

    // ============================================================== DXR variable width
    [IoId("8fe1dfa9-4512-41bb-b483-32999b0a2662")]
    public sealed class DxrVariableWidthComponent : PreviewComponent
    {
        private static readonly Regex XRegex = new Regex(@" X(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex YRegex = new Regex(@" Y(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex ZRegex = new Regex(@" Z(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex XeRegex = new Regex(@"XE=\[(-?\d+\.?\d*)\*P1\]", RegexOptions.Compiled);
        private static readonly Regex EgesRegex = new Regex(@"IC\[-?\d+\.?\d*\]", RegexOptions.Compiled);

        private static readonly System.Drawing.Color ColorThin = System.Drawing.Color.FromArgb(0, 102, 204);
        private static readonly System.Drawing.Color ColorThick = System.Drawing.Color.FromArgb(230, 90, 30);

        public DxrVariableWidthComponent() : base(Io.Name("DXR Variable Width", "Scales the XE extrusion values of a DXR program along X, Y or Z so the printed line width runs from Width Start to Width End. Use it between DXR Generator and Safe Component. Only lines with XE=[value*P1] are changed; all other lines are passed through unchanged.", Io.Utilities, 0)) { }
        public DxrVariableWidthComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("DXRVariableWidthIcon");
        // the segments in their colours, thicker where the bead gets wider (as in Grasshopper 1)
        protected override int[] OwnOutputs => new[] { 1 };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("DXR Code", "Code", "Complete DXR program, as list of lines (DXR Generator output) or as one text.", Access.Twig);
            inputs.AddNumber("Base Width", "Wbase", "Line width [mm] the existing XE values were calculated for (your extrusion calibration, e.g. 3.0).").Set(3.0).Values(1, 2, 3, 4, 5, 6, 8, 10);
            inputs.AddNumber("Width Start", "WA", "Target line width [mm] at the start of the gradient (low end of the axis, or high end if Flip is true).").Set(4.0).Values(2, 3, 4, 5, 6, 8, 10, 15, 20);
            inputs.AddNumber("Width End", "WB", "Target line width [mm] at the end of the gradient (high end of the axis, or low end if Flip is true).").Set(10.0).Values(2, 3, 4, 5, 6, 8, 10, 15, 20);
            inputs.AddInteger("Axis", "Axis", "Axis along which the width changes: 0 = X, 1 = Y, 2 = Z (by height). Right-click or connect a value list to choose.").Set(0).Choices("X", "Y", "Z");
            inputs.AddInteger("Mode", "Mode", "Shape of the transition: 0 = Linear, 1 = Smooth (slow start and end), 2 = Gamma (curve set by Gamma).").Set(0).Choices("Linear", "Smooth", "Gamma");
            inputs.AddNumber("Gamma", "G", "Only for Mode = Gamma. > 1 = width changes late, < 1 = width changes early (e.g. 2.0 or 0.5).", Access.Item, Requirement.MayBeMissing).Set(2.0);
            inputs.AddBoolean("Flip", "Flip", "If true, the gradient is reversed (Width Start at the high end of the axis).").Set(false);
            inputs.AddNumber("Range Min", "Min", "Optional fixed start of the gradient on the axis [mm]. Leave empty to use the part's extent automatically.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Range Max", "Max", "Optional fixed end of the gradient on the axis [mm]. Leave empty to use the part's extent automatically.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Max Factor", "MaxF", "Safety limit for the scale factor (e.g. 5.0 = at most 5x the original extrusion). 0 = no limit.", Access.Item, Requirement.MayBeMissing).Set(0.0);
            inputs.AddBoolean("Ignore First Layer", "Skip1", "If true, the first layer (skirt) is left out when measuring the automatic range, so the skirt does not stretch the gradient. The first layer is still scaled.").Set(true);
            inputs.AddBoolean("Active", "On", "If false, the DXR code is passed through unchanged.").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("DXR Code", "Code", "DXR program with scaled XE values (list of lines). Connect to Safe Component or Fast GCode DXR Preview.", Access.Twig);
            outputs.AddLine("Segments", "Seg", "Extruded segments of the toolpath (travel moves are left out).", Access.Twig);
            outputs.AddNumber("Widths", "W", "Resulting line width [mm] per segment.", Access.Twig);
            outputs.AddNumber("Factors", "F", "Scale factor applied to the XE value per segment.", Access.Twig);
            outputs.AddColour("Colors", "Col", "Color per segment from thin (blue) to thick (orange), e.g. for Custom Preview.", Access.Twig);
            outputs.AddInteger("Layer", "L", "Layer index per segment (0 = lowest layer).", Access.Twig);
            outputs.AddText("Info", "Info", "Summary: axis, range, widths, number of scaled segments and extrusion before/after.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            object[] raw;
            Picture.Clear();
            if (!access.ItemArray(0, out raw) || raw == null) return;
            double baseWidth = 3.0, widthA = 4.0, widthB = 10.0, gamma = 2.0, rangeMin = double.NaN, rangeMax = double.NaN, maxFactor = 0.0;
            int axis = 0, mode = 0; bool flip = false, ignoreFirstLayer = true, active = true;
            access.GetItem(1, out baseWidth); access.GetItem(2, out widthA); access.GetItem(3, out widthB);
            access.GetItem(4, out axis); access.GetItem(5, out mode);
            if (!access.GetItem(6, out gamma)) gamma = 2.0;
            access.GetItem(7, out flip);
            bool hasMin = access.GetItem(8, out rangeMin);
            bool hasMax = access.GetItem(9, out rangeMax);
            if (!access.GetItem(10, out maxFactor)) maxFactor = 0.0;
            access.GetItem(11, out ignoreFirstLayer); access.GetItem(12, out active);

            var lines = SplitLines(string.Join("\n", raw.Where(o => o != null).Select(o => o as string ?? o.ToString())));
            var info = new List<string>();

            if (!active)
            {
                access.SetTwig(0, lines.ToArray(), null, null);
                info.Add("Inactive: DXR code passed through unchanged.");
                access.SetTwig(6, info.ToArray(), null, null);
                return;
            }

            if (baseWidth <= 0.0)
            {
                Gh1.Message(access, 1, "Base Width must be greater than 0. Using 3.0 mm.");
                baseWidth = 3.0;
            }
            if (widthA <= 0.0 || widthB <= 0.0)
            {
                Gh1.Message(access, 2, "Width Start and Width End must be greater than 0.");
                return;
            }
            if (axis < 0 || axis > 2)
            {
                Gh1.Message(access, 1, "Axis must be 0 (X), 1 (Y) or 2 (Z). Using X.");
                axis = 0;
            }
            if (mode < 0 || mode > 2)
            {
                Gh1.Message(access, 1, "Mode must be 0 (Linear), 1 (Smooth) or 2 (Gamma). Using Linear.");
                mode = 0;
            }

            Regex axisRegex = axis == 0 ? XRegex : (axis == 1 ? YRegex : ZRegex);
            string axisName = axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");

            // Pass 1: extent along the axis (optionally without the first layer / skirt).
            double zFirst = double.NaN;
            if (ignoreFirstLayer)
            {
                foreach (string ln in lines)
                {
                    if (!ln.Contains("XE=[")) continue;
                    if (TryMatch(ZRegex, ln, out double z) && (double.IsNaN(zFirst) || z < zFirst))
                        zFirst = z;
                }
            }

            double aMin = double.PositiveInfinity;
            double aMax = double.NegativeInfinity;
            foreach (string ln in lines)
            {
                if (!ln.Contains("XE=[")) continue;
                if (!TryMatch(axisRegex, ln, out double v)) continue;
                if (!double.IsNaN(zFirst) && TryMatch(ZRegex, ln, out double z) && Math.Abs(z - zFirst) < 1e-6)
                    continue;
                if (v < aMin) aMin = v;
                if (v > aMax) aMax = v;
            }

            if (double.IsInfinity(aMin))
            {
                Gh1.Message(access, 1, "No XE moves found. Is this DXR code from the DXR Generator?");
                access.SetTwig(0, lines.ToArray(), null, null);
                info.Add("No XE moves found - code passed through unchanged.");
                access.SetTwig(6, info.ToArray(), null, null);
                return;
            }

            double lo = hasMin ? rangeMin : aMin;
            double hi = hasMax ? rangeMax : aMax;
            double span = hi - lo;
            double fA = widthA / baseWidth;
            double fB = widthB / baseWidth;

            double FactorAt(double value)
            {
                double t = span != 0.0 ? (value - lo) / span : 0.0;
                if (flip) t = 1.0 - t;
                double f = fA + (fB - fA) * Distribute(t, mode, gamma);
                if (maxFactor > 0.0 && f > maxFactor) f = maxFactor;
                return f;
            }

            // Pass 2: scale XE values and collect segment data.
            var outLines = new List<string>(lines.Count);
            var segments = new List<Line>();
            var widths = new List<double>();
            var factors = new List<double>();
            var layers = new List<int>();
            var zLevels = new List<double>();
            bool hasPrev = false;
            Point3d prev = Point3d.Unset;
            double totalOld = 0.0;
            double totalNew = 0.0;
            int eqLine = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string ln = lines[i];
                if (eqLine < 0 && ln.Contains(";Eges"))
                    eqLine = i;

                if (ln.Contains("XE=[") && ln.Contains(" X"))
                {
                    Match mXe = XeRegex.Match(ln);
                    if (mXe.Success && TryMatch(XRegex, ln, out double cx) && TryMatch(YRegex, ln, out double cy) && TryMatch(ZRegex, ln, out double cz))
                    {
                        var cur = new Point3d(cx, cy, cz);
                        double xe = double.Parse(mXe.Groups[1].Value, CultureInfo.InvariantCulture);
                        double curAxis = cur[axis];
                        double reference = hasPrev ? (prev[axis] + curAxis) / 2.0 : curAxis;
                        double f = FactorAt(reference);
                        double newXe = xe * f;
                        totalOld += xe;
                        totalNew += newXe;

                        if (xe != 0.0 && hasPrev)
                        {
                            segments.Add(new Line(prev, cur));
                            widths.Add(f * baseWidth);
                            factors.Add(f);
                            layers.Add(LayerIndex(zLevels, cz));
                        }

                        ln = ln.Substring(0, mXe.Index)
                           + "XE=[" + newXe.ToString("0.000000", CultureInfo.InvariantCulture) + "*P1]"
                           + ln.Substring(mXe.Index + mXe.Length);
                        prev = cur;
                        hasPrev = true;
                    }
                }
                outLines.Add(ln);
            }

            // Keep the header's total extrusion in sync.
            if (eqLine >= 0)
                outLines[eqLine] = EgesRegex.Replace(outLines[eqLine], "IC[" + totalNew.ToString("0.000", CultureInfo.InvariantCulture) + "]", 1);

            double fMin = Math.Min(fA, fB);
            double fMax = Math.Max(fA, fB);
            if (maxFactor > 0.0) fMax = Math.Min(fMax, maxFactor);
            var colors = new List<System.Drawing.Color>(factors.Count);
            var picture = new ShadedPreview.Frame();
            for (int i = 0; i < factors.Count; i++)
            {
                double t = fMax > fMin ? (factors[i] - fMin) / (fMax - fMin) : 0.0;
                var c = Blend(ColorThin, ColorThick, t);
                colors.Add(c);
                picture.Line(segments[i], c, 1 + (int)Math.Round(t * 4.0));
            }
            Picture.Show(picture);

            string modeName = mode == 0 ? "Linear" : (mode == 1 ? "Smooth" : "Gamma " + gamma.ToString("0.##", CultureInfo.InvariantCulture));
            info.Add($"Axis: {axisName}   Range: {F3(lo)} .. {F3(hi)}   Mode: {modeName}{(flip ? "   Flipped" : "")}");
            if (!hasMin && !hasMax && !double.IsNaN(zFirst))
                info.Add($"Range measured without first layer (Z = {F3(zFirst)})");
            else
                info.Add(hasMin || hasMax ? "Range set manually (missing side measured automatically)" : "Range measured from all moves");
            info.Add($"Width {F2(widthA)} -> {F2(widthB)} mm (base {F2(baseWidth)} mm)   Factor {F3(fA)} .. {F3(fB)}{(maxFactor > 0.0 ? $"   capped at {F2(maxFactor)}" : "")}");
            info.Add($"Scaled segments: {segments.Count}   Layers: {zLevels.Count}");
            info.Add($"Extrusion old -> new: {totalOld.ToString("0.0", CultureInfo.InvariantCulture)} -> {totalNew.ToString("0.0", CultureInfo.InvariantCulture)}" +
                     $"  (x{(totalOld != 0.0 ? totalNew / totalOld : 0.0).ToString("0.000", CultureInfo.InvariantCulture)})");

            access.SetTwig(0, outLines.ToArray(), null, null);
            access.SetTwig(1, segments.ToArray(), null, null);
            access.SetTwig(2, widths.ToArray(), null, null);
            access.SetTwig(3, factors.ToArray(), null, null);
            access.SetTwig(4, colors.Select(c => Grasshopper2.Types.Colour.Colour.FromGdi(c)).ToArray(), null, null);
            access.SetTwig(5, layers.ToArray(), null, null);
            access.SetTwig(6, info.ToArray(), null, null);
        }

        private static double Distribute(double t, int mode, double gamma)
        {
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            if (mode == 1)
                return t * t * (3.0 - 2.0 * t);
            if (mode == 2)
                return Math.Pow(t, gamma > 0.0 ? gamma : 1.0);
            return t;
        }

        private static int LayerIndex(List<double> zLevels, double z)
        {
            for (int i = 0; i < zLevels.Count; i++)
            {
                if (Math.Abs(zLevels[i] - z) < 1e-4)
                    return i;
            }
            zLevels.Add(z);
            return zLevels.Count - 1;
        }

        private static bool TryMatch(Regex regex, string line, out double value)
        {
            Match m = regex.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            value = 0.0;
            return false;
        }

        private static System.Drawing.Color Blend(System.Drawing.Color a, System.Drawing.Color b, double t)
        {
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return System.Drawing.Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static string F2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
                return result;
            foreach (string ln in text.Split('\n'))
                result.Add(ln.TrimEnd('\r'));
            return result;
        }

    }
}
