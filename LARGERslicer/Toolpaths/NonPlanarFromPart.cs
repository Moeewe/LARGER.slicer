using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Non-planar layers that follow the part itself, with no guide surface ("Follow the part").
    ///
    /// The part is cut into thin planar contours (a quarter of the layer height apart). Every
    /// contour is divided into M points that start on the same side of the part (aligned seams),
    /// so point j of every contour lies on one "meridian" that runs up the wall from bottom to top.
    /// Along every meridian the arc length s is measured, and u = s / L is the fraction of the
    /// way up. Layer k lies at u = k / N on every meridian: where the wall leans out and the
    /// meridian is longer, the layer comes lower, so the bead keeps about the same thickness along
    /// the wall instead of thinning out on the overhang.
    ///
    /// N = round(mean meridian length / layer height). In flat space a point keeps its X and Y,
    /// and its Z becomes z0 + u x N x layer height, so the layers are planes one layer height apart
    /// there. u is stored in a grid over (angle around the centre of the part, Z).
    ///
    /// The nozzle stays vertical (gantry printer, no A/B axes), so the bead is as high as the
    /// vertical gap between two layers - not the 3D distance, which would over-extrude on a slope.
    /// Where the wall turns nearly flat the skin term of the shell law takes over (BeadHeight, BeadLaw).
    /// </summary>
    public class MeridianWarp : Warp
    {
        public bool Valid;
        /// <summary>Why the part could not be used (when not Valid).</summary>
        public string Problem;
        /// <summary>Number of layers N and the height of the flat stack N x layer height.</summary>
        public int LayerCount;
        public double HRef;
        /// <summary>Meridian lengths (mm).</summary>
        public double LMin, LMax, LMean;
        /// <summary>Meridians whose layers are thicker than 0.8 x bead width along the wall.</summary>
        public int ThickMeridians;
        public List<string> Notes = new List<string>();
        /// <summary>The meridians in real space, for checking.</summary>
        public List<Polyline> Meridians = new List<Polyline>();

        public override string Name => FollowSurface ? "non-planar (follows the surface, no guide)" : "non-planar (follows the part, no guide)";
        public override bool KeepLayerStep => true;
        /// <summary>True = "Follow the surface": u is the way along the surface from the bottom ring
        /// to the crown (see SurfaceField) instead of the share of the meridian in a vertical plane,
        /// and the bead height follows the shell law from the lap below in the path (BeadLaw).</summary>
        public bool FollowSurface;
        public override bool UseBeadLaw => FollowSurface;
        public override double MinLoopFactor => FollowSurface ? 1.5 : 3.0;
        private SurfaceField _field;
        /// <summary>Follow the surface on a part that closes at the top (a head, not a vase with a flat top): the last lap closes the crown.</summary>
        public bool ClosedTop => FollowSurface && _field != null && !_field.FlatTop;

        private const int Bins = 720; // half a degree
        private double _cx, _cy, _z0, _zTop, _h;
        private double[] _zs; // Z of every row
        private double[] _u;  // rows x Bins, u at (row, angle bin)
        private float[] _lean; // rows x Bins, sideways step of the wall per mm of height (dr/dz)

        /// <param name="mesh">The part, meshed.</param>
        /// <param name="layerHeight">Nominal layer height h.</param>
        /// <param name="beadWidth">Bead width w (for the warning on thick layers).</param>
        /// <param name="m">Points per contour (meridians).</param>
        public MeridianWarp(Mesh mesh, double layerHeight, double beadWidth, int m = 200)
            : this(mesh, layerHeight, beadWidth, null, m) { }

        /// <summary>Follow the surface: the layers are iso-lines of the way along the surface from the
        /// bottom ring to the crown (field), one layer height apart along that way.</summary>
        public MeridianWarp(Mesh mesh, double layerHeight, double beadWidth, SurfaceField field, int m = 200)
        {
            if (field != null)
            {
                if (!field.Valid) { Problem = field.Problem ?? "the surface could not be followed"; return; }
                _field = field; FollowSurface = true;
            }
            TiltTool = false;
            if (mesh == null || layerHeight <= 0) { Problem = "no part or no layer height"; return; }
            BoundingBox bb = mesh.GetBoundingBox(true);
            double q = 0.25 * layerHeight;

            // 1. planar contours every h/4 (the slicer cuts in the middle of each step and lifts the contour by half a step)
            var cut = SliceEngine.Slice(mesh, Plane.WorldXY, q, 0.01, null, null, q);
            var levels = new List<Polyline>();
            foreach (var layer in cut)
            {
                Polyline best = null; double bestLen = 0;
                foreach (var c in layer.Contours)
                {
                    if (!Geo.Closed(c)) continue;
                    double len = c.Length;
                    if (len > bestLen) { bestLen = len; best = c; }
                }
                if (best == null) { if (levels.Count == 0) continue; break; } // the closed wall ends here
                double zc = Math.Min(best[0].Z - 0.5 * q, bb.Max.Z - 1e-4);
                var pl = new Polyline(best.Count);
                foreach (var p in best) pl.Add(new Point3d(p.X, p.Y, zc));
                if (layer.Contours.Count > 1 && Notes.Count == 0) Notes.Add("Some heights have more than one contour; only the outer one sets the layers.");
                levels.Add(pl);
            }
            // Follow the surface: where the surface lies nearly flat (under chin and nose, the crown) a
            // quarter layer height in Z moves the contour by many millimetres; then contours are added in
            // between until no point moves more than a quarter layer height from one to the next, or the
            // rings there would be placed by a straight line through a curved surface
            if (_field != null) levels = Refine(mesh, levels, q);
            Build(levels, bb.Min.Z, bb.Max.Z, layerHeight, beadWidth, m);
        }

        private static List<Polyline> Refine(Mesh mesh, List<Polyline> levels, double maxStep)
        {
            if (levels.Count < 2) return levels;
            var result = new List<Polyline>(levels.Count * 2) { levels[0] };
            int added = 0;
            for (int i = 1; i < levels.Count; i++)
            {
                Polyline a = result[result.Count - 1], b = levels[i];
                double d = Math.Max(Shift(a, b), Shift(b, a));
                int extra = Math.Min(120, (int)Math.Ceiling(d / maxStep) - 1);
                for (int k = 1; k <= extra && added < 6000; k++)
                {
                    double z = a[0].Z + (b[0].Z - a[0].Z) * k / (extra + 1);
                    Polyline c = Cut(mesh, z);
                    if (c != null) { result.Add(c); added++; }
                }
                result.Add(b);
            }
            return result;
        }

        /// <summary>How far (in plan) the points of b lie from contour a, at most.</summary>
        private static double Shift(Polyline a, Polyline b)
        {
            double worst = 0;
            int step = Math.Max(1, b.Count / 64);
            for (int i = 0; i < b.Count; i += step)
            {
                double best = double.MaxValue;
                Point3d p = b[i];
                for (int j = 1; j < a.Count; j++)
                {
                    double ax = a[j - 1].X, ay = a[j - 1].Y, ex = a[j].X - ax, ey = a[j].Y - ay;
                    double l2 = ex * ex + ey * ey;
                    double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - ax) * ex + (p.Y - ay) * ey) / l2));
                    double dx = ax + ex * t - p.X, dy = ay + ey * t - p.Y;
                    double d = dx * dx + dy * dy;
                    if (d < best) best = d;
                }
                if (best > worst) worst = best;
            }
            return Math.Sqrt(worst);
        }

        /// <summary>The longest closed contour of the mesh at height z.</summary>
        private static Polyline Cut(Mesh mesh, double z)
        {
            Plane pl = Plane.WorldXY; pl.OriginZ = z;
            Polyline[] cs = Rhino.Geometry.Intersect.Intersection.MeshPlane(mesh, pl);
            if (cs == null) return null;
            Polyline best = null; double bestLen = 0;
            foreach (var c in cs)
            {
                if (c == null || c.Count < 4 || !Geo.Closed(c)) continue;
                double len = c.Length;
                if (len > bestLen) { bestLen = len; best = c; }
            }
            if (best == null) return null;
            var r = new Polyline(best.Count);
            foreach (var p in best) r.Add(new Point3d(p.X, p.Y, z));
            return r;
        }

        /// <summary>From planar contours given by hand (closed, one per height, from bottom to top, about a
        /// quarter layer height apart) between the bottom and the top of the part.</summary>
        public MeridianWarp(List<Polyline> levels, double bottom, double top, double layerHeight, double beadWidth, int m = 200)
        {
            TiltTool = false;
            if (levels == null || layerHeight <= 0) { Problem = "no contours or no layer height"; return; }
            Build(levels, bottom, top, layerHeight, beadWidth, m);
        }

        private void Build(List<Polyline> levels, double bottom, double top, double layerHeight, double beadWidth, int m)
        {
            _h = layerHeight;
            _z0 = bottom;
            m = Math.Max(16, m);
            double q = 0.25 * layerHeight;
            if (levels.Count < 3) { Problem = "the part has no closed wall to follow"; return; }
            _zTop = levels[levels.Count - 1][0].Z;
            if (top - _zTop < q + 1e-3) _zTop = top;
            else Notes.Add(Inv.F($"The closed wall ends at Z {_zTop:0.#}; the layers follow the part up to there."));

            // one centre for all heights: the mean of the contour centres - unless it lies outside
            // some contours (a head: the small rings round the crown lie off the middle of the neck);
            // then the centre of a contour that lies inside all of them (the top one first)
            double cx = 0, cy = 0;
            foreach (var pl in levels) { Point3d c = Centre(pl); cx += c.X; cy += c.Y; }
            _cx = cx / levels.Count; _cy = cy / levels.Count;
            Func<double, double, int> outside = (x, y) => levels.Count(pl => !Inside(pl, x, y));
            int worst = outside(_cx, _cy);
            if (worst > 0)
            {
                for (int i = levels.Count - 1; i >= 0 && worst > 0; i -= Math.Max(1, levels.Count / 60))
                {
                    Point3d c = Centre(levels[i]);
                    int o = outside(c.X, c.Y);
                    if (o < worst) { worst = o; _cx = c.X; _cy = c.Y; }
                }
            }

            // 2. M points per contour, all starting on the ray from the centre in +X, counter-clockwise
            var pts = new List<Point3d[]>(levels.Count);
            bool starShaped = true;
            foreach (var pl in levels)
            {
                var d = Divide(pl, m, _cx, _cy, out bool mono);
                if (d == null) { Problem = "a contour does not go around the centre of the part"; return; }
                starShaped &= mono;
                pts.Add(d);
            }
            if (!starShaped) Notes.Add("The part is not round enough around its centre for every contour; the layers there may be uneven.");

            // 3. arc length along every meridian, from the bottom (Z0) to the top
            int n = levels.Count;
            var s = new double[n + 2, m];
            var lengths = new double[m];
            for (int j = 0; j < m; j++)
            {
                s[0, j] = 0;
                s[1, j] = pts[0][j].Z - _z0;
                for (int i = 1; i < n; i++) s[i + 1, j] = s[i, j] + pts[i][j].DistanceTo(pts[i - 1][j]);
                s[n + 1, j] = s[n, j] + Math.Max(0, _zTop - pts[n - 1][j].Z);
                lengths[j] = s[n + 1, j];
                var mer = new Polyline(n + 2) { new Point3d(pts[0][j].X, pts[0][j].Y, _z0) };
                for (int i = 0; i < n; i++) mer.Add(pts[i][j]);
                if (_zTop > pts[n - 1][j].Z + 1e-6) mer.Add(new Point3d(pts[n - 1][j].X, pts[n - 1][j].Y, _zTop));
                Meridians.Add(mer);
            }
            LMin = lengths.Min(); LMax = lengths.Max(); LMean = lengths.Average();
            if (LMin <= 1e-6) { Problem = "the part is too low"; return; }
            if (_field != null) { LMin = _field.MeridianMin; LMax = _field.MeridianMax; LMean = _field.Meridian; }
            LayerCount = Math.Max(1, (int)Math.Round(LMean / layerHeight, MidpointRounding.AwayFromZero));
            HRef = LayerCount * layerHeight;
            foreach (double L in lengths) if (L / LayerCount > 0.8 * beadWidth) ThickMeridians++;

            // 4. grid (row = Z, column = angle) -> u
            _zs = new double[n + 2];
            _zs[0] = _z0;
            for (int i = 0; i < n; i++) _zs[i + 1] = pts[i][0].Z;
            _zs[n + 1] = Math.Max(_zTop, _zs[n] + 1e-6);
            _u = new double[(n + 2) * Bins];
            var ang = new double[m];
            var uj = new double[m];
            for (int i = 0; i < n + 2; i++)
            {
                int src = Math.Max(0, Math.Min(n - 1, i - 1));
                for (int j = 0; j < m; j++)
                {
                    Point3d p = pts[src][j];
                    double a = Math.Atan2(p.Y - _cy, p.X - _cx);
                    if (a < 0) a += 2 * Math.PI;
                    ang[j] = a;
                    uj[j] = _field == null ? s[i, j] / lengths[j] : i == 0 ? 0 : i == n + 1 ? 1 : _field.At(p);
                }
                // Follow the surface: the rings are smoothed along the way round (the printed head's
                // definition: Smooth Polyline 0.5 x 30), so no ring waves with the mesh
                if (_field != null && i > 0 && i < n + 1)
                {
                    var tmp = new double[m];
                    for (int pass = 0; pass < 30; pass++)
                    {
                        for (int j = 0; j < m; j++) tmp[j] = 0.5 * uj[j] + 0.25 * (uj[(j + m - 1) % m] + uj[(j + 1) % m]);
                        Array.Copy(tmp, uj, m);
                    }
                }
                // the angle grows along the contour: unwrap it, then sample every bin
                for (int j = 1; j < m; j++) while (ang[j] < ang[j - 1] - Math.PI) ang[j] += 2 * Math.PI;
                for (int b = 0; b < Bins; b++)
                {
                    double a = (b + 0.5) * 2 * Math.PI / Bins; // centre of the bin
                    while (a < ang[0]) a += 2 * Math.PI;
                    while (a >= ang[0] + 2 * Math.PI) a -= 2 * Math.PI;
                    int lo = 0, hi = m - 1;
                    if (ang[hi] <= a) lo = hi;
                    else while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (ang[mid] <= a) lo = mid; else hi = mid; }
                    int k = lo;
                    double a0, a1, u0, u1;
                    if (k < m - 1) { a0 = ang[k]; a1 = ang[k + 1]; u0 = uj[k]; u1 = uj[k + 1]; }
                    else { a0 = ang[m - 1]; a1 = ang[0] + 2 * Math.PI; u0 = uj[m - 1]; u1 = uj[0]; }
                    double t = a1 - a0 > 1e-12 ? Math.Max(0, Math.Min(1, (a - a0) / (a1 - a0))) : 0;
                    _u[i * Bins + b] = u0 + (u1 - u0) * t;
                }
            }
            // how far the wall steps sideways per mm of height (dr/dz) at every row and angle: the skin term
            // of the shell law where the wall turns flat (BeadHeight)
            _lean = new float[(n + 2) * Bins];
            var lj = new double[m];
            for (int i = 0; i < n + 2; i++)
            {
                int c = Math.Max(0, Math.Min(n - 1, i - 1)), c2 = c > 0 ? c - 1 : Math.Min(n - 1, c + 1);
                for (int j = 0; j < m; j++)
                {
                    // sideways: the distance in plan to the neighbouring contour (not to its j-th point, which
                    // may have slid along the wall)
                    Point3d a = pts[c][j];
                    double dzz = Math.Abs(a.Z - pts[c2][0].Z), dxy = double.MaxValue;
                    var o = pts[c2];
                    for (int k = 0; k < m; k++)
                    {
                        Point3d s0 = o[k], s1 = o[(k + 1) % m];
                        double ex = s1.X - s0.X, ey = s1.Y - s0.Y, l2 = ex * ex + ey * ey;
                        double tt0 = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((a.X - s0.X) * ex + (a.Y - s0.Y) * ey) / l2));
                        double qx = s0.X + ex * tt0 - a.X, qy = s0.Y + ey * tt0 - a.Y;
                        double d2 = qx * qx + qy * qy;
                        if (d2 < dxy) dxy = d2;
                    }
                    dxy = Math.Sqrt(dxy);
                    lj[j] = dzz > 1e-9 ? Math.Min(50.0, dxy / dzz) : 0;
                    double an = Math.Atan2(a.Y - _cy, a.X - _cx);
                    if (an < 0) an += 2 * Math.PI;
                    ang[j] = an;
                }
                for (int j = 1; j < m; j++) while (ang[j] < ang[j - 1] - Math.PI) ang[j] += 2 * Math.PI;
                for (int bIx = 0; bIx < Bins; bIx++)
                {
                    double a = (bIx + 0.5) * 2 * Math.PI / Bins;
                    while (a < ang[0]) a += 2 * Math.PI;
                    while (a >= ang[0] + 2 * Math.PI) a -= 2 * Math.PI;
                    int lo = 0, hi = m - 1;
                    if (ang[hi] <= a) lo = hi;
                    else while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (ang[mid] <= a) lo = mid; else hi = mid; }
                    double l0 = lj[lo], l1 = lj[lo < m - 1 ? lo + 1 : 0];
                    double a0 = ang[lo], a1 = lo < m - 1 ? ang[lo + 1] : ang[0] + 2 * Math.PI;
                    double tt = a1 - a0 > 1e-12 ? Math.Max(0, Math.Min(1, (a - a0) / (a1 - a0))) : 0;
                    _lean[i * Bins + bIx] = (float)(l0 + (l1 - l0) * tt);
                }
            }
            // u must grow with Z in every column, or the mapping could not be undone
            for (int b = 0; b < Bins; b++)
            {
                _u[b] = 0; _u[(n + 1) * Bins + b] = 1;
                for (int i = 1; i < n + 2; i++)
                    if (_u[i * Bins + b] <= _u[(i - 1) * Bins + b] + 1e-9) _u[i * Bins + b] = Math.Min(1, _u[(i - 1) * Bins + b] + 1e-9);
            }
            Valid = true;
        }

        /// <summary>Point in polygon (XY), crossing rule.</summary>
        private static bool Inside(Polyline pl, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = pl.Count - 1; i < pl.Count; j = i++)
            {
                if ((pl[i].Y > y) != (pl[j].Y > y) &&
                    x < (pl[j].X - pl[i].X) * (y - pl[i].Y) / (pl[j].Y - pl[i].Y) + pl[i].X)
                    inside = !inside;
            }
            return inside;
        }

        private static Point3d Centre(Polyline pl)
        {
            // area centre of the closed polygon in XY
            double a = 0, x = 0, y = 0;
            for (int i = 0; i < pl.Count - 1; i++)
            {
                double c = pl[i].X * pl[i + 1].Y - pl[i + 1].X * pl[i].Y;
                a += c; x += (pl[i].X + pl[i + 1].X) * c; y += (pl[i].Y + pl[i + 1].Y) * c;
            }
            if (Math.Abs(a) < 1e-9) { var b = new BoundingBox(pl); return b.Center; }
            return new Point3d(x / (3 * a), y / (3 * a), pl[0].Z);
        }

        /// <summary>M points at equal distances along the contour, counter-clockwise, the first one on the
        /// ray from the centre in +X. mono = the angle around the centre grows all the way round.</summary>
        private static Point3d[] Divide(Polyline pl, int m, double cx, double cy, out bool mono)
        {
            mono = true;
            var p = new List<Point3d>(pl);
            double area = 0;
            for (int i = 0; i < p.Count - 1; i++) area += p[i].X * p[i + 1].Y - p[i + 1].X * p[i].Y;
            if (area < 0) p.Reverse();
            // where the contour crosses the ray y = cy, x > cx (the one furthest out if there are several)
            int seg = -1; double tBest = 0, xBest = double.MinValue;
            for (int i = 0; i < p.Count - 1; i++)
            {
                double y0 = p[i].Y - cy, y1 = p[i + 1].Y - cy;
                if ((y0 < 0) == (y1 < 0)) continue;
                double t = y0 / (y0 - y1);
                double x = p[i].X + (p[i + 1].X - p[i].X) * t;
                if (x > cx && x > xBest) { xBest = x; seg = i; tBest = t; }
            }
            if (seg < 0) return null;
            var ring = new List<Point3d>(p.Count + 1) { p[seg] + (p[seg + 1] - p[seg]) * tBest };
            for (int k = 1; k < p.Count; k++) ring.Add(p[(seg + k) % (p.Count - 1)]);
            ring.Add(ring[0]);
            double total = 0;
            var acc = new double[ring.Count];
            for (int i = 1; i < ring.Count; i++) { total += ring[i].DistanceTo(ring[i - 1]); acc[i] = total; }
            if (total <= 1e-9) return null;
            var result = new Point3d[m];
            int at = 1;
            double prevA = 0;
            for (int j = 0; j < m; j++)
            {
                double target = total * j / m;
                while (at < ring.Count - 1 && acc[at] < target) at++;
                double l = acc[at] - acc[at - 1];
                double t = l > 1e-12 ? (target - acc[at - 1]) / l : 0;
                result[j] = ring[at - 1] + (ring[at] - ring[at - 1]) * t;
                double a = Math.Atan2(result[j].Y - cy, result[j].X - cx);
                if (j > 0)
                {
                    double d = a - prevA;
                    while (d > Math.PI) d -= 2 * Math.PI;
                    while (d <= -Math.PI) d += 2 * Math.PI;
                    if (d < 0) mono = false;
                }
                prevA = a;
            }
            return result;
        }

        private double Angle(double x, double y)
        {
            double a = Math.Atan2(y - _cy, x - _cx);
            if (a < 0) a += 2 * Math.PI;
            return a;
        }

        /// <summary>u in one row at an angle (linear between two bins).</summary>
        private double URow(int row, int b0, int b1, double t) => _u[row * Bins + b0] * (1 - t) + _u[row * Bins + b1] * t;

        private void Bin(double x, double y, out int b0, out int b1, out double t)
        {
            double f = Angle(x, y) / (2 * Math.PI) * Bins - 0.5;
            if (f < 0) f += Bins;
            b0 = (int)Math.Floor(f) % Bins; b1 = (b0 + 1) % Bins; t = f - Math.Floor(f);
        }

        /// <summary>This much above the bottom stays flat: the first layer lies flat on the bed at its own
        /// height (Follow the surface: also the second, as on the printed head). Above it the layers turn
        /// into the bent ones within one layer height. Slice sets it.</summary>
        public double FlatBand;

        public override Point3d ToFlat(Point3d p)
        {
            Point3d g = GridFlat(p);
            if (!Valid || FlatBand <= 0) return g;
            double z = p.Z - _z0;
            if (z <= FlatBand) return p;
            double s = Math.Min(1.0, (z - FlatBand) / _h);
            return s >= 1.0 ? g : new Point3d(p.X, p.Y, p.Z * (1 - s) + g.Z * s);
        }

        private Point3d GridFlat(Point3d p)
        {
            if (!Valid) return p;
            if (p.Z <= _z0) return p;
            if (p.Z >= _zTop) return new Point3d(p.X, p.Y, _z0 + HRef + (p.Z - _zTop));
            Bin(p.X, p.Y, out int b0, out int b1, out double t);
            int lo = 0, hi = _zs.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_zs[mid] <= p.Z) lo = mid; else hi = mid; }
            double dz = _zs[hi] - _zs[lo];
            double v = dz > 1e-12 ? (p.Z - _zs[lo]) / dz : 0;
            double u = URow(lo, b0, b1, t) * (1 - v) + URow(hi, b0, b1, t) * v;
            return new Point3d(p.X, p.Y, _z0 + u * HRef);
        }

        public override Point3d ToReal(Point3d f, out Vector3d normal)
        {
            normal = Vector3d.ZAxis; // the nozzle stays vertical
            Point3d r = GridReal(f);
            if (!Valid || FlatBand <= 0) return r;
            if (f.Z - _z0 <= FlatBand) return f;
            if (r.Z - _z0 >= FlatBand + _h && GridFlat(new Point3d(f.X, f.Y, _z0 + FlatBand + _h)).Z <= f.Z) return r;
            // in the band where the flat layers turn into the bent ones: undo ToFlat by bisection
            double lo = _z0 + FlatBand, hi = Math.Max(r.Z, _z0 + FlatBand + _h) + _h;
            for (int it = 0; it < 50 && ToFlat(new Point3d(f.X, f.Y, hi)).Z < f.Z; it++) hi += _h;
            for (int it = 0; it < 60; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (ToFlat(new Point3d(f.X, f.Y, mid)).Z < f.Z) lo = mid; else hi = mid;
            }
            return new Point3d(f.X, f.Y, 0.5 * (lo + hi));
        }

        private Point3d GridReal(Point3d f)
        {
            if (!Valid) return f;
            double uf = (f.Z - _z0) / HRef;
            if (uf <= 0) return f;
            if (uf >= 1) return new Point3d(f.X, f.Y, _zTop + (f.Z - _z0 - HRef));
            Bin(f.X, f.Y, out int b0, out int b1, out double t);
            int lo = 0, hi = _zs.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (URow(mid, b0, b1, t) <= uf) lo = mid; else hi = mid; }
            double ua = URow(lo, b0, b1, t), ub = URow(hi, b0, b1, t);
            double v = ub - ua > 1e-12 ? (uf - ua) / (ub - ua) : 0;
            return new Point3d(f.X, f.Y, _zs[lo] + (_zs[hi] - _zs[lo]) * v);
        }

        /// <summary>Vertical gap to the layer below at this place, divided by the layer height: with a vertical
        /// nozzle the bead is as high as that gap.</summary>
        public override double Scale(Point3d flat)
        {
            if (!Valid) return 1.0;
            double top = ToReal(flat, out _).Z;
            double below = ToReal(new Point3d(flat.X, flat.Y, flat.Z - _h), out _).Z;
            double s = (top - below) / _h;
            return s > 1e-6 ? s : 1e-6;
        }

        /// <summary>Follow the part: the vertical gap (as Scale), and where the wall turns nearly flat the skin term
        /// of the shell law, the sideways step to the layer below x skin thickness / bead width (see BeadLaw).
        /// Follow the surface sets its heights from the path itself afterwards (BeadLaw.Apply).</summary>
        public override double BeadHeight(Point3d flat, double flatHeight, PrintSettings st)
        {
            double dz = flatHeight * Scale(flat);
            if (!Valid || FollowSurface || _lean == null || st == null || st.BeadWidth <= 0) return dz;
            Point3d r = ToReal(flat, out _);
            if (r.Z - _z0 <= FlatBand + 1e-6) return dz;
            double lean = LeanAt(r);
            double t = Math.Max(dz, dz * lean * st.EffectiveSkin / st.BeadWidth);
            return Math.Min(t, Math.Max(dz, 3.0 * st.LayerHeight));
        }

        /// <summary>dr/dz of the wall at a real point (linear between rows and angle bins).</summary>
        private double LeanAt(Point3d p)
        {
            if (p.Z <= _z0 || p.Z >= _zTop) return 0;
            Bin(p.X, p.Y, out int b0, out int b1, out double t);
            int lo = 0, hi = _zs.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_zs[mid] <= p.Z) lo = mid; else hi = mid; }
            double dz = _zs[hi] - _zs[lo];
            double v = dz > 1e-12 ? (p.Z - _zs[lo]) / dz : 0;
            double a = _lean[lo * Bins + b0] * (1 - t) + _lean[lo * Bins + b1] * t;
            double b = _lean[hi * Bins + b0] * (1 - t) + _lean[hi * Bins + b1] * t;
            return a * (1 - v) + b * v;
        }

        /// <summary>What the slicer reports about the layers.</summary>
        public string Summary(double beadWidth)
        {
            if (FollowSurface)
                return Inv.F($"Follow the surface: {LayerCount} laps from the bottom ring to the crown, the way along the surface {LMin:0} to {LMax:0} mm (mean {LMean:0}); ") +
                       "the bead height follows the shell law (the vertical gap to the lap below; where the laps step mostly sideways, the sideways step x skin thickness / bead width).";
            string text = Inv.F($"Follow the part: {LayerCount} layers, meridians {LMin:0} to {LMax:0} mm long (mean {LMean:0}), ") +
                          Inv.F($"layers {LMin / LayerCount:0.##} to {LMax / LayerCount:0.##} mm thick along the wall; the bead height is the vertical gap, or the sideways step x skin thickness / bead width where the wall turns flat.");
            if (ThickMeridians > 0)
                text += Inv.F($" On {ThickMeridians} of {Meridians.Count} meridians a layer is thicker than 0.8 x bead width ({0.8 * beadWidth:0.#} mm) along the wall: use a lower layer height or a wider bead.");
            return text;
        }
    }
}
