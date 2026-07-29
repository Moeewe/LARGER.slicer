using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    public class FastGCodeDxrPreviewComponent : GH_Component
    {
        private const int ChunkSize = 4000;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Guid, PreviewCache> Caches = new Dictionary<Guid, PreviewCache>();
        private static readonly Dictionary<Guid, GCodeLineConduit> Conduits = new Dictionary<Guid, GCodeLineConduit>();

        public FastGCodeDxrPreviewComponent()
          : base("Fast GCode DXR Preview", "GPreview",
              "Fast G-code/DXR path visualization with cached parsing and optional 3D ribbon preview.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("GCode In", "Code", "G-code/DXR text as string or list of lines.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Width", "Wmin", "Minimum preview width.", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Max Width", "Wmax", "Maximum preview width.", GH_ParamAccess.item, 8.0);
            pManager.AddIntegerParameter("Color Steps", "Steps", "Number of color buckets.", GH_ParamAccess.item, 16);
            pManager.AddNumberParameter("Progress", "Prog", "Visible path fraction from 0..1.", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Active", "On", "Enable preview generation.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Preview 3D", "3D", "If true, outputs ribbon meshes; if false, uses line conduit mode.", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Min Span", "Span", "Minimum relative color span (default 0.25).", GH_ParamAccess.item, 0.25);

            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Preview Geo", "Prev", "Preview geometry (meshes in 3D mode, lines in conduit mode).", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Preview status summary.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var raw = new List<IGH_Goo>();
            double breiteMin = 1.0;
            double breiteMax = 8.0;
            int farbstufen = 16;
            double fortschritt = 1.0;
            bool aktiv = true;
            bool vorschau3D = true;
            double spanMin = 0.25;

            DA.GetDataList(0, raw);
            DA.GetData(1, ref breiteMin);
            DA.GetData(2, ref breiteMax);
            DA.GetData(3, ref farbstufen);
            DA.GetData(4, ref fortschritt);
            DA.GetData(5, ref aktiv);
            DA.GetData(6, ref vorschau3D);
            DA.GetData(7, ref spanMin);

            breiteMin = SafePositive(breiteMin, 1.0);
            breiteMax = SafePositive(breiteMax, 8.0);
            if (breiteMax < breiteMin)
            {
                double t = breiteMin;
                breiteMin = breiteMax;
                breiteMax = t;
            }

            if (farbstufen < 2)
                farbstufen = 2;

            if (fortschritt < 0.0) fortschritt = 0.0;
            if (fortschritt > 1.0) fortschritt = 1.0;
            if (spanMin < 0.0) spanMin = 0.0;

            string text = NormalizeTextInput(raw);
            ParseSig parseSig = CheapSig(text);

            PreviewCache cache = GetOrCreateCache();

            string parseStatus;
            if (cache.ParseSig.Equals(parseSig))
            {
                parseStatus = "Hit";
            }
            else
            {
                ParseRaw(text, cache.Segments, cache.Epm);
                cache.ParseSig = parseSig;
                cache.ClearDisplayCache();
                parseStatus = "New";
            }

            string dispStatus;
            DisplaySig dispSig = new DisplaySig(parseSig, breiteMin, breiteMax, farbstufen, spanMin);
            if (cache.DisplaySig.Equals(dispSig))
            {
                dispStatus = "Hit";
            }
            else
            {
                cache.RebuildDisplayCache(breiteMin, breiteMax, farbstufen, spanMin);
                cache.DisplaySig = dispSig;
                dispStatus = "New";
            }

            int totalLines = cache.Segments.Count;
            int limitIdx = (int)(fortschritt * totalLines) - 1;

            cache.DisposePartialMesh();

            var activeDrawGroups = new List<DrawGroup>();
            var activeMeshes = new List<Mesh>();

            if (aktiv && totalLines > 0 && limitIdx >= 0)
            {
                if (vorschau3D)
                {
                    int fullChunks = (limitIdx + 1) / ChunkSize;
                    int rem = (limitIdx + 1) % ChunkSize;

                    for (int i = 0; i < fullChunks && i < cache.MeshChunks.Count; i++)
                        activeMeshes.Add(cache.MeshChunks[i]);

                    if (rem > 0 && fullChunks < cache.MeshChunks.Count)
                    {
                        int startIdx = fullChunks * ChunkSize;
                        int endIdx = limitIdx + 1;
                        Mesh partial = cache.BuildPartialMesh(startIdx, endIdx, breiteMin, breiteMax, spanMin);
                        if (partial != null && partial.Faces.Count > 0)
                        {
                            activeMeshes.Add(partial);
                            cache.PartialMesh = partial;
                        }
                    }
                }
                else
                {
                    for (int k = 0; k < cache.BucketIds.Count; k++)
                    {
                        List<int> ids = cache.BucketIds[k];
                        if (ids == null || ids.Count == 0)
                            continue;

                        int count = UpperBound(ids, limitIdx);
                        if (count <= 0)
                            continue;

                        List<Line> lines = cache.BucketLines[k];
                        Line[] draw;
                        if (count == lines.Count)
                        {
                            draw = lines.ToArray();
                        }
                        else
                        {
                            draw = lines.GetRange(0, count).ToArray();
                        }

                        activeDrawGroups.Add(new DrawGroup(draw, cache.BucketColors[k], cache.BucketWidths[k]));
                    }
                }
            }

            DisableConduit(InstanceGuid);
            if (aktiv && !vorschau3D && activeDrawGroups.Count > 0)
            {
                var conduit = new GCodeLineConduit(this, activeDrawGroups, cache.BBox);
                conduit.Enabled = true;
                lock (CacheLock)
                {
                    Conduits[InstanceGuid] = conduit;
                }
            }

            try
            {
                RhinoDoc.ActiveDoc?.Views.Redraw();
            }
            catch
            {
                // Ignore redraw issues.
            }

            var previewGeo = new List<object>();
            if (vorschau3D)
            {
                for (int i = 0; i < activeMeshes.Count; i++)
                    previewGeo.Add(activeMeshes[i]);
            }
            else
            {
                for (int i = 0; i < activeDrawGroups.Count; i++)
                {
                    Line[] arr = activeDrawGroups[i].Lines;
                    for (int j = 0; j < arr.Length; j++)
                        previewGeo.Add(arr[j]);
                }
            }

            string info = $"Segments: {totalLines} | Visible: {Math.Max(0, limitIdx + 1)} | Preview: {(vorschau3D ? "3D Ribbons (GH Preview)" : "Lines (Conduit)")} | Parse: {parseStatus} | Display: {dispStatus}";

            DA.SetDataList(0, previewGeo);
            DA.SetData(1, info);
        }

        public override void RemovedFromDocument(Grasshopper.Kernel.GH_Document document)
        {
            base.RemovedFromDocument(document);
            DisableConduit(InstanceGuid);
            lock (CacheLock)
            {
                if (Caches.TryGetValue(InstanceGuid, out PreviewCache cache))
                {
                    cache.DisposeAllMeshes();
                    Caches.Remove(InstanceGuid);
                }
            }
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            if (Locked)
            {
                DisableConduit(InstanceGuid);
            }
        }

        private PreviewCache GetOrCreateCache()
        {
            lock (CacheLock)
            {
                if (!Caches.TryGetValue(InstanceGuid, out PreviewCache cache))
                {
                    cache = new PreviewCache();
                    Caches[InstanceGuid] = cache;
                }

                return cache;
            }
        }

        private static void DisableConduit(Guid key)
        {
            lock (CacheLock)
            {
                if (!Conduits.TryGetValue(key, out GCodeLineConduit oldConduit))
                    return;

                try
                {
                    oldConduit.Enabled = false;
                }
                catch
                {
                    // Ignore conduit shutdown errors.
                }

                Conduits.Remove(key);
            }
        }

        private static string NormalizeTextInput(List<IGH_Goo> raw)
        {
            if (raw == null || raw.Count == 0)
                return string.Empty;

            if (raw.Count == 1)
            {
                string single = GooToString(raw[0]);
                if (!string.IsNullOrEmpty(single) && single.Contains("\n"))
                    return single;
            }

            var lines = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string s = GooToString(raw[i]);
                if (s != null)
                    lines.Add(s);
            }

            return string.Join("\n", lines);
        }

        private static string GooToString(IGH_Goo goo)
        {
            if (goo == null)
                return null;

            object value = goo.ScriptVariable();
            if (value == null)
                return null;

            return value as string ?? value.ToString();
        }

        private static double SafePositive(double value, double fallback)
        {
            return value <= 0.0 ? fallback : value;
        }

        private static ParseSig CheapSig(string s)
        {
            if (string.IsNullOrEmpty(s))
                return new ParseSig(0, 0, 0, 0);

            int n = s.Length;
            string a = s.Substring(0, Math.Min(64, n));
            int mid = n / 2;
            string b = s.Substring(mid, Math.Min(64, n - mid));
            string c = s.Substring(Math.Max(0, n - 64), Math.Min(64, n));
            return new ParseSig(n, a.GetHashCode(), b.GetHashCode(), c.GetHashCode());
        }

        private static void ParseRaw(string txt, List<Segment> coords, List<double> epm)
        {
            coords.Clear();
            epm.Clear();

            if (string.IsNullOrEmpty(txt))
                return;

            double px = 0.0;
            double py = 0.0;
            double pz = 0.0;
            bool havePrev = false;

            string[] lines = txt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (string.IsNullOrEmpty(ln))
                    continue;

                if (ln.IndexOf("XE=[", StringComparison.Ordinal) < 0 || ln.IndexOf("G1", StringComparison.Ordinal) < 0)
                    continue;

                if (!TryParseXe(ln, out double e))
                    continue;

                if (!TryParseXYZ(ln, out double x, out double y, out double z))
                    continue;

                if (havePrev && e > 0.0)
                {
                    double dx = x - px;
                    double dy = y - py;
                    double dz = z - pz;
                    double sl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (sl > 1e-6)
                    {
                        coords.Add(new Segment(px, py, pz, x, y, z));
                        epm.Add(e / sl);
                    }
                }

                px = x;
                py = y;
                pz = z;
                havePrev = true;
            }
        }

        private static bool TryParseXe(string line, out double e)
        {
            e = 0.0;
            int i = line.IndexOf("XE=[", StringComparison.Ordinal);
            if (i < 0)
                return false;

            i += 4;
            int j = i;
            while (j < line.Length)
            {
                char ch = line[j];
                if (char.IsDigit(ch) || ch == '.' || ch == '-')
                {
                    j++;
                    continue;
                }

                break;
            }

            if (j <= i)
                return false;

            string sub = line.Substring(i, j - i);
            return double.TryParse(sub, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e);
        }

        private static bool TryParseXYZ(string line, out double x, out double y, out double z)
        {
            x = y = z = 0.0;
            bool hx = false;
            bool hy = false;
            bool hz = false;

            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok.Length < 2)
                    continue;

                char c = tok[0];
                string val = tok.Substring(1);
                if (!double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                    continue;

                if (c == 'X') { x = d; hx = true; }
                else if (c == 'Y') { y = d; hy = true; }
                else if (c == 'Z') { z = d; hz = true; }
            }

            return hx && hy && hz;
        }

        private static int UpperBound(List<int> ids, int value)
        {
            int lo = 0;
            int hi = ids.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (ids[mid] <= value)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        protected override Drawing.Bitmap Icon => IconHelper.Load("FastGCodeDxrPreviewIcon.png");
        public override Guid ComponentGuid => new Guid("000961C9-CE01-4FEC-9FF7-B8E2BE841FDC");

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

        private readonly struct DisplaySig : IEquatable<DisplaySig>
        {
            public readonly ParseSig Parse;
            public readonly double BMin;
            public readonly double BMax;
            public readonly int Buckets;
            public readonly double MinRel;

            public DisplaySig(ParseSig parse, double bMin, double bMax, int buckets, double minRel)
            {
                Parse = parse;
                BMin = bMin;
                BMax = bMax;
                Buckets = buckets;
                MinRel = minRel;
            }

            public bool Equals(DisplaySig other)
            {
                return Parse.Equals(other.Parse)
                       && BMin.Equals(other.BMin)
                       && BMax.Equals(other.BMax)
                       && Buckets == other.Buckets
                       && MinRel.Equals(other.MinRel);
            }

            public override bool Equals(object obj)
            {
                return obj is DisplaySig other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Parse.GetHashCode();
                    h = (h * 397) ^ BMin.GetHashCode();
                    h = (h * 397) ^ BMax.GetHashCode();
                    h = (h * 397) ^ Buckets;
                    h = (h * 397) ^ MinRel.GetHashCode();
                    return h;
                }
            }
        }

        private readonly struct Segment
        {
            public readonly double X0;
            public readonly double Y0;
            public readonly double Z0;
            public readonly double X1;
            public readonly double Y1;
            public readonly double Z1;

            public Segment(double x0, double y0, double z0, double x1, double y1, double z1)
            {
                X0 = x0;
                Y0 = y0;
                Z0 = z0;
                X1 = x1;
                Y1 = y1;
                Z1 = z1;
            }
        }

        private sealed class DrawGroup
        {
            public readonly Line[] Lines;
            public readonly Drawing.Color Color;
            public readonly int Width;

            public DrawGroup(Line[] lines, Drawing.Color color, int width)
            {
                Lines = lines;
                Color = color;
                Width = width;
            }
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
            public Mesh PartialMesh;

            public void ClearDisplayCache()
            {
                DisposeAllMeshes();
                BucketIds.Clear();
                BucketLines.Clear();
                BucketColors.Clear();
                BucketWidths.Clear();
                BBox = BoundingBox.Empty;
            }

            public void DisposePartialMesh()
            {
                if (PartialMesh == null)
                    return;

                try
                {
                    PartialMesh.Dispose();
                }
                catch
                {
                    // Ignore dispose errors.
                }

                PartialMesh = null;
            }

            public void DisposeAllMeshes()
            {
                DisposePartialMesh();
                for (int i = 0; i < MeshChunks.Count; i++)
                {
                    try
                    {
                        MeshChunks[i]?.Dispose();
                    }
                    catch
                    {
                        // Ignore dispose errors.
                    }
                }

                MeshChunks.Clear();
            }

            public void RebuildDisplayCache(double bMin, double bMax, int nBuck, double minRel)
            {
                DisposeAllMeshes();
                BucketIds.Clear();
                BucketLines.Clear();
                BucketColors.Clear();
                BucketWidths.Clear();

                for (int k = 0; k < nBuck; k++)
                {
                    BucketIds.Add(new List<int>());
                    BucketLines.Add(new List<Line>());

                    double f = nBuck > 1 ? k / (double)(nBuck - 1) : 0.0;
                    BucketColors.Add(Drawing.Color.FromArgb(255, (int)(255 * f), 0, (int)(255 * (1.0 - f))));
                    BucketWidths.Add(Math.Max(1, (int)Math.Round(bMin + f * (bMax - bMin))));
                }

                double eLo = Epm.Count > 0 ? Min(Epm) : 0.0;
                double eHi = Epm.Count > 0 ? Max(Epm) : 1.0;
                double eSpan = NormSpan(eLo, eHi, minRel);
                double invSpan = eSpan > 1e-9 ? 1.0 / eSpan : 1.0;

                double minx = double.MaxValue;
                double miny = double.MaxValue;
                double minz = double.MaxValue;
                double maxx = double.MinValue;
                double maxy = double.MinValue;
                double maxz = double.MinValue;

                Mesh currentMesh = new Mesh();
                int vIdx = 0;

                for (int idx = 0; idx < Segments.Count; idx++)
                {
                    Segment s = Segments[idx];
                    if (s.X0 < minx) minx = s.X0;
                    if (s.X1 < minx) minx = s.X1;
                    if (s.X0 > maxx) maxx = s.X0;
                    if (s.X1 > maxx) maxx = s.X1;

                    if (s.Y0 < miny) miny = s.Y0;
                    if (s.Y1 < miny) miny = s.Y1;
                    if (s.Y0 > maxy) maxy = s.Y0;
                    if (s.Y1 > maxy) maxy = s.Y1;

                    if (s.Z0 < minz) minz = s.Z0;
                    if (s.Z1 < minz) minz = s.Z1;
                    if (s.Z0 > maxz) maxz = s.Z0;
                    if (s.Z1 > maxz) maxz = s.Z1;

                    double normF = (Epm[idx] - eLo) * invSpan;
                    double wReal = bMin + normF * (bMax - bMin);

                    int k = (int)(normF * (nBuck - 1) + 0.5);
                    if (k < 0) k = 0;
                    else if (k >= nBuck) k = nBuck - 1;

                    BucketIds[k].Add(idx);
                    BucketLines[k].Add(new Line(new Point3d(s.X0, s.Y0, s.Z0), new Point3d(s.X1, s.Y1, s.Z1)));

                    double dx = s.X1 - s.X0;
                    double dy = s.Y1 - s.Y0;
                    double l2 = dx * dx + dy * dy;
                    double nx;
                    double ny;
                    if (l2 > 1e-12)
                    {
                        double l = Math.Sqrt(l2);
                        nx = -dy / l * (wReal * 0.5);
                        ny = dx / l * (wReal * 0.5);
                    }
                    else
                    {
                        nx = wReal * 0.5;
                        ny = 0.0;
                    }

                    Drawing.Color col = BucketColors[k];
                    currentMesh.Vertices.Add((float)(s.X0 + nx), (float)(s.Y0 + ny), (float)s.Z0);
                    currentMesh.Vertices.Add((float)(s.X0 - nx), (float)(s.Y0 - ny), (float)s.Z0);
                    currentMesh.Vertices.Add((float)(s.X1 - nx), (float)(s.Y1 - ny), (float)s.Z1);
                    currentMesh.Vertices.Add((float)(s.X1 + nx), (float)(s.Y1 + ny), (float)s.Z1);

                    currentMesh.VertexColors.Add(col);
                    currentMesh.VertexColors.Add(col);
                    currentMesh.VertexColors.Add(col);
                    currentMesh.VertexColors.Add(col);
                    currentMesh.Faces.AddFace(vIdx, vIdx + 1, vIdx + 2, vIdx + 3);
                    vIdx += 4;

                    if (currentMesh.Faces.Count >= ChunkSize)
                    {
                        MeshChunks.Add(currentMesh);
                        currentMesh = new Mesh();
                        vIdx = 0;
                    }
                }

                if (currentMesh.Faces.Count > 0)
                {
                    MeshChunks.Add(currentMesh);
                }
                else
                {
                    currentMesh.Dispose();
                }

                BBox = Segments.Count > 0
                    ? new BoundingBox(new Point3d(minx, miny, minz), new Point3d(maxx, maxy, maxz))
                    : BoundingBox.Empty;
            }

            public Mesh BuildPartialMesh(int startIdx, int endIdx, double bMin, double bMax, double minRel)
            {
                if (startIdx >= endIdx || startIdx < 0 || endIdx > Segments.Count)
                    return null;

                double eLo = Epm.Count > 0 ? Min(Epm) : 0.0;
                double eHi = Epm.Count > 0 ? Max(Epm) : 1.0;
                double eSpan = NormSpan(eLo, eHi, minRel);

                double invSpan = 1.0 / eSpan;
                int nBuck = BucketColors.Count > 0 ? BucketColors.Count : 16;

                Mesh partial = new Mesh();
                int vIdx = 0;
                for (int idx = startIdx; idx < endIdx; idx++)
                {
                    Segment s = Segments[idx];
                    double normF = (Epm[idx] - eLo) * invSpan;
                    double wReal = bMin + normF * (bMax - bMin);

                    double dx = s.X1 - s.X0;
                    double dy = s.Y1 - s.Y0;
                    double l2 = dx * dx + dy * dy;
                    double nx;
                    double ny;
                    if (l2 > 1e-12)
                    {
                        double l = Math.Sqrt(l2);
                        nx = -dy / l * (wReal * 0.5);
                        ny = dx / l * (wReal * 0.5);
                    }
                    else
                    {
                        nx = wReal * 0.5;
                        ny = 0.0;
                    }

                    int k = (int)(normF * (nBuck - 1) + 0.5);
                    if (k < 0) k = 0;
                    else if (k >= nBuck) k = nBuck - 1;

                    Drawing.Color col = BucketColors[k];
                    partial.Vertices.Add((float)(s.X0 + nx), (float)(s.Y0 + ny), (float)s.Z0);
                    partial.Vertices.Add((float)(s.X0 - nx), (float)(s.Y0 - ny), (float)s.Z0);
                    partial.Vertices.Add((float)(s.X1 - nx), (float)(s.Y1 - ny), (float)s.Z1);
                    partial.Vertices.Add((float)(s.X1 + nx), (float)(s.Y1 + ny), (float)s.Z1);

                    partial.VertexColors.Add(col);
                    partial.VertexColors.Add(col);
                    partial.VertexColors.Add(col);
                    partial.VertexColors.Add(col);
                    partial.Faces.AddFace(vIdx, vIdx + 1, vIdx + 2, vIdx + 3);
                    vIdx += 4;
                }

                return partial;
            }

            private static double NormSpan(double lo, double hi, double minRel)
            {
                double span = hi - lo;
                double floor = lo > 0.0 ? lo * minRel : 1.0;
                if (span < floor)
                    span = floor;

                return span > 1e-9 ? span : 1.0;
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

            private static double Max(List<double> values)
            {
                double max = double.MinValue;
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] > max)
                        max = values[i];
                }

                return max;
            }
        }

        private sealed class GCodeLineConduit : DisplayConduit
        {
            private readonly WeakReference<FastGCodeDxrPreviewComponent> _owner;
            private readonly List<DrawGroup> _drawGroups;
            private readonly BoundingBox _box;
            private int _frame;

            public GCodeLineConduit(FastGCodeDxrPreviewComponent owner, List<DrawGroup> drawGroups, BoundingBox box)
            {
                _owner = new WeakReference<FastGCodeDxrPreviewComponent>(owner);
                _drawGroups = drawGroups ?? new List<DrawGroup>();
                _box = box;
                _frame = 0;
            }

            protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
            {
                if (_box.IsValid)
                    e.IncludeBoundingBox(_box);
            }

            protected override void DrawForeground(DrawEventArgs e)
            {
                _frame++;
                if (_frame % 30 == 1)
                {
                    bool alive = false;
                    if (_owner.TryGetTarget(out FastGCodeDxrPreviewComponent comp) && comp != null)
                    {
                        var doc = comp.OnPingDocument();
                        alive = doc != null && !comp.Locked;
                    }

                    if (!alive)
                    {
                        Enabled = false;
                        _drawGroups.Clear();
                        return;
                    }
                }

                try
                {
                    for (int i = 0; i < _drawGroups.Count; i++)
                    {
                        DrawGroup g = _drawGroups[i];
                        e.Display.DrawLines(g.Lines, g.Color, g.Width);
                    }
                }
                catch
                {
                    // Ignore display errors to avoid breaking redraw loop.
                }
            }
        }
    }
}
