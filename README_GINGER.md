# Ginger V1.3 Beta 3D Printer Cheat Sheet

> 3D printing at a larger scale LFAM requires special attention to the printer and its settings.  
> This handbook helps you master the unique requirements of large-format 3D printers (Ginger V1.3 Beta).

---

## Table of Contents

- [The Process](#the-process)  
- [Starting a Print](#starting-a-print)  
- [3D Printing Production Plate](#3d-printing-production-plate)  
- [Important Pre-Print Checks](#important-pre-print-checks)  
- [Geometries & Design](#geometries--design)  
  - [Geometry Types](#geometry-types)  
  - [Characteristics](#characteristics)  
  - [Design Rules](#design-rules)  
  - [Recommended Slicing Methods](#recommended-slicing-methods)  
- [Slicing](#slicing)  
  - [Software Overview](#software-overview)  
  - [Layer Size Rules](#layer-size-rules)  
  - [General Slicing Tips](#general-slicing-tips)  
    - [Travel Moves](#travel-moves)  
    - [Support Structures](#support-structures)  
    - [Overhangs](#overhangs)  
    - [Bridging](#bridging)  
    - [Infill](#infill)  
    - [Corners](#corners)  
    - [Double Beads](#double-beads)  
- [Grasshopper Slicing](#grasshopper-slicing)  
  - [Slicing in Grasshopper](#slicing-in-grasshopper)  
  - [Preparing Slicing in Grasshopper](#preparing-slicing-in-grasshopper)  
- [Materials](#materials)  
  - [Material Settings](#material-settings)  
  - [Dry Materials](#dry-materials)  
  - [Specifications](#specifications)  
  - [Notes & Recipes](#notes--recipes)  
- [Troubleshooting During Printing](#troubleshooting-during-printing)  
  - [Mixer Settings](#mixer-settings)  
  - [Cooling Guidelines](#cooling-guidelines)  
  - [Homing](#homing)  
  - [Bed Leveling](#bed-leveling)  
  - [Specific Problems](#specific-problems)  
- [Credits](#credits)  

---

## The Process

1. **Design Geometry**  
   - Create the 3D model using your preferred design software.

2. **Slice the Geometry**  
   - Import the model into your slicer software and prepare it for printing.

3. **Transfer File to Printer**  
   - Save the sliced file and transfer it to the printer via SD card or wireless connection.

4. **Preheat Printer & Bed**  
   - Begin preheating both the printer and the bed to the appropriate temperatures.

5. **Extrude/Purge Material**  
   - Extrude and purge material to ensure the nozzle is clean and ready.

6. **Start Print**  
   - Initiate the print from the printer’s interface.

7. **Set Mixer Settings & Flow**  
   - Adjust the mixer settings and flow rate as needed during the print.

8. **Print**  
   - Allow the printer to complete the job, monitoring if necessary.

9. **Cool Down and Remove**  
   - Let the print cool down, then carefully remove it from the bed.

---

## Starting a Print

Follow these steps on the printer interface:

1. **Power On**  
   Turn on the printer.

2. **Preheat**  
   Start heating the bed and the extruder.

3. **Set Temperature**  
   Adjust the temperature as needed, then press the button to confirm.

4. **Auto Feeding**  
   Turn auto-feeding **on**.

5. **Fan Control**  
   Ensure fans are turned **off**.

6. **Bed Preparation**  
   Apply glue, tape, or other adhesive to the print bed.

7. **Purge**  
   Purge material to clean the extruder.

8. **Home the Printer**  
   Home the axes to ensure proper positioning.

9. **Select File**  
   Choose your print file and start the print.

10. **Mixer Settings**  
    Set the mixer ratio (e.g., `93/7%`).

11. **Flow Adjustment**  
    Adjust the flow rate during the brim phase.

12. **Cooling**  
    Turn fans on after the first 4 layers (set between 60–100%).

13. **Bed Control**  
    Turn off the bed when it’s no longer needed.

14. **Print**  
    Enjoy your print!

---

## 3D Printing Production Plate

Use this template each time you run a print:

- **Date:** __________________  
- **Operator:** __________________  
- **File:** __________________  

- **Mixer:** ________  
- **Flow:** ________  
- **Slicer:**  
  - [ ] Grasshopper  
  - [ ] Cura  
  - [ ] Prusa  
  - [ ] Orca  

- **Material:**  
  - [ ] PLA  
  - [ ] PETg  
  - [ ] ABS  
  - [ ] ASA  
  - [ ] __________  

- **Nozzle:**  
  - [ ] Adapter  
  - [ ] 2 mm  
  - [ ] 3 mm  
  - [ ] 5 mm  
  - [ ] 8 mm  

| Zone | Material | Nozzle |
| :--: | :------: | :----: |
|  1   |          |        |
|  2   |          |        |
|  3   |          |        |

---

## Important Pre-Print Checks

Before starting a print, ensure the following:

1. **Nozzle Size**  
   Verify that the nozzle size matches your slicing settings.

2. **Nozzle Cleanliness**  
   Clean the nozzle before homing the printer.

3. **Power Setup**  
   Avoid plugging the bed and printer into the same power socket.

4. **Bed Leveling**  
   Confirm that the bed is properly leveled.

---

## Geometries & Design

### Geometry Types

- **Polysurface:**  
  A combination of surfaces that are joined together. Can be open or closed.

- **Surface:**  
  A single, continuous face. Typically open.

- **BREP (Boundary Representation):**  
  Represents the shape of a solid object, can include surfaces and edges. Can be open or closed.

- **Mesh:**  
  A collection of vertices, edges, and faces that defines the shape of a polyhedral object.

### Characteristics

- **Open/Closed Versions:**  
  - **Open:** Geometry with gaps or holes, often requires closing or additional processing before slicing.  
  - **Closed:** Watertight geometry, ready for slicing.

- **Surface Properties:**  
  - **Top Open:** Ideal for prints where the top remains exposed.  
  - **Top Closed:** Suitable for sealed, solid prints.  
  - **Top Smooth:** Best for designs requiring a flat or curved top surface.  
  - **Top Angled:** Requires careful slicing to manage steep angles or overhangs.  
  - **Undercuts:** Requires specific slicing strategies to avoid print failures.

### Design Rules

- **Continuous Toolpath:**  
  Ensure the toolpath is continuous to minimize retractions and maintain smooth printing.

- **No Seams or Bridging:**  
  Design your model to avoid seams and bridging for better surface quality and structural integrity.

- **Brim or Skirt:**  
  Always include a brim or at least a skirt to enhance bed adhesion and stabilize the print.

### Recommended Slicing Methods

1. **Simple & Fast:**  
   - **Software:** Orca  
   - **Use for:** Standard geometries, quick prints, straightforward shapes.  
   - **Features:** Delete pre-generated start and end G-code, suitable for planar slicing.

2. **Custom & Complex:**  
   - **Software:** Grasshopper  
   - **Use for:** Customized prints, non-planar geometries, complex forms.  
   - **Features:**  
     - Adaptive slicing  
     - Custom brim, skirt  
     - No seams

---

## Slicing

### Software Overview

- **Cura**  
  _Note: STEP files are not supported in Cura. Convert your models to a compatible format (e.g., STL or OBJ) before importing._

- **Orca**  
  _Important:_ When using Orca, delete everything in the start and end G-code sections to customize the print process and avoid conflicts with the printer’s specific settings.

- **Grasshopper (Custom Slicing)**  
  Grasshopper allows for advanced, custom slicing methods, providing greater control over the slicing process. Ideal for complex geometries and specialized print requirements.

### Layer Size Rules

- **Layer Width to Layer Height Ratio:**  
  The ratio of layer width to layer height should be at least 2:1 or higher. Ensures proper layer adhesion and print stability.

- **Maximum Layer Height:**  
  The layer height should be no more than 60% of the nozzle diameter. Staying within this limit ensures that each layer bonds properly to the previous one and maintains print quality.

- **Minimum Layer Width:**  
  The layer width should be at least 150% of the nozzle diameter. Provides sufficient coverage and ensures that each pass of the nozzle overlaps correctly with the previous one.

### General Slicing Tips

#### Travel Moves

a) **Continuing Toolpath:**  
   Ensure the toolpath is continuous to minimize unnecessary retractions and reduce print time.

b) **Combine Prints:**  
   If possible, combine multiple prints into a single job to optimize travel moves and reduce idle time.

c) **Use Artificial Ooze Walls:**  
   Implement artificial ooze walls to manage excess filament during travel moves and reduce stringing.

#### Support Structures

- **Dynamic Z-Axis Lifting:**  
  Consider dynamically lifting the Z-axis during travel moves to avoid collisions and improve print quality, especially with complex geometries.

#### Overhangs

- **Standard Printing:**  
  Maintain a maximum overhang angle of 45° to ensure proper support and prevent sagging.

- **45° Printing:**  
  For prints oriented at 45°, consider achieving steeper overhangs up to 90°.

- **Stepover Rule:**  
  Apply the stepover rule to manage overhangs effectively, ensuring that each layer provides adequate support for the next.

#### Bridging

- **Avoid When Possible:**  
  Minimize the use of bridges, as they can lead to poor print quality. Design your model to eliminate or reduce bridging whenever possible.

#### Infill

- **Integrate into Design:**  
  Incorporate infill patterns directly into your design to optimize strength and material usage, ensuring better structural integrity and aesthetics.

#### Corners

- **Round or Step Divide:**  
  Round sharp corners or divide them into steps to reduce stress concentration and improve print quality.

- **Nozzle Diameter Consideration:**  
  Keep the corner radius at least 0.5× the nozzle diameter to ensure smooth transitions.

#### Double Beads

- **Rule of Thumb:**  
  Follow these guidelines for double beads to achieve optimal layer bonding and wall thickness:  
  - **Layer Width:** Use 0.8–0.9× the layer width.  
  - **Nozzle Diameter:** Set the layer height/width to 1.2–1.4× the nozzle diameter for consistent extrusion and wall strength.

---

## Grasshopper Slicing

### Slicing in Grasshopper

1. **Check Geometry Type**  
   Determine whether the geometry is a **Surface**, **Open Polysurface**, or **Closed Polysurface**. This will influence how you approach slicing and what scripts or tools you may need.

2. **Planar or Non-Planar**  
   Assess whether the geometry is planar or non-planar. Consider using **adaptive layer heights** for non-planar surfaces to improve print quality and reduce material usage.

3. **Create Surface from Open Polysurface**  
   If working with an open polysurface, create a closed surface by sealing any gaps. This ensures the model can be sliced correctly and results in a watertight print.

4. **Check Script Requirements**  
   Identify the appropriate script or slicing settings required for your specific geometry. Different types of models may require different approaches for optimal slicing.

5. **Choose Bottom Infill, Brim, and/or Skirt**  
   Select the appropriate base support structures:  
   - **Bottom Infill:** Provides strength and stability to the base layers.  
   - **Brim:** Helps with adhesion to the build plate, especially for larger prints.  
   - **Skirt:** Primes the extruder and provides a border around the print without touching it.

6. **Assemble Components**  
   Combine all components into the final model. Pay attention to the direction of curves; often, some curve directions may need to be flipped to ensure proper alignment and flow during slicing.

### Preparing Slicing in Grasshopper

1. **Contour Slicing**  
   Start by slicing the geometry into layers using the **Contour** component.

2. **Order and Sort Curves**  
   Organize and align the curves along the Z-axis, matching the geometry’s offset.

3. **Divide and Join Curves**  
   Divide curves into points, create a seam or continuous spiral, then join them. Flip curves if needed for correct alignment.

4. **Guide and Flip Curves**  
   Ensure guide curves and main curves are oriented in the same direction.

5. **Generate G-code**  
   Create G-code for the specific print type:  
   - **Non-Planar:** Varying feed rates, calculated by the distance between points on curves with different heights.  
   - **Planar:** Constant feed rate due to uniform layer height.

---

## Materials

### Material Settings

| Material             | Type    | Zone 1 | Zone 2 | Zone 3 | Additive    | Mixer  |
| -------------------- | ------- | -----: | -----: | -----: | ----------- | ------ |
| Nateruworks PLA      | Pellets |    220 |    210 |    205 | Gleitmittel | 93/7%  |
|                      |         |        |        |        |             |        |
|                      |         |        |        |        |             |        |

### Dry Materials

All except PLA: PETG, ABS, ASA, PC, PET.

### Specifications

- Pellets, Flakes, Filament  
- PLA, ABS, etc.  
- Dry / Not Dry  
- Mixer Settings  
- Additive as glycol for flakes

### Notes & Recipes

-  
-  
-  
-  
-  
-  
-  

---

## Troubleshooting During Printing

### Mixer Settings

The mixer controls the ratio of materials fed into the extruder. Based on our experience, we recommend a **93/7%** mix ratio.

### Cooling Guidelines

Proper cooling is crucial, especially for overhangs. However, avoid using cooling during the initial layers to ensure the polymer sticks to the bed and prevents warping.

- **First Layers:** No cooling for the first 2–4 layers.  
- **Cooling Rates:** Use 60% cooling for most prints; increase to 100% only for strong overhangs.

### Homing

Homing is essential to ensure the correct movement of the Z-axis. Always check if there’s any leftover material on the nozzle before homing to prevent damage to the build plate.

### Bed Leveling

Regularly auto-level the bed through the menu and save the settings in EEPROM. Don’t forget to set and save a new Z-offset. Additionally, manually measure the frame’s distance from the bed. If it’s uneven, power off the printer and manually adjust each motor until all four Z-axis motors are at the same height.

### Specific Problems

#### First Layer Does Not Stick / No Adhesion

- **Bed Leveling:** The bed might not be properly leveled. Check for frame misalignment, readjust, or rerun auto-leveling.  
- **Z-Offset:** Verify the Z-offset and adjust if necessary. Ensure the print bed is heated and apply adhesive spray.  
- **First Layer Speed:** Slow down the speed of the first layer compared to the rest.  
- **Flow Rate:** Increase the flow rate for the first layer.

#### Warping

- **Brim:** Add a brim to increase surface area and prevent warping.  
- **Securing the Print:** Use screws or clamps to hold the print in place, apply more adhesive, or use a permanent print surface like tape or fleece.  
- **Cooling:** Set the cooling to 0% for the first 2–4 layers to improve adhesion.  
- **Bed Temperature:** Turn on the printbed to approx. 60 °C.

#### No / Insufficient Material from Extruder

- **Purge:** Purge a significant amount of material to clear any blockage.  
- **Flow/Speed Adjustment:** Adjust the flow rate or print speed to ensure proper extrusion.

#### Polymer Droplets on the Print

- **Nozzle Maintenance:** Apply new Teflon tape to the printer’s nozzle to ensure a tight seal.  
- **Mixing Issues:** Properly mix the liquid masterbatch to prevent pellets from sticking together.  
- **Drying:** Ensure the material is properly dried before printing.

#### Material Plopping, Hissing, or Visible Steam at the Nozzle

- **Drying:** Dry the material thoroughly to prevent moisture-related issues.

#### Poor Print Quality

- **Material Dryness:** Ensure the material is dry before printing.  
- **Temperature Adjustment:** Adjust the extrusion temperature up or down to optimize print quality.

#### Printer Crashes into the Part

- **Clearance Issues:** The clearance between the extruder and the part may not be sufficient, especially with non-planar printing. This can cause the extruder or bed-leveling sensor to touch the print.  
- **Design Adjustment:** Consider redesigning the part to make it larger or adjust the height to prevent collisions.

### Notes

-  
-  
-  
-  
-  
-  
-  

---

## Credits

Cheat Sheet & Handbook “Ginger V1.3 Beta 3D Printer”  
© Moritz Wesseler – July 2024
