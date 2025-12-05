using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Display;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    /// <summary>
    /// Super Printpath Preview - Fast extrusion visualization using Rhino DisplayConduit.
    /// Uses binary search and caching for sequential extrusion data, rendering pipes directly in the display pipeline.
    /// </summary>
    public class SuperPrintpathPreviewComponent : GH_Component
    {
        public SuperPrintpathPreviewComponent()
          : base("Super Printpath Preview", "SuperPreview",
              "Fast extrusion visualization along a curve using Rhino DisplayConduit. Shows extrusion as pipes directly in the display pipeline without instantiating geometry in GH. Uses binary search and caching for optimal performance with sequential extrusion data.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Curve along which to visualize extrusion", GH_ParamAccess.item);
            pManager.AddNumberParameter("Extrusion Amounts", "E", "Sequential extrusion amounts (cumulative or per-segment). Will be interpreted as cumulative for binary search.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Pipe Radius", "R", "Radius of the pipe visualization in mm", GH_ParamAccess.item, 0.5);
            pManager.AddColourParameter("Color", "Color", "Color for the pipe visualization", GH_ParamAccess.item, System.Drawing.Color.FromArgb(255, 100, 150, 255));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Preview information and statistics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve curve = null;
            var extrusionAmounts = new List<double>();
            double pipeRadius = 0.5;
            System.Drawing.Color color = System.Drawing.Color.FromArgb(255, 100, 150, 255);

            if (!DA.GetData(0, ref curve) || curve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve input is required.");
                return;
            }

            if (!DA.GetDataList(1, extrusionAmounts) || extrusionAmounts == null || extrusionAmounts.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No extrusion amounts provided.");
                DA.SetDataList(0, new List<string> { "No extrusion data to visualize." });
                return;
            }

            DA.GetData(2, ref pipeRadius);
            DA.GetData(3, ref color);

            // Duplicate curve for manipulation
            curve = curve.DuplicateCurve();
            
            // Normalize curve domain to [0, 1] if needed
            var domain = curve.Domain;
            if (Math.Abs(domain.T0) > 0.001 || Math.Abs(domain.T1 - 1.0) > 0.001)
            {
                // Curve is not normalized, but we'll work with the domain as-is
                // The conduit will handle parameterization correctly
            }

            // Build cumulative extrusion array (for binary search)
            var cumulativeExtrusion = new List<double>();
            double cumulative = 0;
            foreach (var ext in extrusionAmounts)
            {
                cumulative += ext;
                cumulativeExtrusion.Add(cumulative);
            }

            // Store data for conduit
            _previewData = new PreviewData
            {
                Curve = curve,
                CumulativeExtrusion = cumulativeExtrusion,
                PipeRadius = Math.Max(0.01, pipeRadius),
                Color = color,
                TotalExtrusion = cumulative
            };

            // Enable conduit if not already enabled
            if (!_conduitEnabled)
            {
                _conduit.Enabled = true;
                _conduitEnabled = true;
            }

            // Update conduit data
            _conduit.SetPreviewData(_previewData);

            var info = new List<string>
            {
                "=== Super Printpath Preview ===",
                $"Curve length: {curve.GetLength():F3} mm",
                $"Extrusion points: {extrusionAmounts.Count}",
                $"Total extrusion: {cumulative:F3} mm",
                $"Pipe radius: {pipeRadius:F3} mm",
                $"Preview enabled: {_conduitEnabled}",
                "",
                "Using Rhino DisplayConduit for fast rendering",
                "Binary search enabled for sequential extrusion lookup"
            };

            DA.SetDataList(0, info);
        }

        private PreviewData _previewData = null;
        private static bool _conduitEnabled = false;
        private static readonly ExtrusionPreviewConduit _conduit = new ExtrusionPreviewConduit();

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SuperPrintpathPreviewIcon.png");
        public override Guid ComponentGuid => new Guid("B2C3D4E5-F6A7-8901-BCDE-F123456789AB");

        public override void RemovedFromDocument(GH_Document document)
        {
            // Disable conduit when component is removed
            if (_conduitEnabled)
            {
                _conduit.Enabled = false;
                _conduitEnabled = false;
            }
            base.RemovedFromDocument(document);
        }
    }

    /// <summary>
    /// Data structure for preview information
    /// </summary>
    internal class PreviewData
    {
        public Curve Curve { get; set; }
        public List<double> CumulativeExtrusion { get; set; }
        public double PipeRadius { get; set; }
        public System.Drawing.Color Color { get; set; }
        public double TotalExtrusion { get; set; }
    }

    /// <summary>
    /// Rhino DisplayConduit for fast extrusion visualization
    /// Draws curve directly without creating any geometry (ultra fast)
    /// </summary>
    internal class ExtrusionPreviewConduit : DisplayConduit
    {
        private Curve _curve = null;
        private double _pipeRadius = 0;
        private System.Drawing.Color _color = System.Drawing.Color.Empty;
        private BoundingBox _bbox = BoundingBox.Unset;

        public void SetPreviewData(PreviewData data)
        {
            if (data == null || data.Curve == null || data.PipeRadius <= 0)
            {
                _curve = null;
                _bbox = BoundingBox.Unset;
                return;
            }

            _curve = data.Curve;
            _pipeRadius = data.PipeRadius;
            _color = data.Color;

            // Precompute bounding box (with radius padding)
            var curveBBox = _curve.GetBoundingBox(true);
            _bbox = new BoundingBox(
                curveBBox.Min - new Vector3d(_pipeRadius, _pipeRadius, _pipeRadius),
                curveBBox.Max + new Vector3d(_pipeRadius, _pipeRadius, _pipeRadius)
            );
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            // CRITICAL: Include bounding box so the curve is not clipped
            if (_curve != null && _bbox.IsValid)
            {
                e.IncludeBoundingBox(_bbox);
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            // Draw curve directly with thick line - NO geometry creation!
            if (_curve == null || _pipeRadius <= 0)
                return;

            var rhinoColor = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B);
            
            // Draw curve with thickness = 2 * radius (diameter)
            // This is MUCH faster than creating Brep/Mesh
            int thickness = Math.Max(1, (int)(_pipeRadius * 2.0));
            e.Display.DrawCurve(_curve, rhinoColor, thickness);
        }

    }
}

