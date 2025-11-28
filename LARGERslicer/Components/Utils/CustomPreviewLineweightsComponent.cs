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

            // Store color and thickness for preview rendering
            _previewColor = color;
            _previewThickness = (int)Math.Max(1, Math.Round(thickness));

            // Return geometries as-is - preview will be handled in DrawViewportWires
            DA.SetDataList(0, geometries);
        }

        private Color _previewColor = Color.Black;
        private int _previewThickness = 1;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden) return;

            // Get geometries from input parameter
            var geoParam = Params.Input[0];
            if (geoParam.SourceCount == 0) return;

            // Get color and thickness from inputs
            Color color = _previewColor;
            int thickness = _previewThickness;

            // Collect geometries from input
            var geoTree = new Grasshopper.Kernel.Data.GH_Structure<IGH_GeometricGoo>();
            if (geoParam.Sources.Count > 0 && geoParam.Sources[0] != null)
            {
                geoParam.Sources[0].CollectData();
                geoTree = geoParam.VolatileData as Grasshopper.Kernel.Data.GH_Structure<IGH_GeometricGoo>;
                if (geoTree == null) return;
            }
            else
            {
                return;
            }

            if (geoTree.DataCount == 0) return;

            // Get color from input
            var colorParam = Params.Input[1];
            if (colorParam.SourceCount > 0 && colorParam.Sources.Count > 0 && colorParam.Sources[0] != null)
            {
                colorParam.Sources[0].CollectData();
                var colorTree = colorParam.VolatileData as Grasshopper.Kernel.Data.GH_Structure<GH_Colour>;
                if (colorTree != null && colorTree.DataCount > 0)
                {
                    var ghColor = colorTree.get_FirstItem(false);
                    if (ghColor != null)
                    {
                        color = ghColor.Value;
                    }
                }
            }

            // Get thickness from input
            var thicknessParam = Params.Input[2];
            if (thicknessParam.SourceCount > 0 && thicknessParam.Sources.Count > 0 && thicknessParam.Sources[0] != null)
            {
                thicknessParam.Sources[0].CollectData();
                var thicknessTree = thicknessParam.VolatileData as Grasshopper.Kernel.Data.GH_Structure<GH_Number>;
                if (thicknessTree != null && thicknessTree.DataCount > 0)
                {
                    var ghThickness = thicknessTree.get_FirstItem(false);
                    if (ghThickness != null)
                    {
                        thickness = (int)Math.Max(1, Math.Round(ghThickness.Value));
                    }
                }
            }

            // Draw geometries with custom color and thickness
            var display = args.Display;
            var rhinoColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);

            foreach (var item in geoTree.AllData(false))
            {
                if (item == null) continue;

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
            }
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CustomPreviewLineweightsIcon.png");
        public override Guid ComponentGuid => new Guid("F7A8B9C0-D1E2-3456-0123-567890123456");
    }
}

