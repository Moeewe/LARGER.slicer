using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;
using Grasshopper2.UI.Icon;
using GrasshopperIO;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Writes the name of every component above it on the canvas, as the Bifocals plug-in does in Grasshopper 1
    /// (Grasshopper 2 shows icons only, and Bifocals does not exist for it). Place one on a canvas; it labels that
    /// document while it is there and Show is true. Nothing is calculated: it only draws after the objects are painted.
    /// </summary>
    [IoId("4c9d7e12-6a35-4f80-b2c1-9e0a5d37f168")]
    public sealed class ComponentLabelsComponent : Component
    {
        public ComponentLabelsComponent() : base(Io.Name("Component Labels", "Writes the name of every component above it on the canvas, like Bifocals in Grasshopper 1. Place one anywhere on the canvas: every component of this document gets its name as a small label. Show switches the labels off without removing it; Plug-in adds the name of the plug-in the component comes from; Size sets the text size.", Io.Utilities, 0))
        { DocumentChanged += Moved; }
        public ComponentLabelsComponent(IReader reader) : base(reader) { DocumentChanged += Moved; }
        protected override IIcon IconInternal => Io.Icon("ComponentLabelsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddBoolean("Show", "Show", "True = the names are written above the components. False = hidden.", ("Show", "Names above the components"), ("Hide", "No names")).Set(true);
            inputs.AddBoolean("Plug-in", "Plug-in", "True = the plug-in a component comes from is written after its name, e.g. 'Slice · LARGER'.").Set(false);
            inputs.AddNumber("Size", "Size", "Text size of the labels (canvas units; the labels zoom with the canvas).").Set(8.0).Values(6, 7, 8, 9, 10, 12);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Info", "I", "How many components are labelled.");
        }

        // what the painter reads (set by Process, read on the UI thread while painting)
        private volatile bool _show = true, _plugin;
        private volatile float _size = 8f;

        protected override void Process(IDataAccess access)
        {
            access.GetItem(0, out bool show); access.GetItem(1, out bool plugin); access.GetItem(2, out double size);
            _show = show; _plugin = plugin; _size = (float)Math.Max(4, Math.Min(24, size));
            Hook();
            int n = Document?.Objects.ActiveObjects.OfType<Component>().Count(c => !ReferenceEquals(c, this)) ?? 0;
            access.SetItem(0, show ? n + " component(s) labelled." : "Labels are hidden.");
        }

        // ---- one painter for the canvas; the label components that live in documents register with it
        private static readonly List<WeakReference<ComponentLabelsComponent>> Live = new List<WeakReference<ComponentLabelsComponent>>();
        private static Canvas _hooked;

        private void Moved(object sender, ObjectDocumentEventArgs e)
        {
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out var c) || ReferenceEquals(c, this));
                if (e.NewDocument != null) Live.Add(new WeakReference<ComponentLabelsComponent>(this));
            }
            Hook();
        }

        private static void Hook()
        {
            try
            {
                var canvas = Editor.Instance?.Canvas;
                if (canvas == null || ReferenceEquals(canvas, _hooked)) return;
                if (_hooked != null) _hooked.AfterPaintObjects -= Paint;
                canvas.AfterPaintObjects += Paint;
                _hooked = canvas;
            }
            catch { }
        }

        private static void Paint(object sender, CanvasPaintEventArgs e)
        {
            try
            {
                var doc = e.Canvas?.Document;
                if (doc == null) return;
                ComponentLabelsComponent me = null;
                lock (Live)
                    foreach (var w in Live)
                        if (w.TryGetTarget(out var c) && ReferenceEquals(c.Document, doc) && c._show) { me = c; break; }
                if (me == null) return;
                var g = e.Graphics.Content;
                bool dark = ReferenceEquals(e.Skin, e.Canvas.SkinDim);
                Color ink = dark ? Color.FromArgb(225, 228, 235) : Color.FromArgb(45, 52, 66);
                Color ground = dark ? Color.FromArgb(30, 34, 42, 205) : Color.FromArgb(255, 255, 255, 215);
                using (var font = new Font(SystemFont.Default, me._size))
                using (var fill = new SolidBrush(ground))
                {
                    foreach (var c in doc.Objects.ActiveObjects.OfType<Component>())
                    {
                        if (ReferenceEquals(c, me)) continue;
                        string name = c.Nomen.Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (me._plugin) { string from = c.Nomen.Chapter; if (!string.IsNullOrEmpty(from)) name += " · " + from; }
                        RectangleF b = c.Attributes.Bounds;
                        SizeF t = g.MeasureString(font, name);
                        float padX = 3f, padY = 1f;
                        var box = new RectangleF(b.X + (b.Width - t.Width) / 2 - padX, b.Y - t.Height - 2 * padY - 3f, t.Width + 2 * padX, t.Height + 2 * padY);
                        using (var path = GraphicsPath.GetRoundRect(box, 3f)) g.FillPath(fill, path);
                        g.DrawText(font, ink, box.X + padX, box.Y + padY, name);
                    }
                }
            }
            catch { /* a label must never break the canvas */ }
        }
    }
}
