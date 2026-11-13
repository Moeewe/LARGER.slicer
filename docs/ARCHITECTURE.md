# Architecture

How LARGER.slicer is built: where the code lives, how the slicer core is organised, how the
Grasshopper 1 and Grasshopper 2 plug-ins sit on top of it, and how it is built and released.
Part of the [developer handbook](README.md).

---

## 1. Repository layout

```
LARGER.slicer/
├── README.md, README.de.md        user-facing overview (EN / DE)
├── THIRD_PARTY_NOTICES.md         libraries, papers and ideas the code builds on
├── LICENSE                        CC BY-NC 4.0
├── docs/                          this handbook (*.md), OWN_MACHINES.md, the website pages (*.html) and README images
├── SETUP/                         installers (macOS .command, Windows .bat + .ps1), the FH Muenster definition,
│                                  RobotLibrary/ (KUKA KR120 R2700-2 for the Robots plug-in)
├── EXAMPLE FILES/                 example definitions, machine guides, Machines/ (own-machine templates)
├── LARGER.slicer.sln
├── LARGERslicer/                  the Grasshopper 1 plug-in (LARGERslicer.gha) and the shared core
│   ├── LARGERslicer.csproj        targets net8.0(-windows), net7.0(-windows), net48
│   ├── LARGERslicer.cs            GH_AssemblyInfo (plug-in id 2e6c19c9-...), category symbol "L"
│   ├── Toolpaths/                 THE SLICER CORE (shared with Grasshopper 2), namespace LARGERslicer.Toolpaths
│   ├── Components/
│   │   ├── AsyncSolve.cs          background calculation for GH1 components
│   │   ├── OptionRows.cs          the rows drawn under a component (choice, number, toggle, button)
│   │   ├── Slicer/                the Slicer tab (SlicerComponents.cs and friends)
│   │   ├── Robot/                 the Robot tab (RobotComponents.cs)
│   │   ├── Export/                DXR components and the curve tools of the former Toolpaths tab
│   │   ├── Utils/                 Utilities tab
│   │   ├── CNC/, CNCUtilities/    Zünd G3 milling (CNC - Program, Cutter Selector, CNC Utilities 01-08)
│   ├── Utils/                     older helpers (DXRHelper, PathHelper, Euler/Fermat/ArcWelder helpers, IconHelper ...)
│   ├── Types/                     MachineSettings (DXR start/end), Cutter, ThekenfrontModels
│   ├── Resources/                 component icons (*.png), 3D models (*.3dm: GingerOne, AE20_Extruder, FHMuenster_Cell),
│   │                              GingerOne.README.md, legacy *.svg
│   ├── Properties/Resources.resx  only the 24 px tab icon (LARGERslicer_24x24)
│   ├── scripts/generate_icons.py  the icon generator (see ICONS.md); the other generate_*.py are older and unused
│   ├── documentations/            older guides and references (component lists, DXR format, KUKA troubleshooting ...)
│   ├── build-package.sh / .bat    builds the three Yak packages
│   ├── manifest.yml, manifest-rh7.yml, yak.yml, icon.png, CHANGELOG.md
│   └── Plugin Installation Files/ a manually installable net48 .gha (+ pdb, Newtonsoft.Json) and the DXR converter definition
└── LARGERslicer.GH2/              the Grasshopper 2 plug-in (LARGERslicerGH2.rhp, Rhino 9)
    ├── LARGERslicerGH2.csproj     compiles ../LARGERslicer/Toolpaths/*.cs directly (no copies)
    ├── Plugin.cs, Shim.cs, Icons.cs
    ├── *Components.cs, QuickStart.cs
    └── Icons/48/                  48 px icons, same drawings as the 24 px GH1 icons
```

The stand-alone app **LARGER.app** is a separate, private project. It shares the presets and the
printing rules described in [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) (bead-width-driven
geometry, machine kinds, the bead law of the 2024 head, Short Lap Time ...); several core comments
say "as in LARGER.app" where a rule was taken over from it or into it. Nothing in this repository
depends on it.

