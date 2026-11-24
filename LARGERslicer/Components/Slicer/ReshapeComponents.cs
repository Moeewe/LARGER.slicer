using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>A Shape Field on a wire.</summary>
    public class GH_ShapeField : GH_Goo<ShapeField>
    {
        public GH_ShapeField() { }
        public GH_ShapeField(ShapeField v) { Value = v; }
        public override bool IsValid => Value != null;
        public override string TypeName => "Shape Field";
        public override string TypeDescription => "How high a part has to become, over its plan";
        public override IGH_Goo Duplicate() => new GH_ShapeField(Value);
        public override string ToString() => Value?.ToString() ?? "Null Shape Field";
    }

    internal static class ReshapeIO
    {
        public static string PathOf(string file)
        {
            string p = (file ?? "").Trim().Trim('"');
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            return p;
        }

        /// <summary>Adds lines to a log file; a new file gets the header first.</summary>
        public static string Append(string file, string defaultName, string header, IEnumerable<string> lines)
        {
            string path = OutputFile.Resolve(file, defaultName);
            var l = new List<string>(); if (!File.Exists(path)) l.Add(header); l.AddRange(lines);
            File.AppendAllLines(path, l); return path;
        }

        public static Mesh Surface(Func<double, double, double> z, double x0, double y0, double x1, double y1, int n = 50)
        {
            double size = Math.Max(1e-6, Math.Max(x1 - x0, y1 - y0)), cell = size / n;
            int nx = Math.Max(2, (int)Math.Ceiling((x1 - x0) / cell) + 1), ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / cell) + 1);
            double dx = (x1 - x0) / (nx - 1), dy = (y1 - y0) / (ny - 1);
            if (dx <= 0) dx = cell; if (dy <= 0) dy = cell;
            var m = new Mesh();
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { double x = x0 + i * dx, y = y0 + j * dy; m.Vertices.Add(x, y, z(x, y)); }
            for (int j = 0; j + 1 < ny; j++) for (int i = 0; i + 1 < nx; i++) { int a = j * nx + i; m.Faces.AddFace(a, a + 1, a + nx + 1, a + nx); }
            m.Normals.ComputeNormals(); m.Compact();
            return m;
        }

        public static bool Head(GH_Component c, IGH_DataAccess da, int first, NonPlanarCheck.Head head)
        {
            da.GetData(first, ref head.TipRadius); da.GetData(first + 1, ref head.HalfAngle); da.GetData(first + 2, ref head.ConeLength); da.GetData(first + 3, ref head.Radius);
            if (head.TipRadius < 0 || head.HalfAngle < 0 || head.HalfAngle >= 90 || head.ConeLength <= 0 || head.Radius <= 0)
            {
                c.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tip Radius must be 0 or more, Cone Angle between 0 and 90 degrees, Cone Length and Head Radius more than 0.");
                return false;
            }
            return true;
        }

        public static void HeadInputs(Action<string, string, string, double> add)
        {
            add("Tip Radius", "r0", "Radius of the nozzle tip (mm). A flat 8 mm tip is 4; with a vertical nozzle it sets how steep the layers may be", 4.0);
            add("Cone Angle", "Ca", "Half angle of the nozzle cone, from its axis (degrees)", 30.0);
            add("Cone Length", "Lc", "Height of the cone from the tip up to the head (mm)", 60.0);
            add("Head Radius", "Hr", "Radius of the head above the cone (mm)", 40.0);
        }

        public static void Verdict(AsyncJob job, string first)
        {
            if (first == null) return;
            if (first.StartsWith("REFUSED")) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, first);
            else if (first.StartsWith("CHECK")) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, first);
        }
    }

    // ============================================================== Shape Field
    /// <summary>Where a part needs more print height and where less.</summary>
    public class ShapeFieldComponent : SlicerComponent
    {
        public ShapeFieldComponent()
            : base("Shape Field", "Shape",
                  "How high a part has to become, over its plan (x, y): from the utilisation of a structural analysis (stress / allowed stress at points), from points with wanted heights or factors, from a profile along a line, from a guide surface, or from the file of an earlier step. Goes into Reshape Path, Reshape Code and Compare Variants.") { }

        public override Guid ComponentGuid => new Guid("4586B16B-8C6C-4027-845B-A8084DB491FB");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerShapeFieldIcon.png", this);

        internal static readonly string[] Modes = { "Factors at points", "Heights at points (mm)", "Utilisation at points", "Factors along a line", "Heights along a line (mm)", "Guide surface (heights)", "File of an earlier step" };

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Mode", "M", "What the values are:\n0 = Factors at points: 1.5 = one and a half times the print height\n1 = Heights at points (mm above the bottom of the part)\n2 = Utilisation at points: stress / allowed stress (or any number with a target); above the target the part grows, below it shrinks\n3 = Factors along a line\n4 = Heights along a line (mm)\n5 = Guide surface: its height above the bottom of the part\n6 = File written by this component in an earlier step", GH_ParamAccess.item, 2);
            p.AddPointParameter("Points", "P", "Modes 0 to 2: the places of the values. Only x and y count; several points above one another (a wall that was analysed over its height) are joined, the largest utilisation counts", GH_ParamAccess.list);
            p.AddNumberParameter("Values", "V", "Modes 0 to 4: one value per point, or per position along the line", GH_ParamAccess.list);
            p.AddLineParameter("Line", "L", "Modes 3 and 4: the line the values run along (seen from above)", GH_ParamAccess.item);
            p.AddNumberParameter("Positions", "t", "Modes 3 and 4: positions along the line, 0 = start, 1 = end. Empty = evenly spaced", GH_ParamAccess.list);
            p.AddGeometryParameter("Guide", "G", "Mode 5: surface(s), Breps or meshes the new top has to follow", GH_ParamAccess.list);
            p.AddGenericParameter("Previous", "Pr", "Mode 2: what was made in the step before: the file ..._field.csv that Reshape Path wrote with Write Log (or a Shape Field). Empty = the part as it was. Mode 6: the file", GH_ParamAccess.item);
            p.AddNumberParameter("Height", "H", "Print height of the part as it was sliced (mm). For the preview, and for a Previous field in mm", GH_ParamAccess.item, 100.0);
            p.AddNumberParameter("Target", "Ta", "Mode 2: the value that is just right (1 for stress / allowed stress)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Exponent", "Ex", "Mode 2: new height = old height x (value / target) ^ exponent. 0.5 = bending about the axis across the height (stress goes with 1 / height squared), 1 = tension, compression or shear (stress goes with 1 / height, the plain linear rule), 0 = no change. Give several to compare them in Compare Variants", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Damping", "Da", "Mode 2: 1 = the full step, 0.5 = half of it. Below 1 the loop does not swing", GH_ParamAccess.item, 0.7);
            p.AddNumberParameter("Min Factor", "Mi", "Smallest factor", GH_ParamAccess.item, 0.4);
            p.AddNumberParameter("Max Factor", "Ma", "Largest factor", GH_ParamAccess.item, 2.5);
            p.AddNumberParameter("Smooth", "Sm", "Points only: 0 = the field goes through every value, more = it passes beside noisy values (about 0.1 to 10). Reshape Path rounds the shape anyway", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("File", "F", "Where the field is saved (CSV), to go on with it in the next step. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "S", "Saves the field", GH_ParamAccess.item, false);
            foreach (int i in new[] { 1, 2, 3, 4, 5, 6, 14 }) p[i].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 0, "Mode", Modes);
            AddSlider(doc, 9, "Exponent", 0m, 1m, 0.5m, 2, "", new double[] { 0, 0.33, 0.5, 0.75, 1 });
            AddSlider(doc, 10, "Damping", 0.1m, 1m, 0.7m, 2, "", new double[] { 0.3, 0.5, 0.7, 1 });
            AddSlider(doc, 11, "Min Factor", 0.1m, 1m, 0.4m, 2, "x");
            AddSlider(doc, 12, "Max Factor", 1m, 5m, 2.5m, 2, "x");
            AddButton(doc, 15, "Save field");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Field", "F", "The shape field, for Reshape Path, Reshape Code and Compare Variants", GH_ParamAccess.item);
            p.AddMeshParameter("Preview", "Pv", "The wanted top, as a surface (before the limits of the printer; Reshape Path shows the real one). Slicing anew: use the Top of Reshape Path as guide of Slice", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What the field is; the first line says it all", GH_ParamAccess.list);
            p.AddTextParameter("Table", "T", "The field as CSV (x, y, value)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int mode = 2; da.GetData(0, ref mode);
            var pts = new List<Point3d>(); da.GetDataList(1, pts); var vals = new List<double>(); da.GetDataList(2, vals);
            Line line = Line.Unset; da.GetData(3, ref line); var ts = new List<double>(); da.GetDataList(4, ts);
            var guide = new List<IGH_GeometricGoo>(); da.GetDataList(5, guide);
            IGH_Goo prevGoo = null; da.GetData(6, ref prevGoo);
            double height = 100, target = 1, ex = 0.5, damp = 0.7, min = 0.4, max = 2.5, smooth = 0; string file = null; bool save = false;
            da.GetData(7, ref height); da.GetData(8, ref target); da.GetData(9, ref ex); da.GetData(10, ref damp); da.GetData(11, ref min); da.GetData(12, ref max); da.GetData(13, ref smooth); da.GetData(14, ref file); da.GetData(15, ref save);
            ShapeField f;
            try
            {
                ShapeField prev = null;
                if (prevGoo is GH_Goo<ShapeField> pf) prev = pf.Value;
                else if (prevGoo != null)
                {
                    string path = ReshapeIO.PathOf(prevGoo.ToString());
                    if (path.Length > 0) { if (!File.Exists(path)) throw new ArgumentException("Previous: the file " + path + " does not exist."); prev = ShapeField.FromCsv(File.ReadAllLines(path)); }
                }
                switch (mode)
                {
                    case 0: case 1:
                        if (pts.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect Points and Values."); return; }
                        f = ShapeField.FromPoints(pts, vals, mode == 1, smooth); break;
                    case 2:
                        if (pts.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect the Points and the Values (utilisation) of the analysis."); return; }
                        f = ShapeField.FromUtilisation(pts, vals, new ShapeField.UtilisationOptions { Target = target, Exponent = ex, Damping = damp, Min = min, Max = max, Smooth = smooth, Previous = prev, Height = height }); break;
                    case 3: case 4:
                        if (!line.IsValid || vals.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Line and at least two Values."); return; }
                        if (ts.Count == 0) ts = Enumerable.Range(0, vals.Count).Select(i => i / (double)(vals.Count - 1)).ToList();
                        double len = Math.Sqrt(line.Direction.X * line.Direction.X + line.Direction.Y * line.Direction.Y);
                        f = ShapeField.FromProfile(line.From, line.Direction, ts.Select(t => t * len).ToList(), vals, mode == 4); break;
                    case 5:
                        if (guide.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect the Guide surface."); return; }
                        f = FromGuide(guide); break;
                    default:
                        if (prev == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect the file of the earlier step to Previous."); return; }
                        f = prev; break;
                }
            }
            catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); return; }
            if (mode != 2 && mode != 5 && !f.IsHeight) { f.Lowest = min; f.Highest = max; }
            var rep = new List<string>();
            double lo = double.MaxValue, hi = 0;
            for (int i = 0; i <= 40; i++) for (int j = 0; j <= 40; j++) { double v = f.Value(f.X0 + (f.X1 - f.X0) * i / 40, f.Y0 + (f.Y1 - f.Y0) * j / 40); if (v < lo) lo = v; if (v > hi) hi = v; }
            string unit = f.IsHeight ? " mm" : "";
            rep.Add(Inv.F($"OK: {(f.IsHeight ? "height" : "factor")} {lo:0.##} to {hi:0.##}{unit} ({hi / Math.Max(1e-9, lo):0.0} : 1), from {f.Source}."));
            rep.AddRange(f.Notes);
            Message = Inv.F($"{lo:0.##} - {hi:0.##}{unit}");
            if (f.MaxUtilisation > 0 && f.Over > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Inv.F($"Utilisation up to {f.MaxUtilisation:0.##}; {100 * f.Over:0} % of the points are above the target."));
            var csv = f.ToCsv();
            if (save) { try { rep.Add("Saved " + Save(file, "ShapeField_" + DateTime.Now.ToString("yyMMddHHmm", Inv.C) + ".csv", csv)); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, rep[rep.Count - 1]); } catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + e.Message); } }
            da.SetData(0, new GH_ShapeField(f));
            if (f.X1 - f.X0 > 1e-6 || f.Y1 - f.Y0 > 1e-6)
            {
                double pad = 0.05 * Math.Max(f.X1 - f.X0, f.Y1 - f.Y0), px = f.X1 - f.X0 < pad ? pad : 0, py = f.Y1 - f.Y0 < pad ? pad : 0;
                da.SetData(1, ReshapeIO.Surface((x, y) => f.IsHeight ? f.Value(x, y) : f.Value(x, y) * height, f.X0 - px, f.Y0 - py, f.X1 + px, f.Y1 + py));
            }
            da.SetDataList(2, rep); da.SetDataList(3, csv);
        }

        /// <summary>The highest point of the guide over every place of a grid.</summary>
        private static ShapeField FromGuide(List<IGH_GeometricGoo> guide)
        {
            var log = new List<string>();
            Mesh m = SliceEngine.ToMesh(guide.Where(g => g != null).Select(g => LARGERslicer.Components.AsyncSolve.Copy(g)), 0.1, log);
            if (m == null || m.Vertices.Count == 0) throw new ArgumentException("The guide could not be meshed. " + string.Join(" ", log));
            var bb = m.GetBoundingBox(true); double size = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y), cell = size / 80;
            int nx = Math.Max(2, (int)Math.Ceiling((bb.Max.X - bb.Min.X) / cell) + 1), ny = Math.Max(2, (int)Math.Ceiling((bb.Max.Y - bb.Min.Y) / cell) + 1);
            var v = new double[nx * ny]; double top = bb.Max.Z + 10;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double x = Math.Min(bb.Max.X - 1e-4 * size, Math.Max(bb.Min.X + 1e-4 * size, bb.Min.X + i * cell)), y = Math.Min(bb.Max.Y - 1e-4 * size, Math.Max(bb.Min.Y + 1e-4 * size, bb.Min.Y + j * cell));
                    double t = Intersection.MeshRay(m, new Ray3d(new Point3d(x, y, top), -Vector3d.ZAxis));
                    v[j * nx + i] = t >= 0 ? top - t : double.NaN;
                }
            if (v.Any(z => !double.IsNaN(z) && z <= 0)) throw new ArgumentException("The guide surface reaches down to the bed (Z 0) or below it. It has to stand above the part everywhere.");
            return ShapeField.FromGrid(bb.Min.X, bb.Min.Y, cell, nx, ny, v, true, "guide surface");
        }
    }

    // ============================================================== Reshape Path
    /// <summary>Gives a finished toolpath another shape, in one of several ways.</summary>
    public class ReshapePathComponent : SlicerComponent
    {
        public ReshapePathComponent()
            : base("Reshape Path", "Reshape",
                  "Gives a finished toolpath another shape: more print height where the Shape Field asks for it, less elsewhere, with the same number of layers (they fan out). The plan of the path stays: seam, infill and the one continuous line are kept. Skirt, way in and flat layers are not touched, the way out starts at the new end. The extrusion is worked out again from the new distance to the lap below. Several variants (fanned laps with a square or a vertical tool, wider beads, extra passes, cut laps) for printing and testing against each other. Put it after Start and End, before the output to the machine.") { }

        public override Guid ComponentGuid => new Guid("4374A994-B7CE-45DC-915D-77BFCE8828CD");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerReshapeIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path (after Start and End)", GH_ParamAccess.item);
            p.AddGenericParameter("Field", "F", "Shape Field", GH_ParamAccess.item);
            p.AddIntegerParameter("Variant", "V", "Which way:\n0 = Fanned laps, tool square to the layers (robot)\n1 = Fanned laps, nozzle vertical (gantry)\n2 = Fanned and wider beads, tool square: half of the factor (its root) goes into the height, the rest into the bead width\n3 = the same, nozzle vertical\n4 = Wider beads only: layers stay flat\n5 = Fanned with extra passes, tool square: layers that get too thick are printed in 3 or 5 passes (single walls only)\n6 = the same, nozzle vertical\n7 = Flat layers of constant height, the laps are cut where the part is lower and turn back", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Material", "Mt", "Material of the part in % of before: 100 = the same amount, only moved to where it is needed; 80 = a fifth less; 0 = the heights exactly as the field says", GH_ParamAccess.item, 100.0);
            p.AddNumberParameter("Min Layer", "Hi", "Smallest layer height (mm). 0 = automatic (0.8 mm; less for small beads)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Layer", "Ha", "Largest layer height (mm). 0 = automatic (half the bead width, or the layer height of the print)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Slope", "Sl", "Steepest slope of a layer (degrees). 0 = automatic: 30 with a tilted tool; with a vertical nozzle what its flat tip allows at a corner", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Smooth", "Sm", "The new top is rounded over about this length (mm). 0 = 6 bead widths", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Flat Layers", "Fl", "Layers at the bottom that stay flat. -1 = the first layer and the solid bottom", GH_ParamAccess.item, -1);
            p.AddIntegerParameter("Roof", "Ro", "A wall that closes to a roof:\n0 = the layers of the roof keep their thickness and move along with the wall\n1 = the roof is stretched too\n2 = refuse such parts", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Max Flow", "Mf", "Moves that need more than this (mm3/s) run slower. 0 = off, -1 = never more than the path needed before", GH_ParamAccess.item, 0.0);
            ReshapeIO.HeadInputs((n, k, d, v) => p.AddNumberParameter(n, k, d, GH_ParamAccess.item, v));
            p.AddMeshParameter("Mesh", "Me", "Optional meshes to bend the same way (the part, for the next analysis)", GH_ParamAccess.list);
            p.AddTextParameter("Name", "N", "Name of the part, for the log", GH_ParamAccess.item, "Part");
            p.AddIntegerParameter("Step", "St", "Number of the step in the loop, for the log", GH_ParamAccess.item, 0);
            p.AddTextParameter("Log File", "Lg", "CSV file the result is added to (one line per save); the report (.md), the layers and the field as made (.csv) go beside it. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Write Log", "W", "Adds this result to the log", GH_ParamAccess.item, false);
            p[15].Optional = true; p[18].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Variant", ReshapeVariants.Names);
            AddSlider(doc, 3, "Material (0 = as the field)", 0m, 150m, 100m, 0, "%", new double[] { 0, 60, 70, 80, 90, 100 });
            AddValueList(doc, 9, "Roof", "Roof layers keep their thickness", "Stretch the roof too", "Refuse parts with a roof");
            AddButton(doc, 19, "Write log");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The reshaped path, with tool axes", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was done and what the check found; the first line is the verdict", GH_ParamAccess.list);
            p.AddNumberParameter("Layer Height", "H", "Bead height of every point (mm)", GH_ParamAccess.list);
            p.AddNumberParameter("Tilt", "Ti", "Tilt of the tool at every point (degrees)", GH_ParamAccess.list);
            p.AddPointParameter("Problems", "X", "Places where the check found something (head, height, turning). Should be empty", GH_ParamAccess.list);
            p.AddMeshParameter("Top", "Tp", "The new top of the part as a surface. Slicing anew: bend the part (Mesh) and use Top as the guide of Slice (flat bottom to guide)", GH_ParamAccess.item);
            p.AddMeshParameter("Mesh", "M", "The meshes, bent", GH_ParamAccess.list);
            p.AddTextParameter("Layers", "L", "One line per layer (CSV): length, heights, tilt, E, time", GH_ParamAccess.list);
            p.AddTextParameter("Log", "Lo", "The line for the experiment log (CSV, with header)", GH_ParamAccess.list);
            p.AddGenericParameter("Made", "Fm", "What was really made, as a Shape Field of factors (after limits, rounding and material). Write Log saves it as a file: that file is the Previous of the Shape Field in the next step of the loop", GH_ParamAccess.item);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0); var f = SlicerIO.Get<ShapeField>(da, 1);
            if (tp == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Toolpath."); return; }
            if (f == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Shape Field."); return; }
            var o = new ReshapeOptions(); int v = 0, roof = 0, step = 0; string name = "Part", log = null; bool write = false;
            da.GetData(2, ref v); da.GetData(3, ref o.Material); da.GetData(4, ref o.MinLayer); da.GetData(5, ref o.MaxLayer); da.GetData(6, ref o.MaxSlope); da.GetData(7, ref o.Smooth); da.GetData(8, ref o.FlatLayers); da.GetData(9, ref roof); da.GetData(10, ref o.MaxFlow);
            if (!ReshapeIO.Head(this, da, 11, o.Head)) return;
            var meshes = new List<Mesh>(); da.GetDataList(15, meshes); da.GetData(16, ref name); da.GetData(17, ref step); da.GetData(18, ref log); da.GetData(19, ref write);
            o.Roof = (RoofMode)Math.Max(0, Math.Min(2, roof));
            var variant = (ReshapeVariant)Math.Max(0, Math.Min(ReshapeVariants.Names.Length - 1, v));
            var copies = meshes.Where(m => m != null).Select(m => m.DuplicateMesh()).ToList();
            Async.Run(da, job =>
            {
                var r = ReshapeVariants.Run(tp, f, variant, o);
                ReshapeIO.Verdict(job, r.Report.FirstOrDefault());
                foreach (var w in r.Warnings) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
                string line = PathReshape.LogLine(name, step, f, r, o, DateTime.Now);
                if (write)
                {
                    try
                    {
                        string p = ReshapeIO.Append(log, "Reshape_Log.csv", PathReshape.LogHeader, new[] { line });
                        string stem = Path.Combine(Path.GetDirectoryName(p) ?? "", Path.GetFileNameWithoutExtension(p) + "_" + OutputFile.Clean(name) + "_step" + step.ToString(Inv.C) + "_v" + (int)variant);
                        File.WriteAllText(stem + ".md", PathReshape.Markdown(name, step, f, r, DateTime.Now));
                        if (r.Path != null) { File.WriteAllLines(stem + "_field.csv", r.Realised(tp).ToCsv()); File.WriteAllLines(stem + "_layers.csv", PathReshape.LayerCsv(r.Path)); }
                        job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Added to " + p);
                    }
                    catch (Exception e) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the log: " + e.Message); }
                }
                job.SetDataList(1, r.Report);
                job.SetDataList(8, new[] { PathReshape.LogHeader, line });
                if (r.Path == null) { job.Message = "refused"; return; }
                job.Message = Inv.F($"{r.HeightAfterMin:0} - {r.HeightAfterMax:0} mm, {100 * r.MaterialShare:0} %");
                job.SetData(0, new GH_Toolpath(r.Path));
                job.SetDataList(2, r.Path.Heights);
                job.SetDataList(3, r.Path.Normals.Select(n => Math.Acos(Math.Max(-1, Math.Min(1, n.Z))) * 180 / Math.PI));
                if (r.Check != null) job.SetDataList(4, r.Check.Collisions.Concat(r.Check.BadHeights).Concat(r.Check.FastTurns));
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                for (int i = 1; i < tp.Count; i++) if (LapBelow.Prints(tp, i) && tp.Roles[i] != PathRole.Skirt) { var q = tp.Points[i]; x0 = Math.Min(x0, q.X); x1 = Math.Max(x1, q.X); y0 = Math.Min(y0, q.Y); y1 = Math.Max(y1, q.Y); }
                double pad = tp.Settings.BeadWidth;
                if (x1 > x0) job.SetData(5, ReshapeIO.Surface((x, y) => variant == ReshapeVariant.CutLaps ? Math.Min(r.Map.ZTop, f.Factor(x, y, r.HeightBefore) * r.Scale * r.HeightBefore + r.Map.ZBase) : r.Map.Z(x, y, r.Map.ZTop), x0 - pad, y0 - pad, x1 + pad, y1 + pad));
                foreach (var m in copies)
                {
                    for (int i = 0; i < m.Vertices.Count; i++) { Point3d q = m.Vertices[i]; m.Vertices.SetVertex(i, q.X, q.Y, r.Map.Z(q.X, q.Y, q.Z)); }
                    m.Normals.ComputeNormals();
                }
                job.SetDataList(6, copies);
                job.SetDataList(7, PathReshape.LayerCsv(r.Path));
                job.SetData(9, new GH_ShapeField(r.Realised(tp)));
            });
        }
    }

    // ============================================================== Compare Variants
    /// <summary>All variants (and several fields) side by side.</summary>
    public class CompareVariantsComponent : SlicerComponent
    {
        public CompareVariantsComponent()
            : base("Compare Variants", "Compare",
                  "Reshapes one toolpath in several ways and with several Shape Fields (for example linear and stress based, or several exponents) and puts the results into one table: material, heights, layer heights, bead widths, tilt and turning of the tool, slope, flow, time, travel moves, head clearance. Each result comes out as a toolpath, so it can be printed and tested against the others.") { }

        public override Guid ComponentGuid => new Guid("F65F6B58-4571-4164-A05D-8333A874CB07");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerCompareIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path (after Start and End)", GH_ParamAccess.item);
            p.AddGenericParameter("Fields", "F", "One or more Shape Fields", GH_ParamAccess.list);
            p.AddIntegerParameter("Variants", "V", "Numbers of the variants (see Reshape Path, 0 to 7). Empty = all", GH_ParamAccess.list);
            p.AddNumberParameter("Material", "Mt", "Material in % of before, the same for all (0 = as the field says)", GH_ParamAccess.item, 100.0);
            p.AddNumberParameter("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Slope", "Sl", "Steepest slope (degrees), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Smooth", "Sm", "Rounding (mm), 0 = 6 bead widths", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Flow", "Mf", "mm3/s; 0 = off, -1 = never more than before", GH_ParamAccess.item, 0.0);
            ReshapeIO.HeadInputs((n, k, d, v) => p.AddNumberParameter(n, k, d, GH_ParamAccess.item, v));
            p.AddTextParameter("Name", "N", "Name of the part, for the log", GH_ParamAccess.item, "Part");
            p.AddTextParameter("Log File", "Lg", "CSV file the results are added to; the table goes beside it as .md. Empty = the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Write Log", "W", "Adds all results to the log", GH_ParamAccess.item, false);
            p[2].Optional = true; p[14].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 3, "Material (0 = as the field)", 0m, 150m, 100m, 0, "%", new double[] { 0, 60, 70, 80, 90, 100 });
            AddButton(doc, 15, "Write log");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Table", "Tb", "The comparison (Markdown table)", GH_ParamAccess.list);
            p.AddGenericParameter("Toolpaths", "T", "The path of every row (empty where refused)", GH_ParamAccess.list);
            p.AddTextParameter("Verdicts", "R", "First line of every report", GH_ParamAccess.list);
            p.AddTextParameter("Log", "Lo", "The rows for the experiment log (CSV, with header)", GH_ParamAccess.list);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0); var goos = new List<IGH_Goo>(); da.GetDataList(1, goos);
            var fields = goos.OfType<GH_Goo<ShapeField>>().Select(g => g.Value).Where(x => x != null).ToList();
            if (tp == null || fields.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Toolpath and at least one Shape Field."); return; }
            var vs = new List<int>(); da.GetDataList(2, vs);
            var variants = (vs.Count > 0 ? vs : Enumerable.Range(0, ReshapeVariants.Names.Length)).Where(i => i >= 0 && i < ReshapeVariants.Names.Length).Select(i => (ReshapeVariant)i).ToList();
            var o = new ReshapeOptions(); string name = "Part", log = null; bool write = false;
            da.GetData(3, ref o.Material); da.GetData(4, ref o.MinLayer); da.GetData(5, ref o.MaxLayer); da.GetData(6, ref o.MaxSlope); da.GetData(7, ref o.Smooth); da.GetData(8, ref o.MaxFlow);
            if (!ReshapeIO.Head(this, da, 9, o.Head)) return;
            da.GetData(13, ref name); da.GetData(14, ref log); da.GetData(15, ref write);
            Async.Run(da, job =>
            {
                var rows = ReshapeVariants.Compare(tp, fields, variants, o);
                var table = ReshapeVariants.Table(rows); var now = DateTime.Now;
                var lines = rows.Select((q, i) => PathReshape.LogLine(name, i, q.Field, q.Result, o, now)).ToList();
                if (write)
                {
                    try { string p = ReshapeIO.Append(log, "Reshape_Log.csv", PathReshape.LogHeader, lines); File.AppendAllLines(Path.ChangeExtension(p, ".md"), new[] { "## " + name + ", " + now.ToString("yyyy-MM-dd HH:mm", Inv.C), "" }.Concat(table).Concat(new[] { "" })); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Added to " + p); }
                    catch (Exception e) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the log: " + e.Message); }
                }
                int ok = rows.Count(q => q.Result.Path != null && (q.Result.Check == null || q.Result.Check.Ok));
                job.Message = Inv.F($"{ok} of {rows.Count} OK");
                job.SetDataList(0, table);
                job.SetDataList(1, rows.Select(q => q.Result.Path != null ? new GH_Toolpath(q.Result.Path) : null));
                job.SetDataList(2, rows.Select(q => ReshapeVariants.Short(q.Variant) + ": " + q.Result.Report.FirstOrDefault()));
                job.SetDataList(3, new[] { PathReshape.LogHeader }.Concat(lines));
            });
        }
    }

    // ============================================================== Reshape Code
    /// <summary>Reshapes a finished G-code or .dxr file.</summary>
    public class ReshapeCodeComponent : SlicerComponent
    {
        public ReshapeCodeComponent()
            : base("Reshape Code", "Reshape Code",
                  "Reshapes a finished file: G-code (Marlin, Cura, PrusaSlicer, our own) or a Weber .dxr. Only the numbers that change are written into their lines (Z, E, tool angles A B C for the robot, F where the flow is limited); start code, end code, skirt, comments and every other line stay byte for byte. The extrusion is worked out again from the new distance to the lap below and keeps the calibration of the file. Files that are not understood are refused.") { }

        public override Guid ComponentGuid => new Guid("A6160EFF-EFCC-421A-AFD8-CD4E7DC4C322");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerReshapeCodeIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("File", "File", "The G-code or .dxr file", GH_ParamAccess.item);
            p.AddTextParameter("Code", "C", "Instead of a file: the lines of the program", GH_ParamAccess.list);
            p.AddGenericParameter("Field", "F", "Shape Field", GH_ParamAccess.item);
            p.AddIntegerParameter("Variant", "V", "Which way (see Reshape Path, 0 to 6). A tilted tool is written for .dxr files only; laps that are cut (7) are not possible in a finished file", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Material", "Mt", "Material of the part in % of before; 0 = as the field says", GH_ParamAccess.item, 100.0);
            p.AddNumberParameter("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Slope", "Sl", "Steepest slope (degrees), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Smooth", "Sm", "Rounding (mm), 0 = 6 bead widths", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Flat Layers", "Fl", "Layers at the bottom that stay flat. -1 = the first layer (and the solid bottom, if the file marks it)", GH_ParamAccess.item, -1);
            p.AddIntegerParameter("Roof", "Ro", "A wall that closes to a roof: 0 = its layers keep their thickness, 1 = stretch, 2 = refuse", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Flow Limit", "Fw", "No move puts out more per second than the largest of the file times this (1 = not more than before; thick places run slower). 0 = off, F stays as it is", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Bead Width", "W", "Bead width (mm). 0 = from the file", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Layer Height", "H", "Layer height (mm). 0 = from the Z steps", GH_ParamAccess.item, 0.0);
            p.AddGenericParameter("Machine Profile", "MP", "Optional. What E means in the file (a Machine Profile): for the bead width and the cross section", GH_ParamAccess.item);
            p.AddTextParameter("Output", "O", "File to write. Empty = beside the old one, with _reshaped in its name", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "S", "Writes the file", GH_ParamAccess.item, false);
            foreach (int i in new[] { 0, 1, 14, 15 }) p[i].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 3, "Variant", ReshapeVariants.Names.Take(7).ToArray());
            AddSlider(doc, 4, "Material (0 = as the field)", 0m, 150m, 100m, 0, "%", new double[] { 0, 60, 70, 80, 90, 100 });
            AddValueList(doc, 10, "Roof", "Roof layers keep their thickness", "Stretch the roof too", "Refuse parts with a roof");
            AddSlider(doc, 11, "Flow Limit (0 = off)", 0m, 2m, 0m, 2, "x", new double[] { 0, 1, 1.2 });
            AddButton(doc, 16, "Save file");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Code", "C", "The lines of the new program", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "What was done; the first line is the verdict", GH_ParamAccess.list);
            p.AddGenericParameter("Before", "B", "The file as a toolpath", GH_ParamAccess.item);
            p.AddGenericParameter("After", "A", "The new file as a toolpath", GH_ParamAccess.item);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            string file = null, outFile = null; var code = new List<string>(); da.GetData(0, ref file); da.GetDataList(1, code);
            var f = SlicerIO.Get<ShapeField>(da, 2);
            if (string.IsNullOrWhiteSpace(file) && code.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a File or the lines of a program."); return; }
            if (f == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Shape Field."); return; }
            var o = new ReshapeCode.Options(); var s = o.Shape; int v = 0, roof = 0; bool save = false;
            da.GetData(3, ref v); da.GetData(4, ref s.Material); da.GetData(5, ref s.MinLayer); da.GetData(6, ref s.MaxLayer); da.GetData(7, ref s.MaxSlope); da.GetData(8, ref s.Smooth); da.GetData(9, ref s.FlatLayers); da.GetData(10, ref roof);
            da.GetData(11, ref o.MaxFlowFactor); da.GetData(12, ref o.BeadWidth); da.GetData(13, ref o.LayerHeight); o.Machine = SlicerIO.Get<MachineProfile>(da, 14); da.GetData(15, ref outFile); da.GetData(16, ref save);
            o.Variant = (ReshapeVariant)Math.Max(0, Math.Min(ReshapeVariants.Names.Length - 1, v)); s.Roof = (RoofMode)Math.Max(0, Math.Min(2, roof));
            string path = ReshapeIO.PathOf(file);
            string target = null;
            if (save)
            {
                string def = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) + "_reshaped" + Path.GetExtension(path) : "Reshaped.gcode";
                target = FreePath(string.IsNullOrWhiteSpace(outFile) && path.Length > 0 ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", def) : outFile, def);
                if (path.Length > 0 && string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Output is the file itself: the original is never written over."); return; }
            }
            Async.Run(da, job =>
            {
                var r = code.Count > 0 ? ReshapeCode.Run(string.Join("\n", code) + "\n", f, o) : ReshapeCode.File_(path, f, o);
                ReshapeIO.Verdict(job, r.Report.FirstOrDefault());
                foreach (var w in r.Warnings) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
                if (r.Refused == null && target != null)
                {
                    try { ReshapeCode.Save(target, r); r.Report.Add("Saved " + target); job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + target); }
                    catch (Exception e) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + e.Message); }
                }
                job.SetDataList(1, r.Report);
                if (r.Before != null) job.SetData(2, new GH_Toolpath(r.Before));
                if (r.Refused != null) { job.Message = "refused"; return; }
                job.Message = Inv.F($"{r.Changed} lines changed");
                job.SetDataList(0, r.Lines);
                if (r.After != null) job.SetData(3, new GH_Toolpath(r.After));
            });
        }
    }

    // ============================================================== Multi-Axis Check
    /// <summary>Checks a path with a tilted tool.</summary>
    public class MultiAxisCheckComponent : SlicerComponent
    {
        public MultiAxisCheckComponent()
            : base("Multi-Axis Check", "MA Check",
                  "Checks a path along its real tool axis (tilted or vertical): bead heights above the lap below, sideways steps, extrusion against the measured gaps, how far the tool is tilted and how fast it turns (degrees per mm and per second at the print speed), how steeply the path runs against the tool, what the head reaches into, and the material flow. Reach and axis limits of the robot are the job of Robot Program.") { }

        public override Guid ComponentGuid => new Guid("BB5F14B0-41BC-4A0D-8025-42822CD98309");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMultiAxisIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            ReshapeIO.HeadInputs((n, k, d, v) => p.AddNumberParameter(n, k, d, GH_ParamAccess.item, v));
            p.AddNumberParameter("Min Layer", "Hi", "Smallest layer height (mm), 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Layer", "Ha", "Largest layer height (mm), 0 = automatic (half the bead width)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Max Tilt", "Ti", "Largest tilt of the tool (degrees)", GH_ParamAccess.item, 45.0);
            p.AddNumberParameter("Max Turn Speed", "Ts", "How fast the tool may turn (degrees per second)", GH_ParamAccess.item, 30.0);
            p.AddNumberParameter("Max Turn", "Tm", "How fast the tool may turn along the path (degrees per mm)", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Max Flow", "Mf", "Largest material flow (mm3/s), 0 = only told", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "OK", "True if nothing is wrong", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was found; the first line is the verdict", GH_ParamAccess.list);
            p.AddNumberParameter("Bead Height", "H", "Measured height above the lap below, along the tool (mm), for every point; 0 = not measured", GH_ParamAccess.list);
            p.AddNumberParameter("Tilt", "Ti", "Tilt of the tool (degrees) for every point", GH_ParamAccess.list);
            p.AddPointParameter("Collisions", "X", "Where the head reaches into earlier beads", GH_ParamAccess.list);
            p.AddPointParameter("Bad Heights", "Bh", "Where the bead is too thick or too thin", GH_ParamAccess.list);
            p.AddPointParameter("Bad Flow", "Bf", "Where the extrusion is more than 10 % off the measured gap", GH_ParamAccess.list);
            p.AddPointParameter("Fast Turns", "Ft", "Where the tool turns too fast", GH_ParamAccess.list);
            p.AddTextParameter("Layers", "L", "One line per layer (CSV)", GH_ParamAccess.list);
        }

        protected override bool CanRunInBackground => true;

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 3) return;
            var o = new MultiAxisCheck.Options();
            if (!ReshapeIO.Head(this, da, 1, o.Head)) return;
            da.GetData(5, ref o.MinLayer); da.GetData(6, ref o.MaxLayer); da.GetData(7, ref o.MaxTilt); da.GetData(8, ref o.MaxTiltPerSecond); da.GetData(9, ref o.MaxTiltPerMm); da.GetData(10, ref o.MaxFlow);
            Async.Run(da, job =>
            {
                var r = MultiAxisCheck.Run(tp, o);
                job.Message = r.Ok ? "OK" : "Check!";
                ReshapeIO.Verdict(job, r.Report.FirstOrDefault());
                job.SetData(0, r.Ok); job.SetDataList(1, r.Report); job.SetDataList(2, r.Heights); job.SetDataList(3, r.Tilts);
                job.SetDataList(4, r.Collisions); job.SetDataList(5, r.BadHeights); job.SetDataList(6, r.BadFlow); job.SetDataList(7, r.FastTurns);
                job.SetDataList(8, PathReshape.LayerCsv(tp));
            });
        }
    }
}
