using Rhino.Geometry;
using LARGERslicer.Toolpaths;

/// <summary>Models for the bench: what students bring, and what hurts.</summary>
public static class Parts
{
    public static Mesh Field(double w, double h, double d, Func<double, double, double> f)
    {
        int nx = (int)Math.Round(w / d), ny = (int)Math.Round(h / d); var m = new Mesh();
        for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) m.Vertices.Add(w * i / nx, h * j / ny, f(w * i / nx, h * j / ny));
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) m.Faces.AddFace(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i);
        m.Normals.ComputeNormals();
        return m;
    }
    static Brep Bx(double x0, double y0, double z0, double x1, double y1, double z1) => new Box(new BoundingBox(x0, y0, z0, x1, y1, z1)).ToBrep();
    static Brep Cyl(double x, double y, double r, double z0, double z1) => new Cylinder(new Circle(new Plane(new Point3d(x, y, z0), Vector3d.ZAxis), r), z1 - z0).ToBrep(true, true);
    static Brep Diff(Brep a, params Brep[] b) => Brep.CreateBooleanDifference(new[] { a }, b, 0.001)[0];
    static Brep Uni(params Brep[] b) { var u = Brep.CreateBooleanUnion(b, 0.001); return u[0]; }
    static Brep Pl(double h, params double[] xy) { var p = new Polyline(); for (int i = 0; i < xy.Length; i += 2) p.Add(xy[i], xy[i + 1], 0); p.Add(p[0]); var b = Extrusion.Create(p.ToNurbsCurve(), h, true).ToBrep(); var bb = b.GetBoundingBox(true); b.Translate(0, 0, -bb.Min.Z); return b; }

    /// <summary>Raised letters as solids, with the holes of R, A, G and O.</summary>
    static IEnumerable<Brep> Word(string text, double size)
    {
        var style = new Rhino.DocObjects.DimensionStyle { TextHeight = size, Font = Rhino.DocObjects.Font.FromQuartetProperties("Arial", true, false) };
        var te = TextEntity.Create(text, new Plane(new Point3d(0, 0, 4), Vector3d.ZAxis), style, false, 0, 0);
        var curves = te.CreateCurves(style, false, 1.0, 0.0);
        var flat = Brep.CreatePlanarBreps(curves, 0.01);
        var bb = BoundingBox.Empty; foreach (var b in flat) bb.Union(b.GetBoundingBox(true));
        var move = Transform.Translation(120 - bb.Center.X, 35 - bb.Center.Y, 0) * Transform.Scale(bb.Center, Math.Min(1, 216 / (bb.Max.X - bb.Min.X)));
        foreach (var b in flat)
        {
            b.Transform(move);
            foreach (var solid in b.Faces.Select(f => f.CreateExtrusion(new LineCurve(new Point3d(0, 0, 0), new Point3d(0, 0, 7)), true))) yield return solid;
        }
    }

    public static List<(string, List<object>)> All()
    {
        var l = new List<(string, List<object>)>(); var rnd = new Random(7);
        void A(string n, params object[] o) => l.Add((n, o.ToList()));
        A("topography", DemoParts.Topography());
        A("terraced terrain (layer model)", Field(180, 130, 0.5, (x, y) => 2 * Math.Floor(6 + 3 * Math.Sin(x / 25) * Math.Cos(y / 22) + 2 * Math.Sin((x + y) / 18))));
        A("noisy scan", Field(150, 110, 1.0, (x, y) => 8 + 3 * Math.Sin(x / 14) * Math.Sin(y / 17) + (rnd.NextDouble() - 0.5) * 0.8));
        A("cliff", Field(160, 100, 0.5, (x, y) => x < 80 + 8 * Math.Sin(y / 12) ? 14 : 2 + 1.5 * Math.Sin(x / 9)));
        A("narrow canyon", Field(140, 100, 0.5, (x, y) => Math.Abs(y - 50 - 12 * Math.Sin(x / 20)) < 2.2 ? 2 : 13));
        A("sharp ridge", Field(140, 100, 0.5, (x, y) => Math.Max(1, 14 - 0.9 * Math.Abs(y - 50 - 10 * Math.Sin(x / 25)))));
        A("crater", Field(140, 140, 0.5, (x, y) => { double r = Math.Sqrt((x - 70) * (x - 70) + (y - 70) * (y - 70)); return 3 + 11 * Math.Exp(-(r - 35) * (r - 35) / 120); }));
        A("waves", Field(150, 100, 0.5, (x, y) => 7 + 5 * Math.Sin(x / 6)));
        A("tilted plane", Field(140, 100, 5, (x, y) => 1 + 0.1 * x));
        A("test stairs", DemoParts.TestStairs());
        A("flat plate", Bx(0, 0, 0, 120, 80, 8));
        A("stairs", Uni(Enumerable.Range(0, 5).Select(i => Bx(i * 24, 0, 0, 120, 70, 3 * (i + 1))).ToArray()));
        A("pyramid", Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(100, 0, 0), new Point3d(50, 50, 14), 0.01), Brep.CreateFromCornerPoints(new Point3d(100, 0, 0), new Point3d(100, 100, 0), new Point3d(50, 50, 14), 0.01), Brep.CreateFromCornerPoints(new Point3d(100, 100, 0), new Point3d(0, 100, 0), new Point3d(50, 50, 14), 0.01), Brep.CreateFromCornerPoints(new Point3d(0, 100, 0), new Point3d(0, 0, 0), new Point3d(50, 50, 14), 0.01));
        A("dome", Brep.CreateBooleanIntersection(new[] { new Sphere(new Point3d(100, 100, -50), 64).ToBrep() }, new[] { Bx(0, 0, 0, 200, 200, 50) }, 0.001)[0]);
        var city = new List<object> { Bx(0, 0, 0, 180, 130, 3) };
        for (int i = 0; i < 6; i++) for (int j = 0; j < 4; j++) if ((i + j) % 3 != 0) city.Add(Bx(10 + i * 28, 10 + j * 30, 2, 10 + i * 28 + 16, 10 + j * 30 + 18, 6 + (i * 7 + j * 5) % 9));
        A("city model (overlapping boxes)", city.ToArray());
        A("pocket plate", Diff(Bx(0, 0, 0, 120, 90, 14), Bx(15, 15, 5, 60, 75, 20), Cyl(90, 45, 18, 6, 20)));
        A("ring with a hole", Diff(Cyl(70, 70, 65, 0, 12), Cyl(70, 70, 30, -1, 20)));
        A("plate with through holes", Diff(Bx(0, 0, 0, 140, 90, 10), Cyl(30, 45, 14, -1, 20), Cyl(105, 45, 20, -1, 20), Cyl(70, 20, 2, -1, 20)));
        A("L shape", Pl(10, 0, 0, 120, 0, 120, 40, 40, 40, 40, 110, 0, 110));
        A("star", Pl(10, Enumerable.Range(0, 10).SelectMany(i => { double a = Math.PI * i / 5, r = i % 2 == 0 ? 60 : 26; return new[] { 70 + r * Math.Cos(a), 70 + r * Math.Sin(a) }; }).ToArray()));
        A("sharp triangle", Pl(10, 0, 0, 140, 0, 0, 22));
        A("two islands", Bx(0, 0, 0, 50, 80, 8), Bx(80, 10, 0, 140, 70, 12));
        var srf = NurbsSurface.CreateThroughPoints(Enumerable.Range(0, 25).Select(i => new Point3d((i % 5) * 35, (i / 5) * 28, 4 + 8 * Math.Abs(Math.Sin(i * 1.3)))), 5, 5, 3, 3, false, false);
        A("NURBS surface", srf.ToBrep());
        var turned = srf.ToBrep(); turned.Rotate(0.6, Vector3d.ZAxis, Point3d.Origin); turned.Translate(-400, 250, 33);
        A("NURBS surface turned, anywhere", turned);
        var flipped = DemoParts.Topography(); flipped.Flip(true, true, true);
        A("mesh upside down normals", flipped);
        A("overhang (mushroom)", Cyl(50, 50, 12, 0, 10), Cyl(50, 50, 40, 9, 13));
        A("sphere", new Sphere(new Point3d(0, 0, 7), 7).ToBrep());
        A("vase", DemoParts.Vase(new Point3d(0, 0, 0)));
        A("dense mesh 640k faces", Field(200, 200, 0.25, (x, y) => 7 + 3 * Math.Sin(x / 12) + 3 * Math.Cos(y / 15)));
        var letters = new List<object> { Bx(0, 0, 0, 130, 60, 4) };
        foreach (var b in new[] { Bx(10, 10, 3, 16, 50, 9), Bx(10, 10, 3, 34, 16, 9), Bx(50, 10, 3, 56, 50, 9), Bx(50, 44, 3, 76, 50, 9), Bx(50, 27, 3, 70, 33, 9), Cyl(105, 30, 18, 3, 9) }) letters.Add(b);
        A("raised letters", letters.ToArray());
        A("engraved letters", Diff(Bx(0, 0, 0, 130, 60, 12), Bx(10, 10, 8, 18, 50, 20), Bx(10, 10, 8, 36, 18, 20), Bx(50, 10, 8, 58, 50, 20), Bx(50, 42, 8, 76, 50, 20)));
        // architecture and patterns: the reliefs of CNC - Relief, and lettering with its counters (the R, the A, the O are open inside)
        for (int k = 0; k < ReliefPatterns.Names.Length - 1; k++) A("relief " + ReliefPatterns.Names[k], ReliefPatterns.Make(k, 240, 160, 14, 0, -1, 1, 6));
        var word = new List<object> { Bx(0, 0, 0, 240, 70, 5) };
        foreach (var c in Word("LARGER", 48)) word.Add(c);
        A("lettering LARGER", word.ToArray());
        var fp = new List<object> { Bx(0, 0, 0, 200, 140, 4) };
        // a floor plan: outer walls, inner walls with doors, columns
        foreach (var b in new[] { (6, 6, 194, 11), (6, 129, 80, 134), (100, 129, 194, 134), (6, 6, 11, 134), (189, 6, 194, 60), (189, 90, 194, 134), (70, 11, 74, 60), (70, 78, 74, 129), (74, 56, 110, 60), (128, 56, 189, 60), (128, 60, 132, 95), (128, 112, 132, 129), (11, 86, 40, 90), (54, 86, 70, 90) }) fp.Add(Bx(b.Item1, b.Item2, 3, b.Item3, b.Item4, 13));
        for (int i = 0; i < 4; i++) fp.Add(Cyl(90 + 25 * i, 33, 4, 3, 13));
        A("floor plan", fp.ToArray());
        var big = DemoParts.Topography(); big.Transform(Transform.Scale(Plane.WorldXY, 4, 4, 1.1));
        A("big topography 960 x 640", big);
        var small = DemoParts.Topography(); small.Transform(Transform.Scale(Plane.WorldXY, 0.2, 0.2, 0.5));
        A("small topography 48 x 32", small);
        return l;
    }
}
