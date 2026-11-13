# LARGER.slicer for Grasshopper 2

First version (0.1.0) of the slicer for Grasshopper 2 in Rhino 9. It uses the same slicer
core as the Grasshopper 1 plug-in: the files in `../LARGERslicer/Toolpaths` are compiled into
this assembly, so both versions cut, fill and check in exactly the same way.

## Components

**LARGER > Slicer**

| Component | What it does |
|---|---|
| Easy Slicer | Model in, checked path out: the whole chain in one component |
| Print Settings | Bead width, layer height, speed (mm/s; 50 mm/s = F3000), nozzle, machine profile |
| Material | Start values for a pellet material: temperatures, drying, density; own materials as `.material` files |
| Material Factor | New extrusion factor from measured bead widths |
| Slice | Flat, angled, non-planar (guide surface, or following the part itself with a vertical nozzle) or multi-axis (guide curve) layers |
| Self Support Check · Self Supporting Envelope · Best Angle | Where the part hangs over too far, the least material that makes it self-supporting, the slice angle with the least overhang |
| Walls | Wall centre lines, one bead spacing apart |
| Bottom Fill | Solid bottom as one line |
| Infill | Ribs, honeycomb, grid, wave, truss, meander, chamber wall, truss wall |
| Support Fins | Thin fins outside the part that hold up overhangs, as part of the one line |
| Surface Pattern | Textures in the wall (knit, ribs, waves, zigzag, dots, loops, a picture), optional region and attractors; the path stays one line |
| Continuous Path | All layers as one path; layered with a short ramp, or spiral |
| Round and Lead | Rounded corners, skirt before the part, exit move after it |
| Path Check | No crossing, every bead supported; layer times against Min Layer Time |
| Non-Planar Check | Non-planar paths for a vertical nozzle: real bead height at every point, the head (tip, cone, body) against what is printed; shows the head at Pose |
| Import G-code | G-code from other slicers or a .dxr file as a toolpath, to check it |
| Material Field | The toolpath as a direction-dependent material field for FE or IGA (CSV, JSON, VTK) |
| Extrusion and Speed | Points, planes, extrusion, speed, role and layer per point |
| Ginger G-code | Full-size G-code for the Ginger One with its start and end sequence |
| Desktop Test Print | The path scaled down for a desktop printer, with that printer's start, layer change and end |
| Machine | Built-in machines and your own from `Documents/LARGER/Machines` (.machine text files), or typed in here |
| Machine G-code | G-code for the machine you connect |
| Quick Start | Puts five worked examples on the canvas |

**LARGER > DXR**

| Component | What it does |
|---|---|
| Machine Settings | Temperatures and cooling for the start and end sequence |
| Toolpath to Robot Lines | One PTP line per path point with one fixed tool orientation |
| DXR Generator | Robot lines, extrusion and speed to a DXR file for the Weber DXR |

**LARGER > Robot**

| Component | What it does |
|---|---|
| Robot | The arm: KUKA KR120 R2700-2 by default, or any six-axis arm from a Robots library file |
| Robot Tool | Tool centre point and weight |
| Robot Cell | Robot, tool and the work base the program counts from; optional Environment and External Axes |
| External Axis | A rail the robot stands on, or a turntable the part stands on |
| Robot Program | Robot moves with the real tool orientation, reach, axis and collision check (Check Collisions, Clearance in mm), lines for the DXR Generator, KRL program |
| Robot Simulation | The robot with its tool at any moment of the print (slider; no Play switch in Grasshopper 2 yet); Colliding shows what collides |
| Cell Geometry | The print cell of the FH Muenster in 3D, the place of the robot and the environment for the collision check |

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

Not ported yet: Infill Sketch, Machine Profile with own factors,
the curve-based Toolpaths tab, and the DXR health check and post-processor.
