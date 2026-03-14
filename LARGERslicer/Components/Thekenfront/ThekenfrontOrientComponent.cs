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
            if (!TryGetFaceNormal(face, out Point3d center, out Vector3d normal))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Die Frontflaeche konnte nicht ausgewertet werden.");
                return;
            }

            normal.Unitize();

            // Schritt 1: Front-Normale nach +Z drehen
            Transform alignFront = Transform.Rotation(normal, Vector3d.ZAxis, center);

            Brep result = solid.DuplicateBrep();
            result.Transform(alignFront);

            // Schritt 2: Um Z drehen, damit die ehemalige Ober-/Unterseite
            // (groesste Nicht-Front-Flaeche) in Y zeigt.
            // Die Bretter werden als Z-Schichten erzeugt und muessen
            // parallel zur originalen Ober-/Unterseite liegen.
            Transform alignRoll = FindRollCorrection(result, useIndex);
            result.Transform(alignRoll);

            // Schritt 3: An den Ursprung verschieben (Min auf 0,0,0)
            BoundingBox bb = result.GetBoundingBox(true);
            Transform move = Transform.Translation(-bb.Min.X, -bb.Min.Y, -bb.Min.Z);
            result.Transform(move);

            Transform total = move * alignRoll * alignFront;
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
        /// Nach der Front-nach-Z-Drehung: Finde die groesste Flaeche, deren Normale
        /// ueberwiegend in der XY-Ebene liegt (= ehemalige Ober-/Unterseite),
        /// und drehe das Solid um Z, damit diese Flaeche nach +Y oder -Y zeigt.
        /// So liegen die spaeter erzeugten Z-Schicht-Bretter parallel zur
        /// originalen Ober-/Unterseite.
        /// </summary>
        private static Transform FindRollCorrection(Brep rotatedSolid, int frontFaceIndex)
        {
            double bestArea = 0;
            Vector3d bestNormalXY = Vector3d.Unset;

            for (int i = 0; i < rotatedSolid.Faces.Count; i++)
            {
                if (!TryGetFaceNormal(rotatedSolid.Faces[i], out _, out Vector3d n))
                    continue;

                n.Unitize();

                // Nur Flaechen betrachten, deren Normale ueberwiegend in XY liegt
                // (also nicht die Frontflaeche/Rueckseite, die nach +Z/-Z zeigt)
                double zComponent = Math.Abs(n.Z);
                if (zComponent > 0.5)
                    continue;

                var amp = AreaMassProperties.Compute(rotatedSolid.Faces[i]);
                if (amp == null)
                    continue;

                double area = amp.Area;
                if (area > bestArea)
                {
                    bestArea = area;
                    // Projiziere Normale in XY-Ebene
                    bestNormalXY = new Vector3d(n.X, n.Y, 0);
                }
            }

            if (!bestNormalXY.IsValid || bestNormalXY.Length < Rhino.RhinoMath.ZeroTolerance)
                return Transform.Identity;

            bestNormalXY.Unitize();

            // Drehe so, dass die Ober-/Unterseiten-Normale nach +Y zeigt
            // (oder -Y, je nach Orientierung – wir nehmen die Richtung,
            // die weniger dreht, also den kuerzeren Weg)
            double angle = Math.Atan2(bestNormalXY.X, bestNormalXY.Y);

            BoundingBox rbb = rotatedSolid.GetBoundingBox(true);
            Point3d centroid = 0.5 * (rbb.Min + rbb.Max);

            return Transform.Rotation(-angle, Vector3d.ZAxis, centroid);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E01");
    }
}
