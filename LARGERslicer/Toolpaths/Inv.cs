using System;
using System.Globalization;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Text with numbers that reads the same on every computer: a point as the decimal mark, never
    /// the comma of a German or French Windows. Every report line and every file line of the core
    /// goes through here: Inv.F($"... {x:0.0} ...").
    /// </summary>
    public static class Inv
    {
        public static readonly CultureInfo C = CultureInfo.InvariantCulture;

        /// <summary>An interpolated string formatted with the invariant culture.</summary>
        public static string F(FormattableString s) => s == null ? "" : s.ToString(C);

        /// <summary>A number with up to the given decimals, invariant ("2.5", never "2,5").</summary>
        public static string N(double v, int digits = 2) => Math.Round(v, digits).ToString("0." + new string('#', Math.Max(0, digits)), C);
    }
}
