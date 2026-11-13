# Changelog

All notable changes to LARGERslicer will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed (canvas test after the path-quality merge)
- Path Check, angled layers (Quick Start 05): "CROSSES ITSELF" on the last layer came from the check, not the path. With a skirt the first point of the path is the end of the approach, which lies flat on the bed (tool axis Z); the check took its plane from that point and tested tilted layers in plan view, where the thin top layer's ramp and its far side overlap. Every layer is now tested in its own plane, taken from a printed point of the part. The place a crossing is shown at was computed with the other move's parameter (off by up to 20 mm); it now lies on the crossing. A move that only starts or ends on another one (within 0.001 mm) touches it and is no crossing; two moves one short step apart are tested too (they were skipped as neighbours).
- Start and End, exit: the move off the part is tested against the last layer (no crossing, half a bead clear): away from the middle, else square to the last move to either side, else straight on; if none is free (a top layer thinner than a bead) the nozzle lifts first and then moves away. Bench: leaning block at 30/45/60 degrees, bead 4/5/8, 1 and 2 walls, exit flow 0 and 1: 36 of 36 without crossings.
- Continuous Path, several walls without bridges (with ribs, or Bottom Fill + ribs as in Quick Start 01): the move over to the next wall could run back across the closing pass of the wall before (layer 3 and every second layer above, Walls 2-3 + Bottom Fill). The next wall now starts where that move is clear of the layer printed so far and of the wall itself, away from corners. Closed loops around almost nothing (mean width under a tenth of a bead: where a tilted layer only stands on the bed) are left out like other slivers; rounding their ends made them cross. Bench (Rhino, sliced Breps): rectangle, L, the Quick Start hull, circle and a box at 602/567, walls 1-3, bottom 0-4 (Auto), with and without ribs, layered and spiral: 300 of 300 without crossings after Continuous Path, Round Corners and Start and End (before 38 with crossings).
- Support Fins: fins start where a layer reaches more than half a bead beyond the one below (plus 0.02 bead against slicing noise), the rule of Path Check and Self Support Check. Before they needed 0.6 bead (3.0 mm for 5 mm), so the 55-degree block at angle 0 (2.86 mm per layer, Self Support Check 27 % unsupported) got no fins; now 4 fins. Start and End no longer says "the part does not start on its outer wall" when it does, in a hollow between fins: a remark says why the move from the skirt is a travel move. A skirt end aimed at a start in a hollow (clear straight line) now prints the link as intended.
- Machine G-code: non-planar layers on a gantry (Ginger) are allowed: a grey remark instead of a warning. Errors stay for what the machine cannot do (tilted tool on a gantry, non-planar on a belt).
- Background calculation (Grasshopper 1): while a component upstream still computes, the components below show a grey "waiting for Slice (still computing)..." instead of red errors such as "Connect the Layers output of Slice" (Slicer and Robot components).
- Round Corners: Min Radius 0 now means half the bead width (2.5 mm for a 5 mm bead), at least half the nozzle when the nozzle is given. The Ginger handbook asks for a corner radius of at least half the nozzle; a bead is at least 1.5 nozzles wide (CEAD, Ginger), so half the bead is 0.75 nozzle or more. A 90-degree corner now moves about 0.7 mm inwards instead of 1.4 mm (bench, rectangle; 2.1 mm on the canvas with the old radius). Round Corners has a row "Min Radius: auto 2.5 mm" (Quick Start and every new one), Grasshopper 2 shows the radius as a remark. Design Rules says which radius Round Corners uses.
- Round Corners: "N wall corner(s) have too little room" no longer counts the step to the next layer. On a wall that leans over (the 55-degree block sliced flat: 2.86 mm per layer) the path jogs sideways at every layer change, an S-bend that cannot hold two full arcs; each layer was reported as a wall corner (49 on the block with R2.5 and with R5 alike, so not new with the smaller radius). Slope changes of the seam ramp are not counted either. The arcs themselves are unchanged. Quick Start: 03 19 -> 9, 07 15 -> 1, 08 31 -> 15; 05 keeps 12 (the thin start layers of the 45-degree block are narrower than a bead).
- Grasshopper 2 (tested on its canvas in Rhino 9.1): component icons are drawn (all icons were empty and Grasshopper 2 showed lettered stand-ins: its PixelIcon refuses the 24 and 48 px images of one icon together); Machine, Robot Cell, Surface Pattern and Material Factor no longer fault when an optional list input is left open; Quick Start sets its values (Ginger profile, ribs, tool) on the typed inputs, which it silently skipped; presets for Surface Pattern (Pattern, Side), Machine, Material, Support Fins Contact and External Axis Kind; Walls shows its progress and stops when the solution is cancelled.
- DXR Generator (Grasshopper 1): while Robot Program still computes in the background it shows the grey "waiting for Robot Program (still computing)..." instead of the red "Robot Path input is required" (Quick Start output B).
- Grasshopper 2 Quick Start: the eight examples of Grasshopper 1 on the current components (Infill Ribs, Round Corners, Start and End instead of the hidden Infill and Round and Lead), with 06 Easy Slicer, 07 the non-planar head on the Ginger One with Printer Simulation and 08 the vase with Surface Pattern; Print Bed checks all eight paths.
- Robot: the 3D model of the arm is also found in Documents/Robots and ~/Robots (where the installers in SETUP put the Robots library), not only in Documents/RobotSystems.
- scripts/generate_icons.py: the example path for Grasshopper 2 icons (`ICON_OUT=../LARGERslicer.GH2/Icons/48`, run from LARGERslicer/).

### Changed (Quick Start and examples, tested on the visible canvas)
- Quick Start has eight examples. Example 07 is now a head sliced along its surface as the head of 2024 (Slice: Follow the surface, its own settings: bead 4, layer 1.75, first layer 0.9 mm at 25 mm/s), played on the Ginger One in Printer Simulation (fed by Simulation of Machine G-code, with Play). It stood where the angled block of example 05 leans over and their paths met at Z 96; the head now stands in the free middle of the bed. New example 08: a vase with waves from Surface Pattern. The Ginger examples print their first layer at 25 mm/s. Machine Settings no longer touches Robot Program.
- Print Settings: a row for First Layer Speed.
- CNC - Program: Tool Spindle Speeds is optional, as its description says (the wrong input was marked optional).
- Example files: new `LARGERslicer - Ginger One Printer Simulation.gh` (a planter on the Ginger One: G-code, Printer Simulation with Play, collision check). Head (Ginger One): Printer Simulation and report panels, one Ginger in the viewport. Head (Robot): report panels. Non Planar Head (Ginger) and Surface Patterns: tidied (no overlapping blocks, settings by wireless links, full input names), Printer Simulation on the Ginger head. Zünd PLT: the Cutter Selector goes into Cutter again (it had slipped into Tool List).

### Fixed (corners and the way into the part)
- Continuous Path: corners and straight edges stay exactly where the wall is (max. 0.012 mm off the wall contour in the bench of rectangle, L and star, 1 and 2 walls, centred or inside, every seam mode, spiral; before up to 1.0-2.5 mm). Causes: the point thinning (contour cleaning in Continuous Path, and Start and End) left out corner points closer than the point spacing to the point before, cutting the corner on every layer; it now only leaves out points whose line stays within 0.02 mm. A bridge between two walls could stand on a corner (Clipper rings offer only their corners as places) and cut it on every layer; bridges now go where both walls run on without a corner for 1.5 bead spacings to either side.
- Round Corners: corners are rounded in the plane of their layer, keeping the path's own height, and the slope changes where a seam ramp starts or ends are smoothed separately. Before, a ramp next to a corner was merged with it and widened the arc beyond the radius (2.23 mm instead of at most 2.07 mm for R 5 at 90 degrees; up to 0.5 mm off a straight edge beside a seam in a corner).
- Skirt, brim and the first layer: every ring ends one bead before its own start (the round bead ends just touch), the link to the next ring is square to both and lands where that ring starts, so the links form a staircase one bead apart; the innermost ring ends right beside the start of the part. The first wall (layered) and the first flat spiral loop leave the same one-bead gap at the seam and the next layer climbs away from its end, so no move crosses the gap. A ramp that grows from nothing no longer prints its first move at full height. The inner brim returns to the wall without a bead. Double coverage of the first layer (0.1 mm raster, overlap 0): layered + skirt 21.1 -> 0.0 mm2, brim outside 33.1 -> 2.9, brim inside 35.1 -> 0.0, both 44.4 -> 2.2, skirt + brim 33.7 -> 2.2, spiral + brim 43.3 -> 3.7, follow-the-surface head + brim 7 18.9 -> 3.5 (what is left: slivers of a few hundredths of a millimetre along the round corners of the rings).

### Added (from the stand-alone app)
- Continuous Path: input **Short Lap Time** (Grasshopper 1 and 2, `PrintSettings.ShortLapTime`): two layer times as on the printed head of 2024 - laps shorter than min + 0.7464 x (max - min) of all lap lengths get Short Lap Time, the longer ones Min Layer Time, changing over 5 laps (the head: 35 s and 40 s). Path Check measures every layer against its own minimum.
- Follow the surface: on a part closed at the top the last lap runs from the last ring into its middle at half a layer height (as the app and the printed head), so the crown is closed.

### Changed (number rows, tab order, background)
- Number rows on the components no longer open a long list of numbers: drag the row left or right (whole range over about 250 px, Shift = finer, one solution when you let go), click it to type any value in a box over the row (`45`, `12,5 mm`, `45°` are read), or use the arrow on the right for a handful of round values and the default. A bar shows where the value sits in its range. Angles (Slice, Easy Slicer, Self Support Check, Best Angle) offer 0/15/30/45/60/75/90°, counts (walls, bottom/top layers, skirt and brim loops, start/end layers) whole numbers only; lengths show mm. A wire into the input still wins.
- Slicer tab in seven ordered groups: 1 Start, 2 Layers, 3 Walls and fill, 4 Path, 5 Check (now with Material Field), 6 Output (now with Import G-code), 7 Curve tools: only Layers to Curves, Curves to Layers and the Curve ... components. Align Curves, Alternate Curves and Join Open Contours (before in a separate "Curve Tools" panel) are in group 7 too, but hidden from the panel (obscure): find them by search. Grasshopper 2 uses the same groups 1 to 6 (it has no curve tools). GUIDs and names are unchanged.
- Long calculations in the background (Grasshopper 1): Slice, Continuous Path, Surface Pattern, Material Field, Self Support Check, Non-Planar Check and Robot Program calculate beside the canvas (`Components/AsyncSolve.cs`), show "computing 3.2 s", start over when an input changes and hand out the result when done; errors become messages. Row `Background: on/off` and a menu item per component, kept in the file. Grasshopper 2 (which already solves beside the canvas): these components and Easy Slicer report progress between their steps and stop early when the solution is cancelled.

