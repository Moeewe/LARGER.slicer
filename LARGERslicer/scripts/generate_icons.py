#!/usr/bin/env python3
"""
One icon set for the whole plug-in.

Style: minimal line glyphs in one ink colour with a single accent colour per group,
2 px strokes with round ends, generous white space. Follows the Grasshopper icon rules:
24 x 24 px, 2 px margin (drawing area 20 x 20), no pure black, transparent ground.
Every icon is written twice: X.png with dark ink (canvas, light toolbar) and X.dark.png with
light ink (toolbar in dark mode). Grasshopper 1 has no icon pairs; IconHelper picks one.
Drawn at 16x and scaled down, so curves stay smooth.

Run from anywhere:  python3 scripts/generate_icons.py
Writes Resources/*.png, icon.png (256 px logo for Yak/GitHub) and the 24 px tab icon.
"""
import math, os
from PIL import Image, ImageDraw

S = 16
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Resources')

INK_ON_DARK = (232, 236, 244)  # the ink colour on a dark toolbar
INK = (38, 44, 58, 255)        # dark slate, not black
SOFT = (38, 44, 58, 90)        # same ink, light: secondary lines
BLUE = (10, 132, 255, 255)     # slicer
TEAL = (48, 176, 199, 255)     # toolpaths
ORANGE = (255, 149, 0, 255)    # DXR / export / machine
GREEN = (40, 190, 90, 255)     # CNC
PURPLE = (175, 82, 222, 255)   # utilities


class G:
    def __init__(self):
        self.img = Image.new('RGBA', (24 * S, 24 * S), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def line(self, pts, col=INK, w=2.0, closed=False):
        p = [(x * S, y * S) for x, y in pts]
        if closed: p = p + [p[0], p[1]]
        self.d.line(p, fill=col, width=int(round(w * S)), joint='curve')
        r = w * S / 2
        for x, y in (p if not closed else []):
            pass
        for x, y in [p[0], p[-1]] if not closed else []:
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=col)   # round caps
        return self

    def rrect(self, x0, y0, x1, y1, r, col=INK, w=2.0, fill=None):
        self.d.rounded_rectangle([x0 * S, y0 * S, x1 * S, y1 * S], radius=r * S, outline=col, width=int(round(w * S)), fill=fill)
        return self

    def circle(self, cx, cy, r, col=INK, w=2.0, fill=None):
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], outline=None if fill and w == 0 else col, width=int(round(w * S)), fill=fill)
        return self

    def dot(self, cx, cy, r=1.6, col=INK):
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=col)
        return self

    def poly(self, pts, col=INK):
        self.d.polygon([(x * S, y * S) for x, y in pts], fill=col)
        return self

    def save(self, name):
        self.img.resize((24, 24), Image.LANCZOS).save(os.path.join(OUT, name))
        on_dark(self.img).resize((24, 24), Image.LANCZOS).save(os.path.join(OUT, name.replace('.png', '.dark.png')))


def on_dark(img):
    """The same drawing with the ink turned light; accent colours stay."""
    px = img.copy(); data = px.load()
    for y in range(px.height):
        for x in range(px.width):
            r, g, b, a = data[x, y]
            if a and (r, g, b) == INK[:3]: data[x, y] = INK_ON_DARK + (a,)
    return px


def arc(cx, cy, r, a0, a1, n=28, ry=None):
    ry = r if ry is None else ry
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def zigzag(x0, x1, ys):
    p = []
    for i, y in enumerate(ys):
        p += [(x0, y), (x1, y)] if i % 2 == 0 else [(x1, y), (x0, y)]
    return p


def arrow(g, x0, y0, x1, y1, col, w=2.0, head=3.2):
    g.line([(x0, y0), (x1, y1)], col, w)
    a = math.atan2(y1 - y0, x1 - x0)
    for s in (2.5, -2.5):
        g.line([(x1, y1), (x1 - head * math.cos(a + s * 0.25), y1 - head * math.sin(a + s * 0.25))], col, w)


