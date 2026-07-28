using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Validates DXR/GCode files before loading them into the machine software.
    /// Can use explicit file path or auto-pick latest file from a folder.
    /// </summary>
    public class DXRFileHealthCheckComponent : GH_Component
    {
        private static readonly Regex DxrMoveRegex = new Regex(@"^\s*N\d+\s+G1\b", RegexOptions.IgnoreCase);
        private static readonly Regex GCodeMoveRegex = new Regex(@"^\s*G0?1\b", RegexOptions.IgnoreCase);
        private static readonly Regex ZValueRegex = new Regex(@"(?:^|\s)Z(?<z>[+-]?\d+(?:[\.,]\d+)?)\b", RegexOptions.IgnoreCase);
        private static readonly Regex SafeBaseNameRegex = new Regex(@"^[A-Za-z0-9_-]+$");

        public DXRFileHealthCheckComponent()
          : base("DXR File Health Check", "DXR Check",
              "Checks DXR/GCode files for readability and printable safety. Supports optional direct code input and reports a single status.",
              "LARGER", "DXR")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "Path", "Optional explicit file path to check (.dxr, .gcode, .nc, .tap).", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Watch Folder", "Folder", "Optional folder to scan for latest file. If empty, common default folders are tried.", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("Use Latest From Folder", "Latest", "If True, checks newest matching file in folder. If False, uses explicit File Path.", GH_ParamAccess.item, true);
            pManager.AddTextParameter("Allowed Extensions", "Ext", "Extensions considered for auto-scan (e.g. .dxr, .gcode, .nc).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Filename Length", "NameLen", "Maximum allowed filename length in characters.", GH_ParamAccess.item, 64);
            pManager.AddNumberParameter("Large File Threshold MB", "LargeMB", "If file is larger than this and has almost no valid commands, it becomes NO GO.", GH_ParamAccess.item, 25.0);
            pManager.AddIntegerParameter("Min Command Lines", "MinCmd", "Minimum amount of valid movement command lines.", GH_ParamAccess.item, 3);
            pManager.AddTextParameter("Code Input", "Code", "Optional DXR/GCode text or line list to validate directly. If provided, file loading is skipped.", GH_ParamAccess.list);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[3].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Checked File", "File", "File that was checked.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Short status text.", GH_ParamAccess.item);
            pManager.AddTextParameter("Report", "Report", "Detailed validation report.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = string.Empty;
            string watchFolder = string.Empty;
            bool useLatest = true;
            var allowedExtensionsInput = new List<string>();
            int maxFileNameLength = 64;
            double largeFileThresholdMb = 25.0;
            int minCommandLines = 3;
            string codeInput = string.Empty;

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref watchFolder);
            DA.GetData(2, ref useLatest);
            DA.GetDataList(3, allowedExtensionsInput);
            DA.GetData(4, ref maxFileNameLength);
            DA.GetData(5, ref largeFileThresholdMb);
            DA.GetData(6, ref minCommandLines);
            codeInput = CollectCodeInputText(DA);

            if (maxFileNameLength < 8)
                maxFileNameLength = 8;
            if (largeFileThresholdMb < 0.1)
                largeFileThresholdMb = 0.1;
            if (minCommandLines < 1)
                minCommandLines = 1;

            var allowedExtensions = NormalizeExtensions(allowedExtensionsInput);
            if (allowedExtensions.Count == 0)
            {
                allowedExtensions.Add(".dxr");
                allowedExtensions.Add(".gcode");
                allowedExtensions.Add(".nc");
                allowedExtensions.Add(".tap");
            }

            var report = new List<string>();
            var fatalIssues = new List<string>();
            var warnings = new List<string>();
            bool usingCodeInput = !string.IsNullOrWhiteSpace(codeInput);

            // Prevent expensive repeated execution when a connected Code input provides many items/branches.
            if (usingCodeInput && DA.Iteration > 0)
                return;

            report.Add($"Config: Latest={useLatest}, MaxNameLen={maxFileNameLength}, LargeMB={largeFileThresholdMb:0.##}, MinCmd={minCommandLines}, CodeInput={usingCodeInput}");
            report.Add($"Extensions: {string.Join(", ", allowedExtensions)}");

            long fileSizeBytes = 0;
            double fileSizeMb = 0.0;
            string[] lines = Array.Empty<string>();
            int textLineCount = 0;
            int nonEmptyLineCount = 0;
            string resolvedPath = string.Empty;
            string extension = string.Empty;
            bool hasDxrMarker = false;

            if (usingCodeInput)
            {
                resolvedPath = "CODE INPUT";
                report.Add("Source: direct code input");

                lines = SplitLines(codeInput);
                textLineCount = lines.Length;
                nonEmptyLineCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                fileSizeBytes = Encoding.UTF8.GetByteCount(codeInput);
                fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

                report.Add($"Input size: {fileSizeBytes} bytes ({fileSizeMb:0.###} MB)");
                report.Add($"Text lines: {textLineCount}, non-empty lines: {nonEmptyLineCount}");

                if (nonEmptyLineCount == 0)
                    fatalIssues.Add("Code input contains no usable text content.");

                hasDxrMarker = lines.Any(l => (l ?? string.Empty).IndexOf("DXR.KUKA", StringComparison.OrdinalIgnoreCase) >= 0);
                extension = hasDxrMarker ? ".dxr" : ".gcode";
                report.Add($"Code mode type guess: {extension}");
            }
            else
            {
                string resolvedFolder = ResolveFolder(watchFolder, report);
                resolvedPath = ResolveFilePath(filePath, resolvedFolder, useLatest, allowedExtensions, report);

                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    fatalIssues.Add("No file selected or found.");
                    BuildOutputs(DA, string.Empty, fatalIssues, warnings, report);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No file found to check.");
                    return;
                }

                report.Add($"Selected file: {resolvedPath}");

                if (!File.Exists(resolvedPath))
                    fatalIssues.Add("File does not exist.");

                extension = (Path.GetExtension(resolvedPath) ?? string.Empty).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                    fatalIssues.Add($"Extension '{extension}' is not allowed.");

                string fileName = Path.GetFileName(resolvedPath) ?? string.Empty;
                string baseName = Path.GetFileNameWithoutExtension(resolvedPath) ?? string.Empty;

                ValidateFilename(fileName, baseName, maxFileNameLength, fatalIssues, warnings, report);

                if (fatalIssues.Count == 0)
                {
                    try
                    {
                        var fi = new FileInfo(resolvedPath);
                        fileSizeBytes = fi.Length;
                        fileSizeMb = fi.Length / (1024.0 * 1024.0);
                        report.Add($"File size: {fileSizeBytes} bytes ({fileSizeMb:0.###} MB)");

                        if (fileSizeBytes <= 0)
                            fatalIssues.Add("File is empty (0 bytes).");

                        if (LooksBinary(resolvedPath))
                            fatalIssues.Add("File looks binary/corrupted (contains null bytes). DXR/GCode must be plain text.");

                        lines = File.ReadAllLines(resolvedPath, Encoding.UTF8);
                        textLineCount = lines.Length;
                        nonEmptyLineCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                        report.Add($"Text lines: {textLineCount}, non-empty lines: {nonEmptyLineCount}");

                        if (nonEmptyLineCount == 0)
                            fatalIssues.Add("File contains no usable text content.");
                    }
                    catch (Exception ex)
                    {
                        fatalIssues.Add($"File cannot be read: {ex.Message}");
                    }
                }
            }

            int movementLines = 0;
            bool hasM29 = false;
            bool hasGCodeEndMarker = false;
            double? minDetectedZ = null;
            double? firstPositiveZ = null;

            if (fatalIssues.Count == 0)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    bool isMoveLine = false;

                    if (extension == ".dxr")
                    {
                        if (DxrMoveRegex.IsMatch(line))
                        {
                            movementLines++;
                            isMoveLine = true;
                        }

                        if (!hasM29 && line.Trim().Equals("M29", StringComparison.OrdinalIgnoreCase))
                            hasM29 = true;

                        if (!hasDxrMarker && line.IndexOf("DXR.KUKA", StringComparison.OrdinalIgnoreCase) >= 0)
                            hasDxrMarker = true;
                    }
                    else
                    {
                        if (GCodeMoveRegex.IsMatch(line))
                        {
                            movementLines++;
                            isMoveLine = true;
                        }

                        if (!hasGCodeEndMarker)
                        {
                            string t = line.Trim();
                            if (t.Equals("M2", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("M30", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("M29", StringComparison.OrdinalIgnoreCase))
                            {
                                hasGCodeEndMarker = true;
                            }
                        }
                    }

                    if (isMoveLine && TryExtractZValue(line, out double z))
                    {
                        if (!minDetectedZ.HasValue || z < minDetectedZ.Value)
                            minDetectedZ = z;

                        if (z > 0.0 && !firstPositiveZ.HasValue)
                            firstPositiveZ = z;
                    }
                }

                report.Add($"Detected movement lines: {movementLines}");
                report.Add(minDetectedZ.HasValue
                    ? $"Detected minimum Z: {minDetectedZ.Value:0.###} mm"
                    : "Detected minimum Z: (no Z value found in movement lines)");
                report.Add(firstPositiveZ.HasValue
                    ? $"Detected first positive layer Z: {firstPositiveZ.Value:0.###} mm"
                    : "Detected first positive layer Z: (none)");

                if (movementLines < minCommandLines)
                {
                    fatalIssues.Add($"Too few movement lines ({movementLines} < {minCommandLines}). File likely incomplete/invalid.");
                }

                if (extension == ".dxr")
                {
                    if (!hasDxrMarker)
                    {
                        fatalIssues.Add("DXR marker missing ('DXR.KUKA').");
                    }
                    if (!hasM29)
                    {
                        fatalIssues.Add("DXR end marker missing (M29).");
                    }
                }
                else
                {
                    if (!hasGCodeEndMarker)
                        warnings.Add("No clear GCode end marker (M2/M30/M29) detected.");
                }

                if (fileSizeMb >= largeFileThresholdMb && movementLines < minCommandLines * 2)
                {
                    fatalIssues.Add($"Large file ({fileSizeMb:0.##} MB) but almost no valid commands. Suspicious empty/corrupt export.");
                }

                if (fileSizeMb >= largeFileThresholdMb && nonEmptyLineCount < 50)
                {
                    fatalIssues.Add($"Large file ({fileSizeMb:0.##} MB) with very low content ({nonEmptyLineCount} lines). Suspicious export.");
                }

                if (minDetectedZ.HasValue && minDetectedZ.Value <= 0.0)
                {
                    fatalIssues.Add($"Build plate safety: detected Z <= 0 ({minDetectedZ.Value:0.###} mm). This would crash into the build plate.");
                }

                if (firstPositiveZ.HasValue && firstPositiveZ.Value < 1.5)
                {
                    warnings.Add($"First positive layer is below 1.5 mm ({firstPositiveZ.Value:0.###} mm). Verify first layer safety.");
                }
            }

            bool go = fatalIssues.Count == 0;
            BuildOutputs(DA, resolvedPath, fatalIssues, warnings, report);

            if (go)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GO: File passed validation.");
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "NO GO: File failed validation.");
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private string CollectCodeInputText(IGH_DataAccess DA)
        {
            var parts = new List<string>();

            if (Params.Input.Count > 7 && Params.Input[7] != null && Params.Input[7].SourceCount > 0)
            {
                var volatileData = Params.Input[7].VolatileData;
                if (volatileData != null)
                {
                    foreach (IGH_Goo goo in volatileData.AllData(true))
                    {
                        if (goo == null)
                            continue;

                        string text = goo.ToString();
                        if (text != null)
                            parts.Add(text);
                    }
                }
            }
            else
            {
                // Handles manually entered local values on the input parameter.
                DA.GetDataList(7, parts);
            }

            if (parts.Count == 0)
                return string.Empty;

            return string.Join("\n", parts);
        }

        private static bool TryExtractZValue(string line, out double z)
        {
            z = 0.0;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            Match m = ZValueRegex.Match(line);
            if (!m.Success)
                return false;

            string token = (m.Groups["z"].Value ?? string.Empty).Trim().Replace(',', '.');
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }

        private static bool ValidateFilename(
            string fileName,
            string baseName,
            int maxLength,
            List<string> fatalIssues,
            List<string> warnings,
            List<string> report)
        {
            bool ok = true;
            report.Add($"Filename: {fileName}");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fatalIssues.Add("Filename is empty.");
                return false;
            }

            if (fileName.Length > maxLength)
                warnings.Add($"Filename is long ({fileName.Length} > {maxLength}). Use shorter names for machine stability.");

            if (baseName.Contains("."))
            {
                fatalIssues.Add("Filename contains extra dot in basename. Use only one dot before extension.");
                ok = false;
            }

            if (!SafeBaseNameRegex.IsMatch(baseName))
            {
                fatalIssues.Add("Filename contains special characters. Allowed: A-Z, a-z, 0-9, underscore, hyphen.");
                ok = false;
            }

            return ok;
        }

        private static List<string> NormalizeExtensions(List<string> extensions)
        {
            var result = new List<string>();

            if (extensions == null)
                return result;

            for (int i = 0; i < extensions.Count; i++)
            {
                string e = extensions[i] ?? string.Empty;
                e = e.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(e))
                    continue;
                if (!e.StartsWith("."))
                    e = "." + e;
                if (!result.Contains(e))
                    result.Add(e);
            }

            return result;
        }

        private static string ResolveFolder(string watchFolder, List<string> report)
        {
            if (!string.IsNullOrWhiteSpace(watchFolder))
            {
                report.Add($"Watch folder input: {watchFolder}");
                return watchFolder;
            }

            string[] candidates =
            {
                @"D:\Data\Gcode",
                @"C:\ProgramData\Weber\GCode",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (Directory.Exists(candidate))
                {
                    report.Add($"Auto folder: {candidate}");
                    return candidate;
                }
            }

            report.Add("No auto folder found.");
            return string.Empty;
        }

        private static string ResolveFilePath(
            string filePath,
            string folder,
            bool useLatest,
            List<string> allowedExtensions,
            List<string> report)
        {
            if (!useLatest && !string.IsNullOrWhiteSpace(filePath))
                return filePath;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                report.Add("Watch folder missing or not found.");
                return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath;
            }

            string latest = FindLatestFile(folder, allowedExtensions);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                report.Add($"Latest file found: {latest}");
                return latest;
            }

            report.Add("No matching file found in watch folder.");
            return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath;
        }

        private static string FindLatestFile(string folder, List<string> allowedExtensions)
        {
            try
            {
                var di = new DirectoryInfo(folder);
                FileInfo latest = null;

                FileInfo[] files = di.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo f = files[i];
                    string ext = (f.Extension ?? string.Empty).ToLowerInvariant();
                    if (!allowedExtensions.Contains(ext))
                        continue;

                    if (latest == null || f.LastWriteTimeUtc > latest.LastWriteTimeUtc)
                        latest = f;
                }

                return latest?.FullName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksBinary(string path)
        {
            try
            {
                byte[] buffer = new byte[4096];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == 0)
                            return true;
                    }
                }
            }
            catch
            {
                // If byte scan fails, leave decision to text parsing stage.
            }

            return false;
        }

        private static void BuildOutputs(
            IGH_DataAccess DA,
            string checkedFile,
            List<string> fatalIssues,
            List<string> warnings,
            List<string> report)
        {
            bool go = fatalIssues.Count == 0;
            string status;

            if (go && warnings.Count > 0)
                status = "GO WITH WARNING";
            else if (go)
                status = "GO - FILE OK";
            else
                status = "NO GO - FILE CHECK FAILED";

            if (fatalIssues.Count > 0)
            {
                for (int i = 0; i < fatalIssues.Count; i++)
                    report.Add("ERROR: " + fatalIssues[i]);
            }

            if (warnings.Count > 0)
            {
                for (int i = 0; i < warnings.Count; i++)
                    report.Add("WARNING: " + warnings[i]);
            }

            DA.SetData(0, checkedFile ?? string.Empty);
            DA.SetData(1, status);
            DA.SetDataList(2, report);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DXRFileHealthCheckIcon.png");
        public override Guid ComponentGuid => new Guid("39D5A003-E78C-4961-B8BD-48D80E589A5E");
    }
}