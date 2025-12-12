# DXR File Format Documentation

## Overview

DXR (DXR.KUKA) is a robot control file format used for KUKA robots in 3D printing applications. This document describes the structure, commands, and their required positions based on reference files.

## File Structure

The DXR file must follow this exact order:

1. **Header Section** (comment lines starting with `;`)
2. **Header Separator** (`;=================================`)
3. **G90 Command** (standalone, NO line number)
4. **Machine Settings** (optional, with N-numbers)
5. **Movement Commands** (with N-numbers)

## 1. Header Section

All header lines are comments (start with `;`). Required fields:

```
;ProgRunTimeTotal =[seconds]
;machine_type =[DXR.KUKA]
;post_processor_version =[V1.0.0]
;1 SD.ACT.GEN.DESC.NAME ="DEFAULT"
;number of rows in org. file =[total_lines]
;number of movement rows = [movement_count]
;number of layers =[layer_count]
;Xmin = [xmin_value]
;Xmax = [xmax_value]
;Ymin = [ymin_value]
;Ymax = [ymax_value]
;Zmin = [zmin_value]
;Zmax = [zmax_value]
;Eges = IC[total_extrusion]
; config end
;=================================
```

### Header Field Descriptions

- **ProgRunTimeTotal**: Estimated total program runtime in seconds (integer)
- **machine_type**: Always `[DXR.KUKA]` for KUKA robots
- **post_processor_version**: Version string (e.g., `[V1.0.0]`)
- **number of rows in org. file**: Total number of lines in original input file
- **number of movement rows**: Count of actual movement commands
- **number of layers**: Number of distinct Z-layers (can be `[X]` if unknown)
- **Xmin/Xmax/Ymin/Ymax/Zmin/Zmax**: Bounding box coordinates (3 decimal places)
- **Eges**: Total extrusion amount in IC format (3 decimal places)

## 2. G90 Command (Critical Position)

**IMPORTANT**: `G90` must appear as a standalone line immediately after the header separator, **WITHOUT an N-number**.

```
;=================================
G90
```

**Why this matters**: 
- `G90` sets absolute positioning mode
- It must be placed before any numbered commands (N10, N11, etc.)
- Having `G90` with an N-number (like `N10 G90`) can cause the robot to hang/wait
- This is a common source of program execution failures

## 3. Machine Settings (Optional)

Machine settings use N-numbers starting from N10 (or higher if needed). Each setting consists of:
1. Variable assignment
2. Subroutine call to apply the setting

### Temperature Settings

```
N10 V.P.VAR_heatbedtemp = 60
N11 L heatbedTemp_sub.nc

N20 V.P.VAR_extrudertemp = 220
N21 L extruderTemp_sub.nc

N30 V.P.VAR_fan = 16
N31 L fan_sub.nc
```

### Command Descriptions

- **V.P.VAR_heatbedtemp**: Sets heated bed temperature in °C
- **V.P.VAR_extrudertemp**: Sets extruder/nozzle temperature in °C
- **V.P.VAR_fan**: Sets cooling fan percentage (0-100)
- **L filename.nc**: Calls a subroutine file to apply the setting
  - `L heatbedTemp_sub.nc`: Applies bed temperature
  - `L extruderTemp_sub.nc`: Applies extruder temperature
  - `L fan_sub.nc`: Applies fan speed

**Note**: The `L` command calls a subroutine that may block execution until the setting is applied. Ensure these subroutine files exist on the robot controller.

### Layer Control Variables

```
N10 V.E.GLOBAL[27] = 0
N11 L layer_sub.nc
N12 L wall_sub.nc
```

- **V.E.GLOBAL[27]**: Layer index variable
  - Stores the current layer number (0, 1, 2, 3, ...)
  - Must be set before calling layer-related subroutines
  - Typically starts at 0 for the first layer
  - Increments with each new layer
  - Used by subroutines to identify which layer is being printed
  
- **L layer_sub.nc**: Layer change subroutine
  - Called after setting the layer index
  - Performs layer-specific initialization
  - May include Z-axis positioning, pause, or other layer setup
  
- **L wall_sub.nc**: Wall printing subroutine (optional)
  - Called for wall/perimeter printing
  - May be used to set specific parameters for wall layers
  - Typically called only for the first layer (index 0)

**Usage Pattern:**
```
; First layer
N10 V.E.GLOBAL[27] = 0
N11 L layer_sub.nc
N12 L wall_sub.nc
... (first layer movements) ...

; Second layer
N404 V.E.GLOBAL[27] = 1
N405 L layer_sub.nc
... (second layer movements) ...

; Third layer
N636 V.E.GLOBAL[27] = 2
N637 L layer_sub.nc
... (third layer movements) ...
```

**Note**: The index `[27]` appears to be a fixed slot for layer information. Other indices may be used for different global variables in the robot controller.

## 4. Movement Commands

Movement commands use N-numbers and follow this general format:

```
N{number} G1 F{speed} X{x} Y{y} Z{z} [A{a} B{b} C{c}] [G91 XE=[{extrusion}*P1] G90]
```

### Movement Command Components

