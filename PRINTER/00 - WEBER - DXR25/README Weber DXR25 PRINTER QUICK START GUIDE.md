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

The Weber DXR25 3D-Printing Robot system combines a KUKA KR120 industrial robot with the Weber DXR25 print head for large-format additive manufacturing. This system enables both conventional and multi-axial 3D printing using specialized DXR code format.

**Important**: Always use the provided Grasshopper script for robot integration. The existing script can generate the required .DXR files from G-code and enables both multi-axial and conventional printing.

---

## System Overview

The system architecture consists of:
- **KUKA KR120 Robot**: 6-axis industrial robot for precise movement
- **Weber DXR25 Print Head**: Specialized extruder for large-format printing
- **Control System**: Robot controller with custom DXR code interpretation
- **Grasshopper Integration**: Automatic G-code to DXR conversion
- **Remote Access**: TeamViewer and Windows Remote Desktop capabilities

---

## Hardware Requirements

- KUKA KR120 robot  
- Weber DXR25 print head  
- Extruder control unit  
- Power & network interfaces  

---

## Software Installation

### Robot Controller Setup

**DXR Code Processing:**
- Machine requires DXR code format (not standard G-code)
- Conversion options:
  1. **Recommended**: Direct conversion in Grasshopper script
  2. Alternative: Weber's conversion script on machine desktop (control cabinet)

**Remote Access:**
- **TeamViewer**: Available for 5-minute sessions (no Pro license)
- **Windows Remote Desktop**: May be available (to be confirmed)
- **Live Video Feed**: Camera link available on-site only (not stored online)

### Slicer & Plugin Installation

**Grasshopper Script Features:**
- Automatic G-code to DXR conversion
- Multi-axial printing support
- Material factor integration
- Robot-specific toolpath generation

**Required Components:**
- Grasshopper with Robot Plugin
- Weber DXR25 Grasshopper script (located in WIP Grasshopper Simulation folder)
- Proper safety fence and collision object configuration

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

**DXR Code Format:**
The machine does not run G-code natively but requires DXR code - a robot-specific format converted from G-code. Our Grasshopper script automatically handles this conversion (recommended method).

**Multi-Axial Printing:**
Multi-axial printing is enabled through additional axis movements in the G-code, describing the rotation of the print head/extruder in A, B, C coordinates. When all A, B, C values are at zero position, the extruder stands straight and prints horizontally.

**DXR Code Structure:**
- **N Commands**: Original line numbers from G-code (e.g., N24 = original line 24)
- **XYZ Coordinates**: Direct 1:1 transfer from G-code
- **G90/G91**: Switch between absolute and relative movements
- **Extrusion Values**: In square brackets [value*P1], where P1 is the material factor set in printer UI

**Example DXR Code Structure:**
```
N24 G1 X911.817 Y952.527 Z32.000 A0.000 B0.000 C0.000 G91 XE=[64.430*P1] G90
```

### Executing the Job

*(Load job on KUKA, start print, monitor progress.)*

---

## Safety & Best Practices

**Critical Safety Rules:**
- **Never start a print without direct line of sight to the robot** - ensure no objects are in the print area
- **Always wait for heating phase completion** - ensure print head reaches proper temperature before starting
- **Read all instructions thoroughly before operation**
- **Set proper safety fences and collision objects** in Robot Plugin
- **Be aware of multi-axial print angles** - extruder may collide with robot arm at certain angles

**Material Handling:**
- Do not modify material factor in Grasshopper (represents standard material strand thickness)
- Material factors are set in machine UI (e.g., PETG: 3.1, PLA: 3.5)
- These factors automatically adjust extrusion amounts for optimal results per material

---

## Troubleshooting

**Print Won't Load / Infinite Loading Animation:**
Most common cause: Robot controller was turned on after the control cabinet with print display.

**Solution:**
1. Go to display menu (top right) → **Runtime** → **End Runtime**
2. Open new Explorer window on Windows desktop
3. Look for **NC Drive** in left sidebar (usually shows red connection symbol when problematic)
4. Click on **NC Drive** - symbol should change to green
5. Print should now load properly

**Connection Issues:**
- Check **NC Drive** connection status in Windows Explorer
- Ensure proper startup sequence: Control cabinet first, then robot controller
- Verify network connections between systems

**Multi-Axial Printing Issues:**
- Check A, B, C axis values in DXR code
- Ensure collision objects are properly configured
- Verify safety fence settings in Robot Plugin
- Monitor for potential extruder-robot collisions at extreme angles

---

## Files & Directory Structure

```text
├── configs/               # TCP, base, and calibration files
├── slicer-presets/        # .json or .ini profiles
├── robot-programs/        # KRL files and scripts
├── docs/                  # this README and additional docs
└── examples/              # sample part files and gcode exports

```

## DXR Code Example

<details>
<summary><strong>Complete DXR Code Example</strong></summary>

