using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;
using Path = System.IO.Path;

namespace LARGERslicer.GH2
{
    // The Formwork tab of Grasshopper 1 (LARGERslicer/Components/CNCUtilities): a glued block of boards
    // around a part, milled on the CNC. Same names, inputs, outputs, defaults and behaviour; the logic
    // is plain RhinoCommon and copied here, only reading the inputs and writing the outputs is
    // Grasshopper 2. The slots are the exposure groups of Grasshopper 1: 0 = steps 1-2, 1 = 3-3b,
    // 2 = 4-5, 3 = 6-8.

    internal static partial class Io
    {
        public const string Formwork = "Formwork";
    }

    /// <summary>Reading the inputs of the formwork components: Grasshopper 2 hands over plain geometry.</summary>
    internal static class FormworkIo
    {
        public static Brep ToBrep(object o)
        {
            o = Io.Solid(o);
            switch (o)
            {
                case null: return null;
                case Brep b: return b;
                case Extrusion ex: return ex.ToBrep();
                case Surface s: return s.ToBrep();
                case SubD sd: return sd.ToBrep(SubDToBrepOptions.Default);
                case Mesh m: return Brep.CreateFromMesh(m, true);
                case Rectangle3d r: { var c = r.ToNurbsCurve(); var bs = Brep.CreatePlanarBreps(c, 0.001); return bs != null && bs.Length > 0 ? bs[0] : null; }
            }
            return null;
        }

        public static Brep OneBrep(IDataAccess access, int index)
        {
            object o;
            return access.GetItem(index, out o) ? ToBrep(o) : null;
        }

        public static List<Brep> Breps(IDataAccess access, int index)
        {
            object[] items;
            if (!access.ItemArray(index, out items) || items == null) return new List<Brep>();
            return items.Select(ToBrep).ToList(); // null stays in place, as an empty item in Grasshopper 1
        }

        public static Surface Surface(IDataAccess access, int index)
        {
            object o;
            if (!access.GetItem(index, out o) || o == null) return null;
            switch (o)
            {
                case BrepFace f: return f.DuplicateSurface();
                case Surface s: return s;
                case Brep b: return b.Faces.Count > 0 ? b.Faces[0].DuplicateSurface() : null;
                case Plane p: return new PlaneSurface(p, new Interval(-1, 1), new Interval(-1, 1));
                case Rectangle3d r: return new PlaneSurface(r.Plane, r.X, r.Y);
            }
            return null;
        }

        /// <summary>The boards of a list input; false when there was data, but none of it a board.</summary>
        public static bool Boards(IDataAccess access, int index, out List<ThekenBoard> boards, out int raw)
        {
            object[] items;
            boards = new List<ThekenBoard>();
            if (!access.ItemArray(index, out items) || items == null) { raw = 0; return true; }
            raw = items.Length;
            boards.AddRange(items.OfType<ThekenBoard>());
            return !(raw > 0 && boards.Count == 0);
        }

        public static List<double> Numbers(IDataAccess access, int index)
        {
            double[] items;
            return access.ItemArray(index, out items) && items != null ? items.ToList() : new List<double>();
        }

        public static List<string> Texts(IDataAccess access, int index)
        {
            string[] items;
            return access.ItemArray(index, out items) && items != null ? items.Where(s => s != null).ToList() : new List<string>();
        }
    }

    // ============================================================== 1 orient part
    [IoId("9e59c178-4b47-4d24-b132-7f8fa7231dc7")]
    public sealed class FormworkOrientComponent : Component
    {
        public FormworkOrientComponent() : base(Io.Name("Formwork - Orient Part", "STEP 1. Orients a solid for CNC processing: length in X, depth in Y, height in Z.", Io.Formwork, 0)) { }
        public FormworkOrientComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityOrientIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Part", "Geo", "Closed solid part to be processed");
            inputs.AddGeneric("Top Face", "Top", "Reference surface for the top side (defines stacking direction Z)", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Front Face", "Front", "Reference surface for the front side (defines depth direction Y)", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Rotation Angle", "Angle", "Additional board orientation rotation around the top axis in degrees (0/90/180/270)").Set(0.0).Values(0, 90, 180, 270);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Oriented Solid", "Solid", "Oriented solid (X=length, Y=depth, Z=height)");
            outputs.AddTransform("Orientation Transform", "XForm", "Applied transformation matrix");
            outputs.AddPlane("Source Frame", "Frame", "Detected/computed source frame for inspection");
        }

