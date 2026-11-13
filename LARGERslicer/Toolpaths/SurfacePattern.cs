using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>The textures Surface Pattern can draw into a wall.</summary>
    public enum SurfacePatternKind { Knit = 0, Ribs = 1, Waves = 2, Zigzag = 3, Dots = 4, Loops = 5, Image = 6 }

    /// <summary>A grey picture, 0 = black, 1 = white. Row 0 is the top of the picture.</summary>
    public sealed class GrayImage
    {
        public readonly int Width, Height;
        public readonly float[] V;

        public GrayImage(int width, int height, float[] values)
        {
            Width = Math.Max(1, width); Height = Math.Max(1, height);
            V = values ?? new float[Width * Height];
        }

        /// <summary>Brightness at (u, v): u runs once around the wall (it repeats), v = 0 at the bottom of the part, 1 at the top.</summary>
        public double Sample(double u, double v)
        {
            u -= Math.Floor(u);
            v = Math.Max(0, Math.Min(1, v));
            double x = u * Width - 0.5, y = (1 - v) * Height - 0.5;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        private double At(int x, int y)
        {
            x %= Width; if (x < 0) x += Width;
            y = Math.Max(0, Math.Min(Height - 1, y));
            return V[y * Width + x];
        }
    }

    /// <summary>Settings of Surface Pattern. Lengths in mm.</summary>
    public sealed class PatternOptions
    {
        public SurfacePatternKind Kind = SurfacePatternKind.Knit;
        /// <summary>How far the wall is pulled out at the peaks, mm.</summary>
        public double Amplitude = 3.0;
        /// <summary>Length of one repeat along the layer (one out and one back), mm.</summary>
        public double Pitch = 10.0;
        /// <summary>Layers printed with the same row before the pattern moves on.</summary>
        public int LayerPeriod = 1;
        /// <summary>How far each row moves along the wall, as a fraction of the pitch.</summary>
        public double PhaseShift = 0.0;
        /// <summary>The peaks are also lowered by this much, mm: the bead sags into a loop.</summary>
        public double Droop = 0.0;
        /// <summary>More extrusion at the peaks: 0.3 = 30 % more at a full peak.</summary>
        public double ExtraFlow = 0.0;
        /// <summary>false = the outside of the part, true = the inside (the hollow of a vase).</summary>
        public bool Inside = false;
        /// <summary>Closed curves, Breps or meshes: the pattern only inside them.</summary>
        public List<object> Regions = new List<object>();
        /// <summary>Soft edge of a region, mm.</summary>
        public double Falloff = 10.0;
        /// <summary>Points or curves: the pattern is strongest at them and fades out at Reach.</summary>
        public List<object> Attractors = new List<object>();
        public double Reach = 100.0;
        public GrayImage Image;
        /// <summary>Light parts of the picture get the pattern instead of dark ones, and everything outside a region instead of inside.</summary>
        public bool Invert = false;
    }

    public sealed class PatternResult
    {
        public Toolpath Path;
        public List<Polyline> Preview = new List<Polyline>();
        /// <summary>Path points that stand more than half a bead beside the layer below.</summary>
        public List<Point3d> Hanging = new List<Point3d>();
        public int Moved, WallPoints, Damped, Layers, Respaced;
        public double PitchMin = double.MaxValue, PitchMax = 0, MaxExcursion = 0;
        public string Info = "";
    }

    /// <summary>
    /// Surface patterns: every layer is a row of points; the points are pulled out of the wall
    /// along its normal in a rhythm (alternately, in waves, every n-th, by a picture), and the
    /// rhythm moves on from layer to layer. Small pulls give pixels and knit, large ones make the
    /// bead hang in loops. The path stays one line: only wall points of a layer move, the seam
    /// ramps, bridges, ribs and infill stay where they are, and the pattern fades out at both ends
    /// of every wall so it meets the seam smoothly. Where a pull would run into another part of
    /// the path it is reduced until nothing crosses.
    /// </summary>
    public static class SurfacePattern
    {
        public static readonly string[] Names =
        {
            "Knit (pixels, rows alternate)",
            "Ribs (vertical, same every layer)",
            "Waves (sine, drifts with Phase Shift)",
            "Zigzag (sharp, same every layer)",
            "Dots (bumps, rows alternate)",
            "Loops (long pulls that hang down)",
            "Image (knit pixels from a picture)",
        };

        // ------------------------------------------------------------------ the patterns
        /// <summary>Samples per repeat. Sharp patterns use few (straight lines between), smooth ones more.</summary>
        public static int Spp(SurfacePatternKind k)
        {
            switch (k)
            {
                case SurfacePatternKind.Dots: return 4;
                case SurfacePatternKind.Ribs: return 10;
                case SurfacePatternKind.Waves: return 8;
                case SurfacePatternKind.Loops: return 12;
                default: return 2;
            }
        }

        /// <summary>Rows alternate by half a repeat (knit), on top of Phase Shift.</summary>
        public static bool Alternates(SurfacePatternKind k) => k == SurfacePatternKind.Knit || k == SurfacePatternKind.Dots || k == SurfacePatternKind.Image;

        /// <summary>How far out (0..1) the j-th sample of the pattern grid is.</summary>
        public static double Value(SurfacePatternKind k, long j)
        {
            int spp = Spp(k);
            long m = ((j % spp) + spp) % spp;
            double frac = m / (double)spp;
            switch (k)
            {
                case SurfacePatternKind.Dots: return m == 0 ? 1 : 0;
                case SurfacePatternKind.Ribs:
                {
                    double t = (frac - 0.3) / 0.4;
                    return t > 0 && t < 1 ? Math.Sin(Math.PI * t) : 0;
                }
                case SurfacePatternKind.Waves: return 0.5 - 0.5 * Math.Cos(2 * Math.PI * frac);
                case SurfacePatternKind.Loops:
                {
                    double t = (frac - 0.25) / 0.5;
                    return t > 0 && t < 1 ? Math.Sqrt(Math.Sin(Math.PI * t)) : 0;
                }
                default: return m == 0 ? 1 : 0; // knit, zigzag, image: out, back, out, back
            }
        }

        /// <summary>The pattern at a continuous position U (in repeats): straight between the samples.</summary>
        public static double ValueAt(SurfacePatternKind k, double u)
        {
            double x = u * Spp(k);
            long j = (long)Math.Floor(x);
            double a = x - j;
            return Value(k, j) * (1 - a) + Value(k, j + 1) * a;
        }

        // ------------------------------------------------------------------ helpers
        private static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

        private static Point2d Flat(Plane pl, Point3d p) { Vector3d v = p - pl.Origin; return new Point2d(v * pl.XAxis, v * pl.YAxis); }

        private static double Cross(Point2d a, Point2d b, Point2d c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static double SegDist(Point2d p, Point2d a, Point2d b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static bool InPolygon(List<Point2d> poly, Point2d p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
            }
            return inside;
        }

        /// <summary>Segments of one layer in a grid: nearest distance and rays.</summary>
        private sealed class SegGrid
        {
            public readonly List<Point2d> A = new List<Point2d>(), B = new List<Point2d>();
            public readonly List<int> Id = new List<int>();
            private readonly double _cell;
            private readonly Dictionary<long, List<int>> _map = new Dictionary<long, List<int>>();
            private int _gx0 = int.MaxValue, _gy0 = int.MaxValue, _gx1 = int.MinValue, _gy1 = int.MinValue;

            public SegGrid(double cell) { _cell = Math.Max(1.0, cell); }

            private static long Key(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;

            public void Add(Point2d a, Point2d b, int id)
            {
                int k = A.Count;
                A.Add(a); B.Add(b); Id.Add(id);
                int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / _cell), x1 = (int)Math.Floor(Math.Max(a.X, b.X) / _cell);
                int y0 = (int)Math.Floor(Math.Min(a.Y, b.Y) / _cell), y1 = (int)Math.Floor(Math.Max(a.Y, b.Y) / _cell);
                if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4000) { x1 = Math.Min(x1, x0 + 60); y1 = Math.Min(y1, y0 + 60); }
                for (int gx = x0; gx <= x1; gx++)
                    for (int gy = y0; gy <= y1; gy++)
                    {
                        long key = Key(gx, gy);
                        if (!_map.TryGetValue(key, out var list)) _map[key] = list = new List<int>();
                        list.Add(k);
                    }
                _gx0 = Math.Min(_gx0, x0); _gy0 = Math.Min(_gy0, y0); _gx1 = Math.Max(_gx1, x1); _gy1 = Math.Max(_gy1, y1);
            }

            public IEnumerable<int> Near(double minX, double minY, double maxX, double maxY)
            {
                int x0 = (int)Math.Floor(minX / _cell), x1 = (int)Math.Floor(maxX / _cell);
                int y0 = (int)Math.Floor(minY / _cell), y1 = (int)Math.Floor(maxY / _cell);
                for (int gx = x0; gx <= x1; gx++)
                    for (int gy = y0; gy <= y1; gy++)
                        if (_map.TryGetValue(Key(gx, gy), out var list))
                            foreach (int k in list) yield return k;
            }

            /// <summary>Distance to the nearest segment, searched up to maxD (infinity if none that close).</summary>
            public double Nearest(Point2d p, double maxD)
            {
                double best = double.PositiveInfinity;
                int cx = (int)Math.Floor(p.X / _cell), cy = (int)Math.Floor(p.Y / _cell);
                int rings = (int)Math.Ceiling(maxD / _cell) + 1;
                for (int r = 0; r <= rings; r++)
                {
                    for (int gx = cx - r; gx <= cx + r; gx++)
                        for (int gy = cy - r; gy <= cy + r; gy++)
                        {
                            if (Math.Max(Math.Abs(gx - cx), Math.Abs(gy - cy)) != r) continue;
                            if (!_map.TryGetValue(Key(gx, gy), out var list)) continue;
                            foreach (int k in list) { double d = SegDist(p, A[k], B[k]); if (d < best) best = d; }
                        }
                    if (best <= r * _cell) break;
                }
                return best <= maxD ? best : double.PositiveInfinity;
            }

            /// <summary>Where a ray from o along unit d first meets a segment (infinity: it leaves the layer).
            /// Segments whose id is in [exLo, exHi] are the ray's own and are skipped.</summary>
            public double Ray(Point2d o, Vector2d d, int exLo, int exHi)
            {
                if (A.Count == 0) return double.PositiveInfinity;
                int gx = (int)Math.Floor(o.X / _cell), gy = (int)Math.Floor(o.Y / _cell);
                int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1;
                double tdx = Math.Abs(d.X) < 1e-12 ? double.PositiveInfinity : _cell / Math.Abs(d.X);
                double tdy = Math.Abs(d.Y) < 1e-12 ? double.PositiveInfinity : _cell / Math.Abs(d.Y);
                double nx = (sx > 0 ? (gx + 1) * _cell - o.X : o.X - gx * _cell);
                double ny = (sy > 0 ? (gy + 1) * _cell - o.Y : o.Y - gy * _cell);
                double tmx = Math.Abs(d.X) < 1e-12 ? double.PositiveInfinity : nx / Math.Abs(d.X);
                double tmy = Math.Abs(d.Y) < 1e-12 ? double.PositiveInfinity : ny / Math.Abs(d.Y);
                double best = double.PositiveInfinity;
                for (int step = 0; step < 100000; step++)
                {
                    if (_map.TryGetValue(Key(gx, gy), out var list))
                        foreach (int k in list)
                        {
                            int id = Id[k];
                            if (id >= exLo && id <= exHi) continue;
                            Point2d a = A[k], b = B[k];
                            double ex = b.X - a.X, ey = b.Y - a.Y;
                            double den = d.X * ey - d.Y * ex;
                            if (Math.Abs(den) < 1e-12) continue;
                            double wx = a.X - o.X, wy = a.Y - o.Y;
                            double t = (wx * ey - wy * ex) / den, u = (wx * d.Y - wy * d.X) / den;
                            if (t > 1e-3 && u >= 0 && u <= 1 && t < best) best = t;
                        }
                    double exit = Math.Min(tmx, tmy);
                    if (best <= exit) return best;
                    if (tmx < tmy) { gx += sx; tmx += tdx; } else { gy += sy; tmy += tdy; }
                    if ((sx > 0 && gx > _gx1) || (sx < 0 && gx < _gx0)) if (tmx < tmy || tdy == double.PositiveInfinity) return best;
                    if ((sy > 0 && gy > _gy1) || (sy < 0 && gy < _gy0)) if (tmy <= tmx || tdx == double.PositiveInfinity) return best;
                    if (gx > _gx1 + 1 && sx > 0 || gx < _gx0 - 1 && sx < 0 || gy > _gy1 + 1 && sy > 0 || gy < _gy0 - 1 && sy < 0) return best;
                }
                return best;
            }
        }

        /// <summary>One wall of one layer: consecutive wall points.</summary>
        private sealed class Run
        {
            public int I0, I1;                    // first and last path index
            public List<Point3d> P = new List<Point3d>();
            public double[] S;                    // arc length at each point
            public double L, Gap, Lc;             // run length, gap from end to start, length around
            public bool Closed, Ccw;
            public Point2d C;                     // centroid in the layer plane
            public double Area;
            public Point3d PointAt(double s)
            {
                if (Closed)
                {
                    s %= Lc; if (s < 0) s += Lc;
                    if (s > L) { double t = Gap > 1e-9 ? (s - L) / Gap : 0; return P[P.Count - 1] + (P[0] - P[P.Count - 1]) * t; }
                }
                else s = Math.Max(0, Math.Min(L, s));
                int k = Seg(s);
                double len = S[k + 1] - S[k];
                double u = len > 1e-12 ? (s - S[k]) / len : 0;
                return P[k] + (P[k + 1] - P[k]) * u;
            }
            /// <summary>Index k of the segment P[k]..P[k+1] that holds s (0 <= s <= L).</summary>
            public int Seg(double s)
            {
                int lo = 0, hi = P.Count - 2;
                if (hi < 0) return 0;
                if (s <= 0) return 0;
                if (s >= L) return hi;
                while (lo < hi) { int mid = (lo + hi + 1) / 2; if (S[mid] <= s) lo = mid; else hi = mid - 1; }
                return lo;
            }
        }

        private sealed class Track
        {
            public bool Closed;
            public Point2d C;
            public Point3d Anchor;   // closed: where the pattern counts from
            public int N;            // closed: repeats around
            public Point3d Start;    // open: the end the pattern counts from
            public double L;
        }

        private sealed class Sample
        {
            public double S, U, Ui;
            public Point3d Base;
            public Vector3d N;      // outward normal of the wall, in the layer
            public int Src;         // path index whose height, width, axis and flow the point takes
            public double F;        // pattern value 0..1
            public double W = 1;    // all weights together (region, attractor, image, fade, eligibility)
            public double Damp = 1; // reduced where a pull would cross the path
            public double Amp;      // final pull, mm
            public Point3d Q;       // final point
        }

        private sealed class RegionGeo
        {
            public List<Point3d> Curve;  // closed curve, sampled
            public BoundingBox Box;
            public Mesh Mesh; public bool MeshClosed;
        }

        // ------------------------------------------------------------------ main
        public static PatternResult Apply(Toolpath tp, PatternOptions o, List<string> log)
        {
            var res = new PatternResult { Path = tp };
            if (tp == null || tp.Count < 3 || o == null) return res;
            log = log ?? new List<string>();
            var st = tp.Settings ?? new PrintSettings();
            double w = Math.Max(0.1, st.BeadWidth);
            double A = Math.Max(0, o.Amplitude);
            double pitch = Math.Max(Math.Max(1.0, 0.4 * w), o.Pitch);
            int period = Math.Max(1, o.LayerPeriod);
            var kind = o.Kind;
            int spp = Spp(kind);
            bool useImage = o.Image != null;
            if (kind == SurfacePatternKind.Image && !useImage) log.Add("Image: no picture given (or it could not be read), so the knit pixels are printed everywhere. Connect the file path of a grey picture.");
            if (A <= 1e-6) { log.Add("Amplitude is 0: nothing to do."); res.Path = tp.Duplicate(); Finish(res, tp, st); return res; }
            int n = tp.Count;

            // ---- where the print starts, the bed, the layers
            int firstPrint = 1;
            while (firstPrint < n && tp.Flows[firstPrint] <= 0) firstPrint++;
            firstPrint = Math.Max(0, firstPrint - 1);
            var byLayer = new SortedDictionary<int, List<int>>();
            double bedZ = double.MaxValue;
            for (int i = firstPrint; i < n; i++)
            {
                if (tp.Roles[i] == PathRole.Skirt) continue;
                if (!byLayer.TryGetValue(tp.Layers[i], out var list)) byLayer[tp.Layers[i]] = list = new List<int>();
                list.Add(i);
                if (tp.Flows[i] > 0 && tp.Points[i].Z < bedZ) bedZ = tp.Points[i].Z;
            }
            if (byLayer.Count == 0) { res.Path = tp.Duplicate(); Finish(res, tp, st); return res; }
            int firstLayer = byLayer.Keys.First();

            bool IsWall(int i) => i > 0 && tp.Roles[i] == PathRole.Wall && tp.Flows[i] > 0 && tp.Roles[i - 1] != PathRole.Skirt;

            // height range of the walls, for the picture
            double zMin = double.MaxValue, zMax = double.MinValue;
            foreach (var kv in byLayer)
            {
                if (kv.Key == firstLayer) continue;
                foreach (int i in kv.Value) if (IsWall(i)) { double z = tp.Points[i].Z; if (z < zMin) zMin = z; if (z > zMax) zMax = z; }
            }
            if (zMax <= zMin) { zMin = 0; zMax = 1; }

            // ---- regions and attractors
            if (o.Attractors == null) o.Attractors = new List<object>();
            var regions = new List<RegionGeo>();
            int openCurves = 0;
            foreach (var g in o.Regions ?? new List<object>())
            {
                if (g == null) continue;
                object go = g;
                if (go is Polyline pl0) go = new PolylineCurve(pl0);
                if (go is Curve c)
                {
                    if (!c.IsClosed) { openCurves++; continue; }
                    int cnt = 160;
                    double[] ts = c.DivideByCount(cnt, true);
                    if (ts == null) continue;
                    var pts = ts.Select(t => c.PointAt(t)).ToList();
                    regions.Add(new RegionGeo { Curve = pts, Box = new BoundingBox(pts) });
                }
                else if (go is Point3d || go is Point) { o.Attractors.Add(go); }
                else
                {
                    Mesh m = go as Mesh;
                    if (m == null) m = SliceEngine.ToMesh(new[] { go }, 0.5, null);
                    if (m == null || m.Faces.Count == 0) continue;
                    regions.Add(new RegionGeo { Mesh = m, MeshClosed = m.IsClosed, Box = m.GetBoundingBox(false) });
                }
            }
            if (openCurves > 0) log.Add(Inv.F($"Region: {openCurves} curve(s) are open and were left out. Draw closed curves on the part (or connect them as Attractors)."));
            if (o.Attractors == null) o.Attractors = new List<object>();
            var attrPts = new List<Point3d>(); var attrCrv = new List<Curve>();
            foreach (var g in o.Attractors ?? new List<object>())
            {
                if (g is Point3d p3) attrPts.Add(p3);
                else if (g is Point pp) attrPts.Add(pp.Location);
                else if (g is Polyline pl1) attrCrv.Add(new PolylineCurve(pl1));
                else if (g is Curve ac) attrCrv.Add(ac);
                else if (g is PointCloud pc) attrPts.AddRange(pc.GetPoints());
            }
            bool useRegion = regions.Count > 0, useAttr = attrPts.Count > 0 || attrCrv.Count > 0;
            double falloff = Math.Max(0, o.Falloff), reach = o.Reach > 0 ? o.Reach : 100;

            // ---- layer by layer
            var newRuns = new Dictionary<int, Tuple<Run, List<Sample>>>(); // first index -> samples
            var tracks = new List<Track>();
            SegGrid below = null; Plane belowPlane = Plane.WorldXY;
            int layerNo = 0, damped = 0, hang = 0, moved = 0, wallPts = 0;
            double maxExc = 0;
            foreach (var kv in byLayer)
            {
                int layer = kv.Key;
                var idx = kv.Value;
                Vector3d axis = tp.Normals[idx[0]];
                if (!axis.Unitize()) axis = Vector3d.ZAxis;
                Plane plane = Geo.PlaneAt(tp.Points[idx[0]], axis);

                // all printing moves of this layer, as they are now
                double cell = Math.Max(2 * w, 4);
                var grid = new SegGrid(cell);
                for (int k = 1; k < idx.Count; k++)
                {
                    int a = idx[k - 1], b = idx[k];
                    if (b != a + 1 || tp.Flows[b] <= 0) continue;
                    grid.Add(Flat(plane, tp.Points[a]), Flat(plane, tp.Points[b]), b);
                }

                // the walls of this layer
                var runs = new List<Run>();
                for (int k = 0; k < idx.Count; k++)
                {
                    if (!IsWall(idx[k])) continue;
                    int k1 = k;
                    while (k1 + 1 < idx.Count && idx[k1 + 1] == idx[k1] + 1 && IsWall(idx[k1 + 1])) k1++;
                    // the run starts at the point before its first wall move (that is where the wall begins)
                    int i0 = idx[k] - 1 >= 0 && k > 0 && idx[k - 1] == idx[k] - 1 ? idx[k] - 1 : idx[k];
                    int i1 = idx[k1];
                    k = k1;
                    if (i1 - i0 < 3) continue;
                    var r = new Run { I0 = i0, I1 = i1 };
                    for (int i = i0; i <= i1; i++) r.P.Add(tp.Points[i]);
                    r.S = new double[r.P.Count];
                    for (int i = 1; i < r.P.Count; i++) r.S[i] = r.S[i - 1] + r.P[i].DistanceTo(r.P[i - 1]);
                    r.L = r.S[r.P.Count - 1];
                    if (r.L < Math.Max(3 * w, pitch)) continue;
                    r.Gap = r.P[0].DistanceTo(r.P[r.P.Count - 1]);
                    r.Closed = r.Gap < Math.Max(3 * w, 0.15 * r.L) && r.L > 6 * w;
                    r.Lc = r.L + (r.Closed ? r.Gap : 0);
                    // area and centroid in the plane
                    double area = 0, cx = 0, cy = 0;
                    var f2 = r.P.Select(p => Flat(plane, p)).ToList();
                    for (int i = 0, j = f2.Count - 1; i < f2.Count; j = i++)
                    {
                        double cr = f2[j].X * f2[i].Y - f2[i].X * f2[j].Y;
                        area += cr; cx += (f2[j].X + f2[i].X) * cr; cy += (f2[j].Y + f2[i].Y) * cr;
                    }
                    area *= 0.5;
                    r.Area = area; r.Ccw = area > 0;
                    r.C = Math.Abs(area) > 1e-9 ? new Point2d(cx / (6 * area), cy / (6 * area)) : new Point2d(f2.Average(p => p.X), f2.Average(p => p.Y));
                    runs.Add(r);
                }

                bool patternLayer = layer != firstLayer && runs.Count > 0;
                int row = Math.Max(0, (layerNo - 1)) / period;
                double shift = o.PhaseShift * row + (Alternates(kind) && row % 2 == 1 ? 0.5 : 0);
                var newTracks = new List<Track>();
                var layerSamples = new List<Tuple<Run, List<Sample>>>();

                foreach (var r in runs)
                {
                    // ---- follow the wall from the layer below: same count of repeats, same start
                    Track prev = null; double bestD = double.MaxValue;
                    foreach (var t in tracks)
                    {
                        if (t.Closed != r.Closed) continue;
                        double d = r.Closed ? t.C.DistanceTo(r.C) : t.Start.DistanceTo(r.P[0]) < t.Start.DistanceTo(r.P[r.P.Count - 1]) ? t.Start.DistanceTo(r.P[0]) : t.Start.DistanceTo(r.P[r.P.Count - 1]);
                        double lim = r.Closed ? Math.Max(4 * w, 0.25 * r.Lc / Math.PI) : Math.Max(6 * w, 0.5 * r.L);
                        if (d < lim && d < bestD) { bestD = d; prev = t; }
                    }
                    var tr = new Track { Closed = r.Closed, C = r.C, L = r.L };
                    double sAnchor = 0; bool forward = true; int N = 1;
                    if (r.Closed)
                    {
                        // where the count starts: next to the start of the layer below, else at the point furthest along X
                        if (prev != null)
                        {
                            double best = double.MaxValue;
                            for (int i = 0; i + 1 < r.P.Count; i++)
                            {
                                var ln = new Line(r.P[i], r.P[i + 1]);
                                double t = ln.ClosestParameter(prev.Anchor); t = Math.Max(0, Math.Min(1, t));
                                double d = ln.PointAt(t).DistanceTo(prev.Anchor);
                                if (d < best) { best = d; sAnchor = r.S[i] + t * (r.S[i + 1] - r.S[i]); }
                            }
                        }
                        else
                        {
                            double bx = double.MinValue;
                            for (int i = 0; i < r.P.Count; i++) { var f = Flat(plane, r.P[i]); if (f.X > bx) { bx = f.X; sAnchor = r.S[i]; } }
                        }
                        tr.Anchor = r.PointAt(sAnchor);
                        if (prev != null && prev.N > 0 && r.Lc / prev.N > pitch / 1.4 && r.Lc / prev.N < pitch * 1.4) N = prev.N;
                        else { N = Math.Max(1, (int)Math.Round(r.Lc / pitch)); if (prev != null && patternLayer) res.Respaced++; }
                        tr.N = N;
                    }
                    else
                    {
                        if (prev != null) forward = prev.Start.DistanceTo(r.P[0]) <= prev.Start.DistanceTo(r.P[r.P.Count - 1]);
                        else
                        {
                            var ch = Flat(plane, r.P[r.P.Count - 1]) - Flat(plane, r.P[0]);
                            forward = ch.X > 1e-6 || (Math.Abs(ch.X) <= 1e-6 && ch.Y > 0);
                        }
                        tr.Start = forward ? r.P[0] : r.P[r.P.Count - 1];
                    }
                    newTracks.Add(tr);
                    if (!patternLayer) continue;

                    // ---- the pattern coordinate: U = repeats along the wall, Ui = around (0..1) for the picture
                    double sign = r.Closed ? (r.Ccw ? 1 : -1) : (forward ? 1 : -1);
                    double k = r.Closed ? sign * N / r.Lc : sign / pitch;
                    double U0 = r.Closed ? -sign * sAnchor * N / r.Lc + shift : (forward ? 0 : r.L / pitch) + shift;
                    double Uat(double s) => U0 + k * s;
                    double UiAt(double s)
                    {
                        if (r.Closed) { double sig = sign * (s - sAnchor) / r.Lc; return sig - Math.Floor(sig); }
                        return forward ? s / r.L : 1 - s / r.L;
                    }
                    if (r.Closed) { double pe = r.Lc / N; res.PitchMin = Math.Min(res.PitchMin, pe); res.PitchMax = Math.Max(res.PitchMax, pe); }

                    // ---- where the samples go: the pattern grid, the corners, both ends, and enough points on curves
                    var ss = new List<double> { 0, r.L };
                    double ua = Uat(0), ub = Uat(r.L);
                    long j0 = (long)Math.Ceiling(Math.Min(ua, ub) * spp), j1 = (long)Math.Floor(Math.Max(ua, ub) * spp);
                    if (j1 - j0 < 200000)
                        for (long j = j0; j <= j1; j++) { double s = (j / (double)spp - U0) / k; if (s > 0 && s < r.L) ss.Add(s); }
                    for (int i = 1; i + 1 < r.P.Count; i++)
                    {
                        Vector3d d0 = r.P[i] - r.P[i - 1], d1 = r.P[i + 1] - r.P[i];
                        if (d0.Unitize() && d1.Unitize() && d0 * d1 < Math.Cos(Math.PI / 12)) ss.Add(r.S[i]);
                    }
                    ss.Sort();
                    var sl = new List<double>();
                    double maxStep = Math.Max(2 * st.Resolution, 0.5 * w);
                    foreach (double s in ss)
                    {
                        if (sl.Count > 0 && s - sl[sl.Count - 1] < 0.25) { if (s >= r.L - 1e-9) sl[sl.Count - 1] = r.L; continue; }
                        if (sl.Count > 0)
                        {
                            double gap = s - sl[sl.Count - 1];
                            int extra = (int)Math.Floor(gap / maxStep);
                            double s0 = sl[sl.Count - 1];
                            for (int e = 1; e <= extra; e++) { double si = s0 + gap * e / (extra + 1); if (si - sl[sl.Count - 1] >= 0.25 && s - si >= 0.25) sl.Add(si); }
                        }
                        sl.Add(s);
                    }
                    if (sl[0] > 0) sl.Insert(0, 0);
                    if (sl[sl.Count - 1] < r.L) sl.Add(r.L);

                    var samples = new List<Sample>(sl.Count);
                    double h = Math.Max(0.5, Math.Min(0.5 * w, 0.25 * pitch));
                    foreach (double s in sl)
                    {
                        var sm = new Sample { S = s, U = Uat(s), Ui = UiAt(s), Base = r.PointAt(s) };
                        int seg = r.Seg(s);
                        sm.Src = s <= 0 ? r.I0 : r.I0 + seg + 1;
                        Point3d pa = r.Closed ? r.PointAt(s - h) : r.PointAt(Math.Max(0, s - h)), pb = r.Closed ? r.PointAt(s + h) : r.PointAt(Math.Min(r.L, s + h));
                        Vector3d t = pb - pa; t -= axis * (t * axis);
                        if (!t.Unitize()) t = plane.XAxis;
                        Vector3d nn = Vector3d.CrossProduct(t, axis); // right of the direction of travel
                        if (r.Closed) { if (!r.Ccw) nn = -nn; }    // closed: away from the loop
                        else if (forward) nn = -nn;                  // open: left of the wall, read from its start
                        nn.Unitize();
                        sm.N = nn;
                        sm.F = ValueAt(kind, sm.U);
                        samples.Add(sm);
                    }

                    // ---- which side of the wall: the outside is where a ray along the normal leaves the layer
                    int votePlus = 0, voteMinus = 0, freePlus = 0, freeMinus = 0;
                    var okPlus = new bool[samples.Count]; var okMinus = new bool[samples.Count];
                    double room = A + 2 * w;
                    for (int q = 0; q < samples.Count; q++)
                    {
                        var sm = samples[q];
                        Point2d o2 = Flat(plane, sm.Base);
                        Vector2d d2 = new Vector2d(sm.N * plane.XAxis, sm.N * plane.YAxis);
                        if (!d2.Unitize()) continue;
                        int lo = sm.Src - 1, hi = sm.Src + 1;
                        double tp1 = grid.Ray(o2, d2, lo, hi), tm1 = grid.Ray(o2, -d2, lo, hi);
                        bool escP = double.IsPositiveInfinity(tp1), escM = double.IsPositiveInfinity(tm1);
                        if (escP) freePlus++;
                        if (escM) freeMinus++;
                        if (!o.Inside) { okPlus[q] = escP; okMinus[q] = escM; }
                        else { okPlus[q] = !escP && tp1 >= room; okMinus[q] = !escM && tm1 >= room; }
                        if (okPlus[q]) votePlus++;
                        if (okMinus[q]) voteMinus++;
                    }
                    bool standing = freePlus > 0.6 * samples.Count && freeMinus > 0.6 * samples.Count; // a wall open on both faces
                    int dir;
                    bool[] ok;
                    if (standing && !r.Closed)
                    {
                        dir = o.Inside ? -1 : 1;
                        ok = samples.Select(s0 => true).ToArray();
                    }
                    else if (votePlus == 0 && voteMinus == 0) continue;
                    else { dir = votePlus >= voteMinus ? 1 : -1; ok = dir > 0 ? okPlus : okMinus; }
                    if (dir < 0) foreach (var sm in samples) sm.N = -sm.N;

                    // ---- weights: side, fade at both ends, region, attractors, picture
                    double fade = Math.Min(0.5 * pitch, 0.25 * r.L);
                    double win = 0.5 * pitch;
                    for (int q = 0; q < samples.Count; q++)
                    {
                        var sm = samples[q];
                        double e = ok[q] ? 1 : 0;
                        if (e > 0)
                        {
                            // soft edge where the free side ends
                            double sum = 0; int cnt = 0;
                            for (int q2 = q; q2 >= 0 && sm.S - samples[q2].S <= win; q2--) { sum += ok[q2] ? 1 : 0; cnt++; }
                            for (int q2 = q + 1; q2 < samples.Count && samples[q2].S - sm.S <= win; q2++) { sum += ok[q2] ? 1 : 0; cnt++; }
                            e = Math.Min(e, Smooth(cnt > 0 ? (sum / cnt - 0.5) * 2 : 1));
                        }
                        double we = fade > 1e-6 ? Smooth(Math.Min(sm.S, r.L - sm.S) / fade) : 1;
                        double wr = 1;
                        if (useRegion)
                        {
                            wr = 0;
                            foreach (var rg in regions) { wr = Math.Max(wr, RegionWeight(rg, sm, r, plane, axis, falloff, w, A)); if (wr >= 1) break; }
                            if (o.Invert) wr = 1 - wr;
                        }
                        double wa = 1;
                        if (useAttr)
                        {
                            double dmin = double.MaxValue;
                            foreach (var p in attrPts) dmin = Math.Min(dmin, p.DistanceTo(sm.Base));
                            foreach (var c in attrCrv) if (c.ClosestPoint(sm.Base, out double t, dmin < double.MaxValue ? dmin : 0)) dmin = Math.Min(dmin, c.PointAt(t).DistanceTo(sm.Base));
                            wa = 1 - Smooth(dmin / reach);
                        }
                        double wi = 1;
                        if (useImage)
                        {
                            double v = (sm.Base.Z - zMin) / (zMax - zMin);
                            double gray = o.Image.Sample(sm.Ui, v);
                            wi = o.Invert ? gray : 1 - gray; // dark = full pattern
                        }
                        sm.W = e * we * wr * wa * wi;
                    }
                    layerSamples.Add(Tuple.Create(r, samples));
                }

                // ---- place the points; where they would cross the path, pull less
                if (patternLayer && layerSamples.Count > 0)
                {
                    double hLayer = tp.Heights[idx[idx.Count / 2]] > 0 ? tp.Heights[idx[idx.Count / 2]] : st.LayerHeight;
                    void Place()
                    {
                        foreach (var ls in layerSamples)
                            foreach (var sm in ls.Item2)
                            {
                                sm.Amp = A * sm.F * sm.W * sm.Damp;
                                double g = A > 0 ? sm.Amp / A : 0;
                                Point3d q = sm.Base + sm.N * sm.Amp;
                                if (o.Droop > 0 && g > 0)
                                {
                                    double room = Math.Max(0, (sm.Base.Z - bedZ) - 0.5 * hLayer);
                                    q -= axis * Math.Min(o.Droop * g, room);
                                }
                                sm.Q = q;
                            }
                    }
                    Place();
                    var involved = new HashSet<Sample>();
                    for (int pass = 0; pass < 6; pass++)
                    {
                        var hit = Crossings(tp, idx, layerSamples, plane);
                        if (hit.Count == 0) break;
                        foreach (var sm in hit) { sm.Damp = pass >= 4 ? 0 : sm.Damp * 0.5; involved.Add(sm); }
                        Place();
                    }
                    damped += involved.Count;
                    foreach (var ls in layerSamples) newRuns[ls.Item1.I0] = ls;
                }

                // ---- this layer as it will be printed: support test against the layer below, then it becomes "below"
                var finalGrid = new SegGrid(Math.Max(2 * w, 4));
                {
                    var pts = LayerPoints(tp, idx, newRuns);
                    for (int k = 1; k < pts.Count; k++)
                        if (pts[k].Item2) finalGrid.Add(Flat(plane, pts[k - 1].Item1), Flat(plane, pts[k].Item1), k);
                }
                if (patternLayer)
                {
                    foreach (var ls in layerSamples)
                        foreach (var sm in ls.Item2)
                        {
                            wallPts++;
                            if (sm.Amp > 0.05) moved++;
                            if (sm.Amp > maxExc) maxExc = sm.Amp;
                            if (below == null || sm.Amp <= 0.05) continue;
                            double d = below.Nearest(Flat(belowPlane, sm.Q), 0.5 * w + 1e-6);
                            if (double.IsPositiveInfinity(d)) { hang++; res.Hanging.Add(sm.Q); }
                        }
                    res.Layers++;
                }
                below = finalGrid; belowPlane = plane;
                tracks = newTracks;
                layerNo++;
            }

            // ---- the new path
            var outp = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            for (int i = 0; i < n; i++)
            {
                if (newRuns.TryGetValue(i, out var nr))
                {
                    var r = nr.Item1; var samples = nr.Item2;
                    double gPrev = 0;
                    for (int q = 0; q < samples.Count; q++)
                    {
                        var sm = samples[q];
                        double g = A > 0 ? sm.Amp / A : 0;
                        int src = q == 0 ? r.I0 : sm.Src;
                        double flow = tp.Flows[src];
                        if (q > 0 && o.ExtraFlow != 0) flow *= Math.Max(0, 1 + o.ExtraFlow * 0.5 * (g + gPrev));
                        if (q == 0) outp.Add(sm.Q, tp.Normals[src], tp.Heights[src], tp.Widths[src], tp.Flows[src], tp.Roles[src], tp.Layers[src]);
                        else outp.Add(sm.Q, tp.Normals[src], tp.Heights[src], tp.Widths[src], flow, PathRole.Wall, tp.Layers[src]);
                        gPrev = g;
                    }
                    i = r.I1;
                    continue;
                }
                outp.AddFrom(tp, i);
            }
            // Follow the surface: the bead heights come from the lap below by the shell law (BeadLaw);
            // the pattern moved the points, so they are measured again on the new path
            if (tp.Warp != null && tp.Warp.UseBeadLaw) BeadLaw.Apply(outp, out _, out _, out _);
            res.Path = outp;
            res.Moved = moved; res.WallPoints = wallPts; res.Damped = damped; res.MaxExcursion = maxExc;
            Finish(res, outp, st);

            // ---- what was done, in plain words
            string name = Names[(int)kind]; int cut = name.IndexOf(" ("); if (cut > 0) name = name.Substring(0, cut);
            var info = new List<string>();
            info.Add(Inv.F($"{name}: amplitude {A:0.##} mm, pitch {pitch:0.##} mm") + (res.PitchMax > 0 ? Inv.F($" (fitted to the walls: {res.PitchMin:0.#} to {res.PitchMax:0.#} mm, a whole number of repeats around every closed wall)") : "") +
                     Inv.F($", {res.Layers} layer(s) patterned (the first layer stays plain), {moved} of {wallPts} wall points moved out, up to {maxExc:0.#} mm."));
            if (hang > 0)
                info.Add(Inv.F($"{hang} point(s) stand more than half a bead ({0.5 * w:0.#} mm) beside the layer below - see Hanging. That is the texture: pixels and loops hang over on purpose, bridging between the points that rest on the bead below. Path Check counts them as unsupported; judge them by eye, and keep the amplitude under about one bead width unless you want loops."));
            else info.Add(Inv.F($"Every moved point still rests on the bead below (within half a bead, {0.5 * w:0.#} mm)."));
            if (damped > 0) info.Add(Inv.F($"{damped} point(s) were pulled out less, so the path does not run across itself (tight inner corners, walls close together)."));
            if (res.Respaced > 0) info.Add(Inv.F($"{res.Respaced} time(s) the wall grew or shrank so much that the count of repeats around it changed; the pattern shifts there."));
            if (o.Droop > 0) info.Add(Inv.F($"Z Droop: the peaks are printed up to {o.Droop:0.##} mm lower, so the bead sags into loops (never closer than half a layer to the bed)."));
            if (o.ExtraFlow != 0) info.Add(Inv.F($"Extra Flow: up to {o.ExtraFlow * 100:0} % more extrusion at the peaks (the longer path already gets its extrusion by length)."));
            info.Add("Still one line: no travel moves added, the seam ramps and infill are untouched, the pattern fades out at the seam.");
            res.Info = string.Join("\n", info);
            return res;
        }

        /// <summary>The layer's points in print order with the new walls in, and whether the move to each prints.</summary>
        private static List<Tuple<Point3d, bool>> LayerPoints(Toolpath tp, List<int> idx, Dictionary<int, Tuple<Run, List<Sample>>> runs)
        {
            var list = new List<Tuple<Point3d, bool>>(idx.Count);
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                bool cont = k > 0 && idx[k - 1] == i - 1;
                if (runs.TryGetValue(i, out var nr))
                {
                    var samples = nr.Item2;
                    for (int q = 0; q < samples.Count; q++) list.Add(Tuple.Create(samples[q].Q, q == 0 ? cont && tp.Flows[i] > 0 : true));
                    while (k + 1 < idx.Count && idx[k + 1] <= nr.Item1.I1) k++;
                    continue;
                }
                list.Add(Tuple.Create(tp.Points[i], cont && tp.Flows[i] > 0));
            }
            return list;
        }

        /// <summary>Samples next to places where the layer would run across itself.</summary>
        private static List<Sample> Crossings(Toolpath tp, List<int> idx, List<Tuple<Run, List<Sample>>> layerSamples, Plane plane)
        {
            // the layer in print order, every point with the sample it came from (null = unchanged point)
            var pts = new List<Point2d>(); var from = new List<Sample>(); var prints = new List<bool>();
            var byStart = new Dictionary<int, Tuple<Run, List<Sample>>>();
            foreach (var ls in layerSamples) byStart[ls.Item1.I0] = ls;
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                bool cont = k > 0 && idx[k - 1] == i - 1;
                if (byStart.TryGetValue(i, out var nr))
                {
                    var samples = nr.Item2;
                    for (int q = 0; q < samples.Count; q++) { pts.Add(Flat(plane, samples[q].Q)); from.Add(samples[q]); prints.Add(q == 0 ? cont && tp.Flows[i] > 0 : true); }
                    while (k + 1 < idx.Count && idx[k + 1] <= nr.Item1.I1) k++;
                    continue;
                }
                pts.Add(Flat(plane, tp.Points[i])); from.Add(null); prints.Add(cont && tp.Flows[i] > 0);
            }
            var hit = new HashSet<Sample>();
            double cell = Math.Max(2 * tp.Settings.BeadWidth, 5);
            var grid = new SegGrid(cell);
            for (int e = 1; e < pts.Count; e++)
            {
                if (!prints[e]) continue;
                Point2d p = pts[e - 1], q = pts[e];
                bool mine = from[e] != null || from[e - 1] != null;
                {
                    foreach (int k in grid.Near(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y)))
                    {
                        int o = grid.Id[k];
                        if (Math.Abs(o - e) <= 1) continue;
                        if (!mine && from[o] == null && from[o - 1] == null) continue;
                        Point2d a = grid.A[k], b = grid.B[k];
                        double d1 = Cross(a, b, p), d2 = Cross(a, b, q), d3 = Cross(p, q, a), d4 = Cross(p, q, b);
                        if (d1 * d2 >= 0 || d3 * d4 >= 0) continue;
                        // runs along each other (the seam closing over its own start): not a crossing
                        Vector2d u1 = q - p, u2 = b - a;
                        if (Math.Abs(u1.X * u2.Y - u1.Y * u2.X) < 0.17 * u1.Length * u2.Length) continue;
                        for (int m = Math.Max(0, e - 2); m <= Math.Min(pts.Count - 1, e + 1); m++) if (from[m] != null) hit.Add(from[m]);
                        for (int m = Math.Max(0, o - 2); m <= Math.Min(pts.Count - 1, o + 1); m++) if (from[m] != null) hit.Add(from[m]);
                    }
                }
                grid.Add(p, q, e);
            }
            // a crossing between two unchanged moves is not ours to fix
            return hit.Where(s => s.Amp > 1e-6).ToList();
        }

        /// <summary>How much of the pattern a region allows at a sample (0..1).</summary>
        private static double RegionWeight(RegionGeo rg, Sample sm, Run r, Plane plane, Vector3d axis, double falloff, double w, double A)
        {
            Point3d p = sm.Base;
            if (rg.Mesh != null)
            {
                var box = rg.Box; box.Inflate(falloff + 2 * w + A);
                if (!box.Contains(p)) return 0;
                Point3d c = rg.Mesh.ClosestPoint(p);
                double d = c.DistanceTo(p);
                if (rg.MeshClosed)
                {
                    if (!rg.Mesh.IsPointInside(p, 0.01, false)) return 0;
                    return falloff > 0 ? Smooth(d / falloff) : 1;
                }
                // an open surface on the wall: the pattern where the wall touches it
                if (d <= w) return 1;
                return falloff > 0 ? 1 - Smooth((d - w) / falloff) : 0;
            }
            // a closed curve drawn on the part: unrolled around the wall (arc length across, height up)
            {
                var box = rg.Box; box.Inflate(falloff + 4 * w + A);
                Point3d pz = p; // test horizontally: the curve's box must reach this height and lie near the wall
                if (pz.Z < box.Min.Z || pz.Z > box.Max.Z) return 0;
                double hP = (p - plane.Origin) * axis;
                var poly = new List<Point2d>(rg.Curve.Count);
                if (r.Closed)
                {
                    Point2d f = Flat(plane, p);
                    double rx = f.X - r.C.X, ry = f.Y - r.C.Y, rad = Math.Sqrt(rx * rx + ry * ry);
                    double th = Math.Atan2(ry, rx);
                    double nearest = double.MaxValue;
                    foreach (var q in rg.Curve)
                    {
                        Point2d fq = Flat(plane, q);
                        double qx = fq.X - r.C.X, qy = fq.Y - r.C.Y;
                        double dth = Math.Atan2(qy, qx) - th;
                        while (dth > Math.PI) dth -= 2 * Math.PI;
                        while (dth < -Math.PI) dth += 2 * Math.PI;
                        double qr = Math.Sqrt(qx * qx + qy * qy);
                        nearest = Math.Min(nearest, Math.Abs(qr - rad) + Math.Abs(dth) * rad);
                        poly.Add(new Point2d(dth * rad, (q - plane.Origin) * axis - hP));
                    }
                    // the curve must lie on (or near) this wall, not on another part nearby
                    double near = Math.Max(0.25 * rad, 6 * w + A);
                    bool onThis = false;
                    foreach (var q in rg.Curve)
                    {
                        Point2d fq = Flat(plane, q);
                        double qr = Math.Sqrt((fq.X - r.C.X) * (fq.X - r.C.X) + (fq.Y - r.C.Y) * (fq.Y - r.C.Y));
                        if (Math.Abs(qr - rad) < near) { onThis = true; break; }
                    }
                    if (!onThis) return 0;
                }
                else
                {
                    // an open wall: seen square to the wall at this point
                    Vector3d t = Vector3d.CrossProduct(axis, sm.N); t.Unitize();
                    bool nearAny = false;
                    foreach (var q in rg.Curve)
                    {
                        Vector3d v = q - p;
                        if (Math.Abs(v * sm.N) < 6 * w + A + falloff) nearAny = true;
                        poly.Add(new Point2d(v * t, v * axis));
                    }
                    if (!nearAny) return 0;
                }
                var o0 = new Point2d(0, 0);
                if (!InPolygon(poly, o0)) return 0;
                if (falloff <= 0) return 1;
                double dmin = double.MaxValue;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) dmin = Math.Min(dmin, SegDist(o0, poly[j], poly[i]));
                return Smooth(dmin / falloff);
            }
        }

        private static void Finish(PatternResult res, Toolpath path, PrintSettings st)
        {
            // one polyline per layer, for the preview
            res.Preview.Clear();
            Polyline cur = null; int curLayer = int.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                if (path.Roles[i] == PathRole.Skirt || path.Roles[i] == PathRole.Travel) { if (cur != null && cur.Count > 1) res.Preview.Add(cur); cur = null; continue; }
                if (cur == null || path.Layers[i] != curLayer)
                {
                    if (cur != null && cur.Count > 1) res.Preview.Add(cur);
                    cur = new Polyline(); curLayer = path.Layers[i];
                    if (i > 0 && path.Roles[i - 1] != PathRole.Skirt && path.Roles[i - 1] != PathRole.Travel) cur.Add(path.Points[i - 1]);
                }
                cur.Add(path.Points[i]);
            }
            if (cur != null && cur.Count > 1) res.Preview.Add(cur);
        }
    }
}
