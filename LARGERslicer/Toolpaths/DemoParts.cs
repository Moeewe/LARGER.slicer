using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// The demo parts of Quick Start, shared by the Grasshopper 1 and the Grasshopper 2 plug-in:
    /// five parts that stand side by side on a bed of 1000 x 1000 mm.
    /// </summary>
    public static class DemoParts
    {
        /// <summary>Example 1: a curved hull, 60 mm high.</summary>
        public static Brep Hull()
        {
            // a curved hull, 60 mm high: shows walls, ribs and bottom in one part
            var outer = new Arc(Plane.WorldXY, 260, Math.PI * 0.9).ToNurbsCurve();
            var inner = new Arc(Plane.WorldXY, 170, Math.PI * 0.9).ToNurbsCurve();
            inner.Reverse();
            var joined = Curve.JoinCurves(new Curve[] { outer, new LineCurve(outer.PointAtEnd, inner.PointAtStart), inner, new LineCurve(inner.PointAtEnd, outer.PointAtStart) });
            if (joined == null || joined.Length == 0) return null;
            // on the bed: every coordinate positive, as a machine with its origin in the corner needs it
            joined[0].Translate(new Vector3d(317, 100, 0)); // front row of the bed, left: x 70..577, y 100..360
            return Surface.CreateExtrusion(joined[0], new Vector3d(0, 0, 60)).ToBrep();
        }

        /// <summary>Non-planar demo: a round hull whose top edge follows a saddle surface, and that surface.</summary>
        public static void NonPlanar(Point3d c, out Brep part, out Brep guide)
        {
            const double r = 120; part = null; guide = null;
            Func<double, double, double> z = (x, y) => 70 + 22 * (x * x - y * y) / (r * r);
            var top = new List<Point3d>();
            for (int i = 0; i < 48; i++) { double a = i * Math.PI * 2 / 48; top.Add(new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z(r * Math.Cos(a), r * Math.Sin(a)))); }
            top.Add(top[0]);
            Curve topCurve = Curve.CreateInterpolatedCurve(top, 3, CurveKnotStyle.ChordPeriodic);
            Curve baseCurve = new Circle(new Plane(c, Vector3d.ZAxis), r).ToNurbsCurve();
            if (topCurve != null)
            {
                // seams at the same place, same direction
                double t; if (topCurve.ClosestPoint(baseCurve.PointAtStart + new Vector3d(0, 0, 70), out t)) topCurve.ChangeClosedCurveSeam(t);
                if (topCurve.TangentAtStart * baseCurve.TangentAtStart < 0) topCurve.Reverse();
                var loft = Brep.CreateFromLoft(new[] { baseCurve, topCurve }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
                if (loft != null && loft.Length > 0) part = loft[0];
            }
            var grid = new List<Point3d>();
            for (int i = 0; i < 7; i++) for (int j = 0; j < 7; j++) { double x = -1.35 * r + i * 0.45 * r, y = -1.35 * r + j * 0.45 * r; grid.Add(new Point3d(c.X + x, c.Y + y, z(x, y))); }
            var srf = NurbsSurface.CreateThroughPoints(grid, 7, 7, 3, 3, false, false);
            if (srf != null) guide = srf.ToBrep();
        }

        /// <summary>Multi-axis demo: a tube that grows along a bent axis, and that axis.</summary>
        public static void MultiAxis(Point3d c, out Brep part, out Curve guide)
        {
            part = null;
            guide = new Arc(c, Vector3d.ZAxis, c + new Vector3d(130, 0, 200)).ToNurbsCurve();
            var pipe = Brep.CreatePipe(guide, 70, false, PipeCapMode.None, true, 0.01, 0.01);
            if (pipe != null && pipe.Length > 0) part = pipe[0];
        }

        // The demo bed: 1000 x 1000 mm, origin in the front left corner. Front row: example 1 and 5.
        // Back row: examples 2, 3 and 4, with the same distance to the edges and to each other.
        public const double BedSize = 1000;

        /// <summary>Example 7: a vase whose +X side bulges out by "bulge" towards the top, for layers that
        /// follow the part with a vertical nozzle (Ginger One). Closed and capped.</summary>
        public static Brep Bulge(Point3d c, double radius, double bulge, double height)
        {
            var curves = new List<Curve>();
            for (int k = 0; k <= 16; k++)
            {
                double z = height * k / 16.0;
                double t = Math.Max(0, Math.Min(1, (z - 0.2 * height) / (0.8 * height)));
                double s = t * t * (3 - 2 * t);
                var pts = new List<Point3d>();
                for (int j = 0; j < 72; j++)
                {
                    double a = j * Math.PI * 2 / 72, cs = Math.Max(0, Math.Cos(a));
                    double r = radius + bulge * s * cs * cs;
                    pts.Add(new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z));
                }
                pts.Add(pts[0]); // close the ring, or the periodic curve leaves a gap of one step
                curves.Add(Curve.CreateInterpolatedCurve(pts, 3, CurveKnotStyle.ChordPeriodic));
            }
            var loft = Brep.CreateFromLoft(curves, Point3d.Unset, Point3d.Unset, LoftType.Normal, false);
            if (loft == null || loft.Length == 0) return null;
            return loft[0].CapPlanarHoles(0.01) ?? loft[0];
        }

        /// <summary>
        /// Example 7: the head printed on the Ginger One in 2024 ("UNIKAT HEAD V2"), the model of that
        /// print (Resources/Head.3dm, 138 x 117 x 177 mm, a closed polysurface cut flat at the neck), its
        /// neck centred on c. Its chin juts forward over the neck and its face is long, so the rings of
        /// "Follow the surface" dip under the chin by up to about 15 mm per lap, as the printed rings did.
        /// When the model cannot be read: a lofted stand-in of about the same size.
        /// </summary>
        public static Brep Head(Point3d c)
        {
            Brep head = BuiltInHead();
            if (head != null)
            {
                head = head.DuplicateBrep();
                head.Translate(new Vector3d(c));
                return head;
            }
            return HeadStandIn(c);
        }

        private static Brep _head;
        private static bool _headRead;

        /// <summary>The model of the printed head, standing on Z 0, its bounding box centred on the origin.</summary>
        private static Brep BuiltInHead()
        {
            lock (typeof(DemoParts))
            {
                if (_headRead) return _head;
                _headRead = true;
                try
                {
                    var asm = typeof(DemoParts).Assembly;
                    string resource = null;
                    foreach (var n in asm.GetManifestResourceNames()) if (n.EndsWith(".Head.3dm", StringComparison.OrdinalIgnoreCase)) { resource = n; break; }
                    if (resource == null) return null;
                    using (var stream = asm.GetManifestResourceStream(resource))
                    using (var memory = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(memory);
                        using (var f = Rhino.FileIO.File3dm.FromByteArray(memory.ToArray()))
                            foreach (var o in f.Objects)
                                if (o.Geometry is Brep b && b.IsValid) { _head = b.DuplicateBrep(); break; }
                    }
                }
                catch { _head = null; }
                return _head;
            }
        }

        /// <summary>A lofted stand-in for the head: a neck ring, the chin forward (-Y), a crown. About 118 x 134 x 171 mm.</summary>
        private static Brep HeadStandIn(Point3d c)
        {
            // z, half width (X), half depth (Y), shift towards the face (-Y)
            double[,] rings =
            {
                { 0, 30, 30, 0 }, { 22, 31, 32, 2 }, { 40, 37, 41, 6 }, { 58, 46, 53, 8 },
                { 80, 55, 63, 7 }, { 104, 59, 67, 4 }, { 128, 56, 63, 2 }, { 146, 47, 53, 1 }, { 158, 36, 41, 0 }, { 166, 20, 23, 0 },
            };
            var curves = new List<Curve>();
            for (int k = 0; k < rings.GetLength(0); k++)
            {
                var pl = new Plane(new Point3d(c.X, c.Y - rings[k, 3], c.Z + rings[k, 0]), Vector3d.XAxis, Vector3d.YAxis);
                curves.Add(new Ellipse(pl, rings[k, 1], rings[k, 2]).ToNurbsCurve());
            }
            var loft = Brep.CreateFromLoft(curves, Point3d.Unset, new Point3d(c.X, c.Y, c.Z + 171), LoftType.Normal, false);
            if (loft == null || loft.Length == 0) return null;
            return loft[0].CapPlanarHoles(0.01) ?? loft[0];
        }

        /// <summary>Example 8: a vase for a surface pattern, round, 160 mm high, waisted in the middle.</summary>
        public static Brep Vase(Point3d c)
        {
            double[,] rings = { { 0, 42 }, { 40, 46 }, { 80, 38 }, { 125, 44 }, { 160, 48 } };
            var curves = new List<Curve>();
            for (int k = 0; k < rings.GetLength(0); k++)
                curves.Add(new Circle(new Plane(new Point3d(c.X, c.Y, c.Z + rings[k, 0]), Vector3d.ZAxis), rings[k, 1]).ToNurbsCurve());
            var loft = Brep.CreateFromLoft(curves, Point3d.Unset, Point3d.Unset, LoftType.Normal, false);
            if (loft == null || loft.Length == 0) return null;
            return loft[0].CapPlanarHoles(0.01) ?? loft[0];
        }

        /// <summary>Example 4: a plate with a round opening, 220 x 220 x 50 mm.</summary>
        public static Brep Plate(Point3d c)
        {
            var outer = new Rectangle3d(new Plane(new Point3d(c.X - 110, c.Y - 110, 0), Vector3d.XAxis, Vector3d.YAxis), 220, 220).ToNurbsCurve();
            var hole = new Circle(new Plane(new Point3d(c.X, c.Y, 0), Vector3d.ZAxis), 50).ToNurbsCurve();
            var face = Brep.CreatePlanarBreps(new Curve[] { outer, hole }, 0.01);
            if (face == null || face.Length == 0) return null;
            return face[0].Faces[0].CreateExtrusion(new LineCurve(Point3d.Origin, new Point3d(0, 0, 50)), true);
        }

        /// <summary>
        /// Example 5: a block that leans 55 degrees towards -Y. Printed in flat layers its wall
        /// overhangs more than a bead can carry; with layers leaning 45 degrees the same way the
        /// wall is only 10 degrees off the build direction. Its foot edge lies along X, so the
        /// first leaning layer starts on a straight line on the bed.
        /// </summary>
        public static Brep Lean(Point3d c)
        {
            const double wx = 90, wy = 60, hgt = 150;
            double shift = hgt * Math.Tan(55 * Math.PI / 180);
            Func<double, double, double, Curve> rect = (cy, z, dummy) => new PolylineCurve(new[]
            {
                new Point3d(c.X - wx, cy - wy, z), new Point3d(c.X + wx, cy - wy, z), new Point3d(c.X + wx, cy + wy, z), new Point3d(c.X - wx, cy + wy, z), new Point3d(c.X - wx, cy - wy, z),
            });
            var loft = Brep.CreateFromLoft(new[] { rect(c.Y, 0, 0), rect(c.Y - shift, hgt, 0) }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
            if (loft == null || loft.Length == 0) return null;
            return loft[0].CapPlanarHoles(0.01) ?? loft[0];
        }

        /// <summary>CNC Quick Start: a topography of 240 x 160 mm, 14 mm high: hills, a ridge and a river.</summary>
        public static Mesh Topography()
        {
            var m = new Mesh();
            const int nx = 240, ny = 160;
            double lo = double.MaxValue, hi = double.MinValue;
            var h = new double[nx + 1, ny + 1];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    double x = i, y = j, river = y - 78 - 24 * Math.Sin(x / 38.0) - 6 * Math.Sin(x / 13.0);
                    double z = 5.5 * Math.Sin(x / 31.0 + 0.6) * Math.Cos(y / 36.0) + 2.6 * Math.Sin((x + 1.7 * y) / 21.0) + 1.0 * Math.Cos((2 * x - y) / 14.0);
                    z += 9 * Math.Exp(-((x - 180) * (x - 180) + (y - 122) * (y - 122)) / 900.0) + 7 * Math.Exp(-((x - 62) * (x - 62) + (y - 34) * (y - 34)) / 700.0);
                    z -= 8 * Math.Exp(-river * river / 150.0);
                    h[i, j] = z; lo = Math.Min(lo, z); hi = Math.Max(hi, z);
                }
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) m.Vertices.Add(i, j, 14.0 * (h[i, j] - lo) / (hi - lo));
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) m.Faces.AddFace(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i);
            m.Normals.ComputeNormals();
            return m;
        }

        /// <summary>
        /// CNC Quick Start: the first thing to mill. Five steps of 2 mm that rise to the right (X), 100 x 50 mm, the lowest step first.
        /// If the steps rise to the left or the heights are wrong, the machine counts Z in another way than the plug-in.
        /// </summary>
        public static Mesh TestStairs()
        {
            var m = new Mesh();
            Action<double, double, double, double> quad = (x0, x1, z0, z1) =>
            {
                int a = m.Vertices.Add(x0, 0, z0); m.Vertices.Add(x1, 0, z1); m.Vertices.Add(x1, 50, z1); m.Vertices.Add(x0, 50, z0);
                m.Faces.AddFace(a, a + 1, a + 2, a + 3);
            };
            for (int i = 0; i < 5; i++)
            {
                quad(20 * i, 20 * (i + 1), 2 + 2 * i, 2 + 2 * i);
                if (i > 0) quad(20 * i, 20 * i, 2 * i, 2 + 2 * i);
            }
            m.Vertices.CombineIdentical(true, true);
            m.Normals.ComputeNormals();
            return m;
        }

        /// <summary>
        /// Example for weaving: a sandwich panel of one closed line, 480 x 45 x 80 mm. The front is straight, the back runs in
        /// waves and nearly touches the front at four knots (centre lines 5 mm apart); the waves lean, so the knots move with the height.
        /// </summary>
        public static Mesh SandwichPanel(Point3d corner, double knot = 5.0)
        {
            const double L = 480, W = 40, H = 80; const int n = 240, nz = 20;
            var m = new Mesh();
            Action<double, double, double> add = (x, y, z) => m.Vertices.Add(corner.X + x, corner.Y + y, corner.Z + z);
            int ring = 0;
            for (int k = 0; k <= nz; k++)
            {
                double z = H * k / nz; int before = m.Vertices.Count;
                for (int i = 0; i <= n; i++) add(L * i / n, 0, z);
                for (int i = 1; i < 10; i++) add(L, W * i / 10.0, z);
                for (int i = n; i >= 0; i--)
                {
                    double x = L * i / n, edge = Math.Min(1, Math.Min(x, L - x) / 40);
                    edge = edge * edge * (3 - 2 * edge);
                    double wave = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (x - 0.35 * (z - 0.5 * H)) / 120.0 + Math.PI);   // 0 at the knots
                    add(x, W - edge * (W - knot) * (1 - wave), z);
                }
                for (int i = 9; i > 0; i--) add(0, W * i / 10.0, z);
                ring = m.Vertices.Count - before;
            }
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < ring; i++)
                {
                    int a = k * ring + i, b = k * ring + (i + 1) % ring;
                    m.Faces.AddFace(a, b, b + ring, a + ring);
                }
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }

        /// <summary>Example for reshaping: a rectangle 600 x 200 x 100 mm, printed lying as a vase; the print height is the depth of the part.</summary>
        public static Brep ReshapeBox(Point3d corner) => new Box(new BoundingBox(corner, corner + new Vector3d(600, 200, 100))).ToBrep();
    }
}