def doc(g, col=INK):
    """Sheet of paper with folded corner, x 5..17, y 3..21."""
    g.line([(13, 3), (6.5, 3), (5, 4.5), (5, 19.5), (6.5, 21), (15.5, 21), (17, 19.5), (17, 7), (13, 3), (13, 7), (17, 7)], col, 1.8)


def frame(g, col=INK):
    g.rrect(3, 3, 21, 21, 4.5, col, 2)


ICONS = {}
def icon(name):
    def deco(fn):
        ICONS[name] = fn
        return fn
    return deco

# ---------------------------------------------------------------- slicer (blue)
@icon('SlicerBedIcon')
def _(g):
    g.line([(3.5, 17.5), (8, 7), (20.5, 7), (16, 17.5)], INK, 2, closed=True)
    g.line([(7.6, 12.2), (18.2, 12.2)], SOFT, 1.4); g.line([(11.8, 7), (9.4, 17.5)], SOFT, 1.4)
    g.dot(3.9, 17.4, 2.3, BLUE)


@icon('SlicerQuickStartIcon')
def _(g): g.circle(12, 12, 9, INK, 2); g.poly([(10, 7.8), (10, 16.2), (16.8, 12)], BLUE)

@icon('SlicerSettingsIcon')
def _(g):
    for y, k in ((7, 15), (12, 9), (17, 13)):
        g.line([(3.5, y), (20.5, y)], INK, 2); g.circle(k, y, 2.3, BLUE, 0, BLUE)

@icon('SlicerMachineIcon')
def _(g):
    g.line([(5, 6), (13, 6), (13, 10)], INK, 2)
    g.rrect(9, 10, 17, 15, 1.4, INK, 2)
    g.poly([(11, 15), (15, 15), (13, 19)], BLUE)
    g.line([(5, 20.5), (20, 20.5)], BLUE, 2)

@icon('SlicerSliceIcon')
def _(g):
    for i, y in enumerate((6, 10.5, 15, 19.5)):
        half = (6, 8.5, 8.5, 6)[i]
        g.line([(12 - half, y), (12 + half, y)], BLUE if i == 1 else INK, 2)

@icon('SlicerWallsIcon')
def _(g): frame(g); g.rrect(7.5, 7.5, 16.5, 16.5, 2.2, BLUE, 2)

@icon('SlicerBottomIcon')
def _(g): frame(g); g.line(zigzag(7.5, 16.5, (8, 12, 16)), BLUE, 2)

@icon('SlicerRibsIcon')
def _(g):
    frame(g)
    g.line([(10.6, 3.6), (10.6, 9.8)] + arc(12, 9.8, 1.4, 180, 0, 10) + [(13.4, 3.6)], BLUE, 1.8)
    g.line([(10.6, 20.4), (10.6, 14.2)] + arc(12, 14.2, 1.4, 180, 360, 10) + [(13.4, 20.4)], BLUE, 1.8)

def hexagon(cx, cy, r):
    return [(cx + r * math.cos(math.radians(60 * k)), cy + r * math.sin(math.radians(60 * k))) for k in range(6)]

@icon('SlicerCellsIcon')
def _(g):
    frame(g)
    for cx, cy in ((9.2, 9.4), (14.8, 12.6), (9.2, 15.8)):
        g.line(hexagon(cx, cy, 3.0), BLUE, 1.6, closed=True)

@icon('InfillCellsIcon')
def _(g):
    frame(g)
    for cx, cy in ((9.2, 9.4), (14.8, 12.6), (9.2, 15.8)):
        g.line(hexagon(cx, cy, 3.0), TEAL, 1.6, closed=True)


@icon('SlicerWaveIcon')
def _(g):
    frame(g)
    g.line([(6 + i * 0.25, 12 - 3.4 * math.sin(i * 0.25 / 12 * 2 * math.pi * 2.5)) for i in range(49)], BLUE, 1.9)

@icon('SlicerSketchIcon')
def _(g):
    frame(g)
    pts = [(8 + 8 * (i / 30) + 2.2 * math.sin(i / 30 * 2 * math.pi), 20.2 - 16.4 * (i / 30)) for i in range(31)]
    g.line(pts, BLUE, 2.2)
    g.dot(pts[-1][0], pts[-1][1], 1.9, INK)

