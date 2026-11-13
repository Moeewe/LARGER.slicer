# Printing principles

The rules LARGER.slicer enforces, why, and where in the code. Most of them come from printing on
the Weber DXR25 (KUKA robot) and the Ginger One at the FH Muenster, and from the printed reference
head of 2024 (section 9). Part of the [developer handbook](README.md).

Every new generator, check or output must keep these rules. When a rule and a requested pattern
conflict, the rule wins and the component says what it did instead.

---

## 1. One continuous line that never crosses itself

A pellet extruder cannot stop cleanly: every travel move pulls strings and every stop leaves a
blob. So the whole print, from skirt to exit, is **one path**.

- Walls, bottom fill and infill are joined into one line per layer: infill is spliced into the wall
  (a rib is a hairpin out of the wall and back), not printed beside it (`FillEngine.Ribs`,
  `CellFill`, `FermatFill`).
- Contours that lie inside each other (inner shell, the wall of an opening) are joined by a
  **bridge** of two lines one bead spacing apart, at the same place on every layer, so the bridges
  stack into a rib (`PathEngine.Build`, `bridge = true`; the Continuous-Toolpath Connection Rule,
  Wesseler, Herrlein, Kiendl). Bridges go where both walls run on without a corner for 1.5 bead
  spacings to either side.
- **Never crossing beats the requested pattern.** A zigzag that would cross falls back to contour
  fill; sketched rib lines that cross are dropped (`WithoutCrossingSketches`); the old curve tools
  run on the same fill engine and follow the same rule. `Path Check` reports every crossing within a
  layer (moves that run within about 10° of each other, or one above the other, are not crossings).
- A move with **F0** stops the machine and is never written; `DXR File Health Check` flags it.

## 2. Bead width drives the geometry

Everything the slicer generates itself (wall offsets, infill, ribs, bottom fill, support fins,
brim, patterns) is derived from the bead width, so neighbouring lines fuse:

- `PrintSettings.BeadSpacing = BeadWidth x (1 - Overlap)` (Overlap 0.15 by default, clamped 0..0.9)
  is the centre distance of two beads side by side.
- Walls: the first wall lies half a bead inside the geometry (Position "inside"), on it
  ("centred") or half a bead outside; further walls one bead spacing in.
- Rib tips stop one bead spacing short of the opposite wall; fill lines end one bead spacing from
  the wall; cell loops lie half a bead spacing inside each cell.
- Spacing inputs that count in bead widths say so (e.g. Support Fins).
- House standard: **3 mm flat nozzle, 5 mm bead, 2 mm layer** (the defaults of `PrintSettings`).

## 3. Every bead rests on the bead below: at most ½ bead offset

The path is the centre of the bead, and the bead reaches half its width to either side. A point of
a new layer may therefore lie at most **½ bead width** beside the path of the layer below (8 mm
bead: 4 mm). The first layer lies on the bed and is not checked.

- `LeadEngine.Check` (Path Check): per point the sideways distance to the layer below, in flat space
  for non-planar paths. Results: "Support OK"; "OK with a note" (max offset ≤ 1.75 x limit and ≤ 1 %
  of the points over, or no layer with many hanging points); otherwise NOT SUPPORTED with the
  overhang angle, `atan(limit / layer height)` (5 mm bead, 2 mm layer = 51°). Points more than
  1.5 x the limit are output.
- Infill on top of a solid bottom layer counts as supported; tilted layers that touch the bed along
  their lowest edge too.
- For laps that lean on the lap below with the bead law (Follow the surface, section 9) offsets up to
  one bead width at up to 2.5 % of the points are accepted ("Support OK for leaning laps";
  calibrated on the 2024 head: 1.8 %, max 4.0 mm).
- `SelfSupport` uses the same rule to check a part before slicing, to grow the smallest
  self-supporting envelope and to find the best slice angle.
- Every generator (ribs, spiral blending, rounding, patterns) must respect it.

Path Check also refuses NaN positions, a first layer on or into the bed (the nozzle must run at
least half a bead height above Z 0) and reports zero-length moves.

