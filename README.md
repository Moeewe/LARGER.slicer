# LARGE FORMAT 3D PRINTING ALGORITHM
  
This Grasshopper definition—**LARGE FORMAT 3D PRINTING ALGORITHM**—is a two-stage slicer for generating large-format toolpaths and G-code directly in Rhino/Grasshopper. It supports skirts, brims (with flip option), adaptive layer widths, and live previews.

**BY MORITZ WESSELER - FH MÜNSTER 2025**  
*Originally inspired by Ginger.Additive; now fully reimplemented.* 

## TABLE OF CONTENTS

1. [PRESENTATION](#presentation)
2. [INSTALLATION (WINDOWS)](#installation-windows)

   1. [Requirements](#requirements)
   2. [Installation Steps](#installation-steps)
3. [INSTALLATION (MACOS)](#installation-macos)
4. [SETUP GRASSHOPPER COMPONENTS](#setup-grasshopper-components)
5. [USAGE](#usage)

   1. [Part 1: 3D Print Path Maker](#part-1-3d-print-path-maker)
   2. [Part 2: G-Code Maker](#part-2-g-code-maker)
6. [FILES & STRUCTURE](#files--structure)
7. [TROUBLESHOOTING](#troubleshooting)
8. [LICENSE](#license)

---

## PRESENTATION

This Grasshopper definition—**LARGE FORMAT 3D PRINTING ALGORITHM**—is a two-stage slicer for generating large-format toolpaths and G-code directly in Rhino/Grasshopper. It supports skirts, brims (with flip option), adaptive layer widths, and live previews.

Originally inspired by Ginger.Additive; current version is a complete reimplementation.

---

## INSTALLATION (WINDOWS)

### Requirements

* Windows 10 or newer
* Rhino 8 (or newer) with Grasshopper and Python 3
<<<<<<< HEAD
* **Different GH Plugins** (Load on startup automatically)

* **Pufferfish**
* **Clipper Components**
* **Sasquatsch**
* **Lunchbox**
* **Heteroptera**
* **Wombat**

=======
* **LunchBox** (fast R-Tree searches)
* **Pufferfish** (utility components)
>>>>>>> af3c9d32a6b4e7449985a6c60d382ba52f77496c

### Installation Steps

1. Download or clone the LARGERslicer.gh file.
2. Open Rhino and Grasshopper
3. Drag and Drop the .gh file onto the Grasshopper canvas.

---

## INSTALLATION (MACOS)

### Requirements

All plugins should load automatically while opening the script first time. Feel free to optimize, so that no plugins are required.

* macOS 11 Big Sur or newer
<<<<<<< HEAD
* Rhino 8 for Mac with Python 3 Grasshopper

=======
* Rhino 8 for Mac with Grasshopper and Python 3
* **LunchBox**
>>>>>>> af3c9d32a6b4e7449985a6c60d382ba52f77496c
* **Pufferfish**
* **Clipper Components**
* **Sasquatsch**
* **Lunchbox**
* **Heteroptera**
* **Wombat**

### Installation Steps

1. Download or clone the LARGERslicer.gh file.
2. Open Rhino and Grasshopper
3. Drag and Drop the .gh file onto the Grasshopper canvas.

---

## SETUP GRASSHOPPER COMPONENTS

0. Launch Rhino and create a file in mm measurement.
1. Launch Grasshopper in Rhino.
2. **File → Open** → select `LARGERslicer.gh`.
3. Ensure Plugins are loaded (check in the toolbar).
4. Unlock custom clusters if ou need: right-click any password-protected component and enter **Supersizedprinting***.
5. Verify clusters appear in **Input**, **Path Maker**, and **G-Code Maker** stages.

---

## USAGE

### Part 1: 3D Print Path Maker

1. **Surface to Slice:** Right-click **Input Geometry** → **Set One Surface** → pick your surface.
2. **Enable Skirt** & **Enable Brim:** Toggle with **True/False** buttons; use **Reverse Brim Orientation** if needed.
3. **Corner Fillet Radius (mm):** Round off corners.
4. **Min. Extrusion Width (mm)** & **Max. Extrusion Width (mm):** Standard and beta adaptive widths.
5. **Layer Height (mm)** & **Initial Layer Height (mm):** Control Z-step sizes.
6. **Point Spacing (mm):** Sets the point interval for slicing (default 2–5 mm).

The component generates **Toolpath Curves** along the midline of each layer.

### Part 2: G-Code Maker

1. **Output Folder:** Right-click **Save to** → **Set One File Path** → choose folder.
2. **Job Name:** Enter your desired filename in the text panel.
3. **Export G-Code:** Click to generate the `.gcode` file; the filename auto-includes estimated material weight, estimated print time, date & timestamp, plus your **Job Name**.
4. **Print Speed (mm/min):** Default 3500 (\~60 mm/s).
5. **Flow (%):** Extrusion multiplier.
6. **Select Printer Profile:** Choose **Ginger** or **Weber** presets.
7. **Previews:**

   * **Printer Bed Preview:** Visualizes the printer and build area.
   * **Toolpath Progress Preview:** Animates the **Toolpath Curves**; use **Preview Slider (0→1)** to scrub through the build sequence.

---

## FILES & STRUCTURE

```text
├── LARGERslicer.gh             # Main Grasshopper definition
├── LICENSE              # MIT License
└── README.md            # This documentation
└── PRINTER            # Contains printer and robot specific readmes and files and .gh

```

---

## TROUBLESHOOTING

| Problem                                | Cause                          | Solution                                               |
| -------------------------------------- | ------------------------------ | ------------------------------------------------------ |
| Missing LunchBox/Pufferfish components | Plugins not installed          | Install via Rhino’s PackageManager                     |
| Surface not accepted                   | Wrong geometry type            | Use **Set One Surface** on a valid Brep surface        |
| G-Code file not appearing              | Write command not triggered    | Click **Write G-Code** and confirm save path           |
| Preview slider has no effect           | Path Preview not connected     | Check that **Path Preview** input is wired to polyline |

---

## LICENSE

This project is released under the MIT License. See `LICENSE` for details.
