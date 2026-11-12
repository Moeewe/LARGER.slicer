using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;

namespace LARGERslicer.Utils
{
    /// <summary>
    /// Icons from the embedded resources. Every icon exists twice: "X.png" with dark lines and
    /// "X.dark.png" with light lines. Grasshopper 1 knows only one icon per component and shows
    /// it in two places: in the toolbar (dark when Rhino runs in dark mode) and on the component
    /// on the canvas (always light). So the choice is made here:
    ///  - the instance Grasshopper keeps for the toolbar is in no document: it gets the light
    ///    lines if Rhino runs in dark mode;
    ///  - an instance on the canvas is in a document and gets the dark lines.
    /// The toolbar keeps what it got at start; after switching the appearance, restart Rhino.
    /// </summary>
    public static class IconHelper
    {
        public static Bitmap Load(string iconName) => Load(iconName, null);

        public static Bitmap Load(string iconName, IGH_DocumentObject owner)
        {
            try
            {
                bool toolbar = owner != null && ForToolbar(owner);
                if (toolbar) ToolbarIcons++;
                if (toolbar && DarkMode)
                {
                    var dark = Read(iconName.Replace(".png", ".dark.png"));
                    if (dark != null) return dark;
                }
                return Read(iconName);
            }
            catch
            {
                return null; // Grasshopper then shows its default icon
            }
        }

        private static Bitmap Read(string iconName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LARGERslicer.Resources." + iconName))
                return stream != null ? new Bitmap(stream) : null;
        }

        /// <summary>
        /// True for an instance that is in no document: that is the one Grasshopper keeps for
        /// the toolbar. A component on the canvas is asked for its icon when it is drawn, and by
        /// then it is part of its document.
        /// </summary>
        private static bool ForToolbar(IGH_DocumentObject owner)
        {
            try { return owner.OnPingDocument() == null; }
            catch { return false; }
        }

        /// <summary>How many icons went to the toolbar (for checking that the choice works).</summary>
        public static int ToolbarIcons;

        private static bool? dark;
        /// <summary>Rhino 8 and later on the Mac draw the Grasshopper toolbar dark in dark mode. Asked by name: Rhino 7 does not have the property.</summary>
        public static bool DarkMode
        {
            get
            {
                if (dark.HasValue) return dark.Value;
                bool d = false;
                try
                {
                    var host = typeof(Rhino.Runtime.HostUtils);
                    var p = host.GetProperty("RunningInDarkMode", BindingFlags.Public | BindingFlags.Static);
                    if (p != null && Rhino.Runtime.HostUtils.RunningOnOSX) d = (bool)p.GetValue(null, null);
                }
                catch { d = false; }
                dark = d;
                return d;
            }
        }
    }
}
