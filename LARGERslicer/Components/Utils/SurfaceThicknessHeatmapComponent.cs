using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;
using Rhino;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    public class SurfaceThicknessHeatmapComponent : GH_Component
    {
        public SurfaceThicknessHeatmapComponent()
          : base("Surface Thickness Heatmap", "ThkMap",
              "Creates per-part thickness/color sampling, heatmap mesh, and rebuilt untrimmed fit surfaces from Breps/Surfaces.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("S", "S", "Input Breps/Surfaces. Each Brep remains one logical unit.", GH_ParamAccess.list);
            pManager.AddGenericParameter("U", "U", "U values per Brep (tree/branches recommended).", GH_ParamAccess.tree);
            pManager.AddGenericParameter("V", "V", "V values per Brep (tree/branches recommended).", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Mode", "Mode", "Mode per sample: 0=min, 1=max, 2=gradient.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Min T", "MinT", "Minimum thickness.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max T", "MaxT", "Maximum thickness.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("N Contours", "N", "Cross-width contour count used for rebuild detail.", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Axis", "Axis", "Gradient axis: 0=auto, 1=X, 2=Y, 3=Z (top->bottom).", GH_ParamAccess.item, 3);
            pManager.AddNumberParameter("Kink Angle", "Kink", "Reserved split angle in degrees (currently informational).", GH_ParamAccess.item, 30.0);

            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Component parameter bundle for downstream G-code mapping.", GH_ParamAccess.item);
            pManager.AddTextParameter("Geo_ID", "GeoID", "Per S_out geometry ID.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Thickness", "T", "Sampled thickness values.", GH_ParamAccess.list);
            pManager.AddColourParameter("Color", "Col", "Sampled colors.", GH_ParamAccess.list);
            pManager.AddPointParameter("Points3D", "Pts", "Sampled 3D points.", GH_ParamAccess.list);
            pManager.AddMeshParameter("HeatmapMesh", "Heat", "Combined heatmap mesh.", GH_ParamAccess.item);
            pManager.AddBrepParameter("S_out", "Sout", "Original Brep units (one per logical input Brep).", GH_ParamAccess.list);
            pManager.AddGeometryParameter("Surfaces_out", "Srf", "Underlying untrimmed surfaces per face.", GH_ParamAccess.list);
            pManager.AddSurfaceParameter("Rebuilt_Srf", "ReSrf", "Rebuilt fit surfaces per face.", GH_ParamAccess.list);
            pManager.AddBrepParameter("Rebuilt_Joined", "ReJoin", "Joined rebuilt untrimmed surfaces per Brep unit.", GH_ParamAccess.list);
            pManager.AddPointParameter("Raster Points", "Grid", "Raster points used for fit rebuild.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Edge Curves", "Edges", "True trim edge curves for control.", GH_ParamAccess.list);
            pManager.AddPointParameter("Contours", "Contours", "Alias of Raster Points for backward compatibility.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var inputGeo = new List<GeometryBase>();
            GH_Structure<IGH_Goo> uTree;
            GH_Structure<IGH_Goo> vTree;
            GH_Structure<IGH_Goo> modeTree;
            double minT = 0.0;
            double maxT = 1.0;
            int nContours = 10;
            int axisInput = 3;
            double kinkAngle = 30.0;

            if (!DA.GetDataList(0, inputGeo))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geometry connected to S.");
                return;
            }

            DA.GetDataTree(1, out uTree);
            DA.GetDataTree(2, out vTree);
            DA.GetDataTree(3, out modeTree);
            DA.GetData(4, ref minT);
            DA.GetData(5, ref maxT);
            DA.GetData(6, ref nContours);
            DA.GetData(7, ref axisInput);
            DA.GetData(8, ref kinkAngle);

            if (nContours < 2) nContours = 2;
            if (maxT < minT)
            {
                double t = minT;
                minT = maxT;
                maxT = t;
            }

            double tol = 0.001;
            try
            {
                double dt = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
                if (dt > 0.0) tol = dt;
            }
            catch
            {
                tol = 0.001;
            }

            var thickness = new List<double>();
            var colors = new List<Drawing.Color>();
            var points3D = new List<Point3d>();
            var heatmapMesh = new Mesh();
            var sOut = new List<Brep>();
            var geoId = new List<string>();
            var surfacesOut = new List<GeometryBase>();
            var rebuiltSrf = new List<Surface>();
            var rebuiltJoined = new List<Brep>();
            var rasterPts = new List<Point3d>();
            var edgeCurves = new List<Curve>();
            var contoursAlias = new List<Point3d>();

            List<Brep> breps = ExtractBreps(inputGeo);
            int nUnits = breps.Count;
            if (nUnits == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid Brep/Surface units extracted from S.");
                SetOutputs(DA, string.Empty, geoId, thickness, colors, points3D, heatmapMesh, sOut, surfacesOut, rebuiltSrf, rebuiltJoined, rasterPts, edgeCurves, contoursAlias);
                return;
            }

            List<List<double>> uPerUnit = ToPerUnitValues(uTree, nUnits);
            List<List<double>> vPerUnit = ToPerUnitValues(vTree, nUnits);
            List<List<double>> modePerUnit = ToPerUnitValues(modeTree, nUnits);

            BoundingBox globalBox = BoundingBox.Empty;
            for (int i = 0; i < breps.Count; i++)
            {
                BoundingBox bb = breps[i].GetBoundingBox(true);
                if (!globalBox.IsValid) globalBox = bb;
                else globalBox.Union(bb);
            }

            char gradAxis = SelectAxis(axisInput, globalBox);
            double gradLo;
            double gradHi;
            if (gradAxis == 'z')
            {
                gradLo = globalBox.Max.Z;
                gradHi = globalBox.Min.Z;
            }
            else if (gradAxis == 'y')
            {
                gradLo = globalBox.Min.Y;
                gradHi = globalBox.Max.Y;
            }
            else
            {
                gradLo = globalBox.Min.X;
                gradHi = globalBox.Max.X;
            }

            string id = InstanceGuid.ToString().Replace(";", "_");
            int axisNum = gradAxis == 'x' ? 1 : (gradAxis == 'y' ? 2 : 3);
            int mode0 = 2;
            if (modePerUnit.Count > 0 && modePerUnit[0].Count > 0)
                mode0 = (int)Math.Round(modePerUnit[0][0]);

            string info = string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6}",
                id, axisNum, mode0, minT, maxT, gradLo, gradHi);

            for (int unitIdx = 0; unitIdx < breps.Count; unitIdx++)
            {
                Brep brep = breps[unitIdx];
                if (brep == null)
                    continue;

                if (brep.Faces.Count == 0)
                {
                    sOut.Add(brep);
                    geoId.Add(id);
                    continue;
                }

                BrepFace refFace = brep.Faces[0];
                Interval uDom = refFace.Domain(0);
                Interval vDom = refFace.Domain(1);

                List<double> uValues = unitIdx < uPerUnit.Count ? uPerUnit[unitIdx] : new List<double>();
                List<double> vValues = unitIdx < vPerUnit.Count ? vPerUnit[unitIdx] : new List<double>();
                List<double> mValues = unitIdx < modePerUnit.Count ? modePerUnit[unitIdx] : new List<double>();

                int meshMode = mValues.Count > 0 ? (int)Math.Round(mValues[0]) : 0;

                for (int i = 0; i < uValues.Count; i++)
                {
                    double uIn = uValues[i];
                    double vIn = i < vValues.Count ? vValues[i] : (vValues.Count > 0 ? vValues[vValues.Count - 1] : 0.0);
                    int currentMode = i < mValues.Count ? (int)Math.Round(mValues[i]) : (mValues.Count > 0 ? (int)Math.Round(mValues[0]) : 0);

                    double uVal = Math.Abs(uIn) <= 1.0 + 1e-9 ? uDom.Min + uIn * uDom.Length : uIn;
                    double vVal = Math.Abs(vIn) <= 1.0 + 1e-9 ? vDom.Min + vIn * vDom.Length : vIn;

                    Point3d pt;
                    try
                    {
                        pt = refFace.PointAt(uVal, vVal);
                    }
                    catch
                    {
                        continue;
                    }

                    double factor = GradientFactorOfPoint(pt, gradAxis, gradLo, gradHi);
                    double tVal = ThicknessFromFactor(currentMode, factor, minT, maxT);
                    Drawing.Color col = ColorFromThickness(tVal, minT, maxT);

                    thickness.Add(tVal);
                    colors.Add(col);
                    points3D.Add(pt);
                }

                sOut.Add(brep);
                geoId.Add(id);

                for (int fi = 0; fi < brep.Faces.Count; fi++)
                {
                    Surface us = brep.Faces[fi].UnderlyingSurface();
                    if (us != null)
                        surfacesOut.Add(us.Duplicate());
                }

                int nu = Math.Max(nContours * 2, 12);
                int nv = Math.Max(nContours, 4);
                var rbResult = RebuildFit(brep, nu, nv);
                for (int i = 0; i < rbResult.Surfaces.Count; i++)
                    rebuiltSrf.Add(rbResult.Surfaces[i]);
                for (int i = 0; i < rbResult.RasterPoints.Count; i++)
                {
                    Point3d p = rbResult.RasterPoints[i];
                    rasterPts.Add(p);
                    contoursAlias.Add(p);
                }

                for (int fi = 0; fi < brep.Faces.Count; fi++)
                {
                    Brep fb = brep.Faces[fi].DuplicateFace(false);
                    var edges = new List<Curve>();
                    for (int ei = 0; ei < fb.Edges.Count; ei++)
                        edges.Add(fb.Edges[ei].DuplicateCurve());

                    Curve[] joined = Curve.JoinCurves(edges, tol);
                    if (joined != null && joined.Length > 0)
                    {
                        for (int i = 0; i < joined.Length; i++)
                            edgeCurves.Add(joined[i]);
                    }
                    else
                    {
                        for (int i = 0; i < edges.Count; i++)
                            edgeCurves.Add(edges[i]);
                    }
                }

                if (rbResult.Surfaces.Count > 0)
                {
                    var srfBreps = new List<Brep>();
                    for (int i = 0; i < rbResult.Surfaces.Count; i++)
                    {
                        Surface s = rbResult.Surfaces[i];
                        Brep b = s?.ToBrep();
                        if (b != null)
                            srfBreps.Add(b);
                    }

                    if (srfBreps.Count > 0)
                    {
                        Brep[] joined = Brep.JoinBreps(srfBreps, tol);
                        if (joined != null && joined.Length > 0)
                        {
                            for (int i = 0; i < joined.Length; i++)
                                rebuiltJoined.Add(joined[i]);
                        }
                        else
                        {
                            for (int i = 0; i < srfBreps.Count; i++)
                                rebuiltJoined.Add(srfBreps[i]);
                        }
                    }
                }

                Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                if (meshes != null && meshes.Length > 0)
                {
                    var unitMesh = new Mesh();
                    for (int i = 0; i < meshes.Length; i++)
                        unitMesh.Append(meshes[i]);

                    for (int vi = 0; vi < unitMesh.Vertices.Count; vi++)
                    {
                        Point3d p = unitMesh.Vertices.Point3dAt(vi);
                        double factor = GradientFactorOfPoint(p, gradAxis, gradLo, gradHi);
                        double tVal = ThicknessFromFactor(meshMode, factor, minT, maxT);
                        Drawing.Color col = ColorFromThickness(tVal, minT, maxT);
                        unitMesh.VertexColors.Add(col);
                    }

                    unitMesh.Normals.ComputeNormals();
                    unitMesh.Compact();
                    heatmapMesh.Append(unitMesh);
                }
            }

            SetOutputs(DA, info, geoId, thickness, colors, points3D, heatmapMesh, sOut, surfacesOut, rebuiltSrf, rebuiltJoined, rasterPts, edgeCurves, contoursAlias);
        }

        private static void SetOutputs(
            IGH_DataAccess DA,
            string info,
            List<string> geoId,
            List<double> thickness,
            List<Drawing.Color> colors,
            List<Point3d> points3D,
            Mesh heatmapMesh,
            List<Brep> sOut,
            List<GeometryBase> surfacesOut,
            List<Surface> rebuiltSrf,
            List<Brep> rebuiltJoined,
            List<Point3d> rasterPts,
            List<Curve> edgeCurves,
            List<Point3d> contoursAlias)
        {
            DA.SetData(0, info ?? string.Empty);
            DA.SetDataList(1, geoId);
            DA.SetDataList(2, thickness);
            DA.SetDataList(3, colors);
            DA.SetDataList(4, points3D);
            DA.SetData(5, heatmapMesh);
            DA.SetDataList(6, sOut);
            DA.SetDataList(7, surfacesOut);
            DA.SetDataList(8, rebuiltSrf);
            DA.SetDataList(9, rebuiltJoined);
            DA.SetDataList(10, rasterPts);
            DA.SetDataList(11, edgeCurves);
            DA.SetDataList(12, contoursAlias);
        }

        private static List<Brep> ExtractBreps(List<GeometryBase> input)
        {
            var result = new List<Brep>();
            if (input == null)
                return result;

            for (int i = 0; i < input.Count; i++)
            {
                GeometryBase obj = input[i];
                if (obj == null)
                    continue;

                switch (obj)
                {
                    case Brep b:
                        result.Add(b);
                        break;
                    case BrepFace bf:
                        result.Add(bf.DuplicateFace(false));
                        break;
                    case Surface s:
                        {
                            Brep sb = s.ToBrep();
                            if (sb != null)
                                result.Add(sb);
                            break;
                        }
                }
            }

            return result;
        }

        private static List<List<double>> ToPerUnitValues(GH_Structure<IGH_Goo> tree, int nUnits)
        {
            int n = Math.Max(1, nUnits);
            var perUnit = new List<List<double>>(n);
            for (int i = 0; i < n; i++)
                perUnit.Add(new List<double>());

            if (tree == null || tree.PathCount == 0)
                return perUnit;

            var branches = new List<List<double>>();
            for (int p = 0; p < tree.PathCount; p++)
            {
                IList<IGH_Goo> branch = tree.Branches[p];
                var vals = new List<double>();
                for (int i = 0; i < branch.Count; i++)
                {
                    if (TryGooToDouble(branch[i], out double v))
                        vals.Add(v);
                }

                branches.Add(vals);
            }

            if (branches.Count == 0)
                return perUnit;

            if (branches.Count == 1)
            {
                for (int i = 0; i < n; i++)
                    perUnit[i] = new List<double>(branches[0]);
                return perUnit;
            }

            for (int i = 0; i < branches.Count && i < perUnit.Count; i++)
                perUnit[i] = branches[i];

            return perUnit;
        }

        private static bool TryGooToDouble(IGH_Goo goo, out double value)
        {
            value = 0.0;
            if (goo == null)
                return false;

            object v = goo.ScriptVariable();
            if (v == null)
                return false;

            switch (v)
            {
                case double d:
                    value = d;
                    return true;
                case float f:
                    value = f;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
            }

            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static char SelectAxis(int axisInput, BoundingBox globalBox)
        {
            if (axisInput == 1) return 'x';
            if (axisInput == 2) return 'y';
            if (axisInput == 3) return 'z';

            double dx = globalBox.Max.X - globalBox.Min.X;
            double dy = globalBox.Max.Y - globalBox.Min.Y;
            double dz = globalBox.Max.Z - globalBox.Min.Z;

            if (dz >= dx && dz >= dy) return 'z';
            if (dy >= dx) return 'y';
            return 'x';
        }

        private static double GradientFactorOfPoint(Point3d pt, char axis, double lo, double hi)
        {
            if (Math.Abs(hi - lo) < 1e-12)
                return 0.0;

            double v = axis == 'x' ? pt.X : (axis == 'y' ? pt.Y : pt.Z);
            double f = (v - lo) / (hi - lo);
            if (f < 0.0) f = 0.0;
            if (f > 1.0) f = 1.0;
            return f;
        }

        private static double ThicknessFromFactor(int mode, double factor, double minT, double maxT)
        {
            if (mode == 0) return minT;
            if (mode == 1) return maxT;
            if (mode == 2) return minT + factor * (maxT - minT);
            return minT;
        }

        private static Drawing.Color ColorFromThickness(double t, double minT, double maxT)
        {
            double c = Math.Abs(maxT - minT) < 1e-12 ? 0.0 : (t - minT) / (maxT - minT);
            if (c < 0.0) c = 0.0;
            if (c > 1.0) c = 1.0;
            int r = (int)(255 * c);
            int b = (int)(255 * (1.0 - c));
            return Drawing.Color.FromArgb(255, r, 0, b);
        }

        private static RebuildResult RebuildFit(Brep brep, int nU, int nV)
        {
            var surfaces = new List<Surface>();
            var points = new List<Point3d>();

            for (int fi = 0; fi < brep.Faces.Count; fi++)
            {
                BrepFace face = brep.Faces[fi];
                List<List<Point3d>> grid = FaceGrid(face, nU, nV);
                if (grid == null || grid.Count < 2 || grid[0].Count < 2)
                    continue;

                int rows = grid.Count;
                int cols = grid[0].Count;
                var flat = new List<Point3d>(rows * cols);
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        Point3d pt = grid[r][c];
                        flat.Add(pt);
                        points.Add(pt);
                    }
                }

                NurbsSurface fit = NurbsSurface.CreateThroughPoints(flat, rows, cols, 3, 3, false, false);
                if (fit != null)
                    surfaces.Add(fit);
            }

            return new RebuildResult(surfaces, points);
        }

        private static List<List<Point3d>> FaceGrid(BrepFace face, int nLong, int nCross)
        {
            Surface srf = face.UnderlyingSurface();
            if (srf == null)
                return null;

            bool InFace(double u, double v)
            {
                PointFaceRelation rel = face.IsPointOnFace(u, v);
                return rel != PointFaceRelation.Exterior;
            }

            Interval uDom0 = srf.Domain(0);
            Interval vDom0 = srf.Domain(1);

            double extU = srf.PointAt(uDom0.Min, vDom0.Mid).DistanceTo(srf.PointAt(uDom0.Max, vDom0.Mid));
            double extV = srf.PointAt(uDom0.Mid, vDom0.Min).DistanceTo(srf.PointAt(uDom0.Mid, vDom0.Max));
            bool swapUV = extV > extU;

            Interval longDom = swapUV ? srf.Domain(1) : srf.Domain(0);
            Interval crossDom = swapUV ? srf.Domain(0) : srf.Domain(1);

            Point3d Eval(double sLong, double sCross)
            {
                if (swapUV)
                {
                    return srf.PointAt(
                        crossDom.Min + sCross * crossDom.Length,
                        longDom.Min + sLong * longDom.Length);
                }

                return srf.PointAt(
                    longDom.Min + sLong * longDom.Length,
                    crossDom.Min + sCross * crossDom.Length);
            }

            bool InFaceLs(double sLong, double sCross)
            {
                if (swapUV)
                {
                    return InFace(
                        crossDom.Min + sCross * crossDom.Length,
                        longDom.Min + sLong * longDom.Length);
                }

                return InFace(
                    longDom.Min + sLong * longDom.Length,
                    crossDom.Min + sCross * crossDom.Length);
            }

            (double a, double b)? CrossRangeAt(double sLong)
            {
                const int samples = 80;
                double min = double.MaxValue;
                double max = double.MinValue;
                bool found = false;

                for (int j = 0; j <= samples; j++)
                {
                    double sc = j / (double)samples;
                    if (!InFaceLs(sLong, sc))
                        continue;

                    if (sc < min) min = sc;
                    if (sc > max) max = sc;
                    found = true;
                }

                if (!found) return null;
                return (min, max);
            }

            List<double> LongParamsWithExtrema(int n)
            {
                int isoDir = swapUV ? 1 : 0;
                double[] crossPositions = { crossDom.Min, crossDom.Mid, crossDom.Max };
                const int M = 60;
                var extrema = new List<double>();

                for (int ci = 0; ci < crossPositions.Length; ci++)
                {
                    Curve iso;
                    try
                    {
                        iso = srf.IsoCurve(isoDir, crossPositions[ci]);
                    }
                    catch
                    {
                        continue;
                    }

                    if (iso == null)
                        continue;

                    Interval id = iso.Domain;
                    var tangs = new Vector3d[M + 1];
                    for (int j = 0; j <= M; j++)
                    {
                        double t = id.Min + (j / (double)M) * id.Length;
                        tangs[j] = iso.TangentAt(t);
                    }

                    var ang = new double[M + 1];
                    for (int j = 1; j < M; j++)
                    {
                        Vector3d a = tangs[j - 1];
                        Vector3d b = tangs[j + 1];
                        if (a.Length > 0.0 && b.Length > 0.0)
                        {
                            a.Unitize();
                            b.Unitize();
                            double dot = Math.Max(-1.0, Math.Min(1.0, a * b));
                            ang[j] = Math.Acos(dot);
                        }
                    }

                    for (int j = 1; j < M; j++)
                    {
                        double left = ang[j - 1];
                        double right = j + 1 <= M ? ang[j + 1] : 0.0;
                        if (ang[j] >= left && ang[j] >= right && ang[j] > 1e-4)
                            extrema.Add(j / (double)M);
                    }
                }

                extrema.Sort();
                var merged = new List<double>();
                for (int i = 0; i < extrema.Count; i++)
                {
                    double e = extrema[i];
                    if (merged.Count == 0 || Math.Abs(e - merged[merged.Count - 1]) > 0.03)
                        merged.Add(e);
                }

                var paramSet = new SortedSet<double>();
                int baseN = Math.Max(n, 2);
                for (int i = 0; i < baseN; i++)
                    paramSet.Add(i / (double)(baseN - 1));

                const double d = 0.02;
                var forced = new List<double>(merged) { 0.0, 1.0 };
                for (int i = 0; i < forced.Count; i++)
                {
                    double e = forced[i];
                    paramSet.Add(e);
                    paramSet.Add(Math.Max(0.0, e - d));
                    paramSet.Add(Math.Min(1.0, e + d));
                }

                return new List<double>(paramSet);
            }

            List<double> longParams = LongParamsWithExtrema(nLong);

            var grid = new List<List<Point3d>>();
            for (int i = 0; i < longParams.Count; i++)
            {
                double sLong = longParams[i];
                var range = CrossRangeAt(sLong);
                if (!range.HasValue)
                    continue;

                double c0 = range.Value.a;
                double c1 = range.Value.b;
                var row = new List<Point3d>(Math.Max(1, nCross));
                for (int iv = 0; iv < nCross; iv++)
                {
                    double sc = nCross > 1 ? iv / (double)(nCross - 1) : 0.0;
                    row.Add(Eval(sLong, c0 + sc * (c1 - c0)));
                }

                grid.Add(row);
            }

            return grid;
        }

        protected override Drawing.Bitmap Icon => IconHelper.Load("SurfaceThicknessHeatmapIcon.png");
        public override Guid ComponentGuid => new Guid("ED47AE1C-5A12-4BF4-9ACD-A3F0AE60E2D2");

        private readonly struct RebuildResult
        {
            public readonly List<Surface> Surfaces;
            public readonly List<Point3d> RasterPoints;

            public RebuildResult(List<Surface> surfaces, List<Point3d> rasterPoints)
            {
                Surfaces = surfaces;
                RasterPoints = rasterPoints;
            }
        }
    }
}
