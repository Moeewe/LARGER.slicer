using System;
using System.Runtime.InteropServices;
using Grasshopper2.Framework;

[assembly: Guid("6f0c2b7e-51a4-4c8e-9d3f-2a7b9e4d1c60")]

namespace LARGERslicer.GH2
{
    /// <summary>Grasshopper 2 finds the plug-in through this class.</summary>
    public sealed class LargerSlicerPlugin : Plugin
    {
        public LargerSlicerPlugin() { }
        public override string Name => "LARGER.slicer";
        public override string Info => "Continuous single-line toolpaths for large-format 3D printing with robots and pellet extruders, and DXR output.";
        public override string Author => "Moritz Wesseler, FH Münster";
        public override string Website => "https://github.com/Moeewe/LARGER.slicer";
        public override Version Version => new Version(0, 1, 0);
    }

    /// <summary>Rhino loads an .rhp as a Rhino plug-in; this makes it a valid one.</summary>
    public sealed class LargerSlicerRhinoPlugIn : Rhino.PlugIns.PlugIn
    {
        public LargerSlicerRhinoPlugIn() { }
    }
}
