using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Standard;
using Grasshopper2.SpecialObjects;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;
using Family = Grasshopper2.Eto.Drawing.OpenColor.Family;

namespace LARGERslicer.GH2
{
    /// <summary>
    /// Puts eight complete, working examples on the canvas and removes itself: the same parts as
    /// Quick Start in the Grasshopper 1 plug-in, standing side by side on a bed of 1000 x 1000 mm.
    /// </summary>
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d5001")]
    public sealed class QuickStartComponent : Component
    {
        private bool _done;

        public QuickStartComponent() : base(Io.Name("Quick Start", "Place this on the canvas to get eight complete, working examples, each in its own group: 1 flat layers from the part to the file for the machine (G-code for the Ginger One, DXR for a robot with the Weber DXR, a small test print for a desktop printer), 2 non-planar layers along a guide surface, 3 multi-axis layers along a guide curve, 4 a part with an opening and wave infill, 5 angled layers, 6 Easy Slicer, 7 a non-planar head played on the Ginger One, 8 a vase with a surface pattern. Replace the demo parts with your own geometry.", Io.Slicer, Group.Start))
        {
            DocumentChanged += Placed;
        }

        // read from a file: the examples were built long ago
        public QuickStartComponent(IReader reader) : base(reader) { _done = true; }
        protected override IIcon IconInternal => Io.Icon("SlicerQuickStartIcon");

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
            // typed inputs (int, number, bool) are Parameter<T>, not Parameter: Io.SetValue sets both
            Action<Component, int, object> set = (c, i, value) => Io.SetValue(c.Parameters.Input(i), value);
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
            write("Eight examples on one print bed. Large-format 3D printing as one continuous line, FH Muenster.", -700, -220, false);
            write("00. SETTINGS FOR ALL EXAMPLES", -80, -190, true);
            write("Bead width and layer height drive everything else.", -80, -150, false);
            var hide = new List<IDocumentObject>();
            var finished = new List<Component>(); // the last component of every example, for the bed check
            const float col = 230; // from one component to the next

            // ---- settings, shared by all examples, and the bed every path is checked against
            var settings = put(new PrintSettingsComponent(), 0, 130); // tall: below its title
            set(settings, 3, 4);     // Machine Profile: the demo is set up for the Ginger One
            set(settings, 9, 25.0);  // First Layer Speed: the Ginger prints the first layer at 25 mm/s
            var bed = put(new PrintBedComponent(), 2 * col, 130);
            group("PRINT SETTINGS AND PRINT BED", Family.Gray, new IDocumentObject[] { settings, bed });

            // ---- 1 flat layers, with the files for the machines
            float y1 = 880; // below the tall Print Settings
            section("01. FLAT LAYERS  -  FROM THE PART TO THE FILE", "The normal case. Replace 'Your part' with your own geometry, read Path Check, then take the file for your machine: output A, B or C.", y1 - 200);
            var part1 = geometry("Your part", "Replace this with your own geometry: standing on the XY plane, in millimetres, on the positive side of the origin.", DemoParts.Hull(), 0, y1);
            var slice = put(new SliceComponent(), col, y1);
            var walls = put(new WallsComponent(), 2 * col, y1);
            var bottom = put(new BottomFillComponent(), 3 * col, y1);
            var ribs = put(new InfillRibsComponent(), 4 * col, y1);
            var path = put(new ContinuousPathComponent(), 5 * col, y1);
            var round = put(new RoundCornersComponent(), 6 * col, y1);
            var finish = put(new StartEndComponent(), 7 * col, y1);
            var check = put(new PathCheckComponent(), 8 * col + 40, y1 + 130);
            var values = put(new ExtrusionSpeedComponent(), 8 * col + 40, y1 - 110);
            feed(part1, slice, 0);
            wire(slice, 0, walls, 0); wire(walls, 0, bottom, 0); wire(bottom, 0, ribs, 0); wire(ribs, 0, path, 0);
            wire(path, 0, round, 0); wire(round, 0, finish, 0);
            wire(finish, 0, check, 0); wire(finish, 0, values, 0);
            foreach (var c in new[] { slice, walls, bottom, ribs, path }) wire(settings, 0, c, 1);
            set(ribs, 3, 150.0); // ribs every 150 mm
            set(ribs, 5, true);  // two ribs, one from each wall, meeting in the middle
            finished.Add(finish);
            hide.AddRange(new IDocumentObject[] { part1, slice, walls, bottom, ribs, path, round, values });
            group("INPUT GEOMETRY AND SLICER", Family.Blue, new IDocumentObject[] { part1, slice, walls, bottom, ribs, path, round, finish });
            group("CHECK AND VALUES FOR THE MACHINE", Family.Green, new IDocumentObject[] { check, values });

