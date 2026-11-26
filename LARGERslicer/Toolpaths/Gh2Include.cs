using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Grasshopper 2 (Rhino 9) loads the LARGER.slicer plug-in only from a path listed in its file
    /// CustomPluginInclude.txt; the package manager puts it into packages/9.0/LARGERslicer/&lt;version&gt;/gh2,
    /// a folder whose name changes with every update. Both plug-ins (Grasshopper 1 and 2) call Ensure when
    /// Rhino 9 loads them: the line then always names the version that is installed, and an old line of
    /// LARGER.slicer is replaced. Other lines of the file are left alone. Takes effect at the next start.
    /// </summary>
    public static class Gh2Include
    {
        private const string Rhp = "LARGERslicerGH2.rhp";

        private static string AppSupport()
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library", "Application Support");
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        /// <summary>The Grasshopper 2 plug-in of the installed LARGERslicer package for Rhino 9, or null.</summary>
        public static string InstalledRhp()
        {
            string pkg = Path.Combine(AppSupport(), "McNeel", "Rhinoceros", "packages", "9.0", "LARGERslicer");
            string manifest = Path.Combine(pkg, "manifest.txt");
            if (!File.Exists(manifest)) return null;
            string version = File.ReadAllLines(manifest).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (string.IsNullOrEmpty(version)) return null;
            string rhp = Path.Combine(pkg, version, "gh2", Rhp);
            return File.Exists(rhp) ? rhp : null;
        }

        /// <summary>Writes the line if it is missing or names another version. Returns what was done (null = nothing).</summary>
        public static string Ensure()
        {
            try
            {
                string rhp = InstalledRhp();
                if (rhp == null) return null;
                string dir = Path.Combine(AppSupport(), "Grasshopper2", "Settings");
                string file = Path.Combine(dir, "CustomPluginInclude.txt");
                var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
                // the old lines of this plug-in: any path to LARGERslicerGH2.rhp in a LARGERslicer package
                var keep = lines.Where(l => !(l.Trim().EndsWith(Rhp, StringComparison.OrdinalIgnoreCase)
                                              && l.IndexOf("LARGERslicer", StringComparison.OrdinalIgnoreCase) >= 0
                                              && l.IndexOf("packages", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                // a copy of your own outside the packages (a developer build) wins: then nothing is changed
                if (keep.Any(l => l.Trim().EndsWith(Rhp, StringComparison.OrdinalIgnoreCase) && File.Exists(l.Trim()))) return null;
                bool has = lines.Any(l => string.Equals(l.Trim(), rhp, StringComparison.OrdinalIgnoreCase));
                if (has && keep.Count == lines.Count - 1) return null;
                keep.Add(rhp);
                Directory.CreateDirectory(dir);
                File.WriteAllLines(file, keep.Where(l => l.Trim().Length > 0));
                return "Grasshopper 2 will load LARGER.slicer from " + rhp + " (after the next start of Rhino).";
            }
            catch (Exception ex) { return "Could not set up Grasshopper 2: " + ex.Message; }
        }
    }
}
