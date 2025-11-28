using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace LARGERslicer.Utils
{
    public static class IconHelper
    {
        /// <summary>
        /// Loads an icon from embedded resources
        /// </summary>
        /// <param name="iconName">Name of the icon file (e.g., "MyIcon.png")</param>
        /// <returns>Bitmap or null if not found</returns>
        public static Bitmap Load(string iconName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"LARGERslicer.Resources.{iconName}";
                
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        return new Bitmap(stream);
                    }
                }
            }
            catch
            {
                // Fallback: return a simple colored bitmap
                var fallback = new Bitmap(24, 24);
                using (var g = Graphics.FromImage(fallback))
                {
                    g.Clear(Color.LightBlue);
                    g.DrawString("?", new Font("Arial", 12), Brushes.Black, 8, 4);
                }
                return fallback;
            }
            
            return null;
        }
    }
} 