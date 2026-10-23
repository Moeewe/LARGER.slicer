using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    /// <summary>
    /// Scales every XE=[value*P1] extrusion value of a DXR program depending on the position
    /// along one axis, so the printed line width runs from Width Start to Width End.
    /// Replaces the former "Create Variable Layerwidths" Python 3 script (works in Rhino 7 and 8).
    /// </summary>
    public class DXRVariableWidthComponent : GH_Component
    {
        private static readonly Regex XRegex = new Regex(@" X(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex YRegex = new Regex(@" Y(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex ZRegex = new Regex(@" Z(-?\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex XeRegex = new Regex(@"XE=\[(-?\d+\.?\d*)\*P1\]", RegexOptions.Compiled);
        private static readonly Regex EgesRegex = new Regex(@"IC\[-?\d+\.?\d*\]", RegexOptions.Compiled);

        private static readonly Drawing.Color ColorThin = Drawing.Color.FromArgb(0, 102, 204);
        private static readonly Drawing.Color ColorThick = Drawing.Color.FromArgb(230, 90, 30);

        // Set when loaded from a file or pasted, so existing definitions never get extra inputs.
        private bool _autoInputsCreated;

        private readonly List<Line> _previewLines = new List<Line>();
        private readonly List<Drawing.Color> _previewColors = new List<Drawing.Color>();
        private readonly List<int> _previewThickness = new List<int>();
        private BoundingBox _previewBox = BoundingBox.Empty;

        public DXRVariableWidthComponent()
          : base("DXR Variable Width", "VarWidth",
              "Scales the XE extrusion values of a DXR program along X, Y or Z so the printed line width runs from Width Start to Width End. " +
              "Use it between DXR Generator and Safe Component. Only lines with XE=[value*P1] are changed; all other lines are passed through unchanged.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("DXR Code", "Code",
                "Complete DXR program, as list of lines (DXR Generator output) or as one text.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Base Width", "Wbase",
                "Line width [mm] the existing XE values were calculated for (your extrusion calibration, e.g. 3.0).", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Width Start", "WA",
                "Target line width [mm] at the start of the gradient (low end of the axis, or high end if Flip is true).", GH_ParamAccess.item, 4.0);
            pManager.AddNumberParameter("Width End", "WB",
                "Target line width [mm] at the end of the gradient (high end of the axis, or low end if Flip is true).", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("Axis", "Axis",
                "Axis along which the width changes: 0 = X, 1 = Y, 2 = Z (by height). Right-click or connect a value list to choose.", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Mode", "Mode",
                "Shape of the transition: 0 = Linear, 1 = Smooth (slow start and end), 2 = Gamma (curve set by Gamma).", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Gamma", "G",
                "Only for Mode = Gamma. > 1 = width changes late, < 1 = width changes early (e.g. 2.0 or 0.5).", GH_ParamAccess.item, 2.0);
            pManager.AddBooleanParameter("Flip", "Flip",
                "If true, the gradient is reversed (Width Start at the high end of the axis).", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Range Min", "Min",
                "Optional fixed start of the gradient on the axis [mm]. Leave empty to use the part's extent automatically.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Range Max", "Max",
                "Optional fixed end of the gradient on the axis [mm]. Leave empty to use the part's extent automatically.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Factor", "MaxF",
                "Safety limit for the scale factor (e.g. 5.0 = at most 5x the original extrusion). 0 = no limit.", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Ignore First Layer", "Skip1",
                "If true, the first layer (skirt) is left out when measuring the automatic range, so the skirt does not stretch the gradient. " +
                "The first layer is still scaled.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Active", "On",
                "If false, the DXR code is passed through unchanged.", GH_ParamAccess.item, true);

            var axis = (Param_Integer)pManager[4];
            axis.AddNamedValue("X", 0);
            axis.AddNamedValue("Y", 1);
            axis.AddNamedValue("Z", 2);

            var mode = (Param_Integer)pManager[5];
            mode.AddNamedValue("Linear", 0);
            mode.AddNamedValue("Smooth", 1);
            mode.AddNamedValue("Gamma", 2);

            pManager[6].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("DXR Code", "Code",
                "DXR program with scaled XE values (list of lines). Connect to Safe Component or Fast GCode DXR Preview.", GH_ParamAccess.list);
            pManager.AddLineParameter("Segments", "Seg",
                "Extruded segments of the toolpath (travel moves are left out).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Widths", "W",
                "Resulting line width [mm] per segment.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Factors", "F",
                "Scale factor applied to the XE value per segment.", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "Col",
                "Color per segment from thin (blue) to thick (orange), e.g. for Custom Preview.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Layer", "L",
                "Layer index per segment (0 = lowest layer).", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info",
                "Summary: axis, range, widths, number of scaled segments and extrusion before/after.", GH_ParamAccess.list);

            // The component draws its own colored preview, so the plain segment preview is hidden.
            pManager.HideParameter(1);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (_autoInputsCreated)
                return;
            _autoInputsCreated = true;

            // Only for a freshly placed component: create ready-to-use inputs on the left.
            if (Params.Input[1].SourceCount > 0 || Params.Input[4].SourceCount > 0 || Params.Input[5].SourceCount > 0)
                return;

            Attributes.ExpireLayout();
            Attributes.PerformLayout();
            float left = Attributes.Bounds.Left;
            float InputY(int i) => Params.Input[i].Attributes.Pivot.Y - 10f;

            AddSlider(document, 1, "Base Width", 0.5m, 20m, 3.0m, left - 260f, InputY(1));
            AddSlider(document, 2, "Width Start", 0.5m, 30m, 4.0m, left - 260f, InputY(2));
            AddSlider(document, 3, "Width End", 0.5m, 30m, 10.0m, left - 260f, InputY(3));
            AddValueList(document, 4, "Axis", new[] { "X", "Y", "Z" }, 0, left - 140f, InputY(4));
            AddValueList(document, 5, "Mode", new[] { "Linear", "Smooth", "Gamma" }, 0, left - 140f, InputY(5));
        }

        private void AddSlider(GH_Document doc, int input, string name, decimal min, decimal max, decimal value, float x, float y)
        {
            var slider = new GH_NumberSlider();
            slider.CreateAttributes();
            slider.NickName = name;
            slider.Slider.Minimum = min;
            slider.Slider.Maximum = max;
            slider.Slider.DecimalPlaces = 1;
            slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
            slider.SetSliderValue(value);
            slider.Attributes.Pivot = new Drawing.PointF(x, y);
            doc.AddObject(slider, false);
            Params.Input[input].AddSource(slider);
        }

        private void AddValueList(GH_Document doc, int input, string name, string[] items, int selected, float x, float y)
        {
            var list = new GH_ValueList();
            list.CreateAttributes();
            list.NickName = name;
            list.ListMode = GH_ValueListMode.DropDown;
            list.ListItems.Clear();
            for (int i = 0; i < items.Length; i++)
                list.ListItems.Add(new GH_ValueListItem(items[i], i.ToString(CultureInfo.InvariantCulture)));
            list.SelectItem(selected);
            list.Attributes.Pivot = new Drawing.PointF(x, y);
            doc.AddObject(list, false);
            Params.Input[input].AddSource(list);
        }

        public override bool Read(GH_IReader reader)
        {
            _autoInputsCreated = true;
            return base.Read(reader);
        }

        protected override void BeforeSolveInstance()
        {
            _previewLines.Clear();
            _previewColors.Clear();
            _previewThickness.Clear();
            _previewBox = BoundingBox.Empty;
            base.BeforeSolveInstance();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var raw = new List<IGH_Goo>();
            double baseWidth = 3.0;
            double widthA = 4.0;
            double widthB = 10.0;
            int axis = 0;
            int mode = 0;
            double gamma = 2.0;
            bool flip = false;
            double rangeMin = double.NaN;
            double rangeMax = double.NaN;
            double maxFactor = 0.0;
            bool ignoreFirstLayer = true;
            bool active = true;

            if (!DA.GetDataList(0, raw)) return;
            DA.GetData(1, ref baseWidth);
            DA.GetData(2, ref widthA);
            DA.GetData(3, ref widthB);
            DA.GetData(4, ref axis);
            DA.GetData(5, ref mode);
            DA.GetData(6, ref gamma);
            DA.GetData(7, ref flip);
            bool hasMin = DA.GetData(8, ref rangeMin);
            bool hasMax = DA.GetData(9, ref rangeMax);
            DA.GetData(10, ref maxFactor);
            DA.GetData(11, ref ignoreFirstLayer);
            DA.GetData(12, ref active);

            var lines = SplitLines(NormalizeTextInput(raw));
            var info = new List<string>();

            if (!active)
            {
                DA.SetDataList(0, lines);
                info.Add("Inactive: DXR code passed through unchanged.");
                DA.SetDataList(6, info);
                return;
            }

            if (baseWidth <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Base Width must be greater than 0. Using 3.0 mm.");
                baseWidth = 3.0;
            }
            if (widthA <= 0.0 || widthB <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width Start and Width End must be greater than 0.");
                return;
            }
            if (axis < 0 || axis > 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Axis must be 0 (X), 1 (Y) or 2 (Z). Using X.");
                axis = 0;
            }
            if (mode < 0 || mode > 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Mode must be 0 (Linear), 1 (Smooth) or 2 (Gamma). Using Linear.");
                mode = 0;
            }

            Regex axisRegex = axis == 0 ? XRegex : (axis == 1 ? YRegex : ZRegex);
            string axisName = axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");

            // Pass 1: extent along the axis (optionally without the first layer / skirt).
            double zFirst = double.NaN;
            if (ignoreFirstLayer)
            {
                foreach (string ln in lines)
                {
                    if (!ln.Contains("XE=[")) continue;
                    if (TryMatch(ZRegex, ln, out double z) && (double.IsNaN(zFirst) || z < zFirst))
                        zFirst = z;
                }
            }

            double aMin = double.PositiveInfinity;
            double aMax = double.NegativeInfinity;
            foreach (string ln in lines)
            {
                if (!ln.Contains("XE=[")) continue;
                if (!TryMatch(axisRegex, ln, out double v)) continue;
                if (!double.IsNaN(zFirst) && TryMatch(ZRegex, ln, out double z) && Math.Abs(z - zFirst) < 1e-6)
                    continue;
                if (v < aMin) aMin = v;
                if (v > aMax) aMax = v;
            }

            if (double.IsInfinity(aMin))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No XE moves found. Is this DXR code from the DXR Generator?");
                DA.SetDataList(0, lines);
                info.Add("No XE moves found - code passed through unchanged.");
                DA.SetDataList(6, info);
                return;
            }

            double lo = hasMin ? rangeMin : aMin;
            double hi = hasMax ? rangeMax : aMax;
            double span = hi - lo;
            double fA = widthA / baseWidth;
            double fB = widthB / baseWidth;

            double FactorAt(double value)
            {
                double t = span != 0.0 ? (value - lo) / span : 0.0;
                if (flip) t = 1.0 - t;
                double f = fA + (fB - fA) * Distribute(t, mode, gamma);
                if (maxFactor > 0.0 && f > maxFactor) f = maxFactor;
                return f;
            }

            // Pass 2: scale XE values and collect segment data.
            var outLines = new List<string>(lines.Count);
            var segments = new List<Line>();
            var widths = new List<double>();
            var factors = new List<double>();
            var layers = new List<int>();
            var zLevels = new List<double>();
            bool hasPrev = false;
            Point3d prev = Point3d.Unset;
            double totalOld = 0.0;
            double totalNew = 0.0;
            int eqLine = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string ln = lines[i];
                if (eqLine < 0 && ln.Contains(";Eges"))
                    eqLine = i;

                if (ln.Contains("XE=[") && ln.Contains(" X"))
                {
                    Match mXe = XeRegex.Match(ln);
                    if (mXe.Success && TryMatch(XRegex, ln, out double cx) && TryMatch(YRegex, ln, out double cy) && TryMatch(ZRegex, ln, out double cz))
                    {
                        var cur = new Point3d(cx, cy, cz);
                        double xe = double.Parse(mXe.Groups[1].Value, CultureInfo.InvariantCulture);
                        double curAxis = cur[axis];
                        double reference = hasPrev ? (prev[axis] + curAxis) / 2.0 : curAxis;
                        double f = FactorAt(reference);
                        double newXe = xe * f;
                        totalOld += xe;
                        totalNew += newXe;

                        if (xe != 0.0 && hasPrev)
                        {
                            segments.Add(new Line(prev, cur));
                            widths.Add(f * baseWidth);
                            factors.Add(f);
                            layers.Add(LayerIndex(zLevels, cz));
                        }

                        ln = ln.Substring(0, mXe.Index)
                           + "XE=[" + newXe.ToString("0.000000", CultureInfo.InvariantCulture) + "*P1]"
                           + ln.Substring(mXe.Index + mXe.Length);
                        prev = cur;
                        hasPrev = true;
                    }
                }
                outLines.Add(ln);
            }

            // Keep the header's total extrusion in sync.
            if (eqLine >= 0)
                outLines[eqLine] = EgesRegex.Replace(outLines[eqLine], "IC[" + totalNew.ToString("0.000", CultureInfo.InvariantCulture) + "]", 1);

            double fMin = Math.Min(fA, fB);
            double fMax = Math.Max(fA, fB);
            if (maxFactor > 0.0) fMax = Math.Min(fMax, maxFactor);
            var colors = new List<Drawing.Color>(factors.Count);
            for (int i = 0; i < factors.Count; i++)
            {
                double t = fMax > fMin ? (factors[i] - fMin) / (fMax - fMin) : 0.0;
                var c = Blend(ColorThin, ColorThick, t);
                colors.Add(c);
                _previewLines.Add(segments[i]);
                _previewColors.Add(c);
                _previewThickness.Add(1 + (int)Math.Round(t * 4.0));
                _previewBox.Union(segments[i].BoundingBox);
            }

            string modeName = mode == 0 ? "Linear" : (mode == 1 ? "Smooth" : "Gamma " + gamma.ToString("0.##", CultureInfo.InvariantCulture));
            info.Add($"Axis: {axisName}   Range: {F3(lo)} .. {F3(hi)}   Mode: {modeName}{(flip ? "   Flipped" : "")}");
            if (!hasMin && !hasMax && !double.IsNaN(zFirst))
                info.Add($"Range measured without first layer (Z = {F3(zFirst)})");
            else
                info.Add(hasMin || hasMax ? "Range set manually (missing side measured automatically)" : "Range measured from all moves");
            info.Add($"Width {F2(widthA)} -> {F2(widthB)} mm (base {F2(baseWidth)} mm)   Factor {F3(fA)} .. {F3(fB)}{(maxFactor > 0.0 ? $"   capped at {F2(maxFactor)}" : "")}");
            info.Add($"Scaled segments: {segments.Count}   Layers: {zLevels.Count}");
            info.Add($"Extrusion old -> new: {totalOld.ToString("0.0", CultureInfo.InvariantCulture)} -> {totalNew.ToString("0.0", CultureInfo.InvariantCulture)}" +
                     $"  (x{(totalOld != 0.0 ? totalNew / totalOld : 0.0).ToString("0.000", CultureInfo.InvariantCulture)})");

            DA.SetDataList(0, outLines);
            DA.SetDataList(1, segments);
            DA.SetDataList(2, widths);
            DA.SetDataList(3, factors);
            DA.SetDataList(4, colors);
            DA.SetDataList(5, layers);
            DA.SetDataList(6, info);
        }

        public override BoundingBox ClippingBox => _previewBox;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked || _previewLines.Count == 0)
                return;
            for (int i = 0; i < _previewLines.Count; i++)
                args.Display.DrawLine(_previewLines[i], _previewColors[i], _previewThickness[i]);
        }

        private static double Distribute(double t, int mode, double gamma)
        {
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            if (mode == 1)
                return t * t * (3.0 - 2.0 * t);
            if (mode == 2)
                return Math.Pow(t, gamma > 0.0 ? gamma : 1.0);
            return t;
        }

        private static int LayerIndex(List<double> zLevels, double z)
        {
            for (int i = 0; i < zLevels.Count; i++)
            {
                if (Math.Abs(zLevels[i] - z) < 1e-4)
                    return i;
            }
            zLevels.Add(z);
            return zLevels.Count - 1;
        }

        private static bool TryMatch(Regex regex, string line, out double value)
        {
            Match m = regex.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            value = 0.0;
            return false;
        }

        private static Drawing.Color Blend(Drawing.Color a, Drawing.Color b, double t)
        {
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return Drawing.Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static string F2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
                return result;
            foreach (string ln in text.Split('\n'))
                result.Add(ln.TrimEnd('\r'));
            return result;
        }

        private static string NormalizeTextInput(List<IGH_Goo> raw)
        {
            if (raw == null || raw.Count == 0)
                return string.Empty;

            var parts = new List<string>(raw.Count);
            foreach (IGH_Goo goo in raw)
            {
                object val = goo?.ScriptVariable();
                if (val != null)
                    parts.Add(val as string ?? val.ToString());
            }
            return string.Join("\n", parts);
        }

        protected override Drawing.Bitmap Icon => IconHelper.Load("DXRVariableWidthIcon.png");

        public override Guid ComponentGuid => new Guid("6E22931A-9B8B-4F69-A7E2-E085BDAE6E9C");
    }
}
