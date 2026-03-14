# Changelog

All notable changes to LARGERslicer will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Optimized Toolpath component with icon
- Versioning strategy documentation
- Yak package configuration

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


