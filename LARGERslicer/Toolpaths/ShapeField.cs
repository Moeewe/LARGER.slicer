using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// How high a part has to become, over its plan: one number for every x, y. It is either a factor
    /// (1.5 = one and a half times the print height) or a height in mm. Made from points with wanted heights,
    /// from the utilisation of a structural analysis, from a profile along a direction, from a guide
    /// surface or from a file of an earlier step. PathReshape fits it to a part (limits, smoothing,
    /// material) and bends the path with it.
    /// </summary>
    public class ShapeField
    {
        /// <summary>True: Value is a height in mm above the bottom of the part; false: a factor on the print height.</summary>
        public bool IsHeight;
        /// <summary>Where the numbers come from, for the reports.</summary>
        public string Source = "constant";
        public readonly List<string> Notes = new List<string>();
        /// <summary>Corners of the data in plan (for the preview and the file).</summary>
        public double X0, Y0, X1, Y1;
        /// <summary>Smallest and largest value the field may take (0 = no limit).</summary>
        public double Lowest, Highest;
        private Func<double, double, double> _f = (x, y) => 1;

        /// <summary>The wanted factor or height at a place in plan.</summary>
        public double Value(double x, double y)
        {
            double v = _f(x, y);
            if (Lowest > 0 && v < Lowest) v = Lowest;
            if (Highest > 0 && v > Highest) v = Highest;
            return v;
        }

        /// <summary>The wanted factor for a part that is this high.</summary>
        public double Factor(double x, double y, double height) => IsHeight ? Value(x, y) / Math.Max(1e-9, height) : Value(x, y);

        public override string ToString() => Inv.F($"Shape Field ({Source}, {(IsHeight ? "height in mm" : "factor")})");

        // ------------------------------------------------------------------ sources

        public static ShapeField Constant(double factor) => new ShapeField { _f = (x, y) => factor, Source = Inv.F($"constant {factor:0.###}"), X0 = 0, Y0 = 0, X1 = 1, Y1 = 1 };

        /// <summary>Any function of x and y (for tests and scripts).</summary>
        public static ShapeField FromFunction(Func<double, double, double> f, bool isHeight, string source, double x0, double y0, double x1, double y1) =>
            new ShapeField { _f = f, IsHeight = isHeight, Source = source, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

        /// <summary>
        /// A smooth field through values at points. Points in one line give a profile along that line; otherwise a
        /// thin plate (the surface that bends least) goes through them; smooth > 0 lets it pass beside noisy
        /// values. More than 350 points are gathered in cells first. The plate never leaves the range of the values.
        /// </summary>
        public static ShapeField FromPoints(IList<Point3d> pts, IList<double> values, bool isHeight, double smooth = 0, string source = null)
        {
            if (pts == null || values == null || pts.Count == 0) throw new ArgumentException("No points.");
            if (pts.Count != values.Count) throw new ArgumentException(Inv.F($"{pts.Count} points but {values.Count} values: there must be one value for every point."));
            for (int i = 0; i < pts.Count; i++)
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || !(values[i] > 0)) throw new ArgumentException(Inv.F($"Value {i} is {values[i]}: every value must be a number above 0."));
            var fld = new ShapeField { IsHeight = isHeight, Source = source ?? Inv.F($"{pts.Count} points") };
            fld.Bounds(pts);
            double lo = values.Min(), hi = values.Max();
            double size = Math.Max(1e-6, Math.Max(fld.X1 - fld.X0, fld.Y1 - fld.Y0));
            if (pts.Count == 1 || hi - lo < 1e-12) { double v = values.Average(); fld._f = (x, y) => v; return fld; }

            // in one line?
            double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y), xx = 0, yy = 0, xy = 0;
            foreach (var p in pts) { xx += (p.X - mx) * (p.X - mx); yy += (p.Y - my) * (p.Y - my); xy += (p.X - mx) * (p.Y - my); }
            double tr = xx + yy, dt = Math.Sqrt(Math.Max(0, (xx - yy) * (xx - yy) / 4 + xy * xy)), e1 = tr / 2 + dt, e2 = Math.Max(0, tr / 2 - dt);
            if (pts.Count == 2 || e2 < 1e-6 * e1)
            {
                double ang = 0.5 * Math.Atan2(2 * xy, xx - yy), ux = Math.Cos(ang), uy = Math.Sin(ang);
                var t = pts.Select(p => (p.X - mx) * ux + (p.Y - my) * uy).ToList();
                var prof = new Profile1(t, values.ToList());
                fld._f = (x, y) => prof.At((x - mx) * ux + (y - my) * uy);
                fld.Source += " in one line";
                return fld;
            }
            var P = pts.ToList(); var V = values.ToList();
            if (P.Count > 350)
            {
                int n = 16; List<Point3d> bp; List<double> bv;
                do { Gather(P, V, size / n, 0, out bp, out bv); n = n * 4 / 5; } while (bp.Count > 350 && n > 3);
                fld.Notes.Add(Inv.F($"{P.Count} points gathered in {bp.Count} cells (mean of each)."));
                P = bp; V = bv;
            }
            var tps = new ThinPlate(P, V, Math.Max(0, smooth) / size);
            fld._f = (x, y) => Math.Max(lo, Math.Min(hi, tps.At(x, y)));
            return fld;
        }

        /// <summary>
        /// A profile along a direction: position (mm along the direction, from the start point) and value. Between the
        /// positions a smooth curve that never swings over; beyond the ends the end value.
        /// </summary>
        public static ShapeField FromProfile(Point3d start, Vector3d direction, IList<double> positions, IList<double> values, bool isHeight)
        {
            double l = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (l < 1e-9) throw new ArgumentException("The direction of a profile must run in plan (x, y).");
            if (positions == null || values == null || positions.Count != values.Count || positions.Count < 2) throw new ArgumentException("A profile needs at least two positions and as many values.");
            if (values.Any(v => double.IsNaN(v) || !(v > 0))) throw new ArgumentException("Every value of a profile must be above 0.");
            double ux = direction.X / l, uy = direction.Y / l, sx = start.X, sy = start.Y;
            var prof = new Profile1(positions.ToList(), values.ToList());
            double a = positions.Min(), b = positions.Max();
            var f = new ShapeField { IsHeight = isHeight, Source = Inv.F($"profile with {positions.Count} positions along {ux:0.##}, {uy:0.##}"), _f = (x, y) => prof.At((x - sx) * ux + (y - sy) * uy) };
            f.Bounds(new[] { new Point3d(sx + ux * a, sy + uy * a, 0), new Point3d(sx + ux * b, sy + uy * b, 0) });
            return f;
        }

        /// <summary>Heights on a regular grid, row by row (NaN = nothing there, filled from its neighbours): a sampled guide surface.</summary>
        public static ShapeField FromGrid(double x0, double y0, double cell, int nx, int ny, double[] v, bool isHeight, string source)
        {
            if (nx < 2 || ny < 2 || v == null || v.Length != nx * ny) throw new ArgumentException("The grid is too small.");
            var w = (double[])v.Clone();
            int holes = w.Count(double.IsNaN);
            if (holes == w.Length) throw new ArgumentException("The guide surface does not lie over the part: nothing was hit.");
            Fill(w, nx, ny);
            var sp = new Spline2(x0, y0, cell, nx, ny, w, true);
            var f = new ShapeField { IsHeight = isHeight, Source = source, X0 = x0, Y0 = y0, X1 = x0 + cell * (nx - 1), Y1 = y0 + cell * (ny - 1), _f = (x, y) => sp.At(x, y) };
            if (holes > 0) f.Notes.Add(Inv.F($"{100.0 * holes / w.Length:0} % of the area lies outside the guide surface and takes the height of its edge."));
            return f;
        }

        public class UtilisationOptions
        {
            /// <summary>The value that is just right: 1 for stress / allowed stress.</summary>
            public double Target = 1.0;
            /// <summary>new height = old height x (value / target) ^ exponent. Bending of a wall in its plane: stress goes with
            /// 1 / height squared, so 0.5; tension, shear or compression over the height: 1.</summary>
            public double Exponent = 0.5;
            /// <summary>1 = the full step, 0.5 = half of it (in the exponent): keeps the loop from swinging.</summary>
            public double Damping = 0.7;
            public double Min = 0.4, Max = 2.5;
            /// <summary>Values over one place in plan (several heights of the wall) are joined in cells of this size (mm, 0 = 1/40 of the part).</summary>
            public double Cell;
            /// <summary>How they are joined: true = the largest counts (safe), false = the mean.</summary>
            public bool Largest = true;
            public double Smooth;
            /// <summary>The field of the step before; null = the part as it is (factor 1).</summary>
            public ShapeField Previous;
            /// <summary>Print height of the part, needed only when Previous is in mm.</summary>
            public double Height;
        }

        /// <summary>Fully stressed design: where the value is above the target the part grows, where it is below it shrinks.</summary>
        public static ShapeField FromUtilisation(IList<Point3d> pts, IList<double> util, UtilisationOptions o)
        {
            o = o ?? new UtilisationOptions();
            if (pts == null || util == null || pts.Count == 0) throw new ArgumentException("No points.");
            if (pts.Count != util.Count) throw new ArgumentException(Inv.F($"{pts.Count} points but {util.Count} values: there must be one value for every point."));
            if (!(o.Target > 0)) throw new ArgumentException("Target must be above 0.");
            if (!(o.Min > 0) || o.Max < o.Min) throw new ArgumentException("Min Factor must be above 0 and not above Max Factor.");
            if (o.Previous != null && o.Previous.IsHeight && !(o.Height > 0)) throw new ArgumentException("The field of the step before is in mm: the print height of the part is needed too.");
            var P = new List<Point3d>(); var U = new List<double>(); int skipped = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double u = Math.Abs(util[i]); // stresses may come with a sign
                if (double.IsNaN(u) || double.IsInfinity(u)) { skipped++; continue; }
                P.Add(pts[i]); U.Add(u);
            }
            if (P.Count == 0) throw new ArgumentException("No usable value.");
            double size = Math.Max(P.Max(p => p.X) - P.Min(p => p.X), P.Max(p => p.Y) - P.Min(p => p.Y));
            Gather(P, U, o.Cell > 0 ? o.Cell : Math.Max(1e-6, size / 40), o.Largest ? 1 : 0, out var gp, out var gu);
            var v = new List<double>(); int low = 0, high = 0;
            for (int i = 0; i < gp.Count; i++)
            {
                double prev = o.Previous != null ? o.Previous.Factor(gp[i].X, gp[i].Y, o.Height) : 1;
                double f = prev * Math.Pow(Math.Max(1e-6, gu[i] / o.Target), o.Exponent * o.Damping);
                if (f < o.Min) { f = o.Min; low++; }
                if (f > o.Max) { f = o.Max; high++; }
                v.Add(f);
            }
            var fld = FromPoints(gp, v, false, o.Smooth, Inv.F($"utilisation at {P.Count} points"));
            fld.Lowest = o.Min; fld.Highest = o.Max;
            fld.Notes.Add(Inv.F($"Utilisation {U.Min():0.###} to {U.Max():0.###}, target {o.Target:0.###}, exponent {o.Exponent:0.##}, damping {o.Damping:0.##}: factor {v.Min():0.###} to {v.Max():0.###}."));
            fld.MaxUtilisation = U.Max(); fld.MinUtilisation = U.Min(); fld.MeanUtilisation = U.Average(); fld.Over = U.Count(u => u > o.Target) / (double)U.Count;
            if (low + high > 0) fld.Notes.Add(Inv.F($"{low} place(s) at Min Factor, {high} at Max Factor."));
            if (skipped > 0) fld.Notes.Add(Inv.F($"{skipped} point(s) without a number were left out."));
            return fld;
        }

        /// <summary>Of the analysis the field was made from (0 = none).</summary>
        public double MaxUtilisation, MinUtilisation, MeanUtilisation;
        /// <summary>Share of the points above the target, 0..1.</summary>
        public double Over;

        private void Bounds(IList<Point3d> pts)
        {
            X0 = pts.Min(p => p.X); X1 = pts.Max(p => p.X); Y0 = pts.Min(p => p.Y); Y1 = pts.Max(p => p.Y);
        }

        /// <summary>Joins points in cells. how 0 = mean, 1 = largest.</summary>
        private static void Gather(IList<Point3d> p, IList<double> v, double cell, int how, out List<Point3d> op, out List<double> ov)
        {
            var d = new Dictionary<long, double[]>(); // sx, sy, sv, n, max
            for (int i = 0; i < p.Count; i++)
            {
                long key = ((long)(int)Math.Floor(p[i].X / cell) << 32) ^ (uint)(int)Math.Floor(p[i].Y / cell);
                if (!d.TryGetValue(key, out var a)) d[key] = a = new double[5];
                a[0] += p[i].X; a[1] += p[i].Y; a[2] += v[i]; a[3]++; if (v[i] > a[4]) a[4] = v[i];
            }
            op = new List<Point3d>(); ov = new List<double>();
            foreach (var kv in d.OrderBy(q => q.Key)) { var a = kv.Value; op.Add(new Point3d(a[0] / a[3], a[1] / a[3], 0)); ov.Add(how == 1 ? a[4] : a[2] / a[3]); }
        }

        /// <summary>Fills NaN cells from their neighbours.</summary>
        internal static void Fill(double[] v, int nx, int ny)
        {
            for (int round = 0; round < nx + ny && v.Any(double.IsNaN); round++)
            {
                var w = (double[])v.Clone();
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        if (!double.IsNaN(v[j * nx + i])) continue;
                        double s = 0; int n = 0;
                        for (int b = -1; b <= 1; b++) for (int a = -1; a <= 1; a++)
                            {
                                int x = i + a, y = j + b;
                                if (x < 0 || y < 0 || x >= nx || y >= ny || double.IsNaN(v[y * nx + x])) continue;
                                s += v[y * nx + x]; n++;
                            }
                        if (n > 0) w[j * nx + i] = s / n;
                    }
                Array.Copy(w, v, v.Length);
            }
        }

        // ------------------------------------------------------------------ file

        /// <summary>The field as text, to go on with in the next step of the loop (Previous).</summary>
        public List<string> ToCsv(int n = 60)
        {
            double size = Math.Max(1e-6, Math.Max(X1 - X0, Y1 - Y0)), cell = size / Math.Max(4, n);
            int nx = Math.Max(2, (int)Math.Ceiling((X1 - X0) / cell) + 1), ny = Math.Max(2, (int)Math.Ceiling((Y1 - Y0) / cell) + 1);
            var l = new List<string> { Inv.F($"# LARGER.slicer shape field; {(IsHeight ? "height" : "factor")}; x0 {Inv.N(X0, 4)} y0 {Inv.N(Y0, 4)} cell {Inv.N(cell, 6)} nx {nx} ny {ny}; {Source.Replace(';', ',')}"), "x,y," + (IsHeight ? "height" : "factor") };
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { double x = X0 + i * cell, y = Y0 + j * cell; l.Add(Inv.N(x, 4) + "," + Inv.N(y, 4) + "," + Inv.N(Value(x, y), 6)); }
            return l;
        }

        public static ShapeField FromCsv(IList<string> lines)
        {
            if (lines == null || lines.Count < 5 || !lines[0].StartsWith("# LARGER.slicer shape field", StringComparison.Ordinal)) throw new ArgumentException("This is not a shape field file (Shape Field writes them).");
            var head = lines[0].Split(';');
            bool isH = head[1].Trim() == "height";
            var w = head[2].Trim().Split(' ');
            double D(int i) => double.Parse(w[i], NumberStyles.Float, Inv.C);
            int nx = int.Parse(w[7], Inv.C), ny = int.Parse(w[9], Inv.C);
            var v = new double[nx * ny];
            for (int k = 0; k < v.Length; k++) v[k] = double.Parse(lines[2 + k].Split(',')[2], NumberStyles.Float, Inv.C);
            return FromGrid(D(1), D(3), D(5), nx, ny, v, isH, "file, from " + (head.Length > 3 ? head[3].Trim() : "?"));
        }

        // ------------------------------------------------------------------ interpolation

        /// <summary>Monotone cubic through (t, v): smooth, never swings over.</summary>
        internal class Profile1
        {
            private readonly double[] _t, _v, _m;
            public Profile1(List<double> t, List<double> v)
            {
                var idx = Enumerable.Range(0, t.Count).OrderBy(i => t[i]).ToList();
                var tt = new List<double>(); var vv = new List<double>(); var cnt = new List<int>();
                foreach (int i in idx)
                {
                    if (tt.Count > 0 && t[i] - tt[tt.Count - 1] < 1e-9) { int k = tt.Count - 1; vv[k] = (vv[k] * cnt[k] + v[i]) / (cnt[k] + 1); cnt[k]++; }
                    else { tt.Add(t[i]); vv.Add(v[i]); cnt.Add(1); }
                }
                _t = tt.ToArray(); _v = vv.ToArray();
                int n = _t.Length; _m = new double[n];
                if (n < 2) return;
                var h = new double[n - 1]; var d = new double[n - 1];
                for (int k = 0; k < n - 1; k++) { h[k] = _t[k + 1] - _t[k]; d[k] = (_v[k + 1] - _v[k]) / h[k]; }
                if (n == 2) { _m[0] = _m[1] = d[0]; return; }
                for (int k = 1; k < n - 1; k++)
                {
                    if (d[k - 1] * d[k] <= 0) continue;
                    double w1 = 2 * h[k] + h[k - 1], w2 = h[k] + 2 * h[k - 1];
                    _m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k]);
                }
                _m[0] = End(h[0], h[1], d[0], d[1]); _m[n - 1] = End(h[n - 2], h[n - 3], d[n - 2], d[n - 3]);
            }
            private static double End(double h0, double h1, double d0, double d1)
            {
                double m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
                if (m * d0 <= 0) return 0;
                return d0 * d1 <= 0 && Math.Abs(m) > 3 * Math.Abs(d0) ? 3 * d0 : m;
            }
            public double At(double x)
            {
                int n = _t.Length;
                if (n == 1 || x <= _t[0]) return _v[0];
                if (x >= _t[n - 1]) return _v[n - 1];
                int k = Array.BinarySearch(_t, x); if (k < 0) k = ~k - 1; if (k > n - 2) k = n - 2;
                double h = _t[k + 1] - _t[k], s = (x - _t[k]) / h, s2 = s * s, s3 = s2 * s;
                return (2 * s3 - 3 * s2 + 1) * _v[k] + (s3 - 2 * s2 + s) * h * _m[k] + (-2 * s3 + 3 * s2) * _v[k + 1] + (s3 - s2) * h * _m[k + 1];
            }
        }

        /// <summary>Thin plate spline (dense solver; up to about 350 points). A straight slope is reproduced exactly.</summary>
        internal class ThinPlate
        {
            private readonly double[] _x, _y, _c; private readonly double _s, _mx, _my;
            public ThinPlate(IList<Point3d> p, IList<double> v, double lambda)
            {
                // double points: mean
                var m = new Dictionary<string, double[]>();
                for (int i = 0; i < p.Count; i++)
                {
                    string k = Inv.N(p[i].X, 4) + "|" + Inv.N(p[i].Y, 4);
                    if (!m.TryGetValue(k, out var a)) m[k] = a = new double[4];
                    a[0] = p[i].X; a[1] = p[i].Y; a[2] += v[i]; a[3]++;
                }
                var q = m.Values.ToList(); int n = q.Count;
                _mx = q.Average(a => a[0]); _my = q.Average(a => a[1]);
                _s = Math.Max(1e-9, q.Max(a => Math.Max(Math.Abs(a[0] - _mx), Math.Abs(a[1] - _my))));
                _x = q.Select(a => (a[0] - _mx) / _s).ToArray(); _y = q.Select(a => (a[1] - _my) / _s).ToArray();
                var A = new double[n + 3, n + 4];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++) A[i, j] = U(_x[i] - _x[j], _y[i] - _y[j]);
                    A[i, i] += lambda + 1e-10; // a small number in the diagonal: smoothing
                    A[i, n] = A[n, i] = 1; A[i, n + 1] = A[n + 1, i] = _x[i]; A[i, n + 2] = A[n + 2, i] = _y[i];
                    A[i, n + 3] = q[i][2] / q[i][3];
                }
                _c = Solve(A, n + 3);
            }
            private static double U(double dx, double dy) { double r2 = dx * dx + dy * dy; return r2 < 1e-18 ? 0 : 0.5 * r2 * Math.Log(r2); }
            public double At(double x, double y)
            {
                double a = (x - _mx) / _s, b = (y - _my) / _s; int n = _x.Length;
                double s = _c[n] + _c[n + 1] * a + _c[n + 2] * b;
                for (int i = 0; i < n; i++) s += _c[i] * U(a - _x[i], b - _y[i]);
                return s;
            }
            private static double[] Solve(double[,] a, int n)
            {
                for (int k = 0; k < n; k++)
                {
                    int p = k; double best = Math.Abs(a[k, k]);
                    for (int i = k + 1; i < n; i++) if (Math.Abs(a[i, k]) > best) { best = Math.Abs(a[i, k]); p = i; }
                    if (best < 1e-14) throw new ArgumentException("The points do not define a surface (all in one place or in one line?).");
                    if (p != k) for (int j = k; j <= n; j++) { double t = a[k, j]; a[k, j] = a[p, j]; a[p, j] = t; }
                    for (int i = k + 1; i < n; i++)
                    {
                        double f = a[i, k] / a[k, k]; if (f == 0) continue;
                        for (int j = k; j <= n; j++) a[i, j] -= f * a[k, j];
                    }
                }
                var x = new double[n];
                for (int i = n - 1; i >= 0; i--) { double s = a[i, n]; for (int j = i + 1; j < n; j++) s -= a[i, j] * x[j]; x[i] = s / a[i, i]; }
                return x;
            }
        }

        /// <summary>
        /// Cubic B-spline over a regular grid: value and slope without a jump anywhere (a jump in the slope would be
        /// a jump of the tool). Outside the grid it runs on with the slope of its edge for one cell, then level.
        /// through = true: the spline goes through the grid values; false: it passes close to them (a little
        /// smoother, and it never leaves their range or their steepest slope).
        /// </summary>
        internal class Spline2
        {
            private readonly double _x0, _y0, _c; private readonly int _nx, _ny, _mx; private readonly double[] _v;
            public Spline2(double x0, double y0, double cell, int nx, int ny, double[] v, bool through)
            {
                _x0 = x0; _y0 = y0; _c = cell; _nx = nx; _ny = ny; _mx = nx + 4; _v = new double[_mx * (ny + 4)];
                var w = (double[])v.Clone();
                if (through)
                {
                    var line = new double[Math.Max(nx, ny)];
                    for (int j = 0; j < ny; j++) { for (int i = 0; i < nx; i++) line[i] = w[j * nx + i]; Control(line, nx); for (int i = 0; i < nx; i++) w[j * nx + i] = line[i]; }
                    for (int i = 0; i < nx; i++) { for (int j = 0; j < ny; j++) line[j] = w[j * nx + i]; Control(line, ny); for (int j = 0; j < ny; j++) w[j * nx + i] = line[j]; }
                }
                for (int j = 0; j < ny + 4; j++)
                    for (int i = 0; i < _mx; i++)
                    {
                        int a = Math.Max(0, Math.Min(nx - 1, i - 2)), b = Math.Max(0, Math.Min(ny - 1, j - 2));
                        int ax = i - 2 < 0 ? i - 2 : i - 2 > nx - 1 ? i - 1 - nx : 0, by = j - 2 < 0 ? j - 2 : j - 2 > ny - 1 ? j - 1 - ny : 0;
                        double gx = ax < 0 ? w[b * nx + 1] - w[b * nx] : ax > 0 ? w[b * nx + nx - 1] - w[b * nx + nx - 2] : 0;
                        double gy = by < 0 ? w[nx + a] - w[a] : by > 0 ? w[(ny - 1) * nx + a] - w[(ny - 2) * nx + a] : 0;
                        _v[j * _mx + i] = w[b * nx + a] + gx * ax + gy * by;
                    }
            }
            /// <summary>Control values of the spline through the values (natural ends: the outermost spans are straight).</summary>
            private static void Control(double[] v, int n)
            {
                if (n < 4) return;
                // (c[i-1] + 4 c[i] + c[i+1]) / 6 = v[i]; at the ends the neighbour outside runs on straight: c = v
                var a = new double[n]; var b = new double[n]; var d = new double[n];
                b[0] = 1; d[0] = v[0]; b[n - 1] = 1; d[n - 1] = v[n - 1];
                var c = new double[n];
                for (int i = 1; i < n - 1; i++) { a[i] = 1; b[i] = 4; c[i] = 1; d[i] = 6 * v[i]; }
                for (int i = 1; i < n; i++) { double m = a[i] / b[i - 1]; b[i] -= m * c[i - 1]; d[i] -= m * d[i - 1]; }
                v[n - 1] = d[n - 1] / b[n - 1];
                for (int i = n - 2; i >= 0; i--) v[i] = (d[i] - c[i] * v[i + 1]) / b[i];
            }
            public double At(double x, double y) => At(x, y, out _, out _);
            public double At(double x, double y, out double dx, out double dy)
            {
                double u = (x - _x0) / _c, v = (y - _y0) / _c; bool ox = false, oy = false;
                if (u < -1) { u = -1; ox = true; } else if (u > _nx) { u = _nx; ox = true; }
                if (v < -1) { v = -1; oy = true; } else if (v > _ny) { v = _ny; oy = true; }
                int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
                if (i > _nx - 1) i = _nx - 1; if (j > _ny - 1) j = _ny - 1;
                double s = u - i, t = v - j;
                double b0 = (1 - s) * (1 - s) * (1 - s) / 6, b1 = (3 * s * s * s - 6 * s * s + 4) / 6, b2 = (-3 * s * s * s + 3 * s * s + 3 * s + 1) / 6, b3 = s * s * s / 6;
                double d0 = -(1 - s) * (1 - s) / 2, d1 = (3 * s * s - 4 * s) / 2, d2 = (-3 * s * s + 2 * s + 1) / 2, d3 = s * s / 2;
                double c0 = (1 - t) * (1 - t) * (1 - t) / 6, c1 = (3 * t * t * t - 6 * t * t + 4) / 6, c2 = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6, c3 = t * t * t / 6;
                double e0 = -(1 - t) * (1 - t) / 2, e1 = (3 * t * t - 4 * t) / 2, e2 = (-3 * t * t + 2 * t + 1) / 2, e3 = t * t / 2;
                double val = 0; dx = 0; dy = 0;
                for (int k = 0; k < 4; k++)
                {
                    int o = (j + 1 + k) * _mx + i + 1;
                    double p0 = _v[o], p1 = _v[o + 1], p2 = _v[o + 2], p3 = _v[o + 3];
                    double row = b0 * p0 + b1 * p1 + b2 * p2 + b3 * p3, rowD = d0 * p0 + d1 * p1 + d2 * p2 + d3 * p3;
                    double cw = k == 0 ? c0 : k == 1 ? c1 : k == 2 ? c2 : c3, ew = k == 0 ? e0 : k == 1 ? e1 : k == 2 ? e2 : e3;
                    val += cw * row; dx += cw * rowD; dy += ew * row;
                }
                dx = ox ? 0 : dx / _c; dy = oy ? 0 : dy / _c;
                return val;
            }
        }
    }
}
