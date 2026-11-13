using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>How a printer wants its file to begin, change layer and end.</summary>
    public enum DesktopFamily { Marlin, BambuP1X1, BambuA1, BambuH2D, PrusaMK4, PrusaMini, Ender3, KlipperMacros, Neptune4 }

    /// <summary>A desktop filament printer for scaled test prints.</summary>
    public class DesktopPrinter
    {
        public string Name;
        public double BedX, BedY, MaxZ = 250;
        public DesktopFamily Family = DesktopFamily.Marlin;

        // The order is the value of the Printer input: new printers are added at the end.
        public static readonly DesktopPrinter[] Presets =
        {
            new DesktopPrinter { Name = "Generic Marlin / Klipper 220 x 220", BedX = 220, BedY = 220 },
            new DesktopPrinter { Name = "Bambu Lab P1P / P1S / X1C 256 x 256", BedX = 256, BedY = 256, MaxZ = 250, Family = DesktopFamily.BambuP1X1 },
            new DesktopPrinter { Name = "Bambu Lab A1 mini 180 x 180", BedX = 180, BedY = 180, MaxZ = 180, Family = DesktopFamily.BambuA1 },
            new DesktopPrinter { Name = "Prusa MK4 250 x 210", BedX = 250, BedY = 210, MaxZ = 220, Family = DesktopFamily.PrusaMK4 },
            new DesktopPrinter { Name = "Prusa MINI 180 x 180", BedX = 180, BedY = 180, MaxZ = 180, Family = DesktopFamily.PrusaMini },
            new DesktopPrinter { Name = "Creality Ender-3 220 x 220", BedX = 220, BedY = 220, MaxZ = 250, Family = DesktopFamily.Ender3 },
            new DesktopPrinter { Name = "Creality K1 220 x 220", BedX = 220, BedY = 220, MaxZ = 250, Family = DesktopFamily.KlipperMacros },
            new DesktopPrinter { Name = "Elegoo Neptune 4 230 x 230", BedX = 230, BedY = 230, MaxZ = 265, Family = DesktopFamily.Neptune4 },
            new DesktopPrinter { Name = "Bambu Lab A1 256 x 256", BedX = 256, BedY = 256, MaxZ = 256, Family = DesktopFamily.BambuA1 },
            new DesktopPrinter { Name = "Bambu Lab H2D 350 x 320 (start and end left open)", BedX = 350, BedY = 320, MaxZ = 320, Family = DesktopFamily.BambuH2D },
        };

        /// <summary>What the user has to know before sending the file to this printer.</summary>
        public IEnumerable<string> Notes()
        {
            switch (Family)
            {
                case DesktopFamily.BambuP1X1:
                    yield return "Bambu P1/X1: a plain .gcode file is started from the microSD card on the printer's screen; the cloud only takes .3mf. " +
                                 "This file does not load filament, wipe the nozzle or use the AMS: load the filament first. " +
                                 "Bambu Lab documents no start sequence for plain files; this one follows the community sequence (heat, G28, purge line at the front) and is not tested on a printer here.";
                    break;
                case DesktopFamily.BambuA1:
                    yield return "Bambu A1: the file uses a plain start (heat, G28, purge line). The printer's own start does more (nozzle cleaning on the bed, flow calibration). Not tested on a printer here: load the filament first and stay at the printer for the first layer.";
                    break;
                case DesktopFamily.BambuH2D:
                    yield return "Bambu H2D: start and end are left open. The H2D chooses one of two nozzles and homes with its own commands, and no plain start is documented. " +
                                 "Paste the start and end G-code of your H2D profile where the file says so, or print the test on another printer.";
                    break;
                case DesktopFamily.PrusaMK4:
                    yield return "Prusa MK4: start and end follow the PrusaSlicer profile (probing only the print area). PrusaSlicer writes binary G-code by default; this is a plain .gcode file.";
                    break;
                case DesktopFamily.KlipperMacros:
                    yield return "Creality K1: the file calls the printer's START_PRINT and END_PRINT macros, which home, level and prime.";
                    break;
            }
        }

        /// <summary>Lines before the first move. x0, y0, sizeX, sizeY: where the print lies on the bed.</summary>
        public List<string> Start(double bed, double nozzle, double x0, double y0, double sizeX, double sizeY)
        {
            Func<double, string> n0 = v => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
            string B = n0(bed), T = n0(nozzle);
            var g = new List<string>();
            switch (Family)
            {
                case DesktopFamily.BambuP1X1:
                    g.AddRange(new[]
                    {
                        "M140 S" + B + " ; bed", "M104 S150 ; nozzle warm, below oozing", "M190 S" + B, "M109 S150",
                        "G28 ; home", "G90", "G21", "M83 ; relative extrusion",
                        "G1 X18 Y1 Z0.8 F18000 ; purge line at the front edge", "M109 S" + T, "G1 Z0.2",
                        "G0 E2 F300", "G0 X240 E15 F2400", "G0 Y11 E0.7 F600", "G0 X239.5", "G0 E0.2", "G0 Y1.5 E0.7", "G0 X18 E15 F2400", "M400",
                        "M73 P0 R0",
                    });
                    break;
                case DesktopFamily.BambuH2D:
                    g.AddRange(new[]
                    {
                        "; ===== START: paste the machine start G-code of your H2D profile here =====",
                        "; (Bambu Studio > printer settings > Machine G-code). Nothing below homes or heats.",
                        "G90", "M83 ; relative extrusion",
                    });
                    break;
                case DesktopFamily.PrusaMK4:
                    g.AddRange(new[]
                    {
                        "M17 ; enable steppers", "M862.3 P \"MK4\" ; printer model check",
                        string.Format(CultureInfo.InvariantCulture, "M555 X{0:0} Y{1:0} W{2:0} H{3:0} ; print area", x0, y0, sizeX, sizeY),
                        "G90 ; use absolute coordinates", "M83 ; extruder relative mode",
                        "M140 S" + B + " ; set bed temp", "M104 T0 S170 ; set extruder temp for bed leveling", "M109 T0 R170 ; wait for temp",
                        "M84 E ; turn off E motor", "G28 ; home all without mesh bed level", "G1 X42 Y-4 Z5 F4800", "M302 S155 ; lower cold extrusion limit to 155C",
                        "G1 E-2 F2400 ; retraction", "M84 E ; turn off E motor", "G29 P9 X10 Y-4 W32 H4", "G0 Z40 F10000",
                        "M190 S" + B + " ; wait for bed temp", "M107", "M84 E ; turn off E motor",
                        "G29 P1 ; invalidate mbl & probe print area", "G29 P1 X0 Y0 W50 H20 C ; probe near purge place", "G29 P3.2 ; interpolate mbl probes",
                        "G29 P3.13 ; extrapolate mbl outside probe area", "G29 A ; activate mbl",
                        "M104 S" + T, "G0 X0 Y-4 Z15 F4800 ; move away and ready for the purge", "M109 S" + T,
                        "G92 E0", "M569 S0 E ; set spreadcycle mode for extruder", "G92 E0 ; reset extruder position",
                        "G1 E2 F2400 ; deretraction after the initial one before nozzle cleaning",
                        "G0 E7 X15 Z0.2 F500 ; purge", "G0 X25 E4 F500 ; purge", "G0 X35 E4 F650 ; purge", "G0 X45 E4 F800 ; purge",
                        "G0 X48 Z0.05 F8000 ; wipe, move close to the bed", "G0 X51 Z0.2 F8000 ; wipe, move quickly away from the bed",
                        "G92 E0", "M221 S100 ; set flow to 100%",
                    });
                    break;
                case DesktopFamily.PrusaMini:
                    g.AddRange(new[]
                    {
                        "M862.3 P \"MINI\" ; printer model check", "G90 ; use absolute coordinates", "M83 ; extruder relative mode",
                        "M104 S170 ; set extruder temp for bed leveling", "M140 S" + B + " ; set bed temp", "M109 R170 ; wait for bed leveling temp", "M190 S" + B + " ; wait for bed temp",
                        "G28 ; home all without mesh bed level", "G29 ; mesh bed leveling", "M104 S" + T + " ; set extruder temp", "G92 E0",
                        "G1 Y-2 X179 F2400", "G1 Z3 F720", "M109 S" + T + " ; wait for extruder temp",
                        "G1 X170 F1000 ; intro line", "G1 Z0.2 F720", "G1 X110 E8 F900", "G1 X40 E10 F700", "G92 E0",
                    });
                    break;
                case DesktopFamily.Ender3:
                    g.AddRange(new[]
                    {
                        "G90 ; use absolute coordinates", "M83 ; extruder relative mode", "M140 S" + B + " ; set final bed temp",
                        "M104 S150 ; set temporary nozzle temp to prevent oozing during homing", "G4 S10 ; allow partial nozzle warmup", "G28 ; home all axis",
                        "G1 Z50 F240", "G1 X2 Y10 F3000", "M104 S" + T + " ; set final nozzle temp", "M190 S" + B + " ; wait for bed temp to stabilize", "M109 S" + T + " ; wait for nozzle temp to stabilize",
                        "G1 Z0.28 F240", "G92 E0", "G1 Y140 E10 F1500 ; prime the nozzle", "G1 X2.3 F5000", "G92 E0", "G1 Y10 E10 F1200 ; prime the nozzle", "G92 E0",
                    });
                    break;
                case DesktopFamily.KlipperMacros:
                    g.AddRange(new[] { "START_PRINT EXTRUDER_TEMP=" + T + " BED_TEMP=" + B, "M204 S2000", "M109 S" + T, "G1 Z3 F600", "M83", "G92 E0", "G1 Z1 F600" });
                    break;
                case DesktopFamily.Neptune4:
                    g.AddRange(new[]
                    {
                        "M400 ; wait for buffer to clear", "M220 S100 ;Set the feed speed to 100%", "M221 S100 ;Set the flow rate to 100%", "M104 S140", "M190 S" + B,
                        "G90", "G28 ;home", "G1 Z10 F300", "G1 X65 Y0.5 F6000", "G1 Z0.4 F300", "M109 S" + T, "M83 ; extruder relative mode", "G92 E0 ;Reset Extruder",
                        "G1 X165 E30 F400 ;Draw the first line", "G1 Z0.6 F120.0 ;Move to side a little", "G1 X162 F3000", "G92 E0 ;Reset Extruder",
                    });
                    break;
                default: // plain Marlin, also the Bambu A1 family
                    g.AddRange(new[] { "M140 S" + B, "M104 S" + T, "G28", "M190 S" + B, "M109 S" + T, "G90", "M83", "G92 E0", "G1 Z5 F3000" });
                    break;
            }
            return g;
        }

        /// <summary>Lines at the beginning of layer number (1-based) of total.</summary>
        public List<string> LayerChange(int number, int total, double z, double height)
        {
            var c = CultureInfo.InvariantCulture;
            var g = new List<string>();
            switch (Family)
            {
                case DesktopFamily.BambuP1X1:
                case DesktopFamily.BambuA1:
                case DesktopFamily.BambuH2D:
                    g.Add("; CHANGE_LAYER");
                    g.Add("; Z_HEIGHT: " + z.ToString("0.###", c));
                    g.Add("; LAYER_HEIGHT: " + height.ToString("0.###", c));
                    g.Add("; layer num/total_layer_count: " + number + "/" + total);
                    g.Add("M73 L" + number);
                    g.Add("M991 S0 P" + (number - 1) + " ;notify layer change");
                    g.Add("M73 P" + (int)Math.Floor(100.0 * (number - 1) / Math.Max(1, total)) + " R0");
                    break;
                case DesktopFamily.PrusaMK4:
                case DesktopFamily.PrusaMini:
                case DesktopFamily.KlipperMacros:
                    g.Add(";AFTER_LAYER_CHANGE");
                    g.Add(";" + z.ToString("0.###", c));
                    break;
                default:
                    g.Add(";LAYER:" + number);
                    break;
            }
            return g;
        }

        /// <summary>Lines after the last move. maxZ: highest point of the print.</summary>
        public List<string> End(double maxZ)
        {
            var c = CultureInfo.InvariantCulture;
            Func<double, string> z = v => Math.Min(v, MaxZ).ToString("0.##", c);
            var g = new List<string>();
            switch (Family)
            {
                case DesktopFamily.BambuP1X1:
                    g.AddRange(new[]
                    {
                        "M400 ; wait for buffer to clear", "G92 E0 ; zero the extruder", "G1 E-0.8 F1800 ; retract", "G1 Z" + z(maxZ + 0.5) + " F900 ; lower z a little",
                        "G1 X65 Y245 F12000 ; move to safe pos", "G1 Y265 F3000", "M140 S0 ; turn off bed", "M106 S0 ; turn off fan",
                        "M106 P2 S0 ; turn off remote part cooling fan", "M106 P3 S0 ; turn off chamber cooling fan", "G1 X100 F12000 ; wipe", "M104 S0 ; turn off hotend",
                        "M400 ; wait all motion done", "G1 Z" + z(maxZ + 100) + " F600", "M73 P100 R0",
                    });
                    break;
                case DesktopFamily.BambuH2D:
                    g.Add("; ===== END: paste the machine end G-code of your H2D profile here =====");
                    break;
                case DesktopFamily.PrusaMK4:
                    g.AddRange(new[]
                    {
                        "G1 Z" + z(maxZ + 1) + " F720 ; Move print head up", "M104 S0 ; turn off temperature", "M140 S0 ; turn off heatbed", "M107 ; turn off fan",
                        "G1 X241 Y170 F3600 ; park", "G1 Z" + z(maxZ + 23) + " F300 ; Move print head up", "G4 ; wait", "M900 K0 ; reset LA", "M142 S36 ; reset heatbreak target temp", "M84 X Y E ; disable motors",
                    });
                    break;
                case DesktopFamily.PrusaMini:
                    g.AddRange(new[]
                    {
                        "G1 E-1 F2100 ; retract", "G1 Z" + z(maxZ + 2) + " F720 ; Move print head up", "G1 X178 Y178 F4200 ; park print head", "G1 Z" + z(maxZ + 30) + " F720 ; Move print head further up",
                        "G4 ; wait", "M104 S0 ; turn off temperature", "M140 S0 ; turn off heatbed", "M107 ; turn off fan", "M221 S100 ; reset flow", "M900 K0 ; reset LA", "M84 ; disable motors",
                    });
                    break;
                case DesktopFamily.Ender3:
                    g.AddRange(new[]
                    {
                        "G1 Z" + z(maxZ + 2) + " F600 ; Move print head up", "G1 X5 Y" + (BedY * 0.8).ToString("0", c) + " F9000 ; present print", "G1 Z" + z(maxZ + 70) + " F600 ; Move print head further up",
                        "M140 S0 ; turn off heatbed", "M104 S0 ; turn off temperature", "M107 ; turn off fan", "M84 X Y E ; disable motors",
                    });
                    break;
                case DesktopFamily.KlipperMacros:
                    g.Add("END_PRINT");
                    break;
                case DesktopFamily.Neptune4:
                    g.AddRange(new[]
                    {
                        ";PRINT_END", "G90 ;Absolute positionning", "M83 ; extruder relative mode", "G92 E0 ; zero the extruder", "G1 E-1.5 F1800",
                        "G1 Z" + z(maxZ + 0.5) + " F3000 ; lower z a little", "G1 X10 Y" + (BedY * 0.8).ToString("0", c) + " Z" + z(maxZ + 50) + " E-5 F9000 ; Move print head up",
                        "M106 S0 ;Turn-off fan", "M104 S0 ;Turn-off hotend", "M140 S0 ;Turn-off bed", "M84 X Y E ;Disable all steppers but Z",
                    });
                    break;
                default:
                    g.AddRange(new[] { "G1 E-0.8 F1800", "G91", "G1 Z10 F3000", "G90", "G0 X5 Y" + (BedY - 5).ToString("0", c) + " F9000", "M107", "M104 S0", "M140 S0", "M84" });
                    break;
            }
            return g;
        }
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
                r.Warnings.Add(Inv.F($"The scaled part is {sx:0} x {sy:0} mm and does not fit the {printer.BedX:0} x {printer.BedY:0} mm bed with a 5 mm margin. Largest scale that fits: {Math.Min((printer.BedX - 10) / Math.Max(1e-9, sx / scale), (printer.BedY - 10) / Math.Max(1e-9, sy / scale)):0.###}."));
            if (w < 0.9 * nozzle || w > 2.0 * nozzle)
                r.Warnings.Add(Inv.F($"The scaled bead is {w:0.##} mm wide; a {nozzle:0.##} mm nozzle prints about {nozzle:0.##} to {2 * nozzle:0.##} mm. Try scale {r.SuggestedScale:0.###}."));
            if (h > 0.8 * nozzle)
                r.Warnings.Add(Inv.F($"The scaled layer is {h:0.##} mm high, more than 80 % of the nozzle. Use a smaller scale or a larger nozzle."));
            if (h < 0.05)
                r.Warnings.Add(Inv.F($"The scaled layer is only {h:0.###} mm high. Use a larger scale."));
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                r.Warnings.Add("The toolpath has tilted tool axes (angled, non-planar or multi-axis). A desktop printer keeps its nozzle vertical, so only positions are used.");

            double filArea = Math.PI * 0.25 * filament * filament;
            double F = Math.Max(5, speedMmS) * 60.0, travelF = Math.Max(F, 9000);
            int firstLayer = tp.Layers.Min();
            var L = r.Lines;
            L.Add("; LARGER.slicer scale test, scale " + N(scale, 4) + ", " + printer.Name);
            L.Add("; bead " + N(w, 2) + " x " + N(h, 2) + " mm, nozzle " + N(nozzle, 2) + " mm, filament " + N(filament, 2) + " mm");
            L.Add("; print speed " + N(F / 60.0, 1) + " mm/s (first layer half of it), the speed of this scale test, not of the full-size print; travel " + N(travelF / 60.0, 0) + " mm/s");
            foreach (var note in printer.Notes()) { r.Warnings.Add(note); L.Add("; NOTE: " + note); }
            double bedX0 = printer.BedX * 0.5 - 0.5 * sx, bedY0 = printer.BedY * 0.5 - 0.5 * sy;
            if (printer.Family == DesktopFamily.BambuP1X1 && bedX0 < 20 && bedY0 < 30)
                r.Warnings.Add("The print reaches into the front left corner of the bed (18 x 28 mm), which the Bambu P1/X1 keeps free for its filament cutter. Use a smaller scale.");
            if (sz > printer.MaxZ) r.Warnings.Add(Inv.F($"The scaled part is {sz:0} mm high; the printer takes {printer.MaxZ:0} mm."));
            L.AddRange(printer.Start(bedTemp, nozzleTemp, bedX0, bedY0, sx, sy));
            int layerTotal = tp.Layers.Distinct().Count(), layerNumber = 0, layerSeen = int.MinValue;

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
                if (tp.Layers[i] != layerSeen)
                {
                    layerSeen = tp.Layers[i]; layerNumber++;
                    L.AddRange(printer.LayerChange(layerNumber, layerTotal, p.Z, h));
                }
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
            L.AddRange(printer.End(sz));

            r.Info = Inv.F($"{printer.Name}: scale {scale:0.###}, part {sx:0} x {sy:0} x {sz:0} mm, bead {w:0.##} x {h:0.##} mm, ") +
                     Inv.F($"{totalE / 1000.0:0.00} m filament, about {minutes:0} min, {L.Count} lines");
            return r;
        }
    }
}
