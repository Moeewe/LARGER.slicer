# LARGERslicer - Component Overview

## Plugin Context
LARGERslicer is a Grasshopper plugin for advanced slicing operations and 3D printing workflow management. It integrates robot path generation, GCode processing, DXR file generation, and CNC toolpath creation for industrial 3D printing and manufacturing systems.

---

## Components by Category

### CNC (1 component)

#### 1. CNC Program
- **Nickname:** CNC
- **Category:** LARGER > CNC
- **Function:** Generates boustrophedon (zigzag) toolpath for CNC milling with Zünd PLT output. Supports SIMPLE and EXTENDED header modes. Creates continuous zigzag paths across geometry surfaces.
- **Key Features:**
  - Boustrophedon (zigzag) path generation on meshes/breps/surfaces
  - Z-height sampling from mesh intersections
  - Zünd PLT file format output (SIMPLE/EXTENDED modes)
  - Configurable step sizes (dx, dy)
  - Feed speed (VS) and rapid speed (VU) control
  - Tool selection, vacuum, and underlay support (EXTENDED mode)
  - Path segments and complete path output
  - Statistics output
- **Icon:** CNCProgramIcon.png (zigzag pattern)

---

### DXR (7 components)

#### 1. DXR Generator
- **Nickname:** DXR Gen
- **Category:** LARGER > DXR
- **Function:** Generates DXR files from robot movements with machine settings and print parameters. Accepts individual robot path/extrusion/speed inputs. Converts movement data into DXR format for robot control systems.
- **Key Features:**
  - Extracts branch {0;0;2} from robot path tree automatically
  - Flattens extrusion and speed trees
  - Integrates machine settings (temperature, cooling)
  - Generates complete DXR files with headers and shutdown sequences
  - Automatic header calculation (runtime, layers, bounds, extrusion totals)
  - Sequential line numbering
- **Icon:** DXRGeneratorIcon.png (file/document with export arrow)

#### 2. DXR GCode Postprocessor
- **Nickname:** DXR GCode
- **Category:** LARGER > DXR
- **Function:** Converts GCode files to DXR format. Parses GCode to extract robot path, extrusion amounts, and print speeds. Automatically extracts temperature settings from GCode header.
- **Key Features:**
  - Parses GCode to extract movement commands (G0/G1)
  - Extracts extrusion amounts (E values) with relative/absolute mode support
  - Extracts print speeds (F values)
  - Automatically extracts temperature settings from GCode header
  - Supports M83 (relative) and M82 (absolute) extrusion modes
  - Handles G92 E0 (extrusion reset) commands
  - Generates complete DXR files with calculated headers
  - Footer comments with generator info and timestamp
- **Icon:** DXRPostprocessorIcon.png (GCode → transformation gear → DXR)

#### 3. Machine Settings
- **Nickname:** Machine
- **Category:** LARGER > DXR
- **Function:** Configures printer settings: bed temperature, nozzle temperature, and cooling fan. Outputs machine configuration object for use with DXR components.
- **Key Features:**
  - Temperature control (bed and nozzle)
  - Cooling fan percentage (0-100%)
  - Generates start/end GCode sequences
  - All settings automatically turn OFF after print completion
  - Sequential line numbering (N10, N20, N30...)
- **Icon:** MachineSettingsIcon.png (gear/settings)

#### 4. Bottom Layer Spiral
- **Nickname:** BL Spiral
- **Category:** LARGER > DXR
- **Function:** Generates concentric spiral fill pattern for bottom layer. Starts from skirt/brim end point and ends at spiralized path start point. Pattern A: Spiral/Concentric.
- **Key Features:**
  - Concentric offset curves from outer boundary inward
  - Clockwise/counterclockwise direction control
  - Minimum radius threshold
  - Continuous path with smooth transitions
  - Automatic connection to skirt/brim and spiralized paths
  - Path statistics output (length, fill percentage)
- **Inputs:**
  - Boundary curve (from skirt/brim)
  - Layer area geometry
  - Start point (skirt/brim end)
  - End point (spiralized path start)
  - Line spacing (extrusion width)
  - Clockwise direction (boolean)
  - Minimum radius (mm, 0 = fill to center)
