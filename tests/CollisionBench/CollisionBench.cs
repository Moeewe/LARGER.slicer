using System; using System.Collections.Generic; using System.Linq; using System.Text; using System.Reflection;
using Rhino.Geometry; using LARGERslicer.Toolpaths;
using Grasshopper.Kernel; using Grasshopper.Kernel.Types;

namespace ColBench
{
    /// <summary>
    /// Regression and speed test of the collision check of Robot Program (see docs/TESTING.md,
    /// "Collision check"). Loaded into a Rhino 9 test slot and called by reflection: Compare runs
    /// six cases (and with "qs" the Quick Start robot examples) the old way (Rhino closest points,
    /// one pose after the other) and the new way on the same prepared bodies, and checks that both
    /// find the same and that the expected collisions are found.
    /// </summary>
    public static class B
    {
        static B() { System.IO.Directory.CreateDirectory(S); }
        // fingerprints of the results are written here (compare two runs with diff)
        static readonly string S = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LARGERslicer", "collision-bench") + "/";
        static System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;

        static RobotCell Cell(bool env)
        {
            var arm = RobotArm.Kr120R2700();
            string libDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotSystems");
            RobotGeometry.Load(arm, null, System.IO.Path.Combine(libDir, "FH Muenster MSA.xml"), out string why);
            arm.Base = Frame.FromPlane(RobotGeometry.FhMuensterRobotBase());
            var tool = new RobotTool { Name = "AE20_Extruder", Weight = 46 };
            tool.Tcp = Frame.FromPlane(new Plane(new Point3d(-460, 3, 108), Vector3d.YAxis, Vector3d.ZAxis));
            tool.Mesh = RobotGeometry.BuiltInTool("AE20_Extruder");
            var c = new RobotCell { Arm = arm, Tool = tool };
            if (env) { var parts = RobotGeometry.FhMuensterCell(); if (parts != null) c.Environment = parts.Values.ToList(); }
            return c;
        }
        static void Ring(Toolpath tp, Point3d c, double r, double z, int layer, Vector3d n, PathRole role = PathRole.Wall)
        {
            int k = Math.Max(12, (int)(2 * Math.PI * r / 15));
            for (int i = 0; i <= k; i++) { double a = 2 * Math.PI * i / k; tp.Add(new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z), n, 2, 5, 1, role, layer); }
        }
        static Toolpath Tower(Point3d c, double r, int layers, Vector3d n)
        {
            var tp = new Toolpath(); tp.Settings = new PrintSettings();
            for (int l = 0; l < layers; l++) Ring(tp, c, r, 2 + 2 * l, l, n);
            return tp;
        }

        /// <summary>A comparable fingerprint of a result: hit moves and every event (pair, kind, count, first/last, depth/gap).</summary>
        static string Sig(CollisionResult col)
        {
            var sb = new StringBuilder();
            sb.Append($"checked={col.Checked} poses={col.Poses} crit={col.Critical} hits={col.Hits} moves=[{string.Join(",", col.Moves)}]\n");
            foreach (var e in col.Events.OrderBy(e => e.A).ThenBy(e => e.B).ThenBy(e => e.Hit))
                sb.Append(string.Format(ci, "  {0} {1} / {2}: n={3} {4}..{5} at {6} depth {7:0.00} gap {8:0.00}\n", e.Hit ? "HIT " : "near", e.A, e.B, e.Count, e.FirstMove, e.LastMove, e.Move, e.Depth, e.Distance));
            return sb.ToString();
        }

