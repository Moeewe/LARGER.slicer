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
- For laps that follow the surface (section 9) offsets up to one bead width at up to 2.5 % of the
  points are accepted with a note, but only in stretches of at most 12 bead widths: such a stretch
  still touches the lap below and hangs between supported stretches like a short bridge (under a
  chin). Up to 1.3.0 this allowance cited the 2024 head (1.8 %, up to 4.0 mm); measured in plan its
  laps never lay more than 1.95 mm (half a bead) beside the lap below - the 4 mm came from the
  importer splitting its G-code into layers by turns.
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

**Non-planar layers with a vertical nozzle: the bead height is the shell law** (section 9.3):
t = max(dz, dxy x t_skin / w) from the closest point of the lap below, E = section(w, t) x length x
Factor. Flat layers and planar warps (guide surfaces) use the vertical gap to the layer below.

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
- On "Follow the surface" paths the bead heights (shell law, section 9.3) are measured again after
  the pattern.

---

## 9. What the reference print taught: UNIKAT HEAD V2 (Ginger One, 2024)

The head "UNIKAT HEAD V2" was printed on the Ginger One (Marlin) in July 2024 from the owner's
Grasshopper definition "BE GINGER SLICER (Non Planar Head) V1.1". Its G-code (18 882 lines,
18 771 G1 moves) was analysed in November 2026 and the plug-in was rebuilt against it. The G-code
itself is not in the repository.

**What to take from it and what not.** The sequence, the brim, how the laps are made, the lap times,
the seam and the crown closure (9.2, 9.4-9.6) are good practice and kept. **Its extrusion is not:**
the owner found V2 over-extruded ("doubled extrusion") at the overhangs. V2's law adds material for
the sideways offset of a lap, which a vertical nozzle does not need (9.3). Version 1.3.0 had adopted
that law and calibrated checks on it; 1.3.1 replaces it with the shell law and tightens those checks
(9.7).

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

### 9.3 The extrusion: V2's law, why it over-extrudes, and the shell law

**V2's law** (from the definition's "layer heights" cluster, confirmed on all 18 041 head points,
rms 0.0006 E/mm):

```
h  = 3D distance from the point to the closest point on the previous lap
a  = angle of that connecting line to the horizontal (90° = straight below)
t  = h (1 - (h/w)(1 - sin a)((w/h) - cos a))
   = dz + (h - dz) · dxy / w            (dz, dxy = vertical and horizontal part of h)
E/mm = 0.21 · w · t                      (w = 4)
```

**Why that is too much.** With a vertical nozzle a single-bead shell of horizontal thickness w gains,
per lap, the area w · dz in every vertical section across the wall: a parallelogram with horizontal
sides w and height dz. How far the lap is offset sideways (dxy, an overhang or a wall leaning in) does
not change that area - a sideways offset moves the material, it does not add any. V2's second term
(h - dz) · dxy / w is therefore extra material, the more the flatter the surface and the larger the
sideways step:

| | dz | dxy | V2 t | shell t | V2 / shell |
|---|---|---|---|---|---|
| vertical wall | 1.75 | 0 | 1.75 | 1.75 | 1.00 |
| 45°, laps 1.75 mm apart along the surface | 1.24 | 1.24 | 1.40 | 1.24 | 1.13 |
| 45°, dz 1.75 | 1.75 | 1.75 | 2.07 | 1.75 | 1.18 |
| half-bead step, dz 1.75 | 1.75 | 2.0 | 2.20 | 1.75 | 1.26 |
| 20°, laps 1.75 mm apart along the surface | 0.60 | 1.64 | 1.07 | 0.72 | 1.49 |
| V2's crown | 0.26 | 1.8 | 0.96 | 0.79 | 1.22 |
| one-bead step, dz 1.75 | 1.75 | 4.0 | 4.37 | 1.75 | 2.50 |

(w 4 mm, t_skin 1.75 mm; numbers from `BeadLaw.Height` and `BeadLaw.HeightV2`.)

