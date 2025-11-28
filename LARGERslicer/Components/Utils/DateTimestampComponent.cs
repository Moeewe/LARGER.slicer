using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class DateTimestampComponent : GH_Component
    {
        private System.Windows.Forms.Timer _updateTimer;

        public DateTimestampComponent()
          : base("Date Timestamp", "Timestamp",
              "Generates a timestamp string with automatic update every second. Format: yymmddHHMM_",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // No inputs required - generates timestamp automatically
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Timestamp", "TS", "Current timestamp string (format: yymmddHHMM_)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Generate current timestamp
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyMMddHHmm") + "_";

            DA.SetData(0, timestamp);

            // Schedule automatic update every 1000ms (1 second)
            if (_updateTimer == null)
            {
                _updateTimer = new System.Windows.Forms.Timer();
                _updateTimer.Interval = 1000;
                _updateTimer.Tick += (sender, e) =>
                {
                    if (OnPingDocument() != null)
                    {
                        OnPingDocument().ScheduleSolution(100, (doc) => ExpireSolution(false));
                    }
                };
                _updateTimer.Start();
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Dispose();
                _updateTimer = null;
            }
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("DateTimestampIcon.png");
        public override Guid ComponentGuid => new Guid("F1A2B3C4-D5E6-7890-ABCD-EF1234567890");
    }
}


