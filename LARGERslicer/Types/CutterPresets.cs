using System;
using System.Globalization;
using System.Linq;

namespace LARGERslicer.Types
{
    /// <summary>The cutters CNC - Cutter offers, shared by Grasshopper 1 and 2.</summary>
    public static class CutterPresets
    {
        /// <summary>The cutters of the list, then an own flat and an own ball cutter (kinds 6 and 7: the sizes come from the inputs).</summary>
        public static string[] Choices => Names.Concat(new[] { "Own flat cutter (the sizes below)", "Own ball cutter (the sizes below)" }).ToArray();

        private static readonly string[] FromDatabase = { "R206-A", "R204-A", "R202-A", "Ball 6", "Ball 4", "Ball 3" };
        public static readonly string[] Names = { "R206-A (flat, 6 mm)", "R204-A (flat, 4 mm)", "R202-A (flat, 2 mm)", "Ball 6 mm", "Ball 4 mm", "Ball 3 mm" };
        public static readonly string[] Shapes = { "Flat", "Ball" };
        public static readonly string[] Bits = new[] { "Not set" }
            .Concat(Enumerable.Range(1, 10).Select(i => "By hand " + i))
            .Concat(Enumerable.Range(1, 8).Select(i => "Place " + i)).ToArray();

        public const string Help = "Which cutter: 0 = R206-A (Zünd, flat, 6 mm: the one for roughing), 1 = R204-A (flat, 4 mm), 2 = R202-A (flat, 2 mm), 3 = ball 6 mm (the one for finishing), 4 = ball 4 mm, 5 = ball 3 mm. Balls are not in the Zünd set: check that yours has the same size, or use CNC - Own Cutter";
        public const string BitHelp = "Where the cutter sits: place 1 to 8 of the magazine (11 to 18, the machine changes it by itself), or by hand 1 to 10 (the machine stops and asks for it). Look into the machine: the wrong place means the wrong cutter. 0 = not set, then there is one file per cutter. It is the number of XX351";

        public static Cutter Make(int kind, int bit, double feed, double rpm, double diameter, double length, double max, int module, double stepDown, double stepover, out string error)
        {
            error = null;
            Cutter c;
            if (kind >= 0 && kind < FromDatabase.Length) c = Cutter.FindCutter(Cutter.GetDefaultCutters(), FromDatabase[kind]).Copy();
            else if (kind == 6 || kind == 7)
            {
                if (!(diameter > 0)) { error = "An own cutter needs its Diameter."; return null; }
                c = new Cutter((kind == 6 ? "Flat " : "Ball ") + diameter.ToString("0.##", CultureInfo.InvariantCulture), 31, diameter, max > 0 ? max : 22, max > 0 ? max : 22, "own cutter") { Shape = kind == 6 ? Cutter.Flat : Cutter.Ball, Feed = 40, SpindleSpeed = 30000 };
            }
            else { error = "Cutter is 0 to 7."; return null; }
            if (bit < 0 || bit > 18) { error = "Magazine is 0 to 18: 11-18 = place 1-8, 1-10 = by hand, 0 = not set."; return null; }
            if (module != 11 && module != 21 && module != 31) { error = "Module is 11, 21 or 31."; return null; }
            if (diameter > 0 && kind < 6) { c.Name += " (D " + diameter.ToString("0.##", CultureInfo.InvariantCulture) + ")"; c.Diameter = diameter; }
            if (length > 0) c.CuttingLength = length;
            if (max > 0) { c.MaxSurfaceDepth = max; c.MaxCuttingDepth = max; }
            if (feed > 0) c.Feed = feed;
            if (rpm > 0) c.SpindleSpeed = rpm;
            c.RouterBit = bit; c.ToolPosition = module;
            c.StepDown = Math.Max(0, stepDown); c.Stepover = Math.Max(0, stepover);
            return c;
        }

        public static string Info(Cutter c)
        {
            var i = CultureInfo.InvariantCulture;
            return string.Format(i, "{0}: {1}, {2:0.##} mm, cutting length {3}, reaches {4:0.##} mm deep, feed {5:0.#} mm/s, {6:0} RPM, module {7}, {8}. {9}",
                c.Name, c.Shape == Cutter.Ball ? "ball" : "flat", c.Diameter, c.CuttingLength > 0 ? c.CuttingLength.ToString("0.##", i) + " mm" : "not known", c.MaxSurfaceDepth, c.Feed, c.SpindleSpeed, c.ToolPosition, c.BitText, c.Description);
        }
    }
}