---

## 2. The shared slicer core (`LARGERslicer/Toolpaths`)

Everything that cuts, fills, joins, checks and writes lives here, free of Grasshopper UI code. The
GH1 components and the GH2 components are thin wrappers that read inputs, call the core and set
outputs. Two rules keep the core portable and testable:

- **No Grasshopper types in the core**, with one exception: `SliceEngine.ToMesh` unwraps
  `Grasshopper.Kernel.Types.IGH_Goo`. Grasshopper 2 compiles the core with a one-line stand-in for
  that interface (`LARGERslicer.GH2/Shim.cs`). `ToolpathGoo.cs` (the GH1 goo wrappers) is excluded
  from the GH2 build.
- **Few native RhinoCommon calls in hot loops.** `Geo.cs` has plain C# versions of `Polyline.IsClosed`
  (`Geo.Closed`) and plane construction (`Geo.PlaneAt`, `Geo.PlaneFromAxes`): RhinoCommon's versions call
  into Rhino's native library, which is slow in tight loops and makes the core impossible to run
  outside Rhino (see [TESTING.md](TESTING.md)). `Inv.cs` formats every number of every report and
  file line with the invariant culture (`2.5`, never `2,5` on a German Windows).

### 2.1 Data model (`Toolpath.cs`, `Warp.cs`)

| Type | What it is |
|---|---|
| `PrintSettings` | The one place for bead width, layer height and speed. Defaults: bead 5 mm, layer 2 mm (house standard, 3 mm nozzle), Overlap 0.15, Speed 3000 mm/min (= 50 mm/s), MinSpeed 300, MaxSpeed 12000 (also the travel speed), Resolution 2 mm. Also FirstLayerHeight, FirstLayerSpeed, MinLayerTime, ShortLapTime, MaxVolumeRate, Flow, Density, Nozzle/NozzleShape, `Machine` (a `MachineProfile`). Derived: `BeadSpacing = width x (1 - overlap)` (overlap clamped 0..0.9), `FirstLayer`, `PointSpacing` (x3 in Preview), `EffectiveMinRadius`, `BeadArea(w, h)` (squashed bead: rectangle with round ends). **Speeds are stored in mm/min** (the F of G-code and DXR); the components take mm/s (see `Components/Slicer/SpeedUnits.cs`). |
| `MachineProfile` | How E is computed for a machine: `E = section x length x Flow x Factor` (x FirstLayerFactor on the first layer), divided by the filament area when FilamentDiameter > 0. `RectangleSection = true` uses w x h (as the hand-made scripts), false the squashed-bead area. Five presets: Generic pellet (volume), Generic filament 1.75 / 2.85, Weber DXR25 (factor 1.128, rectangle, "x P1"), Ginger One (factor 0.21, rectangle). |
| `SliceLayer` | One slice: `Index`, `Plane` (at nozzle height, normal = tool axis), `Height`, `Contours` (polylines, in flat space when warped), `Rings` (wall number per contour, 0 = outermost), `Reference` (plain walls before infill, used to tell wall from infill), `ContourRoles`, `EntryHints` (ends of fill paths, where the wall should start), `Warp`. `RealContours()` maps warped contours back for display. |
| `Toolpath` | The print path. Parallel lists per point: `Points`, `Normals` (tool axis), `Heights` (local layer height), `Widths` (bead width), `Flows` (0 = no extrusion), `Roles` (`PathRole`: Wall, Bottom, Infill, Support, Seam, Travel, Skirt), `Layers` (layer index). **Height, flow and role describe the move that ends at the point.** Plus `Settings` and `Warp` (set when the path was mapped back from non-planar / multi-axis space; its points are in real space). |
| `Warp` (abstract) | Non-planar and multi-axis slicing by deformation: `ToFlat`, `ToReal` (+ layer normal), `Scale` (real / flat layer height, goes into the extrusion), `TiltTool` (false = nozzle stays vertical), `KeepLayerStep`, `UseBeadLaw`, `MinLoopFactor`, `FlatObject` (maps user input such as seam points and rib curves into flat space). Implementations: `SurfaceWarp` (guide surface: Follow, or Morph from a flat first layer to the guide), `CurveWarp` (layers square to a guide curve), `MeridianWarp` (follow the part / follow the surface, in `NonPlanarFromPart.cs`). |

