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
}
