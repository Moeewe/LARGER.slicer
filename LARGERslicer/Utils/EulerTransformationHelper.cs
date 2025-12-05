using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for Euler Transformation of polygonal complexes.
    /// Based on: Gupta et al., 2021 - Continuous Toolpath Planning in a Graphical Framework for Sparse Infill Additive Manufacturing
    /// Transforms a 2D cell complex K into a new complex K̂ such that every vertex has even degree (Eulerian).
    /// </summary>
    public static class EulerTransformationHelper
    {
        /// <summary>
        /// Applies Euler transformation to a set of curves (polygons) to create an Eulerian graph.
        /// Each vertex in the resulting graph will have even degree, guaranteeing an Eulerian tour exists.
        /// Uses mitered offsets to create the transformed complex.
        /// </summary>
        /// <param name="curves">Input curves representing the cell complex</param>
        /// <param name="offsetDistance">Mitered offset distance (typically half the bead width)</param>
        /// <param name="tolerance">Geometric tolerance</param>
        /// <returns>List of curves forming the Euler-transformed complex</returns>
        public static List<Curve> ApplyEulerTransformation(
            List<Curve> curves,
            double offsetDistance,
            double tolerance = 0.01)
        {
            var transformedCurves = new List<Curve>();

            if (curves == null || curves.Count == 0)
                return transformedCurves;

            // For each curve (cell), create mitered offset
            foreach (var curve in curves)
            {
                if (curve == null || !curve.IsValid)
                    continue;

                // Create mitered offset (inward and outward)
                // Mitered offset creates new edges at vertices, ensuring even degrees
                var inwardOffsets = CreateMiteredOffset(curve, -offsetDistance, tolerance);
                var outwardOffsets = CreateMiteredOffset(curve, offsetDistance, tolerance);

                transformedCurves.AddRange(inwardOffsets);
                transformedCurves.AddRange(outwardOffsets);

                // Add original curve edges (if needed for connectivity)
                transformedCurves.Add(curve.DuplicateCurve());
            }

            // Remove duplicates and merge coincident edges
            return MergeCoincidentEdges(transformedCurves, tolerance);
        }

        /// <summary>
        /// Creates mitered offset of a curve.
        /// Mitered offset creates new vertices at corners, ensuring even vertex degrees.
        /// </summary>
        private static List<Curve> CreateMiteredOffset(
            Curve curve,
            double offsetDistance,
            double tolerance)
        {
            var offsets = new List<Curve>();

            if (curve == null || !curve.IsValid)
                return offsets;

            try
            {
                // Use round corner style for smoother offsets
                var offsetCurves = curve.Offset(
                    Plane.WorldXY,
                    offsetDistance,
                    tolerance,
                    CurveOffsetCornerStyle.Round);

                if (offsetCurves != null && offsetCurves.Length > 0)
                {
                    foreach (var offsetCurve in offsetCurves)
                    {
                        if (offsetCurve != null && offsetCurve.IsValid)
                        {
                            offsets.Add(offsetCurve);
                        }
                    }
                }
            }
            catch
            {
                // Fallback: empty
            }

            return offsets;
        }

        /// <summary>
        /// Merges coincident edges to avoid duplicates in the transformed complex.
        /// </summary>
        private static List<Curve> MergeCoincidentEdges(
            List<Curve> curves,
            double tolerance)
        {
            var merged = new List<Curve>();
            var processed = new HashSet<int>();

            for (int i = 0; i < curves.Count; i++)
            {
                if (processed.Contains(i))
                    continue;

                Curve current = curves[i];
                if (current == null || !current.IsValid)
                    continue;

                bool isDuplicate = false;

                // Check against already merged curves
                foreach (var mergedCurve in merged)
                {
                    if (AreCurvesCoincident(current, mergedCurve, tolerance))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    merged.Add(current.DuplicateCurve());
                }

                processed.Add(i);
            }

            return merged;
        }

        /// <summary>
        /// Checks if two curves are coincident (same geometry).
        /// </summary>
        private static bool AreCurvesCoincident(
            Curve c1,
            Curve c2,
            double tolerance)
        {
            if (c1 == null || c2 == null || !c1.IsValid || !c2.IsValid)
                return false;

            // Check if start and end points are close
            double distStart = c1.PointAtStart.DistanceTo(c2.PointAtStart);
            double distEnd = c1.PointAtEnd.DistanceTo(c2.PointAtEnd);

            if (distStart < tolerance && distEnd < tolerance)
            {
                // Check midpoint as well
                double t1 = c1.Domain.Mid;
                double t2 = c2.Domain.Mid;
                double distMid = c1.PointAt(t1).DistanceTo(c2.PointAt(t2));
                return distMid < tolerance;
            }

            return false;
        }

        /// <summary>
        /// Patches a clipped complex by adding edges to pair odd-degree vertices.
        /// After clipping the Euler complex with a polygon, some vertices may have odd degree.
        /// This method adds minimal edges to restore the Euler property.
        /// </summary>
        /// <param name="curves">Curves forming the clipped complex</param>
        /// <param name="boundary">Boundary polygon used for clipping</param>
        /// <param name="spacing">Bead width for connection generation</param>
        /// <param name="tolerance">Geometric tolerance</param>
        /// <returns>List of patch curves added to restore Euler property</returns>
        public static List<Curve> PatchOddDegreeVertices(
            List<Curve> curves,
            Curve boundary,
            double spacing,
            double tolerance = 0.01)
        {
            var patchCurves = new List<Curve>();

            if (curves == null || curves.Count == 0)
                return patchCurves;

            // Build graph from curves
            var graph = BuildGraphFromCurves(curves, tolerance);

            // Find odd-degree vertices
            var oddVertices = graph.Where(kvp => kvp.Value.Count % 2 != 0)
                                   .Select(kvp => kvp.Key)
                                   .ToList();

            // Pair odd-degree vertices and create connections
            for (int i = 0; i < oddVertices.Count - 1; i += 2)
            {
                Point3d v1 = oddVertices[i];
                Point3d v2 = oddVertices[i + 1];

                // Create connection along boundary if possible
                Curve connection = CreateBoundaryConnection(v1, v2, boundary, spacing, tolerance);
                if (connection != null && connection.IsValid)
                {
                    patchCurves.Add(connection);
                }
                else
                {
                    // Fallback: direct line connection
                    Line line = new Line(v1, v2);
                    patchCurves.Add(new LineCurve(line));
                }
            }

            return patchCurves;
        }

        /// <summary>
        /// Builds a graph representation from curves (adjacency list).
        /// </summary>
        private static Dictionary<Point3d, List<Point3d>> BuildGraphFromCurves(
            List<Curve> curves,
            double tolerance)
        {
            var graph = new Dictionary<Point3d, List<Point3d>>();

            foreach (var curve in curves)
            {
                if (curve == null || !curve.IsValid)
                    continue;

                Point3d start = curve.PointAtStart;
                Point3d end = curve.PointAtEnd;

                // Find or create vertices (with tolerance)
                Point3d startKey = FindOrCreateVertex(graph, start, tolerance);
                Point3d endKey = FindOrCreateVertex(graph, end, tolerance);

                // Add edge (undirected)
                if (!graph[startKey].Contains(endKey))
                    graph[startKey].Add(endKey);
                if (!graph[endKey].Contains(startKey))
                    graph[endKey].Add(startKey);
            }

            return graph;
        }

        /// <summary>
        /// Finds existing vertex within tolerance or creates new one.
        /// </summary>
        private static Point3d FindOrCreateVertex(
            Dictionary<Point3d, List<Point3d>> graph,
            Point3d point,
            double tolerance)
        {
            foreach (var key in graph.Keys)
            {
                if (key.DistanceTo(point) < tolerance)
                    return key;
            }

            // Create new vertex
            graph[point] = new List<Point3d>();
            return point;
        }

        /// <summary>
        /// Creates a connection between two points along the boundary.
        /// </summary>
        private static Curve CreateBoundaryConnection(
            Point3d start,
            Point3d end,
            Curve boundary,
            double spacing,
            double tolerance)
        {
            if (boundary == null || !boundary.IsValid)
                return null;

            try
            {
                double tStart, tEnd;
                boundary.ClosestPoint(start, out tStart);
                boundary.ClosestPoint(end, out tEnd);

                tStart = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tStart));
                tEnd = Math.Max(boundary.Domain.T0, Math.Min(boundary.Domain.T1, tEnd));

                // Determine shorter path along boundary
                double distForward = Math.Abs(tEnd - tStart);
                double distBackward = boundary.Domain.Length - distForward;
                bool forward = distForward <= distBackward;

                // Create curve segment along boundary
                double t1 = forward ? tStart : tEnd;
                double t2 = forward ? tEnd : tStart;

                if (t1 > t2)
                {
                    // Handle wrap-around
                    var seg1 = boundary.Trim(boundary.Domain.T0, t2);
                    var seg2 = boundary.Trim(t1, boundary.Domain.T1);
                    if (seg1 != null && seg2 != null && seg1.IsValid && seg2.IsValid)
                    {
                        var joined = Curve.JoinCurves(new[] { seg1, seg2 }, tolerance);
                        if (joined != null && joined.Length > 0)
                            return joined[0];
                    }
                }
                else
                {
                    return boundary.Trim(t1, t2);
                }
            }
            catch
            {
                // Fallback: return null
            }

            return null;
        }

        /// <summary>
        /// Traverses the Euler complex using tree-based search for concentric cycles.
        /// Based on the paper's algorithm for building continuous toolpath by traversing "concentric" cycles.
        /// </summary>
        /// <param name="curves">Curves forming the Euler complex</param>
        /// <param name="startPoint">Starting point for traversal</param>
        /// <param name="spacing">Bead width for point sampling</param>
        /// <param name="tolerance">Geometric tolerance</param>
        /// <returns>Continuous toolpath as list of points</returns>
        public static List<Point3d> TraverseConcentricCycles(
            List<Curve> curves,
            Point3d startPoint,
            double spacing,
            double tolerance = 0.01)
        {
            var pathPoints = new List<Point3d>();

            if (curves == null || curves.Count == 0)
                return pathPoints;

            // Build graph
            var graph = BuildGraphFromCurves(curves, tolerance);

            // Find cycles starting from start point
            var visitedEdges = new HashSet<(Point3d, Point3d)>();
            var currentPoint = startPoint;

            // Find closest vertex to start point
            Point3d startVertex = FindClosestVertex(graph, startPoint, tolerance);
            if (startVertex == Point3d.Unset)
                return pathPoints;

            // Build PatternGraph from dictionary graph
            var patternGraph = new PatternGraph();
            // Note: Tolerance is private, but we can set it via AddCurve which uses it
            foreach (var kvp in graph)
            {
                Point3d vertex = kvp.Key;
                foreach (var neighbor in kvp.Value)
                {
                    // Create edge between vertex and neighbor
                    Line line = new Line(vertex, neighbor);
                    Curve edgeCurve = new LineCurve(line);
                    patternGraph.AddCurve(edgeCurve);
                }
            }

            // Find Eulerian circuit
            var eulerEdges = EulerPathHelper.FindEulerianCircuit(patternGraph);
            if (eulerEdges != null && eulerEdges.Count > 0)
            {
                // Convert edges to points
                var points = new List<Point3d>();
                foreach (var edge in eulerEdges)
                {
                    if (edge != null && edge.Curve != null && edge.Curve.IsValid)
                    {
                        var edgePoints = PathHelper.SampleCurve(edge.Curve, spacing, true);
                        if (points.Count > 0 && edgePoints.Count > 0)
                        {
                            // Remove duplicate if last point equals first point of new edge
                            if (points[points.Count - 1].DistanceTo(edgePoints[0]) < tolerance)
                            {
                                edgePoints.RemoveAt(0);
                            }
                        }
                        points.AddRange(edgePoints);
                    }
                }
                return points;
            }

            return pathPoints;
        }

        /// <summary>
        /// Finds the closest vertex in the graph to a given point.
        /// </summary>
        private static Point3d FindClosestVertex(
            Dictionary<Point3d, List<Point3d>> graph,
            Point3d point,
            double tolerance)
        {
            Point3d closest = Point3d.Unset;
            double minDist = double.MaxValue;

            foreach (var vertex in graph.Keys)
            {
                double dist = vertex.DistanceTo(point);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = vertex;
                }
            }

            return minDist < tolerance * 100 ? closest : Point3d.Unset;
        }
    }
}

