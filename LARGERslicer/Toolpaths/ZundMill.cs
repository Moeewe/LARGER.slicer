using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using LARGERslicer.Types;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// 3D milling on the Zünd G3 as rows over a part (zigzag) and an outline that cuts the part free, with
    /// the PLT text of the machine. One core for CNC - Program of Grasshopper 1 and Grasshopper 2.
    ///
    /// Heights: the part stands on the table (geometry Z = material that remains, never negative). In the
    /// PLT Z counts down from the table: CNC Z = -geometry Z, in 1/100 mm. A positive CNC Z would cut the table.
    /// The tool stays down between two rows as long as the way lies on the part; where it would leave
    /// the part (a hole, a gap, another island) it is lifted.
    /// </summary>
    public sealed class ZundMill
    {
        public const double TableX = 2500.0, TableY = 1800.0, WorkingHeight = 50.0;

        // ---- inputs
        public Mesh Mesh;
        public Brep Brep;
        public double StepX = 1.0, StepY = 1.0, Margin, AngleDegrees;
        public bool StartLeft = true, AddOutline = true, MakePlt = true;
        public double FeedSpeed = 10.0, RapidSpeed = 50.0, MaterialThickness = 10.0, MaterialWidth;
        public int ZpOffset, Tool = 31, AccelDown = 2, AccelUp = 4, VacuumStrength, UnderlayThickness = 200;
        public Cutter Cutter;
        public double MaxCuttingDepth = 22.5, SpindleSpeed = 10000.0;
        public bool SpindleWired;
        public List<int> ToolList = new List<int>();
        public List<double> ToolSpindleSpeeds = new List<double>();

        // ---- results
        /// <summary>0 = remark, 1 = warning, 2 = error.</summary>
        public readonly List<KeyValuePair<int, string>> Messages = new List<KeyValuePair<int, string>>();
        public Curve Path;
        public List<Curve> SegmentCurves = new List<Curve>();
        public string Plt = "", Stats = "";

        /// <summary>One cut without a lift in it.</summary>
        public sealed class Cut
        {
            public List<Point3d> Points;
            /// <summary>Points on the way from the cut before (tool down).</summary>
            public List<Point3d> Link = new List<Point3d>();
            /// <summary>The tool is lifted on the way from the cut before.</summary>
            public bool Lift;
            public bool Outline;
            public int Tool;
        }

        public readonly List<Cut> Cuts = new List<Cut>();

        private void Remark(string s) => Messages.Add(new KeyValuePair<int, string>(0, s));
        private void Warning(string s) => Messages.Add(new KeyValuePair<int, string>(1, s));
        private void Error(string s) => Messages.Add(new KeyValuePair<int, string>(2, s));

        // ================================================================ solve

        public void Solve()
        {
            Prepare();
            BoundingBox box = Brep != null ? Brep.GetBoundingBox(true) : Mesh.GetBoundingBox(true);
            if (!box.IsValid) { Error("The geometry is not valid."); Stats = "ERROR: The geometry is not valid."; return; }

            Mesh part = PartMesh(Mesh, Brep);
            if (part == null || part.Faces.Count == 0) { Error("The geometry could not be meshed."); Stats = "ERROR: The geometry could not be meshed."; return; }
            PathsD footprint = Footprint(part);
            BoundingBox area = box;
            if (footprint.Count > 0)
            {
                RectD r = Clipper.GetBounds(footprint);
                area = new BoundingBox(new Point3d(r.left, r.top, box.Min.Z), new Point3d(r.right, r.bottom, box.Max.Z));
            }
            double footprintWidthY = area.Max.Y - area.Min.Y;
            // 0.01 mm inside the part, so that the first and the last row lie on it; the margin is the user's
            area.Inflate(Margin - 0.01, Margin - 0.01, 0.0);

            var surface = new Sampler(part, box.Min.Z, box.Max.Z);
            AddRows(surface, area, AngleDegrees * Math.PI / 180.0);

            double outlineOffset = Cutter != null && Cutter.Diameter > 0 ? Cutter.Diameter / 2.0 : Math.Max(StepX, StepY);
            if (AddOutline && Cuts.Count > 0) AddOutlines(footprint, box, outlineOffset);
            if (Cuts.Count == 0) Warning("No rows: the part lies outside the area, or the Margin is negative and larger than the part.");

            // ---- heights
            double maxZ = 0.0, minZ = double.MaxValue;
            foreach (var c in Cuts)
                foreach (var p in c.Points.Concat(c.Link)) { if (p.Z > maxZ) maxZ = p.Z; if (p.Z < minZ) minZ = p.Z; }
            if (minZ == double.MaxValue) minZ = MaterialThickness;
            bool intoTable = minZ < -0.005;
            if (intoTable)
                Error(Inv.F($"Minimum geometry Z ({minZ:F2} mm) is negative. This would result in positive CNC Z ({-minZ:F2} mm), which would cut into the table. Geometry Z coordinates must be positive (material remaining): move the part up. No PLT is written."));
            if (maxZ > MaterialThickness + 0.005)
                Warning(Inv.F($"Maximum geometry Z ({maxZ:F2} mm) exceeds material thickness ({MaterialThickness:F2} mm). This would try to leave more material than available. Adjust geometry Z coordinates or material thickness."));
            if (MaterialThickness > WorkingHeight)
                Warning(Inv.F($"Material thickness ({MaterialThickness:F2} mm) exceeds the working height of the Zünd G3 ({WorkingHeight:F0} mm)."));
            CheckDepth(minZ);
            CheckTable();
            AssignTools();

            // ---- geometry outputs
            foreach (var c in Cuts) if (c.Points.Count >= 2) SegmentCurves.Add(new PolylineCurve(c.Points));
            List<Point3d> path = Motion();
            if (path.Count >= 2) Path = new PolylineCurve(path);
            double length = Cuts.Sum(c => new Polyline(c.Points).Length);
            int lifts = Cuts.Count(c => c.Lift);
            Stats = Inv.F($"Boustrophedon{(AddOutline ? " + Outline" : "")} | Segments={Cuts.Count} | Points={path.Count} | Length={length:F2} mm | VS={FeedSpeed:0.###} cm/s ({FeedSpeed * 10:0} mm/s), VU={RapidSpeed:0.###} cm/s");
            if (Cuts.Count > 0) Stats += Inv.F($" | Deepest cut={MaterialThickness - minZ:F2} mm | Lifts={lifts}");
            if (Cuts.Any(c => c.Outline)) Stats += Inv.F($" | Outline offset={outlineOffset:F2} mm{(Cutter != null && Cutter.Diameter > 0 ? " (cutter radius)" : " (larger step, no cutter)")}");

            if (MakePlt && !intoTable)
                Plt = Text(minZ, MaterialWidth > 0.0 ? MaterialWidth * 100.0 : footprintWidthY * 100.0);
        }

        private void Prepare()
        {
            if (Cutter != null)
            {
                Tool = Cutter.ToolPosition;
                MaxCuttingDepth = Cutter.MaxCuttingDepth;
                // Spindle speed given at the Cutter Selector: used unless the Spindle Speed of the program is wired
                if (Cutter.SpindleSpeed > 0)
                {
                    if (SpindleWired) Remark(Inv.F($"The wired Spindle Speed ({SpindleSpeed:F0} RPM) is used, not the {Cutter.SpindleSpeed:F0} RPM of the Cutter Selector."));
                    else
                    {
                        SpindleSpeed = Cutter.SpindleSpeed;
                        Remark(Inv.F($"Spindle speed {SpindleSpeed:F0} RPM from the Cutter Selector (Spindle Speed is not wired here)."));
                    }
                }
            }

            // the depth limit is never corrected: it only counts for the checks
            if (MaxCuttingDepth <= 0.0)
                Error(Inv.F($"Max Cutting Depth must be greater than 0. Current value: {MaxCuttingDepth:F2} mm. Please correct the input value. The value will NOT be automatically corrected to prevent dangerous situations."));
            else if (MaxCuttingDepth < 1.0)
                Warning(Inv.F($"Max Cutting Depth ({MaxCuttingDepth:F2} mm) is very small. Minimum recommended: 1.0 mm. Please verify this is correct for your cutter."));

            StepX = Step(StepX, "Step X (dx)");
            StepY = Step(StepY, "Step Y (dy)");
            if (!Finite(Margin)) Margin = 0.0;
            if (!Finite(AngleDegrees)) AngleDegrees = 0.0;
            FeedSpeed = Finite(FeedSpeed) ? Math.Max(FeedSpeed, 0.1) : 0.1;
            RapidSpeed = Finite(RapidSpeed) ? Math.Max(RapidSpeed, 0.1) : 0.1;
            MaterialThickness = Finite(MaterialThickness) ? Math.Max(MaterialThickness, 0.1) : 0.1;
            SpindleSpeed = Finite(SpindleSpeed) ? Math.Max(SpindleSpeed, 0.0) : 0.0;
            AccelDown = Math.Max(1, Math.Min(4, AccelDown));
            AccelUp = Math.Max(1, Math.Min(4, AccelUp));
            VacuumStrength = Math.Max(0, Math.Min(10, VacuumStrength));
            if (!IsTool(Tool))
            {
                Warning("Tool must be 11, 21, or 31. Using default 31.");
                Tool = 31;
            }
        }

        private double Step(double step, string name)
        {
            if (!Finite(step) || step <= 0.0)
            {
                Error(Inv.F($"{name} must be greater than 0. Current value: {step}. Using minimum value 0.1 mm."));
                return 0.1;
            }
            if (step < 0.1)
            {
                Warning(Inv.F($"{name} is very small ({step:F3} mm). This may generate excessive points and slow down processing. Minimum recommended: 0.1 mm."));
                return 0.1;
            }
            return step;
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        private static bool IsTool(int t) => t == 11 || t == 21 || t == 31;

        // ================================================================ rows

        private void AddRows(Sampler surface, BoundingBox area, double angle)
        {
            if (area.Max.X < area.Min.X || area.Max.Y < area.Min.Y) return;
            double cx = area.Center.X, cy = area.Center.Y, cos = Math.Cos(angle), sin = Math.Sin(angle);
            double w = area.Max.X - area.Min.X, h = area.Max.Y - area.Min.Y;
            if (Math.Abs(angle) > 1e-6)
            {
                // the turned rows have to cover the whole part
                double w2 = w * Math.Abs(cos) + h * Math.Abs(sin), h2 = w * Math.Abs(sin) + h * Math.Abs(cos);
                w = w2; h = h2;
            }
            List<double> us = Stations(-w / 2.0, w / 2.0, StepX), vs = Stations(-h / 2.0, h / 2.0, StepY);
            Point3d At(double u, double v) => new Point3d(cx + u * cos - v * sin, cy + u * sin + v * cos, 0.0);

            var rows = new List<List<Point3d>>[vs.Count];
            System.Threading.Tasks.Parallel.For(0, vs.Count, row =>
            {
                bool forward = StartLeft == (row % 2 == 0);
                rows[row] = Row(surface, forward ? us : Enumerable.Reverse(us).ToList(), u => At(u, vs[row]));
                if (rows[row].Count > 0) return;
                // a corner narrower than the step
                var fine = Stations(-w / 2.0, w / 2.0, Math.Max(0.2, StepX / 8.0));
                if (!forward) fine.Reverse();
                rows[row] = Row(surface, fine, u => At(u, vs[row]));
            });

            foreach (var row in rows)
                foreach (var run in row)
                {
                    var cut = new Cut { Points = run };
                    if (Cuts.Count > 0) Connect(surface, Cuts[Cuts.Count - 1], cut);
                    Cuts.Add(cut);
                }
        }

        // From a to b in steps, with b itself: a last strip narrower than a step is not left standing
        private static List<double> Stations(double a, double b, double step)
        {
            var list = new List<double>();
            int n = (int)Math.Floor((b - a) / step + 1e-9);
            for (int i = 0; i <= n; i++) list.Add(a + i * step);
            if (b - list[list.Count - 1] > 0.005) list.Add(b);
            return list;
        }

        // The runs of one row that lie on the part, each from edge to edge
        private static List<List<Point3d>> Row(Sampler surface, List<double> us, Func<double, Point3d> at)
        {
            var runs = new List<List<Point3d>>();
            List<Point3d> run = null;
            Point3d before = Point3d.Unset;
            bool insideBefore = false;
            for (int i = 0; i < us.Count; i++)
            {
                Point3d p = at(us[i]);
                double? z = surface.Z(p.X, p.Y);
                if (z.HasValue)
                {
                    p.Z = z.Value;
                    if (!insideBefore)
                    {
                        run = new List<Point3d>();
                        if (i > 0) Add(run, Edge(surface, p, before));
                    }
                    if (run.Count > 0) Refine(surface, run, run[run.Count - 1], p, 0);
                    Add(run, p);
                }
                else if (insideBefore)
                {
                    Point3d edge = Edge(surface, before, p);
                    Refine(surface, run, before, edge, 0);
                    Add(run, edge);
                    if (run.Count >= 2) runs.Add(run);
                    run = null;
                }
                insideBefore = z.HasValue;
                before = p;
            }
            if (run != null && run.Count >= 2) runs.Add(run);
            return runs;
        }

        private static void Add(List<Point3d> run, Point3d p)
        {
            if (run.Count == 0 || run[run.Count - 1].DistanceTo(p) > 1e-4) run.Add(p);
        }

        // The last point on the part on the way from a point on it to a point beside it
        private static Point3d Edge(Sampler surface, Point3d inside, Point3d outside)
        {
            for (int i = 0; i < 40; i++)
            {
                double dx = outside.X - inside.X, dy = outside.Y - inside.Y;
                if (dx * dx + dy * dy < 1e-6) break; // 0.001 mm
                var mid = new Point3d((inside.X + outside.X) / 2.0, (inside.Y + outside.Y) / 2.0, 0.0);
                double? z = surface.Z(mid.X, mid.Y);
                if (z.HasValue) inside = new Point3d(mid.X, mid.Y, z.Value);
                else outside = mid;
            }
            return inside;
        }

        // Points between two samples where the straight line would leave the surface by more than 0.05 mm
        // (the edge of a pocket between two samples would be cut off otherwise)
        private static void Refine(Sampler surface, List<Point3d> run, Point3d a, Point3d b, int depth)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            if (depth > 12 || length < 0.02) return;
            var mid = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0.0);
            double? z = surface.Z(mid.X, mid.Y);
            if (!z.HasValue) return; // a slit narrower than the step: stay up
            mid.Z = z.Value;
            if (Math.Abs(mid.Z - (a.Z + b.Z) / 2.0) <= 0.05 && (depth > 0 || Math.Abs(a.Z - b.Z) <= length)) return;
            Refine(surface, run, a, mid, depth + 1);
            Add(run, mid);
            Refine(surface, run, mid, b, depth + 1);
        }

        // The way to the next cut: tool down on the part, lifted where it would leave the part
        private void Connect(Sampler surface, Cut from, Cut to)
        {
            Point3d a = from.Points[from.Points.Count - 1], b = to.Points[0];
            double dx = b.X - a.X, dy = b.Y - a.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max((int)Math.Ceiling(dist / Math.Min(Math.Min(StepX, StepY) * 0.5, 1.0)), 1);
            var samples = new List<Point3d>();
            int off = 0;
            for (int t = 1; t < steps; t++)
            {
                double x = a.X + dx * t / steps, y = a.Y + dy * t / steps;
                double? z = surface.Z(x, y);
                if (!z.HasValue) off++;
                samples.Add(new Point3d(x, y, z ?? double.NaN));
            }
            // round a hollow edge the next row starts close by; everything else is a gap
            if (off * dist / steps > 2.0 * Math.Max(StepX, StepY)) { to.Lift = true; return; }
            for (int i = 0; i < samples.Count; i++)
            {
                if (!double.IsNaN(samples[i].Z)) { to.Link.Add(samples[i]); continue; }
                // beside the part: not deeper than the part before and after
                double up = a.Z;
                for (int k = i; k >= 0; k--) if (!double.IsNaN(samples[k].Z)) { up = samples[k].Z; break; }
                double next = b.Z;
                for (int k = i; k < samples.Count; k++) if (!double.IsNaN(samples[k].Z)) { next = samples[k].Z; break; }
                to.Link.Add(new Point3d(samples[i].X, samples[i].Y, Math.Max(up, next)));
            }
            // a step in height between two samples is never cut off: rise first, sink last
            var full = new List<Point3d> { a };
            full.AddRange(to.Link);
            full.Add(b);
            var safe = new List<Point3d>();
            for (int i = 1; i < full.Count; i++)
            {
                Point3d p = full[i - 1], q = full[i];
                double run = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                if (Math.Abs(q.Z - p.Z) > run + 0.05)
                    safe.Add(q.Z > p.Z ? new Point3d(p.X, p.Y, q.Z) : new Point3d(q.X, q.Y, p.Z));
                if (i < full.Count - 1) safe.Add(q);
            }
            to.Link = safe;
        }

        // ================================================================ outline

        /// <summary>The outline of a dense mesh: of an open one without overhangs its own edge, else the outline seen from above; null = not sure.</summary>
        private static PathsD DenseFootprint(Mesh mesh)
        {
            var found = new PathsD();
            if (!mesh.IsClosed)
            {
                // the same side up everywhere: the edge of the mesh is the outline
                int up = 0, down = 0;
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    MeshFace f = mesh.Faces[i];
                    Point3d p = mesh.Vertices.Point3dAt(f.A), q = mesh.Vertices.Point3dAt(f.B), r = mesh.Vertices.Point3dAt(f.C);
                    double area = (q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y);
                    if (area > 1e-9) up++; else if (area < -1e-9) down++;
                }
                if (up > 0 && down > 0) return null;
                Polyline[] edges = mesh.GetNakedEdges();
                if (edges == null) return null;
                foreach (var e in edges) { if (!e.IsClosed) return null; found.Add(new PathD(e.Take(e.Count - 1).Select(p => new PointD(p.X, p.Y)))); }
                found = Clipper.Union(found, new PathsD(), FillRule.EvenOdd, 4);
            }
            else
            {
                Polyline[] lines = mesh.GetOutlines(Plane.WorldXY);
                if (lines == null || lines.Length == 0) return null;
                foreach (var e in lines) found.Add(new PathD(e.Take(e.IsClosed ? e.Count - 1 : e.Count).Select(p => new PointD(p.X, p.Y))));
                found = Clipper.Union(found, new PathsD(), FillRule.EvenOdd, 4);
                RectD r = Clipper.GetBounds(found); BoundingBox b = mesh.GetBoundingBox(true);
                if (Math.Abs(r.left - b.Min.X) + Math.Abs(r.right - b.Max.X) + Math.Abs(r.top - b.Min.Y) + Math.Abs(r.bottom - b.Max.Y) > 0.4) return null;
            }
            var clean = new PathsD();
            foreach (var p in found) if (p.Count >= 3 && Math.Abs(Clipper.Area(p)) > 0.5) clean.Add(p);
            return clean.Count > 0 ? clean : null;
        }

        /// <summary>The part seen from above: its outer outlines (counter-clockwise) and the holes through it (clockwise).</summary>
        internal static PathsD Footprint(Mesh mesh)
        {
            if (mesh.Faces.Count > 40000)
            {
                PathsD quick = DenseFootprint(mesh);
                if (quick != null) return quick;
            }
            var batches = new List<PathsD>();
            var tri = new PathsD();
            void Triangle(Point3d a, Point3d b, Point3d c)
            {
                double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
                if (Math.Abs(area) < 1e-6) return; // stands upright
                tri.Add(area > 0 ? new PathD { new PointD(a.X, a.Y), new PointD(b.X, b.Y), new PointD(c.X, c.Y) } : new PathD { new PointD(a.X, a.Y), new PointD(c.X, c.Y), new PointD(b.X, b.Y) });
                if (tri.Count >= 1500) { batches.Add(Clipper.Union(tri, new PathsD(), FillRule.NonZero, 4)); tri = new PathsD(); }
            }
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                MeshFace f = mesh.Faces[i];
                Point3d a = mesh.Vertices.Point3dAt(f.A), b = mesh.Vertices.Point3dAt(f.B), c = mesh.Vertices.Point3dAt(f.C);
                Triangle(a, b, c);
                if (f.IsQuad) Triangle(a, c, mesh.Vertices.Point3dAt(f.D));
            }
            if (tri.Count > 0) batches.Add(Clipper.Union(tri, new PathsD(), FillRule.NonZero, 4));
            while (batches.Count > 1)
            {
                var next = new List<PathsD>();
                for (int i = 0; i < batches.Count; i += 2)
                    next.Add(i + 1 < batches.Count ? Clipper.Union(batches[i], batches[i + 1], FillRule.NonZero, 4) : batches[i]);
                batches = next;
            }
            if (batches.Count == 0) return new PathsD();
            // close cracks of rounding between faces, drop crumbs
            PathsD all = Clipper.InflatePaths(Clipper.InflatePaths(batches[0], 0.005, JoinType.Miter, EndType.Polygon, 2.0, 4), -0.005, JoinType.Miter, EndType.Polygon, 2.0, 4);
            var clean = new PathsD();
            foreach (var p in all) if (p.Count >= 3 && Math.Abs(Clipper.Area(p)) > 0.5) clean.Add(p);
            return clean;
        }

        private void AddOutlines(PathsD footprint, BoundingBox box, double offset)
        {
            var outer = new PathsD();
            int holes = 0;
            foreach (var p in footprint) { if (Clipper.Area(p) > 0) outer.Add(p); else holes++; }
            PathsD loops = outer.Count == 0 ? null : Clipper.InflatePaths(outer, offset, JoinType.Miter, EndType.Polygon, 2.0, 4);
            var list = new List<List<Point3d>>();
            if (loops != null)
                foreach (var p in loops)
                {
                    if (Clipper.Area(p) <= 0) continue;
                    PathD s = Clipper.SimplifyPath(p, 0.003, true);
                    if (s.Count < 3) s = p;
                    list.Add(s.Select(q => new Point3d(q.x, q.y, box.Min.Z)).ToList());
                }
            if (list.Count == 0)
            {
                Remark("The outline of the part in plan could not be found: the outline runs around its bounding box.");
                double x0 = box.Min.X - offset, x1 = box.Max.X + offset, y0 = box.Min.Y - offset, y1 = box.Max.Y + offset, z = box.Min.Z;
                list.Add(new List<Point3d> { new Point3d(x0, y0, z), new Point3d(x1, y0, z), new Point3d(x1, y1, z), new Point3d(x0, y1, z) });
            }
            if (holes > 0) Remark(Inv.F($"{holes} hole(s) through the part: the outline does not cut them out."));
            while (list.Count > 0)
            {
                // the loop nearest to the tool, from its nearest point; the tool is lifted on the way there
                Point3d tool = Cuts[Cuts.Count - 1].Points[Cuts[Cuts.Count - 1].Points.Count - 1];
                int bestLoop = 0, bestStart = 0;
                double best = double.MaxValue;
                for (int k = 0; k < list.Count; k++)
                    for (int i = 0; i < list[k].Count; i++)
                    {
                        double d = Math.Pow(list[k][i].X - tool.X, 2) + Math.Pow(list[k][i].Y - tool.Y, 2);
                        if (d < best) { best = d; bestLoop = k; bestStart = i; }
                    }
                List<Point3d> loop = list[bestLoop];
                list.RemoveAt(bestLoop);
                var pts = new List<Point3d>();
                for (int i = 0; i <= loop.Count; i++) pts.Add(loop[(bestStart + i) % loop.Count]);
                Cuts.Add(new Cut { Points = pts, Lift = true, Outline = true });
            }
        }

        // ================================================================ checks

        private void CheckDepth(double minZ)
        {
            double depth = MaterialThickness - minZ;
            bool through = minZ < 0.1;
            if (Cutter != null)
            {
                if (depth > Cutter.MaxCuttingDepth && depth <= Cutter.MaxSurfaceDepth)
                    Warning(Inv.F($"Cutter '{Cutter.Name}' cutting depth ({depth:F2} mm) exceeds through-cutting limit ({Cutter.MaxCuttingDepth:F2} mm) but is within surface cutting limit ({Cutter.MaxSurfaceDepth:F2} mm). This is a surface cut, not a through-cut. Ensure material is not fully cut through."));
                if (depth > Cutter.MaxSurfaceDepth)
                    Error(Inv.F($"Cutter '{Cutter.Name}' cannot cut to depth of {depth:F2} mm. Maximum surface cutting depth: {Cutter.MaxSurfaceDepth:F2} mm. Cutting depth exceeds limit by {depth - Cutter.MaxSurfaceDepth:F2} mm. Please use a different cutter or adjust geometry Z coordinates."));
                if (through && MaterialThickness > Cutter.MaxCuttingDepth)
                    Error(Inv.F($"Through-cutting detected (minGeometryZ = {minZ:F2} mm). Cutter '{Cutter.Name}' cannot cut through material thickness of {MaterialThickness:F2} mm. Maximum through-cutting depth: {Cutter.MaxCuttingDepth:F2} mm (limited by Saugglocke position: 3.3mm above material surface). Material thickness exceeds limit by {MaterialThickness - Cutter.MaxCuttingDepth:F2} mm. Please use a different cutter or reduce material thickness."));
            }
            else
            {
                if (through && MaterialThickness > MaxCuttingDepth)
                    Error(Inv.F($"Through-cutting detected (minGeometryZ = {minZ:F2} mm). Tool {Tool} cannot cut through material thickness of {MaterialThickness:F2} mm. Maximum through-cutting depth: {MaxCuttingDepth:F2} mm (limited by Saugglocke position: 3.3mm above material surface). Material thickness exceeds limit by {MaterialThickness - MaxCuttingDepth:F2} mm. Please adjust Max Cutting Depth parameter or reduce material thickness."));
                else if (depth > MaxCuttingDepth)
                    Error(Inv.F($"Tool {Tool} cannot cut to depth of {depth:F2} mm. Maximum cutting depth: {MaxCuttingDepth:F2} mm. Cutting depth exceeds limit by {depth - MaxCuttingDepth:F2} mm. Please adjust Max Cutting Depth parameter or adjust geometry Z coordinates."));
                else if (MaterialThickness > MaxCuttingDepth)
                    Warning(Inv.F($"The material ({MaterialThickness:F2} mm) is thicker than Tool {Tool} can cut through ({MaxCuttingDepth:F2} mm). The deepest cut here is {depth:F2} mm."));
            }
            double extraction = depth + 3.3;
            if (extraction > 50.0)
                Warning(Inv.F($"Extraction height ({extraction:F2} mm) is very large. This indicates a very deep cut (cutting depth: {depth:F2} mm). Ensure the Saugglocke can be positioned correctly at this height."));
        }

        private void CheckTable()
        {
            if (Cuts.Count == 0) return;
            var b = new BoundingBox(Cuts.SelectMany(c => c.Points));
            if (b.Min.X < -0.005 || b.Min.Y < -0.005 || b.Max.X > TableX + 0.005 || b.Max.Y > TableY + 0.005)
                Warning(Inv.F($"The path leaves the table of the Zünd G3 ({TableX:F0} x {TableY:F0} mm): X {b.Min.X:F1} to {b.Max.X:F1}, Y {b.Min.Y:F1} to {b.Max.Y:F1}. Move the part; leave room for the outline."));
        }

        private void AssignTools()
        {
            bool byList = ToolList.Count > 0 && ToolList.Count == Cuts.Count;
            if (ToolList.Count > 0 && !byList)
                Warning($"Tool List count ({ToolList.Count}) does not match segment count ({Cuts.Count}). Using single Tool parameter for all segments.");
            for (int i = 0; i < Cuts.Count; i++)
            {
                Cuts[i].Tool = byList ? ToolList[i] : Tool;
                if (byList && !IsTool(Cuts[i].Tool))
                {
                    Warning($"Tool at index {i} must be 11, 21, or 31. Using default tool {Tool}.");
                    Cuts[i].Tool = Tool;
                }
                if (i > 0 && Cuts[i].Tool != Cuts[i - 1].Tool) Cuts[i].Lift = true;
            }
        }

        // ================================================================ output

        // The whole motion of the PLT, with its lifts (as ZP: material thickness + 1 mm above the table)
        private List<Point3d> Motion()
        {
            var path = new List<Point3d>();
            double up = MaterialThickness + 1.0;
            foreach (var c in Cuts)
            {
                if (path.Count > 0 && c.Lift)
                {
                    Point3d from = path[path.Count - 1], to = c.Points[0];
                    path.Add(new Point3d(from.X, from.Y, up));
                    path.Add(new Point3d(to.X, to.Y, up));
                }
                else if (path.Count > 0) path.AddRange(c.Link);
                path.AddRange(c.Points);
            }
            return path;
        }

        // mm -> 1/100 mm (Zünd units)
        private static int U(double mm) => (int)Math.Round(mm * 100.0);
        private static string PenUp(Point3d p) => Inv.F($"PU{U(p.X)},{U(p.Y)};");
        private static string Mill(Point3d p) => Inv.F($"MW{U(p.X)},{U(p.Y)},{U(-p.Z)};");

        private double Rpm(int tool)
        {
            int i = tool == 11 ? 0 : tool == 21 ? 1 : 2;
            return i < ToolSpindleSpeeds.Count && Finite(ToolSpindleSpeeds[i]) && ToolSpindleSpeeds[i] > 0 ? ToolSpindleSpeeds[i] : SpindleSpeed;
        }

        private string Text(double minZ, double vacuumWidth)
        {
            // Order and spelling after the HP-GL manual of the Zünd G3 (firmware 1.77): first the module (SP) and
            // the bit (XX351), because ZP, the speeds and the suction bell belong to the chosen tool; the first
            // parameter follows the command without a comma (AS2,4 not AS,2,4); speeds count in cm/s.
            var lines = new List<string>();
            lines.Add(VacuumStrength > 0 ? Inv.F($"PB2,1,{VacuumStrength};") : "PB2,0;"); // vacuum on with level, or off
            lines.Add("ZT1;MA;");                              // tool down, absolute coordinates
            lines.Add(Inv.F($"XX308,1,{UnderlayThickness};")); // additional underlay support on
            int tool = Cuts.Count > 0 ? Cuts[0].Tool : Tool;
            lines.Add(Inv.F($"SP{tool};"));                    // module with the router: 11 left, 21 middle, 31 right
            if (Cutter != null && Cutter.RouterBit > 0) lines.Add(Inv.F($"XX351,{Cutter.RouterBit};")); // router bit: 11-18 magazine, 1-10 by hand
            lines.Add(Inv.F($"VS{FeedSpeed:G6};"));            // tool down, in cm/s
            lines.Add(Inv.F($"VW{FeedSpeed:G6};"));            // the same for the 3D moves (MW)
            lines.Add(Inv.F($"VU{RapidSpeed:G6};"));           // tool up, in cm/s
            lines.Add("");
            if (vacuumWidth > 0) lines.Add(Inv.F($"SV{(int)Math.Round(vacuumWidth)};")); // width of the vacuum zone
            string bell = Inv.F($"XX306,{U(MaterialThickness + 3.3)};"); // suction bell 3.3 mm (its brush) above the material
            lines.Add(bell);
            lines.Add(Inv.F($"AS{AccelDown},{AccelUp};"));     // acceleration cutting, rapid

            if (Cuts.Count > 0)
            {
                string zp = Inv.F($"ZP{U(MaterialThickness) + 100},{ZpOffset};"); // retracted: 1 mm above the material; offset for cutting through
                lines.Add(zp);
                lines.Add(Inv.F($"XX150,{Rpm(tool):F0};"));    // spindle speed
                Point3d last = Cuts[0].Points[0];
                lines.Add(PenUp(last));
                for (int i = 0; i < Cuts.Count; i++)
                {
                    Cut c = Cuts[i];
                    if (i > 0)
                    {
                        if (c.Lift) lines.Add(PenUp(last));    // lift where the cut before ends
                        if (c.Tool != tool)
                        {
                            lines.Add(zp);
                            tool = c.Tool;                     // the other module (XX220 would take the machine offline)
                            lines.Add(Inv.F($"SP{tool};"));
                            lines.Add(zp);
                            lines.Add(bell);
                            lines.Add(Inv.F($"XX150,{Rpm(tool):F0};"));
                        }
                        if (c.Lift) lines.Add(PenUp(c.Points[0]));
                        else foreach (var p in c.Link) lines.Add(Mill(p));
                    }
                    foreach (var p in c.Points)
                    {
                        string m = Mill(p);
                        if (lines[lines.Count - 1] != m) lines.Add(m);
                    }
                    last = c.Points[c.Points.Count - 1];
                }
                lines.Add(PenUp(last));
            }
            lines.Add("ZT0;");          // retract the tool
            lines.Add("PU250000,0;");   // park
            lines.Add("XX308,0;");
            lines.Add("PB2,0;");
            return string.Join("\n", lines);
        }

        // ================================================================ height of the part

        /// <summary>The part as one mesh. A Brep is meshed to 0.01 mm, the unit of the machine.</summary>
        internal static Mesh PartMesh(Mesh mesh, Brep brep)
        {
            if (brep == null) return mesh;
            var mp = new MeshingParameters { Tolerance = 0.01, MinimumTolerance = 0.005, RefineGrid = true, SimplePlanes = true, JaggedSeams = false, ClosedObjectPostProcess = brep.IsSolid };
            Mesh[] ms = Mesh.CreateFromBrep(brep, mp);
            if (ms == null || ms.Length == 0) ms = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
            if (ms == null) return null;
            var all = new Mesh();
            foreach (var m in ms) if (m != null) all.Append(m);
            return all;
        }

        /// <summary>The highest point of the part over a point in plan.</summary>
        private sealed class Sampler
        {
            private readonly Mesh _mesh;
            private readonly double _zMin, _zMax;

            public Sampler(Mesh mesh, double zMin, double zMax) { _mesh = mesh; _zMin = zMin; _zMax = zMax; }

            /// <summary>
            /// Null beside the part. Right on an edge a ray can miss the top face and only meet the bottom or an
            /// upright wall: the highest of the point and four points 0.008 mm beside it counts.
            /// </summary>
            public double? Z(double x, double y)
            {
                double? z = Ray(x, y);
                if (!z.HasValue) return null;
                const double d = 0.008;
                return Math.Max(z.Value, Math.Max(Math.Max(Ray(x - d, y - d) ?? z.Value, Ray(x + d, y - d) ?? z.Value), Math.Max(Ray(x - d, y + d) ?? z.Value, Ray(x + d, y + d) ?? z.Value)));
            }

            private double? Ray(double x, double y)
            {
                var line = new Line(new Point3d(x, y, _zMax + 1.0), new Point3d(x, y, _zMin - 1.0));
                double best = double.MinValue;
#if NET48
                int[] ids;
                var hits = Intersection.MeshLine(_mesh, line, out ids);
#else
                var hits = Intersection.MeshLine(_mesh, line);
#endif
                if (hits != null) foreach (var p in hits) if (p.Z > best) best = p.Z;
                return best == double.MinValue ? (double?)null : best;
            }
        }
    }
}
