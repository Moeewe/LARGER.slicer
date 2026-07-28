using System;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityOrientComponent : GH_Component
    {
                public CncUtilityOrientComponent()
                : base("CNC Utilities 01 Orient Part", "CU_01",
                    "Orients a solid for CNC processing: length in X, depth in Y, height in Z.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Part", "Geo", "Closed solid part to be processed", GH_ParamAccess.item);
            pManager.AddSurfaceParameter("Top Face", "Top", "Reference surface for the top side (defines stacking direction Z)", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddSurfaceParameter("Front Face", "Front", "Reference surface for the front side (defines depth direction Y)", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Rotation Angle", "Angle", "Additional board orientation rotation around the top axis in degrees (0/90/180/270)", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Solid", "Solid", "Oriented solid (X=length, Y=depth, Z=height)", GH_ParamAccess.item);
            pManager.AddTransformParameter("Orientation Transform", "XForm", "Applied transformation matrix", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Source Frame", "Frame", "Detected/computed source frame for inspection", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep solid = null;
            Surface topSrf = null;
            Surface frontSrf = null;
            double angle = 0;

            if (!DA.GetData(0, ref solid))
                return;
            DA.GetData(1, ref topSrf);
            DA.GetData(2, ref frontSrf);
            DA.GetData(3, ref angle);

            if (solid == null || !solid.IsValid || !solid.IsSolid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please provide a valid closed solid.");
                return;
            }

            // --- Schritt 1: Achsenrichtungen bestimmen ---
            Vector3d nTop;    // Normale der Oberseite → wird zu +Z
            Vector3d nFront;  // Normale der Front    → wird zu +Y

            // Oberseiten-Richtung bestimmen
            if (topSrf != null)
            {
                nTop = GetSurfaceNormal(topSrf);
                if (!nTop.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The 'Top Face' input could not be evaluated.");
                    return;
                }
            }
            else
            {
                // Fallback: Groesste planare Flaeche des Solids,
                // die ungefaehr in Welt-Z zeigt → Ober- oder Unterseite
                nTop = DetectTopNormal(solid);
            }
            nTop.Unitize();

            // Front-Richtung bestimmen
            if (frontSrf != null)
            {
                nFront = GetSurfaceNormal(frontSrf);
                if (!nFront.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The 'Front Face' input could not be evaluated.");
                    return;
                }
            }
            else
            {
                // Fallback: Duennste BBox-Achse senkrecht zu nTop
                nFront = DetectFrontNormal(solid, nTop);
            }
            nFront.Unitize();

            // --- Schritt 2: Orthonormales Frame bauen ---
            // nFront senkrecht zu nTop machen (Gram-Schmidt)
            nFront = nFront - (nFront * nTop) * nTop;
            if (nFront.Length < Rhino.RhinoMath.ZeroTolerance)
            {
                // nFront war parallel zu nTop → Fallback BBox-Methode
                nFront = DetectFrontNormal(solid, nTop);
                nFront = nFront - (nFront * nTop) * nTop;
            }
            nFront.Unitize();

            // Laengsrichtung = Kreuzprodukt
            Vector3d nRight = Vector3d.CrossProduct(nFront, nTop);
            nRight.Unitize();

            // Zusaetzliche Rotation um die Top-Achse (nTop)
            if (Math.Abs(angle) > Rhino.RhinoMath.ZeroTolerance)
            {
                double rad = Rhino.RhinoMath.ToRadians(angle);
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                Vector3d newRight = cos * nRight + sin * nFront;
                Vector3d newFront = -sin * nRight + cos * nFront;
                nRight = newRight;
                nRight.Unitize();
                nFront = newFront;
                nFront.Unitize();
            }

            // Zentrum des Solids als Frame-Ursprung
            var amp = AreaMassProperties.Compute(solid);
            Point3d origin = amp != null ? amp.Centroid : solid.GetBoundingBox(true).Center;

            // Quell-Frame: X=nRight (Laenge), Y=nFront (Tiefe), Normal=nTop (Hoehe)
            Plane source = new Plane(origin, nRight, nFront);

            // Ziel-Frame: WorldXY
            Plane target = new Plane(origin, Vector3d.XAxis, Vector3d.YAxis);

            Transform orient = Transform.PlaneToPlane(source, target);

            Brep result = solid.DuplicateBrep();
            result.Transform(orient);

            // An den Ursprung verschieben (BBox-Min auf 0,0,0)
            BoundingBox bb = result.GetBoundingBox(true);
            Transform move = Transform.Translation(-bb.Min.X, -bb.Min.Y, -bb.Min.Z);
            result.Transform(move);

            Transform total = move * orient;

            DA.SetData(0, result);
            DA.SetData(1, total);
            DA.SetData(2, source);
        }

        /// <summary>
        /// Berechnet die Flaechennormale am Schwerpunkt einer Surface.
        /// </summary>
        private static Vector3d GetSurfaceNormal(Surface srf)
        {
            double midU = srf.Domain(0).Mid;
            double midV = srf.Domain(1).Mid;
            Vector3d n = srf.NormalAt(midU, midV);
            return n.IsValid ? n : Vector3d.Unset;
        }

        /// <summary>
        /// Sucht die Flache mit der groessten Flaecheninhalt, deren Normale
        /// am staerksten in Welt-Z zeigt. Fuer allgemeine Plattenbauteile ist das typischerweise
        /// die Ober- oder Unterseite.
        /// </summary>
        private static Vector3d DetectTopNormal(Brep solid)
        {
            double bestScore = 0;
            Vector3d bestN = Vector3d.ZAxis;

            for (int i = 0; i < solid.Faces.Count; i++)
            {
                var face = solid.Faces[i];
                var famp = AreaMassProperties.Compute(face);
                if (famp == null) continue;

                Point3d c = famp.Centroid;
                if (!face.ClosestPoint(c, out double u, out double v)) continue;
                Vector3d n = face.NormalAt(u, v);
                if (!n.IsValid) continue;
                n.Unitize();

                // Wie stark zeigt diese Flaechennormale in Z-Richtung?
                double zAlignment = Math.Abs(n.Z);
                double score = famp.Area * zAlignment;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestN = n;
                }
            }

            // Sicherstellen, dass nTop nach "oben" (+Z) zeigt
            if (bestN.Z < 0) bestN = -bestN;
            return bestN;
        }

        /// <summary>
        /// Bestimmt die Front-Richtung als die duennste Ausdehnung des Solids
        /// senkrecht zur gegebenen Top-Normalen.
        /// </summary>
        private static Vector3d DetectFrontNormal(Brep solid, Vector3d nTop)
        {
            nTop.Unitize();
            BoundingBox bb = solid.GetBoundingBox(true);
            Vector3d diag = bb.Max - bb.Min;

            // Die drei Welt-Achsen und ihre BBox-Ausdehnung
            var axes = new[] {
                (dir: Vector3d.XAxis, size: diag.X),
                (dir: Vector3d.YAxis, size: diag.Y),
                (dir: Vector3d.ZAxis, size: diag.Z)
            };

            // Suche die duennste Achse, die moeglichst senkrecht zu nTop steht
            double bestScore = double.MaxValue;
            Vector3d bestDir = Vector3d.YAxis;

            foreach (var ax in axes)
            {
                double parallel = Math.Abs(ax.dir * nTop);
                if (parallel > 0.7) continue; // zu parallel zur Top-Richtung → ueberspringen

                // Score: duennste Achse bevorzugen (kleinste BBox-Ausdehnung)
                if (ax.size < bestScore)
                {
                    bestScore = ax.size;
                    bestDir = ax.dir;
                }
            }

            // Versuche auch die Flaechenerkennung: groesste Flaeche senkrecht zu nTop
            double bestFaceScore = 0;
            Vector3d bestFaceN = Vector3d.Unset;

            for (int i = 0; i < solid.Faces.Count; i++)
            {
                var face = solid.Faces[i];
                var famp = AreaMassProperties.Compute(face);
                if (famp == null) continue;

                Point3d c = famp.Centroid;
                if (!face.ClosestPoint(c, out double u, out double v)) continue;
                Vector3d n = face.NormalAt(u, v);
                if (!n.IsValid) continue;
                n.Unitize();

                // Muss senkrecht zu nTop stehen
                double topParallel = Math.Abs(n * nTop);
                if (topParallel > 0.5) continue;

                double score = famp.Area * (1.0 - topParallel);
                if (score > bestFaceScore)
                {
                    bestFaceScore = score;
                    bestFaceN = n;
                }
            }

            // Wenn die Flaechenerkennung ein gutes Ergebnis liefert, nutze es
            if (bestFaceN.IsValid)
                return bestFaceN;

            return bestDir;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityOrientIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E01");
    }
}
