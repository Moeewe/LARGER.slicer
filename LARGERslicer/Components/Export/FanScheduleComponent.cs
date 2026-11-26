using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using LARGERslicer.Types;
using LARGERslicer.Toolpaths;

namespace LARGERslicer.Components.Export
{
    /// <summary>
    /// Cooling by layer for the Weber DXR: the fan stays off for the first layers (they must stick to the bed and
    /// to each other), then runs low, later high. Goes between Machine Settings and the DXR Generator, which writes
    /// the change at the layer change, as the Weber post processor does (V.P.VAR_fan, L fan_sub.nc).
    /// </summary>
    public class FanScheduleComponent : GH_Component
    {
        public FanScheduleComponent()
          : base("Fan Schedule", "Fan",
              "Cooling by layer: the fan stays off for the first layers, then runs at a low and later at a full value. Put it between Machine Settings and the DXR Generator; the DXR file switches the fan at the layer change (V.P.VAR_fan, L fan_sub.nc, as the Weber post processor writes it). Steps = your own list, e.g. 5:30, 10:70, 40:100 (after 5 layers 30 %, after 10 layers 70 % ...).",
              "", "") { }

        public override string Category => "LARGER";
        public override string SubCategory => "DXR";
        public override Guid ComponentGuid => new Guid("6E1F0C83-2B5D-4F7A-9C21-8D4A3E5B7F12");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => LARGERslicer.Utils.IconHelper.Load("FanScheduleIcon.png", this);

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Machine Settings", "MS", "From Machine Settings (Weber DXR), or a Machine (G-code machines: the fan lines go into Machine G-code after the layers)", GH_ParamAccess.item);
            p.AddIntegerParameter("Off For", "Off", "The fan stays off for this many layers (they must stick to the bed and to each other)", GH_ParamAccess.item, 5);
            p.AddNumberParameter("Then", "Low", "Fan in percent after those layers", GH_ParamAccess.item, 30.0);
            p.AddIntegerParameter("Full After", "After", "After this many layers the fan runs at Full", GH_ParamAccess.item, 10);
            p.AddNumberParameter("Full", "Full", "Fan in percent from then on", GH_ParamAccess.item, 70.0);
            p.AddBooleanParameter("Ramp", "Ramp", "True = the fan rises evenly from Then to Full between the two layers. False = it jumps", GH_ParamAccess.item, false);
            p.AddTextParameter("Steps", "Steps", "Optional, instead of the values above: after how many layers how many percent, e.g. 5:30, 10:70, 40:100", GH_ParamAccess.item);
            p[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Machine Settings", "MS", "Machine Settings with the fan by layer for the DXR Generator, or the Machine with its fan lines for Machine G-code", GH_ParamAccess.item);
            p.AddTextParameter("Info", "I", "The fan by layer", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            IGH_Goo goo = null; MachineSettings ms = null; MachineDefinition md = null;
            if (da.GetData(0, ref goo))
            {
                if (goo is GH_ObjectWrapper w) { ms = w.Value as MachineSettings; md = w.Value as MachineDefinition; }
                else { goo?.CastTo(out ms); if (ms == null) md = goo?.ScriptVariable() as MachineDefinition; }
            }
            if (ms == null && md == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect Machine Settings (Weber DXR) or a Machine (G-code)."); return; }
            int off = 5, after = 10; double low = 30, full = 70; bool ramp = false; string text = null;
            da.GetData(1, ref off); da.GetData(2, ref low); da.GetData(3, ref after); da.GetData(4, ref full); da.GetData(5, ref ramp); da.GetData(6, ref text);

            var steps = new List<KeyValuePair<int, double>>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var part in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int at) && double.TryParse(kv[1].Trim().TrimEnd('%').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pc))
                        steps.Add(new KeyValuePair<int, double>(Math.Max(0, at), Math.Max(0, Math.Min(100, pc))));
                    else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not understood in Steps: '" + part.Trim() + "' (write layers:percent, e.g. 5:30).");
                }
                steps = steps.OrderBy(s => s.Key).ToList();
            }
            else
            {
                off = Math.Max(0, off); after = Math.Max(off, after);
                steps.Add(new KeyValuePair<int, double>(0, 0));
                if (after > off) steps.Add(new KeyValuePair<int, double>(off, Math.Max(0, Math.Min(100, low))));
                steps.Add(new KeyValuePair<int, double>(after, Math.Max(0, Math.Min(100, full))));
            }
            if (steps.Count == 0 || steps[0].Key > 0) steps.Insert(0, new KeyValuePair<int, double>(0, 0));

            var info = new List<string>();
            if (md != null)
            {
                // a G-code machine: the fan lines go in after the layers, as M106 S0-255 (M107 = off); its own fan lines are replaced
                var m2 = md.WithFanSchedule(steps, ramp);
                if (steps.Count > 0 && steps[0].Value > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The fan of the first layer comes from the start code of the machine.");
                foreach (var st in steps) info.Add(st.Key == 0 ? $"From layer 1: {st.Value:0} %" : $"After {st.Key} layer(s): {st.Value:0} %");
                Message = string.Join(" > ", steps.Select(st => st.Value.ToString("0", CultureInfo.InvariantCulture) + "%"));
                da.SetData(0, new GH_ObjectWrapper(m2));
                da.SetDataList(1, info);
                return;
            }
            var r = ms.Copy();
            r.FanSteps = steps; r.FanRamp = ramp;
            int prevAt = -1;
            foreach (var st in steps)
            {
                info.Add(st.Key == 0 ? $"From layer 1: {st.Value:0} %" : $"After {st.Key} layer(s) (from layer {st.Key + 1}): {st.Value:0} %" + (ramp && prevAt >= 0 ? " (rising evenly up to here)" : ""));
                prevAt = st.Key;
            }
            Message = string.Join(" > ", steps.Select(s => s.Value.ToString("0", CultureInfo.InvariantCulture) + "%"));
            da.SetData(0, r);
            da.SetDataList(1, info);
        }
    }
}
