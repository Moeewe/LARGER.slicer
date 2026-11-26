using System;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Data;

namespace LARGERslicer.GH2
{
    internal static partial class Io
    {
        /// <summary>
        /// The items of a list input, or false with an empty array. Grasshopper 2 (9.1) throws
        /// IndexOutOfRangeException from GetItemArray when an optional list input has no data at all
        /// (an empty tree has no twig 0), which faulted Machine, Robot Cell, Surface Pattern and
        /// Material Factor whenever such an input was left open.
        /// </summary>
        public static bool ItemArray<T>(this IDataAccess access, int index, out T[] items)
        {
            try
            {
                if (access.GetItemArray(index, out items) && items != null) return true;
            }
            catch (IndexOutOfRangeException) { }
            catch (ArgumentOutOfRangeException) { }
            items = new T[0];
            return false;
        }

        /// <summary>
        /// Sets the value of an input (persistent data). Typed inputs (Parameter&lt;int&gt;, Parameter&lt;double&gt;,
        /// ...) do not derive from the untyped Parameter, so "as Parameter" missed them and the value was
        /// silently not set; the value is converted to the input's type here. False if it cannot be set.
        /// </summary>
        public static bool SetValue(Grasshopper2.Parameters.IParameter p, object value)
        {
            if (p == null) return false;
            if (p is Grasshopper2.Parameters.Parameter untyped) { untyped.Set(new[] { value }); return true; }
            for (Type t = p.GetType(); t != null; t = t.BaseType)
            {
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(Grasshopper2.Parameters.Parameter<>)) continue;
                Type item = t.GetGenericArguments()[0];
                object v = value;
                if (v != null && !item.IsInstanceOfType(v))
                {
                    try { v = Convert.ChangeType(v, item, System.Globalization.CultureInfo.InvariantCulture); }
                    catch { return false; }
                }
                Array arr = Array.CreateInstance(item, 1);
                arr.SetValue(v, 0);
                var set = t.GetMethod("Set", new[] { item.MakeArrayType() });
                if (set == null) return false;
                set.Invoke(p, new object[] { arr });
                return true;
            }
            return false;
        }

        /// <summary>The machines for the presets of Machine: built in, then your own files (as the Machine input counts them).</summary>
        public static string[] MachineTitles()
        {
            try { return LARGERslicer.Toolpaths.MachineDefinition.All().Select(m => m.Title).ToArray(); }
            catch { return LARGERslicer.Toolpaths.MachineDefinition.BuiltIn().Select(m => m.Title).ToArray(); }
        }

        /// <summary>The materials for the presets of Material: built in, then your own files.</summary>
        public static string[] MaterialNames()
        {
            try { return LARGERslicer.Toolpaths.MaterialDefinition.All().Select(m => m.Name).ToArray(); }
            catch { return LARGERslicer.Toolpaths.MaterialDefinition.BuiltIn().Select(m => m.Name).ToArray(); }
        }

        /// <summary>A tree input; false and null when it has no data (see ItemArray).</summary>
        public static bool TreeOf<T>(this IDataAccess access, int index, out Tree<T> tree)
        {
            try
            {
                if (access.GetTree(index, out tree) && tree != null) return true;
            }
            catch (IndexOutOfRangeException) { }
            catch (ArgumentOutOfRangeException) { }
            tree = null;
            return false;
        }
    }

    /// <summary>Speeds are typed in mm/s and kept in mm/min (the F of the machine). A value above 1000 comes from an older
    /// definition in mm/min: it is read that way, and the component says so instead of guessing silently.</summary>
    internal static class Speeds
    {
        public static double ToMmPerMin(Grasshopper2.Components.IDataAccess access, double v, string what)
        {
            if (v > 1000)
            {
                access.AddWarning("Speed unit", LARGERslicer.Toolpaths.Inv.F($"{what} {v:0.#} is read as mm/min ({v / 60:0.#} mm/s), as in older definitions. Speeds are typed in mm/s: type {v / 60:0.#}."));
                return v;
            }
            return v * 60;
        }
    }

    /// <summary>
    /// A choice from a list that can grow (machines and materials: built-in ones plus files in Documents/LARGER): the
    /// component keeps the name of what was chosen, so a new file that sorts in before it does not switch a saved
    /// definition to another machine or material.
    /// </summary>
    internal static class ChoiceByName
    {
        public static int Resolve(Grasshopper2.Components.Component c, IDataAccess access, int index, System.Collections.Generic.IList<string> names, string key, string what)
        {
            string savedName = c.CustomValues.Get(key + ".Name", "");
            int savedIndex = c.CustomValues.Get(key + ".Index", -1);
            bool inList = index >= 0 && index < names.Count;
            if (savedName.Length > 0 && index == savedIndex)
            {
                // the number is still the one that was chosen: follow the name if the list has moved
                int now = names.IndexOf(savedName);
                if (now >= 0 && now != index)
                {
                    access.AddRemark(what, LARGERslicer.Toolpaths.Inv.F($"The list of {what.ToLowerInvariant()}s has changed: still using '{savedName}' (now number {now})."));
                    return now;
                }
                if (now < 0) access.AddWarning(what, $"'{savedName}' is no longer in the list; using number {index}.");
                return index;
            }
            // chosen just now (or a definition from before names were kept): remember it
            if (inList) { c.CustomValues.Set(key + ".Name", names[index]); c.CustomValues.Set(key + ".Index", index); }
            return index;
        }
    }
}
