# LARGER SLICER

A complete ecosystem for large-format additive manufacturing, featuring the LARGER Slicer - a two-stage Grasshopper algorithm for generating large-format toolpaths and G-code directly in Rhino/Grasshopper.

**BY MORITZ WESSELER - FH MÜNSTER 2025**  
*Originally inspired by Ginger.Additive; now fully reimplemented with expanded machine support.*

Further Contributors: Fabio Koczula, Claudio Schröder
 

## Table of Contents

- [Supported Machines](#supported-machines)
- [Quick Start](#quick-start)
- [Installation](#installation)
- [Setup](#setup)
- [Usage Guide](#usage-guide)
- [Project Structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Supported Machines

The LARGER Slicer ecosystem supports multiple large-format additive manufacturing platforms:

| Platform | Status | Control Script | Documentation |
|----------|--------|----------------|---------------|
| **Ginger One Printer** | Production Ready | `LARGERslicer.gh` | [Ginger Quick Start Guide](https://github.com/Moeewe/LARGER.slicer/blob/main/PRINTER/00%20-%20GINGER%20-%20ONE/README%20GINGER%2000%20QUICK%20START%20GUIDE.md) |
| **Universal Robots UR5** | Production Ready | `UR5slicer.gh` | [UR5 Quick Start Guide](https://github.com/Moeewe/LARGER.slicer/blob/main/PRINTER/00%20-%20UNIVERSAL%20ROBOTS%20-%20UR5/README%20UR5%2000%20QUICK%20START%20GUIDE.md) |
| **Weber DXR25 Robot** | Multi-Axial Ready | `LARGERslicer.gh` + `DXR Script` | [Weber DXR25 Robot Quick Start Guide] (https://github.com/Moeewe/LARGER.slicer/blob/main/PRINTER/00%20-%20WEBER%20-%20DXR25/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20GERMAN.md)|

### Recommended Workflow

1. **Toolpath Generation**: Use `LARGERslicer.gh` for all toolpath generation
2. **Machine Setup**: Follow the appropriate Quick Start Guide above
3. **Advanced Control**: For UR5, use dedicated `UR5slicer.gh` for robotic workflows

## Quick Start

### Prerequisites

- **Operating System**: Windows 11+ or macOS 11 Big Sur+
- **Software**: Rhino 8+ with Grasshopper and Python 3
- **Required Grasshopper Plugins** (auto-load on first open):
  - Pufferfish
  - Clipper Components  
  - Sasquatch
  - LunchBox
  - Heteroptera
  - Wombat

### Get Started in 3 Steps

1. **Download**: Get `LARGERslicer.gh` from this repository
2. **Open**: Launch Rhino (set to mm), open Grasshopper, drag and drop the .gh file
3. **Start**: Follow the [Usage Guide](#usage-guide) below

## Installation

### System Requirements

- **Windows**: Windows 11 or newer
- **macOS**: macOS 11 Big Sur or newer  
- **Rhino**: Version 8 or newer with Grasshopper and Python 3

### Installation Steps

1. Download or clone the `LARGERslicer.gh` file from this repository
2. Open Rhino and create a new file in **millimeter** measurement units
3. Launch Grasshopper within Rhino
4. **File → Open** → select `LARGERslicer.gh`
5. Allow plugins to load automatically (may take a moment on first open)
6. Unlock custom clusters if needed: right-click any password-protected component and enter **Supersizedprinting**

**Note**: All required plugins should load automatically when opening the script for the first time. If you encounter missing components, install them via Rhino's PackageManager.

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
2. **Select Printer Profile**: Choose **Ginger One** or **Weber DXR** (for UR5, use separate `UR5slicer.gh`)
3. **Set Output Location**: 
   - Use integrated desktop path finder, OR
   - Add your own filepath component with desired save location
4. **Configure Settings**:
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
├── LARGERslicer.gh                                    # Main LARGER Slicer algorithm
├── LICENSE                                            # Licensed under [CC BY-NC 4.0]
├── README.md                                          # This documentation
└── PRINTER/                                           # Machine-specific documentation and scripts
    ├── 00 - GINGER - ONE/                            # Ginger V1.3 Beta LFAM system
    │   ├── README GINGER 00 QUICK START GUIDE.md     # Setup and operation guide
    │   └── Archive/                                   # Historical versions
    ├── 00 - UNIVERSAL ROBOTS - UR5/                  # UR5 Robot control system
    │   ├── README UR5 00 QUICK START GUIDE.md        # Setup and operation guide
    │   └── UR5slicer.gh                              # Dedicated UR5 control script
    ├── 00 - WEBER - DXR25/                           # Weber DXR25 Robot platform
    │   ├── README Weber DXR25 PRINTER QUICK START GUIDE.md  # Setup guide
    │   └── WIP Grasshopper Simulation/               # Development simulation scripts
    └── 01 - WIP - PRINTER GH SCRIPTS : NEW FEATURES/ # Development and new features
```

### Key Components

- **Primary Slicer**: `LARGERslicer.gh` - Main toolpath generation algorithm for all platforms
- **Machine Documentation**: Complete setup and operation guides for each platform
- **UR5 Dedicated Script**: `UR5slicer.gh` - Specialized control for Universal Robots UR5
- **Development Area**: WIP folder contains experimental features and new developments

## System Overview

### Core Features

**LARGER Slicer Algorithm:**
- Two-stage toolpath generation (Path Maker → G-Code Maker)
- Adaptive layer widths and live previews
- Support for skirts, brims with flip option
- Direct integration with multiple machine platforms

**Machine Control Systems:**
- **Ginger One**: Large-format pellet extruder with material database
- **UR5 Robot**: Complete Grasshopper workflow for Universal Robots systems  
- **Weber DXR**: Specialized control for Weber robotic platforms

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
| Preview slider inactive | Path Preview not connected | Check **Path Preview** input wiring |
| Password-protected clusters | Components locked | Right-click component, enter **Supersizedprinting** |

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

**Note**: Weber DXR25 now supports multi-axial printing through Grasshopper script with automatic G-code to DXR conversion. Safety fence configuration and collision object setup are currently in development.
