using System.Globalization;
using System.Text;
using Rhino.Geometry;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;

/// <summary>
/// Mills the PLT of CNC - Rough and Finish once, as the Zünd would: reads the commands (tool, bit, speeds, PU, MW),
/// moves with acceleration and stops at sharp corners, cuts a height field of the plate with the shape of the cutter,
/// and compares the block with the model. Nothing of the plug-in is used for the check but the PLT text and the model.
/// </summary>
public static class ZundBench
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed class Result
    {
        public List<string> Bad = new List<string>();
        public double Seconds, Gouge, GougeP99, Left99, LeftMax, LeftMean, WorstSlot, WorstPlunge, TravelM, DeepRun;
        public string WorstAt = "";
        public int Lifts, Changes, Lines;
        public float[,] Stock; public double X0, Y0, G;
    }

    public static double G(double size) => Math.Max(0.25, size / 900.0);

    public static Result Mill(string plt, Mesh model, MillPlate plate, Func<int, Cutter> bit, double stepDown, bool cutOut)
    {
        var r = new Result();
        var lines = plt.Split('\n');
        r.Lines = lines.Length;
        double T = plate.Thickness;
        var bb = model.GetBoundingBox(true);
        double g = G(Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y)), pad = 12;
        int nx = (int)((bb.Max.X - bb.Min.X + 2 * pad) / g), ny = (int)((bb.Max.Y - bb.Min.Y + 2 * pad) / g);
        double X0 = bb.Min.X - pad, Y0 = bb.Min.Y - pad;
        var s = new float[nx, ny]; var M = new float[nx, ny];
        for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) s[i, j] = (float)T;
        Parallel.For(0, nx, i =>
        {
            for (int j = 0; j < ny; j++)
            {
                double t = Rhino.Geometry.Intersect.Intersection.MeshRay(model, new Ray3d(new Point3d(X0 + i * g, Y0 + j * g, 500), -Vector3d.ZAxis));
                M[i, j] = t >= 0 ? (float)(500 - t) : float.NaN;
            }
        });

        Cutter tool = null; int slot = 0; double up = -1, vw = 0, vu = 0, rpm = 0; int acc = 1;
        Point3d pos = new Point3d(0, 0, T + 50); bool down = false; bool seenMA = false, vac = false;
        Vector3d dir = Vector3d.Zero; double speed = 0;
        bool last = false; int changes = 0; string phase = "";
        var slotArea = new List<double>();
        double lastLevelZ = double.MaxValue;
        for (int n = 0; n < lines.Length; n++)
        {
            string l = lines[n].Trim();
            if (l.Length == 0) continue;
            if (!l.EndsWith(";")) { r.Bad.Add("line " + n + " has no ; " + l); continue; }
            if (l.Any(ch => ch > 126)) r.Bad.Add("line " + n + " is not ASCII");
            foreach (string cmd in l.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string op = cmd.Substring(0, 2);
                if (op == "XX")
                {
                    var q = cmd.Substring(2).Split(',');
                    if (q[0] == "351") { if (slot == 0) r.Bad.Add("XX351 before SP"); if (down) r.Bad.Add("XX351 with the tool down"); tool = bit(int.Parse(q[1])); changes++; r.Seconds += 45; rpm = 0; }
                    else if (q[0] == "150") { rpm = double.Parse(q[1], Inv); if (rpm < 7000 || rpm > 50000) r.Bad.Add("spindle " + rpm); }
                    else if (q[0] == "306") { double h = double.Parse(q[1], Inv); if (h < T * 100) r.Bad.Add("suction bell inside the plate: " + h); if (h > 7000) r.Bad.Add("suction bell too high: " + h); }
                    continue;
                }
                var a = cmd.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(q => double.Parse(q, Inv)).ToArray();
                switch (op)
                {
                    case "SP": slot = (int)a[0]; break;
                    case "ZP": if (slot == 0) r.Bad.Add("ZP before SP"); up = a[0] / 100; if (up < T + 0.5) r.Bad.Add("lift only to " + up); break;
                    case "VW": vw = a[0] * 10; if (a[0] < 0.1 || a[0] > 100) r.Bad.Add("VW " + a[0]); break;
                    case "VU": vu = a[0] * 10; if (a[0] < 0.1 || a[0] > 100) r.Bad.Add("VU " + a[0]); break;
                    case "VS": break;
                    case "AS": acc = (int)a[0]; break;
                    case "MA": seenMA = true; break;
                    case "PB": vac = a.Length > 1 && a[1] > 0; break;
                    case "ZT": case "SV": break;
                    case "PU":
                        {
                            down = false; r.Lifts++;
                            var p = new Point3d(a[0] / 100, a[1] / 100, up);
                            if (up < 0 && n < lines.Length - 5) r.Bad.Add("PU before ZP");
                            double d = new Point3d(pos.X, pos.Y, 0).DistanceTo(new Point3d(p.X, p.Y, 0));
                            r.Seconds += Math.Abs(up - pos.Z) / 100 + Move(d, vu, 10000);
                            if (n < lines.Length - 5) { r.TravelM += d / 1000; if (p.X < -0.01 || p.Y < -0.01 || p.X > 2500 || p.Y > 1800) r.Bad.Add("travel off the table"); }
                            pos = p; dir = Vector3d.Zero;
                            break;
                        }
                    case "MW":
                        {
                            if (!seenMA) r.Bad.Add("MW before MA");
                            if (tool == null) { r.Bad.Add("MW without a tool"); return r; }
                            if (rpm <= 0) r.Bad.Add("MW with the spindle off");
                            if (!vac) r.Bad.Add("MW without vacuum");
                            var p = new Point3d(a[0] / 100, a[1] / 100, -a[2] / 100); // negative = above the table
                            if (a[2] > 0) r.Bad.Add("into the table: " + cmd);
                            if (p.Z > up + 0.01) r.Bad.Add("cut above the lift: " + cmd);
                            if (p.X < plate.X - 0.01 - 0 && p.X < 0) r.Bad.Add("off the table: " + cmd);
                            bool entry = !down;
                            if (entry && (Math.Abs(p.X - pos.X) > 0.011 || Math.Abs(p.Y - pos.Y) > 0.011)) r.Bad.Add("sinks in on the way: " + cmd);
                            down = true;
                            Vector3d v = p - pos; double len = v.Length;
                            if (len > 1e-9)
                            {
                                v /= len;
                                // stops where the direction changes by more than 30 degrees, else goes on
                                double A = acc == 1 ? 1250 : acc == 2 ? 2500 : acc == 3 ? 5000 : 10000;
                                if (dir.Length < 0.5 || dir * v < 0.866) speed = 0;
                                double vmax = vw, va = Math.Min(vmax, Math.Sqrt(speed * speed + 2 * A * len));
                                r.Seconds += len / Math.Max(0.5, 0.5 * (speed + va)); speed = va; dir = v;
                                Cut(s, M, tool, pos, p, X0, Y0, g, T, r, stepDown, entry);
                            }
                            pos = p;
                            break;
                        }
                    default: r.Bad.Add("unknown " + cmd); break;
                }
            }
        }
        r.Changes = changes;
        if (down) r.Bad.Add("ends with the tool down");

        // the block against the model
        double R = 0.75;
        int w = (int)Math.Ceiling(R / g);
        var left = new List<double>(); var deep = new List<double>();
        for (int i = w; i < nx - w; i++)
            for (int j = w; j < ny - w; j++)
            {
                if (float.IsNaN(M[i, j])) continue;
                double lo = double.MaxValue, hi = double.MinValue; bool edge = false;
                for (int u = -1; u <= 1; u++) for (int v = -1; v <= 1; v++) { float m = M[i + u, j + v]; if (float.IsNaN(m)) { edge = true; continue; } lo = Math.Min(lo, m); hi = Math.Max(hi, m); }
                if (edge) continue;
                double top = Math.Min(hi, T), bottom = Math.Min(lo, T);
                left.Add(Math.Max(0, s[i, j] - top)); deep.Add(Math.Max(0, bottom - s[i, j]));
            }
        if (left.Count == 0) { r.Bad.Add("nothing to compare"); return r; }
        left.Sort(); deep.Sort();
        r.Left99 = left[(int)(0.99 * (left.Count - 1))]; r.LeftMax = left[left.Count - 1]; r.LeftMean = left.Average();
        r.Gouge = deep[deep.Count - 1]; r.GougeP99 = deep[(int)(0.999 * (deep.Count - 1))];
        r.Stock = s; r.X0 = X0; r.Y0 = Y0; r.G = g;
        return r;
    }

    static double Move(double d, double v, double a) { if (v <= 0) v = 100; double t = v / a; return d < v * t ? 2 * Math.Sqrt(d / a) : d / v + t; }

    static void Cut(float[,] s, float[,] M, Cutter c, Point3d a, Point3d b, double X0, double Y0, double g, double T, Result r, double stepDown, bool entry)
    {
        double rad = c.Diameter / 2; int k = (int)Math.Ceiling(rad / g) + 1, nx = s.GetLength(0), ny = s.GetLength(1);
        double flat = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int n = Math.Max(1, (int)Math.Ceiling(Math.Max(flat, 0.3 * Math.Abs(b.Z - a.Z)) / g));
        for (int t = 1; t <= n; t++)
        {
            Point3d p = a + (b - a) * (t / (double)n);
            int ci = (int)Math.Round((p.X - X0) / g), cj = (int)Math.Round((p.Y - Y0) / g);
            int deepCells = 0, cells = 0; double worst = 0;
            for (int u = -k; u <= k; u++)
                for (int v = -k; v <= k; v++)
                {
                    int i = ci + u, j = cj + v;
                    double ex = X0 + i * g - p.X, ey = Y0 + j * g - p.Y, d2 = ex * ex + ey * ey;   // from the real middle of the cutter, not from its cell
                    if (d2 > rad * rad) continue;
                    if (i < 0 || j < 0 || i >= nx || j >= ny) continue;
                    cells++;
                    float h = (float)(p.Z + (c.Shape == Cutter.Ball ? rad - Math.Sqrt(rad * rad - d2) : 0));
                    if (s[i, j] > h) { double e = s[i, j] - h; if (e > 1.15 * stepDown + 0.4) deepCells++; if (e > worst) worst = e; s[i, j] = h; }
                }
            // deeper than one step down over a quarter of the cutter's width, and for longer than 2.5 mm of the way:
            // the thin skin of the stock on a wall, met head-on or at a flat angle, is through before
            bool sinks = entry || flat < 0.02;
            if (sinks) { if (cells > 0 && deepCells > 0.12 * cells) r.WorstPlunge = Math.Max(r.WorstPlunge, worst); }
            else if (deepCells > 0.25 * c.Diameter / g * Math.Max(1.0, flat / n / g))
            {
                r.DeepRun += flat / n;
                if (r.DeepRun > 2.5 && worst > r.WorstSlot) { r.WorstSlot = worst; r.WorstAt = c.Name + " at " + p.ToString(); }
            }
            else r.DeepRun = 0;
        }
    }
}
