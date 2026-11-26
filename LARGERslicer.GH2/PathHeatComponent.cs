using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Display;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== path heat (GH1: Components/Slicer/PathHeatComponents.cs)
    /// <summary>A toolpath coloured by one value per point, e.g. the layer height of Reshape Path.</summary>
    [IoId("88b7edcb-3bab-40e2-b1a5-d086a3fdd9dc")]
    public sealed class PathHeatComponent : ColourPreviewComponent, OverlayConduit2.IOverlay
    {
        public PathHeatComponent() : base(Io.Name("Path Heat", "Draws a toolpath coloured by one value per point (the layer height or tilt of Reshape Path, ...), with a title and a colour bar.", Io.Slicer, Group.Check)) { }
        public PathHeatComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerResultFieldIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Toolpath");
            inputs.AddNumber("Values", "V", "One value per point of the toolpath", Access.Twig);
            inputs.AddInterval("Range", "Ra", "Colour range. Empty = from the values", Access.Item, Requirement.MayBeMissing);
            inputs.AddVector("Move", "Mv", "Shift of the view").Set(Vector3d.Zero);
            inputs.AddText("Title", "Ti", "Shown above the part").Set("");
            inputs.AddText("Unit", "U", "Unit of the values for the colour bar").Set("mm");
            inputs.AddInteger("Thickness", "Th", "Line thickness in pixels").Set(3);
            inputs.AddBoolean("Legend", "L", "Draw the colour bar").Set(true);
            inputs.AddText("Label", "La", "Name of the value on the colour bar").Set("Layer height");
            inputs.AddNumber("Until", "Un", "Draw the path only up to here: a fraction 0 to 1, or a point number above 1 (connect Move of Robot Simulation to watch the path grow with the robot)").Set(1.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddInterval("Range", "Ra", "Smallest and largest value");
            outputs.AddText("Info", "I", "Summary");
        }

        /// <summary>What the overlay draws: set together with the lines, read on the UI thread.</summary>
        private sealed class Overlay
        {
            public string Title = "", Unit = "", Label = "";
            public Interval Range;
            public bool Legend;
            public Point3d Top;
        }
        private Overlay _overlay;

        protected override void Process(IDataAccess access)
        {
            ClearColours(); _overlay = null;
            var tp = Io.Get<Toolpath>(access, 0);
            double[] va;
            if (tp == null || !access.ItemArray(1, out va) || va == null) return;
            var v = va.ToList();
            Interval range; Vector3d mv; string title, unit, label; int th; bool legend; double until;
            if (!access.GetItem(2, out range)) range = Interval.Unset;
            if (!access.GetItem(3, out mv)) mv = Vector3d.Zero;
            if (!access.GetItem(4, out title) || title == null) title = "";
            if (!access.GetItem(5, out unit) || unit == null) unit = "mm";
            if (!access.GetItem(6, out th)) th = 3;
            if (!access.GetItem(7, out legend)) legend = true;
            if (!access.GetItem(8, out label) || label == null) label = "";
            if (!access.GetItem(9, out until)) until = 1;
            until = Math.Max(0, until);

            if (v.Count != tp.Count) access.AddWarning("Values", Inv.F($"{v.Count} values for {tp.Count} points: the values are stretched over the path."));
            double Val(int i) => v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)((long)i * v.Count / Math.Max(1, tp.Count)))];
            var finite = v.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList(); // NaN = not measured: grey, not part of the range
            var own = finite.Count > 0 ? new Interval(finite.Min(), finite.Max()) : new Interval(0, 0);
            if (!range.IsValid || range.Length <= 0) range = own;

            var picture = new ColourPicture();
            int width = Math.Max(1, th);
            int last = until == 1 ? tp.Count : until < 1 ? (int)Math.Round(until * tp.Count) : Math.Min(tp.Count, (int)until + 1);
            for (int i = 1; i < last; i++)
            {
                if (tp.Roles.Count > i && tp.Roles[i] == PathRole.Travel) continue;
                picture.Line(new Line(tp.Points[i - 1] + mv, tp.Points[i] + mv), HeatRamp.At(0.5 * (Val(i - 1) + Val(i)), range), width);
            }

            var bb = new BoundingBox(tp.Points.Select(p => p + mv));
            var top = new Point3d(bb.Center.X, bb.Center.Y, bb.Max.Z + 0.15 * bb.Diagonal.Length);
            if (picture.Lines.Count > 0) picture.Box.Union(top);
            _overlay = new Overlay { Title = title, Unit = unit, Label = label, Range = range, Legend = legend, Top = top };
            ShowColours(picture);

            access.SetItem(0, own);
            access.SetItem(1, Inv.F($"{tp.Count} points, values {own.Min:0.###} .. {own.Max:0.###} {unit}"));
        }

        protected override void WiresDrawn(DisplayPipeline pipeline)
        {
            if (_overlay != null && ColourBox.IsValid) OverlayConduit2.Drawn(this, pipeline);
        }

        bool OverlayConduit2.IOverlay.DrawOverlay(DisplayPipeline d, int slot)
        {
            var o = _overlay;
            if (o == null) return false;
            if (!string.IsNullOrWhiteSpace(o.Title)) HeatRamp.Label(d, o.Title, o.Top);
            if (!o.Legend) return false;
            HeatRamp.Legend(d, o.Label, o.Range, o.Unit, slot);
            return true;
        }
    }
}
