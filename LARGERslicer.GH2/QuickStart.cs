using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Standard;
using Grasshopper2.SpecialObjects;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;
using Family = Grasshopper2.Eto.Drawing.OpenColor.Family;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Puts five complete, working examples on the canvas and removes itself: the same parts as
    /// Quick Start in the Grasshopper 1 plug-in, standing side by side on a bed of 1000 x 1000 mm.
    /// </summary>
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d5001")]
    public sealed class QuickStartComponent : Component
    {
        private bool _done;

        public QuickStartComponent() : base(Io.Name("Quick Start", "Place this on the canvas to get five complete, working examples, each in its own group: 1 flat layers from the part to the file for the machine (G-code for the Ginger One, DXR for a robot with the Weber DXR, a small test print for a desktop printer), 2 non-planar layers along a guide surface, 3 multi-axis layers along a guide curve, 4 a part with an opening and wave infill, 5 angled layers. Replace the demo parts with your own geometry.", Io.Slicer, 0))
        {
            DocumentChanged += Placed;
        }

        // read from a file: the examples were built long ago
        public QuickStartComponent(IReader reader) : base(reader) { _done = true; }

        protected override void AddInputs(InputAdder inputs) { }
        protected override void AddOutputs(OutputAdder outputs) { }
        protected override void Process(IDataAccess access) { }

        private void Placed(object sender, ObjectDocumentEventArgs e)
        {
            if (_done || e.NewDocument == null) return;
            // build once the canvas has finished adding this component
            Eto.Forms.Application.Instance.AsyncInvoke(() =>
            {
                Document doc = Document;
                if (_done || doc == null || !ReferenceEquals(doc.Objects.Find(InstanceId), this)) return;
                _done = true;
                try { Build(doc); }
                catch (Exception ex) { Rhino.RhinoApp.WriteLine("LARGER Quick Start: " + ex.Message); }
            });
        }

        private void Build(Document doc)
        {
            PointF origin = Attributes.Pivot;
            Func<IDocumentObject, float, float, IDocumentObject> place = (obj, dx, dy) =>
            {
                doc.Objects.Add(obj, new PointF(origin.X + dx, origin.Y + dy));
                return obj;
            };
            Func<Component, float, float, Component> put = (c, dx, dy) => (Component)place(c, dx, dy);
            Action<Component, int, Component, int> wire = (from, o, to, i) => Connections.Connect(from.Parameters.Output(o), to.Parameters.Input(i), null);
            Action<IParameter, Component, int> feed = (from, to, i) => Connections.Connect(from, to.Parameters.Input(i), null);
            Action<Component, int, object> set = (c, i, value) => { var p = c.Parameters.Input(i) as Parameter; if (p != null) p.Set(new[] { value }); };
            Func<string, string, object, float, float, GenericParameter> geometry = (name, info, value, dx, dy) =>
            {
                var p = new GenericParameter(name, name, info, Access.Twig);
                if (value != null) p.Set(new[] { value });
                place(p, dx, dy);
                return p;
            };
            Action<string, Family, IEnumerable<IDocumentObject>> group = (title, colour, members) =>
            {
                var g = new GroupObject(colour, members.ToArray()) { UserName = title };
                doc.Objects.Add(g);
            };
            // Titles as on the canvas of the FH Muenster definition: a numbered title in capitals,
            // one line below it that says what the section is for, then the named blocks.
            Action<string, float, float, bool> write = (text, dx, dy, bold) =>
            {
                var sc = new ScribbleObject(text);
                if (bold) sc.TextStyle = Eto.Drawing.FontStyle.Bold;
                place(sc, dx, dy);
                // the canvas centres a new object on the point: titles start at the left edge instead
                RectangleF b = sc.Attributes.Bounds;
                sc.Attributes.Move(origin.X + dx - b.X, origin.Y + dy - b.Y);
            };
            Action<string, string, float> section = (title, about, y) => { write(title, -80, y - 190, true); write(about, -80, y - 150, false); };
            write("LARGER.slicer  -  QUICK START", -700, -260, true);
            write("Five examples on one print bed. Large-format 3D printing as one continuous line, FH Muenster.", -700, -220, false);
            write("00. SETTINGS FOR ALL EXAMPLES", -80, -190, true);
            write("Bead width and layer height drive everything else.", -80, -150, false);
            var hide = new List<IDocumentObject>();
            const float col = 230; // from one component to the next

            // ---- settings, shared by all examples
            var settings = put(new PrintSettingsComponent(), 0, 0);
            set(settings, 3, 4); // the demo is set up for the Ginger One
            group("PRINT SETTINGS", Family.Gray, new IDocumentObject[] { settings });

            // ---- 1 flat layers, with the files for the machines
            float y1 = 620;
            section("01. FLAT LAYERS  -  FROM THE PART TO THE FILE", "The normal case. Replace 'Your part' with your own geometry, read Path Check, then take the file for your machine: output A, B or C.", y1 - 300);
            var part1 = geometry("Your part", "Replace this with your own geometry: standing on the XY plane, in millimetres, on the positive side of the origin.", DemoParts.Hull(), 0, y1);
            var slice = put(new SliceComponent(), col, y1);
            var walls = put(new WallsComponent(), 2 * col, y1);
            var bottom = put(new BottomFillComponent(), 3 * col, y1);
            var infill = put(new InfillComponent(), 4 * col, y1);
            var path = put(new ContinuousPathComponent(), 5 * col, y1);
            var finish = put(new FinishPathComponent(), 6 * col, y1);
            var check = put(new PathCheckComponent(), 7 * col + 40, y1 + 110);
            var values = put(new ExtrusionSpeedComponent(), 7 * col + 40, y1 - 110);
            feed(part1, slice, 0);
            wire(slice, 0, walls, 0); wire(walls, 0, bottom, 0); wire(bottom, 0, infill, 0); wire(infill, 0, path, 0); wire(path, 0, finish, 0);
            wire(finish, 0, check, 0); wire(finish, 0, values, 0);
            foreach (var c in new[] { slice, walls, bottom, infill, path }) wire(settings, 0, c, 1);
            set(infill, 2, 0); set(infill, 3, 150.0); // ribs every 150 mm
            hide.AddRange(new IDocumentObject[] { part1, slice, walls, bottom, infill, path, values });
            group("INPUT GEOMETRY AND SLICER", Family.Blue, new IDocumentObject[] { part1, slice, walls, bottom, infill, path, finish });
            group("CHECK AND VALUES FOR THE MACHINE", Family.Green, new IDocumentObject[] { check, values });

            float xo = 8 * col + 140;
            var ginger = put(new GingerGCodeComponent(), xo, y1 - 330);
            wire(finish, 0, ginger, 0);
            group("OUTPUT A  -  GINGER ONE G-CODE  (set Save)", Family.Orange, new IDocumentObject[] { ginger });

            var robot = put(new RobotArmComponent(), xo, y1 - 60);
            var tool = put(new RobotToolComponent(), xo, y1 + 110);
            var cell = put(new RobotCellComponent(), xo + col, y1);
            var program = put(new RobotProgramComponent(), xo + 2 * col, y1);
            var machine = put(new MachineSettingsComponent(), xo + 2 * col, y1 + 300);
            var dxr = put(new DxrGeneratorComponent(), xo + 3 * col + 40, y1 + 60);
            // demo values: the robot stands 1 m beside the bed, in the middle of its side and 400 mm lower; the nozzle tip is 300 mm in front of the flange
            set(robot, 2, new Plane(new Point3d(-1000, DemoParts.BedSize / 2, -400), Vector3d.XAxis, Vector3d.YAxis));
            set(tool, 1, new Plane(new Point3d(0, 0, 300), Vector3d.XAxis, Vector3d.YAxis));
            wire(robot, 0, cell, 0); wire(tool, 0, cell, 1);
            wire(finish, 0, program, 0); wire(cell, 0, program, 1);
            wire(program, 0, dxr, 0); wire(program, 1, dxr, 1); wire(program, 2, dxr, 2); wire(machine, 0, dxr, 3);
            group("OUTPUT B  -  ROBOT CELL, ROBOT PROGRAM AND DXR GENERATOR  (demo values: put in your tool and where robot and bed stand)", Family.Red, new IDocumentObject[] { robot, tool, cell, program, machine, dxr });

            var desktop = put(new DesktopTestPrintComponent(), xo, y1 + 420);
            wire(finish, 0, desktop, 0);
            hide.Add(desktop);

            // one name for all files of this part
            var partName = new TextParameter("Part name", "Part name", "Goes into every file name: GIN or DXR, date and time, this name, weight and print time.", Access.Item);
            partName.Set(new[] { "Test Object" });
            place(partName, 7 * col + 40, y1 - 330);
            feed(partName, ginger, 3); feed(partName, program, 3); feed(partName, desktop, 10);
            Connections.Connect(program.Parameters.Output(8), dxr.Parameters.Input(5), null);
            group("PART NAME  -  goes into every file name", Family.Gray, new IDocumentObject[] { partName });
            group("OUTPUT C  -  DESKTOP TEST PRINT  (choose your printer, set Save)", Family.Violet, new IDocumentObject[] { desktop });

            // ---- the other examples: part (and guide) -> slice -> walls (-> infill) -> path -> finish -> check
            Func<float, IParameter, IParameter, bool, double, List<IDocumentObject>> chain = (y, part, guide, wave, angle) =>
            {
                var sl = put(new SliceComponent(), col, y);
                var wa = put(new WallsComponent(), 2 * col, y);
                Component fi = wave ? put(new InfillComponent(), 3 * col, y) : null;
                float dx = wave ? col : 0;
                var pa = put(new ContinuousPathComponent(), 3 * col + dx, y);
                var en = put(new FinishPathComponent(), 4 * col + dx, y);
                var ch = put(new PathCheckComponent(), 5 * col + dx + 40, y);
                feed(part, sl, 0);
                if (guide != null) feed(guide, sl, 3);
                if (Math.Abs(angle) > 1e-9) set(sl, 2, angle);
                wire(sl, 0, wa, 0);
                Component last = wa;
                if (fi != null) { wire(wa, 0, fi, 0); wire(settings, 0, fi, 1); set(fi, 2, 3); set(fi, 3, 60.0); last = fi; }
                wire(last, 0, pa, 0); wire(pa, 0, en, 0); wire(en, 0, ch, 0);
                foreach (var c in new[] { sl, wa, pa }) wire(settings, 0, c, 1);
                var members = new List<IDocumentObject> { part, sl, wa, pa, en, ch };
                if (guide != null) members.Add(guide);
                if (fi != null) members.Add(fi);
                hide.AddRange(new IDocumentObject[] { part, sl, wa, pa });
                if (fi != null) hide.Add(fi);
                return members;
            };

            float y2 = y1 + 760;
            section("02. NON-PLANAR LAYERS", "The layers follow a guide surface. The first layer lies flat on the bed, the last one follows the top of the part. Draw the guide surface over your part.", y2);
            Brep npPart, npGuide; DemoParts.NonPlanar(new Point3d(190, 700, 0), out npPart, out npGuide);
            var part2 = geometry("Part", "The part of example 2.", npPart, 0, y2 - 40);
            var guide2 = geometry("Guide surface", "The layers follow this surface. Draw it over your part.", npGuide, 0, y2 + 60);
            group("PART, GUIDE SURFACE AND SLICER", Family.Yellow, chain(y2, part2, guide2, false, 0));

            float y3 = y2 + 420;
            section("03. MULTI-AXIS LAYERS", "The layers stand square to a guide curve, for parts that grow along a bent axis (robot with tilting tool). Draw the axis of your part as the guide curve.", y3);
            Brep maPart; Curve maGuide; DemoParts.MultiAxis(new Point3d(445, 700, 0), out maPart, out maGuide);
            var part3 = geometry("Part", "The part of example 3.", maPart, 0, y3 - 40);
            var guide3 = geometry("Guide curve", "The layers stand square to this curve. Draw the axis of your part.", maGuide, 0, y3 + 60);
            group("PART, GUIDE CURVE AND SLICER", Family.Grape, chain(y3, part3, guide3, false, 0));

            float y4 = y3 + 420;
            section("04. PART WITH AN OPENING", "The wave infill joins the wall of the opening and the outer wall into one line, so no move inside the part runs without extrusion. Wavelength 60 mm here.", y4);
            var part4 = geometry("Part", "The part of example 4.", DemoParts.Plate(new Point3d(820, 700, 0)), 0, y4);
            group("PART AND SLICER WITH WAVE INFILL", Family.Teal, chain(y4, part4, null, true, 0));

            float y5 = y4 + 420;
            section("05. ANGLED LAYERS", "Slice with Angle 45: this block leans 55 degrees, more than a bead can overhang in flat layers. Needs a machine that can tilt the tool or the bed.", y5);
            var part5 = geometry("Part", "The part of example 5.", DemoParts.Lean(new Point3d(785, 380, 0)), 0, y5);
            group("PART AND SLICER WITH ANGLE 45", Family.Cyan, chain(y5, part5, null, false, 45));

            // ---- read me first
            var note = new ScribbleObject(
                "INSTRUCTIONS\n\n" +
                "The five examples stand side by side on one print bed of 1000 x 1000 mm, origin in the front left corner. Look at the Rhino viewport.\n\n" +
                "1  Replace 'Your part' (example 1) with your own geometry: standing on the XY plane, in millimetres.\n" +
                "2  Set Bead Width and Layer Height in Print Settings. Everything else follows from them.\n" +
                "3  Read Path Check. OK = the path can be printed. If not, it says what to change.\n" +
                "4  Take the file for your machine: Output A for the Ginger One, Output B for a robot with the Weber DXR.\n" +
                "5  Before the big print: Output C writes the same path small for a desktop printer.\n\n" +
                "File names are made for you: GIN or DXR, date and time, the part name, weight and print time, e.g. DXR_2611051216_Test Object_2668g_53min.dxr.\n" +
                "Brim and seam: inputs Brim of 'Round and Lead' and Seam Gap of 'Continuous Path'. Another machine: 'Machine' and 'Machine G-code'.\n\n" +
                "Just want a path quickly? Use the single component 'Easy Slicer' instead of this chain.");
            note.WrapWidth = 520;
            place(note, -440, 80);

            foreach (var h in hide) h.Display = ObjectDisplay.Hidden;

            doc.Objects.Remove(this);
            doc.Solution.Start(SolutionMode.Regular);
        }
    }
}
