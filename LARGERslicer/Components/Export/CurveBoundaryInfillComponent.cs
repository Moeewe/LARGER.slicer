using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Curve Boundary Infill Component - Generates boustrophedon (zigzag) infill pattern from curves.
    /// Takes the first curve as outer contour on Z-axis, offsets it inward by layer width,
    /// then generates cross-lines at configurable angle to connect the offset curves.
    /// Similar to CNC Program approach but adapted for 3D printing toolpaths.
    /// </summary>
    public class CurveBoundaryInfillComponent : GH_Component
    {
        public CurveBoundaryInfillComponent()
            : base("Curve Fill Cross Lines", "C Cross",
                  "Generates boustrophedon infill pattern. Takes first curve as outer contour, offsets inward by layer width, then connects with cross-lines at configurable angle. Works for first layer and planar layers.",
                  "LARGER", "Slicer")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Individual boundary curves. The first curve will be used as the outer contour on Z-axis.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Width", "LW", "Layer width (extrusion width/bead width in mm). Used for inward offset spacing and cross-line spacing.", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Z Height", "Z", "Z height for planar layers (mm). For first layer, use 0 or layer height.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Angle", "Angle", "Angle for cross-lines (connection lines) in degrees. 0 = horizontal, 90 = vertical.", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Start Left", "StartLeft", "True to start from left, False to start from right", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Boundary Offset", "BO", "Additional offset from boundary (mm). Positive = inward, negative = outward. Default: 0 (uses half layer width).", GH_ParamAccess.item, 0.0);
            pManager[5].Optional = true;
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", GH_ParamAccess.list);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "Boundary", "Outer boundary curve that exactly matches the connection lines of input curves", GH_ParamAccess.item);
            pManager.AddCurveParameter("Infill Path", "Path", "Complete continuous infill path", GH_ParamAccess.item);
            pManager.AddCurveParameter("Offset Curves", "Offsets", "Individual offset curves used for infill", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            List<Curve> inputCurves = new List<Curve>();
            if (!DA.GetDataList(0, inputCurves) || inputCurves == null || inputCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one curve is required.");
                return;
            }

            // Filter valid curves
            inputCurves = inputCurves.Where(c => c != null && c.IsValid).ToList();
            if (inputCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid curves found.");
                return;
            }

            double layerWidth = 5.0;
            if (!DA.GetData(1, ref layerWidth) || layerWidth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer width must be greater than zero.");
                return;
            }

            double zHeight = 0.0;
            DA.GetData(2, ref zHeight);

            double angle = 0.0;
            DA.GetData(3, ref angle);

            bool startLeft = true;
            DA.GetData(4, ref startLeft);

            double boundaryOffset = 0.0;
            DA.GetData(5, ref boundaryOffset);

            List<Curve> holes = new List<Curve>();
            DA.GetDataList(6, holes);
            holes = holes?.Where(h => h != null && h.IsValid).ToList() ?? new List<Curve>();

            // Step 1: Use first curve as boundary (on Z-axis)
            Curve boundary = GetFirstCurveAsBoundary(inputCurves, zHeight);
            if (boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to create boundary from curves.");
                return;
            }

            // Step 2: the wall bead: the given offset, or half a layer width inward
            Curve wall = Inset(boundary, boundaryOffset != 0.0 ? boundaryOffset : layerWidth * 0.5);
            if (wall != null) boundary = wall;

            // Step 3: the cross lines end one layer width inside the wall bead. The fill engine stays half a
            // layer width inside the region it gets, and half a layer width away from the holes.
            Curve region = Inset(boundary, layerWidth * 0.5);
            RegionFillResult fill = region == null ? null : FillEngine.FillRegion(region, holes, layerWidth, false, angle, false, 0.0, null);
            if (fill?.Path == null || fill.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No cross lines fit. Boundary may be too small.");
                // Return boundary as path
                DA.SetData(0, boundary);
                DA.SetData(1, boundary);
                DA.SetDataList(2, new List<Curve> { boundary });
                return;
            }
            if (fill.Note.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Note);
            if (fill.Links.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, fill.Links.Count + " link(s) join separate parts of the fill across open space.");

            // Step 4: at the Z height, beginning left or right (along the lines)
            var path = new Polyline(fill.Path.Select(q => new Point3d(q.X, q.Y, zHeight)));
            var along = new Vector3d(Math.Cos(angle * Math.PI / 180.0), Math.Sin(angle * Math.PI / 180.0), 0);
            bool beginsLeft = (path[0] - path[1]) * along <= 0;
            if (beginsLeft != startLeft) path.Reverse();

            // the single cross lines (the whole path where the fill follows the outline instead)
            var rows = new List<Curve>();
            for (int i = 1; i < path.Count; i++)
            {
                Vector3d step = path[i] - path[i - 1];
                if (step.Length > 0.01 && Math.Abs(step * along) > 0.999 * step.Length) rows.Add(new LineCurve(path[i - 1], path[i]));
            }
            if (rows.Count == 0) rows.Add(new PolylineCurve(path));

            // Set outputs
            DA.SetData(0, boundary);
            DA.SetData(1, new PolylineCurve(path));
            DA.SetDataList(2, rows);
        }

        /// <summary>The curve moved inward by a distance (negative = outward), whatever its direction; null = nothing left.</summary>
        private static Curve Inset(Curve curve, double distance)
        {
            double area = AreaMassProperties.Compute(curve)?.Area ?? 0.0;
            Curve best = null;
            double bestArea = 0.0;
            foreach (double side in new[] { -distance, distance })
            {
                var offsets = curve.Offset(Plane.WorldXY, side, 0.01, CurveOffsetCornerStyle.Sharp);
                if (offsets == null) continue;
                foreach (var c in offsets)
                {
                    if (c == null || !c.IsValid || !c.IsClosed) continue;
                    double a = AreaMassProperties.Compute(c)?.Area ?? 0.0;
                    bool inside = a < area;
                    if (a <= 0 || inside != distance > 0) continue;
                    if (a > bestArea) { best = c; bestArea = a; }
                }
            }
            return best;
        }

        /// <summary>
        /// Gets the first curve as boundary, normalized to Z height.
        /// The first curve on Z-axis is used as the outer contour.
        /// </summary>
        private Curve GetFirstCurveAsBoundary(List<Curve> curves, double zHeight)
        {
            if (curves == null || curves.Count == 0)
                return null;

            // Use first curve as boundary
            Curve first = curves[0].DuplicateCurve();
            if (!first.IsClosed)
            {
                first.MakeClosed(0.01);
            }

            // Set Z height
            Transform moveZ = Transform.Translation(0, 0, zHeight - first.PointAtStart.Z);
            first.Transform(moveZ);

            return first;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CurveBoundaryInfillIcon.png", this);
        public override Guid ComponentGuid => new Guid("C1B2A3D4-E5F6-7890-ABCD-EF1234567890");
        /// <summary>Curve tools: the last group of the Slicer tab, for your own curves.</summary>
        public override GH_Exposure Exposure => GH_Exposure.septenary | GH_Exposure.obscure;
    }
}