- **Outputs:**
  - Complete path curve
  - Path segments (for preview)
  - Path points list
  - Statistics
- **Icon:** BottomLayerSpiralIcon.png (concentric circles/spiral)

#### 5. Bottom Layer Grid
- **Nickname:** BL Grid
- **Category:** LARGER > DXR
- **Function:** Generates rectangular grid/zigzag fill pattern for bottom layer. Similar to boustrophedon pattern. Pattern B: Rectangular Grid/Zigzag.
- **Key Features:**
  - Parallel lines with alternating direction (zigzag)
  - Configurable X and Y spacing
  - Grid rotation angle
  - Start direction control (left/right)
  - Optimized connection order
  - Continuous extrusion path
- **Inputs:**
  - Boundary curve
  - Layer area geometry
  - Start point
  - End point
  - Line spacing (X direction)
  - Spacing Y (perpendicular spacing)
  - Angle (rotation in degrees)
  - Start left (boolean)
- **Outputs:**
  - Complete path curve
  - Path segments
  - Path points list
  - Statistics
- **Icon:** BottomLayerGridIcon.png (rectangular grid/zigzag)

#### 6. Bottom Layer Lines
- **Nickname:** BL Lines
- **Category:** LARGER > DXR
- **Function:** Generates parallel lines fill pattern in a single direction for bottom layer. Pattern C: Lines (Unidirectional).
- **Key Features:**
  - Unidirectional parallel lines
  - Configurable line direction (angle)
  - Optional connection order optimization
  - Minimal travel moves between lines
  - Continuous path generation
- **Inputs:**
  - Boundary curve
  - Layer area geometry
  - Start point
  - End point
  - Line spacing
  - Angle (direction in degrees)
  - Optimize order (boolean)
- **Outputs:**
  - Complete path curve
  - Path segments
  - Path points list
  - Statistics
- **Icon:** BottomLayerLinesIcon.png (parallel lines)

#### 7. Bottom Layer Hilbert
- **Nickname:** BL Hilbert
- **Category:** LARGER > DXR
- **Function:** Generates space-filling Hilbert curve pattern for bottom layer. Pattern E: Hilbert Curve (space-filling).
- **Key Features:**
  - Space-filling Hilbert curve algorithm
  - Configurable recursion order (typically 3-6)
  - Automatic boundary fitting
  - Continuous path through entire area
  - Efficient coverage with minimal overlaps
- **Inputs:**
  - Boundary curve
  - Layer area geometry
  - Start point
  - End point
  - Line spacing
  - Order (recursion depth, 1-8)
- **Outputs:**
  - Complete path curve
  - Path segments
  - Path points list
  - Statistics
- **Icon:** BottomLayerHilbertIcon.png (Hilbert curve pattern)

---

### Utilities (8 components)

#### 1. Safe Component
- **Nickname:** Safe
- **Category:** LARGER > Utilities
- **Function:** Writes text lines to a file safely. Combines folder path, filename, and extension. Uses UTF-8 encoding with cross-platform path handling.
- **Key Features:**
  - File writing with error handling
  - Automatic directory creation
  - Cross-platform path normalization (Windows/Mac)
  - Boolean control for write operation
  - Always outputs file path, even when not writing
  - Optional file name parameter
- **Icon:** SafeIcon.png (floppy disk/save)

#### 2. Date Timestamp
- **Nickname:** Timestamp
- **Category:** LARGER > Utilities
- **Function:** Generates a timestamp string with automatic update every second. Format: yymmddHHMM_ (e.g., 2411281430_)
- **Key Features:**
  - Automatic timestamp generation
  - Updates every second
  - Format: yymmddHHMM_
  - No inputs required
- **Icon:** DateTimestampIcon.png (clock face)

#### 3. Desktop Path
- **Nickname:** Desktop
- **Category:** LARGER > Utilities
- **Function:** Finds the current user's Desktop folder path cross-platform (Windows/Mac). Supports multiple languages (Desktop, Schreibtisch, Escritorio, Bureau, Рабочий стол).
- **Key Features:**
  - Cross-platform path detection
  - Multi-language support
  - Returns full Desktop path
  - No inputs required
- **Icon:** DesktopPathIcon.png (folder icon)

