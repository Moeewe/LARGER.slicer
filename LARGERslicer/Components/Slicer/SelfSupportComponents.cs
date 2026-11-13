using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>Guide input shared by Self Support Check (same rules as Slice).</summary>
    internal static class SelfSupportIO
    {
        public static Warp Guide(IGH_Goo guideGoo, int guideMode, double tol)
        {
            if (guideGoo == null) return null;
            object guide = guideGoo.ScriptVariable();
            if (guide is Line gl) guide = new LineCurve(gl);
            if (guide is Curve gc) { var cw = new CurveWarp(gc, true); return cw.Valid ? cw : null; }
            Mesh gm = SliceEngine.ToMesh(new[] { guide }, tol, null);
            if (gm == null) return null;
            var sw = new SurfaceWarp(gm, guideMode == 0, true);
            return sw.Valid ? sw : null;
        }
    }

    // ============================================================== self support check
    public class SelfSupportCheckComponent : SlicerComponent
    {
        public SelfSupportCheckComponent()
            : base("Self Support Check", "SelfSup",
                  "Shows where the part is not self-supporting for the build direction and bead. A bead may lie Factor x bead width beside the bead below, so walls may lean out atan(that / layer height) from the build direction (5 mm bead, 2 mm layer: 51 deg). Red = hangs over, orange = near the limit, green = fine, grey = on the bed.") { }

        public override Guid ComponentGuid => new Guid("14890845-5658-4603-AC6E-1A3E5683607A");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSelfSupportCheckIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings (bead width, layer height)", GH_ParamAccess.item);
            p.AddPlaneParameter("Plane", "P", "Layer plane, as on Slice. Its normal is the build direction", GH_ParamAccess.item, Plane.WorldXY);
            p.AddNumberParameter("Angle", "A", "Tilts the layers around the plane's X axis (degrees), as on Slice", GH_ParamAccess.item, 0.0);
            p.AddGenericParameter("Guide", "Gd", "Optional, as on Slice. A curve: the build direction is its tangent. A surface: the build direction is the normal of the bent layer", GH_ParamAccess.item);
            p.AddIntegerParameter("Guide Mode", "GM", "Only with a guide surface, as on Slice:\n0 = Flat bottom, top follows the guide\n1 = All layers follow the guide", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Factor", "F", "How far a bead may lie beside the bead below, as a share of the bead width. 0.5 = half a bead (as Path Check)", GH_ParamAccess.item, SelfSupport.DefaultFactor);
            p[1].Optional = true; p[4].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddAngle(doc, 3, "Angle", 0, 90, 0);
            AddSlider(doc, 6, "Factor", 0.2m, 0.8m, 0.5m, 2, "", new double[] { 0.25, 0.4, 0.5, 0.6, 0.75 });
            AddValueList(doc, 5, "Guide Mode", "Flat bottom, top follows guide", "All layers follow guide");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Analysis Mesh", "M", "The part coloured: green fine, orange near the limit, red not self-supporting, grey on the bed", GH_ParamAccess.item);
            p.AddNumberParameter("Unsupported Area", "U", "Area that hangs over more than the limit (mm2)", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was found", GH_ParamAccess.list);
            p.AddMeshParameter("Regions", "Rg", "The unsupported areas, one mesh per region", GH_ParamAccess.list);
            p.AddPointParameter("Starts", "St", "Places where an island starts in mid air (nothing below it)", GH_ParamAccess.list);
            p.AddNumberParameter("Percent", "%", "Unsupported area in % of the surface", GH_ParamAccess.item);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var geoIn = new List<object>();
            if (!da.GetDataList(0, geoIn) || geoIn.Count == 0) return;
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            Plane plane = Plane.WorldXY; double angle = 0, factor = SelfSupport.DefaultFactor; int guideMode = 0;
            IGH_Goo guideGoo = null;
            da.GetData(2, ref plane); da.GetData(3, ref angle); da.GetData(4, ref guideGoo); da.GetData(5, ref guideMode); da.GetData(6, ref factor);
            if (Math.Abs(angle) > 1e-9) plane.Rotate(Rhino.RhinoMath.ToRadians(angle), plane.XAxis, plane.Origin);
            // copies for the background
            var geo = LARGERslicer.Components.AsyncSolve.Copy(geoIn);
            IGH_Goo guideCopy = guideGoo == null ? null : new GH_ObjectWrapper(LARGERslicer.Components.AsyncSolve.Copy(guideGoo));

            Async.Run(da, job =>
            {
                double tol = Math.Max(0.01, settings.Resolution * 0.1);
                Warp warp = SelfSupportIO.Guide(guideCopy, guideMode, tol);
                if (guideGoo != null && warp == null) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The guide could not be used. Checking with the plane.");

                var log = new List<string>();
                Mesh mesh = SliceEngine.ToMesh(geo, tol, log, warp != null ? Math.Max(4.0, settings.BeadWidth * 3) : 0);
                if (mesh == null) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable geometry. Connect Breps, surfaces, SubDs or meshes."); return; }
                foreach (var l in log) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
                var r = SelfSupport.Check(mesh, plane, warp, settings, factor, true);
                job.Message = r.UnsupportedArea > 0 ? $"{r.Percent:0.0} % hangs over" : (r.Starts.Count > 0 ? "mid-air start" : "self-supporting");
                if (!r.Ok) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.FirstOrDefault(l => l.StartsWith("NOT") || l.Contains("mid air")) ?? "Not self-supporting.");
                job.SetData(0, r.Analysis);
                job.SetData(1, r.UnsupportedArea);
                job.SetDataList(2, r.Report);
                job.SetDataList(3, r.Regions);
                job.SetDataList(4, r.Starts);
                job.SetData(5, r.Percent);
            });
        }
    }

    // ============================================================== self-supporting envelope
    public class SelfSupportEnvelopeComponent : SlicerComponent
    {
        public SelfSupportEnvelopeComponent()
            : base("Self Supporting Envelope", "Envelope",
                  "Makes the layers self-supporting by adding as little material as possible: from the top down, every layer must hold the layer above, shrunk by Factor x bead width. Like automatic chamfers under overhangs, measured with the real bead and layer. Gives the new layers (slice-ready), the new part as NURBS (loft) or mesh, and the added material alone, to mill away later.") { }

        public override Guid ComponentGuid => new Guid("2B61FC3E-692F-4FD7-B3A6-F2683F342DE6");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerEnvelopeIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Slice (before Walls)", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings (bead width, layer height)", GH_ParamAccess.item);
            p.AddNumberParameter("Factor", "F", "How far a layer may reach beyond the layer below, as a share of the bead width. 0.5 = half a bead (as Path Check)", GH_ParamAccess.item, SelfSupport.DefaultFactor);
            p.AddBooleanParameter("Geometry", "Geo", "True = also build the new part and the added material as geometry (a loft takes a moment)", GH_ParamAccess.item, true);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 2, "Factor", 0.2m, 0.8m, 0.5m, 2, "", new double[] { 0.25, 0.4, 0.5, 0.6, 0.75 });
            AddToggle(doc, 3, "Geometry", true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Self-supporting layers. Connect them to Walls like the layers of Slice", GH_ParamAccess.list);
            p.AddGeometryParameter("Geometry", "G", "The self-supporting part: a closed NURBS Brep where every layer is one loop, a mesh of the layer stack otherwise", GH_ParamAccess.item);
            p.AddGeometryParameter("Added", "A", "Only the added material (Brep or mesh), to see what is added and mill it away later", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was added, and the loft tolerance", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Contours of the new layers (for checking)", GH_ParamAccess.list);
            p.AddCurveParameter("Added Contours", "AC", "Contours of the added material per layer", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Slice."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            double factor = SelfSupport.DefaultFactor; bool geometry = true;
            da.GetData(2, ref factor); da.GetData(3, ref geometry);
            var sw = Stopwatch.StartNew();
            var r = SelfSupport.Envelope(layers, settings, factor, geometry);
            sw.Stop();
            if (r.Layers.Count == 0 || r.Report.Any(l => l.StartsWith("These layers")))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.FirstOrDefault() ?? "No layers.");
            Message = r.ChangedLayers == 0 ? "nothing added" : $"+{r.AddedVolume / 1000.0:0.0} cm3";
            r.Report.Add($"{r.Layers.Count} layers, {sw.ElapsedMilliseconds} ms.");
            da.SetDataList(0, r.Layers.Select(l => new GH_SliceLayer(l)));
            da.SetData(1, r.Geometry);
            da.SetData(2, r.AddedGeometry);
            da.SetDataList(3, r.Report);
            da.SetDataList(4, r.Layers.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetDataList(5, r.Added.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
        }
    }

    // ============================================================== best build direction
    public class BestAngleComponent : SlicerComponent
    {
        public BestAngleComponent()
            : base("Best Angle", "BestAng",
                  "Finds the slice angle and tilt direction with the least area that is not self-supporting. Tries every angle in the range in 5 degree steps and, with Turn, every tilt direction in 30 degree steps. Ties go to the layers that follow the part most closely, then to the smaller angle and the lower part. Connect Plane and Angle to Slice.") { }

        public override Guid ComponentGuid => new Guid("BC0AD6F4-FE4C-47BB-86A4-3AB699E040DE");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerBestAngleIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings (bead width, layer height)", GH_ParamAccess.item);
            p.AddNumberParameter("Min Angle", "A0", "Smallest slice angle to try (degrees)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Angle", "A1", "Largest slice angle to try (degrees)", GH_ParamAccess.item, 75.0);
            p.AddBooleanParameter("Turn", "T", "True = also try every tilt direction (rotation about Z, 30 degree steps). False = only tilt as Slice does (about X)", GH_ParamAccess.item, true);
            p.AddNumberParameter("Factor", "F", "How far a bead may lie beside the bead below, as a share of the bead width", GH_ParamAccess.item, SelfSupport.DefaultFactor);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddAngle(doc, 2, "Min Angle", 0, 90, 0);
            AddAngle(doc, 3, "Max Angle", 0, 90, 75);
            AddToggle(doc, 4, "Turn", true);
            AddSlider(doc, 5, "Factor", 0.2m, 0.8m, 0.5m, 2, "", new double[] { 0.25, 0.4, 0.5, 0.6, 0.75 });
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPlaneParameter("Plane", "P", "Base plane turned to the best tilt direction. Connect it with Angle to Slice", GH_ParamAccess.item);
            p.AddNumberParameter("Angle", "A", "Best slice angle (degrees)", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Best direction and ranking", GH_ParamAccess.list);
            p.AddPlaneParameter("Layer Plane", "LP", "The tilted layer plane itself (Plane tilted by Angle)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var geo = new List<object>();
            if (!da.GetDataList(0, geo) || geo.Count == 0) return;
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            double a0 = 0, a1 = 75, factor = SelfSupport.DefaultFactor; bool turn = true;
            da.GetData(2, ref a0); da.GetData(3, ref a1); da.GetData(4, ref turn); da.GetData(5, ref factor);
            var sw = Stopwatch.StartNew();
            // coarse mesh: the score only needs face areas and normals
            Mesh mesh = SliceEngine.ToMesh(geo, Math.Max(0.5, 0.25 * settings.BeadWidth), null);
            if (mesh == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable geometry. Connect Breps, surfaces, SubDs or meshes."); return; }
            var r = SelfSupport.BestDirection(mesh, settings, factor, a0, a1, 5, 30, turn);
            sw.Stop();
            r.Report.Add($"{mesh.Faces.Count} faces, {sw.ElapsedMilliseconds} ms.");
            Message = $"{r.Angle:0} deg" + (r.Angle > 0 && turn ? $", turned {r.Direction:0}" : "");
            da.SetData(0, r.Plane);
            da.SetData(1, r.Angle);
            da.SetDataList(2, r.Report);
            da.SetData(3, r.LayerPlane);
        }
    }
}
