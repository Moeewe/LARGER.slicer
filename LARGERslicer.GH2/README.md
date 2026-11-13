# LARGER.slicer for Grasshopper 2

First version (0.1.0) of the slicer for Grasshopper 2 in Rhino 9. It uses the same slicer
core as the Grasshopper 1 plug-in: the files in `../LARGERslicer/Toolpaths` are compiled into
this assembly, so both versions cut, fill and check in exactly the same way.

## Components

The tab follows the Grasshopper 1 plug-in: the same components with the same names, inputs and
icons. The Slicer section is split into the groups of the Grasshopper 1 tab, in this order:
Start, Layers, Walls & fill, Path, Check, Output, Curve tools.

**LARGER > Slicer**

| Group | Component | What it does |
|---|---|---|
| Start | Quick Start | Puts five worked examples on the canvas |
| Start | Easy Slicer | Model in, checked path out: the whole chain in one component |
| Start | Print Settings | Bead width, layer height, speed (mm/s; 50 mm/s = F3000), nozzle, overlap, radius, resolution, machine profile |
| Start | Machine Profile | What E means for your machine (Generic, Weber Additive · DXR25, Ginger Additive · Ginger One ...), factors to calibrate it |
| Start | Design Rules | Layer height range, overlap, CO90 / CO45 / LW45 before you model |
| Start | Print Bed | The bed in the viewport; checks that every path lies on it |
| Start | Material | Start values for a pellet material: temperatures, drying, density; own materials as `.material` files |
| Start | Material Factor | New extrusion factor from measured bead widths |
| Layers | Slice | Flat, angled, non-planar (guide surface, or following the part itself with a vertical nozzle) or multi-axis (guide curve) layers |
| Layers | Self Support Check · Self Supporting Envelope · Best Angle | Where the part hangs over too far, the least material that makes it self-supporting, the slice angle with the least overhang |
| Walls & fill | Walls | Wall centre lines, one bead spacing apart; inside, centred on or outside the geometry |
| Walls & fill | Bottom Fill | Solid bottom as one line |
| Walls & fill | Infill Ribs | Ribs square to the wall, from both walls meeting in the middle |
| Walls & fill | Infill Cells | Honeycomb or grid cells as part of the one line |
| Walls & fill | Infill Wave | Wave, truss or meander along the wall, meeting in the middle |
| Walls & fill | Infill Sketch | Ribs along curves you draw in top view |
| Walls & fill | Infill Chambers | A chamber wall (boxes or truss) inside the part |
| Walls & fill | Support Fins | Thin fins outside the part that hold up overhangs, as part of the one line |
| Path | Continuous Path | All layers as one path; layered with a short ramp, or spiral |
| Path | Round Corners | Rounds the corners of the path |
| Path | Start and End | Skirt and brim before the part, exit move after it |
| Path | Surface Pattern | Textures in the wall (knit, ribs, waves, zigzag, dots, loops, a picture); the path stays one line |
| Check | Path Check | No crossing, every bead supported; layer times against Min Layer Time |
| Check | Non-Planar Check | Non-planar paths for a vertical nozzle: real bead height at every point, the head against what is printed |
| Check | Extrusion and Speed | Points, planes, extrusion, speed, role and layer per point |
| Output | Ginger G-code | Full-size G-code for the Ginger One with its start and end sequence |
| Output | Machine | Built-in machines and your own from `Documents/LARGER/Machines` (.machine text files), or typed in here |
| Output | Machine G-code | G-code for the machine you connect |
| Output | Desktop Test Print | The path scaled down for a desktop printer, with that printer's start, layer change and end |
| Output | Toolpath to Robot Lines | One PTP line per path point with one fixed tool orientation; extrusion and feed per line |
| Output | Printer Simulation | Plays the path on the machine (Ginger One or your own with a 3D model): head on the path, part printed up to that moment |
| Curve tools | Layers to Curves · Curves to Layers | Layers as plain curves for your own pattern, and back |
| Curve tools | Import G-code | G-code from other slicers or a .dxr file as a toolpath, to check it |
| Curve tools | Material Field | The toolpath as a direction-dependent material field for FE or IGA (CSV, JSON, VTK) |
| Curve tools | Curve Wall and Zigzag · Curve Continuous Path | Walls and one continuous path from your own boundary curves (old Toolpaths tab) |
| Curve tools | Curve Fill Zigzag · Curve Fill Contour · Curve Fill Cells · Curve Fill Euler · Curve Continuous Infill · Curve Fill Cross Lines | Fills of your own boundary curves (old Toolpaths tab) |
| Curve tools | Curve Join and Order · Curve Uncross · Curve Bridge · Align Curves · Alternate Curves · Join Open Contours | Joining, ordering, untangling and turning your own curves |

Two components of the first Grasshopper 2 version are kept only so that older definitions open:
`Infill` (now Infill Ribs, Cells, Wave and Chambers) and `Round and Lead` (now Round Corners and
Start and End). They are no longer on the tab.

**LARGER > Robot**