## 4. Speed: constant F per layer, slowed down only by layer time and volume

Speeds are entered in **mm/s** and kept in **mm/min** (the F of G-code and DXR): 50 mm/s = F3000.

- **F is constant within a layer.** No scaling by segment length (the old Feedrate Calculator did;
  the owner wants constant speed). `PathEngine.Process`:
  - print speed = `Speed`, on the first layer `FirstLayerSpeed` if set (adhesion; the 2024 head:
    25 mm/s = F1500);
  - **Min Layer Time**: a layer that would be done sooner is printed slower,
    `F_layer = min(F, printed length x 60 / (MinLayerTime - travel time))`, never below Min Speed. The
    first layer is slowed from its own First Layer Speed, and skirt and brim count for the time of
    the first layer (they are printed right before it);
  - **Short Lap Time** (rule of the 2024 head): laps shorter than `min + 0.7464 x (max - min)` of all
    lap lengths get Short Lap Time, longer ones Min Layer Time, changing linearly over 5 laps; the
    first layer counts by its length but does not set min and max (`PathEngine.LayerTargets`);
  - **Max Volume Rate** (mm³/s): no move asks the extruder for more,
    `F = min(F_layer, MaxVolumeRate x 60 / (bead section x Flow))`, never below Min Speed (the
    Ginger gives about 330 mm³/s).
- Moves without extrusion run at Max Speed, except moves that go down towards the part (the way in):
  these run at print speed, so a wrong height is seen before the nozzle hits.
- Optional `compensateWithSpeed`: the extruder keeps its nominal output and the speed follows the
  local layer height instead (for slow-reacting pellet extruders).
- Points closer than `max(0.5, 0.2 x bead)` mm to the one before are dropped (`LeadEngine.Thin`):
  a controller slows down on hundredths-of-a-millimetre moves and the extruder leaves a blob.
  Points that carry a change (role, flow, layer, tool axis) or a corner (> 0.02 mm off) are kept.

## 5. Nozzle presets, layer heights, design rules

`Toolpaths/Nozzles.cs`, `DesignRules.cs`:

| Source | Rule |
|---|---|
| Weber DXR profiles (Cura, Weber start package) | nozzle 1 / 2 / 3 / 4 / 5 / 6 / 8 mm → bead 1.25 / 2.5 / 3.6 / 4.8 / 6 / 7.2 / 10 mm, layer 0.5 / 1 / 1.25 / 1.8 / 2 / 2.4 / 3.2 mm (about 1.2 x and 0.4 x the nozzle) |
| Weber quick guide | bead 0-50 % (about 20 %) wider than the nozzle, layer 25-50 % of the nozzle; pointed nozzle bead 0.7-1.3 x, flat nozzle 1.0-1.8 x |
| FH Muenster, tested on the DXR | 3 mm flat nozzle, 5 mm bead, 2 mm layer; "Wide" presets scale it (bead 5/3 x, layer 2/3 x the nozzle) and say "print a test wall first" |
| `Nozzles.Check` | warns when the layer is > 80 % of the nozzle (bead hardly pressed) or < 25 % (extruder pushes hard) |
| CEAD 45° sheets | layer height N/4..N/2, overlap 5-10 % of the layer width; wall of two beads CO90 = 2 LW - OL, at 45° CO45 = ½√2 (2 LW - OL); finish thickness LW45 = √2 t_finish (with milling √2 (t_finish - t_milling)) |
| Ginger handbook | layer ≤ 0.6 N, LW ≥ 1.5 N, LW / LH ≥ 2, corner radius ≥ 0.5 N, double bead 0.8-0.9 LW |

## 6. Start, seam and end of a print

- **Skirt first.** A skirt (default 1 loop, 3 bead widths from the part) is always printed first;
  the extruder builds up pressure there, and the bead runs on into the outer wall without stopping
  (`LeadEngine.StartEnd`). The way in comes from above (`Approach`, 30 mm).
- **Brim** (outside, inside or both): loops right against the first layer. Every ring ends one bead
  before its own start, the link to the next ring is square to both, and **each loop ends half a bead
  further on than the one inside it** (a staircase). This replaced aligned links that stacked into
  "chevrons" (section 9.6). The inner brim leaves a lane of two bead spacings for the way in.
