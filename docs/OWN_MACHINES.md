# Your own machine in LARGER.slicer

LARGER.slicer knows the Ginger One and the Weber DXR on a KUKA robot. Any other machine is added
without code: a small text file (`.machine`) for the G-code, and, if you want to see it move, a
Rhino file (`.3dm`) with its 3D model. A robot arm comes from the library of the Robots plug-in.

This guide takes you through four cases:

1. [A G-code printer, no model](#1-a-g-code-printer-no-model) – five minutes
2. [A bed slinger](#2-a-bed-slinger)
3. [A gantry with its 3D model](#3-a-gantry-with-its-3d-model) – it moves in Printer Simulation and is checked for collisions
4. [A robot (Robots library)](#4-a-robot-robots-library)

A complete template lies in `EXAMPLE FILES/Machines/`: `My Gantry.machine` + `My Gantry.3dm`
(a 300 x 300 mm gantry with frame, bed, Z rails, beam and head) and `My Bed Slinger.machine`.

---

## Where the files go

| File | Folder |
|---|---|
| `My printer.machine` | `Documents/LARGER/Machines` (macOS: `~/Documents/LARGER/Machines`, Windows: `Documents\LARGER\Machines`) |
| `My printer.3dm` (optional) | next to it, **same name** – or named in the file with `model = ...` |

Every `.machine` file in that folder appears in the list of every **new** `Machine` component (as
"Manufacturer · Name"). A file anywhere else can be used with the `File` input of `Machine`.
Pass the folder on as it is: the model is found by its name.

---

## The `.machine` file

Plain text, one `key = value` per line, then the code blocks. Lines that begin with `#` are
comments. Keys and blocks:

| Key | Example | Meaning |
|---|---|---|
| `name` | `My Gantry` | Name of the machine (required) |
| `manufacturer` | `Me` | Shown before the name in the list |
| `kind` | `gantry` | How it moves: `gantry`, `bedslinger`, `delta`, `belt 45`, `robot` (see below). Default `gantry` |
| `prefix` | `MYG` | Start of the file names (at most 6 letters) |
| `extension` | `.gcode` | File ending |
| `bed` | `300 x 300 x 300` | Print area X x Y (x height) in mm, from 0, 0 in the front left corner of the bed |
| `extrusion factor` | `1` | E = bead width x layer height x length x flow x this factor |
| `first layer factor` | `1` | Extra factor on the first layer |
| `filament diameter` | `1.75` | 0 = E is a volume (pellet extruder); larger = E is a length of filament of this diameter |
| `section` | `bead` | `rectangle` (width x height) or `bead` (squashed bead with round sides) |
| `travel speed` | `6000` | mm/min for moves without extrusion; 0 = the highest speed of the print settings |
| `material` | `PLA` | Written where a code says `{material}` |
| `bed temperature` | `60` | Written where a code says `{bed}` |
| `model` | `My Gantry.3dm` | The 3D model (`.3dm` or `.obj`), relative to the `.machine` file |

| Block | Written |
|---|---|
| `[start]` | before the print |
| `[layer]` | at every layer change; `{layer}` = number of the layer (1, 2 ...), `{z}` = its height |
| `[after layer 2]` | once, when layer 2 is done (e.g. the fan on); any number |
| `[end]` | after the print |

The same file works in LARGER.app (`kind`, `bed` with height, `model`); the app ignores the keys
it does not use.

### What the kind decides

| kind | flat layers | non-planar, nozzle vertical | angled / multi-axis (tool tilts) | How its model moves |
|---|---|---|---|---|
| `gantry` | yes | yes | **no** | Z, then Y with Z, then X with Y and Z |
| `bedslinger` | yes | yes | **no** | the bed (and layer Y) moves in Y, the head in X and Z |
| `delta` | yes | yes | **no** | only the effector (layer X) |
| `belt 45` | yes, at the belt angle | **no** | only at its belt angle | as a gantry |
| `robot` | yes | yes | yes | not by Printer Simulation: use the Robot tab (see 4) |

`Machine G-code` checks every layer of the path: a layer in one tilted plane needs a tilting tool,
a curved layer can be printed with a vertical nozzle. What the machine cannot print is an **error**
("NOT PRINTABLE on My Gantry [Gantry (X, Y, Z)]: 12 layer(s) lean up to 45 degrees ..."); the G-code
is still written, so you can look at it, but do not run it. Non-planar layers on a gantry get a
grey remark (allowed, worth knowing): check the head against the part in `Printer Simulation`.

---

## 1. A G-code printer, no model

Easiest way, in Grasshopper:

1. Place `Machine`. Choose "Generic · G-code machine (Marlin, Klipper)" as a start.
2. Type a `Name`, your `Start Code`, `Layer Code`, `End Code` (one line per item, or a panel
   with several lines), the `Extrusion Factor` and, for filament, the `Filament Diameter`.
   Choose the `Kind`.
3. Press `Save machine`. The file is written to `Documents/LARGER/Machines/<Name>.machine`.
4. Connect `Machine` → `Machine` of `Machine G-code`, your toolpath → `Toolpath`, press
   `Save G-code`.

Or write the file by hand – the smallest one that works:

```text
name = My printer
kind = gantry
bed = 500 x 500
extrusion factor = 1
[start]
G21
G90
G28
M83
[end]
M84
```

Without a model, `Machine` and `Printer Simulation` draw the bed as a grey plate, and the
collision check takes the head as a round body (radius twice the bead width, at least 50 mm across, 150 mm
high) over the nozzle.

## 2. A bed slinger

Write `kind = bedslinger` (see `EXAMPLE FILES/Machines/My Bed Slinger.machine`). The G-code is
the same as for a gantry: the path is in the coordinates of the bed. What changes:

- `Printer Simulation` moves the **bed** (layer `Bed`, and layer `Y` if you have one) by -Y, so the
  part rides on it, and the head (`X`) only in X and Z. The printed path (`Printed`) moves with
  the bed.
- For the model use the layers `Frame`, `Bed`, `Z` (the X beam that goes up and down), `X` (the
  head) and `TCP` – see 3.

## 3. A gantry with its 3D model

Start from `EXAMPLE FILES/Machines/My Gantry.3dm` or draw your own in Rhino:

1. **Units and origin.** Millimetres. The origin is the point the G-code counts from (X0 Y0 Z0),
   on the top of the bed – usually its front left corner. X to the right, Y to the back, Z up.
2. **Draw the machine in one pose**, any pose, e.g. the head in the middle of the bed. Meshes,
   surfaces, polysurfaces, extrusions or SubDs all work (blocks do not: explode them).
3. **Put every part on the layer of the axis it moves with:**

   | Layer | Moves with | Put on it |
   |---|---|---|
   | `Frame` | nothing | posts, rails of the frame, electrics, everything that stands still (any other layer counts as Frame) |
   | `Bed` | nothing (bed slinger: Y) | the bed plate and what holds it |
   | `Z` | Z | the gantry frame on the vertical axes, its rails for Y |
   | `Y` | Y and Z | the cross beam |
   | `X` | X, Y and Z | the head: carriage, extruder, hot end, nozzle (also `Head` or `Effector`) |
   | `TCP` | – | **one point** exactly at the nozzle tip in the pose you drew |

   Sublayers count by their last name (`My Gantry::X` is `X`).
4. **Colours.** Every object keeps its object colour (or its layer colour); the model is drawn
   matte in these colours, like the robot. Edges sharper than 35 degrees are kept crisp.
5. Save it next to the `.machine` file with the same name (`My Gantry.3dm`), or write
   `model = My Gantry.3dm` into the file. An `.obj` with one group (`g X`, `g Y` ...) per layer
   works too.

**How it moves.** For the nozzle at the path point (x, y, z) and the TCP point at (x0, y0, z0):
Z moves by (0, 0, z - z0), Y by (0, y - y0, z - z0), X by (x - x0, y - y0, z - z0); Frame and Bed
stand. Nothing turns. On a delta only X moves; arms and carriages stay as drawn.

**Watch it.** Connect `Simulation` of `Machine G-code` to the first input of `Printer Simulation`
(one wire, as `Program` of `Robot Program` goes to `Robot Simulation`). Move `Pose` (0 = start,
1 = end, following the print time of the G-code) or click `Play`; `Film Length` sets how many
seconds one run takes on the screen. Instead of the `Simulation` wire you can connect a
`Machine` and a `Toolpath`.

**Collisions.** With `Check collisions` on, `Printer Simulation` checks (as LARGER.app does):

- the head (`X`) and the beam (`Y`; on a bed slinger `Z`) against the part printed so far and
  against the bed (top at Z 0),
- the head against the frame (each connected piece of `Frame` on its own; pieces the head
  touches in the pose you drew are taken as built onto it, e.g. a cable chain),
- leaving out the zone round the nozzle tip (two layers high, as wide as the nozzle there plus
  half a bead), where the nozzle sits on the beads by design.

What collides at the moment shown turns **red** (head, beam, frame pieces, a ball where it
touches). `Collisions` reports the check along the whole print (about 300 poses, plus both ends
of every travel move and the end of every layer) with the first move and suggestions. The check is
a sample check like the robot's: it can miss a collision between two checked poses.

Tips: give the head its real hull (hot end, fan duct, sensors); keep the model small
(a few 10 000 triangles); check that the TCP point is really at the nozzle tip – a head that dips
into the bed at every pose means it is not.

## 4. A robot (Robots library)

Robot arms come from the library of the
[Robots plug-in](https://github.com/visose/Robots) (no `.machine` file needed for the arm):

1. Put the robot library into `Documents/RobotSystems` (`Documents/Robots` and `~/Robots`, where the installers copy it, are searched as well): an `.xml` file (the arm: its axes,
   ranges, speeds) and a `.3dm` of the **same name** next to it with a layer
   `RobotArm.MANUFACTURER.MODEL` that holds seven meshes – the base and the six links, in the
   home pose of the arm (A2 -90, A3 90) – in that order, or on sublayers named `0` ... `6`.
2. `Robot`: `Library` = the path of the `.xml` (or its text), `Cell Name` = the name of the cell
   in it. The 3D model is found beside the `.xml`, in one of these library folders, or at
   `Geometry File`.
3. `Robot Tool`: TCP plane (origin at the nozzle tip, Z from the tip back into the tool), weight,
   and your extruder as a mesh at `Mesh`, drawn where it sits on the flange (the flange is world
   XY at the origin, as in Robots).
4. `Robot Cell` (+ `Cell Geometry` for the floor, fence and walls: your own cell as
   `Documents/LARGER/Cells/My cell.3dm`), then `Robot Program` → `Program` → `Robot Simulation`.

`Robot Simulation` plays it like `Printer Simulation` (same clock, `Play`, `Film Length`).
`Show cell` (off by default) draws the cell around the robot; the collision check always uses it,
and what collides is drawn red even when the cell is hidden.

A `.machine` file with `kind = robot` only says what the machine can print (everything); its
G-code holds positions only. The robot program comes from `Robot Program`.

---

## Checklist

- [ ] `name` and `kind` set, `bed` in mm from the front left corner
- [ ] start code homes the machine, sets absolute positions (`G90`) and relative extrusion (`M83`)
- [ ] extrusion factor checked with a test wall (`Material Factor`)
- [ ] model: layers `Frame`, `Bed`, `Z`, `Y`, `X`, one point on `TCP` at the nozzle tip
- [ ] `Printer Simulation` plays the whole print without red parts
- [ ] first print at reduced speed, hand on the stop button
