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
    /// arm, called RobotArm.MANUFACTURER.MODEL, that holds seven meshes: the base and the six
    /// links, drawn in the pose in which the kinematic model has all its angles at zero.
    /// The library folder is Documents/RobotSystems.
    /// </summary>
    public static class RobotGeometry
    {
        private static readonly Dictionary<string, Mesh[]> Cache = new Dictionary<string, Mesh[]>();

        /// <summary>Documents/RobotSystems, where the Robots plug-in keeps its library.</summary>
        public static string LibraryFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotSystems");

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
                if (Directory.Exists(LibraryFolder))
                    foreach (string xml in Directory.GetFiles(LibraryFolder, "*.xml"))
                    {
                        string text; try { text = File.ReadAllText(xml); } catch { continue; }
                        if (text.IndexOf("\"" + arm.Name + "\"", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add(Path.ChangeExtension(xml, ".3dm"));
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
                why = "No 3D model of " + arm.Name + " was found. Put the robot library of the Robots plug-in (the .xml and the .3dm of the same name) into " + LibraryFolder + ", or give the .3dm file at Geometry File.";
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
                    int layer = -1;
                    foreach (var l in f.AllLayers) if (string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase)) { layer = l.Index; break; }
                    if (layer < 0) { error = "it has no layer " + layerName + "."; return null; }
                    var meshes = new List<Mesh>();
                    foreach (var o in f.Objects)
                    {
                        if (o.Attributes.LayerIndex != layer) continue;
                        Mesh m = o.Geometry as Mesh;
                        if (m == null && o.Geometry is Brep b)
                        {
                            var parts = Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh);
                            if (parts != null && parts.Length > 0) { m = new Mesh(); foreach (var p in parts) m.Append(p); }
                        }
                        if (m != null) meshes.Add(m.DuplicateMesh());
                    }
                    if (meshes.Count < 7) { error = $"layer {layerName} has {meshes.Count} meshes; the base and six links are needed, in that order."; return null; }
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
        public static List<Mesh> Pose(RobotCell cell, double[] axes)
        {
            var arm = cell.Arm;
            if (arm.Links == null || axes == null) return null;
            var now = arm.JointFrames(axes);
            var zero = arm.JointFrames(new double[] { 0, 0, 90, 0, 0, 0 }); // all angles of the model at zero
            var list = new List<Mesh>();
            for (int i = 0; i < 7; i++)
            {
                Mesh m = arm.Links[i].DuplicateMesh();
                Frame f = i == 0 ? arm.Base : arm.Base.Times(now[i - 1]).Times(zero[i - 1].Inverse());
                m.Transform(ToTransform(f));
                list.Add(m);
            }
            if (cell.Tool != null && cell.Tool.Mesh != null)
            {
                Mesh t = cell.Tool.Mesh.DuplicateMesh();
                t.Transform(ToTransform(arm.Base.Times(now[5])));
                list.Add(t);
            }
            return list;
        }

        public static Transform ToTransform(Frame f)
        {
            var t = Transform.Identity;
            t.M00 = f.X.X; t.M01 = f.Y.X; t.M02 = f.Z.X; t.M03 = f.O.X;
            t.M10 = f.X.Y; t.M11 = f.Y.Y; t.M12 = f.Z.Y; t.M13 = f.O.Y;
            t.M20 = f.X.Z; t.M21 = f.Y.Z; t.M22 = f.Z.Z; t.M23 = f.O.Z;
            return t;
        }

        public class CollisionResult
        {
            public bool Checked;
            public int Poses, Hits;
            public int FirstMove = -1;
            public List<int> Moves = new List<int>();
            public List<Mesh> Colliding = new List<Mesh>();
            public string Report = "";
        }

        /// <summary>
        /// Looks for collisions along the program. Poses are taken along the path, at most
        /// maxPoses of them, evenly spread. In every pose the moving front of the robot (the
        /// tool, the wrist and the forearm: links 4 to 6) is tested against the standing rear
        /// (base, links 1 and 2) and against everything given as environment (bed, table, cage).
        /// The print itself is not part of the test: the nozzle touches it all the time.
        /// </summary>
        public static CollisionResult Collisions(RobotProgramResult program, RobotCell cell, IList<Mesh> environment, int maxPoses)
        {
            var r = new CollisionResult();
            if (cell.Arm.Links == null) { r.Report = "Collisions were not checked: the robot has no 3D model."; return r; }
            var usable = new List<int>();
            for (int i = 0; i < program.Moves.Count; i++) if (program.Moves[i].Axes != null && program.Moves[i].Reachable) usable.Add(i);
            if (usable.Count == 0) { r.Report = "Collisions were not checked: no reachable pose."; return r; }
            maxPoses = Math.Max(2, maxPoses);
            int step = Math.Max(1, usable.Count / maxPoses);
            var env = (environment ?? new List<Mesh>()).Where(m => m != null && m.IsValid).ToList();
            r.Checked = true;
            for (int k = 0; k < usable.Count; k += step)
            {
                int i = usable[k];
                var pose = Pose(cell, program.Moves[i].Axes);
                r.Poses++;
                var front = new List<Mesh> { pose[4], pose[5], pose[6] };
                if (pose.Count > 7) front.Add(pose[7]);
                var rear = new List<Mesh> { pose[0], pose[1], pose[2] };
                rear.AddRange(env);
                bool hit = false;
                foreach (var a in front)
                {
                    foreach (var b in rear)
                    {
                        if (!Overlap(a.GetBoundingBox(false), b.GetBoundingBox(false))) continue;
                        var lines = Rhino.Geometry.Intersect.Intersection.MeshMeshFast(a, b);
                        if (lines != null && lines.Length > 0)
                        {
                            hit = true;
                            if (r.FirstMove < 0) { r.FirstMove = i; r.Colliding.Add(a); r.Colliding.Add(b); }
                            break;
                        }
                    }
                    if (hit) break;
                }
                if (hit) { r.Hits++; r.Moves.Add(i); }
            }
            string scope = env.Count > 0 ? $"itself and {env.Count} environment mesh(es)" : "itself (connect bed, table and cage as Environment to check them too)";
            string tool = cell.Tool != null && cell.Tool.Mesh != null ? "with the tool" : "without the tool (give the tool a mesh)";
            r.Report = r.Hits == 0
                ? $"No collision: {r.Poses} poses along the path checked, robot {tool} against {scope}."
                : $"COLLISION in {r.Hits} of {r.Poses} checked poses, first at move {r.FirstMove}: robot {tool} against {scope}. Move the part, turn the tool (Turn) or change where the robot stands.";
            return r;
        }

        private static bool Overlap(BoundingBox a, BoundingBox b)
            => a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;
    }
}
