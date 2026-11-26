# Examples

Every example file in the repository and every section of Quick Start: what it shows and for which
machine. Part of the [developer handbook](README.md).

Component lists below were read from the files themselves (Grasshopper archives) and from the
Quick Start code; files in `99 - ARCHIVE` are kept for reference and are not maintained.

---

## 1. Quick Start (component on the Slicer tab)

Drop `Quick Start` on an empty canvas: it builds the examples, wired and ready, and then removes
itself. All examples stand side by side on one **1000 x 1000 mm print bed** (origin in the front left
corner) so their paths never touch. Code: `QuickStartComponent.BuildRows` in
`LARGERslicer/Components/Slicer/SlicerComponents.cs`, demo parts in `Toolpaths/DemoParts.cs`. Common
to all: a yellow INSTRUCTIONS panel, a yellow `Part name` panel that goes into every file name, and
"wireless" relays (`⇢ Settings`, `⇢ Robot Cell`) fed by hidden wires.

| Section | Shows | Machine / output |
|---|---|---|
| **00. Settings for all examples** | `Machine Profile` (Ginger One, factor 0.21) → `Print Settings` (first layer at 25 mm/s) → `Print Bed` (bed in the viewport, checks every path lies on it) | shared by all |
| **01. Flat layers - from the part to the file** | The normal chain on a curved hull, 60 mm: `Slice` → `Walls` → `Bottom Fill` → `Infill Ribs` (every 150 mm, meeting in the middle) → `Continuous Path` → `Round Corners` → `Start and End` → `Extrusion and Speed`, `Path Check`. Outputs: **A** `Machine` → `Machine G-code` (Save G-code; up to 2.0.4 `Ginger G-code`); **B** the FH Muenster robot cell (`Cell Geometry`, `Robot`, `Robot Tool`, `Robot Cell`, `Robot Program`, `Machine Settings`, `DXR Generator`, `Robot Simulation` with a slider and Play; from 2.0.5 `Robot Program` with the AE20 uses the Weber DXR25 profile (1.128), so B writes correct DXR values instead of the Ginger factor 0.21); **C** `Desktop Test Print` | Ginger One (G-code), Weber DXR25 on the KUKA KR120 (DXR), a desktop printer |
| **02. Non-planar layers** | A round hull whose top follows a saddle surface; `Slice` with the guide surface, Guide Mode 0 (flat bottom, top follows the guide) | any machine; tool square to the layer by default |
| **03. Multi-axis layers** | A part that grows along a bent axis; `Slice` with a guide curve (layers square to it), plus `Robot Program` and `Robot Simulation` where the tool leans with the layers | robot |
| **04. Part with an opening** | `Infill Wave` (wavelength 60 mm) joins the wall of the opening and the outer wall into one line; try Shape 1 (Truss) or 2 (Meander) | any |
| **05. Angled layers** | A block leaning 55°, more than flat layers can overhang; `Slice` with Angle 45, every bead rests on the one below | robot or tilted bed |
| **06. Easy Slicer - one component** | A vase printed as a spiral by `Easy Slicer` alone (Ginger profile, nozzle preset "3 mm flat nozzle, wide bead (tested)"), then `Path Check` | any; connect Toolpath to an output |
| **07. Non-planar head - Ginger Additive · Ginger One (Klipper)** | The head printed in 2024 (UNIKAT HEAD V2, the model of that print, built into the plug-in) sliced along its own surface as it was printed then; every lap dips about 16 to 18 mm under the chin: own `Print Settings` (bead 4, layer 1.75, 20 mm/s, Overlap 0, Min Speed 0.2 mm/s, first layer 0.9 mm at 25 mm/s), `Slice` Guide Mode 3 "Follow the surface" with the nozzle vertical, `Walls`, `Continuous Path` Spiral (Min Layer Time 30 s, Max Volume Rate 330), `Start and End`, `Path Check`, `Non-Planar Check`, `Machine` (Ginger One Klipper) → `Machine G-code` → `Printer Simulation` (Play). Extrusion by the shell law (vertical gap to the lap below, skin where the laps step sideways; `Skin Thickness` 0 = layer height) | Ginger One (Klipper) |
| **08. Surface pattern - a vase with waves** | `Surface Pattern` between `Continuous Path` and `Start and End`: Waves, amplitude 2.5 mm (half a bead), pitch 30 mm, phase shift 0.05 (slow diagonal) | any |

Grasshopper 2 has its own Quick Start (`LARGERslicer.GH2/QuickStart.cs`) with the same sections
00-08; values are set on the inputs (Grasshopper 2 has no rows on the components). Print Settings
reaches every example through a Shout "Settings" and a Listen of that name, and every finished path
goes back to Print Bed through a Shout "Finished path", so no wire crosses the canvas. From 2.0.5 every slicer
example has a `Control Panel` and a `Result` beside it in both versions, and Output A uses `Machine` + `Machine G-code`.

---

