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
    /// Bridge Curves - Connects separate curves with bridges to create continuous paths.
    /// Useful for preventing overfill between disconnected segments.
    /// Based on Laurent Delrieu's random bridge approach from Nautilus plugin.
    /// </summary>
    public class BridgeCurvesComponent : GH_Component
    {
        public BridgeCurvesComponent()
            : base("Bridge Curves", "Bridge",
                  "Connects separate curves with bridges to create continuous paths. Prevents overfill between disconnected segments. Based on Nautilus plugin approach.",
                  "LARGER", "Toolpaths")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Toolpaths";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to connect with bridges", GH_ParamAccess.list);
            pManager.AddNumberParameter("Bridge Density", "Density", "Density of bridges (0-1). Higher = more bridges, lower = fewer bridges.", GH_ParamAccess.item, 0.3);
            pManager.AddNumberParameter("Bridge Length", "Length", "Maximum length for bridges (mm). Longer distances won't be bridged.", GH_ParamAccess.item, 10.0);
            pManager.AddBooleanParameter("Random", "Random", "Use random bridge placement. If false, uses greedy nearest-neighbor.", GH_ParamAccess.item, true);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed for reproducible results", GH_ParamAccess.item, 42);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "P", "Continuous path with bridges", GH_ParamAccess.item);
            pManager.AddCurveParameter("Bridges", "B", "Bridge curves", GH_ParamAccess.list);
            pManager.AddCurveParameter("Original Curves", "O", "Original curves in order", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about bridges created", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var curves = new List<Curve>();
            double bridgeDensity = 0.3;
            double bridgeLength = 10.0;
            bool random = true;
            int seed = 42;

            if (!DA.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No curves provided.");
                return;
            }

            DA.GetData(1, ref bridgeDensity);
            DA.GetData(2, ref bridgeLength);
            DA.GetData(3, ref random);
            DA.GetData(4, ref seed);

            // Filter valid curves
            var validCurves = curves.Where(c => c != null && c.IsValid).ToList();
            if (validCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid curves found.");
                return;
            }

            if (validCurves.Count == 1)
            {
                // Single curve, no bridges needed
                DA.SetData(0, validCurves[0]);
                DA.SetDataList(1, new List<Curve>());
                DA.SetDataList(2, validCurves);
                DA.SetData(3, "Single curve, no bridges needed.");
                return;
            }

            // Optimize curve order
            Point3d startPt = validCurves[0].PointAtStart;
            Point3d endPt = validCurves[validCurves.Count - 1].PointAtEnd;
            var orderedCurves = PathHelper.OptimizeCurveOrder(validCurves, startPt, endPt);

            // Generate bridges
            var bridges = new List<Curve>();
            var pathSegments = new List<Curve>();
            Random randomGen = new Random(seed);

            for (int i = 0; i < orderedCurves.Count; i++)
            {
                pathSegments.Add(orderedCurves[i].DuplicateCurve());

                if (i < orderedCurves.Count - 1)
                {
                    Curve current = orderedCurves[i];
                    Curve next = orderedCurves[i + 1];

                    Point3d currentEnd = current.PointAtEnd;
                    Point3d nextStart = next.PointAtStart;
                    double distance = currentEnd.DistanceTo(nextStart);

                    // Check if bridge is needed
                    // Bridges are created when curves are disconnected (distance > tolerance)
                    // and within the maximum bridge length
                    double tolerance = 0.01;
                    if (distance > tolerance)
                    {
                        bool shouldBridge = false;
                        
                        if (distance <= bridgeLength)
                        {
                            // Within bridge length limit - use density to decide
                            shouldBridge = random 
                                ? randomGen.NextDouble() < bridgeDensity 
                                : true; // Always bridge if not random
                        }
                        else
                        {
                            // Distance exceeds bridge length - only bridge if not random and user wants it
                            // For very long distances, we might want to warn the user
                            if (!random)
                            {
                                shouldBridge = true; // Always bridge if not random, regardless of length
                            }
                        }

                        if (shouldBridge)
                        {
                            Line bridgeLine = new Line(currentEnd, nextStart);
                            Curve bridge = new LineCurve(bridgeLine);
                            bridges.Add(bridge);
                            pathSegments.Add(bridge);
                        }
                    }
                }
            }

            // Join all segments into continuous path
            var joined = Curve.JoinCurves(pathSegments, 0.01);
            Curve finalPath = joined != null && joined.Length > 0 ? joined[0] : pathSegments[0];

            // Create info string with more details
            string info = $"Bridged {validCurves.Count} curves with {bridges.Count} bridges. ";
            info += $"Bridge density: {bridgeDensity:P0}, Max length: {bridgeLength:F2}mm. ";
            
            // Add diagnostic info
            if (bridges.Count == 0 && validCurves.Count > 1)
            {
                double tolerance = 0.01;
                // Calculate distances between curves for diagnostics
                var distances = new List<double>();
                for (int i = 0; i < orderedCurves.Count - 1; i++)
                {
                    double dist = orderedCurves[i].PointAtEnd.DistanceTo(orderedCurves[i + 1].PointAtStart);
                    distances.Add(dist);
                }
                
                if (distances.Count > 0)
                {
                    double minDist = distances.Min();
                    double maxDist = distances.Max();
                    double avgDist = distances.Average();
                    info += $"Curve distances: min={minDist:F2}mm, max={maxDist:F2}mm, avg={avgDist:F2}mm. ";
                    
                    if (minDist <= tolerance)
                    {
                        info += "Some curves are already connected (< 0.01mm). ";
                    }
                    if (maxDist > bridgeLength)
                    {
                        info += $"Some distances exceed max bridge length ({bridgeLength:F2}mm). ";
                    }
                }
            }

            DA.SetData(0, finalPath);
            DA.SetDataList(1, bridges);
            DA.SetDataList(2, orderedCurves);
            DA.SetData(3, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("BridgeCurvesIcon.png");
        public override Guid ComponentGuid => new Guid("6705f4e1-8147-4779-ad92-9a4492100c7a");
    }
}

