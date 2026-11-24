# Structure: Weave and Reshape

Two example files for prints that carry more with the same material. Both work with LARGER.slicer alone (Grasshopper 1, Rhino 8). The texts on the canvas are German.

| File | What it shows |
|---|---|
| `Weave (beads that interlock).gh` | Two walls side by side do not only fuse: their beads grip into each other from layer to layer. |
| `Reshape (print height where it is needed).gh` | The same layers and the same material, but the part is higher where it is needed and lower where it is not: non-planar on a gantry or with a tilted tool on the robot. |

---

## Weave

1. **Part**: connect your own geometry to *Teil*. Walls > Position "centred" if your surfaces are mid-surfaces.
2. **Continuous Path** stays on "Layered". On a spiral the neighbouring beads lie at different heights and cannot interlock.
3. **Weave Zones** shows where beads lie side by side. Tolerances: *Closer*, *Touch*, *Further* (share of the bead width). By hand: *Include*, *Exclude*.
4. **Weave**: choose the pattern.
   - *Shift*: the beads move in and out against each other.
   - *Width*: only the inner bead gets wider and narrower, the outer side stays smooth.
   - *Snake*, *Teeth*, *Custom*.
   Amplitude and wavelength 0 = automatic. The shortest wave the bead can follow is kept.
5. **Weave Variants** compares 14 patterns on the same part in one table.
6. **Weave Test** makes the test series (195 specimens with a record), **Joint Data** gives the joints as couplings with a stiffness for an IGA shell analysis.
7. **Start and End**, **Path Check**, **Ginger G-code** (Machine Profile "Ginger One" is connected to Print Settings).

## Reshape

1. **Part** lying, printed as a vase. The print height is the depth of the part.
2. **Shape Field**: where more, where less. Here a profile along a line: kept on the left, free on the right. From an analysis: mode "Utilisation at points" with the points and values of *Result Field*.
3. **Reshape Path** reshapes the finished path. Skirt and the way into the part stay as they are, the extrusion is worked out again from the distance to the bead below. *Material* 100 = the same amount. Eight variants: fanned with tilted or vertical tool, fanned and wider, only wider, extra layers, flat with shortened layers.
4. **Multi-Axis Check**: does the head stay free? Tilt, turning speed, layer heights. **Path Check** as always.
5. **Robot** (cell of the FH Münster, KR120 with AE20 extruder): reach, axes, program for the DXR.
6. **Reshape Code**: the same for a finished G-code or `.dxr` file.
7. **Compare Variants**: eight ways side by side in one table.
8. **Slice again**: the bent part with its new top as a guide surface.

The warning of *Reshape Path* in this file is on purpose: the limits (layer height 0.8 to 2.5 mm, slope up to 30 degrees) cut the wanted shape, and the report says by how much.

Strengths and the stiffness of the joints are assumptions until they are measured on test pieces.