**The shell law** (1.3.1, `BeadLaw` in `FollowSurface.cs`). Where the surface turns nearly flat (crown,
the shelf under a chin) the laps step mostly sideways and the shell becomes a skin; there the material
per mm of path is the sideways step times the skin thickness. Together:

```
A = max(w · dz, dxy · t_skin)        t = A / w = max(dz, dxy · t_skin / w)        E = factor · w · t · length
```

t_skin = `Skin Thickness` on `Continuous Path` (`PrintSettings.SkinThickness`), 0 = the layer height,
never below 0.4 x layer height. The skin term takes over where dz / dxy < t_skin / w: surfaces flatter
than about 24° for a 4 mm bead and 1.75 mm layers. The least material per lap lies at that crossover:
for laps one layer height apart along the surface t = h · t_skin / sqrt(w² + t_skin²) (0.4 h here).

**Where V2 over-extruded, lap by lap** (its own points, closest point on the previous lap, t_skin 1.75):

| Region | Laps | Surface angle | V2 E/mm | shell E/mm | V2 / shell |
|---|---|---|---|---|---|
| neck, vertical wall | 1-11 | 77-82° | 1.50-1.53 | 1.50-1.53 | 1.00 |
| chin overhang | 13-24 | 65-75° | 1.28-1.36 | 1.23-1.31 | 1.03-1.05 |
| skull | 40-90 | 61-81° | 1.30-1.51 | 1.29-1.50 | 1.00-1.04 |
| shoulder towards the crown | 92-104 | 32-54° | 1.10-1.32 | 0.81-1.23 | 1.07-1.36 |
| last laps before the crown | 106-112 | 14-27° | 0.89-1.05 | 0.64-0.70 | 1.36-1.57 |
| crown | 113-119 | 8-9° | 0.78-0.83 | 0.65-0.67 | 1.20-1.25 |

Over the whole head V2 put 3.4 % more material than the shell law (46 146 against 44 616 E); per point
the median is +0.5 %, 10 % of the points get more than +17.5 %, 1 % more than +56 %. By surface angle:
+0 % above 70°, +2 % at 60-70°, +6 % at 50-60°, +13 % at 40-50°, +27 % at 30-40°, +52 % at 20-30°,
+44 % at 10-20°, +22 % below 10°. The overhang V2 is remembered for is the transition to the crown and
the crown itself; under the chin V2's laps stepped little sideways (up to 0.8 mm), so the excess there
was only 3-5 %. The worst case of the law, about 2.5 x at a full bead's step, did not occur in V2 because
its laps never stepped more than 1.95 mm sideways.

In the code: `BeadLaw.Height(p, q, w, skin)`, `BeadLaw.Apply` sets every point's height from the
closest point of the lap about one lap back in the path (0.75-2 laps); Guide Mode "Follow the surface"
(`Warp.UseBeadLaw`) and again after Surface Pattern. Guide Mode "Follow the part" keeps the vertical
gap and adds the skin term from the wall's lean (`MeridianWarp.BeadHeight`). `BeadLaw.HeightV2` keeps
V2's law only so Non-Planar Check can recognise files made with it (also imported G-code): it reports
"OLD EXTRUSION LAW" with the excess and judges E by the shell law. There is no switch back to V2's law:
an old file is reproduced by its own G-code, and re-slicing it should give the corrected E.

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
   0.55 w with extra E; the last segment gets the E of the bead law.
3. Measured result (0.1 mm raster, overlap 0): layered + skirt 21.1 → 0.0 mm², brim outside 33.1 → 2.9,
   brim inside 35.1 → 0.0, both 44.4 → 2.2, spiral + brim 43.3 → 3.7, the rebuilt head with brim 7:
   18 mm² against V2's 121 mm² (what is left: slivers of a few hundredths of a millimetre along the
   round corners of the rings).

### 9.7 Checks: what V2 justified, and what is left (1.3.1)

1.3.0 accepted on law-following paths what V2 did, "because V2 printed it". Re-examined:

