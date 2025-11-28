using System;
using System.IO;
using Grasshopper.Kernel;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class DesktopPathComponent : GH_Component
    {
        public DesktopPathComponent()
          : base("Desktop Path", "Desktop",
              "Finds the current user's Desktop folder path cross-platform (Windows/Mac). Supports multiple languages.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // No inputs required
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Desktop Path", "Path", "Full path to the Desktop folder", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Information about detected Desktop folder", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string desktopPath = GetDesktopPath();
            var info = new System.Collections.Generic.List<string>
            {
                $"Desktop path detected: {desktopPath}",
                $"Path exists: {Directory.Exists(desktopPath)}"
            };

            DA.SetData(0, desktopPath);
            DA.SetDataList(1, info);
        }

        private string GetDesktopPath()
        {
            // Get home directory
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Common desktop folder names in various languages
            string[] candidates = { "Desktop", "Schreibtisch", "Escritorio", "Bureau", "Рабочий стол" };

            foreach (string name in candidates)
            {
                string desk = Path.Combine(home, name);
                if (Directory.Exists(desk))
                {
                    return desk;
                }
            }

            // Fallback: return home directory
            return home;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DesktopPathIcon.png");
        public override Guid ComponentGuid => new Guid("A2B3C4D5-E6F7-8901-BCDE-F12345678901");
    }
}


