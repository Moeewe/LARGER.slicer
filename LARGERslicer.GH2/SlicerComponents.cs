using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>All layers of a part, handed from component to component as one item.</summary>
    public sealed class LayerStack
    {
        public List<SliceLayer> Layers = new List<SliceLayer>();
        public LayerStack() { }
        public LayerStack(IEnumerable<SliceLayer> layers) { Layers = layers.OrderBy(l => l.Index).ToList(); }
        public override string ToString() => Layers.Count + " layers";
    }

    /// <summary>
    /// The groups of the Slicer section, in the order of the Grasshopper 1 tab (its exposure groups):
    /// a slot is a group of its own in the section, as an exposure is in Grasshopper 1.
    /// </summary>
    internal static class Group
    {
        public const int Start = 0;      // settings, machine, material, Easy Slicer, Quick Start
        public const int Layers = 1;     // Slice and the self-support tools
        public const int Walls = 2;      // Walls & fill: walls, bottom, infill, support fins
        public const int Path = 3;       // the one continuous line, corners, start and end, patterns
        public const int Check = 4;      // checks and the values for the machine
        public const int Output = 5;     // files and simulation
        public const int Curves = 6;     // curve tools: own curves, imported G-code, analysis
    }

    internal static partial class Io
    {
        public const string Chapter = "LARGER";
        public const string Slicer = "Slicer";
        public const string Dxr = "DXR";
        public const string Robot = "Robot";
        public const string Utilities = "Utilities";
        public const string Cnc = "CNC";

        public static Nomen Name(string name, string info, string section, int slot) => new Nomen(name, info, Chapter, section, slot, Rank.Normal);

        /// <summary>Loads old files, but is not offered on the tab: replaced by the components of Grasshopper 1.</summary>
        public static Nomen HiddenName(string name, string info, string section, int slot) => new Nomen(name, info, Chapter, section, slot, Rank.Hidden);

        public static T Get<T>(IDataAccess access, int index) where T : class
        {
            object o;
            return access.GetItem(index, out o) ? o as T : null;
        }

        public static PrintSettings Settings(IDataAccess access, int index) => Get<PrintSettings>(access, index) ?? new PrintSettings();

        public static List<SliceLayer> Layers(IDataAccess access, int index)
        {
            var stack = Get<LayerStack>(access, index);
            if (stack == null) access.AddError("No layers", "Connect the Layers output of a slicer component.");
            return stack?.Layers;
        }

        public static Toolpath Path(IDataAccess access, int index)
        {
            var tp = Get<Toolpath>(access, index);
            if (tp == null || tp.Count < 2) { access.AddError("No toolpath", "Connect the Toolpath output of Continuous Path."); return null; }
            return tp;
        }

        public static void Contours(IDataAccess access, int index, IEnumerable<SliceLayer> layers)
            => access.SetTwig(index, layers.SelectMany(l => l.RealContours()).ToArray(), null, null);

        /// <summary>Grasshopper 2 hands over boxes, spheres and other primitives as plain values: make them Breps.</summary>
        public static object Solid(object o)
        {
            if (o is Box bx) return bx.ToBrep();
            if (o is BoundingBox bb) return bb.ToBrep();
            if (o is Sphere sp) return sp.ToBrep();
            if (o is Cylinder cy) return cy.ToBrep(true, true);
            if (o is Cone co) return co.ToBrep(true);
            if (o is Torus to) return to.ToRevSurface().ToBrep();
            if (o is BrepFace face) return face.DuplicateFace(false);
            return o;
        }

        public static void Remarks(IDataAccess access, IEnumerable<string> log)
        {
            foreach (var l in log) access.AddRemark(l.Length > 60 ? l.Substring(0, 60) + "…" : l, l);
        }
    }

    // ============================================================== design rules (CEAD and Ginger sheets)
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1010")]
    public sealed class DesignRulesComponent : Component
    {
        public DesignRulesComponent() : base(Io.Name("Design Rules", "Before you model: layer height range, overlap, how thick to model a wall of two beads (CO90, and CO45 at 45 degrees), the layer width for a finished wall at 45 degrees (LW45), corner radius, start and end feature. From the CEAD 45-degree sheets and the Ginger handbook.", Io.Slicer, Group.Start)) { }
        public DesignRulesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerDesignRulesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Bead Width", "W", "Layer width in mm.").Set(5.0);
            inputs.AddNumber("Nozzle", "N", "Nozzle diameter in mm. 0 = bead width / 1.5.").Set(3.0);
            inputs.AddNumber("Layer Height", "H", "Layer height in mm, to be checked. 0 = not checked.").Set(2.0);
            inputs.AddNumber("Overlap", "OL", "Overlap of two beads side by side in mm. 0 = middle of 5 to 10 % of the bead width.").Set(0.0);
            inputs.AddNumber("Finish Thickness", "t", "Optional: the wall thickness the finished part needs, in mm.").Set(0.0);
            inputs.AddNumber("Milling", "Mill", "Optional: how much is milled away from that wall afterwards, in mm.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddBoolean("OK", "OK", "True if bead width, nozzle and layer height fit together.");
            outputs.AddText("Report", "R", "The rules with your numbers.", Access.Twig);
            outputs.AddNumber("CO90", "CO90", "Wall of two beads, layers square to it: model it this thick (mm).");
            outputs.AddNumber("CO45", "CO45", "Wall of two beads, layers crossing it at 45 degrees: model it this thick (mm).");
            outputs.AddNumber("LW45", "LW45", "Layer width for the finished wall thickness at 45 degrees (mm).");
        }

        protected override void Process(IDataAccess access)
        {
            double w, n, h, ol, t, mill;
            access.GetItem(0, out w); access.GetItem(1, out n); access.GetItem(2, out h); access.GetItem(3, out ol); access.GetItem(4, out t); access.GetItem(5, out mill);
            if (w <= 0) { access.AddError("Invalid bead width", "Bead Width must be greater than 0."); return; }
            var r = DesignRules.Evaluate(w, n, h, ol, t, mill);
            if (!r.Ok) access.AddWarning("Rule not met", r.Report.Find(l => l.Contains("TOO")) ?? "See Report.");
            access.SetItem(0, r.Ok); access.SetTwig(1, r.Report.ToArray(), null, null);
            access.SetItem(2, r.CO90); access.SetItem(3, r.CO45); access.SetItem(4, r.LW45);
        }
    }

    // ============================================================== settings
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1001")]
    public sealed class PrintSettingsComponent : Component
    {
        public PrintSettingsComponent() : base(Io.Name("Print Settings", "Bead width, layer height and speed for all slicer components. The bead width drives every generated dimension: wall spacing, infill spacing, smallest radius.", Io.Slicer, Group.Start)) { }
        public PrintSettingsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerSettingsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Bead Width", "W", "Width of one printed bead in mm.").Set(5.0);
            inputs.AddNumber("Layer Height", "H", "Layer height in mm. A third to a half of the bead width.").Set(2.0);
            inputs.AddNumber("Speed", "V", "Print speed in mm/s, the same for every move (50 mm/s = F3000 in G-code).").Set(50.0);
            inputs.AddInteger("Machine Profile", "MP", "Machine profile:\n" + string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddNumber("Flow", "Fl", "Extrusion multiplier (1 = 100 %).").Set(1.0);
            inputs.AddBoolean("Preview", "P", "Fast and coarse, for trying things out.").Set(false);
            inputs.AddNumber("Density", "D", "Density of the printed material in g/cm3, for the weight in file names and reports. PLA and PETG about 1.25, PP about 0.9.").Set(1.25);
            inputs.AddNumber("First Layer Height", "H1", "Height of the first layer in mm: the nozzle runs this high above the bed, so the first bead is not pressed into it. 0 = the layer height.").Set(0.0);
            inputs.AddInteger("Nozzle", "Nz", "The nozzle that is screwed in; it sets bead width and layer height. 0 = own values:\n" + string.Join("\n", Nozzles.Names.Select((n, i) => i + " = " + n))).Set(0);
            inputs.AddNumber("First Layer Speed", "V1", "Speed of the first layer in mm/s, for adhesion (the Ginger prints it at about 25 mm/s). 0 = the same as Speed.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Settings", "S", "Print settings for the slicer components.");
            outputs.AddText("Info", "I", "Summary.");
        }

        protected override void Process(IDataAccess access)
        {
            double w, h, f, flow; int machine; bool preview;
            access.GetItem(0, out w); access.GetItem(1, out h); access.GetItem(2, out f);
            access.GetItem(3, out machine); access.GetItem(4, out flow); access.GetItem(5, out preview);
            if (w <= 0 || h <= 0 || f <= 0) { access.AddError("Invalid values", "Bead width, layer height and speed must be larger than 0."); return; }
            if (machine < 0 || machine >= MachineProfile.PresetNames.Length) { access.AddWarning("Unknown machine", "Using the generic profile."); machine = 0; }
            f = f > 1000 ? f : f * 60; // mm/s; a value above 1000 is an older definition in mm/min
            var s = new PrintSettings { BeadWidth = w, LayerHeight = h, Speed = f, Flow = flow > 0 ? flow : 1.0, Preview = preview, Machine = MachineProfile.Preset(machine) };
            double density; access.GetItem(6, out density); if (density > 0) s.Density = density;
            double first; access.GetItem(7, out first); s.FirstLayerHeight = Math.Max(0, first);
            if (first > 0 && (first < 0.5 * h || first > 1.5 * h)) access.AddWarning("First layer", $"The first layer is {first:0.##} mm, the others {h:0.##} mm. Stay between half and one and a half layer heights, or the first bead is crushed or does not stick.");
            double v1; access.GetItem(9, out v1); s.FirstLayerSpeed = v1 > 0 ? (v1 > 1000 ? v1 : v1 * 60) : 0;
            int nozzle; access.GetItem(8, out nozzle);
            if (nozzle > 0)
            {
                Nozzles.Apply(s, nozzle); w = s.BeadWidth; h = s.LayerHeight;
                foreach (var note in Nozzles.Check(s.Nozzle, s.NozzleShape, w, h)) access.AddWarning("Nozzle", note);
            }
            double ratio = h / w;
            if (ratio > 0.6) access.AddWarning("Layer too high", $"The layer height is {ratio * 100:0} % of the bead width. Stay below half the bead width or the beads bond poorly.");
            access.SetItem(0, s);
            access.SetItem(1, s.ToString());
        }
    }

    // ============================================================== slice
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1002")]
    public sealed class SliceComponent : Component
    {
        public SliceComponent() : base(Io.Name("Slice", "Cuts geometry (Breps, surfaces, SubDs, meshes; open or closed) into layers. Flat by default, tilted with an angle. With a guide surface the layers follow it (non-planar); with a guide curve they stand square to it (multi-axis).", Io.Slicer, Group.Layers)) { }
        public SliceComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerSliceIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes.", Access.Twig);
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Angle", "A", "Tilts the layers around the X axis, in degrees. 45 = 45 degree slicing.").Set(0.0);
            inputs.AddGeneric("Guide", "Gd", "Optional. A surface, Brep or mesh: the layers follow it. A curve: the layers stand square to it.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Guide Mode", "GM", "0 = with a guide surface: flat bottom, top follows the guide.\n1 = with a guide surface: all layers follow the guide (curved base or mould).\n2 = follow the part (no guide): leave Guide empty; the layers follow the part's own wall from bottom to top, so overhangs get layers of even thickness. The nozzle stays vertical.\n3 = follow the surface (no guide): leave Guide empty; for closed parts such as a head: rings on the part's surface from the ring it stands on to the crown, under chin and nose as well, closing the top. Use Continuous Path in Spiral mode. The bead height follows the law of the printed head of 2024. The nozzle stays vertical.").Set(0);
            inputs.AddInteger("Tool Axis", "TA", "Only with a guide: 0 = tool square to the layer (robot). 1 = tool stays vertical.").Set(0);
            inputs.AddInteger("Start/End Layers", "SE", "Sacrificial layers before the first and after the last layer, milled away afterwards (about 5 for angled printing). The start feature stands on the bed. 0 = none.").Set(0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers for the following components.");
            outputs.AddPolyline("Contours", "C", "Layer contours.", Access.Twig);
            outputs.AddText("Info", "I", "Statistics.");
        }

        protected override void Process(IDataAccess access)
        {
            object[] geo;
            if (!access.GetItemArray(0, out geo) || geo == null || geo.Length == 0) return;
            var settings = Io.Settings(access, 1);
            double angle; access.GetItem(2, out angle);
            object guide; access.GetItem(3, out guide);
            int guideMode, toolMode; access.GetItem(4, out guideMode); access.GetItem(5, out toolMode);

            Plane plane = Plane.WorldXY;
            if (Math.Abs(angle) > 1e-9) plane.Rotate(Rhino.RhinoMath.ToRadians(angle), plane.XAxis, plane.Origin);
            var log = new List<string>();
            double tol = Math.Max(0.01, settings.Preview ? settings.Resolution * 0.25 : settings.Resolution * 0.025);

            Warp warp = null;
            if (guide != null)
            {
                if (guide is Line gl) guide = new LineCurve(gl);
                if (guide is Polyline gp) guide = new PolylineCurve(gp);
                if (guide is Arc ga) guide = new ArcCurve(ga);
                if (guide is Curve gc) { var cw = new CurveWarp(gc, toolMode == 0); if (cw.Valid) warp = cw; }
                else
                {
                    Mesh gm = SliceEngine.ToMesh(new[] { Io.Solid(guide) }, tol, null);
                    if (gm != null) { var sw = new SurfaceWarp(gm, guideMode == 0, toolMode == 0); if (sw.Valid) warp = sw; }
                }
                if (warp == null) access.AddWarning("Guide not usable", "Connect a surface, Brep or mesh (non-planar) or a curve (multi-axis). Slicing flat.");
                else plane = Plane.WorldXY;
            }

            // a bent layer needs a fine mesh, or long flat faces would stay straight
            bool followPart = guide == null && (guideMode == 2 || guideMode == 3);
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), tol, log, warp != null || followPart ? Math.Max(4.0, settings.BeadWidth * 1.5) : 0);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            if (followPart)
            {
                // non-planar layers from the part itself (meridians), nozzle vertical
                string what = guideMode == 3 ? "Follow the surface" : "Follow the part";
                var mw = guideMode == 3
                    ? new MeridianWarp(mesh, settings.LayerHeight, settings.BeadWidth, new SurfaceField(mesh), 240)
                    : new MeridianWarp(mesh, settings.LayerHeight, settings.BeadWidth);
                if (mw.Valid)
                {
                    warp = mw; plane = Plane.WorldXY;
                    log.Add(mw.Summary(settings.BeadWidth)); log.AddRange(mw.Notes);
                    if (mw.ThickMeridians > 0 && guideMode == 2) access.AddWarning(what, mw.Summary(settings.BeadWidth));
                }
                else access.AddWarning(what, mw.Problem + ". Slicing flat.");
            }
            int extra; access.GetItem(6, out extra);
            var layers = SliceEngine.Slice(mesh, plane, settings.LayerHeight, settings.PointSpacing, log, warp, settings.FirstLayerHeight);
            if (extra > 0 && warp == null)
            {
                // sacrificial layers before and after the part (CEAD: about 5 for angled printing)
                int before = layers.Count;
                layers = StartEndFeature.AddStart(layers, extra, 0.0, log);
                int startMade = layers.Count - before;
                layers = StartEndFeature.AddEnd(layers, extra, log);
                log.Add(startMade > 0
                    ? $"Start and end feature: {startMade} layer(s) in front of the part, standing on the bed, and {extra} on top of its last layer. They are sacrificial: mill them away."
                    : $"End feature: {extra} layer(s) on top of the last one{(Math.Abs(plane.Normal.Z) > 0.999 ? " (flat layers start on the bed and need no start feature)" : "")}. Mill them away.");
            }
            else if (extra > 0) log.Add("Start and end feature: only for flat or angled layers, not with a guide.");
            Io.Remarks(access, log);
            if (layers.Count == 0) { access.AddWarning("No layers", "Check the layer height and where the part stands."); return; }

            access.SetItem(0, new LayerStack(layers));
            Io.Contours(access, 1, layers);
            access.SetItem(2, $"{layers.Count} layers{(warp != null ? ", " + warp.Name : "")}, {layers.Sum(l => l.Contours.Count)} contours");
        }
    }

    // ============================================================== walls
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1003")]
    public sealed class WallsComponent : Component
    {
        public WallsComponent() : base(Io.Name("Walls", "Turns the layer contours into walls: the centre lines of the beads, one bead spacing apart.", Io.Slicer, Group.Walls)) { }
        public WallsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWallsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers from Slice.");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Count", "N", "Number of walls.").Set(1);
            inputs.AddInteger("Position", "P", "0 = the outer bead lies inside the model surface. 1 = centred on it. 2 = outside.").Set(0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers with walls.");
            outputs.AddPolyline("Contours", "C", "Wall centre lines.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int count, position; access.GetItem(2, out count); access.GetItem(3, out position);
            var log = new List<string>();
            var result = FillEngine.Walls(layers, settings, Math.Max(1, count), Math.Max(0, Math.Min(2, position)), log);
            Io.Remarks(access, log);
            access.SetItem(0, new LayerStack(result));
            Io.Contours(access, 1, result);
        }
    }

    // ============================================================== bottom
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1004")]
    public sealed class BottomFillComponent : Component
    {
        public BottomFillComponent() : base(Io.Name("Bottom Fill", "Closes the first layers with a solid bottom printed as one line.", Io.Slicer, Group.Walls)) { }
        public BottomFillComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerBottomIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers with walls.");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Count", "N", "Number of solid bottom layers. 0 = none.").Set(1);
            inputs.AddInteger("Pattern", "P", "0 = automatic. 1 = zigzag. 2 = contour (follows the wall inward).").Set(0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers with bottom.");
            outputs.AddPolyline("Contours", "C", "All contours.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int count, pattern; access.GetItem(2, out count); access.GetItem(3, out pattern);
            var log = new List<string>();
            var result = count > 0 ? FillEngine.Bottom(layers, settings, count, Math.Max(0, Math.Min(2, pattern)), 0, log, out _) : layers;
            Io.Remarks(access, log);
            access.SetItem(0, new LayerStack(result));
            Io.Contours(access, 1, result);
        }
    }

    // ============================================================== infill
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1005")]
    public sealed class InfillComponent : Component
    {
        private static readonly string[] Patterns = { "Ribs (even spacing)", "Honeycomb", "Grid", "Wave", "Truss", "Meander", "Chamber wall", "Truss wall" };

        public InfillComponent() : base(Io.HiddenName("Infill", "Adds an infill that is part of the wall line: one closed line per layer, no travel moves. Ribs, cells, waves that meet in the middle, or a chamber wall.", Io.Slicer, Group.Walls)) { }
        public InfillComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerRibsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers with walls (and bottom).");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Pattern", "P", string.Join("\n", Patterns.Select((n, i) => i + " = " + n))).Set(1);
            inputs.AddNumber("Size", "Sz", "Rib spacing, cell size, wavelength or chamber length in mm.").Set(80.0);
            inputs.AddNumber("Depth", "D", "Waves and chambers: how far the pattern reaches into the part in mm. 0 = as far as the part allows.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers with infill.");
            outputs.AddPolyline("Contours", "C", "All contours.", Access.Twig);
            outputs.AddText("Info", "I", "What was built.");
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int pattern; double size, depth;
            access.GetItem(2, out pattern); access.GetItem(3, out size); access.GetItem(4, out depth);
            pattern = Math.Max(0, Math.Min(Patterns.Length - 1, pattern));
            size = Math.Max(size, 4 * settings.BeadSpacing);
            var log = new List<string>();
            List<SliceLayer> result; int n;
            switch (pattern)
            {
                case 0: result = FillEngine.Ribs(layers, settings, new List<object>(), size, depth, true, 0.15, log, out n); break;
                case 1: result = FillEngine.Cells(layers, settings, 0, size, 0, null, log, out n); break;
                case 2: result = FillEngine.Cells(layers, settings, 1, size, 0, null, log, out n); break;
                case 3: case 4: case 5: result = FillEngine.Waves(layers, settings, pattern - 3, size, depth, null, log, out n); break;
                default: result = FillEngine.Chambers(layers, settings, pattern - 6, size, depth > 0 ? depth : 0.6 * size, log, out n); break;
            }
            Io.Remarks(access, log);
            access.SetItem(0, new LayerStack(result));
            Io.Contours(access, 1, result);
            access.SetItem(2, $"{Patterns[pattern]}: up to {n} per layer");
        }
    }

    // ============================================================== continuous path
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1006")]
    public sealed class ContinuousPathComponent : Component
    {
        public ContinuousPathComponent() : base(Io.Name("Continuous Path", "Joins all layers into one continuous print path. Layered: every layer is flat and the path climbs over a short ramp at one fixed place. Spiral: the path climbs all the time (single walls only).", Io.Slicer, Group.Path)) { }
        public ContinuousPathComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerPathIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers.");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Mode", "M", "1 = layered (default). 0 = spiral (vase mode).").Set(1);
            inputs.AddNumber("Seam Gap", "G", "Layered: the gap at the seam in mm. Every layer ends this far before its own start; the path crosses the gap on a short incline up to the next layer. 0 = one bead width.").Set(0.0);
            inputs.AddGeneric("Seam", "Seam", "Optional. A point or a curve: where the seam is. Every layer starts at the place on its contour nearest to it. Empty = the seam stays where the first layer started.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Bridge", "Br", "True = a contour inside another (inner shell, opening) is joined to it by a bridge of two lines one bead spacing apart, stacked on every layer: one line without travel moves.").Set(true);
            inputs.AddInteger("Seam Mode", "Sm", "Layered only: where the seam goes.\n0 = fixed point (default): at the Seam point, or where the first layer starts.\n1 = inner corner: in an inner corner of the outer wall, where it shows least and milling does not run over it (no inner corner: the sharpest outer corner).\n2 = sharpest corner of the outer wall.\nThe corner is followed from layer to layer, at most half a bead to the side. A Seam point picks which corner.").Set(0);
            inputs.AddNumber("Min Layer Time", "Tmin", "Shortest time a layer may take in s. A layer that would be done sooner is printed slower, so the layer below can cool: F stays constant within the layer and never goes below Min Speed. 0 = off (default). About 60 s suits many large pellet prints.").Set(0.0);
            inputs.AddNumber("Max Volume Rate", "Qmax", "Most the extruder can deliver in mm3/s. Moves that would ask for more run slower, never below Min Speed. 0 = off (default). The Ginger gives about 330 mm3/s.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The print path.");
            outputs.AddPolyline("Path", "P", "The path as a polyline.");
            outputs.AddText("Info", "I", "Statistics.");
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int mode; double ramp; access.GetItem(2, out mode); access.GetItem(3, out ramp);
            var log = new List<string>();
            object seamIn; access.GetItem(4, out seamIn);
            GeometryBase seam = null;
            if (seamIn is Point3d sp) seam = new Point(sp);
            else if (seamIn is Line sl) seam = new LineCurve(sl);
            else if (seamIn is Polyline spl) seam = new PolylineCurve(spl);
            else if (seamIn is GeometryBase sg) seam = sg;
            bool bridge; access.GetItem(5, out bridge);
            int seamMode; double minTime, maxRate;
            access.GetItem(6, out seamMode); access.GetItem(7, out minTime); access.GetItem(8, out maxRate);
            if (minTime > 0 || maxRate > 0)
            {
                // the limits travel with the path, so every machine output (G-code, DXR, robot) uses them
                settings = settings.Duplicate();
                settings.MinLayerTime = Math.Max(0, minTime); settings.MaxVolumeRate = Math.Max(0, maxRate);
            }
            var tp = PathEngine.Build(layers, settings, mode == 0 ? ContinuousMode.Spiral : ContinuousMode.Layered, seam, Math.Max(0, ramp), false, log, bridge, (SeamMode)Math.Max(0, Math.Min(2, seamMode)));
            Io.Remarks(access, log);
            if (tp.Count < 2) { access.AddError("No path", "The layers contain no printable contour."); return; }
            int travels = 0; for (int i = 1; i < tp.Count; i++) if (tp.Roles[i] == PathRole.Travel && tp.Roles[i - 1] != PathRole.Travel) travels++;
            access.SetItem(0, tp);
            access.SetItem(1, tp.ToPolyline());
            access.SetItem(2, $"{tp.Count} points, {tp.Length() / 1000.0:0.00} m, {travels} travel move(s)");
        }
    }

    // ============================================================== finish: round, skirt, exit
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1007")]
    public sealed class FinishPathComponent : Component
    {
        public FinishPathComponent() : base(Io.HiddenName("Round and Lead", "Rounds the corners of the path, puts a skirt in front of the part and an exit move behind it. The skirt ends opposite the start, so the way across is short and square to the wall.", Io.Slicer, Group.Path)) { }
        public FinishPathComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerRoundIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Path from Continuous Path.");
            inputs.AddNumber("Min Radius", "R", "Smallest corner radius in mm. 0 = bead width.").Set(0.0);
            inputs.AddInteger("Skirt Loops", "Sk", "Loops around the first layer, printed first. 0 = none.").Set(1);
            inputs.AddNumber("Exit Length", "E", "Sideways move off the part at the end, in mm.").Set(50.0);
            inputs.AddNumber("Exit Lift", "Z", "Lift at the very end, in mm.").Set(20.0);
            inputs.AddInteger("Brim", "Br", "Beads right against the first layer, so the part holds on to the bed.\n0 = none\n1 = outside\n2 = inside (only where the first layer is open inside)\n3 = both sides").Set(0);
            inputs.AddInteger("Brim Loops", "Bl", "Number of brim beads on each side.").Set(3);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The finished path.");
            outputs.AddPolyline("Path", "P", "The path as a polyline.");
            outputs.AddText("Info", "I", "What was done.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            double radius, exit, lift; int loops;
            access.GetItem(1, out radius); access.GetItem(2, out loops); access.GetItem(3, out exit); access.GetItem(4, out lift);
            var log = new List<string>();
            var rounded = PathEngine.Round(tp, radius > 0 ? radius : tp.Settings.EffectiveMinRadius, 0, out int corners, out int tooSharp);
            int brim, brimLoops; access.GetItem(5, out brim); access.GetItem(6, out brimLoops);
            var result = LeadEngine.StartEnd(rounded, Math.Max(0, loops), 0, Math.Max(0, brimLoops), Math.Max(0, Math.Min(3, brim)), Math.Max(0, exit), Math.Max(0, lift), null, 0, log);
            Io.Remarks(access, log);
            if (tooSharp > 0) access.AddWarning("Tight corners", $"{tooSharp} corner(s) could not reach the radius; the largest possible arc was used.");
            access.SetItem(0, result);
            access.SetItem(1, result.ToPolyline());
            access.SetItem(2, $"{corners} corner(s) rounded, {result.Count} points, {result.Length() / 1000.0:0.00} m");
        }
    }

    // ============================================================== check
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1008")]
    public sealed class PathCheckComponent : Component
    {
        public PathCheckComponent() : base(Io.Name("Path Check", "Checks whether the path can be printed: it never crosses itself, and every bead lies on the bead below. Read the report before every print.", Io.Slicer, Group.Check)) { }
        public PathCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerCheckIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The path to check.");
            inputs.AddNumber("Min Layer Time", "Tmin", "Shortest time a layer may take in s, so the layer below has cooled enough to carry the next one. A layer printed faster lands on soft material: overhangs sag and the part warps. 0 = the Min Layer Time of Continuous Path, or off (the report still lists the times). About 60 s suits many large pellet prints.").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddBoolean("OK", "OK", "True if the path can be printed.");
            outputs.AddText("Report", "R", "What was checked and found.", Access.Twig);
            outputs.AddPoint("Crossings", "X", "Where the path crosses itself.", Access.Twig);
            outputs.AddPoint("Unsupported", "U", "Points that hang in the air.", Access.Twig);
            outputs.AddNumber("Layer Times", "Lt", "Time of every layer at the print speed in s, from the first layer up.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            double minTime; access.GetItem(1, out minTime);
            var r = LeadEngine.Check(tp, 0, Math.Max(0, minTime));
            if (!r.Ok) access.AddWarning("Not printable as it is", string.Join("\n", r.Report));
            // too short layers and an extruder over its limit: the path can be printed, but say it
            foreach (var l in r.Report.Where(l => l.StartsWith("LAYER TIME") || l.StartsWith("VOLUME RATE"))) access.AddWarning(l.StartsWith("LAYER TIME") ? "Layer time" : "Volume rate", l);
            access.SetItem(0, r.Ok);
            access.SetTwig(1, r.Report.ToArray(), null, null);
            access.SetTwig(2, r.Crossings.ToArray(), null, null);
            access.SetTwig(3, r.Unsupported.ToArray(), null, null);
            access.SetTwig(4, r.LayerTimes.ToArray(), null, null);
        }
    }

    // ============================================================== non-planar check
    [IoId("c3e7a2d4-6f1b-4a8e-9d25-8e2da5d2c15d")]
    public sealed class NonPlanarCheckComponent : Component
    {
        public NonPlanarCheckComponent() : base(Io.Name("Non-Planar Check", "Checks a non-planar path for a printer whose nozzle stays vertical. Bead height: the vertical gap to the layer below is measured at every point and compared with the bead width, the layer height and the height the extrusion was worked out for. Head: the nozzle is a cone with a wider head above it; nothing printed before may reach into it.", Io.Slicer, Group.Check)) { }
        public NonPlanarCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerNonPlanarCheckIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The path to check.");
            inputs.AddNumber("Tip Radius", "r0", "Radius of the nozzle tip in mm.").Set(4.0);
            inputs.AddNumber("Cone Angle", "A", "Half angle of the nozzle cone, from vertical, in degrees.").Set(30.0);
            inputs.AddNumber("Cone Length", "Lc", "Height of the cone from the tip up to the head in mm.").Set(60.0);
            inputs.AddNumber("Head Radius", "R", "Radius of the head above the cone in mm.").Set(40.0);
            inputs.AddNumber("Pose", "P", "Where the head is shown along the path: 0 = start, 1 = end.").Set(1.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddBoolean("OK", "OK", "True if the bead heights fit and the head hits nothing.");
            outputs.AddText("Report", "R", "What was checked and found.", Access.Twig);
            outputs.AddNumber("Bead Height", "H", "Measured bead height (vertical gap to the layer below) for every path point in mm; 0 = not measured. Use it to colour the path.", Access.Twig);
            outputs.AddPoint("Bad Heights", "Bh", "Points where the bead is higher than 0.8 x bead width or thinner than 0.4 x layer height.", Access.Twig);
            outputs.AddPoint("Collisions", "X", "Points where the head hits beads printed before, or the path climbs more steeply than the cone. Must be empty.", Access.Twig);
            outputs.AddMesh("Head", "Hd", "The head as it is checked (tip, cone, body), standing on the path at Pose.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            var head = new NonPlanarCheck.Head();
            double tip, angle, length, radius, pose;
            access.GetItem(1, out tip); access.GetItem(2, out angle); access.GetItem(3, out length); access.GetItem(4, out radius); access.GetItem(5, out pose);
            head.TipRadius = tip; head.HalfAngle = angle; head.ConeLength = length; head.Radius = radius;
            if (tip < 0 || angle < 0 || angle >= 90 || length <= 0 || radius <= 0)
            {
                access.AddError("Invalid head", "Tip Radius must be 0 or more, Cone Angle between 0 and 90 degrees, Cone Length and Head Radius more than 0.");
                return;
            }
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                access.AddRemark("Tilted tool axes", "The path has tilted tool axes. This check is for a vertical nozzle.");
            var r = NonPlanarCheck.Run(tp, head);
            if (!r.Ok) access.AddWarning("Check the path", r.Report.FirstOrDefault(l => l.StartsWith("HEAD") || l.StartsWith("TOO") || l.StartsWith("BEAD") || l.StartsWith("EXTRUSION") || l.StartsWith("Empty")) ?? "Read Report.");
            access.SetItem(0, r.Ok);
            access.SetTwig(1, r.Report.ToArray(), null, null);
            access.SetTwig(2, r.BeadHeights.ToArray(), null, null);
            access.SetTwig(3, r.BadHeights.ToArray(), null, null);
            access.SetTwig(4, r.Collisions.ToArray(), null, null);
            int at = (int)Math.Round(Math.Max(0, Math.Min(1, pose)) * (tp.Count - 1));
            access.SetItem(5, head.ToMesh(tp.Points[at]));
        }
    }

    // ============================================================== extrusion and speed
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d1009")]
    public sealed class ExtrusionSpeedComponent : Component
    {
        public ExtrusionSpeedComponent() : base(Io.Name("Extrusion and Speed", "The values for the machine: one plane, one extrusion amount and one speed per path point. The speed is constant and never 0.", Io.Slicer, Group.Check)) { }
        public ExtrusionSpeedComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerExtrusionIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddPoint("Points", "P", "Path points.", Access.Twig);
            outputs.AddPlane("Planes", "Pl", "Tool planes; the normal is the tool axis.", Access.Twig);
            outputs.AddNumber("Extrusion", "E", "Extrusion for the move that ends at each point.", Access.Twig);
            outputs.AddNumber("Feed", "F", "Feed F for the machine in mm/min (mm/s x 60), for the move that ends at each point.", Access.Twig);
            outputs.AddText("Role", "R", "Wall, Bottom, Infill, Seam, Travel or Skirt.", Access.Twig);
            outputs.AddInteger("Layer", "L", "Layer index.", Access.Twig);
            outputs.AddText("Info", "I", "Length, volume and time.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            var r = PathEngine.Process(tp, false);
            access.SetTwig(0, tp.Points.ToArray(), null, null);
            access.SetTwig(1, r.Frames.ToArray(), null, null);
            access.SetTwig(2, r.Extrusion.ToArray(), null, null);
            access.SetTwig(3, r.Speed.ToArray(), null, null);
            access.SetTwig(4, tp.Roles.Select(x => x.ToString()).ToArray(), null, null);
            access.SetTwig(5, tp.Layers.ToArray(), null, null);
            access.SetItem(6, $"{r.Length / 1000.0:0.00} m path, {r.PrintLength / 1000.0:0.00} m printed, {r.Volume / 1000.0:0} cm3, about {r.Minutes:0} min");
        }
    }
}
