using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using System.Drawing;

string OLD = "/Users/moritz/Documents/LARGER.slicer/SETUP/LARGERslicer Weber Robot and Ginger.gh";
string OUT = "/Users/moritz/Documents/LARGER.slicer/SETUP/LARGERslicer Weber Robot and Ginger V2.gh";
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "LARGERslicer");
var sup = asm.GetTypes().First(x => x.Name == "SlicerComponent").GetField("SuppressAutoInputs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
sup.SetValue(null, true);
var doc = new GH_Document();
try {
// ---------- example geometry of the old definition
var old = new GH_Document(); { var a = new GH_IO.Serialization.GH_Archive(); a.ReadFromFile(OLD); a.ExtractObject(old, "Definition"); }
Func<string, IGH_Goo> oldData = id => { var o = old.Objects.OfType<IGH_Param>().First(p => p.InstanceGuid.ToString().StartsWith(id)); var pd = o.GetType().GetProperty("PersistentData").GetValue(o) as Grasshopper.Kernel.Data.IGH_Structure; return pd.AllData(true).First(); };
Func<GeometryBase, double, double, GeometryBase> onBed = (g, x, y) => { var d = g.Duplicate(); var bb = d.GetBoundingBox(true); d.Translate(x - bb.Min.X, y - bb.Min.Y, -bb.Min.Z); return d; };
var geo1 = (Brep)onBed(((GH_Brep)oldData("d1f0")).Value, 150, 300);
var geo1b = (Brep)onBed(((GH_Brep)oldData("5ffc")).Value, 150, 520);
var geo2 = (Brep)onBed(((GH_Brep)oldData("6ced")).Value, 600, 300);
var geo2b = (Brep)onBed(((GH_Surface)oldData("6743")).Value, 850, 300);
var crvSkirt = ((GH_Curve)oldData("0aa9")).Value.DuplicateCurve();
var crvPath = ((GH_Curve)oldData("9d42")).Value.DuplicateCurve();
old.Dispose();

// ---------- helpers
Func<IGH_DocumentObject, float, float, IGH_DocumentObject> place = (obj, x, y) => { obj.CreateAttributes(); obj.Attributes.Pivot = new PointF(x, y); doc.AddObject(obj, false); return obj; };
Func<string, GH_Component> L = name => (GH_Component)Activator.CreateInstance(asm.GetTypes().First(t => t.Name == name));
Func<string, GH_Component> N = name => (GH_Component)Grasshopper.Instances.ComponentServer.ObjectProxies.First(p => p.Desc.Name == name && !p.Obsolete && p.Kind == GH_ObjectType.CompiledObject && p.Type.Assembly.GetName().Name != "LARGERslicer").CreateInstance();
Action<string, float, float, float> write = (text, x, y, size) => { var sc = new GH_Scribble { Text = text, Font = new Font("Arial", size, size >= 30 ? FontStyle.Bold : FontStyle.Regular) }; place(sc, x, y); };
Func<string, double, double, double, int, float, float, GH_NumberSlider> slider = (name, min, max, val, dec, x, y) => { var s = new GH_NumberSlider(); s.NickName = name; s.Slider.DecimalPlaces = dec; s.Slider.Type = dec == 0 ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float; s.Slider.Minimum = (decimal)min; s.Slider.Maximum = (decimal)max; s.SetSliderValue((decimal)val); place(s, x, y); return s; };
Func<string, string[], string[], int, float, float, GH_ValueList> list = (name, names, values, sel, x, y) => { var v = new GH_ValueList(); v.NickName = name; v.ListItems.Clear(); for (int i = 0; i < names.Length; i++) v.ListItems.Add(new GH_ValueListItem(names[i], values[i])); v.SelectItem(sel); place(v, x, y); return v; };
Func<string, float, float, GH_ButtonObject> button = (name, x, y) => { var b = new GH_ButtonObject(); b.NickName = name; place(b, x, y); return b; };
var yellow = Color.FromArgb(255, 255, 250, 90);
Func<string, string, float, float, float, float, bool, GH_Panel> panel = (name, text, x, y, w, h, hi) => { var p = new GH_Panel(); p.NickName = name; p.UserText = text; p.Properties.Multiline = true; p.Properties.Wrap = true; p.Properties.Alignment = GH_Panel.Alignment.Left; if (hi) p.Properties.Colour = yellow; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, w, h); doc.AddObject(p, false); return p; };
Action<string, Color, IGH_DocumentObject[]> group = (title, colour, members) => { var g = new GH_Group { NickName = title, Colour = colour }; g.CreateAttributes(); doc.AddObject(g, false); foreach (var m in members) g.AddObject(m.InstanceGuid); g.ExpireCaches(); };
Action<IGH_Param, double> setNum = (p, v) => { var q = (Param_Number)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Number(v)); };
Action<IGH_Param, int> setInt = (p, v) => { var q = (Param_Integer)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_Integer(v)); };
Action<IGH_Param, string> setStr = (p, v) => { var q = (Param_String)p; q.PersistentData.Clear(); q.PersistentData.Append(new GH_String(v)); };
Action<IGH_Param, IGH_Param> wire = (target, source) => target.AddSource(source);
Action<IGH_DocumentObject> hide = o => { var pv = o as IGH_PreviewObject; if (pv != null) pv.Hidden = true; };
Color blue = Color.FromArgb(70, 10, 132, 255), green = Color.FromArgb(70, 48, 176, 110), orange = Color.FromArgb(70, 232, 135, 12), red = Color.FromArgb(70, 210, 63, 69), grey = Color.FromArgb(70, 120, 130, 145), violet = Color.FromArgb(70, 150, 110, 200);