## 2. Example definitions (`EXAMPLE FILES/`)

### Current (made with the Slicer and Robot tabs, checked on the canvas)

| File | Shows | Machine |
|---|---|---|
| `LARGERslicer - Beispiele für Studierende.gh` | **Start here (German, for students)**: seven explained examples, each in its own group with a Control Panel (settings) and a Result (check, time, material, save): 01 a vase with `Easy Slicer` alone (spiral) → `Ginger G-code`; 02 a box with a closed top (`Easy Slicer` adds a grid below the top); 03 an ornament only where a closed curve is drawn in front of the wall (`Surface Pattern` Region with Falloff); 04 an ornament around an attractor point (Reach); 05 cooling by layer for the Weber DXR (`Machine Settings` → `Fan Schedule`); 06 a clay vase on the UR5 on the Robot chain (`Robot` UR5, `Robot Tool` clay extruder V2, `Robot Cell`, `Robot Program` → URScript, `Robot Simulation`; up to 2.0.3 `UR Cell`, `UR Program`, `UR Simulation`, which are hidden now); 07 milling on the Zünd (topography in a plate, rough, finish, cut out, PLT). Also in `SETUP`. Full names saved in the file | Ginger One, Weber DXR25, UR5, Zünd G3 |
| `LARGERslicer - Ginger One Printer Simulation.gh` | A planter: `Slice`, `Walls`, `Bottom Fill`, `Continuous Path`, `Start and End`, `Path Check`, `Machine` → `Machine G-code` → `Printer Simulation` with Play and the collision check | Ginger One (Marlin) |
| `LARGERslicer - Surface Patterns.gh` (+ `LARGERslicer - Surface Patterns lattice.png`) | Six vases on one bed: knit, ribs, diagonal waves, hanging loops, a lattice picture (the PNG next to the file), a region drawn on the part; each with `Start and End` and `Path Check` (0 crossings) | any |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One Marlin, non-planar).gh` | **The reference print rebuilt**: the 2024 head (`Head.3dm`) placed as printed, Guide Mode "Follow the surface", one wall centred, spiral, brim 7, `Path Check`, `Non-Planar Check`, G-code, `Material Factor` for calibration, `Printer Simulation`, panels "Where the numbers come from" (what was taken from the 2024 print, and why its extrusion was not: it over-extruded the leaning laps; since 1.3.1 the shell law). See [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) 9.3 and 9.8 | Ginger One (Marlin) |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Robot, DXR25).gh` | The same head on the robot: `Cell Geometry`, `Robot`, `Robot Tool`, `Robot Cell`, `Robot Program`, `Robot Simulation`, `Machine Settings`, `DXR Generator`, report panels | KUKA KR120 with Weber DXR25 (AE20 extruder), FH Muenster cell |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Curved Wall, flat top (Robot, DXR25).gh` | **Pair for students, part 1**: a curved hollow wall element at 1:5 (760 x 610 x 846 mm, two skins 48 mm apart), flat layers, one spiral, bead 5 x 2 mm, 423 layers, about 8.9 kg and 7.2 h; `Path Check`, `Robot Program` (reach and collisions OK), `DXR Generator` | KUKA KR120 with Weber DXR25, FH Muenster cell |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Curved Wall, sloped top, non-planar (Robot, DXR25).gh` | **Pair for students, part 2**: the same kind of wall with a sloped, wavy top (715 to 843 mm). A guide surface through the top (Patch, extended past the part), Guide Mode "Flat bottom, top follows guide", Tool Axis "Square to the layer": the layers fan out, the top layer spans 110 mm in height, the nozzle tilts up to 30 degrees; 426 layers, about 8.5 kg and 7.4 h, reach and collisions OK | KUKA KR120 with Weber DXR25, FH Muenster cell |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One Klipper, non-planar).gh` | The head of 2024 (UNIKAT HEAD V2) sliced along its surface (Follow the surface), one spiral, `Path Check`, `Non-Planar Check`, `Machine G-code`, `Printer Simulation`; like Quick Start 07 with 4 mm bead and 2 mm layers (up to 1.3.0 a placeholder part that bulged to one side, with "Follow the part": its laps were nearly flat) | Ginger One (Klipper) |
| `Machines/Template Gantry.machine` + `Template Gantry.3dm` | Template of an own machine: a 300 x 300 mm gantry with frame, bed, Z rails, beam and head on the layers Frame, Bed, Z, Y, X, TCP | own gantry |
| `Machines/Template Bed Slinger.machine` | Template of a bed slinger without a 3D model (220 x 220 x 250, filament 1.75) | own bed slinger |
| `Machines/Template Pellet Gantry.machine` | Template of a pellet gantry without a 3D model | own pellet gantry |
| `02 - STRUCTURE - Weave and Reshape/Weave (beads that interlock).gh`, `Reshape (print height where it is needed).gh` | `Weave Zones` → `Weave` (beads of neighbouring walls that grip into each other) and `Shape Field` → `Reshape Path` (more print height where the part needs it); guide next to them | any |
| `00 - ZUEND - 3D CNC MILLING/Zünd Rough and Finish (height model).gh` | `CNC - Plate`, `CNC - Cutter` (2x) → `CNC - Rough and Finish` → PLT: topography roughed, finished, cut out; the canvas of `CNC - Quick Start`, built by `tests/ExampleBuilders/ZundRoughFinish.csx` | Zünd G3 L-2500 |

Guide for own machines: [OWN_MACHINES.md](OWN_MACHINES.md).

### House definition of the FH Muenster and other files

| File | Shows | Machine |
|---|---|---|
| `SETUP/LARGERslicer Weber Robot and Ginger V2.gh` | **The house definition, version 2** (needs LARGER.slicer 2.0 or newer): the same three workflows with the current components and nothing but LARGER.slicer. 01 Easy Ginger and Robot Slicer (one set of sliders, row A with the Ginger profile to `Ginger G-code` (hidden from 2.0.5 but still in this file; `Machine` + `Machine G-code` does the same), row B with the DXR25 profile to the robot), 02 Advanced Multiaxial Robot Slicer (slice angle, optional guide; a multi-axis example part and guide curve lie ready), 03 Custom Robot Path Creator (own curves, lifted by the layer height, `Curves to Layers`). Each robot part: `Cell Geometry`, `Robot`, `Robot Tool` (AE20), `Robot Cell`, `Robot Program`, `Robot Simulation`, `Machine Settings`, `DXR Generator`, `DXR File Health Check` on the code. 0 errors and 0 warnings in Rhino 8 and 9; 94 KB instead of 17 MB | Weber DXR25 (KUKA), Ginger One |
| `00 - ZUEND - 3D CNC MILLING/Schalungen Skript.gh` | Formwork (Schalungen) script, native Grasshopper | Zünd |
| `LARGERslicer/Plugin Installation Files/LARGERSlicerDXRConverter.gh` (+ `.3dm`) | Converter definition shipped with the manual installation: Machine Settings, DXR Generator | Weber DXR25 |

### Archive (`EXAMPLE FILES/99 - ARCHIVE/`, kept for reference, not maintained)

| File | Note |
|---|---|
| `LARGERslicer Weber Robot and Ginger V1 (Robots plug-in).gh` | The first house definition (was in `SETUP`): 01 Easy Ginger and Robot Slicer, the multiaxial path creator, 03 Custom Robot Path Creator; GCODE MAKER (Ginger), DXR Generator, Machine Settings, DXR File Health Check, DXR Variable Width; robot code from the Robots plug-in, needs Robots and Pufferfish |
| `UR5slicer (Robots plug-in, before 2.0.3).gh` | Robot clay printing and movement (Fabio Koczula, Torben Kurzhals, FH Muenster 2025), Robots plug-in. Today: the Robot chain, example 06 of the student file |
| `Zünd HPGL PLT MSA (CNC - Program, old).gh` | `CNC - Cutter Selector` → `CNC - Program` → PLT for the Zünd flatbed (both components hidden from 2.0.5); its `.3dm` stays in `00 - ZUEND - 3D CNC MILLING` |
| `LARGERslicer Multiaxial Weber Robot (1.0, old).gh` | Non-planar large-format algorithm (Claudio Schröder, Moritz Wesseler, 2025), superseded by Slice with a guide |
| `Weber and Ginger - old states of the house definition/` | `ARCHIV - LARGERslicer Weber Robot and Ginger.gh`, `... WIP.gh`, `... WIP CHAOS.gh` (3D print path maker, GCODE MAKER, DXR code maker, overhang analyser, robot previews) and `Bottom line Tests.gh` (Align Curves, Alternate Curves, Join Open Contours for bottom lines) |
| `Weber DXR20 robot system simulation (Robots plug-in)/` | The robot system of the cell as geometry, Klipper G-code panels (+ `.3dm`, `README_How_to_open.md`) |
| `WIP scripts (no LARGER components)/` | Native-Grasshopper experiments with Ginger start/end code (Easy Spirals, Paper Walls, Non Planar) and `UR5slicer - 45 Degree.gh` (UR5 at 45°) |

### Machine guides next to the examples

- Weber DXR25: `00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md` and `... GERMAN.md`
- Ginger One: `00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md` (also the source of the
  Ginger start/end code, material tables and the E-value multipliers)
- UR5 and Zünd: the guides in their folders
- Manufacturer manuals were removed from the repository for copyright reasons ([DECISIONS.md](DECISIONS.md)).

---

## 3. Test files elsewhere in the repository

`LARGERslicer/examples/` holds older material: a sample DXR file
(`DXR_2512031344_Home_1.21kg_24min.dxr`), `dxr testcode.dxr`, `Example.gcode`, reference C# of the
first DXR postprocessor (excluded from the build) and screenshots.
