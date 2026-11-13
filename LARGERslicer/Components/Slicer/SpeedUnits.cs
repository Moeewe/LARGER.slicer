using System;
using System.Globalization;
using Grasshopper.Kernel;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Speeds are entered in mm/s, as people think about a moving nozzle; the path and the machine
    /// files keep mm/min (the F of G-code and the DXR). Definitions saved before speeds were given in mm/s gave
    /// mm/min: they carry no "SpeedMmS" mark and are read as before, with a note.
    /// </summary>
    public static class SpeedUnits
    {
        private const string Key = "SpeedMmS";

        public static void Mark(GH_IO.Serialization.GH_IWriter writer) => writer.SetBoolean(Key, true);

        public static bool IsOld(GH_IO.Serialization.GH_IReader reader) => !reader.ItemExists(Key);

        /// <summary>One speed in mm/s (or mm/min from an old definition) to mm/min.</summary>
        public static double ToMmPerMin(GH_Component c, bool old, double v)
        {
            if (old)
            {
                c.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Saved with an older version: Speed {Num(v)} is read as mm/min ({Num(v / 60)} mm/s). Speeds are now entered in mm/s.");
                return v;
            }
            if (v > 1000) c.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Speed {Num(v)} mm/s is {Num(v * 60)} mm/min. Speeds are entered in mm/s: did you mean {Num(v / 60)} mm/s?");
            return v * 60;
        }

        /// <summary>Speed, min and max speed of Print Settings in place.</summary>
        public static bool ToMmPerMin(GH_Component c, bool old, ref double speed, ref double min, ref double max)
        {
            speed = ToMmPerMin(c, old, speed);
            if (!old) { min *= 60; max *= 60; }
            return true;
        }

        /// <summary>"50 mm/s" for a speed in mm/min.</summary>
        public static string Text(double mmPerMin) => Num(mmPerMin / 60) + " mm/s";

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