The GH1 goo types (`ToolpathGoo.cs`: `GH_PrintSettings`, `GH_MachineProfile`, `GH_SliceLayer`,
`GH_Toolpath`) wrap these and draw previews. In GH2 the plain objects travel through the wires; all
layers of a part travel as one `LayerStack` item.

### 2.2 The pipeline and its engines

```
geometry ─ SliceEngine ─ layers ─ FillEngine (walls, ribs, bottom) / CellFill / infill ─ PathEngine.Build ─ toolpath
          (Warp: flat space)                                                                  │
          PathEngine.Round ─ SurfacePattern (optional) ─ LeadEngine.StartEnd ─ LeadEngine.Check / NonPlanarCheck
          PathEngine.Process (E and F per move) ─ GingerGCode / MachineGCode / DesktopGCode / RobotLines + DXRHelper / RobotProgram
```

| File | Role |
|---|---|
| `SliceEngine.cs` | `ToMesh`: any input (open/closed Breps, surfaces, extrusions, SubDs, meshes) meshed once into one welded mesh. Rhino's mesher is called under `MeshLock`, one call at a time (background tasks). `Slice`: own triangle/plane cutter, every triangle assigned once to the layers it crosses; the model is cut at the middle of each layer and the contour is lifted to the top of the layer (nozzle height). The first layer is FirstLayerHeight thick, so the nozzle never runs on the bed. With a `Warp` it slices in flat space. |
| `FillEngine.cs` | Walls (`position` 0 inside / 1 centred / 2 outside of the geometry; walls one `BeadSpacing` apart; holes grow), infill ribs (hairpins from the wall, tip one bead spacing short of the opposite wall; from both walls meeting in the middle with the joint alternating per layer), sketched ribs (`WithoutCrossingSketches` drops a sketch line that would cross another), bottom/top fill (`Bottom`: 0 auto, 1 zigzag turned 90° per layer, 2 contour). A place from another layer (rib point, sketch) is found straight above or below it on this layer. `FillEngine` is a partial class: `CellFill.cs` and `RegionFill.cs` add to it. |
| `CellFill.cs`, `CellLibrary.cs` | `FillEngine.Cells`, `Waves` (wave, truss, meander along the wall, meeting in the middle) and `Chambers` (a chamber wall, boxes or truss). Cells: honeycomb and grid as part of the one wall line: every cell is a loop half a bead spacing inside the cell, the loops are joined into ONE closed contour by a spanning tree with crosswise two-line bridges (cannot cross, also prints in spiral mode). `CellLibrary`/`ModelFile` read and write machine and cell 3D models as plain .3dm files (one layer per part). |
| `FermatFill.cs` | Solid fill as one line that never crosses itself and starts and ends side by side (Connected Fermat Spirals, Zhao et al. 2016). Used by contour bottom fill and the curve fills. |
| `RegionFill.cs` | `PathTools`: fills one flat region (curve minus holes) for the curve-based components of the former Toolpaths tab, on the same engines. |
| `PathEngine.cs` | `Build`: one continuous path from the layers. **Layered** (default): every layer flat, changes layer over a short ramp at one place (Seam Gap, default one bead width); `SeamMode` Fixed / InnerCorner / SharpestCorner; contours inside each other are joined by a two-line bridge that stacks into a rib (`bridge`). **Spiral**: vase mode, only for layers that are exactly one closed wall; flat first loop stops one bead before its start, the first climbing loop ramps from zero. `Round`: rounds corners in the plane of their layer (keeps heights; ramps smoothed separately). `Process`: extrusion and feed per move (see PRINTING_PRINCIPLES.md 4). `LayerTargets`: Min Layer Time / Short Lap Time per layer. |
| `LeadEngine.cs` | `StartEnd`: skirt (always first, runs on into the outer wall), brim (outside / inside / both, staggered links), exit move off the part (sideways, then up; Exit Flow 0 = no extrusion), then `Thin` (drops points closer than max(0.5, 0.2 w) unless they carry a change or a corner) and `Approach` (way in from above, default 30 mm). `Check`: support (offset to the layer below, ≤ ½ bead), crossings within a layer, layer times, suggested speed. |
| `FollowSurface.cs`, `NonPlanarFromPart.cs` | `SurfaceField` (u = d_bottom / (d_bottom + d_crown) by Dijkstra on the welded mesh) and `BeadLaw` (bead height from the lap below). `MeridianWarp`: "Follow the part" (Guide Mode 2: contours a quarter layer apart, aligned seams, arc length along meridians) and "Follow the surface" (Guide Mode 3, the 2024 head). |
| `NonPlanarCheck.cs` | For vertical-nozzle non-planar paths: measured bead height per point (vs 0.8 w, 0.4 h and the E the point was given), head clearance (cone + cylinder head model, `Graze` 0.6 mm tolerated). |
| `SurfacePattern.cs` | Textures in the wall (Knit, Ribs, Waves, Zigzag, Dots, Loops, Image) on a finished path; see PRINTING_PRINCIPLES.md 8. |
| `SelfSupport.cs` | Overhang check for a build direction (flat, angled, guide), smallest self-supporting envelope, best slice angle. Rule: lean ≤ maxOffset (0.5 w) per layer = atan(maxOffset / h) (5 mm bead, 2 mm layer = 51°). |
| `SupportFins.cs` | Thin hairpin fins outside the part under overhangs, part of the one line; neck of about one bead for easy removal. |
| `StartEndFeature.cs` | Sacrificial start and end layers for angled printing (CEAD: about 5 layers), milled away later. |
| `DesignRules.cs` | CEAD 45° sheets and Ginger handbook: layer height N/4..N/2 (Ginger ≤ 0.6 N), overlap 5-10 % LW, CO90 = 2 LW - OL, CO45 = ½√2 (2 LW - OL), LW45 = √2 t_finish, Ginger LW ≥ 1.5 N, LW/LH ≥ 2. |
| `Nozzles.cs` | Nozzle presets and bead/layer ranges (see PRINTING_PRINCIPLES.md 5). |
| `Machines.cs` | `MachineKind` (gantry, bedslinger, delta, belt 45, robot) and what each can print (`PathFit`), `MachineDefinition` (built-in Ginger One Marlin, Generic Marlin/Klipper, Ginger One Klipper; own `.machine` text files from `Documents/LARGER/Machines`), `MachineGCode`. Guide for users: [OWN_MACHINES.md](OWN_MACHINES.md). |
| `MachineOutput.cs` | `GingerGCode` (start/end sequences of the Ginger quick start guide, absolute XYZ, relative E) and `RobotLines` (one PTP line per point, fixed A/B/C, for the DXR Generator). |
| `DesktopGCode.cs` | Scaled test prints for desktop printers (Marlin, Bambu P1/X1/A1/H2D, Prusa MK4/Mini, Ender 3, Klipper macros, Neptune 4). |
| `Materials.cs`, `MaterialField.cs` | `.material` files (temperatures as ranges, drying, density, factor) and `MaterialFactor` (new factor = target / measured width x current). `MaterialField`: the path as an orthotropic material field (CSV, JSON, VTK) for FE/IGA; default stiffness values are placeholders. |
| `GCodeImport.cs` | Reads G-code of other slicers (Cura, Simplify3D, PrusaSlicer, AiSync, Grasshopper scripts) and Weber .dxr into a `Toolpath`, so the same checks run on foreign paths. |
| `Robot.cs`, `RobotGeometry.cs`, `ExternalAxes.cs`, `Collision.cs` | Plain-number robot maths (`Frame`), `RobotArm`/`RobotTool`/`RobotCell`/`RobotProgram` (reach, axes, KRL, lines for DXR), arm meshes from the Robots plug-in library (.xml + .3dm of the same name), rail and turntable (`ExternalAxisSolver`), collision model (robot, tool, axes, printed volume, bed, environment; near misses within Clearance). |
| `PrinterModel.cs` | How machines and robots are drawn (matte materials, crisp meshes), `PrinterModel` (layers Frame, Bed, Z, Y, X, TCP of a machine .3dm), `PrinterRun` (poses along the path for Printer Simulation, gantry collision). Built-in model: `Resources/GingerOne.3dm` (spec: [GingerOne.README.md](../LARGERslicer/Resources/GingerOne.README.md)). |
| `EasySlicer.cs` | The whole chain in one call with the common options; `OutputFile` builds the file names (`DXR_2611051216_Test Object_2668g_53min.dxr`). |
| `DemoParts.cs` | The demo parts of Quick Start (shared by GH1 and GH2). |
| `Geo.cs`, `Inv.cs` | See above. |

