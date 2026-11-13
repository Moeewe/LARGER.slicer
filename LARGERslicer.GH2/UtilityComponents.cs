using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using Path = System.IO.Path;

namespace LARGERslicer.GH2
{
    // The small helpers of the Grasshopper 1 Utilities tab that the DXR and CNC definitions still use.
    // Same names, inputs and outputs as in Grasshopper 1 (LARGERslicer/Components/Utils, Export/SafeComponent.cs).

    // ============================================================== feedrate calculator
    [IoId("bdae6930-885e-43f9-930b-e748e656a447")]
    public sealed class FeedrateComponent : Component
    {
        public FeedrateComponent() : base(Io.Name("Feedrate Calculator", "Adjusts feedrate for constant speed. Converts target speed (mm/s) to feedrate (mm/min) and adjusts based on segment lengths. Results are rounded to whole numbers.", Io.Utilities, 0)) { }
        public FeedrateComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("FeedrateIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Segment Lengths", "Lengths", "List of segment lengths in mm", Access.Twig);
            inputs.AddNumber("Target Speed", "Speed", "Target speed in mm/s (e.g., 50 mm/s)").Set(50.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddNumber("Feedrates", "Feedrates", "Calculated feedrates in mm/min (rounded)", Access.Twig);
            outputs.AddText("Debugging", "Debug", "Debugging information with intermediate values", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            double[] lengths; double targetSpeed;
            if (!access.ItemArray(0, out lengths)) return;
            access.GetItem(1, out targetSpeed);
            var debugging = new List<string>();
            if (lengths == null || lengths.Length == 0)
            {
                debugging.Add("No segment lengths available.");
                access.SetTwig(0, new double[0], null, null);
                access.SetTwig(1, debugging.ToArray(), null, null);
                return;
            }
            double targetFeedrate = targetSpeed * 60.0;
            // G1 F is the speed along the path in mm/min, independent of the segment length: a
            // constant print speed is the same F for every move, never 0 (as in Grasshopper 1).
            double constant = Math.Max(1.0, Math.Round(targetFeedrate));
            debugging.Add($"Target feedrate (mm/min): {targetFeedrate:F2}");
            debugging.Add($"Number of segments: {lengths.Length}");
            debugging.Add("Constant speed: every move gets the same feedrate.");
            debugging.Add($"Feedrate for all moves: {constant:F0} mm/min");
            access.SetTwig(0, lengths.Select(_ => constant).ToArray(), null, null);
            access.SetTwig(1, debugging.ToArray(), null, null);
        }
    }

    // ============================================================== date timestamp
    [IoId("aa307042-84c4-45b6-b966-3154c063114c")]
    public sealed class DateTimestampComponent : Component
    {
        private Eto.Forms.UITimer _timer;
        private string _last;

        public DateTimestampComponent() : base(Io.Name("Date Timestamp", "Generates a timestamp string with automatic update every second. Format: yymmddHHMM_", Io.Utilities, 0)) { DocumentChanged += Moved; }
        public DateTimestampComponent(IReader reader) : base(reader) { DocumentChanged += Moved; }
        protected override IIcon IconInternal => Io.Icon("DateTimestampIcon");

        protected override void AddInputs(InputAdder inputs) { }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Timestamp", "TS", "Current timestamp string (format: yymmddHHMM_)");
        }

        protected override void Process(IDataAccess access)
        {
            _last = Stamp();
            access.SetItem(0, _last);
            // Grasshopper 1 used a WinForms timer; here an Eto UI timer looks every second and only
            // recomputes when the text changes (once a minute), so the canvas is not solved every second.
            if (_timer == null)
                Eto.Forms.Application.Instance.AsyncInvoke(StartTimer);
        }

        private static string Stamp() => DateTime.Now.ToString("yyMMddHHmm") + "_";

        private void StartTimer()
        {
            if (_timer != null) return;
            _timer = new Eto.Forms.UITimer { Interval = 1.0 };
            _timer.Elapsed += (s, e) =>
            {
                var doc = Document;
                if (doc == null) { StopTimer(); return; }
                if (Stamp() == _last) return;
                Expire();
                doc.Solution.Start(SolutionMode.Regular);
            };
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop(); _timer.Dispose(); _timer = null;
        }

        private void Moved(object sender, ObjectDocumentEventArgs e)
        {
            if (e.NewDocument == null) Eto.Forms.Application.Instance.AsyncInvoke(StopTimer);
        }
    }

    // ============================================================== desktop path
    [IoId("1a9a8687-44fc-4f49-800a-de9a7ee9e3ea")]
    public sealed class DesktopPathComponent : Component
    {
        public DesktopPathComponent() : base(Io.Name("Desktop Path", "Finds the current user's Desktop folder path cross-platform (Windows/Mac). Supports multiple languages.", Io.Utilities, 0)) { }
        public DesktopPathComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("DesktopPathIcon");

