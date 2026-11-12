using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Nozzle presets: pick the nozzle that is screwed in, get a bead width and layer height that
    /// suit it. Sources:
    ///  - Weber DXR profiles (Cura, "Slicer Installation Data" of the Weber start package): nozzle
    ///    1 / 2 / 3 / 4 / 5 / 6 / 8 mm -> line width 1.25 / 2.5 / 3.6 / 4.8 / 6 / 7.2 / 10 mm, layer
    ///    0.5 / 1 / 1.25 / 1.8 / 2 / 2.4 / 3.2 mm (about 1.2 x and 0.4 x the nozzle).
    ///  - Weber quick guide: bead 0 to 50 % (about 20 %) wider than the nozzle, layer 25 to 50 %
    ///    (about a third) of the nozzle; pointed nozzle 0.7 to 1.3 x, flat nozzle 1.0 to 1.8 x.
    ///  - FH Muenster on the Weber DXR: 3 mm flat nozzle, 5 mm bead, 2 mm layer (tested).
    ///    "Wide" presets scale this to other flat nozzles (bead 5/3 x, layer 2/3 x the nozzle).
    /// </summary>
    public static class Nozzles
    {
        public enum Shape { Unknown, Pointed, Flat }

        public class Preset
        {
            public string Name;
            public double Nozzle, Bead, Layer;
            public Shape Shape;
            public string Source;
            public override string ToString() => Name;
        }

        private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static Preset Weber(double n, double bead, double layer) => new Preset
        {
            Name = $"Weber {F(n)} mm - bead {F(bead)}, layer {F(layer)}", Nozzle = n, Bead = bead, Layer = layer,
            Shape = Shape.Unknown, Source = "Weber DXR profiles (start package)",
        };

        private static Preset Wide(double n) => new Preset
        {
            Name = $"Wide {F(n)} mm flat - bead {F(Math.Round(n * 5 / 3, 1))}, layer {F(Math.Round(n * 2 / 3, 1))}", Nozzle = n,
            Bead = Math.Round(n * 5 / 3, 1), Layer = Math.Round(n * 2 / 3, 1), Shape = Shape.Flat,
            Source = "scaled from the FH Muenster setting (3 mm flat nozzle, 5 mm bead, 2 mm layer); print a test wall first",
        };

        /// <summary>Index 0 = own values: the Bead Width and Layer Height inputs count.</summary>
        public static readonly Preset[] All =
        {
            new Preset { Name = "Own values (Bead Width, Layer Height)", Source = "" },
            new Preset { Name = "FH Muenster 3 mm flat - bead 5, layer 2", Nozzle = 3, Bead = 5, Layer = 2, Shape = Shape.Flat, Source = "FH Muenster, Weber DXR with the 3 mm flat nozzle (tested)" },
            Weber(1, 1.25, 0.5), Weber(2, 2.5, 1.0), Weber(3, 3.6, 1.25), Weber(4, 4.8, 1.8), Weber(5, 6.0, 2.0), Weber(6, 7.2, 2.4), Weber(8, 10.0, 3.2),
            Wide(2), Wide(4), Wide(5), Wide(6), Wide(8),
        };

        public static string[] Names => All.Select(p => p.Name).ToArray();

        public static Preset Get(int index) => All[Math.Max(0, Math.Min(All.Length - 1, index))];

        /// <summary>Puts the preset's bead width and layer height into the settings (index 0 leaves them).</summary>
        public static string Apply(PrintSettings st, int index)
        {
            if (st == null || index <= 0) return null;
            var p = Get(index);
            st.BeadWidth = p.Bead; st.LayerHeight = p.Layer; st.Nozzle = p.Nozzle; st.NozzleShape = p.Shape;
            return $"Nozzle {F(p.Nozzle)} mm: bead {F(p.Bead)} mm, layer {F(p.Layer)} mm ({p.Source}). Bead Width and Layer Height inputs are not used.";
        }

        /// <summary>Bead width range a nozzle can lay (Weber quick guide).</summary>
        public static void BeadRange(double nozzle, Shape shape, out double min, out double max)
        {
            switch (shape)
            {
                case Shape.Pointed: min = 0.7 * nozzle; max = 1.3 * nozzle; break;
                case Shape.Flat: min = 1.0 * nozzle; max = 1.8 * nozzle; break;
                default: min = 0.7 * nozzle; max = 1.8 * nozzle; break;
            }
        }

        /// <summary>What does not fit between nozzle, bead and layer; empty when all is well.</summary>
        public static List<string> Check(double nozzle, Shape shape, double bead, double layer)
        {
            var notes = new List<string>();
            if (nozzle <= 0) return notes;
            BeadRange(nozzle, shape, out double min, out double max);
            string kind = shape == Shape.Pointed ? "a pointed" : shape == Shape.Flat ? "a flat" : "a";
            if (bead > max + 1e-9) notes.Add($"Bead {F(bead)} mm is wider than {kind} {F(nozzle)} mm nozzle lays ({F(min)} to {F(max)} mm): choose a bigger nozzle or a narrower bead.");
            else if (bead < min - 1e-9) notes.Add($"Bead {F(bead)} mm is narrower than {kind} {F(nozzle)} mm nozzle lays ({F(min)} to {F(max)} mm): choose a smaller nozzle or a wider bead.");
            if (layer > 0.8 * nozzle + 1e-9) notes.Add($"Layer {F(layer)} mm is more than 80 % of the {F(nozzle)} mm nozzle: the bead is hardly pressed and bonds poorly.");
            else if (layer < 0.25 * nozzle - 1e-9) notes.Add($"Layer {F(layer)} mm is less than a quarter of the {F(nozzle)} mm nozzle: the extruder has to push hard.");
            return notes;
        }
    }
}
