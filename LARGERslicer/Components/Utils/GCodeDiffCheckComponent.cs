using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using LARGERslicer.Utils;

namespace LARGERslicer.Components.Utils
{
    public class GCodeDiffCheckComponent : GH_Component
    {
        public GCodeDiffCheckComponent()
          : base("G-Code Diff Check", "GDiff",
              "Line-by-line comparison of original and remap output G-code to prove whether coordinates changed.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("GCode A", "A", "Original G-code text.", GH_ParamAccess.list);
            pManager.AddTextParameter("GCode B", "B", "Remap output G-code text.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Report", "Report", "Comparison summary report.", GH_ParamAccess.item);
            pManager.AddTextParameter("Diff Lines", "Diff", "First mismatching line pairs (max 10).", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var rawA = new List<string>();
            var rawB = new List<string>();

            DA.GetDataList(0, rawA);
            DA.GetDataList(1, rawB);

            string textA = ToText(rawA);
            string textB = ToText(rawB);

            string[] aLines = SplitLines(textA);
            string[] bLines = SplitLines(textB);

            int nA = aLines.Length;
            int nB = bLines.Length;
            int n = Math.Min(nA, nB);

            int nIdent = 0;
            int nXeOnly = 0;
            int nCoordDiff = 0;
            int nOther = 0;
            var diffLines = new List<string>();

            for (int i = 0; i < n; i++)
            {
                string la = aLines[i] ?? string.Empty;
                string lb = bLines[i] ?? string.Empty;

                if (la == lb)
                {
                    nIdent++;
                    continue;
                }

                List<string> ca = CoordsOf(la);
                List<string> cb = CoordsOf(lb);

                bool coordsEqual = ca.Count == cb.Count;
                if (coordsEqual)
                {
                    for (int c = 0; c < ca.Count; c++)
                    {
                        if (!string.Equals(ca[c], cb[c], StringComparison.Ordinal))
                        {
                            coordsEqual = false;
                            break;
                        }
                    }
                }

                if (coordsEqual)
                {
                    if (la.Contains("XE=[") && lb.Contains("XE=["))
                    {
                        nXeOnly++;
                    }
                    else
                    {
                        nOther++;
                        if (diffLines.Count < 10)
                        {
                            diffLines.Add($"Line {i + 1}:\nA: {la}\nB: {lb}");
                        }
                    }
                }
                else
                {
                    nCoordDiff++;
                    if (diffLines.Count < 10)
                    {
                        diffLines.Add($"Line {i + 1} (COORDINATES DIFFER):\nA: {la}\nB: {lb}");
                    }
                }
            }

            string report =
                $"Lines A: {nA} | Lines B: {nB} {(nA == nB ? "(MATCH)" : "(DIFFER)")}\n" +
                $"identical: {nIdent} | XE-only changed: {nXeOnly} | coordinates changed: {nCoordDiff} | other differences: {nOther}\n" +
                "-> If 'coordinates changed' is 0, the output path is byte-identical to the original. " +
                "Any visible deviation then comes from visualization, not from code changes.";

            DA.SetData(0, report);
            DA.SetDataList(1, diffLines);
        }

        private static string ToText(List<string> raw)
        {
            if (raw == null || raw.Count == 0)
                return string.Empty;

            if (raw.Count == 1)
            {
                string single = raw[0] ?? string.Empty;
                if (single.Contains("\n") || single.Contains("\r"))
                    return single;
            }

            return string.Join("\n", raw.Where(x => x != null));
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static List<string> CoordsOf(string line)
        {
            var outTokens = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return outTokens;

            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok.Length <= 1)
                    continue;

                char axis = tok[0];
                char firstValueChar = tok[1];

                if ((axis == 'X' || axis == 'Y' || axis == 'Z') &&
                    (char.IsDigit(firstValueChar) || firstValueChar == '-'))
                {
                    outTokens.Add(tok);
                }
            }

            return outTokens;
        }

        protected override System.Drawing.Bitmap Icon => IconHelper.Load("GCodeDiffCheckIcon.png");
        public override Guid ComponentGuid => new Guid("8266D9BD-1D39-410D-8846-04CF006104CD");
    }
}
