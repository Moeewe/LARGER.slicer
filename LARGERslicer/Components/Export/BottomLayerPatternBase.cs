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
            // Minimal common inputs - each component should override and add its specific inputs
            pManager.AddCurveParameter("Curve", "C", "Boundary curve to fill. Can be open or closed - will be closed automatically if needed.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Print Width", "PW", "Print width (bead width/extrusion width in mm). Center-to-center distance equals bead width. For 5mm nozzle, use 5mm.", GH_ParamAccess.item, 5.0);
            pManager.AddCurveParameter("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", GH_ParamAccess.list);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Minimal common output - each component should override and add its specific outputs
            pManager.AddCurveParameter("Single Line Fill", "SLF", "Single continuous fill path", GH_ParamAccess.item);
        }

        /// <summary>
        /// Validates minimal common inputs (Curve, Print Width, Holes).
        /// </summary>
        protected bool ValidateInputs(IGH_DataAccess DA, 
            out Curve boundary, 
            out double printWidth, 
            out List<Curve> holes)
        {
            boundary = null;
            printWidth = 5.0;
            holes = new List<Curve>();

            if (!DA.GetData(0, ref boundary) || boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Valid boundary curve is required.");
                return false;
            }

            if (!DA.GetData(1, ref printWidth))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Print width is required.");
                return false;
            }

            DA.GetDataList(2, holes);

            // Validate print width
            if (printWidth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Print width must be greater than zero.");
                return false;
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

        /// <summary>
        /// Validates that a path is truly continuous without breaks.
        /// CRITICAL for large-format printing - any break = start/stop point = visible seam.
        /// Based on: CEAD Group guidelines - continuous motion essential for 5mm nozzle.
        /// </summary>
        /// <param name="path">Path points to validate</param>
        /// <param name="tolerance">Maximum allowed gap between consecutive points (mm)</param>
        /// <returns>Tuple of (isContinuous, list of break indices)</returns>
        protected (bool isContinuous, List<int> breaks) ValidateContinuity(
            List<Point3d> path,
            double tolerance = 0.01)
        {
            var breaks = new List<int>();

            if (path == null || path.Count < 2)
                return (false, breaks);

            for (int i = 0; i < path.Count - 1; i++)
            {
                double gap = path[i].DistanceTo(path[i + 1]);
                if (gap > tolerance)
                {
                    breaks.Add(i); // Gap detected between i and i+1
                }
            }

            bool isContinuous = (breaks.Count == 0);
            return (isContinuous, breaks);
        }

        /// <summary>
        /// Validates that connection points maintain minimum clearance from existing path.
        /// Prevents spacing violations that cause nozzle collisions in large-format printing.
        /// Based on: Ultimaker - bead width = minimum center-to-center spacing.
        /// </summary>
        /// <param name="connectionPoint">Point to check</param>
        /// <param name="existingPath">Existing path points</param>
        /// <param name="minClearance">Minimum required distance (typically = bead width)</param>
        /// <returns>True if clearance is sufficient</returns>
        protected bool ValidateConnectionClearance(
            Point3d connectionPoint,
            List<Point3d> existingPath,
            double minClearance)
        {
            if (existingPath == null || existingPath.Count == 0)
                return true;

            // Check if connection point is too close to any existing point
            foreach (var pt in existingPath)
            {
                double dist = connectionPoint.DistanceTo(pt);
                if (dist < minClearance && dist > 0.001) // Not same point
                {
                    return false; // Too close!
                }
            }

            return true;
        }

        /// <summary>
        /// Validates minimum spacing between two curves (for offset spirals/contours).
        /// CRITICAL: Prevents curves from touching which causes material overlap in large-format.
        /// Ensures minimum spacing = bead width between adjacent toolpaths.
        /// </summary>
        /// <param name="curve1">First curve</param>
        /// <param name="curve2">Second curve</param>
        /// <param name="minSpacing">Minimum required spacing (bead width)</param>
        /// <param name="tolerance">Sampling tolerance for distance check</param>
        /// <returns>True if spacing is sufficient everywhere</returns>
        protected bool ValidateCurveSpacing(
            Curve curve1,
            Curve curve2,
            double minSpacing,
            double tolerance = 0.1)
        {
            if (curve1 == null || curve2 == null || !curve1.IsValid || !curve2.IsValid)
                return true;

            // Sample both curves densely
            int samples = Math.Max(20, (int)(Math.Max(curve1.GetLength(), curve2.GetLength()) / tolerance));
            samples = Math.Min(samples, 200); // Cap for performance

            for (int i = 0; i <= samples; i++)
            {
                double t1 = curve1.Domain.ParameterAt((double)i / samples);
                Point3d pt1 = curve1.PointAt(t1);

                // Find closest point on curve2
                double t2;
                curve2.ClosestPoint(pt1, out t2);
                Point3d pt2 = curve2.PointAt(t2);

                double dist = pt1.DistanceTo(pt2);
                if (dist < minSpacing * 0.95) // 5% tolerance
                {
                    return false; // Curves too close!
                }
            }

            return true;
        }
    }
}


