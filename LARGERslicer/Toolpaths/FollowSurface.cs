using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// "Follow the surface": the way up the part's own surface, from the ring it stands on to the
    /// crown, as a value u on every vertex of the meshed part (0 on the bottom ring, 1 at the crown).
    /// The laps lie at u = k / N, N = mean way bottom ring - crown / layer height.
    ///
    /// On the wall u = d_bottom / M: the shortest way along the surface from the bottom ring (Dijkstra
    /// on the mesh), divided by the mean way M from the bottom ring to the crown. Its iso-lines lie
    /// exactly one layer height apart along the surface everywhere, so every lap stands on the one
    /// below: they dip deep under the chin and the nose, where the way up the surface is long for little
    /// height, and rise at the back of a head. Towards the crown (between 60 and 95 % of the way) u turns
    /// into the share d_bottom / (d_bottom + d_crown), which reaches 1 on the crown on every side, so the
    /// rings shrink to the crown and close it.
    ///
    /// This is how the printed UNIKAT HEAD V2 (Ginger One, 2024) was laid out: rings on the head's
    /// surface from the neck to the crown, one spiral, the nozzle vertical (its rings dipped up to about
    /// 17 mm per lap; these about 19). Up to 1.3.0 the share alone was used, smoothed over the mesh and
    /// pulled a third of the way towards flat layers: on the head the laps then dipped only about 10 mm,
    /// and lay up to twice the layer height apart under the chin and near the crown. (V2's extrusion law
    /// is not used any more: see BeadLaw.)
    /// </summary>
    public class SurfaceField
    {
        public bool Valid;
        public string Problem;
        /// <summary>Mean length of the way bottom ring - crown along the surface (mm).</summary>
        public double Meridian, MeridianMin, MeridianMax;
        public Point3d Crown;
        /// <summary>True when the part has a flat top face: the rings end at its rim.</summary>
        public bool FlatTop;
        private readonly Mesh _mesh;
        private readonly double[] _u;      // per mesh vertex (after welding)

        /// <summary>Share of the way bottom ring - crown where u turns from the way from the bottom ring
        /// (even laps on the wall) into the share of the way (rings that close the crown), and where it is complete.</summary>
        public const double CrownFrom = 0.6, CrownTo = 0.95;

        public SurfaceField(Mesh part)
        {
            if (part == null || part.Vertices.Count < 4) { Problem = "no part"; return; }
            var m = part.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.Vertices.CombineIdentical(true, true);
            m.Compact();
            _mesh = m;
            int nv = m.Vertices.Count;
            var v = new Point3d[nv];
            for (int i = 0; i < nv; i++) v[i] = m.Vertices[i];
            // also join vertices that lie on top of each other but were not combined (seams of Brep faces)
            var id = new int[nv];
            var key = new Dictionary<(long, long, long), int>();
            for (int i = 0; i < nv; i++)
            {
                var k = ((long)Math.Round(v[i].X * 1000), (long)Math.Round(v[i].Y * 1000), (long)Math.Round(v[i].Z * 1000));
                if (key.TryGetValue(k, out int j)) id[i] = j; else { key[k] = i; id[i] = i; }
            }
            var adj = new List<(int, double)>[nv];
            for (int i = 0; i < nv; i++) adj[i] = new List<(int, double)>(6);
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                int a = id[fc.A], b = id[fc.B], c = id[fc.C];
                Link(adj, v, a, b); Link(adj, v, b, c); Link(adj, v, c, a);
            }
            double zmin = double.MaxValue;
            int top = -1;
            for (int i = 0; i < nv; i++) { if (v[i].Z < zmin) zmin = v[i].Z; if (top < 0 || v[i].Z > v[top].Z) top = i; }
            var bottom = new List<int>();
            for (int i = 0; i < nv; i++) if (id[i] == i && v[i].Z < zmin + 1e-3) bottom.Add(i);
            if (bottom.Count < 3) { Problem = "the part needs a flat bottom to stand on"; return; }
            // the ring the wall stands on: where the surface leaves the bottom. The points inside a finely meshed
            // bottom face are further from the crown, and would count as more laps than the wall has room for
            var onBottom = new HashSet<int>(bottom);
            var ring = bottom.Where(i => adj[i].Count == 0 || adj[i].Any(e => !onBottom.Contains(e.Item1))).ToList();
            if (ring.Count < 3) ring = bottom;
            top = id[top];
            Crown = v[top];
            // the crown: the highest point - or, on a part with a flat top (a vase, a box), the whole top face,
            // so the rings end at its rim instead of running across it to one corner
            double zTopV = v[top].Z;
            var crown = new List<int>();
            for (int i = 0; i < nv; i++) if (id[i] == i && v[i].Z > zTopV - 1e-3) crown.Add(i);
            if (crown.Count < 3) crown = new List<int> { top };
            else FlatTop = true;
            double[] db = Dijkstra(adj, bottom), dt = Dijkstra(adj, crown);
            _u = new double[nv];
            for (int i = 0; i < nv; i++)
            {
                double a = db[id[i]], b = dt[id[i]];
                _u[i] = a < double.MaxValue && b < double.MaxValue && a + b > 1e-12 ? a / (a + b) : 0;
            }
            // u: on the wall the way from the bottom ring over the mean way to the crown (laps one layer
            // height apart along the surface everywhere); towards the crown the share of the way, which
            // reaches 1 on the crown on every side (smooth step between CrownFrom and CrownTo of the share)
            var md0 = ring.Select(i => dt[i]).Where(d => d < double.MaxValue).ToList();
            double mean = md0.Count > 0 ? md0.Average() : 0;
            for (int i = 0; i < nv; i++)
            {
                double a = db[id[i]], r = _u[i];
                if (a >= double.MaxValue || mean <= 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, (r - CrownFrom) / (CrownTo - CrownFrom)));
                double w = t * t * (3 - 2 * t);
                _u[i] = (1 - w) * (a / mean) + w * r;
            }
            var md = ring.Select(i => dt[i]).Where(d => d < double.MaxValue).ToList();
            if (md.Count == 0) { Problem = "the bottom and the crown are not connected by the surface"; return; }
            Meridian = md.Average(); MeridianMin = md.Min(); MeridianMax = md.Max();
            Valid = true;
        }

        private static void Link(List<(int, double)>[] adj, Point3d[] v, int a, int b)
        {
            if (a == b) return;
            double d = v[a].DistanceTo(v[b]);
            adj[a].Add((b, d)); adj[b].Add((a, d));
        }

        private static double[] Dijkstra(List<(int, double)>[] adj, IEnumerable<int> sources)
        {
            var d = new double[adj.Length];
            for (int i = 0; i < d.Length; i++) d[i] = double.MaxValue;
            var heap = new SortedSet<(double, int)>();
            foreach (int s in sources) { d[s] = 0; heap.Add((0, s)); }
            while (heap.Count > 0)
            {
                var (du, u) = heap.Min; heap.Remove(heap.Min);
                if (du > d[u]) continue;
                foreach (var (w, l) in adj[u])
                {
                    double nd = du + l;
                    if (nd < d[w]) { if (d[w] < double.MaxValue) heap.Remove((d[w], w)); d[w] = nd; heap.Add((nd, w)); }
                }
            }
            return d;
        }

        /// <summary>u at a point on (or near) the surface: linear on the closest triangle.</summary>
        public double At(Point3d p)
        {
            if (!Valid) return 0;
            MeshPoint mp = _mesh.ClosestMeshPoint(p, 0);
            if (mp == null || mp.FaceIndex < 0) return 0;
            var f = _mesh.Faces[mp.FaceIndex];
            double[] t = mp.T;
            return _u[f.A] * t[0] + _u[f.B] * t[1] + _u[f.C] * t[2] + (f.IsQuad ? _u[f.D] * t[3] : 0);
        }
    }

    /// <summary>
    /// The bead height law for non-planar laps printed with a vertical nozzle (the shell law, 1.3.1).
    ///
    /// A single-bead shell of horizontal thickness w (the bead width) gains, per lap, the cross-section
    /// w x dz in any vertical section across the wall, dz = how much higher the lap lies than the lap
    /// below: a parallelogram with horizontal sides w and height dz. How far the lap is offset sideways
    /// (dxy, an overhang or a wall leaning in) does not change that area: a sideways offset moves the
    /// material, it does not add any. Where the surface turns nearly flat (crown, the shelf under a
    /// chin) the laps step mostly sideways and the shell becomes a skin: per mm of path it then needs
    /// the sideways step times the skin thickness. Together:
    ///
    ///   A = max(w dz, dxy t_skin)        t = A / w = max(dz, dxy t_skin / w)        E = factor x w x t x length
    ///
    /// with (dz, dxy) = vertical and horizontal part of the line to the closest point of the lap below,
    /// and t_skin = Skin Thickness (default the layer height, never below 0.4 x layer height). The
    /// skin term takes over where dz / dxy &lt; t_skin / w (surface flatter than about 24 degrees for a
    /// 4 mm bead and 1.75 mm layers).
    ///
    /// The law of the printed UNIKAT HEAD V2 (2024), used up to 1.3.0, added the share dxy / w of the
    /// slant distance on top of dz: t = dz + (h - dz) dxy / w (h = 3D distance). That term is extra
    /// material: on V2 3.4 % over the whole head, 4-5 % per lap under the chin, about 12 % on a 45 degree
    /// slope and 36-57 % per lap on the shoulder towards the crown (surface 20-35 degrees). It is kept
    /// as HeightV2 so Non-Planar Check can recognise old files.
    /// </summary>
    public static class BeadLaw
    {
        /// <summary>Bead height by the shell law: max(dz, dxy x skin / w) to the closest point q of the lap below.</summary>
        public static double Height(Point3d p, Point3d q, double w, double skin)
        {
            double dz = Math.Max(0, p.Z - q.Z);
            double dxy = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            return Math.Max(dz, dxy * skin / Math.Max(1e-9, w));
        }

        /// <summary>The law of the printed UNIKAT HEAD V2 (up to 1.3.0): t = dz + (h - dz) dxy / w. It over-extrudes
        /// leaning laps (see the class notes); only for recognising old files.</summary>
        public static double HeightV2(Point3d p, Point3d q, double w)
        {
            double dz = p.Z - q.Z;
            double dxy = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            double h = p.DistanceTo(q);
            if (dz < 0) dz = 0;
            return dz + (h - dz) * Math.Min(1.0, dxy / Math.Max(1e-9, w));
        }

        /// <summary>
        /// Sets the bead height of every printing point after the first lap from the lap below it in
        /// the path itself: the closest point among the points printed about one lap earlier (between
        /// two laps and three quarters of a lap back along the path). The first lap and the first climbing
        /// lap (its ramp) keep their heights.
        /// Returns the smallest and largest height set.
        /// </summary>
        public static void Apply(Toolpath tp, out double low, out double high, out int count)
        {
            low = double.MaxValue; high = 0; count = 0;
            if (tp == null || tp.Count < 3) return;
            double w = tp.Settings.BeadWidth, hNom = tp.Settings.LayerHeight, skin = tp.Settings.EffectiveSkin;
            int n = tp.Count;
            var s = new double[n];
            for (int i = 1; i < n; i++) s[i] = s[i - 1] + tp.Points[i].DistanceTo(tp.Points[i - 1]);
            // length of every lap (path layer index), for the search window
            var lapLen = new Dictionary<int, double>();
            for (int i = 1; i < n; i++)
            {
                lapLen.TryGetValue(tp.Layers[i], out double l);
                lapLen[tp.Layers[i]] = l + s[i] - s[i - 1];
            }
            int first = -1;
            for (int i = 0; i < n; i++) if (tp.Flows[i] > 0) { first = i; break; }
            if (first < 0) return;
            int firstLayer = tp.Layers[first];
            var heights = new double[n];
            for (int i = 0; i < n; i++) heights[i] = tp.Heights[i];
            int lo = 0;
            for (int i = first + 1; i < n; i++)
            {
                // the first lap lies on the bed, and the first climbing lap rises from it with its own ramp (from
                // nothing over the seam gap of the first lap, as on the printed head): both keep their heights
                if (tp.Flows[i] <= 0 || tp.Layers[i] <= firstLayer + 1) continue;
                // the pass that closes the crown (role Seam) runs over the open middle: it keeps its own height
                if (tp.Roles[i] == PathRole.Seam) continue;
                // the lap below sets the window (the last lap and its closing pass share one index)
                if (!lapLen.TryGetValue(tp.Layers[i] - 1, out double lap)) lap = lapLen[tp.Layers[i]];
                lap = Math.Max(3 * w, lap);
                double sFrom = s[i] - 2.0 * lap, sTo = s[i] - 0.75 * lap;
                if (sTo <= s[first]) continue;
                while (lo < i && s[lo] < sFrom) lo++;
                Point3d p = tp.Points[i];
                double best = double.MaxValue; Point3d q = Point3d.Unset;
                for (int j = Math.Max(lo, first + 1); j < i && s[j] <= sTo; j++)
                {
                    if (tp.Flows[j] <= 0) continue;
                    Point3d a = tp.Points[j - 1], b = tp.Points[j];
                    Vector3d ab = b - a;
                    double L2 = ab.SquareLength;
                    double t = L2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / L2));
                    Point3d c = a + ab * t;
                    double d = c.DistanceToSquared(p);
                    if (d < best) { best = d; q = c; }
                }
                if (!q.IsValid) continue;
                double t1 = Height(p, q, w, skin);
                t1 = Math.Max(0.01, Math.Min(3.0 * hNom, t1)); // never exactly 0: a move with E 0 reads as a stop
                heights[i] = t1; count++;
                if (t1 < low) low = t1; if (t1 > high) high = t1;
            }
            for (int i = 0; i < n; i++) tp.Heights[i] = heights[i];
            if (count == 0) low = 0;
        }
    }
}
