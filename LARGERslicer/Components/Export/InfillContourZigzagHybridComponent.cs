using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Infill Contour-Zigzag Hybrid - Generates a continuous contour-zigzag hybrid toolpath.
    /// Based on: Bi et al., 2022 - Continuous Contour-zigzag Hybrid Toolpath for Large Format Additive Manufacturing
    /// Combines smooth contour offsets with locally generated zigzag fill patterns to eliminate unfilled pockets.
    /// </summary>
    public class InfillContourZigzagHybridComponent : BottomLayerPatternBase
    {
        public InfillContourZigzagHybridComponent()
            : base("Single Line Fill with Contour-Zigzag", "SLF CZ Hybrid",
                  "Generates a continuous contour-zigzag hybrid toolpath. Combines smooth contour offsets with zigzag fills to eliminate unfilled pockets. Ideal for large-format printing. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Use Zigzag", "UseZigzag", "Enable zigzag fills between contours. If false, only contours are generated.", GH_ParamAccess.item, true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate minimal common inputs
            Curve boundary;
            double printWidth;
            List<Curve> holes;

            if (!ValidateInputs(DA, out boundary, out printWidth, out holes))
                return;

            // Get pattern-specific parameters
            bool useZigzag = true;
            DA.GetData(3, ref useZigzag);  // Index 3 after base inputs (0-2)

            double spacing = printWidth;
            double boundaryOffset = 0.0; // Will be calculated automatically

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(
                closedBoundary, seamPosition, spacing, useZigzag, holes);

            if (pathPoints == null || pathPoints.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Pattern generation resulted in insufficient points.");
                return;
            }

            // Create output curves using base class method
            CreateOutputCurves(pathPoints, segments, out Curve pathCurve, out List<Curve> segmentCurves);

            if (pathCurve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create output curve.");
                return;
            }

            // Set output - only Single Line Fill for Contour-Zigzag Hybrid
            DA.SetData(0, pathCurve);
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double spacing,
            bool useZigzag,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            if (boundary == null || !boundary.IsValid)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Preprocess boundary curve
            Curve cleanBoundary = ContourZigzagHelper.PreprocessCurve(boundary, 0.01);
            if (cleanBoundary == null || !cleanBoundary.IsValid)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Generate contour offsets (direction automatically detected via negative offset)
            List<Curve> contours = ContourZigzagHelper.GenerateInwardOffsets(cleanBoundary, spacing, 0.01);

            if (contours.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Filter contours that are inside holes
            var filteredContours = new List<Curve>();
            foreach (var contour in contours)
            {
                if (contour == null || !contour.IsValid)
                    continue;

                // Check if contour midpoint is inside any hole
                Point3d midPt = contour.PointAt(contour.Domain.Mid);
                bool insideHole = false;

                if (holes != null)
                {
                    foreach (var hole in holes)
                    {
                        if (hole != null && hole.IsValid && hole.IsClosed)
                        {
                            PointContainment containment = hole.Contains(midPt, Plane.WorldXY, 0.01);
                            if (containment == PointContainment.Inside)
                            {
                                insideHole = true;
                                break;
                            }
                        }
                    }
                }

                if (!insideHole)
                {
                    filteredContours.Add(contour);
                }
            }

            if (filteredContours.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Generate zigzag patterns between contours if enabled
            List<Polyline> zigzags = new List<Polyline>();
            if (useZigzag)
            {
                for (int i = 0; i < filteredContours.Count - 1; i++)
                {
                    Curve outer = filteredContours[i];
                    Curve inner = filteredContours[i + 1];

                    if (outer != null && inner != null && outer.IsValid && inner.IsValid)
                    {
                        Polyline zig = ContourZigzagHelper.GenerateZigzagBetween(outer, inner, spacing, 0.01);
                        if (zig != null && zig.Count >= 2)
                        {
                            // Filter zigzag points that are inside holes
                            var filteredZigPoints = new List<Point3d>();
                            foreach (Point3d pt in zig)
                            {
                                bool insideHole = false;
                                if (holes != null)
                                {
                                    foreach (var hole in holes)
                                    {
                                        if (hole != null && hole.IsValid && hole.IsClosed)
                                        {
                                            PointContainment containment = hole.Contains(pt, Plane.WorldXY, 0.01);
                                            if (containment == PointContainment.Inside)
                                            {
                                                insideHole = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (!insideHole)
                                {
                                    filteredZigPoints.Add(pt);
                                }
                            }

                            if (filteredZigPoints.Count >= 2)
                            {
                                zigzags.Add(new Polyline(filteredZigPoints));
                            }
                        }
                    }
                }
            }

            // Connect contours and zigzags into continuous path using DFS-based reordering
            pathPoints = ContourZigzagHelper.ConnectHybridPath(filteredContours, zigzags, seamPosition, spacing);

            // Create segments for output
            // Add contour segments
            foreach (var contour in filteredContours)
            {
                if (contour != null && contour.IsValid)
                {
                    var contourPoints = PathHelper.SampleCurve(contour, spacing * 0.3, true);
                    if (contourPoints.Count >= 2)
                    {
                        segments.Add(contourPoints);
                    }
                }
            }

            // Add zigzag segments
            foreach (var zigzag in zigzags)
            {
                if (zigzag != null && zigzag.Count >= 2)
                {
                    segments.Add(zigzag.ToList());
                }
            }

            // If path is empty, use first contour
            if (pathPoints.Count == 0 && filteredContours.Count > 0)
            {
                var firstContourPoints = PathHelper.SampleCurve(filteredContours[0], spacing * 0.3, true);
                if (firstContourPoints.Count >= 2)
                {
                    pathPoints = firstContourPoints;
                }
            }

            // Align path start to seam position if possible
            if (pathPoints.Count > 0)
            {
                // Find closest point to seam
                double minDist = double.MaxValue;
                int closestIdx = 0;
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    double dist = pathPoints[i].DistanceTo(seamPosition);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestIdx = i;
                    }
                }

                // Rotate path to start from closest point
                if (closestIdx > 0)
                {
                    var rotated = new List<Point3d>();
                    rotated.AddRange(pathPoints.Skip(closestIdx));
                    rotated.AddRange(pathPoints.Take(closestIdx));
                    pathPoints = rotated;
                }

                // Add connection from seam to start if needed
                if (pathPoints.Count > 0 && pathPoints[0].DistanceTo(seamPosition) > spacing * 0.1)
                {
                    var connection = CreateSeamConnection(seamPosition, pathPoints[0], boundary, spacing);
                    if (connection.Count > 0)
                    {
                        pathPoints.InsertRange(0, connection);
                    }
                    else
                    {
                        pathPoints.Insert(0, seamPosition);
                    }
                }
            }

            return (pathPoints, segments);
        }

        /// <summary>
        /// Creates a connection from seam position to path start.
        /// </summary>
        private List<Point3d> CreateSeamConnection(
            Point3d seamPt, Point3d pathStart, Curve boundary, double spacing)
        {
            var connection = new List<Point3d>();

            if (boundary == null || !boundary.IsValid)
            {
                // Simple linear connection
                int steps = Math.Max(2, (int)Math.Ceiling(seamPt.DistanceTo(pathStart) / spacing));
                for (int s = 1; s < steps; s++)
                {
                    double t = (double)s / steps;
                    connection.Add(seamPt + (pathStart - seamPt) * t);
                }
                return connection;
            }

            try
            {
                double tSeam, tStart;
                boundary.ClosestPoint(seamPt, out tSeam);
                boundary.ClosestPoint(pathStart, out tStart);

                tSeam = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tSeam));
                tStart = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tStart));

                double distForward = Math.Abs(tStart - tSeam);
                double distBackward = boundary.Domain.Length - distForward;
                bool forward = distForward <= distBackward;

                double boundaryLength = forward ? distForward : distBackward;
                int numSteps = Math.Max(2, (int)Math.Ceiling(boundaryLength / (spacing * 0.1)));
                numSteps = Math.Min(numSteps, 200);

                for (int i = 1; i < numSteps; i++)
                {
                    double t;
                    if (forward)
                    {
                        t = tSeam + (tStart - tSeam) * ((double)i / numSteps);
                    }
                    else
                    {
                        if (tSeam > tStart)
                        {
                            double wrapLength = (boundary.Domain.T1 - tSeam) + (tStart - boundary.Domain.T0);
                            t = tSeam + wrapLength * ((double)i / numSteps);
                            if (t > boundary.Domain.T1)
                                t = boundary.Domain.T0 + (t - boundary.Domain.T1);
                        }
                        else
                        {
                            double wrapLength = (tSeam - boundary.Domain.T0) + (boundary.Domain.T1 - tStart);
                            t = tSeam - wrapLength * ((double)i / numSteps);
                            if (t < boundary.Domain.T0)
                                t = boundary.Domain.T1 - (boundary.Domain.T0 - t);
                        }
                    }

                    t = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, t));
                    Point3d pt = boundary.PointAt(t);

                    // Offset inward by half spacing
                    Vector3d tangent = boundary.TangentAt(t);
                    tangent.Unitize();
                    Vector3d normal = new Vector3d(-tangent.Y, tangent.X, 0);
                    normal.Unitize();

                    BoundingBox bbox = boundary.GetBoundingBox(true);
                    Point3d center = bbox.Center;
                    Vector3d toCenter = center - pt;
                    toCenter.Unitize();
                    if (normal * toCenter < 0)
                        normal = -normal;

                    Point3d offsetPt = pt + normal * (spacing * 0.5);
                    connection.Add(offsetPt);
                }
            }
            catch
            {
                // Fallback: simple linear
                int steps = Math.Max(2, (int)Math.Ceiling(seamPt.DistanceTo(pathStart) / spacing));
                for (int s = 1; s < steps; s++)
                {
                    double t = (double)s / steps;
                    connection.Add(seamPt + (pathStart - seamPt) * t);
                }
            }

            return connection;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillContourZigzagHybridIcon.png");
        public override Guid ComponentGuid => new Guid("00e1937f-b7d4-432c-b3ea-d7c5da886787");
    }
}

