# How to Open a Grasshopper Definition in Rhino

This README explains the simple steps to load a Grasshopper file into Rhino.

---

## Prerequisites

- **Rhino** 7 or newer installed  
- **Grasshopper** plugin available (ships with Rhino 6+)  
- A Rhino project file (`.3dm`) and a Grasshopper definition file (`.gh`)

---

## Steps

1. **Open the Rhino File**  
   - Launch Rhino.  
   - Go to **File → Open** (or press `Ctrl+O`).  
   - Select your `.3dm` file and click **Open**.

2. **Launch Grasshopper**  
   - In the Rhino command line, type  
     ```
     Grasshopper
     ```  
     and press `Enter`,  
     or click the Grasshopper icon in the Rhino toolbar.

3. **Load the Grasshopper Definition**  
   - In the Grasshopper window, simply **drag & drop** your `.gh` file onto the canvas.  
   - Wait a moment — Grasshopper will parse and display your definition.

4. **Verify Components**  
   - Ensure all clusters and custom components resolve (no warnings).  
   - If any plugins are missing, install them via Rhino’s PackageManager.

---

## Tips & Troubleshooting

- **Missing Plugins**:  
  If you see components marked in red, install the required plugin (e.g., LunchBox, Pufferfish) via **_Tools → PackageManager_** in Rhino.

- **Locked Clusters**:  
  Right-click on a locked cluster and enter the password if prompted.

- **Saving**:  
  After loading, save your Grasshopper file via **File → Save As** in the Grasshopper window.

---

You're now ready to work with your Grasshopper definition!
