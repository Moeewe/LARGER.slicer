using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class RTreeSortComponent : GH_Component
    {
        public RTreeSortComponent()
          : base("RTree Sort", "RTreeSort",
              "Sorts points by their spatial distribution using RTree. Useful for organizing points before RTree creation or for adaptive layer width calculations.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Points to sort", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Sort Method", "Method", "Sort method: 0=Z-ascending, 1=Z-descending, 2=Distance from origin, 3=Spatial proximity", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Sorted Points", "P", "Points sorted according to method", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Indices", "I", "Original indices of sorted points", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var points = new List<Point3d>();
            int sortMethod = 0;

            if (!DA.GetDataList(0, points)) return;
            DA.GetData(1, ref sortMethod);

            if (points.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No points provided.");
                return;
            }

            var sortedPoints = new List<Point3d>();
            var indices = new List<int>();

            switch (sortMethod)
            {
                case 0: // Z-ascending
                    {
                        var indexed = points.Select((p, i) => new { Point = p, Index = i })
                                           .OrderBy(x => x.Point.Z)
                                           .ToList();
                        sortedPoints = indexed.Select(x => x.Point).ToList();
                        indices = indexed.Select(x => x.Index).ToList();
                        break;
                    }
                case 1: // Z-descending
                    {
                        var indexed = points.Select((p, i) => new { Point = p, Index = i })
                                           .OrderByDescending(x => x.Point.Z)
                                           .ToList();
                        sortedPoints = indexed.Select(x => x.Point).ToList();
                        indices = indexed.Select(x => x.Index).ToList();
                        break;
                    }
                case 2: // Distance from origin
                    {
                        var origin = Point3d.Origin;
                        var indexed = points.Select((p, i) => new { Point = p, Index = i, Distance = p.DistanceTo(origin) })
                                           .OrderBy(x => x.Distance)
                                           .ToList();
                        sortedPoints = indexed.Select(x => x.Point).ToList();
                        indices = indexed.Select(x => x.Index).ToList();
                        break;
                    }
                case 3: // Spatial proximity (nearest neighbor chain)
                    {
                        if (points.Count > 0)
                        {
                            var remaining = new List<(Point3d Point, int Index)>();
                            for (int i = 0; i < points.Count; i++)
                            {
                                remaining.Add((points[i], i));
                            }

                            sortedPoints.Add(remaining[0].Point);
                            indices.Add(remaining[0].Index);
                            remaining.RemoveAt(0);

                            while (remaining.Count > 0)
                            {
                                var lastPoint = sortedPoints[sortedPoints.Count - 1];
                                var nearest = remaining.OrderBy(x => x.Point.DistanceTo(lastPoint)).First();
                                sortedPoints.Add(nearest.Point);
                                indices.Add(nearest.Index);
                                remaining.Remove(nearest);
                            }
                        }
                        break;
                    }
                default:
                    {
                        sortedPoints = points;
                        indices = Enumerable.Range(0, points.Count).ToList();
                        break;
                    }
            }

            DA.SetDataList(0, sortedPoints);
            DA.SetDataList(1, indices);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RTreeSortIcon.png");
        public override Guid ComponentGuid => new Guid("A8B9C0D1-E2F3-4567-1234-678901234567");
    }
}


