using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LARGERslicer.Types;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for DXR file generation. Contains shared logic for converting robot paths to DXR format.
    /// </summary>
    public static class DXRHelper
    {
        /// <summary>
        /// Converts robot movement lines with extrusion and speed data into DXR file format.
        /// </summary>
        /// <param name="robotLines">List of robot movement lines in PTP format</param>
        /// <param name="P1_list">Extrusion amounts (relative, per movement)</param>
        /// <param name="F1_list">Print speeds (mm/min, per movement)</param>
        /// <param name="processInfo">List to add process information to</param>
        /// <param name="machineSettings">Machine settings for header generation</param>
        /// <returns>List of DXR file lines</returns>
        public static List<string> ProcessRobotLinesToDXR(
            List<string> robotLines,
            List<double> P1_list,
            List<double> F1_list,
            List<string> processInfo,
            MachineSettings machineSettings)
        {
            var result = new List<string>();
            
            // Null checks
            if (robotLines == null)
            {
                processInfo?.Add("ERROR: robotLines is null");
                return result;
            }
            
            if (P1_list == null)
            {
                P1_list = new List<double>();
                processInfo?.Add("WARNING: P1_list is null, using empty list");
            }
            
            if (F1_list == null)
            {
                F1_list = new List<double>();
                processInfo?.Add("WARNING: F1_list is null, using empty list");
            }
            
            if (processInfo == null)
            {
                processInfo = new List<string>();
            }

            var dxrHeader = new List<string>();
            var X_vals = new List<double>();
            var Y_vals = new List<double>();
            var Z_vals = new List<double>();

            int line_num;
            var movementLines = new List<string>();

            // Regex for coordinates and angles
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
            var ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)");
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");
            var ra = new Regex(@"A\s*([-+]?[0-9]*\.?[0-9]+)");
            var rb = new Regex(@"B\s*([-+]?[0-9]*\.?[0-9]+)");
            var rc = new Regex(@"C\s*([-+]?[0-9]*\.?[0-9]+)");

            // Collect valid movement lines with error handling
            foreach (string rawLine in robotLines)
            {
                if (string.IsNullOrEmpty(rawLine))
                    continue;
                    
                try
                {
                    string line = rawLine.Trim();
                    if (!line.Contains("PTP"))
                        continue;

                    Match mx = rx.Match(line);
                    Match my = ry.Match(line);
                    Match mz = rz.Match(line);

                    // Safe parsing with error handling
                    if (mx.Success)
                    {
                        if (double.TryParse(mx.Groups[1].Value, out double xVal))
                            X_vals.Add(xVal);
                    }
                    if (my.Success)
                    {
                        if (double.TryParse(my.Groups[1].Value, out double yVal))
                            Y_vals.Add(yVal);
                    }
                    if (mz.Success)
                    {
                        if (double.TryParse(mz.Groups[1].Value, out double zVal))
                            Z_vals.Add(zVal);
                    }

                    if (mx.Success || my.Success || mz.Success)
                        movementLines.Add(line);
                }
                catch (Exception ex)
                {
                    processInfo.Add($"WARNING: Failed to parse line '{rawLine}': {ex.Message}");
                    continue;
                }
            }

            // Clip P1 and F1 to match movement count (with safety checks)
            int movement_count = movementLines.Count;
            if (movement_count == 0)
            {
                processInfo.Add("ERROR: No valid movement lines found");
                return result;
            }
            
            // Safe GetRange with bounds checking
            int p1Count = Math.Min(P1_list.Count, movement_count);
            int f1Count = Math.Min(F1_list.Count, movement_count);
            
            if (p1Count > 0)
            {
                P1_list = P1_list.GetRange(0, p1Count);
            }
            else
            {
                P1_list = new List<double>();
            }
            
            if (f1Count > 0)
            {
                F1_list = F1_list.GetRange(0, f1Count);
            }
            else
            {
                F1_list = new List<double>();
            }

            processInfo.Add($"Found {movement_count} valid movement lines");
            processInfo.Add($"P1 values used: {P1_list.Count}");
            processInfo.Add($"F1 values used: {F1_list.Count}");

            // Calculate bounds
            double xmin = X_vals.Count > 0 ? X_vals.Min() : 0;
            double xmax = X_vals.Count > 0 ? X_vals.Max() : 0;
            double ymin = Y_vals.Count > 0 ? Y_vals.Min() : 0;
            double ymax = Y_vals.Count > 0 ? Y_vals.Max() : 0;
            double zmin = Z_vals.Count > 0 ? Z_vals.Min() : 0;
            double zmax = Z_vals.Count > 0 ? Z_vals.Max() : 0;

            // Calculate total extrusion
            double totalExtrusion = P1_list.Sum();
            processInfo.Add($"Total extrusion: {totalExtrusion:F3}");

            // Calculate estimated print time (in seconds)
            double estimatedTime = CalculateEstimatedPrintTime(movementLines, F1_list);
            int runtimeSeconds = (int)Math.Round(estimatedTime);
            processInfo.Add($"Estimated print time: {runtimeSeconds} seconds ({estimatedTime / 60.0:F1} minutes)");

            // Calculate number of layers (count unique Z values or Z changes)
            int layerCount = CalculateLayerCount(Z_vals);
            processInfo.Add($"Detected {layerCount} layers");

            // Generate DXR header FIRST (before machine settings)
            // Header must come before machine settings according to DXR format
            dxrHeader.Add($";ProgRunTimeTotal =[{runtimeSeconds}]");
            dxrHeader.Add(";machine_type =[DXR.KUKA]");
            dxrHeader.Add(";post_processor_version =[V1.0.0]");
            dxrHeader.Add(";1 SD.ACT.GEN.DESC.NAME =\"DEFAULT\"");
            dxrHeader.Add($";number of rows in org. file =[{robotLines.Count}]");
            dxrHeader.Add($";number of movement rows = [{movement_count}]");
            dxrHeader.Add($";number of layers =[{layerCount}]");
            dxrHeader.Add($";Xmin = [{xmin:F3}]");
            dxrHeader.Add($";Xmax = [{xmax:F3}]");
            dxrHeader.Add($";Ymin = [{ymin:F3}]");
            dxrHeader.Add($";Ymax = [{ymax:F3}]");
            dxrHeader.Add($";Zmin = [{zmin:F3}]");
            dxrHeader.Add($";Zmax = [{zmax:F3}]");
            dxrHeader.Add($";Eges = IC[{totalExtrusion:F3}]");
            dxrHeader.Add("; config end");
            dxrHeader.Add(";=================================");

            result.AddRange(dxrHeader);
            processInfo.Add("Added DXR header with calculated values");

            // Add machine start settings AFTER header (as shown in example)
            if (machineSettings != null)
            {
                string[] startGCode = machineSettings.GetStartGCode();
                result.AddRange(startGCode);
                
                // Calculate next line number after start commands
                // Each start command increments by 10, so next line is: 10 + (number of commands * 10)
                line_num = 10 + (startGCode.Length * 10);
                
                processInfo.Add("Added machine start settings (Bed: " + machineSettings.BedTemperature + "°C, Nozzle: " + machineSettings.NozzleTemperature + "°C, Cooling: " + machineSettings.CoolingPercentage + "%)");
            }
            else
            {
                // If no machine settings, still add G90 at N10
                result.Add("N10 G90");
                line_num = 20; // Start movements at N20
            }

            // Process movements with correct DXR format
            for (int i = 0; i < movementLines.Count; i++)
            {
                string line = movementLines[i];
                double P1 = i < P1_list.Count ? P1_list[i] : 0.0;
                double F1 = i < F1_list.Count ? F1_list[i] : 1000.0;

                // Extract coordinates using helper function
                string X = TryMatch(rx, line, "X");
                string Y = TryMatch(ry, line, "Y");
                string Z = TryMatch(rz, line, "Z");
                string A = TryMatch(ra, line, "A");
                string B = TryMatch(rb, line, "B");
                string C = TryMatch(rc, line, "C");

                // Build DXR line with only non-empty coordinate values
                // Format: N{line_num} G1 F{f1:F3} X{x:F3} Y{y:F3} Z{z:F3} [A B C if present] G91 XE=[{p1:F6}*P1] G90
                var coordinateParts = new List<string>();
                if (!string.IsNullOrEmpty(X)) coordinateParts.Add(X);
                if (!string.IsNullOrEmpty(Y)) coordinateParts.Add(Y);
                if (!string.IsNullOrEmpty(Z)) coordinateParts.Add(Z);
                if (!string.IsNullOrEmpty(A)) coordinateParts.Add(A);
                if (!string.IsNullOrEmpty(B)) coordinateParts.Add(B);
                if (!string.IsNullOrEmpty(C)) coordinateParts.Add(C);
                
                string coordinates = string.Join(" ", coordinateParts);
                string dxrLine = $"N{line_num} G1 F{F1:F3} {coordinates} G91 XE=[{P1:F6}*P1] G90";
                result.Add(dxrLine.Trim());

                line_num += 10;
            }

            int firstMovementLine = line_num - (movementLines.Count * 10);
            processInfo.Add($"Generated {movementLines.Count} DXR movement lines (starting at N{firstMovementLine})");

            return result;
        }

        /// <summary>
        /// Helper function to extract coordinate values from a line using regex
        /// </summary>
        private static string TryMatch(Regex r, string line, string label)
        {
            if (string.IsNullOrEmpty(line))
                return "";
                
            try
            {
                Match m = r.Match(line);
                if (m.Success && double.TryParse(m.Groups[1].Value, out double value))
                {
                    return $"{label}{value:F3}";
                }
            }
            catch
            {
                // Silently ignore parsing errors
            }
            return "";
        }

        /// <summary>
        /// Calculates estimated print time based on movement lines and speeds.
        /// </summary>
        /// <param name="movementLines">List of movement lines</param>
        /// <param name="F1_list">Speed values in mm/min</param>
        /// <returns>Estimated time in seconds</returns>
        public static double CalculateEstimatedPrintTime(List<string> movementLines, List<double> F1_list)
        {
            if (movementLines == null || movementLines.Count < 2 || F1_list == null || F1_list.Count == 0)
                return 0.0;

            double totalTime = 0.0;
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
            var ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)");
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");

            double lastX = 0, lastY = 0, lastZ = 0;
            bool firstMove = true;

            for (int i = 0; i < movementLines.Count; i++)
            {
                string line = movementLines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                // Extract coordinates with error handling
                double x = 0, y = 0, z = 0;
                try
                {
                    var mx = rx.Match(line);
                    var my = ry.Match(line);
                    var mz = rz.Match(line);

                    if (mx.Success && double.TryParse(mx.Groups[1].Value, out double xVal)) x = xVal;
                    if (my.Success && double.TryParse(my.Groups[1].Value, out double yVal)) y = yVal;
                    if (mz.Success && double.TryParse(mz.Groups[1].Value, out double zVal)) z = zVal;
                }
                catch
                {
                    // Skip this line if parsing fails
                    continue;
                }

                if (!firstMove)
                {
                    // Calculate distance
                    double distance = Math.Sqrt(
                        Math.Pow(x - lastX, 2) +
                        Math.Pow(y - lastY, 2) +
                        Math.Pow(z - lastZ, 2)
                    );

                    // Get speed for this movement (F1 value in mm/min)
                    double speed = i < F1_list.Count ? F1_list[i] : 1000.0;

                    // Time = distance / speed * 60 (result in seconds)
                    if (speed > 0)
                        totalTime += (distance / speed) * 60;
                }

                lastX = x;
                lastY = y;
                lastZ = z;
                firstMove = false;
            }

            return totalTime; // Return time in seconds
        }

        /// <summary>
        /// Calculates the number of layers based on unique Z values.
        /// Layers are determined by counting distinct Z heights (rounded to 0.1mm precision).
        /// </summary>
        /// <param name="Z_vals">List of Z coordinate values</param>
        /// <returns>Number of unique layers</returns>
        private static int CalculateLayerCount(List<double> Z_vals)
        {
            if (Z_vals == null || Z_vals.Count == 0)
                return 0;

            // Round Z values to 0.1mm precision to group similar heights
            // This handles floating point precision issues
            var uniqueLayers = new HashSet<int>();
            const double layerPrecision = 0.1; // 0.1mm precision for layer detection

            foreach (double z in Z_vals)
            {
                // Round to nearest 0.1mm and convert to integer for HashSet
                int layerKey = (int)Math.Round(z / layerPrecision);
                uniqueLayers.Add(layerKey);
            }

            return uniqueLayers.Count;
        }

        /// <summary>
        /// Generates a footer comment with generator information, timestamp, and version.
        /// </summary>
        /// <returns>Footer comment line</returns>
        public static string GenerateFooterComment()
        {
            // Get current timestamp in ISO 8601 format with timezone
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss");
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(now);
            string timezone = $"{(offset.Hours >= 0 ? "+" : "")}{offset.Hours:D2}:{offset.Minutes:D2}";
            
            string version = "1.0.0"; // Version can be updated as needed
            
            return $"; DXR generated by LARGERslicer FH Münster Moritz Wesseler - {timestamp} UTC({timezone}) Postprocessor {version}";
        }
    }
}