### Added (non-planar: follow the surface, as the head of 2024 was printed)
- Slice: new Guide Mode **"Follow the surface (no guide)"** (Grasshopper 1 and 2; core in `Toolpaths/FollowSurface.cs` and `NonPlanarFromPart.cs`). For closed parts such as a head: rings on the part's surface from the ring it stands on to the crown, from the way along the surface (shortest paths on the mesh, bottom ring to crown, smoothed, a third pulled towards the level of the height on the wall), under chin and nose as well, closing the crown; the first two layers flat (first layer height, then one layer height higher); contours are added where the surface lies nearly flat, so the rings there follow it. Analysed from the G-code "UNIKAT HEAD V2" (Ginger One, Marlin, July 2024) and its definition.
- **Bead height law of the printed head** (`BeadLaw`): t = h (1 - (h/w)(1 - sin a)((w/h) - cos a)) = dz + (h - dz) dxy / w, from the closest point of the ring below in the path itself; it fits all 18 041 points of the 2024 G-code to 0.0006 E/mm. Used by "Follow the surface" (E = factor x w x t x length) and re-measured after `Surface Pattern`.
- Example `LARGERslicer - Head (Ginger One, non-planar).gh` rebuilt: the head exactly as printed in 2024 (`Head.3dm`, NurbsHead turned about 75 degrees and scaled about 1.74 / 1.56 / 1.83, bottom on Z 0), Follow the surface, one wall centred on the surface, spiral, Min Layer Time 40 s, first layer 0.9 mm at 25 mm/s, brim of 7 beads, Overlap 0. Against the 2024 G-code: 123 / 119 laps, Z 0.9-177.4 / 0.9-178.0, E 48 621 / 48 353, first layer 2628 / 2627 mm at F1500 and E/mm 0.756 / 0.756, 87 / 81 min. Path Check and Non-Planar Check OK.

### Changed
- Start and End, outer brim: the loops no longer all end at the same place. Their links into the next loop lay side by side 0.7 bead apart (the "V" of the 2024 head, where the brim ran into the part, 28 % overlap); now each loop ends half a bead further on than the one inside it, a staircase (at most about a tenth of a bead touches).
- Spiral: the flat first loop stops one bead before its own start (as the foot ring of the 2024 head), the first climbing loop rises from nothing over that gap; the flat closing pass is left out on a ring smaller than three bead widths round (the crown).
- Min Layer Time: the brim and skirt count for the time of the first layer (they are printed right before it), and the first layer is slowed from its own First Layer Speed, not from Speed. The head's foot ring no longer drops to 2.7 mm/s.
- Follow the part / Follow the surface: the first layer lies flat on the bed at the first layer height; the centre the rings are measured around is moved inside every contour when the mean centre lies outside some (the small rings of a crown).

### Calibrated on the 2024 head (it printed fine)
- Path Check: for laps that lean on the lap below with the bead law ("Follow the surface"), offsets up to one bead width at up to 2.5 % of the points are reported as "Support OK for leaning laps" (the 2024 head: 1.8 % of its points, up to 4.0 mm).
- Non-Planar Check: measures the bead height by the law when the extrusion follows it (also for imported G-code); for such paths it accepts up to 0.5 % of the points above 0.8 x bead width (at most 1.1 x) and 1 % below 0.4 x layer height (2024: 0.3 %, up to 1.0 x, and 0.1 %); a nozzle that grazes the flank of a bead by up to 0.6 mm (`Head.Graze`) is reported, not counted as a collision (2024: about 70 moves, up to 0.5 mm; 12 moves deeper, up to 1.7 mm, on the imported G-code with its turn-by-turn layering).

### Added (surface patterns)
- `Surface Pattern` (Slicer tab, Toolpaths group, Grasshopper 1 and 2; core in `Toolpaths/SurfacePattern.cs`): textures in the wall. Every layer is resampled to the pattern pitch and its points are pulled out along the wall normal in a rhythm that moves on from layer to layer: Knit (pixels, rows alternate), Ribs, Waves, Zigzag, Dots, Loops (with Z Droop and Extra Flow the bead sags into loops) and Image (a grey picture wrapped around the part). Optional Region (closed curves unrolled with the wall, Breps, meshes) with Falloff, Attractors with Reach, Side (outside / inside), Invert. Around closed walls the pitch is fitted to a whole number of repeats and followed from layer to layer. The path stays one line: seam ramps, bridges and infill are untouched, the pattern fades out at the seam, pulls that would cross the path are reduced, the first layer stays plain. Outputs Preview, Hanging (points more than half a bead beside the layer below, on purpose for pixels and loops) and Info.
- Example `EXAMPLE FILES/LARGERslicer - Surface Patterns.gh`: six vases on one bed (knit, ribs, diagonal waves, hanging loops, a lattice picture next to the file, a region drawn on the part), each with Start and End and Path Check (0 crossings).
- Icons: `Support Fins` redrawn in the family style and a new `Surface Pattern` icon, both from `scripts/generate_icons.py`.

### Added (first layer speed)
- `First Layer Speed` on Print Settings (Grasshopper 1 and 2), in mm/s, 0 = Speed: the first layer runs at its own speed for adhesion (the Ginger head of 2024 used 25 mm/s = F1500). Min Layer Time can only make it slower. File headers name it.

### Added (Ginger One in 3D, Printer Simulation)
- The Ginger One comes with a 3D model (`Resources/GingerOne.3dm`, spec in `Resources/GingerOne.README.md`): frame with the real corner nodes, bed 1000 x 1000, gantry, beam and head with hopper, auger, barrel and heater bands, in their colours, on the layers Frame, Bed, Z, Y, X and TCP. Machine shows it, Machine G-code shows it with the nozzle at the start of the path (new output `Model`), and a machine saved from a Ginger keeps it.
- `Printer Simulation` (Slicer tab, next to Machine G-code): Machine, Toolpath, Pose (0 to 1) and Play; the gantry, beam and head move so the nozzle tip stands on the path, the printed part grows up to Pose. Your own machine moves too when its .3dm has the layers Z, Y, X and TCP.

### Changed (speeds in mm/s, machine names)
- Speeds are entered in mm/s (Print Settings: Speed 50, Min Speed 5, Max Speed 200 mm/s; Easy Slicer: Speed 50 mm/s); the path and the machine files keep mm/min, the F of G-code and DXR (50 mm/s = F3000). Definitions saved with an older version are read as mm/min, with a note. Reports name speeds in mm/s. The per-point outputs for the machine are called `Feed` (F, mm/min). Also in Grasshopper 2.
- Machines and machine profiles are listed as manufacturer and machine: "Ginger Additive · Ginger One (Klipper)", "Weber Additive · DXR25 on a robot", "Generic · Filament printer 1.75 mm". Own .machine files can carry `manufacturer = ...`.
- Nozzle presets are named by the nozzle: "3 mm flat nozzle, wide bead - bead 5 mm, layer 2 mm (tested)", "4 mm nozzle (Weber profile) - bead 4.8 mm, layer 1.8 mm". The numbers are unchanged.

### Added (non-planar head example)
- Quick Start example 07: layers that follow the part on the Ginger One (Klipper), one spiral, Max Volume Rate 330 mm3/s, Path Check, Non-Planar Check and G-code.
- Non-Planar Check: `Pose` input and `Head` output, the head as it is checked (tip, cone, body) standing on the path.
- Example files `LARGERslicer - Head (Ginger One, non-planar).gh` and `LARGERslicer - Head (Robot, DXR25).gh`: the NurbsHead of the 2024 print "UNIKAT HEAD V2" (1.75 x, 174 mm), layers that follow the part, one spiral; Ginger One (Marlin) with the values of the 2024 G-code (bead 4, layer 1.75, first layer 0.9, 11 mm/s, 35 s per lap, factor 0.21), and the KUKA KR120 with DXR25 in the FH Muenster cell (bead 5, layer 1.5, reach and collision check OK).

### Added (collision check for multi-axis and angled prints)
- `Robot Program` checks the robot, the tool, rail and turntable against each other, the part printed so far, the bed and the Environment: new inputs `Check Collisions` and `Clearance`, outputs `Collision Moves` and `Collision Bodies`; `Robot Simulation` shows what collides in red. The report names the pair, the move and the layer, and suggests what to change (tilt, nozzle length, tool turn, rail or turntable offset). Bodies that stand on something (robot foot, carriage, table) may touch it by a few mm while they slide. Tested with the cell of the FH Muenster (KR120, AE20 extruder): free tower 0 hits, box over the part, tool leaning 45 deg over the printed part, 80 deg at the bed, rail and turntable without false alarm; Quick Start without false alarm and faster than before (64 s instead of 107 s for the whole demo).
- Example `LARGERslicer - Non Planar Head (Ginger).gh` (EXAMPLE FILES, Weber - Ginger): layers that follow the part on the Ginger One with Klipper.

### Fixed (after the canvas test)
- Path Check: the seam closing onto its own start no longer counts as a crossing after Start and End thinned the path.
- Non-Planar Check: the wall next to a fin or the first bead of an overhang no longer reads as a squashed bead (a bead of the same layer only carries a point where it lies clearly lower).
- Start and End: the info no longer shows a negative number of exit points.

### Removed
- Copyrighted manufacturer manuals (Ginger operating manual, Zünd HP-GL manual) are no longer in the repository.

### Added (non-planar for a vertical nozzle, Ginger on Klipper)
- Slice: new Guide Mode **"Follow the part (no guide)"**. Non-planar layers follow the part's own wall from bottom to top (meridians, N = mean wall length / layer height), so overhangs get layers of even thickness without drawing a guide surface. The nozzle stays vertical and the extrusion follows the vertical gap to the layer below - not the 3D distance, which over-extrudes by 15 % at 45 degrees and 43 % at 60 degrees. Warns when a layer is thicker than 0.8 x bead width along the wall.
- **Non-Planar Check** (Slicer, next to Path Check): measures the real bead height at every point against bead width, layer height and extrusion, and checks the head (tip radius, cone angle and length, head radius) against everything printed before and the in-layer slope against the cone ("HEAD COLLISION at move i, layer k, with layer m"). Tested on a 200 mm vase with a 40 mm bulge: 101 layers, bead height 0.8 to 2.0 mm, E per layer within 0.07 % of the measured gap, head clear.
- Built-in machine **Ginger One (Klipper)**: START_PRINT/END_PRINT macros, E as volume x 1.0 (confirm with the Klipper rotation_distance), fan on after layer 1, M107 at the end. Own machines move down one place in the Machine list.
- .machine files: keys `material` and `bed temperature` for `{material}` and `{bed}` in the codes, and blocks `[after layer n]`. Machine component: optional inputs Material and Bed Temperature.

