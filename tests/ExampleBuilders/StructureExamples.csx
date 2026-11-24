// Builds the examples in "EXAMPLE FILES/02 - STRUCTURE - Weave and Reshape". Run inside Rhino with Grasshopper 1 and LARGERslicer loaded.
// Every file works without other plug-ins.
using System; using System.Linq; using System.IO; using System.Collections.Generic; using System.Drawing;
using Grasshopper.Kernel; using Grasshopper.Kernel.Types; using Grasshopper.Kernel.Special; using Grasshopper.Kernel.Parameters; using Rhino.Geometry;
string DIR = "/Users/moritz/Documents/LARGER.slicer/EXAMPLE FILES/02 - STRUCTURE - Weave and Reshape/";
Directory.CreateDirectory(DIR);
var count = Grasshopper.Instances.ComponentServer.ObjectProxies.Count;
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "LARGERslicer");
GH_Document doc = null; float X0 = 60, Y0 = 220;
Func<IGH_DocumentObject, float, float, IGH_DocumentObject> place = (o, x, y) => { o.CreateAttributes(); o.Attributes.Pivot = new PointF(X0 + x, Y0 + y); doc.AddObject(o, false); return o; };
Func<string, float, float, GH_Component> L = (name, x, y) => { var c = (GH_Component)Activator.CreateInstance(asm.GetTypes().First(t => t.Name == name)); place(c, x, y); return c; };
Action<string, float, float, float> write = (t, x, y, s) => place(new GH_Scribble { Text = t, Font = new Font("Arial", s, s >= 20 ? FontStyle.Bold : FontStyle.Regular) }, x, y);
Func<string, string, float, float, float, float, GH_Panel> panel = (name, text, x, y, w, h) => { var p = new GH_Panel { NickName = name, UserText = text }; p.Properties.Multiline = true; p.Properties.Wrap = true; p.Properties.Alignment = GH_Panel.Alignment.Left; p.CreateAttributes(); p.Attributes.Pivot = new PointF(X0 + x, Y0 + y); p.Attributes.Bounds = new RectangleF(X0 + x, Y0 + y, w, h); doc.AddObject(p, false); return p; };
Action<string, Color, IGH_DocumentObject[]> group = (title, c, m) => { var g = new GH_Group { NickName = title, Colour = c }; g.CreateAttributes(); doc.AddObject(g, false); foreach (var q in m) g.AddObject(q.InstanceGuid); g.ExpireCaches(); };
Func<GH_Component, string, IGH_Param> I = (c, n) => c.Params.Input.First(p => p.Name == n), O = (c, n) => c.Params.Output.First(p => p.Name == n);
Action<GH_Component, string, GH_Component, string> wire = (a, o, b, i) => I(b, i).AddSource(o == null ? a.Params.Output[0] : O(a, o));
Action<GH_Component, string, double[]> num = (c, n, v) => { var p = (Param_Number)I(c, n); p.PersistentData.Clear(); foreach (double x in v) p.PersistentData.Append(new GH_Number(x)); };
Action<GH_Component, string, int> intv = (c, n, v) => { var p = (Param_Integer)I(c, n); p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(v)); };
Action<GH_Component, string, string> str = (c, n, v) => { var p = (Param_String)I(c, n); p.PersistentData.Clear(); p.PersistentData.Append(new GH_String(v)); };
Func<int, int, int, Color> col = (r, g, b) => Color.FromArgb(70, r, g, b);
Func<string, string[], int, float, float, GH_ValueList> list = (name, names, sel, x, y) => { var v = new GH_ValueList { NickName = name }; v.ListItems.Clear(); for (int i = 0; i < names.Length; i++) v.ListItems.Add(new GH_ValueListItem(names[i], i.ToString())); place(v, x, y); v.SelectItem(sel); return v; };
Func<string, double, double, double, int, float, float, GH_NumberSlider> slider = (name, lo, hi, v, dec, x, y) => { var s = new GH_NumberSlider { NickName = name }; s.Slider.DecimalPlaces = dec; s.Slider.Type = dec == 0 ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float; s.Slider.Minimum = (decimal)lo; s.Slider.Maximum = (decimal)hi; s.Slider.Value = (decimal)v; place(s, x, y); return s; };
Action<string> save = name => { Console.WriteLine(name + " " + new GH_DocumentIO(doc).SaveQuiet(DIR + name)); };
var sup = asm.GetTypes().First(x => x.Name == "SlicerComponent").GetField("SuppressAutoInputs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
var demo = asm.GetTypes().First(t => t.Name == "DemoParts");

// the cell of the FH Muenster with the AE20 extruder
Func<float, float, List<IGH_DocumentObject>, GH_Component> cell = (x, y, mem) =>
{
    var cg = L("CellGeometryComponent", x, y + 60); var robot = L("RobotArmComponent", x + 240, y); var tool = L("RobotToolComponent", x + 240, y + 130); var rc = L("RobotCellComponent", x + 480, y + 60);
    robot.Params.Input[2].AddSource(cg.Params.Output[2]); ((Param_String)tool.Params.Input[0]).PersistentData.Append(new GH_String("AE20_Extruder"));
    { var pp = (Param_Plane)tool.Params.Input[1]; pp.PersistentData.Clear(); pp.PersistentData.Append(new GH_Plane(new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis))); }
    { var p = (Param_Number)tool.Params.Input[2]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(46)); }
    { var pt = (Param_Point)tool.Params.Input[3]; pt.PersistentData.Clear(); pt.PersistentData.Append(new GH_Point(new Point3d(-61, -28, 113))); }
    rc.Params.Input[0].AddSource(robot.Params.Output[0]); rc.Params.Input[1].AddSource(tool.Params.Output[0]); rc.Params.Input[2].AddSource(cg.Params.Output[3]); rc.Params.Input[4].AddSource(cg.Params.Output[0]);
    tool.Hidden = true; robot.Hidden = true; mem.AddRange(new IGH_DocumentObject[] { cg, robot, tool, rc }); return rc;
};
sup?.SetValue(null, true);
try
{
    // ------------------------------------------------------------ 1 weave
    doc = new GH_Document();
    write("WEAVE: BAHNEN GREIFEN INEINANDER", 0, -190, 30);
    write("Zwei Wände nebeneinander verschmelzen nicht nur, sie werden von Lage zu Lage verwoben", 0, -140, 13);
    panel("SO GEHT ES", "1  Teil: eigene Geometrie an 'Teil' anschließen (Flächen dürfen Mittelflächen sein: Walls > Position 'centred').\n\n2  Continuous Path auf 'Layered' lassen. Bei einer Spirale liegen Nachbarbahnen verschieden hoch.\n\n3  Weave Zones zeigt, wo Bahnen nebeneinander liegen. Toleranzen: Closer, Touch, Further (Anteil der Bahnbreite). Von Hand: Include, Exclude.\n\n4  Weave: Muster wählen. Shift, Width (Außenseite bleibt glatt), Snake, Teeth.\n\n5  Weave Variants vergleicht 14 Muster am selben Teil.\n\n6  Weave Test erzeugt die Probenserie mit Protokoll, Joint Data die Kopplungen für die IGA-Rechnung.", 0, -90, 330, 380);
    var part = new Param_Geometry { NickName = "Teil" }; place(part, 420, -30); part.PersistentData.Append(new GH_Mesh((Mesh)demo.GetMethod("SandwichPanel").Invoke(null, new object[] { new Point3d(150, 150, 0), 5.0 })));
    var ps = L("PrintSettingsComponent", 420, 260); var sl = L("SliceComponent", 620, 60); var wa = L("WallsComponent", 800, 60); var cp = L("ContinuousPathComponent", 980, 60);
    var mp = L("MachineProfileComponent", 420, 700); intv(mp, "Preset", 4); I(ps, "Machine Profile").AddSource(mp.Params.Output[0]); // Ginger One: the E values of the G-code fit the machine
    sl.Params.Input[0].AddSource(part); sl.Params.Input[1].AddSource(ps.Params.Output[0]); wa.Params.Input[0].AddSource(sl.Params.Output[0]); cp.Params.Input[0].AddSource(wa.Params.Output[0]); cp.Params.Input[1].AddSource(ps.Params.Output[0]); wa.Params.Input[1].AddSource(ps.Params.Output[0]); intv(wa, "Position", 1); intv(cp, "Mode", 1);
    group("1  TEIL UND LAGEN", col(10, 132, 255), new IGH_DocumentObject[] { part, ps, mp, sl, wa, cp });
    var wz = L("WeaveZonesComponent", 1200, 60); var we = L("WeaveComponent", 1480, 60); var pat = list("Muster", new[] { "Shift", "Width", "Snake", "Teeth", "Custom", "Plain" }, 0, 1340, 560); var amp = slider("Amplitude (0 = auto)", 0, 5, 0, 2, 1290, 600); var wl = slider("Wavelength (0 = auto)", 0, 200, 0, 0, 1290, 640);
    wz.Params.Input[0].AddSource(cp.Params.Output[0]); we.Params.Input[0].AddSource(cp.Params.Output[0]); wire(wz, "Zones", we, "Zones"); I(we, "Pattern").AddSource(pat); I(we, "Amplitude").AddSource(amp); I(we, "Wavelength").AddSource(wl);
    group("2  STELLEN FINDEN", col(48, 176, 110), new IGH_DocumentObject[] { wz }); group("3  VERWEBEN", col(232, 135, 12), new IGH_DocumentObject[] { we, pat, amp, wl });
    var se = L("StartEndComponent", 1760, 60); se.Params.Input[0].AddSource(we.Params.Output[0]); var ck = L("PathCheckComponent", 1980, 60); ck.Params.Input[0].AddSource(se.Params.Output[0]); var gc = L("GingerGCodeComponent", 1980, 260); gc.Params.Input[0].AddSource(se.Params.Output[0]);
    group("4  SKIRT, ENDE, PRÜFEN, AUSGABE", col(120, 130, 145), new IGH_DocumentObject[] { se, ck, gc });
    var p1 = panel("STELLEN", "", 1180, 760, 430, 170); p1.AddSource(O(wz, "Info")); var p2 = panel("VERWOBEN: ERGEBNIS", "", 1640, 760, 560, 300); p2.AddSource(O(we, "Info"));
    var wv = L("WeaveVariantsComponent", 1200, 1180); wv.Params.Input[0].AddSource(cp.Params.Output[0]); wire(wz, "Zones", wv, "Zones"); var p3 = panel("14 VARIANTEN IM VERGLEICH", "", 1420, 1140, 1250, 320); p3.AddSource(O(wv, "Table")); p3.Properties.Wrap = false; p3.Properties.Font = new Font("Courier New", 7);
    group("5  VARIANTEN GEGENEINANDER", col(150, 110, 200), new IGH_DocumentObject[] { wv, p3 });
    var jd = L("JointDataComponent", 1200, 1720); jd.Params.Input[0].AddSource(we.Params.Output[0]); wire(we, "Zones", jd, "Zones"); var p4 = panel("NÄHTE FÜR IGA", "", 1480, 1560, 700, 320); p4.AddSource(O(jd, "Info"));
    group("6  NÄHTE FÜR DIE IGA-RECHNUNG", col(230, 200, 40), new IGH_DocumentObject[] { jd, p4 });
    var wt = L("WeaveTestComponent", 420, 1040); wt.Params.Input[0].AddSource(ps.Params.Output[0]); var p5 = panel("PROBENSERIE", "", 640, 900, 460, 420); p5.AddSource(O(wt, "Info")); wt.Hidden = true;
    group("7  PROBENSERIE (195 Proben, Protokoll)", col(220, 60, 60), new IGH_DocumentObject[] { wt, p5 });
    foreach (var h in new[] { sl, wa, cp, wz }) h.Hidden = h != wz;
    save("Weave (beads that interlock).gh");

    // ------------------------------------------------------------ 2 reshape
    doc = new GH_Document();
    write("RESHAPE: DRUCKHÖHE, WO SIE GEBRAUCHT WIRD", 0, -190, 30);
    write("Gleich viele Lagen, gleich viel Material. Die Lagen fächern auf, gedruckt wird nonplanar (Portal) oder multiaxial (Roboter)", 0, -140, 13);
    panel("SO GEHT ES", "1  Teil liegend im Vasenmodus. Die Druckhöhe ist die Tiefe des Teils.\n\n2  Shape Field: wo mehr, wo weniger. Hier ein Profil entlang einer Linie. Aus der Rechnung: Mode 'Utilisation at points' (Punkte und Auslastung).\n\n3  Reshape Path formt die fertige Bahn um. Skirt und Weg ins Teil bleiben unberührt, die Extrusion wird aus dem Abstand zur Lage darunter neu gerechnet. Material 100 = gleich viel.\n\n4  Multi-Axis Check: Kopf frei? Neigung, Drehgeschwindigkeit, Lagenhöhen.\n\n5  Compare Variants: acht Wege gegeneinander, eine Tabelle.\n\n6  Reshape Code: dasselbe für fertigen G-Code oder .dxr.\n\n7  Neu slicen: das gebogene Teil mit der neuen Oberkante als Leitfläche.", 0, -90, 330, 420);
    part = new Param_Geometry { NickName = "Teil" }; place(part, 420, -30); part.PersistentData.Append(new GH_Brep((Brep)demo.GetMethod("ReshapeBox").Invoke(null, new object[] { new Point3d(350, 400, 0) })));
    ps = L("PrintSettingsComponent", 420, 260); sl = L("SliceComponent", 620, 60); wa = L("WallsComponent", 800, 60); cp = L("ContinuousPathComponent", 980, 60); se = L("StartEndComponent", 1180, 60);
    sl.Params.Input[0].AddSource(part); sl.Params.Input[1].AddSource(ps.Params.Output[0]); wa.Params.Input[0].AddSource(sl.Params.Output[0]); wa.Params.Input[1].AddSource(ps.Params.Output[0]); cp.Params.Input[0].AddSource(wa.Params.Output[0]); cp.Params.Input[1].AddSource(ps.Params.Output[0]); se.Params.Input[0].AddSource(cp.Params.Output[0]); intv(wa, "Position", 1); intv(cp, "Mode", 0);
    group("1  TEIL UND BAHN (Vase, Skirt)", col(10, 132, 255), new IGH_DocumentObject[] { part, ps, sl, wa, cp, se });
    var sf = L("ShapeFieldComponent", 1180, 800); intv(sf, "Mode", 3); ((Param_Line)I(sf, "Line")).PersistentData.Append(new GH_Line(new Line(new Point3d(350, 500, 0), new Point3d(950, 500, 0)))); num(sf, "Positions", new[] { 0, 0.5, 1.0 }); num(sf, "Values", new[] { 1.25, 1.1, 0.5 });
    group("2  WO MEHR, WO WENIGER (links gehalten, rechts frei)", col(48, 176, 110), new IGH_DocumentObject[] { sf });
    var rs = L("ReshapePathComponent", 1640, 240); rs.Params.Input[0].AddSource(se.Params.Output[0]); wire(sf, "Field", rs, "Field"); var va = list("Variante", new[] { "0 Gefächert, Düse geneigt", "1 Gefächert, Düse senkrecht", "2 Gefächert + breiter, geneigt", "3 Gefächert + breiter, senkrecht", "4 Nur breiter, flach", "5 Mit Zusatzlagen, geneigt", "6 Mit Zusatzlagen, senkrecht", "7 Flach, Lagen gekürzt" }, 0, 1400, 900); var mt = slider("Material %", 60, 130, 100, 0, 1400, 940); I(rs, "Variant").AddSource(va); I(rs, "Material").AddSource(mt); str(rs, "Name", "Rechteck");
    var bm = new Param_Mesh { NickName = "Teil als Netz" }; place(bm, 1400, 1000); foreach (var m in Mesh.CreateFromBrep((Brep)demo.GetMethod("ReshapeBox").Invoke(null, new object[] { new Point3d(350, 400, 0) }), new MeshingParameters { MaximumEdgeLength = 8, JaggedSeams = false })) bm.PersistentData.Append(new GH_Mesh(m)); bm.Hidden = true; I(rs, "Mesh").AddSource(bm);
    group("3  UMFORMEN", col(232, 135, 12), new IGH_DocumentObject[] { rs, va, mt, bm });
    var mac = L("MultiAxisCheckComponent", 2000, 100); mac.Params.Input[0].AddSource(rs.Params.Output[0]); ck = L("PathCheckComponent", 2000, 440); ck.Params.Input[0].AddSource(rs.Params.Output[0]); p1 = panel("ERGEBNIS", "", 1800, 640, 620, 320); p1.AddSource(O(rs, "Report"));
    group("4  PRÜFEN", col(120, 130, 145), new IGH_DocumentObject[] { mac, ck, p1 });
    var mem = new List<IGH_DocumentObject>(); var rcell = cell(2520, 60, mem); var pr = L("RobotProgramComponent", 3260, 160); pr.Params.Input[0].AddSource(rs.Params.Output[0]); pr.Params.Input[1].AddSource(rcell.Params.Output[0]); mem.Add(pr); var sim = L("RobotSimulationComponent", 3540, 160); sim.Params.Input[0].AddSource(pr.Params.Output.First(p => p.Name.Contains("Program"))); mem.Add(sim);
    group("5  ROBOTER (Zelle FH Münster): Reichweite, Achsen, DXR", col(220, 60, 60), mem.ToArray());
    gc = L("MachineGCodeComponent", 1180, 1320); /* Machine G-code: empty Machine = Ginger One */ gc.Params.Input[0].AddSource(se.Params.Output[0]); var rc2 = L("ReshapeCodeComponent", 1640, 1380); I(rc2, "Code").AddSource(gc.Params.Output[0]); wire(sf, "Field", rc2, "Field"); intv(rc2, "Variant", 1); p2 = panel("CODE UMGEFORMT", "", 1860, 1220, 620, 320); p2.AddSource(O(rc2, "Report"));
    group("6  FERTIGEN G-CODE ODER .DXR UMFORMEN", col(150, 110, 200), new IGH_DocumentObject[] { gc, rc2, p2 });
    var cv = L("CompareVariantsComponent", 1640, 1940); cv.Params.Input[0].AddSource(se.Params.Output[0]); wire(sf, "Field", cv, "Fields"); p3 = panel("ACHT WEGE GEGENEINANDER", "", 1860, 1780, 1350, 260); p3.AddSource(O(cv, "Table")); p3.Properties.Wrap = false; p3.Properties.Font = new Font("Courier New", 7);
    group("7  VARIANTEN GEGENEINANDER", col(230, 200, 40), new IGH_DocumentObject[] { cv, p3 });
    var s2 = L("SliceComponent", 1860, 2380); wire(rs, "Mesh", s2, s2.Params.Input[0].Name); s2.Params.Input[1].AddSource(ps.Params.Output[0]); wire(rs, "Top", s2, "Guide"); var w2 = L("WallsComponent", 2060, 2380); w2.Params.Input[0].AddSource(s2.Params.Output[0]); w2.Params.Input[1].AddSource(ps.Params.Output[0]); intv(w2, "Position", 1); var c2 = L("ContinuousPathComponent", 2260, 2380); c2.Params.Input[0].AddSource(w2.Params.Output[0]); c2.Params.Input[1].AddSource(ps.Params.Output[0]); intv(c2, "Mode", 0); var k2 = L("PathCheckComponent", 2480, 2380); k2.Params.Input[0].AddSource(c2.Params.Output[0]);
    group("8  NEU SLICEN: gebogenes Teil, neue Oberkante als Leitfläche", col(10, 132, 255), new IGH_DocumentObject[] { s2, w2, c2, k2 });
    foreach (var h in new[] { sl, wa, cp, se, s2, w2, c2, cv, rc2, sf }) h.Hidden = true;
    save("Reshape (print height where it is needed).gh");
}
finally { sup?.SetValue(null, false); }
