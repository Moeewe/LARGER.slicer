# Weber DXR25 3D-Printing Robot (KUKA KR120)

**Work in Progress** – structure only and start & end G-Code

---

## Table of Contents

1. [Introduction](#introduction)  
2. [System Overview](#system-overview)  
3. [Hardware Requirements](#hardware-requirements)  
4. [Software Installation](#software-installation)  
   - [Robot Controller Setup](#robot-controller-setup)  
   - [Slicer & Plugin Installation](#slicer--plugin-installation)  
5. [KUKA Integration](#kuka-integration)  
   - [TCP & Base Configuration](#tcp--base-configuration)  
   - [Communication Protocols](#communication-protocols)  
6. [Calibration & Homing](#calibration--homing)  
7. [Printing Workflow](#printing-workflow)  
   - [Preparing the Part File](#preparing-the-part-file)  
   - [Toolpath Generation](#toolpath-generation)  
   - [Executing the Job](#executing-the-job)  
8. [Safety & Best Practices](#safety--best-practices)  
9. [Troubleshooting](#troubleshooting)  
10. [Files & Directory Structure](#files--directory-structure)
11. [Start & End G-Code](#start--end-g-code)  
12. [Future Development](#future-development)  
13. [License](#license)  
14. [Credits](#credits)  

---

## Introduction

*(Placeholder for project description, goals, scope.)*

---

## System Overview

*(High-level architecture: Weber DXR25 + KUKA KR120 + peripherals.)*

---

## Hardware Requirements

- KUKA KR120 robot  
- Weber DXR25 print head  
- Extruder control unit  
- Power & network interfaces  

---

## Software Installation

### Robot Controller Setup

*(Instructions for loading programs to KUKA controller.)*

### Slicer & Plugin Installation

*(List and install slicing software and Grasshopper/other plugins.)*

---

## KUKA Integration

### TCP & Base Configuration

*(Define tool center point, base frames, coordinate systems.)*

### Communication Protocols

*(Ethernet/IP, OPC UA, WebSocket, or other interfaces.)*

---

## Calibration & Homing

*(Steps to calibrate robot, probe printer home position, set offsets.)*

---

## Printing Workflow

### Preparing the Part File

*(Convert CAD to mesh, export compatible format.)*

### Toolpath Generation

*(Generate robot-specific path via slicer or custom script.)*

### Executing the Job

*(Load job on KUKA, start print, monitor progress.)*

---

## Safety & Best Practices

*(Emergency stop, safe zones, collision avoidance, protective measures.)*

---

## Troubleshooting

*(Common errors and remedies: loss of communication, path deviation, filament issues.)*

---

## Files & Directory Structure

```text
├── configs/               # TCP, base, and calibration files
├── slicer-presets/        # .json or .ini profiles
├── robot-programs/        # KRL files and scripts
├── docs/                  # this README and additional docs
└── examples/              # sample part files and gcode exports

```

## Start & End G-Code


<details>
<summary><strong>Start G-Code</strong></summary>

```gcode
; START CODE

; turn on temperature
M42 P57 I T1 S1		
M42 P57 I T1 S1
G4 P2000
M42 P57 I T1 S0		
M42 P57 I T1 S0
G4 P200

; set units to mm
G21

; use absolute coordinates
G90

; reset extrusion
G92 E0

; use relative distances for extrusion
M83

; end of start gcode

```
</details>


<details>
<summary><strong>End G-Code</strong></summary>

```gcode

; END CODE

; deactivate temperature control (pin PC4)
M42 P49 I T1 S1
M42 P49 I T1 S1
G4 P200


; safe deactivate temperature control (pin PC4)
M42 P49 I T1 S1
M42 P49 I T1 S1
G4 P200

; deactivate motors
M84

; let temperature-control activate (pin PC4)
G4 P2000
M42 P49 I T1 S0
M42 P49 I T1 S0
G4 P2000


; end of end code

```
</details>

---


## Future Development

(Planned features: non-planar slicing, multi-material, closed-loop feedback.)

⸻

## License

(e.g. MIT License – see LICENSE file.)

⸻

## Credits

(Author, affiliations, acknowledgments.)
