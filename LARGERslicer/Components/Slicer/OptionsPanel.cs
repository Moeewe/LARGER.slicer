using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using LARGERslicer.Components;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// The settings of a component as a small panel of choices and numbers, docked on its left: one
    /// row per setting, and from each row a wire into the input it sets. A freshly placed component
    /// (e.g. Slice) brings it along; delete it to work with the inputs alone, or place it again from
    /// the toolbar. The values live in the panel itself; the component reads them through its inputs.
    /// </summary>
    public abstract class OptionsPanelComponent : GH_Component, IOptionRows
    {
        private readonly Dictionary<string, double> _values = new Dictionary<string, double>(StringComparer.Ordinal);
        private SlicerComponent _host;
        private List<OptionRow> _source, _rows;

        protected OptionsPanelComponent(string name, string nick, string description)
            : base(name, nick, description, SlicerIO.Category, SlicerIO.SubCategory) { }

        /// <summary>A fresh instance of the component this panel serves (its inputs and settings rows are read from it).</summary>
        protected abstract SlicerComponent CreateHost();

        private SlicerComponent Host => _host ?? (_host = CreateHost());
        /// <summary>The settings rows of the host, in the order of its inputs: one output each.</summary>
        internal List<OptionRow> Source => _source ?? (_source = Host.SettingRows());
        /// <summary>The input of the host that output k feeds.</summary>
        internal int HostInput(int k) => k < Source.Count ? Source[k].Input : -1;

        protected override void RegisterInputParams(GH_InputParamManager p) { }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            foreach (var r in Source)
            {
                var hp = Host.Params.Input[r.Input];
                string what = "Connect to " + hp.Name + " of " + Host.Name + ".\n" + hp.Description;
                if (hp is Param_Integer) p.AddIntegerParameter(hp.Name, hp.NickName, what, GH_ParamAccess.item);
                else if (hp is Param_Boolean) p.AddBooleanParameter(hp.Name, hp.NickName, what, GH_ParamAccess.item);
                else p.AddNumberParameter(hp.Name, hp.NickName, what, GH_ParamAccess.item);
            }
        }

        /// <summary>The value the host would use without a wire: the default of its input.</summary>
        private double Default(OptionRow r)
        {
            var hp = Host.Params.Input[r.Input];
            if (hp is Param_Integer pi && pi.PersistentData.DataCount > 0) return pi.PersistentData.get_FirstItem(true).Value;
            if (hp is Param_Number pn && pn.PersistentData.DataCount > 0) return pn.PersistentData.get_FirstItem(true).Value;
            if (hp is Param_Boolean pb && pb.PersistentData.DataCount > 0) return pb.PersistentData.get_FirstItem(true).Value ? 1 : 0;
            return double.IsNaN(r.Default) ? 0 : r.Default;
        }

        private double Value(OptionRow r) => _values.TryGetValue(r.Label, out double v) ? v : Default(r);

        /// <summary>Takes over the values the host inputs hold now (a component placed with values set).</summary>
        internal void TakeValues(GH_Component host)
        {
            foreach (var r in Source)
            {
                if (r.Input >= host.Params.Input.Count) continue;
                var hp = host.Params.Input[r.Input];
                if (hp is Param_Integer pi && pi.PersistentData.DataCount > 0) _values[r.Label] = pi.PersistentData.get_FirstItem(true).Value;
                else if (hp is Param_Number pn && pn.PersistentData.DataCount > 0) _values[r.Label] = pn.PersistentData.get_FirstItem(true).Value;
                else if (hp is Param_Boolean pb && pb.PersistentData.DataCount > 0) _values[r.Label] = pb.PersistentData.get_FirstItem(true).Value ? 1 : 0;
            }
        }

        public List<OptionRow> Rows
        {
            get
            {
                if (_rows != null) return _rows;
                _rows = new List<OptionRow>();
                foreach (var s in Source)
                {
                    var r = s; // one per row
                    var row = new OptionRow
                    {
                        Kind = r.Kind, Label = r.Label, Input = -1, Items = r.Items, Presets = r.Presets, Decimals = r.Decimals,
                        Unit = r.Unit, Min = r.Min, Max = r.Max, Step = r.Step, Default = Default(r), On = r.On, Off = r.Off,
                    };
                    if (r.Kind == RowKind.Toggle) { row.GetSetting = () => Value(r) > 0.5; row.SetSetting = b => _values[r.Label] = b ? 1 : 0; }
                    else { row.Get = () => Value(r); row.Put = v => _values[r.Label] = v; }
                    _rows.Add(row);
                }
                return _rows;
            }
        }

        public override void CreateAttributes() { m_attributes = new OptionsPanelAttributes(this); }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            for (int k = 0; k < Source.Count && k < Params.Output.Count; k++)
            {
                double v = Value(Source[k]);
                var o = Params.Output[k];
                if (o is Param_Integer) da.SetData(k, (int)Math.Round(v));
                else if (o is Param_Boolean) da.SetData(k, v > 0.5);
                else da.SetData(k, v);
            }
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetString("LARGER_Options", string.Join(";", _values.Select(kv => kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture))));
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            _values.Clear();
            string text = null;
            if (reader.TryGetString("LARGER_Options", ref text) && !string.IsNullOrEmpty(text))
                foreach (var part in text.Split(';'))
                {
                    int eq = part.LastIndexOf('=');
                    if (eq > 0 && double.TryParse(part.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) _values[part.Substring(0, eq)] = v;
                }
            return base.Read(reader);
        }

        /// <summary>Places the panel on the left of a freshly placed component and wires each row into its input.</summary>
        internal static void Dock(GH_Document doc, GH_Component host, OptionsPanelComponent panel)
        {
            host.Attributes.ExpireLayout(); host.Attributes.PerformLayout();
            panel.TakeValues(host);
            panel.CreateAttributes();
            doc.AddObject(panel, false);
            panel.Attributes.ExpireLayout(); panel.Attributes.PerformLayout();
            var hb = host.Attributes.Bounds; var pb = panel.Attributes.Bounds;
            int first = panel.HostInput(0);
            float top = first >= 0 && first < host.Params.Input.Count ? host.Params.Input[first].Attributes.Pivot.Y - 13f : hb.Top;
            panel.Attributes.Pivot = new PointF(hb.Left - pb.Width - 46f, top);
            panel.Attributes.ExpireLayout(); panel.Attributes.PerformLayout();
            for (int k = 0; k < panel.Params.Output.Count; k++)
            {
                int i = panel.HostInput(k);
                if (i >= 0 && i < host.Params.Input.Count) host.Params.Input[i].AddSource(panel.Params.Output[k]);
            }
            host.Attributes.ExpireLayout();
        }
    }

    /// <summary>The panel: rows only, each with the grip of its output on the right; the name underneath.</summary>
    internal sealed class OptionsPanelAttributes : OptionAttributes
    {
        private const float Pitch = 21f, Footer = 15f;
        public OptionsPanelAttributes(IGH_Component owner) : base(owner) { }

        protected override void Layout()
        {
            var rows = Rows;
            _rects.Clear();
            float w = 120f;
            foreach (var r in rows)
            {
                string longest = r.Items != null && r.Items.Length > 0 ? r.Items.Select(ShortItem).OrderByDescending(t => t.Length).First() : "0000 mm";
                w = Math.Max(w, GH_FontServer.StringWidth(r.Label + ": " + longest + "  ▾", GH_FontServer.Small) + 30f);
            }
            w = Math.Min(w, 260f);
            float x = Pivot.X, y = Pivot.Y;
            for (int i = 0; i < rows.Count; i++)
            {
                float ry = y + 4f + i * Pitch;
                _rects.Add(new RectangleF(x + 5f, ry + (Pitch - RowHeight) / 2f, w - 16f, RowHeight));
                if (i < Owner.Params.Output.Count)
                {
                    var oa = Owner.Params.Output[i].Attributes;
                    oa.Bounds = new RectangleF(x + w - 8f, ry, 8f, Pitch);
                    oa.Pivot = new PointF(x + w - 4f, ry + Pitch / 2f);
                }
            }
            float h = 4f + rows.Count * Pitch + Footer + 2f;
            Bounds = new RectangleF(x, y, w, h);
            m_innerBounds = Bounds;
        }

        private static string ShortItem(string item)
        {
            if (string.IsNullOrEmpty(item)) return item ?? "";
            int cut = item.Length;
            foreach (string s in new[] { " (", ", ", " - ", ": " }) { int k = item.IndexOf(s, StringComparison.Ordinal); if (k > 0 && k < cut) cut = k; }
            return item.Substring(0, cut);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel != GH_CanvasChannel.Objects) return;
            var palette = Owner.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error ? GH_Palette.Error
                : Owner.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning ? GH_Palette.Warning : GH_Palette.Normal;
            using (var cap = GH_Capsule.CreateCapsule(Bounds, palette, 3, 0))
            {
                foreach (var o in Owner.Params.Output) cap.AddOutputGrip(o.Attributes.Pivot.Y);
                cap.Render(graphics, Selected, Owner.Locked, false);
            }
            var foot = new RectangleF(Bounds.X + 2f, Bounds.Bottom - Footer - 1f, Bounds.Width - 4f, Footer);
            using (var b = new SolidBrush(Color.FromArgb(Owner.Locked ? 120 : 235, 45, 45, 48)))
                graphics.FillRectangle(b, foot);
            using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(Owner.NickName, GH_FontServer.Small, Brushes.White, foot, f);
            RenderRows(graphics);
        }
    }

    /// <summary>The settings of Slice: angle, guide mode, tool axis, start/end layers.</summary>
    public class SliceOptionsComponent : OptionsPanelComponent
    {
        public SliceOptionsComponent()
            : base("Slice Options", "Slice Options",
                  "The settings of Slice as choices: angle, guide mode, tool axis, start and end layers. Comes along when you place Slice, wired into its inputs. Delete it to work with the inputs alone, or place it from the toolbar and connect it again.") { }

        public override Guid ComponentGuid => new Guid("C3A7E1D4-5B2F-4E86-9A1C-7D0F3B8E2A61");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override Bitmap Icon => IconHelper.Load("SlicerSliceIcon.png", this);
        protected override SlicerComponent CreateHost() => new SliceComponent();
    }
}
