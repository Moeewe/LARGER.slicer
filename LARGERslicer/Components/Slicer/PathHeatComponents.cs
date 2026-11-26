using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>Something drawn on top of everything (titles, colour bars), so the robot or the parts never hide it.</summary>
    internal interface IOverlay { bool OverlayActive { get; } void DrawOverlay(DisplayPipeline d); }

    internal sealed class OverlayConduit : DisplayConduit
    {
        private static OverlayConduit _conduit;
        private static readonly List<WeakReference<IOverlay>> Items = new List<WeakReference<IOverlay>>();

        public static void Register(IOverlay o)
        {
            lock (Items)
            {
                Items.RemoveAll(w => !w.TryGetTarget(out _));
                if (!Items.Any(w => w.TryGetTarget(out var t) && ReferenceEquals(t, o))) Items.Add(new WeakReference<IOverlay>(o));
            }
            if (_conduit == null) _conduit = new OverlayConduit();
            _conduit.Enabled = true;
        }

        public static bool IsShown(GH_Component c)
        {
            if (c.Hidden || c.Locked) return false;
            var doc = c.OnPingDocument();
            return doc != null && doc.Enabled && doc.PreviewMode != GH_PreviewMode.Disabled && Grasshopper.Instances.ActiveCanvas?.Document == doc;
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            IOverlay[] list; lock (Items) list = Items.Select(w => w.TryGetTarget(out var t) ? t : null).Where(t => t != null).ToArray();
            foreach (var o in list) { try { if (o.OverlayActive) o.DrawOverlay(e.Display); } catch { } }
        }
    }

    /// <summary>Colour ramp and legend shared by the result views.</summary>
    internal static class Heat
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

        /// <summary>Big line at the top left of the viewport, on a light plate.</summary>
        public static void Headline(DisplayPipeline d, string text)
        {
            var r = TextSize(d, text, 30);
            d.Draw2dRectangle(new Rectangle(16, 14, r.Width + 28, r.Height + 22), Color.FromArgb(225, 255, 255, 255), 0, Color.FromArgb(225, 255, 255, 255));
            d.Draw2dText(text, Color.FromArgb(20, 20, 20), new Point2d(30, 24), false, 30, "Arial");
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
            d.Draw2dText(title, fg, new Point2d(x, y - 22), false, 14);
            for (int k = 0; k <= 4; k++)
            {
                double v = range.Min + range.Length * k / 4.0;
                d.Draw2dText(Inv.F($"{v:0.##} {unit}"), fg, new Point2d(x + w + 8, y + h - k * h / 4 - 7), false, 12);
            }
        }
    }

    /// <summary>A toolpath coloured by one value per point, e.g. the layer height of Reshape Path.</summary>
    public class PathHeatComponent : SlicerComponent, IOverlay
    {
        public PathHeatComponent()
            : base("Path Heat", "Heat",
                  "Draws a toolpath coloured by one value per point (the layer height or tilt of Reshape Path, ...), with a title and a colour bar.") { }

        public override Guid ComponentGuid => new Guid("9B3C1E57-2D84-4F06-B7A9-61E0C4D2F812");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override Bitmap Icon => IconHelper.Load("SlicerResultFieldIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Toolpath", GH_ParamAccess.item);
            p.AddNumberParameter("Values", "V", "One value per point of the toolpath", GH_ParamAccess.list);
            p.AddIntervalParameter("Range", "Ra", "Colour range. Empty = from the values", GH_ParamAccess.item);
            p.AddVectorParameter("Move", "Mv", "Shift of the view", GH_ParamAccess.item, Vector3d.Zero);
            p.AddTextParameter("Title", "Ti", "Shown above the part", GH_ParamAccess.item, "");
            p.AddTextParameter("Unit", "U", "Unit of the values for the colour bar", GH_ParamAccess.item, "mm");
            p.AddIntegerParameter("Thickness", "Th", "Line thickness in pixels", GH_ParamAccess.item, 3);
            p.AddBooleanParameter("Legend", "L", "Draw the colour bar", GH_ParamAccess.item, true);
            p.AddTextParameter("Label", "La", "Name of the value on the colour bar", GH_ParamAccess.item, "Layer height");
            p.AddNumberParameter("Until", "Un", "Draw the path only up to here: a fraction 0 to 1, or a point number above 1 (connect Move of Robot Simulation to watch the path grow with the robot)", GH_ParamAccess.item, 1.0);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntervalParameter("Range", "Ra", "Smallest and largest value", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Summary", GH_ParamAccess.item);
        }

        private Line[] _lines; private Color[] _colors; private string _title = "", _unit = ""; private Interval _range; private int _th = 3; private bool _legend; private Point3d _top; private string _label = "";

        protected override void BeforeSolveInstance() { _lines = null; base.BeforeSolveInstance(); }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0); var v = new List<double>();
            if (tp == null || !da.GetDataList(1, v)) return;
            Interval range = Interval.Unset; var mv = Vector3d.Zero; string title = "", unit = "mm"; int th = 3; bool legend = true;
            da.GetData(2, ref range); da.GetData(3, ref mv); da.GetData(4, ref title); da.GetData(5, ref unit); da.GetData(6, ref th); da.GetData(7, ref legend); da.GetData(8, ref _label); double until = 1; da.GetData(9, ref until); until = Math.Max(0, until);
            if (v.Count != tp.Count) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Inv.F($"{v.Count} values for {tp.Count} points: the values are stretched over the path."));
            double Val(int i) => v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)((long)i * v.Count / Math.Max(1, tp.Count)))];
            var finite = v.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList(); // NaN = not measured: grey, not part of the range
            var own = finite.Count > 0 ? new Interval(finite.Min(), finite.Max()) : new Interval(0, 0); if (!range.IsValid || range.Length <= 0) range = own;
            var lines = new List<Line>(); var cols = new List<Color>();
            int last = until == 1 ? tp.Count : until < 1 ? (int)Math.Round(until * tp.Count) : Math.Min(tp.Count, (int)until + 1);
            for (int i = 1; i < last; i++)
            {
                if (tp.Roles[i] == PathRole.Travel) continue;
                lines.Add(new Line(tp.Points[i - 1] + mv, tp.Points[i] + mv)); cols.Add(Heat.At(0.5 * (Val(i - 1) + Val(i)), range));
            }
            _lines = lines.ToArray(); _colors = cols.ToArray(); _range = range; _unit = unit; _th = Math.Max(1, th); _legend = legend;
            var bb = new BoundingBox(tp.Points.Select(p => p + mv)); _top = new Point3d(bb.Center.X, bb.Center.Y, bb.Max.Z + 0.15 * bb.Diagonal.Length); _title = title;
            da.SetData(0, own); da.SetData(1, Inv.F($"{tp.Count} points, values {own.Min:0.###} .. {own.Max:0.###} {unit}"));
        }

        public override BoundingBox ClippingBox { get { var b = BoundingBox.Empty; if (_lines != null) foreach (var l in _lines) { b.Union(l.From); b.Union(l.To); } if (b.IsValid) b.Union(_top); return b; } }
        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (_lines == null || Hidden || Locked) return;
            for (int i = 0; i < _lines.Length; i++) args.Display.DrawLine(_lines[i], _colors[i], _th);
            OverlayConduit.Register(this);
        }

        public bool OverlayActive => _lines != null && OverlayConduit.IsShown(this);

        public void DrawOverlay(DisplayPipeline d)
        {
            if (!string.IsNullOrWhiteSpace(_title)) Heat.Label(d, _title, _top);
            if (_legend)
            {
                // every Path Heat on the canvas gets its own place for the legend, so two legends never lie on top of each other
                int slot = 1;
                var doc = OnPingDocument();
                if (doc != null)
                {
                    int k = 0;
                    foreach (var o in doc.Objects)
                        if (o is PathHeatComponent ph && ph.OverlayActive && ph._legend) { k++; if (ReferenceEquals(ph, this)) { slot = k; break; } }
                }
                Heat.Legend(d, _label, _range, _unit, slot);
            }
        }
    }
}
