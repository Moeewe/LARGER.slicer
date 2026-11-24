using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper2.Components;
using Grasshopper2.Data;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Path = System.IO.Path;

namespace LARGERslicer.GH2
{
    // Weave and reshape: the same components as in Grasshopper 1 (LARGERslicer/Components/Slicer/WeaveComponents.cs
    // and ReshapeComponents.cs), on the same core (LARGERslicer/Toolpaths). The small helpers of those files that
    // know Grasshopper 1 types are written again here for the plain data of Grasshopper 2.

    internal static class WeaveIo
    {
        /// <summary>Curves, points, Breps and meshes as regions (copies, safe beside other components).</summary>
        public static List<WeaveRegion> Regions(IEnumerable<object> items, double radius, List<string> log)
        {
            var res = new List<WeaveRegion>();
            foreach (var item in items)
            {
                if (item == null) continue;
                object o = Io.Solid(item);
                if (o is Point p0) o = p0.Location;
                if (o is Surface sf) o = sf.ToBrep();
                if (o is Point3d p) { res.Add(new WeaveRegion { Line = new List<Point3d> { p }, Radius = radius, Spatial = true }); continue; }
                if (o is Brep b0)
                {
                    var b = ReferenceEquals(b0, item) ? b0.DuplicateBrep() : b0;
                    var bb = b.GetBoundingBox(true); var guard = new object();
                    if (b.IsSolid) res.Add(new WeaveRegion { Inside = q => bb.Contains(q) && Locked(guard, () => b.IsPointInside(q, 0.01, false)) });
                    else { var big = bb; big.Inflate(radius); res.Add(new WeaveRegion { Inside = q => big.Contains(q) && Locked(guard, () => b.ClosestPoint(q).DistanceTo(q) <= radius) }); }
                    continue;
                }
                if (o is Mesh m0)
                {
                    var m = m0.DuplicateMesh();
                    var bb = m.GetBoundingBox(true); var guard = new object();
                    if (m.IsClosed) res.Add(new WeaveRegion { Inside = q => bb.Contains(q) && Locked(guard, () => m.IsPointInside(q, 0.01, false)) });
                    else { var big = bb; big.Inflate(radius); res.Add(new WeaveRegion { Inside = q => big.Contains(q) && Locked(guard, () => m.ClosestPoint(q).DistanceTo(q) <= radius) }); }
                    continue;
                }
                var c = o is GeometryBase && !(o is Curve) ? null : CurveIo.ToCurve(o);
                if (c == null)
                {
                    log?.Add("This kind of geometry can not be a region: " + o.GetType().Name + ". Use closed curves, open curves or points (with Radius), Breps or meshes.");
                    continue;
                }
                var pts = Points(c);
                if (pts.Count < 2) continue;
                if (c.IsClosed) res.Add(new WeaveRegion { Polygon = pts.Select(q => new Point2d(q.X, q.Y)).ToList() });
                else
                {
                    // a curve drawn at one height counts for all layers (top view)
                    bool flat = pts.Max(q => q.Z) - pts.Min(q => q.Z) < 1e-3;
                    res.Add(new WeaveRegion { Line = pts, Radius = radius, Spatial = !flat });
                }
            }
            return res;
        }

        private static bool Locked(object guard, Func<bool> f) { lock (guard) return f(); }

        private static List<Point3d> Points(Curve c)
        {
            if (c.TryGetPolyline(out Polyline pl)) return pl.ToList();
            double len = c.GetLength();
            int n = Math.Max(8, Math.Min(2000, (int)(len / 1.0)));
            var t = c.DivideByCount(n, true);
            var r = t == null ? new List<Point3d>() : t.Select(q => c.PointAt(q)).ToList();
            if (c.IsClosed && r.Count > 0) r.Add(r[0]);
            return r;
        }

        /// <summary>The zones on the wires of this input (single zones, or a whole set).</summary>
        public static List<WeaveZone> Zones(IDataAccess access, int index)
        {
            var res = new List<WeaveZone>();
            access.ItemArray(index, out object[] items);
            foreach (var v in items)
            {
                if (v is WeaveZone z) res.Add(z);
                else if (v is WeaveZoneSet s) res.AddRange(s.Zones);
            }
            return res;
        }

        public static T Pick<T>(T[] list, int i, T fallback) => list == null || list.Length == 0 ? fallback : list[Math.Min(i, list.Length - 1)];

        public static double[] Numbers(string text)
        {
            var r = new List<double>();
            foreach (var s in (text ?? "").Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) r.Add(v);
            return r.ToArray();
        }

        /// <summary>A tree with the branch of every item given by a number (a zone, a bed), as the GH_Path of Grasshopper 1.</summary>
        public static Tree<T> Branches<T>(IEnumerable<KeyValuePair<int, T>> items)
        {
            var groups = items.GroupBy(k => k.Key).OrderBy(g => g.Key).ToList();
            return Garden.TreeFromUnchecked(groups.Select(g => new Grasshopper2.Data.Path(g.Key)), groups.Select(g => Garden.TwigFromList(g.Select(k => k.Value), null, null)));
        }

        public static KeyValuePair<int, T> At<T>(int branch, T item) => new KeyValuePair<int, T>(branch, item);

        // ---- files: several inputs (lists, trees) never write over each other, as SlicerComponent.FreePath of Grasshopper 1
        private sealed class Written { public object Solution; public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        private static readonly ConditionalWeakTable<Component, Written> Last = new ConditionalWeakTable<Component, Written>();

        /// <summary>The path for a file of this solution: _2, _3 ... when this component has written that one already in this solution.</summary>
        public static string FreePath(IDataAccess access, Component owner, string file, string defaultName)
        {
            string path = OutputFile.Resolve(file, defaultName), free = path;
            string stem = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path)), ext = Path.GetExtension(path);
            var w = Last.GetOrCreateValue(owner);
            lock (w)
            {
                if (!ReferenceEquals(w.Solution, access.Solution)) { w.Solution = access.Solution; w.Files.Clear(); }
                for (int n = 2; !w.Files.Add(free); n++) free = stem + "_" + n.ToString(CultureInfo.InvariantCulture) + ext;
            }
            return free;
        }

        public static void Error(IDataAccess access, string text) => Gh1.Message(access, 2, text);
        public static void Warning(IDataAccess access, string text) => Gh1.Message(access, 1, text);
        public static void Remark(IDataAccess access, string text) => Gh1.Message(access, 0, text);

