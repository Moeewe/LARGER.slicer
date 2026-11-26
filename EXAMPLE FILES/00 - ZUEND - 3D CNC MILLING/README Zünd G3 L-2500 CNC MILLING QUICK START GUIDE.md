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
2. **Grasshopper Usage**: the LARGER.slicer components on the **LARGER > CNC** tab (`CNC - Plate`, `CNC - Cutter`, `CNC - Rough and Finish`, or `CNC - Quick Start`) are made for **3D milling of height models** only. The example is `Zünd Rough and Finish (height model).gh` in this folder
3. **Material Limitations**: Maximum material thickness: 50mm working height, 60mm opening height
4. **Safety**: Always follow Zünd safety protocols and ensure proper dust extraction is active

## Prerequisites

- Rhino 8 or 9 with Grasshopper (Grasshopper 2 in Rhino 9 has the same CNC components)
- LARGER.slicer 2.0 or newer
- 3D geometry prepared in Rhino (mesh, surface, Brep or SubD), in millimetres

### The old workflow (CNC - Program, archived)

Up to 1.3 a single component, `CNC - Program` with `CNC - Cutter Selector`, made one zigzag (boustrophedon) pass and a PLT file. Its definition is now `../99 - ARCHIVE/Zünd HPGL PLT MSA (CNC - Program, old).gh`; `ZündHGPL PLT MSA.3dm` stays in this folder. From 2.0.5 both components are no longer on the toolbar (old files still open them): use `CNC - Rough and Finish` and `CNC - Cutter`, which rough in levels, finish with a ball, cut the part out and check the result before a file is written.

## Quick Start: mill a topography in three steps

Place **CNC - Quick Start** (LARGER > CNC) on the canvas, or open `Zünd Rough and Finish (height model).gh`: a topography
is roughed out of a plate, finished with a ball and cut out all round at the end.

| Step | Component | What you do |
|---|---|---|
| 1 | **CNC - Relief** or *Dein Modell* | choose one of 12 reliefs (site model, facade, scales, vaults ...), or disconnect the wire and right click *Dein Modell* > Set one Geometry: your mesh, surface or Brep, in millimetres, anywhere |
| 2 | **CNC - Plate** | measure the real plate and type in Length, Width, Thickness; choose Material. The model is put into the middle, its highest point half a millimetre below the top |
| 3 | **CNC - Cutter** (2x) | one thick flat cutter (R206-A), one ball. **Magazine** = the place in which the cutter really sits. A cutter that is not in the list: from 2.0.5 **Cutter** 6 (own flat) or 7 (own ball) with the inputs Diameter, Cutting Length, Max Depth and Spindle Speed (the old CNC - Own Cutter still opens in old files). **Module**: 11, 21 or 31, the module the router sits in (FH Münster: 31) |
| 4 | **CNC - Rough and Finish** | read the first line of the report: READY or STOP. STOP says what to change, and nothing is written |
| 5 | Rhino | orange = roughing, blue = finishing, red = cutting out, grey = ways with the cutter lifted, and the finished part |
| 6 | *Save PLT* | the file lands on the Desktop. Load it in Zünd Cut Center |

### Mill the landscape in foam first

Before the first own part, mill the example *Topography* of CNC - Quick Start in foam. Finishing moves X, Y and Z at the
same time (three axes simultaneously), so this one run shows whether the machine counts Z as the plug-in does: the landscape
stands as in the Rhino model (not mirrored, not upside down), its deepest place is as deep below the top of the plate as the
report says ("... mm deep at most"), and the cut around it ends at the table. If not, stop.

Z in the file: **0 = table, -thickness = top of the plate**, everything is milled in between. This needs a machine that is
initialised to the underlay (ITI), not by hand to the top of the plate.

What happens on the machine:

1. **Rough**: the thick cutter takes the bulk away in levels, in rings from the middle outwards, always in the same direction (climb). It sinks in along a ring, 3 degrees steep.
2. **Finish**: the machine changes the cutter (`XX351`), the ball goes round over the surface from the edge inwards. *Stepover* of the ball decides how smooth it gets (0.6 mm = ridges of 0.015 mm with a 6 mm ball). Rows instead of rings: *Finishing*.
3. **Cut out**: the thick cutter again, round and round, deeper every time, down to the table. *Hold* says what keeps the part in place: tabs (8 x 2 mm, 4 of them), a thin skin of 0.6 mm, or it stays in the plate (also with a sloped border). Cut tabs and skin with a knife. At least 10 mm of the plate should stay round the cut.

Good to know

- The R206-A cuts through 22 mm at most. For thicker plates choose *Stays in the plate* or use a longer cutter.
- *Step Down* automatic: foam 1 x, PU board 0.7 x, MDF and plywood 0.5 x, hardwood 0.33 x the diameter. Start values: try them on a scrap.
- Without the magazine set *Magazine* to "Not set": there is one file per cutter, change it by hand in between. The zero point must stay.
- Only the upper side of the model counts; undercuts cannot be milled.

⚠️ The change of the cutter (`XX351`) has not been run with this plug-in yet. First run: foam, nothing on the table that
must not be hit, hand on the emergency stop.

## File Structure

```
00 - ZUEND - 3D CNC MILLING/
├── README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md  # This guide
├── Zünd Rough and Finish (height model).gh                  # roughing, finishing, cutting out (CNC - Quick Start)
├── Schalungen Skript.gh                                     # formwork script, native Grasshopper
└── ZündHGPL PLT MSA.3dm                                     # Rhino file of the old CNC - Program definition

99 - ARCHIVE/
└── Zünd HPGL PLT MSA (CNC - Program, old).gh                # the old one-pass zigzag definition (CNC - Program)
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Toolpath exceeds working area | Scale geometry or reposition within 1800×2500mm bounds |
| Z-height error | Verify geometry Z-values are within 50mm limit |
| Tool not found | Check ARC magazine configuration and tool inventory |
| Poor surface finish | Reduce stepover, reduce the feed, increase spindle speed |
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

- **LARGERslicer Plugin**: See the [main README](../../README.md) for the CNC components
- **Website (German)**: [Fräsen mit der Zünd](https://parametricsolutions.de/larger-slicer/zuend/)
- **Zünd Cut Center**: Refer to official Zünd software documentation for standard operations

---

**Machine Location**: FH Münster  
**Software**: Zünd Cut Center V3.5.6+ / MIND Software V6.1+  
**Last Updated**: November 2026
