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
    /// Infill Fermat Spirals - Generates Connected Fermat Spirals (CFS) toolpath.
    /// CFS provides smooth, continuous curves ideal for large-format 3D printing.
    /// Based on research: Zhao et al., 2016 - Connected Fermat Spirals for Layered Fabrication
    /// </summary>
    public class InfillFermatSpiralsComponent : BottomLayerPatternBase
    {
        public InfillFermatSpiralsComponent()
            : base("Infill Fermat Spirals", "Infill Fermat",
                  "Generates Connected Fermat Spirals (CFS) toolpath. Smooth, continuous curves ideal for large-format printing. Avoids sharp 90° turns of fractal patterns. Automatically detects curve orientation to fill inward.")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Min Radius", "MinRadius", "Minimum radius before stopping spiral (mm). 0 = fill to center.", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Max Region Size", "MaxRegion", "Maximum region size before subdividing (mm). Larger values = fewer regions.", GH_ParamAccess.item, 50.0);
            pManager.AddBooleanParameter("Subdivide Regions", "Subdivide", "Subdivide complex shapes into simpler regions", GH_ParamAccess.item, true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Validate inputs using base class method
            if (!ValidateInputs(DA, out Curve boundary, out Point3d seamPoint, out double spacing, out double boundaryOffset, out List<Curve> holes))
                return;

            double minRadius = 0.5;
            double maxRegionSize = 50.0;
            bool subdivideRegions = true;
            DA.GetData(5, ref minRadius);
            DA.GetData(6, ref maxRegionSize);
            DA.GetData(7, ref subdivideRegions);

            if (minRadius < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Min radius should be >= 0. Using 0.");
                minRadius = 0;
            }

            // Prepare boundary with offset (direction auto-detected)
            // For large-format: outer path should be ~half bead width from boundary
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate if not provided)
            Point3d? seamPointNullable = seamPoint.IsValid ? (Point3d?)seamPoint : null;
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, seamPointNullable);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(
                closedBoundary, seamPosition, seamParam, spacing, minRadius, maxRegionSize, subdivideRegions, holes);

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

            // Calculate statistics using base class method
            string stats = CalculateStatistics(pathPoints, closedBoundary, spacing, 
                $"Fermat Spirals | MinRadius={minRadius:F2}mm | Regions: {segments.Count}");

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, segmentCurves);
            DA.SetDataList(2, pathPoints);
            DA.SetData(3, stats);
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double seamParameter,
            double spacing,
            double minRadius,
            double maxRegionSize,
            bool subdivideRegions,
            List<Curve> holes)
        {
            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Step 1: Partition boundary into regions (if needed)
            List<Curve> regions;
            if (subdivideRegions)
            {
                regions = FermatSpiralHelper.PartitionIntoRegions(boundary, holes, maxRegionSize);
            }
            else
            {
                regions = new List<Curve> { boundary.DuplicateCurve() };
            }

            if (regions.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Generate Fermat spiral for each region
            var spirals = new List<List<Point3d>>();
            foreach (var region in regions)
            {
                if (region == null || !region.IsValid)
                    continue;

                // Filter out holes from region
                var regionSpiral = FermatSpiralHelper.GenerateInwardFermatSpiral(region, spacing, minRadius);
                
                // Filter points inside holes
                if (holes != null && holes.Count > 0 && regionSpiral.Count > 0)
                {
                    var filteredSpiral = new List<Point3d>();
                    foreach (var pt in regionSpiral)
                    {
                        bool insideHole = false;
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
                        if (!insideHole)
                        {
                            filteredSpiral.Add(pt);
                        }
                    }
                    regionSpiral = filteredSpiral;
                }

                if (regionSpiral.Count >= 2)
                {
                    spirals.Add(regionSpiral);
                    segments.Add(new List<Point3d>(regionSpiral));
                }
            }

            if (spirals.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 3: Connect spirals into continuous path
            pathPoints = FermatSpiralHelper.ConnectSpirals(spirals, boundary, spacing);

            // If connection failed, use first spiral
            if (pathPoints.Count == 0 && spirals.Count > 0)
            {
                pathPoints = spirals[0];
            }

            // Step 4: Start from seam position if possible
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillFermatSpiralsIcon.png");
        public override Guid ComponentGuid => new Guid("dd4ae05d-99dc-4ce2-96b5-694df6731030");
    }
}

