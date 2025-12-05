using System;

namespace LARGERslicer.Types
{
    /// <summary>
    /// Machine settings for DXR output. Supports both simplified (V.P.VAR_*) and advanced (V.E.GLOBAL_*) formats.
    /// </summary>
    public class MachineSettings
    {
        // Simplified settings (backward compatible)
        public double BedTemperature { get; set; }
        public double NozzleTemperature { get; set; }
        public double CoolingPercentage { get; set; }

        // Advanced multi-zone settings (optional)
        public bool UseAdvancedFormat { get; set; } = false; // If true, use V.E.GLOBAL_* format
        
        // Printbed zones (1-4)
        public bool BedZone1Enabled { get; set; } = false;
        public double BedZone1Temperature { get; set; } = 0;
        public bool BedZone2Enabled { get; set; } = false;
        public double BedZone2Temperature { get; set; } = 0;
        public bool BedZone3Enabled { get; set; } = false;
        public double BedZone3Temperature { get; set; } = 0;
        public bool BedZone4Enabled { get; set; } = false;
        public double BedZone4Temperature { get; set; } = 0;

        // Extruder zones
        public bool FillingZoneCoolingEnabled { get; set; } = false;
        public double FillingZoneTemperature { get; set; } = 30;
        public bool ExtruderZone1Enabled { get; set; } = false;
        public double ExtruderZone1Temperature { get; set; } = 180;
        public bool ExtruderZone2Enabled { get; set; } = false;
        public double ExtruderZone2Temperature { get; set; } = 180;
        public bool NozzleZoneEnabled { get; set; } = false;
        public double NozzleZoneTemperature { get; set; } = 180;

        // Fan settings (advanced format)
        public bool FanEnabled { get; set; } = false;
        public double FanSpeed { get; set; } = 80; // 0-255 or percentage depending on format

        public MachineSettings(double bedTemp, double nozzleTemp, double cooling)
        {
            BedTemperature = Math.Max(0, bedTemp);
            NozzleTemperature = Math.Max(0, nozzleTemp);
            CoolingPercentage = Math.Max(0, Math.Min(100, cooling));
            
            // Auto-enable and set bed zones (all 4 zones get the same temperature)
            if (bedTemp > 0)
            {
                BedZone1Enabled = true;
                BedZone1Temperature = bedTemp;
                BedZone2Enabled = true;
                BedZone2Temperature = bedTemp;
                BedZone3Enabled = true;
                BedZone3Temperature = bedTemp;
                BedZone4Enabled = true;
                BedZone4Temperature = bedTemp;
            }
            
            // Auto-enable and set extruder zones with temperature gradient
            // Filling Zone: Always 45°C (cooling)
            // Zone 1: Nozzle - 10°C
            // Zone 2: Nozzle - 5°C
            // Nozzle Zone: The specified nozzle temperature
            if (nozzleTemp > 0)
            {
                // Filling Zone: Always 45°C for cooling
                FillingZoneCoolingEnabled = true;
                FillingZoneTemperature = 45.0;
                
                // Zone 1: Nozzle - 10°C (physically above Zone 2)
                ExtruderZone1Enabled = true;
                ExtruderZone1Temperature = Math.Max(0, nozzleTemp - 10.0);
                
                // Zone 2: Nozzle - 5°C (physically above Nozzle)
                ExtruderZone2Enabled = true;
                ExtruderZone2Temperature = Math.Max(0, nozzleTemp - 5.0);
                
                // Nozzle Zone: The specified temperature
                NozzleZoneEnabled = true;
                NozzleZoneTemperature = nozzleTemp;
            }
            
            // Fan settings
            if (cooling > 0)
            {
                FanEnabled = true;
                FanSpeed = cooling;
            }
        }

        /// <summary>
        /// Advanced constructor for multi-zone settings
        /// </summary>
        public MachineSettings(
            double bedTemp, double nozzleTemp, double cooling,
            bool useAdvancedFormat = false,
            bool bedZone1Enabled = false, double bedZone1Temp = 0,
            bool bedZone2Enabled = false, double bedZone2Temp = 0,
            bool bedZone3Enabled = false, double bedZone3Temp = 0,
            bool bedZone4Enabled = false, double bedZone4Temp = 0,
            bool fillingZoneEnabled = false, double fillingZoneTemp = 30,
            bool extruderZone1Enabled = false, double extruderZone1Temp = 180,
            bool extruderZone2Enabled = false, double extruderZone2Temp = 180,
            bool nozzleZoneEnabled = false, double nozzleZoneTemp = 180,
            bool fanEnabled = false, double fanSpeed = 80)
        {
            BedTemperature = Math.Max(0, bedTemp);
            NozzleTemperature = Math.Max(0, nozzleTemp);
            CoolingPercentage = Math.Max(0, Math.Min(100, cooling));
            
            UseAdvancedFormat = useAdvancedFormat;
            BedZone1Enabled = bedZone1Enabled;
            BedZone1Temperature = Math.Max(0, bedZone1Temp);
            BedZone2Enabled = bedZone2Enabled;
            BedZone2Temperature = Math.Max(0, bedZone2Temp);
            BedZone3Enabled = bedZone3Enabled;
            BedZone3Temperature = Math.Max(0, bedZone3Temp);
            BedZone4Enabled = bedZone4Enabled;
            BedZone4Temperature = Math.Max(0, bedZone4Temp);
            FillingZoneCoolingEnabled = fillingZoneEnabled;
            FillingZoneTemperature = Math.Max(0, fillingZoneTemp);
            ExtruderZone1Enabled = extruderZone1Enabled;
            ExtruderZone1Temperature = Math.Max(0, extruderZone1Temp);
            ExtruderZone2Enabled = extruderZone2Enabled;
            ExtruderZone2Temperature = Math.Max(0, extruderZone2Temp);
            NozzleZoneEnabled = nozzleZoneEnabled;
            NozzleZoneTemperature = Math.Max(0, nozzleZoneTemp);
            FanEnabled = fanEnabled;
            FanSpeed = Math.Max(0, Math.Min(255, fanSpeed));
        }

