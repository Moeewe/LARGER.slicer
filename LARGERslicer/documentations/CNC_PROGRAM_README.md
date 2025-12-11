# CNC Program Component - User Guide

## Overview

The **CNC Program** component generates boustrophedon (zigzag) toolpaths from 3D geometry and outputs HPGL code (.plt files) for Zünd CNC machines. It supports 3-axis simultaneous control with configurable tool selection, spindle speeds, vacuum zones, and material handling.

**Category:** LARGER > CNC  
**Component Name:** CNC - Program

---

## Workflow

1. **Input Geometry**: Provide mesh, brep, or surface geometry
2. **Configure Parameters**: Set toolpath spacing, speeds, material properties, and machine settings
3. **Generate Toolpath**: Component calculates boustrophedon path with Z-height sampling
4. **Output PLT File**: HPGL code is written to a .plt file for machine execution

---

## Input Parameters

### Geometry Inputs

#### Geometry (Geo)
- **Type:** Mesh, Brep, or Surface
- **Description:** 3D geometry to generate toolpath from
- **Required:** Yes
- **Usage:** Connect your mesh or surface geometry. The component samples Z-heights across the surface to create the toolpath.

---

### Toolpath Parameters

#### Step X (dx)
- **Type:** Number (mm)
- **Default:** 1.0
- **Description:** Step size in X direction for toolpath sampling
- **Range:** > 0
- **Example:** 1.0 = 1mm spacing between toolpath lines in X direction

#### Step Y (dy)
- **Type:** Number (mm)
- **Default:** 1.0
- **Description:** Step size in Y direction for toolpath sampling
- **Range:** > 0
- **Example:** 9.0 = 9mm spacing between toolpath lines in Y direction (raster pitch)

#### Margin
- **Type:** Number (mm)
- **Default:** 0.0
- **Description:** Margin around geometry bounding box
- **Usage:** Adds extra space around the geometry before generating toolpath

#### Start Left (StartLeft)
- **Type:** Boolean
- **Default:** True
- **Description:** True to start toolpath from left, False to start from right
- **Usage:** Controls the direction of the first toolpath line

#### Angle
- **Type:** Number (degrees)
- **Default:** 0.0
- **Description:** Rotation angle for toolpath lines
- **Range:** 0-360
- **Usage:** 
  - 0° = horizontal/vertical lines
  - 45° = diagonal lines
  - 126° = custom angle (as shown in example)

#### Add Outline (Outline)
- **Type:** Boolean
- **Default:** True
- **Description:** True to add outline contour at bottom to cut out the part
- **Usage:** When enabled, adds a contour path at the lowest Z-level to release the part from the material plate

---

### Speed and Movement Parameters

#### Feed Speed (VW)
- **Type:** Number (mm/s)
- **Default:** 10.0
- **Description:** Feed speed (VS) for cutting movements
- **Range:** > 0
- **Example:** 7.0 = 7mm/s cutting speed

#### Rapid Speed (VF)
- **Type:** Number (mm/s)
- **Default:** 50.0
- **Description:** Rapid speed (VU) for non-cutting movements
- **Range:** > 0
- **Example:** 100.0 = 100mm/s rapid movement speed

#### Z Up
- **Type:** Number (mm)
- **Default:** 5.0
- **Description:** Z retract height for rapid movements
- **Usage:** Height to retract tool when moving between disconnected path segments

---

### Material Parameters

#### Material Thickness (MatThick)
- **Type:** Number (mm)
- **Default:** 10.0
- **Description:** Material thickness in millimeters
- **Usage:** 
  - Used for ZP calculation (top position = material thickness + 1mm safety)
  - Used for XX306 extraction height (material thickness + 3.3mm)
  - Used for validation (geometry Z should not exceed this value)
- **Example:** 32.0 = 32mm material thickness

**Important:** 
- Geometry Z coordinates are **positive** (representing material remaining, e.g., +5mm = 5mm remain)
- CNC Z coordinates are **negative** (relative to table surface at Z=0)
- Conversion: `CNC_Z = -geometryZ`
- Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments

