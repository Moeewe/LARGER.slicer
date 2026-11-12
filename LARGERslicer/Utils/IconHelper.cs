using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Icons from the embedded resources, drawn the way Grasshopper draws its own: light bodies and
    /// coloured accents with a dark edge and a faint shadow, no ground of their own. The light
    /// body carries on a dark toolbar, the dark edge on a light one and on the grey and green
    /// capsules of the canvas, so one file reads everywhere. Made by scripts/generate_icons.py.
    /// </summary>
    public static class IconHelper
    {
        public static Bitmap Load(string iconName) => Load(iconName, null);

        public static Bitmap Load(string iconName, IGH_DocumentObject owner)
        {
            try { return Read(iconName); }
            catch { return null; } // Grasshopper then shows its default icon
        }

        private static Bitmap Read(string iconName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LARGERslicer.Resources." + iconName))
                return stream != null ? new Bitmap(stream) : null;
        }
    }
}