@icon('SlicerDesktopIcon')
def _(g):
    g.rrect(4, 4, 20, 20, 2.5, INK, 2)
    g.line([(8, 16.5), (16, 16.5)], BLUE, 2)
    g.line([(9, 13.5), (15, 13.5)], BLUE, 2)
    g.poly([(10.4, 7), (13.6, 7), (12, 10.4)], INK)

@icon('SlicerChambersIcon')
def _(g):
    frame(g)
    g.rrect(7.2, 7.2, 16.8, 16.8, 1.2, BLUE, 1.6)
    for a, b in (((12, 3.5), (12, 7.2)), ((12, 16.8), (12, 20.5)), ((3.5, 12), (7.2, 12)), ((16.8, 12), (20.5, 12))):
        g.line([a, b], BLUE, 1.6)

@icon('SlicerPathIcon')
def _(g):
    pts = [(12 + 7.5 * math.cos(t * 5 * math.pi), 19.5 - t * 14 + 1.6 * math.sin(t * 5 * math.pi)) for t in [i / 150 for i in range(151)]]
    g.line(pts[:70], INK, 2); g.line(pts[69:], BLUE, 2)

@icon('SlicerRoundIcon')
def _(g):
    g.line([(5, 20), (5, 5), (20, 5)], SOFT, 1.6)
    g.line([(9, 20), (9, 15)] + arc(15, 15, 6, 180, 270) + [(20, 9)], BLUE, 2.2)

@icon('SlicerStartEndIcon')
def _(g):
    g.line(arc(11, 13, 8, 60, 335, 40), SOFT, 1.8)
    g.rrect(7.5, 9.5, 14.5, 16.5, 2.2, INK, 2)
    arrow(g, 15.5, 8.5, 20.5, 3.5, BLUE, 2)

@icon('SlicerExtrusionIcon')
def _(g):
    for i, h in enumerate((6, 10, 15, 9)):
        x = 4.6 + i * 4.6
        g.line([(x, 20), (x, 20 - h)], BLUE if i == 2 else INK, 2.6)

@icon('SlicerCheckIcon')
def _(g): g.circle(12, 12, 9, INK, 2); g.line([(7.8, 12.4), (10.8, 15.4), (16.4, 8.8)], BLUE, 2.3)

@icon('SlicerOpenIcon')
def _(g):
    for y in (6.5, 12, 17.5): g.line([(3.5, y), (12.5, y)], INK, 2)
    arrow(g, 15.5, 12, 21, 12, BLUE, 2)

# ---------------------------------------------------------------- older toolpath tools (teal)
@icon('AlignCurvesIcon')
def _(g):
    g.line([(4.5, 3.5), (4.5, 20.5)], TEAL, 2)
    for y, x in ((7, 19), (12, 14), (17, 17)): g.line([(8, y), (x, y)], INK, 2)

@icon('AlternateCurvesIcon')
def _(g): arrow(g, 4, 8.5, 20, 8.5, INK, 2); arrow(g, 20, 15.5, 4, 15.5, TEAL, 2)

@icon('BridgeCurvesIcon')
def _(g):
    g.line([(3.5, 17), (8, 17)], INK, 2); g.line([(16, 17), (20.5, 17)], INK, 2)
    g.line(arc(12, 17, 4, 180, 360, 20), TEAL, 2)

@icon('ContinuousInfillIcon')
def _(g): frame(g); g.line([(7.5, 8), (16.5, 8), (16.5, 12), (7.5, 12), (7.5, 16), (16.5, 16)], TEAL, 2)

@icon('ContinuousPathFromCurvesIcon')
def _(g): g.line(arc(12, 12, 8, -60, 250, 40), INK, 2); g.dot(16, 5.1, 2.2, TEAL)

@icon('ContinuousToolpathIcon')
def _(g):
    pts = [(4 + 14 * t, 12 - 5.5 * math.sin(2 * math.pi * t)) for t in [i / 40 for i in range(41)]]
    g.line(pts, INK, 2); g.dot(4, 12, 2.2, TEAL)
    g.line([(18, 12), (20.6, 9.6)], INK, 2); g.line([(18, 12), (21, 13.4)], INK, 2)

