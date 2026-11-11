using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Turns any geometry (open or closed Breps, surfaces, extrusions, SubDs, meshes) into layers.
    /// Everything is meshed once and then cut with planes in parallel, which is far faster than
    /// contouring NURBS and works the same for every input type.
    /// </summary>
    public static class SliceEngine
    {
        /// <summary>Unwraps Grasshopper data and meshes it into one welded mesh.</summary>
        public static Mesh ToMesh(IEnumerable<object> geometry, double tolerance, List<string> log, double maxEdge = 0)
        {
            var mp = new MeshingParameters
            {
                Tolerance = tolerance,
                RelativeTolerance = 0,
                MinimumEdgeLength = 0.0001,
                MaximumEdgeLength = maxEdge,
                GridAspectRatio = 0,
                RefineGrid = true,
                JaggedSeams = false,
                SimplePlanes = true
            };

            var joined = new Mesh();
            int used = 0, skipped = 0;
            foreach (var item in geometry)
            {
                object o = item;
                if (o is IGH_Goo goo) o = goo.ScriptVariable();

                Brep brep = null;
                if (o is Mesh m) { joined.Append(m); used++; continue; }
                if (o is Brep b) brep = b;
                else if (o is Extrusion ex) brep = ex.ToBrep();
                else if (o is Surface srf) brep = srf.ToBrep();
                else if (o is SubD sd)
                {
                    var sm = Mesh.CreateFromSubD(sd, 3);
                    if (sm != null) { joined.Append(sm); used++; } else skipped++;
                    continue;
                }

                if (brep == null) { skipped++; continue; }
                var parts = Mesh.CreateFromBrep(brep, mp);
                if (parts == null || parts.Length == 0) { skipped++; continue; }
                foreach (var p in parts) joined.Append(p);
                used++;
            }

            if (skipped > 0) log?.Add($"{skipped} input(s) could not be meshed and were skipped.");
            if (used == 0 || joined.Vertices.Count == 0) return null;

            joined.Faces.ConvertQuadsToTriangles();
            joined.Vertices.CombineIdentical(true, true);
            joined.Weld(Math.PI);
            joined.Compact();
            return joined;
        }

        /// <summary>
        /// Cuts the mesh into layers perpendicular to the plane normal. The model is cut at the
        /// middle of each layer and the contour is lifted to the top of the layer, where the
        /// nozzle travels. Tilt the plane for angled (e.g. 45 degree) printing.
        /// Uses its own triangle/plane cutter: every triangle is assigned once to the layers it
        /// crosses, which is orders of magnitude faster than intersecting the mesh per plane.
        /// </summary>
        public static List<SliceLayer> Slice(Mesh mesh, Plane basePlane, double layerHeight, double minSpacing, List<string> log, Warp warp = null)
        {
            var layers = new List<SliceLayer>();
            if (mesh == null || layerHeight <= 0) return layers;

            Vector3d n = basePlane.Normal;
            n.Unitize();

            Point3d[] verts = mesh.Vertices.ToPoint3dArray();
            if (warp != null)
            {
                // Slice in flat space. The step is reduced so that the thickest real layer is the
                // nominal layer height; thinner places get less material through the path heights.
                warp.Prepare(mesh.GetBoundingBox(true));
                var scale = new double[verts.Length];
                Parallel.For(0, verts.Length, i => { verts[i] = warp.ToFlat(verts[i]); scale[i] = warp.Scale(verts[i]); });
                double maxScale = 1.0;
                foreach (double s in scale) if (s > maxScale) maxScale = s;
                maxScale = Math.Min(maxScale, 2.0);
                layerHeight /= maxScale;
                if (maxScale > 1.02) log?.Add($"Layers are up to {maxScale:0.00}x further apart on the outside of the bend; the step along the guide is {layerHeight:0.00} mm so no layer is higher than set.");
            }
            var dist = new double[verts.Length];
            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                double d = (verts[i] - basePlane.Origin) * n;
                dist[i] = d;
                if (d < min) min = d;
                if (d > max) max = d;
            }
            int count = (int)Math.Ceiling((max - min) / layerHeight - 1e-6);
            if (count < 1) return layers;

            // cut levels: middle of each layer, but never above the model
            var level = new double[count];
            // (a bent model is slightly uneven at its ends after mapping, so stay a little further inside)
            double margin = warp != null ? Math.Min(0.15, layerHeight * 0.2) : 1e-4;
            for (int i = 0; i < count; i++) level[i] = Math.Min(min + (i + 0.5) * layerHeight, max - margin);

            // triangles (quads are split) and the layers each one crosses
            var tris = new List<int>(mesh.Faces.Count * 3);
            foreach (MeshFace f in mesh.Faces)
            {
                tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                if (f.IsQuad) { tris.Add(f.A); tris.Add(f.C); tris.Add(f.D); }
            }
            var buckets = new List<int>[count];
            for (int i = 0; i < count; i++) buckets[i] = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                double a = dist[tris[t]], b = dist[tris[t + 1]], c = dist[tris[t + 2]];
                double lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c));
                int i0 = Math.Max(0, (int)Math.Floor((lo - min) / layerHeight - 0.5) - 1);
                int i1 = Math.Min(count - 1, (int)Math.Ceiling((hi - min) / layerHeight - 0.5) + 1);
                for (int i = i0; i <= i1; i++)
                    if (lo < level[i] && hi >= level[i]) buckets[i].Add(t);
            }

            var result = new SliceLayer[count];
            Parallel.For(0, count, i =>
            {
                double z = level[i];
                double top = min + (i + 1) * layerHeight;
                var layer = new SliceLayer
                {
                    Index = i,
                    Height = layerHeight,
                    Warp = warp,
                    Plane = Geo.PlaneFromAxes(basePlane.Origin + n * top, basePlane.XAxis, basePlane.YAxis)
                };
                Vector3d lift = n * (top - z);
                foreach (var pl in CutLayer(verts, dist, tris, buckets[i], z))
                {
                    if (pl.Count < 2 || pl.Length < minSpacing) continue;
                    for (int k = 0; k < pl.Count; k++) pl[k] += lift;
                    layer.Contours.Add(pl);
                }
                result[i] = layer;
            });

            int empty = 0;
            foreach (var l in result)
            {
                if (l.Contours.Count == 0) { empty++; continue; }
                layers.Add(l);
            }
            if (empty > 0) log?.Add($"{empty} layer(s) had no contour and were dropped.");
            return layers;
        }

        /// <summary>
        /// Cuts the given triangles at one level and chains the pieces into polylines. Every cut
        /// point lies on a mesh edge, so pieces are linked exactly through shared edges - no
        /// tolerance matching needed. A vertex exactly on the level counts as above it.
        /// </summary>
        private static List<Polyline> CutLayer(Point3d[] verts, double[] dist, List<int> tris, List<int> bucket, double z)
        {
            var polylines = new List<Polyline>();
            if (bucket.Count == 0) return polylines;

            // node = crossed mesh edge; each crossed triangle links two nodes
            var nodeOf = new Dictionary<long, int>(bucket.Count * 2);
            var points = new List<Point3d>(bucket.Count + 8);
            var link = new List<int>(bucket.Count * 2 + 16); // two neighbour slots per node, -1 = free

            Func<int, int, int> node = (u, v) =>
            {
                long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
                if (nodeOf.TryGetValue(key, out int id)) return id;
                double du = dist[u] - z, dv = dist[v] - z;
                double t = du / (du - dv);
                id = points.Count;
                points.Add(verts[u] + (verts[v] - verts[u]) * t);
                link.Add(-1); link.Add(-1);
                nodeOf[key] = id;
                return id;
            };

            foreach (int t in bucket)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                bool sa = dist[a] >= z, sb = dist[b] >= z, sc = dist[c] >= z;
                if (sa == sb && sb == sc) continue;
                int n1, n2;
                if (sa != sb && sa != sc) { n1 = node(a, b); n2 = node(a, c); }
                else if (sb != sa && sb != sc) { n1 = node(b, a); n2 = node(b, c); }
                else { n1 = node(c, a); n2 = node(c, b); }
                if (n1 == n2) continue;
                // non-manifold edges (more than two triangles) keep their first two links
                int f1 = link[2 * n1] < 0 ? 2 * n1 : (link[2 * n1 + 1] < 0 ? 2 * n1 + 1 : -1);
                int f2 = link[2 * n2] < 0 ? 2 * n2 : (link[2 * n2 + 1] < 0 ? 2 * n2 + 1 : -1);
                if (f1 < 0 || f2 < 0) continue;
                link[f1] = n2; link[f2] = n1;
            }

            int nodes = points.Count;
            var used = new bool[nodes];
            // open chains first (they start at a node with one neighbour), then closed loops
            for (int pass = 0; pass < 2; pass++)
            {
                for (int start = 0; start < nodes; start++)
                {
                    if (used[start]) continue;
                    bool isEnd = link[2 * start] < 0 || link[2 * start + 1] < 0;
                    if (pass == 0 && !isEnd) continue;

                    var pl = new Polyline();
                    int prev = -1, cur = start;
                    while (cur >= 0 && !used[cur])
                    {
                        used[cur] = true;
                        pl.Add(points[cur]);
                        int a = link[2 * cur], b = link[2 * cur + 1];
                        int next = (a >= 0 && a != prev && !used[a]) ? a : ((b >= 0 && b != prev && !used[b]) ? b : -1);
                        prev = cur; cur = next;
                    }
                    if (pass == 1 && pl.Count > 2) pl.Add(pl[0]); // closed loop
                    if (pl.Count > 1) polylines.Add(pl);
                }
            }
            return polylines;
        }
    }
}
