using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    public enum WeavePattern { Shift = 0, Width = 1, Snake = 2, Teeth = 3, Custom = 4, Plain = 5 }

    /// <summary>What the face of a bead does that looks away from the seam.</summary>
    public enum WeaveFaces
    {
        /// <summary>Walls keep their face (the bead gets wider and narrower instead), infill moves as a whole.</summary>
        Auto = 0,
        /// <summary>Both beads move as a whole.</summary>
        Move = 1,
        /// <summary>Both outer faces stay: one bead wide, the other one narrow.</summary>
        Keep = 2
    }

    /// <summary>Settings of one woven zone. Lengths in mm.</summary>
    public sealed class WeaveSettings
    {
        public WeavePattern Pattern = WeavePattern.Shift;
        /// <summary>How far the seam leaves the middle. 0 = a quarter of the bead width.</summary>
        public double Amplitude = 0;
        /// <summary>Snake: length of one wave. Teeth: distance of the teeth. 0 = automatic (shortest printable wave x 1.2; teeth 50 mm).</summary>
        public double Wavelength = 0;
        /// <summary>Teeth: length of one tooth. 0 = the shortest printable one x 1.2.</summary>
        public double ToothLength = 0;
        /// <summary>Snake, Teeth: how far the pattern moves on per layer, as a fraction of the wave. Negative = automatic (Snake 0.5 = woven, Teeth 0).</summary>
        public double Phase = -1;
        /// <summary>Layers printed with the same row before the pattern moves on.</summary>
        public int LayerPeriod = 1;
        /// <summary>Custom: offset per layer, as a fraction of the amplitude.</summary>
        public double[] Offsets = { 0, 1, 0, -1 };
        public WeaveFaces Faces = WeaveFaces.Auto;
        /// <summary>Length over which the pattern grows from nothing at its ends. 0 = as short as the bead allows.</summary>
        public double Fade = 0;
        /// <summary>Woven length per zone, mm. 0 = the whole zone.</summary>
        public double Extent = 0;
        /// <summary>Where the woven length lies in the zone: 0 = start, 0.5 = middle, 1 = end.</summary>
        public double Position = 0.5;
        /// <summary>The woven length again every ... mm. 0 = once.</summary>
        public double Repeat = 0;
        /// <summary>Largest sideways step from layer to layer, in bead widths.</summary>
        public double StepLimit = 0.5;
        /// <summary>Limits of the bead width, as a fraction of the nominal width.</summary>
        public double WidthMin = 0.7, WidthMax = 1.4;
        /// <summary>The bead width follows the distance of the two beads: wider where they are too far apart to fuse, narrower
        /// where they are pressed into each other, so they overlap as much as planned. The outer faces stay where they are.</summary>
        public bool Fit = true;
        /// <summary>Also weave where the beads do not quite touch.</summary>
        public bool Almost = true;
        /// <summary>The pattern follows the spot when it wanders with the height (always for spots shorter than two waves).</summary>
        public bool Follow = false;
        /// <summary>The pattern is counted from the middle of every woven stretch (a tooth in the middle of a region).</summary>
        public bool Centred = false;
        /// <summary>No pattern on the first layer (the widths are fitted there too).</summary>
        public bool PlainFirstLayer = true;
        /// <summary>Only these layers (path layer numbers).</summary>
        public int FromLayer = int.MinValue, ToLayer = int.MaxValue;

        public WeaveSettings Duplicate() { var d = (WeaveSettings)MemberwiseClone(); d.Offsets = Offsets == null ? null : (double[])Offsets.Clone(); return d; }

        // ------------------------------------------------------------ the rules
        /// <summary>Half a bead plus half the spacing: the smallest radius the seam may have, so the bead on
        /// the inner side of the bend still has half a bead width as radius.</summary>
        public static double SeamRadius(double w, double spacing) => 0.5 * (w + spacing);
        /// <summary>Shortest sine wave of this amplitude: 2 pi sqrt(a (w + s) / 2).</summary>
        public static double MinWave(double a, double w, double spacing) => 2 * Math.PI * Math.Sqrt(Math.Max(0, a) * SeamRadius(w, spacing));
        /// <summary>Shortest tooth (raised cosine) of this height: pi sqrt(a (w + s)).</summary>
        public static double MinTooth(double a, double w, double spacing) => Math.PI * Math.Sqrt(Math.Max(0, a) * (w + spacing));
        /// <summary>Shortest fade (two parabolas) to this offset: sqrt(2 a (w + s)).</summary>
        public static double MinFade(double a, double w, double spacing) => Math.Sqrt(2 * Math.Max(0, a) * (w + spacing));

        internal double PhaseUsed => Phase >= 0 ? Phase : Pattern == WeavePattern.Snake ? 0.5 : 0;

        /// <summary>Row number of a layer.</summary>
        internal int Row(int layer) { int p = Math.Max(1, LayerPeriod); return layer >= 0 ? layer / p : -((-layer + p - 1) / p); }

        /// <summary>The pattern, -1..1, at place s on a layer. L = wave or tooth distance, b = tooth length.</summary>
        internal double Unit(double s, int layer, double L, double b)
        {
            int k = Row(layer);
            switch (Pattern)
            {
                case WeavePattern.Snake: return Math.Sin(2 * Math.PI * (s / L + PhaseUsed * k));
                case WeavePattern.Teeth:
                {
                    double x = s / L + PhaseUsed * k; x -= Math.Round(x); x *= L;
                    double v = Math.Abs(x) < 0.5 * b ? 0.5 * (1 + Math.Cos(2 * Math.PI * x / b)) : 0;
                    return (k & 1) == 0 ? v : -v;
                }
                case WeavePattern.Custom:
                {
                    if (Offsets == null || Offsets.Length == 0) return 0;
                    int m = k % Offsets.Length; if (m < 0) m += Offsets.Length;
                    return Offsets[m];
                }
                case WeavePattern.Plain: return 0;
                default: return (k & 1) == 0 ? 1 : -1;
            }
        }

        /// <summary>Largest change of the pattern from one layer to the next, for amplitude 1.</summary>
        internal double LayerChange()
        {
            double best = 0, L = 1, b = 0.4;
            int cycle = Math.Max(24, (Offsets == null ? 1 : Offsets.Length) * 2) * Math.Max(1, LayerPeriod);
            for (int k = 0; k < cycle; k++)
                for (int i = 0; i < 80; i++) best = Math.Max(best, Math.Abs(Unit(i / 80.0, k, L, b) - Unit(i / 80.0, k + 1, L, b)));
            return best;
        }
        internal double Peak => Pattern == WeavePattern.Custom && Offsets != null && Offsets.Length > 0 ? Math.Max(1e-9, Offsets.Max(v => Math.Abs(v))) : 1;
    }

    /// <summary>Settings for some zones and places. The first rule that fits a place wins; no rule = not woven.</summary>
    public sealed class WeaveRule
    {
        public WeaveSettings Settings = new WeaveSettings();
        /// <summary>Zone ids (null = all).</summary>
        public HashSet<int> Zones;
        /// <summary>Only inside these regions (null or empty = everywhere).</summary>
        public List<WeaveRegion> Regions;
    }

    public sealed class WeaveResult
    {
        public Toolpath Path;
        public WeaveZoneSet Set;
        public List<string> Report = new List<string>();
        public List<string> Warnings = new List<string>();
        public int WovenZones, WovenRuns, MovedPoints, DampedRuns, DampedPlaces, Layers;
        /// <summary>Length of seam that is woven (more than a tenth of the amplitude), over all layers, mm.</summary>
        public double WovenLength;
        public double MaxOffset, WidthLow = double.MaxValue, WidthHigh;
        public double Seconds;
        public string Verdict = "";
    }

    /// <summary>
    /// Interlocking beads. Where two beads lie side by side the seam between them is moved: from layer
    /// to layer to the left and right (Shift, Width), as a wave (Snake), in local teeth, or by a list.
    /// Both beads follow the seam, so their spacing stays what it was: no gap, no extra material. The
    /// layers grip into each other because the seam of one layer lies over a bead of the layer below.
    /// The path stays one line; only points move, nothing is cut or added.
    /// <para>A bead has two faces. The face at a seam moves with the seam. The other face either
    /// follows (the bead moves as a whole) or stays (the bead gets wider or narrower: the extrusion
    /// follows through Toolpath.Widths). A bead with a seam on both sides gets both. So a bead never
    /// pushes into a third one, and a wall can keep its outside smooth.</para>
    /// <para>The seam is a wave of amplitude a and length L; the beads are its offsets by half the
    /// spacing s. The bead on the inner side of a bend needs half a bead width as radius, so the seam
    /// needs the radius (w + s) / 2: L >= 2 pi sqrt(a (w + s) / 2).</para>
    /// </summary>
    public static class Weave
    {
        public static readonly string[] PatternNames =
        {
            "Shift (seam left and right, layer by layer)",
            "Width (outer faces stay, wide and narrow beads)",
            "Snake (waves that cross from layer to layer)",
            "Teeth (straight, with a small tooth every ...)",
            "Custom (list of offsets per layer)",
            "Plain (no pattern, only fit the bead widths)",
        };

        public static WeaveResult Apply(Toolpath tp, WeaveZoneSet set, WeaveSettings s) => Apply(tp, set, new[] { new WeaveRule { Settings = s } });

        public static WeaveResult Apply(Toolpath tp, WeaveZoneSet set, IList<WeaveRule> rules)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var res = new WeaveResult { Set = set, Path = tp };
            if (set.Zones.Count == 0 || rules == null || rules.Count == 0)
            {
                res.Path = tp.Duplicate(); res.Verdict = set.Zones.Count == 0 ? "NOTHING WOVEN: no zones." : "NOTHING WOVEN: no settings.";
                res.Report.Add(res.Verdict); return res;
            }
            if (set.SpiralNote.Length > 0) res.Warnings.Add(set.SpiralNote);
            foreach (var r in rules) CheckSettings(r.Settings, tp.Settings, res);
            var ranges = WeaveFinder.Split(set.Flat);
            res.Layers = ranges.Count;
            int firstLayer = ranges.Count > 0 ? ranges[0].Layer : 0;
            var byLayer = new Dictionary<int, List<WeaveRun>>();
            foreach (var z in set.Zones) foreach (var r in z.Runs) { if (!byLayer.TryGetValue(r.Layer, out var l)) byLayer[r.Layer] = l = new List<WeaveRun>(); l.Add(r); }
            var done = new LayerResult[ranges.Count];
            Parallel.For(0, ranges.Count, new ParallelOptions { CancellationToken = Cancel.Token }, i =>
            {
                List<WeaveRun> runs;
                if (ranges[i].Climb) runs = byLayer.Values.SelectMany(v => v).ToList();
                else if (!byLayer.TryGetValue(ranges[i].Layer, out runs)) return;
                done[i] = One(set, ranges[i], i > 0 ? ranges[i - 1] : null, i + 1 < ranges.Count ? ranges[i + 1] : null, runs, rules, firstLayer);
            });
            // the new path
            var moves = new Dictionary<int, List<NewPoint>>();
            var zones = new HashSet<int>();
            foreach (var d in done)
            {
                if (d == null) continue;
                foreach (var kv in d.Moves) moves[kv.Key] = kv.Value;
                foreach (int z in d.Zones) zones.Add(z);
                res.WovenRuns += d.Runs; res.DampedRuns += d.DampedRuns; res.DampedPlaces += d.DampedPlaces; res.WovenLength += d.Length;
                res.MaxOffset = Math.Max(res.MaxOffset, d.MaxOffset); res.WidthLow = Math.Min(res.WidthLow, d.WidthLow); res.WidthHigh = Math.Max(res.WidthHigh, d.WidthHigh);
                res.MovedPoints += d.Moved;
            }
            res.WovenZones = zones.Count;
            res.Path = Assemble(tp, set, moves);
            res.Seconds = clock.Elapsed.TotalSeconds;
            if (res.WidthLow > 1e8) { res.WidthLow = tp.Settings.BeadWidth; res.WidthHigh = tp.Settings.BeadWidth; }
            double total = set.Zones.Sum(z => z.TotalLength);
            res.Verdict = res.WovenRuns == 0
                ? "NOTHING WOVEN: the zones are too short, too curved or outside the settings for this bead width."
                : Inv.F($"WOVEN: {res.WovenZones} of {set.Zones.Count} zone(s), {res.WovenLength / 1000:0.0} of {total / 1000:0.0} m of seam; seam offset up to {res.MaxOffset:0.00} mm.");
            res.Report.Insert(0, res.Verdict);
            res.Report.Add(Inv.F($"Bead width {res.WidthLow:0.00} to {res.WidthHigh:0.00} mm (nominal {tp.Settings.BeadWidth:0.##}). {res.Path.Count} points ({tp.Count} before), {res.Seconds:0.0} s."));
            if (res.DampedPlaces > 0) res.Report.Add(Inv.F($"The pattern was made smaller at {res.DampedPlaces} place(s) in {res.DampedRuns} stretch(es), where a bead would have been pressed into another one, opened a gap or got too tight a bend."));
            return res;
        }

        private static void CheckSettings(WeaveSettings s, PrintSettings st, WeaveResult res)
        {
            double w = st.BeadWidth, sp = st.BeadSpacing, a = AmplitudeOf(s, w);
            if (s.Pattern == WeavePattern.Snake)
            {
                double lm = WeaveSettings.MinWave(a, w, sp);
                if (s.Wavelength > 0 && s.Wavelength < lm) res.Warnings.Add(Inv.F($"Wavelength {s.Wavelength:0.#} mm is too short for amplitude {a:0.##} mm and a {w:0.##} mm bead: {lm:0.#} mm is used."));
            }
            if (s.Pattern == WeavePattern.Teeth)
            {
                double b = WeaveSettings.MinTooth(a, w, sp);
                if (s.ToothLength > 0 && s.ToothLength < b) res.Warnings.Add(Inv.F($"Tooth length {s.ToothLength:0.#} mm is too short for a {a:0.##} mm tooth: {b:0.#} mm is used."));
            }
            double step = s.LayerChange() * a, lim = s.StepLimit * w;
            if (s.Faces == WeaveFaces.Move || s.Pattern != WeavePattern.Width && s.Faces == WeaveFaces.Auto)
                if (step > lim + 1e-9) res.Warnings.Add(Inv.F($"Amplitude {a:0.##} mm would move the beads {step:0.##} mm from layer to layer, more than {s.StepLimit:0.##} bead widths ({lim:0.##} mm): it is made smaller where a bead moves as a whole."));
        }

        internal static double AmplitudeOf(WeaveSettings s, double w) => s.Amplitude > 0 ? s.Amplitude : 0.25 * w;

        // ------------------------------------------------------------------ one layer
        internal struct NewPoint { public double U, Dx, Dy, Dw; public bool Vertex; }

        private sealed class LayerResult
        {
            public Dictionary<int, List<NewPoint>> Moves = new Dictionary<int, List<NewPoint>>();
            public HashSet<int> Zones = new HashSet<int>();
            public int Runs, DampedRuns, DampedPlaces, Moved; public double Length, MaxOffset, WidthLow = double.MaxValue, WidthHigh;
        }

        private sealed class Work
        {
            public WeaveRun R; public WeaveZone Z;
            public int[] Rule;          // per station: which rule (-1 none)
            public double[] Limit;      // largest amplitude per station
            public double[] Damp, DampE; // 1 = as wanted (pattern, fit of the widths)
            public double[] D, Dp, E;   // seam offset (A to B), its slope, closing of the gap
            public double[] Full;       // amplitude reached
            public bool Damped;
        }

        private static LayerResult One(WeaveZoneSet set, WeaveFinder.LayerRange range, WeaveFinder.LayerRange under, WeaveFinder.LayerRange over, List<WeaveRun> runs, IList<WeaveRule> rules, int firstLayer)
        {
            var l = WeaveFinder.Sample(set.Flat, range, set.Step);
            double w = set.Path.Settings.BeadWidth, step = set.Step, sp = set.Path.Settings.BeadSpacing;
            int n = l.N;
            foreach (var r in runs) if (r.A[r.Count - 1] >= n || r.B.Max() > n - 1) return null; // another path
            Around(l, w, set.Options);

            // which run lies on which side of a sample
            var cov = new[] { new int[n], new int[n] };
            for (int i = 0; i < n; i++) cov[0][i] = cov[1][i] = -1;
            for (int q = 0; q < runs.Count; q++)
            {
                var r = runs[q];
                for (int k = 0; k < r.Count; k++) cov[r.SideA][r.A[k]] = q;
                int b0 = (int)Math.Ceiling(Math.Min(r.B[0], r.B[r.Count - 1])), b1 = (int)Math.Floor(Math.Max(r.B[0], r.B[r.Count - 1]));
                for (int j = b0; j <= b1; j++) if (cov[r.SideB][j] < 0) cov[r.SideB][j] = q;
            }

            // the rule per station
            var works = new List<Work>();
            double aMax = 0; foreach (var rl in rules) aMax = Math.Max(aMax, AmplitudeOf(rl.Settings, w) * rl.Settings.Peak);
            bool any = false;
            foreach (var r in runs)
            {
                var k = new Work { R = r, Z = set.Zones[r.Zone], Rule = new int[r.Count], Damp = new double[r.Count], DampE = new double[r.Count], Limit = new double[r.Count] };
                for (int i = 0; i < r.Count; i++)
                {
                    k.Damp[i] = 1; k.DampE[i] = 1; k.Rule[i] = -1;
                    for (int q = 0; q < rules.Count; q++)
                    {
                        var ru = rules[q]; var s = ru.Settings;
                        if (ru.Zones != null && !ru.Zones.Contains(r.Zone)) continue;
                        if (ru.Regions != null && ru.Regions.Count > 0 && !ru.Regions.Any(g => g.Contains(r.Mid[i]))) continue;
                        if (r.Layer < s.FromLayer || r.Layer > s.ToLayer) continue;
                        if (s.Extent > 0)
                        {
                            double c = k.Z.SMin + (k.Z.SMax - k.Z.SMin) * Math.Max(0, Math.Min(1, s.Position)), x = r.ZoneS(i) - c;
                            if (s.Repeat > 1e-6) x -= s.Repeat * Math.Round(x / s.Repeat);
                            if (Math.Abs(x) > 0.5 * s.Extent) { k.Rule[i] = -1; break; }
                        }
                        k.Rule[i] = q; any = true; break;
                    }
                }
                works.Add(k);
            }
            if (!any) return null;

            // does the free face follow? per side and sample, soft along the path
            var follow = new[] { new double[n], new double[n] };
            for (int sd = 0; sd < 2; sd++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (cov[sd][i] >= 0) { follow[sd][i] = 1; continue; } // a seam: not a free face
                    int q = cov[1 - sd][i];
                    WeaveFaces f = WeaveFaces.Auto; bool width = false;
                    if (q >= 0)
                    {
                        var wk = works[q]; int st = Math.Max(0, Math.Min(wk.R.Count - 1, wk.R.SideA == 1 - sd && i >= wk.R.A[0] && i <= wk.R.A[wk.R.Count - 1] ? i - wk.R.A[0] : 0));
                        int ru = wk.Rule[st]; if (ru < 0) ru = wk.Rule.FirstOrDefault(v => v >= 0);
                        if (ru >= 0) { f = rules[ru].Settings.Faces; width = rules[ru].Settings.Pattern == WeavePattern.Width; }
                    }
                    bool keep = width || f == WeaveFaces.Keep || (f == WeaveFaces.Auto && l.Role[i] != PathRole.Infill && l.Role[i] != PathRole.Bottom);
                    double room = sd == 0 ? l.Any0[i] : l.Any1[i];
                    if (room < Math.Max(1.3 * w, w + aMax)) keep = true; // a third bead: neither press into it nor leave it
                    follow[sd][i] = keep ? 0 : 1;
                }
                follow[sd] = Soft(l, follow[sd], w);
            }

            // limits per station
            var changes = rules.Select(q => q.Settings.LayerChange() / q.Settings.Peak).ToArray();
            var others = new List<WeaveLayer>(); var otherGrids = new List<WeaveFinder.Grid>();
            if (l.Climb) { var o = WeaveFinder.Sample(set.Flat, range, 1e9); others.Add(o); otherGrids.Add(new WeaveFinder.Grid(o, 2 * w)); }
            else
                foreach (var rg in new[] { under, over })
                    if (rg != null) { var o = WeaveFinder.Sample(set.Flat, rg, 1e9); others.Add(o); otherGrids.Add(new WeaveFinder.Grid(o, 2 * w)); }
            Func<int, double> beside = i =>
            {
                double d = 0;
                if (l.Climb)
                {
                    foreach (int dir in new[] { -1, 1 })
                    {
                        double q = WeaveCheck.Distance(others[0], otherGrids[0], l.X[i], l.Y[i], 2 * w, l.Lap[i], l.H[i], dir);
                        if (q < 1.99 * w) d = Math.Max(d, q);
                    }
                    return d;
                }
                for (int o = 0; o < others.Count; o++) d = Math.Max(d, WeaveCheck.Distance(others[o], otherGrids[o], l.X[i], l.Y[i], 2 * w));
                return d;
            };
            foreach (var k in works)
            {
                var r = k.R;
                // how far the two beads lie beside the layer below and above already
                var lean = new double[r.Count]; int se = Math.Max(1, (int)(0.4 * w / step));
                for (int i = 0; i < r.Count; i += se)
                {
                    double d = Math.Max(beside(r.A[i]), beside((int)Math.Round(r.B[i])));
                    for (int q = Math.Max(0, i - se); q <= Math.Min(r.Count - 1, i + se); q++) lean[q] = Math.Max(lean[q], d);
                }
                for (int i = 0; i < r.Count; i++)
                {
                    if (k.Rule[i] < 0 || r.Layer == firstLayer && rules[k.Rule[i]].Settings.PlainFirstLayer) { k.Limit[i] = 0; continue; }
                    var s = rules[k.Rule[i]].Settings;
                    double a = AmplitudeOf(s, w) * s.Peak, lim = a;
                    double g = r.Gap[i], rm = WeaveSettings.SeamRadius(w, Math.Min(g, w)), kap = r.Kappa[i], bend = 1 / rm - kap;
                    if (bend <= 0) { k.Limit[i] = 0; continue; }
                    Lengths(s, a, w, out double L, out double b);
                    if (s.Pattern == WeavePattern.Snake) lim = Math.Min(lim, bend * L * L / (4 * Math.PI * Math.PI));
                    else if (s.Pattern == WeavePattern.Teeth) lim = Math.Min(lim, bend * b * b / (2 * Math.PI * Math.PI));
                    else if (kap > 1e-6) lim = Math.Min(lim, Math.Max(0, 1 / kap - rm));
                    if (g > w * (1 + set.Options.Touch) && !s.Almost && !r.Forced) { k.Limit[i] = 0; continue; }
                    // the faces that stay: the bead changes its width
                    int ja = r.A[i], jb = (int)Math.Round(r.B[i]);
                    double fa = follow[1 - r.SideA][ja], fb = follow[1 - r.SideB][jb];
                    bool twoA = cov[1 - r.SideA][ja] >= 0, twoB = cov[1 - r.SideB][jb] >= 0;
                    double close = Closing(s, g, w, sp);
                    double dw = Math.Max(0, Math.Min((1 - s.WidthMin) * w + close, (s.WidthMax - 1) * w - close));
                    double ka = twoA ? 0 : 1 - fa, kb = twoB ? 0 : 1 - fb;
                    bool steady = s.Pattern != WeavePattern.Snake && s.Pattern != WeavePattern.Teeth;
                    if ((twoA || twoB) && !steady) lim = Math.Min(lim, dw); // a bead with a seam on both sides: the two can differ
                    if (ka > 0.02) lim = Math.Min(lim, dw / ka);
                    if (kb > 0.02) lim = Math.Min(lim, dw / kb);
                    // layer to layer: the middle of a bead moves by 1 - k/2 of the seam
                    double move = 1 - 0.5 * Math.Min(ka, kb), change = changes[k.Rule[i]] * move;
                    if (change > 1e-6) lim = Math.Min(lim, Math.Max(0, s.StepLimit * w - lean[i]) / change);
                    // a spiral: where the two beads are half a layer apart, one bead touches two layers of the other: no pattern fits
                    if (l.Climb) lim *= Math.Max(0, 1 - 2 * Math.Abs(r.Rise[i]) / l.LayerHeight);
                    k.Limit[i] = Math.Max(0, lim);
                }
            }

            double[] nx = null, ny = null, nw = null, dx = null, dy = null;
            for (int round = 0; ; round++)
            {
                foreach (var k in works) Waves(k, rules, w, sp, step, k.R.Layer);
                Place(l, works, cov, follow, w, out dx, out dy, out nw);
                nx = new double[n]; ny = new double[n];
                for (int i = 0; i < n; i++) { nx[i] = l.X[i] + dx[i]; ny[i] = l.Y[i] + dy[i]; }
                if (round == 6) break;
                if (!Damp(l, works, cov, rules, set, nx, ny, dx, dy, nw, round)) break;
            }

            var res = new LayerResult();
            foreach (var k in works)
            {
                double len = 0;
                for (int i = 1; i < k.R.Count; i++)
                    if (k.Rule[i] >= 0)
                    {
                        double a = AmplitudeOf(rules[k.Rule[i]].Settings, w);
                        if (k.Full[i] > 0.1 * a || rules[k.Rule[i]].Settings.Pattern == WeavePattern.Plain && Math.Abs(k.E[i]) > 0.01 * w) len += k.R.S[i] - k.R.S[i - 1];
                        res.MaxOffset = Math.Max(res.MaxOffset, Math.Abs(k.D[i]));
                    }
                if (len > 0) { res.Runs++; res.Length += len; res.Zones.Add(k.R.Zone); }
                if (k.Damped) { res.DampedRuns++; for (int i = 0; i < k.Damp.Length; i++) if (k.Damp[i] < 0.999 && (i == 0 || k.Damp[i - 1] >= 0.999)) res.DampedPlaces++; }
            }
            // the moves that change
            const double eps = 1e-4;
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(nw[i] - l.W[i]) > 0.01) { res.WidthLow = Math.Min(res.WidthLow, nw[i]); res.WidthHigh = Math.Max(res.WidthHigh, nw[i]); }
                if (i == 0 || l.Chain[i] != l.Chain[i - 1]) continue;
                bool m = Math.Abs(dx[i]) + Math.Abs(dy[i]) + Math.Abs(nw[i] - l.W[i]) > eps, p = Math.Abs(dx[i - 1]) + Math.Abs(dy[i - 1]) + Math.Abs(nw[i - 1] - l.W[i - 1]) > eps;
                if (m) res.Moved++;
                if (!m && !p) continue;
                int src = l.Src[i];
                if (!res.Moves.ContainsKey(src)) res.Moves[src] = new List<NewPoint>();
            }
            var keys = new HashSet<int>(res.Moves.Keys);
            for (int i = 1; i < n; i++)
            {
                if (l.Chain[i] != l.Chain[i - 1] || !keys.Contains(l.Src[i])) continue;
                var list = res.Moves[l.Src[i]];
                double u0 = l.Src[i - 1] == l.Src[i] && !l.Vertex[i - 1] ? l.T[i - 1] : 0;
                bool m = Math.Abs(dx[i]) + Math.Abs(dy[i]) > eps || Math.Abs(dx[i - 1]) + Math.Abs(dy[i - 1]) > eps;
                if (m)
                {
                    // one point in between, on a smooth curve through the displacements
                    int a = i > 1 && l.Chain[i - 2] == l.Chain[i] ? i - 2 : i - 1, b = i + 1 < n && l.Chain[i + 1] == l.Chain[i] ? i + 1 : i;
                    list.Add(new NewPoint
                    {
                        U = 0.5 * (u0 + l.T[i]),
                        Dx = (-dx[a] + 9 * dx[i - 1] + 9 * dx[i] - dx[b]) / 16, Dy = (-dy[a] + 9 * dy[i - 1] + 9 * dy[i] - dy[b]) / 16,
                        Dw = 0.5 * (nw[i - 1] - l.W[i - 1] + nw[i] - l.W[i])
                    });
                }
                list.Add(new NewPoint { U = l.T[i], Dx = dx[i], Dy = dy[i], Dw = nw[i] - l.W[i], Vertex = l.Vertex[i] });
            }
            return res;
        }

        /// <summary>Distance to the nearest other bead on both sides of every sample (filled by the finder too, but not kept).</summary>
        private static void Around(WeaveLayer l, double w, WeaveFindOptions o)
        {
            double reach = 2.0 * w; int n = l.N;
            var grid = new WeaveFinder.Grid(l, reach);
            l.Any0 = new float[n]; l.Any1 = new float[n];
            WeaveFinder.Each(l, n, i => Nearest(l, grid, i, l.X, l.Y, l.X[i], l.Y[i], reach, out l.Any0[i], out l.Any1[i], out _, out _));
        }

        private static void Nearest(WeaveLayer l, WeaveFinder.Grid grid, int i, double[] px, double[] py, double x, double y, double reach, out float any0, out float any1, out int seg0, out int seg1)
        {
            double tx = l.Tx[i], ty = l.Ty[i], b0 = reach, b1 = reach; seg0 = seg1 = -1;
            int gx0 = grid.Gx(x - reach), gx1 = grid.Gx(x + reach), gy0 = grid.Gy(y - reach), gy1 = grid.Gy(y + reach);
            for (int lz = grid.Z0(l.Lap[i], 1); lz <= grid.Z1(l.Lap[i], 1); lz++)
            for (int gy = gy0; gy <= gy1; gy++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    int c = (lz * grid.Ny + gy) * grid.Nx + gx;
                    for (int q = grid.Start(c), e = grid.End(c); q < e; q++)
                    {
                        int s = grid.Items[q], a = grid.SegA[s], b = grid.SegB[s];
                        double ax = px[a], ay = py[a], ex = px[b] - ax, ey = py[b] - ay, l2 = ex * ex + ey * ey;
                        if (l2 < 1e-18) continue;
                        double u = ((x - ax) * ex + (y - ay) * ey) / l2; if (u < 0) u = 0; else if (u > 1) u = 1;
                        double vx = ax + u * ex - x, vy = ay + u * ey - y, d2 = vx * vx + vy * vy;
                        if (d2 >= reach * reach || d2 < 1e-9) continue;
                        if (l.Climb && Math.Abs(l.H[a] + u * (l.H[b] - l.H[a]) - l.H[i]) > 0.5 * l.LayerHeight) continue;
                        double d = Math.Sqrt(d2);
                        if (Math.Abs(vx * tx + vy * ty) > 0.8 * d) continue;
                        double apart = Math.Abs(l.Arc[a] + u * (l.Arc[b] - l.Arc[a]) - l.Arc[i]);
                        if (apart < 3 * d) continue;
                        if (tx * vy - ty * vx > 0) { if (d < b0) { b0 = d; seg0 = u > 0.5 ? b : a; } } else if (d < b1) { b1 = d; seg1 = u > 0.5 ? b : a; }
                    }
                }
            any0 = b0 < reach ? (float)b0 : float.MaxValue; any1 = b1 < reach ? (float)b1 : float.MaxValue;
        }

        /// <summary>A 0..1 value along the path, worn down by half the window and smoothed, so it never exceeds the old one near a drop.</summary>
        private static double[] Soft(WeaveLayer l, double[] v, double window)
        {
            int n = l.N; bool flat = true;
            for (int i = 1; i < n && flat; i++) if (Math.Abs(v[i] - v[i - 1]) > 1e-9) flat = false;
            if (flat) return v;
            var r = new double[n];
            for (int c0 = 0; c0 < n;)
            {
                int c1 = c0; while (c1 + 1 < n && l.Chain[c1 + 1] == l.Chain[c0]) c1++;
                double step = c1 > c0 ? (l.Arc[c1] - l.Arc[c0]) / (c1 - c0) : 1;
                int h = Math.Max(1, (int)Math.Ceiling(0.5 * window / Math.Max(1e-6, step)));
                var part = new double[c1 - c0 + 1]; Array.Copy(v, c0, part, 0, part.Length);
                part = Smooth(Erode(part, h, 1), h, false);
                Array.Copy(part, 0, r, c0, part.Length);
                c0 = c1 + 1;
            }
            return r;
        }

        /// <summary>Smallest value within +-h (outside the array: edge).</summary>
        private static double[] Erode(double[] v, int h, double edge)
        {
            int n = v.Length; var r = new double[n];
            var q = new int[n + 1]; int q0 = 0, q1 = 0; // sliding minimum
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                while (next < n && next <= i + h) { while (q1 > q0 && v[q[q1 - 1]] >= v[next]) q1--; q[q1++] = next++; }
                while (q1 > q0 && q[q0] < i - h) q0++;
                double m = v[q[q0]];
                if (edge < m && (i - h < 0 || i + h >= n)) m = edge;
                r[i] = m;
            }
            return r;
        }

        /// <summary>Two sliding means of half width h/2 each: a soft ramp of two parabolas. outside = 0 beyond the ends (else the end value).</summary>
        private static double[] Smooth(double[] v, int h, bool outsideZero)
        {
            int h1 = Math.Max(0, h / 2), h2 = Math.Max(0, h - h1);
            return Mean(Mean(v, h1, outsideZero), h2, outsideZero);
        }

        private static double[] Mean(double[] v, int h, bool zero)
        {
            if (h <= 0) return v;
            int n = v.Length; var r = new double[n]; double sum = 0;
            Func<int, double> at = i => i < 0 ? (zero ? 0 : v[0]) : i >= n ? (zero ? 0 : v[n - 1]) : v[i];
            for (int i = -h; i <= h; i++) sum += at(i);
            for (int i = 0; i < n; i++) { r[i] = sum / (2 * h + 1); sum += at(i + h + 1) - at(i - h); }
            return r;
        }

        internal static void Lengths(WeaveSettings s, double a, double w, out double L, out double b)
        {
            L = 1; b = 1;
            if (s.Pattern == WeavePattern.Snake)
            {
                // the same wave length in the whole zone, whatever the gap does: from the bead width
                if (s.Wavelength <= 0) L = 1.2 * WeaveSettings.MinWave(a, w, w);
                else L = Math.Max(s.Wavelength, 1.02 * WeaveSettings.MinWave(a, w, w));
            }
            else if (s.Pattern == WeavePattern.Teeth)
            {
                double m = WeaveSettings.MinTooth(a, w, w);
                b = s.ToothLength > 0 ? Math.Max(s.ToothLength, 1.02 * m) : 1.2 * m;
                L = Math.Max(s.Wavelength > 0 ? s.Wavelength : 50, b);
            }
        }

        /// <summary>How far each bead grows (or shrinks) at the seam so the two overlap by the planned amount.</summary>
        internal static double Closing(WeaveSettings s, double g, double w, double spacing)
            => s.Fit ? Math.Max(-(1 - s.WidthMin) * w, Math.Min(0.5 * (g - spacing), 0.875 * (s.WidthMax - 1) * w)) : 0;

        /// <summary>The seam of one run: where, how far, how steep.</summary>
        private static void Waves(Work k, IList<WeaveRule> rules, double w, double sp, double step, int layer)
        {
            var r = k.R; int n = r.Count;
            k.D = new double[n]; k.Dp = new double[n]; k.E = new double[n]; k.Full = new double[n];
            double ds = n > 1 ? r.Length / (n - 1) : step; if (ds < 1e-6) ds = step;
            for (int i0 = 0; i0 < n;)
            {
                int i1 = i0, rule = k.Rule[i0];
                double a = rule < 0 ? 0 : AmplitudeOf(rules[rule].Settings, w) * rules[rule].Settings.Peak;
                Func<int, bool> open = i => k.Rule[i] == rule && Math.Min(k.Limit[i], k.Damp[i] * a) > 0.05 * a;
                if (rule < 0 || a <= 0 || !open(i0)) { i0++; continue; }
                while (i1 + 1 < n && open(i1 + 1)) i1++;
                int m = i1 - i0 + 1;
                var s = rules[rule].Settings;
                double len = r.S[i1] - r.S[i0], rr = w + Math.Min(w, r.Gap[(i0 + i1) / 2]);
                // the pattern must fade in and out within half the length each
                double ac = Math.Min(a, 0.25 * len * len / (2 * rr) / 1.3);
                if (ac > 0.04 * w)
                {
                    Lengths(s, a, w, out double L, out double b);
                    double want = s.Fade > 0 ? s.Fade : s.Pattern == WeavePattern.Snake ? 0.5 * L : s.Pattern == WeavePattern.Teeth ? 0 : 0.5 * w;
                    double f = Math.Min(Math.Max(want, 1.14 * WeaveSettings.MinFade(ac, w, rr - w)), 0.5 * len);
                    int h = Math.Max(1, (int)Math.Ceiling(0.5 * f / ds));
                    var lim = new double[m];
                    for (int i = 0; i < m; i++) lim[i] = Math.Min(ac, Math.Min(k.Limit[i0 + i], a) * k.Damp[i0 + i]);
                    lim = Smooth(Erode(lim, h, 0), h, true);
                    int sign = r.NSign * k.Z.BundleSign;
                    bool follow = s.Follow || (k.Z.MeanLength < 2 * L && k.Z.Runs.Count <= k.Z.LayerCount + 1);
                    double from = follow ? r.Centre : 0;
                    if (s.Centred)
                    {
                        int c0 = i0, c1 = i1;
                        while (c0 > 0 && k.Rule[c0 - 1] == rule) c0--;
                        while (c1 + 1 < n && k.Rule[c1 + 1] == rule) c1++;
                        from = 0.5 * (r.ZoneS(c0) + r.ZoneS(c1));
                    }
                    for (int i = 0; i < m; i++)
                    {
                        int q = i0 + i;
                        double p = k.Z.BundleSDir * (r.ZoneS(q) - from) + (s.Centred ? 0 : k.Z.BundleS0);
                        k.Full[q] = lim[i];
                        k.D[q] = sign * lim[i] / s.Peak * s.Unit(p, layer, L, b);
                    }
                }
                i0 = i1 + 1;
            }
            // the widths: fitted wherever a rule holds, with a soft start and end
            for (int i0 = 0; i0 < n;)
            {
                int i1 = i0, rule = k.Rule[i0];
                if (rule < 0 || !rules[rule].Settings.Fit) { i0++; continue; }
                while (i1 + 1 < n && k.Rule[i1 + 1] == rule) i1++;
                int m = i1 - i0 + 1, h = Math.Max(1, (int)Math.Ceiling(0.5 * Math.Min(1.5 * w, 0.5 * (r.S[i1] - r.S[i0])) / ds));
                var s = rules[rule].Settings;
                var v = new double[m];
                for (int i = 0; i < m; i++) v[i] = k.DampE[i0 + i];
                v = Smooth(Erode(v, h, 0), h, true);
                for (int i = 0; i < m; i++) k.E[i0 + i] = v[i] * Closing(s, r.Gap[i0 + i], w, sp);
                i0 = i1 + 1;
            }
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double d = r.S[b] - r.S[a];
                k.Dp[i] = d > 1e-9 ? (k.D[b] - k.D[a]) / d : 0;
            }
        }

        /// <summary>Moves the samples of a layer: face by face.</summary>
        private static void Place(WeaveLayer l, List<Work> works, int[][] cov, double[][] follow, double w, out double[] dx, out double[] dy, out double[] nw)
        {
            int n = l.N;
            // per side: movement of the face towards its own outside, its direction, and the turn of the bead around the seam
            var f = new[] { new double[n], new double[n] }; var ux = new[] { new double[n], new double[n] }; var uy = new[] { new double[n], new double[n] };
            var rx = new[] { new double[n], new double[n] }; var ry = new[] { new double[n], new double[n] }; var on = new[] { new bool[n], new bool[n] };
            var e = new[] { new double[n], new double[n] };
            for (int q = 0; q < works.Count; q++)
            {
                var k = works[q]; var r = k.R;
                for (int st = 0; st < r.Count; st++) Face(k, st, r.A[st], r.SideA, true, f, e, ux, uy, rx, ry, on);
                // the other bead: where its samples lie along the run
                int m = r.Count; bool up = r.B[m - 1] >= r.B[0]; int s0 = 0;
                double lo = Math.Min(r.B[0], r.B[m - 1]), hi = Math.Max(r.B[0], r.B[m - 1]);
                for (int c = 0, j = (int)(up ? Math.Ceiling(lo) : Math.Floor(hi)); up ? j <= hi : j >= lo; j += up ? 1 : -1, c++)
                {
                    while (s0 + 1 < m - 1 && (up ? r.B[s0 + 1] < j : r.B[s0 + 1] > j)) s0++;
                    if (cov[r.SideB][j] != q) continue;
                    double d = r.B[s0 + 1] - r.B[s0], t = Math.Abs(d) > 1e-9 ? Math.Max(0, Math.Min(1, (j - r.B[s0]) / d)) : 0;
                    Face(k, s0 + t, j, r.SideB, false, f, e, ux, uy, rx, ry, on);
                }
            }
            dx = new double[n]; dy = new double[n]; nw = new double[n];
            for (int i = 0; i < n; i++)
            {
                nw[i] = l.W[i];
                // the ends of a stretch belong to the path before and after it
                if (i == 0 || i == n - 1 || l.Chain[i] != l.Chain[i - 1] || l.Chain[i] != l.Chain[i + 1]) continue;
                if (!on[0][i] && !on[1][i]) continue;
                double f0 = f[0][i] + e[0][i], f1 = f[1][i] + e[1][i];
                double ux0 = ux[0][i], uy0 = uy[0][i], ux1 = ux[1][i], uy1 = uy[1][i], kr = 1;
                if (!on[0][i]) { kr = follow[0][i]; f0 = -kr * f[1][i]; ux0 = -ux1; uy0 = -uy1; }
                else if (!on[1][i]) { kr = follow[1][i]; f1 = -kr * f[0][i]; ux1 = -ux0; uy1 = -uy0; }
                else kr = 0.5;
                dx[i] = 0.5 * (ux0 * f0 + ux1 * f1) + kr * (rx[0][i] + rx[1][i]);
                dy[i] = 0.5 * (uy0 * f0 + uy1 * f1) + kr * (ry[0][i] + ry[1][i]);
                nw[i] = l.W[i] + f0 + f1;
            }
        }

        private static void Face(Work k, double st, int i, int side, bool isA, double[][] f, double[][] e, double[][] ux, double[][] uy, double[][] rx, double[][] ry, bool[][] on)
        {
            var r = k.R;
            double D = WeaveFinder.At(k.D, st), Dp = WeaveFinder.At(k.Dp, st), E = WeaveFinder.At(k.E, st), g = WeaveFinder.At(r.Gap, st);
            double nx = WeaveFinder.At(r.Nx, st), ny = WeaveFinder.At(r.Ny, st), d = Math.Sqrt(nx * nx + ny * ny);
            if (d < 1e-9) return;
            nx /= d; ny /= d;
            int j = Math.Max(0, Math.Min(r.Count - 2, (int)st));
            double tx = r.Mx[j + 1] - r.Mx[j], ty = r.My[j + 1] - r.My[j];
            // square to n, in the direction of the run
            double sg = -ny * tx + nx * ty > 0 ? 1 : -1; tx = -ny * sg; ty = nx * sg;
            // normal of the wavy seam minus the plain normal
            double c = 1 / Math.Sqrt(1 + Dp * Dp), mx = (nx - Dp * tx) * c - nx, my = (ny - Dp * ty) * c - ny, half = 0.5 * Math.Min(g, 1e9);
            on[side][i] = true;
            if (isA) { f[side][i] = D; e[side][i] = E; ux[side][i] = nx; uy[side][i] = ny; rx[side][i] = -half * mx; ry[side][i] = -half * my; }
            else { f[side][i] = -D; e[side][i] = E; ux[side][i] = -nx; uy[side][i] = -ny; rx[side][i] = half * mx; ry[side][i] = half * my; }
        }

        /// <summary>Looks for beads that came too close, gaps, bends that are too tight and widths out of range; makes the
        /// pattern smaller there. Returns whether something changed.</summary>
        private static bool Damp(WeaveLayer l, List<Work> works, int[][] cov, IList<WeaveRule> rules, WeaveZoneSet set, double[] nx, double[] ny, double[] dx, double[] dy, double[] nw, int round)
        {
            int n = l.N; double w = set.Path.Settings.BeadWidth, sp = set.Path.Settings.BeadSpacing, reach = 2 * w;
            var bad = new bool[n]; bool any = false;
            var grid = new WeaveFinder.Grid(l, reach, nx, ny, false);
            var first = works.Select(q => q.Rule.FirstOrDefault(v => v >= 0)).ToArray();
            WeaveFinder.Each(l, n, i =>
            {
                if (i == 0 || i == n - 1 || l.Chain[i] != l.Chain[i - 1] || l.Chain[i] != l.Chain[i + 1]) return;
                bool moved = Math.Abs(dx[i]) + Math.Abs(dy[i]) > 0.01 || Math.Abs(nw[i] - l.W[i]) > 0.01;
                if (!moved) return;
                double wmin = 0.7, wmax = 1.4; int q = Math.Max(cov[0][i], cov[1][i]);
                if (q >= 0) { int ru = first[q]; wmin = rules[ru].Settings.WidthMin; wmax = rules[ru].Settings.WidthMax; }
                if (nw[i] < (wmin - 0.005) * w || nw[i] > (wmax + 0.005) * w) { bad[i] = any = true; return; }
                // bend
                double half = 0.35 * w, kOld = Bend(l, l.X, l.Y, i, half), kNew = Bend(l, nx, ny, i, half);
                if (kNew > 1.12 / (0.5 * w) && kNew > 1.1 * kOld + 0.02 / w) { bad[i] = any = true; return; }
                // neighbours
                Nearest(l, grid, i, nx, ny, nx[i], ny[i], reach, out float a0, out float a1, out int s0, out int s1);
                for (int sd = 0; sd < 2; sd++)
                {
                    float now = sd == 0 ? a0 : a1, was = sd == 0 ? l.Any0[i] : l.Any1[i]; int sg = sd == 0 ? s0 : s1;
                    if (now < 1e9f)
                    {
                        double over = 0.5 * (nw[i] + nw[sg]) - now, before = was < 1e9f ? l.W[i] - was : -1e9;
                        if (over > Math.Max(Math.Max(0, before), cov[sd][i] >= 0 ? w - sp : 0) + 0.06 * w) { bad[i] = any = true; }
                    }
                    if (cov[sd][i] >= 0)
                    {
                        double gapNow = now < 1e9f ? now - 0.5 * (nw[i] + nw[sg]) : w, gapWas = was < 1e9f ? was - l.W[i] : 0;
                        if (gapNow > Math.Max(0, gapWas) + 0.05 * w) { bad[i] = any = true; }
                    }
                }
            });
            if (!any) return false;
            double soft = 0.6;
            foreach (var k in works)
            {
                var r = k.R; int m = r.Count;
                var hit = new bool[m]; bool some = false;
                for (int st = 0; st < m; st++)
                {
                    int ja = r.A[st], jb = (int)Math.Round(r.B[st]);
                    if (bad[ja] || bad[jb]) { hit[st] = true; some = true; }
                }
                if (!some) continue;
                k.Damped = true;
                int win = Math.Max(1, (int)Math.Ceiling(w / set.Step));
                var near = new bool[m];
                for (int st = 0, last = -100000; st < m; st++) { if (hit[st]) last = st; if (st - last <= win) near[st] = true; }
                for (int st = m - 1, last = 100000; st >= 0; st--) { if (hit[st]) last = st; if (last - st <= win) near[st] = true; }
                for (int st = 0; st < m; st++)
                    if (near[st]) { k.Damp[st] *= round >= 4 ? 0 : soft; if (round >= 3) k.DampE[st] *= round >= 4 ? 0 : soft; }
            }
            return true;
        }

        private static double Bend(WeaveLayer l, double[] x, double[] y, int i, double half)
        {
            int a = i, b = i, c = l.Chain[i];
            while (a > 0 && l.Chain[a - 1] == c && l.Arc[i] - l.Arc[a] < half) a--;
            while (b + 1 < l.N && l.Chain[b + 1] == c && l.Arc[b] - l.Arc[i] < half) b++;
            if (a == i || b == i) return 0;
            // circle through three points
            double ux = x[i] - x[a], uy = y[i] - y[a], vx = x[b] - x[i], vy = y[b] - y[i], wx = x[b] - x[a], wy = y[b] - y[a];
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy), lw = Math.Sqrt(wx * wx + wy * wy);
            double cr = Math.Abs(ux * vy - uy * vx);
            return lu * lv * lw < 1e-12 ? 0 : 2 * cr / (lu * lv * lw);
        }

        // ------------------------------------------------------------------ the path
        private static Toolpath Assemble(Toolpath tp, WeaveZoneSet set, Dictionary<int, List<NewPoint>> moves)
        {
            var r = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            bool warped = !ReferenceEquals(set.Flat, set.Path);
            var flat = set.Flat;
            for (int i = 0; i < tp.Count; i++)
            {
                if (!moves.TryGetValue(i, out var list) || list.Count == 0) { r.AddFrom(tp, i); continue; }
                Point3d a = flat.Points[i - 1], b = flat.Points[i];
                bool printedBefore = tp.Flows[i - 1] > 0;
                double h0 = printedBefore ? tp.Heights[i - 1] : tp.Heights[i], w0 = tp.Widths[i - 1] > 0 ? tp.Widths[i - 1] : tp.Widths[i];
                Vector3d n0 = tp.Normals[i - 1], n1 = tp.Normals[i];
                var frame = WeaveFrame.At(flat.Normals[i]);
                var pts = new List<Point3d>(); var ws = new List<double>(); var us = new List<double>();
                foreach (var q in list)
                {
                    var p = new Point3d(a.X + (b.X - a.X) * q.U, a.Y + (b.Y - a.Y) * q.U, a.Z + (b.Z - a.Z) * q.U);
                    p = new Point3d(p.X + frame.Xx * q.Dx + frame.Yx * q.Dy, p.Y + frame.Xy * q.Dx + frame.Yy * q.Dy, p.Z + frame.Xz * q.Dx + frame.Yz * q.Dy);
                    if (warped) p = tp.Warp.ToReal(p, out _);
                    pts.Add(p); ws.Add(w0 + (tp.Widths[i] - w0) * q.U + q.Dw); us.Add(q.U);
                }
                // leave out points that lie on the line anyway (0.015 mm), but the last one
                int last = r.Count - 1;
                Point3d from = r.Points[last]; double wFrom = r.Widths[last]; int prev = -1;
                for (int k = 0; k < pts.Count; k++)
                {
                    bool keep = k == pts.Count - 1;
                    if (!keep)
                    {
                        // does the line from the last kept point to the next one pass all points in between?
                        for (int j = prev + 1; j <= k && !keep; j++)
                        {
                            double d = Off(pts[j], from, pts[k + 1], out double t);
                            if (d > 0.015 || Math.Abs(wFrom + (ws[k + 1] - wFrom) * t - ws[j]) > 0.03) keep = true;
                        }
                        if (!keep && Dist(from, pts[k + 1]) > 6) keep = true;
                    }
                    if (!keep) continue;
                    double u = us[k];
                    Vector3d nn = n1;
                    if (Math.Abs(n0.X - n1.X) + Math.Abs(n0.Y - n1.Y) + Math.Abs(n0.Z - n1.Z) > 1e-9 && printedBefore)
                    {
                        double x = n0.X + (n1.X - n0.X) * u, y = n0.Y + (n1.Y - n0.Y) * u, z = n0.Z + (n1.Z - n0.Z) * u, d = Math.Sqrt(x * x + y * y + z * z);
                        if (d > 1e-9) nn = new Vector3d(x / d, y / d, z / d);
                    }
                    r.Add(pts[k], nn, h0 + (tp.Heights[i] - h0) * u, ws[k], tp.Flows[i], tp.Roles[i], tp.Layers[i]);
                    from = pts[k]; wFrom = ws[k]; prev = k;
                }
            }
            return r;
        }

        private static double Dist(Point3d a, Point3d b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return Math.Sqrt(x * x + y * y + z * z); }

        private static double Off(Point3d p, Point3d a, Point3d b, out double t)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, l2 = dx * dx + dy * dy + dz * dz;
            t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy + (p.Z - a.Z) * dz) / l2));
            double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y, z = a.Z + t * dz - p.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