@icon('OptimizedToolpathIcon')
def _(g):
    p = [(5, 17.5), (10, 7), (15, 15), (19.5, 6)]
    g.line(p, INK, 2)
    for x, y in p: g.dot(x, y, 2.2, TEAL)

@icon('JoinOpenContoursIcon')
def _(g): g.line(arc(8.5, 12, 5, 90, 270, 20), INK, 2); g.line(arc(15.5, 12, 5, -90, 90, 20), INK, 2); g.line([(8.5, 7), (15.5, 7)], TEAL, 2); g.line([(8.5, 17), (15.5, 17)], TEAL, 2)

@icon('SuppressSelfIntersectionsIcon')
def _(g):
    g.line([(4, 6), (20, 18)], INK, 2); g.line([(4, 18), (9.6, 13.8)], INK, 2); g.line([(14.4, 10.2), (20, 6)], INK, 2)
    g.circle(12, 12, 3.2, TEAL, 1.8)

@icon('InfillContourIcon')
def _(g): frame(g); g.rrect(7, 7, 17, 17, 2.5, TEAL, 1.8); g.dot(12, 12, 1.7, TEAL)

@icon('InfillEulerTransformationIcon')
def _(g):
    frame(g)
    g.line([(7.5, 16.5), (7.5, 7.5), (12, 12), (16.5, 7.5), (16.5, 16.5), (12, 12)], TEAL, 1.8)

@icon('InfillLinesIcon')
def _(g):
    frame(g)
    for x in (8, 12, 16): g.line([(x, 7.5), (x, 16.5)], TEAL, 2)

@icon('CurvePatternSplitIcon')
def _(g):
    g.line([(3.5, 12), (8, 12)], INK, 2.2); g.line([(10.5, 12), (13.5, 12)], TEAL, 2.2); g.line([(16, 12), (20.5, 12)], INK, 2.2)
    for x in (9.25, 14.75): g.line([(x, 8.5), (x, 15.5)], SOFT, 1.4)

# ---------------------------------------------------------------- DXR, export, machine (orange)
@icon('DXRGeneratorIcon')
def _(g): doc(g); arrow(g, 11, 10, 11, 17, ORANGE, 2, 2.8)

@icon('DXRPostprocessorIcon')
def _(g): doc(g); g.line([(9.6, 11), (7.6, 13.5), (9.6, 16)], ORANGE, 1.8); g.line([(12.4, 11), (14.4, 13.5), (12.4, 16)], ORANGE, 1.8)

@icon('DXRFileHealthCheckIcon')
def _(g): doc(g); g.line([(8, 13.6), (10.4, 16), (14.2, 11)], ORANGE, 2)

@icon('DXRVariableWidthIcon')
def _(g): g.poly([(3.5, 11.2), (20.5, 7), (20.5, 17), (3.5, 12.8)], ORANGE); g.line([(3.5, 12), (20.5, 12)], INK, 1.4)

@icon('MachineSettingsIcon')
def _(g):
    for x, k in ((7, 15), (12, 8.5), (17, 13)):
        g.line([(x, 3.5), (x, 20.5)], INK, 2); g.circle(x, k, 2.3, ORANGE, 0, ORANGE)

@icon('MachineSettingsExtendedIcon')
def _(g):
    for x, k in ((6, 14), (11, 8.5)):
        g.line([(x, 3.5), (x, 20.5)], INK, 2); g.circle(x, k, 2.3, ORANGE, 0, ORANGE)
    g.line([(14.5, 12), (20.5, 12)], ORANGE, 2); g.line([(17.5, 9), (17.5, 15)], ORANGE, 2)

@icon('CadPackageExportIcon')
def _(g): g.line([(4, 13), (4, 19.5), (20, 19.5), (20, 13)], INK, 2); arrow(g, 12, 15.5, 12, 4, ORANGE, 2)

