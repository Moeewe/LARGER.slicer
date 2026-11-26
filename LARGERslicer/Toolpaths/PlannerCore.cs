using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>What the Path Planner is asked for, beside the Print Settings.</summary>
    public class PlannerOptions
    {
        public int Walls = 1;
        /// <summary>0 = inside the geometry, 1 = centred on it, 2 = outside.</summary>
        public int WallPosition = 0;
        public int BottomLayers = 0, TopLayers = 0;
        /// <summary>Solid layers: 0 = fitted rings, 1 = rings and a centre line, 2 = rings only, 3 = zigzag.</summary>
        public int Fill = 0;
        /// <summary>0 = none, 1 = ribs, 2 = wave, 3 = truss, 4 = meander.</summary>
        public int Infill = 0;
        /// <summary>Rib distance or wave length (mm). 0 = chosen from the bead width.</summary>
        public double InfillSize = 0;
        public int SkirtLoops = 1;
        /// <summary>0 = none, 1 = outside, 2 = inside, 3 = both sides.</summary>
        public int Brim = 0;
        /// <summary>0 = flat, 1 = follow the part, 2 = follow the top, 3 = follow the surface, 4 = spiral.</summary>
        public int Layers = 0;
        /// <summary>Slice angle in degrees (flat layers only).</summary>
        public double Angle = 0;
        /// <summary>0 = auto (the longest side), 1 = the sharpest corner, 2 = the back, 3 = nearest to SeamPoint.</summary>
        public int Seam = 0;
        public Point3d SeamPoint = Point3d.Unset;
        /// <summary>0 = best, 1 = fast, 2 = fill, 3 = seam.</summary>
        public int Goal = 0;
        /// <summary>How many paths to hand out (the best first).</summary>
        public int Variants = 1;
        /// <summary>The planner chooses the kind of layers itself (Layers and Angle are then only a start).</summary>
        public bool StrategyAuto = false;

        public static readonly string[] WallPositions = { "Inside the geometry", "Centred on the geometry", "Outside the geometry" };
        public static readonly string[] Fills = { "Fitted rings", "Rings and centre line", "Rings only", "Zigzag" };
        public static readonly string[] Infills = { "None", "Ribs", "Wave", "Truss", "Meander" };
        public static readonly string[] Brims = { "None", "Outside", "Inside", "Both sides" };
        public static readonly string[] LayerKinds = { "Flat", "Follow the part", "Follow the top", "Follow the surface", "Spiral" };
        public static readonly string[] Seams = { "Auto (longest side)", "Corner", "Back", "At Seam Point" };
        public static readonly string[] Goals = { "Best", "Fast", "Fill", "Seam" };

        private static int Clamp(int v, int max) => Math.Max(0, Math.Min(max, v));

        /// <summary>The settings as the planner reads them (one JSON object). Speeds go over in mm/s.</summary>
        public string ToJson(PrintSettings st)
        {
            st = st ?? new PrintSettings();
            var m = st.Machine ?? new MachineProfile();
            var j = new List<string>();
            Action<string, double> num = (k, v) => j.Add("\"" + k + "\":" + v.ToString("R", CultureInfo.InvariantCulture));
            Action<string, string> txt = (k, v) => j.Add("\"" + k + "\":\"" + v + "\"");
            Action<string, bool> flag = (k, v) => j.Add("\"" + k + "\":" + (v ? "true" : "false"));

            // from the Print Settings: bead, layer, speeds, nozzle, overlap, and how the machine counts material
            num("bead", st.BeadWidth); num("layer", st.LayerHeight); num("first-layer", Math.Max(0, st.FirstLayerHeight));
            num("overlap", st.Overlap);
            num("speed", st.Speed / 60.0); num("first-layer-speed", Math.Max(0, st.FirstLayerSpeed) / 60.0);
            num("min-speed", st.MinSpeed / 60.0); num("travel-speed", st.MaxSpeed / 60.0);
            num("min-layer-time", Math.Max(0, st.MinLayerTime)); num("short-layer-time", Math.Max(0, st.ShortLapTime));
            num("max-volume-rate", Math.Max(0, st.MaxVolumeRate));
            num("nozzle", Math.Max(0, st.Nozzle));
            txt("nozzle-shape", st.NozzleShape == Nozzles.Shape.Flat ? "flat" : st.NozzleShape == Nozzles.Shape.Pointed ? "pointed" : "unknown");
            if (st.SkinThickness > 0) num("skin", st.SkinThickness);
            if (st.MinRadius > 0) num("round", st.MinRadius);
            num("flow", st.Flow); num("density", st.Density);
            string name = m.Name ?? "";
            txt("machine", name.IndexOf("DXR", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("robot", StringComparison.OrdinalIgnoreCase) >= 0 ? "dxr"
                : name.IndexOf("Ginger", StringComparison.OrdinalIgnoreCase) >= 0 ? "ginger" : "generic");
            txt("section", m.RectangleSection ? "rectangle" : "bead");
            num("first-layer-factor", m.FirstLayerFactor);

            // from the component
            num("walls", Math.Max(1, Walls));
            txt("walls-position", new[] { "inside", "centred", "outside" }[Clamp(WallPosition, 2)]);
            num("bottom", Math.Max(0, BottomLayers)); num("top", Math.Max(0, TopLayers));
            txt("fill", new[] { "fitted", "centre", "rings", "zigzag" }[Clamp(Fill, 3)]);
            int infill = Clamp(Infill, 4);
            if (infill == 1) num("ribs", InfillSize > 0 ? InfillSize : 12 * st.BeadWidth);
            else if (infill > 1)
            {
                num("wave", -1); // as deep as the part allows
                txt("wave-shape", new[] { "wave", "truss", "meander" }[infill - 2]);
                if (InfillSize > 0) num("wave-length", InfillSize);
            }
            num("skirt", Math.Max(0, SkirtLoops));
            txt("brim", new[] { "none", "outside", "inside", "both" }[Clamp(Brim, 3)]);
            int layers = Clamp(Layers, 4);
            txt("layers", new[] { "flat", "follow", "top", "surface", "flat" }[layers]);
            flag("spiral", layers == 4);
            if (layers == 0 && Math.Abs(Angle) > 1e-9) num("angle", Angle);
            int seam = Clamp(Seam, 3);
            if (SeamPoint.IsValid && (seam == 3 || seam == 0))
                j.Add("\"seam\":[" + SeamPoint.X.ToString("R", CultureInfo.InvariantCulture) + "," + SeamPoint.Y.ToString("R", CultureInfo.InvariantCulture) + "]");
            else txt("seam", new[] { "auto", "corner", "back", "auto" }[seam]);
            txt("plan", new[] { "best", "fast", "fill", "seam" }[Clamp(Goal, 3)]);
            num("variants", Math.Max(1, Variants));
            if (StrategyAuto) txt("strategy", "auto");
            return "{" + string.Join(",", j) + "}";
        }
    }

    /// <summary>One path of the planner.</summary>
    public class PlannerVariant
    {
        /// <summary>What it is best at ("best", "fill", "fast", "seam"), or "alt1", "alt2" ...</summary>
        public string Name = "";
        /// <summary>The choices it was built with, in words.</summary>
        public string Choices = "";
        public Toolpath Path;
        /// <summary>True when the planner's own check passes.</summary>
        public bool Ok;
        /// <summary>Its measures (cost, crossings, twice_mm, half_on_mm, unfilled_mm2, travel_mm, minutes ...).</summary>
        public Dictionary<string, object> Scores = new Dictionary<string, object>();
        /// <summary>Verdict, numbers, check lines ("check"), notes ("notes") and design rules ("design_rules").</summary>
        public Dictionary<string, object> Report = new Dictionary<string, object>();

        public double Number(string key) => Report.TryGetValue(key, out object v) && v is double d ? d : 0.0;
        public List<string> Lines(string key) => Report.TryGetValue(key, out object v) && v is List<object> l ? l.OfType<string>().ToList() : new List<string>();
    }

    /// <summary>What a call of the planner hands back.</summary>
    public class PlannerResult
    {
        /// <summary>Null = fine; else why there is no path, in a sentence.</summary>
        public string Error;
        public List<PlannerVariant> Variants = new List<PlannerVariant>();
        /// <summary>The scores of all paths side by side (a text table).</summary>
        public string Table = "";
        /// <summary>The geometry analysis (JSON).</summary>
        public string Analysis = "";
        /// <summary>The kinds of layers that were compared (text; only with Strategy Auto).</summary>
        public string Strategies = "";

        public PlannerVariant Best => Variants.Count > 0 ? Variants[0] : null;

        /// <summary>The report of the best path, as lines to read.</summary>
        public List<string> ReportLines()
        {
            var r = new List<string>();
            if (Error != null) { r.Add("NO PATH: " + Error); return r; }
            var b = Best; if (b == null) return r;
            r.Add(b.Ok ? "Printable." : "NOT printable as it is. Read the lines below.");
            r.Add(Inv.F($"{b.Number("layers"):0} layers, path {b.Number("path_mm") / 1000.0:0.00} m, {b.Path.Count} points, about {b.Number("minutes"):0} min, {b.Number("grams"):0} g."));
            r.Add("Taken: " + b.Name + (b.Choices.Length > 0 ? " (" + b.Choices + ")" : "") + (Variants.Count > 1 ? Inv.F($"; {Variants.Count - 1} more in Variants") : "") + ".");
            string strategy = b.Report.TryGetValue("strategy", out object s) ? s as string : null;
            if (!string.IsNullOrEmpty(strategy)) r.Add("Layers chosen by the planner: " + strategy + ".");
            r.AddRange(b.Lines("check"));
            foreach (string n in b.Lines("notes")) r.Add(n.StartsWith("Note:") || n.StartsWith("Warning:") ? n : "Note: " + n);
            foreach (string n in b.Lines("design_rules")) r.Add("Design rules: " + n);
            r.Add(b.Ok
                ? "NEXT: connect Toolpath to Machine G-code, Robot Program or Printer Simulation. Start and End is not needed: skirt, brim and the exit are in the path."
                : "NEXT: change what the lines above name (or try another Goal, Fill or Seam), then connect Toolpath to an output component.");
            return r;
        }
    }

    /// <summary>
    /// The path planner: an optional native library that plans the whole print path of a part (skirt,
    /// brim, walls, solid layers, infill, the way from layer to layer, the exit) in several ways and
    /// hands out the best ones as Toolpaths. The library is a separate file that is not part of the
    /// public plug-in; without it everything else works as before and <see cref="Available"/> is false.
    /// It is looked for in a folder "native" beside the plug-in: liblarger_core.dylib on macOS,
    /// larger_core.dll on Windows.
    /// </summary>
    public static class PlannerCore
    {
        /// <summary>The interface version this code speaks (larger_abi of the library).</summary>
        public const int Abi = 1;

        private static readonly object Gate = new object();
        private static bool _tried;
        private static IntPtr _lib;
        private static string _why = "", _version = "", _path = "";

        /// <summary>True when the planner library is installed and loaded.</summary>
        public static bool Available { get { Ensure(); return _lib != IntPtr.Zero; } }
        /// <summary>When not available: why, in one sentence. Empty when it is.</summary>
        public static string Why { get { Ensure(); return _why; } }
        /// <summary>Version of the library ("" when not available).</summary>
        public static string Version { get { Ensure(); return _version; } }
        /// <summary>The file that was loaded ("" when not available).</summary>
        public static string LibraryPath { get { Ensure(); return _path; } }

        public const string NotInstalled = "The path planner is not installed: it is an optional library that does not come with the public plug-in (Easy Slicer and the single components work without it).";

        // ---- the C functions
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FnText();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnInt();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FnSlice(float[] triangles, int count, byte[] json, IntPtr cancel);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FnOfHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnCount(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FnOfVariant(IntPtr h, int i);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnPoints(IntPtr h, int i);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnFree(IntPtr h);

        private static FnSlice _slice;
        private static FnOfHandle _error, _table, _analysis, _strategies;
        private static FnCount _count;
        private static FnPoints _points;
        private static FnOfVariant _name, _choices, _scores, _report, _xyz, _axis, _height, _width, _flow, _role, _layer;
        private static FnFree _free;

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static bool IsMac
        {
            get
            {
                try { return RuntimeInformation.IsOSPlatform(OSPlatform.OSX); }
                catch { return Environment.OSVersion.Platform == PlatformID.MacOSX || Directory.Exists("/System/Library/CoreServices"); }
            }
        }

        /// <summary>Name of the library file on this system.</summary>
        public static string FileName => IsWindows ? "larger_core.dll" : IsMac ? "liblarger_core.dylib" : "liblarger_core.so";

        /// <summary>The folder the plug-in file lies in, set by the plug-in when it knows it (the library is looked for in
        /// its subfolder "native" first).</summary>
        public static string PluginFolder;

        /// <summary>The folders of the installed LARGERslicer package (packages/&lt;Rhino&gt;/LARGERslicer/&lt;version&gt;/&lt;framework&gt;),
        /// newest Rhino first: the fallback when the plug-in folder is not known.</summary>
        private static List<string> PackageFolders()
        {
            var list = new List<string>();
            try
            {
                string root = IsWindows
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library", "Application Support");
                string packages = Path.Combine(root, "McNeel", "Rhinoceros", "packages");
                int running = 0; try { running = Rhino.RhinoApp.ExeVersion; } catch { }
                foreach (string ver in new[] { running + ".0", "9.0", "8.0", "7.0" }.Distinct())
                {
                    string pkg = Path.Combine(packages, ver, "LARGERslicer");
                    string manifest = Path.Combine(pkg, "manifest.txt");
                    if (!File.Exists(manifest)) continue;
                    string version = File.ReadAllLines(manifest).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                    if (string.IsNullOrEmpty(version)) continue;
                    string dir = Path.Combine(pkg, version);
                    if (!Directory.Exists(dir)) continue;
                    list.Add(dir);
                    list.AddRange(Directory.GetDirectories(dir));
                    if (ver == running + ".0") break; // the package of the Rhino that runs is enough
                }
            }
            catch { }
            return list;
        }

        /// <summary>Where the library is looked for, in this order.</summary>
        public static List<string> Candidates()
        {
            var list = new List<string>();
            try { string env = Environment.GetEnvironmentVariable("LARGER_PLANNER_CORE"); if (!string.IsNullOrEmpty(env)) list.Add(env); } catch { }
            var dirs = new List<string>();
            // Grasshopper 1 loads the .gha from memory, so the assembly does not know its folder: the plug-in tells it
            if (!string.IsNullOrEmpty(PluginFolder)) dirs.Add(PluginFolder);
            dirs.AddRange(PackageFolders());
            try { string loc = typeof(PlannerCore).Assembly.Location; if (!string.IsNullOrEmpty(loc)) dirs.Add(Path.GetDirectoryName(loc)); } catch { }
            try { if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory)) dirs.Add(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            foreach (string d in dirs.Where(d => !string.IsNullOrEmpty(d)).Distinct())
            {
                list.Add(Path.Combine(d, "native", FileName));
                list.Add(Path.Combine(d, FileName));
            }
            return list;
        }

        private static void Ensure()
        {
            if (_tried) return;
            lock (Gate)
            {
                if (_tried) return;
                try { Load(); }
                catch (Exception e) { _lib = IntPtr.Zero; _why = "The path planner was found but could not be loaded on this computer (" + e.Message + ")."; }
                _tried = true;
            }
        }

        private static void Load()
        {
            string file = Candidates().FirstOrDefault(File.Exists);
            if (file == null) { _why = NotInstalled; return; }
            IntPtr lib = Native.Open(file, out string problem);
            if (lib == IntPtr.Zero)
            {
                _why = "The path planner was found but could not be loaded on this computer: the file may be built for another processor or be damaged" + (string.IsNullOrEmpty(problem) ? "." : " (" + problem + ").");
                return;
            }
            var abi = Get<FnInt>(lib, "larger_abi");
            var version = Get<FnText>(lib, "larger_version");
            if (abi == null || version == null) { _why = "The file " + Path.GetFileName(file) + " beside the plug-in is not the path planner."; return; }
            int have = abi();
            if (have != Abi) { _why = Inv.F($"The path planner that is installed belongs to another version of the plug-in (its interface is {have}, this plug-in speaks {Abi})."); return; }
            _slice = Get<FnSlice>(lib, "larger_slice_cancel");
            _error = Get<FnOfHandle>(lib, "larger_error"); _table = Get<FnOfHandle>(lib, "larger_plan_table");
            _analysis = Get<FnOfHandle>(lib, "larger_analysis"); _strategies = Get<FnOfHandle>(lib, "larger_strategies");
            _count = Get<FnCount>(lib, "larger_variant_count"); _points = Get<FnPoints>(lib, "larger_variant_points");
            _name = Get<FnOfVariant>(lib, "larger_variant_name"); _choices = Get<FnOfVariant>(lib, "larger_variant_choices");
            _scores = Get<FnOfVariant>(lib, "larger_variant_scores"); _report = Get<FnOfVariant>(lib, "larger_variant_report");
            _xyz = Get<FnOfVariant>(lib, "larger_variant_xyz"); _axis = Get<FnOfVariant>(lib, "larger_variant_axis");
            _height = Get<FnOfVariant>(lib, "larger_variant_height"); _width = Get<FnOfVariant>(lib, "larger_variant_width");
            _flow = Get<FnOfVariant>(lib, "larger_variant_flow"); _role = Get<FnOfVariant>(lib, "larger_variant_role");
            _layer = Get<FnOfVariant>(lib, "larger_variant_layer"); _free = Get<FnFree>(lib, "larger_free");
            if (_slice == null || _error == null || _table == null || _analysis == null || _strategies == null || _count == null || _points == null || _name == null || _choices == null
                || _scores == null || _report == null || _xyz == null || _axis == null || _height == null || _width == null || _flow == null || _role == null || _layer == null || _free == null)
            { _why = "The path planner that is installed lacks a function this plug-in needs: it belongs to another version."; return; }
            _version = Text(version());
            _path = file; _why = ""; _lib = lib;
        }

        private static T Get<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = Native.Symbol(lib, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        /// <summary>A UTF-8 text of the library (it stays the library's).</summary>
        private static string Text(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            if (n == 0) return "";
            var bytes = new byte[n]; Marshal.Copy(p, bytes, 0, n);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>The triangles of a mesh as the planner takes them: 9 numbers a triangle, where the mesh lies.</summary>
        public static float[] Triangles(Mesh mesh)
        {
            if (mesh == null) return new float[0];
            float[] v = mesh.Vertices.ToFloatArray();
            int[] f = mesh.Faces.ToIntArray(true);
            var t = new float[f.Length * 3];
            for (int i = 0; i < f.Length; i++)
            {
                int a = f[i] * 3;
                t[3 * i] = v[a]; t[3 * i + 1] = v[a + 1]; t[3 * i + 2] = v[a + 2];
            }
            return t;
        }

        /// <summary>Plans the path of a mesh with these settings. Never throws: a problem is the Error of the result.</summary>
        public static PlannerResult Run(Mesh mesh, PrintSettings settings, PlannerOptions options, CancellationToken token = default(CancellationToken))
            => Run(Triangles(mesh), settings, options, token);

        /// <summary>The same from triangles (9 numbers each: x, y, z of three corners, in mm).</summary>
        public static PlannerResult Run(float[] triangles, PrintSettings settings, PlannerOptions options, CancellationToken token = default(CancellationToken))
        {
            settings = settings ?? new PrintSettings();
            return Run(triangles, (options ?? new PlannerOptions()).ToJson(settings), settings, token);
        }

        /// <summary>The same with the settings as the planner's own JSON text; 'settings' goes into the Toolpaths.</summary>
        public static PlannerResult Run(float[] triangles, string json, PrintSettings settings, CancellationToken token = default(CancellationToken))
        {
            var res = new PlannerResult();
            if (!Available) { res.Error = Why; return res; }
            if (triangles == null || triangles.Length < 9) { res.Error = "The part has no triangles."; return res; }
            settings = settings ?? new PrintSettings();
            IntPtr h = IntPtr.Zero, stop = IntPtr.Zero;
            var registration = default(CancellationTokenRegistration);
            try
            {
                // a number the library looks at now and then: not 0 = the result is not needed any more
                stop = Marshal.AllocHGlobal(4); Marshal.WriteInt32(stop, 0);
                IntPtr flag = stop;
                if (token.CanBeCanceled) registration = token.Register(() => Marshal.WriteInt32(flag, 1));
                byte[] text = Encoding.UTF8.GetBytes(json + "\0");
                h = _slice(triangles, triangles.Length / 9, text, stop);
                if (h == IntPtr.Zero) { res.Error = "The path planner gave no answer."; return res; }
                string error = Text(_error(h));
                if (error.Length > 0) { res.Error = error; return res; }
                res.Table = Text(_table(h)); res.Analysis = Text(_analysis(h)); res.Strategies = Text(_strategies(h));
                int count = _count(h);
                for (int i = 0; i < count; i++)
                {
                    var v = new PlannerVariant { Name = Text(_name(h, i)), Choices = Text(_choices(h, i)) };
                    v.Scores = Json.Object(Text(_scores(h, i)));
                    v.Report = Json.Object(Text(_report(h, i)));
                    v.Ok = v.Report.TryGetValue("printable", out object ok) && ok is bool b && b;
                    v.Path = PathOf(h, i, settings);
                    if (v.Path != null) res.Variants.Add(v);
                }
                if (res.Variants.Count == 0) res.Error = "The path planner made no path.";
            }
            catch (Exception e) { res.Variants.Clear(); res.Error = "The path planner failed: " + e.Message; }
            finally
            {
                registration.Dispose(); // (waits for a callback that is running)
                if (h != IntPtr.Zero) try { _free(h); } catch { }
                if (stop != IntPtr.Zero) Marshal.FreeHGlobal(stop);
            }
            return res;
        }

        private static Toolpath PathOf(IntPtr h, int i, PrintSettings settings)
        {
            int n = _points(h, i);
            if (n < 2) return null;
            IntPtr pxyz = _xyz(h, i), paxis = _axis(h, i), ph = _height(h, i), pw = _width(h, i), pf = _flow(h, i), pr = _role(h, i), pl = _layer(h, i);
            if (pxyz == IntPtr.Zero || paxis == IntPtr.Zero || ph == IntPtr.Zero || pw == IntPtr.Zero || pf == IntPtr.Zero || pr == IntPtr.Zero || pl == IntPtr.Zero) return null;
            var xyz = new double[3 * n]; var axis = new double[3 * n];
            var height = new double[n]; var width = new double[n]; var flow = new double[n];
            var role = new int[n]; var layer = new int[n];
            Marshal.Copy(pxyz, xyz, 0, 3 * n); Marshal.Copy(paxis, axis, 0, 3 * n);
            Marshal.Copy(ph, height, 0, n); Marshal.Copy(pw, width, 0, n); Marshal.Copy(pf, flow, 0, n);
            Marshal.Copy(pr, role, 0, n); Marshal.Copy(pl, layer, 0, n);
            var tp = new Toolpath { Settings = settings.Duplicate() };
            tp.Points.Capacity = n; tp.Normals.Capacity = n; tp.Heights.Capacity = n; tp.Widths.Capacity = n; tp.Flows.Capacity = n; tp.Roles.Capacity = n; tp.Layers.Capacity = n;
            for (int k = 0; k < n; k++)
            {
                int r = role[k];
                tp.Add(new Point3d(xyz[3 * k], xyz[3 * k + 1], xyz[3 * k + 2]), new Vector3d(axis[3 * k], axis[3 * k + 1], axis[3 * k + 2]),
                    height[k], width[k], flow[k], r >= 0 && r <= 6 ? (PathRole)r : PathRole.Wall, layer[k]);
            }
            return tp;
        }

        // ---------------------------------------------------------------- loading a native library by its full path
        private static class Native
        {
#if NET5_0_OR_GREATER
            public static IntPtr Open(string file, out string problem)
            {
                problem = null;
                try { return NativeLibrary.Load(file); }
                catch (Exception e) { problem = e.GetType().Name; return IntPtr.Zero; }
            }

            public static IntPtr Symbol(IntPtr lib, string name) => NativeLibrary.TryGetExport(lib, name, out IntPtr p) ? p : IntPtr.Zero;
#else
            // .NET Framework (Rhino 7, Rhino 8 on Windows in .NET Framework mode) and Mono (Rhino 7 on the Mac)
            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string file);
            [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr lib, string name);
            [DllImport("libdl")] private static extern IntPtr dlopen(string file, int mode);
            [DllImport("libdl")] private static extern IntPtr dlsym(IntPtr lib, string name);

            public static IntPtr Open(string file, out string problem)
            {
                problem = null;
                try
                {
                    if (IsWindows)
                    {
                        IntPtr h = LoadLibraryW(file);
                        if (h == IntPtr.Zero) problem = "Windows error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                        return h;
                    }
                    return dlopen(file, 2);
                }
                catch (Exception e) { problem = FirstLine(e.Message); return IntPtr.Zero; }
            }

            public static IntPtr Symbol(IntPtr lib, string name)
            {
                try { return IsWindows ? GetProcAddress(lib, name) : dlsym(lib, name); }
                catch { return IntPtr.Zero; }
            }
#endif
            private static string FirstLine(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                int cut = s.IndexOf('\n'); if (cut > 0) s = s.Substring(0, cut);
                return s.Length > 200 ? s.Substring(0, 200) : s.Trim();
            }
        }

        // ---------------------------------------------------------------- reading the planner's JSON answers
        /// <summary>A small JSON reader: objects become dictionaries, lists List&lt;object&gt;, numbers double, true/false bool.</summary>
        internal static class Json
        {
            public static Dictionary<string, object> Object(string text)
            {
                try { int i = 0; return Value(text ?? "", ref i) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
                catch { return new Dictionary<string, object>(); }
            }

            private static void Space(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private static object Value(string s, ref int i)
            {
                Space(s, ref i);
                if (i >= s.Length) throw new FormatException();
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    while (true)
                    {
                        Space(s, ref i);
                        if (s[i] == '}') { i++; return d; }
                        if (s[i] == ',') { i++; continue; }
                        string key = String(s, ref i);
                        Space(s, ref i); if (s[i] != ':') throw new FormatException(); i++;
                        d[key] = Value(s, ref i);
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    while (true)
                    {
                        Space(s, ref i);
                        if (s[i] == ']') { i++; return l; }
                        if (s[i] == ',') { i++; continue; }
                        l.Add(Value(s, ref i));
                    }
                }
                if (c == '"') return String(s, ref i);
                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
                string word = s.Substring(start, i - start);
                if (word == "true") return true;
                if (word == "false") return false;
                if (word == "null") return null;
                return double.Parse(word, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private static string String(string s, ref int i)
            {
                if (s[i] != '"') throw new FormatException();
                var b = new StringBuilder(); i++;
                while (s[i] != '"')
                {
                    char c = s[i++];
                    if (c != '\\') { b.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': b.Append('\n'); break;
                        case 't': b.Append('\t'); break;
                        case 'r': b.Append('\r'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'u': b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                        default: b.Append(e); break;
                    }
                }
                i++;
                return b.ToString();
            }
        }
    }
}
