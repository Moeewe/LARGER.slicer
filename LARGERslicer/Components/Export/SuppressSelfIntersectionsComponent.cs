using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Removes self-crossings from a print line. Where two parts of the line cross, the stretch
    /// in between is run the other way round, so the line stays one line and no longer crosses.
    /// </summary>
    public class SuppressSelfIntersectionsComponent : GH_Component
    {
        public SuppressSelfIntersectionsComponent()
            : base("Curve Uncross", "C Uncross",
                  "Removes the crossings of a line with itself. By default the line stays one continuous line: the stretch between two crossing parts is run the other way round. With Split the line is cut at the crossings instead.",
                  "LARGER", "Slicer")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Slicer · Curves";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Curve that may self-intersect", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Accuracy with which curved input is turned into a polyline (mm)", GH_ParamAccess.item, 0.01);
            pManager.AddBooleanParameter("Split Segments", "Split", "False: keep one continuous line without crossings. True: cut the curve into pieces at the crossings.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Healed Curves", "H", "Healed curves without self-intersections", GH_ParamAccess.list);
            pManager.AddPointParameter("Intersections", "IX", "Self-intersection points found", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about healing process", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve curve = null; double tol = 0.01; bool split = false;
            if (!DA.GetData(0, ref curve) || curve == null) return;
            DA.GetData(1, ref tol); DA.GetData(2, ref split);
            tol = Math.Max(tol, 1e-4);

            Polyline pl;
            if (!curve.TryGetPolyline(out pl))
            {
                var pc = curve.ToPolyline(tol, Math.PI / 90.0, 0, 0);
                if (pc == null || !pc.TryGetPolyline(out pl)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The curve could not be turned into a polyline."); return; }
            }

            if (split)
            {
                var events = Rhino.Geometry.Intersect.Intersection.CurveSelf(curve, tol);
                var ts = new List<double>(); var pts = new List<Point3d>();
                if (events != null) foreach (var e in events) { ts.Add(e.ParameterA); ts.Add(e.ParameterB); pts.Add(e.PointA); }
                Curve[] pieces = ts.Count > 0 ? curve.Split(ts) : null;
                DA.SetDataList(0, pieces != null && pieces.Length > 0 ? pieces : new[] { curve });
                DA.SetDataList(1, pts);
                DA.SetData(2, pts.Count == 0 ? "No self-crossings." : pts.Count + " crossing(s), cut into " + (pieces?.Length ?? 1) + " pieces.");
                return;
            }

            var healed = PathTools.Uncross(pl, tol, out int moves, out List<Point3d> crossings);
            int left = moves > 0 ? PathTools.CountCrossings(healed) : 0;
            if (left > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, left + " crossing(s) could not be removed.");
            Message = moves == 0 ? "clean" : moves + " fixed";
            DA.SetDataList(0, new[] { moves == 0 ? curve : new PolylineCurve(healed) });
            DA.SetDataList(1, crossings);
            DA.SetData(2, moves == 0 ? "No self-crossings." : moves + " crossing(s) removed; the line is still one line" + (left > 0 ? ", " + left + " left" : "") + ".");
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SuppressSelfIntersectionsIcon.png", this);
        public override Guid ComponentGuid => new Guid("58c9301e-90c3-47b3-aa08-01ad3fe41ae8");
        /// <summary>Curve tools: the last group of the Slicer tab, for your own curves.</summary>
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
    }
}
