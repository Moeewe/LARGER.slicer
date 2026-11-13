# Examples

Every example file in the repository and every section of Quick Start: what it shows and for which
machine. Part of the [developer handbook](README.md).

Component lists below were read from the files themselves (Grasshopper archives) and from the
Quick Start code; "WIP" and "OUTDATED" files are kept for reference and are not maintained.

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
| **01. Flat layers - from the part to the file** | The normal chain on a curved hull, 60 mm: `Slice` → `Walls` → `Bottom Fill` → `Infill Ribs` (every 150 mm, meeting in the middle) → `Continuous Path` → `Round Corners` → `Start and End` → `Extrusion and Speed`, `Path Check`. Outputs: **A** `Ginger G-code` (Save G-code); **B** the FH Muenster robot cell (`Cell Geometry`, `Robot`, `Robot Tool`, `Robot Cell`, `Robot Program`, `Machine Settings`, `DXR Generator`, `Robot Simulation` with a slider and Play); **C** `Desktop Test Print` | Ginger One (G-code), Weber DXR25 on the KUKA KR120 (DXR), a desktop printer |
| **02. Non-planar layers** | A round hull whose top follows a saddle surface; `Slice` with the guide surface, Guide Mode 0 (flat bottom, top follows the guide) | any machine; tool square to the layer by default |
| **03. Multi-axis layers** | A part that grows along a bent axis; `Slice` with a guide curve (layers square to it), plus `Robot Program` and `Robot Simulation` where the tool leans with the layers | robot |
| **04. Part with an opening** | `Infill Wave` (wavelength 60 mm) joins the wall of the opening and the outer wall into one line; try Shape 1 (Truss) or 2 (Meander) | any |
| **05. Angled layers** | A block leaning 55°, more than flat layers can overhang; `Slice` with Angle 45, every bead rests on the one below | robot or tilted bed |
| **06. Easy Slicer - one component** | A vase printed as a spiral by `Easy Slicer` alone (Ginger profile, nozzle preset "3 mm flat nozzle, wide bead (tested)"), then `Path Check` | any; connect Toolpath to an output |
| **07. Non-planar head - Ginger Additive · Ginger One (Klipper)** | A head sliced along its own surface as the head of 2024: own `Print Settings` (bead 4, layer 1.75, 20 mm/s, Overlap 0, Min Speed 0.2 mm/s, first layer 0.9 mm at 25 mm/s), `Slice` Guide Mode 3 "Follow the surface" with the nozzle vertical, `Walls`, `Continuous Path` Spiral (Min Layer Time 30 s, Max Volume Rate 330), `Start and End`, `Path Check`, `Non-Planar Check`, `Machine` (Ginger One Klipper) → `Machine G-code` → `Printer Simulation` (Play) | Ginger One (Klipper) |
| **08. Surface pattern - a vase with waves** | `Surface Pattern` between `Continuous Path` and `Start and End`: Waves, amplitude 2.5 mm (half a bead), pitch 30 mm, phase shift 0.05 (slow diagonal) | any |

Grasshopper 2 has its own Quick Start (`LARGERslicer.GH2/QuickStart.cs`) with the same sections
00-08; values are set on the inputs (Grasshopper 2 has no rows on the components) and the
settings are wired directly (no wireless relays).

---

## 2. Example definitions (`EXAMPLE FILES/`)

### Current (made with the Slicer and Robot tabs, checked on the canvas)