Older helpers outside `Toolpaths/` (`Utils/DXRHelper.cs`, `PathHelper.cs`, `Types/MachineSettings.cs`,
`Types/Cutter.cs`) are also compiled into GH2 because the DXR and CNC components need them.

---

## 3. The Grasshopper 1 layer (`LARGERslicer/Components`)

### 3.1 Tabs and groups

Category `LARGER`, subcategories `Slicer`, `Robot`, `DXR`, `Utilities`, `CNC`, `CNC Utilities` (and `Export` for CAD Package Export alone).
The Slicer tab is ordered by exposure: primary = Start (Quick Start, Easy Slicer, Print Settings,
Machine Profile, Design Rules, Print Bed, Material ...), secondary = Layers (Slice, self-support
tools), tertiary = Walls and fill, quarternary = Path (Continuous Path, Round Corners, Start and
End, Surface Pattern), quinary = Check, senary = Output, septenary = Curve tools (Layers to Curves,
Curves to Layers and the `Curve ...` components; Align Curves, Alternate Curves and Join Open
Contours are there too but hidden, found by search). **Component names, inputs, outputs and GUIDs
of released components never change**, so old definitions keep opening.

### 3.2 `SlicerComponent` (base of the Slicer tab)

`Components/Slicer/SlicerComponents.cs`. Gives every slicer component:

