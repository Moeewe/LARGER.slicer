using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    internal static class WeaveIO
    {
        /// <summary>Curves, points, Breps and meshes as regions (copies, safe in the background).</summary>
        public static List<WeaveRegion> Regions(IEnumerable<IGH_GeometricGoo> items, double radius, List<string> log)
        {
            var res = new List<WeaveRegion>();
            foreach (var goo in items)
            {
                if (goo == null) continue;
                object o = LARGERslicer.Components.AsyncSolve.Copy(goo);
                if (o is Point p0) o = p0.Location;
                if (o is Line ln) o = new LineCurve(ln);
                if (o is Circle ci) o = new ArcCurve(ci);
                if (o is Rectangle3d rc) o = rc.ToNurbsCurve();
                if (o is Surface sf) o = sf.ToBrep();
                if (o is Box bx) o = bx.ToBrep();
                if (o is Point3d p) res.Add(new WeaveRegion { Line = new List<Point3d> { p }, Radius = radius, Spatial = true });
                else if (o is Curve c)
                {
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
                else if (o is Brep b)
                {
                    var bb = b.GetBoundingBox(true); var guard = new object();
                    if (b.IsSolid) res.Add(new WeaveRegion { Inside = q => bb.Contains(q) && Locked(guard, () => b.IsPointInside(q, 0.01, false)) });
                    else { var big = bb; big.Inflate(radius); res.Add(new WeaveRegion { Inside = q => big.Contains(q) && Locked(guard, () => b.ClosestPoint(q).DistanceTo(q) <= radius) }); }
                }
                else if (o is Mesh m)
                {
                    var bb = m.GetBoundingBox(true); var guard = new object();
                    if (m.IsClosed) res.Add(new WeaveRegion { Inside = q => bb.Contains(q) && Locked(guard, () => m.IsPointInside(q, 0.01, false)) });
                    else { var big = bb; big.Inflate(radius); res.Add(new WeaveRegion { Inside = q => big.Contains(q) && Locked(guard, () => m.ClosestPoint(q).DistanceTo(q) <= radius) }); }
                }
                else log?.Add("This kind of geometry can not be a region: " + (o == null ? "nothing" : o.GetType().Name) + ". Use closed curves, open curves or points (with Radius), Breps or meshes.");
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

        public static List<WeaveZone> Zones(IGH_DataAccess da, int index)
        {
            var goos = new List<IGH_Goo>(); var res = new List<WeaveZone>();
            da.GetDataList(index, goos);
            foreach (var g in goos)
            {
                object v = g is GH_ObjectWrapper w ? w.Value : g?.ScriptVariable();
                if (v is WeaveZone z) res.Add(z);
                else if (v is WeaveZoneSet s) res.AddRange(s.Zones);
            }
            return res;
        }

        public static T Pick<T>(List<T> list, int i, T fallback) => list == null || list.Count == 0 ? fallback : list[Math.Min(i, list.Count - 1)];

        public static double[] Numbers(string text)
        {
            var r = new List<double>();
            foreach (var s in (text ?? "").Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) r.Add(v);
            return r.ToArray();
        }
    }

    // ============================================================== Weave Zones
    /// <summary>Finds the places where two beads of a layer lie side by side.</summary>
    public class WeaveZonesComponent : SlicerComponent
    {
        public WeaveZonesComponent()
            : base("Weave Zones", "Zones",
                  "Finds the places where two beads of a layer lie side by side: two walls, infill against a wall, the two lines of a rib, knots where two strips nearly touch. Every place is followed through the layers (one zone per place). How near is near can be set in fractions of the bead width. Places can be given by hand: Include and Exclude. Use it after Continuous Path (or Round Corners) and before Start and End; then Weave.") { }

        public override Guid ComponentGuid => new Guid("B6F846FB-2357-4A74-9187-B5739C1AD197");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWeaveZonesIcon.png", this);
        protected override bool CanRunInBackground => true;
        private IGH_DataAccess _da; // of the running solution: trees are handed out through it

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path from Continuous Path (any toolpath works)", GH_ParamAccess.item);
            p.AddGeometryParameter("Include", "In", "Optional. Places set by hand: closed curves (top view, all layers), open curves and points (with Radius), Breps or meshes (only the layers inside). Inside, beads further apart are taken too", GH_ParamAccess.list);
            p.AddGeometryParameter("Exclude", "Ex", "Optional. Never here: the same kinds of geometry", GH_ParamAccess.list);
            p.AddIntegerParameter("Include Mode", "Mo", "0 = only inside the Include places\n1 = everything that is found, and the Include places", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Radius", "Ra", "Around points, open curves and open surfaces, mm", GH_ParamAccess.item, 15.0);
            p.AddNumberParameter("Closer", "Cl", "How much nearer than one bead width, as a fraction: 0.85 = the centre lines may be as near as 0.15 bead widths (pressed into each other; Weave can fit the widths there). 0.35 leaves such places out", GH_ParamAccess.item, 0.85);
            p.AddNumberParameter("Touch", "To", "Beads that are one bead width apart, plus or minus this fraction, count as touching; nearer is fused, further is almost touching", GH_ParamAccess.item, 0.08);
            p.AddNumberParameter("Further", "Fu", "How much further than one bead width, as a fraction: 0.25 = up to 1.25 bead widths between the centre lines", GH_ParamAccess.item, 0.25);
            p.AddNumberParameter("Min Length", "Lm", "Shortest stretch in mm. 0 = 1.5 bead widths", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Min Layers", "Ln", "A zone must go through at least this many layers", GH_ParamAccess.item, 2);
            p.AddNumberParameter("Max Angle", "An", "Lines count as parallel up to this angle, degrees", GH_ParamAccess.item, 25.0);
            p.AddBooleanParameter("Bottom", "Bo", "True = also the lines of solid bottom and top fill", GH_ParamAccess.item, false);
            p[1].Optional = true; p[2].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 3, "Include Mode", "Only inside Include", "Found places and Include");
            AddSlider(doc, 5, "Closer", 0m, 0.9m, 0.85m, 2, "", new double[] { 0.15, 0.35, 0.5, 0.85 });
            AddSlider(doc, 7, "Further", 0m, 0.6m, 0.25m, 2, "", new double[] { 0, 0.1, 0.25, 0.4 });
            AddSlider(doc, 8, "Min Length", 0m, 100m, 0m, 0, "mm");
            AddToggle(doc, 11, "Bottom", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Zones", "Z", "The zones, the longest first. Pick from them with List Item, Cull or Dispatch", GH_ParamAccess.list);
            p.AddCurveParameter("Seams", "S", "The seam between the two beads, one branch per zone, one curve per layer", GH_ParamAccess.tree);
            p.AddPointParameter("Centres", "C", "Middle of every zone (with Text Tag: the ids)", GH_ParamAccess.list);
            p.AddIntegerParameter("Ids", "Id", "Id of every zone", GH_ParamAccess.list);
            p.AddIntegerParameter("Class", "Cl", "0 = fused, 1 = touching, 2 = almost touching, 3 = pressed into each other", GH_ParamAccess.list);
            p.AddNumberParameter("Length", "L", "Mean length of the seam per layer, mm", GH_ParamAccess.list);
            p.AddNumberParameter("Gap", "G", "Mean distance of the two centre lines, mm", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Verdict first, then the zones", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            _da = da;
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Toolpath output of Continuous Path."); return; }
            var inc = new List<IGH_GeometricGoo>(); var exc = new List<IGH_GeometricGoo>(); da.GetDataList(1, inc); da.GetDataList(2, exc);
            int mode = 0, ml = 2; double radius = 15, cl = 0.85, to = 0.08, fu = 0.25, lm = 0, an = 25; bool bottom = false;
            da.GetData(3, ref mode); da.GetData(4, ref radius); da.GetData(5, ref cl); da.GetData(6, ref to); da.GetData(7, ref fu); da.GetData(8, ref lm); da.GetData(9, ref ml); da.GetData(10, ref an); da.GetData(11, ref bottom);
            var log = new List<string>();
            var o = new WeaveFindOptions
            {
                Include = WeaveIO.Regions(inc, radius, log), Exclude = WeaveIO.Regions(exc, radius, log), OnlyIncluded = mode == 0,
                Closer = Math.Max(0, Math.Min(0.95, cl)), Touch = Math.Max(0, to), Further = Math.Max(0, Math.Min(1, fu)), MinLength = lm, MinLayers = ml, MaxAngle = an, Bottom = bottom
            };
            Async.Run(da, job =>
            {
                foreach (var l in log) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
                var set = WeaveFinder.Find(tp, o);
                if (set.Zones.Count == 0) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, set.Verdict + " Try a larger Further, or a shorter Min Length.");
                int pressed = set.Zones.Count(z => z.Class == WeaveClass.Pressed);
                if (pressed > 0) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{pressed} zone(s) are pressed into each other: too much material there. Weave with Fit makes the beads narrower at these places.");
                job.Message = set.Zones.Count + " zones";
                var tree = new DataTree<object>();
                foreach (var z in set.Zones)
                    foreach (var r in z.Runs.OrderBy(q => q.Layer)) tree.Add(new PolylineCurve(Thin(r.Mid)), new GH_Path(z.Id));
                job.SetDataList(0, set.Zones.Select(z => new GH_ObjectWrapper(z)));
                job.OnApply(() => _da.SetDataTree(1, tree));
                job.SetDataList(2, set.Zones.Select(z => z.Centre));
                job.SetDataList(3, set.Zones.Select(z => z.Id));
                job.SetDataList(4, set.Zones.Select(z => (int)z.Class));
                job.SetDataList(5, set.Zones.Select(z => z.MeanLength));
                job.SetDataList(6, set.Zones.Select(z => z.GapMean));
                job.SetData(7, string.Join("\n", set.Report));
            });
        }

        private static IEnumerable<Point3d> Thin(Point3d[] p)
        {
            for (int i = 0; i < p.Length; i++) if (i % 3 == 0 || i == p.Length - 1) yield return p[i];
        }
    }

    // ============================================================== Weave
    /// <summary>Interlocks the beads of neighbouring walls from layer to layer.</summary>
    public class WeaveComponent : SlicerComponent
    {
        public WeaveComponent()
            : base("Weave", "Weave",
                  "Interlocks two beads that lie side by side. Instead of only touching along a line, the seam between them moves from layer to layer, so the layers grip into each other: Shift (left, right, left ...), Width (the outer faces stay, one bead wide and one narrow), Snake (waves that cross from layer to layer, like a braid seen from above), Teeth (straight, with a small tooth every few centimetres) or your own list. The two beads keep their distance: no gap, no extra material. With Fit the bead width also follows the distance, so beads that nearly touch fuse and beads that were pressed together do not over-extrude. The path stays one line. Per zone settings: give lists (item 0 for zone 0 ...). Afterwards: Round Corners if you use it, then Start and End.") { }

        public override Guid ComponentGuid => new Guid("13F04509-8FD3-4417-8918-DCD52E511C41");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWeaveIcon.png", this);
        protected override bool CanRunInBackground => true;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path (the one Weave Zones was given, or a later state of it)", GH_ParamAccess.item);
            p.AddGenericParameter("Zones", "Z", "Optional. Zones from Weave Zones. Empty = find them with the default tolerances and weave all", GH_ParamAccess.list);
            p.AddIntegerParameter("Pattern", "P", "0 = Shift: the seam lies left and right, layer by layer\n1 = Width: the outer faces stay, beads wide and narrow in turn\n2 = Snake: a wave, moved on from layer to layer\n3 = Teeth: straight, a small tooth every Wavelength\n4 = Custom: the list Offsets\n5 = Plain: no pattern, only fit the widths\nA list: one per zone", GH_ParamAccess.list, 0);
            p.AddNumberParameter("Amplitude", "A", "How far the seam leaves the middle, mm. 0 = a quarter of the bead width. A list: one per zone", GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Wavelength", "L", "Snake: length of one wave; Teeth: distance of the teeth, mm. 0 = automatic (shortest printable wave x 1.2; teeth 50 mm). A list: one per zone", GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Phase", "Ph", "Snake, Teeth: how far the pattern moves on per layer, as a fraction of the wave. 0.5 = woven, 0.25 = helix, 0 = stacked. -1 = automatic (Snake 0.5, Teeth 0). A list: one per zone", GH_ParamAccess.list, -1.0);
            p.AddIntegerParameter("Faces", "F", "The faces that look away from the seam:\n0 = Auto: walls keep their face, infill moves\n1 = Both beads move\n2 = Both keep their face (the bead width changes)\nA list: one per zone", GH_ParamAccess.list, 0);
            p.AddNumberParameter("Extent", "E", "Woven length per zone, mm (150 = 15 cm, 5 = a tiny spot). 0 = the whole zone. A list: one per zone", GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Position", "Po", "Where the woven length lies in the zone: 0 = start, 0.5 = middle, 1 = end. A list: one per zone", GH_ParamAccess.list, 0.5);
            p.AddNumberParameter("Repeat", "Re", "The woven length again every ... mm. 0 = once", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Fade", "Fa", "Length over which the pattern grows from nothing at its ends, mm. 0 = as short as the bead allows", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Tooth Length", "Tl", "Teeth: length of one tooth, mm. 0 = the shortest printable one x 1.2", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Layer Period", "Lp", "Layers with the same row before the pattern moves on", GH_ParamAccess.item, 1);
            p.AddTextParameter("Offsets", "Of", "Custom: the offset per layer as a fraction of the amplitude, e.g. 0,1,0,-1. A list: one text per zone", GH_ParamAccess.list, "0,1,0,-1");
            p.AddBooleanParameter("Fit", "Fi", "True = the bead width follows the distance of the two beads, so they overlap as planned everywhere (wider over a gap, narrower where they are pressed together)", GH_ParamAccess.item, true);
            p.AddNumberParameter("Step Limit", "Sl", "Largest sideways step of a bead from layer to layer, in bead widths (0.5 = every bead rests on the one below)", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Width Min", "W0", "Narrowest bead, as a fraction of the bead width", GH_ParamAccess.item, 0.7);
            p.AddNumberParameter("Width Max", "W1", "Widest bead, as a fraction of the bead width", GH_ParamAccess.item, 1.4);
            p.AddGeometryParameter("Place", "Pl", "Optional. Weave only inside these places: closed curves, curves or points (15 mm around), Breps or meshes", GH_ParamAccess.list);
            p.AddBooleanParameter("Check", "Ck", "True = measure the path before and after (raster of the beads, crossings, bends, steps, contact areas)", GH_ParamAccess.item, true);
            p[1].Optional = true; p[18].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Pattern", Weave.PatternNames);
            AddSlider(doc, 3, "Amplitude", 0m, 10m, 0m, 2, "mm");
            AddSlider(doc, 4, "Wavelength", 0m, 200m, 0m, 1, "mm");
            AddSlider(doc, 5, "Phase", -1m, 1m, -1m, 2, "", new double[] { -1, 0, 0.25, 0.5 });
            AddValueList(doc, 6, "Faces", "Auto (walls keep their face)", "Both beads move", "Both keep their face");
            AddSlider(doc, 7, "Extent", 0m, 1000m, 0m, 0, "mm");
            AddToggle(doc, 14, "Fit", true);
            AddToggle(doc, 19, "Check", true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The woven path", GH_ParamAccess.item);
            p.AddCurveParameter("Preview", "Pv", "One polyline per layer", GH_ParamAccess.list);
            p.AddGenericParameter("Zones", "Z", "The zones that were used (for Joint Data and Weave Variants)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Verdict first, then what was done, then the numbers before and after", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Toolpath output of Continuous Path."); return; }
            var zones = WeaveIO.Zones(da, 1);
            var pat = new List<int>(); var amp = new List<double>(); var len = new List<double>(); var pha = new List<double>(); var fac = new List<int>(); var ext = new List<double>(); var pos = new List<double>(); var off = new List<string>();
            da.GetDataList(2, pat); da.GetDataList(3, amp); da.GetDataList(4, len); da.GetDataList(5, pha); da.GetDataList(6, fac); da.GetDataList(7, ext); da.GetDataList(8, pos); da.GetDataList(13, off);
            double rep = 0, fade = 0, tooth = 0, sl = 0.5, w0 = 0.7, w1 = 1.4; int lp = 1; bool fit = true, check = true;
            da.GetData(9, ref rep); da.GetData(10, ref fade); da.GetData(11, ref tooth); da.GetData(12, ref lp); da.GetData(14, ref fit); da.GetData(15, ref sl); da.GetData(16, ref w0); da.GetData(17, ref w1); da.GetData(19, ref check);
            var place = new List<IGH_GeometricGoo>(); da.GetDataList(18, place);
            var log = new List<string>();
            var regions = WeaveIO.Regions(place, 15, log);
            bool given = Params.Input[1].SourceCount > 0;
            if (given && zones.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No zones arrive at Zones: nothing is woven."); da.SetData(0, new GH_Toolpath(tp)); return; }
            Func<int, WeaveSettings> make = i =>
            {
                var s = new WeaveSettings
                {
                    Pattern = (WeavePattern)Math.Max(0, Math.Min(5, WeaveIO.Pick(pat, i, 0))), Amplitude = Math.Max(0, WeaveIO.Pick(amp, i, 0.0)), Wavelength = Math.Max(0, WeaveIO.Pick(len, i, 0.0)), Phase = WeaveIO.Pick(pha, i, -1.0),
                    Faces = (WeaveFaces)Math.Max(0, Math.Min(2, WeaveIO.Pick(fac, i, 0))), Extent = Math.Max(0, WeaveIO.Pick(ext, i, 0.0)), Position = WeaveIO.Pick(pos, i, 0.5), Repeat = Math.Max(0, rep), Fade = Math.Max(0, fade),
                    ToothLength = Math.Max(0, tooth), LayerPeriod = Math.Max(1, lp), Fit = fit, StepLimit = Math.Max(0.05, sl), WidthMin = Math.Max(0.3, Math.Min(1, w0)), WidthMax = Math.Max(1, Math.Min(2.5, w1))
                };
                var o = WeaveIO.Numbers(WeaveIO.Pick(off, i, "0,1,0,-1")); if (o.Length > 0) s.Offsets = o;
                return s;
            };

            Async.Run(da, job =>
            {
                foreach (var l in log) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
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
                    if (rules.Count == 0) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The zones do not fit this toolpath: nothing is woven.");
                }
                else
                {
                    set = WeaveFinder.Find(tp, new WeaveFindOptions());
                    for (int i = 0; i < set.Zones.Count; i++) rules.Add(new WeaveRule { Settings = make(i), Zones = new HashSet<int> { i }, Regions = regions });
                }
                var r = Weave.Apply(tp, set, rules);
                foreach (var l in r.Warnings.Distinct()) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
                var text = new List<string>(r.Report);
                if (check && r.WovenRuns > 0)
                {
                    Cancel.Check();
                    var c = WeaveCheck.Compare(tp, r.Path, set);
                    text.Insert(1, c.Verdict); text.Add(""); text.AddRange(c.Table.Skip(1));
                    if (!c.Ok) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, c.Verdict);
                }
                if (r.WovenRuns == 0) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Verdict);
                job.Message = r.WovenZones + " of " + set.Zones.Count + " zones";
                var used = new HashSet<int>(rules.SelectMany(q => q.Zones));
                job.SetData(0, new GH_Toolpath(r.Path));
                job.SetDataList(1, Layers(r.Path));
                job.SetDataList(2, set.Zones.Where(z => used.Contains(z.Id)).Select(z => new GH_ObjectWrapper(z)));
                job.SetData(3, string.Join("\n", text));
            });
        }

        internal static List<PolylineCurve> Layers(Toolpath tp)
        {
            var res = new List<PolylineCurve>(); var cur = new List<Point3d>();
            for (int i = 1; i < tp.Count; i++)
            {
                bool print = tp.Flows[i] > 0;
                if (print && cur.Count == 0) cur.Add(tp.Points[i - 1]);
                if (print) cur.Add(tp.Points[i]);
                if (cur.Count > 1 && (!print || i + 1 >= tp.Count || tp.Layers[i + 1] != tp.Layers[i])) { res.Add(new PolylineCurve(cur)); cur = new List<Point3d>(); }
            }
            return res;
        }
    }

    // ============================================================== Weave Variants
    /// <summary>The same part woven in different ways, in one table.</summary>
    public class WeaveVariantsComponent : SlicerComponent
    {
        public WeaveVariantsComponent()
            : base("Weave Variants", "WeaveVar",
                  "Weaves the same zones in fourteen ways (as sliced, fit only, shift, width, snake, helix, teeth, list ...) and measures every one the same way: contact area beside and between the layers, over-extrusion, gaps, crossings, bends, support, length, material, time. One table to compare them, and every path to look at or print.") { }

        public override Guid ComponentGuid => new Guid("357FD45C-04ED-461B-B450-ABF8B5FAE6CB");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWeaveVariantsIcon.png", this);
        protected override bool CanRunInBackground => true;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path before weaving", GH_ParamAccess.item);
            p.AddGenericParameter("Zones", "Z", "Optional. Zones from Weave Zones (its whole search is used)", GH_ParamAccess.list);
            p.AddNumberParameter("Amplitude", "A", "Amplitude of the variants, mm. 0 = a quarter of the bead width", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Layers", "N", "Layers that are drawn as a raster (the contact areas use all layers)", GH_ParamAccess.item, 12);
            p.AddTextParameter("File", "File", "Optional. File or folder for the CSV. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the CSV", GH_ParamAccess.item, false);
            p[1].Optional = true; p[4].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 2, "Amplitude", 0m, 10m, 0m, 2, "mm");
            AddCount(doc, 3, "Layers", 2, 60, 12);
            AddButton(doc, 5, "Save");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Table", "Tab", "The comparison (use a Panel with a fixed-width font)", GH_ParamAccess.list);
            p.AddGenericParameter("Toolpaths", "T", "The path of every variant", GH_ParamAccess.list);
            p.AddTextParameter("Names", "N", "Name of every variant", GH_ParamAccess.list);
            p.AddTextParameter("CSV", "CSV", "The table as CSV", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Verdict", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            var zones = WeaveIO.Zones(da, 1); double a = 0; int n = 12; string file = null; bool save = false;
            da.GetData(2, ref a); da.GetData(3, ref n); da.GetData(4, ref file); da.GetData(5, ref save);
            if (save) file = FreePath(file, OutputFile.Name("VAR", "Weave Variants", null, ".csv"));
            Async.Run(da, job =>
            {
                var set = zones.Count > 0 && ReferenceEquals(zones[0].Set.Path, tp) ? zones[0].Set : WeaveFinder.Find(tp, zones.Count > 0 ? zones[0].Set.Options : new WeaveFindOptions());
                if (set.Zones.Count == 0) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, set.Verdict); return; }
                var v = WeaveVariants.Run(tp, set, WeaveVariants.Standard(tp.Settings.BeadWidth, a), n, new JointMaterial());
                var csv = WeaveVariants.Csv(v);
                if (save)
                {
                    try { File.WriteAllLines(file, csv); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + file); }
                    catch (Exception ex) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
                }
                var best = v.Where(q => q.Numbers.Crossings <= v[0].Numbers.Crossings && q.Numbers.StepOver <= v[0].Numbers.StepOver + 0.3).OrderByDescending(q => q.Joint.HorizontalShare).First();
                job.Message = v.Count + " variants";
                job.SetDataList(0, WeaveVariants.Table(v));
                job.SetDataList(1, v.Select(q => new GH_Toolpath(q.Result.Path)));
                job.SetDataList(2, v.Select(q => q.Name));
                job.SetDataList(3, csv);
                job.SetData(4, Inv.F($"Most interlock without new crossings or overhang: {best.Name} ({best.Joint.HorizontalShare:0.00} mm2 per mm2 of joint face, {v[0].Joint.HorizontalShare:0.00} as sliced). Which one is stronger only a test tells."));
            });
        }
    }

    // ============================================================== Weave Test
    /// <summary>Specimens for tests of the joint.</summary>
    public class WeaveTestComponent : SlicerComponent
    {
        public WeaveTestComponent()
            : base("Weave Test", "WeaveTest",
                  "A test series for woven joints: pieces of double wall, plain and woven in different ways, with different amplitudes and gaps. All pieces are printed as one line (two beads side by side in rows), in random order, and sawn apart afterwards. The protocol is a CSV table with a line per specimen and empty fields for the results. Suggested tests: shear along the joint, tension across, bending; five of each.") { }

        public override Guid ComponentGuid => new Guid("C46D5014-69F2-4D11-8DFC-C899041E7EF0");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWeaveTestIcon.png", this);
        protected override bool CanRunInBackground => true;
        private IGH_DataAccess _da; // of the running solution: trees are handed out through it

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddNumberParameter("Length", "L", "Length of a specimen, mm", GH_ParamAccess.item, 80.0);
            p.AddNumberParameter("Height", "H", "Height of a specimen, mm", GH_ParamAccess.item, 60.0);
            p.AddNumberParameter("Waste", "W", "Waste between two specimens (saw, change of the gap), mm", GH_ParamAccess.item, 25.0);
            p.AddNumberParameter("Bed X", "X", "Print bed in X, mm", GH_ParamAccess.item, 1000.0);
            p.AddNumberParameter("Bed Y", "Y", "Print bed in Y, mm", GH_ParamAccess.item, 700.0);
            p.AddIntegerParameter("Replicates", "N", "Specimens per variant and test", GH_ParamAccess.item, 5);
            p.AddBooleanParameter("Shear", "Sh", "Specimens for shear along the joint", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Tension", "Te", "Specimens for tension across the joint (peel)", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Bending", "Be", "Specimens for bending", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Amplitudes", "Am", "Also smaller amplitudes of Shift", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Gaps", "Ga", "Also larger gaps, plain and woven", GH_ParamAccess.item, true);
            p.AddIntegerParameter("Seed", "Se", "Another number gives another random order", GH_ParamAccess.item, 1);
            p.AddTextParameter("File", "File", "Optional. File or folder for the protocol. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the protocol (CSV)", GH_ParamAccess.item, false);
            p[13].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 1, "Length", 30m, 300m, 80m, 0, "mm");
            AddSlider(doc, 2, "Height", 10m, 300m, 60m, 0, "mm");
            AddCount(doc, 6, "Replicates", 1, 10, 5);
            AddToggle(doc, 7, "Shear", true); AddToggle(doc, 8, "Tension", true); AddToggle(doc, 9, "Bending", true);
            AddToggle(doc, 10, "Amplitudes", true); AddToggle(doc, 11, "Gaps", true);
            AddButton(doc, 14, "Save");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Beds", "T", "One path per bed (to Start and End and the output)", GH_ParamAccess.list);
            p.AddGenericParameter("Specimens", "Sp", "One path per variant, alone", GH_ParamAccess.list);
            p.AddTextParameter("Variants", "V", "Names of the variants", GH_ParamAccess.list);
            p.AddCurveParameter("Marks", "M", "Rectangles of the specimens on the bed, one branch per bed (saw here)", GH_ParamAccess.tree);
            p.AddTextParameter("Ids", "Id", "Id of every specimen, in the order of the marks", GH_ParamAccess.tree);
            p.AddTextParameter("Protocol", "CSV", "The protocol: one line per specimen, with the numbers of the joint and empty fields for the results", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Verdict, layout and the tests", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            _da = da;
            if (Async.Deliver(da)) return;
            var st = SlicerIO.Get<PrintSettings>(da, 0) ?? new PrintSettings();
            var o = new WeaveTestOptions(); string file = null; bool save = false;
            da.GetData(1, ref o.Length); da.GetData(2, ref o.Height); da.GetData(3, ref o.Waste); da.GetData(4, ref o.BedX); da.GetData(5, ref o.BedY); da.GetData(6, ref o.Replicates);
            da.GetData(7, ref o.Shear); da.GetData(8, ref o.Peel); da.GetData(9, ref o.Bending); da.GetData(10, ref o.Amplitudes); da.GetData(11, ref o.Gaps); da.GetData(12, ref o.Seed); da.GetData(13, ref file); da.GetData(14, ref save);
            if (!o.Shear && !o.Peel && !o.Bending) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Switch on at least one test."); return; }
            if (o.Length < 4 * st.BeadWidth) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The specimens are shorter than four bead widths."); return; }
            o.Waste = Math.Max(o.Waste, 3 * st.BeadWidth);
            if (o.BedX < 2 * o.Margin + o.Length + 3 * o.Waste + 20 * st.BeadWidth) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The bed is too short for one specimen in a row."); return; }
            if (save) file = FreePath(file, OutputFile.Name("TEST", "Weave Test", null, ".csv"));
            Async.Run(da, job =>
            {
                var t = WeaveCoupons.Make(st, o);
                var csv = t.Csv(st);
                if (save)
                {
                    try { File.WriteAllLines(file, csv); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + file); }
                    catch (Exception ex) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
                }
                var marks = new DataTree<object>(); var ids = new DataTree<object>();
                for (int i = 0; i < t.Coupons.Count; i++)
                {
                    var c = t.Coupons[i]; double h = 0.5 * c.Gap + st.BeadWidth, x0 = Math.Min(c.From.X, c.To.X), x1 = Math.Max(c.From.X, c.To.X);
                    marks.Add(new PolylineCurve(new[] { new Point3d(x0, c.From.Y - h, 0), new Point3d(x1, c.From.Y - h, 0), new Point3d(x1, c.From.Y + h, 0), new Point3d(x0, c.From.Y + h, 0), new Point3d(x0, c.From.Y - h, 0) }), new GH_Path(t.BedOf[i]));
                    ids.Add(c.Id + " " + c.Variant, new GH_Path(t.BedOf[i]));
                }
                var variants = t.Coupons.GroupBy(c => c.Variant).Select(g => g.First()).OrderBy(c => c.Id).ToList();
                job.Message = t.Coupons.Count + " specimens";
                job.SetDataList(0, t.Beds.Select(b => new GH_Toolpath(b)));
                job.SetDataList(1, variants.Select(c => new GH_Toolpath(c.Path)));
                job.SetDataList(2, variants.Select(c => c.Variant));
                job.OnApply(() => { _da.SetDataTree(3, marks); _da.SetDataTree(4, ids); });
                job.SetDataList(5, csv);
                job.SetData(6, string.Join("\n", t.Report));
            });
        }
    }

    // ============================================================== Joint Data
    /// <summary>The joints as data for the analysis (IGA) and for experiments.</summary>
    public class JointDataComponent : SlicerComponent
    {
        public JointDataComponent()
            : base("Joint Data", "Joints",
                  "The joints of a path as data, for the analysis and for tests. The surfaces of the analysis stay smooth: a joint is a coupling between two patches (or a patch link) with a stiffness and a strength. Per zone: the seam on the mid surfaces, length, gap, contact area beside and between the layers per metre, interlock depth, and an estimate of stiffness and strength; per point pair the area it stands for and the penalty alpha; per height band the patch link with the thickness it needs. All material values are placeholders until they are measured with Weave Test.") { }

        public override Guid ComponentGuid => new Guid("ABE0E84E-70CB-42E8-9983-60A516468AD3");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerJointIcon.png", this);
        protected override bool CanRunInBackground => true;
        private IGH_DataAccess _da; // of the running solution: trees are handed out through it

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The woven path (or any path, to compare)", GH_ParamAccess.item);
            p.AddGenericParameter("Zones", "Z", "Zones from Weave (or Weave Zones)", GH_ParamAccess.list);
            p.AddNumberParameter("Pair Spacing", "D", "Distance of the point pairs along the seam and up, mm", GH_ParamAccess.item, 10.0);
            p.AddNumberParameter("E Across", "Ea", "Young's modulus across the beads in the layer, MPa. Placeholder", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("E Layers", "El", "Young's modulus across the layers, MPa. Placeholder", GH_ParamAccess.item, 1800.0);
            p.AddNumberParameter("G In Layer", "G", "Shear modulus in the layer, MPa. Placeholder", GH_ParamAccess.item, 1100.0);
            p.AddNumberParameter("G Layers", "Gl", "Shear modulus across the layers, MPa. Placeholder", GH_ParamAccess.item, 700.0);
            p.AddNumberParameter("Side Tension", "Ss", "Tensile strength of the weld of two beads side by side, MPa. Placeholder", GH_ParamAccess.item, 20.0);
            p.AddNumberParameter("Side Shear", "Ts", "Shear strength of the weld of two beads side by side, MPa. Placeholder", GH_ParamAccess.item, 12.0);
            p.AddNumberParameter("Layer Tension", "Sl", "Tensile strength between layers, MPa. Placeholder", GH_ParamAccess.item, 18.0);
            p.AddNumberParameter("Layer Shear", "Tl", "Shear strength between layers, MPa. Placeholder", GH_ParamAccess.item, 12.0);
            p.AddNumberParameter("Shell E", "E", "Modulus of the shells in the analysis, MPa (for alpha and the link thickness)", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Shell Thickness", "t", "Thickness of the shells, mm. 0 = bead width", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Element Size", "h", "Size of the elements in the analysis, mm (for alpha)", GH_ParamAccess.item, 10.0);
            p.AddTransformParameter("Move Back", "X", "Optional. Transform from the print bed to the model of the analysis (a part that is printed lying)", GH_ParamAccess.item);
            p.AddTextParameter("Name", "N", "Name in the files", GH_ParamAccess.item, "Joints");
            p.AddTextParameter("Folder", "Dir", "Optional. Folder. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write JSON and the three CSV tables", GH_ParamAccess.item, false);
            p[1].Optional = false; p[14].Optional = true; p[16].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 2, "Pair Spacing", 2m, 100m, 10m, 0, "mm");
            AddButton(doc, 17, "Save");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points A", "A", "Points on the mid surface of the first wall, one branch per zone", GH_ParamAccess.tree);
            p.AddPointParameter("Points B", "B", "The points opposite, on the second wall", GH_ParamAccess.tree);
            p.AddLineParameter("Links", "L", "Patch links: the shortest line across the joint per height band, one branch per zone", GH_ParamAccess.tree);
            p.AddNumberParameter("Link Thickness", "tL", "Fusion thickness of every link (opening stiffness), mm", GH_ParamAccess.tree);
            p.AddNumberParameter("Interlock", "Il", "Interlock contact per joint face, per zone (mm2 per mm2)", GH_ParamAccess.list);
            p.AddNumberParameter("Side Contact", "Sc", "Contact of the beads side by side per joint face, per zone", GH_ParamAccess.list);
            p.AddTextParameter("Zones CSV", "CSV", "One line per zone", GH_ParamAccess.list);
            p.AddTextParameter("JSON", "JSON", "Everything as JSON", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Verdict, and how it maps to the couplings of a shell analysis", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            _da = da;
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            var zones = WeaveIO.Zones(da, 1);
            if (tp == null || tp.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            if (zones.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the zones of Weave or Weave Zones."); return; }
            var o = new JointDataOptions(); var m = o.Material; var x = Transform.Identity; string dir = null; bool save = false;
            da.GetData(2, ref o.PairSpacing); da.GetData(3, ref m.EAcross); da.GetData(4, ref m.ELayers); da.GetData(5, ref m.G); da.GetData(6, ref m.GLayers); da.GetData(7, ref m.SigmaSide); da.GetData(8, ref m.TauSide); da.GetData(9, ref m.SigmaLayer); da.GetData(10, ref m.TauLayer);
            da.GetData(11, ref o.E); da.GetData(12, ref o.Thickness); da.GetData(13, ref o.ElementSize); bool moved = da.GetData(14, ref x); da.GetData(15, ref o.Name); da.GetData(16, ref dir); da.GetData(17, ref save);
            o.PairSpacing = Math.Max(1, o.PairSpacing);
            var d0 = new JointMaterial();
            m.Placeholder = m.EAcross == d0.EAcross && m.G == d0.G && m.SigmaSide == d0.SigmaSide && m.TauSide == d0.TauSide && m.TauLayer == d0.TauLayer;
            string json = null, zc = null, pc = null, lc = null;
            if (save)
            {
                json = FreePath(dir, OutputFile.Name("JOINT", o.Name, null, ".json"));
                string stem = Path.Combine(Path.GetDirectoryName(json) ?? "", Path.GetFileNameWithoutExtension(json));
                zc = stem + "_zones.csv"; pc = stem + "_pairs.csv"; lc = stem + "_links.csv";
            }
            Async.Run(da, job =>
            {
                var set = zones[0].Set;
                var d = JointData.Compute(tp, set, o, moved ? (Func<Point3d, Point3d>)(q => { q.Transform(x); return q; }) : null);
                var want = new HashSet<int>(zones.Select(z => z.Id));
                d.Keep(want);
                if (m.Placeholder) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "All material values are placeholders: stiffness and strength are estimates. Measure them with Weave Test.");
                var j = WeaveCheck.Sum(d.Joints);
                string text = d.Json();
                if (save)
                {
                    try { File.WriteAllText(json, text); File.WriteAllLines(zc, d.ZoneCsv()); File.WriteAllLines(pc, d.PairCsv()); File.WriteAllLines(lc, d.LinkCsv()); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + json + " and three tables"); }
                    catch (Exception ex) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the files: " + ex.Message); }
                }
                var a = new DataTree<object>(); var b = new DataTree<object>(); var l = new DataTree<object>(); var t = new DataTree<object>();
                foreach (var p in d.Pairs) { a.Add(p.A, new GH_Path(p.Zone)); b.Add(p.B, new GH_Path(p.Zone)); }
                foreach (var k in d.Links) { l.Add(new Line(k.A, k.B), new GH_Path(k.Zone)); t.Add(k.TOpen, new GH_Path(k.Zone)); }
                job.Message = d.Joints.Count + " joints";
                job.OnApply(() => { _da.SetDataTree(0, a); _da.SetDataTree(1, b); _da.SetDataTree(2, l); _da.SetDataTree(3, t); });
                job.SetDataList(4, d.Joints.Select(q => q.HorizontalShare)); job.SetDataList(5, d.Joints.Select(q => q.VerticalShare));
                job.SetDataList(6, d.ZoneCsv()); job.SetData(7, text);
                job.SetData(8, Inv.F($"{d.Joints.Count} joint(s), {j.Face / 100:0.0} cm2 of joint face: side contact {j.VerticalShare:0.00}, interlock {j.HorizontalShare:0.00} mm2 per mm2 (depth {j.Depth:0.00} mm). {d.Pairs.Count} point pairs, {d.Links.Count} links.\n\n") + JointData.Rule.Replace(". ", ".\n"));
            });
        }
    }
}
