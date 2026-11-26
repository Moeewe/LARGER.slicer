using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper2.Display;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Display;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// What Path Heat, DXR Width Preview and Surface Thickness Heatmap draw: lines in their own colour
    /// and width, and meshes in their vertex colours (Grasshopper 2 draws a mesh output in plain grey).
    /// Process fills a picture and hands it over with Show; drawing only reads the last finished one.
    /// </summary>
    internal sealed class ColourPicture
    {
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<Color> Colours = new List<Color>();
        public readonly List<int> Widths = new List<int>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public BoundingBox Box = BoundingBox.Empty;

        public void Line(Line l, Color c, int width)
        {
            Lines.Add(l); Colours.Add(c); Widths.Add(Math.Max(1, width));
            Box.Union(l.From); Box.Union(l.To);
        }

        public void Mesh(Mesh m)
        {
            if (m == null || m.Faces.Count == 0) return;
            Meshes.Add(m);
            Box.Union(m.GetBoundingBox(false));
        }

        public bool Empty => Lines.Count == 0 && Meshes.Count == 0;
    }

    /// <summary>A component that draws a ColourPicture besides the standard preview of its other outputs.</summary>
    public abstract class ColourPreviewComponent : PreviewComponent
    {
        private ColourPicture _shown = new ColourPicture();

        protected ColourPreviewComponent(Grasshopper2.UI.Nomen nomen) : base(nomen) { }
        protected ColourPreviewComponent(IReader reader) : base(reader) { }

        internal void ShowColours(ColourPicture p) { _shown = p ?? new ColourPicture(); }
        internal void ClearColours() { _shown = new ColourPicture(); }
        internal BoundingBox ColourBox => _shown.Box;

        public override BoundingBox DisplayBounds()
        {
            var b = base.DisplayBounds();
            b.Union(_shown.Box);
            return b;
        }

        public override void DisplayFaces(DisplayPipeline pipeline, Guises guises, ref BoundingBox extents)
        {
            base.DisplayFaces(pipeline, guises, ref extents);
            var p = _shown;
            if (p.Meshes.Count == 0) return;
            foreach (var m in p.Meshes)
            {
                try
                {
                    if (m.VertexColors.Count == m.Vertices.Count) pipeline.DrawMeshFalseColors(m);
                    else pipeline.DrawMeshShaded(m, new DisplayMaterial(Color.Silver));
                }
                catch { }
            }
            extents.Union(p.Box);
        }

        public override void DisplayWires(DisplayPipeline pipeline, Guises guises, ref BoundingBox extents)
        {
            base.DisplayWires(pipeline, guises, ref extents);
            var p = _shown;
            if (p.Lines.Count > 0)
            {
                // runs of the same colour and width in one call: thousands of segments stay fast
                int i = 0;
                var run = new List<Line>();
                while (i < p.Lines.Count)
                {
                    run.Clear();
                    Color c = p.Colours[i]; int w = p.Widths[i];
                    while (i < p.Lines.Count && p.Colours[i] == c && p.Widths[i] == w) run.Add(p.Lines[i++]);
                    pipeline.DrawLines(run, c, w);
                }
                extents.Union(p.Box);
            }
            WiresDrawn(pipeline);
        }

        /// <summary>Called after the wires of this component were drawn in a viewport (for overlays such as a legend).</summary>
        protected virtual void WiresDrawn(DisplayPipeline pipeline) { }
    }

    /// <summary>Colour ramp and legend of the result views (the Heat of Grasshopper 1, PathHeatComponents.cs).</summary>
    internal static class HeatRamp
    {
        // deep blue, teal, pale yellow, orange, deep red: readable on light and dark viewports
        private static readonly Color[] Stops =
        {
            Color.FromArgb(38, 70, 140), Color.FromArgb(40, 150, 170), Color.FromArgb(235, 225, 150),
            Color.FromArgb(240, 140, 60), Color.FromArgb(185, 35, 45)
        };

        public static Color At(double t)
        {
            if (double.IsNaN(t)) return Color.Gray;
            t = Math.Max(0, Math.Min(1, t)) * (Stops.Length - 1); int i = Math.Min(Stops.Length - 2, (int)Math.Floor(t)); double f = t - i;
            var a = Stops[i]; var b = Stops[i + 1];
            return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        public static Color At(double v, Interval range) => At(range.Length <= 0 ? 0.5 : (v - range.Min) / range.Length);

        /// <summary>Title on a light plate, centred over a point of the model.</summary>
        public static void Label(DisplayPipeline d, string text, Point3d at)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var s = d.Viewport.WorldToClient(at); var r = TextSize(d, text, 17);
            int w = r.Width + 24, h = r.Height + 14; var box = new Rectangle((int)s.X - w / 2, (int)s.Y - h / 2, w, h);
            d.Draw2dRectangle(box, Color.FromArgb(200, 255, 255, 255), 0, Color.FromArgb(215, 255, 255, 255));
            d.Draw2dText(text, Color.FromArgb(25, 25, 25), new Point2d(s.X, s.Y), true, 17, "Arial");
        }

        private static Size TextSize(DisplayPipeline d, string text, int height)
        {
            var r = d.Measure2dText(text, new Point2d(0, 0), false, 0, height, "Arial");
            int w = Math.Max(r.Width, (int)(text.Length * height * 0.52)), h = Math.Max(r.Height, (int)(height * 1.15));
            return new Size(w, h);
        }

        /// <summary>Colour bar at the lower left of the viewport.</summary>
        public static void Legend(DisplayPipeline d, string title, Interval range, string unit, int slot)
        {
            var vp = d.Viewport.Bounds; int h = Math.Min(260, vp.Height / 3), w = 18;
            int x = 24 + slot * 190, y = vp.Height - h - 40;
            int pw = Math.Max(140, TextSize(d, title ?? "", 14).Width + 20);
            d.Draw2dRectangle(new Rectangle(x - 10, y - 32, pw, h + 52), Color.FromArgb(215, 255, 255, 255), 0, Color.FromArgb(215, 255, 255, 255));
            const int steps = 40;
            for (int i = 0; i < steps; i++)
            {
                int y0 = y + h - (i + 1) * h / steps, y1 = y + h - i * h / steps;
                var c = At((i + 0.5) / steps);
                d.Draw2dRectangle(new Rectangle(x, y0, w, y1 - y0 + 1), c, 0, c);
            }
            var fg = Color.FromArgb(60, 60, 60);
            d.Draw2dRectangle(new Rectangle(x, y, w, h), fg, 1, Color.Transparent);
            d.Draw2dText(title ?? "", fg, new Point2d(x, y - 22), false, 14);
            for (int k = 0; k <= 4; k++)
            {
                double v = range.Min + range.Length * k / 4.0;
                d.Draw2dText(Inv.F($"{v:0.##} {unit}"), fg, new Point2d(x + w + 8, y + h - k * h / 4 - 7), false, 12);
            }
        }
    }

    /// <summary>
    /// Something drawn on top of everything (title and colour bar of Path Heat), as the OverlayConduit of
    /// Grasshopper 1. Grasshopper 2 has no 2D overlay of its own: a Rhino display conduit draws the
    /// overlays of the components whose wires were drawn in the same frame of the same viewport, so a
    /// hidden, disabled or deleted component draws no legend either.
    /// </summary>
    internal sealed class OverlayConduit2 : DisplayConduit
    {
        /// <summary>Draws the overlay; true when it took the legend place given by slot.</summary>
        internal interface IOverlay { bool DrawOverlay(DisplayPipeline d, int slot); }

        private static OverlayConduit2 _conduit;
        private static readonly List<IOverlay> DrawnThisFrame = new List<IOverlay>();
        private static object _frameViewport;

        /// <summary>From DisplayWires: this component shows in the frame that is being drawn.</summary>
        public static void Drawn(IOverlay o, DisplayPipeline d)
        {
            if (_conduit == null) _conduit = new OverlayConduit2();
            if (!_conduit.Enabled) _conduit.Enabled = true;
            object vp = d?.Viewport?.Id;
            if (!Equals(vp, _frameViewport)) { DrawnThisFrame.Clear(); _frameViewport = vp; }
            if (!DrawnThisFrame.Contains(o)) DrawnThisFrame.Add(o);
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            // the start of every frame: nothing has been drawn in it yet
            DrawnThisFrame.Clear();
            _frameViewport = e.Display?.Viewport?.Id;
        }

        protected override void DrawOverlay(DrawEventArgs e)
        {
            var list = DrawnThisFrame.ToArray();
            // every legend gets its own place, so two legends never lie on top of each other
            int slot = 1;
            foreach (var o in list)
            {
                try { if (o.DrawOverlay(e.Display, slot)) slot++; } catch { }
            }
        }
    }
}
