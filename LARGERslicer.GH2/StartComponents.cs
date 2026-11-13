using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== machine profile
    [IoId("2d520606-f428-415b-a544-b370de0d5805")]
    public sealed class MachineProfileComponent : Component
    {
        public MachineProfileComponent() : base(Io.Name("Machine Profile", "Describes how your printer wants the extrusion values. Pick a starting point and change the numbers to calibrate it: Factor scales every value, First Layer Factor only the first layer. Connect it to Machine of Print Settings. The Weber and Ginger presets use the values of the FH Muenster definition; check them on your machine.", Io.Slicer, Group.Start)) { }
        public MachineProfileComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerMachineIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddInteger("Preset", "P", "Starting point:\n" + string.Join("\n", MachineProfile.PresetNames.Select((n, i) => i + " = " + n))).Set(0).Choices(MachineProfile.PresetNames);
            inputs.AddNumber("Factor", "Fa", "Multiplies every extrusion value. Empty = value of the preset.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("First Layer Factor", "F1", "Multiplies the extrusion of the first layer only (e.g. 1.1 to squash it onto the bed). Empty = value of the preset.", Access.Item, Requirement.MayBeMissing);
            inputs.AddNumber("Filament Diameter", "Fil", "0 = E is a volume. >0 = E is the length of filament of this diameter (mm). Empty = value of the preset.", Access.Item, Requirement.MayBeMissing);
            inputs.AddInteger("Cross Section", "CS", "How the bead volume is calculated:\n0 = squashed bead with round sides\n1 = width x height (rectangle). Empty = value of the preset.", Access.Item, Requirement.MayBeMissing).Choices("Squashed bead", "Rectangle");
            inputs.AddText("Name", "N", "Your name for this machine. Empty = name of the preset.", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Machine Profile", "MP", "Machine profile for Print Settings (input Machine).");
            outputs.AddText("Info", "I", "What E means for this machine.");
        }

        protected override void Process(IDataAccess access)
        {
            int preset; access.GetItem(0, out preset);
            if (preset < 0 || preset >= MachineProfile.PresetNames.Length) { access.AddWarning("Unknown preset", $"Preset {preset} does not exist. Using 0."); preset = 0; }
            var m = MachineProfile.Preset(preset);
            if (access.GetItem(1, out double factor)) m.Factor = factor;
            if (access.GetItem(2, out double first)) m.FirstLayerFactor = first;
            if (access.GetItem(3, out double fil)) { m.FilamentDiameter = Math.Max(0, fil); m.Unit = m.FilamentDiameter > 0 ? "mm filament" : (m.Unit == "mm filament" ? "mm3" : m.Unit); }
            if (access.GetItem(4, out int cs)) m.RectangleSection = cs == 1;
            if (access.GetItem(5, out string name) && !string.IsNullOrWhiteSpace(name)) m.Name = name.Trim();
            if (m.Factor <= 0) { access.AddError("Factor", "Factor must be greater than 0. A factor of 0 gives no extrusion at all."); return; }
            if (m.FirstLayerFactor <= 0) { access.AddError("First Layer Factor", "First Layer Factor must be greater than 0."); return; }
            string section = m.RectangleSection ? "width x height" : "squashed bead (width - height) x height + pi/4 x height2";
            access.SetItem(0, m);
            access.SetItem(1, $"{m.Name}: E = {section} x length x Flow x {m.Factor:0.####}" +
                              (m.FirstLayerFactor != 1 ? $" (first layer x {m.FirstLayerFactor:0.##})" : "") +
                              (m.FilamentDiameter > 0 ? $" / filament area ({m.FilamentDiameter:0.##} mm)" : "") + $", unit: {m.Unit}");
        }
    }

    // ============================================================== print bed
    [IoId("2341c9e1-b844-4809-80da-4ef9d1ae1054")]
    public sealed class PrintBedComponent : Component
    {
        public PrintBedComponent() : base(Io.Name("Print Bed", "Gives the print bed of your machine, origin in the front left corner, and checks that every connected path lies on it: no negative coordinates, nothing beyond the edge, nothing higher than the machine. Place your part on the bed before slicing.", Io.Slicer, Group.Start)) { }
        public PrintBedComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerBedIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Size X", "X", "Width of the bed (mm).").Set(1000.0);
            inputs.AddNumber("Size Y", "Y", "Depth of the bed (mm).").Set(1000.0);
            inputs.AddNumber("Height", "Z", "Largest print height of the machine (mm).").Set(1000.0);
            inputs.AddGeneric("Toolpath", "T", "Optional. The finished paths: they are checked against the bed.", Access.Twig, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddCurve("Bed", "B", "Outline of the bed.");
            outputs.AddBoolean("OK", "OK", "True if every path lies on the bed and below the height.");
            outputs.AddText("Info", "I", "Size of the bed, and how much room the paths leave to its edges.", Access.Twig);
            outputs.AddLine("Grid", "G", "Grid lines every 100 mm, to show the bed in the viewport.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            double x, y, z; access.GetItem(0, out x); access.GetItem(1, out y); access.GetItem(2, out z);
            if (x <= 0 || y <= 0 || z <= 0) { access.AddError("Invalid size", "Size X, Size Y and Height must be greater than 0."); return; }
            bool fits = true;
            var info = new List<string> { $"Bed {x:0} x {y:0} mm, height {z:0} mm, origin in the front left corner." };
            access.ItemArray(3, out object[] raw);
            var box = BoundingBox.Empty; int paths = 0;
            foreach (var o in raw ?? new object[0])
            {
                if (!(o is Toolpath tp) || tp.Count < 2) continue;
                paths++;
                foreach (var q in tp.Points) box.Union(q);
            }
            if (paths > 0)
            {
                const double tol = 0.01;
                var problems = new List<string>();
                if (box.Min.X < -tol || box.Min.Y < -tol) problems.Add($"runs to X {box.Min.X:0}, Y {box.Min.Y:0}: negative coordinates are off the bed. Move the part onto the positive side of the origin");
                if (box.Max.X > x + tol || box.Max.Y > y + tol) problems.Add($"runs to X {box.Max.X:0}, Y {box.Max.Y:0}: beyond the bed of {x:0} x {y:0} mm");
                if (box.Min.Z < -tol) problems.Add($"goes down to Z {box.Min.Z:0.0}: below the bed");
                if (box.Max.Z > z + tol) problems.Add($"is {box.Max.Z:0} mm high: more than the {z:0} mm of the machine");
                fits = problems.Count == 0;
                if (fits)
                {
                    double room = Math.Min(Math.Min(box.Min.X, box.Min.Y), Math.Min(x - box.Max.X, y - box.Max.Y));
                    info.Add($"{paths} path(s) fit: X {box.Min.X:0}..{box.Max.X:0}, Y {box.Min.Y:0}..{box.Max.Y:0}, up to Z {box.Max.Z:0}. Smallest distance to the edge of the bed (skirt included): {room:0} mm.");
                }
                else foreach (var pr in problems) { info.Add("DOES NOT FIT: the path " + pr + "."); access.AddWarning("Does not fit", "The path " + pr + "."); }
            }
            else info.Add("Connect the finished paths to Toolpath to check that they lie on the bed.");
            var grid = new List<Line>();
            for (double gx = 100; gx < x - 1e-6; gx += 100) grid.Add(new Line(gx, 0, 0, gx, y, 0));
            for (double gy = 100; gy < y - 1e-6; gy += 100) grid.Add(new Line(0, gy, 0, x, gy, 0));
            access.SetItem(0, new Rectangle3d(Plane.WorldXY, x, y).ToNurbsCurve());
            access.SetItem(1, fits);
            access.SetTwig(2, info.ToArray(), null, null);
            access.SetTwig(3, grid.ToArray(), null, null);
        }
    }
}
