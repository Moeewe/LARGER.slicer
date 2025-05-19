# UR5 3D-Printing & Drawing Slicer

**File:** `UR5.slicer.gh`  
**Version:** Work-In-Progress  
**By:** Fabio Koczula & Torben Kurzhals – FH Münster 2025  
**Contributors:** Moritz Wesseler  

> **Robot Clay Printing and Movement Algorithm**  
> Based on the Robots plugin by Visose  

---

## Table of Contents

1. [Overview](#overview)  
2. [Network Setup](#network-setup)  
3. [Rhino & Grasshopper Setup](#rhino--grasshopper-setup)  
4. [Loading the Definition](#loading-the-definition)  
5. [Cluster Components](#cluster-components)  
   - [Geometry Pipeline & Simple Slicer](#geometry-pipeline--simple-slicer)  
   - [Worksurface Mapper](#worksurface-mapper)  
   - [Pen/Brush Tool (Robotic Drawing)](#penbrush-tool-robotic-drawing)  
   - [Robot Simulation & Code Export](#robot-simulation--code-export)  
   - [Exceptions & Known Bugs](#exceptions--known-bugs)  
6. [Generating & Uploading Robot Code](#generating--uploading-robot-code)  
7. [Running on the UR5](#running-on-the-ur5)  
8. [Troubleshooting](#troubleshooting)  
9. [Future Tutorials](#future-tutorials)  
10. [Credits](#credits)  

---

## Overview

This Grasshopper definition slices your 3D geometry or 2D curves into robot‐ready toolpaths for the UR5. It supports:

- **Clay extrusion**: generate 3D print layers  
- **2D drawing**: planar strokes for pens or brushes  
- **Live simulation** & direct URScript export  

---

## Network Setup

1. **Wired connection only**: connect your laptop’s Ethernet port to the UR5’s network adapter.  
2. **Static IP (laptop)**:  
   - IP: `192.168.1.66`  
   - Subnet mask: `255.255.255.0`  
3. **Robot IP** (default): `192.168.1.2`  
4. **Verify link**:  
   ```bash
   ping 192.168.1.2
   ```

---

## Rhino & Grasshopper Setup

1. **Open Rhino** → load your `.3dm` scene.  
2. In Rhino’s command line, type `Grasshopper` → press Enter.  
3. Install any missing plugins (e.g., Robots, MeshEdit) via **_Tools → PackageManager_**.

---

## Loading the Definition

- **Drag & drop** `UR5.slicer.gh` into the Grasshopper canvas.  
- Wait for all clusters to load—no red components should remain.  
- If prompted, unlock clusters with the provided password.

---

## Cluster Components

### Geometry Pipeline & Simple Slicer

- **Inputs**:  
  - Geometry  
  - First layer height, nth-layer height  
  - Point distance, zigzag toggle & thickness  
  - Number of bottom layers, bottom layer linewidth  
- **Outputs**:  
  - `planes`, `curves` for each slice  
  - `geometry`, `center`, bottom-layer curves & points  

### Worksurface Mapper

- **Inputs**:  
  - `planes`, `curves`, original geometry  
  - `animation %`, workspace `center`  
  - Distances 1 & 2, workspace `width`, `length`, `height`  
- **Outputs**:  
  - Transformed `planes` & `curves` positioned over your build surface  

### Pen/Brush Tool (Robotic Drawing)

- **Inputs**:  
  - Planar `curve`  
  - `Precision (mm)`, `Rotate Planar`, `Rotate 3D`  
- **Outputs**:  
  - Oriented `planes` ready for pen or brush attachment  

### Robot Simulation & Code Export

- **Inputs**:  
  - `planes`  
  - Toggles: `flip planes`, `rotate planes`, `ironing`  
  - Joint‐limits: shoulder, elbow, wrist  
  - Motion mode dropdown (e.g. Joint/Linear)  
  - `speed [mm/s]`, `animation %`  
  - Buttons: `Upload`, `Play`, `Pause`  
  - `Tool Selection` (e.g. Pen Holder, Clay Nozzle)  
- **Outputs**:  
  - `program` (URScript text)  
  - `Warnings`, `Errors` panels  

### Exceptions & Known Bugs

- **Exceptions** cluster (captures runtime errors)  
- **Bugs** (sticky note):  
  - Make nozzle length adjustable  
  - Implement plane rotation for pen holder  

---

## Generating & Uploading Robot Code

1. Connect the `program` output to a Grasshopper **Panel**.  
2. Click **Upload** → this streams the URScript to `192.168.1.2`.  
3. Check **Warnings** & **Errors** panels for issues.

---

## Running on the UR5

1. On the UR5 teach pendant, navigate to **Program →** your uploaded script.  
2. Press **Play** to start execution.  
3. Use **Pause** or **E-stop** if needed.

---

## Troubleshooting

- **No network link**: verify static IP and cable.  
- **Red components**: install missing GH plugins or unlock clusters.  
- **Upload fails**: ensure UR5 firmware matches your GH Robots plugin version.  

---

## Future Tutorials

- **Clay 3D-Print Workflow** (detailed layering & drying)  
- **Pen & Brush Drawing Workflow** (multi-color, pressure control)  

*(Coming soon)*

---

## Credits

**Authors:**  
— Fabio Koczula & Torben Kurzhals, FH Münster 2025  
**Further Contributors:**  
— Moritz Wesseler  

© All rights reserved.
