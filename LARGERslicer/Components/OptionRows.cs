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
    /// </summary>
    public class OptionRow
    {
        public RowKind Kind;
        public string Label;
        public int Input = -1;
        public string[] Items;            // Choice: item i has the value i
        public double[] Presets;          // Number: quick picks
        public int Decimals = 1;
        public string Unit = "";
        public string On, Off;            // Toggle: what the row says in either state (empty = Label: on / off)
        public Action<GH_Component> Click; // Button
        public bool Momentary;            // Button on a boolean input: true for one solution, then back to false
    }

    public interface IOptionRows
    {
        List<OptionRow> Rows { get; }
    }

    /// <summary>Draws the rows of an IOptionRows component under it and answers clicks on them.</summary>
    public class OptionAttributes : GH_ComponentAttributes
    {
        private const float RowHeight = 18f, Gap = 3f;
        private readonly List<RectangleF> _rects = new List<RectangleF>();

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
                string text = Text(r, out bool active, out bool connected);
                // too long for the component: the value alone (the menu still says what it is)
                int colon = text.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0 && GH_FontServer.StringWidth(text, GH_FontServer.Small) > _rects[i].Width - 6) text = text.Substring(colon + 2);
                var palette = r.Kind == RowKind.Button || (r.Kind == RowKind.Toggle && active) ? GH_Palette.Black : GH_Palette.White;
                if (connected) palette = GH_Palette.Grey;
                using (var cap = GH_Capsule.CreateTextCapsule(_rects[i], _rects[i], palette, text, GH_FontServer.Small, 2, 0))
                    cap.Render(graphics, Selected, Owner.Locked, false);
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

        private string Text(OptionRow r, out bool active, out bool connected)
        {
            active = false; connected = false;
            var comp = (GH_Component)Owner;
            IGH_Param p = r.Input >= 0 && r.Input < comp.Params.Input.Count ? comp.Params.Input[r.Input] : null;
            if (p != null && p.SourceCount > 0) { connected = true; return r.Label + ": connected"; }
            switch (r.Kind)
            {
                case RowKind.Choice:
                    int v = GetInt(p);
                    string item = r.Items != null && v >= 0 && v < r.Items.Length ? Short(r.Items[v]) : v.ToString(CultureInfo.InvariantCulture);
                    return r.Label + ": " + item + "  ▾";
                case RowKind.Number:
                    return r.Label + ": " + Num(GetNumber(p), r.Decimals) + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit) + "  ▾";
                case RowKind.Toggle:
                    active = GetBool(p);
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

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var rows = Rows;
                for (int i = 0; i < rows.Count && i < _rects.Count; i++)
                {
                    if (!_rects[i].Contains(e.CanvasLocation)) continue;
                    Activate(sender, e, rows[i]);
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        private void Activate(GH_Canvas canvas, GH_CanvasMouseEvent e, OptionRow r)
        {
            var comp = (GH_Component)Owner;
            IGH_Param p = r.Input >= 0 && r.Input < comp.Params.Input.Count ? comp.Params.Input[r.Input] : null;
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
                {
                    var menu = new ContextMenuStrip();
                    double now = GetNumber(p);
                    foreach (double v in r.Presets ?? new double[0])
                    {
                        double value = v;
                        var item = new ToolStripMenuItem(Num(v, r.Decimals) + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit)) { Checked = Math.Abs(v - now) < 1e-9 };
                        item.Click += (s, a) => Set(comp, r.Input, new GH_Number(value), true);
                        menu.Items.Add(item);
                    }
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(new ToolStripLabel("Type a value, then Enter:"));
                    GH_DocumentObject.Menu_AppendTextItem(menu, Num(now, r.Decimals), (s, a) =>
                    {
                        if (a.KeyCode != Keys.Enter) return;
                        var box = s as Grasshopper.GUI.GH_MenuTextBox;
                        if (box != null && double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double typed))
                        {
                            menu.Close();
                            Set(comp, r.Input, new GH_Number(typed), true);
                        }
                    }, null, true);
                    menu.Show(canvas, e.ControlLocation);
                    break;
                }
                case RowKind.Toggle:
                    Set(comp, r.Input, new GH_Boolean(!GetBool(p)), true);
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
                            back = (s, a) => { if (!GetBool(p, true)) return; doc.SolutionEnd -= back; Set(comp, r.Input, new GH_Boolean(false), false); };
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
