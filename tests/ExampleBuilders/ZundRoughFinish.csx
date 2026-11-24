// Builds "EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/Zünd Rough and Finish (height model).gh":
// the canvas that CNC - Quick Start places, saved as a file. Run inside Rhino with Grasshopper 1 and LARGERslicer loaded.
using System; using System.Linq; using System.Reflection;
using Grasshopper.Kernel;
string OUT = "/Users/moritz/Documents/LARGER.slicer/EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/Zünd Rough and Finish (height model).gh";
var count = Grasshopper.Instances.ComponentServer.ObjectProxies.Count;
var t = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "LARGERslicer").GetTypes().First(x => x.Name == "CncQuickStartComponent");
var doc = new GH_Document();
t.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { doc, 40f, 480f });
Console.WriteLine(new GH_DocumentIO(doc).SaveQuiet(OUT));
