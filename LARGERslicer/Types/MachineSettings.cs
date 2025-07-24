using System;

namespace LARGERslicer.Types
{
    public class MachineSettings
    {
        public double BedTemperature { get; set; }
        public double NozzleTemperature { get; set; }
        public double CoolingPercentage { get; set; }

        public MachineSettings(double bedTemp, double nozzleTemp, double cooling)
        {
            BedTemperature = Math.Max(0, bedTemp);
            NozzleTemperature = Math.Max(0, nozzleTemp);
            CoolingPercentage = Math.Max(0, Math.Min(100, cooling));
        }

        public override string ToString()
        {
            return $"Bed: {BedTemperature}°C, Nozzle: {NozzleTemperature}°C, Cooling: {CoolingPercentage}%";
        }

        /// <summary>
        /// Generates start G-code for machine initialization
        /// </summary>
        public string[] GetStartGCode()
        {
            var startCode = new System.Collections.Generic.List<string>();
            
            // Set temperatures if > 0
            if (BedTemperature > 0)
            {
                startCode.Add($"N10 V.P.VAR_heatbedtemp = {BedTemperature:F0}");
                startCode.Add("N20 L heatbedTemp_sub.nc");
            }
            
            if (NozzleTemperature > 0)
            {
                startCode.Add($"N30 V.P.VAR_extrudertemp = {NozzleTemperature:F0}");
                startCode.Add("N40 L extruderTemp_sub.nc");
            }
            
            if (CoolingPercentage > 0)
            {
                startCode.Add($"N50 V.P.VAR_fan = {CoolingPercentage:F0}");
                startCode.Add("N60 L fan_sub.nc");
            }

            return startCode.ToArray();
        }

        /// <summary>
        /// Generates end G-code for machine shutdown (always turns everything off)
        /// </summary>
        public string[] GetEndGCode()
        {
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