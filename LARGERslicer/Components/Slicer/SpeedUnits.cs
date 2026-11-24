using System;
using System.Globalization;
using Grasshopper.Kernel;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Speeds are entered in mm/s, as people think about a moving nozzle; the path and the machine
    /// files keep mm/min (the F of G-code and the DXR). Definitions saved before speeds were given in mm/s gave
    /// mm/min: they carry no "SpeedMmS" mark and are read as before, with a note. They stay in mm/min
    /// (saved without the mark, rows in mm/min) until somebody changes them in the context menu.
    /// </summary>
    public static class SpeedUnits
    {
        private const string Key = "SpeedMmS";

        /// <summary>Marks speeds in mm/s. An old definition stays unmarked: its values are still mm/min.</summary>
        public static void Mark(GH_IO.Serialization.GH_IWriter writer, bool old) { if (!old) writer.SetBoolean(Key, true); }

        public static bool IsOld(GH_IO.Serialization.GH_IReader reader) => !reader.ItemExists(Key);

        /// <summary>One speed in mm/s (or mm/min from an old definition) to mm/min.</summary>
        public static double ToMmPerMin(GH_Component c, bool old, double v)
        {
            if (old)
            {
                c.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Saved with an older version: Speed {Num(v)} is read as mm/min ({Num(v / 60)} mm/s). Speeds are now entered in mm/s; to change this component: right-click, 'Change speeds to mm/s'.");
                return v;
            }
            if (v > 1000) c.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Speed {Num(v)} mm/s is {Num(v * 60)} mm/min. Speeds are entered in mm/s: did you mean {Num(v / 60)} mm/s?");
            return v * 60;
        }

        /// <summary>Context menu of an old definition: the typed speeds become mm/s, wired ones are read as mm/s from then on.</summary>
        public static void AppendMenu(System.Windows.Forms.ToolStripDropDown menu, GH_Component c, int[] inputs, Action changed)
        {
            bool wired = false;
            foreach (int i in inputs) if (i < c.Params.Input.Count && c.Params.Input[i].SourceCount > 0) wired = true;
            GH_DocumentObject.Menu_AppendItem(menu, "Change speeds to mm/s", (s, e) =>
            {
                c.RecordUndoEvent("Change speeds to mm/s");
                foreach (int i in inputs)
                {
                    if (i >= c.Params.Input.Count || !(c.Params.Input[i] is Grasshopper.Kernel.Parameters.Param_Number p)) continue;
                    foreach (var branch in p.PersistentData.Branches)
                        for (int k = 0; k < branch.Count; k++)
                            if (branch[k] != null) branch[k] = new Grasshopper.Kernel.Types.GH_Number(Math.Round(branch[k].Value / 60, 4));
                    p.ExpireSolution(false);
                }
                changed();
                c.ExpireSolution(true);
            }).ToolTipText = "This component was saved with an older version and reads its speeds as mm/min. Click: the speeds set on it are converted (3000 mm/min becomes 50 mm/s)."
                + (wired ? " A speed is connected here: its slider has to be changed by hand (divide by 60)." : "");
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
