using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Self-supporting prints, worked out on the geometry.
    ///
    /// A bead is held by the bead below when it lies at most maxOffset beside it
    /// (maxOffset = factor x bead width, 0.5 by default: the same rule as the Support test of
    /// Path Check). Per layer of height h a wall may therefore lean out by maxOffset, which is an
    /// overhang angle of atan(maxOffset / h) from the build direction: 5 mm bead, 2 mm layer = 51 deg.
    ///
    ///  - Check: which faces of the part hang over more than that for a build direction
    ///    (flat, angled, guide curve or guide surface).
    ///  - Envelope: the smallest self-supporting version of the layers, grown from the top layer
    ///    down (each layer must hold the layer above, shrunk by maxOffset), and back to geometry
    ///    (a lofted NURBS Brep where every layer is one loop, a mesh otherwise).
    ///  - BestDirection: the slice angle and tilt direction with the least unsupported area.
    /// </summary>
    public static class SelfSupport
    {
        public const double DefaultFactor = 0.5;
        /// <summary>Faces this many degrees under the limit are shown as near the limit.</summary>
        public const double NearBand = 10.0;

        public static double MaxOffset(PrintSettings s, double factor) => (factor > 0 ? factor : DefaultFactor) * s.BeadWidth;

        /// <summary>Largest overhang from the build direction in degrees: atan(maxOffset / layer height).</summary>
        public static double LimitAngle(double maxOffset, double layerHeight) =>
            layerHeight > 0 ? Math.Atan(maxOffset / layerHeight) * 180.0 / Math.PI : 90.0;

        private static string F(double v, string fmt = "0.#") => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

        // ================================================================== geometry check

        public class CheckResult
        {
            /// <summary>Faces coloured green (ok), orange (near the limit), red (unsupported). Grey = on the bed.</summary>
            public Mesh Analysis;
            /// <summary>The unsupported faces, one mesh per connected region.</summary>
            public List<Mesh> Regions = new List<Mesh>();
            /// <summary>Lowest points of islands that start in mid air (nothing below them along the build direction).</summary>
            public List<Point3d> Starts = new List<Point3d>();
            public double UnsupportedArea, NearArea, TotalArea, Percent, LimitDeg, MaxOffset, WorstOverhang, MeanOverhang, Height;
            public List<string> Report = new List<string>();
            public bool Ok => UnsupportedArea <= 0 && Starts.Count == 0;
        }

        /// <summary>Precomputed faces of a mesh, shared by Check and BestDirection.</summary>
        private sealed class FaceSet
        {
            public Point3d[] V;
            public int[] A, B, C;
            public Vector3d[] N;      // unit outward normal
            public double[] Area;
            public bool[] Bed;        // lies on the bed (lowest Z)
            public int[][] Nb;        // vertex neighbours
            public double BedZ, Total;

            public FaceSet(Mesh mesh, double bedTol)
            {
                var m = mesh.DuplicateMesh();
                m.Faces.ConvertQuadsToTriangles();
                V = m.Vertices.ToPoint3dArray();
                int fc = m.Faces.Count;
                A = new int[fc]; B = new int[fc]; C = new int[fc]; N = new Vector3d[fc]; Area = new double[fc]; Bed = new bool[fc];
                BedZ = double.MaxValue;
                foreach (var p in V) if (p.Z < BedZ) BedZ = p.Z;
                var nb = new HashSet<int>[V.Length];
                for (int i = 0; i < V.Length; i++) nb[i] = new HashSet<int>();
                for (int f = 0; f < fc; f++)
                {
                    MeshFace mf = m.Faces[f];
                    A[f] = mf.A; B[f] = mf.B; C[f] = mf.C;
                    Vector3d cr = Vector3d.CrossProduct(V[mf.B] - V[mf.A], V[mf.C] - V[mf.A]);
                    double len = cr.Length;
                    Area[f] = 0.5 * len;
                    N[f] = len > 1e-12 ? cr / len : Vector3d.Zero;
                    Total += Area[f];
                    Bed[f] = V[mf.A].Z <= BedZ + bedTol && V[mf.B].Z <= BedZ + bedTol && V[mf.C].Z <= BedZ + bedTol;
                    nb[mf.A].Add(mf.B); nb[mf.A].Add(mf.C); nb[mf.B].Add(mf.A); nb[mf.B].Add(mf.C); nb[mf.C].Add(mf.A); nb[mf.C].Add(mf.B);
                }
                Nb = nb.Select(h => h.ToArray()).ToArray();
                // a closed part whose faces point inwards (seen with capped lofts): turn the normals out
                double vol = 0;
                for (int f = 0; f < fc; f++) vol += (Vector3d)V[A[f]] * Vector3d.CrossProduct((Vector3d)V[B[f]], (Vector3d)V[C[f]]);
                if (vol < 0 && m.IsClosed) for (int f = 0; f < fc; f++) N[f] = -N[f];
            }
        }

        /// <summary>
        /// Geometry check. The build direction is the plane normal (flat or angled), or, with a
        /// warp, the local layer normal (guide curve: its tangent at the closest point; guide
        /// surface: the normal of the bent layer). A face hangs over when the angle between its
        /// normal and the downward build direction is smaller than 90 deg - limit. Faces on the
        /// bed do not count, nor (flat layers, guides) faces on the start plane. A tilted start plane
        /// is not the bed: a face lying in it would be the first layer, in mid air, and counts.
        /// </summary>
        public static CheckResult Check(Mesh mesh, Plane plane, Warp warp, PrintSettings s, double factor, bool buildMeshes = true)
        {
            var r = new CheckResult();
            if (mesh == null || mesh.Faces.Count == 0) { r.Report.Add("No geometry."); return r; }
            double h = s.LayerHeight;
            r.MaxOffset = MaxOffset(s, factor);
            r.LimitDeg = LimitAngle(r.MaxOffset, h);
            double bedTol = Math.Max(0.01, 0.25 * h);
            var fs = new FaceSet(mesh, bedTol);
            r.TotalArea = fs.Total;

            // build direction per face and stacking coordinate per vertex
            int fc = fs.A.Length;
            var dir = new Vector3d[fc];
            var sv = new double[fs.V.Length];
            Vector3d n0 = plane.Normal; n0.Unitize();
            if (warp == null)
            {
                for (int f = 0; f < fc; f++) dir[f] = n0;
                for (int i = 0; i < sv.Length; i++) sv[i] = (fs.V[i] - plane.Origin) * n0;
            }
            else
            {
                bool tilt = warp.TiltTool;
                warp.TiltTool = true; // only to read the layer normal
                try
                {
                    var sw = warp as SurfaceWarp;
                    if (sw != null && sw.Morph) sw.FlatBand = s.FirstLayer;
                    warp.Prepare(mesh.GetBoundingBox(true));
                    Parallel.For(0, sv.Length, i => sv[i] = warp.ToFlat(fs.V[i]).Z);
                    Parallel.For(0, fc, f =>
                    {
                        Point3d c = (fs.V[fs.A[f]] + fs.V[fs.B[f]] + fs.V[fs.C[f]]) / 3.0;
                        Vector3d ln;
                        warp.ToReal(warp.ToFlat(c), out ln);
                        if (!ln.Unitize()) ln = Vector3d.ZAxis;
                        dir[f] = ln;
                    });
                }
                finally { warp.TiltTool = tilt; }
            }
            Score(fs, dir, sv, r, bedTol, s.BeadWidth, warp != null || Math.Abs(n0.Z) > 0.999);

            // colours and regions
            if (buildMeshes)
            {
                var an = new Mesh();
                var red = new Mesh();
                var redMap = new Dictionary<int, int>();
                var green = System.Drawing.Color.FromArgb(96, 178, 106);
                var orange = System.Drawing.Color.FromArgb(240, 160, 50);
                var crimson = System.Drawing.Color.FromArgb(214, 48, 49);
                var grey = System.Drawing.Color.FromArgb(150, 150, 150);
                double sMin = sv.Min();
                bool startIsBed = warp != null || Math.Abs(n0.Z) > 0.999;
                for (int f = 0; f < fc; f++)
                {
                    int cls = startIsBed && OnStart(fs, f, sv, sMin, bedTol) ? 3 : Class(fs, f, dir[f], sv, bedTol, r.LimitDeg, out _);
                    var col = cls == 3 ? grey : cls == 2 ? crimson : cls == 1 ? orange : green;
                    int b = an.Vertices.Count;
                    an.Vertices.Add(fs.V[fs.A[f]]); an.Vertices.Add(fs.V[fs.B[f]]); an.Vertices.Add(fs.V[fs.C[f]]);
                    an.VertexColors.Add(col); an.VertexColors.Add(col); an.VertexColors.Add(col);
                    an.Faces.AddFace(b, b + 1, b + 2);
                    if (cls == 2)
                    {
                        int[] ids = { fs.A[f], fs.B[f], fs.C[f] };
                        var k = new int[3];
                        for (int j = 0; j < 3; j++)
                        {
                            if (!redMap.TryGetValue(ids[j], out k[j])) { k[j] = red.Vertices.Count; red.Vertices.Add(fs.V[ids[j]]); redMap[ids[j]] = k[j]; }
                        }
                        red.Faces.AddFace(k[0], k[1], k[2]);
                    }
                }
                an.Normals.ComputeNormals();
                r.Analysis = an;
                if (red.Faces.Count > 0)
                {
                    red.Normals.ComputeNormals();
                    Mesh[] pieces = red.SplitDisjointPieces();
                    if (pieces != null && pieces.Length > 0) r.Regions.AddRange(pieces); else r.Regions.Add(red);
                }
            }

            // report
            r.Report.Add(Inv.F($"Bead {F(s.BeadWidth, "0.##")} mm, layer {F(h, "0.##")} mm: a bead may lie {F(r.MaxOffset, "0.##")} mm beside the one below, so walls may lean out up to {F(r.LimitDeg)} deg from the build direction."));
            if (warp != null) r.Report.Add(Inv.F($"Build direction: {warp.Name}, taken per face from the local layer."));
            else r.Report.Add(Inv.F($"Build direction: {DirText(n0)}."));
            r.Percent = r.TotalArea > 0 ? 100.0 * r.UnsupportedArea / r.TotalArea : 0;
            if (r.UnsupportedArea <= 0) r.Report.Add(Inv.F($"Self-supporting: no face leans out more than {F(r.LimitDeg)} deg (worst {F(r.WorstOverhang)} deg)."));
            else r.Report.Add(Inv.F($"NOT SELF-SUPPORTING: {F(r.UnsupportedArea, "0")} mm2 ({F(r.Percent, "0.0")} % of the surface) lean out more than {F(r.LimitDeg)} deg, worst {F(r.WorstOverhang)} deg. Shown red."));
            if (r.NearArea > 0) r.Report.Add(Inv.F($"Near the limit (within {F(NearBand, "0")} deg): {F(r.NearArea, "0")} mm2, shown orange."));
            if (r.Starts.Count > 0) r.Report.Add(Inv.F($"{r.Starts.Count} place(s) start in mid air: the lowest point of an island with nothing below it. See Starts."));
            if (!r.Ok) r.Report.Add("Fix: try Best Angle, or make the layers self-supporting with Self Supporting Envelope.");
            return r;
        }

        private static bool OnStart(FaceSet fs, int f, double[] sv, double sMin, double tol) =>
            sv[fs.A[f]] <= sMin + tol && sv[fs.B[f]] <= sMin + tol && sv[fs.C[f]] <= sMin + tol;

        /// <summary>0 = ok, 1 = near the limit, 2 = unsupported, 3 = on the bed or the start plane.</summary>
        private static int Class(FaceSet fs, int f, Vector3d d, double[] sv, double bedTol, double limit, out double overhang)
        {
            overhang = 0;
            if (fs.Area[f] <= 0) return 0;
            if (fs.Bed[f]) return 3;
            // angle between the normal and the downward build direction; overhang from the build direction
            double c = -(fs.N[f] * d);
            if (c > 1) c = 1; else if (c < -1) c = -1;
            double beta = Math.Acos(c) * 180.0 / Math.PI;
            overhang = 90.0 - beta;
            if (overhang <= 0) return 0;
            if (overhang > limit) return 2;
            return overhang > limit - NearBand ? 1 : 0;
        }

        private static void Score(FaceSet fs, Vector3d[] dir, double[] sv, CheckResult r, double bedTol, double bead, bool flatOrGuide)
        {
            double sMin = double.MaxValue, sMax = double.MinValue;
            foreach (double v in sv) { if (v < sMin) sMin = v; if (v > sMax) sMax = v; }
            // the start plane is the bed for flat layers (and for guides, which start square to the part).
            // A tilted start plane is not: a face lying in it is the first layer, in mid air.
            bool startIsBed = flatOrGuide;
            r.Height = sMax - sMin;
            double down = 0, weighted = 0;
            for (int f = 0; f < fs.A.Length; f++)
            {
                // faces on the start plane stand on the bed (or the start feature)
                if (startIsBed && OnStart(fs, f, sv, sMin, bedTol)) continue;
                int cls = Class(fs, f, dir[f], sv, bedTol, r.LimitDeg, out double ov);
                if (cls == 3) continue;
                if (ov > 0) { down += fs.Area[f]; weighted += fs.Area[f] * ov; if (ov > r.WorstOverhang) r.WorstOverhang = ov; }
                if (cls == 2) r.UnsupportedArea += fs.Area[f];
                else if (cls == 1) r.NearArea += fs.Area[f];
            }
            r.MeanOverhang = fs.Total > 0 ? weighted / fs.Total : 0;
            // islands that start in mid air: a vertex lower than all its neighbours, away from the bed
            double eps = 1e-6 * Math.Max(1, r.Height);
            for (int i = 0; i < fs.V.Length; i++)
            {
                if (fs.Nb[i].Length == 0) continue;
                if (fs.V[i].Z <= fs.BedZ + Math.Max(bedTol, bead) || (startIsBed && sv[i] <= sMin + bedTol)) continue;
                bool lowest = true;
                foreach (int j in fs.Nb[i]) if (sv[j] <= sv[i] + eps) { lowest = false; break; }
                if (lowest) r.Starts.Add(fs.V[i]);
            }
        }

        private static string DirText(Vector3d d)
        {
            double tilt = Math.Acos(Math.Max(-1, Math.Min(1, d.Z))) * 180.0 / Math.PI;
            if (tilt < 0.05) return "flat layers (up = Z)";
            return Inv.F($"layers tilted {F(tilt)} deg, leaning towards ({F(d.X, "0.00")}, {F(d.Y, "0.00")}) in plan");
        }

        // ================================================================== best build direction

        public class BestResult
        {
            /// <summary>The base plane turned to the tilt direction. Slice tilts it by Angle about its X axis.</summary>
            public Plane Plane = Plane.WorldXY;
            /// <summary>The layer plane itself (Plane tilted by Angle).</summary>
            public Plane LayerPlane = Plane.WorldXY;
            public double Angle, Direction, UnsupportedArea, Percent;
            public List<string> Report = new List<string>();
        }

        private sealed class Candidate
        {
            public double Angle, Dir, Unsupported, Near, Mean, Height, Score; public int Starts; public Plane Base, Layer;
        }

        /// <summary>
        /// Sweeps the slice angle (minAngle..maxAngle in angleStep) and the tilt direction (rotation
        /// about Z, 0..360 in dirStep) and scores each candidate by its unsupported area (islands
        /// that start in mid air count as a bead-sized square each). Ties: the smaller mean overhang
        /// (the layers follow the part more closely, in 1 deg steps), then the smaller angle, then the lower part.
        /// Angle 0 and direction 0 is the Slice component's own plane: World XY, tilted about X.
        /// </summary>
        public static BestResult BestDirection(Mesh coarse, PrintSettings s, double factor, double minAngle, double maxAngle, double angleStep, double dirStep, bool turn)
        {
            var res = new BestResult();
            if (coarse == null || coarse.Faces.Count == 0) { res.Report.Add("No geometry."); return res; }
            double h = s.LayerHeight;
            double maxOff = MaxOffset(s, factor), limit = LimitAngle(maxOff, h);
            double bedTol = Math.Max(0.01, 0.25 * h);
            var fs = new FaceSet(coarse, bedTol);
            if (angleStep <= 0) angleStep = 5; if (dirStep <= 0) dirStep = 30;
            minAngle = Math.Max(0, Math.Min(89, minAngle)); maxAngle = Math.Max(minAngle, Math.Min(89, maxAngle));

            var cands = new List<Candidate>();
            for (double a = minAngle; a <= maxAngle + 1e-9; a += angleStep)
            {
                if (a < 1e-9 || !turn) { cands.Add(new Candidate { Angle = a, Dir = 0 }); continue; }
                for (double d = 0; d < 360 - 1e-9; d += dirStep) cands.Add(new Candidate { Angle = a, Dir = d });
            }
            Parallel.ForEach(cands, c =>
            {
                Plane b = Plane.WorldXY;
                b.Rotate(c.Dir * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
                Plane l = b;
                l.Rotate(c.Angle * Math.PI / 180.0, l.XAxis, l.Origin);
                c.Base = b; c.Layer = l;
                Vector3d n = l.Normal; n.Unitize();
                var dir = new Vector3d[fs.A.Length];
                for (int f = 0; f < dir.Length; f++) dir[f] = n;
                var sv = new double[fs.V.Length];
                for (int i = 0; i < sv.Length; i++) sv[i] = (fs.V[i] - Point3d.Origin) * n;
                var r = new CheckResult { LimitDeg = limit, MaxOffset = maxOff };
                Score(fs, dir, sv, r, bedTol, s.BeadWidth, Math.Abs(n.Z) > 0.999);
                c.Unsupported = r.UnsupportedArea; c.Near = r.NearArea; c.Mean = r.MeanOverhang; c.Height = r.Height; c.Starts = r.Starts.Count;
                c.Score = r.UnsupportedArea + c.Starts * 4 * s.BeadWidth * s.BeadWidth;
            });
            // compare in steps, so tiny differences of the coarse mesh do not decide
            double bucket = Math.Max(s.BeadWidth * s.BeadWidth, 0.005 * fs.Total);
            var ranked = cands.OrderBy(c => Math.Round(c.Score / bucket)).ThenBy(c => Math.Round(c.Mean)).ThenBy(c => c.Angle)
                .ThenBy(c => Math.Round(c.Height / s.BeadWidth)).ThenBy(c => c.Dir).ToList();
            var best = ranked[0];
            res.Plane = best.Base; res.LayerPlane = best.Layer; res.Angle = best.Angle; res.Direction = best.Dir;
            res.UnsupportedArea = best.Unsupported; res.Percent = fs.Total > 0 ? 100 * best.Unsupported / fs.Total : 0;
            var flat = cands.First(c => c.Angle == cands.Min(k => k.Angle));
            Vector3d lean = best.Layer.Normal; lean.Z = 0;
            res.Report.Add(Inv.F($"Limit {F(limit)} deg (bead {F(s.BeadWidth, "0.##")} mm, layer {F(h, "0.##")} mm, may lie {F(maxOff, "0.##")} mm beside the bead below). {cands.Count} directions tried."));
            res.Report.Add(best.Angle < 1e-9
                ? Inv.F($"Best: flat layers. Unsupported {F(best.Unsupported, "0")} mm2 ({F(res.Percent, "0.0")} %).")
                : Inv.F($"Best: angle {F(best.Angle, "0")} deg, turned {F(best.Dir, "0")} deg about Z (layers lean towards ({F(lean.X, "0.00")}, {F(lean.Y, "0.00")})). Unsupported {F(best.Unsupported, "0")} mm2 ({F(res.Percent, "0.0")} %), mean overhang {F(best.Mean)} deg."));
            if (best.Starts > 0) res.Report.Add(Inv.F($"{best.Starts} island(s) start in mid air in this direction."));
            if (flat != best) res.Report.Add(Inv.F($"For comparison, {(flat.Angle < 1e-9 ? "flat" : F(flat.Angle, "0") + " deg")}: unsupported {F(flat.Unsupported, "0")} mm2."));
            res.Report.Add("Connect Plane and Angle to Slice. Ranking (angle / turn: unsupported mm2, mid-air starts, mean overhang, height):");
            foreach (var c in ranked.Take(10))
                res.Report.Add(Inv.F($"  {F(c.Angle, "0")} deg / {F(c.Dir, "0")} deg: {F(c.Unsupported, "0")} mm2, {c.Starts}, {F(c.Mean)} deg, {F(c.Height, "0")} mm"));
            return res;
        }

        // ================================================================== envelope

        public class EnvelopeResult
        {
            public List<SliceLayer> Layers = new List<SliceLayer>();
            /// <summary>Only the added material, per layer (same planes; empty layers left out).</summary>
            public List<SliceLayer> Added = new List<SliceLayer>();
            /// <summary>The self-supporting part: a closed Brep (loft) or a mesh.</summary>
            public GeometryBase Geometry;
            /// <summary>The added material alone: Brep (loft) or mesh.</summary>
            public GeometryBase AddedGeometry;
            /// <summary>Largest distance of the lofted Brep from the layer contours (mm); -1 = no loft.</summary>
            public double LoftTolerance = -1;
            public double AddedVolume, Volume;
            public int ChangedLayers;
            public List<string> Report = new List<string>();
        }

        private const int Prec = 4;

        /// <summary>
        /// The minimal self-supporting envelope. From the top layer down, every layer must hold
        /// the one above: region_i = region_i UNION offset(project(region_i+1), -maxOffset).
        /// Thin parts of the layer above that this does not reach (narrower than two maxOffsets,
        /// such as the first sliver of an island) are carried down as they are. Angled layers are
        /// cut at the bed, which carries what reaches it. Guide layers are worked in the flat space
        /// of the guide, the same space Path Check uses.
        /// </summary>
        public static EnvelopeResult Envelope(List<SliceLayer> input, PrintSettings s, double factor, bool makeGeometry)
        {
            var res = new EnvelopeResult();
            var log = res.Report;
            if (input == null || input.Count == 0) { log.Add("No layers."); return res; }
            var layers = input.OrderBy(l => l.Index).ToList();
            if (layers.Any(l => l.Rings != null || l.Reference != null || (l.ContourRoles != null && l.ContourRoles.Any(rr => rr != PathRole.Wall))))
            {
                log.Add("These layers already have walls or infill. Connect the Layers output of Slice: the envelope works on the outline of the part.");
                res.Layers = layers; return res;
            }
            Warp warp = layers[0].Warp;
            double bead = s.BeadWidth;
            double m = MaxOffset(s, factor);
            double shrink = 0.95 * m; // a little inside the limit, so the paths stay within it after rounding
            double sliver = 0.02 * bead;

            // the step between layers, and layers in gaps (nothing there yet, but it may have to carry something)
            var planes = layers.Select(l => l.Plane).ToList();
            Vector3d n = planes[0].Normal; n.Unitize();
            var gaps = new List<double>();
            for (int i = 1; i < layers.Count; i++) gaps.Add((planes[i].Origin - planes[i - 1].Origin) * n);
            double step = gaps.Count > 0 ? gaps.OrderBy(g => g).ElementAt(gaps.Count / 2) : s.LayerHeight;
            var stack = new List<SliceLayer>();
            for (int i = 0; i < layers.Count; i++)
            {
                if (i > 0 && step > 0 && gaps[i - 1] > 1.5 * step)
                {
                    int k = (int)Math.Round(gaps[i - 1] / step) - 1;
                    for (int j = 1; j <= k; j++)
                    {
                        var pl = layers[i - 1].Plane; pl.Origin = layers[i - 1].Plane.Origin + n * (gaps[i - 1] * j / (k + 1));
                        stack.Add(new SliceLayer { Plane = pl, Height = step, Warp = warp });
                    }
                    log.Add(Inv.F($"{k} empty layer(s) added between layer {layers[i - 1].Index + 1} and {layers[i].Index + 1}, so they can carry what is above."));
                }
                stack.Add(layers[i]);
            }

            int N = stack.Count;
            var orig = new PathsD[N];
            int open = 0;
            for (int i = 0; i < N; i++) orig[i] = ToRegion(stack[i].Contours, stack[i].Plane, ref open);
            if (open > 0) log.Add(Inv.F($"{open} open contour(s) were left out: the envelope needs closed outlines (a closed part)."));

            // angled layers: nothing below the bed
            bool angled = warp == null && Math.Abs(n.Z) < 0.999;
            double bedZ = double.MaxValue;
            if (angled)
                foreach (var l in stack) foreach (var c in l.Contours) foreach (var p in c) if (p.Z < bedZ) bedZ = p.Z;

            var env = new PathsD[N];
            var grown = new bool[N];
            var added = new PathsD[N];
            env[N - 1] = orig[N - 1];
            added[N - 1] = new PathsD();
            for (int i = N - 2; i >= 0; i--)
            {
                PathsD above = Project(env[i + 1], stack[i + 1].Plane, stack[i].Plane);
                PathsD need = Clipper.InflatePaths(above, -shrink, JoinType.Round, EndType.Polygon, 2.0, Prec);
                // what is still more than maxOffset away from this layer: thin pieces, carried down as they are
                PathsD reach = Clipper.InflatePaths(Clipper.Union(orig[i], need, FillRule.NonZero, Prec), m, JoinType.Round, EndType.Polygon, 2.0, Prec);
                PathsD left = Open(Clipper.Difference(above, reach, FillRule.NonZero, Prec), sliver);
                if (left.Count > 0) need = Clipper.Union(need, left, FillRule.NonZero, Prec);
                if (angled) need = Clipper.Intersect(need, AboveBed(stack[i].Plane, bedZ), FillRule.NonZero, Prec);
                PathsD extra = Clipper.Difference(need, orig[i], FillRule.NonZero, Prec);
                if (AreaOf(extra) < 0.01 * bead * bead) { env[i] = orig[i]; added[i] = new PathsD(); continue; }
                // keep even thin rings: the layer above relies on them
                env[i] = Clean(Clipper.SimplifyPaths(Clipper.Union(orig[i], need, FillRule.NonZero, Prec), 0.005 * bead, true), 0.05 * bead * bead);
                added[i] = Clean(Clipper.Difference(env[i], orig[i], FillRule.NonZero, Prec), 0.05 * bead * bead);
                grown[i] = true;
            }

            // layers out, added material, statistics
            int firstChanged = -1, lastChanged = -1;
            for (int i = 0; i < N; i++)
            {
                var L = stack[i];
                bool changed = grown[i];
                var outL = new SliceLayer { Index = i, Height = L.Height, Plane = L.Plane, Warp = warp };
                outL.Contours = changed ? ToContours(env[i], L.Plane) : new List<Polyline>(L.Contours);
                if (outL.Contours.Count == 0 && !changed) continue;
                res.Layers.Add(outL);
                res.Volume += AreaOf(env[i]) * L.Height;
                if (changed)
                {
                    res.ChangedLayers++;
                    if (firstChanged < 0) firstChanged = i;
                    lastChanged = i;
                    res.AddedVolume += AreaOf(added[i]) * L.Height;
                    res.Added.Add(new SliceLayer { Index = i, Height = L.Height, Plane = L.Plane, Warp = warp, Contours = ToContours(added[i], L.Plane) });
                }
            }
            for (int i = 0; i < res.Layers.Count; i++) res.Layers[i].Index = i;

            // verify: every layer lies within maxOffset of the layer below
            int bad = 0;
            for (int i = 1; i < N; i++)
            {
                PathsD above = Project(env[i], stack[i].Plane, stack[i - 1].Plane);
                PathsD reach = Clipper.InflatePaths(env[i - 1], m * 1.001, JoinType.Round, EndType.Polygon, 2.0, Prec);
                PathsD outside = Clipper.Difference(above, reach, FillRule.NonZero, Prec);
                if (angled) outside = Clipper.Intersect(outside, AboveBed(stack[i - 1].Plane, bedZ), FillRule.NonZero, Prec);
                if (AreaOf(Open(outside, sliver)) > 0.25 * bead * bead) bad++;
            }

            log.Add(Inv.F($"Allowed: {F(m, "0.##")} mm beside the layer below per layer ({F(factor > 0 ? factor : DefaultFactor, "0.##")} x bead {F(bead, "0.##")} mm), {F(LimitAngle(m, s.LayerHeight))} deg at {F(s.LayerHeight, "0.##")} mm layers."));
            if (res.ChangedLayers == 0) log.Add("Already self-supporting: nothing added.");
            else log.Add(Inv.F($"Added {F(res.AddedVolume / 1000.0, "0.0")} cm3 ({F(res.Volume > 0 ? 100 * res.AddedVolume / res.Volume : 0, "0.0")} % of the envelope) in {res.ChangedLayers} layer(s), layers {firstChanged + 1} to {lastChanged + 1}. Print the new layers as they are; mill the added material away afterwards if it is not wanted."));
            log.Add(bad == 0 ? "Checked: every layer now lies within the allowed offset of the layer below." : Inv.F($"{bad} layer(s) still reach further than allowed (see Path Check)."));
            if (warp != null) log.Add("Guide: worked in the flat space of the guide, as Path Check does. On the outside of strong bends the real offset can be a little larger.");
            if (angled) log.Add(Inv.F($"Angled layers: the envelope is cut at the bed (Z {F(bedZ, "0.#")}); what reaches the bed stands on it."));

            if (makeGeometry)
            {
                try { MakeGeometry(res, stack, env, added, warp, bead); }
                catch (Exception ex) { log.Add("The geometry could not be built: " + ex.Message); }
            }
            return res;
        }

        // ------------------------------------------------------------------ envelope geometry

        private static void MakeGeometry(EnvelopeResult res, List<SliceLayer> stack, PathsD[] env, PathsD[] added, Warp warp, double bead)
        {
            var log = res.Report;
            double tol;
            Brep b = LoftStack(stack, env, warp, bead, out tol, out string why);
            if (b != null) { res.Geometry = b; res.LoftTolerance = tol; log.Add(Inv.F($"Geometry: lofted NURBS Brep{(b.IsSolid ? " (closed)" : " (open: the ends could not be capped)")}, within {F(tol, "0.00")} mm of the layer contours (taken at mid-layer).")); }
            else { res.Geometry = SlabMesh(stack, env, warp); log.Add(Inv.F($"Geometry: mesh of the layer stack ({why}). It shows every layer as a slab of its own height.")); }

            // the added material: from the first to the last layer with something added
            int i0 = Array.FindIndex(added, a => a.Count > 0), i1 = Array.FindLastIndex(added, a => a.Count > 0);
            if (i0 < 0) return;
            var sub = stack.GetRange(i0, i1 - i0 + 1);
            var subAdded = added.Skip(i0).Take(i1 - i0 + 1).ToArray();
            Brep ab = subAdded.All(a => a.Count > 0) ? LoftStack(sub, subAdded, warp, bead, out double atol, out string awhy) : null;
            if (ab != null) { res.AddedGeometry = ab; log.Add("Added material: lofted Brep."); }
            else { res.AddedGeometry = SlabMesh(sub, subAdded, warp); log.Add("Added material: mesh of the added layer pieces."); }
        }

        /// <summary>A closed loft through one loop per layer (at mid-layer, plus the bottom of the first and the top of the last layer).</summary>
        private static Brep LoftStack(List<SliceLayer> stack, PathsD[] regions, Warp warp, double bead, out double tolerance, out string why)
        {
            tolerance = -1; why = "";
            for (int i = 0; i < regions.Length; i++)
                if (regions[i].Count != 1) { why = regions[i].Count == 0 ? "a layer is empty" : "some layers have more than one loop or a hole"; return null; }
            if (regions.Length < 2) { why = "only one layer"; return null; }

            // sections in 3D
            var sections = new List<Polyline>();
            var normals = new List<Vector3d>();
            Func<int, double, Polyline> section = (i, shift) =>
            {
                var pl = stack[i].Plane; Vector3d nn = pl.Normal; nn.Unitize();
                var poly = new Polyline(regions[i][0].Select(q => pl.PointAt(q.x, q.y) + nn * shift));
                poly.Add(poly[0]);
                return warp != null ? warp.ToReal(poly, Math.Max(1.0, bead)) : poly;
            };
            sections.Add(section(0, -stack[0].Height)); normals.Add(stack[0].Plane.Normal);
            for (int i = 0; i < regions.Length; i++) { sections.Add(section(i, -0.5 * stack[i].Height)); normals.Add(stack[i].Plane.Normal); }
            sections.Add(section(regions.Length - 1, 0)); normals.Add(stack[regions.Length - 1].Plane.Normal);

            // same direction, seams one above the other
            for (int k = 0; k < sections.Count; k++)
            {
                var pl = sections[k];
                if (Signed(pl, normals[k]) < 0) pl.Reverse();
                if (k > 0)
                {
                    Point3d prev = sections[k - 1][0];
                    int best = 0; double bd = double.MaxValue;
                    for (int j = 0; j < pl.Count - 1; j++) { double d = pl[j].DistanceToSquared(prev); if (d < bd) { bd = d; best = j; } }
                    if (best > 0)
                    {
                        var rot = new Polyline(pl.Count);
                        for (int j = 0; j < pl.Count - 1; j++) rot.Add(pl[(best + j) % (pl.Count - 1)]);
                        rot.Add(rot[0]);
                        pl = rot;
                    }
                }
                sections[k] = pl;
            }

            // NURBS sections with one point count for all: enough for 0.1 x bead on the worst section
            double target = 0.1 * bead;
            double perim = sections.Max(p => p.Length);
            int count = Math.Max(16, Math.Min(400, (int)Math.Ceiling(perim / (2 * bead))));
            List<Curve> curves = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                curves = sections.Select(p => (Curve)new PolylineCurve(p).Rebuild(count, 3, false)).ToList();
                if (curves.Any(c => c == null)) { why = "the contours could not be rebuilt"; return null; }
                double dev = 0;
                for (int k = 0; k < sections.Count; k += Math.Max(1, sections.Count / 12))
                    dev = Math.Max(dev, Deviation(sections[k], curves[k]));
                if (dev <= target || count >= 400) break;
                count = Math.Min(400, (int)(count * 1.7));
            }

            Brep[] loft = Brep.CreateFromLoft(curves, Point3d.Unset, Point3d.Unset, LoftType.Loose, false);
            if (loft == null || loft.Length == 0) { why = "the loft failed"; return null; }
            Brep b = loft[0];
            Brep capped = b.CapPlanarHoles(Math.Max(0.01, 0.01 * bead));
            if (capped != null) b = capped;
            if (b.IsSolid && b.SolidOrientation == BrepSolidOrientation.Inward) b.Flip();

            // tolerance: distance of the contours (not the rebuilt curves) from the Brep
            double maxDev = 0;
            foreach (var pl in sections)
            {
                int stepPts = Math.Max(1, (pl.Count - 1) / 48);
                for (int j = 0; j < pl.Count - 1; j += stepPts)
                {
                    Point3d c = b.ClosestPoint(pl[j]);
                    if (c.IsValid) maxDev = Math.Max(maxDev, c.DistanceTo(pl[j]));
                }
            }
            tolerance = maxDev;
            return b;
        }

        private static double Deviation(Polyline pl, Curve c)
        {
            double max = 0;
            int stepPts = Math.Max(1, (pl.Count - 1) / 200);
            for (int j = 0; j < pl.Count - 1; j += stepPts)
            {
                double t;
                if (c.ClosestPoint(pl[j], out t)) max = Math.Max(max, c.PointAt(t).DistanceTo(pl[j]));
            }
            return max;
        }

        private static double Signed(Polyline pl, Vector3d n)
        {
            Vector3d a = Vector3d.Zero;
            for (int i = 0; i < pl.Count - 1; i++) a += Vector3d.CrossProduct((Vector3d)pl[i], (Vector3d)pl[i + 1]);
            return a * n;
        }

        /// <summary>Every layer region as a slab of its own height: side walls and both caps.</summary>
        private static Mesh SlabMesh(List<SliceLayer> stack, PathsD[] regions, Warp warp)
        {
            var mesh = new Mesh();
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i] == null || regions[i].Count == 0) continue;
                var pl = stack[i].Plane; Vector3d nn = pl.Normal; nn.Unitize();
                double hh = stack[i].Height;
                var slab = new Mesh();
                var loopsTop = new List<List<Point3d>>();
                var loopsBot = new List<List<Point3d>>();
                foreach (var path in regions[i])
                {
                    if (path.Count < 3) continue;
                    var top = path.Select(q => pl.PointAt(q.x, q.y)).ToList();
                    var bot = top.Select(p => p - nn * hh).ToList();
                    loopsTop.Add(top); loopsBot.Add(bot);
                    int b0 = slab.Vertices.Count;
                    foreach (var p in bot) slab.Vertices.Add(p);
                    foreach (var p in top) slab.Vertices.Add(p);
                    int c = top.Count;
                    for (int j = 0; j < c; j++)
                    {
                        int j1 = (j + 1) % c;
                        slab.Faces.AddFace(b0 + j, b0 + j1, b0 + c + j1, b0 + c + j);
                    }
                }
                foreach (var loops in new[] { loopsTop, loopsBot })
                {
                    if (loops.Count == 0) continue;
                    Plane cp = pl; cp.Origin = loops[0][0];
                    Mesh cap = null;
                    try { cap = Mesh.CreateFromTessellation(loops.SelectMany(q => q), loops.Select(q => (IEnumerable<Point3d>)q.Concat(new[] { q[0] }).ToList()), cp, false); }
                    catch { cap = null; }
                    if (cap != null) slab.Append(cap);
                }
                if (warp != null)
                    for (int v = 0; v < slab.Vertices.Count; v++) slab.Vertices.SetVertex(v, warp.ToReal(slab.Vertices[v], out _));
                mesh.Append(slab);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        // ------------------------------------------------------------------ 2D helpers

        private static PathsD ToRegion(List<Polyline> contours, Plane pl, ref int open)
        {
            var paths = new PathsD();
            foreach (var c in contours)
            {
                if (c == null || c.Count < 4) continue;
                if (!Geo.Closed(c) && c.First.DistanceTo(c.Last) > 1e-3) { open++; continue; }
                var p = new PathD(c.Count);
                for (int i = 0; i < c.Count - 1; i++) { Vector3d v = c[i] - pl.Origin; p.Add(new PointD(v * pl.XAxis, v * pl.YAxis)); }
                paths.Add(p);
            }
            return paths.Count == 0 ? paths : Clipper.Union(paths, new PathsD(), FillRule.EvenOdd, Prec);
        }

        private static List<Polyline> ToContours(PathsD paths, Plane pl)
        {
            var list = new List<Polyline>();
            foreach (var p in paths)
            {
                if (p.Count < 3 || Math.Abs(Clipper.Area(p)) < 1) continue;
                var poly = new Polyline(p.Select(q => pl.PointAt(q.x, q.y)));
                poly.Add(poly[0]);
                list.Add(poly);
            }
            return list;
        }

        /// <summary>A region of one layer plane, projected along that plane's normal into another one.</summary>
        private static PathsD Project(PathsD region, Plane from, Plane to)
        {
            var result = new PathsD(region.Count);
            foreach (var path in region)
            {
                var p = new PathD(path.Count);
                foreach (var q in path)
                {
                    Vector3d v = from.PointAt(q.x, q.y) - to.Origin;
                    p.Add(new PointD(v * to.XAxis, v * to.YAxis));
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>Drops loops (and holes) smaller than minArea.</summary>
        private static PathsD Clean(PathsD region, double minArea)
        {
            var r = new PathsD(region.Count);
            foreach (var p in region) if (Math.Abs(Clipper.Area(p)) >= minArea) r.Add(p);
            return r;
        }

        /// <summary>Removes pieces thinner than 2 r (morphological opening).</summary>
        private static PathsD Open(PathsD region, double r)
        {
            if (region.Count == 0) return region;
            var shrunk = Clipper.InflatePaths(region, -r, JoinType.Round, EndType.Polygon, 2.0, Prec);
            if (shrunk.Count == 0) return shrunk;
            return Clipper.Intersect(Clipper.InflatePaths(shrunk, r, JoinType.Round, EndType.Polygon, 2.0, Prec), region, FillRule.NonZero, Prec);
        }

        private static double AreaOf(PathsD region)
        {
            double a = 0;
            foreach (var p in region) a += Clipper.Area(p);
            return Math.Abs(a);
        }

        /// <summary>The half of a layer plane above the bed, as a large polygon in plane coordinates.</summary>
        private static PathsD AboveBed(Plane pl, double bedZ)
        {
            var g = new PointD(pl.XAxis.Z, pl.YAxis.Z);
            double gl = Math.Sqrt(g.x * g.x + g.y * g.y);
            const double M = 100000;
            if (gl < 1e-9)
                return new PathsD { new PathD { new PointD(-M, -M), new PointD(M, -M), new PointD(M, M), new PointD(-M, M) } };
            double c = bedZ - pl.Origin.Z;
            var gu = new PointD(g.x / gl, g.y / gl); var tt = new PointD(-gu.y, gu.x);
            var q0 = new PointD(gu.x * c / gl, gu.y * c / gl);
            return new PathsD { new PathD {
                new PointD(q0.x + tt.x * M, q0.y + tt.y * M), new PointD(q0.x - tt.x * M, q0.y - tt.y * M),
                new PointD(q0.x - tt.x * M + gu.x * M, q0.y - tt.y * M + gu.y * M), new PointD(q0.x + tt.x * M + gu.x * M, q0.y + tt.y * M + gu.y * M) } };
        }
    }
}
