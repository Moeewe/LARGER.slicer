using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Export
{
    public class SafeComponent : GH_Component
    {
        public SafeComponent()
          : base("Safe Component", "Safe",
              "Writes text lines to a file. Combines folder path, filename, and extension. Uses UTF-8 encoding.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Folder Path", "Folder", "Folder path where file should be saved", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name", "Name", "Filename without extension", GH_ParamAccess.item);
            pManager.AddTextParameter("Extension", "Ext", "File extension (e.g., '.txt', '.dxr')", GH_ParamAccess.item, ".txt");
            pManager.AddTextParameter("Lines", "Lines", "List of text lines to write to file", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Write", "Write", "True to write file, False to skip", GH_ParamAccess.item, false);
            
            pManager[1].Optional = true; // File Name is optional
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "Path", "Full path to the created file", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Information about file operation", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string folder = string.Empty;
            string name = string.Empty;
            string ext = ".txt";
            var lines = new List<string>();
            bool write = false;

            if (!DA.GetData(0, ref folder)) return;
            DA.GetData(1, ref name); // Optional - can be empty
            DA.GetData(2, ref ext);
            if (!DA.GetDataList(3, lines)) return;
            DA.GetData(4, ref write);

            var info = new List<string>();
            string filePath = string.Empty;

            // Validate folder path
            if (string.IsNullOrWhiteSpace(folder))
            {
                info.Add("ERROR: Folder path is empty or null");
                info.Add("Please provide a valid folder path (e.g., use Desktop Path component)");
                DA.SetData(0, string.Empty);
                DA.SetDataList(1, info);
                return;
            }

            // Ensure extension starts with dot
            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            // Normalize folder path (remove trailing slashes)
            folder = folder.TrimEnd('/', '\\');
            
            // Normalize name (remove leading slashes to prevent absolute path issues)
            name = name?.TrimStart('/', '\\') ?? string.Empty;
            
            // Check if folder is just root or drive letter (which is problematic)
            if (folder == "/" || folder == "\\" || folder == string.Empty || 
                (folder.Length == 2 && folder[1] == ':'))  // Windows drive letter like C:
            {
                info.Add("ERROR: Cannot write to root directory or drive root");
                info.Add("Please provide a valid folder path:");
                info.Add("  Windows: C:\\Users\\username\\Desktop");
                info.Add("  macOS: /Users/username/Desktop");
                DA.SetData(0, string.Empty);
                DA.SetDataList(1, info);
                return;
            }
            
            // Combine path (always calculate the full path, even if name is empty)
            if (string.IsNullOrWhiteSpace(name))
            {
                // If name is empty, just use folder path
                filePath = folder;
                info.Add("WARNING: File name is empty - only folder path will be used");
            }
            else
            {
                filePath = Path.Combine(folder, name + ext);
            }
            
            // Add debug info
            info.Add($"Input folder: '{folder}'");
            info.Add($"Input name: '{name}'");
            info.Add($"Input extension: '{ext}'");
            info.Add($"Combined path: '{filePath}'");
            
            // Normalize path separators for current OS
            try
            {
                filePath = Path.GetFullPath(filePath);
                info.Add($"Full path: '{filePath}'");
            }
            catch (Exception ex)
            {
                info.Add($"ERROR: Invalid path - {ex.Message}");
                DA.SetData(0, filePath); // Still output the attempted path
                DA.SetDataList(1, info);
                return;
            }
            
            // Always output the file path, even if we can't write yet
            DA.SetData(0, filePath);

            if (!write)
            {
                info.Add("Write is False - file not written");
                info.Add($"Target path: {filePath}");
                info.Add($"Lines ready: {lines.Count}");
                DA.SetDataList(1, info);
                return;
            }

            try
            {
                // Normalize path separators for current OS
                filePath = Path.GetFullPath(filePath);

                info.Add($"Attempting to write to: {filePath}");
                info.Add($"Directory exists: {Directory.Exists(folder)}");
                info.Add($"File will be created: {!File.Exists(filePath)}");

                // Ensure directory exists
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    info.Add($"Created directory: {folder}");
                }

                // Check if we can write to the directory
                if (!Directory.Exists(folder))
                {
                    throw new DirectoryNotFoundException($"Directory does not exist and could not be created: {folder}");
                }

                // Write file with UTF-8 encoding
                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    foreach (string line in lines)
                    {
                        writer.WriteLine(line);
                    }
                }

                // Verify file was written
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    info.Add($"File written successfully: {filePath}");
                    info.Add($"Lines written: {lines.Count}");
                    info.Add($"File size: {fileInfo.Length} bytes");
                }
                else
                {
                    throw new IOException("File was not created - write operation may have failed silently");
                }
            }
            catch (Exception ex)
            {
                info.Add($"ERROR: Failed to write file");
                info.Add($"Error message: {ex.Message}");
                info.Add($"Error type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    info.Add($"Inner exception: {ex.InnerException.Message}");
                }
            }

            // File path is already set above, just update info
            DA.SetDataList(1, info);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SafeIcon.png");
        public override Guid ComponentGuid => new Guid("C4D5E6F7-A8B9-0123-DEF0-234567890123");
    }
}