            float xo = 10 * col + 100; // right of the checks
            var ginger = put(new GingerGCodeComponent(), xo, y1 - 330);
            wire(finish, 0, ginger, 0);
            group("OUTPUT A  -  GINGER ONE G-CODE  (set Save)", Family.Orange, new IDocumentObject[] { ginger });

            var robot = put(new RobotArmComponent(), xo, y1 - 60);
            var tool = put(new RobotToolComponent(), xo, y1 + 110);
            var cell = put(new RobotCellComponent(), xo + col, y1);
            var program = put(new RobotProgramComponent(), xo + 2 * col, y1);
            var machine = put(new MachineSettingsComponent(), xo + 2 * col, y1 + 300);
            var dxr = put(new DxrGeneratorComponent(), xo + 3 * col + 40, y1 + 60);
            // the cell of the FH Muenster: where the robot stands, the bed corner, and what stands around it
            var cellGeo = put(new CellGeometryComponent(), xo - col, y1 + 120);
            wire(cellGeo, 2, robot, 2); wire(cellGeo, 3, cell, 2); wire(cellGeo, 0, cell, 3);
            // the extruder of the cell as its definition sets it up (AE20: TCP = YZ plane at -460, 3, 108, 46 kg)
            set(tool, 0, "AE20_Extruder");
            set(tool, 1, new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis));
            set(tool, 2, 46.0);
            wire(robot, 0, cell, 0); wire(tool, 0, cell, 1);
            wire(finish, 0, program, 0); wire(cell, 0, program, 1);
            wire(program, 0, dxr, 0); wire(program, 1, dxr, 1); wire(program, 2, dxr, 2); wire(machine, 0, dxr, 3);
            // play the program: the robot at any moment of the print
            var simulate = put(new RobotSimulationComponent(), xo + 2 * col + 80, y1 + 600);
            wire(program, 10, simulate, 0);
            hide.Add(robot); hide.Add(program); // the simulation shows the robot
            var simSlider = new Grasshopper2.Parameters.Special.NumberSliderObject("Simulation (0 = start, 1 = end)", new Grasshopper2.UI.UiNumber(3, 0.5m, 0m, 1m));
            place(simSlider, xo + col + 20, y1 + 560);
            Connections.Connect(simSlider, simulate.Parameters.Input(1), null);
            group("OUTPUT B  -  ROBOT CELL, ROBOT PROGRAM AND DXR GENERATOR  (the cell of the FH Muenster: robot on its pedestal, AE20 extruder)", Family.Red, new IDocumentObject[] { cellGeo, robot, tool, cell, program, machine, dxr });
            group("ROBOT SIMULATION  -  move the slider: the robot runs the print in the Rhino viewport", Family.Orange, new IDocumentObject[] { simulate, simSlider });

            var desktop = put(new DesktopTestPrintComponent(), xo - 40, y1 + 640);
            wire(finish, 0, desktop, 0);
            hide.Add(desktop);

            // one name for all files of this part
            var partName = new TextParameter("Part name", "Part name", "Goes into every file name: GIN or DXR, date and time, this name, weight and print time.", Access.Item);
            partName.Set(new[] { "Test Object" });
            place(partName, 8 * col + 40, y1 - 330);
            feed(partName, ginger, 3); feed(partName, program, 3); feed(partName, desktop, 10);
            Connections.Connect(program.Parameters.Output(8), dxr.Parameters.Input(5), null);
            group("PART NAME  -  goes into every file name", Family.Gray, new IDocumentObject[] { partName });
            group("OUTPUT C  -  DESKTOP TEST PRINT  (choose your printer, set Save)", Family.Violet, new IDocumentObject[] { desktop });

