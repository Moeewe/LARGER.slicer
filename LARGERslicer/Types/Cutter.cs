using System;
using System.Collections.Generic;

namespace LARGERslicer.Types
{
    /// <summary>
    /// Represents a CNC cutter/tool with its specifications.
    /// </summary>
    public class Cutter
    {
        public string Name { get; set; }
        public int ToolPosition { get; set; } // 11 (left), 21 (middle), 31 (right)
        public double Diameter { get; set; } // in mm
        public double MaxCuttingDepth { get; set; } // Maximum depth for through-cutting in mm
        public double MaxSurfaceDepth { get; set; } // Maximum depth for surface cutting in mm (optional, defaults to MaxCuttingDepth)
        public string Description { get; set; }

        public Cutter(string name, int toolPosition, double diameter, double maxCuttingDepth, double maxSurfaceDepth = 0, string description = "")
        {
            Name = name;
            ToolPosition = toolPosition;
            Diameter = diameter;
            MaxCuttingDepth = maxCuttingDepth;
            MaxSurfaceDepth = maxSurfaceDepth > 0 ? maxSurfaceDepth : maxCuttingDepth;
            Description = description;
        }

        public override string ToString()
        {
            return $"{Name} (Tool {ToolPosition}, Ø{Diameter}mm, Max: {MaxCuttingDepth}mm)";
        }

        /// <summary>
        /// Gets the default cutter database for Zünd CNC machines.
        /// MaxCuttingDepth: Maximum depth for through-cutting (limited by Saugglocke position: 3.3mm above material surface).
        /// MaxSurfaceDepth: Maximum depth for surface cutting (can be deeper than through-cutting).
        /// </summary>
        public static List<Cutter> GetDefaultCutters()
        {
            return new List<Cutter>
            {
                // Cutter database - adjust based on your actual tools
                // Format: Name, ToolPosition, Diameter (mm), MaxCuttingDepth (mm), MaxSurfaceDepth (mm), Description
                new Cutter("Fräser 1", 11, 3.0, 20.0, 30.0, "3mm diameter, 20mm through-cut max, 30mm surface max"),
                new Cutter("Fräser 2", 21, 4.0, 22.0, 32.0, "4mm diameter, 22mm through-cut max, 32mm surface max"),
                new Cutter("Fräser 3", 31, 5.0, 24.0, 35.0, "5mm diameter, 24mm through-cut max, 35mm surface max"),
                new Cutter("Fräser 4", 31, 6.0, 22.5, 35.0, "6mm diameter, 22.5mm through-cut max (max material thickness for through-cutting), 35mm surface max"),
                // Add more cutters as needed
                // Note: MaxCuttingDepth is limited by Saugglocke position (3.3mm above material surface)
                // For 6mm Fräser: Max cutting depth = 22.5mm (can cut through max 22.5mm material)
            };
        }

        /// <summary>
        /// Finds a cutter by name or tool position.
        /// </summary>
        public static Cutter FindCutter(List<Cutter> cutters, string nameOrPosition)
        {
            if (int.TryParse(nameOrPosition, out int toolPos))
            {
                return cutters.Find(c => c.ToolPosition == toolPos);
            }
            return cutters.Find(c => c.Name.Equals(nameOrPosition, StringComparison.OrdinalIgnoreCase));
        }
    }
}

