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
    /// Infill Spiral - Generates concentric spiral fill pattern from outer boundary to inner point.
    /// Pattern A: Spiral/Concentric
    /// </summary>
    public class InfillSpiralComponent : BottomLayerPatternBase
    {
        public InfillSpiralComponent()
            : base("Single Line Fill with Spiral", "SLF Spiral",
                  "Generates concentric spiral fill pattern. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Spiral";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddBooleanParameter("Clockwise", "CW", "True for clockwise spiral, False for counterclockwise", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Min Radius", "MinR", "Minimum radius before stopping (mm). 0 = fill to center", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            base.RegisterOutputParams(pManager);
            // Only Single Line Fill output needed for Spiral
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
            bool clockwise = true;
            double minRadius = 0.0;
            DA.GetData(3, ref clockwise);  // Index 3 after base inputs (0-2)
            DA.GetData(4, ref minRadius);

            double spacing = printWidth;
            double boundaryOffset = 0.0; // Will be calculated automatically in PrepareBoundary

            // Prepare boundary with offset (direction auto-detected)
            // For large-format: outer path should be ~half bead width from boundary
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            
            // Combine original holes with offset holes
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate - farthest from center)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(closedBoundary, seamPosition, seamParam, spacing, clockwise, minRadius, holes);

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

            // Set output - only Single Line Fill for Spiral
            DA.SetData(0, pathCurve);
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double seamParameter,
            double spacing,
            bool clockwise,
            double minRadius,
            List<Curve> holes)
        {

            var pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();

            // Generate offset curves (direction automatically detected)
            var offsetCurves = PathHelper.GenerateOffsetCurves(boundary, spacing, 1000, holes);

            if (offsetCurves.Count == 0)
            {
                // Fallback: just return seam position
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Add boundary as first curve
            var allCurves = new List<Curve> { boundary };
            allCurves.AddRange(offsetCurves);

            // Convert seam parameter (curve parameter t) to normalized length parameter
            double boundaryLength = boundary.GetLength();
            if (boundaryLength <= 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }
            
            // Convert curve parameter to normalized length parameter [0,1]
            double normalizedSeamT = 0.0;
            try
            {
                // Ensure seamParameter is within domain
                double validSeamParam = seamParameter;
                if (validSeamParam < boundary.Domain.T0) validSeamParam = boundary.Domain.T0;
                if (validSeamParam > boundary.Domain.T1) validSeamParam = boundary.Domain.T1;
                
                // Get the length up to this parameter by sampling
                Curve trimmed = null;
                try
                {
                    trimmed = boundary.Trim(boundary.Domain.T0, validSeamParam);
                }
                catch
                {
                    // If trim fails, use closest point approach
                    double t;
                    if (boundary.ClosestPoint(seamPosition, out t))
                    {
                        validSeamParam = t;
                        try
                        {
                            trimmed = boundary.Trim(boundary.Domain.T0, validSeamParam);
                        }
                        catch
                        {
                            // Still fails, use start point
                            trimmed = null;
                        }
                    }
                }
                
                if (trimmed != null && trimmed.IsValid)
                {
                    double lengthAtSeam = trimmed.GetLength();
                    normalizedSeamT = lengthAtSeam / boundaryLength;
                }
                else
                {
                    // Fallback: use seam position to find closest normalized length
                    double t;
                    if (boundary.ClosestPoint(seamPosition, out t))
                    {
                        // Normalize parameter to [0,1]
                        double normalizedParam = (t - boundary.Domain.T0) / boundary.Domain.Length;
                        // Convert to normalized length parameter
                        double lengthT;
                        if (boundary.NormalizedLengthParameter(normalizedParam, out lengthT))
                        {
                            // Try to get length at this parameter
                            try
                            {
                                Curve testTrim = boundary.Trim(boundary.Domain.T0, t);
                                if (testTrim != null && testTrim.IsValid)
                                {
                                    normalizedSeamT = testTrim.GetLength() / boundaryLength;
                                }
                                else
                                {
                                    normalizedSeamT = normalizedParam;
                                }
                            }
                            catch
                            {
                                normalizedSeamT = normalizedParam;
                            }
                        }
                        else
                        {
                            normalizedSeamT = normalizedParam;
                        }
                    }
                    else
                    {
                        // Last resort: start at beginning
                        normalizedSeamT = 0.0;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback: use seam position to find closest normalized length
                double t;
                if (boundary.ClosestPoint(seamPosition, out t))
                {
                    double normalizedParam = (t - boundary.Domain.T0) / boundary.Domain.Length;
                    normalizedSeamT = normalizedParam;
                }
                else
                {
                    normalizedSeamT = 0.0;
                }
            }
            
            // Ensure it's in [0, 1]
            if (normalizedSeamT < 0) normalizedSeamT = 0;
            if (normalizedSeamT > 1) normalizedSeamT = 1;
            
            // Determine if we need to reverse curves for clockwise/counterclockwise
            // IMPORTANT: Create copies before reversing to avoid modifying originals
            if (!clockwise)
            {
                var reversedCurves = new List<Curve>();
                foreach (var curve in allCurves)
                {
                    Curve reversed = curve.DuplicateCurve();
                    reversed.Reverse();
                    reversedCurves.Add(reversed);
                }
                allCurves = reversedCurves;
                // Adjust normalized seam parameter for reversed curves
                normalizedSeamT = 1.0 - normalizedSeamT;
            }
            else
            {
                // Still create copies to avoid modifying originals
                var copiedCurves = new List<Curve>();
                foreach (var curve in allCurves)
                {
                    copiedCurves.Add(curve.DuplicateCurve());
                }
                allCurves = copiedCurves;
            }

            // Sample points from each curve, starting from seam position
            var allCurvePoints = new List<List<Point3d>>();
            var allCurveSeamParams = new List<double>(); // Track seam parameter for each curve
            
            for (int curveIdx = 0; curveIdx < allCurves.Count; curveIdx++)
            {
                var curve = allCurves[curveIdx];
                double seamParam = normalizedSeamT;
                
                // CRITICAL: Align seam with previous curve's end for smooth transitions
                if (curveIdx > 0 && allCurvePoints.Count > 0)
                {
                    var prevCurvePoints = allCurvePoints[allCurvePoints.Count - 1];
                    if (prevCurvePoints.Count > 0)
                    {
                        Point3d prevEnd = prevCurvePoints[prevCurvePoints.Count - 1];
                        
                        // Find closest point on current curve to previous curve end
                        double tClosest;
                        curve.ClosestPoint(prevEnd, out tClosest);
                        
                        // Convert to normalized length parameter
                        double curveLength = curve.GetLength();
                        if (curveLength > 0.001)
                        {
                            try
                            {
                                Curve trimmed = curve.Trim(curve.Domain.T0, tClosest);
                                if (trimmed != null && trimmed.IsValid)
                                {
                                    double lengthToClosest = trimmed.GetLength();
                                    seamParam = lengthToClosest / curveLength;
                                }
                            }
                            catch
                            {
                                // Fallback: use parameter-based approximation
                                seamParam = (tClosest - curve.Domain.T0) / curve.Domain.Length;
                            }
                        }
                        
                        // Clamp to [0,1]
                        if (seamParam < 0) seamParam = 0;
                        if (seamParam > 1) seamParam = 1;
                    }
                }
                
                allCurveSeamParams.Add(seamParam);
                
                // Sample curve starting from aligned seam position
                var curvePoints = SampleCurveFromSeam(curve, spacing * 0.5, seamParam, clockwise);
                
                // Filter points that are inside holes
                if (holes != null && holes.Count > 0)
                {
                    curvePoints = curvePoints.Where(pt => IsPointValid(pt, boundary, holes)).ToList();
                }
                
                if (curvePoints.Count >= 2)
                {
                    allCurvePoints.Add(curvePoints);
                }
            }

            // Connect curves with transitions
            for (int i = 0; i < allCurvePoints.Count; i++)
            {
                var currentCurve = allCurvePoints[i];

                if (i == 0)
                {
                    // First curve: start from seam position
                    segments.Add(new List<Point3d>(currentCurve));
                }
                else
                {
                    // Connect from previous curve end to current curve start
                    Point3d prevEnd = allCurvePoints[i - 1][allCurvePoints[i - 1].Count - 1];
                    Point3d currStart = currentCurve[0];

                    // Add connection following boundary offset (geometric consistency)
                    if (prevEnd.DistanceTo(currStart) > spacing * 0.1)
                    {
                        // Use offset-following connection to maintain geometric consistency
                        var connection = PathHelper.CreateOffsetFollowingConnection(
                            prevEnd, currStart, boundary, allCurves[i - 1], allCurves[i], spacing);
                        
                        if (connection.Count > 0)
                        {
                            segments.Add(connection);
                        }
                        else
                        {
                            // Fallback: 90° connection if offset connection fails
                            var fallbackConnection = Create90DegreeConnection(
                                prevEnd, currStart, spacing, allCurves[i - 1], allCurves[i]);
                            if (fallbackConnection.Count > 0)
                            {
                                segments.Add(fallbackConnection);
                            }
                        }
                    }

                    segments.Add(new List<Point3d>(currentCurve));
                }

                // Check if we've reached minimum radius
                if (minRadius > 0)
                {
                    BoundingBox bbox = allCurves[i].GetBoundingBox(true);
                    double currentRadius = Math.Min(bbox.Diagonal.X, bbox.Diagonal.Y) * 0.5;
                    if (currentRadius < minRadius)
                        break;
                }
            }

            // Build continuous path starting from seam position
            pathPoints.Add(seamPosition);
            foreach (var seg in segments)
            {
                if (pathPoints.Count > 0 && seg.Count > 0)
                {
                    // Smooth connection if needed
                    Point3d lastPt = pathPoints[pathPoints.Count - 1];
                    Point3d firstPt = seg[0];
                    if (lastPt.DistanceTo(firstPt) > 0.01)
                    {
                        int steps = Math.Max(2, (int)Math.Ceiling(lastPt.DistanceTo(firstPt) / spacing));
                        for (int s = 1; s < steps; s++)
                        {
                            double t = (double)s / steps;
                            pathPoints.Add(lastPt + (firstPt - lastPt) * t);
                        }
                    }
                }
                pathPoints.AddRange(seg);
            }

            // Do NOT return to seam position - end at last point of pattern
            // This prevents unwanted retraction/travel moves across the geometry

            return (pathPoints, segments);
        }

        /// <summary>
        /// Creates a 90° perpendicular connection between spiral loops.
        /// CRITICAL: Connection must be perpendicular to origin curve to avoid chaotic crossing paths.
        /// Uses tangent at exit point to determine perpendicular direction.
        /// </summary>
        private List<Point3d> Create90DegreeConnection(
            Point3d startPt, Point3d endPt, double spacing, Curve prevCurve, Curve nextCurve)
        {
            var connection = new List<Point3d>();
            
            if (prevCurve == null || !prevCurve.IsValid)
                return connection;

            try
            {
                // Get tangent at exit point (prevCurve end)
                double tStart;
                prevCurve.ClosestPoint(startPt, out tStart);
                Vector3d tangentAtExit = prevCurve.TangentAt(tStart);
                
                if (tangentAtExit.Length < 0.001)
                    return connection;
                    
                tangentAtExit.Unitize();
                
                // Calculate perpendicular direction (90° to tangent)
                // Cross with Z-axis to get perpendicular in XY plane
                Vector3d perpendicular = Vector3d.CrossProduct(tangentAtExit, Vector3d.ZAxis);
                if (perpendicular.Length < 0.001)
                {
                    // Tangent is vertical, use different perpendicular
                    perpendicular = new Vector3d(-tangentAtExit.Y, tangentAtExit.X, 0);
                }
                perpendicular.Unitize();
                
                // Determine which perpendicular direction points toward endPt
                Vector3d toEnd = endPt - startPt;
                if (perpendicular * toEnd < 0)
                {
                    perpendicular = -perpendicular;
                }
                
                // Create L-shaped connection: 90° turn then straight to endpoint
                double radialDistance = startPt.DistanceTo(endPt);
                
                // First segment: move perpendicular to tangent (radial direction)
                Point3d midPoint = startPt + perpendicular * radialDistance;
                
                // Sample first leg (perpendicular exit)
                int steps1 = Math.Max(4, (int)Math.Ceiling(radialDistance / (spacing * 0.5)));
                for (int i = 1; i < steps1; i++)
                {
                    double t = (double)i / steps1;
                    connection.Add(startPt + perpendicular * radialDistance * t);
                }
                
                // Add midpoint
                connection.Add(midPoint);
                
                // Second segment: move toward endpoint (tangential to next curve)
                double tangentialDistance = midPoint.DistanceTo(endPt);
                int steps2 = Math.Max(4, (int)Math.Ceiling(tangentialDistance / (spacing * 0.5)));
                for (int i = 1; i < steps2; i++)
                {
                    double t = (double)i / steps2;
                    connection.Add(midPoint + (endPt - midPoint) * t);
                }
            }
            catch
            {
                // Fallback: empty (will skip connection)
            }
            
            return connection;
        }

        /// <summary>
        /// Creates a smooth bridge between two spiral loops using tangent-aligned curve.
        /// Critical for 5mm nozzle large-format printing to avoid sharp transitions.
        /// DEPRECATED: Replaced by Create90DegreeConnection for cleaner paths.
        /// </summary>
        private List<Point3d> CreateSmoothSpiralBridge(
            Point3d startPt, Point3d endPt, double spacing, Curve prevCurve, Curve nextCurve)
        {
            var bridge = new List<Point3d>();
            
            if (prevCurve == null || nextCurve == null || !prevCurve.IsValid || !nextCurve.IsValid)
                return bridge;

            try
            {
                // Get tangent directions at connection points
                double tStart, tEnd;
                prevCurve.ClosestPoint(startPt, out tStart);
                nextCurve.ClosestPoint(endPt, out tEnd);
                
                Vector3d tangentStart = prevCurve.TangentAt(tStart);
                Vector3d tangentEnd = nextCurve.TangentAt(tEnd);
                
                if (tangentStart.Length < 0.001 || tangentEnd.Length < 0.001)
                    return bridge;
                    
                tangentStart.Unitize();
                tangentEnd.Unitize();
                
                // Create smooth cubic bezier bridge aligned with tangents
                // This ensures NO sharp corners at transition points
                double bridgeLength = startPt.DistanceTo(endPt);
                double controlDist = bridgeLength * 0.4; // Control point distance
                
                Point3d control1 = startPt + tangentStart * controlDist;
                Point3d control2 = endPt - tangentEnd * controlDist;
                
                int steps = Math.Max(10, (int)Math.Ceiling(bridgeLength / (spacing * 0.3)));
                steps = Math.Min(steps, 50); // Reasonable upper limit
                
                for (int i = 1; i < steps; i++)
                {
                    double t = (double)i / steps;
                    
                    // Cubic Bezier curve formula
                    Point3d pt = Math.Pow(1 - t, 3) * startPt +
                                 3 * Math.Pow(1 - t, 2) * t * control1 +
                                 3 * (1 - t) * t * t * control2 +
                                 Math.Pow(t, 3) * endPt;
                    
                    bridge.Add(pt);
                }
            }
            catch
            {
                // Fallback: empty (caller will use default connection)
            }
            
            return bridge;
        }

        /// <summary>
        /// Samples a curve starting from the seam position.
        /// </summary>
        /// <param name="curve">Curve to sample</param>
        /// <param name="spacing">Spacing between sample points</param>
        /// <param name="normalizedSeamT">Normalized length parameter [0,1] for seam position</param>
        /// <param name="clockwise">True for clockwise sampling, false for counterclockwise</param>
        private List<Point3d> SampleCurveFromSeam(Curve curve, double spacing, double normalizedSeamT, bool clockwise)
        {
            var points = new List<Point3d>();
            
            if (curve == null || !curve.IsValid)
                return points;

            double length = curve.GetLength();
            if (length <= 0)
                return points;

            int numPoints = Math.Max(2, (int)Math.Ceiling(length / spacing) + 1);

            // Ensure normalizedSeamT is in [0, 1]
            double seamT = normalizedSeamT;
            if (seamT < 0) seamT = 0;
            if (seamT > 1) seamT = 1;

            // Start from seam position
            double t;
            if (!curve.NormalizedLengthParameter(seamT, out t))
            {
                // Fallback: use start point
                points.Add(curve.PointAtStart);
            }
            else
            {
                points.Add(curve.PointAt(t));
            }

            // Sample points along curve
            for (int i = 1; i < numPoints; i++)
            {
                double offset = (double)i / numPoints;
                double sampleT = clockwise ? (seamT + offset) % 1.0 : (seamT - offset + 1.0) % 1.0;
                
                // Ensure sampleT is in [0, 1]
                if (sampleT < 0) sampleT += 1.0;
                if (sampleT > 1) sampleT -= 1.0;
                
                if (curve.NormalizedLengthParameter(sampleT, out t))
                {
                    points.Add(curve.PointAt(t));
                }
            }

            return points;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillSpiralIcon.png");
        public override Guid ComponentGuid => new Guid("cc2885de-9333-4d25-aa4b-7f0b16f31256");
    }
}