        public override string ToString()
        {
            if (UseAdvancedFormat)
            {
                return $"Advanced Format: Bed Zones={BedZone1Enabled}|{BedZone2Enabled}|{BedZone3Enabled}|{BedZone4Enabled}, " +
                       $"Extruder Zones={ExtruderZone1Enabled}|{ExtruderZone2Enabled}|{NozzleZoneEnabled}, " +
                       $"Fan={FanEnabled}@{FanSpeed}";
            }
            return $"Bed: {BedTemperature}°C, Nozzle: {NozzleTemperature}°C, Cooling: {CoolingPercentage}%";
        }

        /// <summary>
        /// Generates start G-code for machine initialization
        /// Uses sequential N-numbers starting from N10
        /// Supports both simplified (V.P.VAR_*) and advanced (V.E.GLOBAL_*) formats
        /// Based on technician specifications:
        /// - Simple Mode: V.P.VAR_* sets global temperatures, then zones are activated
        /// - Extended Mode: Individual V.E.GLOBAL[] temperatures, then zones are activated
        /// - V.E.GLOBAL_BOOL[1] = 1 must be set at the end for value acceptance
        /// </summary>
        public string[] GetStartGCode()
        {
            var startCode = new System.Collections.Generic.List<string>();
            int lineNum = 10; // Start at N10 after header
            
            if (UseAdvancedFormat)
            {
                // Extended Mode: Individual temperature control for each zone
                // NO V.P.VAR_* commands - only V.E.GLOBAL[] for individual control
                
                // Extruder zones: Set temperatures first (without BOOL activation yet)
                if (FillingZoneCoolingEnabled && FillingZoneTemperature > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[41] = {FillingZoneTemperature:F0}");
                    lineNum += 10;
                }
                if (ExtruderZone1Enabled && ExtruderZone1Temperature > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[55] = {ExtruderZone1Temperature:F0}");
                    lineNum += 10;
                }
                if (ExtruderZone2Enabled && ExtruderZone2Temperature > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[57] = {ExtruderZone2Temperature:F0}");
                    lineNum += 10;
                }
                if (NozzleZoneEnabled && NozzleZoneTemperature > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[71] = {NozzleZoneTemperature:F0}");
                    lineNum += 10;
                }
                
                // Extruder zones: Activate zones (BOOL after temperature setting)
                if (ExtruderZone1Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[24] = TRUE");
                    lineNum += 10;
                }
                if (ExtruderZone2Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[26] = TRUE");
                    lineNum += 10;
                }
                if (NozzleZoneEnabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[40] = TRUE");
                    lineNum += 10;
                }
                // Note: V.E.GLOBAL_BOOL[44] for Filling Zone is NOT needed (handled by fan_sub)
                
                // Fan: Direct setting (NO V.P.VAR_fan in extended mode)
                if (FanEnabled && FanSpeed > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[3] = {FanSpeed:F0}");
                    lineNum += 10;
                }
                // Note: V.E.GLOBAL_BOOL[44] for Fan is NOT needed (already set in fan_sub if V.P.VAR_fan was used)
                
                // Printbed: Set global temperature first (if any zone is enabled)
                if ((BedZone1Enabled || BedZone2Enabled || BedZone3Enabled || BedZone4Enabled) && 
                    (BedTemperature > 0 || BedZone1Temperature > 0))
                {
                    double bedTemp = BedTemperature > 0 ? BedTemperature : BedZone1Temperature;
                    startCode.Add($"N{lineNum} V.P.VAR_heatbedtemp = {bedTemp:F0}");
                    lineNum += 10;
                    startCode.Add($"N{lineNum} L heatbedTemp_sub.nc");
                    lineNum += 10;
                }
                
                // Printbed zones: Activate zones (BOOL after global temperature setting)
                if (BedZone1Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[72] = TRUE");
                    lineNum += 10;
                }
                if (BedZone2Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[74] = TRUE");
                    lineNum += 10;
                }
                if (BedZone3Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[76] = TRUE");
                    lineNum += 10;
                }
                if (BedZone4Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[78] = TRUE");
                    lineNum += 10;
                }
            }
            else
            {
                // Simple Mode: V.P.VAR_* sets global temperatures, then zones are activated
                
                // 1. Set bed temperature GLOBAL (all 4 zones get the same temperature)
                if (BedTemperature > 0 || (BedZone1Enabled && BedZone1Temperature > 0))
                {
                    double bedTemp = BedTemperature > 0 ? BedTemperature : BedZone1Temperature;
                    startCode.Add($"N{lineNum} V.P.VAR_heatbedtemp = {bedTemp:F0}");
                    lineNum += 10;
                    startCode.Add($"N{lineNum} L heatbedTemp_sub.nc");
                    lineNum += 10;
                }
                
                // 2. Activate bed zones (after global temperature is set)
                if (BedZone1Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[72] = TRUE");
                    lineNum += 10;
                }
                if (BedZone2Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[74] = TRUE");
                    lineNum += 10;
                }
                if (BedZone3Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[76] = TRUE");
                    lineNum += 10;
                }
                if (BedZone4Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[78] = TRUE");
                    lineNum += 10;
                }
                
                // 3. Set nozzle temperature GLOBAL (all extruder zones except Filling Zone)
                if (NozzleTemperature > 0 || (NozzleZoneEnabled && NozzleZoneTemperature > 0))
                {
                    double nozzleTemp = NozzleTemperature > 0 ? NozzleTemperature : NozzleZoneTemperature;
                    startCode.Add($"N{lineNum} V.P.VAR_extrudertemp = {nozzleTemp:F0}");
                    lineNum += 10;
                    startCode.Add($"N{lineNum} L extruderTemp_sub.nc");
                    lineNum += 10;
                }
                
                // 4. Set Filling Zone temperature separately (if enabled and different from default)
                // V.P.VAR_extrudertemp does NOT set the Filling Zone, so we need V.E.GLOBAL[41]
                if (FillingZoneCoolingEnabled && FillingZoneTemperature > 0)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL[41] = {FillingZoneTemperature:F0}");
                    lineNum += 10;
                }
                
                // 5. Activate extruder zones (after temperatures are set)
                if (ExtruderZone1Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[24] = TRUE");
                    lineNum += 10;
                }
                if (ExtruderZone2Enabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[26] = TRUE");
                    lineNum += 10;
                }
                if (NozzleZoneEnabled)
                {
                    startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[40] = TRUE");
                    lineNum += 10;
                }
                // Note: V.E.GLOBAL_BOOL[44] for Filling Zone is NOT needed (handled by fan_sub)
                
                // 6. Set fan speed
                if (CoolingPercentage > 0 || (FanEnabled && FanSpeed > 0))
                {
                    double fanValue = CoolingPercentage > 0 ? CoolingPercentage : FanSpeed;
                    startCode.Add($"N{lineNum} V.P.VAR_fan = {fanValue:F0}");
                    lineNum += 10;
                    startCode.Add($"N{lineNum} L fan_sub.nc");
                    lineNum += 10;
                }
                // Note: V.E.GLOBAL[3] and V.E.GLOBAL_BOOL[44] are NOT needed (already set by fan_sub)
            }
            
