# Changelog

All notable changes to LARGERslicer will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Optimized Toolpath component with icon
- Versioning strategy documentation
- Yak package configuration

### Changed
- Optimized Toolpath component moved to correct category (LARGER → Toolpaths)
- Project structure reorganized (documentations/, scripts/, examples/ folders)

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

