<div align="center">

<a href="https://parametricsolutions.de/larger-slicer/"><img alt="LARGER.slicer: one continuous line for large-format 3D printing" src="docs/social-preview.png" width="100%"></a>

# LARGER.slicer

**Model in. One line. Print.**<br>
The slicer for large-format 3D printing with robots and pellet extruders, in Rhino and Grasshopper.

[![Latest release](https://img.shields.io/github/v/release/Moeewe/LARGER.slicer?label=release&color=0a84ff)](https://github.com/Moeewe/LARGER.slicer/releases/latest) [![Rhino](https://img.shields.io/badge/Rhino-9%20%7C%208%20%7C%207-14203a)](https://www.rhino3d.com/) [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-14203a)](#quick-start) [![License: CC BY-NC 4.0](https://img.shields.io/badge/license-CC%20BY--NC%204.0-lightgrey)](https://creativecommons.org/licenses/by-nc/4.0/)

<a href="https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip"><img alt="Download LARGERslicer-SETUP.zip" src="https://img.shields.io/badge/Download-LARGERslicer--SETUP.zip-0a84ff?style=for-the-badge&logo=rhinoceros&logoColor=white"></a>

[Product page](https://parametricsolutions.de/larger-slicer/) · [Release notes](https://github.com/Moeewe/LARGER.slicer/releases/latest) · [Documentation](LARGERslicer/documentations/README.md) · [Changelog](LARGERslicer/CHANGELOG.md)

**English** · [Deutsch](README.de.md)

</div>

---

## One line, from skirt to last layer

A pellet extruder cannot stop cleanly: every travel move pulls strings and every halt leaves a blob. LARGER.slicer therefore plans the whole print as **one continuous path** and checks it before it reaches the robot.

| | |
|---|---|
| **One line** | Walls, bottom and infill are joined into a single path. Infill is spliced into the wall, not printed beside it. |
| **Never crosses itself** | A crossing means the nozzle ploughs through fresh material. If a pattern cannot be printed without one, the slicer picks another. |
| **Flat layers** | Every layer lies flat. The seam stays open by one bead width; the path crosses the gap on a short incline to the next layer, always in the same place. |
| **Supported** | A bead may overhang the one below by half a bead width at most. `Path Check` reports where a part exceeds that. |

## Infill from one line

Every drawing is a real layer from the slicer (part 300 × 190 mm, bead 5 mm). Size, stretch, angle and position are adjustable.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme-infill-dark.png">
  <img alt="Ten infill patterns as single continuous toolpaths: ribs, meeting ribs, honeycomb, stretched honeycomb, grid, wave, truss, meander, chamber wall, truss wall" src="docs/readme-infill-light.png" width="100%">
</picture>

**Draw your own infill:** sketch curves over the part in top view and `Infill Sketch` turns each one into a rib that follows it from wall to wall, as part of the same line.

## Bottoms without stopping

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme-bottom-dark.png">
  <img alt="Four bottom fills as single continuous toolpaths: zigzag, zigzag at 45 degrees, contour, contour around a hole" src="docs/readme-bottom-light.png" width="100%">
</picture>

## The Slicer tab

New here? `Easy Slicer` is one component from the model to the checked print path; the [first-steps page](https://parametricsolutions.de/larger-slicer/erste-schritte/) walks through it. For every option, components connect in this order; `Quick Start` places the whole chain with example parts.

Settings sit on the components themselves: the rows under a component are choices, numbers, switches and buttons (`Save G-code`, `Save KRL`, `Play`). Click a row to change it: a choice opens a menu. A number is dragged left or right (Shift = finer; the bar shows where it sits in its range), clicked to type any value, or set from the arrow on its right with a few round values (angles 0, 15, 30, 45, 60, 75, 90°). A button writes its file once per click and switches back by itself. The value goes into the input as if typed into it, so the file keeps it, and a wire into the same input still wins (the row then says "connected").

**Long calculations run in the background.** `Slice`, `Walls`, `Continuous Path`, `Surface Pattern`, `Material Field`, `Self Support Check`, `Non-Planar Check` and `Robot Program` calculate beside the canvas, which stays usable; the component shows "computing 3.2 s" and hands out its result when it is done. Changing an input meanwhile starts over, and the running calculation stops within about a layer. Components below one that still computes show a grey "waiting for ... (still computing)" instead of an error. The row `Background: on` (or the menu item "Calculate in the background") switches this off per component; the file keeps the setting.

**Speeds are in mm/s.** `Print Settings` and `Easy Slicer` take Speed 50, Min Speed 5 and Max Speed 200 mm/s by default. The path and the machine files keep mm/min, the F of G-code and DXR: 50 mm/s = F3000. Definitions saved with an older version are read as mm/min, with a note on the component.

The tab has seven groups, in the order you connect them (the dropdown of the Slicer panel shows them separated by lines):

| Group | Component | What it does |
|---|---|---|
| **1 Start** | `Quick Start` · `Easy Slicer` | The whole chain with example parts; or one component from the model to the checked print path |
| | `Print Settings` | Bead width, layer height, overlap, speed (mm/s), minimum radius. `Nozzle` picks bead width and layer height for the nozzle that is screwed in |
| | `Machine Profile` | How your printer wants its extrusion values: volume, filament length, or a factor for the controller. Listed as manufacturer and machine, e.g. "Generic · Pellet extruder" |
| | `Material` · `Material Factor` | Start values for a pellet material (temperatures, drying, density); a new extrusion factor from measured bead widths |
| | `Design Rules` · `Print Bed` | Before you model: layer height range, overlap, wall thickness for two beads; the bed of your machine and whether the paths lie on it |
| **2 Layers** | `Slice` | Breps, surfaces, SubDs, meshes. Flat, tilted, non-planar (guide surface, "Follow the part" or "Follow the surface") or multi-axis (guide curve) |
| | `Best Angle` · `Self Support Check` · `Self Supporting Envelope` | The slice angle with the least overhang, where the part hangs over too far, and the least material that makes it self-supporting |
| **3 Walls and fill** | `Walls` | Bead inside, centred on or outside the geometry |
| | `Bottom Fill` | Closed bottom as one line: zigzag or contour |
| | `Infill Ribs` · `Infill Sketch` · `Infill Cells` · `Infill Wave` · `Infill Chambers` | Infill spliced into the wall path |
| | `Support Fins` | Thin fins outside the part that hold up overhangs, as part of the same line |
| **4 Path** | `Continuous Path` | Flat layers with an open seam (gap of one bead width), or a spiral for single walls. Seam Mode, Min Layer Time, Short Lap Time, Max Volume Rate |
| | `Round Corners` | Every corner becomes an arc (Min Radius 0 = half the bead width, at least half the nozzle) |
| | `Start and End` | Way in from 30 mm above, skirt first, brim outside, inside or both, a move off the part at the end |
| | `Surface Pattern` | Textures in the wall: knit, pixels, ribs, waves, zigzag, dots, hanging loops or a picture, on the whole part or inside a region you draw |
| **5 Check** | `Path Check` | Support, self-crossings, bead shape, travel moves, layer times |
| | `Non-Planar Check` | Bead height and head clearance for non-planar layers with a vertical nozzle |
| | `Extrusion and Speed` | Plane, E and F per point for the DXR Generator or G-code |
| | `Material Field` | Puts the toolpath back onto the part as a direction-dependent material field for FE or IGA |
| **6 Output** | `Machine` · `Machine G-code` | Built-in G-code machines (e.g. "Ginger Additive · Ginger One (Klipper)") or your own: start, layer and end code and E factor as a `.machine` file in Documents/LARGER/Machines |
| | `Ginger G-code` | The path in full size as G-code for the Ginger One, with its start and end sequence |
| | `Desktop Test Print` | The same path scaled down as G-code for a desktop filament printer |
| | `Printer Simulation` | The print head running the path on the machine, with a slider or Play |
| | `Toolpath to Robot Lines` | PTP lines for the DXR Generator (Weber DXR) |
| | `Import G-code` | Reads G-code from other slicers or a .dxr file as a toolpath, to check it with `Path Check` |
| **7 Curve tools** | `Layers to Curves` · `Curves to Layers` · `Curve Fill Zigzag` · `Curve Fill Contour` · `Curve Fill Cells` · `Curve Fill Euler` · `Curve Fill Cross Lines` · `Curve Continuous Infill` · `Curve Wall and Zigzag` · `Curve Continuous Path` · `Curve Join and Order` · `Curve Bridge` · `Curve Uncross` | For your own curves instead of a model: layers as curves and back, one outline filled as one line, curves joined into one path without crossings. `Align Curves`, `Alternate Curves` and `Join Open Contours` are in this group too but hidden from the panel: find them by searching (double-click the canvas) |

`Quick Start` places eight examples: 01 flat layers, 02 non-planar, 03 multi-axis, 04 a part with an opening, 05 angled layers, 06 `Easy Slicer` (a vase as a spiral), 07 a non-planar head on the Ginger One (Klipper), sliced along its own surface ("Follow the surface"), with Printer Simulation, and 08 a vase with `Surface Pattern`.

### New components in detail

<details>
<summary><b>Material, Material Factor, Import G-code, Material Field</b></summary>

**`Material`** (Slicer). Start values for a pellet material: feed zone, heat zones 1 to 3, nozzle and bed in °C, drying, residual moisture and density in g/cm³. Built in: Carbon-P PETG, rPETG and PLA, with the start values of the Weber DXR quick guide; the densities are typical values.
- Inputs: `Material` (a choice on the component), optional values that override the chosen material (zones as one value or a range such as 200-210), `Factor`, and the `Save material` button.
- Wiring: `Density` → `Density` of `Print Settings`, so file names and reports show the right weight.
- Own materials: `.material` text files in Documents/LARGER/Materials, or fill in the inputs and press `Save material`.
- Watch out: the built-in temperatures are start values. Check them against the data sheet of your material.

**`Material Factor`** (Slicer). Finds a new extrusion factor from a test print: new = target width / measured width × current factor.
- Inputs: `Target Width` (mm, default 5), `Measured Widths` (mm, several values: the mean is used), `Current Factor` (default 1), optional `Wall Thickness` (mm) of a single-wall test.
- Outputs: `New Factor`, `Off` (% of the target, + = too wide), `Mean`, `Report`.
- Wiring: use `New Factor` as `Factor` of `Machine Profile`, or as P1 on the Weber DXR, and print the test again.
- Watch out: measure at several places. The report warns when the measurements are uneven.

**`Import G-code`** (Slicer). Reads G-code from Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build or a script, or a Weber `.dxr`, and checks it like a path of its own: starts and stops, travel moves, retractions, F changes and F0, extrusion into the bed, unsupported beads, crossings, time and weight. Leaning layers and A/B/C tool axes are recognised.
- Inputs: `File`, `Bead Width` and `Layer Height` (mm, 0 = estimated from the file), optional `Machine Profile` (what E means; empty = E is a volume in mm³), `Density` (g/cm³, default 1.25), and the `Keep start code` switch.
- Outputs: `Toolpath`, `Path`, `Layers`, `Report`.
- Wiring: `Toolpath` → `Path Check`, `Robot Program` or the DXR components.
- Watch out: if E is a filament length or carries a factor, connect the matching `Machine Profile`, otherwise the bead widths come out wrong.

**`Material Field`** (Slicer). Puts the toolpath back onto the part as a direction-dependent material field for FE or IGA. Per sample point: bead direction e1, across the bead e2, stacking direction e3, layer and bead, distance to the next layer and bead interface, layer time and a weakness value. With the moduli it gives the rotated orthotropic stiffness per point.
- Inputs: `Toolpath`, optional `Geometry` (Breps: samples are a grid in each face's UV, so face, u, v map onto the NURBS of the analysis) or `Sample Points` (e.g. Gauss points), `Sample Spacing` (mm, 0 = half the layer height), moduli E1, E2, E3, G12, G13 (MPa) and Poisson ratios, `Cooling Time` (s, default 60), `File`, `Format` (CSV, JSON, VTK for ParaView) and the `Save` button.
- Outputs: `Points`, `Frames`, `Directions`, `Stacking`, `Weakness` (0 = sound, 1 = weak), `Weakness Mesh`, `Table`, `Info`.
- Watch out: the default moduli and the cooling time are placeholders. Replace them with values from coupon tests of your material.

</details>

<details>
<summary><b>Self Support Check, Self Supporting Envelope, Best Angle, Support Fins</b></summary>

The overhang limit comes from the bead: a bead may lie `Factor` × bead width beside the bead below (default 0.5). With 5 mm beads in 2 mm layers a wall may lean about 51° from the build direction.

**`Self Support Check`** (Slicer, next to `Slice`). Colours the part by how far it hangs over for the chosen build direction: green fine, orange near the limit, red not self-supporting, grey on the bed.
- Inputs: `Geometry`, `Settings` (from `Print Settings`), `Plane`, `Angle` (degrees), optional `Guide` and `Guide Mode`, all as on `Slice`, and `Factor`.
- Outputs: `Analysis Mesh`, `Unsupported Area` (mm²), `Percent`, `Regions`, `Starts` (islands that begin in mid air), `Report`.
- Watch out: use the same plane, angle and guide as on `Slice`, otherwise it checks another build direction.

**`Self Supporting Envelope`** (Slicer). Adds the least material that makes every layer hold the one above, like automatic chamfers under overhangs.
- Inputs: `Layers` from `Slice` (before `Walls`), `Settings`, `Factor`, `Geometry` switch (also build the new part as geometry).
- Outputs: `Layers` (slice-ready: connect to `Walls`), `Geometry` (the new part as a closed NURBS Brep or a mesh), `Added` (only the added material), `Contours`, `Added Contours`, `Report`.
- Watch out: the added material changes the part. Plan to mill it away, and building the geometry (a loft) takes a moment.

**`Best Angle`** (Slicer). Tries slice angles in 5° steps and, with `Turn`, every tilt direction in 30° steps, and suggests the one with the least overhang.
- Inputs: `Geometry`, `Settings`, `Min Angle` and `Max Angle` (degrees, default 0 to 75), `Turn`, `Factor`.
- Outputs: `Plane` and `Angle` (connect both to `Slice`), `Layer Plane`, `Report` with the ranking.
- Watch out: an angled print needs a robot or a tilted bed, and usually start and end layers (`Slice`, `Start/End Layers`).

**`Support Fins`** (Slicer, next to Infill). Thin fins outside the part hold up overhangs. Each fin stands square to the outer wall from the bed up to just under the overhang, as a hairpin out of the wall and back, so it stays part of the one line.
- Inputs: `Layers` (after `Walls`, `Bottom Fill` and infill), `Settings`, `Overhang Angle` (degrees from vertical, 0 = half a bead per layer), `Spacing` (bead widths, default 15), `Reach` (bead widths, default 1), `Contact` (Neck, Neck + gap, Full), `Gap` (mm, default 0.3, for Neck + gap).
- Wiring: right before `Continuous Path`. The seam ramp keeps off the fins.
- Watch out: "Full" is the strongest contact but the hardest to remove. "Neck + gap" prints the top fin layer lower so the overhang breaks off.

</details>

<details>
<summary><b>Surface Pattern: knit, ribs, waves, loops, pictures</b></summary>

**`Surface Pattern`** (Slicer, Toolpaths group, after `Continuous Path`, before `Start and End`; works on any toolpath). Every layer is a row of points. The points are pulled out of the wall along its normal in a rhythm, and the rhythm moves on from layer to layer. Small pulls give a fine knit or pixels; large ones let the bead hang over between the points that rest on the layer below, and with `Z Droop` and `Extra Flow` it sags into loops.
- Inputs: `Toolpath`, `Pattern` (Knit, Ribs, Waves, Zigzag, Dots, Loops, Image), `Amplitude` (mm, how far out), `Pitch` (mm, one repeat along the layer; fitted so a whole number of repeats goes round a closed wall), `Layer Period` (layers per row), `Phase Shift` (fraction of the pitch per row: 0 stacked, 0.5 woven, 0.05 diagonal), `Z Droop` (mm), `Extra Flow` (fraction), `Side` (Outside or Inside), `Region` (closed curves drawn on the part, Breps or meshes) and `Falloff` (mm, soft edge), `Attractors` (points or curves) and `Reach` (mm), `Image` (file path of a grey picture, dark = pattern; a bare file name is looked for next to the .gh file) and `Invert`.
- Outputs: `Toolpath`, `Preview` (one polyline per layer), `Hanging` (points more than half a bead beside the layer below), `Info`.
- Regions: a closed curve is unrolled with the wall (arc length across, height up); a path point gets the pattern if it lies inside the curve there. A closed Brep or mesh: points inside it. An open surface lying on the wall: points on it.
- The path stays one line: only wall points move, seam ramps, bridges, ribs and infill stay, the pattern fades out at the seam, and where a pull would run across the path it is reduced. The first layer stays plain.
- Watch out: up to half the bead width every bead rests on the bead below. More is allowed on purpose (pixels, loops); `Info` counts those points and `Path Check` lists them as unsupported, so judge them by eye. The extrusion follows the longer path by itself; `Extra Flow` adds more at the peaks, and `Max Volume Rate` in `Continuous Path` still limits the extruder.
- Example: `EXAMPLE FILES/LARGERslicer - Surface Patterns.gh` (six vases: knit, ribs, waves, loops, a lattice picture, a region).

| Look | Pattern | Amplitude | Pitch | Phase Shift | Layer Period | Z Droop / Extra Flow |
|---|---|---|---|---|---|---|
| Fine knit, pixels | Knit | 0.4 to 0.6 x bead | 2 x bead | 0 | 1 | 0 / 0 |
| Vertical ribs | Ribs | 0.5 to 1 x bead | 3 x bead | 0 | 1 | 0 / 0 |
| Deep sine walls (stacked) | Waves | 2 to 4 x bead | 6 to 10 x bead | 0 | 1 | 0 / 0 |
| Woven waves, layers shifted | Waves | 1.5 to 2 x bead | 6 x bead | 0.5 | 1 | 0 / 0 |
| Diagonal bands | Waves or Knit | 1 x bead | 3 to 6 x bead | 0.03 to 0.08 | 1 | 0 / 0 |
| Scales, bumps | Dots | 0.8 x bead | 3 x bead | 0 | 2 | 0 / 0 |
| Hanging loops | Loops | 2 to 4 x bead | 4 to 5 x bead | 0.5 | 2 | 1 layer / 0.3 |
| Picture or lattice | Image + picture | 0.6 x bead | 1.5 x bead | 0 | 1 | 0 / 0 |
| Texture only in a field | any + Region curve | | | | | |

</details>

<details>
<summary><b>Slice "Follow the part" and "Follow the surface", Non-Planar Check, Continuous Path and Path Check options</b></summary>

**`Slice`, Guide Mode "Follow the part (no guide)".** Leave `Guide` empty. The non-planar layers follow the part's own wall from bottom to top, so overhangs get layers of even thickness without a guide surface. The nozzle stays vertical, and the extrusion follows the height above the closest point of the lap below, only its vertical part (where the wall turns nearly flat: the sideways step x `Skin Thickness` / bead width, see below). A warning appears when a layer gets thicker than 0.8 × bead width along the wall. If the part does not suit it, `Slice` says so and slices flat.

**`Slice`, Guide Mode "Follow the surface (no guide)".** Leave `Guide` empty and use `Continuous Path` in Spiral mode. Made for closed parts such as a head, the way the head "UNIKAT HEAD V2" was printed on the Ginger One in 2024: rings on the part's own surface from the ring it stands on to the crown (the way along the surface, a third pulled towards the level of the height on the wall), under chin and nose as well, closing the top. The first two layers lie flat (first layer height, then one layer height higher). The nozzle stays vertical. The bead height of every point follows the **shell law**, measured from the closest point of the ring below: a wall one bead wide gains bead width x vertical gap per ring, however far the ring leans (a sideways offset moves material, it adds none), and where the rings step mostly sideways (crown, under the chin) they form a skin: t = max(dz, dxy x Skin Thickness / w), and E follows it. (Up to 1.3.0 the law of the 2024 print was used, t = dz + (h - dz) x dxy / w; it over-extruded the leaning rings, by up to about 50 % per ring towards the crown.) Non-Planar Check measures by the shell law, accepts no bead above 0.8 x bead width, up to 1 % of the points below 0.4 x layer height (crown), and names E made with the old law. Path Check accepts rings up to one bead width beside the ring below at 2.5 % of the points only in short stretches (at most 12 bead widths, held like a short bridge); nozzle grazes up to 0.6 mm are reported, not counted. Example: [`LARGERslicer - Head (Ginger One, non-planar).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh>).

**`Non-Planar Check`** (Slicer, next to `Path Check`). For printers whose nozzle stays vertical. It measures the real bead height at every point against bead width, layer height and extrusion, and checks the head against everything printed before.
- Inputs: `Toolpath`, `Tip Radius` (mm, default 4), `Cone Angle` (half angle from vertical, degrees, default 30), `Cone Length` (mm, default 60), `Head Radius` (mm, default 40), `Pose` (0 = start, 1 = end).
- Outputs: `OK`, `Report`, `Bead Height` (mm per point), `Bad Heights`, `Collisions` (must be empty), `Head` (the checked head standing on the path at `Pose`).
- Watch out: measure your own nozzle and head. The defaults are only examples.

**`Continuous Path`, new inputs** (layered mode):
- `Seam Mode`: Fixed point (as before), Inner corner (in an inner corner of the outer wall, where it shows least; without one, the sharpest outer corner), or Sharpest corner. The corner is followed from layer to layer, at most half a bead to the side.
- `Min Layer Time` (s, 0 = off): layers that would be done sooner are printed slower, at one speed within the layer, never below Min Speed. About 60 s suits many large pellet prints.
- `Short Lap Time` (s, 0 = Min Layer Time): the minimum for short laps, shorter than min + 0.7464 x (max - min) of all lap lengths, with a change over 5 laps, as on the head printed in 2024 (Min Layer Time 35 s, Short Lap Time 40 s).
- Corners and straight edges stay where the wall is; the seam ramp runs along the wall and never cuts a corner. Skirt and brim rings end one bead before their start and step into the next ring square to it, the innermost ring runs into the start of the part.
- `Max Volume Rate` (mm³/s, 0 = off): moves that would ask more of the extruder run slower. The Ginger One gives about 330 mm³/s.
- `Skin Thickness` (mm, 0 = the layer height, never below 0.4 x layer height): non-planar only. Where the laps step mostly sideways (a crown, the shelf under a chin) the bead gets the sideways step x Skin Thickness / bead width instead of the (small) vertical gap.

**`Path Check`**: input `Min Layer Time` (s, 0 = the value of `Continuous Path`) and output `Layer Times` (s per layer). Layers that are too short get three remedies in the report: a slower speed, several parts side by side, or a pause as a last resort.

</details>

<details>
<summary><b>Machines and nozzle presets</b></summary>

- Machines and machine profiles are listed as manufacturer and machine: "Ginger Additive · Ginger One (Klipper)", "Weber Additive · DXR25 on a robot", "Generic · Filament printer 1.75 mm". Own `.machine` files can carry `manufacturer = ...`.
- **Ginger Additive · Ginger One (Klipper)**: START_PRINT and END_PRINT macros, E as volume × 1.0, fan on after layer 1. Confirm the factor with the Klipper `rotation_distance` of your extruder.
- `.machine` files know `material` and `bed temperature` for `{material}` and `{bed}` in the codes, and blocks `[after layer n]`. `Machine` has the optional inputs `Material` and `Bed Temperature` (°C).
- Nozzle presets on `Print Settings` and `Easy Slicer` are named by the nozzle, e.g. "3 mm flat nozzle, wide bead - bead 5 mm, layer 2 mm (tested)" or "4 mm nozzle (Weber profile) - bead 4.8 mm, layer 1.8 mm". A warning says when bead and layer do not suit the nozzle.

</details>

## The Robot tab

For printing with a robot: the cell is described once, then every toolpath becomes robot moves with the real tool orientation.

| Component | What it does |
|---|---|
| `Robot` | The arm. KUKA KR120 R2700-2 by default; any six-axis arm with a spherical wrist from a Robots plug-in library file (.xml) |
| `Robot Tool` | Tool centre point as in the Robots plug-in (AE20 extruder: YZ plane at -460, 3, 108), weight, centre of mass, 3D model |
| `Robot Cell` | Robot, tool and the work base the program counts from (KUKA `$BASE`). Optional inputs `Environment` (meshes for the collision check) and `External Axes` |
| `External Axis` | A rail the robot stands on, or a turntable the part stands on. Connect it to `External Axes` of `Robot Cell` |
| `Robot Program` | Tool tip on every path point, tool along the path's tool axis. Checks reach, axis ranges, wrist singularity, axis speed and collisions. Outputs the moves for the `DXR Generator`, a KRL program and the arm at any place of the path |
| `Robot Simulation` | Plays the program: the robot with its tool at any moment of the print (slider or Play). Output `Colliding` shows in red what collides at that moment |
| `Cell Geometry` | A sample print cell in 3D (floor, pedestal, bed, fence; the cell of the FH Muenster), the place of the robot, and the environment for the collision check. Your own cell as a .3dm file |

<details>
<summary><b>External axes and the collision check</b></summary>

**`External Axis`** (Robot). A rail (the robot moves along it) or a turntable (the part turns on it). `Robot Program` moves it along the print: the rail keeps the robot beside the point being printed, the table turns that point towards the robot. Both move smoothly, below their top speed and inside their range.
- Inputs: `Kind` (rail or turntable), `Place` (rail: moves along its X axis; table: origin = centre, Z = axis of rotation, X = 0°), `Min` and `Max` (mm for a rail, degrees for a table; both 0 = rail 0 to 3000 mm, table -360 to 360°), `Max Speed` (mm/s or °/s; 0 = 1000 mm/s or 90 °/s), `Name` (KUKA E1 to E6), `Endless`, `Offset` (mm or degrees), `Smoothing` (mm of path, default 300), `Coupled` (KUKA coupled base), `Size` (mm, drawing only).
- Wiring: `External Axis` → `External Axes` of `Robot Cell` (one rail and one table per cell).
- Watch out: KRL writes E1/E2 on every line, but the DXR output carries no E values. Not yet tried on a controller: run it in T1 first.

**Collision check in `Robot Program`.** The robot, the tool, rail and turntable are checked against each other, against the part printed so far, the bed and the `Environment` of `Robot Cell`.
- Inputs: `Check Collisions` (on by default; also a row on the component), `Clearance` (mm, default 10: closer bodies are reported as a near miss), `Collision Poses` (how many poses along the path, default 500; -1 = every move, 0 = no check).
- Outputs: `Collision Moves` (move numbers, as `Move` of `Robot Simulation`), `Collision Bodies` (the bodies at their worst collision, with a ball where they touch), `Collisions` (path points).
- The report names the pair, the move and the layer, and suggests what to change: tilt, nozzle length, tool turn, rail or turntable offset.
- Watch out: the check needs the 3D model of the robot (see the Info of `Robot`); without it, collisions are not checked. A tool without a mesh is checked as a plain cylinder from the flange to the tip, so connect your extruder as `Mesh` to `Robot Tool`.

</details>

The curve-based tools of the former Toolpaths tab are now the last group of the Slicer tab (see above), named like their Slicer counterparts (`Curve Fill Zigzag`, `Curve Fill Contour`, `Curve Fill Cells`, `Curve Continuous Path`, `Curve Uncross` ...).

## Machines

File names carry machine, date and time, part, weight and print time: `DXR_2611051216_Test Object_2668g_53min.dxr`. A vertical extruder is written as A 0, B 0, C 0, as the Weber DXR expects.

| | |
|---|---|
| **Proven in use** | Weber Additive DXR25 (KUKA robot, DXR code) · Ginger Additive Ginger One (G-code) |
| **Scaled test prints** | Bambu Lab, Prusa, Creality, Elegoo and other Marlin or Klipper printers, via `Desktop Test Print` |
| **Other pellet and robot systems** | Not tested. The generic pellet profile outputs position, tool axis, E and F per point; controller format and extrusion factor have to be matched on the machine. |

### 45-degree printing

- **Before modelling:** `Design Rules` gives layer height range, overlap, how thick to model a wall of two beads (CO90, and CO45 where the layers cross it at 45 degrees), the layer width for a finished wall thickness (LW45) and the start and end feature, after the CEAD sheets and the Ginger handbook.
- **Slicing:** `Slice` with `Angle` 45.
- **One line:** `Continuous Path` joins contours that lie inside each other (inner and outer shell, openings) with a bridge of two lines that stacks into a rib on every layer, after the Continuous-Toolpath Connection Rule (Wesseler, Herrlein, Kiendl).

### Your own machine or print cell

No code is needed. Everything is a text file or a Rhino file in `Documents/LARGER`:

| What | Where | How |
|---|---|---|
| G-code machine | `Machines/My printer.machine` | Start, layer and end code, bed size and E factor as text (see `Machine`). Easiest: type them into `Machine` and set `Save`. |
| Its 3D model, animated | `Machines/My printer.3dm` | Draw it in Rhino in the machine's coordinates (its 0, 0, 0 at the origin), one layer per axis: `Frame`, `Bed`, `Z`, `Y`, `X` and a point on `TCP` at the nozzle tip. `Printer Simulation` then moves it along the print and checks head, beam and frame for collisions. `kind = gantry / bedslinger / delta / belt 45 / robot` says what it can print. Without a model the bed is drawn from the bed size. |
| Pellet material | `Materials/My material.material` | Temperatures, drying, density and factor as text (see `Material`). Easiest: fill in the inputs of `Material` and press `Save material`. |
| Robot print cell | `Cells/My cell.3dm` | Draw it in millimetres in the coordinates of the bed, one part per layer: `Bed` (drawn, not checked for collisions), `Robot Base` (a point where the robot stands and a line from it along its X axis), `Work Base` (optional), and any other layer (Floor, Fence, Table ...) for the collision check. Set `Export` on `Cell Geometry` to get the cell of the FH Muenster in this form as a start. |
| Robot arm | `Documents/RobotSystems` (also `Documents/Robots` and `~/Robots`, where the installers put it) | The library of the Robots plug-in; `Robot` reads the arm and its 3D model from there (`Geometry File`). |
| Tool | `Robot Tool` | TCP plane, weight and centre of mass as in Robots; connect your extruder as a mesh to `Mesh`. |

New files appear in the lists of `Machine`, `Material` and `Cell Geometry` the next time they are placed. Pass the folder on as it is: the `.3dm` next to a `.machine` file is found by its name.

Step by step, with a complete example file, the layer convention of the model, bed slingers, deltas and robot arms: **[docs/OWN_MACHINES.md](docs/OWN_MACHINES.md)**. A template to copy: `EXAMPLE FILES/Machines/My Gantry.machine` + `My Gantry.3dm`.

All brands named belong to their respective owners. LARGER.slicer is an independent project and is not affiliated with these manufacturers.

## Also in the package

- **One Grasshopper definition, three workflows** – *Easy Slicer* slices surfaces automatically, *Path Creator (multiaxial)* turns prepared toolpaths into robot paths, *Custom Paths* exports your own paths as robot code.
- **Robot code for Weber DXR25 (KUKA)** – DXR Generator and G-code → DXR postprocessor with automatic header (runtime, layers, extrusion totals) and line numbering.
- **Variable line widths** – `DXR Variable Width` scales the extrusion along X, Y or Z with a colored width preview.
- **Preflight safety** – `DXR File Health Check` validates file name, readability and content before printing, and fails any move with feed rate 0.
- **CNC toolpaths** – zigzag (boustrophedon) toolpaths with HPGL/PLT output for the Zünd G3.
- **One-click setup** – the installer sets up all plugins and the KUKA KR120 robot library on Windows and macOS.
- **Machine guides** – operating and troubleshooting guides for every supported machine (German and English).

## Download

📥 **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** – always the latest release.
Contains the Grasshopper definition, the installer for Windows and macOS and the KUKA KR120 robot library.

🧩 **Plugin only:** search for **LARGERslicer** in the Rhino Package Manager (`PackageManager` command).

## Quick Start

**Requirements:** Rhino 8 (recommended) or Rhino 7 · Windows 11 or macOS 11+ · internet connection during setup

1. Download **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** and unzip it.
2. Close Rhino and run the installer in the `SETUP` folder:

   | Windows | macOS |
   |---|---|
   | Double-click `Install Windows.bat` | Right-click `Install Mac.command` → **Open** → **Open** |

3. Start Rhino, create a file in **millimeters**, open Grasshopper and open `LARGERslicer Weber Robot and Ginger.gh` from the `SETUP` folder.

The installer installs **Robots**, **LARGERslicer** and **Pufferfish** from the Rhino Package Manager and copies the KUKA KR120 robot library (`KR120_R2700_2`) to the folder the Robots plugin reads. It can be run again at any time and then only updates what is outdated.

<details>
<summary><b>Installer details and troubleshooting</b></summary>

- **Existing installations:** current plugins are kept, older ones are updated, newer ones (e.g. beta builds) are kept.
- **Old manual installations:** `.gha` files copied into the Grasshopper Libraries folder are detected and can be moved to a backup folder (otherwise components load twice).
- **Robot library:** a differing existing file is saved as `*.bak-<date>` before it is replaced.
- **macOS blocks the installer** (“unidentified developer”): right-click `Install Mac.command` → **Open**.
- **Robot system `KR120_R2700_2` missing:** run the installer again, or click **Libraries** on the *Load Robot System* component and download the online library **FH Muenster MSA**.
- **Rhino 7:** gets Robots 1.9 (Robots 2.x requires Rhino 8). The definition is tested in Rhino 8.
- **Pin a plugin version:** add the version after the package name in the `PACKAGES` list at the top of the installer, e.g. `"Robots 2.4.4"`.

More: [SETUP guide](SETUP/README.md)

</details>

## Supported Machines

| Machine | Type | Grasshopper definition | Guide |
|---|---|---|---|
| **Weber DXR25** | KUKA robot, multiaxial | `LARGERslicer Weber Robot and Ginger.gh` | [English](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md>) · [Deutsch](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md>) |
| **Ginger One** | Large-format pellet printer | `LARGERslicer Weber Robot and Ginger.gh` | [Quick Start](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md>) |
| **Ginger One (Marlin), non-planar head** | Pellet printer, vertical nozzle | [`LARGERslicer - Head (Ginger One, non-planar).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh>): the 2024 head placed as printed, "Follow the surface", one spiral, brim, `Path Check`, `Non-Planar Check`, G-code | The comparison with the 2024 G-code is on the canvas |
| **Ginger One (Klipper), non-planar** | Pellet printer, vertical nozzle | [`LARGERslicer - Non Planar Head (Ginger).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Non Planar Head (Ginger).gh>): layers that follow the part, one spiral, `Path Check`, `Non-Planar Check`, G-code | Like Quick Start example 07, but with "Follow the part" |
| **Universal Robots UR5** | Collaborative robot | `UR5slicer.gh` | [Quick Start](<EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/README UR5 00 QUICK START GUIDE.md>) |
| **Zünd G3 L-2500** | CNC milling / cutting | `ZündHGPL PLT MSA.gh` | [Quick Start](<EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md>) |

## Usage Guide

`LARGERslicer Weber Robot and Ginger.gh` contains three workflows – copy the one you need into a new Grasshopper file:

1. **Easy Slicer** – slices surfaces and creates the toolpath automatically.
2. **Path Creator (multiaxial)** – converts a prepared toolpath into multiaxial robot paths.
3. **Custom Paths** – uses only the conversion/export components to create DXR robot code from your own paths.

Password-protected clusters: right-click the component and enter **Supersizedprinting**.

<details>
<summary><b>Part 1: 3D Print Path Maker</b></summary>

1. **Input geometry:** right-click **Set One Surface** → pick your surface.
2. **Skirt / Brim:** set them in the rows under `Start and End` (click a row for its choices).
3. **Parameters:**
   - **Corner Fillet Radius (mm)** – rounds off corners
   - **Min/Max Extrusion Width (mm)** – standard and adaptive widths
   - **Layer Height (mm)** – Z step
   - **Point Spacing (mm)** – point interval (default 2–5 mm)

**Output:** toolpath curves along the midline of each layer.

</details>

<details>
<summary><b>Part 2: G-Code / DXR Maker</b></summary>

1. **Preview:** scrub through the toolpath with the **Preview Slider (0 → 1)**.
2. **Printer profile:** choose **Ginger One** or **Weber DXR** (UR5: use `UR5slicer.gh`).
3. **Preflight check:** validate the file with **DXR File Health Check** and continue only on **GO**.
4. **Output location:** use the integrated desktop path or your own file path.
5. **Settings:** job name, feedrate / speed, flow (%).

**Export in two steps:**

1. Click **1. Slice Data** – calculates weight, time and coordinates.
2. Click **2. Export G-Code** – writes the file.

If you skip *Slice Data*, only the standard start/end code is written – without coordinates, weight or time.

**Outputs:** weight (kg), print time, machine-ready code.

</details>

## Documentation

| Topic | Link |
|---|---|
| Overview of all guides | [Documentation index](LARGERslicer/documentations/README.md) |
| Operate a machine | [Machine guides](LARGERslicer/documentations/README.md#machine-guides) |
| KUKA / DXR25 robot errors | [English](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md) · [Deutsch](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md) |
| Plugin components | [Component overview](LARGERslicer/documentations/COMPONENTS_OVERVIEW.md) · [Inputs/outputs](LARGERslicer/documentations/COMPONENT_INPUTS_OUTPUTS.md) |
| What's new | [Changelog](LARGERslicer/CHANGELOG.md) |

## Troubleshooting

| Problem | Solution |
|---|---|
| Plugin components missing or red | Run the installer again, then restart Rhino |
| Robot system `KR120_R2700_2` missing | Run the installer again, or download the **FH Muenster MSA** online library via **Libraries** on *Load Robot System* |
| Surface not accepted | Use **Set One Surface** with a valid Brep surface |
| G-code file does not appear | Click **Export G-Code** and check the save path |
| DXR File Health Check returns **NO GO** | Fix file name / path, export again and check until **GO** |
| Preview slider inactive | Check the **Path Preview** input wiring |
| DXR25: robot not mastered / external axis E1 (KSS01347, KSS00106, KSS15047) | [Robot Not Mastered (E1)](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-not-mastered-e1) – a program reset alone does not help |
| DXR25: brake test / start movement does not begin | [Brake test / start movement](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-does-not-perform-brake-test--start-movement-after-startup) |
| DXR25: emergency stop near the safety fence | [Emergency stop near safety fence](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-emergency-stop-triggered-near-safety-fence-multiaxial-printing) |
| DXR25: robot does not move / reference run does not start | [Robot does not move](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed) |

> [!TIP]
> **45° multiaxial prints:** tilt the extruder toward the wall or the largest free area, not toward the safety door, and keep extra clearance from the fence – the braking zone starts about 10–15 cm before the physical barrier.

## Development

**Developer documentation** ([handbook](docs/README.md)): [Architecture](docs/ARCHITECTURE.md) · [Printing principles](docs/PRINTING_PRINCIPLES.md) · [Icons](docs/ICONS.md) · [Testing](docs/TESTING.md) · [Examples](docs/EXAMPLES.md) · [Decisions](docs/DECISIONS.md)

<details>
<summary><b>Repository structure</b></summary>

```
LARGER.slicer/
├── SETUP/                     # Start here: installer, Weber/Ginger definition, robot library
├── LARGERslicer/              # Grasshopper plugin (C#)
│   ├── Components/            # CNC, Export and Utility components
│   ├── Resources/             # Component icons
│   ├── documentations/        # Guides and references
│   └── build-package.sh/.bat  # Builds the Rhino 7, 8 and 9 Yak packages
└── EXAMPLE FILES/             # Machine-specific definitions and guides
    ├── 00 - UNIVERSAL ROBOTS - UR5/
    ├── 00 - WEBER - GINGER [Robotic] 3D Printing/
    └── 00 - ZUENDT - 3D CNC MILLING/
```

</details>

**Build the plugin:**

```bash
cd LARGERslicer
./build-package.sh          # or build-package.bat on Windows; optional: new version, e.g. 1.1.14
```

Creates `dist/largerslicer-<version>-rh9_0-any.yak`, `dist/largerslicer-<version>-rh8_0-any.yak` and `dist/largerslicer-<version>-rh7_0-any.yak`. More: [Plugin README](LARGERslicer/README.md).

## License

© 2025 Moritz Wesseler, FH Münster · Licensed under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) – non-commercial use only.

Contributors: Fabio Koczula, Claudio Schröder · Originally inspired by Ginger.Additive, fully reimplemented with expanded machine support.

## Safety and disclaimer

Robot and machine programs made with LARGER.slicer are not verified. Before every run, check them in T1 or test mode at reduced speed; collisions, axis limits, singularities and temperatures are the operator's responsibility. Only trained people may run the machines, following the operating instructions of the cell and the manufacturers (KUKA, Weber and others). No warranty, as far as the law allows. Third-party licences, sources and trademarks: see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
