using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    public class GH_PrintSettings : GH_Goo<PrintSettings>
    {
        public GH_PrintSettings() { }
        public GH_PrintSettings(PrintSettings v) { Value = v; }
        public override bool IsValid => Value != null;
        public override string TypeName => "Print Settings";
        public override string TypeDescription => "Bead, layer and speed settings for the LARGER slicer";
        public override IGH_Goo Duplicate() => new GH_PrintSettings(Value?.Duplicate());
        public override string ToString() => Value?.ToString() ?? "Null Print Settings";
    }

    public class GH_MachineProfile : GH_Goo<MachineProfile>
    {
        public GH_MachineProfile() { }
        public GH_MachineProfile(MachineProfile v) { Value = v; }
        public override bool IsValid => Value != null;
        public override string TypeName => "Machine Profile";
        public override string TypeDescription => "How a printer converts the path into extrusion values";
        public override IGH_Goo Duplicate() => new GH_MachineProfile(Value?.Duplicate());
        public override string ToString() => Value?.ToString() ?? "Null Machine Profile";
    }

    public class GH_SliceLayer : GH_Goo<SliceLayer>, IGH_PreviewData
    {
        public GH_SliceLayer() { }
        public GH_SliceLayer(SliceLayer v) { Value = v; }
        public override bool IsValid => Value != null;
        public override string TypeName => "Slice Layer";
        public override string TypeDescription => "Contours of one layer";
        public override IGH_Goo Duplicate() => new GH_SliceLayer(Value);
        public override string ToString() => Value?.ToString() ?? "Null Layer";

        public BoundingBox ClippingBox
        {
            get
            {
                var bb = BoundingBox.Empty;
                if (Value != null) foreach (var c in Value.RealContours()) bb.Union(c.BoundingBox);
                return bb;
            }
        }
        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (Value == null) return;
            foreach (var c in Value.RealContours()) args.Pipeline.DrawPolyline(c, args.Color, args.Thickness);
        }
        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }
    }

    public class GH_Toolpath : GH_Goo<Toolpath>, IGH_PreviewData
    {
        private BoundingBox _box = BoundingBox.Unset;

        public GH_Toolpath() { }
        public GH_Toolpath(Toolpath v) { Value = v; }
        public override bool IsValid => Value != null && Value.Count > 1;
        public override string TypeName => "Toolpath";
        public override string TypeDescription => "Print path with tool axis, layer height, width and flow per point";
        public override IGH_Goo Duplicate() => new GH_Toolpath(Value);
        public override string ToString() => Value?.ToString() ?? "Null Toolpath";

        public override bool CastTo<Q>(ref Q target)
        {
            if (Value != null && typeof(Q).IsAssignableFrom(typeof(GH_Curve)))
            {
                target = (Q)(object)new GH_Curve(new PolylineCurve(Value.Points));
                return true;
            }
            return base.CastTo(ref target);
        }

        public BoundingBox ClippingBox
        {
            get
            {
                if (!_box.IsValid && Value != null) _box = new BoundingBox(Value.Points);
                return _box;
            }
        }
        /// <summary>Colour per role, so wall, infill, bottom, seam and travel can be told apart at a glance.</summary>
        public static Color RoleColor(PathRole role)
        {
            switch (role)
            {
                case PathRole.Infill: return Color.FromArgb(240, 140, 0);
                case PathRole.Bottom: return Color.FromArgb(0, 160, 150);
                case PathRole.Seam: return Color.FromArgb(200, 0, 160);
                case PathRole.Travel: return Color.FromArgb(150, 150, 150);
                case PathRole.Skirt: return Color.FromArgb(120, 180, 60);
                case PathRole.Support: return Color.FromArgb(130, 90, 200);
                default: return Color.FromArgb(0, 90, 170);
            }
        }

        private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<PathRole, Point3d[]>> _runs;

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (Value == null || Value.Count < 2) return;
            if (_runs == null)
            {
                // consecutive points with the same role are drawn as one polyline
                _runs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<PathRole, Point3d[]>>();
                int start = 1;
                for (int i = 2; i <= Value.Count; i++)
                {
                    if (i < Value.Count && Value.Roles[i] == Value.Roles[start]) continue;
                    var pts = new Point3d[i - start + 1];
                    for (int k = 0; k < pts.Length; k++) pts[k] = Value.Points[start - 1 + k];
                    _runs.Add(new System.Collections.Generic.KeyValuePair<PathRole, Point3d[]>(Value.Roles[start], pts));
                    start = i;
                }
            }
            foreach (var run in _runs)
                args.Pipeline.DrawPolyline(run.Value, RoleColor(run.Key), run.Key == PathRole.Travel ? 1 : args.Thickness + 1);
        }
        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }
    }
}
