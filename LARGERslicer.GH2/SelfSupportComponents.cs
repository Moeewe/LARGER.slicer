using System;
using System.Collections.Generic;
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
    internal static class SelfSupportIo
    {
        public static Warp Guide(object guide, int guideMode, double tol)
        {
            if (guide == null) return null;
            if (guide is Line gl) guide = new LineCurve(gl);
            if (guide is Polyline gp) guide = new PolylineCurve(gp);
            if (guide is Arc ga) guide = new ArcCurve(ga);
            if (guide is Curve gc) { var cw = new CurveWarp(gc, true); return cw.Valid ? cw : null; }
            Mesh gm = SliceEngine.ToMesh(new[] { Io.Solid(guide) }, tol, null);
            if (gm == null) return null;
            var sw = new SurfaceWarp(gm, guideMode == 0, true);
            return sw.Valid ? sw : null;
        }
    }

    // ============================================================== self support check
    [IoId("7a38200c-c413-44cc-afe1-c727eb4d16df")]
    public sealed class SelfSupportCheckComponent : Component
    {
        public SelfSupportCheckComponent() : base(Io.Name("Self Support Check", "Shows where the part is not self-supporting for the build direction and bead. A bead may lie Factor x bead width beside the bead below, so walls may lean out atan(that / layer height) from the build direction (5 mm bead, 2 mm layer: 51 deg). Red = hangs over, orange = near the limit, green = fine, grey = on the bed.", Io.Slicer, Group.Layers)) { }
        public SelfSupportCheckComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerSelfSupportCheckIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes.", Access.Twig);
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Angle", "A", "Tilts the layers around the X axis, in degrees, as on Slice.").Set(0.0);
            inputs.AddGeneric("Guide", "Gd", "Optional, as on Slice. A curve: the build direction is its tangent. A surface: the normal of the bent layer.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Guide Mode", "GM", "Only with a guide surface: 0 = flat bottom, top follows the guide. 1 = all layers follow the guide.").Set(0).Choices("Flat bottom, top follows guide", "All layers follow guide");
            inputs.AddNumber("Factor", "F", "How far a bead may lie beside the bead below, as a share of the bead width. 0.5 = half a bead.").Set(SelfSupport.DefaultFactor);
            inputs.AddPlane("Plane", "P", "Layer plane, as on Slice. Its normal is the build direction. Empty = World XY.", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddMesh("Analysis Mesh", "M", "Green fine, orange near the limit, red not self-supporting, grey on the bed.");
            outputs.AddNumber("Unsupported Area", "U", "Area that hangs over more than the limit (mm2).");
            outputs.AddText("Report", "R", "What was found.", Access.Twig);
            outputs.AddMesh("Regions", "Rg", "The unsupported areas, one mesh per region.", Access.Twig);
            outputs.AddPoint("Starts", "St", "Places where an island starts in mid air.", Access.Twig);
            outputs.AddNumber("Percent", "%", "Unsupported area in % of the surface.");
        }

        protected override void Process(IDataAccess access)
        {
            object[] geo;
            if (!access.ItemArray(0, out geo) || geo == null || geo.Length == 0) return;
            var settings = Io.Settings(access, 1);
            double angle, factor; object guide; int guideMode;
            access.GetItem(2, out angle); access.GetItem(3, out guide); access.GetItem(4, out guideMode); access.GetItem(5, out factor);
            Plane plane;
            if (!access.GetItem(6, out plane) || !plane.IsValid) plane = Plane.WorldXY;
            if (Math.Abs(angle) > 1e-9) plane.Rotate(Rhino.RhinoMath.ToRadians(angle), plane.XAxis, plane.Origin);
            double tol = Math.Max(0.01, settings.Resolution * 0.1);
            Warp warp = SelfSupportIo.Guide(guide, guideMode, tol);
            if (guide != null && warp == null) access.AddWarning("Guide not usable", "Checking with the plane.");
            var log = new List<string>();
            if (Io.Step(access, 5)) return; // progress; stop if the inputs changed meanwhile
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), tol, log, warp != null ? Math.Max(4.0, settings.BeadWidth * 3) : 0);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            Io.Remarks(access, log);
            if (Io.Step(access, 40)) return; // progress; stop if the inputs changed meanwhile
            var r = SelfSupport.Check(mesh, plane, warp, settings, factor, true);
            if (!r.Ok) access.AddWarning("Not self-supporting", string.Join("\n", r.Report));
            access.SetItem(0, r.Analysis);
            access.SetItem(1, r.UnsupportedArea);
            access.SetTwig(2, r.Report.ToArray(), null, null);
            access.SetTwig(3, r.Regions.ToArray(), null, null);
            access.SetTwig(4, r.Starts.ToArray(), null, null);
            access.SetItem(5, r.Percent);
        }
    }

    // ============================================================== self-supporting envelope
    [IoId("75aa2165-bbff-4f7c-b84a-ac05af2c9185")]
    public sealed class SelfSupportEnvelopeComponent : Component
    {
        public SelfSupportEnvelopeComponent() : base(Io.Name("Self Supporting Envelope", "Makes the layers self-supporting with as little added material as possible: from the top down, every layer must hold the layer above, shrunk by Factor x bead width. Gives the new layers, the new part as NURBS (loft) or mesh, and the added material alone.", Io.Slicer, Group.Layers)) { }
        public SelfSupportEnvelopeComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerEnvelopeIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers from Slice (before Walls).");
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Factor", "F", "How far a layer may reach beyond the layer below, as a share of the bead width. 0.5 = half a bead.").Set(SelfSupport.DefaultFactor);
            inputs.AddBoolean("Geometry", "Geo", "True = also build the new part and the added material as geometry.").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Self-supporting layers, for Walls.");
            outputs.AddGeneric("Geometry", "G", "The self-supporting part: a closed NURBS Brep where every layer is one loop, a mesh otherwise.");
            outputs.AddGeneric("Added", "A", "Only the added material (Brep or mesh).");
            outputs.AddText("Report", "R", "What was added, and the loft tolerance.", Access.Twig);
            outputs.AddPolyline("Contours", "C", "Contours of the new layers.", Access.Twig);
            outputs.AddPolyline("Added Contours", "AC", "Contours of the added material per layer.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var settings = Io.Settings(access, 1);
            double factor; bool geometry;
            access.GetItem(2, out factor); access.GetItem(3, out geometry);
            var r = SelfSupport.Envelope(layers, settings, factor, geometry);
            if (r.Layers.Count == 0 || r.Report.Any(l => l.StartsWith("These layers"))) access.AddWarning("Envelope", r.Report.FirstOrDefault() ?? "No layers.");
            access.SetItem(0, new LayerStack(r.Layers));
            if (r.Geometry != null) access.SetItem(1, r.Geometry);
            if (r.AddedGeometry != null) access.SetItem(2, r.AddedGeometry);
            access.SetTwig(3, r.Report.ToArray(), null, null);
            Io.Contours(access, 4, r.Layers);
            Io.Contours(access, 5, r.Added);
        }
    }

    // ============================================================== best build direction
    [IoId("6bf35e8d-bb34-4c2b-ab8a-f4b43aa521a9")]
    public sealed class BestAngleComponent : Component
    {
        public BestAngleComponent() : base(Io.Name("Best Angle", "Finds the slice angle and tilt direction with the least area that is not self-supporting: every angle in the range in 5 degree steps and, with Turn, every tilt direction in 30 degree steps. Connect Plane and Angle to Slice (turn the part or the bed by the plane).", Io.Slicer, Group.Layers)) { }
        public BestAngleComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerBestAngleIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes.", Access.Twig);
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Min Angle", "A0", "Smallest slice angle to try, in degrees.").Set(0.0);
            inputs.AddNumber("Max Angle", "A1", "Largest slice angle to try, in degrees.").Set(75.0);
            inputs.AddBoolean("Turn", "T", "True = also try every tilt direction (30 degree steps about Z).").Set(true);
            inputs.AddNumber("Factor", "F", "How far a bead may lie beside the bead below, as a share of the bead width.").Set(SelfSupport.DefaultFactor);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddPlane("Plane", "P", "Base plane turned to the best tilt direction.");
            outputs.AddNumber("Angle", "A", "Best slice angle in degrees.");
            outputs.AddText("Report", "R", "Best direction and ranking.", Access.Twig);
            outputs.AddPlane("Layer Plane", "LP", "The tilted layer plane itself.");
        }

        protected override void Process(IDataAccess access)
        {
            object[] geo;
            if (!access.ItemArray(0, out geo) || geo == null || geo.Length == 0) return;
            var settings = Io.Settings(access, 1);
            double a0, a1, factor; bool turn;
            access.GetItem(2, out a0); access.GetItem(3, out a1); access.GetItem(4, out turn); access.GetItem(5, out factor);
            Mesh mesh = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), Math.Max(0.5, 0.25 * settings.BeadWidth), null);
            if (mesh == null) { access.AddError("No usable geometry", "Connect Breps, surfaces, SubDs or meshes."); return; }
            var r = SelfSupport.BestDirection(mesh, settings, factor, a0, a1, 5, 30, turn);
            access.SetItem(0, r.Plane);
            access.SetItem(1, r.Angle);
            access.SetTwig(2, r.Report.ToArray(), null, null);
            access.SetItem(3, r.LayerPlane);
        }
    }
}
