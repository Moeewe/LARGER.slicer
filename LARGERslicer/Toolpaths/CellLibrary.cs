using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Reads and writes the 3D models of machines and print cells as plain Rhino files: one layer
    /// per part, named after the part. Everyone can draw their own in Rhino and save it as .3dm;
    /// no code is needed.
    /// </summary>
    public static class ModelFile
    {
        public class Content
        {
            /// <summary>Everything on one layer, joined into one mesh, by layer name.</summary>
            public Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<Point3d>> Points = new Dictionary<string, List<Point3d>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<Line>> Lines = new Dictionary<string, List<Line>>(StringComparer.OrdinalIgnoreCase);
        }

        public static Content Read(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "There is no file " + path + "."; return null; }
                using (var f = File3dm.Read(path))
                {
                    if (f == null) { error = path + " is not a Rhino file."; return null; }
                    var names = new Dictionary<int, string>();
                    foreach (var l in f.AllLayers) names[l.Index] = l.Name;
                    var solids = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
                    var c = new Content();
                    foreach (var o in f.Objects)
                    {
                        if (!names.TryGetValue(o.Attributes.LayerIndex, out string layer)) layer = "Default";
                        var g = o.Geometry;
                        if (g is Point pt) Add(c.Points, layer, pt.Location);
                        else if (g is Curve cv) { if (cv.IsLinear()) Add(c.Lines, layer, new Line(cv.PointAtStart, cv.PointAtEnd)); }
                        else if (g is Mesh || g is Brep || g is Extrusion || g is Surface || g is SubD) Add(solids, layer, (object)g);
                    }
                    foreach (var kv in solids)
                    {
                        var m = SliceEngine.ToMesh(kv.Value, 0.5, null);
                        if (m != null && m.Faces.Count > 0) c.Meshes[kv.Key] = m;
                    }
                    if (c.Meshes.Count == 0) { error = path + " has no meshes, surfaces or solids."; return null; }
                    return c;
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static void Add<T>(Dictionary<string, List<T>> d, string key, T value)
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = new List<T>();
            list.Add(value);
        }

        /// <summary>Writes meshes on layers of their names, and each plane as a point with a line along its X axis.</summary>
        public static void Write(string path, IDictionary<string, Mesh> meshes, IDictionary<string, Plane> planes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var f = new File3dm())
            {
                f.Settings.ModelUnitSystem = Rhino.UnitSystem.Millimeters;
                Func<string, int> layer = name =>
                {
                    var existing = f.AllLayers.FirstOrDefault(l => l.Name == name);
                    if (existing != null) return existing.Index;
                    f.AllLayers.Add(new Rhino.DocObjects.Layer { Name = name, Color = System.Drawing.Color.Gray });
                    return f.AllLayers.Count - 1;
                };
                foreach (var kv in meshes) if (kv.Value != null) f.Objects.AddMesh(kv.Value, new Rhino.DocObjects.ObjectAttributes { LayerIndex = layer(kv.Key) });
                if (planes != null)
                    foreach (var kv in planes)
                    {
                        int li = layer(kv.Key);
                        f.Objects.AddPoint(kv.Value.Origin, new Rhino.DocObjects.ObjectAttributes { LayerIndex = li });
                        f.Objects.AddLine(new Line(kv.Value.Origin, kv.Value.Origin + kv.Value.XAxis * 300), new Rhino.DocObjects.ObjectAttributes { LayerIndex = li });
                    }
                f.Write(path, 7);
            }
        }

        /// <summary>A frame drawn as a point, and optionally a line starting there along its X axis. Z stays up.</summary>
        public static Plane? Frame(Content c, string layer)
        {
            if (c == null || !c.Points.TryGetValue(layer, out var pts) || pts.Count == 0) return null;
            Point3d o = pts[0];
            Vector3d x = Vector3d.XAxis;
            if (c.Lines.TryGetValue(layer, out var lines) && lines.Count > 0)
            {
                var ln = lines.OrderBy(l => Math.Min(l.From.DistanceTo(o), l.To.DistanceTo(o))).First();
                Vector3d d = ln.From.DistanceTo(o) <= ln.To.DistanceTo(o) ? ln.To - ln.From : ln.From - ln.To;
                d.Z = 0;
                if (d.Unitize()) x = d;
            }
            return new Plane(o, x, Vector3d.CrossProduct(Vector3d.ZAxis, x));
        }
    }

    /// <summary>A print cell: what stands around the robot, where the robot stands, where the print counts from.</summary>
    public class CellModel
    {
        public string Name = "Cell";
        public string Source = "built in";
        /// <summary>The parts by name. Bed is drawn but not checked for collisions; every other part is.</summary>
        public Dictionary<string, Mesh> Parts = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        public Plane RobotBase = Plane.WorldXY;
        public Plane WorkBase = Plane.WorldXY;
        public override string ToString() => Inv.F($"Cell {Name} ({Source})");
    }

    /// <summary>
    /// Print cells: the cell of the FH Muenster is built in; your own are .3dm files in
    /// Documents/LARGER/Cells. Draw the cell in Rhino in millimetres, in the coordinates of the
    /// print bed (the origin is where the print counts from), and put each part on a layer:
    ///   Bed        - the print bed (drawn, not checked for collisions: the nozzle runs right above it)
    ///   Robot Base - a point where the foot of the robot stands, and a line from it along the robot's X axis
    ///   Work Base  - optional: a point (and line) where the print counts from; empty = the origin
    ///   any other layer (Floor, Fence, Glass, Walls, Pedestal, Table ...) - checked for collisions
    /// The Export input of Cell Geometry writes the built-in cell this way, as a start.
    /// </summary>
    public static class CellLibrary
    {
        public static string Folder => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "LARGER", "Cells");
        public const string RobotBaseLayer = "Robot Base", WorkBaseLayer = "Work Base", BedLayer = "Bed";

        public static CellModel FhMuenster()
        {
            var parts = RobotGeometry.FhMuensterCell();
            if (parts == null) return null;
            var c = new CellModel { Name = "FH Muenster (Weber DXR, KUKA KR120)", RobotBase = RobotGeometry.FhMuensterRobotBase() };
            foreach (var kv in parts) c.Parts[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>The built-in cell first, then every .3dm file of the folder, by name.</summary>
        public static List<CellModel> All(List<string> log = null)
        {
            var list = new List<CellModel>();
            var fh = FhMuenster(); if (fh != null) list.Add(fh);
            try
            {
                if (Directory.Exists(Folder))
                    foreach (string file in Directory.GetFiles(Folder, "*.3dm").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        var c = Load(file, out string error);
                        if (c != null) list.Add(c); else log?.Add(Path.GetFileName(file) + ": " + error);
                    }
            }
            catch (Exception ex) { log?.Add("The cells folder could not be read: " + ex.Message); }
            return list;
        }

        public static string[] Names() => All().Select(c => c.Name).ToArray();

        public static CellModel Load(string path, out string error)
        {
            var content = ModelFile.Read(path, out error);
            if (content == null) return null;
            var robot = ModelFile.Frame(content, RobotBaseLayer);
            if (robot == null) { error = "it has no layer 'Robot Base' with a point where the robot stands."; return null; }
            var c = new CellModel { Name = Path.GetFileNameWithoutExtension(path), Source = path, RobotBase = robot.Value, WorkBase = ModelFile.Frame(content, WorkBaseLayer) ?? Plane.WorldXY };
            foreach (var kv in content.Meshes)
                if (!kv.Key.Equals(RobotBaseLayer, StringComparison.OrdinalIgnoreCase) && !kv.Key.Equals(WorkBaseLayer, StringComparison.OrdinalIgnoreCase)) c.Parts[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>Writes the cell as a .3dm in the form Load reads; returns the path.</summary>
        public static string Export(CellModel cell, string path = null)
        {
            path = path ?? Path.Combine(Folder, OutputFile.Clean(cell.Name) + " (copy to edit).3dm");
            var planes = new Dictionary<string, Plane> { [RobotBaseLayer] = cell.RobotBase, [WorkBaseLayer] = cell.WorkBase };
            ModelFile.Write(path, cell.Parts, planes);
            return path;
        }

        /// <summary>Collision parts: every part but the bed; the walls only if asked.</summary>
        public static List<Mesh> Environment(CellModel cell, bool walls) =>
            cell.Parts.Where(kv => !kv.Key.Equals(BedLayer, StringComparison.OrdinalIgnoreCase) && (walls || !kv.Key.Equals("Walls", StringComparison.OrdinalIgnoreCase))).Select(kv => kv.Value).ToList();
    }
}
