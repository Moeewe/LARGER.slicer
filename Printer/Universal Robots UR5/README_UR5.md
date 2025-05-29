# UR5 3D-Printing & Drawing Slicer

**File:** `UR5.slicer.gh`  
**Version:** Work-In-Progress  
**By:** Moritz Wesseler  
**Contributors:** Fabio Koczula & Torben Kurzhals – FH Münster 2025  

> **Robot Clay Printing and Movement Algorithm**  
> Based on the Robots plugin by Visose  

---

## Quick Start Guide

1. Connect UR5 via Ethernet (IP: `192.168.1.44`)
2. Open Rhino & load `UR5.slicer.gh`
3. Configure robot orientation in Input Cf and plane flips
4. Test full animation before upload
5. Upload & execute program on UR5 pendant

---

## Prerequisites

### Required Software
- Rhino 3D
- Grasshopper
- Robots plugin by Visose
- MeshEdit plugin

### Hardware Setup
- UR5 Robot
- Ethernet connection
- Compatible tools (pen holder, clay extruder, etc.)

---

## Table of Contents

1. [Network Setup](#network-setup)
2. [Software Setup](#software-setup)
3. [Working with the Definition](#working-with-the-definition)
4. [Robot Configuration](#robot-configuration)
5. [Practical Tips & Best Practices](#practical-tips--best-practices)
6. [Troubleshooting](#troubleshooting)
7. [Future Tutorials](#future-tutorials)
8. [Credits](#credits)

---

## Network Setup

1. **Wired Connection Only**  
   Connect your laptop's Ethernet port directly to the UR5's network adapter.

2. **Configure Static IP (Laptop)**  
   1. Open **Network Settings**  
   2. Select **USB-C Dock Ethernet** → **Details**  
   3. Under **TCP/IP**, set **Configure IPv4** to **Manual** and enter:  
      - **IP Address:** `192.168.1.44`  
      - **Subnet Mask:** `255.255.255.0`  

3. **Robot IP** (default in Grasshopper):  
   ```
   192.168.1.66
   ```

4. **Verify Connection**  
   ```bash
   ping 192.168.1.66
   ```

> **Troubleshooting Tips**  
> - If the Grasshopper upload hangs, disconnect the Ethernet cable, verify your IP settings and cable connection, then reconnect.  
> - Always **save** your Grasshopper definition before uploading to prevent crashes.  
> - When the connection succeeds, the UR5 pendant will display **Abort | Execute or Continue Script** – select **Execute** to start the job.  

---

## Software Setup

### Rhino & Grasshopper Installation
1. **Open Rhino** → load your `.3dm` scene  
2. In Rhino's command line, type `Grasshopper` → press Enter  
3. Install required plugins via **_Tools → PackageManager_**:
   - Robots
   - MeshEdit

### Loading the Definition
- **Drag & drop** `UR5.slicer.gh` into the Grasshopper canvas  
- Wait for all clusters to load—no red components should remain  
- If prompted, unlock clusters with the provided password  

---

## Working with the Definition

### Geometry Pipeline & Simple Slicer
- **Inputs**:  
  - Geometry  
  - First layer height, nth-layer height  
  - Point distance, zigzag toggle & thickness  
  - Number of bottom layers, bottom layer linewidth  
- **Outputs**:  
  - `planes`, `curves` for each slice  
  - `geometry`, `center`, bottom-layer curves & points  

### Worksurface Mapper
- **Inputs**:  
  - `planes`, `curves`, original geometry  
  - `animation %`, workspace `center`  
  - Distances 1 & 2, workspace `width`, `length`, `height`  
- **Outputs**:  
  - Transformed `planes` & `curves` positioned over your build surface  

### Tool Components
- **Pen/Brush Tool**:  
  - Planar `curve`  
  - `Precision (mm)`, `Rotate Planar`, `Rotate 3D`  
  - Oriented `planes` for tool attachment  

---

## Robot Configuration

### Important Configuration Steps
1. **Robot Configuration (Input Cf)**
   - Double-click to toggle checkboxes for Shoulder, Elbow, and Wrist configurations
   - These settings affect the robot's joint orientations and are crucial for different tools
   - Adjust these settings until the robot's pose matches your needs

2. **Pre-Upload Animation Check**
   - ALWAYS run through the complete animation using the animation slider before uploading
   - This helps identify potential collisions or unreachable positions
   - Check that all movements are smooth and feasible

3. **Flip Button Usage**
   - Only use the Flip button if the robot tries to approach paths "from below"
   - This is specifically for cases where the simulation shows the robot colliding with the table or print area
   - Different from joint configuration settings (Shoulder, Elbow, Wrist)

### Simulation & Export Settings
- **Inputs**:  
  - `planes`  
  - Toggles: `flip planes`, `rotate planes`, `ironing`  
  - Joint‐limits: shoulder, elbow, wrist  
  - Motion mode dropdown (e.g. Joint/Linear)  
  - `speed [mm/s]`, `animation %`  
  - Buttons: `Upload`, `Play`, `Pause`  
  - `Tool Selection` (e.g. Pen Holder, Clay Nozzle)  
- **Outputs**:  
  - `program` (URScript text)  
  - `Warnings`, `Errors` panels  

---

## Practical Tips & Best Practices

### 1. Curve Direction Awareness
Robots follow curve directions from start to end. If multiple curves are connected without adjusting direction, the robot might zigzag between start points. Important considerations:
- Incorrect curve directions can lead to unnecessary empty movements or direction changes
- For parallel lines (e.g., in infill patterns), flip every second curve for efficient back-and-forth movement
- Use the `Flip Curve` component in Grasshopper, controlled by index or layer
- This significantly reduces print time and improves quality by minimizing travel movements

### 2. Tool Lifting (e.g., Brush or Pen)
When working with sensitive tools like brushes or paste extruders, proper tool lifting is crucial:
- Add lifting points with Z-offset (10–30 mm upwards) BEFORE the start point
- Add lifting points AFTER the end point to prevent smearing
- Implement this through additional points with a "Move" command in Z-direction
- For drawing or brushing applications, these lifting movements prevent:
  - Material dragging across the surface
  - Tool damage from lateral movements
  - Unwanted marks or strokes

### 3. Consistent Point Spacing
Use `Divide Curve` instead of `Evaluate Curve` for consistent spacing, especially across multiple curves. For optimal results:
- `Divide Curve` guarantees points at start and end
- Calculate division count based on curve length and desired spacing
- Use formula: `round(curve.Length / desired_spacing)`
- This ensures uniform extrusion rates and consistent material flow
- Particularly important for:
  - Clay printing
  - Material extrusion
  - Brush applications with varying pressure

### 4. Robot Orientation & Collision Avoidance
In the Robots plugin, proper configuration of the robot's orientation is crucial:
- Use the Robot Configuration (Input Cf) component to adjust joint orientations
- Double-click to toggle Shoulder, Elbow, and Wrist checkboxes for different tool setups
- These settings determine the robot's default pose and joint configurations
- The Flip button should only be used if the robot tries to approach paths "from below" and would collide with the work surface
- Always check the simulation preview for:
  - Natural arm movements
  - Potential self-collisions
  - Optimal working angles
  - Reachability of all points
- Adjust orientation settings until you achieve smooth, collision-free movements

### 5. TCP & Tool Orientation
The starting angles of each joint vary significantly depending on the TCP (Tool Center Point):
- Vertical tools (e.g., 3D print head) require different configurations than horizontal tools (e.g., pen holder)
- When switching tools:
  - Check and adjust the default robot pose
  - Verify tool orientation matches the physical setup
  - Test movements in simulation before running
  - Consider the approach angles for different operations
- Each tool might need its own saved configuration for optimal results

### 6. Tool Length Settings
Tool length must match the physical setup precisely:
- Lengths are defined in:
  - Tool definition clusters
  - Robot component settings
- Incorrect settings can cause:
  - Tool collision with the work surface
  - Insufficient contact pressure
  - Damage to tools or work surface
- Always double-check measurements when:
  - Creating new tools
  - Adjusting the physical setup
  - Changing tool attachments
- Consider using a tool offset variable for quick adjustments

### 7. Unexpected Lifting Movements
Some clusters automatically insert lifting movements between paths or even inside single paths:
- Check the Grasshopper definition for automated safety movements
- These are usually grouped and labeled clearly
- Common locations for automatic lifts:
  - Between individual paths
  - At layer changes
  - During tool operations
- Can be disabled or edited if:
  - The movements are unnecessary
  - You need continuous contact
  - You want to optimize print time
- Consider keeping safety movements for:
  - Initial testing
  - Complex geometries
  - Areas with high collision risk

---

## Troubleshooting

- **No network link**: verify static IP and cable
- **Red components**: install missing GH plugins or unlock clusters
- **Upload fails**: ensure UR5 firmware matches your GH Robots plugin version
- **Multiple overlapping robots in simulation**: This usually indicates that your input planes are not flattened. Check if your planes are in nested lists/branches - they should be in a single, flat list. Use the Flatten component to resolve this issue, as separate branches will create individual robot programs.

---

## Future Tutorials

- **Clay 3D-Print Workflow** (detailed layering & drying)  
- **Pen & Brush Drawing Workflow** (multi-color, pressure control)  

*(Coming soon)*

---

## Credits

**Authors:**  
— Moritz Wesseler  

**Further Contributors:**  
— Fabio Koczula & Torben Kurzhals, FH Münster 2025  

© All rights reserved.
