// Paste into run_csharp of a Rhino 9 test slot. Set REPO to the checkout, build first:
//   dotnet build LARGERslicer/LARGERslicer.csproj -c Release -f net8.0
//   dotnet build tests/CollisionBench -c Release
// METHOD: Compare (arg "" = six cases and the gantry check, "qs" = also the Quick Start robot examples),
// QuickStart (arg "full" = solve Quick Start with the checks on and read Robot Program), Regression.
var REPO = "/path/to/LARGER.slicer/";
var W = REPO + "LARGERslicer/bin/Release/net8.0/";
var R = REPO + "tests/CollisionBench/bin/Release/net8.0/";
var alc = new System.Runtime.Loader.AssemblyLoadContext("cb-" + DateTime.Now.Ticks, true);
var files = new Dictionary<string, string> { { "LARGERslicer", W + "LARGERslicer.gha" }, { "Clipper2Lib", W + "Clipper2Lib.dll" }, { "ColBench", R + "ColBench.dll" } };
alc.Resolving += (ctx, name) => files.TryGetValue(name.Name, out var f) ? ctx.LoadFromStream(new System.IO.MemoryStream(System.IO.File.ReadAllBytes(f))) : null;
foreach (var k in new[] { "Clipper2Lib", "LARGERslicer" }) alc.LoadFromStream(new System.IO.MemoryStream(System.IO.File.ReadAllBytes(files[k])));
var rt = alc.LoadFromStream(new System.IO.MemoryStream(System.IO.File.ReadAllBytes(files["ColBench"])));
var m = rt.GetType("ColBench.B").GetMethod("Compare");
string res;
try { res = (string)m.Invoke(null, new object[] { "qs" }); } catch (Exception e) { res = e.ToString(); }
alc.Unload();
Console.WriteLine(res);
