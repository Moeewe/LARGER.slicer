using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using LARGERslicer.Types;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// DXR GCode Postprocessor - Converts GCode to DXR format.
    /// Use this component when you have GCode input from a slicer.
    /// </summary>
    public class DXRPostprocessorComponent : GH_Component
    {
        public DXRPostprocessorComponent()
          : base("DXR GCode Postprocessor", "DXR GCode",
              "Converts GCode to DXR format. Parses GCode to extract robot path, extrusion amounts, and print speeds.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("GCode", "GCode", "Complete GCode file content as text (can be tree/list). Extracts robot path, extrusion, and speed from GCode.", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Machine Settings", "Machine", "Printer configuration (connect Machine Settings component)", GH_ParamAccess.item);
            
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("DXR File", "DXR", "Complete DXR file content ready for export", GH_ParamAccess.list);
            pManager.AddTextParameter("Process Info", "Info", "Generation summary and statistics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MachineSettings machineSettings = null;
            GH_Structure<GH_String> gCodeTree = null;

            var processInfo = new List<string>();
            var result = new List<string>();

            try
            {
                // Get GCode input as tree (required)
                if (!DA.GetDataTree(0, out gCodeTree) || gCodeTree == null || gCodeTree.Branches.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GCode input is required.");
                    processInfo.Add("ERROR: GCode input is empty or not connected");
                    DA.SetDataList(0, result);
                    DA.SetDataList(1, processInfo);
                    return;
                }

                try
                {
                    // Combine all branches of the tree into a single string
                    var gCodeLines = new List<string>();
                    foreach (var branch in gCodeTree.Branches)
                    {
                        foreach (var item in branch)
                        {
                            if (item != null && !string.IsNullOrWhiteSpace(item.Value))
                            {
                                gCodeLines.Add(item.Value);
                            }
                        }
                    }

                    string gCodeInput = string.Join("\n", gCodeLines);
                    processInfo.Add($"GCode input detected: {gCodeLines.Count} lines, {gCodeInput.Length} characters total");

                    // Get machine settings (optional)
                    DA.GetData(1, ref machineSettings);

                    // Parse GCode to extract movement data and analyze header/footer
                    processInfo.Add("Parsing GCode to extract movement data and analyze header/footer");
                    var parsedData = ParseGCode(gCodeInput, processInfo, machineSettings);
                    
                    List<string> robotLines = parsedData.RobotLines;
                    List<double> P1_list = parsedData.ExtrusionAmounts;
                    List<double> F1_list = parsedData.PrintSpeeds;
                    
                    processInfo.Add($"Parsed: {robotLines.Count} movements, {P1_list.Count} extrusions, {F1_list.Count} speeds");

                    // Use extracted or provided machine settings
                    MachineSettings finalSettings = parsedData.ExtractedSettings ?? machineSettings;

                    // Process robot lines using the DXR conversion logic
                    var dxrLines = DXRHelper.ProcessRobotLinesToDXR(robotLines, P1_list, F1_list, processInfo, finalSettings);
                    result.AddRange(dxrLines);

                    // Add machine end settings (always turns everything off)
                    string[] gCodeEnd;
                    if (finalSettings != null)
                    {
                        gCodeEnd = finalSettings.GetEndGCode();
                        processInfo.Add("Added machine shutdown sequence (all systems OFF)");
                    }
                    else
                    {
                        // Default end sequence
                        gCodeEnd = new string[]
                        {
                            "N9999994 V.P.VAR_heatbedtemp = 0",
                            "N9999995 L heatbedTemp_sub.nc",
                            "N9999996 V.P.VAR_fan = 0",
                            "N9999997 L fan_sub.nc",
                            "N9999998 V.P.VAR_extrudertemp = 0",
                            "N9999999 L extruderTemp_sub.nc",
                            "M29"
                        };
                        processInfo.Add("Added default shutdown sequence");
                    }

                    result.AddRange(gCodeEnd);
                    
                    // Add footer comment with generator info
                    string footerComment = DXRHelper.GenerateFooterComment();
                    result.Add(footerComment);
                    
                    processInfo.Add($"Total DXR file lines: {result.Count}");
                }
                catch (Exception innerEx)
                {
                    processInfo.Add($"Error during DXR processing: {innerEx.Message}");
                    processInfo.Add($"Stack trace: {innerEx.StackTrace}");
                    result.Clear();
                }
            }
            catch (Exception ex)
            {
                processInfo.Add($"Error during processing: {ex.Message}");
                processInfo.Add($"Stack trace: {ex.StackTrace}");
                result.Clear();
            }

            DA.SetDataList(0, result);
            DA.SetDataList(1, processInfo);
        }

        private class ParsedGCodeData
        {
            public List<string> RobotLines { get; set; }
            public List<double> ExtrusionAmounts { get; set; }
            public List<double> PrintSpeeds { get; set; }
            public MachineSettings ExtractedSettings { get; set; }

            public ParsedGCodeData()
            {
                RobotLines = new List<string>();
                ExtrusionAmounts = new List<double>();
                PrintSpeeds = new List<double>();
                ExtractedSettings = null;
            }
        }

        private ParsedGCodeData ParseGCode(string gCode, List<string> processInfo, MachineSettings providedSettings)
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
                
                result.ExtractedSettings = new MachineSettings(bedTemp, nozzleTemp, fanSpeed);
                
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
                    result.ExtractedSettings = new MachineSettings(bedTemp, nozzleTemp, fanPercent);
                }
            }
            else if (providedSettings != null)
            {
                processInfo.Add("No temperature settings found in GCode header - using provided Machine Settings");
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

            int movementCount = 0;
            int extrusionCount = 0;
            int speedCount = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                
                // Skip comments and empty lines
                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
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

                // Only add movement if coordinates changed or extrusion occurred
                bool hasMovement = (x.HasValue || y.HasValue || z.HasValue);
                bool hasExtrusion = e.HasValue;

                if (hasMovement || hasExtrusion)
                {
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

                    // Update state
                    lastX = currentX;
                    lastY = currentY;
                    lastZ = currentZ;
                    lastF = currentF;
                }
            }

            processInfo.Add($"Parsed GCode: {movementCount} movements, {extrusionCount} extrusions, {speedCount} speed values");
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


        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRPostprocessorIcon.png");
        public override Guid ComponentGuid => new Guid("B8E9A1F2-4C7D-4E5F-9A2B-3C8D9E0F1A2B");
    }
} 



