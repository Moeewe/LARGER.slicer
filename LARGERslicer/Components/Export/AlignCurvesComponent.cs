using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Align Curves - Flips all curves to point in the same direction.
    /// Ensures consistent orientation for continuous toolpath generation.
    /// </summary>
    public class AlignCurvesComponent : GH_Component
    {
        public AlignCurvesComponent()
            : base("Align Curves", "AlignCrv",
                  "Flips all curves to point in the same direction. Ensures consistent orientation for continuous toolpath generation.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Curve Tools";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to align", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Reference Index", "Ref", "Index of reference curve (0 = first curve). All other curves will be aligned to this one.", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Reverse", "Rev", "If true, reverses all curves. If false, aligns to reference direction.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Aligned Curves", "C", "Curves aligned to same direction", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Flipped", "F", "True if curve was flipped, False if original direction", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about alignment", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var curves = new List<Curve>();
            int referenceIndex = 0;
            bool reverse = false;

            if (!DA.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No curves provided.");
                return;
            }

            DA.GetData(1, ref referenceIndex);
            DA.GetData(2, ref reverse);

            // Filter valid curves
            var validCurves = curves.Where(c => c != null && c.IsValid).ToList();
            if (validCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid curves found.");
                return;
            }

            // Clamp reference index
            if (referenceIndex < 0) referenceIndex = 0;
            if (referenceIndex >= validCurves.Count) referenceIndex = validCurves.Count - 1;

            var alignedCurves = new List<Curve>();
            var flippedFlags = new List<bool>();

            if (reverse)
            {
                // Reverse all curves
                foreach (var curve in validCurves)
                {
                    Curve reversed = curve.DuplicateCurve();
                    reversed.Reverse();
                    alignedCurves.Add(reversed);
                    flippedFlags.Add(true);
                }
            }
            else
            {
                // Align to reference curve direction
                Curve referenceCurve = validCurves[referenceIndex];
                Vector3d referenceDirection = GetCurveDirection(referenceCurve);

                // Validate reference direction
                if (referenceDirection.Length < 0.001)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Reference curve has degenerate direction. Using original curves.");
                    DA.SetDataList(0, validCurves);
                    DA.SetDataList(1, Enumerable.Repeat(false, validCurves.Count).ToList());
                    DA.SetData(2, "Reference curve is degenerate - no alignment performed.");
                    return;
                }

                foreach (var curve in validCurves)
                {
                    Vector3d curveDirection = GetCurveDirection(curve);
                    
                    // Skip degenerate curves
                    if (curveDirection.Length < 0.001)
                    {
                        alignedCurves.Add(curve.DuplicateCurve());
                        flippedFlags.Add(false);
                        continue;
                    }

                    double dotProduct = referenceDirection * curveDirection;

                    // If directions are opposite (dot product < 0), flip the curve
                    bool shouldFlip = dotProduct < 0;
                    
                    Curve aligned = curve.DuplicateCurve();
                    if (shouldFlip)
                    {
                        aligned.Reverse();
                    }
                    alignedCurves.Add(aligned);
                    flippedFlags.Add(shouldFlip);
                }
            }

            string info = $"Aligned {validCurves.Count} curves. ";
            if (reverse)
            {
                info += "All curves reversed.";
            }
            else
            {
                int flippedCount = flippedFlags.Count(f => f);
                info += $"Reference: curve {referenceIndex}. {flippedCount} curve(s) flipped.";
            }

            DA.SetDataList(0, alignedCurves);
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("AlignCurvesIcon.png");
        public override Guid ComponentGuid => new Guid("fa478232-2bbb-4fd4-98ff-0f3b9bb0d2eb");
    }
}

