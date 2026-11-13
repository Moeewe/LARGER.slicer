using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>The five infill components of Grasshopper 1, one per kind of infill.</summary>
    internal static class InfillIo
    {
        public static void Inputs(InputAdder inputs, string from = "Layers from Walls or Bottom Fill.")
        {
            inputs.AddGeneric("Layers", "L", from);
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
        }

        public static void Outputs(OutputAdder outputs, string what)
        {
            outputs.AddGeneric("Layers", "L", what);
            outputs.AddPolyline("Contours", "C", "Paths as curves (for checking).", Access.Twig);
            outputs.AddText("Info", "I", "Statistics.");
        }

        public static void Done(IDataAccess access, List<SliceLayer> result, List<string> log, string info)
        {
            Io.Remarks(access, log);
            access.SetItem(0, new LayerStack(result));
            Io.Contours(access, 1, result);
            access.SetItem(2, info);
        }

        public static Point3d? Point(IDataAccess access, int index)
        {
            if (access.GetItem(index, out Point3d p) && p.IsValid) return p;
            return null;
        }
    }

    // ============================================================== infill cells
    [IoId("f105ab40-71ac-4a33-af0c-a087ef3e3bc1")]
    public sealed class InfillCellsComponent : Component
    {
        public InfillCellsComponent() : base(Io.Name("Infill Cells", "Fills the part with honeycomb or grid cells as part of the one continuous wall line. Every cell is a closed loop; neighbouring loops lie one bead spacing apart and fuse into a double wall. All loops and the wall are joined by short bridges into a single closed line that never crosses itself, so it also prints in spiral (vase) mode. Use it instead of Infill Ribs.", Io.Slicer, Group.Walls)) { }
        public InfillCellsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerCellsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            InfillIo.Inputs(inputs);
            inputs.AddInteger("Pattern", "P", "0 = Honeycomb\n1 = Grid").Set(0).Choices("Honeycomb", "Grid");
            inputs.AddNumber("Cell Size", "Sz", "Distance between the middles of two neighbouring cells (mm). At least four bead spacings.").Set(60.0).Values(20, 40, 60, 80, 100, 150, 200, 300);
            inputs.AddNumber("Angle", "A", "Turns the pattern (degrees).").Set(0.0).Angles();
            inputs.AddPoint("Origin", "O", "Fixed point of the pattern: the middle of one cell. Move it to shift the whole pattern. The cells stand exactly above each other in all layers. Empty = world origin.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Stretch", "St", "Stretches the cells along the pattern direction (1 = regular, 2 = twice as long). Turn the direction with Angle.").Set(1.0).Values(1, 1.5, 2, 3);
        }

        protected override void AddOutputs(OutputAdder outputs) => InfillIo.Outputs(outputs, "Layers with the cells joined into the wall path.");

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int pattern; double size, angle, stretch;
            access.GetItem(2, out pattern); access.GetItem(3, out size); access.GetItem(4, out angle); access.GetItem(6, out stretch);
            var origin = InfillIo.Point(access, 5);
            if (size < 4 * settings.BeadSpacing) access.AddRemark("Cell Size raised", $"Cell Size raised to {4 * settings.BeadSpacing:0.#} mm: a cell needs room for two beads and an opening.");
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Cells(layers, settings, pattern, size, angle, origin, log, out int cells, stretch);
            sw.Stop();
            if (cells == 0) access.AddWarning("No cells", "No cells fit. Reduce Cell Size or check that the layers have a closed wall.");
            InfillIo.Done(access, result, log, $"{(cells == 0 ? "No cells" : pattern == 1 ? "Grid" : "Honeycomb")}: up to {cells} cells per layer, bead spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== infill wave
    [IoId("cf91c4ed-8b46-4ccf-8b4f-2798046af195")]
    public sealed class InfillWaveComponent : Component
    {
        public InfillWaveComponent() : base(Io.Name("Infill Wave", "A single inner line that runs along the wall and dips into the part at regular intervals: as a smooth wave, a truss (straight flanks) or a meander (steep flanks). Every straight side gets a whole number of equal waves, corners stay clear, and the line is part of the one continuous wall path. Wavelength and Depth scale the pattern; on round parts Origin moves it.", Io.Slicer, Group.Walls)) { }
        public InfillWaveComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerWaveIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            InfillIo.Inputs(inputs);
            inputs.AddInteger("Shape", "Sh", "0 = Wave (smooth, no corners)\n1 = Truss (straight flanks, triangles)\n2 = Meander (steep flanks, long contact with the wall)").Set(0).Choices("Wave", "Truss", "Meander");
            inputs.AddNumber("Wavelength", "WL", "Distance from crest to crest along the wall (mm). The slicer fits a whole number of waves around the part.").Set(80.0).Values(30, 40, 60, 80, 100, 150, 200, 300, 400);
            inputs.AddNumber("Depth", "D", "How far the line may reach into the part (mm). With Meet this is the limit; 0 = as far as the part allows.").Set(0.0);
            inputs.AddPoint("Origin", "O", "Round parts only: a crest sits at the wall point nearest to this point. Empty = the right-most side of the part.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Meet", "Meet", "True = every dip reaches until it meets something: the dip from the opposite side (in the middle of the part) or the wall of a hole. That is what makes the part stiff.\nFalse = all dips have the same Depth and end freely.", ("On", "Dips meet in the middle"), ("Off", "Dips end at Depth")).Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs) => InfillIo.Outputs(outputs, "Layers with the wave joined into the wall path.");

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int shape; double wavelength, depth; bool meet;
            access.GetItem(2, out shape); access.GetItem(3, out wavelength); access.GetItem(4, out depth); access.GetItem(6, out meet);
            var origin = InfillIo.Point(access, 5);
            if (!(meet && depth <= 0) && depth < 2 * settings.BeadWidth)
            {
                access.AddWarning("Depth too small", $"Depth must be at least {2 * settings.BeadWidth:0.#} mm (two bead widths).");
                return;
            }
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Waves(layers, settings, Math.Max(0, Math.Min(2, shape)), wavelength, depth, origin, log, out int waves, meet);
            sw.Stop();
            if (waves == 0) access.AddWarning("No wave", "No wave fits. Reduce Depth or Wavelength, or check that the layers have a closed wall.");
            InfillIo.Done(access, result, log, $"{(waves == 0 ? "No wave" : shape == 1 ? "Truss" : shape == 2 ? "Meander" : "Wave")}: up to {waves} waves per layer, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== infill sketch
    [IoId("c912fd57-67f7-44f3-a3b3-2a44fc41022b")]
    public sealed class InfillSketchComponent : Component
    {
        public InfillSketchComponent() : base(Io.Name("Infill Sketch", "Draw your infill. Sketch curves over the part in top view; every curve that runs from wall to wall becomes a rib that follows it, built from two fused beads and joined into the one continuous wall path. Works on every layer the curve crosses, also on tilted and bent layers.", Io.Slicer, Group.Walls)) { }
        public InfillSketchComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerSketchIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            InfillIo.Inputs(inputs);
            inputs.AddCurve("Sketch", "Sk", "Curves drawn in top view (flat, at any height). Each one should cross the part from wall to wall. Keep bends wider than the bead width.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddBoolean("Meet", "Meet", "False = one rib along the whole curve\nTrue = two ribs, one from each wall, that meet on the curve; the joint changes sides every layer so the ribs interlock", ("On", "Two ribs meet on the curve"), ("Off", "One rib along the curve")).Set(false);
            inputs.AddNumber("Stagger", "St", "Meet only: how far the joint moves off the middle, alternating every layer (0-0.35).").Set(0.15).Values(0, 0.1, 0.15, 0.25, 0.35);
        }

        protected override void AddOutputs(OutputAdder outputs) => InfillIo.Outputs(outputs, "Layers with the sketched ribs joined into the wall path.");

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            access.ItemArray(2, out Curve[] curves);
            bool meet; double stagger; access.GetItem(3, out meet); access.GetItem(4, out stagger);
            var sketch = (curves ?? new Curve[0]).Where(c => c != null && c.IsValid).Cast<object>().ToList();
            if (sketch.Count == 0)
            {
                access.AddRemark("No sketch", "Draw curves over the part in top view and connect them to Sketch.");
                access.SetItem(0, new LayerStack(layers));
                return;
            }
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var crossLog = new List<string>();
            sketch = FillEngine.WithoutCrossingSketches(sketch, crossLog);
            foreach (var l in crossLog) access.AddWarning("Sketch", l);
            var result = FillEngine.Ribs(layers, settings, sketch, 0, 0, meet, stagger, log, out int ribs);
            sw.Stop();
            if (ribs == 0) access.AddWarning("No ribs", "No rib could be made. Each curve must be flat (drawn in top view) and cross the part from wall to wall.");
            InfillIo.Done(access, result, log, $"{sketch.Count} sketched curve(s), {ribs} rib(s) in total over {result.Count} layers, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== infill chambers
    [IoId("455ab52f-baaa-4808-a4b1-29ff9ef361a0")]
    public sealed class InfillChambersComponent : Component
    {
        public InfillChambersComponent() : base(Io.Name("Infill Chambers", "A band of closed chambers along the inside of the wall: the classic double wall with webs, as boxes or as a truss with diagonals. Every chamber is a loop, neighbouring loops fuse, and all are joined into the one continuous wall path. The chambers of a layer stand on those of the layer below, also on twisting or tapering parts.", Io.Slicer, Group.Walls)) { }
        public InfillChambersComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerChambersIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            InfillIo.Inputs(inputs);
            inputs.AddInteger("Shape", "Sh", "0 = Boxes (webs square to the wall)\n1 = Truss (boxes with alternating diagonals)").Set(0).Choices("Boxes", "Truss");
            inputs.AddNumber("Length", "Len", "Length of a chamber along the wall (mm). Every side gets a whole number of equal chambers.").Set(70.0).Values(30, 50, 70, 100, 150, 200, 300, 400);
            inputs.AddNumber("Depth", "D", "Depth of the band, from the wall inwards (mm). Reduced automatically where the part is too narrow.").Set(50.0).Values(20, 30, 50, 80, 100, 150, 200, 300);
        }

        protected override void AddOutputs(OutputAdder outputs) => InfillIo.Outputs(outputs, "Layers with the chambers joined into the wall path.");

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            int shape; double length, depth; access.GetItem(2, out shape); access.GetItem(3, out length); access.GetItem(4, out depth);
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Chambers(layers, settings, shape == 1 ? 1 : 0, length, depth, log, out int chambers);
            sw.Stop();
            if (chambers == 0) access.AddWarning("No chambers", "No chamber fits. Reduce Depth or Length, or check that the layers have a closed wall.");
            InfillIo.Done(access, result, log, $"{(chambers == 0 ? "No chambers" : shape == 1 ? "Truss" : "Boxes")}: up to {chambers} chambers per layer, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== infill ribs
    [IoId("c184895f-2aed-4ce3-8519-2937ed09b7c4")]
    public sealed class InfillRibsComponent : Component
    {
        public InfillRibsComponent() : base(Io.Name("Infill Ribs", "Adds stiffening ribs as part of the continuous path. Each rib is a hairpin whose two legs lie one bead spacing apart and fuse; the tip stops one bead spacing before the opposite wall. Draw curves in top view to say where the ribs run, or let them be distributed evenly.", Io.Slicer, Group.Walls)) { }
        public InfillRibsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerRibsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            InfillIo.Inputs(inputs, "Layers from Walls (or Slice).");
            inputs.AddGeneric("Locations", "Loc", "Where ribs go:\nCurve drawn in top view = the rib follows the curve from wall to wall (draw your infill)\nPoint = straight rib at the nearest wall point in every layer\nCurve running upwards = rib where the curve crosses each layer\nPlane = rib where the plane cuts the wall", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Spacing", "Sp", "Additionally distribute ribs evenly along the wall at about this distance (mm). 0 = only the given locations.").Set(0.0).Values(0, 50, 80, 100, 150, 200, 300);
            inputs.AddNumber("Depth", "D", "Rib length (mm). 0 = across to the opposite wall.").Set(0.0);
            inputs.AddBoolean("Meet", "Meet", "True = two ribs, one from each wall, meet inside the part. False = one rib across.", ("On", "Two ribs meet inside"), ("Off", "One rib across")).Set(false);
            inputs.AddNumber("Stagger", "St", "Meet only: how far the joint of the two ribs is moved off the middle, alternating every layer (0-0.35 of the wall distance). The ribs then overlap from layer to layer and lock together.").Set(0.15).Values(0, 0.1, 0.15, 0.25, 0.35);
        }

        protected override void AddOutputs(OutputAdder outputs) => InfillIo.Outputs(outputs, "Layers with ribs spliced into the wall path.");

        protected override void Process(IDataAccess access)
        {
            using var cancel = Io.Cancellable(access); // stop the core loops when this solution is cancelled
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            access.ItemArray(2, out object[] raw);
            var locations = new List<object>();
            int unknown = 0;
            foreach (var o in raw ?? new object[0])
            {
                if (o is Point3d || o is Point || o is Curve || o is Line || o is Plane) locations.Add(o);
                else if (o is Polyline pl) locations.Add(new PolylineCurve(pl));
                else if (o is Arc arc) locations.Add(new ArcCurve(arc));
                else if (o is Circle circle) locations.Add(new ArcCurve(circle));
                else if (o != null) unknown++;
            }
            if (unknown > 0) access.AddWarning("Locations ignored", $"{unknown} location(s) ignored. Use points, curves, lines or planes.");
            double spacing, depth, stagger; bool meet;
            access.GetItem(3, out spacing); access.GetItem(4, out depth); access.GetItem(5, out meet); access.GetItem(6, out stagger);
            if (locations.Count == 0 && spacing <= 0) access.AddRemark("No ribs", "No ribs: connect Locations or set Spacing.");
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Ribs(layers, settings, locations, spacing, depth, meet, stagger, log, out int ribs);
            sw.Stop();
            InfillIo.Done(access, result, log, $"{(ribs == 0 ? "No ribs" : meet ? "Meet" : "Across")}: {ribs} ribs in {result.Count} layers ({(result.Count > 0 ? (double)ribs / result.Count : 0):0.#} per layer), leg spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }
}