- **Option rows** (`CreateAutoInputs`): `AddValueList` / `AddValueListAt` (a choice, item i = value i),
  `AddSlider` (a number with range, decimals, unit and quick picks; `QuickPicks` chooses a handful of
  round values), `AddAngle` (0/15/30/45/60/75/90°), `AddCount` (whole numbers), `AddToggle`,
  `AddButton` (momentary, for Save inputs). The name "AutoInputs" is historic: these used to place
  sliders and value lists beside the component; `SuppressAutoInputs` is kept only for Quick Start.
- **Background calculation** when `CanRunInBackground` is true (see 3.4).
- `SlicerIO.Get<T>`: unwraps `GH_Goo<T>` or `ScriptVariable()`.

The Robot tab has its own base (`RobotComponent`) with the same rows.

### 3.3 Option rows (`Components/OptionRows.cs`)

`OptionRow` (Kind Choice / Number / Toggle / Button, Label, `Input` index, Items, Presets, Decimals,
Unit, Min/Max/Step/Default, On/Off texts, Click, Momentary, GetSetting/SetSetting) and
`OptionAttributes` (a `GH_ComponentAttributes` that draws the rows under the component and handles
mouse input).

- **The value goes into the input's persistent data** (`OptionAttributes.Set`), exactly as if typed
  in, then the input is expired. The component reads it the normal way, the file keeps it, and a
  wire into the input wins (the row then says "connected" and ignores clicks).
- Numbers: drag left/right over the range (about 250 px; Shift = finer; one solution on release),
  click to type in a box over the row (`TryParseNumber` reads `45`, `45°`, `12,5 mm`, `1.5e3`), or
  the arrow for quick picks and the default. Integer inputs get integers.