#### 4. Custom Preview Lineweights
- **Nickname:** LineWeight
- **Category:** LARGER > Utilities
- **Function:** Sets custom line weights and colors for geometry preview. Similar to Human plugin preview component. Allows custom visualization of curves, breps, meshes, and points.
- **Key Features:**
  - Custom line thickness
  - Custom colors for geometry
  - Supports curves, breps, meshes, points
  - Real-time viewport preview
- **Icon:** CustomPreviewLineweightsIcon.png (three lines with different weights)

#### 5. RTree Closest Point
- **Nickname:** RTreeCP
- **Category:** LARGER > Utilities
- **Function:** Finds the closest point in reference geometry from search points. Combines RTree creation and closest point search in one component. Uses spatial indexing for efficient nearest neighbor queries.
- **Key Features:**
  - Builds RTree from reference points
  - Finds closest reference point for each search point
  - Returns both closest points and their indices
  - Validates empty inputs
- **Icon:** RTreeClosestPointIcon.png (spatial search/triangle hierarchy)

#### 6. RTree Sort
- **Nickname:** RTreeSort
- **Category:** LARGER > Utilities
- **Function:** Sorts points by their spatial distribution using various methods. Useful for organizing points before RTree creation or for adaptive layer width calculations.
- **Key Features:**
  - Multiple sort methods: Z-ascending/descending, distance from origin, spatial proximity
  - Returns sorted points and original indices
  - Nearest neighbor chain algorithm for spatial sorting
- **Icon:** RTreeSortIcon.png (tree structure for sorting)

#### 7. Feedrate Calculator
- **Nickname:** Feedrate
- **Category:** LARGER > Utilities
- **Function:** Adjusts feedrate for constant speed. Converts target speed (mm/s) to feedrate (mm/min) and adjusts based on segment lengths. Results are rounded to whole numbers.
- **Key Features:**
  - Speed conversion (mm/s to mm/min)
  - Adaptive feedrate based on segment length
  - Maintains constant speed across varying segment lengths
  - Debugging output with intermediate values
- **Icon:** FeedrateIcon.png (speedometer/semicircle with needle)

#### 8. Stream Freeze
- **Nickname:** Freeze
- **Category:** LARGER > Utilities
- **Function:** Determines whether streaming data is allowed to pass through or not. Data can be controlled downstream through a component's solution, preventing unwanted ticks. Can hold/freeze the last received data.
- **Key Features:**
  - Boolean control for data flow
  - Holds last data when frozen
  - Visual indicator when data is frozen (blue overlay)
  - Prevents unwanted solution updates
- **Icon:** StreamFreezeIcon.png (pause/freeze symbol - two vertical bars)

---

## DXR File Format

### Header Structure
The DXR header includes automatically calculated values:
- `ProgRunTimeTotal`: Estimated print time in seconds (calculated from movements and speeds)
- `number of layers`: Count of unique Z-heights (0.1mm precision)
- `Eges`: Total extrusion amount (sum of all P1 values)
- Bounds: Xmin/Xmax, Ymin/Ymax, Zmin/Zmax

### File Structure
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
; DXR generated by LARGERslicer FH Münster Moritz Wesseler - 2025-01-28T14:30:45 UTC(+01:00) Postprocessor 1.0.0
```

### Features
- Sequential line numbering (N10, N20, N30...)
- Only non-empty coordinate values (no trailing spaces)
- Automatic header calculation
- Footer comments with generator info and timestamp
- Version: 1.0.0

---

## Technical Notes

### Icon System
- All icons are loaded via `IconHelper.Load("IconName.png")`
- Icons stored in `Resources/` folder
- Embedded as resources in the .NET assembly
- Format: PNG with transparent background, 24x24 pixels
- Design: Black (#000000) with minimal blue accent (#0066CC)

### Component Registration
- Main category: "LARGER" (short name: "LARGER")
- Subcategories: "CNC", "DXR", "Utilities"
- All components use consistent naming and icon system

### Version Information
- Plugin version: 1.0.0
- Postprocessor version: V1.0.0
- Author: Moritz Wesseler, FH Münster
- Contact: m.wesseler@fh-muenster.de
