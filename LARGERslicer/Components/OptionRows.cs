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
    public enum RowKind { Choice, Number, Toggle, Button }

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

        internal double StepSize => Step > 0 ? Step : Math.Pow(10, -Math.Max(0, Decimals));
        internal bool HasRange => !double.IsNaN(Min) && !double.IsNaN(Max) && Max > Min;
    }

    public interface IOptionRows
    {
        List<OptionRow> Rows { get; }
    }

    /// <summary>Draws the rows of an IOptionRows component under it and answers clicks and drags on them.</summary>
    public class OptionAttributes : GH_ComponentAttributes
    {
        private const float RowHeight = 18f, Gap = 3f, ArrowWidth = 14f;
        private readonly List<RectangleF> _rects = new List<RectangleF>();

        /// <summary>True while a Save button quietly goes back to false (no new solution, nothing really changed).</summary>
        internal static bool QuietReset;

        // a number row being dragged
        private int _drag = -1;
        private PointF _dragStart;
        private double _dragFrom, _dragValue;
        private bool _moved;

        public OptionAttributes(IGH_Component owner) : base(owner) { }

        private List<OptionRow> Rows => (Owner as IOptionRows)?.Rows ?? new List<OptionRow>();

        protected override void Layout()
        {
            base.Layout();
            _rects.Clear();
            var rows = Rows;
            if (rows.Count == 0) return;
            var b = Bounds;
            float y = b.Bottom + 2f;
            foreach (var r in rows)
            {
                _rects.Add(new RectangleF(b.X + 4f, y, b.Width - 8f, RowHeight));
                y += RowHeight + Gap;
            }
            Bounds = new RectangleF(b.X, b.Y, b.Width, y - b.Y + 2f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            var rows = Rows;
            for (int i = 0; i < rows.Count && i < _rects.Count; i++)
            {
                var r = rows[i];
                var rect = _rects[i];
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
                double v = _drag == i ? _dragValue : GetNumber(Param(r));
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
            switch (r.Kind)
            {
                case RowKind.Choice:
                    int v = GetInt(p);
                    string item = r.Items != null && v >= 0 && v < r.Items.Length ? Short(r.Items[v]) : v.ToString(CultureInfo.InvariantCulture);
                    return r.Label + ": " + item + "  ▾";
                case RowKind.Number:
                    {
                        double nv = _drag == index ? _dragValue : GetNumber(p);
                        if (r.ZeroText != null && Math.Abs(nv) < 1e-12) { string z = null; try { z = r.ZeroText(); } catch { } if (!string.IsNullOrEmpty(z)) return r.Label + ": " + z; }
                        return r.Label + ": " + WithUnit(r, nv);
                    }
                case RowKind.Toggle:
                    active = r.GetSetting != null ? r.GetSetting() : GetBool(p);
                    if (!string.IsNullOrEmpty(r.On)) return active ? r.On : r.Off;
                    return r.Label + ": " + (active ? "on" : "off");
                default:
                    return r.Label;
            }
        }

        // ---- values live in the input's own (persistent) data
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

        internal static void Set(GH_Component comp, int input, IGH_Goo value, bool recompute)
        {
            var p = comp.Params.Input[input];
            comp.RecordUndoEvent("Change " + p.Name);
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
            if (Math.Abs(value - GetNumber(Param(r))) < 1e-12) return; // nothing changed: no new solution
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
            for (int i = 0; i < Rows.Count && i < _rects.Count; i++) if (_rects[i].Contains(canvasPoint)) return i;
            return -1;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int i = RowAt(e.CanvasLocation);
                if (i >= 0)
                {
                    var r = Rows[i];
                    var p = Param(r);
                    if (r.Kind == RowKind.Number && (p == null || p.SourceCount == 0) && !Owner.Locked)
                    {
                        // the arrow: quick picks; elsewhere: drag to change it, or click to type
                        if (e.CanvasLocation.X >= _rects[i].Right - ArrowWidth) { NumberMenu(sender, e, r, i); return GH_ObjectResponse.Handled; }
                        _drag = i; _dragStart = e.ControlLocation; _dragFrom = GetNumber(p); _dragValue = _dragFrom; _moved = false;
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
            box.ShowTextInputBox(canvas, Num(GetNumber(Param(r)), r.Decimals), true, true, canvas.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl));
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
            double now = GetNumber(Param(r));
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
            if (p != null && p.SourceCount > 0) return; // a wire decides
            switch (r.Kind)
            {
                case RowKind.Choice:
                {
                    var menu = new ContextMenuStrip();
                    int now = GetInt(p);
                    for (int k = 0; r.Items != null && k < r.Items.Length; k++)
                    {
                        int value = k;
                        var item = new ToolStripMenuItem(r.Items[k]) { Checked = k == now };
                        item.Click += (s, a) => Set(comp, r.Input, new GH_Integer(value), true);
                        menu.Items.Add(item);
                    }
                    menu.Show(canvas, e.ControlLocation);
                    break;
                }
                case RowKind.Number:
                    break; // handled by drag / click / arrow
                case RowKind.Toggle:
                    if (r.SetSetting != null && r.GetSetting != null)
                    {
                        comp.RecordUndoEvent("Change " + r.Label);
                        r.SetSetting(!r.GetSetting());
                        comp.ExpireSolution(true);
                    }
                    else Set(comp, r.Input, new GH_Boolean(!GetBool(p)), true);
                    break;
                case RowKind.Button:
                    if (r.Momentary && p != null)
                    {
                        // true for one solution (writes the file), then quietly back to false
                        var doc = comp.OnPingDocument();
                        if (doc != null)
                        {
                            GH_Document.SolutionEndEventHandler back = null;
                            // back to false once a solution has run with true (not an earlier one still running)
                            back = (s, a) =>
                            {
                                if (!GetBool(p, true)) return;
                                doc.SolutionEnd -= back;
                                QuietReset = true;
                                try { Set(comp, r.Input, new GH_Boolean(false), false); }
                                finally { QuietReset = false; }
                            };
                            doc.SolutionEnd += back;
                        }
                        Set(comp, r.Input, new GH_Boolean(true), true);
                    }
                    r.Click?.Invoke(comp);
                    break;
            }
            canvas.Refresh();
        }
    }
}
