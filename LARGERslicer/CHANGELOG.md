# Changelog

All notable changes to LARGERslicer will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
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
