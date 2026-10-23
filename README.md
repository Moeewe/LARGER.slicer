<div align="center">

<img alt="LARGER.slicer logo" src="LARGERslicer/icon.png" width="128" height="128">

# LARGER.slicer

**Large-format 3D printing and CNC toolpaths in Rhino / Grasshopper**<br>
Slicer, robot code (DXR / KUKA) and machine guides for Weber DXR25, Ginger One, UR5 and Zünd – in one package.

[![Latest release](https://img.shields.io/github/v/release/Moeewe/LARGER.slicer?label=release&color=2c6aa6)](https://github.com/Moeewe/LARGER.slicer/releases/latest) [![Rhino](https://img.shields.io/badge/Rhino-8%20%7C%207-2c6aa6)](https://www.rhino3d.com/) [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-2c6aa6)](#quick-start) [![License: CC BY-NC 4.0](https://img.shields.io/badge/license-CC%20BY--NC%204.0-lightgrey)](https://creativecommons.org/licenses/by-nc/4.0/)

<a href="https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip"><img alt="Download LARGERslicer-SETUP.zip" src="https://img.shields.io/badge/Download-LARGERslicer--SETUP.zip-2c6aa6?style=for-the-badge&logo=rhinoceros&logoColor=white"></a>

[Release notes](https://github.com/Moeewe/LARGER.slicer/releases/latest) · [Documentation](LARGERslicer/documentations/README.md) · [Changelog](LARGERslicer/CHANGELOG.md)

**English** · [Deutsch](README.de.md)

</div>

---

## Features

- **One Grasshopper definition, three workflows** – *Easy Slicer* slices surfaces automatically, *Path Creator (multiaxial)* turns prepared toolpaths into robot paths, *Custom Paths* exports your own paths as robot code.
- **Robot code for Weber DXR25 (KUKA)** – DXR Generator and G-code → DXR postprocessor with automatic header (runtime, layers, extrusion totals) and line numbering.
- **Variable line widths** – `DXR Variable Width` scales the extrusion along X, Y or Z with a colored width preview.
- **Preflight safety** – `DXR File Health Check` validates file name, readability and content before printing (GO / NO GO).
- **CNC toolpaths** – zigzag (boustrophedon) toolpaths with HPGL/PLT output for the Zünd G3.
- **One-click setup** – the installer sets up all plugins and the KUKA KR120 robot library on Windows and macOS.
- **Machine guides** – operating and troubleshooting guides for every supported machine (German and English).

## Download

📥 **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** – always the latest release.
Contains the Grasshopper definition, the installer for Windows and macOS and the KUKA KR120 robot library.

🧩 **Plugin only:** search for **LARGERslicer** in the Rhino Package Manager (`PackageManager` command).

## Quick Start

**Requirements:** Rhino 8 (recommended) or Rhino 7 · Windows 11 or macOS 11+ · internet connection during setup

1. Download **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** and unzip it.
2. Close Rhino and run the installer in the `SETUP` folder:

   | Windows | macOS |
   |---|---|
   | Double-click `Install Windows.bat` | Right-click `Install Mac.command` → **Open** → **Open** |

3. Start Rhino, create a file in **millimeters**, open Grasshopper and open `LARGERslicer Weber Robot and Ginger.gh` from the `SETUP` folder.

The installer installs **Robots**, **LARGERslicer** and **Pufferfish** from the Rhino Package Manager and copies the KUKA KR120 robot library (`KR120_R2700_2`) to the folder the Robots plugin reads. It can be run again at any time and then only updates what is outdated.

<details>
<summary><b>Installer details and troubleshooting</b></summary>

- **Existing installations:** current plugins are kept, older ones are updated, newer ones (e.g. beta builds) are kept.
- **Old manual installations:** `.gha` files copied into the Grasshopper Libraries folder are detected and can be moved to a backup folder (otherwise components load twice).
- **Robot library:** a differing existing file is saved as `*.bak-<date>` before it is replaced.
- **macOS blocks the installer** (“unidentified developer”): right-click `Install Mac.command` → **Open**.
- **Robot system `KR120_R2700_2` missing:** run the installer again, or click **Libraries** on the *Load Robot System* component and download the online library **FH Muenster MSA**.
- **Rhino 7:** gets Robots 1.9 (Robots 2.x requires Rhino 8). The definition is tested in Rhino 8.
- **Pin a plugin version:** add the version after the package name in the `PACKAGES` list at the top of the installer, e.g. `"Robots 2.4.4"`.

More: [SETUP guide](SETUP/README.md)

</details>

## Supported Machines

| Machine | Type | Grasshopper definition | Guide |
|---|---|---|---|
| **Weber DXR25** | KUKA robot, multiaxial | `LARGERslicer Weber Robot and Ginger.gh` | [English](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md>) · [Deutsch](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md>) |
| **Ginger One** | Large-format pellet printer | `LARGERslicer Weber Robot and Ginger.gh` | [Quick Start](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md>) |
| **Universal Robots UR5** | Collaborative robot | `UR5slicer.gh` | [Quick Start](<EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/README UR5 00 QUICK START GUIDE.md>) |
| **Zünd G3 L-2500** | CNC milling / cutting | `ZündHGPL PLT MSA.gh` | [Quick Start](<EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md>) |

## Usage Guide

`LARGERslicer Weber Robot and Ginger.gh` contains three workflows – copy the one you need into a new Grasshopper file:

1. **Easy Slicer** – slices surfaces and creates the toolpath automatically.
2. **Path Creator (multiaxial)** – converts a prepared toolpath into multiaxial robot paths.
3. **Custom Paths** – uses only the conversion/export components to create DXR robot code from your own paths.

Password-protected clusters: right-click the component and enter **Supersizedprinting**.

<details>
<summary><b>Part 1: 3D Print Path Maker</b></summary>

1. **Input geometry:** right-click **Set One Surface** → pick your surface.
2. **Skirt / Brim:** switch on or off with the True/False toggles.
3. **Parameters:**
   - **Corner Fillet Radius (mm)** – rounds off corners
   - **Min/Max Extrusion Width (mm)** – standard and adaptive widths
   - **Layer Height (mm)** – Z step
   - **Point Spacing (mm)** – point interval (default 2–5 mm)

**Output:** toolpath curves along the midline of each layer.

</details>

<details>
<summary><b>Part 2: G-Code / DXR Maker</b></summary>

1. **Preview:** scrub through the toolpath with the **Preview Slider (0 → 1)**.
2. **Printer profile:** choose **Ginger One** or **Weber DXR** (UR5: use `UR5slicer.gh`).
3. **Preflight check:** validate the file with **DXR File Health Check** and continue only on **GO**.
4. **Output location:** use the integrated desktop path or your own file path.
5. **Settings:** job name, feedrate / speed, flow (%).

**Export in two steps:**

1. Click **1. Slice Data** – calculates weight, time and coordinates.
2. Click **2. Export G-Code** – writes the file.

If you skip *Slice Data*, only the standard start/end code is written – without coordinates, weight or time.

**Outputs:** weight (kg), print time, machine-ready code.

</details>

## Documentation

| Topic | Link |
|---|---|
| Overview of all guides | [Documentation index](LARGERslicer/documentations/README.md) |
| Operate a machine | [Machine guides](LARGERslicer/documentations/README.md#machine-guides) |
| KUKA / DXR25 robot errors | [English](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md) · [Deutsch](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md) |
| Plugin components | [Component overview](LARGERslicer/documentations/COMPONENTS_OVERVIEW.md) · [Inputs/outputs](LARGERslicer/documentations/COMPONENT_INPUTS_OUTPUTS.md) |
| What's new | [Changelog](LARGERslicer/CHANGELOG.md) |

## Troubleshooting

| Problem | Solution |
|---|---|
| Plugin components missing or red | Run the installer again, then restart Rhino |
| Robot system `KR120_R2700_2` missing | Run the installer again, or download the **FH Muenster MSA** online library via **Libraries** on *Load Robot System* |
| Surface not accepted | Use **Set One Surface** with a valid Brep surface |
| G-code file does not appear | Click **Export G-Code** and check the save path |
| DXR File Health Check returns **NO GO** | Fix file name / path, export again and check until **GO** |
| Preview slider inactive | Check the **Path Preview** input wiring |
| DXR25: robot not mastered / external axis E1 (KSS01347, KSS00106, KSS15047) | [Robot Not Mastered (E1)](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-not-mastered-e1) – a program reset alone does not help |
| DXR25: brake test / start movement does not begin | [Brake test / start movement](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-does-not-perform-brake-test--start-movement-after-startup) |
| DXR25: emergency stop near the safety fence | [Emergency stop near safety fence](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-emergency-stop-triggered-near-safety-fence-multiaxial-printing) |
| DXR25: robot does not move / reference run does not start | [Robot does not move](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed) |

> [!TIP]
> **45° multiaxial prints:** tilt the extruder toward the wall or the largest free area, not toward the safety door, and keep extra clearance from the fence – the braking zone starts about 10–15 cm before the physical barrier.

## Development

<details>
<summary><b>Repository structure</b></summary>

```
LARGER.slicer/
├── SETUP/                     # Start here: installer, Weber/Ginger definition, robot library
├── LARGERslicer/              # Grasshopper plugin (C#)
│   ├── Components/            # CNC, Export and Utility components
│   ├── Resources/             # Component icons
│   ├── documentations/        # Guides and references
│   └── build-package.sh/.bat  # Builds the Rhino 7 and Rhino 8 Yak packages
└── EXAMPLE FILES/             # Machine-specific definitions and guides
    ├── 00 - UNIVERSAL ROBOTS - UR5/
    ├── 00 - WEBER - GINGER [Robotic] 3D Printing/
    └── 00 - ZUENDT - 3D CNC MILLING/
```

</details>

**Build the plugin:**

```bash
cd LARGERslicer
./build-package.sh          # or build-package.bat on Windows; optional: new version, e.g. 1.1.14
```

Creates `dist/largerslicer-<version>-rh8_0-any.yak` and `dist/largerslicer-<version>-rh7_0-any.yak`. More: [Plugin README](LARGERslicer/README.md).

## License

© 2025 Moritz Wesseler, FH Münster · Licensed under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) – non-commercial use only.

Contributors: Fabio Koczula, Claudio Schröder · Originally inspired by Ginger.Additive, fully reimplemented with expanded machine support.
