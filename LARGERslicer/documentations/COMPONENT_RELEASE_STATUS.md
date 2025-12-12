# Component Release Status

This document tracks which components have been released and published in which version.

**Current Package Version:** 1.0.18 (from manifest.yml and LARGERslicer.csproj)

---

## Released Components (v1.0.0 - Initial Release)

### CNC Components
- ✅ **CNC Program** - Released in v1.0.0
  - Multiple updates in v1.0.6 through v1.0.18
  - Status: **PUBLISHED**

### DXR Components
- ✅ **DXR Generator** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **DXR Postprocessor** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Machine Settings** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Machine Settings Extended** - Released in v1.0.0
  - Status: **PUBLISHED**

### Infill Pattern Components (Bottom Layer Patterns)
- ✅ **Infill Spiral** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Infill Contour** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Infill Grid** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Infill Lines** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Infill Hilbert** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Infill Fermat Spirals** - Released in v1.0.0
  - Status: **PUBLISHED**

### Toolpath Components
- ✅ **Continuous Toolpath** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Optimized Toolpath** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Eulerian Path** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Bridge Curves** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Join Open Contours** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Suppress Self Intersections** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Align Curves** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Alternate Curves** - Released in v1.0.0
  - Status: **PUBLISHED**

### Utility Components
- ✅ **Safe Component** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Date Timestamp** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Desktop Path** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Custom Preview Lineweights** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Feedrate Calculator** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **RTree Closest Point** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **RTree Sort** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Stream Freeze** - Released in v1.0.0
  - Status: **PUBLISHED**
- ✅ **Super Printpath Preview** - Released in v1.0.0
  - Status: **PUBLISHED**

---

## Unreleased Components (Not in CHANGELOG)

### Infill Pattern Components
- ❌ **Infill Contour Zigzag Hybrid** - **NOT RELEASED**
  - File exists: `InfillContourZigzagHybridComponent.cs`
  - Status: **UNRELEASED** - Not mentioned in CHANGELOG

- ❌ **Infill Euler Transformation** - **NOT RELEASED**
  - File exists: `InfillEulerTransformationComponent.cs`
  - Status: **UNRELEASED** - Not mentioned in CHANGELOG

- ❌ **Continuous Infill** - **NOT RELEASED**
  - File exists: `ContinuousInfillComponent.cs`
  - Status: **UNRELEASED** - Not mentioned in CHANGELOG

- ❌ **Continuous Path From Curves** - **NOT RELEASED**
  - File exists: `ContinuousPathFromCurvesComponent.cs`
  - Status: **UNRELEASED** - Not mentioned in CHANGELOG

### Toolpath Components
- ❌ **Smart Slicer** - **NOT RELEASED**
  - File exists: `SmartSlicerComponent.cs`
  - Status: **UNRELEASED** - Not mentioned in CHANGELOG

- ❌ **Curve Boundary Infill** - **NOT RELEASED** (Just Created)
  - File exists: `CurveBoundaryInfillComponent.cs`
  - Created: 2025-01-XX (today)
  - Status: **UNRELEASED** - New component, not yet published

---

## Summary

### Total Components: 35
- **Released & Published:** 28 components (v1.0.0)
- **Unreleased:** 7 components

### Breakdown by Category:

**CNC:**
- Released: 1
- Unreleased: 0

**DXR:**
- Released: 4
- Unreleased: 0

**Infill Patterns:**
- Released: 6
- Unreleased: 4

**Toolpaths:**
- Released: 8
- Unreleased: 2

**Utilities:**
- Released: 9
- Unreleased: 0

---

## Recommendations

### Components to Release in Next Version (v1.0.19 or v1.1.0):

1. **Infill Contour Zigzag Hybrid** - Complete and ready
2. **Infill Euler Transformation** - Complete and ready
3. **Continuous Infill** - Complete and ready
4. **Continuous Path From Curves** - Complete and ready
5. **Smart Slicer** - Complete and ready
6. **Curve Boundary Infill** - Just created, needs testing

### Action Items:

1. ✅ Test all unreleased components
2. ✅ Update CHANGELOG.md with new components
3. ✅ Update COMPONENTS_OVERVIEW.md with new components
4. ✅ Ensure all components have proper icons
5. ✅ Verify all components compile and work correctly
6. ✅ Update version number before publishing
7. ✅ Build and test package before publishing

---

## Version History Reference

- **v1.0.18** - Current version (Rhino 7/8 compatibility fixes)
- **v1.0.17** - CNC Program improvements (Brep support)
- **v1.0.16** - CNC Program Z-axis coordinate fixes
- **v1.0.15** - CNC Program underlay support
- **v1.0.14** - CNC Program vacuum fixes
- **v1.0.13** - CNC Program vacuum and extraction
- **v1.0.12** - CNC Program tool changes
- **v1.0.11** - CNC Program advanced HPGL control
- **v1.0.10** - CNC Program rotation fixes
- **v1.0.9** - CNC Program path fixes
- **v1.0.8** - CNC Program outline contour
- **v1.0.7** - CNC Program curve input
- **v1.0.6** - CNC Program retract logic
- **v1.0.0** - Initial release (28 components)

---

*Last updated: 2025-01-XX*
*Based on CHANGELOG.md and component file analysis*

