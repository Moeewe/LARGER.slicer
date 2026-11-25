using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using LARGERslicer.Components;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>The components a panel works on: those in its group (or, outside any group, the whole canvas), left to right.</summary>
    internal static class Scope
    {
        public static List<GH_Group> GroupsOf(IGH_DocumentObject o, GH_Document doc)
            => doc == null ? new List<GH_Group>() : doc.Objects.OfType<GH_Group>().Where(g => g.ObjectIDs.Contains(o.InstanceGuid)).ToList();

        /// <summary>Everything the panel looks at: its groups' members, or the whole canvas.</summary>
        public static List<IGH_DocumentObject> Pool(IGH_DocumentObject owner, GH_Document doc)
        {
            if (doc == null) return new List<IGH_DocumentObject>();
            var groups = GroupsOf(owner, doc);
            return (groups.Count > 0
                ? groups.SelectMany(g => g.ObjectIDs).Distinct().Select(id => doc.FindObject(id, true)).Where(x => x != null)
                : doc.Objects).ToList();
        }

        /// <summary>Plain Grasshopper inputs in the pool: sliders, value lists and toggles that feed something.</summary>
        public static List<IGH_DocumentObject> Plain(IGH_DocumentObject owner, GH_Document doc)
            => Pool(owner, doc).Where(o => (o is GH_NumberSlider || o is GH_ValueList || o is GH_BooleanToggle) && ((IGH_Param)o).Recipients.Count > 0)
                .OrderBy(o => o.Attributes?.Pivot.Y ?? 0).ThenBy(o => o.Attributes?.Pivot.X ?? 0).ToList();

        public static List<IGH_Component> Members(IGH_DocumentObject owner, GH_Document doc, out string signature)
        {
            signature = "";
            var list = new List<IGH_Component>();
            if (doc == null) return list;
            var groups = GroupsOf(owner, doc);
            IEnumerable<IGH_DocumentObject> pool = Pool(owner, doc);
            foreach (var o in pool)
                if (o is IGH_Component c && !ReferenceEquals(c, owner) && !(c is ControlPanelComponent) && !(c is ResultComponent)) list.Add(c);
            // in one group: left to right. Over the whole canvas: group by group in their order (a number at the
            // start of the group's name, as "01. PART", else top to bottom), then left to right within each
            if (groups.Count > 0) list = list.OrderBy(c => c.Attributes?.Pivot.X ?? 0).ThenBy(c => c.Attributes?.Pivot.Y ?? 0).ToList();
            else list = Order(list.Cast<IGH_DocumentObject>().ToList(), doc).Cast<IGH_Component>().ToList();
            signature = (groups.Count > 0 ? string.Join(",", groups.Select(g => g.ObjectIDs.Count)) : "doc" + doc.ObjectCount + "g" + doc.Objects.OfType<GH_Group>().Sum(g => g.ObjectIDs.Count)) + "|" + list.Count + "|" + Plain(owner, doc).Count
                        + "|" + string.Join(",", list.Select(c => c.InstanceGuid.ToString().Substring(0, 6) + c.Params.Input.Count(p => p.SourceCount > 0)));
            return list;
        }

        /// <summary>Over the whole canvas: group by group in their order (a number at the start of the group's name,
        /// as "01. PART", else top to bottom), then left to right within each group.</summary>
        public static List<IGH_DocumentObject> Order(List<IGH_DocumentObject> items, GH_Document doc)
        {
            // while a file is being read the document can hold groups whose members are not there yet
            var all = doc.Objects.OfType<GH_Group>().Where(x => x?.ObjectIDs != null).ToList();
            items = items.Where(c => c != null).ToList();
            Func<IGH_DocumentObject, double> key = c =>
            {
                var g = all.Where(x => x.ObjectIDs.Contains(c.InstanceGuid)).OrderByDescending(x => x.ObjectIDs.Count).FirstOrDefault();
                if (g == null) return 3e9;
                var m = System.Text.RegularExpressions.Regex.Match(g.NickName ?? "", @"^\s*(\d+)");
                if (m.Success) return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 1e6;
                var b = g.Attributes?.Bounds ?? RectangleF.Empty;
                return 1e9 + Math.Round(b.Top / 50.0) * 1e5 + b.Left;
            };
            return items.OrderBy(key).ThenBy(c => c.Attributes?.Pivot.X ?? 0).ThenBy(c => c.Attributes?.Pivot.Y ?? 0).ToList();
        }

        public static string GroupName(IGH_DocumentObject c, GH_Document doc)
        {
            var g = GroupsOf(c, doc).OrderByDescending(x => x.ObjectIDs.Count).FirstOrDefault();
            if (g == null || string.IsNullOrEmpty(g.NickName)) return "";
            int cut = g.NickName.IndexOf("  -", StringComparison.Ordinal);
            return cut > 0 ? g.NickName.Substring(0, cut) : g.NickName;
        }
    }

    /// <summary>
    /// Control Panel: the settings of every LARGER component in its group, in one place at the start of
    /// the definition, sorted from left to right as the definition runs. No wires: a value set here goes
    /// into the input of the component it belongs to, as if typed there; the components then show only a
    /// note instead of their own rows. "Advanced" shows every setting, otherwise the ones that matter most.
    /// Delete the panel and the components show their rows again; experts can set or wire every input
    /// directly as before.
    /// </summary>
    public class ControlPanelComponent : GH_Component, IOptionRows
    {
        public ControlPanelComponent()
            : base("Control Panel", "Controls",
                  "The settings of every LARGER component in this group in one place, sorted as the definition runs: put it at the start of a group (or on a canvas without groups) and set the print here. No wires: a value goes into the input it belongs to. The components then show a note instead of their own rows. 'Advanced' shows every setting. Delete the panel to work on the components directly.",
                  SlicerIO.Category, SlicerIO.SubCategory) { }

        public override Guid ComponentGuid => new Guid("8F2D6A1C-4B7E-4C3A-9E51-2A7D0C6B9F14");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => IconHelper.Load("ControlPanelIcon.png", this);
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { }

        /// <summary>The settings shown without "Advanced", by component name (others: their first two).</summary>
        private static readonly Dictionary<string, string[]> Essential = new Dictionary<string, string[]>
        {
            { "Machine Profile", new[] { "Machine" } },
            { "Print Settings", new[] { "Nozzle", "Bead Width", "Layer Height", "Speed", "Flow" } },
            { "Easy Slicer", new[] { "Bead Width", "Layer Height", "Speed", "Walls", "Bottom Layers", "Spiral", "Brim" } },
            { "Slice", new[] { "Angle", "Guide Mode" } },
            { "Walls", new[] { "Walls" } },
            { "Bottom Fill", new[] { "Bottom Layers", "Pattern" } },
            { "Infill Wave", new[] { "Shape", "Wavelength" } },
            { "Continuous Path", new[] { "Mode", "Seam Gap", "Min Layer Time" } },
            { "Machine Settings", new[] { "Bed Temperature", "Nozzle Temperature", "Cooling Fan" } },
            { "Machine", new[] { "Machine", "Extrusion Factor", "Bed Temperature", "Nozzle Temperature" } },
            { "Material Factor", new[] { "Target Width", "Current Factor" } },
            { "Weave", new[] { "Pattern", "Amplitude", "Wavelength", "Faces" } },
            { "Surface Pattern", new[] { "Pattern", "Amplitude", "Pitch", "Side" } },
            { "Shape Field", new[] { "Mode", "Min Factor", "Max Factor" } },
            { "Reshape Path", new[] { "Variant", "Material (0 = as the field)", "Min Layer", "Max Layer" } },
            { "Start and End", new[] { "Skirt Loops", "Brim" } },
            { "Path Check", new string[0] },
            { "Round Corners", new string[0] },
            { "Desktop Test Print", new[] { "Printer" } },
            { "Cell Geometry", new[] { "Cell" } },
            { "CNC - Relief", new[] { "Pattern", "Length", "Width", "Height" } },
            { "CNC - Plate", new[] { "Length (X)", "Width (Y)", "Thickness", "Material" } },
            { "CNC - Cutter", new[] { "Cutter", "Magazine" } },
            { "CNC - Own Cutter", new[] { "Shape", "Diameter" } },
            { "CNC - Rough and Finish", new[] { "Roughing", "Finishing", "Stock", "Tabs" } },
            { "Robot Simulation", new string[0] },
            { "Robot", new[] { "Robot" } },
            { "Robot Tool", new[] { "Preset" } },
            { "Robot Program", new[] { "Blend", "Turn" } },
            { "UR Cell", new[] { "Model", "Tool", "Plate" } },
            { "UR Program", new[] { "Speed", "Blend", "Turn" } },
            { "UR Simulation", new string[0] },
            { "Fan Schedule", new[] { "Off For", "Then", "Full After", "Full" } },
            { "Printer Simulation", new string[0] },
        };

        private bool _advanced;
        private HashSet<string> _open;      // over the whole canvas: the groups shown open (null = decided by size)
        private List<OptionRow> _rows;
        private string _signature;
        private readonly List<IGH_Component> _targets = new List<IGH_Component>();
        private readonly List<IGH_Component> _candidates = new List<IGH_Component>(); // every component the panel could show
        private readonly HashSet<Guid> _excluded = new HashSet<Guid>();                // the ones switched off in its menu

        /// <summary>The components whose settings the panel shows now (they carry a bar in its colour).</summary>
        internal IReadOnlyList<IGH_Component> Targets => _targets;

        public List<OptionRow> Rows
        {
            get
            {
                var doc = OnPingDocument();
                try
                {
                    var members = Scope.Members(this, doc, out string sig);
                    if (_rows != null && sig == _signature) return _rows;
                    _signature = sig;
                    Build(doc, members);
                    return _rows;
                }
                catch (Exception)
                {
                    // a document still being read (a file opening): build again at the next redraw
                    _rows = null; _signature = null;
                    return new List<OptionRow>();
                }
            }
        }

        /// <summary>True when the group changed since the rows were built (a component added, a wire made).</summary>
        internal bool Stale() { var doc = OnPingDocument(); Scope.Members(this, doc, out string sig); return sig != _signature; }

        private void Build(GH_Document doc, List<IGH_Component> members)
        {
            var old = ControlRegistry.Release(InstanceGuid);
            _rows = new List<OptionRow>();
            _targets.Clear();
            _sliderRows.Clear();
            _candidates.Clear();
            bool many = Scope.GroupsOf(this, doc).Count == 0;
            // sliders, value lists and toggles of the definition (any plug-in, e.g. the IGAToolkit), except the
            // simulation slider, which the Result shows
            var plain = Scope.Plain(this, doc).Where(o => !((IGH_Param)o).Recipients.Any(r => { var top = r.Attributes?.GetTopLevel?.DocObject; return top != null && (top.Name == "Robot Simulation" || top.Name == "Printer Simulation" || top.Name == "UR Simulation"); })).ToList();
            // everything in the order of the definition: over the whole canvas group by group (see Scope.Order)
            var items = new List<IGH_DocumentObject>(members); items.AddRange(plain);
            // over the whole canvas: a group with a Control Panel of its own belongs to that panel
            if (many) items = items.Where(o => !Scope.GroupsOf(o, doc).Any(g => g.ObjectIDs.Any(id => id != InstanceGuid && doc.FindObject(id, true) is ControlPanelComponent))).ToList();
            if (many) items = Scope.Order(items, doc);
            // over the whole canvas every group is a heading that opens and closes; a short panel opens all of them
            var byGroup = new List<KeyValuePair<string, List<OptionRow>>>();
            Func<IGH_DocumentObject, List<OptionRow>> bucket = o =>
            {
                string g = many ? Scope.GroupName(o, doc) : "";
                if (g.Length == 0 && many) g = "(not in a group)";
                var kv = byGroup.FirstOrDefault(x => x.Key == g);
                if (kv.Value == null) { kv = new KeyValuePair<string, List<OptionRow>>(g, new List<OptionRow>()); byGroup.Add(kv); }
                return kv.Value;
            };
            // sliders that feed a setting of a component the panel shows (made for an optimiser): shown in that
            // setting's place, not among the other inputs
            var inPlace = new HashSet<IGH_DocumentObject>(plain.Where(pl => ((IGH_Param)pl).Recipients.Any(rc =>
            {
                var top = rc.Attributes?.GetTopLevel?.DocObject as IGH_Component;
                return top != null && members.Contains(top) && !_excluded.Contains(top.InstanceGuid) && top is IOptionRows;
            })));
            bool plainCaption = false;
            foreach (var o in items)
            {
                if (o is IGH_Param && inPlace.Contains(o)) continue;
                if (o is IGH_Param)
                {
                    var pr = PlainRow(o); if (pr == null) continue;
                    var into0 = bucket(o);
                    if (!many && !plainCaption) { into0.Add(new OptionRow { Kind = RowKind.Label, Label = "Inputs", Caption = true }); plainCaption = true; }
                    into0.Add(pr);
                    continue;
                }
                var c = (IGH_Component)o;
                // LARGER components and any component that brings rows of its own (another plug-in)
                if (!(c is IOptionRows) && c.GetType().Assembly != typeof(ControlPanelComponent).Assembly) continue;
                List<OptionRow> theirs = new List<OptionRow>();
                if (c is IOptionRows src) { try { theirs = src.Rows; } catch { continue; } }
                var settings = theirs.Where(r => r.Input >= 0 && r.Input < c.Params.Input.Count && r.Kind != RowKind.Button && r.Kind != RowKind.Label).ToList();
                // every other single number, whole number or true/false typed into an input gets a plain row too
                // (temperatures, Min Layer Time, Flow ...): what a user changes in practice is all in the panel
                var have = new HashSet<int>(settings.Select(r => r.Input));
                for (int ii = 0; ii < c.Params.Input.Count; ii++)
                    if (!have.Contains(ii)) { var gr = GenericRow(c, ii); if (gr != null) settings.Add(gr); }
                // a choice or number the component keeps itself (Robot: which robot; Robot Tool: preset)
                settings.AddRange(theirs.Where(r => r.Input < 0 && !r.External && r.Get != null && r.Put != null && (r.Kind == RowKind.Choice || r.Kind == RowKind.Number)));
                if (settings.Count == 0) continue;
                _candidates.Add(c);
                if (_excluded.Contains(c.InstanceGuid)) continue; // switched off in the panel's menu: the component keeps its own rows
                // all of them belong to the panel (the component shows a note), only the important ones unless Advanced
                ControlRegistry.Set(InstanceGuid, c, settings.Where(r => r.Input >= 0).Select(r => r.Input));
                _targets.Add(c);
                string[] keep;
                // a component without a list of main settings shows its settings under Advanced only
                if (!Essential.TryGetValue(c.Name, out keep)) keep = new string[0];
                // a wired input is shown too, with the value the wire brings, and cannot be changed here
                var shown = settings.Where(r => _advanced || keep.Contains(r.Label)).ToList();
                if (shown.Count == 0) continue;
                var into = bucket(c);
                into.Add(new OptionRow { Kind = RowKind.Label, Label = c.NickName != c.Name && !string.IsNullOrEmpty(c.NickName) ? c.Name + " (" + c.NickName + ")" : c.Name, Caption = !many });
                foreach (var r in shown)
                {
                    if (r.Input < 0) { into.Add(OwnProxy(c, r)); continue; }
                    // fed by a slider (one made for an optimiser): the slider's row in its place
                    var pin = c.Params.Input[r.Input];
                    var feeder = pin.Sources.FirstOrDefault(feed => inPlace.Contains(feed));
                    if (feeder != null) { var fr = PlainRow(feeder); if (fr != null) into.Add(fr); continue; }
                    var row = Proxy(c, r);
                    if (row.MakeSlider != null) { var cc = c; int ix = r.Input; var rr = r; row.MakeSlider = () => SliderFor(cc, ix, rr, FreeSpot(), Scope.GroupsOf(this, OnPingDocument())); }
                    into.Add(row);
                    if (row.MakeSlider != null) _sliderRows.Add(row);
                }
            }
            byGroup = byGroup.Where(kv => kv.Value.Count > 0).ToList();
            if (!many) foreach (var kv in byGroup) _rows.AddRange(kv.Value);
            else
            {
                int total = byGroup.Sum(kv => kv.Value.Count + 1);
                if (_open == null) _open = total <= 30 ? new HashSet<string>(byGroup.Select(kv => kv.Key)) : new HashSet<string>();
                foreach (var kv in byGroup)
                {
                    string name = kv.Key;
                    _rows.Add(new OptionRow
                    {
                        Kind = RowKind.Toggle, Label = name, On = "\u25BE  " + name, Off = "\u25B8  " + name,
                        GetSetting = () => _open.Contains(name),
                        SetSetting = b => { if (b) _open.Add(name); else _open.Remove(name); _rows = null; _signature = null; Attributes?.ExpireLayout(); },
                    });
                    if (_open.Contains(name)) _rows.AddRange(kv.Value);
                }
            }
            if (_targets.Count == 0 && plain.Count == 0)
                _rows.Add(new OptionRow { Kind = RowKind.Label, Label = "No settings in this group." });
            _rows.Add(new OptionRow
            {
                Kind = RowKind.Toggle, Label = "Advanced", On = "Advanced: every setting", Off = "Advanced: the main settings",
                GetSetting = () => _advanced,
                SetSetting = b => { _advanced = b; _rows = null; _signature = null; Attributes?.ExpireLayout(); },
            });
            // components no longer shown, and the ones shown now, draw their rows anew
            foreach (var g in old) (doc?.FindObject(g, true) as IGH_Component)?.Attributes?.ExpireLayout();
            foreach (var c in _targets) c.Attributes?.ExpireLayout();
        }

        /// <summary>A row for an input without a row of its own: one number, whole number or true/false typed into it.</summary>
        private static OptionRow GenericRow(IGH_Component c, int i)
        {
            var p = c.Params.Input[i];
            if (p.SourceCount > 0) return null;
            if (p is Param_Boolean pb && pb.PersistentData.DataCount == 1)
                return new OptionRow { Kind = RowKind.Toggle, Label = p.Name, Input = i };
            double v;
            if (p is Param_Integer pi && pi.PersistentData.DataCount == 1) v = pi.PersistentData.get_FirstItem(true).Value;
            else if (p is Param_Number pn && pn.PersistentData.DataCount == 1) v = pn.PersistentData.get_FirstItem(true).Value;
            else return null;
            int dec = p is Param_Integer ? 0 : Math.Abs(v) >= 100 ? 0 : Math.Abs(v) >= 10 ? 1 : Math.Abs(v) >= 1 || v == 0 ? 2 : 3;
            return new OptionRow { Kind = RowKind.Number, Label = p.Name, Input = i, Decimals = dec, Default = v };
        }

        /// <summary>A row for a slider, value list or toggle on the canvas; the object keeps its value.</summary>
        private static OptionRow PlainRow(IGH_DocumentObject o)
        {
            string name = !string.IsNullOrEmpty(o.NickName) ? o.NickName : o.Name;
            if (o is GH_NumberSlider sl)
            {
                var sd = sl.Slider;
                return new OptionRow
                {
                    Kind = RowKind.Number, Label = name, Input = -1, External = true, Min = (double)sd.Minimum, Max = (double)sd.Maximum,
                    Decimals = sd.Type == Grasshopper.GUI.Base.GH_SliderAccuracy.Float ? sd.DecimalPlaces : 0, Default = (double)sl.CurrentValue,
                    Presets = SlicerComponent.QuickPicks((double)sd.Minimum, (double)sd.Maximum, (double)sl.CurrentValue, sd.Type == Grasshopper.GUI.Base.GH_SliderAccuracy.Float ? sd.DecimalPlaces : 0),
                    Get = () => (double)sl.CurrentValue,
                    Put = v => { sl.RecordUndoEvent("Change " + name); sl.SetSliderValue((decimal)Math.Max((double)sd.Minimum, Math.Min((double)sd.Maximum, v))); sl.ExpireSolution(true); },
                };
            }
            if (o is GH_ValueList vl)
            {
                if (vl.ListItems.Count == 0) return null;
                return new OptionRow
                {
                    Kind = RowKind.Choice, Label = name, Input = -1, External = true, Items = vl.ListItems.Select(i => i.Name).ToArray(),
                    Get = () => Math.Max(0, vl.ListItems.FindIndex(i => i.Selected)),
                    Put = v => { int k = (int)Math.Round(v); if (k < 0 || k >= vl.ListItems.Count) return; vl.RecordUndoEvent("Change " + name); vl.SelectItem(k); vl.ExpireSolution(true); },
                };
            }
            if (o is GH_BooleanToggle bt)
                return new OptionRow
                {
                    Kind = RowKind.Toggle, Label = name, Input = -1, External = true,
                    GetSetting = () => bt.Value,
                    SetSetting = b => { bt.RecordUndoEvent("Change " + name); bt.Value = b; bt.ExpireSolution(true); },
                };
            return null;
        }

        /// <summary>A row on the panel that reads and writes the input of the component it belongs to.</summary>
        private readonly List<OptionRow> _sliderRows = new List<OptionRow>();

        /// <summary>
        /// A real number slider in the input, left of its component and in its groups: an optimiser such as
        /// Galapagos changes sliders (its genes), not values in a panel. The panel then shows the slider; one undo.
        /// </summary>
        /// <summary>A free place for a new slider: in a column right of the panel, below the ones already there.</summary>
        private PointF FreeSpot()
        {
            var doc = OnPingDocument(); var b = Attributes.Bounds;
            float x = b.Right + 25f, y = b.Top;
            for (int k = 0; k < 200; k++, y += 24f)
            {
                var spot = new RectangleF(x - 4f, y - 2f, 230f, 24f);
                if (doc == null || !doc.Objects.Any(o => !(o is GH_Group) && !(o is Grasshopper.Kernel.Special.GH_Scribble) && !(o is Grasshopper.Kernel.Special.GH_Markup) && o.Attributes != null && o.Attributes.Bounds.IntersectsWith(spot))) break;
            }
            return new PointF(x, y);
        }

        internal static void SliderFor(IGH_Component c, int input, OptionRow r, PointF? at = null, List<GH_Group> groups = null)
        {
            var doc = c.OnPingDocument();
            var p = c.Params.Input[input];
            if (doc == null || p.SourceCount > 0) return;
            bool choice = r.Kind == RowKind.Choice, whole = choice || p is Param_Integer;
            double v = OptionAttributes.ValueOf(p);
            double min = choice ? 0 : r.HasRange ? r.Min : Math.Min(0, v);
            double max = choice ? r.Items.Length - 1 : r.HasRange ? r.Max : Math.Max(1, 2 * Math.Abs(v));
            var sl = new GH_NumberSlider();
            sl.CreateAttributes();
            sl.NickName = r.Label;
            sl.Slider.Type = whole ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
            sl.Slider.DecimalPlaces = whole ? 0 : Math.Max(0, Math.Min(6, r.Decimals));
            sl.Slider.Minimum = (decimal)min; sl.Slider.Maximum = (decimal)max;
            sl.SetSliderValue((decimal)Math.Max(min, Math.Min(max, v)));
            var pb = p.Attributes.Bounds;
            sl.Attributes.Pivot = at ?? new PointF(pb.Left - 230f, pb.Top + pb.Height / 2f - 10f);
            var undo = new Grasshopper.Kernel.Undo.GH_UndoRecord("Slider for " + r.Label);
            undo.AddAction(new Grasshopper.Kernel.Undo.Actions.GH_WireAction(p));
            doc.AddObject(sl, false);
            undo.AddAction(new Grasshopper.Kernel.Undo.Actions.GH_AddObjectAction(sl));
            // in the panel's groups (a panel over the whole canvas has none): there the panel finds it
            foreach (var g in groups ?? Scope.GroupsOf(c, doc)) g.AddObject(sl.InstanceGuid);
            p.AddSource(sl);
            doc.UndoServer.PushUndoRecord(undo);
            sl.ExpireSolution(true);
        }

        /// <summary>A row for a setting the component keeps itself: set it there, with undo, and solve again.</summary>
        private static OptionRow OwnProxy(IGH_Component c, OptionRow r) => new OptionRow
        {
            Kind = r.Kind, Label = r.Label, Input = -1, Items = r.Items, Presets = r.Presets, Decimals = r.Decimals, Unit = r.Unit,
            Min = r.Min, Max = r.Max, Step = r.Step, Default = r.Default, ZeroText = r.ZeroText, External = true,
            Get = r.Get,
            Put = v => { ((GH_Component)c).RecordUndoEvent("Change " + r.Label); r.Put(v); r.Picked?.Invoke((int)Math.Round(v)); c.ExpireSolution(true); },
        };

        private static OptionRow Proxy(IGH_Component c, OptionRow r)
        {
            var p = c.Params.Input[r.Input];
            int input = r.Input;
            var row = new OptionRow
            {
                Kind = r.Kind, Label = r.Label, Input = -1, Items = r.Items, Presets = r.Presets, Decimals = r.Decimals, Unit = r.Unit,
                Min = r.Min, Max = r.Max, Step = r.Step, Default = r.Default, ZeroText = r.ZeroText, On = r.On, Off = r.Off, External = true,
                Wired = () => p.SourceCount > 0,
                MakeSlider = r.Kind == RowKind.Number || (r.Kind == RowKind.Choice && r.Items != null && r.Items.Length > 1) ? () => SliderFor(c, input, r) : (Action)null,
            };
            if (r.Kind == RowKind.Toggle)
            {
                row.GetSetting = () => OptionAttributes.ValueOf(p) > 0.5;
                row.SetSetting = b => OptionAttributes.Set((GH_Component)c, input, new GH_Boolean(b), true);
            }
            else
            {
                row.Get = () => OptionAttributes.ValueOf(p);
                row.Put = v =>
                {
                    IGH_Goo goo = p is Param_Integer ? (IGH_Goo)new GH_Integer((int)Math.Round(v)) : new GH_Number(v);
                    OptionAttributes.Set((GH_Component)c, input, goo, true, () => r.Picked?.Invoke((int)Math.Round(v)));
                };
            }
            return row;
        }

        public override void CreateAttributes() { m_attributes = new PanelAttributes(this, "Control Panel", 250f); }

        /// <summary>The menu says which components the panel works on; each can be switched off (it then keeps its own rows).</summary>
        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            Menu_AppendSeparator(menu);
            var head = new System.Windows.Forms.ToolStripLabel("Settings shown here (click to switch off):") { ForeColor = Color.DimGray };
            menu.Items.Add(head);
            if (_candidates.Count == 0) Menu_AppendItem(menu, "(no LARGER component with settings in this group)", null, false);
            foreach (var c in _candidates.ToList())
            {
                var cc = c;
                string label = c.Name + (c.NickName != c.Name && !string.IsNullOrEmpty(c.NickName) ? " (" + c.NickName + ")" : "");
                string g = Scope.GroupsOf(this, OnPingDocument()).Count == 0 ? Scope.GroupName(c, OnPingDocument()) : "";
                Menu_AppendItem(menu, label + (g.Length > 0 ? "  -  " + g : ""), (s, e) =>
                {
                    RecordUndoEvent("Control Panel: " + cc.Name);
                    if (!_excluded.Remove(cc.InstanceGuid)) _excluded.Add(cc.InstanceGuid);
                    _rows = null; _signature = null; Attributes?.ExpireLayout(); cc.Attributes?.ExpireLayout();
                    Grasshopper.Instances.RedrawCanvas();
                }, true, !_excluded.Contains(cc.InstanceGuid));
            }
            var make = _sliderRows.Where(r => r.MakeSlider != null && !(r.Wired?.Invoke() ?? false)).ToList();
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Sliders for an optimiser (Galapagos): the " + make.Count + " setting(s) shown", (s, e) => { foreach (var r in make) r.MakeSlider(); }, make.Count > 0)
                .ToolTipText = "Puts a number slider into every setting the panel shows now (Advanced: every setting). Galapagos can then change them as genes. Delete a slider and the panel sets the value again.";
            Menu_AppendItem(menu, "Show every component again", (s, e) => { RecordUndoEvent("Control Panel"); _excluded.Clear(); _rows = null; _signature = null; Attributes?.ExpireLayout(); Grasshopper.Instances.RedrawCanvas(); }, _excluded.Count > 0);
        }

        // Names in full on the canvas: what "Bead W" or "Lh" mean is otherwise hard to guess
        private static bool _fullNames;
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (!_fullNames) { _fullNames = true; try { if (!Grasshopper.CentralSettings.CanvasFullNames) { Grasshopper.CentralSettings.CanvasFullNames = true; Grasshopper.Instances.RedrawCanvas(); } } catch { } }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            foreach (var g in ControlRegistry.Release(InstanceGuid)) (document?.FindObject(g, true) as IGH_Component)?.Attributes?.ExpireLayout();
            base.RemovedFromDocument(document);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("LARGER_Advanced", _advanced);
            if (_excluded.Count > 0) writer.SetString("LARGER_Excluded", string.Join("\n", _excluded));
            if (_open != null) writer.SetString("LARGER_Open", string.Join("\n", _open));
            return base.Write(writer);
        }
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool a = false; if (reader.TryGetBoolean("LARGER_Advanced", ref a)) _advanced = a;
            string ex = null; if (reader.TryGetString("LARGER_Excluded", ref ex)) { _excluded.Clear(); foreach (var t in ex.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) if (Guid.TryParse(t, out var g)) _excluded.Add(g); }
            string open = null; if (reader.TryGetString("LARGER_Open", ref open)) _open = new HashSet<string>(open.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            _rows = null; _signature = null;
            return base.Read(reader);
        }
    }

    /// <summary>
    /// Result: what a user of the definition wants to know and do at the end, without wires. It reads the
    /// components of its group: the verdict of the checks, layers, time and material, the file to save, the
    /// simulation slider and Play.
    /// </summary>
    public class ResultComponent : GH_Component, IOptionRows
    {
        public ResultComponent()
            : base("Result", "Result",
                  "What the definition in this group gives, in one place and without wires: the verdict of the checks, layers, print time and material, a button to save the file for the machine, and the simulation. Put it at the end of a group.",
                  SlicerIO.Category, SlicerIO.SubCategory) { }

        public override Guid ComponentGuid => new Guid("3B9E5D27-7C14-4F8A-A6D2-91C0E4B37A58");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => IconHelper.Load("ResultIcon.png", this);
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { }

        private List<OptionRow> _rows;
        private string _signature;

        public List<OptionRow> Rows
        {
            get
            {
                var doc = OnPingDocument();
                try
                {
                    var members = Scope.Members(this, doc, out string sig);
                    if (_rows != null && sig == _signature) return _rows;
                    _signature = sig;
                    _rows = Build(members);
                    return _rows;
                }
                catch (Exception)
                {
                    _rows = null; _signature = null;
                    return new List<OptionRow>();
                }
            }
        }

        internal bool Stale() { var doc = OnPingDocument(); Scope.Members(this, doc, out string sig); return sig != _signature; }

        private static IEnumerable<string> Texts(IGH_Component c, string output)
        {
            var o = c.Params.Output.FirstOrDefault(x => x.Name == output);
            return o == null ? Enumerable.Empty<string>() : o.VolatileData.AllData(true).Select(g => g?.ToString() ?? "");
        }

        private static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int k = s.IndexOf(": ", StringComparison.Ordinal);
            int dot = s.IndexOf(". ", StringComparison.Ordinal);
            if (dot > 0) s = s.Substring(0, dot);
            return s;
        }

        private List<OptionRow> Build(List<IGH_Component> members)
        {
            var rows = new List<OptionRow>();
            Func<Func<string>, OptionRow> line = f => new OptionRow { Kind = RowKind.Label, TextOf = f };
            Action<string> caption = t => rows.Add(new OptionRow { Kind = RowKind.Label, Label = t, Caption = true });

            // ---- checks
            var checks = members.Where(c => c.Name == "Path Check" || c.Name.EndsWith("Health Check") || c.Name == "Non-Planar Check" || c.Name == "Multi-Axis Check").ToList();
            var robots = members.Where(c => c.Name == "Robot Program" || c.Name == "UR Program").ToList();
            if (checks.Count > 0 || robots.Count > 0) caption("Check");
            foreach (var c in checks)
            {
                var cc = c;
                rows.Add(line(() =>
                {
                    var ok = cc.Params.Output.FirstOrDefault(x => x.Name == "OK")?.VolatileData.AllData(true).FirstOrDefault();
                    string verdict = ok is GH_Boolean gb ? (gb.Value ? "OK" : "NOT OK") : Texts(cc, "Verdict").FirstOrDefault() ?? "";
                    if (cc.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error) verdict = "ERROR";
                    return cc.Name + ": " + (verdict.Length > 0 ? verdict : "waiting");
                }));
                if (c.Name == "Path Check")
                    rows.Add(line(() =>
                    {
                        var rep = Texts(cc, "Report").ToList();
                        string d = rep.FirstOrDefault(t => t.StartsWith("Double material")) ?? "";
                        var m = System.Text.RegularExpressions.Regex.Match(d, @"at (\d+) place.*?about ([\d.,]+) cm3");
                        string dbl = rep.Any(t => t.StartsWith("No double")) ? "no double material" : m.Success ? "double material " + m.Groups[2].Value + " cm3 at " + m.Groups[1].Value + " places" : "";
                        string tr = rep.Any(t => t.StartsWith("TRAVEL")) ? ", TRAVEL THROUGH THE PRINT" : "";
                        return dbl.Length + tr.Length > 0 ? "  " + dbl + tr : "";
                    }));
            }
            foreach (var c in robots)
            {
                var cc = c;
                rows.Add(line(() =>
                {
                    var rep = Texts(cc, "Report").ToList();
                    if (rep.Count == 0) return "Robot: calculating";
                    if (cc.Name == "UR Program")
                    {
                        var okp = cc.Params.Output.FirstOrDefault(x => x.Name == "OK")?.VolatileData.AllData(true).FirstOrDefault();
                        return "UR: " + (okp is GH_Boolean ub && ub.Value ? "reach OK" : "CHECK the report (reach or payload)");
                    }
                    Func<string, string> head = t => { int k = t.IndexOf(':'); return k > 0 ? t.Substring(0, k) : t; };
                    string reach = rep.FirstOrDefault(t => t.StartsWith("Reach") || t.StartsWith("UNREACHABLE") || t.StartsWith("Cannot")) ?? "";
                    string coll = rep.FirstOrDefault(t => t.StartsWith("No collision") || t.StartsWith("COLLISION") || t.StartsWith("Collision:")) ?? "";
                    return "Robot: " + string.Join(", ", new[] { head(reach), head(coll) }.Where(x => x.Length > 0));
                }));
            }

            // ---- the print in numbers: layers, length, time and material (from the file name the outputs give)
            var path = members.FirstOrDefault(c => c.Name == "Continuous Path") ?? members.FirstOrDefault(c => c.Name == "Easy Slicer");
            var slice = members.FirstOrDefault(c => c.Name == "Slice");
            var named = members.Where(c => c.Params.Output.Any(o => o.Name == "File Name")).ToList();
            if (path != null || slice != null || named.Count > 0) caption("Print");
            if (slice != null) { var s0 = slice; rows.Add(line(() => FirstSentence(Texts(s0, "Info").FirstOrDefault() ?? ""))); }
            if (path != null) { var p0 = path; rows.Add(line(() => FirstSentence(Texts(p0, "Info").FirstOrDefault() ?? ""))); }
            foreach (var c in named)
            {
                var cc = c;
                rows.Add(line(() =>
                {
                    string fn = Texts(cc, "File Name").FirstOrDefault() ?? "";
                    var m = System.Text.RegularExpressions.Regex.Match(fn, @"_(\d+)g_(\d+)min");
                    if (!m.Success) return fn.Length > 0 ? "File: " + fn : "";
                    double g = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    return Inv.F($"{cc.Name}: {min / 60:0.0} h, {g / 1000:0.0} kg");
                }));
            }

            // ---- files and simulation: the buttons and the slider of the components themselves
            var buttons = new List<OptionRow>();
            foreach (var c in members)
            {
                if (!(c is IOptionRows src) || !(c.Attributes is OptionAttributes oa)) continue;
                List<OptionRow> theirs; try { theirs = src.Rows; } catch { continue; }
                foreach (var b in theirs.Where(r => r.Kind == RowKind.Button && r.Label.StartsWith("Save", StringComparison.OrdinalIgnoreCase)))
                {
                    var bb = b; var cc = c;
                    buttons.Add(new OptionRow { Kind = RowKind.Button, Label = b.Label + "  (" + c.Name + ")", Click = _ => (cc.Attributes as OptionAttributes)?.PressRow(bb) });
                }
            }
            var sims = members.Where(c => c.Name == "Robot Simulation" || c.Name == "Printer Simulation" || c.Name == "UR Simulation").ToList();
            if (buttons.Count > 0 || sims.Count > 0) caption("File and simulation");
            rows.AddRange(buttons);
            foreach (var c in sims)
            {
                var time = c.Params.Input.Count > 1 ? c.Params.Input[c.Name == "Printer Simulation" ? 2 : 1] : null;
                var slider = time?.Sources.Select(s => s.Attributes.GetTopLevel.DocObject).OfType<GH_NumberSlider>().FirstOrDefault();
                if (slider != null)
                {
                    var sl = slider;
                    rows.Add(new OptionRow
                    {
                        Kind = RowKind.Number, Label = "Simulation", Input = -1, External = true, Min = (double)sl.Slider.Minimum, Max = (double)sl.Slider.Maximum,
                        Decimals = Math.Max(3, sl.Slider.DecimalPlaces), Default = 0,
                        Presets = new[] { 0, 0.001, 0.01, 0.25, 0.5, 0.75, 1.0 },
                        Get = () => (double)sl.CurrentValue,
                        Put = v => { sl.RecordUndoEvent("Simulation"); sl.SetSliderValue((decimal)v); sl.ExpireSolution(true); },
                    });
                }
                if (c is IOptionRows sr)
                {
                    var play = sr.Rows.FirstOrDefault(r => r.Kind == RowKind.Toggle && r.Label == "Play");
                    if (play != null) rows.Add(ProxyToggle(c, play));
                }
            }
            if (rows.Count == 0) rows.Add(new OptionRow { Kind = RowKind.Label, Label = "No checks or outputs in this group yet." });
            return rows;
        }

        private static OptionRow ProxyToggle(IGH_Component c, OptionRow r)
        {
            var p = c.Params.Input[r.Input]; int input = r.Input;
            return new OptionRow
            {
                Kind = RowKind.Toggle, Label = r.Label, On = r.On, Off = r.Off, Input = -1, External = true,
                GetSetting = () => OptionAttributes.ValueOf(p) > 0.5,
                SetSetting = b => OptionAttributes.Set((GH_Component)c, input, new GH_Boolean(b), true),
            };
        }

        public override void CreateAttributes() { m_attributes = new PanelAttributes(this, "Result", 270f); }

        // the texts change with every solution of the group: draw them anew
        public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); document.SolutionEnd += Refresh; }
        public override void RemovedFromDocument(GH_Document document) { document.SolutionEnd -= Refresh; base.RemovedFromDocument(document); }
        private void Refresh(object sender, GH_SolutionEventArgs e) { Attributes?.ExpireLayout(); OnDisplayExpired(false); }
    }

    /// <summary>A panel drawn as rows only (captions, lines, settings, buttons) with its name underneath.</summary>
    internal sealed class PanelAttributes : OptionAttributes
    {
        private const float Header = 18f, Line = 15f;
        private readonly string _title; private readonly float _width;
        public PanelAttributes(IGH_Component owner, string title, float width) : base(owner) { _title = title; _width = width; }

        /// <summary>Selected, the panel shows what it sets in Grasshopper's own colour for selected objects.</summary>
        private static Color Mark => Color.FromArgb(70, 150, 40);

        protected override void Layout()
        {
            var rows = Rows;
            _rects.Clear();
            float x = Pivot.X, y = Pivot.Y + Header + 5f;
            foreach (var r in rows)
            {
                if (r.Kind == RowKind.Label)
                {
                    if (r.Caption && _rects.Count > 0) y += 4f;
                    // an empty information line takes no room
                    string t = r.TextOf != null ? SafeLine(r.TextOf) : r.Label;
                    if (string.IsNullOrEmpty(t)) { _rects.Add(RectangleF.Empty); continue; }
                    _rects.Add(new RectangleF(x + 6f, y, _width - 12f, Line)); y += Line;
                    continue;
                }
                _rects.Add(new RectangleF(x + 5f, y, _width - 10f, RowHeight)); y += RowHeight + Gap;
            }
            Bounds = new RectangleF(x, Pivot.Y, _width, y - Pivot.Y + 4f);
            m_innerBounds = Bounds;
            // the panel grew over the Result below it (a component was added): the Result moves down
            if (Owner is ControlPanelComponent)
                foreach (var res in Owner.OnPingDocument()?.Objects.OfType<ResultComponent>() ?? Enumerable.Empty<ResultComponent>())
                {
                    var ra = res.Attributes; if (ra == null) continue;
                    var rb = ra.Bounds;
                    if (rb.Top > Bounds.Top && rb.Top < Bounds.Bottom + 8f && rb.Right > Bounds.Left && rb.Left < Bounds.Right)
                    {
                        ra.Pivot = new PointF(ra.Pivot.X, ra.Pivot.Y + (Bounds.Bottom + 10f - rb.Top));
                        ra.ExpireLayout();
                    }
                }
        }

        private static string SafeLine(Func<string> f) { try { return f(); } catch { return ""; } }

        /// <summary>The height of the first input the panel sets on a component (else the middle of the component).</summary>
        private float FirstMarked(IGH_Component c, RectangleF b)
        {
            foreach (var p in c.Params.Input)
                if (p.SourceCount == 0 && c is IOptionRows src && src.Rows.Any(r => r.Input == c.Params.IndexOfInputParam(p.Name) && ControlRegistry.Controls(c, r)))
                    return p.Attributes.Bounds.Y + p.Attributes.Bounds.Height / 2f;
            return b.Y + b.Height / 2f;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            // selected: a thin dashed line to every component the panel sets and a frame around it
            if (channel == GH_CanvasChannel.Overlay && Selected && Owner is ControlPanelComponent cp0)
            {
                var old = graphics.SmoothingMode; graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(170, Mark), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var frame = new Pen(Color.FromArgb(200, Mark), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    foreach (var c in cp0.Targets)
                    {
                        var b = c.Attributes?.Bounds ?? RectangleF.Empty;
                        if (b.IsEmpty) continue;
                        b.Inflate(5f, 5f);
                        using (var path = GH_CapsuleRenderEngine.CreateRoundedRectangle(b, 6f)) graphics.DrawPath(frame, path);
                        // a curve as Grasshopper draws its wires: out of the panel to the right, into the
                        // component from the left, at the height of the first input the panel sets
                        var to = new PointF(b.Left, FirstMarked(c, b));
                        var from = new PointF(Bounds.Right, Bounds.Top + Header / 2f);
                        using (var wire = GH_Painter.ConnectionPath(from, to, GH_WireDirection.right, GH_WireDirection.left)) graphics.DrawPath(pen, wire);
                    }
                graphics.SmoothingMode = old;
                return;
            }
            if (channel != GH_CanvasChannel.Objects) return;
            // the group changed (a component added or wired): build the rows anew
            if ((Owner is ControlPanelComponent cp && cp.Stale()) || (Owner is ResultComponent rc && rc.Stale())) { ExpireLayout(); PerformLayout(); }
            using (var cap = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal, 4, 0))
                cap.Render(graphics, Selected, Owner.Locked, false);
            // the name in a dark capsule, as Grasshopper writes the message of a component (drag the panel
            // by it, or by any heading or line). A nickname of its own is shown with it.
            string name = Owner.NickName != Owner.Name && !string.IsNullOrEmpty(Owner.NickName) ? _title + "  \u2022  " + Owner.NickName : _title;
            var head = new RectangleF(Bounds.X + 4f, Bounds.Y + 3f, Bounds.Width - 8f, Header - 2f);
            using (var cap = GH_Capsule.CreateTextCapsule(head, head, GH_Palette.Black, name, GH_FontServer.StandardBold, 3, 0))
                cap.Render(graphics, Selected, Owner.Locked, false);
            RenderRows(graphics);
        }
    }
}