            // ---- examples 2 to 5: part (and guide) -> slice -> walls (-> infill) -> path -> round -> start and end -> check
            Func<float, IParameter, IParameter, Component, double, List<IDocumentObject>> chain = (y, part, guide, infill, angle) =>
            {
                var sl = put(new SliceComponent(), col, y);
                var wa = put(new WallsComponent(), 2 * col, y);
                float dx = infill != null ? col : 0;
                if (infill != null) place(infill, 3 * col, y);
                var pa = put(new ContinuousPathComponent(), 3 * col + dx, y);
                var ro = put(new RoundCornersComponent(), 4 * col + dx, y);
                var en = put(new StartEndComponent(), 5 * col + dx, y);
                var ch = put(new PathCheckComponent(), 6 * col + dx + 40, y);
                feed(part, sl, 0);
                if (guide != null) feed(guide, sl, 3);
                if (Math.Abs(angle) > 1e-9) set(sl, 2, angle);
                wire(sl, 0, wa, 0);
                Component last = wa;
                if (infill != null) { wire(wa, 0, infill, 0); wire(settings, 0, infill, 1); last = infill; }
                wire(last, 0, pa, 0); wire(pa, 0, ro, 0); wire(ro, 0, en, 0); wire(en, 0, ch, 0);
                foreach (var c in new[] { sl, wa, pa }) wire(settings, 0, c, 1);
                finished.Add(en);
                var members = new List<IDocumentObject> { part, sl, wa, pa, ro, en, ch };
                if (guide != null) members.Add(guide);
                if (infill != null) members.Add(infill);
                hide.AddRange(new IDocumentObject[] { part, sl, wa, pa, ro });
                if (infill != null) hide.Add(infill);
                return members;
            };

            float y2 = y1 + 760;
            section("02. NON-PLANAR LAYERS", "The layers follow a guide surface. The first layer lies flat on the bed, the last one follows the top of the part. Draw the guide surface over your part.", y2);
            Brep npPart, npGuide; DemoParts.NonPlanar(new Point3d(190, 700, 0), out npPart, out npGuide);
            var part2 = geometry("Part", "The part of example 2.", npPart, 0, y2 - 40);
            var guide2 = geometry("Guide surface", "The layers follow this surface. Draw it over your part.", npGuide, 0, y2 + 60);
            group("PART, GUIDE SURFACE AND SLICER", Family.Yellow, chain(y2, part2, guide2, null, 0));

            float y3 = y2 + 420;
            section("03. MULTI-AXIS LAYERS", "The layers stand square to a guide curve, for parts that grow along a bent axis (robot with tilting tool). Draw the axis of your part as the guide curve.", y3);
            Brep maPart; Curve maGuide; DemoParts.MultiAxis(new Point3d(445, 700, 0), out maPart, out maGuide);
            var part3 = geometry("Part", "The part of example 3.", maPart, 0, y3 - 40);
            var guide3 = geometry("Guide curve", "The layers stand square to this curve. Draw the axis of your part.", maGuide, 0, y3 + 60);
            group("PART, GUIDE CURVE AND SLICER", Family.Grape, chain(y3, part3, guide3, null, 0));

            float y4 = y3 + 420;
            section("04. PART WITH AN OPENING", "Infill Wave joins the wall of the opening and the outer wall into one line, so no move inside the part runs without extrusion. Wavelength 60 mm here; try Shape Truss or Meander.", y4);
            var part4 = geometry("Part", "The part of example 4.", DemoParts.Plate(new Point3d(820, 700, 0)), 0, y4);
            var wave4 = new InfillWaveComponent();
            var row4 = chain(y4, part4, null, wave4, 0);
            set(wave4, 3, 60.0); // waves of 60 mm, so they meet the wall of the opening
            group("PART AND SLICER WITH WAVE INFILL", Family.Teal, row4);

            float y5 = y4 + 420;
            section("05. ANGLED LAYERS", "Slice with Angle 45: this block leans 55 degrees, more than a bead can overhang in flat layers. With leaning layers every bead rests on the one below. Needs a robot or a tilted bed.", y5);
            var part5 = geometry("Part", "The part of example 5.", DemoParts.Lean(new Point3d(785, 380, 0)), 0, y5);
            group("PART AND SLICER WITH ANGLE 45", Family.Cyan, chain(y5, part5, null, null, 45));