#### ZP Offset (ZPOffset)
- **Type:** Integer
- **Default:** 0
- **Description:** ZP offset for through-cutting
- **Usage:**
  - 0 = cut to material surface (Z=0)
  - +1, +2, etc. = offset for through-cutting
- **Note:** ZP (Z-Position oben) is positive: Materialstärke * 100 + 100 (1mm safety)

#### Material Width (MatWidth)
- **Type:** Number (mm)
- **Default:** 0.0
- **Description:** Material width in Y-dimension for vacuum zone calculation
- **Usage:**
  - If 0 or not provided, uses bounding box Y-dimension
  - For plates >= 430mm, vacuum zones are 80mm wide sections
  - Used for SV command (vacuum zone width)
- **Example:** 650.0 = 650mm material width

---

### Tool and Spindle Parameters

#### Tool
- **Type:** Integer
- **Default:** 31
- **Description:** Tool selection for SP command
- **Values:**
  - 11 = Left tool position
  - 21 = Middle tool position
  - 31 = Right tool position (default)
- **Usage:** Selects which tool position to use for the entire toolpath (if Tool List is not provided)

#### Spindle Speed (RPM)
- **Type:** Number (RPM)
- **Default:** 10000.0
- **Description:** Spindle speed for XX150 command
- **Range:** > 0
- **Example:** 30000 = 30000 RPM spindle speed

#### Tool List (Tools)
- **Type:** Integer List (Optional)
- **Description:** List of tools (11, 21, 31) for each path segment
- **Usage:**
  - If empty, uses single Tool parameter for all segments
  - If provided, must match the number of path segments
  - Enables tool changes between segments
- **Example:** [31, 21, 31] = use tool 31 for segment 1, tool 21 for segment 2, tool 31 for segment 3

#### Tool Spindle Speeds (ToolRPM)
- **Type:** Number List (Optional)
- **Description:** List of spindle speeds (RPM) for each tool
- **Usage:**
  - If empty, uses single Spindle Speed for all tools
  - Maps to tools in order: 11, 21, 31
  - Used when different tools require different spindle speeds
- **Example:** [28000, 30000, 32000] = 28000 RPM for tool 11, 30000 RPM for tool 21, 32000 RPM for tool 31

---

### Acceleration Parameters

#### Accel Down (AccDown)
- **Type:** Integer
- **Default:** 2
- **Description:** Acceleration for cutting movements
- **Range:** 1-4
- **Usage:** Lower values = slower acceleration (more gentle), higher values = faster acceleration

#### Accel Up (AccUp)
- **Type:** Integer
- **Default:** 4
- **Description:** Acceleration for rapid movements
- **Range:** 1-4
- **Usage:** Typically higher than Accel Down for faster rapid movements

---

### Vacuum and Support Parameters

#### Vacuum Strength (VacStr)
- **Type:** Integer
- **Default:** 0
- **Description:** Vacuum strength level
- **Range:** 0-10
- **Usage:**
  - 0 = Vacuum off (PB2,0;)
  - 1-10 = Vacuum on with strength level (PB2,1,{level};)
- **Example:** 1 = minimum vacuum strength, 10 = maximum vacuum strength

#### Underlay Thickness (Underlay)
- **Type:** Integer
- **Default:** 200
- **Description:** Underlay thickness in increments for XX308 command
- **Usage:**
  - 100 increments = 1mm
  - Default: 200 = 2mm underlay thickness
  - XX308 is always enabled at start (XX308,1,{thickness};) and disabled at end (XX308,0;)
- **Example:** 200 = 2mm underlay support

---

### Output Control

#### Make PLT (MakePLT)
- **Type:** Boolean
- **Default:** True
- **Description:** True to generate PLT output
- **Usage:** When enabled, generates HPGL code. When disabled, only calculates toolpath geometry.

---

## Output Parameters

### Path
- **Type:** Curve
- **Description:** Complete continuous toolpath as a single polyline curve
- **Usage:** Preview the toolpath in Rhino viewport

### Segments
- **Type:** Curve List
- **Description:** Individual path segments (each boustrophedon row)
- **Usage:** Useful for analyzing individual segments or assigning different tools

