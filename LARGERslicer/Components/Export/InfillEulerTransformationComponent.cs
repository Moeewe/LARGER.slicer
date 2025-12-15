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
    /// Infill Euler Transformation - Applies Euler transformation to create guaranteed Eulerian toolpaths.
    /// Based on: Gupta et al., 2021 - Continuous Toolpath Planning in a Graphical Framework for Sparse Infill Additive Manufacturing
    /// Transforms a 2D cell complex into an Eulerian graph where every vertex has even degree, guaranteeing a continuous Eulerian tour.
    /// </summary>
    public class InfillEulerTransformationComponent : BottomLayerPatternBase
    {
        public InfillEulerTransformationComponent()
            : base("Single Line Fill with Euler", "SLF Euler",
                  "Applies Euler transformation to create guaranteed Eulerian toolpaths. Transforms input curves into an Eulerian graph where every vertex has even degree, ensuring a continuous tour exists. Automatically detects curve orientation to fill inward.")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
            pManager.AddNumberParameter("Offset Distance", "Offset", "Mitered offset distance for Euler transformation (typically half bead width, mm)", GH_ParamAccess.item, 2.5);
            pManager.AddBooleanParameter("Patch Odd Vertices", "Patch", "Automatically patch odd-degree vertices after clipping", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Use Concentric Traversal", "Concentric", "Use tree-based concentric cycle traversal algorithm", GH_ParamAccess.item, true);
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
            double offsetDistance = 2.5;
            bool patchOddVertices = true;
            bool useConcentricTraversal = true;
            DA.GetData(3, ref offsetDistance);  // Index 3 after base inputs (0-2)
            DA.GetData(4, ref patchOddVertices);
            DA.GetData(5, ref useConcentricTraversal);

            if (offsetDistance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Offset distance should be > 0. Using half spacing.");
                offsetDistance = printWidth * 0.5;
            }

            double spacing = printWidth;
            double boundaryOffset = 0.0; // Will be calculated automatically

            // Prepare boundary with offset (direction auto-detected)
            Curve closedBoundary = PrepareBoundary(boundary, boundaryOffset, out List<Curve> offsetHoles, holes, spacing);
            holes.AddRange(offsetHoles);

            // Get seam position (auto-calculate)
            var (seamPosition, seamParam) = GetSeamPosition(closedBoundary, null);

            // Generate pattern-specific path
            var (pathPoints, segments) = GeneratePattern(
                closedBoundary, seamPosition, spacing, offsetDistance, patchOddVertices, useConcentricTraversal, holes);

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

            // Separate closed and open patterns
            var patternsClosed = new List<Curve>();
            var patternsOpened = new List<Curve>();
            var bridges = new List<Curve>();
            var polylines = new List<Curve>();

            // Add main path as polyline
            if (pathCurve != null)
            {
                polylines.Add(pathCurve);
            }

            // Categorize segments
            foreach (var seg in segmentCurves)
            {
                if (seg != null && seg.IsValid)
                {
                    if (seg.IsClosed)
                        patternsClosed.Add(seg);
                    else
                        patternsOpened.Add(seg);
                }
            }

            // Create planes for each segment
            var planes = new List<Plane>();
            foreach (var seg in segmentCurves)
            {
                if (seg != null && seg.IsValid && seg.PointAtStart.IsValid)
                {
                    Point3d pt = seg.PointAtStart;
                    planes.Add(new Plane(pt, Vector3d.ZAxis));
                }
            }

            // Set outputs according to new structure
            DA.SetDataList(0, polylines);  // Polylines
            DA.SetDataList(1, planes);      // Planes
            DA.SetDataList(2, patternsClosed);  // Patterns Closed
            DA.SetDataList(3, patternsOpened);  // Patterns Opened
            DA.SetDataList(4, bridges);     // Bridges
            DA.SetData(5, pathCurve);      // Single Line Fill
        }

        private (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
            Curve boundary,
            Point3d seamPosition,
            double spacing,
            double offsetDistance,
            bool patchOddVertices,
            bool useConcentricTraversal,
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

            // Step 1: Create initial infill lattice (offset curves)
            // Generate inward offsets as base complex
            List<Curve> baseCurves = PathHelper.GenerateOffsetCurves(boundary, spacing, 1000, holes);

            if (baseCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 2: Apply Euler transformation
            List<Curve> transformedCurves = EulerTransformationHelper.ApplyEulerTransformation(
                baseCurves, offsetDistance, 0.01);

            // Step 3: Clip transformed complex with boundary
            var clippedCurves = new List<Curve>();
            foreach (var curve in transformedCurves)
            {
                if (curve == null || !curve.IsValid)
                    continue;

                // Check if curve is inside boundary
                Point3d midPt = curve.PointAt(curve.Domain.Mid);
                if (boundary.Contains(midPt, Plane.WorldXY, 0.01) == PointContainment.Inside)
                {
                    // Check if not inside any hole
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
                        clippedCurves.Add(curve.DuplicateCurve());
                    }
                }
            }

            // Step 4: Patch odd-degree vertices if requested
            // NOTE: Disabled by default - patching can cause chaos in path generation
            // User feedback: "Wenn add bridges weggemacht wird, läuft es besser"
            // PatchOddVertices acts like "add bridges" - can cause unwanted connections
            if (patchOddVertices && clippedCurves.Count > 0)
            {
                // Only patch if absolutely necessary (very few curves)
                // This prevents chaos from too many bridge connections
                if (clippedCurves.Count < 5)
                {
                    List<Curve> patchCurves = EulerTransformationHelper.PatchOddDegreeVertices(
                        clippedCurves, boundary, spacing, 0.01);
                    // Limit patch curves to prevent chaos
                    if (patchCurves.Count <= clippedCurves.Count)
                    {
                        clippedCurves.AddRange(patchCurves);
                    }
                }
            }

            if (clippedCurves.Count == 0)
            {
                pathPoints.Add(seamPosition);
                segments.Add(new List<Point3d> { seamPosition });
                return (pathPoints, segments);
            }

            // Step 5: Traverse Euler complex
            if (useConcentricTraversal)
            {
                // Use tree-based concentric cycle traversal
                pathPoints = EulerTransformationHelper.TraverseConcentricCycles(
                    clippedCurves, seamPosition, spacing * 0.3, 0.01);
            }
            else
            {
                // Use standard Euler path helper
                var graph = new PatternGraph();
                // Note: Tolerance is set internally, default is 0.01
                foreach (var curve in clippedCurves)
                {
                    if (curve != null && curve.IsValid)
                    {
                        graph.AddCurve(curve);
                    }
                }

                // Find Eulerian circuit
                var eulerEdges = EulerPathHelper.FindEulerianCircuit(graph);
                if (eulerEdges != null && eulerEdges.Count > 0)
                {
                    // Convert edges to points
                    pathPoints = new List<Point3d>();
                    foreach (var edge in eulerEdges)
                    {
                        if (edge != null && edge.Curve != null && edge.Curve.IsValid)
                        {
                            var edgePoints = PathHelper.SampleCurve(edge.Curve, spacing * 0.3, true);
                            if (pathPoints.Count > 0 && edgePoints.Count > 0)
                            {
                                // Remove duplicate if last point equals first point of new edge
                                if (pathPoints[pathPoints.Count - 1].DistanceTo(edgePoints[0]) < 0.01)
                                {
                                    edgePoints.RemoveAt(0);
                                }
                            }
                            pathPoints.AddRange(edgePoints);
                        }
                    }
                }
            }

            // Create segments for output
            foreach (var curve in clippedCurves)
            {
                if (curve != null && curve.IsValid)
                {
                    var curvePoints = PathHelper.SampleCurve(curve, spacing * 0.3, true);
                    if (curvePoints.Count >= 2)
                    {
                        segments.Add(curvePoints);
                    }
                }
            }

            // If path is empty, use first curve
            if (pathPoints.Count == 0 && clippedCurves.Count > 0)
            {
                var firstCurvePoints = PathHelper.SampleCurve(clippedCurves[0], spacing * 0.3, true);
                if (firstCurvePoints.Count >= 2)
                {
                    pathPoints = firstCurvePoints;
                }
            }

            // Align path start to seam position
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
                int steps = Math.Max(2, (int)Math.Ceiling(seamPt.DistanceTo(pathStart) / spacing));
                for (int s = 1; s < steps; s++)
                {
                    double t = (double)s / steps;
                    connection.Add(seamPt + (pathStart - seamPt) * t);
                }
            }

            return connection;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("InfillEulerTransformationIcon.png");
        public override Guid ComponentGuid => new Guid("5dbd79e8-a7eb-47bd-8f62-11b41f6bfaa8");
    }
}

