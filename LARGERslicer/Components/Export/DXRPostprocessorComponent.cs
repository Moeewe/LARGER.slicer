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
                    var parsedData = GCodeToDxr.Parse(gCodeInput, processInfo, machineSettings);
                    foreach (var w in parsedData.Warnings) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w); processInfo.Add("WARNING: " + w); }
                    
                    List<string> robotLines = parsedData.RobotLines;
                    List<double> P1_list = parsedData.ExtrusionAmounts;
                    List<double> F1_list = parsedData.PrintSpeeds;
                    List<string> layerTypes = parsedData.LayerTypes;
                    List<bool> isRetraction = parsedData.IsRetraction;
                    
                    processInfo.Add($"Parsed: {robotLines.Count} movements, {P1_list.Count} extrusions, {F1_list.Count} speeds");
                    if (layerTypes.Count > 0)
                    {
                        var uniqueTypes = layerTypes.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                        if (uniqueTypes.Count > 0)
                        {
                            processInfo.Add($"Detected layer types: {string.Join(", ", uniqueTypes)}");
                        }
                    }
                    int retractionCount = isRetraction.Count(r => r);
                    if (retractionCount > 0)
                    {
                        processInfo.Add($"Detected {retractionCount} retraction commands");
                    }

                    // Use extracted or provided machine settings
                    MachineSettings finalSettings = parsedData.ExtractedSettings ?? machineSettings;

                    // Process robot lines using the DXR conversion logic
                    var dxrLines = DXRHelper.ProcessRobotLinesToDXR(robotLines, P1_list, F1_list, processInfo, finalSettings, layerTypes, isRetraction);
                    if (dxrLines.Count == 0)
                    {
                        // no moves: a file with only the switch-off lines would look like a finished print
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No moves found in the G-code (G0, G1, G2, G3 with X, Y, Z). Nothing was generated.");
                        DA.SetDataList(0, result);
                        DA.SetDataList(1, processInfo);
                        return;
                    }
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The G-code could not be converted: " + innerEx.Message);
                    result.Clear();
                }
            }
            catch (Exception ex)
            {
                processInfo.Add($"Error during processing: {ex.Message}");
                processInfo.Add($"Stack trace: {ex.StackTrace}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The G-code could not be converted: " + ex.Message);
                result.Clear();
            }

            DA.SetDataList(0, result);
            DA.SetDataList(1, processInfo);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRPostprocessorIcon.png", this);
        public override Guid ComponentGuid => new Guid("B8E9A1F2-4C7D-4E5F-9A2B-3C8D9E0F1A2B");
    }
} 