- Buttons on boolean Save inputs are momentary: true for one solution, then set back to false
  quietly (`QuietReset`, which also tells AsyncSolve that nothing really changed).
- A toggle may act on a component setting instead of an input (`GetSetting`/`SetSetting`), e.g.
  `Background: on`.

### 3.4 Background calculation (`Components/AsyncSolve.cs`)

Used by Slice, Continuous Path, Surface Pattern, Material Field, Self Support Check, Non-Planar
Check and Robot Program. Pattern inside `SolveInstance`:

```csharp
if (Async.Deliver(da)) return;          // a finished result is handed out
// read every input here, on the UI thread; copy geometry (AsyncSolve.Copy)
Async.Run(da, job => { /* calculate; job.SetData / SetDataList / AddRuntimeMessage / OnApply */ });
```

- `Before()` (BeforeSolveInstance) cancels running tasks, bumps a generation counter and starts a
  clock; `Run` starts a `Task` per iteration with a `CancellationToken`; the work never touches
  Grasshopper, everything goes through the `AsyncJob`.
- When all tasks of a generation are finished, `Ready` schedules a solution in which the component
  expires itself (`_selfExpire`) with `_deliver = true`; that solution hands out the stored jobs per
  iteration (`Hand`: errors become error messages, then messages, items, lists, OnApply actions).
- An input change meanwhile (`Expired`) marks the work dirty: its results are thrown away and the
  new solution starts again. Removing the component or closing the document cancels.
- The component message shows `computing 3.2 s` (and `k/n ·` for several iterations), refreshed by a
  200 ms timer; no clock is left standing when nothing started.
- `Enabled` is saved in the file (`LARGER.Background`); row `Background` and the context menu item
  "Calculate in the background" switch it. With Background off, `Run` calculates right away.

### 3.5 Speeds in mm/s (`Components/Slicer/SpeedUnits.cs`)

Components take mm/s and convert to mm/min. A definition written by a version with mm/s carries the
key `SpeedMmS`; a definition without it is old and its speeds are read as mm/min, with a remark. A
value above 1000 mm/s gets a warning ("did you mean ... mm/s?").

### 3.6 Icons (`Utils/IconHelper.cs`)

`IconHelper.Load("SlicerSliceIcon.png", this)` reads the embedded resource
`LARGERslicer.Resources.<name>`; on any error it returns null and Grasshopper draws its default icon.
The plug-in icon comes from `Properties/Resources.resx` (`LARGERslicer_24x24`). How the PNGs are
made: [ICONS.md](ICONS.md).

### 3.7 Quick Start builder

`QuickStartComponent` (in `SlicerComponents.cs`) has no inputs or outputs. When it is added to a
document (`AddedToDocument`, not when a file is read: `Read` sets `_done = true`), it schedules
`Build(doc)` after the current solution and places the eight examples (see
[EXAMPLES.md](EXAMPLES.md)): `place` positions objects relative to its own pivot, `relay` makes a
"wireless" named parameter fed by a hidden wire (`⇢ Settings`, `⇢ Robot Cell`), `group` and
`section`/`write` add coloured groups and numbered scribble titles in the style of the FH Muenster
definition, a yellow INSTRUCTIONS panel and a yellow `Part name` panel that feeds every file name.
Values are set by writing persistent data of the inputs (`setInt`, `GH_Number`), never by sliders.
At the end Quick Start removes itself from the document.

---

## 4. The Grasshopper 2 layer (`LARGERslicer.GH2`)

- **Same core, compiled in**: `<Compile Include="../LARGERslicer/Toolpaths/*.cs" Exclude="ToolpathGoo.cs" LinkBase="Core" />`
  plus `DXRHelper.cs`, `MachineSettings.cs`, `PathHelper.cs`, `Cutter.cs`. Fixing the core fixes both plug-ins.
- `Plugin.cs`: `LargerSlicerPlugin : Grasshopper2.Framework.Plugin` (name, author, version 0.1.0,
  icon) and an empty `Rhino.PlugIns.PlugIn` so Rhino accepts the `.rhp`.