            // CRITICAL: Value acceptance command (must be set after all value changes)
            // This ensures all settings are applied before movement code starts
            // This MUST be the last command before layer initialization (which is done by DXRHelper)
            startCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[1] = 1");
            
            return startCode.ToArray();
        }

        /// <summary>
        /// Generates end G-code for machine shutdown (always turns everything off)
        /// Supports both simplified and advanced formats
        /// </summary>
        public string[] GetEndGCode()
        {
            if (UseAdvancedFormat)
            {
                // Advanced format: Turn off all zones
                var endCode = new System.Collections.Generic.List<string>();
                int lineNum = 9999990;
                
                // Turn off printbed zones
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[72] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[74] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[76] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[78] = FALSE");
                lineNum += 10;
                
                // Turn off extruder zones
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[44] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[24] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[26] = FALSE");
                lineNum += 10;
                endCode.Add($"N{lineNum} V.E.GLOBAL_BOOL[40] = FALSE");
                lineNum += 10;
                
                // Turn off fan
                endCode.Add($"N{lineNum} V.E.GLOBAL[3] = 0");
                lineNum += 10;
                
                endCode.Add("M29");
                return endCode.ToArray();
            }
            else
            {
                // Simplified format (backward compatible)
                return new string[]
                {
                    "N9999994 V.P.VAR_heatbedtemp = 0",
                    "N9999995 L heatbedTemp_sub.nc",
                    "N9999996 V.P.VAR_fan = 0", 
                    "N9999997 L fan_sub.nc",
                    "N9999998 V.P.VAR_extrudertemp = 0",
                    "N9999999 L extruderTemp_sub.nc",
                    "M29"
                };
            }
        }
    }
} 