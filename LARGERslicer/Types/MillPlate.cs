using System;
using System.Globalization;
using Rhino.Geometry;

namespace LARGERslicer.Types
{
    /// <summary>The raw plate on the table of the Zünd, and where the model lies in it.</summary>
    public sealed class MillPlate
    {
        public double Length = 300, Width = 220, Thickness = 20;
        /// <summary>The front left corner of the plate on the table.</summary>
        public double X = 20, Y = 20;
        public int Material;
        /// <summary>0 = middle of the plate, 1 = front left corner (Edge away from the edges), 2 = as drawn.</summary>
        public int Placement;
        public double Edge = 15;
        /// <summary>0 = the highest point just below the top of the plate, 1 = as drawn.</summary>
        public int Height;
        public int Vacuum = 5, Underlay = 200;

        public static readonly string[] Materials = { "Foam (XPS, EPS)", "PU board (Ureol)", "MDF", "Plywood, softwood", "Hardwood" };
        // how deep one level may go, as a share of the cutter's diameter: start values, try them on a scrap
        private static readonly double[] StepShare = { 1.0, 0.7, 0.5, 0.5, 0.33 };
        public static readonly string[] Placements = { "Middle of the plate", "Front left corner", "As drawn" };
        public static readonly string[] Heights = { "Level with the top of the plate", "As drawn" };

        public const string MaterialHelp = "What the plate is made of. It sets how deep one roughing level goes when the cutter does not say: foam 1 x, PU board 0.7 x, MDF and plywood 0.5 x, hardwood 0.33 x the diameter. Start values: try them on a scrap";
        public const string PlacementHelp = "Where the model lies in the plate: 0 = in the middle, 1 = in the front left corner (Edge away from both edges), 2 = where it is drawn";
        public const string HeightHelp = "How deep the model lies in the plate: 0 = its highest point half a millimetre below the top, so the whole surface is milled, 1 = where it is drawn (Z 0 = table)";

        public double Share => StepShare[Math.Max(0, Math.Min(StepShare.Length - 1, Material))];
        public string MaterialName => Materials[Math.Max(0, Math.Min(Materials.Length - 1, Material))];

        /// <summary>What is wrong with the plate, or null.</summary>
        public string Problem()
        {
            if (!(Length > 0) || !(Width > 0) || !(Thickness > 0)) return "Length, Width and Thickness of the plate must be greater than 0.";
            if (Thickness > 50) return "The plate is thicker than the working height of the Zünd G3 (50 mm).";
            if (X < 0 || Y < 0 || X + Length > 2500 || Y + Width > 1800) return string.Format(CultureInfo.InvariantCulture, "The plate does not lie on the table of the Zünd G3 (2500 x 1800 mm): it reaches X {0:0.#} to {1:0.#}, Y {2:0.#} to {3:0.#}.", X, X + Length, Y, Y + Width);
            return null;
        }

        public Box Box => new Box(Plane.WorldXY, new Interval(X, X + Length), new Interval(Y, Y + Width), new Interval(0, Thickness));

        /// <summary>Moves the model into the plate.</summary>
        public Transform Place(BoundingBox model)
        {
            double dx = 0, dy = 0, dz = 0;
            if (Placement == 0) { dx = X + Length / 2 - model.Center.X; dy = Y + Width / 2 - model.Center.Y; }
            else if (Placement == 1) { dx = X + Edge - model.Min.X; dy = Y + Edge - model.Min.Y; }
            if (Height == 0) dz = Thickness - 0.5 - model.Max.Z;
            return Transform.Translation(dx, dy, dz);
        }

        public MillPlate Copy() => (MillPlate)MemberwiseClone();

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Plate {0:0.#} x {1:0.#} x {2:0.#} mm, {3}", Length, Width, Thickness, MaterialName);
    }
}
