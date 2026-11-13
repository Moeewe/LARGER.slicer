using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== Curve Fill Cross Lines
    [IoId("784bb854-b999-416a-83c1-29b1396783ba")]
    public sealed class CurveFillCrossLinesComponent : Component
    {
        public CurveFillCrossLinesComponent() : base(Io.Name("Curve Fill Cross Lines", "Generates boustrophedon infill pattern. Takes first curve as outer contour, offsets inward by layer width, then connects with cross-lines at configurable angle. Works for first layer and planar layers.", Io.Slicer, Group.Curves)) { }
        public CurveFillCrossLinesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CurveBoundaryInfillIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Individual boundary curves. The first curve will be used as the outer contour on Z-axis.", Access.Twig);
            inputs.AddNumber("Layer Width", "LW", "Layer width (extrusion width/bead width in mm). Used for inward offset spacing and cross-line spacing.").Set(5.0);
            inputs.AddNumber("Z Height", "Z", "Z height for planar layers (mm). For first layer, use 0 or layer height.").Set(0.0);
            inputs.AddNumber("Angle", "Angle", "Angle for cross-lines (connection lines) in degrees. 0 = horizontal, 90 = vertical.").Set(0.0);
            inputs.AddBoolean("Start Left", "StartLeft", "True to start from left, False to start from right").Set(true);
            inputs.AddNumber("Boundary Offset", "BO", "Additional offset from boundary (mm). Positive = inward, negative = outward. Default: 0 (uses half layer width).", Access.Item, Requirement.MayBeMissing).Set(0.0);
            inputs.AddCurve("Holes", "Holes", "Optional inner boundary curves (holes/islands) to exclude from fill", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Boundary", "Boundary", "Outer boundary curve that exactly matches the connection lines of input curves", Access.Item);
            outputs.AddCurve("Infill Path", "Path", "Complete continuous infill path", Access.Item);
            outputs.AddCurve("Offset Curves", "Offsets", "Individual offset curves used for infill", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var inputCurves = CurveIo.Curves(access, 0).Where(c => c.IsValid).ToList();
            if (inputCurves.Count == 0) { access.AddError("No curves", "At least one curve is required."); return; }
            double layerWidth;
            if (!access.GetItem(1, out layerWidth) || layerWidth <= 0) { access.AddError("Invalid layer width", "Layer width must be greater than zero."); return; }
            double zHeight, angle, boundaryOffset = 0.0; bool startLeft;
            access.GetItem(2, out zHeight); access.GetItem(3, out angle); access.GetItem(4, out startLeft);
            access.GetItem(5, out boundaryOffset);
            var holes = CurveIo.Curves(access, 6).Where(h => h.IsValid).ToList();

            // 1 the first curve is the boundary, at Z height
            Curve boundary = inputCurves[0].DuplicateCurve();
            if (!boundary.IsClosed) boundary.MakeClosed(0.01);
            boundary.Transform(Transform.Translation(0, 0, zHeight - boundary.PointAtStart.Z));
            if (!boundary.IsValid) { access.AddError("No boundary", "Failed to create boundary from curves."); return; }

            // 2 offset: the given one, or half a layer width inward
            double offset = boundaryOffset != 0.0 ? boundaryOffset : layerWidth * 0.5;
            {
                double offsetDistance = offset * PathHelper.GetOffsetDirection(boundary, holes);
                var offsetCurves = boundary.Offset(Plane.WorldXY, offsetDistance, 0.01, CurveOffsetCornerStyle.Sharp);
                var valid = offsetCurves?.Where(c => c != null && c.IsValid).ToList();
                if (valid != null && valid.Count > 0)
                    boundary = valid.OrderByDescending(c => AreaMassProperties.Compute(c)?.Area ?? 0).First();
            }

            // 3 offsets inward by layer width
            var offsetList = PathHelper.GenerateOffsetCurves(boundary, layerWidth, 1000, holes);
            if (offsetList.Count == 0)
            {
                access.AddWarning("No offsets", "No offset curves generated. Boundary may be too small.");
                access.SetItem(0, boundary); access.SetItem(1, boundary);
                CurveIo.SetCurves(access, 2, new[] { boundary });
                return;
            }

            // 4 boustrophedon over all offsets
            var segments = Boustrophedon(offsetList, boundary, layerWidth, angle, startLeft, zHeight, out List<Point3d> pathPoints);
            Curve infillPath = null;
            if (pathPoints.Count >= 2)
            {
                var cleaned = Clean(pathPoints, 0.01);
                if (cleaned.Count >= 2) { var pl = new Polyline(cleaned); if (pl.IsValid) infillPath = new PolylineCurve(pl); }
            }
            var segmentCurves = new List<Curve>();
            foreach (var seg in segments)
            {
                var cleaned = Clean(seg, 0.01);
                if (cleaned.Count < 2) continue;
                var pl = new Polyline(cleaned);
                if (pl.IsValid) segmentCurves.Add(new PolylineCurve(pl));
            }
            access.SetItem(0, boundary);
            if (infillPath != null) access.SetItem(1, infillPath);
            CurveIo.SetCurves(access, 2, segmentCurves);
        }

        private static List<List<Point3d>> Boustrophedon(List<Curve> offsetCurves, Curve boundary, double layerWidth, double angle, bool startLeft, double zHeight, out List<Point3d> pathPoints)
        {
            pathPoints = new List<Point3d>();
            var segments = new List<List<Point3d>>();
            BoundingBox bbox = boundary.GetBoundingBox(true);
            var allLines = PathHelper.GenerateParallelLines(boundary, layerWidth, angle * Math.PI / 180.0, bbox.Center, bbox);
            bool forward = startLeft;
            foreach (var line in allLines)
            {
                if (line == null || !line.IsValid) continue;
                var hits = new List<(double param, Point3d point)>();
                foreach (var oc in offsetCurves)
                {
                    if (oc == null || !oc.IsValid) continue;
                    var ix = Rhino.Geometry.Intersect.Intersection.CurveCurve(line, oc, 0.01, 0.01);
                    if (ix == null) continue;
                    foreach (var e in ix) { Point3d pt = line.PointAt(e.ParameterA); pt.Z = zHeight; hits.Add((e.ParameterA, pt)); }
                }
                hits.Sort((a, b) => a.param.CompareTo(b.param));
                for (int i = 0; i + 1 < hits.Count; i += 2)
                {
                    double t1 = hits[i].param, t2 = hits[i + 1].param;
                    if (Math.Abs(t2 - t1) <= 0.01) continue;
                    if (!forward) { double tmp = t1; t1 = t2; t2 = tmp; }
                    Curve trimmed = line.Trim(t1, t2);
                    if (trimmed == null || !trimmed.IsValid || trimmed.GetLength() <= layerWidth * 0.1) continue;
                    int n = Math.Max(2, (int)Math.Ceiling(trimmed.GetLength() / (layerWidth * 0.5)));
                    var pts = new List<Point3d>();
                    for (int p = 0; p <= n; p++) { Point3d pt = trimmed.PointAt(trimmed.Domain.ParameterAt((double)p / n)); pt.Z = zHeight; pts.Add(pt); }
                    segments.Add(pts);
                }
                forward = !forward;
            }
            if (segments.Count == 0) return segments;
            pathPoints.AddRange(segments[0]);
            for (int i = 1; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Count == 0) continue;
                Point3d last = pathPoints[pathPoints.Count - 1], first = seg[0];
                double d = last.DistanceTo(first);
                if (d > layerWidth * 0.1)
                {
                    int steps = Math.Max(2, (int)Math.Ceiling(d / (layerWidth * 0.5)));
                    for (int s = 1; s < steps; s++)
                    {
                        double t = (double)s / steps;
                        pathPoints.Add(new Point3d(last.X + t * (first.X - last.X), last.Y + t * (first.Y - last.Y), zHeight));
                    }
                }
                pathPoints.AddRange(seg);
            }
            return segments;
        }

        private static List<Point3d> Clean(List<Point3d> points, double minDistance)
        {
            var cleaned = new List<Point3d>();
            if (points == null || points.Count == 0) return cleaned;
            cleaned.Add(points[0]);
            for (int i = 1; i < points.Count; i++) if (points[i].DistanceTo(cleaned[cleaned.Count - 1]) >= minDistance) cleaned.Add(points[i]);
            return cleaned;
        }
    }

    // ============================================================== Curve Join and Order
    [IoId("9f350166-c634-462c-8718-51e285786f8e")]
    public sealed class CurveJoinOrderComponent : Component
    {
        public CurveJoinOrderComponent() : base(Io.Name("Curve Join and Order", "Combines multiple curves into an optimized continuous path. Removes self-intersections, optimizes curve order using nearest-neighbor algorithm, and adds connections between gaps. Ideal for combining output from multiple pattern components.", Io.Slicer, Group.Curves)) { }
        public CurveJoinOrderComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("OptimizedToolpathIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Input curves to optimize (can be from multiple pattern components)", Access.Twig);
            inputs.AddPoint("Start Point", "Start", "Start point for path optimization (default: origin)").Set(Point3d.Origin);
            inputs.AddNumber("Spacing", "S", "Bead width/spacing - used for connection generation").Set(5.0);
            inputs.AddBoolean("Remove Self-Intersections", "RemoveSelf", "Remove crossings of the path with itself without cutting it (the stretch between two crossing parts is run the other way round)").Set(true);
            inputs.AddBoolean("Optimize Order", "Optimize", "Reorder curves to minimize travel distance").Set(true);
            inputs.AddBoolean("Add Connections", "Connect", "Add connection curves between gaps").Set(true);
            inputs.AddNumber("Max Gap Distance", "MaxGap", "Maximum gap distance for adding connections (mm)").Set(50.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Optimized Path", "Path", "The joined path: one curve if every gap could be connected", Access.Twig);
            outputs.AddCurve("Connections", "Conn", "Generated connection curves", Access.Twig);
            outputs.AddText("Info", "Info", "Optimization statistics");
        }

        protected override void Process(IDataAccess access)
        {
            var left = CurveIo.Curves(access, 0).Where(c => c.IsValid).ToList();
            if (left.Count == 0) { access.AddWarning("No curves", "No input curves provided"); return; }
            Point3d start; double spacing, maxGap; bool uncross, optimize, connect;
            if (!access.GetItem(1, out start)) start = Point3d.Origin;
            access.GetItem(2, out spacing); access.GetItem(3, out uncross); access.GetItem(4, out optimize);
            access.GetItem(5, out connect); access.GetItem(6, out maxGap);
            int originalCount = left.Count;

            // 1 order: always continue with the curve whose end is nearest
            var ordered = new List<Curve>();
            Point3d cursor = start;
            while (left.Count > 0)
            {
                int pick = 0; double best = double.MaxValue; bool flip = false; double seam = double.NaN;
                int scan = optimize ? left.Count : 1;
                for (int i = 0; i < scan; i++)
                {
                    Curve c = left[i];
                    if (c.IsClosed)
                    {
                        if (c.ClosestPoint(cursor, out double t))
                        {
                            double d = c.PointAt(t).DistanceTo(cursor);
                            if (d < best) { best = d; pick = i; flip = false; seam = t; }
                        }
                    }
                    else
                    {
                        double d0 = c.PointAtStart.DistanceTo(cursor), d1 = c.PointAtEnd.DistanceTo(cursor);
                        if (d0 < best) { best = d0; pick = i; flip = false; seam = double.NaN; }
                        if (optimize && d1 < best) { best = d1; pick = i; flip = true; seam = double.NaN; }
                    }
                }
                Curve next = left[pick];
                left.RemoveAt(pick);
                if (!double.IsNaN(seam) && next.IsClosed) next.ChangeClosedCurveSeam(seam);
                if (flip) next.Reverse();
                ordered.Add(next);
                cursor = next.PointAtEnd;
            }

            // 2 connect where the gap is small enough to print across
            var connections = new List<Curve>();
            var chains = new List<List<Curve>> { new List<Curve> { ordered[0] } };
            int tooFar = 0;
            for (int i = 1; i < ordered.Count; i++)
            {
                Point3d a = ordered[i - 1].PointAtEnd, b = ordered[i].PointAtStart;
                double gap = a.DistanceTo(b);
                if (gap <= 1e-6) chains[chains.Count - 1].Add(ordered[i]);
                else if (connect && gap <= maxGap)
                {
                    var link = new LineCurve(a, b);
                    connections.Add(link);
                    chains[chains.Count - 1].Add(link);
                    chains[chains.Count - 1].Add(ordered[i]);
                }
                else { tooFar++; chains.Add(new List<Curve> { ordered[i] }); }
            }

            // 3 one curve per chain, crossings removed without cutting the line
            var path = new List<Curve>();
            int fixedCrossings = 0, remaining = 0;
            foreach (var chain in chains)
            {
                Curve joined;
                if (chain.Count == 1) joined = chain[0];
                else
                {
                    var j = Curve.JoinCurves(chain, 1e-4, true);
                    if (j == null || j.Length != 1) { path.AddRange(chain); continue; }
                    joined = j[0];
                }
                if (uncross)
                {
                    Polyline pl;
                    if (!joined.TryGetPolyline(out pl))
                    {
                        var pc = joined.ToPolyline(0.02, Math.PI / 90.0, 0, 0);
                        if (pc != null) pc.TryGetPolyline(out pl);
                    }
                    if (pl != null && pl.Count > 3 && PathTools.CountCrossings(pl) > 0)
                    {
                        var healed = PathTools.Uncross(pl, 0.01, out int moves, out _);
                        fixedCrossings += moves;
                        remaining += PathTools.CountCrossings(healed);
                        joined = new PolylineCurve(healed);
                    }
                }
                path.Add(joined);
            }

            double printLength = ordered.Sum(c => c.GetLength()), linkLength = connections.Sum(c => c.GetLength());
            if (path.Count > 1)
                access.AddRemark("Separate paths", $"{path.Count} separate paths: {tooFar} gap(s) are wider than Max Gap Distance ({maxGap:0.#} mm){(connect ? "" : " or Add Connections is off")}.");
            if (remaining > 0) access.AddWarning("Crossings left", remaining + " crossing(s) could not be removed.");
            int wide = connections.Count(c => c.GetLength() > 1.6 * spacing);
            CurveIo.SetCurves(access, 0, path);
            CurveIo.SetCurves(access, 1, connections);
            access.SetItem(2, $"{originalCount} curve(s) -> {path.Count} path(s)\n" +
                              $"Print length {printLength / 1000.0:0.00} m, links {linkLength:0} mm in {connections.Count} piece(s)" +
                              (wide > 0 ? $" ({wide} longer than 1.6 x spacing: they print across open space)" : "") + "\n" +
                              (uncross ? $"Crossings removed: {fixedCrossings}" + (remaining > 0 ? $", left: {remaining}" : "") : "Crossings not checked"));
        }
    }

    // ============================================================== Curve Uncross
    [IoId("a979e2a8-553b-4de8-83a8-c197ba0c24c0")]
    public sealed class CurveUncrossComponent : Component
    {
        public CurveUncrossComponent() : base(Io.Name("Curve Uncross", "Removes the crossings of a line with itself. By default the line stays one continuous line: the stretch between two crossing parts is run the other way round. With Split the line is cut at the crossings instead.", Io.Slicer, Group.Curves)) { }
        public CurveUncrossComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SuppressSelfIntersectionsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curve", "C", "Curve that may self-intersect");
            inputs.AddNumber("Tolerance", "T", "Accuracy with which curved input is turned into a polyline (mm)").Set(0.01);
            inputs.AddBoolean("Split Segments", "Split", "False: keep one continuous line without crossings. True: cut the curve into pieces at the crossings.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Healed Curves", "H", "Healed curves without self-intersections", Access.Twig);
            outputs.AddPoint("Intersections", "IX", "Self-intersection points found", Access.Twig);
            outputs.AddText("Info", "I", "Information about healing process");
        }

        protected override void Process(IDataAccess access)
        {
            var curve = CurveIo.Curve(access, 0); if (curve == null) return;
            double tol; bool split; access.GetItem(1, out tol); access.GetItem(2, out split);
            tol = Math.Max(tol, 1e-4);
            Polyline pl;
            if (!curve.TryGetPolyline(out pl))
            {
                var pc = curve.ToPolyline(tol, Math.PI / 90.0, 0, 0);
                if (pc == null || !pc.TryGetPolyline(out pl)) { access.AddError("No polyline", "The curve could not be turned into a polyline."); return; }
            }
            if (split)
            {
                var events = Rhino.Geometry.Intersect.Intersection.CurveSelf(curve, tol);
                var ts = new List<double>(); var pts = new List<Point3d>();
                if (events != null) foreach (var e in events) { ts.Add(e.ParameterA); ts.Add(e.ParameterB); pts.Add(e.PointA); }
                Curve[] pieces = ts.Count > 0 ? curve.Split(ts) : null;
                CurveIo.SetCurves(access, 0, pieces != null && pieces.Length > 0 ? pieces : new[] { curve });
                access.SetTwig(1, pts.ToArray(), null, null);
                access.SetItem(2, pts.Count == 0 ? "No self-crossings." : pts.Count + " crossing(s), cut into " + (pieces?.Length ?? 1) + " pieces.");
                return;
            }
            var healed = PathTools.Uncross(pl, tol, out int moves, out List<Point3d> crossings);
            int leftOver = moves > 0 ? PathTools.CountCrossings(healed) : 0;
            if (leftOver > 0) access.AddWarning("Crossings left", leftOver + " crossing(s) could not be removed.");
            CurveIo.SetCurves(access, 0, new[] { moves == 0 ? curve : new PolylineCurve(healed) });
            access.SetTwig(1, crossings.ToArray(), null, null);
            access.SetItem(2, moves == 0 ? "No self-crossings." : moves + " crossing(s) removed; the line is still one line" + (leftOver > 0 ? ", " + leftOver + " left" : "") + ".");
        }
    }

    // ============================================================== Curve Bridge
    [IoId("5d64e84e-ac24-4093-9578-251a55bb658c")]
    public sealed class CurveBridgeComponent : Component
    {
        public CurveBridgeComponent() : base(Io.Name("Curve Bridge", "Connects separate curves with bridges to create continuous paths. Prevents overfill between disconnected segments. Based on Nautilus plugin approach.", Io.Slicer, Group.Curves)) { }
        public CurveBridgeComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("BridgeCurvesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Curves to connect with bridges", Access.Twig);
            inputs.AddNumber("Bridge Density", "Density", "Density of bridges (0-1). Higher = more bridges, lower = fewer bridges.").Set(0.3);
            inputs.AddNumber("Bridge Length", "Length", "Maximum length for bridges (mm). Longer distances won't be bridged.").Set(10.0);
            inputs.AddBoolean("Random", "Random", "Use random bridge placement. If false, uses greedy nearest-neighbor.").Set(true);
            inputs.AddInteger("Seed", "Seed", "Random seed for reproducible results").Set(42);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Path", "P", "Continuous path with bridges", Access.Item);
            outputs.AddCurve("Bridges", "B", "Bridge curves", Access.Twig);
            outputs.AddCurve("Original Curves", "O", "Original curves in order", Access.Twig);
            outputs.AddText("Info", "I", "Information about bridges created");
        }

        protected override void Process(IDataAccess access)
        {
            var curves = CurveIo.Curves(access, 0);
            if (curves.Count == 0) { access.AddWarning("No curves", "No curves provided."); return; }
            double density, maxLength; bool random; int seed;
            access.GetItem(1, out density); access.GetItem(2, out maxLength); access.GetItem(3, out random); access.GetItem(4, out seed);
            var valid = curves.Where(c => c.IsValid).ToList();
            if (valid.Count == 0) { access.AddWarning("No curves", "No valid curves found."); return; }
            if (valid.Count == 1)
            {
                access.SetItem(0, valid[0]);
                CurveIo.SetCurves(access, 1, new Curve[0]);
                CurveIo.SetCurves(access, 2, valid);
                access.SetItem(3, "Single curve, no bridges needed.");
                return;
            }

            var ordered = PathHelper.OptimizeCurveOrder(valid, valid[0].PointAtStart, out List<Curve> _);
            var bridges = new List<Curve>();
            var pieces = new List<Curve>();
            var rnd = new Random(seed);
            const double tolerance = 0.01;
            for (int i = 0; i < ordered.Count; i++)
            {
                pieces.Add(ordered[i].DuplicateCurve());
                if (i == ordered.Count - 1) continue;
                Point3d a = ordered[i].PointAtEnd, b = ordered[i + 1].PointAtStart;
                double distance = a.DistanceTo(b);
                if (distance <= tolerance) continue;
                // within the length: density decides (random) or always; longer: only without random
                bool bridge = distance <= maxLength ? (!random || rnd.NextDouble() < density) : !random;
                if (!bridge) continue;
                var line = new LineCurve(new Line(a, b));
                bridges.Add(line); pieces.Add(line);
            }
            var joined = Curve.JoinCurves(pieces, 0.01);
            Curve final = joined != null && joined.Length > 0 ? joined[0] : pieces[0];

            string info = $"Bridged {valid.Count} curves with {bridges.Count} bridges. Bridge density: {density:P0}, Max length: {maxLength:F2}mm. ";
            if (bridges.Count == 0)
            {
                var d = new List<double>();
                for (int i = 0; i < ordered.Count - 1; i++) d.Add(ordered[i].PointAtEnd.DistanceTo(ordered[i + 1].PointAtStart));
                if (d.Count > 0)
                {
                    info += $"Curve distances: min={d.Min():F2}mm, max={d.Max():F2}mm, avg={d.Average():F2}mm. ";
                    if (d.Min() <= tolerance) info += "Some curves are already connected (< 0.01mm). ";
                    if (d.Max() > maxLength) info += $"Some distances exceed max bridge length ({maxLength:F2}mm). ";
                }
            }
            access.SetItem(0, final);
            CurveIo.SetCurves(access, 1, bridges);
            CurveIo.SetCurves(access, 2, ordered);
            access.SetItem(3, info);
        }
    }

    /// <summary>Directions as the curve tools of Grasshopper 1 read them.</summary>
    internal static class CurveDirection
    {
        /// <summary>Start to end; for closed or degenerate curves the tangent at the start.</summary>
        public static Vector3d Of(Curve curve)
        {
            if (curve == null || !curve.IsValid) return Vector3d.Zero;
            if (curve.IsClosed)
            {
                Vector3d t = curve.TangentAtStart;
                if (t.IsValid && t.Length > 0.001) { t.Unitize(); return t; }
            }
            Vector3d d = curve.PointAtEnd - curve.PointAtStart;
            if (d.Length < 0.001)
            {
                Vector3d t = curve.TangentAtStart;
                if (t.IsValid && t.Length > 0.001) { t.Unitize(); return t; }
                return Vector3d.Zero;
            }
            d.Unitize();
            return d;
        }
    }

    // ============================================================== Align Curves
    [IoId("ed73565f-7748-46f7-ab05-4d515c6542c6")]
    public sealed class AlignCurvesComponent : Component
    {
        public AlignCurvesComponent() : base(Io.Name("Align Curves", "Flips all curves to point in the same direction. Ensures consistent orientation for continuous toolpath generation.", Io.Slicer, Group.Curves)) { }
        public AlignCurvesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("AlignCurvesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Curves to align", Access.Twig);
            inputs.AddInteger("Reference Index", "Ref", "Index of reference curve (0 = first curve). All other curves will be aligned to this one.").Set(0);
            inputs.AddBoolean("Reverse", "Rev", "If true, reverses all curves. If false, aligns to reference direction.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Aligned Curves", "C", "Curves aligned to same direction", Access.Twig);
            outputs.AddBoolean("Flipped", "F", "True if curve was flipped, False if original direction", Access.Twig);
            outputs.AddText("Info", "I", "Information about alignment");
        }

        protected override void Process(IDataAccess access)
        {
            var curves = CurveIo.Curves(access, 0);
            if (curves.Count == 0) { access.AddWarning("No curves", "No curves provided."); return; }
            int reference; bool reverse; access.GetItem(1, out reference); access.GetItem(2, out reverse);
            var valid = curves.Where(c => c.IsValid).ToList();
            if (valid.Count == 0) { access.AddWarning("No curves", "No valid curves found."); return; }
            reference = Math.Max(0, Math.Min(valid.Count - 1, reference));

            var aligned = new List<Curve>(); var flipped = new List<bool>();
            if (reverse)
            {
                foreach (var c in valid) { var r = c.DuplicateCurve(); r.Reverse(); aligned.Add(r); flipped.Add(true); }
            }
            else
            {
                Vector3d refDir = CurveDirection.Of(valid[reference]);
                if (refDir.Length < 0.001)
                {
                    access.AddWarning("Degenerate reference", "Reference curve has degenerate direction. Using original curves.");
                    CurveIo.SetCurves(access, 0, valid);
                    access.SetTwig(1, Enumerable.Repeat(false, valid.Count).ToArray(), null, null);
                    access.SetItem(2, "Reference curve is degenerate - no alignment performed.");
                    return;
                }
                foreach (var c in valid)
                {
                    Vector3d dir = CurveDirection.Of(c);
                    var a = c.DuplicateCurve();
                    bool flip = dir.Length >= 0.001 && refDir * dir < 0;
                    if (flip) a.Reverse();
                    aligned.Add(a); flipped.Add(flip);
                }
            }
            string info = $"Aligned {valid.Count} curves. " + (reverse ? "All curves reversed." : $"Reference: curve {reference}. {flipped.Count(f => f)} curve(s) flipped.");
            CurveIo.SetCurves(access, 0, aligned);
            access.SetTwig(1, flipped.ToArray(), null, null);
            access.SetItem(2, info);
        }
    }

    // ============================================================== Alternate Curves
    [IoId("32cbafb1-623e-4995-a64d-e77e4f908359")]
    public sealed class AlternateCurvesComponent : Component
    {
        public AlternateCurvesComponent() : base(Io.Name("Alternate Curves", "Alternates curve directions (left-right-left-right pattern). Useful for creating zigzag patterns and preventing overfill in continuous toolpaths.", Io.Slicer, Group.Curves)) { }
        public AlternateCurvesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("AlternateCurvesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Curves", "C", "Curves to alternate", Access.Twig);
            inputs.AddBoolean("Start Left", "Left", "If true, first curve points left. If false, first curve points right.").Set(true);
            inputs.AddInteger("Group Size", "Group", "Number of consecutive curves with same direction before alternating (1 = alternate each, 2 = pairs, etc.)").Set(1);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Alternated Curves", "C", "Curves with alternating directions", Access.Twig);
            outputs.AddBoolean("Flipped", "F", "True if curve was flipped, False if original direction", Access.Twig);
            outputs.AddText("Info", "I", "Information about alternation");
        }

        protected override void Process(IDataAccess access)
        {
            var curves = CurveIo.Curves(access, 0);
            if (curves.Count == 0) { access.AddWarning("No curves", "No curves provided."); return; }
            bool startLeft; int groupSize; access.GetItem(1, out startLeft); access.GetItem(2, out groupSize);
            var valid = curves.Where(c => c.IsValid).ToList();
            if (valid.Count == 0) { access.AddWarning("No curves", "No valid curves found."); return; }
            groupSize = Math.Max(1, groupSize);

            // a horizontal axis from the first usable curve, World X if there is none
            Vector3d axis = Vector3d.XAxis;
            foreach (var c in valid)
            {
                Vector3d d = CurveDirection.Of(c); var xy = new Vector3d(d.X, d.Y, 0);
                if (xy.Length >= 0.001) { xy.Unitize(); axis = xy; break; }
            }
            var result = new List<Curve>(); var flipped = new List<bool>();
            bool wantLeft = startLeft; int inGroup = 0;
            foreach (var c in valid)
            {
                if (inGroup >= groupSize) { wantLeft = !wantLeft; inGroup = 0; }
                Vector3d d = CurveDirection.Of(c); var xy = new Vector3d(d.X, d.Y, 0);
                bool pointsLeft = xy.Length >= 0.001 && xy * axis < 0;   // against the axis = left
                bool flip = pointsLeft != wantLeft;
                var a = c.DuplicateCurve(); if (flip) a.Reverse();
                result.Add(a); flipped.Add(flip); inGroup++;
            }
            CurveIo.SetCurves(access, 0, result);
            access.SetTwig(1, flipped.ToArray(), null, null);
            access.SetItem(2, $"Alternated {valid.Count} curves. Pattern: {(startLeft ? "Left" : "Right")} first, group size: {groupSize}. {flipped.Count(f => f)} curve(s) flipped.");
        }
    }

    // ============================================================== Join Open Contours
    [IoId("3fb8a5f9-905b-44b2-a6b8-a74abd739fb1")]
    public sealed class JoinOpenContoursComponent : Component
    {
        public JoinOpenContoursComponent() : base(Io.Name("Join Open Contours", "Connects open contours into a single continuous toolpath. Optimizes connection order for minimal travel distance.", Io.Slicer, Group.Curves)) { }
        public JoinOpenContoursComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("JoinOpenContoursIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddCurve("Contours", "C", "Open contours to join", Access.Twig);
            inputs.AddNumber("Tolerance", "T", "Tolerance for joining curves (mm)").Set(0.01);
            inputs.AddBoolean("Optimize Order", "Opt", "If true, optimizes curve order for minimal travel. If false, uses input order.").Set(true);
            inputs.AddInteger("Transition Type", "Trans", "Transition type: 0 = Linear, 1 = Arc").Set(0).Choices("Linear", "Arc");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Path", "P", "Continuous path from joined contours", Access.Item);
            outputs.AddCurve("Contours", "C", "Contours in order", Access.Twig);
            outputs.AddCurve("Transitions", "T", "Transition curves between contours", Access.Twig);
            outputs.AddText("Info", "I", "Information about joining process");
        }

        protected override void Process(IDataAccess access)
        {
            var contours = CurveIo.Curves(access, 0);
            if (contours.Count == 0) { access.AddWarning("No contours", "No contours provided."); return; }
            double tolerance; bool optimize; int transition;
            access.GetItem(1, out tolerance); access.GetItem(2, out optimize); access.GetItem(3, out transition);
            if (transition < 0 || transition > 1) { access.AddWarning("Transition Type", "Transition Type out of range. Using 0 = Linear."); transition = 0; }
            var valid = contours.Where(c => c.IsValid && !c.IsClosed).ToList();
            if (valid.Count == 0) { access.AddWarning("No contours", "No valid open contours found."); return; }
            if (valid.Count == 1)
            {
                access.SetItem(0, valid[0]);
                CurveIo.SetCurves(access, 1, valid);
                CurveIo.SetCurves(access, 2, new Curve[0]);
                access.SetItem(3, "Single contour, no joining needed.");
                return;
            }
            var ordered = optimize
                ? PathHelper.OptimizeCurveOrder(valid, valid[0].PointAtStart, out List<Curve> _)
                : valid.Select(c => c.DuplicateCurve()).ToList();

            var transitions = new List<Curve>(); var pieces = new List<Curve>();
            for (int i = 0; i < ordered.Count; i++)
            {
                pieces.Add(ordered[i].DuplicateCurve());
                if (i == ordered.Count - 1) continue;
                Point3d a = ordered[i].PointAtEnd, b = ordered[i + 1].PointAtStart;
                double distance = a.DistanceTo(b);
                if (distance <= tolerance) continue;
                Curve t = null;
                if (transition == 1 && distance > tolerance * 2)
                {
                    Vector3d dir = b - a; dir.Unitize();
                    var perp = new Vector3d(-dir.Y, dir.X, 0); perp.Unitize();
                    var arc = new Arc(a, (a + b) * 0.5 + perp * (distance * 0.3), b);
                    if (arc.IsValid) t = new ArcCurve(arc);
                }
                if (t == null) t = new LineCurve(new Line(a, b));
                transitions.Add(t); pieces.Add(t);
            }
            var joined = Curve.JoinCurves(pieces, tolerance);
            bool fragmented = joined != null && joined.Length > 1;
            Curve final;
            if (joined != null && joined.Length == 1) final = joined[0];
            else if (fragmented)
            {
                final = joined.OrderByDescending(c => c.GetLength()).First();
                access.AddWarning("Not fully joined", "Path could not be fully joined into one curve. Outputting longest segment.");
            }
            else final = pieces[0];

            string info = $"Joined {valid.Count} open contours into continuous path. Transitions: {transitions.Count} ({(transition == 0 ? "Linear" : "Arc")}). " +
                          (optimize ? "Order optimized." : "Using input order.") +
                          (fragmented ? " Join result was fragmented; longest segment returned as Path." : "");
            access.SetItem(0, final);
            CurveIo.SetCurves(access, 1, ordered);
            CurveIo.SetCurves(access, 2, transitions);
            access.SetItem(3, info);
        }
    }
}
