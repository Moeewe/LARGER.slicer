# LARGER.slicer – features

Content basis for the website. Every item is backed by a component or the changelog. Status:
**released** = in 1.3.1 (the version on the Rhino package server and on GitHub main), **next** = version 2.0, on the branch
`bugfixes-1.3.2` ("[2.0.0]" in `LARGERslicer/CHANGELOG.md`, not released yet), **idea** = not built yet.

## What it is

A slicer for large-format 3D printing (pellet extruders, robots, gantries) and a toolbox for 3-axis milling,
inside Rhino and Grasshopper. Developed and tested at FH Münster by Moritz Wesseler (research associate);
so far used mainly for the internal prints of the lab. One continuous print line is the idea behind every
path: no stops, no travel moves, checked before it reaches the machine.

- Grasshopper 1 (Rhino 7, 8) and Grasshopper 2 (Rhino 9), macOS and Windows – released
- One slicer core for both, tested outside Rhino on a console bench – released
- Installers in `SETUP` (Mac and Windows) that also bring the robot library – released

## Start without knowing everything

| Feature | Components | Status |
|---|---|---|
| Quick Start: eight complete examples on one bed (flat with ribs, non-planar, multi-axis, angled, chambers, Easy Slicer, the non-planar head on the Ginger One, a textured vase) | Quick Start | released |
| Easy Slicer: model in, checked print path out (slice, walls, bottom, infill, one line, round corners, skirt, exit, check) | Easy Slicer | released |
| Design Rules before modelling: what bead width and nozzle allow, how thick to model a wall | Design Rules | released |
| Reports in plain words on every check component (what is wrong, what to change) | all checks | released |
| House definition version 2: the three workflows of the lab (Ginger, robot multi-axis, own robot path) with LARGER.slicer only | `SETUP/…V2.gh` | released |
| Extrusion factor test: walls with three factors per machine, measure, get the new factor | `SETUP/LARGERslicer Extrusion Factor Test.gh`, Material Factor | next |
| Corrected first house definition (extrusion of the custom path, end move, file checks, plug-in list) | `SETUP/…corrected.gh` | next |

## Machines

| Feature | Components | Status |
|---|---|---|
| Ginger One (Marlin and Klipper): full-size G-code with its start and end sequence, 3D model, Printer Simulation with collision check | Ginger G-code, Machine G-code, Printer Simulation | released |
| Weber DXR25 on a KUKA KR120: DXR files, temperatures of all zones, file health check | Robot Program, DXR Generator, Machine Settings (Extended), DXR File Health Check | released |
| Own robot kinematics (any six-axis arm with spherical wrist), tool, cell; KRL program; simulation | Robot, Robot Tool, Robot Cell, Robot Program, Robot Simulation | released |
| Rail and turntable | External Axis | released |
| The FH Münster cell in 3D (floor, pedestal, bed, fence, glass, walls); own cells from a .3dm | Cell Geometry | released |
| Your own machine: a `.machine` text file plus a `.3dm` with the layers Frame, Bed, Z, Y, X, TCP; it moves in the simulation | Machine, Machine Profile | released |
| Desktop test print: the same path scaled down for Bambu Lab, Prusa, Creality, Elegoo | Desktop Test Print | released |
| Extrusion as numbers, not code: E = section × length × flow × factor per machine | Machine Profile | released |
| Materials with start values (PETG, rPETG, PLA), own materials, material factor from measured widths | Material, Material Factor | released |
| Robots hanging from the ceiling or on a gantry rail, safety zones beside the fence for braking distances | – | idea |
| Connect a new machine in minutes (tutorial, MCP server) | – | idea |

## Slicing and paths

