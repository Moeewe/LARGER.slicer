using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Start and end feature for angled printing (CEAD "Angled printing (45 deg) design process":
    /// "add a start and end feature to add some extra layers before the start and after the end of
    /// the print, which can be milled away afterwards; make sure the start feature is +/- 5 layers
    /// thick"; the boat hull paper: "a separate start surface allowed the 45-degree build to commence").
    ///
    /// Start: layers in front of the part's first layer. Each is the first layer moved back by whole
    /// layer heights and filled down to the bed in its own plane, so the feature stands on the bed,
    /// every layer of it lies on the one before (it grows towards the bed by one layer height
    /// divided by the slope per layer) and the part's first layer lies on a full layer.
    /// End: copies of the last layer on top of it, one layer height apart.
    /// Both are added as layers, so everything after Slice treats them like the part; they are
    /// sacrificial and milled away.
    /// </summary>
    public static class StartEndFeature
    {
        /// <summary>
        /// The start feature: startLayers layers in front of the part's first layer, one layer height
        /// apart. Each is the union of
        ///  - a band in front of the edge where the leaning layers begin, from the bed up to twice the
        ///    feature length in front of it (for parts whose first layer is a small corner), and
        ///  - the first layer moved back by whole layer heights and filled down to the bed in its own
        ///    plane (for parts whose first layer is a full face leaning against the bed),
        /// cut off at the bed. So the feature stands on the bed and every layer lies on the one before.
        /// Done layer by layer, without joining solids. Flat layers need none.
        /// </summary>
        public static List<SliceLayer> AddStart(List<SliceLayer> layers, int startLayers, double bedZ, List<string> log)
        {
            if (layers == null || layers.Count == 0 || startLayers <= 0) return layers;
            if (layers[0].Warp != null) return layers;
            var first = layers[0];
            Plane p0 = first.Plane;
            Vector3d n = p0.Normal; n.Unitize();
            if (Math.Abs(n.Z) > 0.999) return layers;
            double h = layers.Count > 1 ? layers[1].Height : first.Height;
            Vector3d front = new Vector3d(-n.X, -n.Y, 0); front.Unitize();
            Vector3d side = Vector3d.CrossProduct(Vector3d.ZAxis, front); side.Unitize();
            double sin = Math.Sqrt(n.X * n.X + n.Y * n.Y);
            double L = startLayers * h / Math.Max(0.05, sin);

            var pts = first.Contours.SelectMany(c => c).ToList();
            if (pts.Count == 0) return layers;
            var early = layers.Take(Math.Min(layers.Count, startLayers + 1)).SelectMany(l => l.Contours).SelectMany(c => c).ToList();
            double t0 = early.Min(q => (Vector3d)q * side), t1 = early.Max(q => (Vector3d)q * side);
            if (t1 - t0 < 1) { t0 -= 5; t1 += 5; }
            var low = pts.Where(q => q.Z < bedZ + 1.0).ToList();
            bool onBed = low.Count > 0;
            double sA = onBed ? low.Max(q => (Vector3d)q * front) : 0;

            var region = ToRegion(first.Contours, p0);
            Vector3d down3 = -Vector3d.ZAxis - n * (-Vector3d.ZAxis * n); down3.Unitize();
            var down = new PointD(down3 * p0.XAxis, down3 * p0.YAxis);
            double reach = 10000;
            var seg = new PathD { new PointD(0, 0), new PointD(down.x * reach, down.y * reach), new PointD(down.x * reach + 1e-3, down.y * reach + 1e-3), new PointD(1e-3, 1e-3) };
            var swept = new PathsD(region);
            foreach (var r in region) swept = Clipper.Union(swept, Clipper.MinkowskiSum(seg, r, true), FillRule.NonZero, 4);

            var added = new List<SliceLayer>();
            for (int j = startLayers; j >= 1; j--)
            {
                var pl = p0; pl.Origin = p0.Origin - n * (j * h);
                Func<Point3d, PointD> to2 = q => { Vector3d v = q - pl.Origin; return new PointD(v * pl.XAxis, v * pl.YAxis); };
                // where this plane meets a vertical line at (s, t)
                Func<double, double, Point3d> onPlane = (sv, tv) => { Point3d b = Point3d.Origin + front * sv + side * tv; double z = (n * (pl.Origin - b) + n.Z * b.Z) / n.Z; return new Point3d(b.X, b.Y, z); };
                var parts = new PathsD();
                if (onBed)
                {
                    double sF = sA + 2 * L;
                    // the bed line of this plane: n.(front s + bedZ Z) = n.origin
                    double sBed = (n * (Vector3d)pl.Origin - n.Z * bedZ) / (n * front);
                    if (sF > sBed + 0.5)
                    {
                        var band = new PathD { to2(new Point3d((front * sBed + side * t0).X, (front * sBed + side * t0).Y, bedZ)), to2(new Point3d((front * sBed + side * t1).X, (front * sBed + side * t1).Y, bedZ)), to2(onPlane(sF, t1)), to2(onPlane(sF, t0)) };
                        parts.Add(band);
                    }
                }
                // the first layer moved back and filled down, above the bed
                var g = new PointD(pl.XAxis.Z, pl.YAxis.Z);
                double gl = Math.Sqrt(g.x * g.x + g.y * g.y);
                PathsD piece = new PathsD();
                if (gl > 1e-9)
                {
                    double c = bedZ - pl.Origin.Z;
                    var gu = new PointD(g.x / gl, g.y / gl); var tt = new PointD(-gu.y, gu.x);
                    var q0 = new PointD(gu.x * c / gl, gu.y * c / gl);
                    double M = reach;
                    var half = new PathD { new PointD(q0.x + tt.x * M, q0.y + tt.y * M), new PointD(q0.x - tt.x * M, q0.y - tt.y * M), new PointD(q0.x - tt.x * M + gu.x * M, q0.y - tt.y * M + gu.y * M), new PointD(q0.x + tt.x * M + gu.x * M, q0.y + tt.y * M + gu.y * M) };
                    piece = Clipper.Intersect(swept, new PathsD { half }, FillRule.NonZero, 4);
                    if (parts.Count > 0) parts = Clipper.Intersect(parts, new PathsD { half }, FillRule.NonZero, 4);
                }
                var all = Clipper.Union(parts, piece, FillRule.NonZero, 4);
                var contours = ToContours(all, pl);
                if (contours.Count > 0) added.Add(new SliceLayer { Plane = pl, Height = h, Contours = contours });
            }
            if (added.Count == 0) { log?.Add("Start feature: nothing to add in front of the first layer."); return layers; }
            var result = new List<SliceLayer>(added); result.AddRange(layers);
            for (int i = 0; i < result.Count; i++) result[i].Index = i;
            return result;
        }

        private static PathsD ToRegion(List<Polyline> contours, Plane pl)
        {
            var paths = new PathsD();
            foreach (var c in contours)
            {
                if (c == null || c.Count < 4 || c.First.DistanceTo(c.Last) > 1e-3) continue;
                var p = new PathD();
                for (int i = 0; i < c.Count - 1; i++) { Vector3d v = c[i] - pl.Origin; p.Add(new PointD(v * pl.XAxis, v * pl.YAxis)); }
                paths.Add(p);
            }
            return paths.Count == 0 ? paths : Clipper.Union(paths, new PathsD(), FillRule.EvenOdd, 4);
        }

        private static List<Polyline> ToContours(PathsD paths, Plane pl)
        {
            var list = new List<Polyline>();
            foreach (var p in paths)
            {
                if (p.Count < 3 || Math.Abs(Clipper.Area(p)) < 1) continue;
                var poly = new Polyline(p.Select(q => pl.PointAt(q.x, q.y)));
                poly.Add(poly[0]);
                list.Add(poly);
            }
            return list;
        }

        /// <summary>The end feature: endLayers copies of the last layer on top of it, one layer height apart.</summary>
        public static List<SliceLayer> AddEnd(List<SliceLayer> layers, int endLayers, List<string> log)
        {
            if (layers == null || layers.Count == 0 || endLayers <= 0) return layers;
            if (layers[0].Warp != null) { log?.Add("End feature: only for flat or angled layers, not with a guide."); return layers; }
            var result = new List<SliceLayer>(layers);
            var last = layers[layers.Count - 1];
            Vector3d n = last.Plane.Normal; n.Unitize();
            double h = layers.Count > 1 ? layers[1].Height : last.Height;
            for (int j = 1; j <= endLayers; j++)
            {
                var pl = last.Plane; pl.Origin = last.Plane.Origin + n * (j * h);
                var shift = Transform.Translation(n * (j * h));
                result.Add(new SliceLayer { Plane = pl, Height = h, Contours = last.Contours.Select(cv => { var c = new Polyline(cv); c.Transform(shift); return c; }).ToList() });
            }
            for (int i = 0; i < result.Count; i++) result[i].Index = i;
            return result;
        }
    }
}
