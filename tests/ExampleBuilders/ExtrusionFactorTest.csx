// Builds SETUP/LARGERslicer Extrusion Factor Test.gh: single walls printed with three factors each on the
// Ginger One and on the Weber DXR robot, and the table that turns the measured bead widths into the new factor.
// Run inside Rhino 8 with Grasshopper 1 and LARGERslicer loaded. The file needs only LARGER.slicer.
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using System.Drawing;
// BODY
string OUT = "/Users/moritz/Documents/LARGER.slicer/SETUP/LARGERslicer Extrusion Factor Test.gh";
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "LARGERslicer");
var sup = asm.GetTypes().First(x => x.Name == "SlicerComponent").GetField("SuppressAutoInputs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
sup.SetValue(null, true);
var doc = new GH_Document();
string log = "";
try {
// ---------- helpers (as in SetupDefinitionV2.csx)
Func<IGH_DocumentObject, float, float, IGH_DocumentObject> place = (obj, x, y) => { obj.CreateAttributes(); obj.Attributes.Pivot = new PointF(x, y); doc.AddObject(obj, false); return obj; };
Func<string, GH_Component> L = name => (GH_Component)Activator.CreateInstance(asm.GetTypes().First(t => t.Name == name));
Action<string, float, float, float> write = (text, x, y, size) => { var sc = new GH_Scribble { Text = text, Font = new Font("Arial", size, size >= 30 ? FontStyle.Bold : FontStyle.Regular) }; place(sc, x, y); };
Func<string, double, double, double, int, float, float, GH_NumberSlider> slider = (name, min, max, val, dec, x, y) => { var s = new GH_NumberSlider(); s.NickName = name; s.Slider.DecimalPlaces = dec; s.Slider.Type = dec == 0 ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float; s.Slider.Minimum = (decimal)min; s.Slider.Maximum = (decimal)max; s.SetSliderValue((decimal)val); place(s, x, y); return s; };
Func<string, float, float, GH_ButtonObject> button = (name, x, y) => { var b = new GH_ButtonObject(); b.NickName = name; place(b, x, y); return b; };
var yellow = Color.FromArgb(255, 255, 250, 90);
Func<string, string, float, float, float, float, bool, GH_Panel> panel = (name, text, x, y, w, h, hi) => { var p = new GH_Panel(); p.NickName = name; p.UserText = text; p.Properties.Multiline = true; p.Properties.Wrap = true; p.Properties.Alignment = GH_Panel.Alignment.Left; if (hi) p.Properties.Colour = yellow; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, w, h); doc.AddObject(p, false); return p; };
Action<string, Color, IGH_DocumentObject[]> group = (title, colour, members) => { var g = new GH_Group { NickName = title, Colour = colour }; g.CreateAttributes(); doc.AddObject(g, false); foreach (var m in members) g.AddObject(m.InstanceGuid); g.ExpireCaches(); };
Action<IGH_Param, double> setNum = (p, v) => { var q = (Param_Number)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Number(v)); };
Action<IGH_Param, int> setInt = (p, v) => { var q = (Param_Integer)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Integer(v)); };
Action<IGH_Param, string> setStr = (p, v) => { var q = (Param_String)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_String(v)); };
Action<IGH_Param, double> setNumGen = (p, v) => { p.GetType().GetMethod("SetPersistentData", new[] { typeof(object[]) }).Invoke(p, new object[] { new object[] { new GH_Number(v) } }); };
Action<IGH_Param, IGH_Param> wire = (target, source) => target.AddSource(source);
Action<IGH_DocumentObject> hide = o => { var pv = o as IGH_PreviewObject; if (pv != null) pv.Hidden = true; };
Func<GH_Component, string, IGH_Param> In = (c, n) => c.Params.Input.First(p => p.Name == n);
Func<GH_Component, string, IGH_Param> Out = (c, n) => c.Params.Output.First(p => p.Name == n);
Color blue = Color.FromArgb(70, 10, 132, 255), green = Color.FromArgb(70, 48, 176, 110), orange = Color.FromArgb(70, 232, 135, 12), red = Color.FromArgb(70, 210, 63, 69);
var inv = System.Globalization.CultureInfo.InvariantCulture;

