using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Reliefs for the mill: models and panels with an architectural background. Every pattern is a height
    /// 0..1 over the panel (no undercuts), put on a base that may be wavy.
    /// </summary>
    public static class ReliefPatterns
    {
        public static readonly string[] Names =
        {
            "Site model (terraces, streets, houses)", "Facade (splayed windows)", "Flowing scales", "Carved flow (long gouges)", "Carved flow (short gouges)",
            "Voronoi", "Diamond facets", "Gyroid", "Labyrinth", "Flowing flutes", "Cross vaults", "Star lattice", "Topography",
        };

        public const string Help = "0 = site model with terraces, streets and houses, 1 = facade with splayed windows, 2 = scales that follow a flow and change their size, 3 = long carved gouges along a flow, 4 = short carved gouges, 5 = Voronoi cells, 6 = diamond facets (folded pyramids), 7 = gyroid (a slice through a minimal surface), 8 = labyrinth, 9 = flowing flutes, 10 = cross vaults with ribs, 11 = star lattice, 12 = topography";

        // size of a cell and bend of the panel that suit the pattern on a panel of 240 x 160 mm with a 6 mm ball
        private static readonly double[] Sizes = { 26, 40, 24, 15, 17, 34, 40, 72, 28, 24, 54, 80, 100 };
        private static readonly double[] Waves = { 0, 0, 0.45, 0.2, 0.25, 0.5, 0, 0.4, 0.5, 0, 0, 0, 0 };
        public static double DefaultSize(int kind) => kind >= 0 && kind < Sizes.Length ? Sizes[kind] : 30;
        public static double DefaultWave(int kind) => kind >= 0 && kind < Waves.Length ? Waves[kind] : 0;

        /// <param name="kind">Index into Names.</param>
        /// <param name="length">Size in X in mm.</param>
        /// <param name="width">Size in Y in mm.</param>
        /// <param name="height">Height of the whole relief in mm.</param>
        /// <param name="size">Size of one cell, scale or house in mm, 0 = what suits the pattern.</param>
        /// <param name="ball">Diameter of the finishing ball in mm: valleys and inner corners are rounded so that the ball reaches every place. 0 = leave them sharp.</param>
        /// <param name="wave">0 = flat panel, 1 = the panel itself is as wavy as the pattern is high, negative = what suits the pattern.</param>
        public static Mesh Make(int kind, double length, double width, double height, double size, double wave, int seed = 1, double ball = 0)
        {
            if (kind < 0 || kind >= Names.Length || !(length > 0) || !(width > 0) || !(height > 0)) return null;
            if (!(size > 0)) size = DefaultSize(kind);
            if (wave < 0) wave = DefaultWave(kind);
            size = Math.Max(2.0, Math.Min(size, Math.Min(length, width)));
            wave = Math.Max(0, Math.Min(3, wave));
            var f = Pattern(kind, length, width, size, seed);
            double d = Math.Max(0.4, Math.Sqrt(length * width / 400000.0));
            int nx = Math.Max(2, (int)Math.Ceiling(length / d)), ny = Math.Max(2, (int)Math.Ceiling(width / d));
            var z = new double[(nx + 1) * (ny + 1)];
            double lo = double.MaxValue, hi = double.MinValue;
            System.Threading.Tasks.Parallel.For(0, ny + 1, j =>
            {
                for (int i = 0; i <= nx; i++)
                {
                    double x = length * i / nx, y = width * j / ny;
                    double b = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (x / length * 1.1 + 0.15) + 0.9 * Math.Sin(2 * Math.PI * y / width * 0.7)) * Math.Cos(Math.PI * (y / width * 0.9 - 0.2));
                    z[j * (nx + 1) + i] = f(x, y) + wave * b;
                }
            });
            foreach (double v in z) { if (v < lo) lo = v; if (v > hi) hi = v; }
            double s = hi - lo > 1e-9 ? height / (hi - lo) : 0;
            for (int i = 0; i < z.Length; i++) z[i] = (z[i] - lo) * s;
            if (ball > 0.2) z = Reachable(z, nx, ny, length / nx, width / ny, 0.5 * ball + 0.05);
            var m = new Mesh();
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) m.Vertices.Add(length * i / nx, width * j / ny, z[j * (nx + 1) + i]);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    m.Faces.AddFace(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i);
            m.Normals.ComputeNormals();
            return m;
        }

        /// <summary>What a ball of this radius leaves when it rolls over the surface: first its lowest way (middle of the ball), then its underside.</summary>
        private static double[] Reachable(double[] z, int nx, int ny, double dx, double dy, double r)
        {
            int w = nx + 1, h = ny + 1, kx = (int)Math.Ceiling(r / dx), ky = (int)Math.Ceiling(r / dy);
            var ox = new List<int>(); var oy = new List<int>(); var up = new List<double>();
            for (int b = -ky; b <= ky; b++)
                for (int a = -kx; a <= kx; a++)
                {
                    double q = r * r - a * dx * a * dx - b * dy * b * dy;
                    if (q >= 0) { ox.Add(a); oy.Add(b); up.Add(Math.Sqrt(q)); }
                }
            int n = ox.Count;
            var c = new double[z.Length]; var res = new double[z.Length];
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double best = double.MinValue;
                    for (int k = 0; k < n; k++)
                    {
                        int u = x + ox[k], v = y + oy[k];
                        if (u < 0 || v < 0 || u >= w || v >= h) continue;
                        double q = z[v * w + u] + up[k];
                        if (q > best) best = q;
                    }
                    c[y * w + x] = best;
                }
            });
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double best = double.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        int u = x + ox[k], v = y + oy[k];
                        if (u < 0 || v < 0 || u >= w || v >= h) continue;
                        double q = c[v * w + u] - up[k];
                        if (q < best) best = q;
                    }
                    res[y * w + x] = Math.Max(z[y * w + x], best);
                }
            });
            return res;
        }

        private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        private static double Smooth(double a, double b, double v) { double t = Clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); }
        private static double Hash(int i, int j, int k)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + j * 668265263 + k * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        private static Func<double, double, double> Pattern(int kind, double L, double W, double a, int seed)
        {
            switch (kind)
            {
                case 0: return Site(L, W, a, seed);
                case 1:
                    {
                        // one window per cell; towards a point the openings grow and the light comes from another side
                        int cx = Math.Max(1, (int)Math.Round(L / a)), cy = Math.Max(1, (int)Math.Round(W / (1.35 * a)));
                        double sx = L / cx, sy = W / cy;
                        return (x, y) =>
                        {
                            int i = Math.Min(cx - 1, (int)(x / sx)), j = Math.Min(cy - 1, (int)(y / sy));
                            double u = x / sx - i - 0.5, v = y / sy - j - 0.5;                 // -0.5 .. 0.5 in the cell
                            double mx = (i + 0.5) / cx - 0.72, my = (j + 0.5) / cy - 0.62;
                            double q = Math.Exp(-(mx * mx + my * my) * 5.0);                   // 1 = at the point
                            double w = 0.17 + 0.11 * q, h = 0.21 + 0.11 * q;                    // half the glass
                            double ox = (0.09 - 0.18 * Hash(i, j, 3)) * (1 - 0.5 * q), oy = 0.05 - 0.10 * Hash(i, j, 5);
                            double du = Math.Abs(u - ox) - w, dv = Math.Abs(v - oy) - h;
                            // the reveal: from the glass to the edge of the cell, steeper on the near side
                            double ru = 0.46 - (u > ox ? ox : -ox) - w, rv = 0.46 - (v > oy ? oy : -oy) - h;
                            double t = Math.Max(du / ru, dv / rv);
                            if (t <= 0) return 0.0;                                             // glass
                            return 0.10 + 0.90 * Clamp(t);
                        };
                    }
                case 2: return Scales(L, W, a);
                case 3: return Carved(L, W, a, 4.2, seed, true);
                case 4: return Carved(L, W, a, 2.0, seed, false);
                case 5: return Voronoi(L, W, a, seed);
                case 6:
                    {
                        int cx = Math.Max(1, (int)Math.Round(L / a)), cy = Math.Max(1, (int)Math.Round(W / (0.8 * a)));
                        double sx = L / cx, sy = W / cy;
                        return (x, y) =>
                        {
                            int j = Math.Min(cy - 1, (int)(y / sy));
                            double off = (j % 2) * 0.5, xi = x / sx + off; int i = (int)Math.Floor(xi);
                            double u = xi - i, v = y / sy - j;
                            double px = (i + 0.5 - off) / cx - 0.3, py = (j + 0.5) / cy - 0.6;
                            double q = Math.Exp(-(px * px + py * py) * 6);
                            double ax = 0.5 + 0.28 * Math.Sin(i * 0.9 + j * 1.7), ay = 0.5 + 0.26 * Math.Cos(i * 1.3 - j * 0.8);
                            double t = Math.Min(Math.Min(u / ax, (1 - u) / (1 - ax)), Math.Min(v / ay, (1 - v) / (1 - ay)));
                            return (0.5 + 0.5 * q) * Clamp(t);
                        };
                    }
                case 7:
                    {
                        double k = 2 * Math.PI / a;
                        // heights where the gyroid cuts the panel: smooth saddles with holes
                        return (x, y) =>
                        {
                            double best = 0;
                            for (int n = 24; n >= 0; n--)
                            {
                                double z = n / 24.0 * 0.5 * a;
                                double g = Math.Sin(k * x) * Math.Cos(k * y) + Math.Sin(k * y) * Math.Cos(k * z) + Math.Sin(k * z) * Math.Cos(k * x);
                                if (Math.Abs(g) < 0.42)
                                {
                                    // the exact upper edge between this slice and the one above
                                    double lo = z, hi = Math.Min(0.5 * a, z + 0.5 * a / 24);
                                    for (int it = 0; it < 10; it++)
                                    {
                                        double mid = 0.5 * (lo + hi);
                                        double gm = Math.Sin(k * x) * Math.Cos(k * y) + Math.Sin(k * y) * Math.Cos(k * mid) + Math.Sin(k * mid) * Math.Cos(k * x);
                                        if (Math.Abs(gm) < 0.42) lo = mid; else hi = mid;
                                    }
                                    best = lo / (0.5 * a); break;
                                }
                            }
                            return best;
                        };
                    }
                case 8:
                    {
                        // waves of one length from many directions make a labyrinth like coral
                        const int n = 24; var dx = new double[n]; var dy = new double[n]; var ph = new double[n];
                        for (int i = 0; i < n; i++) { double t = 2 * Math.PI * (i + 0.35 * Hash(i, seed, 1)) / n; dx[i] = 2 * Math.PI / a * Math.Cos(t); dy[i] = 2 * Math.PI / a * Math.Sin(t); ph[i] = 2 * Math.PI * Hash(i, seed, 2); }
                        return (x, y) =>
                        {
                            double s = 0; for (int i = 0; i < n; i++) s += Math.Cos(dx[i] * x + dy[i] * y + ph[i]);
                            return Smooth(-0.9, 0.9, s / Math.Sqrt(n / 2.0));
                        };
                    }
                case 9:
                    return (x, y) =>
                    {
                        // flutes that are pushed aside by two eddies, deeper where they are close
                        double wx = x / L, wy = y / W;
                        double p1 = 0.55 * Math.Exp(-((wx - 0.3) * (wx - 0.3) + (wy - 0.35) * (wy - 0.35)) * 9), p2 = 0.6 * Math.Exp(-((wx - 0.74) * (wx - 0.74) + (wy - 0.7) * (wy - 0.7)) * 8);
                        double s = (y + 0.10 * W * Math.Sin(2.4 * Math.PI * wx) + 0.36 * W * (p1 - p2)) / a;
                        double t = s - Math.Floor(s);
                        double flute = Math.Sqrt(Clamp(1 - 4 * (t - 0.5) * (t - 0.5)));   // round rod
                        return (0.45 + 0.55 * Math.Sin(Math.PI * wx) * (0.6 + 0.4 * Math.Cos(2 * Math.PI * wy))) * flute;
                    };
                case 10:
                    {
                        int cx = Math.Max(1, (int)Math.Round(L / a)), cy = Math.Max(1, (int)Math.Round(W / a));
                        double sx = L / cx, sy = W / cy;
                        return (x, y) =>
                        {
                            int i = Math.Min(cx - 1, (int)(x / sx)), j = Math.Min(cy - 1, (int)(y / sy));
                            double u = 2 * (x / sx - i) - 1, v = 2 * (y / sy - j) - 1;
                            double au = Math.Abs(u), av = Math.Abs(v);
                            // two barrels cross: the higher one shows; pointed arches (two arcs that meet in the ridge)
                            Func<double, double> arch = q => (Math.Sqrt(Math.Max(0, 2.25 - (q + 0.5) * (q + 0.5))) ) / Math.Sqrt(2.0);
                            double h = Math.Max(arch(au), arch(av));
                            double groin = Math.Abs(au - av), rim = 1 - Math.Max(au, av);
                            h += 0.07 * (1 - Smooth(0.03, 0.055, groin)) + 0.07 * (1 - Smooth(0.03, 0.055, rim));   // ribs
                            double r = Math.Sqrt(u * u + v * v);
                            return h + 0.10 * (1 - Smooth(0.10, 0.14, r));                                           // boss
                        };
                    }
                case 11: return Lattice(a);
                default:
                    {
                        double sx = 240.0 / L, sy = 160.0 / W;
                        return (x0, y0) =>
                        {
                            double x = x0 * sx, y = y0 * sy, river = y - 78 - 24 * Math.Sin(x / 38.0) - 6 * Math.Sin(x / 13.0);
                            double z = 5.5 * Math.Sin(x / 31.0 + 0.6) * Math.Cos(y / 36.0) + 2.6 * Math.Sin((x + 1.7 * y) / 21.0) + 1.0 * Math.Cos((2 * x - y) / 14.0);
                            z += 9 * Math.Exp(-((x - 180) * (x - 180) + (y - 122) * (y - 122)) / 900.0) + 7 * Math.Exp(-((x - 62) * (x - 62) + (y - 34) * (y - 34)) / 700.0);
                            return (z - 8 * Math.Exp(-river * river / 150.0)) / 22.0;
                        };
                    }
            }
        }

        /// <summary>A hillside in terraces, two streets and a river through it, houses with gable roofs and courtyard blocks.</summary>
        private static Func<double, double, double> Site(double L, double W, double a, int seed)
        {
            double e = Math.Max(0.5, 0.03 * a);
            Func<double, double, double> hill = (x, y) => 0.5 + 0.30 * Math.Sin(2.6 * x / L + 0.5) * Math.Cos(2.2 * y / W - 0.9) + 0.30 * (y / W - 0.5) + 0.14 * Math.Sin(3.1 * (x / L + 1.3 * y / W));
            Func<double, double, double> ground = (x, y) => Math.Round(hill(x, y) * 8) / 8 * 0.85;
            Func<double, double> streetY = x => 0.40 * W + 0.10 * W * Math.Sin(2.2 * x / L * Math.PI * 0.6 + 0.3);
            Func<double, double> streetX = y => 0.62 * L - 0.12 * L * Math.Sin(1.5 * y / W);
            double street = 0.30 * a;
            int nx = Math.Max(1, (int)(L / a)), ny = Math.Max(1, (int)(W / a));
            double px = L / nx, py = W / ny;
            var houses = new List<double[]>();      // cx, cy, half length, half width, angle, base, storeys, kind
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double cx = (i + 0.5) * px + (Hash(i, j, seed + 11) - 0.5) * 0.12 * a, cy = (j + 0.5) * py + (Hash(i, j, seed + 12) - 0.5) * 0.12 * a;
                    double hl = (0.30 + 0.07 * Hash(i, j, seed + 13)) * a, hw = (0.20 + 0.05 * Hash(i, j, seed + 14)) * a, r = hw + 0.02 * a;
                    if (cx < 0.45 * a || cy < 0.45 * a || cx > L - 0.45 * a || cy > W - 0.45 * a) continue;
                    if (Math.Abs(cy - streetY(cx)) < r + 0.5 * street || Math.Abs(cx - streetX(cy)) < r + 0.10 * a + 0.5 * street) continue;
                    if (Hash(i, j, seed + 15) < 0.06) continue;
                    double slope = Math.Atan2(streetY(cx + 1) - streetY(cx - 1), 2);
                    int kind = Hash(i, j, seed + 16) < 0.22 ? 1 : 0;
                    houses.Add(new[] { cx, cy, kind == 1 ? 0.42 * a : hl, kind == 1 ? 0.40 * a : hw, slope + (Hash(i, j, seed + 17) < 0.5 || kind == 1 ? 0 : 0.5 * Math.PI), ground(cx, cy), 0.16 + 0.14 * Hash(i, j, seed + 18), kind });
                }
            return (x, y) =>
            {
                double z = ground(x, y);
                double s = Math.Min(Math.Abs(y - streetY(x)), Math.Abs(x - streetX(y)));
                if (s < 0.5 * street) z = Math.Min(z, hill(x, y) * 0.85 - 0.02);              // a street runs over the terraces as a ramp
                else if (s < 0.5 * street + e) { double t = (s - 0.5 * street) / e; z = t * z + (1 - t) * Math.Min(z, hill(x, y) * 0.85 - 0.02); }
                foreach (var h in houses)
                {
                    double dx = x - h[0], dy = y - h[1], c = Math.Cos(h[4]), sn = Math.Sin(h[4]);
                    double u = Math.Abs(c * dx + sn * dy), v = Math.Abs(-sn * dx + c * dy);
                    if (u > h[2] + e || v > h[3] + e) continue;
                    double outside = Math.Max(u - h[2], v - h[3]);
                    double top;
                    if (h[7] > 0.5)
                    {
                        // a block round a courtyard, flat roof
                        double inside = Math.Min(h[2] - u, h[3] - v), depth = 0.15 * a;
                        if (inside > depth + e) continue;
                        top = h[5] + h[6];
                        double wall = inside > depth ? 1 - (inside - depth) / e : outside > 0 ? 1 - outside / e : 1;
                        z = Math.Max(z, z + (top - z) * wall);
                    }
                    else
                    {
                        double roof = h[6] * 0.75 + 0.12 * (1 - Math.Min(1, v / h[3]));          // gable
                        top = h[5] + roof;
                        double wall = outside > 0 ? 1 - outside / e : 1;
                        z = Math.Max(z, z + (top - z) * wall);
                    }
                }
                return z;
            };
        }

        /// <summary>
        /// Coordinates that follow a flow: along (u) and across (v) the stream lines, in mm, and how much longer a step
        /// is there than on the panel. The map is conformal (a straight flow and the pull of two whirls that lie outside
        /// the panel), so cells keep their shape and change their size.
        /// </summary>
        private sealed class Flow
        {
            private readonly double _m, _cx1, _cy1, _cx2, _cy2, _k1, _k2, _ca, _sa, _norm;
            public Flow(double L, double W)
            {
                _m = Math.Max(L, W);
                double l = L / _m, w = W / _m;
                _cx1 = 0.9 * l; _cy1 = -0.2; _cx2 = 0.1 * l; _cy2 = w + 0.33; _k1 = -0.4; _k2 = -0.2;   // the flow turns by 125 degrees, the largest cell is three times the smallest
                double alpha = 2.0; _ca = Math.Cos(alpha); _sa = Math.Sin(alpha);
                _norm = 1;
                double sum = 0; int n = 0;
                for (int j = 0; j <= 8; j++) for (int i = 0; i <= 8; i++) { Map(L * i / 8.0, W * j / 8.0, out _, out _, out double s); sum += s; n++; }
                _norm = n / sum;
            }
            public void Map(double x, double y, out double u, out double v, out double scale)
            {
                double zx = x / _m, zy = y / _m;
                u = _ca * zx + _sa * zy; v = -_sa * zx + _ca * zy;
                double du = _ca, dv = -_sa;                       // derivative of the map (complex)
                Add(zx - _cx1, zy - _cy1, _k1, 1, ref u, ref v, ref du, ref dv);
                Add(zx - _cx2, zy - _cy2, _k2, -1, ref u, ref v, ref du, ref dv);
                u *= _m * _norm; v *= _m * _norm;
                scale = Math.Sqrt(du * du + dv * dv) * _norm;
            }
            // k * (-i) * log(z - c): the stream lines are circles round c; the cut of the angle points away from the panel (down for side 1, up for -1)
            private static void Add(double dx, double dy, double k, int side, ref double u, ref double v, ref double du, ref double dv)
            {
                double r2 = dx * dx + dy * dy, ln = 0.5 * Math.Log(r2), th = side > 0 ? Math.Atan2(dx, dy) * -1 + 0.5 * Math.PI : -(Math.Atan2(dx, -dy) * -1 + 0.5 * Math.PI);
                u += k * th; v -= k * ln;
                du += k * (-dy) / r2 * 1.0; dv += -k * dx / r2 * 1.0;
            }
        }

        /// <summary>
        /// Hollows cut by a gouge, one beside the other along a flow; where two meet a sharp ridge stays. The strokes bend
        /// with the flow, are steep in front and run out flat at the back, as a chisel leaves them.
        /// </summary>
        private static Func<double, double, double> Carved(double L, double W, double a, double aspect, int seed, bool arch)
        {
            double m = Math.Min(L, W);
            // the direction of the strokes: round an eye at one side and straight far from it, or an S over the whole panel
            Func<double, double, double> angle = (x, y) =>
            {
                double gx, gy;
                if (arch)
                {
                    double dx = x - 0.80 * L, dy = y - 0.22 * W, d = Math.Sqrt(dx * dx + dy * dy) + 1e-9, far = Smooth(0.55, 1.35, d / m);
                    gx = (1 - far) * dx / d + far * -0.80; gy = (1 - far) * dy / d + far * 0.60;
                }
                else
                {
                    double q = 2 * Math.PI * x / L, t = 2 * Math.PI * y / W;
                    gx = -0.75 * Math.Cos(0.9 * q + 0.5 * Math.Sin(t * 0.5)) - 0.25; gy = 1.0 + 0.35 * Math.Sin(0.7 * t + 0.6);
                }
                return Math.Atan2(gx, -gy);      // along the level lines
            };
            Func<double, double, double> size = (x, y) => 0.78 + 0.5 * (0.5 + 0.5 * Math.Sin(2.6 * x / L + 1.1) * Math.Cos(2.2 * y / W - 0.4));
            double len = aspect * a;
            // seeds: thrown at random, kept when far enough from the others in the measure of the strokes, then evened out
            var px = new List<double>(); var py = new List<double>();
            double border = 0.6 * len;
            int throws = (int)Math.Min(60000, 60 * (L + 2 * border) * (W + 2 * border) / (a * len));
            for (int k = 0; k < throws; k++)
            {
                double x = -border + (L + 2 * border) * Hash(k, seed, 21), y = -border + (W + 2 * border) * Hash(k, seed, 22);
                double th = angle(x, y), c = Math.Cos(th), sn = Math.Sin(th), z = size(x, y);
                bool free = true;
                for (int i = 0; i < px.Count && free; i++)
                {
                    double dx = px[i] - x, dy = py[i] - y;
                    if (Math.Abs(dx) > len * 1.5 || Math.Abs(dy) > len * 1.5) continue;
                    double u = (c * dx + sn * dy) / (len * z), v = (-sn * dx + c * dy) / (a * z);
                    if (u * u + v * v < 0.62) free = false;
                }
                if (free) { px.Add(x); py.Add(y); }
            }
            int n = px.Count;
            for (int round = 0; round < 5; round++)
            {
                var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
                double g = Math.Max(1.0, a / 5);
                for (double y = -border; y < W + border; y += g)
                    for (double x = -border; x < L + border; x += g)
                    {
                        double th = angle(x, y), c = Math.Cos(th), sn = Math.Sin(th), best = double.MaxValue; int b = -1;
                        for (int i = 0; i < n; i++)
                        {
                            double dx = px[i] - x, dy = py[i] - y;
                            if (Math.Abs(dx) > len * 1.6 || Math.Abs(dy) > len * 1.6) continue;
                            double u = (c * dx + sn * dy) / len, v = (-sn * dx + c * dy) / a, q = u * u + v * v;
                            if (q < best) { best = q; b = i; }
                        }
                        if (b >= 0) { sx[b] += x; sy[b] += y; cnt[b]++; }
                    }
                for (int i = 0; i < n; i++) if (cnt[i] > 0) { px[i] += 0.7 * (sx[i] / cnt[i] - px[i]); py[i] += 0.7 * (sy[i] / cnt[i] - py[i]); }
            }
            var ca = new double[n]; var sa = new double[n]; var bend = new double[n]; var sz = new double[n]; var cutDeep = new double[n];
            double cell = len * 1.2;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                double th = angle(px[i], py[i]); ca[i] = Math.Cos(th); sa[i] = Math.Sin(th); sz[i] = size(px[i], py[i]); cutDeep[i] = 0.85 + 0.3 * Hash(i, seed, 31);
                double e = 0.25 * len, t2 = angle(px[i] + e * ca[i], py[i] + e * sa[i]), t1 = angle(px[i] - e * ca[i], py[i] - e * sa[i]), dt = t2 - t1;
                while (dt > Math.PI) dt -= 2 * Math.PI; while (dt < -Math.PI) dt += 2 * Math.PI;
                bend[i] = Math.Max(-1.2 / len, Math.Min(1.2 / len, dt / (2 * e)));
                long key = ((long)Math.Floor(px[i] / cell) << 32) ^ (uint)(int)Math.Floor(py[i] / cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }
            return (x, y) =>
            {
                double best = 1.4, deep = 1; int gx = (int)Math.Floor(x / cell), gy = (int)Math.Floor(y / cell);
                for (int j = gy - 1; j <= gy + 1; j++)
                    for (int k = gx - 1; k <= gx + 1; k++)
                    {
                        if (!grid.TryGetValue(((long)k << 32) ^ (uint)j, out var list)) continue;
                        foreach (int i in list)
                        {
                            double dx = x - px[i], dy = y - py[i], u = ca[i] * dx + sa[i] * dy, v = -sa[i] * dx + ca[i] * dy - 0.5 * bend[i] * u * u;
                            double uu = u / (0.5 * len * sz[i]), vv = v / (0.5 * a * sz[i]);
                            uu *= uu > 0 ? 1.12 : 0.9;
                            double d = 0.62 * Math.Pow(Math.Abs(uu), 2.6) + 0.78 * vv * vv;
                            if (d < best) { best = d; deep = cutDeep[i] * sz[i]; }
                        }
                    }
                return deep * best;
            };
        }

        /// <summary>Scales that follow a flow: pointed, overlapping like roof tiles, small where the flow is tight and large where it opens.</summary>
        private static Func<double, double, double> Scales(double L, double W, double a)
        {
            var flow = new Flow(L, W);
            double r = 0.5 * a, row = 0.62 * a;
            return (x, y) =>
            {
                flow.Map(x, y, out double fu, out double fv, out double scale);
                double best = 0;
                int j0 = (int)Math.Floor(fu / row);
                for (int j = j0 - 2; j <= j0 + 2; j++)
                {
                    double off = ((j % 2) + 2) % 2 * 0.5 * a, cu = (j + 0.5) * row;
                    int i = (int)Math.Round((fv - off) / a); double dv = (fv - i * a - off) / r, du = (fu - cu) / row;   // -1 .. 1 across, along in rows
                    // the tip at du = -0.75, widest at 0.25; from the tip the scale falls back evenly under the next ones: a sharp drop at the tip
                    if (du < -0.75 || du > 1.3) continue;
                    double t = (du + 0.75) / 1.0, half = t < 1 ? Math.Pow(Math.Sin(0.5 * Math.PI * t), 0.72) : 1.0;
                    double q = Math.Abs(dv); if (q > half) continue;
                    double h = (1 - 0.46 * (du + 0.75)) * (1 - 0.22 * (q / Math.Max(0.3, half)) * (q / Math.Max(0.3, half)));
                    if (h > best) best = h;
                }
                double big = Math.Max(0.55, Math.Min(1.5, 1.0 / scale));
                return best * big;
            };
        }

        private static Func<double, double, double> Voronoi(double L, double W, double a, int seed)
        {
            return (x, y) =>
            {
                int ci = (int)Math.Floor(x / a), cj = (int)Math.Floor(y / a);
                double d1 = double.MaxValue, d2 = double.MaxValue, px = 0, py = 0, qx = 0, qy = 0; int bi = 0, bj = 0;
                for (int j = cj - 2; j <= cj + 2; j++)
                    for (int i = ci - 2; i <= ci + 2; i++)
                    {
                        double sx = (i + 0.12 + 0.76 * Hash(i, j, seed)) * a, sy = (j + 0.12 + 0.76 * Hash(i, j, seed + 7)) * a;
                        double d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                        if (d < d1) { d2 = d1; qx = px; qy = py; d1 = d; px = sx; py = sy; bi = i; bj = j; }
                        else if (d < d2) { d2 = d; qx = sx; qy = sy; }
                    }
                double gap = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                double edge = gap > 1e-9 ? (d2 - d1) / (2 * gap) : 0;             // distance to the nearest border
                double rib = 0.10 * a;
                double gx = x / L - 0.35, gy = y / W - 0.45, q = Math.Exp(-(gx * gx + gy * gy) * 4);
                if (edge < rib) return 1.0;
                double t = Clamp((edge - rib) / (0.30 * a));
                double depth = 0.35 + 0.65 * (0.35 + 0.65 * q) * (0.75 + 0.25 * Hash(bi, bj, seed + 3));
                return 1.0 - depth * Math.Sqrt(1 - (1 - t) * (1 - t));            // a bowl
            };
        }

        /// <summary>Eight-pointed stars and crosses as raised bands (two squares, one turned by 45 degrees, on a square grid).</summary>
        private static Func<double, double, double> Lattice(double a)
        {
            return (x, y) =>
            {
                double u = x / a - Math.Floor(x / a) - 0.5, v = y / a - Math.Floor(y / a) - 0.5;
                double au = Math.Abs(u), av = Math.Abs(v);
                const double s = 0.30;                                       // half the square
                double sq = Math.Max(au, av) - s, di = (au + av) / Math.Sqrt(2) - s;
                double star = Math.Min(sq, di);                              // < 0 inside the star
                double band = Math.Abs(star);
                double link = Math.Min(au, av);                              // bands that join the stars
                bool outside = star > 0;
                double bw = 0.045;
                double h = 0.15;
                h = Math.Max(h, 1 - Smooth(bw, bw + 0.02, band));
                if (outside) h = Math.Max(h, 0.8 * (1 - Smooth(0.6 * bw, 0.6 * bw + 0.02, link)));
                double r = Math.Sqrt(u * u + v * v);
                if (!outside) h = Math.Max(h, 0.15 + 0.5 * Clamp(1 - r / 0.2) );   // a bud in the middle
                else h = Math.Max(h, 0.15 + 0.35 * Clamp(1 - Math.Sqrt((0.5 - au) * (0.5 - au) + (0.5 - av) * (0.5 - av)) / 0.16));
                return h;
            };
        }
    }
}
