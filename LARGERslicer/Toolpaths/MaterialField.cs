using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Orthotropic material of a printed part, in the frame of the bead:
    /// 1 = along the bead, 2 = across the bead in the layer, 3 = across the layers (stacking).
    /// The default values are PLACEHOLDERS of the right order for a fibre-filled pellet material;
    /// real values must come from tests on printed coupons (see the report of the component).
    /// Units: MPa. Poisson ratios nu_ij = -strain_j / strain_i under stress in i.
    /// </summary>
    public class OrthotropicMaterial
    {
        public double E1 = 3000, E2 = 2400, E3 = 1800;     // MPa: along bead, across bead, across layers
        public double G12 = 1100, G13 = 700, G23 = 700;    // MPa: in the layer, bead/layer, across/layer
        public double Nu12 = 0.35, Nu13 = 0.30, Nu23 = 0.30;

        public OrthotropicMaterial Duplicate() => (OrthotropicMaterial)MemberwiseClone();

        /// <summary>6x6 compliance in Voigt order 11, 22, 33, 23, 13, 12 (engineering shear strains).</summary>
        public double[,] Compliance()
        {
            var s = new double[6, 6];
            s[0, 0] = 1 / E1; s[1, 1] = 1 / E2; s[2, 2] = 1 / E3;
            s[0, 1] = s[1, 0] = -Nu12 / E1;
            s[0, 2] = s[2, 0] = -Nu13 / E1;
            s[1, 2] = s[2, 1] = -Nu23 / E2;
            s[3, 3] = 1 / G23; s[4, 4] = 1 / G13; s[5, 5] = 1 / G12;
            return s;
        }

        /// <summary>6x6 stiffness in the bead frame (Voigt order 11, 22, 33, 23, 13, 12).</summary>
        public double[,] Stiffness()
        {
            var s = Compliance();
            var c = new double[6, 6];
            // normal block: invert the 3x3
            double a = s[0, 0], b = s[0, 1], cc = s[0, 2], d = s[1, 1], e = s[1, 2], f = s[2, 2];
            double det = a * (d * f - e * e) - b * (b * f - e * cc) + cc * (b * e - d * cc);
            c[0, 0] = (d * f - e * e) / det; c[0, 1] = (cc * e - b * f) / det; c[0, 2] = (b * e - cc * d) / det;
            c[1, 1] = (a * f - cc * cc) / det; c[1, 2] = (b * cc - a * e) / det; c[2, 2] = (a * d - b * b) / det;
            c[1, 0] = c[0, 1]; c[2, 0] = c[0, 2]; c[2, 1] = c[1, 2];
            c[3, 3] = G23; c[4, 4] = G13; c[5, 5] = G12;
            return c;
        }

        /// <summary>Empty when the values make a valid (positive definite) material, otherwise what is wrong.</summary>
        public string Check()
        {
            if (E1 <= 0 || E2 <= 0 || E3 <= 0 || G12 <= 0 || G13 <= 0 || G23 <= 0) return "All moduli must be larger than 0.";
            var s = Compliance();
            double det = s[0, 0] * (s[1, 1] * s[2, 2] - s[1, 2] * s[1, 2]) - s[0, 1] * (s[0, 1] * s[2, 2] - s[1, 2] * s[0, 2]) + s[0, 2] * (s[0, 1] * s[1, 2] - s[1, 1] * s[0, 2]);
            if (Nu12 * Nu12 >= E1 / E2 || Nu13 * Nu13 >= E1 / E3 || Nu23 * Nu23 >= E2 / E3 || det <= 0)
                return "These Poisson ratios do not give a valid material (the stiffness is not positive definite). Make them smaller.";
            return "";
        }

        /// <summary>
        /// Rotates a stiffness from the bead frame (e1, e2, e3: unit vectors in world coordinates)
        /// into world coordinates: C_world = K C K^T with the Bond stress matrix K of R = [e1 e2 e3].
        /// </summary>
        public static double[,] Rotate(double[,] c, Vector3d e1, Vector3d e2, Vector3d e3)
        {
            // a[i,j] = component i of local axis j
            var a = new double[3, 3]
            {
                { e1.X, e2.X, e3.X },
                { e1.Y, e2.Y, e3.Y },
                { e1.Z, e2.Z, e3.Z }
            };
            var k = new double[6, 6];
            int[] vi = { 0, 1, 2, 1, 0, 0 }, vj = { 0, 1, 2, 2, 2, 1 }; // Voigt index -> tensor indices
            for (int I = 0; I < 6; I++)
            {
                int i = vi[I], j = vj[I];
                for (int J = 0; J < 6; J++)
                {
                    int p = vi[J], q = vj[J];
                    k[I, J] = J < 3 ? a[i, p] * a[j, q] : a[i, p] * a[j, q] + a[i, q] * a[j, p];
                }
            }
            var kc = new double[6, 6];
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                {
                    double sum = 0;
                    for (int m = 0; m < 6; m++) sum += k[i, m] * c[m, j];
                    kc[i, j] = sum;
                }
            var r = new double[6, 6];
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                {
                    double sum = 0;
                    for (int m = 0; m < 6; m++) sum += kc[i, m] * k[j, m];
                    r[i, j] = sum;
                }
            return r;
        }

        /// <summary>Upper triangle row by row: C11 C12 C13 C14 C15 C16 C22 ... C66 (21 values).</summary>
        public static double[] Upper(double[,] c)
        {
            var u = new double[21]; int n = 0;
            for (int i = 0; i < 6; i++) for (int j = i; j < 6; j++) u[n++] = c[i, j];
            return u;
        }

        public static readonly string[] UpperNames = Enumerable.Range(0, 6).SelectMany(i => Enumerable.Range(i, 6 - i).Select(j => "C" + (i + 1) + (j + 1))).ToArray();

        public override string ToString() =>
            $"E {E1:0.#} / {E2:0.#} / {E3:0.#} MPa, G {G12:0.#} / {G13:0.#} / {G23:0.#} MPa, nu {Nu12:0.###} / {Nu13:0.###} / {Nu23:0.###}";
    }

    /// <summary>The material at one sample point of the part.</summary>
    public class MaterialSample
    {
        public int Face = -1;              // Brep face index, -1 = not on a face
        public double U = -1, V = -1;      // face parameters (the NURBS parameters of that face)
        public Point3d P;
        public Vector3d E1, E2, E3;        // along the bead, across the bead in the layer, stacking direction
        public int Layer = -1;             // -1 = no bead within reach (not printed there)
        public int Bead = -1;              // running number of the bead (one uninterrupted piece of path in one layer)
        public double DistToBead = -1;     // mm from the bead centre line
        public double DistLayerInterface = -1; // mm to the nearest layer-to-layer interface, -1 = none (single layer)
        public double DistBeadInterface = -1;  // mm to the nearest bead-to-bead interface in the layer, -1 = no neighbour bead
        public double BeadSpacing = -1;    // mm centre distance to the neighbour bead, -1 = none
        public double Overlap = -1;        // (width - spacing) / width, -1 = no neighbour
        public double LayerTime;           // s to print the whole layer
        public double InterfaceTime = -1;  // s between printing this bead and the bead across the nearest layer interface
        public double Weakness;            // 0..1: closeness to a layer interface x its cooling (see MaterialField)
        public double Temperature = -1;    // slot for a print temperature proxy; -1 = no data yet
        public double[] C;                 // 21 values of the world stiffness (MaterialOptions.Stiffness)
    }

    public class MaterialFieldOptions
    {
        /// <summary>Distance of the sample points in mm. 0 = half the layer height (resolves every layer).</summary>
        public double Spacing = 0;
        public OrthotropicMaterial Material = new OrthotropicMaterial();
        /// <summary>Time in s after which a layer counts as cold for the next one (placeholder, from tests).</summary>
        public double CoolingTime = 60;
        public int MaxSamples = 200000;
        public bool Stiffness = true;
    }

    public class MaterialFieldResult
    {
        public List<MaterialSample> Samples = new List<MaterialSample>();
        /// <summary>The surface grid coloured by weakness (only with geometry).</summary>
        public Mesh Mesh;
        public double[] LayerTimes = new double[0];
        public double Spacing;
        public double MaxWeakness;
        public OrthotropicMaterial Material;
        public PrintSettings Settings;
        public List<string> Log = new List<string>();
        public string Info = "";
    }

    /// <summary>
    /// Maps a toolpath back onto the part as a direction-dependent material field, so an analysis
    /// on the NURBS geometry (IGA, FE) can use the print's anisotropy: for every sample point the
    /// nearest bead gives the local frame (e1 along the bead, e3 the stacking direction, e2 = e3 x e1),
    /// the layer and bead, the distances to the layer and bead interfaces and how long the layer
    /// below had to cool. The bead is taken as a slab of the local layer height under the path
    /// (the path is the top of the layer, where the nozzle runs).
    /// Weakness = closeness to the nearest layer interface (1 on it, 0 in the middle of the bead)
    /// x (1 - exp(-interface time / cooling time)).
    /// </summary>
    public static class MaterialField
    {
        private class Segments
        {
            public int N;
            public Point3d[] A, B;        // centre line of the bead (path shifted down by half the layer height)
            public Vector3d[] Dir, E3;
            public double[] Len, T0, T1, H, W;
            public int[] Layer, Bead;
            public double Cell;
            public Dictionary<long, List<int>> Grid = new Dictionary<long, List<int>>();
        }

        private static Vector3d Unit(Vector3d v)
        {
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return l > 1e-12 ? new Vector3d(v.X / l, v.Y / l, v.Z / l) : Vector3d.Zero;
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x + (1 << 20)) << 42) | ((long)(y + (1 << 20)) << 21) | (long)(z + (1 << 20));

        public static MaterialFieldResult Compute(Toolpath tp, IList<Brep> breps, IList<Point3d> points, MaterialFieldOptions o)
        {
            var r = new MaterialFieldResult { Material = o.Material, Settings = tp.Settings };
            var st = tp.Settings ?? new PrintSettings();
            string bad = o.Material.Check();
            if (bad.Length > 0) r.Log.Add(bad);

            var seg = BuildSegments(tp, st, out double[] layerTimes);
            r.LayerTimes = layerTimes;
            if (seg.N == 0) { r.Log.Add("The toolpath has no printing moves."); return r; }

            double spacing = o.Spacing > 0 ? o.Spacing : 0.5 * st.LayerHeight;
            if (spacing <= 0) spacing = 1.0;

            // sample points: given points, a UV grid on every Brep face, or the beads themselves
            var samples = new List<MaterialSample>();
            var grids = new List<int[]>(); // per face: nu, nv, first sample index (-1 = outside the trim)
            var nodeIndex = new List<int[]>();
            if (points != null && points.Count > 0)
            {
                foreach (var p in points)
                {
                    var s = new MaterialSample { P = p };
                    if (breps != null)
                    {
                        double best = double.MaxValue;
                        for (int b = 0, faceBase = 0; b < breps.Count; faceBase += breps[b].Faces.Count, b++)
                        {
                            var br = breps[b];
                            if (br == null) continue;
                            if (!br.ClosestPoint(p, out Point3d cp, out ComponentIndex ci, out double u, out double v, 0, out Vector3d _)) continue;
                            double d = cp.DistanceTo(p);
                            if (d < best && ci.ComponentIndexType == ComponentIndexType.BrepFace)
                            {
                                best = d; s.Face = faceBase + ci.Index; s.U = u; s.V = v;
                            }
                        }
                        if (best > Math.Max(0.01, 0.5 * spacing)) { s.Face = -1; s.U = s.V = -1; } // inside the volume, not on a face
                    }
                    samples.Add(s);
                }
                r.Log.Add($"{points.Count} given points (e.g. Gauss points of the analysis).");
            }
            else if (breps != null && breps.Count > 0)
            {
                // spacing so that the grid stays below MaxSamples
                for (int pass = 0; pass < 2; pass++)
                {
                    long total = 0;
                    foreach (var br in breps)
                        if (br != null)
                            foreach (var f in br.Faces)
                            {
                                f.GetSurfaceSize(out double w, out double h);
                                total += (long)(Math.Ceiling(w / spacing) + 1) * (long)(Math.Ceiling(h / spacing) + 1);
                            }
                    if (total <= o.MaxSamples) break;
                    double old = spacing;
                    spacing *= Math.Sqrt(total / (double)o.MaxSamples) * 1.02;
                    r.Log.Add($"{total} samples at {old:0.##} mm would be too many; spacing set to {spacing:0.##} mm.");
                }
                int faceNo = 0;
                foreach (var br in breps)
                {
                    if (br == null) continue;
                    foreach (var f in br.Faces)
                    {
                        f.GetSurfaceSize(out double w, out double h);
                        int nu = Math.Max(1, (int)Math.Ceiling(w / spacing)), nv = Math.Max(1, (int)Math.Ceiling(h / spacing));
                        Interval du = f.Domain(0), dv = f.Domain(1);
                        var idx = new int[(nu + 1) * (nv + 1)];
                        for (int j = 0; j <= nv; j++)
                            for (int i = 0; i <= nu; i++)
                            {
                                double u = du.ParameterAt(i / (double)nu), v = dv.ParameterAt(j / (double)nv);
                                if (f.IsPointOnFace(u, v) == PointFaceRelation.Exterior) { idx[j * (nu + 1) + i] = -1; continue; }
                                idx[j * (nu + 1) + i] = samples.Count;
                                samples.Add(new MaterialSample { Face = faceNo, U = u, V = v, P = f.PointAt(u, v) });
                            }
                        grids.Add(new[] { nu, nv });
                        nodeIndex.Add(idx);
                        faceNo++;
                    }
                }
                if (spacing > 0.51 * st.LayerHeight)
                    r.Log.Add($"Sample spacing {spacing:0.##} mm is more than half the layer height: the layer interfaces are not resolved, read Weakness as a rough value.");
            }
            else
            {
                // volume samples: along every bead, at its centre and near its bottom and top
                for (int k = 0; k < seg.N; k++)
                {
                    int parts = Math.Max(1, (int)Math.Ceiling(seg.Len[k] / spacing));
                    for (int m = 0; m < parts; m++)
                    {
                        Point3d c = seg.A[k] + (seg.B[k] - seg.A[k]) * ((m + 0.5) / parts);
                        foreach (double f in new[] { -0.4, 0.0, 0.4 })
                            samples.Add(new MaterialSample { P = c + seg.E3[k] * (f * seg.H[k]) });
                    }
                    if (samples.Count > o.MaxSamples) { r.Log.Add($"Stopped at {o.MaxSamples} volume samples; set a larger spacing."); break; }
                }
                r.Log.Add("No geometry: samples lie in the beads (centre and 40 % of the layer height above and below).");
            }
            r.Spacing = spacing;

            var stiff = o.Stiffness && bad.Length == 0 ? o.Material.Stiffness() : null;
            double reach = 1.5 * st.BeadWidth + 1.5 * st.LayerHeight + 0.5 * spacing;
            Parallel.For(0, samples.Count, i => Evaluate(samples[i], seg, layerTimes, reach, o, stiff));

            int missing = samples.Count(s => s.Layer < 0);
            if (missing > 0) r.Log.Add($"{missing} of {samples.Count} samples have no bead within {reach:0.#} mm (not printed there): layer -1, no directions.");
            r.Samples = samples;
            r.MaxWeakness = samples.Count > 0 ? samples.Max(s => s.Weakness) : 0;

            if (nodeIndex.Count > 0) r.Mesh = BuildMesh(samples, grids, nodeIndex, r.MaxWeakness);

            r.Info = $"{samples.Count} samples, spacing {spacing:0.##} mm, {seg.N} bead segments in {layerTimes.Count(t => t > 0)} layers. " +
                     $"Layer time {(layerTimes.Length > 0 ? layerTimes.Where(t => t > 0).DefaultIfEmpty(0).Min() : 0):0.#} to {(layerTimes.Length > 0 ? layerTimes.Max() : 0):0.#} s. " +
                     $"Largest weakness {r.MaxWeakness:0.###} (colours run from 0 to it). Material: {o.Material}.";
            return r;
        }

        private static Segments BuildSegments(Toolpath tp, PrintSettings st, out double[] layerTimes)
        {
            var proc = PathEngine.Process(tp, false);
            int n = tp.Count;
            var time = new double[n]; // s
            for (int i = 1; i < n; i++)
                time[i] = time[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]) / Math.Max(1e-6, proc.Speed[i]) * 60.0;

            int maxLayer = 0;
            for (int i = 0; i < n; i++) if (tp.Layers[i] > maxLayer) maxLayer = tp.Layers[i];
            layerTimes = new double[maxLayer + 1];
            for (int i = 1; i < n; i++) if (tp.Layers[i] >= 0) layerTimes[tp.Layers[i]] += time[i] - time[i - 1];

            bool Printing(int i) => i > 0 && tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel && tp.Points[i].DistanceToSquared(tp.Points[i - 1]) > 1e-12;

            // stacking direction of every flat layer: plane through its points; the sign follows the tool axis
            var layerNormal = new Vector3d[maxLayer + 1];
            if (tp.Warp == null)
            {
                var pts = new List<Point3d>[maxLayer + 1];
                var axis = new Vector3d[maxLayer + 1];
                for (int L = 0; L <= maxLayer; L++) pts[L] = new List<Point3d>();
                for (int i = 1; i < n; i++)
                {
                    if (!Printing(i) || tp.Layers[i] < 0) continue;
                    axis[tp.Layers[i]] += tp.Normals[i];
                    if (tp.Roles[i] == PathRole.Seam) continue; // the ramp to the next layer is not in the plane
                    pts[tp.Layers[i]].Add(tp.Points[i]);
                }
                for (int L = 0; L <= maxLayer; L++)
                {
                    Vector3d a = Unit(axis[L]);
                    Vector3d fit = FitNormal(pts[L]);
                    if (fit.IsZero) fit = a;
                    else if (!a.IsZero && fit * a < 0) fit = -fit;
                    layerNormal[L] = fit.IsZero ? Vector3d.ZAxis : fit;
                }
            }

            var s = new Segments();
            var A = new List<Point3d>(); var B = new List<Point3d>(); var D = new List<Vector3d>(); var E = new List<Vector3d>();
            var len = new List<double>(); var t0 = new List<double>(); var t1 = new List<double>(); var hh = new List<double>(); var ww = new List<double>();
            var lay = new List<int>(); var bead = new List<int>();
            int beadNo = -1; int last = -2;
            for (int i = 1; i < n; i++)
            {
                if (!Printing(i)) continue;
                if (last != i - 1 || tp.Layers[i] != tp.Layers[i - 1] || tp.Roles[i] != tp.Roles[i - 1]) beadNo++;
                last = i;
                Vector3d dir = tp.Points[i] - tp.Points[i - 1];
                double l = dir.Length;
                dir = Unit(dir);
                int L = Math.Max(0, tp.Layers[i]);
                Vector3d e3 = tp.Warp != null ? WarpNormal(tp.Warp, 0.5 * (tp.Points[i] + (Vector3d)tp.Points[i - 1]), tp.Normals[i]) : layerNormal[L];
                double h = tp.Heights[i] > 0 ? tp.Heights[i] : st.LayerHeight;
                double w = tp.Widths[i] > 0 ? tp.Widths[i] : st.BeadWidth;
                Vector3d down = e3 * (-0.5 * h);
                A.Add(tp.Points[i - 1] + down); B.Add(tp.Points[i] + down); D.Add(dir); E.Add(e3);
                len.Add(l); t0.Add(time[i - 1]); t1.Add(time[i]); hh.Add(h); ww.Add(w); lay.Add(L); bead.Add(beadNo);
            }
            s.N = A.Count; s.A = A.ToArray(); s.B = B.ToArray(); s.Dir = D.ToArray(); s.E3 = E.ToArray();
            s.Len = len.ToArray(); s.T0 = t0.ToArray(); s.T1 = t1.ToArray(); s.H = hh.ToArray(); s.W = ww.ToArray();
            s.Layer = lay.ToArray(); s.Bead = bead.ToArray();

            // grid of pieces no longer than one cell, filed by their midpoint
            s.Cell = Math.Max(1.0, Math.Max(st.BeadWidth, st.LayerHeight));
            for (int k = 0; k < s.N; k++)
            {
                int parts = Math.Max(1, (int)Math.Ceiling(s.Len[k] / s.Cell));
                for (int m = 0; m < parts; m++)
                {
                    Point3d c = s.A[k] + (s.B[k] - s.A[k]) * ((m + 0.5) / parts);
                    long key = Key((int)Math.Floor(c.X / s.Cell), (int)Math.Floor(c.Y / s.Cell), (int)Math.Floor(c.Z / s.Cell));
                    if (!s.Grid.TryGetValue(key, out var list)) s.Grid[key] = list = new List<int>(4);
                    if (list.Count == 0 || list[list.Count - 1] != k) list.Add(k);
                }
            }
            return s;
        }

        /// <summary>Normal of the best plane through the points (smallest axis of their spread); zero if they lie on a line.</summary>
        private static Vector3d FitNormal(List<Point3d> pts)
        {
            if (pts.Count < 3) return Vector3d.Zero;
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= pts.Count; cy /= pts.Count; cz /= pts.Count;
            var m = new double[3, 3];
            foreach (var p in pts)
            {
                double x = p.X - cx, y = p.Y - cy, z = p.Z - cz;
                m[0, 0] += x * x; m[0, 1] += x * y; m[0, 2] += x * z; m[1, 1] += y * y; m[1, 2] += y * z; m[2, 2] += z * z;
            }
            m[1, 0] = m[0, 1]; m[2, 0] = m[0, 2]; m[2, 1] = m[1, 2];
            Jacobi(m, out double[] val, out double[,] vec);
            int lo = 0, mid = 0, hi = 0;
            for (int i = 1; i < 3; i++) { if (val[i] < val[lo]) lo = i; if (val[i] > val[hi]) hi = i; }
            mid = 3 - lo - hi;
            if (lo == hi || val[mid] < 1e-9 * Math.Max(1e-12, val[hi]) || val[mid] < 1e-6) return Vector3d.Zero; // a line
            if (val[lo] > 0.05 * val[mid]) return Vector3d.Zero; // not flat
            return Unit(new Vector3d(vec[0, lo], vec[1, lo], vec[2, lo]));
        }

        private static void Jacobi(double[,] a, out double[] d, out double[,] v)
        {
            v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            a = (double[,])a.Clone();
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-15 * (Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1e-300)) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        if (theta == 0) t = 1;
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                        }
                    }
            }
            d = new[] { a[0, 0], a[1, 1], a[2, 2] };
        }

        /// <summary>Real normal of a bent layer: the flat layer (constant flat Z) mapped back, by finite differences.</summary>
        private static Vector3d WarpNormal(Warp warp, Point3d real, Vector3d fallback)
        {
            try
            {
                Point3d f = warp.ToFlat(real);
                const double d = 1.0;
                Vector3d gx = warp.ToReal(f + new Vector3d(d, 0, 0), out _) - warp.ToReal(f - new Vector3d(d, 0, 0), out _);
                Vector3d gy = warp.ToReal(f + new Vector3d(0, d, 0), out _) - warp.ToReal(f - new Vector3d(0, d, 0), out _);
                Vector3d up = warp.ToReal(f + new Vector3d(0, 0, d), out _) - warp.ToReal(f, out _);
                Vector3d nn = Unit(Vector3d.CrossProduct(gx, gy));
                if (nn.IsZero) return Unit(fallback);
                return nn * up < 0 ? -nn : nn;
            }
            catch { return Unit(fallback); }
        }

        private static Point3d Closest(Segments s, int k, Point3d p, out double t)
        {
            Vector3d ab = s.B[k] - s.A[k];
            double l2 = ab * ab;
            t = l2 > 1e-18 ? Math.Max(0, Math.Min(1, ((p - s.A[k]) * ab) / l2)) : 0;
            return s.A[k] + ab * t;
        }

        /// <summary>Nearest bead piece to p that the filter accepts (filter gets segment, closest point, parameter).</summary>
        private static int Nearest(Segments s, Point3d p, double maxDist, Func<int, Point3d, bool> accept, out Point3d best, out double bestT, out double bestD)
        {
            best = Point3d.Unset; bestT = 0; bestD = double.MaxValue;
            int found = -1;
            double c = s.Cell;
            int cx = (int)Math.Floor(p.X / c), cy = (int)Math.Floor(p.Y / c), cz = (int)Math.Floor(p.Z / c);
            int R = (int)Math.Ceiling(maxDist / c) + 1;
            for (int r = 0; r <= R; r++)
            {
                for (int x = -r; x <= r; x++)
                    for (int y = -r; y <= r; y++)
                        for (int z = -r; z <= r; z++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != r) continue;
                            if (!s.Grid.TryGetValue(Key(cx + x, cy + y, cz + z), out var list)) continue;
                            foreach (int k in list)
                            {
                                Point3d q = Closest(s, k, p, out double t);
                                double d = q.DistanceTo(p);
                                if (d >= bestD || d > maxDist) continue;
                                if (accept != null && !accept(k, q)) continue;
                                bestD = d; best = q; bestT = t; found = k;
                            }
                        }
                if (found >= 0 && bestD <= r * c - 0.5 * c) break;
            }
            return found;
        }

        private static void Evaluate(MaterialSample sm, Segments s, double[] layerTimes, double reach, MaterialFieldOptions o, double[,] stiff)
        {
            Point3d p = sm.P;
            int k = Nearest(s, p, reach, null, out Point3d c, out double t, out double dist);
            if (k < 0) return;

            Vector3d e1 = s.Dir[k], e3 = s.E3[k];
            e3 = Unit(e3 - e1 * (e3 * e1));
            if (e3.IsZero) e3 = s.E3[k];
            Vector3d e2 = Unit(Vector3d.CrossProduct(e3, e1));
            sm.E1 = e1; sm.E2 = e2; sm.E3 = e3;
            int L = s.Layer[k];
            sm.Layer = L; sm.Bead = s.Bead[k]; sm.DistToBead = dist;
            sm.LayerTime = L < layerTimes.Length ? layerTimes[L] : 0;
            double h = s.H[k], w = s.W[k];
            double tHere = s.T0[k] + (s.T1[k] - s.T0[k]) * t;

            // layer interfaces: the beads directly below and above
            Vector3d rel = p - c;
            double up = rel * e3, side = rel * e2;
            double best = double.MaxValue, gap = -1;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int other = L + dir;
                Point3d probe = c + e3 * (dir * h);
                int j = Nearest(s, probe, 0.5 * h + 0.5 * w, (kk, q) => s.Layer[kk] == other, out Point3d q2, out double t2, out double d2);
                if (j < 0) continue;
                double tOther = s.T0[j] + (s.T1[j] - s.T0[j]) * t2;
                double dInt = Math.Max(0, 0.5 * h - dir * up);
                if (dInt < best) { best = dInt; gap = Math.Abs(tHere - tOther); }
            }
            if (best < double.MaxValue)
            {
                sm.DistLayerInterface = best;
                sm.InterfaceTime = gap;
                double closeness = 1 - Math.Min(1, best / (0.5 * h));
                double tau = o.CoolingTime > 0 ? o.CoolingTime : 60;
                sm.Weakness = closeness * (1 - Math.Exp(-gap / tau));
            }

            // bead interfaces: the neighbour beads beside this one in the same layer
            double nearSide = double.MaxValue, spacing = double.MaxValue;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                int sideSign = sgn;
                Point3d probe = c + e2 * (sideSign * w);
                int j = Nearest(s, probe, 1.5 * w, (kk, q) =>
                {
                    if (s.Layer[kk] != L) return false;
                    Vector3d rr = q - c;
                    double lat = rr * e2;
                    return lat * sideSign > 0.25 * w && Math.Abs(rr * e3) < 0.5 * h && Math.Abs(s.Dir[kk] * e1) > 0.7 && Math.Abs(rr * e1) < w;
                }, out Point3d q3, out double t3, out double d3);
                if (j < 0) continue;
                double lat2 = Math.Abs((q3 - c) * e2);
                spacing = Math.Min(spacing, lat2);
                double dInt = Math.Abs(0.5 * lat2 - sideSign * side);
                nearSide = Math.Min(nearSide, dInt);
            }
            if (spacing < double.MaxValue)
            {
                sm.BeadSpacing = spacing;
                sm.DistBeadInterface = nearSide;
                sm.Overlap = Math.Max(0, (w - spacing) / w);
            }

            if (stiff != null) sm.C = OrthotropicMaterial.Upper(OrthotropicMaterial.Rotate(stiff, e1, e2, e3));
        }

        /// <summary>Blue (sound) - green - yellow - red (weak).</summary>
        public static System.Drawing.Color Ramp(double v)
        {
            v = Math.Max(0, Math.Min(1, v));
            double[][] stops = { new double[] { 40, 90, 200 }, new double[] { 60, 180, 120 }, new double[] { 245, 210, 60 }, new double[] { 215, 40, 40 } };
            double x = v * 3; int i = Math.Min(2, (int)x); double f = x - i;
            return System.Drawing.Color.FromArgb(
                (int)(stops[i][0] + (stops[i + 1][0] - stops[i][0]) * f),
                (int)(stops[i][1] + (stops[i + 1][1] - stops[i][1]) * f),
                (int)(stops[i][2] + (stops[i + 1][2] - stops[i][2]) * f));
        }

        private static Mesh BuildMesh(List<MaterialSample> samples, List<int[]> grids, List<int[]> nodeIndex, double maxW)
        {
            var mesh = new Mesh();
            for (int f = 0; f < grids.Count; f++)
            {
                int nu = grids[f][0], nv = grids[f][1];
                var idx = nodeIndex[f];
                var vert = new int[idx.Length];
                for (int n = 0; n < idx.Length; n++)
                {
                    vert[n] = -1;
                    if (idx[n] < 0) continue;
                    var s = samples[idx[n]];
                    vert[n] = mesh.Vertices.Add(s.P);
                    mesh.VertexColors.Add(s.Layer < 0 ? System.Drawing.Color.Gray : Ramp(maxW > 1e-12 ? s.Weakness / maxW : 0));
                }
                for (int j = 0; j < nv; j++)
                    for (int i = 0; i < nu; i++)
                    {
                        int a = vert[j * (nu + 1) + i], b = vert[j * (nu + 1) + i + 1], c = vert[(j + 1) * (nu + 1) + i + 1], d = vert[(j + 1) * (nu + 1) + i];
                        if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                        mesh.Faces.AddFace(a, b, c, d);
                    }
            }
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        // ------------------------------------------------------------------ export

        private static string F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "-1" : v.ToString("0.######", CultureInfo.InvariantCulture);
        private static string V(Vector3d v) => F(v.X) + "," + F(v.Y) + "," + F(v.Z);

        public static readonly string[] Columns =
        {
            "u", "v", "face", "x", "y", "z", "e1x", "e1y", "e1z", "e2x", "e2y", "e2z", "e3x", "e3y", "e3z",
            "layer", "bead", "dist_layer_interface", "dist_bead_interface", "bead_spacing", "overlap",
            "layer_time", "interface_time", "weakness", "temperature", "dist_to_bead"
        };

        /// <summary>One header line and one line per sample; -1 = not available.</summary>
        public static List<string> Csv(MaterialFieldResult r)
        {
            bool c = r.Samples.Any(s => s.C != null);
            var lines = new List<string>(r.Samples.Count + 1)
            {
                string.Join(",", Columns) + (c ? "," + string.Join(",", OrthotropicMaterial.UpperNames) : "")
            };
            var sb = new StringBuilder();
            foreach (var s in r.Samples)
            {
                sb.Clear();
                sb.Append(F(s.U)).Append(',').Append(F(s.V)).Append(',').Append(s.Face).Append(',')
                  .Append(F(s.P.X)).Append(',').Append(F(s.P.Y)).Append(',').Append(F(s.P.Z)).Append(',')
                  .Append(V(s.E1)).Append(',').Append(V(s.E2)).Append(',').Append(V(s.E3)).Append(',')
                  .Append(s.Layer).Append(',').Append(s.Bead).Append(',')
                  .Append(F(s.DistLayerInterface)).Append(',').Append(F(s.DistBeadInterface)).Append(',')
                  .Append(F(s.BeadSpacing)).Append(',').Append(F(s.Overlap)).Append(',')
                  .Append(F(s.LayerTime)).Append(',').Append(F(s.InterfaceTime)).Append(',')
                  .Append(F(s.Weakness)).Append(',').Append(F(s.Temperature)).Append(',').Append(F(s.DistToBead));
                if (c)
                    for (int i = 0; i < 21; i++) sb.Append(',').Append(s.C != null ? F(s.C[i]) : "0");
                lines.Add(sb.ToString());
            }
            return lines;
        }

        public static string Json(MaterialFieldResult r)
        {
            var m = r.Material ?? new OrthotropicMaterial();
            var st = r.Settings ?? new PrintSettings();
            var sb = new StringBuilder();
            sb.Append("{\n  \"format\": \"LARGER.slicer material field\",\n  \"version\": 1,\n");
            sb.Append("  \"units\": { \"length\": \"mm\", \"time\": \"s\", \"modulus\": \"MPa\" },\n");
            sb.Append("  \"frame\": \"e1 along the bead, e2 = e3 x e1 across the bead in the layer, e3 stacking direction\",\n");
            sb.Append("  \"voigt\": \"11, 22, 33, 23, 13, 12; engineering shear strains; C upper triangle row by row\",\n");
            sb.Append("  \"missing\": -1,\n");
            sb.Append($"  \"print\": {{ \"bead_width\": {F(st.BeadWidth)}, \"layer_height\": {F(st.LayerHeight)}, \"overlap\": {F(st.Overlap)}, \"speed_mm_min\": {F(st.Speed)} }},\n");
            sb.Append($"  \"material\": {{ \"E1\": {F(m.E1)}, \"E2\": {F(m.E2)}, \"E3\": {F(m.E3)}, \"G12\": {F(m.G12)}, \"G13\": {F(m.G13)}, \"G23\": {F(m.G23)}, \"nu12\": {F(m.Nu12)}, \"nu13\": {F(m.Nu13)}, \"nu23\": {F(m.Nu23)}, \"placeholder\": true }},\n");
            sb.Append("  \"layer_times\": [").Append(string.Join(", ", r.LayerTimes.Select(F))).Append("],\n");
            sb.Append("  \"columns\": [").Append(string.Join(", ", Columns.Select(x => "\"" + x + "\""))).Append(r.Samples.Any(s => s.C != null) ? ", \"C\"" : "").Append("],\n");
            sb.Append("  \"samples\": [\n");
            for (int i = 0; i < r.Samples.Count; i++)
            {
                var s = r.Samples[i];
                sb.Append("    [").Append(F(s.U)).Append(", ").Append(F(s.V)).Append(", ").Append(s.Face).Append(", ")
                  .Append(F(s.P.X)).Append(", ").Append(F(s.P.Y)).Append(", ").Append(F(s.P.Z)).Append(", ")
                  .Append(V(s.E1).Replace(",", ", ")).Append(", ").Append(V(s.E2).Replace(",", ", ")).Append(", ").Append(V(s.E3).Replace(",", ", ")).Append(", ")
                  .Append(s.Layer).Append(", ").Append(s.Bead).Append(", ")
                  .Append(F(s.DistLayerInterface)).Append(", ").Append(F(s.DistBeadInterface)).Append(", ")
                  .Append(F(s.BeadSpacing)).Append(", ").Append(F(s.Overlap)).Append(", ")
                  .Append(F(s.LayerTime)).Append(", ").Append(F(s.InterfaceTime)).Append(", ")
                  .Append(F(s.Weakness)).Append(", ").Append(F(s.Temperature)).Append(", ").Append(F(s.DistToBead));
                if (s.C != null) sb.Append(", [").Append(string.Join(", ", s.C.Select(F))).Append("]");
                sb.Append(i + 1 < r.Samples.Count ? "],\n" : "]\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>Legacy ASCII VTK point cloud (POLYDATA with vertices) for ParaView.</summary>
        public static List<string> Vtk(MaterialFieldResult r)
        {
            int n = r.Samples.Count;
            var l = new List<string>(n * 12 + 40)
            {
                "# vtk DataFile Version 3.0",
                "LARGER.slicer material field (mm, s, MPa; -1 = not available)",
                "ASCII",
                "DATASET POLYDATA",
                $"POINTS {n} double"
            };
            foreach (var s in r.Samples) l.Add($"{F(s.P.X)} {F(s.P.Y)} {F(s.P.Z)}");
            l.Add($"VERTICES {n} {2 * n}");
            for (int i = 0; i < n; i++) l.Add("1 " + i);
            l.Add($"POINT_DATA {n}");
            void Vec(string name, Func<MaterialSample, Vector3d> f)
            {
                l.Add($"VECTORS {name} double");
                foreach (var s in r.Samples) { var v = f(s); l.Add($"{F(v.X)} {F(v.Y)} {F(v.Z)}"); }
            }
            void Sca(string name, string type, Func<MaterialSample, string> f)
            {
                l.Add($"SCALARS {name} {type} 1");
                l.Add("LOOKUP_TABLE default");
                foreach (var s in r.Samples) l.Add(f(s));
            }
            Vec("e1", s => s.E1); Vec("e2", s => s.E2); Vec("e3", s => s.E3);
            Sca("weakness", "double", s => F(s.Weakness));
            Sca("layer", "int", s => s.Layer.ToString(CultureInfo.InvariantCulture));
            Sca("bead", "int", s => s.Bead.ToString(CultureInfo.InvariantCulture));
            Sca("face", "int", s => s.Face.ToString(CultureInfo.InvariantCulture));
            Sca("u", "double", s => F(s.U));
            Sca("v", "double", s => F(s.V));
            Sca("dist_layer_interface", "double", s => F(s.DistLayerInterface));
            Sca("dist_bead_interface", "double", s => F(s.DistBeadInterface));
            Sca("overlap", "double", s => F(s.Overlap));
            Sca("layer_time", "double", s => F(s.LayerTime));
            Sca("interface_time", "double", s => F(s.InterfaceTime));
            if (r.Samples.Any(s => s.C != null))
            {
                l.Add("FIELD stiffness 1");
                l.Add($"C 21 {n} double");
                foreach (var s in r.Samples) l.Add(string.Join(" ", (s.C ?? new double[21]).Select(F)));
            }
            return l;
        }

        /// <summary>Writes base.csv, base.json and/or base.vtk (format 0 = all, 1 = CSV, 2 = JSON, 3 = VTK). Returns the files written.</summary>
        public static List<string> Write(MaterialFieldResult r, string file, int format)
        {
            string path = OutputFile.Resolve(file, OutputFile.Name("MAT", "Material Field", null, ".csv"));
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) throw new System.IO.DirectoryNotFoundException($"The folder {dir} does not exist.");
            string stem = System.IO.Path.Combine(dir ?? "", System.IO.Path.GetFileNameWithoutExtension(path));
            var written = new List<string>();
            if (format == 0 || format == 1) { System.IO.File.WriteAllLines(stem + ".csv", Csv(r)); written.Add(stem + ".csv"); }
            if (format == 0 || format == 2) { System.IO.File.WriteAllText(stem + ".json", Json(r)); written.Add(stem + ".json"); }
            if (format == 0 || format == 3) { System.IO.File.WriteAllLines(stem + ".vtk", Vtk(r)); written.Add(stem + ".vtk"); }
            return written;
        }

        public static readonly string[] Formats = { "CSV, JSON and VTK", "CSV", "JSON", "VTK (ParaView)" };
    }
}