        protected override void Process(IDataAccess access)
        {
            Brep solid = FormworkIo.OneBrep(access, 0);
            Surface topSrf = FormworkIo.Surface(access, 1);
            Surface frontSrf = FormworkIo.Surface(access, 2);
            double angle;
            if (!access.GetItem(3, out angle)) angle = 0;

            if (solid == null || !solid.IsValid || !solid.IsSolid)
            {
                access.AddError("No solid", "Please provide a valid closed solid.");
                return;
            }

            // step 1: the axis directions
            Vector3d nTop;    // normal of the top side, becomes +Z
            Vector3d nFront;  // normal of the front, becomes +Y

            if (topSrf != null)
            {
                nTop = GetSurfaceNormal(topSrf);
                if (!nTop.IsValid) { access.AddError("Top Face", "The 'Top Face' input could not be evaluated."); return; }
            }
            else
            {
                // the largest face of the solid that points roughly along world Z: top or bottom
                nTop = DetectTopNormal(solid);
            }
            nTop.Unitize();

            if (frontSrf != null)
            {
                nFront = GetSurfaceNormal(frontSrf);
                if (!nFront.IsValid) { access.AddError("Front Face", "The 'Front Face' input could not be evaluated."); return; }
            }
            else
            {
                // the thinnest bounding box axis square to nTop
                nFront = DetectFrontNormal(solid, nTop);
            }
            nFront.Unitize();

            // step 2: an orthonormal frame (Gram-Schmidt)
            nFront = nFront - (nFront * nTop) * nTop;
            if (nFront.Length < RhinoMath.ZeroTolerance)
            {
                nFront = DetectFrontNormal(solid, nTop);
                nFront = nFront - (nFront * nTop) * nTop;
            }
            nFront.Unitize();

            Vector3d nRight = Vector3d.CrossProduct(nFront, nTop);
            nRight.Unitize();

            // extra rotation around the top axis
            if (Math.Abs(angle) > RhinoMath.ZeroTolerance)
            {
                double rad = RhinoMath.ToRadians(angle);
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                Vector3d newRight = cos * nRight + sin * nFront;
                Vector3d newFront = -sin * nRight + cos * nFront;
                nRight = newRight; nRight.Unitize();
                nFront = newFront; nFront.Unitize();
            }

            var amp = AreaMassProperties.Compute(solid);
            Point3d origin = amp != null ? amp.Centroid : solid.GetBoundingBox(true).Center;

            // source frame: X = nRight (length), Y = nFront (depth), normal = nTop (height)
            Plane source = new Plane(origin, nRight, nFront);
            Plane target = new Plane(origin, Vector3d.XAxis, Vector3d.YAxis);
            Transform orient = Transform.PlaneToPlane(source, target);

            Brep result = solid.DuplicateBrep();
            result.Transform(orient);

            // bounding box minimum to 0,0,0
            BoundingBox bb = result.GetBoundingBox(true);
            Transform move = Transform.Translation(-bb.Min.X, -bb.Min.Y, -bb.Min.Z);
            result.Transform(move);

            Transform total = move * orient;

            access.SetItem(0, result);
            access.SetItem(1, total);
            access.SetItem(2, source);
        }

        private static Vector3d GetSurfaceNormal(Surface srf)
        {
            Vector3d n = srf.NormalAt(srf.Domain(0).Mid, srf.Domain(1).Mid);
            return n.IsValid ? n : Vector3d.Unset;
        }

        private static Vector3d DetectTopNormal(Brep solid)
        {
            double bestScore = 0;
            Vector3d bestN = Vector3d.ZAxis;
            for (int i = 0; i < solid.Faces.Count; i++)
            {
                var face = solid.Faces[i];
                var famp = AreaMassProperties.Compute(face);
                if (famp == null) continue;
                if (!face.ClosestPoint(famp.Centroid, out double u, out double v)) continue;
                Vector3d n = face.NormalAt(u, v);
                if (!n.IsValid) continue;
                n.Unitize();
                double score = famp.Area * Math.Abs(n.Z);
                if (score > bestScore) { bestScore = score; bestN = n; }
            }
            if (bestN.Z < 0) bestN = -bestN;
            return bestN;
        }

