using System;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    public class MachineSettingsComponent : GH_Component
    {
        public MachineSettingsComponent()
          : base("Machine Settings", "Machine",
              "Configure printer settings: bed temperature, nozzle temperature, and cooling fan",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Bed Temperature", "Bed Temp", "Heated bed temperature in °C (0 = off)", GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Nozzle Temperature", "Nozzle Temp", "Extruder nozzle temperature in °C (0 = off)", GH_ParamAccess.item, 200.0);
            pManager.AddNumberParameter("Cooling Fan", "Cooling", "Cooling fan percentage 0-100% (0 = off)", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Machine Settings", "Settings", "Machine configuration for DXR output", GH_ParamAccess.item);
            pManager.AddTextParameter("Settings Info", "Info", "Current machine settings summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double bedTemp = 60.0;
            double nozzleTemp = 200.0;
            double cooling = 50.0;

            DA.GetData(0, ref bedTemp);
            DA.GetData(1, ref nozzleTemp);
            DA.GetData(2, ref cooling);

            var settings = new MachineSettings(bedTemp, nozzleTemp, cooling);
            
            var info = new System.Collections.Generic.List<string>
            {
                "=== Machine Settings ===",
                $"Bed Temperature: {settings.BedTemperature}°C {(settings.BedTemperature == 0 ? "(OFF)" : "")}",
                $"Nozzle Temperature: {settings.NozzleTemperature}°C {(settings.NozzleTemperature == 0 ? "(OFF)" : "")}",
                $"Cooling Fan: {settings.CoolingPercentage}% {(settings.CoolingPercentage == 0 ? "(OFF)" : "")}",
                "",
                "Note: All settings will automatically turn OFF after print completion"
            };

            // Add start G-code preview
            var startGCode = settings.GetStartGCode();
            if (startGCode.Length > 0)
            {
                info.Add("");
                info.Add("Start G-code will include:");
                foreach (var line in startGCode)
                {
                    info.Add($"  {line}");
                }
            }
            else
            {
                info.Add("");
                info.Add("No start G-code (all settings are OFF)");
            }

            DA.SetData(0, settings);
            DA.SetDataList(1, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("MachineSettingsIcon.png");
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    }
} 