- `Shim.cs`: the stand-in `IGH_Goo` interface for the core.
- **`Io` helpers** (`SlicerComponents.cs`, `Icons.cs`, partial static class): `Io.Name(name, info,
  section, slot)` creates the `Nomen` (chapter `LARGER`, sections `Slicer`, `DXR`, `Robot`,
  `Utilities`, `CNC`); `Io.HiddenName` for components that only load old files (`Infill`, `Round and
  Lead`, Rank.Hidden); `Get<T>`, `Settings`, `Layers` (from a `LayerStack`), `Path`, `Contours`,
  `Solid` (GH2 hands over Box, Sphere, Cylinder ... as values: made into Breps), `Remarks`.
- **Groups = slots**: `Group.Start 0, Layers 1, Walls 2, Path 3, Check 4, Output 5, Curves 6`, the
  same order as the GH1 exposures.
- **Choices and values**: GH2 has no option rows. `Io.Choices(...)` adds the GH1 choice items as
  presets of an integer input (item i = value i); `Io.Values(...)` adds round values as presets of a
  number input.
- **Progress and early stop**: GH2 already solves beside the canvas. Long components call
  `if (Io.Step(access, 30)) return;` between their steps: it sets the progress (0-100 %) and returns
  true when Grasshopper cancelled the solution because inputs changed.
- **Icons**: `Io.Icon("SlicerSliceIcon")` builds one `PixelIcon` from the embedded 24 px image (the
  GH1 file), the 48 px image (`Icons/48`) and, for the plug-in, the 256 px logo; cached; never throws.
  Resource names: `LARGERslicer.GH2.Icons.<24|48|256>.<name>.png`.
- **Quick Start** (`QuickStart.cs`): GH2 version with examples 00-05 (settings and five examples).
- What is not ported and why: [LARGERslicer.GH2/README.md](../LARGERslicer.GH2/README.md).

---

## 5. Build, install, release

### 5.1 Grasshopper 1 (`LARGERslicer/`)

```bash
cd LARGERslicer
dotnet build -c Release            # all five target frameworks
./build-package.sh [1.3.0]         # or build-package.bat; optional new version
```

| Target | Rhino | Grasshopper package |
|---|---|---|
| `net48` | Rhino 7 (and the Windows fallback of 8/9) | Grasshopper 7.0.20314.3001 |
| `net7.0`, `net7.0-windows` | Rhino 8 (macOS / Windows) | Grasshopper 8.0.23304.9001 |
| `net8.0`, `net8.0-windows` | Rhino 9 | Grasshopper 9.0.26300.9401 |

NuGet: Clipper2 1.5.4 (Boost licence, walls and infill offsets), Newtonsoft.Json, System.Resources.Extensions.
On macOS net7.0/net8.0 reference WinForms (reference assemblies / `Microsoft.WindowsDesktop.App.WindowsForms`),
because the option rows and menus use `System.Windows.Forms`.

`build-package.sh` (same steps in the `.bat`):

1. With a version argument, writes it into the csproj (Version, AssemblyVersion, FileVersion,
   InformationalVersion), `manifest.yml`, `manifest-rh7.yml` and `yak.yml`; without one it reads
   the csproj and **refuses to build if a manifest has another version**.
2. Finds `yak` (PATH, then the one inside Rhino 9 or Rhino 8), `dotnet clean`, `dotnet build -c Release`.
3. Copies outputs to `net48/`, `net7.0*/`, `dist/LARGERslicer*.gha` and `Plugin Installation Files/LARGERSlicer/`.
4. Stages each package in its own folder under `obj/yak-staging` (`yak build` packs everything in the
   current folder; packages built in the project folder once contained sources, obj/ and PDFs):
   rh9 = net48 + net8.0 + net8.0-windows, rh8 = net48 + net7.0 + net7.0-windows, rh7 = net48 at the
   root with `manifest-rh7.yml`. Adds icon.png, README.md, LICENSE; deletes .pdb.
