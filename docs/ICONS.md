# Icons

How the component icons, the tab icon and the logo are made. Part of the
[developer handbook](README.md).

**One script draws everything:** `LARGERslicer/scripts/generate_icons.py` (Python 3 + Pillow).
The other `generate_*.py` scripts in that folder (`generate_gh_icons.py`, `generate_pattern_icons.py`,
`generate_plugin_icon.py`, `generate_designer_svg_icons.py`, `generate_professional_svg_icons.py`)
are earlier attempts and are not used any more; neither are the `*.svg` files and about 25 older
PNGs in `Resources/` that no component loads (e.g. `BottomLayer*Icon.png`, `Infill*Icon.png`,
`SmartSlicerIcon.png`, `RTreeIcon.png`).

---

## 1. The style

As Grasshopper draws its own icons: **light bodies and one accent colour per group, each with a
dark edge and a faint shadow, and no ground of their own.** Grasshopper 1 shows one icon on very
different grounds (light or dark toolbar, the grey component capsule, green when selected); the
light body carries on dark grounds, the dark edge on light ones, so one file reads everywhere.
Until version 1.2 the glyphs sat on a dark rounded "chip"; `chip()` and `chip_ground()` are still in
the script but no longer used.

Drawing rules:

- Every glyph is drawn in a **24 x 24 unit field** at 16x supersampling (`S = 16`, 384 px), with
  round line caps. Coordinates in the drawing functions are in the 24-unit field.
- Lines are 1.8-2.3 units wide; secondary lines use `SOFT` (ink at low alpha), which becomes a quiet grey.
- Draw the main shape in **`INK`** (dark slate, `(38, 44, 58)`, never black). `finish()` turns ink into
  the light body. Use **one accent colour**, the group's, for the part that says what the component does.
- Common building blocks: `doc(g)` (a sheet with folded corner, for file outputs), `frame(g)` (a rounded
  square, for "inside the part"), `portal(g)` (a gantry), `hexagon`, `arc`, `zigzag`, `arrow`, `helix`.

| Constant | Colour | Group |
|---|---|---|
| `BLUE` `(10, 132, 255)` | blue | Slicer |
| `TEAL` `(48, 176, 199)` | teal | curve tools of the former Toolpaths tab |
| `ORANGE` `(255, 149, 0)` | orange | DXR, export, machines, robot |
| `GREEN` `(40, 190, 90)` | green | CNC |
| `PURPLE` `(175, 82, 222)` | purple | Utilities |

### `finish(src, scale=0.92)`

1. Pixels in ink colour become `LIGHT (250, 251, 253)`; faint ink (alpha ≤ 160) becomes
   `MID (168, 175, 188)` - soft lines turn into a quiet grey. Accent colours stay.
2. The drawing is scaled to 92 % and centred on the 384 px field.
3. Work at 4x the final size (`k = 96 x SIZE / 24`): the alpha above 50 is thresholded, dilated
   (MaxFilter of 2·round(3f)+1) and blurred (0.7 f) into the **edge** in `EDGE (30, 34, 43)`; 30 % of
   that, blurred by 2.5 f and moved down by 3 f, is the **shadow**.
4. Shadow, edge and body are stacked and scaled to `SIZE x SIZE` with Lanczos.

`f = SIZE / 24`: edge, shadow and offset scale with the output size, so the 48 px icons are the same
drawings finished at twice the size, not upscaled 24 px images.

---

## 2. The registry

```python
ICONS = {}
def icon(name):            # decorator: registers a drawing under its file name
    ...

@icon('SlicerQuickStartIcon')
def _(g): g.circle(12, 12, 9, INK, 2); g.poly([(10, 7.8), (10, 16.2), (16.8, 12)], BLUE)
```

The name is the file name without `.png` and the name the component asks for:
`IconHelper.Load("SlicerQuickStartIcon.png", this)` in Grasshopper 1, `Io.Icon("SlicerQuickStartIcon")`
in Grasshopper 2. The script currently registers 92 icons, grouped by section comments
(slicer, "one icon per component (1.2.2)", older toolpath tools, DXR/export/machine, CNC,
utilities, robot).

