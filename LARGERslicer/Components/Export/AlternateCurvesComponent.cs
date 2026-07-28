using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Alternate Curves - Alternates curve directions (left-right-left-right pattern).
    /// Useful for creating zigzag patterns and preventing overfill in continuous toolpaths.
    /// </summary>
    public class AlternateCurvesComponent : GH_Component
    {
        public AlternateCurvesComponent()
            : base("Alternate Curves", "AltCrv",
                  "Alternates curve directions (left-right-left-right pattern). Useful for creating zigzag patterns and preventing overfill in continuous toolpaths.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Curve Tools";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to alternate", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Start Left", "Left", "If true, first curve points left. If false, first curve points right.", GH_ParamAccess.item, true);
            pManager.AddIntegerParameter("Group Size", "Group", "Number of consecutive curves with same direction before alternating (1 = alternate each, 2 = pairs, etc.)", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Alternated Curves", "C", "Curves with alternating directions", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Flipped", "F", "True if curve was flipped, False if original direction", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about alternation", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var curves = new List<Curve>();
            bool startLeft = true;
            int groupSize = 1;

            if (!DA.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No curves provided.");
                return;
            }

            DA.GetData(1, ref startLeft);
            DA.GetData(2, ref groupSize);

            // Filter valid curves
            var validCurves = curves.Where(c => c != null && c.IsValid).ToList();
            if (validCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid curves found.");
                return;
            }

            // Clamp group size
            if (groupSize < 1) groupSize = 1;

            Vector3d alternationAxis = GetAlternationAxis(validCurves);

            var alternatedCurves = new List<Curve>();
            var flippedFlags = new List<bool>();
            bool currentDirection = startLeft; // true = left, false = right
            int currentGroupCount = 0;

            foreach (var curve in validCurves)
            {
                // Check if we need to switch direction
                if (currentGroupCount >= groupSize)
                {
                    currentDirection = !currentDirection;
                    currentGroupCount = 0;
                }

                // Determine if curve should point left or right
                Vector3d curveDirection = GetCurveDirection(curve);
                bool curvePointsLeft = IsLeftDirection(curveDirection, alternationAxis);

                // Flip if direction doesn't match desired direction
                bool shouldFlip = (curvePointsLeft != currentDirection);
                
                Curve alternated = curve.DuplicateCurve();
                if (shouldFlip)
                {
                    alternated.Reverse();
                }
                alternatedCurves.Add(alternated);
                flippedFlags.Add(shouldFlip);

                currentGroupCount++;
            }

            string info = $"Alternated {validCurves.Count} curves. ";
            info += $"Pattern: {(startLeft ? "Left" : "Right")} first, group size: {groupSize}. ";
            int flippedCount = flippedFlags.Count(f => f);
            info += $"{flippedCount} curve(s) flipped.";

            DA.SetDataList(0, alternatedCurves);
            DA.SetDataList(1, flippedFlags);
            DA.SetData(2, info);
        }

        /// <summary>
        /// Gets the direction vector of a curve (from start to end).
        /// For closed curves, uses tangent at start point.
        /// </summary>
        private Vector3d GetCurveDirection(Curve curve)
        {
            if (curve == null || !curve.IsValid)
                return Vector3d.Zero;

            // For closed curves, use tangent at start point
            if (curve.IsClosed)
            {
                Vector3d tangent = curve.TangentAtStart;
                if (tangent.IsValid && tangent.Length > 0.001)
                {
                    tangent.Unitize();
                    return tangent;
                }
            }

            // For open curves, use direction from start to end
            Point3d start = curve.PointAtStart;
            Point3d end = curve.PointAtEnd;
            Vector3d direction = end - start;
            
            // If start and end are the same (degenerate curve), use tangent
            if (direction.Length < 0.001)
            {
                Vector3d tangent = curve.TangentAtStart;
                if (tangent.IsValid && tangent.Length > 0.001)
                {
                    tangent.Unitize();
                    return tangent;
                }
                return Vector3d.Zero;
            }
            
            // Normalize
            direction.Unitize();
            return direction;
        }

        /// <summary>
        /// Builds a stable horizontal axis from the first usable curve direction.
        /// Falls back to World X when no horizontal direction can be extracted.
        /// </summary>
        private Vector3d GetAlternationAxis(List<Curve> curves)
        {
            foreach (var c in curves)
            {
                Vector3d d = GetCurveDirection(c);
                Vector3d xy = new Vector3d(d.X, d.Y, 0);
                if (xy.Length >= 0.001)
                {
                    xy.Unitize();
                    return xy;
                }
            }

            return Vector3d.XAxis;
        }

        /// <summary>
        /// Determines if a direction vector points left relative to a given horizontal axis.
        /// </summary>
        private bool IsLeftDirection(Vector3d direction, Vector3d axis)
        {
            // Project to XY plane
            Vector3d xyDirection = new Vector3d(direction.X, direction.Y, 0);
            if (xyDirection.Length < 0.001)
                return false; // Vertical or zero vector

            Vector3d xyAxis = new Vector3d(axis.X, axis.Y, 0);
            if (xyAxis.Length < 0.001)
                xyAxis = Vector3d.XAxis;
            else
                xyAxis.Unitize();
            
            // Negative projection means opposite to axis => "left".
            return (xyDirection * xyAxis) < 0;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("AlternateCurvesIcon.png");
        public override Guid ComponentGuid => new Guid("00f7adb2-9f77-4a8a-a5d9-4e00871797d7");
    }
}

