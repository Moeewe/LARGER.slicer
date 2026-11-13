# Decisions

Key decisions behind LARGER.slicer and why they were made, so they are not undone by accident.
Newest context first within each section. Part of the [developer handbook](README.md).

---

## Printing

**Non-planar bead height: the shell law, not the law of the 2024 head (1.3.1).** 1.3.0 took over the
extrusion law of UNIKAT HEAD V2 (t = dz + (h - dz) dxy / w) because the print "worked". The owner then
found that V2 over-extruded at the overhangs. With a vertical nozzle a shell one bead wide gains
w x dz per lap however far the lap leans - a sideways offset moves material, it does not add any - and
where the laps step mostly sideways the shell becomes a skin of the sideways step x a skin thickness.
So t = max(dz, dxy x t_skin / w), t_skin = `Skin Thickness` (default the layer height, at least
0.4 x layer height). On V2's own points this is 3.4 % less material over the head and up to 57 % less
per lap towards the crown ([PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) 9.3). V2's law is not
offered as an option: an old print is reproduced by its own G-code, and keeping a known
over-extrusion selectable invites using it. It stays in the code (`BeadLaw.HeightV2`) only so that
Non-Planar Check can recognise and name it in old files and imported G-code.
Lesson: a reference print tells what is printable, not that every number in it is right; a law needs
a physical derivation before it becomes the default.

**Checks: allowances only where the physics supports them (1.3.1).** 1.3.0 had loosened the checks for
law-following paths "because V2 printed it". Re-examined against the shell law: no bead above 0.8 w
is accepted any more (those heights came from V2's law itself); E is judged against the shell law
(same tolerance as before), and E that matches V2's law is reported with its excess; below 0.4 h up
to 1 % stays for the last crown rings, and points where the skin term sets the height are not judged
by that rule; the support allowance (up to one bead width beside the lap below at up to 2.5 % of the
points) stays only for stretches of at most 12 bead widths that hang between supported ones like a
short bridge - V2 itself never needed it (its laps lay at most 1.95 mm beside the lap below in plan;
the 4 mm read before came from splitting its G-code into layers by turns); nozzle grazing up to
0.6 mm stays (geometry, not extrusion).

**One continuous line, and never crossing beats the requested pattern.** Pellet extruders cannot
stop cleanly; travel moves string and stops leave blobs; a crossing ploughs through fresh material.
So walls, fill and infill are one line, and where a pattern cannot be printed without a crossing the
slicer chooses another (zigzag falls back to contour fill with a remark, crossing sketch lines are
dropped). `Path Check` has a crossings test.

**Bead width drives all generated geometry.** Free spacing values produced lines that did not fuse.
Every generator takes its distances from `BeadWidth` / `BeadSpacing`; the house standard 3 mm
nozzle / 5 mm bead / 2 mm layer is the default.

**Flat layers with one fixed seam ramp; spiral only for single walls.** Continuously climbing paths
"destroy the right layers" (owner). Layers stay flat and change over a short incline at one fixed
place; no drifting seam, no second pass over the seam. Spiral (vase) mode is only used where every
layer is exactly one closed wall.

**Constant speed: F constant per layer.** The owner wants constant print speed. The `Feedrate
Calculator` no longer scales F by segment length; F only drops for a whole layer (Min Layer Time,
Short Lap Time) or a move (Max Volume Rate), never below Min Speed. F0 is never written.

**Skirt first, exit move last.** The extruder gets up to pressure on the skirt, not on the part,
and the drop when it stops lands beside the part.

**Staggered brim.** The 2024 head's brim links all lay at the same angle and stacked into
overlapping "chevrons" (6.2 mm² each, 121 mm² in the first layer). Now each brim loop ends half a bead
further on than the one inside it and every ring ends one bead before its own start; first-layer
double coverage dropped to a few mm² ([PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) 9.6).

**Short Lap Time from the printed head.** Its definition gave short laps (neck, crown) 40 s and long
laps 35 s, switching at min + 0.7464 x (max - min) of the lap lengths over 5 laps. The rule was taken
over literally (`PathEngine.LayerTargets`), including the constant, because it is proven on a print.

**"Follow the surface" uses its own field, not a copy of V2's rings.** V2's laps came from a loft of
the definition's own curves and are not iso-lines of any simple field (several were tested). The
surface field (geodesic ratio, blended towards Z on the wall) was chosen for low head grazing. (Up to
1.3.0 the extrusion followed V2's bead law exactly; since 1.3.1 it follows the shell law, see above.)

**Extrusion factors as numbers, not code.** Ginger 0.21 and Weber 1.128 are the GCODE MAKER factors
of the FH Muenster definition at 100 % flow (w x h x length). A new machine is a `MachineProfile` or a
`.machine` text file, a cell or machine model a Rhino file with named layers: nobody has to program
to add a machine.

## Slicing technique

**Non-planar and multi-axis by deformation.** The model is mapped into a "flat" space where the
wanted layers are planes (`Warp`: guide surface, guide curve, follow the part, follow the surface),
sliced there with the unchanged pipeline (walls, fill, ribs, spiral, seam), and the finished path is
mapped back with the local layer height going into the extrusion. One pipeline serves every slicing
mode.

**Mesh once, cut with an own plane cutter.** Contouring NURBS per layer was far too slow; every
input type is meshed once into a welded mesh and every triangle is assigned once to the layers it
crosses. Very fast slicing is a core requirement (with a Preview mode that skips rounding).

**The core avoids native RhinoCommon calls and Grasshopper types.** `Geo.Closed`, `Geo.PlaneAt` and
friends replace calls that go into Rhino's native library (slow in tight loops, impossible outside
Rhino). This keeps the core fast and testable in a console bench, and lets Grasshopper 2 compile the
same files with a one-line shim.

**Invariant culture everywhere.** Every number in reports and machine files goes through `Inv`, so a
German or French Windows never writes `2,5` into G-code.

## User interface

**Settings as rows on the component.** "Apple-level" usability: drop-down choices instead of magic
numbers, clear labels, plain messages. First the components placed value lists and sliders beside
themselves; now they draw rows under themselves (`OptionRows.cs`). The value goes into the input's own
persistent data, so the file keeps it and a wire still wins. Number rows are dragged, typed or picked
from a few round values instead of opening long lists.

**Speeds in mm/s, with legacy reading.** People think of a moving nozzle in mm/s; G-code and DXR
want mm/min. Inputs are mm/s, the path keeps mm/min (50 mm/s = F3000). Files written since carry the
mark `SpeedMmS`; files without it are read as mm/min with a note, so old definitions print the same.

**Long calculations in the background (GH1).** Slicing a large part blocked the canvas. Eight
components calculate on a worker task (`AsyncSolve`): inputs are read and geometry copied on the UI
thread, the work never touches Grasshopper, a changed input throws the old result away, the finished
result is handed out in a short self-expired solution. On by default, switchable per component and
saved in the file. Grasshopper 2 already solves beside the canvas, so there the components only report
progress (`Io.Step`) and pass the solution's cancel token into the core (`Io.Cancellable`,
`Toolpaths/Cancel.cs`), as GH1's background tasks do: a changed input stops the core loops within
about a layer.

