using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    public enum ExternalAxisKind { Rail, Turntable }

    /// <summary>
    /// An axis of the cell besides the six of the arm (KUKA: E1 to E6).
    /// Rail (linear track): the robot stands on a carriage that moves along the X axis of Place.
    ///   The value is in mm; at 0 the robot stands where its Base says.
    /// Turntable (rotary positioner): the part stands on a table that turns about the Z axis of
    ///   Place, through its origin. The value is in degrees, counted the right-hand way about Z;
    ///   at 0 the part stands as it is drawn in Rhino.
    /// </summary>
    public class ExternalAxis
    {
        public ExternalAxisKind Kind = ExternalAxisKind.Rail;
        /// <summary>Name in the KUKA program: E1 ... E6.</summary>
        public string Name = "E1";
        public Frame Place = Frame.World;
        /// <summary>Range: mm for a rail, degrees for a turntable.</summary>
        public double Min, Max = 3000;
        /// <summary>Turntable only: turns on without end, the range does not count.</summary>
        public bool Endless;
        /// <summary>Top speed: mm/s for a rail, degrees/s for a turntable.</summary>
        public double MaxSpeed = 1000;
        /// <summary>
        /// Rail: the robot foot stands this far ahead of the printed point, along the rail (mm).
        /// Turntable: the printed point is turned to this angle (degrees) away from the direction
        /// to the robot.
        /// </summary>
        public double Offset;
        /// <summary>How much path (mm) the axis averages over, so it moves smoothly and not with every corner.</summary>
        public double Smoothing = 300;
        /// <summary>Turntable only: KRL targets counted in the turning base of the table (mathematically coupled), else in the fixed base.</summary>
        public bool Coupled;
        /// <summary>Turntable only: number of the turntable in MACHINE_DEF of the controller, for the coupled base.</summary>
        public int Machine = 2;
        /// <summary>Size of the drawing: rail = width of the beam, turntable = radius of the table (mm). 0 = a sensible size.</summary>
        public double Size;

        public bool IsRail => Kind == ExternalAxisKind.Rail;
        public string Unit => IsRail ? "mm" : "degrees";
        public bool Limited => IsRail || !Endless;

        public static ExternalAxis Rail(Vector3d direction, double min, double max, double maxSpeed = 1000, string name = "E1")
        {
            var x = direction; if (!x.Unitize()) x = Vector3d.XAxis;
            var z = Vector3d.ZAxis - x * (Vector3d.ZAxis * x); if (!z.Unitize()) z = Vector3d.YAxis;
            return new ExternalAxis { Kind = ExternalAxisKind.Rail, Name = name, Place = new Frame { X = x, Y = Vector3d.CrossProduct(z, x), Z = z, O = Point3d.Origin }, Min = min, Max = max, MaxSpeed = maxSpeed };
        }

        public static ExternalAxis Turntable(Point3d centre, double min, double max, bool endless, double maxSpeed = 90, string name = "E2")
            => new ExternalAxis { Kind = ExternalAxisKind.Turntable, Name = name, Place = new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = centre }, Min = min, Max = max, Endless = endless, MaxSpeed = maxSpeed };

        public Vector3d Direction { get { var d = Place.X; d.Unitize(); return d; } }
        public Vector3d AxisZ { get { var d = Place.Z; d.Unitize(); return d; } }

        public bool InRange(double v) => !Limited || (v >= Min - 1e-6 && v <= Max + 1e-6);

        /// <summary>What the axis does at this value, as a frame: a shift for a rail, a turn about the table axis for a turntable.</summary>
        public Frame Motion(double value)
        {
            if (IsRail) return new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = Point3d.Origin + Direction * value };
            return Rotation(Place.O, AxisZ, value * Math.PI / 180.0);
        }

        /// <summary>A turn about an axis through a point, as a frame (Rodrigues).</summary>
        public static Frame Rotation(Point3d c, Vector3d k, double radians)
        {
            double co = Math.Cos(radians), si = Math.Sin(radians);
            Func<Vector3d, Vector3d> rot = v => v * co + Vector3d.CrossProduct(k, v) * si + k * ((k * v) * (1 - co));
            var f = new Frame { X = rot(Vector3d.XAxis), Y = rot(Vector3d.YAxis), Z = rot(Vector3d.ZAxis) };
            Vector3d cv = c - Point3d.Origin;
            f.O = Point3d.Origin + (cv - rot(cv));
            return f;
        }

        public override string ToString()
        {
            var ci = CultureInfo.InvariantCulture;
            string range = Limited ? string.Format(ci, "{0:0.#}..{1:0.#} {2}", Min, Max, Unit) : "endless";
            return string.Format(ci, "{0} {1}, {2}, up to {3:0.#} {4}/s", Name, IsRail ? "rail" : "turntable", range, MaxSpeed, IsRail ? "mm" : "degrees");
        }
    }

    /// <summary>The values the external axes take along a program, and how much of them is used.</summary>
    public class ExternalAxisPlan
    {
        /// <summary>Per move, per axis (in the order of the cell's list).</summary>
        public List<double[]> Values = new List<double[]>();
        public double[] UsedMin, UsedMax, TopSpeed, Lag;
        /// <summary>Moves at which an axis wanted to go beyond its range.</summary>
        public int[] Clamped;
        public List<string> Report = new List<string>();
    }

    /// <summary>
    /// Chooses the values of the external axes for every move of a toolpath.
    ///  - Turntable: turns the part so that the point being printed faces the robot (at a fixed
    ///    angle), so the arm barely moves. Followed round without 360 degree jumps.
    ///  - Rail: moves the robot along the rail so it stands beside the point being printed
    ///    (the point square to the rail from the robot foot, plus Offset): the arm works at the
    ///    shortest distance the rail allows.
    /// Both are averaged over Smoothing mm of path, kept below their top speed at the print
    /// speed, and kept inside their range.
    /// </summary>
    public static class ExternalAxisSolver
    {
        public static ExternalAxisPlan Solve(Toolpath tp, RobotCell cell, IList<double> speedMmPerMin)
        {
            var plan = new ExternalAxisPlan();
            var axes = cell.ExternalAxes ?? new List<ExternalAxis>();
            int n = tp.Count, m = axes.Count;
            plan.UsedMin = new double[m]; plan.UsedMax = new double[m]; plan.TopSpeed = new double[m]; plan.Lag = new double[m]; plan.Clamped = new int[m];
            for (int i = 0; i < n; i++) plan.Values.Add(new double[m]);
            if (n == 0 || m == 0) return plan;

            // path length and time of every move
            var s = new double[n]; var dt = new double[n];
            for (int i = 1; i < n; i++)
            {
                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                s[i] = s[i - 1] + len;
                double f = speedMmPerMin != null && i < speedMmPerMin.Count && speedMmPerMin[i] > 0 ? speedMmPerMin[i] : Math.Max(1, tp.Settings.Speed);
                dt[i] = len / (f / 60.0);
            }

            var world = new Point3d[n];
            for (int i = 0; i < n; i++) world[i] = tp.Points[i];

            // turntables first: they decide where the point is in the room
            for (int k = 0; k < m; k++)
            {
                var ax = axes[k];
                if (ax.IsRail) continue;
                Vector3d z = ax.AxisZ, ex = ax.Place.X - z * (ax.Place.X * z); if (!ex.Unitize()) ex = Vector3d.XAxis;
                Vector3d ey = Vector3d.CrossProduct(z, ex);
                Point3d c = ax.Place.O;
                Vector3d toRobot = cell.Arm.Base.O - c;
                double robotAngle = Math.Abs(toRobot * ex) + Math.Abs(toRobot * ey) > 1e-6 ? Math.Atan2(toRobot * ey, toRobot * ex) * 180 / Math.PI : 0;
                double want = robotAngle + ax.Offset;
                var ideal = new double[n]; var w = new double[n];
                double prev = double.NaN;
                double refRadius = 20;
                for (int i = 0; i < n; i++)
                {
                    Vector3d q = world[i] - c;
                    double x = q * ex, y = q * ey, r = Math.Sqrt(x * x + y * y);
                    if (r < 1e-3) { ideal[i] = double.IsNaN(prev) ? 0 : prev; w[i] = 0; continue; }
                    double th = want - Math.Atan2(y, x) * 180 / Math.PI;
                    if (!double.IsNaN(prev)) th -= 360 * Math.Round((th - prev) / 360);
                    ideal[i] = th; w[i] = Math.Min(1, r / refRadius); prev = th;
                }
                // fill the start (points on the axis before the first real one)
                int first = Array.FindIndex(w, v => v > 0);
                if (first > 0) for (int i = 0; i < first; i++) ideal[i] = ideal[first];
                // the whole run shifted by full turns so it sits in the middle of the range (or near 0 when endless)
                double lo = ideal.Min(), hi = ideal.Max(), centre = ax.Endless ? 0 : (ax.Min + ax.Max) / 2;
                double shift = -360 * Math.Round(((lo + hi) / 2 - centre) / 360);
                for (int i = 0; i < n; i++) ideal[i] += shift;
                Fit(ax, k, ideal, w, s, dt, plan);
                for (int i = 0; i < n; i++) world[i] = ax.Motion(plan.Values[i][k]).Pt(world[i]);
            }

            // then the rails: the robot follows the point
            for (int k = 0; k < m; k++)
            {
                var ax = axes[k];
                if (!ax.IsRail) continue;
                Vector3d d = ax.Direction; Point3d foot = cell.Arm.Base.O;
                var ideal = new double[n]; var w = new double[n];
                for (int i = 0; i < n; i++) { ideal[i] = (world[i] - foot) * d + ax.Offset; w[i] = 1; }
                Fit(ax, k, ideal, w, s, dt, plan);
            }

            // ---- report
            for (int k = 0; k < m; k++)
            {
                var ax = axes[k];
                var ci = CultureInfo.InvariantCulture;
                string u = ax.IsRail ? "mm" : "degrees";
                string range = ax.Limited ? string.Format(ci, " of {0:0.#}..{1:0.#} {2}", ax.Min, ax.Max, u) : " (endless)";
                string turns = !ax.IsRail && plan.UsedMax[k] - plan.UsedMin[k] > 360 ? string.Format(ci, ", {0:0.#} turns", (plan.UsedMax[k] - plan.UsedMin[k]) / 360) : "";
                string speed = ax.MaxSpeed > 0 ? string.Format(ci, " ({0:0} % of {1:0.#})", plan.TopSpeed[k] / ax.MaxSpeed * 100, ax.MaxSpeed) : "";
                plan.Report.Add(string.Format(ci, "{0} {1}: {2:0.#}..{3:0.#} {4} used{5}{6}, top speed {7:0.#} {8}/s{9}.",
                    ax.Name, ax.IsRail ? "rail" : "turntable", plan.UsedMin[k], plan.UsedMax[k], u, range, turns, plan.TopSpeed[k], ax.IsRail ? "mm" : "degrees", speed));
                if (plan.Clamped[k] > 0)
                    plan.Report.Add(ax.IsRail
                        ? $"{ax.Name}: at {plan.Clamped[k]} point(s) the rail would have to go beyond its range; it stops at its end and the arm has to reach the rest."
                        : $"{ax.Name}: at {plan.Clamped[k]} point(s) the table would have to turn beyond its range; it stops there and the arm has to reach the rest. A table that turns without end (Endless) avoids it.");
                double lagLimit = ax.IsRail ? 100 : 10;
                if (plan.Lag[k] > lagLimit)
                    plan.Report.Add(string.Format(ci, "{0}: too slow to follow the path, it trails up to {1:0} {2} behind. The arm makes up for it if it can; a higher top speed or a lower print speed helps.", ax.Name, plan.Lag[k], u));
            }
            return plan;
        }

        /// <summary>Smooth, keep below the top speed, keep in range; then note what was used.</summary>
        private static void Fit(ExternalAxis ax, int k, double[] ideal, double[] w, double[] s, double[] dt, ExternalAxisPlan plan)
        {
            int n = ideal.Length;
            var v = (double[])ideal.Clone();
            double half = Math.Max(0, ax.Smoothing) / 2;
            if (half > 1e-6) { v = BoxFilter(v, w, s, half); v = BoxFilter(v, w, s, half); }

            // keep in range: a turn beyond the range is held at the end
            if (ax.Limited)
                for (int i = 0; i < n; i++)
                {
                    if (ideal[i] < ax.Min - 1e-6 || ideal[i] > ax.Max + 1e-6) plan.Clamped[k]++;
                    v[i] = Math.Max(ax.Min, Math.Min(ax.Max, v[i]));
                }

            // top speed (with 5 % to spare): a pass forward and one backward, then their mean:
            // both keep the speed, so their mean does too, and it neither leads nor trails
            if (ax.MaxSpeed > 0)
            {
                var fw = (double[])v.Clone(); var bw = (double[])v.Clone();
                for (int i = 1; i < n; i++) { double c = 0.95 * ax.MaxSpeed * dt[i]; fw[i] = Math.Max(fw[i - 1] - c, Math.Min(fw[i - 1] + c, fw[i])); }
                for (int i = n - 2; i >= 0; i--) { double c = 0.95 * ax.MaxSpeed * dt[i + 1]; bw[i] = Math.Max(bw[i + 1] - c, Math.Min(bw[i + 1] + c, bw[i])); }
                for (int i = 0; i < n; i++) v[i] = 0.5 * (fw[i] + bw[i]);
            }

            double lo = double.MaxValue, hi = double.MinValue, top = 0, lag = 0;
            for (int i = 0; i < n; i++)
            {
                plan.Values[i][k] = v[i];
                lo = Math.Min(lo, v[i]); hi = Math.Max(hi, v[i]);
                if (i > 0 && dt[i] > 1e-6) top = Math.Max(top, Math.Abs(v[i] - v[i - 1]) / dt[i]);
                double want = ax.Limited ? Math.Max(ax.Min, Math.Min(ax.Max, ideal[i])) : ideal[i];
                if (w[i] > 0.5) lag = Math.Max(lag, Math.Abs(v[i] - want));
            }
            plan.UsedMin[k] = lo; plan.UsedMax[k] = hi; plan.TopSpeed[k] = top; plan.Lag[k] = lag;
        }

        /// <summary>Mean over the path from s - half to s + half, each point counted by its share of path length and its weight.</summary>
        private static double[] BoxFilter(double[] v, double[] w, double[] s, double half)
        {
            int n = v.Length;
            var len = new double[n];
            for (int i = 0; i < n; i++) len[i] = ((i + 1 < n ? s[i + 1] : s[i]) - (i > 0 ? s[i - 1] : s[i])) / 2 + 1e-6;
            var sw = new double[n + 1]; var swv = new double[n + 1];
            for (int i = 0; i < n; i++) { double g = w[i] * len[i]; sw[i + 1] = sw[i] + g; swv[i + 1] = swv[i] + g * v[i]; }
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                // the window stays symmetric near the ends of the path, so a steady turn (a ramp)
                // is kept as it is and the axis does not trail at the start or lead at the end
                double h = Math.Min(half, Math.Min(s[i] - s[0], s[n - 1] - s[i]));
                int a = LowerBound(s, s[i] - h), b = UpperBound(s, s[i] + h) - 1;
                if (b < a) { o[i] = v[i]; continue; }
                double wsum = sw[b + 1] - sw[a];
                o[i] = wsum > 1e-9 ? (swv[b + 1] - swv[a]) / wsum : v[i];
            }
            return o;
        }

        private static int LowerBound(double[] s, double x)
        {
            int lo = 0, hi = s.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (s[mid] < x - 1e-9) lo = mid + 1; else hi = mid; }
            return lo;
        }

        private static int UpperBound(double[] s, double x)
        {
            int lo = 0, hi = s.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (s[mid] <= x + 1e-9) lo = mid + 1; else hi = mid; }
            return lo;
        }
    }

    /// <summary>Simple 3D models of the external axes: a beam with a carriage for a rail, a round table for a turntable.</summary>
    public static class ExternalAxisGeometry
    {
        /// <summary>The axes at these values: for a rail the beam (fixed) and the carriage under the robot, for a turntable the table turned.</summary>
        public static List<Mesh> Pose(RobotCell cell, double[] values)
        {
            var list = new List<Mesh>();
            if (cell?.ExternalAxes == null) return list;
            for (int k = 0; k < cell.ExternalAxes.Count; k++)
            {
                var ax = cell.ExternalAxes[k];
                double v = values != null && k < values.Length ? values[k] : 0;
                if (ax.IsRail)
                {
                    Vector3d d = ax.Direction, up = cell.Arm.Base.Z; up.Unitize();
                    Vector3d side = Vector3d.CrossProduct(up, d); if (!side.Unitize()) side = Vector3d.YAxis;
                    up = Vector3d.CrossProduct(d, side);
                    double width = ax.Size > 0 ? ax.Size : 600, carriage = 100, beam = 150;
                    Point3d foot = cell.Arm.Base.O;
                    double lo = Math.Min(ax.Min, ax.Max) - width / 2 - 200, hi = Math.Max(ax.Min, ax.Max) + width / 2 + 200;
                    var beamFrame = new Frame { X = d, Y = side, Z = up, O = foot - up * carriage };
                    list.Add(Box(beamFrame, lo, hi, -width / 2, width / 2, -beam, 0));
                    var carFrame = new Frame { X = d, Y = side, Z = up, O = foot + d * v };
                    double c = width * 1.5;
                    list.Add(Box(carFrame, -c / 2, c / 2, -c / 2, c / 2, -carriage, 0));
                }
                else
                {
                    double r = ax.Size > 0 ? ax.Size : 600, h = 120;
                    var f = ax.Place;
                    Vector3d z = ax.AxisZ, x = f.X - z * (f.X * z); if (!x.Unitize()) x = Vector3d.XAxis;
                    var table = new Frame { X = x, Y = Vector3d.CrossProduct(z, x), Z = z, O = f.O };
                    var turned = ax.Motion(v).Times(table);
                    list.Add(Cylinder(turned, r, -h, 0, 64));
                    // a mark on the edge, to see it turn
                    list.Add(Box(turned, r * 0.75, r * 0.95, -r * 0.05, r * 0.05, 0, 8));
                    // the stand under it does not turn
                    list.Add(Cylinder(table, r * 0.5, -h - 500, -h, 32));
                }
            }
            return list;
        }

        private static Mesh Box(Frame f, double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var m = new Mesh();
            for (int k = 0; k < 8; k++)
                m.Vertices.Add(f.Pt(new Point3d((k & 1) == 0 ? x0 : x1, (k & 2) == 0 ? y0 : y1, (k & 4) == 0 ? z0 : z1)));
            m.Faces.AddFace(0, 2, 3, 1); m.Faces.AddFace(4, 5, 7, 6);
            m.Faces.AddFace(0, 1, 5, 4); m.Faces.AddFace(2, 6, 7, 3);
            m.Faces.AddFace(0, 4, 6, 2); m.Faces.AddFace(1, 3, 7, 5);
            m.Normals.ComputeNormals();
            return m;
        }

        private static Mesh Cylinder(Frame f, double r, double z0, double z1, int seg)
        {
            var m = new Mesh();
            for (int i = 0; i < seg; i++)
            {
                double a = 2 * Math.PI * i / seg;
                m.Vertices.Add(f.Pt(new Point3d(r * Math.Cos(a), r * Math.Sin(a), z0)));
                m.Vertices.Add(f.Pt(new Point3d(r * Math.Cos(a), r * Math.Sin(a), z1)));
            }
            int bottom = m.Vertices.Add(f.Pt(new Point3d(0, 0, z0)));
            int top = m.Vertices.Add(f.Pt(new Point3d(0, 0, z1)));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                m.Faces.AddFace(2 * i, 2 * j, 2 * j + 1, 2 * i + 1);
                m.Faces.AddFace(top, 2 * i + 1, 2 * j + 1);
                m.Faces.AddFace(bottom, 2 * j, 2 * i);
            }
            m.Normals.ComputeNormals();
            return m;
        }
    }

    /// <summary>Makes an external axis from the inputs of the component (shared by Grasshopper 1 and 2).</summary>
    public static class ExternalAxisMaker
    {
        public static ExternalAxis Make(int kind, Plane place, double min, double max, double speed, string name, bool endless, double offset, double smooth, bool coupled, double size, out string error)
        {
            error = null;
            if (kind < 0 || kind > 1) { error = "Kind is 0 (rail) or 1 (turntable)."; return null; }
            if (!place.IsValid) { error = "The plane is not valid."; return null; }
            bool rail = kind == 0;
            if (min == 0 && max == 0) { if (rail) max = 3000; else { min = -360; max = 360; } }
            if (max <= min) { error = "Max must be larger than Min."; return null; }
            if (speed < 0 || smooth < 0 || size < 0) { error = "Max Speed, Smoothing and Size cannot be negative."; return null; }
            if (speed == 0) speed = rail ? 1000 : 90;
            name = string.IsNullOrWhiteSpace(name) ? (rail ? "E1" : "E2") : name.Trim().ToUpperInvariant();
            if (name.Length != 2 || name[0] != 'E' || name[1] < '1' || name[1] > '6') { error = "The name must be E1, E2, E3, E4, E5 or E6, as in the KUKA program."; return null; }
            var ax = rail ? ExternalAxis.Rail(place.XAxis, min, max, speed, name) : ExternalAxis.Turntable(place.Origin, min, max, endless, speed, name);
            if (!rail) ax.Place = Frame.FromPlane(place);
            ax.Offset = offset; ax.Smoothing = smooth; ax.Coupled = !rail && coupled; ax.Size = size; ax.Endless = !rail && endless;
            return ax;
        }
    }

}