- **N{number}**: Line number (increments of 10: N10, N20, N30, ...)
- **G1**: Linear interpolation (movement command)
- **G0**: Rapid positioning (non-extrusion moves)
- **F{speed}**: Feedrate in mm/min
- **X, Y, Z**: Absolute coordinates (3 decimal places)
- **A, B, C**: Optional rotation angles (3 decimal places)
- **G91 XE=[{value}*P1] G90**: Extrusion command
  - `G91`: Switch to relative mode for extrusion
  - `XE=[{value}*P1]`: Relative extrusion amount (P1 is a multiplier variable)
  - `G90`: Switch back to absolute mode

### Movement Examples

**Simple movement without extrusion:**
```
N13 G1 X899.633 Y900.000 Z32.000 A0.000 B0.000 C0.000 F3000.000
```

**Movement with extrusion:**
```
N15 G1 Y903.167 A0.000 B0.000 C0.000 G91 XE=[38.529*P1] G90 F1800.000
```

**Rapid positioning (G0):**
```
N260 G0 F24000 X234.383 Y159.091 Z1.8
```

**Movement with partial coordinates:**
```
N80 G1 X305.921 Z37.052
```

### Important Notes on Movements

1. **G90 at end**: Some movements end with `G90` (especially those with extrusion), but not all movements require it
2. **Coordinate order**: Coordinates can appear in any order (X, Y, Z, A, B, C)
3. **Missing coordinates**: If a coordinate doesn't change, it can be omitted
4. **Speed placement**: `F{speed}` can appear at the beginning or end of the line
5. **Extrusion format**: `G91 XE=[{value}*P1] G90` is the standard extrusion format

## 5. End Sequence

At the end of the program, machine settings should be turned off:

```
N9999994 V.P.VAR_heatbedtemp = 0
N9999995 L heatbedTemp_sub.nc
N9999996 V.P.VAR_fan = 0
N9999997 L fan_sub.nc
N9999998 V.P.VAR_extrudertemp = 0
N9999999 L extruderTemp_sub.nc
M29
```

- **M29**: Program end command

## Common Issues and Solutions

### Issue: Robot does not perform brake test / start movement after startup

**Symptoms:**
- Robot does not perform required brake test / start movement after startup
- Robot cannot move to home position
- Drive button blinks continuously
- Error message: "Druck abgebrochen bei XYZ jeweils 0"
- Control cabinet display shows: "Neu Starten" or "Warnschwelle für Bremstest erreicht mit 0 Stunden Restlaufzeit"
- KUKA Smartpad shows: "Quitt Fahrtfreigabe gesamt Verursacher KS" and "Active-Status erforderlich"
- Status indicators not all green

**Solution:**
The CNC program on the robot controller needs to be manually reselected. See detailed troubleshooting steps in `KUKA_ROBOT_TROUBLESHOOTING.md`.

**Quick Fix:**
1. Switch key from Remote to Gear icon
2. Enter T1 mode, then switch back to Remote
3. Open navigation (blue gear icon → Open → orange X)
4. Select "cnc" folder and click "Anwählen"
5. Reset program (tap yellow "R" square → "Programm zurücksetzen")
6. Switch to EXT mode, then back to Remote

For complete step-by-step instructions in German and English, see: `documentations/KUKA_ROBOT_TROUBLESHOOTING.md`

---

### Issue: Robot stops/hangs when reading machine settings

**Possible causes:**
1. **G90 with N-number**: `G90` must be standalone without N-number
2. **Missing subroutines**: `L` commands call subroutines that must exist on the controller
3. **Blocking subroutines**: Subroutines may wait for conditions that never occur

**Solutions:**
- Ensure `G90` appears as standalone line after header separator
- Verify all `.nc` subroutine files exist on robot controller
- Check that subroutines return control properly

### Issue: Robot doesn't execute movements

**Possible causes:**
1. Missing `G90` after header
2. Incorrect N-number sequence
3. Invalid coordinate format

**Solutions:**
- Verify `G90` is present (standalone, no N-number)
- Ensure N-numbers increment properly (10, 20, 30, ...)
- Check coordinate precision (3 decimal places)

## Reference Examples

### Example 1: Minimal Structure
```
;Header comments...
;=================================
G90
N10 V.P.VAR_heatbedtemp = 60
N11 L heatbedTemp_sub.nc
N20 G1 X100.000 Y100.000 Z10.000 F3000.000
```

### Example 2: With Extrusion
```
;Header comments...
;=================================
G90
N10 V.P.VAR_extrudertemp = 220
N11 L extruderTemp_sub.nc
N20 G1 X100.000 Y100.000 Z10.000 G91 XE=[10.000*P1] G90 F3000.000
```

### Example 3: Complex Setup
```
;Header comments...
;=================================
G90
N10 V.E.GLOBAL[27] = 0
N11 L layer_sub.nc
N12 L wall_sub.nc
N20 G1 X899.633 Y900.000 Z32.000 F3000.000
N30 G1 Z2.000
N40 G1 Y903.167 G91 XE=[38.529*P1] G90 F1800.000
```

## Best Practices

1. **Always include standalone `G90`** after header separator
2. **Use sequential N-numbers** in increments of 10
3. **Group related settings** together (e.g., all temperature settings)
4. **Verify subroutine files exist** before using `L` commands
5. **Test with minimal settings first** to isolate issues
6. **Use 3 decimal places** for all coordinates
7. **Include F (feedrate)** in movement commands
8. **End program with M29** and shutdown sequence

## Version History

- **V1.0.0**: Initial documentation based on reference files
- Documented critical `G90` positioning requirement
- Identified common hanging issues with machine settings

