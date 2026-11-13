using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper2.Components;
using Grasshopper2.Parameters;
using Grasshopper2.UI.Icon;
using GrasshopperIO;
using LARGERslicer.Toolpaths;
using LARGERslicer.Types;
using LARGERslicer.Utils;
using Rhino.Geometry;

namespace LARGERslicer.GH2
{
    // ============================================================== machine settings
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d2001")]
    public sealed class MachineSettingsComponent : Component
    {
        public MachineSettingsComponent() : base(Io.Name("Machine Settings", "Temperatures and cooling for the Weber DXR. They become the start and end sequence of the DXR file.", Io.Dxr, 0)) { }
        public MachineSettingsComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("MachineSettingsIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddNumber("Bed Temperature", "Bed", "Heated bed in °C. 0 = off.").Set(60.0);
            inputs.AddNumber("Nozzle Temperature", "Nozzle", "Extruder nozzle in °C. 0 = off.").Set(200.0);
            inputs.AddNumber("Cooling", "Fan", "Cooling fan in percent, 0 to 100. 0 = off.").Set(50.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Machine Settings", "MS", "Machine settings for the DXR Generator.");
            outputs.AddText("Info", "I", "Summary.");
        }

        protected override void Process(IDataAccess access)
        {
            double bed, nozzle, fan;
            access.GetItem(0, out bed); access.GetItem(1, out nozzle); access.GetItem(2, out fan);
            if (bed < 0 || nozzle < 0 || fan < 0 || fan > 100) { access.AddError("Invalid values", "Temperatures must not be negative, cooling is 0 to 100 percent."); return; }
            var m = new MachineSettings(bed, nozzle, fan);
            access.SetItem(0, m);
            access.SetItem(1, m.ToString());
        }
    }

    // ============================================================== toolpath -> robot lines
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d2002")]
    public sealed class ToolpathToRobotLinesComponent : Component
    {
        public ToolpathToRobotLinesComponent() : base(Io.Name("Toolpath to Robot Lines", "Writes the toolpath as robot moves, one PTP line per point, with the matching extrusion and speed. The tool orientation A, B, C is the same for every point: use it for flat layers with a vertical tool, and check it against your own cell before the first run.", Io.Slicer, Group.Output)) { }
        public ToolpathToRobotLinesComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("SlicerRobotLinesIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Toolpath", "T", "The finished path.");
            inputs.AddNumber("A", "A", "Tool orientation A in degrees (rotation about Z), as your robot cell expects it for a vertical tool.").Set(0.0);
            inputs.AddNumber("B", "B", "Tool orientation B in degrees (rotation about Y).").Set(0.0);
            inputs.AddNumber("C", "C", "Tool orientation C in degrees (rotation about X).").Set(0.0);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("Robot Path", "Path", "One PTP line per path point.", Access.Twig);
            outputs.AddNumber("Extrusion", "E", "Extrusion per line.", Access.Twig);
            outputs.AddNumber("Speed", "F", "Speed in mm/min per line.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            double a, b, c; access.GetItem(1, out a); access.GetItem(2, out b); access.GetItem(3, out c);
            var r = RobotLines.Build(tp, a, b, c);
            if (r.Tilted) access.AddWarning("Tilted tool axis", "This path has tilted tool axes (angled, non-planar or multi-axis). The lines carry one fixed orientation, so the tool will not follow the tilt. Use the Planes output of Extrusion and Speed with your robot programming instead.");
            if (r.Speed.Any(v => v <= 0)) access.AddError("Speed 0", "A move has speed 0; the machine would stop there.");
            access.SetTwig(0, r.Lines.ToArray(), null, null);
            access.SetTwig(1, r.Extrusion.ToArray(), null, null);
            access.SetTwig(2, r.Speed.ToArray(), null, null);
        }
    }

    // ============================================================== DXR generator
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d2003")]
    public sealed class DxrGeneratorComponent : Component
    {
        public DxrGeneratorComponent() : base(Io.Name("DXR Generator", "Translates robot moves with extrusion and speed into a DXR file for the Weber DXR extruder, including the start and end sequence of the machine.", Io.Dxr, 1)) { }
        public DxrGeneratorComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("DXRGeneratorIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Robot Path", "Path", "Robot moves, one PTP line each.", Access.Twig);
            inputs.AddNumber("Extrusion", "E", "Extrusion per move.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddNumber("Speed", "F", "Speed in mm/min per move.", Access.Twig, Requirement.MayBeMissing);
            inputs.AddGeneric("Machine Settings", "MS", "Machine Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Trim Last", "Tr", "Drop the last extrusion and speed value. Needed for lists that carry one value more than there are moves, as the Grasshopper 1 definitions do. Not needed after Toolpath to Robot Lines.").Set(false);
            inputs.AddText("File", "File", "Optional: file, folder or just a name (File Name of Robot Program). Empty = the Desktop. Full path of the .dxr file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("DXR", "DXR", "The DXR file, line by line.", Access.Twig);
            outputs.AddText("Info", "I", "What was generated.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string[] path; double[] e, f; bool trim, save; string file;
            if (!access.GetItemArray(0, out path) || path == null || path.Length == 0) { access.AddError("No robot path", "Connect robot moves, one PTP line each."); return; }
            access.GetItemArray(1, out e); access.GetItemArray(2, out f);
            var machine = Io.Get<MachineSettings>(access, 3);
            access.GetItem(4, out trim); access.GetItem(5, out file); access.GetItem(6, out save);

            var info = new List<string>();
            var p1 = (e ?? new double[0]).ToList(); var f1 = (f ?? new double[0]).ToList();
            if (trim) { if (p1.Count > 0) p1.RemoveAt(p1.Count - 1); if (f1.Count > 0) f1.RemoveAt(f1.Count - 1); }
            if (p1.Count == 0) access.AddWarning("No extrusion", "All moves get extrusion 0.");
            if (f1.Count == 0) access.AddWarning("No speed", "All moves get the default speed of 1000 mm/min.");
            if (f1.Any(v => v <= 0)) { access.AddError("Speed 0", "A move has speed 0; the machine would stop there."); return; }

            var result = new List<string>();
            try
            {
                result.AddRange(DXRHelper.ProcessRobotLinesToDXR(path.Where(l => l != null).ToList(), p1, f1, info, machine));
                if (result.Count == 0) { access.AddError("Nothing generated", string.Join("\n", info)); access.SetTwig(1, info.ToArray(), null, null); return; }
                result.AddRange(machine != null ? machine.GetEndGCode() : new[]
                {
                    "N9999994 V.P.VAR_heatbedtemp = 0", "N9999995 L heatbedTemp_sub.nc",
                    "N9999996 V.P.VAR_fan = 0", "N9999997 L fan_sub.nc",
                    "N9999998 V.P.VAR_extrudertemp = 0", "N9999999 L extruderTemp_sub.nc", "M29"
                });
                result.Add(DXRHelper.GenerateFooterComment());
                info.Add($"Total DXR file lines: {result.Count}");
            }
            catch (Exception ex)
            {
                access.AddError("DXR generation failed", ex.Message);
                return;
            }

            if (save)
            {
                // empty = the Desktop; a bare name (File Name of Robot Program) goes to the Desktop too
                try { string written = OutputFile.Write(file, OutputFile.Name("DXR", "Print", null, ".dxr"), result); info.Add("Saved: " + written); access.AddRemark("Saved", written); }
                catch (Exception ex) { access.AddError("Could not save", ex.Message); }
            }
            access.SetTwig(0, result.ToArray(), null, null);
            access.SetTwig(1, info.ToArray(), null, null);
        }
    }
}