- **Flat layers, one fixed seam.** In Layered mode every layer is flat and ends one Seam Gap
  (default one bead width) before its own start; the path climbs to the next layer on a short incline
  across that gap, at the same place on every layer. No drifting seam, no second pass over the seam.
  The first wall and the first flat spiral loop leave the same one-bead gap.
- **Seam position** (`SeamMode`): Fixed (a seam point, or where the first layer starts; default),
  Inner corner (a concave corner of the outer wall: least visible, and milling does not run over
  it), Sharpest corner.
- **Spiral (vase) mode** only for layers that are exactly one closed wall without infill; layers
  with ribs, cells or a bottom are always printed flat. The flat first loop stops one bead before its
  start, the first climbing loop rises from nothing over that gap; the flat closing pass is left out
  on rings smaller than three bead widths (a crown).
- **Corners stay where the wall is** (`Round Corners` rounds in the plane of the layer and keeps
  ramps separate; thinning keeps corner points).
- **Exit move.** After the last point the nozzle moves sideways off the part (default 50 mm) and
  lifts (20 mm) without extrusion, so the drop that forms when the extruder stops does not end up on
  the part. An own exit curve can replace it.
- **Start and end layers for angled printing** (Slice input `Start/End Layers`, `StartEndFeature`):
  sacrificial layers in front of and after the part (CEAD: about 5), milled away later. The start
  feature stands on the bed and gives the first leaning layer a full layer to lie on; flat layers get
  only the end feature.

## 7. Extrusion: E conventions per machine

The path carries width, height and flow per point; `MachineProfile` turns them into the number the
machine wants: `E = section x length x Flow x Factor` (x First Layer Factor on the first layer),
divided by the filament area when a filament diameter is set. Section = w x h (`RectangleSection`,
as the hand-made scripts) or the squashed-bead area.

| Machine | What E is | Profile |
|---|---|---|
| Ginger One (Marlin), `GIN` | relative E (M83) = 0.21 x w x h x length, at 100 % flow | factor 0.21, rectangle (factor of the GCODE MAKER in the FH Muenster definition) |
| Ginger One (Klipper), `GIK` | volume in mm³ x 1.0 (as the Klipper script did); confirm with the Klipper `rotation_distance` | factor 1.0, rectangle |
| Weber DXR25 on a KUKA robot | `G91 XE=[value*P1] G90` per move: relative extrusion, P1 = the material factor set on the controller | factor 1.128 (an old Cura value), rectangle, unit "x P1" |
| Generic pellet extruder | volume in mm³ | factor 1 |
| Filament printers (1.75 / 2.85 mm), Desktop Test Print | filament length | filament diameter |

**Ginger: 0.21 versus the handbook's 2.5 mm "filament diameter simulation".** The Ginger handbook
(`README GINGER 00 QUICK START GUIDE.md`, "Slicer E-Value Multipliers") drives the pellet extruder
from an ordinary filament slicer by pretending a filament diameter, 2.5 mm for virgin PLA (2.9-3.4
for other materials). Then E = V / (π/4 x 2.5²) = 0.204 x V. The GCODE MAKER of the FH Muenster
definition, and the plug-in, write E = 0.21 x w x h x L directly. Both describe the same calibration
within 3 %; per material the extrusion is tuned with `Material Factor` (new factor = target width /
measured width x current factor).

Other machine conventions:

- Ginger One start/end: the sequences of the Ginger quick start guide (pins M42 P49/P57, mixer
  `M163 S0 P1`, `M163 S1 P0.07`, `M164`, `M0 Click if EXTR. READY`, G28, `G92 E0`, `M83`), fan
  `M106 S153` (60 %) after layer 4 (handbook: no cooling on layers 1-4).
- The Weber DXR expects a vertical extruder as A 0, B 0, C 0.
- File names carry machine, date and time, part, weight and print time:
  `DXR_2611051216_Test Object_2668g_53min.dxr` (weight from `PrintSettings.Density`, 1.25 g/cm³ by default).
