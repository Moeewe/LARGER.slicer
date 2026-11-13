using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// G-code in full size for the Ginger One (pellet extruder on a gantry, Marlin firmware).
    /// Start and end are the sequences of the Ginger quick start guide in EXAMPLE FILES; the moves
    /// in between are absolute positions with relative extrusion; the feedrate is the print speed,
    /// lower on short layers (Min Layer Time) and on high-volume moves (Max Volume Rate) when these are set.
    /// </summary>
    public static class GingerGCode
    {
        public static readonly string[] Start =
        {
            "; START CODE",
            "; Custom pins declaration",
            "M42 I P49 T1 S0    ; pin PC4",
            "M42 I P57 T1 S0    ; pin PF9",
            "; Activate temperature-control",
            "M42 P49 I T1 S0",
            "M42 P57 I T1 S1",
            "G4 P2000",
            "M42 P57 I T1 S0",
            "G4 P200",
            "; Set units to mm and disable cold extrusion",
            "G21",
            "M302 P1",
            "; Set mixer values",
            "M163 S0 P1",
            "M163 S1 P0.07",
            "M164",
            "; Raise Z before homing",
            "G91",
            "G1 F200 Z10",
            "G90",
            "; Home X/Y and bed-level",
            "G28 X0 Y0",
            "M420 S1",
            "; Extruder ready tone",
            "M300 P200",
            "G4 P500",
            "M300 P200",
            "M0 Click if EXTR. READY",
            "; Final home",
            "G28",
            "G90",
            "G1 F200 Z2",
            "G92 E0",
            "M83",
        };

        public static readonly string[] End =
        {
            "; END CODE",
            "; Deactivate temperature control",
            "M42 P49 I T1 S1",
            "G4 P200",
            "; Raise Z-axis for part clearance",
            "G91",
            "G1 F100 Z30",
            "G90",
            "; Home X and disable motors",
            "G28 X",
            "M84",
            "; Finishing tone",
            "M300 P500",
            "G4 P1000",
            "M300 P500",
            "G4 P1000",
            "M300 P1000",
        };

        public class Result
        {
            public List<string> Lines = new List<string>();
            public List<string> Warnings = new List<string>();
            /// <summary>Remarks: worth knowing, no problem (shown grey).</summary>
            public List<string> Notes = new List<string>();
            public string Info = "";
        }

        private static string N(double v, int digits) => Math.Round(v, digits).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);

        public static Result Build(Toolpath tp)
        {
            var r = new Result();
            if (tp == null || tp.Count < 2) { r.Warnings.Add("Empty toolpath."); return r; }
            var st = tp.Settings;
            var pr = PathEngine.Process(tp, false);
            var m = st.Machine ?? new MachineProfile();
            if (m.Name == null || m.Name.IndexOf("Ginger", StringComparison.OrdinalIgnoreCase) < 0)
                r.Warnings.Add("The print settings do not use the Ginger machine profile, so the E values are not scaled for the Ginger. Connect Machine Profile with the Ginger preset to Print Settings.");
            if (tp.Normals.Any(n => Math.Abs(n.Z) < 0.9998))
                r.Warnings.Add("The toolpath has tilted tool axes (angled, non-planar or multi-axis). The Ginger keeps its nozzle vertical: only the positions are written. Check the clearance between nozzle and part.");
            var box = new BoundingBox(tp.Points);
            if (box.Min.X < 0 || box.Min.Y < 0 || box.Min.Z < 0)
                r.Warnings.Add(Inv.F($"Part of the path has negative coordinates (X {box.Min.X:0}, Y {box.Min.Y:0}, Z {box.Min.Z:0}). Move the part onto the bed: the machine origin is at 0, 0, 0."));

            var L = r.Lines;
            L.Add("; LARGER.slicer - Ginger One");
            L.Add("; bead " + N(st.BeadWidth, 2) + " x " + N(st.LayerHeight, 2) + " mm, " + m);
            L.Add("; " + OutputFile.SpeedNote(st, pr));
            L.Add("; path " + N(pr.Length / 1000.0, 2) + " m, about " + N(pr.Minutes, 0) + " min");
            L.AddRange(Start);

            double travel = Math.Max(st.Speed, st.MaxSpeed);
            Point3d p0 = tp.Points[0];
            L.Add("G0 X" + N(p0.X, 3) + " Y" + N(p0.Y, 3) + " F" + N(travel, 0));
            L.Add("G0 Z" + N(p0.Z, 3) + " F" + N(travel, 0));
            int layer = int.MinValue, number = 0; double total = 0; int zero = 0;
            for (int i = 1; i < tp.Count; i++)
            {
                if (tp.Layers[i] != layer) { layer = tp.Layers[i]; number++; L.Add(";LAYER:" + number); }
                Point3d p = tp.Points[i];
                if (p.DistanceTo(tp.Points[i - 1]) < 1e-6) continue;
                bool print = tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                if (!print) { L.Add("G0 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " F" + N(travel, 0)); continue; }
                double f = pr.Speed[i]; if (f <= 0) { f = st.Speed; zero++; }
                // the feedrate is written on every line and is never 0 (it changes where Min Layer Time or Max Volume Rate slow a layer or a move)
                L.Add("G1 X" + N(p.X, 3) + " Y" + N(p.Y, 3) + " Z" + N(p.Z, 3) + " E" + N(pr.Extrusion[i], 5) + " F" + N(f, 0));
                total += pr.Extrusion[i];
            }
            if (zero > 0) r.Warnings.Add(Inv.F($"{zero} move(s) had speed 0 and got the print speed instead."));
            L.AddRange(End);
            r.Info = Inv.F($"Ginger One: {number} layers, {pr.PrintLength / 1000.0:0.00} m printed, E total {total:0.0}, about {pr.Minutes:0} min, {L.Count} lines");
            return r;
        }
    }

    /// <summary>
    /// Robot moves as text, one PTP line per path point, for the DXR Generator. The Weber DXR
    /// extruder is driven by the robot program: the DXR file carries position, extrusion and speed
    /// per move. One fixed tool orientation is written; it suits flat layers with a vertical tool.
    /// </summary>
    public static class RobotLines
    {
        public class Result
        {
            public List<string> Lines = new List<string>();
            public List<double> Extrusion = new List<double>();
            public List<double> Speed = new List<double>();
            public bool Tilted;
        }

        public static Result Build(Toolpath tp, double a, double b, double c)
        {
            var r = new Result();
            if (tp == null || tp.Count < 2) return r;
            var pr = PathEngine.Process(tp, false);
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < tp.Count; i++)
            {
                Point3d p = tp.Points[i];
                if (Math.Abs(tp.Normals[i].Z) < 0.999) r.Tilted = true;
                r.Lines.Add(string.Format(ci, "PTP {{X {0:0.###}, Y {1:0.###}, Z {2:0.###}, A {3:0.###}, B {4:0.###}, C {5:0.###}}}", p.X, p.Y, p.Z, a, b, c));
            }
            r.Extrusion.AddRange(pr.Extrusion);
            r.Speed.AddRange(pr.Speed);
            return r;
        }
    }
}
