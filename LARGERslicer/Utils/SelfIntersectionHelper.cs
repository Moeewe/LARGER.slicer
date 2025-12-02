using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for handling self-intersecting curves (undercuts/hinterschneidungen).
    /// Based on Laurent Delrieu's Euler-Cycle approach: at each self-intersection, flip curve direction.
    /// This prevents path crossing itself and heals bad paths automatically.
    /// </summary>
    public static class SelfIntersectionHelper
    {
        private static int _recursionDepth = 0;
        private const int MAX_RECURSION_DEPTH = 10;

        /// <summary>
        /// Suppresses self-intersections in a curve using Euler-Cycle approach.
        /// At each self-intersection, flips curve direction to prevent crossing.
        /// </summary>
        /// <param name="curve">Input curve that may self-intersect</param>
        /// <param name="tolerance">Tolerance for intersection detection</param>
        /// <param name="splitSegments">If true, splits curve into separate segments at intersections. If false, flips direction.</param>
        /// <returns>List of non-intersecting curve segments</returns>
        public static List<Curve> SuppressSelfIntersections(Curve curve, double tolerance = 0.01, bool splitSegments = false)
        {
            var result = new List<Curve>();
            
            if (curve == null || !curve.IsValid)
                return result;

            // Prevent infinite recursion
            _recursionDepth++;
            if (_recursionDepth > MAX_RECURSION_DEPTH)
            {
                _recursionDepth--;
                // Return curve as-is if too deep
                result.Add(curve.DuplicateCurve());
                return result;
            }

            // Find all self-intersections with adaptive tolerance
            double adaptiveTolerance = Math.Max(tolerance, curve.GetLength() * 0.0001);
            var selfIntersections = FindSelfIntersections(curve, adaptiveTolerance);

            if (selfIntersections.Count == 0)
            {
                // No self-intersections, return curve as-is
                result.Add(curve.DuplicateCurve());
                return result;
            }

            if (splitSegments)
            {
                // Split curve at intersection points
                var segments = SegmentCurveAtIntersections(curve, selfIntersections, tolerance);
                // Process segments to remove self-intersections
                var processedSegments = ProcessSegmentsForSelfIntersection(segments, adaptiveTolerance);
                result.AddRange(processedSegments);
            }
            else
            {
                // Try flipping direction first
                Curve reversed = curve.DuplicateCurve();
                reversed.Reverse();
                
                // Check if reversed version has fewer intersections
                var reversedIntersections = FindSelfIntersections(reversed, adaptiveTolerance);
                
                if (reversedIntersections.Count < selfIntersections.Count)
                {
                    // Reversed version is better, use it
                    result.Add(reversed);
                }
                else
                {
                    // Original is better or same - if still has intersections, segment anyway
                    if (selfIntersections.Count > 0)
                    {
                        var segments = SegmentCurveAtIntersections(curve, selfIntersections, tolerance);
                        var processedSegments = ProcessSegmentsForSelfIntersection(segments, adaptiveTolerance);
                        result.AddRange(processedSegments);
                    }
                    else
                    {
                        result.Add(curve.DuplicateCurve());
                    }
                }
            }
            _recursionDepth--;
            return result;
        }

        /// <summary>
        /// Finds all self-intersection points in a curve.
        /// </summary>
        private static List<Point3d> FindSelfIntersections(Curve curve, double tolerance)
        {
            var intersections = new List<Point3d>();

            // Use Rhino's self-intersection detection
            var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(curve, tolerance);

            if (selfIntersections != null)
            {
                foreach (var intersection in selfIntersections)
                {
                    Point3d pt = curve.PointAt(intersection.ParameterA);
                    intersections.Add(pt);
                }
            }

            return intersections;
        }

        /// <summary>
        /// Segments a curve at intersection points.
        /// </summary>
        private static List<Curve> SegmentCurveAtIntersections(Curve curve, List<Point3d> intersectionPoints, double tolerance)
        {
            var segments = new List<Curve>();

            if (intersectionPoints.Count == 0)
            {
                segments.Add(curve.DuplicateCurve());
                return segments;
            }

            // Find intersection parameters on curve
            var intersectionParams = new List<double>();
            foreach (var pt in intersectionPoints)
            {
                double t;
                if (curve.ClosestPoint(pt, out t))
                {
                    double dist = curve.PointAt(t).DistanceTo(pt);
                    if (dist <= tolerance)
                    {
                        intersectionParams.Add(t);
                    }
                }
            }

            // Add start and end points
            intersectionParams.Add(curve.Domain.T0);
            intersectionParams.Add(curve.Domain.T1);

            // Sort and remove duplicates
            intersectionParams = intersectionParams.Distinct().OrderBy(t => t).ToList();

            // Create segments between intersection points
            for (int i = 0; i < intersectionParams.Count - 1; i++)
            {
                double t1 = intersectionParams[i];
                double t2 = intersectionParams[i + 1];

                // Handle wrap-around for closed curves
                if (i == intersectionParams.Count - 2 && curve.IsClosed)
                {
                    // Last segment wraps around
                    Curve seg1 = curve.Trim(t1, curve.Domain.T1);
                    Curve seg2 = curve.Trim(curve.Domain.T0, t2);
                    if (seg1 != null && seg1.IsValid && seg1.GetLength() > tolerance)
                    {
                        segments.Add(seg1);
                    }
                    if (seg2 != null && seg2.IsValid && seg2.GetLength() > tolerance)
                    {
                        segments.Add(seg2);
                    }
                }
                else
                {
                    Curve segment = curve.Trim(t1, t2);
                    if (segment != null && segment.IsValid && segment.GetLength() > tolerance)
                    {
                        segments.Add(segment);
                    }
                }
            }

            return segments;
        }

        /// <summary>
        /// Processes segments to remove self-intersections using Euler-Cycle approach.
        /// Flips curve direction at intersections to prevent crossing.
        /// </summary>
        private static List<Curve> ProcessSegmentsForSelfIntersection(List<Curve> segments, double tolerance)
        {
            var processed = new List<Curve>();

            foreach (var segment in segments)
            {
                // Check if segment self-intersects
                var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(segment, tolerance);

                if (selfIntersections != null && selfIntersections.Count > 0)
                {
                    // Segment has self-intersections, try flipping direction
                    Curve reversed = segment.DuplicateCurve();
                    reversed.Reverse();

                    // Check if reversed version has fewer intersections
                    var reversedIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(reversed, tolerance);
                    
                    if (reversedIntersections == null || reversedIntersections.Count < selfIntersections.Count)
                    {
                        // Reversed version is better, use it
                        processed.Add(reversed);
                    }
                    else
                    {
                        // Original is better, or same - recursively process
                        var subSegments = SuppressSelfIntersections(segment, tolerance);
                        processed.AddRange(subSegments);
                    }
                }
                else
                {
                    // No self-intersections, add as-is
                    processed.Add(segment.DuplicateCurve());
                }
            }

            return processed;
        }

        /// <summary>
        /// Handles offset curves that may self-intersect due to undercuts.
        /// Splits self-intersecting offset curves into valid segments.
        /// </summary>
        public static List<Curve> HandleOffsetSelfIntersections(Curve[] offsetCurves, double tolerance = 0.01)
        {
            var validCurves = new List<Curve>();

            foreach (var offsetCurve in offsetCurves)
            {
                if (offsetCurve == null || !offsetCurve.IsValid)
                    continue;

                // Check for self-intersections
                var selfIntersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(offsetCurve, tolerance);

                if (selfIntersections != null && selfIntersections.Count > 0)
                {
                    // Curve self-intersects, split it
                    var segments = SuppressSelfIntersections(offsetCurve, tolerance);
                    validCurves.AddRange(segments);
                }
                else
                {
                    // No self-intersections, add as-is
                    validCurves.Add(offsetCurve.DuplicateCurve());
                }
            }

            return validCurves;
        }

        /// <summary>
        /// Generates offset curves with proper handling of self-intersections (undercuts).
        /// When offset curves self-intersect, they are split into valid segments.
        /// </summary>
        public static List<Curve> GenerateOffsetCurvesWithUndercutHandling(
            Curve boundary, 
            double spacing, 
            int maxOffsets = 1000,
            double tolerance = 0.01,
            List<Curve> otherCurves = null)
        {
            var allOffsetCurves = new List<Curve>();
            Curve current = boundary.DuplicateCurve();

            // Determine offset direction based on curve orientation
            double offsetDirection = PathHelper.GetOffsetDirection(boundary, otherCurves);
            double offsetDistance = spacing * offsetDirection;

            for (int i = 0; i < maxOffsets; i++)
            {
                var offsetCurves = current.Offset(Plane.WorldXY, offsetDistance, tolerance, CurveOffsetCornerStyle.Sharp);
                
                if (offsetCurves == null || offsetCurves.Length == 0)
                    break;

                // Handle self-intersections in offset curves (undercuts)
                var validCurves = HandleOffsetSelfIntersections(offsetCurves, tolerance);

                if (validCurves.Count == 0)
                    break;

                // Use largest valid curve for next iteration
                Curve largest = validCurves.OrderByDescending(c => AreaMassProperties.Compute(c)?.Area ?? 0).First();
                
                // Check if offset is still valid
                double area = AreaMassProperties.Compute(largest)?.Area ?? 0;
                if (area < spacing * spacing)
                    break;

                // Add all valid curves (may be multiple if self-intersection occurred)
                allOffsetCurves.AddRange(validCurves);
                
                // Use largest for next iteration
                current = largest;
            }

            return allOffsetCurves;
        }
    }
}

