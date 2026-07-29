using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    public class OverlapTesterVariableWidthComponent : GH_Component
    {
        private sealed class TeilParams
        {
            public int Axis;
            public int Mode;
            public double MinT;
            public double MaxT;
            public double Lo;
            public double Hi;

            public TeilParams(int axis, int mode, double minT, double maxT, double lo, double hi)
            {
                Axis = axis;
                Mode = mode;
                MinT = minT;
                MaxT = maxT;
                Lo = lo;
                Hi = hi;
            }
        }

        public OverlapTesterVariableWidthComponent()
          : base("Overlap Tester Var Width", "OverlapV12",
              "False-color overlap tester with optional variable width per-part logic (Part Geometry / Geometry ID / Part Info).",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "Geo", "Geometry or Mesh to analyze.", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Slice Plane", "Plane", "Slice/direction plane. Normal defines print direction.", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Mesh Resolution", "MeshR", "<= 0 uses minimal/turbo meshing.", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Layer Height", "LH", "Layer height in mm.", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Layer Width", "LW", "Base path width in mm.", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Threshold", "Th", "Allowed overlap factor (0..1+).", GH_ParamAccess.item, 0.5);
            pManager.AddBooleanParameter("Show Contours", "Contours", "Compute contour curves.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Analysis Step", "Step", "Contour step multiplier in layers.", GH_ParamAccess.item, 1);
            pManager.AddBooleanParameter("Only Underside", "Under", "If true, only underside normals are critical.", GH_ParamAccess.item, false);

            pManager.AddGeometryParameter("Part Geometry", "PartGeo", "Optional part geometries for variable-width logic.", GH_ParamAccess.list);
            pManager.AddTextParameter("Geo ID", "GeoID", "Optional IDs aligned to Teil Geo.", GH_ParamAccess.list);
            pManager.AddTextParameter("Part Info", "PartInfo", "Optional info lines: id;axis;mode;minT;maxT;lo;hi", GH_ParamAccess.list);

            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Heat Mesh", "Heat", "Mesh with vertex danger colors.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Contours", "C", "Optional contour curves.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Danger Levels", "D", "Per-vertex normalized danger 0..1.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Shift XY", "Shift", "Maximum computed XY shift in mm.", GH_ParamAccess.item);
            pManager.AddTextParameter("Report", "R", "Execution and profile report.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object geometryInput = null;
            Plane slicePlane = Plane.WorldXY;
            double meshResol = 1.0;
            double layerHeight = 0.2;
            double layerWidth = 0.4;
            double threshold = 0.5;
            bool showContours = false;
            int analysisStep = 1;
            bool onlyUnderside = false;

            var teilGeo = new List<GeometryBase>();
            var geoIds = new List<string>();
            var teilInfo = new List<string>();

            if (!DA.GetData(0, ref geometryInput) || geometryInput == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Geometry input.");
                return;
            }

            DA.GetData(1, ref slicePlane);
            DA.GetData(2, ref meshResol);
            DA.GetData(3, ref layerHeight);
            DA.GetData(4, ref layerWidth);
            DA.GetData(5, ref threshold);
            DA.GetData(6, ref showContours);
            DA.GetData(7, ref analysisStep);
            DA.GetData(8, ref onlyUnderside);
            DA.GetDataList(9, teilGeo);
            DA.GetDataList(10, geoIds);
            DA.GetDataList(11, teilInfo);

            meshResol = FallbackPositive(meshResol, 1.0);
            layerHeight = FallbackPositive(layerHeight, 0.2);
            layerWidth = FallbackPositive(layerWidth, 0.4);
            threshold = FallbackPositive(threshold, 0.5);
            if (analysisStep < 1) analysisStep = 1;

            var report = new List<string>();
            var contours = new List<Curve>();
            var dangerLevels = new List<double>();
            double maxShiftXY = 0.0;

            var totalSw = Stopwatch.StartNew();
            var profile = new List<Tuple<string, double>>();
            var sw = Stopwatch.StartNew();

            Mesh workMesh = BuildWorkMesh(geometryInput, meshResol, report, profile, ref sw);
            if (workMesh == null || workMesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh creation failed.");
                report.Add("ERROR: Mesh creation failed.");
                DA.SetDataList(4, report);
                return;
            }

            if (!slicePlane.IsValid)
                slicePlane = Plane.WorldXY;

            var n = slicePlane.Normal;
            n.Unitize();

            if (workMesh.Normals.Count != workMesh.Vertices.Count)
                workMesh.Normals.ComputeNormals();

            Tick(profile, "2a. Normals ensure", ref sw);

            int vertexCount = workMesh.Vertices.Count;
            var colors = new Drawing.Color[vertexCount];

            var partData = BuildPartData(teilGeo, geoIds, teilInfo, layerWidth);
            bool haveParts = partData.Item1.Count > 0 && partData.Item1.Count == partData.Item2.Count;
            double minWidthSeen = double.MaxValue;
            double maxWidthSeen = double.MinValue;

            Tick(profile, "2b. Build part data", ref sw);

            for (int i = 0; i < vertexCount; i++)
            {
                Point3f v = workMesh.Vertices[i];
                Vector3f vn = workMesh.Normals[i];

                double nz = vn.X * n.X + vn.Y * n.Y + vn.Z * n.Z;
                double localWidth = layerWidth;

                if (haveParts)
                {
                    localWidth = LocalWidthAt(v.X, v.Y, v.Z, partData.Item1, partData.Item2, layerWidth);
                    if (localWidth < minWidthSeen) minWidthSeen = localWidth;
                    if (localWidth > maxWidthSeen) maxWidthSeen = localWidth;
                }

                double maxAllowedShift = localWidth * threshold;
                double shiftXY = 0.0;

                double eff;
                if (onlyUnderside)
                {
                    eff = nz < -1e-6 ? -nz : 0.0;
                }
                else
                {
                    eff = Math.Abs(nz);
                    if (eff <= 1e-6)
                        eff = 0.0;
                }

                if (eff > 0.0)
                {
                    double nxySq = 1.0 - eff * eff;
                    shiftXY = nxySq < 1e-12 ? maxAllowedShift * 10.0 : layerHeight * (eff / Math.Sqrt(nxySq));
                }

                if (shiftXY > maxShiftXY)
                    maxShiftXY = shiftXY;

                double t = maxAllowedShift > 0.0 ? shiftXY / maxAllowedShift : 0.0;
                if (t < 0.0) t = 0.0;
                if (t > 1.0) t = 1.0;

                dangerLevels.Add(t);
                colors[i] = DangerColor(t);
            }

            Tick(profile, "2c. Vertex loop", ref sw);

            workMesh.VertexColors.SetColors(colors);
            Tick(profile, "2d. Set vertex colors", ref sw);

            if (showContours)
            {
                BuildContours(workMesh, slicePlane, layerHeight, analysisStep, contours);
                Tick(profile, "3. Build contours", ref sw);
            }

            totalSw.Stop();

            report.Add($"v12 (variable widths) | LayerHeight:{layerHeight} | Base LayerWidth:{layerWidth} | MaxOverlap:{threshold * 100:0.##}%");
            report.Add($"Vertices: {vertexCount}");
            report.Add(haveParts
                ? $"Local width range: {minWidthSeen:0.###}..{maxWidthSeen:0.###} mm"
                : $"Constant width mode: {layerWidth:0.###} mm");
            report.Add($"Max Shift XY: {maxShiftXY:0.###} mm");
            report.Add($"Contours: {contours.Count}");
            report.Add($"Total time: {totalSw.Elapsed.TotalMilliseconds:0.##} ms");
            report.Add("---- Profile ----");
            for (int i = 0; i < profile.Count; i++)
            {
                report.Add($"{profile[i].Item1}: {profile[i].Item2:0.##} ms");
            }

            DA.SetData(0, workMesh);
            DA.SetDataList(1, contours);
            DA.SetDataList(2, dangerLevels);
            DA.SetData(3, maxShiftXY);
            DA.SetDataList(4, report);
        }

        private static double FallbackPositive(double v, double fallback)
        {
            return v <= 0.0 ? fallback : v;
        }

        private static void Tick(List<Tuple<string, double>> profile, string label, ref Stopwatch sw)
        {
            sw.Stop();
            profile.Add(Tuple.Create(label, sw.Elapsed.TotalMilliseconds));
            sw = Stopwatch.StartNew();
        }

        private static Mesh BuildWorkMesh(object input, double meshResol, List<string> report, List<Tuple<string, double>> profile, ref Stopwatch sw)
        {
            Mesh mesh = null;

            if (input is Mesh inMesh)
            {
                mesh = inMesh.DuplicateMesh();
                Tick(profile, "1a. Input mesh duplicate", ref sw);
            }
            else
            {
                Brep brep = null;
                if (input is Brep b)
                    brep = b;
                else if (input is Surface s)
                    brep = s.ToBrep();
                else if (input is Extrusion ex)
                    brep = ex.ToBrep();
                else if (input is GeometryBase gb)
                    brep = Brep.TryConvertBrep(gb);

                Tick(profile, "1a. Geometry to brep", ref sw);

                if (brep != null)
                {
                    MeshingParameters mp;
                    if (meshResol <= 0.0)
                    {
                        mp = MeshingParameters.Minimal;
                    }
                    else
                    {
                        mp = MeshingParameters.Default;
                        mp.MaximumEdgeLength = meshResol;
                        mp.MinimumEdgeLength = meshResol * 0.1;
                        mp.GridMinCount = 16;
                        mp.GridMaxCount = 0;
                        mp.RefineGrid = true;
                        mp.JaggedSeams = false;
                    }

                    Mesh[] arr = Mesh.CreateFromBrep(brep, mp);
                    Tick(profile, "1b. Create mesh from brep", ref sw);

                    if (arr != null && arr.Length > 0)
                    {
                        mesh = new Mesh();
                        for (int i = 0; i < arr.Length; i++)
                        {
                            if (arr[i] != null)
                                mesh.Append(arr[i]);
                        }
                        Tick(profile, "1c. Append submeshes", ref sw);
                    }
                }
            }

            if (mesh != null)
            {
                mesh.Vertices.CombineIdentical(true, true);
                mesh.Normals.ComputeNormals();
                Tick(profile, "1d. Weld + normals", ref sw);
                report.Add($"Mesh prepared: {mesh.Vertices.Count} vertices.");
            }

            return mesh;
        }

        private static Tuple<List<TeilParams>, List<Point3d>> BuildPartData(
            List<GeometryBase> teilGeo,
            List<string> geoIds,
            List<string> teilInfo,
            double fallbackLayerWidth)
        {
            var infoById = new Dictionary<string, TeilParams>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < teilInfo.Count; i++)
            {
                string line = teilInfo[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] p = line.Split(';');
                if (p.Length < 6)
                    continue;

                try
                {
                    string id = p[0];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    TeilParams tp;
                    if (p.Length >= 7)
                    {
                        tp = new TeilParams(
                            ParseInt(p[1], 3),
                            ParseInt(p[2], 0),
                            ParseDouble(p[3], fallbackLayerWidth),
                            ParseDouble(p[4], fallbackLayerWidth),
                            ParseDouble(p[5], 0.0),
                            ParseDouble(p[6], 1.0));
                    }
                    else
                    {
                        tp = new TeilParams(
                            ParseInt(p[1], 3),
                            2,
                            ParseDouble(p[2], fallbackLayerWidth),
                            ParseDouble(p[3], fallbackLayerWidth),
                            ParseDouble(p[4], 0.0),
                            ParseDouble(p[5], 1.0));
                    }

                    infoById[id] = tp;
                }
                catch
                {
                    // Ignore malformed part info rows.
                }
            }

            var paramsByGeo = new List<TeilParams>();
            var centers = new List<Point3d>();

            for (int i = 0; i < teilGeo.Count; i++)
            {
                string gid = i < geoIds.Count ? (geoIds[i] ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(gid) && infoById.TryGetValue(gid, out TeilParams parsed))
                {
                    paramsByGeo.Add(parsed);
                }
                else
                {
                    paramsByGeo.Add(new TeilParams(3, 0, fallbackLayerWidth, fallbackLayerWidth, 0.0, 1.0));
                }

                try
                {
                    BoundingBox bb = teilGeo[i]?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    centers.Add(bb.IsValid ? bb.Center : Point3d.Origin);
                }
                catch
                {
                    centers.Add(Point3d.Origin);
                }
            }

            return Tuple.Create(paramsByGeo, centers);
        }

        private static double LocalWidthAt(double px, double py, double pz, List<TeilParams> teilParams, List<Point3d> centers, double fallback)
        {
            if (teilParams == null || centers == null || teilParams.Count == 0 || teilParams.Count != centers.Count)
                return fallback;

            int bestIndex = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < centers.Count; i++)
            {
                Point3d c = centers[i];
                double dx = px - c.X;
                double dy = py - c.Y;
                double dz = pz - c.Z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return fallback;

            TeilParams tp = teilParams[bestIndex];
            if (tp.Mode == 0)
                return tp.MinT;
            if (tp.Mode == 1)
                return tp.MaxT;

            double v = tp.Axis == 1 ? px : (tp.Axis == 2 ? py : pz);
            double f;
            if (Math.Abs(tp.Hi - tp.Lo) < 1e-12)
            {
                f = 0.0;
            }
            else
            {
                f = (v - tp.Lo) / (tp.Hi - tp.Lo);
                if (f < 0.0) f = 0.0;
                if (f > 1.0) f = 1.0;
            }

            return tp.MinT + f * (tp.MaxT - tp.MinT);
        }

        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            if (double.TryParse((s ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return (int)Math.Round(d);
            return fallback;
        }

        private static double ParseDouble(string s, double fallback)
        {
            if (double.TryParse((s ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }

        private static Drawing.Color DangerColor(double t)
        {
            if (t < 0.25)
            {
                double s = t * 4.0;
                return Drawing.Color.FromArgb(255, 0, (int)(255 * s), 255);
            }

            if (t < 0.50)
            {
                double s = (t - 0.25) * 4.0;
                return Drawing.Color.FromArgb(255, 0, 255, (int)(255 * (1.0 - s)));
            }

            if (t < 0.75)
            {
                double s = (t - 0.50) * 4.0;
                return Drawing.Color.FromArgb(255, (int)(255 * s), 255, 0);
            }

            {
                double s = (t - 0.75) * 4.0;
                return Drawing.Color.FromArgb(255, 255, (int)(255 * (1.0 - s)), 0);
            }
        }

        private static void BuildContours(Mesh mesh, Plane slicePlane, double layerHeight, int analysisStep, List<Curve> contours)
        {
            if (mesh == null || !mesh.IsValid)
                return;

            Vector3d n = slicePlane.Normal;
            n.Unitize();

            Point3d[] corners = mesh.GetBoundingBox(true).GetCorners();
            if (corners == null || corners.Length == 0)
                return;

            double hMin = double.MaxValue;
            double hMax = double.MinValue;
            Point3d origin = slicePlane.Origin;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3d v = corners[i] - origin;
                double h = v.X * n.X + v.Y * n.Y + v.Z * n.Z;
                if (h < hMin) hMin = h;
                if (h > hMax) hMax = h;
            }

            double step = layerHeight * analysisStep;
            if (step <= 0.0)
                return;

            double hCur = hMin;
            double end = hMax + step * 0.01;

            while (hCur <= end)
            {
                Plane cut = new Plane(origin + n * hCur, slicePlane.XAxis, slicePlane.YAxis);
                Polyline[] polys = Intersection.MeshPlane(mesh, cut);
                if (polys != null)
                {
                    for (int i = 0; i < polys.Length; i++)
                    {
                        Polyline pl = polys[i];
                        if (pl.Count >= 2)
                        {
                            Curve c = pl.ToNurbsCurve();
                            if (c != null)
                                contours.Add(c);
                        }
                    }
                }

                hCur += step;
            }
        }

        protected override Drawing.Bitmap Icon => IconHelper.Load("OverlapTesterVariableWidthIcon.png");
        public override Guid ComponentGuid => new Guid("D65C658E-6AE5-4918-B737-D36F9E3C1640");
    }
}
