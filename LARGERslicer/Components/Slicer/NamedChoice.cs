using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace LARGERslicer.Components.Slicer
{
    /// <summary>
    /// A choice from a list that is read from a folder (machines, materials). The input holds the number
    /// in the list, and that number means something else as soon as a file is added or removed. So the
    /// component remembers the name as well, for the value set in its own row: the name wins.
    /// A wire into the input is a number as before.
    /// </summary>
    internal sealed class NamedChoice
    {
        private const string NameKey = "LARGER.ChoiceName", IndexKey = "LARGER.ChoiceIndex";
        private int _index = -1;   // the number in the input when the name was chosen
        private string _name;
        private OptionRow _row;

        /// <summary>The row of this choice (its list is kept up to date, picking in it chooses the name).</summary>
        public void Attach(OptionRow row)
        {
            _row = row;
            if (row != null) row.Picked = k => { if (row.Items != null && k >= 0 && k < row.Items.Length) { _index = k; _name = row.Items[k]; row.Shown = null; } };
        }

        /// <summary>The number in today's list for the number in the input.</summary>
        public int Resolve(GH_Component c, int input, int index, IList<string> names, string what, string folder)
        {
            if (_row != null) { _row.Items = new List<string>(names).ToArray(); _row.Shown = null; }
            var p = c.Params.Input[input] as Param_Integer;
            if (p == null || p.SourceCount > 0 || p.PersistentData.DataCount != 1) { _index = -1; _name = null; return index; }
            bool inList = index >= 0 && index < names.Count;
            if (_name == null || index != _index)
            {
                // chosen just now, or a definition from before names were kept
                _index = index; _name = inList ? names[index] : null;
                return index;
            }
            if (inList && string.Equals(names[index], _name, StringComparison.Ordinal)) return index;
            for (int k = 0; k < names.Count; k++)
                if (string.Equals(names[k], _name, StringComparison.Ordinal))
                {
                    c.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The list of {what}s has changed: {_name}, chosen here, is no. {k} now (no. {index} before) and is still used.");
                    if (_row != null) _row.Shown = _name;
                    return k;
                }
            c.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The {what} chosen here, {_name}, is not in {folder} any more." + (inList ? $" Using {names[index]}, which has its number now: pick a {what} in the row to confirm." : ""));
            return index;
        }

        public void Write(GH_IO.Serialization.GH_IWriter writer)
        {
            if (_name == null) return;
            writer.SetString(NameKey, _name); writer.SetInt32(IndexKey, _index);
        }

        public void Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool has = reader.ItemExists(NameKey) && reader.ItemExists(IndexKey);
            _name = has ? reader.GetString(NameKey) : null;
            _index = has ? reader.GetInt32(IndexKey) : -1;
        }
    }
}