            // ---- 6 Easy Slicer: the whole chain in one component, a vase printed as a spiral
            float y6 = y5 + 420;
            section("06. EASY SLICER  -  ONE COMPONENT", "The whole chain in one block, here a vase printed as a spiral. Choose nozzle, walls, infill, solid layers, angle and brim on its inputs, then connect Toolpath to an output.", y6);
            var part6 = geometry("Part", "The part of example 6: a vase in the free corner of the bed, one wall.", new Cylinder(new Circle(new Plane(new Point3d(935, 515, 0), Vector3d.ZAxis), 38), 140).ToBrep(true, true), 0, y6);
            var easy = put(new EasySlicerComponent(), col, y6 + 120);
            var check6 = put(new PathCheckComponent(), 2 * col + 60, y6);
            feed(part6, easy, 0);
            set(easy, 4, 4);     // Machine Profile: Ginger One, as the other examples
            set(easy, 11, true); // Spiral: one line that climbs all the time
            set(easy, 12, 1);    // Nozzle: 3 mm flat nozzle, wide bead (tested)
            wire(easy, 0, check6, 0);
            finished.Add(easy);
            hide.Add(part6);
            group("EASY SLICER  -  set nozzle, infill, walls, angle on its inputs", Family.Lime, new IDocumentObject[] { part6, easy, check6 });

            // ---- 7 non-planar head on the Ginger One: layers that follow the surface, as the head of 2024
            float y7 = y6 + 820; // Easy Slicer is tall: the next example starts below it
            section("07. NON-PLANAR HEAD  -  GINGER ADDITIVE · GINGER ONE (KLIPPER)", "A head sliced along its own surface, as the head of 2024 (Slice: Follow the surface): rings from the neck to the crown, the nozzle vertical, one spiral. Printer Simulation plays the print on the Ginger One: set Play.", y7);
            float r7 = y7 + 100; // the tall blocks reach upwards: keep them below the lines of text
            var part7 = geometry("Head", "The part of example 7: a head standing in the free middle of the bed.", DemoParts.Head(new Point3d(480, 470, 0)), 0, r7);
            // its own settings, as the head of 2024: bead 4, layer 1.75, 20 mm/s, first layer 0.9 mm at 25 mm/s;
            // the small crown rings may slow down far (Min Speed 0.2 mm/s) so every lap gets its cooling time
            var settings7 = put(new PrintSettingsComponent(), col, r7 + 40);
            set(settings7, 0, 4.0); set(settings7, 1, 1.75); set(settings7, 2, 20.0); set(settings7, 3, 4);
            set(settings7, 7, 0.9); set(settings7, 9, 25.0); set(settings7, 10, 0.0); set(settings7, 14, 0.2);
            var slice7 = put(new SliceComponent(), 2 * col + 40, r7);
            var walls7 = put(new WallsComponent(), 3 * col + 40, r7);
            var path7 = put(new ContinuousPathComponent(), 4 * col + 40, r7);
            var finish7 = put(new StartEndComponent(), 5 * col + 40, r7);
            var gcode7 = put(new MachineGCodeComponent(), 6 * col + 80, r7);
            var machine7 = put(new MachineComponent(), 5 * col + 40, r7 + 300);
            var check7 = put(new PathCheckComponent(), 6 * col + 80, r7 + 230);
            var np7 = put(new NonPlanarCheckComponent(), 6 * col + 80, r7 + 420);
            var sim7 = put(new PrinterSimulationComponent(), 7 * col + 120, r7);
            feed(part7, slice7, 0);
            foreach (var c in new[] { slice7, walls7, path7 }) wire(settings7, 0, c, 1);
            set(slice7, 4, 3); // Guide Mode: follow the surface (no guide)
            set(slice7, 5, 1); // Tool Axis: the nozzle stays vertical
            wire(slice7, 0, walls7, 0); wire(walls7, 0, path7, 0);
            set(path7, 2, 0);      // Mode: spiral, one line that climbs all the time
            set(path7, 7, 30.0);   // Min Layer Time: the crown rings need time to cool
            set(path7, 8, 330.0);  // Max Volume Rate: the Ginger delivers about 330 mm3/s
            wire(path7, 0, finish7, 0);
            set(machine7, 0, 2);   // Machine: Ginger Additive · Ginger One (Klipper)
            wire(finish7, 0, gcode7, 0); wire(machine7, 0, gcode7, 1);
            wire(finish7, 0, check7, 0); wire(finish7, 0, np7, 0);
            wire(gcode7, 4, sim7, 1); // Simulation: the print with its machine and times
            set(sim7, 2, 0.6);
            finished.Add(finish7);
            // one Ginger in the viewport: the one of Printer Simulation
            hide.AddRange(new IDocumentObject[] { part7, slice7, walls7, path7, machine7, gcode7, np7 });
            group("HEAD SLICED ALONG ITS SURFACE, ONE SPIRAL, CHECKS  -  own settings as the head of 2024; then set Save on Machine G-code", Family.Indigo, new IDocumentObject[] { part7, settings7, slice7, walls7, path7, finish7, machine7, gcode7, check7, np7 });
            group("PRINTER SIMULATION  -  set Play, or move Pose", Family.Orange, new IDocumentObject[] { sim7 });

