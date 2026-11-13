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
    }
}
