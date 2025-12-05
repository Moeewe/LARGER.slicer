# Komponenten Inputs/Outputs Analyse

## Basis-Klasse (BottomLayerPatternBase)
**Gemeinsame Inputs:**
- Curve (Index 0) - Boundary curve
- Print Width (Index 1) - Bead width
- Holes (Index 2, optional) - Inner boundary curves

**Gemeinsame Outputs:**
- Single Line Fill (Index 0) - Continuous path

## Komponenten-spezifische Inputs/Outputs

### 1. InfillSpiralComponent
**Zusätzliche Inputs:**
- Clockwise (Index 3) - Direction
- Min Radius (Index 4) - Stop radius

**Outputs:**
- Single Line Fill (Index 0) - Nur kontinuierlicher Pfad

### 2. InfillContourComponent
**Zusätzliche Inputs:**
- Use ArcWelder (Index 3) - Arc conversion

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 3. InfillGridComponent
**Zusätzliche Inputs:**
- Angle (Index 3) - Rotation
- Start Left (Index 4) - Start direction
- Spacing Y (Index 5) - Perpendicular spacing

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 4. InfillLinesComponent
**Zusätzliche Inputs:**
- Angle (Index 3) - Line direction
- Optimize Order (Index 4) - Path optimization

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 5. InfillHilbertComponent
**Zusätzliche Inputs:**
- Order (Index 3) - Recursion depth

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 6. InfillFermatSpiralsComponent
**Zusätzliche Inputs:**
- Min Radius (Index 3) - Stop radius
- Max Region Size (Index 4) - Region size
- Subdivide Regions (Index 5) - Subdivision flag

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 7. InfillContourZigzagHybridComponent
**Zusätzliche Inputs:**
- Use Zigzag (Index 3) - Enable zigzag

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 8. InfillEulerTransformationComponent
**Zusätzliche Inputs:**
- Offset Distance (Index 3) - Mitered offset
- Patch Odd Vertices (Index 4) - Auto-patch
- Use Concentric Traversal (Index 5) - Algorithm type

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 9. ContinuousToolpathComponent
**Zusätzliche Inputs:**
- Random Bridges (Index 3) - Bridge placement
- Bridge Density (Index 4) - Bridge frequency
- Handle Undercuts (Index 5) - Self-intersection handling

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad

### 10. ContinuousPathFromCurvesComponent
**Zusätzliche Inputs:**
- Angle (Index 3) - Line direction
- Random Bridges (Index 4) - Bridge placement
- Bridge Density (Index 5) - Bridge frequency

**Outputs:**
- Single Line Fill (Index 0) - Kontinuierlicher Pfad




