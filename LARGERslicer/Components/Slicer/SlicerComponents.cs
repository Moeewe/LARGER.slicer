using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Slicer
{
    internal static class SlicerIO
    {
        public const string Category = "LARGER";
        public const string SubCategory = "Slicer";

        public static T Get<T>(IGH_DataAccess da, int index) where T : class
        {
            IGH_Goo goo = null;
            if (!da.GetData(index, ref goo) || goo == null) return null;
            if (goo is GH_Goo<T> typed) return typed.Value;
            return goo.ScriptVariable() as T;
        }
    }

    /// <summary>
    /// Base for all slicer components. A freshly placed component brings its own drop-down
    /// lists and sliders, so nobody has to remember what "Mode 1" means.
    /// </summary>
    public abstract class SlicerComponent : GH_Component
    {
        private bool _autoInputsDone;

        protected SlicerComponent(string name, string nick, string description)
            : base(name, nick, description, SlicerIO.Category, SlicerIO.SubCategory) { }

        /// <summary>Override to add value lists, sliders or toggles when the component is first placed.</summary>
        protected virtual void CreateAutoInputs(GH_Document doc) { }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (_autoInputsDone) return;
            _autoInputsDone = true;
            foreach (var p in Params.Input) if (p.SourceCount > 0) return;
            try
            {
                Attributes.ExpireLayout();
                Attributes.PerformLayout();
                CreateAutoInputs(document);
            }
            catch { /* helpers are a convenience; never block placing the component */ }
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            _autoInputsDone = true; // loaded or pasted: keep what the file says
            return base.Read(reader);
        }

        private System.Drawing.PointF Beside(int input, float dx)
        {
            return new System.Drawing.PointF(Attributes.Bounds.Left - dx, Params.Input[input].Attributes.Pivot.Y - 10f);
        }

        protected void AddValueList(GH_Document doc, int input, string name, params string[] items)
        {
            AddValueListAt(doc, input, name, 0, items);
        }

        /// <summary>Value list whose item i has the value i; 'selected' is the item shown first.</summary>
        protected void AddValueListAt(GH_Document doc, int input, string name, int selected, params string[] items)
        {
            var list = new Grasshopper.Kernel.Special.GH_ValueList();
            list.CreateAttributes();
            list.NickName = name;
            list.ListMode = Grasshopper.Kernel.Special.GH_ValueListMode.DropDown;
            list.ListItems.Clear();
            for (int i = 0; i < items.Length; i++)
                list.ListItems.Add(new Grasshopper.Kernel.Special.GH_ValueListItem(items[i], i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            list.SelectItem(Math.Max(0, Math.Min(items.Length - 1, selected)));
            list.Attributes.Pivot = Beside(input, 190f);
            doc.AddObject(list, false);
            Params.Input[input].AddSource(list);
        }

        protected void AddSlider(GH_Document doc, int input, string name, decimal min, decimal max, decimal value, int decimals)
        {
            var slider = new Grasshopper.Kernel.Special.GH_NumberSlider();
            slider.CreateAttributes();
            slider.NickName = name;
            slider.Slider.Minimum = min;
            slider.Slider.Maximum = max;
            slider.Slider.DecimalPlaces = decimals;
            slider.Slider.Type = decimals == 0 ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
            slider.SetSliderValue(value);
            slider.Attributes.Pivot = Beside(input, 260f);
            doc.AddObject(slider, false);
            Params.Input[input].AddSource(slider);
        }

        protected void AddToggle(GH_Document doc, int input, string name, bool value)
        {
            var toggle = new Grasshopper.Kernel.Special.GH_BooleanToggle();
            toggle.CreateAttributes();
            toggle.NickName = name;
            toggle.Value = value;
            toggle.Attributes.Pivot = Beside(input, 150f);
            doc.AddObject(toggle, false);
            Params.Input[input].AddSource(toggle);
        }
    }

    // ============================================================== 0 Quick Start
    /// <summary>
    /// Drop it on an empty canvas: it builds the complete slicing chain with a demo part and the
    /// house settings, wired and ready, and then removes itself.
    /// </summary>
    public class QuickStartComponent : GH_Component
    {
        private bool _done;

        public QuickStartComponent()
            : base("Quick Start", "Start",
                  "Place this on the canvas to get a complete, working slicing setup: demo part, print settings (5 mm bead, 2 mm layer), walls, bottom, ribs, continuous path, rounding, skirt and exit, extrusion and check. Replace the demo part with your own geometry.",
                  SlicerIO.Category, SlicerIO.SubCategory) { }

        public override Guid ComponentGuid => new Guid("5CC24A30-0D0B-4EDC-906E-F78FD264DAB3");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerQuickStartIcon.png");
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { }

        public override bool Read(GH_IO.Serialization.GH_IReader reader) { _done = true; return base.Read(reader); }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (_done) return;
            _done = true;
            // build after the current solution so the canvas is in a clean state
            document.ScheduleSolution(5, doc => Build(doc));
        }

        private static Brep DemoPart()
        {
            // a curved hull, 60 mm high: shows walls, ribs and bottom in one part
            var outer = new Arc(Plane.WorldXY, 300, Math.PI * 0.9).ToNurbsCurve();
            var inner = new Arc(Plane.WorldXY, 200, Math.PI * 0.9).ToNurbsCurve();
            inner.Reverse();
            var joined = Curve.JoinCurves(new Curve[] { outer, new LineCurve(outer.PointAtEnd, inner.PointAtStart), inner, new LineCurve(inner.PointAtEnd, outer.PointAtStart) });
            return joined != null && joined.Length > 0 ? Surface.CreateExtrusion(joined[0], new Vector3d(0, 0, 60)).ToBrep() : null;
        }

        private void Build(GH_Document doc)
        {
            float x0 = Attributes.Pivot.X + 260f, y0 = Attributes.Pivot.Y;
            Func<IGH_DocumentObject, float, float, IGH_DocumentObject> place = (obj, dx, dy) =>
            {
                obj.CreateAttributes();
                obj.Attributes.Pivot = new System.Drawing.PointF(x0 + dx, y0 + dy);
                doc.AddObject(obj, false);
                return obj;
            };
            var part = (Grasshopper.Kernel.Parameters.Param_Brep)place(new Grasshopper.Kernel.Parameters.Param_Brep(), 0, 330);
            part.NickName = "Your part";
            Brep demo = DemoPart();
            if (demo != null) part.PersistentData.Append(new GH_Brep(demo));

            var machine = (GH_Component)place(new MachineProfileComponent(), -180, -40);
            var settings = (GH_Component)place(new PrintSettingsComponent(), 40, 0);
            var slice = (GH_Component)place(new SliceComponent(), 260, 200);
            var walls = (GH_Component)place(new WallsComponent(), 450, 200);
            var bottom = (GH_Component)place(new BottomFillComponent(), 860, 200);
            var ribs = (GH_Component)place(new InfillRibsComponent(), 1060, 200);
            var path = (GH_Component)place(new ContinuousPathComponent(), 1490, 200);
            var round = (GH_Component)place(new RoundCornersComponent(), 1690, 200);
            var lead = (GH_Component)place(new StartEndComponent(), 1880, 200);
            var extrude = (GH_Component)place(new ExtrusionSpeedComponent(), 2340, 120);
            var check = (GH_Component)place(new PathCheckComponent(), 2100, 420);

            settings.Params.Input[11].AddSource(machine.Params.Output[0]);
            IGH_Param s = settings.Params.Output[0];
            slice.Params.Input[0].AddSource(part); slice.Params.Input[1].AddSource(s);
            walls.Params.Input[0].AddSource(slice.Params.Output[0]); walls.Params.Input[1].AddSource(s);
            bottom.Params.Input[0].AddSource(walls.Params.Output[0]); bottom.Params.Input[1].AddSource(s);
            ribs.Params.Input[0].AddSource(bottom.Params.Output[0]); ribs.Params.Input[1].AddSource(s);
            path.Params.Input[0].AddSource(ribs.Params.Output[0]); path.Params.Input[1].AddSource(s);
            round.Params.Input[0].AddSource(path.Params.Output[0]);
            lead.Params.Input[0].AddSource(round.Params.Output[0]);
            extrude.Params.Input[0].AddSource(lead.Params.Output[0]);
            check.Params.Input[0].AddSource(lead.Params.Output[0]);

            // demo values: ribs every 150 mm, meeting in the middle
            var spacing = (Grasshopper.Kernel.Parameters.Param_Number)ribs.Params.Input[3];
            spacing.PersistentData.Clear(); spacing.PersistentData.Append(new GH_Number(150));
            var meet = (Grasshopper.Kernel.Parameters.Param_Boolean)ribs.Params.Input[5];
            meet.PersistentData.Clear(); meet.PersistentData.Append(new GH_Boolean(true));

            // spell out every input and output, so nobody has to guess what "W" or "Fl" means
            foreach (var c in new GH_Component[] { machine, settings, slice, walls, bottom, ribs, path, round, lead, extrude, check })
            {
                foreach (var prm in c.Params.Input) prm.NickName = prm.Name;
                foreach (var prm in c.Params.Output) prm.NickName = prm.Name;
                c.Attributes.ExpireLayout();
            }

            // show only the finished path and the check
            foreach (var c in new GH_Component[] { slice, walls, bottom, ribs, path, round, extrude }) c.Hidden = true;
            part.Hidden = true;

            doc.RemoveObject(this, false);
        }
    }

    // ============================================================== 0b open interface
    public class LayersToCurvesComponent : SlicerComponent
    {
        public LayersToCurvesComponent()
            : base("Layers to Curves", "L>Crv",
                  "Opens layers up as plain curves, one branch per layer. Use it to try your own pattern in a script or with standard Grasshopper components, then turn the result back into layers with Curves to Layers.") { }

        public override Guid ComponentGuid => new Guid("85370F11-452C-4324-BEBE-FBB9A6C3EC29");
        public override GH_Exposure Exposure => GH_Exposure.septenary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerOpenIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from any slicer component", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "Contours, one branch per layer", GH_ParamAccess.tree);
            p.AddPlaneParameter("Planes", "P", "Layer plane at nozzle height, one per layer", GH_ParamAccess.list);
            p.AddNumberParameter("Heights", "H", "Layer height, one per layer", GH_ParamAccess.list);
            p.AddTextParameter("Roles", "R", "Role of every curve (Wall, Bottom, Infill ...), same tree as the curves", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).OrderBy(l => l.Index).ToList();
            var curves = new Grasshopper.DataTree<Curve>();
            var roles = new Grasshopper.DataTree<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                var branch = new Grasshopper.Kernel.Data.GH_Path(i);
                for (int c = 0; c < layers[i].Contours.Count; c++)
                {
                    curves.Add(new PolylineCurve(layers[i].Contours[c]), branch);
                    roles.Add((layers[i].ContourRoles != null && c < layers[i].ContourRoles.Count ? layers[i].ContourRoles[c] : PathRole.Wall).ToString(), branch);
                }
            }
            Message = layers.Count + " layers";
            da.SetDataTree(0, curves);
            da.SetDataList(1, layers.Select(l => l.Plane));
            da.SetDataList(2, layers.Select(l => l.Height));
            da.SetDataTree(3, roles);
        }
    }

    public class CurvesToLayersComponent : SlicerComponent
    {
        public CurvesToLayersComponent()
            : base("Curves to Layers", "Crv>L",
                  "Turns your own curves into layers, one branch per layer. Everything after it (Continuous Path, Round Corners, Extrusion ...) then works on your pattern. Closed curves are printed as loops, open curves back and forth.") { }

        public override Guid ComponentGuid => new Guid("94691320-4B3D-4C27-9CE4-83CBAAE545AD");
        public override GH_Exposure Exposure => GH_Exposure.septenary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerOpenIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "Curves, one branch per layer, bottom layer first", GH_ParamAccess.tree);
            p.AddPlaneParameter("Planes", "P", "Layer plane at nozzle height, one per layer. With a single plane the layers are stacked along its normal", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddTextParameter("Roles", "R", "Optional role per curve (Wall, Bottom, Infill, Support), same tree as the curves", GH_ParamAccess.tree);
            p[1].Optional = true; p[2].Optional = true; p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers for the path components", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Grasshopper.Kernel.Data.GH_Structure<GH_Curve> tree;
            if (!da.GetDataTree(0, out tree) || tree.PathCount == 0) return;
            var planes = new List<Plane>();
            da.GetDataList(1, planes);
            var settings = SlicerIO.Get<PrintSettings>(da, 2) ?? new PrintSettings();
            Grasshopper.Kernel.Data.GH_Structure<GH_String> roleTree;
            da.GetDataTree(3, out roleTree);

            var layers = new List<GH_SliceLayer>();
            for (int b = 0; b < tree.PathCount; b++)
            {
                var branch = tree.Branches[b];
                var layer = new SliceLayer { Index = b, Height = settings.LayerHeight, ContourRoles = new List<PathRole>() };
                Point3d firstPoint = Point3d.Unset;
                for (int c = 0; c < branch.Count; c++)
                {
                    Curve crv = branch[c]?.Value;
                    if (crv == null) continue;
                    Polyline pl;
                    if (!crv.TryGetPolyline(out pl))
                    {
                        var pc = crv.ToPolyline(0.05, 0.05, Math.Max(0.5, settings.PointSpacing), 0);
                        if (pc == null) continue;
                        pl = pc.ToPolyline();
                    }
                    if (pl.Count < 2) continue;
                    if (!firstPoint.IsValid) firstPoint = pl[0];
                    layer.Contours.Add(pl);
                    PathRole role = PathRole.Wall;
                    if (roleTree != null && b < roleTree.PathCount && c < roleTree.Branches[b].Count && roleTree.Branches[b][c] != null)
                        Enum.TryParse(roleTree.Branches[b][c].Value, true, out role);
                    layer.ContourRoles.Add(role);
                }
                if (layer.Contours.Count == 0) continue;

                if (planes.Count > b) layer.Plane = planes[b];
                else if (planes.Count == 1)
                {
                    // same orientation as the given plane, moved along its normal to this layer's curves
                    Plane basePlane = planes[0];
                    layer.Plane = new Plane(basePlane.Origin + basePlane.Normal * basePlane.DistanceTo(firstPoint), basePlane.XAxis, basePlane.YAxis);
                }
                else layer.Plane = new Plane(new Point3d(0, 0, firstPoint.Z), Vector3d.XAxis, Vector3d.YAxis);
                layers.Add(new GH_SliceLayer(layer));
            }
            Message = layers.Count + " layers";
            da.SetDataList(0, layers);
        }
    }

    // ============================================================== 1 Print Settings
    public class PrintSettingsComponent : SlicerComponent
    {
        public PrintSettingsComponent()
            : base("Print Settings", "PrintSet",
                  "Bead width, layer height, speed and quality for all slicer components. Preview = fast and coarse, without rounding.") { }

        public override Guid ComponentGuid => new Guid("EC73D6C2-DE66-465F-ACAB-F7D5861F7489");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSettingsIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Bead Width", "W", "Width of the printed bead (mm)", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Layer Height", "H", "Layer height (mm)", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Speed", "F", "Print speed (mm/min)", GH_ParamAccess.item, 3000.0);
            p.AddNumberParameter("Flow", "Flow", "Extrusion multiplier (1 = calculated amount)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Overlap", "Ov", "Sideways overlap of neighbouring beads, 0-1 (used by walls and infill)", GH_ParamAccess.item, 0.15);
            p.AddNumberParameter("Min Radius", "R", "Smallest corner radius of the path (mm). 0 = bead width", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Resolution", "Res", "Smallest point spacing of the path (mm)", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Filament Diameter", "Fil", "0 = extrusion as volume in mm3 (pellet extruder). >0 = extrusion as filament length", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Min Speed", "Fmin", "Lowest speed when speed compensates the layer height (mm/min)", GH_ParamAccess.item, 300.0);
            p.AddNumberParameter("Max Speed", "Fmax", "Highest speed and travel speed (mm/min)", GH_ParamAccess.item, 12000.0);
            p.AddBooleanParameter("Preview", "Prev", "True = fast preview: coarse slicing, corners are not rounded", GH_ParamAccess.item, false);
            p.AddGenericParameter("Machine", "M", "Optional Machine Profile: how your printer wants the extrusion values (volume, filament length, or a value the controller scales). Empty = volume in mm3.", GH_ParamAccess.item);
            p[11].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 0, "Bead Width", 1m, 40m, 5m, 1);
            AddSlider(doc, 1, "Layer Height", 0.5m, 20m, 2m, 1);
            AddSlider(doc, 2, "Speed", 300m, 12000m, 3000m, 0);
            AddToggle(doc, 10, "Preview", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Settings", "S", "Print settings", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var s = new PrintSettings();
            da.GetData(0, ref s.BeadWidth); da.GetData(1, ref s.LayerHeight); da.GetData(2, ref s.Speed);
            da.GetData(3, ref s.Flow); da.GetData(4, ref s.Overlap); da.GetData(5, ref s.MinRadius);
            da.GetData(6, ref s.Resolution); da.GetData(7, ref s.FilamentDiameter);
            da.GetData(8, ref s.MinSpeed); da.GetData(9, ref s.MaxSpeed); da.GetData(10, ref s.Preview);
            if (Params.Input.Count > 11)
            {
                var machine = SlicerIO.Get<MachineProfile>(da, 11);
                if (machine != null) s.Machine = machine.Duplicate();
            }
            if (s.Machine.FilamentDiameter <= 0 && s.FilamentDiameter > 0)
            {
                s.Machine.FilamentDiameter = s.FilamentDiameter;
                s.Machine.Unit = "mm filament";
            }

            if (s.BeadWidth <= 0 || s.LayerHeight <= 0 || s.Speed <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bead width, layer height and speed must be greater than 0.");
                return;
            }
            if (s.LayerHeight > s.BeadWidth)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Layer height is larger than the bead width. Beads this tall rarely bond well.");
            s.Resolution = Math.Max(0.05, s.Resolution);
            s.MaxSpeed = Math.Max(s.MaxSpeed, s.Speed);
            s.MinSpeed = Math.Min(Math.Max(1, s.MinSpeed), s.Speed);
            Message = (s.Preview ? "Preview" : "Final") + (s.Machine.Name != new MachineProfile().Name ? "\n" + s.Machine.Name.Split(' ')[0] : "");
            da.SetData(0, new GH_PrintSettings(s));
        }
    }

    // ============================================================== 1b Machine Profile
    public class MachineProfileComponent : SlicerComponent
    {
        public MachineProfileComponent()
            : base("Machine Profile", "Machine",
                  "Describes how your printer wants the extrusion values. Pick a starting point and change the numbers to calibrate it: Factor scales every value, First Layer Factor only the first layer. Connect it to Print Settings. The Weber and Ginger presets use the values of the FH Muenster definition; check them on your machine.") { }

        public override Guid ComponentGuid => new Guid("9F2AB030-5375-43C2-ADEE-84F63E5D8EE6");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMachineIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Preset", "P", "Starting point:\n" + string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n)), GH_ParamAccess.item, 0);
            p.AddNumberParameter("Factor", "Fa", "Multiplies every extrusion value. Empty = value of the preset", GH_ParamAccess.item);
            p.AddNumberParameter("First Layer Factor", "F1", "Multiplies the extrusion of the first layer only (e.g. 1.1 to squash it onto the bed). Empty = value of the preset", GH_ParamAccess.item);
            p.AddNumberParameter("Filament Diameter", "Fil", "0 = E is a volume. >0 = E is the length of filament of this diameter (mm). Empty = value of the preset", GH_ParamAccess.item);
            p.AddIntegerParameter("Cross Section", "CS", "How the bead volume is calculated:\n0 = squashed bead with round sides\n1 = width x height (rectangle). Empty = value of the preset", GH_ParamAccess.item);
            p.AddTextParameter("Name", "N", "Your name for this machine. Empty = name of the preset", GH_ParamAccess.item);
            for (int i = 1; i < 6; i++) p[i].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 0, "Machine", MachineProfile.PresetNames);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Machine", "M", "Machine profile for Print Settings", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "What E means for this machine", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int preset = 0;
            da.GetData(0, ref preset);
            if (preset < 0 || preset >= MachineProfile.PresetNames.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Preset {preset} does not exist. Using 0.");
                preset = 0;
            }
            var m = MachineProfile.Preset(preset);
            double factor = 0, first = 0, fil = 0; int cs = 0; string name = null;
            if (da.GetData(1, ref factor)) m.Factor = factor;
            if (da.GetData(2, ref first)) m.FirstLayerFactor = first;
            if (da.GetData(3, ref fil)) { m.FilamentDiameter = Math.Max(0, fil); m.Unit = m.FilamentDiameter > 0 ? "mm filament" : (m.Unit == "mm filament" ? "mm3" : m.Unit); }
            if (da.GetData(4, ref cs)) m.RectangleSection = cs == 1;
            if (da.GetData(5, ref name) && !string.IsNullOrWhiteSpace(name)) m.Name = name.Trim();
            if (m.Factor <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Factor must be greater than 0. A factor of 0 gives no extrusion at all.");
                return;
            }
            if (m.FirstLayerFactor <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "First Layer Factor must be greater than 0.");
                return;
            }
            Message = m.Name.Split(' ')[0];
            string section = m.RectangleSection ? "width x height" : "squashed bead (width - height) x height + pi/4 x height2";
            da.SetData(0, new GH_MachineProfile(m));
            da.SetData(1, $"{m.Name}: E = {section} x length x Flow x {m.Factor:0.####}" +
                          (m.FirstLayerFactor != 1 ? $" (first layer x {m.FirstLayerFactor:0.##})" : "") +
                          (m.FilamentDiameter > 0 ? $" / filament area ({m.FilamentDiameter:0.##} mm)" : "") + $", unit: {m.Unit}");
        }
    }

    // ============================================================== 2 Slice
    public class SliceComponent : SlicerComponent
    {
        public SliceComponent()
            : base("Slice", "Slice",
                  "Cuts any geometry (open or closed Breps, surfaces, SubDs, meshes) into layers. Planar by default; set an angle for angled printing (e.g. 45 degrees). Connect a guide surface for non-planar layers or a guide curve for multi-axis printing along a bent axis.") { }

        public override Guid ComponentGuid => new Guid("0EC13A81-79E9-437A-88E4-103796F5B31F");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSliceIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Geometry", "G", "Breps, surfaces, extrusions, SubDs or meshes - open or closed", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddPlaneParameter("Plane", "P", "Layer plane. Its normal is the build direction and the tool axis", GH_ParamAccess.item, Plane.WorldXY);
            p.AddNumberParameter("Angle", "A", "Tilts the layers around the plane's X axis (degrees). 45 = 45 degree slicing", GH_ParamAccess.item, 0.0);
            p.AddGenericParameter("Guide", "Gd", "Optional. A surface, Brep or mesh: the layers follow it (non-planar). A curve: the layers stand square to it (multi-axis, for parts that grow along a bent axis). Empty = flat layers.", GH_ParamAccess.item);
            p.AddIntegerParameter("Guide Mode", "GM", "Only with a guide surface:\n0 = Flat bottom, top follows the guide: the first layer lies flat on the bed and the layers bend gradually\n1 = All layers follow the guide: for printing on a curved base or mould", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Tool", "T", "Only with a guide:\n0 = Tool square to the layer (robot)\n1 = Tool stays vertical (3-axis printer)", GH_ParamAccess.item, 0);
            p[1].Optional = true;
            p[4].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 5, "Guide Mode", "Flat bottom, top follows guide", "All layers follow guide");
            AddValueList(doc, 6, "Tool", "Square to the layer (robot)", "Stays vertical (3-axis)");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers for the path components", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Layer contours as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var geo = new List<object>();
            if (!da.GetDataList(0, geo) || geo.Count == 0) return;
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            Plane plane = Plane.WorldXY; double angle = 0;
            da.GetData(2, ref plane); da.GetData(3, ref angle);
            if (Math.Abs(angle) > 1e-9) plane.Rotate(Rhino.RhinoMath.ToRadians(angle), plane.XAxis, plane.Origin);

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            double tol = Math.Max(0.01, settings.Preview ? settings.Resolution * 0.25 : settings.Resolution * 0.025);
            // optional guide: surface -> non-planar, curve -> multi-axis
            Warp warp = null;
            IGH_Goo guideGoo = null;
            int guideMode = 0, toolMode = 0;
            da.GetData(4, ref guideGoo); da.GetData(5, ref guideMode); da.GetData(6, ref toolMode);
            if (guideGoo != null)
            {
                object guide = guideGoo.ScriptVariable();
                if (guide is Line gl) guide = new LineCurve(gl);
                if (guide is Curve gc)
                {
                    var cw = new CurveWarp(gc, toolMode == 0);
                    if (cw.Valid) warp = cw;
                }
                else
                {
                    Mesh gm = SliceEngine.ToMesh(new[] { guide }, tol, null);
                    if (gm != null)
                    {
                        var sw2 = new SurfaceWarp(gm, guideMode == 0, toolMode == 0);
                        if (sw2.Valid) warp = sw2;
                    }
                }
                if (warp == null)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The guide could not be used. Connect a surface, Brep or mesh (non-planar) or a curve (multi-axis). Slicing flat.");
                else
                {
                    if (Math.Abs(plane.Normal.Z) < 0.9999 || Math.Abs(angle) > 1e-9)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "With a guide the layers are defined by the guide; Plane and Angle are ignored.");
                    plane = Plane.WorldXY;
                }
            }

            // a bent layer needs a fine mesh, or long flat faces would stay straight
            Mesh mesh = SliceEngine.ToMesh(geo, tol, log, warp != null ? Math.Max(4.0, settings.BeadWidth * 1.5) : 0);
            if (mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable geometry. Connect Breps, surfaces, SubDs or meshes.");
                return;
            }
            long meshMs = sw.ElapsedMilliseconds;
            var layers = SliceEngine.Slice(mesh, plane, settings.LayerHeight, settings.PointSpacing, log, warp);
            sw.Stop();

            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
            if (layers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No layers. Check layer height and plane.");
                return;
            }

            Message = layers.Count + " layers" + (warp is CurveWarp ? "\nmulti-axis" : warp != null ? "\nnon-planar" : "");
            int open = layers.Sum(l => l.Contours.Count(c => !Geo.Closed(c)));
            int total = layers.Sum(l => l.Contours.Count);
            da.SetDataList(0, layers.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, layers.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"{layers.Count} layers{(warp != null ? ", " + warp.Name : "")}, {total} contours ({open} open), meshing {meshMs} ms, slicing {sw.ElapsedMilliseconds - meshMs} ms" +
                          (settings.Preview ? " [preview]" : ""));
        }
    }

    // ============================================================== 2b Walls
    public class WallsComponent : SlicerComponent
    {
        public WallsComponent()
            : base("Walls", "Walls",
                  "Turns the model contour into wall paths. Position decides whether the bead lies inside your geometry (default), is centred on it, or lies outside. Further walls follow at bead spacing (bead width x (1 - overlap)).") { }

        public override Guid ComponentGuid => new Guid("48764F79-F830-4CF8-8F8B-9B2928110004");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWallsIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Slice", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N", "Number of walls", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Position", "Pos", "Where the bead lies relative to your geometry:\n0 = Inside: the outside of the print is the geometry (path moves half a bead width inward)\n1 = Centred: the geometry is the middle of the print path\n2 = Outside: the inside of the print is the geometry", GH_ParamAccess.item, 0);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 3, "Position", "Inside the geometry", "Centred on the geometry", "Outside the geometry");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with wall paths", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Wall paths as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Slice."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            int count = 1, position = 0;
            da.GetData(2, ref count); da.GetData(3, ref position);

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Walls(layers, settings, count, position, log);
            Message = (count == 1 ? "1 wall" : count + " walls") + (position == 1 ? ", centred" : position == 2 ? ", outside" : ", inside");
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);

            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"{result.Count} layers, {result.Sum(l => l.Contours.Count)} wall paths, spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 2d Infill Cells
    public class InfillCellsComponent : SlicerComponent
    {
        public InfillCellsComponent()
            : base("Infill Cells", "Cells",
                  "Fills the part with honeycomb or grid cells as part of the one continuous wall line. Every cell is a closed loop; neighbouring loops lie one bead spacing apart and fuse into a double wall. All loops and the wall are joined by short bridges into a single closed line that never crosses itself, so it also prints in spiral (vase) mode. Use it instead of Infill Ribs.") { }

        public override Guid ComponentGuid => new Guid("9C6C787F-70B0-4358-9253-5D30EAE46AD4");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerCellsIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls or Bottom Fill", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Pattern", "P", "0 = Honeycomb\n1 = Grid", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Cell Size", "Sz", "Distance between the middles of two neighbouring cells (mm). At least four bead spacings.", GH_ParamAccess.item, 60.0);
            p.AddNumberParameter("Angle", "A", "Turns the pattern (degrees)", GH_ParamAccess.item, 0.0);
            p.AddPointParameter("Origin", "O", "Fixed point of the pattern: the middle of one cell. Move it to shift the whole pattern. The cells stand exactly above each other in all layers. Empty = world origin.", GH_ParamAccess.item);
            p.AddNumberParameter("Stretch", "St", "Stretches the cells along the pattern direction (1 = regular, 2 = twice as long). Turn the direction with Angle.", GH_ParamAccess.item, 1.0);
            p[1].Optional = true; p[5].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Pattern", "Honeycomb", "Grid");
            AddSlider(doc, 3, "Cell Size", 20, 300, 60, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with the cells joined into the wall path", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Paths as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls or Bottom Fill."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            int pattern = 0; double size = 60, angle = 0; Point3d origin = Point3d.Unset;
            da.GetData(2, ref pattern); da.GetData(3, ref size); da.GetData(4, ref angle);
            bool hasOrigin = da.GetData(5, ref origin) && origin.IsValid;
            double stretch = 1.0;
            if (Params.Input.Count > 6) da.GetData(6, ref stretch);
            if (size < 4 * settings.BeadSpacing)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Cell Size raised to {4 * settings.BeadSpacing:0.#} mm: a cell needs room for two beads and an opening.");

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Cells(layers, settings, pattern, size, angle, hasOrigin ? (Point3d?)origin : null, log, out int cells, stretch);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
            if (cells == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No cells fit. Reduce Cell Size or check that the layers have a closed wall.");
            Message = cells == 0 ? "No cells" : (pattern == 1 ? "Grid" : "Honeycomb");
            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"up to {cells} cells per layer, bead spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 2e Infill Wave
    public class InfillWaveComponent : SlicerComponent
    {
        public InfillWaveComponent()
            : base("Infill Wave", "Wave",
                  "A single inner line that swings between the wall and a set depth: as a smooth wave, a truss (straight flanks) or a meander (steep flanks). At every crest it runs along the wall for two bead widths and fuses with it. The line is part of the one continuous wall path. Wavelength and Depth scale the pattern, Origin moves it.") { }

        public override Guid ComponentGuid => new Guid("80A0FC23-1A1C-44EA-89C1-EEF889BA2507");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWaveIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls or Bottom Fill", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Shape", "Sh", "0 = Wave (smooth, no corners)\n1 = Truss (straight flanks, triangles)\n2 = Meander (steep flanks, long contact with the wall)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Wavelength", "WL", "Distance from crest to crest along the wall (mm). The slicer fits a whole number of waves around the part.", GH_ParamAccess.item, 80.0);
            p.AddNumberParameter("Depth", "D", "How far the line reaches into the part, measured from crest to trough (mm). Reduced automatically where the part is too narrow.", GH_ParamAccess.item, 60.0);
            p.AddPointParameter("Origin", "O", "A crest sits at the wall point nearest to this point. Move it to shift the pattern along the wall. Empty = the right-most side of the part.", GH_ParamAccess.item);
            p[1].Optional = true; p[5].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Shape", "Wave", "Truss", "Meander");
            AddSlider(doc, 3, "Wavelength", 30, 400, 80, 0);
            AddSlider(doc, 4, "Depth", 10, 400, 60, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with the wave joined into the wall path", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Paths as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls or Bottom Fill."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            int shape = 0; double wavelength = 80, depth = 60; Point3d origin = Point3d.Unset;
            da.GetData(2, ref shape); da.GetData(3, ref wavelength); da.GetData(4, ref depth);
            bool hasOrigin = da.GetData(5, ref origin) && origin.IsValid;
            if (depth < 2 * settings.BeadWidth)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Depth must be at least {2 * settings.BeadWidth:0.#} mm (two bead widths).");
                return;
            }

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Waves(layers, settings, Math.Max(0, Math.Min(2, shape)), wavelength, depth, hasOrigin ? (Point3d?)origin : null, log, out int waves);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
            if (waves == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No wave fits. Reduce Depth or Wavelength, or check that the layers have a closed wall.");
            Message = waves == 0 ? "No wave" : (shape == 1 ? "Truss" : shape == 2 ? "Meander" : "Wave");
            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"up to {waves} waves per layer, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 2f Infill Sketch
    public class InfillSketchComponent : SlicerComponent
    {
        public InfillSketchComponent()
            : base("Infill Sketch", "Sketch",
                  "Draw your infill. Sketch curves over the part in top view; every curve that runs from wall to wall becomes a rib that follows it, built from two fused beads and joined into the one continuous wall path. Works on every layer the curve crosses, also on tilted and bent layers.") { }

        public override Guid ComponentGuid => new Guid("336FF81A-BFB3-464E-AE8F-61E1D320126F");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSketchIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls or Bottom Fill", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddCurveParameter("Sketch", "Sk", "Curves drawn in top view (flat, at any height). Each one should cross the part from wall to wall. Keep bends wider than the bead width.", GH_ParamAccess.list);
            p.AddBooleanParameter("Meet", "Meet", "False = one rib along the whole curve\nTrue = two ribs, one from each wall, that meet on the curve; the joint changes sides every layer so the ribs interlock", GH_ParamAccess.item, false);
            p.AddNumberParameter("Stagger", "St", "Meet only: how far the joint moves off the middle, alternating every layer (0-0.35)", GH_ParamAccess.item, 0.15);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddToggle(doc, 3, "Meet", false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with the sketched ribs joined into the wall path", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Paths as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls or Bottom Fill."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            var curves = new List<Curve>();
            da.GetDataList(2, curves);
            bool meet = false; double stagger = 0.15;
            da.GetData(3, ref meet); da.GetData(4, ref stagger);
            var sketch = curves.Where(c => c != null && c.IsValid).Cast<object>().ToList();
            if (sketch.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Draw curves over the part in top view and connect them to Sketch.");
                da.SetDataList(0, layers.Select(l => new GH_SliceLayer(l)));
                return;
            }
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Ribs(layers, settings, sketch, 0, 0, meet, stagger, log, out int ribs);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
            if (ribs == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No rib could be made. Each curve must be flat (drawn in top view) and cross the part from wall to wall.");
            Message = ribs == 0 ? "No ribs" : sketch.Count + (sketch.Count == 1 ? " curve" : " curves");
            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"{sketch.Count} sketched curve(s), {ribs} rib(s) in total over {result.Count} layers, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 2c Infill Ribs
    public class InfillRibsComponent : SlicerComponent
    {
        public InfillRibsComponent()
            : base("Infill Ribs", "Ribs",
                  "Adds stiffening ribs as part of the continuous path. Each rib is a hairpin whose two legs lie one bead spacing apart and fuse; the tip stops one bead spacing before the opposite wall. Draw curves in top view to say where the ribs run, or let them be distributed evenly.") { }

        public override Guid ComponentGuid => new Guid("044D4817-5A79-47BC-945D-643A38F6CB95");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerRibsIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls (or Slice)", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddGenericParameter("Locations", "Loc", "Where ribs go:\nCurve drawn in top view = the rib follows the curve from wall to wall (draw your infill)\nPoint = straight rib at the nearest wall point in every layer\nCurve running upwards = rib where the curve crosses each layer\nPlane = rib where the plane cuts the wall", GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "Sp", "Additionally distribute ribs evenly along the wall at about this distance (mm). 0 = only the given locations", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Depth", "D", "Rib length (mm). 0 = across to the opposite wall", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Meet", "Meet", "True = two ribs, one from each wall, meet inside the part. False = one rib across", GH_ParamAccess.item, false);
            p.AddNumberParameter("Stagger", "St", "Meet only: how far the joint of the two ribs is moved off the middle, alternating every layer (0-0.35 of the wall distance). The ribs then overlap from layer to layer and lock together", GH_ParamAccess.item, 0.15);
            p[1].Optional = true; p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with ribs spliced into the wall path", GH_ParamAccess.list);
            p.AddCurveParameter("Contours", "C", "Paths as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls or Slice."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();

            var locGoos = new List<IGH_Goo>();
            da.GetDataList(2, locGoos);
            var locations = new List<object>();
            int unknown = 0;
            foreach (var g in locGoos)
            {
                object o = g?.ScriptVariable();
                if (o is Point3d || o is Point || o is Curve || o is Line || o is Plane) locations.Add(o);
                else if (o is Arc arc) locations.Add(new ArcCurve(arc));
                else if (o is Circle circle) locations.Add(new ArcCurve(circle));
                else if (o != null) unknown++;
            }
            if (unknown > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{unknown} location(s) ignored. Use points, curves, lines or planes.");

            double spacing = 0, depth = 0, stagger = 0.15; bool meet = false;
            da.GetData(3, ref spacing); da.GetData(4, ref depth); da.GetData(5, ref meet); da.GetData(6, ref stagger);
            if (locations.Count == 0 && spacing <= 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No ribs: connect Locations or set Spacing.");

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Ribs(layers, settings, locations, spacing, depth, meet, stagger, log, out int ribs);
            Message = ribs == 0 ? "No ribs" : (meet ? "Meet" : "Across");
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);

            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"{ribs} ribs in {result.Count} layers ({(result.Count > 0 ? (double)ribs / result.Count : 0):0.#} per layer), leg spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 2d Bottom Fill
    public class BottomFillComponent : SlicerComponent
    {
        public BottomFillComponent()
            : base("Bottom Fill", "Bottom",
                  "Solid layers at the bottom, as part of the continuous path. The fill is one line that never crosses itself; lines lie one bead spacing apart and the wall connects to it with one short step. For a closed top use Top Closure.") { }

        public override Guid ComponentGuid => new Guid("A29B6A71-A470-4E16-B257-DF8C32D97ED9");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerBottomIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N", "Number of solid bottom layers. 0 = no bottom (walls and ribs only). A number as large as the layer count fills the whole part", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Pattern", "P", "0 = Auto (zigzag where one continuous zigzag fits, otherwise contour)\n1 = Zigzag (parallel lines, turned 90 degrees every layer)\n2 = Contour (follows the wall inward as one line; for curved hulls, openings, narrow arms)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Angle", "A", "Zigzag direction in the layer plane (degrees)", GH_ParamAccess.item, 0.0);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 3, "Pattern", "Auto", "Zigzag", "Contour (follows the wall)");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with bottom fill", GH_ParamAccess.list);
            p.AddCurveParameter("Fill", "F", "Fill lines as curves (for checking)", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).ToList();
            if (layers.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Walls."); return; }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            int count = 1, pattern = 0, top = 0; double angle = 0;
            da.GetData(2, ref count); da.GetData(3, ref pattern); da.GetData(4, ref angle);

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Bottom(layers, settings, Math.Max(0, count), pattern, angle, log, out int filled, Math.Max(0, top));
            Message = count <= 0 && top <= 0 ? "Off" : (pattern == 1 ? "Zigzag" : pattern == 2 ? "Contour" : "Auto") + (top > 0 ? " + top" : "");
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);

            var fills = new List<PolylineCurve>();
            int pieces = 0;
            foreach (var l in result)
            {
                if (l.ContourRoles == null) continue;
                for (int c = 0; c < l.Contours.Count; c++)
                    if (l.ContourRoles[c] == PathRole.Bottom) { fills.Add(new PolylineCurve(l.Contours[c])); pieces++; }
            }
            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, fills);
            da.SetData(2, $"{filled} solid layer(s) filled with {pieces} continuous line(s), line spacing {settings.BeadSpacing:0.##} mm, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 3 Continuous Path
    public class ContinuousPathComponent : SlicerComponent
    {
        public ContinuousPathComponent()
            : base("Continuous Path", "ContPath",
                  "Joins all layers into one print path without stops. Spiral = vase mode, no seam. Layered = flat layers that climb to the next layer over a ramp at one fixed seam; open contours are printed back and forth.") { }

        public override Guid ComponentGuid => new Guid("9D15FB9C-67D1-4DC8-A98B-962362497525");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPathIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Slice", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Mode", "M", "1 = Layered (default): every layer is flat, the path changes layer over a short ramp at one fixed place\n0 = Spiral (vase mode): the path climbs all the time. Only for single walls without infill - layers with ribs, cells or a bottom are always printed flat", GH_ParamAccess.item, 1);
            p.AddGeometryParameter("Seam", "Seam", "Optional point or curve: where the seam is. Every layer starts at the place on its contour nearest to it. Without it the seam stays where the first layer started", GH_ParamAccess.item);
            p.AddNumberParameter("Ramp Length", "Ramp", "Layered only: length of the seam. Over this length the path climbs to the next layer while it keeps going round; every layer climbs at the same place. The layers stay flat everywhere else. About 4 bead widths is a good start; longer = softer but more of the layer is inclined. 0 = hard step", GH_ParamAccess.item, 20.0);
            p[1].Optional = true; p[3].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueListAt(doc, 2, "Mode", 1, "Spiral (vase, single wall only)", "Layered (flat layers)");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Continuous print path", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos = new List<IGH_Goo>();
            if (!da.GetDataList(0, goos)) return;
            var layers = goos.OfType<GH_SliceLayer>().Select(g => g.Value).Where(l => l != null).OrderBy(l => l.Index).ToList();
            if (layers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Layers output of Slice.");
                return;
            }
            var settings = SlicerIO.Get<PrintSettings>(da, 1) ?? new PrintSettings();
            int mode = 0; double ramp = 100; bool alternate = false;
            da.GetData(2, ref mode); da.GetData(4, ref ramp);
            IGH_GeometricGoo seamGoo = null; GeometryBase seam = null;
            if (da.GetData(3, ref seamGoo) && seamGoo != null)
            {
                object sv = seamGoo.ScriptVariable();
                if (sv is Point3d pt) seam = new Point(pt); else seam = sv as GeometryBase;
            }

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var tp = PathEngine.Build(layers, settings, mode == 0 ? ContinuousMode.Spiral : ContinuousMode.Layered, seam, ramp, alternate, log);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
            if (tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Empty path."); return; }

            int travels = tp.Roles.Count(r => r == PathRole.Travel);
            bool spiral = mode == 0 && !log.Any(l => l.StartsWith("Spiral needs"));
            Message = spiral ? "Spiral" : "Layered";
            da.SetData(0, new GH_Toolpath(tp));
            da.SetData(1, $"{tp.Count} points, {tp.Length() / 1000.0:0.00} m, {travels} travel move(s), {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 4 Round Corners
    public class RoundCornersComponent : SlicerComponent
    {
        public RoundCornersComponent()
            : base("Round Corners", "Round",
                  "Rounds every corner of the path with an arc that leaves and rejoins the path tangentially. Zigzag turns and rib tips become half circles. Skipped while Preview is on in the Print Settings.") { }

        public override Guid ComponentGuid => new Guid("602A12F1-F59C-4403-8CBE-D2E54491A9B7");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerRoundIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            p.AddNumberParameter("Min Radius", "R", "Corner radius for walls (mm). 0 = value from the Print Settings", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Rib Radius", "Rr", "Corner radius where ribs and fill leave the wall (mm). 0 = bead spacing. Larger = softer, wider rib foot", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Rounded print path", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null) return;
            double r = 0, rr = 0; da.GetData(1, ref r); da.GetData(2, ref rr);
            if (r <= 0) r = tp.Settings.EffectiveMinRadius;

            if (tp.Settings.Preview)
            {
                da.SetData(0, new GH_Toolpath(tp));
                da.SetData(1, "Preview: corners not rounded");
                Message = "Preview: off";
                return;
            }
            var sw = Stopwatch.StartNew();
            var rounded = PathEngine.Round(tp, r, rr, out int corners, out int tooSharp);
            sw.Stop();
            Message = $"R{r:0.#}";
            da.SetData(0, new GH_Toolpath(rounded));
            if (tooSharp > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{tooSharp} wall corner(s) have too little room for R{r:0.##} (another corner is too close); they got the largest arc that fits.");
            da.SetData(1, $"{corners} corner(s) rounded, {tooSharp} with a smaller radius than R{r:0.##}, {rounded.Count} points, {sw.ElapsedMilliseconds} ms");
        }
    }

    // ============================================================== 4b Start and End
    public class StartEndComponent : SlicerComponent
    {
        public StartEndComponent()
            : base("Start and End", "StartEnd",
                  "Adds a skirt before the part and an exit move after it. The skirt is always printed first and runs on into the outer wall as one continuous bead; the exit moves the nozzle off the part before it stops, so no drop is left on it.") { }

        public override Guid ComponentGuid => new Guid("33D81FBE-3B35-431E-A621-D0944C613D7F");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerStartEndIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            p.AddIntegerParameter("Skirt Loops", "Sk", "Loops around the first layer before the part starts. 0 = no skirt", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Skirt Distance", "Sd", "Distance between skirt and part (mm). 0 = three bead widths", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Exit Length", "Ex", "After the last point: move sideways off the part by this length (mm). 0 = none", GH_ParamAccess.item, 50.0);
            p.AddNumberParameter("Exit Lift", "Lift", "Then lift the nozzle by this height (mm)", GH_ParamAccess.item, 20.0);
            p.AddCurveParameter("Exit Curve", "Crv", "Optional: your own exit path. Replaces Exit Length and Exit Lift", GH_ParamAccess.item);
            p.AddNumberParameter("Exit Flow", "Fl", "Flow factor on the exit move. 0 = no extrusion, 1 = full extrusion", GH_ParamAccess.item, 0.0);
            p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path with skirt and exit", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) return;
            int loops = 1; double dist = 0, exit = 50, lift = 20, flow = 0; Curve crv = null;
            da.GetData(1, ref loops); da.GetData(2, ref dist); da.GetData(3, ref exit); da.GetData(4, ref lift);
            da.GetData(5, ref crv); da.GetData(6, ref flow);

            var log = new List<string>();
            var result = LeadEngine.StartEnd(tp, Math.Max(0, loops), dist, Math.Max(0, exit), Math.Max(0, lift), crv, flow, log);
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
            int skirt = result.Roles.Count(r => r == PathRole.Skirt);
            Message = (loops > 0 ? "Skirt" : "No skirt") + (crv != null ? " + curve exit" : (exit > 0 || lift > 0 ? " + exit" : ""));
            da.SetData(0, new GH_Toolpath(result));
            da.SetData(1, $"{skirt} skirt points, {result.Count - tp.Count - skirt} exit points, path now {result.Length() / 1000.0:0.00} m");
        }
    }

    // ============================================================== 5b Path Check
    public class PathCheckComponent : SlicerComponent
    {
        public PathCheckComponent()
            : base("Path Check", "Check",
                  "Checks whether the path can be printed. Main test: every bead must lie on the bead below. The path is the middle of the bead, so it may sit at most half a bead width beside the layer below. Points further out are shown; they would hang in the air.") { }

        public override Guid ComponentGuid => new Guid("8D9D06FF-B358-4D10-AAB8-6531B0F20E1E");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerCheckIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            p.AddNumberParameter("Max Offset", "Max", "Largest allowed sideways offset to the layer below (mm). 0 = half the bead width", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "OK", "True if nothing stands in the way of printing", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was checked and what was found", GH_ParamAccess.list);
            p.AddPointParameter("Unsupported", "U", "Path points that clearly do not lie on the layer below (more than 1.5 x the allowed offset). Smaller overshoots are counted in the report and visible in Offset", GH_ParamAccess.list);
            p.AddNumberParameter("Offset", "Off", "Sideways offset to the layer below for every path point (mm). Use it to colour the path", GH_ParamAccess.list);
            p.AddPointParameter("Crossings", "X", "Places where the path crosses itself within a layer. Must be empty", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) return;
            double max = 0; da.GetData(1, ref max);
            var r = LeadEngine.Check(tp, max);
            Message = r.Ok ? "OK" : "Check!";
            if (!r.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report.First(l => l.StartsWith("CROSSES") || l.StartsWith("NOT") || l.StartsWith("Bead shape:") || l.StartsWith("Speed")));
            da.SetData(0, r.Ok);
            da.SetDataList(1, r.Report);
            da.SetDataList(2, r.Unsupported);
            da.SetDataList(3, r.Offsets);
            da.SetDataList(4, r.Crossings);
        }
    }

    // ============================================================== 5 Extrusion and Speed
    public class ExtrusionSpeedComponent : SlicerComponent
    {
        public ExtrusionSpeedComponent()
            : base("Extrusion and Speed", "E+F",
                  "Calculates extrusion and speed for every move from the local layer height and bead width. Outputs points, tool planes, E and F ready for the DXR/GCode components.") { }

        public override Guid ComponentGuid => new Guid("273EE2E4-32D8-4874-B0F5-47F32A4D5E1F");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerExtrusionIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Print path", GH_ParamAccess.item);
            p.AddIntegerParameter("Compensation", "C", "How a changing layer height is handled:\n0 = Flow: speed stays constant, extrusion follows the height\n1 = Speed: extruder output stays constant, thin layers are printed faster", GH_ParamAccess.item, 0);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 1, "Compensation", "Flow follows height", "Speed follows height");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Path", "Path", "Print path as polyline", GH_ParamAccess.item);
            p.AddPointParameter("Points", "Pt", "Path points", GH_ParamAccess.list);
            p.AddPlaneParameter("Planes", "Pl", "Tool planes: Z = tool axis, X = travel direction", GH_ParamAccess.list);
            p.AddNumberParameter("Extrusion", "E", "Extrusion for the move that ends at each point (mm3, or mm filament)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "F", "Speed for the move that ends at each point (mm/min)", GH_ParamAccess.list);
            p.AddNumberParameter("Layer Height", "H", "Local layer height at each point (mm)", GH_ParamAccess.list);
            p.AddIntegerParameter("Layer", "L", "Layer index of each point", GH_ParamAccess.list);
            p.AddTextParameter("Role", "Role", "Wall, Bottom, Infill, Support, Seam or Travel", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Length, volume and print time", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) return;
            int mode = 0; da.GetData(1, ref mode);

            var r = PathEngine.Process(tp, mode == 1);
            Message = $"{r.Minutes:0} min";
            da.SetData(0, new PolylineCurve(tp.Points));
            da.SetDataList(1, tp.Points);
            da.SetDataList(2, r.Frames);
            da.SetDataList(3, r.Extrusion);
            da.SetDataList(4, r.Speed);
            da.SetDataList(5, tp.Heights);
            da.SetDataList(6, tp.Layers);
            da.SetDataList(7, tp.Roles.Select(x => x.ToString()));
            string unit = tp.Settings.Machine != null ? tp.Settings.Machine.Unit : (tp.Settings.FilamentDiameter > 0 ? "mm filament" : "mm3");
            da.SetData(8, $"Path {r.Length / 1000.0:0.00} m (printing {r.PrintLength / 1000.0:0.00} m), " +
                          $"volume {r.Volume / 1000.0:0.0} cm3, time {r.Minutes:0.0} min, E in {unit}");
        }
    }

    // ============================================================== 6 Desktop Test Print
    public class DesktopTestPrintComponent : SlicerComponent
    {
        public DesktopTestPrintComponent()
            : base("Desktop Test Print", "Desktop",
                  "Scaled-down G-code of the same toolpath for a small filament printer (Bambu Lab, Prusa, Creality, Elegoo and other Marlin or Klipper machines). Path, bead width and layer height shrink by the same factor, so you can check the line pattern, seams and overhangs on the desk before the large print. It checks the path, not the material: cooling and sagging of a pellet extruder do not scale. Start and end code are generic; compare them with your printer before the first run.") { }

        public override Guid ComponentGuid => new Guid("960F3310-1853-4750-A622-E2FBFD8DBE5D");
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerDesktopIcon.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End (or Round Corners)", GH_ParamAccess.item);
            p.AddIntegerParameter("Printer", "P", "Bed size:\n" + string.Join("\n", DesktopPrinter.Presets.Select((x, i) => i + " = " + x.Name)), GH_ParamAccess.item, 0);
            p.AddNumberParameter("Scale", "Sc", "Scale of the test print (0.1 = one tenth). 0 = automatic: the bead becomes 1.2 x the nozzle diameter", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Nozzle", "Nz", "Nozzle diameter of the desktop printer (mm)", GH_ParamAccess.item, 0.4);
            p.AddNumberParameter("Filament", "Fil", "Filament diameter (mm)", GH_ParamAccess.item, 1.75);
            p.AddNumberParameter("Nozzle Temperature", "Tn", "Degrees C", GH_ParamAccess.item, 215.0);
            p.AddNumberParameter("Bed Temperature", "Tb", "Degrees C", GH_ParamAccess.item, 60.0);
            p.AddNumberParameter("Speed", "V", "Print speed (mm/s). The first layer runs at half of it", GH_ParamAccess.item, 40.0);
            p.AddNumberParameter("Flow", "Fl", "Extrusion multiplier (1 = calculated amount)", GH_ParamAccess.item, 1.0);
            p.AddTextParameter("File", "File", "Optional file path (.gcode). Written when Save is true", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the file", GH_ParamAccess.item, false);
            p[9].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 1, "Printer", DesktopPrinter.Presets.Select(x => x.Name).ToArray());
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("G-code", "G", "G-code lines", GH_ParamAccess.list);
            p.AddCurveParameter("Preview", "C", "The scaled path as it lies on the printer bed", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "Size, bead, filament and time", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            int printer = 0; double scale = 0, nozzle = 0.4, filament = 1.75, tn = 215, tb = 60, speed = 40, flow = 1; string file = null; bool save = false;
            da.GetData(1, ref printer); da.GetData(2, ref scale); da.GetData(3, ref nozzle); da.GetData(4, ref filament);
            da.GetData(5, ref tn); da.GetData(6, ref tb); da.GetData(7, ref speed); da.GetData(8, ref flow); da.GetData(9, ref file); da.GetData(10, ref save);
            if (nozzle <= 0 || filament <= 0 || flow <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Nozzle, Filament and Flow must be greater than 0."); return; }
            printer = Math.Max(0, Math.Min(DesktopPrinter.Presets.Length - 1, printer));

            var r = DesktopGCode.Build(tp, DesktopPrinter.Presets[printer], scale, nozzle, filament, tn, tb, speed, flow);
            foreach (var wmsg in r.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, wmsg);
            Message = "1 : " + (1.0 / (scale > 0 ? scale : r.SuggestedScale)).ToString("0.#");
            if (save && !string.IsNullOrWhiteSpace(file))
            {
                try { System.IO.File.WriteAllLines(file, r.Lines); AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + file); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            da.SetDataList(0, r.Lines);
            if (r.Preview.Count > 1) da.SetData(1, new PolylineCurve(r.Preview));
            da.SetData(2, r.Info);
        }
    }
}
