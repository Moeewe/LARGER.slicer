using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class StreamFreezeComponent : GH_Component
    {
        private bool _holdData = false;
        private object _lastData = null;

        public StreamFreezeComponent()
          : base("Stream Freeze", "Freeze",
              "Determines whether streaming data is allowed to pass through or not. Data can be controlled downstream through a component's solution, preventing unwanted ticks. To keep the last received data choose the [Hold-Data] within the menu options.",
              "", "")
        {
        }

        public override string Category => "LARGER";
        public override string SubCategory => "Utilities";

        public override void CreateAttributes()
        {
            m_attributes = new StreamFreezeAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Data to control", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Hold Data", "Hold", "True to hold/freeze data, False to pass through", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Output data (frozen if Hold-Data is enabled)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object data = null;
            bool holdData = false;

            if (!DA.GetData(0, ref data)) return;
            DA.GetData(1, ref holdData);

            _holdData = holdData;

            if (_holdData && _lastData != null)
            {
                // Return last held data
                DA.SetData(0, _lastData);
            }
            else
            {
                // Pass through current data
                _lastData = data;
                DA.SetData(0, data);
            }
        }

        public bool HoldData
        {
            get { return _holdData; }
            set
            {
                _holdData = value;
                ExpireSolution(true);
            }
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("StreamFreezeIcon.png");
        public override Guid ComponentGuid => new Guid("B3C4D5E6-F7A8-9012-CDEF-123456789012");
    }

    public class StreamFreezeAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        public StreamFreezeAttributes(StreamFreezeComponent owner) : base(owner) { }

        protected override void Render(Grasshopper.GUI.Canvas.GH_Canvas canvas, System.Drawing.Graphics graphics, Grasshopper.GUI.Canvas.GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == Grasshopper.GUI.Canvas.GH_CanvasChannel.Objects)
            {
                var component = Owner as StreamFreezeComponent;
                if (component != null && component.HoldData)
                {
                    // Draw visual indicator that data is frozen
                    var bounds = Bounds;
                    var rect = new System.Drawing.RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(100, System.Drawing.Color.Blue)))
                    {
                        graphics.FillRectangle(brush, rect);
                    }
                }
            }
        }
    }
}

