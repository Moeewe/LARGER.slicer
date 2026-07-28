using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.Components.CNCUtilities
{
    public class CncUtilityBlockComponent : GH_Component
    {
                public CncUtilityBlockComponent()
                : base("CNC Utilities 05 Build Raw Block", "CU_05",
                    "Builds the raw block from boards, milling depths, and board length.",
                            "LARGER", "CNC Utilities")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "CNC Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Oriented Body", "Body", "Reference body from CNC Utilities 01 Orient Part", GH_ParamAccess.item);
            pManager.AddGenericParameter("Split Boards", "Split", "Boards from CNC Utilities 03b Split Joint", GH_ParamAccess.list);
            pManager.AddNumberParameter("Milling Depths", "Depths", "Milling depth per board from CNC Utilities 04 Milling Depth", GH_ParamAccess.list);
            pManager.AddNumberParameter("Board Length", "Length", "Unified board length from CNC Utilities 04 Milling Depth", GH_ParamAccess.item);
            pManager.AddNumberParameter("Side Overhang", "Overhang", "Overhang used for placement in mm", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Boards", "Boards", "All boards as Breps", GH_ParamAccess.list);
            pManager.AddBrepParameter("Container Box", "Box", "Enclosing box of the glued block", GH_ParamAccess.item);
            pManager.AddBrepParameter("Reference Body", "Ref", "Oriented reference body", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep refSolid = null;
            var boardsRaw = new List<object>();
            var depths = new List<double>();
            double boardLength = 0;
            double overhang = 100;

            if (!DA.GetData(0, ref refSolid))
                return;
            if (!DA.GetDataList(1, boardsRaw))
                return;
            if (!DA.GetDataList(2, depths))
                return;
            if (!DA.GetData(3, ref boardLength))
                return;
            DA.GetData(4, ref overhang);

            var boards = new List<ThekenBoard>();
            foreach (var o in boardsRaw)
            {
                if (TryGetBoard(o, out ThekenBoard row))
                    boards.Add(row);
            }

            if (boardsRaw.Count > 0 && boards.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input data could not be parsed as a board list.");
                return;
            }

            if (boards.Count != depths.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Board count and milling depth count must match.");
                return;
            }

            BoundingBox sbb = refSolid.GetBoundingBox(true);
            double x0 = sbb.Min.X - overhang;
            double y0 = sbb.Min.Y;

            var breps = new List<Brep>();
            BoundingBox all = BoundingBox.Empty;

            for (int i = 0; i < boards.Count; i++)
            {
                ThekenBoard row = boards[i];
                double depth = depths[i];

                var plane = new Plane(new Point3d(x0, y0, row.ZMin), Vector3d.XAxis, Vector3d.YAxis);
                var box = new Box(plane,
                    new Interval(0, boardLength),
                    new Interval(0, depth),
                    new Interval(0, row.Thickness));

                Brep b = box.ToBrep();
                if (b != null)
                {
                    breps.Add(b);
                    all.Union(b.GetBoundingBox(true));
                }
            }

            Brep container = all.IsValid ? new Box(all).ToBrep() : null;

            DA.SetDataList(0, breps);
            DA.SetData(1, container);
            DA.SetData(2, refSolid);
        }

        private static bool TryGetBoard(object input, out ThekenBoard board)
        {
            board = null;

            if (input is ThekenBoard directBoard)
            {
                board = directBoard;
                return true;
            }

            if (input is GH_ObjectWrapper wrapper && wrapper.Value is ThekenBoard wrappedBoard)
            {
                board = wrappedBoard;
                return true;
            }

            return false;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("CncUtilityBlockIcon.png");
        public override Guid ComponentGuid => new Guid("7C8CF0D6-9A8E-4C10-9DDE-6EEA644A4E06");
    }
}
