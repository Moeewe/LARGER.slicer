using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>How the collision check of Robot Program works.</summary>
    public class CollisionOptions
    {
        public bool Enabled = true;
        /// <summary>Two bodies closer than this (mm) are reported as a near miss.</summary>
        public double Clearance = 10;
        /// <summary>Fast mode: this many poses spread evenly along the path, plus the critical ones. Below 0 = every move.</summary>
        public int Poses = 500;
        /// <summary>A pose is critical (always checked) where the tool axis has turned this much (degrees) since the last checked pose.</summary>
        public double TurnStep = 5;
    }

    /// <summary>One finding: a collision (Hit) or a near miss, at one move.</summary>
    public class CollisionEvent
    {
        public int Move = -1, Layer;
        public string A = "", B = "";
        public bool Hit;
        /// <summary>Hit: how deep the bodies overlap (mm, 0 = they only cross). Near miss: the gap (mm).</summary>
        public double Depth, Distance;
        public Point3d Where = Point3d.Unset;
        /// <summary>Angle of the tool axis to the bed, degrees.</summary>
        public double Tilt;
        /// <summary>Tool against part or bed: how high above the nozzle tip the tool touches (mm).</summary>
        public double ToolHeight = -1;
        /// <summary>How many checked poses show this, and the first and last move of them.</summary>
        public int Count = 1, FirstMove = -1, LastMove = -1;
        public int BodyA = -1, BodyB = -1;

        public string Text()
        {
            var ci = CultureInfo.InvariantCulture;
            string where = string.Format(ci, "move {0} (layer {1})", Move, Layer);
            string more = Count > 1 ? string.Format(ci, "; {0} checked poses, moves {1} to {2}", Count, FirstMove, LastMove) : "";
            if (Hit)
            {
                string depth = Depth >= 0.5 ? string.Format(ci, "depth {0:0} mm", Depth) : "the surfaces cross";
                return string.Format(ci, "COLLISION: {0} hits {1} at {2}, {3}{4}.", A, B, where, depth, more);
            }
            return string.Format(ci, "CLEARANCE: {0} passes {1:0} mm from {2} at {3}{4}.", A, Distance, B, where, more);
        }
    }

    /// <summary>What the collision check found along a program.</summary>
    public class CollisionResult
    {
        public bool Checked;
        public int Poses, Critical, Stride = 1;
        public double Seconds;
        /// <summary>The worst collision and the closest near miss of every pair of bodies.</summary>
        public List<CollisionEvent> Events = new List<CollisionEvent>();
        /// <summary>Moves at which something collides, in order.</summary>
        public List<int> Moves = new List<int>();
        /// <summary>Where it collides (world), one point per colliding move.</summary>
        public List<Point3d> Points = new List<Point3d>();
        /// <summary>The colliding bodies at the worst collision of every pair, where they are then.</summary>
        public List<Mesh> Bodies = new List<Mesh>();
        public List<string> Lines = new List<string>();
        public List<string> Suggestions = new List<string>();
        /// <summary>The prepared bodies, for checking single moments (Robot Simulation).</summary>
        public CollisionModel Model;
        public int Hits => Moves.Count;
        public int FirstMove => Moves.Count > 0 ? Moves[0] : -1;
        public string Summary => Lines.Count > 0 ? Lines[0] : "";
    }

    /// <summary>The result of one pose: which bodies collide.</summary>
    public class PoseCollision
    {
        /// <summary>Robot bodies that collide: 0 = base, 1..6 = links, 7 = tool.</summary>
        public HashSet<int> Robot = new HashSet<int>();
        /// <summary>Indices into the Environment list of the cell.</summary>
        public HashSet<int> Environment = new HashSet<int>();
        /// <summary>Indices into the list of ExternalAxisGeometry.Pose (all meshes of a colliding axis).</summary>
        public HashSet<int> Axes = new HashSet<int>();
        public bool Printed, Bed;
        public List<Point3d> Where = new List<Point3d>();
        public List<CollisionEvent> Events = new List<CollisionEvent>();
        public bool Any => Robot.Count > 0 || Environment.Count > 0 || Axes.Count > 0 || Printed || Bed;
    }

    /// <summary>
    /// The part printed so far, as a cheap volume: the beads as short pieces with their width and
    /// height (an ellipse in section), each stamped with the move that laid it, in a grid. A
    /// query gives the gap from a point to the beads laid up to a move (below 0 = inside).
    /// The part stays in its own coordinates; on a turntable the query points are turned into them.
    /// </summary>
    public class PrintedVolume
    {
        private struct Seg { public Point3d A, B; public Vector3d N; public double W, H; public int Stamp; }
        private readonly List<Seg> _segs = new List<Seg>();
        private readonly Dictionary<long, List<int>> _fine = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, int> _coarse = new Dictionary<long, int>();
        private readonly double _c, _C, _reach;
        private double _maxHalf;
        public BoundingBox Box = BoundingBox.Empty;
        public int Count => _segs.Count;
        public double MinZ = double.MaxValue;

        public PrintedVolume(Toolpath tp, IList<double> extrusion, double reach)
        {
            _reach = reach;
            double w0 = tp.Settings.BeadWidth > 0 ? tp.Settings.BeadWidth : 5;
            _c = Math.Max(reach + w0, 6);
            _C = 4 * _c;
            int n = tp.Count;
            Func<int, bool> printing = i => i > 0 && i < n && (extrusion == null || i >= extrusion.Count || extrusion[i] > 0) && (i >= tp.Roles.Count || tp.Roles[i] != PathRole.Travel);
            int a = -1;
            for (int i = 1; i < n; i++)
            {
                if (!printing(i)) { a = -1; continue; }
                if (a < 0) a = i - 1;
                // merge straight runs: the next point would bend the piece by more than a fraction of the bead height
                bool end = i + 1 >= n || !printing(i + 1) || tp.Points[a].DistanceTo(tp.Points[i + 1]) > 4 * _c;
                if (!end)
                {
                    Point3d p = tp.Points[a], q = tp.Points[i + 1];
                    double h = Get(tp.Heights, i, tp.Settings.LayerHeight);
                    for (int j = a + 1; j <= i && !end; j++) if (new Line(p, q).DistanceTo(tp.Points[j], true) > 0.25 * h) end = true;
                }
                if (!end) continue;
                Add(tp, a, i);
                a = i;
            }
        }

        private static double Get(List<double> l, int i, double fallback) => l != null && i < l.Count && l[i] > 0 ? l[i] : fallback;

        private void Add(Toolpath tp, int a, int b)
        {
            var nrm = tp.Normals.Count > b ? tp.Normals[b] : Vector3d.ZAxis; if (!nrm.Unitize()) nrm = Vector3d.ZAxis;
            double w = Get(tp.Widths, b, tp.Settings.BeadWidth), h = Get(tp.Heights, b, tp.Settings.LayerHeight);
            // the path is the top of the bead (nozzle height): the bead centre lies half a height lower
            var s = new Seg { A = tp.Points[a] - nrm * (h / 2), B = tp.Points[b] - nrm * (h / 2), N = nrm, W = w / 2, H = h / 2, Stamp = b };
            int idx = _segs.Count; _segs.Add(s);
            _maxHalf = Math.Max(_maxHalf, Math.Max(s.W, s.H));
            var box = new BoundingBox(new[] { s.A, s.B }); box.Inflate(Math.Max(s.W, s.H));
            Box.Union(box);
            MinZ = Math.Min(MinZ, Math.Min(s.A.Z, s.B.Z) - s.H);
            ForCells(box, _c, k => { if (!_fine.TryGetValue(k, out var list)) _fine[k] = list = new List<int>(2); list.Add(idx); });
            box.Inflate(_reach);
            ForCells(box, _C, k => { if (!_coarse.TryGetValue(k, out int st) || b < st) _coarse[k] = b; });
        }

        private static void ForCells(BoundingBox b, double c, Action<long> f)
        {
            int x0 = (int)Math.Floor(b.Min.X / c), x1 = (int)Math.Floor(b.Max.X / c), y0 = (int)Math.Floor(b.Min.Y / c), y1 = (int)Math.Floor(b.Max.Y / c), z0 = (int)Math.Floor(b.Min.Z / c), z1 = (int)Math.Floor(b.Max.Z / c);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) f(Key(x, y, z));
        }

        private static long Key(int x, int y, int z) => ((long)(x + (1 << 20)) << 42) | ((long)(y + (1 << 20)) << 21) | (long)(z + (1 << 20));

        /// <summary>
        /// Gap from p to the beads laid up to move maxStamp (below 0 = inside, the depth), or
        /// +infinity if none is within the reach given at construction.
        /// </summary>
        public double Distance(Point3d p, int maxStamp, out int stamp)
        {
            stamp = -1;
            if (!_coarse.TryGetValue(Key((int)Math.Floor(p.X / _C), (int)Math.Floor(p.Y / _C), (int)Math.Floor(p.Z / _C)), out int first) || first > maxStamp) return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            int cx = (int)Math.Floor(p.X / _c), cy = (int)Math.Floor(p.Y / _c), cz = (int)Math.Floor(p.Z / _c);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!_fine.TryGetValue(Key(x, y, z), out var list)) continue;
                        foreach (int i in list)
                        {
                            var s = _segs[i];
                            if (s.Stamp > maxStamp) break; // in the order they were laid
                            Vector3d ab = s.B - s.A, ap = p - s.A;
                            double l2 = ab.SquareLength, t = l2 > 1e-12 ? Math.Max(0, Math.Min(1, (ap * ab) / l2)) : 0;
                            Vector3d d = p - (s.A + ab * t);
                            double len = d.Length;
                            if (len - _maxHalf > best) continue;
                            double dn = d * s.N, dt = Math.Sqrt(Math.Max(0, len * len - dn * dn));
                            double e = Math.Sqrt((dt / s.W) * (dt / s.W) + (dn / s.H) * (dn / s.H));
                            // along the ray from the bead centre: e = 1 on the bead surface
                            double gap = e > 1e-9 ? len * (1 - 1 / e) : -Math.Min(s.W, s.H);
                            if (gap < best) { best = gap; stamp = s.Stamp; }
                        }
                    }
            return best;
        }
    }

    /// <summary>
    /// The bodies of the cell, prepared once for many poses: simplified meshes, sample points on
    /// their surfaces, local bounding boxes. Tests run fast first (boxes in world and in the
    /// body's own frame), then on sample points (gap and depth), and only for close candidates
    /// Rhino's mesh-mesh intersection.
    /// </summary>
    public class CollisionModel
    {
        internal class Shape
        {
            public Mesh Mesh;          // simplified, local coordinates
            public BoundingBox Box;    // local
            public Point3d[] Samples;  // local
            public double Spacing;
            public bool Closed;
            public bool[] Nozzle;      // tool only: sample lies in the zone around the nozzle tip
            public double[] Height;    // tool only: height of the sample above the tip, along the tool
        }

        internal class Body
        {
            public string Name;
            public int Robot = -1, Env = -1, Axis = -1; // where it comes from
            public Shape Shape;
            public bool Moving; // robot links, carriage, table
            public int[] Parts; // axis bodies: their meshes in ExternalAxisGeometry.Pose
        }

        private enum Mode { Full, CollisionOnly, Skip }
        private class Pair { public int A, B; public Mode Mode; public bool Standing; }

        public RobotCell Cell;
        public Toolpath Path;
        public RobotProgramResult Program;
        public CollisionOptions Options;
        public PrintedVolume Printed;
        public double BedZ = double.NaN;
        public BoundingBox BedBox = BoundingBox.Empty; // part coordinates
        public double NozzleRadius, NozzleHeight;
        public List<string> Notes = new List<string>();
        public bool HasToolModel, ToolIsCylinder;

        private readonly List<Body> _bodies = new List<Body>(); // robot 0..7, then axes, then environment pieces
        private int _axisFirst, _axisCount, _envFirst;
        private readonly List<int> _axisSource = new List<int>(); // per axis body: first index in ExternalAxisGeometry.Pose
        private readonly List<int[]> _axisParts = new List<int[]>();
        private readonly List<Pair> _pairs = new List<Pair>();
        private readonly List<int> _toPrinted = new List<int>(), _toBed = new List<int>();
        private Frame[] _zeroInv;
        private bool _standingDone;

        private static readonly ConditionalWeakTable<Mesh, Shape> ShapeCache = new ConditionalWeakTable<Mesh, Shape>();
        private static readonly ConditionalWeakTable<Mesh, Shape[]> PieceCache = new ConditionalWeakTable<Mesh, Shape[]>();

        internal Body BodyAt(int i) => _bodies[i];

        public static string RobotName(int i) => i == 0 ? "robot base" : i == 7 ? "tool" : "link " + i;

        public static CollisionModel Make(RobotProgramResult program, Toolpath tp, RobotCell cell, CollisionOptions opt)
        {
            var m = new CollisionModel { Cell = cell, Path = tp, Program = program, Options = opt ?? new CollisionOptions() };
            m.Build();
            return m;
        }

        private void Build()
        {
            var arm = Cell.Arm;
            double R = Math.Max(0, Options.Clearance);
            double w = Path.Settings.BeadWidth > 0 ? Path.Settings.BeadWidth : 5, h = Path.Settings.LayerHeight > 0 ? Path.Settings.LayerHeight : 2;
            var zero = arm.JointFrames(RobotGeometry.HomePose);
            _zeroInv = zero.Select(f => f.Inverse()).ToArray();

            // ---- robot: base and six links
            for (int i = 0; i < 7; i++)
            {
                double spacing = i >= 4 ? 15 : 30;
                _bodies.Add(new Body { Name = RobotName(i), Robot = i, Moving = i > 0, Shape = GetShape(arm.Links[i], 1500, spacing, 4000) });
            }
            // ---- tool: its mesh, or a plain cylinder from the flange to the tip
            Mesh toolMesh = Cell.Tool?.Mesh;
            HasToolModel = toolMesh != null && toolMesh.Vertices.Count > 0;
            var tcp = Cell.Tool != null ? Cell.Tool.Tcp : RobotTool.StraightTool(0);
            if (!HasToolModel)
            {
                double len = (tcp.O - Point3d.Origin).Length;
                if (len > 20)
                {
                    var cyl = new Cylinder(new Circle(new Plane(Point3d.Origin, tcp.O - Point3d.Origin), Math.Max(20, 2 * w)), len - 2 * w);
                    toolMesh = Mesh.CreateFromCylinder(cyl, 2, 24, true, true);
                    ToolIsCylinder = toolMesh != null;
                }
            }
            if (toolMesh != null)
            {
                var shape = GetShape(toolMesh, 3000, Math.Max(4, w), 12000, true);
                // the zone around the nozzle tip, where the tool sits on the beads by design: as high as two
                // layers and as wide as the nozzle there, plus a bead
                Vector3d za = tcp.Z; za.Unitize();
                NozzleHeight = 2 * h + 2;
                double rad = 0;
                var v = toolMesh.Vertices;
                for (int k = 0; k < v.Count; k++)
                {
                    Vector3d d = (Point3d)v[k] - tcp.O; double u = d * za;
                    if (u < NozzleHeight) rad = Math.Max(rad, (d - za * u).Length);
                }
                NozzleRadius = Math.Min(Math.Max(w, rad + w / 2), Math.Max(6 * w, 40));
                var nozzle = new bool[shape.Samples.Length]; var height = new double[shape.Samples.Length];
                for (int k = 0; k < nozzle.Length; k++)
                {
                    Vector3d d = shape.Samples[k] - tcp.O; double u = d * za;
                    height[k] = u;
                    nozzle[k] = u < NozzleHeight && (d - za * u).Length < NozzleRadius;
                }
                // the shape is shared (cache): the zone depends on the path, so keep a copy
                shape = new Shape { Mesh = shape.Mesh, Box = shape.Box, Samples = shape.Samples, Spacing = shape.Spacing, Closed = shape.Closed, Nozzle = nozzle, Height = height };
                _bodies.Add(new Body { Name = "tool", Robot = 7, Moving = true, Shape = shape });
            }
            else _bodies.Add(new Body { Name = "tool", Robot = 7, Moving = true, Shape = null });

            // ---- external axes: their simple models at 0 (the shapes are made again per pose)
            _axisFirst = _bodies.Count;
            var axesModel = ExternalAxisGeometry.Pose(Cell, null);
            int src = 0;
            if (Cell.ExternalAxes != null)
                foreach (var ax in Cell.ExternalAxes)
                {
                    if (ax.IsRail)
                    {
                        AddAxis("rail beam", src, new[] { src }, false); AddAxis("rail carriage", src + 1, new[] { src + 1 }, true); src += 2;
                    }
                    else
                    {
                        AddAxis("turntable", src, new[] { src, src + 1 }, true); AddAxis("turntable stand", src + 2, new[] { src + 2 }, false); src += 3;
                    }
                }
            _axisCount = _bodies.Count - _axisFirst;

            // ---- environment, in pieces (a fence of many panels: each panel its own box)
            _envFirst = _bodies.Count;
            if (Cell.Environment != null)
                for (int e = 0; e < Cell.Environment.Count; e++)
                {
                    var env = Cell.Environment[e];
                    if (env == null || !env.IsValid) continue;
                    string name = "the " + EnvName(env, e);
                    if (!PieceCache.TryGetValue(env, out var shapes))
                    {
                        Mesh[] pieces = env.DisjointMeshCount > 1 && env.DisjointMeshCount < 400 ? env.SplitDisjointPieces() : new[] { env };
                        shapes = pieces.Where(pc => pc != null && pc.Faces.Count > 0).Select(pc => MakeShape(pc, 3000, 40, 6000)).ToArray();
                        try { PieceCache.Add(env, shapes); } catch { }
                    }
                    foreach (var sh in shapes) _bodies.Add(new Body { Name = name, Env = e, Shape = sh });
                }

            // ---- the part printed so far, and the bed under it
            double reach = Math.Max(R, 10);
            Printed = new PrintedVolume(Path, Program.Extrusion, reach);
            if (Printed.Count > 0)
            {
                BedZ = Printed.MinZ;
                var b = Printed.Box; double grow = Math.Max(300, 0.25 * Math.Max(b.Max.X - b.Min.X, b.Max.Y - b.Min.Y));
                BedBox = new BoundingBox(b.Min.X - grow, b.Min.Y - grow, BedZ - 100, b.Max.X + grow, b.Max.Y + grow, BedZ);
            }

            // ---- which pairs are tested
            int robotCount = 8;
            // the arm with itself: not neighbouring links, not pairs that touch already in the home position
            var homeFrames = RobotFrames(RobotGeometry.HomePose, null);
            for (int i = 0; i < robotCount; i++)
                for (int j = i + 2; j < robotCount; j++)
                {
                    if (_bodies[i].Shape == null || _bodies[j].Shape == null) continue;
                    var pr = new Pair { A = j, B = i, Mode = Mode.Full };
                    var t = TestPair(new Posed(_bodies[j], homeFrames[j]), new Posed(_bodies[i], homeFrames[i]), R, false);
                    if (t.Hit || t.Distance < 1) { pr.Mode = Mode.Skip; continue; }
                    if (t.Distance < R) pr.Mode = Mode.CollisionOnly;
                    _pairs.Add(pr);
                }
            bool rail = Cell.ExternalAxes != null && Cell.ExternalAxes.Any(a => a.IsRail);
            for (int i = rail ? 0 : 1; i < robotCount; i++)
            {
                if (_bodies[i].Shape == null) continue;
                bool low = i <= 1; // base and link 1 stand on things: what they touch at the start they stand on
                for (int e = _envFirst; e < _bodies.Count; e++) _pairs.Add(new Pair { A = i, B = e, Standing = low });
                for (int a = _axisFirst; a < _axisFirst + _axisCount; a++)
                {
                    string an = _bodies[a].Name;
                    if (i == 0 && (an == "rail beam" || an == "rail carriage")) continue; // the robot stands on its carriage
                    _pairs.Add(new Pair { A = i, B = a, Standing = low });
                }
                if (i >= 1) { _toPrinted.Add(i); _toBed.Add(i); }
            }
            // the carriage and the table against what stands around them
            for (int a = _axisFirst; a < _axisFirst + _axisCount; a++)
                if (_bodies[a].Moving)
                    for (int e = _envFirst; e < _bodies.Count; e++) _pairs.Add(new Pair { A = a, B = e, Standing = true });

            int skipped = _bodies.Count(b => b.Shape == null);
            if (!HasToolModel)
                Notes.Add(ToolIsCylinder ? $"The tool has no 3D model: it is checked as a cylinder of {Math.Max(40, 4 * w):0} mm from the flange to the tip. Give the tool a mesh (Robot Tool) for a true check."
                                         : "The tool has no 3D model and its tip sits on the flange: the tool itself is not checked. Give the tool a mesh (Robot Tool).");
            if (Cell.Environment == null || Cell.Environment.Count == 0) Notes.Add("No Environment in the cell: floor, fence and walls are not checked (Cell Geometry gives the cell of the FH Muenster).");
        }

        private void AddAxis(string name, int source, int[] parts, bool moving)
        {
            _bodies.Add(new Body { Name = name, Axis = source, Moving = moving, Parts = parts });
            _axisSource.Add(source); _axisParts.Add(parts);
        }

        /// <summary>A name for an environment mesh: the layer name of the built-in cell if it is one of its parts.</summary>
        private static string EnvName(Mesh env, int index)
        {
            try
            {
                var parts = RobotGeometry.FhMuensterCell();
                if (parts != null)
                {
                    var b = env.GetBoundingBox(false);
                    foreach (var kv in parts)
                    {
                        if (kv.Value.Vertices.Count != env.Vertices.Count) continue;
                        var c = kv.Value.GetBoundingBox(false);
                        if (c.Min.DistanceTo(b.Min) < 1 && c.Max.DistanceTo(b.Max) < 1) return kv.Key.ToLowerInvariant();
                    }
                }
            }
            catch { }
            return "environment mesh " + (index + 1);
        }

        // ------------------------------------------------------------------ shapes
        internal static Shape GetShape(Mesh m, int maxFaces, double spacing, int maxSamples, bool noCache = false)
        {
            if (m == null) return null;
            if (!noCache && ShapeCache.TryGetValue(m, out var cached) && Math.Abs(cached.Spacing - spacing) < 1e-9) return cached;
            var s = MakeShape(m, maxFaces, spacing, maxSamples);
            if (!noCache) { try { ShapeCache.Remove(m); ShapeCache.Add(m, s); } catch { } }
            return s;
        }

        private static Shape MakeShape(Mesh source, int maxFaces, double spacing, int maxSamples)
        {
            var m = source.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            if (m.Faces.Count > maxFaces)
            {
                var r = m.DuplicateMesh();
                bool closed = m.IsClosed;
                try { if (r.Reduce(maxFaces, true, 5, false) && r.Faces.Count > 0 && (!closed || r.IsClosed)) m = r; } catch { }
            }
            m.Faces.CullDegenerateFaces();
            m.FaceNormals.ComputeFaceNormals();
            bool solid = m.IsClosed && m.IsValid;
            if (solid)
            {
                // inside tests need normals that point out
                int so = m.SolidOrientation();
                if (so < 0) { m.Flip(true, true, true); m.FaceNormals.ComputeFaceNormals(); }
                else if (so == 0) solid = false;
            }
            var shape = new Shape { Mesh = m, Box = m.GetBoundingBox(false), Closed = solid };
            // sample points: vertices, and points on faces no further apart than the spacing
            double area = 0;
            var v = m.Vertices;
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                area += 0.5 * Vector3d.CrossProduct((Point3d)v[fc.B] - (Point3d)v[fc.A], (Point3d)v[fc.C] - (Point3d)v[fc.A]).Length;
            }
            double s = spacing;
            if (area / (0.5 * s * s) > maxSamples) s = Math.Sqrt(area / (0.5 * maxSamples));
            shape.Spacing = s;
            var pts = new List<Point3d>(v.Count * 2);
            for (int k = 0; k < v.Count; k++) pts.Add(v[k]);
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                Point3d a = v[fc.A], b = v[fc.B], c = v[fc.C];
                double longest = Math.Max(a.DistanceTo(b), Math.Max(b.DistanceTo(c), c.DistanceTo(a)));
                int n = (int)Math.Ceiling(longest / s);
                if (n <= 1) { if (longest > s * 0.5) pts.Add(new Point3d((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3)); continue; }
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                    {
                        if ((i == 0 && j == 0) || (i == n) || (j == n)) continue; // corners are vertices
                        double u = i / (double)n, w = j / (double)n;
                        pts.Add(a + (b - a) * u + (c - a) * w);
                    }
            }
            shape.Samples = pts.ToArray();
            return shape;
        }

        // ------------------------------------------------------------------ poses
        internal class Posed
        {
            public Body Body; public Shape Shape; public Frame F; public BoundingBox WorldBox;
            private Mesh _mesh; private Point3d[] _samples;
            public Posed(Body b, Frame f) : this(b, b.Shape, f) { }
            public Posed(Body b, Shape s, Frame f)
            {
                Body = b; Shape = s; F = f;
                WorldBox = BoundingBox.Empty;
                if (s != null) foreach (var c in s.Box.GetCorners()) WorldBox.Union(f.Pt(c));
            }
            public Mesh Mesh
            {
                get
                {
                    if (_mesh == null)
                    {
                        _mesh = Shape.Mesh.DuplicateMesh();
                        _mesh.Transform(RobotGeometry.ToTransform(F));
                        _mesh.FaceNormals.ComputeFaceNormals();
                    }
                    return _mesh;
                }
            }
            public Point3d[] Samples
            {
                get
                {
                    if (_samples == null) { var l = Shape.Samples; _samples = new Point3d[l.Length]; for (int i = 0; i < l.Length; i++) _samples[i] = F.Pt(l[i]); }
                    return _samples;
                }
            }
        }

        private Frame[] RobotFrames(double[] axes, double[] ext)
        {
            var arm = Cell.Arm;
            Frame foot = Cell.RobotBaseAt(ext);
            var now = arm.JointFrames(axes);
            var f = new Frame[8];
            f[0] = foot;
            for (int i = 1; i < 7; i++) f[i] = foot.Times(now[i - 1]).Times(_zeroInv[i - 1]);
            f[7] = foot.Times(now[5]).Times(Frame.World.TurnedAboutZ(Math.PI));
            return f;
        }

        private List<Posed> Pose(double[] axes, double[] ext)
        {
            var frames = RobotFrames(axes, ext);
            var list = new List<Posed>(_bodies.Count);
            for (int i = 0; i < 8; i++) list.Add(new Posed(_bodies[i], frames[i]));
            if (_axisCount > 0)
            {
                var meshes = ExternalAxisGeometry.Pose(Cell, ext);
                for (int a = 0; a < _axisCount; a++)
                {
                    var joined = new Mesh();
                    foreach (int p in _axisParts[a]) if (p < meshes.Count) joined.Append(meshes[p]);
                    var body = _bodies[_axisFirst + a];
                    list.Add(new Posed(body, MakeShape(joined, 4000, 40, 4000), Frame.World));
                }
            }
            for (int e = _envFirst; e < _bodies.Count; e++) list.Add(new Posed(_bodies[e], Frame.World));
            return list;
        }

        // ------------------------------------------------------------------ tests
        internal struct Test { public bool Hit; public double Depth, Distance; public Point3d Where; public double ToolHeight; }

        private static Test None => new Test { Distance = double.PositiveInfinity, Where = Point3d.Unset, ToolHeight = -1 };

        private static double BoxGap(BoundingBox a, BoundingBox b)
        {
            double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The world box of b seen in a's own frame, against a's own box: a tighter test for slanted links.</summary>
        private static double LocalGap(Posed a, Posed b)
        {
            var inv = a.F.Inverse();
            var box = BoundingBox.Empty;
            foreach (var c in b.WorldBox.GetCorners()) box.Union(inv.Pt(c));
            return BoxGap(a.Shape.Box, box);
        }

        /// <summary>Gap and depth between two bodies: boxes first, then sample points, then (only when close) the meshes.</summary>
        internal static Test TestPair(Posed a, Posed b, double R, bool skipNozzle)
        {
            var t = None;
            if (a.Shape == null || b.Shape == null) return t;
            if (BoxGap(a.WorldBox, b.WorldBox) > R) return t;
            if (a.Body.Robot >= 0 && LocalGap(a, b) > R) return t;
            if (b.Body.Robot >= 0 && LocalGap(b, a) > R) return t;
            double minA = Side(a, b, R, skipNozzle, ref t);
            if (t.Distance > R && minA > a.Shape.Spacing) return t;
            double minB = Side(b, a, R, false, ref t);
            if (t.Hit) return t;
            // no sample inside: do the surfaces cross? Only possible if both sides come this close
            if (minA <= Math.Max(R, a.Shape.Spacing) && minB <= Math.Max(R, b.Shape.Spacing))
            {
                Line[] lines = null;
                try { lines = Rhino.Geometry.Intersect.Intersection.MeshMeshFast(a.Mesh, b.Mesh); } catch { }
                if (lines != null && lines.Length > 0)
                {
                    // the tool crossing near its tip is the nozzle on the bed or the beads, by design
                    var cross = lines.Select(l => l.PointAt(0.5)).ToList();
                    if (skipNozzle && a.Body.Robot == 7 && a.Shape.Nozzle != null)
                    {
                        var inv = a.F.Inverse(); var shape = a.Shape;
                        cross = cross.Where(p => !InNozzle(inv.Pt(p), shape)).ToList();
                    }
                    if (cross.Count > 0) { t.Hit = true; t.Depth = Math.Max(t.Depth, 0); t.Where = cross[0]; t.Distance = 0; }
                }
            }
            return t;
        }

        private static bool InNozzle(Point3d local, Shape tool)
        {
            // the zone is stored per sample; for any point take the nearest sample's flag
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < tool.Samples.Length; i++) { double d = tool.Samples[i].DistanceToSquared(local); if (d < bd) { bd = d; best = i; } }
            return best >= 0 && tool.Nozzle[best];
        }

        /// <summary>Samples of a against the mesh of b: smallest gap, and depth for samples inside a closed b.</summary>
        private static double Side(Posed a, Posed b, double R, bool skipNozzle, ref Test t)
        {
            var box = b.WorldBox; double grow = Math.Max(R, a.Shape.Spacing); box.Inflate(grow);
            var inner = b.WorldBox;
            var pts = a.Samples; var noz = skipNozzle ? a.Shape.Nozzle : null;
            double min = double.PositiveInfinity;
            Mesh mb = null;
            for (int i = 0; i < pts.Length; i++)
            {
                var q = pts[i];
                if (!box.Contains(q)) continue;
                if (noz != null && noz[i]) continue;
                bool maybeInside = b.Shape.Closed && inner.Contains(q);
                if (mb == null) mb = b.Mesh;
                var mp = mb.ClosestMeshPoint(q, maybeInside ? 0.0 : grow);
                if (mp == null) continue;
                Vector3d d = q - mp.Point; double len = d.Length;
                bool inside = maybeInside && mp.FaceIndex >= 0 && mp.FaceIndex < mb.FaceNormals.Count && d * (Vector3d)mb.FaceNormals[mp.FaceIndex] < 0 && len > 1e-6;
                if (inside)
                {
                    min = 0;
                    if (!t.Hit || len > t.Depth) { t.Hit = true; t.Depth = len; t.Where = q; t.Distance = 0; t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; }
                    continue;
                }
                if (len < min) min = len;
                if (!t.Hit && len < t.Distance) { t.Distance = len; t.Where = q; t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; }
            }
            return min;
        }

        /// <summary>A robot body against the part printed up to maxStamp (part coordinates via partInv).</summary>
        private Test TestPrinted(Posed a, Frame partInv, Frame part, int maxStamp, double R)
        {
            var t = None;
            if (a.Shape == null || Printed == null || Printed.Count == 0) return t;
            var box = BoundingBox.Empty;
            foreach (var c in a.WorldBox.GetCorners()) box.Union(partInv.Pt(c));
            var vb = Printed.Box; vb.Inflate(R + 1);
            if (BoxGap(box, vb) > 0) return t;
            var loc = a.Shape.Samples; var noz = a.Shape.Nozzle;
            var toPart = partInv.Times(a.F);
            for (int i = 0; i < loc.Length; i++)
            {
                if (noz != null && noz[i]) continue;
                var p = toPart.Pt(loc[i]);
                if (!vb.Contains(p)) continue;
                double gap = Printed.Distance(p, maxStamp, out int stamp);
                if (double.IsPositiveInfinity(gap)) continue;
                if (gap < 0) { if (!t.Hit || -gap > t.Depth) { t.Hit = true; t.Depth = -gap; t.Where = part.Pt(p); t.Distance = 0; t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; } }
                else if (!t.Hit && gap < t.Distance && RidesAbove(a.Shape, i, gap)) { t.Distance = gap; t.Where = part.Pt(p); t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; }
            }
            return t;
        }

        /// <summary>
        /// The tool rides above the layer it prints: a point of it this high above the tip is about
        /// this far from the beads under it. Only a gap clearly smaller than that is a near miss.
        /// </summary>
        private static bool RidesAbove(Shape s, int i, double gap) => s.Height == null || gap < 0.7 * s.Height[i];

        /// <summary>A robot body against the bed under the part (a slab, part coordinates: it turns with a table).</summary>
        private Test TestBed(Posed a, Frame partInv, Frame part, double R)
        {
            var t = None;
            if (a.Shape == null || double.IsNaN(BedZ)) return t;
            var box = BoundingBox.Empty;
            foreach (var c in a.WorldBox.GetCorners()) box.Union(partInv.Pt(c));
            var bb = BedBox; bb.Inflate(0, 0, R);
            if (BoxGap(box, bb) > 0) return t;
            var loc = a.Shape.Samples; var noz = a.Shape.Nozzle;
            var toPart = partInv.Times(a.F);
            for (int i = 0; i < loc.Length; i++)
            {
                if (noz != null && noz[i]) continue;
                var p = toPart.Pt(loc[i]);
                if (p.X < BedBox.Min.X || p.X > BedBox.Max.X || p.Y < BedBox.Min.Y || p.Y > BedBox.Max.Y || p.Z < BedBox.Min.Z) continue;
                double gap = p.Z - BedZ;
                if (gap < 0) { if (!t.Hit || -gap > t.Depth) { t.Hit = true; t.Depth = -gap; t.Where = part.Pt(p); t.Distance = 0; t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; } }
                else if (!t.Hit && gap < t.Distance && RidesAbove(a.Shape, i, gap)) { t.Distance = gap; t.Where = part.Pt(p); t.ToolHeight = a.Shape.Height != null ? a.Shape.Height[i] : -1; }
            }
            return t;
        }

        /// <summary>
        /// Checks one pose. move: the move whose beads are the newest already laid (the robot is
        /// on its way to move+1, or at move). Events of near misses only if near is true.
        /// </summary>
        public PoseCollision Check(double[] axes, double[] ext, int move, bool near = false)
        {
            var res = new PoseCollision();
            if (axes == null) return res;
            double R = Math.Max(0, Options.Clearance);
            var posed = Pose(axes, ext);
            if (!_standingDone) DecideStanding(posed, R);
            Frame part = Cell.PartAt(ext), partInv = part.Inverse();
            int layer = move >= 0 && move < Path.Layers.Count ? Path.Layers[move] : 0;
            double tilt = Tilt(move);
            Action<int, int, Test, string> record = (ia, ib, t, other) =>
            {
                if (!t.Hit && !(near && t.Distance < R)) return;
                var ev = new CollisionEvent { Move = move, Layer = layer, A = posed[ia].Body.Name, B = other ?? posed[ib].Body.Name, Hit = t.Hit, Depth = t.Depth, Distance = t.Distance, Where = t.Where, Tilt = tilt, ToolHeight = t.ToolHeight, FirstMove = move, LastMove = move, BodyA = ia, BodyB = ib };
                res.Events.Add(ev);
                if (!t.Hit) return;
                Mark(res, posed[ia].Body);
                if (ib >= 0) Mark(res, posed[ib].Body);
                else if (other == "printed part") res.Printed = true; else res.Bed = true;
                if (t.Where.IsValid) res.Where.Add(t.Where);
            };
            foreach (var pr in _pairs)
            {
                if (pr.Mode == Mode.Skip) continue;
                var t = TestPair(posed[pr.A], posed[pr.B], R, true);
                if (pr.Mode == Mode.CollisionOnly && !t.Hit) continue;
                record(pr.A, pr.B, t, null);
            }
            foreach (int i in _toPrinted) record(i, -1, TestPrinted(posed[i], partInv, part, move, R), "printed part");
            foreach (int i in _toBed) record(i, -1, TestBed(posed[i], partInv, part, R), "bed");
            return res;
        }

        private static void Mark(PoseCollision res, Body b)
        {
            if (b.Robot >= 0) res.Robot.Add(b.Robot);
            else if (b.Env >= 0) res.Environment.Add(b.Env);
            else if (b.Parts != null) foreach (int p in b.Parts) res.Axes.Add(p);
        }

        /// <summary>What the base, link 1, the carriage and the table touch in the first pose they stand on (pedestal, floor): left out.</summary>
        private void DecideStanding(List<Posed> posed, double R)
        {
            _standingDone = true;
            var left = new List<string>();
            foreach (var pr in _pairs)
            {
                if (!pr.Standing) continue;
                var t = TestPair(posed[pr.A], posed[pr.B], R, false);
                if (t.Hit || t.Distance < 1) { pr.Mode = Mode.Skip; left.Add(posed[pr.A].Body.Name + " on " + posed[pr.B].Body.Name); }
                else if (t.Distance < R) pr.Mode = Mode.CollisionOnly;
            }
            if (left.Count > 0) Notes.Add("Not checked, as they stand on each other from the start: " + string.Join(", ", left.Distinct()) + ".");
        }

        /// <summary>Angle of the tool axis to the bed normal at a move, degrees.</summary>
        public double Tilt(int move)
        {
            if (move < 0 || move >= Path.Normals.Count) return 0;
            var n = Path.Normals[move]; if (!n.Unitize()) return 0;
            return Math.Acos(Math.Max(-1, Math.Min(1, n.Z))) * 180 / Math.PI;
        }

        /// <summary>A body as a mesh where it is in this pose (the full model for robot and tool), for showing it.</summary>
        public Mesh BodyMesh(int index, double[] axes, double[] ext)
        {
            if (index < 0 || index >= _bodies.Count) return null;
            var b = _bodies[index];
            if (b.Robot >= 0)
            {
                var model = RobotGeometry.Pose(Cell, axes, ext);
                if (model != null && b.Robot < model.Count && (b.Robot < 7 || HasToolModel)) return model[b.Robot];
                if (b.Shape == null) return null;
                var m = b.Shape.Mesh.DuplicateMesh(); m.Transform(RobotGeometry.ToTransform(RobotFrames(axes, ext)[b.Robot])); return m;
            }
            if (b.Env >= 0) return Cell.Environment[b.Env];
            if (b.Axis >= 0)
            {
                var meshes = ExternalAxisGeometry.Pose(Cell, ext); var joined = new Mesh();
                foreach (int p in _axisParts[index - _axisFirst]) if (p < meshes.Count) joined.Append(meshes[p]);
                return joined;
            }
            return null;
        }
    }

    /// <summary>The collision check of Robot Program: picks the poses, runs them, writes the report.</summary>
    public static class CollisionCheck
    {
        public static CollisionResult Run(RobotProgramResult program, Toolpath tp, RobotCell cell, CollisionOptions opt)
        {
            var sw = Stopwatch.StartNew();
            opt = opt ?? new CollisionOptions();
            var r = new CollisionResult();
            var ci = CultureInfo.InvariantCulture;
            if (!opt.Enabled) { r.Lines.Add("Collisions were not checked (Check Collisions is off)."); return r; }
            if (cell.Arm.Links == null) { r.Lines.Add("Collisions were not checked: the robot has no 3D model (see the Info of Robot)."); return r; }
            var usable = new List<int>();
            for (int i = 0; i < program.Moves.Count; i++) if (program.Moves[i].Axes != null && program.Moves[i].Reachable) usable.Add(i);
            if (usable.Count == 0) { r.Lines.Add("Collisions were not checked: the robot reaches no pose."); return r; }

            var model = CollisionModel.Make(program, tp, cell, opt);
            r.Model = model;
            double prepared = sw.Elapsed.TotalSeconds;

            // ---- which poses: every n-th move, plus the critical ones
            var picks = new SortedSet<int>();
            if (opt.Poses < 0 || usable.Count <= Math.Max(2, opt.Poses)) { foreach (int i in usable) picks.Add(i); r.Stride = 1; }
            else
            {
                double stride = (usable.Count - 1) / (double)(Math.Max(2, opt.Poses) - 1);
                r.Stride = Math.Max(1, (int)Math.Round(stride));
                for (double k = 0; k < usable.Count; k += stride) picks.Add(usable[(int)k]);
                picks.Add(usable[usable.Count - 1]);
                int evenly = picks.Count;
                // both ends of every move without extrusion (the robot moves furthest there)
                if (program.Extrusion.Count == program.Moves.Count)
                    for (int i = 1; i < program.Moves.Count; i++)
                        if (program.Extrusion[i] <= 0 && program.Moves[i].Axes != null && program.Moves[i - 1].Axes != null) { picks.Add(i - 1); picks.Add(i); }
                // where the tool turns: every TurnStep degrees of change, and the strongest tilt of every layer
                Vector3d last = Vector3d.Unset; double cos = Math.Cos(Math.Max(0.5, opt.TurnStep) * Math.PI / 180);
                var tiltOfLayer = new Dictionary<int, int>();
                foreach (int i in usable)
                {
                    var mv = program.Moves[i];
                    Vector3d z = mv.Part.Dir(tp.Normals[i]); z.Unitize();
                    if (!last.IsValid || z * last < cos) { picks.Add(i); last = z; }
                    else if (picks.Contains(i)) last = z;
                    int layer = i < tp.Layers.Count ? tp.Layers[i] : 0;
                    if (!tiltOfLayer.TryGetValue(layer, out int best) || model.Tilt(i) > model.Tilt(best) + 1e-9) tiltOfLayer[layer] = i;
                }
                foreach (var kv in tiltOfLayer) if (model.Tilt(kv.Value) > 1) picks.Add(kv.Value);
                r.Critical = picks.Count - evenly;
            }

            // ---- run them
            var worst = new Dictionary<string, CollisionEvent>();
            var hitMoves = new SortedDictionary<int, Point3d>();
            foreach (int i in picks)
            {
                var mv = program.Moves[i];
                var pc = model.Check(mv.Axes, mv.External, i, true);
                r.Poses++;
                if (pc.Any) hitMoves[i] = pc.Where.Count > 0 ? pc.Where[0] : mv.Part.Pt(tp.Points[i]);
                foreach (var ev in pc.Events)
                {
                    string key = ev.A + "|" + ev.B + "|" + (ev.Hit ? "hit" : "near");
                    if (!worst.TryGetValue(key, out var w)) { worst[key] = ev; continue; }
                    w.Count++; w.FirstMove = Math.Min(w.FirstMove, ev.Move); w.LastMove = Math.Max(w.LastMove, ev.Move);
                    bool worse = ev.Hit ? ev.Depth > w.Depth : ev.Distance < w.Distance;
                    if (worse) { ev.Count = w.Count; ev.FirstMove = w.FirstMove; ev.LastMove = w.LastMove; worst[key] = ev; }
                }
            }
            r.Checked = true;
            r.Moves.AddRange(hitMoves.Keys);
            r.Points.AddRange(hitMoves.Values);
            // a near miss of a pair that also collides says nothing new
            var hits = worst.Values.Where(e => e.Hit).OrderByDescending(e => e.Depth).ToList();
            var nears = worst.Values.Where(e => !e.Hit && !worst.ContainsKey(e.A + "|" + e.B + "|hit")).OrderBy(e => e.Distance).ToList();
            r.Events.AddRange(hits); r.Events.AddRange(nears);
            r.Seconds = sw.Elapsed.TotalSeconds;

            // ---- report
            string poses = r.Stride > 1 ? string.Format(ci, "{0} poses (every {1}th move and {2} critical ones)", r.Poses, r.Stride, r.Critical) : string.Format(ci, "{0} poses (every move)", r.Poses);
            string scope = "itself, the tool, the printed part, the bed" + (cell.HasExternalAxes ? ", rail and turntable" : "") + (cell.Environment != null && cell.Environment.Count > 0 ? ", the environment" : "");
            r.Lines.Add(hits.Count == 0
                ? string.Format(ci, "No collision: {0} checked in {1:0.0} s, {2:0} mm clearance, robot against {3}{4}.", poses, r.Seconds, opt.Clearance, scope, nears.Count > 0 ? $"; {nears.Count} near miss(es)" : "")
                : string.Format(ci, "COLLISIONS at {0} of {1} checked in {2:0.0} s ({3:0} mm clearance): {4} pair(s) of bodies collide, first at move {5}.", r.Moves.Count, poses, r.Seconds, opt.Clearance, hits.Count, r.FirstMove));
            foreach (var e in hits.Take(12)) r.Lines.Add(e.Text());
            foreach (var e in nears.Take(8)) r.Lines.Add(e.Text());
            if (hits.Count > 12 || nears.Count > 8) r.Lines.Add(string.Format(ci, "... and {0} more.", Math.Max(0, hits.Count - 12) + Math.Max(0, nears.Count - 8)));
            r.Lines.AddRange(model.Notes);
            r.Lines.Add(string.Format(ci, "Collision check: bodies prepared in {0:0.00} s, printed part as {1} bead pieces, nozzle zone {2:0} mm wide and {3:0} mm high left out.", prepared, model.Printed.Count, 2 * model.NozzleRadius, model.NozzleHeight));
            Suggest(r, hits, nears, model, cell, opt);
            r.Lines.AddRange(r.Suggestions.Select(s => "Suggestion: " + s));

            // ---- bodies to show: the worst collision of every pair
            foreach (var e in hits.Take(6))
            {
                var mv = program.Moves[e.Move];
                if (e.BodyA >= 0) { var m = model.BodyMesh(e.BodyA, mv.Axes, mv.External); if (m != null) r.Bodies.Add(m); }
                if (e.BodyB >= 0) { var m = model.BodyMesh(e.BodyB, mv.Axes, mv.External); if (m != null) r.Bodies.Add(m); }
                if (e.Where.IsValid) r.Bodies.Add(Mesh.CreateFromSphere(new Sphere(e.Where, Math.Max(5, Math.Min(50, e.Depth + 5))), 16, 12));
            }
            return r;
        }

        private static void Suggest(CollisionResult r, List<CollisionEvent> hits, List<CollisionEvent> nears, CollisionModel model, RobotCell cell, CollisionOptions opt)
        {
            var ci = CultureInfo.InvariantCulture;
            var s = r.Suggestions;
            var toolPart = hits.Where(e => (e.B == "printed part" || e.B == "bed")).ToList();
            if (toolPart.Count > 0)
            {
                // the smallest tilt at which it hits: below it the checked poses are clear
                double hitTilt = double.MaxValue;
                foreach (var e in toolPart) hitTilt = Math.Min(hitTilt, e.Tilt);
                bool bed = toolPart.Any(e => e.B == "bed"), part = toolPart.Any(e => e.B == "printed part");
                if (hitTilt > 2)
                    s.Add(string.Format(ci, "Keep the tilt of the tool below about {0:0} degrees: it hits {1} from {2:0} degrees on (Angle of Slice, or a flatter guide).", Math.Max(0, Math.Floor(hitTilt - 5)), bed && part ? "the bed and the printed part" : bed ? "the bed" : "the printed part", hitTilt));
                var deep = toolPart.Where(e => e.A == "tool").OrderByDescending(e => e.Depth).FirstOrDefault();
                if (deep != null && deep.ToolHeight > model.NozzleHeight * 2)
                    s.Add(string.Format(ci, "A nozzle about {0:0} mm longer keeps the extruder body clear: it touches {1:0} mm above the tip.", Math.Ceiling(deep.Depth + opt.Clearance), deep.ToolHeight));
                s.Add("Turn the tool about its axis (Turn of Robot Program) so the extruder body leans away from the part.");
                if (cell.HasExternalAxes && cell.ExternalAxes.Any(a => !a.IsRail)) s.Add("Turn the table to another angle (Offset of the turntable), so the robot reaches the point from the open side.");
            }
            var env = hits.Where(e => e.B.StartsWith("the ") || e.B.StartsWith("rail") || e.B.StartsWith("turntable")).ToList();
            if (env.Count > 0)
            {
                var e = env[0];
                if (cell.HasExternalAxes && cell.ExternalAxes.Any(a => a.IsRail)) s.Add($"Change Offset of the rail, so the robot stands further from {e.B} (it hits at move {e.Move}).");
                if (cell.HasExternalAxes && cell.ExternalAxes.Any(a => !a.IsRail)) s.Add("Change Offset of the turntable, so the arm reaches the part from another side.");
                s.Add($"Move or turn the part on the bed, or change where the robot stands: {e.A} hits {e.B}.");
            }
            if (hits.Any(e => e.A.StartsWith("link") || e.A == "tool") && hits.Any(e => e.B.StartsWith("link") || e.B == "robot base"))
                s.Add("The arm runs into itself: change the tool turn (Turn) or the place of the part, so the wrist does not fold back onto the arm.");
            if (hits.Any(e => e.A == "rail carriage" || e.A == "turntable"))
                s.Add("An external axis hits the environment: check the range of the rail and the size and place of the table.");
            if (hits.Count == 0 && nears.Any(e => e.Distance < opt.Clearance / 2))
                s.Add("Clear, but some bodies come close: check them in Robot Simulation at the moves given.");
        }
    }
}
