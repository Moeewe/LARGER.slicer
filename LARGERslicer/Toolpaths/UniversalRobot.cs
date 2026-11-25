using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// A Universal Robots arm (UR3, UR5, UR10 and their e-series), described by the Denavit-Hartenberg values
    /// UR publishes. Its wrist is not spherical (the last three axes do not meet in one point), so it is solved
    /// here on its own, not by RobotArm. The base frame is the one the controller uses for p[...] poses (with
    /// every axis at 0 the flange lies at x = a2 + a3, y = -(d4 + d6), z = d1 - d5, as the pendant shows).
    /// </summary>
    public class URModel
    {
        public string Name;
        public double D1, A2, A3, D4, D5, D6;   // mm
        public double Payload;                  // kg
        public double MaxJointSpeed = 180;      // deg/s, the slowest axes

        public double Reach => Math.Abs(A2) + Math.Abs(A3) + D6;

        public static readonly URModel[] All =
        {
            new URModel { Name = "UR5",   D1 = 89.159, A2 = -425,   A3 = -392.25, D4 = 109.15, D5 = 94.65,  D6 = 82.3,   Payload = 5 },
            new URModel { Name = "UR5e",  D1 = 162.5,  A2 = -425,   A3 = -392.2,  D4 = 133.3,  D5 = 99.7,   D6 = 99.6,   Payload = 5 },
            new URModel { Name = "UR10",  D1 = 127.3,  A2 = -612,   A3 = -572.3,  D4 = 163.941, D5 = 115.7, D6 = 92.2,   Payload = 10, MaxJointSpeed = 120 },
            new URModel { Name = "UR10e", D1 = 180.7,  A2 = -612.7, A3 = -571.55, D4 = 174.15, D5 = 119.85, D6 = 116.55, Payload = 12.5, MaxJointSpeed = 120 },
            new URModel { Name = "UR3",   D1 = 151.9,  A2 = -243.65, A3 = -213.25, D4 = 112.35, D5 = 85.35, D6 = 81.9,   Payload = 3 },
            new URModel { Name = "UR3e",  D1 = 151.85, A2 = -243.55, A3 = -213.2,  D4 = 131.05, D5 = 85.35, D6 = 92.1,   Payload = 3 },
        };

        public static URModel ByName(string name) => All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

        private double[] Alpha => new[] { Math.PI / 2, 0, 0, Math.PI / 2, -Math.PI / 2, 0 };
        private double[] A => new[] { 0, A2, A3, 0, 0, 0 };
        private double[] D => new[] { D1, 0, 0, D4, D5, D6 };

        /// <summary>The frames of the joints 1..6 (index 0 = the base) for joint angles in radians; the last is the flange.</summary>
        public Frame[] Frames(double[] q)
        {
            var f = new Frame[7];
            f[0] = Frame.World;
            var al = Alpha; var a = A; var d = D;
            for (int i = 0; i < 6; i++)
            {
                double ct = Math.Cos(q[i]), st = Math.Sin(q[i]), ca = Math.Cos(al[i]), sa = Math.Sin(al[i]);
                // DH: rotation about z by theta, along z by d, along x by a, about x by alpha
                var local = new Frame
                {
                    X = new Vector3d(ct, st, 0),
                    Y = new Vector3d(-st * ca, ct * ca, sa),
                    Z = new Vector3d(st * sa, -ct * sa, ca),
                    O = new Point3d(a[i] * ct, a[i] * st, d[i]),
                };
                f[i + 1] = f[i].Times(local);
            }
            return f;
        }

        public Frame Flange(double[] q) => Frames(q)[6];

        /// <summary>A typical start for printing on a table in front of the robot (radians): elbow up, tool down.</summary>
        public static double[] Home => new[] { 0.0, -Math.PI / 2, Math.PI / 2, -Math.PI / 2, -Math.PI / 2, 0.0 };

        /// <summary>
        /// The joint angles for a flange frame, starting from 'seed' (the pose before): damped least squares on
        /// position and orientation, so the arm stays in the configuration it is in, as the controller does
        /// with movel. Returns false when the target is not reached (out of reach, or a singular pose).
        /// </summary>
        public bool Solve(Frame target, double[] seed, out double[] q, out double posErr, out double angErr)
        {
            q = (double[])seed.Clone();
            posErr = double.MaxValue; angErr = double.MaxValue;
            double lambda = 5.0; // mm
            for (int it = 0; it < 60; it++)
            {
                var f = Flange(q);
                var e = Error(target, f);
                posErr = Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]);
                angErr = Math.Sqrt(e[3] * e[3] + e[4] * e[4] + e[5] * e[5]);
                if (posErr < 0.01 && angErr < 1e-5) return true;
                // the Jacobian by small steps of every joint (orientation in mm per radian, weighted by the reach)
                var J = new double[6, 6];
                const double h = 1e-6;
                for (int j = 0; j < 6; j++)
                {
                    var qj = (double[])q.Clone(); qj[j] += h;
                    var ej = Error(target, Flange(qj));
                    for (int r = 0; r < 6; r++) J[r, j] = (e[r] - ej[r]) / h;
                }
                // (J^T J + l^2 I) dq = J^T e
                var JtJ = new double[6, 6]; var Jte = new double[6];
                for (int r = 0; r < 6; r++)
                {
                    for (int c = 0; c < 6; c++) { double s = 0; for (int k = 0; k < 6; k++) s += J[k, r] * J[k, c]; JtJ[r, c] = s; }
                    double t = 0; for (int k = 0; k < 6; k++) t += J[k, r] * e[k]; Jte[r] = t;
                    JtJ[r, r] += lambda * lambda;
                }
                var dq = SolveLinear(JtJ, Jte);
                if (dq == null) return false;
                double step = 0; for (int j = 0; j < 6; j++) step = Math.Max(step, Math.Abs(dq[j]));
                double scale = step > 0.3 ? 0.3 / step : 1.0;
                for (int j = 0; j < 6; j++) q[j] += dq[j] * scale;
                lambda = posErr < 1 ? 0.5 : 5.0;
            }
            return posErr < 0.1 && angErr < 1e-3;
        }

        /// <summary>The difference of two frames: position (mm), then orientation (radians, times 300 mm so both weigh alike).</summary>
        private static double[] Error(Frame target, Frame f)
        {
            Vector3d dp = target.O - f.O;
            // the turn from f to the target, as a rotation vector in the base frame (cross products of the axes
            // alone vanish too where f is turned half round: the solver would stop there)
            Vector3d w = RotationVector(target.Times(f.Inverse()));
            const double k = 300;
            return new[] { dp.X, dp.Y, dp.Z, w.X * k, w.Y * k, w.Z * k };
        }

        private static double[] SolveLinear(double[,] M, double[] b)
        {
            int n = b.Length; var a = (double[,])M.Clone(); var x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                if (Math.Abs(a[p, c]) < 1e-12) return null;
                if (p != c) { for (int k = 0; k < n; k++) { var t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; } var tb = x[c]; x[c] = x[p]; x[p] = tb; }
                for (int r = c + 1; r < n; r++) { double f = a[r, c] / a[c, c]; for (int k = c; k < n; k++) a[r, k] -= f * a[c, k]; x[r] -= f * x[c]; }
            }
            for (int r = n - 1; r >= 0; r--) { double s = x[r]; for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k]; x[r] = s / a[r, r]; }
            return x;
        }

        /// <summary>A frame as a UR rotation vector (axis times angle, radians).</summary>
        public static Vector3d RotationVector(Frame f)
        {
            double r00 = f.X.X, r01 = f.Y.X, r02 = f.Z.X, r10 = f.X.Y, r11 = f.Y.Y, r12 = f.Z.Y, r20 = f.X.Z, r21 = f.Y.Z, r22 = f.Z.Z;
            double cos = Math.Max(-1, Math.Min(1, (r00 + r11 + r22 - 1) / 2));
            double ang = Math.Acos(cos);
            if (ang < 1e-9) return Vector3d.Zero;
            if (Math.PI - ang < 1e-6)
            {
                // 180 degrees: the axis from the diagonal
                double x = Math.Sqrt(Math.Max(0, (r00 + 1) / 2)), y = Math.Sqrt(Math.Max(0, (r11 + 1) / 2)), z = Math.Sqrt(Math.Max(0, (r22 + 1) / 2));
                if (x > 1e-6) { y = Math.Sign(r01) * y; z = Math.Sign(r02) * z; }
                else if (y > 1e-6) { z = Math.Sign(r12) * z; }
                return new Vector3d(x, y, z) * ang;
            }
            var axis = new Vector3d(r21 - r12, r02 - r20, r10 - r01) / (2 * Math.Sin(ang));
            return axis * ang;
        }
    }

    /// <summary>
    /// The 3D models of the UR arms as Universal Robots publishes them (ur_description, BSD-3-Clause,
    /// Copyright Universal Robots A/S; see Resources/UR/LICENSE-ur_description.txt): one mesh per link, placed
    /// by the joint chain of that description (joint origins of default_kinematics.yaml, the mesh offsets of
    /// visual_parameters.yaml already applied in the stored meshes, millimetres).
    /// </summary>
    public static class URMeshes
    {
        // per model: the origin of every joint (x, y, z in mm, roll, pitch, yaw) relative to the link before it
        private static readonly Dictionary<string, double[,]> Joints = new Dictionary<string, double[,]>(StringComparer.OrdinalIgnoreCase)
        {
            { "UR3", new double[,] { { 0.0, 0.0, 151.9, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -243.65, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -213.25, 0.0, 112.35, 0.0, 0.0, 0.0 }, { 0.0, -85.35, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 81.9, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
            { "UR3e", new double[,] { { 0.0, 0.0, 151.85, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -243.55, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -213.2, 0.0, 131.05, 0.0, 0.0, 0.0 }, { 0.0, -85.35, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 92.1, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
            { "UR5", new double[,] { { 0.0, 0.0, 89.159, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -425.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -392.25, 0.0, 109.15, 0.0, 0.0, 0.0 }, { 0.0, -94.65, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 82.3, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
            { "UR5e", new double[,] { { 0.0, 0.0, 162.5, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -425.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -392.2, 0.0, 133.3, 0.0, 0.0, 0.0 }, { 0.0, -99.7, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 99.6, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
            { "UR10", new double[,] { { 0.0, 0.0, 127.3, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -612.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -572.3, 0.0, 163.941, 0.0, 0.0, 0.0 }, { 0.0, -115.7, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 92.2, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
            { "UR10e", new double[,] { { 0.0, 0.0, 180.7, 0.0, 0.0, 0.0 }, { 0.0, 0.0, 0.0, 1.570796327, 0.0, 0.0 }, { -612.7, 0.0, 0.0, 0.0, 0.0, 0.0 }, { -571.55, 0.0, 174.15, 0.0, 0.0, 0.0 }, { 0.0, -119.85, -0.0, 1.570796327, 0.0, 0.0 }, { 0.0, 116.55, -0.0, 1.570796326589793, 3.141592653589793, 3.141592653589793 } } },
        };

        private static readonly Dictionary<string, Mesh[]> Cache = new Dictionary<string, Mesh[]>(StringComparer.OrdinalIgnoreCase);

        public static bool Has(string model) => Joints.ContainsKey(model) && Load(model) != null;

        private static Frame Rpy(double x, double y, double z, double r, double p, double w)
        {
            double cr = Math.Cos(r), sr = Math.Sin(r), cp = Math.Cos(p), sp = Math.Sin(p), cy = Math.Cos(w), sy = Math.Sin(w);
            return new Frame
            {
                X = new Vector3d(cy * cp, sy * cp, -sp),
                Y = new Vector3d(cy * sp * sr - sy * cr, sy * sp * sr + cy * cr, cp * sr),
                Z = new Vector3d(cy * sp * cr + sy * sr, sy * sp * cr - cy * sr, cp * cr),
                O = new Point3d(x, y, z),
            };
        }

        /// <summary>The frames of the base and the six links for joint angles q (radians), in the controller's base frame.</summary>
        public static Frame[] Links(string model, double[] q)
        {
            var j = Joints[model];
            var f = new Frame[7];
            f[0] = Frame.World; // base_link_inertia: turned half round from base_link, as the controller's base is
            for (int i = 0; i < 6; i++)
                f[i + 1] = f[i].Times(Rpy(j[i, 0], j[i, 1], j[i, 2], j[i, 3], j[i, 4], j[i, 5])).TurnedAboutZ(q[i]);
            return f;
        }

        /// <summary>tool0 of the description (the flange as the controller sees it), for checking against URModel.Flange.</summary>
        public static Frame Tool0(string model, double[] q)
        {
            var w3 = Links(model, q)[6];
            return w3.Times(Rpy(0, 0, 0, 0, -Math.PI / 2, -Math.PI / 2)).Times(Rpy(0, 0, 0, Math.PI / 2, 0, Math.PI / 2));
        }

        private static Mesh[] Load(string model)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(model, out var m)) return m;
                Mesh[] res = null;
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + model.ToLowerInvariant() + ".urm", StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                        using (var s = asm.GetManifestResourceStream(name))
                        using (var r = new System.IO.BinaryReader(s))
                        {
                            if (new string(r.ReadChars(4)) == "URM1")
                            {
                                int links = r.ReadInt32(); res = new Mesh[links];
                                for (int k = 0; k < links; k++)
                                {
                                    var mesh = new Mesh();
                                    int nv = r.ReadInt32();
                                    for (int v = 0; v < nv; v++) mesh.Vertices.Add(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                                    int nf = r.ReadInt32();
                                    for (int t = 0; t < nf; t++) mesh.Faces.AddFace(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                                    mesh.Normals.ComputeNormals(); mesh.Compact();
                                    res[k] = mesh;
                                }
                            }
                        }
                }
                catch { res = null; }
                Cache[model] = res;
                return res;
            }
        }

        /// <summary>The arm as UR draws it, at joint angles q, standing at 'base'.</summary>
        public static List<Mesh> Arm(string model, Frame robotBase, double[] q)
        {
            var res = new List<Mesh>();
            var meshes = Load(model);
            if (meshes == null || !Joints.ContainsKey(model)) return res;
            var f = Links(model, q);
            for (int k = 0; k < meshes.Length && k < f.Length; k++)
            {
                var w = robotBase.Times(f[k]);
                var x = Transform.PlaneToPlane(Plane.WorldXY, w.ToPlane());
                var m = meshes[k].DuplicateMesh(); m.Transform(x); res.Add(m);
            }
            return res;
        }
    }

    /// <summary>A tool on the UR flange: its tip and direction (Z = where it points), weight and centre of mass.</summary>
    public class URTool
    {
        public string Name = "Tool";
        public Frame Tcp = Frame.World;   // in the flange frame
        public double Weight;             // kg
        public Point3d Centroid;          // mm, in the flange frame
        public bool Extrudes;             // switches an extruder on and off (clay); pens and wool do not

        /// <summary>The tools of the FH Muenster UR5 (from the UR5 slicer of 2025).</summary>
        public static URTool Preset(int kind, double length)
        {
            switch (kind)
            {
                case 0: // clay extruder V2: the nozzle sits beside the flange, 171 mm out along its Y axis
                    // (the UR5 slicer of 2025 gave the TCP to the Robots plug-in as a YZ plane at 171, -2.478, 54.5;
                    // that plug-in turns every UR tool by +90 degrees about the flange axis before set_tcp, so on the
                    // robot it is this frame)
                    return new URTool
                    {
                        Name = "Lehmextruder_V2", Weight = 2.5, Extrudes = true, Centroid = new Point3d(2.5, 25, 55),
                        Tcp = new Frame { X = -Vector3d.XAxis, Y = Vector3d.ZAxis, Z = Vector3d.YAxis, O = new Point3d(2.478, 171, 54.5) },
                    };
                case 1: // pen holder: straight on, the pen length decides the tip
                    return new URTool { Name = "PenHolder", Weight = 0.15, Centroid = new Point3d(0, 0, 60), Tcp = new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = new Point3d(0, 0, length > 0 ? length : 194) } };
                case 2: // wool spinner
                    return new URTool { Name = "Faden_Nozzle", Weight = 0.1, Centroid = new Point3d(0, 0, 45), Tcp = new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = new Point3d(0, 0, length > 0 ? length : 97) } };
                default:
                    return new URTool { Name = "OwnTool", Weight = 0.5, Tcp = new Frame { X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis, O = new Point3d(0, 0, length) } };
            }
        }

        /// <summary>A TCP given as for the Robots plug-in (a plane relative to its flange), as it is on a UR: that plug-in
        /// turns every UR tool by +90 degrees about the flange axis before set_tcp.</summary>
        public static Frame FromRobotsPlugin(Frame tcp) => new Frame { X = Vector3d.YAxis, Y = -Vector3d.XAxis, Z = Vector3d.ZAxis, O = Point3d.Origin }.Times(tcp);

        public static readonly string[] Names = { "Clay extruder V2 (Lehmextruder, 2.5 kg)", "Pen holder (pen length)", "Wool spinner (97 mm)", "Own tool (length straight on)" };
    }

    /// <summary>A UR arm chosen in Robot: the model and where its foot stands. Robot Cell makes a URCell of it.</summary>
    public class URArm
    {
        public URModel Model = URModel.All[0];
        public Frame Base = Frame.World;
        public override string ToString() => "Universal Robots " + Model.Name;
    }

    /// <summary>A UR robot with its tool, where it stands, and what it prints on.</summary>
    public class URCell
    {
        public URModel Model = URModel.All[0];
        public URTool Tool = URTool.Preset(0, 0);
        public Frame Base = Frame.World;   // where the robot base is in Rhino
        public double Plate;               // thickness of the work plate on the table (mm): the print starts on top of it
        public string Ip = "192.168.1.66";
        public string StartCode, OnCode, OffCode; // null = the codes of the FH Muenster UR5

        public override string ToString() => Model.Name + " with " + Tool.Name;
    }

    /// <summary>A UR program: its script and every target with the joints the simulation shows.</summary>
    public class URProgram
    {
        /// <summary>The codes of the FH Muenster UR5: ask for the compressed air, extruder on and off over the digital outputs 0 and 1.</summary>
        public const string AirCode = "popup(\"Bitte kontrolliere, ob die Druckluft eingeschaltet ist!\", \"Druckluft an?\", warning=False, error=False, blocking=True)";
        public const string OnCode = "set_digital_out(1,False)\nset_digital_out(0,True)";
        public const string OffCode = "set_digital_out(0,False)\nset_digital_out(1,True)";

        public URCell Cell;
        public List<string> Script = new List<string>();
        public List<Frame> Targets = new List<Frame>();     // TCP in the robot base frame
        public List<double[]> Joints = new List<double[]>(); // radians
        public List<bool> Printing = new List<bool>();
        public List<double> Seconds = new List<double>();   // time at each target from the start
        public List<Point3d> PathPoints = new List<Point3d>(); // in Rhino
        public List<string> Report = new List<string>();
        public List<Point3d> Unreachable = new List<Point3d>();
        public bool Ok = true;
        public double Minutes => Seconds.Count > 0 ? Seconds[Seconds.Count - 1] / 60.0 : 0;

        private static string N(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string Pose(Frame f) { var rv = URModel.RotationVector(f); return $"p[{N(f.O.X / 1000)}, {N(f.O.Y / 1000)}, {N(f.O.Z / 1000)}, {N(rv.X)}, {N(rv.Y)}, {N(rv.Z)}]"; }

        /// <summary>
        /// The program for a print path. Every point becomes a movel with the speed of the path (or 'speed'
        /// mm/s), blended over 'blend' mm; the tool points along the layer's tool axis (down on flat layers)
        /// and is turned about it by 'turnDeg'. The extruder code goes in where printing starts and stops.
        /// </summary>
        public static URProgram Build(Toolpath tp, URCell cell, double speed, double blend, double turnDeg, double approach,
            string startCode, string onCode, string offCode, string name)
        {
            var r = new URProgram { Cell = cell };
            if (tp == null || tp.Count < 2) { r.Ok = false; r.Report.Add("No toolpath."); return r; }
            var pr = PathEngine.Process(tp, false);
            var baseInv = cell.Base.Inverse();
            var tcp = cell.Tool.Tcp; var tcpInv = tcp.Inverse();
            double turn = turnDeg * Math.PI / 180.0;
            var m = cell.Model;

            Func<int, Point3d, Frame> target = (i, p) =>
            {
                Vector3d n = tp.Normals[i]; if (!n.Unitize()) n = Vector3d.ZAxis;
                Vector3d z = -n;
                Vector3d x = Vector3d.XAxis - z * (Vector3d.XAxis * z); if (!x.Unitize()) { x = Vector3d.YAxis - z * (Vector3d.YAxis * z); x.Unitize(); }
                var f = new Frame { X = x, Y = Vector3d.CrossProduct(z, x), Z = z, O = p + new Vector3d(0, 0, cell.Plate) };
                return baseInv.Times(f.TurnedAboutZ(turn));
            };

            // ---- the script
            var s = r.Script;
            string prog = string.IsNullOrWhiteSpace(name) ? "LARGER" : new string(name.Where(char.IsLetterOrDigit).ToArray());
            if (prog.Length == 0 || char.IsDigit(prog[0])) prog = "P" + prog;
            s.Add($"def {prog}():");
            s.Add($"  # LARGER.slicer: {tp.Count} points, {m.Name}, tool {cell.Tool.Name}");
            s.Add($"  set_tcp({Pose(tcp)})");
            s.Add($"  set_payload({N(cell.Tool.Weight)}, [{N(cell.Tool.Centroid.X / 1000)}, {N(cell.Tool.Centroid.Y / 1000)}, {N(cell.Tool.Centroid.Z / 1000)}])");
            foreach (var l in (startCode ?? "").Split('\n')) if (l.Trim().Length > 0) s.Add("  " + l.Trim());
            if (cell.Tool.Extrudes) foreach (var l in (offCode ?? "").Split('\n')) if (l.Trim().Length > 0) s.Add("  " + l.Trim());

            double[] q = URModel.Home;
            // the base turned towards the first point (with every axis at 0 the arm reaches out along -X)
            {
                var p0 = baseInv.Pt(tp.Points[0]);
                q[0] = Math.Atan2(p0.Y, p0.X) + Math.PI;
                if (q[0] > Math.PI) q[0] -= 2 * Math.PI;
            }
            double t = 0; Frame prevT = default; bool havePrev = false;
            bool on = false;
            double aLin = 1.2;
            int far = 0;
            // the approach: above the first point, joint move
            {
                var first = target(0, tp.Points[0] + tp.Normals[0] * approach);
                AddTarget(r, m, tcpInv, first, ref q, ref far, cell, tp.Points[0] + tp.Normals[0] * approach, false, ref t, ref prevT, ref havePrev, 100);
                s.Add($"  movej(get_inverse_kin({Pose(first)}), a=1.0, v=0.5)");
            }
            for (int i = 0; i < tp.Count; i++)
            {
                bool print = i > 0 && tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                if (cell.Tool.Extrudes && print != on)
                {
                    foreach (var l in ((print ? onCode : offCode) ?? "").Split('\n')) if (l.Trim().Length > 0) s.Add("  " + l.Trim());
                    on = print;
                }
                double v = speed > 0 ? speed : (i < pr.Speed.Count && pr.Speed[i] > 0 ? pr.Speed[i] / 60.0 : tp.Settings.Speed);
                if (!print) v = Math.Min(100, Math.Max(v, 50)); // moves without printing: brisk, but not more than 100 mm/s
                var tf = target(i, tp.Points[i]);
                AddTarget(r, m, tcpInv, tf, ref q, ref far, cell, tp.Points[i], print, ref t, ref prevT, ref havePrev, v);
                // the controller stops with an error where two blends overlap: never more than 45 % of the moves on either side
                double rb = 0;
                if (blend > 0 && i > 0 && i < tp.Count - 1)
                {
                    double lp = tp.Points[i].DistanceTo(tp.Points[i - 1]), ln = tp.Points[i].DistanceTo(tp.Points[i + 1]);
                    rb = Math.Min(blend, 0.45 * Math.Min(lp, ln));
                    if (rb < 0.05) rb = 0;
                }
                string rad = rb > 0 ? $", r={N(rb / 1000)}" : "";
                s.Add($"  movel({Pose(tf)}, a={N(aLin)}, v={N(v / 1000)}{rad})");
            }
            if (cell.Tool.Extrudes && on) foreach (var l in (offCode ?? "").Split('\n')) if (l.Trim().Length > 0) s.Add("  " + l.Trim());
            {
                int last = tp.Count - 1;
                var up = target(last, tp.Points[last] + tp.Normals[last] * approach);
                AddTarget(r, m, tcpInv, up, ref q, ref far, cell, tp.Points[last] + tp.Normals[last] * approach, false, ref t, ref prevT, ref havePrev, 50);
                s.Add($"  movel({Pose(up)}, a={N(aLin)}, v=0.05)");
            }
            s.Add("end");
            s.Add($"{prog}()");

            // ---- the report
            r.Report.Add(Inv.F($"{tp.Count} points for {m.Name} (reach {m.Reach:0} mm) with {cell.Tool.Name} ({cell.Tool.Weight:0.##} kg of {m.Payload:0.#}), about {r.Minutes:0} min, {s.Count} script lines."));
            if (cell.Tool.Weight > m.Payload) { r.Ok = false; r.Report.Add(Inv.F($"TOO HEAVY: the tool weighs {cell.Tool.Weight:0.#} kg, the {m.Name} carries {m.Payload:0.#} kg.")); }
            if (far == 0) r.Report.Add("Reach OK: the arm gets to every point from the start pose without changing its configuration.");
            else { r.Ok = false; r.Report.Add(Inv.F($"NOT REACHED: {far} point(s) are out of reach or need a turn of the wrist the arm cannot make from where it is. See Unreachable. Move the part nearer to the robot, lower it, or change Turn.")); }
            if (s.Count > 60000) r.Report.Add(Inv.F($"Long program ({s.Count} lines): the controller needs a while to read it before it moves. Fewer points (Resolution in Print Settings) shorten it."));
            if (cell.Tool.Extrudes) r.Report.Add("The extruder is switched by: on = " + (onCode ?? "").Replace("\n", "; ").Trim() + ", off = " + (offCode ?? "").Replace("\n", "; ").Trim() + ".");
            r.Report.Add("First run: start slowly (speed slider on the pendant at 10 to 25 %) and watch the first layer; the robot plans every move itself.");
            return r;
        }

        private static void AddTarget(URProgram r, URModel m, Frame tcpInv, Frame tcpTarget, ref double[] q, ref int far, URCell cell, Point3d world,
            bool print, ref double t, ref Frame prevT, ref bool havePrev, double v)
        {
            var flange = tcpTarget.Times(tcpInv);
            bool ok = m.Solve(flange, q, out var qn, out double pe, out double ae);
            if (!ok) { far++; if (r.Unreachable.Count < 2000) r.Unreachable.Add(world); }
            else q = qn;
            world += new Vector3d(0, 0, cell.Plate); // where it is printed: on top of the plate
            if (havePrev) t += prevT.O.DistanceTo(tcpTarget.O) / Math.Max(1, v);
            prevT = tcpTarget; havePrev = true;
            r.Targets.Add(tcpTarget); r.Joints.Add((double[])q.Clone()); r.Printing.Add(print); r.Seconds.Add(t); r.PathPoints.Add(world);
        }

        /// <summary>Sends the script to the robot (primary interface, port 30002): the pendant then asks to run it.</summary>
        public static string Upload(IEnumerable<string> script, string ip, int port = 30002)
        {
            using (var client = new System.Net.Sockets.TcpClient())
            {
                var task = client.ConnectAsync(ip, port);
                if (!task.Wait(3000)) throw new TimeoutException("No answer from " + ip + ":" + port + " within 3 s. Is the cable in and the laptop on 192.168.1.x?");
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", script) + "\n");
                using (var stream = client.GetStream()) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
                return Inv.F($"Sent {bytes.Length / 1024.0:0} KB to {ip}:{port}.");
            }
        }
    }

    /// <summary>A UR arm as meshes: the model Universal Robots publishes where there is one, plain drums and rods
    /// otherwise, then the tool from the flange to its tip.</summary>
    public static class URDraw
    {
        private static Mesh Rod(Point3d a, Point3d b, double r)
        {
            var axis = b - a; double len = axis.Length;
            if (len < 1e-6) return null;
            var pl = new Plane(a, axis);
            return Mesh.CreateFromCylinder(new Cylinder(new Circle(pl, r), len), 1, 20);
        }

        public static List<Mesh> Arm(URCell cell, double[] q)
        {
            var res = new List<Mesh>();
            var m = cell.Model; double r = Math.Max(30, Math.Abs(m.A2) * 0.11);
            var f = m.Frames(q).Select(x => cell.Base.Times(x)).ToArray();
            var real = URMeshes.Arm(m.Name, cell.Base, q);
            if (real.Count > 0) res.AddRange(real);
            var b0 = real.Count > 0 ? null : Rod(f[0].O, f[0].O + f[0].Z * m.D1 * 0.6, r * 1.3); if (b0 != null) res.Add(b0);
            for (int i = 0; i < 6 && real.Count == 0; i++)
            {
                // the drum of joint i turns about the Z axis of frame i
                var c = f[i].O + f[i].Z * (i == 0 ? m.D1 : 0);
                var d = Rod(c - f[i].Z * r, c + f[i].Z * r, r); if (d != null) res.Add(d);
                var l = Rod(f[i].O, f[i + 1].O, r * 0.8); if (l != null) res.Add(l);
            }
            var tip = f[6].Times(cell.Tool.Tcp);
            var bend = f[6].O + f[6].Z * Math.Max(20, cell.Tool.Tcp.O.Z);
            var t1 = Rod(f[6].O, bend, r * 0.6); if (t1 != null) res.Add(t1);
            var t2 = Rod(bend, tip.O, r * 0.35); if (t2 != null) res.Add(t2);
            return res;
        }
    }
}
