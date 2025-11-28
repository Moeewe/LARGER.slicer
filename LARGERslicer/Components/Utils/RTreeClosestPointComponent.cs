using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class RTreeClosestPointComponent : GH_Component
    {
        public RTreeClosestPointComponent()
          : base("RTree Closest Point", "RTreeCP",
              "Finds the closest point in reference geometry from search points. Combines RTree creation and closest point search in one component.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Search Points", "Search", "Points to search from", GH_ParamAccess.list);
            pManager.AddPointParameter("Reference Points", "Reference", "Reference points to build RTree and search against", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Closest Point", "CP", "Closest point from reference points for each search point", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Closest Index", "Index", "Index of closest reference point for each search point", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var searchPoints = new List<Point3d>();
            var referencePoints = new List<Point3d>();

            if (!DA.GetDataList(0, searchPoints)) 
            {
                DA.SetDataList(0, new List<Point3d>());
                DA.SetDataList(1, new List<int>());
                return;
            }
            
            if (!DA.GetDataList(1, referencePoints))
            {
                DA.SetDataList(0, new List<Point3d>());
                DA.SetDataList(1, new List<int>());
                return;
            }

            if (referencePoints == null || referencePoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No reference points provided. Cannot build RTree.");
                DA.SetDataList(0, new List<Point3d>());
                DA.SetDataList(1, new List<int>());
                return;
            }

            if (searchPoints == null || searchPoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No search points provided.");
                DA.SetDataList(0, new List<Point3d>());
                DA.SetDataList(1, new List<int>());
                return;
            }

            // Build RTree from reference points
            var rtree = new RTree();
            for (int i = 0; i < referencePoints.Count; i++)
            {
                rtree.Insert(referencePoints[i], i);
            }

            // Find closest points
            var closestPoints = new List<Point3d>();
            var closestIndices = new List<int>();

            foreach (var searchPt in searchPoints)
            {
                int closestIndex = -1;
                Point3d closestPoint = Point3d.Unset;
                double closestDist = double.MaxValue;

                // Search for closest point
                rtree.Search(new Sphere(searchPt, double.MaxValue), (sender, e) =>
                {
                    double dist = searchPt.DistanceTo(referencePoints[e.Id]);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestIndex = e.Id;
                        closestPoint = referencePoints[e.Id];
                    }
                });

                if (closestIndex >= 0)
                {
                    closestPoints.Add(closestPoint);
                    closestIndices.Add(closestIndex);
                }
                else
                {
                    // Fallback: use first reference point if search failed
                    closestPoints.Add(referencePoints[0]);
                    closestIndices.Add(0);
                }
            }

            DA.SetDataList(0, closestPoints);
            DA.SetDataList(1, closestIndices);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("RTreeClosestPointIcon.png");
        public override Guid ComponentGuid => new Guid("E6F7A8B9-C0D1-2345-F012-456789012345");
    }
}

