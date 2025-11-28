using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.CNC
{
    public class CNCProgramComponent : GH_Component
    {
        public CNCProgramComponent()
          : base("CNC - Program", "CNC",
              "Generates boustrophedon (zigzag) toolpath for CNC milling with Zünd PLT output. Supports SIMPLE and EXTENDED header modes.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "Geo", "Mesh, Brep, or Surface to generate toolpath from", GH_ParamAccess.item);
            pManager.AddNumberParameter("Step X", "dx", "Step size in X direction (mm)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Step Y", "dy", "Step size in Y direction (mm)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Margin", "Margin", "Margin around geometry bounding box (mm)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Start Left", "StartLeft", "True to start from left, False to start from right", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Make PLT", "MakePLT", "True to generate PLT output", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Feed Speed", "VW", "Feed speed VS (mm/s)", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Rapid Speed", "VF", "Rapid speed VU (mm/s)", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Z Up", "Zup", "Z retract height (mm)", GH_ParamAccess.item, 5.0);
            pManager.AddIntegerParameter("Tool", "Tool", "Tool number (SP command, EXTENDED mode only)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Vacuum", "Vacuum", "Enable vacuum (PB9, EXTENDED mode only)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Underlay", "Underlay", "Enable underlay (XX308, EXTENDED mode only)", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Header Mode", "HeaderMode", "Header mode: 'SIMPLE' or 'EXTENDED'", GH_ParamAccess.item, "SIMPLE");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Path", "Path", "Complete toolpath as polyline curve", GH_ParamAccess.item);
            pManager.AddCurveParameter("Segments", "Segments", "Individual path segments as curves", GH_ParamAccess.list);
            pManager.AddTextParameter("PLT", "PLT", "Zünd PLT file content", GH_ParamAccess.item);
            pManager.AddTextParameter("Stats", "Stats", "Toolpath statistics and information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo geo = null;
            double dx = 1.0;
            double dy = 1.0;
            double margin = 0.0;
            bool startLeft = true;
            bool makePLT = true;
            double feedSpeed = 10.0;
            double rapidSpeed = 50.0;
            double zUp = 5.0;
            int tool = 0;
            bool vacuum = false;
            bool underlay = false;
            string headerMode = "SIMPLE";

            if (!DA.GetData(0, ref geo)) return;
            if (!DA.GetData(1, ref dx)) return;
            if (!DA.GetData(2, ref dy)) return;
            DA.GetData(3, ref margin);
            DA.GetData(4, ref startLeft);
            DA.GetData(5, ref makePLT);
            DA.GetData(6, ref feedSpeed);
            DA.GetData(7, ref rapidSpeed);
            DA.GetData(8, ref zUp);
            DA.GetData(9, ref tool);
            DA.GetData(10, ref vacuum);
            DA.GetData(11, ref underlay);
            DA.GetData(12, ref headerMode);

            // Validate inputs
            dx = Math.Max(dx, 1e-6);
            dy = Math.Max(dy, 1e-6);
            feedSpeed = Math.Max(feedSpeed, 0.1);
            rapidSpeed = Math.Max(rapidSpeed, 0.1);

            headerMode = (headerMode ?? "SIMPLE").ToUpper().Trim();
            if (headerMode != "SIMPLE" && headerMode != "EXTENDED")
            {
                headerMode = "SIMPLE";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "HeaderMode must be 'SIMPLE' or 'EXTENDED'. Using 'SIMPLE'.");
            }

            // Convert geometry to mesh
            Mesh mesh = EnsureMesh(geo);
            if (mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry could not be converted to mesh.");
                DA.SetData(0, null);
                DA.SetDataList(1, new List<Curve>());
                DA.SetData(2, "");
                DA.SetData(3, "ERROR: Geometry could not be converted to mesh.");
                return;
            }

            // Get bounding box with margin
            BoundingBox bbox = mesh.GetBoundingBox(true);
            bbox.Inflate(margin, margin, 0.0);

            // Generate boustrophedon path
            var (segments, pathPoints) = GenerateBoustrophedonPath(mesh, bbox, dx, dy, startLeft);

            // Create geometry outputs
            List<Curve> segmentCurves = new List<Curve>();
            foreach (var seg in segments)
            {
                if (seg.Count >= 2)
                {
                    Polyline poly = new Polyline(seg);
                    segmentCurves.Add(new PolylineCurve(poly));
                }
            }

            Curve pathCurve = null;
            if (pathPoints.Count >= 2)
            {
                Polyline pathPoly = new Polyline(pathPoints);
                pathCurve = new PolylineCurve(pathPoly);
            }

            // Calculate statistics
            double totalLength = segments.Sum(s => s.Count >= 2 ? new Polyline(s).Length : 0.0);
            string stats = $"Segments={segments.Count} | Points={pathPoints.Count} | Length={totalLength:F2} mm | dx={dx:F3}, dy={dy:F3} | VS={feedSpeed:F3} mm/s, VU={rapidSpeed:F3} mm/s | Mode={headerMode}";

            // Generate PLT
            string plt = "";
            if (makePLT)
            {
                Point3d parkXY = new Point3d(2500.0, 0.0, 0.0); // PU250000,0 like in sample
                plt = BuildPLTFromPath(pathPoints, feedSpeed, rapidSpeed, zUp, tool, vacuum, underlay, parkXY, headerMode);
            }

            DA.SetData(0, pathCurve);
            DA.SetDataList(1, segmentCurves);
            DA.SetData(2, plt);
            DA.SetData(3, stats);
        }

        private Mesh EnsureMesh(IGH_GeometricGoo geo)
        {
            if (geo == null) return null;

            // Handle Mesh
            if (geo is GH_Mesh ghMesh)
            {
                Mesh m = ghMesh.Value.DuplicateMesh();
                m.UnifyNormals();
                m.Normals.ComputeNormals();
                return m;
            }

            // Handle Brep
            if (geo is GH_Brep ghBrep)
            {
                Brep brep = ghBrep.Value;
                Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                if (meshes == null || meshes.Length == 0) return null;

                Mesh combined = new Mesh();
                foreach (var m in meshes)
                {
                    combined.Append(m);
                }
                combined.UnifyNormals();
                combined.Normals.ComputeNormals();
                return combined;
            }

            // Handle Surface - treat same as Brep
            if (geo is GH_Surface ghSurface)
            {
                // GH_Surface.Value can be Brep or Surface, handle both cases
                object surfaceValue = ghSurface.Value;
                Brep brep = null;
                
                if (surfaceValue is Brep brepValue)
                {
                    brep = brepValue;
                }
                else if (surfaceValue is Surface surface)
                {
                    brep = surface.ToBrep();
                }
                
                if (brep != null && brep.IsValid)
                {
                    Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
                    if (meshes != null && meshes.Length > 0)
                    {
                        Mesh combined = new Mesh();
                        foreach (var m in meshes)
                        {
                            if (m != null) combined.Append(m);
                        }
                        combined.UnifyNormals();
                        combined.Normals.ComputeNormals();
                        return combined;
                    }
                }
            }

            return null;
        }

        private double? ZAtXYMesh(Mesh mesh, double x, double y, double zMin, double zMax)
        {
            Point3d a = new Point3d(x, y, zMax + 1.0);
            Point3d b = new Point3d(x, y, zMin - 1.0);
            Line line = new Line(a, b);

            var intersections = Rhino.Geometry.Intersect.Intersection.MeshLine(mesh, line);
            if (intersections != null)
            {
                var intersectionList = intersections.ToList();
                if (intersectionList.Count > 0)
                {
                    double maxZ = double.MinValue;
                    foreach (var pt in intersectionList)
                    {
                        if (pt.Z > maxZ) maxZ = pt.Z;
                    }
                    return maxZ;
                }
            }
            return null;
        }

        private (List<List<Point3d>> segments, List<Point3d> pathPoints) GenerateBoustrophedonPath(
            Mesh mesh, BoundingBox bbox, double dx, double dy, bool startLeft)
        {
            double xMin = bbox.Min.X;
            double xMax = bbox.Max.X;
            double yMin = bbox.Min.Y;
            double yMax = bbox.Max.Y;
            double zMin = bbox.Min.Z;
            double zMax = bbox.Max.Z;

            // Generate Y rows
            List<double> yVals = new List<double>();
            for (double y = yMin; y <= yMax + 1e-9; y += dy)
            {
                yVals.Add(y);
            }

            List<List<Point3d>> segments = new List<List<Point3d>>();

            for (int row = 0; row < yVals.Count; row++)
            {
                double yy = yVals[row];

                // Generate X samples
                List<double> xs = new List<double>();
                for (double x = xMin; x <= xMax + 1e-9; x += dx)
                {
                    xs.Add(x);
                }

                // Boustrophedon direction
                bool forward = (startLeft && (row % 2 == 0)) || (!startLeft && (row % 2 == 1));
                List<double> xSeq = forward ? xs : xs.AsEnumerable().Reverse().ToList();

                List<Point3d> pts = new List<Point3d>();
                foreach (double xx in xSeq)
                {
                    double? z = ZAtXYMesh(mesh, xx, yy, zMin, zMax);
                    if (!z.HasValue)
                    {
                        if (pts.Count >= 2)
                        {
                            segments.Add(new List<Point3d>(pts));
                        }
                        pts.Clear();
                        continue;
                    }
                    pts.Add(new Point3d(xx, yy, z.Value));
                }

                if (pts.Count >= 2)
                {
                    segments.Add(pts);
                }
            }

            // Connect segments into continuous path
            List<Point3d> pathPoints = new List<Point3d>();
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (i == 0)
                {
                    pathPoints.AddRange(seg);
                    continue;
                }

                Point3d prev = pathPoints[pathPoints.Count - 1];
                Point3d first = seg[0];

                // Connector along Y at edge X = first.X
                double dyAbs = Math.Abs(first.Y - prev.Y);
                int steps = Math.Max((int)(dyAbs / Math.Max(dx, 1e-6)), 1);

                for (int t = 1; t <= steps; t++)
                {
                    double tt = (double)t / steps;
                    double xi = first.X;
                    double yi = prev.Y + tt * (first.Y - prev.Y);
                    double? zi = ZAtXYMesh(mesh, xi, yi, zMin, zMax);
                    if (!zi.HasValue)
                    {
                        zi = prev.Z;
                    }
                    pathPoints.Add(new Point3d(xi, yi, zi.Value));
                }

                pathPoints.AddRange(seg);
            }

            return (segments, pathPoints);
        }

        private int UU(double mm)
        {
            // mm -> 1/100 mm (Zünd Units)
            return (int)Math.Round(mm * 100.0);
        }

        private string BuildPLTFromPath(
            List<Point3d> pathPoints,
            double feedSpeed,      // VS (mm/s)
            double rapidSpeed,     // VU (mm/s)
            double zUp,
            int tool,
            bool vacuum,
            bool underlay,
            Point3d parkXY,
            string mode)
        {
            List<string> lines = new List<string>();

            // Header (Sample-compatible)
            lines.Add("PB2,1;");
            lines.Add("ZT1;MA;");
            lines.Add($"VS{feedSpeed:G6};");
            lines.Add($"VU{rapidSpeed:G6};");
            lines.Add(""); // Empty line like in sample

            if (mode == "EXTENDED")
            {
                if (vacuum) lines.Add("PB9,1;");
                if (underlay) lines.Add("XX308,1;");
                if (tool > 0) lines.Add($"SP{tool};");
                lines.Add("XX62;");
                lines.Add($"ZP{UU(zUp)},{0};");
                lines.Add("XX81;");
                lines.Add("AS2,4;");
                lines.Add("XX82;");
            }

            // Path
            if (pathPoints == null || pathPoints.Count == 0)
            {
                lines.Add("ZT0;");
                lines.Add($"PU{UU(parkXY.X)},{UU(parkXY.Y)};");
                lines.Add("PB2,0;");
                return string.Join("\n", lines);
            }

            Point3d p0 = pathPoints[0];
            lines.Add($"PU{UU(p0.X)},{UU(p0.Y)};");
            lines.Add($"MW{UU(p0.X)},{UU(p0.Y)},{UU(p0.Z)};");

            for (int i = 1; i < pathPoints.Count; i++)
            {
                Point3d p = pathPoints[i];
                lines.Add($"MW{UU(p.X)},{UU(p.Y)},{UU(p.Z)};");
            }

            // Trailer
            lines.Add("ZT0;");
            lines.Add($"PU{UU(parkXY.X)},{UU(parkXY.Y)};");
            lines.Add("PB2,0;");

            return string.Join("\n", lines);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CNCProgramIcon.png");
        public override Guid ComponentGuid => new Guid("C1D2E3F4-A5B6-7890-CDEF-123456789ABC");
    }
}