- Machine kinds (`Machines.cs`, as in LARGER.app): gantry, bed slinger and delta print flat and
  non-planar layers with a vertical nozzle; a belt printer prints only at its belt angle; only a robot
  can tilt the tool (angled and multi-axis).

## 8. Surface patterns

`Surface Pattern` (`Toolpaths/SurfacePattern.cs`) sits between Continuous Path and Start and End.
Kinds: Knit (pixels, rows alternate), Ribs, Waves (drift with Phase Shift), Zigzag, Dots, Loops
(long pulls that hang; with Droop and Extra Flow the bead sags into loops), Image (a grey picture
wrapped around the part). Rules:

- Every layer is resampled to the pattern pitch and its **wall points** are pulled out along the
  wall normal (outside or, with Side, inside). Seam ramps, bridges, ribs and infill stay where they
  are; the first layer stays plain.
- Around closed walls the pitch is fitted to a whole number of repeats and followed from layer to
  layer; the pattern fades out at both ends of every wall so it meets the seam smoothly.
- A pull that would cross another part of the path is reduced until nothing crosses: **the path
  stays one line**.
- The ½-bead rule is broken on purpose for pixels and loops: such points are output as `Hanging`.
  An amplitude of half a bead (Quick Start 08: 2.5 mm on a 5 mm bead) keeps every bead on the one
  below.
- Regions (closed curves unrolled with the wall, Breps, meshes) with Falloff, Attractors with Reach,
  Invert.
- On "Follow the surface" paths the bead-law heights are measured again after the pattern.

---

## 9. What the reference print taught: UNIKAT HEAD V2 (Ginger One, 2024)

The head "UNIKAT HEAD V2" was printed on the Ginger One (Marlin) in July 2024 from the owner's
Grasshopper definition "BE GINGER SLICER (Non Planar Head) V1.1" and printed well. Its G-code
(18 882 lines, 18 771 G1 moves) was analysed in November 2026 and the plug-in was rebuilt and
calibrated against it. The G-code itself is not in the repository.

### 9.1 Units and totals

- `M83` relative E; **E = 0.21 x volume**: E/mm = 0.21 x w x h with w = 4 mm bead.
- Flow 100 %, travel F10000; max volume rate slider 330 mm³/s, never binding (highest real rate about 73 mm³/s).
- E total 48 353 (about 230 cm³ of bead), about 81 min (first layer 1.75 min, head 76 min).

### 9.2 Sequence

| Moves | What | Z | F | E/mm |
|---|---|---|---|---|
| start code | M42 pins, mixer M163/M164, G28, beeps, `M0 Click if EXTR. READY`, G28, M420, G90, Z+10, G92 E0, M83 | | | |
| 3-589 | **first layer: 8 brim rings**, 4 mm apart, outside in, 2627 mm | 0.9 | 1500 (25 mm/s) | 0.756 = 0.21 x 4 x 0.9 |
| 590 | travel 4.3 mm up to Z 2.0, no retract | | 10000 | 0 |
| 591-719 | **lap 0: one flat ring** on the innermost brim ring (the foot) | 2.0 | 355 (layer time 40 s) | 0.924 = 0.21 x 4 x 1.1 |
| 720-18761 | **head spiral, 120 laps**, one continuous non-planar line | 2.016 → 178.0 | 11.5-665, constant per lap | ramps in from 0.013, then 0.6-1.66 |
| 18762-18767 | tiny closing loop on the crown | 178.0 | 231 | 0.12 |
| | fan `M106 S140` at Z about 39 (lap 20), `M107` before the last crown move; end: travel off, Z +30, home X | | | |

The first layer is a brim, there is no bottom fill: the head is hollow (vase). The innermost brim
ring is the foot of the head; lap 0 lies exactly on it. The spiral starts at lap 0's start point and
its E is computed from the distance to lap 0, so it **ramps in from almost 0 over the whole first
lap** (Z 2.0 → 3.7): the controlled transition that bonds the head to its foot.

### 9.3 The extrusion law (bead height from the lap below)

