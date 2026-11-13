# GingerOne.3dm: the Ginger One as a 3D model that can move

The Ginger One (Ginger Additive, ginger.one v1.3 pellet printer) as one shared asset, used by the
LARGER.slicer Grasshopper plug-in (Machine, Machine G-code, Printer Simulation) and by LARGER.app.

## File

- Meshes only, millimetres, about 26,000 triangles. One mesh per colour per layer. Each mesh carries
  its colour as its **object colour**: red printed parts, aluminium, black motors, brass heater bands,
  steel barrel and nozzle.
- Also available as `GingerOne.obj` + `GingerOne.mtl`: one group per layer, one material per colour.

## Coordinates

- **Origin**: the front-left corner of the 1000 x 1000 mm print area, on the bed surface.
  This is where the G-code counts from (X0 Y0 Z0).
- **X** to the right, **Y** to the back (away from the control panel), **Z** up.
- Bed: exactly X 0..1000, Y 0..1000, top at Z 0 (an 8 mm plate below it).
- Frame: 1500 x 1500 x 1500 mm outside the corner nodes (X -250..1250, Y -250..1250, floor at
  Z -60). The control panel stands out in front (to Y -500, so the machine is 1750 deep) and above
  (to Z 1590, 1650 above the floor).

## Layers (moving groups)

| Layer   | Moves with  | What is on it |
|---------|-------------|---------------|
| `Frame` | nothing     | posts, top and floor bars, red corner nodes, diagonal steel cables, Z lead screws with their motors and bearing blocks, control panel, electrical box |
| `Bed`   | nothing     | bed plate and its spacers |
| `Z`     | Z           | gantry frame on the four Z axes, its carriages and lead-screw nuts, Y rails, Y motors and belts |
| `Y`     | Y and Z     | cross beam, its carriages, X motor, belt, drag chain |
| `X`     | X, Y and Z  | the extrusion head: carriage plate, mount, principal motor, hopper, feeding auger with its motor, barrel with three heater bands, nozzle, cooling fans |
| `TCP`   | (X, Y, Z)   | one point object: the nozzle tip |

## Reference pose

The model is drawn with the nozzle tip at **(x0, y0, z0) = (500, 500, 500)**: head in the middle,
gantry at mid height. The `TCP` point lies there. (This is the pose of the drawing, not the
machine's G28 home.)

## How to pose it

For the nozzle tip at the path point (x, y, z), translate each group (no rotations):

- `Frame`, `Bed`: stay.
- `Z`: by (0, 0, z - z0)
- `Y`: by (0, y - y0, z - z0)
- `X`: by (x - x0, y - y0, z - z0)

The tip then lies exactly on (x, y, z). Travel: X 0..1000, Y 0..1000, Z 0..1000.

## Sources

- Size drawing "ginger.one v1.3" (Ginger Additive, 2023): 1500 x 1500 x 1500 frame, 1650 high and
  1750 deep with the control panel.
- Cura machine definition of the Ginger One: build volume 1000 x 1000 x 1000.
- Ginger operating manual, figures 2, 4 and 5 (layout of the frame, the head and its parts), compared
  by eye; nothing of the manual is part of this file.
- Real printed parts of the machine (STL, reduced): corner nodes S01, lead-screw nut holders S05,
  Z motor mounts S13, Y carriage plates M02, X carriage plate M05, head mount E02, hopper E11.
  Everything else is simplified geometry (40 x 40 tubes, boxes, cylinders).

## Your own machine

Draw it the same way (layers Frame, Bed, Z, Y, X and a point on TCP, in the machine's coordinates)
and save it as a .3dm next to your .machine file: Printer Simulation then moves it too.