@icon('SafeIcon')
def _(g): g.line([(4, 13), (4, 19.5), (20, 19.5), (20, 13)], INK, 2); arrow(g, 12, 4, 12, 15, ORANGE, 2)

@icon('GCodeDiffCheckIcon')
def _(g):
    g.rrect(3.5, 4, 10.5, 20, 2, INK, 1.8); g.rrect(13.5, 4, 20.5, 20, 2, INK, 1.8)
    g.line([(5.8, 9), (8.2, 9)], SOFT, 1.5); g.line([(5.8, 12), (8.2, 12)], ORANGE, 1.8); g.line([(15.8, 12), (18.2, 12)], ORANGE, 1.8); g.line([(15.8, 15), (18.2, 15)], SOFT, 1.5)

@icon('GCodeERemapMultiPartIcon')
def _(g):
    g.line([(9.5, 5), (4.5, 5), (4.5, 19), (9.5, 19)], INK, 2); g.line([(4.5, 12), (8.5, 12)], INK, 2)
    arrow(g, 12.5, 12, 20.5, 12, ORANGE, 2)

@icon('FastGCodeDxrPreviewIcon')
def _(g): g.line(arc(12, 12, 9, 200, 340, 24, 7) + arc(12, 12, 9, 20, 160, 24, 7), INK, 1.9, closed=True); g.dot(12, 12, 2.8, ORANGE)

@icon('SuperPrintpathPreviewIcon')
def _(g):
    g.line(arc(12, 9.5, 8.5, 200, 340, 24, 6) + arc(12, 9.5, 8.5, 20, 160, 24, 6), INK, 1.9, closed=True); g.dot(12, 9.5, 2.4, ORANGE)
    g.line([(6, 17.5), (18, 17.5)], SOFT, 1.6); g.line([(8, 20.5), (16, 20.5)], SOFT, 1.6)

@icon('CustomPreviewLineweightsIcon')
def _(g): g.line([(4, 6), (20, 6)], INK, 1.2); g.line([(4, 11.5), (20, 11.5)], INK, 2.4); g.line([(4.6, 18), (19.4, 18)], ORANGE, 3.8)

@icon('OverlapTesterVariableWidthIcon')
def _(g): g.rrect(3, 5.5, 16, 13.5, 4, INK, 1.9); g.rrect(8, 10.5, 21, 18.5, 4, ORANGE, 1.9)

@icon('SurfaceThicknessHeatmapIcon')
def _(g):
    for i, a in enumerate((70, 130, 190, 255)):
        g.rrect(3.5 + i * 4.4, 6 + (3 - i) * 2.2, 6.9 + i * 4.4, 20, 1.2, None, 0, ORANGE[:3] + (a,))

@icon('FeedrateIcon')
def _(g): g.line(arc(12, 15, 9, 180, 360, 30), INK, 2); g.line([(12, 15), (16.8, 8.6)], ORANGE, 2.2); g.dot(12, 15, 1.9, INK)

# ---------------------------------------------------------------- CNC (green)
@icon('CNCProgramIcon')
def _(g): g.line([(12, 3.5), (12, 9)], INK, 3.2); g.poly([(9.4, 9), (14.6, 9), (12, 13)], INK); g.line(zigzag(4.5, 19.5, (16, 20)), GREEN, 2)

@icon('CncUtilityOrientIcon')
def _(g):
    g.rrect(8, 8, 16, 16, 1.8, INK, 2); g.line(arc(12, 12, 9, 200, 320, 20), GREEN, 2)
    g.line([(18.9, 6.2), (19.2, 2.8)], GREEN, 2); g.line([(18.9, 6.2), (15.6, 6.4)], GREEN, 2)

@icon('CncUtilityBBoxIcon')
def _(g):
    g.rrect(7, 7, 17, 17, 1.5, INK, 2)
    for (x, y, dx, dy) in ((3.5, 3.5, 1, 1), (20.5, 3.5, -1, 1), (3.5, 20.5, 1, -1), (20.5, 20.5, -1, -1)):
        g.line([(x + 3 * dx, y), (x, y), (x, y + 3 * dy)], GREEN, 1.8)

