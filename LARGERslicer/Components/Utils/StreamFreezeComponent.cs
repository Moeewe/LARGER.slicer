using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class StreamFreezeComponent : GH_Component
    {
        private bool _holdData = false;
        private GH_Structure<IGH_Goo> _held; // everything that passed last (the whole list or tree)

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
            DA.SetData(0, data); // replaced by the held data after the last item (AfterSolveInstance)
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            // also when no data arrives (then SolveInstance does not run)
            var hold = Params.Input[1].VolatileData;
            _holdData = !hold.IsEmpty && GH_Convert.ToBoolean(hold.AllData(true).FirstOrDefault(), out bool on, GH_Conversion.Both) && on;
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            var output = Params.Output[0];
            if (_holdData && _held != null)
            {
                output.VolatileData.Clear();
                output.AddVolatileDataTree(_held);
            }
            else if (output.VolatileData is GH_Structure<IGH_Goo> now && !now.IsEmpty) _held = now.ShallowDuplicate();
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

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("StreamFreezeIcon.png", this);
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

