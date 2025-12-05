# DXR Postprocessor Syntax Analysis & Improvement Recommendations

## Overview
Based on the KUKA-CNC syntax description, this document identifies discrepancies between the current implementation and the official syntax, and provides recommendations for improvements.

## Key Findings

### 1. Movement Command Format

**Current Implementation:**
```
N{line_num} G1 {coordinates} G91 XE=[{P1:F6}*P1] G90 F{F1:F3}
```

**Syntax Description Example:**
```
N30 G1 X303.695 Y654.171 Z241.34 A0.0 B-13.72197 C0.0 G91 XE=162.385*P11 G90
```

**Actual DXR File Format:**
```
N100 G1 F6059.000 X121.040 Y65.250 Z2.000 G91 XE=[56.500000*P1] G90
```

**Issues Identified:**
1. **F (Feedrate) Position**: Current code places F at the END, but actual DXR files show F at the BEGINNING (after G1)
2. **Euler Angles (A, B, C)**: Syntax description shows A, B, C are present, but current code only includes them if detected in input
3. **Extrusion Format**: Current format `XE=[{value}*P1]` matches actual files (with brackets), but syntax description shows `XE=162.385*P11` (without brackets, different variable)

**Recommendation:**
- Move F to beginning: `N{line_num} G1 F{F1:F3} {coordinates} G91 XE=[{P1:F6}*P1] G90`
- Always include A, B, C with default 0.0 if not provided
- Keep brackets in extrusion format (matches actual files)

### 2. Temperature & Fan Control

**Current Implementation:**
```
V.P.VAR_heatbedtemp = {value}
V.P.VAR_extrudertemp = {value}
V.P.VAR_fan = {value}
```

**Syntax Description:**
```
Printbed Zones:
  V.E.GLOBAL_BOOL[72] = TRUE  (zone 1 on)
  V.E.GLOBAL[?] = {temp}      (zone 1 temp - not defined yet)
  V.E.GLOBAL_BOOL[74] = TRUE  (zone 2 on)
  V.E.GLOBAL_BOOL[76] = TRUE  (zone 3 on)
  V.E.GLOBAL_BOOL[78] = TRUE  (zone 4 on)

Extruder Zones:
  V.E.GLOBAL_BOOL[44] = TRUE  (filling zone cooling on)
  V.E.GLOBAL[41] = 30         (filling zone temp)
  V.E.GLOBAL_BOOL[24] = TRUE  (heating extruder zone 1 on)
  V.E.GLOBAL[55] = 180        (heating extruder zone 1 temp)
  V.E.GLOBAL_BOOL[26] = TRUE  (heating extruder zone 2 on)
  V.E.GLOBAL[57] = 180        (heating extruder zone 2 temp)
  V.E.GLOBAL_BOOL[40] = TRUE  (heating nozzle zone on)
  V.E.GLOBAL[71] = 180        (heating nozzle zone temp)

Fan:
  V.E.GLOBAL_BOOL[44] = TRUE  (fan on - Extruder 1)
  V.E.GLOBAL[3] = 80          (fan speed - Extruder 1)
```

**Issue:**
The syntax description shows a completely different variable system using `V.E.GLOBAL_BOOL[]` and `V.E.GLOBAL[]` instead of `V.P.VAR_*`. However, the current implementation might be using a simplified/abstracted version that works with the robot controller.

**Recommendation:**
- **Option A (Recommended)**: Keep current `V.P.VAR_*` format if it works with your robot setup
- **Option B**: Add support for multi-zone heating if needed (requires more complex MachineSettings)
- **Option C**: Make it configurable which format to use

### 3. Layer-Type Subroutines

**Syntax Description Shows:**
Different subroutines for different layer types:
- `L_skirt_sub.nc` - for skirt/raft
- `L_skin_sub.nc` - for solid layers
- `L_wall_sub.nc` - for inner/outer perimeters
- `L_infill_sub.nc` - for infill
- `L_retract_sub` - for retraction
- `L_layer_sub.nc` - for layer changes

**Current Implementation:**
Only uses `L layer_sub.nc` for layer changes.

**Recommendation:**
- Add support for detecting layer types from GCode comments (e.g., `;TYPE:SKIRT`, `;TYPE:SKIN`, `;TYPE:WALL-OUTER`, `;TYPE:WALL-INNER`, `;TYPE:INFILL`)
- Insert appropriate subroutine calls before corresponding movements
- This allows different dynamic settings per layer type

### 4. Extrusion Without Movement (Retraction)

