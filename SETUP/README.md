# LARGER.slicer – SETUP

**English** · [Deutsch](README.de.md)

This folder contains everything needed to set up the Grasshopper definition **`LARGERslicer Weber Robot and Ginger.gh`** for the Weber DXR25 and the Ginger One.

| File | Purpose |
|---|---|
| `LARGERslicer Weber Robot and Ginger.gh` | Grasshopper definition with Easy Slicer, Path Creator (multiaxial) and Custom Paths |
| `Install Windows.bat` (+ `setup-windows.ps1`) | Installer for Windows |
| `Install Mac.command` | Installer for macOS |
| `RobotLibrary/` | KUKA KR120 robot library of FH Münster MSA (`KR120_R2700_2`) |

## Installation

**Requirements:** Rhino 8 (recommended) or Rhino 7 · internet connection

1. Close Rhino.
2. Run the installer:

   | Windows | macOS |
   |---|---|
   | Double-click `Install Windows.bat` | Right-click `Install Mac.command` → **Open** → **Open** |

3. Start Rhino, create a file in **millimeters**, open Grasshopper and open `LARGERslicer Weber Robot and Ginger.gh` from this folder.

## What the installer does

- Installs the latest compatible **Robots**, **LARGERslicer** and **Pufferfish** from the Rhino Package Manager – for Rhino 8 and/or Rhino 7, whichever is installed.
- Copies the **KR120_R2700_2** robot library to the folder the Robots plugin reads (`Documents/Robots`, on macOS with Rhino 7 also `~/Robots`).
- Can be run again at any time:
  - current plugins are kept, older ones are updated, newer ones (e.g. beta builds) are kept,
  - old `.gha` files copied by hand into the Grasshopper Libraries folder are detected and can be moved to a backup folder,
  - a differing existing library file is saved as `*.bak-<date>` before it is replaced.

## Troubleshooting

| Problem | Solution |
|---|---|
| macOS: “cannot be opened because it is from an unidentified developer” | Right-click `Install Mac.command` → **Open** |
| “Package-Server nicht erreichbar” (package server not reachable) | Check the internet connection and run the installer again |
| Grasshopper: robot system **KR120_R2700_2 missing** | Run the installer again, or click **Libraries** on *Load Robot System* and download the online library **FH Muenster MSA** |
| Components missing or loaded twice | Run the installer again, let it move old manual copies, restart Rhino |
| Rhino was running during the installation | Restart Rhino – plugins are only loaded at startup |

## More guides

- [Weber DXR25 – Commissioning and Operating Manual](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20ENGLISH.md)
- [Ginger One – Quick Start](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md)
- [LARGER.slicer on GitHub](https://github.com/Moeewe/LARGER.slicer)
