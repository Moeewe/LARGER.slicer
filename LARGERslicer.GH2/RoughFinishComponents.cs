using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== relief
    [IoId("a7c2f1d4-58e3-4b9a-9c06-1e7d3b5f8a09")]
    public sealed class CncReliefComponent : Component
    {
        public CncReliefComponent() : base(Io.Name("CNC - Relief", "Examples to mill: a site model, a facade, scales, Voronoi cells, a gyroid, vaults and other patterns as a mesh. Choose one, set its size, connect it to Model of CNC - Rough and Finish. Wave bends the whole panel.", Io.Cnc, 0)) { }
        public CncReliefComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCReliefIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Pattern", "Pattern", ReliefPatterns.Help).Set(0).Choices(ReliefPatterns.Names);
            inputs.AddNumber("Length", "Length", "Size in X in mm.").Set(240.0).Values(160, 240, 300, 400, 600);
            inputs.AddNumber("Width", "Width", "Size in Y in mm.").Set(160.0).Values(100, 160, 220, 300, 400);
            inputs.AddNumber("Height", "Height", "Height of the whole relief in mm.").Set(14.0).Values(6, 10, 14, 18, 25);
            inputs.AddNumber("Cell Size", "Size", "Size of one house, window, scale or cell in mm. 0 = automatic. Below about three times the diameter of the ball the cutter no longer reaches the details.").Set(0.0).Values(0, 16, 24, 32, 40, 60);
            inputs.AddNumber("Wave", "Wave", "Bends the panel: 0 = flat, 1 = its waves are as high as the pattern, -1 = automatic.").Set(-1.0).Values(-1, 0, 0.3, 0.6, 1, 1.5);
            inputs.AddInteger("Seed", "Seed", "Another number gives another arrangement (site model, gouges, Voronoi, labyrinth).").Set(1);
            inputs.AddNumber("Ball", "Ball", "Diameter of the ball that finishes it, in mm: valleys are rounded so that this ball reaches every place. 0 = sharp valleys.").Set(6.0).Values(0, 3, 4, 6, 8);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Model", "Model", "The relief: connect it to Model of CNC - Rough and Finish.");
        }

        protected override void Process(IDataAccess access)
        {
            int kind, seed; double l, w, h, size, wave, ball;
            access.GetItem(0, out kind); access.GetItem(1, out l); access.GetItem(2, out w); access.GetItem(3, out h); access.GetItem(4, out size); access.GetItem(5, out wave); access.GetItem(6, out seed); access.GetItem(7, out ball);
            var m = ReliefPatterns.Make(kind, l, w, h, size, wave, seed, ball);
            if (m == null) { access.AddError("Relief", "Pattern is 0 to " + (ReliefPatterns.Names.Length - 1) + "; Length, Width and Height must be greater than 0."); return; }
            access.SetItem(0, m);
        }
    }

    // ============================================================== plate
    [IoId("5d0c1f7e-2c5b-4d0b-9f6e-8a1b7c3d2e01")]
    public sealed class CncPlateComponent : Component
    {
        public CncPlateComponent() : base(Io.Name("CNC - Plate", "STEP 1. The raw plate as it lies on the table of the Zünd: measure yours and type in its size. Also where the model lies in it and what it is made of. Connect it to CNC - Rough and Finish.", Io.Cnc, 0)) { }
        public CncPlateComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCPlateIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Length", "Length", "Size of the plate in X in mm. Measure the real plate.").Set(300.0).Values(200, 300, 400, 600, 800, 1200, 2500);
            inputs.AddNumber("Width", "Width", "Size of the plate in Y in mm. Measure the real plate.").Set(220.0).Values(150, 220, 300, 400, 600, 800, 1250);
            inputs.AddNumber("Thickness", "Thickness", "Thickness of the plate in mm, at most 50. Measure it with a caliper: the cutter goes down to the table by it.").Set(20.0).Values(10, 15, 19, 20, 25, 30, 40, 50);
            inputs.AddInteger("Material", "Material", MillPlate.MaterialHelp).Set(0).Choices(MillPlate.Materials);
            inputs.AddInteger("Placement", "Placement", MillPlate.PlacementHelp).Set(0).Choices(MillPlate.Placements);
            inputs.AddInteger("Height", "Height", MillPlate.HeightHelp).Set(0).Choices(MillPlate.Heights);
            inputs.AddPoint("Corner", "Corner", "Where the front left corner of the plate lies on the table, seen from the zero point of the machine (X, Y in mm).").Set(new Point3d(20, 20, 0));
            inputs.AddInteger("Vacuum", "Vacuum", "Vacuum of the table, 0 (off) to 10.").Set(5);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Plate", "Plate", "The plate: connect it to Plate of CNC - Rough and Finish.");
            outputs.AddGeneric("Box", "Box", "The plate as a box, to see where it lies.");
        }

        protected override void Process(IDataAccess access)
        {
            var m = new MillPlate(); Point3d corner;
            access.GetItem(0, out m.Length); access.GetItem(1, out m.Width); access.GetItem(2, out m.Thickness); access.GetItem(3, out m.Material);
            access.GetItem(4, out m.Placement); access.GetItem(5, out m.Height); if (access.GetItem(6, out corner)) { m.X = corner.X; m.Y = corner.Y; }
            access.GetItem(7, out m.Vacuum);
            string bad = m.Problem();
            if (bad != null) { access.AddError("Plate", bad); return; }
            access.SetItem(0, m);
            access.SetItem(1, m.Box);
        }
    }

    // ============================================================== cutter
    [IoId("321e011c-bfe0-4782-974a-2d299c7d10ed")]
    public sealed class CncCutterComponent : Component
    {
        public CncCutterComponent() : base(Io.Name("CNC - Cutter", "STEP 2. One cutter (router bit): which one it is and in which place of the magazine it sits. Everything else is filled in. Take one for roughing (thick, flat) and one for finishing (a ball). For a cutter that is not in the list: CNC - Own Cutter.", Io.Cnc, 0)) { }
        public CncCutterComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCCutterIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Cutter", "Cutter", CutterPresets.Help).Set(0).Choices(CutterPresets.Names);
            inputs.AddInteger("Magazine", "Magazine", CutterPresets.BitHelp).Set(0).Choices(CutterPresets.Bits);
            inputs.AddNumber("Step Down", "Step Down", "How deep one level goes in mm. 0 = automatic, after the material of the plate.").Set(0.0).Values(0, 1, 2, 3, 4, 6);
            inputs.AddNumber("Stepover", "Stepover", "How far the paths lie apart in mm. 0 = automatic: roughing 45 %, finishing 10 % of the diameter. Finishing: closer = smoother and longer.").Set(0.0).Values(0, 0.3, 0.5, 0.8, 1, 2, 3);
            inputs.AddNumber("Feed", "Feed", "How fast the cutter moves in the material, in mm/s (the file has cm/s, as the Zünd counts). 0 = automatic.").Set(0.0).Values(0, 20, 30, 40, 50, 70, 100);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Cutter", "Cutter", "The cutter: connect it to Rough Cutter or Finish Cutter of CNC - Rough and Finish.");
            outputs.AddText("Info", "Info", "The cutter in words.");
        }

        protected override void Process(IDataAccess access)
        {
            int kind, bit; double down, over, feed;
            access.GetItem(0, out kind); access.GetItem(1, out bit); access.GetItem(2, out down); access.GetItem(3, out over); access.GetItem(4, out feed);
            if (kind < 0 || kind > 5) { access.AddError("Cutter", "Cutter is 0 to 5. For another one: CNC - Own Cutter."); return; }
            var c = CutterPresets.Make(kind, bit, feed, 0, 0, 0, 0, 31, down, over, out string error);
            if (c == null) { access.AddError("Cutter", error); return; }
            access.SetItem(0, c);
            access.SetItem(1, CutterPresets.Info(c));
        }
    }

    // ============================================================== own cutter
    [IoId("d1c5e0a7-3f64-4b0a-8a5e-6f7b8c9d0e03")]
    public sealed class CncOwnCutterComponent : Component
    {
        public CncOwnCutterComponent() : base(Io.Name("CNC - Own Cutter", "A cutter that is not in the list of CNC - Cutter: measure it and type in its sizes.", Io.Cnc, 0)) { }
        public CncOwnCutterComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCCutterIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Shape", "Shape", "0 = flat end (roughing), 1 = ball (finishing).").Set(1).Choices(CutterPresets.Shapes);
            inputs.AddNumber("Diameter", "Diameter", "Diameter of the cutting edge in mm.").Set(6.0).Values(2, 3, 4, 6, 8);
            inputs.AddNumber("Cutting Length", "Cut Length", "Length of the cutting edge in mm. Above it the 6 mm shank begins and is kept off the model.").Set(20.0).Values(6, 12, 14, 20, 22, 30);
            inputs.AddNumber("Max Depth", "Max Depth", "How deep the cutter reaches into the plate in mm, before the holder or the suction bell touches.").Set(22.0).Values(10, 15, 22, 30, 40);
            inputs.AddInteger("Magazine", "Magazine", CutterPresets.BitHelp).Set(0).Choices(CutterPresets.Bits);
            inputs.AddNumber("Step Down", "Step Down", "How deep one level goes in mm. 0 = automatic, after the material of the plate.").Set(0.0).Values(0, 1, 2, 3, 4, 6);
            inputs.AddNumber("Stepover", "Stepover", "How far the paths lie apart in mm. 0 = automatic: roughing 45 %, finishing 10 % of the diameter.").Set(0.0).Values(0, 0.3, 0.5, 0.8, 1, 2, 3);
            inputs.AddNumber("Feed", "Feed", "How fast the cutter moves in the material, in mm/s. 0 = automatic.").Set(0.0).Values(0, 20, 30, 40, 50, 70, 100);
            inputs.AddNumber("Spindle Speed", "RPM", "Turns per minute, 7000 to 50000.").Set(30000.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Cutter", "Cutter", "The cutter: connect it to Rough Cutter or Finish Cutter of CNC - Rough and Finish.");
            outputs.AddText("Info", "Info", "The cutter in words.");
        }

        protected override void Process(IDataAccess access)
        {
            int shape, bit; double d, l, max, down, over, feed, rpm;
            access.GetItem(0, out shape); access.GetItem(1, out d); access.GetItem(2, out l); access.GetItem(3, out max); access.GetItem(4, out bit);
            access.GetItem(5, out down); access.GetItem(6, out over); access.GetItem(7, out feed); access.GetItem(8, out rpm);
            var c = CutterPresets.Make(shape == 0 ? 6 : 7, bit, feed, rpm, d, l, max, 31, down, over, out string error);
            if (c == null) { access.AddError("Cutter", error); return; }
            access.SetItem(0, c);
            access.SetItem(1, CutterPresets.Info(c));
        }
    }

    // ============================================================== rough and finish
    [IoId("a4bac166-6fcc-44b4-bce8-0f454aa6d793")]
    public sealed class CncRoughFinishComponent : Component
    {
        public CncRoughFinishComponent() : base(Io.Name("CNC - Rough and Finish", "STEP 3. Mills a relief (topography, height model, mould) out of a plate on the Zünd G3, in three steps: 1 ROUGH: a thick cutter takes the bulk away in levels, in rings, never zigzag. 2 FINISH: the machine changes the cutter and a ball goes over the surface in close paths. 3 CUT OUT: the part is sawn out all round and held by tabs or a thin skin, or it stays in the plate (Hold). Shows every path and the finished part; Save writes the file. Red = nothing is written, read the message. The path is simulated: the report says what is left and whether anything is cut too deep.", Io.Cnc, 0)) { }
        public CncRoughFinishComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CNCRoughFinishIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Model", "Model", "The relief: mesh, surface, Brep or SubD, in millimetres. Only its upper side counts. It may lie anywhere: CNC - Plate puts it into the plate.", Access.Twig);
            inputs.AddGeneric("Plate", "Plate", "The raw plate (CNC - Plate). Empty = 300 x 220 x 20 mm.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Rough Cutter", "Rough Cutter", "The thick flat cutter for the bulk and for cutting out (CNC - Cutter). Empty = finish only.", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Finish Cutter", "Finish Cutter", "The fine cutter for the surface, best a ball (CNC - Cutter). Empty = rough only.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Roughing", "Roughing", "How the bulk is taken away: 0 = rings from the middle outwards, always in the same direction (climb), 1 = rows.").Set(0).Choices("Rings", "Rows");
            inputs.AddInteger("Finishing", "Finishing", "How the surface is gone over: 0 = rings from the edge inwards, always in the same direction, 1 = rows, 2 = rows and a second time across (finest, twice the time).").Set(0).Choices("Rings", "Rows", "Rows and across");
            inputs.AddNumber("Row Angle", "Row Angle", "Only for rows: their direction in degrees, 0 = along X.").Set(0.0).Values(0, 45, 90, 135);
            inputs.AddNumber("Stock", "Stock", "How much the roughing leaves for the finishing in mm.").Set(0.5).Values(0, 0.3, 0.5, 1);
            inputs.AddInteger("Hold", "Hold", ReliefMill.HoldHelp + ".").Set(0).Choices(ReliefMill.Holds);
            inputs.AddInteger("Tabs", "Tabs", "Hold 0 only: how many tabs hold the part in the plate. Cut them with a knife afterwards.").Set(4);
            inputs.AddNumber("Tab Width", "Tab Width", "Hold 0 only: width of a tab in mm. 0 = automatic (8 mm).").Set(0.0).Values(0, 5, 8, 12);
            inputs.AddNumber("Hold Height", "Hold Height", "Height of the tabs (automatic 2 mm) or thickness of the skin (automatic 0.6 mm), in mm. 0 = automatic.").Set(0.0).Values(0, 0.4, 0.6, 1, 2, 3);
            inputs.AddText("File", "File", "Name or path of the PLT file. Empty = named by itself, on the Desktop.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True = write the PLT file once (it goes back to False).").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Report", "Report", "What happens, how long it takes, what is left and what is cut too deep. Read it before milling.", Access.Twig);
            outputs.AddText("PLT", "PLT", "The whole program with the changes of the cutter.");
            outputs.AddText("PLT per Cutter", "PLT per Cutter", "The same as one program per cutter, to change it by hand in between.", Access.Twig);
            outputs.AddCurve("1 Rough", "1 Rough", "The cuts of the thick cutter.", Access.Twig);
            outputs.AddCurve("2 Finish", "2 Finish", "The cuts of the fine cutter.", Access.Twig);
            outputs.AddCurve("3 Cut Out", "3 Cut Out", "The cut all round, over the tabs.", Access.Twig);
            outputs.AddCurve("Travel", "Travel", "The ways with the cutter lifted, between the cuts.", Access.Twig);
            outputs.AddMesh("Result", "Result", "What the plate looks like afterwards (simulated).");
            outputs.AddMesh("Model in Plate", "In Plate", "The model where it lies in the plate.");
        }

        protected override void Process(IDataAccess access)
        {
            using (Io.Cancellable(access))
            {
                object[] geo;
                if (!access.ItemArray(0, out geo) || geo == null || geo.Length == 0) return;
                string file; bool save;
                var mill = new ReliefMill { Plate = (Io.Get<MillPlate>(access, 1) ?? new MillPlate()).Copy(), Rough = Io.Get<Cutter>(access, 2), Finish = Io.Get<Cutter>(access, 3) };
                access.GetItem(4, out mill.RoughPattern); access.GetItem(5, out mill.FinishPattern); access.GetItem(6, out mill.FinishAngle); access.GetItem(7, out mill.Stock);
                access.GetItem(8, out mill.Hold); access.GetItem(9, out mill.Tabs); access.GetItem(10, out mill.TabWidth); access.GetItem(11, out mill.HoldHeight); if (!access.GetItem(12, out file)) file = ""; access.GetItem(13, out save);
                mill.RoughAngle = mill.FinishAngle + 90;
                if (Io.Step(access, 5)) return;
                mill.Part = SliceEngine.ToMesh(geo.Where(g => g != null).Select(Io.Solid), 0.01, new List<string>());
                if (mill.Part == null) { access.AddError("No model", "Model cannot be used. Connect a mesh, surface, Brep or SubD."); return; }
                if (Io.Step(access, 15)) return;
                mill.Solve();
                foreach (var m in mill.Messages) Gh1.Message(access, m.Key, m.Value);
                Func<Func<string, bool>, Curve[]> paths = want => mill.Operations.Where(o => want(o.Name)).SelectMany(o => o.Cuts).Select(c => (Curve)new PolylineCurve(c.Points)).ToArray();
                access.SetTwig(0, mill.Report.ToArray(), null, null);
                access.SetItem(1, mill.Plt);
                access.SetTwig(2, mill.PltPerTool.ToArray(), null, null);
                access.SetTwig(3, paths(n => n == "Rough"), null, null);
                access.SetTwig(4, paths(n => n != "Rough" && n != "Cut out"), null, null);
                access.SetTwig(5, paths(n => n == "Cut out"), null, null);
                access.SetTwig(6, mill.Travel().Select(l => (Curve)new LineCurve(l)).ToArray(), null, null);
                if (mill.Result != null) access.SetItem(7, mill.Result);
                if (mill.Placed != null) access.SetItem(8, mill.Placed);
                if (mill.Messages.Any(m => m.Key == 2)) { if (save) Files.Reset(this, 13); return; }
                if (mill.Plt.Length > 0) Files.Save(access, this, 13, save, file, new List<string> { mill.Plt }, () => "Relief_" + DateTime.Now.ToString("yyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture) + ".plt");
                else if (save) access.AddWarning("Not saved", "Give both cutters their place in the magazine, or take the programs of PLT per Cutter.");
            }
        }
    }

    // ============================================================== quick start
    [IoId("9f3d2b61-7a44-4c0e-8d1f-5b6a7c8d9e02")]
    public sealed class CncQuickStartComponent : Component
    {
        private bool _done;
        public CncQuickStartComponent() : base(Io.Name("CNC - Quick Start", "Place this on the canvas to get a complete example for the Zünd: a topography that is roughed out of a plate, finished with a ball and cut out at the end. Replace the model by your own.", Io.Cnc, 0)) { DocumentChanged += Placed; }
        public CncQuickStartComponent(IReader reader) : base(reader) { _done = true; }
        protected override IIcon IconInternal => Io.Icon("CNCQuickStartIcon");
        protected override void AddInputs(InputAdder inputs) { }
        protected override void AddOutputs(OutputAdder outputs) { }
        protected override void Process(IDataAccess access) { }

        private void Placed(object sender, Grasshopper2.Doc.ObjectDocumentEventArgs e)
        {
            if (_done || e.NewDocument == null) return;
            Eto.Forms.Application.Instance.AsyncInvoke(() =>
            {
                var doc = Document;
                if (_done || doc == null || !ReferenceEquals(doc.Objects.Find(InstanceId), this)) return;
                _done = true;
                try { Build(doc); }
                catch (Exception ex) { Rhino.RhinoApp.WriteLine("LARGER CNC Quick Start: " + ex.Message); }
            });
        }

        private void Build(Grasshopper2.Doc.Document doc)
        {
            var o = Attributes.Pivot;
            Func<Component, float, float, Component> put = (c, x, y) => { doc.Objects.Add(c, new Eto.Drawing.PointF(o.X + x, o.Y + y)); return c; };
            Action<string, float, float, bool> write = (text, x, y, bold) =>
            {
                var sc = new Grasshopper2.SpecialObjects.ScribbleObject(text);
                if (bold) sc.TextStyle = Eto.Drawing.FontStyle.Bold;
                doc.Objects.Add(sc, new Eto.Drawing.PointF(o.X + x, o.Y + y));
                var b = sc.Attributes.Bounds;
                sc.Attributes.Move(o.X + x - b.X, o.Y + y - b.Y);
            };
            write("ZÜND G3: FRÄSEN IN DREI SCHRITTEN", 160, -230, true);
            write("Schruppen (dicker Fräser, Ringe)  >  Schlichten (Kugelfräser, fein)  >  Ausschneiden (rundherum, mit Stegen)", 160, -195, false);
            write("1  Beispiel wählen oder eigenes Modell anschließen.   2  Platte messen und eintragen.   3  Magazinplätze der Fräser prüfen.   4  Report lesen, dann Save.", 160, -165, false);
            var model = new Grasshopper2.Parameters.Standard.GenericParameter("Dein Modell", "Modell", "Dein Modell", Access.Twig);
            var relief = put(new CncReliefComponent(), 260, -95); Io.SetValue(relief.Parameters.Input(0), ReliefPatterns.Names.Length - 1);
            doc.Objects.Add(model, new Eto.Drawing.PointF(o.X + 440, o.Y - 40));
            Connections.Connect(relief.Parameters.Output(0), model, null);
            write("Zuerst das Beispiel 'Topography' in Schaum fräsen: steht die Landschaft wie im Modell und ist die tiefste Stelle so tief wie im Bericht, zählt die Maschine Z wie das Plug-in.", 160, -135, false);
            var plate = put(new CncPlateComponent(), 260, 120);
            var thick = put(new CncCutterComponent(), 260, 360);
            var fine = put(new CncCutterComponent(), 260, 600);
            Io.SetValue(thick.Parameters.Input(1), 13);
            Io.SetValue(fine.Parameters.Input(0), 3); Io.SetValue(fine.Parameters.Input(1), 14); Io.SetValue(fine.Parameters.Input(3), 0.6);
            var mill = put(new CncRoughFinishComponent(), 620, 240);
            Connections.Connect(model, mill.Parameters.Input(0), null);
            Connections.Connect(plate.Parameters.Output(0), mill.Parameters.Input(1), null);
            Connections.Connect(thick.Parameters.Output(0), mill.Parameters.Input(2), null);
            Connections.Connect(fine.Parameters.Output(0), mill.Parameters.Input(3), null);
        }
    }
}