// the robot cell of the FH Muenster with the AE20 extruder; returns Robot Cell
Func<float, float, List<IGH_DocumentObject>, GH_Component> cell = (x, y, members) =>
{
    var cg = L("CellGeometryComponent"); place(cg, x, y + 60);
    var robot = L("RobotArmComponent"); place(robot, x + 240, y);
    var tool = L("RobotToolComponent"); place(tool, x + 240, y + 130);
    var rc = L("RobotCellComponent"); place(rc, x + 480, y + 60);
    wire(robot.Params.Input[2], cg.Params.Output[2]);
    setStr(tool.Params.Input[0], "AE20_Extruder");
    { var pp = (Param_Plane)tool.Params.Input[1]; pp.PersistentData.Clear(); pp.PersistentData.Append(new GH_Plane(new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis))); }
    setNum(tool.Params.Input[2], 46);
    { var pt = (Param_Point)tool.Params.Input[3]; pt.PersistentData.Clear(); pt.PersistentData.Append(new GH_Point(new Point3d(-61, -28, 113))); }
    wire(rc.Params.Input[0], robot.Params.Output[0]); wire(rc.Params.Input[1], tool.Params.Output[0]);
    wire(rc.Params.Input[2], cg.Params.Output[3]); wire(rc.Params.Input[4], cg.Params.Output[0]);
    hide(tool); hide(robot);
    members.AddRange(new IGH_DocumentObject[] { cg, robot, tool, rc });
    return rc;
};
// Robot Program -> DXR Generator -> DXR File Health Check, simulation, machine settings. gate = optional "is the Weber profile chosen"
Func<float, float, IGH_Param, GH_Component, GH_Panel, double[], IGH_Param, List<IGH_DocumentObject>, GH_Component> dxrOut = (x, y, toolpath, rc, nameP, temps, gate, members) =>
{
    var prog = L("RobotProgramComponent"); place(prog, x, y);
    wire(prog.Params.Input[0], toolpath); wire(prog.Params.Input[1], rc.Params.Output[0]); wire(prog.Params.Input[3], nameP);
    var sBed = slider("Bed Temperature", 0, 100, temps[0], 0, x - 60, y + 360);
    var sNoz = slider("Nozzle Temperature", 0, 400, temps[1], 0, x - 60, y + 390);
    var sFan = slider("Cooling Fan %", 0, 100, temps[2], 0, x - 60, y + 420);
    var ms = L("MachineSettingsComponent"); place(ms, x + 250, y + 390);
    wire(ms.Params.Input[0], sBed); wire(ms.Params.Input[1], sNoz); wire(ms.Params.Input[2], sFan);
    var dxr = L("DXRGeneratorComponent"); place(dxr, x + 520, y + 60);
    wire(dxr.Params.Input[0], prog.Params.Output[0]); wire(dxr.Params.Input[1], prog.Params.Output[1]); wire(dxr.Params.Input[2], prog.Params.Output[2]);
    wire(dxr.Params.Input[3], ms.Params.Output[0]); wire(dxr.Params.Input[4], prog.Params.Output[11]);
    var save = button("Save DXR to desktop", x + 250, y + 250);
    members.AddRange(new IGH_DocumentObject[] { prog, sBed, sNoz, sFan, ms, dxr, save });
    if (gate != null) { var and = N("Gate And"); place(and, x + 400, y + 300); wire(and.Params.Input[0], save); wire(and.Params.Input[1], gate); wire(dxr.Params.Input[5], and.Params.Output[0]); members.Add(and); }
    else wire(dxr.Params.Input[5], save);
    var hc = L("DXRFileHealthCheckComponent"); place(hc, x + 800, y + 60);
    wire(hc.Params.Input[7], dxr.Params.Output[0]);
    var pCheck = panel("FILE CHECK (must say GO)", "", x + 1010, y - 60, 330, 60, false); wire(pCheck, hc.Params.Output[1]);
    var pRep = panel("File check report", "", x + 1010, y + 20, 330, 220, false); wire(pRep, hc.Params.Output[2]);
    var pRobot = panel("Robot report (reach, axes, collisions)", "", x + 250, y - 250, 420, 220, false); wire(pRobot, prog.Params.Output[5]);
    var sSim = slider("Preview Slider (0 = start, 1 = end)", 0, 1, 0.5, 3, x + 720, y + 330);
    var sim = L("RobotSimulationComponent"); place(sim, x + 1010, y + 360);
    wire(sim.Params.Input[0], prog.Params.Output[12]); wire(sim.Params.Input[1], sSim);
    members.AddRange(new IGH_DocumentObject[] { hc, pCheck, pRep, pRobot, sSim, sim });
    return dxr;
};

