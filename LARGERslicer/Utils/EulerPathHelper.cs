using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Helper class for finding Euler paths in graphs.
    /// Implements Hierholzer's algorithm for Eulerian circuits.
    /// </summary>
    public static class EulerPathHelper
    {
        /// <summary>
        /// Finds an Eulerian circuit in the graph using Hierholzer's algorithm.
        /// Returns the sequence of edges that form the Euler path.
        /// </summary>
        public static List<GraphEdge> FindEulerianCircuit(PatternGraph graph)
        {
            if (graph == null || graph.Nodes == null || graph.Edges == null)
                return new List<GraphEdge>();

            if (graph.Nodes.Count == 0 || graph.Edges.Count == 0)
                return new List<GraphEdge>();

            // Make a copy of edges for tracking (filter null edges)
            var validEdges = graph.Edges.Where(e => e != null).ToList();
            if (validEdges.Count == 0)
                return new List<GraphEdge>();

            var remainingEdges = new HashSet<GraphEdge>(validEdges);
            var circuit = new List<GraphEdge>();
            var currentPath = new Stack<GraphEdge>();

            // Start from any node with valid edges
            GraphNode currentNode = graph.Nodes.FirstOrDefault(n => n != null && n.Edges != null && n.Edges.Any(e => e != null && remainingEdges.Contains(e)));
            if (currentNode == null)
                return new List<GraphEdge>();

            GraphEdge currentEdge = currentNode.Edges.FirstOrDefault(e => e != null && remainingEdges.Contains(e));

            if (currentEdge == null)
                return new List<GraphEdge>();

            // Start the path
            currentPath.Push(currentEdge);
            remainingEdges.Remove(currentEdge);
            currentNode = currentEdge.GetOtherNode(currentNode);

            // Hierholzer's algorithm
            while (remainingEdges.Count > 0)
            {
                // Find next edge from current node
                if (currentNode == null || currentNode.Edges == null)
                    break;

                GraphEdge nextEdge = currentNode.Edges.FirstOrDefault(e => e != null && remainingEdges.Contains(e));

                if (nextEdge != null)
                {
                    // Continue the path
                    currentPath.Push(nextEdge);
                    remainingEdges.Remove(nextEdge);
                    GraphNode nextNode = nextEdge.GetOtherNode(currentNode);
                    if (nextNode == null)
                        break;
                    currentNode = nextNode;
                }
                else
                {
                    // No more edges from current node, add to circuit and backtrack
                    if (currentPath.Count > 0)
                    {
                        GraphEdge edge = currentPath.Pop();
                        if (edge == null)
                            break;
                        circuit.Insert(0, edge);
                        
                        // Update current node to the start of the popped edge
                        if (currentPath.Count > 0)
                        {
                            GraphEdge prevEdge = currentPath.Peek();
                            if (prevEdge != null)
                            {
                                GraphNode prevNode = prevEdge.GetOtherNode(currentNode);
                                if (prevNode != null)
                                    currentNode = prevNode;
                            }
                        }
                        else
                        {
                            // Find a node with remaining edges
                            var nodeWithEdges = graph.Nodes.FirstOrDefault(n => 
                                n != null && n.Edges != null && n.Edges.Any(e => e != null && remainingEdges.Contains(e)));
                            if (nodeWithEdges != null)
                            {
                                currentNode = nodeWithEdges;
                                GraphEdge newEdge = currentNode.Edges.FirstOrDefault(e => e != null && remainingEdges.Contains(e));
                                if (newEdge != null)
                                {
                                    currentPath.Push(newEdge);
                                    remainingEdges.Remove(newEdge);
                                    GraphNode newNode = newEdge.GetOtherNode(currentNode);
                                    if (newNode != null)
                                        currentNode = newNode;
                                    else
                                        break;
                                }
                            }
                            else
                            {
                                break; // No more nodes with edges
                            }
                        }
                    }
                    else
                    {
                        break; // No more paths possible
                    }
                }
            }

            // Add remaining edges from current path
            while (currentPath.Count > 0)
            {
                circuit.Insert(0, currentPath.Pop());
            }

            return circuit;
        }

        /// <summary>
        /// Solves the Chinese Postman problem: pairs odd-degree nodes to make graph Eulerian.
        /// Returns list of edges to duplicate/add.
        /// </summary>
        public static List<(GraphNode, GraphNode)> PairOddDegreeNodes(PatternGraph graph)
        {
            if (graph == null)
                return new List<(GraphNode, GraphNode)>();

            var oddNodes = graph.GetOddDegreeNodes();
            if (oddNodes == null)
                return new List<(GraphNode, GraphNode)>();

            var pairs = new List<(GraphNode, GraphNode)>();

            if (oddNodes.Count % 2 != 0)
            {
                // Should not happen if graph is valid, but handle gracefully
                return pairs;
            }

            // Greedy pairing: pair closest odd nodes
            var remaining = new List<GraphNode>(oddNodes.Where(n => n != null));

            while (remaining.Count >= 2)
            {
                GraphNode node1 = remaining[0];
                if (node1 == null)
                {
                    remaining.RemoveAt(0);
                    continue;
                }

                GraphNode bestMatch = null;
                double minDistance = double.MaxValue;

                // Find closest odd node
                for (int i = 1; i < remaining.Count; i++)
                {
                    GraphNode node2 = remaining[i];
                    if (node2 == null)
                        continue;

                    double distance = node1.Point.DistanceTo(node2.Point);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestMatch = node2;
                    }
                }

                if (bestMatch != null)
                {
                    pairs.Add((node1, bestMatch));
                    remaining.Remove(node1);
                    remaining.Remove(bestMatch);
                }
                else
                {
                    break;
                }
            }

            return pairs;
        }

        /// <summary>
        /// Connects disconnected components using minimal spanning tree approach.
        /// Returns list of bridge connections (node pairs).
        /// </summary>
        public static List<(GraphNode, GraphNode)> BridgeComponents(PatternGraph graph)
        {
            var components = graph.GetConnectedComponents();
            var bridges = new List<(GraphNode, GraphNode)>();

            if (components.Count <= 1)
                return bridges; // Already connected

            // For each pair of components, find closest nodes and connect
            for (int i = 0; i < components.Count - 1; i++)
            {
                var component1 = components[i];
                var component2 = components[i + 1];

                // Find closest pair of nodes between components
                GraphNode closest1 = null;
                GraphNode closest2 = null;
                double minDistance = double.MaxValue;

                foreach (var node1 in component1)
                {
                    foreach (var node2 in component2)
                    {
                        double distance = node1.Point.DistanceTo(node2.Point);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closest1 = node1;
                            closest2 = node2;
                        }
                    }
                }

                if (closest1 != null && closest2 != null)
                {
                    bridges.Add((closest1, closest2));
                }
            }

            return bridges;
        }

        /// <summary>
        /// Converts Euler path (sequence of edges) to a continuous polyline.
        /// </summary>
        public static Polyline ConvertToPolyline(List<GraphEdge> eulerPath, double tolerance = 0.01)
        {
            var points = new List<Point3d>();

            if (eulerPath == null || eulerPath.Count == 0)
                return new Polyline();

            // Start with first edge
            GraphEdge firstEdge = eulerPath[0];
            if (firstEdge == null || firstEdge.Start == null)
                return new Polyline();
            
            points.Add(firstEdge.Start.Point);

            // Sample points along first edge
            if (firstEdge.Curve != null && firstEdge.Curve.IsValid)
            {
                var sampled = PathHelper.SampleCurve(firstEdge.Curve, tolerance * 2, false);
                if (sampled.Count > 0)
                {
                    // Skip first point (already added)
                    points.AddRange(sampled.Skip(1));
                }
                else
                {
                    points.Add(firstEdge.End.Point);
                }
            }
            else
            {
                points.Add(firstEdge.End.Point);
            }

            // Process remaining edges
            for (int i = 1; i < eulerPath.Count; i++)
            {
                GraphEdge edge = eulerPath[i];
                GraphEdge prevEdge = eulerPath[i - 1];

                if (edge == null || edge.Start == null || edge.End == null)
                    continue;
                if (prevEdge == null || prevEdge.End == null)
                    continue;

                // Determine start point (should match end of previous edge)
                Point3d startPt = prevEdge.End.Point;
                Point3d endPt = edge.End.Point;

                // Check if we need to reverse the edge
                double distToStart = edge.Start.Point.DistanceTo(startPt);
                double distToEnd = edge.End.Point.DistanceTo(startPt);

                bool reverse = distToEnd < distToStart;

                if (edge.Curve != null && edge.Curve.IsValid)
                {
                    Curve curveToSample = reverse ? edge.Curve.DuplicateCurve() : edge.Curve;
                    if (reverse)
                    {
                        curveToSample.Reverse();
                    }

                    var sampled = PathHelper.SampleCurve(curveToSample, tolerance * 2, false);
                    if (sampled.Count > 0)
                    {
                        // Skip first point (should match last point)
                        if (sampled[0].DistanceTo(points[points.Count - 1]) > tolerance)
                        {
                            points.Add(sampled[0]);
                        }
                        points.AddRange(sampled.Skip(1));
                    }
                    else
                    {
                        if (reverse)
                        {
                            points.Add(edge.Start.Point);
                        }
                        else
                        {
                            points.Add(edge.End.Point);
                        }
                    }
                }
                else
                {
                    Point3d nextPt = reverse ? edge.Start.Point : edge.End.Point;
                    if (nextPt.DistanceTo(points[points.Count - 1]) > tolerance)
                    {
                        points.Add(nextPt);
                    }
                }
            }

            // Remove duplicate consecutive points
            var cleanedPoints = new List<Point3d> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].DistanceTo(cleanedPoints[cleanedPoints.Count - 1]) > tolerance * 0.1)
                {
                    cleanedPoints.Add(points[i]);
                }
            }

            return new Polyline(cleanedPoints);
        }
    }
}

