using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNC
{
    public class CutterSelectorComponent : GH_Component
    {
        public CutterSelectorComponent()
          : base("CNC - Cutter Selector", "CutterSel",
              "Selects a cutter from the database and outputs cutter information for use with CNC Program component. Supports automatic tool changes for different cutting passes.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Cutter Name", "Name", "Cutter name from database (e.g., 'Fräser 4') or tool position (11, 21, 31). Leave empty to list all available cutters.", GH_ParamAccess.item);
            pManager[0].Optional = true;
            pManager.AddNumberParameter("Spindle Speed", "RPM", "Optional: Override default spindle speed for this cutter (RPM)", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Cutter", "Cutter", "Cutter object with all specifications for CNC Program component", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Cutter information and specifications", GH_ParamAccess.item);
            pManager.AddTextParameter("Available Cutters", "Available", "List of all available cutters in database", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string cutterName = "";
            double? customSpindleSpeed = null;

            // Get inputs
            DA.GetData(0, ref cutterName);
            double tempRpm = 0.0;
            if (DA.GetData(1, ref tempRpm))
            {
                customSpindleSpeed = tempRpm;
            }

            // Load cutter database
            List<Cutter> cutters = Cutter.GetDefaultCutters();
            
            // Output available cutters list
            List<string> availableCutters = cutters.Select(c => $"{c.Name} (Tool {c.ToolPosition}, Ø{c.Diameter}mm, Max: {c.MaxCuttingDepth}mm)").ToList();
            DA.SetDataList(2, availableCutters);

            // Find selected cutter
            Cutter selectedCutter = null;
            if (!string.IsNullOrWhiteSpace(cutterName))
            {
                selectedCutter = Cutter.FindCutter(cutters, cutterName);
                if (selectedCutter == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Cutter '{cutterName}' not found in database. Available cutters: {string.Join(", ", cutters.Select(c => c.Name))}");
                    DA.SetData(0, null);
                    DA.SetData(1, "ERROR: Cutter not found. Check 'Available Cutters' output for list.");
                    return;
                }
            }
            else
            {
                // If no cutter specified, use first available or show error
                if (cutters.Count > 0)
                {
                    selectedCutter = cutters[0];
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        $"No cutter specified. Using default: {selectedCutter.Name}. Specify a cutter name to select a specific tool.");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No cutters available in database.");
                    DA.SetData(0, null);
                    DA.SetData(1, "ERROR: No cutters in database.");
                    return;
                }
            }

            // Create cutter object (with optional custom spindle speed)
            Cutter outputCutter = selectedCutter;
            if (customSpindleSpeed.HasValue && customSpindleSpeed.Value > 0)
            {
                // Note: Spindle speed is not stored in Cutter class, but can be passed separately
                // For now, we'll just output the cutter and note the custom speed in info
            }

            // Output cutter object
            DA.SetData(0, new GH_Cutter(outputCutter));

            // Output info
            string info = $"Cutter: {outputCutter.Name}\n" +
                         $"Tool Position: {outputCutter.ToolPosition}\n" +
                         $"Diameter: {outputCutter.Diameter} mm\n" +
                         $"Max Cutting Depth (through-cut): {outputCutter.MaxCuttingDepth} mm\n" +
                         $"Max Surface Depth: {outputCutter.MaxSurfaceDepth} mm\n" +
                         $"Description: {outputCutter.Description}";
            
            if (customSpindleSpeed.HasValue)
            {
                info += $"\nCustom Spindle Speed: {customSpindleSpeed.Value:F0} RPM";
            }

            DA.SetData(1, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCProgramIcon.png");
        public override Guid ComponentGuid => new Guid("F9E8D7C6-B5A4-3210-9876-543210FEDCBA");
    }
}