**Syntax Description:**
```
G1 for Linear movement
G91 - Incremental dimension (here just for the Extrusion to not give it absolute)
Extruder movement, here retraction
G90 Absolute dimension (for everything following)
Feed in mm/min (just used to give a feed without)
Example: G1 G91 XE=-1-300*P11 G90 F30000
```

**Current Implementation:**
Does not handle standalone retraction commands (extrusion without movement).

**Recommendation:**
- Detect retraction commands (negative E values without X/Y/Z movement)
- Generate: `N{line_num} G1 G91 XE=[{retraction}*P1] G90 F{feedrate}`

### 5. Header Fields

**Current Implementation:**
- `ProgRunTimeTotal =[seconds]` ✓
- `Eges = IC[...]` - Currently removed, but syntax shows it should be present

**Syntax Description:**
- `Eres = IC [1055468.943]` - Material volume in mm³

**Recommendation:**
- Add `Eges` or `Eres` field back to header (total extrusion/material volume)
- Calculate from sum of all P1 values

### 6. Program End Sequence

**Current Implementation:**
```
N9999994 V.P.VAR_heatbedtemp = 0
N9999995 L heatbedTemp_sub.nc
N9999996 V.P.VAR_fan = 0
N9999997 L fan_sub.nc
N9999998 V.P.VAR_extrudertemp = 0
N9999999 L extruderTemp_sub.nc
M29
```

**Syntax Description:**
Shows `M29` as end command (matches current implementation).

**Status:** ✓ Correct

### 7. Euler Angles (A, B, C)

**Syntax Description:**
Shows Euler angles are part of movement commands:
```
A0.0 B-13.72197 C0.0
```

**Current Implementation:**
Only includes A, B, C if detected in input PTP lines.

**Recommendation:**
- Always include A, B, C in movement commands
- Default to `A0.0 B0.0 C0.0` if not provided
- This ensures consistent format and allows for future non-planar printing

### 8. G90 Standalone Command

**Current Implementation:**
Does not add standalone `G90` after header (based on comment in code).

**Syntax Description:**
Does not explicitly show standalone `G90`, but DXR_FORMAT_DOCUMENTATION.md indicates it's critical.

**Recommendation:**
- Verify if standalone `G90` is needed (check with actual robot behavior)
- If needed, add after header separator: `;=================================\nG90`

## Priority Recommendations

### High Priority (Critical for Correctness)
1. **Fix F position**: Move F to beginning of movement command
2. **Always include Euler angles**: Add A0.0 B0.0 C0.0 to all movements
3. **Add Eges field**: Include total extrusion in header

### Medium Priority (Functionality Enhancement)
4. **Layer-type subroutines**: Detect and insert appropriate subroutines (skirt, skin, wall, infill)
5. **Retraction handling**: Support standalone retraction commands
6. **Multi-zone temperature**: If needed, support multiple heating zones

### Low Priority (Nice to Have)
7. **Extrusion variable**: Consider if P11 vs P1 matters (syntax shows P11, files show P1)
8. **Standalone G90**: Verify if needed based on robot behavior

## Implementation Notes

### Movement Command Format (Recommended)
```csharp
// Format: N{line_num} G1 F{feedrate} X{x} Y{y} Z{z} A{a} B{b} C{c} G91 XE=[{extrusion}*P1] G90
string dxrLine = $"N{line_num} G1 F{F1:F3} X{lastX:F3} Y{lastY:F3} Z{lastZ:F3} A{lastA:F3} B{lastB:F3} C{lastC:F3} G91 XE=[{P1:F6}*P1] G90";
```

### Layer Type Detection (Recommended)
```csharp
// Parse GCode comments for layer type
// ;TYPE:SKIRT -> L_skirt_sub.nc
// ;TYPE:SKIN -> L_skin_sub.nc
// ;TYPE:WALL-OUTER -> L_wall_sub.nc
// ;TYPE:WALL-INNER -> L_wall_sub.nc
// ;TYPE:INFILL -> L_infill_sub.nc
```

### Header Eges Field (Recommended)
```csharp
// Calculate total extrusion
double totalExtrusion = P1_list.Sum();
dxrHeader.Add($";Eges = IC[{totalExtrusion:F3}]");
```

## Testing Recommendations

1. Test with actual robot to verify F position doesn't cause issues
2. Verify Euler angles default to 0.0 works correctly
3. Test layer-type subroutines if robot controller supports them
4. Verify retraction commands work correctly
5. Check if Eges field is required by robot controller

