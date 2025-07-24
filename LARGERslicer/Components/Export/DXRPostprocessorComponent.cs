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
    public class DXRPostprocessorComponent : GH_Component
    {
        public DXRPostprocessorComponent()
          : base("DXR Generator", "DXR",
              "Generate DXR files from robot movements with machine settings and print parameters",
              "LARGERslicer", "Export")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Robot Path", "Path", "Robot movement data (automatically extracts branch {0;0;2})", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Extrusion Amount", "Extrusion", "Material extrusion values (automatically flattened, last value auto-removed)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Print Speed", "Speed", "Movement speed values (automatically flattened, last value auto-removed)", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Machine Settings", "Machine", "Printer configuration (connect Machine Settings component)", GH_ParamAccess.item);
            
            // Make Machine Settings optional
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("DXR File", "DXR", "Complete DXR file content ready for export", GH_ParamAccess.list);
            pManager.AddTextParameter("Process Info", "Info", "Generation summary and statistics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var robotLinesTree = new GH_Structure<GH_String>();
            var P1_tree = new GH_Structure<GH_Number>();
            var F1_tree = new GH_Structure<GH_Number>();
            MachineSettings machineSettings = null;

            if (!DA.GetDataTree(0, out robotLinesTree)) return;
            if (!DA.GetDataTree(1, out P1_tree)) return;
            if (!DA.GetDataTree(2, out F1_tree)) return;
            DA.GetData(3, ref machineSettings); // Optional parameter

            var processInfo = new List<string>();
            var result = new List<string>();

            try
            {
                // Automatically extract branch {0;0;2}
                var targetPath = new GH_Path(0, 0, 2);
                var robotLines = new List<string>();
                
                if (robotLinesTree.PathExists(targetPath))
                {
                    var branch = robotLinesTree.get_Branch(targetPath);
                    foreach (var item in branch)
                    {
                        if (item is GH_String ghString && ghString.Value != null)
                            robotLines.Add(ghString.Value);
                    }
                    processInfo.Add($"Successfully extracted {robotLines.Count} lines from branch {{0;0;2}}");
                }
                else
                {
                    processInfo.Add("ERROR: Branch {0;0;2} not found in input data tree");
                    DA.SetDataList(0, result);
                    DA.SetDataList(1, processInfo);
                    return;
                }

                // Flatten P1 and F1 trees to lists
                var P1_list = FlattenNumberTree(P1_tree);
                var F1_list = FlattenNumberTree(F1_tree);
                
                processInfo.Add($"Flattened P1 tree to {P1_list.Count} values");
                processInfo.Add($"Flattened F1 tree to {F1_list.Count} values");

                // Automatically cull last values (-1) from P1 and F1 lists
                if (P1_list.Count > 0)
                {
                    P1_list.RemoveAt(P1_list.Count - 1);
                    processInfo.Add($"Removed last value from P1 list. New count: {P1_list.Count}");
                }

                if (F1_list.Count > 0)
                {
                    F1_list.RemoveAt(F1_list.Count - 1);
                    processInfo.Add($"Removed last value from F1 list. New count: {F1_list.Count}");
                }

                // Process robot lines using the DXR conversion logic (includes machine settings)
                var dxrLines = ProcessRobotLinesToDXR(robotLines, P1_list, F1_list, processInfo, machineSettings);
                result.AddRange(dxrLines);
                
                // Add machine end settings (always turns everything off)
                string[] gCodeEnd;
                if (machineSettings != null)
                {
                    gCodeEnd = machineSettings.GetEndGCode();
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
                processInfo.Add($"Total DXR file lines: {result.Count}");
            }
            catch (Exception ex)
            {
                processInfo.Add($"Error during processing: {ex.Message}");
                result.Clear();
            }

            DA.SetDataList(0, result);
            DA.SetDataList(1, processInfo);
        }

        private List<string> ProcessRobotLinesToDXR(List<string> robotLines, List<double> P1_list, List<double> F1_list, List<string> processInfo, MachineSettings machineSettings)
        {
            var result = new List<string>();
            var header = new List<string>();
            var X_vals = new List<double>();
            var Y_vals = new List<double>();
            var Z_vals = new List<double>();

            int line_num; // Will be set later
            var movementLines = new List<string>();

            // Regex for coordinates and angles
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
            var ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)");
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");
            var ra = new Regex(@"A\s*([-+]?[0-9]*\.?[0-9]+)");
            var rb = new Regex(@"B\s*([-+]?[0-9]*\.?[0-9]+)");
            var rc = new Regex(@"C\s*([-+]?[0-9]*\.?[0-9]+)");

            // Collect valid movement lines
            foreach (string rawLine in robotLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || !line.Contains("PTP"))
                    continue;

                Match mx = rx.Match(line);
                Match my = ry.Match(line);
                Match mz = rz.Match(line);

                if (mx.Success) X_vals.Add(double.Parse(mx.Groups[1].Value));
                if (my.Success) Y_vals.Add(double.Parse(my.Groups[1].Value));
                if (mz.Success) Z_vals.Add(double.Parse(mz.Groups[1].Value));

                if (mx.Success || my.Success || mz.Success)
                    movementLines.Add(line);
            }

            // Clip P1 and F1 to match movement count
            int movement_count = movementLines.Count;
            P1_list = P1_list.GetRange(0, Math.Min(P1_list.Count, movement_count));
            F1_list = F1_list.GetRange(0, Math.Min(F1_list.Count, movement_count));

            processInfo.Add($"Found {movement_count} valid movement lines");
            processInfo.Add($"P1 values used: {P1_list.Count}");
            processInfo.Add($"F1 values used: {F1_list.Count}");

            // Calculate bounds
            double xmin = X_vals.Count > 0 ? Min(X_vals) : 0;
            double xmax = X_vals.Count > 0 ? Max(X_vals) : 0;
            double ymin = Y_vals.Count > 0 ? Min(Y_vals) : 0;
            double ymax = Y_vals.Count > 0 ? Max(Y_vals) : 0;
            double zmin = Z_vals.Count > 0 ? Min(Z_vals) : 0;
            double zmax = Z_vals.Count > 0 ? Max(Z_vals) : 0;

            // Calculate additional statistics
            int layerCount = Z_vals.Count > 0 ? Z_vals.Distinct().Count() : 0;
            double totalExtrusion = P1_list.Sum();
            double estimatedTimeSeconds = CalculateEstimatedPrintTime(movementLines, F1_list);
            
            processInfo.Add($"Calculated layers: {layerCount}");
            processInfo.Add($"Total extrusion: {totalExtrusion:F3}");
            processInfo.Add($"Estimated print time: {estimatedTimeSeconds:F0} seconds ({estimatedTimeSeconds/60:F1} minutes)");

            // Header
            header.Add($";ProgRunTimeTotal =[{estimatedTimeSeconds:F0}]");
            header.Add(";machine_type =[DXR.KUKA]");
            header.Add(";post_processor_version =[V1.0.3.17]");
            header.Add(";1 SD.ACT.GEN.DESC.NAME =\"DEFAULT\"");
            header.Add($";number of rows in org. file =[{robotLines.Count}]");
            header.Add($";number of movement rows = [{movement_count}]");
            header.Add($";number of layers =[{layerCount}]");
            header.Add($";Xmin = [{xmin:F3}]");
            header.Add($";Xmax = [{xmax:F3}]");
            header.Add($";Ymin = [{ymin:F3}]");
            header.Add($";Ymax = [{ymax:F3}]");
            header.Add($";Zmin = [{zmin:F3}]");
            header.Add($";Zmax = [{zmax:F3}]");
            header.Add($";Eges = IC[{totalExtrusion:F3}]");
            header.Add("; config end");
            header.Add(";==================================");

            result.AddRange(header);

            // Add machine settings after header
            line_num = 10; // Start with N10
            if (machineSettings != null)
            {
                // Add G90 command first
                result.Add($"N{line_num} G90");
                line_num += 10;

                // Add machine start settings
                if (machineSettings.CoolingPercentage > 0)
                {
                    result.Add($"N{line_num} V.P.VAR_fan = {machineSettings.CoolingPercentage:F0}");
                    result.Add($"N{line_num + 1} L fan_sub.nc");
                    line_num += 10;
                }

                if (machineSettings.NozzleTemperature > 0)
                {
                    result.Add($"N{line_num} V.P.VAR_extrudertemp = {machineSettings.NozzleTemperature:F0}");
                    result.Add($"N{line_num + 1} L extruderTemp_sub.nc");
                    line_num += 10;
                }

                if (machineSettings.BedTemperature > 0)
                {
                    result.Add($"N{line_num} V.P.VAR_heatbedtemp = {machineSettings.BedTemperature:F0}");
                    result.Add($"N{line_num + 1} L heatbedTemp_sub.nc");
                    line_num += 10;
                }

                // Add layer and wall setup commands
                result.Add($"N{line_num} V.E.GLOBAL[27] = 1");
                result.Add($"N{line_num + 1} L layer_sub.nc");
                line_num += 10;
                result.Add($"N{line_num + 1} L wall_sub.nc");
                line_num += 10;

                processInfo.Add($"Added machine start settings (Bed: {machineSettings.BedTemperature}°C, Nozzle: {machineSettings.NozzleTemperature}°C, Cooling: {machineSettings.CoolingPercentage}%)");
            }
            else
            {
                processInfo.Add("No machine settings provided - using default configuration");
            }
            for (int i = 0; i < movementLines.Count; i++)
            {
                string line = movementLines[i];
                string X = TryMatch(rx, line, "X");
                string Y = TryMatch(ry, line, "Y");
                string Z = TryMatch(rz, line, "Z");
                string A = TryMatch(ra, line, "A");
                string B = TryMatch(rb, line, "B");
                string C = TryMatch(rc, line, "C");

                double p1 = i < P1_list.Count ? P1_list[i] : 0.0;
                double f1 = i < F1_list.Count ? F1_list[i] : 1000.0;

                string newLine = $"N{line_num} G1 F{f1:F3} {X} {Y} {Z} {A} {B} {C} G91 XE=[{p1:F6}*P1] G90";
                result.Add(newLine.Trim());

                line_num += 10;
            }

            return result;
        }

        // Helper functions
        private double Min(List<double> values)
        {
            double min = double.MaxValue;
            foreach (double v in values)
                if (v < min) min = v;
            return min;
        }

        private double Max(List<double> values)
        {
            double max = double.MinValue;
            foreach (double v in values)
                if (v > max) max = v;
            return max;
        }

        private string TryMatch(Regex r, string line, string label)
        {
            Match m = r.Match(line);
            return m.Success ? $"{label}{double.Parse(m.Groups[1].Value):F3}" : "";
        }

        private List<double> FlattenNumberTree(GH_Structure<GH_Number> tree)
        {
            var result = new List<double>();
            
            foreach (var path in tree.Paths)
            {
                var branch = tree.get_Branch(path);
                foreach (var item in branch)
                {
                    if (item is GH_Number ghNumber)
                        result.Add(ghNumber.Value);
                }
            }
            
            return result;
        }

        private double CalculateEstimatedPrintTime(List<string> movementLines, List<double> F1_list)
        {
            if (movementLines.Count < 2 || F1_list.Count == 0)
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
                
                // Extract coordinates
                double x = 0, y = 0, z = 0;
                var mx = rx.Match(line);
                var my = ry.Match(line);
                var mz = rz.Match(line);
                
                if (mx.Success) x = double.Parse(mx.Groups[1].Value);
                if (my.Success) y = double.Parse(my.Groups[1].Value);
                if (mz.Success) z = double.Parse(mz.Groups[1].Value);

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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRPostprocessorIcon.png");
        public override Guid ComponentGuid => new Guid("B8E9A1F2-4C7D-4E5F-9A2B-3C8D9E0F1A2B");
    }
} 