@icon('CncUtilitySliceIcon')
def _(g):
    g.rrect(4, 5, 20, 19, 2, INK, 2)
    for y in (9.7, 14.3): g.line([(4.5, y), (19.5, y)], GREEN, 1.8)

@icon('CncUtilitySplitIcon')
def _(g): g.rrect(3.5, 6, 10.5, 18, 2, INK, 2); g.rrect(13.5, 6, 20.5, 18, 2, INK, 2); g.line([(12, 3.5), (12, 20.5)], GREEN, 1.6)

@icon('CncUtilityDepthIcon')
def _(g): g.line([(3.5, 8), (9, 8), (9, 18), (20.5, 18)], INK, 2); arrow(g, 15, 5, 15, 14, GREEN, 2, 2.8)

@icon('CncUtilityBlockIcon')
def _(g):
    g.line([(12, 3.5), (20, 8), (20, 16.5), (12, 21), (4, 16.5), (4, 8)], INK, 2, closed=True)
    g.line([(4, 8), (12, 12.5), (20, 8)], GREEN, 1.8); g.line([(12, 12.5), (12, 21)], GREEN, 1.8)

@icon('CncUtilityFixtureIcon')
def _(g):
    g.rrect(7.5, 9, 16.5, 17, 1.5, INK, 2)
    g.line([(3.5, 6), (3.5, 20), (6.5, 20)], GREEN, 2); g.line([(20.5, 6), (20.5, 20), (17.5, 20)], GREEN, 2)
    g.line([(3.5, 6), (9, 6)], GREEN, 2); g.line([(20.5, 6), (15, 6)], GREEN, 2)

@icon('CncUtilityBomIcon')
def _(g):
    for y in (6.5, 12, 17.5): g.dot(5, y, 1.5, GREEN); g.line([(9, y), (20, y)], INK, 2)

@icon('CncUtilityExportIcon')
def _(g): g.line([(4, 13), (4, 19.5), (20, 19.5), (20, 13)], INK, 2); arrow(g, 12, 15.5, 12, 4, GREEN, 2)

# ---------------------------------------------------------------- utilities (purple)
@icon('DateTimestampIcon')
def _(g): g.circle(12, 12, 9, INK, 2); g.line([(12, 7), (12, 12), (15.6, 14.2)], PURPLE, 2)

@icon('DesktopPathIcon')
def _(g): g.rrect(3, 4.5, 21, 16, 2.2, INK, 2); g.line([(12, 16), (12, 20)], INK, 2); g.line([(8, 20), (16, 20)], PURPLE, 2)

@icon('FastDeconstructPointIcon')
def _(g): g.line([(12, 13), (12, 3.5)], INK, 2); g.line([(12, 13), (4, 18.5)], INK, 2); g.line([(12, 13), (20, 18.5)], INK, 2); g.dot(12, 13, 2.6, PURPLE)

@icon('RTreeClosestPointIcon')
def _(g):
    for x, y in ((5, 6), (19, 7), (17, 18), (6, 18.5)): g.dot(x, y, 1.6, INK)
    g.line([(9, 12), (14.5, 10.5)], PURPLE, 1.8); g.dot(9, 12, 2.3, PURPLE); g.dot(14.5, 10.5, 1.6, INK)

@icon('RTreeSortIcon')
def _(g):
    p = [(5, 18), (8.5, 8), (13, 15), (18.5, 6)]
    g.line(p, PURPLE, 1.6)
    for i, (x, y) in enumerate(p): g.dot(x, y, 1.4 + i * 0.45, INK)

@icon('StreamFreezeIcon')
def _(g):
    g.circle(12, 12, 9, INK, 2); g.line([(9.6, 8), (9.6, 16)], PURPLE, 2.4); g.line([(14.4, 8), (14.4, 16)], PURPLE, 2.4)


@icon('SlicerEasyIcon')
def _(g):
    g.rrect(3.5, 3.5, 20.5, 20.5, 4.5, INK, 2)
    g.line([(8, 12.4), (11, 15.4), (16.4, 8.6)], BLUE, 2.4)