// ---------- head
write("LARGER.slicer", 196, 94, 72);
write("LARGE FORMAT 3D PRINTING ALGORITHM  -  VERSION 2\nBY MORITZ WESSELER - FH MÜNSTER\n\nInspired by Ginger.Additive\n\nUnderneath you find three ways to prepare your part\nfor a Ginger One 3D printer or the Weber DXR 3D printing robot.\n\n01. The Easy Slicer slices your geometry and creates the toolpath.\n\n02. The Advanced Multiaxial Robot Slicer does the same for the robot,\n      with layers that can lean (angle) or follow a guide.\n\n03. The Custom Robot Path Creator turns your own curves\n      into a robot program.\n\nVersion 2 needs only the LARGER.slicer plug-in (1.3.1 or newer).\nRobots and Pufferfish are no longer needed.", 201, 213, 25);
write("INSTRUCTIONS", 1485, 101, 72);
panel("INSTRUCTIONS", "INSTRUCTIONS\n\n1  Take one of the three scripts and copy it to a new file (select its groups, Ctrl+C, new file, Ctrl+V).\n\n2  Only the LARGER.slicer plug-in is needed (1.3.1 or newer). If components are missing: run the installer in the SETUP folder, restart Rhino.\n\n3  Work in a Rhino file in MILLIMETRES. The origin is the corner of the print bed: your part has to stand on Z = 0, on the positive side of the origin. For the robot, keep it near the middle of the bed (the example parts show where).\n\n4  Connect your geometry: replace the example in 'Geometry' (right click > Set one Geometry). Breps, surfaces, SubDs and meshes work, open or closed.\n\n5  Set the print settings with the sliders. Script 01 calculates the path twice, row A for the Ginger One and row B for the robot: take the file of your machine.\n\n6  READ THE REPORTS. 'Path Check' has to say OK, 'Robot report' must not show unreachable points or collisions, 'FILE CHECK' has to say GO. If not, the report says what to change.\n\n7  Move the Preview Slider to watch the robot in the Rhino viewport. Then press the button 'Save DXR to desktop' (robot) or 'Export G-Code' (Ginger One). The file lands on the Desktop; its name is made of DXR or GIN, date and time, your job name, weight and print time.\n\n8  CHECK YOUR FILE before printing, and never leave the machine alone on the first layers.\n\nGood to know\n- More settings sit on the components themselves: click a row such as 'Brim: None' to choose, or a number to type a value. A wire or slider into the same input wins.\n- Every component explains its inputs when you hover over them.\n- More examples: drop the component 'Quick Start' (Slicer tab) on an empty canvas.", 1490, 230, 760, 640, true);
{ var ip = doc.Objects.OfType<GH_Panel>().First(q => q.NickName == "INSTRUCTIONS"); ip.Properties.Font = new Font("Arial", 9f); }