            // ---- 8 surface pattern: a vase with waves in its wall
            float y8 = y7 + 860;
            section("08. SURFACE PATTERN  -  A VASE WITH WAVES", "Surface Pattern between Continuous Path and Start and End pulls the wall out in a rhythm: Knit, Ribs, Waves, Zigzag, Dots, Loops or a picture. Choose it on Pattern; the path stays one line.", y8);
            float r8 = y8 + 100; // the tall blocks reach upwards: keep them below the lines of text
            var part8 = geometry("Vase", "The part of example 8.", DemoParts.Vase(new Point3d(200, 470, 0)), 0, r8);
            var slice8 = put(new SliceComponent(), col, r8);
            var walls8 = put(new WallsComponent(), 2 * col, r8);
            var path8 = put(new ContinuousPathComponent(), 3 * col, r8);
            var pattern8 = put(new SurfacePatternComponent(), 4 * col, r8);
            var finish8 = put(new StartEndComponent(), 5 * col, r8);
            var check8 = put(new PathCheckComponent(), 6 * col + 40, r8);
            feed(part8, slice8, 0);
            foreach (var c in new[] { slice8, walls8, path8 }) wire(settings, 0, c, 1);
            wire(slice8, 0, walls8, 0); wire(walls8, 0, path8, 0); wire(path8, 0, pattern8, 0);
            set(pattern8, 1, 2);     // Waves
            set(pattern8, 2, 2.5);   // Amplitude: half a bead, so every bead rests on the one below
            set(pattern8, 3, 30.0);  // Pitch
            set(pattern8, 5, 0.05);  // Phase Shift: a slow diagonal
            wire(pattern8, 0, finish8, 0); wire(finish8, 0, check8, 0);
            finished.Add(finish8);
            hide.AddRange(new IDocumentObject[] { part8, slice8, walls8, path8, pattern8 });
            group("VASE, SLICER AND SURFACE PATTERN  -  try Knit, Ribs or Loops on Pattern", Family.Pink, new IDocumentObject[] { part8, slice8, walls8, path8, pattern8, finish8, check8 });

            // every finished path on the one bed: the examples must not touch each other
            foreach (var f in finished) wire(f, 0, bed, 3);

            // ---- read me first
            var note = new ScribbleObject(
                "INSTRUCTIONS\n\n" +
                "The eight examples stand side by side on one print bed of 1000 x 1000 mm, origin in the front left corner. Look at the Rhino viewport; Print Bed checks that no two paths touch.\n\n" +
                "1  Replace 'Your part' (example 1) with your own geometry: standing on the XY plane, in millimetres.\n" +
                "2  Set Bead Width and Layer Height in Print Settings (or pick a Nozzle). Everything else follows from them.\n" +
                "3  Read Path Check. OK = the path can be printed. If not, it says what to change.\n" +
                "4  Take the file for your machine: Output A for the Ginger One, Output B for a robot with the Weber DXR.\n" +
                "5  Before the big print: Output C writes the same path small for a desktop printer.\n\n" +
                "File names are made for you: GIN or DXR, date and time, the part name, weight and print time, e.g. DXR_2611051216_Test Object_2668g_53min.dxr.\n" +
                "Inputs with a list of choices (Nozzle, Brim, Mode, Pattern, Machine ...) offer their presets on the input. Seam: 'Seam Gap' of 'Continuous Path'. Brim: 'Brim' of 'Start and End'.\n" +
                "Other infill: replace 'Infill Ribs' by Infill Wave, Cells, Chambers or Sketch.\n" +
                "Just want a path quickly? Example 6: the single component 'Easy Slicer'.\n" +
                "Printer with a vertical nozzle and curved layers (Ginger One): example 7, played on the machine in Printer Simulation.\n" +
                "Textures in the wall: example 8, Surface Pattern between Continuous Path and Start and End.");
            note.WrapWidth = 520;
            place(note, -440, 80);
            // top left corner under the title (the canvas centres a new object on its point)
            RectangleF nb = note.Attributes.Bounds;
            note.Attributes.Move(origin.X - 700 - nb.X, origin.Y - 150 - nb.Y);

            foreach (var h in hide) h.Display = ObjectDisplay.Hidden;

            doc.Objects.Remove(this);
            doc.Solution.Start(SolutionMode.Regular);
        }
    }
}
