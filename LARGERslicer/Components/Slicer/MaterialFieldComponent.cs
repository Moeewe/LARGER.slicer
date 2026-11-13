using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// Toolpath back onto the part: the print direction as a material field for an analysis
    /// (IGA or FE) on the NURBS geometry. See MaterialField in the core.
    /// </summary>
    public class MaterialFieldComponent : SlicerComponent
    {
        public MaterialFieldComponent()
            : base("Material Field", "MatField",
                  "Puts the toolpath back onto the part as a material field for analysis (IGA or FE). For every sample point: the bead direction (e1), the direction across the bead (e2) and the stacking direction (e3), the layer and bead, how far the next layer or bead interface is and how long the layer below had to cool. With the moduli it gives the rotated stiffness per point. Samples are a grid in the UV of every Brep face, so (face, u, v) maps straight onto the NURBS of the analysis; or give your own points (e.g. Gauss points). The default moduli are placeholders: measure your material.") { }

        public override Guid ComponentGuid => new Guid("E7A3C1D9-5B2F-4E86-9A0C-3F1D8B6E2A47");
        public override GH_Exposure Exposure => GH_Exposure.septenary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMaterialFieldIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The finished toolpath", GH_ParamAccess.item);
            p.AddBrepParameter("Geometry", "G", "Optional. The part as Breps (NURBS). Samples are a grid in each face's UV. Empty = samples inside the beads", GH_ParamAccess.list);
            p.AddPointParameter("Sample Points", "Pts", "Optional. Your own points, e.g. the Gauss points of the analysis. They replace the grid; with Geometry each one gets its face, u and v", GH_ParamAccess.list);
            p.AddNumberParameter("Sample Spacing", "D", "Distance of the samples in mm. 0 = half the layer height, so every layer interface shows", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("E Bead", "E1", "Young's modulus along the bead in MPa. Placeholder: measure it", GH_ParamAccess.item, 3000.0);
            p.AddNumberParameter("E Across", "E2", "Young's modulus across the beads, in the layer, in MPa. Placeholder: measure it", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("E Layers", "E3", "Young's modulus across the layers in MPa. Placeholder: measure it", GH_ParamAccess.item, 1800.0);
            p.AddNumberParameter("G In Layer", "G12", "Shear modulus in the layer in MPa. Placeholder", GH_ParamAccess.item, 1100.0);
            p.AddNumberParameter("G Across Layers", "G13", "Shear modulus across the layers (G13 = G23) in MPa. Placeholder", GH_ParamAccess.item, 700.0);
            p.AddNumberParameter("Poisson In Layer", "nu12", "Poisson ratio in the layer. Placeholder", GH_ParamAccess.item, 0.35);
            p.AddNumberParameter("Poisson Across Layers", "nu13", "Poisson ratio across the layers (nu13 = nu23). Placeholder", GH_ParamAccess.item, 0.30);
            p.AddNumberParameter("Cooling Time", "tc", "Seconds after which a layer counts as cold for the next one. Longer waits make the interface weaker. Placeholder: from tests", GH_ParamAccess.item, 60.0);
            p.AddTextParameter("File", "File", "Optional. File or folder. Empty = the Desktop, named MAT_date_Material Field", GH_ParamAccess.item);
            p.AddIntegerParameter("Format", "Fmt", "0 = CSV, JSON and VTK\n1 = CSV\n2 = JSON\n3 = VTK (ParaView)", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Save", "Save", "True = write the files", GH_ParamAccess.item, false);
            p[1].Optional = true;
            p[2].Optional = true;
            p[12].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 3, "Sample Spacing", 0, 50, 0, 1);
            AddValueList(doc, 13, "Format", MaterialField.Formats);
            AddButton(doc, 14, "Save");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Sample points", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "F", "Local frame per point: X = along the bead (e1), Y = across the bead (e2), Z = stacking direction (e3)", GH_ParamAccess.list);
            p.AddLineParameter("Directions", "Dir", "Lines along the bead (e1)", GH_ParamAccess.list);
            p.AddLineParameter("Stacking", "Stk", "Lines in the stacking direction (e3)", GH_ParamAccess.list);
            p.AddNumberParameter("Weakness", "W", "0 = sound, 1 = right on a layer interface after a long wait. Closeness to the interface x (1 - exp(-interface time / cooling time))", GH_ParamAccess.list);
            p.AddMeshParameter("Weakness Mesh", "M", "The faces coloured by weakness: blue sound, red weak (scaled to the largest value)", GH_ParamAccess.item);
            p.AddTextParameter("Table", "Tab", "The CSV table: header and one line per point", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Summary and remarks", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            var breps = new List<Brep>(); da.GetDataList(1, breps);
            var pts = new List<Point3d>(); da.GetDataList(2, pts);
            double spacing = 0, tc = 60;
            var m = new OrthotropicMaterial();
            double g13 = m.G13, nu13 = m.Nu13;
            da.GetData(3, ref spacing);
            da.GetData(4, ref m.E1); da.GetData(5, ref m.E2); da.GetData(6, ref m.E3);
            da.GetData(7, ref m.G12); da.GetData(8, ref g13); da.GetData(9, ref m.Nu12); da.GetData(10, ref nu13);
            m.G13 = m.G23 = g13; m.Nu13 = m.Nu23 = nu13;
            da.GetData(11, ref tc);
            string file = null; int format = 0; bool save = false;
            da.GetData(12, ref file); da.GetData(13, ref format); da.GetData(14, ref save);

            var r = MaterialField.Compute(tp, breps.Where(b => b != null).ToList(), pts, new MaterialFieldOptions { Spacing = spacing, Material = m, CoolingTime = tc });
            foreach (var l in r.Log) AddRuntimeMessage(l.StartsWith("These Poisson") || l.StartsWith("All moduli") ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, l);
            if (r.Samples.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No samples."); return; }

            if (save)
            {
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + string.Join(", ", MaterialField.Write(r, file, Math.Max(0, Math.Min(3, format))))); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the files: " + ex.Message); }
            }

            double len = Math.Max(1.0, 0.8 * (r.Spacing > 0 ? Math.Max(r.Spacing, tp.Settings.LayerHeight) : tp.Settings.BeadWidth));
            var ok = r.Samples.Where(s => s.Layer >= 0).ToList();
            Message = r.Samples.Count + " points";
            da.SetDataList(0, r.Samples.Select(s => s.P));
            da.SetDataList(1, ok.Select(s => Geo.PlaneFromAxes(s.P, s.E1, s.E2)));
            da.SetDataList(2, ok.Select(s => new Line(s.P, s.E1 * len)));
            da.SetDataList(3, ok.Select(s => new Line(s.P, s.E3 * len)));
            da.SetDataList(4, r.Samples.Select(s => s.Weakness));
            if (r.Mesh != null) da.SetData(5, r.Mesh);
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Weakness Mesh needs Geometry and no Sample Points.");
            da.SetDataList(6, MaterialField.Csv(r));
            da.SetData(7, r.Info);
        }
    }
}
