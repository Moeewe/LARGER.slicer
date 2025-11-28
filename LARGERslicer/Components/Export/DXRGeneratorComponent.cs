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
              "Generate DXR files from robot path, extrusion amounts, and print speeds. Extracts branch {0;0;2} from robot path tree.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Robot Path", "Path", "Robot movement data (automatically extracts branch {0;0;2})", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Extrusion Amount", "Extrusion", "Material extrusion values (automatically flattened, last value auto-removed)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Print Speed", "Speed", "Movement speed values in mm/min (automatically flattened, last value auto-removed)", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Machine Settings", "Machine", "Printer configuration (connect Machine Settings component)", GH_ParamAccess.item);
            
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
                // Get tree inputs
                GH_Structure<GH_String> robotLinesTree = new GH_Structure<GH_String>();
                GH_Structure<GH_Number> P1_tree = new GH_Structure<GH_Number>();
                GH_Structure<GH_Number> F1_tree = new GH_Structure<GH_Number>();

                if (!DA.GetDataTree(0, out robotLinesTree))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Robot Path input is required.");
                    DA.SetDataList(0, result);
                    DA.SetDataList(1, processInfo);
                    return;
                }

                DA.GetDataTree(1, out P1_tree);
                DA.GetDataTree(2, out F1_tree);
                DA.GetData(3, ref machineSettings);

                // Extract branch {0;0;2} from robot path tree
                var targetPath = new GH_Path(0, 0, 2);
                List<string> robotLines = new List<string>();

                if (robotLinesTree.PathExists(targetPath))
                {
                    var branch = robotLinesTree.get_Branch(targetPath);
                    foreach (var item in branch)
                    {
                        if (item is GH_String ghString && !string.IsNullOrEmpty(ghString.Value))
                            robotLines.Add(ghString.Value);
                    }
                    processInfo.Add($"Extracted {robotLines.Count} lines from branch {{0;0;2}}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Branch {0;0;2} not found in Robot Path tree.");
                    processInfo.Add("ERROR: Required branch {0;0;2} not found in input data tree");
                    DA.SetDataList(0, result);
                    DA.SetDataList(1, processInfo);
                    return;
                }

                // Flatten extrusion and speed trees
                List<double> P1_list = FlattenNumberTree(P1_tree);
                List<double> F1_list = FlattenNumberTree(F1_tree);

                processInfo.Add($"Flattened: P1={P1_list.Count} values, F1={F1_list.Count} values");

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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRPostprocessorIcon.png");
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-7890-ABCD-123456789012");
    }
}