| Component | What it does |
|---|---|
| Robot | The arm: KUKA KR120 R2700-2 by default, or any six-axis arm from a Robots library file |
| Robot Tool | Tool centre point and weight |
| Robot Cell | Robot, tool and the work base the program counts from; optional Environment and External Axes |
| External Axis | A rail the robot stands on, or a turntable the part stands on |
| Cell Geometry | The print cell of the FH Muenster in 3D, the place of the robot and the environment for the collision check |
| Robot Program | Robot moves with the real tool orientation, reach, axis and collision check, lines for the DXR Generator, KRL program |
| Robot Simulation | The robot with its tool at any moment of the print (slider); Colliding shows what collides |

**LARGER > DXR**

| Component | What it does |
|---|---|
| Machine Settings | Temperatures and cooling for the start and end sequence |
| Machine Settings Extended | Multi-zone settings: 4 bed zones and 4 extruder zones (extended mode) |
| DXR Generator | Robot lines, extrusion and speed to a DXR file for the Weber DXR |
| DXR GCode Postprocessor | G-code from Cura and other slicers to a DXR file |
| DXR File Health Check | Checks a DXR file before printing: name, readability, content, no move with feed rate 0 |

**LARGER > Utilities**

| Component | What it does |
|---|---|
| DXR Variable Width | Scales the extrusion of a DXR program along X, Y or Z; the segments and their colours as preview |
| Feedrate Calculator | Target speed in mm/s to feedrate in mm/min, rounded |
| Date Timestamp | A timestamp (yymmddHHMM_) for file names |
| Desktop Path | The path of the desktop folder |
| Safe Component | Writes text lines to a file, UTF-8 without BOM (for robot controllers) |

**LARGER > CNC**

| Component | What it does |
|---|---|
| CNC - Cutter Selector | A cutter from the database for CNC - Program |
| CNC - Program | Boustrophedon toolpath and PLT file for the Zünd G3 |

### Icons

Every component has the icon of its Grasshopper 1 twin. The 24 px images are the files of the
Grasshopper 1 plug-in (`../LARGERslicer/Resources`), the 48 px images in `Icons/48` are the same
drawings finished at twice the size by the same script, so they stay sharp where Grasshopper 2
draws icons larger:

```bash
cd ../LARGERslicer
ICON_SIZE=48 ICON_OUT=../LARGERslicer.GH2/Icons/48 python3 scripts/generate_icons.py
```

Both are embedded in the assembly; Grasshopper 2 gets the largest one as a `PixelIcon` (`Icons.cs`): its `PixelIcon` (9.1) refuses several sizes of one picture, and with that every icon was empty and Grasshopper 2 drew lettered stand-ins.

### Not in Grasshopper 2

These Grasshopper 1 tools are not ported. None of the example and setup definitions uses them,
or Grasshopper 2 does the job itself:

| Grasshopper 1 | Why not |
|---|---|
| CNC Utilities 01 to 08, CAD Package Export | Planning tools for milled formwork; used by no definition |
| Fast Deconstruct Point, RTree Closest Point, RTree Sort | Speed-ups for Grasshopper 1; Grasshopper 2 has fast native components for this |
| Stream Freeze, Custom Preview Lineweights, Super Printpath Preview, Fast GCode DXR Preview | Canvas and viewport helpers built on the Grasshopper 1 canvas and display; Grasshopper 2 has its own preview, and Printer Simulation, Robot Simulation and Import G-code show the path |
| G-Code Diff Check, GCode E Remap MultiPart, Curve Pattern Split, Surface Thickness Heatmap | One-off analysis and G-code repair tools from earlier projects; used by no definition. Import G-code with Path Check checks foreign G-code |
| Overlap Tester Var Width | Test tool for DXR Variable Width; used by no definition |

## Build

```bash
dotnet build -c Release
```

The result is `bin/Release/net8.0/LARGERslicerGH2.rhp` (plus `Clipper2Lib.dll`). The project
looks for Rhino 9 in its default folder; pass `-p:Rhino9=<folder with RhinoCommon.dll>` if it
is somewhere else.

## Load it

Grasshopper 2 reads extra plug-ins from the file `CustomPluginInclude.txt` in its settings
folder (macOS: `~/Library/Application Support/Grasshopper2/Settings`, Windows:
`%APPDATA%\Grasshopper2\Settings`). Add the full path of `LARGERslicerGH2.rhp` as one line
and restart Rhino.

## Status

Tested in Grasshopper 2 (Rhino 9.1) on a box: slice, walls, bottom, honeycomb infill,
continuous path, check and DXR all compute. A guide curve from a native arc component is
accepted by Slice. Not yet tested: non-planar and multi-axis slicing on a real part, Windows, installation through the package manager, and a real print.

`Toolpath to Robot Lines` writes one fixed tool orientation (A, B, C). Check it against your
robot cell before the first run. For tilted tool axes use the planes from `Extrusion and Speed`.

Quick Start was run in Grasshopper 2 (Rhino 9.1): it builds the five examples and all of them compute. The robot is shown in 3D and checked for collisions as in Grasshopper 1. Not tried by hand in Grasshopper 2 yet: Machine, Machine G-code, the brim and saving files (the code they share with the Grasshopper 1 plug-in is tested there).

A definition saved with an earlier build of this plug-in can show Print Settings and Round and Lead as faulted, because they have more inputs now: place them again.

All Grasshopper 1 components that the example and setup definitions use are in Grasshopper 2 now
(see the lists above). The components added after the first Grasshopper 2 version, the presets on
their inputs and the icons are built and compiled, but not yet tried on a Grasshopper 2 canvas.
