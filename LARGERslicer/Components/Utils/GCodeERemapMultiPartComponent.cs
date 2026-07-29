using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    public class GCodeERemapMultiPartComponent : GH_Component
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Guid, RemapCache> CacheByComponent = new Dictionary<Guid, RemapCache>();

        public GCodeERemapMultiPartComponent()
          : base("GCode E Remap MultiPart", "ERemap+",
              "Recalculates XE values for one shared G-code using multiple part geometries and per-part thickness modes/gradients.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("GCode In", "Code", "Full G-code as text or list of lines.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Original Width", "Wold", "Original constant line width used by source code.", GH_ParamAccess.item, 4.0);
            pManager.AddGeometryParameter("Part Geometry", "PartGeo", "Original part geometries (merged list).", GH_ParamAccess.list);
            pManager.AddTextParameter("Geometry ID", "GeoID", "IDs aligned with Part Geometry.", GH_ParamAccess.list);
            pManager.AddTextParameter("Part Info", "PartInfo", "Info rows: ID;axis;mode;Min_T;Max_T;Lo;Hi (old format without mode also supported).", GH_ParamAccess.list);
            pManager.AddGenericParameter("Offset", "V", "Slice translation (vector or point).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Outline Dist", "OD", "Distance threshold for outline detection. 0 disables outline logic.", GH_ParamAccess.item, 0.0);
            pManager.AddIntegerParameter("Decimals", "Dec", "Decimal places for new XE values.", GH_ParamAccess.item, 6);
            pManager.AddTransformParameter("Slice XForm", "XF", "Optional forward slice transform. It is inverted internally for mapping.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Debug", "Dbg", "When true, fills detailed debug outputs (can be heavy).", GH_ParamAccess.item, false);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode Out", "Out", "Remapped G-code string.", GH_ParamAccess.item);
            pManager.AddTextParameter("GCode Out Text", "OutTxt", "Same as GCode Out (compat output).", GH_ParamAccess.item);
            pManager.AddNumberParameter("E New", "Enew", "New XE values (debug mode).", GH_ParamAccess.list);
            pManager.AddNumberParameter("E Old", "Eold", "Original XE values (debug mode).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Used Thickness", "D", "Applied local widths (debug mode).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Part Index", "Pi", "Assigned part index per movement line (debug mode).", GH_ParamAccess.list);
            pManager.AddPointParameter("Mapped Points", "Pts", "Transformed points in source-geometry space (debug mode).", GH_ParamAccess.list);
            pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var rawCode = new List<IGH_Goo>();
            double breiteAlt = 4.0;
            var teilGeo = new List<GeometryBase>();
            var geoIds = new List<string>();
            var teilInfo = new List<string>();
            object versatzObj = null;
            double outlineDist = 0.0;
            int dezimal = 6;
            Transform sliceXForm = Transform.Unset;
            bool debug = false;

            DA.GetDataList(0, rawCode);
            DA.GetData(1, ref breiteAlt);
            DA.GetDataList(2, teilGeo);
            DA.GetDataList(3, geoIds);
            DA.GetDataList(4, teilInfo);
            DA.GetData(5, ref versatzObj);
            DA.GetData(6, ref outlineDist);
            DA.GetData(7, ref dezimal);
            bool hasSliceXForm = DA.GetData(8, ref sliceXForm);
            DA.GetData(9, ref debug);

            string text = NormalizeTextInput(rawCode);
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

            ParseSig textSig = CheapSig(text);

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

            RemapCache cache = GetOrCreateCache(InstanceGuid);

            if (!cache.ParseSig.Equals(textSig))
            {
                ParseText(text, cache.Parsed);
                cache.ParseSig = textSig;
                cache.MapSig = string.Empty;
                cache.Mapped.Clear();
                cache.SampleInfo = new SampleInfo(0, 0, 0);
            }

            string mapSig = BuildMapSig(textSig, xform, teilBboxes);
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

            DA.SetData(0, gcodeOutText);
            DA.SetData(1, gcodeOutText);
            DA.SetDataList(2, eNeu);
            DA.SetDataList(3, eAlt);
            DA.SetDataList(4, dickeUsed);
            DA.SetDataList(5, teilIdx);
            DA.SetDataList(6, punkteT);
            DA.SetData(7, report);
        }

        public override void RemovedFromDocument(Grasshopper.Kernel.GH_Document document)
        {
            base.RemovedFromDocument(document);
            lock (CacheLock)
            {
                CacheByComponent.Remove(InstanceGuid);
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

        private static string NormalizeTextInput(List<IGH_Goo> raw)
        {
            if (raw == null || raw.Count == 0)
                return string.Empty;

            if (raw.Count == 1)
            {
                string s = GooToString(raw[0]);
                if (!string.IsNullOrEmpty(s) && s.Contains("\n"))
                    return s;
            }

            var parts = new List<string>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                string s = GooToString(raw[i]);
                if (s != null)
                    parts.Add(s);
            }

            return string.Join("\n", parts);
        }

        private static string GooToString(IGH_Goo goo)
        {
            if (goo == null)
                return null;

            object val = goo.ScriptVariable();
            if (val == null)
                return null;

            return val as string ?? val.ToString();
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

            if (versatzObj is IGH_Goo goo)
                versatzObj = goo.ScriptVariable();

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
                case GH_Vector gvh:
                    vx = gvh.Value.X;
                    vy = gvh.Value.Y;
                    vz = gvh.Value.Z;
                    return;
                case GH_Point gph:
                    vx = gph.Value.X;
                    vy = gph.Value.Y;
                    vz = gph.Value.Z;
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

        private static string BuildMapSig(ParseSig textSig, Transform xf, List<BoundingBox> bbs)
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

            return sb.ToString();
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

        protected override Drawing.Bitmap Icon => IconHelper.Load("GCodeERemapMultiPartIcon.png");
        public override Guid ComponentGuid => new Guid("15A59A1B-9CB5-4965-9E10-B407082A57EC");

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
}
