using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using LARGERslicer.Types;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// DXR Generator - Converts robot path, extrusion, and speed data to DXR format.
    /// Use this component when you have individual path/extrusion/speed inputs.
    /// </summary>
    public class DXRGeneratorComponent : GH_Component
    {
        public DXRGeneratorComponent()
          : base("DXR Generator", "DXR Gen",
              "Generate DXR files from robot path, extrusion amounts, and print speeds. Accepts tree branches, lists, or single values.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Robot Path", "Path", "Robot movement data (tree branches, list, or single value)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Extrusion Amount", "Extrusion", "Material extrusion values (tree branches, list, or single value). Last value auto-removed.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Print Speed", "Speed", "Movement speed values in mm/min (tree branches, list, or single value). Last value auto-removed.", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Machine Settings", "Machine", "Printer configuration (connect Machine Settings component)", GH_ParamAccess.item);
            
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("DXR File", "DXR", "Complete DXR file content ready for export", GH_ParamAccess.list);
            pManager.AddTextParameter("Process Info", "Info", "Generation summary and statistics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MachineSettings machineSettings = null;

            var processInfo = new List<string>();
            var result = new List<string>();

            try
            {
                // Get inputs - support tree, list, or single value
                List<string> robotLines = ExtractRobotPath(DA, 0, processInfo);
                
                if (robotLines == null || robotLines.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Robot Path input is required and must contain at least one value.");
                    DA.SetDataList(0, result);
                    DA.SetDataList(1, processInfo);
                    return;
                }

                // Extract extrusion and speed values - support tree, list, or single value
                List<double> P1_list = ExtractNumberValues(DA, 1, processInfo, "Extrusion");
                List<double> F1_list = ExtractNumberValues(DA, 2, processInfo, "Speed");

                DA.GetData(3, ref machineSettings);

                processInfo.Add($"Processing: {robotLines.Count} robot path lines, P1={P1_list.Count} values, F1={F1_list.Count} values");

                // Remove last values (matching original behavior)
                if (P1_list.Count > 0)
                {
                    P1_list.RemoveAt(P1_list.Count - 1);
                    processInfo.Add($"Removed last P1 value. Count: {P1_list.Count}");
                }

                if (F1_list.Count > 0)
                {
                    F1_list.RemoveAt(F1_list.Count - 1);
                    processInfo.Add($"Removed last F1 value. Count: {F1_list.Count}");
                }

                // Validate counts BEFORE processing (will be validated again in DXRHelper, but early warning is helpful)
                // Note: Exact count matching will be checked after parsing robot lines (some lines might be invalid)
                if (P1_list.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Extrusion values provided. All movements will use P1=0.0 (no extrusion).");
                    processInfo.Add("WARNING: No Extrusion values provided - all movements will have P1=0.0");
                }

                if (F1_list.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Print Speed values provided. All movements will use F1=1000.0 mm/min (default speed).");
                    processInfo.Add("WARNING: No Print Speed values provided - all movements will have F1=1000.0 mm/min");
                }

                // Process robot lines using the DXR conversion logic
                var dxrLines = DXRHelper.ProcessRobotLinesToDXR(robotLines, P1_list, F1_list, processInfo, machineSettings);
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
                
                // Add footer comment with generator info
                string footerComment = DXRHelper.GenerateFooterComment();
                result.Add(footerComment);
                
                processInfo.Add($"Total DXR file lines: {result.Count}");
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

        /// <summary>
        /// Extracts robot path from tree input (trees can contain branches, lists, or single values)
        /// Handles tree, list, and single value inputs gracefully with exception handling
        /// </summary>
        private List<string> ExtractRobotPath(IGH_DataAccess DA, int index, List<string> processInfo)
        {
            var robotLines = new List<string>();

            // Check parameter access type first to avoid exceptions
            bool canUseTree = false;
            try
            {
                if (Params.Input != null && index < Params.Input.Count)
                {
                    var param = Params.Input[index];
                    canUseTree = (param.Access == GH_ParamAccess.tree);
                }
            }
            catch
            {
                // If we can't check, try tree first anyway
                canUseTree = true;
            }

            // Try GetDataTree() first if parameter supports it - wrap in try-catch because it throws if access mode doesn't match
            if (canUseTree)
            {
                try
                {
            GH_Structure<GH_String> robotLinesTree = null;
            if (DA.GetDataTree(index, out robotLinesTree) && robotLinesTree != null && robotLinesTree.PathCount > 0)
            {
                // Process all branches in the tree
                foreach (var branch in robotLinesTree.Branches)
                {
                    foreach (var item in branch)
                    {
                        if (item != null && !string.IsNullOrEmpty(item.Value))
                            robotLines.Add(item.Value);
                    }
                }
                processInfo.Add($"Extracted {robotLines.Count} lines from tree structure ({robotLinesTree.PathCount} branches)");
                return robotLines;
                    }
                }
                catch (Exception ex)
                {
                    // GetDataTree() failed - parameter might not be in tree mode at runtime, try list fallback
                    processInfo.Add($"Tree access failed: {ex.GetType().Name}, trying list fallback...");
                }
            }

            // Fallback: Try as list (for compatibility)
            try
            {
            var list = new List<GH_String>();
            if (DA.GetDataList(index, list) && list.Count > 0)
            {
                foreach (var item in list)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Value))
                        robotLines.Add(item.Value);
                }
                processInfo.Add($"Extracted {robotLines.Count} lines from list input");
                return robotLines;
                }
            }
            catch (Exception ex)
            {
                // List access failed, try single value
                processInfo.Add($"List access failed: {ex.GetType().Name}, trying single value...");
            }

            // Final fallback: Try as single value
            try
            {
                GH_String singleValue = null;
                if (DA.GetData(index, ref singleValue) && singleValue != null && !string.IsNullOrEmpty(singleValue.Value))
                {
                    robotLines.Add(singleValue.Value);
                    processInfo.Add($"Extracted 1 line from single value input");
                    return robotLines;
                }
            }
            catch (Exception ex)
            {
                // All access methods failed
                processInfo.Add($"Single value access failed: {ex.GetType().Name}");
            }

            return robotLines;
        }

        /// <summary>
        /// Extracts number values from tree input (trees can contain branches, lists, or single values)
        /// Handles tree, list, and single value inputs gracefully with exception handling
        /// </summary>
        private List<double> ExtractNumberValues(IGH_DataAccess DA, int index, List<string> processInfo, string valueName)
        {
            var values = new List<double>();

            // Check parameter access type first to avoid exceptions
            bool canUseTree = false;
            try
            {
                if (Params.Input != null && index < Params.Input.Count)
                {
                    var param = Params.Input[index];
                    canUseTree = (param.Access == GH_ParamAccess.tree);
                }
            }
            catch
            {
                // If we can't check, try tree first anyway
                canUseTree = true;
            }

            // Try GetDataTree() first if parameter supports it - wrap in try-catch because it throws if access mode doesn't match
            if (canUseTree)
            {
                try
                {
            GH_Structure<GH_Number> tree = null;
            if (DA.GetDataTree(index, out tree) && tree != null && tree.PathCount > 0)
            {
                // Process all branches in the tree
                foreach (var branch in tree.Branches)
                {
                    foreach (var item in branch)
                    {
                        if (item is GH_Number ghNumber)
                            values.Add(ghNumber.Value);
                    }
                }
                processInfo.Add($"Extracted {values.Count} {valueName} values from tree structure ({tree.PathCount} branches)");
                return values;
                    }
                }
                catch (Exception ex)
                {
                    // GetDataTree() failed - parameter might not be in tree mode at runtime, try list fallback
                    processInfo.Add($"Tree access failed for {valueName}: {ex.GetType().Name}, trying list fallback...");
                }
            }

            // Fallback: Try as list (for compatibility)
            try
            {
            var list = new List<GH_Number>();
            if (DA.GetDataList(index, list) && list.Count > 0)
            {
                foreach (var item in list)
                {
                    if (item != null)
                        values.Add(item.Value);
                }
                processInfo.Add($"Extracted {values.Count} {valueName} values from list input");
                return values;
                }
            }
            catch (Exception ex)
            {
                // List access failed, try single value
                processInfo.Add($"List access failed for {valueName}: {ex.GetType().Name}, trying single value...");
            }

            // Final fallback: Try as single value
            try
            {
                GH_Number singleValue = null;
                if (DA.GetData(index, ref singleValue) && singleValue != null)
                {
                    values.Add(singleValue.Value);
                    processInfo.Add($"Extracted 1 {valueName} value from single value input");
                    return values;
                }
            }
            catch (Exception ex)
            {
                // All access methods failed
                processInfo.Add($"Single value access failed for {valueName}: {ex.GetType().Name}");
            }

            // Optional parameter - return empty list if not provided
            return values;
        }

        private List<double> FlattenNumberTree(GH_Structure<GH_Number> tree)
        {
            var result = new List<double>();
            if (tree == null) return result;

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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRGeneratorIcon.png");
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-7890-ABCD-123456789012");
    }
}
