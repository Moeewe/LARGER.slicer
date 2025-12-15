using System;
using Grasshopper.Kernel.Types;

namespace LARGERslicer.Types
{
    /// <summary>
    /// Grasshopper wrapper for Cutter type to enable it as a parameter.
    /// </summary>
    public class GH_Cutter : GH_Goo<Cutter>
    {
        public GH_Cutter() { }

        public GH_Cutter(Cutter cutter)
        {
            Value = cutter;
        }

        public override bool IsValid => Value != null;

        public override string TypeName => "Cutter";

        public override string TypeDescription => "CNC Cutter/Tool specification";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_Cutter();
            return new GH_Cutter(new Cutter(
                Value.Name,
                Value.ToolPosition,
                Value.Diameter,
                Value.MaxCuttingDepth,
                Value.MaxSurfaceDepth,
                Value.Description
            ));
        }

        public override string ToString()
        {
            return Value?.ToString() ?? "Null Cutter";
        }

        public override bool CastFrom(object source)
        {
            if (source == null)
            {
                Value = null;
                return false;
            }

            if (source is Cutter cutter)
            {
                Value = cutter;
                return true;
            }

            if (source is GH_Cutter ghCutter)
            {
                Value = ghCutter.Value;
                return true;
            }

            return false;
        }

    }
}

