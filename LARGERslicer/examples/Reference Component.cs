using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using IGAToolkit.Utils;
using IGAToolkit.Types;

namespace IGAToolkit.Components.Creators
{
    public class LoadCreatorComponent : GH_Component
    {
        public LoadCreatorComponent()
          : base("Load Creator", "Load",
              "Creates load objects for structural analysis (surface loads, direction as int or vector).",
              "IGA", "Creators")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("parts", "parts", "List of Part objects", GH_ParamAccess.list);
            pManager.AddIntegerParameter("load_id", "load_id", "Load identifier", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("part_id", "part_id", "1-based index in parts list", GH_ParamAccess.item, 1);
            pManager.AddTextParameter("load_type", "load_type", "Load type (e.g. 'load_vec_surf')", GH_ParamAccess.item, "load_vec_surf");
            pManager.AddNumberParameter("fload", "fload", "Load magnitude", GH_ParamAccess.item, 1.0);
            pManager.AddGenericParameter("direction", "direction", "Direction as int (1-6) or vector [x,y,z]", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("load", "load", "Load object for MATLAB Interpreter", GH_ParamAccess.item);
            pManager.AddTextParameter("load_info", "load_info", "Status and debug information", GH_ParamAccess.list);
            pManager.AddGenericParameter("preview_geometry", "preview_geometry", "Geometry for visualization", GH_ParamAccess.list);
            pManager.AddTextParameter("preview_labels", "preview_labels", "Labels for geometry preview", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var parts = new List<Part>();
            int loadId = 1;
            int partId = 1;
            string loadType = "load_vec_surf";
            double fload = 1.0;
            object directionInput = null;

            if (!DA.GetDataList(0, parts)) return;
            DA.GetData(1, ref loadId);
            DA.GetData(2, ref partId);
            DA.GetData(3, ref loadType);
            DA.GetData(4, ref fload);
            DA.GetData(5, ref directionInput);

            var loadInfo = new List<string>();
            var previewGeometry = new List<object>();
            var previewLabels = new List<string>();
            Load load = null;

            try
            {
                if (partId < 1 || partId > parts.Count)
                    throw new ArgumentException($"part_id {partId} is out of range (1..{parts.Count})");

                var part = parts[partId - 1];

                // Direction: int (1-6) oder Vektor [x,y,z]
                int dirNum = LoadProcessor.ProcessLoadDirection(directionInput);

                // Nur surface loads werden unterstützt
                if (loadType != "load_vec_surf" && loadType != "0")
                {
                    loadInfo.Add("ERROR: Only 'load_vec_surf' (surface loads) are currently supported.");
                    DA.SetData(0, null);
                    DA.SetDataList(1, loadInfo);
                    DA.SetDataList(2, previewGeometry);
                    DA.SetDataList(3, previewLabels);
                    return;
                }

                load = new Load(loadId, partId, loadType, fload, dirNum);

                // Vorschau: Surface des Parts
                if (part.rhino_brep != null)
                {
                    previewGeometry.Add(part.rhino_brep);
                    previewLabels.Add($"P{partId}S");
                    loadInfo.Add($"Surface load preview: Part {partId} (complete surface/patch)");
                }
                else
                {
                    loadInfo.Add("Warning: Could not extract surface for preview.");
                }

                loadInfo.Add($"Load created successfully with ID: {load.load_id}");
                loadInfo.Add($"  Load type: {load.load_type}");
                loadInfo.Add($"  Direction: {dirNum} (+x=1, -x=2, +y=3, -y=4, +z=5, -z=6)");
                loadInfo.Add($"  Magnitude: {load.fload}");
            }
            catch (Exception ex)
            {
                loadInfo.Add($"Error creating load: {ex.Message}");
                load = null;
            }

            DA.SetData(0, load);
            DA.SetDataList(1, loadInfo);
            DA.SetDataList(2, previewGeometry);
            DA.SetDataList(3, previewLabels);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("LoadCreatorIcon.png");
        public override Guid ComponentGuid => new Guid("C1E5075C-53F6-4B88-B5C4-3BD9A08DAD69");
    }
}