using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Components.Slicer;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNC
{
    /// <summary>Reliefs to mill: models and panels with an architectural background.</summary>
    public class CncReliefComponent : SlicerComponent, IWideRows
    {
        public CncReliefComponent()
            : base("CNC - Relief", "Relief",
                  "Examples to mill: a site model, a facade, scales, diamond facets, a gyroid, vaults and other patterns as a mesh. Choose one, set its size, connect it to Model of CNC - Rough and Finish. Wave bends the whole panel.") { }

        public override string SubCategory => "CNC";
        public override Guid ComponentGuid => new Guid("6B9A0E52-74C1-4D2B-8B0F-3A5D7E1C9F46");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCReliefIcon.png", this);
        public float MinWidth => 230;

        // files saved before Voronoi was taken out (2.0.3 and older) count the patterns after it one higher
        public override bool Write(GH_IO.Serialization.GH_IWriter writer) { writer.SetBoolean("LARGER_NoVoronoi", true); return base.Write(writer); }
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool ok = base.Read(reader);
            if (!reader.ItemExists("LARGER_NoVoronoi") && Params.Input.Count > 0 && Params.Input[0] is Grasshopper.Kernel.Parameters.Param_Integer pi && pi.PersistentData.DataCount > 0)
            {
                var old = pi.PersistentData.AllData(true).OfType<GH_Integer>().FirstOrDefault();
                if (old != null) { pi.PersistentData.Clear(); pi.PersistentData.Append(new GH_Integer(ReliefPatterns.FromOld(old.Value))); }
            }
            return ok;
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Pattern", "Pattern", ReliefPatterns.Help, GH_ParamAccess.item, 0);
            p.AddNumberParameter("Length", "Length", "Size in X in mm", GH_ParamAccess.item, 240.0);
            p.AddNumberParameter("Width", "Width", "Size in Y in mm", GH_ParamAccess.item, 160.0);
            p.AddNumberParameter("Height", "Height", "Height of the whole relief in mm", GH_ParamAccess.item, 14.0);
            p.AddNumberParameter("Cell Size", "Size", "Size of one house, window, scale or cell in mm. 0 = automatic. Below about three times the diameter of the ball the cutter no longer reaches the details", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Wave", "Wave", "Bends the panel: 0 = flat, 1 = its waves are as high as the pattern, -1 = automatic", GH_ParamAccess.item, -1.0);
            p.AddIntegerParameter("Seed", "Seed", "Another number gives another arrangement (site model, gouges, labyrinth)", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Ball", "Ball", "Diameter of the ball that finishes it, in mm: valleys are rounded so that this ball reaches every place. 0 = sharp valleys", GH_ParamAccess.item, 6.0);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 0, "Pattern", ReliefPatterns.Names);
            AddSlider(doc, 1, "Length", 40, 1200, 240, 0, "mm", new double[] { 160, 240, 300, 400, 600 });
            AddSlider(doc, 2, "Width", 40, 800, 160, 0, "mm", new double[] { 100, 160, 220, 300, 400 });
            AddSlider(doc, 3, "Height", 2, 45, 14, 1, "mm", new double[] { 6, 10, 14, 18, 25 });
            var cs = AddSlider(doc, 4, "Cell Size", 0, 120, 0, 0, "mm", new double[] { 0, 16, 24, 32, 40, 60 });
            AddSlider(doc, 7, "Ball", 0, 12, 6, 1, "mm", new double[] { 0, 3, 4, 6, 8 });
            if (cs != null) cs.ZeroText = () => "automatic";
            AddSlider(doc, 5, "Wave (-1 auto)", -1, 2, -1, 1, "", new double[] { -1, 0, 0.3, 0.6, 1, 1.5 });
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Model", "Model", "The relief: connect it to Model of CNC - Rough and Finish", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int kind = 0, seed = 1; double l = 240, w = 160, h = 14, size = 0, wave = -1, ball = 6;
            da.GetData(0, ref kind); da.GetData(1, ref l); da.GetData(2, ref w); da.GetData(3, ref h); da.GetData(4, ref size); da.GetData(5, ref wave); da.GetData(6, ref seed); da.GetData(7, ref ball);
            var m = ReliefPatterns.Make(kind, l, w, h, size, wave, seed, ball);
            if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Inv.F($"Pattern is 0 to {ReliefPatterns.Names.Length - 1}; Length, Width and Height must be greater than 0.")); return; }
            Message = ReliefPatterns.Names[kind].Split('(')[0].Trim();
            da.SetData(0, m);
        }
    }

    /// <summary>The raw plate: size, material and where the model lies in it.</summary>
    public class CncPlateComponent : SlicerComponent, IWideRows
    {
        public CncPlateComponent()
            : base("CNC - Plate", "Plate",
                  "STEP 1. The raw plate as it lies on the table of the Zünd: measure yours and type in its size. Also where the model lies in it and what it is made of. Connect it to CNC - Rough and Finish.") { }

        public override string SubCategory => "CNC";
        public override Guid ComponentGuid => new Guid("3B0E6C52-5C0D-4B7E-9D3B-0B6C6E7D9A11");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCPlateIcon.png", this);
        public float MinWidth => 210;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Length", "Length", "Size of the plate in X in mm. Measure the real plate", GH_ParamAccess.item, 300.0);
            p.AddNumberParameter("Width", "Width", "Size of the plate in Y in mm. Measure the real plate", GH_ParamAccess.item, 220.0);
            p.AddNumberParameter("Thickness", "Thickness", "Thickness of the plate in mm, at most 50. Measure it with a caliper: the cutter goes down to the table by it", GH_ParamAccess.item, 20.0);
            p.AddIntegerParameter("Material", "Material", MillPlate.MaterialHelp, GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Placement", "Placement", MillPlate.PlacementHelp, GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Height", "Height", MillPlate.HeightHelp, GH_ParamAccess.item, 0);
            p.AddPointParameter("Corner", "Corner", "Where the front left corner of the plate lies on the table, seen from the zero point of the machine (X, Y in mm)", GH_ParamAccess.item, new Point3d(20, 20, 0));
            p.AddIntegerParameter("Vacuum", "Vacuum", "Vacuum of the table, 0 (off) to 10", GH_ParamAccess.item, 5);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddSlider(doc, 0, "Length (X)", 50, 2500, 300, 1, "mm", new double[] { 200, 300, 400, 600, 800, 1200, 2500 });
            AddSlider(doc, 1, "Width (Y)", 50, 1800, 220, 1, "mm", new double[] { 150, 220, 300, 400, 600, 800, 1250 });
            AddSlider(doc, 2, "Thickness", 3, 50, 20, 1, "mm", new double[] { 10, 15, 19, 20, 25, 30, 40, 50 });
            AddValueList(doc, 3, "Material", MillPlate.Materials);
            AddValueList(doc, 4, "Placement", MillPlate.Placements);
            AddValueList(doc, 5, "Height", MillPlate.Heights);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Plate", "Plate", "The plate: connect it to Plate of CNC - Rough and Finish", GH_ParamAccess.item);
            p.AddBoxParameter("Box", "Box", "The plate as a box, to see where it lies", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var m = new MillPlate(); var corner = new Point3d(20, 20, 0);
            da.GetData(0, ref m.Length); da.GetData(1, ref m.Width); da.GetData(2, ref m.Thickness); da.GetData(3, ref m.Material);
            da.GetData(4, ref m.Placement); da.GetData(5, ref m.Height); da.GetData(6, ref corner); da.GetData(7, ref m.Vacuum);
            m.X = corner.X; m.Y = corner.Y;
            string bad = m.Problem();
            if (bad != null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, bad); return; }
            Message = Inv.F($"{m.Length:0.#} x {m.Width:0.#} x {m.Thickness:0.#}");
            da.SetData(0, new GH_ObjectWrapper(m));
            da.SetData(1, m.Box);
        }
    }

    /// <summary>A cutter of the list.</summary>
    public class CncCutterComponent : SlicerComponent, IWideRows
    {
        public CncCutterComponent()
            : base("CNC - Cutter", "Cutter",
                  "STEP 2. One cutter (router bit): choose which one it is and in which place of the magazine it sits. Everything else is filled in. Take one for roughing (thick, flat) and one for finishing (a ball). For a cutter that is not in the list: CNC - Own Cutter.") { }

        public override string SubCategory => "CNC";
        public override Guid ComponentGuid => new Guid("460150CA-6CD7-43A2-BE22-141AA1DFD9CB");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCCutterIcon.png", this);
        public float MinWidth => 190;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Cutter", "Cutter", CutterPresets.Help, GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Magazine", "Magazine", CutterPresets.BitHelp, GH_ParamAccess.item, 0);
            p.AddNumberParameter("Step Down", "Step Down", "How deep one level goes in mm. 0 = automatic, after the material of the plate", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Stepover", "Stepover", "How far the paths lie apart in mm. 0 = automatic: roughing 45 %, finishing 10 % of the diameter. Finishing: closer = smoother and longer", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Feed", "Feed", "How fast the cutter moves in the material, in mm/s (the file has cm/s, as the Zünd counts). 0 = automatic", GH_ParamAccess.item, 0.0);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 0, "Cutter", CutterPresets.Names);
            AddValueList(doc, 1, "Magazine", CutterPresets.Bits);
            var sd = AddSlider(doc, 2, "Step Down", 0, 12, 0, 1, "mm", new double[] { 0, 1, 2, 3, 4, 6 });
            var so = AddSlider(doc, 3, "Stepover", 0, 5, 0, 2, "mm", new double[] { 0, 0.3, 0.5, 0.8, 1, 2, 3 });
            var fe = AddSlider(doc, 4, "Feed", 0, 150, 0, 0, "mm/s", new double[] { 0, 20, 30, 40, 50, 70, 100 });
            foreach (var r in new[] { sd, so, fe }) if (r != null) r.ZeroText = () => "automatic";
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Cutter", "Cutter", "The cutter: connect it to Rough Cutter or Finish Cutter of CNC - Rough and Finish", GH_ParamAccess.item);
            p.AddTextParameter("Info", "Info", "The cutter in words", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int kind = 0, bit = 0; double down = 0, over = 0, feed = 0;
            da.GetData(0, ref kind); da.GetData(1, ref bit); da.GetData(2, ref down); da.GetData(3, ref over); da.GetData(4, ref feed);
            if (kind < 0 || kind > 5) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cutter is 0 to 5. For another one: CNC - Own Cutter."); return; }
            var c = CutterPresets.Make(kind, bit, feed, 0, 0, 0, 0, 31, down, over, out string error);
            if (c == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            Message = c.Name;
            da.SetData(0, new GH_Cutter(c));
            da.SetData(1, CutterPresets.Info(c));
        }
    }

    /// <summary>A cutter that is not in the list.</summary>
    public class CncOwnCutterComponent : SlicerComponent, IWideRows
    {
        public CncOwnCutterComponent()
            : base("CNC - Own Cutter", "Own Cutter",
                  "A cutter that is not in the list of CNC - Cutter: measure it and type in its sizes.") { }

        public override string SubCategory => "CNC";
        public override Guid ComponentGuid => new Guid("B2D5C0E6-1B7A-4E55-9E7C-2F6B0E4A7C21");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCCutterIcon.png", this);
        public float MinWidth => 190;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Shape", "Shape", "0 = flat end (roughing), 1 = ball (finishing)", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Diameter", "Diameter", "Diameter of the cutting edge in mm", GH_ParamAccess.item, 6.0);
            p.AddNumberParameter("Cutting Length", "Cut Length", "Length of the cutting edge in mm. Above it the 6 mm shank begins and is kept off the model", GH_ParamAccess.item, 20.0);
            p.AddNumberParameter("Max Depth", "Max Depth", "How deep the cutter reaches into the plate in mm, before the holder or the suction bell touches", GH_ParamAccess.item, 22.0);
            p.AddIntegerParameter("Magazine", "Magazine", CutterPresets.BitHelp, GH_ParamAccess.item, 0);
            p.AddNumberParameter("Step Down", "Step Down", "How deep one level goes in mm. 0 = automatic, after the material of the plate", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Stepover", "Stepover", "How far the paths lie apart in mm. 0 = automatic: roughing 45 %, finishing 10 % of the diameter. Finishing: closer = smoother and longer", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Feed", "Feed", "How fast the cutter moves in the material, in mm/s (the file has cm/s, as the Zünd counts). 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Spindle Speed", "RPM", "Turns per minute, 7000 to 50000", GH_ParamAccess.item, 30000.0);
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 0, "Shape", CutterPresets.Shapes);
            AddSlider(doc, 1, "Diameter", 1, 12, 6, 2, "mm", new double[] { 2, 3, 4, 6, 8 });
            AddSlider(doc, 2, "Cut Length", 2, 60, 20, 1, "mm", new double[] { 6, 12, 14, 20, 22, 30 });
            AddSlider(doc, 3, "Max Depth", 2, 50, 22, 1, "mm", new double[] { 10, 15, 22, 30, 40 });
            AddValueList(doc, 4, "Magazine", CutterPresets.Bits);
            var sd = AddSlider(doc, 5, "Step Down", 0, 12, 0, 1, "mm", new double[] { 0, 1, 2, 3, 4, 6 });
            var so = AddSlider(doc, 6, "Stepover", 0, 5, 0, 2, "mm", new double[] { 0, 0.3, 0.5, 0.8, 1, 2, 3 });
            var fe = AddSlider(doc, 7, "Feed", 0, 150, 0, 0, "mm/s", new double[] { 0, 20, 30, 40, 50, 70, 100 });
            foreach (var r in new[] { sd, so, fe }) if (r != null) r.ZeroText = () => "automatic";
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Cutter", "Cutter", "The cutter: connect it to Rough Cutter or Finish Cutter of CNC - Rough and Finish", GH_ParamAccess.item);
            p.AddTextParameter("Info", "Info", "The cutter in words", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int shape = 1, bit = 0; double d = 6, l = 20, max = 22, down = 0, over = 0, feed = 0, rpm = 30000;
            da.GetData(0, ref shape); da.GetData(1, ref d); da.GetData(2, ref l); da.GetData(3, ref max); da.GetData(4, ref bit);
            da.GetData(5, ref down); da.GetData(6, ref over); da.GetData(7, ref feed); da.GetData(8, ref rpm);
            var c = CutterPresets.Make(shape == 0 ? 6 : 7, bit, feed, rpm, d, l, max, 31, down, over, out string error);
            if (c == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            Message = c.Name;
            da.SetData(0, new GH_Cutter(c));
            da.SetData(1, CutterPresets.Info(c));
        }
    }

    /// <summary>Rough with a thick cutter, finish with a fine one, cut out, with the change of the cutter in the program.</summary>
    public class CncRoughFinishComponent : SlicerComponent, IWideRows
    {
        public CncRoughFinishComponent()
            : base("CNC - Rough and Finish", "Rough + Finish",
                  "STEP 3. Mills a relief (topography, height model, mould) out of a plate on the Zünd G3, in three steps: 1 ROUGH: a thick cutter takes the bulk away in levels, in rings, never zigzag. 2 FINISH: the machine changes the cutter and a ball goes over the surface in close paths. 3 CUT OUT: the part is sawn out all round and held by tabs or a thin skin, or it stays in the plate (Hold). Shows every path and the finished part; click Save PLT for the file. Red = nothing is written, read the message. The path is simulated: the report says what is left and whether anything is cut too deep.") { }

        public override string SubCategory => "CNC";
        public override Guid ComponentGuid => new Guid("8AA92BBF-A1B3-46FD-9AD1-98DBCEA88E0B");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCRoughFinishIcon.png", this);
        protected override bool CanRunInBackground => true;
        public float MinWidth => 230;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model", "Model", "The relief: mesh, surface, Brep or SubD, in millimetres. Only its upper side counts. It may lie anywhere: CNC - Plate puts it into the plate", GH_ParamAccess.list);
            p.AddGenericParameter("Plate", "Plate", "The raw plate (CNC - Plate). Empty = 300 x 220 x 20 mm", GH_ParamAccess.item);
            p.AddGenericParameter("Rough Cutter", "Rough Cutter", "The thick flat cutter for the bulk and for cutting out (CNC - Cutter). Empty = finish only", GH_ParamAccess.item);
            p.AddGenericParameter("Finish Cutter", "Finish Cutter", "The fine cutter for the surface, best a ball (CNC - Cutter). Empty = rough only", GH_ParamAccess.item);
            p.AddIntegerParameter("Roughing", "Roughing", "How the bulk is taken away: 0 = rings from the middle outwards, always in the same direction (climb), 1 = rows", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Finishing", "Finishing", "How the surface is gone over: 0 = rings from the edge inwards, always in the same direction, 1 = rows, 2 = rows and a second time across (finest, twice the time)", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Row Angle", "Row Angle", "Only for rows: their direction in degrees, 0 = along X", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Stock", "Stock", "How much the roughing leaves for the finishing in mm", GH_ParamAccess.item, 0.5);
            p.AddIntegerParameter("Hold", "Hold", ReliefMill.HoldHelp, GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Tabs", "Tabs", "Hold 0 only: how many tabs hold the part in the plate. Cut them with a knife afterwards", GH_ParamAccess.item, 4);
            p.AddNumberParameter("Tab Width", "Tab Width", "Hold 0 only: width of a tab in mm. 0 = automatic (8 mm)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Hold Height", "Hold Height", "Height of the tabs (automatic 2 mm) or thickness of the skin (automatic 0.6 mm), in mm. 0 = automatic", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("File", "File", "Name or path of the PLT file. Empty = named by itself, on the Desktop", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "Save", "True = write the PLT file (or click Save PLT)", GH_ParamAccess.item, false);
            p[1].Optional = true; p[2].Optional = true; p[3].Optional = true; p[12].Optional = true;
        }

        protected override void CreateAutoInputs(GH_Document doc)
        {
            AddValueList(doc, 4, "Roughing", "Rings", "Rows");
            AddValueList(doc, 5, "Finishing", "Rings", "Rows", "Rows and across");
            AddSlider(doc, 7, "Stock", 0, 2, 0.5m, 2, "mm", new double[] { 0, 0.3, 0.5, 1 });
            AddValueList(doc, 8, "Hold", ReliefMill.Holds);
            AddCount(doc, 9, "Tabs", 0, 12, 4);
            AddButton(doc, 13, "Save PLT");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Report", "Report", "What happens, how long it takes, what is left and what is cut too deep. Read it before milling", GH_ParamAccess.list);
            p.AddTextParameter("PLT", "PLT", "The whole program with the changes of the cutter", GH_ParamAccess.item);
            p.AddTextParameter("PLT per Cutter", "PLT per Cutter", "The same as one program per cutter, to change it by hand in between", GH_ParamAccess.list);
            p.AddCurveParameter("1 Rough", "1 Rough", "The cuts of the thick cutter", GH_ParamAccess.list);
            p.AddCurveParameter("2 Finish", "2 Finish", "The cuts of the fine cutter", GH_ParamAccess.list);
            p.AddCurveParameter("3 Cut Out", "3 Cut Out", "The cut all round, over the tabs", GH_ParamAccess.list);
            p.AddLineParameter("Travel", "Travel", "The ways with the cutter lifted, between the cuts", GH_ParamAccess.list);
            p.AddMeshParameter("Result", "Result", "What the plate looks like afterwards (simulated)", GH_ParamAccess.item);
            p.AddMeshParameter("Model in Plate", "In Plate", "The model where it lies in the plate", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (Async.Deliver(da)) return;
            var geo = new List<object>();
            if (!da.GetDataList(0, geo) || geo.Count == 0) return;
            string file = null; bool save = false;
            var mill = new ReliefMill { Plate = (Get<MillPlate>(da, 1) ?? new MillPlate()).Copy(), Rough = Get<Cutter>(da, 2), Finish = Get<Cutter>(da, 3) };
            da.GetData(4, ref mill.RoughPattern); da.GetData(5, ref mill.FinishPattern); da.GetData(6, ref mill.FinishAngle); da.GetData(7, ref mill.Stock);
            da.GetData(8, ref mill.Hold); da.GetData(9, ref mill.Tabs); da.GetData(10, ref mill.TabWidth); da.GetData(11, ref mill.HoldHeight); da.GetData(12, ref file); da.GetData(13, ref save);
            mill.RoughAngle = mill.FinishAngle + 90;
            var copy = AsyncSolve.Copy(geo);
            string path = save ? FreePath(file, "Relief_" + DateTime.Now.ToString("yyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture) + ".plt") : null;
            Async.Run(da, job =>
            {
                mill.Part = SliceEngine.ToMesh(copy, 0.01, new List<string>());
                if (mill.Part == null) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Model cannot be used. Connect a mesh, surface, Brep or SubD."); return; }
                mill.Solve();
                foreach (var m in mill.Messages)
                    job.AddRuntimeMessage(m.Key == 2 ? GH_RuntimeMessageLevel.Error : m.Key == 1 ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, m.Value);
                bool stopped = mill.Messages.Any(m => m.Key == 2);
                Func<Func<string, bool>, IEnumerable<Curve>> paths = want => mill.Operations.Where(o => want(o.Name)).SelectMany(o => o.Cuts).Select(c => (Curve)new PolylineCurve(c.Points));
                job.SetDataList(0, mill.Report);
                job.SetData(1, mill.Plt);
                job.SetDataList(2, mill.PltPerTool);
                job.SetDataList(3, paths(n => n == "Rough"));
                job.SetDataList(4, paths(n => n != "Rough" && n != "Cut out"));
                job.SetDataList(5, paths(n => n == "Cut out"));
                job.SetDataList(6, mill.Travel());
                if (mill.Result != null) job.SetData(7, mill.Result);
                if (mill.Placed != null) job.SetData(8, mill.Placed);
                job.Message = stopped ? "STOP - read the report" : Inv.F($"READY - about {mill.Minutes:0} min");
                if (path != null && !stopped)
                {
                    try
                    {
                        if (mill.Plt.Length > 0) job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(path, "Relief.plt", new List<string> { mill.Plt }));
                        else
                        {
                            // no numbers for the changes: one file per cutter
                            int k = 0; string stem = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", System.IO.Path.GetFileNameWithoutExtension(path));
                            foreach (string part in mill.PltPerTool)
                                job.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Saved " + OutputFile.Write(stem + "_" + (++k) + ".plt", "Relief.plt", new List<string> { part }));
                        }
                    }
                    catch (Exception ex) { job.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not save: " + ex.Message); }
                }
            });
        }

        private static T Get<T>(IGH_DataAccess da, int index) where T : class
        {
            IGH_Goo goo = null;
            if (!da.GetData(index, ref goo) || goo == null) return null;
            if (goo is GH_Cutter c) return c.Value as T;
            return goo.ScriptVariable() as T;
        }
    }

    /// <summary>The whole CNC example on the canvas.</summary>
    public class CncQuickStartComponent : GH_Component
    {
        private bool _done;
        public CncQuickStartComponent()
            : base("CNC - Quick Start", "CNC Start", "Place this on the canvas to get a complete example for the Zünd: a topography that is roughed out of a plate, finished with a ball and cut out at the end. Replace the model by your own.", "LARGER", "CNC") { }
        public override Guid ComponentGuid => new Guid("C7B1B7B4-6F2E-4A52-8A35-7A0E7D2F9C33");
        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCQuickStartIcon.png", this);
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { }
        public override bool Read(GH_IO.Serialization.GH_IReader reader) { _done = true; return base.Read(reader); }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (_done) return;
            _done = true;
            document.ScheduleSolution(5, doc => Build(doc, Attributes.Pivot.X + 160, Attributes.Pivot.Y));
        }

        internal static void Build(GH_Document doc, float x0, float y0)
        {
            Func<IGH_DocumentObject, float, float, IGH_DocumentObject> place = (o, x, y) => { o.CreateAttributes(); o.Attributes.Pivot = new System.Drawing.PointF(x0 + x, y0 + y); doc.AddObject(o, false); return o; };
            Action<string, float, float, float> write = (t, x, y, size) => place(new Grasshopper.Kernel.Special.GH_Scribble { Text = t, Font = new System.Drawing.Font("Arial", size, size >= 20 ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular) }, x, y);
            Func<string, string, float, float, float, float, Grasshopper.Kernel.Special.GH_Panel> panel = (name, text, x, y, w, h) =>
            {
                var p = new Grasshopper.Kernel.Special.GH_Panel { NickName = name, UserText = text };
                p.Properties.Multiline = true; p.Properties.Wrap = true; p.Properties.Alignment = Grasshopper.Kernel.Special.GH_Panel.Alignment.Left;
                p.CreateAttributes();
                p.Attributes.Pivot = new System.Drawing.PointF(x0 + x, y0 + y); p.Attributes.Bounds = new System.Drawing.RectangleF(x0 + x, y0 + y, w, h);
                doc.AddObject(p, false);
                return p;
            };
            Action<string, System.Drawing.Color, IGH_DocumentObject[]> group = (title, back, members) =>
            {
                var g = new Grasshopper.Kernel.Special.GH_Group { NickName = title, Colour = back };
                g.CreateAttributes(); doc.AddObject(g, false);
                foreach (var m in members) g.AddObject(m.InstanceGuid);
                g.ExpireCaches();
            };
            Action<IGH_Param, int> setInt = (p, v) => { var q = (Grasshopper.Kernel.Parameters.Param_Integer)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Integer(v)); };
            Action<IGH_Param, double> setNum = (p, v) => { var q = (Grasshopper.Kernel.Parameters.Param_Number)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Number(v)); };
            Func<int, int, int, System.Drawing.Color> colour = (r, g, b) => System.Drawing.Color.FromArgb(70, r, g, b);

            write("ZÜND G3: FRÄSEN IN DREI SCHRITTEN", 0, -410, 30);
            write("Schruppen (dicker Fräser, Ringe)  >  Schlichten (Kugelfräser, fein)  >  Ausschneiden (rundherum, mit Stegen)", 0, -360, 13);

            var help = panel("SO GEHT ES", "1  MODELL: ein Beispiel wählen (Lageplan, Fassade, Schuppen ...). Eigenes Modell: Kabel zu 'Dein Modell' lösen, dann rechte Maustaste > 'Set one Geometry'. Millimeter, Lage egal.\n\n2  PLATTE: echte Platte messen, Länge, Breite und Dicke eintragen. Die Dicke mit dem Messschieber!\n\n3  FRÄSER: bei 'Magazine' den Platz wählen, in dem der Fräser wirklich steckt. Kugelfräser: Maße des eigenen prüfen.\n\n4  BERICHT lesen. READY = kann gefräst werden. STOP = es wird nichts geschrieben, der Bericht sagt warum.\n\n5  In Rhino ansehen: orange = Schruppen, blau = Schlichten, rot = Ausschneiden, grau = Fahrten mit gehobenem Fräser, dazu das fertige Teil.\n\n6  'Save PLT' anklicken. Die Datei liegt auf dem Schreibtisch.\n\nERSTER LAUF: Schaum, nichts im Weg, Hand am Not-Aus.", 0, -90, 330, 400);
            var relief = new CncReliefComponent(); place(relief, 500, -225); relief.Hidden = true; setInt(relief.Params.Input[0], ReliefPatterns.Names.Length - 1);
            var model = new Grasshopper.Kernel.Parameters.Param_Geometry { NickName = "Dein Modell" };
            place(model, 820, -190);
            model.AddSource(relief.Params.Output[0]); model.Hidden = true;
            group("1  MODELL: Beispiel wählen oder eigenes", colour(10, 132, 255), new IGH_DocumentObject[] { relief, model });

            var note = panel("ZUERST: DIE LANDSCHAFT IN SCHAUM", "Vor dem ersten eigenen Teil das Beispiel 'Topography' in Schaum fräsen. Beim Schlichten fahren X, Y und Z gleichzeitig: so zeigt ein Lauf, ob die Maschine Z so zählt wie das Plug-in.\n\nPrüfen: die Landschaft steht wie im Rhino-Modell (nicht gespiegelt, nicht verkehrt herum), die tiefste Stelle liegt so tief unter der Plattenoberseite, wie der Bericht sagt ('... mm deep at most'), der Rand ist bis zum Tisch durchgeschnitten.\nSonst: nicht weiterfräsen.", 0, 460, 330, 190);
            group("0  TEST DER MASCHINE", colour(220, 60, 60), new IGH_DocumentObject[] { note });

            var plate = new CncPlateComponent(); place(plate, 500, 120);
            plate.Hidden = true;
            group("2  PLATTE (messen!)", colour(150, 110, 200), new IGH_DocumentObject[] { plate });

            var thick = new CncCutterComponent(); place(thick, 500, 500); setInt(thick.Params.Input[0], 0); setInt(thick.Params.Input[1], 13);
            var fine = new CncCutterComponent(); place(fine, 500, 800); setInt(fine.Params.Input[0], 3); setInt(fine.Params.Input[1], 14); setNum(fine.Params.Input[3], 0.6);
            group("3a  DICKER FRÄSER: schruppt und schneidet aus", colour(232, 135, 12), new IGH_DocumentObject[] { thick });
            group("3b  KUGELFRÄSER: schlichtet", colour(10, 132, 255), new IGH_DocumentObject[] { fine });

            var mill = new CncRoughFinishComponent(); place(mill, 900, 200);
            mill.Params.Input[0].AddSource(model); mill.Params.Input[1].AddSource(plate.Params.Output[0]); mill.Params.Input[2].AddSource(thick.Params.Output[0]); mill.Params.Input[3].AddSource(fine.Params.Output[0]);
            mill.Hidden = true;
            group("4  FRÄSEN  (Save PLT schreibt die Datei)", colour(48, 176, 110), new IGH_DocumentObject[] { mill });

            var report = panel("BERICHT", "", 1180, -90, 520, 260);
            report.AddSource(mill.Params.Output[0]);
            group("5  BERICHT LESEN", colour(230, 200, 40), new IGH_DocumentObject[] { report });

            // the paths in their colours
            Func<string, int, System.Drawing.Color, float, IGH_DocumentObject[]> show = (name, output, c, y) =>
            {
                var swatch = new Grasshopper.Kernel.Special.GH_ColourSwatch { NickName = name, SwatchColour = c };
                place(swatch, 1180, y);
                var proxy = Grasshopper.Instances.ComponentServer.ObjectProxies.FirstOrDefault(q => q.Desc.Name == "Custom Preview" && !q.Obsolete && q.Kind == GH_ObjectType.CompiledObject && q.Desc.Category == "Display");
                var preview = proxy?.CreateInstance() as GH_Component;
                if (preview == null) return new IGH_DocumentObject[] { swatch };
                place(preview, 1390, y);
                preview.Params.Input[0].AddSource(mill.Params.Output[output]); preview.Params.Input[1].AddSource(swatch);
                return new IGH_DocumentObject[] { swatch, preview };
            };
            var seen = new List<IGH_DocumentObject>();
            seen.AddRange(show("Schruppen", 3, System.Drawing.Color.FromArgb(255, 120, 0), 250));
            seen.AddRange(show("Schlichten", 4, System.Drawing.Color.FromArgb(0, 110, 255), 310));
            seen.AddRange(show("Ausschneiden", 5, System.Drawing.Color.FromArgb(220, 0, 0), 370));
            seen.AddRange(show("Fahrten oben", 6, System.Drawing.Color.FromArgb(150, 150, 150), 430));
            seen.AddRange(show("Fertiges Teil", 7, System.Drawing.Color.FromArgb(222, 205, 170), 490));
            group("6  ANSEHEN (Farben; Vorschau je Zeile abschaltbar)", colour(120, 130, 145), seen.ToArray());
            doc.NewSolution(false);
        }
    }
}
