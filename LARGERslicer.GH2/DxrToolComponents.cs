using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Grasshopper2.Components;
using Grasshopper2.Data;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.GH2
{
    // The second row of the DXR tab of Grasshopper 1 (LARGERslicer/Components/Utils): DXR Width Preview,
    // DXR E Remap, G-code Diff Check and Surface Thickness Heatmap. Same names, inputs, outputs, defaults
    // and behaviour. Their logic lives inside the Grasshopper 1 components, so it is copied here line for
    // line; only reading the inputs, writing the outputs and the preview are Grasshopper 2.

    /// <summary>Reading the inputs of the copied DXR tools.</summary>
    internal static class DxrTools
    {
        /// <summary>Grasshopper 1 joined the items of a text list with line breaks (one item with line breaks stays as it is).</summary>
        public static string JoinText(object[] raw)
        {
            if (raw == null || raw.Length == 0) return string.Empty;
            var parts = raw.Where(o => o != null).Select(o => o as string ?? o.ToString()).ToList();
            if (parts.Count == 1) return parts[0];
            return string.Join("\n", parts);
        }

        /// <summary>Any geometry as Rhino geometry: Grasshopper 2 keeps lines, boxes, points ... as plain values.</summary>
        public static GeometryBase ToGeometry(object o)
        {
            if (o == null) return null;
            if (o is GeometryBase g) return g;
            if (o is Point3d p) return new Point(p);
            var solid = Io.Solid(o);
            if (solid is GeometryBase sg) return sg;
            if (o is Plane pl) return new PlaneSurface(pl, new Interval(-1, 1), new Interval(-1, 1));
            return CurveIo.ToCurve(o);
        }

        /// <summary>The branches of a tree input, in order; empty when nothing is connected.</summary>
        public static List<List<object>> Branches(IDataAccess access, int index)
        {
            var result = new List<List<object>>();
            if (!access.TreeOf(index, out Tree<object> tree) || tree == null) return result;
            foreach (var twig in tree.AllTwigs) result.Add(twig.Items.ToList());
            return result;
        }

        public static bool TryDouble(object v, out double value)
        {
            value = 0.0;
            switch (v)
            {
                case null: return false;
                case double d: value = d; return true;
                case float f: value = f; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case bool b: value = b ? 1 : 0; return true;
            }
            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    // ============================================================== G-code diff check (GH1: Components/Utils/GCodeDiffCheckComponent.cs)
    [IoId("07e1c089-79bd-47cd-80c0-71d2a563e333")]
    public sealed class GCodeDiffCheckComponent : Component
    {
        public GCodeDiffCheckComponent() : base(Io.Name("G-code Diff Check", "Compares two programs line by line, e.g. before and after DXR E Remap, to prove that only the extrusion changed and no coordinate.", Io.Dxr, 1)) { }
        public GCodeDiffCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("GCodeDiffCheckIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("GCode A", "A", "Original G-code text.", Access.Twig);
            inputs.AddText("GCode B", "B", "Remap output G-code text.", Access.Twig);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Report", "Report", "Comparison summary report.");
            outputs.AddText("Diff Lines", "Diff", "First mismatching line pairs (max 10).", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string[] ra, rb;
            access.ItemArray(0, out ra); access.ItemArray(1, out rb);
            string[] aLines = SplitLines(ToText(ra));
            string[] bLines = SplitLines(ToText(rb));

            int nA = aLines.Length, nB = bLines.Length, n = Math.Min(nA, nB);
            int nIdent = 0, nXeOnly = 0, nCoordDiff = 0, nOther = 0;
            var diffLines = new List<string>();

            for (int i = 0; i < n; i++)
            {
                string la = aLines[i] ?? string.Empty;
                string lb = bLines[i] ?? string.Empty;
                if (la == lb) { nIdent++; continue; }

                List<string> ca = CoordsOf(la), cb = CoordsOf(lb);
                bool coordsEqual = ca.Count == cb.Count;
                if (coordsEqual)
                    for (int c = 0; c < ca.Count; c++)
                        if (!string.Equals(ca[c], cb[c], StringComparison.Ordinal)) { coordsEqual = false; break; }

                if (coordsEqual)
                {
                    // only the extrusion: everything else of the line (A, B, C, F ...) is the same
                    if (la.Contains("XE=[") && lb.Contains("XE=[") && WithoutXe(la) == WithoutXe(lb)) nXeOnly++;
                    else
                    {
                        nOther++;
                        if (diffLines.Count < 10) diffLines.Add($"Line {i + 1}:\nA: {la}\nB: {lb}");
                    }
                }
                else
                {
                    nCoordDiff++;
                    if (diffLines.Count < 10) diffLines.Add($"Line {i + 1} (COORDINATES DIFFER):\nA: {la}\nB: {lb}");
                }
            }

            string report =
                $"Lines A: {nA} | Lines B: {nB} {(nA == nB ? "(MATCH)" : "(DIFFER)")}\n" +
                $"identical: {nIdent} | XE-only changed: {nXeOnly} | coordinates changed: {nCoordDiff} | other differences: {nOther}\n" +
                (nA == nB && nCoordDiff == 0 && nOther == 0
                    ? "-> No difference except XE values: the output path is byte-identical to the original. " +
                      "Any visible deviation then comes from visualization, not from code changes."
                    : "-> The programs differ in more than XE values" +
                      (nCoordDiff > 0 ? $"; {nCoordDiff} line(s) with other coordinates" : "") +
                      (nOther > 0 ? $"; {nOther} line(s) with other angles, speeds or commands" : "") +
                      (nA != nB ? $"; {Math.Abs(nA - nB)} line(s) only in {(nA > nB ? "A" : "B")}" : "") + ".");

            access.SetItem(0, report);
            access.SetTwig(1, diffLines.ToArray(), null, null);
        }

        private static string ToText(string[] raw)
        {
            if (raw == null || raw.Length == 0) return string.Empty;
            if (raw.Length == 1)
            {
                string single = raw[0] ?? string.Empty;
                if (single.Contains("\n") || single.Contains("\r")) return single;
            }
            return string.Join("\n", raw.Where(x => x != null));
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n' }, StringSplitOptions.None);
        }

        /// <summary>The line with every XE=[...] emptied.</summary>
        private static string WithoutXe(string line)
        {
            int from = 0;
            while ((from = line.IndexOf("XE=[", from, StringComparison.Ordinal)) >= 0)
            {
                from += 4;
                int end = line.IndexOf(']', from);
                if (end < 0) break;
                line = line.Substring(0, from) + line.Substring(end);
            }
            return line;
        }

        private static List<string> CoordsOf(string line)
        {
            var outTokens = new List<string>();
            if (string.IsNullOrWhiteSpace(line)) return outTokens;
            foreach (string tok in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length <= 1) continue;
                char axis = tok[0], first = tok[1];
                if ((axis == 'X' || axis == 'Y' || axis == 'Z') && (char.IsDigit(first) || first == '-')) outTokens.Add(tok);
            }
            return outTokens;
        }
    }

    // ============================================================== DXR width preview (GH1: Components/Utils/FastGCodeDxrPreviewComponent.cs)
    [IoId("032bd757-fb19-459e-b5a1-28001fd011d3")]
    public sealed class DxrWidthPreviewComponent : ColourPreviewComponent
    {
        private const int ChunkSize = 4000;
        // the parsed program and its display, kept while the text and the widths stay the same
        private readonly object _lock = new object();
        private readonly Dictionary<int, PreviewCache> _caches = new Dictionary<int, PreviewCache>();

        public DxrWidthPreviewComponent() : base(Io.Name("DXR Width Preview", "Draws a DXR program coloured by its line width (the XE values), with an optional 3D ribbon. Reads DXR lines (G1 ... XE=[...]); for plain G-code use Import G-code.", Io.Dxr, 1)) { }
        public DxrWidthPreviewComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("FastGCodeDxrPreviewIcon");
        // the ribbons and lines are drawn in their colours by the component itself
        protected override int[] OwnOutputs => new[] { 0 };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("GCode In", "Code", "G-code/DXR text as string or list of lines.", Access.Twig);
            inputs.AddNumber("Min Width", "Wmin", "Minimum preview width.").Set(1.0);
            inputs.AddNumber("Max Width", "Wmax", "Maximum preview width.").Set(8.0);
            inputs.AddInteger("Color Steps", "Steps", "Number of color buckets.").Set(16);
            inputs.AddNumber("Progress", "Prog", "Visible path fraction from 0..1.").Set(1.0);
            inputs.AddBoolean("Active", "On", "Enable preview generation.").Set(true);
            inputs.AddBoolean("Preview 3D", "3D", "If true, outputs ribbon meshes; if false, uses line conduit mode.").Set(true);
            inputs.AddNumber("Min Span", "Span", "Minimum relative color span (default 0.25).", Access.Item, Requirement.MayBeMissing).Set(0.25);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Preview Geo", "Prev", "Preview geometry (meshes in 3D mode, lines in conduit mode).", Access.Twig);
            outputs.AddText("Info", "Info", "Preview status summary.");
        }

        protected override void Process(IDataAccess access)
        {
            ClearColours();
            object[] raw;
            access.ItemArray(0, out raw);
            double breiteMin, breiteMax, fortschritt, spanMin; int farbstufen; bool aktiv, vorschau3D;
            if (!access.GetItem(1, out breiteMin)) breiteMin = 1.0;
            if (!access.GetItem(2, out breiteMax)) breiteMax = 8.0;
            if (!access.GetItem(3, out farbstufen)) farbstufen = 16;
            if (!access.GetItem(4, out fortschritt)) fortschritt = 1.0;
            if (!access.GetItem(5, out aktiv)) aktiv = true;
            if (!access.GetItem(6, out vorschau3D)) vorschau3D = true;
            if (!access.GetItem(7, out spanMin)) spanMin = 0.25;

            breiteMin = breiteMin <= 0.0 ? 1.0 : breiteMin;
            breiteMax = breiteMax <= 0.0 ? 8.0 : breiteMax;
            if (breiteMax < breiteMin) { double t = breiteMin; breiteMin = breiteMax; breiteMax = t; }
            if (farbstufen < 2) farbstufen = 2;
            if (fortschritt < 0.0) fortschritt = 0.0;
            if (fortschritt > 1.0) fortschritt = 1.0;
            if (spanMin < 0.0) spanMin = 0.0;

            string text = DxrTools.JoinText(raw);
            var parseSig = CheapSig(text);
            var picture = new ColourPicture();
            var previewGeo = new List<object>();
            string info;

            lock (_lock)
            {
                if (!_caches.TryGetValue(access.Index, out PreviewCache cache)) { cache = new PreviewCache(); _caches[access.Index] = cache; }
                string parseStatus, dispStatus;
                if (cache.ParseSig.Equals(parseSig)) parseStatus = "Hit";
                else
                {
                    ParseRaw(text, cache.Segments, cache.Epm);
                    cache.ParseSig = parseSig;
                    cache.ClearDisplayCache();
                    parseStatus = "New";
                }

                var dispSig = new DisplaySig(parseSig, breiteMin, breiteMax, farbstufen, spanMin);
                if (cache.DisplaySig.Equals(dispSig)) dispStatus = "Hit";
                else
                {
                    cache.RebuildDisplayCache(breiteMin, breiteMax, farbstufen, spanMin);
                    cache.DisplaySig = dispSig;
                    dispStatus = "New";
                }

                int totalLines = cache.Segments.Count;
                int limitIdx = (int)(fortschritt * totalLines) - 1;

                if (aktiv && totalLines > 0 && limitIdx >= 0)
                {
                    if (vorschau3D)
                    {
                        int fullChunks = (limitIdx + 1) / ChunkSize;
                        int rem = (limitIdx + 1) % ChunkSize;
                        for (int i = 0; i < fullChunks && i < cache.MeshChunks.Count; i++) { previewGeo.Add(cache.MeshChunks[i]); picture.Mesh(cache.MeshChunks[i]); }
                        if (rem > 0 && fullChunks < cache.MeshChunks.Count)
                        {
                            Mesh partial = cache.BuildPartialMesh(fullChunks * ChunkSize, limitIdx + 1, breiteMin, breiteMax, spanMin);
                            if (partial != null && partial.Faces.Count > 0) { previewGeo.Add(partial); picture.Mesh(partial); }
                        }
                    }
                    else
                    {
                        for (int k = 0; k < cache.BucketIds.Count; k++)
                        {
                            List<int> ids = cache.BucketIds[k];
                            if (ids == null || ids.Count == 0) continue;
                            int count = UpperBound(ids, limitIdx);
                            if (count <= 0) continue;
                            var lines = cache.BucketLines[k];
                            for (int j = 0; j < count; j++) { previewGeo.Add(lines[j]); picture.Line(lines[j], cache.BucketColors[k], cache.BucketWidths[k]); }
                        }
                    }
                }

                info = $"Segments: {totalLines} | Visible: {Math.Max(0, limitIdx + 1)} | Preview: {(vorschau3D ? "3D Ribbons (GH Preview)" : "Lines (Conduit)")} | Parse: {parseStatus} | Display: {dispStatus}";
                // programs that are no longer there
                foreach (var gone in _caches.Keys.Where(i => i >= Math.Max(1, access.Iterations)).ToList()) _caches.Remove(gone);
            }

            ShowColours(picture);
            access.SetTwig(0, previewGeo.ToArray(), null, null);
            access.SetItem(1, info);
        }

        private static ParseSig CheapSig(string s)
        {
            if (string.IsNullOrEmpty(s)) return new ParseSig(0, 0, 0, 0);
            // the whole text counts: a changed layer keeps length, start and end the same
            unchecked
            {
                ulong h = 14695981039346656037UL;
                int d = 5381;
                for (int i = 0; i < s.Length; i++) { h = (h ^ s[i]) * 1099511628211UL; d = d * 33 + s[i]; }
                return new ParseSig(s.Length, (int)h, (int)(h >> 32), d);
            }
        }

        private static void ParseRaw(string txt, List<Segment> coords, List<double> epm)
        {
            coords.Clear();
            epm.Clear();
            if (string.IsNullOrEmpty(txt)) return;

            double px = 0.0, py = 0.0, pz = 0.0;
            bool havePrev = false;
            foreach (string ln in txt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (string.IsNullOrEmpty(ln)) continue;
                if (ln.IndexOf("XE=[", StringComparison.Ordinal) < 0 || ln.IndexOf("G1", StringComparison.Ordinal) < 0) continue;
                if (!TryParseXe(ln, out double e)) continue;
                if (!TryParseXYZ(ln, out double x, out double y, out double z)) continue;

                if (havePrev && e > 0.0)
                {
                    double dx = x - px, dy = y - py, dz = z - pz;
                    double sl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (sl > 1e-6) { coords.Add(new Segment(px, py, pz, x, y, z)); epm.Add(e / sl); }
                }
                px = x; py = y; pz = z;
                havePrev = true;
            }
        }

        private static bool TryParseXe(string line, out double e)
        {
            e = 0.0;
            int i = line.IndexOf("XE=[", StringComparison.Ordinal);
            if (i < 0) return false;
            i += 4;
            int j = i;
            while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.' || line[j] == '-')) j++;
            if (j <= i) return false;
            return double.TryParse(line.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out e);
        }

        private static bool TryParseXYZ(string line, out double x, out double y, out double z)
        {
            x = y = z = 0.0;
            bool hx = false, hy = false, hz = false;
            foreach (string tok in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) continue;
                char c = tok[0];
                if (c == 'X') { x = d; hx = true; }
                else if (c == 'Y') { y = d; hy = true; }
                else if (c == 'Z') { z = d; hz = true; }
            }
            return hx && hy && hz;
        }

        private static int UpperBound(List<int> ids, int value)
        {
            int lo = 0, hi = ids.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (ids[mid] <= value) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private readonly struct ParseSig : IEquatable<ParseSig>
        {
            public readonly int N, A, B, C;
            public ParseSig(int n, int a, int b, int c) { N = n; A = a; B = b; C = c; }
            public bool Equals(ParseSig other) => N == other.N && A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is ParseSig other && Equals(other);
            public override int GetHashCode() { unchecked { int h = N; h = (h * 397) ^ A; h = (h * 397) ^ B; h = (h * 397) ^ C; return h; } }
        }

        private readonly struct DisplaySig : IEquatable<DisplaySig>
        {
            public readonly ParseSig Parse;
            public readonly double BMin, BMax, MinRel;
            public readonly int Buckets;
            public DisplaySig(ParseSig parse, double bMin, double bMax, int buckets, double minRel) { Parse = parse; BMin = bMin; BMax = bMax; Buckets = buckets; MinRel = minRel; }
            public bool Equals(DisplaySig other) => Parse.Equals(other.Parse) && BMin.Equals(other.BMin) && BMax.Equals(other.BMax) && Buckets == other.Buckets && MinRel.Equals(other.MinRel);
            public override bool Equals(object obj) => obj is DisplaySig other && Equals(other);
            public override int GetHashCode() { unchecked { int h = Parse.GetHashCode(); h = (h * 397) ^ BMin.GetHashCode(); h = (h * 397) ^ BMax.GetHashCode(); h = (h * 397) ^ Buckets; h = (h * 397) ^ MinRel.GetHashCode(); return h; } }
        }

        private readonly struct Segment
        {
            public readonly double X0, Y0, Z0, X1, Y1, Z1;
            public Segment(double x0, double y0, double z0, double x1, double y1, double z1) { X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; }
        }

        private sealed class PreviewCache
        {
            public ParseSig ParseSig;
            public DisplaySig DisplaySig;
            public readonly List<Segment> Segments = new List<Segment>();
            public readonly List<double> Epm = new List<double>();
            public readonly List<List<int>> BucketIds = new List<List<int>>();
            public readonly List<List<Line>> BucketLines = new List<List<Line>>();
            public readonly List<Drawing.Color> BucketColors = new List<Drawing.Color>();
            public readonly List<int> BucketWidths = new List<int>();
            public readonly List<Mesh> MeshChunks = new List<Mesh>();
            public BoundingBox BBox = BoundingBox.Empty;

            public void ClearDisplayCache()
            {
                MeshChunks.Clear(); BucketIds.Clear(); BucketLines.Clear(); BucketColors.Clear(); BucketWidths.Clear();
                BBox = BoundingBox.Empty;
                DisplaySig = default;
            }

            public void RebuildDisplayCache(double bMin, double bMax, int nBuck, double minRel)
            {
                MeshChunks.Clear(); // not disposed: the meshes are outputs and may still be in use
                BucketIds.Clear(); BucketLines.Clear(); BucketColors.Clear(); BucketWidths.Clear();

                for (int k = 0; k < nBuck; k++)
                {
                    BucketIds.Add(new List<int>());
                    BucketLines.Add(new List<Line>());
                    double f = nBuck > 1 ? k / (double)(nBuck - 1) : 0.0;
                    BucketColors.Add(Drawing.Color.FromArgb(255, (int)(255 * f), 0, (int)(255 * (1.0 - f))));
                    BucketWidths.Add(Math.Max(1, (int)Math.Round(bMin + f * (bMax - bMin))));
                }

                double eLo = Epm.Count > 0 ? Epm.Min() : 0.0;
                double eHi = Epm.Count > 0 ? Epm.Max() : 1.0;
                double eSpan = NormSpan(eLo, eHi, minRel);
                double invSpan = eSpan > 1e-9 ? 1.0 / eSpan : 1.0;

                var box = BoundingBox.Empty;
                Mesh currentMesh = new Mesh();
                int vIdx = 0;
                for (int idx = 0; idx < Segments.Count; idx++)
                {
                    Segment s = Segments[idx];
                    box.Union(new Point3d(s.X0, s.Y0, s.Z0)); box.Union(new Point3d(s.X1, s.Y1, s.Z1));

                    double normF = (Epm[idx] - eLo) * invSpan;
                    double wReal = bMin + normF * (bMax - bMin);
                    int k = (int)(normF * (nBuck - 1) + 0.5);
                    if (k < 0) k = 0; else if (k >= nBuck) k = nBuck - 1;

                    BucketIds[k].Add(idx);
                    BucketLines[k].Add(new Line(new Point3d(s.X0, s.Y0, s.Z0), new Point3d(s.X1, s.Y1, s.Z1)));

                    AddRibbon(currentMesh, s, wReal, BucketColors[k], ref vIdx);
                    if (currentMesh.Faces.Count >= ChunkSize)
                    {
                        MeshChunks.Add(currentMesh);
                        currentMesh = new Mesh();
                        vIdx = 0;
                    }
                }
                if (currentMesh.Faces.Count > 0) MeshChunks.Add(currentMesh); else currentMesh.Dispose();
                BBox = Segments.Count > 0 ? box : BoundingBox.Empty;
            }

            public Mesh BuildPartialMesh(int startIdx, int endIdx, double bMin, double bMax, double minRel)
            {
                if (startIdx >= endIdx || startIdx < 0 || endIdx > Segments.Count) return null;
                double eLo = Epm.Count > 0 ? Epm.Min() : 0.0;
                double eHi = Epm.Count > 0 ? Epm.Max() : 1.0;
                double invSpan = 1.0 / NormSpan(eLo, eHi, minRel);
                int nBuck = BucketColors.Count > 0 ? BucketColors.Count : 16;

                Mesh partial = new Mesh();
                int vIdx = 0;
                for (int idx = startIdx; idx < endIdx; idx++)
                {
                    double normF = (Epm[idx] - eLo) * invSpan;
                    double wReal = bMin + normF * (bMax - bMin);
                    int k = (int)(normF * (nBuck - 1) + 0.5);
                    if (k < 0) k = 0; else if (k >= nBuck) k = nBuck - 1;
                    AddRibbon(partial, Segments[idx], wReal, BucketColors[k], ref vIdx);
                }
                return partial;
            }

            /// <summary>One flat quad of the bead width along the segment.</summary>
            private static void AddRibbon(Mesh mesh, Segment s, double wReal, Drawing.Color col, ref int vIdx)
            {
                double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, l2 = dx * dx + dy * dy;
                double nx, ny;
                if (l2 > 1e-12) { double l = Math.Sqrt(l2); nx = -dy / l * (wReal * 0.5); ny = dx / l * (wReal * 0.5); }
                else { nx = wReal * 0.5; ny = 0.0; }

                mesh.Vertices.Add((float)(s.X0 + nx), (float)(s.Y0 + ny), (float)s.Z0);
                mesh.Vertices.Add((float)(s.X0 - nx), (float)(s.Y0 - ny), (float)s.Z0);
                mesh.Vertices.Add((float)(s.X1 - nx), (float)(s.Y1 - ny), (float)s.Z1);
                mesh.Vertices.Add((float)(s.X1 + nx), (float)(s.Y1 + ny), (float)s.Z1);
                for (int i = 0; i < 4; i++) mesh.VertexColors.Add(col);
                mesh.Faces.AddFace(vIdx, vIdx + 1, vIdx + 2, vIdx + 3);
                vIdx += 4;
            }

            private static double NormSpan(double lo, double hi, double minRel)
            {
                double span = hi - lo;
                double floor = lo > 0.0 ? lo * minRel : 1.0;
                if (span < floor) span = floor;
                return span > 1e-9 ? span : 1.0;
            }
        }
    }

    // ============================================================== DXR E Remap (GH1: Components/Utils/GCodeERemapMultiPartComponent.cs)
    [IoId("7d18022a-7cfb-4e0d-acb8-112b5c06fb9f")]
    public sealed class DxrERemapComponent : Component
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Guid, RemapCache> CacheByComponent = new Dictionary<Guid, RemapCache>();

        public DxrERemapComponent() : base(Io.Name("DXR E Remap", "Recalculates the XE values of one DXR program from several part geometries, each with its own wall thickness or gradient.", Io.Dxr, 1)) { }
        public DxrERemapComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("GCodeERemapMultiPartIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("GCode In", "Code", "Full G-code as text or list of lines.", Access.Twig);
            inputs.AddNumber("Original Width", "Wold", "Original constant line width used by source code.").Set(4.0);
            inputs.AddGeneric("Part Geometry", "PartGeo", "Original part geometries (merged list).", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("Geometry ID", "GeoID", "IDs aligned with Part Geometry.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("Part Info", "PartInfo", "Info rows: ID;axis;mode;Min_T;Max_T;Lo;Hi (old format without mode also supported).", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Offset", "V", "Slice translation (vector or point).", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Outline Dist", "OD", "Distance threshold for outline detection. 0 disables outline logic.").Set(0.0);
            inputs.AddInteger("Decimals", "Dec", "Decimal places for new XE values.").Set(6);
            inputs.AddTransform("Slice XForm", "XF", "Optional forward slice transform. It is inverted internally for mapping.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Debug", "Dbg", "When true, fills detailed debug outputs (can be heavy).", Access.Item, Requirement.MayBeMissing).Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("GCode Out", "Out", "Remapped G-code string.");
            outputs.AddText("GCode Out Text", "OutTxt", "Same as GCode Out (compat output).");
            outputs.AddNumber("E New", "Enew", "New XE values (debug mode).", Access.Twig);
            outputs.AddNumber("E Old", "Eold", "Original XE values (debug mode).", Access.Twig);
            outputs.AddNumber("Used Thickness", "D", "Applied local widths (debug mode).", Access.Twig);
            outputs.AddInteger("Part Index", "Pi", "Assigned part index per movement line (debug mode).", Access.Twig);
            outputs.AddPoint("Mapped Points", "Pts", "Transformed points in source-geometry space (debug mode).", Access.Twig);
            outputs.AddText("Report", "R", "Diagnostic report.");
        }

        protected override void Process(IDataAccess access)
        {
            object[] rawCode;
            access.ItemArray(0, out rawCode);
            double breiteAlt; object versatzObj = null; double outlineDist; int dezimal; bool debug;
            if (!access.GetItem(1, out breiteAlt)) breiteAlt = 4.0;
            object[] geoRaw;
            access.ItemArray(2, out geoRaw);
            var teilGeo = (geoRaw ?? new object[0]).Select(DxrTools.ToGeometry).ToList();
            var geoIds = FormworkIo.Texts(access, 3);
            var teilInfo = FormworkIo.Texts(access, 4);
            access.GetItem(5, out versatzObj);
            if (!access.GetItem(6, out outlineDist)) outlineDist = 0.0;
            if (!access.GetItem(7, out dezimal)) dezimal = 6;
            bool hasSliceXForm = access.GetTransform(8, out Transform sliceXForm);
            if (!access.GetItem(9, out debug)) debug = false;

            string text = DxrTools.JoinText(rawCode);
            // the cache of this component is shared by its iterations: one at a time
            lock (GetOrCreateCache(InstanceId))
            {
                if (breiteAlt == 0.0)
                    breiteAlt = 1.0;
                if (breiteAlt < 0.0)
                    breiteAlt = Math.Abs(breiteAlt);
                if (breiteAlt <= 0.0)
                    breiteAlt = 4.0;

                if (outlineDist < 0.0)
                    outlineDist = 0.0;
                if (dezimal < 0)
                    dezimal = 6;

                ParseSig textSig = TextSig(text);

                double vx = 0.0;
                double vy = 0.0;
                double vz = 0.0;
                ParseOffset(versatzObj, ref vx, ref vy, ref vz);

                var infoById = new Dictionary<string, TeilParam>(StringComparer.OrdinalIgnoreCase);
                var allMinT = new List<double>();
                for (int i = 0; i < teilInfo.Count; i++)
                {
                    if (TryParseTeilInfo(teilInfo[i], out string id, out TeilParam tp))
                    {
                        infoById[id] = tp;
                        allMinT.Add(tp.MinT);
                    }
                }

                double outlineDicke = allMinT.Count > 0 ? Min(allMinT) : breiteAlt;

                var teilParams = new List<TeilParam>(teilGeo.Count);
                for (int i = 0; i < teilGeo.Count; i++)
                {
                    string gid = i < geoIds.Count ? geoIds[i] : null;
                    if (!string.IsNullOrWhiteSpace(gid) && infoById.TryGetValue(gid, out TeilParam matched))
                    {
                        teilParams.Add(matched);
                    }
                    else
                    {
                        teilParams.Add(new TeilParam(3, 0, breiteAlt, breiteAlt, 0.0, 1.0));
                    }
                }

                Transform xform = BuildInverseTransform(hasSliceXForm, sliceXForm, vx, vy, vz);

                var teilCenters = new List<Point3d>(teilGeo.Count);
                var teilBboxes = new List<BoundingBox>(teilGeo.Count);
                var centersXYZ = new List<Vec3>(teilGeo.Count);
                int geoInvalid = 0;
                for (int i = 0; i < teilGeo.Count; i++)
                {
                    if (TryGetBoundingBox(teilGeo[i], out BoundingBox bb))
                    {
                        teilBboxes.Add(bb);
                        teilCenters.Add(bb.Center);
                        centersXYZ.Add(new Vec3(bb.Center.X, bb.Center.Y, bb.Center.Z));
                    }
                    else
                    {
                        geoInvalid++;
                        var zero = new BoundingBox(Point3d.Origin, Point3d.Origin);
                        teilBboxes.Add(zero);
                        teilCenters.Add(Point3d.Origin);
                        centersXYZ.Add(new Vec3(0, 0, 0));
                    }
                }

                RemapCache cache = GetOrCreateCache(InstanceId);

                if (!cache.ParseSig.Equals(textSig))
                {
                    ParseText(text, cache.Parsed);
                    cache.ParseSig = textSig;
                    cache.MapSig = string.Empty;
                    cache.Mapped.Clear();
                    cache.SampleInfo = new SampleInfo(0, 0, 0);
                }

                string mapSig = BuildMapSig(textSig, xform, teilBboxes, teilGeo);
                if (!string.Equals(cache.MapSig, mapSig, StringComparison.Ordinal))
                {
                    BuildMappedCache(cache, mapSig, teilGeo, centersXYZ, xform);
                }

                var outLines = new List<string>(cache.Parsed.Count);
                var eNeu = new List<double>();
                var eAlt = new List<double>();
                var dickeUsed = new List<double>();
                var teilIdx = new List<int>();
                var punkteT = new List<Point3d>();

                double invBAlt = 1.0 / breiteAlt;
                double sumENeu = 0.0;

                int mapIdx = 0;
                for (int i = 0; i < cache.Parsed.Count; i++)
                {
                    ParseEntry entry = cache.Parsed[i];
                    if (!entry.IsMove)
                    {
                        outLines.Add(entry.RawLine ?? string.Empty);
                        continue;
                    }

                    MappedEntry mapped = cache.Mapped[mapIdx++];

                    bool isOutline = false;
                    if (outlineDist > 0.0 && mapped.PartIndex >= 0 && mapped.PartIndex < teilBboxes.Count)
                    {
                        BoundingBox bb = teilBboxes[mapped.PartIndex];
                        double ddx = bb.Min.X - mapped.X;
                        if (ddx < 0.0) ddx = mapped.X - bb.Max.X;
                        if (ddx < 0.0) ddx = 0.0;

                        double ddy = bb.Min.Y - mapped.Y;
                        if (ddy < 0.0) ddy = mapped.Y - bb.Max.Y;
                        if (ddy < 0.0) ddy = 0.0;

                        double ddz = bb.Min.Z - mapped.Z;
                        if (ddz < 0.0) ddz = mapped.Z - bb.Max.Z;
                        if (ddz < 0.0) ddz = 0.0;

                        double sq = ddx * ddx + ddy * ddy + ddz * ddz;
                        isOutline = sq > outlineDist * outlineDist;
                    }

                    double d;
                    if (isOutline)
                    {
                        d = outlineDicke;
                    }
                    else if (mapped.PartIndex >= 0 && mapped.PartIndex < teilParams.Count)
                    {
                        TeilParam tp = teilParams[mapped.PartIndex];
                        if (tp.Modus == 0)
                        {
                            d = tp.MinT;
                        }
                        else if (tp.Modus == 1)
                        {
                            d = tp.MaxT;
                        }
                        else
                        {
                            double v = tp.Achse == 1 ? mapped.X : (tp.Achse == 2 ? mapped.Y : mapped.Z);
                            double f;
                            if (Math.Abs(tp.Hi - tp.Lo) < 1e-12)
                            {
                                f = 0.0;
                            }
                            else
                            {
                                f = (v - tp.Lo) / (tp.Hi - tp.Lo);
                                if (f < 0.0) f = 0.0;
                                if (f > 1.0) f = 1.0;
                            }

                            d = tp.MinT + f * (tp.MaxT - tp.MinT);
                        }
                    }
                    else
                    {
                        d = breiteAlt;
                    }

                    double eNew = entry.EAlt * d * invBAlt;
                    sumENeu += eNew;

                    outLines.Add(entry.Pre + eNew.ToString("F" + dezimal, CultureInfo.InvariantCulture) + entry.Post);

                    if (debug)
                    {
                        eAlt.Add(entry.EAlt);
                        eNeu.Add(eNew);
                        dickeUsed.Add(d);
                        teilIdx.Add(mapped.PartIndex);
                        punkteT.Add(new Point3d(mapped.X, mapped.Y, mapped.Z));
                    }
                }

                int maxHeader = Math.Min(40, outLines.Count);
                for (int i = 0; i < maxHeader; i++)
                {
                    if (outLines[i].StartsWith(";Eges", StringComparison.Ordinal))
                    {
                        outLines[i] = ";Eges = IC[" + sumENeu.ToString("F3", CultureInfo.InvariantCulture) + "]";
                        break;
                    }
                }

                string gcodeOutText = string.Join("\n", outLines);

                int nMatch = 0;
                for (int i = 0; i < teilGeo.Count; i++)
                {
                    if (i < geoIds.Count && !string.IsNullOrWhiteSpace(geoIds[i]) && infoById.ContainsKey(geoIds[i]))
                        nMatch++;
                }

                string beispielVorher = string.Empty;
                string beispielNachher = string.Empty;
                for (int i = 0; i < cache.Parsed.Count; i++)
                {
                    ParseEntry p = cache.Parsed[i];
                    if (p.IsMove)
                    {
                        beispielVorher = p.Pre + p.EAlt.ToString(CultureInfo.InvariantCulture) + p.Post;
                        beispielNachher = outLines[i];
                        break;
                    }
                }

                Bounds3 pointBounds = ComputeMappedBounds(cache.Mapped);
                Bounds3 centerBounds = ComputeCenterBounds(centersXYZ);
                string modusZeile = BuildModeLine(cache.Mapped, teilParams);
                string sampZeile = $"Sampling: {cache.SampleInfo.SampleCount} Punkte | BBox-Fallback: {cache.SampleInfo.BBoxFallbackCount} Teile | fehlgeschlagen: {cache.SampleInfo.FailedCount}";

                string report =
                    $"Geometrien: {teilGeo.Count} (UNGUELTIG: {geoInvalid}!) | Geo_IDs: {geoIds.Count} | Infos: {infoById.Count} | ID-Matches: {nMatch} | Modi: [{ModeList(teilParams)}]\n" +
                    sampZeile + "\n" +
                    modusZeile + "\n" +
                    $"Punkte_T BBox: X {pointBounds.MinX:0}..{pointBounds.MaxX:0}  Y {pointBounds.MinY:0}..{pointBounds.MaxY:0}  Z {pointBounds.MinZ:0}..{pointBounds.MaxZ:0}\n" +
                    $"Zentren  BBox: X {centerBounds.MinX:0}..{centerBounds.MaxX:0}  Y {centerBounds.MinY:0}..{centerBounds.MaxY:0}  Z {centerBounds.MinZ:0}..{centerBounds.MaxZ:0}\n" +
                    "-> Boxen muessen sich ueberlappen. Bei UNGUELTIG > 0: Teil_Geo-Input hat falschen Type Hint (Brep/Geometry statt Text).\n" +
                    $"PFAD-CHECK: Zeilen in {cache.Parsed.Count} = Zeilen out {outLines.Count} | Koordinaten unveraendert (nur XE ersetzt)\n" +
                    $"Beispiel vorher : {Trim110(beispielVorher)}\n" +
                    $"Beispiel nachher: {Trim110(beispielNachher)}";

                access.SetItem(0, gcodeOutText);
                access.SetItem(1, gcodeOutText);
                access.SetTwig(2, eNeu.ToArray(), null, null);
                access.SetTwig(3, eAlt.ToArray(), null, null);
                access.SetTwig(4, dickeUsed.ToArray(), null, null);
                access.SetTwig(5, teilIdx.ToArray(), null, null);
                access.SetTwig(6, punkteT.ToArray(), null, null);
                access.SetItem(7, report);
            }
        }

        private static RemapCache GetOrCreateCache(Guid componentGuid)
        {
            lock (CacheLock)
            {
                if (!CacheByComponent.TryGetValue(componentGuid, out RemapCache cache))
                {
                    cache = new RemapCache();
                    CacheByComponent[componentGuid] = cache;
                }

                return cache;
            }
        }

        private static void ParseText(string text, List<ParseEntry> parsed)
        {
            parsed.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.IndexOf("XE=[", StringComparison.Ordinal) < 0 || ln.IndexOf("G1", StringComparison.Ordinal) < 0)
                {
                    parsed.Add(ParseEntry.Raw(ln));
                    continue;
                }

                if (!TryParseXYZ(ln, out double x, out double y, out double z))
                {
                    parsed.Add(ParseEntry.Raw(ln));
                    continue;
                }

                int xi = ln.IndexOf("XE=[", StringComparison.Ordinal);
                if (xi < 0)
                {
                    parsed.Add(ParseEntry.Raw(ln));
                    continue;
                }

                xi += 4;
                int xj = xi;
                while (xj < ln.Length)
                {
                    char ch = ln[xj];
                    if (char.IsDigit(ch) || ch == '.' || ch == '-')
                    {
                        xj++;
                        continue;
                    }

                    break;
                }

                if (xj <= xi)
                {
                    parsed.Add(ParseEntry.Raw(ln));
                    continue;
                }

                if (!double.TryParse(ln.Substring(xi, xj - xi), NumberStyles.Float, CultureInfo.InvariantCulture, out double eAlt))
                {
                    parsed.Add(ParseEntry.Raw(ln));
                    continue;
                }

                parsed.Add(ParseEntry.Move(ln.Substring(0, xi), ln.Substring(xj), x, y, z, eAlt));
            }
        }

        private static bool TryParseXYZ(string ln, out double x, out double y, out double z)
        {
            x = y = z = 0.0;
            bool hx = false;
            bool hy = false;
            bool hz = false;

            string[] tokens = ln.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok.Length < 2)
                    continue;

                char c = tok[0];
                string val = tok.Substring(1);
                if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    continue;

                if (c == 'X') { x = d; hx = true; }
                else if (c == 'Y') { y = d; hy = true; }
                else if (c == 'Z') { z = d; hz = true; }
            }

            return hx && hy && hz;
        }

        private static bool TryParseTeilInfo(string row, out string id, out TeilParam tp)
        {
            id = null;
            tp = default;
            if (string.IsNullOrWhiteSpace(row))
                return false;

            string[] parts = row.Split(';');
            if (parts.Length < 6)
                return false;

            id = parts[0]?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return false;

            if (parts.Length >= 7)
            {
                tp = new TeilParam(
                    ParseInt(parts[1], 3),
                    ParseInt(parts[2], 2),
                    ParseDouble(parts[3], 1.0),
                    ParseDouble(parts[4], 1.0),
                    ParseDouble(parts[5], 0.0),
                    ParseDouble(parts[6], 1.0));
            }
            else
            {
                tp = new TeilParam(
                    ParseInt(parts[1], 3),
                    2,
                    ParseDouble(parts[2], 1.0),
                    ParseDouble(parts[3], 1.0),
                    ParseDouble(parts[4], 0.0),
                    ParseDouble(parts[5], 1.0));
            }

            return true;
        }

        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            if (double.TryParse((s ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return (int)Math.Round(d);
            return fallback;
        }

        private static double ParseDouble(string s, double fallback)
        {
            if (double.TryParse((s ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
            return fallback;
        }

        private static ParseSig TextSig(string s)
        {
            if (string.IsNullOrEmpty(s))
                return new ParseSig(0, 0, 0, 0);

            // the whole text counts: a changed temperature or layer keeps length, start and end the same
            unchecked
            {
                ulong h = 14695981039346656037UL;
                int d = 5381;
                for (int i = 0; i < s.Length; i++)
                {
                    h = (h ^ s[i]) * 1099511628211UL;
                    d = d * 33 + s[i];
                }
                return new ParseSig(s.Length, (int)h, (int)(h >> 32), d);
            }
        }

        private static bool TryGetBoundingBox(GeometryBase g, out BoundingBox bb)
        {
            bb = BoundingBox.Empty;
            if (g == null)
                return false;

            try
            {
                bb = g.GetBoundingBox(true);
                return bb.IsValid;
            }
            catch
            {
                return false;
            }
        }

        private static Transform BuildInverseTransform(bool hasSliceXForm, Transform sliceXForm, double vx, double vy, double vz)
        {
            if (hasSliceXForm)
            {
                try
                {
                    if (sliceXForm.TryGetInverse(out Transform inv))
                        return inv;
                }
                catch
                {
                    // Fall back below.
                }
            }

            double halfPi = 0.5 * Math.PI;
            Point3d origin = Point3d.Origin;
            Transform rotZInv = Transform.Rotation(-halfPi, Vector3d.ZAxis, origin);
            Transform rotYInv = Transform.Rotation(+halfPi, Vector3d.YAxis, origin);
            Transform transInv = Transform.Translation(-vx, -vy, -vz);
            return rotZInv * rotYInv * transInv;
        }

        private static void ParseOffset(object versatzObj, ref double vx, ref double vy, ref double vz)
        {
            if (versatzObj == null)
                return;

            switch (versatzObj)
            {
                case Vector3d v3:
                    vx = v3.X;
                    vy = v3.Y;
                    vz = v3.Z;
                    return;
                case Point3d p3:
                    vx = p3.X;
                    vy = p3.Y;
                    vz = p3.Z;
                    return;
                case IList<double> dl when dl.Count >= 3:
                    vx = dl[0];
                    vy = dl[1];
                    vz = dl[2];
                    return;
                case IList<object> ol when ol.Count >= 3:
                    vx = ParseObjDouble(ol[0]);
                    vy = ParseObjDouble(ol[1]);
                    vz = ParseObjDouble(ol[2]);
                    return;
            }
        }

        private static double ParseObjDouble(object o)
        {
            if (o == null)
                return 0.0;
            if (o is double d)
                return d;
            if (o is float f)
                return f;
            if (o is int i)
                return i;
            if (double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                return r;
            return 0.0;
        }

        private static string BuildMapSig(ParseSig textSig, Transform xf, List<BoundingBox> bbs, List<GeometryBase> geos)
        {
            var sb = new StringBuilder(512);
            sb.Append(textSig.GetHashCode().ToString(CultureInfo.InvariantCulture));
            sb.Append("|surf2|");

            AppendRounded(sb, xf.M00, 9);
            AppendRounded(sb, xf.M01, 9);
            AppendRounded(sb, xf.M02, 9);
            AppendRounded(sb, xf.M03, 6);
            AppendRounded(sb, xf.M10, 9);
            AppendRounded(sb, xf.M11, 9);
            AppendRounded(sb, xf.M12, 9);
            AppendRounded(sb, xf.M13, 6);
            AppendRounded(sb, xf.M20, 9);
            AppendRounded(sb, xf.M21, 9);
            AppendRounded(sb, xf.M22, 9);
            AppendRounded(sb, xf.M23, 6);

            for (int i = 0; i < bbs.Count; i++)
            {
                BoundingBox bb = bbs[i];
                AppendRounded(sb, bb.Min.X, 2);
                AppendRounded(sb, bb.Min.Y, 2);
                AppendRounded(sb, bb.Min.Z, 2);
                AppendRounded(sb, bb.Max.X, 2);
                AppendRounded(sb, bb.Max.Y, 2);
                AppendRounded(sb, bb.Max.Z, 2);
            }

            // the shape itself: another part may have the same box
            for (int i = 0; i < geos.Count; i++)
                sb.Append(ShapeHash(geos[i]).ToString(CultureInfo.InvariantCulture)).Append(';');

            return sb.ToString();
        }

        /// <summary>A number for the shape: mesh vertices, or points on the edges and faces of a Brep.</summary>
        private static int ShapeHash(GeometryBase g)
        {
            unchecked
            {
                int h = 17;
                void Add(Point3d p) { h = h * 31 + p.X.GetHashCode(); h = h * 31 + p.Y.GetHashCode(); h = h * 31 + p.Z.GetHashCode(); }
                try
                {
                    if (g is Mesh m)
                    {
                        h = h * 31 + m.Faces.Count;
                        for (int i = 0; i < m.Vertices.Count; i++) Add(m.Vertices.Point3dAt(i));
                        return h;
                    }
                    Brep b = g as Brep ?? (g is Surface sf ? sf.ToBrep() : g == null ? null : Brep.TryConvertBrep(g));
                    if (b == null) return h;
                    foreach (var v in b.Vertices) Add(v.Location);
                    foreach (var e in b.Edges) Add(e.PointAt(e.Domain.Mid));
                    foreach (var f in b.Faces)
                        for (int u = 0; u <= 4; u++)
                            for (int v = 0; v <= 4; v++)
                                Add(f.PointAt(f.Domain(0).ParameterAt(u / 4.0), f.Domain(1).ParameterAt(v / 4.0)));
                }
                catch { } // the bounding box still counts
                return h;
            }
        }

        private static void AppendRounded(StringBuilder sb, double v, int decimals)
        {
            sb.Append(Math.Round(v, decimals).ToString(CultureInfo.InvariantCulture));
            sb.Append(';');
        }

        private static void BuildMappedCache(RemapCache cache, string mapSig, List<GeometryBase> geos, List<Vec3> centersXYZ, Transform xf)
        {
            cache.Mapped.Clear();
            cache.Mapped.Capacity = Math.Max(cache.Mapped.Capacity, cache.Parsed.Count);

            var samples = new List<SamplePoint>();
            int nMesh = 0;
            int nBbox = 0;
            int nFail = 0;

            for (int i = 0; i < geos.Count; i++)
            {
                SampleResult res = SampleOneGeo(geos[i], i, samples);
                if (res == SampleResult.Mesh) nMesh++;
                else if (res == SampleResult.BBox) nBbox++;
                else nFail++;
            }

            var grid = new Dictionary<CellKey, List<SamplePoint>>(samples.Count / 8 + 1);
            const double cell = 10.0;
            double invCell = 1.0 / cell;

            for (int i = 0; i < samples.Count; i++)
            {
                SamplePoint s = samples[i];
                var key = new CellKey((int)Math.Floor(s.X * invCell), (int)Math.Floor(s.Y * invCell), (int)Math.Floor(s.Z * invCell));
                if (!grid.TryGetValue(key, out List<SamplePoint> bucket))
                {
                    bucket = new List<SamplePoint>();
                    grid[key] = bucket;
                }

                bucket.Add(s);
            }

            const double earlySq = 1.5 * 1.5;

            for (int i = 0; i < cache.Parsed.Count; i++)
            {
                ParseEntry entry = cache.Parsed[i];
                if (!entry.IsMove)
                    continue;

                double mx = xf.M00 * entry.X + xf.M01 * entry.Y + xf.M02 * entry.Z + xf.M03;
                double my = xf.M10 * entry.X + xf.M11 * entry.Y + xf.M12 * entry.Z + xf.M13;
                double mz = xf.M20 * entry.X + xf.M21 * entry.Y + xf.M22 * entry.Z + xf.M23;

                int ti = NearestSurface(mx, my, mz, invCell, grid, centersXYZ, earlySq);
                cache.Mapped.Add(new MappedEntry(mx, my, mz, ti));
            }

            cache.MapSig = mapSig;
            cache.SampleInfo = new SampleInfo(samples.Count, nBbox, nFail);
        }

        private static int NearestSurface(
            double px,
            double py,
            double pz,
            double invCell,
            Dictionary<CellKey, List<SamplePoint>> grid,
            List<Vec3> centers,
            double earlySq)
        {
            int cx = (int)Math.Floor(px * invCell);
            int cy = (int)Math.Floor(py * invCell);
            int cz = (int)Math.Floor(pz * invCell);

            int bestI = -1;
            double bestD = double.MaxValue;

            if (grid.TryGetValue(new CellKey(cx, cy, cz), out List<SamplePoint> ownCell))
            {
                for (int i = 0; i < ownCell.Count; i++)
                {
                    SamplePoint s = ownCell[i];
                    double dx = px - s.X;
                    double dy = py - s.Y;
                    double dz = pz - s.Z;
                    double dd = dx * dx + dy * dy + dz * dz;
                    if (dd < bestD)
                    {
                        bestD = dd;
                        bestI = s.PartIndex;
                    }
                }

                if (bestI >= 0 && bestD <= earlySq)
                    return bestI;
            }

            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        if (ox == 0 && oy == 0 && oz == 0)
                            continue;

                        if (!grid.TryGetValue(new CellKey(cx + ox, cy + oy, cz + oz), out List<SamplePoint> cell1))
                            continue;

                        for (int i = 0; i < cell1.Count; i++)
                        {
                            SamplePoint s = cell1[i];
                            double dx = px - s.X;
                            double dy = py - s.Y;
                            double dz = pz - s.Z;
                            double dd = dx * dx + dy * dy + dz * dz;
                            if (dd < bestD)
                            {
                                bestD = dd;
                                bestI = s.PartIndex;
                            }
                        }
                    }
                }
            }

            if (bestI >= 0)
                return bestI;

            for (int ox = -2; ox <= 2; ox++)
            {
                for (int oy = -2; oy <= 2; oy++)
                {
                    for (int oz = -2; oz <= 2; oz++)
                    {
                        if (ox >= -1 && ox <= 1 && oy >= -1 && oy <= 1 && oz >= -1 && oz <= 1)
                            continue;

                        if (!grid.TryGetValue(new CellKey(cx + ox, cy + oy, cz + oz), out List<SamplePoint> cell2))
                            continue;

                        for (int i = 0; i < cell2.Count; i++)
                        {
                            SamplePoint s = cell2[i];
                            double dx = px - s.X;
                            double dy = py - s.Y;
                            double dz = pz - s.Z;
                            double dd = dx * dx + dy * dy + dz * dz;
                            if (dd < bestD)
                            {
                                bestD = dd;
                                bestI = s.PartIndex;
                            }
                        }
                    }
                }
            }

            if (bestI >= 0)
                return bestI;

            int ci = -1;
            double cd = double.MaxValue;
            for (int i = 0; i < centers.Count; i++)
            {
                Vec3 c = centers[i];
                double dx = px - c.X;
                double dy = py - c.Y;
                double dz = pz - c.Z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < cd)
                {
                    cd = d;
                    ci = i;
                }
            }

            return ci;
        }

        private static SampleResult SampleOneGeo(GeometryBase g, int partIndex, List<SamplePoint> outSamples)
        {
            bool added = false;
            try
            {
                if (g is Mesh gm)
                {
                    int vCount = gm.Vertices.Count;
                    for (int i = 0; i < vCount; i++)
                    {
                        Point3f p = gm.Vertices[i];
                        outSamples.Add(new SamplePoint(p.X, p.Y, p.Z, partIndex));
                        added = true;
                    }

                    if (added) return SampleResult.Mesh;
                }

                Brep b = null;
                if (g is Brep br) b = br;
                else if (g is Surface sf) b = sf.ToBrep();
                else if (g != null) b = Brep.TryConvertBrep(g);

                if (b != null)
                {
                    var mp = MeshingParameters.Default;
                    mp.MaximumEdgeLength = 4.0;
                    mp.MinimumEdgeLength = 0.8;
                    mp.GridMinCount = 8;

                    Mesh[] arr = Mesh.CreateFromBrep(b, mp);
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Mesh m = arr[i];
                            if (m == null)
                                continue;

                            int vc = m.Vertices.Count;
                            for (int vi = 0; vi < vc; vi++)
                            {
                                Point3f p = m.Vertices[vi];
                                outSamples.Add(new SamplePoint(p.X, p.Y, p.Z, partIndex));
                                added = true;
                            }
                        }
                    }

                    if (added) return SampleResult.Mesh;
                }
            }
            catch
            {
                // Fallback below.
            }

            try
            {
                if (g == null)
                    return SampleResult.Fail;

                BoundingBox bb = g.GetBoundingBox(true);
                Point3d[] corners = bb.GetCorners();
                for (int i = 0; i < corners.Length; i++)
                {
                    Point3d p = corners[i];
                    outSamples.Add(new SamplePoint(p.X, p.Y, p.Z, partIndex));
                    added = true;
                }

                Point3d c = bb.Center;
                outSamples.Add(new SamplePoint(c.X, c.Y, c.Z, partIndex));
                return added ? SampleResult.BBox : SampleResult.Fail;
            }
            catch
            {
                return SampleResult.Fail;
            }
        }

        private static double Min(List<double> values)
        {
            double min = double.MaxValue;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < min)
                    min = values[i];
            }

            return min;
        }

        private static Bounds3 ComputeMappedBounds(List<MappedEntry> mapped)
        {
            if (mapped == null || mapped.Count == 0)
                return Bounds3.Zero;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            for (int i = 0; i < mapped.Count; i++)
            {
                MappedEntry m = mapped[i];
                if (m.X < minX) minX = m.X;
                if (m.Y < minY) minY = m.Y;
                if (m.Z < minZ) minZ = m.Z;
                if (m.X > maxX) maxX = m.X;
                if (m.Y > maxY) maxY = m.Y;
                if (m.Z > maxZ) maxZ = m.Z;
            }

            return new Bounds3(minX, maxX, minY, maxY, minZ, maxZ);
        }

        private static Bounds3 ComputeCenterBounds(List<Vec3> centers)
        {
            if (centers == null || centers.Count == 0)
                return Bounds3.Zero;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            for (int i = 0; i < centers.Count; i++)
            {
                Vec3 c = centers[i];
                if (c.X < minX) minX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X;
                if (c.Y > maxY) maxY = c.Y;
                if (c.Z > maxZ) maxZ = c.Z;
            }

            return new Bounds3(minX, maxX, minY, maxY, minZ, maxZ);
        }

        private static string BuildModeLine(List<MappedEntry> mapped, List<TeilParam> teilParams)
        {
            int c0 = 0;
            int c1 = 0;
            int c2 = 0;
            int total = 0;

            for (int i = 0; i < mapped.Count; i++)
            {
                int ti = mapped[i].PartIndex;
                if (ti < 0 || ti >= teilParams.Count)
                    continue;

                int m = teilParams[ti].Modus;
                if (m == 0) c0++;
                else if (m == 1) c1++;
                else c2++;
                total++;
            }

            if (total <= 0)
                return "Punkte je Modus: keine";

            double p0 = 100.0 * c0 / total;
            double p1 = 100.0 * c1 / total;
            double p2 = 100.0 * c2 / total;
            return $"Punkte je Modus: duenn(0) {p0:0.0}% | dick(1) {p1:0.0}% | Verlauf(2) {p2:0.0}%";
        }

        private static string ModeList(List<TeilParam> teilParams)
        {
            if (teilParams == null || teilParams.Count == 0)
                return string.Empty;

            var vals = new string[teilParams.Count];
            for (int i = 0; i < teilParams.Count; i++)
                vals[i] = teilParams[i].Modus.ToString(CultureInfo.InvariantCulture);

            return string.Join(",", vals);
        }

        private static string Trim110(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length > 110 ? s.Substring(0, 110) : s;
        }

        private sealed class RemapCache
        {
            public ParseSig ParseSig;
            public string MapSig = string.Empty;
            public readonly List<ParseEntry> Parsed = new List<ParseEntry>();
            public readonly List<MappedEntry> Mapped = new List<MappedEntry>();
            public SampleInfo SampleInfo;
        }

        private readonly struct ParseSig : IEquatable<ParseSig>
        {
            public readonly int N;
            public readonly int A;
            public readonly int B;
            public readonly int C;

            public ParseSig(int n, int a, int b, int c)
            {
                N = n;
                A = a;
                B = b;
                C = c;
            }

            public bool Equals(ParseSig other)
            {
                return N == other.N && A == other.A && B == other.B && C == other.C;
            }

            public override bool Equals(object obj)
            {
                return obj is ParseSig other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = N;
                    h = (h * 397) ^ A;
                    h = (h * 397) ^ B;
                    h = (h * 397) ^ C;
                    return h;
                }
            }
        }

        private readonly struct ParseEntry
        {
            public readonly bool IsMove;
            public readonly string RawLine;
            public readonly string Pre;
            public readonly string Post;
            public readonly double X;
            public readonly double Y;
            public readonly double Z;
            public readonly double EAlt;

            private ParseEntry(bool isMove, string rawLine, string pre, string post, double x, double y, double z, double eAlt)
            {
                IsMove = isMove;
                RawLine = rawLine;
                Pre = pre;
                Post = post;
                X = x;
                Y = y;
                Z = z;
                EAlt = eAlt;
            }

            public static ParseEntry Raw(string line) => new ParseEntry(false, line, null, null, 0, 0, 0, 0);
            public static ParseEntry Move(string pre, string post, double x, double y, double z, double eAlt)
                => new ParseEntry(true, null, pre, post, x, y, z, eAlt);
        }

        private readonly struct MappedEntry
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;
            public readonly int PartIndex;

            public MappedEntry(double x, double y, double z, int partIndex)
            {
                X = x;
                Y = y;
                Z = z;
                PartIndex = partIndex;
            }
        }

        private readonly struct TeilParam
        {
            public readonly int Achse;
            public readonly int Modus;
            public readonly double MinT;
            public readonly double MaxT;
            public readonly double Lo;
            public readonly double Hi;

            public TeilParam(int achse, int modus, double minT, double maxT, double lo, double hi)
            {
                Achse = achse;
                Modus = modus;
                MinT = minT;
                MaxT = maxT;
                Lo = lo;
                Hi = hi;
            }
        }

        private readonly struct SamplePoint
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;
            public readonly int PartIndex;

            public SamplePoint(double x, double y, double z, int partIndex)
            {
                X = x;
                Y = y;
                Z = z;
                PartIndex = partIndex;
            }
        }

        private readonly struct SampleInfo
        {
            public readonly int SampleCount;
            public readonly int BBoxFallbackCount;
            public readonly int FailedCount;

            public SampleInfo(int sampleCount, int bboxFallbackCount, int failedCount)
            {
                SampleCount = sampleCount;
                BBoxFallbackCount = bboxFallbackCount;
                FailedCount = failedCount;
            }
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public CellKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(CellKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = X;
                    h = (h * 397) ^ Y;
                    h = (h * 397) ^ Z;
                    return h;
                }
            }
        }

        private readonly struct Vec3
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public Vec3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        private readonly struct Bounds3
        {
            public readonly double MinX;
            public readonly double MaxX;
            public readonly double MinY;
            public readonly double MaxY;
            public readonly double MinZ;
            public readonly double MaxZ;

            public static readonly Bounds3 Zero = new Bounds3(0, 0, 0, 0, 0, 0);

            public Bounds3(double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
                MinZ = minZ;
                MaxZ = maxZ;
            }
        }

        private enum SampleResult
        {
            Mesh,
            BBox,
            Fail
        }
    }

    // ============================================================== surface thickness heatmap (GH1: Components/Utils/SurfaceThicknessHeatmapComponent.cs)
    [IoId("135802b7-d753-4755-ae27-0ae3ea1d9cf3")]
    public sealed class SurfaceThicknessHeatmapComponent : ColourPreviewComponent
    {
        public SurfaceThicknessHeatmapComponent() : base(Io.Name("Surface Thickness Heatmap", "Measures the wall thickness of each part as a coloured mesh and gives the Part Info for DXR E Remap; also rebuilds untrimmed fit surfaces from Breps and surfaces.", Io.Dxr, 1)) { }
        public SurfaceThicknessHeatmapComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SurfaceThicknessHeatmapIcon");
        // the heatmap mesh in its vertex colours (Grasshopper 2 would draw it grey)
        protected override int[] OwnOutputs => new[] { 5 };

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("S", "S", "Input Breps/Surfaces. Each Brep remains one logical unit.", Access.Twig);
            inputs.AddGeneric("U", "U", "U values per Brep (tree/branches recommended).", Access.Tree);
            inputs.AddGeneric("V", "V", "V values per Brep (tree/branches recommended).", Access.Tree);
            inputs.AddGeneric("Mode", "Mode", "Mode per sample: 0=min, 1=max, 2=gradient.", Access.Tree);
            inputs.AddNumber("Min T", "MinT", "Minimum thickness.");
            inputs.AddNumber("Max T", "MaxT", "Maximum thickness.");
            inputs.AddInteger("N Contours", "N", "Cross-width contour count used for rebuild detail.", Access.Item, Requirement.MayBeMissing).Set(10);
            inputs.AddInteger("Axis", "Axis", "Gradient axis: 0=auto, 1=X, 2=Y, 3=Z (top->bottom).", Access.Item, Requirement.MayBeMissing).Set(3).Choices("Auto", "X", "Y", "Z");
            inputs.AddNumber("Kink Angle", "Kink", "Reserved split angle in degrees (currently informational).", Access.Item, Requirement.MayBeMissing).Set(30.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Info", "Info", "Component parameter bundle for downstream G-code mapping.");
            outputs.AddText("Geo_ID", "GeoID", "Per S_out geometry ID.", Access.Twig);
            outputs.AddNumber("Thickness", "T", "Sampled thickness values.", Access.Twig);
            outputs.AddColour("Color", "Col", "Sampled colors.", Access.Twig);
            outputs.AddPoint("Points3D", "Pts", "Sampled 3D points.", Access.Twig);
            outputs.AddMesh("HeatmapMesh", "Heat", "Combined heatmap mesh.");
            outputs.AddGeneric("S_out", "Sout", "Original Brep units (one per logical input Brep).", Access.Twig);
            outputs.AddGeneric("Surfaces_out", "Srf", "Underlying untrimmed surfaces per face.", Access.Twig);
            outputs.AddSurface("Rebuilt_Srf", "ReSrf", "Rebuilt fit surfaces per face.", Access.Twig);
            outputs.AddGeneric("Rebuilt_Joined", "ReJoin", "Joined rebuilt untrimmed surfaces per Brep unit.", Access.Twig);
            outputs.AddPoint("Raster Points", "Grid", "Raster points used for fit rebuild.", Access.Twig);
            outputs.AddCurve("Edge Curves", "Edges", "True trim edge curves for control.", Access.Twig);
            outputs.AddPoint("Contours", "Contours", "Alias of Raster Points for backward compatibility.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            ClearColours();
            object[] geoRaw;
            if (!access.ItemArray(0, out geoRaw) || geoRaw == null || geoRaw.Length == 0)
            {
                Gh1.Message(access, 1, "No geometry connected to S.");
                return;
            }
            var inputGeo = geoRaw.Select(DxrTools.ToGeometry).Where(g => g != null).ToList();
            var uTree = DxrTools.Branches(access, 1);
            var vTree = DxrTools.Branches(access, 2);
            var modeTree = DxrTools.Branches(access, 3);
            double minT, maxT, kinkAngle; int nContours, axisInput;
            if (!access.GetItem(4, out minT)) return;
            if (!access.GetItem(5, out maxT)) return;
            if (!access.GetItem(6, out nContours)) nContours = 10;
            if (!access.GetItem(7, out axisInput)) axisInput = 3;
            if (!access.GetItem(8, out kinkAngle)) kinkAngle = 30.0;

            if (nContours < 2) nContours = 2;
            if (maxT < minT)
            {
                double t = minT;
                minT = maxT;
                maxT = t;
            }

            double tol = 0.001;
            try
            {
                double dt = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
                if (dt > 0.0) tol = dt;
            }
            catch
            {
                tol = 0.001;
            }

            var thickness = new List<double>();
            var colors = new List<Drawing.Color>();
            var points3D = new List<Point3d>();
            var heatmapMesh = new Mesh();
            var sOut = new List<Brep>();
            var geoId = new List<string>();
            var surfacesOut = new List<GeometryBase>();
            var rebuiltSrf = new List<Surface>();
            var rebuiltJoined = new List<Brep>();
            var rasterPts = new List<Point3d>();
            var edgeCurves = new List<Curve>();
            var contoursAlias = new List<Point3d>();

            List<Brep> breps = ExtractBreps(inputGeo);
            int nUnits = breps.Count;
            if (nUnits == 0)
            {
                Gh1.Message(access, 1, "No valid Brep/Surface units extracted from S.");
                SetOutputs(access, string.Empty, geoId, thickness, colors, points3D, heatmapMesh, sOut, surfacesOut, rebuiltSrf, rebuiltJoined, rasterPts, edgeCurves, contoursAlias);
                return;
            }

            List<List<double>> uPerUnit = ToPerUnitValues(uTree, nUnits);
            List<List<double>> vPerUnit = ToPerUnitValues(vTree, nUnits);
            List<List<double>> modePerUnit = ToPerUnitValues(modeTree, nUnits);

            BoundingBox globalBox = BoundingBox.Empty;
            for (int i = 0; i < breps.Count; i++)
            {
                BoundingBox bb = breps[i].GetBoundingBox(true);
                if (!globalBox.IsValid) globalBox = bb;
                else globalBox.Union(bb);
            }

            char gradAxis = SelectAxis(axisInput, globalBox);
            double gradLo;
            double gradHi;
            if (gradAxis == 'z')
            {
                gradLo = globalBox.Max.Z;
                gradHi = globalBox.Min.Z;
            }
            else if (gradAxis == 'y')
            {
                gradLo = globalBox.Min.Y;
                gradHi = globalBox.Max.Y;
            }
            else
            {
                gradLo = globalBox.Min.X;
                gradHi = globalBox.Max.X;
            }

            string id = InstanceId.ToString().Replace(";", "_");
            int axisNum = gradAxis == 'x' ? 1 : (gradAxis == 'y' ? 2 : 3);
            int mode0 = 2;
            if (modePerUnit.Count > 0 && modePerUnit[0].Count > 0)
                mode0 = (int)Math.Round(modePerUnit[0][0]);

            string info = string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6}",
                id, axisNum, mode0, minT, maxT, gradLo, gradHi);

            for (int unitIdx = 0; unitIdx < breps.Count; unitIdx++)
            {
                Brep brep = breps[unitIdx];
                if (brep == null)
                    continue;

                if (brep.Faces.Count == 0)
                {
                    sOut.Add(brep);
                    geoId.Add(id);
                    continue;
                }

                BrepFace refFace = brep.Faces[0];
                Interval uDom = refFace.Domain(0);
                Interval vDom = refFace.Domain(1);

                List<double> uValues = unitIdx < uPerUnit.Count ? uPerUnit[unitIdx] : new List<double>();
                List<double> vValues = unitIdx < vPerUnit.Count ? vPerUnit[unitIdx] : new List<double>();
                List<double> mValues = unitIdx < modePerUnit.Count ? modePerUnit[unitIdx] : new List<double>();

                int meshMode = mValues.Count > 0 ? (int)Math.Round(mValues[0]) : 0;

                for (int i = 0; i < uValues.Count; i++)
                {
                    double uIn = uValues[i];
                    double vIn = i < vValues.Count ? vValues[i] : (vValues.Count > 0 ? vValues[vValues.Count - 1] : 0.0);
                    int currentMode = i < mValues.Count ? (int)Math.Round(mValues[i]) : (mValues.Count > 0 ? (int)Math.Round(mValues[0]) : 0);

                    double uVal = Math.Abs(uIn) <= 1.0 + 1e-9 ? uDom.Min + uIn * uDom.Length : uIn;
                    double vVal = Math.Abs(vIn) <= 1.0 + 1e-9 ? vDom.Min + vIn * vDom.Length : vIn;

                    Point3d pt;
                    try
                    {
                        pt = refFace.PointAt(uVal, vVal);
                    }
                    catch
                    {
                        continue;
                    }

                    double factor = GradientFactorOfPoint(pt, gradAxis, gradLo, gradHi);
                    double tVal = ThicknessFromFactor(currentMode, factor, minT, maxT);
                    Drawing.Color col = ColorFromThickness(tVal, minT, maxT);

                    thickness.Add(tVal);
                    colors.Add(col);
                    points3D.Add(pt);
                }

                sOut.Add(brep);
                geoId.Add(id);

                for (int fi = 0; fi < brep.Faces.Count; fi++)
                {
                    Surface us = brep.Faces[fi].UnderlyingSurface();
                    if (us != null)
                        surfacesOut.Add(us.Duplicate());
                }

                int nu = Math.Max(nContours * 2, 12);
                int nv = Math.Max(nContours, 4);
                var rbResult = RebuildFit(brep, nu, nv);
                for (int i = 0; i < rbResult.Surfaces.Count; i++)
                    rebuiltSrf.Add(rbResult.Surfaces[i]);
                for (int i = 0; i < rbResult.RasterPoints.Count; i++)
                {
                    Point3d p = rbResult.RasterPoints[i];
                    rasterPts.Add(p);
                    contoursAlias.Add(p);
                }

                for (int fi = 0; fi < brep.Faces.Count; fi++)
                {
                    Brep fb = brep.Faces[fi].DuplicateFace(false);
                    var edges = new List<Curve>();
                    for (int ei = 0; ei < fb.Edges.Count; ei++)
                        edges.Add(fb.Edges[ei].DuplicateCurve());

                    Curve[] joined = Curve.JoinCurves(edges, tol);
                    if (joined != null && joined.Length > 0)
                    {
                        for (int i = 0; i < joined.Length; i++)
                            edgeCurves.Add(joined[i]);
                    }
                    else
                    {
                        for (int i = 0; i < edges.Count; i++)
                            edgeCurves.Add(edges[i]);
                    }
                }

                if (rbResult.Surfaces.Count > 0)
                {
                    var srfBreps = new List<Brep>();
                    for (int i = 0; i < rbResult.Surfaces.Count; i++)
                    {
                        Surface s = rbResult.Surfaces[i];
                        Brep b = s?.ToBrep();
                        if (b != null)
                            srfBreps.Add(b);
                    }

                    if (srfBreps.Count > 0)
                    {
                        Brep[] joined = Brep.JoinBreps(srfBreps, tol);
                        if (joined != null && joined.Length > 0)
                        {
                            for (int i = 0; i < joined.Length; i++)
                                rebuiltJoined.Add(joined[i]);
                        }
                        else
                        {
                            for (int i = 0; i < srfBreps.Count; i++)
                                rebuiltJoined.Add(srfBreps[i]);
                        }
                    }
                }

                Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                if (meshes != null && meshes.Length > 0)
                {
                    var unitMesh = new Mesh();
                    for (int i = 0; i < meshes.Length; i++)
                        unitMesh.Append(meshes[i]);

                    for (int vi = 0; vi < unitMesh.Vertices.Count; vi++)
                    {
                        Point3d p = unitMesh.Vertices.Point3dAt(vi);
                        double factor = GradientFactorOfPoint(p, gradAxis, gradLo, gradHi);
                        double tVal = ThicknessFromFactor(meshMode, factor, minT, maxT);
                        Drawing.Color col = ColorFromThickness(tVal, minT, maxT);
                        unitMesh.VertexColors.Add(col);
                    }

                    unitMesh.Normals.ComputeNormals();
                    unitMesh.Compact();
                    heatmapMesh.Append(unitMesh);
                }
            }

            SetOutputs(access, info, geoId, thickness, colors, points3D, heatmapMesh, sOut, surfacesOut, rebuiltSrf, rebuiltJoined, rasterPts, edgeCurves, contoursAlias);

            var picture = new ColourPicture();
            picture.Mesh(heatmapMesh);
            ShowColours(picture);
        }

        private static void SetOutputs(
            IDataAccess access,
            string info,
            List<string> geoId,
            List<double> thickness,
            List<Drawing.Color> colors,
            List<Point3d> points3D,
            Mesh heatmapMesh,
            List<Brep> sOut,
            List<GeometryBase> surfacesOut,
            List<Surface> rebuiltSrf,
            List<Brep> rebuiltJoined,
            List<Point3d> rasterPts,
            List<Curve> edgeCurves,
            List<Point3d> contoursAlias)
        {
            access.SetItem(0, info ?? string.Empty);
            access.SetTwig(1, geoId.ToArray(), null, null);
            access.SetTwig(2, thickness.ToArray(), null, null);
            access.SetTwig(3, colors.ToArray(), null, null);
            access.SetTwig(4, points3D.ToArray(), null, null);
            access.SetItem(5, heatmapMesh);
            access.SetTwig(6, sOut.ToArray(), null, null);
            access.SetTwig(7, surfacesOut.ToArray(), null, null);
            access.SetTwig(8, rebuiltSrf.ToArray(), null, null);
            access.SetTwig(9, rebuiltJoined.ToArray(), null, null);
            access.SetTwig(10, rasterPts.ToArray(), null, null);
            access.SetTwig(11, edgeCurves.ToArray(), null, null);
            access.SetTwig(12, contoursAlias.ToArray(), null, null);
        }

        /// <summary>The values of every branch, as Grasshopper 1 read them (numbers, or texts that are numbers).</summary>
        private static List<List<double>> ToPerUnitValues(List<List<object>> tree, int nUnits)
        {
            int n = Math.Max(1, nUnits);
            var perUnit = new List<List<double>>(n);
            for (int i = 0; i < n; i++)
                perUnit.Add(new List<double>());

            if (tree == null || tree.Count == 0)
                return perUnit;

            var branches = new List<List<double>>();
            foreach (var branch in tree)
            {
                var vals = new List<double>();
                foreach (var o in branch)
                    if (DxrTools.TryDouble(o, out double v))
                        vals.Add(v);
                branches.Add(vals);
            }

            if (branches.Count == 1)
            {
                for (int i = 0; i < n; i++)
                    perUnit[i] = new List<double>(branches[0]);
                return perUnit;
            }

            for (int i = 0; i < branches.Count && i < perUnit.Count; i++)
                perUnit[i] = branches[i];

            return perUnit;
        }

        private static List<Brep> ExtractBreps(List<GeometryBase> input)
        {
            var result = new List<Brep>();
            if (input == null)
                return result;

            for (int i = 0; i < input.Count; i++)
            {
                GeometryBase obj = input[i];
                if (obj == null)
                    continue;

                switch (obj)
                {
                    case Brep b:
                        result.Add(b);
                        break;
                    case BrepFace bf:
                        result.Add(bf.DuplicateFace(false));
                        break;
                    case Surface s:
                        {
                            Brep sb = s.ToBrep();
                            if (sb != null)
                                result.Add(sb);
                            break;
                        }
                }
            }

            return result;
        }
        private static char SelectAxis(int axisInput, BoundingBox globalBox)
        {
            if (axisInput == 1) return 'x';
            if (axisInput == 2) return 'y';
            if (axisInput == 3) return 'z';

            double dx = globalBox.Max.X - globalBox.Min.X;
            double dy = globalBox.Max.Y - globalBox.Min.Y;
            double dz = globalBox.Max.Z - globalBox.Min.Z;

            if (dz >= dx && dz >= dy) return 'z';
            if (dy >= dx) return 'y';
            return 'x';
        }

        private static double GradientFactorOfPoint(Point3d pt, char axis, double lo, double hi)
        {
            if (Math.Abs(hi - lo) < 1e-12)
                return 0.0;

            double v = axis == 'x' ? pt.X : (axis == 'y' ? pt.Y : pt.Z);
            double f = (v - lo) / (hi - lo);
            if (f < 0.0) f = 0.0;
            if (f > 1.0) f = 1.0;
            return f;
        }

        private static double ThicknessFromFactor(int mode, double factor, double minT, double maxT)
        {
            if (mode == 0) return minT;
            if (mode == 1) return maxT;
            if (mode == 2) return minT + factor * (maxT - minT);
            return minT;
        }

        private static Drawing.Color ColorFromThickness(double t, double minT, double maxT)
        {
            double c = Math.Abs(maxT - minT) < 1e-12 ? 0.0 : (t - minT) / (maxT - minT);
            if (c < 0.0) c = 0.0;
            if (c > 1.0) c = 1.0;
            int r = (int)(255 * c);
            int b = (int)(255 * (1.0 - c));
            return Drawing.Color.FromArgb(255, r, 0, b);
        }

        private static RebuildResult RebuildFit(Brep brep, int nU, int nV)
        {
            var surfaces = new List<Surface>();
            var points = new List<Point3d>();

            for (int fi = 0; fi < brep.Faces.Count; fi++)
            {
                BrepFace face = brep.Faces[fi];
                List<List<Point3d>> grid = FaceGrid(face, nU, nV);
                if (grid == null || grid.Count < 2 || grid[0].Count < 2)
                    continue;

                int rows = grid.Count;
                int cols = grid[0].Count;
                var flat = new List<Point3d>(rows * cols);
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        Point3d pt = grid[r][c];
                        flat.Add(pt);
                        points.Add(pt);
                    }
                }

                NurbsSurface fit = NurbsSurface.CreateThroughPoints(flat, rows, cols, 3, 3, false, false);
                if (fit != null)
                    surfaces.Add(fit);
            }

            return new RebuildResult(surfaces, points);
        }

        private static List<List<Point3d>> FaceGrid(BrepFace face, int nLong, int nCross)
        {
            Surface srf = face.UnderlyingSurface();
            if (srf == null)
                return null;

            bool InFace(double u, double v)
            {
                PointFaceRelation rel = face.IsPointOnFace(u, v);
                return rel != PointFaceRelation.Exterior;
            }

            Interval uDom0 = srf.Domain(0);
            Interval vDom0 = srf.Domain(1);

            double extU = srf.PointAt(uDom0.Min, vDom0.Mid).DistanceTo(srf.PointAt(uDom0.Max, vDom0.Mid));
            double extV = srf.PointAt(uDom0.Mid, vDom0.Min).DistanceTo(srf.PointAt(uDom0.Mid, vDom0.Max));
            bool swapUV = extV > extU;

            Interval longDom = swapUV ? srf.Domain(1) : srf.Domain(0);
            Interval crossDom = swapUV ? srf.Domain(0) : srf.Domain(1);

            Point3d Eval(double sLong, double sCross)
            {
                if (swapUV)
                {
                    return srf.PointAt(
                        crossDom.Min + sCross * crossDom.Length,
                        longDom.Min + sLong * longDom.Length);
                }

                return srf.PointAt(
                    longDom.Min + sLong * longDom.Length,
                    crossDom.Min + sCross * crossDom.Length);
            }

            bool InFaceLs(double sLong, double sCross)
            {
                if (swapUV)
                {
                    return InFace(
                        crossDom.Min + sCross * crossDom.Length,
                        longDom.Min + sLong * longDom.Length);
                }

                return InFace(
                    longDom.Min + sLong * longDom.Length,
                    crossDom.Min + sCross * crossDom.Length);
            }

            (double a, double b)? CrossRangeAt(double sLong)
            {
                const int samples = 80;
                double min = double.MaxValue;
                double max = double.MinValue;
                bool found = false;

                for (int j = 0; j <= samples; j++)
                {
                    double sc = j / (double)samples;
                    if (!InFaceLs(sLong, sc))
                        continue;

                    if (sc < min) min = sc;
                    if (sc > max) max = sc;
                    found = true;
                }

                if (!found) return null;
                return (min, max);
            }

            List<double> LongParamsWithExtrema(int n)
            {
                int isoDir = swapUV ? 1 : 0;
                double[] crossPositions = { crossDom.Min, crossDom.Mid, crossDom.Max };
                const int M = 60;
                var extrema = new List<double>();

                for (int ci = 0; ci < crossPositions.Length; ci++)
                {
                    Curve iso;
                    try
                    {
                        iso = srf.IsoCurve(isoDir, crossPositions[ci]);
                    }
                    catch
                    {
                        continue;
                    }

                    if (iso == null)
                        continue;

                    Interval id = iso.Domain;
                    var tangs = new Vector3d[M + 1];
                    for (int j = 0; j <= M; j++)
                    {
                        double t = id.Min + (j / (double)M) * id.Length;
                        tangs[j] = iso.TangentAt(t);
                    }

                    var ang = new double[M + 1];
                    for (int j = 1; j < M; j++)
                    {
                        Vector3d a = tangs[j - 1];
                        Vector3d b = tangs[j + 1];
                        if (a.Length > 0.0 && b.Length > 0.0)
                        {
                            a.Unitize();
                            b.Unitize();
                            double dot = Math.Max(-1.0, Math.Min(1.0, a * b));
                            ang[j] = Math.Acos(dot);
                        }
                    }

                    for (int j = 1; j < M; j++)
                    {
                        double left = ang[j - 1];
                        double right = j + 1 <= M ? ang[j + 1] : 0.0;
                        if (ang[j] >= left && ang[j] >= right && ang[j] > 1e-4)
                            extrema.Add(j / (double)M);
                    }
                }

                extrema.Sort();
                var merged = new List<double>();
                for (int i = 0; i < extrema.Count; i++)
                {
                    double e = extrema[i];
                    if (merged.Count == 0 || Math.Abs(e - merged[merged.Count - 1]) > 0.03)
                        merged.Add(e);
                }

                var paramSet = new SortedSet<double>();
                int baseN = Math.Max(n, 2);
                for (int i = 0; i < baseN; i++)
                    paramSet.Add(i / (double)(baseN - 1));

                const double d = 0.02;
                var forced = new List<double>(merged) { 0.0, 1.0 };
                for (int i = 0; i < forced.Count; i++)
                {
                    double e = forced[i];
                    paramSet.Add(e);
                    paramSet.Add(Math.Max(0.0, e - d));
                    paramSet.Add(Math.Min(1.0, e + d));
                }

                return new List<double>(paramSet);
            }

            List<double> longParams = LongParamsWithExtrema(nLong);

            var grid = new List<List<Point3d>>();
            for (int i = 0; i < longParams.Count; i++)
            {
                double sLong = longParams[i];
                var range = CrossRangeAt(sLong);
                if (!range.HasValue)
                    continue;

                double c0 = range.Value.a;
                double c1 = range.Value.b;
                var row = new List<Point3d>(Math.Max(1, nCross));
                for (int iv = 0; iv < nCross; iv++)
                {
                    double sc = nCross > 1 ? iv / (double)(nCross - 1) : 0.0;
                    row.Add(Eval(sLong, c0 + sc * (c1 - c0)));
                }

                grid.Add(row);
            }

            return grid;
        }

        private readonly struct RebuildResult
        {
            public readonly List<Surface> Surfaces;
            public readonly List<Point3d> RasterPoints;

            public RebuildResult(List<Surface> surfaces, List<Point3d> rasterPoints)
            {
                Surfaces = surfaces;
                RasterPoints = rasterPoints;
            }
        }
    }
}
