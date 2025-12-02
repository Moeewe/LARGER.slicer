using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Graph node representing a point in the pattern.
    /// </summary>
    public class GraphNode
    {
        public Point3d Point { get; set; }
        public int Id { get; set; }
        public List<GraphEdge> Edges { get; set; }

        public GraphNode(Point3d point, int id)
        {
            Point = point;
            Id = id;
            Edges = new List<GraphEdge>();
        }

        public int Degree => Edges.Count;
        public bool IsOddDegree => Degree % 2 == 1;
    }

    /// <summary>
    /// Graph edge representing a segment between two nodes.
    /// </summary>
    public class GraphEdge
    {
        public GraphNode Start { get; set; }
        public GraphNode End { get; set; }
        public Curve Curve { get; set; }
        public int Id { get; set; }
        public bool IsDuplicate { get; set; } // For Chinese Postman duplicates

        public GraphEdge(GraphNode start, GraphNode end, Curve curve, int id)
        {
            Start = start;
            End = end;
            Curve = curve;
            Id = id;
            IsDuplicate = false;
        }

        public GraphNode GetOtherNode(GraphNode node)
        {
            if (node == Start) return End;
            if (node == End) return Start;
            return null;
        }

        public double Length => Curve?.GetLength() ?? Start.Point.DistanceTo(End.Point);
    }

    /// <summary>
    /// Graph structure for modeling pattern segments.
    /// Supports Euler path finding and Chinese Postman problem solving.
    /// </summary>
    public class PatternGraph
    {
        public List<GraphNode> Nodes { get; set; }
        public List<GraphEdge> Edges { get; set; }
        private int _nextNodeId = 0;
        private int _nextEdgeId = 0;
        
        // Spatial hash for fast node lookup (performance optimization)
        private Dictionary<string, GraphNode> _nodeHash;
        private double _tolerance;

        public PatternGraph()
        {
            Nodes = new List<GraphNode>();
            Edges = new List<GraphEdge>();
            _nodeHash = new Dictionary<string, GraphNode>();
            _tolerance = 0.01;
        }

        /// <summary>
        /// Adds a curve segment to the graph, creating nodes at endpoints.
        /// </summary>
        public void AddCurve(Curve curve, double tolerance = 0.01)
        {
            if (curve == null || !curve.IsValid)
                return;

            _tolerance = tolerance;

            Point3d startPt = curve.PointAtStart;
            Point3d endPt = curve.PointAtEnd;

            // Find or create nodes for endpoints
            GraphNode startNode = FindOrCreateNode(startPt, tolerance);
            GraphNode endNode = FindOrCreateNode(endPt, tolerance);

            // Skip self-loops (start and end are the same node)
            if (startNode == endNode)
                return;

            // Create edge
            GraphEdge edge = new GraphEdge(startNode, endNode, curve.DuplicateCurve(), _nextEdgeId++);
            Edges.Add(edge);
            startNode.Edges.Add(edge);
            endNode.Edges.Add(edge);
        }

        /// <summary>
        /// Finds an existing node near the point, or creates a new one.
        /// Uses spatial hashing for O(1) lookup performance.
        /// </summary>
        private GraphNode FindOrCreateNode(Point3d point, double tolerance)
        {
            // Ensure tolerance is valid
            if (tolerance <= 0)
                tolerance = 0.01;

            // Create spatial hash key (round to tolerance grid)
            double gridSize = Math.Max(tolerance * 2.0, 0.001); // Use 2x tolerance for hash grid, minimum 0.001
            int x = (int)Math.Round(point.X / gridSize);
            int y = (int)Math.Round(point.Y / gridSize);
            int z = (int)Math.Round(point.Z / gridSize);
            string hashKey = $"{x}_{y}_{z}";

            // Check hash first (fast path)
            if (_nodeHash.TryGetValue(hashKey, out GraphNode existingNode))
            {
                // Verify distance is within tolerance (hash collision check)
                if (existingNode != null && existingNode.Point.DistanceTo(point) <= tolerance)
                {
                    return existingNode;
                }
            }

            // Check nearby hash cells (for edge cases)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        string nearbyKey = $"{x + dx}_{y + dy}_{z + dz}";
                        if (_nodeHash.TryGetValue(nearbyKey, out GraphNode nearbyNode))
                        {
                            if (nearbyNode != null && nearbyNode.Point.DistanceTo(point) <= tolerance)
                            {
                                // Update hash to point to this node
                                _nodeHash[hashKey] = nearbyNode;
                                return nearbyNode;
                            }
                        }
                    }
                }
            }

            // No existing node found, create new one
            GraphNode newNode = new GraphNode(point, _nextNodeId++);
            Nodes.Add(newNode);
            _nodeHash[hashKey] = newNode;
            return newNode;
        }

        /// <summary>
        /// Gets all nodes with odd degree (needed for Chinese Postman).
        /// </summary>
        public List<GraphNode> GetOddDegreeNodes()
        {
            return Nodes.Where(n => n.IsOddDegree).ToList();
        }

        /// <summary>
        /// Checks if the graph is Eulerian (all nodes have even degree).
        /// </summary>
        public bool IsEulerian()
        {
            return Nodes.All(n => !n.IsOddDegree);
        }

        /// <summary>
        /// Checks if the graph is connected (all nodes reachable from any node).
        /// </summary>
        public bool IsConnected()
        {
            if (Nodes.Count == 0)
                return true;

            var visited = new HashSet<int>();
            var stack = new Stack<GraphNode>();
            stack.Push(Nodes[0]);
            visited.Add(Nodes[0].Id);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var edge in current.Edges)
                {
                    var neighbor = edge.GetOtherNode(current);
                    if (neighbor != null && !visited.Contains(neighbor.Id))
                    {
                        visited.Add(neighbor.Id);
                        stack.Push(neighbor);
                    }
                }
            }

            return visited.Count == Nodes.Count;
        }

        /// <summary>
        /// Gets connected components of the graph.
        /// </summary>
        public List<List<GraphNode>> GetConnectedComponents()
        {
            var components = new List<List<GraphNode>>();
            var visited = new HashSet<int>();

            foreach (var node in Nodes)
            {
                if (!visited.Contains(node.Id))
                {
                    var component = new List<GraphNode>();
                    var stack = new Stack<GraphNode>();
                    stack.Push(node);
                    visited.Add(node.Id);
                    component.Add(node);

                    while (stack.Count > 0)
                    {
                        var current = stack.Pop();
                        foreach (var edge in current.Edges)
                        {
                            var neighbor = edge.GetOtherNode(current);
                            if (neighbor != null && !visited.Contains(neighbor.Id))
                            {
                                visited.Add(neighbor.Id);
                                component.Add(neighbor);
                                stack.Push(neighbor);
                            }
                        }
                    }

                    components.Add(component);
                }
            }

            return components;
        }

        /// <summary>
        /// Adds a duplicate edge (for Chinese Postman problem).
        /// </summary>
        public void AddDuplicateEdge(GraphEdge originalEdge)
        {
            GraphEdge duplicate = new GraphEdge(originalEdge.Start, originalEdge.End, 
                originalEdge.Curve?.DuplicateCurve(), _nextEdgeId++);
            duplicate.IsDuplicate = true;
            Edges.Add(duplicate);
            originalEdge.Start.Edges.Add(duplicate);
            originalEdge.End.Edges.Add(duplicate);
        }

        /// <summary>
        /// Adds a bridge edge between two nodes.
        /// </summary>
        public void AddBridgeEdge(GraphNode node1, GraphNode node2)
        {
            Line bridgeLine = new Line(node1.Point, node2.Point);
            Curve bridgeCurve = new LineCurve(bridgeLine);
            GraphEdge bridge = new GraphEdge(node1, node2, bridgeCurve, _nextEdgeId++);
            Edges.Add(bridge);
            node1.Edges.Add(bridge);
            node2.Edges.Add(bridge);
        }
    }
}