**No separate curves panel.** For a while the curve tools of the former Toolpaths tab had their own
panel ("Slicer · Curves"). They went back into the Slicer tab as its seventh group, holding only
Layers to Curves, Curves to Layers and the `Curve ...` components; the rarely used Align Curves,
Alternate Curves and Join Open Contours are in that group but hidden (found by search). One tab,
ordered from Start to Output, is easier to learn.

**Old definitions must keep opening.** Released components keep names, inputs, outputs and GUIDs.
The old Toolpaths components run on the new fill engine. Grasshopper 2 keeps two replaced components
(`Infill`, `Round and Lead`) hidden so its first files still load.

**Quick Start on one bed, with wireless links.** All examples stand side by side on one 1000 x 1000
mm bed (they were checked not to touch), titled like the FH Muenster definition, with relays fed by
hidden wires instead of wires across the canvas.

**One icon style, no ground.** Until 1.2 icons sat on a dark chip; now they are drawn like
Grasshopper's own (light body, accent colour, dark edge, faint shadow) so one file reads on every
ground. One deterministic script makes all of them, the 48 px Grasshopper 2 set included ([ICONS.md](ICONS.md)).

## Project and repository

**One core, two plug-ins.** Grasshopper 2 compiles `LARGERslicer/Toolpaths/*.cs` directly instead of a
copy, so a fix lands in both. GH1-only canvas helpers and one-off tools are not ported
([LARGERslicer.GH2/README.md](../LARGERslicer.GH2/README.md) lists them with the reason).

**Own robot maths, Robots library format.** Robot kinematics, external axes and collisions are in
the core (`Robot.cs`, `ExternalAxes.cs`, `Collision.cs`); robot arms and their meshes are read from the
library format of the Robots plug-in (.xml + .3dm), so existing libraries work without a runtime
dependency on that plug-in.

**Manufacturer manuals removed from the repository.** The Ginger manual PDF and the HP-GL manuals
are copyrighted by their makers. They were deleted from the repository (commit "Remove copyrighted
manufacturer manuals", November 2026), are kept only locally and are listed in `.gitignore`. The
guides written at the FH Muenster stay.

**Three Yak packages from staging folders.** `yak build` packs the whole folder and tags a package
by the newest Grasshopper it references; packages built in the project folder contained sources,
`obj/` and PDFs, and a Rhino 9 build in a Rhino 8 package would be mis-tagged. Each package is staged
on its own (rh9, rh8, rh7). Publishing stays a manual step of the owner.

**Test in an own Rhino, never in the owner's.** The owner works in Rhino 8 while tests run; tests
use a separate Rhino 9 slot and their own documents ([TESTING.md](TESTING.md)).

**The stand-alone app shares the rules, not the code.** LARGER.app (private) is a separate
implementation; it shares presets and printing rules with the plug-in, and rules move between them
in both directions (machine kinds, Short Lap Time, crown closure).