5. `yak build --platform any` per stage, checks the tag (`-rh9_0-`, `-rh8_0-`, `-rh7_0-`: yak tags by
   the newest Grasshopper referenced, so net8.0 must stay out of the Rhino 8 package) and moves the
   `.yak` files to `dist/`. It prints the `yak push` commands; **pushing is a manual step of the owner.**

Version numbers: the csproj (`1.3.0`, unreleased at the time of writing) and the manifests go
together; the last release is listed in [CHANGELOG.md](../LARGERslicer/CHANGELOG.md). Older notes:
`documentations/YAK_PACKAGE_GUIDE.md`, `VERSIONING_STRATEGY.md`.

**The `obj/` quirk.** `.gitignore` ignores `**/obj/`, but 14 files under `LARGERslicer/obj/`
(`project.assets.json`, `*.nuget.*`, `Debug/*/AssemblyAttributes.cs`, `*.assets.cache`,
`FileListAbsolute.txt`) were committed before that rule and are still tracked. Every build or restore
changes them, so `git status` shows them modified. Never commit them with real work; restore them with
`git checkout -- LARGERslicer/obj` (or leave them out of `git add`). The same goes for the build
output copied into the tracked `Plugin Installation Files/LARGERSlicer/*.gha|pdb|dll` and the
`net7.0*/` runtime-config folders: commit those only as part of a release build.

### 5.2 Grasshopper 2 (`LARGERslicer.GH2/`)

```bash
cd LARGERslicer.GH2
dotnet build -c Release            # -p:Rhino9=<folder with RhinoCommon.dll> if Rhino 9 is elsewhere
```

References RhinoCommon, Grasshopper2, GrasshopperIO and Eto from the Rhino 9 installation
(`/Applications/Rhino 9.app/Contents/Frameworks/RhCore.framework/Versions/A/Resources`, or
`C:\Program Files\Rhino 9\System`), not from NuGet. Output: `bin/Release/net8.0/LARGERslicerGH2.rhp`
+ `Clipper2Lib.dll`. Load it by adding its full path as a line to `CustomPluginInclude.txt` in the
Grasshopper 2 settings folder (macOS `~/Library/Application Support/Grasshopper2/Settings`, Windows
`%APPDATA%\Grasshopper2\Settings`) and restarting Rhino. There is no Yak package for GH2 yet.

### 5.3 Where the plug-in is installed

| What | macOS | Windows |
|---|---|---|
| Yak packages (installer, package manager) | `~/Library/Application Support/McNeel/Rhinoceros/packages/<7.0, 8.0, 9.0>/LARGERslicer/<version>/` (one subfolder per target framework) | `%APPDATA%\McNeel\Rhinoceros\packages\...` |
| Old manual copies (detected and moved to a backup by the installer) | `.../McNeel/Rhinoceros/<n>.0/Plug-ins/Grasshopper (b45a29b1-...)/Libraries` | `%APPDATA%\Grasshopper\Libraries` |
| Robots library (installer) | `~/Documents/Robots` (Rhino 8, Robots 2.x), `~/Robots` (Rhino 7) | `Documents\Robots` |
| Own machines, materials, cells | `Documents/LARGER/Machines`, `.../Materials`, `.../Cells` | same under Documents |

`SETUP/Install Mac.command` and `SETUP/Install Windows.bat` (+ `setup-windows.ps1`) install Robots,
LARGERslicer and Pufferfish from the package server, copy the KR120 library, keep newer (beta)
plug-ins and back up differing files. See [SETUP/README.md](../SETUP/README.md).

### 5.4 Release steps

What the scripts and the repository imply (the owner decides the details):

1. Changes merged on `main`; the CHANGELOG `[Unreleased]` section gets the version number.
2. `./build-package.sh <version>` syncs all version numbers and builds; check the three tags in `dist/`.
3. Test locally: drag the `.yak` for your Rhino onto the Rhino window, open the example files.
4. Publishing (`yak push`, GitHub release with `LARGERslicer-SETUP.zip`, website) is done by the owner;
   automated sessions never push packages.
