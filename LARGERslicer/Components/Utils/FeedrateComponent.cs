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

            // Calculate average segment length
            double avgLength = segmentLengths.Average();

            // Debugging: Show base values
            debugging.Add($"Target feedrate (mm/min): {targetFeedrate:F2}");
            debugging.Add($"Number of segments: {segmentLengths.Count}");
            debugging.Add($"Average segment length: {avgLength:F3}");

            // Safety check for avg_length = 0
            if (Math.Abs(avgLength) < 1e-10)
            {
                feedrates = segmentLengths.Select(_ => Math.Round(targetFeedrate)).ToList();
                debugging.Add("WARNING: Average segment length is 0. Using standard feedrate.");
            }
            else
            {
                // Adjust feedrate for each segment and round to whole number
                feedrates = segmentLengths.Select(segLen => Math.Round(targetFeedrate * (segLen / avgLength))).ToList();
            }

            // Debugging: Add calculated feedrates
            debugging.Add($"Calculated feedrates (rounded): [{string.Join(", ", feedrates.Select(f => f.ToString("F0")))}]");

            DA.SetDataList(0, feedrates);
            DA.SetDataList(1, debugging);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("FeedrateIcon.png");
        public override Guid ComponentGuid => new Guid("D5E6F7A8-B9C0-1234-EF01-345678901234");
    }
}


