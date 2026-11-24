using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// The same as PathReshape for a finished file: G-code or a Weber .dxr. The moves are read, bent, and only
    /// the numbers that change are written back into their lines: Z, E, for a tilted tool A B C, and F where the
    /// flow is limited. Every other line (start code, end code, skirt, comments, layer marks, speeds) stays as it is,
    /// byte for byte. E is worked out again from the new distance to the lap below; what the file itself puts out
    /// per mm3 (its calibration) is kept, line by line. A file that is not understood is refused.
    /// </summary>
    public static class ReshapeCode
    {
        public class Options
        {
            public ReshapeOptions Shape = new ReshapeOptions();
            /// <summary>Which way (see ReshapeVariant); laps that are cut are not possible in a finished file.</summary>
            public ReshapeVariant Variant = ReshapeVariant.FannedSquare;
            /// <summary>True: the Tilt and ExtraPasses of Shape count instead of Variant.</summary>
            public bool Plain;
            /// <summary>mm; 0 = from the file (header, or E per mm).</summary>
            public double BeadWidth, LayerHeight;
            /// <summary>0 = automatic, 1 = E counts width x height, 2 = E counts a bead with round sides.</summary>
            public int Section;
            /// <summary>What E means, for the bead width; null = known from the file or assumed.</summary>
            public MachineProfile Machine;
            /// <summary>No move may put out more per second than the largest of the file times this; 0 = off (F is not touched).</summary>
            public double MaxFlowFactor;
            /// <summary>A line of comment that says the file was reshaped.</summary>
            public bool Mark = true;
        }

        public class Result
        {
            public string Refused;
            /// <summary>The new file, with the line ends of the old one.</summary>
            public string Text;
            public List<string> Lines = new List<string>();
            public List<string> Report = new List<string>(), Warnings = new List<string>();
            public PathReshape.Result Shape;
            public Toolpath Before, After;
            public bool Dxr, AbsoluteE;
            public int LinesIn, LinesOut, Changed, Inserted, Kept, HeaderChanged, Slowed;
            public double EBefore, EAfter, Calibration, BeadWidth, LayerHeight, EPerMm;
            /// <summary>Largest difference between what was meant and what the written file says, mm and share of E.</summary>
            public double CheckMm, CheckE;
        }

        private class Word { public string Key; public double V; public int From, To, Dec; public int Mode; } // Mode 0 plain, 1 IC, 2 AC; From..To = the number in the line

        private class Line
        {
            public string Raw, Eol; public List<Word> W; public int Code; // Code = length of the part before the comment
            public Move M; public List<Move> Arc; public bool EOnly; public double EOnlyDelta; public int EWord = -1; public bool ResetE, RelE; public double ResetTo; public int N = -1; public bool Relative, Homes;
        }

        private class Move
        {
            public int Line; public double X, Y, Z, A, B, C, SX, SY, SZ, SA, SB, SC, E, F; public bool Rapid, Arc, ArcEnd; public int Hint = int.MinValue, Layer; public PathRole? Type; public int Break; public double Length;
        }

        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static readonly Regex RxLayer = new Regex(@"^\s*LAYER\s*:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxLayerWord = new Regex(@"^\s*layer\s+(-?\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxType = new Regex(@"^\s*(?:TYPE\s*:|feature)\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxVar = new Regex(@"V\.E\.GLOBAL\[27\]\s*=\s*(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex RxSub = new Regex(@"^(?:N\d+\s*)?L\s+([A-Za-z0-9_]+)(?:\.nc)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBead = new Regex(@"\bbead\s+([0-9.]+)\s*x\s*([0-9.]+)\s*mm", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxWidth = new Regex(@"^\s*;?\s*(?:extrusion width\s*:|extrusion_width\s*=|extruderWidth\s*,|line width\s*:)\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Reads a file and reshapes it. The result is refused, with the reason, when the file cannot be read or understood.</summary>
        public static Result File_(string file, ShapeField field, Options o)
        {
            string path = (file ?? "").Trim().Trim('"');
            if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            if (!File.Exists(path)) return new Result { Refused = "The file " + path + " does not exist.", Report = { "REFUSED: The file " + path + " does not exist." } };
            // Latin-1 keeps every byte as it is, whatever the file was written in
            return Run(Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path)), field, o, Path.GetExtension(path));
        }

        /// <summary>Writes a result so that every untouched byte of the old file comes back.</summary>
        public static void Save(string path, Result r) => File.WriteAllBytes(path, Encoding.GetEncoding(28591).GetBytes(r.Text ?? ""));

        // ------------------------------------------------------------------ reading

        private static List<Line> Split(string text)
        {
            var l = new List<Line>(); int i = 0, n = text.Length;
            while (i < n)
            {
                int j = i; while (j < n && text[j] != '\n' && text[j] != '\r') j++;
                int e = j; if (e < n) e += text[e] == '\r' && e + 1 < n && text[e + 1] == '\n' ? 2 : 1;
                l.Add(new Line { Raw = text.Substring(i, j - i), Eol = text.Substring(j, e - j) });
                i = e;
            }
            return l;
        }

        private static int Number(string s, int i, int end, out double v, out int dec)
        {
            v = 0; dec = 0; int j = i; if (j < end && (s[j] == '-' || s[j] == '+')) j++;
            int d = 0; bool dot = false;
            while (j < end && (s[j] >= '0' && s[j] <= '9' || s[j] == '.' && !dot)) { if (s[j] == '.') dot = true; else { d++; if (dot) dec++; } j++; }
            if (d == 0) return 0;
            return double.TryParse(s.Substring(i, j - i), NumberStyles.Float, Ci, out v) ? j - i : 0;
        }

        private static bool Letter(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';

        private static List<Word> Words(string s, int end)
        {
            var list = new List<Word>(); int i = 0;
            while (i < end)
            {
                if (s[i] == '(') { while (i < end && s[i] != ')') i++; i++; continue; }
                if (!Letter(s[i])) { i++; continue; }
                int k0 = i; while (i < end && Letter(s[i])) i++;
                string key = s.Substring(k0, i - k0).ToUpperInvariant(); int mode = 0;
                int j = i; while (j < end && s[j] == ' ') j++;
                if (j < end && s[j] == '=')
                {
                    j++; while (j < end && s[j] == ' ') j++;
                    if (j + 2 < end && (s[j] == 'I' || s[j] == 'i' || s[j] == 'A' || s[j] == 'a') && (s[j + 1] == 'C' || s[j + 1] == 'c') && (s[j + 2] == '(' || s[j + 2] == '[')) { mode = s[j] == 'I' || s[j] == 'i' ? 1 : 2; j += 3; }
                    else if (j < end && s[j] == '[') j++;
                    int u = Number(s, j, end, out double v, out int dec);
                    if (u > 0) { list.Add(new Word { Key = key, V = v, From = j, To = j + u, Dec = dec, Mode = mode }); j += u; }
                    while (j < end && s[j] != ']' && s[j] != ')' && s[j] != ' ') j++;
                    i = j; continue;
                }
                if (key.Length != 1 && key != "XE") continue;
                int us = Number(s, j, end, out double val, out int de);
                if (us == 0) { if (key.Length == 1) list.Add(new Word { Key = key, V = double.NaN, From = j, To = j }); continue; }
                list.Add(new Word { Key = key, V = val, From = j, To = j + us, Dec = de }); i = j + us;
            }
            return list;
        }

        private class Program
        {
            public List<Line> L; public List<Move> Moves = new List<Move>(); public bool Dxr, AbsE, Abc, AnyE; public string Why; public int Strange, Code; public double Bead = 0, LayerH = 0; public bool RoundHeader, Larger, Ginger; public int FirstPrint = -1, LastPrint = -1;
        }

        private static Program Read(string text, string ext)
        {
            var p = new Program { L = Split(text) };
            double x = 0, y = 0, z = 0, a = 0, b = 0, c = 0, feed = 0, e = 0, xe = 0; bool rel = false, m83 = false; int motion = 0, hint = int.MinValue, var = int.MinValue, count = -1, gap = 0; PathRole? type = null;
            bool knowX = false, knowY = false, knowZ = false;
            void No(int line, string s) { if (p.Why == null) p.Why = Inv.F($"Line {line + 1}: ") + s; }
            for (int li = 0; li < p.L.Count; li++)
            {
                var ln = p.L[li]; string s = ln.Raw; int semi = s.IndexOf(';'); ln.Code = semi < 0 ? s.Length : semi;
                if (semi >= 0)
                {
                    string cm = s.Substring(semi + 1).Trim(); Match m;
                    if ((m = RxLayer.Match(cm)).Success || (m = RxLayerWord.Match(cm)).Success) hint = int.Parse(m.Groups[1].Value, Ci);
                    else if (cm.StartsWith("LAYER_CHANGE", StringComparison.OrdinalIgnoreCase)) hint = ++count + 1000000;
                    else if ((m = RxType.Match(cm)).Success) type = GCodeImport.RoleOf(m.Groups[1].Value);
                    if (cm.IndexOf("machine_type", StringComparison.OrdinalIgnoreCase) >= 0 && cm.IndexOf("DXR", StringComparison.OrdinalIgnoreCase) >= 0) p.Dxr = true;
                    if (cm.StartsWith("LARGER.slicer", StringComparison.OrdinalIgnoreCase)) { p.Larger = true; if (cm.IndexOf("Ginger One", StringComparison.OrdinalIgnoreCase) >= 0) p.Ginger = true; }
                    if (li < 400 && p.Bead <= 0 && (m = RxBead.Match(cm)).Success) { double.TryParse(m.Groups[1].Value, NumberStyles.Float, Ci, out p.Bead); double.TryParse(m.Groups[2].Value, NumberStyles.Float, Ci, out p.LayerH); if (cm.IndexOf("Ginger", StringComparison.OrdinalIgnoreCase) >= 0 || cm.IndexOf("DXR", StringComparison.OrdinalIgnoreCase) >= 0) p.RoundHeader = false; else p.RoundHeader = p.Larger; }
                    if (li < 400 && p.Bead <= 0 && (m = RxWidth.Match(cm)).Success) double.TryParse(m.Groups[1].Value, NumberStyles.Float, Ci, out p.Bead);
                }
                string code = s.Substring(0, ln.Code).Trim();
                if (code.Length == 0) { gap++; continue; }
                p.Code++;
                string body = code; if ((body[0] == 'N' || body[0] == 'n') && body.Length > 1 && char.IsDigit(body[1])) { int k = 1; while (k < body.Length && char.IsDigit(body[k])) k++; if (int.TryParse(body.Substring(1, k - 1), NumberStyles.Integer, Ci, out int nn)) ln.N = nn; body = body.Substring(k).Trim(); }
                if (body.Length == 0) { gap++; continue; }
                if (body.StartsWith("V.", StringComparison.OrdinalIgnoreCase)) { var mv = RxVar.Match(body); if (mv.Success) var = int.Parse(mv.Groups[1].Value, Ci); p.Dxr = true; gap++; continue; }
                var ms = RxSub.Match(body);
                if (ms.Success)
                {
                    string sub = ms.Groups[1].Value.ToLowerInvariant();
                    if (sub.StartsWith("layer")) hint = var != int.MinValue ? var : ++count;
                    else if (sub.StartsWith("wall")) type = PathRole.Wall; else if (sub.StartsWith("infill") || sub.StartsWith("fill")) type = PathRole.Infill;
                    else if (sub.StartsWith("skin") || sub.StartsWith("top") || sub.StartsWith("bottom")) type = PathRole.Bottom;
                    else if (sub.StartsWith("skirt") || sub.StartsWith("brim")) type = PathRole.Skirt; else if (sub.StartsWith("support")) type = PathRole.Support;
                    gap++; continue;
                }
                if (Regex.IsMatch(body, @"^(PTP|LIN|SPTP|SLIN|DEF|CIRC)\b")) { No(li, "this is a KRL robot program (PTP / LIN). Reshape the toolpath before Robot Program, or give the .dxr or G-code file."); return p; }
                ln.W = Words(s, ln.Code);
                if (ln.W.Count == 0 || "GMTXYZEFABCSNPIJRKDHLOQUVW".IndexOf(ln.W[0].Key[0]) < 0 || ln.W[0].Key == "N" && ln.W.Count == 1) { p.Strange++; gap++; continue; }
                double? nx = null, ny = null, nz = null, na = null, nb = null, nc = null; double dE = 0; bool hasE = false, g92 = false, home = false, hasIJ = false, hasR = false, isM = false, g4 = false; double ai = 0, aj = 0, ar = 0; int known = 0;
                foreach (var w in ln.W)
                {
                    double v = w.V; string k = w.Key;
                    if (k == "N") continue;
                    if (w.To + 1 < ln.Code && s[w.To] == ',' && char.IsDigit(s[w.To + 1])) No(li, "a number with a comma (" + k + "): the file has to use points.");
                    if (k == "G")
                    {
                        known++;
                        if (double.IsNaN(v)) { No(li, "a G word without a number."); continue; }
                        double g = Math.Round(v, 3);
                        if (g == 0 || g == 1 || g == 2 || g == 3) motion = (int)g;
                        else if (g == 90) rel = false; else if (g == 91) rel = true; else if (g == 92) g92 = true; else if (g == 28) home = true; else if (g == 4) g4 = true;
                        else if (g == 20) No(li, "the file is in inches (G20). Convert it to mm first.");
                        else if (g == 5 || g == 6 || g == 18 || g == 19 || g == 68 || g == 51 || g == 81 || g == 112 || g == 12.1) No(li, Inv.F($"G{g:0.#} is not understood (splines, other arc planes, turned or scaled axes)."));
                        continue;
                    }
                    if (k == "M" || k == "T") { known++; if (k == "M") { isM = true; if (v == 82) m83 = false; else if (v == 83) m83 = true; } continue; }
                    if (isM || g4) continue; // the words behind M and G4 are no positions
                    if (k == "F") { if (!double.IsNaN(v)) feed = v; known++; continue; }
                    if (g92) { if (k == "E") { e = double.IsNaN(v) ? 0 : v; ln.ResetE = true; ln.ResetTo = e; } else if (k == "XE") xe = v; else if ("XYZ".IndexOf(k[0]) >= 0 && k.Length == 1) No(li, "the file shifts X, Y or Z with G92: where the part stands is not clear."); continue; }
                    if (home) continue;
                    if (double.IsNaN(v)) { if ("XYZEABC".IndexOf(k[0]) >= 0) No(li, k + " without a number."); continue; }
                    bool r = w.Mode == 1 || w.Mode == 0 && rel;
                    switch (k)
                    {
                        case "X": nx = r ? x + v : v; known++; break;
                        case "Y": ny = r ? y + v : v; known++; break;
                        case "Z": nz = r ? z + v : v; known++; break;
                        case "A": na = r ? a + v : v; known++; break;
                        case "B": nb = r ? b + v : v; known++; break;
                        case "C": nc = r ? c + v : v; known++; break;
                        case "I": ai = v; hasIJ = true; known++; break;
                        case "J": aj = v; hasIJ = true; known++; break;
                        case "R": ar = v; hasR = true; known++; break;
                        case "E": { bool er = w.Mode == 1 || w.Mode == 0 && (rel || m83); ln.RelE = er; double d = er ? v : v - e; e = er ? e + v : v; dE += d; hasE = true; if (!er) p.AbsE = true; ln.EWord = ln.W.IndexOf(w); known++; break; }
                        case "XE": { double d = r ? v : v - xe; xe = r ? xe + v : v; dE += d; hasE = true; p.Dxr = true; ln.EWord = ln.W.IndexOf(w); if (!r) No(li, "XE is absolute here; Weber files write it relative (G91 XE=[..*P1] G90)."); known++; break; }
                    }
                    if (rel && (k == "X" || k == "Y" || k == "Z")) ln.Relative = true;
                }
                if (known == 0) { p.Strange++; gap++; continue; }
                if (home) { x = y = z = 0; ln.Homes = true; gap++; continue; }
                if (g92 || isM || g4) { gap++; continue; }
                if (!(nx.HasValue || ny.HasValue || nz.HasValue || na.HasValue || nb.HasValue || nc.HasValue || hasE || motion >= 2 && hasIJ)) { gap++; continue; }
                if (na.HasValue || nb.HasValue || nc.HasValue) p.Abc = true;
                double tx = nx ?? x, ty = ny ?? y, tz = nz ?? z, ta = na ?? a, tb = nb ?? b, tc = nc ?? c;
                double dist = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y) + (tz - z) * (tz - z));
                bool arc = motion >= 2 && (hasIJ || hasR);
                if (dist < 1e-9 && !arc && Math.Abs(ta - a) + Math.Abs(tb - b) + Math.Abs(tc - c) < 1e-9)
                {
                    if (hasE) { ln.EOnly = true; ln.EOnlyDelta = dE; }
                    if (hasE) gap++;
                    knowX |= nx.HasValue; knowY |= ny.HasValue; knowZ |= nz.HasValue;
                    continue;
                }
                var segs = new List<double[]>();
                if (arc) segs = GCodeImport.Arc(x, y, z, tx, ty, tz, ai, aj, ar, hasR, motion == 2, 1.0);
                if (segs.Count == 0) { arc = false; segs.Add(new[] { tx, ty, tz }); }
                double tot = 0; { double px = x, py = y, pz = z; foreach (var q in segs) { tot += Math.Sqrt((q[0] - px) * (q[0] - px) + (q[1] - py) * (q[1] - py) + (q[2] - pz) * (q[2] - pz)); px = q[0]; py = q[1]; pz = q[2]; } }
                { double px = x, py = y, pz = z; if (arc) ln.Arc = new List<Move>();
                  for (int q = 0; q < segs.Count; q++)
                  {
                    var sg = segs[q]; double len = Math.Sqrt((sg[0] - px) * (sg[0] - px) + (sg[1] - py) * (sg[1] - py) + (sg[2] - pz) * (sg[2] - pz)), t = (q + 1.0) / segs.Count;
                    var mv = new Move { Line = li, X = sg[0], Y = sg[1], Z = sg[2], SX = px, SY = py, SZ = pz, A = a + (ta - a) * t, B = b + (tb - b) * t, C = c + (tc - c) * t, SA = a, SB = b, SC = c, E = dE * (tot > 1e-12 ? len / tot : 1.0 / segs.Count), F = feed, Rapid = motion == 0, Arc = arc, ArcEnd = arc && q == segs.Count - 1, Hint = hint, Type = type, Break = gap, Length = len };
                    if (arc) ln.Arc.Add(mv); else ln.M = mv;
                    if (mv.E > 1e-9 && ln.Relative) No(li, "printing moves with relative positions (G91 X Y Z). The file has to be in absolute positions (G90).");
                    if (mv.E > 1e-9) { p.AnyE = true; if (p.FirstPrint < 0) p.FirstPrint = p.Moves.Count; p.LastPrint = p.Moves.Count; if (!(knowX || nx.HasValue) || !(knowY || ny.HasValue)) No(li, "the file prints before it says where the nozzle is (X, Y)."); }
                    p.Moves.Add(mv); px = sg[0]; py = sg[1]; pz = sg[2];
                  } }
                gap = 0;
                x = tx; y = ty; z = tz; a = ta; b = tb; c = tc; knowX |= nx.HasValue; knowY |= ny.HasValue; knowZ |= nz.HasValue;
            }
            return p;
        }

        // ------------------------------------------------------------------ the job

        public static Result Run(string text, ShapeField field, Options o, string ext = "")
        {
            o = o ?? new Options(); var r = new Result();
            Result Refuse(string why) { r.Refused = why; r.Text = null; r.Report.Insert(0, "REFUSED: " + why); return r; }
            if (string.IsNullOrWhiteSpace(text)) return Refuse("The file is empty.");
            var p = Read(text, ext);
            r.LinesIn = p.L.Count; r.Dxr = p.Dxr; r.AbsoluteE = p.AbsE;
            if (p.Code > 0 && p.Strange > 0.02 * p.Code + 5) return Refuse(Inv.F($"This is not G-code or a .dxr file that is understood: {p.Strange} of {p.Code} lines are something else."));
            if (!p.AnyE) return Refuse("No extrusion found: the file has no move with E (or XE) going up.");
            if (p.Why != null) return Refuse(p.Why);
            if (p.Abc && !p.Dxr && p.Moves.Any(m => Math.Abs(m.A) + Math.Abs(m.B) + Math.Abs(m.C) > 1e-6)) return Refuse("The G-code turns the tool (A B C). How this machine counts its angles is not known; only Weber .dxr files may have them.");
            if (!o.Plain && o.Variant == ReshapeVariant.CutLaps) return Refuse("Laps that are cut need another order of the moves. That is possible with the toolpath (Reshape Path), not in a finished file.");
            bool tilt = o.Plain ? o.Shape.Tilt : ReshapeVariants.Tilts(o.Variant);
            if (tilt && !p.Dxr) { tilt = false; r.Warnings.Add("G-code of a gantry has no tool angles: the nozzle stays vertical. (A tilted tool is written for .dxr files only.)"); }

            // ---- the moves that matter: from the one before the first bead, up to the last absolute move after the last bead
            int from = Math.Max(0, p.FirstPrint - 1), to = p.LastPrint;
            while (to + 1 < p.Moves.Count)
            {
                bool clean = true;
                for (int l = p.Moves[to].Line + 1; l <= p.Moves[to + 1].Line && clean; l++) clean = !p.L[l].Relative && !p.L[l].Homes && !p.L[l].ResetE;
                if (!clean) break;
                to++;
            }
            var mv = p.Moves.GetRange(from, to - from + 1);
            bool lead = p.FirstPrint == 0; // the very first move prints already
            string layers = Layers(mv);
            // ---- layer height, E per mm, bead width
            var levels = mv.Where(m => m.E > 1e-9).GroupBy(m => m.Layer).OrderBy(g => g.Key).Select(g => Median(g.Select(m => m.Z).ToList())).ToList();
            var steps = new List<double>(); for (int k = 1; k < levels.Count; k++) if (levels[k] - levels[k - 1] > 0.02) steps.Add(levels[k] - levels[k - 1]);
            double h = o.LayerHeight > 0 ? o.LayerHeight : steps.Count > 0 ? Median(steps) : p.LayerH;
            if (!(h > 0)) return Refuse("No layers found: all beads lie at one height.");
            int firstLayer = mv.Where(m => m.E > 1e-9).Min(m => m.Layer);
            var epm = mv.Where(m => m.E > 1e-9 && m.Length > 0.2 && m.Layer > firstLayer).Select(m => m.E / m.Length).ToList();
            if (epm.Count < 10) return Refuse("Too few beads above the first layer.");
            double K = Median(epm); r.EPerMm = K; r.LayerHeight = h;
            double w = o.BeadWidth, factor = o.Machine != null ? o.Machine.Factor / (o.Machine.FilamentDiameter > 0 ? Math.PI / 4 * o.Machine.FilamentDiameter * o.Machine.FilamentDiameter : 1) : p.Dxr ? 1.128 : p.Ginger ? 0.21 : 0;
            string wFrom = "as given";
            if (!(w > 0) && p.Bead > 0) { w = p.Bead; wFrom = "from the file"; }
            if (!(w > 0) && factor > 0) { w = K / (factor * h); wFrom = Inv.F($"from E per mm ({K:0.###}) and the factor {factor:0.###}"); }
            if (!(w > 0.8 * h && w < 12 * h)) { if (w > 0) r.Warnings.Add(Inv.F($"The bead width {w:0.##} mm ({wFrom}) does not go with the layer height {h:0.##} mm: 2.5 x the layer height is assumed. Give the Bead Width.")); w = 2.5 * h; wFrom = "assumed: 2.5 x layer height, give it if it is different"; }
            r.BeadWidth = w; r.Calibration = K / (w * h);
            bool round = o.Section == 2 || o.Section == 0 && (o.Machine != null ? !o.Machine.RectangleSection : p.RoundHeader);

            // ---- the toolpath
            var fs = mv.Where(m => m.E > 1e-9 && m.F > 0).Select(m => m.F).ToList(); double speed = fs.Count > 0 ? Median(fs) : 3000;
            var st = new PrintSettings { BeadWidth = w, LayerHeight = h, Speed = speed, MinSpeed = Math.Min(300, speed), MaxSpeed = Math.Max(speed, 12000), Machine = new MachineProfile { Name = "file", RectangleSection = !round, Factor = 1 } };
            st.Machine.Factor = K / Math.Max(1e-9, st.Machine.Section(w, h));
            double z0 = mv.Where(m => m.E > 1e-9).Min(m => m.Z);
            if (z0 > 0.05) st.FirstLayerHeight = Math.Min(z0, 3 * h);
            var tp = new Toolpath { Settings = st }; var of = new List<int>(); // path point -> move (index in mv)
            Vector3d Axis(double A, double B, double C) => Math.Abs(A) + Math.Abs(B) + Math.Abs(C) < 1e-9 ? Vector3d.ZAxis : Frame.FromAbc(Point3d.Origin, A, B, C).Z;
            if (lead || mv[0].E > 1e-9) { tp.Add(new Point3d(mv[0].SX, mv[0].SY, mv[0].SZ), Axis(mv[0].SA, mv[0].SB, mv[0].SC), h, w, 0, PathRole.Travel, mv[0].Layer); of.Add(-1); }
            for (int k = 0; k < mv.Count; k++)
            {
                var m = mv[k]; bool print = m.E > 1e-9;
                double hi = print && m.Length > 0.05 ? h * Math.Max(0.02, Math.Min(3, m.E / m.Length / K)) : h;
                if (print && m.Layer == firstLayer) hi = st.FirstLayer;
                tp.Add(new Point3d(m.X, m.Y, m.Z), Axis(m.A, m.B, m.C), hi, w, print ? 1 : 0, print ? (m.Type.HasValue && m.Type.Value != PathRole.Travel ? m.Type.Value : PathRole.Wall) : PathRole.Travel, m.Layer);
                of.Add(k);
            }
            r.Before = tp;
            // ---- is it what it seems? layers one above the other
            string bad = Sound(tp, p, h);
            if (bad != null) return Refuse(bad);
            var so = Copy(o.Shape); so.Tilt = tilt; so.MaxFlow = 0;
            so.BreakBefore = new bool[tp.Count];
            for (int i = 1; i < tp.Count; i++) so.BreakBefore[i] = of[i] >= 0 && (mv[of[i]].Break > 0 || mv[of[i]].Arc);
            PathReshape.Result s;
            s = o.Plain ? PathReshape.Apply(tp, field, so) : ReshapeVariants.Run(tp, field, tilt || !ReshapeVariants.Tilts(o.Variant) ? o.Variant : ReshapeVariants.Upright(o.Variant), so);
            r.Shape = s;
            if (s.Refused != null) return Refuse(s.Refused);
            r.After = s.Path;

            // ---- back into the file
            r.EBefore = mv.Sum(m => Math.Max(0, m.E));
            double capRate = 0;
            if (o.MaxFlowFactor > 0) { foreach (var m in mv) if (m.E > 1e-9 && m.Length > 0.5 && m.F > 0 && m.Type != PathRole.Skirt) capRate = Math.Max(capRate, m.E / m.Length * m.F); capRate *= o.MaxFlowFactor; }
            var outL = new List<string>(); var outEol = new List<string>(); string eol = p.L.Count > 0 && p.L[0].Eol.Length > 0 ? p.L[0].Eol : "\n";
            var expect = new List<double[]>(); // x y z e of every written move, for the check
            int next = 0; double eShift = 0; // absolute E: new - old
            double mx = double.NaN, my = double.NaN, mz = double.NaN, ma = 0, mb = 0, mc = 0, mf = double.NaN, eAbsNew = 0, eAbsOld = 0; int lastN = -1;
            var firstLine = new HashSet<int>(); for (int i = 0; i < tp.Count; i++) if (of[i] >= 0) firstLine.Add(mv[of[i]].Line);
            bool identical = s.Identity;
            void Emit(string raw, string e) { outL.Add(raw); outEol.Add(e); }
            // a line that is not one of the moves: as it is, except that absolute E runs on from the new amount
            void Verbatim(int li)
            {
                var ln = p.L[li]; string raw = ln.Raw;
                if (ln.ResetE) { eAbsOld = eAbsNew = ln.ResetTo; eShift = 0; }
                if (ln.N >= 0) lastN = ln.N;
                if (p.AbsE && ln.EWord >= 0 && !ln.RelE && ln.W[ln.EWord].Key == "E" && (ln.EOnly || ln.M != null))
                {
                    var wd = ln.W[ln.EWord]; eAbsOld = wd.V; eAbsNew = wd.V + eShift;
                    if (Math.Abs(eShift) > 1e-9) { raw = Put(raw, wd, wd.V + eShift, Math.Max(wd.Dec, 3)); r.Changed++; }
                    else r.Kept++;
                }
                else r.Kept++;
                var m = ln.M ?? (ln.Arc != null && ln.Arc.Count > 0 ? ln.Arc[ln.Arc.Count - 1] : null);
                if (m != null) { mx = m.X; my = m.Y; mz = m.Z; ma = m.A; mb = m.B; mc = m.C; if (m.F > 0) mf = m.F; }
                else if (ln.W != null) foreach (var wd in ln.W) if (wd.Key == "F" && !double.IsNaN(wd.V)) mf = wd.V;
                Emit(raw, ln.Eol);
            }
            void UpTo(int li) { while (next < li && !(firstLine.Contains(next))) { Verbatim(next); next++; } }
            var perSource = new int[tp.Count]; for (int j = 0; j < s.Path.Count; j++) perSource[s.Source[j]]++;
            var arcTouched = new HashSet<int>();
            for (int j = 0; j < s.Path.Count; j++) { int i = s.Source[j]; if (of[i] >= 0 && mv[of[i]].Arc && (s.Inserted[j] || s.Path.Points[j].Z != tp.Points[i].Z || Math.Abs(s.Weight[j] - 1) > 1e-9)) arcTouched.Add(mv[of[i]].Line); }
            var pending = new List<int>(); // path points put in before the next move of the file
            void Flush(int beforeN) { if (pending.Count == 0) return; int room = beforeN > lastN && lastN >= 0 ? beforeN - lastN - 1 : 0; for (int q = 0; q < pending.Count; q++) Write(pending[q], room >= pending.Count ? lastN + 1 + (int)((long)q * room / pending.Count) : -1); pending.Clear(); }
            void Angles(int j, int i, out double A, out double B, out double C)
            {
                var m = of[i] >= 0 ? mv[of[i]] : null; A = m?.A ?? 0; B = m?.B ?? 0; C = m?.C ?? 0;
                Vector3d n0 = tp.Normals[i], n1 = s.Path.Normals[j];
                if (!tilt || Math.Abs(n0.X - n1.X) + Math.Abs(n0.Y - n1.Y) + Math.Abs(n0.Z - n1.Z) < 1e-9) return;
                // the old frame, turned the shortest way from the old tool axis to the new one
                var f = Frame.FromAbc(Point3d.Origin, A, B, C); Vector3d ax = Vector3d.CrossProduct(n0, n1); double cs = n0.X * n1.X + n0.Y * n1.Y + n0.Z * n1.Z, kk = 1 / (1 + cs);
                Vector3d Rot(Vector3d v) { var cr = Vector3d.CrossProduct(ax, v); double d = ax.X * v.X + ax.Y * v.Y + ax.Z * v.Z; return v * cs + cr + ax * (d * kk); }
                var abc = new Frame { X = Rot(f.X), Y = Rot(f.Y), Z = Rot(f.Z), O = Point3d.Origin }.Abc(); A = Math.Round(abc[0], 4); B = Math.Round(abc[1], 4); C = Math.Round(abc[2], 4);
            }
            double Feed(int j, int i, double e)
            {
                var m = of[i] >= 0 ? mv[of[i]] : null; double f = m?.F ?? 0;
                if (capRate <= 0 || e <= 1e-9 || f <= 0 || j == 0 || tp.Roles[i] == PathRole.Skirt) return f;
                double l = s.Path.Points[j].DistanceTo(s.Path.Points[j - 1]);
                if (l < 0.05 || e / l * f <= capRate * 1.0005) return f;
                r.Slowed++; return Math.Max(1, Math.Round(capRate * l / e));
            }
            string Num(double v, int dec) { string t = Math.Round(v, dec).ToString("F" + dec.ToString(Ci), Ci); return t == "-" + (0.0).ToString("F" + dec.ToString(Ci), Ci) ? t.Substring(1) : t; }
            // a new line for a point that was put in
            void Write(int j, int N)
            {
                int i = s.Source[j]; var m = of[i] >= 0 ? mv[of[i]] : null; Point3d q = s.Path.Points[j]; bool pr = m != null && m.E > 1e-9 && s.Weight[j] >= 0 && LapBelow.Prints(s.Path, j);
                double e = pr ? m.E * s.Weight[j] : 0, f = Feed(j, i, e); var sb = new StringBuilder();
                if (N > 0) { sb.Append('N').Append(N.ToString(Ci)).Append(' '); lastN = N; }
                sb.Append(m != null && m.Rapid ? "G0" : "G1");
                if (f > 0 && f != mf) { sb.Append(" F").Append(Inv.N(f, 3)); mf = f; }
                double X = Math.Round(q.X, 3), Y = Math.Round(q.Y, 3), Z = Math.Round(q.Z, 3);
                sb.Append(" X").Append(Num(X, 3)).Append(" Y").Append(Num(Y, 3)).Append(" Z").Append(Num(Z, 3)); mx = X; my = Y; mz = Z;
                if (tilt) { Angles(j, i, out double A, out double B, out double C); if (A != ma || B != mb || C != mc) { sb.Append(" A").Append(Num(A, 4)).Append(" B").Append(Num(B, 4)).Append(" C").Append(Num(C, 4)); ma = A; mb = B; mc = C; } }
                if (e > 0)
                {
                    if (p.Dxr) sb.Append(" G91 XE=[").Append(Num(e, 6)).Append("*P1] G90");
                    else if (p.AbsE) { eAbsNew += e; sb.Append(" E").Append(Num(eAbsNew, 5)); }
                    else sb.Append(" E").Append(Num(e, 5));
                }
                Emit(sb.ToString(), eol); r.Inserted++; expect.Add(new[] { q.X, q.Y, q.Z, e });
            }
            for (int j = 0; j < s.Path.Count; j++)
            {
                int i = s.Source[j]; if (of[i] < 0) continue;
                var m = mv[of[i]]; var ln = p.L[m.Line];
                UpTo(m.Line);
                if (m.Arc && !arcTouched.Contains(m.Line)) { if (m.ArcEnd) { Flush(ln.N); Verbatim(m.Line); next = m.Line + 1; expect.Add(null); } continue; }
                if (m.Arc)
                {
                    pending.Add(j);
                    if (m.ArcEnd && !s.Inserted[j]) { eAbsOld += ln.Arc.Sum(q => q.E); Flush(ln.N); eShift = eAbsNew - eAbsOld; next = m.Line + 1; r.Changed++; if (ln.N >= 0) lastN = ln.N; }
                    continue;
                }
                if (s.Inserted[j]) { pending.Add(j); continue; }
                Flush(ln.N);
                if (next < m.Line) { while (next < m.Line) { Verbatim(next); next++; } }
                // the line itself
                Point3d pt = s.Path.Points[j]; string raw = ln.Raw; bool touched = false; double eNew = m.E > 1e-9 ? m.E * s.Weight[j] : m.E, fNew = Feed(j, i, m.E > 1e-9 ? eNew : 0);
                bool moved = Math.Abs(pt.Z - m.Z) > 5e-5; double zOut = moved ? Math.Round(pt.Z, 3) : m.Z;
                double A1 = m.A, B1 = m.B, C1 = m.C; if (tilt) Angles(j, i, out A1, out B1, out C1);
                var ed = new List<KeyValuePair<int, Func<string, string>>>(); // edits from the back to the front: position, edit
                void Set(string key, double v, int dec, double modal, double old)
                {
                    var wd = ln.W.FirstOrDefault(q => q.Key == key);
                    if (wd != null) { if (Math.Abs(wd.V - v) > 5e-7 + 0.5 * Math.Pow(10, -Math.Max(dec, wd.Dec)) * 0.999 || Math.Abs(wd.V - v) > 5e-7 && Math.Abs(old - v) > 5e-7) { ed.Add(new KeyValuePair<int, Func<string, string>>(wd.From, t => Put(t, wd, v, Math.Max(dec, wd.Dec)))); touched = true; } }
                    else if (double.IsNaN(modal) || Math.Abs(modal - v) > 1e-9)
                    {
                        int at = After(ln, key); string ins = " " + key + Inv.N(v, 6);
                        if (key == "Z" || "ABC".IndexOf(key[0]) >= 0) ins = " " + key + Num(v, dec);
                        ed.Add(new KeyValuePair<int, Func<string, string>>(at, t => t.Insert(at, ins))); touched = true;
                    }
                }
                Set("X", m.X, 3, mx, m.X); Set("Y", m.Y, 3, my, m.Y); Set("Z", zOut, 3, mz, m.Z);
                if (tilt) { Set("A", A1, 4, ma, m.A); Set("B", B1, 4, mb, m.B); Set("C", C1, 4, mc, m.C); }
                if (fNew > 0 && !double.IsNaN(mf) && Math.Abs(fNew - mf) > 1e-9) { var fw = ln.W.FirstOrDefault(q => q.Key == "F"); if (fw == null || Math.Abs(fw.V - fNew) > 1e-9) Set("F", fNew, 0, mf, m.F); }
                else if (fNew > 0 && ln.W.Any(q => q.Key == "F" && Math.Abs(q.V - fNew) > 1e-9)) Set("F", fNew, 0, mf, m.F);
                if (ln.EWord >= 0)
                {
                    var wd = ln.W[ln.EWord]; bool abs = p.AbsE && wd.Key == "E" && wd.Mode != 1 && !ln.RelE;
                    if (abs) { eAbsOld = wd.V; eAbsNew += eNew; if (Math.Abs(eAbsNew - eAbsOld) > 1e-9 && (s.FirstMoved >= 0 && j > 0)) { double v = eAbsNew; if (Math.Abs(v - wd.V) > 0.5 * Math.Pow(10, -Math.Max(3, wd.Dec)) * 1.0001) { ed.Add(new KeyValuePair<int, Func<string, string>>(wd.From, t => Put(t, wd, v, Math.Max(wd.Dec, 3)))); touched = true; } } eShift = eAbsNew - eAbsOld; if (Math.Abs(eShift) < 1e-7) eShift = 0; }
                    else if (m.E > 1e-9 && Math.Abs(s.Weight[j] - 1) > 1e-7) { ed.Add(new KeyValuePair<int, Func<string, string>>(wd.From, t => Put(t, wd, eNew, Math.Max(wd.Dec, 3)))); touched = true; }
                }
                if (touched) { raw = Edit(raw, ed); r.Changed++; } else r.Kept++;
                Emit(raw, ln.Eol); next = m.Line + 1; if (ln.N >= 0) lastN = ln.N;
                mx = m.X; my = m.Y; mz = zOut; ma = A1; mb = B1; mc = C1; if (fNew > 0) mf = fNew;
                expect.Add(new[] { m.X, m.Y, pt.Z, Math.Max(0, eNew) });
            }
            Flush(-1);
            int lastOwn = outL.Count;
            while (next < p.L.Count) { Verbatim(next); next++; }
            // the tool stands straight again for the end code
            if (tilt && Math.Abs(ma - mv[mv.Count - 1].A) + Math.Abs(mb - mv[mv.Count - 1].B) + Math.Abs(mc - mv[mv.Count - 1].C) > 1e-6)
            {
                var m = mv[mv.Count - 1];
                outL.Insert(lastOwn, "G1 A" + Num(m.A, 4) + " B" + Num(m.B, 4) + " C" + Num(m.C, 4)); outEol.Insert(lastOwn, eol); r.Inserted++;
            }
            // ---- end code that moves over the part at the old height
            string end = EndCode(p, mv, tp, s, out double liftTo);
            if (end != null)
            {
                var lm = mv[mv.Count - 1];
                outL.Insert(lastOwn, (p.Dxr ? "G1" : "G0") + " Z" + Num(liftTo, 3) + (lm.F > 0 && p.Dxr ? " F" + Inv.N(Math.Min(lm.F, 3000), 3) : "")); outEol.Insert(lastOwn, eol); r.Inserted++;
                r.Warnings.Add(end);
            }
            if (!identical) Header(p, outL, mv, s, tp, r);
            if (!identical && o.Mark)
            {
                string mark = "; reshaped by LARGER.slicer: " + (field?.Source ?? "").Replace('\n', ' ') + Inv.F($", layers x {s.Map.SMin:0.###} to {s.Map.SMax:0.###}");
                mark = new string(mark.Select(ch => ch < 128 ? ch : '?').ToArray());
                if (p.Dxr) { if (outEol.Count > 0 && outEol[outEol.Count - 1].Length == 0) outEol[outEol.Count - 1] = eol; outL.Add(mark); outEol.Add(eol); }
                else { outL.Insert(0, mark); outEol.Insert(0, eol); }
                r.Inserted++;
            }
            var all = new StringBuilder(); for (int k = 0; k < outL.Count; k++) all.Append(outL[k]).Append(outEol[k]);
            r.Text = all.ToString(); r.Lines = outL; r.LinesOut = outL.Count;

            // ---- read the new file again: does it say what was meant?
            var p2 = Read(r.Text, ext);
            if (p2.Why != null) return Refuse("The written file could not be read again: " + p2.Why);
            Compare(p2, s.Path, s, tp, mv, of, r);
            if (r.CheckMm > 0.02 || r.CheckE > 0.002) return Refuse(Inv.F($"The written file does not say what was meant (up to {r.CheckMm:0.###} mm, {100 * r.CheckE:0.##} % of E off). Nothing was written. Please send the file to the developers."));
            r.EAfter = p2.Moves.Sum(m => Math.Max(0, m.E));

            // ---- report
            var rep = r.Report; bool ok = s.Check == null || s.Check.Ok;
            rep.Add(identical ? "OK: the field is 1 everywhere, the file is unchanged." : (ok ? "OK: " : "CHECK: ") + Inv.F($"{r.Changed} of {r.LinesIn} lines changed, {r.Inserted} put in, {r.Kept} kept byte for byte; E of the part {100 * s.MaterialShare:0.#} % of before") + (ok ? "." : ", but " + s.Check.First + "."));
            rep.Add(Inv.F($"File: {(p.Dxr ? "Weber DXR" : "G-code")}, {(p.Dxr ? "relative XE" : p.AbsE ? "absolute E" : "relative E")}, {levels.Count} layers ({layers}), layer height {h:0.##} mm, bead width {w:0.##} mm ({wFrom})."));
            rep.Add(Inv.F($"Calibration of the file: {r.Calibration:0.####} E per mm3 of width x height x length (E per mm {K:0.####}); it is kept, {(round ? "counted with round bead sides" : "E goes with the height")}."));
            rep.Add(tilt ? "Tool angles A B C are written (KUKA: A about Z, B about Y', C about X''), tool square to the layers." : "The nozzle stays vertical.");
            if (o.MaxFlowFactor > 0) rep.Add(Inv.F($"Flow: no move above {o.MaxFlowFactor:0.##} x the largest flow of the file; {r.Slowed} move(s) slower."));
            if (r.HeaderChanged > 0) rep.Add(Inv.F($"Header: {r.HeaderChanged} value(s) changed (Zmin, Zmax, Eges, number of rows, time)."));
            rep.Add(Inv.F($"Read back: positions within {r.CheckMm:0.####} mm, E within {100 * r.CheckE:0.###} %."));
            foreach (var l in s.Report.Skip(1)) rep.Add(l);
            foreach (var l in r.Warnings) rep.Add("Warning: " + l);
            return r;
        }

        /// <summary>All edits of a line. They are positions in the old text, so they are made from the back; at the same place the later one first.</summary>
        private static string Edit(string raw, List<KeyValuePair<int, Func<string, string>>> ed)
        {
            foreach (var kv in ed.Select((v, i) => new { v, i }).OrderByDescending(q => q.v.Key).ThenByDescending(q => q.i)) raw = kv.v.Value(raw);
            return raw;
        }

        private static string Put(string s, Word w, double v, int dec)
        {
            string old = s.Substring(w.From, w.To - w.From);
            string t = Math.Round(v, dec).ToString("F" + dec.ToString(Ci), Ci);
            // written as short as the file does: "Z4" stays short, "Z4.000" long
            if (w.Dec < dec && t.IndexOf('.') >= 0) { string cut = t.TrimEnd('0'); int keep = cut.IndexOf('.') + 1 + w.Dec; t = cut.Length < keep ? t.Substring(0, keep) : cut; t = t.TrimEnd('.'); }
            return s.Substring(0, w.From) + t + s.Substring(w.To);
        }

        /// <summary>Where a missing word goes: X and Y behind G and F, Z behind X Y, A B C behind Z, F behind G.</summary>
        private static int After(Line ln, string key)
        {
            string order = key == "F" ? "NG" : key == "X" ? "NGF" : key == "Y" ? "NGFX" : key == "Z" ? "NGFXY" : key == "A" ? "NGFXYZ" : key == "B" ? "NGFXYZA" : "NGFXYZAB";
            int at = 0; 
            foreach (var w in ln.W)
            {
                if (w.Key == "XE" || w.Key == "E" || w.Key == "G" && (w.V == 91 || w.V == 90) && at > 0 && ln.W.Any(q => q.Key == "XE")) break;
                if (w.Key.Length == 1 && order.IndexOf(w.Key[0]) >= 0) at = Math.Max(at, w.To);
            }
            return at;
        }

        private static ReshapeOptions Copy(ReshapeOptions o) => new ReshapeOptions { Tilt = o.Tilt, MinLayer = o.MinLayer, MaxLayer = o.MaxLayer, MaxSlope = o.MaxSlope, Smooth = o.Smooth, FlatLayers = o.FlatLayers, Roof = o.Roof, ExtraPasses = o.ExtraPasses, Material = o.Material, Head = o.Head, Limit = o.Limit, Check = o.Check, MinWidth = o.MinWidth, MaxWidth = o.MaxWidth };

        private static double Median(List<double> v) { if (v.Count == 0) return 0; v.Sort(); return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2] + v[v.Count / 2 - 1]); }

        /// <summary>Layer numbers: from the marks of the file if every mark goes up by about one height, else from the Z steps, else from the turns.</summary>
        private static string Layers(List<Move> mv)
        {
            var hints = new HashSet<int>(mv.Where(m => m.Hint != int.MinValue && m.E > 1e-9).Select(m => m.Hint));
            if (hints.Count >= 4)
            {
                int layer = 0, last = int.MinValue; bool any = false;
                foreach (var m in mv) { if (m.Hint != int.MinValue) { if (any && m.Hint != last) layer++; last = m.Hint; any = true; } m.Layer = layer; }
                // marks may be fewer than the real layers (several heights under one mark): then they are no use
                bool good = true;
                foreach (var g in mv.Where(m => m.E > 1e-9 && m.Length > 0.5).GroupBy(m => m.Layer))
                {
                    var z = g.Where(m => Math.Abs(m.Z - m.SZ) < 1e-6).GroupBy(m => Math.Round(m.Z, 2)).Where(q => q.Sum(m => m.Length) > 0.1 * g.Sum(m => m.Length)).Count();
                    if (z > 1) { good = false; break; }
                }
                if (good) return "from the layer marks of the file";
            }
            double cx = 0, cy = 0; int n = 0; foreach (var m in mv) if (m.E > 1e-9) { cx += m.X; cy += m.Y; n++; }
            cx /= Math.Max(1, n); cy /= Math.Max(1, n);
            int lay = 0; double level = double.NaN, printed = 0, turn = 0; int turns = 0;
            foreach (var m in mv)
            {
                if (m.E > 1e-9)
                {
                    if (Math.Abs(m.Z - m.SZ) < 0.01) { if (!double.IsNaN(level) && Math.Abs(m.Z - level) > 0.05 && printed > 0) { lay++; printed = 0; turn = 0; } level = m.Z; }
                    else
                    {
                        double a0 = Math.Atan2(m.SY - cy, m.SX - cx), d = Math.Atan2(m.Y - cy, m.X - cx) - a0; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                        turn += d; if (Math.Abs(turn) >= 2 * Math.PI) { lay++; turns++; turn -= Math.Sign(turn) * 2 * Math.PI; printed = 0; }
                    }
                    printed += m.Length;
                }
                m.Layer = lay;
            }
            return turns > 0 ? "from the Z steps and the turns around the part" : "from the Z steps";
        }

        /// <summary>Why this is not a part whose print height can be changed; null = it is.</summary>
        private static string Sound(Toolpath tp, Program p, double h)
        {
            // beads must lie in layers that go up: the print directions are level, and later layers are higher
            double level = 0, all = 0; int jumps = 0, down = 0, layers = 0; double lastMed = double.MinValue;
            for (int i = 1; i < tp.Count; i++)
            {
                if (!LapBelow.Prints(tp, i)) continue;
                var d = tp.Points[i] - tp.Points[i - 1]; double l = tp.Points[i].DistanceTo(tp.Points[i - 1]); if (l < 1e-9) continue;
                all += l; if (Math.Abs(d.Z) < 0.5 * l) level += l;
                if (Math.Abs(d.Z) > Math.Max(50, 20 * h)) jumps++;
            }
            if (jumps > 0) return Inv.F($"{jumps} printing move(s) jump more than {Math.Max(50, 20 * h):0} mm in Z: numbers of the file are broken (for example .566 written as 566), or it is not a part in layers.");
            if (level < 0.8 * all) return "The beads of this file do not lie in layers one above the other (angled or multi-axis printing): the print height does not run upwards.";
            foreach (var g in Enumerable.Range(1, tp.Count - 1).Where(i => LapBelow.Prints(tp, i)).GroupBy(i => tp.Layers[i]).OrderBy(g => g.Key))
            {
                double z = Median(g.Select(i => tp.Points[i].Z).ToList()); layers++;
                if (z < lastMed - 0.05) down++;
                lastMed = z;
            }
            if (layers < 4) return Inv.F($"Only {layers} layer(s) found.");
            if (down > 0.02 * layers) return Inv.F($"{down} of {layers} layers lie lower than the one before: several parts one after the other, or layers that are not understood.");
            return null;
        }

        /// <summary>After the last bead: code with relative lifts or without Z that then moves sideways. With the new top it may run through the part.</summary>
        private static string EndCode(Program p, List<Move> mv, Toolpath tp, PathReshape.Result s, out double liftTo)
        {
            liftTo = 0;
            double top = double.MinValue, top0 = double.MinValue;
            for (int i = 1; i < s.Path.Count; i++) if (LapBelow.Prints(s.Path, i)) top = Math.Max(top, s.Path.Points[i].Z);
            for (int i = 1; i < tp.Count; i++) if (LapBelow.Prints(tp, i)) top0 = Math.Max(top0, tp.Points[i].Z);
            double zNew = s.Path.Points[s.Path.Count - 1].Z, zOld = tp.Points[tp.Count - 1].Z;
            // the way out of the file itself is in s.Path; what comes later keeps its words
            bool after = false; int lastLine = mv[mv.Count - 1].Line; double z = zNew;
            for (int l = lastLine + 1; l < p.L.Count; l++)
            {
                var ln = p.L[l]; if (ln.Homes) break;
                var m = ln.M; if (m == null) continue;
                after = true;
                bool hasZ = ln.W.Any(q => q.Key == "Z");
                if (hasZ) z = ln.Relative ? z + (m.Z - m.SZ) : m.Z;
                bool xy = Math.Abs(m.X - m.SX) + Math.Abs(m.Y - m.SY) > 1;
                if (xy && z < top + 1 && zOld >= top0 - 0.01 && top > zNew + 0.5) { liftTo = top + 10; return Inv.F($"The end code moves sideways at Z {z:0.#} mm, but the new part is up to {top:0.#} mm high: a lift to Z {liftTo:0.#} was put in before it. Check the end of the file."); }
            }
            if (!after && s.Path.Count > 0 && top > zNew + 0.5 && p.L.Skip(lastLine + 1).Any(q => q.Homes)) { liftTo = top + 10; return Inv.F($"The file homes right after the last move, at Z {zNew:0.#} mm, but the new part is up to {top:0.#} mm high: a lift to Z {liftTo:0.#} was put in. Check the end of the file."); }
            return null;
        }

        private static readonly Regex RxHead = new Regex(@"^(\s*;\s*(Zmin|Zmax|Eges|number of movement rows|ProgRunTimeTotal)\s*=\s*(?:IC)?\s*\[?\s*)([-0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The head of a .dxr: the robot stops when the tool leaves the box Zmin..Zmax written there.</summary>
        private static void Header(Program p, List<string> outL, List<Move> mv, PathReshape.Result s, Toolpath tp, Result r)
        {
            if (!p.Dxr) return;
            int f = mv.Count > 0 ? p.Moves.IndexOf(mv[0]) : 0, t = f + mv.Count - 1;
            double n1 = s.Path.Points.Max(q => q.Z), n0 = s.Path.Points.Min(q => q.Z);
            for (int i = 0; i < p.Moves.Count; i++) if (i < f || i > t) { n1 = Math.Max(n1, p.Moves[i].Z); n0 = Math.Min(n0, p.Moves[i].Z); }
            double dE = 0, dT = 0;
            for (int j = 1; j < s.Path.Count; j++) { int i = s.Source[j]; int k = i - (tp.Count - mv.Count); if (k < 0) continue; dE += Math.Max(0, mv[k].E) * s.Weight[j] * (LapBelow.Prints(tp, i) ? 1 : 0); if (mv[k].F > 0) dT += 60 * s.Path.Points[j].DistanceTo(s.Path.Points[j - 1]) / mv[k].F; }
            foreach (var m in mv) { dE -= Math.Max(0, m.E); if (m.F > 0) dT -= 60 * m.Length / m.F; }
            for (int l = 0; l < Math.Min(outL.Count, 60); l++)
            {
                var mt = RxHead.Match(outL[l]); if (!mt.Success) continue;
                if (!double.TryParse(mt.Groups[3].Value, NumberStyles.Float, Ci, out double v)) continue;
                int dot = mt.Groups[3].Value.IndexOf('.'), dec = dot < 0 ? 0 : mt.Groups[3].Value.Length - dot - 1; double nv = v; string key = mt.Groups[2].Value.ToLowerInvariant();
                if (key == "zmax") { if (n1 > v + 1e-9) nv = Math.Ceiling(n1 * Math.Pow(10, dec) - 1e-6) / Math.Pow(10, dec); } // the box is only made larger
                else if (key == "zmin") { if (n0 < v - 1e-9) nv = Math.Floor(n0 * Math.Pow(10, dec) + 1e-6) / Math.Pow(10, dec); }
                else if (key == "eges") nv = v + dE;
                else if (key == "number of movement rows") nv = v + r.Inserted;
                else if (v > 0) nv = v + dT;
                string txt = Math.Round(nv, dec).ToString("F" + dec.ToString(Ci), Ci);
                if (Math.Abs(nv - v) < 0.5 * Math.Pow(10, -dec)) continue;
                outL[l] = mt.Groups[1].Value + txt + outL[l].Substring(mt.Length); r.HeaderChanged++; r.Changed++; r.Kept--;
            }
        }

        /// <summary>The written file against the path that was meant.</summary>
        private static void Compare(Program q, Toolpath path, PathReshape.Result s, Toolpath tp, List<Move> mv, List<int> of, Result r)
        {
            var want = new List<double[]>();
            for (int j = 0; j < path.Count; j++) { int i = s.Source[j]; if (of[i] < 0) continue; var m = mv[of[i]]; want.Add(new[] { path.Points[j].X, path.Points[j].Y, path.Points[j].Z, m.E > 1e-9 && LapBelow.Prints(path, j) ? m.E * s.Weight[j] : 0 }); }
            // both as beads along the way: compare the sums of E and every printing position in order
            var a = want.Where(v => v[3] > 1e-6).ToList(); var b = q.Moves.Where(m => m.E > 1e-6).ToList();
            double ea = want.Sum(v => v[3]), eb = q.Moves.Sum(m => Math.Max(0, m.E));
            r.CheckE = ea > 0 ? Math.Abs(eb / ea - 1) : 0;
            int k = 0; double worst = 0;
            foreach (var v in a)
            {
                // moves of almost no E may round to nothing in the file: look a little ahead
                double best = double.MaxValue; int at = k;
                for (int t = k; t < Math.Min(b.Count, k + 60); t++) { double d = Math.Max(Math.Abs(b[t].X - v[0]), Math.Max(Math.Abs(b[t].Y - v[1]), Math.Abs(b[t].Z - v[2]))); if (d < best) { best = d; at = t; if (d < 0.002) break; } }
                if (v[3] < 0.002 && best > 0.02) continue;
                if (best > worst) worst = best;
                k = Math.Min(b.Count, at + 1);
                if (k >= b.Count) break;
            }
            r.CheckMm = worst;
        }
    }
}
