using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== material field (toolpath back into IGA)
    [IoId("4c9e2b71-8a3d-4f56-b0e1-7d2a9c5f3e18")]
    public sealed class MaterialFieldComponent : Component
    {
        public MaterialFieldComponent() : base(Io.Name("Material Field", "Puts the toolpath back onto the part as a material field for analysis (IGA or FE): per sample point the bead direction (e1), across the bead (e2), the stacking direction (e3), layer, bead, distance to the next layer and bead interface, and how long the layer below had to cool. With the moduli: the rotated stiffness per point. Samples are a grid in each Brep face's UV, so (face, u, v) maps onto the NURBS of the analysis; or give your own points (e.g. Gauss points). The default moduli are placeholders: measure your material.", Io.Slicer, Group.Curves)) { }
        public MaterialFieldComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerMaterialFieldIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
            inputs.AddGeneric("Geometry", "G", "Optional. The part as Breps (NURBS). Samples are a grid in each face's UV. Empty = samples inside the beads.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddPoint("Sample Points", "Pts", "Optional. Your own points, e.g. the Gauss points of the analysis. They replace the grid.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Sample Spacing", "D", "Distance of the samples in mm. 0 = half the layer height, so every layer interface shows.").Set(0.0);
            inputs.AddNumber("E Bead", "E1", "Young's modulus along the bead in MPa. Placeholder: measure it.").Set(3000.0);
            inputs.AddNumber("E Across", "E2", "Young's modulus across the beads, in the layer, in MPa. Placeholder.").Set(2400.0);
            inputs.AddNumber("E Layers", "E3", "Young's modulus across the layers in MPa. Placeholder.").Set(1800.0);
            inputs.AddNumber("G In Layer", "G12", "Shear modulus in the layer in MPa. Placeholder.").Set(1100.0);
            inputs.AddNumber("G Across Layers", "G13", "Shear modulus across the layers (G13 = G23) in MPa. Placeholder.").Set(700.0);
            inputs.AddNumber("Poisson In Layer", "nu12", "Poisson ratio in the layer. Placeholder.").Set(0.35);
            inputs.AddNumber("Poisson Across Layers", "nu13", "Poisson ratio across the layers (nu13 = nu23). Placeholder.").Set(0.30);
            inputs.AddNumber("Cooling Time", "tc", "Seconds after which a layer counts as cold for the next one. Placeholder: from tests.").Set(60.0);
            inputs.AddText("File", "File", "Optional: file or folder. Empty = the Desktop.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Format", "Fmt", string.Join("\n", MaterialField.Formats.Select((x, i) => i + " = " + x))).Set(0);
            inputs.AddBoolean("Save", "Save", "True writes the files.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddPoint("Points", "P", "Sample points.", Access.Twig);
            outputs.AddPlane("Frames", "F", "Local frame: X along the bead (e1), Y across (e2), Z stacking (e3).", Access.Twig);
            outputs.AddLine("Directions", "Dir", "Lines along the bead (e1).", Access.Twig);
            outputs.AddLine("Stacking", "Stk", "Lines in the stacking direction (e3).", Access.Twig);
            outputs.AddNumber("Weakness", "W", "0 = sound, 1 = on a layer interface after a long wait.", Access.Twig);
            outputs.AddMesh("Weakness Mesh", "M", "The faces coloured by weakness: blue sound, red weak.");
            outputs.AddText("Table", "Tab", "The CSV table.", Access.Twig);
            outputs.AddText("Info", "I", "Summary.");
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            object[] geo; access.GetItemArray(1, out geo);
            Point3d[] pts; access.GetItemArray(2, out pts);
            var breps = new List<Brep>();
            if (geo != null)
                foreach (var g in geo)
                {
                    object o = Io.Solid(g);
                    if (o is Brep b) breps.Add(b);
                    else if (o is Extrusion ex) breps.Add(ex.ToBrep());
                    else if (o is Surface srf) breps.Add(srf.ToBrep());
                }
            double spacing, e1, e2, e3, g12, g13, nu12, nu13, tc; string file; int format; bool save;
            access.GetItem(3, out spacing); access.GetItem(4, out e1); access.GetItem(5, out e2); access.GetItem(6, out e3);
            access.GetItem(7, out g12); access.GetItem(8, out g13); access.GetItem(9, out nu12); access.GetItem(10, out nu13);
            access.GetItem(11, out tc); access.GetItem(12, out file); access.GetItem(13, out format); access.GetItem(14, out save);
            var m = new OrthotropicMaterial { E1 = e1, E2 = e2, E3 = e3, G12 = g12, G13 = g13, G23 = g13, Nu12 = nu12, Nu13 = nu13, Nu23 = nu13 };

            var r = MaterialField.Compute(tp, breps, pts != null ? pts.ToList() : null, new MaterialFieldOptions { Spacing = spacing, Material = m, CoolingTime = tc });
            Io.Remarks(access, r.Log);
            if (r.Samples.Count == 0) { access.AddWarning("No samples", "Nothing to sample."); return; }
            if (save)
            {
                try { access.AddRemark("Saved", string.Join(", ", MaterialField.Write(r, file, Math.Max(0, Math.Min(3, format))))); }
                catch (Exception ex) { access.AddError("Could not save", ex.Message); }
            }

            double len = Math.Max(1.0, 0.8 * Math.Max(r.Spacing, tp.Settings.LayerHeight));
            var ok = r.Samples.Where(s => s.Layer >= 0).ToList();
            access.SetTwig(0, r.Samples.Select(s => s.P).ToArray(), null, null);
            access.SetTwig(1, ok.Select(s => Geo.PlaneFromAxes(s.P, s.E1, s.E2)).ToArray(), null, null);
            access.SetTwig(2, ok.Select(s => new Line(s.P, s.E1 * len)).ToArray(), null, null);
            access.SetTwig(3, ok.Select(s => new Line(s.P, s.E3 * len)).ToArray(), null, null);
            access.SetTwig(4, r.Samples.Select(s => s.Weakness).ToArray(), null, null);
            if (r.Mesh != null) access.SetItem(5, r.Mesh);
            access.SetTwig(6, MaterialField.Csv(r).ToArray(), null, null);
            access.SetItem(7, r.Info);
        }
    }
}
