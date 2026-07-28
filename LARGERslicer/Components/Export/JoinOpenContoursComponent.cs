using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Join Open Contours - Connects open contours into a single continuous toolpath.
    /// Optimizes connection order for minimal travel distance.
    /// </summary>
    public class JoinOpenContoursComponent : GH_Component
    {
        public JoinOpenContoursComponent()
            : base("Join Open Contours", "JoinOpen",
                  "Connects open contours into a single continuous toolpath. Optimizes connection order for minimal travel distance.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Curve Tools";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Contours", "C", "Open contours to join", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance for joining curves (mm)", GH_ParamAccess.item, 0.01);
            pManager.AddBooleanParameter("Optimize Order", "Opt", "If true, optimizes curve order for minimal travel. If false, uses input order.", GH_ParamAccess.item, true);
            pManager.AddIntegerParameter("Transition Type", "Trans", "Transition type: 0 = Linear, 1 = Arc", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "P", "Continuous path from joined contours", GH_ParamAccess.item);
            pManager.AddCurveParameter("Contours", "C", "Contours in order", GH_ParamAccess.list);
            pManager.AddCurveParameter("Transitions", "T", "Transition curves between contours", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about joining process", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var contours = new List<Curve>();
            double tolerance = 0.01;
            bool optimizeOrder = true;
            int transitionType = 0;

            if (!DA.GetDataList(0, contours) || contours.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No contours provided.");
                return;
            }

            DA.GetData(1, ref tolerance);
            DA.GetData(2, ref optimizeOrder);
            DA.GetData(3, ref transitionType);

            if (transitionType < 0 || transitionType > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Transition Type out of range. Using 0 = Linear.");
                transitionType = 0;
            }

            // Filter valid open curves
            var validContours = contours
                .Where(c => c != null && c.IsValid && !c.IsClosed)
                .ToList();

            if (validContours.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid open contours found.");
                return;
            }

            if (validContours.Count == 1)
            {
                // Single contour, no joining needed
                DA.SetData(0, validContours[0]);
                DA.SetDataList(1, validContours);
                DA.SetDataList(2, new List<Curve>());
                DA.SetData(3, "Single contour, no joining needed.");
                return;
            }

            // Optimize order if requested
            List<Curve> orderedContours;
            if (optimizeOrder)
            {
                Point3d startPt = validContours[0].PointAtStart;
                orderedContours = PathHelper.OptimizeCurveOrder(validContours, startPt, out _);
            }
            else
            {
                orderedContours = validContours.Select(c => c.DuplicateCurve()).ToList();
            }

            // Create transitions between contours
            var transitionCurves = new List<Curve>();
            var pathSegments = new List<Curve>();

            for (int i = 0; i < orderedContours.Count; i++)
            {
                pathSegments.Add(orderedContours[i].DuplicateCurve());

                if (i < orderedContours.Count - 1)
                {
                    Curve current = orderedContours[i];
                    Curve next = orderedContours[i + 1];

                    Point3d currentEnd = current.PointAtEnd;
                    Point3d nextStart = next.PointAtStart;
                    double distance = currentEnd.DistanceTo(nextStart);

                    // Create transition if distance is significant
                    if (distance > tolerance)
                    {
                        Curve transition = null;
                        
                        if (transitionType == 1 && distance > tolerance * 2)
                        {
                            // Try to create arc transition
                            // Use a simple arc approximation
                            Point3d midPt = (currentEnd + nextStart) * 0.5;
                            Vector3d direction = nextStart - currentEnd;
                            direction.Unitize();
                            
                            // Create perpendicular vector for arc bulge
                            Vector3d perpVector = new Vector3d(-direction.Y, direction.X, 0);
                            perpVector.Unitize();
                            
                            // Create arc through three points
                            Point3d arcStart = currentEnd;
                            Point3d arcMid = midPt + perpVector * (distance * 0.3);
                            Point3d arcEnd = nextStart;
                            
                            Arc arc = new Arc(arcStart, arcMid, arcEnd);
                            if (arc.IsValid)
                            {
                                transition = new ArcCurve(arc);
                            }
                        }
                        
                        // Fallback to linear transition
                        if (transition == null)
                        {
                            Line transitionLine = new Line(currentEnd, nextStart);
                            transition = new LineCurve(transitionLine);
                        }
                        
                        transitionCurves.Add(transition);
                        pathSegments.Add(transition);
                    }
                }
            }

            // Join all segments into continuous path
            var joined = Curve.JoinCurves(pathSegments, tolerance);
            bool fragmentedJoin = joined != null && joined.Length > 1;
            Curve finalPath;

            if (joined != null && joined.Length == 1)
            {
                finalPath = joined[0];
            }
            else if (joined != null && joined.Length > 1)
            {
                finalPath = joined.OrderByDescending(c => c.GetLength()).First();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Path could not be fully joined into one curve. Outputting longest segment.");
            }
            else
            {
                finalPath = pathSegments[0];
            }

            // Create info string
            string info = $"Joined {validContours.Count} open contours into continuous path. ";
            info += $"Transitions: {transitionCurves.Count} ({((transitionType == 0) ? "Linear" : "Arc")}). ";
            if (optimizeOrder)
            {
                info += "Order optimized.";
            }
            else
            {
                info += "Using input order.";
            }
            if (fragmentedJoin)
            {
                info += " Join result was fragmented; longest segment returned as Path.";
            }

            DA.SetData(0, finalPath);
            DA.SetDataList(1, orderedContours);
            DA.SetDataList(2, transitionCurves);
            DA.SetData(3, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("JoinOpenContoursIcon.png");
        public override Guid ComponentGuid => new Guid("8bd82196-bc28-4897-893c-6d3885de8b94");
    }
}

