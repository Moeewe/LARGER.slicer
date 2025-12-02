using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for converting polylines to lines and arcs.
    /// Based on ArcWelder algorithm: tests sequences of points to see if they can be represented
    /// as arcs or lines, reducing GCode size significantly.
    /// References:
    /// - https://github.com/FormerLurker/ArcWelderLib
    /// - https://hackaday.com/2020/11/03/this-gcode-post-processor-squeezes-lines-into-arcs/
    /// - https://stackoverflow.com/questions/56485500/arcs-and-line-segments-detection-from-collection-of-points
    /// </summary>
    public static class ArcWelderHelper
    {
        /// <summary>
        /// Converts a polyline to a list of curves (lines and arcs).
        /// Tests sequences of points to see if they can be represented as arcs or lines.
        /// </summary>
        /// <param name="points">Input points</param>
        /// <param name="tolerance">Maximum deviation from arc/line</param>
        /// <param name="minArcRadius">Minimum radius for arcs (smaller arcs become lines)</param>
        /// <returns>List of curves (LineCurve and ArcCurve)</returns>
        public static List<Curve> ConvertToLinesAndArcs(List<Point3d> points, double tolerance, double minArcRadius = 0.1)
        {
            var result = new List<Curve>();
            
            if (points == null || points.Count < 2)
                return result;

            if (points.Count == 2)
            {
                // Simple line
                result.Add(new LineCurve(points[0], points[1]));
                return result;
            }

            int i = 0;
            while (i < points.Count - 1)
            {
                // Try to fit arc starting from point i
                int arcEnd = TryFitArc(points, i, tolerance, minArcRadius, out Arc? arc);
                
                if (arcEnd > i + 1 && arc.HasValue && arc.Value.IsValid)
                {
                    // Arc fits, add it
                    result.Add(new ArcCurve(arc.Value));
                    i = arcEnd;
                }
                else
                {
                    // Try to fit line
                    int lineEnd = TryFitLine(points, i, tolerance);
                    
                    if (lineEnd > i + 1)
                    {
                        // Line fits, add it
                        Line line = new Line(points[i], points[lineEnd]);
                        result.Add(new LineCurve(line));
                        i = lineEnd;
                    }
                    else
                    {
                        // Single segment, add as line
                        Line line = new Line(points[i], points[i + 1]);
                        result.Add(new LineCurve(line));
                        i++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Tries to fit an arc starting from startIndex.
        /// Tests P1, P2, P3, then P1, P2, P3, P4, etc. until arc no longer fits.
        /// </summary>
        private static int TryFitArc(List<Point3d> points, int startIndex, double tolerance, double minRadius, out Arc? arc)
        {
            arc = null;
            
            if (startIndex >= points.Count - 2)
                return startIndex;

            // Need at least 3 points for an arc
            if (points.Count - startIndex < 3)
                return startIndex;

            // Start with first 3 points
            int bestEnd = startIndex + 2;
            Arc? bestArc = null;

            for (int endIndex = startIndex + 3; endIndex <= points.Count; endIndex++)
            {
                // Try to fit arc through points[startIndex] to points[endIndex-1]
                if (TryCreateArc(points, startIndex, endIndex, tolerance, minRadius, out Arc? testArc))
                {
                    bestEnd = endIndex;
                    bestArc = testArc;
                }
                else
                {
                    // Arc no longer fits, return previous best
                    break;
                }
            }

            arc = bestArc;
            return bestEnd;
        }

        /// <summary>
        /// Tries to create an arc through a sequence of points.
        /// </summary>
        private static bool TryCreateArc(List<Point3d> points, int startIndex, int endIndex, double tolerance, double minRadius, out Arc? arc)
        {
            arc = null;

            if (endIndex - startIndex < 3)
                return false;

            Point3d startPt = points[startIndex];
            Point3d midPt = points[(startIndex + endIndex) / 2];
            Point3d endPt = points[endIndex - 1];

            // Try to create arc through three points using circle fitting
            Circle circle;
            if (Circle.TryFitCircleToPoints(new Point3d[] { startPt, midPt, endPt }, out circle))
            {
                // Create arc from circle by calculating angles
                Vector3d xAxis = circle.Plane.XAxis;
                Vector3d yAxis = circle.Plane.YAxis;
                
                Vector3d toStart = startPt - circle.Center;
                Vector3d toEnd = endPt - circle.Center;
                
                // Project onto circle plane
                double startX = Vector3d.Multiply(toStart, xAxis);
                double startY = Vector3d.Multiply(toStart, yAxis);
                double endX = Vector3d.Multiply(toEnd, xAxis);
                double endY = Vector3d.Multiply(toEnd, yAxis);
                
                // Calculate angles
                double angleStart = Math.Atan2(startY, startX);
                double angleEnd = Math.Atan2(endY, endX);
                
                // Normalize angles to [0, 2π]
                if (angleStart < 0) angleStart += 2 * Math.PI;
                if (angleEnd < 0) angleEnd += 2 * Math.PI;
                
                // Ensure arc goes through mid point
                Vector3d toMid = midPt - circle.Center;
                double midX = Vector3d.Multiply(toMid, xAxis);
                double midY = Vector3d.Multiply(toMid, yAxis);
                double angleMid = Math.Atan2(midY, midX);
                if (angleMid < 0) angleMid += 2 * Math.PI;
                
                // Determine arc direction
                double angleDiff = angleEnd - angleStart;
                if (angleDiff < 0) angleDiff += 2 * Math.PI;
                
                // Create arc using ArcCurve and extract Arc
                // Create trimmed arc by creating ArcCurve and extracting arc
                ArcCurve arcCurve = new ArcCurve(circle, angleStart, angleStart + angleDiff);
                Arc testArc = arcCurve.Arc;

                // Check if all points are within tolerance using ArcCurve
                double maxDeviation = 0;
                for (int i = startIndex; i < endIndex; i++)
                {
                    double t;
                    arcCurve.ClosestPoint(points[i], out t);
                    Point3d closestPt = arcCurve.PointAt(t);
                    double deviation = points[i].DistanceTo(closestPt);
                    maxDeviation = Math.Max(maxDeviation, deviation);
                }

                // Check radius
                if (testArc.Radius < minRadius)
                    return false;

                // Check if deviation is acceptable
                if (maxDeviation <= tolerance && testArc.IsValid)
                {
                    arc = testArc;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to fit a line starting from startIndex.
        /// Tests if consecutive points are collinear within tolerance.
        /// </summary>
        private static int TryFitLine(List<Point3d> points, int startIndex, double tolerance)
        {
            if (startIndex >= points.Count - 1)
                return startIndex;

            Point3d startPt = points[startIndex];
            Point3d nextPt = points[startIndex + 1];
            
            // Direction vector
            Vector3d direction = nextPt - startPt;
            if (direction.Length < 0.001)
                return startIndex + 1;

            direction.Unitize();

            int endIndex = startIndex + 2;
            
            // Check if subsequent points are collinear
            for (int i = startIndex + 2; i < points.Count; i++)
            {
                Vector3d toPoint = points[i] - startPt;
                double distance = toPoint.Length;
                
                if (distance < 0.001)
                    continue; // Point too close to start

                // Project point onto line
                double projection = Vector3d.Multiply(toPoint, direction);
                Point3d projectedPt = startPt + direction * projection;
                
                // Check perpendicular distance
                double perpendicularDist = points[i].DistanceTo(projectedPt);
                
                if (perpendicularDist > tolerance)
                {
                    // Point deviates too much, stop line here
                    break;
                }

                endIndex = i + 1;
            }

            return endIndex;
        }

        /// <summary>
        /// Converts a curve to lines and arcs.
        /// Samples the curve and then converts the sampled points.
        /// </summary>
        public static List<Curve> ConvertCurveToLinesAndArcs(Curve curve, double sampleSpacing, double tolerance, double minArcRadius = 0.1)
        {
            if (curve == null || !curve.IsValid)
                return new List<Curve>();

            // Sample curve
            var points = PathHelper.SampleCurve(curve, sampleSpacing, true);
            
            // Convert to lines and arcs
            return ConvertToLinesAndArcs(points, tolerance, minArcRadius);
        }

        /// <summary>
        /// Calculates statistics about the conversion (reduction ratio, etc.).
        /// </summary>
        public static string GetConversionStats(List<Point3d> originalPoints, List<Curve> convertedCurves)
        {
            if (originalPoints == null || convertedCurves == null)
                return "Invalid input";

            int originalCount = originalPoints.Count;
            int convertedCount = convertedCurves.Count;
            double reductionRatio = originalCount > 0 ? (1.0 - (double)convertedCount / originalCount) * 100.0 : 0.0;

            int arcCount = convertedCurves.Count(c => c is ArcCurve);
            int lineCount = convertedCurves.Count(c => c is LineCurve);

            return $"Points: {originalCount} → Curves: {convertedCount} ({reductionRatio:F1}% reduction) | Arcs: {arcCount} | Lines: {lineCount}";
        }
    }
}

