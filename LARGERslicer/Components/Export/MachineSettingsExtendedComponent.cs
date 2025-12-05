using System;
using Grasshopper.Kernel;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    public class MachineSettingsExtendedComponent : GH_Component
    {
        public MachineSettingsExtendedComponent()
          : base("Machine Settings Extended", "Machine Ext",
              "Configure advanced multi-zone printer settings: 4 bed zones, 4 extruder zones (Extended Mode - V.E.GLOBAL_* format)",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Heated Bed: ONE global temperature + 4 zone ON/OFF controls
            pManager.AddNumberParameter("Bed Temperature", "Bed Temp", "Global bed temperature in °C for all zones (V.P.VAR_heatbedtemp). All 4 zones receive this same temperature.", GH_ParamAccess.item, 60.0);
            pManager.AddBooleanParameter("Bed Zone 1", "BZ1", "Enable/disable bed zone 1 (V.E.GLOBAL_BOOL[72] = TRUE/FALSE)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Bed Zone 2", "BZ2", "Enable/disable bed zone 2 (V.E.GLOBAL_BOOL[74] = TRUE/FALSE)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Bed Zone 3", "BZ3", "Enable/disable bed zone 3 (V.E.GLOBAL_BOOL[76] = TRUE/FALSE)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Bed Zone 4", "BZ4", "Enable/disable bed zone 4 (V.E.GLOBAL_BOOL[78] = TRUE/FALSE)", GH_ParamAccess.item, false);
            
            // Extruder Zones (4 zones: Filling always 45°C, Heating 1, Heating 2, Nozzle)
            // Filling Zone: Always 45°C (cooling), no input needed
            pManager.AddNumberParameter("Extruder Zone 1 Temp", "EZ1 Temp", "Heating extruder zone 1 temperature in °C (V.E.GLOBAL_BOOL[24], V.E.GLOBAL[55]). Standard: 220°C. Set to 0 to disable.", GH_ParamAccess.item, 220.0);
            pManager.AddNumberParameter("Extruder Zone 2 Temp", "EZ2 Temp", "Heating extruder zone 2 temperature in °C (V.E.GLOBAL_BOOL[26], V.E.GLOBAL[57]). Standard: 225°C. Set to 0 to disable.", GH_ParamAccess.item, 225.0);
            pManager.AddNumberParameter("Nozzle Zone Temp", "NZ Temp", "Heating nozzle zone temperature in °C (V.E.GLOBAL_BOOL[40], V.E.GLOBAL[71]). Standard: 230°C. Set to 0 to disable.", GH_ParamAccess.item, 230.0);
            
            // Fan settings - Always enabled if speed > 0
            pManager.AddNumberParameter("Fan Speed", "Fan Speed", "Fan speed 0-255 (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[3] for Extruder 1). Set to 0 to disable.", GH_ParamAccess.item, 80.0);
            
            // Make all inputs optional
            for (int i = 0; i < pManager.ParamCount; i++)
            {
                pManager[i].Optional = true;
            }
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Machine Settings", "Settings", "Machine configuration for DXR output (Extended Mode)", GH_ParamAccess.item);
            pManager.AddTextParameter("Settings Info", "Info", "Current machine settings summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Heated Bed: ONE global temperature + 4 zone ON/OFF controls
            double bedTemperature = 60.0;
            bool bedZone1Enabled = false;
            bool bedZone2Enabled = false;
            bool bedZone3Enabled = false;
            bool bedZone4Enabled = false;
            
            // Extruder Zones (4 zones)
            // Filling Zone: Always 45°C (cooling), always enabled
            double fillingZoneTemp = 45.0;
            double extruderZone1Temp = 220.0;  // Standard: 220°C
            double extruderZone2Temp = 225.0;  // Standard: 225°C
            double nozzleZoneTemp = 230.0;     // Standard: 230°C
            
            // Fan settings
            double fanSpeed = 80.0;

            // Read inputs
            // Bed: global temperature + zone ON/OFF
            DA.GetData(0, ref bedTemperature);
            DA.GetData(1, ref bedZone1Enabled);
            DA.GetData(2, ref bedZone2Enabled);
            DA.GetData(3, ref bedZone3Enabled);
            DA.GetData(4, ref bedZone4Enabled);
            
            // Extruder zones (temperatures only, Filling always 45°C)
            DA.GetData(5, ref extruderZone1Temp);
            DA.GetData(6, ref extruderZone2Temp);
            DA.GetData(7, ref nozzleZoneTemp);
            
            // Fan (speed only)
            DA.GetData(8, ref fanSpeed);
            
            // Filling Zone: Always enabled at 45°C
            bool fillingZoneEnabled = true;
            
            // Extruder zones: Enabled if temperature > 0
            bool extruderZone1Enabled = extruderZone1Temp > 0;
            bool extruderZone2Enabled = extruderZone2Temp > 0;
            bool nozzleZoneEnabled = nozzleZoneTemp > 0;
            
            // Fan: Enabled if speed > 0
            bool fanEnabled = fanSpeed > 0;

            // Create advanced multi-zone settings
            // Use default values for simple mode params (not used in advanced mode)
            // Bed zones all get the same global temperature
            var settings = new MachineSettings(
                bedTemp: bedTemperature, nozzleTemp: 0, cooling: 0,
                useAdvancedFormat: true,
                bedZone1Enabled: bedZone1Enabled, bedZone1Temp: bedTemperature,
                bedZone2Enabled: bedZone2Enabled, bedZone2Temp: bedTemperature,
                bedZone3Enabled: bedZone3Enabled, bedZone3Temp: bedTemperature,
                bedZone4Enabled: bedZone4Enabled, bedZone4Temp: bedTemperature,
                fillingZoneEnabled: fillingZoneEnabled, fillingZoneTemp: fillingZoneTemp,
                extruderZone1Enabled: extruderZone1Enabled, extruderZone1Temp: extruderZone1Temp,
                extruderZone2Enabled: extruderZone2Enabled, extruderZone2Temp: extruderZone2Temp,
                nozzleZoneEnabled: nozzleZoneEnabled, nozzleZoneTemp: nozzleZoneTemp,
                fanEnabled: fanEnabled, fanSpeed: fanSpeed
            );
            
            // Generate info output
            var info = new System.Collections.Generic.List<string>
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("MachineSettingsExtendedIcon.png");
        public override Guid ComponentGuid => new Guid("B1C2D3E4-F5A6-7890-BCDE-F123456789AB");
    }
}