        static CollisionResult Run(string name, Toolpath tp, RobotCell cell, StringBuilder sb, StringBuilder sig, double clearance = 10, int poses = 500)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = RobotProgram.Build(tp, cell, 0, "T", true);
            double tb = sw.Elapsed.TotalSeconds; sw.Restart();
            var col = r.CheckCollisions(tp, cell, new CollisionOptions { Poses = poses, Clearance = clearance });
            double tc = sw.Elapsed.TotalSeconds;
            var hitPairs = col.Events.Where(e => e.Hit).Select(e => e.A + "/" + e.B).Distinct().OrderBy(x => x);
            sb.AppendLine(string.Format(ci, "== {0}: points {1}, check {2:0.00}s, poses {3}, hit moves {4}; hit pairs: {5}", name, tp.Count, tc, col.Poses, col.Hits, string.Join(", ", hitPairs)));
            foreach (var l in col.Lines.Where(l => l.StartsWith("Collision check") || l.StartsWith("PROFILE") || l.StartsWith("No coll") || l.StartsWith("COLLISIONS") || l.StartsWith("Collisions were"))) sb.AppendLine("   " + l);
            sig.AppendLine("## " + name); sig.Append(Sig(col));
            return col;
        }

        public static string Regression(string arg)
        {
            var sb = new StringBuilder(); var sig = new StringBuilder();
            try
            {
                var cell = Cell(true);
                var c = new Point3d(650, 500, 0);
                Run("1 free tower", Tower(c, 150, 100, Vector3d.ZAxis), cell, sb, sig);
                var cell2 = Cell(true);
                cell2.Environment.Add(Mesh.CreateFromBox(new BoundingBox(400, 300, 350, 900, 700, 450), 1, 1, 1));
                Run("2 box over the part", Tower(c, 150, 100, Vector3d.ZAxis), cell2, sb, sig);
                var tp3 = Tower(c, 100, 150, Vector3d.ZAxis);
                var tilt2 = new Vector3d(1, 0, 1); tilt2.Unitize();
                var s0 = new Point3d(c.X - 180, c.Y - 60, 4);
                tp3.Add(new Point3d(s0.X, s0.Y, 320), Vector3d.ZAxis, 2, 5, 0, PathRole.Travel, 150);
                for (int i = 0; i <= 12; i++) tp3.Add(new Point3d(s0.X, s0.Y + 10 * i, 4), tilt2, 2, 5, 1, PathRole.Wall, 150);
                Run("3 tool leaning 45 over the part", tp3, cell, sb, sig);
                var t4 = new Vector3d(Math.Sin(80 * Math.PI / 180), 0, Math.Cos(80 * Math.PI / 180));
                Run("4 80 deg at the bed", Tower(c, 150, 3, t4), cell, sb, sig);
                var cell5 = Cell(true);
                cell5.ExternalAxes.Add(ExternalAxis.Rail(Vector3d.XAxis, -1500, 1500, 1000, "E1"));
                cell5.ExternalAxes.Add(ExternalAxis.Turntable(c, -360, 360, true, 90, "E2"));
                Run("5 rail and turntable", Tower(c, 150, 60, Vector3d.ZAxis), cell5, sb, sig);
                var cell6 = Cell(false); cell6.Arm.Links = null;
                Run("6 no robot model", Tower(c, 150, 5, Vector3d.ZAxis), cell6, sb, sig);
                if (arg == "every")
                {
                    // every move of a smaller case, to compare the full check too
                    Run("7 box over the part, every move", Tower(c, 150, 20, Vector3d.ZAxis), cell2, sb, sig, 10, -1);
                }
            }
            catch (Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
            if (!string.IsNullOrEmpty(arg) && arg != "every") System.IO.File.WriteAllText(S + "sig_" + arg + ".txt", sig.ToString());
            else System.IO.File.WriteAllText(S + "sig_last.txt", sig.ToString());
            return sb.ToString();
        }

        static void SetReference(bool on) => typeof(CollisionModel).GetField("Reference", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, on);

        /// <summary>
        /// The regression test: the six cases (and Quick Start with arg "qs") checked the old way
        /// (Rhino closest points, one pose after the other) and the new way, on the same prepared
        /// bodies; the fingerprints of both must be identical, and the expected hits must be there.
        /// </summary>
        public static string Compare(string arg)
        {
            var sb = new StringBuilder();
            // warm the caches, so both see the same bodies (Rhino's mesh reduction is not deterministic)
            SetReference(false); Regression("warm");
            SetReference(true); var tRef = System.Diagnostics.Stopwatch.StartNew(); var outRef = Regression("ref"); double sRef = tRef.Elapsed.TotalSeconds;
            SetReference(false); var tNew = System.Diagnostics.Stopwatch.StartNew(); var outNew = Regression("new"); double sNew = tNew.Elapsed.TotalSeconds;
            string a = System.IO.File.ReadAllText(S + "sig_ref.txt"), b = System.IO.File.ReadAllText(S + "sig_new.txt");
            sb.AppendLine("REFERENCE (" + sRef.ToString("0.0", ci) + " s)\n" + string.Join("\n", outRef.Split('\n').Where(l => l.StartsWith("=="))));
            sb.AppendLine("NEW (" + sNew.ToString("0.0", ci) + " s)\n" + string.Join("\n", outNew.Split('\n').Where(l => l.StartsWith("=="))));
            sb.AppendLine(a == b ? "FINGERPRINTS IDENTICAL" : "FINGERPRINTS DIFFER:\n" + Diff(a, b));
            // what must be found
            var expect = new (string, Func<string, bool>)[]
            {
                ("1 free tower: 0 hits", s => Block(s, "1 free tower").Contains("hits=0 ")),
                ("2 box: tool and links 4-6 hit the box", s => new[] { "HIT  tool / the environment mesh 7", "HIT  link 4 / the environment mesh 7", "HIT  link 5 / the environment mesh 7", "HIT  link 6 / the environment mesh 7" }.All(x => Block(s, "2 box").Contains(x))),
                ("3 leaning 45: tool hits the printed part", s => Block(s, "3 tool leaning").Contains("HIT  tool / printed part")),
                ("4 80 deg: tool hits the bed", s => Block(s, "4 80 deg").Contains("HIT  tool / bed")),
                ("5 rail + turntable: no hit", s => Block(s, "5 rail").Contains("hits=0 ") && !Block(s, "5 rail").Contains("HIT")),
                ("6 no robot model: not checked", s => Block(s, "6 no robot").Contains("checked=False")),
            };
            foreach (var (name, ok) in expect) sb.AppendLine((ok(b) ? "PASS " : "FAIL ") + name);
            // the gantry check (Printer Simulation) shares the body tests: a leaning vase on the Ginger One
            {
                var tp = new Toolpath(); tp.Settings = new PrintSettings();
                for (int l = 0; l < 120; l++) Ring(tp, new Point3d(500 + 1.5 * l, 500, 0), 120, 2 + 2 * l, l, Vector3d.ZAxis);
                for (int i = 0; i < tp.Count; i++) { if (tp.Flows.Count > i) tp.Flows[i] = 1; }
                string G(bool reference)
                {
                    SetReference(reference);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var g = GantryCollision.Make(tp, MachineDefinition.GingerOne());
                    var col = g.Run(500);
                    return $"{sw.Elapsed.TotalSeconds:0.00}s|" + Sig(col);
                }
                string g1 = G(true), g2 = G(false);
                SetReference(false);
                sb.AppendLine("GANTRY reference " + g1.Split('|')[0] + ", new " + g2.Split('|')[0] + ": " + (g1.Split('|')[1] == g2.Split('|')[1] ? "IDENTICAL" : "DIFFER\n" + Diff(g1.Split('|')[1], g2.Split('|')[1])));
                sb.AppendLine("  " + g2.Split('|')[1].Split('\n')[0]);
            }
            if (arg == "qs")
            {
                SetReference(true); var r1 = QuickStart("qsref");
                SetReference(false); var r2 = QuickStart("qsnew");
                string qa = System.IO.File.ReadAllText(S + "qssig_qsref.txt"), qb = System.IO.File.ReadAllText(S + "qssig_qsnew.txt");
                sb.AppendLine("QS REFERENCE\n" + string.Join("\n", r1.Split('\n').Where(l => l.StartsWith("==") || l.StartsWith("Quick"))));
                sb.AppendLine("QS NEW\n" + string.Join("\n", r2.Split('\n').Where(l => l.StartsWith("==") || l.StartsWith("Quick"))));
                sb.AppendLine(qa == qb ? "QS FINGERPRINTS IDENTICAL" : "QS FINGERPRINTS DIFFER:\n" + Diff(qa, qb));
            }
            SetReference(false);
            return sb.ToString();
        }

        static string Block(string sig, string name)
        {
            int a = sig.IndexOf("## " + name); if (a < 0) return "";
            int b = sig.IndexOf("## ", a + 3); return b < 0 ? sig.Substring(a) : sig.Substring(a, b - a);
        }

        static string Diff(string a, string b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n'); var sb = new StringBuilder();
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i] : "", y = i < lb.Length ? lb[i] : "";
                if (x != y) sb.AppendLine("  - " + x + "\n  + " + y);
            }
            return sb.ToString();
        }

        /// <summary>The Quick Start robot examples: build Quick Start in memory, solve it without the collision checks, then time the check of every Robot Program.</summary>
        public static string QuickStart(string arg)
        {
            var sb = new StringBuilder(); var sig = new StringBuilder();
            var gha = typeof(Toolpath).Assembly;
            var qsT = gha.GetType("LARGERslicer.Components.Slicer.QuickStartComponent");
            var doc = new GH_Document(); doc.Enabled = false;
            try
            {
                var qs = (GH_Component)Activator.CreateInstance(qsT);
                qsT.GetField("_done", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(qs, true);
                qs.CreateAttributes(); qs.Attributes.Pivot = new System.Drawing.PointF(0, 0);
                doc.AddObject(qs, false);
                qsT.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(qs, new object[] { doc });
                var programs = doc.Objects.OfType<GH_Component>().Where(o => o.Name == "Robot Program").ToList();
                if (arg != "full")
                foreach (var p in programs)
                {
                    var cc = p.Params.Input[10] as Grasshopper.Kernel.Parameters.Param_Boolean;
                    cc.PersistentData.Clear(); cc.PersistentData.Append(new GH_Boolean(false));
                }
                // the robot simulations would only replay; switch them off
                foreach (var o in doc.Objects.OfType<GH_Component>().Where(o => o.Name.Contains("Simulation"))) o.Locked = true;
                // calculate everything right away (Background off)
                var asyncT = gha.GetType("LARGERslicer.Components.AsyncSolve");
                int sync = 0;
                foreach (var o in doc.Objects)
                    for (var t = o.GetType(); t != null; t = t.BaseType)
                        foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                            if (f.FieldType == asyncT && f.GetValue(o) is object a) { asyncT.GetProperty("Enabled").SetValue(a, false); sync++; }
                sb.AppendLine("background off on " + sync);
                doc.Enabled = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                doc.NewSolution(false, GH_SolutionMode.Silent);
                sb.AppendLine(string.Format(ci, "Quick Start solved in {0:0.0}s ({1}), {2} Robot Program(s)", sw.Elapsed.TotalSeconds, arg == "full" ? "with the collision checks" : "collision checks off", programs.Count));
                if (arg == "full")
                {
                    foreach (var p in programs)
                        sb.AppendLine(string.Format(ci, "Robot Program '{0}': {1:0.00} s in the solution, message '{2}': {3}", p.Params.Input[3].VolatileData.AllData(true).FirstOrDefault(), p.ProcessorTime.TotalSeconds, p.Message,
                            string.Join(" | ", p.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Concat(p.RuntimeMessages(GH_RuntimeMessageLevel.Warning)).Concat(p.RuntimeMessages(GH_RuntimeMessageLevel.Error)))));
                    return sb.ToString();
                }
                int k = 0;
                foreach (var p in programs)
                {
                    k++;
                    object Src(int i) { var s = p.Params.Input[i].Sources.FirstOrDefault(); var g = s?.VolatileData.AllData(true).FirstOrDefault(); return g is IGH_Goo goo ? goo.ScriptVariable() : null; }
                    var tp = Src(0) as Toolpath; var cell = Src(1) as RobotCell;
                    if (tp == null || cell == null) { sb.AppendLine($"program {k}: no toolpath/cell ({Src(0)?.GetType().Name}, {Src(1)?.GetType().Name})"); continue; }
                    var col = Run("QS program " + k + " (" + p.Params.Input[3].VolatileData.AllData(true).FirstOrDefault() + ")", tp, cell, sb, sig);
                    if (arg == "twice") Run("QS program " + k + " again", tp, cell, sb, sig);
                }
            }
            catch (Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
            finally { doc.Enabled = false; doc.Dispose(); }
            System.IO.File.WriteAllText(S + "qssig_" + (string.IsNullOrEmpty(arg) ? "last" : arg) + ".txt", sig.ToString());
            return sb.ToString();
        }
    }
}