### Added (seam, layer time, volume rate)
- **Seam Mode** on Continuous Path (Grasshopper 1 and 2): Fixed point (as before), Inner corner, or Sharpest corner. The seam and its ramp go into an inner corner of the outer wall, where they show least and milling does not run over them; without one, to the sharpest outer corner. Followed from layer to layer within half a bead, never at the cost of a move without extrusion. L and star parts: seam in the inner corner on every layer, stack step 0 mm.
- The skirt also runs straight into a part that starts in a hollow of its outline.
- **Min Layer Time** (Continuous Path): layers that would be done sooner are printed slower, constant within the layer, never below Min Speed. **Max Volume Rate** (mm3/s, about 330 for the Ginger): moves that would ask more of the extruder run slower.
- **Path Check**: Min Layer Time input and Layer Times output. Too-short layers get three remedies: the speed that reaches the minimum, how many parts side by side would, or a pause as a last resort (and why it harms a pellet extruder).

### Fixed
- Path Check no longer fails when the path is not printable for a reason without a known prefix (e.g. INTO THE BED).

### Added (self-supporting geometry)
- **Self Support Check, Self Supporting Envelope, Best Angle** (Slicer, next to Slice; Grasshopper 1 and 2). Self Support Check colours the part by how far it hangs over for the chosen build direction (flat, angled, guide curve or guide surface); the limit comes from the bead (half a bead beside the bead below: 5 mm beads in 2 mm layers allow 51 degrees). Self Supporting Envelope adds the least material that makes every layer hold the one above: slice-ready layers, the new part as a closed NURBS Brep (or a mesh) for analysis (IGA), and the added material alone to mill away later. Best Angle tries slice angles 0 to 75 degrees in every tilt direction and suggests the one with the least overhang. Tested: the mushroom part goes from "NOT SUPPORTED, offset 50 mm" to "Support OK" with 417 cm3 added; Best Angle picks 55 degrees for the 55-degree lean block.

### Added (toolpath back into the analysis)
- **Material Field** (Slicer, Grasshopper 1 and 2): puts the toolpath back onto the part as a direction-dependent material field for IGA or FE. Per sample point: bead direction e1, across the bead e2, stacking direction e3, layer and bead, distance to the next layer and bead interface, bead spacing and overlap, layer time and the time across the interface, and a weakness value. With the moduli it gives the rotated orthotropic stiffness per point. Samples are a grid in each Brep face's UV, so (face, u, v) maps onto the NURBS of the analysis, or your own points such as Gauss points. Save writes CSV, JSON and VTK (ParaView). The default material values are placeholders until coupon tests give real ones.

### Added (support fins, materials, G-code import)
- **Support Fins** (Slicer, next to Infill; also Grasshopper 2): thin fins outside the part hold up overhangs. Each stands square to the outer wall from the bed up to just under the overhang, exactly on the fin below, and is part of the one continuous line: a hairpin out of the wall and back, touching the part through a narrow neck so it cuts off easily. Contact "Neck + gap" prints the top fin layer 0.3 mm lower so the overhang breaks off. Spacing and reach in bead widths.
- Continuous Path: the seam ramp stays off ribs and support fins, so the move to the next wall never cuts through a hairpin.
- **Material** (Slicer): start values for pellet materials - feed zone, heat zones, nozzle, bed, drying, residual moisture, density. Built in: Carbon-P PETG, rPETG and PLA with the start values of the Weber quick guide (densities typical). Own materials as `.material` files in Documents/LARGER/Materials.
- **Material Factor**: new extrusion factor from measured bead widths (new = target / measured x current), with a warning for uneven measurements.
- **Import G-code**: reads G-code from Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build and scripts, and Weber .dxr, as a Toolpath, and checks it with the same Path Check: starts and stops, travel moves, retractions, F changes and F0, extrusion into the bed, unsupported beads, crossings, time and weight. Leaning layers and A/B/C tool axes are recognised. On the Weber examples: Hopper 3 lines, 3DBenchy 2268 separate lines; it also found that the 45 degree cube .dxr of the start package has Z values without their decimal point (Z.566 became Z566).

### Added (external axes: rail and turntable)
- New **External Axis** component (Robot, Grasshopper 1 and 2): a rail the robot stands on, or a turntable the part stands on - range, top speed, KUKA name (E1 to E6), endless table, offset, smoothing, coupled base, drawing size. Robot Cell has a new optional input External Axes (at the end, old files still open).
- Robot Program moves rail and table along the print: the rail keeps the robot beside the point being printed, the table turns that point towards the robot; smooth, below top speed, inside the range, never a jump of a full turn. The report lists per axis the range used, the speed needed and points beyond the range. KRL writes E1/E2 on every line; a turntable in the fixed base or a coupled base (EK).
- Robot Simulation shows the rail carriage, the turning table, the robot on its moved base, the printed path turning with the table and the cell. Tested in Rhino 9: a 6 m wall that is out of reach becomes reachable with a 3 m rail; a cylinder of 10 turns on an endless table keeps axis 1 within 0.4 degrees (25.7 degrees without).
- Not yet tried on a controller; DXR output carries no E values; one rail and one table per cell.

### Added (nozzles, Easy Slicer, Quick Start)
- `Nozzle` on Print Settings (Grasshopper 1 and 2) and Easy Slicer: choose the nozzle that is screwed in and get bead width and layer height that suit it. From the Weber DXR profiles of the start package (1 / 2 / 3 / 4 / 5 / 6 / 8 mm -> bead 1.25 / 2.5 / 3.6 / 4.8 / 6 / 7.2 / 10 mm, layer 0.5 / 1 / 1.25 / 1.8 / 2 / 2.4 / 3.2 mm), a tested setting (3 mm flat nozzle, 5 mm bead, 2 mm layer) and wide beads for other flat nozzles scaled from it. A warning says when bead and layer do not suit the nozzle (Weber quick guide: a pointed nozzle lays 0.7 to 1.3 x its diameter, a flat one 1.0 to 1.8 x; layer a quarter to 80 % of the nozzle).
- `Design Rules`: bead range for pointed and flat nozzles, Weber's own bead and layer for the nozzle, and the milling allowance of half a bead.
- `Easy Slicer` does every option of the chain: Nozzle, Solid Pattern (auto, zigzag, rings) for bottom and top, Top Layers, Angle (leaning layers), Start/End Layers, Wall Position, Bridge, and the infill "Solid (rings along the wall)" for solid parts - all as rows on the component.
- The curve tools (Curve Fill Zigzag, Contour, Cells, Euler, Cross Lines, Continuous Infill, Wall and Zigzag, Curve Bridge, Join and Order, Uncross, Align, Alternate, Join Open Contours) moved from the separate "Slicer Curves" and "Toolpaths" panels into the Slicer tab, as its last group next to Layers to Curves and Curves to Layers.
- Quick Start: wireless links instead of long wires - every example gets its own "Settings" parameter fed from Print Settings by a hidden wire (select it to see the wire), the multi-axis robot program its own "Robot Cell". New example 6: Easy Slicer with the FH Muenster nozzle printing a vase as a spiral. 99 objects, no overlaps, six Path Checks OK, six paths on the bed.

### Changed (settings and buttons on the components)
- Grasshopper 1: settings that used to need a value list, a slider or a True/False toggle in front of a component are now rows on the component itself, under its inputs and outputs: a choice (click: menu), a number (click: quick picks or type a value, Enter), an on/off switch and buttons. `Save G-code`, `Save KRL`, `Save DXR`, `Save machine` and `Export cell` write the file once per click and switch back by themselves; `Robot Simulation` has a `Play` / `Stop` row. Rows on Slice (Angle, Guide Mode, Tool Axis, Start/End Layers), Walls, Bottom Fill, Infill, Continuous Path (Mode, Bridge), Start and End (Skirt, Brim), Print Settings, Easy Slicer, Machine, Cell Geometry, Robot Program and the output components. The value goes into the input as if typed into it, so the file keeps it and a wire into the input still wins (the row then says "connected"). Quick Start places no value lists or toggles any more: 87 objects, no overlaps, all Path Checks OK.

### Added (start and end feature)
- `Slice` (Grasshopper 1 and 2): input `Start/End Layers` adds sacrificial layers before the part's first and after its last layer, as the CEAD sheet for 45-degree printing recommends (about 5, milled away afterwards). With leaning layers each start layer is a band in front of the edge where the layers begin plus the first layer moved back and filled down to the bed, so the feature stands on the bed and the part's first layer lies on a full layer; the end layers are copies of the last one. Built layer by layer, without joining solids. Tested at 45 degrees on a box, the leaning demo block and a profile cut parallel to the layers: one line, check OK, every bead supported.

### Changed (icons)
- New icons in the style of Grasshopper's own: light bodies and one accent colour per group with a dark edge and a faint shadow, without the dark chip. They read on a light and a dark toolbar and on the grey, orange and green capsules of the canvas.

### Added (45-degree printing, from the boat hull paper and the CEAD and Ginger sheets)
- `Continuous Path` (and `Easy Slicer`): **bridges** after the Continuous-Toolpath Connection Rule of Wesseler, Herrlein and Kiendl. A contour that lies inside another one - the inner shell inside the outer shell, the wall of an opening inside the outer wall - is joined to it by two lines one bead spacing apart; both contours are cut open there and become one line, so the nozzle never moves without extrusion between them. The bridge stands exactly where the bridge of the layer below stood, so the bridges stack into a rib (offset 0.2 to 0.4 mm on the test plates). Two walls one bead apart (a wall of two beads, CO90) are joined by a short turn where that makes the whole layer one line. Input `Bridge` (on by default) switches it off. Measured on 16 test parts with 10 option sets: travel moves inside the print 668 -> 399, no check got worse; a double shell (outer shell and inner chamber, walls CO90) 75 -> 0 travel moves flat and 96 -> 54 at 45 degrees (crossings 14 -> 1).
- `Design Rules`: what bead width and nozzle allow before you model - layer height range (a quarter to half the nozzle), overlap (5 to 10 % of the bead width), how thick to model a wall of two beads (CO90 = 2 LW - OL; CO45 = 1/2 sqrt2 (2 LW - OL) where the layers cross it at 45 degrees), the layer width for a finished wall at 45 degrees (LW45 = sqrt2 t, with milling sqrt2 (t - t milling)), corner radius, double beads and the start and end feature of about 5 layers. The numbers match the CEAD table. Also in Grasshopper 2.

