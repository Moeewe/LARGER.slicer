# LARGERslicer - Component Overview

A compact list of every component of the Grasshopper 1 plug-in, by tab, as the code has it
(from 2.0.5; read from `LARGERslicer/Components/**`). The user-facing descriptions with inputs
and examples are the tables of the repository README: [Slicer tab](../../README.md#the-slicer-tab)
and [Robot tab](../../README.md#the-robot-tab). The Grasshopper 2 plug-in has its own list in
[LARGERslicer.GH2/README.md](../../LARGERslicer.GH2/README.md).

Speeds are entered in mm/s (50 mm/s = F3000); the machine files keep mm/min.

"Hidden" components are not in the toolbar but still open in older files (their GUIDs are
unchanged). "Obscure" components are found only by searching the canvas.

---

## LARGER > Slicer

The tab is grouped by exposure, in the order a definition runs.

| Group | Component (nickname) | What it does | Main inputs |
|---|---|---|---|
| Start | Quick Start (Start) | Places eight worked examples on one print bed, each with a Control Panel and a Result | - |
| Start | Control Panel (Controls) | The main settings of every LARGER component in its group, without wires; a wire wins | - |
| Start | Result (Result) | Verdict of the checks, layers, time, material, save buttons and simulation of the group | - |
| Start | Easy Slicer (Easy) | Model in, checked path out: the whole chain in one component | Geometry, Bead Width, Layer Height, Speed, Profile Preset, Walls, Bottom Layers, Infill, Spiral, Brim, Nozzle Preset, Top Layers, Angle, Sacrificial Layers, Wall Position |
| Start | Print Settings (PrintSet) | Bead width, layer height, speed and quality for all slicer components | Bead Width, Layer Height, Speed, Flow, Overlap, Min Radius, Resolution, Machine Profile, Density, First Layer Height, Nozzle Preset, First Layer Speed (always mm/s) |
| Start | Print Tuning (Tune) | Between Print Settings and the slicer components: speed per line type, coasting, prime, travel lift; Machine G-code, DXR and KUKA follow it | Settings, Wall/Infill/Bottom/Skirt/Support Speed (mm/s, 0 = print speed), Coasting, Prime, Travel Lift (mm) |
| Start | Machine Profile (Profile) | How the printer wants its extrusion values; factors to calibrate | Preset, Factor, First Layer Factor, Filament Diameter, Cross Section |
| Start | Design Rules (Rules) | Layer range, overlap, CO90 / CO45 / LW45 before you model; optional Settings take bead, layer, nozzle and overlap from Print Settings. Output "Layer Range" | Bead Width, Nozzle, Layer Height, Overlap, Finish Thickness, Milling, Settings |
| Start | Print Bed (Bed) | The bed in the viewport; checks that every path lies on it | Size X, Size Y, Height, Toolpath |
| Start | Material (Material) | Start values for a pellet material; own `.material` files | Material, zones, Nozzle, Bed, Drying, Density, Factor |
| Start | Material Factor (Factor) | New extrusion factor (F1) from measured bead widths and the current factor (F0) | Target Width, Measured Widths, Current Factor |
| Layers | Slice (Slice) | Flat, angled, non-planar (guide surface or following the part) or multi-axis layers | Geometry, Settings, Plane, Angle, Guide, Guide Mode, Tool Axis, Sacrificial Layers |
| Layers | Self Support Check (SelfSup) | Where the part is not self-supporting | Geometry, Settings, Plane, Angle, Guide, Factor |
| Layers | Self Supporting Envelope (Envelope) | Least material that makes the layers self-supporting | Layers, Settings, Factor |
| Layers | Best Angle (BestAng) | Slice angle and tilt with the least overhang | Geometry, Settings, Min/Max Angle, Turn |
| Walls & fill | Walls (Walls) | Wall centre lines one bead spacing apart; Position inside, centred or outside (with Outside the extra walls go outward) | Layers, Settings, Count, Position |
| Walls & fill | Bottom Fill (Bottom) | Solid bottom (and with Top Layers a closed top) as one line | Layers, Settings, Count, Pattern, Angle, Top Layers |
| Walls & fill | Infill Ribs (Ribs) | Ribs square to the wall or along drawn curves (filters crossing curves itself) | Layers, Settings, Locations, Spacing, Depth, Meet, Stagger |
| Walls & fill | Infill Cells (Cells) | Honeycomb or grid cells as part of the one line | Layers, Settings, Pattern, Cell Size, Angle, Origin, Stretch |
| Walls & fill | Infill Wave (Wave) | Wave, truss or meander along the wall; passes the layers through with a warning when Depth is too small | Layers, Settings, Shape, Wavelength, Depth, Origin, Meet |
| Walls & fill | Infill Chambers (Chambers) | A band of closed chambers (boxes or truss) along the inside of the wall | Layers, Settings, Shape, Length, Depth |
| Walls & fill | Support Fins (Fins) | Thin fins outside the part that hold up overhangs | Layers, Settings, Overhang Angle, Spacing, Reach, Contact, Gap |
| Path | Continuous Path (ContPath) | All layers as one path: layered with a ramp, or spiral | Layers, Settings, Mode, Seam, Seam Gap, Bridge, Seam Mode, Min Layer Time, Max Volume Rate, Skin Thickness |
| Path | Round Corners (Round) | Rounds every corner with tangent arcs | Toolpath, Min Radius, Rib Radius |
| Path | Start and End (StartEnd) | Skirt, brim and exit move | Toolpath, Skirt Loops, Skirt Distance, Exit Length/Lift/Curve/Flow, Brim, Brim Loops |
| Path | Surface Pattern (Pattern) | Textures in the wall (knit, pixels, ribs, waves, zigzag, dots, loops, a picture); Pitch 0 = three bead widths | Toolpath, Pattern, Amplitude, Pitch, Layer Period, Region, Falloff, Attractors, Reach, Image |
| Path | Weave Zones (Zones) | Finds where two beads of a layer lie side by side | Toolpath, Include, Exclude, Radius, Min Length, Min Layers |
| Path | Weave (Weave) | Interlocks two beads that lie side by side (shift, width, snake, helix, teeth ...) | Toolpath, Zones, Pattern, Amplitude, Wavelength, Phase, Layer Period |
| Path | Shape Field (Shape) | How high the part must become over its plan (from an analysis, points, a profile ...) | Mode, Points, Values, Line, Guide, Height, Exponent |
| Path | Reshape Path (Reshape) | Reshapes a finished toolpath by a Shape Field, same number of layers; its check also judges the flow limit | Toolpath, Field, Variant, Material, Min/Max Layer, Max Slope, Flat Layers, Roof, Max Flow |
| Path | Compare Variants (Compare) | Several reshapes in one table | Toolpath, Fields, Variants, Material, Min/Max Layer, Max Flow, Flat Layers, Roof |
| Check | Path Check (Check) | No crossing, every bead supported, layer times | Toolpath, Max Offset, Min Layer Time |
| Check | Non-Planar Check (NP Check) | Real bead height for a vertical nozzle, the head against the print; limit 0.8 x bead width; NaN for unmeasured points | Toolpath, Tip Radius, Cone Angle, Cone Length, Head Radius, Pose |
| Check | Multi-Axis Check (MA Check) | Bead heights, tilt, turn speed and flow along the real tool axis; same bead-height limit as Non-Planar Check | Toolpath, Min Layer, Max Layer, Max Tilt, Max Turn Speed, Max Turn, Max Flow |
| Check | Path Heat (Heat) | A toolpath coloured by one value per point, with title and colour bar; NaN is grey | Toolpath, Values, Range, Move, Title, Unit, Legend |
| Check | Extrusion and Speed (E+F) | Points, planes, E and F per move | Toolpath, Compensation |
| Check | Material Field (MatField) | The toolpath as a direction-dependent material field (CSV, JSON, VTK) | Toolpath, Geometry, Sample Points, moduli, File |
| Check | Joint Data (Joints) | The joints of a path as data for analysis and tests | Toolpath, Zones, stiffnesses, File |
| Check | Weave Variants (WeaveVar) | The same zones woven in fourteen ways, measured the same way | Toolpath, Zones, Amplitude, Layers |
| Check | Weave Test (WeaveTest) | A test series of woven joint specimens on beds | Settings, Length, Height, Bed X, Bed Y, Replicates |
| Output | Machine (Machine) | Built-in and own G-code machines (`.machine` files) | Machine, File, Name, Start/Layer/End Code, Extrusion Factor, Model, Material |
| Output | Machine G-code (G-code) | G-code for the connected machine; moves down onto the part at print speed (from 2.0.5) | Toolpath, Machine, Name, File, Save |
| Output | Printer Simulation (PrintSim) | Plays the print on the machine, with collision check | Machine, Toolpath, Pose, Play, Film Length |
| Output | Desktop Test Print (Desktop) | The path scaled down for a desktop filament printer | Toolpath, Printer, Scale, Nozzle, File, Save |
| Output | Import G-code (Import) | G-code from other slicers or a .dxr file as a toolpath, to check it | File, Bead Width, Layer Height, Machine Profile |
| Output | Reshape Code (Reshape Code) | Reshapes a finished G-code or .dxr file in place | File, Code, Field, Variant, Material, Output, Save |
| Output | Toolpath to Robot Lines (PTP) | One PTP line per point with one fixed A, B, C; writes "; layer N" tags | Toolpath, A, B, C |
| Curve tools | Layers to Curves (L>Crv) · Curves to Layers (Crv>L) | Layers as plain curves and back (the real contours of non-planar layers) | Layers / Curves, Planes, Settings, Roles |
| Curve tools | Curve Wall and Zigzag · Curve Continuous Infill · Curve Fill Zigzag · Curve Fill Cells | Fills of your own boundary curves | Curve / Boundary, Print Width, Holes |
| Curve tools | Curve Join and Order · Curve Uncross | Joining, ordering and untangling your own curves | Curves |

Hidden on the Slicer tab: Ginger G-code (use Machine + Machine G-code), Infill Sketch (now in
Infill Ribs), Slice Options.

Obscure on the Slicer tab (search only): Align Curves, Alternate Curves, Join Open Contours,
Curve Continuous Path, Curve Fill Contour, Curve Fill Euler, Curve Fill Cross Lines, Curve Bridge
(the last five duplicate Curve Continuous Infill, Curve Fill Zigzag and Curve Join and Order).

---

## LARGER > Robot

| Component (nickname) | What it does | Main inputs |
|---|---|---|
| Robot (Robot) | KUKA KR120 R2700-2, a Universal Robots arm (UR3, UR5, UR10 and their e-Series), or an arm from a Robots library file (warns when a Library file overrides the chosen UR) | Library, Cell Name, Base, Geometry File |
| Robot Tool (Tool) | Tool centre point and weight, or a preset (AE20, clay extruder V2, pen holder, wool spinner, Own tool) | Name, TCP, Weight, Centroid, Mesh |
| Robot Cell (Cell) | Robot, tool and work base; warns which inputs a UR ignores | Robot, Tool, Work Base, Base Name, Environment, External Axes |
| External Axis (Ext Axis) | A rail or a turntable | Kind, Place, Min, Max, Max Speed |
| Cell Geometry (Cell Geo) | The FH Muenster print cell in 3D | Walls, Fence, Cell, File, Export |
| Robot Program (Program) | Robot moves with reach, axis and collision check; output "Code" is KRL or URScript; for a UR also Blend, Robot IP and Upload to robot (rows and right-click menu) | Toolpath, Cell, Turn, Name, Check, Pose, File, Save, Environment, Check Collisions, Clearance |
| Robot Simulation (RobotSim) | The robot with its tool at any moment of the print | Program, Time, Play, Film Length, Show Cell |

Hidden: UR Cell, UR Program, UR Simulation (use the Robot chain).

---

## LARGER > DXR

| Row | Component (nickname) | What it does |
|---|---|---|
| 1 | Machine Settings (DXR Set) | Temperatures and cooling for the start and end sequence (V.P.VAR_*) |
| 1 | Machine Settings Extended (DXR Set+) | Four bed zones and the extruder zones (V.E.GLOBAL_*) |
| 1 | Fan Schedule (Fan) | Fan by layer; also takes a G-code Machine (M106 S0-255 / M107) |
| 1 | DXR Generator (DXR Gen) | Robot lines, extrusion and feed to a DXR file (input "Feed (mm/min)") |
| 1 | G-code to DXR (G>DXR) | G-code from any slicer to a DXR program; connected Machine Settings win over the G-code header (was DXR GCode Postprocessor) |
| 1 | DXR File Health Check (DXR Check) | Checks a DXR or G-code file before printing |
| 2 | DXR Variable Width (VarWidth) | Scales the XE values of a DXR program along X, Y or Z |
| 2 | DXR E Remap (ERemap) | Recalculates XE from several part geometries (was GCode E Remap MultiPart) |
| 2 | G-code Diff Check (GDiff) | Compares two programs line by line (was G-Code Diff Check) |
| 2 | DXR Width Preview (DXR View) | A DXR program coloured by line width (was Fast GCode DXR Preview) |
| 2 | Surface Thickness Heatmap (ThkMap) | Wall thickness per part as a coloured mesh, Part Info for DXR E Remap |

---

## LARGER > CNC

| Component (nickname) | What it does |
|---|---|
| CNC - Quick Start (CNC Start) | Places a complete Zünd example: a topography roughed, finished and cut out |
| CNC - Plate (Plate) | STEP 1: the raw plate on the table, where the model lies, material |
| CNC - Cutter (Cutter) | STEP 2: one cutter and its magazine place; Cutter 6 = own flat, 7 = own ball cutter (Diameter, Cutting Length, Max Depth, Spindle Speed); Module 11/21/31 for every cutter |
| CNC - Rough and Finish (Rough + Finish) | STEP 3: rough, finish, cut out, PLT file for the Zünd G3 |
| CNC - Relief (Relief) | Example models to mill (site model, facade, scales, gyroid, vaults ...) |

Hidden: CNC - Program and CNC - Cutter Selector (use CNC - Rough and Finish and CNC - Cutter),
CNC - Own Cutter (now Cutter 6 and 7 of CNC - Cutter).

---

## LARGER > Formwork

Milled formwork from boards (was "CNC Utilities 01 to 08"); the step is in each description.

| Step | Component (nickname) | What it does |
|---|---|---|
| 1 | Formwork - Orient Part (Orient) | Orients a solid: length in X, depth in Y, height in Z |
| 2 | Formwork - Dimensions (Dims) | Bounding box and dimensions of the oriented solid |
| 3 | Formwork - Board Layers (Boards) | Splits the height into boards and separation joints |
| 3b | Formwork - Split Joint (Split) | Splits each board at its joint into a lower and an upper part |
| 4 | Formwork - Milling Depth (Depth) | Milling depth per board in steps |
| 5 | Formwork - Raw Block (Block) | The raw block from boards, depths and board length |
| 6 | Formwork - Fixtures (Fixtures) | L-shaped stops and bases; shortens every board by Insert Depth at both ends (it does not mill pockets) |
| 7 | Formwork - Bill of Materials (BOM) | Bill of materials for a panel and CSV |
| 8 | Formwork - Export (Export) | Boards and bill of materials as 3DM and CSV |

---

## LARGER > Export

| Component (nickname) | What it does |
|---|---|
| CAD Package Export (CAD Export) | Breps 1:1 to 3DM, STEP and IGES; STEP/IGES through a hidden document (the open Rhino document is not touched), once per switch-on, notes open Breps |
| Save Text File (Save) | Writes text lines to a file, UTF-8 without BOM (was Safe Component) |

---

## LARGER > Utilities

| Component (nickname) | What it does |
|---|---|
| Curve Pattern Split (CurvePattern) | Splits a curve into fixed and variable sections by a repeating module |
| Custom Preview Lineweights (LineWeight) | Preview with own line weights and colours |
| Overlap Tester Var Width (OverlapV12) | False-colour overlap test with variable width per part |
| Stream Freeze (Freeze) | Lets data pass or holds it |

Hidden: Super Printpath Preview, RTree Closest Point, RTree Sort, Feedrate Calculator, Fast
Deconstruct Point, Desktop Path, Date Timestamp.

---

## DXR File Format

### Header
The DXR header carries values calculated from the program:
- `ProgRunTimeTotal`: estimated print time in seconds
- `number of layers`
- `Eges`: total extrusion (sum of all XE values)
- Bounds: Xmin/Xmax, Ymin/Ymax, Zmin/Zmax

### File structure
```
;Header comments (calculated values)
;=================================
N10 G90
N20 V.P.VAR_heatbedtemp = 60
N30 L heatbedTemp_sub.nc
... (machine settings)
N70 G1 F3000.000 X113.380 Y471.580 Z2.000 G91 XE=[0.000000*P1] G90
... (movement lines)
N9999999 L extruderTemp_sub.nc
M29
; footer: generator and time stamp
```

- Sequential line numbering (N10, N20, N30 ...)
- Feature subprograms are called as the Weber software calls them (`L wall_sub.nc`, `L infill_sub.nc`, `L skin_sub.nc`, `L skirt_sub.nc`)
- Fan Schedule switches the fan at the layer change (`V.P.VAR_fan`, `L fan_sub.nc`; in the Extended format `V.E.GLOBAL[3]` on the 0-255 scale of Fan Speed)

---

## Technical Notes

- Icons are loaded with `IconHelper.Load("IconName.png")` from `Resources/` and embedded in the assembly (24 px; the Grasshopper 2 plug-in also has 48 px copies). See [docs/ICONS.md](../../docs/ICONS.md).
- Main category "LARGER"; tabs Slicer, Robot, DXR, CNC, Formwork, Export, Utilities.
- Renamed, moved and hidden components keep their GUIDs, so old files open.
- Author: Moritz Wesseler, FH Münster.
