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
                  "Place this on the canvas to get three complete, working examples, each in its own labelled group: 1 flat layers from the part to the file for the machine (G-code for the Ginger One, DXR for the Weber DXR), 2 non-planar layers along a guide surface, 3 multi-axis layers along a guide curve. Replace the demo parts with your own geometry.",
                  SlicerIO.Category, SlicerIO.SubCategory) { }

        public override Guid ComponentGuid => new Guid("5CC24A30-0D0B-4EDC-906E-F78FD264DAB3");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerQuickStartIcon.png", this);
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
            if (joined == null || joined.Length == 0) return null;
            // on the bed: every coordinate positive, as a machine with its origin in the corner needs it
            joined[0].Translate(new Vector3d(350, 60, 0)); // room for the skirt on the positive side
            return Surface.CreateExtrusion(joined[0], new Vector3d(0, 0, 60)).ToBrep();
        }

        /// <summary>Non-planar demo: a round hull whose top edge follows a saddle surface, and that surface.</summary>
        private static void NonPlanarDemo(Point3d c, out Brep part, out Brep guide)
        {
            const double r = 160; part = null; guide = null;
            Func<double, double, double> z = (x, y) => 70 + 22 * (x * x - y * y) / (r * r);
            var top = new List<Point3d>();
            for (int i = 0; i < 48; i++) { double a = i * Math.PI * 2 / 48; top.Add(new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z(r * Math.Cos(a), r * Math.Sin(a)))); }
            top.Add(top[0]);
            Curve topCurve = Curve.CreateInterpolatedCurve(top, 3, CurveKnotStyle.ChordPeriodic);
            Curve baseCurve = new Circle(new Plane(c, Vector3d.ZAxis), r).ToNurbsCurve();
            if (topCurve != null)
            {
                // seams at the same place, same direction
                double t; if (topCurve.ClosestPoint(baseCurve.PointAtStart + new Vector3d(0, 0, 70), out t)) topCurve.ChangeClosedCurveSeam(t);
                if (topCurve.TangentAtStart * baseCurve.TangentAtStart < 0) topCurve.Reverse();
                var loft = Brep.CreateFromLoft(new[] { baseCurve, topCurve }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
                if (loft != null && loft.Length > 0) part = loft[0];
            }
            var grid = new List<Point3d>();
            for (int i = 0; i < 7; i++) for (int j = 0; j < 7; j++) { double x = -1.35 * r + i * 0.45 * r, y = -1.35 * r + j * 0.45 * r; grid.Add(new Point3d(c.X + x, c.Y + y, z(x, y))); }
            var srf = NurbsSurface.CreateThroughPoints(grid, 7, 7, 3, 3, false, false);
            if (srf != null) guide = srf.ToBrep();
        }

        /// <summary>Multi-axis demo: a tube that grows along a bent axis, and that axis.</summary>
        private static void MultiAxisDemo(Point3d c, out Brep part, out Curve guide)
        {
            part = null;
            guide = new Arc(c, Vector3d.ZAxis, c + new Vector3d(170, 0, 260)).ToNurbsCurve();
            var pipe = Brep.CreatePipe(guide, 90, false, PipeCapMode.None, true, 0.01, 0.01);
            if (pipe != null && pipe.Length > 0) part = pipe[0];
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
            Action<string, System.Drawing.Color, IGH_DocumentObject[]> group = (title, colour, members) =>
            {
                var g = new Grasshopper.Kernel.Special.GH_Group { NickName = title, Colour = colour };
                g.CreateAttributes();
                doc.AddObject(g, false);
                foreach (var m in members) g.AddObject(m.InstanceGuid);
                g.ExpireCaches();
            };
            Action<IGH_Param, int> setInt = (prm, v) => { var pi = prm as Grasshopper.Kernel.Parameters.Param_Integer; if (pi == null) return; pi.PersistentData.Clear(); pi.PersistentData.Append(new GH_Integer(v)); };
            var all = new List<GH_Component>();
            var hide = new List<IGH_PreviewObject>();

            // ---- read me first
            var note = new Grasshopper.Kernel.Special.GH_Panel();
            note.NickName = "Start here";
            note.UserText =
                "START HERE\n\n" +
                "1  Replace 'Your part' (example 1) with your own geometry. It has to stand on the XY plane, in millimetres.\n" +
                "2  Set Bead Width and Layer Height in the Settings group. Everything else follows from them.\n" +
                "3  Read Path Check. OK = the path can be printed. If not, it says what to change.\n" +
                "4  Take the file for your machine:\n" +
                "     Output A - Ginger One: set Save on 'Ginger G-code'. The file lands on the Desktop.\n" +
                "     Output B - robot with Weber DXR: put your tool and the place of robot and bed into the robot cell, read the Report of 'Robot Program', then save the DXR.\n" +
                "5  Before the big print: 'Desktop Test Print' writes the same path small for a desktop printer.\n\n" +
                "Other infill: delete 'Infill Ribs' and put Infill Wave, Cells, Chambers or Sketch in its place.\n" +
                "Curved or tilted layers: see examples 2 and 3.\n" +
                "Just want a path quickly? Use the single component 'Easy Slicer' instead of this chain.";
            note.Properties.Multiline = true;
            note.Properties.Wrap = true;
            note.CreateAttributes();
            note.Attributes.Pivot = new System.Drawing.PointF(x0 - 760, y0 - 80);
            note.Attributes.Bounds = new System.Drawing.RectangleF(x0 - 760, y0 - 80, 500, 400);
            doc.AddObject(note, false);

            // ---- settings, shared by all three examples
            var machine = (GH_Component)place(new MachineProfileComponent(), -180, -60);
            var settings = (GH_Component)place(new PrintSettingsComponent(), 60, -60);
            settings.Params.Input[11].AddSource(machine.Params.Output[0]);
            IGH_Param s = settings.Params.Output[0];
            all.Add(machine); all.Add(settings);
            group("Settings - shared by all three examples. Set bead width and layer height here.", System.Drawing.Color.FromArgb(70, 120, 130, 145), new IGH_DocumentObject[] { machine, settings });

            // ---- 1 flat layers
            float y1 = 420;
            var part = (Grasshopper.Kernel.Parameters.Param_Brep)place(new Grasshopper.Kernel.Parameters.Param_Brep(), 60, y1 + 20);
            part.NickName = "Your part";
            Brep demo = DemoPart();
            if (demo != null) part.PersistentData.Append(new GH_Brep(demo));
            var slice = (GH_Component)place(new SliceComponent(), 300, y1);
            var walls = (GH_Component)place(new WallsComponent(), 520, y1);
            var bottom = (GH_Component)place(new BottomFillComponent(), 730, y1);
            var ribs = (GH_Component)place(new InfillRibsComponent(), 950, y1);
            var path = (GH_Component)place(new ContinuousPathComponent(), 1220, y1);
            var round = (GH_Component)place(new RoundCornersComponent(), 1440, y1);
            var lead = (GH_Component)place(new StartEndComponent(), 1650, y1);
            var extrude = (GH_Component)place(new ExtrusionSpeedComponent(), 1930, y1 - 90);
            var check = (GH_Component)place(new PathCheckComponent(), 1930, y1 + 150);
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
            all.AddRange(new[] { slice, walls, bottom, ribs, path, round, lead, extrude, check });
            hide.AddRange(new IGH_PreviewObject[] { part, slice, walls, bottom, ribs, path, round, extrude });
            group("1  Flat layers - the normal case. Replace 'Your part' with your own geometry. Swap Infill Ribs for Cells, Wave, Chambers or Sketch.", System.Drawing.Color.FromArgb(70, 10, 132, 255),
                new IGH_DocumentObject[] { part, slice, walls, bottom, ribs, path, round, lead });
            group("Result: Path Check says whether it can be printed, Extrusion and Speed gives the values for the machine.", System.Drawing.Color.FromArgb(70, 48, 176, 110), new IGH_DocumentObject[] { extrude, check });

            // ---- output: the file for the machine
            var ginger = (GH_Component)place(new GingerGCodeComponent(), 2260, y1 - 90);
            ginger.Params.Input[0].AddSource(lead.Params.Output[0]);
            // the robot cell: robot behind the bed, tool, work base in the bed corner
            var robot = (GH_Component)place(new LARGERslicer.Components.Robot.RobotArmComponent(), 2260, y1 + 130);
            var tool = (GH_Component)place(new LARGERslicer.Components.Robot.RobotToolComponent(), 2260, y1 + 270);
            var cellC = (GH_Component)place(new LARGERslicer.Components.Robot.RobotCellComponent(), 2500, y1 + 190);
            var program = (GH_Component)place(new LARGERslicer.Components.Robot.RobotProgramComponent(), 2720, y1 + 130);
            Action<IGH_Param, Plane> setPlane = (prm, pl) => { var pp = prm as Grasshopper.Kernel.Parameters.Param_Plane; if (pp == null) return; pp.PersistentData.Clear(); pp.PersistentData.Append(new GH_Plane(pl)); };
            // demo values: the robot stands 1.2 m behind the bed corner and 400 mm lower; the nozzle tip is 300 mm in front of the flange
            setPlane(robot.Params.Input[2], new Plane(new Point3d(-1200, 170, -400), Vector3d.XAxis, Vector3d.YAxis));
            setPlane(tool.Params.Input[1], new Plane(new Point3d(0, 0, 300), Vector3d.XAxis, Vector3d.YAxis));
            cellC.Params.Input[0].AddSource(robot.Params.Output[0]);
            cellC.Params.Input[1].AddSource(tool.Params.Output[0]);
            program.Params.Input[0].AddSource(lead.Params.Output[0]);
            program.Params.Input[1].AddSource(cellC.Params.Output[0]);
            var dxrMachine = (GH_Component)place(new LARGERslicer.Components.Export.MachineSettingsComponent(), 2720, y1 + 420);
            var dxr = (GH_Component)place(new LARGERslicer.Components.Export.DXRGeneratorComponent(), 3020, y1 + 190);
            dxr.Params.Input[0].AddSource(program.Params.Output[0]);
            dxr.Params.Input[1].AddSource(program.Params.Output[1]);
            dxr.Params.Input[2].AddSource(program.Params.Output[2]);
            dxr.Params.Input[3].AddSource(dxrMachine.Params.Output[0]);
            all.AddRange(new[] { ginger, robot, tool, cellC, program });
            hide.Add(tool);
            group("Output A - Ginger One: G-code with the machine's start and end. Give a file path and set Save to write it.", System.Drawing.Color.FromArgb(70, 232, 135, 12), new IGH_DocumentObject[] { ginger });
            group("Output B - Robot with the Weber DXR: the robot cell (robot, tool, work base), the robot program with its reach check, then the DXR file. Put in your own tool and where robot and bed stand. Choose the matching preset in Machine Profile.", System.Drawing.Color.FromArgb(70, 210, 63, 69), new IGH_DocumentObject[] { robot, tool, cellC, program, dxrMachine, dxr });
            // the demo is set up for the Ginger: its E values need the Ginger factor
            var presetList = machine.Params.Input[0].Sources.OfType<Grasshopper.Kernel.Special.GH_ValueList>().FirstOrDefault();
            if (presetList != null && presetList.ListItems.Count > 4) presetList.SelectItem(4);
            else setInt(machine.Params.Input[0], 4);

            // ---- 2 non-planar and 3 multi-axis: part + guide -> slice -> walls -> path -> round -> start/end -> check
            Func<float, IGH_Param, IGH_Param, int, int, IGH_DocumentObject[]> chain = (y, partParam, guideParam, guideMode, pathMode) =>
            {
                var sl = (GH_Component)place(new SliceComponent(), 300, y);
                var wa = (GH_Component)place(new WallsComponent(), 520, y);
                var pa = (GH_Component)place(new ContinuousPathComponent(), 730, y);
                var ro = (GH_Component)place(new RoundCornersComponent(), 950, y);
                var le = (GH_Component)place(new StartEndComponent(), 1160, y);
                var ch = (GH_Component)place(new PathCheckComponent(), 1440, y);
                sl.Params.Input[0].AddSource(partParam); sl.Params.Input[1].AddSource(s); sl.Params.Input[4].AddSource(guideParam);
                setInt(sl.Params.Input[5], guideMode);
                wa.Params.Input[0].AddSource(sl.Params.Output[0]); wa.Params.Input[1].AddSource(s);
                pa.Params.Input[0].AddSource(wa.Params.Output[0]); pa.Params.Input[1].AddSource(s);
                setInt(pa.Params.Input[2], pathMode);
                ro.Params.Input[0].AddSource(pa.Params.Output[0]);
                le.Params.Input[0].AddSource(ro.Params.Output[0]);
                ch.Params.Input[0].AddSource(le.Params.Output[0]);
                all.AddRange(new[] { sl, wa, pa, ro, le, ch });
                hide.AddRange(new IGH_PreviewObject[] { sl, wa, pa, ro });
                return new IGH_DocumentObject[] { sl, wa, pa, ro, le, ch };
            };

            float y2 = 900;
            Brep npPart, npGuide; NonPlanarDemo(new Point3d(950, 180, 0), out npPart, out npGuide);
            var part2 = (Grasshopper.Kernel.Parameters.Param_Brep)place(new Grasshopper.Kernel.Parameters.Param_Brep(), 60, y2 - 20);
            part2.NickName = "Part";
            if (npPart != null) part2.PersistentData.Append(new GH_Brep(npPart));
            var guide2 = (Grasshopper.Kernel.Parameters.Param_Brep)place(new Grasshopper.Kernel.Parameters.Param_Brep(), 60, y2 + 60);
            guide2.NickName = "Guide surface";
            if (npGuide != null) guide2.PersistentData.Append(new GH_Brep(npGuide));
            var row2 = chain(y2, part2, guide2, 0, 1).ToList(); row2.Add(part2); row2.Add(guide2);
            hide.Add(part2);
            group("2  Non-planar - the layers follow a guide surface. The first layer lies flat on the bed, the last one follows the top of the part. Draw the guide surface over your part.", System.Drawing.Color.FromArgb(70, 255, 149, 0), row2.ToArray());

            float y3 = 1320;
            Brep maPart; Curve maGuide; MultiAxisDemo(new Point3d(1450, 180, 0), out maPart, out maGuide);
            var part3 = (Grasshopper.Kernel.Parameters.Param_Brep)place(new Grasshopper.Kernel.Parameters.Param_Brep(), 60, y3 - 20);
            part3.NickName = "Part";
            if (maPart != null) part3.PersistentData.Append(new GH_Brep(maPart));
            var guide3 = (Grasshopper.Kernel.Parameters.Param_Curve)place(new Grasshopper.Kernel.Parameters.Param_Curve(), 60, y3 + 60);
            guide3.NickName = "Guide curve";
            if (maGuide != null) guide3.PersistentData.Append(new GH_Curve(maGuide));
            var row3 = chain(y3, part3, guide3, 0, 1).ToList(); row3.Add(part3); row3.Add(guide3);
            hide.Add(part3);
            group("3  Multi-axis - the layers stand square to a guide curve, for parts that grow along a bent axis (robot with tilting tool). Draw the axis of your part as the guide curve.", System.Drawing.Color.FromArgb(70, 175, 82, 222), row3.ToArray());

            // spell out every input and output, so nobody has to guess what "W" or "Fl" means
            foreach (var c in all)
            {
                foreach (var prm in c.Params.Input) prm.NickName = prm.Name;
                foreach (var prm in c.Params.Output) prm.NickName = prm.Name;
                c.Attributes.ExpireLayout();
            }

            // show only the finished paths, the guides and the checks
            foreach (var h in hide) h.Hidden = true;

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerOpenIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerOpenIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSettingsIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerMachineIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSliceIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWallsIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerCellsIcon.png", this);

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
                  "A single inner line that runs along the wall and dips into the part at regular intervals: as a smooth wave, a truss (straight flanks) or a meander (steep flanks). Every straight side gets a whole number of equal waves, corners stay clear, and the line is part of the one continuous wall path. Wavelength and Depth scale the pattern; on round parts Origin moves it.") { }

        public override Guid ComponentGuid => new Guid("80A0FC23-1A1C-44EA-89C1-EEF889BA2507");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerWaveIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls or Bottom Fill", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Shape", "Sh", "0 = Wave (smooth, no corners)\n1 = Truss (straight flanks, triangles)\n2 = Meander (steep flanks, long contact with the wall)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Wavelength", "WL", "Distance from crest to crest along the wall (mm). The slicer fits a whole number of waves around the part.", GH_ParamAccess.item, 80.0);
            p.AddNumberParameter("Depth", "D", "How far the line may reach into the part (mm). With Meet this is the limit; 0 = as far as the part allows.", GH_ParamAccess.item, 0.0);
            p.AddPointParameter("Origin", "O", "Round parts only: a crest sits at the wall point nearest to this point. Empty = the right-most side of the part.", GH_ParamAccess.item);
            p.AddBooleanParameter("Meet", "Meet", "True = every dip reaches until it meets something: the dip from the opposite side (in the middle of the part) or the wall of a hole. That is what makes the part stiff.\nFalse = all dips have the same Depth and end freely.", GH_ParamAccess.item, true);
            p[1].Optional = true; p[5].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Shape", "Wave", "Truss", "Meander");
            AddSlider(doc, 3, "Wavelength", 30, 400, 80, 0);
            AddToggle(doc, 6, "Meet", true);
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
            int shape = 0; double wavelength = 80, depth = 0; Point3d origin = Point3d.Unset;
            da.GetData(2, ref shape); da.GetData(3, ref wavelength); da.GetData(4, ref depth);
            bool hasOrigin = da.GetData(5, ref origin) && origin.IsValid;
            bool meet = true;
            if (Params.Input.Count > 6) da.GetData(6, ref meet);
            if (!(meet && depth <= 0) && depth < 2 * settings.BeadWidth)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Depth must be at least {2 * settings.BeadWidth:0.#} mm (two bead widths).");
                return;
            }

            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Waves(layers, settings, Math.Max(0, Math.Min(2, shape)), wavelength, depth, hasOrigin ? (Point3d?)origin : null, log, out int waves, meet);
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerSketchIcon.png", this);

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
            var crossLog = new List<string>();
            sketch = FillEngine.WithoutCrossingSketches(sketch, crossLog);
            foreach (var l in crossLog) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, l);
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

    // ============================================================== 2g Infill Chambers
    public class InfillChambersComponent : SlicerComponent
    {
        public InfillChambersComponent()
            : base("Infill Chambers", "Chambers",
                  "A band of closed chambers along the inside of the wall: the classic double wall with webs, as boxes or as a truss with diagonals. Every chamber is a loop, neighbouring loops fuse, and all are joined into the one continuous wall path. The chambers of a layer stand on those of the layer below, also on twisting or tapering parts.") { }

        public override Guid ComponentGuid => new Guid("7BBFAAF9-6748-49F9-A6A3-0549539812DE");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerChambersIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers from Walls or Bottom Fill", GH_ParamAccess.list);
            p.AddGenericParameter("Settings", "S", "Print Settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Shape", "Sh", "0 = Boxes (webs square to the wall)\n1 = Truss (boxes with alternating diagonals)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Length", "Len", "Length of a chamber along the wall (mm). Every side gets a whole number of equal chambers.", GH_ParamAccess.item, 70.0);
            p.AddNumberParameter("Depth", "D", "Depth of the band, from the wall inwards (mm). Reduced automatically where the part is too narrow.", GH_ParamAccess.item, 50.0);
            p[1].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 2, "Shape", "Boxes", "Truss");
            AddSlider(doc, 3, "Length", 30, 400, 70, 0);
            AddSlider(doc, 4, "Depth", 20, 300, 50, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Layers", "L", "Layers with the chambers joined into the wall path", GH_ParamAccess.list);
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
            int shape = 0; double length = 70, depth = 50;
            da.GetData(2, ref shape); da.GetData(3, ref length); da.GetData(4, ref depth);
            var log = new List<string>();
            var sw = Stopwatch.StartNew();
            var result = FillEngine.Chambers(layers, settings, shape == 1 ? 1 : 0, length, depth, log, out int chambers);
            sw.Stop();
            foreach (var l in log) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, l);
            if (chambers == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No chamber fits. Reduce Depth or Length, or check that the layers have a closed wall.");
            Message = chambers == 0 ? "No chambers" : (shape == 1 ? "Truss" : "Boxes");
            da.SetDataList(0, result.Select(l => new GH_SliceLayer(l)));
            da.SetDataList(1, result.SelectMany(l => l.RealContours()).Select(c => new PolylineCurve(c)));
            da.SetData(2, $"up to {chambers} chambers per layer, {sw.ElapsedMilliseconds} ms");
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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerRibsIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerBottomIcon.png", this);

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
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerPathIcon.png", this);

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
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerRoundIcon.png", this);

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
        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerStartEndIcon.png", this);

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
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerCheckIcon.png", this);

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
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerExtrusionIcon.png", this);

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
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerDesktopIcon.png", this);

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
            p.AddTextParameter("File", "File", "Optional. File or folder. Empty = larger_print.gcode on the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the file (to the Desktop if File is empty)", GH_ParamAccess.item, false);
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
            if (save)
            {
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(file, "larger_testprint.gcode", r.Lines)); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            da.SetDataList(0, r.Lines);
            if (r.Preview.Count > 1) da.SetData(1, new PolylineCurve(r.Preview));
            da.SetData(2, r.Info);
        }
    }
    // ============================================================== easy slicer
    /// <summary>The whole chain in one component, for the first print and for simple parts.</summary>
    public class EasySlicerComponent : SlicerComponent
    {
        public EasySlicerComponent()
            : base("Easy Slicer", "Easy",
                  "Model in, checked print path out: slices, builds walls, bottom and infill, joins everything into one continuous line, rounds the corners, adds skirt and exit and checks the result. Uses settings that suit most prints. For every option use the single components (Slice, Walls, Infill ..., Continuous Path) - they do the same steps one by one.") { }

        public override Guid ComponentGuid => new Guid("2A7F9D41-8C35-4E60-B1A9-7D3E5F2C8B46");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerEasyIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Geometry", "G", "Your part: Breps, surfaces, SubDs or meshes, standing on the XY plane", GH_ParamAccess.list);
            p.AddNumberParameter("Bead Width", "W", "Width of one printed bead (mm). Every dimension follows from it", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Layer Height", "H", "Layer height (mm): a third to a half of the bead width", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Speed", "F", "Print speed (mm/min), the same for every move", GH_ParamAccess.item, 3000.0);
            p.AddIntegerParameter("Machine", "M", "Machine profile:\n" + string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n)), GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Walls", "N", "Number of walls", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Bottom Layers", "B", "Solid layers at the bottom. 0 = open (hulls, vases)", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Infill", "I", string.Join("\n", EasySlicer.Infills.Select((n, i) => i + " = " + n)), GH_ParamAccess.item, 0);
            p.AddNumberParameter("Infill Size", "Sz", "Rib distance, wavelength or cell size (mm). 0 = chosen from the bead width", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Spiral", "Sp", "Vase mode: the path climbs all the time. Only for walls without infill and bottom", GH_ParamAccess.item, false);
            p[0].Optional = true; // an empty input shows "Connect your part" instead of a collect error
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 1, "Bead Width", 2, 20, 5, 1);
            AddSlider(doc, 2, "Layer Height", 0.5m, 10, 2, 1);
            AddValueList(doc, 4, "Machine", MachineProfile.PresetNames);
            AddValueList(doc, 7, "Infill", EasySlicer.Infills);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "The finished path: connect to Ginger G-code, Robot Program, Desktop Test Print or Extrusion and Speed", GH_ParamAccess.item);
            p.AddCurveParameter("Path", "P", "The path as a curve", GH_ParamAccess.item);
            p.AddBooleanParameter("OK", "OK", "True if the path can be printed", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "What was done, what the check found and what to do about it", GH_ParamAccess.list);
            p.AddGenericParameter("Layers", "L", "The layers with walls and infill, for the single components", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var geo = new List<object>();
            if (!da.GetDataList(0, geo) || geo.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Connect your part to Geometry: a Brep, surface, SubD or mesh that stands on the XY plane."); Message = "Connect a part"; return; }
            double w = 5, h = 2, f = 3000, size = 0; int machine = 0, walls = 1, bottom = 0, infill = 0; bool spiral = false;
            da.GetData(1, ref w); da.GetData(2, ref h); da.GetData(3, ref f); da.GetData(4, ref machine); da.GetData(5, ref walls);
            da.GetData(6, ref bottom); da.GetData(7, ref infill); da.GetData(8, ref size); da.GetData(9, ref spiral);
            if (w <= 0 || h <= 0 || f <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bead Width, Layer Height and Speed must be greater than 0."); return; }
            if (h > 0.6 * w) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The layer height is {h / w * 100:0} % of the bead width. Stay below half the bead width or the beads bond poorly.");
            machine = Math.Max(0, Math.Min(MachineProfile.PresetNames.Length - 1, machine));
            var st = new PrintSettings { BeadWidth = w, LayerHeight = h, Speed = f, Machine = MachineProfile.Preset(machine) };

            var log = new List<string>();
            Mesh mesh = SliceEngine.ToMesh(geo, Math.Max(0.01, st.Resolution * 0.025), log);
            if (mesh == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable geometry. Connect Breps, surfaces, SubDs or meshes."); return; }
            var layers = SliceEngine.Slice(mesh, Plane.WorldXY, st.LayerHeight, st.PointSpacing, log);
            var r = EasySlicer.Run(layers, st, new EasySlicer.Options { Walls = walls, BottomLayers = Math.Max(0, bottom), Infill = infill, InfillSize = size, Spiral = spiral });

            var report = new List<string>();
            report.Add("DONE: " + string.Join(" > ", r.Steps));
            if (r.Check != null) report.AddRange(r.Check.Report);
            foreach (var note in r.Notes.Concat(log).Distinct()) report.Add("Note: " + note);
            if (r.Path == null || r.Path.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, r.Notes.FirstOrDefault() ?? "No print path could be made.");
                Message = "No path";
                da.SetData(2, false); da.SetDataList(3, report);
                return;
            }
            if (!r.Check.Ok) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The check found something: read Report. " + (r.Check.Report.FirstOrDefault(l => l.StartsWith("NOT") || l.StartsWith("CROSS")) ?? ""));
            report.Add(r.Ok ? "NEXT: connect Toolpath to Ginger G-code, Robot Program or Desktop Test Print." : "NEXT: fix what the check reports, then connect Toolpath to an output component.");
            Message = r.Ok ? "Printable" : "Read the report";
            da.SetData(0, new GH_Toolpath(r.Path));
            da.SetData(1, new PolylineCurve(r.Path.Points));
            da.SetData(2, r.Ok);
            da.SetDataList(3, report);
            da.SetDataList(4, r.Layers.Select(l => new GH_SliceLayer(l)));
        }
    }

    // ============================================================== machine output
    public class GingerGCodeComponent : SlicerComponent
    {
        public GingerGCodeComponent()
            : base("Ginger G-code", "Ginger",
                  "Writes the toolpath as G-code for the Ginger One, in full size: the start and end sequence of the Ginger quick start guide, absolute positions, relative extrusion, one constant feedrate. Use Machine Profile with the Ginger preset so the E values fit the pellet extruder.") { }

        public override Guid ComponentGuid => new Guid("3B1D6C52-9E0A-4F37-8C1B-5D2A7E9F4A10");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerDesktopIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End", GH_ParamAccess.item);
            p.AddTextParameter("File", "File", "Optional. File or folder. Empty = larger_print.gcode on the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the file (to the Desktop if File is empty)", GH_ParamAccess.item, false);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("G-code", "G", "G-code lines", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Layers, length, extrusion and time", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            string file = null; bool save = false;
            da.GetData(1, ref file); da.GetData(2, ref save);
            var r = GingerGCode.Build(tp);
            foreach (var wmsg in r.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, wmsg);
            if (save)
            {
                try { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(file, "larger_print.gcode", r.Lines)); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write the file: " + ex.Message); }
            }
            Message = r.Lines.Count + " lines";
            da.SetDataList(0, r.Lines);
            da.SetData(1, r.Info);
        }
    }

    public class ToolpathToRobotLinesComponent : SlicerComponent
    {
        public ToolpathToRobotLinesComponent()
            : base("Toolpath to Robot Lines", "PTP",
                  "Writes the toolpath as robot moves for the DXR Generator: one PTP line per point, with the matching extrusion and speed. The tool orientation A, B, C is the same for every point: use it for flat layers with a vertical tool and check it against your robot cell before the first run. With the Robots plug-in, take its program instead.") { }

        public override Guid ComponentGuid => new Guid("7C4E9A21-63B8-4D15-A0F2-1E8B5C7D3F92");
        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("SlicerOpenIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Toolpath", "T", "Toolpath from Start and End", GH_ParamAccess.item);
            p.AddNumberParameter("A", "A", "Tool orientation A in degrees (rotation about Z), as your robot cell expects it for a vertical tool", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("B", "B", "Tool orientation B in degrees (rotation about Y)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("C", "C", "Tool orientation C in degrees (rotation about X)", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Robot Path", "Path", "One PTP line per path point: connect to Robot Path of the DXR Generator", GH_ParamAccess.list);
            p.AddNumberParameter("Extrusion", "E", "Extrusion per line, plus one closing value (the DXR Generator drops the last value)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "F", "Speed in mm/min per line, plus one closing value", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var tp = SlicerIO.Get<Toolpath>(da, 0);
            if (tp == null || tp.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a Toolpath."); return; }
            double a = 0, b = 0, c = 0;
            da.GetData(1, ref a); da.GetData(2, ref b); da.GetData(3, ref c);
            var r = RobotLines.Build(tp, a, b, c);
            if (r.Tilted) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "This path has tilted tool axes (angled, non-planar or multi-axis). The lines carry one fixed orientation, so the tool will not follow the tilt. Use the Planes of Extrusion and Speed with your robot programming instead.");
            if (r.Speed.Any(v => v <= 0)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A move has speed 0; the machine would stop there.");
            // the DXR Generator of this plug-in removes the last extrusion and speed value: give it one to remove
            var e = new List<double>(r.Extrusion) { 0 }; var f = new List<double>(r.Speed) { r.Speed.Count > 0 ? r.Speed[r.Speed.Count - 1] : tp.Settings.Speed };
            Message = r.Lines.Count + " moves";
            da.SetDataList(0, r.Lines);
            da.SetDataList(1, e);
            da.SetDataList(2, f);
        }
    }

}
