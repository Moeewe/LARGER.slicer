using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace LARGERslicer.Components
{
    public enum RowKind { Choice, Number, Toggle, Button, Label }

    /// <summary>
    /// One setting drawn on the component itself, under its inputs and outputs: a choice, a number,
    /// an on/off switch or a button. It works on an input of the component: the value goes into that
    /// input (as if typed into it), so the component reads it the usual way, the file keeps it, and a
    /// wire into the input still wins - the row then says "connected".
    /// A toggle may also work on a setting of the component itself (GetSetting/SetSetting, no input),
    /// e.g. "Background: on".
    /// </summary>
    public class OptionRow
    {
        public RowKind Kind;
        public string Label;
        public int Input = -1;
        public string[] Items;            // Choice: item i has the value i
        public string Shown;              // Choice: the item really used, when it is not Items[value] (null = Items[value])
        public Action<int> Picked;        // Choice: an item was picked in the menu (also the one picked before)
        // Number: drag left/right to change it, click to type a value, the arrow at the right for quick picks
        public double[] Presets;          // Number: quick picks (a handful)
        public int Decimals = 1;
        public string Unit = "";
        public double Min = double.NaN, Max = double.NaN; // Number: the range a drag covers (typing may go beyond)
        public double Step;               // Number: a drag moves in these steps (0 = from Decimals)
        public double Default = double.NaN;
        public Func<string> ZeroText;     // Number: what the row says for 0 (e.g. "auto, 2.5 mm"); null = the number
        public string On, Off;            // Toggle: what the row says in either state (empty = Label: on / off)
        public Action<GH_Component> Click; // Button
        public bool Momentary;            // Button on a boolean input: true for one solution, then back to false
        public Func<bool> GetSetting;     // Toggle on a setting of the component (no input)
        public Action<bool> SetSetting;
        public Func<double> Get;          // Choice / Number kept by the component itself (no input, Input = -1)
        public Action<double> Put;
        public bool External;             // Get/Put (or Get/SetSetting) work on another component, which records the undo and solves
        public Func<string> TextOf;       // Label: the text to show now (null = Label); a Label row is a caption or a line of information
        public bool Caption;              // Label: a section heading (drawn bold, without a box)
        public Func<bool> Wired;          // a row shown elsewhere (Control Panel): true while a wire feeds the input; the row then shows the value it gets and cannot be changed
        public Action MakeSlider;         // a row shown elsewhere (Control Panel): puts a real slider into the input, e.g. as a gene for Galapagos

        internal double StepSize => Step > 0 ? Step : Math.Pow(10, -Math.Max(0, Decimals));
        internal bool HasRange => !double.IsNaN(Min) && !double.IsNaN(Max) && Max > Min;
    }

    /// <summary>A component that wants to be at least this wide, so that its rows show their labels.</summary>
    public interface IWideRows { float MinWidth { get; } }

    public interface IOptionRows
    {
        List<OptionRow> Rows { get; }
    }

    /// <summary>
    /// Which settings rows a Control Panel shows instead of the component itself: the component then
    /// draws a note in their place. Keyed by the component; one panel per component.
    /// </summary>
    public static class ControlRegistry
    {
        private static readonly Dictionary<Guid, KeyValuePair<Guid, HashSet<int>>> _map = new Dictionary<Guid, KeyValuePair<Guid, HashSet<int>>>();

        public static bool Controls(IGH_DocumentObject owner, OptionRow r)
            => owner != null && r != null && r.Input >= 0 && r.Kind != RowKind.Button && r.Kind != RowKind.Label
               && _map.TryGetValue(owner.InstanceGuid, out var kv) && kv.Value.Contains(r.Input);

        /// <summary>The panel now shows these inputs of the component (an empty set releases it).</summary>
        public static void Set(Guid panel, IGH_DocumentObject comp, IEnumerable<int> inputs)
        {
            var set = new HashSet<int>(inputs);
            if (set.Count == 0) { if (_map.TryGetValue(comp.InstanceGuid, out var old) && old.Key == panel) _map.Remove(comp.InstanceGuid); }
            else _map[comp.InstanceGuid] = new KeyValuePair<Guid, HashSet<int>>(panel, set);
        }

        /// <summary>Releases every component the panel showed; returns them so their layout can be renewed.</summary>
        public static List<Guid> Release(Guid panel)
        {
            var gone = _map.Where(kv => kv.Value.Key == panel).Select(kv => kv.Key).ToList();
            foreach (var g in gone) _map.Remove(g);
            return gone;
        }

        /// <summary>The inputs of a component a panel sets (none: empty).</summary>
        public static List<int> InputsOf(IGH_DocumentObject comp) => comp != null && _map.TryGetValue(comp.InstanceGuid, out var kv) ? kv.Value.ToList() : new List<int>();

        public static Guid? PanelOf(IGH_DocumentObject comp) => comp != null && _map.TryGetValue(comp.InstanceGuid, out var kv) ? kv.Key : (Guid?)null;

        /// <summary>The components a panel shows now.</summary>
        public static List<Guid> Of(Guid panel) => _map.Where(kv => kv.Value.Key == panel).Select(kv => kv.Key).ToList();

    }

    /// <summary>Draws the rows of an IOptionRows component under it and answers clicks and drags on them.</summary>
    public class OptionAttributes : GH_ComponentAttributes
    {
        protected const float RowHeight = 18f, Gap = 3f, ArrowWidth = 14f;
        protected readonly List<RectangleF> _rects = new List<RectangleF>();

        /// <summary>True while a Save button quietly goes back to false (no new solution, nothing really changed).</summary>
        internal static bool QuietReset;

        // a number row being dragged
        private int _drag = -1;
        private PointF _dragStart;
        private double _dragFrom, _dragValue;
        private bool _moved;

        public OptionAttributes(IGH_Component owner) : base(owner) { }

        protected List<OptionRow> Rows => (Owner as IOptionRows)?.Rows ?? new List<OptionRow>();

        protected override void Layout()
        {
            base.Layout();
            _rects.Clear();
            var rows = Rows;
            if (rows.Count == 0 && ControlRegistry.InputsOf(Owner).Count == 0) { _marks.Clear(); return; }
            // wide enough to read the labels of the rows
            float wide = (Owner as IWideRows)?.MinWidth ?? 0;
            if (Bounds.Width < wide)
            {
                float extra = wide - Bounds.Width;
                m_innerBounds = new RectangleF(m_innerBounds.X, m_innerBounds.Y, m_innerBounds.Width + extra, m_innerBounds.Height);
                foreach (var o in Owner.Params.Output)
                {
                    var r = o.Attributes.Bounds; r.X += extra; o.Attributes.Bounds = r;
                    o.Attributes.Pivot = new PointF(o.Attributes.Pivot.X + extra, o.Attributes.Pivot.Y);
                }
                Bounds = new RectangleF(Bounds.X, Bounds.Y, wide, Bounds.Height);
            }
            // inputs a Control Panel sets get a small sign at their left, as Grasshopper marks a flattened or
            // grafted input: the input column grows to the left by the width of the sign
            _marks.Clear();
            var comp = (GH_Component)Owner;
            foreach (int ix in ControlRegistry.InputsOf(Owner))
                if (ix >= 0 && ix < comp.Params.Input.Count && comp.Params.Input[ix].SourceCount == 0) _marks.Add(ix);
            _marks.Sort();
            if (_marks.Count > 0)
            {
                foreach (var ip in comp.Params.Input)
                {
                    var r = ip.Attributes.Bounds; r.X -= MarkWidth; r.Width += MarkWidth; ip.Attributes.Bounds = r;
                    ip.Attributes.Pivot = new PointF(ip.Attributes.Pivot.X - MarkWidth, ip.Attributes.Pivot.Y);
                }
                Bounds = new RectangleF(Bounds.X - MarkWidth, Bounds.Y, Bounds.Width + MarkWidth, Bounds.Height);
            }
            var b = Bounds;
            float y = b.Bottom + 2f;
            _wiredAtLayout = WiredKey();
            foreach (var r in rows)
            {
                // a row whose input has a wire is not drawn: the wire decides (e.g. an Options component);
                // nor a row a Control Panel shows: its input carries the sign instead
                if (Wired(r)) { _rects.Add(RectangleF.Empty); continue; }
                if (ControlRegistry.Controls(Owner, r)) { _rects.Add(RectangleF.Empty); continue; }
                _rects.Add(new RectangleF(b.X + 4f, y, b.Width - 8f, RowHeight));
                y += RowHeight + Gap;
            }
            Bounds = new RectangleF(b.X, b.Y, b.Width, y - b.Y + (y > b.Bottom + 2f ? 2f : 0f));
        }

        private const float MarkWidth = 13f;
        private readonly List<int> _marks = new List<int>();

        private string _wiredAtLayout;
        private bool Wired(OptionRow r) { var p = Param(r); return p != null && p.SourceCount > 0; }
        private string WiredKey() => string.Concat(Rows.Select(r => Wired(r) ? '1' : ControlRegistry.Controls(Owner, r) ? '2' : '0'))
            + "|" + string.Join(",", ControlRegistry.InputsOf(Owner).Select(k => k < ((GH_Component)Owner).Params.Input.Count && ((GH_Component)Owner).Params.Input[k].SourceCount > 0 ? -k - 1 : k));

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            // a wire was connected or removed since the last layout: rows come or go
            if (_wiredAtLayout != null && WiredKey() != _wiredAtLayout) { ExpireLayout(); PerformLayout(); }
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            RenderRows(graphics);
            foreach (int k in _marks) DrawMark(graphics, MarkRect(k));
        }

        private RectangleF MarkRect(int input)
        {
            var comp = (GH_Component)Owner;
            if (input < 0 || input >= comp.Params.Input.Count) return RectangleF.Empty;
            var r = comp.Params.Input[input].Attributes.Bounds;
            return new RectangleF(r.X + 3f, r.Y + r.Height / 2f - 4.5f, 9f, 9f);
        }

        /// <summary>The sign of an input set in a Control Panel: two slider lines with their knobs, in the ink of the
        /// component's text (as Grasshopper's own flatten and graft signs).</summary>
        private void DrawMark(Graphics g, RectangleF m)
        {
            if (m.IsEmpty) return;
            var old = g.SmoothingMode; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var ink = Owner.Locked ? Color.FromArgb(110, 110, 110) : Color.FromArgb(35, 35, 35);
            using (var pen = new Pen(ink, 1f))
            using (var knob = new SolidBrush(ink))
            {
                float[] at = { 0.3f, 0.7f };
                for (int k = 0; k < 2; k++)
                {
                    float yy = m.Y + 2.5f + k * 4f;
                    g.DrawLine(pen, m.X, yy, m.Right, yy);
                    float kx = m.X + m.Width * at[k];
                    g.FillRectangle(knob, kx - 1.25f, yy - 2f, 2.5f, 4f);
                }
            }
            g.SmoothingMode = old;
        }

        // the sign tells where the setting is
        public override bool IsTooltipRegion(PointF canvasPoint) => _marks.Any(k => MarkRect(k).Contains(canvasPoint)) || base.IsTooltipRegion(canvasPoint);
        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            int k = -1; foreach (int x in _marks) if (MarkRect(x).Contains(canvasPoint)) { k = x; break; }
            if (k >= 0)
            {
                var panel = ControlRegistry.PanelOf(Owner);
                var po = panel.HasValue ? Owner.OnPingDocument()?.FindObject(panel.Value, true) : null;
                e.Title = ((GH_Component)Owner).Params.Input[k].Name + ": set in the Control Panel";
                e.Text = "This input gets its value from the Control Panel" + (po != null && po.NickName != po.Name ? " '" + po.NickName + "'" : "") + ". Select the panel to see everything it sets. A wire into the input still wins.";
                return;
            }
            base.SetupTooltip(canvasPoint, e);
        }

        /// <summary>Draws the rows into their rectangles.</summary>
        protected void RenderRows(Graphics graphics)
        {
            var rows = Rows;
            for (int i = 0; i < rows.Count && i < _rects.Count; i++)
            {
                var r = rows[i];
                var rect = _rects[i];
                if (rect.IsEmpty) continue;
                if (r.Kind == RowKind.Label)
                {
                    string lt = r.TextOf != null ? SafeText(r.TextOf) : r.Label;
                    var font = r.Caption ? GH_FontServer.StandardBold : GH_FontServer.Small;
                    // too long: cut with an ellipsis (the full text is in the tooltip of the component it comes from)
                    while (lt.Length > 4 && GH_FontServer.StringWidth(lt, font) > rect.Width - 4) lt = lt.Substring(0, lt.Length - 2).TrimEnd() + "…";
                    using (var f = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                    using (var br = new SolidBrush(Owner.Locked ? Color.Gray : Color.FromArgb(25, 25, 28)))
                        graphics.DrawString(lt, font, br, new RectangleF(rect.X + 2f, rect.Y, rect.Width - 2f, rect.Height), f);
                    continue;
                }
                string text = Text(r, i, out bool active, out bool connected);
                bool number = r.Kind == RowKind.Number && !connected;
                // a number keeps room for its arrow on the right
                var textRect = number ? new RectangleF(rect.X, rect.Y, rect.Width - ArrowWidth, rect.Height) : rect;
                // too long for the component: the value alone (the menu still says what it is)
                int colon = text.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0 && GH_FontServer.StringWidth(text, GH_FontServer.Small) > textRect.Width - 6) text = text.Substring(colon + 2);
                var palette = r.Kind == RowKind.Button || (r.Kind == RowKind.Toggle && active) ? GH_Palette.Black : GH_Palette.White;
                if (connected) palette = GH_Palette.Grey;
                using (var cap = GH_Capsule.CreateTextCapsule(rect, textRect, palette, text, GH_FontServer.Small, 2, 0))
                    cap.Render(graphics, Selected, Owner.Locked, false);
                if (!number) continue;

                // where the value sits in its range: a thin bar along the bottom
                double v = _drag == i ? _dragValue : NumOf(r, Param(r));
                if (r.HasRange)
                {
                    float f = (float)Math.Max(0, Math.Min(1, (v - r.Min) / (r.Max - r.Min)));
                    using (var bar = new SolidBrush(Color.FromArgb(_drag == i ? 200 : 110, 40, 40, 40)))
                        graphics.FillRectangle(bar, rect.X + 3f, rect.Bottom - 3.5f, Math.Max(1.5f, f * (rect.Width - 6f)), 2f);
                }
                // the arrow for the quick picks
                using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), 1f))
                    graphics.DrawLine(pen, rect.Right - ArrowWidth, rect.Y + 4f, rect.Right - ArrowWidth, rect.Bottom - 4f);
                using (var arrow = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
                {
                    float cx = rect.Right - ArrowWidth / 2f, cy = rect.Y + rect.Height / 2f;
                    graphics.FillPolygon(arrow, new[] { new PointF(cx - 3f, cy - 1.5f), new PointF(cx + 3f, cy - 1.5f), new PointF(cx, cy + 2f) });
                }
            }
        }

        private static string SafeText(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
        private static bool Locked(OptionRow r) => r.Wired != null && SafeBool(r.Wired);

        /// <summary>The value of a row as text (for a row whose value comes from a wire).</summary>
        private string Value(OptionRow r, IGH_Param p)
        {
            switch (r.Kind)
            {
                case RowKind.Choice: int v = IntOf(r, p); return r.Items != null && v >= 0 && v < r.Items.Length ? Short(r.Items[v]) : v.ToString(CultureInfo.InvariantCulture);
                case RowKind.Toggle: bool on = r.GetSetting != null ? r.GetSetting() : GetBool(p); return on ? "on" : "off";
                default: return WithUnit(r, NumOf(r, p));
            }
        }

        /// <summary>The row shows an item up to its explanation ("Square to the layer (robot)" -> "Square to the layer"); the menu shows it whole.</summary>
        private static string Short(string item)
        {
            if (string.IsNullOrEmpty(item)) return item;
            int cut = item.Length;
            foreach (string s in new[] { " (", ", ", " - ", ": " }) { int k = item.IndexOf(s, StringComparison.Ordinal); if (k > 0 && k < cut) cut = k; }
            return item.Substring(0, cut);
        }

        private static string Num(double v, int dec) => v.ToString(dec <= 0 ? "0" : "0." + new string('#', dec), CultureInfo.InvariantCulture);

        private static string WithUnit(OptionRow r, double v)
            => Num(v, r.Decimals) + (string.IsNullOrEmpty(r.Unit) ? "" : r.Unit == "°" ? "°" : " " + r.Unit);

        private IGH_Param Param(OptionRow r)
        {
            var comp = (GH_Component)Owner;
            return r.Input >= 0 && r.Input < comp.Params.Input.Count ? comp.Params.Input[r.Input] : null;
        }

        private string Text(OptionRow r, int index, out bool active, out bool connected)
        {
            active = false; connected = false;
            IGH_Param p = Param(r);
            if (p != null && p.SourceCount > 0) { connected = true; return r.Label + ": connected"; }
            if (r.Wired != null && SafeBool(r.Wired)) { connected = true; return r.Label + ": " + Value(r, p) + "  (wire)"; }
            switch (r.Kind)
            {
                case RowKind.Choice:
                    int v = IntOf(r, p);
                    string item = r.Shown != null ? Short(r.Shown) : r.Items != null && v >= 0 && v < r.Items.Length ? Short(r.Items[v]) : v.ToString(CultureInfo.InvariantCulture);
                    return r.Label + ": " + item + "  ▾";
                case RowKind.Number:
                    {
                        double nv = _drag == index ? _dragValue : NumOf(r, p);
                        if (r.ZeroText != null && Math.Abs(nv) < 1e-12) { string z = null; try { z = r.ZeroText(); } catch { } if (!string.IsNullOrEmpty(z)) return r.Label + ": " + z; }
                        return r.Label + ": " + WithUnit(r, nv);
                    }
                case RowKind.Toggle:
                    active = r.GetSetting != null ? r.GetSetting() : GetBool(p);
                    if (!string.IsNullOrEmpty(r.On)) return active ? r.On : r.Off;
                    return r.Label + ": " + (active ? "on" : "off");
                default:
                    return r.Label + (_waiting.Contains(r.Input) ? " (when ready)" : "");
            }
        }

        // ---- values live in the input's own (persistent) data, or in the component (Get / Put)
        private static int IntOf(OptionRow r, IGH_Param p) => p == null && r.Get != null ? (int)Math.Round(r.Get()) : GetInt(p);
        private static double NumOf(OptionRow r, IGH_Param p) => p == null && r.Get != null ? r.Get() : GetNumber(p);

        /// <summary>A row kept by the component itself: store the value and solve again.</summary>
        private bool PutOwn(OptionRow r, double value)
        {
            if (Param(r) != null || r.Put == null) return false;
            var comp = (GH_Component)Owner;
            if (r.External) { r.Put(value); return true; }
            comp.RecordUndoEvent("Change " + r.Label);
            r.Put(value);
            comp.ExpireSolution(true);
            return true;
        }

        private static int GetInt(IGH_Param p)
        {
            if (p is Param_Integer pi && pi.PersistentData.DataCount > 0) return pi.PersistentData.get_FirstItem(true).Value;
            if (p != null && p.VolatileDataCount > 0 && GH_Convert.ToInt32(p.VolatileData.AllData(true).First(), out int x, GH_Conversion.Both)) return x;
            return 0;
        }

        private static double GetNumber(IGH_Param p)
        {
            if (p is Param_Number pn && pn.PersistentData.DataCount > 0) return pn.PersistentData.get_FirstItem(true).Value;
            if (p is Param_Integer pi && pi.PersistentData.DataCount > 0) return pi.PersistentData.get_FirstItem(true).Value;
            if (p != null && p.VolatileDataCount > 0 && GH_Convert.ToDouble(p.VolatileData.AllData(true).First(), out double x, GH_Conversion.Both)) return x;
            return 0;
        }

        private static bool GetBool(IGH_Param p, bool solved = false)
        {
            if (!solved && p is Param_Boolean pb && pb.PersistentData.DataCount > 0) return pb.PersistentData.get_FirstItem(true).Value;
            if (p != null && p.VolatileDataCount > 0 && GH_Convert.ToBoolean(p.VolatileData.AllData(true).First(), out bool x, GH_Conversion.Both)) return x;
            return false;
        }

        internal static void Set(GH_Component comp, int input, IGH_Goo value, bool recompute, Action afterUndoRecord = null, bool undo = true)
        {
            var p = comp.Params.Input[input];
            if (undo) comp.RecordUndoEvent("Change " + p.Name);
            afterUndoRecord?.Invoke();
            if (p is Param_Integer pi && value is GH_Integer gi) { pi.PersistentData.Clear(); pi.PersistentData.Append(gi); }
            else if (p is Param_Integer pi2 && value is GH_Number gn2) { pi2.PersistentData.Clear(); pi2.PersistentData.Append(new GH_Integer((int)Math.Round(gn2.Value))); }
            else if (p is Param_Number pn && value is GH_Number gn) { pn.PersistentData.Clear(); pn.PersistentData.Append(gn); }
            else if (p is Param_Boolean pb && value is GH_Boolean gb) { pb.PersistentData.Clear(); pb.PersistentData.Append(gb); }
            else return;
            // the input itself must expire, or it keeps the value it collected before (and so does
            // everything after it); without recompute it takes the new value at the next solution
            p.ExpireSolution(recompute);
        }

        /// <summary>Sets a number row, rounded to its steps (an integer input gets an integer).</summary>
        private void SetNumber(OptionRow r, double value)
        {
            var comp = (GH_Component)Owner;
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            if (Param(r) is Param_Integer) value = Math.Round(value);
            if (Math.Abs(value - NumOf(r, Param(r))) < 1e-12) return; // nothing changed: no new solution
            if (PutOwn(r, value)) return;
            Set(comp, r.Input, new GH_Number(value), true);
        }

        /// <summary>Reads a typed number: "45", "45°", "12,5 mm", "1.5e3".</summary>
        internal static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace(',', '.');
            int end = 0;
            while (end < t.Length && ("+-.0123456789eE".IndexOf(t[end]) >= 0)) end++;
            // "1e" or a unit starting with e: try shorter until it parses
            for (int n = end; n > 0; n--)
                if (double.TryParse(t.Substring(0, n), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }

        private int RowAt(PointF canvasPoint)
        {
            // a label (heading, line of information) is not a control: there the component can be dragged
            for (int i = 0; i < Rows.Count && i < _rects.Count; i++) if (!_rects[i].IsEmpty && Rows[i].Kind != RowKind.Label && _rects[i].Contains(canvasPoint)) return i;
            return -1;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // right click on a setting of a Control Panel: make it a real slider (for an optimiser such as Galapagos)
            if (e.Button == MouseButtons.Right)
            {
                int ri = RowAt(e.CanvasLocation);
                if (ri >= 0 && Rows[ri].MakeSlider != null && !Locked(Rows[ri]))
                {
                    var r = Rows[ri];
                    var menu = new ContextMenuStrip();
                    var item = new ToolStripMenuItem("Slider for an optimiser (Galapagos)") { ToolTipText = "Puts a number slider into this input, beside its component. Galapagos and other optimisers can then change it as a gene; the panel shows the slider." };
                    item.Click += (s2, a2) => r.MakeSlider();
                    menu.Items.Add(item);
                    menu.Show(sender, e.ControlLocation);
                    return GH_ObjectResponse.Handled;
                }
            }
            if (e.Button == MouseButtons.Left)
            {
                int i = RowAt(e.CanvasLocation);
                if (i >= 0)
                {
                    var r = Rows[i];
                    var p = Param(r);
                    if (Locked(r)) return GH_ObjectResponse.Handled; // the wire decides
                    if (r.Kind == RowKind.Number && (p == null || p.SourceCount == 0) && !Owner.Locked)
                    {
                        // the arrow: quick picks; elsewhere: drag to change it, or click to type
                        if (e.CanvasLocation.X >= _rects[i].Right - ArrowWidth) { NumberMenu(sender, e, r, i); return GH_ObjectResponse.Handled; }
                        _drag = i; _dragStart = e.ControlLocation; _dragFrom = NumOf(r, p); _dragValue = _dragFrom; _moved = false;
                        return GH_ObjectResponse.Capture;
                    }
                    Activate(sender, e, r);
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_drag >= 0 && _drag < Rows.Count)
            {
                var r = Rows[_drag];
                float dx = e.ControlLocation.X - _dragStart.X;
                if (!_moved && Math.Abs(dx) < 3f) return GH_ObjectResponse.Handled;
                _moved = true;
                // the whole range over about 250 pixels; Shift = ten times finer
                double span = r.HasRange ? r.Max - r.Min : Math.Max(10 * r.StepSize, Math.Abs(_dragFrom) * 2 + 10 * r.StepSize);
                double perPixel = span / 250.0;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) perPixel *= 0.1;
                double step = r.StepSize;
                double v = _dragFrom + dx * perPixel;
                v = Math.Round(v / step) * step;
                if (r.HasRange) v = Math.Max(Math.Min(r.Min, _dragFrom), Math.Min(Math.Max(r.Max, _dragFrom), v));
                _dragValue = Math.Round(v, Math.Max(0, r.Decimals) + 2);
                sender.Refresh();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseMove(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_drag >= 0)
            {
                int i = _drag;
                _drag = -1;
                if (i < Rows.Count)
                {
                    var r = Rows[i];
                    // one solution when the drag ends, not one per pixel
                    if (_moved) SetNumber(r, _dragValue);
                    else TypeNumber(sender, r, i);
                }
                sender.Refresh();
                return GH_ObjectResponse.Release;
            }
            return base.RespondToMouseUp(sender, e);
        }

        /// <summary>A small text box right over the row: type a value, Enter (Escape keeps the old one).</summary>
        private void TypeNumber(GH_Canvas canvas, OptionRow r, int i)
        {
            if (i >= _rects.Count) return;
            var box = new NumberBox(text => { if (TryParseNumber(text, out double v)) SetNumber(r, v); });
            var rect = _rects[i];
            box.Bounds = GH_Convert.ToRectangle(new RectangleF(rect.X + 1, rect.Y, rect.Width - ArrowWidth - 1, rect.Height));
            box.Font = GH_FontServer.Small;
            box.ShowTextInputBox(canvas, Num(NumOf(r, Param(r)), r.Decimals), true, true, canvas.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl));
        }

        private sealed class NumberBox : Grasshopper.GUI.Base.GH_TextBoxInputBase
        {
            private readonly Action<string> _accept;
            public NumberBox(Action<string> accept) { _accept = accept; }
            protected override void HandleTextInputAccepted(string text) => _accept(text);
        }

        /// <summary>Quick picks, the default, and a field to type a value.</summary>
        private void NumberMenu(GH_Canvas canvas, GH_CanvasMouseEvent e, OptionRow r, int i)
        {
            var menu = new ContextMenuStrip();
            double now = NumOf(r, Param(r));
            foreach (double v in r.Presets ?? new double[0])
            {
                double value = v;
                var item = new ToolStripMenuItem(WithUnit(r, v)) { Checked = Math.Abs(v - now) < 1e-9 };
                item.Click += (s, a) => SetNumber(r, value);
                menu.Items.Add(item);
            }
            if (!double.IsNaN(r.Default) && !(r.Presets ?? new double[0]).Any(v => Math.Abs(v - r.Default) < 1e-9))
            {
                var def = new ToolStripMenuItem("Default: " + WithUnit(r, r.Default));
                def.Click += (s, a) => SetNumber(r, r.Default);
                menu.Items.Add(def);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripLabel("Type a value, then Enter:"));
            GH_DocumentObject.Menu_AppendTextItem(menu, Num(now, r.Decimals), (s, a) =>
            {
                if (a.KeyCode != Keys.Enter) return;
                var box = s as Grasshopper.GUI.GH_MenuTextBox;
                if (box != null && TryParseNumber(box.Text, out double typed))
                {
                    menu.Close();
                    SetNumber(r, typed);
                }
            }, null, true);
            var hint = new ToolStripLabel("Tip: drag the row left or right (Shift = fine)") { ForeColor = Color.DimGray };
            menu.Items.Add(hint);
            menu.Show(canvas, e.ControlLocation);
        }

        private void Activate(GH_Canvas canvas, GH_CanvasMouseEvent e, OptionRow r)
        {
            var comp = (GH_Component)Owner;
            IGH_Param p = Param(r);
            if ((p != null && p.SourceCount > 0) || Locked(r)) return; // a wire decides
            switch (r.Kind)
            {
                case RowKind.Choice:
                {
                    var menu = new ContextMenuStrip();
                    int now = IntOf(r, p);
                    for (int k = 0; r.Items != null && k < r.Items.Length; k++)
                    {
                        int value = k;
                        var item = new ToolStripMenuItem(r.Items[k]) { Checked = r.Shown != null ? r.Items[k] == r.Shown : k == now };
                        item.Click += (s, a) => { if (PutOwn(r, value)) { r.Picked?.Invoke(value); canvas.Refresh(); return; } Set(comp, r.Input, new GH_Integer(value), true, () => r.Picked?.Invoke(value)); };
                        menu.Items.Add(item);
                    }
                    menu.Show(canvas, e.ControlLocation);
                    break;
                }
                case RowKind.Number:
                case RowKind.Label:
                    break; // handled by drag / click / arrow; a label does nothing
                case RowKind.Toggle:
                    if (r.SetSetting != null && r.GetSetting != null && r.External) r.SetSetting(!r.GetSetting());
                    else if (r.SetSetting != null && r.GetSetting != null)
                    {
                        comp.RecordUndoEvent("Change " + r.Label);
                        r.SetSetting(!r.GetSetting());
                        comp.ExpireSolution(true);
                    }
                    else Set(comp, r.Input, new GH_Boolean(!GetBool(p)), true);
                    break;
                case RowKind.Button:
                    if (r.Momentary && p != null && comp.Locked) return; // a disabled component does not solve: Save would stay true
                    if (r.Momentary && p != null) Press(comp, r, p, true);
                    r.Click?.Invoke(comp);
                    break;
            }
            canvas.Refresh();
        }

        /// <summary>Presses a button row of this component from elsewhere (the Result component), as a click on it would.</summary>
        internal void PressRow(OptionRow r)
        {
            var comp = (GH_Component)Owner;
            if (r == null || r.Kind != RowKind.Button) return;
            IGH_Param p = Param(r);
            if (p != null && p.SourceCount > 0) return;
            if (r.Momentary && p != null && comp.Locked) return;
            if (r.Momentary && p != null) Press(comp, r, p, true);
            r.Click?.Invoke(comp);
        }

        /// <summary>The value an input holds now (its own data), for rows shown on another component.</summary>
        internal static double ValueOf(IGH_Param p)
        {
            // with a wire: the value it brings (the one typed in before no longer counts)
            if (p != null && p.SourceCount > 0)
                return p is Param_Boolean ? (GetBool(p, true) ? 1 : 0)
                     : p.VolatileDataCount > 0 && GH_Convert.ToDouble(p.VolatileData.AllData(true).First(), out double x, GH_Conversion.Both) ? x : 0;
            return p is Param_Boolean ? (GetBool(p) ? 1 : 0) : GetNumber(p);
        }

        // Save rows (by input) pressed while the component waits for a background calculation above it
        private readonly HashSet<int> _waiting = new HashSet<int>();

        /// <summary>True for one solution (writes the file), then quietly back to false.</summary>
        private void Press(GH_Component comp, OptionRow r, IGH_Param p, bool now)
        {
            var doc = comp.OnPingDocument();
            if (doc != null)
            {
                GH_Document.SolutionEndEventHandler back = null;
                // back to false once a solution has run with true (not an earlier one still running)
                back = (s, a) =>
                {
                    if (!GetBool(p, true)) return;
                    doc.SolutionEnd -= back;
                    // only the value goes back: the stored one and the one the input holds now. The
                    // component keeps its results (expiring it here left it blank until the next
                    // solution, e.g. the DXR Generator without DXR and Info after Save).
                    if (p is Param_Boolean pb)
                    {
                        pb.PersistentData.Clear(); pb.PersistentData.Append(new GH_Boolean(false));
                        pb.ClearData();
                        pb.AddVolatileData(new Grasshopper.Kernel.Data.GH_Path(0), 0, new GH_Boolean(false));
                        comp.OnDisplayExpired(false);
                    }
                    // nothing to save yet: pressed again as soon as the result is there
                    if (AsyncSolve.Waiting(comp) != null) PressLater(doc, comp, r, p);
                };
                doc.SolutionEnd += back;
            }
            Set(comp, r.Input, new GH_Boolean(true), now, null, now);
        }

        private void PressLater(GH_Document doc, GH_Component comp, OptionRow r, IGH_Param p)
        {
            if (!_waiting.Add(r.Input)) return;
            GH_Document.SolutionEndEventHandler later = null;
            later = (s, a) =>
            {
                bool gone = comp.OnPingDocument() != doc || comp.Locked || p.SourceCount > 0;
                if (!gone && AsyncSolve.Waiting(comp) != null) return; // still waiting
                doc.SolutionEnd -= later;
                _waiting.Remove(r.Input);
                if (!gone) doc.ScheduleSolution(1, d => { if (comp.OnPingDocument() == d && !comp.Locked && p.SourceCount == 0) Press(comp, r, p, false); });
            };
            doc.SolutionEnd += later;
        }
    }
}
