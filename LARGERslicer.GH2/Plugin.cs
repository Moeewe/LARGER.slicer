using System;
using System.Runtime.InteropServices;
using Grasshopper2.Framework;

[assembly: Guid("6f0c2b7e-51a4-4c8e-9d3f-2a7b9e4d1c60")]

namespace LARGERslicer.GH2
{
    /// <summary>Grasshopper 2 finds the plug-in through this class.</summary>
    public sealed class LargerSlicerPlugin : Plugin
    {
        public LargerSlicerPlugin()
        {
            // the preview of Grasshopper 2 in Rhino 8 reads the same CustomPluginInclude.txt, but this build is made for the
            // Grasshopper 2 of Rhino 9 (types it uses do not exist there): refuse cleanly instead of failing later
            if (Rhino.RhinoApp.ExeVersion < 9)
                throw new NotSupportedException("LARGER.slicer for Grasshopper 2 needs Rhino 9. In Rhino 8 use LARGER.slicer in Grasshopper 1.");
            // the line in CustomPluginInclude.txt follows the installed version (see Gh2Include)
            try
            {
                string note = LARGERslicer.Toolpaths.Gh2Include.Ensure();
                if (note != null) Rhino.RhinoApp.WriteLine("LARGER.slicer: " + note);
            }
            catch { }
        }
        public override string Name => "LARGER.slicer";
        public override string Info => "Continuous single-line toolpaths for large-format 3D printing with robots and pellet extruders, and DXR output.";
        public override string Author => "Moritz Wesseler, FH Münster";
        public override string Website => "https://github.com/Moeewe/LARGER.slicer";
        /// <summary>The version of this assembly, the same as the Grasshopper 1 plug-in (set in the csproj by build-package.sh).</summary>
        public override Version Version => typeof(LargerSlicerPlugin).Assembly.GetName().Version;
        /// <summary>The tab icon of Grasshopper 1 when small, the logo (one line climbing in loops) when large.</summary>
        public override Grasshopper2.UI.Icon.IIcon Icon => Io.Icon("LARGERslicer");
    }

    /// <summary>Rhino loads an .rhp as a Rhino plug-in; this makes it a valid one.</summary>
    public sealed class LargerSlicerRhinoPlugIn : Rhino.PlugIns.PlugIn
    {
        public LargerSlicerRhinoPlugIn() { }
    }
}
