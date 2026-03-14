using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LARGERslicer.Components.Thekenfront
{
    public class ThekenfrontOrientComponent : GH_Component
    {
        public ThekenfrontOrientComponent()
          : base("TH Orient", "TH_01",
                            "Orientiert einen Thekenfront-Abschnitt so, dass die Frontflaeche nach oben auf World-XY zeigt.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Thekenfront";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Solid", "S", "Geschlossenes Brep/Solid eines Thekenabschnitts", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frontflaechen-Index", "Idx", "Manueller Index der Frontflaeche", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Ausrichtung Auto", "A", "True = Frontflaeche automatisch ueber groesste +Y-Normale erkennen", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Orientiertes Solid", "OS", "Ausgerichtetes Solid mit Front nach oben", GH_ParamAccess.item);
            pManager.AddTransformParameter("Transform", "T", "Verwendete Transformationsmatrix", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Verwendeter Index", "VI", "Tatsaechlich verwendeter Frontflaechen-Index", GH_ParamAccess.item);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input muss ein gueltiges, geschlossenes Solid sein.");
                return;
            }

            int useIndex = auto ? DetectFrontFaceIndex(solid) : idx;
            if (useIndex < 0 || useIndex >= solid.Faces.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Frontflaechen-Index ungueltig.");
                return;
            }

            BrepFace face = solid.Faces[useIndex];
            if (!TryGetFaceNormal(face, out Point3d center, out Vector3d normal))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Normale der Frontflaeche konnte nicht ermittelt werden.");
                return;
            }

            normal.Unitize();
            Transform align = Transform.Rotation(normal, Vector3d.ZAxis, center);

            Brep result = solid.DuplicateBrep();
            result.Transform(align);

            BoundingBox bb = result.GetBoundingBox(true);
            Transform move = Transform.Translation(-bb.Min.X, -bb.Min.Y, -bb.Min.Z);
            result.Transform(move);

            Transform final = move * align;
            DA.SetData(0, result);
            DA.SetData(1, final);
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

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E01");
    }
}