// =====================================================================================
// 01
float y1 = 1180;
write("01. EASY GINGER AND ROBOT SLICER", 236, y1 - 97, 50);
write("1. INPUT GEOMETRY HERE\n\n[Right click > Set one Geometry]", 242, y1, 25);
var m1 = new List<IGH_DocumentObject>();
var g1 = new Param_Geometry { NickName = "Geometry" }; place(g1, 330, y1 + 190); g1.PersistentData.Append(new GH_Brep(geo1));
var g1b = new Param_Geometry { NickName = "Example Geometry 2 (not connected)" }; place(g1b, 330, y1 + 270); g1b.PersistentData.Append(new GH_Brep(geo1b)); hide(g1b);
write("2. SEAM POINT\n\nThe seam of every layer lies\nnext to this point.\n[Right click > Set one Point]", 242, y1 + 330, 25);
var seam1 = new Param_Point { NickName = "Seam Point" }; place(seam1, 330, y1 + 540); { var bb = geo1.GetBoundingBox(true); seam1.PersistentData.Append(new GH_Point(new Point3d(bb.Min.X - 30, bb.Center.Y, 0))); }
group("Input Your Geometry here (connected)", blue, new IGH_DocumentObject[] { g1 }); group("Example Geometry 2 (not connected)", grey, new IGH_DocumentObject[] { g1b }); group("Seam Point", blue, new IGH_DocumentObject[] { seam1 });

write("3D PRINT PATH MAKER", 760, y1, 25);
var w1 = slider("Extrusion Width (mm)", 0.5, 15, 5, 1, 760, y1 + 80);
var h1 = slider("Layer Height (mm)", 0.2, 10, 2, 2, 760, y1 + 110);
var h1a = slider("Initial Layer Height (mm)", 0.2, 10, 2, 2, 760, y1 + 140);
var f1 = slider("Feedrate / Speed (mm/s)", 1, 200, 60, 0, 760, y1 + 170);
var fl1 = slider("Flow (1 = 100 %)", 0.5, 1.5, 1, 2, 760, y1 + 200);
var res1 = slider("Point Spacing (mm) [Resolution]", 0.5, 10, 2, 1, 760, y1 + 230);
var rad1 = slider("Corner Fillet Radius (mm), 0 = auto", 0, 20, 0, 1, 760, y1 + 260);
var mode1 = list("Slicing", new[] { "Layered (seam)", "Spiralize (vase mode)" }, new[] { "1", "0" }, 0, 760, y1 + 320);
var skirt1 = slider("Skirt Loops (0 = no skirt)", 0, 5, 1, 0, 760, y1 + 360);
var brim1 = list("Brim", new[] { "No Brim", "Brim outside", "Brim inside", "Brim both sides" }, new[] { "0", "1", "2", "3" }, 0, 760, y1 + 400);
var tip1 = panel("Tip", "Extrusion width 5 / height 2 works best with the 3 mm nozzle.\nLayer height: a third to a half of the width.\n\nThese settings feed both machines: row A is calculated for the Ginger One, row B for the Weber DXR robot. Take the file of your machine.", 760, y1 + 450, 300, 150, false);
group("PRINT SETTINGS FOR BOTH MACHINES", blue, new IGH_DocumentObject[] { w1, h1, h1a, f1, fl1, res1, rad1, mode1, skirt1, brim1, tip1 });
// one row per machine: its machine profile decides what the extrusion values mean
Func<int, float, string, List<IGH_DocumentObject>, IGH_Param> row1 = (preset, y, title, members) =>
{
    var mp = L("MachineProfileComponent"); place(mp, 1180, y - 60); setInt(mp.Params.Input[0], preset);
    var ps = L("PrintSettingsComponent"); place(ps, 1400, y + 40);
    wire(ps.Params.Input[0], w1); wire(ps.Params.Input[1], h1); wire(ps.Params.Input[2], f1); wire(ps.Params.Input[3], fl1); wire(ps.Params.Input[5], rad1); wire(ps.Params.Input[6], res1); wire(ps.Params.Input[11], mp.Params.Output[0]); wire(ps.Params.Input[13], h1a);
    var sl = L("SliceComponent"); place(sl, 1640, y); wire(sl.Params.Input[0], g1); wire(sl.Params.Input[1], ps.Params.Output[0]);
    var wa = L("WallsComponent"); place(wa, 1860, y); wire(wa.Params.Input[0], sl.Params.Output[0]); wire(wa.Params.Input[1], ps.Params.Output[0]); setInt(wa.Params.Input[3], 1);
    var cp = L("ContinuousPathComponent"); place(cp, 2080, y); wire(cp.Params.Input[0], wa.Params.Output[0]); wire(cp.Params.Input[1], ps.Params.Output[0]); wire(cp.Params.Input[2], mode1); wire(cp.Params.Input[3], seam1);
    var se = L("StartEndComponent"); place(se, 2320, y); wire(se.Params.Input[0], cp.Params.Output[0]); wire(se.Params.Input[1], skirt1); wire(se.Params.Input[7], brim1);
    var ch = L("PathCheckComponent"); place(ch, 2560, y + 120); wire(ch.Params.Input[0], se.Params.Output[0]);
    var pc = panel("PATH CHECK " + title + " (must say OK)", "", 2780, y + 60, 380, 250, false); wire(pc, ch.Params.Output[1]);
    hide(sl); hide(wa); hide(cp);
    members.AddRange(new IGH_DocumentObject[] { mp, ps, sl, wa, cp, se, ch, pc });
    return se.Params.Output[0];
};
hide(g1);
var job1 = panel("Job Name (Continous_Text)", "Example_Geometry_Name_V1", 1180, y1 + 70, 300, 40, true);

