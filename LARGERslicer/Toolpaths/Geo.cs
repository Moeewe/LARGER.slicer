using System;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Small geometry helpers written in plain C#. RhinoCommon's own versions of these call into
    /// Rhino's native library (Polyline.IsClosed even copies the whole polyline first), which is
    /// slow in tight loops and makes the slicer core impossible to test outside Rhino.
    /// </summary>
    public static class Geo
    {
        public static bool Closed(Polyline pl)
        {
            return pl != null && pl.Count > 2 && pl[0].DistanceToSquared(pl[pl.Count - 1]) < 1e-12;
        }

        /// <summary>Plane through origin with the given normal; the X axis is chosen as horizontal as possible.</summary>
        public static Plane PlaneAt(Point3d origin, Vector3d normal)
        {
            Vector3d z = normal;
            if (!z.Unitize()) z = Vector3d.ZAxis;
            Vector3d x = Math.Abs(z.Z) > 0.999 ? Vector3d.XAxis : Vector3d.CrossProduct(Vector3d.ZAxis, z);
            x -= z * (x * z);
            x.Unitize();
            Vector3d y = Vector3d.CrossProduct(z, x);
            return PlaneFromAxes(origin, x, y);
        }

        public static Plane PlaneFromAxes(Point3d origin, Vector3d x, Vector3d y)
        {
            Plane p = Plane.WorldXY;
            p.Origin = origin; p.XAxis = x; p.YAxis = y; p.ZAxis = Vector3d.CrossProduct(x, y);
            return p;
        }
    }
}
