// In Rhino 9 (script editor or run_csharp): builds nothing, loads the bench and the plug-in into their own context.
using System; using System.IO; using System.Reflection; using System.Runtime.Loader;
var root = "/Users/moritz/Documents/LARGER.slicer/"; var rel = root + "LARGERslicer/bin/Release/net8.0/";
var alc = new AssemblyLoadContext("zund", true);
Func<string, Assembly> load = p => { using var ms = new MemoryStream(File.ReadAllBytes(p)); return alc.LoadFromStream(ms); };
load(rel + "Clipper2Lib.dll"); load(rel + "LARGERslicer.gha");
var run = load(root + "tests/ZundBench/bin/Release/net8.0/ZundBench.dll").GetType("Run");
var log = Path.Combine(Path.GetTempPath(), "zundbench.txt");
System.Threading.Tasks.Task.Run(() => run.GetMethod("All").Invoke(null, new object[] { log, "" }));   // or "Mutations" with one argument
Console.WriteLine(log);