float yA = y1 + 280;
write("A   GCODE MAKER  -  GINGER ONE", 1180, yA - 110, 25);
var mA = new List<IGH_DocumentObject>();
var tpA = row1(4, yA + 40, "A", mA);
var exp1 = button("Export G-Code", 3280, yA + 30);
var gin = L("GingerGCodeComponent"); place(gin, 3520, yA - 20); wire(gin.Params.Input[0], tpA); wire(gin.Params.Input[2], exp1); wire(gin.Params.Input[3], job1);
var pGin = panel("G-code info", "", 3740, yA - 60, 330, 100, false); wire(pGin, gin.Params.Output[1]);
mA.AddRange(new IGH_DocumentObject[] { exp1, gin, pGin });
group("A   GINGER ONE  -  path with the Ginger machine profile, then press Export G-Code", orange, mA.ToArray());

float yB = y1 + 1020;
write("B   DXR CODE MAKER  -  WEBER DXR ROBOT", 1180, yB - 110, 25);
var m1r = new List<IGH_DocumentObject>();
var tpB = row1(3, yB + 40, "B", m1r);
var rc1 = cell(1180, yB + 480, m1r);
dxrOut(2100, yB + 700, tpB, rc1, job1, new double[] { 90, 220, 16 }, null, m1r);
group("B   WEBER DXR ROBOT  -  path with the DXR25 machine profile; read the reports, then press Save DXR to desktop", red, m1r.ToArray());

float yV = y1 + 2000;
// variable widths, as before: switched off
write("Create Variable Layerwidths\n[BETA - enable the component and connect DXR of a DXR Generator to use it]", 3700, yV + 250, 25);
var vw = L("DXRVariableWidthComponent"); place(vw, 4060, yV + 450);
var vBase = slider("Base Width", 0.5, 20, 5, 1, 3710, yV + 380); var vS = slider("Width Start", 0.5, 30, 4, 1, 3710, yV + 410); var vE = slider("Width End", 0.5, 30, 10, 1, 3710, yV + 440);
var vAx = list("Axis", new[] { "X", "Y", "Z" }, new[] { "0", "1", "2" }, 0, 3830, yV + 480); var vMo = list("Mode", new[] { "Linear", "Smooth", "Gamma" }, new[] { "0", "1", "2" }, 0, 3830, yV + 520);
wire(vw.Params.Input[1], vBase); wire(vw.Params.Input[2], vS); wire(vw.Params.Input[3], vE); wire(vw.Params.Input[4], vAx); wire(vw.Params.Input[5], vMo);
group("Variable Layerwidths [BETA]", grey, new IGH_DocumentObject[] { vw, vBase, vS, vE, vAx, vMo });

