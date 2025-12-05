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
        /// <param name="layerTypes">Optional: Layer types for each movement (SKIRT, SKIN, WALL, INFILL, etc.)</param>
        /// <param name="isRetraction">Optional: True if movement is retraction without position change</param>
        /// <returns>List of DXR file lines</returns>
        public static List<string> ProcessRobotLinesToDXR(
            List<string> robotLines,
            List<double> P1_list,
            List<double> F1_list,
            List<string> processInfo,
            MachineSettings machineSettings,
            List<string> layerTypes = null,
            List<bool> isRetraction = null)
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
            
            if (layerTypes == null)
            {
                layerTypes = new List<string>();
            }
            
            if (isRetraction == null)
            {
                isRetraction = new List<bool>();
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

            // Calculate total extrusion (for Eges field in header)
            double totalExtrusion = P1_list.Sum();
            processInfo.Add($"Total extrusion: {totalExtrusion:F3}");

            // Calculate estimated print time (in seconds)
            double estimatedTime = CalculateEstimatedPrintTime(movementLines, F1_list);
            int runtimeSeconds = (int)Math.Round(estimatedTime);
            processInfo.Add($"Estimated print time: {runtimeSeconds} seconds ({estimatedTime / 60.0:F1} minutes)");

            // Detect layer changes BEFORE generating header (so we can use accurate layer count)
            // This uses intelligent detection for both planar and non-planar printing
            List<int> layerChangeIndices = DetectLayerChanges(movementLines, processInfo);
            // Layer count = number of layer changes + 1 (for the first layer)
            // If no layer changes detected, assume at least 1 layer
            int layerCount = Math.Max(1, layerChangeIndices.Count + 1);
            processInfo.Add($"Detected {layerCount} layers ({layerChangeIndices.Count} layer changes + initial layer)");

            // Generate DXR header FIRST (before machine settings)
            // Minimal header format - only essential fields
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
            // Eges: Total material volume/extrusion in mm³ (IC format)
            dxrHeader.Add($";Eges = IC[{totalExtrusion:F3}]");
            dxrHeader.Add("; config end");
            dxrHeader.Add(";=================================");

            result.AddRange(dxrHeader);
            // CRITICAL: G90 must be standalone (no N-number) immediately after header separator
            result.Add("G90");
            processInfo.Add("Added DXR header with calculated values");
            processInfo.Add("Added standalone G90 command (absolute positioning mode)");

            // Add machine start settings (temperatures, fan, etc.) if provided
            // These come after header but before layer initialization
            line_num = 10; // Start machine settings at N10
            if (machineSettings != null)
            {
                string[] startGCode = machineSettings.GetStartGCode();
                if (startGCode != null && startGCode.Length > 0)
                {
                    result.AddRange(startGCode);
                    // Update line_num to continue after machine settings
                    // Count how many lines were added
                    int settingsLines = startGCode.Length;
                    // Each setting typically uses 2 lines (variable + subroutine), so increment by 10 per setting
                    // But we need to find the highest N number
                    int maxN = 10;
                    foreach (var line in startGCode)
                    {
                        if (line.StartsWith("N") && int.TryParse(line.Substring(1).Split(' ')[0], out int nValue))
                        {
                            if (nValue > maxN) maxN = nValue;
                        }
                    }
                    line_num = maxN + 10; // Continue after last machine setting
                    processInfo.Add($"Added machine start settings ({startGCode.Length} lines, ending at N{maxN})");
                }
            }
            
            // Initialize first layer (layer 0) before movements start
            // Format: N70 V.E.GLOBAL[27] = 0, N80 L layer_sub.nc, then movements start at N100
            // But if machine settings were added, we need to start after them
            if (movementLines.Count > 0)
            {
                // If machine settings were added and we're past N70, use current line_num
                // Otherwise start at N70 for layer initialization
                if (line_num < 70)
                {
                    line_num = 70;
                }
                
                result.Add($"N{line_num} V.E.GLOBAL[27] = 0");
                line_num += 10;
                result.Add($"N{line_num} L layer_sub.nc");
                line_num += 10; // Continue from here for movements
                
                // Ensure movements start at a reasonable number (at least N100)
                if (line_num < 100)
                {
                    line_num = 100;
                }
                
                processInfo.Add($"Added first layer initialization (V.E.GLOBAL[27] = 0, layer_sub.nc), movements start at N{line_num}");
            }
            else
            {
                // If no movements, ensure line_num is at least 100 for consistency
                if (line_num < 100)
                {
                    line_num = 100;
                }
            }

            // Process movements with correct DXR format
            // Track last known values for X, Y, Z to ensure they are always present
            double lastX = 0.0, lastY = 0.0, lastZ = 0.0;
            // Track if A, B, C have ever been seen (to know if we should include them)
            bool hasA = false, hasB = false, hasC = false;
            double lastA = 0.0, lastB = -0.0, lastC = 0.0;
            int currentLayer = 1; // Start at 1 since layer 0 is already set
            bool isFirstLayer = false; // Already handled above
            string lastLayerType = ""; // Track last layer type for subroutine insertion
            
            for (int i = 0; i < movementLines.Count; i++)
            {
                // Check if this is a layer change
                if (layerChangeIndices.Contains(i))
                {
                    // Add layer change commands before the movement
                    result.Add($"N{line_num} V.E.GLOBAL[27] = {currentLayer}");
                    line_num += 10;
                    result.Add($"N{line_num} L layer_sub.nc");
                    line_num += 10;
                    
                    // Add wall_sub.nc only for first layer
                    if (isFirstLayer)
                    {
                        result.Add($"N{line_num} L wall_sub.nc");
                        line_num += 10;
                        isFirstLayer = false;
                    }
                    
                    currentLayer++;
                    lastLayerType = ""; // Reset layer type on layer change
                }
                
                string line = movementLines[i];
                double P1 = i < P1_list.Count ? P1_list[i] : 0.0;
                double F1 = i < F1_list.Count ? F1_list[i] : 1000.0;
                bool isRetract = i < isRetraction.Count ? isRetraction[i] : false;
                string layerType = i < layerTypes.Count ? layerTypes[i] : "";
                
                // Check if layer type changed and insert appropriate subroutine
                if (!string.IsNullOrEmpty(layerType) && layerType != lastLayerType)
                {
                    string subroutine = GetSubroutineForLayerType(layerType);
                    if (!string.IsNullOrEmpty(subroutine))
                    {
                        result.Add($"N{line_num} L {subroutine}");
                        line_num += 10;
                        processInfo?.Add($"Inserted {subroutine} for layer type: {layerType}");
                    }
                    lastLayerType = layerType;
                }

                // Extract coordinates using helper function
                double? xVal = TryMatchValue(rx, line);
                double? yVal = TryMatchValue(ry, line);
                double? zVal = TryMatchValue(rz, line);
                double? aVal = TryMatchValue(ra, line);
                double? bVal = TryMatchValue(rb, line);
                double? cVal = TryMatchValue(rc, line);

                // Update last known values if new values are present
                if (xVal.HasValue) lastX = xVal.Value;
                if (yVal.HasValue) lastY = yVal.Value;
                if (zVal.HasValue) lastZ = zVal.Value;
                if (aVal.HasValue) { lastA = aVal.Value; hasA = true; }
                if (bVal.HasValue) { lastB = bVal.Value; hasB = true; }
                if (cVal.HasValue) { lastC = cVal.Value; hasC = true; }

                // Build DXR line: X, Y, Z always present; A, B, C always present (default 0.0)
                // Format: N{line_num} G1 F{F1:F3} X{x:F3} Y{y:F3} Z{z:F3} A{a:F3} B{b:F3} C{c:F3} G91 XE=[{P1:F6}*P1] G90
                // Note: F comes AFTER G1 (at beginning), A/B/C always included (default 0.0 if not provided)
                var coordinateParts = new List<string>();
                coordinateParts.Add($"X{lastX:F3}");
                coordinateParts.Add($"Y{lastY:F3}");
                coordinateParts.Add($"Z{lastZ:F3}");
                
                // Always include A, B, C (default to 0.0 if not provided)
                // This ensures consistent format and allows for future non-planar printing
                double a = hasA ? lastA : 0.0;
                double b = hasB ? lastB : 0.0;
                double c = hasC ? lastC : 0.0;
                coordinateParts.Add($"A{a:F3}");
                coordinateParts.Add($"B{b:F3}");
                coordinateParts.Add($"C{c:F3}");
                
                string coordinates = string.Join(" ", coordinateParts);
                
                // Handle retraction without movement (special format)
                if (isRetract)
                {
                    // Retraction: G1 G91 XE=[{retraction}*P1] G90 F{feedrate}
                    // Note: For retraction, we use a feedrate without movement
                    double retractionFeedrate = F1 > 0 ? F1 : 30000.0; // Default 30000 mm/min for retraction
                    string dxrLine = $"N{line_num} G1 G91 XE=[{P1:F6}*P1] G90 F{retractionFeedrate:F3}";
                    result.Add(dxrLine);
                    processInfo?.Add($"Retraction command at N{line_num}: {P1:F6}mm");
                }
                else
                {
                    // Normal movement: F comes after G1 (at beginning), not at the end
                    string dxrLine = $"N{line_num} G1 F{F1:F3} {coordinates} G91 XE=[{P1:F6}*P1] G90";
                    result.Add(dxrLine);
                }

                line_num += 10;
            }

            int firstMovementLine = line_num - (movementLines.Count * 10);
            processInfo.Add($"Generated {movementLines.Count} DXR movement lines (starting at N{firstMovementLine})");

            return result;
        }

        /// <summary>
        /// Helper function to extract coordinate values from a line using regex
        /// Returns the parsed value or null if not found
        /// </summary>
        private static double? TryMatchValue(Regex r, string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;
                
            try
            {
                Match m = r.Match(line);
                if (m.Success && double.TryParse(m.Groups[1].Value, out double value))
                {
                    return value;
                }
            }
            catch
            {
                // Silently ignore parsing errors
            }
            return null;
        }

        /// <summary>
        /// Helper function to extract coordinate values from a line using regex (legacy method for compatibility)
        /// </summary>
        private static string TryMatch(Regex r, string line, string label)
        {
            double? value = TryMatchValue(r, line);
            return value.HasValue ? $"{label}{value.Value:F3}" : "";
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
        /// Detects layer changes in movement lines.
        /// Supports both planar (Z-value increases) and non-planar printing (finds point closest above start point).
        /// </summary>
        /// <param name="movementLines">List of movement lines</param>
        /// <param name="processInfo">List to add process information to</param>
        /// <returns>List of movement line indices where layer changes occur</returns>
        private static List<int> DetectLayerChanges(List<string> movementLines, List<string> processInfo)
        {
            var layerChangeIndices = new List<int>();
            if (movementLines == null || movementLines.Count < 2)
                return layerChangeIndices;

            // Regex for coordinates
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
            var ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)");
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");

            // Extract all coordinates
            var points = new List<(double x, double y, double z, int index)>();
            double lastX = 0, lastY = 0, lastZ = 0;

            for (int i = 0; i < movementLines.Count; i++)
            {
                string line = movementLines[i];
                double? xVal = TryMatchValue(rx, line);
                double? yVal = TryMatchValue(ry, line);
                double? zVal = TryMatchValue(rz, line);

                if (xVal.HasValue) lastX = xVal.Value;
                if (yVal.HasValue) lastY = yVal.Value;
                if (zVal.HasValue) lastZ = zVal.Value;

                points.Add((lastX, lastY, lastZ, i));
            }

            if (points.Count < 2)
                return layerChangeIndices;

            // Determine if printing is planar or non-planar
            bool isPlanar = IsPlanarPrinting(points);
            processInfo.Add(isPlanar ? "Detected planar printing (layer detection by Z changes)" : "Detected non-planar printing (layer detection by return to start area)");

            if (isPlanar)
            {
                // Planar: Detect layer changes by checking if after a Z jump, many points have similar Z height
                // This works for any layer height (1mm, 1.5mm, 2mm, 6mm, etc.)
                const double zTolerance = 0.1; // Tolerance for "same Z height" (0.1mm)
                const int minPointsInLayer = 5; // Minimum points with same Z to consider it a new layer
                const double minZJump = 0.2; // Minimum Z jump to consider (0.2mm)
                
                double previousZ = points[0].z;
                double lastLayerZ = points[0].z;
                int lookAheadWindow = Math.Min(20, points.Count / 10); // Look ahead 20 points or 10% of total
                
                for (int i = 1; i < points.Count - lookAheadWindow; i++)
                {
                    double currentZ = points[i].z;
                    double zDiff = currentZ - previousZ;
                    
                    // Check if there's a significant Z jump (potential layer change)
                    if (zDiff > minZJump && currentZ > lastLayerZ + minZJump)
                    {
                        // Look ahead to see if many points have similar Z height after this jump
                        int sameZCount = 0;
                        double targetZ = currentZ;
                        
                        for (int j = i; j < Math.Min(i + lookAheadWindow, points.Count); j++)
                        {
                            if (Math.Abs(points[j].z - targetZ) < zTolerance)
                            {
                                sameZCount++;
                            }
                        }
                        
                        // If many points have the same Z after the jump, it's a new layer
                        if (sameZCount >= minPointsInLayer)
                        {
                            // Find the start of the jump (where Z started to increase)
                            int jumpStartIndex = i;
                            for (int j = i - 1; j >= 0 && j >= i - 5; j--) // Check up to 5 points back
                            {
                                if (Math.Abs(points[j].z - previousZ) < zTolerance)
                                {
                                    jumpStartIndex = j + 1; // Jump starts at next point
                                    break;
                                }
                            }
                            
                            layerChangeIndices.Add(jumpStartIndex);
                            double layerHeight = targetZ - lastLayerZ;
                            processInfo.Add($"Layer change detected at movement {jumpStartIndex}: Z {lastLayerZ:F3} -> {targetZ:F3} (height: {layerHeight:F3}mm, {sameZCount} points at new Z)");
                            lastLayerZ = targetZ;
                            previousZ = targetZ;
                            i += sameZCount - 1; // Skip ahead to avoid detecting same layer multiple times
                            continue;
                        }
                    }
                    previousZ = currentZ;
                }
            }
            else
            {
                // Non-planar: Find points closest above start point (after initial Brim/Skirt)
                // Start point is the first point
                var startPoint = points[0];
                double startX = startPoint.x;
                double startY = startPoint.y;
                double startZ = startPoint.z;
                
                // Tolerance for "close to start point" in XY plane
                const double xyTolerance = 5.0; // 5mm tolerance
                
                // Find the first significant movement away from start (end of Brim/Skirt)
                int firstRealLayerStart = 0;
                for (int i = 1; i < points.Count; i++)
                {
                    double dist = Math.Sqrt(Math.Pow(points[i].x - startX, 2) + Math.Pow(points[i].y - startY, 2));
                    if (dist > xyTolerance * 2) // Moved significantly away from start
                    {
                        firstRealLayerStart = i;
                        break;
                    }
                }
                
                // Track the minimum Z above start point for each "cycle"
                double minZAboveStart = double.MaxValue;
                int lastLayerChangeIndex = firstRealLayerStart;
                
                for (int i = firstRealLayerStart; i < points.Count; i++)
                {
                    double dist = Math.Sqrt(Math.Pow(points[i].x - startX, 2) + Math.Pow(points[i].y - startY, 2));
                    double zDiff = points[i].z - startZ;
                    
                    // If we're close to start point in XY and above it in Z
                    if (dist < xyTolerance && zDiff > 0)
                    {
                        // This is a candidate for layer start
                        if (zDiff < minZAboveStart)
                        {
                            minZAboveStart = zDiff;
                            lastLayerChangeIndex = i;
                        }
                    }
                    else if (dist > xyTolerance)
                    {
                        // We've moved away from start - if we found a minimum, that was a layer change
                        if (minZAboveStart < double.MaxValue && lastLayerChangeIndex > 0)
                        {
                            // Only add if it's different from the last one we added
                            if (layerChangeIndices.Count == 0 || lastLayerChangeIndex != layerChangeIndices[layerChangeIndices.Count - 1])
                            {
                                layerChangeIndices.Add(lastLayerChangeIndex);
                                processInfo.Add($"Layer change detected at movement {lastLayerChangeIndex}: Return to start area (Z +{minZAboveStart:F3})");
                            }
                            minZAboveStart = double.MaxValue;
                        }
                    }
                }
                
                // Add final layer change if we ended near start
                if (minZAboveStart < double.MaxValue && lastLayerChangeIndex > 0)
                {
                    if (layerChangeIndices.Count == 0 || lastLayerChangeIndex != layerChangeIndices[layerChangeIndices.Count - 1])
                    {
                        layerChangeIndices.Add(lastLayerChangeIndex);
                    }
                }
            }

            return layerChangeIndices;
        }

        /// <summary>
        /// Determines if printing is planar (flat layers) or non-planar (continuous Z changes).
        /// Planar: Z values form distinct levels
        /// Non-planar: Z values change continuously
        /// </summary>
        private static bool IsPlanarPrinting(List<(double x, double y, double z, int index)> points)
        {
            if (points.Count < 10)
                return true; // Default to planar for small datasets

            // Calculate Z value variance
            var zValues = points.Select(p => p.z).ToList();
            double zMin = zValues.Min();
            double zMax = zValues.Max();
            double zRange = zMax - zMin;
            
            if (zRange < 0.1)
                return true; // All at same height = planar

            // Count how many times Z changes direction (up/down)
            int directionChanges = 0;
            bool? lastDirection = null; // true = up, false = down
            
            for (int i = 1; i < points.Count; i++)
            {
                double zDiff = points[i].z - points[i - 1].z;
                if (Math.Abs(zDiff) > 0.01) // Ignore tiny changes
                {
                    bool currentDirection = zDiff > 0;
                    if (lastDirection.HasValue && currentDirection != lastDirection.Value)
                    {
                        directionChanges++;
                    }
                    lastDirection = currentDirection;
                }
            }
            
            // Planar: Few direction changes (Z mostly increases in steps)
            // Non-planar: Many direction changes (Z goes up and down continuously)
            double changeRatio = (double)directionChanges / points.Count;
            
            // If more than 5% direction changes, it's likely non-planar
            return changeRatio < 0.05;
        }

        /// <summary>
        /// Gets the appropriate subroutine name for a given layer type.
        /// </summary>
        /// <param name="layerType">Layer type (SKIRT, SKIN, WALL, INFILL, etc.)</param>
        /// <returns>Subroutine filename or empty string if no match</returns>
        private static string GetSubroutineForLayerType(string layerType)
        {
            if (string.IsNullOrEmpty(layerType))
                return "";
                
            string typeUpper = layerType.ToUpper();
            
            if (typeUpper.Contains("SKIRT") || typeUpper.Contains("SUPPORT-INTERFACE"))
                return "L_skirt_sub.nc";
            else if (typeUpper.Contains("SKIN"))
                return "L_skin_sub.nc";
            else if (typeUpper.Contains("WALL") || typeUpper.Contains("BRIDGE"))
                return "L_wall_sub.nc";
            else if (typeUpper.Contains("INFILL"))
                return "L_infill_sub.nc";
            else if (typeUpper.Contains("RETRACT"))
                return "L_retract_sub.nc";
            else
                return ""; // Unknown type, no subroutine
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

