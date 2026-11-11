using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
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
            pManager.AddBooleanParameter("Remove Self-Intersections", "RemoveSelf", "Remove crossings of the path with itself without cutting it (the stretch between two crossing parts is run the other way round)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Optimize Order", "Optimize", "Reorder curves to minimize travel distance", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Add Connections", "Connect", "Add connection curves between gaps", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Max Gap Distance", "MaxGap", "Maximum gap distance for adding connections (mm)", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Optimized Path", "Path", "The joined path: one curve if every gap could be connected", GH_ParamAccess.list);
            pManager.AddCurveParameter("Connections", "Conn", "Generated connection curves", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Optimization statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var input = new List<Curve>();
            Point3d start = Point3d.Origin;
            double spacing = 5.0, maxGap = 50.0;
            bool uncross = true, optimize = true, connect = true;
            if (!DA.GetDataList(0, input)) return;
            DA.GetData(1, ref start); DA.GetData(2, ref spacing); DA.GetData(3, ref uncross);
            DA.GetData(4, ref optimize); DA.GetData(5, ref connect); DA.GetData(6, ref maxGap);

            var left = input.Where(c => c != null && c.IsValid).Select(c => c.DuplicateCurve()).ToList();
            if (left.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No input curves provided"); return; }
            int originalCount = left.Count;

            // 1. order: always continue with the curve whose end is nearest; closed curves are
            //    entered at their nearest point, open ones are flipped if their far end is nearer
            var ordered = new List<Curve>();
            Point3d cursor = start;
            while (left.Count > 0)
            {
                int pick = 0; double best = double.MaxValue; bool flip = false; double seam = double.NaN;
                int scan = optimize ? left.Count : 1;
                for (int i = 0; i < scan; i++)
                {
                    Curve c = left[i];
                    if (c.IsClosed)
                    {
                        if (c.ClosestPoint(cursor, out double t))
                        {
                            double d = c.PointAt(t).DistanceTo(cursor);
                            if (d < best) { best = d; pick = i; flip = false; seam = t; }
                        }
                    }
                    else
                    {
                        double d0 = c.PointAtStart.DistanceTo(cursor), d1 = c.PointAtEnd.DistanceTo(cursor);
                        if (d0 < best) { best = d0; pick = i; flip = false; seam = double.NaN; }
                        if (optimize && d1 < best) { best = d1; pick = i; flip = true; seam = double.NaN; }
                    }
                }
                Curve next = left[pick];
                left.RemoveAt(pick);
                if (!double.IsNaN(seam) && next.IsClosed) next.ChangeClosedCurveSeam(seam);
                if (flip) next.Reverse();
                ordered.Add(next);
                cursor = next.PointAtEnd;
            }

            // 2. connect: a straight link wherever the gap is small enough to print across
            var connections = new List<Curve>();
            var chains = new List<List<Curve>> { new List<Curve> { ordered[0] } };
            int tooFar = 0;
            for (int i = 1; i < ordered.Count; i++)
            {
                Point3d a = ordered[i - 1].PointAtEnd, b = ordered[i].PointAtStart;
                double gap = a.DistanceTo(b);
                if (gap <= 1e-6) chains[chains.Count - 1].Add(ordered[i]);
                else if (connect && gap <= maxGap)
                {
                    var link = new LineCurve(a, b);
                    connections.Add(link);
                    chains[chains.Count - 1].Add(link);
                    chains[chains.Count - 1].Add(ordered[i]);
                }
                else { tooFar++; chains.Add(new List<Curve> { ordered[i] }); }
            }

            // 3. one curve per chain; crossings are removed without cutting the line
            var path = new List<Curve>();
            int fixedCrossings = 0, remaining = 0;
            foreach (var chain in chains)
            {
                Curve joined;
                if (chain.Count == 1) joined = chain[0];
                else
                {
                    var j = Curve.JoinCurves(chain, 1e-4, true);
                    if (j == null || j.Length != 1) { path.AddRange(chain); continue; }
                    joined = j[0];
                }
                if (uncross)
                {
                    Polyline pl;
                    if (!joined.TryGetPolyline(out pl))
                    {
                        var pc = joined.ToPolyline(0.02, Math.PI / 90.0, 0, 0);
                        if (pc != null) pc.TryGetPolyline(out pl);
                    }
                    if (pl != null && pl.Count > 3 && PathTools.CountCrossings(pl) > 0)
                    {
                        var healed = PathTools.Uncross(pl, 0.01, out int moves, out _);
                        fixedCrossings += moves;
                        remaining += PathTools.CountCrossings(healed);
                        joined = new PolylineCurve(healed);
                    }
                }
                path.Add(joined);
            }

            double printLength = ordered.Sum(c => c.GetLength()), linkLength = connections.Sum(c => c.GetLength());
            if (path.Count > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{path.Count} separate paths: {tooFar} gap(s) are wider than Max Gap Distance ({maxGap:0.#} mm){(connect ? "" : " or Add Connections is off")}.");
            if (remaining > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, remaining + " crossing(s) could not be removed.");
            int wide = connections.Count(c => c.GetLength() > 1.6 * spacing);
            Message = path.Count == 1 ? "1 path" : path.Count + " paths";

            DA.SetDataList(0, path);
            DA.SetDataList(1, connections);
            DA.SetData(2, $"{originalCount} curve(s) -> {path.Count} path(s)\n" +
                          $"Print length {printLength / 1000.0:0.00} m, links {linkLength:0} mm in {connections.Count} piece(s)" +
                          (wide > 0 ? $" ({wide} longer than 1.6 x spacing: they print across open space)" : "") + "\n" +
                          (uncross ? $"Crossings removed: {fixedCrossings}" + (remaining > 0 ? $", left: {remaining}" : "") : "Crossings not checked"));
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("OptimizedToolpathIcon.png");
    }
}
