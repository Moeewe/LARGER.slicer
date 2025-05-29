# Ginger V1.3 Beta LFAM 3D Printer Handbook & Cheat Sheet

**Large-Format Additive Manufacturing (LFAM)** with the Ginger V1.3 Beta requires careful setup, design considerations, and process control. This guide collects all the essential steps, rules, and tips—from geometry preparation to troubleshooting—to help you achieve reliable, high-quality prints.

---

## Table of Contents

1. [Introduction](#introduction)  
2. [Quick Start Guide](#quick-start-guide)  
3. [Prerequisites & Setup](#prerequisites--setup)  
   - [Power & Safety](#power--safety)  
   - [Pre-Print Checks](#pre-print-checks)  
   - [Heating System](#heating-system)  
4. [Print Process Overview](#print-process-overview)  
5. [Geometries & Design Guidelines](#geometries--design-guidelines)  
6. [Slicing Guidelines](#slicing-guidelines)  
   - [Software Overview](#software-overview)  
   - [Layer & Flow Rules](#layer--flow-rules)  
   - [General Slicing Tips](#general-slicing-tips)  
7. [Grasshopper-Based Custom Slicing](#grasshopper-based-custom-slicing)  
8. [Material Handling & Settings](#material-handling--settings)  
9. [Troubleshooting During Printing](#troubleshooting-during-printing)  
10. [Start & End G-Code](#start--end-g-code)  
11. [Credits](#credits)  

---

## Introduction

Large-scale 3D printing introduces unique mechanical, thermal, and material challenges. The Ginger V1.3 Beta printer, with its pellet extruder and wide-format build area, empowers complex, structural parts—but only when carefully configured. This handbook consolidates:

- Printer start-up & shutdown  
- Geometry preparation & design rules  
- Slicer choices & parameter guidelines  
- Grasshopper workflows for non-planar/custom slicing  
- Material recipes & drying requirements  
- Troubleshooting common issues  

Use it as both a step-by-step guide and a quick reference during your print runs.

---

## Quick Start Guide

1. **Power On** the printer via the power box with multiple sockets.
2. **Remove** all red emergency safety knobs.
3. **Preheat** bed & extruder to your material's recommended temperatures.
4. **Purge** material until the nozzle is clean.
5. **Home** all axes to ensure proper positioning.
6. **Load File** via micro-SD.
7. **Set Mixer Ratio** (default **93 % base / 7 % additive**).
8. **Adjust Flow** for initial layers.
9. **Start Print**.

---

## Prerequisites & Setup

### Power & Safety
- **Heatbed Power**: The heatbed requires a separate power cable and **must be connected to a different circuit**. It consumes up to **4 x 800 W**, so never plug it into the same circuit as the main machine.
- **Main Power-On**: The machine is turned on via a **power box with multiple sockets**. Look for a small switch (usually on top of the box).
- **Emergency Buttons**: Ensure **all red emergency safety knobs** are removed before starting the system.

### Pre-Print Checks
1. **Nozzle Size** matches your layer-height and flow settings.  
2. **Nozzle Cleanliness** — wipe or purge any residue **before** homing.  
3. **Power Setup** — bed and printer on separate circuits to avoid brown-outs.  
4. **Bed Leveling & Z-Offset** — auto-level and save in EEPROM; adjust Z-offset for proper first-layer squish.  
5. **Bed Adhesion** — apply glue stick, tape, or appropriate adhesive.

### Heating System
- **Extruder Heating**: On the **right-hand display**, set the target temperature using the rotary knob or touchscreen. 
- Activate the heating system and wait until the **target temperature is reached** before moving forward.

### Production Plate Template

_Print and keep this sheet next to the printer to track your test prints._

| Field         | Entry                              |
|--------------:|------------------------------------|
| **Date**      | __________                         |
| **Operator**  | __________                         |
| **File Name** | __________                         |
| **Mixer**     | __________                         |
| **Flow (%)**  | __________                         |
| **Slicer**    | ◻ Grasshopper ◻ Cura ◻ Prusa ◻ Orca |
| **Material**  | ◻ PLA ◻ PETg ◻ ABS ◻ ASA ◻ Other:__ |
| **Nozzle**    | ◻ Adapter ◻ 2 mm ◻ 3 mm ◻ 5 mm ◻ 8 mm |

| Zone | Material | Nozzle |
|:----:|:--------:|:------:|
|  1   |          |        |
|  2   |          |        |
|  3   |          |        |

---

## Print Process Overview

### Initial Setup
1. **Design & Slice**  
   - Model in CAD → export STL/OBJ → import into slicer.  
2. **Transfer File**  
   - Export your sliced G-code using **Larger Slicer**
   - Place the `.gcode` file on a **microSD card**
   - Insert it into the **rear-left slot of the controller box**

### Startup Sequence
1. **Home** the printer
2. Wait until **extruder temperature is reached**, then allow Z-homing to avoid material drag
3. **Clean leftover material** manually from the nozzle to avoid clogging or displacement during the start

### Print Monitoring
1. **Monitor Brim & First Layers**  
   - Ensure adhesion; adjust flow if needed
2. **Activate Cooling**  
   - After 4 solid layers, set fan to 60 – 100 % for overhangs
3. **Z-Offset & Flow Adjustments**
   - Can be changed during the print via the **Tune menu** on the printer
   - Ideally adjust these in the Grasshopper script before export

### Completion
1. **Wait for Cooling**
   - Once complete, let bed temperature drop before removal
   - This prevents damaging the surface or deforming the part

---

## Geometries & Design Guidelines

### Geometry Types

- **Polysurface**: joined surfaces; can be open or closed.  
- **BREP**: solid definition with faces & edges; must be watertight.  
- **Mesh**: vertex/edge/face network; ensure manifold.

### Characteristics

- **Open vs Closed**: close gaps for slicer compatibility.  
- **Surface Profiles**:  
  - **Top Open**: good for hollow or vented parts.  
  - **Top Closed**: airtight/sealed prints.  
  - **Angled & Undercuts**: require supports or custom slicing strategies.

### Design Rules

- **Continuous Toolpath**: avoid retractions and minimize seams.  
- **Include Brim/Skirt**: stabilizes base layers & improves adhesion.  
- **Avoid Excessive Bridges**: design features to minimize unsupported spans.

---

## Slicing Guidelines

### Software Overview

- **Cura**: convert STEP → STL/OBJ first.  
- **Orca**: delete default start/end G-code for full control.  
- **PrusaSlicer**: robust defaults; supports custom scripts.  
- **Grasshopper**: advanced, parametric slicing for non-planar parts.

### Layer & Flow Rules

- **Layer Width : Height ≥ 2 : 1** for good bonding.  
- **Max Layer Height ≤ 60 %** of nozzle diameter.  
- **Min Layer Width ≥ 150 %** of nozzle diameter.  
- **Double Beads**: use 0.8 – 0.9× layer width; set nozzle width to 1.2 – 1.4× diameter for strong walls.

### General Slicing Tips

- **Travel Moves**  
  - *Continuous Path*: reduces stringing & time.  
  - *Combine Multiple Parts*: fewer retracts.  
  - *Ooze Walls*: catch excess filament.

- **Support Structures**  
  - *Dynamic Z-Lift*: avoid collisions on complex prints.

- **Overhangs & Bridging**  
  - Standard limit 45 °; steep up to 90 ° with stepover rule.  
  - Avoid bridges when possible; design to minimize.

- **Infill**  
  - Integrate patterns directly into the model for strength & aesthetics.

- **Corners**  
  - Round or step-divide sharp corners.  
  - Corner radius ≥ 0.5× nozzle diameter.

---

## Grasshopper-Based Custom Slicing

### 1. Geometry Assessment

- Identify **Surface**, **Open/Closed Polysurface**.  
- Determine **Planar vs Non-Planar** surfaces → adaptive layer heights.

### 2. Model Preparation

- Seal open polysurfaces to create watertight solids.  
- Select bottom curves: **infill**, **brim**, **skirt**.

### 3. Curve & Contour Workflow

1. **Contour Slicing**: use the Contour component.  
2. **Order & Sort**: align curves along the Z-axis.  
3. **Divide & Join**: generate continuous spirals or seams.  
4. **Guide & Flip**: ensure consistent curve direction.

### 4. G-Code Generation

- **Non-Planar**: feed rate varies with point-to-point distance.  
- **Planar**: constant feed rate for uniform layers.

> **Optional:** Try the [LARGER.slicer](https://github.com/Moeewe/LARGER.slicer) Grasshopper plugin for advanced workflows.

---

## Material Handling & Settings

| Material          | Form     | Zone 1 (°C) | Zone 2 (°C) | Zone 3 (°C) | Additive      | Mixer Ratio |
|------------------:|----------|-----------:|-----------:|-----------:|--------------|------------:|
| Nateruworks PLA   | Pellets  |        220 |        210 |        205 | —            | 93 / 7 %    |
| QiTECH PETg       | Pellets  |        260 |        260 |        240 | —            | 93 / 7 %    |
| [Your Material]   | [Type]   |      [__] |      [__] |      [__] | [Additive]   | [__ / __ %] |

### Material Requirements
- **Dry Materials**: PETg, ABS, ASA, PC, PET (all require drying)
- **Material Slicer Settings**: All materials have custom factors to multiply the E-Value with
- **Forms**: pellets, flakes  
- **Additives**: glycol for flakes  
- **Nozzle Swap**: wrap threads with Teflon tape to seal after swapping

### Material Feed System
- The yellow hose leads from the extruder to the material box
- Inside the box, place the **gray suction tube at the bottom** to ensure reliable pellet intake

---

## Troubleshooting During Printing

### Mixer Settings

- Default **93 % / 7 %** for smooth, consistent extrusion.

### Cooling Guidelines

- **Layers 1–4**: 0 % cooling to ensure bed adhesion.  
- **After Layer 4**: 60 % cooling standard; 100 % for heavy overhangs.

### Homing

- Clear residual material from the nozzle **before** homing to protect the build plate.

### Bed Leveling

1. Auto-level and save Z-offset in EEPROM.  
2. Manually verify frame level; adjust each Z-motor if uneven.  
3. Ensure your start/end G-codes match your firmware's bed-level commands.

### Common Problems & Remedies

| Problem                          | Solutions                                                                                           |
|----------------------------------|----------------------------------------------------------------------------------------------------|
| **First-Layer Doesn't Stick**    | – Re-level bed & adjust Z-offset<br>– Slow first-layer speed<br>– Increase first-layer flow rate    |
| **Warping**                      | – Add brim<br>– Secure with screws/clamps or tape<br>– 0 % cooling on initial layers<br>– Bed @ 60 °C |
| **Insufficient Extrusion**       | – Purge to clear blockage<br>– Adjust flow rate or print speed                                     |
| **Polymer Droplets on Surface**  | – Replace Teflon tape on nozzle<br>– Thoroughly mix masterbatch<br>– Dry materials                 |
| **Hissing/Steam at Nozzle**      | – Dry pellets/flakes completely before use                                                        |
| **Poor Surface Quality**         | – Verify material dryness<br>– Fine-tune extrusion temperature                                      |
| **Nozzle Collision / Crash**     | – Increase part clearance or redesign features<br>– Re-check bed leveling & Z-offset               |

---

## Start & End G-Code

<details>
<summary><strong>Start G-Code</strong></summary>

```gcode
; START CODE
; Custom pins declaration
M42 I P49 T1 S0    ; pin PC4
M42 I P57 T1 S0    ; pin PF9

; Activate temperature-control
M42 P49 I T1 S0
M42 P57 I T1 S1
G4 P2000
M42 P57 I T1 S0
G4 P200

; Set units to mm and disable cold extrusion
G21
M302 P1

; Set mixer values
M163 S0 P1
M163 S1 P0.07
M164

; Raise Z before homing
G91
G1 F200 Z10
G90

; Home X/Y and bed-level
G28 X0 Y0
M420 S1
; G29 F150 L150 R300 B300    ; custom bed-level area

; Extruder ready tone
M300 P200
G4 P500
M300 P200
M0 Click if EXTR. READY

; Final home
G28
G90
G1 F200 Z2
G92 E0
M83
```
</details>


<details>
<summary><strong>End G-Code</strong></summary>

```gcode
; END CODE
; Deactivate temperature control
M42 P49 I T1 S1
G4 P200

; Raise Z-axis for part clearance
G91
G1 F100 Z30
G90

; Home X and disable motors
G28 X
M84

; Finishing tone
M300 P500
G4 P1000
M300 P500
G4 P1000
M300 P1000
```
</details>

---

## Credits

Ginger V1.3 Beta Handbook & Cheat Sheet  
© Created by **Moritz Wesseler**, July 2024  
