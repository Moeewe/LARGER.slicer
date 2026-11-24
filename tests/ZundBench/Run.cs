using System.Globalization;
using System.Text;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;

public static class Run
{
    static Cutter C(int kind, int bit, double over = 0) => CutterPresets.Make(kind, bit, 0, 0, 0, 0, 40, 31, 0, over, out _);

    /// <summary>Breaks a good PLT in several ways: the bench has to notice every one of them.</summary>
    public static void Mutations(string log)
    {
        File.WriteAllText(log, "");
        var plate = new MillPlate();
        var z = new ReliefMill { Part = DemoParts.Topography(), Plate = plate, Rough = C(0, 13), Finish = C(3, 14, 0.6) };
        z.Solve();
        var good = z.Plt.Split('\n');
        Func<string, Func<int, string>, string> each = (_, f) => string.Join("\n", Enumerable.Range(0, good.Length).Select(f).Where(q => q != null));
        string Z(string l, double by) { var q = l.Substring(2).TrimEnd(';').Split(','); return $"MW{q[0]},{q[1]},{int.Parse(q[2]) + (int)by};"; }
        int firstLift = Array.FindIndex(good, 30, l => l.StartsWith("PU"));
        var cases = new List<(string, string)>
        {
            ("unchanged", z.Plt),
            ("everything 0.5 mm deeper", each("", i => good[i].StartsWith("MW") ? Z(good[i], 50) : good[i])),
            ("everything 0.3 mm higher", each("", i => good[i].StartsWith("MW") ? Z(good[i], -30) : good[i])),
            ("sign of Z turned round", each("", i => good[i].StartsWith("MW") ? "MW" + good[i].Substring(2).Replace(",-", ",") : good[i])),
            ("Z counted from the top (model inverted)", each("", i => { if (!good[i].StartsWith("MW")) return good[i]; var q = good[i].Substring(2).TrimEnd(';').Split(','); return $"MW{q[0]},{q[1]},{-(2000 + int.Parse(q[2]))};"; })),
            ("lifts forgotten", each("", i => i > 30 && i < good.Length - 6 && good[i].StartsWith("PU") ? null : good[i])),
            ("change of the cutter forgotten", each("", i => good[i].StartsWith("XX351,14") ? null : good[i])),
            ("spindle forgotten", each("", i => good[i].StartsWith("XX150") ? null : good[i])),
            ("ZP before SP", "ZP2100,0;\n" + z.Plt),
            ("roughing skipped", string.Join("\n", good.Take(5).Concat(good.SkipWhile(l => l != "XX351,14;")))),
            ("only the last roughing level", string.Join("\n", good.Take(15).Concat(good.Skip(Array.FindIndex(good, l => l == "XX351,14;") / 2).SkipWhile(l => !l.StartsWith("PU"))))),
            ("2 mm beside", each("", i => { if (!good[i].StartsWith("MW") && !good[i].StartsWith("PU")) return good[i]; var q = good[i].Substring(2).TrimEnd(';').Split(','); return good[i].Substring(0, 2) + (int.Parse(q[0]) + 200) + "," + string.Join(",", q.Skip(1)) + ";"; })),
        };
        foreach (var (name, plt) in cases)
        {
            var r = ZundBench.Mill(plt, z.Placed, plate, b => b == 13 ? z.Rough : z.Finish, 6, true);
            var bad = r.Bad.Distinct().Take(2).ToList();
            if (r.GougeP99 > 0.12) bad.Add("too deep " + r.GougeP99.ToString("0.00", CultureInfo.InvariantCulture));
            if (r.Left99 > 0.25) bad.Add("left " + r.Left99.ToString("0.00", CultureInfo.InvariantCulture));
            if (r.WorstSlot > 0) bad.Add("slot " + r.WorstSlot.ToString("0.0", CultureInfo.InvariantCulture));
            if (r.WorstPlunge > 0) bad.Add("plunge " + r.WorstPlunge.ToString("0.0", CultureInfo.InvariantCulture));
            File.AppendAllText(log, $"{name}: {(bad.Count == 0 ? "good" : "NOTICED " + string.Join(" | ", bad))}\n");
        }
        File.AppendAllText(log, "DONE\n");
    }

