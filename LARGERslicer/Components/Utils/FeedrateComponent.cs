using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class FeedrateComponent : GH_Component
    {
        public FeedrateComponent()
          : base("Feedrate Calculator", "Feedrate",
              "Adjusts feedrate for constant speed. Converts target speed (mm/s) to feedrate (mm/min) and adjusts based on segment lengths. Results are rounded to whole numbers.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Segment Lengths", "Lengths", "List of segment lengths in mm", GH_ParamAccess.list);
            pManager.AddNumberParameter("Target Speed", "Speed", "Target speed in mm/s (e.g., 50 mm/s)", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Feedrates", "Feedrates", "Calculated feedrates in mm/min (rounded)", GH_ParamAccess.list);
            pManager.AddTextParameter("Debugging", "Debug", "Debugging information with intermediate values", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var segmentLengths = new List<double>();
            double targetSpeed = 50.0;

            if (!DA.GetDataList(0, segmentLengths)) return;
            DA.GetData(1, ref targetSpeed);

            var debugging = new List<string>();
            var feedrates = new List<double>();

            // Check if segment_lengths is empty
            if (segmentLengths == null || segmentLengths.Count == 0)
            {
                debugging.Add("No segment lengths available.");
                DA.SetDataList(0, feedrates);
                DA.SetDataList(1, debugging);
                return;
            }

            // Convert speed (mm/s) to feedrate (mm/min)
            double targetFeedrate = targetSpeed * 60.0;

            // G1 F is the speed along the path in mm/min. It does not depend on how long a segment
            // is, so a constant print speed means the same F for every move. (Scaling F with the
            // segment length, as this component did before, made the speed fluctuate wherever the
            // points were unevenly spaced, and gave F0 for zero-length segments.)
            double constant = Math.Max(1.0, Math.Round(targetFeedrate));
            feedrates = segmentLengths.Select(_ => constant).ToList();

            debugging.Add($"Target feedrate (mm/min): {targetFeedrate:F2}");
            debugging.Add($"Number of segments: {segmentLengths.Count}");
            debugging.Add("Constant speed: every move gets the same feedrate.");

            debugging.Add($"Feedrate for all moves: {constant:F0} mm/min");

            DA.SetDataList(0, feedrates);
            DA.SetDataList(1, debugging);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("FeedrateIcon.png");
        public override Guid ComponentGuid => new Guid("D5E6F7A8-B9C0-1234-EF01-345678901234");
    }
}


