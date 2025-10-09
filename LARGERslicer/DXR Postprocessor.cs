using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

List<string> result = new List<string>();
List<string> header = new List<string>();
List<double> X_vals = new List<double>();
List<double> Y_vals = new List<double>();
List<double> Z_vals = new List<double>();

int line_num = 30;
List<string> movementLines = new List<string>();

// Regex for coordinates and angles
Regex rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
Regex ry = new Regex(@"Y\s*([-+]?[0-9]*\.?[0-9]+)");
Regex rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");
Regex ra = new Regex(@"A\s*([-+]?[0-9]*\.?[0-9]+)");
Regex rb = new Regex(@"B\s*([-+]?[0-9]*\.?[0-9]+)");
Regex rc = new Regex(@"C\s*([-+]?[0-9]*\.?[0-9]+)");

// Collect valid movement lines
foreach (string rawLine in robotLines)
{
    string line = rawLine.Trim();
    if (string.IsNullOrEmpty(line) || !line.Contains("PTP"))
        continue;

    Match mx = rx.Match(line);
    Match my = ry.Match(line);
    Match mz = rz.Match(line);

    if (mx.Success) X_vals.Add(double.Parse(mx.Groups[1].Value));
    if (my.Success) Y_vals.Add(double.Parse(my.Groups[1].Value));
    if (mz.Success) Z_vals.Add(double.Parse(mz.Groups[1].Value));

    if (mx.Success || my.Success || mz.Success)
        movementLines.Add(line);
}

// Clip P1 and F1 to match movement count
int movement_count = movementLines.Count;
P1_list = P1_list.GetRange(0, Math.Min(P1_list.Count, movement_count));
F1_list = F1_list.GetRange(0, Math.Min(F1_list.Count, movement_count));

// Calculate bounds
double xmin = X_vals.Count > 0 ? Min(X_vals) : 0;
double xmax = X_vals.Count > 0 ? Max(X_vals) : 0;
double ymin = Y_vals.Count > 0 ? Min(Y_vals) : 0;
double ymax = Y_vals.Count > 0 ? Max(Y_vals) : 0;
double zmin = Z_vals.Count > 0 ? Min(Z_vals) : 0;
double zmax = Z_vals.Count > 0 ? Max(Z_vals) : 0;

// Header
header.Add(";ProgRunTimeTotal =[0]");
header.Add(";machine_type =[DXR.KUKA]");
header.Add(";post_processor_version =[V1.0.3.17]");
header.Add(";1 SD.ACT.GEN.DESC.NAME =\"DEFAULT\"");
header.Add($";number of rows in org. file =[{robotLines.Count}]");
header.Add($";number of movement rows = [{movement_count}]");
header.Add(";number of layers =[X]");
header.Add($";Xmin = [{xmin:F3}]");
header.Add($";Xmax = [{xmax:F3}]");
header.Add($";Ymin = [{ymin:F3}]");
header.Add($";Ymax = [{ymax:F3}]");
header.Add($";Zmin = [{zmin:F3}]");
header.Add($";Zmax = [{zmax:F3}]");
header.Add(";Eges = IC[0.0]");
header.Add("; config end");
header.Add(";=================================");

result.AddRange(header);

// Generate DXR lines
for (int i = 0; i < movementLines.Count; i++)
{
    string line = movementLines[i];
    string X = TryMatch(rx, line, "X");
    string Y = TryMatch(ry, line, "Y");
    string Z = TryMatch(rz, line, "Z");
    string A = TryMatch(ra, line, "A");
    string B = TryMatch(rb, line, "B");
    string C = TryMatch(rc, line, "C");

    double p1 = P1_list[i];
    double f1 = F1_list[i];

    string newLine = $"N{line_num} G1 F{f1:F3} {X} {Y} {Z} {A} {B} {C} G91 XE=[{p1:F6}*P1] G90";
    result.Add(newLine.Trim());

    line_num += 10;
}

dxrLines = result;

// Helper functions
double Min(List<double> values)
{
    double min = double.MaxValue;
    foreach (double v in values)
        if (v < min) min = v;
    return min;
}

double Max(List<double> values)
{
    double max = double.MinValue;
    foreach (double v in values)
        if (v > max) max = v;
    return max;
}

string TryMatch(Regex r, string line, string label)
{
    Match m = r.Match(line);
    return m.Success ? $"{label}{double.Parse(m.Groups[1].Value):F3}" : "";
}
