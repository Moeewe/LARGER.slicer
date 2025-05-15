# Ginger V1.3 Beta LFAM 3D Printer Handbook & Cheat Sheet

> **Large-Format Additive Manufacturing (LFAM)** with the Ginger V1.3 Beta requires careful setup, design considerations, and process control.  
> This guide collects all the essential steps, rules, and tips—from geometry preparation to troubleshooting—to help you achieve reliable, high-quality prints.

---

## Table of Contents

1. [Introduction](#introduction)  
2. [Quick Start & Production Plate Template](#quick-start--production-plate-template)  
3. [Pre-Print Checks & Setup](#pre-print-checks--setup)  
4. [Print Process Overview](#print-process-overview)  
5. [Geometries & Design Guidelines](#geometries---design-guidelines)  
6. [Slicing Guidelines](#slicing-guidelines)  
   - [Software Overview](#software-overview)  
   - [Layer & Flow Rules](#layer---flow-rules)  
   - [General Slicing Tips](#general-slicing-tips)  
7. [Grasshopper-Based Custom Slicing](#grasshopper-based-custom-slicing)  
8. [Material Handling & Settings](#material-handling---settings)  
9. [Troubleshooting During Printing](#troubleshooting-during-printing)  
10. [Credits](#credits)  

---

## Introduction

Large-scale 3D printing introduces unique mechanical, thermal, and material challenges. The Ginger V1.3 Beta printer, with its dual-pellet mixer and wide-format build area, empowers complex, structural parts—but only when carefully configured. This handbook consolidates:

- Printer start-up & shutdown  
- Geometry preparation & design rules  
- Slicer choices & parameter guidelines  
- Grasshopper workflows for non-planar/custom slicing  
- Material recipes & drying requirements  
- Troubleshooting common issues  

Use it as both a step-by-step guide and a quick reference during your print runs.

---

## Quick Start & Production Plate Template

### Quick Start

1. **Power On** the printer.  
2. **Preheat** bed & extruder to your material’s recommended temperatures.  
3. **Purge** filament until the nozzle is clean.  
4. **Home** all axes to ensure proper positioning.  
5. **Load File** via SD or wireless.  
6. **Set Mixer Ratio** (default **93 % base / 7 % additive**).  
7. **Adjust Flow** for initial layers.  
8. **Start Print**.

### Production Plate Template

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

## Pre-Print Checks & Setup

Before every print, verify:

1. **Nozzle Size** matches your layer-height and flow settings.  
2. **Nozzle Cleanliness**—wipe or purge any residue.  
3. **Power Setup**—bed and printer on separate circuits to avoid brown-outs.  
4. **Bed Leveling & Z-Offset**—auto-level and save in EEPROM; adjust Z-offset for proper first-layer squish.  
5. **Bed Adhesion**—apply glue stick, tape, or appropriate adhesive.  

---

## Print Process Overview

1. **Design & Slice**  
   - Model in CAD → export STL/OBJ → import into slicer.  
2. **Transfer File**  
   - Use SD card or wireless connection.  
3. **Preheat**  
   - Bed & nozzle to target temperatures.  
4. **Purge Material**  
   - Extrude until flow is stable and clean.  
5. **Start Print**  
   - Initiate from printer UI.  
6. **Monitor Brim & First Layers**  
   - Ensure adhesion; adjust flow if needed.  
7. **Activate Cooling**  
   - After 4 solid layers, set fan to 60–100 % for overhangs.  
8. **Finish & Cool**  
   - Once complete, let bed temperature drop before removal.  

---

## Geometries & Design Guidelines

### Geometry Types

- **Polysurface**: Joined surfaces; can be open/closed.  
- **BREP**: Solid definition with faces & edges; must be watertight.  
- **Mesh**: Vertex/edge/face network; ensure manifold.  

### Characteristics

- **Open vs Closed**: Close gaps for slicer compatibility.  
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

- **Cura**: popular; convert STEP→STL/OBJ first.  
- **Orca**: delete default start/end G-code for full control.  
- **PrusaSlicer**: robust defaults; supports custom scripts.  
- **Grasshopper**: advanced, parametric slicing for non-planar parts.

### Layer & Flow Rules

- **Layer Width : Height ≥ 2 : 1** for good bonding.  
- **Max Layer Height ≤ 60 %** of nozzle diameter.  
- **Min Layer Width ≥ 150 %** of nozzle diameter.  
- **Double Beads**: use 0.8–0.9× layer width; set nozzle width to 1.2–1.4× diameter for strong walls.

### General Slicing Tips

- **Travel Moves**  
  - *Continuous Path*: reduces stringing & time.  
  - *Combine Multiple Parts*: fewer retracts.  
  - *Ooze Walls*: catch excess filament.

- **Support Structures**  
  - *Dynamic Z-Lift*: avoid collisions on complex prints.

- **Overhangs & Bridging**  
  - Standard limit 45°; steep up to 90° with stepover rule.  
  - Avoid bridges when possible; design to minimize.

- **Infill**  
  - Integrate patterns into model for strength & aesthetics.

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
- Select bottom supports: **infill**, **brim**, **skirt**.  

### 3. Curve & Contour Workflow

1. **Contour Slicing**: use Contour component.  
2. **Order & Sort**: align curves along Z-axis.  
3. **Divide & Join**: generate continuous spirals or seams.  
4. **Guide & Flip**: ensure consistent curve direction.  

### 4. G-Code Generation

- **Non-Planar**: feed rate varies with point-to-point distance.  
- **Planar**: constant feed rate for uniform layers.

---

## Material Handling & Settings

| Material          | Form     | Zone 1 (°C) | Zone 2 (°C) | Zone 3 (°C) | Additive      | Mixer Ratio |
|------------------:|----------|-----------:|-----------:|-----------:|--------------|------------:|
| Nateruworks PLA   | Pellets  |        220 |        210 |        205 | Flow Aid     | 93 / 7 %    |
| [Your Material]   | [Type]   |      [__] |      [__] |      [__] | [Additive]   | [__ / __ %] |

- **Dry Materials**: PETg, ABS, ASA, PC, PET (all require drying).  
- **Forms**: pellets, flakes, filament.  
- **Additives**: glycol for flakes; teflon tape for nozzle seal.

---

## Troubleshooting During Printing

### Mixer Settings

- Default **93 % base / 7 % additive** for smooth, consistent extrusion.

### Cooling Guidelines

- **Layers 1–4**: 0 % cooling to ensure bed adhesion.  
- **After Layer 4**: 60 % cooling standard, 100 % for heavy overhangs.

### Homing

- Clear any residual material from the nozzle before homing to protect the build plate.

### Bed Leveling

1. Auto-level and save Z-offset in EEPROM.  
2. Manually verify frame level; adjust each Z-motor if uneven.

### Common Problems & Remedies

| Problem                                   | Solutions                                                                                              |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------|
| **First-Layer Doesn’t Stick**             | - Re-level bed & adjust Z-offset<br>- Slow first-layer speed<br>- Increase first-layer flow rate     |
| **Warping**                               | - Add brim<br>- Secure with screws/clamps or tape<br>- 0 % cooling on initial layers<br>- Bed @ 60 °C |
| **Insufficient Extrusion**                | - Purge to clear blockage<br>- Adjust flow rate or print speed                                       |
| **Polymer Droplets on Surface**           | - Replace Teflon tape on nozzle<br>- Ensure masterbatch is thoroughly mixed<br>- Dry materials       |
| **Hissing/Steam at Nozzle**               | - Dry pellets/flakes completely before use                                                           |
| **Poor Surface Quality**                  | - Verify material dryness<br>- Fine-tune extrusion temperature                                        |
| **Nozzle Collision / Printer Crash**      | - Increase part clearance or redesign features<br>- Re-check bed leveling & Z-offset                  |

---

## Credits

Created by **Moritz Wesseler**, July 2024  
© Ginger V1.3 Beta Handbook & Cheat Sheet  