### PLT
- **Type:** String
- **Description:** HPGL code as text
- **Usage:** Connect to file writing component to save as .plt file

### Stats
- **Type:** String
- **Description:** Statistics about the generated toolpath
- **Format:** `Boustrophedon [+ Outline] | Segments={count} | Points={count} | Length={mm} mm | VS={mm/s} mm/s, VU={mm/s} mm/s`
- **Example:** `Boustrophedon + Outline | Segments=45 | Points=1234 | Length=5678.90 mm | VS=7.000 mm/s, VU=100.000 mm/s`

---

## HPGL Commands Generated

The component generates the following HPGL commands:

### Header Commands
- `IN;` - Initialize plotter
- `VS{feedSpeed};` - Set feed speed (mm/s)
- `VU{rapidSpeed};` - Set rapid speed (mm/s)
- `SV{vacuumZone};` - Set vacuum zone width (increments)
- `XX306,{extractionHeight};` - Set extraction position height (material thickness + 3.3mm)
- `XX308,1,{underlayThickness};` - Enable underlay support
- `AS,{accelDown},{accelUp};` - Set acceleration
- `ZP{zpValue},{zpOffset};` - Set Z-axis top position (material thickness + 1mm safety)

### Toolpath Commands
- `SP{tool};` - Select tool (11, 21, or 31)
- `XX150,{rpm};` - Set spindle speed (RPM)
- `PU{x},{y};` - Pen up (move without cutting)
- `MW{x},{y},{z};` - Move with tool down (cutting move)
  - Z values are negative (cutting into material)
  - Example: `MW10000,20000,-500;` = move to (100mm, 200mm) cutting to -5mm depth

### Tool Change Sequence
When tool changes occur:
1. `ZP{zpValue},{zpOffset};` - Retract to safe position
2. `XX220;` - Move to tool change position
3. `SP{newTool};` - Select new tool
4. `ZP{zpValue},{zpOffset};` - Set Z-axis position after tool change
5. `XX150,{rpm};` - Set spindle speed for new tool
6. `XX306,{extractionHeight};` - Set extraction height (after every tool change)

### Trailer Commands
- `PU{x},{y};` - Pen up (lift tool at end)
- `ZT0;` - Tool up (retract tool)
- `XX308,0;` - Disable underlay support
- `PB2,0;` - Switch vacuum off

---

## Coordinate System

### Z-Axis Coordinate System
- **Z=0**: Table surface (where material is placed)
- **Material Top Surface**: Negative value = -materialThickness (e.g., -30mm for 30mm material)
- **Cutting Depth**: More negative = deeper cut (e.g., -5mm = 5mm material remain, 25mm removed for 30mm material)
- **Positive Z values are NOT allowed** (would cut into table!)

### Geometry Z Coordinates
- **Geometry Z**: Positive values (material remaining)
  - Example: +5mm = 5mm material remain
  - Example: +0mm = through-cutting (no material remain)
  - Example: +30mm = material surface (no cutting)

### CNC Z Coordinates (in HPGL)
- **CNC_Z = -geometryZ** (directly negative)
- **MW Command Z**: Negative values in increments
  - Example: geometry Z=5mm → CNC_Z = -5mm = -500 increments
  - Example: geometry Z=0mm → CNC_Z = 0mm = 0 increments (through-cutting)

### ZP (Z-Position oben)
- **Positive value** (absolute position above table)
- **Formula**: `ZP = Materialstärke * 100 + 100` (1mm safety margin)
- **Example**: 30mm material → ZP = 3000 + 100 = 3100 increments (31mm above table)

---

## Example Setup

Based on the provided Grasshopper definition:

```
Geometry Input:
  - Mesh from Quadrangulate component

Toolpath Parameters:
  - Step X (dx): 1.0 mm
  - Step Y (dy): 9.0 mm (Rasterpitch)
  - Margin: 0.0 mm
  - Start Left: False
  - Angle: 126°
  - Add Outline: False

Speed Parameters:
  - Feed Speed: 7.0 mm/s
  - Rapid Speed: 100.0 mm/s
  - Z Up: 0.0 mm (Sicherheitsabstand)

Material Parameters:
  - Material Thickness: 32.0 mm
  - Material Width: 650.0 mm
  - ZP Offset: 0

Tool Parameters:
  - Tool: 31 (default, not connected)
  - Spindle Speed: 30000 RPM

Acceleration:
  - Accel Down: 2
  - Accel Up: 4

Vacuum and Support:
  - Vacuum Strength: 1
  - Underlay Thickness: 200 (2mm, default)

Output:
  - Make PLT: True (default)
  - PLT output → File writing component → Desktop/CNC-Program.plt
```

