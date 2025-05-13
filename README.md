---

# LARGE FORMAT 3D PRINTING ALGORITHM

**Version 1.0.0**
**BY MORITZ WESSELER – FH MÜNSTER 2025**
*Originally inspired by Ginger.Additive; now fully reimplemented.*

---

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
7. [FUTURE PROJECTS](#future-projects)
8. [TROUBLESHOOTING](#troubleshooting)
9. [LICENSE](#license)

---

## PRESENTATION

This Grasshopper definition—**LARGE FORMAT 3D PRINTING ALGORITHM**—is a two-stage slicer for generating large-format toolpaths and G-code directly in Rhino/Grasshopper. It supports skirts, brims (with flip option), adaptive layer widths, and live previews.

Originally inspired by Ginger.Additive; current version is a complete reimplementation.

---

## INSTALLATION (WINDOWS)

### Requirements

* Windows 10 or newer
* Rhino 7 (or newer) with Grasshopper
* **LunchBox** (fast R-Tree searches)
* **Pufferfish** (utility components)

### Installation Steps

1. Open Rhino → **PackageManager**.
2. Search for and install **LunchBox**.
3. Search for and install **Pufferfish**.
4. Download or clone this repository (as `.gh`).

---

## INSTALLATION (MACOS)

### Requirements

* macOS 11 Big Sur or newer
* Rhino 7 for Mac with Grasshopper
* **LunchBox**
* **Pufferfish**

### Installation Steps

1. Open Rhino for Mac → **PackageManager**.
2. Install **LunchBox** and **Pufferfish**.
3. Clone or download this repo (as `.gh`).

---

## SETUP GRASSHOPPER COMPONENTS

1. Launch Grasshopper in Rhino.
2. **File → Open** → select `LARGE.gh`.
3. Ensure LunchBox and Pufferfish are loaded (check in the toolbar).
4. Unlock custom clusters: right-click any password-protected component and enter **Supersizedprinting**.
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
├── LARGE.gh             # Main Grasshopper definition
├── LICENSE              # MIT License
└── README.md            # This documentation
```

---

## FUTURE PROJECTS

* **Ginger Printer Readme** – dedicated documentation for Ginger machine setup.
* **Weber Printer Readme** – dedicated documentation for Weber machine.
* **Non-Planar Module** – advanced slicing for curved surfaces.
* **Direct Robot Control** – integrate with KUKA/UR for robotic deposition.

---

## TROUBLESHOOTING

| Problem                                | Cause                          | Solution                                               |
| -------------------------------------- | ------------------------------ | ------------------------------------------------------ |
| Missing LunchBox/Pufferfish components | Plugins not installed          | Install via Rhino’s PackageManager                     |
| Components locked by password          | Custom clusters require unlock | Right-click component → enter **Supersizedprinting**   |
| Surface not accepted                   | Wrong geometry type            | Use **Set One Surface** on a valid Brep surface        |
| G-Code file not appearing              | Write command not triggered    | Click **Write G-Code** and confirm save path           |
| Preview slider has no effect           | Path Preview not connected     | Check that **Path Preview** input is wired to polyline |

---

## LICENSE

This project is released under the MIT License. See `LICENSE` for details.
