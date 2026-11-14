using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Non-planar and multi-axis slicing by deformation: the model is mapped into a "flat" space
    /// in which the wanted layers are ordinary planes, sliced there with the normal pipeline
    /// (walls, fill, ribs, spiral, seam), and the finished path is mapped back. Every stage keeps
    /// working unchanged, and the local layer height that results from the mapping goes into the
    /// extrusion.
    /// </summary>
    public abstract class Warp
    {
        /// <summary>False: the tool stays vertical (3-axis machines). True: the tool is square to the layer.</summary>
        public bool TiltTool = true;
        public abstract string Name { get; }
        /// <summary>True: Slice keeps the layer height as the step in flat space, even where the real
        /// layers lie further apart (the warp has chosen its layer count itself).</summary>
        public virtual bool KeepLayerStep => false;
        /// <summary>True: after mapping back, the bead height of every point is set from the lap below
        /// it in the path by the shell law (BeadLaw) instead of from Scale.</summary>
        public virtual bool UseBeadLaw => false;
        /// <summary>Closed loops shorter than this many bead widths are left out as slivers.</summary>
        public virtual double MinLoopFactor => 3.0;

        /// <summary>Called once with the bounding box of the model, before anything is mapped.</summary>
        public virtual void Prepare(BoundingBox model) { }

        public abstract Point3d ToFlat(Point3d real);
        /// <summary>Maps a point back and returns the layer normal there (unit length).</summary>
        public abstract Point3d ToReal(Point3d flat, out Vector3d normal);

        /// <summary>Real layer height divided by the layer height in flat space, at a flat point.</summary>
        public virtual double Scale(Point3d flat)
        {
            const double d = 0.5;
            Point3d a = ToReal(new Point3d(flat.X, flat.Y, flat.Z - d), out _);
            Point3d b = ToReal(new Point3d(flat.X, flat.Y, flat.Z + d), out Vector3d n);
            double s = Math.Abs((b - a) * n) / (2 * d);
            return s > 1e-6 ? s : 1e-6;
        }

        /// <summary>Bead height at a flat point for a bead that is flatHeight high in flat space: the real
        /// layer height there (flatHeight x Scale). Warps that know how far the layer below lies to the side
        /// add the skin term of the shell law (see BeadLaw).</summary>
        public virtual double BeadHeight(Point3d flat, double flatHeight, PrintSettings st) => flatHeight * Scale(flat);

        public Polyline ToReal(Polyline flat, double maxSegment)
        {
            var result = new Polyline(flat.Count);
            for (int i = 0; i < flat.Count; i++)
            {
                if (i > 0)
                {
                    double len = flat[i].DistanceTo(flat[i - 1]);
                    int parts = maxSegment > 0 ? (int)Math.Ceiling(len / maxSegment) : 1;
                    for (int k = 1; k < parts; k++)
                        result.Add(ToReal(flat[i - 1] + (flat[i] - flat[i - 1]) * (k / (double)parts), out _));
                }
                result.Add(ToReal(flat[i], out _));
            }
            return result;
        }

        /// <summary>Maps something the user drew in real space (seam, rib location, guide) into flat space.</summary>
        public object FlatObject(object o)
        {
            if (o is Point3d p) return ToFlat(p);
            if (o is Point pt) return new Point(ToFlat(pt.Location));
            if (o is Line ln) o = new LineCurve(ln);
            if (o is Curve crv)
            {
                double len = crv.GetLength();
                int n = Math.Max(2, Math.Min(2000, (int)Math.Ceiling(len / 3.0)));
                var pl = new Polyline(n + 1);
                double[] ts = crv.DivideByCount(n, true);
                if (ts == null) return o;
                foreach (double t in ts) pl.Add(ToFlat(crv.PointAt(t)));
                if (crv.IsClosed && pl.Count > 2) pl.Add(pl[0]);
                return new PolylineCurve(pl);
            }
            return o;
        }
    }

    /// <summary>
    /// Layers follow a guide surface.
    /// Follow: every layer is the guide surface moved up - for printing on a curved base or mould.
    /// Morph:  the first layer is flat on the bed (at the first layer height) and the layers above it bend gradually until the last one
    ///         follows the guide - for a curved, smooth top on a flat-printed part.
    /// The guide is sampled once into a height grid, so mapping a point costs a few multiplications.
    /// </summary>
    public class SurfaceWarp : Warp
    {
        private readonly bool _morph;
        private readonly double _x0, _y0, _cell;
        private readonly int _nx, _ny;
        private readonly double[] _g;
        private double _z0, _href = 1, _gRef;
        public bool Valid;
        /// <summary>Morph only: smallest and largest local layer height as a factor of the nominal one.</summary>
        public double MinFactor = 1;
        /// <summary>Morph only: this much above the bottom stays flat - the first layer lies flat on
        /// the bed at its own height and only the layers above it bend. The slicer sets it to the
        /// first layer height.</summary>
        public double FlatBand = 0;
        public bool Morph => _morph;
        private double Band => Math.Min(Math.Max(0, FlatBand), 0.2 * _href);

        public override string Name => _morph ? "non-planar (flat bottom to guide surface)" : "non-planar (follows guide surface)";

        public SurfaceWarp(Mesh guide, bool morph, bool tiltTool)
        {
            _morph = morph; TiltTool = tiltTool;
            BoundingBox bb = guide.GetBoundingBox(true);
            double size = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            if (size <= 0) return;
            _cell = Math.Max(size / 220.0, 0.5);
            _x0 = bb.Min.X; _y0 = bb.Min.Y;
            _nx = (int)Math.Ceiling((bb.Max.X - bb.Min.X) / _cell) + 1;
            _ny = (int)Math.Ceiling((bb.Max.Y - bb.Min.Y) / _cell) + 1;
            _g = new double[_nx * _ny];
            var hit = new bool[_nx * _ny];
            double top = bb.Max.Z + 10.0;
            int hits = 0;
            for (int j = 0; j < _ny; j++)
                for (int i = 0; i < _nx; i++)
                {
                    // nudge the border samples inwards so they do not miss the edge of the surface
                    double x = Math.Min(Math.Max(_x0 + i * _cell, bb.Min.X + 1e-3), bb.Max.X - 1e-3);
                    double y = Math.Min(Math.Max(_y0 + j * _cell, bb.Min.Y + 1e-3), bb.Max.Y - 1e-3);
                    double t = Intersection.MeshRay(guide, new Ray3d(new Point3d(x, y, top), -Vector3d.ZAxis));
                    if (t >= 0) { _g[j * _nx + i] = top - t; hit[j * _nx + i] = true; hits++; }
                }
            if (hits == 0) return;

            // cells the guide does not cover take the value of the nearest covered cell
            var queue = new Queue<int>();
            for (int k = 0; k < hit.Length; k++) if (hit[k]) queue.Enqueue(k);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                int i = k % _nx, j = k / _nx;
                for (int d = 0; d < 4; d++)
                {
                    int ii = i + (d == 0 ? 1 : d == 1 ? -1 : 0), jj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ii < 0 || jj < 0 || ii >= _nx || jj >= _ny) continue;
                    int kk = jj * _nx + ii;
                    if (hit[kk]) continue;
                    hit[kk] = true; _g[kk] = _g[k]; queue.Enqueue(kk);
                }
            }
            Valid = true;
        }

        private double G(double x, double y)
        {
            double fx = (x - _x0) / _cell, fy = (y - _y0) / _cell;
            if (fx < 0) fx = 0; else if (fx > _nx - 1) fx = _nx - 1;
            if (fy < 0) fy = 0; else if (fy > _ny - 1) fy = _ny - 1;
            int i = Math.Min((int)fx, _nx - 2), j = Math.Min((int)fy, _ny - 2);
            if (i < 0) i = 0; if (j < 0) j = 0;
            int i1 = Math.Min(i + 1, _nx - 1), j1 = Math.Min(j + 1, _ny - 1);
            double u = fx - i, v = fy - j;
            double a = _g[j * _nx + i], b = _g[j * _nx + i1], c = _g[j1 * _nx + i], d = _g[j1 * _nx + i1];
            return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
        }

        public override void Prepare(BoundingBox model)
        {
            _z0 = model.Min.Z;
            double gMin = double.MaxValue, gMax = double.MinValue;
            for (int j = 0; j < _ny; j++)
                for (int i = 0; i < _nx; i++)
                {
                    double x = _x0 + i * _cell, y = _y0 + j * _cell;
                    if (x < model.Min.X - _cell || x > model.Max.X + _cell || y < model.Min.Y - _cell || y > model.Max.Y + _cell) continue;
                    double g = _g[j * _nx + i];
                    if (g < gMin) gMin = g;
                    if (g > gMax) gMax = g;
                }
            if (gMin > gMax) { gMin = double.MaxValue; gMax = double.MinValue; foreach (double g in _g) { if (g < gMin) gMin = g; if (g > gMax) gMax = g; } }
            _gRef = gMin;
            _href = Math.Max(gMax - _z0, 1e-6);
            MinFactor = Math.Max(0.25, (gMin - _z0) / _href);
        }

        /// <summary>Morph: height of the guide above the bed, never thinner than a quarter of the highest point.</summary>
        private double Column(double x, double y) => Math.Max(G(x, y) - _z0, 0.25 * _href);

        public override Point3d ToFlat(Point3d p)
        {
            if (!Valid) return p;
            if (_morph)
            {
                double b = Band;
                if (p.Z <= _z0 + b) return p;
                return new Point3d(p.X, p.Y, _z0 + b + (p.Z - _z0 - b) * (_href - b) / (Column(p.X, p.Y) - b));
            }
            return new Point3d(p.X, p.Y, p.Z - G(p.X, p.Y) + _gRef);
        }

        public override Point3d ToReal(Point3d f, out Vector3d normal)
        {
            normal = Vector3d.ZAxis;
            if (!Valid) return f;
            double amount = 1.0;
            Point3d real;
            if (_morph)
            {
                double b = Band;
                if (f.Z <= _z0 + b) { amount = 0; real = f; }
                else
                {
                    amount = (f.Z - _z0 - b) / (_href - b);
                    real = new Point3d(f.X, f.Y, _z0 + b + (f.Z - _z0 - b) * (Column(f.X, f.Y) - b) / (_href - b));
                }
            }
            else real = new Point3d(f.X, f.Y, f.Z + G(f.X, f.Y) - _gRef);

            if (TiltTool)
            {
                double gx = (G(f.X + _cell, f.Y) - G(f.X - _cell, f.Y)) / (2 * _cell);
                double gy = (G(f.X, f.Y + _cell) - G(f.X, f.Y - _cell)) / (2 * _cell);
                normal = new Vector3d(-gx * amount, -gy * amount, 1.0);
                double len = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + 1.0);
                normal = new Vector3d(normal.X / len, normal.Y / len, 1.0 / len);
            }
            return real;
        }

        public override double Scale(Point3d flat)
        {
            if (!Valid) return 1.0;
            // the layers lie this far apart vertically ...
            double vertical = 1.0;
            if (_morph && flat.Z > _z0 + Band) vertical = (Column(flat.X, flat.Y) - Band) / (_href - Band);
            if (!TiltTool) return vertical; // vertical nozzle: the bead is as high as the vertical gap
            // ... but a tool square to the layer lays a bead as thick as the gap square to the layer,
            // which on a slope is smaller by the cosine of the slope (45 degrees: 0.71)
            ToReal(flat, out Vector3d n);
            return Math.Max(1e-6, vertical * Math.Abs(n.Z));
        }
    }

    /// <summary>
    /// Multi-axis: the layers are planes square to a guide curve, one layer height apart along
    /// the curve - for parts that grow along a bent axis (branches, arches, overhangs printed
    /// without support). In flat space the curve is a straight vertical line.
    /// </summary>
    public class CurveWarp : Warp
    {
        private readonly Point3d[] _o;
        private readonly Vector3d[] _t, _x, _y;
        private readonly double _step;
        private readonly Point3d _base;
        public bool Valid;

        public override string Name => "multi-axis (layers square to guide curve)";

        public CurveWarp(Curve guide, bool tiltTool)
        {
            TiltTool = tiltTool;
            Curve c = guide.DuplicateCurve();
            if (c.PointAtEnd.Z < c.PointAtStart.Z) c.Reverse();
            double length = c.GetLength();
            if (length < 1e-6) return;
            int n = Math.Max(2, Math.Min(8000, (int)Math.Ceiling(length / 0.2)));
            double[] ts = c.DivideByCount(n, true);
            if (ts == null || ts.Length < 2) return;
            _step = length / n;
            _o = new Point3d[ts.Length]; _t = new Vector3d[ts.Length]; _x = new Vector3d[ts.Length]; _y = new Vector3d[ts.Length];
            for (int i = 0; i < ts.Length; i++)
            {
                _o[i] = c.PointAt(ts[i]);
                Vector3d t = c.TangentAt(ts[i]);
                double tl = Math.Sqrt(t.X * t.X + t.Y * t.Y + t.Z * t.Z);
                _t[i] = tl > 1e-12 ? t / tl : Vector3d.ZAxis;
            }
            Vector3d seed = Math.Abs(_t[0].X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis;
            for (int i = 0; i < ts.Length; i++)
            {
                Vector3d x = (i == 0 ? seed : _x[i - 1]);
                x -= _t[i] * (x * _t[i]);
                double xl = Math.Sqrt(x.X * x.X + x.Y * x.Y + x.Z * x.Z);
                x = xl > 1e-9 ? x / xl : _x[Math.Max(0, i - 1)];
                _x[i] = x;
                _y[i] = Vector3d.CrossProduct(_t[i], x);
            }
            _base = _o[0];
            Valid = true;
        }

        public override Point3d ToFlat(Point3d p)
        {
            if (!Valid) return p;
            int n = _o.Length;
            // coarse search, then fine
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < n; i += 16) { double d = Dist2(p, _o[i]); if (d < bd) { bd = d; best = i; } }
            int lo = Math.Max(0, best - 16), hi = Math.Min(n - 1, best + 16);
            for (int i = lo; i <= hi; i++) { double d = Dist2(p, _o[i]); if (d < bd) { bd = d; best = i; } }
            // the frame whose slab [0, step) holds the point
            int k = best;
            double along = (p - _o[k]) * _t[k];
            for (int guard = 0; guard < 4; guard++)
            {
                if (along < 0 && k > 0) { k--; along = (p - _o[k]) * _t[k]; }
                else if (along >= _step && k < n - 1) { k++; along = (p - _o[k]) * _t[k]; }
                else break;
            }
            Vector3d v = p - _o[k];
            return new Point3d(_base.X + v * _x[k], _base.Y + v * _y[k], _base.Z + k * _step + along);
        }

        public override Point3d ToReal(Point3d f, out Vector3d normal)
        {
            normal = Vector3d.ZAxis;
            if (!Valid) return f;
            double s = f.Z - _base.Z;
            int k = (int)Math.Floor(s / _step);
            if (k < 0) k = 0; else if (k > _o.Length - 1) k = _o.Length - 1;
            double along = s - k * _step;
            if (TiltTool) normal = _t[k];
            return _o[k] + _x[k] * (f.X - _base.X) + _y[k] * (f.Y - _base.Y) + _t[k] * along;
        }

        private static double Dist2(Point3d a, Point3d b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }

        /// <summary>Layer height on the outside of a bend is larger than along the curve.</summary>
        public override double Scale(Point3d flat)
        {
            if (!Valid) return 1.0;
            double d = _step * 4;
            Point3d a = ToReal(new Point3d(flat.X, flat.Y, flat.Z - d), out _);
            Point3d b = ToReal(new Point3d(flat.X, flat.Y, flat.Z + d), out _);
            double s = a.DistanceTo(b) / (2 * d);
            return s > 1e-6 ? s : 1e-6;
        }
    }
}