        private static Vector3d DetectFrontNormal(Brep solid, Vector3d nTop)
        {
            nTop.Unitize();
            BoundingBox bb = solid.GetBoundingBox(true);
            Vector3d diag = bb.Max - bb.Min;
            var axes = new[] { (dir: Vector3d.XAxis, size: diag.X), (dir: Vector3d.YAxis, size: diag.Y), (dir: Vector3d.ZAxis, size: diag.Z) };

            double bestScore = double.MaxValue;
            Vector3d bestDir = Vector3d.YAxis;
            foreach (var ax in axes)
            {
                if (Math.Abs(ax.dir * nTop) > 0.7) continue;
                if (ax.size < bestScore) { bestScore = ax.size; bestDir = ax.dir; }
            }

            // the largest face square to nTop
            double bestFaceScore = 0;
            Vector3d bestFaceN = Vector3d.Unset;
            for (int i = 0; i < solid.Faces.Count; i++)
            {
                var face = solid.Faces[i];
                var famp = AreaMassProperties.Compute(face);
                if (famp == null) continue;
                if (!face.ClosestPoint(famp.Centroid, out double u, out double v)) continue;
                Vector3d n = face.NormalAt(u, v);
                if (!n.IsValid) continue;
                n.Unitize();
                double topParallel = Math.Abs(n * nTop);
                if (topParallel > 0.5) continue;
                double score = famp.Area * (1.0 - topParallel);
                if (score > bestFaceScore) { bestFaceScore = score; bestFaceN = n; }
            }
            return bestFaceN.IsValid ? bestFaceN : bestDir;
        }
    }

    // ============================================================== 2 dimensions
    [IoId("7bdc8565-cded-4018-8d06-7cdeea4687b9")]
    public sealed class FormworkDimensionsComponent : Component
    {
        public FormworkDimensionsComponent() : base(Io.Name("Formwork - Dimensions", "STEP 2. Calculates dimensions and bounding box of the oriented solid.", Io.Formwork, 0)) { }
        public FormworkDimensionsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityBBoxIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Oriented Solid", "Solid", "Solid from CNC Utilities 01 Orient Part");
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddBox("Bounding Box", "BBox", "Bounding box of the oriented solid");
            outputs.AddNumber("Width X", "W", "Block width in X");
            outputs.AddNumber("Depth Y", "D", "Block depth in Y");
            outputs.AddNumber("Height Z", "H", "Block height in Z");
        }

        protected override void Process(IDataAccess access)
        {
            Brep solid = FormworkIo.OneBrep(access, 0);
            if (solid == null || !solid.IsValid) { access.AddError("Invalid solid", "The oriented solid is invalid."); return; }
            BoundingBox bb = solid.GetBoundingBox(true);
            if (!bb.IsValid) { access.AddError("No bounding box", "Bounding box could not be computed."); return; }
            access.SetItem(0, new Box(bb));
            access.SetItem(1, bb.Max.X - bb.Min.X);
            access.SetItem(2, bb.Max.Y - bb.Min.Y);
            access.SetItem(3, bb.Max.Z - bb.Min.Z);
        }
    }

    // ============================================================== 3 board layers
    [IoId("9d3bd24f-9ca2-4ff9-ae9e-f129052809ef")]
    public sealed class FormworkBoardLayersComponent : Component
    {
        public FormworkBoardLayersComponent() : base(Io.Name("Formwork - Board Layers", "STEP 3. Splits total height into material layers and separation joints.", Io.Formwork, 1)) { }
        public FormworkBoardLayersComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilitySliceIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Total Height", "H", "Total block height in mm (for example from CNC Utilities 02 Dimensions)");
            inputs.AddNumber("Bottom Edge Board", "Bottom", "Thickness of the bottom edge board in mm").Set(35.0);
            inputs.AddNumber("Top Edge Board", "Top", "Thickness of the top edge board in mm").Set(35.0);
            inputs.AddNumber("Middle Board Thickness", "Mid", "Thickness of middle boards in mm").Set(30.0);
            inputs.AddInteger("Joint Count", "Joints", "Number of separation joints").Set(1);
            inputs.AddNumber("Minimum Joint Width", "MinJoint", "Minimum width per joint in mm").Set(4.0);
            inputs.AddNumber("Joint Positions", "Positions", "Optional: absolute joint center heights in mm", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Boards", "Boards", "Generated boards including joint metadata", Access.Twig);
            outputs.AddNumber("Joint Centers", "Centers", "Joint center heights in mm", Access.Twig);
            outputs.AddNumber("Joint Width", "Width", "Actual joint width in mm");
            outputs.AddText("Info", "Info", "Notes about board partitioning", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            double h, ru, ro, mb, fw; int fn;
            if (!access.GetItem(0, out h)) return;
            if (!access.GetItem(1, out ru)) ru = 35;
            if (!access.GetItem(2, out ro)) ro = 35;
            if (!access.GetItem(3, out mb)) mb = 30;
            if (!access.GetItem(4, out fn)) fn = 1;
            if (!access.GetItem(5, out fw)) fw = 4;
            var manual = FormworkIo.Numbers(access, 6);

            if (h <= 0 || ru <= 0 || ro <= 0 || mb <= 0) { access.AddError("Sizes", "All heights and thicknesses must be greater than 0."); return; }

            fn = Math.Max(1, fn);
            fw = Math.Max(4.0, fw);

            double inner = h - ru - ro;
            if (inner <= fw * fn) { access.AddError("Too low", "Total height is too small for boards and joints."); return; }

            int middleCount = (int)Math.Floor((inner - fw * fn) / mb + 1e-6);
            if (middleCount < 1)
            {
                // less than one board would make the joints narrower than the minimum (or negative)
                access.AddError("Too low", Inv.F($"Total height is too small: one middle board ({mb:F2} mm) and {fn} joint(s) of {fw:F2} mm need {ru + ro + mb + fw * fn:F2} mm, the total height is {h:F2} mm."));
                return;
            }

            double usedMiddle = middleCount * mb;
            double rest = inner - usedMiddle;
            double actualFugeWidth = rest / fn;

            if (manual.Count > 0 && manual.Count != fn)
                access.AddWarning("Joint positions", $"{manual.Count} joint position(s) for {fn} joint(s): the positions are not used. Give one position per joint.");

            var fugenMitten = new List<double>();
            if (manual.Count == fn) { fugenMitten.AddRange(manual); fugenMitten.Sort(); }
            else if (fn == 1) fugenMitten.Add(h * 0.5);
            else
            {
                double spacing = inner / (fn + 1);
                for (int i = 1; i <= fn; i++) fugenMitten.Add(ru + i * spacing);
            }

            foreach (double m in fugenMitten)
            {
                if (m <= ru || m >= (h - ro)) { access.AddError("Joint position", "At least one joint position is outside the usable inner range."); return; }
            }

            var boards = new List<ThekenBoard> { new ThekenBoard { ZMin = 0, ZMax = ru, Type = "Randbrett unten", SourceIndex = 0 } };

            double zCursor = ru;
            int src = 1, consumedMiddleBoards = 0, marked = 0;

            foreach (double fugeCenter in fugenMitten)
            {
                double fugeLow = fugeCenter - actualFugeWidth * 0.5;
                double fugeHigh = fugeCenter + actualFugeWidth * 0.5;

                if (fugeLow <= zCursor) { access.AddError("Joint position", Inv.F($"Joint at Z={fugeCenter:F2} is invalid or overlaps another joint.")); return; }

                double deltaToFugeLow = fugeLow - zCursor;
                int regularBoardsBefore = (int)Math.Floor(deltaToFugeLow / mb);
                double lowerHalfThickness = deltaToFugeLow - regularBoardsBefore * mb;

                for (int j = 0; j < regularBoardsBefore; j++)
                {
                    boards.Add(new ThekenBoard { ZMin = zCursor, ZMax = zCursor + mb, Type = "Mittelbrett", SourceIndex = src++ });
                    zCursor += mb;
                    consumedMiddleBoards++;
                }

                double upperHalfThickness = mb - lowerHalfThickness;
                double fugeBoardTop = fugeHigh + upperHalfThickness;

                boards.Add(new ThekenBoard
                {
                    ZMin = zCursor, ZMax = fugeBoardTop, Type = "Fuge-Brett", ContainsFuge = true,
                    FugeCenter = fugeCenter, FugeWidth = actualFugeWidth, SourceIndex = src++
                });

                zCursor = fugeBoardTop;
                consumedMiddleBoards++;
                marked++;
            }

            int remainingMiddleBoards = middleCount - consumedMiddleBoards;
            if (remainingMiddleBoards < 0) { access.AddError("Joints", "Joint distribution does not match the configured board thicknesses."); return; }

            for (int i = 0; i < remainingMiddleBoards; i++)
            {
                boards.Add(new ThekenBoard { ZMin = zCursor, ZMax = zCursor + mb, Type = "Mittelbrett", SourceIndex = src++ });
                zCursor += mb;
            }

            double expectedTopStart = h - ro;
            if (Math.Abs(zCursor - expectedTopStart) > 0.01)
                access.AddWarning("Board stack", Inv.F($"Board stack ends at Z={zCursor:F2}, top edge board starts at Z={expectedTopStart:F2}. Please review parameters."));

            boards.Add(new ThekenBoard { ZMin = expectedTopStart, ZMax = h, Type = "Randbrett oben", SourceIndex = src++ });

            var infos = new List<string>
            {
                Inv.F($"Usable inner height: {inner:F2} mm"),
                $"Middle board count: {middleCount}",
                Inv.F($"Actual joint width: {actualFugeWidth:F2} mm"),
                $"Joint-board count: {marked}"
            };
            for (int i = 0; i < fugenMitten.Count; i++)
                infos.Add(Inv.F($"Joint {i + 1}: {fugenMitten[i]:F2} +/- {actualFugeWidth * 0.5:F2}"));

            access.SetTwig(0, boards.ToArray(), null, null);
            access.SetTwig(1, fugenMitten.ToArray(), null, null);
            access.SetItem(2, actualFugeWidth);
            access.SetTwig(3, infos.ToArray(), null, null);
        }
    }

    // ============================================================== 3b split joint
    [IoId("3e317095-477c-4fcd-acb1-a5951bf9577d")]
    public sealed class FormworkSplitJointComponent : Component
    {
        public FormworkSplitJointComponent() : base(Io.Name("Formwork - Split Joint", "STEP 3b. Splits each layer with a separation joint into lower and upper parts.", Io.Formwork, 1)) { }
        public FormworkSplitJointComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilitySplitIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Boards", "Boards", "Boards from CNC Utilities 03 Layer Split", Access.Twig);
            inputs.AddBoolean("Split Enabled", "On", "True = split joint boards").Set(true);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Split Boards", "Split", "Board list after splitting joint boards", Access.Twig);
            outputs.AddText("Info", "Info", "Split notes", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            bool split;
            if (!FormworkIo.Boards(access, 0, out var inputBoards, out int raw)) { access.AddError("No boards", "Input data could not be parsed as a board list."); return; }
            if (raw == 0) return;
            if (!access.GetItem(1, out split)) split = true;

            var outBoards = new List<ThekenBoard>();
            var info = new List<string>();

            if (!split)
            {
                outBoards.AddRange(inputBoards);
                info.Add("Split is disabled. Returning the unchanged board list.");
                access.SetTwig(0, outBoards.ToArray(), null, null);
                access.SetTwig(1, info.ToArray(), null, null);
                return;
            }

            int createdSplits = 0;
            const double tol = 0.01; // thinner than this is no board: the joint lies between two boards
            foreach (var b in inputBoards)
            {
                bool doSplit = b.ContainsFuge && b.FugeCenter > b.ZMin && b.FugeCenter < b.ZMax;
                double fw = b.FugeWidth > 0 ? b.FugeWidth : 4.0;
                if (!doSplit) { outBoards.Add(b); continue; }

                double lo = b.FugeCenter - fw * 0.5;
                double hi = b.FugeCenter + fw * 0.5;
                bool below = lo > b.ZMin + tol, above = hi < b.ZMax - tol;
                if (!below && above && lo > b.ZMin - tol)
                {
                    // the joint lies right below a whole board
                    outBoards.Add(WholeBoard(b, hi, b.ZMax));
                    info.Add(Inv.F($"Joint at Z={b.FugeCenter:F2} lies between two boards."));
                }
                else if (below && !above && hi < b.ZMax + tol)
                {
                    // the joint lies right above a whole board
                    outBoards.Add(WholeBoard(b, b.ZMin, lo));
                    info.Add(Inv.F($"Joint at Z={b.FugeCenter:F2} lies between two boards."));
                }
                else if (below && above)
                {
                    outBoards.Add(new ThekenBoard { ZMin = b.ZMin, ZMax = lo, Type = "Joint Board A", IsSplitPart = true, SourceIndex = b.SourceIndex });
                    outBoards.Add(new ThekenBoard { ZMin = hi, ZMax = b.ZMax, Type = "Joint Board B", IsSplitPart = true, SourceIndex = b.SourceIndex });
                    createdSplits++;
                }
                else
                {
                    outBoards.Add(b);
                    info.Add($"Joint in board {b.SourceIndex} could not be split because bounds are invalid.");
                }
            }

            info.Add($"Created split joint boards: {createdSplits}");
            access.SetTwig(0, outBoards.ToArray(), null, null);
            access.SetTwig(1, info.ToArray(), null, null);
        }

        private static ThekenBoard WholeBoard(ThekenBoard source, double zMin, double zMax)
            => new ThekenBoard { ZMin = zMin, ZMax = zMax, Type = "Mittelbrett", SourceIndex = source.SourceIndex };
    }

    // ============================================================== 4 milling depth
    [IoId("81f90169-0f19-4013-8d85-46311c812ad9")]
    public sealed class FormworkMillingDepthComponent : Component
    {
        public FormworkMillingDepthComponent() : base(Io.Name("Formwork - Milling Depth", "STEP 4. Calculates required milling depth per board in stepped increments.", Io.Formwork, 2)) { }
        public FormworkMillingDepthComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityDepthIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Oriented Body", "Body", "From CNC Utilities 01 Orient Part");
            inputs.AddGeneric("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", Access.Twig);
            inputs.AddNumber("Start Depth", "Start", "Minimum allowed milling depth in mm").Set(150.0);
            inputs.AddNumber("Depth Step", "Step", "Step size for milling depth in mm").Set(50.0);
            inputs.AddNumber("Front Buffer", "Front", "Additional front buffer in mm").Set(5.0);
            inputs.AddNumber("Rear Buffer", "Rear", "Additional rear buffer in mm").Set(5.0);
            inputs.AddNumber("Side Overhang", "Overhang", "Additional board length on both sides in mm").Set(100.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddNumber("Milling Depths", "Depths", "Calculated milling depth per board in mm", Access.Twig);
            outputs.AddNumber("Board Length", "Length", "Unified board length in mm");
            outputs.AddNumber("Raw Depths", "Raw", "Unstepped maximum depth per board in mm, from the front (lowest Y) of the body", Access.Twig);
            outputs.AddText("Info", "Info", "Depth calculation notes per board", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            object so;
            if (!access.GetItem(0, out so) || so == null) return;
            Brep solid = FormworkIo.ToBrep(so);
            if (!FormworkIo.Boards(access, 1, out var boards, out int raw)) { access.AddError("No boards", "Input data could not be parsed as a board list."); return; }
            if (raw == 0) return;
            double depthBase, depthStep, pv, ph, overhang;
            if (!access.GetItem(2, out depthBase)) depthBase = 150;
            if (!access.GetItem(3, out depthStep)) depthStep = 50;
            if (!access.GetItem(4, out pv)) pv = 5;
            if (!access.GetItem(5, out ph)) ph = 5;
            if (!access.GetItem(6, out overhang)) overhang = 100;

            if (depthStep <= 0) { access.AddError("Depth step", "Depth step must be greater than 0."); return; }
            if (solid == null || !solid.IsValid) { access.AddError("Invalid body", "The oriented body is invalid."); return; }

            BoundingBox bb = solid.GetBoundingBox(true);
            double length = (bb.Max.X - bb.Min.X) + 2.0 * overhang;

            var depths = new List<double>();
            var rawDepths = new List<double>();
            var info = new List<string>();

            for (int i = 0; i < boards.Count; i++)
            {
                if (Io.Step(access, 100 * i / Math.Max(1, boards.Count))) return;
                // all boards stand flush at the front of the block (step 5), so the depth counts from there
                double rawDepth = GetMaxDepthInZRange(solid, bb.Min.Y, boards[i].ZMin, boards[i].ZMax, out bool cut);
                if (!cut)
                    access.AddWarning("Board misses the body", Inv.F($"Board {i + 1} ({boards[i].Type}, Z {boards[i].ZMin:F1} to {boards[i].ZMax:F1}) does not meet the body: it gets the start depth."));
                double need = rawDepth + pv + ph;
                double stepped = StepUp(need, depthBase, depthStep);
                rawDepths.Add(rawDepth);
                depths.Add(stepped);
                info.Add(Inv.F($"{boards[i].Type}: raw={rawDepth:F1} need={need:F1} step={stepped:F1}"));
            }

            access.SetTwig(0, depths.ToArray(), null, null);
            access.SetItem(1, length);
            access.SetTwig(2, rawDepths.ToArray(), null, null);
            access.SetTwig(3, info.ToArray(), null, null);
        }

        private static double GetMaxDepthInZRange(Brep solid, double front, double zMin, double zMax, out bool cut)
        {
            cut = false;
            int samples = Math.Max(5, (int)Math.Ceiling((zMax - zMin) / 2.0)); // a section every 2 mm: a bulge between two sections is not missed
            double maxDepth = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double z = zMin + (zMax - zMin) * (i / (double)samples);
                Curve[] contours = Brep.CreateContourCurves(solid, new Plane(new Point3d(0, 0, z), Vector3d.ZAxis));
                if (contours == null) continue;
                foreach (Curve c in contours)
                {
                    if (c == null) continue;
                    BoundingBox cb = c.GetBoundingBox(true);
                    if (!cb.IsValid) continue;
                    cut = true;
                    double d = cb.Max.Y - front;
                    if (d > maxDepth) maxDepth = d;
                }
            }
            return maxDepth;
        }

        private static double StepUp(double value, double baseDepth, double step)
        {
            if (value <= baseDepth) return baseDepth;
            return baseDepth + Math.Ceiling((value - baseDepth) / step) * step;
        }
    }

    // ============================================================== 5 raw block
    [IoId("3690f0e9-9217-491d-8032-d6fcd9f57736")]
    public sealed class FormworkRawBlockComponent : Component
    {
        public FormworkRawBlockComponent() : base(Io.Name("Formwork - Raw Block", "STEP 5. Builds the raw block from boards, milling depths, and board length.", Io.Formwork, 2)) { }
        public FormworkRawBlockComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityBlockIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Oriented Body", "Body", "Reference body from CNC Utilities 01 Orient Part");
            inputs.AddGeneric("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", Access.Twig);
            inputs.AddNumber("Milling Depths", "Depths", "Milling depth per board from CNC Utilities 04 Milling Depth", Access.Twig);
            inputs.AddNumber("Board Length", "Length", "Unified board length from CNC Utilities 04 Milling Depth");
            inputs.AddNumber("Side Overhang", "Overhang", "Overhang used for placement in mm").Set(100.0);
            inputs.AddNumber("Front Buffer", "Front", "How far the boards stand out in front of the body in mm: the Front Buffer of CNC Utilities 04 Milling Depth").Set(5.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Boards", "Boards", "All boards as Breps", Access.Twig);
            outputs.AddGeneric("Container Box", "Box", "Enclosing box of the glued block");
            outputs.AddGeneric("Reference Body", "Ref", "Oriented reference body");
        }

        protected override void Process(IDataAccess access)
        {
            Brep refSolid = FormworkIo.OneBrep(access, 0);
            if (refSolid == null) return;
            if (!FormworkIo.Boards(access, 1, out var boards, out int raw)) { access.AddError("No boards", "Input data could not be parsed as a board list."); return; }
            if (raw == 0) return;
            var depths = FormworkIo.Numbers(access, 2);
            if (depths.Count == 0) return;
            double boardLength, overhang, frontBuffer;
            if (!access.GetItem(3, out boardLength)) return;
            if (!access.GetItem(4, out overhang)) overhang = 100;
            if (!access.GetItem(5, out frontBuffer)) frontBuffer = 5;

            if (boards.Count != depths.Count) { access.AddError("Counts", "Board count and milling depth count must match."); return; }

            BoundingBox sbb = refSolid.GetBoundingBox(true);
            double x0 = sbb.Min.X - overhang;
            double y0 = sbb.Min.Y - frontBuffer;

            var breps = new List<Brep>();
            BoundingBox all = BoundingBox.Empty;
            for (int i = 0; i < boards.Count; i++)
            {
                var row = boards[i];
                var plane = new Plane(new Point3d(x0, y0, row.ZMin), Vector3d.XAxis, Vector3d.YAxis);
                Brep b = new Box(plane, new Interval(0, boardLength), new Interval(0, depths[i]), new Interval(0, row.Thickness)).ToBrep();
                if (b != null) { breps.Add(b); all.Union(b.GetBoundingBox(true)); }
            }

            Brep container = all.IsValid ? new Box(all).ToBrep() : null;
            access.SetTwig(0, breps.ToArray(), null, null);
            if (container != null) access.SetItem(1, container);
            access.SetItem(2, refSolid);
        }
    }

    // ============================================================== 6 fixtures
    [IoId("80222fc2-a4bb-4e8e-929a-90adb79fa0c4")]
    public sealed class FormworkFixturesComponent : Component
    {
        public FormworkFixturesComponent() : base(Io.Name("Formwork - Fixtures", "STEP 6. Creates L-shaped fixture elements (stop + base) and shortens every board by Insert Depth at both ends, where it sits in the stops. The contour of the stops is given for checking.", Io.Formwork, 3)) { }
        public FormworkFixturesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityFixtureIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Boards", "Boards", "Boards from CNC Utilities 05 Build Raw Block", Access.Twig);
            inputs.AddNumber("Insert Depth", "Depth", "How deep the stop is inserted into the boards (mm)").Set(5.0);
            inputs.AddNumber("Stop Thickness", "Thk", "Total thickness of the vertical stop board (mm)").Set(30.0);
            inputs.AddNumber("Base Height", "BaseH", "Thickness of the horizontal base board (mm)").Set(20.0);
            inputs.AddNumber("Base Overhang", "BaseO", "How far the base extends beyond the stop (mm)").Set(50.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Left Stop", "Left", "Vertical stop board on the left");
            outputs.AddGeneric("Right Stop", "Right", "Vertical stop board on the right");
            outputs.AddCurve("Left Contour", "Cont L", "Left step contour (YZ profile)");
            outputs.AddCurve("Right Contour", "Cont R", "Right step contour (YZ profile)");
            outputs.AddGeneric("Left Base", "BaseL", "Horizontal base board on the left");
            outputs.AddGeneric("Right Base", "BaseR", "Horizontal base board on the right");
            outputs.AddGeneric("Milled Boards", "Boards", "Boards with insertion pockets on both ends", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            object[] rawItems;
            if (!access.ItemArray(0, out rawItems) || rawItems == null) return;
            var boards = rawItems.Select(FormworkIo.ToBrep).ToList();
            double insertDepth, stopThickness, baseHeight, baseExtension;
            if (!access.GetItem(1, out insertDepth)) insertDepth = 5;
            if (!access.GetItem(2, out stopThickness)) stopThickness = 30;
            if (!access.GetItem(3, out baseHeight)) baseHeight = 20;
            if (!access.GetItem(4, out baseExtension)) baseExtension = 50;

            if (boards.Count == 0) { access.AddWarning("No boards", "No boards provided."); return; }
            if (insertDepth <= 0) { access.AddError("Insert depth", "Insert depth must be greater than 0."); return; }
            if (stopThickness <= insertDepth) { access.AddError("Stop thickness", "Stop thickness must be greater than insert depth."); return; }

            // the bounding boxes of all boards (empty items are left out)
            boards = boards.Where(b => b != null && b.IsValid).ToList();
            if (boards.Count == 0) { access.AddWarning("No boards", "No valid board."); return; }
            BoundingBox allBB = BoundingBox.Empty;
            var boardBoxes = new List<BoundingBox>();
            foreach (var b in boards) { var bx = b.GetBoundingBox(true); boardBoxes.Add(bx); allBB.Union(bx); }

            double xMin = allBB.Min.X, xMax = allBB.Max.X, yMin = allBB.Min.Y, yMax = allBB.Max.Y, zMin = allBB.Min.Z, zMax = allBB.Max.Z;

            if (xMax - xMin <= 2.0 * insertDepth)
            {
                access.AddError("Boards too short", "Boards are shorter than twice the insert depth. Boards must be longer than the insert contour.");
                return;
            }

            // vertical stops: Insert Depth reaches into the boards, the rest stands outside
            double leftOuterX = xMin - (stopThickness - insertDepth);
            double leftInnerX = xMin + insertDepth;
            Box leftStop = new Box(Plane.WorldXY, new Interval(leftOuterX, leftInnerX), new Interval(yMin, yMax), new Interval(zMin, zMax));
            double rightInnerX = xMax - insertDepth;
            double rightOuterX = xMax + (stopThickness - insertDepth);
            Box rightStop = new Box(Plane.WorldXY, new Interval(rightInnerX, rightOuterX), new Interval(yMin, yMax), new Interval(zMin, zMax));

            // horizontal bases (clamping surface): under the stop and beyond it
            Box leftBase = new Box(Plane.WorldXY, new Interval(leftOuterX - baseExtension, leftInnerX), new Interval(yMin, yMax), new Interval(zMin - baseHeight, zMin));
            Box rightBase = new Box(Plane.WorldXY, new Interval(rightInnerX, rightOuterX + baseExtension), new Interval(yMin, yMax), new Interval(zMin - baseHeight, zMin));

            // boards with pockets: they are plain boxes, so shortening them at both ends is enough
            var milledBoards = new List<Brep>();
            for (int i = 0; i < boards.Count; i++)
            {
                BoundingBox bbb = boardBoxes[i];
                Brep sb = new Box(Plane.WorldXY, new Interval(bbb.Min.X + insertDepth, bbb.Max.X - insertDepth), new Interval(bbb.Min.Y, bbb.Max.Y), new Interval(bbb.Min.Z, bbb.Max.Z)).ToBrep();
                milledBoards.Add(sb ?? boards[i]);
            }

            // step contours, for checking
            var sortedBoxes = new List<BoundingBox>(boardBoxes);
            sortedBoxes.Sort((a, b) => a.Min.Z.CompareTo(b.Min.Z));

            access.SetItem(0, leftStop.ToBrep());
            access.SetItem(1, rightStop.ToBrep());
            access.SetItem(2, BuildStepPolyline(sortedBoxes, xMin).ToPolylineCurve());
            access.SetItem(3, BuildStepPolyline(sortedBoxes, xMax).ToPolylineCurve());
            access.SetItem(4, leftBase.ToBrep());
            access.SetItem(5, rightBase.ToBrep());
            access.SetTwig(6, milledBoards.ToArray(), null, null);
        }

        private static Polyline BuildStepPolyline(List<BoundingBox> sortedBoxes, double xSide)
        {
            var pts = new List<Point3d>();
            if (sortedBoxes.Count == 0) return new Polyline();
            double yFront = sortedBoxes[0].Min.Y;
            pts.Add(new Point3d(xSide, yFront, sortedBoxes[0].Min.Z));
            foreach (var b in sortedBoxes)
            {
                pts.Add(new Point3d(xSide, b.Max.Y, b.Min.Z));
                pts.Add(new Point3d(xSide, b.Max.Y, b.Max.Z));
            }
            double top = sortedBoxes[sortedBoxes.Count - 1].Max.Z;
            pts.Add(new Point3d(xSide, yFront, top));
            pts.Add(new Point3d(xSide, yFront, sortedBoxes[0].Min.Z));
            return new Polyline(pts);
        }
    }

    // ============================================================== 7 bill of materials
    [IoId("fb9c2f9f-7662-4974-ac37-c092a95fc9b7")]
    public sealed class FormworkBomComponent : Component
    {
        public FormworkBomComponent() : base(Io.Name("Formwork - Bill of Materials", "STEP 7. Generates a bill of materials for panel display and CSV export.", Io.Formwork, 3)) { }
        public FormworkBomComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityBomIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", Access.Twig);
            inputs.AddNumber("Milling Depths", "Depths", "Milling depths from CNC Utilities 04 Milling Depth", Access.Twig);
            inputs.AddNumber("Board Length", "Length", "Board length from CNC Utilities 04 Milling Depth");
            inputs.AddText("Material", "Material", "Material name used in the BOM").Set("MDF / Solid Wood");
            inputs.AddGeneric("Left Fixture", "Left", "Optional left fixture stop from CNC Utilities 06", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Right Fixture", "Right", "Optional right fixture stop from CNC Utilities 06", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Left Base", "BaseL", "Optional left base board from CNC Utilities 06", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Right Base", "BaseR", "Optional right base board from CNC Utilities 06", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Panel Text", "Panel", "Formatted BOM text for a GH panel", Access.Twig);
            outputs.AddText("CSV Header", "Header", "CSV header row");
            outputs.AddText("CSV Rows", "CSV", "CSV data rows", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            if (!FormworkIo.Boards(access, 0, out var rows, out int raw)) { access.AddError("No boards", "Input data could not be parsed as a board list."); return; }
            if (raw == 0) return;
            var depths = FormworkIo.Numbers(access, 1);
            if (depths.Count == 0) return;
            double length; string material;
            if (!access.GetItem(2, out length)) return;
            if (!access.GetItem(3, out material) || material == null) material = "MDF / Vollholz";
            Brep saugL = FormworkIo.OneBrep(access, 4), saugR = FormworkIo.OneBrep(access, 5), basisL = FormworkIo.OneBrep(access, 6), basisR = FormworkIo.OneBrep(access, 7);

            if (rows.Count != depths.Count) { access.AddError("Counts", "Board count and milling depth count must match."); return; }

            var groups = rows
                .Select((row, index) => new { Row = row, Depth = depths[index] })
                .GroupBy(r => new { T = r.Row.Type, L = Math.Round(length, 1), D = Math.Round(r.Depth, 1), H = Math.Round(r.Row.Thickness, 1) })
                .OrderBy(g => SortOrder(g.Key.T))
                .ToList();

            var panel = new List<string>();
            var csv = new List<string>();
            string header = "Pos;Type;Length;Depth;Height;Count;Material;Remark";

            int pos = 1;
            panel.Add("Pos | Type | L | D | H | Count | Material | Remark");
            panel.Add("-------------------------------------------------------");
            foreach (var g in groups)
            {
                int count = g.Count();
                string remark = g.Key.T.Contains("Fuge") ? "Fuge" : g.Key.T.Contains("Joint") ? "Joint" : "";
                panel.Add(Inv.F($"{pos} | {g.Key.T} | {g.Key.L:F1} | {g.Key.D:F1} | {g.Key.H:F1} | {count} | {material} | {remark}"));
                csv.Add(string.Join(";", new[]
                {
                    pos.ToString(CultureInfo.InvariantCulture), g.Key.T,
                    g.Key.L.ToString("F1", CultureInfo.InvariantCulture), g.Key.D.ToString("F1", CultureInfo.InvariantCulture), g.Key.H.ToString("F1", CultureInfo.InvariantCulture),
                    count.ToString(CultureInfo.InvariantCulture), material, remark
                }));
                pos++;
            }

            if (saugL != null) AddSaugRow(ref pos, material, "Fixture left", saugL, panel, csv);
            if (saugR != null) AddSaugRow(ref pos, material, "Fixture right", saugR, panel, csv);
            if (basisL != null) AddSaugRow(ref pos, material, "Base left", basisL, panel, csv);
            if (basisR != null) AddSaugRow(ref pos, material, "Base right", basisR, panel, csv);

            access.SetTwig(0, panel.ToArray(), null, null);
            access.SetItem(1, header);
            access.SetTwig(2, csv.ToArray(), null, null);
        }

        private static int SortOrder(string t)
        {
            if (t.Contains("unten")) return 0;
            if (t.Contains("Mittel")) return 1;
            if (t.Contains("Fuge") || t.Contains("Joint")) return 2;
            if (t.Contains("oben")) return 3;
            return 4;
        }

        private static void AddSaugRow(ref int pos, string material, string type, Brep b, List<string> panel, List<string> csv)
        {
            BoundingBox bb = b.GetBoundingBox(true);
            double l = bb.Max.X - bb.Min.X, d = bb.Max.Y - bb.Min.Y, h = bb.Max.Z - bb.Min.Z;
            panel.Add(Inv.F($"{pos} | {type} | {l:F1} | {d:F1} | {h:F1} | 1 | {material} | Fixture geometry"));
            csv.Add(string.Join(";", new[]
            {
                pos.ToString(CultureInfo.InvariantCulture), type,
                l.ToString("F1", CultureInfo.InvariantCulture), d.ToString("F1", CultureInfo.InvariantCulture), h.ToString("F1", CultureInfo.InvariantCulture),
                "1", material, "Fixture geometry"
            }));
            pos++;
        }
    }

    // ============================================================== 8 export
    [IoId("72500329-8c94-419b-bd28-0446c64aa268")]
    public sealed class FormworkExportComponent : Component
    {
        public FormworkExportComponent() : base(Io.Name("Formwork - Export", "STEP 8. Exports boards and bill of materials as 3DM and CSV.", Io.Formwork, 3)) { }
        public FormworkExportComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("CncUtilityExportIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Boards", "Boards", "Boards from CNC Utilities 05 Build Raw Block", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Container Box", "Box", "Container box from CNC Utilities 05 Build Raw Block", Access.Item, Requirement.MayBeMissing);
            inputs.AddGeneric("Reference Body", "Ref", "Reference body from CNC Utilities 05 Build Raw Block", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("CSV Header", "Header", "CSV header row from CNC Utilities 07 Bill of Materials", Access.Item, Requirement.MayBeMissing);
            inputs.AddText("CSV Rows", "CSV", "CSV data rows from CNC Utilities 07 Bill of Materials", Access.Twig, Requirement.MayBeMissing);
            inputs.AddText("Export Folder", "Folder", "Destination folder for export").Set("");
            inputs.AddText("File Name", "Name", "Base name for exported files").Set("CNC_Project");
            inputs.AddBoolean("Write 3DM", "3DM", "Export a 3DM file").Set(true);
            inputs.AddBoolean("Write CSV", "CSV", "Export a CSV file").Set(true);
            inputs.AddBoolean("Start Export", "Start", "True = run export").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Files", "Files", "Written file paths", Access.Twig);
            outputs.AddText("Log", "Log", "Export log", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var boards = FormworkIo.Breps(access, 0);
            Brep container = FormworkIo.OneBrep(access, 1);
            Brep reference = FormworkIo.OneBrep(access, 2);
            string header, path, name; bool write3dm, writeCsv, run;
            if (!access.GetItem(3, out header) || header == null) header = "";
            var lines = FormworkIo.Texts(access, 4);
            if (!access.GetItem(5, out path) || path == null) path = "";
            if (!access.GetItem(6, out name) || name == null) name = "CNC_Project";
            if (!access.GetItem(7, out write3dm)) write3dm = true;
            if (!access.GetItem(8, out writeCsv)) writeCsv = true;
            if (!access.GetItem(9, out run)) run = false;

            var files = new List<string>();
            var log = new List<string>();

            if (!run)
            {
                log.Add("Export is ready. Set 'Start Export' to True.");
                access.SetTwig(0, files.ToArray(), null, null);
                access.SetTwig(1, log.ToArray(), null, null);
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                string docPath = RhinoDoc.ActiveDoc?.Path;
                path = string.IsNullOrWhiteSpace(docPath) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : Path.GetDirectoryName(docPath);
            }

            try { Directory.CreateDirectory(path); }
            catch (Exception ex)
            {
                access.AddError("Export folder", "Export folder is invalid: " + ex.Message);
                log.Add("Canceled: export folder is invalid - " + ex.Message);
                access.SetTwig(0, files.ToArray(), null, null);
                access.SetTwig(1, log.ToArray(), null, null);
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string basePath = Path.Combine(path, name + "_" + stamp);
            // several branches in the same second: never write over the files of the one before
            string free = basePath;
            for (int n = 2; File.Exists(free + ".3dm") || File.Exists(free + ".csv"); n++)
                free = basePath + "_" + n.ToString(CultureInfo.InvariantCulture);
            basePath = free;

            if (write3dm)
            {
                string p3 = basePath + ".3dm";
                try
                {
                    var f3 = new File3dm();
                    foreach (var b in boards) if (b != null) f3.Objects.AddBrep(b);
                    if (container != null) f3.Objects.AddBrep(container);
                    if (reference != null) f3.Objects.AddBrep(reference);
                    if (!f3.Write(p3, 7) || !File.Exists(p3)) throw new IOException("Rhino could not write " + p3);
                    files.Add(p3);
                    log.Add("3DM file written");
                }
                catch (Exception ex)
                {
                    access.AddError("3DM file not written", "3DM file not written: " + ex.Message);
                    log.Add("Error writing 3DM file: " + ex.Message);
                }
            }

            if (writeCsv)
            {
                string pc = basePath + ".csv";
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(header);
                    foreach (var l in lines) sb.AppendLine(l);
                    File.WriteAllText(pc, sb.ToString(), Encoding.UTF8);
                    files.Add(pc);
                    log.Add("CSV file written");
                }
                catch (Exception ex)
                {
                    access.AddError("CSV file not written", "CSV file not written: " + ex.Message);
                    log.Add("Error writing CSV file: " + ex.Message);
                }
            }

            access.SetTwig(0, files.ToArray(), null, null);
            access.SetTwig(1, log.ToArray(), null, null);
        }
    }
}
