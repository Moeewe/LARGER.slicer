using System;
using System.Collections.Generic;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Design rules for large-format pellet printing, flat and at 45 degrees, from the CEAD
    /// "Angled printing (45 deg) design process" and "Nozzle size and layer width" sheets and the
    /// Ginger handbook of the FH Muenster. All values in mm.
    ///   layer height      N/4 .. N/2                      (CEAD; Ginger: at most 0.6 N)
    ///   overlap           0.05 LW .. 0.1 LW               (CEAD)
    ///   double wall       CO90 = 2 LW - OL                (CAD offset of a wall of two beads, layers square to it)
    ///   at 45 degrees     CO45 = 1/2 sqrt2 (2 LW - OL)    (the same wall where the layers cross it at 45 degrees)
    ///   finish thickness  LW45 = sqrt2 t_finish, with milling LW45 = sqrt2 (t_finish - t_milling)
    ///   Ginger            LW >= 1.5 N, LW / LH >= 2, corner radius >= 0.5 N, double bead 0.8 .. 0.9 LW
    ///   start and end     about 5 layers more before and after the part when printing at an angle
    /// </summary>
    public static class DesignRules
    {
        public class Result
        {
            public double Nozzle, LayerMin, LayerMax, LayerRecommended, OverlapMin, OverlapMax, Overlap, CO90, CO45, LW45, MinCornerRadius, StartLayers;
            public bool Ok = true;
            public List<string> Report = new List<string>();
        }

        public static Result Evaluate(double beadWidth, double nozzle, double layerHeight, double overlap, double finishThickness, double milling)
        {
            var r = new Result();
            double lw = beadWidth;
            // without a nozzle: the one CEAD pairs with this layer width (layer width about 1.5 x nozzle)
            r.Nozzle = nozzle > 0 ? nozzle : Math.Round(lw / 1.5, 1);
            double n = r.Nozzle;
            r.LayerMin = n / 4; r.LayerMax = n / 2; r.LayerRecommended = Math.Min(r.LayerMax, lw / 2.5);
            r.OverlapMin = 0.05 * lw; r.OverlapMax = 0.1 * lw;
            r.Overlap = overlap > 0 ? overlap : 0.5 * (r.OverlapMin + r.OverlapMax);
            r.CO90 = 2 * lw - r.Overlap;
            r.CO45 = 0.5 * Math.Sqrt(2) * (2 * lw - r.Overlap);
            r.MinCornerRadius = 0.5 * n;
            r.StartLayers = 5;
            Func<double, string> f = v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            r.Report.Add(Inv.F($"Nozzle {f(n)} mm{(nozzle > 0 ? "" : " (assumed: about bead width / 1.5)")}, bead width {f(lw)} mm."));
            r.Report.Add(Inv.F($"Layer height {f(r.LayerMin)} to {f(r.LayerMax)} mm (a quarter to half the nozzle)."));
            // Weber quick guide: the nozzle shape decides how wide a bead it lays
            r.Report.Add(Inv.F($"Bead width for this nozzle (Weber): pointed nozzle {f(0.7 * n)} to {f(1.3 * n)} mm, flat nozzle {f(1.0 * n)} to {f(1.8 * n)} mm; Weber's own profiles use about {f(1.2 * n)} mm with a {f(0.4 * n)} mm layer."));
            if (lw > 1.3 * n + 1e-9 && lw <= 1.8 * n + 1e-9) r.Report.Add(Inv.F($"Bead {f(lw)} mm needs a flat nozzle: a pointed {f(n)} mm nozzle lays at most {f(1.3 * n)} mm."));
            else if (lw > 1.8 * n + 1e-9) { r.Ok = false; r.Report.Add(Inv.F($"BEAD TOO WIDE FOR THE NOZZLE: {f(lw)} mm is more than even a flat {f(n)} mm nozzle lays ({f(1.8 * n)} mm).")); }
            r.Report.Add(Inv.F($"Milling allowance: print half a bead ({f(0.5 * lw)} mm) more where the surface is milled (Weber)."));
            if (layerHeight > 0)
            {
                if (layerHeight > 0.8 * n + 1e-9) { r.Ok = false; r.Report.Add(Inv.F($"LAYER TOO HIGH: {f(layerHeight)} mm is more than 80 % of the nozzle; the bead is hardly pressed and bonds poorly.")); }
                else if (layerHeight > r.LayerMax + 1e-9) r.Report.Add(Inv.F($"Note: {f(layerHeight)} mm is above the CEAD range (half the nozzle, {f(r.LayerMax)} mm; the Ginger handbook allows up to 60 %, {f(0.6 * n)} mm). It can work with a well-flowing material: check how the layers bond."));
                else if (layerHeight < r.LayerMin - 1e-9) r.Report.Add(Inv.F($"Note: {f(layerHeight)} mm is less than a quarter of the nozzle: the extruder has to push hard, printing takes long."));
                else r.Report.Add(Inv.F($"Layer height {f(layerHeight)} mm: OK."));
                if (lw / layerHeight < 2) { r.Ok = false; r.Report.Add(Inv.F($"BEAD TOO NARROW: width / height is {f(lw / layerHeight)}; keep it at 2 or more (Ginger handbook).")); }
            }
            if (lw < 1.5 * n - 1e-9) { r.Ok = false; r.Report.Add(Inv.F($"BEAD TOO NARROW FOR THE NOZZLE: {f(lw)} mm is less than 1.5 x the nozzle ({f(1.5 * n)} mm).")); }
            r.Report.Add(Inv.F($"Overlap of two beads side by side {f(r.OverlapMin)} to {f(r.OverlapMax)} mm; used {f(r.Overlap)} mm{(overlap > 0 && (overlap < r.OverlapMin - 1e-9 || overlap > r.OverlapMax + 1e-9) ? " - OUTSIDE the range" : "")}."));
            r.Report.Add(Inv.F($"Wall of two beads: model it {f(r.CO90)} mm thick where the layers stand square to it (CO90 = 2 LW - OL), {f(r.CO45)} mm where they cross it at 45 degrees (CO45 = 1/2 sqrt2 (2 LW - OL))."));
            if (finishThickness > 0)
            {
                r.LW45 = Math.Sqrt(2) * Math.Max(0, finishThickness - Math.Max(0, milling));
                r.Report.Add(milling > 0
                    ? Inv.F($"For a finished wall of {f(finishThickness)} mm after milling {f(milling)} mm away, at 45 degrees use a layer width of {f(r.LW45)} mm (LW45 = sqrt2 (t - t milling)).")
                    : Inv.F($"For a finished wall of {f(finishThickness)} mm at 45 degrees use a layer width of {f(r.LW45)} mm (LW45 = sqrt2 t)."));
            }
            r.Report.Add(Inv.F($"Corners: radius at least {f(r.MinCornerRadius)} mm (half the nozzle); Round Corners uses the bead width."));
            r.Report.Add(Inv.F($"Two beads in one go (double bead): 0.8 to 0.9 x the layer width apart, {f(0.8 * lw)} to {f(0.9 * lw)} mm."));
            r.Report.Add(Inv.F($"At an angle: add a start and an end feature of about {r.StartLayers:0} layers ({f(r.StartLayers * (layerHeight > 0 ? layerHeight : r.LayerRecommended))} mm) before and after the part, milled away afterwards, so the part itself starts and ends on full layers: set Start/End Layers on Slice."));
            r.Report.Add("Overhang: flat layers up to 45 degrees; printed at 45 degrees up to 90 degrees. Contours inside contours are joined by bridges in Continuous Path, so the line never stops.");
            return r;
        }
    }
}
