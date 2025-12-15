# Automatic Tool Change Strategy for Multiple Cutting Passes

## Overview

This document outlines strategies for implementing automatic tool changes when different cutting passes require different cutters. This is useful for multi-pass operations where different tools are needed for roughing, finishing, or different depth ranges.

## Use Cases

### 1. **Multi-Pass Cutting by Depth**
- **Roughing Pass**: Large diameter cutter (e.g., Fräser 4, 6mm) for fast material removal
- **Finishing Pass**: Smaller diameter cutter (e.g., Fräser 1, 3mm) for fine details
- **Through-Cut Pass**: Appropriate cutter based on material thickness

### 2. **Different Tools for Different Geometry Regions**
- **Large Areas**: Fast, large diameter cutter
- **Small Details**: Precise, small diameter cutter
- **Edges/Contours**: Specialized edge cutter

### 3. **Progressive Depth Cutting**
- **Pass 1**: Cut to 50% depth with roughing cutter
- **Pass 2**: Cut to 75% depth with medium cutter
- **Pass 3**: Final cut to full depth with finishing cutter

## Implementation Strategies

### Strategy 1: Cutter List per Segment (Current Implementation)

**How it works:**
- User provides a list of cutters (or tool positions) corresponding to each path segment
- Each segment can use a different cutter
- Tool changes are automatically inserted between segments

**Pros:**
- Full user control
- Works with existing segment-based structure
- Simple to implement

**Cons:**
- Manual assignment required
- User must know which segment needs which tool

**Example:**
```
Segments: [Segment1, Segment2, Segment3]
Cutters: [Fräser 4, Fräser 4, Fräser 1]
Result: Segment1-2 use Fräser 4, Segment3 uses Fräser 1 (tool change between Segment2 and Segment3)
```

### Strategy 2: Automatic Assignment by Depth (Recommended for Future)

**How it works:**
- Analyze geometry Z values for each segment
- Automatically assign cutters based on cutting depth requirements
- Use larger cutters for deeper cuts, smaller for shallow/fine work

**Implementation:**
```csharp
// Pseudo-code
foreach (segment in segments)
{
    double minZ = segment.Min(z => z.Z);
    double cuttingDepth = materialThickness - minZ;
    
    if (cuttingDepth > 20mm)
        cutter = FindCutterByMaxDepth(cuttingDepth); // Fräser 4 for deep cuts
    else if (cuttingDepth < 5mm)
        cutter = FindCutterByDiameter(3.0); // Fräser 1 for fine work
    else
        cutter = FindCutterByDiameter(4.0); // Fräser 2 for medium work
}
```

**Pros:**
- Automatic optimization
- User doesn't need to manually assign tools
- Intelligent tool selection

**Cons:**
- More complex logic
- May need user override capability

### Strategy 3: Pass-Based Tool Assignment

**How it works:**
- User defines multiple "passes" with different cutters
- Each pass processes all or selected segments
- Tool changes occur between passes

**Example:**
```
Pass 1 (Roughing): Fräser 4, all segments, 80% depth
Pass 2 (Finishing): Fräser 1, all segments, 100% depth
Pass 3 (Outline): Fräser 2, outline only, through-cut
```

**Implementation:**
- New input: List of Pass objects
- Each Pass contains: Cutter, Depth, Segment filter
- Generate toolpath for each pass sequentially

### Strategy 4: Geometry-Based Automatic Assignment

**How it works:**
- Analyze segment characteristics (length, curvature, area)
- Assign tools based on geometry properties
- Large straight segments → large cutter
- Small curved segments → small cutter

**Implementation:**
```csharp
foreach (segment in segments)
{
    double segmentLength = CalculateLength(segment);
    double curvature = CalculateCurvature(segment);
    double area = CalculateArea(segment);
    
    if (segmentLength > 100mm && curvature < 0.1)
        cutter = Fräser4; // Large, straight → large cutter
    else if (curvature > 0.5 || area < 10mm²)
        cutter = Fräser1; // Small, curved → small cutter
    else
        cutter = Fräser2; // Medium
}
```

## Current Implementation Status

### ✅ Implemented
- Tool change sequence (XX220, SP, ZP, XX150)
- Tool list per segment input
- Cutter database with specifications
- Cutter validation (depth limits)

### 🔄 In Progress
- Cutter Selector component (separate component)
- Cutter object passing between components

### 📋 Future Enhancements
- Automatic cutter assignment by depth
- Pass-based tool assignment
- Geometry-based automatic assignment
- Cutter optimization suggestions

## Recommendations

### For Immediate Use
1. **Use Cutter Selector Component**: Select cutter explicitly for each operation
2. **Manual Tool List**: Provide tool list matching segments when different tools are needed
3. **Validation**: Always validate cutter capabilities before generating toolpath

### For Future Development
1. **Implement Strategy 2 (Depth-Based)**: Most practical automatic assignment
2. **Add Pass System**: For multi-pass operations (roughing → finishing)
3. **User Override**: Always allow manual override of automatic assignments

## Example Workflow

### Manual Multi-Tool Setup
```
1. Cutter Selector → Fräser 4 (for roughing)
2. CNC Program → Generate roughing toolpath
3. Cutter Selector → Fräser 1 (for finishing)
4. CNC Program → Generate finishing toolpath (same geometry, different cutter)
5. Combine toolpaths or run separately
```

### Automatic Multi-Tool (Future)
```
1. CNC Program → Geometry input
2. Component analyzes depth requirements
3. Automatically assigns:
   - Fräser 4 for segments with depth > 20mm
   - Fräser 1 for segments with depth < 5mm
   - Fräser 2 for all others
4. Generates toolpath with automatic tool changes
```

## Technical Notes

### Tool Change Sequence
The current implementation uses this sequence:
1. `ZP{zpValue},{zpOffset};` - Retract to safe position
2. `XX220;` - Move to tool change position
3. `SP{newTool};` - Select new tool
4. `ZP{zpValue},{zpOffset};` - Set Z-axis position after tool change
5. `XX150,{rpm};` - Set spindle speed for new tool
6. Continue with next segment

### Cutter Validation
Before tool change, validate:
- Material thickness ≤ MaxCuttingDepth
- Actual cutting depth ≤ MaxCuttingDepth (for through-cut) or MaxSurfaceDepth (for surface)
- Tool position compatibility (11, 21, 31)

### Extraction Height Adjustment
Extraction height (XX306) is automatically calculated based on deepest cut:
- Formula: `(materialThickness - minGeometryZ) + 3.3mm`
- Prevents Z-axis blocking by Saugglocke
- Adjusted for each tool if needed