### Fixed (non-planar and multi-axis, checked against the FH Muenster definition)
- Non-planar layers (guide surface) with the tool square to the layer got too much material: the bead height was taken as the vertical gap between the layers, but a tilted nozzle lays a bead as thick as the gap square to the layer, which is smaller by the cosine of the slope (+3 to +7 % at 10 to 30 degrees, +41 % at 45). Measured on the demo part and on a 40 degree guide: the height used for E now matches the real bead thickness within 0.3 %. Multi-axis (guide curve) was right before and is unchanged (within 2 %).
- Non-planar with Flat bottom: the first layer was not flat. It was squeezed with the rest and lay only 0.35 to 0.6 mm above the bed where the guide is low. It now lies flat at the first layer height and only the layers above it bend.
- Robot Program: the turn of the tool about its own axis is now taken by the shortest way from vertical. Before it was the projection of world X, which turns over at 90 degrees of tilt: a multi-axis print that tilts the tool to 90 degrees or past it would have spun the extruder half a turn in one move.
- DXR Generator: the layer marks of non-planar and multi-axis paths were wrong (a bent tube of 275 layers got 287 marks; the FH Muenster definition's own tube of 237 layers got 672). Robot Program now writes the layer of every point into its lines (`; layer 12`) and the DXR Generator uses it. Lines without it (e.g. from the Robots plug-in) are now told apart better (flat only if most points lie in runs at one height), and non-planar layers are found where the path comes back in 3D to where its round began: 237 of 237 for the definition's tube.
- Start and End: a point given twice in a row (where a wall hands over to the seam) made a move of length 0 once per layer; some controllers stop on them. They are removed. Found by a stress test of 16 parts x 10 option sets: 66 of 160 paths had them, now none.
- Path Check reports invalid numbers (stops the machine) and moves of length 0, and for non-planar and multi-axis paths how thick the beads get (a note where they are thinner than 40 % of the layer height).

### Added (your own machines and cells)
- `Cell Geometry` reads your own print cell: a .3dm in Documents/LARGER/Cells (or any file via `File`) with one part per layer - `Bed`, `Robot Base` (a point and a line along the robot's X axis), `Work Base` (optional) and any other layer for the collision check. `Export` writes the cell of the FH Muenster in this form as a start. Also in Grasshopper 2.
- `Machine` shows the machine in 3D: a .3dm of the same name next to the .machine file (or `model = ...` in it), or what you connect to `Model`; `Save` writes it next to the .machine file. Without a model the bed is drawn from `bed = ...`. Also in Grasshopper 2.

### Added (first layer)
- `Print Settings` and `Easy Slicer` (Grasshopper 1 and 2) have a `First Layer Height` input, as the FH Muenster definition has: the first layer can be thinner or thicker than the others, and its extrusion follows its own height. 0 = the layer height, as before. The nozzle always runs one first layer height above the bottom of the part, never on it.
- `Path Check` and every check after slicing: a bead printed on or below Z 0 is reported as `INTO THE BED`, one less than half its height above Z 0 as `TOO LOW`, and the path is not OK. `Slice` says when the part reaches below Z 0 or stands above it. Before, a part sunk into the bed or curves drawn on the bed were printed into the bed without a word.

### Added (Grasshopper 2)
- `Robot Simulation` and `Cell Geometry` in Grasshopper 2; `Robot` shows the arm in 3D, `Robot Program` checks collisions (with the environment of `Robot Cell` and the part already printed) and gives the robot at the chosen pose. `Continuous Path` has a `Seam` input. Quick Start uses the cell of the FH Muenster and plays the program with a slider.

### Fixed (stability)
- `Slice`: the number of layers is rounded to the nearest whole layer. Before it was rounded up, so a part of 58.2 mm in 2 mm layers was printed 60 mm high, almost a full layer above its top. Found by slicing two parts with the FH Muenster definition and with LARGER.slicer side by side (that definition rounds down and leaves out the top layer of a 200 mm part).
- Rhino could crash when Grasshopper 2 solved several definitions at once: its mesher was called on the same part from two threads. Meshing is now done one call at a time, on a copy.
- `Robot Simulation`: where an axis cannot turn fast enough, the time of the move follows the slowest axis.

### Fixed (bottom fill, compared with Bi et al. 2022)
- `Bottom Fill` with Pattern Zigzag: a zigzag that openings split into pieces needed a move without extrusion between the pieces (9 to 11 on three layers of a plate with two openings). Now another direction is tried, and if no direction gives one line, the layer is filled following the wall, with a note. A zigzag that ended in a pointed corner far from where the layer below stopped could make the wall cross its own way into the fill; now the direction is chosen so the fill starts next to where the layer below stopped.
- Grasshopper 2: the inputs and outputs have the same names as in Grasshopper 1 (Machine Profile, Machine Settings, Tool Axis, Cell Name, Filament Diameter, Path).

### Fixed (robot, checked against the definition BeispielRoboter.gh of the FH Muenster)
- `Robot Program` wrote the tool orientation the wrong way round: a vertical tool came out as `A 0, B 0, C 180` in the robot lines and so in the DXR file, while the Weber DXR expects `A 0, B 0, C 0` for a vertical extruder. Targets and TCP now follow the Robots plug-in: the target's Z axis is the layer normal (up for flat layers), the TCP's Z axis points from the nozzle tip back into the tool. A TCP from a Robots definition can be used as it is; the AE20 extruder is a YZ plane at -460, 3, 108. **TCP planes made for earlier versions of Robot Tool must be turned half a turn about their X axis.** The default of Robot Tool is now a straight tool on the flange.
- The robot stands where the definition of the FH Muenster puts it: at 651.5, -1448, 211.1, turned like the bed (`Cell Geometry`, Quick Start; before: 196 mm high and turned by 90 degrees, read from the pedestal).
- Axes 4 and 6 are followed round without a jump. Before, a value that would have passed the axis range was replaced by the same angle one turn back; the report said "within range", the real robot would have stopped at the limit. Now this is reported as NOT RUNNABLE.
- A change of the arm configuration (shoulder, elbow or wrist turning over) between two points is an error: a linear move cannot do it. The solver keeps the configuration where it can.
- A wrist singularity and an axis that would have to turn faster than it can are errors now, not only remarks.
- Long moves (travel moves, the way in) are checked every 10 mm on the way, not only at their ends.
- `$LOAD` is noted in the KRL file also when no weight is given.
- `DXR Generator`: the way in from above is not counted as a layer.

### Added
- Way in from above: every path starts 30 mm above its first point and comes down onto it at print speed (Start and End, Easy Slicer), as the Weber converter does. Before, the machine moved to the start at print height across the bed.
- Very short pieces of path (shorter than a fifth of the bead width) are left out. Rounded corners left pieces of a few hundredths of a millimetre; a machine slows down on them and the extruder leaves a blob.
- Collision check: all links (link 3 and the tool against links 1 to 4 were missing), up to 500 poses spread along the path plus both ends of every travel move, 5 mm clearance, and the tool against what is already printed (except right at the nozzle tip).

### Added
- `Robot Simulation` (Robot tab): plays the robot program. The robot with its tool stands at any moment of the print, `Time` from 0 to 1 following the real time of the print; `Play` lets it run by itself, `Film Length` says how many seconds one run takes on the screen. Outputs the axis angles, the tool tip (TCP), the move and the time. Moving the slider only places the robot, the program is not calculated again. `Robot Program` has the output `Program` for it. In `Quick Start` it is part of output B, with a slider and a Play switch. Example 3 (multi-axis) has a robot program and a simulation of its own, so you can watch the tool lean with the layers.
- `Cell Geometry` (Robot tab): the print cell of the FH Muenster in 3D, as it is drawn in the definition of the FH Muenster: floor, pedestal of the robot, print bed, safety fence, glass and walls, in the coordinates of the print bed. It gives the place of the robot (in the middle of its pedestal, looking towards the bed), the work base and the environment for the collision check; `Robot Cell` has an `Environment` input for it. `Quick Start` uses it, so the robot of example 1 stands where it stands in the lab.
- `Robot Program` and `Cell Geometry` draw their models in colour (robot orange on a dark base, tool steel blue, fence yellow, glass see-through).
- The robot in 3D. `Robot` reads the 3D model of the arm from the library of the Robots plug-in: the `.3dm` beside the library file, or the one in `Documents/RobotSystems` that belongs to the arm (input `Geometry File` for any other place). The seven meshes may lie on the layer of the arm or on its sublayers 0 to 6. `Robot Tool` takes the tool as a `Mesh`, drawn where it sits on the flange; the name `AE20_Extruder` brings the 3D model of the extruder of the FH Muenster cell, which comes with the plug-in. Links whose very small faces collapse when they are moved into a pose are repaired, so no link is left out of the picture or of the collision check. `Robot Program` shows the robot with its tool at the chosen `Pose` (output `Robot`): move the Pose slider to watch it run the path.
- Collision check in `Robot Program`: at up to `Collision Poses` places along the path (200) the tool, wrist and forearm are tested against the base and the first two links and against everything connected as `Environment` (bed, table, cage). The report says how many poses were checked and where the first collision is; output `Collisions` gives the path points.
- `Quick Start`: the tool of example 1 is the extruder of the FH Muenster cell as its definition sets it up (AE20 extruder: nozzle tip at -460, 3, 108 of the flange, pointing along -X, 46 kg, centre of mass -61, -28, 113).
- Your own machines. A machine that prints from G-code is a small text file ending in `.machine` (name, letters for the file names, bed, extrusion factor, and the blocks `[start]`, `[layer]`, `[end]`) in the folder `Documents/LARGER/Machines`. The new component `Machine` lists the built-in machines (Ginger One, a generic Marlin machine) and every file of that folder; it also takes a name, start, layer and end code and an extrusion factor directly and saves them as such a file. `Machine G-code` writes the toolpath for the machine connected to it. For the Ginger One it writes the same file as `Ginger G-code`. The Weber DXR keeps its own components (`Machine Settings`, `Robot Program`, `DXR Generator`).
- File names as the FH Muenster names its files: letters of the machine, date and time, name of the part, weight and print time, for example `DXR_2611051216_Test Object_2668g_53min.dxr` or `GIN_2611051216_Test Object_2668g_53min.gcode`. `Ginger G-code`, `Desktop Test Print` and `Machine G-code` have a `Name` input for the part; `Robot Program` has the output `File Name` for the DXR file. `Print Settings` has the input `Density` (1.25 g/cm3) for the weight. A file name is at most 64 characters and keeps to letters, digits, spaces, minus and underscore.
- `DXR Generator`: inputs `File` and `Save`. An empty `File` or just a name writes to the Desktop.
- `Quick Start` is laid out like the FH Muenster definition: a numbered title in capitals for every example (00 settings, 01 to 05 the examples) with one line that says what it is for, short names on the groups, a yellow panel with the instructions and a yellow panel `Part name` that feeds every file name.
- Brim: beads that lie right against the first layer and widen its foot, so the part holds on to the bed. `Start and End` has the new inputs `Brim` (none, outside, inside, both sides) and `Brim Loops` (3); `Easy Slicer` has `Brim`. The brim is part of the one continuous line. Outside, the loops are printed from the outermost in and the last one runs on into the wall. Inside, the brim is printed when the first layer is finished: the bead runs straight in to the innermost loop, the loops are printed from the inside out, each one the other way round than the one before, and the last one ends back where the first layer ended. The loops leave a lane of two bead spacings open at that place for the way in, so nothing is crossed and nothing moves without extrusion. Skirt and brim can be used together; the skirt then keeps clear of the brim.
  - The inner brim needs free room inside the first layer: with a solid bottom it is left out, with infill it lies in the room where the first layer ends. A note says so.
  - Not available on tilted layers.

### Changed
- Icons: every icon is now a white glyph with its accent colour on a small dark chip with round corners, like the logo. One icon reads on every ground Grasshopper puts it on (light and dark toolbar, the grey capsule on the canvas, green when selected), so the two sets for light and dark and the switching between them are gone. The 26 icons of the older components keep their look.
- Names of inputs and outputs made the same everywhere. `DXR Generator`: `Extrusion`, `Speed`, `DXR`, `Info` (before: Extrusion Amount, Print Speed, DXR File, Process Info). `Machine Settings`: output `Info`. A machine profile is called `Machine Profile` wherever it is connected or chosen (`Print Settings`, `Easy Slicer`, `Machine Profile`), so it is not mistaken for a `Machine` (G-code) or the `Machine Settings` (DXR). `Slice`: `Tool Axis` (before: Tool). `Print Bed`: output `OK`. `Desktop Test Print`: output `Path`, input `Filament Diameter`. `Robot`: `Cell Name`. Planes are `Pl`, paths `P`, flow `Fl` as short names. Saved definitions keep working: the inputs and outputs stay in their places.
- `Robot Program`: the KRL file is named exactly like the program in it (letters, digits and underscore), as the controller needs it; a space in the name becomes an underscore.
- `Continuous Path`: open seam. The flat part of every layer ends one bead width before its own start, and the path crosses that gap on a short incline up to the next layer, at the same place on every layer. Before, the incline was 20 mm long. The input is now called `Seam Gap`; 0 = one bead width, a larger value gives a longer, softer seam. Definitions saved earlier keep the value stored in them.
- `Start and End`: the skirt stays open by one bead width (before: one bead spacing).
- `Easy Slicer` uses the same seam.

## [1.3.0] - 2026-11-12

### Added
- **Robot tab**: `Robot` (KUKA KR120 R2700-2 by default, or any six-axis arm with a spherical wrist from a Robots plug-in library file), `Robot Tool`, `Robot Cell` and `Robot Program`. The program puts the tool tip on every path point with the tool along the path's tool axis, so angled, non-planar and multi-axis paths tilt the tool. It calculates the axis angles of every point (same conventions as the Robots plug-in) and reports points out of reach, axis ranges, wrist singularities and axis speed. Outputs: the moves with X, Y, Z, A, B, C for the `DXR Generator`, a KRL program with tool and base, and the arm at any place of the path.
- `Quick Start`: output B now runs through the robot cell and `Robot Program` into the DXR file.
- `Easy Slicer`: one component from the model to the checked print path (slice, walls, bottom, infill, continuous path, rounded corners, skirt and exit, check) with the settings most prints need. Its report says what was done, what the check found and what to connect next. An infill size of 0 picks a fitting size and reduces it if the part is too small.
- Saving: `Ginger G-code`, `Desktop Test Print` and `Robot Program` write to the Desktop when no file is given; a folder or a name without ending works too.
- `Quick Start` places a "Start here" note with the steps.
- First-steps page: https://parametricsolutions.de/larger-slicer/erste-schritte/
- `Ginger G-code`: full-size G-code for the Ginger One with the start and end sequence of the Ginger quick start guide, relative extrusion and one constant feedrate on every line.
- `Toolpath to Robot Lines`: the toolpath as PTP lines with extrusion and speed for the `DXR Generator`, for flat layers with a vertical tool.
- `Quick Start`: the flat example now ends in the file for the machine (Ginger G-code, and robot lines into a DXR file); the demo parts lie on positive coordinates.
- `Desktop Test Print`: every printer has its own start, layer change and end, taken from the BambuStudio, PrusaSlicer and OrcaSlicer profiles: Bambu Lab P1P / P1S / X1C (layer notifications `M73 L`, `M991`, purge line at the front), Prusa MK4 and MINI, Creality Ender-3 and K1, Elegoo Neptune 4. New presets: Bambu Lab A1, Bambu Lab H2D (start and end left open: no plain start is documented for it).
- Grasshopper 2: `Ginger G-code` and `Desktop Test Print`.

### Added (demos and bed)
- `Print Bed`: shows the bed of the machine in the Rhino viewport (plate, grid every 100 mm, origin) and checks that every connected path lies on it and below the machine's height.
- `Quick Start`: five examples instead of three, standing in two tidy rows on one 1000 x 1000 mm bed: 1 flat layers with ribs and bottom, 2 non-planar, 3 multi-axis, 4 a plate with an opening whose wall `Infill Wave` joins to the outer wall, 5 a block leaning 55 degrees printed with 45 degree layers. New output C: `Desktop Test Print`. The robot stands beside the middle of the bed.

### Fixed
- Found by running 1.3.0 in Grasshopper on a plate with a round opening:
  - `Round Corners`: a U-turn (the path coming back beside itself, at every infill joint) now gets its half circle from the distance of the two lines alone. Before, the cut was searched for and landed on a different place from layer to layer, so the turn hung up to 11 mm beside the one below and `Path Check` reported unsupported beads.
  - `Infill Cells`: a layer whose walls are the same as those below gets the same line, and every joint of the layer below is remembered as a pair of places. A round wall is cut into different pieces on every layer by the slicer; small cells next to it could appear on one layer and be gone on the next.
  - `Robot Program`: `$BASE` in the KRL file is counted from the robot's foot, not from the origin of the Rhino model.
- `Quick Start`: the flat demo part lies further on the bed, so its skirt stays on positive coordinates and `Ginger G-code` does not warn.
- `Easy Slicer`: an empty Geometry input says "Connect your part" instead of a collect error; the report says when walls of openings are only reached by moves without extrusion and which infill joins them.
- `Infill Chambers` on angled layers: what is carried from layer to layer moves straight up with the part.
- `DXR Generator`: every layer is counted. Before, a layer was only seen when the last step of the ramp climbed more than 0.2 mm; where the ramp ended closer below the next layer, that layer got no `layer_sub.nc` call and the number of layers in the header was too low (102 instead of 120 on the demo part). A layer is now a stretch of at least five points at one height, and the call sits on the first point of the ramp.

### Changed
- Icons: no ground behind the glyph. Every icon exists twice, with dark lines and with light lines. The toolbar gets the light lines when Rhino 8 or 9 on the Mac runs in dark mode; components on the canvas always get the dark lines (the canvas stays light). Grasshopper 1 has no icon pairs of its own, so the plug-in chooses. The toolbar keeps what it got at start: after switching the appearance, restart Rhino.
- The Toolpaths section is now **Slicer Curves** and its components are named like their Slicer counterparts: `Curve Fill Zigzag` (was Single Path Fill with Lines), `Curve Fill Contour` (Single Line Fill with Offsets), `Curve Fill Cells`, `Curve Fill Euler`, `Curve Fill Cross Lines` (Curve Boundary Infill), `Curve Continuous Infill`, `Curve Continuous Path` (Continuous Toolpath), `Curve Wall and Zigzag` (Continuous Path from Boundary), `Curve Join and Order` (Optimized Toolpath), `Curve Uncross` (Suppress Self Intersections), `Curve Bridge`. Existing definitions keep working: only names and place changed.
- Slicer toolbar order: settings, slice, walls and fill, path, check and values, output, open interface.
- Angled printing (`Slice` with an angle): every layer now starts in the middle of its lowest edge, on the bed; the skirt goes around the whole footprint on the bed; contours smaller than one bead (where a layer just touches the part) are left out; `Path Check` counts beads that lie on the bed as supported.
- `Infill Wave` on angled layers: sides that climb from the bed keep their wavelength and count their waves from the bed, and a wave grows by a small step per layer instead of appearing at full depth.

### Fixed
- `DXR Generator`: numbers were read and written in the computer's number format. On a computer set to German, outside Rhino's own number handling, `331.682` became `331682,000`. The DXR writer now always uses a decimal point.
- `Infill Sketch`: a sketch line that crosses an earlier one is left out, with a warning naming both lines. Before, both were built and the path crossed itself on every layer.
- `Continuous Path`: a wall the nozzle reaches by a travel move (the wall of an opening next to a chamber wall, for example) starts where it started on the layer below, so its seam stays in one place.

## [1.2.1] - 2026-11-11

Found by running the whole Slicer tab in Grasshopper (Rhino 9) on parts with openings.

### Added
- First version for Grasshopper 2 (Rhino 9) in `LARGERslicer.GH2`: nine slicer components and three DXR components on the same slicer core. See its README for what is ported and what is tested.
- `Quick Start` now builds three labelled examples: flat layers, non-planar layers along a guide surface, multi-axis layers along a guide curve.

### Fixed
- `Start and End`: the skirt ends exactly opposite the first point of the part and stops one bead spacing before its own beginning. The move across to the part is the shortest one, square to the wall; before, it could run as a long diagonal close alongside wall and skirt. If the part starts further in (bottom fill first), that move is a travel.
- `Infill Wave`: the wall of an opening that a dip reaches is now part of the line (no travel move to it on every layer). A meander no longer runs into an opening.
- `Infill Cells`, `Infill Wave`, `Infill Chambers`: joints no longer jump between layers. A slicer hands every layer over with slightly different points; the joints are now placed by the shape and by where the layer below has them. The wall of an opening is opened once instead of several times.
- `Bottom Fill`: with several openings the contour fill could cross itself at one link; such a place is re-joined.
- `Round Corners`: travel moves stay straight.
- `Path Check`: infill on top of a solid bottom layer counts as supported.

## [1.2.0] - 2026-11-11

### Added
- `Infill Chambers` (Slicer tab): a band of closed chambers along the inside of the wall, as boxes or as a truss with diagonals, joined into the continuous wall path. Chambers stand on those of the layer below, also on twisting or tapering parts.
- `Infill Sketch` (Slicer tab): draw your infill. Curves sketched over the part in top view become ribs that follow them from wall to wall, joined into the continuous wall path.
- `Desktop Test Print` (Slicer tab): scaled-down G-code of the same toolpath for small filament printers (Bambu Lab, Prusa, Creality, Elegoo and other Marlin or Klipper machines). Path, bead width and layer height shrink by the same factor; the component checks bed size, bead width against the nozzle and layer height, and suggests a scale. Start and end code are generic.
- `Infill Wave` (Slicer tab): one inner line that swings between the wall and a set depth, as a smooth wave, a truss or a meander. Every crest runs along the wall for two bead widths; the line is joined into the continuous wall path. Wavelength and Depth scale the pattern, Origin shifts it along the wall.
- `Infill Cells`: new `Stretch` input makes the cells longer along the pattern direction.
- `Machine Profile` (Slicer tab): describes how a printer wants its extrusion values instead of hard-wiring machines. E = cross section x length x Flow x Factor, with an extra factor for the first layer, an optional filament diameter and a choice between the squashed-bead and the width x height cross section. Presets: generic pellet extruder (volume), filament 1.75 / 2.85 mm, Weber DXR robot (factor 1.128, value multiplied by P1 on the controller) and Ginger (factor 0.21), taken from the GCODE MAKER of the FH Muenster definition at 100 % flow. Connect it to the new `Machine` input of `Print Settings`; `Quick Start` places it.
- `Infill Cells` (Slicer tab): honeycomb or grid infill as part of the one continuous wall line. Every cell is a closed loop half a bead spacing inside the cell, so neighbouring loops fuse into double walls; a spanning tree joins all loops, the walls of holes and the outer wall with short bridges into a single closed line that never crosses itself. The outer wall is opened in one place only. Because the layer stays one closed contour it also prints in spiral (vase) mode. The pattern has a fixed origin, so the cells stand above each other in all layers.
- `Single Line Fill with Cells` (Toolpaths tab): the same for a single boundary curve.
- `Slice`: non-planar and multi-axis slicing through a new `Guide` input. A guide surface gives non-planar layers, either with a flat first layer and a top that follows the guide, or with all layers following the guide (printing on a curved base). A guide curve gives multi-axis layers that stand square to the curve, for parts that grow along a bent axis. The model is mapped into a flat space, sliced with the normal pipeline and mapped back, so walls, bottom fill, ribs, spiral, seam, rounding, start/end and `Path Check` all work on these layers. The tool axis is square to the layer (or vertical for 3-axis printers) and the local layer height goes into the extrusion.

### Changed
- `Infill Wave`: new `Meet` option (on by default). Every dip reaches until it meets something: the dip coming from the opposite side, in the middle of the part, or the wall of a hole. Both end in a flat piece and fuse, which is what makes a waved or trussed part stiff. Opposite sides get the same number of waves so the dips face each other; `Depth` is now the limit (0 = as far as the part allows).
- `Infill Wave` rebuilt: the line follows the wall and dips in at regular intervals. Every straight side gets a whole number of equal waves, corners stay clear so dips of two sides cannot collide, and on round parts the wave is anchored to the layer below so it keeps its place on twisting and tapering shapes.
- `Path Check`: a handful of stray points off the layer below is reported as a note instead of a failure, and the closing pass over the seam is no longer counted as a crossing.
- `Continuous Path` now prints flat layers by default (Mode = Layered) with a 20 mm ramp at one fixed place instead of 100 mm. In Spiral mode, layers with ribs or cells spliced into the wall are printed flat as well: before, up to 60 % of the path ran between two layers and the infill climbed with the wall.
- `Machine Profile`: the Weber DXR preset uses the factor 1.128 of the GCODE MAKER (a Cura value, at 100 % flow) instead of 1/3.
- New logo: one continuous line that climbs in loops and ends at the nozzle. `docs/` holds a 1024 px logo, a GitHub social preview image and the website draft.
- `Path Check` explains unsupported beads as an overhang: how far the wall leans, what the limit is for the current bead width and layer height, which layers are affected, and what to do (guide surface, tilted plane, cut the part).
- `Continuous Path` (Layered): a layer with several closed walls (a ring, a part with holes) now starts on the wall the layer below ended on. That is one travel move per layer instead of two.
- Toolpaths tab rewritten on the fill engine of the Slicer tab. `Single Path Fill with Lines`, `Single Line Fill with Offsets`, `Continuous Toolpath`, `Continuous Path from Boundary`, `Single Line Fill with Euler` and `Continuous Infill` keep their names, inputs, outputs and IDs (old definitions still open) but now return one line at bead-width spacing that never crosses itself, in milliseconds instead of seconds. A zigzag that cannot fill a shape in one line follows the outline instead and says so. Inputs that are no longer needed (bridges, undercuts, ArcWelder) are kept and marked as unused.
- `Suppress Self Intersections` now really removes crossings: the stretch between two crossing parts is run the other way round, so the line stays one line. `Optimized Toolpath` orders, flips and joins curves into one path and reports gaps it could not close.
- `Feedrate Calculator` outputs the same feed rate for every move. F is the speed along the path, so constant speed means constant F; the old scaling with segment length made the speed fluctuate on unevenly spaced points.
- `Continuous Toolpath`, `Continuous Path from Boundary`, `Single Path Fill with Lines` and `Single Line Fill with Euler` wrote to six outputs although they have one. Grasshopper stopped with "Output parameter Index [1] too high or too low", and the single output received intermediate polylines instead of the fill path. They now output the continuous fill path.
- `Feedrate Calculator` never outputs F0 any more. A zero-length segment (the placeholder in front of the first point) was the source of the `F0` in the first movement line of the easy slicer in the SETUP definition.
- `DXR File Health Check` now fails a file in which a movement line runs with feed rate 0 (a move with F0 never finishes) and warns when the first movement line has no feed rate.

- New icon set for all components and a new plug-in logo: minimal line glyphs in one ink colour with one accent colour per group (slicer blue, toolpaths teal, DXR/export orange, CNC green, utilities purple), 24 px with 2 px margin. Generated by `scripts/generate_icons.py`.
- `Infill Ribs`: distributed ribs divide every wall side into equal cells, short spans are built first, a distributed rib never ends against another rib, and ribs keep apart by the distance between their middles.
- `Path Check`: a few points just over the limit are reported as a note instead of a failure; only clearly unsupported points (more than 1.5 x the limit) are output as points.

- `Bottom Fill`, pattern Contour: rebuilt as a connected Fermat spiral (after Zhao et al. 2016). The fill of a layer is one line that never crosses itself and starts and ends side by side at the wall; pockets and the rings around holes are spliced in as side trips, and the walls of holes become part of the fill line, so no travel move crosses the fill. Auto uses a zigzag only where it is one line and starts where the layer below stopped, otherwise Contour.
- Layer changes in bottom layers: layers alternate between wall-then-fill and fill-then-wall, so each layer starts directly above the point where the layer below ended. The wall meets the fill on a straight piece of wall and runs one bead spacing past its start, so the outer wall is closed.
- `Path Check`: new test and output `Crossings` - the path must never run across its own bead within a layer.

- `Continuous Path`, Layered: the seam is now a fixed ramp. At the seam the path climbs one layer height over the ramp length while it keeps going round, and every layer climbs at the same place, so inside the ramp zone each pass lies exactly one layer height above the pass below. Nothing is printed twice and the extrusion stays constant; only the first ramp and one closing pass at the top fade. Without a seam input the seam sits in the middle of the longest straight piece. The `Alternate` input is gone.
- `Start and End`: the skirt is printed first and runs on into the outer wall as one continuous bead (no travel move in between).
- `Walls`: new `Position` input (drop-down): bead inside the geometry (default), centred on it, or outside.
- `Quick Start` spells out all input and output names.
- `DXR Generator`: a speed value of 0 is never written to a movement line; it is replaced by the neighbouring speed and reported.
- Same outline on consecutive solid layers gives exactly the same fill line.
- The slicer core no longer depends on Rhino's native library for simple geometry (`Toolpaths/Geo.cs`), which makes it testable outside Rhino.

### Added
- New `Slicer` tab (first stage of the toolpath rebuild; the existing `Toolpaths` components are unchanged). All components share one `Toolpath` data type that carries tool axis, local layer height, bead width, flow and role per point:
  - `Print Settings`: bead width, layer height, speed, flow, overlap, minimum radius, resolution and a Preview switch in one place.
  - `Slice`: cuts open or closed Breps, surfaces, extrusions, SubDs and meshes into layers with its own triangle cutter (about 10-70 ms for 200-250 layers in tests). Plane and angle input for angled slicing such as 45 degrees.
  - `Walls`: turns the model contour into wall paths. The first wall is inset by half a bead width, further walls follow at bead spacing (bead width x (1 - overlap)); holes are handled. Uses Clipper2 for offsetting (new dependency `Clipper2Lib.dll`).
  - `Infill Ribs`: stiffening ribs as part of the continuous path. A rib is a hairpin with both legs one bead spacing apart; it stops one bead spacing before the opposite wall or a neighbouring rib. Ribs are placed with points, curves or planes and/or distributed evenly. With `Meet`, two ribs from opposite walls meet inside the part and their joint alternates sides from layer to layer (`Stagger`), so the ribs interlock. Rib points are tagged with the role `Infill`.
  - `Continuous Path`: Spiral (vase mode, path blends from layer to layer, flat first and last loop with fading height) or Layered (scarf seam over a ramp length, optional alternating direction, zigzag for open contours). Optional seam point or curve.
  - `Bottom Fill`: solid first layers as part of the continuous path, zigzag (turned 90 degrees every layer, ends chained from layer to layer) or concentric. Lines lie one bead spacing apart and end one bead spacing from the wall; the wall starts next to the fill so the path turns from the wall into the fill. In Spiral mode the bottom layers are printed flat and the spiral starts on top of them.
  - `Round Corners`: replaces every corner by an arc that leaves and rejoins the path tangentially (exact radius on wall corners, half circles on zigzag turns and rib tips, S curves on sideways steps). Separate radius for rib feet. Skipped in Preview.
  - `Start and End`: skirt around the first layer before the part and an exit move (sideways off the part, then lift) or a custom exit curve with its own flow factor, so the extruder neither starts nor stops on the part.
  - `Path Check`: support test - a path point may sit at most half a bead width beside the path of the layer below, otherwise the bead hangs in the air. Outputs the unsupported points, the offset per point and a plain-language report (bead shape, travel moves).
  - Usability: freshly placed components bring their own drop-down lists and sliders, show their state as a label (Spiral, Zigzag, Preview, OK ...), have their own icons, and the path preview is coloured by role (wall, infill, bottom, seam, travel, skirt).
  - `Quick Start`: dropped on an empty canvas it builds the complete chain with a demo part and the house settings (5 mm bead, 2 mm layer) and removes itself.
  - `Layers to Curves` / `Curves to Layers`: open interface. Layers come out as plain curves (one branch per layer) and any curves go back in as layers, so own patterns can be tried in a script or with standard components without changing the plug-in.
  - `Infill Ribs`: curves drawn in top view are followed by the rib from wall to wall (projected along the layer normal, so it also works for tilted layers). Distributed ribs now stand square to the wall, come from one wall at even distances, avoid wall corners and narrow leftover cells, and take over their position from the layer below.
  - `Bottom Fill`: pattern Auto (one continuous zigzag where a direction allows it, otherwise contour) and Contour with double offset after Bi et al. 2022.
  - `Extrusion and Speed`: extrusion per move from local layer height and bead width; a changing height is compensated either by flow or by speed. Outputs points, tool planes, E, F, layer height, layer index and role.
- Rhino 9 support: the plugin now also builds for `net8.0` / `net8.0-windows` against the Grasshopper 9 SDK, and `build-package.sh` / `build-package.bat` create a dedicated `rh9_0` Yak package (net48 + net8.0 + net8.0-windows) next to the Rhino 8 and Rhino 7 packages. Components run in Grasshopper 1 inside Rhino 9; Grasshopper 2 is not supported yet.

## [1.1.13] - 2026-10-23

### Added
- Added `DXR Variable Width` utility component: scales the XE extrusion values of a DXR program along X, Y or Z so the line width runs from Width Start to Width End (Linear, Smooth or Gamma transition, optional fixed range, factor cap and skirt-aware range). Outputs the scaled code plus segments, widths, factors, colors and layer indices, and draws a colored width preview. When placed on the canvas it creates and connects sliders for the widths and value lists for Axis and Mode. Replaces the `Python 3 Script` in the Weber/Ginger definition and works in Rhino 7 and Rhino 8 (output verified identical to the script).

### Documentation
- Added a central documentation index and separate German and English KUKA recovery guides.
- Reorganized the German DXR25 manual around normal operation, file loading, printing, and symptom-based troubleshooting; retained E1 recovery as a special case.
- Updated machine-guide links and references to the current combined Grasshopper definition.

## [1.1.12] - 2026-07-29

### Changed
- Refined utility component icons to better match function semantics at 24x24 and align with the established dark-blue icon style.
- Harmonized remaining mixed-language input/output labels and descriptions in newly added utility components to English.

## [1.1.11] - 2026-07-29

### Added
- Added `Curve Pattern Split` utility component for modular fixed/variable curve segmentation.
- Added `Fast GCode DXR Preview` utility component with cached parsing and 3D ribbon or conduit line preview modes.
- Added `G-Code Diff Check` utility component for line/coordinate comparison between original and remapped code.
- Added `GCode E Remap MultiPart` utility component for XE recalculation with multi-part mapping and optional outline handling.
- Added `Overlap Tester Var Width` utility component for variable-width overlap analysis and heatmap output.
- Added `Surface Thickness Heatmap` utility component for per-part thickness sampling, rebuild surfaces, and heatmap mesh generation.

## [1.1.10] - 2026-07-28

### Fixed
- Recolored affected custom component icons to match the darker blue reference icon style used by existing LARGERslicer components.
- Updated icon tone consistency for: `CadPackageExport`, `CncUtilityExport`, `FastDeconstructPoint`, `GCodeRobotCodeMaker`, `NameCreator`, `PathCreatorMultiaxial`, `PreviewComponent`, `PrintPathCreatorPlanar`, and `RobotCodeCreator`.

## [1.1.9] - 2026-07-28

### Fixed
- `DXR File Health Check`: direct `Code Input` now collects full connected data (including lists/trees) and validates once, preventing Grasshopper grafting-style repeated solves and long runtime when code comes from upstream components.

## [1.1.8] - 2026-07-28

### Changed
- `DXR File Health Check` now supports optional direct `Code Input` validation, allowing checks without a saved file.
- Simplified outputs to `Checked File`, `Status`, and `Report` (removed separate GO/NO GO and cabinet sheet outputs).
- Increased default `Large File Threshold MB` from 8 MB to 25 MB.

### Added
- Added Z-value safety checks in movement lines (including negative values).
- Added build plate protection rule: any detected `Z <= 0` is `NO GO`.
- Added first-layer safety warning: first positive layer below `1.5 mm` is flagged as warning.

### Fixed
- Reduced `DXR File Health Check` icon visual footprint to match standard Grasshopper icon density.

## [1.1.7] - 2026-07-28

### Fixed
- Packaging hotfix: `build-package.sh` now always copies fresh build outputs into package root `LARGERslicer.gha` before `yak build`, preventing stale plugin binaries from being published.
- Synced manual install bundle files in `Plugin Installation Files/LARGERSlicer/` from the latest net48 release build.
- Prevented regression where older component naming/icon state could reappear after package updates.

## [1.1.6] - 2026-07-28

### Fixed
- Restored plugin-facing CNC Utilities and export registration metadata in component constructors for stable category visibility.
- Fixed `CAD Package Export` user-facing log text to English.
- Ensured `DXR File Health Check` registration metadata is explicit so the component appears reliably in Grasshopper.

### Changed
- Aligned assembly/file/informational version metadata with package version to avoid stale plugin loading across updates.

## [1.1.5] - 2026-07-28

### Fixed
- Hotfix for `Fast Deconstruct Point` component registration to ensure stable loading and placement in Grasshopper.

### Verified
- Confirmed unique component GUIDs across the plugin (no duplicate `ComponentGuid` values).

## [1.1.4] - 2026-07-28

### Added
- Added `DXR File Health Check` component with dedicated GUID and icon for operator-facing GO/NO-GO file validation.
- Added printable cabinet sheets for GO/NO-GO workflow and NO-GO immediate help with QR links to repository and issue tracking.

### Changed
- Simplified and hardened DXR/GCode preflight logic for clearer readable/filename/content rule status in cabinet output.

## [1.1.3] - 2026-07-27

### Changed
- Refined `CNC Utilities 08` and `Fast Deconstruct Point` icons to match the existing icon language with transparent backgrounds.

## [1.1.2] - 2026-07-27

### Added
- Added `Fast Deconstruct Point` utility component for high-performance point decomposition into X/Y/Z outputs.

### Fixed
- Fixed curve-tool robustness in `Join Open Contours` and `Alternate Curves` for safer ordering and direction handling.
- Updated component icons to match the CNC Utilities icon style, with a dedicated icon for the fast point utility.

## [1.1.1] - 2026-07-27

### Fixed
- Package publish follow-up: released a new package version so Rhino Package Manager upgrades continue from 1.0.48 after 1.1.0 became unavailable on Yak.

## [1.1.0] - 2026-07-27

### Changed
- Renamed the previous Thekenfront toolchain to CNC Utilities (CU_01 to CU_08) with generic CNC-oriented naming for broader workflows.
- Added bilingual DE/EN compatibility aliases for key GHPython export scripts while preserving legacy German inputs and outputs.

### Added
- Added a full 24x24 icon set for CNC Utilities components and wired all CU components to embedded PNG icons.
- Added CAD package export helper components and export naming utilities used by the updated export pipeline.

### Added
- Optimized Toolpath component with icon
- Versioning strategy documentation
- Yak package configuration

## [1.0.48] - 2026-03-14

### Added
- TH_01 Orient: Neuer Input 'Drehwinkel' (default 0°).
  Rotiert die Brettorientierung um die Top-Achse.
  Typisch: 0° / 90° / 180° / 270° um Laenge und Tiefe zu tauschen.

## [1.0.47] - 2026-03-14

### Changed
- TH_01 Orient komplett ueberarbeitet: Neue optionale Referenzflaechen-Inputs.
  - 'Oberseite' (Surface): Definiert die Stapelrichtung (Z). Bretter liegen parallel dazu.
  - 'Frontflaeche' (Surface): Definiert die Tiefenrichtung (Y).
  - Ohne Referenzflaechen: Automatische Erkennung ueber BBox-Dimensionen
    (duennste Achse = Front, Z-Ausrichtung = Top).
  - Neuer Output 'Quell-Frame' zur visuellen Kontrolle der erkannten Orientierung.
  - Alte Inputs (Index, Auto) entfernt.

## [1.0.46] - 2026-03-14

### Fixed
- TH_01 Orient: Korrektes Achsen-Mapping fuer die gesamte Pipeline.
  Kreuzprodukt-Reihenfolge korrigiert (nFront x nTopPerp statt nTopPerp x nFront)
  und Source-Plane auf (nRight, nFront) gesetzt.
  Ergebnis: X=Brettlaenge, Y=Fraestiefe (variabel), Z=Stapelhoehe.
  Bretter liegen jetzt parallel zur Ober-/Unterseite und passend auf dem Solid.

## [1.0.45] - 2026-03-14

### Fixed
- TH_01 Orient: Achsen-Mapping zurueck auf v1.0.42-Stand (Front→Z, Hoehe→Y).
  Bretter werden von vorne nach hinten gestapelt (Tiefe = Stapelrichtung).
  Jedes Brett ist ein vollhohes, vollbreites Schichtbrett.

## [1.0.44] - 2026-03-14

### Fixed (fehlerhaft – korrigiert in 1.0.45)
- TH_01 Orient: Falscher Achsentausch, Stapelrichtung und Hoehe vertauscht.

## [1.0.43] - 2026-03-14

### Changed
- TH_06 Saugelemente komplett ueberarbeitet: L-foermige Saugelemente mit
  vertikalem Anschlagbrett + horizontalem Basisbrett pro Seite
- Neue Eingaben: Einstecktiefe (5mm), Anschlag-Staerke (30mm),
  Basis-Hoehe (20mm), Basis-Ueberstand (50mm)
- Neuer Ausgang: Gefraeste Bretter (Bretter mit Einsteck-Taschen an beiden Enden)
- TH_07 BOM erweitert: nimmt jetzt auch Basis-Bretter links/rechts als optionale Eingaben

## [1.0.42] - 2026-03-14

### Fixed
- TH_01 Orient: Komplett ueberarbeitete Ausrichtung mit PlaneToPlane statt zweistufiger Rotation.
  Front- und Ober-/Unterseitenflaeche werden im Originalsystem gleichzeitig erkannt,
  sodass Bretter zuverlaessig parallel zur originalen Ober-/Unterseite liegen.

## [1.0.41] - 2026-03-14

### Changed
- Thekenfront: Alle Komponenten-Namen, Portbezeichnungen und Fehlermeldungen auf verstaendliches Deutsch umgestellt
- Thekenfront: TH_01 Orient verwendet jetzt zweistufige Ausrichtung – Roll-Korrektur stellt sicher, dass Bretter parallel zur originalen Ober-/Unterseite liegen

### Fixed
- TH_03 Slice: Doppelte Fehlermeldungen und defekte Info-Listen-Initialisierung bereinigt

## [1.0.40] - 2026-03-14

### Fixed
- Fixed Thekenfront slicing so fuge boards include the full board-plus-gap height instead of only tagging a regular 30 mm board
- Fixed TH_03b Fuge Split not producing split parts for valid fuge positions in common one-fuge setups

## [1.0.39] - 2026-03-14

### Fixed
- Reworked Thekenfront workflow to remove cyclic Grasshopper wiring between depth and block generation
- TH_04 Depth now outputs scalar depth data and board length instead of composite board objects
- TH_05 Block and TH_07 BOM now consume split boards plus depth lists in a linear pipeline

### Changed
- Renamed Thekenfront inputs and outputs to be clearer and closer to the intended planning terminology
- Added wiring guide for the full Thekenfront workflow in `documentations/THEKENFRONT_BAUPLAN.md`

## [1.0.38] - 2026-03-14

### Fixed
- Fixed Yak packaging so Rhino Package Manager receives the current integrated LARGERslicer binaries for net48, net7.0, and net7.0-windows
- Fixed missing Thekenfront components in Rhino after package installation caused by stale packaged runtime folders

### Changed
- Package build scripts now sync root runtime folders and dist artifacts from bin/Release before yak build

## [1.0.37] - 2026-03-13

### Added
- Integrated Thekenfront Fräsvorbereitung toolset directly into existing LARGERslicer plugin (no separate plugin)
- New Thekenfront components in LARGER category:
  - TH_01 Orient
  - TH_02 BBox
  - TH_03 Slice
  - TH_03b Fuge Split
  - TH_04 Depth
  - TH_05 Block
  - TH_06 Saug
  - TH_07 BOM
  - TH_08 Export
- Shared Thekenfront board data models for cross-component pipeline

### Changed
- Thekenfront slicing logic now preserves residual height in fuge width distribution and supports automatic/manual fuge centers
- Thekenfront split logic now operates on per-board fuge metadata from slicing stage
- Thekenfront depth logic now computes stepped depth per board over local Z ranges
- Thekenfront suction logic now outputs left/right simplified stair outlines alongside suction bodies

## [1.0.18] - 2025-01-XX

### Fixed
- **CRITICAL**: Fixed Rhino 7 compatibility issues
  - Added separate Grasshopper package references for Rhino 7 (7.0.20314.3001) and Rhino 8 (8.0.23304.9001)
  - Fixed `Intersection.MeshLine` API compatibility between Rhino 7 and Rhino 8 using conditional compilation
  - Added `rhino_version` and `minimum_rhino_version` to manifest.yml for proper package compatibility detection
- Fixed package installation and uninstallation issues in Rhino 7 and Rhino 8

### Changed
- Package now correctly builds separate .gha files for net48 (Rhino 7) and net7.0/net7.0-windows (Rhino 8)
- manifest.yml now includes proper Rhino version compatibility information

## [1.0.17] - 2025-01-XX

### Added
- CNC Program component: Direct Brep/NURBS support (no mesh conversion required)
  - Breps and NURBS surfaces are now processed directly for higher precision
  - Mesh support remains available as fallback
- CNC Program component: Improved outline generation using actual geometry footprint
  - Outline now uses the actual geometry footprint (projected onto XY plane) instead of bounding box
  - Works correctly for circles and irregular geometries
  - `GetGeometryFootprintFromBrep`: Extracts footprint from Breps using edge curves

### Changed
- CNC Program component: Z-sampling now uses Brep face intersections for higher precision
  - `ZAtXYBrep`: Samples Z heights directly from Brep faces using curve-surface intersection
  - `ZAtXY`: Generic function supporting both Mesh and Brep
- CNC Program component: All geometry processing functions now support both Mesh and Brep
  - `GenerateBoustrophedonPath`, `MovePointInward`, `FindBoundaryIntersection`, `FindConnectionPathToOutline` all support Brep

### Fixed
- CNC Program component: Outline generation for circular geometries now uses actual footprint instead of bounding box

## [1.0.16] - 2025-01-XX

### Fixed
- CNC Program component: Corrected Z-axis coordinate system logic
  - Geometry Z coordinates are now correctly interpreted as positive (material remaining, e.g., +5mm = 5mm remain)
  - CNC Z coordinates: CNC_Z = -geometryZ (directly negative, relative to table surface at Z=0)
  - ZP (Z-Position oben) is now correctly positive: Materialstärke * 100 + 100 (absolute position above table)
  - Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments; 30mm material → ZP = 3100 increments

### Changed
- CNC Program component: ZP calculation now uses only material thickness (independent of geometry)
- CNC Program component: Improved validation for Z values (warns if geometry Z exceeds material thickness or would result in positive CNC Z)

## [1.0.15] - 2025-01-XX

### Added
- CNC Program component: Underlay Thickness input parameter (default: 200 increments = 2mm) for XX308 command
- CNC Program component: Added descriptive comments to all HPGL commands explaining their function

### Changed
- CNC Program component: XX308 now always enabled at start (XX308,1,{thickness};) and disabled at end (XX308,0;)
- CNC Program component: Removed Underlay boolean input (now always active with configurable thickness)

## [1.0.14] - 2025-01-XX

### Fixed
- CNC Program component: Vacuum strength now correctly integrated into PB2 command (PB2,1,{level};) instead of separate PB9 command
- CNC Program component: PB2 now sets vacuum level directly: PB2,1,{strength}; for levels 1-10, PB2,0; for off

## [1.0.13] - 2025-01-XX

### Added
- CNC Program component: Vacuum strength input (0-10) with PB9 command
- CNC Program component: Extraction height (XX306) automatically calculated from material thickness + 3.3mm brush length

### Changed
- CNC Program component: Removed PU (Pen Up) commands between path segments - toolpath is now continuous (only MW commands)
- CNC Program component: PU only used at start of first segment and after tool changes

## [1.0.12] - 2025-01-XX

### Added
- CNC Program component: Tool change functionality
  - Tool List input: Optional list of tools (11, 21, 31) for each path segment
  - Tool Spindle Speeds input: Optional list of spindle speeds (RPM) for each tool
  - Automatic tool change sequence: ZP → XX220 → SP → ZP → XX150
  - Tool change validation: Ensures tool list matches segment count

### Changed
- CNC Program component: PLT generation now processes segments individually to support tool changes
- CNC Program component: Tool change sequence follows HPGL Manual specifications (Index 220)

## [1.0.11] - 2025-01-XX

### Added
- CNC Program component: New input parameters for advanced HPGL control
  - Material Thickness (mm) - for ZP calculation and MW Z-value adjustment
  - Tool selection (11, 21, 31) - for SP command (left, middle, right)
  - Spindle Speed (RPM) - for XX150 command
  - Acceleration Down/Up (1-4) - for AS command (cutting and rapid acceleration)
  - Automatic vacuum zone calculation based on bounding box width in Y direction
- CNC Program component: ZP command (Z-Position oben) - Materialstärke + 10mm safety distance
- CNC Program component: SV command (Set Vacuum) - automatically calculated from object width
- CNC Program component: AS command (Acceleration Select) - separate acceleration for cutting and rapid
- CNC Program component: MW Z-values now relative to material surface (subtract material thickness)

### Changed
- CNC Program component: All HPGL commands (SP, XX150, SV, ZP, AS) now available in single unified mode
- CNC Program component: Removed Header Mode parameter (SIMPLE/EXTENDED distinction)
- CNC Program component: All commands validated against HPGL Manual (Generation 3)

### Fixed
- CNC Program component: AS command format corrected (AS,{down},{up} with comma after AS)
- CNC Program component: All HPGL command syntax validated and corrected according to official manual

## [1.0.10] - 2025-01-XX

### Fixed
- CNC Program component: Fixed bounding box expansion for rotation - now correctly calculates expanded bounding box based on rotation angle
- CNC Program component: Fixed retract height - now uses absolute 50mm (max build space) instead of relative height
- CNC Program component: Removed unused code and cleaned up function signatures

### Changed
- CNC Program component: Bounding box is now properly expanded when rotation angle is applied (trigonometric calculation)
- CNC Program component: Retract height is clamped to maximum build space (50mm) to prevent exceeding machine limits

## [1.0.9] - 2025-01-XX

### Fixed
- CNC Program component: Fixed path segments jumping back - improved connection logic with retract for distant segments
- CNC Program component: Fixed outline contour issues - removed duplicate points and double rotation, now properly aligned

### Changed
- CNC Program component: Segment connections now use retract logic (50mm) when segments are far apart (> 2*max(dx,dy))
- CNC Program component: Outline contour now has exactly 4 points (no duplicate closing point)
- CNC Program component: Outline is properly axis-aligned in rotated coordinate system (no double rotation)

## [1.0.8] - 2025-01-XX

### Added
- CNC Program component: Automatic outline contour generation - adds bottom outline to cut out parts from plate
- CNC Program component: "Add Outline" parameter to enable/disable outline contour
- CNC Program component: Angle rotation parameter for toolpath lines

### Changed
- CNC Program component: Removed Curve input parameter (simplified to Geometry-only input)
- CNC Program component: Outline contour is automatically added after boustrophedon path (moves down to bottom, then cuts outline)

## [1.0.7] - 2025-01-XX

### Fixed
- CNC Program component: Fixed issue where component failed when only Curves input was provided (without Geometry)
- CNC Program component: Both Geometry and Curves inputs are now properly optional - component works with either one

### Changed
- CNC Program component: Improved input validation and fallback logic when Geometry cannot be converted

## [1.0.6] - 2025-01-XX

### Added
- CNC Program component: Direct Curve input support - can now accept curves directly without geometry
- CNC Program component: Retract logic for connecting multiple curves (50mm retract height)

### Changed
- CNC Program component: Geometry input now has priority over Curves input
- CNC Program component: Improved path connection between multiple curves with proper retract movements

## [1.0.0] - 2024-12-05

### Added
- Initial release of LARGERslicer
- DXR Generator component for creating DXR files from toolpaths
- DXR Postprocessor component for converting GCode to DXR format
- Machine Settings components (basic and extended)
- Continuous Toolpath component with undercut handling
- Multiple infill pattern components:
  - Spiral patterns
  - Contour/Zigzag patterns
  - Hilbert curves
  - Fermat spirals
  - Grid patterns
  - Lines patterns
- Toolpath optimization components:
  - Optimized Toolpath
  - Eulerian Path
  - Bridge Curves
  - Join Open Contours
  - Suppress Self Intersections
  - Align Curves
  - Alternate Curves
- Utility components:
  - Date Timestamp
  - Desktop Path
  - Safe Component
  - Custom Preview Lineweights
  - Feedrate Calculator
  - RTree Closest Point
  - RTree Sort
  - Stream Freeze
  - Super Printpath Preview
- CNC Program component
- Comprehensive icon system with professional SVG icons
- Full documentation in documentations/ folder

### Technical Details
- Supports Rhino 7 and Rhino 8
- Targets .NET Framework 4.8 and .NET 7.0
- Cross-platform support (Windows and macOS)
- Professional icon design following Grasshopper guidelines

---

## Version History Format

Each version entry follows this structure:

```markdown
## [VERSION] - YYYY-MM-DD

### Added
- New features

### Changed
- Changes in existing functionality

### Deprecated
- Soon-to-be removed features

### Removed
- Removed features

### Fixed
- Bug fixes

### Security
- Security fixes
```