// ---------- head
write("EXTRUSION FACTOR TEST", 200, 90, 60);
write("Single walls with three factors each, for the Ginger One and the Weber DXR robot.\nPrint, measure the wall thickness, type it in: the file gives the new factor.", 205, 180, 20);
panel("HOW TO", "HOW TO\n\n1  Set the bead and the speed you print with (blue group). Every wall is 120 x 40 mm with round corners (R 15) and 30 mm high, printed as a spiral (vase): one bead, no seam.\n\n2  GINGER ONE: press Export G-Code in each of the three orange rows. Three files land on the desktop; the walls stand next to each other on the bed (Y 100, 200, 300 mm). Print them one after another.\n\n3  WEBER DXR: read the robot report and the FILE CHECK of each red row (must say GO), then press Save DXR to desktop. Keep P1 = 1 on the controller, so the factor in the file is the one you test. The walls stand at Y 250, 350, 450 mm.\n\n4  Let the walls cool. Measure the wall thickness with a calliper on the long sides, at half height, 4 to 6 places per wall, away from the corners.\n\n5  Type the values into the yellow panel of the wall, one value per line, with a decimal point (5.2). NEW FACTOR = target width / measured width x factor of the wall. The three walls of one machine should give nearly the same new factor. If they do not, the extruder does not follow the factor in a straight line: note it.\n\n6  Print one wall again with the new factor to check it. Then use it as Factor in Machine Profile (in the first definition: the slider 'Wert aus Cura für Roboter' or 'Faktor Cura Ginger').\n\nWrite down what you used: material, temperatures, speed, nozzle, P1 on the controller.\nBed: PLA 60-70 °C, PETG 80-90 °C.", 1120, 60, 640, 640, true);

float sx = 200, sy = 340;
var wB = slider("Bead Width (mm)", 2, 10, 5, 1, sx, sy);
var hB = slider("Layer Height (mm)", 0.5, 5, 2, 1, sx, sy + 40);
var vB = slider("Speed (mm/s)", 5, 100, 30, 0, sx, sy + 80);
var tipS = panel("Tip", "The factor belongs to the material, the nozzle and roughly the speed. Test again when one of them changes.", sx, sy + 130, 300, 70, false);
group("PRINT SETTINGS FOR ALL WALLS", blue, new IGH_DocumentObject[] { wB, hB, vB, tipS });

// ---------- one wall: geometry, profile, settings, path, check; returns Start and End
Func<int, double, double, double, float, string, List<IGH_DocumentObject>, Tuple<IGH_Param, GH_NumberSlider>> wall = (preset, factor, bx, by, y, label, mem) =>
{
    var geo = new Param_Brep(); geo.NickName = "Wall " + label; place(geo, 1840, y); { var outline = new Rectangle3d(new Plane(new Point3d(bx, by, 0), Vector3d.XAxis, Vector3d.YAxis), new Interval(0, 120), new Interval(0, 40)).ToNurbsCurve(); var rounded = Curve.CreateFilletCornersCurve(outline, 15, 0.01, 0.1) ?? outline; geo.PersistentData.Append(new GH_Brep(Extrusion.Create(rounded, 30, true).ToBrep())); } hide(geo);
    var fs = slider("Factor " + label, preset == 4 ? 0.05 : 0.5, preset == 4 ? 0.5 : 2.0, factor, 3, 1840, y + 40);
    var mp = L("MachineProfileComponent"); place(mp, 2100, y); setInt(In(mp, "Preset"), preset); wire(In(mp, "Factor"), fs);
    var ps = L("PrintSettingsComponent"); place(ps, 2320, y); wire(ps.Params.Input[0], wB); wire(ps.Params.Input[1], hB); wire(ps.Params.Input[2], vB); wire(ps.Params.Input[11], mp.Params.Output[0]); wire(ps.Params.Input[13], hB); wire(ps.Params.Input[15], vB);
    var sl = L("SliceComponent"); place(sl, 2540, y); wire(sl.Params.Input[0], geo); wire(sl.Params.Input[1], ps.Params.Output[0]);
    var wa = L("WallsComponent"); place(wa, 2740, y); wire(wa.Params.Input[0], sl.Params.Output[0]); wire(wa.Params.Input[1], ps.Params.Output[0]); setInt(wa.Params.Input[3], 1);
    var cp = L("ContinuousPathComponent"); place(cp, 2940, y); wire(cp.Params.Input[0], wa.Params.Output[0]); wire(cp.Params.Input[1], ps.Params.Output[0]); setInt(cp.Params.Input[2], 0);
    var se = L("StartEndComponent"); place(se, 3140, y); wire(se.Params.Input[0], cp.Params.Output[0]); setInt(se.Params.Input[1], 1);
    var ch = L("PathCheckComponent"); place(ch, 3340, y + 120); wire(ch.Params.Input[0], se.Params.Output[0]);
    var pc = panel("PATH CHECK " + label + " (must say OK)", "", 3540, y + 90, 360, 120, false); wire(pc, ch.Params.Output[1]);
    hide(sl); hide(wa); hide(cp);
    mem.AddRange(new IGH_DocumentObject[] { geo, fs, mp, ps, sl, wa, cp, se, ch, pc });
    return Tuple.Create(se.Params.Output[0], fs);
};

