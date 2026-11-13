using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    // ============================================================== 4c Surface Pattern
    /// <summary>
    /// Textures in the wall: knit, pixels, ribs, waves, loops, a picture. Every layer is a row of
    /// points; the points are pulled out of the wall in a rhythm, and the rhythm moves on from
    /// layer to layer. Works on the toolpath, after Continuous Path and before Start and End.
    /// </summary>
    public class SurfacePatternComponent : SlicerComponent
    {
        public SurfacePatternComponent()
            : base("Surface Pattern", "Pattern",
                  "Textures in the wall: knit, pixels, ribs, waves, zigzag, dots, loops or a picture. Every layer is a row of points; they are pulled out of the wall along its normal in a rhythm, and the rhythm moves on from layer to layer. Small pulls give a fine knit, large ones let the bead hang in loops. Use it after Continuous Path and before Start and End. The path stays one line: only the walls move, seam ramps, bridges and infill stay, and the pattern fades out at the seam.") { }

        public override Guid ComponentGuid => new Guid("6147C32D-8EEB-4731-B299-7F3B95F01FC4");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPatternIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path from Continuous Path (any toolpath works)", GH_ParamAccess.item);
            p.AddIntegerParameter("Pattern", "P", "0 = Knit: pixels; every point out and back, rows alternate by half a repeat\n1 = Ribs: vertical ribs, the same every layer\n2 = Waves: a soft sine; with Phase Shift it runs diagonally or weaves\n3 = Zigzag: sharp, the same every layer\n4 = Dots: one bump per repeat, rows alternate\n5 = Loops: long pulls that hang down (set Z Droop and Extra Flow)\n6 = Image: knit pixels where the picture is dark", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Amplitude", "A", "How far the wall is pulled out at the peaks, in mm. Up to half the bead width every bead rests on the one below; more makes pixels and loops hang over (on purpose)", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Pitch", "Pi", "Length of one repeat along the layer, in mm (one out and one back). Around closed walls it is fitted so a whole number of repeats goes round", GH_ParamAccess.item, 10.0);
            p.AddIntegerParameter("Layer Period", "Lp", "Layers printed with the same row before the pattern moves on. 1 = every layer; 2 or 3 = taller pixels", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Phase Shift", "Ph", "How far each row moves along the wall, as a fraction of the pitch. 0 = stacked (ribs), 0.5 = every other row in between (woven), 0.05 = slow diagonal", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Z Droop", "Dz", "The peaks are also printed this much lower, in mm, so the bead sags into a loop. 0 = flat layers. About one layer height for loops", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Extra Flow", "Fl", "More extrusion at the peaks, as a fraction: 0.3 = 30 % more at a full peak. The longer path is already extruded by its length; this is on top, for loops that should be full", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Side", "Sd", "Which face gets the pattern:\n0 = Outside (default): where the wall looks into the open\n1 = Inside: the hollow of a vase or the face of a hole\nA free-standing open wall is open on both faces: Outside and Inside then pick one each - check in Preview", GH_ParamAccess.item, 0);
            p.AddGeometryParameter("Region", "R", "Optional. Where the pattern goes: closed curves drawn on (or in front of) the part, or Breps / meshes. A curve is unrolled with the wall (arc length across, height up) and a path point gets the pattern if it lies inside it there. A closed Brep or mesh: points inside it. An open surface lying on the wall: points on it", GH_ParamAccess.list);
            p.AddNumberParameter("Falloff", "Fo", "Soft edge of a Region, in mm: the pattern grows from nothing at the edge to full over this distance. 0 = hard edge", GH_ParamAccess.item, 10.0);
            p.AddGeometryParameter("Attractors", "At", "Optional points or curves: the pattern is full at them and fades out at Reach", GH_ParamAccess.list);
            p.AddNumberParameter("Reach", "Re", "Attractors only: distance in mm at which the pattern has faded out", GH_ParamAccess.item, 100.0);
            p.AddTextParameter("Image", "Img", "Optional. File path of a picture (PNG, JPG); a bare file name is looked for next to the Grasshopper file. It is wrapped once around the part, from bottom to top: dark = full pattern, white = smooth wall. Works with every pattern; Image (6) prints it as knit pixels", GH_ParamAccess.item);
            p.AddBooleanParameter("Invert", "Inv", "True = light parts of the picture get the pattern instead of dark ones, and everything outside a Region instead of inside", GH_ParamAccess.item, false);
            p[9].Optional = true; p[11].Optional = true; p[13].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 1, "Pattern", SurfacePattern.Names);
            AddSlider(doc, 2, "Amplitude", 0.5m, 30m, 3m, 1, "mm");
            AddSlider(doc, 3, "Pitch", 2m, 60m, 10m, 1, "mm");
            AddSlider(doc, 5, "Phase Shift", 0m, 1m, 0m, 2, "", new double[] { 0, 0.05, 0.1, 0.25, 0.5 });
            AddValueList(doc, 8, "Side", "Outside (looks into the open)", "Inside (the hollow of a vase)");
            AddToggle(doc, 14, "Invert", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The path with the pattern", GH_ParamAccess.item);
            p.AddCurveParameter("Preview", "Pv", "One polyline per layer of the new path (pipe or thicken them to see the texture)", GH_ParamAccess.list);
            p.AddPointParameter("Hanging", "H", "Points that stand more than half a bead beside the layer below. For pixels and loops that is the texture itself; Path Check lists them as unsupported", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "What was done", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Toolpath output of Continuous Path."); return; }
            var o = new PatternOptions();
            int kind = 0, period = 1, side = 0; double amp = 3, pitch = 10, phase = 0, droop = 0, flow = 0, falloff = 10, reach = 100; string file = null; bool invert = false;
            da.GetData(1, ref kind); da.GetData(2, ref amp); da.GetData(3, ref pitch); da.GetData(4, ref period); da.GetData(5, ref phase);
            da.GetData(6, ref droop); da.GetData(7, ref flow); da.GetData(8, ref side); da.GetData(10, ref falloff); da.GetData(12, ref reach);
            da.GetData(13, ref file); da.GetData(14, ref invert);
            o.Kind = (SurfacePatternKind)Math.Max(0, Math.Min(SurfacePattern.Names.Length - 1, kind));
            o.Amplitude = amp; o.Pitch = pitch; o.LayerPeriod = period; o.PhaseShift = phase; o.Droop = Math.Max(0, droop); o.ExtraFlow = flow;
            o.Inside = side == 1; o.Falloff = falloff; o.Reach = reach; o.Invert = invert;
            var region = new List<IGH_GeometricGoo>(); da.GetDataList(9, region);
            var attr = new List<IGH_GeometricGoo>(); da.GetDataList(11, attr);
            o.Regions = region.Where(g => g != null).Select(g => g.ScriptVariable()).Where(v => v != null).ToList();
            o.Attractors = attr.Where(g => g != null).Select(g => g.ScriptVariable()).Where(v => v != null).ToList();
            if (!string.IsNullOrWhiteSpace(file))
            {
                string path = file.Trim().Trim('"');
                // a bare file name or a relative path: next to the Grasshopper file
                string docPath = OnPingDocument()?.FilePath;
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(docPath)) path = Path.Combine(Path.GetDirectoryName(docPath), path);
                o.Image = LoadImage(path, out string problem);
                if (o.Image == null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);
            }
            if (pitch < tp.Settings.BeadWidth)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The pitch is shorter than one bead width: the bead fills the pattern in and it hardly shows.");

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var r = SurfacePattern.Apply(tp, o, log);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
            if (r.Hanging.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{r.Hanging.Count} point(s) hang over the layer below on purpose (the texture). Path Check will list them.");
            string name = SurfacePattern.Names[(int)o.Kind]; int cut = name.IndexOf(" ("); if (cut > 0) name = name.Substring(0, cut);
            Message = name + " " + amp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mm";
            da.SetData(0, new GH_Toolpath(r.Path));
            da.SetDataList(1, r.Preview.Select(c => new PolylineCurve(c)));
            da.SetDataList(2, r.Hanging);
            da.SetData(3, r.Info + $"\n{r.Path.Count} points ({tp.Count} before), {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>A picture as grey values, at most 600 px wide.</summary>
        internal static GrayImage LoadImage(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) { problem = "Image: file not found: " + path; return null; }
                using (var bmp = new System.Drawing.Bitmap(path))
                {
                    int step = Math.Max(1, (int)Math.Ceiling(Math.Max(bmp.Width, bmp.Height) / 600.0));
                    int w = Math.Max(1, bmp.Width / step), h = Math.Max(1, bmp.Height / step);
                    var v = new float[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = bmp.GetPixel(Math.Min(bmp.Width - 1, x * step), Math.Min(bmp.Height - 1, y * step));
                            // transparent = white (no pattern)
                            double a = c.A / 255.0;
                            double g = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                            v[y * w + x] = (float)(g * a + (1 - a));
                        }
                    return new GrayImage(w, h, v);
                }
            }
            catch (Exception e) { problem = "Image: could not read the picture (" + e.Message + ")."; return null; }
        }
    }
}
