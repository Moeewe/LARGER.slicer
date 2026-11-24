# The UR5 in LARGER.slicer – how it can be connected

Status (November 2026): research, nothing built. The UR5 of the lab runs today with its own definition
`EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/UR5slicer.gh` (Robots plug-in by visose, MeshEdit; clay and pen
tools; not tested with LARGER.slicer 2.0).

## Why the robot components of LARGER.slicer do not take it yet

`Robot` / `Robot Program` solve arms with a **spherical wrist**: the last three axes meet in one point
(KUKA, ABB, Fanuc, Stäubli). The UR5 has an **offset wrist** (d4, d5, d6 ≠ 0), so the code refuses it
(`ArmSolver`: "This arm has no spherical wrist"). Its inverse kinematics has its own closed form with up to
eight solutions per pose (shoulder left/right, elbow up/down, wrist flipped), published by Universal Robots
and widely used.

UR5 (CB3) Denavit–Hartenberg values from Universal Robots, in mm: d1 89.159, a2 −425, a3 −392.25,
d4 109.15, d5 94.65, d6 82.3. The UR5e differs slightly (d1 162.5, a3 −392.2, d4 133.3, d5 99.7, d6 99.6).
Axis ranges ±360° on all joints.

## What would have to be built

1. **Kinematics:** a second solver for UR arms (offset wrist), chosen by the arm's library entry; the same
   checks as for the KUKA (reach, joint ranges, joint speed, configuration changes, singularity near the
   wrist and the shoulder, collisions with cell and part). Robot Simulation and Cell Geometry then work as
   they do now.
2. **Program output: URScript** instead of KRL/DXR. One `movel(p[x, y, z, rx, ry, rz], a=…, v=…, r=…)` per
   point: position in metres in the base frame, orientation as a rotation vector (axis × angle), speed `v`
   in m/s from the print speed, blend radius `r` for a smooth path (a few mm). Extrusion as an output on
   every move or at start and stop: `set_standard_analog_out(0, …)` for a variable speed, or
   `set_standard_digital_out(n, True/False)` for on/off – depends on how the clay extruder is wired
   (question below).
3. **Sending the program:** as a `.script` file the pendant runs (USB stick or the robot's file system), or
   over the network: the robot accepts a URScript program as text on port 30002 (secondary interface);
   the e-series only in Remote Control mode. The dashboard server (port 29999) can load and start saved
   programs. The Robots plug-in already sends over the network; the same can be done from a component with
   a Send button and a check of the robot's state.
4. **Machine file:** a robot entry like the KR120 (arm, tool TCP, base plane on the table, cell), so the UR5
   is one more choice in `Robot`, not a separate definition.

Rough size: kinematics and checks 2–3 days, URScript writer and Send 1–2 days, tests on the real arm
(first with a pen at the slowest speed) 1 day.

## Questions for the lab

- Which UR5 exactly: CB3 (UR5) or e-series (UR5e)? Which software version (PolyScope)?
- How is the clay extruder switched: digital output, analog output, tool I/O, its own controller?
- Which tool centre point and weight do the clay extruder and the pen holder have?
- Is sending over the network wanted, or only a file that is started on the pendant?

## To fix now

The public README of the UR5 folder contains the access passwords of the robot pendant. They should be
removed from the README (and changed on the robot, because they are in the git history of the public
repository).
