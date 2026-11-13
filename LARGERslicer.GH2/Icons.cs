using System.Collections.Generic;
using System.IO;
using Grasshopper2.UI.Icon;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// The icons of the Grasshopper 1 plug-in, for the same components here. The 24 px images are the
    /// files of Grasshopper 1 (LARGERslicer/Resources), the 48 px ones are drawn by the same script
    /// at twice the size (scripts/generate_icons.py with ICON_SIZE=48), so they stay sharp on the
    /// larger and high-resolution icons Grasshopper 2 draws. Both are embedded in this assembly.
    /// </summary>
    internal static partial class Io
    {
        private static readonly Dictionary<string, IIcon> IconCache = new Dictionary<string, IIcon>();
        private static readonly string[] IconSizes = { "24", "48", "256" };

        /// <summary>
        /// The option rows of Grasshopper 1 (a choice drawn on the component) as presets of the input:
        /// item i has the value i, as in the drop-down list of Grasshopper 1.
        /// </summary>
        public static Grasshopper2.Parameters.Parameter<int> Choices(this Grasshopper2.Parameters.Parameter<int> p, params string[] names)
        {
            for (int i = 0; i < names.Length; i++) p.Presets?.Add(names[i], "", i);
            return p;
        }

        /// <summary>The round values of a Grasshopper 1 slider row as presets of a number input.</summary>
        public static Grasshopper2.Parameters.Parameter<double> Values(this Grasshopper2.Parameters.Parameter<double> p, params double[] values)
        {
            foreach (var v in values) p.Presets?.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture), "", v);
            return p;
        }

        /// <summary>The icon of that name (file name without .png), or null if there is none.</summary>
        public static IIcon Icon(string name)
        {
            lock (IconCache)
            {
                if (IconCache.TryGetValue(name, out var cached)) return cached;
                IIcon icon = null;
                try
                {
                    var asm = typeof(Io).Assembly;
                    var images = new List<Eto.Drawing.Bitmap>();
                    foreach (var size in IconSizes)
                    {
                        using (var s = asm.GetManifestResourceStream($"LARGERslicer.GH2.Icons.{size}.{name}.png"))
                        {
                            if (s == null) continue;
                            var bytes = new MemoryStream(); s.CopyTo(bytes); bytes.Position = 0;
                            images.Add(new Eto.Drawing.Bitmap(bytes));
                        }
                    }
                    if (images.Count > 0) icon = new PixelIcon(images.ToArray());
                }
                catch { icon = null; }   // never let an icon take the component down: Grasshopper draws its default
                IconCache[name] = icon;
                return icon;
            }
        }
    }
}
