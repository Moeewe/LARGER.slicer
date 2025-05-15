# Ginger V1.3 Beta 3D Printer Handbook

> **Large-Format Additive Manufacturing (LFAM)** demands special attention to geometry, slicing, machine setup, and materials.  
> This handbook aggregates process steps, design guidelines, slicing recipes, materials data, and troubleshooting tips for the Ginger V1.3 Beta printer.

---

## Table of Contents

1. [Quick Start: The Process](#quick-start-the-process)  
2. [Printer Setup & Pre-Print](#printer-setup--pre-print)  
   - [Starting a Print](#starting-a-print)  
   - [Important Pre-Print Checks](#important-pre-print-checks)  
   - [3D Printing Production Plate (Template)](#3d-printing-production-plate-template)  
3. [Design & Geometry](#design--geometry)  
4. [Slicing Recipes](#slicing-recipes)  
   - [Software Overview](#software-overview)  
   - [Layer-Size Rules](#layer-size-rules)  
   - [General Slicing Tips](#general-slicing-tips)  
   - [Grasshopper Workflow](#grasshopper-workflow)  
5. [Materials](#materials)  
6. [Troubleshooting During Printing](#troubleshooting-during-printing)  
7. [Credits](#credits)  

---

## Quick Start: The Process

1. **Design Geometry.**  
   Create your 3D model in CAD (Polysurface, Mesh, BREP, etc.).

2. **Slice Geometry.**  
   Import into your slicer (Cura, Orca, Grasshopper…) and generate G-code.

3. **Transfer to Printer.**  
   Copy the G-code to SD or wireless.

4. **Preheat Printer & Bed.**  
   Set nozzle and bed temperatures.

5. **Extrude & Purge.**  
   Clear any residue for a clean start.

6. **Start Print.**  
   Select file, confirm settings, and launch.

7. **Monitor & Adjust.**  
   Mixer ratio, flow, fans, cooling, etc.

8. **Cool Down & Remove.**  
   Let the part stabilize, then demold carefully.

---

## Printer Setup & Pre-Print

### Starting a Print

1. **Power On** the machine.  
2. **Preheat** bed & extruder.  
3. **Set Temperature** → Confirm.  
4. **Auto-Feeding** → **On**  
5. **Fan Control** → **Off** (initial layers)  
6. **Bed Prep** → Glue, tape, or adhesive spray  
7. **Purge** nozzle until clean  
8. **Home** all axes  
9. **Select File** and start  
10. **Mixer Ratio** e.g. `93/7%`  
11. **Flow** tweaks during brim phase  
12. **Cooling** → Enable after 4 layers (60–100%)  
13. **Bed Off** once adhesion phase ends  
14. **Enjoy** your print!

> **Tip:**  
> Keep a small brim or skirt to stabilize first layers.

### Important Pre-Print Checks

- **Nozzle Size** matches slicer settings.  
- **Nozzle Cleanliness** → Wipe & purge before homing.  
- **Power Setup** → Avoid sharing bed & printer on one outlet.  
- **Bed Leveling** → Auto-level + manual Z-offset check.

### 3D Printing Production Plate (Template)

| Date       | Operator   | File              |
|:----------:|:----------:|:-----------------:|
| yyyy-mm-dd | Your name  | YourPrint.gcode   |

| Mixer Ratio | Flow Rate | Slicer      |
|:-----------:|:---------:|:-----------:|
| 93/7%       | ____%     | [ ] Grasshopper  
|             |           | [ ] Cura  
|             |           | [ ] Prusa  
|             |           | [ ] Orca  

| Material | PLA | PETg | ABS | ASA | Other |
|:--------:|:---:|:----:|:---:|:---:|:-----:|
| [ ]      | [ ] | [ ]  | [ ] | [ ] | [ ]   |

| Nozzle  | Adapter | 2 mm | 3 mm | 5 mm | 8 mm |
|:-------:|:-------:|:----:|:----:|:----:|:----:|
| [ ]     | [ ]     | [ ]  | [ ]  | [ ]  | [ ]  |

| Zone | Material | Nozzle |
|:----:|:--------:|:------:|
|  1   |          |        |
|  2   |          |        |
|  3   |          |        |

---

## Design & Geometry

### Geometry Types

- **Polysurface:** joined surfaces (open/closed)  
- **Surface:** single continuous face (usually open)  
- **BREP:** solid boundary (can include edges & faces)  
- **Mesh:** vertices/edges/faces polyhedron

### Characteristics

- **Open vs. Closed**  
  - *Open:* gaps/hollows → must seal.  
  - *Closed:* watertight.

- **Surface Properties**  
  - Top Open / Closed / Smooth / Angled / Undercuts

### Design Rules

- **Continuous Toolpath** minimizes retractions.  
- **No Seams/Bridging** → better finish & strength.  
- **Brim/Skirt** → adhesion & stability.

### Recommended Methods

| Type           | Software     | Use Case                           | Highlights                    |
|:--------------:|:------------:|:----------------------------------:|:------------------------------:|
| Simple & Fast  | Orca         | Planar, quick, standard shapes     | Remove preset G-code blocks   |
| Custom & Complex | Grasshopper | Non-planar, bespoke geometries      | Adaptive slicing, custom ends |

---

## Slicing Recipes

### Software Overview

- **Cura** – convert STEP → STL/OBJ first.  
- **Orca** – clear start/end G-code for full control.  
- **Grasshopper** – ultimate custom slicing via scripts.

### Layer-Size Rules

- **Width:Height ≥ 2:1**  
- **Max Height ≤ 60%** of nozzle diameter  
- **Min Width ≥ 150%** of nozzle diameter

### General Tips

#### Travel Moves

- **Continuous Toolpath**  
- **Combine Multiple Parts** in one job  
- **Artificial Ooze Walls** reduce stringing

#### Support

- **Dynamic Z-Lift** during travels

#### Overhangs

- **≤ 45°** standard; **up to 90°** with special strategies  
- **Stepover Rule** for layer support

#### Bridging

- **Minimize** → avoid poor quality

#### Infill

- **Integrated** into model for strength

#### Corners

- **Round or Step** to reduce stress  
- Radius ≥ 0.5× nozzle diameter

#### Double Beads

- **Layer Width:** 0.8–0.9× nominal  
- **Height/Width:** 1.2–1.4× nozzle diameter

### Grasshopper Workflow

<details>
<summary>1. Check & Prepare Geometry</summary>

- Identify as Surface, Open/Closed Polysurface  
- Seal gaps for watertight print  
- Decide planar vs. non-planar → use adaptive heights  
- Select brim/skirt/infill strategy
</details>

<details>
<summary>2. Contour Slicing Steps</summary>

1. Slice with **Contour** component  
2. Order & align curves along Z  
3. Divide/join curves → ensure continuous spiral  
4. Flip/guide curves for consistent direction  
5. Generate G-code:  
   - **Non-planar:** variable feed per height  
   - **Planar:** constant feed
</details>

---

## Materials

### Material Settings

| Material           | Form     | Zone 1 | Zone 2 | Zone 3 | Additive    | Mixer  |
|:------------------:|:--------:|:-----: |:-----: |:-----: |:-----------:|:------:|
| Nateruworks PLA    | Pellets  | 220 °C | 210 °C | 205 °C | Gleitmittel | 93/7%  |
| _Your custom row_  |          |        |        |        |             |        |

### Dry Materials

> Except PLA: PETG, ABS, ASA, PC, PET

### Specifications

- Pellets, Flakes, Filament  
- Dry/Not-dry status  
- Mixer & additive recipes

<details>
<summary>Notes & Recipes</summary>

– List custom material blends here  
– Glycol % for flakes  
– …
</details>

---

## Troubleshooting During Printing

### Mixer Settings

> **Recommended:** 93/7% (per experience)

### Cooling Guidelines

- **Layers 1–4:** 0% fan  
- **After:** 60% (up to 100% for overhangs)

### Homing

- Clear nozzle first → avoid build-plate damage

### Bed Leveling

- Auto-level → save Z-offset in EEPROM  
- Manually check each corner motor if uneven

### Common Problems

<details>
<summary>First Layer Doesn’t Stick</summary>

- **Bed Leveling:** re-run or manual adjust  
- **Z-Offset:** verify & fine-tune  
- **Speed:** slow first layer  
- **Flow:** increase for layer 1  
- **Adhesive:** spray or tape
</details>

<details>
<summary>Warping</summary>

- **Brim:** increase surface area  
- **Clamps/Screws:** secure part  
- **Fan:** 0% for first layers  
- **Bed Temp:** ~60 °C
</details>

<details>
<summary>Insufficient Extrusion</summary>

- **Purge:** clear blockage  
- **Flow/Speed:** adjust extrusion rate
</details>

<details>
<summary>Polymer Droplets / Steam</summary>

- **Nozzle Seal:** fresh Teflon tape  
- **Mixing:** fully disperse masterbatch  
- **Drying:** ensure material dryness
</details>

<details>
<summary>Poor Print Quality</summary>

- **Material Dryness**  
- **Temperature Tuning** up/down
</details>

<details>
<summary>Extruder Crashes</summary>

- **Clearance:** increase part offset  
- **Redesign:** raise or enlarge model
</details>

<details>
<summary>Notes</summary>

– …  
</details>

---

## Credits

> **Cheat Sheet & Handbook** for Ginger V1.3 Beta  
> © Moritz Wesseler, July 2024  
> Maintained by: _Your Name / Team_