From the definition's "layer heights" cluster, confirmed on all 18 041 head points (rms 0.0006 E/mm):

```
h  = 3D distance from the point to the closest point on the previous lap
a  = angle of that connecting line to the horizontal (90° = straight below)
t  = h (1 - (h/w)(1 - sin a)((w/h) - cos a))
   = dz + (h - dz) · dxy / w            (dz, dxy = vertical and horizontal part of h)
E/mm = 0.21 · w · t                      (w = 4)
```

Stacked straight, t is the vertical gap; where the lap below lies to the side (overhang, crown) only
the share dxy / w of the slant distance counts, so a leaning bead is not overfilled. At the crown
(a ≈ 8°, dz 0.26, dxy 1.8) t ≈ 0.98 mm instead of 1.84 (3D distance) or 0.26 (vertical gap).
Alternatives fit much worse: pure vertical gap has a 95th-percentile error of 37 %, plain 3D distance
24 %. Per-lap mean E/mm: 1.53 (vertical neck) → 1.28 (chin overhang) → 1.40-1.51 (skull) → 0.80
(crown).

In the code: `BeadLaw.Height(p, q, w)` in `FollowSurface.cs`; `BeadLaw.Apply` sets every point's
height from the closest point of the lap about one lap back in the path (0.75-2 laps). Used by Guide
Mode "Follow the surface" (`Warp.UseBeadLaw`) and re-applied after Surface Pattern; Non-Planar Check
measures heights by the law when E follows it better than the vertical gap (also for imported G-code).

### 9.4 Laps and lap times

- Laps are V iso-curves of the lofted head surface (121 of them), smoothed (0.5 x 30), resampled to
  about 1.9 mm point spacing, seams aligned (always on the +Y side), then spiralized: every point of
  lap k moves towards the next lap by a fraction rising 0 → 1 along the lap.
- Lap starts every 1.70-1.75 mm in Z at the seam; Z span within a lap median 10.8 mm, max 18.7 mm
  (chin and jaw against the back of the head); lap length 238 mm (neck) → 390 mm (widest) → 9.6 mm.
- **Speed = layer time per lap**: F = 60 x lap length / T, **T = 40 s for laps shorter than
  min + 0.7464 x (max - min) of all lap lengths (293.5 mm), 35 s for longer laps**, with a 5-lap linear
  change between them. So F runs from 336 mm/min (neck) to 665 (widest) down to 11.5 (crown): very slow
  crown laps are cooling time for tiny rings. The first layer at F1500 is the only fixed speed.
- This is `Short Lap Time` (40 s) and `Min Layer Time` (35 s) in the plug-in (section 4).

### 9.5 Crown closure

The laps shrink r 18 → 9 → 1.5 mm with about 1.8 mm sideways and 0.25 mm vertical step per lap: the
crown is closed by a nearly flat spiral whose beads overlap sideways by about 55 % with E for
t ≈ 0.95 mm, effectively a 2 mm top skin. F drops to 11.5 mm/min (T stays ≥ 40 s). In the plug-in, on
a part closed at the top the last lap runs from the last ring into its middle at half a layer height,
closing the crown; loops down to 1.5 bead widths long are kept there.

### 9.6 Overlaps, and how they are avoided now

Measured by rasterising the flat layers as beads on a 0.1 mm grid and counting area covered by two
beads that lie more than 2 w apart along the path (the **double-coverage metric**, see TESTING.md):

- **Z 0.9 brim: 121 mm² double-covered** (1.2 % of 10 400 mm²). 44 mm² are 7 "chevrons": every
  ring-to-ring connector is a straight diagonal and, because all seams are aligned, the connectors
  stack radially; parallel diagonals 4 mm apart radially are only 4 x cos 45° = 2.87 mm apart, 28 %
  overlap, 6.2 mm² each.
- **Where the brim runs into the head** (the "small overlap" the owner had noticed): the last chevron
  and the end of the foot ring (2.9 mm²) at Z 0.9, then lap 0 at Z 2.0 closing with only a 2.2 mm gap
  and +24 % E on its last segment (about 1.9 mm³ extra), and the spiral starting at the same angle.
  V2 left both seam gaps (4 mm at Z 0.9, 2.2 mm at Z 2.0) open; the spiral's ramp closes them from
  above.