        /// <summary>An optional text; null when nothing is there.</summary>
        public static string Text(IDataAccess access, int index) => access.GetItem(index, out string s) ? s : null;
    }

    internal static class ReshapeIo
    {
        public static string PathOf(string file)
        {
            string p = (file ?? "").Trim().Trim('"');
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            return p;
        }

        private static readonly object LogLock = new object(); // Grasshopper 2 solves components (and items) side by side

        /// <summary>Adds lines to a log file; a new file gets the header first.</summary>
        public static string Append(string file, string defaultName, string header, IEnumerable<string> lines)
        {
            string path = OutputFile.Resolve(file, defaultName);
            lock (LogLock)
            {
                var l = new List<string>(); if (!File.Exists(path)) l.Add(header); l.AddRange(lines);
                File.AppendAllLines(path, l);
            }
            return path;
        }

        /// <summary>Adds lines to a text file beside the log.</summary>
        public static void AppendText(string path, IEnumerable<string> lines) { lock (LogLock) File.AppendAllLines(path, lines); }

        public static Mesh Surface(Func<double, double, double> z, double x0, double y0, double x1, double y1, int n = 50)
        {
            double size = Math.Max(1e-6, Math.Max(x1 - x0, y1 - y0)), cell = size / n;
            int nx = Math.Max(2, (int)Math.Ceiling((x1 - x0) / cell) + 1), ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / cell) + 1);
            double dx = (x1 - x0) / (nx - 1), dy = (y1 - y0) / (ny - 1);
            if (dx <= 0) dx = cell; if (dy <= 0) dy = cell;
            var m = new Mesh();
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { double x = x0 + i * dx, y = y0 + j * dy; m.Vertices.Add(x, y, z(x, y)); }
            for (int j = 0; j + 1 < ny; j++) for (int i = 0; i + 1 < nx; i++) { int a = j * nx + i; m.Faces.AddFace(a, a + 1, a + nx + 1, a + nx); }
            m.Normals.ComputeNormals(); m.Compact();
            return m;
        }

        public static bool Head(IDataAccess access, int first, NonPlanarCheck.Head head)
        {
            access.GetItem(first, out head.TipRadius); access.GetItem(first + 1, out head.HalfAngle); access.GetItem(first + 2, out head.ConeLength); access.GetItem(first + 3, out head.Radius);
            if (head.TipRadius < 0 || head.HalfAngle < 0 || head.HalfAngle >= 90 || head.ConeLength <= 0 || head.Radius <= 0)
            {
                WeaveIo.Error(access, "Tip Radius must be 0 or more, Cone Angle between 0 and 90 degrees, Cone Length and Head Radius more than 0.");
                return false;
            }
            return true;
        }

        public static void HeadInputs(InputAdder inputs)
        {
            inputs.AddNumber("Tip Radius", "r0", "Radius of the nozzle tip (mm). A flat 8 mm tip is 4; with a vertical nozzle it sets how steep the layers may be").Set(4.0);
            inputs.AddNumber("Cone Angle", "Ca", "Half angle of the nozzle cone, from its axis (degrees)").Set(30.0).Angles();
            inputs.AddNumber("Cone Length", "Lc", "Height of the cone from the tip up to the head (mm)").Set(60.0);
            inputs.AddNumber("Head Radius", "Hr", "Radius of the head above the cone (mm)").Set(40.0);
        }

        public static void Verdict(IDataAccess access, string first)
        {
            if (first == null) return;
            if (first.StartsWith("REFUSED")) WeaveIo.Error(access, first);
            else if (first.StartsWith("CHECK")) WeaveIo.Warning(access, first);
        }
    }

    // ============================================================== weave zones
    [IoId("35ea15e7-aeae-41c3-bb55-49c60be2b33e")]
    public sealed class WeaveZonesComponent : Component
    {
        public WeaveZonesComponent() : base(Io.Name("Weave Zones", "Finds the places where two beads of a layer lie side by side: two walls, infill against a wall, the two lines of a rib, knots where two strips nearly touch. Every place is followed through the layers (one zone per place). How near is near can be set in fractions of the bead width. Places can be given by hand: Include and Exclude. Use it after Continuous Path (or Round Corners) and before Start and End; then Weave.", Io.Slicer, Group.Path)) { }
        public WeaveZonesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWeaveZonesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path from Continuous Path (any toolpath works)");
            inputs.AddGeneric("Include", "In", "Optional. Places set by hand: closed curves (top view, all layers), open curves and points (with Radius), Breps or meshes (only the layers inside). Inside, beads further apart are taken too", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Exclude", "Ex", "Optional. Never here: the same kinds of geometry", Access.Twig, Requirement.MayBeMissing);
            inputs.AddInteger("Include Mode", "Mo", "0 = only inside the Include places\n1 = everything that is found, and the Include places").Set(0).Choices("Only inside Include", "Found places and Include");
            inputs.AddNumber("Radius", "Ra", "Around points, open curves and open surfaces, mm").Set(15.0);
            inputs.AddNumber("Closer", "Cl", "How much nearer than one bead width, as a fraction: 0.85 = the centre lines may be as near as 0.15 bead widths (pressed into each other; Weave can fit the widths there). 0.35 leaves such places out").Set(0.85).Values(0.15, 0.35, 0.5, 0.85);
            inputs.AddNumber("Touch", "To", "Beads that are one bead width apart, plus or minus this fraction, count as touching; nearer is fused, further is almost touching").Set(0.08);
            inputs.AddNumber("Further", "Fu", "How much further than one bead width, as a fraction: 0.25 = up to 1.25 bead widths between the centre lines").Set(0.25).Values(0, 0.1, 0.25, 0.4);
            inputs.AddNumber("Min Length", "Lm", "Shortest stretch in mm. 0 = 1.5 bead widths").Set(0.0).Values(0, 3, 8, 25, 60, 100);
            inputs.AddInteger("Min Layers", "Ln", "A zone must go through at least this many layers").Set(2);
            inputs.AddNumber("Max Angle", "An", "Lines count as parallel up to this angle, degrees").Set(25.0);
            inputs.AddBoolean("Bottom", "Bo", "True = also the lines of solid bottom and top fill").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Zones", "Z", "The zones, the longest first. Pick from them with List Item, Cull or Dispatch", Access.Twig);
            outputs.AddCurve("Seams", "S", "The seam between the two beads, one branch per zone, one curve per layer", Access.Tree);
            outputs.AddPoint("Centres", "C", "Middle of every zone (with Text Tag: the ids)", Access.Twig);
            outputs.AddInteger("Ids", "Id", "Id of every zone", Access.Twig);
            outputs.AddInteger("Class", "Cl", "0 = fused, 1 = touching, 2 = almost touching, 3 = pressed into each other", Access.Twig);
            outputs.AddNumber("Length", "L", "Mean length of the seam per layer, mm", Access.Twig);
            outputs.AddNumber("Gap", "G", "Mean distance of the two centre lines, mm", Access.Twig);
            outputs.AddText("Info", "I", "Verdict first, then the zones");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Path(access, 0); if (tp == null) return;
            if (tp.Count < 3) { WeaveIo.Error(access, "Connect the Toolpath output of Continuous Path."); return; }
            access.ItemArray(1, out object[] inc); access.ItemArray(2, out object[] exc);
            int mode, ml; double radius, cl, to, fu, lm, an; bool bottom;
            access.GetItem(3, out mode); access.GetItem(4, out radius); access.GetItem(5, out cl); access.GetItem(6, out to); access.GetItem(7, out fu); access.GetItem(8, out lm); access.GetItem(9, out ml); access.GetItem(10, out an); access.GetItem(11, out bottom);
            var log = new List<string>();
            var o = new WeaveFindOptions
            {
                Include = WeaveIo.Regions(inc, radius, log), Exclude = WeaveIo.Regions(exc, radius, log), OnlyIncluded = mode == 0,
                Closer = Math.Max(0, Math.Min(0.95, cl)), Touch = Math.Max(0, to), Further = Math.Max(0, Math.Min(1, fu)), MinLength = lm, MinLayers = ml, MaxAngle = an, Bottom = bottom
            };
            foreach (var l in log) WeaveIo.Warning(access, l);
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var set = WeaveFinder.Find(tp, o);
            if (set.Zones.Count == 0) WeaveIo.Warning(access, set.Verdict + " Try a larger Further, or a shorter Min Length.");
            int pressed = set.Zones.Count(z => z.Class == WeaveClass.Pressed);
            if (pressed > 0) WeaveIo.Remark(access, $"{pressed} zone(s) are pressed into each other: too much material there. Weave with Fit makes the beads narrower at these places.");
            Io.Label(this, set.Zones.Count + " zones");
            access.SetTwig(0, set.Zones.Cast<object>().ToArray(), null, null);
            access.SetTree(1, WeaveIo.Branches(set.Zones.SelectMany(z => z.Runs.OrderBy(q => q.Layer).Select(r => WeaveIo.At(z.Id, (Curve)new PolylineCurve(Thin(r.Mid)))))));
            access.SetTwig(2, set.Zones.Select(z => z.Centre).ToArray(), null, null);
            access.SetTwig(3, set.Zones.Select(z => z.Id).ToArray(), null, null);
            access.SetTwig(4, set.Zones.Select(z => (int)z.Class).ToArray(), null, null);
            access.SetTwig(5, set.Zones.Select(z => z.MeanLength).ToArray(), null, null);
            access.SetTwig(6, set.Zones.Select(z => z.GapMean).ToArray(), null, null);
            access.SetItem(7, string.Join("\n", set.Report));
        }

        private static IEnumerable<Point3d> Thin(Point3d[] p)
        {
            for (int i = 0; i < p.Length; i++) if (i % 3 == 0 || i == p.Length - 1) yield return p[i];
        }
    }

    // ============================================================== weave
    [IoId("ccc87e56-d216-42f6-b1b0-db6e4ae72e85")]
    public sealed class WeaveComponent : Component
    {
        public WeaveComponent() : base(Io.Name("Weave", "Interlocks two beads that lie side by side. Instead of only touching along a line, the seam between them moves from layer to layer, so the layers grip into each other: Shift (left, right, left ...), Width (the outer faces stay, one bead wide and one narrow), Snake (waves that cross from layer to layer, like a braid seen from above), Teeth (straight, with a small tooth every few centimetres) or your own list. The two beads keep their distance: no gap, no extra material. With Fit the bead width also follows the distance, so beads that nearly touch fuse and beads that were pressed together do not over-extrude. The path stays one line. Per zone settings: give lists (item 0 for zone 0 ...). Afterwards: Round Corners if you use it, then Start and End.", Io.Slicer, Group.Path)) { }
        public WeaveComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWeaveIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path (the one Weave Zones was given, or a later state of it)");
            inputs.AddGeneric("Zones", "Z", "Optional. Zones from Weave Zones. Empty = find them with the default tolerances and weave all", Access.Twig, Requirement.MayBeMissing);
            inputs.AddInteger("Pattern", "P", "0 = Shift: the seam lies left and right, layer by layer\n1 = Width: the outer faces stay, beads wide and narrow in turn\n2 = Snake: a wave, moved on from layer to layer\n3 = Teeth: straight, a small tooth every Wavelength\n4 = Custom: the list Offsets\n5 = Plain: no pattern, only fit the widths\nA list: one per zone", Access.Twig).Set(0).Choices(Weave.PatternNames);
            inputs.AddNumber("Amplitude", "A", "How far the seam leaves the middle, mm. 0 = a quarter of the bead width. A list: one per zone", Access.Twig).Set(0.0).Values(0, 0.2, 0.75, 2.5, 5, 10);
            inputs.AddNumber("Wavelength", "L", "Snake: length of one wave; Teeth: distance of the teeth, mm. 0 = automatic (shortest printable wave x 1.2; teeth 50 mm). A list: one per zone", Access.Twig).Set(0.0).Values(0, 1, 5, 25, 80, 200);
            inputs.AddNumber("Phase", "Ph", "Snake, Teeth: how far the pattern moves on per layer, as a fraction of the wave. 0.5 = woven, 0.25 = helix, 0 = stacked. -1 = automatic (Snake 0.5, Teeth 0). A list: one per zone", Access.Twig).Set(-1.0).Values(-1, 0, 0.25, 0.5);
            inputs.AddInteger("Faces", "F", "The faces that look away from the seam:\n0 = Auto: walls keep their face, infill moves\n1 = Both beads move\n2 = Both keep their face (the bead width changes)\nA list: one per zone", Access.Twig).Set(0).Choices("Auto (walls keep their face)", "Both beads move", "Both keep their face");
            inputs.AddNumber("Extent", "E", "Woven length per zone, mm (150 = 15 cm, 5 = a tiny spot). 0 = the whole zone. A list: one per zone", Access.Twig).Set(0.0).Values(0, 4, 20, 80, 400, 1000);
            inputs.AddNumber("Position", "Po", "Where the woven length lies in the zone: 0 = start, 0.5 = middle, 1 = end. A list: one per zone", Access.Twig).Set(0.5);
            inputs.AddNumber("Repeat", "Re", "The woven length again every ... mm. 0 = once").Set(0.0);
            inputs.AddNumber("Fade", "Fa", "Length over which the pattern grows from nothing at its ends, mm. 0 = as short as the bead allows").Set(0.0);
            inputs.AddNumber("Tooth Length", "Tl", "Teeth: length of one tooth, mm. 0 = the shortest printable one x 1.2").Set(0.0);
            inputs.AddInteger("Layer Period", "Lp", "Layers with the same row before the pattern moves on").Set(1);
            inputs.AddText("Offsets", "Of", "Custom: the offset per layer as a fraction of the amplitude, e.g. 0,1,0,-1. A list: one text per zone", Access.Twig).Set("0,1,0,-1");
            inputs.AddBoolean("Fit", "Fi", "True = the bead width follows the distance of the two beads, so they overlap as planned everywhere (wider over a gap, narrower where they are pressed together)").Set(true);
            inputs.AddNumber("Step Limit", "Sl", "Largest sideways step of a bead from layer to layer, in bead widths (0.5 = every bead rests on the one below)").Set(0.5);
            inputs.AddNumber("Width Min", "W0", "Narrowest bead, as a fraction of the bead width").Set(0.7);
            inputs.AddNumber("Width Max", "W1", "Widest bead, as a fraction of the bead width").Set(1.4);
            inputs.AddGeneric("Place", "Pl", "Optional. Weave only inside these places: closed curves, curves or points (15 mm around), Breps or meshes", Access.Twig, Requirement.MayBeMissing);
            inputs.AddBoolean("Check", "Ck", "True = measure the path before and after (raster of the beads, crossings, bends, steps, contact areas)").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The woven path");
            outputs.AddCurve("Preview", "Pv", "One polyline per layer", Access.Twig);
            outputs.AddGeneric("Zones", "Z", "The zones that were used (for Joint Data and Weave Variants)", Access.Twig);
            outputs.AddText("Info", "I", "Verdict first, then what was done, then the numbers before and after");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Path(access, 0); if (tp == null) return;
            if (tp.Count < 3) { WeaveIo.Error(access, "Connect the Toolpath output of Continuous Path."); return; }
            var zones = WeaveIo.Zones(access, 1);
            access.ItemArray(2, out int[] pat); access.ItemArray(3, out double[] amp); access.ItemArray(4, out double[] len); access.ItemArray(5, out double[] pha); access.ItemArray(6, out int[] fac); access.ItemArray(7, out double[] ext); access.ItemArray(8, out double[] pos); access.ItemArray(13, out string[] off);
            double rep, fade, tooth, sl, w0, w1; int lp; bool fit, check;
            access.GetItem(9, out rep); access.GetItem(10, out fade); access.GetItem(11, out tooth); access.GetItem(12, out lp); access.GetItem(14, out fit); access.GetItem(15, out sl); access.GetItem(16, out w0); access.GetItem(17, out w1); access.GetItem(19, out check);
            access.ItemArray(18, out object[] place);
            var log = new List<string>();
            var regions = WeaveIo.Regions(place, 15, log);
            bool given = Parameters.Input(1).Inputs.Count > 0;
            if (given && zones.Count == 0) { WeaveIo.Warning(access, "No zones arrive at Zones: nothing is woven."); access.SetItem(0, tp); return; }
            Func<int, WeaveSettings> make = i =>
            {
                var s = new WeaveSettings
                {
                    Pattern = (WeavePattern)Math.Max(0, Math.Min(5, WeaveIo.Pick(pat, i, 0))), Amplitude = Math.Max(0, WeaveIo.Pick(amp, i, 0.0)), Wavelength = Math.Max(0, WeaveIo.Pick(len, i, 0.0)), Phase = WeaveIo.Pick(pha, i, -1.0),
                    Faces = (WeaveFaces)Math.Max(0, Math.Min(2, WeaveIo.Pick(fac, i, 0))), Extent = Math.Max(0, WeaveIo.Pick(ext, i, 0.0)), Position = WeaveIo.Pick(pos, i, 0.5), Repeat = Math.Max(0, rep), Fade = Math.Max(0, fade),
                    ToothLength = Math.Max(0, tooth), LayerPeriod = Math.Max(1, lp), Fit = fit, StepLimit = Math.Max(0.05, sl), WidthMin = Math.Max(0.3, Math.Min(1, w0)), WidthMax = Math.Max(1, Math.Min(2.5, w1))
                };
                var o = WeaveIo.Numbers(WeaveIo.Pick(off, i, "0,1,0,-1")); if (o.Length > 0) s.Offsets = o;
                return s;
            };

            foreach (var l in log) WeaveIo.Warning(access, l);
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            WeaveZoneSet set; var rules = new List<WeaveRule>();
            if (zones.Count > 0 && ReferenceEquals(zones[0].Set.Path, tp))
            {
                set = zones[0].Set;
                for (int i = 0; i < zones.Count; i++) rules.Add(new WeaveRule { Settings = make(i), Zones = new HashSet<int> { zones[i].Id }, Regions = regions });
            }
            else if (zones.Count > 0)
            {
                // a later state of the path: find again, and take over the zones by their place
                set = WeaveFinder.Find(tp, zones[0].Set.Options);
                foreach (var kv in WeaveFinder.Match(zones, set)) rules.Add(new WeaveRule { Settings = make(kv.Value), Zones = new HashSet<int> { kv.Key }, Regions = regions });
                if (rules.Count == 0) WeaveIo.Warning(access, "The zones do not fit this toolpath: nothing is woven.");
            }
            else
            {
                set = WeaveFinder.Find(tp, new WeaveFindOptions());
                for (int i = 0; i < set.Zones.Count; i++) rules.Add(new WeaveRule { Settings = make(i), Zones = new HashSet<int> { i }, Regions = regions });
            }
            if (Io.Step(access, 40)) return;
            var r = Weave.Apply(tp, set, rules);
            foreach (var l in r.Warnings.Distinct()) WeaveIo.Remark(access, l);
            var text = new List<string>(r.Report);
            if (check && r.WovenRuns > 0)
            {
                if (Io.Step(access, 70)) return;
                var c = WeaveCheck.Compare(tp, r.Path, set);
                text.Insert(1, c.Verdict); text.Add(""); text.AddRange(c.Table.Skip(1));
                if (!c.Ok) WeaveIo.Warning(access, c.Verdict);
            }
            if (r.WovenRuns == 0) WeaveIo.Warning(access, r.Verdict);
            Io.Label(this, r.WovenZones + " of " + set.Zones.Count + " zones");
            var used = new HashSet<int>(rules.SelectMany(q => q.Zones));
            access.SetItem(0, r.Path);
            access.SetTwig(1, Layers(r.Path), null, null);
            access.SetTwig(2, set.Zones.Where(z => used.Contains(z.Id)).Cast<object>().ToArray(), null, null);
            access.SetItem(3, string.Join("\n", text));
        }

        private static Curve[] Layers(Toolpath tp)
        {
            var res = new List<Curve>(); var cur = new List<Point3d>();
            for (int i = 1; i < tp.Count; i++)
            {
                bool print = tp.Flows[i] > 0;
                if (print && cur.Count == 0) cur.Add(tp.Points[i - 1]);
                if (print) cur.Add(tp.Points[i]);
                if (cur.Count > 1 && (!print || i + 1 >= tp.Count || tp.Layers[i + 1] != tp.Layers[i])) { res.Add(new PolylineCurve(cur)); cur = new List<Point3d>(); }
            }
            return res.ToArray();
        }
    }

    // ============================================================== weave variants
    [IoId("6ba3a297-85e5-4e02-97ea-025d75c5feef")]
    public sealed class WeaveVariantsComponent : Component
    {
        public WeaveVariantsComponent() : base(Io.Name("Weave Variants", "Weaves the same zones in fourteen ways (as sliced, fit only, shift, width, snake, helix, teeth, list ...) and measures every one the same way: contact area beside and between the layers, over-extrusion, gaps, crossings, bends, support, length, material, time. One table to compare them, and every path to look at or print.", Io.Slicer, Group.Check)) { }
        public WeaveVariantsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWeaveVariantsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path before weaving");
            inputs.AddGeneric("Zones", "Z", "Optional. Zones from Weave Zones (its whole search is used)", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Amplitude", "A", "Amplitude of the variants, mm. 0 = a quarter of the bead width").Set(0.0).Values(0, 0.2, 0.75, 2.5, 5, 10);
            inputs.AddInteger("Layers", "N", "Layers that are drawn as a raster (the contact areas use all layers)").Set(12).Counts(2, 5, 8, 12, 20, 30, 60);
            inputs.AddText("File", "File", "Optional. File or folder for the CSV. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True = write the CSV").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Table", "Tab", "The comparison (use a Panel with a fixed-width font)", Access.Twig);
            outputs.AddGeneric("Toolpaths", "T", "The path of every variant", Access.Twig);
            outputs.AddText("Names", "N", "Name of every variant", Access.Twig);
            outputs.AddText("CSV", "CSV", "The table as CSV", Access.Twig);
            outputs.AddText("Info", "I", "Verdict");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Get<Toolpath>(access, 0);
            if (tp == null || tp.Count < 3) { WeaveIo.Error(access, "Connect a Toolpath."); return; }
            var zones = WeaveIo.Zones(access, 1); double a; int n; bool save;
            access.GetItem(2, out a); access.GetItem(3, out n); string file = WeaveIo.Text(access, 4); access.GetItem(5, out save);
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var set = zones.Count > 0 && ReferenceEquals(zones[0].Set.Path, tp) ? zones[0].Set : WeaveFinder.Find(tp, zones.Count > 0 ? zones[0].Set.Options : new WeaveFindOptions());
            if (set.Zones.Count == 0) { WeaveIo.Warning(access, set.Verdict); if (save) Files.Reset(this, 5); return; }
            if (Io.Step(access, 20)) return;
            var v = WeaveVariants.Run(tp, set, WeaveVariants.Standard(tp.Settings.BeadWidth, a), n, new JointMaterial());
            var csv = WeaveVariants.Csv(v);
            if (Io.Step(access, 95)) return;
            if (save)
            {
                try { file = WeaveIo.FreePath(access, this, file, OutputFile.Name("VAR", "Weave Variants", null, ".csv")); File.WriteAllLines(file, csv); WeaveIo.Remark(access, "Saved " + file); }
                catch (Exception ex) { WeaveIo.Error(access, "Could not write the file: " + ex.Message); }
                Files.Reset(this, 5);
            }
            var best = v.Where(q => q.Numbers.Crossings <= v[0].Numbers.Crossings && q.Numbers.StepOver <= v[0].Numbers.StepOver + 0.3).OrderByDescending(q => q.Joint.HorizontalShare).First();
            Io.Label(this, v.Count + " variants");
            access.SetTwig(0, WeaveVariants.Table(v).ToArray(), null, null);
            access.SetTwig(1, v.Select(q => (object)q.Result.Path).ToArray(), null, null);
            access.SetTwig(2, v.Select(q => q.Name).ToArray(), null, null);
            access.SetTwig(3, csv.ToArray(), null, null);
            access.SetItem(4, Inv.F($"Most interlock without new crossings or overhang: {best.Name} ({best.Joint.HorizontalShare:0.00} mm2 per mm2 of joint face, {v[0].Joint.HorizontalShare:0.00} as sliced). Which one is stronger only a test tells."));
        }
    }

    // ============================================================== weave test
    [IoId("45375e3c-e2df-4972-8f1b-8a58d77a5c99")]
    public sealed class WeaveTestComponent : Component
    {
        public WeaveTestComponent() : base(Io.Name("Weave Test", "A test series for woven joints: pieces of double wall, plain and woven in different ways, with different amplitudes and gaps. All pieces are printed as one line (two beads side by side in rows), in random order, and sawn apart afterwards. The protocol is a CSV table with a line per specimen and empty fields for the results. Suggested tests: shear along the joint, tension across, bending; five of each.", Io.Slicer, Group.Check)) { }
        public WeaveTestComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWeaveTestIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Settings", "S", "Print Settings");
            inputs.AddNumber("Length", "L", "Length of a specimen, mm").Set(80.0).Values(30, 50, 75, 80, 120, 200, 300);
            inputs.AddNumber("Height", "H", "Height of a specimen, mm").Set(60.0).Values(10, 20, 40, 60, 80, 150, 300);
            inputs.AddNumber("Waste", "W", "Waste between two specimens (saw, change of the gap), mm").Set(25.0);
            inputs.AddNumber("Bed X", "X", "Print bed in X, mm").Set(1000.0);
            inputs.AddNumber("Bed Y", "Y", "Print bed in Y, mm").Set(700.0);
            inputs.AddInteger("Replicates", "N", "Specimens per variant and test").Set(5).Counts(1, 3, 4, 5, 6, 10);
            inputs.AddBoolean("Shear", "Sh", "Specimens for shear along the joint").Set(true);
            inputs.AddBoolean("Tension", "Te", "Specimens for tension across the joint (peel)").Set(true);
            inputs.AddBoolean("Bending", "Be", "Specimens for bending").Set(true);
            inputs.AddBoolean("Amplitudes", "Am", "Also smaller amplitudes of Shift").Set(true);
            inputs.AddBoolean("Gaps", "Ga", "Also larger gaps, plain and woven").Set(true);
            inputs.AddInteger("Seed", "Se", "Another number gives another random order").Set(1);
            inputs.AddText("File", "File", "Optional. File or folder for the protocol. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True = write the protocol (CSV)").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Beds", "T", "One path per bed (to Start and End and the output)", Access.Twig);
            outputs.AddGeneric("Specimens", "Sp", "One path per variant, alone", Access.Twig);
            outputs.AddText("Variants", "V", "Names of the variants", Access.Twig);
            outputs.AddCurve("Marks", "M", "Rectangles of the specimens on the bed, one branch per bed (saw here)", Access.Tree);
            outputs.AddText("Ids", "Id", "Id of every specimen, in the order of the marks", Access.Tree);
            outputs.AddText("Protocol", "CSV", "The protocol: one line per specimen, with the numbers of the joint and empty fields for the results", Access.Twig);
            outputs.AddText("Info", "I", "Verdict, layout and the tests");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var st = Io.Settings(access, 0);
            var o = new WeaveTestOptions(); bool save;
            access.GetItem(1, out o.Length); access.GetItem(2, out o.Height); access.GetItem(3, out o.Waste); access.GetItem(4, out o.BedX); access.GetItem(5, out o.BedY); access.GetItem(6, out o.Replicates);
            access.GetItem(7, out o.Shear); access.GetItem(8, out o.Peel); access.GetItem(9, out o.Bending); access.GetItem(10, out o.Amplitudes); access.GetItem(11, out o.Gaps); access.GetItem(12, out o.Seed); string file = WeaveIo.Text(access, 13); access.GetItem(14, out save);
            string problem = !o.Shear && !o.Peel && !o.Bending ? "Switch on at least one test." : null;
            if (problem != null) { WeaveIo.Warning(access, problem); if (save) Files.Reset(this, 14); return; }
            if (o.Length < 4 * st.BeadWidth) problem = "The specimens are shorter than four bead widths.";
            else
            {
                o.Waste = Math.Max(o.Waste, 3 * st.BeadWidth);
                if (o.BedX < 2 * o.Margin + o.Length + 3 * o.Waste + 20 * st.BeadWidth) problem = "The bed is too short for one specimen in a row.";
            }
            if (problem != null) { WeaveIo.Error(access, problem); if (save) Files.Reset(this, 14); return; }
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var t = WeaveCoupons.Make(st, o);
            var csv = t.Csv(st);
            if (Io.Step(access, 95)) return;
            if (save)
            {
                try { file = WeaveIo.FreePath(access, this, file, OutputFile.Name("TEST", "Weave Test", null, ".csv")); File.WriteAllLines(file, csv); WeaveIo.Remark(access, "Saved " + file); }
                catch (Exception ex) { WeaveIo.Error(access, "Could not write the file: " + ex.Message); }
                Files.Reset(this, 14);
            }
            var marks = new List<KeyValuePair<int, Curve>>(); var ids = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < t.Coupons.Count; i++)
            {
                var c = t.Coupons[i]; double h = 0.5 * c.Gap + st.BeadWidth, x0 = Math.Min(c.From.X, c.To.X), x1 = Math.Max(c.From.X, c.To.X);
                marks.Add(WeaveIo.At(t.BedOf[i], (Curve)new PolylineCurve(new[] { new Point3d(x0, c.From.Y - h, 0), new Point3d(x1, c.From.Y - h, 0), new Point3d(x1, c.From.Y + h, 0), new Point3d(x0, c.From.Y + h, 0), new Point3d(x0, c.From.Y - h, 0) })));
                ids.Add(WeaveIo.At(t.BedOf[i], c.Id + " " + c.Variant));
            }
            var variants = t.Coupons.GroupBy(c => c.Variant).Select(g => g.First()).OrderBy(c => c.Id).ToList();
            Io.Label(this, t.Coupons.Count + " specimens");
            access.SetTwig(0, t.Beds.Cast<object>().ToArray(), null, null);
            access.SetTwig(1, variants.Select(c => (object)c.Path).ToArray(), null, null);
            access.SetTwig(2, variants.Select(c => c.Variant).ToArray(), null, null);
            access.SetTree(3, WeaveIo.Branches(marks)); access.SetTree(4, WeaveIo.Branches(ids));
            access.SetTwig(5, csv.ToArray(), null, null);
            access.SetItem(6, string.Join("\n", t.Report));
        }
    }

    // ============================================================== joint data
    [IoId("350d2e4e-3903-4c92-a54c-cbad46085b93")]
    public sealed class JointDataComponent : Component
    {
        public JointDataComponent() : base(Io.Name("Joint Data", "The joints of a path as data, for the analysis and for tests. The surfaces of the analysis stay smooth: a joint is a coupling between two patches (or a patch link) with a stiffness and a strength. Per zone: the seam on the mid surfaces, length, gap, contact area beside and between the layers per metre, interlock depth, and an estimate of stiffness and strength; per point pair the area it stands for and the penalty alpha; per height band the patch link with the thickness it needs. All material values are placeholders until they are measured with Weave Test.", Io.Slicer, Group.Check)) { }
        public JointDataComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerJointIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The woven path (or any path, to compare)");
            inputs.AddGeneric("Zones", "Z", "Zones from Weave (or Weave Zones)", Access.Twig);
            inputs.AddNumber("Pair Spacing", "D", "Distance of the point pairs along the seam and up, mm").Set(10.0).Values(2, 5, 10, 12, 25, 60, 100);
            inputs.AddNumber("E Across", "Ea", "Young's modulus across the beads in the layer, MPa. Placeholder").Set(2400.0);
            inputs.AddNumber("E Layers", "El", "Young's modulus across the layers, MPa. Placeholder").Set(1800.0);
            inputs.AddNumber("G In Layer", "G", "Shear modulus in the layer, MPa. Placeholder").Set(1100.0);
            inputs.AddNumber("G Layers", "Gl", "Shear modulus across the layers, MPa. Placeholder").Set(700.0);
            inputs.AddNumber("Side Tension", "Ss", "Tensile strength of the weld of two beads side by side, MPa. Placeholder").Set(20.0);
            inputs.AddNumber("Side Shear", "Ts", "Shear strength of the weld of two beads side by side, MPa. Placeholder").Set(12.0);
            inputs.AddNumber("Layer Tension", "Sl", "Tensile strength between layers, MPa. Placeholder").Set(18.0);
            inputs.AddNumber("Layer Shear", "Tl", "Shear strength between layers, MPa. Placeholder").Set(12.0);
            inputs.AddNumber("Shell E", "E", "Modulus of the shells in the analysis, MPa (for alpha and the link thickness)").Set(2400.0);
            inputs.AddNumber("Shell Thickness", "t", "Thickness of the shells, mm. 0 = bead width").Set(0.0);
            inputs.AddNumber("Element Size", "h", "Size of the elements in the analysis, mm (for alpha)").Set(10.0);
            inputs.AddTransform("Move Back", "X", "Optional. Transform from the print bed to the model of the analysis (a part that is printed lying)", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Name", "N", "Name in the files").Set("Joints");
            inputs.AddText("Folder", "Dir", "Optional. Folder. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True = write JSON and the three CSV tables").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddPoint("Points A", "A", "Points on the mid surface of the first wall, one branch per zone", Access.Tree);
            outputs.AddPoint("Points B", "B", "The points opposite, on the second wall", Access.Tree);
            outputs.AddLine("Links", "L", "Patch links: the shortest line across the joint per height band, one branch per zone", Access.Tree);
            outputs.AddNumber("Link Thickness", "tL", "Fusion thickness of every link (opening stiffness), mm", Access.Tree);
            outputs.AddNumber("Interlock", "Il", "Interlock contact per joint face, per zone (mm2 per mm2)", Access.Twig);
            outputs.AddNumber("Side Contact", "Sc", "Contact of the beads side by side per joint face, per zone", Access.Twig);
            outputs.AddText("Zones CSV", "CSV", "One line per zone", Access.Twig);
            outputs.AddText("JSON", "JSON", "Everything as JSON");
            outputs.AddText("Info", "I", "Verdict, and how it maps to the couplings of a shell analysis");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Get<Toolpath>(access, 0);
            var zones = WeaveIo.Zones(access, 1);
            if (tp == null || tp.Count < 3) { WeaveIo.Error(access, "Connect a Toolpath."); return; }
            if (zones.Count == 0) { WeaveIo.Error(access, "Connect the zones of Weave or Weave Zones."); return; }
            var o = new JointDataOptions(); var m = o.Material; bool save;
            access.GetItem(2, out o.PairSpacing); access.GetItem(3, out m.EAcross); access.GetItem(4, out m.ELayers); access.GetItem(5, out m.G); access.GetItem(6, out m.GLayers); access.GetItem(7, out m.SigmaSide); access.GetItem(8, out m.TauSide); access.GetItem(9, out m.SigmaLayer); access.GetItem(10, out m.TauLayer);
            access.GetItem(11, out o.E); access.GetItem(12, out o.Thickness); access.GetItem(13, out o.ElementSize); bool moved = access.GetTransform(14, out Transform x); access.GetItem(15, out o.Name); string dir = WeaveIo.Text(access, 16); access.GetItem(17, out save);
            o.PairSpacing = Math.Max(1, o.PairSpacing);
            var d0 = new JointMaterial();
            m.Placeholder = m.EAcross == d0.EAcross && m.G == d0.G && m.SigmaSide == d0.SigmaSide && m.TauSide == d0.TauSide && m.TauLayer == d0.TauLayer;
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var set = zones[0].Set;
            var d = JointData.Compute(tp, set, o, moved ? (Func<Point3d, Point3d>)(q => { q.Transform(x); return q; }) : null);
            var want = new HashSet<int>(zones.Select(z => z.Id));
            d.Keep(want);
            if (m.Placeholder) WeaveIo.Remark(access, "All material values are placeholders: stiffness and strength are estimates. Measure them with Weave Test.");
            var j = WeaveCheck.Sum(d.Joints);
            string text = d.Json();
            if (Io.Step(access, 95)) return;
            if (save)
            {
                try
                {
                    string json = WeaveIo.FreePath(access, this, dir, OutputFile.Name("JOINT", o.Name, null, ".json"));
                    string stem = Path.Combine(Path.GetDirectoryName(json) ?? "", Path.GetFileNameWithoutExtension(json));
                    File.WriteAllText(json, text); File.WriteAllLines(stem + "_zones.csv", d.ZoneCsv()); File.WriteAllLines(stem + "_pairs.csv", d.PairCsv()); File.WriteAllLines(stem + "_links.csv", d.LinkCsv());
                    WeaveIo.Remark(access, "Saved " + json + " and three tables");
                }
                catch (Exception ex) { WeaveIo.Error(access, "Could not write the files: " + ex.Message); }
                Files.Reset(this, 17);
            }
            Io.Label(this, d.Joints.Count + " joints");
            access.SetTree(0, WeaveIo.Branches(d.Pairs.Select(p => WeaveIo.At(p.Zone, p.A))));
            access.SetTree(1, WeaveIo.Branches(d.Pairs.Select(p => WeaveIo.At(p.Zone, p.B))));
            access.SetTree(2, WeaveIo.Branches(d.Links.Select(k => WeaveIo.At(k.Zone, new Line(k.A, k.B)))));
            access.SetTree(3, WeaveIo.Branches(d.Links.Select(k => WeaveIo.At(k.Zone, k.TOpen))));
            access.SetTwig(4, d.Joints.Select(q => q.HorizontalShare).ToArray(), null, null); access.SetTwig(5, d.Joints.Select(q => q.VerticalShare).ToArray(), null, null);
            access.SetTwig(6, d.ZoneCsv().ToArray(), null, null); access.SetItem(7, text);
            access.SetItem(8, Inv.F($"{d.Joints.Count} joint(s), {j.Face / 100:0.0} cm2 of joint face: side contact {j.VerticalShare:0.00}, interlock {j.HorizontalShare:0.00} mm2 per mm2 (depth {j.Depth:0.00} mm). {d.Pairs.Count} point pairs, {d.Links.Count} links.\n\n") + JointData.Rule.Replace(". ", ".\n"));
        }
    }

    // ============================================================== shape field
    [IoId("d725c586-712f-4199-91e6-a3494cbdd5fb")]
    public sealed class ShapeFieldComponent : Component
    {
        public ShapeFieldComponent() : base(Io.Name("Shape Field", "How high a part has to become, over its plan (x, y): from the utilisation of a structural analysis (stress / allowed stress at points), from points with wanted heights or factors, from a profile along a line, from a guide surface, or from the file of an earlier step. Goes into Reshape Path, Reshape Code and Compare Variants.", Io.Slicer, Group.Path)) { }
        public ShapeFieldComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerShapeFieldIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Mode", "M", "What the values are:\n0 = Factors at points: 1.5 = one and a half times the print height\n1 = Heights at points (mm above the bottom of the part)\n2 = Utilisation at points: stress / allowed stress (or any number with a target); above the target the part grows, below it shrinks\n3 = Factors along a line\n4 = Heights along a line (mm)\n5 = Guide surface: its height above the bottom of the part\n6 = File written by this component in an earlier step").Set(2).Choices(Modes);
            inputs.AddPoint("Points", "P", "Modes 0 to 2: the places of the values. Only x and y count; several points above one another (a wall that was analysed over its height) are joined, the largest utilisation counts", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Values", "V", "Modes 0 to 4: one value per point, or per position along the line", Access.Twig, Requirement.MayBeMissing);
            inputs.AddLine("Line", "L", "Modes 3 and 4: the line the values run along (seen from above)", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Positions", "t", "Modes 3 and 4: positions along the line, 0 = start, 1 = end. Empty = evenly spaced", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Guide", "G", "Mode 5: surface(s), Breps or meshes the new top has to follow", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Previous", "Pr", "Mode 2: what was made in the step before: the file ..._field.csv that Reshape Path wrote with Write Log (or a Shape Field). Empty = the part as it was. Mode 6: the file", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Height", "H", "Print height of the part as it was sliced (mm). For the preview, and for a Previous field in mm").Set(100.0);
            inputs.AddNumber("Target", "Ta", "Mode 2: the value that is just right (1 for stress / allowed stress)").Set(1.0);
            inputs.AddNumber("Exponent", "Ex", "Mode 2: new height = old height x (value / target) ^ exponent. 0.5 = bending about the axis across the height (stress goes with 1 / height squared), 1 = tension, compression or shear (stress goes with 1 / height, the plain linear rule), 0 = no change. Give several to compare them in Compare Variants").Set(0.5).Values(0, 0.33, 0.5, 0.75, 1);
            inputs.AddNumber("Damping", "Da", "Mode 2: 1 = the full step, 0.5 = half of it. Below 1 the loop does not swing").Set(0.7).Values(0.3, 0.5, 0.7, 1);
            inputs.AddNumber("Min Factor", "Mi", "Smallest factor").Set(0.4).Values(0.1, 0.2, 0.25, 0.4, 0.5, 0.75, 1);
            inputs.AddNumber("Max Factor", "Ma", "Largest factor").Set(2.5).Values(1, 1.5, 2.5, 4, 5);
            inputs.AddNumber("Smooth", "Sm", "Points only: 0 = the field goes through every value, more = it passes beside noisy values (about 0.1 to 10). Reshape Path rounds the shape anyway").Set(0.0);
            inputs.AddText("File", "F", "Where the field is saved (CSV), to go on with it in the next step. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "S", "Saves the field").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Field", "F", "The shape field, for Reshape Path, Reshape Code and Compare Variants");
            outputs.AddMesh("Preview", "Pv", "The wanted top, as a surface (before the limits of the printer; Reshape Path shows the real one). Slicing anew: use the Top of Reshape Path as guide of Slice");
            outputs.AddText("Report", "R", "What the field is; the first line says it all", Access.Twig);
            outputs.AddText("Table", "T", "The field as CSV (x, y, value)", Access.Twig);
        }

        internal static readonly string[] Modes = { "Factors at points", "Heights at points (mm)", "Utilisation at points", "Factors along a line", "Heights along a line (mm)", "Guide surface (heights)", "File of an earlier step" };

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            int mode; access.GetItem(0, out mode);
            access.ItemArray(1, out Point3d[] pts); access.ItemArray(2, out double[] vals);
            if (!access.GetItem(3, out Line line)) line = Line.Unset;
            access.ItemArray(4, out double[] positions);
            access.ItemArray(5, out object[] guide);
            if (!access.GetItem(6, out object previous)) previous = null;
            double height, target, ex, damp, min, max, smooth; bool save;
            access.GetItem(7, out height); access.GetItem(8, out target); access.GetItem(9, out ex); access.GetItem(10, out damp); access.GetItem(11, out min); access.GetItem(12, out max); access.GetItem(13, out smooth); string file = WeaveIo.Text(access, 14); access.GetItem(15, out save);
            ShapeField f;
            string missing = null;
            try
            {
                ShapeField prev = previous as ShapeField;
                if (prev == null && previous != null)
                {
                    string path = ReshapeIo.PathOf(previous.ToString());
                    if (path.Length > 0) { if (!File.Exists(path)) throw new ArgumentException("Previous: the file " + path + " does not exist."); prev = ShapeField.FromCsv(File.ReadAllLines(path)); }
                }
                f = null;
                switch (mode)
                {
                    case 0: case 1:
                        if (pts.Length == 0) { missing = "Connect Points and Values."; break; }
                        f = ShapeField.FromPoints(pts, vals, mode == 1, smooth); break;
                    case 2:
                        if (pts.Length == 0) { missing = "Connect the Points and the Values (utilisation) of the analysis."; break; }
                        f = ShapeField.FromUtilisation(pts, vals, new ShapeField.UtilisationOptions { Target = target, Exponent = ex, Damping = damp, Min = min, Max = max, Smooth = smooth, Previous = prev, Height = height }); break;
                    case 3: case 4:
                        if (!line.IsValid || vals.Length < 2) { missing = "Connect a Line and at least two Values."; break; }
                        var ts = positions.Length > 0 ? positions.ToList() : Enumerable.Range(0, vals.Length).Select(i => i / (double)(vals.Length - 1)).ToList();
                        double len = Math.Sqrt(line.Direction.X * line.Direction.X + line.Direction.Y * line.Direction.Y);
                        f = ShapeField.FromProfile(line.From, line.Direction, ts.Select(t => t * len).ToList(), vals, mode == 4); break;
                    case 5:
                        if (guide.Length == 0) { missing = "Connect the Guide surface."; break; }
                        f = FromGuide(guide); break;
                    default:
                        if (prev == null) { missing = "Connect the file of the earlier step to Previous."; break; }
                        f = prev; break;
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException)) { WeaveIo.Error(access, e.Message); if (save) Files.Reset(this, 15); return; }
            if (missing != null) { WeaveIo.Warning(access, missing); if (save) Files.Reset(this, 15); return; }
            if (mode != 2 && mode != 5 && !f.IsHeight) { f.Lowest = min; f.Highest = max; }
            var rep = new List<string>();
            double lo = double.MaxValue, hi = 0;
            for (int i = 0; i <= 40; i++) for (int j = 0; j <= 40; j++) { double v = f.Value(f.X0 + (f.X1 - f.X0) * i / 40, f.Y0 + (f.Y1 - f.Y0) * j / 40); if (v < lo) lo = v; if (v > hi) hi = v; }
            string unit = f.IsHeight ? " mm" : "";
            rep.Add(Inv.F($"OK: {(f.IsHeight ? "height" : "factor")} {lo:0.##} to {hi:0.##}{unit} ({hi / Math.Max(1e-9, lo):0.0} : 1), from {f.Source}."));
            rep.AddRange(f.Notes);
            Io.Label(this, Inv.F($"{lo:0.##} - {hi:0.##}{unit}"));
            if (f.MaxUtilisation > 0 && f.Over > 0) WeaveIo.Remark(access, Inv.F($"Utilisation up to {f.MaxUtilisation:0.##}; {100 * f.Over:0} % of the points are above the target."));
            var csv = f.ToCsv();
            if (save)
            {
                string name = "ShapeField_" + DateTime.Now.ToString("yyMMddHHmm", Inv.C) + ".csv";
                try { rep.Add("Saved " + OutputFile.Write(WeaveIo.FreePath(access, this, file, name), name, csv)); WeaveIo.Remark(access, rep[rep.Count - 1]); }
                catch (Exception e) { WeaveIo.Error(access, "Could not write the file: " + e.Message); }
                Files.Reset(this, 15);
            }
            access.SetItem(0, f);
            if (f.X1 - f.X0 > 1e-6 || f.Y1 - f.Y0 > 1e-6)
            {
                double pad = 0.05 * Math.Max(f.X1 - f.X0, f.Y1 - f.Y0), px = f.X1 - f.X0 < pad ? pad : 0, py = f.Y1 - f.Y0 < pad ? pad : 0;
                access.SetItem(1, ReshapeIo.Surface((x, y) => f.IsHeight ? f.Value(x, y) : f.Value(x, y) * height, f.X0 - px, f.Y0 - py, f.X1 + px, f.Y1 + py));
            }
            access.SetTwig(2, rep.ToArray(), null, null); access.SetTwig(3, csv.ToArray(), null, null);
        }

        /// <summary>The highest point of the guide over every place of a grid.</summary>
        private static ShapeField FromGuide(IEnumerable<object> guide)
        {
            var log = new List<string>();
            Mesh m = SliceEngine.ToMesh(guide.Where(g => g != null).Select(Io.Solid), 0.1, log);
            if (m == null || m.Vertices.Count == 0) throw new ArgumentException("The guide could not be meshed. " + string.Join(" ", log));
            var bb = m.GetBoundingBox(true); double size = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y), cell = size / 80;
            int nx = Math.Max(2, (int)Math.Ceiling((bb.Max.X - bb.Min.X) / cell) + 1), ny = Math.Max(2, (int)Math.Ceiling((bb.Max.Y - bb.Min.Y) / cell) + 1);
            var v = new double[nx * ny]; double top = bb.Max.Z + 10;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double x = Math.Min(bb.Max.X - 1e-4 * size, Math.Max(bb.Min.X + 1e-4 * size, bb.Min.X + i * cell)), y = Math.Min(bb.Max.Y - 1e-4 * size, Math.Max(bb.Min.Y + 1e-4 * size, bb.Min.Y + j * cell));
                    double t = Intersection.MeshRay(m, new Ray3d(new Point3d(x, y, top), -Vector3d.ZAxis));
                    v[j * nx + i] = t >= 0 ? top - t : double.NaN;
                }
            if (v.Any(z => !double.IsNaN(z) && z <= 0)) throw new ArgumentException("The guide surface reaches down to the bed (Z 0) or below it. It has to stand above the part everywhere.");
            return ShapeField.FromGrid(bb.Min.X, bb.Min.Y, cell, nx, ny, v, true, "guide surface");
        }
    }

    // ============================================================== reshape path
    [IoId("da7fdbbd-89aa-4201-9f28-10763a3295fc")]
    public sealed class ReshapePathComponent : Component
    {
        public ReshapePathComponent() : base(Io.Name("Reshape Path", "Gives a finished toolpath another shape: more print height where the Shape Field asks for it, less elsewhere, with the same number of layers (they fan out). The plan of the path stays: seam, infill and the one continuous line are kept. Skirt, way in and flat layers are not touched, the way out starts at the new end. The extrusion is worked out again from the new distance to the lap below. Several variants (fanned laps with a square or a vertical tool, wider beads, extra passes, cut laps) for printing and testing against each other. Put it after Start and End, before the output to the machine.", Io.Slicer, Group.Path)) { }
        public ReshapePathComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerReshapeIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path (after Start and End)");
            inputs.AddGeneric("Field", "F", "Shape Field");
            inputs.AddInteger("Variant", "V", "Which way:\n0 = Fanned laps, tool square to the layers (robot)\n1 = Fanned laps, nozzle vertical (gantry)\n2 = Fanned and wider beads, tool square: half of the factor (its root) goes into the height, the rest into the bead width\n3 = the same, nozzle vertical\n4 = Wider beads only: layers stay flat\n5 = Fanned with extra passes, tool square: layers that get too thick are printed in 3 or 5 passes (single walls only)\n6 = the same, nozzle vertical\n7 = Flat layers of constant height, the laps are cut where the part is lower and turn back").Set(0).Choices(ReshapeVariants.Names);
            inputs.AddNumber("Material", "Mt", "Material of the part in % of before: 100 = the same amount, only moved to where it is needed; 80 = a fifth less; 0 = the heights exactly as the field says").Set(100.0).Values(0, 60, 70, 80, 90, 100);
            inputs.AddNumber("Min Layer", "Hi", "Smallest layer height (mm). 0 = automatic (0.8 mm; less for small beads)").Set(0.0);
            inputs.AddNumber("Max Layer", "Ha", "Largest layer height (mm). 0 = automatic (half the bead width, or the layer height of the print)").Set(0.0);
            inputs.AddNumber("Max Slope", "Sl", "Steepest slope of a layer (degrees). 0 = automatic: 30 with a tilted tool; with a vertical nozzle what its flat tip allows at a corner").Set(0.0);
            inputs.AddNumber("Smooth", "Sm", "The new top is rounded over about this length (mm). 0 = 6 bead widths").Set(0.0);
            inputs.AddInteger("Flat Layers", "Fl", "Layers at the bottom that stay flat. -1 = the first layer and the solid bottom").Set(-1);
            inputs.AddInteger("Roof", "Ro", "A wall that closes to a roof:\n0 = the layers of the roof keep their thickness and move along with the wall\n1 = the roof is stretched too\n2 = refuse such parts").Set(0).Choices("Roof layers keep their thickness", "Stretch the roof too", "Refuse parts with a roof");
            inputs.AddNumber("Max Flow", "Mf", "Moves that need more than this (mm3/s) run slower. 0 = off, -1 = never more than the path needed before").Set(0.0);
            ReshapeIo.HeadInputs(inputs);
            inputs.AddMesh("Mesh", "Me", "Optional meshes to bend the same way (the part, for the next analysis)", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("Name", "N", "Name of the part, for the log").Set("Part");
            inputs.AddInteger("Step", "St", "Number of the step in the loop, for the log").Set(0);
            inputs.AddText("Log File", "Lg", "CSV file the result is added to (one line per save); the report (.md), the layers and the field as made (.csv) go beside it. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Write Log", "W", "Adds this result to the log").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The reshaped path, with tool axes");
            outputs.AddText("Report", "R", "What was done and what the check found; the first line is the verdict", Access.Twig);
            outputs.AddNumber("Layer Height", "H", "Bead height of every point (mm)", Access.Twig);
            outputs.AddNumber("Tilt", "Ti", "Tilt of the tool at every point (degrees)", Access.Twig);
            outputs.AddPoint("Problems", "X", "Places where the check found something (head, height, turning). Should be empty", Access.Twig);
            outputs.AddMesh("Top", "Tp", "The new top of the part as a surface. Slicing anew: bend the part (Mesh) and use Top as the guide of Slice (flat bottom to guide)");
            outputs.AddMesh("Mesh", "M", "The meshes, bent", Access.Twig);
            outputs.AddText("Layers", "L", "One line per layer (CSV): length, heights, tilt, E, time", Access.Twig);
            outputs.AddText("Log", "Lo", "The line for the experiment log (CSV, with header)", Access.Twig);
            outputs.AddGeneric("Made", "Fm", "What was really made, as a Shape Field of factors (after limits, rounding and material). Write Log saves it as a file: that file is the Previous of the Shape Field in the next step of the loop");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Get<Toolpath>(access, 0); var f = Io.Get<ShapeField>(access, 1);
            if (tp == null) { WeaveIo.Warning(access, "Connect a Toolpath."); return; }
            if (f == null) { WeaveIo.Warning(access, "Connect a Shape Field."); return; }
            var o = new ReshapeOptions(); int v, roof, step; string name; bool write;
            access.GetItem(2, out v); access.GetItem(3, out o.Material); access.GetItem(4, out o.MinLayer); access.GetItem(5, out o.MaxLayer); access.GetItem(6, out o.MaxSlope); access.GetItem(7, out o.Smooth); access.GetItem(8, out o.FlatLayers); access.GetItem(9, out roof); access.GetItem(10, out o.MaxFlow);
            access.GetItem(19, out write);
            if (!ReshapeIo.Head(access, 11, o.Head)) { if (write) Files.Reset(this, 19); return; }
            access.ItemArray(15, out Mesh[] meshes); access.GetItem(16, out name); access.GetItem(17, out step); string log = WeaveIo.Text(access, 18);
            o.Roof = (RoofMode)Math.Max(0, Math.Min(2, roof));
            var variant = (ReshapeVariant)Math.Max(0, Math.Min(ReshapeVariants.Names.Length - 1, v));
            var copies = meshes.Where(m => m != null).Select(m => m.DuplicateMesh()).ToArray();
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var r = ReshapeVariants.Run(tp, f, variant, o);
            if (Io.Step(access, 90)) return;
            ReshapeIo.Verdict(access, r.Report.FirstOrDefault());
            foreach (var w in r.Warnings) WeaveIo.Warning(access, w);
            string line = PathReshape.LogLine(name, step, f, r, o, DateTime.Now);
            if (write)
            {
                try
                {
                    string p = ReshapeIo.Append(log, "Reshape_Log.csv", PathReshape.LogHeader, new[] { line });
                    string stem = Path.Combine(Path.GetDirectoryName(p) ?? "", Path.GetFileNameWithoutExtension(p) + "_" + OutputFile.Clean(name) + "_step" + step.ToString(Inv.C) + "_v" + (int)variant);
                    File.WriteAllText(stem + ".md", PathReshape.Markdown(name, step, f, r, DateTime.Now));
                    if (r.Path != null) { File.WriteAllLines(stem + "_field.csv", r.Realised(tp).ToCsv()); File.WriteAllLines(stem + "_layers.csv", PathReshape.LayerCsv(r.Path)); }
                    WeaveIo.Remark(access, "Added to " + p);
                }
                catch (Exception e) { WeaveIo.Error(access, "Could not write the log: " + e.Message); }
                Files.Reset(this, 19);
            }
            access.SetTwig(1, r.Report.ToArray(), null, null);
            access.SetTwig(8, new[] { PathReshape.LogHeader, line }, null, null);
            if (r.Path == null) { Io.Label(this, "refused"); return; }
            Io.Label(this, Inv.F($"{r.HeightAfterMin:0} - {r.HeightAfterMax:0} mm, {100 * r.MaterialShare:0} %"));
            access.SetItem(0, r.Path);
            access.SetTwig(2, r.Path.Heights.ToArray(), null, null);
            access.SetTwig(3, r.Path.Normals.Select(n => Math.Acos(Math.Max(-1, Math.Min(1, n.Z))) * 180 / Math.PI).ToArray(), null, null);
            if (r.Check != null) access.SetTwig(4, r.Check.Collisions.Concat(r.Check.BadHeights).Concat(r.Check.FastTurns).ToArray(), null, null);
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 1; i < tp.Count; i++) if (LapBelow.Prints(tp, i) && tp.Roles[i] != PathRole.Skirt) { var q = tp.Points[i]; x0 = Math.Min(x0, q.X); x1 = Math.Max(x1, q.X); y0 = Math.Min(y0, q.Y); y1 = Math.Max(y1, q.Y); }
            double pad = tp.Settings.BeadWidth;
            if (x1 > x0) access.SetItem(5, ReshapeIo.Surface((x, y) => variant == ReshapeVariant.CutLaps ? Math.Min(r.Map.ZTop, f.Factor(x, y, r.HeightBefore) * r.Scale * r.HeightBefore + r.Map.ZBase) : r.Map.Z(x, y, r.Map.ZTop), x0 - pad, y0 - pad, x1 + pad, y1 + pad));
            foreach (var m in copies)
            {
                for (int i = 0; i < m.Vertices.Count; i++) { Point3d q = m.Vertices[i]; m.Vertices.SetVertex(i, q.X, q.Y, r.Map.Z(q.X, q.Y, q.Z)); }
                m.Normals.ComputeNormals();
            }
            access.SetTwig(6, copies, null, null);
            access.SetTwig(7, PathReshape.LayerCsv(r.Path).ToArray(), null, null);
            access.SetItem(9, r.Realised(tp));
        }
    }

    // ============================================================== compare variants
    [IoId("c7539ae7-dd92-4437-899f-18aea81c385e")]
    public sealed class CompareVariantsComponent : Component
    {
        public CompareVariantsComponent() : base(Io.Name("Compare Variants", "Reshapes one toolpath in several ways and with several Shape Fields (for example linear and stress based, or several exponents) and puts the results into one table: material, heights, layer heights, bead widths, tilt and turning of the tool, slope, flow, time, travel moves, head clearance. Each result comes out as a toolpath, so it can be printed and tested against the others.", Io.Slicer, Group.Path)) { }
        public CompareVariantsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerCompareIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path (after Start and End)");
            inputs.AddGeneric("Fields", "F", "One or more Shape Fields", Access.Twig);
            inputs.AddInteger("Variants", "V", "Numbers of the variants (see Reshape Path, 0 to 7). Empty = all", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Material", "Mt", "Material in % of before, the same for all (0 = as the field says)").Set(100.0).Values(0, 60, 70, 80, 90, 100);
            inputs.AddNumber("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic").Set(0.0);
            inputs.AddNumber("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic").Set(0.0);
            inputs.AddNumber("Max Slope", "Sl", "Steepest slope (degrees), 0 = automatic").Set(0.0);
            inputs.AddNumber("Smooth", "Sm", "Rounding (mm), 0 = 6 bead widths").Set(0.0);
            inputs.AddNumber("Max Flow", "Mf", "mm3/s; 0 = off, -1 = never more than before").Set(0.0);
            ReshapeIo.HeadInputs(inputs);
            inputs.AddText("Name", "N", "Name of the part, for the log").Set("Part");
            inputs.AddText("Log File", "Lg", "CSV file the results are added to; the table goes beside it as .md. Empty = the Desktop", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Write Log", "W", "Adds all results to the log").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Table", "Tb", "The comparison (Markdown table)", Access.Twig);
            outputs.AddGeneric("Toolpaths", "T", "The path of every row (empty where refused)", Access.Twig);
            outputs.AddText("Verdicts", "R", "First line of every report", Access.Twig);
            outputs.AddText("Log", "Lo", "The rows for the experiment log (CSV, with header)", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Get<Toolpath>(access, 0); access.ItemArray(1, out object[] items);
            var fields = items.OfType<ShapeField>().ToList();
            if (tp == null || fields.Count == 0) { WeaveIo.Warning(access, "Connect a Toolpath and at least one Shape Field."); return; }
            access.ItemArray(2, out int[] vs);
            var variants = (vs.Length > 0 ? vs : Enumerable.Range(0, ReshapeVariants.Names.Length)).Where(i => i >= 0 && i < ReshapeVariants.Names.Length).Select(i => (ReshapeVariant)i).ToList();
            var o = new ReshapeOptions(); string name; bool write;
            access.GetItem(3, out o.Material); access.GetItem(4, out o.MinLayer); access.GetItem(5, out o.MaxLayer); access.GetItem(6, out o.MaxSlope); access.GetItem(7, out o.Smooth); access.GetItem(8, out o.MaxFlow);
            access.GetItem(13, out name); string log = WeaveIo.Text(access, 14); access.GetItem(15, out write);
            if (!ReshapeIo.Head(access, 9, o.Head)) { if (write) Files.Reset(this, 15); return; }
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var rows = ReshapeVariants.Compare(tp, fields, variants, o);
            if (Io.Step(access, 95)) return;
            var table = ReshapeVariants.Table(rows); var now = DateTime.Now;
            var lines = rows.Select((q, i) => PathReshape.LogLine(name, i, q.Field, q.Result, o, now)).ToList();
            if (write)
            {
                try { string p = ReshapeIo.Append(log, "Reshape_Log.csv", PathReshape.LogHeader, lines); ReshapeIo.AppendText(Path.ChangeExtension(p, ".md"), new[] { "## " + name + ", " + now.ToString("yyyy-MM-dd HH:mm", Inv.C), "" }.Concat(table).Concat(new[] { "" })); WeaveIo.Remark(access, "Added to " + p); }
                catch (Exception e) { WeaveIo.Error(access, "Could not write the log: " + e.Message); }
                Files.Reset(this, 15);
            }
            int ok = rows.Count(q => q.Result.Path != null && (q.Result.Check == null || q.Result.Check.Ok));
            Io.Label(this, Inv.F($"{ok} of {rows.Count} OK"));
            access.SetTwig(0, table.ToArray(), null, null);
            access.SetTwig(1, rows.Select(q => (object)q.Result.Path).ToArray(), null, null); // null where refused
            access.SetTwig(2, rows.Select(q => ReshapeVariants.Short(q.Variant) + ": " + q.Result.Report.FirstOrDefault()).ToArray(), null, null);
            access.SetTwig(3, new[] { PathReshape.LogHeader }.Concat(lines).ToArray(), null, null);
        }
    }

    // ============================================================== reshape code
    [IoId("6037adb4-d591-4e24-83b1-dea7746db6d2")]
    public sealed class ReshapeCodeComponent : Component
    {
        public ReshapeCodeComponent() : base(Io.Name("Reshape Code", "Reshapes a finished file: G-code (Marlin, Cura, PrusaSlicer, our own) or a Weber .dxr. Only the numbers that change are written into their lines (Z, E, tool angles A B C for the robot, F where the flow is limited); start code, end code, skirt, comments and every other line stay byte for byte. The extrusion is worked out again from the new distance to the lap below and keeps the calibration of the file. Files that are not understood are refused.", Io.Slicer, Group.Output)) { }
        public ReshapeCodeComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerReshapeCodeIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("File", "File", "The G-code or .dxr file", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Code", "C", "Instead of a file: the lines of the program", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Field", "F", "Shape Field");
            inputs.AddInteger("Variant", "V", "Which way (see Reshape Path, 0 to 6). A tilted tool is written for .dxr files only; laps that are cut (7) are not possible in a finished file").Set(0).Choices(ReshapeVariants.Names.Take(7).ToArray());
            inputs.AddNumber("Material", "Mt", "Material of the part in % of before; 0 = as the field says").Set(100.0).Values(0, 60, 70, 80, 90, 100);
            inputs.AddNumber("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic").Set(0.0);
            inputs.AddNumber("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic").Set(0.0);
            inputs.AddNumber("Max Slope", "Sl", "Steepest slope (degrees), 0 = automatic").Set(0.0);
            inputs.AddNumber("Smooth", "Sm", "Rounding (mm), 0 = 6 bead widths").Set(0.0);
            inputs.AddInteger("Flat Layers", "Fl", "Layers at the bottom that stay flat. -1 = the first layer (and the solid bottom, if the file marks it)").Set(-1);
            inputs.AddInteger("Roof", "Ro", "A wall that closes to a roof: 0 = its layers keep their thickness, 1 = stretch, 2 = refuse").Set(0).Choices("Roof layers keep their thickness", "Stretch the roof too", "Refuse parts with a roof");
            inputs.AddNumber("Flow Limit", "Fw", "No move puts out more per second than the largest of the file times this (1 = not more than before; thick places run slower). 0 = off, F stays as it is").Set(0.0).Values(0, 1, 1.2);
            inputs.AddNumber("Bead Width", "W", "Bead width (mm). 0 = from the file").Set(0.0);
            inputs.AddNumber("Layer Height", "H", "Layer height (mm). 0 = from the Z steps").Set(0.0);
            inputs.AddGeneric("Machine Profile", "MP", "Optional. What E means in the file (a Machine Profile): for the bead width and the cross section", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Output", "O", "File to write. Empty = beside the old one, with _reshaped in its name", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "S", "Writes the file").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Code", "C", "The lines of the new program", Access.Twig);
            outputs.AddText("Report", "R", "What was done; the first line is the verdict", Access.Twig);
            outputs.AddGeneric("Before", "B", "The file as a toolpath");
            outputs.AddGeneric("After", "A", "The new file as a toolpath");
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            string file = WeaveIo.Text(access, 0), outFile = WeaveIo.Text(access, 15); access.ItemArray(1, out string[] code); bool save; access.GetItem(16, out save);
            var f = Io.Get<ShapeField>(access, 2);
            if (string.IsNullOrWhiteSpace(file) && code.Length == 0) { WeaveIo.Warning(access, "Connect a File or the lines of a program."); if (save) Files.Reset(this, 16); return; }
            if (f == null) { WeaveIo.Warning(access, "Connect a Shape Field."); if (save) Files.Reset(this, 16); return; }
            var o = new ReshapeCode.Options(); var s = o.Shape; int v, roof;
            access.GetItem(3, out v); access.GetItem(4, out s.Material); access.GetItem(5, out s.MinLayer); access.GetItem(6, out s.MaxLayer); access.GetItem(7, out s.MaxSlope); access.GetItem(8, out s.Smooth); access.GetItem(9, out s.FlatLayers); access.GetItem(10, out roof);
            access.GetItem(11, out o.MaxFlowFactor); access.GetItem(12, out o.BeadWidth); access.GetItem(13, out o.LayerHeight); o.Machine = Io.Get<MachineProfile>(access, 14);
            o.Variant = (ReshapeVariant)Math.Max(0, Math.Min(ReshapeVariants.Names.Length - 1, v)); s.Roof = (RoofMode)Math.Max(0, Math.Min(2, roof));
            string path = ReshapeIo.PathOf(file);
            string target = null;
            if (save)
            {
                string def = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) + "_reshaped" + Path.GetExtension(path) : "Reshaped.gcode";
                target = WeaveIo.FreePath(access, this, string.IsNullOrWhiteSpace(outFile) && path.Length > 0 ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", def) : outFile, def);
                if (path.Length > 0 && string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) { WeaveIo.Error(access, "Output is the file itself: the original is never written over."); Files.Reset(this, 16); return; }
            }
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var r = code.Length > 0 ? ReshapeCode.Run(string.Join("\n", code) + "\n", f, o) : ReshapeCode.File_(path, f, o);
            if (Io.Step(access, 95)) return;
            ReshapeIo.Verdict(access, r.Report.FirstOrDefault());
            foreach (var w in r.Warnings) WeaveIo.Warning(access, w);
            if (r.Refused == null && target != null)
            {
                try { ReshapeCode.Save(target, r); r.Report.Add("Saved " + target); WeaveIo.Remark(access, "Saved " + target); }
                catch (Exception e) { WeaveIo.Error(access, "Could not write the file: " + e.Message); }
            }
            if (save) Files.Reset(this, 16);
            access.SetTwig(1, r.Report.ToArray(), null, null);
            if (r.Before != null) access.SetItem(2, r.Before);
            if (r.Refused != null) { Io.Label(this, "refused"); return; }
            Io.Label(this, Inv.F($"{r.Changed} lines changed"));
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            if (r.After != null) access.SetItem(3, r.After);
        }
    }

    // ============================================================== multi-axis check
    [IoId("7a657099-5808-4afe-b84f-583886ea40a6")]
    public sealed class MultiAxisCheckComponent : Component
    {
        public MultiAxisCheckComponent() : base(Io.Name("Multi-Axis Check", "Checks a path along its real tool axis (tilted or vertical): bead heights above the lap below, sideways steps, extrusion against the measured gaps, how far the tool is tilted and how fast it turns (degrees per mm and per second at the print speed), how steeply the path runs against the tool, what the head reaches into, and the material flow. Reach and axis limits of the robot are the job of Robot Program.", Io.Slicer, Group.Check)) { }
        public MultiAxisCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerMultiAxisIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Print path");
            ReshapeIo.HeadInputs(inputs);
            inputs.AddNumber("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic").Set(0.0);
            inputs.AddNumber("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic (half the bead width)").Set(0.0);
            inputs.AddNumber("Max Tilt", "Ti", "Largest tilt of the tool (degrees)").Set(45.0);
            inputs.AddNumber("Max Turn Speed", "Ts", "How fast the tool may turn (degrees per second)").Set(30.0);
            inputs.AddNumber("Max Turn", "Tm", "How fast the tool may turn along the path (degrees per mm)").Set(2.0);
            inputs.AddNumber("Max Flow", "Mf", "Largest material flow (mm3/s), 0 = only told").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddBoolean("OK", "OK", "True if nothing is wrong");
            outputs.AddText("Report", "R", "What was found; the first line is the verdict", Access.Twig);
            outputs.AddNumber("Bead Height", "H", "Measured height above the lap below, along the tool (mm), for every point; 0 = not measured", Access.Twig);
            outputs.AddNumber("Tilt", "Ti", "Tilt of the tool (degrees) for every point", Access.Twig);
            outputs.AddPoint("Collisions", "X", "Where the head reaches into earlier beads", Access.Twig);
            outputs.AddPoint("Bad Heights", "Bh", "Where the bead is too thick or too thin", Access.Twig);
            outputs.AddPoint("Bad Flow", "Bf", "Where the extrusion is more than 10 % off the measured gap", Access.Twig);
            outputs.AddPoint("Fast Turns", "Ft", "Where the tool turns too fast", Access.Twig);
            outputs.AddText("Layers", "L", "One line per layer (CSV)", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var tp = Io.Path(access, 0);
            if (tp == null || tp.Count < 3) return;
            var o = new MultiAxisCheck.Options();
            if (!ReshapeIo.Head(access, 1, o.Head)) return;
            access.GetItem(5, out o.MinLayer); access.GetItem(6, out o.MaxLayer); access.GetItem(7, out o.MaxTilt); access.GetItem(8, out o.MaxTiltPerSecond); access.GetItem(9, out o.MaxTiltPerMm); access.GetItem(10, out o.MaxFlow);
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var r = MultiAxisCheck.Run(tp, o);
            Io.Label(this, r.Ok ? "OK" : "Check!");
            ReshapeIo.Verdict(access, r.Report.FirstOrDefault());
            access.SetItem(0, r.Ok); access.SetTwig(1, r.Report.ToArray(), null, null); access.SetTwig(2, r.Heights.ToArray(), null, null); access.SetTwig(3, r.Tilts.ToArray(), null, null);
            access.SetTwig(4, r.Collisions.ToArray(), null, null); access.SetTwig(5, r.BadHeights.ToArray(), null, null); access.SetTwig(6, r.BadFlow.ToArray(), null, null); access.SetTwig(7, r.FastTurns.ToArray(), null, null);
            access.SetTwig(8, PathReshape.LayerCsv(tp).ToArray(), null, null);
        }
    }
}