    /// <summary>The ways to hold the part: tabs, skin, stays in the plate, sloped border, loose; with a thin and a normal frame.</summary>
    public static void Holds(string log)
    {
        File.WriteAllText(log, "");
        foreach (string name in new[] { "topography", "relief Carved flow (long gouges)", "relief Facade (splayed windows)", "ring with a hole", "two islands", "L shape" })
            for (int hold = 0; hold < ReliefMill.Holds.Length; hold++)
            {
                var mesh = SliceEngine.ToMesh(Parts.All().First(q => q.Item1 == name).Item2, 0.01, new List<string>());
                var bb = mesh.GetBoundingBox(true); double h = bb.Max.Z - bb.Min.Z;
                var plate = new MillPlate { Length = Math.Ceiling(bb.Max.X - bb.Min.X) + 2 * (h + 12), Width = Math.Ceiling(bb.Max.Y - bb.Min.Y) + 2 * (h + 12), Thickness = Math.Max(12, Math.Ceiling(h + 3)) };
                var z = new ReliefMill { Part = mesh, Plate = plate, Rough = C(0, 13), Finish = C(3, 14, 0.8), Hold = hold };
                z.Solve();
                if (z.Plt.Length == 0) { File.AppendAllText(log, "STOP " + name + " " + z.Report[0] + "\n"); continue; }
                var r = ZundBench.Mill(z.Plt, z.Placed, plate, b => b == 13 ? z.Rough : z.Finish, plate.Share * 6, true);
                var bad = r.Bad.Distinct().Take(3).ToList();
                if (r.GougeP99 > 0.12) bad.Add("too deep " + r.GougeP99.ToString("0.00", CultureInfo.InvariantCulture));
                if (r.WorstSlot > 0) bad.Add("slot " + r.WorstSlot.ToString("0.0", CultureInfo.InvariantCulture) + " " + r.WorstAt);
                if (r.WorstPlunge > 0) bad.Add("plunge " + r.WorstPlunge.ToString("0.0", CultureInfo.InvariantCulture));
                double low = z.Operations.SelectMany(o => o.Cuts).SelectMany(c => c.Points).Min(p => p.Z);
                File.AppendAllText(log, string.Format(CultureInfo.InvariantCulture, "{0} {1} / {2}: lowest {3:0.00}, left 99% {4:0.00}, {5:0} min {6} {7}\n", bad.Count == 0 ? "ok " : "BAD", name, ReliefMill.Holds[hold], low, r.Left99, r.Seconds / 60, string.Join(" | ", bad), string.Concat(z.Messages.Where(m => m.Key > 0).Select(m => " ! " + m.Value))));
            }
        File.AppendAllText(log, "DONE\n");
    }

    public static void All(string log, string filter)
    {
        File.WriteAllText(log, "");
        int ok = 0, all = 0, stop = 0;
        foreach (var (name, geo) in Parts.All())
        {
            if (!string.IsNullOrEmpty(filter) && !filter.Split('|').Any(f => name.Contains(f))) continue;
            foreach (var (mat, fine, over, rp, fp) in new[] { (0, 3, 0.6, 0, 0), (2, 4, 0.5, 0, 1), (2, 3, 0.8, 1, 2) })
            {
                all++;
                string head = $"{name} / {new MillPlate { Material = mat }.MaterialName}, {(rp == 0 ? "rings" : "rows")} + {(fp == 0 ? "rings" : fp == 1 ? "rows" : "cross")}, ball {C(fine, 14).Diameter}";
                try
                {
                    var mesh = SliceEngine.ToMesh(geo, 0.01, new List<string>());
                    var bb = mesh.GetBoundingBox(true); double h = bb.Max.Z - bb.Min.Z;
                    var plate = new MillPlate { Length = Math.Ceiling(bb.Max.X - bb.Min.X) + 30, Width = Math.Ceiling(bb.Max.Y - bb.Min.Y) + 30, Thickness = Math.Max(12, Math.Ceiling(h + 2)), Material = mat };
                    var z = new ReliefMill { Part = mesh, Plate = plate, Rough = C(0, 13), Finish = C(fine, 14, over), RoughPattern = rp, FinishPattern = fp, RoughAngle = 90 };
                    var sw = System.Diagnostics.Stopwatch.StartNew(); z.Solve(); long ms = sw.ElapsedMilliseconds;
                    if (z.Plt.Length == 0) { stop++; File.AppendAllText(log, "STOP " + head + ": " + z.Report[0] + "\n"); continue; }
                    double down = plate.Share * 6;
                    var r = ZundBench.Mill(z.Plt, z.Placed, plate, b => b == 13 ? z.Rough : z.Finish, down, true);
                    var bad = r.Bad.Distinct().Take(4).ToList();
                    if (r.GougeP99 > 0.12) bad.Add(string.Format(CultureInfo.InvariantCulture, "too deep {0:0.00} (worst {1:0.0})", r.GougeP99, r.Gouge));
                    if (r.WorstSlot > 0) bad.Add(string.Format(CultureInfo.InvariantCulture, "cut {0:0.0} mm deep, step is {1:0.0}, {2}", r.WorstSlot, down, r.WorstAt));
                    if (r.WorstPlunge > 0) bad.Add(string.Format(CultureInfo.InvariantCulture, "sinks in {0:0.0} mm deep", r.WorstPlunge));
                    if (bad.Count == 0) ok++;
                    File.AppendAllText(log, string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2} ms, {3} lines, {4} lifts, travel {5:0.0} m, {6:0} min (report {7:0}), left mean {8:0.00} 99% {9:0.00} max {10:0.0}, deep 99.9% {11:0.00} max {12:0.00}, ops {13}{14}{15}\n",
                        bad.Count == 0 ? "ok  " : "BAD ", head, ms, r.Lines, r.Lifts, r.TravelM, r.Seconds / 60, z.Minutes, r.LeftMean, r.Left99, r.LeftMax, r.GougeP99, r.Gouge, string.Join("/", z.Operations.Select(o => o.Name + o.Cuts.Count)),
                        bad.Count > 0 ? "  <<< " + string.Join(" | ", bad) : "", string.Concat(z.Messages.Where(m => m.Key > 0).Select(m => "  ! " + m.Value))));
                }
                catch (Exception e) { File.AppendAllText(log, "EXC " + head + " " + e + "\n"); }
            }
        }
        File.AppendAllText(log, $"{ok} of {all} good, {stop} stopped\nDONE\n");
    }
}
