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
            "None (walls only)", "Ribs", "Wave", "Truss", "Meander", "Honeycomb", "Grid", "Chamber wall", "Solid (rings along the wall)",
        };

        public class Options
        {
            public int Walls = 1;
            public int BottomLayers = 0;
            public int Infill = 0;          // index into Infills
            public double InfillSize = 0;   // 0 = chosen from the bead width
            public bool Spiral = false;     // vase mode: only for walls without infill and bottom
            public int SkirtLoops = 1;
            /// <summary>Brim: 0 = none, 1 = outside, 2 = inside, 3 = both sides of the first layer.</summary>
            public int BrimSide = 0;
            public int BrimLoops = 3;
            /// <summary>Join contours that lie inside others (inner shell, openings) by a bridge, so the line never stops.</summary>
            public bool Bridge = true;
            /// <summary>Pattern of solid layers: 0 = auto, 1 = zigzag, 2 = contour (rings along the wall).</summary>
            public int BottomPattern = 0;
            /// <summary>Solid layers that close the top of the part.</summary>
            public int TopLayers = 0;
            /// <summary>Walls: 0 = inside the geometry, 1 = centred on it, 2 = outside.</summary>
            public int WallPosition = 0;
            /// <summary>Seam: 0 = fixed point (where the first layer starts), 1 = inner corner, 2 = sharpest corner.</summary>
            public int Seam = 0;
            /// <summary>Shortest time per layer (s): shorter layers are printed slower, and the check reports it. 0 = off.</summary>
            public double MinLayerTime = 0;
            /// <summary>Most the extruder delivers (mm3/s); moves that ask for more run slower. 0 = off.</summary>
            public double MaxVolumeRate = 0;
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

        private sealed class Filled
        {
            public List<SliceLayer> Layers; public double Size; public double Hang;
            public List<string> Steps = new List<string>(), Notes = new List<string>(), Log = new List<string>();
        }

        /// <summary>
        /// The longest stretch of the solid fill printed in layer 'top' that lies more than half a bead beside
        /// everything printed in the layer below (walls, infill, fill): where it hangs in the air (mm).
        /// </summary>
        internal static double FreeSpan(Toolpath tp, int top, double w)
        {
            var cells = new Dictionary<long, List<Line>>();
            Func<double, double, long> key = (x, y) => ((long)Math.Floor(x / w) * 73856093L) ^ ((long)Math.Floor(y / w) * 19349663L);
            for (int i = 1; i < tp.Count; i++)
            {
                if (tp.Layers[i] != top - 1 || tp.Flows[i] <= 0 || tp.Roles[i] == PathRole.Travel) continue;
                var ln = new Line(new Point3d(tp.Points[i - 1].X, tp.Points[i - 1].Y, 0), new Point3d(tp.Points[i].X, tp.Points[i].Y, 0));
                int k = Math.Max(1, (int)Math.Ceiling(ln.Length / w));
                var seen = new HashSet<long>();
                for (int j = 0; j <= k; j++) { var q = ln.PointAt(j / (double)k); long kk = key(q.X, q.Y); if (seen.Add(kk)) { if (!cells.TryGetValue(kk, out var l)) cells[kk] = l = new List<Line>(); l.Add(ln); } }
            }
            double longest = 0, run = 0;
            for (int i = 1; i < tp.Count; i++)
            {
                if (tp.Layers[i] != top || tp.Roles[i] != PathRole.Bottom || tp.Flows[i] <= 0) { run = 0; continue; }
                Point3d a = tp.Points[i - 1], b = tp.Points[i];
                double L = a.DistanceTo(b); int k = Math.Max(1, (int)Math.Ceiling(L / (0.5 * w)));
                for (int j = 1; j <= k; j++)
                {
                    var q = a + (b - a) * (j / (double)k); var q0 = new Point3d(q.X, q.Y, 0);
                    bool held = false;
                    for (int dx = -1; dx <= 1 && !held; dx++)
                        for (int dy = -1; dy <= 1 && !held; dy++)
                            if (cells.TryGetValue(key(q.X + dx * w, q.Y + dy * w), out var l))
                                foreach (var ln in l) if (ln.ClosestPoint(q0, true).DistanceTo(q0) <= 0.5 * w) { held = true; break; }
                    if (held) run = 0; else { run += L / k; if (run > longest) longest = run; }
                }
            }
            return longest;
        }

        public static Result Run(List<SliceLayer> layers, PrintSettings st, Options o)
        {
            var r = new Result();
            var log = new List<string>();
            if (layers == null || layers.Count == 0) { r.Notes.Add("No layers: the part is lower than one layer, or it does not stand on the slicing plane."); return r; }
            int infill = Math.Max(0, Math.Min(Infills.Length - 1, o.Infill));
            // a closed top over an empty box: the liquid plastic would fall in. The top needs something to
            // lie on, so a grid is added below it as support
            bool topSupport = o.TopLayers > 0 && infill == 0;
            if (topSupport)
            {
                infill = 6; // a grid: it carries the top lines whichever way they run (ribs only cross one direction)
                r.Notes.Add("Top Layers need something to lie on: over an empty box the liquid plastic falls in. A grid of four bead widths was added as support below the top. Choose another infill, or set Top Layers to 0 for an open box.");
            }
            double size = o.InfillSize > 0 ? o.InfillSize : DefaultSize(st, infill);
            // a grid that carries a top: close enough that the first top layer bridges from wall to wall of a cell
            if (topSupport && o.InfillSize <= 0) size = 4 * st.BeadWidth;
            r.Steps.Add(Inv.F($"{layers.Count} layers of {st.LayerHeight:0.##} mm{(layers.Count > 0 && Math.Abs(layers[0].Height - st.LayerHeight) > 1e-6 ? Inv.F($" (the first {layers[0].Height:0.##} mm)") : "")}, bead {st.BeadWidth:0.##} mm"));

            layers = FillEngine.Walls(layers, st, Math.Max(1, o.Walls), Math.Max(0, Math.Min(2, o.WallPosition)), log);
            r.Steps.Add(o.Walls > 1 ? Inv.F($"{o.Walls} walls") : "1 wall");
            if (layers.Count == 0) { r.Notes.Add("The part is thinner than one bead width everywhere: nothing to print. Use a narrower bead or a thicker part."); r.Notes.AddRange(log); return r; }

            int bottomPattern = Math.Max(0, Math.Min(2, o.BottomPattern));
            bool solid = infill == 8;
            var walled = layers;
            // bottom, top and infill with the solid lines in one direction (turned 90 degrees every layer)
            Func<double, Filled> fill = angle =>
            {
                var f = new Filled { Layers = walled, Size = size };
                if (solid)
                {
                    // a solid part: every layer filled with rings along the wall, joined into one line whose
                    // start and end lie at the wall (zigzag would need a move without extrusion between layers)
                    f.Layers = FillEngine.Bottom(f.Layers, st, f.Layers.Count, 2, 0, f.Log, out int filledAll);
                    f.Steps.Add(Inv.F($"solid, rings along the wall: {filledAll} layer(s) filled"));
                    return f;
                }
                if (o.BottomLayers > 0 || o.TopLayers > 0)
                {
                    f.Layers = FillEngine.Bottom(f.Layers, st, Math.Max(0, o.BottomLayers), bottomPattern, angle, f.Log, out int filled, Math.Max(0, o.TopLayers));
                    f.Steps.Add(Inv.F($"{filled} solid layer(s) ({(o.BottomLayers > 0 ? o.BottomLayers + " bottom" : "")}{(o.BottomLayers > 0 && o.TopLayers > 0 ? ", " : "")}{(o.TopLayers > 0 ? o.TopLayers + " top" : "")}, {new[] { "auto", "zigzag", "contour" }[bottomPattern]})"));
                }
                if (infill > 0)
                {
                    // With an automatic size: if nothing fits, try smaller ones before giving up.
                    var before = f.Layers; double sz = size; int n = 0;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        var tryLog = new List<string>();
                        switch (infill)
                        {
                            case 1: f.Layers = FillEngine.Ribs(before, st, new List<object>(), sz, 0, true, 0.15, tryLog, out n); break;
                            case 2: case 3: case 4: f.Layers = FillEngine.Waves(before, st, infill - 2, sz, 0, null, tryLog, out n); break;
                            case 5: f.Layers = FillEngine.Cells(before, st, 0, sz, 0, null, tryLog, out n); break;
                            case 6: f.Layers = FillEngine.Cells(before, st, 1, sz, 0, null, tryLog, out n); break;
                            default: f.Layers = FillEngine.Chambers(before, st, 0, sz, 0.6 * sz, tryLog, out n); break;
                        }
                        if (n > 0 || o.InfillSize > 0 || sz / 1.5 < 6 * st.BeadWidth) { f.Log.AddRange(tryLog); break; }
                        sz /= 1.5;
                    }
                    f.Size = sz;
                    f.Steps.Add(Inv.F($"{Infills[infill]}, size {sz:0} mm: up to {n} per layer"));
                    if (n == 0) f.Notes.Add(Inv.F($"No {Infills[infill].ToLowerInvariant()} fits into this part") + (o.InfillSize > 0 ? Inv.F($" at size {sz:0} mm. Try a smaller Infill Size.") : ": it is too small for this pattern with a " + Inv.F($"{st.BeadWidth:0.#} mm bead. Printed with walls only.")));
                    else if (sz < size - 1e-9) f.Notes.Add(Inv.F($"The infill size was reduced from {size:0} to {sz:0} mm so the pattern fits the part."));
                }
                return f;
            };
            var best = fill(0);
            bool spiral = o.Spiral && infill == 0 && o.BottomLayers == 0 && o.TopLayers == 0;
            if (o.Spiral && !spiral) r.Notes.Add("Spiral only works for walls without infill and bottom; printed as flat layers instead.");
            if (o.MinLayerTime > 0 || o.MaxVolumeRate > 0) { st = st.Duplicate(); st.MinLayerTime = Math.Max(0, o.MinLayerTime); st.MaxVolumeRate = Math.Max(0, o.MaxVolumeRate); }
            var stp = st;
            Func<Filled, Toolpath> build = f => PathEngine.Build(f.Layers, stp, spiral ? ContinuousMode.Spiral : ContinuousMode.Layered, null, 0, false, f.Log, o.Bridge, (SeamMode)Math.Max(0, Math.Min(2, o.Seam)));
            var tp = build(best);
            // a top over infill: its first layer must cross the infill below as it is printed, not run along it
            // (a line between two infill walls hangs in the air). Both directions are tried; less free span wins.
            if (!solid && infill > 0 && o.TopLayers > 0 && best.Layers.Count > o.TopLayers && tp.Count > 1)
            {
                int first = best.Layers.OrderBy(l => l.Index).ElementAt(best.Layers.Count - o.TopLayers).Index;
                double hang = FreeSpan(tp, first, st.BeadWidth);
                if (hang > 1.5 * st.BeadWidth)
                {
                    var turned = fill(90);
                    var tp2 = build(turned);
                    double hang2 = tp2.Count > 1 ? FreeSpan(tp2, first, st.BeadWidth) : double.MaxValue;
                    if (hang2 < hang - 1e-6) { best = turned; tp = tp2; hang = hang2; }
                }
            }
            layers = best.Layers; size = best.Size;
            r.Steps.AddRange(best.Steps); r.Notes.AddRange(best.Notes); log.AddRange(best.Log);
            r.Layers = layers;
            if (tp.Count < 2) { r.Notes.Add("No printable contour was found."); r.Notes.AddRange(log); return r; }
            tp = PathEngine.Round(tp, st.EffectiveMinRadius, 0, out int corners, out _, log);
            tp = LeadEngine.StartEnd(tp, Math.Max(0, o.SkirtLoops), 0, Math.Max(0, o.BrimLoops), o.BrimSide, 10 * st.BeadWidth, 4 * st.BeadWidth, null, 0, log);
            r.Path = tp;
            r.Steps.Add((spiral ? "spiral path" : "flat layers with an open seam (gap of one bead width)") + Inv.F($", {corners} corner(s) rounded, skirt{(o.BrimSide > 0 && o.BrimLoops > 0 ? ", brim" : "")} and exit"));
            r.Check = LeadEngine.Check(tp, 0);
            r.Notes.AddRange(log.Distinct());
            // travel moves inside the part: the extruder keeps flowing there, so say how to avoid them
            // (the move from the skirt to the part and the exit move at the end do not count)
            int travels = 0, firstPart = -1, lastPart = -1;
            for (int i = 0; i < tp.Count; i++) if (tp.Roles[i] != PathRole.Skirt && tp.Roles[i] != PathRole.Travel) { if (firstPart < 0) firstPart = i; lastPart = i; }
            for (int i = Math.Max(1, firstPart); i < lastPart; i++) if (tp.Roles[i] == PathRole.Travel && tp.Roles[i - 1] != PathRole.Travel) travels++;
            if (travels > 0)
                r.Notes.Add(infill == 2 || infill == 3 || infill == 4 || infill == 5 || infill == 6
                    ? Inv.F($"{travels} move(s) inside the part run without extrusion; a pellet extruder leaves a thread there.")
                    : Inv.F($"{travels} move(s) inside the part run without extrusion (a pellet extruder leaves a thread there): the walls of openings or separate pieces are not joined to the outer wall. Infill Wave, Truss, Meander, Honeycomb or Grid joins them into one line."));
            return r;
        }
    }

    /// <summary>Where a file goes when the user gives no path, a folder, or a name without ending.</summary>
    public static class OutputFile
    {
        /// <summary>
        /// The speed line of a file header, as the moves were written: print speed in mm/s, and,
        /// when Min Layer Time or Max Volume Rate are set, that short layers or high-volume moves
        /// run slower (the F of every move says how fast). Never "constant" when it is not.
        /// </summary>
        public static string SpeedNote(PrintSettings st, PathEngine.ProcessResult pr)
        {
            string s = Inv.F($"print speed {st.Speed / 60.0:0.#} mm/s");
            if (st.FirstLayerSpeed > 0) s += Inv.F($", first layer {st.FirstLayerSpeed / 60.0:0.#} mm/s");
            var slower = new List<string>();
            if (pr != null && pr.LayerFeed.Count > 0)
            {
                double low = pr.LayerFeed.Values.Min();
                slower.Add(Inv.F($"{pr.LayerFeed.Count} short layer(s) slower for Min Layer Time {st.MinLayerTime:0} s (down to {low / 60.0:0.#} mm/s)"));
            }
            else if (st.MinLayerTime > 0) slower.Add(Inv.F($"Min Layer Time {st.MinLayerTime:0} s (no layer needs to slow down)"));
            if (pr != null && pr.VolumeCapped > 0)
                slower.Add(Inv.F($"{pr.VolumeCapped} move(s) slower for Max Volume Rate {st.MaxVolumeRate:0} mm3/s"));
            else if (st.MaxVolumeRate > 0) slower.Add(Inv.F($"Max Volume Rate {st.MaxVolumeRate:0} mm3/s (no move above it)"));
            if (slower.Count == 0) return s + (st.FirstLayerSpeed > 0 ? ", otherwise the same on every print move" : ", the same on every print move") + " (Min Layer Time and Max Volume Rate are off)";
            return s + "; " + string.Join("; ", slower) + "; the F of each move is the speed it runs at";
        }

        /// <summary>
        /// Empty = the Desktop with the default name. A folder = that folder with the default name.
        /// A name without ending gets the ending of the default name.
        /// </summary>
        public static string Resolve(string file, string defaultName)
        {
            string ext = System.IO.Path.GetExtension(defaultName);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(file)) return System.IO.Path.Combine(desktop, defaultName);
            file = file.Trim().Trim('"');
            if (file.StartsWith("~")) file = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + file.Substring(1);
            if (System.IO.Directory.Exists(file)) return System.IO.Path.Combine(file, defaultName);
            // a name ending like the weight or the time of a print ("..._53min") is not a file ending
            string given = System.IO.Path.GetExtension(file);
            // nor is a number ("Part 1.5mm", "v1.2"): an ending begins with a letter
            if (string.IsNullOrEmpty(given) || given.Length > 6 || given.Length < 2 || !char.IsLetter(given[1]) || given.Contains(" ")) file += ext;
            // a bare name goes to the Desktop
            if (string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(file))) file = System.IO.Path.Combine(desktop, file);
            return file;
        }

        /// <summary>Letters, digits, spaces, minus and underscore only; everything else would trouble a controller or a file system.</summary>
        public static string Clean(string name)
        {
            var chars = (name ?? "").Trim().Select(ch => char.IsLetterOrDigit(ch) && ch < 128 || ch == ' ' || ch == '-' || ch == '_' ? ch : '_').ToArray();
            string s = new string(chars).Trim('_', ' ');
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.Length == 0 ? "Print" : s;
        }

        /// <summary>Weight of the printed material in grams: volume of the beads x density.</summary>
        public static double Grams(double volumeMm3, PrintSettings st) => volumeMm3 / 1000.0 * (st != null && st.Density > 0 ? st.Density : 1.25);

        /// <summary>
        /// The name a print file gets when none is given, as the FH Muenster names its files:
        /// PREFIX_date and time_name of the part_weight_time, e.g. DXR_2611051216_Test Object_2668g_53min.dxr.
        /// The date and time (year month day hour minute) keep the files of a day in order; weight
        /// and time say at a glance how much material and how long. At most 64 characters.
        /// </summary>
        public static string Name(string prefix, string part, Toolpath tp, string extension)
        {
            if (tp != null && tp.Count > 1)
            {
                var pr = PathEngine.Process(tp, false);
                return Name(prefix, part, Grams(pr.Volume, tp.Settings), pr.Minutes, extension);
            }
            return Name(prefix, part, double.NaN, double.NaN, extension);
        }

        /// <summary>As above, with weight (g) and time (min) known already (NaN = left out).</summary>
        public static string Name(string prefix, string part, double grams, double minutes, string extension)
        {
            string stamp = DateTime.Now.ToString("yyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
            string tail = "";
            if (!double.IsNaN(grams) && !double.IsNaN(minutes))
                tail = "_" + (grams > 0 ? Math.Max(1, Math.Round(grams)) : 0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "g_"
                     + Math.Max(1, Math.Round(minutes)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "min";
            string head = Clean(prefix).ToUpperInvariant() + "_" + stamp + "_";
            extension = extension ?? "";
            if (extension.Length > 0 && !extension.StartsWith(".")) extension = "." + extension;
            string name = Clean(part);
            int room = 64 - head.Length - tail.Length - extension.Length;
            if (name.Length > room) name = name.Substring(0, Math.Max(1, room)).Trim();
            return head + name + tail + extension;
        }

        /// <summary>Writes the lines; returns the path written, or throws with a message that says why not.</summary>
        public static string Write(string file, string defaultName, IEnumerable<string> lines)
        {
            string path = Resolve(file, defaultName);
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) throw new System.IO.DirectoryNotFoundException(Inv.F($"The folder {dir} does not exist."));
            System.IO.File.WriteAllLines(path, lines);
            return path;
        }
    }
}
