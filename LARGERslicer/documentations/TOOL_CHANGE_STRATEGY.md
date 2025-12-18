# Tool Change Strategy for CNC Program Component

## Overview
This document outlines the strategy for implementing tool changes in the CNC Program component, allowing multiple tools (11, 21, 31) to be used within a single toolpath.

## HPGL Commands for Tool Changes

### Key Commands
1. **XX220** - Move to Tool Change Position
   - Syntax: `XX220;` or `XX220, ChangePos;`
   - Moves module carriage to tool change position
   - Rotates tools to service-friendly position
   - Cutter changes to offline after execution (for safety)

2. **SP** - Select Tool
   - Syntax: `SP{tool};` (11, 21, or 31)
   - Already implemented in current version

3. **ZP** - Z-Axis Position
   - Syntax: `ZP{value},{downY};`
   - Must be set before and after tool change
   - Value = Materialstärke + 10mm safety (in increments: 100 increments = 1mm)

## User Requirements

Based on user feedback:
> "Vor Nach dem Wechseln von jedem Fräskopf muss ein Befehl hinzugefügt werden, der die höhe vom material definiert. Materialstärke hinzufügen."

Translation: "Before and after changing each tool head, a command must be added that defines the material height. Add material thickness."

## Implementation Strategy

### Option 1: Tool Assignment per Segment (Recommended)
- **Input**: List/Array of tool numbers (11, 21, 31) corresponding to each path segment
- **Logic**: 
  - Group consecutive segments with the same tool
  - When tool changes between segments:
    1. Retract to safe height
    2. Add `ZP{materialThickness*100 + 1000},0;` (before tool change)
    3. Add `XX220;` (move to tool change position)
    4. Add `SP{newTool};` (select new tool)
    5. Add `ZP{materialThickness*100 + 1000},0;` (after tool change)
    6. Add `XX150,{spindleSpeed};` (set spindle speed for new tool)
    7. Continue with next segment

### Option 2: Tool Change Points
- **Input**: List of indices where tool changes should occur
- **Logic**: Insert tool change sequence at specified points in the path

### Option 3: Automatic Tool Assignment
- **Input**: Rules for tool assignment (e.g., based on segment length, depth, or geometry)
- **Logic**: Automatically assign tools based on heuristics

## Recommended Implementation (Option 1)

### New Input Parameters
1. **Tool List** (optional)
   - Type: `List<int>` or `List<GH_Integer>`
   - Description: Tool number (11, 21, or 31) for each path segment
   - Default: Use single tool from existing "Tool" parameter
   - If provided, must match number of segments

2. **Tool Change Spindle Speeds** (optional)
   - Type: `List<double>` or `List<GH_Number>`
   - Description: Spindle speed (RPM) for each tool
   - Default: Use single spindle speed from existing parameter
   - If provided, must match number of unique tools

### Implementation Flow

```csharp
// Pseudo-code for tool change logic
List<Point3d> pathPoints = ...; // Generated path points
List<List<Point3d>> segments = ...; // Path segments
List<int> toolAssignments = ...; // Tool for each segment

int currentTool = toolAssignments[0];
List<string> pltLines = new List<string>();

// Initial setup with first tool
pltLines.Add($"SP{currentTool};");
pltLines.Add($"XX150,{spindleSpeed};");
// ... other initial commands ...

// Process segments
for (int i = 0; i < segments.Count; i++)
{
    int segmentTool = toolAssignments[i];
    
    // Check if tool change needed
    if (segmentTool != currentTool)
    {
        // Before tool change: ZP command
        int zpValue = (int)Math.Round(materialThickness * 100.0) + 1000;
        pltLines.Add($"ZP{zpValue},0;");
        
        // Move to tool change position
        pltLines.Add("XX220;");
        
        // Select new tool
        pltLines.Add($"SP{segmentTool};");
        
        // After tool change: ZP command again
        pltLines.Add($"ZP{zpValue},0;");
        
        // Set spindle speed for new tool (if different speeds per tool)
        if (toolSpindleSpeeds != null && toolSpindleSpeeds.Count > 0)
        {
            int toolIndex = GetToolIndex(segmentTool); // 0=11, 1=21, 2=31
            pltLines.Add($"XX150,{toolSpindleSpeeds[toolIndex]};");
        }
        
        currentTool = segmentTool;
    }
    
    // Add segment path points
    foreach (Point3d pt in segments[i])
    {
        int zIncrements = UU(pt.Z) - materialThicknessIncrements;
        pltLines.Add($"MW{UU(pt.X)},{UU(pt.Y)},{zIncrements};");
    }
}
```

### Considerations

1. **Safety**: XX220 command puts cutter in offline mode - may need to handle this
2. **Material Thickness**: ZP must be set before and after tool change
3. **Spindle Speed**: May need different speeds for different tools
4. **Retract Height**: Should retract before tool change to avoid collisions
5. **Path Continuity**: Tool changes break the continuous path - user must understand this

## Alternative: Simplified Approach

If full tool change automation is complex, we could:
1. Allow user to manually insert tool change commands at specific points
2. Provide a helper component that adds tool change sequences
3. Support multiple separate PLT outputs (one per tool)

## Next Steps

1. Review with user: Which approach is preferred?
2. Implement Option 1 (Tool Assignment per Segment) as recommended
3. Test with real-world scenarios
4. Update documentation and examples