| File | Shows | Machine |
|---|---|---|
| `LARGERslicer - Ginger One Printer Simulation.gh` | A planter: `Slice`, `Walls`, `Bottom Fill`, `Continuous Path`, `Start and End`, `Path Check`, `Machine` → `Machine G-code` → `Printer Simulation` with Play and the collision check | Ginger One (Marlin) |
| `LARGERslicer - Surface Patterns.gh` (+ `LARGERslicer - Surface Patterns lattice.png`) | Six vases on one bed: knit, ribs, diagonal waves, hanging loops, a lattice picture (the PNG next to the file), a region drawn on the part; each with `Start and End` and `Path Check` (0 crossings) | any |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh` | **The reference print rebuilt**: the 2024 head (`Head.3dm`) placed as printed, Guide Mode "Follow the surface", one wall centred, spiral, brim 7, `Path Check`, `Non-Planar Check`, G-code, `Material Factor` for calibration, `Printer Simulation`, panels "Where the numbers come from". See [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) 9.8 | Ginger One (Marlin) |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Robot, DXR25).gh` | The same head on the robot: `Cell Geometry`, `Robot`, `Robot Tool`, `Robot Cell`, `Robot Program`, `Robot Simulation`, `Machine Settings`, `DXR Generator`, report panels | KUKA KR120 with Weber DXR25 (AE20 extruder), FH Muenster cell |
| `00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Non Planar Head (Ginger).gh` | Layers that follow the part, one spiral, `Path Check`, `Non-Planar Check`, `Machine G-code`, `Printer Simulation`; like Quick Start 07 | Ginger One (Klipper) |
| `Machines/My Gantry.machine` + `My Gantry.3dm` | Template of an own machine: a 300 x 300 mm gantry with frame, bed, Z rails, beam and head on the layers Frame, Bed, Z, Y, X, TCP | own gantry |
| `Machines/My Bed Slinger.machine` | Template of a bed slinger without a 3D model (220 x 220 x 250, filament 1.75) | own bed slinger |

Guide for own machines: [OWN_MACHINES.md](OWN_MACHINES.md).

### Machine definitions of the FH Muenster (older component set)

| File | Shows | Machine |
|---|---|---|
| `SETUP/LARGERslicer Weber Robot and Ginger.gh` | The house definition, three workflows: 01 Easy Ginger and Robot Slicer, the multiaxial path creator, 03 Custom Robot Path Creator; GCODE MAKER (Ginger), DXR Generator, Machine Settings, DXR File Health Check, DXR Variable Width; robot code from the Robots plug-in. Usage: README "Usage Guide" | Weber DXR25 (KUKA), Ginger One |
| `00 - UNIVERSAL ROBOTS - UR5/UR5slicer.gh` | Robot clay printing and movement (Fabio Koczula, Torben Kurzhals, FH Muenster 2025), Robots plug-in; guide next to it | UR5 |
| `00 - ZUENDT - 3D CNC MILLING/ZündHGPL PLT MSA.gh` (+ `.3dm`) | `CNC - Cutter Selector` → `CNC - Program` → PLT for the Zünd flatbed; guide next to it | Zünd G3 L-2500 |
| `00 - ZUENDT - 3D CNC MILLING/Schalungen Skript.gh` | Formwork (Schalungen) script, native Grasshopper | Zünd |
| `00 - WEBER - DXR25 - README/WIP Grasshopper Simulation/Weber DXR20 FH Münster Robot System.gh` (+ `.3dm`, `README_How_to_open.md`) | The robot system of the cell as geometry, Klipper G-code panels; work in progress | Weber DXR / cell |
| `LARGERslicer/Plugin Installation Files/LARGERSlicerDXRConverter.gh` (+ `.3dm`) | Converter definition shipped with the manual installation: Machine Settings, DXR Generator | Weber DXR25 |

### Archive, WIP and outdated

| File | Note |
|---|---|
| `00 - WEBER - GINGER [Robotic] 3D Printing/00 - Archiv & WIP/ARCHIV - LARGERslicer Weber Robot and Ginger.gh`, `... WIP.gh`, `... WIP CHAOS.gh` | Earlier states of the house definition (3D print path maker, GCODE MAKER, DXR code maker, overhang analyser, robot previews) |
| `00 - Archiv & WIP/Bottom line Tests.gh` | Tests of Align Curves, Alternate Curves, Join Open Contours for bottom lines |
| `OUTDATED - LARGERslicer Multiaxial Weber Robot.gh` | Non-planar large-format algorithm (Claudio Schröder, Moritz Wesseler, 2025), superseded by Slice with a guide |
| `01 - WIP - PRINTER GH SCRIPTS - NEW FEATURES/LARGERslicer/LARGERslicer - Easy Spirals.gh`, `- Paper Walls.gh`, `- Non Planar.gh` | Native-Grasshopper experiments with Ginger start/end code (spirals, thin walls, non-planar with Klipper G-code) |
| `01 - WIP - PRINTER GH SCRIPTS - NEW FEATURES/UR5slicer WIP 45°/UR5slicer - 45 Degree.gh` | UR5 at 45°, work in progress |

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
