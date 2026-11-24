using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clipper2Lib;
using Rhino.Geometry;
using LARGERslicer.Types;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Rough and finish a relief (terrain, height model, mould) on a machine with three axes and an upright
    /// cutter: a thick cutter takes the bulk in levels, a fine (ball) cutter follows the surface in close rows.
    ///
    /// The part is read as a height field. For every place of the tool axis the lowest height is worked out at
    /// which neither the cutter nor its shank touches the part (drop cutter: the highest of part height minus
    /// tool profile over the disc of the tool), so the shape of the tool is in the path, not only its tip.
    ///
    /// Roughing: at every level rings that follow the border of what may be cut, from the middle outwards, one
    /// direction (climb), no zigzag; z = the level or the lowest allowed height + stock, whichever is higher.
    /// So nothing is cut deeper than one step down and the slopes are left as slopes, not as terraces.
    /// The way into the material is a ramp along the ring. Finishing: rings from outside to inside, or rows.
    /// At the end the part is cut out of the plate, in levels, held by tabs.
    /// </summary>
    public sealed class ReliefMill
    {
        // ---- inputs
        public Mesh Part;
        public MillPlate Plate = new MillPlate();
        public Cutter Rough, Finish;
        /// <summary>0 = rings, 1 = rows.</summary>
        public int RoughPattern;
        /// <summary>0 = rings, 1 = rows, 2 = rows and a second time across.</summary>
        public int FinishPattern;
        public double Stock = 0.5, RoughAngle, FinishAngle;
        /// <summary>How the part is held at the end: see Holds.</summary>
        public int Hold = HoldTabs;
        public int Tabs = 4;
        /// <summary>Width of a tab, and height of a tab or thickness of the skin, in mm. 0 = automatic (tab 8 x 2 mm, skin 0.6 mm).</summary>
        public double TabWidth = 0, HoldHeight = 0;
        public const int HoldTabs = 0, HoldSkin = 1, HoldStay = 2, HoldBevel = 3, HoldFree = 4;
        public static readonly string[] Holds = { "Cut out, tabs hold it", "Cut out, a thin skin holds it", "Stays in the plate", "Stays in the plate, sloped border", "Cut out, loose" };
        public const string HoldHelp = "How the part is held when it is done: 0 = cut out all round, small tabs hold it, 1 = cut out but for a thin skin at the bottom that holds it all round (cut it with a knife), 2 = not cut out: it stays in the plate, 3 = not cut out, and a slope of 45 degrees leads from the top of the plate down to the model, like a picture frame (no deep walls beside the model), 4 = cut out and loose: only with a good vacuum";
        public bool CutOut => Hold == HoldTabs || Hold == HoldSkin || Hold == HoldFree;
        public const double FrameMin = 10.0; // so much of the plate should stay round a part that hangs on tabs
        public const double Rapid = 500;
        private const int AccelDown = 2, AccelUp = 4, ZpOffset = 0;
        private const double Margin = 1.0; // the finishing axis goes this far over the edge of the model, so that the edge is clean
        private double RoughMargin => Margin + (Rough != null && Finish != null ? Math.Max(0, (Finish.Diameter - Rough.Diameter) / 2.0) : 0) + 0.7; // the roughing a little further

        // worked out
        private double MaterialThickness, StepDown, RoughStepover, FinishStepover;
        private bool CrossFinish => FinishPattern == 2;

        // ---- results
        /// <summary>0 = remark, 1 = warning, 2 = error.</summary>
        public readonly List<KeyValuePair<int, string>> Messages = new List<KeyValuePair<int, string>>();
        public readonly List<string> Report = new List<string>();
        public readonly List<Operation> Operations = new List<Operation>();
        public string Plt = "";
        public readonly List<string> PltPerTool = new List<string>();
        public Mesh Result;
        public double Minutes, MaxLeft, MeanLeft, MaxGouge, MaxBeforeFinish, GridSize, TravelLength, MinBase;
        /// <summary>The model where it lies in the plate.</summary>
        public Mesh Placed;

        public sealed class Cut
        {
            public List<Point3d> Points = new List<Point3d>();
        }

        /// <summary>One cutter at work; a lift before every cut.</summary>
        public sealed class Operation
        {
            public string Name;
            public Cutter Cutter;
            public double Feed, Rpm;
            public readonly List<Cut> Cuts = new List<Cut>();
            public double Length => Cuts.Sum(c => new Polyline(c.Points).Length);
        }

        /// <summary>The ways with the cutter lifted, between the cuts.</summary>
        public List<Line> Travel()
        {
            var l = new List<Line>();
            Point3d last = Point3d.Unset;
            foreach (var op in Operations)
                foreach (var c in op.Cuts)
                {
                    Point3d a = c.Points[0], up = new Point3d(a.X, a.Y, Up);
                    if (last.IsValid) l.Add(new Line(last, up));
                    last = c.Points[c.Points.Count - 1]; last.Z = Up;
                }
            return l;
        }

        private void Remark(string s) => Messages.Add(new KeyValuePair<int, string>(0, s));
        private void Warning(string s) => Messages.Add(new KeyValuePair<int, string>(1, s));
        private void Error(string s) => Messages.Add(new KeyValuePair<int, string>(2, s));

        // ---- height field
        private int _nx, _ny;
        private double _x0, _y0, _d;
        private float[] _h;       // height of the part, -infinity beside it
        private bool[] _region;   // where the tool axis may go
        private double _floor;

        public double Up => Plate.Thickness + 1.0;

        // ================================================================ solve

        public void Solve()
        {
            Calculate();
            int changes = 0;
            for (int i = 1; i < Operations.Count; i++) if (Operations[i].Cutter.Name != Operations[i - 1].Cutter.Name) changes++;
            var stop = Messages.Where(m => m.Key == 2).Select(m => m.Value).ToList();
            Report.Insert(0, stop.Count > 0 ? "STOP, nothing is written: " + string.Join(" ", stop)
                : Inv.F($"READY: about {Minutes:0} min, {changes} change(s) of the cutter, at most {MaxGouge:0.00} mm too deep.") + (Plt.Length == 0 ? " One file per cutter." : ""));
        }

        private void Calculate()
        {
            if (Part == null || Part.Faces.Count == 0) { Error("No model. Connect a mesh, surface, Brep or SubD to Model."); return; }
            if (Rough == null && Finish == null) { Error("Connect a cutter to Rough Cutter and one to Finish Cutter (CNC - Cutter)."); return; }
            if (Plate == null) Plate = new MillPlate();
            string problem = Plate.Problem();
            if (problem != null) { Error(problem); return; }
            MaterialThickness = Plate.Thickness;
            if (!Check()) return;
            Part = Part.DuplicateMesh();
            BoundingBox box = Part.GetBoundingBox(true);
            double high = box.Max.Z - box.Min.Z;
            Part.Transform(Plate.Place(box));
            box = Part.GetBoundingBox(true);
            Placed = Part;
            _floor = box.Min.Z;
            if (_floor < -0.005)
            {
                Error(Plate.Height == 0
                    ? Inv.F($"The model is {high:0.0} mm high, the plate only {MaterialThickness:0.0} mm thick. Take a thicker plate (at least {high + 2:0} mm) or scale the model. Nothing is written.")
                    : Inv.F($"The model goes down to Z {_floor:0.00}. Z 0 is the table: the cutter would cut into it. Move the model up, or set Height of CNC - Plate to 'Level with the top'. Nothing is written."));
                return;
            }
            if (box.Max.Z > MaterialThickness + 0.005)
                Warning(Inv.F($"The model sticks out of the plate by {box.Max.Z - MaterialThickness:0.0} mm: that part cannot be milled."));
            Cutter saw = Rough ?? Finish;
            double round = CutOut ? saw.Diameter : 0;
            if (box.Min.X - round < Plate.X - 0.005 || box.Min.Y - round < Plate.Y - 0.005 || box.Max.X + round > Plate.X + Plate.Length + 0.005 || box.Max.Y + round > Plate.Y + Plate.Width + 0.005)
            {
                string text = Inv.F($"The model ({box.Max.X - box.Min.X:0.#} x {box.Max.Y - box.Min.Y:0.#} mm){(CutOut ? Inv.F($" and the cut around it ({saw.Diameter:0.#} mm on every side)") : "")} does not lie in the plate ({Plate.Length:0.#} x {Plate.Width:0.#} mm). It needs at least {box.Max.X - box.Min.X + 2 * round:0} x {box.Max.Y - box.Min.Y + 2 * round:0} mm, and Placement of CNC - Plate has to put it there.");
                if (CutOut) { Error(text + " Nothing is written."); return; }
                Warning(text);
            }
            if (CutOut && Hold != HoldFree)
            {
                double frame = Math.Min(Math.Min(box.Min.X - round - Plate.X, box.Min.Y - round - Plate.Y), Math.Min(Plate.X + Plate.Length - box.Max.X - round, Plate.Y + Plate.Width - box.Max.Y - round));
                if (frame < FrameMin - 0.005)
                    Warning(Inv.F($"Only {Math.Max(0, frame):0.0} mm of the plate stay as a frame round the cut. The part hangs on it: {FrameMin:0} mm should stay, a plate of {box.Max.X - box.Min.X + 2 * (round + FrameMin):0} x {box.Max.Y - box.Min.Y + 2 * (round + FrameMin):0} mm does it. Or choose 'Stays in the plate'."));
            }
            MinBase = _floor;
            if (_floor < 2.0 && !CutOut) Remark(Inv.F($"Only {_floor:0.0} mm of the plate are left at the thinnest place."));
            Report.Add(Inv.F($"{Plate}. Model {box.Max.X - box.Min.X:0.#} x {box.Max.Y - box.Min.Y:0.#} x {high:0.#} mm, at X {box.Min.X:0.#}, Y {box.Min.Y:0.#}; {MaterialThickness - _floor:0.0} mm deep at most, {_floor:0.0} mm of the plate stay below it."));

            Raster(box);
            if (Hold == HoldBevel) Bevel();
            if (Rough != null) AddRoughing();
            Cancel.Check();
            float[] stockLeft = null;
            if (Rough != null && Finish != null)
            {
                stockLeft = Block();
                foreach (var op in Operations) CutAway(stockLeft, op);
                Region(Margin);
                MaxBeforeFinish = Engagement(stockLeft);
                Report.Add(Inv.F($"After roughing the finishing cutter would have up to {MaxBeforeFinish:0.0} mm before it."));
                {
                    AddRest(stockLeft);
                    var rest = Operations.FirstOrDefault(o => o.Name == "Rest");
                    if (rest != null)
                    {
                        CutAway(stockLeft, rest);
                        MaxBeforeFinish = Engagement(stockLeft);
                        Report.Add(Inv.F($"After that up to {MaxBeforeFinish:0.0} mm."));
                    }
                }
            }
            if (Finish != null) Region(Margin);
            if (Finish != null && Rough == null)
            {
                MaxBeforeFinish = Engagement(Block());
                if (MaxBeforeFinish > 1.5 * Finish.Diameter)
                    Warning(Inv.F($"Finish only: in a raw block '{Finish.Name}' would cut up to {MaxBeforeFinish:0.0} mm deep in one go. Connect a Rough Cutter, unless the block is roughed already."));
            }
            if (Finish != null)
            {
                AddFinishing(FinishPattern, FinishAngle, "Finish");
                if (CrossFinish) AddFinishing(1, FinishAngle + 90, "Finish across");
            }
            Cancel.Check();
            if (CutOut) AddOutline(box);
            if (Operations.Count == 0) { Error("Nothing to mill."); return; }

            CheckTable();
            RunSimulation();
            WriteTimes();
            WritePlt();
        }

        private bool Check()
        {
            foreach (var c in new[] { Rough, Finish })
            {
                if (c == null) continue;
                if (!(c.Diameter > 0)) { Error($"Cutter '{c.Name}' has no diameter."); return false; }
                if (c.ToolPosition != 11 && c.ToolPosition != 21 && c.ToolPosition != 31) { Error($"Cutter '{c.Name}': the slot of the router module must be 11, 21 or 31."); return false; }
                if (c.RouterBit < 0 || c.RouterBit > 18) { Error($"Cutter '{c.Name}': Magazine is 11-18 for the places 1-8, 1-10 for a change by hand, 0 for not set."); return false; }
            }
            Stock = Math.Max(0, Stock);
            if (Rough != null)
            {
                RoughStepover = Rough.Stepover > 0 ? Rough.Stepover : 0.45 * Rough.Diameter;
                if (RoughStepover > 0.5 * Rough.Diameter && RoughPattern == 0)
                {
                    Remark(Inv.F($"Rings of '{Rough.Name}' lie at most half its diameter apart, or posts would stay in the corners: {0.5 * Rough.Diameter:0.##} mm is used."));
                    RoughStepover = 0.5 * Rough.Diameter;
                }
                RoughStepover = Math.Min(RoughStepover, 0.95 * Rough.Diameter);
            }
            Cutter saw = Rough ?? Finish;
            StepDown = saw.StepDown > 0 ? saw.StepDown : Plate.Share * saw.Diameter;
            if (saw.CuttingLength > 0 && StepDown > saw.CuttingLength) { Error(Inv.F($"Step Down ({StepDown:0.##} mm) is longer than the cutting edge of '{saw.Name}' ({saw.CuttingLength:0.##} mm).")); return false; }
            if (StepDown > 1.5 * saw.Diameter) Warning(Inv.F($"Step Down ({StepDown:0.##} mm) is more than 1.5 x the diameter of '{saw.Name}'. {Plate.Share * saw.Diameter:0.#} mm is a start value for {Plate.MaterialName}."));
            if (Finish != null)
            {
                FinishStepover = Math.Max(0.05, Finish.Stepover > 0 ? Finish.Stepover : Finish.Diameter / 10.0);
                if (FinishStepover > 0.7 * Finish.Diameter) Warning(Inv.F($"Stepover ({FinishStepover:0.##} mm) is wide for '{Finish.Name}': the paths stay visible."));
                if (Finish.Shape != Cutter.Ball) Remark($"'{Finish.Name}' is a flat cutter: slopes get small steps. A ball cutter leaves them smoother.");
            }
            return true;
        }

        // ================================================================ height field

        private void Raster(BoundingBox box)
        {
            double reach = new[] { Rough, Finish }.Where(c => c != null).Max(c => Math.Max(c.Diameter, c.ShankDiameter)) / 2.0;
            double pad = reach + RoughMargin + 1.0 + (Hold == HoldBevel ? Math.Max(0, MaterialThickness - _floor) : 0);
            double w = box.Max.X - box.Min.X + 2 * pad, h = box.Max.Y - box.Min.Y + 2 * pad;
            double fine = new[] { Rough, Finish }.Where(c => c != null).Min(c => c.Diameter);
            double d = Math.Max(0.1, Math.Min(0.5, fine / 30.0));
            if (Finish != null) d = Math.Max(0.1, Math.Min(d, FinishStepover / 2.0));
            d = Math.Max(d, Math.Sqrt(w * h / 4.0e6)); // at most four million places
            d = Math.Max(d, Math.Pow(w * h * Math.PI * reach * reach / 2.5e9, 0.25)); // and a few seconds for large parts
            _d = GridSize = d;
            _nx = Math.Max(3, (int)Math.Ceiling(w / d) + 1); _ny = Math.Max(3, (int)Math.Ceiling(h / d) + 1);
            _x0 = box.Center.X - 0.5 * (_nx - 1) * d; _y0 = box.Center.Y - 0.5 * (_ny - 1) * d;
            _h = new float[_nx * _ny];
            for (int i = 0; i < _h.Length; i++) _h[i] = float.NegativeInfinity;
            for (int f = 0; f < Part.Faces.Count; f++)
            {
                MeshFace q = Part.Faces[f];
                Point3d a = Part.Vertices.Point3dAt(q.A), b = Part.Vertices.Point3dAt(q.B), c = Part.Vertices.Point3dAt(q.C);
                Triangle(a, b, c);
                if (q.IsQuad) Triangle(a, c, Part.Vertices.Point3dAt(q.D));
                if ((f & 4095) == 0) Cancel.Check();
            }
        }

        /// <summary>A slope of 45 degrees from the edge of the model up to the top of the plate: part of what is milled, like the model.</summary>
        private void Bevel()
        {
            var g = new float[_h.Length];
            for (int i = 0; i < g.Length; i++) g[i] = _h[i] > float.NegativeInfinity ? _h[i] : float.PositiveInfinity;
            float e = (float)_d, c = (float)(_d * Math.Sqrt(2.0)), k = (float)(_d * Math.Sqrt(5.0));
            // two sweeps: the lowest way up from any place of the edge (chamfer distance, 2 % off a circle at most)
            Action<int, int, int, int, float> from = (x, y, ox, oy, w) =>
            {
                int u = x + ox, v = y + oy;
                if (u < 0 || v < 0 || u >= _nx || v >= _ny) return;
                float q = g[v * _nx + u] + w; int i = y * _nx + x;
                if (_h[i] > float.NegativeInfinity) return;
                if (q < g[i]) g[i] = q;
            };
            for (int y = 0; y < _ny; y++) for (int x = 0; x < _nx; x++) { from(x, y, -1, 0, e); from(x, y, 0, -1, e); from(x, y, -1, -1, c); from(x, y, 1, -1, c); from(x, y, -2, -1, k); from(x, y, -1, -2, k); from(x, y, 1, -2, k); from(x, y, 2, -1, k); }
            for (int y = _ny - 1; y >= 0; y--) for (int x = _nx - 1; x >= 0; x--) { from(x, y, 1, 0, e); from(x, y, 0, 1, e); from(x, y, 1, 1, c); from(x, y, -1, 1, c); from(x, y, 2, 1, k); from(x, y, 1, 2, k); from(x, y, -1, 2, k); from(x, y, -2, 1, k); }
            float top = (float)MaterialThickness;
            double x1 = Plate.X + Plate.Length, y1 = Plate.Y + Plate.Width; bool over = false;
            for (int y = 0; y < _ny; y++)
                for (int x = 0; x < _nx; x++)
                {
                    int i = y * _nx + x;
                    if (_h[i] > float.NegativeInfinity || !(g[i] < top)) continue;
                    double px = _x0 + x * _d, py = _y0 + y * _d;
                    if (px < Plate.X - 1e-6 || py < Plate.Y - 1e-6 || px > x1 + 1e-6 || py > y1 + 1e-6) { over = true; continue; }
                    _h[i] = g[i];
                }
            if (over) Warning("The sloped border runs over the edge of the plate. Take a larger plate or a smaller model.");
            Report.Add(Inv.F($"Border: a slope of 45 degrees from the model up to the top of the plate, at most {MaterialThickness - _floor:0.#} mm wide."));
        }

        /// <summary>The tool axis stays over the model, or within the margin around it.</summary>
        private void Region(double margin)
        {
            var outside = new bool[_h.Length];
            for (int i = 0; i < _h.Length; i++) outside[i] = !(_h[i] > float.NegativeInfinity);
            var sq = Distance(outside); // from every place beside the model to the model
            double lim = margin / _d; lim *= lim;
            var r = new bool[_h.Length];
            for (int i = 0; i < r.Length; i++) r[i] = sq[i] <= lim + 1e-3;
            _region = r;
        }

        private void Triangle(Point3d a, Point3d b, Point3d c)
        {
            double det = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(det) < 1e-12) return; // upright
            int x0 = Math.Max(0, (int)Math.Ceiling((Math.Min(a.X, Math.Min(b.X, c.X)) - _x0) / _d - 1e-6)), x1 = Math.Min(_nx - 1, (int)Math.Floor((Math.Max(a.X, Math.Max(b.X, c.X)) - _x0) / _d + 1e-6));
            int y0 = Math.Max(0, (int)Math.Ceiling((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - _y0) / _d - 1e-6)), y1 = Math.Min(_ny - 1, (int)Math.Floor((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - _y0) / _d + 1e-6));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = _x0 + x * _d, py = _y0 + y * _d;
                    double u = ((px - a.X) * (c.Y - a.Y) - (c.X - a.X) * (py - a.Y)) / det, v = ((b.X - a.X) * (py - a.Y) - (px - a.X) * (b.Y - a.Y)) / det;
                    if (u < -1e-6 || v < -1e-6 || u + v > 1 + 1e-6) continue;
                    float z = (float)(a.Z + u * (b.Z - a.Z) + v * (c.Z - a.Z));
                    int i = y * _nx + x;
                    if (z > _h[i]) _h[i] = z;
                }
        }

        private struct Offset { public int X, Y; public float F; }

        /// <summary>The profile of a cutter on the grid: how high its underside is over the tip. grow = wider by that (stock at walls).</summary>
        private List<Offset> Profile(Cutter c, double grow, bool shank)
        {
            double r = c.Diameter / 2.0, rs = shank && c.CuttingLength > 0 ? c.ShankDiameter / 2.0 : 0.0;
            double reach = Math.Max(r, rs) + grow;
            int k = (int)Math.Ceiling(reach / _d) + 1;
            var list = new List<Offset>();
            for (int y = -k; y <= k; y++)
                for (int x = -k; x <= k; x++)
                {
                    double dist = Math.Max(0, Math.Sqrt(x * x + y * y) * _d - grow);
                    // half a place more, so that a wall between two places is not cut
                    double f;
                    if (dist <= r + 0.5 * _d) { double e = Math.Min(dist, r); f = c.Shape == Cutter.Ball ? r - Math.Sqrt(Math.Max(0, r * r - e * e)) : 0.0; }
                    else if (dist <= rs + 0.5 * _d) f = c.CuttingLength;
                    else continue;
                    list.Add(new Offset { X = x, Y = y, F = (float)f });
                }
            return list;
        }

        /// <summary>The lowest height of the tip for every place of the tool axis.</summary>
        private float[] DropCutter(Cutter c, double stock) => DropCutter(c, stock, stock);

        private float[] DropCutter(Cutter c, double wider, double higher)
        {
            var profile = Profile(c, wider, true).ToArray();
            var cl = new float[_h.Length];
            float floor = (float)_floor, add = (float)higher;
            Parallel.For(0, _ny, y =>
            {
                for (int x = 0; x < _nx; x++)
                {
                    int i = y * _nx + x;
                    if (!_region[i]) { cl[i] = float.NaN; continue; }
                    float best = float.NegativeInfinity;
                    foreach (var o in profile)
                    {
                        int u = x + o.X, v = y + o.Y;
                        if (u < 0 || v < 0 || u >= _nx || v >= _ny) continue;
                        float z = _h[v * _nx + u] - o.F;
                        if (z > best) best = z;
                    }
                    cl[i] = Math.Max(best + add, floor);
                }
            });
            return cl;
        }

        /// <summary>Height of the tip at a place; NaN outside the region.</summary>
        private double At(float[] cl, double px, double py, bool soft = false)
        {
            double fx = (px - _x0) / _d, fy = (py - _y0) / _d;
            int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
            if (x < 0 || y < 0 || x >= _nx - 1 || y >= _ny - 1) return double.NaN;
            double u = fx - x, v = fy - y;
            float a = cl[y * _nx + x], b = cl[y * _nx + x + 1], c = cl[(y + 1) * _nx + x], d = cl[(y + 1) * _nx + x + 1];
            if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d))
            {
                // at the edge of the region the highest of the places around counts
                float n = cl[(v < 0.5 ? y : y + 1) * _nx + (u < 0.5 ? x : x + 1)];
                if (float.IsNaN(n)) return double.NaN;
                foreach (float q in new[] { a, b, c, d }) if (!float.IsNaN(q) && q > n) n = q;
                return n;
            }
            // over a step (a wall between two places) the higher side counts
            const double no = 1e-4; // corners without weight do not count
            double hi = double.MinValue, lo = double.MaxValue;
            if (u < 1 - no && v < 1 - no) { hi = Math.Max(hi, a); lo = Math.Min(lo, a); }
            if (u > no && v < 1 - no) { hi = Math.Max(hi, b); lo = Math.Min(lo, b); }
            if (u < 1 - no && v > no) { hi = Math.Max(hi, c); lo = Math.Min(lo, c); }
            if (u > no && v > no) { hi = Math.Max(hi, d); lo = Math.Min(lo, d); }
            if (!soft && hi - lo > 2.5 * _d) return hi;
            return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
        }

        // ================================================================ rows

        private sealed class Run { public int Row; public List<Point3d> P; public bool Done; }

        /// <summary>Rows over the region at an angle. z(x, y) = height of the tip, NaN = not here.</summary>
        private List<Run> Rows(double angleDeg, double spacing, Func<double, double, double> z)
        {
            double angle = angleDeg * Math.PI / 180.0, cos = Math.Cos(angle), sin = Math.Sin(angle);
            if (Math.Abs(cos) < 1e-12) cos = 0; if (Math.Abs(sin) < 1e-12) sin = 0;
            // the region in the turned frame
            double cx = _x0 + 0.5 * (_nx - 1) * _d, cy = _y0 + 0.5 * (_ny - 1) * _d, u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            for (int y = 0; y < _ny; y++)
                for (int x = 0; x < _nx; x++)
                {
                    if (!_region[y * _nx + x]) continue;
                    double px = _x0 + x * _d - cx, py = _y0 + y * _d - cy, u = px * cos + py * sin, v = -px * sin + py * cos;
                    if (u < u0) u0 = u; if (u > u1) u1 = u; if (v < v0) v0 = v; if (v > v1) v1 = v;
                }
            var runs = new List<Run>();
            if (u1 < u0) return runs;
            int nv = Math.Max(1, (int)Math.Ceiling((v1 - v0) / spacing - 1e-6)), nu = Math.Max(1, (int)Math.Ceiling((u1 - u0) / _d));
            var rows = new List<Run>[nv + 1];
            Parallel.For(0, nv + 1, r =>
            {
                double v = nv == 0 ? v0 : v0 + (v1 - v0) * r / nv;
                var list = new List<Run>();
                Run run = null;
                for (int i = 0; i <= nu; i++)
                {
                    double u = u0 + (u1 - u0) * i / nu, px = cx + u * cos - v * sin, py = cy + u * sin + v * cos, h = z(px, py);
                    if (double.IsNaN(h)) { run = null; continue; }
                    if (run == null) { run = new Run { Row = r, P = new List<Point3d>() }; list.Add(run); }
                    run.P.Add(new Point3d(px, py, h));
                }
                rows[r] = list.Where(q => q.P.Count >= 2).ToList();
            });
            foreach (var l in rows) runs.AddRange(l);
            return runs;
        }

        /// <summary>Runs joined to cuts: the next row goes on where the last one ended as long as it begins close by and the way is free.</summary>
        private List<List<Point3d>> Chain(List<Run> runs, double maxLink, Func<double, double, double> z)
        {
            var byRow = runs.GroupBy(r => r.Row).ToDictionary(g => g.Key, g => g.ToList());
            var cuts = new List<List<Point3d>>();
            var open = new HashSet<Run>(runs);
            Point3d tool = runs.Count > 0 ? runs[0].P[0] : Point3d.Origin;
            while (open.Count > 0)
            {
                Cancel.Check();
                // nearest run, from either end
                Run start = null; bool flip = false; double best = double.MaxValue;
                foreach (var r in open)
                {
                    double a = Flat(r.P[0], tool), b = Flat(r.P[r.P.Count - 1], tool);
                    if (a < best) { best = a; start = r; flip = false; }
                    if (b < best) { best = b; start = r; flip = true; }
                }
                var cut = new List<Point3d>();
                Run now = start;
                while (now != null)
                {
                    if (flip) now.P.Reverse();
                    cut.AddRange(now.P);
                    open.Remove(now); now.Done = true;
                    tool = cut[cut.Count - 1];
                    Run next = null; best = maxLink * maxLink; List<Point3d> way = null;
                    foreach (int row in new[] { now.Row + 1, now.Row - 1 })
                    {
                        if (!byRow.TryGetValue(row, out var list)) continue;
                        foreach (var r in list)
                        {
                            if (r.Done) continue;
                            foreach (bool end in new[] { false, true })
                            {
                                Point3d p = end ? r.P[r.P.Count - 1] : r.P[0];
                                double dist = Flat(p, tool);
                                if (dist >= best) continue;
                                var link = Link(tool, p, z);
                                if (link == null) continue;
                                best = dist; next = r; flip = end; way = link;
                            }
                        }
                        if (next != null) break; // the next row first, the one before only where it was left open
                    }
                    if (next != null) cut.AddRange(way);
                    now = next;
                }
                cuts.Add(cut);
            }
            return cuts;
        }

        private static double Flat(Point3d a, Point3d b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

        private List<Point3d> Link(Point3d a, Point3d b, Func<double, double, double> z)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Flat(a, b)) / _d));
            var way = new List<Point3d>();
            for (int i = 1; i < n; i++)
            {
                double x = a.X + (b.X - a.X) * i / n, y = a.Y + (b.Y - a.Y) * i / n, h = z(x, y);
                if (double.IsNaN(h)) return null;
                way.Add(new Point3d(x, y, h));
            }
            return way;
        }

        // ================================================================ operations

        private static double FeedOf(Cutter c) => c.Feed > 0 ? Math.Max(1, Math.Min(1000, c.Feed)) : 40.0;
        private static double RpmOf(Cutter c) => c.SpindleSpeed > 0 ? c.SpindleSpeed : 30000.0;

        private void AddRoughing()
        {
            Region(RoughMargin);
            double stock = Stock;
            float[] cl = DropCutter(Rough, stock);
            double min = double.MaxValue;
            for (int i = 0; i < cl.Length; i++) if (!float.IsNaN(cl[i]) && cl[i] < min) min = cl[i];
            if (min >= MaterialThickness - 0.01) { Remark("Nothing to rough: the part fills the material."); return; }
            CheckDepth(Rough, min);
            var op = new Operation { Name = "Rough", Cutter = Rough, Feed = FeedOf(Rough), Rpm = RpmOf(Rough) };
            int levels = Levels(op, cl, null, min, StepDown, RoughStepover, RoughPattern, RoughAngle, out double step);
            if (op.Cuts.Count > 0) Operations.Add(op);
            Report.Add(Inv.F($"Rough: {Rough.Name}, {levels} level(s) of {step:0.##} mm, {(RoughPattern == 0 ? "rings" : "rows")} {RoughStepover:0.##} mm apart, {stock:0.##} mm left for the finishing, {op.Cuts.Count} cuts, {op.Length / 1000:0.0} m."));
        }

        /// <summary>
        /// Rings (or rows) at every level. material = how high the tool touches what is still there (null = the
        /// whole block): places with nothing to cut are left out.
        /// </summary>
        private int Levels(Operation op, float[] cl, float[] material, double min, double stepDown, double stepover, int pattern, double angle, out double step)
        {
            double top = MaterialThickness;
            int levels = Math.Max(1, (int)Math.Ceiling((top - min) / stepDown - 1e-6));
            step = (top - min) / levels;
            for (int k = 1; k <= levels; k++)
            {
                Cancel.Check();
                double above = top - (k - 1) * step, level = top - k * step;
                // where the level before has already reached the allowed height there is nothing left
                var mask = new bool[cl.Length];
                bool any = false;
                for (int i = 0; i < cl.Length; i++)
                    if (!float.IsNaN(cl[i]) && cl[i] < above - 0.02 && (material == null || material[i] > Math.Max(level, cl[i]) + 0.15)) { mask[i] = true; any = true; }
                if (!any) continue;
                Func<double, double, double> z = (x, y) =>
                {
                    double h = At(cl, x, y, Stock >= 0.2); // the stock takes the half place this may be off at a wall
                    if (double.IsNaN(h) || h > above - 0.02) return double.NaN;
                    if (material != null && !mask[Near(x, y)]) return double.NaN;
                    return Math.Max(h, level);
                };
                Func<double, double, double> way = (x, y) => { double h = At(cl, x, y, Stock >= 0.2); return double.IsNaN(h) ? double.NaN : Math.Max(h, level); };
                double from = Math.Min(top, above) + 0.2;
                if (pattern == 0)
                {
                    foreach (var cut in JoinRings(Rings(mask, stepover, way, op.Cutter.Diameter / 2.0 - stepover), stepover, way, from, true, ref _tool)) op.Cuts.Add(new Cut { Points = Simplify(cut) });
                    foreach (var cut in Feet(mask, cl, level, above, false)) op.Cuts.Add(new Cut { Points = Simplify(Ramp(cut, from)) });
                    if (op.Cuts.Count > 0) _tool = op.Cuts[op.Cuts.Count - 1].Points[op.Cuts[op.Cuts.Count - 1].Points.Count - 1];
                    continue;
                }
                var cuts = Chain(Rows(angle, stepover, z), Math.Max(3.0 * stepover, 12.0), way);
                cuts.AddRange(Walls(mask, cl, level, material == null ? 1.5 : 0.4, true));
                cuts.AddRange(Feet(mask, cl, level, above, false));
                foreach (var cut in cuts)
                    if (cut.Count >= 2 && (material == null || Length(cut) > 0.15)) op.Cuts.Add(new Cut { Points = Simplify(Ramp(cut, from)) });
            }
            return levels;
        }

        private int Near(double x, double y)
            => Math.Max(0, Math.Min(_ny - 1, (int)Math.Round((y - _y0) / _d))) * _nx + Math.Max(0, Math.Min(_nx - 1, (int)Math.Round((x - _x0) / _d)));

        // ---- rings

        private Point3d _tool = Point3d.Unset; // where the last cut ended
        private sealed class Ring { public List<Point3d> P; public int K; public bool Done; public double MinX, MinY, MaxX, MaxY; }

        /// <summary>Squared distance (in places) of every place of an area to the nearest place outside it.</summary>
        private float[] Distance(bool[] mask)
        {
            const float far = 1e12f;
            var f = new float[mask.Length];
            for (int i = 0; i < f.Length; i++) f[i] = mask[i] ? far : 0;
            Action<Func<int, int>, int> line = (at, n) =>
            {
                var g = new float[n]; var v = new int[n]; var z = new float[n + 1];
                for (int q = 0; q < n; q++) g[q] = f[at(q)];
                int k = 0; v[0] = 0; z[0] = -far; z[1] = far;
                for (int q = 1; q < n; q++)
                {
                    float s = ((g[q] + (float)q * q) - (g[v[k]] + (float)v[k] * v[k])) / (2f * q - 2f * v[k]);
                    while (s <= z[k] && k > 0) { k--; s = ((g[q] + (float)q * q) - (g[v[k]] + (float)v[k] * v[k])) / (2f * q - 2f * v[k]); }
                    if (s <= z[k]) { v[0] = q; k = 0; z[0] = -far; z[1] = far; continue; }
                    k++; v[k] = q; z[k] = s; z[k + 1] = far;
                }
                k = 0;
                for (int q = 0; q < n; q++) { while (z[k + 1] < q) k++; f[at(q)] = (q - v[k]) * (float)(q - v[k]) + g[v[k]]; }
            };
            Parallel.For(0, _ny, y => line(x => y * _nx + x, _nx));
            Parallel.For(0, _nx, x => line(y => y * _nx + x, _ny));
            return f;
        }

        /// <summary>Lines of equal distance to the border of an area: at the border, one stepover inside, two ... The inside lies on the left (climb).</summary>
        private List<Ring> Rings(bool[] mask, double stepover, Func<double, double, double> z, double thin)
        {
            var sq = Distance(mask);
            var dist = new float[sq.Length];
            double deepest = 0;
            for (int i = 0; i < sq.Length; i++) { dist[i] = (float)(Math.Sqrt(Math.Min(sq[i], 1e9f)) * _d); if (dist[i] > deepest) deepest = dist[i]; }
            double first = 1.45 * _d; // the line at the border: all places around it lie in the area, so a wall beside it does not lift the tool
            int count = deepest < first ? 0 : 1 + (int)Math.Floor((deepest - first) / stepover);
            var rings = new List<Ring>();
            var seg = new Dictionary<long, long>[count];
            for (int k = 0; k < count; k++) seg[k] = new Dictionary<long, long>();
            // edges of the grid: 2 per place (to the right, up)
            for (int y = 0; y + 1 < _ny; y++)
                for (int x = 0; x + 1 < _nx; x++)
                {
                    int a = y * _nx + x, b = a + 1, c = a + _nx + 1, d = a + _nx;
                    float va = dist[a], vb = dist[b], vc = dist[c], vd = dist[d];
                    float lo = Math.Min(Math.Min(va, vb), Math.Min(vc, vd)), hi = Math.Max(Math.Max(va, vb), Math.Max(vc, vd));
                    if (hi < first) continue;
                    int k0 = Math.Max(0, (int)Math.Ceiling((lo - first) / stepover)), k1 = Math.Min(count - 1, (int)Math.Floor((hi - first) / stepover));
                    for (int k = k0; k <= k1; k++)
                    {
                        double iso = first + k * stepover;
                        bool ia = va > iso, ib = vb > iso, ic = vc > iso, id = vd > iso;
                        if (ia == ib && ib == ic && ic == id) continue;
                        long eab = 2L * a, ebc = 2L * b + 1, edc = 2L * d, ead = 2L * a + 1;
                        // walk round counter-clockwise: where the inside is left, the line goes to where it is entered again
                        bool[] inside = { ia, ib, ic, id };
                        long[] edge = { eab, ebc, edc, ead };
                        for (int e = 0; e < 4; e++)
                        {
                            if (!(inside[e] && !inside[(e + 1) & 3])) continue;
                            for (int f = 1; f < 4; f++)
                                if (!inside[(e + f) & 3] && inside[(e + f + 1) & 3]) { seg[k][edge[e]] = edge[(e + f) & 3]; break; }
                        }
                    }
                }
            for (int k = 0; k < count; k++)
            {
                double iso = first + k * stepover;
                Func<long, Point3d> at = e =>
                {
                    int i = (int)(e / 2), j = (e & 1) == 0 ? i + 1 : i + _nx;
                    double t = (iso - dist[i]) / (dist[j] - dist[i]);
                    if (double.IsNaN(t)) t = 0.5;
                    return new Point3d(_x0 + (i % _nx + ((e & 1) == 0 ? t : 0)) * _d, _y0 + (i / _nx + ((e & 1) == 0 ? 0 : t)) * _d, 0);
                };
                var seen = new HashSet<long>();
                foreach (var start in seg[k].Keys.ToList())
                {
                    if (seen.Contains(start)) continue;
                    var pts = new List<Point3d>();
                    long e = start; bool closed = false;
                    while (seen.Add(e)) { pts.Add(at(e)); if (!seg[k].TryGetValue(e, out e)) break; if (e == start) { closed = true; break; } }
                    if (pts.Count < 3 || !closed) continue;
                    if (thin > 0 && k > 0)
                    {
                        // so narrow that the ring around it clears it: no turning on the spot
                        double area = 0, round = 0;
                        for (int i = 0; i < pts.Count; i++) { var q = pts[i]; var w = pts[(i + 1) % pts.Count]; area += q.X * w.Y - w.X * q.Y; round += q.DistanceTo(w); }
                        if (area > 0 && area / round < thin) continue;
                    }
                    // the steps of the grid smoothed away; the height is read at the new place, so nothing is cut too deep
                    int half = Math.Max(1, (int)Math.Round(0.4 / _d)), n = pts.Count;
                    var smooth = new List<Point3d>(n);
                    for (int i = 0; i < n; i++)
                    {
                        double sx = 0, sy = 0; int m = 0, h = Math.Min(half, (n - 1) / 4);
                        for (int o = -h; o <= h; o++) { var q = pts[((i + o) % n + n) % n]; sx += q.X; sy += q.Y; m++; }
                        double px = sx / m, py = sy / m, pz = z(px, py);
                        double pz0 = z(pts[i].X, pts[i].Y);
                        if (double.IsNaN(pz) || (!double.IsNaN(pz0) && pz > pz0 + 0.15)) { px = pts[i].X; py = pts[i].Y; pz = pz0; } // not up a wall
                        if (double.IsNaN(pz)) { int near = Near(px, py); px = _x0 + (near % _nx) * _d; py = _y0 + (near / _nx) * _d; pz = z(px, py); }
                        if (!double.IsNaN(pz)) smooth.Add(new Point3d(px, py, pz));
                    }
                    if (smooth.Count < 3) continue;
                    var r = new Ring { P = smooth, K = k };
                    r.MinX = smooth.Min(q => q.X); r.MaxX = smooth.Max(q => q.X); r.MinY = smooth.Min(q => q.Y); r.MaxY = smooth.Max(q => q.Y);
                    rings.Add(r);
                }
            }
            return rings;
        }

        /// <summary>
        /// The rings one after the other, each begun where the last one was left. outwards = from the middle of
        /// the area to its border (roughing, with a ramp along the ring from the given height), else from the border inwards.
        /// Where the next ring is far away the tool is lifted.
        /// </summary>
        private List<List<Point3d>> JoinRings(List<Ring> rings, double stepover, Func<double, double, double> z, double from, bool outwards, ref Point3d tool)
        {
            var cuts = new List<List<Point3d>>();
            var open = new List<Ring>(rings);
            // climb: what is still there has to lie on the right - outside of the ring going outwards, inside of it going inwards
            if (!outwards) foreach (var r in rings) r.P.Reverse();
            double reach = 2.5 * stepover + 2 * _d;
            while (open.Count > 0)
            {
                Cancel.Check();
                // a new start: the nearest of the rings that have no open ring further in (or out) beside them
                Ring start = null; int at = 0; double best = double.MaxValue;
                int extreme = outwards ? open.Max(r => r.K) : open.Min(r => r.K);
                foreach (var r in open)
                {
                    // roughing begins at the innermost ring of a place: one without a deeper ring close by
                    if (outwards && r.K < extreme && open.Any(o => o.K == r.K + 1 && Beside(r, o, 1.8 * stepover))) continue;
                    if (!outwards && r.K > extreme && open.Any(o => o.K == r.K - 1 && Beside(r, o, 1.8 * stepover))) continue;
                    if (!tool.IsValid) { if (start == null) { start = r; at = 0; } continue; }
                    int i = Nearest(r, tool, out double d2);
                    if (d2 < best) { best = d2; start = r; at = i; }
                }
                if (start == null) start = open[0];
                var cut = new List<Point3d>();
                Ring now = start; List<Point3d> way = null;
                while (now != null)
                {
                    var p = new List<Point3d>();
                    for (int i = 0; i <= now.P.Count; i++) p.Add(now.P[(at + i) % now.P.Count]);
                    if (cut.Count == 0) cut.AddRange(outwards ? Spiral(p, from) : p);
                    else { cut.AddRange(way); cut.AddRange(p); }
                    now.Done = true; open.Remove(now);
                    tool = cut[cut.Count - 1];
                    // on to a ring beside this one; a little ahead, so that the tool does not turn on the spot
                    Vector3d ahead = p.Count > 1 ? p[1] - p[0] : Vector3d.Zero; ahead.Z = 0;
                    Point3d aim = tool;
                    if (ahead.Unitize()) aim = tool + ahead * (1.2 * stepover);
                    Ring next = null; best = double.MaxValue; int nextAt = 0; way = null;
                    foreach (var r in open)
                    {
                        if (Math.Abs(r.K - now.K) > 1) continue;
                        if (tool.X < r.MinX - reach || tool.X > r.MaxX + reach || tool.Y < r.MinY - reach || tool.Y > r.MaxY + reach) continue;
                        int i = Nearest(r, aim, out double _);
                        double d2 = Flat(r.P[i], tool);
                        // outwards first the rings further in
                        double cost = d2 * (outwards ? (r.K > now.K ? 0.25 : 1) : (r.K < now.K ? 0.25 : 1));
                        if (d2 > reach * reach || cost >= best) continue;
                        var link = Link(tool, r.P[i], z);
                        if (link == null) continue;
                        best = cost; next = r; nextAt = i; way = link;
                    }
                    now = next; at = nextAt;
                }
                cuts.Add(cut);
            }
            return cuts;
        }

        private static bool Beside(Ring a, Ring b, double d)
        {
            if (a.MinX > b.MaxX + d || b.MinX > a.MaxX + d || a.MinY > b.MaxY + d || b.MinY > a.MaxY + d) return false;
            int sa = Math.Max(1, a.P.Count / 200), sb = Math.Max(1, b.P.Count / 400);
            double dd = d * d;
            for (int i = 0; i < b.P.Count; i += sb) for (int j = 0; j < a.P.Count; j += sa) if (Flat(b.P[i], a.P[j]) < dd) return true;
            return false;
        }

        private static int Nearest(Ring r, Point3d p, out double d2)
        {
            int at = 0; d2 = double.MaxValue;
            for (int i = 0; i < r.P.Count; i++) { double d = Flat(r.P[i], p); if (d < d2) { d2 = d; at = i; } }
            return at;
        }

        /// <summary>Into the material along a closed ring, 3 degrees steep, round and round until it is down; then the ring itself.</summary>
        private static List<Point3d> Spiral(List<Point3d> ring, double from)
        {
            double len = 0;
            for (int i = 1; i < ring.Count; i++) len += Math.Sqrt(Flat(ring[i], ring[i - 1]));
            if (len < 1.5) // too small to go round: straight down
            {
                var down = new List<Point3d> { new Point3d(ring[0].X, ring[0].Y, Math.Max(from, ring[0].Z)) };
                down.AddRange(ring);
                return down;
            }
            var res = new List<Point3d> { new Point3d(ring[0].X, ring[0].Y, Math.Max(from, ring[0].Z)) };
            double z = from, low = ring.Min(q => q.Z);
            int n = ring.Count - 1, at = 0;
            for (int guard = 0; guard < 200000; guard++)
            {
                Point3d a = ring[at], b = ring[at + 1];
                z -= Math.Sqrt(Flat(a, b)) * 0.0524;
                at++;
                bool touches = z <= b.Z;
                res.Add(new Point3d(b.X, b.Y, Math.Max(z, b.Z)));
                if (at == n) at = 0;
                if (z <= low || (touches && z < b.Z - 0.3)) break;
            }
            // a full turn from here, so that the ramp is cut away
            for (int i = 1; i <= n; i++) res.Add(ring[(at + i) % n]);
            // and on to where the ring begins, to leave it there
            for (int i = (at % n) + 1; i <= n && at != 0; i++) res.Add(ring[i]);
            return res;
        }

        /// <summary>The stretches of the border of an area that run along a wall (steeper than 45 degrees) or along the edge of the region.</summary>
        /// <summary>
        /// Along the foot of walls inside an area (a low house in a street that is cut in the same level): rings
        /// and rows follow the border of the area, so beside such a wall a strip would stay, for the next level to
        /// cut two steps deep.
        /// </summary>
        private List<List<Point3d>> Feet(bool[] mask, float[] cl, double level, double above, bool edge)
        {
            var all = new List<List<Point3d>>();
            foreach (double t in new[] { level + 0.05, level + 0.5 * (above - level) })
            {
                var low = new bool[mask.Length];
                bool any = false, part = false;
                for (int i = 0; i < mask.Length; i++) { low[i] = mask[i] && cl[i] <= t; if (low[i]) any = true; else if (mask[i]) part = true; }
                if (!any || !part) continue;
                foreach (var run in Walls(low, cl, level, 1.5, edge, mask)) all.Add(run);
            }
            return all;
        }

        private List<List<Point3d>> Walls(bool[] mask, float[] cl, double level, double shortest, bool edge, bool[] within = null)
        {
            // border lines between places inside and outside; a vertex = the place inside, so the line stays on allowed ground
            var next = new Dictionary<long, long>();
            Func<int, int, long> key = (a, b) => (long)a * _h.Length + b; // from inside place a to outside place b
            for (int y = 0; y + 1 < _ny; y++)
                for (int x = 0; x + 1 < _nx; x++)
                {
                    int a = y * _nx + x, b = a + 1, c = a + _nx + 1, d = a + _nx;
                    int code = (mask[a] ? 1 : 0) | (mask[b] ? 2 : 0) | (mask[c] ? 4 : 0) | (mask[d] ? 8 : 0);
                    if (code == 0 || code == 15) continue;
                    // edges: 0 = ab, 1 = bc, 2 = cd, 3 = da; inside lies on the left
                    int[] corner = { a, b, c, d };
                    for (int e = 0; e < 4; e++)
                    {
                        int p = corner[e], q = corner[(e + 1) & 3];
                        if (!(mask[p] && !mask[q])) continue; // leaves the area across this edge (counter-clockwise)
                        // ... and comes back over the next edge that enters it
                        for (int f = 1; f < 4; f++)
                        {
                            int p2 = corner[(e + f) & 3], q2 = corner[(e + f + 1) & 3];
                            if (!mask[p2] && mask[q2]) { next[key(p, q)] = key(q2, p2); break; }
                        }
                    }
                }
            var res = new List<List<Point3d>>();
            var seen = new HashSet<long>();
            foreach (var startKey in next.Keys.ToList())
            {
                if (seen.Contains(startKey)) continue;
                var ring = new List<long>();
                long k = startKey; bool closed = false;
                while (true)
                {
                    if (!seen.Add(k)) { closed = k == startKey; break; }
                    ring.Add(k);
                    if (!next.TryGetValue(k, out k)) break;
                }
                int n = ring.Count;
                if (n < 4) continue;
                var pts = new Point3d[n]; var steep = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    int a = (int)(ring[i] / _h.Length), b = (int)(ring[i] % _h.Length);
                    pts[i] = new Point3d(_x0 + (a % _nx) * _d, _y0 + (a / _nx) * _d, Math.Max(level, cl[a]));
                    steep[i] = (float.IsNaN(cl[b]) ? edge : cl[b] - cl[a] > 1.0 * _d) && (within == null || within[b]); // inside an area only the walls in it
                }
                // short gaps closed, short pieces dropped
                int gap = (int)Math.Ceiling(3.0 / _d);
                if (!steep.Any(q => q)) continue;
                int first = Array.IndexOf(steep, true);
                List<Point3d> run = null; int off = 0;
                for (int j = 0; j <= n; j++)
                {
                    int i = (first + j) % n;
                    if (j == n && !closed) break;
                    if (steep[i])
                    {
                        if (run == null) run = new List<Point3d>();
                        else if (off > 0) for (int g = off; g >= 1; g--) run.Add(pts[((i - g) % n + n) % n]);
                        off = 0;
                        if (run.Count == 0 || run[run.Count - 1].DistanceTo(pts[i]) > 1e-6) run.Add(pts[i]);
                    }
                    else if (run != null && ++off > gap) { if (Length(run) > shortest) res.Add(run); run = null; off = 0; }
                }
                if (run != null && Length(run) > shortest) res.Add(run);
            }
            return res;
        }

        private static double Length(List<Point3d> p) { double l = 0; for (int i = 1; i < p.Count; i++) l += p[i].DistanceTo(p[i - 1]); return l; }

        /// <summary>
        /// How high the tip of a cutter stands when it touches what is still there over the part. A slimmer
        /// cutter is asked, so that the thin skin of the stock on a wall does not count as a full cut.
        /// </summary>
        private float[] Touch(Cutter c, float[] s)
        {
            float[] keep = _h; double floor = _floor;
            var over = new float[s.Length];
            for (int i = 0; i < s.Length; i++) over[i] = keep[i] > float.NegativeInfinity ? s[i] : float.NegativeInfinity;
            _h = over; _floor = double.MinValue;
            try { return DropCutter(c, -Math.Min(Stock + 0.5, 0.25 * c.Diameter), 0); } finally { _h = keep; _floor = floor; }
        }

        /// <summary>How deep the finishing cutter would dive into what is still there, at most.</summary>
        private double Engagement(float[] s)
        {
            float[] cl = _finishCl ?? (_finishCl = DropCutter(Finish, 0)), material = Touch(Finish, s);
            double max = 0;
            for (int i = 0; i < cl.Length; i++) if (!float.IsNaN(cl[i]) && material[i] - cl[i] > max) max = material[i] - cl[i];
            return max;
        }

        private float[] _finishCl;

        /// <summary>What the thick cutter could not reach, taken out with the fine one in levels before it finishes.</summary>
        private void AddRest(float[] s)
        {
            double limit = 1.0 * Finish.Diameter;
            if (MaxBeforeFinish <= limit) return;
            float[] cl = DropCutter(Finish, 0, Math.Min(Stock, 0.3)); // no stock on the walls: a narrow valley has no room for it
            float[] material = Touch(Finish, s);
            double min = double.MaxValue;
            for (int i = 0; i < cl.Length; i++) if (!float.IsNaN(cl[i]) && cl[i] < min) min = cl[i];
            var op = new Operation { Name = "Rest", Cutter = Finish, Feed = FeedOf(Finish), Rpm = RpmOf(Finish) };
            // only where more is left than the finishing pass may take
            for (int i = 0; i < material.Length; i++) if (!float.IsNaN(material[i]) && material[i] - cl[i] < 0.7 * limit) material[i] = float.NegativeInfinity;
            Levels(op, cl, material, min, Math.Min(StepDown, 0.5 * Finish.Diameter), 0.45 * Finish.Diameter, 0, FinishAngle, out double step);
            if (op.Cuts.Count == 0) return;
            Operations.Add(op);
            Report.Add(Inv.F($"Rest: {Finish.Name} takes out what the thick cutter did not reach (up to {MaxBeforeFinish:0.0} mm), levels of {step:0.##} mm, {op.Cuts.Count} cuts, {op.Length / 1000:0.0} m."));
        }

        /// <summary>The way into the material: forth and back along the first stretch of the cut, 3 degrees steep, never below the cut itself.</summary>
        private static List<Point3d> Ramp(List<Point3d> cut, double from)
        {
            double depth = from - cut[0].Z;
            if (depth < 0.05) return cut;
            const double slope = 0.0524; // tan 3 degrees
            var seg = new List<Point3d> { cut[0] };
            double len = 0;
            for (int i = 1; i < cut.Count && len < 20.0; i++) { len += cut[i].DistanceTo(cut[i - 1]); seg.Add(cut[i]); }
            var res = new List<Point3d> { new Point3d(cut[0].X, cut[0].Y, from) };
            if (len > 0.5)
            {
                double need = depth / slope, s = 0;
                int passes = (int)Math.Ceiling(need / len);
                if ((passes & 1) == 1) passes++; // back at the beginning when it is down
                double fall = depth / (passes * len);
                for (int p = 0; p < passes && p < 400; p++)
                {
                    for (int i = 1; i < seg.Count; i++)
                    {
                        Point3d a = seg[(p & 1) == 0 ? i - 1 : seg.Count - i], b = seg[(p & 1) == 0 ? i : seg.Count - 1 - i];
                        s += a.DistanceTo(b);
                        res.Add(new Point3d(b.X, b.Y, Math.Max(b.Z, from - fall * s)));
                    }
                }
            }
            res.AddRange(cut);
            return res;
        }

        private void AddFinishing(int pattern, double angle, string name)
        {
            float[] cl = _finishCl ?? (_finishCl = DropCutter(Finish, 0));
            double min = double.MaxValue;
            for (int i = 0; i < cl.Length; i++) if (!float.IsNaN(cl[i]) && cl[i] < min) min = cl[i];
            if (min == double.MaxValue) return;
            CheckDepth(Finish, min);
            double top = MaterialThickness;
            Func<double, double, double> z = (x, y) => { double h = At(cl, x, y); return double.IsNaN(h) || h > top + 0.005 ? double.NaN : h; };
            var op = new Operation { Name = name, Cutter = Finish, Feed = FeedOf(Finish), Rpm = RpmOf(Finish) };
            if (pattern == 0)
            {
                var mask = new bool[cl.Length];
                for (int i = 0; i < cl.Length; i++) mask[i] = !float.IsNaN(cl[i]) && cl[i] <= top + 0.005;
                foreach (var cut in JoinRings(Rings(mask, FinishStepover, z, 0), FinishStepover, z, 0, false, ref _tool)) op.Cuts.Add(new Cut { Points = Simplify(cut) });
            }
            else
                foreach (var cut in Chain(Rows(angle, FinishStepover, z), Math.Max(3.0 * FinishStepover, 2.0), z))
                    if (cut.Count >= 2) op.Cuts.Add(new Cut { Points = Simplify(cut) });
            if (op.Cuts.Count > 0) Operations.Add(op);
            double r = Finish.Diameter / 2.0;
            double scallop = Finish.Shape == Cutter.Ball ? r - Math.Sqrt(Math.Max(0, r * r - FinishStepover * FinishStepover / 4.0)) : 0;
            Report.Add(Inv.F($"{name}: {Finish.Name}, {(pattern == 0 ? "rings" : "rows")} {FinishStepover:0.##} mm apart") + (Finish.Shape == Cutter.Ball ? Inv.F($" (ridges {scallop:0.000} mm on flat ground)") : "") + Inv.F($", {op.Cuts.Count} cut(s), {op.Length / 1000:0.0} m."));
        }

        /// <summary>The part is cut out of the plate: round and round, deeper every time, over the tabs in the last levels.</summary>
        private void AddOutline(BoundingBox box)
        {
            Cutter c = Rough ?? Finish;
            PathsD foot = ZundMill.Footprint(Part);
            // holes through the model are cut out as well, first; holes the cutter does not fit into stay
            PathsD loops = foot.Count == 0 ? null : Clipper.InflatePaths(foot, c.Diameter / 2.0, JoinType.Round, EndType.Polygon, 2.0, 4, 0.01);
            if (loops == null || loops.Count == 0) { Warning("The outline of the model could not be found: it is not cut out."); return; }
            int tabs = Hold == HoldTabs ? Math.Max(0, Math.Min(40, Tabs)) : 0;
            double skin = Hold == HoldSkin ? Math.Max(0.1, Math.Min(0.4 * MaterialThickness, HoldHeight > 0 ? HoldHeight : 0.6)) : 0;
            CheckDepth(c, skin);
            double wide = TabWidth > 0 ? Math.Max(1.0, TabWidth) : 8.0;
            double tabHeight = Math.Min(HoldHeight > 0 ? HoldHeight : 2.0, 0.4 * MaterialThickness), tabWidth = wide + c.Diameter;
            int levels = Math.Max(1, (int)Math.Ceiling((MaterialThickness - skin) / StepDown - 1e-6));
            double step = (MaterialThickness - skin) / levels;
            var op = new Operation { Name = "Cut out", Cutter = c, Feed = FeedOf(c), Rpm = RpmOf(c) };
            int made = 0;
            int inner = loops.Count(q => Clipper.Area(q) < 0), holes = foot.Count(q => Clipper.Area(q) < 0);
            if (holes > inner) Remark(Inv.F($"{holes - inner} hole(s) through the model are narrower than '{c.Name}' ({c.Diameter:0.#} mm) and are not cut out."));
            foreach (var path in loops.OrderBy(q => Clipper.Area(q) > 0 ? 1 : 0))
            {
                if (Math.Abs(Clipper.Area(path)) < 0.01) continue;
                PathD s = Clipper.SimplifyPath(path, 0.003, true);
                if (s.Count < 3) s = path;
                var loop = new Polyline(s.Select(q => new Point3d(q.x, q.y, 0)));
                loop.Reverse(); // round the part clockwise, round a hole counter-clockwise: the part on the right, climb
                loop.Add(loop[0]);
                // begin in the middle of the longest straight stretch, where a mark is easy to sand
                int longest = 0;
                for (int i = 1; i + 1 < loop.Count; i++) if (loop[i + 1].DistanceTo(loop[i]) > loop[longest + 1].DistanceTo(loop[longest])) longest = i;
                var turned = new Polyline { 0.5 * (loop[longest] + loop[longest + 1]) };
                for (int i = 1; i < loop.Count; i++) turned.Add(loop[(longest + i) % (loop.Count - 1)]);
                turned.Add(turned[0]);
                loop = turned;
                double len = loop.Length, rampLen = Math.Min(len, Math.Max(10.0, step / 0.0524));
                int here = len < 4 * tabWidth ? 0 : Math.Min(tabs, (int)(len / (3 * tabWidth)));
                made += here;
                Func<double, double, double> over = (q, z) =>
                {
                    // over a tab the cutter rises, 45 degrees steep
                    for (int t = 0; t < here; t++)
                    {
                        double off = Math.Abs(((q - (t + 0.5) * len / here) % len + 1.5 * len) % len - 0.5 * len);
                        double h = tabHeight - Math.Max(0, off - tabWidth / 2);
                        if (h > z) z = h;
                    }
                    return z;
                };
                var cut = new Cut();
                double z0 = MaterialThickness + 0.2;
                for (int k = 1; k <= levels; k++)
                {
                    double z1 = MaterialThickness - k * step, zA = z0;
                    // down along the beginning of the loop, then round at the level
                    Along(loop, len, 0, rampLen, (q, t, at) => cut.Points.Add(new Point3d(q.X, q.Y, over(at, zA + (z1 - zA) * t))));
                    Along(loop, len, rampLen, len, (q, t, at) => cut.Points.Add(new Point3d(q.X, q.Y, over(at, z1))));
                    if (k == levels) Along(loop, len, 0, rampLen, (q, t, at) => cut.Points.Add(new Point3d(q.X, q.Y, over(at, z1))));
                    z0 = z1;
                }
                cut.Points = Simplify(cut.Points);
                op.Cuts.Add(cut);
            }
            if (op.Cuts.Count == 0) return;
            Operations.Add(op);
            Report.Add(Inv.F($"Cut out: {c.Name}, {op.Cuts.Count} loop(s){(inner > 0 ? Inv.F($", {inner} of them in holes") : "")}, {levels} level(s) of {step:0.##} mm {(skin > 0 ? Inv.F($"; a skin of {skin:0.##} mm stays at the bottom and holds it") : made > 0 ? Inv.F($"down to the table; {made} tab(s) {wide:0.#} mm wide and {tabHeight:0.#} mm high hold it") : "down to the table")}."));
            if (made == 0 && skin <= 0) Warning("No tabs: the part is loose at the end of the cut. The vacuum has to hold it.");
        }

        private static void Along(Polyline loop, double len, double from, double to, Action<Point3d, double, double> add)
        {
            int n = Math.Max(1, (int)Math.Ceiling((to - from) / 0.5));
            var at = new List<double>();
            for (int i = 0; i <= n; i++) at.Add(from + (to - from) * i / n);
            double s = 0;
            for (int i = 1; i < loop.Count; i++) { s += loop[i].DistanceTo(loop[i - 1]); if (s > from + 1e-6 && s < to - 1e-6) at.Add(s); } // the corners
            at.Sort();
            foreach (double q in at) add(At(loop, Math.Min(q, len)), (q - from) / Math.Max(1e-9, to - from), q);
        }

        private static Point3d At(Polyline loop, double s)
        {
            for (int i = 1; i < loop.Count; i++)
            {
                double l = loop[i].DistanceTo(loop[i - 1]);
                if (s <= l && l > 1e-9) return loop[i - 1] + (loop[i] - loop[i - 1]) * (s / l);
                s -= l;
            }
            return loop[loop.Count - 1];
        }

        /// <summary>Points on straight stretches left out (Douglas-Peucker, 0.008 mm).</summary>
        private static List<Point3d> Simplify(List<Point3d> p)
        {
            if (p.Count < 3) return p;
            var keep = new bool[p.Count];
            keep[0] = keep[p.Count - 1] = true;
            var todo = new Stack<(int, int)>();
            // pieces, so that long cuts stay fast
            for (int i = 0; i < p.Count - 1; i += 2000) { int e = Math.Min(i + 2000, p.Count - 1); keep[i] = keep[e] = true; todo.Push((i, e)); }
            while (todo.Count > 0)
            {
                var (a, b) = todo.Pop();
                if (b - a < 2) continue;
                Point3d A = p[a], B = p[b];
                Vector3d ab = B - A;
                double ll = ab.SquareLength, worst = 0; int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    Vector3d ap = p[i] - A;
                    double t = ll > 1e-18 ? Math.Max(0, Math.Min(1, (ap * ab) / ll)) : 0;
                    double dist = (ap - ab * t).SquareLength;
                    if (dist > worst) { worst = dist; at = i; }
                }
                if (worst <= 0.008 * 0.008 || at < 0) continue;
                keep[at] = true;
                todo.Push((a, at)); todo.Push((at, b));
            }
            var res = new List<Point3d>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) res.Add(p[i]);
            return res;
        }

        // ================================================================ checks

        private void CheckDepth(Cutter c, double minZ)
        {
            double depth = MaterialThickness - minZ;
            if (c.MaxSurfaceDepth > 0 && depth > c.MaxSurfaceDepth + 0.005)
                Error(Inv.F($"'{c.Name}' cannot reach {depth:0.0} mm below the top of the material: it goes {c.MaxSurfaceDepth:0.0} mm deep."));
            else if (minZ < 0.1 && MaterialThickness > c.MaxCuttingDepth + 0.005)
                Error(Inv.F($"'{c.Name}' cuts through {c.MaxCuttingDepth:0.0} mm at most (the suction bell stands 3.3 mm above the plate); the plate is {MaterialThickness:0.0} mm. Take a thinner plate, let the part stay in the plate (Hold), or take a longer cutter (CNC - Own Cutter)."));
            else if (depth > c.MaxCuttingDepth + 0.005)
                Warning(Inv.F($"'{c.Name}' has to go {depth:0.0} mm deep, {c.MaxCuttingDepth:0.0} mm is what it surely reaches with the suction bell above the material. Check the reach at the machine, or take thinner material."));
            double rpm = RpmOf(c);
            if (rpm < 7000 || rpm > 50000) Warning(Inv.F($"'{c.Name}': {rpm:0} RPM is outside the range of the RM-A (7000 to 50000)."));
        }

        private void CheckTable()
        {
            var b = BoundingBox.Empty;
            foreach (var op in Operations) foreach (var c in op.Cuts) b.Union(new BoundingBox(c.Points));
            if (b.Min.X < -0.005 || b.Min.Y < -0.005 || b.Max.X > ZundMill.TableX + 0.005 || b.Max.Y > ZundMill.TableY + 0.005)
                Warning(Inv.F($"The path leaves the table of the Zünd G3 ({ZundMill.TableX:0} x {ZundMill.TableY:0} mm): X {b.Min.X:0.0} to {b.Max.X:0.0}, Y {b.Min.Y:0.0} to {b.Max.Y:0.0}. Move the part."));
        }

        // ================================================================ simulation

        private void RunSimulation()
        {
            float[] s = Block();
            foreach (var op in Operations) CutAway(s, op);
            MaxLeft = Left(s, out MeanLeft, out double low);
            MaxGouge = Math.Max(0, -low);
            Report.Add(Inv.F($"Simulated: {MeanLeft:0.00} mm left on average, {MaxLeft:0.0} mm at most (inner corners the cutter does not reach), {MaxGouge:0.00} mm cut too deep at most (grid {_d:0.##} mm)."));
            if (Finish != null && MaxLeft > 0.6)
                Remark(Inv.F($"Up to {MaxLeft:0.0} mm stay in places where '{Finish.Name}' ({Finish.Diameter:0.#} mm) does not fit in: inner corners, foot of walls, narrow valleys. A smaller ball reaches further in."));
            BuildResult(s);
        }

        private float[] Block()
        {
            var s = new float[_h.Length];
            for (int i = 0; i < s.Length; i++) s[i] = (float)MaterialThickness;
            return s;
        }

        /// <summary>
        /// What stands over the part at most and on average, and how deep it was cut at most (low, negative).
        /// Every place is compared with the places around it, so that a wall half a place further does not count.
        /// </summary>
        private double Left(float[] s, out double mean, out double low)
        {
            double max = 0, sum = 0; long n = 0; low = 0;
            for (int y = 1; y + 1 < _ny; y++)
                for (int x = 1; x + 1 < _nx; x++)
                {
                    int i = y * _nx + x;
                    if (!(_h[i] > float.NegativeInfinity)) continue;
                    double hi = double.MinValue, lo = double.MaxValue;
                    bool inside = true;
                    for (int v = -1; v <= 1; v++)
                        for (int u = -1; u <= 1; u++)
                        {
                            double h = Math.Min(_h[i + v * _nx + u], MaterialThickness);
                            if (double.IsNegativeInfinity(h)) { inside = false; continue; }
                            if (h > hi) hi = h; if (h < lo) lo = h;
                        }
                    sum += s[i] - Math.Min(_h[i], MaterialThickness); n++;
                    if (s[i] - hi > max) max = s[i] - hi;
                    if (inside && s[i] - lo < low) low = s[i] - lo;
                }
            mean = n > 0 ? sum / n : 0;
            return max;
        }

        private void CutAway(float[] s, Operation op)
        {
            // the true tool on the grid (no half place more)
            double r = op.Cutter.Diameter / 2.0;
            int k = (int)Math.Ceiling(r / _d);
            var prof = new List<Offset>();
            for (int y = -k; y <= k; y++)
                for (int x = -k; x <= k; x++)
                {
                    double dist = Math.Sqrt(x * x + y * y) * _d;
                    if (dist > r) continue;
                    prof.Add(new Offset { X = x, Y = y, F = (float)(op.Cutter.Shape == Cutter.Ball ? r - Math.Sqrt(Math.Max(0, r * r - dist * dist)) : 0) });
                }
            var pa = prof.ToArray();
            foreach (var c in op.Cuts)
            {
                Cancel.Check();
                for (int i = 1; i < c.Points.Count; i++)
                {
                    Point3d a = c.Points[i - 1], b = c.Points[i];
                    int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Flat(a, b)) / _d));
                    for (int t = i == 1 ? 0 : 1; t <= n; t++)
                    {
                        double w = t / (double)n, z = a.Z + (b.Z - a.Z) * w;
                        int x = (int)Math.Round((a.X + (b.X - a.X) * w - _x0) / _d), y = (int)Math.Round((a.Y + (b.Y - a.Y) * w - _y0) / _d);
                        foreach (var o in pa)
                        {
                            int u = x + o.X, v = y + o.Y;
                            if (u < 0 || v < 0 || u >= _nx || v >= _ny) continue;
                            int q = v * _nx + u;
                            float h = (float)z + o.F;
                            if (h < s[q]) s[q] = h;
                        }
                    }
                }
            }
        }

        private void BuildResult(float[] s)
        {
            int f = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_nx * (double)_ny / 250000.0)));
            var m = new Mesh();
            int mx = (_nx - 1) / f + 1, my = (_ny - 1) / f + 1;
            var at = new int[mx * my];
            double x1 = Plate.X + Plate.Length, y1 = Plate.Y + Plate.Width;
            for (int y = 0; y < my; y++)
                for (int x = 0; x < mx; x++)
                {
                    double px = _x0 + x * f * _d, py = _y0 + y * f * _d;
                    bool on = px >= Plate.X - 1e-6 && px <= x1 + 1e-6 && py >= Plate.Y - 1e-6 && py <= y1 + 1e-6;
                    at[y * mx + x] = on ? m.Vertices.Add(px, py, Math.Max(0, s[y * f * _nx + x * f])) : -1;
                }
            for (int y = 0; y + 1 < my; y++)
                for (int x = 0; x + 1 < mx; x++)
                {
                    int a = at[y * mx + x], b = at[y * mx + x + 1], c = at[(y + 1) * mx + x + 1], d = at[(y + 1) * mx + x];
                    if (a >= 0 && b >= 0 && c >= 0 && d >= 0) m.Faces.AddFace(a, b, c, d);
                }
            // the rest of the plate around it
            double gx0 = Math.Max(Plate.X, _x0), gy0 = Math.Max(Plate.Y, _y0), gx1 = Math.Min(x1, _x0 + (mx - 1) * f * _d), gy1 = Math.Min(y1, _y0 + (my - 1) * f * _d), t = MaterialThickness;
            Action<double, double, double, double> top = (ax, ay, bx, by) =>
            {
                if (bx - ax < 1e-6 || by - ay < 1e-6) return;
                int i = m.Vertices.Add(ax, ay, t); m.Vertices.Add(bx, ay, t); m.Vertices.Add(bx, by, t); m.Vertices.Add(ax, by, t);
                m.Faces.AddFace(i, i + 1, i + 2, i + 3);
            };
            top(Plate.X, Plate.Y, x1, gy0); top(Plate.X, gy1, x1, y1); top(Plate.X, gy0, gx0, gy1); top(gx1, gy0, x1, gy1);
            m.Normals.ComputeNormals();
            Result = m;
        }

        // ================================================================ output

        /// <summary>Seconds, as the machine moves: it speeds up with AS2 and stops where the direction changes by more than 30 degrees.</summary>
        private double TimeOf(Operation op)
        {
            const double cutting = 2500, lifted = 10000; // mm/s2 at AS2 and AS4
            double t = 0; Point3d last = Point3d.Unset;
            foreach (var c in op.Cuts)
            {
                if (last.IsValid)
                {
                    double way = Math.Sqrt(Flat(last, c.Points[0])), full = Rapid * Rapid / lifted;
                    t += way < full ? 2 * Math.Sqrt(way / lifted) : way / Rapid + Rapid / lifted;
                }
                t += 1.0 + Math.Max(0, Up - c.Points[0].Z) / Plunge(op.Feed);
                double v = 0; Vector3d dir = Vector3d.Zero;
                for (int i = 1; i < c.Points.Count; i++)
                {
                    Vector3d d = c.Points[i] - c.Points[i - 1];
                    double l = d.Length;
                    if (l < 1e-9) continue;
                    d /= l;
                    if (dir * d < 0.866) v = 0;
                    double top = Math.Min(op.Feed, Math.Sqrt(v * v + 2 * cutting * l));
                    t += l / Math.Max(0.5, 0.5 * (v + top));
                    v = top; dir = d;
                }
                last = c.Points[c.Points.Count - 1];
            }
            return t;
        }

        private static double Plunge(double feed) => Math.Max(5.0, feed / 3.0);

        private void WriteTimes()
        {
            double sum = 0; Cutter now = null; int changes = 0;
            foreach (var op in Operations)
            {
                double t = TimeOf(op);
                if (now != null && now.Name != op.Cutter.Name) { changes++; t += 45; }
                now = op.Cutter;
                Report.Add(Inv.F($"{op.Name}: about {t / 60:0} min at {op.Feed:0} mm/s, {op.Rpm:0} RPM, {op.Cutter.Name}, {op.Cutter.BitText}."));
                sum += t;
            }
            Minutes = sum / 60;
            if (Minutes > 240) Remark(Inv.F($"About {Minutes / 60:0.0} hours. A thicker ball and a wider Stepover are much faster."));
            int lifts = Operations.Sum(o => o.Cuts.Count);
            TravelLength = 0;
            Point3d last = Point3d.Unset;
            foreach (var op in Operations)
                foreach (var c in op.Cuts) { if (last.IsValid) TravelLength += Math.Sqrt(Flat(last, c.Points[0])); last = c.Points[c.Points.Count - 1]; }
            Report.Add(Inv.F($"{lifts} cut(s), {TravelLength / 1000:0.0} m with the cutter lifted."));
            Report.Add(Inv.F($"In all about {Minutes:0} min, {changes} change(s) of the cutter. Estimated with the acceleration of the machine; measure the first job."));
        }

        private static int U(double mm) => (int)Math.Round(mm * 100.0);
        // the Zünd counts speeds in cm/s
        private static string V(double mmPerSecond) => Inv.N(Math.Max(0.1, Math.Min(100.0, mmPerSecond / 10.0)), 2);

        private void WritePlt()
        {
            // one program with the changes in it, if the machine can change every cutter by number
            var cutters = Operations.Select(o => o.Cutter.Name).Distinct().Count();
            bool numbered = Operations.All(o => o.Cutter.RouterBit > 0);
            var bits = Operations.GroupBy(o => o.Cutter.RouterBit).Where(g => g.Key > 0 && g.Select(o => o.Cutter.Name).Distinct().Count() > 1).ToList();
            if (bits.Count > 0) { Error("Both cutters have the same place in the magazine: the machine would mill with the wrong one. Set Magazine of CNC - Cutter to where each one really sits."); return; }
            if (Messages.Any(m => m.Key == 2)) return;
            if (cutters == 1 || numbered) Plt = Program(Operations);
            else Warning("Magazine is not set on every CNC - Cutter, so there is one file per cutter: change the cutter by hand between the files.");
            // one program per stretch with the same cutter
            var part = new List<Operation>();
            foreach (var op in Operations)
            {
                if (part.Count > 0 && part[0].Cutter.Name != op.Cutter.Name) { PltPerTool.Add(Program(part)); part = new List<Operation>(); }
                part.Add(op);
            }
            if (part.Count > 0) PltPerTool.Add(Program(part));
        }

        private string Program(List<Operation> ops)
        {
            var l = new List<string>();
            l.Add(Plate.Vacuum > 0 ? Inv.F($"PB2,1,{Math.Min(10, Plate.Vacuum)};") : "PB2,0;");
            l.Add("ZT1;MA;");
            double width = Plate.Y + Plate.Width; // the vacuum reaches from the edge of the table over the plate
            l.Add(Inv.F($"SV{U(width)};"));
            l.Add(Inv.F($"XX308,1,{Plate.Underlay};"));
            string zp = Inv.F($"ZP{U(MaterialThickness) + 100},{ZpOffset};");
            Cutter now = null; Point3d last = Point3d.Unset;
            foreach (var op in ops)
            {
                if (now == null || now.Name != op.Cutter.Name)
                {
                    if (last.IsValid) { l.Add(Inv.F($"PU{U(last.X)},{U(last.Y)};")); l.Add(zp); }
                    // the module first, then the bit: ZP, speed and suction bell belong to the chosen tool
                    l.Add(Inv.F($"SP{op.Cutter.ToolPosition};"));
                    if (op.Cutter.RouterBit > 0) l.Add(Inv.F($"XX351,{op.Cutter.RouterBit};"));
                    l.Add(zp);
                    l.Add(Inv.F($"XX306,{U(MaterialThickness + 3.3)};")); // suction bell 3.3 mm above the material
                    l.Add(Inv.F($"AS{Math.Max(1, Math.Min(4, AccelDown))},{Math.Max(1, Math.Min(4, AccelUp))};"));
                    l.Add(Inv.F($"XX150,{op.Rpm:0};"));
                    l.Add("MA;");
                }
                now = op.Cutter;
                l.Add("VS" + V(op.Feed) + "," + V(Rapid) + ";");
                l.Add("VU" + V(Rapid) + ";");
                foreach (var c in op.Cuts)
                {
                    if (last.IsValid) l.Add(Inv.F($"PU{U(last.X)},{U(last.Y)};"));
                    Point3d a = c.Points[0];
                    l.Add(Inv.F($"PU{U(a.X)},{U(a.Y)};"));
                    l.Add("VW" + V(Plunge(op.Feed)) + ";");
                    string before = Mill(a);
                    l.Add(before);
                    l.Add("VW" + V(op.Feed) + ";");
                    bool slow = false;
                    for (int i = 1; i < c.Points.Count; i++)
                    {
                        string m = Mill(c.Points[i]);
                        if (m == before) continue;
                        // steeper down than 30 degrees (from a ledge to the ground below): a third of the feed
                        double fall = c.Points[i - 1].Z - c.Points[i].Z;
                        bool steep = fall > 0.3 && fall > 0.577 * Math.Sqrt(Flat(c.Points[i], c.Points[i - 1]));
                        if (steep != slow) { l.Add("VW" + V(steep ? Plunge(op.Feed) : op.Feed) + ";"); slow = steep; }
                        l.Add(m);
                        before = m;
                    }
                    last = c.Points[c.Points.Count - 1];
                }
            }
            if (last.IsValid) l.Add(Inv.F($"PU{U(last.X)},{U(last.Y)};"));
            l.Add("ZT0;"); l.Add("PU250000,0;"); l.Add("XX308,0;"); l.Add("PB2,0;");
            return string.Join("\n", l);
        }

        private static string Mill(Point3d p) => Inv.F($"MW{U(p.X)},{U(p.Y)},{U(-p.Z)};");
    }
}