`G` is the drawing canvas: `line(pts, col, w, closed)`, `rrect`, `circle`, `dot`, `poly`, and
`save(name)` which calls `finish()` and writes `ICON_OUT/name`.

---

## 3. Running it

```bash
cd LARGERslicer
python3 scripts/generate_icons.py
```

writes (from the script's own folder, so it can be run from anywhere):

| File | What |
|---|---|
| `LARGERslicer/Resources/<Name>.png` | all registered component icons, 24 px (embedded into the GH1 `.gha`, and as the 24 px images of GH2) |
| `LARGERslicer/icon.png` | the 256 px logo (Yak package icon, GitHub) |
| `LARGERslicer/Resources/plugin-icon.png` | the same 256 px logo (GH2 plug-in icon, 256 px slot) |
| `docs/logo-1024.png` | the logo at 1024 px (website) |
| `LARGERslicer/Resources/LARGERslicer_24x24.png` | the tab icon (three stacked loops, the top one blue), finished like the components; GH1 takes it from `Properties/Resources.resx`, GH2 as the 24 px plug-in icon |

**Grasshopper 2 (48 px).** Grasshopper 2 draws icons larger and on high-resolution screens. The same
drawings are finished at 48 px, and nothing else (no logo, no tab icon) is written:

```bash
cd LARGERslicer
ICON_SIZE=48 ICON_OUT=../LARGERslicer.GH2/Icons/48 python3 scripts/generate_icons.py
```

(`ICON_OUT` is taken as given, so a relative path counts from the current folder.)
`LARGERslicerGH2.csproj` embeds `../LARGERslicer/Resources/*Icon.png` as
`LARGERslicer.GH2.Icons.24.<name>.png`, `Icons/48/*.png` as `...Icons.48...`, and the logo as the 256 px
image of the plug-in icon; `Io.Icon` builds the `PixelIcon` from the largest image it finds (48 px, else 24 px; 256 px for the
plug-in): Grasshopper 2 9.1's `PixelIcon` refuses several sizes of one picture and then drew lettered
stand-ins.

### The logo

`logo(size)`: a dark rounded square (gradient `(28, 36, 54)` → `(10, 14, 24)`, corner radius 22.5 %)
with **one continuous line that climbs in three loops** (`helix`): the single-line print path the
slicer is about. Back halves are drawn first and dimmer, front halves on top, so the loops read as a
coil; the newest third of the line is blue `(10, 132, 255)`, and a white dot marks the nozzle where
the line ends.

---

## 4. Adding or changing an icon

1. Add a function with `@icon('YourComponentIcon')` in the section of its group; draw in ink with
   the group's accent.
2. Before running, record the checksums of the current files:
   ```bash
   md5 -q LARGERslicer/Resources/*.png > /tmp/icons-before.txt      # Linux: md5sum
   ```
3. Run the script for 24 px **and** for 48 px (section 3).
4. Compare: `md5 -q LARGERslicer/Resources/*.png | diff /tmp/icons-before.txt -` must show only
   the icons you meant to change. The drawing is deterministic: with the same Pillow version a run
   reproduces every unchanged file byte for byte (checked with Pillow 12.2: 92 of 92 identical at 24
   and at 48 px). If unrelated icons change, a shared helper or `finish()` changed, or a different
   Pillow resizes differently - do not commit those.
5. Reference the icon from the component (`IconHelper.Load("YourComponentIcon.png", this)` in GH1,
   `Io.Icon("YourComponentIcon")` in GH2). New PNGs in `Resources/` are embedded automatically
   (`<EmbeddedResource Include="Resources\*.png" />`).
6. Look at it: on a light and a dark toolbar and on the canvas (selected and not). A sheet of all
   icons on several grounds is the quickest check.

`IconHelper.Load` and `Io.Icon` never throw: a missing icon shows Grasshopper's default, so a wrong
name is only noticed by looking.
