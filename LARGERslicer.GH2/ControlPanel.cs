using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Doc.Attributes;
using Grasshopper2.Parameters;
using Grasshopper2.SpecialObjects;
using Grasshopper2.UI;
using Grasshopper2.UI.Flex;
using Grasshopper2.UI.Icon;
using Grasshopper2.UI.Skinning;
using GrasshopperIO;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Which components a Control Panel or a Result looks after: those of its group, or, outside any group, every
    /// LARGER component on the canvas that is not in a group with a panel of its own (as in Grasshopper 1).
    /// </summary>
    internal static class Scope
    {
        public static bool IsOurs(IDocumentObject o) => o != null && o.GetType().Assembly == typeof(Scope).Assembly;

        public static List<Component> Members(Component owner)
        {
            var list = new List<Component>();
            var doc = owner?.Document;
            if (doc == null) return list;
            try
            {
                var groups = doc.Objects.ContainingGroups(owner.InstanceId).ToList();
                IEnumerable<Component> pool;
                if (groups.Count > 0)
                    pool = groups.SelectMany(g => g.ContentIds).Distinct().Select(id => doc.Objects.FindComponent(id)).Where(c => c != null);
                else
                {
                    // groups that have a panel of their own are left to it
                    var taken = new HashSet<Guid>();
                    foreach (var g in doc.Objects.Groups)
                    {
                        var ids = g.ContentIds.ToList();
                        if (ids.Any(id => doc.Objects.FindComponent(id) is ControlPanelComponent)) taken.UnionWith(ids);
                    }
                    pool = doc.Objects.ForwardsOfT<Component>().Where(c => !taken.Contains(c.InstanceId));
                }
                foreach (var c in pool)
                    if (!ReferenceEquals(c, owner) && IsOurs(c) && !(c is ControlPanelComponent) && !(c is ResultComponent)) list.Add(c);
                return list.OrderBy(c => c.Attributes?.Pivot.X ?? 0).ThenBy(c => c.Attributes?.Pivot.Y ?? 0).ToList();
            }
            catch { return new List<Component>(); } // a document still being read
        }

        public static string NameOf(IDocumentObject o) => o?.Nomen.Name ?? "";

        /// <summary>The value an input holds now: its own value, or, with a wire, what it got in the last solution.</summary>
        public static object ValueOf(IParameter p)
        {
            try
            {
                if (p.Inputs.Count > 0) return p.State?.Data?.Tree()?.NonNullItems.FirstOrDefault();
                return p.PersistentDataWeak?.NonNullItems.FirstOrDefault();
            }
            catch { return null; }
        }

        public static object OutputOf(Component c, string name)
        {
            try
            {
                var p = c.Parameters.Outputs.FirstOrDefault(x => x.Nomen.Name == name);
                return p?.State?.Data?.Tree()?.NonNullItems.FirstOrDefault();
            }
            catch { return null; }
        }

        /// <summary>Sets the own value of an input and solves again.</summary>
        public static void Put(IParameter p, object value)
        {
            if (p == null || p.Inputs.Count > 0) return;
            if (!Io.SetValue(p, value)) return;
            p.Expire();
            var doc = p.Document;
            if (doc == null) return;
            doc.Modify();
            doc.Solution.Start();
        }

        public static string Show(IParameter p, object v)
        {
            if (v == null) return "-";
            var ci = CultureInfo.InvariantCulture;
            if (p is Parameter<int> pi && v is int i)
            {
                var name = pi.Presets?.AllPresets.FirstOrDefault(x => Equals(x.Value, i))?.Name;
                return string.IsNullOrEmpty(name) || name == i.ToString(ci) ? i.ToString(ci) + Unit(p.Nomen.Name) : name;
            }
            if (v is bool b) return b ? "on" : "off";
            if (v is double d) return d.ToString("0.###", ci) + Unit(p.Nomen.Name);
            if (v is int n) return n.ToString(ci) + Unit(p.Nomen.Name);
            return v.ToString();
        }

        /// <summary>The unit a setting is given in, from its name.</summary>
        private static string Unit(string name)
        {
            if (name.Contains("Temperature")) return " °C";
            if (name == "Off For" || name == "Full After") return " layers";
            if (name == "Speed" || name.EndsWith(" Speed")) return " mm/s";
            if (name.Contains("Time")) return name == "Time" ? "" : " s";
            if (name == "Then" || name == "Full" || name == "Cooling" || name.Contains("Fan")) return " %";
            if (name == "Angle" || name == "Turn" || name.EndsWith(" Angle")) return "°";
            string[] mm = { "Width", "Height", "Length", "Thickness", "Diameter", "Gap", "Amplitude", "Pitch", "Wavelength", "Stock", "Clearance", "Blend", "Size", "Reach", "Falloff", "Plate" };
            return mm.Any(name.Contains) ? " mm" : "";
        }
    }

    // ============================================================== Control Panel
    /// <summary>
    /// Control Panel: the main settings of every LARGER component of its group in one place, without wires.
    /// Click a setting to change it; a wire into an input still wins (the row then shows what the wire gives).
    /// The input panel (right click) holds every setting.
    /// </summary>
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d0101")]
    public sealed class ControlPanelComponent : Component
    {
        public ControlPanelComponent() : base(Io.Name("Control Panel", "The main settings of every LARGER component in its group, in one place and without wires. Click a setting to change it. A wire into an input still wins. Outside a group it looks after every LARGER component on the canvas that is not in a group with a panel of its own. Right click: every setting.", Io.Slicer, Group.Start)) { }
        public ControlPanelComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("ControlPanelIcon");
        protected override void AddInputs(InputAdder inputs) { }
        protected override void AddOutputs(OutputAdder outputs) { }
        protected override void Process(IDataAccess access) { }

        protected override IAttributes CreateAttributes() => new RowAttributes(this, "Control Panel", 230);

        internal bool Advanced
        {
            get => CustomValues.Get("LARGER_Advanced", false);
            set { CustomValues.Set("LARGER_Advanced", value); Attributes?.InvalidateLayout(); }
        }

        // the main settings of each component (as in Grasshopper 1); a component not listed shows every setting
        internal static readonly Dictionary<string, string[]> Essential = new Dictionary<string, string[]>
        {
            { "Machine Profile", new[] { "Machine" } },
            { "Print Settings", new[] { "Nozzle", "Bead Width", "Layer Height", "Speed", "Flow" } },
            { "Easy Slicer", new[] { "Bead Width", "Layer Height", "Speed", "Walls", "Bottom Layers", "Spiral", "Brim" } },
            { "Slice", new[] { "Angle", "Guide Mode" } },
            { "Walls", new[] { "Count" } },
            { "Bottom Fill", new[] { "Count", "Pattern" } },
            { "Infill Wave", new[] { "Shape", "Wavelength" } },
            { "Continuous Path", new[] { "Mode", "Seam Gap", "Min Layer Time" } },
            { "Machine Settings", new[] { "Bed Temperature", "Nozzle Temperature", "Cooling" } },
            { "Machine", new[] { "Machine", "Extrusion Factor", "Bed Temperature", "Nozzle Temperature" } },
            { "Material Factor", new[] { "Target Width", "Current Factor" } },
            { "Weave", new[] { "Pattern", "Amplitude", "Wavelength", "Faces" } },
            { "Surface Pattern", new[] { "Pattern", "Amplitude", "Pitch", "Side" } },
            { "Shape Field", new[] { "Mode", "Min Factor", "Max Factor" } },
            { "Reshape Path", new[] { "Variant", "Min Layer", "Max Layer" } },
            { "Start and End", new[] { "Skirt Loops", "Brim" } },
            { "Path Check", new string[0] },
            { "Round Corners", new string[0] },
            { "Desktop Test Print", new[] { "Printer" } },
            { "Cell Geometry", new[] { "Cell" } },
            { "CNC - Relief", new[] { "Pattern", "Length", "Width", "Height" } },
            { "CNC - Plate", new[] { "Length", "Width", "Thickness", "Material" } },
            { "CNC - Cutter", new[] { "Cutter", "Magazine" } },
            { "CNC - Own Cutter", new[] { "Shape", "Diameter" } },
            { "CNC - Rough and Finish", new[] { "Roughing", "Finishing", "Stock", "Tabs" } },
            { "Robot", new[] { "Robot" } },
            { "Robot Tool", new[] { "Preset" } },
            { "Robot Program", new[] { "Blend", "Turn" } },
            { "Robot Simulation", new[] { "Time" } },
            { "Fan Schedule", new[] { "Off For", "Then", "Full After", "Full" } },
            { "Printer Simulation", new string[0] },
        };

        // Save and Upload are buttons of the Result
        internal static bool Settable(IParameter p) =>
            p.Access == Access.Item && (p is Parameter<int> || p is Parameter<double> || p is Parameter<bool>)
            && p.Nomen.Name != "Save" && p.Nomen.Name != "Upload";

        internal List<Row> BuildRows()
        {
            var rows = new List<Row>();
            foreach (var c in Scope.Members(this))
            {
                var inputs = c.Parameters.Inputs.Where(Settable).ToList();
                if (!Advanced && Essential.TryGetValue(c.Nomen.Name, out var keep))
                    inputs = inputs.Where(p => keep.Any(k => p.Nomen.Name.StartsWith(k, StringComparison.Ordinal))).ToList();
                if (inputs.Count == 0) continue;
                rows.Add(new Row { Caption = true, Text = c.Nomen.Name });
                foreach (var p in inputs) rows.Add(new Row { Param = p });
            }
            if (rows.Count == 0) rows.Add(new Row { Info = true, Text = "Put it in a group with LARGER components." });
            rows.Add(new Row { Toggle = true, Text = Advanced ? "Advanced: every setting" : "Advanced: the main settings" });
            return rows;
        }

        internal void Clicked(Row r)
        {
            if (r.Toggle) { Advanced = !Advanced; Document?.Modify(); return; }
            var p = r.Param;
            if (p == null) return;
            if (p.Inputs.Count > 0) return; // a wire wins
            var v = Scope.ValueOf(p);
            if (p is Parameter<bool>) { Scope.Put(p, !(v is bool b && b)); return; }
            var menu = new Eto.Forms.ContextMenu();
            var ci = CultureInfo.InvariantCulture;
            void Add(string text, object value, bool current)
            {
                var item = new Eto.Forms.CheckMenuItem { Text = text, Checked = current };
                item.Click += (s, e) => Scope.Put(p, value);
                menu.Items.Add(item);
            }
            if (p is Parameter<int> pi)
            {
                int now = v is int i ? i : 0;
                var presets = pi.Presets?.AllPresets.ToList();
                if (presets != null && presets.Count > 0) foreach (var pre in presets) Add(pre.Name, pre.Value, pre.Value == now);
                else foreach (var k in new[] { now - 2, now - 1, now + 1, now + 2 }.Where(k => k >= 0)) Add(k.ToString(ci), k, false);
            }
            else if (p is Parameter<double> pd)
            {
                double now = v is double d ? d : 0;
                var presets = pd.Presets?.AllPresets.ToList();
                if (presets != null && presets.Count > 0) foreach (var pre in presets) Add(pre.Name, pre.Value, Math.Abs(pre.Value - now) < 1e-9);
                else foreach (var k in Nice(now)) Add(k.ToString("0.###", ci), k, Math.Abs(k - now) < 1e-9);
            }
            menu.Items.Add(new Eto.Forms.SeparatorMenuItem());
            var type = new Eto.Forms.ButtonMenuItem { Text = "Type a value ..." };
            type.Click += (s, e) => { var t = Ask(Scope.NameOf(p), Scope.Show(p, v)); if (t != null) Parse(p, t); };
            menu.Items.Add(type);
            menu.Show();
        }

        /// <summary>A few round values around the current one.</summary>
        private static double[] Nice(double v)
        {
            if (v == 0) return new[] { 0, 0.5, 1, 2, 5, 10 };
            double a = Math.Abs(v), mag = Math.Pow(10, Math.Floor(Math.Log10(a)));
            var list = new List<double> { v * 0.5, v * 0.75, v, v * 1.25, v * 1.5, v * 2 };
            return list.Select(x => Math.Round(x / (mag / 10)) * (mag / 10)).Distinct().ToArray();
        }

        private static void Parse(IParameter p, string text)
        {
            text = text.Trim().Replace(',', '.');
            var ci = CultureInfo.InvariantCulture;
            if (p is Parameter<int> && int.TryParse(text, NumberStyles.Integer, ci, out int i)) Scope.Put(p, i);
            else if (p is Parameter<double> && double.TryParse(text, NumberStyles.Float, ci, out double d)) Scope.Put(p, d);
        }

        /// <summary>A small window to type a value in.</summary>
        internal static string Ask(string title, string now)
        {
            var dlg = new Eto.Forms.Dialog<string> { Title = title, Padding = 10, Resizable = false };
            var box = new Eto.Forms.TextBox { Text = now == "-" ? "" : now, Width = 160 };
            var ok = new Eto.Forms.Button { Text = "OK" };
            ok.Click += (s, e) => dlg.Close(box.Text);
            var cancel = new Eto.Forms.Button { Text = "Cancel" };
            cancel.Click += (s, e) => dlg.Close(null);
            dlg.DefaultButton = ok; dlg.AbortButton = cancel;
            dlg.Content = new Eto.Forms.StackLayout { Spacing = 8, Items = { new Eto.Forms.Label { Text = title }, box, new Eto.Forms.StackLayout { Orientation = Eto.Forms.Orientation.Horizontal, Spacing = 6, Items = { cancel, ok } } } };
            try { return dlg.ShowModal(); } catch { return null; }
        }

        public override void AppendToInputPanel(Grasshopper2.UI.InputPanel.InputPanel panel)
        {
            base.AppendToInputPanel(panel);
            using (panel.BeginCategory("Control Panel"))
            {
                panel.AddCheck("Every setting (Advanced)", Advanced, v => { Advanced = v; Document?.Modify(); }, "Off: the main settings of each component. On: every number, choice and switch.");
                panel.AddLabel("Click a setting on the panel to change it. A wire into an input wins over the panel.", false, null);
            }
        }
    }

    // ============================================================== Result
    /// <summary>
    /// Result: what the definition in its group gives, in one place and without wires: the verdict of the checks
    /// and a button for every file the group can save.
    /// </summary>
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d0102")]
    public sealed class ResultComponent : Component
    {
        public ResultComponent() : base(Io.Name("Result", "What the definition in this group gives, in one place and without wires: the verdict of the checks and a button for every file to save (or to send to the robot). Put it at the end of a group.", Io.Slicer, Group.Start)) { }
        public ResultComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("ResultIcon");
        protected override void AddInputs(InputAdder inputs) { }
        protected override void AddOutputs(OutputAdder outputs) { }
        protected override void Process(IDataAccess access) { }

        protected override IAttributes CreateAttributes() => new RowAttributes(this, "Result", 230);

        internal List<Row> BuildRows()
        {
            var rows = new List<Row>();
            var members = Scope.Members(this);
            var checks = new List<Row>();
            foreach (var c in members)
            {
                string n = c.Nomen.Name;
                if (n != "Path Check" && n != "Robot Program" && n != "Non-Planar Check" && n != "Multi-Axis Check" && n != "Self Support Check") continue;
                var ok = Scope.OutputOf(c, "OK");
                var report = Scope.OutputOf(c, "Report") as string;
                if (ok == null && report == null) continue;
                bool good = ok is bool b && b;
                string label = n == "Robot Program" ? "Robot" : n;
                string first = (report ?? "").Split('\n').FirstOrDefault() ?? "";
                checks.Add(new Row { Info = true, Text = good ? label + ": OK" : label + ": CHECK  " + first, Bad = !good });
            }
            if (checks.Count > 0) { rows.Add(new Row { Caption = true, Text = "Check" }); rows.AddRange(checks); }
            var buttons = new List<Row>();
            foreach (var c in members)
                foreach (var p in c.Parameters.Inputs)
                {
                    string pn = p.Nomen.Name;
                    if (!(p is Parameter<bool>) || (pn != "Save" && pn != "Upload")) continue;
                    buttons.Add(new Row { Button = true, Param = p, Text = (pn == "Upload" ? "Upload to robot" : "Save") + "  (" + c.Nomen.Name + ")" });
                }
            if (buttons.Count > 0) { rows.Add(new Row { Caption = true, Text = "Files" }); rows.AddRange(buttons); }
            if (rows.Count == 0) rows.Add(new Row { Info = true, Text = "Put it in a group with LARGER components." });
            return rows;
        }

        internal void Clicked(Row r)
        {
            if (!r.Button || r.Param == null) return;
            if (r.Param.Inputs.Count > 0) return; // a wire decides
            Scope.Put(r.Param, true); // the component writes the file and turns Save back off by itself
        }
    }

    // ============================================================== drawing
    internal sealed class Row
    {
        public string Text;
        public bool Caption, Info, Toggle, Button, Bad;
        public IParameter Param;
        public RectangleF Box;
    }

    /// <summary>
    /// A component drawn as a list of rows, in the colours and fonts of the Grasshopper 2 skin: captions,
    /// settings (label and value in a box) and buttons. A click on a row is passed to the component.
    /// </summary>
    internal sealed class RowAttributes : ComponentAttributes
    {
        private const float Pad = 6f, LineH = 16f, BoxH = 18f, Gap = 3f, Head = 22f;
        private readonly string _title;
        private readonly float _width;
        private List<Row> _rows = new List<Row>();

        public RowAttributes(Component owner, string title, float width) : base(owner)
        {
            _title = title; _width = width;
            Responder.MouseSingleClickHook += Click;
            owner.DocumentChanged += (s, e) => Hook();
            Hook();
        }

        private Document _hooked;
        private void Hook()
        {
            var doc = Owner.Document;
            if (doc == _hooked) return;
            if (_hooked != null) _hooked.Solution.SolutionCompleted -= Done;
            _hooked = doc;
            if (doc != null) doc.Solution.SolutionCompleted += Done;
        }

        private void Done(object sender, SolutionEventArgs e)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => { InvalidateLayout(); InvalidateDisplay(); }));
        }

        private List<Row> Build()
        {
            try
            {
                if (Owner is ControlPanelComponent cp) return cp.BuildRows();
                if (Owner is ResultComponent rc) return rc.BuildRows();
            }
            catch { }
            return new List<Row>();
        }

        protected override void LayoutCentralBox(Font font)
        {
            _rows = Build();
            float x = Pivot.X, y = Pivot.Y + Head;
            foreach (var r in _rows)
            {
                if (r.Caption) { y += 4f; r.Box = new RectangleF(x + Pad, y, _width - 2 * Pad, LineH); y += LineH; continue; }
                if (r.Info) { r.Box = new RectangleF(x + Pad, y, _width - 2 * Pad, LineH); y += LineH; continue; }
                r.Box = new RectangleF(x + Pad, y, _width - 2 * Pad, BoxH); y += BoxH + Gap;
            }
            CentralBox = new RectangleF(x, Pivot.Y, _width, y - Pivot.Y + Pad);
        }

        protected override void DrawContent(Grasshopper2.Eto.Drawing.Context context, Skin skin, Grasshopper2.UI.Primitives.Capsule capsule, Shade shade)
        {
            var g = context.Graphics;
            var text = shade.Text;
            var loud = skin.Shape.FontLoud; var word = skin.Shape.FontWord;
            var box = CentralBox;
            // title
            var t = g.MeasureString(loud, _title);
            g.DrawText(loud, text, box.X + (box.Width - t.Width) / 2, box.Y + (Head - t.Height) / 2, _title);
            g.DrawLine(new Pen(shade.Edge, 1f), box.X + Pad, box.Y + Head - 2, box.Right - Pad, box.Y + Head - 2);
            var cp = Owner as ControlPanelComponent;
            foreach (var r in _rows)
            {
                var b = r.Box;
                if (r.Caption)
                {
                    g.DrawText(loud, text, b.X, b.Y + 1, Fit(g, loud, r.Text, b.Width));
                    continue;
                }
                if (r.Info)
                {
                    g.DrawText(word, r.Bad ? Color.FromArgb(200, 60, 40) : text, b.X + 2, b.Y + 1, Fit(g, word, r.Text, b.Width - 2));
                    continue;
                }
                bool wired = r.Param != null && r.Param.Inputs.Count > 0;
                var fill = r.Button || r.Toggle ? Mix(shade.Slab, text, 0.75f) : Mix(shade.Slab, Colors.White, 0.35f);
                var ink = r.Button || r.Toggle ? shade.Slab : text;
                if (wired) { fill = Mix(shade.Slab, text, 0.12f); ink = Mix(text, shade.Slab, 0.45f); }
                using (var path = GraphicsPath.GetRoundRect(b, 3f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(new Pen(Mix(shade.Edge, shade.Slab, 0.3f), 1f), path);
                }
                string s;
                if (r.Button || r.Toggle) s = r.Text;
                else
                {
                    var v = Scope.ValueOf(r.Param);
                    s = Scope.NameOf(r.Param) + ": " + Scope.Show(r.Param, v) + (wired ? "  (wire)" : "");
                }
                s = Fit(g, word, s, b.Width - 8);
                var m = g.MeasureString(word, s);
                g.DrawText(word, ink, b.X + (b.Width - m.Width) / 2, b.Y + (b.Height - m.Height) / 2, s);
            }
        }

        private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.Rb + (b.Rb - a.Rb) * t), (int)(a.Gb + (b.Gb - a.Gb) * t), (int)(a.Bb + (b.Bb - a.Bb) * t), a.Ab);

        private static string Fit(Graphics g, Font f, string s, float width)
        {
            if (string.IsNullOrEmpty(s) || g.MeasureString(f, s).Width <= width) return s ?? "";
            while (s.Length > 1 && g.MeasureString(f, s + "...").Width > width) s = s.Substring(0, s.Length - 1);
            return s + "...";
        }

        private Response Click(ResponseMouseArgs e)
        {
            if (e.Buttons != Eto.Forms.MouseButtons.Primary) return Response.Ignored;
            var at = e.ContentLocation;
            var r = _rows.FirstOrDefault(x => !x.Caption && !x.Info && x.Box.Contains(at));
            if (r == null) return Response.Ignored;
            try
            {
                if (Owner is ControlPanelComponent cp) cp.Clicked(r);
                else if (Owner is ResultComponent rc) rc.Clicked(r);
            }
            catch (Exception ex) { Rhino.RhinoApp.WriteLine("LARGER.slicer: " + ex.Message); }
            InvalidateLayout(); InvalidateDisplay();
            return Response.Handled;
        }
    }
}
