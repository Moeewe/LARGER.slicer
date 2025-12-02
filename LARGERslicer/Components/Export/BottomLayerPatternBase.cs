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
    /// Abstract base class for bottom layer fill pattern components.
    /// Provides common inputs and outputs for all pattern types.
    /// </summary>
    public abstract class BottomLayerPatternBase : GH_Component
    {
        protected BottomLayerPatternBase(string name, string nickname, string description)
            : base(name, nickname, description, "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Boundary curve to fill. Can be open or closed - will be closed automatically if needed.", GH_ParamAccess.item);
            pManager.AddPointParameter("Seam Point", "Seam", "Seam point to determine connection position on boundary. If not provided, automatically calculated (farthest from center).", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Line Spacing", "Spacing", "Distance between pattern lines (bead width/extrusion width in mm). Center-to-center distance equals bead width. For 5mm nozzle, use 5mm spacing.", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Boundary Offset", "Offset", "Additional offset from boundary inward (mm). Outer path should be ~half bead width from boundary. Use 0 for automatic.", GH_ParamAccess.item, 0.0);
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", GH_ParamAccess.list);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Complete continuous fill path as polyline curve", GH_ParamAccess.item);
            pManager.AddCurveParameter("Segments", "Segments", "Individual path segments for preview", GH_ParamAccess.list);
            pManager.AddPointParameter("Path Points", "Points", "All path points as ordered list", GH_ParamAccess.list);
            pManager.AddTextParameter("Stats", "Stats", "Path statistics (length, fill percentage, etc.)", GH_ParamAccess.item);
        }

        /// <summary>
        /// Validates common inputs and returns validated values.
        /// </summary>
        protected bool ValidateInputs(IGH_DataAccess DA, out Curve boundary, out Point3d seamPoint, out double spacing, out double boundaryOffset, out List<Curve> holes)
        {
            boundary = null;
            seamPoint = Point3d.Unset;
            spacing = 2.0;
            boundaryOffset = 0.0;
            holes = new List<Curve>();

            if (!DA.GetData(0, ref boundary))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve is required.");
                return false;
            }

            DA.GetData(1, ref seamPoint);
            if (!DA.GetData(2, ref spacing)) return false;
            DA.GetData(3, ref boundaryOffset);
            DA.GetDataList(4, holes);

            // Validate boundary
            if (boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve is invalid.");
                return false;
            }

            // Validate spacing
            if (spacing <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Line spacing must be greater than zero.");
                return false;
            }

            // Validate boundary offset
            if (boundaryOffset < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Boundary offset should be >= 0. Using 0.");
                boundaryOffset = 0;
            }

            // Validate holes
            holes = holes?.Where(h => h != null && h.IsValid).ToList() ?? new List<Curve>();

            return true;
        }

        /// <summary>
        /// Ensures boundary is closed and applies offset if needed.
        /// Automatically detects curve orientation for correct offset direction.
        /// For large-format printing: outer path should be ~half bead width from boundary.
        /// </summary>
        protected Curve PrepareBoundary(Curve boundary, double boundaryOffset, out List<Curve> offsetHoles, List<Curve> otherCurves = null, double spacing = 0.0)
        {
            offsetHoles = new List<Curve>();

            Curve closedBoundary = boundary.DuplicateCurve();
            if (!closedBoundary.IsClosed)
            {
                closedBoundary.MakeClosed(0.01);
            }

            // Calculate total offset: half bead width (for outer path) + additional offset
            // If spacing is provided and boundaryOffset is 0, use half spacing automatically
            double totalOffset = boundaryOffset;
            if (spacing > 0 && boundaryOffset == 0)
            {
                totalOffset = spacing * 0.5; // Half bead width for outer path
            }

            // Apply boundary offset if specified (direction automatically detected)
            if (totalOffset > 0)
            {
                double offsetDirection = PathHelper.GetOffsetDirection(closedBoundary, otherCurves);
                double offsetDistance = totalOffset * offsetDirection;
                var offsetCurves = closedBoundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    // Use largest offset curve - with NULL safety
                    var validOffsets = offsetCurves.Where(c => c != null && c.IsValid).ToList();
                    if (validOffsets.Count > 0)
                    {
                        closedBoundary = validOffsets.OrderByDescending(c => 
                        {
                            var area = AreaMassProperties.Compute(c);
                            return area != null ? area.Area : 0;
                        }).First();
                    }
                }
            }

            return closedBoundary;
        }

        /// <summary>
        /// Finds or calculates seam point position on boundary.
        /// </summary>
        protected (Point3d seamPosition, double seamParameter) GetSeamPosition(Curve boundary, Point3d? seamPoint)
        {
            if (seamPoint.HasValue && seamPoint.Value.IsValid)
            {
                return PathHelper.FindSeamPosition(boundary, seamPoint.Value);
            }
            else
            {
                // Auto-calculate: find point farthest from center
                BoundingBox bbox = boundary.GetBoundingBox(true);
                Point3d center = bbox.Center;
                
                double maxDist = 0;
                Point3d farthestPoint = boundary.PointAtStart;
                double farthestParam = boundary.Domain.T0;

                // Sample boundary to find farthest point - limit samples for performance
                double boundaryLength = boundary.GetLength();
                int samples = Math.Min(200, Math.Max(50, (int)Math.Ceiling(boundaryLength / Math.Max(1.0, boundaryLength / 100.0))));
                for (int i = 0; i <= samples; i++)
                {
                    double t = boundary.Domain.ParameterAt((double)i / samples);
                    Point3d pt = boundary.PointAt(t);
                    double dist = center.DistanceTo(pt);
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        farthestPoint = pt;
                        farthestParam = t;
                    }
                }

                return (farthestPoint, farthestParam);
            }
        }

        /// <summary>
        /// Checks if a point is inside the boundary curve and outside any holes.
        /// </summary>
        protected bool IsPointValid(Point3d point, Curve boundary, List<Curve> holes, double tolerance = 0.01)
        {
            if (boundary == null || !boundary.IsValid)
                return false;

            // Check if point is inside boundary
            PointContainment containment = boundary.Contains(point, Plane.WorldXY, tolerance);
            if (containment != PointContainment.Inside)
                return false;

            // Check if point is outside all holes
            foreach (var hole in holes)
            {
                if (hole != null && hole.IsValid)
                {
                    PointContainment holeContainment = hole.Contains(point, Plane.WorldXY, tolerance);
                    if (holeContainment == PointContainment.Inside)
                    {
                        return false; // Point is inside a hole
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Creates output curves from path points and segments.
        /// </summary>
        protected void CreateOutputCurves(List<Point3d> pathPoints, List<List<Point3d>> segments, out Curve pathCurve, out List<Curve> segmentCurves)
        {
            pathCurve = null;
            segmentCurves = new List<Curve>();

            if (pathPoints == null || pathPoints.Count < 2)
                return;

            // Clean path points: remove duplicates and points that are too close together
            var cleanedPathPoints = CleanPointList(pathPoints, 0.01);
            
            if (cleanedPathPoints.Count < 2)
            {
                // If cleaning removed too many points, try with smaller tolerance
                cleanedPathPoints = CleanPointList(pathPoints, 0.001);
            }

            if (cleanedPathPoints.Count < 2)
                return;

            Polyline pathPolyline = new Polyline(cleanedPathPoints);
            if (pathPolyline.IsValid)
            {
                pathCurve = new PolylineCurve(pathPolyline);
            }
            else
            {
                // Fallback: try to create a simple polyline with just start and end
                if (cleanedPathPoints.Count >= 2)
                {
                    var simplePoly = new Polyline(new[] { cleanedPathPoints[0], cleanedPathPoints[cleanedPathPoints.Count - 1] });
                    if (simplePoly.IsValid)
                    {
                        pathCurve = new PolylineCurve(simplePoly);
                    }
                }
            }

            foreach (var seg in segments)
            {
                if (seg != null && seg.Count >= 2)
                {
                    var cleanedSeg = CleanPointList(seg, 0.01);
                    if (cleanedSeg.Count >= 2)
                    {
                        Polyline segPoly = new Polyline(cleanedSeg);
                        if (segPoly.IsValid)
                        {
                            segmentCurves.Add(new PolylineCurve(segPoly));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cleans a point list by removing duplicate points and points that are too close together.
        /// </summary>
        private List<Point3d> CleanPointList(List<Point3d> points, double minDistance)
        {
            if (points == null || points.Count == 0)
                return new List<Point3d>();

            var cleaned = new List<Point3d>();
            cleaned.Add(points[0]); // Always keep first point

            for (int i = 1; i < points.Count; i++)
            {
                double dist = points[i].DistanceTo(cleaned[cleaned.Count - 1]);
                if (dist >= minDistance)
                {
                    cleaned.Add(points[i]);
                }
            }

            return cleaned;
        }

        /// <summary>
        /// Calculates path statistics.
        /// </summary>
        protected string CalculateStatistics(List<Point3d> pathPoints, Curve boundary, double spacing, string additionalInfo = "")
        {
            if (pathPoints == null || pathPoints.Count < 2)
                return "No valid path generated.";

            Polyline pathPolyline = new Polyline(pathPoints);
            double totalLength = pathPolyline.Length;
            double boundaryArea = AreaMassProperties.Compute(boundary)?.Area ?? 0.0;
            double fillPercentage = boundaryArea > 0 ? (totalLength * spacing / boundaryArea) * 100.0 : 0.0;

            string stats = $"Points={pathPoints.Count} | Length={totalLength:F2} mm | Fill={fillPercentage:F1}% | Spacing={spacing:F3} mm";
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                stats += $" | {additionalInfo}";
            }

            return stats;
        }
    }
}