| Feature | Components | Status |
|---|---|---|
| Slice any geometry: Breps, surfaces, SubDs, meshes, open or closed | Slice | released |
| Non-planar layers along a guide surface, multi-axis along a guide curve, angled (45°) printing with sacrificial start/end layers | Slice | released |
| Follow the surface: rings on the part's surface as the printed head of 2024, with the measured bead law | Slice | released |
| Walls inside, centred or outside; bottom and top fill (zigzag, Fermat spiral) | Walls, Bottom Fill | released |
| Infill as part of the one line: ribs, honeycomb/grid cells, waves/truss/meander, chambers, drawn infill | Infill Ribs, Infill Cells, Infill Wave, Infill Chambers, Infill Sketch | released |
| One continuous path: layered or spiral, seam modes, bridges between shells, min layer time, max volume rate | Continuous Path | released |
| Round corners, skirt, brim and exit move | Round Corners, Start and End | released |
| Textures in the wall (knit, pixels, ribs, waves, loops, a picture) | Surface Pattern | released |
| Self-supporting check, envelope and best build angle; support fins | Self Support Check, Self Supporting Envelope, Best Angle, Support Fins | released |
| Curve tools: continuous fills from your own outlines | Curve Fill …, Curve Continuous Path, Curve Bridge … | released |
| Extrusion and speed from the local layer height and bead width | Extrusion and Speed | released |
| Weave: beads of two neighbouring walls grip into each other (shift, width, snake, teeth), 14 patterns compared, test series with protocol | Weave Zones, Weave, Weave Variants, Weave Test | next |
| Reshape: same layers and material, more print height where the part needs it (fanned layers, wider beads, extra layers), eight variants compared, also for finished G-code and .dxr | Shape Field, Reshape Path, Compare Variants, Reshape Code | next |

## Checks before the machine

| Feature | Components | Status |
|---|---|---|
| Path Check: crossings, support, bead shape, travel, layer times, beads into the bed | Path Check | released |
| Non-planar check for a vertical nozzle (bead height, head collision) | Non-Planar Check | released |
| Multi-axis check along the real tool axis (bead heights, tilt, turning speed, flow) | Multi-Axis Check | next |
| Robot: reach, axis ranges and speeds, collisions of robot, tool, axes, printed part, bed and cell | Robot Program | released |
| Print bed: every path on the bed and below the machine's height | Print Bed | released |
| DXR/G-code file health check (F0, empty, names, Z) | DXR File Health Check | released |
| Read any G-code (Cura, Simplify3D, PrusaSlicer, AiSync, Weber .dxr) and check it the same way | Import G-code | released |

## CNC milling (Zünd G3)

| Feature | Components | Status |
|---|---|---|
| Zigzag 3D milling with outline, cutter database, PLT (HP-GL) for Zünd Cut Center | CNC - Program, CNC - Cutter Selector | released |
| Board workflow: orient, split into layers, milling depth, raw block, fixtures, bill of materials, export | CNC Utilities 01–08 | released |
| Rough and finish a height model: rings in levels, ball finishing in three axes at once, cut out with tabs, skin or slope; tool change from the magazine; time estimate | CNC - Plate, CNC - Cutter, CNC - Own Cutter, CNC - Rough and Finish | next |
| 13 parametric reliefs to mill (site model, facade, scales, Voronoi, gyroid, labyrinth, topography …) | CNC - Relief | next |
| Quick Start for the Zünd: a topography, first run in foam | CNC - Quick Start | next |

## Good for a "Version 2.0" story

The first house definition mixed native Grasshopper clusters with the Robots and Pufferfish plug-ins. Now
everything the lab prints and mills runs on LARGER.slicer's own components: own robot kinematics, cell,
simulation and collision check; own machines from text files; one path type from the slice to the file;
checks that explain themselves; CNC from relief to PLT. Version 2 of the house definition needs no other
plug-in.

## Where beginners struggle, and what helps already

- Too many components and inputs → Quick Start, Easy Slicer, tooltips on every input, numbered groups.
- Not knowing whether a file is safe → reports that say OK / GO / READY or what to change.
- New machines need code → `.machine` file and `docs/OWN_MACHINES.md`; still too hard (see ideas).
- Extrusion values differ per machine → Machine Profile, Material Factor, the factor test file.
- Missing step-by-step tutorials and videos → idea.
