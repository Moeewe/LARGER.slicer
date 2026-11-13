using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Standard;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Curve tools: the old Toolpaths tab of Grasshopper 1, kept for your own curves. Same names,
    /// inputs and behaviour as there; they run on the fill engine of the Slicer tab.
    /// </summary>
    internal static class CurveIo
    {
        /// <summary>Grasshopper 2 keeps curves as lines, polylines, arcs, circles, rectangles or Rhino curves: one Rhino curve.</summary>
        public static Curve ToCurve(object o)
        {
            if (o == null) return null;
            if (o is Curve c) return c.DuplicateCurve();
            if (o is Line l) return new LineCurve(l);
            if (o is Polyline pl) return new PolylineCurve(pl);
            if (o is Arc a) return new ArcCurve(a);
            if (o is Circle ci) return new ArcCurve(ci);
            if (o is Rectangle3d r) return r.ToNurbsCurve();
            try { return CurveBroker.ToRhinoCurve(o); } catch { return null; }
        }

        public static Curve Curve(IDataAccess access, int index)
        {
            object o;
            return access.GetItem(index, out o) ? ToCurve(o) : null;
        }

        public static List<Curve> Curves(IDataAccess access, int index)
        {
            object[] items;
            if (!access.ItemArray(index, out items) || items == null) return new List<Curve>();
            return items.Select(ToCurve).Where(c => c != null).ToList();
        }

        public static void SetCurves(IDataAccess access, int index, IEnumerable<Curve> curves)
            => access.SetTwig(index, curves.ToArray(), null, null);

        /// <summary>The common start of the five region fills (Grasshopper 1: BottomLayerPatternBase.ValidateInputs).</summary>
        public static bool Region(IDataAccess access, out Curve boundary, out double printWidth, out List<Curve> holes)
        {
            boundary = Curve(access, 0); printWidth = 5.0; holes = null;
            if (boundary == null || !boundary.IsValid) { access.AddError("No boundary", "Valid boundary curve is required."); return false; }
            if (!access.GetItem(1, out printWidth)) { access.AddError("No print width", "Print width is required."); return false; }
            if (printWidth <= 0) { access.AddError("Invalid print width", "Print width must be greater than zero."); return false; }
            holes = Curves(access, 2).Where(h => h.IsValid).ToList();
            return true;
        }

        /// <summary>The common inputs of the five region fills.</summary>
        public static void RegionInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curve", "C", "Boundary curve to fill. Can be open or closed - will be closed automatically if needed.");
            inputs.AddNumber("Print Width", "PW", "Print width (bead width/extrusion width in mm). Center-to-center distance equals bead width. For 5mm nozzle, use 5mm.").Set(5.0);
            inputs.AddCurve("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", Access.Twig, Requirement.MayBeMissing);
        }

        public static void RegionOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Single Line Fill", "SLF", "Single continuous fill path", Access.Item);
        }

        /// <summary>Messages and output of a region fill, as in Grasshopper 1.</summary>
        public static void Finish(IDataAccess access, RegionFillResult fill, bool linkNote = true, string holeNote = null)
        {
            if (fill.Path == null || fill.Path.Count < 2)
            {
                access.AddWarning("No fill", fill.Info.Length > 0 ? fill.Info : "No fill could be made.");
                return;
            }
            if (linkNote && fill.Links.Count > 0)
                access.AddRemark("Links", fill.Links.Count + " link(s) join separate parts of the fill across open space. The contour fill (Single Line Fill with Offsets) avoids this on shapes with bays or holes.");
            if (holeNote != null) access.AddRemark("Holes", holeNote);
            if (fill.Note.Length > 0) access.AddRemark("Note", fill.Note);
            access.SetItem(0, new PolylineCurve(fill.Path));
        }

        public const string Unused = "No longer needed: the fill is always one continuous line. Kept so that old definitions still open.";
    }

    // ============================================================== Curve Wall and Zigzag
    [IoId("4610df71-034f-48f3-9115-94397605a618")]
    public sealed class CurveWallZigzagComponent : Component
    {
        public CurveWallZigzagComponent() : base(Io.Name("Curve Wall and Zigzag", "One continuous print line from a boundary: one wall loop along the outline, then a zigzag infill at the chosen angle inside it. The curve is the outside of the print.", Io.Slicer, Group.Curves)) { }
        public CurveWallZigzagComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("ContinuousPathFromCurvesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddNumber("Angle", "Angle", "Direction of the infill lines in degrees (0 = along X)").Set(0.0);
            inputs.AddBoolean("Random Bridges", "Random", CurveIo.Unused).Set(true);
            inputs.AddNumber("Bridge Density", "BridgeD", CurveIo.Unused).Set(0.3);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double angle; access.GetItem(3, out angle);
            var fill = FillEngine.FillRegion(boundary, holes, printWidth, false, angle, true, 0.0, null);
            CurveIo.Finish(access, fill, true,
                holes.Count > 0 && fill.Note.Length == 0 ? "Holes are left open but get no wall of their own here. Continuous Toolpath prints a wall around holes." : null);
        }
    }

    // ============================================================== Curve Continuous Path
    [IoId("4630e252-56af-430b-9c86-7fdd1fb80f9c")]
    public sealed class CurveContinuousPathComponent : Component
    {
        public CurveContinuousPathComponent() : base(Io.Name("Curve Continuous Path", "Turns a closed outline into one continuous print line: the wall first, then the inside following the wall inwards, without lifting and without crossing itself. Holes are walked around.", Io.Slicer, Group.Curves)) { }
        public CurveContinuousPathComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("ContinuousToolpathIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddBoolean("Random Bridges", "Random", CurveIo.Unused).Set(true);
            inputs.AddNumber("Bridge Density", "BridgeD", CurveIo.Unused).Set(0.3);
            inputs.AddBoolean("Handle Undercuts", "Undercuts", CurveIo.Unused).Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            CurveIo.Finish(access, FillEngine.FillRegion(boundary, holes, printWidth, true, 0.0, false, 0.0, null));
        }
    }

    // ============================================================== Curve Fill Cells
    [IoId("3ad92d51-90d1-4816-a8fa-a4223368544b")]
    public sealed class CurveFillCellsComponent : Component
    {
        public CurveFillCellsComponent() : base(Io.Name("Curve Fill Cells", "One closed print line made of the wall and honeycomb or grid cells. Every cell is a loop; neighbouring loops lie one print width apart and fuse into double walls; short bridges join everything into a single line that never crosses itself. The curve is the outside of the print.", Io.Slicer, Group.Curves)) { }
        public CurveFillCellsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("InfillCellsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddInteger("Pattern", "P", "0 = Honeycomb, 1 = Grid").Set(0).Choices("Honeycomb", "Grid");
            inputs.AddNumber("Cell Size", "Size", "Distance between the middles of two neighbouring cells (mm)").Set(60.0);
            inputs.AddNumber("Angle", "Angle", "Turns the pattern (degrees)").Set(0.0);
            inputs.AddPoint("Origin", "O", "Fixed point of the pattern (middle of one cell), so the cells line up from layer to layer. Empty = start of the plane of the curve.", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            int pattern; double size, angle;
            access.GetItem(3, out pattern); access.GetItem(4, out size); access.GetItem(5, out angle);
            bool hasOrigin = access.GetItem(6, out Point3d origin) && origin.IsValid;
            var fill = FillEngine.FillCells(boundary, holes, printWidth, pattern, size, angle, hasOrigin ? (Point3d?)origin : null);
            if (fill.Path == null || fill.Path.Count < 2) { access.AddWarning("No fill", fill.Info.Length > 0 ? fill.Info : "No fill could be made."); return; }
            access.AddRemark("Cells", fill.Info);
            access.SetItem(0, new PolylineCurve(fill.Path));
        }
    }

    // ============================================================== Curve Fill Contour
    [IoId("08d009a9-15d3-4d57-877e-94dfa88acb98")]
    public sealed class CurveFillContourComponent : Component
    {
        public CurveFillContourComponent() : base(Io.Name("Curve Fill Contour", "Fills a region with one line that follows the outline inwards (connected Fermat spiral). Works on shapes with bays and holes; the line never crosses itself. The curve is the outside of the print.", Io.Slicer, Group.Curves)) { }
        public CurveFillContourComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("InfillContourIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddBoolean("Use ArcWelder", "ArcWelder", "No longer used: the output is a polyline; use Round Corners (Slicer tab) for smooth corners. Kept so that old definitions still open.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            CurveIo.Finish(access, FillEngine.FillRegion(boundary, holes, printWidth, true, 0.0, false, 0.0, null));
        }
    }

    // ============================================================== Curve Fill Euler
    [IoId("08792648-4033-4ba6-a4e4-9bac718018bc")]
    public sealed class CurveFillEulerComponent : Component
    {
        public CurveFillEulerComponent() : base(Io.Name("Curve Fill Euler", "Fills a region with one line that can be drawn without lifting the pen. Concentric = true follows the outline inwards, false prints a wall loop and a zigzag inside. Offset Distance moves the first bead further inside.", Io.Slicer, Group.Curves)) { }
        public CurveFillEulerComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("InfillEulerTransformationIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddNumber("Offset Distance", "Offset", "Distance of the first bead from the curve (mm). Half the print width puts the outside of the print on the curve.").Set(2.5);
            inputs.AddBoolean("Patch Odd Vertices", "Patch", CurveIo.Unused).Set(true);
            inputs.AddBoolean("Use Concentric Traversal", "Concentric", "True: follow the outline inwards. False: wall loop and zigzag.").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double offset; bool concentric;
            access.GetItem(3, out offset); access.GetItem(5, out concentric);
            CurveIo.Finish(access, FillEngine.FillRegion(boundary, holes, printWidth, concentric, 0.0, !concentric, Math.Max(0.0, offset - 0.5 * printWidth), null));
        }
    }

    // ============================================================== Curve Fill Zigzag
    [IoId("9ff02f7c-5026-44e6-88b4-486b05d58cb0")]
    public sealed class CurveFillZigzagComponent : Component
    {
        public CurveFillZigzagComponent() : base(Io.Name("Curve Fill Zigzag", "Fills a region with one zigzag line at a chosen angle. The curve is the outside of the print; lines are one print width apart. Best for simple shapes - on shapes with bays or holes use Single Line Fill with Offsets.", Io.Slicer, Group.Curves)) { }
        public CurveFillZigzagComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("InfillLinesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            CurveIo.RegionInputs(inputs);
            inputs.AddNumber("Angle", "Angle", "Direction of the fill lines in degrees (0 = along X, 90 = along Y)").Set(0.0);
            inputs.AddBoolean("Optimize Order", "Optimize", CurveIo.Unused).Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs) => CurveIo.RegionOutputs(outputs);

        protected override void Process(IDataAccess access)
        {
            if (!CurveIo.Region(access, out Curve boundary, out double printWidth, out List<Curve> holes)) return;
            double angle; access.GetItem(3, out angle);
            CurveIo.Finish(access, FillEngine.FillRegion(boundary, holes, printWidth, false, angle, false, 0.0, null));
        }
    }

    // ============================================================== Curve Continuous Infill
    [IoId("8a87afdf-f348-46aa-bc97-1d66b7258086")]
    public sealed class CurveContinuousInfillComponent : Component
    {
        public CurveContinuousInfillComponent() : base(Io.Name("Curve Continuous Infill", "Fills a closed boundary with one continuous line that follows the outline inwards and never crosses itself. The line starts next to the seam point. Holes are walked around.", Io.Slicer, Group.Curves)) { }
        public CurveContinuousInfillComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("ContinuousInfillIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Boundary", "Boundary", "Closed boundary curve to fill. It is the outside of the print.");
            inputs.AddNumber("Spacing", "Spacing", "Distance between the lines = bead width in mm").Set(5.0);
            inputs.AddNumber("Boundary Offset", "Offset", "Additional offset from boundary inward (mm)").Set(0.0);
            inputs.AddCurve("Holes", "Holes", "Optional inner boundary curves (holes) to exclude", Access.Twig, Requirement.MayBeMissing);
            inputs.AddPoint("Seam Point", "Seam", "The line starts next to this point. Empty = chosen automatically.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Handle Undercuts", "Undercuts", CurveIo.Unused).Set(true);
            inputs.AddBoolean("Use ArcWelder", "ArcWelder", CurveIo.Unused).Set(false);
            inputs.AddNumber("Arc Tolerance", "ArcTol", CurveIo.Unused).Set(0.1);
            inputs.AddNumber("Min Arc Radius", "MinRadius", CurveIo.Unused).Set(0.1);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Path", "Path", "Continuous toolpath as one polyline", Access.Item);
            outputs.AddCurve("Segments", "Segments", "Links across open space, if the fill could not be made in one piece (normally empty)", Access.Twig);
            outputs.AddPoint("Points", "Points", "All path points", Access.Twig);
            outputs.AddText("Info", "Info", "Path generation information");
        }

        protected override void Process(IDataAccess access)
        {
            var boundary = CurveIo.Curve(access, 0); if (boundary == null) return;
            double spacing, offset; access.GetItem(1, out spacing); access.GetItem(2, out offset);
            var holes = CurveIo.Curves(access, 3);
            bool hasSeam = access.GetItem(4, out Point3d seam) && seam.IsValid;
            if (spacing <= 0) { access.AddError("Invalid spacing", "Spacing must be greater than zero."); return; }
            if (!boundary.IsClosed) access.AddRemark("Open boundary", "The boundary is open; it is closed with a straight line.");
            var fill = FillEngine.FillRegion(boundary, holes, spacing, true, 0.0, false, Math.Max(0.0, offset), hasSeam ? (Point3d?)seam : null);
            if (fill.Path == null || fill.Path.Count < 2) { access.AddWarning("No fill", fill.Info.Length > 0 ? fill.Info : "No fill could be made."); return; }
            if (fill.Note.Length > 0) access.AddRemark("Note", fill.Note);
            access.SetItem(0, new PolylineCurve(fill.Path));
            CurveIo.SetCurves(access, 1, fill.Links.Select(l => (Curve)new LineCurve(l)));
            access.SetTwig(2, fill.Path.ToArray(), null, null);
            access.SetItem(3, fill.Info);
        }
    }
}
