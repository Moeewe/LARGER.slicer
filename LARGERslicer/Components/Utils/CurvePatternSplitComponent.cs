using System;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;
using LARGERslicer.Utils;
using Rhino.Geometry;
using Drawing = System.Drawing;

namespace LARGERslicer.Components.Utils
{
    public class CurvePatternSplitComponent : GH_Component
    {
        public CurvePatternSplitComponent()
          : base("Curve Pattern Split", "CurvePattern",
              "Splits a curve into fixed and variable sections based on a repeating module pattern.",
              "LARGER", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Input curve to split.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Fixed Min", "Fmin", "Fixed short segment length (> 0).", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Fixed Max", "Fmax", "Fixed long segment length (> 0); internally halved at module edges.", GH_ParamAccess.item, 20.0);
            pManager.AddIntegerParameter("Repeat Count", "R", "Number of repeated modules.", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Points at pattern boundaries along the curve.", GH_ParamAccess.list);
            pManager.AddCurveParameter("All Curves", "All", "All split segments in order.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Variable Curves", "Var", "Variable-length segments.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Fixed Curves", "Fix", "Fixed-length segments.", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Pattern summary or validation message.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve input = null;
            double fixedMin = 0.0;
            double fixedMax = 0.0;
            int repeatCount = 1;

            var points = new List<Point3d>();
            var allCurves = new List<Curve>();
            var varCurves = new List<Curve>();
            var fixCurves = new List<Curve>();
            string info;

            if (!DA.GetData(0, ref input) || input == null)
            {
                DA.SetData(4, "No curve connected.");
                DA.SetDataList(0, points);
                DA.SetDataList(1, allCurves);
                DA.SetDataList(2, varCurves);
                DA.SetDataList(3, fixCurves);
                return;
            }

            DA.GetData(1, ref fixedMin);
            DA.GetData(2, ref fixedMax);
            DA.GetData(3, ref repeatCount);

            Curve crv = input.DuplicateCurve();
            double totalLength = crv.GetLength();
            double halfFixedMax = fixedMax / 2.0;

            if (fixedMin <= 0.0 || fixedMax <= 0.0)
            {
                info = "Fixed Min and Fixed Max must be greater than 0.";
                DA.SetData(4, info);
                DA.SetDataList(0, points);
                DA.SetDataList(1, allCurves);
                DA.SetDataList(2, varCurves);
                DA.SetDataList(3, fixCurves);
                return;
            }

            if (repeatCount < 1)
            {
                info = "Repeat Count must be at least 1.";
                DA.SetData(4, info);
                DA.SetDataList(0, points);
                DA.SetDataList(1, allCurves);
                DA.SetDataList(2, varCurves);
                DA.SetDataList(3, fixCurves);
                return;
            }

            var patternTypes = new List<string>();
            for (int i = 0; i < repeatCount; i++)
            {
                patternTypes.Add("V");
                patternTypes.Add("F_half_max");
                patternTypes.Add("F_min");
                patternTypes.Add("F_min");
                patternTypes.Add("F_half_max");
                patternTypes.Add("V");
            }

            int countFMin = 0;
            int countFHalfMax = 0;
            int countV = 0;
            for (int i = 0; i < patternTypes.Count; i++)
            {
                string t = patternTypes[i];
                if (t == "F_min") countFMin++;
                else if (t == "F_half_max") countFHalfMax++;
                else if (t == "V") countV++;
            }

            double fixedTotal = (countFMin * fixedMin) + (countFHalfMax * halfFixedMax);
            double remaining = totalLength - fixedTotal;

            if (remaining <= 0.0)
            {
                info = string.Format(
                    CultureInfo.InvariantCulture,
                    "Curve too short.\nCurve length: {0:0.###}\nRequired fixed length: {1:0.###}\nReduce fixed values or Repeat Count.",
                    totalLength,
                    fixedTotal);

                DA.SetData(4, info);
                DA.SetDataList(0, points);
                DA.SetDataList(1, allCurves);
                DA.SetDataList(2, varCurves);
                DA.SetDataList(3, fixCurves);
                return;
            }

            double variableLength = remaining / countV;

            var patternLengths = new List<double>(patternTypes.Count);
            for (int i = 0; i < patternTypes.Count; i++)
            {
                string t = patternTypes[i];
                if (t == "F_min") patternLengths.Add(fixedMin);
                else if (t == "F_half_max") patternLengths.Add(halfFixedMax);
                else patternLengths.Add(variableLength);
            }

            var distances = new List<double>(patternLengths.Count + 1) { 0.0 };
            double current = 0.0;
            for (int i = 0; i < patternLengths.Count; i++)
            {
                current += patternLengths[i];
                distances.Add(current);
            }

            distances[distances.Count - 1] = totalLength;
            for (int i = 0; i < distances.Count; i++)
            {
                if (distances[i] < 0.0) distances[i] = 0.0;
                if (distances[i] > totalLength) distances[i] = totalLength;
            }

            var parameters = new List<double>();
            for (int i = 0; i < distances.Count; i++)
            {
                if (crv.LengthParameter(distances[i], out double t))
                {
                    parameters.Add(t);
                    points.Add(crv.PointAt(t));
                }
            }

            var splitParams = new List<double>();
            for (int i = 1; i < parameters.Count - 1; i++)
                splitParams.Add(parameters[i]);

            Curve[] pieces = splitParams.Count > 0 ? crv.Split(splitParams) : new[] { crv };
            if (pieces != null && pieces.Length > 0)
            {
                for (int i = 0; i < pieces.Length; i++)
                    allCurves.Add(pieces[i]);

                int n = Math.Min(allCurves.Count, patternTypes.Count);
                for (int i = 0; i < n; i++)
                {
                    if (patternTypes[i] == "V")
                        varCurves.Add(allCurves[i]);
                    else
                        fixCurves.Add(allCurves[i]);
                }
            }

            info = string.Format(
                CultureInfo.InvariantCulture,
                "Curve length: {0:0.###}\nFixed Min: {1:0.###} ({2}x)\nFixed Max (halved): {3:0.###} ({4}x)\nVariable length per segment: {5:0.###} ({6}x)\nPattern: {7}",
                totalLength,
                fixedMin,
                countFMin,
                halfFixedMax,
                countFHalfMax,
                variableLength,
                countV,
                string.Join(" ", patternTypes));

            DA.SetDataList(0, points);
            DA.SetDataList(1, allCurves);
            DA.SetDataList(2, varCurves);
            DA.SetDataList(3, fixCurves);
            DA.SetData(4, info);
        }

        protected override Drawing.Bitmap Icon => IconHelper.Load("CurvePatternSplitIcon.png");
        public override Guid ComponentGuid => new Guid("DE75D31F-29EB-48C7-9386-08DB7A5E6E07");
    }
}
