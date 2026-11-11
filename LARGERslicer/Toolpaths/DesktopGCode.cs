using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>A desktop filament printer for scaled test prints.</summary>
    public class DesktopPrinter
    {
        public string Name;
        public double BedX, BedY;

        public static readonly DesktopPrinter[] Presets =
        {
            new DesktopPrinter { Name = "Generic Marlin / Klipper 220 x 220", BedX = 220, BedY = 220 },
            new DesktopPrinter { Name = "Bambu Lab A1 / P1 / X1 256 x 256", BedX = 256, BedY = 256 },
            new DesktopPrinter { Name = "Bambu Lab A1 mini 180 x 180", BedX = 180, BedY = 180 },
            new DesktopPrinter { Name = "Prusa MK4 / MK3 250 x 210", BedX = 250, BedY = 210 },
            new DesktopPrinter { Name = "Prusa MINI 180 x 180", BedX = 180, BedY = 180 },
            new DesktopPrinter { Name = "Creality Ender-3 220 x 220", BedX = 220, BedY = 220 },
            new DesktopPrinter { Name = "Creality K1 220 x 220", BedX = 220, BedY = 220 },
            new DesktopPrinter { Name = "Elegoo Neptune 4 225 x 225", BedX = 225, BedY = 225 },
        };
    }

    public class DesktopGCodeResult
    {
        public List<string> Lines = new List<string>();
        public Polyline Preview = new Polyline();
        public List<string> Warnings = new List<string>();
        public string Info = "";
        public double SuggestedScale;
    }

    /// <summary>
    /// Turns a large-format toolpath into plain G-code for a small filament printer, scaled down.
    /// The path, the bead width and the layer height all shrink by the same factor, so the small
    /// print shows the same line pattern, seams and overhangs as the big one. It is a check of the
    /// path, not of the material: cooling, sagging and bonding of a pellet extruder do not scale.
    /// </summary>
    public static class DesktopGCode
    {
        private static string N(double v, int digits) => Math.Round(v, digits).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);

        public static DesktopGCodeResult Build(Toolpath tp, DesktopPrinter printer, double scale, double nozzle, double filament,
            double nozzleTemp, double bedTemp, double speedMmS, double flow)
        {
            var r = new DesktopGCodeResult();
            if (tp == null || tp.Count < 2) { r.Warnings.Add("Empty toolpath."); return r; }
            var st = tp.Settings;
            r.SuggestedScale = Math.Round(1.2 * nozzle / st.BeadWidth, 3);
            if (scale <= 0) scale = r.SuggestedScale;
            double w = st.BeadWidth * scale, h = st.LayerHeight * scale;

            var box = new BoundingBox(tp.Points);
            double zBase = box.Min.Z - st.LayerHeight;
            double cx = 0.5 * (box.Min.X + box.Max.X), cy = 0.5 * (box.Min.Y + box.Max.Y);
            double sx = (box.Max.X - box.Min.X) * scale, sy = (box.Max.Y - box.Min.Y) * scale, sz = (box.Max.Z - zBase) * scale;
            Func<Point3d, Point3d> map = p => new Point3d((p.X - cx) * scale + printer.BedX * 0.5, (p.Y - cy) * scale + printer.BedY * 0.5, Math.Max(0.05, (p.Z - zBase) * scale));

            if (sx > printer.BedX - 10 || sy > printer.BedY - 10)
                r.Warnings.Add($"The scaled part is {sx:0} x {sy:0} mm and does not fit the {printer.BedX:0} x {printer.BedY:0} mm bed with a 5 mm margin. Largest scale that fits: {Math.Min((printer.BedX - 10) / Math.Max(1e-9, sx / scale), (printer.BedY - 10) / Math.Max(1e-9, sy / scale)):0.###}.");
            if (w < 0.9 * nozzle || w > 2.0 * nozzle)
                r.Warnings.Add($"The scaled bead is {w:0.##} mm wide; a {nozzle:0.##} mm nozzle prints about {nozzle:0.##} to {2 * nozzle:0.##} mm. Try scale {r.SuggestedScale:0.###}.");
            if (h > 0.8 * nozzle)
                r.Warnings.Add($"The scaled layer is {h:0.##} mm high, more than 80 % of the nozzle. Use a smaller scale or a larger nozzle.");
            if (h < 0.05)
                r.Warnings.Add($"The scaled layer is only {h:0.###} mm high. Use a larger scale.");
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                r.Warnings.Add("The toolpath has tilted tool axes (angled, non-planar or multi-axis). A desktop printer keeps its nozzle vertical, so only positions are used.");

            double filArea = Math.PI * 0.25 * filament * filament;
            double F = Math.Max(5, speedMmS) * 60.0, travelF = Math.Max(F, 9000);
            int firstLayer = tp.Layers.Min();
            var L = r.Lines;
            L.Add("; LARGER.slicer scale test, scale " + N(scale, 4) + ", " + printer.Name);
            L.Add("; bead " + N(w, 2) + " x " + N(h, 2) + " mm, nozzle " + N(nozzle, 2) + " mm, filament " + N(filament, 2) + " mm");
            L.Add("M140 S" + N(bedTemp, 0)); L.Add("M104 S" + N(nozzleTemp, 0));
            L.Add("G28");
            L.Add("M190 S" + N(bedTemp, 0)); L.Add("M109 S" + N(nozzleTemp, 0));
            L.Add("G90"); L.Add("M83"); L.Add("G92 E0");
            L.Add("G1 Z5 F3000");

            Point3d prev = map(tp.Points[0]);
            L.Add("G0 X" + N(prev.X, 3) + " Y" + N(prev.Y, 3) + " F" + N(travelF, 0));
            L.Add("G0 Z" + N(prev.Z, 3) + " F3000");
            r.Preview.Add(prev);
            bool retracted = false, fanOn = false;
            double totalE = 0, minutes = 0, lastF = -1;
            const double retract = 0.8;
            for (int i = 1; i < tp.Count; i++)
            {
                Point3d p = map(tp.Points[i]);
                double len = p.DistanceTo(prev);
                // at a small scale the points lie closer than the printer can resolve: thin them out
                bool printing = tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                if (len < 0.15 && printing && i < tp.Count - 1 && tp.Layers[i + 1] == tp.Layers[i] && tp.Flows[i + 1] > 0) continue;
                r.Preview.Add(p);
                if (len < 1e-5) { prev = p; continue; }
                if (!fanOn && tp.Layers[i] > firstLayer) { L.Add("M106 S255"); fanOn = true; }
                bool print = tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                if (!print)
                {
                    if (!retracted) { L.Add("G1 E-" + N(retract, 2) + " F1800"); retracted = true; }
                    L.Add("G0 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " F" + N(travelF, 0));
                    lastF = travelF; minutes += len / travelF;
                }
                else
                {
                    if (retracted) { L.Add("G1 E" + N(retract, 2) + " F1800"); retracted = false; lastF = -1; }
                    double hh = Math.Max(0.02, 0.5 * (tp.Heights[i] + tp.Heights[i - 1]) * scale);
                    double ww = 0.5 * (tp.Widths[i] + tp.Widths[i - 1]) * scale;
                    double e = PrintSettings.BeadArea(ww, hh) * len / filArea * tp.Flows[i] * flow;
                    double f = tp.Layers[i] == firstLayer ? 0.5 * F : F;
                    L.Add("G1 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " E" + N(e, 5) + (Math.Abs(f - lastF) > 0.5 ? " F" + N(f, 0) : ""));
                    lastF = f; totalE += e; minutes += len / f;
                }
                prev = p;
            }
            L.Add("G1 E-" + N(retract, 2) + " F1800");
            L.Add("G91"); L.Add("G1 Z10 F3000"); L.Add("G90");
            L.Add("G0 X5 Y" + N(printer.BedY - 5, 0) + " F" + N(travelF, 0));
            L.Add("M107"); L.Add("M104 S0"); L.Add("M140 S0"); L.Add("M84");

            r.Info = $"{printer.Name}: scale {scale:0.###}, part {sx:0} x {sy:0} x {sz:0} mm, bead {w:0.##} x {h:0.##} mm, " +
                     $"{totalE / 1000.0:0.00} m filament, about {minutes:0} min, {L.Count} lines";
            return r;
        }
    }
}
