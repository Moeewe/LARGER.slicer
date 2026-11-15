using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// The triangles of a collision body in its own coordinates, in a bounding-volume tree, for
    /// closest-point queries in plain managed math: thread-safe (read only once built) and much
    /// faster than a call into Rhino per sample point. Every triangle keeps the face normal Rhino
    /// gave its face, so "inside" is decided exactly as before (the gap vector against the normal
    /// of the closest face).
    /// </summary>
    internal sealed class TriTree
    {
        // triangle corners and face normals, xyz in a row
        private readonly double[] _a, _b, _c, _n;
        // nodes: box (min xyz, max xyz); a leaf has Count > 0 and its triangles at Start..Start+Count
        private readonly double[] _box;
        private readonly int[] _left, _right, _start, _count;
        [ThreadStatic] private static int[] _stack;
        private int _nodes;
        public readonly int Triangles;

        private const int Leaf = 4;

        public TriTree(Mesh m)
        {
            var tris = new List<int[]>();
            var normals = new List<Vector3d>();
            var v = m.Vertices;
            bool haveNormals = m.FaceNormals.Count == m.Faces.Count;
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                Vector3d n = haveNormals ? (Vector3d)m.FaceNormals[f] : Vector3d.Zero;
                tris.Add(new[] { fc.A, fc.B, fc.C }); normals.Add(n);
                if (fc.IsQuad) { tris.Add(new[] { fc.A, fc.C, fc.D }); normals.Add(n); }
            }
            int t = tris.Count; Triangles = t;
            var pts = new Point3d[v.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = v[i];
            var a = new double[3 * t]; var b = new double[3 * t]; var c = new double[3 * t]; var nn = new double[3 * t];
            var cen = new double[3 * t];
            for (int i = 0; i < t; i++)
            {
                Point3d p = pts[tris[i][0]], q = pts[tris[i][1]], r = pts[tris[i][2]];
                a[3 * i] = p.X; a[3 * i + 1] = p.Y; a[3 * i + 2] = p.Z;
                b[3 * i] = q.X; b[3 * i + 1] = q.Y; b[3 * i + 2] = q.Z;
                c[3 * i] = r.X; c[3 * i + 1] = r.Y; c[3 * i + 2] = r.Z;
                nn[3 * i] = normals[i].X; nn[3 * i + 1] = normals[i].Y; nn[3 * i + 2] = normals[i].Z;
                for (int k = 0; k < 3; k++) cen[3 * i + k] = (a[3 * i + k] + b[3 * i + k] + c[3 * i + k]) / 3;
            }
            // build: split the longest side of the centroid box at the median
            var order = new int[t]; for (int i = 0; i < t; i++) order[i] = i;
            int cap = 2 * t + 1;
            _box = new double[6 * cap]; _left = new int[cap]; _right = new int[cap]; _start = new int[cap]; _count = new int[cap];
            if (t > 0) BuildNode(order, 0, t, a, b, c, cen);
            // the triangles in tree order, so a leaf reads one block
            _a = new double[3 * t]; _b = new double[3 * t]; _c = new double[3 * t]; _n = new double[3 * t];
            for (int i = 0; i < t; i++)
                for (int k = 0; k < 3; k++)
                {
                    _a[3 * i + k] = a[3 * order[i] + k]; _b[3 * i + k] = b[3 * order[i] + k];
                    _c[3 * i + k] = c[3 * order[i] + k]; _n[3 * i + k] = nn[3 * order[i] + k];
                }
        }

        private int NewNode()
        {
            if (_nodes >= _left.Length) throw new InvalidOperationException("TriTree: too many nodes");
            return _nodes++;
        }

        private int BuildNode(int[] order, int lo, int hi, double[] a, double[] b, double[] c, double[] cen)
        {
            int node = NewNode();
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            double cx0 = double.MaxValue, cy0 = double.MaxValue, cz0 = double.MaxValue, cx1 = double.MinValue, cy1 = double.MinValue, cz1 = double.MinValue;
            for (int i = lo; i < hi; i++)
            {
                int t = order[i];
                for (int s = 0; s < 3; s++)
                {
                    var arr = s == 0 ? a : s == 1 ? b : c;
                    double x = arr[3 * t], y = arr[3 * t + 1], z = arr[3 * t + 2];
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; if (z < z0) z0 = z; if (z > z1) z1 = z;
                }
                double ux = cen[3 * t], uy = cen[3 * t + 1], uz = cen[3 * t + 2];
                if (ux < cx0) cx0 = ux; if (ux > cx1) cx1 = ux; if (uy < cy0) cy0 = uy; if (uy > cy1) cy1 = uy; if (uz < cz0) cz0 = uz; if (uz > cz1) cz1 = uz;
            }
            int o = 6 * node;
            _box[o] = x0; _box[o + 1] = y0; _box[o + 2] = z0; _box[o + 3] = x1; _box[o + 4] = y1; _box[o + 5] = z1;
            if (hi - lo <= Leaf) { _start[node] = lo; _count[node] = hi - lo; return node; }
            double ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
            int axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
            int mid = (lo + hi) / 2;
            Array.Sort(order, lo, hi - lo, Comparer<int>.Create((p, q) => cen[3 * p + axis].CompareTo(cen[3 * q + axis])));
            _count[node] = 0;
            _left[node] = BuildNode(order, lo, mid, a, b, c, cen);
            _right[node] = BuildNode(order, mid, hi, a, b, c, cen);
            return node;
        }

        private double BoxDist2(int node, double x, double y, double z)
        {
            int o = 6 * node;
            double dx = x < _box[o] ? _box[o] - x : x > _box[o + 3] ? x - _box[o + 3] : 0;
            double dy = y < _box[o + 1] ? _box[o + 1] - y : y > _box[o + 4] ? y - _box[o + 4] : 0;
            double dz = z < _box[o + 2] ? _box[o + 2] - z : z > _box[o + 5] ? z - _box[o + 5] : 0;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// The closest point of the triangles to p, if it lies within maxDist (+infinity = any).
        /// face: the triangle (for Normal). Returns false if none is that close.
        /// </summary>
        public bool Closest(Point3d p, double maxDist, out Point3d closest, out int face, out double dist)
        {
            closest = Point3d.Unset; face = -1; dist = double.PositiveInfinity;
            if (Triangles == 0) return false;
            double best = double.IsPositiveInfinity(maxDist) ? double.PositiveInfinity : maxDist * maxDist;
            double bx = 0, by = 0, bz = 0; int bf = -1;
            double x = p.X, y = p.Y, z = p.Z;
            var stack = _stack ?? (_stack = new int[128]);
            int sp = 0; stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (BoxDist2(node, x, y, z) > best) continue;
                int cnt = _count[node];
                if (cnt > 0)
                {
                    int s = _start[node];
                    for (int i = s; i < s + cnt; i++)
                    {
                        ClosestOnTriangle(i, x, y, z, out double qx, out double qy, out double qz);
                        double dx = x - qx, dy = y - qy, dz = z - qz, d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 < best || (d2 <= best && bf < 0)) { best = d2; bx = qx; by = qy; bz = qz; bf = i; }
                    }
                    continue;
                }
                int l = _left[node], r = _right[node];
                double dl = BoxDist2(l, x, y, z), dr = BoxDist2(r, x, y, z);
                if (sp + 2 > stack.Length) { var big = new int[stack.Length * 2]; Array.Copy(stack, big, stack.Length); _stack = stack = big; }
                // nearer child last, so it is taken first
                if (dl <= dr) { if (dr <= best) stack[sp++] = r; if (dl <= best) stack[sp++] = l; }
                else { if (dl <= best) stack[sp++] = l; if (dr <= best) stack[sp++] = r; }
            }
            if (bf < 0) return false;
            closest = new Point3d(bx, by, bz); face = bf; dist = Math.Sqrt(best);
            return true;
        }

        /// <summary>True if some triangle comes within r of p.</summary>
        public bool AnyWithin(Point3d p, double r)
        {
            if (Triangles == 0) return false;
            double r2 = r * r, x = p.X, y = p.Y, z = p.Z;
            var stack = _stack ?? (_stack = new int[128]);
            int sp = 0; stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (BoxDist2(node, x, y, z) > r2) continue;
                int cnt = _count[node];
                if (cnt > 0)
                {
                    int s = _start[node];
                    for (int i = s; i < s + cnt; i++)
                    {
                        ClosestOnTriangle(i, x, y, z, out double qx, out double qy, out double qz);
                        double dx = x - qx, dy = y - qy, dz = z - qz;
                        if (dx * dx + dy * dy + dz * dz <= r2) return true;
                    }
                    continue;
                }
                if (sp + 2 > stack.Length) { var big = new int[stack.Length * 2]; Array.Copy(stack, big, stack.Length); _stack = stack = big; }
                stack[sp++] = _right[node]; stack[sp++] = _left[node];
            }
            return false;
        }

        public Vector3d Normal(int face) => new Vector3d(_n[3 * face], _n[3 * face + 1], _n[3 * face + 2]);

        private Point3d Corner(int tri, int k)
        {
            var arr = k == 0 ? _a : k == 1 ? _b : _c; int o = 3 * tri;
            return new Point3d(arr[o], arr[o + 1], arr[o + 2]);
        }

        /// <summary>
        /// Do the surfaces of two bodies cross? a in its own coordinates, b moved into them by bToA.
        /// Returns the middle of the first crossing (a triangle of one through a triangle of the
        /// other, in a's coordinates) that accept takes (null = any). Triangles that only lie in one
        /// plane together do not count, as with Rhino's MeshMeshFast.
        /// </summary>
        public static bool Cross(TriTree a, TriTree b, Frame bToA, Func<Point3d, bool> accept, out Point3d where)
        {
            where = Point3d.Unset;
            if (a.Triangles == 0 || b.Triangles == 0) return false;
            var stack = new Stack<long>();
            stack.Push(0);
            var tb = new Point3d[3];
            while (stack.Count > 0)
            {
                long pr = stack.Pop(); int na = (int)(pr >> 32), nb = (int)(pr & 0xFFFFFFFF);
                // b's box seen in a's coordinates (the box around its turned corners)
                int ob = 6 * nb;
                double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    var c = bToA.Pt(new Point3d(b._box[ob + ((k & 1) == 0 ? 0 : 3)], b._box[ob + 1 + ((k & 2) == 0 ? 0 : 3)], b._box[ob + 2 + ((k & 4) == 0 ? 0 : 3)]));
                    if (c.X < x0) x0 = c.X; if (c.X > x1) x1 = c.X; if (c.Y < y0) y0 = c.Y; if (c.Y > y1) y1 = c.Y; if (c.Z < z0) z0 = c.Z; if (c.Z > z1) z1 = c.Z;
                }
                int oa = 6 * na;
                if (x1 < a._box[oa] || x0 > a._box[oa + 3] || y1 < a._box[oa + 1] || y0 > a._box[oa + 4] || z1 < a._box[oa + 2] || z0 > a._box[oa + 5]) continue;
                bool la = a._count[na] > 0, lb = b._count[nb] > 0;
                if (la && lb)
                {
                    int sa = a._start[na], sb = b._start[nb];
                    for (int j = sb; j < sb + b._count[nb]; j++)
                    {
                        for (int k = 0; k < 3; k++) tb[k] = bToA.Pt(b.Corner(j, k));
                        for (int i = sa; i < sa + a._count[na]; i++)
                        {
                            if (!TriTri(a.Corner(i, 0), a.Corner(i, 1), a.Corner(i, 2), tb[0], tb[1], tb[2], out var mid)) continue;
                            if (accept != null && !accept(mid)) continue;
                            where = mid; return true;
                        }
                    }
                    continue;
                }
                // open the bigger node (or the one that is not a leaf)
                double va = Volume(a, na), vb = Volume(b, nb);
                if (lb || (!la && va >= vb)) { stack.Push(((long)a._right[na] << 32) | (uint)nb); stack.Push(((long)a._left[na] << 32) | (uint)nb); }
                else { stack.Push(((long)na << 32) | (uint)b._right[nb]); stack.Push(((long)na << 32) | (uint)b._left[nb]); }
            }
            return false;
        }

        private static double Volume(TriTree t, int n) { int o = 6 * n; return (t._box[o + 3] - t._box[o]) + (t._box[o + 4] - t._box[o + 1]) + (t._box[o + 5] - t._box[o + 2]); }

        /// <summary>Two triangles cross: the points where the edges of each pass through the other; the middle of the extreme two.</summary>
        private static bool TriTri(Point3d a0, Point3d a1, Point3d a2, Point3d b0, Point3d b1, Point3d b2, out Point3d mid)
        {
            mid = Point3d.Unset;
            // quick rejection: all corners of one on one side of the other's plane
            Vector3d na = Vector3d.CrossProduct(a1 - a0, a2 - a0), nb = Vector3d.CrossProduct(b1 - b0, b2 - b0);
            double la = na.Length, lb = nb.Length;
            if (la < 1e-12 || lb < 1e-12) return false;
            double e = 1e-9;
            double db0 = (b0 - a0) * na / la, db1 = (b1 - a0) * na / la, db2 = (b2 - a0) * na / la;
            if ((db0 > e && db1 > e && db2 > e) || (db0 < -e && db1 < -e && db2 < -e)) return false;
            double da0 = (a0 - b0) * nb / lb, da1 = (a1 - b0) * nb / lb, da2 = (a2 - b0) * nb / lb;
            if ((da0 > e && da1 > e && da2 > e) || (da0 < -e && da1 < -e && da2 < -e)) return false;
            if (Math.Abs(db0) <= e && Math.Abs(db1) <= e && Math.Abs(db2) <= e) return false; // in one plane
            int n = 0; Point3d p0 = Point3d.Unset, p1 = Point3d.Unset;
            void Add(Point3d p) { if (n == 0) p0 = p; else if (n == 1 || p.DistanceToSquared(p0) > p1.DistanceToSquared(p0)) p1 = p; n++; }
            if (EdgeTri(a0, a1, b0, b1, b2, out var q)) Add(q);
            if (EdgeTri(a1, a2, b0, b1, b2, out q)) Add(q);
            if (EdgeTri(a2, a0, b0, b1, b2, out q)) Add(q);
            if (EdgeTri(b0, b1, a0, a1, a2, out q)) Add(q);
            if (EdgeTri(b1, b2, a0, a1, a2, out q)) Add(q);
            if (EdgeTri(b2, b0, a0, a1, a2, out q)) Add(q);
            if (n == 0) return false;
            mid = n == 1 ? p0 : new Point3d((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2, (p0.Z + p1.Z) / 2);
            return true;
        }

        /// <summary>Segment p-q through triangle a-b-c (Moeller-Trumbore), the point where it passes.</summary>
        private static bool EdgeTri(Point3d p, Point3d q, Point3d a, Point3d b, Point3d c, out Point3d hit)
        {
            hit = Point3d.Unset;
            Vector3d d = q - p, e1 = b - a, e2 = c - a;
            Vector3d h = Vector3d.CrossProduct(d, e2);
            double det = e1 * h;
            if (Math.Abs(det) < 1e-12) return false;
            double f = 1 / det;
            Vector3d s = p - a;
            double u = f * (s * h); if (u < 0 || u > 1) return false;
            Vector3d qv = Vector3d.CrossProduct(s, e1);
            double v = f * (d * qv); if (v < 0 || u + v > 1) return false;
            double t = f * (e2 * qv); if (t < 0 || t > 1) return false;
            hit = p + d * t; return true;
        }

        /// <summary>Closest point on triangle i (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private void ClosestOnTriangle(int i, double px, double py, double pz, out double qx, out double qy, out double qz)
        {
            int o = 3 * i;
            double ax = _a[o], ay = _a[o + 1], az = _a[o + 2], bx = _b[o], by = _b[o + 1], bz = _b[o + 2], cx = _c[o], cy = _c[o + 1], cz = _c[o + 2];
            double abx = bx - ax, aby = by - ay, abz = bz - az, acx = cx - ax, acy = cy - ay, acz = cz - az;
            double apx = px - ax, apy = py - ay, apz = pz - az;
            double d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
            if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; return; }
            double bpx = px - bx, bpy = py - by, bpz = pz - bz;
            double d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
            if (d3 >= 0 && d4 <= d3) { qx = bx; qy = by; qz = bz; return; }
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { double v = d1 / (d1 - d3); qx = ax + v * abx; qy = ay + v * aby; qz = az + v * abz; return; }
            double cpx = px - cx, cpy = py - cy, cpz = pz - cz;
            double d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
            if (d6 >= 0 && d5 <= d6) { qx = cx; qy = cy; qz = cz; return; }
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { double w = d2 / (d2 - d6); qx = ax + w * acx; qy = ay + w * acy; qz = az + w * acz; return; }
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                qx = bx + w * (cx - bx); qy = by + w * (cy - by); qz = bz + w * (cz - bz); return;
            }
            double den = 1 / (va + vb + vc), vv = vb * den, ww = vc * den;
            qx = ax + abx * vv + acx * ww; qy = ay + aby * vv + acy * ww; qz = az + abz * vv + acz * ww;
        }
    }

    /// <summary>
    /// The sample points of a body in small groups (cells of a grid in its own coordinates), each
    /// with a centre and a radius: a whole group is left out when no triangle of the other body comes
    /// within its radius plus the clearance.
    /// </summary>
    internal sealed class SampleGroups
    {
        public Point3d[] Centre;
        public double[] Radius;
        /// <summary>Group of every sample.</summary>
        public int[] Of;
        public int Count => Centre.Length;

        public SampleGroups(Point3d[] samples, BoundingBox box, double spacing)
        {
            int n = samples.Length;
            double diag = box.IsValid ? box.Diagonal.Length : 1;
            // about 30 to 60 samples a group, never smaller than a few sample spacings
            double cell = Math.Max(Math.Max(4 * spacing, 1e-3), diag / 24);
            var map = new Dictionary<long, int>();
            Of = new int[n];
            var sums = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                var p = samples[i];
                long key = ((long)((int)Math.Floor((p.X - box.Min.X) / cell) & 0x1FFFFF) << 42) | ((long)((int)Math.Floor((p.Y - box.Min.Y) / cell) & 0x1FFFFF) << 21) | (long)((int)Math.Floor((p.Z - box.Min.Z) / cell) & 0x1FFFFF);
                if (!map.TryGetValue(key, out int g)) { g = sums.Count; map[key] = g; sums.Add(new double[4]); }
                Of[i] = g; var s = sums[g]; s[0] += p.X; s[1] += p.Y; s[2] += p.Z; s[3]++;
            }
            Centre = new Point3d[sums.Count]; Radius = new double[sums.Count];
            for (int g = 0; g < sums.Count; g++) { var s = sums[g]; Centre[g] = new Point3d(s[0] / s[3], s[1] / s[3], s[2] / s[3]); }
            for (int i = 0; i < n; i++) { int g = Of[i]; double d = samples[i].DistanceTo(Centre[g]); if (d > Radius[g]) Radius[g] = d; }
            // a little more, for rounding
            for (int g = 0; g < Radius.Length; g++) Radius[g] = Radius[g] * (1 + 1e-9) + 1e-6;
        }
    }
}
