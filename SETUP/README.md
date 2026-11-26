# LARGER.slicer – SETUP

**English** · [Deutsch](README.de.md)

This folder contains everything needed to start with LARGER.slicer: the installers, the house definition for the Weber DXR25 and the Ginger One, the examples for students and the Extrusion Factor Test.

**Use `LARGERslicer Weber Robot and Ginger V2.gh`.** It has the three workflows of the lab, built with the current components, and needs **only the LARGER.slicer plug-in** (2.0 or newer) - no Robots, no Pufferfish. It opens without errors or warnings in Rhino 8 and Rhino 9. The first version (needs Robots and Pufferfish, no longer maintained) is now in `EXAMPLE FILES/99 - ARCHIVE` as `LARGERslicer Weber Robot and Ginger V1 (Robots plug-in).gh`.

| File | Purpose |
|---|---|
| `LARGERslicer Weber Robot and Ginger V2.gh` | **The house definition.** 01 Easy Ginger and Robot Slicer, 02 Advanced Multiaxial Robot Slicer, 03 Custom Robot Path Creator; LARGER.slicer only |
| `LARGERslicer - Beispiele für Studierende.gh` | Seven explained examples (German) to start with, see below |
| `LARGERslicer Extrusion Factor Test.gh` | Finds the extrusion factor: three single walls each for the Ginger One (0.18 / 0.21 / 0.24) and the Weber DXR (1.0 / 1.128 / 1.258, P1 = 1 on the controller). Measure the wall thickness, type it in, the file gives the new factor. LARGER.slicer only. When you write G-code with `Machine G-code`, set the new factor as `Extrusion Factor` on `Machine` |
| `Install Windows.bat` (+ `setup-windows.ps1`) | Installer for Windows |
| `LARGER.slicer Installer.pkg` | Installer for macOS, signed and notarized (only in the release download; built with `macos-installer/build.sh` from `Install Mac.command`) |
| `RobotLibrary/` | KUKA KR120 robot library of FH Münster MSA (`KR120_R2700_2`), for the archived first version |

## Installation

**Requirements:** Rhino 8 (recommended), Rhino 9 or Rhino 7 · internet connection

1. Close Rhino.
2. Run the installer:

   | Windows | macOS |
   |---|---|
   | Double-click `Install Windows.bat` (if Windows shows *Windows protected your PC*: **More info** → **Run anyway**) | Double-click `LARGER.slicer Installer.pkg` (signed and notarized by Apple) |

3. Start Rhino, create a file in **millimeters**, open Grasshopper and open `LARGERslicer Weber Robot and Ginger V2.gh` from this folder.

## What the installer does

- Installs the latest compatible **Robots**, **LARGERslicer** and **Pufferfish** from the Rhino Package Manager – for Rhino 8 and/or Rhino 7, whichever is installed.
- From 2.0.5: with Rhino 9 on the computer it also installs LARGER.slicer for Rhino 9 and writes the line in Grasshopper 2's `CustomPluginInclude.txt`, so Grasshopper 2 loads it from the first start (the Grasshopper 2 plug-in needs Rhino 9; it refuses to load in the Grasshopper 2 preview of Rhino 8).
- **LARGERslicer** comes from the folder `packages` of the release download (`LARGERslicer-SETUP.zip`) when that package is newer than the one on the Rhino package server, so the plug-in fits the definition in this folder. The folder exists only in the release download, not in the repository.
- Copies the **KR120_R2700_2** robot library to the folder the Robots plugin reads (`Documents/Robots`, on macOS with Rhino 7 also `~/Robots`).
- Can be run again at any time:
  - current plugins are kept, older ones are updated, newer ones (e.g. beta builds) are kept,
  - old `.gha` files copied by hand into the Grasshopper Libraries folder are detected and can be moved to a backup folder,
  - a differing existing library file is saved as `*.bak-<date>` before it is replaced.

## Troubleshooting

| Problem | Solution |
|---|---|
| macOS: what did the installer do? | See `~/Library/Logs/LARGERslicer-Installer.log`. Without the package: `bash "Install Mac.command"` in Terminal |
| “Package-Server nicht erreichbar” (package server not reachable) | Check the internet connection and run the installer again |
| Grasshopper: robot system **KR120_R2700_2 missing** (archived first version only) | Run the installer again, or click **Libraries** on *Load Robot System* and download the online library **FH Muenster MSA** |
| Components missing or loaded twice | Run the installer again, let it move old manual copies, restart Rhino |
| Rhino was running during the installation | Restart Rhino – plugins are only loaded at startup |

## More guides

- [Weber DXR25 – Commissioning and Operating Manual](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20ENGLISH.md)
- [Ginger One – Quick Start](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md)
- [LARGER.slicer on GitHub](https://github.com/Moeewe/LARGER.slicer)

## Examples

- `LARGERslicer - Beispiele für Studierende.gh`: seven explained examples (German) to start with: 01 vase, 02 box with a top, 03 and 04 ornaments where you want them, 05 fan by layer for the Weber DXR, 06 clay vase on the UR5 (the Robot chain, URScript), 07 milling on the Zünd. Each with a Control Panel and a Result.
- On an empty canvas, `Quick Start` (Slicer tab) places eight more examples, and `CNC - Quick Start` (CNC tab) the milling chain.
