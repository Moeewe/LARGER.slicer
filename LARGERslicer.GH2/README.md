# LARGER.slicer for Grasshopper 2

First version (0.1.0) of the slicer for Grasshopper 2 in Rhino 9. It uses the same slicer
core as the Grasshopper 1 plug-in: the files in `../LARGERslicer/Toolpaths` are compiled into
this assembly, so both versions cut, fill and check in exactly the same way.

## Components

**LARGER > Slicer**

| Component | What it does |
|---|---|
| Easy Slicer | Model in, checked path out: the whole chain in one component |
| Print Settings | Bead width, layer height, speed, machine profile |
| Slice | Flat, angled, non-planar (guide surface) or multi-axis (guide curve) layers |
| Walls | Wall centre lines, one bead spacing apart |
| Bottom Fill | Solid bottom as one line |
| Infill | Ribs, honeycomb, grid, wave, truss, meander, chamber wall, truss wall |
| Continuous Path | All layers as one path; layered with a short ramp, or spiral |
| Round and Lead | Rounded corners, skirt before the part, exit move after it |
| Path Check | No crossing, every bead supported |
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
| Robot Cell | Robot, tool and the work base the program counts from |
| Robot Program | Robot moves with the real tool orientation, reach and axis check, lines for the DXR Generator, KRL program |

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

Not tested in Grasshopper 2 yet: Quick Start, Machine, Machine G-code, the brim and the file names (they compile; the code they share with the Grasshopper 1 plug-in is tested on the bench).

Not ported yet: Infill Sketch, Machine Profile with own factors,
the curve-based Toolpaths tab, and the DXR health check and post-processor.