Kept from V2: brim rings exactly one bead apart, the foot ring = innermost brim ring, a flat ring
on it, the spiral ramping in from E ≈ 0. Changed in the plug-in:

1. **Staggered brim**: each loop ends half a bead further on than the one inside it, so the links
   never lie side by side (slivers of 1.5-2.2 mm² instead of 6.2 mm² chevrons).
2. Every closed flat ring ends one bead before its own start (round bead ends just touch), never
   0.55 w with extra E; the last segment gets the E of the law.
3. Measured result (0.1 mm raster, overlap 0): layered + skirt 21.1 → 0.0 mm², brim outside 33.1 → 2.9,
   brim inside 35.1 → 0.0, both 44.4 → 2.2, spiral + brim 43.3 → 3.7, the rebuilt head with brim 7:
   18 mm² against V2's 121 mm² (what is left: slivers of a few hundredths of a millimetre along the
   round corners of the rings).

### 9.7 Calibrated checks

Because V2 printed well, the checks accept what it did:

- Path Check: "Support OK for leaning laps" for bead-law paths, offsets up to 1.0 w at up to 2.5 % of
  the points (V2: 1.8 %, max 3.99 mm).
- Non-Planar Check: for law-following paths up to 0.5 % of the points above 0.8 w (at most 1.1 w) and
  1 % below 0.4 h (V2: 0.3 % up to 1.0 w, 0.1 %); the nozzle grazing a bead flank by up to 0.6 mm
  (`Head.Graze`) is reported, not counted as a collision (V2: about 70 moves up to 0.5 mm; 12 deeper
  moves on the imported G-code are partly an artefact of the importer splitting layers by turns).

### 9.8 The rebuilt example

`EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh`:
the head exactly as printed (`Head.3dm`: the NurbsHead turned about 75° and scaled about
1.74 / 1.56 / 1.83, bottom on Z 0), Slice Guide Mode 3 "Follow the surface", one wall centred,
Spiral, bead 4, layer 1.75, first layer 0.9 mm at 25 mm/s, Overlap 0, Min Layer Time 40 s, Qmax 330,
brim outside 7, Ginger One (Marlin).

| | V2 (2024) | rebuilt |
|---|---|---|
| Laps (> 30 mm) | 119 | 123 |
| Z range | 0.9-178.0 | 0.9-177.4 |
| E total | 48 353 | 48 621 |
| First layer | Z 0.90, F1500, 2627 mm, 0.756 E/mm | Z 0.90, F1500, 2628 mm, 0.756 E/mm |
| E/mm 5 % / median / 95 % | 0.94 / 1.42 / 1.57 | 0.92 / 1.35 / 1.69 |
| Print time | 81 min | 87 min |
| First-layer double coverage | 121 mm² | 18 mm² |

Path Check and Non-Planar Check OK, 0 crossings. Remaining differences: the rebuilt rings dip less
(Z span median 8.4 against 10.8 mm), all laps at 40 s, crown rings 1.5-2 mm apart with E/mm 0.5-0.7,
point spacing 2.3 against 1.9 mm.

**How "Follow the surface" lays the rings** (`SurfaceField`, `MeridianWarp`): u per mesh vertex =
d_bottom / (d_bottom + d_crown) by Dijkstra on the welded mesh (crown = highest vertex, or the whole
top face if flat), 40 rounds of neighbour averaging (bottom, crown and u > 0.85 held), blended 0.35
towards Z on the wall, fading out for u 0.6 → 0.9. u is sampled on planar contours every h/4,
refined until no contour point moves more than h/4 (chin and crown get more contours), smoothed along
each ring and stored in an (angle, Z) grid. Layer count = round(mean bottom-to-crown way / h). The
first layer and one layer above it are flat. Note: V2's rings are not iso-lines of any simple field
(geodesic ratio, planar Z, vertical-plane meridians were all tested); they came from a loft of the
definition's own curves. The blend was chosen for low head grazing.
