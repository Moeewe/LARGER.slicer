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

    // ============================================================== fan schedule
    [IoId("b1f0a6a2-3c1d-4f0e-9a51-0c6e5a7d2010")]
    public sealed class FanScheduleComponent : Component
    {
        public FanScheduleComponent() : base(Io.Name("Fan Schedule", "Cooling by layer: the fan stays off for the first layers, then runs at a low and later at a full value. Put it between Machine Settings and the DXR Generator; the DXR file switches the fan at the layer change (V.P.VAR_fan, L fan_sub.nc, as the Weber post processor writes it). Steps = your own list, e.g. 5:30, 10:70, 40:100 (after 5 layers 30 %, after 10 layers 70 % ...).", Io.Dxr, 0)) { }
        public FanScheduleComponent(IReader reader) : base(reader) { }
        protected override IIcon IconInternal => Io.Icon("FanScheduleIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddGeneric("Machine Settings", "MS", "From Machine Settings.");
            inputs.AddInteger("Off For", "Off", "The fan stays off for this many layers (they must stick to the bed and to each other).").Set(5).Counts(0, 1, 2, 3, 5, 8, 10);
            inputs.AddNumber("Then", "Low", "Fan in percent after those layers.").Set(30.0).Values(0, 20, 30, 50);
            inputs.AddInteger("Full After", "After", "After this many layers the fan runs at Full.").Set(10).Counts(5, 10, 15, 20, 30);
            inputs.AddNumber("Full", "Full", "Fan in percent from then on.").Set(70.0).Values(50, 70, 85, 100);
            inputs.AddBoolean("Ramp", "Ramp", "True = the fan rises evenly from Then to Full between the two layers. False = it jumps.").Set(false);
            inputs.AddText("Steps", "Steps", "Optional, instead of the values above: after how many layers how many percent, e.g. 5:30, 10:70, 40:100.", Access.Item, Requirement.MayBeMissing);
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddGeneric("Machine Settings", "MS", "Machine Settings with the fan by layer, for the DXR Generator.");
            outputs.AddText("Info", "I", "The fan by layer.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var ms = Io.Get<MachineSettings>(access, 0);
            var md = ms == null ? Io.Get<MachineDefinition>(access, 0) : null;
            if (ms == null && md == null) { access.AddWarning("Machine Settings", "Connect Machine Settings (Weber DXR) or a Machine (G-code)."); return; }
            int off, after; double low, full; bool ramp; string text;
            access.GetItem(1, out off); access.GetItem(2, out low); access.GetItem(3, out after); access.GetItem(4, out full); access.GetItem(5, out ramp); access.GetItem(6, out text);
            var steps = new List<KeyValuePair<int, double>>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var part in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int at) && double.TryParse(kv[1].Trim().TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pc))
                        steps.Add(new KeyValuePair<int, double>(Math.Max(0, at), Math.Max(0, Math.Min(100, pc))));
                    else access.AddWarning("Steps", "Not understood: '" + part.Trim() + "' (write layers:percent, e.g. 5:30).");
                }
                steps = steps.OrderBy(st => st.Key).ToList();
            }
            else
            {
                off = Math.Max(0, off); after = Math.Max(off, after);
                steps.Add(new KeyValuePair<int, double>(0, 0));
                if (after > off) steps.Add(new KeyValuePair<int, double>(off, Math.Max(0, Math.Min(100, low))));
                steps.Add(new KeyValuePair<int, double>(after, Math.Max(0, Math.Min(100, full))));
            }
            if (steps.Count == 0 || steps[0].Key > 0) steps.Insert(0, new KeyValuePair<int, double>(0, 0));
            if (md != null)
            {
                // a G-code machine: M106/M107 after the layers, in place of its own fan lines
                access.SetItem(0, md.WithFanSchedule(steps, ramp));
                access.SetTwig(1, steps.Select(st => st.Key == 0 ? $"From layer 1: {st.Value:0} %" : $"After {st.Key} layer(s): {st.Value:0} %").ToArray(), null, null);
                return;
            }
            var r = ms.Copy();
            r.FanSteps = steps; r.FanRamp = ramp;
            var info = new List<string>();
            int prev = -1;
            foreach (var st in steps)
            {
                info.Add(st.Key == 0 ? $"From layer 1: {st.Value:0} %" : $"After {st.Key} layer(s) (from layer {st.Key + 1}): {st.Value:0} %" + (ramp && prev >= 0 ? " (rising evenly up to here)" : ""));
                prev = st.Key;
            }
            access.SetItem(0, r);
            access.SetTwig(1, info.ToArray(), null, null);
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
            outputs.AddNumber("Feed", "F", "Feed F for the machine, in mm/min (mm/s x 60), per line.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            var tp = Io.Path(access, 0); if (tp == null) return;
            double a, b, c; access.GetItem(1, out a); access.GetItem(2, out b); access.GetItem(3, out c);
            var r = RobotLines.Build(tp, a, b, c);
            if (r.ProfileWarning != null) access.AddWarning("Extrusion profile", r.ProfileWarning);
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
        public DxrGeneratorComponent() : base(Io.Name("DXR Generator", "Translates robot moves with extrusion and speed into a DXR file for the Weber DXR extruder, including the start and end sequence of the machine.", Io.Dxr, 0)) { }
        public DxrGeneratorComponent(IReader reader) : base(reader)
        {
            // files saved before the lists were read as trees keep their inputs and wires
            for (int i = 0; i < 3; i++) SetAccess(Parameters.Input(i), Access.Tree);
        }
        protected override IIcon IconInternal => Io.Icon("DXRGeneratorIcon");

        protected override void AddInputs(InputAdder inputs)
        {
            inputs.AddText("Robot Path", "Path", "Robot moves, one PTP line each. All branches together are one program.", Access.Tree);
            inputs.AddNumber("Extrusion", "E", "Extrusion per move.", Access.Tree, Requirement.MayBeMissing);
            inputs.AddNumber("Feed (mm/min)", "F", "Feed F in mm/min (mm/s x 60) per move, as Robot Program and Extrusion and Speed give it.", Access.Tree, Requirement.MayBeMissing);
            inputs.AddGeneric("Machine Settings", "MS", "Machine Settings.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Trim Last", "Tr", "Drop the last extrusion and speed value. Needed for lists that carry one value more than there are moves, as the Grasshopper 1 definitions do. Not needed after Toolpath to Robot Lines.").Set(false);
            inputs.AddText("File", "File", "Optional: file, folder or just a name (File Name of Robot Program). Empty = the Desktop. Full path of the .dxr file to write.", Access.Item, Requirement.MayBeMissing);
            inputs.AddBoolean("Save", "Save", "True writes the file.").Set(false);
        }

        /// <summary>Every item of an input in order, whatever its tree.</summary>
        private List<object> All(IDataAccess access, int index)
        {
            if (Parameters.Input(index).Access == Access.Tree)
                return access.GetITree(index, out var tree) && tree != null ? tree.NonNullItems.ToList() : new List<object>();
            return access.ItemArray(index, out object[] items) ? items.Where(o => o != null).ToList() : new List<object>();
        }

        private static void SetAccess(IParameter p, Access access)
        {
            try { if (p != null && p.Access != access) typeof(AbstractParameter).GetProperty("Access").SetValue(p, access); }
            catch { } // the input is then read branch by branch, as before
        }

        protected override void AddOutputs(OutputAdder outputs)
        {
            outputs.AddText("DXR", "DXR", "The DXR file, line by line.", Access.Twig);
            outputs.AddText("Info", "I", "What was generated.", Access.Twig);
        }

        protected override void Process(IDataAccess access)
        {
            string[] path; double[] e, f; bool trim, save; string file;
            // all branches are one program, as in Grasshopper 1 (a branch per layer must not become a file per layer)
            path = All(access, 0).Select(o => o as string ?? o.ToString()).Where(l => l.Length > 0).ToArray();
            if (path.Length == 0) { access.AddError("No robot path", "Connect robot moves, one PTP line each."); return; }
            e = All(access, 1).Select(o => Convert.ToDouble(o, CultureInfo.InvariantCulture)).ToArray();
            f = All(access, 2).Select(o => Convert.ToDouble(o, CultureInfo.InvariantCulture)).ToArray();
            var machine = Io.Get<MachineSettings>(access, 3);
            access.GetItem(4, out trim); access.GetItem(5, out file); access.GetItem(6, out save);

            var info = new List<string>();
            var p1 = (e ?? new double[0]).ToList(); var f1 = (f ?? new double[0]).ToList();
            if (trim) { if (p1.Count > 0) p1.RemoveAt(p1.Count - 1); if (f1.Count > 0) f1.RemoveAt(f1.Count - 1); }
            if (f1.Count == 1 && path.Length > 1) { f1 = Enumerable.Repeat(f1[0], path.Length).ToList(); info.Add("One speed value: used for every move."); }
            int linear = path.Count(l => l != null && (l.TrimStart().StartsWith("LIN", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith("CIRC", StringComparison.OrdinalIgnoreCase)));
            if (linear > 0) access.AddWarning("LIN or CIRC moves", $"{linear} robot line(s) are LIN or CIRC moves. Only PTP lines are converted: these moves are missing in the DXR file.");
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
                Files.Reset(this, 6);
            }
            access.SetTwig(0, result.ToArray(), null, null);
            access.SetTwig(1, info.ToArray(), null, null);
        }
    }
}