```dxr
;ProgRunTimeTotal = [6501]
;post_processor_version =[V1.0.3.3]
;machine_type =[DXR.KUKA]
;number of rows in org. Gcode = [38025]
;number of movement rows =[37591]
;Xmin = [891.529]
;Xmax = [1200.391]
;Ymin = [775.523]
;Ymax = [1025.491]
;Zmin = [2.000]
;Zmax = [391.829]
;Eges = IC[1138401.000]
;AiSync = [1]
;config end
;========================================================
G90

;========================================================
N10 V.E.GLOBAL[27] = 0
N11 L layer_sub.nc
N12 L wall_sub.nc
N13 G1 X899.633 Y900.000 Z32.000 A0.000 B0.000 C0.000 F3000.000 
N14 G1 Z2.000 A0.000 B0.000 C0.000 
N15 G1 Y903.167 A0.000 B0.000 C0.000 G91 XE=[38.529*P1] G90 F1800.000 
N16 G1 X899.877 Y908.495 A0.000 B0.000 C0.000 G91 XE=[64.893*P1] G90 
N17 G1 X900.559 Y915.416 A0.000 B0.000 C0.000 G91 XE=[84.590*P1] G90 
N18 G1 X901.359 Y920.690 A0.000 B0.000 C0.000 G91 XE=[64.849*P1] G90 
N19 G1 X902.400 Y925.921 A0.000 B0.000 C0.000 G91 XE=[64.811*P1] G90 
N20 G1 X903.679 Y931.099 A0.000 B0.000 C0.000 G91 XE=[64.763*P1] G90 
N21 G1 X905.697 Y937.754 A0.000 B0.000 C0.000 G91 XE=[84.359*P1] G90 
N22 G1 X907.511 Y942.770 A0.000 B0.000 C0.000 G91 XE=[64.609*P1] G90 
N23 G1 X909.552 Y947.698 A0.000 B0.000 C0.000 G91 XE=[64.526*P1] G90 
N24 G1 X911.817 Y952.527 A0.000 B0.000 C0.000 G91 XE=[64.430*P1] G90 
N25 G1 X915.095 Y958.660 A0.000 B0.000 C0.000 G91 XE=[83.864*P1] G90 
N26 G1 X917.852 Y963.226 A0.000 B0.000 C0.000 G91 XE=[64.170*P1] G90 
N27 G1 X920.816 Y967.661 A0.000 B0.000 C0.000 G91 XE=[64.042*P1] G90 
N28 G1 X923.979 Y971.956 A0.000 B0.000 C0.000 G91 XE=[63.904*P1] G90 
N29 G1 X928.391 Y977.332 A0.000 B0.000 C0.000 G91 XE=[83.123*P1] G90 
N30 G1 X931.985 Y981.272 A0.000 B0.000 C0.000 G91 XE=[63.548*P1] G90 
N31 G1 X935.757 Y985.044 A0.000 B0.000 C0.000 G91 XE=[63.382*P1] G90 
N32 G1 X939.698 Y988.639 A0.000 B0.000 C0.000 G91 XE=[63.204*P1] G90 
N33 G1 X945.073 Y993.050 A0.000 B0.000 C0.000 G91 XE=[82.163*P1] G90 
N34 G1 X949.368 Y996.214 A0.000 B0.000 C0.000 G91 XE=[62.768*P1] G90 
N35 G1 X953.803 Y999.177 A0.000 B0.000 C0.000 G91 XE=[62.566*P1] G90 
N36 G1 X958.369 Y1001.934 A0.000 B0.000 C0.000 G91 XE=[62.357*P1] G90 
N37 G1 X964.502 Y1005.212 A0.000 B0.000 C0.000 G91 XE=[81.020*P1] G90 
N38 G1 X969.331 Y1007.477 A0.000 B0.000 C0.000 G91 XE=[61.855*P1] G90 
N39 G1 X974.259 Y1009.518 A0.000 B0.000 C0.000 G91 XE=[61.627*P1] G90 
N40 G1 X979.275 Y1011.332 A0.000 B0.000 C0.000 G91 XE=[61.395*P1] G90 
N41 G1 X985.930 Y1013.351 A0.000 B0.000 C0.000 G91 XE=[79.737*P1] G90 
N42 G1 X991.108 Y1014.629 A0.000 B0.000 C0.000 G91 XE=[60.844*P1] G90 
N43 G1 X996.340 Y1015.670 A0.000 B0.000 C0.000 G91 XE=[60.602*P1] G90 
N44 G1 X1001.613 Y1016.470 A0.000 B0.000 C0.000 G91 XE=[60.352*P1] G90 
N45 G1 X1008.534 Y1017.152 A0.000 B0.000 C0.000 G91 XE=[78.362*P1] G90 
N46 G1 X1013.862 Y1017.396 A0.000 B0.000 C0.000 G91 XE=[59.775*P1] G90 
N47 G1 X1019.196 A0.000 B0.000 C0.000 G91 XE=[59.525*P1] G90 
N48 G1 X1024.524 Y1017.152 A0.000 B0.000 C0.000 G91 XE=[59.270*P1] G90 
N49 G1 X1031.445 Y1016.470 A0.000 B0.000 C0.000 G91 XE=[76.947*P1] G90 
N50 G1 X1036.719 Y1015.670 A0.000 B0.000 C0.000 G91 XE=[58.688*P1] G90 
```
</details>

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

**Current Development Areas:**
- **Safety Fence Configuration**: Proper setup of collision objects and safety boundaries
- **Multi-Axial Print Optimization**: Solving potential extruder-robot arm collision issues
- **Robot Plugin Integration**: Complete integration with proper fence and collision settings
- **Remote Access Enhancement**: Confirming Windows Remote Desktop functionality

**Completed Features:**
- ✅ **Multi-Axial Printing**: Now available with Grasshopper script
- ✅ **DXR Code Conversion**: Automatic conversion from G-code implemented
- ✅ **Material Factor Integration**: Automatic material-specific extrusion adjustment
-  **Live Video Monitoring**: Camera link soon available (on-site only)

**Future Enhancements:**
- Enhanced collision detection algorithms
- Improved material database integration
- Advanced multi-material printing capabilities
- Real-time print monitoring and feedback systems

⸻

## License

(e.g. MIT License – see LICENSE file.)

⸻

## Credits

(Author, affiliations, acknowledgments.)
