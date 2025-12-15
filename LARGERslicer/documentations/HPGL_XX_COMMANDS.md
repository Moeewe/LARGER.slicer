# HPGL XX Commands Reference

## Overview
XX commands are Zünd-specific extensions to the HPGL standard. They use the format `XX{index},{parameters};` where the index refers to a specific function.

## XX Commands Used in CNC Program Component

### XX62 - Continuous Path
- **Purpose**: Enables continuous path mode for smoother tool movements
- **Syntax**: `XX62;`
- **Usage**: Called before starting the toolpath to enable continuous path processing
- **Location in code**: Before ZP command in header section

### XX81 - Stop Angle
- **Purpose**: Sets the stop angle for directional changes
- **Syntax**: `XX81;`
- **Usage**: Called after ZP to configure stop angle settings
- **Location in code**: After ZP command, before AS command

### XX82 - Additional Tool Offset Correction
- **Purpose**: Applies additional tool offset correction
- **Syntax**: `XX82;`
- **Usage**: Called after AS (Acceleration) command to apply tool offset
- **Location in code**: After AS command, before path starts

### XX150 - Router/URT Speed Selection (RPM)
- **Purpose**: Sets the spindle/router speed in RPM
- **Syntax**: `XX150,{rpm};`
- **Parameters**: 
  - `{rpm}`: Spindle speed in rotations per minute (e.g., 10000)
- **Usage**: Called after tool selection (SP) to set the spindle speed
- **Location in code**: 
  - After initial SP command in header
  - After each tool change (SP command)

### XX220 - Move to Tool Change Position
- **Purpose**: Moves the module carriage to tool change position
- **Syntax**: `XX220;` or `XX220, ChangePos;`
- **Behavior**: 
  - Rotates tools to service-friendly position
  - Cutter changes to offline mode after execution (for safety)
- **Usage**: Called during tool change sequence
- **Location in code**: In tool change sequence: ZP → XX220 → SP → ZP → XX150

### XX308 - Additional Underlay
- **Purpose**: Enables additional underlay support
- **Syntax**: `XX308,1;` (enable) or `XX308,0;` (disable)
- **Usage**: Optional, controlled by "Underlay" input parameter
- **Location in code**: After SV command, before XX62

## Command Sequence in PLT Output

```
PB2,1;
ZT1;MA;
VS{feedSpeed};
VU{rapidSpeed};
[empty line]
SV,{vacuumZone};
XX308,1;  (if underlay enabled)
XX62;
ZP{zpValue},0;
XX81;
AS,{accelDown},{accelUp};
XX82;
SP{tool};
XX150,{spindleSpeed};
[path commands...]
```

## Tool Change Sequence

When tool changes:
```
ZP{zpValue},0;     (before tool change)
XX220;             (move to tool change position)
SP{newTool};       (select new tool)
ZP{zpValue},0;     (after tool change)
XX150,{rpm};       (set spindle speed for new tool)
```

## Notes

- All XX commands are Zünd-specific and not part of standard HPGL
- XX commands must be terminated with semicolon (`;`)
- Some XX commands (like XX220) put the cutter in offline mode for safety
- XX150 must be set after each tool change to ensure correct spindle speed