// ---------- measuring: measured widths -> Material Factor -> new factor
Action<float, string, GH_NumberSlider, List<IGH_DocumentObject>> measure = (y, label, fs, mem) =>
{
    var pm = panel("MEASURED " + label + " (mm, one per line)", "", 4900, y, 170, 150, true); pm.Properties.Multiline = false;
    // empty lines are left out, so an empty panel means "not measured yet" and not a conversion error
    Func<string, string, GH_Component> nat = (n, cat) => (GH_Component)Grasshopper.Instances.ComponentServer.ObjectProxies.First(q => q.Desc.Name == n && q.Desc.Category == cat && !q.Obsolete).CreateInstance();
    var tl = nat("Text Length", "Sets"); place(tl, 5100, y + 170); wire(tl.Params.Input[0], pm);
    var gt = nat("Larger Than", "Maths"); place(gt, 5170, y + 170); wire(gt.Params.Input[0], tl.Params.Output[0]); setNumGen(gt.Params.Input[1], 0);
    var cu = nat("Cull Pattern", "Sets"); place(cu, 5250, y + 170); wire(cu.Params.Input[0], pm); wire(cu.Params.Input[1], gt.Params.Output[0]);
    var mf = L("MaterialFactorComponent"); place(mf, 5140, y + 40);
    wire(In(mf, "Target Width"), wB); wire(In(mf, "Measured Widths"), cu.Params.Output[0]); wire(In(mf, "Current Factor"), fs);
    mem.AddRange(new IGH_DocumentObject[] { tl, gt, cu });
    var pn = panel("NEW FACTOR " + label, "", 5360, y, 160, 40, true); wire(pn, Out(mf, "New Factor"));
    var pr = panel("Report " + label, "", 5360, y + 50, 420, 120, false); wire(pr, Out(mf, "Report"));
    mem.AddRange(new IGH_DocumentObject[] { pm, mf, pn, pr });
};

// ---------- Ginger One
var gFactors = new[] { 0.18, 0.21, 0.24 };
write("A   GINGER ONE  -  three walls, three G-code files", 1840, 820, 25);
var mG = new List<IGH_DocumentObject>(); var mGm = new List<IGH_DocumentObject>();
for (int i = 0; i < 3; i++)
{
    float y = 900 + i * 300; string label = "Ginger " + gFactors[i].ToString("0.000", inv);
    var r = wall(4, gFactors[i], 100, 100 + i * 100, y, label, mG);
    var job = panel("Job name " + (i + 1), "Factor_Ginger_" + gFactors[i].ToString("0.000", inv).Replace(".", "_"), 3940, y - 20, 240, 30, false);
    var exp = button("Export G-Code " + (i + 1), 3940, y + 30);
    var gin = L("GingerGCodeComponent"); place(gin, 4200, y); wire(gin.Params.Input[0], r.Item1); wire(gin.Params.Input[2], exp); wire(gin.Params.Input[3], job);
    var pg = panel("G-code info " + (i + 1), "", 4420, y - 20, 330, 90, false); wire(pg, gin.Params.Output[1]);
    mG.AddRange(new IGH_DocumentObject[] { job, exp, gin, pg });
    measure(y, label, r.Item2, mGm);
}
group("A   GINGER ONE  -  read the path checks, then press Export G-Code", orange, mG.ToArray());
group("A   MEASURE (Ginger One)", green, mGm.ToArray());

