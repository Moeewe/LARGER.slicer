# LARGE FORMAT 3D PRINTING ALGORITHM

This repository contains a **complete ecosystem** for large-format additive manufacturing, centered around the **LARGER Slicer**—a two-stage Grasshopper definition for generating large-format toolpaths and G-code directly in Rhino/Grasshopper.

Beyond path generation, this repository includes **machine control scripts** for various printing platforms and **comprehensive handbooks** for setup, operation, and troubleshooting.

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

This repository provides a **complete large-format additive manufacturing ecosystem** consisting of three main components:

### 1. **LARGER Slicer** (Primary Focus)
A two-stage Grasshopper algorithm for generating large-format toolpaths and G-code. Features include skirts, brims (with flip option), adaptive layer widths, and live previews.

### 2. **Machine Control Systems**
- **Ginger One Printer**: Large-format pellet extruder with comprehensive material database
- **UR5 Robot Control**: Complete Grasshopper workflow for Universal Robots UR5 systems
- **Weber DXR Robot Control**: Specialized control for Weber DXR robotic platforms

### 3. **Documentation & Handbooks**
Complete setup, operation, and troubleshooting guides for all supported platforms.

Originally inspired by Ginger.Additive; current version is a complete reimplementation with expanded machine support.

---

## INSTALLATION (WINDOWS)

### Requirements

* Windows 11 or newer
* Rhino 8 (or newer) with Grasshopper and Python 3
* **Required Grasshopper Plugins** (Load on startup automatically)
  * **Pufferfish**
  * **Clipper Components**
  * **Sasquatch**
  * **LunchBox**
  * **Heteroptera**
  * **Wombat**

### Installation Steps

1. Download or clone the LARGERslicer.gh file.
2. Open Rhino and Grasshopper
3. Drag and Drop the .gh file onto the Grasshopper canvas.

---

## INSTALLATION (MACOS)

### Requirements

* macOS 11 Big Sur or newer
* Rhino 8 for Mac with Grasshopper and Python 3
* **Required Grasshopper Plugins** (should load automatically on first open)
  * **Pufferfish**
  * **Clipper Components**
  * **Sasquatch**
  * **LunchBox**
  * **Heteroptera**
  * **Wombat**

### Installation Steps

1. Download or clone the LARGERslicer.gh file.
2. Open Rhino and Grasshopper
3. Drag and Drop the .gh file onto the Grasshopper canvas.

> **Note:** All plugins should load automatically when opening the script for the first time. Feel free to optimize so that no plugins are required.

---

## SETUP GRASSHOPPER COMPONENTS

1. Launch Rhino and create a file in mm measurement.
2. Launch Grasshopper in Rhino.
3. **File → Open** → select `LARGERslicer.gh`.
4. Ensure Plugins are loaded (check in the toolbar).
5. Unlock custom clusters if needed: right-click any password-protected component and enter **Supersizedprinting**.
6. Verify clusters appear in **Input**, **Path Maker**, and **G-Code Maker** stages.

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
4. **Print Speed (mm/min):** Default 3500 (~60 mm/s).
5. **Flow (%):** Extrusion multiplier.
6. **Select Printer Profile:** Choose **Ginger** or **Weber** presets.
7. **Previews:**
   * **Printer Bed Preview:** Visualizes the printer and build area.
   * **Toolpath Progress Preview:** Animates the **Toolpath Curves**; use **Preview Slider (0→1)** to scrub through the build sequence.

---

## FILES & STRUCTURE

```text
├── LARGERslicer.gh                      # Main LARGER Slicer Grasshopper definition
├── LICENSE                              # MIT License
├── README.md                            # This documentation
├── _ LARGERslicer WIP/                  # Development versions and specialized scripts
└── _ PRINTER/                           # Machine-specific files and documentation
    ├── Ginger One Printer/             # Ginger V1.3 Beta LFAM 3D Printer
    ├── Universal Robots UR5/           # UR5 Robot System
    └── Weber DXR Robot/                # Weber DXR Robot System
```

### Key Components:

1. **Primary Slicer**: `LARGERslicer.gh` - The main toolpath generation algorithm
2. **Specialized Variants**: Development versions for specific use cases and machine configurations
3. **Machine Documentation**: Complete setup, operation, and troubleshooting guides for all platforms
4. **Python Utilities**: Supporting modules for file operations and system integration

---

## QUICK ACCESS TO MACHINE GUIDES

### 📖 **Machine-Specific Documentation**

| Platform | Quick Start Guide | Control Script | Status |
|----------|-------------------|----------------|--------|
| **Ginger One Printer** | [📋 Ginger Quick Start Guide](./_PRINTER/Ginger%20One%20Printer/README%20GINGER%2000%20QUICK%20START%20GUIDE.md) | Use `LARGERslicer.gh` | ✅ Production Ready |
| **Universal Robots UR5** | [📋 UR5 Quick Start Guide](./_PRINTER/Universal%20Robots%20UR5/README%20UR5%2000%20QUICK%20START%20GUIDE.md) | [🤖 UR5Slicer.gh](./_PRINTER/Universal%20Robots%20UR5/UR5slicer%20WIP%2045°/UR5Slicer.gh) | ✅ Production Ready |
| **Weber DXR Robot** | [📋 Weber DXR Quick Start Guide](./_PRINTER/Weber%20DXR%20Robot/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE.md) | Use `LARGERslicer.gh` | 🚧 Work in Progress |

### 🎯 **Recommended Workflow**

1. **Start here**: Use `LARGERslicer.gh` for toolpath generation
2. **Machine Setup**: Follow the appropriate Quick Start Guide above
3. **Advanced Control**: For UR5, use the dedicated `UR5Slicer.gh` for robotic workflows

> **Note:** Weber DXR multi-axial control is under development. Current workflow uses `LARGERslicer.gh` for path preparation with simulation scripts for previews.

---

## TROUBLESHOOTING

| Problem                                | Cause                          | Solution                                               |
| -------------------------------------- | ------------------------------ | ------------------------------------------------------ |
| Missing LunchBox/Pufferfish components | Plugins not installed          | Install via Rhino's PackageManager                     |
| Surface not accepted                   | Wrong geometry type            | Use **Set One Surface** on a valid Brep surface        |
| G-Code file not appearing              | Write command not triggered    | Click **Write G-Code** and confirm save path           |
| Preview slider has no effect           | Path Preview not connected     | Check that **Path Preview** input is wired to polyline |

---

## LICENSE

This project is released under the MIT License. See `LICENSE` for details.
