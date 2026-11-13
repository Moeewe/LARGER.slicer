using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Support fins under overhangs, printed as part of the one continuous line.
    ///
    /// A fin is a thin wall that stands outside the part, square to its outer wall, from the bed
    /// up to the layer just under the overhang. Like a rib it is a hairpin: the path leaves the
    /// outer wall, runs out to the tip, turns and comes back one bead spacing beside itself, so
    /// the line never stops and the two legs fuse into one fin. The fin of every layer lies on
    /// exactly the same line as the fin below, so it always stands on it.
    ///
    /// Removal: by default the two legs leave the wall a quarter bead apart and only open to the
    /// full bead spacing one bead width further out. The fin then touches the part through a
    /// neck about one bead wide, which is easy to cut. Optionally the top fin layer is printed a
    /// little lower (the gap), so the overhang rests on it without fusing and breaks off.
    ///
    /// Every dimension comes from the bead width. Run it after Walls (and after Bottom Fill and
    /// infill), right before Continuous Path.
    /// </summary>
    public static class SupportFins
    {
        /// <summary>How the fin meets the part.</summary>
        public enum Contact
        {
            /// <summary>Narrow neck at the wall (about one bead), easy to cut.</summary>
            Neck = 0,
            /// <summary>Neck, and the top fin layer is printed lower by the gap: breaks off.</summary>
            NeckAndGap = 1,
            /// <summary>Both legs meet the wall at full bead spacing: strongest, hardest to remove.</summary>
            Full = 2
        }

        private class Fin
        {
            public int Id;
            public Point2d P;     // root on the layer where the fin was laid out
            public Vector2d D;    // outward, square to the wall there
            public Vector2d N;    // D turned left
            public readonly Dictionary<int, double> Plan = new Dictionary<int, double>();  // layer -> planned tip (distance along D from P)
            public readonly Dictionary<int, double> Built = new Dictionary<int, double>(); // layer -> tip as printed
        }

        private class Piece
        {
            public Fin Fin;
            public int Contour;            // index of the wall contour it is spliced into
            public double SA, SB;          // where the legs leave the wall (arc length on the ring)
            public List<Point2d> Inner;    // points between the two feet: neck, tip, tip, neck
            public List<Point2d> Outline;  // feet + inner, for clearance and support tests
        }

        private class Wall
        {
            public int Contour;
            public List<Point2d> Ring;
            public double[] Cum;
        }

        /// <summary>
        /// Adds support fins to the layers.
        ///  angleDeg: steepest overhang (from vertical) that prints without support. 0 = half a bead width per layer.
        ///  spacingBeads: fin distance along the overhang edge, in bead widths (measured where it is widest, i.e. at the overhang outline).
        ///  marginBeads: how far the fin reaches beyond the overhanging wall, in bead widths.
        ///  gap: Contact.NeckAndGap only: how much lower the top fin layer is printed (mm).
        /// Returns new layers; the input is not changed.
        /// </summary>
        public static List<SliceLayer> Add(List<SliceLayer> layers, PrintSettings settings, double angleDeg, double spacingBeads,
            double marginBeads, Contact contact, double gap, List<string> log, out int finCount, out List<Polyline> finPaths)
        {
            finCount = 0;
            finPaths = new List<Polyline>();
            var ordered = (layers ?? new List<SliceLayer>()).Where(l => l != null).OrderBy(l => l.Index).ToList();
            int L = ordered.Count;
            if (L == 0) return ordered;

            double w = settings.BeadWidth, spacing = settings.BeadSpacing, g = 0.5 * spacing;
            double finSpacing = Math.Max(3, spacingBeads) * w;
            double margin = Math.Max(0, marginBeads) * w;
            double halfFoot = contact == Contact.Full ? g : 0.125 * w; // half the distance of the two feet
            gap = Math.Max(0, Math.Min(0.5 * settings.LayerHeight, gap));
            if (ordered.Any(l => l.Rings == null)) log?.Add("Run Walls first: the fins are joined into the outer wall.");

            // ---- the outer walls of every layer (ring 0), in 2D
            var walls = new List<Wall>[L];
            var rings = new List<List<Point2d>>[L];
            for (int i = 0; i < L; i++)
            {
                var layer = ordered[i];
                walls[i] = new List<Wall>();
                for (int c = 0; c < layer.Contours.Count; c++)
                {
                    var pl = layer.Contours[c];
                    if (!Geo.Closed(pl) || pl.Count < 4) continue;
                    if (layer.Rings != null && c < layer.Rings.Count && layer.Rings[c] != 0) continue;
                    var ring = new List<Point2d>(pl.Count - 1);
                    for (int k = 0; k < pl.Count - 1; k++) ring.Add(To2d(layer.Plane, pl[k]));
                    walls[i].Add(new Wall { Contour = c, Ring = ring, Cum = FillEngine.Cumulative(ring) });
                }
                rings[i] = walls[i].Select(x => x.Ring).ToList();
            }

            // ---- 1) plan, from the top down. A fin starts under the first layer that overhangs and
            // runs down to the bed. Further down it keeps its line; its tip only ever grows, so every
            // layer of the fin lies on the layer below.
            var fins = new List<Fin>();
            var active = new List<Fin>();
            int landed = 0, lost = 0;
            for (int i = L - 2; i >= 0; i--)
            {
                int j = i + 1;
                double h = ordered[j].Height > 0 ? ordered[j].Height : settings.LayerHeight;
                double allowed = angleDeg > 0 && angleDeg < 89 ? h * Math.Tan(angleDeg * Math.PI / 180.0) : 0.5 * w;
                if (rings[i].Count == 0) { lost += active.Count; active.Clear(); continue; }

                var roots = new Dictionary<Fin, double>();
                foreach (var fin in active.ToList())
                {
                    double tip = fin.Plan[j];
                    int r = Root(walls[i], rings[i], fin.P, fin.D, tip, w, out double uRoot, out _, out _);
                    if (r <= 0)
                    {
                        if (r == 0) landed++; else lost++;
                        active.Remove(fin);
                        continue;
                    }
                    double exit = Exit(rings[j], fin.P, fin.D, uRoot, w);
                    if (exit - uRoot > allowed + 0.1 * w) tip = Math.Max(tip, exit + margin);
                    fin.Plan[i] = tip;
                    roots[fin] = uRoot;
                }

                // overhang of layer j over layer i: new fins where none stand yet
                foreach (var wall in walls[i])
                {
                    foreach (var cand in Candidates(wall, rings[i], rings[j], allowed, w, spacing, finSpacing))
                    {
                        Point2d p = cand.P; Vector2d d = cand.D;
                        double exit = Exit(rings[j], p, d, 0, w);
                        if (exit <= allowed + 0.1 * w) continue;
                        double tip = exit + margin;
                        bool near = false;
                        foreach (var other in active)
                        {
                            double ur = roots.TryGetValue(other, out double v) ? v : 0;
                            if (SegmentDistance(p, p + d * tip, other.P + other.D * ur, other.P + other.D * other.Plan[i]) < 2.5 * spacing + 1e-6 ||
                                p.DistanceTo(other.P + other.D * ur) < cand.Clear) { near = true; break; }
                        }
                        if (near) continue;
                        var fin = new Fin { Id = fins.Count, P = p, D = d, N = new Vector2d(-d.Y, d.X) };
                        fin.Plan[i] = tip;
                        fins.Add(fin); active.Add(fin);
                        roots[fin] = 0;
                    }
                }
            }

            // ---- 2) build, from the bed up. Every piece must stand on the layer below (at most half a
            // bead beside it) and keep one bead spacing away from the part and from other fins; a
            // piece that cannot is cut back, and so is everything above it. No fin floats.
            var pieces = new List<Piece>[L];
            List<Point2d[]> below = null;
            int trimmed = 0, dropped = 0;
            for (int i = 0; i < L; i++)
            {
                pieces[i] = new List<Piece>();
                var layer = ordered[i];
                var obstacles = Segments(layer.Contours.Select(c => c.Select(p => To2d(layer.Plane, p)).ToList()), false);
                foreach (var fin in fins)
                {
                    if (!fin.Plan.TryGetValue(i, out double tip)) continue;
                    int r = Root(walls[i], rings[i], fin.P, fin.D, tip, w, out double uRoot, out int wallIndex, out _);
                    if (r <= 0) { dropped++; continue; }
                    var wall = walls[i][wallIndex];
                    // where the two legs leave the wall
                    if (!Foot(wall.Ring, fin.P + fin.N * (-halfFoot), fin.D, uRoot, w, out double uA, out Point2d footA) ||
                        !Foot(wall.Ring, fin.P + fin.N * halfFoot, fin.D, uRoot, w, out double uB, out Point2d footB)) { dropped++; continue; }
                    double uFoot = Math.Max(uA, uB);
                    double uNeck = contact == Contact.Full ? uFoot : uFoot + w;

                    Func<double, double> side = u => u >= uNeck ? g : halfFoot + (g - halfFoot) * Math.Max(0, (u - uFoot) / Math.Max(1e-9, uNeck - uFoot));
                    double step = 0.5 * w, end = tip;
                    bool cut = false;
                    for (double u = uFoot + 0.25 * w; ; u += step)
                    {
                        if (u > tip) u = tip;
                        bool ok = true;
                        for (int sgn = -1; sgn <= 1 && ok; sgn += 2)
                        {
                            Point2d q = fin.P + fin.D * u + fin.N * (sgn * side(u));
                            if (u > uFoot + 1.5 * w)
                            {
                                if (Nearest(obstacles, q) < 0.9 * spacing) ok = false;
                                foreach (var other in pieces[i]) if (ok && PolylineDistance(other.Outline, q) < 0.9 * spacing) ok = false;
                            }
                            if (ok && i > 0 && (below == null || Nearest(below, q) > 0.5 * w)) ok = false;
                        }
                        if (ok && u >= tip - 1e-9)
                        {
                            // the turn at the tip reaches half a bead spacing further
                            Point2d t = fin.P + fin.D * (u + g);
                            if (Nearest(obstacles, t) < 0.9 * spacing) ok = false;
                            foreach (var other in pieces[i]) if (ok && PolylineDistance(other.Outline, t) < 0.9 * spacing) ok = false;
                        }
                        if (!ok) { end = u - step; cut = true; break; }
                        if (u >= tip - 1e-9) break;
                    }
                    if (end - uNeck < w) { dropped++; continue; }
                    if (cut) trimmed++;

                    var inner = new List<Point2d>();
                    if (contact != Contact.Full) inner.Add(fin.P + fin.D * uNeck - fin.N * g);
                    inner.Add(fin.P + fin.D * end - fin.N * g);
                    inner.Add(fin.P + fin.D * end + fin.N * g);
                    if (contact != Contact.Full) inner.Add(fin.P + fin.D * uNeck + fin.N * g);
                    var outline = new List<Point2d> { footA };
                    outline.AddRange(inner);
                    outline.Add(footB);
                    pieces[i].Add(new Piece
                    {
                        Fin = fin, Contour = wall.Contour, Inner = inner, Outline = outline,
                        SA = FillEngine.ClosestS(wall.Ring, wall.Cum, footA, out _),
                        SB = FillEngine.ClosestS(wall.Ring, wall.Cum, footB, out _)
                    });
                    fin.Built[i] = end;
                }

                // what the next layer stands on: this layer's paths and fins
                var all = layer.Contours.Select(c => c.Select(p => To2d(layer.Plane, p)).ToList()).ToList();
                all.AddRange(pieces[i].Select(pc => pc.Outline));
                below = Segments(all, false);
            }

            // ---- 3) splice the fins into the outer walls
            var result = new List<SliceLayer>(L);
            int pieceCount = 0, gapped = 0;
            for (int i = 0; i < L; i++)
            {
                var src = ordered[i];
                var dst = new SliceLayer { Index = src.Index, Plane = src.Plane, Height = src.Height, Warp = src.Warp };
                dst.Rings = src.Rings != null ? new List<int>(src.Rings) : null;
                dst.Reference = src.Reference ?? src.Contours.Select(c => new Polyline(c)).ToList();
                dst.Contours.AddRange(src.Contours);
                dst.ContourRoles = src.ContourRoles;
                dst.EntryHints = src.EntryHints;
                result.Add(dst);
                if (pieces[i].Count == 0) continue;

                Vector3d up = src.Plane.ZAxis;
                foreach (var group in pieces[i].GroupBy(pc => pc.Contour))
                {
                    var wall = walls[i].First(x => x.Contour == group.Key);
                    double len = wall.Cum[wall.Ring.Count];
                    // order every piece along the ring; the path meets the first foot first
                    var list = new List<KeyValuePair<double[], Piece>>();
                    foreach (var pc in group)
                    {
                        double ab = Mod(pc.SB - pc.SA, len);
                        double from = ab < 0.5 * len ? pc.SA : pc.SB;
                        double span = ab < 0.5 * len ? ab : len - ab;
                        list.Add(new KeyValuePair<double[], Piece>(new[] { Mod(from, len), span, ab < 0.5 * len ? 1 : 0 }, pc));
                    }
                    list = list.OrderBy(x => x.Key[0]).ToList();
                    // two feet must not overlap (fins are far apart, but stay safe)
                    for (int k = list.Count - 1; k > 0; k--)
                        if (list[k - 1].Key[0] + list[k - 1].Key[1] + 1e-6 >= list[k].Key[0]) { list[k].Value.Fin.Built.Remove(i); list.RemoveAt(k); dropped++; }

                    var pts = new List<Point3d>();
                    for (int k = 0; k < list.Count; k++)
                    {
                        var pc = list[k].Value;
                        double from = list[k].Key[0], to = from + list[k].Key[1];
                        var inner = list[k].Key[2] > 0 ? pc.Inner : Enumerable.Reverse(pc.Inner).ToList();
                        // breakaway: the top layer of the fin is printed lower, the overhang rests on it without fusing
                        bool lower = contact == Contact.NeckAndGap && gap > 0 && !pc.Fin.Plan.ContainsKey(i + 1) && i + 1 < L;
                        if (lower) gapped++;
                        var finPl = new Polyline { To3d(src.Plane, FillEngine.PointAt(wall.Ring, wall.Cum, from)) };
                        foreach (var q in inner)
                        {
                            Point3d p3 = To3d(src.Plane, q);
                            if (lower) p3 -= up * gap;
                            pts.Add(p3); finPl.Add(p3);
                        }
                        finPl.Add(To3d(src.Plane, FillEngine.PointAt(wall.Ring, wall.Cum, to)));
                        finPaths.Add(src.Warp != null ? src.Warp.ToReal(finPl, 4.0) : finPl);
                        double next = k + 1 < list.Count ? list[k + 1].Key[0] : list[0].Key[0] + len;
                        foreach (var q in FillEngine.Sub(wall.Ring, wall.Cum, to, next)) pts.Add(To3d(src.Plane, q));
                        pieceCount++;
                    }
                    if (pts.Count < 3) continue;
                    var pl = new Polyline(pts);
                    pl.Add(pl[0]);
                    dst.Contours[group.Key] = pl;
                }
            }

            finCount = fins.Count(f => f.Built.Count > 0);
            if (fins.Count == 0) log?.Add("No overhang found: nothing needs fins.");
            else
            {
                int finLayers = pieces.Count(p => p.Count > 0);
                log?.Add(Inv.F($"{finCount} fin(s), {pieceCount} fin pieces in {finLayers} layers, leg spacing {spacing:0.##} mm."));
                if (gapped > 0) log?.Add(Inv.F($"Breakaway gap: {gapped} top fin piece(s) printed {gap:0.##} mm lower."));
                if (landed > 0) log?.Add(Inv.F($"{landed} fin(s) stand on the part below instead of on the bed."));
                if (lost > 0 || dropped > 0 || trimmed > 0)
                    log?.Add(Inv.F($"{lost + dropped} fin piece(s) left out and {trimmed} shortened: no wall to start from, no room next to the part or another fin, or nothing below to stand on. Fins above them are shortened too, so no fin floats."));
                if (fins.Count > finCount) log?.Add(Inv.F($"{fins.Count - finCount} planned fin(s) could not be printed at all."));
            }
            return result;
        }

        // ------------------------------------------------------------------ planning helpers

        /// <summary>
        /// Positions along one outer wall of layer i where the layer above reaches out further than
        /// allowed. Every overhanging stretch gets fins at about finSpacing apart, measured where
        /// the stretch is longest (usually along the overhang outline); a stretch with ends gets
        /// a fin near each end.
        /// </summary>
        private class Candidate
        {
            public Point2d P;     // on the wall
            public Vector2d D;    // outward
            public double Clear;  // no new fin if an existing one starts closer than this
        }

        private static List<Candidate> Candidates(Wall wall, List<List<Point2d>> here, List<List<Point2d>> above,
            double allowed, double w, double spacing, double finSpacing)
        {
            var result = new List<Candidate>();
            var ring = wall.Ring; var cum = wall.Cum;
            double len = cum[ring.Count];
            if (len < 6 * spacing || above.Count == 0) return result;
            int n = Math.Max(8, (int)Math.Ceiling(len / (0.5 * w)));
            var flag = new bool[n];
            var reachPt = new Point2d[n];
            int count = 0;
            for (int k = 0; k < n; k++)
            {
                double s = k * len / n;
                if (!Normal(ring, cum, here, s, spacing, out Point2d p, out Vector2d d)) continue;
                Point2d probe = p + d * (allowed + 0.1 * w);
                if (!Inside(above, probe) || Inside(here, probe)) continue;
                flag[k] = true; count++;
                reachPt[k] = p + d * Exit(above, p, d, 0, w);
            }
            if (count == 0) return result;

            var runs = new List<int[]>(); // first sample, number of samples
            if (count == n) runs.Add(new[] { 0, n });
            else
            {
                int start = 0;
                while (flag[start]) start++; // an unflagged sample to start from
                for (int t = 1; t <= n; t++)
                {
                    int k = (start + t) % n;
                    if (!flag[k]) continue;
                    int m = 0;
                    while (m < n && flag[(k + m) % n]) m++;
                    runs.Add(new[] { k, m });
                    t += m;
                }
            }

            foreach (var run in runs)
            {
                double ds = len / n;
                double rootLen = (run[1] - 1) * ds, tipLen = 0;
                for (int m = 1; m < run[1]; m++) tipLen += reachPt[(run[0] + m - 1) % n].DistanceTo(reachPt[(run[0] + m) % n]);
                double eff = Math.Max(rootLen, tipLen);
                // fins already there count if they start within half a fin spacing, measured at the wall
                double clear = Math.Max(2.5 * spacing, 0.5 * finSpacing * Math.Max(ds, rootLen) / Math.Max(ds, eff));
                var at = new List<double>();
                if (run[1] == n)
                {
                    int c = Math.Max(3, (int)Math.Ceiling(eff / finSpacing - 1e-9));
                    // start where the wall is furthest in +X, so the layout does not depend on where the contour starts
                    double s0 = 0, best = double.MinValue;
                    for (int k = 0; k < ring.Count; k++) if (ring[k].X > best) { best = ring[k].X; s0 = cum[k]; }
                    for (int k = 0; k < c; k++) at.Add(s0 + k * len / c);
                }
                else
                {
                    double s0 = run[0] * ds;
                    double inset = Math.Min(2 * spacing, 0.5 * rootLen);
                    double usable = rootLen - 2 * inset;
                    if (eff < 0.5 * finSpacing || usable < spacing) at.Add(s0 + 0.5 * rootLen);
                    else
                    {
                        int c = Math.Max(2, (int)Math.Ceiling(eff / finSpacing - 1e-9) + 1);
                        for (int k = 0; k < c; k++) at.Add(s0 + inset + usable * k / (c - 1));
                    }
                }

                foreach (double s0 in at)
                {
                    // never on a corner: slide along the wall until it is straight under the fin
                    foreach (double shift in new[] { 0.0, spacing, -spacing, 2 * spacing, -2 * spacing })
                    {
                        double s = s0 + shift;
                        Point2d a = FillEngine.PointAt(ring, cum, s - spacing), m = FillEngine.PointAt(ring, cum, s), b = FillEngine.PointAt(ring, cum, s + spacing);
                        Vector2d v1 = m - a, v2 = b - m;
                        if (!Unit(ref v1) || !Unit(ref v2) || v1 * v2 < 0.94) continue;
                        if (!Normal(ring, cum, here, s, spacing, out Point2d p, out Vector2d d)) continue;
                        if (!Inside(above, p + d * (allowed + 0.1 * w))) continue;
                        result.Add(new Candidate { P = p, D = d, Clear = clear });
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>Point on the wall and the outward direction square to it.</summary>
        private static bool Normal(List<Point2d> ring, double[] cum, List<List<Point2d>> here, double s, double spacing, out Point2d p, out Vector2d d)
        {
            p = FillEngine.PointAt(ring, cum, s);
            Vector2d t = FillEngine.PointAt(ring, cum, s + spacing) - FillEngine.PointAt(ring, cum, s - spacing);
            d = Vector2d.Zero;
            if (!Unit(ref t)) return false;
            d = new Vector2d(t.Y, -t.X);
            double probe = 0.25 * spacing;
            bool outA = !Inside(here, p + d * probe), outB = !Inside(here, p - d * probe);
            if (!outA && outB) d = -d;
            else if (outA == outB) return false; // thin spot: no clear outside
            return true;
        }

        /// <summary>
        /// Where the fin line leaves the part on this layer: the outermost wall crossing below the
        /// tip, at which the line runs from material into the open. 1 = found, 0 = the tip lies in
        /// the part (the fin stands on it), -1 = no usable wall (none, or met at a slant over 60 degrees).
        /// </summary>
        private static int Root(List<Wall> walls, List<List<Point2d>> rings, Point2d P, Vector2d D, double tip, double w,
            out double uRoot, out int wallIndex, out Point2d at)
        {
            uRoot = 0; wallIndex = -1; at = P;
            if (rings.Count == 0) return -1;
            if (Inside(rings, P + D * tip)) return 0;
            double best = double.MinValue;
            for (int wi = 0; wi < walls.Count; wi++)
            {
                var r = walls[wi].Ring;
                for (int k = 0; k < r.Count; k++)
                {
                    Point2d a = r[k], b = r[(k + 1) % r.Count];
                    if (!LineSegment(P, D, a, b, out double u, out _)) continue;
                    if (u >= tip - 2 * w || u <= best) continue;
                    if (Inside(rings, P + D * (u + 0.2 * w)) || !Inside(rings, P + D * (u - 0.2 * w))) continue;
                    Vector2d e = b - a;
                    if (!Unit(ref e) || Math.Abs(D.X * e.Y - D.Y * e.X) < 0.5) { best = u; wallIndex = -2 - wi; continue; }
                    best = u; wallIndex = wi;
                }
            }
            if (wallIndex == -1) return -1;
            if (wallIndex < -1) { wallIndex = -1; return -1; } // the outermost crossing is too slanted
            uRoot = best; at = P + D * best;
            return 1;
        }

        /// <summary>Where a line parallel to the fin crosses the given wall near the root.</summary>
        private static bool Foot(List<Point2d> ring, Point2d P, Vector2d D, double uRoot, double w, out double u, out Point2d foot)
        {
            u = 0; foot = P;
            double best = double.MaxValue;
            for (int k = 0; k < ring.Count; k++)
            {
                if (!LineSegment(P, D, ring[k], ring[(k + 1) % ring.Count], out double uu, out _)) continue;
                double d = Math.Abs(uu - uRoot);
                if (d < best && d < 2 * w) { best = d; u = uu; }
            }
            if (best == double.MaxValue) return false;
            foot = P + D * u;
            return true;
        }

        /// <summary>Distance along the line from uFrom to where it leaves the material of 'rings' (uFrom if it is not inside there).</summary>
        private static double Exit(List<List<Point2d>> rings, Point2d P, Vector2d D, double uFrom, double w)
        {
            if (rings.Count == 0 || !Inside(rings, P + D * (uFrom + 0.05 * w))) return uFrom;
            double first = double.MaxValue;
            foreach (var r in rings)
                for (int k = 0; k < r.Count; k++)
                    if (LineSegment(P, D, r[k], r[(k + 1) % r.Count], out double u, out _) && u > uFrom + 0.05 * w && u < first) first = u;
            return first == double.MaxValue ? uFrom : first;
        }

        // ------------------------------------------------------------------ geometry helpers

        private static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d v = p - pl.Origin;
            return new Point2d(v * pl.XAxis, v * pl.YAxis);
        }

        private static Point3d To3d(Plane pl, Point2d p) => pl.Origin + pl.XAxis * p.X + pl.YAxis * p.Y;

        private static double Mod(double a, double m) { a %= m; return a < 0 ? a + m : a; }

        private static bool Unit(ref Vector2d v)
        {
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (l < 1e-12) return false;
            v = new Vector2d(v.X / l, v.Y / l);
            return true;
        }

        /// <summary>Even-odd: inside an odd number of the rings.</summary>
        private static bool Inside(List<List<Point2d>> rings, Point2d p)
        {
            bool inside = false;
            foreach (var r in rings)
                for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                    if ((r[i].Y > p.Y) != (r[j].Y > p.Y) &&
                        p.X < (r[j].X - r[i].X) * (p.Y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X)
                        inside = !inside;
            return inside;
        }

        /// <summary>Line P + D*u against segment a-b; u along the line, t along the segment (0..1).</summary>
        private static bool LineSegment(Point2d P, Vector2d D, Point2d a, Point2d b, out double u, out double t)
        {
            u = t = 0;
            Vector2d e = b - a;
            double den = D.X * e.Y - D.Y * e.X;
            if (Math.Abs(den) < 1e-12) return false;
            Vector2d ap = a - P;
            u = (ap.X * e.Y - ap.Y * e.X) / den;
            t = (ap.X * D.Y - ap.Y * D.X) / den;
            return t >= 0 && t < 1;
        }

        private static List<Point2d[]> Segments(IEnumerable<List<Point2d>> lines, bool close)
        {
            var segs = new List<Point2d[]>();
            foreach (var l in lines)
            {
                for (int k = 1; k < l.Count; k++) segs.Add(new[] { l[k - 1], l[k] });
                if (close && l.Count > 2) segs.Add(new[] { l[l.Count - 1], l[0] });
            }
            return segs;
        }

        private static double Nearest(List<Point2d[]> segs, Point2d p)
        {
            double best = double.MaxValue;
            foreach (var s in segs)
            {
                double d = FillEngine.PointSegment(p, s[0], s[1]);
                if (d < best) best = d;
            }
            return best;
        }

        private static double PolylineDistance(List<Point2d> pl, Point2d p)
        {
            double best = double.MaxValue;
            for (int k = 1; k < pl.Count; k++) best = Math.Min(best, FillEngine.PointSegment(p, pl[k - 1], pl[k]));
            return best;
        }

        private static double SegmentDistance(Point2d a, Point2d b, Point2d c, Point2d d)
        {
            Vector2d ab = b - a, cd = d - c;
            double den = ab.X * cd.Y - ab.Y * cd.X;
            if (Math.Abs(den) > 1e-12)
            {
                Vector2d ac = c - a;
                double t = (ac.X * cd.Y - ac.Y * cd.X) / den, u = (ac.X * ab.Y - ac.Y * ab.X) / den;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
            }
            return Math.Min(Math.Min(FillEngine.PointSegment(a, c, d), FillEngine.PointSegment(b, c, d)),
                            Math.Min(FillEngine.PointSegment(c, a, b), FillEngine.PointSegment(d, a, b)));
        }
    }
}
