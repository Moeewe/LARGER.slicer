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

        public override string Name => "non-planar (follows the part, no guide)";
        public override bool KeepLayerStep => true;

        private const int Bins = 720; // half a degree
        private readonly double _cx, _cy, _z0, _zTop, _h;
        private readonly double[] _zs; // Z of every row
        private readonly double[] _u;  // rows x Bins, u at (row, angle bin)

        /// <param name="mesh">The part, meshed.</param>
        /// <param name="layerHeight">Nominal layer height h.</param>
        /// <param name="beadWidth">Bead width w (for the warning on thick layers).</param>
        /// <param name="m">Points per contour (meridians).</param>
        public MeridianWarp(Mesh mesh, double layerHeight, double beadWidth, int m = 200)
        {
            TiltTool = false;
            _h = layerHeight;
            if (mesh == null || layerHeight <= 0) { Problem = "no part or no layer height"; return; }
            m = Math.Max(16, m);
            BoundingBox bb = mesh.GetBoundingBox(true);
            _z0 = bb.Min.Z;
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
            if (levels.Count < 3) { Problem = "the part has no closed wall to follow"; return; }
            _zTop = levels[levels.Count - 1][0].Z;
            if (bb.Max.Z - _zTop < q + 1e-3) _zTop = bb.Max.Z;
            else Notes.Add($"The closed wall ends at Z {_zTop:0.#}; the layers follow the part up to there.");

            // one centre for all heights: the mean of the contour centres
            double cx = 0, cy = 0;
            foreach (var pl in levels) { Point3d c = Centre(pl); cx += c.X; cy += c.Y; }
            _cx = cx / levels.Count; _cy = cy / levels.Count;

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
                    uj[j] = s[i, j] / lengths[j];
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
            // u must grow with Z in every column, or the mapping could not be undone
            for (int b = 0; b < Bins; b++)
            {
                _u[b] = 0; _u[(n + 1) * Bins + b] = 1;
                for (int i = 1; i < n + 2; i++)
                    if (_u[i * Bins + b] <= _u[(i - 1) * Bins + b] + 1e-9) _u[i * Bins + b] = Math.Min(1, _u[(i - 1) * Bins + b] + 1e-9);
            }
            Valid = true;
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

        public override Point3d ToFlat(Point3d p)
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

        /// <summary>What the slicer reports about the layers.</summary>
        public string Summary(double beadWidth)
        {
            string text = $"Follow the part: {LayerCount} layers, meridians {LMin:0} to {LMax:0} mm long (mean {LMean:0}), " +
                          $"layers {LMin / LayerCount:0.##} to {LMax / LayerCount:0.##} mm thick along the wall.";
            if (ThickMeridians > 0)
                text += $" On {ThickMeridians} of {Meridians.Count} meridians a layer is thicker than 0.8 x bead width ({0.8 * beadWidth:0.#} mm) along the wall: use a lower layer height or a wider bead.";
            return text;
        }
    }
}