        protected override void AddInputs(InputAdder inputs) { }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Desktop Path", "Path", "Full path to the Desktop folder");
            outputs.AddText("Info", "Info", "Information about detected Desktop folder", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string desktop = DesktopFolder();
            access.SetItem(0, desktop);
            access.SetTwig(1, new[] { $"Desktop path detected: {desktop}", $"Path exists: {Directory.Exists(desktop)}" }, null, null);
        }

        internal static string DesktopFolder()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // common names of the desktop folder in several languages
            string[] candidates = { "Desktop", "Schreibtisch", "Escritorio", "Bureau", "Рабочий стол" };
            foreach (string name in candidates)
            {
                string desk = Path.Combine(home, name);
                if (Directory.Exists(desk)) return desk;
            }
            return home;
        }
    }

    // ============================================================== safe (write a text file)
    [IoId("c666a17e-44bb-4353-a607-85378e9adfcf")]
    public sealed class SafeComponent : Component
    {
        public SafeComponent() : base(Io.Name("Safe Component", "Writes text lines to a file. Combines folder path, filename, and extension. Uses UTF-8 encoding WITHOUT BOM (required for robot controllers).", Io.Utilities, 0)) { }
        public SafeComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SafeIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Folder Path", "Folder", "Folder path where file should be saved");
            inputs.AddText("File Name", "Name", "Filename without extension", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Extension", "Ext", "File extension (e.g., '.txt', '.dxr')").Set(".txt");
            inputs.AddText("Lines", "Lines", "List of text lines to write to file", Access.Twig);
            inputs.AddBoolean("Write", "Write", "True to write file, False to skip", ("Write", "Write the file"), ("Skip", "Do not write")).Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("File Path", "Path", "Full path to the created file");
            outputs.AddText("Info", "Info", "Information about file operation", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string folder, name, ext; string[] linesIn; bool write;
            if (!access.GetItem(0, out folder)) return;
            if (!access.GetItem(1, out name)) name = string.Empty;
            if (!access.GetItem(2, out ext) || ext == null) ext = ".txt";
            if (!access.ItemArray(3, out linesIn) || linesIn == null) return;
            access.GetItem(4, out write);
            var lines = linesIn.Select(l => l ?? string.Empty).ToList();

            var info = new List<string>();
            string filePath;
            void Done(string path) { access.SetItem(0, path ?? string.Empty); access.SetTwig(1, info.ToArray(), null, null); }

            if (string.IsNullOrWhiteSpace(folder))
            {
                info.Add("ERROR: Folder path is empty or null");
                info.Add("Please provide a valid folder path (e.g., use Desktop Path component)");
                Done(string.Empty);
                return;
            }
            if (!ext.StartsWith(".")) ext = "." + ext;
            folder = folder.TrimEnd('/', '\\');
            name = name?.TrimStart('/', '\\') ?? string.Empty;
            if (folder == "/" || folder == "\\" || folder == string.Empty || (folder.Length == 2 && folder[1] == ':'))
            {
                info.Add("ERROR: Cannot write to root directory or drive root");
                info.Add("Please provide a valid folder path:");
                info.Add("  Windows: C:\\Users\\username\\Desktop");
                info.Add("  macOS: /Users/username/Desktop");
                Done(string.Empty);
                return;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                filePath = folder;
                info.Add("WARNING: File name is empty - only folder path will be used");
            }
            else filePath = Path.Combine(folder, name + ext);
            info.Add($"Input folder: '{folder}'");
            info.Add($"Input name: '{name}'");
            info.Add($"Input extension: '{ext}'");
            info.Add($"Combined path: '{filePath}'");
            try
            {
                filePath = Path.GetFullPath(filePath);
                info.Add($"Full path: '{filePath}'");
            }
            catch (Exception ex)
            {
                info.Add($"ERROR: Invalid path - {ex.Message}");
                Done(filePath);
                return;
            }

            if (!write)
            {
                info.Add("Write is False - file not written");
                info.Add($"Target path: {filePath}");
                info.Add($"Lines ready: {lines.Count}");
                Done(filePath);
                return;
            }

            try
            {
                info.Add($"Attempting to write to: {filePath}");
                info.Add($"Directory exists: {Directory.Exists(folder)}");
                info.Add($"File will be created: {!File.Exists(filePath)}");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    info.Add($"Created directory: {folder}");
                }
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException($"Directory does not exist and could not be created: {folder}");
                // UTF-8 WITHOUT BOM, as robot controllers need it
                using (var writer = new StreamWriter(filePath, false, new System.Text.UTF8Encoding(false)))
                    foreach (string line in lines) writer.WriteLine(line);
                if (!File.Exists(filePath))
                    throw new IOException("File was not created - write operation may have failed silently");
                var fi = new FileInfo(filePath);
                info.Add($"File written successfully: {filePath}");
                info.Add($"Lines written: {lines.Count}");
                info.Add($"File size: {fi.Length} bytes");
            }
            catch (Exception ex)
            {
                info.Add("ERROR: Failed to write file");
                info.Add($"Error message: {ex.Message}");
                info.Add($"Error type: {ex.GetType().Name}");
                if (ex.InnerException != null) info.Add($"Inner exception: {ex.InnerException.Message}");
            }
            Done(filePath);
        }
    }
}
