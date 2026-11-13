using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// How machines and robots are drawn in the viewport, the same for all of them: matte
    /// materials made from the object colours, and meshes with crisp edges (split where faces
    /// meet at a sharp angle, normals pointing out), so a 40 x 40 tube looks like a tube and not
    /// like a sausage.
    /// </summary>
    public static class MachineLook
    {
        private static readonly Dictionary<long, Rhino.Display.DisplayMaterial> Materials = new Dictionary<long, Rhino.Display.DisplayMaterial>();

        /// <summary>A matte material of this colour, as the robot is drawn (kept, so drawing makes no new ones every frame).</summary>
        public static Rhino.Display.DisplayMaterial Material(Color c, double transparency = 0)
        {
            long key = ((long)c.ToArgb() << 8) ^ (long)Math.Round(Math.Max(0, Math.Min(1, transparency)) * 255);
            lock (Materials)
            {
                if (!Materials.TryGetValue(key, out var m))
                {
                    // the robot's materials: Rhino's defaults for a coloured, non-shiny surface
                    m = transparency > 0 ? new Rhino.Display.DisplayMaterial(c, transparency) : new Rhino.Display.DisplayMaterial(c);
                    Materials[key] = m;
                }
                return m;
            }
        }

        /// <summary>Red, for what collides.</summary>
        public static readonly Color Hit = Color.FromArgb(230, 30, 30);
        /// <summary>The cell around a robot (fence, walls), when it is shown.</summary>
        public static readonly Color Cell = Color.FromArgb(200, 200, 196);

        /// <summary>Faces that meet at more than this angle get an edge of their own (degrees).</summary>
        public const double CreaseAngle = 35;

        /// <summary>
        /// A copy of the mesh made for drawing: degenerate faces removed, closed pieces turned so
        /// their normals point out, split along the sharp edges (Unweld) with normals for each
        /// side, so flat faces stay flat and round parts stay round.
        /// </summary>
        public static Mesh Prepare(Mesh source)
        {
            if (source == null) return null;
            var m = source.DuplicateMesh();
            try
            {
                m.Faces.CullDegenerateFaces();
                m.Vertices.CombineIdentical(true, true);
                m.Vertices.CullUnused();
                m.FaceNormals.ComputeFaceNormals();
                if (m.IsClosed && m.SolidOrientation() < 0) m.Flip(true, true, true);
                // welded meshes (8 corners per box, as many files come) get averaged normals: shading
                // then runs round over every edge. Split the sharp edges and give each side its own normal.
                m.Unweld(CreaseAngle * Math.PI / 180.0, true);
                if (m.Normals.Count != m.Vertices.Count) m.Normals.ComputeNormals();
                m.Compact();
            }
            catch { if (source.Normals.Count != source.Vertices.Count) { m = source.DuplicateMesh(); m.Normals.ComputeNormals(); } }
            return m;
        }
    }

    /// <summary>
    /// The 3D model of a printer that can be posed: a Rhino file with one layer per moving
    /// group, in the coordinates of the machine (millimetres, origin = where the G-code counts
    /// from, on the bed surface). Layers:
    ///   Frame (and any other layer) - stands still
    ///   Bed - stands still; on a bed slinger it moves in Y (the bed carries the part)
    ///   Z - moves with Z (the gantry frame on the vertical axes; on a bed slinger the X beam)
    ///   Y - moves with Y and Z (the cross beam)
    ///   X - moves with X, Y and Z (the head: everything that moves with the nozzle; on a delta the effector)
    ///   TCP - one point: the nozzle tip in the pose the model is drawn in
    /// Each object keeps its object colour. The Ginger One is built in (GingerOne.3dm, see
    /// Resources/GingerOne.README.md); docs/OWN_MACHINES.md says how to draw your own.
    /// </summary>
    public class PrinterModel
    {
        public class Part
        {
            public string Group;
            public Mesh Mesh;
            public Color Color;
        }

        public string Name = "";
        public List<Part> Parts = new List<Part>();
        /// <summary>The nozzle tip in the pose the model is drawn in.</summary>
        public Point3d Tcp = Point3d.Origin;
        /// <summary>True when the file has the layers Z, Y or X, so it can be posed.</summary>
        public bool CanMove => Parts.Any(p => p.Group == "X" || p.Group == "Y" || p.Group == "Z");
        /// <summary>True when a point on layer TCP says where the nozzle tip is.</summary>
        public bool HasTcp;

        public BoundingBox Box { get { var b = BoundingBox.Empty; foreach (var p in Parts) b.Union(p.Mesh.GetBoundingBox(false)); return b; } }

        /// <summary>The parts with the head at the point p (a gantry): Z moves by (0, 0, dz), Y by (0, dy, dz), X by (dx, dy, dz).</summary>
        public List<Part> Pose(Point3d p) => Pose(p, MachineKind.Gantry, out _);

        /// <summary>
        /// The parts with the nozzle at the path point p (machine coordinates), for this kind of machine.
        /// Gantry, belt: Z by (0, 0, dz), Y by (0, dy, dz), X by (dx, dy, dz).
        /// Bed slinger: the bed (and Y) by (0, -dy, 0), Z by (0, 0, dz), X by (dx, 0, dz).
        /// Delta: X (the effector) by (dx, dy, dz), the rest stands.
        /// partShift: how far the part on the bed moves with it (only a bed slinger moves its bed).
        /// </summary>
        public List<Part> Pose(Point3d p, MachineKind kind, out Vector3d partShift)
        {
            var list = new List<Part>();
            foreach (var part in Parts)
            {
                Vector3d move = Move(part.Group, p, kind, out partShift);
                var m = part.Mesh;
                if (!move.IsZero) { m = m.DuplicateMesh(); m.Translate(move); }
                list.Add(new Part { Group = part.Group, Mesh = m, Color = part.Color });
            }
            Move("Bed", p, kind, out partShift);
            return list;
        }

        /// <summary>How far a group moves when the nozzle is at p (see Pose).</summary>
        public Vector3d Move(string group, Point3d p, MachineKind kind, out Vector3d partShift)
        {
            Vector3d d = p - Tcp;
            partShift = Vector3d.Zero;
            switch (kind)
            {
                case MachineKind.BedSlinger:
                    partShift = new Vector3d(0, -d.Y, 0);
                    return group == "X" ? new Vector3d(d.X, 0, d.Z) : group == "Z" ? new Vector3d(0, 0, d.Z) : group == "Y" || group == "Bed" ? partShift : Vector3d.Zero;
                case MachineKind.Delta:
                    return group == "X" ? d : Vector3d.Zero;
                default:
                    return group == "X" ? d : group == "Y" ? new Vector3d(0, d.Y, d.Z) : group == "Z" ? new Vector3d(0, 0, d.Z) : Vector3d.Zero;
            }
        }

        /// <summary>The meshes of these groups joined into one, in the pose the model is drawn in (null if there are none).</summary>
        public Mesh Joined(params string[] groups)
        {
            var j = new Mesh();
            foreach (var p in Parts) if (groups.Contains(p.Group)) j.Append(p.Mesh);
            return j.Faces.Count > 0 ? j : null;
        }

        private static readonly Dictionary<string, PrinterModel> FileCache = new Dictionary<string, PrinterModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a model file (kept while the file does not change: Printer Simulation asks for it
        /// at every frame). .3dm with the layers above, or .obj with one group (g) per layer.
        /// </summary>
        public static PrinterModel FromFile(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "There is no file " + path + "."; return null; }
                string key = Path.GetFullPath(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks;
                lock (FileCache) if (FileCache.TryGetValue(key, out var cached)) return cached;
                PrinterModel m;
                if (string.Equals(Path.GetExtension(path), ".obj", StringComparison.OrdinalIgnoreCase)) m = FromObj(File.ReadAllLines(path), out error);
                else using (var f = File3dm.Read(path)) m = From(f, out error);
                if (m == null) return null;
                m.Name = Path.GetFileNameWithoutExtension(path);
                lock (FileCache) FileCache[key] = m;
                return m;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static readonly Dictionary<string, PrinterModel> Cache = new Dictionary<string, PrinterModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A model that comes with the plug-in, by name (GingerOne). Null if there is none.</summary>
        public static PrinterModel BuiltIn(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                PrinterModel model = null;
                try
                {
                    var asm = typeof(PrinterModel).Assembly;
                    string resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name + ".3dm", StringComparison.OrdinalIgnoreCase));
                    if (resource != null)
                        using (var stream = asm.GetManifestResourceStream(resource))
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            using (var f = File3dm.FromByteArray(memory.ToArray()))
                            {
                                model = From(f, out _);
                                if (model != null) model.Name = name;
                            }
                        }
                }
                catch { model = null; }
                Cache[name] = model;
                return model;
            }
        }

        /// <summary>Writes a model that comes with the plug-in to a .3dm file, e.g. next to a machine saved from it.</summary>
        public static bool WriteBuiltIn(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var asm = typeof(PrinterModel).Assembly;
                string resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name + ".3dm", StringComparison.OrdinalIgnoreCase));
                if (resource == null) return false;
                using (var stream = asm.GetManifestResourceStream(resource))
                using (var file = File.Create(path)) stream.CopyTo(file);
                return true;
            }
            catch { return false; }
        }

        /// <summary>A copy whose meshes can be changed without touching the cached model.</summary>
        public PrinterModel Duplicate()
        {
            var c = new PrinterModel { Name = Name, Tcp = Tcp, HasTcp = HasTcp };
            foreach (var p in Parts) c.Parts.Add(new Part { Group = p.Group, Mesh = p.Mesh.DuplicateMesh(), Color = p.Color });
            return c;
        }

        /// <summary>Meshes a surface, solid or SubD of the model file, as a render mesh (not welded: the edges stay crisp).</summary>
        private static Mesh MeshOf(GeometryBase g)
        {
            if (g is Mesh mesh) return mesh;
            Brep brep = g as Brep ?? (g as Extrusion)?.ToBrep() ?? (g as Surface)?.ToBrep();
            if (g is SubD sd) { lock (SliceEngine.MeshLock) return Mesh.CreateFromSubD(sd, 3); }
            if (brep == null) return null;
            Mesh[] parts;
            lock (SliceEngine.MeshLock) parts = Mesh.CreateFromBrep(brep.DuplicateBrep(), MeshingParameters.QualityRenderMesh);
            if (parts == null || parts.Length == 0) return null;
            var j = new Mesh(); foreach (var p in parts) j.Append(p);
            return j;
        }

        private static PrinterModel From(File3dm f, out string error)
        {
            error = null;
            if (f == null) { error = "Not a Rhino file."; return null; }
            var names = new Dictionary<int, string>();
            var layerColors = new Dictionary<int, Color>();
            foreach (var l in f.AllLayers) { names[l.Index] = l.Name; layerColors[l.Index] = l.Color; }
            var model = new PrinterModel();
            // one mesh per group and colour: fewer, larger meshes draw faster
            var joined = new Dictionary<string, Mesh>();
            var colorOf = new Dictionary<string, Color>();
            int skipped = 0;
            foreach (var o in f.Objects)
            {
                if (!names.TryGetValue(o.Attributes.LayerIndex, out string layer)) layer = "Frame";
                string group = Group(layer);
                Color color = o.Attributes.ColorSource == Rhino.DocObjects.ObjectColorSource.ColorFromObject ? o.Attributes.ObjectColor
                    : layerColors.TryGetValue(o.Attributes.LayerIndex, out var lc) ? lc : Color.Gray;
                var g = o.Geometry;
                if (g is Rhino.Geometry.Point pt) { if (group == "TCP" && !model.HasTcp) { model.Tcp = pt.Location; model.HasTcp = true; } continue; }
                if (group == "TCP") continue;
                Mesh mesh = MeshOf(g);
                if (mesh == null || mesh.Faces.Count == 0) { if (!(g is Curve) && !(g is TextEntity) && !(g is AnnotationBase)) skipped++; continue; }
                string key = group + "|" + color.ToArgb();
                if (!joined.TryGetValue(key, out var j)) { joined[key] = j = new Mesh(); colorOf[key] = color; }
                j.Append(mesh);
            }
            foreach (var kv in joined)
                model.Parts.Add(new Part { Group = kv.Key.Substring(0, kv.Key.IndexOf('|')), Mesh = MachineLook.Prepare(kv.Value), Color = colorOf[kv.Key] });
            if (model.Parts.Count == 0) { error = "The file has no meshes, surfaces or solids." + (skipped > 0 ? Inv.F($" {skipped} object(s) (blocks?) could not be used: explode blocks before saving.") : ""); return null; }
            return model;
        }

        /// <summary>An OBJ model: one group (g) per layer, colours from the material names (usemtl cRRGGBB) where they are written that way.</summary>
        private static PrinterModel FromObj(string[] lines, out string error)
        {
            error = null;
            var v = new List<Point3d>();
            var joined = new Dictionary<string, Mesh>();
            var colorOf = new Dictionary<string, Color>();
            var map = new Dictionary<string, Dictionary<int, int>>();
            string group = "Frame", mtl = "";
            var model = new PrinterModel();
            foreach (string raw in lines)
            {
                var t = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                switch (t[0])
                {
                    case "v": if (t.Length >= 4) v.Add(new Point3d(Num(t[1]), Num(t[2]), Num(t[3]))); break;
                    case "g": case "o": group = Group(t.Length > 1 ? string.Join(" ", t.Skip(1)) : "Frame"); break;
                    case "usemtl": mtl = t.Length > 1 ? t[1] : ""; break;
                    case "p": if (group == "TCP" && t.Length > 1 && int.TryParse(t[1].Split('/')[0], out int pi)) { model.Tcp = v[pi < 0 ? v.Count + pi : pi - 1]; model.HasTcp = true; } break;
                    case "f":
                        if (t.Length < 4 || group == "TCP") break;
                        string key = group + "|" + mtl;
                        if (!joined.TryGetValue(key, out var m)) { joined[key] = m = new Mesh(); colorOf[key] = ObjColor(mtl); map[key] = new Dictionary<int, int>(); }
                        var idx = new List<int>();
                        foreach (var s in t.Skip(1))
                        {
                            if (!int.TryParse(s.Split('/')[0], out int k)) continue;
                            k = k < 0 ? v.Count + k : k - 1;
                            if (k < 0 || k >= v.Count) continue;
                            if (!map[key].TryGetValue(k, out int local)) { local = m.Vertices.Add(v[k]); map[key][k] = local; }
                            idx.Add(local);
                        }
                        for (int i = 1; i + 1 < idx.Count; i++) m.Faces.AddFace(idx[0], idx[i], idx[i + 1]);
                        break;
                }
            }
            foreach (var kv in joined)
                if (kv.Value.Faces.Count > 0) model.Parts.Add(new Part { Group = kv.Key.Substring(0, kv.Key.IndexOf('|')), Mesh = MachineLook.Prepare(kv.Value), Color = colorOf[kv.Key] });
            if (model.Parts.Count == 0) { error = "The OBJ file has no faces."; return null; }
            return model;
        }

        private static double Num(string s) => double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

        private static Color ObjColor(string mtl)
        {
            string h = (mtl ?? "").TrimStart('c', 'C', '#');
            if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int rgb))
                return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return Color.FromArgb(150, 152, 158);
        }

        /// <summary>The moving group of a layer: X, Y, Z, TCP, Bed, or Frame for everything that stands still.
        /// Sublayers count by their last name (Machine::X). Head and Effector are taken as X.</summary>
        public static string Group(string layer)
        {
            string l = (layer ?? "").Trim();
            int dot = l.LastIndexOf("::", StringComparison.Ordinal);
            if (dot >= 0) l = l.Substring(dot + 2);
            if (l.Equals("X", StringComparison.OrdinalIgnoreCase) || l.Equals("Head", StringComparison.OrdinalIgnoreCase) || l.Equals("Effector", StringComparison.OrdinalIgnoreCase)) return "X";
            if (l.Equals("Y", StringComparison.OrdinalIgnoreCase)) return "Y";
            if (l.Equals("Z", StringComparison.OrdinalIgnoreCase)) return "Z";
            if (l.Equals("TCP", StringComparison.OrdinalIgnoreCase)) return "TCP";
            if (l.Equals("Bed", StringComparison.OrdinalIgnoreCase)) return "Bed";
            return "Frame";
        }
    }

    /// <summary>
    /// A print on a G-code machine, ready to be played (Machine G-code gives it to Printer
    /// Simulation, as Robot Program gives its program to Robot Simulation): the machine, the path
    /// in machine coordinates and the moment each point is reached, from the feedrates of the
    /// G-code.
    /// </summary>
    public class PrinterRun
    {
        public MachineDefinition Machine;
        public Toolpath Path;
        /// <summary>Seconds from the start at which each point is reached.</summary>
        public List<double> Seconds = new List<double>();
        public double Duration => Seconds.Count > 0 ? Seconds[Seconds.Count - 1] : 0;
        public override string ToString() => Inv.F($"Printer run ({Machine?.Name}, {Path?.Count ?? 0} points, {Duration / 60:0.#} min)");

        /// <summary>The run from the speeds of the processed path (mm/min per move; travel at the machine's travel speed).</summary>
        public static PrinterRun Make(Toolpath tp, MachineDefinition machine, PathEngine.ProcessResult pr = null)
        {
            var run = new PrinterRun { Machine = machine, Path = tp };
            if (tp == null || tp.Count == 0) return run;
            if (pr == null)
            {
                var copy = tp.Duplicate(); copy.Settings = tp.Settings.Duplicate();
                if (machine != null) copy.Settings.Machine = machine.Profile.Duplicate();
                try { pr = PathEngine.Process(copy, false); } catch { pr = null; }
            }
            var st = tp.Settings;
            double travel = machine != null && machine.TravelSpeed > 0 ? machine.TravelSpeed : Math.Max(st.Speed, st.MaxSpeed);
            double t = 0;
            for (int i = 0; i < tp.Count; i++)
            {
                if (i > 0)
                {
                    bool print = tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Travel;
                    double f = !print ? travel : pr != null && i < pr.Speed.Count && pr.Speed[i] > 0 ? pr.Speed[i] : st.Speed;
                    t += tp.Points[i].DistanceTo(tp.Points[i - 1]) / Math.Max(1.0, f) * 60.0;
                }
                run.Seconds.Add(t);
            }
            return run;
        }

        /// <summary>Where the nozzle is at this moment: k = the point it moves to (1 .. Count-1), 0 for a path of one point.</summary>
        public Point3d At(double seconds, out int k)
        {
            k = 0;
            if (Path == null || Path.Count == 0) return Point3d.Unset;
            if (Path.Count == 1) return Path.Points[0];
            int i = Seconds.BinarySearch(seconds);
            if (i < 0) i = ~i;
            k = Math.Max(1, Math.Min(Path.Count - 1, i));
            double a = Seconds[k - 1], b = Seconds[k];
            double t = b - a > 1e-12 ? Math.Max(0, Math.Min(1, (seconds - a) / (b - a))) : 1;
            return Path.Points[k - 1] + (Path.Points[k] - Path.Points[k - 1]) * t;
        }

        private GantryCollision _collider; private bool _colliderMade;
        /// <summary>The head, beam and frame of the machine prepared for the collision check (made once, when first asked for).</summary>
        public GantryCollision Collider
        {
            get
            {
                if (!_colliderMade)
                {
                    _colliderMade = true;
                    try { _collider = GantryCollision.Make(Path, Machine); } catch { _collider = null; }
                }
                return _collider;
            }
        }

        private CollisionResult _report;
        /// <summary>The collision check along the whole path (about 300 poses plus travel ends and layer ends), made once.</summary>
        public CollisionResult Report
        {
            get
            {
                if (_report == null && Collider != null) { try { _report = Collider.Run(300); } catch (Exception ex) { _report = new CollisionResult(); _report.Lines.Add("The collision check failed: " + ex.Message); } }
                return _report;
            }
        }
    }
}
