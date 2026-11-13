using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>Start and end moves of a print, and the printability check of a finished path.</summary>
    public static class LeadEngine
    {
        // ------------------------------------------------------------------ start and end

        /// <summary>
        /// Adds a skirt in front of the path and an exit move behind it.
        ///
        /// Skirt: loops around the first layer, a few bead widths away from the part. It is always
        /// printed first; the extruder gets up to pressure there instead of on the part. From the
        /// skirt the bead runs on into the outer wall without stopping.
        ///
        /// Exit: after the last point the nozzle moves sideways off the part and then lifts, so the
        /// drop that forms when the extruder stops does not end up on the part. Give a curve to
        /// set the exit path yourself. exitFlow is the flow factor on the exit (0 = no extrusion).
        /// </summary>
        public static Toolpath StartEnd(Toolpath tp, int skirtLoops, double skirtDistance,
            double exitLength, double exitLift, Curve exitCurve, double exitFlow, List<string> log)
            => StartEnd(tp, skirtLoops, skirtDistance, 0, 0, exitLength, exitLift, exitCurve, exitFlow, log);

        /// <summary>
        /// The same with a brim: beads that lie right against the first layer and widen its foot,
        /// so the part holds on to the bed. brimSide: 0 = none, 1 = outside, 2 = inside, 3 = both.
        ///
        /// Outside: loops around the part, the outermost first; the last one ends next to the
        /// start of the part and the bead runs on into the wall. Inside: printed when the first
        /// layer is finished. The bead runs straight in to the innermost loop, then the loops
        /// are printed from the inside out, each one the other way round than the one before,
        /// and the last one ends back where the first layer ended. The loops leave a lane of two
        /// bead spacings open for the way in, so nothing is crossed and nothing moves without
        /// extrusion.
        /// </summary>
        public static Toolpath StartEnd(Toolpath tp, int skirtLoops, double skirtDistance, int brimLoops, int brimSide,
            double exitLength, double exitLift, Curve exitCurve, double exitFlow, List<string> log, double approachHeight = 30)
        {
            var done = StartEndOnly(tp, skirtLoops, skirtDistance, brimLoops, brimSide, exitLength, exitLift, exitCurve, exitFlow, log);
            done = Thin(done, Math.Max(0.5, 0.2 * tp.Settings.BeadWidth));
            return Approach(done, approachHeight);
        }

        /// <summary>
        /// Leaves out points that lie closer than minDistance to the point before them. A machine
        /// needs a few milliseconds for every line; on pieces of a few hundredths of a millimetre
        /// it slows down, and the extruder, which keeps going, leaves a blob. A point is kept where
        /// the role, the flow, the layer or the tool axis changes, where leaving it out would move the path
        /// more than 0.02 mm (a corner, the start of a ramp), and the last point always.
        /// </summary>
        public static Toolpath Thin(Toolpath tp, double minDistance)
        {
            if (tp == null || tp.Count < 3 || minDistance <= 0) return tp;
            var r = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            r.AddFrom(tp, 0);
            int kept = 0; // index in tp of the last point kept
            for (int i = 1; i < tp.Count; i++)
            {
                int k = r.Count - 1;
                bool last = i == tp.Count - 1;
                bool same = tp.Roles[i] == r.Roles[k] && Math.Abs(tp.Flows[i] - r.Flows[k]) < 1e-9 && tp.Layers[i] == r.Layers[k]
                            && tp.Normals[i] * r.Normals[k] > 0.9994; // about 2 degrees
                double d = tp.Points[i].DistanceTo(r.Points[k]);
                if (!last && same && d < minDistance)
                {
                    // only where the line to the next point stays on the path: a corner or the start of a
                    // ramp is never cut off (the wall would step in there)
                    bool onLine = true;
                    for (int j = kept + 1; j <= i && onLine; j++)
                        onLine = PathEngine.DistToSegment(tp.Points[j], r.Points[k], tp.Points[i + 1]) <= PathEngine.ShapeTolerance;
                    if (onLine) continue;
                }
                // the same point twice (where a wall hands over to the seam, for example) is a move of
                // length 0 that carries nothing: no machine needs it, and some controllers stop on it.
                // The moves before and after keep their own role, flow and layer.
                if (d < 1e-6 && tp.Normals[i] * r.Normals[k] > 0.9994) { kept = i; continue; }
                r.AddFrom(tp, i); kept = i;
            }
            return r;
        }

        /// <summary>
        /// Puts a point this far above the first point in front of the path, along the tool axis.
        /// The machine comes down onto the start from above instead of moving towards it at print
        /// height across the bed (the Weber converter does the same with 30 mm).
        /// </summary>
        public static Toolpath Approach(Toolpath tp, double height)
        {
            if (tp == null || tp.Count < 1 || height <= 0) return tp;
            var r = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            Vector3d axis = tp.Normals[0]; if (!axis.Unitize()) axis = Vector3d.ZAxis;
            r.Add(tp.Points[0] + axis * height, tp.Normals[0], tp.Heights[0], tp.Widths[0], 0, PathRole.Travel, tp.Layers[0]);
            for (int i = 0; i < tp.Count; i++) r.AddFrom(tp, i);
            // the way down onto the start prints nothing
            r.Flows[1] = 0; r.Roles[1] = PathRole.Travel;
            return r;
        }

        private static Toolpath StartEndOnly(Toolpath tp, int skirtLoops, double skirtDistance, int brimLoops, int brimSide,
            double exitLength, double exitLift, Curve exitCurve, double exitFlow, List<string> log)
        {
            var result = new Toolpath { Settings = tp.Settings, Warp = tp.Warp };
            if (tp.Count < 2) return tp.Duplicate();
            var st = tp.Settings;
            double w = st.BeadWidth, spacing = st.BeadSpacing;

            // ---- brim: the outline of the first layer's beads, and what is free inside it
            bool brimOut = brimLoops > 0 && (brimSide == 1 || brimSide == 3), brimIn = brimLoops > 0 && (brimSide == 2 || brimSide == 3);
            int lastOfFirst = 0;
            while (lastOfFirst + 1 < tp.Count && tp.Layers[lastOfFirst + 1] == tp.Layers[0]) lastOfFirst++;
            var brimPlane = Geo.PlaneAt(tp.Points[0], tp.Normals[0]);
            Func<Point3d, Point2d> flat = q => { Vector3d v = q - brimPlane.Origin; return new Point2d(v * brimPlane.XAxis, v * brimPlane.YAxis); };
            Func<Point2d, Point3d> solid = q => brimPlane.Origin + brimPlane.XAxis * q.X + brimPlane.YAxis * q.Y;
            double brimHeight = tp.Heights[0] > 0 ? tp.Heights[0] : st.LayerHeight;
            PathsD beads = null;
            if (brimOut || brimIn)
            {
                if (tp.Warp == null && Math.Abs(tp.Normals[0].Z) < 0.999)
                {
                    log?.Add("Brim skipped: with tilted layers the first layer is only a strip on the bed.");
                    brimOut = brimIn = false;
                }
                else
                {
                    var lines = new PathsD(); PathD run = null;
                    for (int i = 0; i <= lastOfFirst; i++)
                    {
                        Point2d q = flat(tp.Points[i]);
                        if (i > 0 && tp.Flows[i] <= 0) run = null;
                        if (run == null) { run = new PathD(); lines.Add(run); }
                        run.Add(new PointD(q.X, q.Y));
                    }
                    lines.RemoveAll(l => l.Count < 2);
                    // a ring that leaves its seam gap open (end one bead before its start) counts as closed:
                    // the brim follows the wall, not the little notch of the gap
                    foreach (var l in lines)
                    {
                        PointD a = l[0], b = l[l.Count - 1];
                        double gapLen = Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
                        if (l.Count > 3 && gapLen > 1e-6 && gapLen <= 1.5 * w) l.Add(a);
                    }
                    beads = lines.Count > 0 ? Clipper.InflatePaths(lines, 0.5 * w, JoinType.Round, EndType.Round, 2.0, 3, 0.05) : null;
                    if (beads == null || beads.Count == 0) { log?.Add("Brim skipped: the first layer is too small."); brimOut = brimIn = false; }
                }
            }
            double outerBrim = brimOut ? brimLoops * spacing : 0;

            // ---- skirt and outer brim: closed rings around the first layer, printed from the outermost in.
            // Every ring leaves a gap of one bead before its own start (its end never runs over its start;
            // with round bead ends the two just touch), and the bead goes straight across to the next ring,
            // square to both, right where that ring starts. So each ring starts one bead further on than the
            // one inside it ends: the links form a staircase, never lie side by side and cross nothing, and
            // neighbouring rings stay exactly one bead spacing apart. The innermost ring ends right beside
            // the start of the part (the seam, or where the spiral starts) and runs on into it.
            double linkLength = -1, skirtUsed = skirtDistance;
            bool tilted = tp.Warp == null && Math.Abs(tp.Normals[0].Z) < 0.999;
            Vector3d ringAxis = tp.Normals[0];
            Plane ringPlane = brimPlane;
            double ringHeight = tp.Heights[0] > 0 ? tp.Heights[0] : st.LayerHeight;
            var rings = new List<List<Point2d>>();   // in print order
            Point2d? skirtAim = null;                // where the innermost skirt ring ends when it leads straight to the part
            if (skirtLoops > 0)
            {
                var pts2 = new List<Point2d>();
                Point2d startOnPlane;
                if (tilted)
                {
                    // Tilted layers (angled printing): the first layer is only a strip. The skirt
                    // goes around everything that stands on the bed, flat on the bed.
                    double zBed = double.MaxValue;
                    for (int i = 0; i < tp.Count; i++) if (tp.Flows[i] > 0 && tp.Points[i].Z < zBed) zBed = tp.Points[i].Z;
                    if (zBed == double.MaxValue) zBed = tp.Points[0].Z;
                    ringPlane = Plane.WorldXY; ringPlane.Origin = new Point3d(0, 0, zBed); ringAxis = Vector3d.ZAxis;
                    double reach = st.LayerHeight + 0.5 * w;
                    for (int i = 0; i < tp.Count; i++)
                        if (tp.Points[i].Z <= zBed + reach) pts2.Add(new Point2d(tp.Points[i].X, tp.Points[i].Y));
                    startOnPlane = new Point2d(tp.Points[0].X, tp.Points[0].Y);
                }
                else
                {
                    for (int i = 0; i <= lastOfFirst; i++) pts2.Add(flat(tp.Points[i]));
                    startOnPlane = pts2.Count > 0 ? pts2[0] : new Point2d(0, 0);
                }
                var hull = ConvexHull(pts2);
                if (hull.Count >= 3)
                {
                    if (skirtDistance <= 0) skirtDistance = 3 * w;
                    // the skirt stays clear of an outer brim
                    if (outerBrim > 0) skirtDistance = Math.Max(skirtDistance, outerBrim + 3 * w);
                    skirtUsed = skirtDistance;
                    var path = new PathD(hull.Count);
                    foreach (var p in hull) path.Add(new PointD(p.X, p.Y));
                    // A part that starts in a hollow of its outline (an inner corner) lies further from
                    // the skirt. Then the skirt ends at the nearest point from which a straight line
                    // reaches the start without touching the first layer, and that line prints: skirt
                    // and wall stay one bead.
                    if (!tilted && !brimOut)
                    {
                        PathsD inner = Clipper.InflatePaths(new PathsD { path }, skirtDistance, JoinType.Round, EndType.Polygon, 2.0, 3, 0.05);
                        if (inner != null && inner.Count > 0 && inner[0].Count >= 3)
                        {
                            var ring0 = inner[0].Select(p => new Point2d(p.x, p.y)).ToList();
                            var cum0 = FillEngine.Cumulative(ring0);
                            FillEngine.ClosestS(ring0, cum0, startOnPlane, out double away0);
                            if (away0 > skirtDistance + 0.75 * w)
                            {
                                double bestD = double.MaxValue;
                                double len0 = cum0[ring0.Count];
                                foreach (var q in Enumerable.Range(0, (int)(len0 / (0.5 * w))).Select(k => FillEngine.PointAt(ring0, cum0, k * 0.5 * w)))
                                {
                                    double d = q.DistanceTo(startOnPlane);
                                    if (d >= bestD) continue;
                                    if (LinkClear(ringPlane.Origin + ringPlane.XAxis * q.X + ringPlane.YAxis * q.Y, tp, lastOfFirst, w)) { bestD = d; skirtAim = q; }
                                }
                            }
                        }
                    }
                    for (int k = skirtLoops - 1; k >= 0; k--)
                    {
                        PathsD ring = Clipper.InflatePaths(new PathsD { path }, skirtDistance + k * spacing, JoinType.Round, EndType.Polygon, 2.0, 3, 0.05);
                        if (ring == null || ring.Count == 0 || ring[0].Count < 3) continue;
                        var loop = ring[0].Select(p => new Point2d(p.x, p.y)).ToList();
                        if (FillEngine.Cumulative(loop)[loop.Count] < 4 * w) continue;
                        rings.Add(loop);
                    }
                }
                else log?.Add("Skirt skipped: the first layer is too small.");
            }

            int brimMade = 0;
            if (brimOut)
            {
                Point2d start2 = flat(tp.Points[0]);
                PathD outline = null; double nearest = double.MaxValue;
                foreach (var path in beads)
                {
                    if (Clipper.Area(path) <= 0) continue;
                    var ring0 = path.Select(q => new Point2d(q.x, q.y)).ToList();
                    FillEngine.ClosestS(ring0, FillEngine.Cumulative(ring0), start2, out double d0);
                    if (d0 < nearest) { nearest = d0; outline = path; }
                }
                for (int k = brimLoops; k >= 1 && outline != null; k--)
                {
                    PathsD ring = Clipper.InflatePaths(new PathsD { outline }, k * spacing - 0.5 * w, JoinType.Round, EndType.Polygon, 2.0, 3, 0.05);
                    if (ring == null || ring.Count == 0 || ring[0].Count < 3) continue;
                    var loop = ring[0].Select(q => new Point2d(q.x, q.y)).ToList();
                    if (FillEngine.Cumulative(loop)[loop.Count] < 4 * w) continue;
                    rings.Add(loop); brimMade++;
                }
                if (brimMade == 0) log?.Add("Outer brim skipped: no room for it.");
                else if (beads.Count(b => Clipper.Area(b) > 0) > 1) log?.Add("The first layer stands in several pieces: the outer brim goes around the piece the print starts on.");
            }

            if (rings.Count > 0)
            {
                Func<Point3d, Point2d> to2 = q => { Vector3d v = q - ringPlane.Origin; return new Point2d(v * ringPlane.XAxis, v * ringPlane.YAxis); };
                Point2d s2 = to2(tp.Points[0]);
                // the rings run round the same way as the part, so the bead goes on from the last ring into
                // the part without turning back
                Vector2d go = Vector2d.Zero;
                for (int i = 1; i <= lastOfFirst && go.Length < 1e-6; i++) go = to2(tp.Points[i]) - s2;
                var lastRing = rings[rings.Count - 1];
                var lastCum = FillEngine.Cumulative(lastRing);
                double sNear = FillEngine.ClosestS(lastRing, lastCum, s2, out _);
                Vector2d along = FillEngine.PointAt(lastRing, lastCum, sNear + 0.5) - FillEngine.PointAt(lastRing, lastCum, sNear - 0.5);
                if (go.Length > 1e-6 && along * go < 0) foreach (var r in rings) r.Reverse();

                double gap = w;
                var cums = rings.Select(FillEngine.Cumulative).ToList();
                var ends = new double[rings.Count];
                Point2d target = s2;
                for (int j = rings.Count - 1; j >= 0; j--)
                {
                    bool aimed = j == rings.Count - 1 && skirtAim.HasValue && !brimOut;
                    ends[j] = FillEngine.ClosestS(rings[j], cums[j], aimed ? skirtAim.Value : target, out _);
                    if (j == rings.Count - 1)
                    {
                        linkLength = FillEngine.PointAt(rings[j], cums[j], ends[j]).DistanceTo(s2);
                        if (brimMade > 0) skirtUsed = spacing;
                    }
                    target = FillEngine.PointAt(rings[j], cums[j], ends[j] + gap);
                }
                // the whole line, with the points where it turns from a ring onto a link and back
                var line = new List<Point2d>(); var turns = new HashSet<int>();
                for (int j = 0; j < rings.Count; j++)
                {
                    double len = cums[j][rings[j].Count];
                    var piece = FillEngine.Sub(rings[j], cums[j], ends[j] + gap, ends[j] + len);
                    if (j > 0) turns.Add(line.Count);
                    foreach (var q in piece) if (line.Count == 0 || q.DistanceTo(line[line.Count - 1]) > 1e-6) line.Add(q);
                    turns.Add(line.Count - 1);
                }
                line.Add(s2); // the start of the part (printed by the part itself)
                line = Fillet(line, turns, 0.25 * w);
                for (int k = 0; k + 1 < line.Count; k++)
                {
                    Point3d p3 = ringPlane.Origin + ringPlane.XAxis * line[k].X + ringPlane.YAxis * line[k].Y;
                    if (result.Count > 0 && p3.DistanceTo(result.Points[result.Count - 1]) < 1e-6) continue;
                    result.Add(p3, ringAxis, ringHeight, w, 1, PathRole.Skirt, tp.Layers[0]);
                }
            }

            // ---- inner brim: prepared here, printed when the first layer is finished
            List<Point3d> innerBrim = brimIn ? InnerBrim(tp, beads, lastOfFirst, brimLoops, flat, solid, log) : null;

            // ---- the part itself. The skirt ends next to the first point of the part and the
            // nozzle keeps printing as it moves across: skirt and outer wall are one continuous
            // bead, the extruder never stops between them.
            int offset = result.Count;
            for (int i = 0; i < tp.Count; i++)
            {
                result.AddFrom(tp, i);
                if (i == lastOfFirst && innerBrim != null)
                {
                    // The way back from the last brim loop to the wall runs over beads printed already
                    // (the end of the first layer): it carries no bead height, so nothing is printed
                    // twice. The nozzle keeps moving at print speed; the next layer grows from there.
                    Point3d home = tp.Points[lastOfFirst];
                    bool homeAdded = innerBrim.Count > 1 && innerBrim[innerBrim.Count - 1].DistanceTo(home) < 1e-6;
                    int ringEnd = homeAdded ? innerBrim.Count - 2 : innerBrim.Count - 1;
                    for (int k = 0; k <= ringEnd; k++) result.Add(innerBrim[k], tp.Normals[0], brimHeight, w, 1, PathRole.Skirt, tp.Layers[0]);
                    Point3d next = homeAdded ? home : (i + 1 < tp.Count ? tp.Points[i + 1] : home);
                    Vector3d back = next - innerBrim[ringEnd];
                    if (back.Unitize())
                    {
                        result.Add(innerBrim[ringEnd] + back * 0.05, tp.Normals[0], 0, w, 1, PathRole.Seam, tp.Layers[0]);
                        if (homeAdded) result.Add(home, tp.Normals[0], 0, w, 1, PathRole.Seam, tp.Layers[0]);
                    }
                }
            }
            if (offset > 0)
            {
                // The move across to the part prints only if the part starts on its outside, right
                // opposite the skirt. If it starts further in (a bottom fill, an inner wall), a
                // printed line would lie where other beads are still to come: then it is a travel.
                bool direct = linkLength >= 0 && linkLength <= skirtUsed + 0.75 * w;
                result.Flows[offset] = direct ? 1 : 0;
                if (!direct) { result.Roles[offset] = PathRole.Travel; log?.Add("The part does not start on its outer wall: the move from the skirt to the start is a travel move."); }
                result.Heights[offset] = tp.Heights[0] > 0 ? tp.Heights[0] : st.LayerHeight;
            }

            // ---- exit
            int lastIndex = tp.Count - 1;
            Point3d end = tp.Points[lastIndex];
            Vector3d axis = tp.Normals[lastIndex];
            int lastLayer = tp.Layers[lastIndex];
            double flow = Math.Max(0, exitFlow);
            PathRole exitRole = flow > 0 ? PathRole.Wall : PathRole.Travel;
            double h = st.LayerHeight;

            if (exitCurve != null)
            {
                Polyline pl;
                if (!exitCurve.TryGetPolyline(out pl))
                {
                    var pc = exitCurve.ToPolyline(0.1, 0.05, 0.5, 0);
                    pl = pc != null ? pc.ToPolyline() : null;
                }
                if (pl != null && pl.Count >= 2)
                {
                    // run the curve from the end that is nearer to the last print point
                    if (pl[pl.Count - 1].DistanceTo(end) < pl[0].DistanceTo(end)) pl.Reverse();
                    foreach (var p in pl)
                    {
                        if (p.DistanceTo(result.Points[result.Count - 1]) < 1e-6) continue;
                        result.Add(p, axis, h, w, flow, exitRole, lastLayer);
                    }
                }
                else log?.Add("Exit curve could not be used.");
            }
            else if (exitLength > 0 || exitLift > 0)
            {
                // away from the middle of the last layer = the shortest way off the part
                Point3d centre = Point3d.Origin; int cnt = 0;
                for (int i = lastIndex; i >= 0 && tp.Layers[i] == lastLayer; i--) { centre += tp.Points[i]; cnt++; }
                centre /= Math.Max(1, cnt);
                Vector3d dir = end - centre;
                dir -= axis * (dir * axis);
                if (!dir.Unitize())
                {
                    dir = tp.Points[lastIndex] - tp.Points[lastIndex - 1];
                    dir -= axis * (dir * axis);
                    if (!dir.Unitize()) dir = Vector3d.XAxis;
                }
                Point3d p = end;
                if (exitLength > 0) { p += dir * exitLength; result.Add(p, axis, h, w, flow, exitRole, lastLayer); }
                if (exitLift > 0) { p += axis * exitLift; result.Add(p, axis, h, w, 0, PathRole.Travel, lastLayer); }
            }
            return result;
        }

        /// <summary>
        /// Rounds the given corners of a line with a curve that leaves and rejoins it r from the
        /// corner (a quadratic arc inside the corner), so the nozzle does not stop dead on a link.
        /// </summary>
        private static List<Point2d> Fillet(List<Point2d> line, HashSet<int> corners, double r)
        {
            if (r <= 0 || corners.Count == 0 || line.Count < 3) return line;
            var cum = new double[line.Count];
            for (int i = 1; i < line.Count; i++) cum[i] = cum[i - 1] + line[i].DistanceTo(line[i - 1]);
            Func<double, Point2d> at = s =>
            {
                s = Math.Max(0, Math.Min(cum[cum.Length - 1], s));
                int i = Array.BinarySearch(cum, s); if (i >= 0) return line[i];
                i = ~i; if (i <= 0) return line[0];
                double seg = cum[i] - cum[i - 1];
                return seg < 1e-12 ? line[i] : line[i - 1] + (line[i] - line[i - 1]) * ((s - cum[i - 1]) / seg);
            };
            var cut = new List<Tuple<double, double, int>>();
            foreach (int c in corners.OrderBy(c => c))
            {
                if (c <= 0 || c >= line.Count - 1) continue;
                Vector2d a = line[c] - line[c - 1], b = line[c + 1] - line[c];
                if (a.Length < 1e-9 || b.Length < 1e-9 || Math.Acos(Math.Max(-1, Math.Min(1, (a * b) / (a.Length * b.Length)))) < 0.2) continue;
                // never more than half the straight piece on either side (the link between two corners)
                double room = cum[c] - (cut.Count > 0 ? cut[cut.Count - 1].Item2 : 0);
                double t = Math.Min(r, Math.Min(0.5 * room, 0.5 * (cum[c + 1] - cum[c])));
                if (t < 1e-3) continue;
                cut.Add(Tuple.Create(cum[c] - t, cum[c] + t, c));
            }
            if (cut.Count == 0) return line;
            var r2 = new List<Point2d>();
            int idx = 0;
            foreach (var k in cut)
            {
                while (idx < line.Count && cum[idx] < k.Item1 - 1e-9) r2.Add(line[idx++]);
                Point2d p0 = at(k.Item1), p1 = line[k.Item3], p2 = at(k.Item2);
                for (int j = 0; j <= 6; j++)
                {
                    double u = j / 6.0, v = 1 - u;
                    var q = new Point2d(v * v * p0.X + 2 * u * v * p1.X + u * u * p2.X, v * v * p0.Y + 2 * u * v * p1.Y + u * u * p2.Y);
                    if (r2.Count == 0 || q.DistanceTo(r2[r2.Count - 1]) > 1e-6) r2.Add(q);
                }
                while (idx < line.Count && cum[idx] <= k.Item2 + 1e-9) idx++;
            }
            while (idx < line.Count) r2.Add(line[idx++]);
            return r2;
        }

        /// <summary>
        /// The inner brim as one line that starts and ends where the first layer ended (that
        /// point itself is not in the list). Null if there is no room for it.
        /// </summary>
        private static List<Point3d> InnerBrim(Toolpath tp, PathsD beads, int lastOfFirst, int loops,
            Func<Point3d, Point2d> flat, Func<Point2d, Point3d> solid, List<string> log)
        {
            double w = tp.Settings.BeadWidth, spacing = tp.Settings.BeadSpacing;
            Point2d end2 = flat(tp.Points[lastOfFirst]);
            // the free room inside the first layer that lies next to its end
            PathD room = null; double nearest = double.MaxValue;
            foreach (var path in beads)
            {
                if (Clipper.Area(path) >= 0) continue;
                var ring0 = path.Select(q => new Point2d(q.x, q.y)).ToList();
                FillEngine.ClosestS(ring0, FillEngine.Cumulative(ring0), end2, out double d0);
                if (d0 < nearest) { nearest = d0; room = path; }
            }
            if (room == null || nearest > w) { log?.Add("Inner brim skipped: the first layer does not end next to free room inside the part (a solid bottom fills it)."); return null; }
            var hole = new PathD(room); hole.Reverse();
            PathsD free = Clipper.Difference(new PathsD { hole }, beads, FillRule.NonZero, 3);
            if (free == null || free.Count == 0) { log?.Add("Inner brim skipped: no room for it."); return null; }

            // the loops, from the wall inwards; each one only if it has room for the lane
            var rings = new List<List<Point2d>>(); var cums = new List<double[]>(); var at = new List<double>();
            for (int k = 1; k <= loops; k++)
            {
                PathsD inset = Clipper.InflatePaths(free, -(k * spacing - 0.5 * w), JoinType.Round, EndType.Polygon, 2.0, 3, 0.05);
                PathD best = null; double area = 0;
                if (inset != null) foreach (var path in inset) { double a = Clipper.Area(path); if (a > area) { area = a; best = path; } }
                if (best == null || best.Count < 3) break;
                var loop = best.Select(q => new Point2d(q.x, q.y)).ToList();
                var cum = FillEngine.Cumulative(loop);
                if (cum[loop.Count] < 6 * spacing) break;
                Point2d anchor = rings.Count == 0 ? end2 : FillEngine.Sub(rings[rings.Count - 1], cums[cums.Count - 1], at[at.Count - 1], at[at.Count - 1] + 1e-6)[0];
                double s0 = FillEngine.ClosestS(loop, cum, anchor, out double away);
                if (away > 1.5 * spacing) break; // this loop is no longer beside the one before
                rings.Add(loop); cums.Add(cum); at.Add(s0);
            }
            if (rings.Count == 0) { log?.Add("Inner brim skipped: no room for it."); return null; }
            if (rings.Count < loops) log?.Add(Inv.F($"Inner brim: room for {rings.Count} of {loops} loops."));
            if (beads.Count(b => Clipper.Area(b) < 0) > 1) log?.Add("The first layer divides the inside of the part into several rooms: the inner brim lies in the one where the first layer ends.");

            Func<int, double, Point2d> on = (k, s) => FillEngine.Sub(rings[k], cums[k], s, s + 1e-6)[0];
            var line = new List<Point2d>();
            // straight in, through the middle of the lane, to the innermost loop
            for (int k = 0; k < rings.Count; k++) line.Add(on(k, at[k]));

            // Which way round the loop next to the wall runs: so that it arrives at the end of
            // the first layer moving the way the path goes on from there.
            bool forward = true;
            if (lastOfFirst + 1 < tp.Count || lastOfFirst > 0)
            {
                Point2d ahead = lastOfFirst + 1 < tp.Count ? flat(tp.Points[lastOfFirst + 1]) : end2;
                Vector2d go = ahead - end2;
                if (go.Length < 1e-6 && lastOfFirst + 2 < tp.Count) go = flat(tp.Points[lastOfFirst + 2]) - end2;
                if (go.Length < 1e-6) go = end2 - flat(tp.Points[lastOfFirst - 1]);
                double len0 = cums[0][rings[0].Count];
                Point2d endForward = on(0, at[0] + len0 - spacing); // where the loop ends if it runs forward
                forward = (end2 - endForward) * go >= 0;
            }
            for (int k = rings.Count - 1; k >= 0; k--)
            {
                double len = cums[k][rings[k].Count];
                var piece = FillEngine.Sub(rings[k], cums[k], at[k] + spacing, at[k] + len - spacing);
                // loop k runs the way loop 0 does if it is an even number of loops away from it
                bool thisForward = (k % 2 == 0) == forward;
                if (!thisForward) piece.Reverse();
                line.AddRange(piece);
            }
            var result = new List<Point3d>();
            Point3d last = tp.Points[lastOfFirst];
            foreach (Point2d q in line)
            {
                Point3d p3 = solid(q);
                if (p3.DistanceTo(last) < 1e-6) continue;
                result.Add(p3); last = p3;
            }
            // back onto the end of the first layer, unless the path goes on from exactly there
            Point3d home = tp.Points[lastOfFirst];
            if (lastOfFirst + 1 >= tp.Count || tp.Points[lastOfFirst + 1].DistanceTo(home) > 1e-6) result.Add(home);
            return result;
        }

        private static List<Point2d> ConvexHull(List<Point2d> pts)
        {
            var p = pts.OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
            if (p.Count < 3) return p;
            Func<Point2d, Point2d, Point2d, double> cross = (o, a, b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var hull = new List<Point2d>();
            foreach (var q in p)
            {
                while (hull.Count >= 2 && cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            int lower = hull.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && cross(hull[hull.Count - 2], hull[hull.Count - 1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>True if the straight line from the skirt to the start of the part keeps half a bead clear of the first layer (up to one bead before the start) and is at most 30 bead widths long.</summary>
        private static bool LinkClear(Point3d from, Toolpath tp, int lastOfFirst, double w)
        {
            Point3d to = tp.Points[0];
            double len = from.DistanceTo(to);
            if (len > 30 * w || len < 1e-6) return false;
            Vector3d dir = (to - from) / len;
            for (double t = 0; t <= len - w; t += 0.5 * w)
            {
                Point3d p = from + dir * t;
                for (int i = 1; i <= lastOfFirst && i < tp.Count; i++)
                {
                    if (tp.Flows[i] <= 0) continue;
                    Point3d a = tp.Points[i - 1], b = tp.Points[i];
                    Vector3d ab = b - a; double l2 = ab.SquareLength;
                    double u = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                    if ((a + ab * u).DistanceTo(p) < 0.5 * w) return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ check

        public class CheckResult
        {
            public List<Point3d> Unsupported = new List<Point3d>();
            public List<double> Offsets = new List<double>(); // per path point: sideways distance to the layer below
            public List<Point3d> Crossings = new List<Point3d>(); // where the path crosses itself within a layer
            public List<double> LayerTimes = new List<double>();   // seconds per layer, in print order
            /// <summary>Print speed (mm/min) at which the shortest layer reaches Min Layer Time; 0 = not needed or not checked.</summary>
            public double SuggestedSpeed;
            public List<string> Report = new List<string>();
            public double MaxOffset;
            public bool Ok = true;
        }

        /// <summary>
        /// Checks whether the path can be printed.
        ///
        /// Support: a bead is only held if it lies on the bead below. The path is the middle of
        /// the bead and the bead reaches half its width to either side, so a path point may sit at
        /// most half a bead width beside the path of the layer below. Points further out hang in
        /// the air. The first layer lies on the bed and is not checked.
        /// </summary>
        public static CheckResult Check(Toolpath tp, double maxOffset, double minLayerTime = 0)
        {
            var r = new CheckResult();
            Toolpath real = tp;
            // Follow the surface: the laps lean on the lap below where the surface turns flat (under the
            // chin, the crown), and the extrusion follows the law of the printed head there
            bool leaning = tp.Warp != null && tp.Warp.UseBeadLaw;
            if (tp.Warp != null)
            {
                // non-planar / multi-axis: test in flat space, where layers are planes again
                var flat = tp.Duplicate();
                flat.Warp = null;
                for (int i = 0; i < flat.Count; i++)
                {
                    Point3d f = tp.Warp.ToFlat(flat.Points[i]);
                    flat.Heights[i] = flat.Heights[i] / tp.Warp.Scale(f);
                    flat.Points[i] = f;
                    flat.Normals[i] = Vector3d.ZAxis;
                }
                tp = flat;
            }
            var st = tp.Settings;
            double limit = maxOffset > 0 ? maxOffset : 0.5 * st.BeadWidth;
            int n = tp.Count;
            for (int i = 0; i < n; i++) r.Offsets.Add(0);
            if (n < 2) { r.Report.Add("Empty path."); r.Ok = false; return r; }

            // the way in from above, before the first printed move, is not part of the print
            int firstPrint = 1;
            while (firstPrint < n && tp.Flows[firstPrint] <= 0) firstPrint++;
            firstPrint = Math.Max(0, firstPrint - 1);

            // point indices per layer, in print order
            var byLayer = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (tp.Roles[i] == PathRole.Skirt || i < firstPrint) continue;
                if (!byLayer.TryGetValue(tp.Layers[i], out var list)) byLayer[tp.Layers[i]] = list = new List<int>();
                list.Add(i);
            }

            List<Point2d[]> below = null; bool belowSolid = false;
            double bedZ = double.MaxValue;
            for (int i = 0; i < n; i++) if (tp.Flows[i] > 0 && tp.Roles[i] != PathRole.Skirt && tp.Points[i].Z < bedZ) bedZ = tp.Points[i].Z;
            Plane plane = Plane.WorldXY;
            bool havePlane = false;
            int unsupported = 0, checkedPoints = 0;
            var hangLayers = new List<int>(); // layers on which a good part of the bead hangs in the air
            foreach (var kv in byLayer)
            {
                var idx = kv.Value;
                Vector3d axis = tp.Normals[idx[0]];
                if (!havePlane) { plane = Geo.PlaneAt(tp.Points[idx[0]], axis); havePlane = true; }

                // this layer as 2D segments (what the next layer will rest on)
                var segs = new List<Point2d[]>(idx.Count);
                for (int k = 1; k < idx.Count; k++)
                {
                    int a = idx[k - 1], b = idx[k];
                    if (b != a + 1 || tp.Flows[b] <= 0) continue;
                    segs.Add(new[] { Flat(plane, tp.Points[a]), Flat(plane, tp.Points[b]) });
                }

                int hangCount = 0;
                if (below != null && below.Count > 0)
                {
                    int hint = 0;
                    foreach (int i in idx)
                    {
                        if (tp.Flows[i] <= 0) continue;
                        // infill on top of a solid bottom layer lies on material everywhere
                        if (belowSolid && tp.Roles[i] == PathRole.Infill) { checkedPoints++; continue; }
                        // tilted layers stand on the bed along their lowest edge: what touches the bed is supported
                        if (Math.Abs(axis.Z) < 0.999 && tp.Warp == null && tp.Points[i].Z <= bedZ + st.LayerHeight + 0.5 * st.BeadWidth) { checkedPoints++; continue; }
                        Point2d p = Flat(plane, tp.Points[i]);
                        double d = Nearest(below, p, ref hint, 40);
                        if (d > limit) { int full = -1; d = Nearest(below, p, ref full, int.MaxValue); if (full >= 0) hint = full; }
                        r.Offsets[i] = d;
                        checkedPoints++;
                        if (d > r.MaxOffset) r.MaxOffset = d;
                        if (d > limit) unsupported++;
                        // shown as unsupported: clearly off the bead below (more than 1.5 x the limit)
                        if (d > 1.5 * limit) { r.Unsupported.Add(tp.Points[i]); hangCount++; }
                    }
                }
                if (hangCount > Math.Max(3, idx.Count / 50)) hangLayers.Add(kv.Key);
                if (segs.Count > 0) { below = segs; belowSolid = idx.Any(i => tp.Roles[i] == PathRole.Bottom && tp.Flows[i] > 0); }
            }

            // ---- the path must never cross itself within a layer
            foreach (var kv in byLayer)
            {
                var idx = kv.Value;
                Vector3d axis = tp.Normals[idx[0]];
                double h = Math.Max(0.1, st.LayerHeight);
                var segA = new List<int>(); // index of the end point of every printing move in this layer
                for (int k = 1; k < idx.Count; k++)
                    if (idx[k] == idx[k - 1] + 1 && tp.Flows[idx[k]] > 0) segA.Add(idx[k]);
                if (segA.Count < 3) continue;
                double cell = Math.Max(2 * st.BeadWidth, 5);
                var grid = new Dictionary<long, List<int>>();
                foreach (int e in segA)
                {
                    Point2d p = Flat(plane, tp.Points[e - 1]), q = Flat(plane, tp.Points[e]);
                    int x0 = (int)Math.Floor(Math.Min(p.X, q.X) / cell), x1 = (int)Math.Floor(Math.Max(p.X, q.X) / cell);
                    int y0 = (int)Math.Floor(Math.Min(p.Y, q.Y) / cell), y1 = (int)Math.Floor(Math.Max(p.Y, q.Y) / cell);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 400) { x1 = x0; y1 = y0; } // very long move: register once
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long key = ((long)gx << 32) ^ (uint)gy;
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                            foreach (int o in list)
                            {
                                if (Math.Abs(o - e) <= 2) continue; // neighbours share a point
                                Point2d a = Flat(plane, tp.Points[o - 1]), b = Flat(plane, tp.Points[o]);
                                double d1 = Cross(a, b, p), d2 = Cross(a, b, q), d3 = Cross(p, q, a), d4 = Cross(p, q, b);
                                if (d1 * d2 >= 0 || d3 * d4 >= 0) continue;
                                // Two moves that run almost along each other do not cross; that is the path
                                // turning back over itself at a layer change, one above the other.
                                Vector2d u1 = q - p, u2 = b - a;
                                // The closing pass after the last ramp runs over the start of its own layer
                                // on purpose; after rounding the two are not exactly the same line.
                                bool seamPass = tp.Roles[e] == PathRole.Seam || tp.Roles[o] == PathRole.Seam;
                                if (Math.Abs(u1.X * u2.Y - u1.Y * u2.X) < (seamPass ? 0.5 : 0.17) * u1.Length * u2.Length) continue; // under ~10 degrees (30 on the seam)
                                // same height? (the last flat loop of a spiral lies above the loop below it)
                                double zHere = (tp.Points[e] - plane.Origin) * axis, zThere = (tp.Points[o] - plane.Origin) * axis;
                                if (Math.Abs(zHere - zThere) > 0.25 * h) continue;
                                Point3d hit = tp.Points[e - 1] + (tp.Points[e] - tp.Points[e - 1]) * (d3 / (d3 - d4));
                                // both moves end within a few tenths of a millimetre of the hit: they touch end to
                                // end (the seam closing onto its own start after thinning), no bead is ploughed
                                double touch = 0.1 * st.BeadWidth;
                                if (Math.Min(hit.DistanceTo(tp.Points[e - 1]), hit.DistanceTo(tp.Points[e])) < touch
                                    && Math.Min(hit.DistanceTo(tp.Points[o - 1]), hit.DistanceTo(tp.Points[o])) < touch) continue;
                                if (r.Crossings.Count == 0 || r.Crossings[r.Crossings.Count - 1].DistanceTo(hit) > 1e-6) r.Crossings.Add(hit);
                            }
                            list.Add(e);
                        }
                }
            }
            if (r.Crossings.Count == 0) r.Report.Add("No crossings: the path never runs across itself within a layer.");
            else
            {
                r.Ok = false;
                r.Report.Add(Inv.F($"CROSSES ITSELF: the path runs across its own bead at {r.Crossings.Count} place(s). The nozzle would plough through material it has just laid. See the Crossings output."));
            }

            // ---- report
            if (unsupported == 0)
                r.Report.Add(Inv.F($"Support OK: every bead lies on the layer below (largest sideways offset {r.MaxOffset:0.0} mm, allowed {limit:0.0} mm = half the bead width)."));
            else if (leaning && maxOffset <= 0 && r.MaxOffset <= 2.0 * limit + 0.05 && unsupported <= 0.025 * Math.Max(1, checkedPoints))
            {
                // Calibrated on the UNIKAT HEAD V2 (Ginger One, 2024), which printed fine: 336 of its 18 177
                // points (1.8 %) lie 2 to 4 mm (up to one bead width) beside the lap below, under the chin
                // and on the crown, where the rings lean on the ring below and the law gives them less material.
                r.Report.Add(Inv.F($"Support OK for leaning laps: {unsupported} of {checkedPoints} points ({100.0 * unsupported / Math.Max(1, checkedPoints):0.#} %) lie more than {limit:0.0} mm beside the lap below (up to {r.MaxOffset:0.0} mm), where the surface turns flat (under the chin, at the crown). ") +
                             Inv.F($"Accepted up to one bead width ({2 * limit:0.0} mm) at up to 2.5 % of the points: the printed head of 2024 had 1.8 % of its points up to 4.0 mm beside the lap below and printed fine. Use the Offset output to see where."));
            }
            else if ((r.MaxOffset <= 1.75 * limit && unsupported <= 0.01 * Math.Max(1, checkedPoints)) || (hangLayers.Count == 0 && r.Unsupported.Count <= Math.Max(8, checkedPoints / 2000)))
            {
                // a handful of points just over the limit: the bead still rests on most of its width
                r.Report.Add(Inv.F($"Support OK with a note: {unsupported} of {checkedPoints} points lie slightly more than {limit:0.0} mm beside the layer below (up to {r.MaxOffset:0.0} mm). ") +
                             "They still rest on part of the bead below - typically where ribs start on the bottom layer or two rib tips meet. Use the Offset output to see where.");
            }
            else
            {
                r.Ok = false;
                r.Report.Add(Inv.F($"NOT SUPPORTED: {unsupported} of {checkedPoints} points lie more than {limit:0.0} mm beside the layer below (largest offset {r.MaxOffset:0.0} mm). ") +
                             "These beads hang in the air. Use a wider bead, a lower layer height, a tilted slicing plane or more ribs.");
                if (hangLayers.Count > 0 && st.LayerHeight > 0)
                {
                    // where, and what it means as an overhang angle
                    var ranges = new List<string>();
                    int a0 = hangLayers[0], b0 = a0;
                    for (int k = 1; k <= hangLayers.Count; k++)
                    {
                        if (k < hangLayers.Count && hangLayers[k] <= b0 + 2) { b0 = hangLayers[k]; continue; }
                        ranges.Add(a0 == b0 ? (a0 + 1).ToString() : (a0 + 1) + "-" + (b0 + 1));
                        if (k < hangLayers.Count) { a0 = hangLayers[k]; b0 = a0; }
                    }
                    double allowed = Math.Atan(limit / st.LayerHeight) * 180.0 / Math.PI;
                    double found = Math.Atan(r.MaxOffset / st.LayerHeight) * 180.0 / Math.PI;
                    r.Report.Add(Inv.F($"Overhang: the wall leans up to {found:0} degrees from vertical. With a {st.BeadWidth:0.#} mm bead and {st.LayerHeight:0.#} mm layers about {allowed:0} degrees is the limit. ") +
                                 Inv.F($"Affected layers: {string.Join(", ", ranges.Take(8))}{(ranges.Count > 8 ? " ..." : "")}. ") +
                                 "Domes, spheres and flat tops close faster than that: slice them with a Guide surface (non-planar) or a tilted plane, or cut the part where it gets too flat.");
                }
            }

            // ---- numbers the machine can read: no NaN, no move of length 0
            {
                int badNumber = -1, zero = 0;
                for (int i = 0; i < real.Count; i++)
                {
                    if (badNumber < 0 && (!real.Points[i].IsValid || !real.Normals[i].IsValid || double.IsNaN(real.Heights[i]) || double.IsNaN(real.Widths[i]) || double.IsNaN(real.Flows[i]))) badNumber = i;
                    if (i > 0 && real.Points[i].DistanceTo(real.Points[i - 1]) < 1e-6) zero++;
                }
                if (badNumber >= 0) { r.Ok = false; r.Report.Add(Inv.F($"INVALID NUMBERS: point {badNumber + 1} has no valid position, tool axis, height or width. The file would stop the machine. Check the geometry around there.")); }
                if (zero > 0) r.Report.Add(Inv.F($"Note: {zero} move(s) of length 0 (the same point twice). Start and End removes them; a path made by hand should not have them, some controllers stop on them."));
            }

            // ---- the bed is Z 0: the nozzle must run a layer height above it, not on it
            {
                int low = -1;
                for (int i = firstPrint; i < real.Count; i++)
                    if (real.Flows[i] > 0 && real.Normals[i].Z > 0.999 && (low < 0 || real.Points[i].Z < real.Points[low].Z)) low = i; // tilted beads stand on their edge
                if (low >= 0)
                {
                    double z = real.Points[low].Z, hb = real.Heights.Count > low && real.Heights[low] > 0 ? real.Heights[low] : st.LayerHeight;
                    if (z < 0.5 * hb)
                    {
                        r.Ok = false;
                        r.Report.Add(z <= 0.01
                            ? Inv.F($"INTO THE BED: the nozzle extrudes at Z {z:0.##}, on or below the bed (Z 0). The first layer must lie one layer height above the bed: put the part's bottom on Z 0, or lift curves you drew on the bed by the first layer height.")
                            : Inv.F($"TOO LOW: the lowest bead is printed at Z {z:0.##}, less than half its height ({hb:0.##} mm) above the bed (Z 0). The nozzle would press it into the bed: put the part's bottom on Z 0 or set First Layer Height."));
                    }
                }
            }

            // ---- non-planar and multi-axis: the bead gets thinner where the layers lie closer together
            if (real.Warp != null)
            {
                double thin = double.MaxValue, thick = 0; int thinCount = 0, printed = 0;
                for (int i = firstPrint; i < real.Count; i++)
                {
                    if (real.Flows[i] <= 0 || real.Roles[i] == PathRole.Seam || real.Roles[i] == PathRole.Skirt) continue;
                    double hh = real.Heights[i];
                    printed++;
                    if (hh < thin) thin = hh;
                    if (hh > thick) thick = hh;
                    if (hh < 0.4 * st.LayerHeight) thinCount++;
                }
                if (printed > 0)
                    r.Report.Add(thinCount > 0
                        ? Inv.F($"Bead height: the layers are {thin:0.##} to {thick:0.##} mm thick; at {thinCount} of {printed} points less than 40 % of the layer height. The extrusion follows the thickness, but beads this thin are pressed flat and may not bond: a flatter guide, or a guide closer to the part's top, gives more even layers.")
                        : Inv.F($"Bead height OK: the layers are {thin:0.##} to {thick:0.##} mm thick; the extrusion follows the thickness at every point."));
            }

            double ratio = st.LayerHeight / st.BeadWidth;
            if (ratio > 0.8)
            {
                r.Ok = false;
                r.Report.Add(Inv.F($"Bead shape: layer height is {ratio:P0} of the bead width. Above about 80 % the bead is hardly squashed and bonds poorly."));
            }
            else if (ratio < 0.15)
                r.Report.Add(Inv.F($"Bead shape: layer height is only {ratio:P0} of the bead width. Very flat beads need a lot of pressure; check that the extruder can deliver this."));
            else
                r.Report.Add(Inv.F($"Bead shape OK: layer height is {ratio:P0} of the bead width."));

            int travels = 0, tiny = 0; double travelLength = 0;
            for (int i = firstPrint + 1; i < n; i++)
            {
                double len = tp.Points[i].DistanceTo(tp.Points[i - 1]);
                if (tp.Flows[i] <= 0) { travelLength += len; if (tp.Flows[i - 1] > 0 || i == firstPrint + 1) travels++; }
                else if (len < 0.05) tiny++;
            }
            r.Report.Add(travels == 0 ? "Continuous: no travel moves." : Inv.F($"Travel: {travels} move(s) without extrusion, {travelLength:0} mm in total."));
            if (tiny > n / 20) r.Report.Add(Inv.F($"{tiny} moves are shorter than 0.05 mm. Raise Resolution in the Print Settings to thin out the path."));
            if (st.Speed <= 0 || st.MinSpeed <= 0)
            {
                r.Ok = false;
                r.Report.Add("Speed is 0. A move with F0 never finishes, the machine would stop at that line.");
            }
            else LayerTime(real, minLayerTime, r);
            return r;
        }

        /// <summary>
        /// Layer time and extruder output. A layer printed too fast lands on a layer that is still
        /// soft, so overhangs sag and the part warps (Weber). The time per layer comes from the
        /// speeds the machine gets (Process: the print speed, short layers slowed down when Min Layer
        /// Time is set in the Print Settings, capped by Max Volume Rate). Below the minimum the
        /// report says what to do: print slower, print several parts side by side, or, as a last
        /// resort, wait on every short layer. minSeconds = 0: the value of the Print Settings; both
        /// 0 = only the times are reported.
        /// </summary>
        private static void LayerTime(Toolpath tp, double minSeconds, CheckResult r)
        {
            var st = tp.Settings;
            var pr = PathEngine.Process(tp, false);
            if (pr.LayerSeconds.Count == 0) return;
            if (minSeconds <= 0) minSeconds = st.MinLayerTime;
            r.LayerTimes.AddRange(pr.LayerSeconds.Values);
            int shortLayer = pr.LayerSeconds.First().Key, longLayer = shortLayer;
            foreach (var kv in pr.LayerSeconds)
            {
                if (kv.Value < pr.LayerSeconds[shortLayer]) shortLayer = kv.Key;
                if (kv.Value > pr.LayerSeconds[longLayer]) longLayer = kv.Key;
            }
            double shortest = pr.LayerSeconds[shortLayer];

            // ---- extruder output
            if (st.MaxVolumeRate > 0)
            {
                if (pr.VolumeOverLimit > 0)
                    r.Report.Add(Inv.F($"VOLUME RATE: {pr.VolumeOverLimit} move(s) need more than the extruder's {st.MaxVolumeRate:0} mm3/s even at Min Speed {st.MinSpeed / 60:0.#} mm/s (up to {pr.MaxVolumeRate:0} mm3/s): the bead gets thin there. Use a lower layer height or bead width, or a lower Min Speed."));
                else
                    r.Report.Add(Inv.F($"Volume rate OK: at most {pr.MaxVolumeRate:0} mm3/s, the extruder gives {st.MaxVolumeRate:0} mm3/s") + (pr.VolumeCapped > 0 ? Inv.F($" ({pr.VolumeCapped} move(s) run slower to stay below it).") : "."));
            }
            else
                r.Report.Add(Inv.F($"Volume rate: at most {pr.MaxVolumeRate:0} mm3/s. Set Max Volume Rate (Continuous Path) to what your extruder delivers, and moves that ask for more run slower."));

            string slowed = "";
            if (pr.LayerFeed.Count > 0)
            {
                var low = pr.LayerFeed.OrderBy(kv => kv.Value).First();
                slowed = Inv.F($"{pr.LayerFeed.Count} short layer(s) are printed slower to reach {st.MinLayerTime:0} s") + (st.ShortLapTime > 0 ? Inv.F($" ({st.ShortLapTime:0} s on short laps)") : "") + Inv.F($" (down to {low.Value / 60:0.#} mm/s at layer {low.Key + 1}). ");
            }
            if (minSeconds <= 0)
            {
                r.Report.Add(Inv.F($"Layer time: {shortest:0} s (layer {shortLayer + 1}) to {pr.LayerSeconds[longLayer]:0} s per layer at {st.Speed / 60:0.#} mm/s. ") +
                             "Set Min Layer Time (for example 60 s for a large pellet print) to check that every layer has cooled enough before the next one lands on it.");
                return;
            }
            // with Short Lap Time every layer has its own minimum (the one Process slowed it down to)
            var targets = st.ShortLapTime > 0 && Math.Abs(minSeconds - st.MinLayerTime) < 1e-9 ? PathEngine.LayerTargets(tp) : null;
            int tooShort = pr.LayerSeconds.Count(kv => kv.Value < (targets != null && targets.TryGetValue(kv.Key, out double tk) ? tk : minSeconds) - 0.5);
            if (tooShort == 0)
            {
                r.Report.Add(Inv.F($"Layer time OK: {slowed}Every layer takes at least {minSeconds:0} s (shortest {shortest:0} s at layer {shortLayer + 1})."));
                return;
            }

            var s = new System.Text.StringBuilder();
            s.Append(Inv.F($"LAYER TIME: {slowed}{tooShort} layer(s) take less than {minSeconds:0} s (shortest {shortest:0} s at layer {shortLayer + 1}). ") +
                     "The layer below is still soft there: overhangs can sag and the part can warp. What helps, best first: ");
            // 1) slower: the printing moves take longer, travel moves stay as they are
            pr.LayerPrintSeconds.TryGetValue(shortLayer, out double printing);
            double other = shortest - printing;
            double now = pr.LayerFeed.TryGetValue(shortLayer, out double f0) ? f0 : st.Speed;
            double need = printing > 1e-9 && minSeconds > other ? now * printing / (minSeconds - other) : 0;
            if (need > 0)
            {
                double feed = Math.Max(st.MinSpeed, Math.Floor(need / 10) * 10); // rounded down: then the layer is at least that long
                double reached = other + printing * now / feed;
                r.SuggestedSpeed = feed;
                if (need < st.MinSpeed || now <= st.MinSpeed + 1e-9)
                    s.Append(Inv.F($"(1) Slower helps only in part: even at the lowest speed {st.MinSpeed / 60:0.#} mm/s (Min Speed in the Print Settings) the shortest layer takes {reached:0} s. Combine it with (2) or (3). "));
                else if (st.MinLayerTime <= 0)
                    s.Append(Inv.F($"(1) Slower: set Min Layer Time to {minSeconds:0} s in Continuous Path, and only the short layers are printed slower (the shortest at {feed / 60:0.#} mm/s instead of {st.Speed / 60:0.#} mm/s, then {reached:0} s); or print everything at {feed / 60:0.#} mm/s (Speed in the Print Settings). "));
                else
                    s.Append(Inv.F($"(1) Slower: print the short layers at {feed / 60:0.#} mm/s; the shortest then takes {reached:0} s. "));
            }
            // 2) more parts at once: the nozzle prints the others while one cools
            int copies = (int)Math.Ceiling(minSeconds / Math.Max(1e-3, shortest));
            s.Append(Inv.F($"(2) Print {copies} parts side by side in one path: each layer then takes about {copies * shortest:0} s, and you get {copies} parts in that time. "));
            // 3) pause: last resort
            s.Append(Inv.F($"(3) Last resort: wait {minSeconds - shortest:0} s on the short layers (a dwell, G4). On a pellet extruder the screw stops during the wait: the melt keeps oozing, so the bead gets thin where it stops and a thread or blob is left at the seam when it starts again."));
            r.Report.Add(s.ToString());
        }

        private static double Cross(Point2d a, Point2d b, Point2d c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static Point2d Flat(Plane plane, Point3d p)
        {
            Vector3d v = p - plane.Origin;
            return new Point2d(v * plane.XAxis, v * plane.YAxis);
        }

        private static double Nearest(List<Point2d[]> segs, Point2d p, ref int hint, int window)
        {
            int from = 0, to = segs.Count;
            if (hint >= 0 && window < segs.Count) { from = Math.Max(0, hint - window); to = Math.Min(segs.Count, hint + window); }
            double best = double.MaxValue; int bi = hint;
            for (int i = from; i < to; i++)
            {
                Point2d a = segs[i][0], b = segs[i][1];
                Vector2d ab = b - a;
                double l2 = ab.SquareLength;
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
                double d = (a + ab * t).DistanceTo(p);
                if (d < best) { best = d; bi = i; }
            }
            hint = bi;
            return best;
        }
    }
}
