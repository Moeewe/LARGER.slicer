using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== surface pattern
    [IoId("96f34d47-7f6b-4a02-80d3-e409c7ecec57")]
    public sealed class SurfacePatternComponent : Component
    {
        public SurfacePatternComponent() : base(Io.Name("Surface Pattern", "Textures in the wall: knit, pixels, ribs, waves, zigzag, dots, loops or a picture. Every layer is a row of points; they are pulled out of the wall in a rhythm that moves on from layer to layer. Use it after Continuous Path. The path stays one line.", Io.Slicer, Group.Path)) { }
        public SurfacePatternComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerPatternIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "Path from Continuous Path (any toolpath works).");
            inputs.AddInteger("Pattern", "P", "0 = Knit (pixels, rows alternate)\n1 = Ribs (the same every layer)\n2 = Waves (sine; drifts with Phase Shift)\n3 = Zigzag (sharp, the same every layer)\n4 = Dots (one bump per repeat, rows alternate)\n5 = Loops (long pulls that hang down: set Z Droop and Extra Flow)\n6 = Image (knit pixels where the picture is dark)").Set(0);
            inputs.AddNumber("Amplitude", "A", "How far the wall is pulled out at the peaks, in mm. Up to half the bead width every bead rests on the one below; more hangs over on purpose.").Set(3.0);
            inputs.AddNumber("Pitch", "Pi", "Length of one repeat along the layer, in mm. Fitted around closed walls.").Set(10.0);
            inputs.AddInteger("Layer Period", "Lp", "Layers printed with the same row before the pattern moves on.").Set(1);
            inputs.AddNumber("Phase Shift", "Ph", "How far each row moves along the wall, as a fraction of the pitch. 0 = stacked, 0.5 = woven, 0.05 = diagonal.").Set(0.0);
            inputs.AddNumber("Z Droop", "Dz", "The peaks are printed this much lower, in mm, so the bead sags into a loop.").Set(0.0);
            inputs.AddNumber("Extra Flow", "Fl", "More extrusion at the peaks, as a fraction (0.3 = 30 %).").Set(0.0);
            inputs.AddInteger("Side", "Sd", "0 = Outside (where the wall looks into the open). 1 = Inside (the hollow of a vase).").Set(0);
            inputs.AddGeneric("Region", "R", "Optional. Closed curves drawn on the part (unrolled with the wall), or Breps / meshes: the pattern only inside.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Falloff", "Fo", "Soft edge of a Region, in mm.").Set(10.0);
            inputs.AddGeneric("Attractors", "At", "Optional points or curves: the pattern is full at them and fades out at Reach.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Reach", "Re", "Attractors only: distance in mm at which the pattern has faded out.").Set(100.0);
            inputs.AddText("Image", "Img", "Optional. File path of a picture, wrapped once around the part: dark = full pattern.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Invert", "Inv", "True = light parts of the picture, and everything outside a Region, get the pattern.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Toolpath", "T", "The path with the pattern.");
            outputs.AddPolyline("Preview", "Pv", "One polyline per layer.", Access.Twig);
            outputs.AddPoint("Hanging", "H", "Points more than half a bead beside the layer below (the texture; Path Check lists them).", Access.Twig);
            outputs.AddText("Info", "I", "What was done.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            int kind, period, side; double amp, pitch, phase, droop, flow, falloff, reach; string file; bool invert;
            access.GetItem(1, out kind); access.GetItem(2, out amp); access.GetItem(3, out pitch); access.GetItem(4, out period);
            access.GetItem(5, out phase); access.GetItem(6, out droop); access.GetItem(7, out flow); access.GetItem(8, out side);
            access.GetItem(10, out falloff); access.GetItem(12, out reach); access.GetItem(13, out file); access.GetItem(14, out invert);
            object[] region, attr;
            access.GetItemArray(9, out region); access.GetItemArray(11, out attr);
            var o = new PatternOptions
            {
                Kind = (SurfacePatternKind)Math.Max(0, Math.Min(SurfacePattern.Names.Length - 1, kind)),
                Amplitude = amp, Pitch = pitch, LayerPeriod = period, PhaseShift = phase, Droop = Math.Max(0, droop), ExtraFlow = flow,
                Inside = side == 1, Falloff = falloff, Reach = reach, Invert = invert,
                Regions = (region ?? new object[0]).Where(g => g != null).Select(Io.Solid).ToList(),
                Attractors = (attr ?? new object[0]).Where(g => g != null).Select(g => g is Line l ? new LineCurve(l) : g).ToList(),
            };
            if (!string.IsNullOrWhiteSpace(file))
            {
                o.Image = LoadImage(file.Trim().Trim('"'), out string problem);
                if (o.Image == null) access.AddWarning("Image", problem);
            }
            var log = new List<string>();
            if (Io.Step(access, 10)) return; // progress; stop if the inputs changed meanwhile
            var r = SurfacePattern.Apply(tp, o, log);
            foreach (var l in log) access.AddWarning("Surface Pattern", l);
            access.SetItem(0, r.Path);
            access.SetTwig(1, r.Preview.ToArray(), null, null);
            access.SetTwig(2, r.Hanging.ToArray(), null, null);
            access.SetItem(3, r.Info);
        }

        private static GrayImage LoadImage(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) { problem = "File not found: " + path; return null; }
                using (var bmp = new Eto.Drawing.Bitmap(path))
                {
                    int step = Math.Max(1, (int)Math.Ceiling(Math.Max(bmp.Width, bmp.Height) / 600.0));
                    int w = Math.Max(1, bmp.Width / step), h = Math.Max(1, bmp.Height / step);
                    var v = new float[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = bmp.GetPixel(Math.Min(bmp.Width - 1, x * step), Math.Min(bmp.Height - 1, y * step));
                            double g = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                            v[y * w + x] = (float)(g * c.A + (1 - c.A));
                        }
                    return new GrayImage(w, h, v);
                }
            }
            catch (Exception e) { problem = "Could not read the picture (" + e.Message + ")."; return null; }
        }
    }
}
