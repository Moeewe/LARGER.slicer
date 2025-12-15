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
    /// Optimized Toolpath - Combines multiple curves into an optimized continuous path.
    /// 
    /// WHAT IT DOES:
    /// This component takes multiple input curves (e.g., from different pattern components) and:
    /// 1. Removes self-intersections: Automatically splits curves at self-intersection points
    /// 2. Optimizes curve order: Reorders curves using nearest-neighbor algorithm to minimize travel distance
    /// 3. Adds connections: Creates connection curves between gaps (up to max gap distance)
    /// 4. Simplifies connections: Removes unnecessary connection points for cleaner paths
    /// 
    /// USE CASES:
    /// - Combine output from multiple pattern components (e.g., InfillLines + InfillContour)
    /// - Optimize toolpaths that have self-intersections or poor ordering
    /// - Create continuous paths from disconnected curve segments
    /// 
    /// OUTPUT:
    /// - Optimized Path: Reordered and connected curves ready for printing
    /// - Connections: Generated connection curves between gaps (for preview/debugging)
    /// - Info: Statistics about optimization (path length, travel moves, etc.)
    /// </summary>
    public class OptimizedToolpathComponent : GH_Component
    {
        public OptimizedToolpathComponent()
            : base("Optimized Toolpath", "OptPath",
                  "Combines multiple curves into an optimized continuous path. Removes self-intersections, optimizes curve order using nearest-neighbor algorithm, and adds connections between gaps. Ideal for combining output from multiple pattern components.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        public override Guid ComponentGuid => new Guid("8F3D2A1C-9B4E-4F2A-A1C3-5D6E7F8A9B0C");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Input curves to optimize (can be from multiple pattern components)", GH_ParamAccess.list);
            pManager.AddPointParameter("Start Point", "Start", "Start point for path optimization (default: origin)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Spacing", "S", "Bead width/spacing - used for connection generation", GH_ParamAccess.item, 5.0);
            pManager.AddBooleanParameter("Remove Self-Intersections", "RemoveSelf", "Automatically split curves at self-intersections", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Optimize Order", "Optimize", "Reorder curves to minimize travel distance", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Add Connections", "Connect", "Add connection curves between gaps", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Max Gap Distance", "MaxGap", "Maximum gap distance for adding connections (mm)", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Optimized Path", "Path", "Optimized continuous toolpath", GH_ParamAccess.list);
            pManager.AddCurveParameter("Connections", "Conn", "Generated connection curves", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Optimization statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            List<Curve> inputCurves = new List<Curve>();
            Point3d startPoint = Point3d.Origin;
            double spacing = 5.0;
            bool removeSelfIntersections = true;
            bool optimizeOrder = true;
            bool addConnections = true;
            double maxGapDistance = 50.0;

            if (!DA.GetDataList(0, inputCurves)) return;
            DA.GetData(1, ref startPoint);
            DA.GetData(2, ref spacing);
            DA.GetData(3, ref removeSelfIntersections);
            DA.GetData(4, ref optimizeOrder);
            DA.GetData(5, ref addConnections);
            DA.GetData(6, ref maxGapDistance);

            // Validate inputs
            if (inputCurves == null || inputCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No input curves provided");
                return;
            }

            int originalCount = inputCurves.Count;
            List<Curve> processedCurves = new List<Curve>(inputCurves);
            int selfIntersectionsRemoved = 0;

            // Step 1: Remove self-intersections
            if (removeSelfIntersections)
            {
                List<Curve> noSelfIntersections = new List<Curve>();
                
                foreach (var curve in processedCurves)
                {
                    if (curve == null || !curve.IsValid)
                        continue;

                    var segments = PathHelper.RemoveSelfIntersections(curve, 0.01);
                    
                    if (segments.Count > 1)
                    {
                        selfIntersectionsRemoved += segments.Count - 1;
                    }
                    
                    noSelfIntersections.AddRange(segments);
                }
                
                processedCurves = noSelfIntersections;
            }

            // Step 2: Optimize curve order
            List<Curve> connections = new List<Curve>();
            List<Curve> orderedCurves = processedCurves;

            if (optimizeOrder)
            {
                double maxGap = addConnections ? maxGapDistance : double.MaxValue;
                orderedCurves = PathHelper.OptimizeCurveOrder(processedCurves, startPoint, out connections, maxGap);
            }

            // Step 3: Simplify connections
            if (addConnections && connections.Count > 0)
            {
                connections = PathHelper.SimplifyConnections(connections, 5.0);
            }

            // Create output - interleave curves and connections
            List<Curve> finalPath = new List<Curve>();
            
            for (int i = 0; i < orderedCurves.Count; i++)
            {
                finalPath.Add(orderedCurves[i]);
                
                // Add connection if available
                if (addConnections && i < connections.Count)
                {
                    finalPath.Add(connections[i]);
                }
            }

            // Calculate statistics
            double totalLength = finalPath.Sum(c => c.GetLength());
            double connectionLength = connections.Sum(c => c.GetLength());
            double pathLength = totalLength - connectionLength;
            
            string info = $"Optimization Results:\n" +
                         $"- Input curves: {originalCount}\n" +
                         $"- Processed curves: {processedCurves.Count}\n" +
                         $"- Self-intersections removed: {selfIntersectionsRemoved}\n" +
                         $"- Connection curves: {connections.Count}\n" +
                         $"- Total path length: {totalLength:F2} mm\n" +
                         $"- Material path: {pathLength:F2} mm ({(pathLength/totalLength*100):F1}%)\n" +
                         $"- Travel moves: {connectionLength:F2} mm ({(connectionLength/totalLength*100):F1}%)";

            // Set outputs
            DA.SetDataList(0, finalPath);
            DA.SetDataList(1, connections);
            DA.SetData(2, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("OptimizedToolpathIcon.png");
    }
}
