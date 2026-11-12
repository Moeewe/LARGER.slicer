using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Icons from the embedded resources. Every icon is a white glyph on a small dark chip, so
    /// one file reads on every ground Grasshopper puts it on: light or dark toolbar, the grey
    /// capsule on the canvas, green when selected. There is nothing to choose.
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
