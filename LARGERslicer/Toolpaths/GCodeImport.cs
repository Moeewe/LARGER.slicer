using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Reads G-code from other slicers (Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build,
    /// Grasshopper scripts) and Weber .dxr files, turns it into a Toolpath and checks it with the
    /// same Path Check as our own paths.
    ///
    /// Understood: G0 / G1 (and G2 / G3 arcs in XY) with X Y Z E F and the tool axes A B C,
    /// G90 / G91, M82 / M83 (absolute or relative E, as Marlin: M83 stays in force through G90),
    /// G92 E (reset of E), G20 / G21, G28 (home), G4 (dwell), G10 / G11 (firmware retraction),
    /// comments ;LAYER:n (Cura and ours), ; layer n (Simplify3D, AiSync and ours), ;LAYER_CHANGE
    /// (PrusaSlicer), ;TYPE: (Cura, PrusaSlicer) and ; feature (Simplify3D).
    /// Weber DXR: N numbers, G91 XE=[24.137*P1] G90 (relative extrusion, P1 = the material factor
    /// of the machine, counted as 1), E=IC(..) / XE=IC(..), the layer marker L layer_sub.nc with
    /// V.E.GLOBAL[27] = n, and the role markers L wall_sub.nc, infill_sub, skin_sub, skirt_sub.
    /// </summary>
    public static class GCodeImport
    {
        public class Options
        {
            /// <summary>mm; 0 = estimated from E per mm and the layer height.</summary>
            public double BeadWidth;
            /// <summary>mm; 0 = estimated from the Z steps.</summary>
            public double LayerHeight;
            /// <summary>What E means. Null = E is a volume in mm3 of width x height x length (the way Cura counts with the Weber profiles).</summary>
            public MachineProfile Machine;
            /// <summary>g/cm3, for the weight.</summary>
            public double Density = 1.25;
            /// <summary>false = moves of the start and end code (homing, lifting) are left out: the path begins with the move onto the first bead and ends with the first move after the last bead.</summary>
            public bool KeepSetupMoves;
            /// <summary>Segment length for arcs (G2 / G3), mm.</summary>
            public double ArcResolution = 1.0;
        }

        /// <summary>One move of the file, from the position before it to (X, Y, Z).</summary>
        public class Move
        {
            public double X, Y, Z, A, B, C;
            /// <summary>Where the move starts.</summary>
            public double SX, SY, SZ;
            /// <summary>Extrusion of this move (positive = material out).</summary>
            public double E;
            public double F;
            public bool Rapid;
            /// <summary>A retraction, an E-only move or a firmware retraction happened before this move.</summary>
            public bool StopBefore;
            public int Line;
            public int LayerHint = int.MinValue;
            public PathRole? Type;
            public int Layer;
            public double Length;
        }

        public class Result
        {
            public Toolpath Path;
            public List<Polyline> Layers = new List<Polyline>();
            public List<string> Report = new List<string>();
            public List<string> Warnings = new List<string>();
            public LeadEngine.CheckResult Check;
            public string Format = "G-code";
            public int FeedChangesInBead, Lines, MotionLines, LayerCount, TravelMoves, TravelRuns, Starts, Retractions, RetractionsOutside, FeedChanges, F0, ZJumps, IntoBed, FlowOutliers;
            public double Length, PrintLength, TravelLength, FPrintMin, FPrintMax, FTravelMax, Speed, Minutes, FileMinutes = -1, Grams, Volume, BeadWidth, LayerHeight, FlowMin, FlowMax;
            public bool WidthEstimated, HeightEstimated, Tilted, HasRotation;
            public string LayerSource = "";
            public double FileWidth = -1, FileLayerHeight = -1;
            /// <summary>Stops inside the print: Starts - 1.</summary>
            public int Stops => Math.Max(0, Starts - 1);
        }

        private class Word { public string Key; public double Value; public int Mode; } // Mode 0 = plain, 1 = IC (relative), 2 = AC (absolute)

        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static string F(double v, string fmt = "0.##") => v.ToString(fmt, Ci);

        private static readonly Regex LayerColon = new Regex(@"^\s*LAYER\s*:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LayerWord = new Regex(@"^\s*layer\s+(-?\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TypeColon = new Regex(@"^\s*TYPE\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FeatureWord = new Regex(@"^\s*feature\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HeightHeader = new Regex(@"^\s*(?:Layer height\s*:|layer_height\s*=|layerHeight\s*,)\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WidthHeader = new Regex(@"^\s*(?:extrusion width\s*:|extrusion_width\s*=|extruderWidth\s*,|line width\s*:)\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TimeHeader = new Regex(@"^\s*(?:TIME\s*:|ProgRunTimeTotal\s*=\s*\[?)\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DxrLayerVar = new Regex(@"V\.E\.GLOBAL\[27\]\s*=\s*(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex DxrSub = new Regex(@"^\s*L\s+([A-Za-z0-9_]+)\.nc", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Reads a file. Throws with a plain message when it cannot be read.</summary>
        public static Result Read(string file, Options o)
        {
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("No file given.");
            string path = file.Trim().Trim('"');
            if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            if (!File.Exists(path)) throw new FileNotFoundException("The file " + path + " does not exist.");
            var r = Parse(File.ReadLines(path), o);
            r.Report.Insert(0, $"Read {Path.GetFileName(path)}: {r.Format}, {r.Lines} lines, {r.MotionLines} moves.");
            return r;
        }

        /// <summary>Turns the lines into a Toolpath and checks it.</summary>
        public static Result Parse(IEnumerable<string> lines, Options o)
        {
            o = o ?? new Options();
            var r = new Result();
            var moves = new List<Move>();

            // ---- state of the machine
            double x = 0, y = 0, z = 0, a = 0, b = 0, c = 0, eAbs = 0, xeAbs = 0, feed = 0, units = 1;
            bool posRel = false, m83 = false;
            int motion = 0;
            bool stopPending = false, homed = false, printed = false, g92Xyz = false;
            int layerHint = int.MinValue, dxrLayerVar = int.MinValue, layerCounter = -1;
            PathRole? type = null;
            double dwell = 0;
            var formats = new List<string>();
            int lineNo = 0;

            foreach (string raw in lines)
            {
                lineNo++;
                if (raw == null) continue;
                string line = raw;

                // ---- comments: ; to the end of the line, and ( ... )
                string comment = null;
                int semi = line.IndexOf(';');
                if (semi >= 0) { comment = line.Substring(semi + 1); line = line.Substring(0, semi); }
                int open = line.IndexOf('(');
                if (open >= 0 && line.IndexOf("IC(", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("AC(", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    int close = line.IndexOf(')', open);
                    string inner = close > open ? line.Substring(open + 1, close - open - 1) : line.Substring(open + 1);
                    comment = comment == null ? inner : inner + " " + comment;
                    line = close > open ? line.Substring(0, open) + " " + line.Substring(close + 1) : line.Substring(0, open);
                }
                if (comment != null)
                {
                    string cm = comment.Trim();
                    Match mt;
                    if ((mt = LayerColon.Match(cm)).Success) layerHint = int.Parse(mt.Groups[1].Value, Ci);
                    else if ((mt = LayerWord.Match(cm)).Success) layerHint = int.Parse(mt.Groups[1].Value, Ci);
                    else if (cm.StartsWith("LAYER_CHANGE", StringComparison.OrdinalIgnoreCase)) layerHint = ++layerCounter + 1000000;
                    else if ((mt = TypeColon.Match(cm)).Success) type = RoleOf(mt.Groups[1].Value);
                    else if ((mt = FeatureWord.Match(cm)).Success) type = RoleOf(mt.Groups[1].Value);
                    if (lineNo < 400 || r.FileLayerHeight < 0 || r.FileWidth < 0)
                    {
                        if (r.FileLayerHeight < 0 && (mt = HeightHeader.Match(cm)).Success) { double v; if (double.TryParse(mt.Groups[1].Value, NumberStyles.Float, Ci, out v) && v > 0) r.FileLayerHeight = v; }
                        if (r.FileWidth < 0 && (mt = WidthHeader.Match(cm)).Success) { double v; if (double.TryParse(mt.Groups[1].Value, NumberStyles.Float, Ci, out v) && v > 0) r.FileWidth = v; }
                        if (r.FileMinutes < 0 && (mt = TimeHeader.Match(cm)).Success) { double v; if (double.TryParse(mt.Groups[1].Value, NumberStyles.Float, Ci, out v) && v > 0) r.FileMinutes = v / 60.0; }
                    }
                    if (lineNo < 400 || formats.Count == 0) Detect(cm, formats);
                }

                // ---- Weber DXR: N numbers, variables and sub programs
                string code = line.Trim();
                if (code.Length == 0) continue;
                if ((code[0] == 'N' || code[0] == 'n') && code.Length > 1 && char.IsDigit(code[1]))
                {
                    int k = 1; while (k < code.Length && char.IsDigit(code[k])) k++;
                    code = code.Substring(k).Trim();
                    if (code.Length == 0) continue;
                }
                if (code.StartsWith("V.", StringComparison.OrdinalIgnoreCase))
                {
                    var mv = DxrLayerVar.Match(code);
                    if (mv.Success) dxrLayerVar = int.Parse(mv.Groups[1].Value, Ci);
                    if (!formats.Contains("Weber DXR")) formats.Add("Weber DXR");
                    continue;
                }
                var ms = DxrSub.Match(code);
                if (ms.Success)
                {
                    string sub = ms.Groups[1].Value.ToLowerInvariant();
                    if (sub.StartsWith("layer")) layerHint = dxrLayerVar != int.MinValue ? dxrLayerVar : ++layerCounter;
                    else if (sub.StartsWith("wall")) type = PathRole.Wall;
                    else if (sub.StartsWith("infill") || sub.StartsWith("fill")) type = PathRole.Infill;
                    else if (sub.StartsWith("skin") || sub.StartsWith("top") || sub.StartsWith("bottom")) type = PathRole.Bottom;
                    else if (sub.StartsWith("skirt") || sub.StartsWith("brim")) type = PathRole.Skirt;
                    else if (sub.StartsWith("support")) type = PathRole.Support;
                    if (!formats.Contains("Weber DXR")) formats.Add("Weber DXR");
                    continue;
                }

                // ---- the words of the line, in order (DXR switches G91 / G90 in the middle of a line)
                var words = Words(code);
                if (words.Count == 0) continue;

                bool isG92 = false, isHome = false, arc = false, cw = false;
                double? nx = null, ny = null, nz = null, na = null, nb = null, nc = null;
                double dE = 0; bool hasE = false;
                double ai = 0, aj = 0, ar = 0; bool hasIJ = false, hasR = false;
                var homeAxes = new List<char>();
                foreach (var w in words)
                {
                    string k = w.Key; double v = w.Value;
                    if (k == "G")
                    {
                        int g = (int)Math.Round(v);
                        if (Math.Abs(v - g) > 1e-6) continue;
                        switch (g)
                        {
                            case 0: motion = 0; break;
                            case 1: motion = 1; break;
                            case 2: motion = 2; break;
                            case 3: motion = 3; break;
                            case 4: break;
                            case 10: r.Retractions++; if (!printed) r.RetractionsOutside++; stopPending = true; break;
                            case 11: break;
                            case 20: units = 25.4; break;
                            case 21: units = 1; break;
                            case 28: isHome = true; break;
                            case 90: posRel = false; break;
                            case 91: posRel = true; break;
                            case 92: isG92 = true; break;
                        }
                        continue;
                    }
                    if (k == "M")
                    {
                        int mcode = (int)Math.Round(v);
                        if (mcode == 82) m83 = false; else if (mcode == 83) m83 = true;
                        continue;
                    }
                    if (k == "F")
                    {
                        if (v <= 0) r.F0++;
                        feed = v * units;
                        continue;
                    }
                    bool rel = w.Mode == 1 || (w.Mode == 0 && posRel);
                    if (isG92)
                    {
                        if (k == "E") eAbs = v;
                        else if (k == "XE") xeAbs = v;
                        else if (k == "X" || k == "Y" || k == "Z") g92Xyz = true;
                        continue;
                    }
                    if (isHome) { if (k.Length == 1 && "XYZ".IndexOf(k[0]) >= 0) homeAxes.Add(k[0]); continue; }
                    switch (k)
                    {
                        case "X": nx = rel ? (nx ?? x) + v * units : v * units; break;
                        case "Y": ny = rel ? (ny ?? y) + v * units : v * units; break;
                        case "Z": nz = rel ? (nz ?? z) + v * units : v * units; break;
                        case "A": na = rel ? (na ?? a) + v : v; break;
                        case "B": nb = rel ? (nb ?? b) + v : v; break;
                        case "C": nc = rel ? (nc ?? c) + v : v; break;
                        case "I": ai = v * units; hasIJ = true; break;
                        case "J": aj = v * units; hasIJ = true; break;
                        case "R": ar = v * units; hasR = true; break;
                        case "E":
                            {
                                bool erel = w.Mode == 1 || (w.Mode == 0 && (posRel || m83));
                                double d = erel ? v : v - eAbs;
                                eAbs = erel ? eAbs + v : v;
                                dE += d; hasE = true;
                                break;
                            }
                        case "XE":
                            {
                                double d = rel ? v : v - xeAbs;
                                xeAbs = rel ? xeAbs + v : v;
                                dE += d; hasE = true;
                                break;
                            }
                        case "P": case "S":
                            if (words.Any(q => q.Key == "G" && Math.Abs(q.Value - 4) < 1e-6)) dwell += k == "P" ? v / 1000.0 / 60.0 : v / 60.0;
                            break;
                    }
                }

                if (isHome)
                {
                    // the machine drives to its end stops: the next move starts from 0 on these axes
                    if (homeAxes.Count == 0 || homeAxes.Contains('X')) x = 0;
                    if (homeAxes.Count == 0 || homeAxes.Contains('Y')) y = 0;
                    if (homeAxes.Count == 0 || homeAxes.Contains('Z')) z = 0;
                    homed = true;
                    continue;
                }
                if (isG92) continue;
                bool axisWords = nx.HasValue || ny.HasValue || nz.HasValue || na.HasValue || nb.HasValue || nc.HasValue || hasE;
                if (!axisWords) continue;
                // a line with axis words and no G word moves with the motion that is in force
                arc = motion == 2 || motion == 3; cw = motion == 2;
                r.MotionLines++;
                double tx = nx ?? x, ty = ny ?? y, tz = nz ?? z, ta = na ?? a, tb = nb ?? b, tc = nc ?? c;
                bool rotates = Math.Abs(ta - a) > 1e-9 || Math.Abs(tb - b) > 1e-9 || Math.Abs(tc - c) > 1e-9;
                double dist = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y) + (tz - z) * (tz - z));
                if (na.HasValue || nb.HasValue || nc.HasValue) r.HasRotation = true;

                if (dist < 1e-9 && !rotates)
                {
                    // E only: a retraction or the push back after it. The bead stops here.
                    if (dE < -1e-9) { r.Retractions++; if (!printed) r.RetractionsOutside++; }
                    if (Math.Abs(dE) > 1e-9) stopPending = true;
                    continue;
                }
                if (dE < -1e-9) { r.Retractions++; if (!printed) r.RetractionsOutside++; stopPending = true; }

                var segs = new List<double[]>();
                if (arc && (hasIJ || hasR) && dist > 1e-9)
                    segs = Arc(x, y, z, tx, ty, tz, ai, aj, ar, hasR, cw, Math.Max(0.1, o.ArcResolution));
                if (segs.Count == 0) segs.Add(new[] { tx, ty, tz });
                double total = 0; { double px = x, py = y, pz = z; foreach (var s in segs) { total += Math.Sqrt((s[0] - px) * (s[0] - px) + (s[1] - py) * (s[1] - py) + (s[2] - pz) * (s[2] - pz)); px = s[0]; py = s[1]; pz = s[2]; } }
                {
                    double px = x, py = y, pz = z;
                    for (int si = 0; si < segs.Count; si++)
                    {
                        var s = segs[si];
                        double len = Math.Sqrt((s[0] - px) * (s[0] - px) + (s[1] - py) * (s[1] - py) + (s[2] - pz) * (s[2] - pz));
                        double t = segs.Count == 1 ? 1 : (si + 1.0) / segs.Count;
                        var mv = new Move
                        {
                            X = s[0], Y = s[1], Z = s[2], SX = px, SY = py, SZ = pz,
                            A = a + (ta - a) * t, B = b + (tb - b) * t, C = c + (tc - c) * t,
                            E = dE > 0 ? dE * (total > 1e-12 ? len / total : 1.0 / segs.Count) : 0,
                            F = feed, Rapid = motion == 0, StopBefore = stopPending && si == 0, Line = lineNo, LayerHint = layerHint, Type = type, Length = len,
                        };
                        moves.Add(mv);
                        if (mv.E > 1e-9) printed = true;
                        px = s[0]; py = s[1]; pz = s[2];
                    }
                }
                stopPending = false;
                x = tx; y = ty; z = tz; a = ta; b = tb; c = tc;
            }
            r.Lines = lineNo;
            if (formats.Contains("Weber DXR")) formats.Remove("AiSync / Ai Build");
            r.Format = formats.Count > 0 ? string.Join(", ", formats.Distinct()) : "G-code (no slicer named in the file)";
            if (homed) r.Warnings.Add("The file homes the machine (G28): positions right after it are taken as 0.");
            if (g92Xyz) r.Warnings.Add("The file sets X, Y or Z with G92: the positions after it may be shifted against the machine.");
            if (units != 1) r.Warnings.Add("The file is in inches (G20): converted to mm.");
            Build(r, moves, o, dwell);
            return r;
        }

        private static void Detect(string cm, List<string> formats)
        {
            string l = cm.ToLowerInvariant();
            if (l.Contains("cura_steamengine") || l.Contains("generated with cura")) { if (!formats.Contains("Cura")) formats.Add("Cura"); }
            else if (l.Contains("simplify3d")) { if (!formats.Contains("Simplify3D")) formats.Add("Simplify3D"); }
            else if (l.Contains("prusaslicer") || l.Contains("superslicer") || l.Contains("orcaslicer") || l.Contains("slic3r")) { if (!formats.Contains("PrusaSlicer")) formats.Add("PrusaSlicer"); }
            else if (l.Contains("larger.slicer")) { if (!formats.Contains("LARGER.slicer")) formats.Add("LARGER.slicer"); }
            else if (l.Contains("machine_type") && l.Contains("dxr")) { if (!formats.Contains("Weber DXR")) formats.Add("Weber DXR"); }
            else if (l.Contains("aisync") || l.Contains("ai sync") || l.Contains("ai build") || l.Contains("progruntimetotal")) { if (!formats.Contains("AiSync / Ai Build") && !formats.Contains("Weber DXR")) formats.Add("AiSync / Ai Build"); }
            else if (l.Contains("grasshopper") || l.Contains("materialfaktor") || l.Contains("fullcontrol")) { if (!formats.Contains("Grasshopper or script")) formats.Add("Grasshopper or script"); }
        }

        /// <summary>Cura, PrusaSlicer and Simplify3D feature names to our roles.</summary>
        public static PathRole RoleOf(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.Contains("skirt") || n.Contains("brim") || n.Contains("prime")) return PathRole.Skirt;
            if (n.Contains("support")) return PathRole.Support;
            if (n.Contains("skin") || n.Contains("solid") || n.Contains("top") || n.Contains("bottom") || n.Contains("bridge") || n.Contains("ironing")) return PathRole.Bottom;
            if (n.Contains("fill") || n.Contains("gap")) return PathRole.Infill;
            if (n.Contains("travel")) return PathRole.Travel;
            return PathRole.Wall;
        }

        /// <summary>Splits a code line into words: G1, X12.5, X.5, Y400., XE=[24.1*P1], E=IC(3.2), XE=AC(10).</summary>
        private static List<Word> Words(string s)
        {
            var list = new List<Word>();
            int i = 0, n = s.Length;
            while (i < n)
            {
                char ch = char.ToUpperInvariant(s[i]);
                if (ch < 'A' || ch > 'Z') { i++; continue; }
                int k0 = i;
                while (i < n && char.IsLetter(s[i])) i++;
                string key = s.Substring(k0, i - k0).ToUpperInvariant();
                while (i < n && s[i] == ' ') i++;
                int mode = 0;
                if (i < n && s[i] == '=')
                {
                    i++;
                    while (i < n && s[i] == ' ') i++;
                    if (i + 2 < n && (s[i] == 'I' || s[i] == 'i' || s[i] == 'A' || s[i] == 'a') && (s[i + 1] == 'C' || s[i + 1] == 'c') && (s[i + 2] == '(' || s[i + 2] == '['))
                    {
                        mode = char.ToUpperInvariant(s[i]) == 'I' ? 1 : 2;
                        i += 3;
                    }
                    else if (i < n && (s[i] == '[' || s[i] == '(')) i++;
                    double v;
                    int used = Number(s, i, out v);
                    if (used > 0)
                    {
                        list.Add(new Word { Key = key, Value = v, Mode = mode });
                        i += used;
                    }
                    // skip the rest of the expression ("*P1]")
                    while (i < n && s[i] != ' ' && !(char.IsLetter(s[i]) && i > 0 && s[i - 1] == ' ')) { if (s[i] == ']' || s[i] == ')') { i++; break; } i++; }
                    continue;
                }
                if (key.Length != 1 && key != "XE") { continue; } // a word like "IC" or a variable name: not a G-code word
                double value;
                int len = Number(s, i, out value);
                if (len == 0) continue;
                list.Add(new Word { Key = key, Value = value, Mode = 0 });
                i += len;
            }
            return list;
        }

        /// <summary>Reads a number at s[i] (sign, digits, one point, an exponent); returns the characters used, 0 if none.</summary>
        private static int Number(string s, int i, out double v)
        {
            v = 0;
            int j = i, n = s.Length;
            if (j < n && (s[j] == '-' || s[j] == '+')) j++;
            int digits = 0; bool dot = false;
            while (j < n && (char.IsDigit(s[j]) || (s[j] == '.' && !dot)))
            {
                if (s[j] == '.') dot = true; else digits++;
                j++;
            }
            if (digits == 0) return 0;
            if (j < n && (s[j] == 'e' || s[j] == 'E') && j + 1 < n && (char.IsDigit(s[j + 1]) || ((s[j + 1] == '-' || s[j + 1] == '+') && j + 2 < n && char.IsDigit(s[j + 2]))))
            {
                int e = j + 1; if (s[e] == '-' || s[e] == '+') e++;
                while (e < n && char.IsDigit(s[e])) e++;
                j = e;
            }
            if (!double.TryParse(s.Substring(i, j - i), NumberStyles.Float, Ci, out v)) return 0;
            return j - i;
        }

        /// <summary>G2 / G3 in the XY plane as short straight pieces; Z changes evenly (helix).</summary>
        private static List<double[]> Arc(double x0, double y0, double z0, double x1, double y1, double z1, double i, double j, double rad, bool useR, bool cw, double step)
        {
            var pts = new List<double[]>();
            double cx, cy;
            if (useR)
            {
                double dx = x1 - x0, dy = y1 - y0, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9 || Math.Abs(rad) < d / 2 - 1e-9) return pts;
                double h = Math.Sqrt(Math.Max(0, rad * rad - d * d / 4));
                double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
                // negative R = the long way round
                double sgn = (cw ? 1 : -1) * (rad < 0 ? -1 : 1);
                cx = mx + sgn * h * dy / d; cy = my - sgn * h * dx / d;
                rad = Math.Abs(rad);
            }
            else { cx = x0 + i; cy = y0 + j; rad = Math.Sqrt(i * i + j * j); }
            if (rad < 1e-9) return pts;
            double a0 = Math.Atan2(y0 - cy, x0 - cx), a1 = Math.Atan2(y1 - cy, x1 - cx);
            double sweep = a1 - a0;
            if (cw) { while (sweep >= -1e-12) sweep -= 2 * Math.PI; }
            else { while (sweep <= 1e-12) sweep += 2 * Math.PI; }
            int n = Math.Max(1, Math.Min(3600, (int)Math.Ceiling(Math.Abs(sweep) * rad / step)));
            for (int k = 1; k <= n; k++)
            {
                double t = (double)k / n, ang = a0 + sweep * t;
                if (k == n) pts.Add(new[] { x1, y1, z1 });
                else pts.Add(new[] { cx + rad * Math.Cos(ang), cy + rad * Math.Sin(ang), z0 + (z1 - z0) * t });
            }
            return pts;
        }

        // ------------------------------------------------------------------ toolpath

        private static void Build(Result r, List<Move> all, Options o, double dwellMinutes)
        {
            const double eps = 1e-9;
            int firstPrint = all.FindIndex(m => m.E > eps), lastPrint = all.FindLastIndex(m => m.E > eps);
            if (firstPrint < 0)
            {
                r.Report.Add("No extrusion found: the file has no move with E going up.");
                r.Path = new Toolpath();
                return;
            }
            // retractions before the first and after the last bead belong to the start and end code
            List<Move> moves;
            if (o.KeepSetupMoves) moves = all;
            else
            {
                int from = Math.Max(0, firstPrint - 1), to = Math.Min(all.Count - 1, lastPrint + 1);
                moves = all.GetRange(from, to - from + 1);
                int left = all.Count - moves.Count;
                if (left > 0) r.Report.Add($"Left out {left} move(s) of the start and end code (homing, lifting, moving to the start). The path begins with the move onto the first bead.");
            }
            if (moves[0].E > eps)
            {
                // the first bead starts where the machine stood before it: put that point in front
                moves.Insert(0, new Move { X = moves[0].SX, Y = moves[0].SY, Z = moves[0].SZ, F = moves[0].F, Rapid = true, Line = moves[0].Line, LayerHint = moves[0].LayerHint });
            }

            // ---- tool axis: A B C if the file has them. Else the direction square to the printing
            // moves: the beads lie in the layers, so their directions span the layer planes. Flat
            // layers give Z; layers printed at an angle give their tilt.
            Vector3d axis = Vector3d.ZAxis;
            if (!r.HasRotation)
            {
                var cov = new double[3, 3];
                for (int k = 1; k < moves.Count; k++)
                {
                    var m = moves[k]; if (m.E <= eps || m.Length < 1e-6) continue;
                    double dx = (m.X - m.SX) / m.Length, dy = (m.Y - m.SY) / m.Length, dz = (m.Z - m.SZ) / m.Length, wgt = m.Length;
                    cov[0, 0] += wgt * dx * dx; cov[0, 1] += wgt * dx * dy; cov[0, 2] += wgt * dx * dz;
                    cov[1, 1] += wgt * dy * dy; cov[1, 2] += wgt * dy * dz; cov[2, 2] += wgt * dz * dz;
                }
                cov[1, 0] = cov[0, 1]; cov[2, 0] = cov[0, 2]; cov[2, 1] = cov[1, 2];
                Eigen(cov, out double[] ev, out Vector3d[] vecs);
                Vector3d nrm = vecs[0];
                double tilt = Vector3d.VectorAngle(nrm.Z < 0 ? -nrm : nrm, Vector3d.ZAxis) * 180 / Math.PI;
                // tilted only if the moves really lie in planes (smallest spread far below the others)
                if (tilt > 10 && ev[0] < 0.05 * ev[1])
                {
                    // the layers climb along the axis: from the first beads towards the last ones
                    var firstP = moves.First(m => m.E > eps); var lastP = moves.Last(m => m.E > eps);
                    if (nrm.Z < 0) nrm = -nrm;
                    if (new Vector3d(lastP.X - firstP.X, lastP.Y - firstP.Y, lastP.Z - firstP.Z) * nrm < 0) nrm = -nrm;
                    axis = nrm; r.Tilted = true;
                    r.Report.Add($"Tilted layers: the beads lie in planes tilted {F(tilt, "0")} degrees from the bed (angled printing). The tool axis is taken square to them.");
                }
            }
            var frame = Geo.PlaneAt(Point3d.Origin, axis);
            Func<Move, double> level = m => m.X * axis.X + m.Y * axis.Y + m.Z * axis.Z;

            // ---- layers: from the layer comments, else from the steps along the tool axis, else
            // (spiral, non-planar: the nozzle climbs all the time) one layer per turn around the part
            var hints = new HashSet<int>(moves.Where(m => m.LayerHint != int.MinValue).Select(m => m.LayerHint));
            if (hints.Count >= 2)
            {
                int layer = 0, last = int.MinValue; bool any = false;
                foreach (var m in moves)
                {
                    if (m.LayerHint != int.MinValue)
                    {
                        if (any && m.LayerHint != last) layer++;
                        last = m.LayerHint; any = true;
                    }
                    m.Layer = layer;
                }
                r.LayerSource = "the layer comments of the file";
            }
            else
            {
                double cx = 0, cy = 0; int cn = 0;
                foreach (var m in moves) if (m.E > eps) { var v = new Vector3d(m.X, m.Y, m.Z); cx += v * frame.XAxis; cy += v * frame.YAxis; cn++; }
                cx /= Math.Max(1, cn); cy /= Math.Max(1, cn);
                int layer = 0, turnLayers = 0; double layerL = double.NaN, printedIn = 0, turn = 0;
                for (int k = 0; k < moves.Count; k++)
                {
                    var m = moves[k];
                    if (k > 0 && m.E > eps)
                    {
                        var p = moves[k - 1];
                        double lm = level(m), lp = level(p);
                        if (Math.Abs(lm - lp) < 0.01)
                        {
                            if (!double.IsNaN(layerL) && Math.Abs(lm - layerL) > 0.02 && printedIn > 0) { layer++; printedIn = 0; turn = 0; }
                        }
                        else
                        {
                            var vp = new Vector3d(p.X, p.Y, p.Z); var vm = new Vector3d(m.X, m.Y, m.Z);
                            double a0 = Math.Atan2(vp * frame.YAxis - cy, vp * frame.XAxis - cx), a1 = Math.Atan2(vm * frame.YAxis - cy, vm * frame.XAxis - cx), d = a1 - a0;
                            while (d > Math.PI) d -= 2 * Math.PI;
                            while (d < -Math.PI) d += 2 * Math.PI;
                            turn += d;
                            if (Math.Abs(turn) >= 2 * Math.PI) { layer++; turnLayers++; turn -= Math.Sign(turn) * 2 * Math.PI; printedIn = 0; }
                        }
                        layerL = lm;
                        printedIn += m.Length;
                    }
                    m.Layer = layer;
                }
                string along = r.Tilted ? "the steps along the tool axis" : "the Z steps";
                r.LayerSource = turnLayers > 0 ? along + " and, where the nozzle climbs all the time (spiral, non-planar), one layer per turn around the part" : along;
            }
            r.LayerCount = moves[moves.Count - 1].Layer + 1;
            var layerIdx = new SortedDictionary<int, List<int>>();
            for (int k = 0; k < moves.Count; k++)
                if (moves[k].E > eps) { if (!layerIdx.TryGetValue(moves[k].Layer, out var l)) layerIdx[moves[k].Layer] = l = new List<int>(); l.Add(k); }

            // ---- layer height: the step from layer to layer along the tool axis
            var levels = new List<double>();
            foreach (var kv in layerIdx)
            {
                var lv = kv.Value.Select(k => new Vector3d(moves[k].X, moves[k].Y, moves[k].Z) * axis).ToList();
                levels.Add(Median(lv));
            }
            var steps = new List<double>();
            for (int k = 1; k < levels.Count; k++) { double d = levels[k] - levels[k - 1]; if (d > 0.01) steps.Add(d); }
            double h = o.LayerHeight > 0 ? o.LayerHeight : (steps.Count > 0 ? Median(steps) : (r.FileLayerHeight > 0 ? r.FileLayerHeight : (levels.Count > 0 && levels[0] > 0 ? levels[0] : 1.0)));
            r.HeightEstimated = o.LayerHeight <= 0;
            r.LayerHeight = h;

            // ---- what E means, and the bead width that fits it
            var machine = o.Machine?.Duplicate() ?? new MachineProfile { Name = "E as volume (width x height x length)", RectangleSection = true, Factor = 1, Unit = "mm3" };
            double filament = machine.FilamentDiameter > 0 ? Math.PI * 0.25 * machine.FilamentDiameter * machine.FilamentDiameter : 1.0;
            var epm = new List<double>(); var epmW = new List<double>();
            for (int k = 1; k < moves.Count; k++)
                if (moves[k].E > eps && moves[k].Length > 0.2) { epm.Add(moves[k].E / moves[k].Length); epmW.Add(moves[k].Length); }
            double epmMedian = epm.Count > 0 ? Median(epm) : 0; // by count: a few broken, very long moves do not pull it
            double w;
            if (o.BeadWidth > 0) w = o.BeadWidth;
            else
            {
                double section = epmMedian * filament / Math.Max(1e-9, machine.Factor);
                if (machine.RectangleSection) w = section / h;
                else
                {
                    w = section / h + h * (1 - Math.PI / 4);
                    if (w < h) w = section / (Math.PI / 4 * h);
                }
                if (!(w > 0) || double.IsInfinity(w)) w = r.FileWidth > 0 ? r.FileWidth : 2 * h;
            }
            r.WidthEstimated = o.BeadWidth <= 0;
            r.BeadWidth = w;
            double nominal = machine.Section(w, h) * machine.Factor / filament;

            // ---- settings
            var st = new PrintSettings
            {
                BeadWidth = w, LayerHeight = h, Machine = machine, FilamentDiameter = 0, Density = o.Density > 0 ? o.Density : 1.25, Flow = 1,
            };
            var printF = new List<double>(); var printFW = new List<double>();
            double fMin = double.MaxValue, fMax = 0, tMax = 0;
            foreach (var m in moves)
            {
                if (m.F <= 0) continue;
                if (m.E > eps) { printF.Add(m.F); printFW.Add(Math.Max(1e-6, m.Length)); fMin = Math.Min(fMin, m.F); fMax = Math.Max(fMax, m.F); }
                else tMax = Math.Max(tMax, m.F);
            }
            st.Speed = printF.Count > 0 ? WeightedMedian(printF, printFW) : 3000;
            st.MinSpeed = fMin < double.MaxValue ? Math.Min(fMin, st.Speed) : st.Speed;
            st.MaxSpeed = Math.Max(st.Speed, Math.Max(fMax, tMax));
            double z0 = levels.Count > 0 ? levels[0] : h;
            if (!r.Tilted && z0 > 0.05 && Math.Abs(z0 - h) > 0.05) st.FirstLayerHeight = z0;
            r.FPrintMin = fMin < double.MaxValue ? fMin : 0; r.FPrintMax = fMax; r.FTravelMax = tMax; r.Speed = st.Speed;

            // ---- the toolpath: one point per move end; flow = E of the move / E the bead width asks for
            int firstLayer = moves.Where(m => m.E > eps).Select(m => m.Layer).DefaultIfEmpty(0).Min();
            var tp = new Toolpath { Settings = st };
            double flowMin = double.MaxValue, flowMax = 0; int outliers = 0;
            for (int k = 0; k < moves.Count; k++)
            {
                var m = moves[k];
                Vector3d n = r.HasRotation ? AxisOf(m.A, m.B, m.C) : axis;
                double flow = 0; PathRole role = PathRole.Travel;
                if (k > 0 && m.E > eps)
                {
                    double len = Math.Max(1e-9, m.Length);
                    double first = m.Layer == firstLayer && machine.FirstLayerFactor > 0 ? machine.FirstLayerFactor : 1.0;
                    flow = nominal > 0 ? m.E / len / nominal / first : 1;
                    role = m.Type.HasValue && m.Type.Value != PathRole.Travel ? m.Type.Value : PathRole.Wall;
                    if (m.Length > 0.2)
                    {
                        flowMin = Math.Min(flowMin, flow); flowMax = Math.Max(flowMax, flow);
                        if (flow < 0.2 || flow > 5) outliers++;
                    }
                }
                tp.Add(new Point3d(m.X, m.Y, m.Z), n, h, w, flow, role, m.Layer);
            }
            r.Path = tp;
            r.FlowMin = flowMin < double.MaxValue ? flowMin : 0; r.FlowMax = flowMax; r.FlowOutliers = outliers;

            // ---- numbers of the print
            int travelRun = 0; bool inTravel = false;
            double minutes = dwellMinutes; int fChanges = 0, fChangesPrint = 0; double lastF = double.NaN; int noFeed = 0;
            for (int k = 1; k < moves.Count; k++)
            {
                var m = moves[k];
                r.Length += m.Length;
                bool print = m.E > eps;
                if (print)
                {
                    r.PrintLength += m.Length;
                    bool start = k == 1 || moves[k - 1].E <= eps || m.StopBefore;
                    if (start) r.Starts++;
                    inTravel = false;
                    if (Math.Abs(m.Z - moves[k - 1].Z) > Math.Max(50, 20 * h) && (tp.Flows[k] < 0.2 || tp.Flows[k] > 5)) r.ZJumps++;
                    if (m.Z <= 0 && !r.Tilted && !r.HasRotation) r.IntoBed++;
                }
                else
                {
                    r.TravelLength += m.Length; r.TravelMoves++;
                    if (!inTravel) { travelRun++; inTravel = true; }
                }
                if (m.F > 0) minutes += m.Length / m.F; else if (m.Length > 0) noFeed++;
                if (!double.IsNaN(lastF) && Math.Abs(m.F - lastF) > 1e-6) { fChanges++; if (print && moves[k - 1].E > eps) fChangesPrint++; }
                lastF = m.F;
            }
            r.TravelRuns = travelRun;
            r.FeedChanges = fChanges; r.FeedChangesInBead = fChangesPrint;
            r.Minutes = minutes;
            var pr = PathEngine.Process(tp, false);
            r.Volume = pr.Volume;
            r.Grams = OutputFile.Grams(pr.Volume, st);

            // ---- layers for the preview
            int lay = int.MinValue; Polyline cur = null;
            for (int k = 0; k < tp.Count; k++)
            {
                if (tp.Layers[k] != lay)
                {
                    if (cur != null && cur.Count > 1) r.Layers.Add(cur);
                    cur = new Polyline();
                    if (k > 0) cur.Add(tp.Points[k - 1]); // the way into the layer
                    lay = tp.Layers[k];
                }
                cur.Add(tp.Points[k]);
            }
            if (cur != null && cur.Count > 1) r.Layers.Add(cur);

            // ---- the same check as for our own paths
            r.Check = LeadEngine.Check(tp, 0);

            // ---- report
            var rep = r.Report;
            rep.Add($"Layers: {r.LayerCount}, found from {r.LayerSource}.");
            rep.Add(r.HeightEstimated
                ? $"Layer height {F(h)} mm, estimated from the steps between the layers{(r.FileLayerHeight > 0 ? $" (the file says {F(r.FileLayerHeight)} mm)" : "")}."
                : $"Layer height {F(h)} mm, as given.");
            rep.Add(r.WidthEstimated
                ? $"Bead width {F(w)} mm, estimated from E per mm ({F(epmMedian, "0.###")} {machine.Unit} per mm) and the layer height, with {machine.Name}{(r.FileWidth > 0 ? $"; the file says {F(r.FileWidth)} mm" : "")}."
                : $"Bead width {F(w)} mm, as given; at this width the file extrudes {F(flowMin < double.MaxValue ? Median(Enumerable.Range(1, tp.Count - 1).Where(i => tp.Flows[i] > 0).Select(i => tp.Flows[i]).ToList()) * 100 : 0, "0")} % of the calculated amount.");
            if (r.WidthEstimated && r.FileWidth > 0 && Math.Abs(w - r.FileWidth) > 0.25 * r.FileWidth)
                r.Warnings.Add($"The estimated bead width ({F(w)} mm) is far from the width the file names ({F(r.FileWidth)} mm). Connect the Machine Profile the file was made for, or give the Bead Width.");
            if (w / h > 8 || w / h < 1)
                r.Warnings.Add($"Bead {F(w)} x {F(h)} mm looks wrong. E may be counted differently: connect the Machine Profile the file was made for (filament diameter, factor), or give Bead Width and Layer Height.");
            rep.Add($"Length {F(r.Length / 1000, "0.00")} m: {F(r.PrintLength / 1000, "0.00")} m printed, {F(r.TravelLength / 1000, "0.00")} m without extrusion.");
            rep.Add(r.Starts <= 1
                ? "One continuous line: the extrusion starts once and never stops (single-toolpath score 1)."
                : $"{r.Starts} starts and {r.Stops} stops of the extrusion: the print is {r.Starts} separate lines (single-toolpath score 1/{r.Starts} = {F(1.0 / r.Starts, "0.####")}). Every stop and start leaves a mark on a pellet extruder.");
            rep.Add(r.TravelMoves == 0 ? "Travel: no moves without extrusion." : $"Travel: {r.TravelMoves} move(s) without extrusion in {r.TravelRuns} run(s), {F(r.TravelLength, "0")} mm.");
            rep.Add(r.Retractions == 0 ? "Retractions: none." : $"Retractions: {r.Retractions}{(r.RetractionsOutside > 0 ? $" ({r.RetractionsOutside} before the first bead)" : "")}.");
            rep.Add(r.FPrintMax > 0
                ? $"Speed: printing at {F(r.FPrintMin / 60, "0.#")} to {F(r.FPrintMax / 60, "0.#")} mm/s (mostly {F(st.Speed / 60, "0.#")} mm/s = F{F(st.Speed, "0")}), travel up to {F(r.FTravelMax / 60, "0.#")} mm/s; F changes {r.FeedChanges} time(s), {r.FeedChangesInBead} of them in the middle of a bead."
                : "Feedrate: no F in the file.");
            if (r.F0 > 0) { r.Warnings.Add($"F0: {r.F0} line(s) set the feedrate to 0. A move with F0 never finishes: the machine stops there."); }
            else rep.Add("No F0: every feedrate is above 0.");
            if (noFeed > 0) r.Warnings.Add($"{noFeed} move(s) run without a feedrate (none set yet, or F0).");
            rep.Add(r.FlowMax > 0 ? $"Flow: {F(r.FlowMin * 100, "0")} to {F(r.FlowMax * 100, "0")} % of the bead width x layer height.{(r.FlowOutliers > 0 ? $" {r.FlowOutliers} move(s) are below 20 % or above 500 %." : "")}" : "Flow: -");
            if (r.ZJumps > 0)
                r.Warnings.Add($"Z JUMPS: {r.ZJumps} printing move(s) jump more than {F(Math.Max(50, 20 * h), "0")} mm in Z with far too little or too much material for their length. Such numbers are usually broken (for example .566 written as 566). Do not print the file before this is clear.");
            if (r.IntoBed > 0) r.Warnings.Add($"INTO THE BED: {r.IntoBed} printing move(s) at Z 0 or below.");
            foreach (var l in r.Check.Report) rep.Add("Check: " + l);
            rep.Add($"Time about {F(r.Minutes, "0")} min from the feedrates (without acceleration){(r.FileMinutes > 0 ? $"; the file says {F(r.FileMinutes, "0")} min" : "")}.");
            rep.Add($"Weight about {F(r.Grams, "0")} g at {F(st.Density)} g/cm3 ({F(r.Volume / 1000, "0")} cm3).");
            if (r.HasRotation) rep.Add("Tool axes: the file has A, B, C. They are read as turns about X, Y and Z (in this order, degrees) and give the tool axis of every point.");
            foreach (var wmsg in r.Warnings) rep.Add("Warning: " + wmsg);
        }

        /// <summary>Tool axis from A B C in degrees: turn Z about X by A, then about Y by B, then about Z by C.</summary>
        public static Vector3d AxisOf(double aDeg, double bDeg, double cDeg)
        {
            double A = aDeg * Math.PI / 180, B = bDeg * Math.PI / 180, C = cDeg * Math.PI / 180;
            double sa = Math.Sin(A), ca = Math.Cos(A), sb = Math.Sin(B), cb = Math.Cos(B), sc = Math.Sin(C), cc = Math.Cos(C);
            return new Vector3d(ca * sb * cc + sa * sc, ca * sb * sc - sa * cc, ca * cb);
        }

        private static double Median(List<double> v)
        {
            if (v == null || v.Count == 0) return 0;
            var s = v.OrderBy(q => q).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        }

        private static double WeightedMedian(List<double> v, List<double> w)
        {
            var idx = Enumerable.Range(0, v.Count).OrderBy(i => v[i]).ToList();
            double total = w.Sum(), acc = 0;
            foreach (int i in idx) { acc += w[i]; if (acc >= 0.5 * total) return v[i]; }
            return v[idx[idx.Count - 1]];
        }

        /// <summary>Eigenvalues (smallest first) and eigenvectors of a symmetric 3x3 matrix (Jacobi).</summary>
        private static void Eigen(double[,] src, out double[] values, out Vector3d[] vectors)
        {
            var m = (double[,])src.Clone();
            var vec = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(m[0, 1]) + Math.Abs(m[0, 2]) + Math.Abs(m[1, 2]);
                if (off < 1e-14 * (Math.Abs(m[0, 0]) + Math.Abs(m[1, 1]) + Math.Abs(m[2, 2]) + 1e-300)) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(m[p, q]) < 1e-300) continue;
                        double theta = (m[q, q] - m[p, p]) / (2 * m[p, q]);
                        double t = theta == 0 ? 1 : Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                        for (int k = 0; k < 3; k++) { double a = m[k, p], b = m[k, q]; m[k, p] = cs * a - sn * b; m[k, q] = sn * a + cs * b; }
                        for (int k = 0; k < 3; k++) { double a = m[p, k], b = m[q, k]; m[p, k] = cs * a - sn * b; m[q, k] = sn * a + cs * b; }
                        for (int k = 0; k < 3; k++) { double a = vec[k, p], b = vec[k, q]; vec[k, p] = cs * a - sn * b; vec[k, q] = sn * a + cs * b; }
                    }
            }
            var order = new[] { 0, 1, 2 }.OrderBy(k => m[k, k]).ToArray();
            values = order.Select(k => m[k, k]).ToArray();
            vectors = order.Select(k => { var v = new Vector3d(vec[0, k], vec[1, k], vec[2, k]); v.Unitize(); return v; }).ToArray();
        }
    }
}
