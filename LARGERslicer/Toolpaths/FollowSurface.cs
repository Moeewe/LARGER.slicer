using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// "Follow the surface": the way up the part's own surface, from the ring it stands on to the
    /// crown, as a value u on every vertex of the meshed part (0 on the bottom ring, 1 at the crown).
    ///
    /// u = d_bottom / (d_bottom + d_crown), both shortest paths along the mesh edges (Dijkstra).
    /// Its iso-lines run round the part like the iso-curves of a surface: they dip under the chin
    /// and the nose, rise at the back of a head, and shrink to the crown, where they close it.
    /// This is how the printed UNIKAT HEAD V2 (Ginger One, 2024) was laid out: rings on the head's
    /// surface from the neck to the crown, one spiral, the nozzle vertical.
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

        /// <summary>How far the rings on the wall are pulled towards the level of the height (0 = the way
        /// along the surface alone, 1 = flat layers), fading out towards the crown.</summary>
        public readonly double ZBlend;

        public SurfaceField(Mesh part, double zBlend = 0.35)
        {
            ZBlend = Math.Max(0, Math.Min(1, zBlend));
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
            // Shortest paths along mesh edges zigzag, so their lengths depend a little on how the
            // triangles happen to lie: the rings would wave with the mesh. A few rounds of averaging
            // with the neighbours (bottom ring and crown held) take that out, as the printed head's
            // definition smoothed its rings (Smooth Polyline 0.5 x 30).
            // Not near the crown: there the rings are small circles round one point, averaging would pull
            // the values round the (held) crown down and squeeze the last rings onto it.
            var fixedV = new bool[nv];
            foreach (int i in bottom) fixedV[i] = true;
            foreach (int i in crown) fixedV[i] = true;
            var uw = new double[nv];
            for (int i = 0; i < nv; i++) uw[i] = _u[id[i]];
            for (int i = 0; i < nv; i++) if (uw[i] > 0.85) fixedV[i] = true;
            var next = new double[nv];
            for (int round = 0; round < 40; round++)
            {
                for (int i = 0; i < nv; i++)
                {
                    if (id[i] != i || fixedV[i] || adj[i].Count == 0) { next[i] = uw[i]; continue; }
                    double sum = 0;
                    foreach (var (j, _) in adj[i]) sum += uw[j];
                    next[i] = 0.5 * uw[i] + 0.5 * sum / adj[i].Count;
                }
                var tmp = uw; uw = next; next = tmp;
            }
            // On the wall the rings are pulled a third of the way towards the level of the height, fading
            // out towards the crown (where the way along the surface alone spaces them evenly): the rings
            // still dip under chin, nose and ears, but less deep, as the iso-curves of the printed head
            // did (and the stand-alone app does the same). Less dip = less of the nozzle grazing the
            // ring beside it (Non-Planar Check).
            double zTop = v[top].Z, zr = Math.Max(1e-9, zTop - zmin);
            for (int i = 0; i < nv; i++)
            {
                double u = uw[id[i]];
                double b = ZBlend * (1.0 - Math.Max(0.0, Math.Min(1.0, (u - 0.6) / 0.3)));
                _u[i] = u * (1 - b) + (v[i].Z - zmin) / zr * b;
            }
            var md = bottom.Select(i => dt[i]).Where(d => d < double.MaxValue).ToList();
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
    /// The bead height law of the printed UNIKAT HEAD V2 (owner's definition "head test unikat.gh",
    /// confirmed on all 18 041 points of its G-code to 0.0006 E/mm):
    ///
    ///   h = distance to the closest point of the lap below, a = angle of that line to the horizontal
    ///   t = h (1 - (h/w)(1 - sin a)((w/h) - cos a))  =  dz + (h - dz) dxy / w
    ///
    /// Stacked straight (the lap below right underneath): t = the vertical gap. Where the lap below
    /// lies to the side (overhang, crown) only the share dxy / w of the slant distance counts, so a
    /// bead that leans on the one below is not overfilled. E = factor x w x t x length.
    /// </summary>
    public static class BeadLaw
    {
        public static double Height(Point3d p, Point3d q, double w)
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
            double w = tp.Settings.BeadWidth, hNom = tp.Settings.LayerHeight;
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
                double t1 = Height(p, q, w);
                t1 = Math.Max(0.01, Math.Min(3.0 * hNom, t1)); // never exactly 0: a move with E 0 reads as a stop
                heights[i] = t1; count++;
                if (t1 < low) low = t1; if (t1 > high) high = t1;
            }
            for (int i = 0; i < n; i++) tp.Heights[i] = heights[i];
            if (count == 0) low = 0;
        }
    }
}
