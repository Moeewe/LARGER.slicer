# LARGER SLICER

A complete ecosystem for large-format additive manufacturing, featuring the LARGER Slicer - a two-stage Grasshopper algorithm for generating large-format toolpaths and G-code directly in Rhino/Grasshopper.

**BY MORITZ WESSELER - FH MÜNSTER 2025**  
*Originally inspired by Ginger.Additive; now fully reimplemented with expanded machine support.*

Further Contributors: Fabio Koczula, Claudio Schröder
 

## Table of Contents

- [Latest Updates](#latest-updates)
- [Supported Machines](#supported-machines)
- [Quick Start](#quick-start)
- [Installation](#installation)
- [Setup](#setup)
- [Usage Guide](#usage-guide)
- [Project Structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Latest Updates

### 1.1.4 (2026-07-28)

- Added **DXR File Health Check** for operator-facing GO/NO-GO file validation before machine loading.
- Added dedicated icon and GUID for the DXR health-check component.
- Added printable cabinet sheets for safer shop-floor operation:
   - [Cabinet GO-NO-GO Sheet (DE)](LARGERslicer/documentations/SCHALTSCHRANK_ZETTEL_DXR_GOCHECK_DE.md)
   - [Cabinet NO-GO Immediate Help (DE)](LARGERslicer/documentations/SCHALTSCHRANK_ZETTEL_NOGO_HILFE_DE.md)
- Recommended QR targets for on-machine access:
   - https://github.com/Moeewe/LARGER.slicer
   - https://github.com/Moeewe/LARGER.slicer/issues

## Supported Machines

The LARGER Slicer ecosystem supports multiple large-format additive manufacturing platforms:

| Platform | Status | Control Script | Documentation |
|----------|--------|----------------|---------------|
| **Ginger One Printer** | Production Ready | `LARGERslicer Weber Robot and Ginger.gh` | [Ginger Quick Start Guide](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md) |
| **Universal Robots UR5** | Production Ready | `UR5slicer.gh` | [UR5 Quick Start Guide](EXAMPLE%20FILES/00%20-%20UNIVERSAL%20ROBOTS%20-%20UR5/README%20UR5%2000%20QUICK%20START%20GUIDE.md) |
| **Weber DXR25 Robot** | Multi-Axial Ready | `LARGERslicer Weber Robot and Ginger.gh` | [Weber DXR25 German Guide](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20GERMAN.md) \| [English Guide](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20ENGLISH.md) |

Folder naming note: In this README, `00 - WEBER : GINGER` is labeled as **00 - WEBER : GINGER [Robotic] 3D Printing** for clarity.

### Recommended Workflow

Use the merged starter definition `LARGERslicer Weber Robot and Ginger.gh` and choose one of these three workflows inside the file:

1. **Easy Slicer**: Slices surfaces and creates the toolpath automatically.
2. **Path Creator (Multiaxial)**: Converts a prepared toolpath to multiaxial robot paths.
3. **Custom Paths**: Uses only conversion/export components to generate DXR-specific robot code from your own paths.

Legacy note: `LARGERslicer Multiaxial Weber Robot.gh` is archived and not required for the current workflow.

For UR5 workflows, use the dedicated `UR5slicer.gh` script.

## Quick Start

### Prerequisites

- **Operating System**: Windows 11+ or macOS 11 Big Sur+
- **Software**: Rhino 8+ with Grasshopper and Python 3
- **Required Grasshopper Plugins**:
  - LARGERslicer (install from `LARGERslicer/Plugin Installation Files/` or via PackageManager)
  - Pufferfish (included in Rhino 8+)

### Get Started in 3 Steps

1. **Download**: Get [LARGERslicer Weber Robot and Ginger.gh](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/LARGERslicer%20Weber%20Robot%20and%20Ginger.gh) from this repository.
2. **Open**: Launch Rhino (set to mm), open Grasshopper, drag and drop the .gh file
3. **Start**: Follow the [Usage Guide](#usage-guide) below

## Installation

### System Requirements

- **Windows**: Windows 11 or newer
- **macOS**: macOS 11 Big Sur or newer  
- **Rhino**: Version 8 or newer with Grasshopper and Python 3

### Installation Steps

1. **Install LARGERslicer Plugin** (if using DXR/CNC components):
   
   **Rhino Package Manager (Required)**
   - In Rhino, run the command `PackageManager` (or `PaketManager` in German)
   - Search for "LARGERslicer"
   - Click "Install" (or "Anwenden" in German)
   - Restart Rhino/Grasshopper
   
   ~~**Option 2: Manual Installation**~~ *(Deprecated - No longer supported)*
   - ~~Copy `LARGERslicer.gha`, `LARGERslicer.pdb`, and `Newtonsoft.Json.dll` from `LARGERslicer/Plugin Installation Files/LARGERSlicer/` to your Grasshopper Libraries folder~~
   - ~~Restart Rhino/Grasshopper~~
   - ~~Plugin components will appear under the **LARGER** category~~
   - **Note**: Manual installation is no longer supported. Please use the Package Manager for automatic updates and compatibility.

   **Upgrading from Manual Installation (Old Version)**
   
   If you previously installed LARGERslicer by manually copying `.gha` files to the Components folder, follow these steps to upgrade to the Package Manager version:
   
   1. **Open Grasshopper** and navigate to the Components folder
   2. **Delete old plugin files**: Remove all LARGERslicer-related files (`.gha`, `.pdb`, `Newtonsoft.Json.dll`) from the Components folder
   3. **Open Rhino Package Manager**:
      - In Rhino, type `PackageManager` (or `PaketManager` in German) in the command line
      - Search for "LARGERslicer"
      - Click "Install" (or "Anwenden" in German)
   4. **Restart your computer** (not just Rhino) - this ensures all plugin references are cleared
   5. **Re-slice your files**: After restart, open your Grasshopper files and regenerate toolpaths with the new version
   
   **Note**: The Package Manager version enables automatic updates, so you'll always have the latest features and bug fixes without manual file management.

2. **Download or clone the `.gh` files** from this repository:
   - [LARGERslicer Weber Robot and Ginger.gh](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/LARGERslicer%20Weber%20Robot%20and%20Ginger.gh) — Merged starter file with Easy Slicer, Path Creator, and Custom Path workflows
   - [UR5slicer.gh](EXAMPLE%20FILES/00%20-%20UNIVERSAL%20ROBOTS%20-%20UR5/UR5slicer.gh) — Dedicated UR5 robotic workflow

3. **Open Rhino** and create a new file in **millimeter** measurement units

4. **Launch Grasshopper** within Rhino

5. **File → Open** → select your desired `.gh` file

6. Unlock custom clusters if needed: right-click any password-protected component and enter **Supersizedprinting**

**Note**: Pufferfish is included with Rhino 8+ and will load automatically. If you encounter missing components, install them via Rhino's PackageManager.

## Setup

### Verify Installation

1. Ensure all plugins appear in the Grasshopper toolbar
2. Verify clusters appear in **Input**, **Path Maker**, and **G-Code Maker** stages
3. Check that custom components are unlocked and functional

### Initial Configuration

The LARGER Slicer is organized into two main stages:

- **Part 1**: 3D Print Path Maker - Generates toolpath curves
- **Part 2**: G-Code Maker - Exports machine-ready G-code

## Usage Guide

### Part 1: 3D Print Path Maker

**Basic Setup:**
1. **Input Geometry**: Right-click **Set One Surface** → pick your surface
2. **Enable Features**: Toggle **Skirt** and **Brim** with True/False buttons
3. **Configure Parameters**:
   - **Corner Fillet Radius (mm)**: Round off corners
   - **Min/Max Extrusion Width (mm)**: Standard and adaptive widths
   - **Layer Height (mm)**: Control Z-step sizes
   - **Point Spacing (mm)**: Sets point interval (default 2-5 mm)

**Output**: Generates **Toolpath Curves** along the midline of each layer

### Part 2: G-Code Maker

**Setup Process:**
1. **Preview Toolpath**: Use **Preview Slider (0→1)** to scrub through toolpath animation
2. **Select Printer Profile**: Choose **Ginger One** or **Weber DXR** (for UR5, use separate `UR5slicer.gh` in the UR5 folder)
3. **Run Preflight Check**: Validate the output file with **DXR File Health Check** and continue only with **GO**
4. **Set Output Location**: 
   - Use integrated desktop path finder, OR
   - Add your own filepath component with desired save location
5. **Configure Settings**:
   - **Job Name**: Enter desired filename (e.g., "Testobject")
   - **Feedrate/Speed**: Adjust print speed as needed
   - **Flow (%)**: Set extrusion multiplier

**Generate G-Code (Important 2-Step Process):**
1. **Click "1. Slice Data"** - This calculates weight, time, and generates coordinates
2. **Click "2. Export G-Code"** - This exports the final G-code file

**Note**: You MUST click "Slice Data" first, otherwise you'll only get standard start/end G-code without coordinates, weight, or time calculations. This two-step process keeps the script running efficiently.

**Outputs**:
- **Weight in kg**: Displays estimated material usage
- **Time**: Shows estimated print time
- **G-Code**: Generated machine-ready code with coordinates

## Project Structure

```
LARGER.slicer/
├── LICENSE                                           # Licensed under [CC BY-NC 4.0]
├── README.md                                         # This documentation
├── LARGERslicer/                                     # Grasshopper plugin source code
│   ├── README.md                                     # Plugin documentation
│   ├── LARGERslicer.csproj                           # .NET project file
│   ├── Components/                                   # Plugin components
│   │   ├── CNC/                                      # CNC toolpath components
│   │   ├── Export/                                   # DXR export components
│   │   └── Utils/                                    # Utility components
│   ├── Types/                                        # Custom data types
│   ├── Utils/                                        # Helper classes
│   ├── Resources/                                    # Icons and assets
│   ├── documentations/                               # Documentation (Markdown)
│   └── Plugin Installation Files/                    # Pre-built plugin files for manual install (deprecated)
│       └── LARGERSlicer/                             # Contains .gha/.pdb + Newtonsoft.Json.dll
└── EXAMPLE FILES/                                    # Machine-specific scripts and docs
   ├── 00 - UNIVERSAL ROBOTS - UR5/
   │   ├── README UR5 00 QUICK START GUIDE.md
   │   └── UR5slicer.gh
   ├── 00 - WEBER : GINGER [Robotic] 3D Printing/
   │   ├── LARGERslicer Weber Robot and Ginger.gh
   │   ├── LARGERslicer Multiaxial Weber Robot.gh (legacy / archived, no longer primary)
   │   ├── 00 - GINGER - ONE - README/
   │   └── 00 - WEBER - DXR25 - README/
   └── 00 - ZUENDT - 3D CNC MILLING/
      ├── README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md
      └── ZündHGPL PLT MSA.gh
```

### Key Components

- **Grasshopper Plugin**: `LARGERslicer.gha` — Custom Grasshopper components for DXR generation, CNC toolpaths, and utilities (see `LARGERslicer/` directory)
- **Primary Slicer**: `LARGERslicer Weber Robot and Ginger.gh` — Merged starter script with Easy Slicer, Path Creator (multiaxial), and Custom Path conversion workflows (repo path: `EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/`, labeled here as **00 - WEBER : GINGER [Robotic] 3D Printing**)
- **Machine Documentation**: Complete setup and operation guides for each platform (German & English) under `EXAMPLE FILES/`
- **UR5 Dedicated Script**: `UR5slicer.gh` — Specialized control for Universal Robots UR5 (`EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/`)
- **Development Area**: WIP folder contains experimental features and new developments

## System Overview

### Core Features

**LARGER Slicer Algorithm:**
- One merged starter file with three workflows: Easy Slicer, Path Creator (multiaxial), and Custom Paths
- Two-stage toolpath generation (Path Maker → G-Code Maker)
- Adaptive layer widths and live previews
- Support for skirts, brims with flip option
- Direct integration with multiple machine platforms

**LARGERslicer Grasshopper Plugin:**
- **DXR Processing**: DXR Generator and GCode Postprocessor for robot control systems
- **Preflight Safety**: DXR File Health Check with explicit readable/filename/content rule status
- **CNC Toolpaths**: Boustrophedon (zigzag) toolpath generation with Zünd PLT output
- **Utilities**: File operations, timestamps, spatial indexing, and more
- Automatic header calculation (runtime, layers, extrusion totals)
- Sequential line numbering and proper DXR file formatting
- Current documented release: 1.1.4

**Machine Control Systems:**
- **Ginger One**: Large-format pellet extruder with material database
- **UR5 Robot**: Complete Grasshopper workflow for Universal Robots systems  
- **Weber DXR**: Specialized control for Weber robotic platforms with DXR file generation

**Documentation & Support:**
- Comprehensive setup guides for all platforms
- Troubleshooting resources and operation manuals
- Active development with regular updates

## Troubleshooting

| Problem | Cause | Solution |
|---------|--------|----------|
| Missing plugin components | Plugins not installed | Install via Rhino's PackageManager |
| Surface not accepted | Wrong geometry type | Use **Set One Surface** on valid Brep surface |
| G-Code file not appearing | Write command not triggered | Click **Export G-Code** and confirm save path |
| DXR File Health Check returns NO GO | Invalid filename, unreadable file, or suspicious/incomplete content | Fix filename/path, re-export, and re-check until GO |
| Preview slider inactive | Path Preview not connected | Check **Path Preview** input wiring |
| Password-protected clusters | Components locked | Right-click component, enter **Supersizedprinting** |
| KUKA Robot brake test / start movement / safety-fence emergency stop (multiaxial) | CNC program not selected, or tool/extruder enters protected braking zone near fence | Follow [KUKA Safety Stop Recovery (Fence Proximity / Multiaxial)](#kuka-safety-stop-recovery-fence-proximity--multiaxial), then see [KUKA Robot Troubleshooting Guide](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md) or [Weber DXR25 Quick Start Guide](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20ENGLISH.md) Section 13 |

### KUKA Safety Stop Recovery (Fence Proximity / Multiaxial)

Use this when the robot stops near the fence, brakes audibly engage, and movement is blocked with safety/brake-test related messages on HMI/switch cabinet.

1. **Confirm status message** on HMI/Smartpad or cabinet display (safety stop, brake test required, movement blocked).
2. **Switch to setup path on KRC5**:
   - Turn key from **Remote** to **Gear** position.
   - Ensure mode is **EXT**, then switch to **T1**.
   - Turn key back; Smartpad/Fernbedienung may reboot.
3. **Manually move robot out of safety zone**:
   - Press and hold a rear dead-man switch (Totmannschalter).
   - Move axis-by-axis (or with SpaceMouse, if enabled) until the TCP/extruder is clearly outside the protected area.
   - Motion icons only turn green while dead-man is pressed.
4. **Return to automatic operation**:
   - Turn key to **Gear**, switch from **T1** back to **EXT**.
   - Turn key back to **Remote**/operating mode.
5. **If start still fails**:
   - Re-open and reselect the **cnc** program on Smartpad.
   - Run **Programm zurücksetzen** and re-reference if requested.
   - See [KUKA Robot Troubleshooting Guide](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md) and [Weber DXR25 Quick Start Guide](EXAMPLE%20FILES/00%20-%20WEBER%20%3A%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20ENGLISH.md) (Section 13).

**Tip for path planning (important for 45° multiaxial prints):**
- Prefer extruder tilt toward the wall / largest free area, not toward the safety door.
- Keep extra clearance before the fence because the braking zone begins before the physical barrier (approx. 10-15 cm depending on setup).
- Orient 45° jobs so the extruder points to the side opposite the entry/safety door.

### Getting Help

1. Check machine-specific Quick Start Guides
2. Review troubleshooting table above
3. Verify all prerequisites are installed
4. Ensure Rhino file is set to millimeter units

## License

Licensed under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) —  
non-commercial use only.  
© 2025 Moritz Wesseler.
---

**Note**: Weber DXR25 now supports multi-axial printing through Grasshopper script with automatic G-code to DXR conversion via the LARGERslicer plugin. The plugin provides DXR Generator and DXR GCode Postprocessor components for seamless conversion. Safety fence configuration and collision object setup are currently in development.

## Plugin Development

The LARGERslicer Grasshopper plugin source code is located in the `LARGERslicer/` directory. For plugin development, building, and component documentation, see:
- [Plugin README](LARGERslicer/README.md) — Plugin overview and development guide
- [Component Overview](LARGERslicer/documentations/COMPONENTS_OVERVIEW.md) — Complete component reference

### Building the Plugin

```bash
cd LARGERslicer
dotnet build
```

The build generates `.gha` files for all target frameworks in `bin/Debug/`.
