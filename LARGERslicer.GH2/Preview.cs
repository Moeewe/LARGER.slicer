using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Display;
using Grasshopper2.Parameters;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Display;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// What a component draws itself in the viewport, as the Grasshopper 1 twins do in
    /// DrawViewportMeshes / DrawViewportWires: shaded meshes in their own colours (machines, robots,
    /// the bed plate) and coloured lines of a given thickness (DXR Variable Width). Process fills a
    /// Frame and hands it over with Show; drawing happens on the UI thread and only reads the last
    /// finished frame, so a solution that is still running never draws half a picture.
    /// </summary>
    internal sealed class ShadedPreview
    {
        public sealed class Frame
        {
            internal readonly List<KeyValuePair<Mesh, DisplayMaterial>> Meshes = new List<KeyValuePair<Mesh, DisplayMaterial>>();
            internal readonly List<Tuple<Polyline, Color, int>> Lines = new List<Tuple<Polyline, Color, int>>();
            internal readonly List<Tuple<Line, Color>> Arrows = new List<Tuple<Line, Color>>();
            internal BoundingBox Box = BoundingBox.Empty;

            public void Mesh(Mesh m, Color c, double transparency = 0)
            {
                if (m == null) return;
                Meshes.Add(new KeyValuePair<Mesh, DisplayMaterial>(m, MachineLook.Material(c, transparency)));
                Box.Union(m.GetBoundingBox(false));
            }
            public void Hit(Mesh m) => Mesh(m, MachineLook.Hit);
            public void Line(Line l, Color c, int thickness = 1) => Polyline(new Polyline(new[] { l.From, l.To }), c, thickness);
            public void Polyline(Polyline p, Color c, int thickness = 1)
            {
                if (p == null || p.Count < 2) return;
                Lines.Add(Tuple.Create(p, c, Math.Max(1, thickness)));
                Box.Union(p.BoundingBox);
            }
            public void Arrow(Line l, Color c) { Arrows.Add(Tuple.Create(l, c)); Box.Union(l.BoundingBox); }
            public bool Empty => Meshes.Count == 0 && Lines.Count == 0 && Arrows.Count == 0;
        }

        private Frame _shown = new Frame();
        public void Show(Frame f) { _shown = f ?? new Frame(); }
        public void Clear() { _shown = new Frame(); }
        public BoundingBox Box => _shown.Box;
        public bool HasMeshes => _shown.Meshes.Count > 0;

        public void Faces(DisplayPipeline pipeline, ref BoundingBox extents)
        {
            var f = _shown;
            if (f.Meshes.Count == 0) return;
            foreach (var kv in f.Meshes) pipeline.DrawMeshShaded(kv.Key, kv.Value);
            extents.Union(f.Box);
        }

        public void Wires(DisplayPipeline pipeline, ref BoundingBox extents)
        {
            var f = _shown;
            if (f.Lines.Count == 0 && f.Arrows.Count == 0) return;
            foreach (var l in f.Lines) pipeline.DrawPolyline(l.Item1, l.Item2, l.Item3);
            foreach (var a in f.Arrows) pipeline.DrawArrow(a.Item1, a.Item2);
            extents.Union(f.Box);
        }
    }

    /// <summary>
    /// A component that draws its own picture (ShadedPreview) besides, or instead of, the standard
    /// preview of its outputs. OwnOutputs are the outputs the own picture stands for: Grasshopper 2
    /// does not draw those again in its plain grey (the data stays on the output).
    /// </summary>
    public abstract class PreviewComponent : Component
    {
        internal readonly ShadedPreview Picture = new ShadedPreview();

        protected PreviewComponent(Grasshopper2.UI.Nomen nomen) : base(nomen) { }
        protected PreviewComponent(IReader reader) : base(reader) { }

        /// <summary>Indices of outputs drawn by the own picture.</summary>
        protected virtual int[] OwnOutputs => new int[0];

        private IEnumerable<IParameter> Others
        {
            get
            {
                var own = new HashSet<IParameter>();
                foreach (int i in OwnOutputs) if (i >= 0 && i < Parameters.OutputCount) own.Add(Parameters.Output(i));
                return DisplayParameters.Where(p => !own.Contains(p));
            }
        }

        public override BoundingBox DisplayBounds()
        {
            var b = BoundingBox.Empty;
            foreach (var p in Others) b.Union(p.DisplayBounds());
            b.Union(Picture.Box);
            return b;
        }

        public override void DisplayFaces(DisplayPipeline pipeline, Guises guises, ref BoundingBox extents)
        {
            foreach (var p in Others) p.DisplayFaces(pipeline, guises, ref extents);
            Picture.Faces(pipeline, ref extents);
        }

        public override void DisplayWires(DisplayPipeline pipeline, Guises guises, ref BoundingBox extents)
        {
            foreach (var p in Others) p.DisplayWires(pipeline, guises, ref extents);
            Picture.Wires(pipeline, ref extents);
        }
    }
}
