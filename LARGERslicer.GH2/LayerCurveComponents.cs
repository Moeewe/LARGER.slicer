using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Data;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== layers to curves
    [IoId("d420268d-285e-426a-b58c-fd4b84193384")]
    public sealed class LayersToCurvesComponent : Component
    {
        public LayersToCurvesComponent() : base(Io.Name("Layers to Curves", "Opens layers up as plain curves, one branch per layer. Use it to try your own pattern in a script or with standard Grasshopper components, then turn the result back into layers with Curves to Layers.", Io.Slicer, Group.Curves)) { }
        public LayersToCurvesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerOpenIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Layers", "L", "Layers from any slicer component.");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Curves", "C", "Contours, one branch per layer.", Access.Tree);
            outputs.AddPlane("Planes", "Pl", "Layer plane at nozzle height, one per layer.", Access.Twig);
            outputs.AddNumber("Heights", "H", "Layer height, one per layer.", Access.Twig);
            outputs.AddText("Roles", "R", "Role of every curve (Wall, Bottom, Infill ...), same tree as the curves.", Access.Tree);
        }

        protected override void Process(IDataAccess access)
        {
            var layers = Io.Layers(access, 0); if (layers == null) return;
            var curves = new Curve[layers.Count][];
            var roles = new string[layers.Count][];
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                curves[i] = l.Contours.Select(c => (Curve)new PolylineCurve(c)).ToArray();
                roles[i] = Enumerable.Range(0, l.Contours.Count).Select(c => (l.ContourRoles != null && c < l.ContourRoles.Count ? l.ContourRoles[c] : PathRole.Wall).ToString()).ToArray();
            }
            access.SetTree(0, Garden.TreeFromArrays(curves, null, null));
            access.SetTwig(1, layers.Select(l => l.Plane).ToArray(), null, null);
            access.SetTwig(2, layers.Select(l => l.Height).ToArray(), null, null);
            access.SetTree(3, Garden.TreeFromArrays(roles, null, null));
        }
    }

    // ============================================================== curves to layers
    [IoId("dab8661a-b79a-4501-abb8-babfa2a4c559")]
    public sealed class CurvesToLayersComponent : Component
    {
        public CurvesToLayersComponent() : base(Io.Name("Curves to Layers", "Turns your own curves into layers, one branch per layer. Everything after it (Continuous Path, Round Corners, Extrusion ...) then works on your pattern. Closed curves are printed as loops, open curves back and forth.", Io.Slicer, Group.Curves)) { }
        public CurvesToLayersComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerCurvesToLayersIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Curves, one branch per layer, bottom layer first.", Access.Tree);
            inputs.AddPlane("Planes", "Pl", "Layer plane at nozzle height, one per layer. With a single plane the layers are stacked along its normal.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Settings", "S", "Print Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("Roles", "R", "Optional role per curve (Wall, Bottom, Infill, Support), same tree as the curves.", Access.Tree, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Layers", "L", "Layers for the path components.");
            outputs.AddText("Info", "I", "How many layers.");
        }

        protected override void Process(IDataAccess access)
        {
            if (!access.GetTree(0, out Tree<Curve> tree) || tree == null || tree.PathCount == 0) return;
            access.GetItemArray(1, out Plane[] planeArr);
            var planes = planeArr ?? new Plane[0];
            var settings = Io.Settings(access, 2);
            access.GetTree(3, out Tree<string> roleTree);
            var roleTwigs = roleTree?.AllTwigs?.ToList();

            var layers = new List<SliceLayer>();
            var twigs = tree.AllTwigs.ToList();
            for (int b = 0; b < twigs.Count; b++)
            {
                var branch = twigs[b].Items.ToList();
                var roles = roleTwigs != null && b < roleTwigs.Count ? roleTwigs[b].Items.ToList() : null;
                var layer = new SliceLayer { Index = b, Height = settings.LayerHeight, ContourRoles = new List<PathRole>() };
                Point3d firstPoint = Point3d.Unset;
                for (int c = 0; c < branch.Count; c++)
                {
                    Curve crv = branch[c];
                    if (crv == null) continue;
                    if (!crv.TryGetPolyline(out Polyline pl))
                    {
                        var pc = crv.ToPolyline(0.05, 0.05, Math.Max(0.5, settings.PointSpacing), 0);
                        if (pc == null) continue;
                        pl = pc.ToPolyline();
                    }
                    if (pl.Count < 2) continue;
                    if (!firstPoint.IsValid) firstPoint = pl[0];
                    layer.Contours.Add(pl);
                    PathRole role = PathRole.Wall;
                    if (roles != null && c < roles.Count && roles[c] != null) Enum.TryParse(roles[c], true, out role);
                    layer.ContourRoles.Add(role);
                }
                if (layer.Contours.Count == 0) continue;
                if (planes.Length > b) layer.Plane = planes[b];
                else if (planes.Length == 1)
                {
                    // same orientation as the given plane, moved along its normal to this layer's curves
                    Plane basePlane = planes[0];
                    layer.Plane = new Plane(basePlane.Origin + basePlane.Normal * basePlane.DistanceTo(firstPoint), basePlane.XAxis, basePlane.YAxis);
                }
                else layer.Plane = new Plane(new Point3d(0, 0, firstPoint.Z), Vector3d.XAxis, Vector3d.YAxis);
                layers.Add(layer);
            }
            access.SetItem(0, new LayerStack(layers));
            access.SetItem(1, layers.Count + " layers");
        }
    }
}
