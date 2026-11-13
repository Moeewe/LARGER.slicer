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
    /// The 3D model of a gantry printer that can be posed: a Rhino file with one layer per moving
    /// group, in the coordinates of the machine (millimetres, origin = where the G-code counts
    /// from, on the bed surface). Layers:
    ///   Frame, Bed (and any other layer) - stand still
    ///   Z - moves with Z (the gantry frame on the vertical axes)
    ///   Y - moves with Y and Z (the cross beam)
    ///   X - moves with X, Y and Z (the head: everything that moves with the nozzle)
    ///   TCP - one point: the nozzle tip in the pose the model is drawn in
    /// Each object keeps its object colour. The Ginger One is built in (GingerOne.3dm, see
    /// Resources/GingerOne.README.md).
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

        public BoundingBox Box { get { var b = BoundingBox.Empty; foreach (var p in Parts) b.Union(p.Mesh.GetBoundingBox(false)); return b; } }

        /// <summary>The parts with the head at the point p: Z moves by (0, 0, dz), Y by (0, dy, dz), X by (dx, dy, dz).</summary>
        public List<Part> Pose(Point3d p)
        {
            Vector3d d = p - Tcp;
            var list = new List<Part>();
            foreach (var part in Parts)
            {
                Vector3d move = part.Group == "X" ? d : part.Group == "Y" ? new Vector3d(0, d.Y, d.Z) : part.Group == "Z" ? new Vector3d(0, 0, d.Z) : Vector3d.Zero;
                var m = part.Mesh;
                if (!move.IsZero) { m = m.DuplicateMesh(); m.Translate(move); }
                list.Add(new Part { Group = part.Group, Mesh = m, Color = part.Color });
            }
            return list;
        }

        public static PrinterModel FromFile(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "There is no file " + path + "."; return null; }
                using (var f = File3dm.Read(path))
                {
                    var m = From(f, out error);
                    if (m != null) m.Name = Path.GetFileNameWithoutExtension(path);
                    return m;
                }
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
            var c = new PrinterModel { Name = Name, Tcp = Tcp };
            foreach (var p in Parts) c.Parts.Add(new Part { Group = p.Group, Mesh = p.Mesh.DuplicateMesh(), Color = p.Color });
            return c;
        }

        private static PrinterModel From(File3dm f, out string error)
        {
            error = null;
            if (f == null) { error = "Not a Rhino file."; return null; }
            var names = new Dictionary<int, string>();
            var layerColors = new Dictionary<int, Color>();
            foreach (var l in f.AllLayers) { names[l.Index] = l.Name; layerColors[l.Index] = l.Color; }
            var model = new PrinterModel();
            bool tcp = false;
            foreach (var o in f.Objects)
            {
                if (!names.TryGetValue(o.Attributes.LayerIndex, out string layer)) layer = "Frame";
                string group = Group(layer);
                Color color = o.Attributes.ColorSource == Rhino.DocObjects.ObjectColorSource.ColorFromObject ? o.Attributes.ObjectColor
                    : layerColors.TryGetValue(o.Attributes.LayerIndex, out var lc) ? lc : Color.Gray;
                var g = o.Geometry;
                if (g is Rhino.Geometry.Point pt) { if (group == "TCP" && !tcp) { model.Tcp = pt.Location; tcp = true; } continue; }
                Mesh mesh = g as Mesh;
                if (mesh == null && (g is Brep || g is Extrusion || g is Surface || g is SubD)) mesh = SliceEngine.ToMesh(new object[] { g }, 0.5, null);
                if (mesh == null || mesh.Faces.Count == 0) continue;
                mesh = mesh.DuplicateMesh();
                if (mesh.Normals.Count != mesh.Vertices.Count) mesh.Normals.ComputeNormals();
                model.Parts.Add(new Part { Group = group, Mesh = mesh, Color = color });
            }
            if (model.Parts.Count == 0) { error = "The file has no meshes, surfaces or solids."; return null; }
            return model;
        }

        /// <summary>The moving group of a layer: X, Y, Z, TCP, or Frame for everything that stands still.</summary>
        public static string Group(string layer)
        {
            string l = (layer ?? "").Trim();
            int dot = l.LastIndexOf("::", StringComparison.Ordinal);
            if (dot >= 0) l = l.Substring(dot + 2);
            if (l.Equals("X", StringComparison.OrdinalIgnoreCase)) return "X";
            if (l.Equals("Y", StringComparison.OrdinalIgnoreCase)) return "Y";
            if (l.Equals("Z", StringComparison.OrdinalIgnoreCase)) return "Z";
            if (l.Equals("TCP", StringComparison.OrdinalIgnoreCase)) return "TCP";
            if (l.Equals("Bed", StringComparison.OrdinalIgnoreCase)) return "Bed";
            return "Frame";
        }
    }
}