// =====================================================================================
// 02
float y2 = 4050;
write("02. ADVANCED MULTIAXIAL ROBOT SLICER", 176, y2 - 150, 50);
write("1. INPUT GEOMETRY HERE\n\n[Right click > Set one Geometry]", 191, y2 - 30, 25);
var g2 = new Param_Geometry { NickName = "Geometry" }; place(g2, 330, y2 + 160); g2.PersistentData.Append(new GH_Brep(geo2));
var g2b = new Param_Geometry { NickName = "Example Geometry 2 (strong overhang, not connected)" }; place(g2b, 330, y2 + 240); g2b.PersistentData.Append(new GH_Brep(geo2b)); hide(g2b);
write("2. SEAM POINT", 191, y2 + 310, 25);
var seam2 = new Param_Point { NickName = "Seam Point" }; place(seam2, 330, y2 + 380); { var bb = geo2.GetBoundingBox(true); seam2.PersistentData.Append(new GH_Point(new Point3d(bb.Min.X - 30, bb.Center.Y, 0))); }
write("3. GUIDE (optional)\n\nFor multi-axis layers.\nA curve: the layers stand square to it,\nthe tool leans with them.\nA surface: the layers follow it.\nConnect it to 'Guide' of Slice.\n\nTry it: connect the multi-axis\nexample part to Geometry and its\nguide curve to Guide of Slice,\nand set 'spiralize' to Layered.", 191, y2 + 450, 20);
Brep maPart = null; Curve guideCrv = null;
{ var mi = asm.GetTypes().First(t => t.Name == "DemoParts").GetMethod("MultiAxis"); var args = new object[] { new Point3d(330, 620, 0), null, null }; mi.Invoke(null, args); maPart = (Brep)args[1]; guideCrv = (Curve)args[2]; }
var part2m = new Param_Geometry { NickName = "Multi-axis example part (not connected)" }; place(part2m, 330, y2 + 860); part2m.PersistentData.Append(new GH_Brep(maPart)); hide(part2m);
var guide2 = new Param_Curve { NickName = "Multi-axis example guide curve (not connected)" }; place(guide2, 330, y2 + 940); guide2.PersistentData.Append(new GH_Curve(guideCrv)); hide(guide2);
group("Input Your Geometry here (connected)", blue, new IGH_DocumentObject[] { g2 }); group("Example Geometry 2, strong overhang (not connected)", grey, new IGH_DocumentObject[] { g2b }); group("Seam Point", blue, new IGH_DocumentObject[] { seam2 }); group("Multi-axis example part (not connected)", grey, new IGH_DocumentObject[] { part2m }); group("Multi-axis example guide curve (not connected)", grey, new IGH_DocumentObject[] { guide2 });

