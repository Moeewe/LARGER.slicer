# Zünd G3 L-2500 - 3D CNC Milling Quick Start Guide

## Machine Overview

**Zünd Digital Cutter G3 L-2500**  
3-Axis CNC milling and cutting system for large-format digital fabrication

### Technical Specifications

| Specification | Value |
|--------------|-------|
| **Model** | Zünd G3 L-2500 Digital Cutter |
| **Working Area** | Width: 1800 mm, Length: 2500 mm |
| **Working Height** | 50 mm (standard operation) |
| **Opening Height** | 60 mm (maximum material clearance) |
| **Module Configuration** | 3-fold module carrier with interchangeable tools |
| **Vision System** | Integrated compact color camera (ICC) |

### Milling Module Specifications

**Routing Module RM-A, 1kW with MQL (Minimum Quantity Lubrication)**

- **Spindle**: Air-cooled high-frequency spindle with pneumatic 6mm collet (QC quick-change)
- **Power**: 1 kW
- **Collet Size**: 6mm shaft diameter
- **Cooling**: Minimum Quantity Lubrication (MQL) system
- **Dust Extraction**: Powerful extraction with electronically controlled suction bell
- **Z-Compensation**: Material surface compensation for precise depth control
- **Tool Change**: Automatic Router Bit Changer (ARC) with configurable magazine
  - Capacity: Up to 6 standard + 2 special routers (total 8 tools)
  - Features: Automatic cleaning function, scan detection for magazine positions

### Tool Set

**Universal Router Bit Set (6mm shaft diameter)**

**Zünd Multipurpose (9 pieces):**
- 2× R202-A (Ø2.0mm / 6mm / 1 flute)
- 2× R204-A (Ø4.0mm / 14mm / 1 flute)
- 2× R206-A (Ø6.0mm / 22mm / 1 flute)
- 1× R141-A (V-groove 90°)
- 2× E6-A (Ø6mm / 92° / 1 flute)

**Zünd ACM (6 pieces):**
- 2× R207-A (Ø2.0mm / 6mm / 1 flute)
- 2× R208-A (Ø3.0mm / 8mm / 1 flute)
- 2× R209-A (Ø4.0mm / 8mm / 1 flute)

### Vacuum System

- **Adjustable Vacuum Area**: Configurable to material size
- **Material Support**: 4× Fräsunterlage Sealgrip (1m × 1.6m)
- **Cutting Surface**: PLP cutting mat (anthrazit), 2.5mm thick, 1880mm × 2884mm (4-piece)

### Dust Extraction

- **Dust Collector**: CLEANTEX CTM 48 E industrial vacuum
- **Application**: Extraction of milling chips
- **Included**: Compact cleaning set

### Additional Features

- **Laser Pointer**: Integrated for precise material positioning
- **Cable Management**: Gantry-mounted hose guide system (minimum room height: 3.0m)
- **Vision System**: ICC compact color camera with perfect registration mark recognition
  - Works with low color contrast and poor lighting conditions
  - Handles reflective materials
  - Includes 3 sockets for driven tool connection
- **Framegrabber Box**: For ICC camera control via Zünd Cut Center (V3.5.6+) or MIND Software (V6.1+), USB-C connection

## Important Usage Notes

⚠️ **CRITICAL OPERATION INFORMATION**

1. **Primary Operation**: Standard cutting and 2D operations should be performed through **Zünd Cut Center** software
2. **Grasshopper Usage**: The provided Grasshopper script (`ZündHGPL PLT MSA.gh`) is specifically designed for **3D milling operations only**
3. **Material Limitations**: Maximum material thickness: 50mm working height, 60mm opening height
4. **Safety**: Always follow Zünd safety protocols and ensure proper dust extraction is active

## 3D Milling Workflow (Grasshopper)

### Prerequisites

- Rhino 8+ with Grasshopper
- LARGERslicer plugin installed (for CNC Program component)
- 3D geometry prepared in Rhino (Breps or Meshes)

### Setup Process

1. **Open Files**:
   - Launch `ZündHGPL PLT MSA.3dm` in Rhino
   - Open `ZündHGPL PLT MSA.gh` in Grasshopper

2. **Input Geometry**:
   - Reference your 3D geometry (Brep or Mesh)
   - Ensure geometry fits within working area (1800mm × 2500mm)
   - Verify Z-height does not exceed 50mm

3. **Configure Parameters**:
   - **Material Thickness**: Set actual material thickness in mm
   - **Pass Depth**: Stepdown per milling pass (typical: 2-5mm)
   - **Stepover**: Lateral spacing between toolpath lines (typical: 60-80% of tool diameter)
   - **Tool Selection**: Choose appropriate router bit from tool magazine
   - **Spindle Speed**: Set RPM according to material and tool (typical: 18,000-24,000 RPM)

4. **Generate Toolpath**:
   - Use **CNC Program** component to generate boustrophedon (zigzag) toolpath
   - Preview toolpath in Rhino viewport
   - Verify Z-coordinates are within safe limits

5. **Export PLT File**:
   - Set output filepath
   - Click export to generate HPGL/PLT file
   - File will include all necessary commands:
     - Tool selection (XX220)
     - Spindle speed (SP)
     - Vacuum control (PB2)
     - Z-positioning (ZP, MW)
     - Extraction height (XX306)
     - Underlay compensation (XX308)

6. **Transfer to Machine**:
   - Copy PLT file to Zünd Cut Center
   - Load material and secure with vacuum
   - Perform tool height calibration
   - Run job through Zünd Cut Center interface

## File Structure

```
00 - ZUENDT - 3D CNC MILLING/
├── README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md  # This guide
├── ZündHGPL PLT MSA.3dm                                     # Rhino template file
└── ZündHGPL PLT MSA.gh                                      # Grasshopper 3D milling script
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Toolpath exceeds working area | Scale geometry or reposition within 1800×2500mm bounds |
| Z-height error | Verify geometry Z-values are within 50mm limit |
| Tool not found | Check ARC magazine configuration and tool inventory |
| Poor surface finish | Reduce stepover, decrease pass depth, increase spindle speed |
| Excessive chatter | Reduce spindle speed, increase feed rate, check tool sharpness |
| Vacuum insufficient | Clean cutting mat, verify material is flat, increase vacuum strength |

## Safety Guidelines

- Always wear safety glasses when operating CNC equipment
- Ensure dust extraction is running before starting milling operations
- Never reach into working area while machine is in operation
- Verify emergency stop button is accessible
- Check tool condition before each job
- Secure material properly with vacuum system
- Do not exceed maximum material thickness (50mm)

## References

- **LARGERslicer Plugin**: See [Plugin Documentation](../../LARGERslicer/README.md) for CNC component details
- **HPGL Commands**: See [CNC Program Component](../../LARGERslicer/COMPONENTS_OVERVIEW.md) for command reference
- **Zünd Cut Center**: Refer to official Zünd software documentation for standard operations

---

**Machine Location**: FH Münster  
**Software**: Zünd Cut Center V3.5.6+ / MIND Software V6.1+  
**Last Updated**: December 2025
