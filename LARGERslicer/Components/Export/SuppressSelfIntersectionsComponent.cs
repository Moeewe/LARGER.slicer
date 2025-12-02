using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Suppress Self Intersections - Heals self-intersecting curves using Euler-Cycle approach.
    /// At each self-intersection, flips curve direction to prevent crossing.
    /// Based on Laurent Delrieu's approach from Nautilus plugin.
    /// </summary>
    public class SuppressSelfIntersectionsComponent : GH_Component
    {
        public SuppressSelfIntersectionsComponent()
            : base("Suppress Self Intersections", "SuppressIX",
                  "Heals self-intersecting curves using Euler-Cycle approach. At each self-intersection, flips curve direction to prevent crossing. Based on Nautilus plugin approach.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Curve that may self-intersect", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance for intersection detection (mm)", GH_ParamAccess.item, 0.01);
            pManager.AddBooleanParameter("Split Segments", "Split", "If true, splits curve into separate segments at intersections. If false, flips direction.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Healed Curves", "H", "Healed curves without self-intersections", GH_ParamAccess.list);
            pManager.AddPointParameter("Intersections", "IX", "Self-intersection points found", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about healing process", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve inputCurve = null;
            double tolerance = 0.01;
            bool splitSegments = false;

            if (!DA.GetData(0, ref inputCurve) || inputCurve == null || !inputCurve.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid input curve.");
                return;
            }

            DA.GetData(1, ref tolerance);
            DA.GetData(2, ref splitSegments);

            // Suppress self-intersections
            var healedCurves = SelfIntersectionHelper.SuppressSelfIntersections(inputCurve, tolerance, splitSegments);

            // Find intersection points for output
            var intersections = new List<Point3d>();
            var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(inputCurve, tolerance);
            if (selfIntersections != null)
            {
                foreach (var intersection in selfIntersections)
                {
                    Point3d pt = inputCurve.PointAt(intersection.ParameterA);
                    intersections.Add(pt);
                }
            }

            // Create info string
            string info = $"Found {intersections.Count} self-intersection(s). ";
            info += $"Healed into {healedCurves.Count} curve segment(s). ";
            if (intersections.Count == 0)
            {
                info += "No self-intersections detected.";
            }

            DA.SetDataList(0, healedCurves);
            DA.SetDataList(1, intersections);
            DA.SetData(2, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SuppressSelfIntersectionsIcon.png");
        public override Guid ComponentGuid => new Guid("58c9301e-90c3-47b3-aa08-01ad3fe41ae8");
    }
}

