using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// A position and orientation: three unit axes and an origin. Plain numbers, so the robot
    /// maths below is the same inside and outside Rhino.
    /// </summary>
    public struct Frame
    {
        public Vector3d X, Y, Z;
        public Point3d O;

        public static Frame World => new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = Point3d.Origin };

        public static Frame FromPlane(Plane p) => new Frame { X = p.XAxis, Y = p.YAxis, Z = p.ZAxis, O = p.Origin };

        public Plane ToPlane()
        {
            var p = Plane.WorldXY;
            p.Origin = O; p.XAxis = X; p.YAxis = Y; p.ZAxis = Z;
            return p;
        }

        /// <summary>this * other: the other frame, given in this frame's coordinates, seen from outside.</summary>
        public Frame Times(Frame b) => new Frame { X = Dir(b.X), Y = Dir(b.Y), Z = Dir(b.Z), O = Pt(b.O) };

        public Vector3d Dir(Vector3d v) => X * v.X + Y * v.Y + Z * v.Z;
        public Point3d Pt(Point3d p) => O + X * p.X + Y * p.Y + Z * p.Z;

        public Frame Inverse()
        {
            Vector3d o = O - Point3d.Origin;
            return new Frame
            {
                X = new Vector3d(X.X, Y.X, Z.X), Y = new Vector3d(X.Y, Y.Y, Z.Y), Z = new Vector3d(X.Z, Y.Z, Z.Z),
                O = new Point3d(-(X * o), -(Y * o), -(Z * o)),
            };
        }

        /// <summary>Turned about its own Z axis.</summary>
        public Frame TurnedAboutZ(double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new Frame { X = X * c + Y * s, Y = Y * c - X * s, Z = Z, O = O };
        }

        /// <summary>KUKA angles in degrees: A about Z, then B about the new Y, then C about the new X.</summary>
        public double[] Abc()
        {
            double a = Math.Atan2(X.Y, X.X);
            double b = Math.Atan2(-X.Z, Math.Sqrt(Y.Z * Y.Z + Z.Z * Z.Z));
            double c = Math.Atan2(Y.Z, Z.Z);
            if (Math.Abs(Math.Abs(b) - Math.PI / 2) < 1e-9) { a = Math.Atan2(-Y.X, Y.Y); c = 0; } // B = +-90: A and C turn about the same axis
            const double deg = 180.0 / Math.PI;
            return new[] { a * deg, b * deg, c * deg };
        }

        public static Frame FromAbc(Point3d origin, double aDeg, double bDeg, double cDeg)
        {
            const double rad = Math.PI / 180.0;
            double ca = Math.Cos(aDeg * rad), sa = Math.Sin(aDeg * rad), cb = Math.Cos(bDeg * rad), sb = Math.Sin(bDeg * rad), cc = Math.Cos(cDeg * rad), sc = Math.Sin(cDeg * rad);
            return new Frame
            {
                X = new Vector3d(ca * cb, sa * cb, -sb),
                Y = new Vector3d(ca * sb * sc - sa * cc, sa * sb * sc + ca * cc, cb * sc),
                Z = new Vector3d(ca * sb * cc + sa * sc, sa * sb * cc - ca * sc, cb * cc),
                O = origin,
            };
        }
    }

    /// <summary>
    /// A six-axis arm with a spherical wrist (the usual industrial robot), described the way the
    /// Robots plug-in describes it: per axis a link length a, an offset d, the range and the top
    /// speed. The file format of its robot library can be read directly.
    /// </summary>
    public class RobotArm
    {
        public string Name = "Robot";
        public string Manufacturer = "KUKA";
        public double[] A = new double[6], D = new double[6];
        public double[] MinDeg = new double[6], MaxDeg = new double[6], MaxSpeed = new double[6];
        /// <summary>Where the robot stands, in world coordinates.</summary>
        public Frame Base = Frame.World;
        public double Payload;

        private static readonly double[] Alpha = { Math.PI / 2, 0, Math.PI / 2, -Math.PI / 2, Math.PI / 2, 0 };

        /// <summary>The KUKA KR120 R2700-2 of the FH Münster cell (values of the Robots library file).</summary>
        public static RobotArm Kr120R2700()
        {
            return new RobotArm
            {
                Name = "KR120_R2700_2", Manufacturer = "KUKA", Payload = 120,
                A = new double[] { 330, 1150, 115, 0, 0, 0 },
                D = new double[] { 645, 0, 0, 1220, 0, 215 },
                MinDeg = new double[] { -185, -140, -120, -350, -125, -350 },
                MaxDeg = new double[] { 185, -5, 168, 350, 125, 350 },
                MaxSpeed = new double[] { 120, 115, 120, 190, 180, 260 },
            };
        }

        /// <summary>Reads a robot from the XML of the Robots plug-in library. cell: name of the RobotCell, empty = the first one.</summary>
        public static RobotArm FromXml(string xml, string cell = null)
        {
            var ci = CultureInfo.InvariantCulture;
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (Exception e) { throw new ArgumentException("The text is not valid XML: " + e.Message); }
            var cells = doc.Descendants("RobotCell").ToList();
            if (cells.Count == 0) throw new ArgumentException("No RobotCell found. Use a file of the Robots plug-in library.");
            var xc = string.IsNullOrWhiteSpace(cell) ? cells[0] : cells.FirstOrDefault(c => string.Equals((string)c.Attribute("name"), cell, StringComparison.OrdinalIgnoreCase));
            if (xc == null) throw new ArgumentException($"RobotCell '{cell}' not found. The file has: {string.Join(", ", cells.Select(c => (string)c.Attribute("name")))}.");
            var xa = xc.Descendants("RobotArm").FirstOrDefault();
            if (xa == null) throw new ArgumentException("The RobotCell has no RobotArm.");
            var joints = xa.Descendants("Revolute").OrderBy(j => (int)j.Attribute("number")).ToList();
            if (joints.Count != 6) throw new ArgumentException($"A six-axis arm is needed; this one has {joints.Count} revolute joints.");
            Func<XElement, string, double> num = (e, n) =>
            {
                var at = e.Attribute(n);
                if (at == null) throw new ArgumentException($"Attribute '{n}' is missing on <{e.Name}>.");
                return double.Parse(at.Value, NumberStyles.Float, ci);
            };
            var arm = new RobotArm
            {
                Name = (string)xa.Attribute("model") ?? (string)xc.Attribute("name") ?? "Robot",
                Manufacturer = (string)xa.Attribute("manufacturer") ?? (string)xc.Attribute("manufacturer") ?? "",
                Payload = xa.Attribute("payload") != null ? num(xa, "payload") : 0,
            };
            for (int i = 0; i < 6; i++)
            {
                arm.A[i] = num(joints[i], "a"); arm.D[i] = num(joints[i], "d");
                arm.MinDeg[i] = num(joints[i], "minrange"); arm.MaxDeg[i] = num(joints[i], "maxrange");
                arm.MaxSpeed[i] = joints[i].Attribute("maxspeed") != null ? num(joints[i], "maxspeed") : 0;
            }
            var xb = xa.Element("Base");
            if (xb != null)
            {
                // position and unit quaternion (q1 = scalar part)
                double w = num(xb, "q1"), x = num(xb, "q2"), y = num(xb, "q3"), z = num(xb, "q4");
                double n2 = Math.Sqrt(w * w + x * x + y * y + z * z);
                if (n2 > 1e-12) { w /= n2; x /= n2; y /= n2; z /= n2; } else { w = 1; x = y = z = 0; }
                arm.Base = new Frame
                {
                    X = new Vector3d(1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)),
                    Y = new Vector3d(2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)),
                    Z = new Vector3d(2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)),
                    O = new Point3d(num(xb, "x"), num(xb, "y"), num(xb, "z")),
                };
            }
            if (!arm.HasSphericalWrist) throw new ArgumentException("This arm has no spherical wrist (the last three axes do not meet in one point); it cannot be calculated here.");
            return arm;
        }

        public bool HasSphericalWrist => Math.Abs(D[2]) < 1e-9 && Math.Abs(A[3]) < 1e-9 && Math.Abs(A[4]) < 1e-9 && Math.Abs(D[4]) < 1e-9 && Math.Abs(A[5]) < 1e-9 && Math.Abs(A[1]) > 1e-9;

        /// <summary>Longest distance from the base axis the wrist centre can reach.</summary>
        public double Reach => A[0] + A[1] + Math.Sqrt(A[2] * A[2] + D[3] * D[3]) + D[5];

        // KUKA counts its axes the other way round than the model, and axis 3 from a different zero
        public static double ToDegrees(double radians, int axis) => (axis == 2 ? -radians + Math.PI / 2 : -radians) * 180.0 / Math.PI;
        public static double ToRadians(double degrees, int axis) => axis == 2 ? -(degrees * Math.PI / 180.0 - Math.PI / 2) : -(degrees * Math.PI / 180.0);

        /// <summary>Flange position for axis angles in degrees (as on the pendant), in the robot's own coordinates.</summary>
        public Frame Forward(double[] degrees)
        {
            Frame t = Frame.World;
            for (int i = 0; i < 6; i++)
            {
                double q = ToRadians(degrees[i], i), c = Math.Cos(q), s = Math.Sin(q), ca = Math.Cos(Alpha[i]), sa = Math.Sin(Alpha[i]);
                var link = new Frame
                {
                    X = new Vector3d(c, s, 0), Y = new Vector3d(-s * ca, c * ca, sa), Z = new Vector3d(s * sa, -c * sa, ca),
                    O = new Point3d(A[i] * c, A[i] * s, D[i]),
                };
                t = t.Times(link);
            }
            return t;
        }

        /// <summary>
        /// Axis angles in degrees that put the flange on the target (in the robot's own
        /// coordinates). shoulder / elbow / wrist choose one of the eight ways to get there.
        /// </summary>
        public double[] Inverse(Frame target, bool shoulder, bool elbow, bool wrist, out bool outOfReach, out bool singular)
        {
            outOfReach = false; singular = false;
            double a1 = A[0], a2 = -A[2], b = -D[1], c1 = D[0], c2 = A[1], c3 = D[3], c4 = D[5];
            Point3d c = target.O - target.Z * c4; // wrist centre
            double nx1 = Math.Sqrt(Math.Max(0, c.X * c.X + c.Y * c.Y - b * b)) - a1;
            double tmp1 = Math.Atan2(c.Y, c.X), tmp2 = Math.Atan2(b, nx1 + a1);
            var q = new double[6];
            q[0] = shoulder ? tmp1 + tmp2 - Math.PI : tmp1 - tmp2;

            double h = c.Z - c1, kappa2 = a2 * a2 + c3 * c3, c22 = c2 * c2;
            double s1_2 = nx1 * nx1 + h * h, s2x = nx1 + 2 * a1, s2_2 = s2x * s2x + h * h;
            Func<double, double> acos = v => { if (v > 1 + 1e-9 || v < -1 - 1e-9 || double.IsNaN(v)) return double.NaN; return Math.Acos(Math.Max(-1, Math.Min(1, v))); };
            double reachSq = shoulder ? s2_2 : s1_2;
            double t2 = acos((reachSq + c22 - kappa2) / (2 * Math.Sqrt(reachSq) * c2));
            double t3 = acos((reachSq - c22 - kappa2) / (2 * c2 * Math.Sqrt(kappa2)));
            if (double.IsNaN(t2)) { t2 = 0; outOfReach = true; }
            if (double.IsNaN(t3)) { t3 = 0; outOfReach = true; }
            double psi = Math.Atan2(a2, c3);
            if (!shoulder)
            {
                double g = Math.Atan2(nx1, h);
                q[1] = elbow ? t2 + g : -t2 + g;
                q[2] = elbow ? -t3 - psi : t3 - psi;
            }
            else
            {
                double g = Math.Atan2(s2x, h);
                q[1] = elbow ? -t2 - g : t2 - g;
                q[2] = elbow ? t3 - psi : -t3 - psi;
            }

            double s0 = Math.Sin(q[0]), c0 = Math.Cos(q[0]), s23 = Math.Sin(q[1] + q[2]), c23 = Math.Cos(q[1] + q[2]);
            double m02 = target.Z.X, m12 = target.Z.Y, m22 = target.Z.Z, m00 = target.X.X, m10 = target.X.Y, m20 = target.X.Z, m01 = target.Y.X, m11 = target.Y.Y, m21 = target.Y.Z;
            double ex = m02 * s23 * c0 + m12 * s23 * s0 + m22 * c23;
            double ey = m02 * c23 * c0 + m12 * c23 * s0 - m22 * s23;
            double ez = m12 * c0 - m02 * s0;
            q[4] = Math.Atan2(Math.Sqrt(ey * ey + ez * ez), ex);
            if (wrist) q[4] = -q[4];
            if (Math.Min(Math.Abs(q[4]), Math.PI - Math.Abs(q[4])) < 0.0124)
            {
                // axes 4 and 6 in line: only their sum is fixed. Keep axis 4 at 0.
                singular = true;
                q[3] = 0;
                var yAxis = new Vector3d(-Math.Sin(q[0]), Math.Cos(q[0]), 0);
                var xAxis = Vector3d.CrossProduct(yAxis, target.Z);
                var tx = target.X;
                q[5] = Math.Atan2(yAxis * tx, xAxis * tx);
            }
            else
            {
                q[3] = Math.Atan2(ez, ey);
                double y6 = m01 * s23 * c0 + m11 * s23 * s0 + m21 * c23;
                double x6 = -m00 * s23 * c0 - m10 * s23 * s0 - m20 * c23;
                q[5] = Math.Atan2(y6, x6);
            }
            if (wrist) { q[3] += Math.PI; q[5] -= Math.PI; }

            double[] start = { 0, Math.PI / 2, Math.PI / 2, 0, 0, Math.PI }, sign = { 1, -1, -1, 1, -1, 1 };
            var deg = new double[6];
            for (int i = 0; i < 6; i++)
            {
                double v = sign[i] * q[i] + start[i];
                while (v > Math.PI) v -= 2 * Math.PI;
                while (v < -Math.PI) v += 2 * Math.PI;
                if (double.IsNaN(v) || double.IsInfinity(v)) { v = 0; outOfReach = true; }
                deg[i] = ToDegrees(v, i);
            }
            return deg;
        }

        /// <summary>The arm as a line through its joints, in world coordinates: for a quick look at a pose.</summary>
        public List<Point3d> Skeleton(double[] degrees)
        {
            var pts = new List<Point3d> { Base.O };
            Frame t = Frame.World;
            for (int i = 0; i < 6; i++)
            {
                double q = ToRadians(degrees[i], i), c = Math.Cos(q), s = Math.Sin(q), ca = Math.Cos(Alpha[i]), sa = Math.Sin(Alpha[i]);
                // up the offset d first, then out along the link a: shows the real shape of the arm
                pts.Add(Base.Pt(t.Pt(new Point3d(0, 0, D[i]))));
                t = t.Times(new Frame { X = new Vector3d(c, s, 0), Y = new Vector3d(-s * ca, c * ca, sa), Z = new Vector3d(s * sa, -c * sa, ca), O = new Point3d(A[i] * c, A[i] * s, D[i]) });
                pts.Add(Base.Pt(t.O));
            }
            return pts;
        }

        public override string ToString() => $"{Manufacturer} {Name} (reach {Reach:0} mm)";
    }

    /// <summary>The tool on the flange: where its tip is, and what it weighs.</summary>
    public class RobotTool
    {
        public string Name = "Extruder";
        /// <summary>Tool centre point relative to the flange; its Z axis points out of the nozzle.</summary>
        public Frame Tcp = Frame.World;
        public double Weight;
        public Point3d Centroid = Point3d.Origin;
        public override string ToString() => $"{Name} (TCP {Tcp.O.X:0.#}, {Tcp.O.Y:0.#}, {Tcp.O.Z:0.#}; {Weight:0.#} kg)";
    }

    /// <summary>Robot, tool and the place of the work on the table.</summary>
    public class RobotCell
    {
        public RobotArm Arm = RobotArm.Kr120R2700();
        public RobotTool Tool = new RobotTool();
        /// <summary>The frame the program counts from ($BASE), in world coordinates. Usually a corner of the print bed.</summary>
        public Frame WorkBase = Frame.World;
        public string BaseName = "Bed";
        public override string ToString() => $"{Arm}, tool {Tool.Name}";
    }

    public class RobotMove
    {
        public Frame Target;      // TCP in base coordinates
        public double[] Xyzabc;   // the six numbers of the program line
        public double[] Axes;     // axis angles in degrees
        public bool Reachable = true, InLimits = true, Singular;
    }

    public class RobotProgramResult
    {
        public List<RobotMove> Moves = new List<RobotMove>();
        public List<string> Lines = new List<string>();      // one PTP line per point, for the DXR Generator
        public List<string> Krl = new List<string>();        // a complete KRL program
        public List<double> Extrusion = new List<double>(), Speed = new List<double>();
        public List<string> Report = new List<string>();
        public bool Ok = true;
        public int OutOfReach, OverLimit, Singular;
        public double[] AxisMin = new double[6], AxisMax = new double[6];
    }

    /// <summary>
    /// Turns a toolpath into robot moves: the tool tip on every path point, the tool axis along
    /// the path's tool axis. Checks that the robot can get there (reach, axis ranges, wrist
    /// singularity) and writes the moves as lines for the DXR Generator and as a KRL program.
    /// </summary>
    public static class RobotProgram
    {
        private static string N(double v, int digits) { v = Math.Round(v, digits); if (v == 0) v = 0; return v.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture); }

        private static string XyzAbc(double[] v) => $"X {N(v[0], 3)},Y {N(v[1], 3)},Z {N(v[2], 3)},A {N(v[3], 4)},B {N(v[4], 4)},C {N(v[5], 4)}";

        /// <summary>
        /// Target frame of the tool tip: Z points along the tool, into the part (against the
        /// path's tool axis); X is world X as far as that allows, turned by `turn` degrees about the tool.
        /// </summary>
        public static Frame Target(Point3d p, Vector3d toolAxis, double turnDeg)
        {
            Vector3d z = -toolAxis; if (!z.Unitize()) z = -Vector3d.ZAxis;
            Vector3d x = Vector3d.XAxis - z * (Vector3d.XAxis * z);
            if (x.Length < 1e-6) x = Vector3d.YAxis - z * (Vector3d.YAxis * z);
            x.Unitize();
            Vector3d y = Vector3d.CrossProduct(z, x);
            var f = new Frame { X = x, Y = y, Z = z, O = p };
            return Math.Abs(turnDeg) > 1e-12 ? f.TurnedAboutZ(turnDeg * Math.PI / 180.0) : f;
        }

        public static RobotProgramResult Build(Toolpath tp, RobotCell cell, double turnDeg, string name, bool checkAxes)
        {
            var r = new RobotProgramResult();
            if (tp == null || tp.Count < 2) { r.Ok = false; r.Report.Add("Empty toolpath."); return r; }
            var pr = PathEngine.Process(tp, false);
            var arm = cell.Arm;
            Frame baseInv = cell.WorkBase.Inverse(), robotInv = arm.Base.Inverse();
            // the flange of the model looks the other way round than the flange the tool is measured from
            Frame tcpInv = Frame.World.TurnedAboutZ(Math.PI).Times(cell.Tool.Tcp).Inverse();
            for (int i = 0; i < 6; i++) { r.AxisMin[i] = double.MaxValue; r.AxisMax[i] = double.MinValue; }

            double[] prev = null; int firstBad = -1; string firstBadWhy = "";
            for (int i = 0; i < tp.Count; i++)
            {
                Frame world = Target(tp.Points[i], tp.Normals[i], turnDeg);
                Frame inBase = baseInv.Times(world);
                var abc = inBase.Abc();
                var mv = new RobotMove { Target = inBase, Xyzabc = new[] { inBase.O.X, inBase.O.Y, inBase.O.Z, abc[0], abc[1], abc[2] } };

                if (checkAxes)
                {
                    Frame flange = robotInv.Times(world).Times(tcpInv);
                    double[] best = null; double bestCost = double.MaxValue; bool bestReach = false, bestLimits = false, bestSing = false;
                    for (int cfg = 0; cfg < 8; cfg++)
                    {
                        var deg = arm.Inverse(flange, (cfg & 1) != 0, (cfg & 2) != 0, (cfg & 4) != 0, out bool far, out bool sing);
                        // axes 4 and 6 can turn more than once round: stay near where they were
                        if (prev != null)
                            foreach (int ax in new[] { 3, 5 })
                                while (true)
                                {
                                    if (deg[ax] - prev[ax] > 180 && deg[ax] - 360 >= arm.MinDeg[ax]) deg[ax] -= 360;
                                    else if (deg[ax] - prev[ax] < -180 && deg[ax] + 360 <= arm.MaxDeg[ax]) deg[ax] += 360;
                                    else break;
                                }
                        bool within = true;
                        for (int ax = 0; ax < 6; ax++) if (deg[ax] < arm.MinDeg[ax] - 1e-6 || deg[ax] > arm.MaxDeg[ax] + 1e-6) within = false;
                        double cost = (far ? 1e9 : 0) + (within ? 0 : 1e6) + (prev == null ? cfg : Enumerable.Range(0, 6).Sum(ax => (deg[ax] - prev[ax]) * (deg[ax] - prev[ax])));
                        if (cost < bestCost) { bestCost = cost; best = deg; bestReach = !far; bestLimits = within; bestSing = sing; }
                    }
                    mv.Axes = best; mv.Reachable = bestReach; mv.InLimits = bestLimits; mv.Singular = bestSing;
                    if (!bestReach) { r.OutOfReach++; if (firstBad < 0) { firstBad = i; firstBadWhy = "out of reach"; } }
                    else if (!bestLimits) { r.OverLimit++; if (firstBad < 0) { firstBad = i; firstBadWhy = "outside an axis range"; } }
                    if (bestSing) r.Singular++;
                    if (bestReach) { prev = best; for (int ax = 0; ax < 6; ax++) { r.AxisMin[ax] = Math.Min(r.AxisMin[ax], best[ax]); r.AxisMax[ax] = Math.Max(r.AxisMax[ax], best[ax]); } }
                }
                r.Moves.Add(mv);
                r.Lines.Add("PTP {" + XyzAbc(mv.Xyzabc).Replace(",", ", ") + "}");
            }
            r.Extrusion.AddRange(pr.Extrusion);
            r.Speed.AddRange(pr.Speed);

            // ---- report
            r.Report.Add($"{tp.Count} moves for {arm}, tool {cell.Tool.Name}, counted from '{cell.BaseName}'.");
            if (checkAxes)
            {
                if (r.OutOfReach == 0 && r.OverLimit == 0)
                    r.Report.Add("Reach OK: the robot gets to every point within its axis ranges. Axes used: " +
                                 string.Join(", ", Enumerable.Range(0, 6).Select(ax => $"A{ax + 1} {r.AxisMin[ax]:0}..{r.AxisMax[ax]:0}")) + " degrees.");
                else
                {
                    r.Ok = false;
                    var p = tp.Points[firstBad];
                    r.Report.Add($"NOT REACHABLE: {r.OutOfReach} point(s) are out of reach and {r.OverLimit} need an axis beyond its range. First one: point {firstBad + 1} at {p.X:0}, {p.Y:0}, {p.Z:0} ({firstBadWhy}). " +
                                 "Move the part closer to the robot, turn it, or change the tool turn.");
                }
                if (r.Singular > 0)
                    r.Report.Add($"Wrist: at {r.Singular} point(s) axes 4 and 6 are in line (axis 5 near 0). The robot may slow down or stop there. A small tilt of the tool or another position of the part avoids it.");
                // how fast the axes have to turn at print speed
                double worst = 0; int worstAxis = 0;
                for (int i = 1; i < r.Moves.Count; i++)
                {
                    var a = r.Moves[i - 1].Axes; var b = r.Moves[i].Axes;
                    if (a == null || b == null || !r.Moves[i].Reachable || !r.Moves[i - 1].Reachable || pr.Speed[i] <= 0) continue;
                    double seconds = tp.Points[i].DistanceTo(tp.Points[i - 1]) / (pr.Speed[i] / 60.0);
                    if (seconds < 1e-4) continue;
                    for (int ax = 0; ax < 6; ax++)
                        if (arm.MaxSpeed[ax] > 0) { double share = Math.Abs(b[ax] - a[ax]) / seconds / arm.MaxSpeed[ax]; if (share > worst) { worst = share; worstAxis = ax; } }
                }
                if (worst > 1.0) { r.Report.Add($"Axis speed: A{worstAxis + 1} would have to turn at {worst * 100:0} % of its top speed. The robot cannot keep the print speed there (usually next to a wrist singularity)."); }
                else if (worst > 0) r.Report.Add($"Axis speed OK: at most {worst * 100:0} % of the top speed (A{worstAxis + 1}).");
            }
            else r.Report.Add("Axis ranges were not checked.");

            // ---- KRL program: one file, tool and base written out, the path as linear moves
            string prog = new string((string.IsNullOrWhiteSpace(name) ? "LARGER_PRINT" : name).Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (prog.Length == 0 || char.IsDigit(prog[0])) prog = "P" + prog;
            if (prog.Length > 24) prog = prog.Substring(0, 24);
            var tcpAbc = cell.Tool.Tcp.Abc(); var bAbc = cell.WorkBase.Abc();
            var k = r.Krl;
            k.Add("&ACCESS RVP");
            k.Add("&REL 1");
            k.Add($"DEF {prog}()");
            k.Add("; LARGER.slicer - " + arm + ", tool " + cell.Tool.Name);
            k.Add("BAS (#INITMOV,0)");
            k.Add("$ADVANCE = 5");
            k.Add("$TOOL = {" + XyzAbc(new[] { cell.Tool.Tcp.O.X, cell.Tool.Tcp.O.Y, cell.Tool.Tcp.O.Z, tcpAbc[0], tcpAbc[1], tcpAbc[2] }) + "}");
            if (cell.Tool.Weight > 0)
                k.Add("$LOAD = {M " + N(cell.Tool.Weight, 3) + ",CM {X " + N(cell.Tool.Centroid.X, 2) + ",Y " + N(cell.Tool.Centroid.Y, 2) + ",Z " + N(cell.Tool.Centroid.Z, 2) + ",A 0,B 0,C 0},J {X 0,Y 0,Z 0}}");
            k.Add("$BASE = {" + XyzAbc(new[] { cell.WorkBase.O.X, cell.WorkBase.O.Y, cell.WorkBase.O.Z, bAbc[0], bAbc[1], bAbc[2] }) + "}");
            k.Add("$APO.CDIS = 0.5");
            double lastSpeed = -1;
            for (int i = 0; i < r.Moves.Count; i++)
            {
                var mv = r.Moves[i];
                if (i == 0)
                {
                    k.Add("; approach in axis mode, then follow the path");
                    if (mv.Axes != null && mv.Reachable) k.Add("PTP {" + string.Join(",", mv.Axes.Select((v, ax) => $"A{ax + 1} {N(v, 4)}")) + "}");
                    else k.Add("PTP {" + XyzAbc(mv.Xyzabc) + "}");
                    continue;
                }
                double mps = Math.Max(1e-4, pr.Speed[i] / 60000.0); // mm/min -> m/s
                if (Math.Abs(mps - lastSpeed) > 1e-9) { k.Add("$VEL.CP = " + N(mps, 5)); lastSpeed = mps; }
                k.Add("LIN {" + XyzAbc(mv.Xyzabc) + "} C_DIS");
            }
            k.Add("END");
            return r;
        }
    }
}