# ---- robot
@icon('RobotArmIcon')
def _(g):
    g.line([(5, 20), (11, 20)], INK, 2.4); g.line([(8, 20), (8, 14.5), (14, 7), (19, 10)], INK, 2)
    g.dot(8, 14.5, 2.0, ORANGE); g.dot(14, 7, 2.0, ORANGE); g.dot(19, 10, 1.6, INK)


@icon('RobotToolIcon')
def _(g):
    g.rrect(7, 3.5, 17, 9.5, 1.6, INK, 2); g.line([(12, 9.5), (12, 14)], INK, 3); g.poly([(9.2, 14), (14.8, 14), (12, 18.2)], INK)
    g.line([(6, 20.5), (18, 20.5)], ORANGE, 2); g.dot(12, 20.5, 1.7, ORANGE)


@icon('RobotCellIcon')
def _(g):
    g.rrect(3, 3, 21, 21, 3, SOFT, 1.6)
    g.line([(7, 17.5), (7, 13), (11.5, 8.5), (15, 11)], INK, 2); g.dot(7, 13, 1.7, ORANGE)
    g.line([(13, 17.5), (19, 17.5)], ORANGE, 2.2)


@icon('RobotProgramIcon')
def _(g):
    doc(g); g.line([(8, 16.5), (8, 13.5), (10.8, 10.8), (13.4, 12.6)], ORANGE, 1.8); g.dot(8, 13.5, 1.3, ORANGE)


def helix(n, turns=3.0, steps=720):
    """One continuous line climbing in loops: a print in vase mode. Returns (x, y, depth) in pixels."""
    pts = []
    for i in range(steps + 1):
        t = i / steps
        a = 2 * math.pi * turns * t - math.pi * 0.25
        pts.append((n * (0.5 + 0.30 * math.cos(a)), n * (0.735 - 0.40 * t + 0.085 * math.sin(a)), math.sin(a)))
    return pts


def logo(size):
    """Dark rounded square with ONE continuous line that climbs in loops and ends at the nozzle:
    the single-line print path this slicer is about. The newest loop is blue."""
    k = 4
    n = size * k
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    grad = Image.new('RGBA', (n, n))
    gd = ImageDraw.Draw(grad)
    top, bottom = (28, 36, 54), (10, 14, 24)
    for y in range(n):
        t = y / (n - 1)
        gd.line([(0, y), (n, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=int(n * 0.225), fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    w = n * 0.052
    pts = helix(n)
    last = len(pts) - 1
    # back halves first (dimmer), then the front halves on top, so the loops read as a coil
    for front in (False, True):
        for i, (x, y, depth) in enumerate(pts):
            if (depth >= 0) != front: continue
            t = i / last
            if t > 2.0 / 3.0: col = (10, 132, 255, 255) if front else (8, 92, 190, 255)
            else:
                v = int(150 + 105 * t * 1.5) if front else int(88 + 50 * t * 1.5)
                col = (v, v, min(255, v + 6), 255)
            d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=col)
    # nozzle: a white dot where the line ends
    x, y, _ = pts[-1]
    r = n * 0.045
    d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


def tab_icon():
    """24 px version for the Grasshopper tab: the three loops without the dark ground."""
    g = G()
    for cy, col in ((17.2, SOFT), (12.6, INK), (8, BLUE)):
        g.line(arc(12, cy, 8.4, 0, 360, 48, 3.1), col, 2, closed=True)
    return g.img


if __name__ == '__main__':
    for name, fn in ICONS.items():
        g = G(); fn(g); g.save(name + '.png')
    logo(256).save(os.path.join(HERE, '..', 'icon.png'))
    logo(256).save(os.path.join(OUT, 'plugin-icon.png'))
    logo(1024).save(os.path.join(HERE, '..', '..', 'docs', 'logo-1024.png'))
    tab_icon().resize((24, 24), Image.LANCZOS).save(os.path.join(OUT, 'LARGERslicer_24x24.png'))
    on_dark(tab_icon()).resize((24, 24), Image.LANCZOS).save(os.path.join(OUT, 'LARGERslicer_24x24.dark.png'))
    print(len(ICONS), 'icons, logo and tab icon written')
