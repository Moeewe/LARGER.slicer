using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class CustomPreviewLineweightsComponent : GH_Component
    {
        public CustomPreviewLineweightsComponent()
          : base("Custom Preview Lineweights", "LineWeight",
              "Sets custom line weights and colors for geometry preview. Similar to Human plugin preview component.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Geometry to preview (Curves, Breps, Meshes)", GH_ParamAccess.list);
            pManager.AddColourParameter("Color", "C", "Color for geometry preview", GH_ParamAccess.item, Color.Black);
            pManager.AddNumberParameter("Thickness", "T", "Line thickness for preview", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Geometry with custom preview settings", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var geometries = new List<IGH_GeometricGoo>();
            Color color = Color.Black;
            double thickness = 1.0;

            if (!DA.GetDataList(0, geometries)) return;
            DA.GetData(1, ref color);
            DA.GetData(2, ref thickness);

            // Store geometry, color and thickness for preview rendering
            int pixels = (int)Math.Max(1, Math.Round(thickness));
            foreach (var g in geometries)
                if (g != null) _items.Add(new Item { Geometry = g, Color = color, Thickness = pixels });

            // Return geometries as-is - preview will be handled in DrawViewportWires
            DA.SetDataList(0, geometries);
        }

        // what to draw, kept from the solution (nothing is collected while drawing)
        private sealed class Item { public IGH_GeometricGoo Geometry; public Color Color; public int Thickness; }
        private readonly List<Item> _items = new List<Item>();

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _items.Clear();
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked) return;

            // Draw geometries with custom color and thickness
            var display = args.Display;

            foreach (var kept in _items)
            {
                var item = kept.Geometry;
                int thickness = kept.Thickness;
                var rhinoColor = System.Drawing.Color.FromArgb(kept.Color.R, kept.Color.G, kept.Color.B);

                // Draw curves
                if (item is GH_Curve ghCurve)
                {
                    var curve = ghCurve.Value;
                    if (curve != null)
                    {
                        display.DrawCurve(curve, rhinoColor, thickness);
                    }
                }
                // Draw brep edges
                else if (item is GH_Brep ghBrep)
                {
                    var brep = ghBrep.Value;
                    if (brep != null)
                    {
                        foreach (var edge in brep.Edges)
                        {
                            display.DrawCurve(edge, rhinoColor, thickness);
                        }
                    }
                }
                // Draw mesh edges
                else if (item is GH_Mesh ghMesh)
                {
                    var mesh = ghMesh.Value;
                    if (mesh != null)
                    {
                        display.DrawMeshWires(mesh, rhinoColor, thickness);
                    }
                }
                // Draw points
                else if (item is GH_Point ghPoint)
                {
                    var point = ghPoint.Value;
                    if (point.IsValid)
                    {
                        display.DrawPoint(point, rhinoColor);
                    }
                }
                // lines, arcs, circles, rectangles, boxes, surfaces, SubDs ...
                else if (item is IGH_PreviewData preview)
                {
                    preview.DrawViewportWires(new GH_PreviewWireArgs(args.Viewport, display, rhinoColor, thickness));
                }
            }
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CustomPreviewLineweightsIcon.png", this);
        public override Guid ComponentGuid => new Guid("F7A8B9C0-D1E2-3456-0123-567890123456");
    }
}