write("3D PRINT PATH MAKER", 789, y2 - 60, 25);
var m2 = new List<IGH_DocumentObject>();
var h2a = slider("first layer height", 0.2, 10, 2, 2, 792, y2 + 70);
var h2 = slider("Layer Height", 0.2, 10, 2, 2, 792, y2 + 100);
var w2 = slider("Layer Width", 0.5, 15, 3.5, 1, 792, y2 + 130);
var res2 = slider("Resolution", 0.5, 10, 2, 1, 792, y2 + 160);
var ang2 = slider("Slice Angle (0 = flat, 45 = angled layers)", 0, 60, 0, 0, 792, y2 + 190);
var sk2 = slider("Skirt Loops (0 = no skirt)", 0, 5, 1, 0, 792, y2 + 220);
var skd2 = slider("skirt distance (0 = auto)", 0, 100, 0, 0, 792, y2 + 250);
var mode2 = list("spiralize", new[] { "Spiralize (vase mode)", "Layered (seam)" }, new[] { "0", "1" }, 0, 792, y2 + 290);
var f2a = slider("first layer speed mm/s", 1, 100, 30, 0, 792, y2 + 340);
var f2 = slider("print speed mm/s", 1, 100, 30, 0, 792, y2 + 370);
var mlt2 = slider("Minimum Layer Time (s)", 0, 100, 30, 0, 792, y2 + 400);
var mp2 = L("MachineProfileComponent"); place(mp2, 1400, y2 + 40); setInt(mp2.Params.Input[0], 3);
var ps2 = L("PrintSettingsComponent"); place(ps2, 1640, y2 + 140);
wire(ps2.Params.Input[0], w2); wire(ps2.Params.Input[1], h2); wire(ps2.Params.Input[2], f2); wire(ps2.Params.Input[6], res2); wire(ps2.Params.Input[11], mp2.Params.Output[0]); wire(ps2.Params.Input[13], h2a); wire(ps2.Params.Input[15], f2a);
var sl2 = L("SliceComponent"); place(sl2, 1180, y2 + 600); wire(sl2.Params.Input[0], g2); wire(sl2.Params.Input[1], ps2.Params.Output[0]); wire(sl2.Params.Input[3], ang2);
var wa2 = L("WallsComponent"); place(wa2, 1400, y2 + 600); wire(wa2.Params.Input[0], sl2.Params.Output[0]); wire(wa2.Params.Input[1], ps2.Params.Output[0]); setInt(wa2.Params.Input[3], 1);
var cp2 = L("ContinuousPathComponent"); place(cp2, 1620, y2 + 600); wire(cp2.Params.Input[0], wa2.Params.Output[0]); wire(cp2.Params.Input[1], ps2.Params.Output[0]); wire(cp2.Params.Input[2], mode2); wire(cp2.Params.Input[3], seam2); wire(cp2.Params.Input[7], mlt2);
var se2 = L("StartEndComponent"); place(se2, 1860, y2 + 600); wire(se2.Params.Input[0], cp2.Params.Output[0]); wire(se2.Params.Input[1], sk2); wire(se2.Params.Input[2], skd2);
var ch2 = L("PathCheckComponent"); place(ch2, 2100, y2 + 600); wire(ch2.Params.Input[0], se2.Params.Output[0]);
var pc2 = panel("PATH CHECK (must say OK)", "", 2100, y2 + 140, 380, 330, false); wire(pc2, ch2.Params.Output[1]);
hide(sl2); hide(wa2); hide(cp2); hide(g2);
m2.AddRange(new IGH_DocumentObject[] { h2a, h2, w2, res2, ang2, sk2, skd2, mode2, f2a, f2, mlt2, mp2, ps2, sl2, wa2, cp2, se2, ch2, pc2 });
group("3D PRINT PATH MAKER", blue, m2.ToArray());

write("DXR CODE MAKER", 2620, y2 - 60, 25);
var job2 = panel("Filename", "NON_PLANAR_EXAMPLE", 2620, y2 + 70, 300, 40, true);
var m2r = new List<IGH_DocumentObject> { job2 };
var rc2 = cell(2900, y2 + 180, m2r);
dxrOut(3700, y2 + 220, se2.Params.Output[0], rc2, job2, new double[] { 60, 230, 33 }, null, m2r);
group("DXR CODE MAKER  -  ROBOT SYSTEM, ROBOT PROGRAM, DXR GENERATOR  (read the reports, then press Save DXR to desktop)", red, m2r.ToArray());