---

## Tips and Best Practices

1. **Geometry Preparation:**
   - Ensure geometry is a closed, watertight mesh or surface
   - Use Quadrangulate or similar tools to prepare mesh if needed
   - Check geometry Z coordinates are positive (material remaining)

2. **Toolpath Spacing:**
   - Smaller Step X/Y = finer detail but longer toolpath
   - Larger Step X/Y = faster machining but less detail
   - Typical values: 1-10mm depending on tool diameter and material

3. **Speed Settings:**
   - Feed Speed: Match to material and tool capabilities
   - Rapid Speed: Can be higher (50-100 mm/s typical)
   - Spindle Speed: Match to tool manufacturer recommendations

4. **Material Settings:**
   - Always set Material Thickness correctly (affects ZP and XX306)
   - Material Width: Important for vacuum zone calculation on large plates
   - ZP Offset: Use for through-cutting scenarios

5. **Tool Changes:**
   - Tool List must match number of segments
   - Each tool can have different spindle speed via Tool Spindle Speeds
   - Tool change sequence includes safety retraction and extraction height reset

6. **Validation:**
   - Component warns if geometry Z exceeds material thickness
   - Component errors if geometry Z would result in positive CNC Z (would cut into table)
   - Check Stats output for toolpath length and point count

---

## Troubleshooting

### Issue: Component shows error "Geometry could not be converted to mesh"
- **Solution:** Ensure input is a valid mesh, brep, or surface. Try using Quadrangulate or Mesh components to prepare geometry.

### Issue: Z values seem incorrect
- **Check:** Geometry Z coordinates should be positive (material remaining)
- **Check:** Material Thickness is set correctly
- **Check:** CNC Z will be negative (CNC_Z = -geometryZ)

### Issue: Tool changes not working
- **Check:** Tool List count matches segment count
- **Check:** Tool values are 11, 21, or 31
- **Check:** Tool Spindle Speeds list matches tool requirements

### Issue: Vacuum not working
- **Check:** Vacuum Strength is set to 1-10 (0 = off)
- **Check:** Material Width is set correctly for vacuum zone calculation

### Issue: PLT file not generated
- **Check:** Make PLT is set to True
- **Check:** File writing component is properly connected
- **Check:** File path and permissions are correct

---

## Version History

- **v1.0.16**: Corrected Z-axis coordinate system (geometry Z positive → CNC Z negative, ZP positive)
- **v1.0.15**: Added Underlay Thickness input, XX308 always enabled
- **v1.0.14**: Vacuum strength integrated into PB2 command
- **v1.0.13**: Added Vacuum Strength and Extraction Height (XX306)
- **v1.0.12**: Added Tool Change functionality
- **v1.0.11**: Material Width input for vacuum zones
- **v1.0.10**: ZP Offset for through-cutting
- **v1.0.9**: Removed unnecessary HPGL commands (XX62, XX81, XX82)
- **v1.0.8**: ZP safety margin reduced from 10mm to 1mm

---

## Additional Resources

- **HPGL Manual**: See `Hpgl_Man_generation3.pdf` for complete HPGL command reference
- **Tool Change Strategy**: See `documentations/TOOL_CHANGE_STRATEGY.md`
- **HPGL XX Commands**: See `documentations/HPGL_XX_COMMANDS.md`
- **Component Overview**: See `documentations/COMPONENTS_OVERVIEW.md`

---

## Support

For issues, questions, or feature requests, please visit:
- **GitHub**: https://github.com/Moeewe/LARGER.slicer
- **Documentation**: See `documentations/` folder in project

---

*Last updated: Version 1.0.16*




