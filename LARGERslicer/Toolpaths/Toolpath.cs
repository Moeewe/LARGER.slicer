using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>What a toolpath point is printing. The role belongs to the move that arrives at the point.</summary>
    public enum PathRole { Wall = 0, Bottom = 1, Infill = 2, Support = 3, Seam = 4, Travel = 5, Skirt = 6 }

    /// <summary>
    /// Print parameters shared by all slicer components, so bead width, layer height and speed
    /// are defined in exactly one place.
    /// </summary>
    public class PrintSettings
    {
        public double BeadWidth = 5.0;        // mm (house standard: 3 mm nozzle, 5 mm bead, 2 mm layer)
        public double LayerHeight = 2.0;      // mm
        public double Overlap = 0.15;         // sideways overlap of neighbouring beads, 0..1
        public double Speed = 3000.0;         // mm/min, nominal print speed
        public double MinSpeed = 300.0;       // mm/min, lower limit when speed compensates height
        public double MaxSpeed = 12000.0;     // mm/min, upper limit and travel speed
        public double MinRadius = 0.0;        // mm, 0 = use bead width
        public double Flow = 1.0;             // global extrusion multiplier
        public double FilamentDiameter = 0.0; // mm, 0 = report extrusion as volume (mm3)
        public double Resolution = 2.0;       // mm, smallest point spacing of the path
        public bool Preview = false;          // fast mode: coarse meshing, no rounding
        public MachineProfile Machine = new MachineProfile();

        public double PointSpacing => Preview ? Resolution * 3.0 : Resolution;
        public double EffectiveMinRadius => MinRadius > 0 ? MinRadius : BeadWidth;
        /// <summary>Centre distance of two beads lying next to each other.</summary>
        public double BeadSpacing => BeadWidth * (1.0 - Math.Max(0.0, Math.Min(0.9, Overlap)));

        /// <summary>Cross section of a squashed bead (rectangle with round ends), mm2.</summary>
        public static double BeadArea(double width, double height)
        {
            if (width <= 0 || height <= 0) return 0;
            if (width >= height) return (width - height) * height + Math.PI * 0.25 * height * height;
            return Math.PI * 0.25 * width * height;
        }

        public PrintSettings Duplicate()
        {
            var d = (PrintSettings)MemberwiseClone();
            d.Machine = Machine?.Duplicate() ?? new MachineProfile();
            return d;
        }

        public override string ToString() =>
            $"Print Settings (bead {BeadWidth:0.##} x {LayerHeight:0.##} mm, F{Speed:0}{(Preview ? ", preview" : "")})";
    }

    /// <summary>
    /// How a printer turns the path into an extrusion value. Machines differ: one wants the
    /// volume, another the filament length, another a value that the controller multiplies by its
    /// own screw or RPM factor. The profile only describes that conversion, so any printer can be
    /// added with numbers instead of code.
    /// E = cross section x length x Flow x Factor (x First Layer Factor on the first layer),
    /// divided by the filament area when a filament diameter is set.
    /// </summary>
    public class MachineProfile
    {
        public string Name = "Generic pellet extruder";
        /// <summary>Multiplies every extrusion value (calibration of the machine).</summary>
        public double Factor = 1.0;
        /// <summary>Extra multiplier on the first layer (squash or spare it).</summary>
        public double FirstLayerFactor = 1.0;
        /// <summary>0 = E is a volume; &gt;0 = E is the length of filament of this diameter.</summary>
        public double FilamentDiameter = 0.0;
        /// <summary>true = cross section width x height (as in many hand-made scripts); false = squashed bead with round sides.</summary>
        public bool RectangleSection = false;
        /// <summary>Unit shown next to E, e.g. "mm3", "mm filament", "x P1".</summary>
        public string Unit = "mm3";

        public MachineProfile Duplicate() => (MachineProfile)MemberwiseClone();

        public double Section(double width, double height)
        {
            if (width <= 0 || height <= 0) return 0;
            return RectangleSection ? width * height : PrintSettings.BeadArea(width, height);
        }

        public override string ToString() => $"{Name} (E x {Factor:0.###}{(FirstLayerFactor != 1 ? $", first layer x {FirstLayerFactor:0.##}" : "")}, {Unit})";

        /// <summary>Starting points. Weber 1.128 (a Cura value) and Ginger 0.21 are the factors of the GCODE MAKER in the FH Muenster definition at 100 % flow, applied to width x height x length.</summary>
        public static readonly string[] PresetNames =
        {
            "Generic pellet extruder (volume)",
            "Filament printer 1.75 mm",
            "Filament printer 2.85 mm",
            "Weber DXR robot (XE x P1)",
            "Ginger (G-code)",
        };

        public static MachineProfile Preset(int i)
        {
            switch (i)
            {
                case 1: return new MachineProfile { Name = PresetNames[1], FilamentDiameter = 1.75, Unit = "mm filament" };
                case 2: return new MachineProfile { Name = PresetNames[2], FilamentDiameter = 2.85, Unit = "mm filament" };
                case 3: return new MachineProfile { Name = PresetNames[3], Factor = 1.128, RectangleSection = true, Unit = "x P1" };
                case 4: return new MachineProfile { Name = PresetNames[4], Factor = 0.21, RectangleSection = true, Unit = "mm (Ginger E)" };
                default: return new MachineProfile();
            }
        }
    }

    /// <summary>One slice: the contours of the model on one layer, already lifted to nozzle height.</summary>
    public class SliceLayer
    {
        public int Index;
        public Plane Plane;      // plane at nozzle height (top of the layer); normal = tool axis
        public double Height;    // layer height in mm
        public List<Polyline> Contours = new List<Polyline>();
        /// <summary>Wall number of each contour (0 = outermost). Null until Walls has run.</summary>
        public List<int> Rings;
        /// <summary>The plain walls before infill was spliced in; used to tell wall from infill.</summary>
        public List<Polyline> Reference;
        /// <summary>Role of each contour (null = all walls). Bottom fill and similar set this.</summary>
        public List<PathRole> ContourRoles;
        /// <summary>Ends of the fill paths. The wall starts next to one of them so the path can turn from the wall into the fill.</summary>
        public List<Point3d> EntryHints;
        /// <summary>Non-planar / multi-axis mapping. The contours are in flat space; null = ordinary planar slicing.</summary>
        public Warp Warp;

        /// <summary>The contours as they will be printed (mapped back when the layer is non-planar).</summary>
        public IEnumerable<Polyline> RealContours()
        {
            if (Warp == null) return Contours;
            if (_real == null || _realCount != Contours.Count)
            {
                var list = new List<Polyline>(Contours.Count);
                foreach (var c in Contours) list.Add(Warp.ToReal(c, 4.0));
                _real = list; _realCount = Contours.Count;
            }
            return _real;
        }
        private List<Polyline> _real;
        private int _realCount;

        public override string ToString() => $"Layer {Index} ({Contours.Count} contour{(Contours.Count == 1 ? "" : "s")})";
    }

    /// <summary>
    /// A print path. Every point carries what the machine needs: tool axis, local layer height,
    /// bead width, flow factor and role. Height/flow/role describe the move that ends at the point.
    /// </summary>
    public class Toolpath
    {
        public List<Point3d> Points = new List<Point3d>();
        public List<Vector3d> Normals = new List<Vector3d>();
        public List<double> Heights = new List<double>();
        public List<double> Widths = new List<double>();
        public List<double> Flows = new List<double>();
        public List<PathRole> Roles = new List<PathRole>();
        public List<int> Layers = new List<int>();
        public PrintSettings Settings = new PrintSettings();
        /// <summary>Set when the path was mapped back from a non-planar / multi-axis slicing. The points are in real space.</summary>
        public Warp Warp;

        public int Count => Points.Count;

        public void Add(Point3d p, Vector3d normal, double height, double width, double flow, PathRole role, int layer)
        {
            Points.Add(p); Normals.Add(normal); Heights.Add(height); Widths.Add(width);
            Flows.Add(flow); Roles.Add(role); Layers.Add(layer);
        }

        public void AddFrom(Toolpath o, int i)
        {
            Add(o.Points[i], o.Normals[i], o.Heights[i], o.Widths[i], o.Flows[i], o.Roles[i], o.Layers[i]);
        }

        public Toolpath Duplicate()
        {
            var t = new Toolpath { Settings = Settings, Warp = Warp };
            t.Points.AddRange(Points); t.Normals.AddRange(Normals); t.Heights.AddRange(Heights);
            t.Widths.AddRange(Widths); t.Flows.AddRange(Flows); t.Roles.AddRange(Roles); t.Layers.AddRange(Layers);
            return t;
        }

        public Polyline ToPolyline() => new Polyline(Points);

        public double Length()
        {
            double l = 0;
            for (int i = 1; i < Points.Count; i++) l += Points[i].DistanceTo(Points[i - 1]);
            return l;
        }

        public override string ToString()
        {
            int layers = Layers.Count > 0 ? Layers[Layers.Count - 1] + 1 : 0;
            return $"Toolpath ({Count} points, {layers} layers, {Length() / 1000.0:0.00} m)";
        }
    }
}
