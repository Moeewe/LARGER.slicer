using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clipper2Lib;
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
        /// <summary>Rhino's mesher, one call at a time (see ToMesh).</summary>
        public static readonly object MeshLock = new object();

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

            var loose = new Mesh();          // surfaces and open meshes: joined along their common edges
            var solids = new List<Mesh>();   // closed bodies stay apart, so that bodies touching each other keep their own contours
            int used = 0, skipped = 0;
            foreach (var item in geometry)
            {
                Cancel.Check(); // between parts: a single Brep is meshed by Rhino in one go
                object o = item;
                if (o is IGH_Goo goo) o = goo.ScriptVariable();

                Brep brep = null;
                if (o is Mesh m) { Take(m.DuplicateMesh(), loose, solids); used++; continue; }
                if (o is Brep b) brep = b;
                else if (o is Extrusion ex) brep = ex.ToBrep();
                else if (o is Surface srf) brep = srf.ToBrep();
                else if (o is Box box && box.IsValid) brep = box.ToBrep(); // Grasshopper's Box is a primitive, no Brep
                else if (o is SubD sd)
                {
                    Mesh sm;
                    lock (MeshLock) sm = Mesh.CreateFromSubD(sd, 3);
                    if (sm != null) { Take(sm, loose, solids); used++; } else skipped++;
                    continue;
                }

                if (brep == null) { skipped++; continue; }
                // Grasshopper 2 solves components side by side; Rhino's mesher crashed when the same
                // Brep was meshed on two threads at once. A copy per call, one call at a time.
                Mesh[] parts;
                lock (MeshLock) parts = Mesh.CreateFromBrep(brep.DuplicateBrep(), mp);
                if (parts == null || parts.Length == 0) { skipped++; continue; }
                var whole = new Mesh();
                foreach (var p in parts) whole.Append(p);
                Take(whole, loose, solids);
                used++;
            }

            if (skipped > 0) log?.Add(Inv.F($"{skipped} input(s) could not be meshed and were skipped."));
            if (used == 0 || loose.Vertices.Count == 0 && solids.Count == 0) return null;

            Cancel.Check();
            var joined = new Mesh();
            if (loose.Vertices.Count > 0) { Join(loose); joined.Append(loose); }
            foreach (var sm in solids) joined.Append(sm);
            joined.Compact();
            return joined;
        }

        /// <summary>Puts a part with the closed bodies, or with the loose surfaces that are joined later.</summary>
        private static void Take(Mesh part, Mesh loose, List<Mesh> solids)
        {
            var own = part.DuplicateMesh();
            Join(own);
            if (own.IsClosed) solids.Add(own); else loose.Append(part);
        }

        private static void Join(Mesh mesh)
        {
            mesh.Faces.ConvertQuadsToTriangles();
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Weld(Math.PI);
            mesh.Compact();
        }

        /// <summary>
        /// Cuts the mesh into layers perpendicular to the plane normal. The model is cut at the
        /// middle of each layer and the contour is lifted to the top of the layer, where the
        /// nozzle travels. Tilt the plane for angled (e.g. 45 degree) printing.
        /// Uses its own triangle/plane cutter: every triangle is assigned once to the layers it
        /// crosses, which is orders of magnitude faster than intersecting the mesh per plane.
        /// The first layer is firstLayerHeight thick (0 = the layer height): the nozzle runs that
        /// high above the bottom of the part, never on it, so the first bead is not pressed into
        /// the bed. Every further layer is one layer height higher.
        /// </summary>
        public static List<SliceLayer> Slice(Mesh mesh, Plane basePlane, double layerHeight, double minSpacing, List<string> log, Warp warp = null, double firstLayerHeight = 0,
            System.Threading.CancellationToken token = default)
        {
            // token: stops early (OperationCanceledException) when the result is not needed any more
            using (Cancel.Use(token)) return SliceCore(mesh, basePlane, layerHeight, minSpacing, log, warp, firstLayerHeight);
        }

        private static List<SliceLayer> SliceCore(Mesh mesh, Plane basePlane, double layerHeight, double minSpacing, List<string> log, Warp warp, double firstLayerHeight)
        {
            var layers = new List<SliceLayer>();
            if (mesh == null || layerHeight <= 0) return layers;
            double first = firstLayerHeight > 0 ? firstLayerHeight : layerHeight;

            Vector3d n = basePlane.Normal;
            n.Unitize();

            Point3d[] verts = mesh.Vertices.ToPoint3dArray();
            if (warp != null)
            {
                // Slice in flat space. The step is reduced so that the thickest real layer is the
                // nominal layer height; thinner places get less material through the path heights.
                // a flat-bottomed non-planar part keeps its first layer flat at the first layer height
                bool flatFirst = warp is SurfaceWarp sw && sw.Morph;
                if (flatFirst) ((SurfaceWarp)warp).FlatBand = first;
                // following the part or its surface: the first layer lies flat on the bed too (Follow the
                // surface: also the second one, as on the printed head of 2024: 0.9 mm, then 2.0 mm)
                if (warp is MeridianWarp mw && mw.Valid)
                {
                    if (mw.FlatBand <= 0) mw.FlatBand = mw.FollowSurface ? first + layerHeight : first;
                    flatFirst = true;
                }
                warp.Prepare(mesh.GetBoundingBox(true));
                var scale = new double[verts.Length];
                Parallel.For(0, verts.Length, new ParallelOptions { CancellationToken = Cancel.Token }, i => { verts[i] = warp.ToFlat(verts[i]); scale[i] = warp.Scale(verts[i]); });
                double maxScale = 1.0;
                foreach (double s in scale) if (s > maxScale) maxScale = s;
                maxScale = Math.Min(maxScale, 2.0);
                if (warp.KeepLayerStep) maxScale = 1.0; // the warp has fixed the layer count itself
                layerHeight /= maxScale;
                if (!flatFirst) first /= maxScale; // (the flat first layer is not stretched)
                if (maxScale > 1.02) log?.Add(Inv.F($"Layers are up to {maxScale:0.00}x further apart on the outside of the bend; the step along the guide is {layerHeight:0.00} mm so no layer is higher than set."));
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
            // as many layers as fit best: a part of 58.2 mm in 2 mm layers gets 29 (58 mm high),
            // not 30 (60 mm, more than a layer above its top); 200 mm gets all 100. Only a part lower
            // than half the first layer gets none.
            double span = max - min;
            if (span <= 0.5 * first) return layers;
            int count = 1 + Math.Max(0, (int)Math.Round((span - first) / layerHeight, MidpointRounding.AwayFromZero));

            // top of each layer (where the nozzle runs) and cut level: the middle of each layer,
            // but never above the model
            var top = new double[count];
            var level = new double[count];
            // (a bent model is slightly uneven at its ends after mapping, so stay a little further inside)
            double margin = warp != null ? Math.Min(0.15, layerHeight * 0.2) : 1e-4;
            for (int i = 0; i < count; i++)
            {
                top[i] = min + first + i * layerHeight;
                double mid = i == 0 ? min + 0.5 * first : top[i] - 0.5 * layerHeight;
                level[i] = Math.Min(mid, max - margin);
            }

            // triangles (quads are split) and the layers each one crosses
            var tris = new List<int>(mesh.Faces.Count * 3);
            foreach (MeshFace f in mesh.Faces)
            {
                tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                if (f.IsQuad) { tris.Add(f.A); tris.Add(f.C); tris.Add(f.D); }
            }
            var body = Bodies(verts.Length, tris);
            var buckets = new List<int>[count];
            for (int i = 0; i < count; i++) buckets[i] = new List<int>();
            Cancel.Check();
            for (int t = 0; t < tris.Count; t += 3)
            {
                if ((t & 0x3FFFF) == 0) Cancel.Check();
                double a = dist[tris[t]], b = dist[tris[t + 1]], c = dist[tris[t + 2]];
                double lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c));
                // the first layer has its own height; from the second on the levels are evenly spaced
                if (lo < level[0] && hi >= level[0]) buckets[0].Add(t);
                int i0 = Math.Max(1, (int)Math.Floor((lo - min - first) / layerHeight + 0.5) - 1);
                int i1 = Math.Min(count - 1, (int)Math.Ceiling((hi - min - first) / layerHeight + 0.5) + 1);
                for (int i = i0; i <= i1; i++)
                    if (lo < level[i] && hi >= level[i]) buckets[i].Add(t);
            }

            var result = new SliceLayer[count];
            int merged = 0, chained = 0;
            Parallel.For(0, count, new ParallelOptions { CancellationToken = Cancel.Token }, i =>
            {
                if (Cancel.Requested) return; // Parallel.For then ends with OperationCanceledException
                double z = level[i];
                var layer = new SliceLayer
                {
                    Index = i,
                    Height = i == 0 ? first : layerHeight,
                    Warp = warp,
                    Plane = Geo.PlaneFromAxes(basePlane.Origin + n * top[i], basePlane.XAxis, basePlane.YAxis)
                };
                Vector3d lift = n * (top[i] - z);
                var bodies = new List<int>();
                foreach (var pl in CutLayer(verts, dist, tris, buckets[i], z, out var at))
                {
                    int b = body[at[0]]; at.RemoveAt(0);
                    if (pl.Count < 2 || pl.Length < minSpacing) continue;
                    for (int k = 0; k < pl.Count; k++) pl[k] += lift;
                    layer.Contours.Add(pl); bodies.Add(b);
                }
                if (JoinEnds(layer.Contours, bodies, Math.Min(0.3, 0.15 * Math.Max(0.1, minSpacing)))) System.Threading.Interlocked.Increment(ref chained);
                if (MergeBodies(layer, bodies)) System.Threading.Interlocked.Increment(ref merged);
                result[i] = layer;
            });

            int empty = 0;
            foreach (var l in result)
            {
                if (l.Contours.Count == 0) { empty++; continue; }
                layers.Add(l);
            }
            if (merged > 0) log?.Add(Inv.F($"{merged} layer(s): parts that overlap or touch are printed as one part (their outlines were united)."));
            if (chained > 0) log?.Add(Inv.F($"{chained} layer(s): open contours whose ends meet were joined (surfaces that touch along an edge, but are not joined in the model)."));
            if (empty > 0) log?.Add(Inv.F($"{empty} layer(s) had no contour and were dropped."));
            if (warp == null && Math.Abs(n.Z) > 0.999)
            {
                // the bed is Z 0: say so when the part does not stand on it
                double bottom = basePlane.Origin.Z * Math.Sign(n.Z) + min;
                if (bottom < -0.05)
                    log?.Add(Inv.F($"The part reaches {-bottom:0.##} mm below Z 0. If Z 0 is the bed, the first layers are printed into it: move the part up onto the bed."));
                else if (bottom > 0.05)
                    log?.Add(Inv.F($"The part stands {bottom:0.##} mm above Z 0. The first layer is printed {first:0.##} mm above the bottom of the part, at Z {bottom + first:0.##}."));
            }
            return layers;
        }

        /// <summary>
        /// Open contours whose ends meet become one: single surfaces (strips, end pieces) that touch along an edge but are
        /// meshed apart. Only ends that have exactly one partner within the tolerance are joined, so a T stays a T.
        /// </summary>
        private static bool JoinEnds(List<Polyline> lines, List<int> bodies, double tol)
        {
            bool any = false;
            while (true)
            {
                var ends = new List<KeyValuePair<int, bool>>(); // contour, at its end
                for (int i = 0; i < lines.Count; i++)
                    if (!Geo.Closed(lines[i])) { ends.Add(new KeyValuePair<int, bool>(i, false)); ends.Add(new KeyValuePair<int, bool>(i, true)); }
                if (ends.Count < 2) return any;
                Func<int, Point3d> at = e => ends[e].Value ? lines[ends[e].Key][lines[ends[e].Key].Count - 1] : lines[ends[e].Key][0];
                var partner = new int[ends.Count];
                for (int a = 0; a < ends.Count; a++)
                {
                    partner[a] = -1; int close = 0;
                    for (int b = 0; b < ends.Count; b++)
                        if (b != a && at(a).DistanceTo(at(b)) <= tol) { close++; partner[a] = b; }
                    if (close != 1) partner[a] = -1;
                }
                int from = -1;
                for (int a = 0; a < ends.Count && from < 0; a++) if (partner[a] >= 0 && partner[partner[a]] == a) from = a;
                if (from < 0) return any;
                int to = partner[from], ia = ends[from].Key, ib = ends[to].Key;
                any = true;
                if (ia == ib)
                {
                    // its own ends: close it, exactly
                    if (lines[ia].Count < 4) { partner = null; return any; }
                    lines[ia][lines[ia].Count - 1] = lines[ia][0];
                    continue;
                }
                var first = new Polyline(lines[ia]); var second = new Polyline(lines[ib]);
                if (!ends[from].Value) first.Reverse();
                if (ends[to].Value) second.Reverse();
                for (int k = 1; k < second.Count; k++) first.Add(second[k]);
                int lo = Math.Min(ia, ib), hi = Math.Max(ia, ib);
                lines[lo] = first;
                lines.RemoveAt(hi);
                if (hi < bodies.Count) bodies.RemoveAt(hi);
            }
        }

        /// <summary>Per mesh vertex the body it belongs to (vertices connected by triangles).</summary>
        private static int[] Bodies(int count, List<int> tris)
        {
            var root = new int[count];
            for (int i = 0; i < count; i++) root[i] = i;
            int Find(int v) { while (root[v] != v) { root[v] = root[root[v]]; v = root[v]; } return v; }
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = Find(tris[t]), b = Find(tris[t + 1]), c = Find(tris[t + 2]);
                if (b != a) root[b] = a;
                if (c != a) root[c] = a;
            }
            for (int i = 0; i < count; i++) root[i] = Find(i);
            return root;
        }

        /// <summary>
        /// Bodies that overlap or touch are one part for the printer: their outlines are united.
        /// Taken one by one they would be read as outline and hole, and a wall would be missing.
        /// A body that lies completely inside another one stays what it was: a hole (an inner shell).
        /// </summary>
        private static bool MergeBodies(SliceLayer layer, List<int> bodies)
        {
            if (bodies.Count < 2) return false;
            var closed = new Dictionary<int, PathsD>();
            var box = new Dictionary<int, BoundingBox>();
            for (int k = 0; k < bodies.Count; k++)
            {
                var c = layer.Contours[k];
                if (!Geo.Closed(c) || c.Count < 4) continue;
                var path = new PathD(c.Count);
                for (int q = 0; q < c.Count - 1; q++)
                {
                    Vector3d d = c[q] - layer.Plane.Origin;
                    path.Add(new PointD(d * layer.Plane.XAxis, d * layer.Plane.YAxis));
                }
                if (!closed.TryGetValue(bodies[k], out var list)) { closed[bodies[k]] = list = new PathsD(); box[bodies[k]] = BoundingBox.Empty; }
                list.Add(path);
                var bb = box[bodies[k]];
                foreach (var p in path) bb.Union(new Point3d(p.x, p.y, 0));
                box[bodies[k]] = bb;
            }
            if (closed.Count < 2) return false;

            var ids = closed.Keys.OrderBy(v => v).ToList();
            var region = new Dictionary<int, PathsD>();
            PathsD Region(int id) { if (!region.TryGetValue(id, out var r)) region[id] = r = Clipper.Union(closed[id], new PathsD(), FillRule.EvenOdd, 5); return r; }
            var group = ids.ToDictionary(v => v, v => v);
            int Find(int v) { while (group[v] != v) v = group[v]; return v; }
            const double eps = 0.001;
            bool any = false;
            for (int a = 0; a < ids.Count; a++)
                for (int b = a + 1; b < ids.Count; b++)
                {
                    var ba = box[ids[a]]; var bb = box[ids[b]];
                    if (ba.Min.X > bb.Max.X + eps || bb.Min.X > ba.Max.X + eps || ba.Min.Y > bb.Max.Y + eps || bb.Min.Y > ba.Max.Y + eps) continue;
                    PathsD ra = Region(ids[a]), rb = Region(ids[b]);
                    double areaA = Math.Abs(Clipper.Area(ra)), areaB = Math.Abs(Clipper.Area(rb));
                    double common = Math.Abs(Clipper.Area(Clipper.Intersect(ra, rb, FillRule.NonZero, 5)));
                    double small = Math.Min(areaA, areaB), large = Math.Max(areaA, areaB);
                    bool one;
                    if (common > 1e-3 * small + 1e-6)
                        // partly overlapping, or the same body twice; completely inside: an inner shell, left alone
                        one = common < 0.999 * small || small > 0.999 * large;
                    else
                        // side by side: one part if they share an edge
                        one = Clipper.Union(ra, rb, FillRule.NonZero, 5).Count < ra.Count + rb.Count;
                    if (one) { group[Find(ids[b])] = Find(ids[a]); any = true; }
                }
            if (!any) return false;

            var contours = new List<Polyline>();
            for (int k = 0; k < bodies.Count; k++)
            {
                var c = layer.Contours[k];
                bool alone = !Geo.Closed(c) || c.Count < 4 || ids.Count(v => Find(v) == Find(bodies[k])) < 2;
                if (alone) contours.Add(c);
            }
            foreach (var g in ids.GroupBy(Find).Where(v => v.Count() > 1))
            {
                var sum = new PathsD();
                foreach (int id in g) sum.AddRange(Region(id));
                foreach (var path in Clipper.Union(sum, new PathsD(), FillRule.NonZero, 5))
                {
                    if (path.Count < 3) continue;
                    var pl = new Polyline(path.Count + 1);
                    foreach (var p in path) pl.Add(layer.Plane.Origin + layer.Plane.XAxis * p.x + layer.Plane.YAxis * p.y);
                    pl.Add(pl[0]);
                    contours.Add(pl);
                }
            }
            layer.Contours = contours;
            return true;
        }

        /// <summary>
        /// Cuts the given triangles at one level and chains the pieces into polylines. Every cut
        /// point lies on a mesh edge, so pieces are linked exactly through shared edges - no
        /// tolerance matching needed. A vertex exactly on the level counts as above it.
        /// </summary>
        private static List<Polyline> CutLayer(Point3d[] verts, double[] dist, List<int> tris, List<int> bucket, double z, out List<int> at)
        {
            at = new List<int>(); // per polyline: a mesh vertex beside it, which tells its body
            var beside = new List<int>();
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
                beside.Add(u);
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
                    if (pl.Count > 1) { polylines.Add(pl); at.Add(beside[start]); }
                }
            }
            return polylines;
        }
    }
}
