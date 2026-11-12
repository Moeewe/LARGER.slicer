using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// The whole chain in one call, with the settings most prints need: walls, bottom, infill,
    /// one continuous path, rounded corners, skirt and exit, and the check. The single
    /// components do the same steps one by one and offer every option.
    /// </summary>
    public static class EasySlicer
    {
        public static readonly string[] Infills =
        {
            "None (walls only)", "Ribs", "Wave", "Truss", "Meander", "Honeycomb", "Grid", "Chamber wall",
        };

        public class Options
        {
            public int Walls = 1;
            public int BottomLayers = 0;
            public int Infill = 0;          // index into Infills
            public double InfillSize = 0;   // 0 = chosen from the bead width
            public bool Spiral = false;     // vase mode: only for walls without infill and bottom
            public int SkirtLoops = 1;
        }

        public class Result
        {
            public List<SliceLayer> Layers = new List<SliceLayer>();
            public Toolpath Path;
            public LeadEngine.CheckResult Check;
            public List<string> Steps = new List<string>();   // what was done, in order
            public List<string> Notes = new List<string>();   // what the user should know
            public bool Ok => Path != null && Path.Count > 1 && Check != null && Check.Ok;
        }

        /// <summary>Infill size that suits the bead when none is given: about 16 bead widths.</summary>
        public static double DefaultSize(PrintSettings st, int infill)
        {
            double w = st.BeadWidth;
            switch (infill)
            {
                case 1: return 30 * w;   // ribs: one every 150 mm for a 5 mm bead
                case 5: case 6: return 12 * w;
                case 7: return 14 * w;
                default: return 16 * w;  // waves
            }
        }

        public static Result Run(List<SliceLayer> layers, PrintSettings st, Options o)
        {
            var r = new Result();
            var log = new List<string>();
            if (layers == null || layers.Count == 0) { r.Notes.Add("No layers: the part is lower than one layer, or it does not stand on the slicing plane."); return r; }
            int infill = Math.Max(0, Math.Min(Infills.Length - 1, o.Infill));
            double size = o.InfillSize > 0 ? o.InfillSize : DefaultSize(st, infill);
            r.Steps.Add($"{layers.Count} layers of {st.LayerHeight:0.##} mm, bead {st.BeadWidth:0.##} mm");

            layers = FillEngine.Walls(layers, st, Math.Max(1, o.Walls), 0, log);
            r.Steps.Add(o.Walls > 1 ? $"{o.Walls} walls" : "1 wall");
            if (layers.Count == 0) { r.Notes.Add("The part is thinner than one bead width everywhere: nothing to print. Use a narrower bead or a thicker part."); r.Notes.AddRange(log); return r; }

            if (o.BottomLayers > 0)
            {
                layers = FillEngine.Bottom(layers, st, o.BottomLayers, 0, 0, log, out int filled);
                r.Steps.Add($"{filled} solid bottom layer(s)");
            }
            int n = 0;
            if (infill > 0)
            {
                // With an automatic size: if nothing fits, try smaller ones before giving up.
                var before = layers; double asked = size;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    var tryLog = new List<string>();
                    switch (infill)
                    {
                        case 1: layers = FillEngine.Ribs(before, st, new List<object>(), size, 0, true, 0.15, tryLog, out n); break;
                        case 2: case 3: case 4: layers = FillEngine.Waves(before, st, infill - 2, size, 0, null, tryLog, out n); break;
                        case 5: layers = FillEngine.Cells(before, st, 0, size, 0, null, tryLog, out n); break;
                        case 6: layers = FillEngine.Cells(before, st, 1, size, 0, null, tryLog, out n); break;
                        default: layers = FillEngine.Chambers(before, st, 0, size, 0.6 * size, tryLog, out n); break;
                    }
                    if (n > 0 || o.InfillSize > 0 || size / 1.5 < 6 * st.BeadWidth) { log.AddRange(tryLog); break; }
                    size /= 1.5;
                }
                r.Steps.Add($"{Infills[infill]}, size {size:0} mm: up to {n} per layer");
                if (n == 0) r.Notes.Add($"No {Infills[infill].ToLowerInvariant()} fits into this part" + (o.InfillSize > 0 ? $" at size {size:0} mm. Try a smaller Infill Size." : ": it is too small for this pattern with a " + $"{st.BeadWidth:0.#} mm bead. Printed with walls only."));
                else if (size < asked - 1e-9) r.Notes.Add($"The infill size was reduced from {asked:0} to {size:0} mm so the pattern fits the part.");
            }
            r.Layers = layers;

            bool spiral = o.Spiral && infill == 0 && o.BottomLayers == 0;
            if (o.Spiral && !spiral) r.Notes.Add("Spiral only works for walls without infill and bottom; printed as flat layers instead.");
            var tp = PathEngine.Build(layers, st, spiral ? ContinuousMode.Spiral : ContinuousMode.Layered, null, 0, false, log);
            if (tp.Count < 2) { r.Notes.Add("No printable contour was found."); r.Notes.AddRange(log); return r; }
            tp = PathEngine.Round(tp, st.EffectiveMinRadius, 0, out int corners, out _);
            tp = LeadEngine.StartEnd(tp, Math.Max(0, o.SkirtLoops), 0, 10 * st.BeadWidth, 4 * st.BeadWidth, null, 0, log);
            r.Path = tp;
            r.Steps.Add((spiral ? "spiral path" : "flat layers with an open seam (gap of one bead width)") + $", {corners} corner(s) rounded, skirt and exit");
            r.Check = LeadEngine.Check(tp, 0);
            r.Notes.AddRange(log.Distinct());
            // travel moves inside the part: the extruder keeps flowing there, so say how to avoid them
            // (the move from the skirt to the part and the exit move at the end do not count)
            int travels = 0, firstPart = -1, lastPart = -1;
            for (int i = 0; i < tp.Count; i++) if (tp.Roles[i] != PathRole.Skirt && tp.Roles[i] != PathRole.Travel) { if (firstPart < 0) firstPart = i; lastPart = i; }
            for (int i = Math.Max(1, firstPart); i < lastPart; i++) if (tp.Roles[i] == PathRole.Travel && tp.Roles[i - 1] != PathRole.Travel) travels++;
            if (travels > 0)
                r.Notes.Add(infill == 2 || infill == 3 || infill == 4 || infill == 5 || infill == 6
                    ? $"{travels} move(s) inside the part run without extrusion; a pellet extruder leaves a thread there."
                    : $"{travels} move(s) inside the part run without extrusion (a pellet extruder leaves a thread there): the walls of openings or separate pieces are not joined to the outer wall. Infill Wave, Truss, Meander, Honeycomb or Grid joins them into one line.");
            return r;
        }
    }

    /// <summary>Where a file goes when the user gives no path, a folder, or a name without ending.</summary>
    public static class OutputFile
    {
        /// <summary>
        /// Empty = the Desktop with the default name. A folder = that folder with the default name.
        /// A name without ending gets the ending of the default name.
        /// </summary>
        public static string Resolve(string file, string defaultName)
        {
            string ext = System.IO.Path.GetExtension(defaultName);
            if (string.IsNullOrWhiteSpace(file))
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), defaultName);
            file = file.Trim().Trim('"');
            if (file.StartsWith("~")) file = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + file.Substring(1);
            if (System.IO.Directory.Exists(file)) return System.IO.Path.Combine(file, defaultName);
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(file))) file += ext;
            return file;
        }

        /// <summary>Writes the lines; returns the path written, or throws with a message that says why not.</summary>
        public static string Write(string file, string defaultName, IEnumerable<string> lines)
        {
            string path = Resolve(file, defaultName);
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) throw new System.IO.DirectoryNotFoundException($"The folder {dir} does not exist.");
            System.IO.File.WriteAllLines(path, lines);
            return path;
        }
    }
}
