# Universal Robots (UR3, UR5, UR10 and their e-Series) in LARGER.slicer

Since 2.0.4 a UR arm runs on the same Robot chain as the KUKA, without the Robots plug-in:

`Robot` → `Robot Tool` → `Robot Cell` → `Robot Program` → `Robot Simulation`

Example: example 06 of `EXAMPLE FILES/LARGERslicer - Beispiele für Studierende.gh` (also in `SETUP`), a clay
vase on the UR5 of the FH Muenster. The old definition `UR5slicer.gh` (Robots plug-in) is in
`EXAMPLE FILES/99 - ARCHIVE`. Operating the arm: the guide in `EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5`.

## The chain

| Component | For a UR |
|---|---|
| `Robot` | `Robot` choice: UR3, UR3e, UR5, UR5e, UR10, UR10e (Denavit-Hartenberg values of Universal Robots; the arm drawn from Universal Robots' own models, see THIRD-PARTY-NOTICES.md). From 2.0.5 it warns when a `Library` file overrides the chosen UR model |
| `Robot Tool` | Presets of the FH Muenster UR5: clay extruder V2, pen holder, wool spinner. "Own tool": the TCP is given as for the KUKA / the Robots plug-in (Z from the tip back into the tool); from 2.0.5 it is turned so Z points out of the tool, as URScript wants it (it was reversed before). The same holds for the AE20 on a UR |
| `Robot Cell` | Robot, tool and where the work lies. From 2.0.5 it warns which inputs a UR ignores |
| `Robot Program` | Writes URScript at the output `Code` (called `KRL` up to 2.0.4): set_tcp and set_payload, the start code (the FH start code first asks for compressed air), the extruder on and off where printing starts and stops (digital outputs 0 and 1 as on the FH UR5; own code possible), `movel` along the path at the speed of the path, blended over `Blend` (blends never overlap), and the approach. From 2.0.5 the approach is a `movej([...])` to the solved joint angles instead of `get_inverse_kin`, so the robot moves in the configuration that was simulated. Rows: `Save script` (a `.script` file), `Upload to robot` and the `Robot IP` in the menu |
| `Robot Simulation` | The arm with its tool and the path printed so far, by slider or Play |

## Upload to robot

`Upload to robot` sends the program to the robot (port 30002). From 2.0.5 it sends only a program that is
OK (the hidden `UR Program` of old files now sends once when its `Upload` turns true). The robot runs the script at once; an e-Series only in remote
control. Stand clear of the arm and have the emergency stop at hand before you switch it on.

## What is checked, and what is not

Checked: reach of every point from the start pose (its own solver: the UR wrist is not spherical), the
payload, and from 2.0.5 the wrist singularity (axis 5 near 0) and the joint speed against the limits of
the model. When one fails, the Report says NOT RUNNABLE or TOO FAST.

Not checked on a UR: collisions, external axes, the work base. The Report says so. Run every new program
first at low speed (and best with the pen holder) before you print.

`UR Cell`, `UR Program` and `UR Simulation` of 2.0.3 are hidden; old files still open with them.