- **Bead height above 0.8 w** (1.3.0: up to 0.5 % of the points, up to 1.1 w): removed. Those heights
  came from V2's law itself (t → h where dxy → w); by the shell law a bead is never higher than the
  vertical gap or the skin, so a point above 0.8 w is a real fault.
- **E against the measured height**: the tolerance was not loosened (mean ≤ 10 %, ≤ 5 % of the points
  more than 10 % off), but 1.3.0 measured against V2's law, which let its over-extrusion pass. Now the
  shell law is the reference; E that matches V2's law is reported as "OLD EXTRUSION LAW" with its
  excess (V2's own G-code, imported: about 11 % over the measured points, and "EXTRUSION DOES NOT FIT").
- **Bead height below 0.4 h** (up to 1 % of the points): kept for the last crown rings; points where the
  skin term sets the height are not judged by that rule (the skin is t_skin thick, at least 0.4 h by
  construction; t = A / w is only an equivalent height there).
- **Support** (Path Check, up to one bead width beside the lap below at up to 2.5 %): V2 never needed it
  (≤ 1.95 mm in plan; the 4 mm were an import artefact). Kept only where it is physically plausible: in
  stretches of at most 12 bead widths that hang between supported stretches like a short bridge. The
  rebuilt head needs it under the chin (0.8 % of its points up to 4.0 mm, where its rings dip less than
  V2's and step out further).
- **Nozzle grazing a bead flank up to 0.6 mm** (`Head.Graze`): kept. It is geometry, not extrusion
  (V2: about 70 moves up to 0.5 mm); with less material on leaning laps the flanks are, if anything,
  smaller.

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
| E total | 48 353 | 48 621 with the V2 law (1.3.0), about 47 000 with the shell law (1.3.1) |
| First layer | Z 0.90, F1500, 2627 mm, 0.756 E/mm | Z 0.90, F1500, 2628 mm, 0.756 E/mm |
| E/mm 5 % / median / 95 % | 0.94 / 1.42 / 1.57 | 0.92 / 1.35 / 1.69 (V2 law), about 0.74 / 1.33 / 1.64 (shell law) |
| Print time | 81 min | 87 min |
| First-layer double coverage | 121 mm² | 18 mm² |

With the V2 law (1.3.0): Path Check and Non-Planar Check OK, 0 crossings. With the shell law (1.3.1)
only E changes; replayed on the example's G-code (laps by its layer comments, `BeadLaw.Apply`):
E on the head about 3.5 % lower (total about 47 000 instead of 48 621), E/mm 5 % / median / 95 %
0.74 / 1.33 / 1.64, the shoulder towards the crown 20-48 % lower per lap, the crown rings 0.50-0.56
E/mm; Non-Planar Check on that replay: E fits the shell law (mean 0.4 %), 0.6 % thin points (accepted),
head clearance OK. Not yet re-solved in Grasshopper: the definition itself was not changed, it picks up
the law when it solves. Remaining differences: the rebuilt rings dip less (Z span median 8.4 against
10.8 mm), all laps at 40 s, crown rings 1.5-2 mm apart, point spacing 2.3 against 1.9 mm.

**How "Follow the surface" lays the rings** (`SurfaceField`, `MeridianWarp`): u per mesh vertex =
d_bottom / (d_bottom + d_crown) by Dijkstra on the welded mesh (crown = highest vertex, or the whole
top face if flat), 40 rounds of neighbour averaging (bottom, crown and u > 0.85 held), blended 0.35
towards Z on the wall, fading out for u 0.6 → 0.9. u is sampled on planar contours every h/4,
refined until no contour point moves more than h/4 (chin and crown get more contours), smoothed along
each ring and stored in an (angle, Z) grid. Layer count = round(mean bottom-to-crown way / h). The
first layer and one layer above it are flat. Note: V2's rings are not iso-lines of any simple field
(geodesic ratio, planar Z, vertical-plane meridians were all tested); they came from a loft of the
definition's own curves. The blend was chosen for low head grazing.
