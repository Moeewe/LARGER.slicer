# Testing

How LARGER.slicer has been tested so far, what each method is good for, and the traps. There is no
unit-test project in the repository: tests run against the built plug-in, in Rhino or in a console
bench, and their harnesses live outside the repository (in a session's scratch folder). This page
describes them so they can be rebuilt. Part of the [developer handbook](README.md).

**The owner's rule: "the algorithms have to work in Grasshopper".** An engine call that passes is not
enough; new components are checked on a real Grasshopper canvas, and results are judged by looking
at them (layers drawn as images, the canvas captured).

---

## 0. Ground rules

- **Never touch the owner's Rhino 8 or his open documents and canvas.** He works in Rhino 8 while
  automated tests run. Tests use their own Rhino 9 instance and their own documents.
- **One Rhino 9 per Mac.** Rhino 9 (WIP) runs one instance per machine, so there is one test slot at
  a time.
- **Install only `LARGERslicer.gha` and `LARGERslicer.pdb`, and only while Rhino 9 is closed.** Test
  install folder (macOS): `~/Library/Application Support/McNeel/Rhinoceros/packages/9.0/LARGERslicer/<version>/net8.0/`
  (built from `bin/Release/net8.0`). Copying the whole build folder created duplicates with " 2" in
  their names, and Grasshopper then loaded the old `LARGERslicer 2.gha`. A new build only loads after
  Rhino restarts.
- **Grasshopper 2 plug-in (test install):** `LARGERslicerGH2.rhp` + `.pdb` + `Clipper2Lib.dll` go into
  the sub-folder `.../packages/9.0/LARGERslicer/<version>/gh2/`, again only while Rhino 9 is closed,
  and `~/Library/Application Support/Grasshopper2/Settings/CustomPluginInclude.txt` holds that
  path. **Never into `net8.0/`:** Rhino 9.1 then tries to load the new .rhp as a Rhino plug-in at
  startup, fails (Grasshopper2.dll is not loaded yet) and its plug-in installer waits in a dialog;
  the MCP slot never comes up (`spawn_slot` times out). `sample <pid>` showed the main thread in
  `CRhPlugInManager::InstallPlugInsWithRegisteredUI`. Section 2.5.
- **Close documents without the save/autosave dialog**, or an automated run hangs on it (section 2.4).
- A slot that the test tooling did not spawn itself (an "adopted" one) cannot be closed by it; ask the
  owner to close that Rhino 9 window.

---

## 1. Console bench (no Rhino running)

Fast checks of the slicer core: build layers, run Walls / Bottom / Ribs / Build / Round / StartEnd /
Check, dump the points and draw them.

Recipe (as used):

- A net8 console project that references the built `LARGERslicer.gha` (`bin/Release/net7.0` or
  `net8.0`) and RhinoCommon.dll, Grasshopper.dll, GH_IO.dll **from the Rhino installation** with
  `<Private>false</Private>`.
- `AssemblyLoadContext.Default.Resolving` handler that maps `LARGERslicer`, `Clipper2Lib`,
  `RhinoCommon`, `Grasshopper`, `GH_IO` to their files.
- **RhinoCommon native stub.** RhinoCommon is a managed wrapper around `librhcommon_c`; outside Rhino
  that native library is not loaded. A tiny C file implements the handful of native entry points the
  core still touches, compiled to `librhcommon_c.dylib` and wired with
  `NativeLibrary.SetDllImportResolver` (or found via `DYLD_LIBRARY_PATH`):

  ```c
  double ON_2dVector_Length(V2 v);          double ON_3dVector_Length(V3 v);
  bool   ON_2dVector_Unitize(V2* v);        bool   ON_3dVector_Unitize(V3* v);
  void   ON_Plane_CreateFromFrame(PL* p, V3 o, V3 x, V3 y);
  void   ON_Plane_CreateFromNormal(PL* p, V3 o, V3 n);
  ```
  (`V2`/`V3` are structs of doubles, `PL` is origin + three axes.)
- Limits: the bench cannot mesh or slice Breps, and `new Plane(...)` needs the stub; set plane fields
  directly or use `Geo.PlaneAt`. This is why the core uses `Geo.Closed`/`Geo.PlaneAt` instead of
  `Polyline.IsClosed`/`new Plane(...)`.
- **Use real sliced contours.** Clean bench polygons hide layer-to-layer bugs: a mesh slice cuts a
  round wall into different pieces on every layer. Dump the `Contours` of a real `Slice` from a Rhino
  slot to a text file and replay them in the bench (that is how the cell and corner-rounding bugs
  showed up).
- Draw the result with Pillow (layers in top view, roles in colours) and look at it before reporting.

A second use of the same setup: **cross-checks** of outputs, e.g. feeding the robot moves of another
DXR file through `DXRHelper.ProcessRobotLinesToDXR` and comparing line by line.

---

## 2. Inside Rhino 9 (MCP test slot)

The `rhino` MCP server starts a Rhino 9 "slot" and runs C# (`run_csharp`) or Python in it.

### 2.1 Loading a fresh build without installing it

```csharp
var dir = ".../LARGERslicer/bin/Release/net8.0/";
AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
    e.Name.StartsWith("Clipper2Lib") ? Assembly.LoadFrom(dir + "Clipper2Lib.dll") : null;
var asm = Assembly.Load(File.ReadAllBytes(dir + "LARGERslicer.gha"));
Func<string, Type> T = n => asm.GetType("LARGERslicer.Toolpaths." + n);
```

`Assembly.Load(bytes)` does not lock the file, so the build can be repeated. The Clipper2Lib resolver
is needed because the bytes-loaded assembly has no folder to find its dependency in.

**Reloading:** a second `Assembly.Load(bytes)` of a rebuilt .gha does not rebind a test DLL that
references it (it binds to the first copy). For core tests written as a small net8 class library
(referencing the .gha and Rhino 9's RhinoCommon), load .gha + Clipper2Lib + test DLL into a **fresh
collectible `AssemblyLoadContext`** with a `Resolving` handler for each run, and call it by
reflection from `run_csharp`.

### 2.2 Components in an in-memory document

Component tests do not need the visible canvas: create `new GH_Document()`, add component instances
(created from the loaded assembly), set persistent data or connect parameters, call
`doc.NewSolution(false)` and read the outputs. Background components (`AsyncSolve`) need their
Background row off, or a wait for the delivered solution.

### 2.3 Quick Start by reflection

Quick Start builds its examples in `AddedToDocument`, scheduled after the current solution. To build
them synchronously into a test document:

```csharp
var qsT = asm.GetType("LARGERslicer.Components.Slicer.QuickStartComponent");
var doc = new GH_Document(); doc.Enabled = false;
var qs = (GH_Component)Activator.CreateInstance(qsT);
qsT.GetField("_done", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(qs, true); // no scheduled build
qs.CreateAttributes(); qs.Attributes.Pivot = new PointF(0, 0);
doc.AddObject(qs, false);
qsT.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(qs, new object[] { doc });
```

Then lay out (`ExpireLayout`/`PerformLayout`), check overlaps of groups and bounding boxes of the
paths (the examples must not touch each other on the shared bed), solve, and read the runtime
messages of every component.

### 2.4 The visible-canvas harness

For the final check of components and example files ("tested on the canvas"), a small net8 class
library was loaded into the Rhino 9 slot and called by reflection. It references Rhino 9's
RhinoCommon, Grasshopper, GH_IO, Eto and the built `LARGERslicer.gha`. Its parts (names as used,
rebuild as needed):

| Part | Does |
|---|---|
| Documents | `NewDoc()` (autosave off, new document on the active canvas); `Open(file)` reads a .gh through `GH_Archive.ExtractObject` instead of `GH_DocumentIO.Open`, which would ask in a dialog about IO messages; `CloseAll()` |
| Reports | runtime messages of every object (errors, warnings, remarks with position), text of every panel with its source, one output of a named component, the plug-in libraries a file references (`GHALibraries` chunk) |
| Rows | finds a component by name (`Name#k` = k-th from the top), lists its option rows, performs a real click on a row through the attributes |
| Checks | bounding boxes of all finished paths and which ones meet in 3D; dump of walls and path of one layer near a point, for plotting; wall corners the path does not reach |
| Capture | the canvas (`GH_Canvas.GenerateHiResImageTile`) and the Rhino viewport (`ViewCapture`) as PNG, to look at |
| Layout | helpers to place, group and tidy example files (wireless relays, full input names) and save them |

**Closing without the dialog** (otherwise the run hangs on "keep or discard autosave / save changes"):

```csharp
GH_AutoSaveSettings.EnableAutoSave = false;
foreach (var d in Instances.DocumentServer.ToList())
{
    d.Enabled = false;
    d.IsModified = false;
    Instances.DocumentServer.RemoveDocument(d);
}
Instances.ActiveCanvas.Document = null;
```

For the Rhino document: mark it unmodified or use `_-New _None` before exiting the slot.

### 2.5 Grasshopper 2 in the test slot

Grasshopper 2 is not loaded in a fresh slot: `Rhino.PlugIns.PlugIn.LoadPlugIn(new Guid("8307876d-a461-4daa-bb77-eb3715925513"))`,
then `RhinoApp.RunScript("_GH2", false)` opens the editor (the command is `GH2`, not `Grasshopper2`).
Check with `AppDomain.CurrentDomain.GetAssemblies()` which `LARGERslicerGH2` was loaded and from
where: `CustomPluginInclude.txt` decides it.

The harness (net8 class library, loaded by `Assembly.Load(bytes)` and called by reflection, as in
2.4) references Rhino 9's RhinoCommon, Eto, `Grasshopper2.dll`, `GrasshopperIO.dll` (in
`ManagedPlugIns/Grasshopper2Plugin.rhp/`) and the built .rhp. The Grasshopper 2 API it needs:

| Task | Call |
|---|---|
| New document on the canvas | `Document.NewActiveDocument()`, `Editor.Instance.Documents.Push(doc, null)` |
| Place, wire | `doc.Objects.Add(obj, new PointF(x, y))`, `Connections.Connect(output, input, null)` |
| Set an input | `Parameter<T>.Set(T[])` (by reflection for typed inputs; `GenericParameter.Set(object[])`), then `Expire()` the parameter and its component |
| Solve | `doc.Solution.StartWait()` (blocks), or `doc.Solution.Start(SolutionMode.Regular)` and poll |
| Results | `component.State.Phase`, `State.Data.Messages` (Level, Text, Info), `State.FaultException` (stack trace of a fault), `param.State.Data.Tree().NonNullItems` |
| Progress | `((DocumentObject)component).ProgressPercentage` while `Phase` is Computing |
| Tab | `ObjectProxies.Chapters` (chapter "LARGER", `Sections`, `Proxies` with `Nomen.Slot`, `Icon`) |
| Presets | `parameter.PresetsWeak` (enumerable) |
| Pictures | `Editor.Instance.Canvas.DrawToBitmap(region, width, height, true, true, true)`; `ObjectProxy.Draw(scale, padding, skin)` per component; the viewport with `Rhino.Display.ViewCapture` |

Closing: `Unmodify()` every document in `Document.AllDocuments`, pop documents from
`Editor.Instance.Documents` only while `Current` is not empty (popping the last one makes a new
empty document: a `while (Current != null)` loop never ends and hangs the slot).

Faults to look for first: an optional list input left open (GH2 9.1 throws in `GetItemArray`, use
`access.ItemArray`), typed inputs set through `as Parameter` (silently nothing), icons drawn as
lettered stand-ins (`PixelIcon` with several sizes throws; the instance's `Icon` is then a
`VectorIcon`).

---

## 3. Metrics and how results were judged

- **Path Check** on every path: 0 crossings, support, layer times; `Non-Planar Check` on vertical-nozzle
  non-planar paths. The example files of 1.2.x were opened this way and tidied until their checks were
  OK and no component showed an error.
- **Double-coverage raster metric** (first layer and other flat layers): rasterise every printing move
  as a bead of width w (round ends) on a **0.1 mm grid**; a cell covered by two beads that lie more than
  2 w apart along the path counts as double coverage. Report mm² (and, for an imported G-code,
  per spot). It measured the chevrons of the 2024 brim (121 mm²) and the improvements in
  [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) 9.6. Run it with Overlap 0, so intended overlap of
  neighbouring beads does not count.
- **Corner fidelity**: maximum distance of the path from the wall contour in a bench of rectangle, L
  and star, 1 and 2 walls, centred or inside, every seam mode, spiral (after the corner fix: 0.012 mm).
- **Against a reference G-code**: import it (`Import G-code`), run the same checks, compare laps, Z
  range, E per mm (5 % / median / 95 %), E total, first layer, time (the 2024 head).
- **Pictures**: layers in top view and the canvas captured, looked at before reporting. "Chaos" (ribs not
  square to the wall, joints not alternating evenly) is a failure even when the checks pass.

---

## 4. What is not tested automatically

Windows, real machines (every robot or machine program must run in T1 / test mode first), the
package installation through the package manager on every Rhino version, and Grasshopper 2 beyond the
components exercised in its README "Status" section (Quick Start and the main chain were run on the
Grasshopper 2 canvas in November 2026). Report honestly what was only tested outside
Grasshopper.