// =====================================================================================
// 03
float y3 = 5650;
write("03. CUSTOM ROBOT PATH CREATOR", 208, y3 - 170, 50);
write("DXR CODE MAKER FROM LINE (ADVANCED!)", 204, y3 - 100, 25);
write("INPUT CURVES\n\nYour own path, drawn in Rhino\nwhere it is printed (bed corner =\norigin), flat on Z = 0: it is lifted\nby the layer height. The curves\nare printed in this order.", 227, y3, 25);
var c3a = new Param_Curve { NickName = "Curve 1 (skirt)" }; place(c3a, 330, y3 + 250); c3a.PersistentData.Append(new GH_Curve(crvSkirt));
var c3b = new Param_Curve { NickName = "Curve 2 (path)" }; place(c3b, 330, y3 + 330); c3b.PersistentData.Append(new GH_Curve(crvPath));
group("Curve 1 (skirt)", blue, new IGH_DocumentObject[] { c3a }); group("Curve 2 (path)", blue, new IGH_DocumentObject[] { c3b });
write("EXTRUSION (E-VALUE) AND FEEDRATE (SPEED)", 789, y3 - 30, 25);
var m3 = new List<IGH_DocumentObject>();
var fl3 = slider("Extrusion Multipler Factor", 0, 5, 1, 2, 792, y3 + 70);
var h3 = slider("Layer Heigth", 0.2, 5, 2.5, 2, 792, y3 + 100);
var w3 = slider("Layer Width", 0.5, 10, 5, 1, 792, y3 + 130);
var f3 = slider("Feedrate mm/s (30 mm/s = 1800 mm/min)", 1, 100, 30, 0, 792, y3 + 160);
panel("Information", "30 to 60 mm/s is fine for the robot. The extrusion is calculated from width x height x length for the Weber DXR25 (factor 1.128, as in the manual and Cura); change it with the Extrusion Multiplier Factor.", 792, y3 + 200, 320, 110, false);
var mp3 = L("MachineProfileComponent"); place(mp3, 1400, y3 + 40); setInt(mp3.Params.Input[0], 3);
var ps3 = L("PrintSettingsComponent"); place(ps3, 1640, y3 + 140);
wire(ps3.Params.Input[0], w3); wire(ps3.Params.Input[1], h3); wire(ps3.Params.Input[2], f3); wire(ps3.Params.Input[3], fl3); wire(ps3.Params.Input[11], mp3.Params.Output[0]);
var cl3 = L("CurvesToLayersComponent"); place(cl3, 1180, y3 + 600); var uz3 = N("Unit Z"); place(uz3, 980, y3 + 520); wire(uz3.Params.Input[0], h3);
var mv3 = N("Move"); place(mv3, 980, y3 + 610); wire(mv3.Params.Input[0], c3a); wire(mv3.Params.Input[0], c3b); wire(mv3.Params.Input[1], uz3.Params.Output[0]); hide(mv3);
wire(cl3.Params.Input[0], mv3.Params.Output[0]); wire(cl3.Params.Input[2], ps3.Params.Output[0]);
var cp3 = L("ContinuousPathComponent"); place(cp3, 1400, y3 + 600); wire(cp3.Params.Input[0], cl3.Params.Output[0]); wire(cp3.Params.Input[1], ps3.Params.Output[0]);
var se3 = L("StartEndComponent"); place(se3, 1640, y3 + 600); wire(se3.Params.Input[0], cp3.Params.Output[0]); setInt(se3.Params.Input[1], 0);
hide(cl3); hide(cp3);
m3.AddRange(new IGH_DocumentObject[] { fl3, h3, w3, f3, mp3, ps3, uz3, mv3, cl3, cp3, se3 });
group("ROBOT PATH FROM CURVES", blue, m3.ToArray());
write("DXR CODE MAKER", 2620, y3 - 30, 25);
var job3 = panel("Filename", "CUSTOM_PATH_EXAMPLE", 2620, y3 + 100, 300, 40, true);
var m3r = new List<IGH_DocumentObject> { job3 };
var rc3 = cell(2900, y3 + 210, m3r);
dxrOut(3700, y3 + 250, se3.Params.Output[0], rc3, job3, new double[] { 60, 230, 33 }, null, m3r);
group("DXR CODE MAKER  -  ROBOT SYSTEM, ROBOT PROGRAM, DXR GENERATOR  (read the reports, then press Save DXR to desktop)", red, m3r.ToArray());

// lock the beta component, as in version 1
vw.Locked = true;
// long wires are drawn faint (visible when the object is selected), so the canvas stays readable
foreach (var o in doc.Objects)
{
    var ps = new List<IGH_Param>(); if (o is IGH_Component cc) ps.AddRange(cc.Params.Input); else if (o is IGH_Param fp) ps.Add(fp);
    foreach (var q in ps) { var me = q.Attributes.GetTopLevel.Pivot; if (q.Sources.Any(src => { var sp = src.Attributes.GetTopLevel.Pivot; return Math.Abs(sp.X - me.X) > 520 || Math.Abs(sp.Y - me.Y) > 420; })) q.WireDisplay = GH_ParamWireDisplay.faint; }
}
foreach (var o in doc.Objects) o.ExpireSolution(false);
var arcOut = new GH_IO.Serialization.GH_Archive(); arcOut.AppendObject(doc, "Definition"); arcOut.WriteToFile(OUT, true, false);
Console.WriteLine("written " + doc.ObjectCount + " objects, " + new System.IO.FileInfo(OUT).Length + " bytes");
} finally { sup.SetValue(null, false); doc.Dispose(); }
