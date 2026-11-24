using System;
using System.Collections.Generic;

namespace LARGERslicer.Types
{
    /// <summary>
    /// Represents a CNC cutter/tool with its specifications.
    /// </summary>
    public class Cutter
    {
        public const int Flat = 0, Ball = 1;

        public string Name { get; set; }
        public int ToolPosition { get; set; } // slot of the router module: 11 (left), 21 (middle), 31 (right)
        public double Diameter { get; set; } // in mm
        public double MaxCuttingDepth { get; set; } // Maximum depth for through-cutting in mm
        public double MaxSurfaceDepth { get; set; } // Maximum depth for surface cutting in mm (optional, defaults to MaxCuttingDepth)
        public string Description { get; set; }
        public double SpindleSpeed { get; set; } // RPM given at the Cutter Selector, 0 = not set

        /// <summary>0 = flat end, 1 = ball.</summary>
        public int Shape { get; set; }
        /// <summary>Length of the cutting edge in mm, 0 = not known. Above it the shank begins.</summary>
        public double CuttingLength { get; set; }
        public double ShankDiameter { get; set; } = 6.0; // collet of the RM-A
        /// <summary>The number the Zünd changes bits with (XX351): 11-18 = magazine place 1-8 (automatic), 1-10 = by hand, guided at the machine, 0 = no change.</summary>
        public int RouterBit { get; set; }
        /// <summary>Feed in mm/s, 0 = not set.</summary>
        public double Feed { get; set; }
        /// <summary>How deep one level goes, and how far the paths lie apart, in mm. 0 = worked out.</summary>
        public double StepDown { get; set; }
        public double Stepover { get; set; }

        public Cutter(string name, int toolPosition, double diameter, double maxCuttingDepth, double maxSurfaceDepth = 0, string description = "")
        {
            Name = name;
            ToolPosition = toolPosition;
            Diameter = diameter;
            MaxCuttingDepth = maxCuttingDepth;
            MaxSurfaceDepth = maxSurfaceDepth > 0 ? maxSurfaceDepth : maxCuttingDepth;
            Description = description;
        }

        public Cutter Copy() => (Cutter)MemberwiseClone();

        /// <summary>Where the magazine place or the manual bit is named in words.</summary>
        public string BitText => RouterBit >= 11 && RouterBit <= 18 ? $"magazine place {RouterBit - 10} (XX351,{RouterBit})" : RouterBit >= 1 && RouterBit <= 10 ? $"bit {RouterBit}, changed by hand (XX351,{RouterBit})" : "no bit change";

        public override string ToString()
        {
            return $"{Name} (Tool {ToolPosition}, Ø{Diameter}mm{(Shape == Ball ? " ball" : "")}, Max: {MaxCuttingDepth}mm)";
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
                // the bits of the Zünd universal set (diameter / cutting length), and ball cutters for Rough and Finish
                new Cutter("R206-A", 31, 6.0, 22.0, 35.0, "Zünd multipurpose, flat, 6 mm, cutting length 22 mm: for roughing") { CuttingLength = 22, Feed = 50, SpindleSpeed = 30000 },
                new Cutter("R204-A", 31, 4.0, 14.0, 30.0, "Zünd multipurpose, flat, 4 mm, cutting length 14 mm") { CuttingLength = 14, Feed = 40, SpindleSpeed = 35000 },
                new Cutter("R202-A", 31, 2.0, 6.0, 30.0, "Zünd multipurpose, flat, 2 mm, cutting length 6 mm, 6 mm shank") { CuttingLength = 6, Feed = 25, SpindleSpeed = 40000 },
                new Cutter("Ball 6", 31, 6.0, 22.0, 35.0, "Ball cutter R3, 6 mm shank, for finishing (not in the Zünd set: check the cutting length of yours)") { Shape = Ball, CuttingLength = 22, Feed = 50, SpindleSpeed = 35000 },
                new Cutter("Ball 4", 31, 4.0, 14.0, 30.0, "Ball cutter R2, 6 mm shank, for finishing (not in the Zünd set: check the cutting length of yours)") { Shape = Ball, CuttingLength = 14, Feed = 40, SpindleSpeed = 40000 },
                new Cutter("Ball 3", 31, 3.0, 12.0, 30.0, "Ball cutter R1.5, 6 mm shank, for fine finishing (not in the Zünd set: check the cutting length of yours)") { Shape = Ball, CuttingLength = 12, Feed = 30, SpindleSpeed = 40000 },
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

        /// <summary>
        /// All cutters on a tool position given as a number; empty if a name is given.
        /// </summary>
        public static List<Cutter> Ambiguous(List<Cutter> cutters, string nameOrPosition)
        {
            return int.TryParse(nameOrPosition, out int toolPos) ? cutters.FindAll(c => c.ToolPosition == toolPos) : new List<Cutter>();
        }
    }
}
