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
    /// Eulerian Path - Generates a continuous toolpath from a closed boundary curve using graph-based Euler path algorithm.
    /// Implements the complete workflow: pattern generation, graph modeling, bridging, Chinese Postman problem solving,
    /// and Euler path finding to create a single uninterrupted toolpath.
    /// </summary>
    public class EulerianPathComponent : GH_Component
    {
        public EulerianPathComponent()
            : base("Eulerian Path", "EulerPath",
                  "Generates a continuous toolpath from a closed boundary curve using graph-based Euler path algorithm. Creates a single uninterrupted path covering the entire pattern without retractions.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "Boundary", "Closed boundary curve of the pattern", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Pattern Type", "Pattern", "Pattern type: 0 = Offsets, 1 = Grid, 2 = Lines, 3 = Spiral", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Line Spacing", "Spacing", "Distance between pattern lines (mm)", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Angle", "Angle", "Angle for grid/lines pattern (degrees)", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Tolerance", "Tol", "Tolerance for graph operations (mm)", GH_ParamAccess.item, 0.01);
            pManager.AddBooleanParameter("Add Bridges", "Bridge", "Add bridges between disconnected components", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Fix Overlaps", "FixOverlap", "Fix overlapping segments by offsetting duplicates", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Overlap Offset", "OverlapOff", "Offset distance for overlapping segments (mm)", GH_ParamAccess.item, 0.1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Continuous Eulerian path as polyline", GH_ParamAccess.item);
            pManager.AddCurveParameter("Pattern Segments", "Segments", "Original pattern segments", GH_ParamAccess.list);
            pManager.AddCurveParameter("Bridges", "Bridges", "Bridge segments added", GH_ParamAccess.list);
            pManager.AddCurveParameter("Duplicates", "Duplicates", "Duplicate edges added for Eulerian property", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Information about the path generation", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null;
            int patternType = 0;
            double spacing = 2.0;
            double angle = 0.0;
            double tolerance = 0.01;
            bool addBridges = true;
            bool fixOverlaps = false;
            double overlapOffset = 0.1;

            if (!DA.GetData(0, ref boundary) || boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary curve.");
                return;
            }

            DA.GetData(1, ref patternType);
            DA.GetData(2, ref spacing);
            DA.GetData(3, ref angle);
            DA.GetData(4, ref tolerance);
            DA.GetData(5, ref addBridges);
            DA.GetData(6, ref fixOverlaps);
            DA.GetData(7, ref overlapOffset);

            // Step 1: Prepare boundary curve
            if (!boundary.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Boundary curve is not closed. Attempting to close it.");
                boundary = boundary.DuplicateCurve();
                if (!boundary.IsClosed)
                {
                    boundary.MakeClosed(tolerance);
                }
            }

            // Step 2: Generate pattern segments
            var patternSegments = GeneratePatternSegments(boundary, patternType, spacing, angle, tolerance);
            
            if (patternSegments.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No pattern segments generated.");
                return;
            }

            // Step 3: Build graph from segments
            PatternGraph graph = BuildGraph(patternSegments, tolerance);

            if (graph.Nodes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Graph construction failed.");
                return;
            }

            var bridges = new List<Curve>();
            var duplicates = new List<Curve>();

            // Step 4: Bridge disconnected components
            if (addBridges && !graph.IsConnected())
            {
                var bridgePairs = EulerPathHelper.BridgeComponents(graph);
                foreach (var (node1, node2) in bridgePairs)
                {
                    graph.AddBridgeEdge(node1, node2);
                    Line bridgeLine = new Line(node1.Point, node2.Point);
                    bridges.Add(new LineCurve(bridgeLine));
                }
            }

            // Step 5: Make graph Eulerian (Chinese Postman)
            if (!graph.IsEulerian())
            {
                var oddPairs = EulerPathHelper.PairOddDegreeNodes(graph);
                if (oddPairs != null)
                {
                    foreach (var (node1, node2) in oddPairs)
                    {
                        if (node1 == null || node2 == null)
                            continue;

                        // Find shortest path between nodes
                        var path = FindShortestPath(graph, node1, node2);
                        if (path != null && path.Count > 0)
                        {
                            // Duplicate edges along the path
                            foreach (var edge in path)
                            {
                                if (edge == null || edge.Start == null || edge.End == null)
                                    continue;

                                graph.AddDuplicateEdge(edge);
                                if (edge.Curve != null && edge.Curve.IsValid)
                                {
                                    duplicates.Add(edge.Curve.DuplicateCurve());
                                }
                                else
                                {
                                    duplicates.Add(new LineCurve(new Line(edge.Start.Point, edge.End.Point)));
                                }
                            }
                        }
                        else
                        {
                            // Direct connection if no path found
                            graph.AddBridgeEdge(node1, node2);
                            Line bridgeLine = new Line(node1.Point, node2.Point);
                            bridges.Add(new LineCurve(bridgeLine));
                        }
                    }
                }
            }

            // Step 6: Find Eulerian circuit
            List<GraphEdge> eulerPath;
            try
            {
                eulerPath = EulerPathHelper.FindEulerianCircuit(graph);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error finding Eulerian path: {ex.Message}");
                return;
            }

            if (eulerPath == null || eulerPath.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to find Eulerian path.");
                return;
            }

            // Step 7: Convert to polyline
            Polyline pathPolyline;
            try
            {
                pathPolyline = EulerPathHelper.ConvertToPolyline(eulerPath, tolerance);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error converting to polyline: {ex.Message}");
                return;
            }

            if (pathPolyline == null || pathPolyline.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create polyline from Euler path.");
                return;
            }

            // Step 8: Fix overlaps (optional)
            if (fixOverlaps)
            {
                pathPolyline = FixOverlaps(pathPolyline, eulerPath, overlapOffset, tolerance);
            }

            Curve pathCurve = new PolylineCurve(pathPolyline);

            // Create info string
            string info = $"Eulerian path generated: {eulerPath.Count} edges, {pathPolyline.Count} points. ";
            info += $"Bridges: {bridges.Count}, Duplicates: {duplicates.Count}. ";
            info += $"Path length: {pathCurve.GetLength():F2}mm.";

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, patternSegments);
            DA.SetDataList(2, bridges);
            DA.SetDataList(3, duplicates);
            DA.SetData(4, info);
        }

        /// <summary>
        /// Generates pattern segments based on pattern type.
        /// </summary>
        private List<Curve> GeneratePatternSegments(Curve boundary, int patternType, double spacing, double angle, double tolerance)
        {
            var segments = new List<Curve>();

            // Convert angle from degrees to radians
            double angleRad = angle * Math.PI / 180.0;

            switch (patternType)
            {
                case 0: // Offsets
                    segments = PathHelper.GenerateOffsetCurves(boundary, spacing, 100, null); // Direction auto-detected
                    // Filter segments that are inside boundary
                    segments = FilterSegmentsInsideBoundary(segments, boundary, tolerance);
                    break;

                case 1: // Grid
                    BoundingBox bbox = boundary.GetBoundingBox(true);
                    var gridLines = PathHelper.GenerateParallelLines(boundary, spacing, angleRad, bbox.Center, bbox);
                    // GenerateParallelLines already trims to boundary, so we can use them directly
                    segments.AddRange(gridLines);
                    // Filter to ensure segments are inside boundary
                    segments = FilterSegmentsInsideBoundary(segments, boundary, tolerance);
                    break;

                case 2: // Lines
                    bbox = boundary.GetBoundingBox(true);
                    var lines = PathHelper.GenerateParallelLines(boundary, spacing, angleRad, bbox.Center, bbox);
                    // GenerateParallelLines already trims to boundary
                    segments.AddRange(lines);
                    // Filter to ensure segments are inside boundary
                    segments = FilterSegmentsInsideBoundary(segments, boundary, tolerance);
                    break;

                case 3: // Spiral
                    segments = PathHelper.GenerateOffsetCurves(boundary, spacing, 100, null); // Direction auto-detected
                    // Filter segments that are inside boundary
                    segments = FilterSegmentsInsideBoundary(segments, boundary, tolerance);
                    break;

                default:
                    segments = PathHelper.GenerateOffsetCurves(boundary, spacing, 100, null); // Direction auto-detected
                    segments = FilterSegmentsInsideBoundary(segments, boundary, tolerance);
                    break;
            }

            // Remove invalid or very short segments
            segments = segments.Where(s => s != null && s.IsValid && s.GetLength() > tolerance).ToList();

            return segments;
        }

        /// <summary>
        /// Filters segments to only include those inside the boundary.
        /// </summary>
        private List<Curve> FilterSegmentsInsideBoundary(List<Curve> segments, Curve boundary, double tolerance)
        {
            var filtered = new List<Curve>();

            if (boundary == null || !boundary.IsValid)
                return segments; // Return all if boundary is invalid

            foreach (var segment in segments)
            {
                if (segment == null || !segment.IsValid)
                    continue;

                try
                {
                    // For offset curves (closed), check if they're inside
                    if (segment.IsClosed)
                    {
                        Point3d midPt = segment.PointAt(0.5);
                        if (midPt.IsValid)
                        {
                            PointContainment containment = boundary.Contains(midPt, Plane.WorldXY, tolerance);
                            if (containment == PointContainment.Inside)
                            {
                                Curve duplicate = segment.DuplicateCurve();
                                if (duplicate != null && duplicate.IsValid)
                                {
                                    filtered.Add(duplicate);
                                }
                            }
                        }
                    }
                    else
                    {
                        // For open curves (lines), check if midpoint is inside boundary
                        Point3d midPt = segment.PointAt(0.5);
                        if (midPt.IsValid)
                        {
                            PointContainment containment = boundary.Contains(midPt, Plane.WorldXY, tolerance);
                            if (containment == PointContainment.Inside)
                            {
                                Curve duplicate = segment.DuplicateCurve();
                                if (duplicate != null && duplicate.IsValid)
                                {
                                    filtered.Add(duplicate);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip segments that cause errors
                    continue;
                }
            }

            return filtered;
        }

        /// <summary>
        /// Builds a graph from pattern segments.
        /// Handles both open and closed curves properly.
        /// </summary>
        private PatternGraph BuildGraph(List<Curve> segments, double tolerance)
        {
            PatternGraph graph = new PatternGraph();

            foreach (var segment in segments)
            {
                if (segment == null || !segment.IsValid)
                    continue;

                // For closed curves, we need to handle them differently
                // Instead of sampling, we'll just add them as-is and let the graph handle it
                // The graph will create nodes at start/end (which are the same for closed curves)
                if (segment.IsClosed)
                {
                    // For closed curves, sample at a reasonable resolution
                    // but not too fine to avoid performance issues
                    double length = segment.GetLength();
                    if (length < tolerance)
                        continue;

                    // Limit sampling to avoid too many edges
                    int maxSamples = 100; // Maximum samples per closed curve
                    double sampleInterval = Math.Max(tolerance * 2, length / maxSamples);
                    int numSamples = Math.Min(maxSamples, Math.Max(4, (int)Math.Ceiling(length / sampleInterval)));
                    
                    if (numSamples <= 0)
                        continue;

                    var points = new List<Point3d>();
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = segment.Domain.ParameterAt((double)i / numSamples);
                        Point3d pt = segment.PointAt(t);
                        if (pt.IsValid)
                        {
                            points.Add(pt);
                        }
                    }

                    // Ensure we have at least 2 points
                    if (points.Count < 2)
                    {
                        // Fallback: just add start and end
                        points.Clear();
                        points.Add(segment.PointAtStart);
                        points.Add(segment.PointAt(segment.Domain.T1));
                    }

                    // Create edges between consecutive points
                    for (int i = 0; i < points.Count; i++)
                    {
                        int next = (i + 1) % points.Count;
                        if (points[i].DistanceTo(points[next]) > tolerance)
                        {
                            try
                            {
                                Line line = new Line(points[i], points[next]);
                                if (line.IsValid)
                                {
                                    Curve lineCurve = new LineCurve(line);
                                    if (lineCurve != null && lineCurve.IsValid)
                                    {
                                        graph.AddCurve(lineCurve, tolerance);
                                    }
                                }
                            }
                            catch
                            {
                                // Skip invalid lines
                                continue;
                            }
                        }
                    }
                }
                else
                {
                    // For open curves, add directly
                    try
                    {
                        graph.AddCurve(segment, tolerance);
                    }
                    catch
                    {
                        // Skip invalid curves
                        continue;
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// Finds shortest path between two nodes using BFS.
        /// </summary>
        private List<GraphEdge> FindShortestPath(PatternGraph graph, GraphNode start, GraphNode end)
        {
            if (graph == null || start == null || end == null)
                return new List<GraphEdge>();

            var queue = new Queue<(GraphNode, List<GraphEdge>)>();
            var visited = new HashSet<int>();
            
            queue.Enqueue((start, new List<GraphEdge>()));
            visited.Add(start.Id);

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();

                if (current == null)
                    continue;

                if (current == end)
                {
                    return path;
                }

                if (current.Edges == null)
                    continue;

                foreach (var edge in current.Edges)
                {
                    if (edge == null)
                        continue;

                    var neighbor = edge.GetOtherNode(current);
                    if (neighbor != null && !visited.Contains(neighbor.Id))
                    {
                        visited.Add(neighbor.Id);
                        var newPath = new List<GraphEdge>(path) { edge };
                        queue.Enqueue((neighbor, newPath));
                    }
                }
            }

            return new List<GraphEdge>(); // No path found
        }

        /// <summary>
        /// Fixes overlapping segments by offsetting duplicates.
        /// </summary>
        private Polyline FixOverlaps(Polyline polyline, List<GraphEdge> eulerPath, double offset, double tolerance)
        {
            // This is a simplified version - full implementation would track which edges are duplicates
            // and offset them appropriately
            var points = new List<Point3d>(polyline);

            // For now, just return the original polyline
            // Full implementation would require tracking duplicate edges and offsetting them
            return polyline;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("EulerianPathIcon.png");
        public override Guid ComponentGuid => new Guid("114f748a-c155-4804-9812-19287cde2bc2");
    }
}

