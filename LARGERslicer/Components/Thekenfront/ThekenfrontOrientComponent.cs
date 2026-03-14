using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontOrientComponent : GH_Component
    {
                public ThekenfrontOrientComponent()
                    : base("Thekenfront 01 Ausrichten", "TH_01",
                                                        "Richtet den Thekenabschnitt so aus, dass die Frontflaeche nach oben zeigt.",
                            "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Thekenabschnitt", "Geo", "Geschlossenes Solid des Thekenabschnitts", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frontflaechen-Index", "Index", "Manuelle Auswahl der Frontflaeche", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Auto-Erkennung", "Auto", "True = Frontflaeche automatisch erkennen", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Orientiertes Solid", "Solid", "Ausrichtetes Solid mit Front nach oben", GH_ParamAccess.item);
            pManager.AddTransformParameter("Ausrichtungs-Transform", "XForm", "Verwendete Transformationsmatrix", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Verwendeter Flaechenindex", "Index", "Tatsaechlich verwendeter Frontflaechen-Index", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep solid = null;
            int idx = 0;
            bool auto = true;

            if (!DA.GetData(0, ref solid))
                return;
            DA.GetData(1, ref idx);
            DA.GetData(2, ref auto);

            if (solid == null || !solid.IsValid || !solid.IsSolid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bitte ein gueltiges, geschlossenes Solid anschliessen.");
                return;
            }

            int useIndex = auto ? DetectFrontFaceIndex(solid) : idx;
            if (useIndex < 0 || useIndex >= solid.Faces.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Der angegebene Frontflaechen-Index ist ungueltig.");
                return;
            }

            BrepFace face = solid.Faces[useIndex];
            if (!TryGetFaceNormal(face, out Point3d center, out Vector3d nFront))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Die Frontflaeche konnte nicht ausgewertet werden.");
                return;
            }

            nFront.Unitize();

            // Ober-/Unterseite des Originals finden (groesste Flaeche senkrecht zur Front)
            Vector3d nTop = FindPerpendicularFaceNormal(solid, useIndex, nFront);

            // Orthogonalisieren: Anteil von nTop parallel zu nFront entfernen
            Vector3d nTopPerp = nTop - (nTop * nFront) * nFront;
            if (nTopPerp.Length < Rhino.RhinoMath.ZeroTolerance)
            {
                // Fallback: Welt-Z senkrecht zu nFront projizieren
                nTopPerp = Vector3d.ZAxis - (Vector3d.ZAxis * nFront) * nFront;
                if (nTopPerp.Length < Rhino.RhinoMath.ZeroTolerance)
                    nTopPerp = Vector3d.YAxis - (Vector3d.YAxis * nFront) * nFront;
            }
            nTopPerp.Unitize();

            // Dritte Achse = Kreuzprodukt
            Vector3d nRight = Vector3d.CrossProduct(nTopPerp, nFront);
            nRight.Unitize();

            // Quell-Frame: X=Laengsrichtung, Y=Front, Normal=Ober/Unterseite
            Plane source = new Plane(center, nRight, nFront);

            // Ziel-Frame: X=WorldX, Y=WorldY, Normal=WorldZ
            // Ergebnis: Laenge → +X, Front/Tiefe → +Y, Hoehe → +Z (Stapelrichtung)
            Plane target = new Plane(center, Vector3d.XAxis, Vector3d.YAxis);

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
            DA.SetData(2, useIndex);
        }

        private static bool TryGetFaceNormal(BrepFace face, out Point3d center, out Vector3d normal)
        {
            center = Point3d.Origin;
            normal = Vector3d.Unset;

            var amp = AreaMassProperties.Compute(face);
            if (amp == null)
                return false;

            center = amp.Centroid;
            if (!face.ClosestPoint(center, out double u, out double v))
                return false;

            normal = face.NormalAt(u, v);
            return normal.IsValid && normal.Length > Rhino.RhinoMath.ZeroTolerance;
        }

        private static int DetectFrontFaceIndex(Brep solid)
        {
            int best = 0;
            double bestY = double.MinValue;

            for (int i = 0; i < solid.Faces.Count; i++)
            {
                if (!TryGetFaceNormal(solid.Faces[i], out _, out Vector3d n))
                    continue;

                if (n.Y > bestY)
                {
                    bestY = n.Y;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Sucht im Original-Solid die groesste Flaeche, deren Normale
        /// moeglichst senkrecht zur Frontnormalen steht (= Ober-/Unterseite).
        /// </summary>
        private static Vector3d FindPerpendicularFaceNormal(Brep solid, int frontIndex, Vector3d frontNormal)
        {
            frontNormal.Unitize();
            double bestScore = 0;
            Vector3d bestNormal = Vector3d.Unset;

            for (int i = 0; i < solid.Faces.Count; i++)
            {
                if (i == frontIndex)
                    continue;

                if (!TryGetFaceNormal(solid.Faces[i], out _, out Vector3d n))
                    continue;

                n.Unitize();

                // Flaechen ueberspringen, deren Normale zu parallel zur Front steht
                double parallelism = Math.Abs(n * frontNormal);
                if (parallelism > 0.7)
                    continue;

                double perpendicularity = 1.0 - parallelism;

                var amp = AreaMassProperties.Compute(solid.Faces[i]);
                if (amp == null)
                    continue;

                double score = amp.Area * perpendicularity;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestNormal = n;
                }
            }

            // Fallback: Welt-Z wenn nichts passendes gefunden
            if (!bestNormal.IsValid)
                return Vector3d.ZAxis;

            return bestNormal;
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E01");
    }
}