// ---------- Weber DXR robot
var rFactors = new[] { 1.0, 1.128, 1.258 };
float yR = 900 + 3 * 300 + 200;
write("B   WEBER DXR ROBOT  -  three walls, three DXR files (P1 = 1 on the controller)", 1840, yR - 80, 25);
var mR = new List<IGH_DocumentObject>(); var mRm = new List<IGH_DocumentObject>();
// the robot cell of the FH Muenster with the AE20 extruder (as in SetupDefinitionV2.csx)
var cg = L("CellGeometryComponent"); place(cg, 1840, yR + 1160);
var robot = L("RobotArmComponent"); place(robot, 2080, yR + 1100);
var tool = L("RobotToolComponent"); place(tool, 2080, yR + 1230);
var rc = L("RobotCellComponent"); place(rc, 2320, yR + 1160);
wire(robot.Params.Input[2], cg.Params.Output[2]);
setStr(tool.Params.Input[0], "AE20_Extruder");
{ var pp = (Param_Plane)tool.Params.Input[1]; pp.PersistentData.Clear(); pp.PersistentData.Append(new GH_Plane(new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis))); }
setNum(tool.Params.Input[2], 46);
{ var pt = (Param_Point)tool.Params.Input[3]; pt.PersistentData.Clear(); pt.PersistentData.Append(new GH_Point(new Point3d(-61, -28, 113))); }
wire(rc.Params.Input[0], robot.Params.Output[0]); wire(rc.Params.Input[1], tool.Params.Output[0]); wire(rc.Params.Input[2], cg.Params.Output[3]); wire(rc.Params.Input[4], cg.Params.Output[0]);
hide(tool); hide(robot);
var sBed = slider("Bed Temperature", 0, 100, 60, 0, 1840, yR + 1330);
var sNoz = slider("Nozzle Temperature", 0, 400, 230, 0, 1840, yR + 1360);
var sFan = slider("Cooling Fan %", 0, 100, 33, 0, 1840, yR + 1390);
var ms = L("MachineSettingsComponent"); place(ms, 2100, yR + 1360); wire(ms.Params.Input[0], sBed); wire(ms.Params.Input[1], sNoz); wire(ms.Params.Input[2], sFan);
mR.AddRange(new IGH_DocumentObject[] { cg, robot, tool, rc, sBed, sNoz, sFan, ms });
for (int i = 0; i < 3; i++)
{
    float y = yR + i * 360; string label = "Robot " + rFactors[i].ToString("0.000", inv);
    var r = wall(3, rFactors[i], 500, 250 + i * 100, y, label, mR);
    var job = panel("Job name R" + (i + 1), "Factor_Robot_" + rFactors[i].ToString("0.000", inv).Replace(".", "_"), 3940, y - 20, 240, 30, false);
    var prog = L("RobotProgramComponent"); place(prog, 3940, y + 40); wire(prog.Params.Input[0], r.Item1); wire(prog.Params.Input[1], rc.Params.Output[0]); wire(prog.Params.Input[3], job);
    var dxr = L("DXRGeneratorComponent"); place(dxr, 4200, y + 40);
    wire(dxr.Params.Input[0], prog.Params.Output[0]); wire(dxr.Params.Input[1], prog.Params.Output[1]); wire(dxr.Params.Input[2], prog.Params.Output[2]); wire(dxr.Params.Input[3], ms.Params.Output[0]); wire(dxr.Params.Input[4], prog.Params.Output[11]);
    var save = button("Save DXR to desktop " + (i + 1), 3940, y + 200); wire(dxr.Params.Input[5], save);
    var hc = L("DXRFileHealthCheckComponent"); place(hc, 4420, y + 40); wire(hc.Params.Input[7], dxr.Params.Output[0]);
    var pCheck = panel("FILE CHECK R" + (i + 1) + " (must say GO)", "", 4620, y - 20, 240, 40, false); wire(pCheck, hc.Params.Output[1]);
    var pRobot = panel("Robot report R" + (i + 1), "", 4620, y + 30, 240, 220, false); wire(pRobot, prog.Params.Output[5]);
    mR.AddRange(new IGH_DocumentObject[] { job, prog, dxr, save, hc, pCheck, pRobot });
    measure(y, label, r.Item2, mRm);
}
group("B   WEBER DXR ROBOT  -  read the reports, then press Save DXR to desktop", red, mR.ToArray());
group("B   MEASURE (Weber DXR)", green, mRm.ToArray());

// long wires faint, as in V2
foreach (var o in doc.Objects)
{
    var pl = new List<IGH_Param>(); if (o is IGH_Component cc) pl.AddRange(cc.Params.Input); else if (o is IGH_Param fp) pl.Add(fp);
    foreach (var q in pl) { var me = q.Attributes.GetTopLevel.Pivot; if (q.Sources.Any(src => { var sp = src.Attributes.GetTopLevel.Pivot; return Math.Abs(sp.X - me.X) > 520 || Math.Abs(sp.Y - me.Y) > 420; })) q.WireDisplay = GH_ParamWireDisplay.faint; }
}
foreach (var o in doc.Objects) o.ExpireSolution(false);
var arcOut = new GH_IO.Serialization.GH_Archive(); arcOut.AppendObject(doc, "Definition"); arcOut.WriteToFile(OUT, true, false);
log += "written " + doc.ObjectCount + " objects, " + new System.IO.FileInfo(OUT).Length + " bytes";
} finally { sup.SetValue(null, false); doc.Dispose(); }
Console.WriteLine(log);
