using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>How close two neighbouring beads are.</summary>
    public enum WeaveClass
    {
        /// <summary>The beads overlap sideways (gap clearly below one bead width): they fuse along a strip.</summary>
        Fused = 0,
        /// <summary>The bead edges just meet (gap about one bead width).</summary>
        Touching = 1,
        /// <summary>A small gap stays open between the beads.</summary>
        Almost = 2,
        /// <summary>Pressed into each other: much nearer than one bead spacing (too much material).</summary>
        Pressed = 3
    }

    /// <summary>
    /// A place set by hand: a closed curve in top view (optionally only between two heights), an open
    /// curve or a point with a radius, or anything that can say whether a point lies inside (a Brep).
    /// </summary>
    public sealed class WeaveRegion
    {
        /// <summary>Closed polygon (top view). Null when the region is a curve or a point.</summary>
        public List<Point2d> Polygon;
        /// <summary>Points of an open curve, or a single point; used with Radius.</summary>
        public List<Point3d> Line;
        public double Radius = 0;
        /// <summary>true = the distance to Line is measured in space, false = in top view.</summary>
        public bool Spatial = false;
        /// <summary>Only between these heights (world Z).</summary>
        public double ZMin = double.NegativeInfinity, ZMax = double.PositiveInfinity;
        /// <summary>Own test (a closed Brep or mesh). Must be thread-safe.</summary>
        public Func<Point3d, bool> Inside;

        public bool Contains(Point3d p)
        {
            if (p.Z < ZMin || p.Z > ZMax) return false;
            if (Inside != null) return Inside(p);
            if (Polygon != null)
            {
                bool inside = false;
                for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
                    if ((Polygon[i].Y > p.Y) != (Polygon[j].Y > p.Y) &&
                        p.X < (Polygon[j].X - Polygon[i].X) * (p.Y - Polygon[i].Y) / (Polygon[j].Y - Polygon[i].Y) + Polygon[i].X) inside = !inside;
                return inside;
            }
            if (Line == null || Line.Count == 0) return false;
            double best = double.MaxValue;
            for (int i = 0; i < Line.Count; i++)
            {
                Point3d a = Line[Math.Max(0, i - 1)], b = Line[i];
                double ax = a.X, ay = a.Y, az = Spatial ? a.Z : 0, dx = b.X - ax, dy = b.Y - ay, dz = Spatial ? b.Z - az : 0, pz = Spatial ? p.Z : 0;
                double l2 = dx * dx + dy * dy + dz * dz;
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - ax) * dx + (p.Y - ay) * dy + (pz - az) * dz) / l2));
                double ex = ax + t * dx - p.X, ey = ay + t * dy - p.Y, ez = az + t * dz - pz;
                best = Math.Min(best, ex * ex + ey * ey + ez * ez);
            }
            return best <= Radius * Radius;
        }

        public static WeaveRegion Circle(Point3d c, double radius) => new WeaveRegion { Line = new List<Point3d> { c }, Radius = radius };
        public static WeaveRegion Rectangle(double x0, double y0, double x1, double y1) => new WeaveRegion
        { Polygon = new List<Point2d> { new Point2d(x0, y0), new Point2d(x1, y0), new Point2d(x1, y1), new Point2d(x0, y1) } };
    }

    /// <summary>Settings of the zone finder. The tolerances are fractions of the bead width.</summary>
    public sealed class WeaveFindOptions
    {
        /// <summary>Closest: two lines nearer than w x (1 - Closer) are left alone (one line lies on the other).</summary>
        public double Closer = 0.85;
        /// <summary>Nearer than w x (1 - Press) the beads are pressed into each other: too much material, unless the bead width is fitted.</summary>
        public double Press = 0.35;
        /// <summary>Within w x (1 +- Touch) the beads count as touching; nearer is fused, further is almost touching.</summary>
        public double Touch = 0.08;
        /// <summary>Furthest: up to w x (1 + Further) the beads count as almost touching.</summary>
        public double Further = 0.25;
        /// <summary>Shortest stretch, mm. 0 = 1.5 bead widths.</summary>
        public double MinLength = 0;
        /// <summary>Two lines count as parallel up to this angle, degrees.</summary>
        public double MaxAngle = 25;
        /// <summary>A zone must run through at least this many layers.</summary>
        public int MinLayers = 2;
        /// <summary>Also the lines of solid bottom and top fill.</summary>
        public bool Bottom = false;
        /// <summary>Places set by hand. Inside an Include region beads up to w x (1 + IncludeFurther) apart are taken.</summary>
        public List<WeaveRegion> Include = new List<WeaveRegion>();
        public List<WeaveRegion> Exclude = new List<WeaveRegion>();
        /// <summary>true = only inside the Include regions, false = everything that was found, and the Include regions.</summary>
        public bool OnlyIncluded = true;
        public double IncludeFurther = 0.6;

        internal bool MayBeWoven(PathRole r) => r == PathRole.Wall || r == PathRole.Infill || (Bottom && r == PathRole.Bottom);
    }

    /// <summary>One stretch of one layer where two beads run side by side.</summary>
    public sealed class WeaveRun
    {
        public int Layer;
        public int Zone = -1;
        /// <summary>Stations along the run: sample on the first bead (A) and the fractional sample on the second (B).</summary>
        public int[] A;
        public double[] B;
        public double[] Gap;
        /// <summary>Arc length along the seam, from the first station.</summary>
        public double[] S;
        /// <summary>Middle between the two beads in the layer plane, and the unit direction from A to B.</summary>
        public double[] Mx, My, Nx, Ny;
        /// <summary>0 = the other bead lies to the left of this one, 1 = to the right.</summary>
        public int SideA, SideB;
        /// <summary>Zone coordinate = SSign x S + S0; the normal of the zone = NSign x (A to B).</summary>
        public double S0; public int SSign = 1, NSign = 1;
        /// <summary>Middle of the spot in the zone coordinate, smooth over the layers (spots can wander with the height).</summary>
        public double Centre;
        /// <summary>Curvature of the neighbouring beads (the larger one).</summary>
        public double[] Kappa;
        /// <summary>Height of the second bead above the first, mm (a spiral; 0 in flat layers).</summary>
        public double[] Rise;
        /// <summary>Middle in space, every station.</summary>
        public Point3d[] Mid;
        /// <summary>Centre lines of the two beads in space (the mid surfaces, before weaving).</summary>
        public Point3d[] PA, PB;
        public PathRole RoleA, RoleB;
        public bool Forced;

        public int Count => A.Length;
        public double Length => S.Length > 0 ? S[S.Length - 1] : 0;
        public double ZoneS(int k) => SSign * S[k] + S0;
        internal int ChainB; internal int DirB;
    }

    /// <summary>A seam between two beads, followed through the layers.</summary>
    public sealed class WeaveZone
    {
        public int Id;
        public WeaveZoneSet Set;
        public List<WeaveRun> Runs = new List<WeaveRun>();
        public WeaveClass Class;
        public int FirstLayer, LastLayer, LayerCount;
        /// <summary>Mean length of the seam per layer, and the total over all layers, mm.</summary>
        public double MeanLength, TotalLength;
        public double GapMin, GapMean, GapMax;
        /// <summary>Span of the zone coordinate.</summary>
        public double SMin, SMax;
        public string Roles = "";
        /// <summary>Neighbouring zones that share a bead move together: the sign and the origin of this zone.</summary>
        public int BundleSign = 1; public int BundleSDir = 1; public double BundleS0; public int Bundle;
        public Point3d Centre;

        public override string ToString() => Inv.F($"Zone {Id}: {Roles}, {Class.ToString().ToLowerInvariant()}, gap {GapMean:0.0#} mm, {MeanLength:0} mm long, layers {FirstLayer}-{LastLayer}");
    }

    /// <summary>The samples of one layer (only while it is worked on, or kept for a small path).</summary>
    internal sealed class WeaveLayer
    {
        public int Layer;
        public int N;
        public double[] X, Y, H, Arc, Tx, Ty, W;
        public int[] Src;      // path index of the move the sample lies on
        public double[] T;     // place on that move, 0..1
        public int[] Chain;
        /// <summary>Path layer of every sample (one number in a flat layer; climbs in a spiral, which is worked on as a whole).</summary>
        public int[] Lap;
        public bool Climb; public double LayerHeight;
        public PathRole[] Role;
        public bool[] Vertex;
        public float[] Any0, Any1; // distance to the nearest other bead on the left / right
        public WeaveFrame Frame;
        public List<WeaveRun> Runs = new List<WeaveRun>();
    }

    internal struct WeaveFrame
    {
        public double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz;
        public static WeaveFrame At(Vector3d n)
        {
            double l = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            var f = new WeaveFrame();
            if (l < 1e-9 || n.Z / l > 0.99999) { f.Xx = 1; f.Yy = 1; f.Zz = 1; return f; }
            f.Zx = n.X / l; f.Zy = n.Y / l; f.Zz = n.Z / l;
            // x as horizontal as possible
            double xx = -f.Zy, xy = f.Zx, xz = 0, xl = Math.Sqrt(xx * xx + xy * xy);
            if (xl < 1e-9) { xx = 1; xy = 0; xl = 1; }
            f.Xx = xx / xl; f.Xy = xy / xl; f.Xz = xz;
            f.Yx = f.Zy * f.Xz - f.Zz * f.Xy; f.Yy = f.Zz * f.Xx - f.Zx * f.Xz; f.Yz = f.Zx * f.Xy - f.Zy * f.Xx;
            return f;
        }
        public void To(Point3d p, out double x, out double y, out double h)
        {
            x = p.X * Xx + p.Y * Xy + p.Z * Xz; y = p.X * Yx + p.Y * Yy + p.Z * Yz; h = p.X * Zx + p.Y * Zy + p.Z * Zz;
        }
        public Point3d From(double x, double y, double h) => new Point3d(x * Xx + y * Yx + h * Zx, x * Xy + y * Yy + h * Zy, x * Xz + y * Yz + h * Zz);
    }

    /// <summary>Everything the zone finder knows about a path.</summary>
    public sealed class WeaveZoneSet
    {
        public Toolpath Path;
        public WeaveFindOptions Options;
        public List<WeaveZone> Zones = new List<WeaveZone>();
        public int LayerCount, Samples;
        /// <summary>The path is a spiral: beads side by side can lie at different heights.</summary>
        public bool Climbs;
        /// <summary>What that means for this path (empty for flat layers).</summary>
        public string SpiralNote = "";
        public double Seconds;
        /// <summary>Sample distance along the path, mm.</summary>
        public double Step;
        /// <summary>The path in flat space when it is warped (else the path itself).</summary>
        internal Toolpath Flat;
        public List<string> Report = new List<string>();

        public string Verdict
        {
            get
            {
                if (Zones.Count == 0) return "NOTHING FOUND: no two beads run side by side within the tolerances.";
                double len = Zones.Sum(z => z.TotalLength) / 1000.0;
                int f = Zones.Count(z => z.Class == WeaveClass.Fused), t = Zones.Count(z => z.Class == WeaveClass.Touching), p = Zones.Count(z => z.Class == WeaveClass.Pressed), a = Zones.Count - f - t - p;
                return Inv.F($"{Zones.Count} zone(s), {len:0.0} m of seam over all layers: {f} fused, {t} touching, {a} almost touching{(p > 0 ? Inv.F($", {p} pressed into each other (too close)") : "")}.");
            }
        }
    }

    /// <summary>
    /// Finds the places where two beads of a layer lie side by side (two walls, infill against a
    /// wall, two sides of a rib, the same line folded onto itself) and follows them through the
    /// layers, so a pattern can alternate along the height. See Weave.
    /// </summary>
    public static class WeaveFinder
    {
        /// <summary>Distance of the samples along the path.</summary>
        internal static double StepOf(PrintSettings st) => Math.Max(0.3, st.BeadWidth / 6.0);

        public static WeaveZoneSet Find(Toolpath tp, WeaveFindOptions o)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var set = new WeaveZoneSet { Path = tp, Options = o ?? new WeaveFindOptions(), Step = StepOf(tp.Settings), Flat = Flat(tp) };
            o = set.Options;
            var layers = Split(set.Flat);
            set.LayerCount = layers.Count;
            var runs = new List<WeaveRun>[layers.Count];
            int samples = 0;
            var token = Cancel.Token;
            Parallel.For(0, layers.Count, new ParallelOptions { CancellationToken = token }, i =>
            {
                var l = Sample(set.Flat, layers[i], set.Step);
                Detect(l, set);
                System.Threading.Interlocked.Add(ref samples, l.N);
                runs[i] = l.Runs;
            });
            if (layers.Count == 1 && layers[0].Climb)
            {
                runs = runs[0].GroupBy(r => r.Layer).OrderBy(g => g.Key).Select(g => g.ToList()).ToArray();
                set.LayerCount = set.Flat.Layers.Distinct().Count(); set.Climbs = true;
            }
            set.Samples = samples;
            Track(set, runs);
            set.Seconds = clock.Elapsed.TotalSeconds;
            Describe(set);
            return set;
        }

        /// <summary>The path in flat space when it was bent by a guide surface or curve.</summary>
        internal static Toolpath Flat(Toolpath tp)
        {
            // laps that follow the surface stay as they are: they are looked at from above
            if (tp.Warp == null || tp.Warp.UseBeadLaw) return tp;
            var f = tp.Duplicate(); f.Warp = null;
            for (int i = 0; i < f.Count; i++) { f.Points[i] = tp.Warp.ToFlat(tp.Points[i]); f.Normals[i] = Vector3d.ZAxis; }
            return f;
        }

        internal sealed class LayerRange { public int Layer; public bool Climb; public List<int[]> Chains = new List<int[]>(); }

        private static bool Printed(Toolpath tp, int i) => i > 0 && tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel && tp.Roles[i] != PathRole.Skirt;

        /// <summary>Per layer: the stretches of the path that print without a break (from point, to point).</summary>
        internal static List<LayerRange> Split(Toolpath tp)
        {
            if (Climbs(tp))
            {
                var one = new LayerRange { Climb = true, Layer = int.MinValue };
                for (int i = 1; i < tp.Count; i++)
                {
                    if (!Printed(tp, i)) continue;
                    int j = i; while (j + 1 < tp.Count && Printed(tp, j + 1)) j++;
                    if (one.Layer == int.MinValue) one.Layer = tp.Layers[i];
                    one.Chains.Add(new[] { i - 1, j });
                    i = j;
                }
                return new List<LayerRange> { one };
            }
            var map = new SortedDictionary<int, LayerRange>();
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Printed(tp, i)) continue;
                int layer = tp.Layers[i], j = i;
                while (j + 1 < tp.Count && Printed(tp, j + 1) && tp.Layers[j + 1] == layer) j++;
                if (!map.TryGetValue(layer, out var r)) map[layer] = r = new LayerRange { Layer = layer };
                r.Chains.Add(new[] { i - 1, j });
                i = j;
            }
            return map.Values.ToList();
        }

        /// <summary>
        /// A spiral: the height rises along the layer, so two beads of the same layer number can lie up to
        /// one layer height apart, and the bead at the same height can be one of the next layer. Such a path is worked on as a
        /// whole, and neighbours are beads at the same height.
        /// </summary>
        internal static bool Climbs(Toolpath tp)
        {
            var lo = new Dictionary<int, double>(); var hi = new Dictionary<int, double>();
            for (int i = 1; i < tp.Count; i++)
            {
                if (!Printed(tp, i) || tp.Roles[i] == PathRole.Seam) continue;
                var n = tp.Normals[i]; var p = tp.Points[i]; double h = p.X * n.X + p.Y * n.Y + p.Z * n.Z; int l = tp.Layers[i];
                if (!lo.TryGetValue(l, out double a) || h < a) lo[l] = h;
                if (!hi.TryGetValue(l, out double b) || h > b) hi[l] = h;
            }
            int up = lo.Keys.Count(l => hi[l] - lo[l] > 0.4 * tp.Settings.LayerHeight);
            return up * 2 > lo.Count;
        }

        internal static WeaveLayer Sample(Toolpath tp, LayerRange range, double step)
        {
            var l = new WeaveLayer { Layer = range.Layer, Climb = range.Climb, LayerHeight = tp.Settings.LayerHeight, Frame = WeaveFrame.At(tp.Normals[range.Chains[0][1]]) };
            var x = new List<double>(); var y = new List<double>(); var h = new List<double>(); var arc = new List<double>(); var w = new List<double>();
            var src = new List<int>(); var t = new List<double>(); var ch = new List<int>(); var role = new List<PathRole>(); var vx = new List<bool>();
            double at = 0;
            for (int c = 0; c < range.Chains.Count; c++)
            {
                int i0 = range.Chains[c][0], i1 = range.Chains[c][1];
                at += 1e5; // chains are far from each other along the path
                l.Frame.To(tp.Points[i0], out double px, out double py, out double ph);
                x.Add(px); y.Add(py); h.Add(ph); arc.Add(at); w.Add(tp.Widths[i0] > 0 ? tp.Widths[i0] : tp.Widths[i0 + 1]); src.Add(i0 + 1); t.Add(0); ch.Add(c); role.Add(tp.Roles[i0 + 1]); vx.Add(true);
                for (int i = i0 + 1; i <= i1; i++)
                {
                    l.Frame.To(tp.Points[i], out double qx, out double qy, out double qh);
                    double dx = qx - px, dy = qy - py, len = Math.Sqrt(dx * dx + dy * dy);
                    int parts = Math.Max(1, (int)Math.Ceiling(len / step - 1e-6));
                    double w0 = w[w.Count - 1], w1 = tp.Widths[i];
                    for (int k = 1; k <= parts; k++)
                    {
                        double u = k / (double)parts;
                        x.Add(px + dx * u); y.Add(py + dy * u); h.Add(ph + (qh - ph) * u); arc.Add(at + len * u); w.Add(w0 + (w1 - w0) * u);
                        src.Add(i); t.Add(u); ch.Add(c); role.Add(tp.Roles[i]); vx.Add(k == parts);
                    }
                    at += len; px = qx; py = qy; ph = qh;
                }
            }
            int n = x.Count;
            l.Lap = new int[n]; for (int i = 0; i < n; i++) l.Lap[i] = tp.Layers[src[i]];
            l.N = n; l.X = x.ToArray(); l.Y = y.ToArray(); l.H = h.ToArray(); l.Arc = arc.ToArray(); l.W = w.ToArray();
            l.Src = src.ToArray(); l.T = t.ToArray(); l.Chain = ch.ToArray(); l.Role = role.ToArray(); l.Vertex = vx.ToArray();
            l.Tx = new double[n]; l.Ty = new double[n];
            for (int i = 0; i < n; i++)
            {
                // direction over about one step to either side
                int a = i, b = i; double step1 = Math.Min(step, 1.5);
                while (a > 0 && l.Chain[a - 1] == l.Chain[i] && l.Arc[i] - l.Arc[a] < step1) a--;
                while (b + 1 < n && l.Chain[b + 1] == l.Chain[i] && l.Arc[b] - l.Arc[i] < step1) b++;
                double dx = l.X[b] - l.X[a], dy = l.Y[b] - l.Y[a], d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 1e-9) { l.Tx[i] = dx / d; l.Ty[i] = dy / d; } else if (i > 0) { l.Tx[i] = l.Tx[i - 1]; l.Ty[i] = l.Ty[i - 1]; } else l.Tx[i] = 1;
            }
            return l;
        }

        /// <summary>The moves of a layer in a grid. Dense arrays: cells in one list, sorted by counting.</summary>
        internal sealed class Grid
        {
            public int[] Cell0, Items; public int Nx, Ny, L0, NL = 1; public bool Climb; public double X0, Y0, Size;
            public int[] SegA, SegB; // sample indices of the ends of a move

            public Grid(WeaveLayer l, double size) : this(l, size, l.X, l.Y, true) { }

            public Grid(WeaveLayer l, double size, double[] px, double[] py, bool whole)
            {
                var a = new List<int>(); var b = new List<int>();
                for (int i = 1, last = 0; i < l.N; i++)
                {
                    if (l.Chain[i] != l.Chain[i - 1]) { last = i; continue; }
                    if (!whole || l.Vertex[i]) { a.Add(last); b.Add(i); last = i; }
                }
                SegA = a.ToArray(); SegB = b.ToArray();
                double x1 = double.MinValue, y1 = double.MinValue; X0 = double.MaxValue; Y0 = double.MaxValue;
                for (int i = 0; i < l.N; i++) { X0 = Math.Min(X0, px[i]); Y0 = Math.Min(Y0, py[i]); x1 = Math.Max(x1, px[i]); y1 = Math.Max(y1, py[i]); }
                Size = size;
                int laps = l.Climb ? l.Lap.Max() - l.Lap.Min() + 1 : 1;
                while ((x1 - X0) / Size * ((y1 - Y0) / Size) * laps > 8000000) Size *= 1.5;
                Nx = (int)((x1 - X0) / Size) + 1; Ny = (int)((y1 - Y0) / Size) + 1;
                Climb = l.Climb;
                if (Climb) { L0 = l.Lap.Min(); NL = l.Lap.Max() - L0 + 1; }
                Cell0 = new int[Nx * Ny * NL + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        for (int c = 1; c < Cell0.Length; c++) Cell0[c] += Cell0[c - 1];
                        Items = new int[Cell0[Cell0.Length - 1]];
                    }
                    for (int s = SegA.Length - 1; s >= 0; s--)
                    {
                        double ax = px[SegA[s]], ay = py[SegA[s]], bx = px[SegB[s]], by = py[SegB[s]]; int lz = Climb ? l.Lap[SegB[s]] - L0 : 0;
                        int gx0 = Gx(Math.Min(ax, bx)), gx1 = Gx(Math.Max(ax, bx)), gy0 = Gy(Math.Min(ay, by)), gy1 = Gy(Math.Max(ay, by));
                        bool big = (gx1 - gx0) > 1 && (gy1 - gy0) > 1;
                        for (int gy = gy0; gy <= gy1; gy++)
                            for (int gx = gx0; gx <= gx1; gx++)
                            {
                                if (big)
                                {
                                    // a long slanted move: only the cells it passes
                                    double cx = X0 + (gx + 0.5) * Size, cy = Y0 + (gy + 0.5) * Size, dx = bx - ax, dy = by - ay;
                                    double dist = Math.Abs((cx - ax) * dy - (cy - ay) * dx) / Math.Sqrt(dx * dx + dy * dy);
                                    if (dist > 0.75 * Size) continue;
                                }
                                int c = (lz * Ny + gy) * Nx + gx;
                                if (pass == 0) Cell0[c]++; else Items[--Cell0[c]] = s;
                            }
                    }
                }
            }
            public int Gx(double x) { int g = (int)((x - X0) / Size); return g < 0 ? 0 : g >= Nx ? Nx - 1 : g; }
            public int Gy(double y) { int g = (int)((y - Y0) / Size); return g < 0 ? 0 : g >= Ny ? Ny - 1 : g; }
            /// <summary>The laps to look at around a lap (only one in a flat layer).</summary>
            public int Z0(int lap, int d) => Climb ? Math.Max(0, lap - L0 - d) : 0;
            public int Z1(int lap, int d) => Climb ? Math.Min(NL - 1, lap - L0 + d) : 0;
            public int Start(int c) => Cell0[c];
            public int End(int c) => c + 1 < Cell0.Length - 1 ? Cell0[c + 1] : Items.Length;
        }

        /// <summary>Sample index (with fraction) at an arc length; hint is an index near it.</summary>
        internal static double IndexAt(WeaveLayer l, double arc, int lo, int hi)
        {
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (l.Arc[m] <= arc) lo = m; else hi = m; }
            double d = l.Arc[hi] - l.Arc[lo];
            return lo + (d > 1e-12 ? Math.Max(0, Math.Min(1, (arc - l.Arc[lo]) / d)) : 0);
        }

        private static void Detect(WeaveLayer l, WeaveZoneSet set)
        {
            var o = set.Options; var st = set.Path.Settings;
            double w = st.BeadWidth, step = set.Step;
            int n = l.N;
            bool inc = o.Include != null && o.Include.Count > 0, exc = o.Exclude != null && o.Exclude.Count > 0;
            double far = Math.Max(o.Further, inc ? o.IncludeFurther : 0);
            double reach = w * Math.Max(2.0, 1.15 + far);
            var grid = new Grid(l, reach);
            double sin = Math.Sin(Math.Max(1, Math.Min(45, o.MaxAngle)) * Math.PI / 180);
            var foot = new[] { new double[n], new double[n] };   // partner sample on the left / right (-1 none)
            var gap = new[] { new double[n], new double[n] };
            l.Any0 = new float[n]; l.Any1 = new float[n];
            Each(l, n, i =>
            {
                foot[0][i] = foot[1][i] = -1; l.Any0[i] = l.Any1[i] = float.MaxValue;
                if (!(o.MayBeWoven(l.Role[i]))) return;
                double px = l.X[i], py = l.Y[i], tx = l.Tx[i], ty = l.Ty[i], wi = l.W[i] > 0 ? l.W[i] : w;
                double best0 = reach, best1 = reach, any0 = reach, any1 = reach; double f0 = -1, f1 = -1;
                int gx0 = grid.Gx(px - reach), gx1 = grid.Gx(px + reach), gy0 = grid.Gy(py - reach), gy1 = grid.Gy(py + reach);
                for (int lz = grid.Z0(l.Lap[i], 1); lz <= grid.Z1(l.Lap[i], 1); lz++)
                for (int gy = gy0; gy <= gy1; gy++)
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        int c = (lz * grid.Ny + gy) * grid.Nx + gx;
                        for (int q = grid.Start(c), e = grid.End(c); q < e; q++)
                        {
                            int s = grid.Items[q], a = grid.SegA[s], b = grid.SegB[s];
                            double ax = l.X[a], ay = l.Y[a], dx = l.X[b] - ax, dy = l.Y[b] - ay, l2 = dx * dx + dy * dy;
                            if (l2 < 1e-18) continue;
                            double u = ((px - ax) * dx + (py - ay) * dy) / l2; if (u < 0) u = 0; else if (u > 1) u = 1;
                            double vx = ax + u * dx - px, vy = ay + u * dy - py, d2 = vx * vx + vy * vy;
                            if (d2 >= reach * reach || d2 < 1e-6) continue;
                            if (l.Climb && Math.Abs(l.H[a] + u * (l.H[b] - l.H[a]) - l.H[i]) > 0.5 * l.LayerHeight) continue; // another height
                            double d = Math.Sqrt(d2), along = Math.Abs(vx * tx + vy * ty);
                            if (along > 0.8 * d) continue;
                            double arc = l.Arc[a] + u * (l.Arc[b] - l.Arc[a]), apart = Math.Abs(arc - l.Arc[i]);
                            if (apart < 1.4 * d) continue;
                            bool left = tx * vy - ty * vx > 0;
                            if (apart > 3 * d) { if (left) { if (d < any0) any0 = d; } else if (d < any1) any1 = d; }
                            if (along > 0.45 * d) continue;
                            double len = Math.Sqrt(l2);
                            if (Math.Abs(tx * dy - ty * dx) > sin * len) continue;
                            if (!o.MayBeWoven(l.Role[b])) continue;
                            if (left) { if (d < best0) { best0 = d; f0 = IndexAt(l, arc, a, b); } }
                            else if (d < best1) { best1 = d; f1 = IndexAt(l, arc, a, b); }
                        }
                    }
                l.Any0[i] = any0 < reach ? (float)any0 : float.MaxValue; l.Any1[i] = any1 < reach ? (float)any1 : float.MaxValue;
                for (int sd = 0; sd < 2; sd++)
                {
                    double g = sd == 0 ? best0 : best1, f = sd == 0 ? f0 : f1, any = sd == 0 ? any0 : any1;
                    if (f < 0 || any < 0.75 * g) continue;                      // another line lies in between
                    if (g < wi * (1 - o.Closer) || g > wi * (1 + far)) continue;
                    gap[sd][i] = g; foot[sd][i] = f;
                }
            });

            // the regions, and the wider tolerance inside an Include region
            var forced = inc ? new bool[n] : null;
            if (inc || exc)
                for (int i = 0; i < n; i++)
                {
                    if (foot[0][i] < 0 && foot[1][i] < 0) continue;
                    Point3d p = World(set, l, l.X[i], l.Y[i], l.H[i]);
                    bool inIn = inc && o.Include.Any(r => r.Contains(p)), inEx = exc && o.Exclude.Any(r => r.Contains(p));
                    if (inc) forced[i] = inIn;
                    for (int sd = 0; sd < 2; sd++)
                    {
                        if (foot[sd][i] < 0) continue;
                        double wi = l.W[i] > 0 ? l.W[i] : w;
                        if (inEx || (inc && o.OnlyIncluded && !inIn) || (!inIn && gap[sd][i] > wi * (1 + o.Further))) foot[sd][i] = -1;
                    }
                }

            // both must mean each other
            int near = (int)Math.Ceiling(0.8 * w / step) + 1;
            Func<int, int, int> facing = (i, sd) =>
            {
                int j = (int)Math.Round(foot[sd][i]);
                for (int k = 0; k <= near; k++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        int jj = j + sg * k;
                        if (jj < 0 || jj >= n || l.Chain[jj] != l.Chain[j]) continue;
                        // the side of the partner that looks at this bead
                        int side = l.Tx[jj] * (l.Y[i] - l.Y[jj]) - l.Ty[jj] * (l.X[i] - l.X[jj]) > 0 ? 0 : 1;
                        if (foot[side][jj] >= 0 && Math.Abs(foot[side][jj] - i) <= near + 2) return side;
                        if (k == 0) break;
                    }
                return -1;
            };

            double minLen = o.MinLength > 0 ? o.MinLength : 1.5 * w;
            int bridge = (int)Math.Ceiling(0.6 * w / step);
            for (int sd = 0; sd < 2; sd++)
                for (int i = 0; i < n; i++)
                {
                    if (foot[sd][i] < 0 || foot[sd][i] <= i) continue;
                    int sb = facing(i, sd);
                    if (sb < 0) continue;
                    var A = new List<int> { i }; var B = new List<double> { foot[sd][i] };
                    int chain = l.Chain[i], chb = l.Chain[(int)foot[sd][i]], dir = 0, miss = 0, j = i;
                    while (j + 1 < n && l.Chain[j + 1] == chain && l.Lap[j + 1] == l.Lap[i])
                    {
                        j++;
                        double f = foot[sd][j], prev = B[B.Count - 1];
                        bool ok = f > j && l.Chain[(int)f] == chb && Math.Abs(f - prev) <= 3 * (j - A[A.Count - 1]) + 4;
                        if (ok)
                        {
                            int d = f > prev + 1e-6 ? 1 : f < prev - 1e-6 ? -1 : 0;
                            if (dir == 0) dir = d; else if (d != 0 && d != dir) { if (Math.Abs(f - prev) > 1.5) ok = false; else f = prev; }
                        }
                        if (ok && facing(j, sd) != sb) ok = false;
                        if (ok) { A.Add(j); B.Add(f); miss = 0; }
                        else if (++miss > bridge) break;
                    }
                    int last = A[A.Count - 1];
                    if (A.Count >= 3 && l.Arc[last] - l.Arc[i] >= minLen)
                    {
                        var r = Make(l, set, A, B, sd, sb, chb, dir == 0 ? -1 : dir);
                        if (r != null && r.Length >= 0.8 * minLen)
                        {
                            if (forced != null) { int c = 0; foreach (int a in r.A) if (forced[a]) c++; r.Forced = c * 2 > r.Count; }
                            l.Runs.Add(r);
                        }
                    }
                    i = last;
                }
        }

        /// <summary>All samples, in parallel when the whole path is one piece.</summary>
        internal static void Each(WeaveLayer l, int n, Action<int> f)
        {
            if (l.Climb && n > 20000) Parallel.For(0, n, f); else for (int i = 0; i < n; i++) f(i);
        }

        internal static Point3d World(WeaveZoneSet set, WeaveLayer l, double x, double y, double h)
        {
            Point3d p = l.Frame.From(x, y, h);
            return !ReferenceEquals(set.Flat, set.Path) ? set.Path.Warp.ToReal(p, out _) : p;
        }

        internal static double At(double[] v, double f) { int i = (int)f; if (i >= v.Length - 1) return v[v.Length - 1]; if (i < 0) return v[0]; double t = f - i; return v[i] * (1 - t) + v[i + 1] * t; }

        private static WeaveRun Make(WeaveLayer l, WeaveZoneSet set, List<int> A, List<double> B, int sideA, int sideB, int chainB, int dir)
        {
            // every sample of A between the first and the last one
            var a = new List<int>(); var b = new List<double>();
            for (int k = 0; k < A.Count; k++)
            {
                if (k > 0)
                    for (int j = A[k - 1] + 1; j < A[k]; j++) { double t = (j - A[k - 1]) / (double)(A[k] - A[k - 1]); a.Add(j); b.Add(B[k - 1] + (B[k] - B[k - 1]) * t); }
                a.Add(A[k]); b.Add(B[k]);
            }
            int n = a.Count;
            var r = new WeaveRun { Layer = l.Lap[a[n / 2]], A = a.ToArray(), B = b.ToArray(), SideA = sideA, SideB = sideB, ChainB = chainB, DirB = dir };
            r.Gap = new double[n]; r.S = new double[n]; r.Mx = new double[n]; r.My = new double[n]; r.Nx = new double[n]; r.Ny = new double[n];
            r.Kappa = new double[n]; r.Rise = new double[n]; r.Mid = new Point3d[n]; r.PA = new Point3d[n]; r.PB = new Point3d[n];
            var nx = new double[n]; var ny = new double[n];
            for (int k = 0; k < n; k++)
            {
                double ax = l.X[r.A[k]], ay = l.Y[r.A[k]], bx = At(l.X, r.B[k]), by = At(l.Y, r.B[k]);
                double dx = bx - ax, dy = by - ay, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) return null;
                r.Gap[k] = d; nx[k] = dx / d; ny[k] = dy / d; r.Mx[k] = 0.5 * (ax + bx); r.My[k] = 0.5 * (ay + by);
                double ha = l.H[r.A[k]], hb = At(l.H, r.B[k]);
                r.Rise[k] = hb - ha;
                r.Mid[k] = World(set, l, r.Mx[k], r.My[k], 0.5 * (ha + hb));
                r.PA[k] = World(set, l, ax, ay, ha); r.PB[k] = World(set, l, bx, by, hb);
                if (k > 0) { double sx = r.Mx[k] - r.Mx[k - 1], sy = r.My[k] - r.My[k - 1]; r.S[k] = r.S[k - 1] + Math.Sqrt(sx * sx + sy * sy); }
            }
            for (int k = 0; k < n; k++)
            {
                double x = 0, y = 0;
                for (int q = Math.Max(0, k - 2); q <= Math.Min(n - 1, k + 2); q++) { x += nx[q]; y += ny[q]; }
                double d = Math.Sqrt(x * x + y * y); if (d < 1e-9) { x = nx[k]; y = ny[k]; d = 1; }
                r.Nx[k] = x / d; r.Ny[k] = y / d;
            }
            // curvature of the two beads over one bead width
            double w = set.Path.Settings.BeadWidth;
            for (int k = 0; k < n; k++)
            {
                double ka = Curvature(l, r.A[k], 0.5 * w), kb = Curvature(l, (int)Math.Round(r.B[k]), 0.5 * w);
                r.Kappa[k] = Math.Max(ka, kb);
            }
            r.RoleA = l.Role[r.A[n / 2]]; r.RoleB = l.Role[(int)r.B[n / 2]];
            return r;
        }

        /// <summary>Turn of the path over a window of 2 x half around sample i, per mm (1 / radius).</summary>
        internal static double Curvature(WeaveLayer l, int i, double half)
        {
            int a = i, b = i, c = l.Chain[i];
            while (a > 0 && l.Chain[a - 1] == c && l.Arc[i] - l.Arc[a] < half) a--;
            while (b + 1 < l.N && l.Chain[b + 1] == c && l.Arc[b] - l.Arc[i] < half) b++;
            double len = l.Arc[b] - l.Arc[a];
            if (len < 1e-9 || b - a < 2) return 0;
            double turn = 0;
            for (int k = a + 1; k < b; k++)
            {
                double ux = l.X[k] - l.X[k - 1], uy = l.Y[k] - l.Y[k - 1], vx = l.X[k + 1] - l.X[k], vy = l.Y[k + 1] - l.Y[k];
                turn += Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            }
            return Math.Abs(turn) / Math.Max(len, half);
        }

        /// <summary>
        /// The zones of another search (an older state of the path) in a new one: new id -> index in old.
        /// Same layer, the seam within half a bead.
        /// </summary>
        public static Dictionary<int, int> Match(IList<WeaveZone> old, WeaveZoneSet set)
        {
            var map = new Dictionary<long, List<KeyValuePair<Point3d, int>>>();
            double w = set.Path.Settings.BeadWidth;
            Func<int, int, int, long> key = (l, x, y) => ((long)l * 73856093) ^ ((long)x * 19349663) ^ ((long)y * 83492791);
            for (int i = 0; i < old.Count; i++)
                foreach (var r in old[i].Runs)
                    for (int k = 0; k < r.Count; k += 4)
                    {
                        long q = key(r.Layer, (int)Math.Floor(r.Mid[k].X / w), (int)Math.Floor(r.Mid[k].Y / w));
                        if (!map.TryGetValue(q, out var list)) map[q] = list = new List<KeyValuePair<Point3d, int>>();
                        list.Add(new KeyValuePair<Point3d, int>(r.Mid[k], i));
                    }
            var res = new Dictionary<int, int>();
            foreach (var z in set.Zones)
            {
                var votes = new Dictionary<int, int>(); int asked = 0;
                foreach (var r in z.Runs)
                    for (int k = 0; k < r.Count; k += 8)
                    {
                        asked++; var p = r.Mid[k]; int gx = (int)Math.Floor(p.X / w), gy = (int)Math.Floor(p.Y / w); double bd = 0.36 * w * w; int bi = -1;
                        for (int x = gx - 1; x <= gx + 1; x++)
                            for (int y = gy - 1; y <= gy + 1; y++)
                                if (map.TryGetValue(key(r.Layer, x, y), out var list))
                                    foreach (var kv in list) { double ex = kv.Key.X - p.X, ey = kv.Key.Y - p.Y, d = ex * ex + ey * ey; if (d < bd) { bd = d; bi = kv.Value; } }
                        if (bi >= 0) { votes.TryGetValue(bi, out int c); votes[bi] = c + 1; }
                    }
                if (votes.Count == 0) continue;
                var best = votes.OrderByDescending(kv => kv.Value).First();
                if (best.Value * 3 >= asked) res[z.Id] = best.Key;
            }
            return res;
        }

        // ------------------------------------------------------------------ through the layers
        private sealed class Cell { public WeaveRun R; public int K; }

        private static void Track(WeaveZoneSet set, List<WeaveRun>[] layers)
        {
            double w = set.Path.Settings.BeadWidth;
            var zones = new List<WeaveZone>();
            var active = new List<KeyValuePair<int, WeaveRun>>(); // (layer number, run), youngest last
            var links = new Dictionary<long, List<double[]>>();   // zone pair -> votes (normal sign, s sign, s offset)
            for (int li = 0; li < layers.Length; li++)
            {
                Cancel.Check();
                var rs = layers[li];
                if (rs == null) continue;
                // the seams of the layers below (up to three back), youngest wins
                var map = new Dictionary<long, List<Cell>>();
                foreach (var kv in active)
                {
                    var r = kv.Value; int every = Math.Max(1, (int)(0.4 * w / set.Step));
                    for (int k = 0; k < r.Count; k += every)
                    {
                        long key = Key(r.Mx[k], r.My[k], w);
                        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Cell>();
                        list.Add(new Cell { R = r, K = k });
                    }
                }
                foreach (var r in rs)
                {
                    var votes = new Dictionary<int, List<double[]>>();
                    int every = Math.Max(1, (int)(0.5 * w / set.Step)), asked = 0;
                    for (int k = 0; k < r.Count; k += every)
                    {
                        asked++;
                        Cell best = null; double bd = 1.0 * w * w; int bestAge = -1;
                        int cx = (int)Math.Floor(r.Mx[k] / w), cy = (int)Math.Floor(r.My[k] / w);
                        for (int gx = cx - 1; gx <= cx + 1; gx++)
                            for (int gy = cy - 1; gy <= cy + 1; gy++)
                                if (map.TryGetValue(((long)gx << 32) ^ (uint)gy, out var list))
                                    foreach (var c in list)
                                    {
                                        double dx = c.R.Mx[c.K] - r.Mx[k], dy = c.R.My[c.K] - r.My[k], d2 = dx * dx + dy * dy;
                                        if (d2 > w * w) continue;
                                        if (Math.Abs(c.R.Nx[c.K] * r.Nx[k] + c.R.Ny[c.K] * r.Ny[k]) < 0.9) continue;
                                        double off = Math.Abs(dx * r.Nx[k] + dy * r.Ny[k]);
                                        if (off > 0.75 * w) continue;
                                        if (c.R.Layer > bestAge || (c.R.Layer == bestAge && d2 < bd)) { bestAge = c.R.Layer; bd = d2; best = c; }
                                    }
                        if (best == null) continue;
                        var br = best.R;
                        double dot = br.Nx[best.K] * r.Nx[k] + br.Ny[best.K] * r.Ny[k];
                        int nSign = (dot > 0 ? 1 : -1) * br.NSign;
                        // direction of growing s in both runs (tangent)
                        Tangent(br, best.K, out double tx, out double ty); Tangent(r, k, out double ux, out double uy);
                        double td = tx * ux + ty * uy;
                        int sSign = (td > 0 ? 1 : -1) * br.SSign;
                        double ex = r.Mx[k] - br.Mx[best.K], ey = r.My[k] - br.My[best.K];
                        double sAt = br.ZoneS(best.K) + br.SSign * (ex * tx + ey * ty);
                        if (!votes.TryGetValue(br.Zone, out var vl)) votes[br.Zone] = vl = new List<double[]>();
                        vl.Add(new[] { nSign, sSign, sAt - sSign * r.S[k] });
                    }
                    KeyValuePair<int, List<double[]>> win = default(KeyValuePair<int, List<double[]>>); int most = 0;
                    foreach (var kv in votes) if (kv.Value.Count > most) { most = kv.Value.Count; win = kv; }
                    if (most > 0 && most * 4 >= asked)
                    {
                        r.Zone = win.Key;
                        r.NSign = win.Value.Sum(v => v[0]) >= 0 ? 1 : -1;
                        r.SSign = win.Value.Sum(v => v[1]) >= 0 ? 1 : -1;
                        var off = win.Value.Where(v => (int)v[1] == r.SSign).Select(v => v[2]).OrderBy(v => v).ToList();
                        r.S0 = off[off.Count / 2];
                    }
                    else
                    {
                        r.Zone = zones.Count; r.S0 = -0.5 * r.Length;
                        zones.Add(new WeaveZone { Id = zones.Count, Set = set });
                    }
                    zones[r.Zone].Runs.Add(r);
                }
                Links(rs, links, set.Step, w);
                active.RemoveAll(kv => li - kv.Key >= 3);
                foreach (var r in rs) active.Add(new KeyValuePair<int, WeaveRun>(li, r));
            }
            // keep zones that go through enough layers, number them again from the longest seam on
            var keep = zones.Where(z => z.Runs.Select(r => r.Layer).Distinct().Count() >= Math.Max(1, set.Options.MinLayers)).ToList();
            foreach (var z in keep) Sum(z, w, set.Options);
            var old = keep.ToDictionary(z => z.Id);
            Bundles(old, links);
            foreach (var z in zones) if (!old.ContainsKey(z.Id)) foreach (var r in z.Runs) r.Zone = -1;
            keep = keep.OrderByDescending(z => z.TotalLength).ToList();
            for (int i = 0; i < keep.Count; i++) { keep[i].Id = i; foreach (var r in keep[i].Runs) r.Zone = i; }
            set.Zones = keep;
        }

        private static void Tangent(WeaveRun r, int k, out double tx, out double ty)
        {
            int a = Math.Max(0, k - 2), b = Math.Min(r.Count - 1, k + 2);
            tx = r.Mx[b] - r.Mx[a]; ty = r.My[b] - r.My[a];
            double d = Math.Sqrt(tx * tx + ty * ty); if (d > 1e-9) { tx /= d; ty /= d; }
        }

        private static long Key(double x, double y, double cell) => ((long)(int)Math.Floor(x / cell) << 32) ^ (uint)(int)Math.Floor(y / cell);

        /// <summary>Two zones that share a bead (three beads side by side) should move together.</summary>
        private static void Links(List<WeaveRun> rs, Dictionary<long, List<double[]>> links, double step, double w)
        {
            if (rs.Count < 2) return;
            var at = new Dictionary<int, KeyValuePair<WeaveRun, int>>(); // sample -> run, station
            foreach (var r in rs)
                for (int k = 0; k < r.Count; k += 6)
                    for (int s = 0; s < 2; s++)
                    {
                        int i = s == 0 ? r.A[k] : (int)Math.Round(r.B[k]);
                        for (int q = -3; q <= 3; q++)
                            if (at.TryGetValue(i + q, out var other) && other.Key != r && other.Key.Zone != r.Zone)
                            {
                                var o = other.Key; int m = other.Value;
                                double nd = (o.Nx[m] * r.Nx[k] + o.Ny[m] * r.Ny[k]) * o.NSign * r.NSign;
                                if (Math.Abs(nd) < 0.8) continue;
                                Tangent(o, m, out double tx, out double ty); Tangent(r, k, out double ux, out double uy);
                                int sd = (tx * ux + ty * uy > 0 ? 1 : -1) * o.SSign * r.SSign;
                                int a = Math.Min(o.Zone, r.Zone), b = Math.Max(o.Zone, r.Zone);
                                double sa = o.Zone == a ? o.ZoneS(m) : r.ZoneS(k), sb = o.Zone == a ? r.ZoneS(k) : o.ZoneS(m);
                                long key = ((long)a << 32) | (uint)b;
                                if (!links.TryGetValue(key, out var list)) links[key] = list = new List<double[]>();
                                if (list.Count < 4000) list.Add(new[] { nd > 0 ? 1.0 : -1.0, sd, sa - sd * sb });
                                break;
                            }
                        if (!at.ContainsKey(i)) at[i] = new KeyValuePair<WeaveRun, int>(r, k);
                    }
        }

        private static void Bundles(Dictionary<int, WeaveZone> zones, Dictionary<long, List<double[]>> links)
        {
            var next = new Dictionary<int, List<KeyValuePair<int, List<double[]>>>>();
            foreach (var kv in links)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                if (!zones.ContainsKey(a) || !zones.ContainsKey(b) || kv.Value.Count < 3) continue;
                foreach (var p in new[] { new[] { a, b }, new[] { b, a } })
                {
                    if (!next.TryGetValue(p[0], out var l)) next[p[0]] = l = new List<KeyValuePair<int, List<double[]>>>();
                    l.Add(new KeyValuePair<int, List<double[]>>(p[1], kv.Value));
                }
            }
            var seen = new HashSet<int>(); int bundle = 0;
            foreach (var z in zones.Values.OrderByDescending(v => v.TotalLength))
            {
                if (!seen.Add(z.Id)) continue;
                z.Bundle = ++bundle;
                var todo = new Queue<int>(); todo.Enqueue(z.Id);
                while (todo.Count > 0)
                {
                    int i = todo.Dequeue(); var zi = zones[i];
                    if (!next.TryGetValue(i, out var l)) continue;
                    foreach (var kv in l)
                    {
                        if (!seen.Add(kv.Key)) continue;
                        var zj = zones[kv.Key]; zj.Bundle = bundle;
                        int nd = kv.Value.Sum(v => v[0]) >= 0 ? 1 : -1, sd = kv.Value.Sum(v => v[1]) >= 0 ? 1 : -1;
                        var off = kv.Value.Where(v => (int)v[1] == sd).Select(v => v[2]).OrderBy(v => v).ToList();
                        double m = off[off.Count / 2]; // s(smaller id) = sd * s(larger id) + m
                        zj.BundleSign = zi.BundleSign * nd;
                        // pattern coordinate p = dir * s + s0; make both equal at the shared bead
                        if (kv.Key > i) { zj.BundleSDir = zi.BundleSDir * sd; zj.BundleS0 = zi.BundleS0 + zi.BundleSDir * m; }
                        else { zj.BundleSDir = zi.BundleSDir * sd; zj.BundleS0 = zi.BundleS0 - zi.BundleSDir * sd * m; }
                        todo.Enqueue(kv.Key);
                    }
                }
            }
        }

        private static void Sum(WeaveZone z, double w, WeaveFindOptions o)
        {
            var layers = z.Runs.Select(r => r.Layer).Distinct().ToList();
            z.FirstLayer = layers.Min(); z.LastLayer = layers.Max(); z.LayerCount = layers.Count;
            z.TotalLength = z.Runs.Sum(r => r.Length); z.MeanLength = z.TotalLength / layers.Count;
            z.GapMin = double.MaxValue; z.GapMax = 0; z.SMin = double.MaxValue; z.SMax = double.MinValue;
            // the middle of the spot: where the beads are nearest (weighted), smooth over seven layers
            var mid = new SortedDictionary<int, double>();
            foreach (var g in z.Runs.GroupBy(r => r.Layer))
            {
                var r = g.OrderByDescending(q => q.Length).First(); double lo = r.Gap.Min(), hi = r.Gap.Max(), a = 0, b = 0;
                for (int k = 0; k < r.Count; k++) { double wt = hi - lo < 0.05 * w ? 1 : Math.Pow((hi - r.Gap[k]) / (hi - lo), 2) + 1e-3; a += wt * r.ZoneS(k); b += wt; }
                mid[g.Key] = a / b;
            }
            foreach (var r in z.Runs)
            {
                // a straight line through the neighbouring layers
                double s0 = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int q = r.Layer - 3; q <= r.Layer + 3; q++)
                    if (mid.TryGetValue(q, out double m)) { double xq = q - r.Layer; s0++; sx += xq; sy += m; sxx += xq * xq; sxy += xq * m; }
                double det = s0 * sxx - sx * sx;
                r.Centre = det > 1e-9 ? (sy * sxx - sx * sxy) / det : sy / Math.Max(1, s0);
            }
            double sum = 0; int n = 0; var c = new int[4]; double x = 0, y = 0, zz = 0; var roles = new Dictionary<string, int>();
            foreach (var r in z.Runs)
            {
                for (int k = 0; k < r.Count; k++)
                {
                    double g = r.Gap[k]; sum += g; n++; z.GapMin = Math.Min(z.GapMin, g); z.GapMax = Math.Max(z.GapMax, g);
                    c[(int)ClassOf(g, w, o)]++;
                    double s = r.ZoneS(k); z.SMin = Math.Min(z.SMin, s); z.SMax = Math.Max(z.SMax, s);
                }
                var m = r.Mid[r.Count / 2]; x += m.X; y += m.Y; zz += m.Z;
                string name = r.RoleA <= r.RoleB ? r.RoleA + "-" + r.RoleB : r.RoleB + "-" + r.RoleA;
                roles.TryGetValue(name, out int had); roles[name] = had + 1;
            }
            z.GapMean = sum / Math.Max(1, n);
            z.Class = (WeaveClass)Array.IndexOf(c, c.Max());
            z.Roles = roles.OrderByDescending(kv => kv.Value).First().Key;
            z.Centre = new Point3d(x / z.Runs.Count, y / z.Runs.Count, zz / z.Runs.Count);
        }

        public static WeaveClass ClassOf(double gap, double w, WeaveFindOptions o)
            => gap < w * (1 - Math.Max(o.Press, o.Touch)) ? WeaveClass.Pressed : gap < w * (1 - o.Touch) ? WeaveClass.Fused : gap <= w * (1 + o.Touch) ? WeaveClass.Touching : WeaveClass.Almost;

        private static void Describe(WeaveZoneSet set)
        {
            double w = set.Path.Settings.BeadWidth; var o = set.Options;
            set.Report.Add(set.Verdict);
            set.Report.Add(Inv.F($"Bead {w:0.##} mm: pressed = {w * (1 - o.Closer):0.0#} to {w * (1 - o.Press):0.0#} mm between the centre lines, fused = up to {w * (1 - o.Touch):0.0#} mm, touching = up to {w * (1 + o.Touch):0.0#} mm, almost touching = up to {w * (1 + o.Further):0.0#} mm."));
            foreach (var z in set.Zones.Take(40)) set.Report.Add(z.ToString());
            if (set.Zones.Count > 40) set.Report.Add(Inv.F($"... and {set.Zones.Count - 40} more, shorter ones."));
            set.Report.Add(Inv.F($"{set.LayerCount} layers, {set.Samples} samples, {set.Seconds:0.00} s"));
            if (set.Climbs)
            {
                var all = set.Zones.SelectMany(z => z.Runs).ToList();
                double len = all.Sum(r => r.Length), off = all.Where(r => r.Rise.Average(v => Math.Abs(v)) > 0.25 * set.Path.Settings.LayerHeight).Sum(r => r.Length);
                if (len > 0) set.SpiralNote = Inv.F($"This path is a spiral: the two beads of a seam can lie at different heights. {100 * off / len:0} % of the seam is more than a quarter layer off; there the pattern gets smaller (nothing at half a layer). Use Layered in Continuous Path to weave everywhere.");
                if (off > 0.05 * len) set.Report.Add(set.SpiralNote); else set.SpiralNote = "";
            }
            if (set.Path.Warp != null && set.Path.Warp.UseBeadLaw) set.Report.Add("Layers that follow the surface are looked at from above: only for walls that are not too flat.");
        }
    }
}
