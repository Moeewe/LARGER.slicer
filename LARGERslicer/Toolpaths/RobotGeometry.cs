using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// The robot as a 3D model. The geometry comes from the library of the Robots plug-in: next
    /// to every robot file (.xml) lies a Rhino file of the same name (.3dm) with one layer per
    /// arm, called RobotArm.MANUFACTURER.MODEL, that holds seven meshes (or seven sublayers 0 to 6
    /// with one mesh each): the base and the six links, drawn in the home position of the arm.
    /// The library folders: Documents/RobotSystems, Documents/Robots and ~/Robots (LibraryFolders).
    /// </summary>
    public static class RobotGeometry
    {
        private static readonly Dictionary<string, Mesh[]> Cache = new Dictionary<string, Mesh[]>();

        /// <summary>Documents/RobotSystems, where the Robots plug-in keeps its library.</summary>
        public static string LibraryFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotSystems");

        /// <summary>
        /// Every folder a robot library may be in: Documents/RobotSystems (the Robots plug-in's own place),
        /// Documents/Robots (where the installers in SETUP copy it) and ~/Robots (Rhino 7 installs).
        /// </summary>
        public static IEnumerable<string> LibraryFolders
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var all = new List<string> { LibraryFolder, Path.Combine(docs, "Robots"), Path.Combine(home, "Robots") };
                return all.Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
            }
        }

        /// <summary>The pose the library meshes are drawn in: the home position of a KUKA arm (A2 -90, A3 90).</summary>
        public static readonly double[] HomePose = { 0, -90, 90, 0, 0, 0 };

        public static string LayerName(RobotArm arm) => "RobotArm." + arm.Manufacturer + "." + arm.Name;

        /// <summary>
        /// Looks for the meshes of the arm: in the file given, else in the .3dm beside the
        /// library file the arm was read from, else in the library folder (the .3dm beside any
        /// .xml that names this arm). Returns where they were found, or null with a reason.
        /// </summary>
        public static string Load(RobotArm arm, string geometryFile, string libraryFile, out string why)
        {
            why = null;
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(geometryFile)) candidates.Add(Expand(geometryFile));
            if (!string.IsNullOrWhiteSpace(libraryFile)) candidates.Add(Path.ChangeExtension(Expand(libraryFile), ".3dm"));
            try
            {
                foreach (string folder in LibraryFolders)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string xml in Directory.GetFiles(folder, "*.xml"))
                    {
                        string text; try { text = File.ReadAllText(xml); } catch { continue; }
                        if (text.IndexOf("\"" + arm.Name + "\"", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add(Path.ChangeExtension(xml, ".3dm"));
                    }
                }
            }
            catch { /* the folder is a convenience */ }

            string layer = LayerName(arm);
            foreach (string file in candidates.Distinct())
            {
                if (!File.Exists(file)) continue;
                string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks + "|" + layer;
                Mesh[] links;
                lock (Cache)
                {
                    if (!Cache.TryGetValue(key, out links))
                    {
                        links = Read(file, layer, out string error);
                        if (links == null) { why = Path.GetFileName(file) + ": " + error; continue; }
                        Cache[key] = links;
                    }
                }
                arm.Links = links;
                arm.GeometrySource = file;
                why = null;
                return file;
            }
            if (why == null)
                why = "No 3D model of " + arm.Name + " was found. Put the robot library of the Robots plug-in (the .xml and the .3dm of the same name) into " + string.Join(" or ", LibraryFolders) + ", or give the .3dm file at Geometry File.";
            return null;
        }

        private static string Expand(string path)
        {
            path = path.Trim().Trim('"');
            if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            return path;
        }

        private static Mesh[] Read(string file, string layerName, out string error)
        {
            error = null;
            try
            {
                using (var f = File3dm.Read(file))
                {
                    if (f == null) { error = "the file could not be read."; return null; }
                    Guid layerId = Guid.Empty; int layer = -1;
                    foreach (var l in f.AllLayers) if (string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase)) { layer = l.Index; layerId = l.Id; break; }
                    if (layer < 0) { error = "it has no layer " + layerName + "."; return null; }
                    // The meshes lie on the layer itself (in the order base, link 1 ... link 6), or
                    // each on a sublayer of its own named 0, 1 ... 6.
                    var order = new Dictionary<int, double> { { layer, -1 } };
                    foreach (var l in f.AllLayers)
                        if (l.ParentLayerId == layerId && double.TryParse(l.Name, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)) order[l.Index] = number;
                    var found = new List<KeyValuePair<double, Mesh>>();
                    int seen = 0;
                    foreach (var o in f.Objects)
                    {
                        seen++;
                        if (!order.TryGetValue(o.Attributes.LayerIndex, out double rank)) continue;
                        Mesh m = o.Geometry as Mesh;
                        if (m == null && o.Geometry is Brep b)
                        {
                            Mesh[] parts;
                            lock (SliceEngine.MeshLock) parts = Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh);
                            if (parts != null && parts.Length > 0) { m = new Mesh(); foreach (var p in parts) m.Append(p); }
                        }
                        // on the layer itself the order in the file counts; on sublayers their number
                        if (m != null) found.Add(new KeyValuePair<double, Mesh>(rank < 0 ? -1e9 + seen : rank, m.DuplicateMesh()));
                    }
                    var meshes = found.OrderBy(k => k.Key).Select(k => k.Value).ToList();
                    if (meshes.Count < 7) { error = Inv.F($"layer {layerName} has {meshes.Count} meshes; the base and six links are needed, in that order."); return null; }
                    return meshes.Take(7).ToArray();
                }
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        /// <summary>
        /// The robot with its tool at the given axis angles (degrees, as on the pendant), in
        /// world coordinates: base, six links, then the tool if it has a mesh. Null if the arm
        /// has no geometry.
        /// </summary>
        public static List<Mesh> Pose(RobotCell cell, double[] axes) => Pose(cell, axes, null);

        /// <summary>The same, with the robot standing where the external axes (a rail) have moved it.</summary>
        public static List<Mesh> Pose(RobotCell cell, double[] axes, double[] external)
        {
            var arm = cell.Arm;
            if (arm.Links == null || axes == null) return null;
            Frame foot = cell.RobotBaseAt(external);
            var now = arm.JointFrames(axes);
            var zero = arm.JointFrames(HomePose); // the pose the meshes are drawn in
            var list = new List<Mesh>();
            for (int i = 0; i < 7; i++)
            {
                Mesh m = arm.Links[i].DuplicateMesh();
                Frame f = i == 0 ? foot : foot.Times(now[i - 1]).Times(zero[i - 1].Inverse());
                m.Transform(ToTransform(f));
                Clean(m);
                list.Add(m);
            }
            if (cell.Tool != null && cell.Tool.Mesh != null)
            {
                Mesh t = cell.Tool.Mesh.DuplicateMesh();
                // the flange of the controller is the model's last frame turned half a turn about its axis
                t.Transform(ToTransform(foot.Times(now[5]).Times(Frame.World.TurnedAboutZ(Math.PI))));
                Clean(t);
                list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// The tools that come with the plug-in, by name: the AE20 extruder of the FH Muenster
        /// cell (name AE20_Extruder), drawn where it sits on the flange. Null if there is none
        /// of that name.
        /// </summary>
        public static Mesh BuiltInTool(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string file = new string(name.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()) + ".3dm";
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase));
                if (resource == null) return null;
                using (var stream = asm.GetManifestResourceStream(resource))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    using (var f = File3dm.FromByteArray(memory.ToArray()))
                    {
                        var joined = new Mesh();
                        foreach (var o in f.Objects) if (o.Geometry is Mesh m) joined.Append(m);
                        return joined.Vertices.Count > 0 ? joined : null;
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// The print cell of the FH Muenster (Weber DXR on the KUKA KR120 R2700-2) as it is drawn
        /// in the definition of the FH Muenster, in the coordinates of the print bed: the origin
        /// is the corner of the bed the print coordinates count from, the bed plate lies under
        /// it. Parts by name: Floor, Bed, Fence, Walls, Glass, Pedestal. Null if not available.
        /// </summary>
        public static Dictionary<string, Mesh> FhMuensterCell()
        {
            lock (Cache)
            {
                if (_cell != null) return _cell;
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    string resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".FHMuenster_Cell.3dm", StringComparison.OrdinalIgnoreCase));
                    if (resource == null) return null;
                    using (var stream = asm.GetManifestResourceStream(resource))
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        using (var f = File3dm.FromByteArray(memory.ToArray()))
                        {
                            var names = new Dictionary<int, string>();
                            foreach (var l in f.AllLayers) names[l.Index] = l.Name;
                            var parts = new Dictionary<string, Mesh>();
                            foreach (var o in f.Objects)
                                if (o.Geometry is Mesh m && names.TryGetValue(o.Attributes.LayerIndex, out string name)) parts[name] = m.DuplicateMesh();
                            _cell = parts.Count > 0 ? parts : null;
                        }
                    }
                }
                catch { _cell = null; }
                return _cell;
            }
        }
        private static Dictionary<string, Mesh> _cell;

        /// <summary>
        /// Where the robot stands in that cell: on top of its pedestal, at the point the definition
        /// of the FH Muenster gives to Load Robot System, turned like the bed.
        /// </summary>
        public static Plane FhMuensterRobotBase()
        {
            var cell = FhMuensterCell();
            // as the robot is set up in the definition of the FH Muenster (BeispielRoboter.gh): the
            // base point of Load Robot System, the robot's own X axis along the X axis of the bed
            return new Plane(new Point3d(651.5, -1448.0, 211.107), Vector3d.XAxis, Vector3d.YAxis);
        }

        /// <summary>
        /// Moving a mesh can press the corners of a very small face onto one point; such a face
        /// makes the whole mesh invalid, and Rhino then neither bakes nor intersects it.
        /// </summary>
        private static void Clean(Mesh m)
        {
            if (m.IsValid) return;
            m.Faces.CullDegenerateFaces();
            m.Vertices.CullUnused();
            m.Normals.ComputeNormals();
            m.Compact();
        }

        public static Transform ToTransform(Frame f)
        {
            var t = Transform.Identity;
            t.M00 = f.X.X; t.M01 = f.Y.X; t.M02 = f.Z.X; t.M03 = f.O.X;
            t.M10 = f.X.Y; t.M11 = f.Y.Y; t.M12 = f.Z.Y; t.M13 = f.O.Y;
            t.M20 = f.X.Z; t.M21 = f.Y.Z; t.M22 = f.Z.Z; t.M23 = f.O.Z;
            return t;
        }
    }
}
