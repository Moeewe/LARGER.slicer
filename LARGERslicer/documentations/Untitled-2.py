"""
GHPython: Bretter aus Brep – vollständige Stapellogik
======================================================
INPUTS:
    brep              : Brep
    dicke_erstes      : float   – Stärke erstes Brett (Standard: 35mm)
    dicke_letztes     : float   – Stärke letztes Brett (Standard: 35mm)
    dicke_mitte       : float   – Stärke Mittelbretter (Standard: 30mm)
    fuge_aktiv        : bool    – Fuge ein/aus (Standard: True)
    fuge_min          : float   – Mindestfuge (Standard: 4mm)
    fuge_position     : float   – 0-1 (Standard: 0.5=mitte)
    links             : float   – Überstand vorne (Standard: 0)
    rechts            : float   – Überstand hinten (Standard: 0)
    breiten_raster    : float   – Breite aufrunden (Standard: 50mm)
    laengen_raster    : float   – Länge aufrunden (Standard: 50mm)
    rand_min          : float   – Mindestrand (Standard: 5mm)
    max_breiten_typen : int     – Max. verschiedene Brettbreiten (0=unbegrenzt)
    min_breite        : float   – Mindestbrettbreite (Standard: 0)
    ruecken_gerade    : bool    – True = alle Bretter hinten bündig (Block), False = gestaffelt
    vorne_buendig     : bool    – Vorderkante alle bündig (Standard: True)
    hinten_buendig    : bool    – Hinterkante alle bündig (Standard: False)
    z_ueberstand_unten: float   – Z-Überstand unterstes Brett in -Z (Standard: 0)
    z_ueberstand_oben : float   – Z-Überstand oberstes Brett in +Z (Standard: 0)
    modus_ohne_fuge   : int     – 0=Auto(Rest auf Unterstes), 1=Keine Anpassung, 2=Erstes/Letztes fix + Mitte gleichmäßig
    achsen_korrektur  : int     – Manuelle 3D-Drehung der Bretter (0=Auto, 1=90°X, 2=90°Y, 3=90°Z, 4=90°X+90°Z, 5=90°Y+90°Z)
    orientierung      : int     – 0=Parallel (Bretter parallel zu Ober-/Unterseite), 1=Optimiert (3D-Rotation für min. BBox)

OUTPUTS:
    bretter        : Brep
    stueckliste    : Text
    info           : Text
    offset_kurven  : Curve (Debug)
    parameter_namen: Text [List]
    parameter_werte: Text [List]
"""

import Rhino.Geometry as rg
import math

# GH-Input-Fallbacks fuer Editor/Analyzer.
brep = globals().get("brep", None)
dicke_erstes = globals().get("dicke_erstes", None)
dicke_letztes = globals().get("dicke_letztes", None)
dicke_mitte = globals().get("dicke_mitte", None)
fuge_aktiv = globals().get("fuge_aktiv", None)
fuge_min = globals().get("fuge_min", None)
fuge_position = globals().get("fuge_position", None)
links = globals().get("links", None)
rechts = globals().get("rechts", None)
breiten_raster = globals().get("breiten_raster", None)
laengen_raster = globals().get("laengen_raster", None)
rand_min = globals().get("rand_min", None)
max_breiten_typen = globals().get("max_breiten_typen", None)
min_breite = globals().get("min_breite", None)
ruecken_gerade = globals().get("ruecken_gerade", None)
front_seite = globals().get("front_seite", None)
vorne_buendig = globals().get("vorne_buendig", None)
hinten_buendig = globals().get("hinten_buendig", None)
z_ueberstand_unten = globals().get("z_ueberstand_unten", None)
z_ueberstand_oben = globals().get("z_ueberstand_oben", None)
modus_ohne_fuge = globals().get("modus_ohne_fuge", None)
achsen_korrektur = globals().get("achsen_korrektur", None)
orientierung = globals().get("orientierung", None)

bretter       = []
stueckliste   = []
info          = []
offset_kurven = []
parameter_namen = []
parameter_werte = []

# ── Hilfsfunktionen ──────────────────────────────────────────────────────────

def schneide_brep(brep, z_hoehe, tol=0.01):
    ebene = rg.Plane(rg.Point3d(0, 0, z_hoehe), rg.Vector3d.ZAxis)
    ok, kurven, _ = rg.Intersect.Intersection.BrepPlane(brep, ebene, tol)
    if ok and kurven:
        joined = rg.Curve.JoinCurves(list(kurven), tol)
        return list(joined) if joined else []
    return []

def bbox_kurve(kurven_liste, x_axis, y_axis, n=100):
    all_xs, all_ys = [], []
    for k in kurven_liste:
        d = k.Domain
        for i in range(n + 1):
            p = rg.Vector3d(k.PointAt(d.ParameterAt(i / n)))
            all_xs.append(rg.Vector3d.Multiply(p, x_axis))
            all_ys.append(rg.Vector3d.Multiply(p, y_axis))
    if not all_xs:
        return None
    return min(all_xs), max(all_xs), min(all_ys), max(all_ys)

def laengsachse_aus_kurven(kurven_liste):
    vecs = []
    for k in kurven_liste:
        v = rg.Vector3d(k.PointAtEnd - k.PointAtStart)
        if v.Length < 1e-6:
            v = rg.Vector3d(k.PointAt(k.Domain.ParameterAt(0.5)) - k.PointAtStart)
        if v.Length < 1e-6: continue
        v.Z = 0
        if v.Length < 1e-6: continue
        v.Unitize()
        if vecs and rg.Vector3d.Multiply(v, vecs[0]) < 0:
            v = -v
        vecs.append(v)
    if not vecs:
        return rg.Vector3d.XAxis
    x = rg.Vector3d(sum(v.X for v in vecs)/len(vecs),
                    sum(v.Y for v in vecs)/len(vecs), 0)
    x.Unitize()
    return x


def min_bbox_achse(kurven_liste, schritt_grad=1.0):
    """
    Findet die Rotation um Z, die die minimale 2D-Bounding-Box (Fläche)
    aller übergebenen Kurven ergibt.  Die Bretter bleiben parallel
    zueinander – nur die gemeinsame Orientierung wird optimiert.

    Zwei Phasen:
      1. Grobe Suche in 1°-Schritten (0°–179°)
      2. Feine Suche ±2° um den besten Winkel in 0.1°-Schritten
    """
    # Punkte in 2D sammeln
    pts_x, pts_y = [], []
    for k in kurven_liste:
        d = k.Domain
        for i in range(101):
            p = k.PointAt(d.ParameterAt(i / 100.0))
            pts_x.append(p.X)
            pts_y.append(p.Y)
    if not pts_x:
        return rg.Vector3d.XAxis

    best_area = float('inf')
    best_angle = 0.0

    def _eval(angle):
        ca, sa = math.cos(angle), math.sin(angle)
        min_u =  float('inf')
        max_u = -float('inf')
        min_v =  float('inf')
        max_v = -float('inf')
        for j in range(len(pts_x)):
            u =  pts_x[j] * ca + pts_y[j] * sa
            v = -pts_x[j] * sa + pts_y[j] * ca
            if u < min_u: min_u = u
            if u > max_u: max_u = u
            if v < min_v: min_v = v
            if v > max_v: max_v = v
        return (max_u - min_u) * (max_v - min_v)

    # Phase 1 – grob
    n_steps = int(180.0 / schritt_grad)
    for i in range(n_steps):
        angle = math.radians(i * schritt_grad)
        area = _eval(angle)
        if area < best_area:
            best_area = area
            best_angle = angle

    # Phase 2 – fein (±2° in 0.1°-Schritten)
    fine_range = math.radians(schritt_grad * 2)
    fine_step = fine_range / 40.0
    for j in range(-20, 21):
        angle = best_angle + j * fine_step
        area = _eval(angle)
        if area < best_area:
            best_area = area
            best_angle = angle

    # Sicherstellen, dass X-Achse die LÄNGERE Richtung ist (= Brettlänge).
    # Sonst sind die Bretter um 90° gedreht.
    ca, sa = math.cos(best_angle), math.sin(best_angle)
    min_u =  float('inf')
    max_u = -float('inf')
    min_v =  float('inf')
    max_v = -float('inf')
    for j in range(len(pts_x)):
        u =  pts_x[j] * ca + pts_y[j] * sa
        v = -pts_x[j] * sa + pts_y[j] * ca
        if u < min_u: min_u = u
        if u > max_u: max_u = u
        if v < min_v: min_v = v
        if v > max_v: max_v = v
    if (max_v - min_v) > (max_u - min_u):
        best_angle += math.pi / 2.0

    x_axis = rg.Vector3d(math.cos(best_angle), math.sin(best_angle), 0)
    x_axis.Unitize()
    return x_axis

def auf_raster(wert, raster):
    if raster <= 0: return wert
    return math.ceil(wert / raster) * raster

def offset_nach_aussen(k, rm, x_axis, y_axis):
    """Beide Offset-Richtungen probieren – die mit größerer BBox = nach außen."""
    z_k = k.PointAtStart.Z
    off_plane = rg.Plane(rg.Point3d(0, 0, z_k), rg.Vector3d.ZAxis)
    best, best_span = None, -1
    for sign in [rm, -rm]:
        try:
            off = k.Offset(off_plane, sign, 0.1, rg.CurveOffsetCornerStyle.Sharp)
            if not off: continue
            bb = bbox_kurve(list(off), x_axis, y_axis)
            if not bb: continue
            span = (bb[1]-bb[0]) + (bb[3]-bb[2])
            if span > best_span:
                best_span = span
                best = list(off)
        except: pass
    return best


def clamp(v, vmin, vmax):
    return max(vmin, min(vmax, v))


def _unique_sorted(values, tol=1e-6):
    vals = sorted(values)
    out = []
    for v in vals:
        if not out or abs(v - out[-1]) > tol:
            out.append(v)
    return out


def probe_hoehen_fuer_brett(z_u, z_o, z_min_brep, z_max_brep, step_mm=5.0):
    """
    Erzeugt robuste Z-Schnitthoehen innerhalb des gueltigen Brep-Bereichs.
    Mehrere Schnitte pro Brett erfassen lokale Profilwechsel (z. B. Griffkante).
    """
    eps = 0.05
    z_lo = z_min_brep + eps
    z_hi = z_max_brep - eps
    if z_lo > z_hi:
        z_mid = 0.5 * (z_min_brep + z_max_brep)
        return [z_mid]

    zi_u = clamp(z_u, z_lo, z_hi)
    zi_o = clamp(z_o, z_lo, z_hi)
    if zi_o < zi_u:
        zi_u, zi_o = zi_o, zi_u

    span = max(0.0, zi_o - zi_u)
    step = max(1.0, float(step_mm))

    # Basis: gleichmaessige Schnitte ueber die Brettintervall-Hoehe.
    if span < 1e-6:
        samples = [zi_u]
    else:
        n = int(math.ceil(span / step)) + 1
        n = max(3, min(25, n))
        samples = [zi_u + (span * i / float(n - 1)) for i in range(n)]

    # Zusaetzliche Fokus-Schnitte nahe Unter-/Oberkante und in der Mitte.
    samples.extend([
        clamp(zi_u + 0.1, z_lo, z_hi),
        clamp(zi_u + 0.5, z_lo, z_hi),
        clamp(0.5 * (zi_u + zi_o), z_lo, z_hi),
        clamp(zi_o - 0.5, z_lo, z_hi),
        clamp(zi_o - 0.1, z_lo, z_hi),
    ])

    return _unique_sorted(samples, 1e-4)


def typ_anzeige(typ_key):
    """Mapping von internen Typ-Keys zu lesbaren Stuecklisten-Bezeichnungen."""
    if typ_key == "erstes":
        return "Unterstes"
    if typ_key == "letztes" or typ_key == "mitte_letztes":
        return "Oberstes"
    return "Mitte"


def mode_name_ohne_fuge(mode_val):
    if mode_val == 0:
        return "Auto (Rest auf Unterstes)"
    if mode_val == 1:
        return "Keine Anpassung"
    if mode_val == 2:
        return "Erstes/Letztes fix, Mitte gleichmaessig"
    return "Unbekannt"


def waehle_auto_mitte_anzahl(rest_fuer_mitte, d_mitte, min_dicke=0.5):
    """
    Waehlt fuer Auto-Modus die Mittelbrettanzahl so, dass das Sonderbrett
    moeglichst nahe an d_mitte liegt und nicht unnoetig zu dick wird.

    Rueckgabe:
        (n_mitte, rest)
        mit d_sonder = d_mitte + rest.
    """
    if d_mitte <= 0:
        return 1, 0.0

    n_floor = max(1, int(math.floor(rest_fuer_mitte / d_mitte)))
    n_ceil = max(1, int(math.ceil(rest_fuer_mitte / d_mitte)))
    kandidaten = sorted(set([n_floor, n_ceil]))

    best = None
    for n in kandidaten:
        rest = rest_fuer_mitte - n * d_mitte
        d_sonder = d_mitte + rest
        if d_sonder <= min_dicke:
            continue

        # Primär: Sonderbrett nahe an d_mitte.
        score = abs(d_sonder - d_mitte)
        # Sekundär: lieber etwas kleiner als zu dick.
        tie_break = 0 if d_sonder <= d_mitte else 1
        key = (score, tie_break)

        if best is None or key < best[0]:
            best = (key, n, rest)

    if best is not None:
        return best[1], best[2]

    # Fallback: floor verwenden.
    n = n_floor
    rest = rest_fuer_mitte - n * d_mitte
    return n, rest


def finde_optimale_3d_orientierung(brep, grob_deg=15.0, fein_deg=2.0, max_punkte=300):
    """
    Findet die 3D-Rotation (Euler ZYX), die das Volumen der achsausgerichteten
    Bounding Box des Breps minimiert.  Dadurch werden die Bretter optimal um
    das Bauteil orientiert – nicht nur in XY, sondern auch gekippt.

    Phase 1: Grobe Suche (15°-Schritte)
    Phase 2: Feine Suche (2°-Schritte im Umfeld)

    Rückgabe: (xform_hin, xform_zurueck, vol_original, vol_optimal, winkel_grad)
    """
    # Mesh-Vertices extrahieren für schnelle Auswertung
    meshes = rg.Mesh.CreateFromBrep(brep, rg.MeshingParameters.FastRenderMesh)
    raw_pts = []
    if meshes:
        for m in meshes:
            verts = m.Vertices
            for i in range(verts.Count):
                v = verts[i]
                raw_pts.append((float(v.X), float(v.Y), float(v.Z)))
    if len(raw_pts) < 4:
        bb = brep.GetBoundingBox(True)
        for c in bb.GetCorners():
            raw_pts.append((float(c.X), float(c.Y), float(c.Z)))

    # Subsampling für Performance
    if len(raw_pts) > max_punkte:
        step = max(1, len(raw_pts) // max_punkte)
        pts = raw_pts[::step]
    else:
        pts = list(raw_pts)

    n = len(pts)
    if n < 4:
        ident = rg.Transform.Identity
        return ident, ident, 0.0, 0.0, (0, 0, 0)

    # Schwerpunkt als Rotationszentrum
    cx = sum(p[0] for p in pts) / n
    cy = sum(p[1] for p in pts) / n
    cz = sum(p[2] for p in pts) / n
    centered = [(p[0] - cx, p[1] - cy, p[2] - cz) for p in pts]

    def bbox_vol(a, b, g):
        """BBox-Volumen für Euler-Rotation Rz(a) · Ry(b) · Rx(g)."""
        ca, sa = math.cos(a), math.sin(a)
        cb, sb = math.cos(b), math.sin(b)
        cg, sg = math.cos(g), math.sin(g)
        r00 = ca * cb;  r01 = ca * sb * sg - sa * cg;  r02 = ca * sb * cg + sa * sg
        r10 = sa * cb;  r11 = sa * sb * sg + ca * cg;  r12 = sa * sb * cg - ca * sg
        r20 = -sb;       r21 = cb * sg;                  r22 = cb * cg
        first = True
        xn = xp = yn = yp = zn = zp = 0.0
        for x, y, z in centered:
            rx = r00 * x + r01 * y + r02 * z
            ry = r10 * x + r11 * y + r12 * z
            rz = r20 * x + r21 * y + r22 * z
            if first:
                xn = xp = rx; yn = yp = ry; zn = zp = rz; first = False
            else:
                if rx < xn: xn = rx
                if rx > xp: xp = rx
                if ry < yn: yn = ry
                if ry > yp: yp = ry
                if rz < zn: zn = rz
                if rz > zp: zp = rz
        return (xp - xn) * (yp - yn) * (zp - zn)

    vol_orig = bbox_vol(0.0, 0.0, 0.0)
    best_vol = vol_orig
    best_a = best_b = best_g = 0.0

    # Phase 1: Grobe Suche
    gr = math.radians(grob_deg)
    na = int(180.0 / grob_deg)
    nb = int(90.0 / grob_deg) + 1
    ng = int(180.0 / grob_deg)
    for ai in range(na):
        a = ai * gr
        for bi in range(nb):
            b = bi * gr
            for gi in range(ng):
                g = gi * gr
                vol = bbox_vol(a, b, g)
                if vol < best_vol:
                    best_vol = vol
                    best_a, best_b, best_g = a, b, g

    # Phase 2: Feine Verfeinerung
    fr = math.radians(fein_deg)
    margin = int(grob_deg / fein_deg)
    sa_best, sb_best, sg_best = best_a, best_b, best_g
    for da in range(-margin, margin + 1):
        a = sa_best + da * fr
        for db in range(-margin, margin + 1):
            b = sb_best + db * fr
            for dg in range(-margin, margin + 1):
                g = sg_best + dg * fr
                vol = bbox_vol(a, b, g)
                if vol < best_vol:
                    best_vol = vol
                    best_a, best_b, best_g = a, b, g

    # Mindestens 0.5% Verbesserung nötig, sonst keine Rotation anwenden
    verbesserung = 1.0 - best_vol / vol_orig if vol_orig > 0 else 0.0
    winkel = (math.degrees(best_a), math.degrees(best_b), math.degrees(best_g))
    if verbesserung < 0.005:
        best_a = best_b = best_g = 0.0

    # ── BBox-Dimensionen nach Euler-Rotation berechnen ────────────────
    # Wird IMMER berechnet, auch für Identity (0,0,0).
    ca, sa = math.cos(best_a), math.sin(best_a)
    cb, sb = math.cos(best_b), math.sin(best_b)
    cg, sg = math.cos(best_g), math.sin(best_g)
    r00 = ca*cb;  r01 = ca*sb*sg - sa*cg;  r02 = ca*sb*cg + sa*sg
    r10 = sa*cb;  r11 = sa*sb*sg + ca*cg;  r12 = sa*sb*cg - ca*sg
    r20 = -sb;     r21 = cb*sg;              r22 = cb*cg
    xn = xp = yn = yp = zn = zp = 0.0
    first = True
    for x, y, z in centered:
        rx = r00*x + r01*y + r02*z
        ry = r10*x + r11*y + r12*z
        rz = r20*x + r21*y + r22*z
        if first:
            xn = xp = rx; yn = yp = ry; zn = zp = rz; first = False
        else:
            if rx < xn: xn = rx
            if rx > xp: xp = rx
            if ry < yn: yn = ry
            if ry > yp: yp = ry
            if rz < zn: zn = rz
            if rz > zp: zp = rz
    dx = xp - xn
    dy = yp - yn
    dz = zp - zn

    # Euler-Transforms bauen (ggf. Identity wenn keine Verbesserung)
    center = rg.Point3d(cx, cy, cz)
    if abs(best_a) < 1e-10 and abs(best_b) < 1e-10 and abs(best_g) < 1e-10:
        xform_hin = rg.Transform.Identity
        xform_zurueck = rg.Transform.Identity
    else:
        rx  = rg.Transform.Rotation(best_g,  rg.Vector3d.XAxis, center)
        ry  = rg.Transform.Rotation(best_b,  rg.Vector3d.YAxis, center)
        rz  = rg.Transform.Rotation(best_a,  rg.Vector3d.ZAxis, center)
        rxi = rg.Transform.Rotation(-best_g, rg.Vector3d.XAxis, center)
        ryi = rg.Transform.Rotation(-best_b, rg.Vector3d.YAxis, center)
        rzi = rg.Transform.Rotation(-best_a, rg.Vector3d.ZAxis, center)
        xform_hin = rz * ry * rx
        xform_zurueck = rxi * ryi * rzi

    # ── IMMER sicherstellen, dass Z die GRÖSSTE BBox-Dimension ist ──
    # Z = Stapelrichtung.  Je größer Z, desto MEHR Bretter und desto
    # KLEINER jedes einzelne Brett (X × Y).  Deshalb muss die längste
    # Achse in Z liegen – nicht die kürzeste.
    korrektur = None
    korrektur_inv = None
    dims = [(dx, 'x'), (dy, 'y'), (dz, 'z')]
    dims.sort(key=lambda t: t[0])          # aufsteigend
    groesste_achse = dims[2][1]            # größte Dimension

    # Effektive Dimensionen nach Korrektur mitführen
    eff_dx, eff_dy = dx, dy

    if groesste_achse == 'x':
        # X ist am größten → 90° um Y kippen (X→Z)
        korrektur = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.YAxis, center)
        korrektur_inv = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.YAxis, center)
        eff_dx, eff_dy = dz, dy
    elif groesste_achse == 'y':
        # Y ist am größten → 90° um X kippen (Y→Z)
        korrektur = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.XAxis, center)
        korrektur_inv = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.XAxis, center)
        eff_dx, eff_dy = dx, dz
    # else: Z ist bereits am größten → keine Korrektur nötig

    if korrektur is not None:
        xform_hin = korrektur * xform_hin
        xform_zurueck = xform_zurueck * korrektur_inv

    # ── Sicherstellen, dass X >= Y (Brettlänge = längste In-Plane-Achse) ─
    # Nach der Z-Korrektur kann die längste Dimension in Y gelandet sein.
    # Dann müssen X und Y noch getauscht werden (90° um Z).
    if eff_dx > eff_dy:
        z_swap = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.ZAxis, center)
        z_swap_inv = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.ZAxis, center)
        xform_hin = z_swap * xform_hin
        xform_zurueck = xform_zurueck * z_swap_inv

    return xform_hin, xform_zurueck, vol_orig, best_vol, winkel


# ── Hauptlogik ───────────────────────────────────────────────────────────────

try:
    if brep is None:
        raise Exception("Kein Brep verbunden!")

    import Rhino
    if not isinstance(brep, rg.Brep):
        if isinstance(brep, (list, tuple)): brep = brep[0]
        if hasattr(brep, 'Value'):    brep = brep.Value
        if hasattr(brep, 'Geometry'): brep = brep.Geometry
        if not isinstance(brep, rg.Brep):
            try:
                import System
                if isinstance(brep, System.Guid):
                    obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(brep)
                    if obj: brep = obj.Geometry
            except: pass
    if not isinstance(brep, rg.Brep):
        raise Exception("Input ist kein Brep (Typ: {})".format(type(brep).__name__))

    # Parameter
    _de  = float(dicke_erstes)    if dicke_erstes    is not None else 35.0
    _dl  = float(dicke_letztes)   if dicke_letztes   is not None else 35.0
    _dm  = float(dicke_mitte)     if dicke_mitte     is not None else 30.0
    _fm  = float(fuge_min)        if fuge_min        is not None else 4.0
    _fp  = max(0.0, min(1.0, float(fuge_position) if fuge_position is not None else 0.5))
    _fon = bool(fuge_aktiv)       if fuge_aktiv      is not None else True
    _l   = float(links)           if links           is not None else 0.0
    _r   = float(rechts)          if rechts          is not None else 0.0
    _br  = float(breiten_raster)  if breiten_raster  is not None else 50.0
    _lr  = float(laengen_raster)  if laengen_raster  is not None else 50.0
    _rm  = float(rand_min)        if rand_min        is not None else 5.0
    if _rm < 0.0:
        _rm = 0.0
    _mbt = int(max_breiten_typen) if max_breiten_typen is not None else 0
    _mb  = float(min_breite)      if min_breite      is not None else 0.0
    _rg  = bool(ruecken_gerade)   if ruecken_gerade  is not None else False
    _fs  = int(front_seite)       if front_seite     is not None else 0
    _vb  = bool(vorne_buendig)    if vorne_buendig   is not None else True
    _hb  = bool(hinten_buendig)   if hinten_buendig  is not None else False
    _zu  = float(z_ueberstand_unten) if z_ueberstand_unten is not None else 0.0
    _zo  = float(z_ueberstand_oben)  if z_ueberstand_oben  is not None else 0.0
    _mof = int(modus_ohne_fuge) if modus_ohne_fuge is not None else 0
    if _mof < 0 or _mof > 2:
        _mof = 0
    _ak = int(achsen_korrektur) if achsen_korrektur is not None else 0
    if _ak < 0 or _ak > 5:
        _ak = 0
    _ori = int(orientierung) if orientierung is not None else 1
    if _ori < 0 or _ori > 1:
        _ori = 1

    # Standardisierte Parameter-Ausgabe fuer Preset-Speicherung/Rekonstruktion.
    parameter_namen = [
        "u2.dicke_erstes",
        "u2.dicke_letztes",
        "u2.dicke_mitte",
        "u2.fuge_aktiv",
        "u2.fuge_min",
        "u2.fuge_position",
        "u2.links",
        "u2.rechts",
        "u2.breiten_raster",
        "u2.laengen_raster",
        "u2.rand_min",
        "u2.max_breiten_typen",
        "u2.min_breite",
        "u2.ruecken_gerade",
        "u2.front_seite",
        "u2.vorne_buendig",
        "u2.hinten_buendig",
        "u2.z_ueberstand_unten",
        "u2.z_ueberstand_oben",
        "u2.modus_ohne_fuge",
        "u2.achsen_korrektur",
        "u2.orientierung",
    ]
    parameter_werte = [
        _de,
        _dl,
        _dm,
        _fon,
        _fm,
        _fp,
        _l,
        _r,
        _br,
        _lr,
        _rm,
        _mbt,
        _mb,
        _rg,
        _fs,
        _vb,
        _hb,
        _zu,
        _zo,
        _mof,
        _ak,
        _ori,
    ]

    # ── Optimale 3D-Orientierung finden ───────────────────────────────────
    # Brep wird in optimaler Rotation bearbeitet, Ergebnisse am Ende zurückrotiert.
    brep = brep.Duplicate()
    if _ori == 1:
        xform_hin, xform_zurueck, vol_orig, vol_opt, rot_winkel = \
            finde_optimale_3d_orientierung(brep)
        brep.Transform(xform_hin)
    else:
        xform_hin = rg.Transform.Identity
        xform_zurueck = rg.Transform.Identity
        vol_orig = 0.0
        vol_opt = 0.0
        rot_winkel = (0, 0, 0)
        info.append("Orientierung: Parallel (keine 3D-Rotation)")

    # ── Manuelle Achsenkorrektur (0–5: verschiedene 3D-Orientierungen) ──
    # Dreht den Brep zusätzlich, sodass andere Achsen zur Stapelrichtung (Z) werden.
    # 0=Auto, 1=90°X, 2=90°Y, 3=90°Z, 4=90°X+90°Z, 5=90°Y+90°Z
    if _ak > 0 and _ori == 1:
        bb_tmp = brep.GetBoundingBox(True)
        ctr_pt = rg.Point3d(
            0.5 * (bb_tmp.Min.X + bb_tmp.Max.X),
            0.5 * (bb_tmp.Min.Y + bb_tmp.Max.Y),
            0.5 * (bb_tmp.Min.Z + bb_tmp.Max.Z))
        man_hin = rg.Transform.Identity
        man_zurueck = rg.Transform.Identity
        ak_label = ""
        if _ak == 1:
            man_hin = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.XAxis, ctr_pt)
            man_zurueck = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.XAxis, ctr_pt)
            ak_label = "90° um X"
        elif _ak == 2:
            man_hin = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.YAxis, ctr_pt)
            man_zurueck = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.YAxis, ctr_pt)
            ak_label = "90° um Y"
        elif _ak == 3:
            man_hin = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            man_zurueck = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            ak_label = "90° um Z"
        elif _ak == 4:
            rx_h = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.XAxis, ctr_pt)
            rz_h = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            man_hin = rz_h * rx_h
            rx_z = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.XAxis, ctr_pt)
            rz_z = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            man_zurueck = rx_z * rz_z
            ak_label = "90° um X + 90° um Z"
        elif _ak == 5:
            ry_h = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.YAxis, ctr_pt)
            rz_h = rg.Transform.Rotation(math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            man_hin = rz_h * ry_h
            ry_z = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.YAxis, ctr_pt)
            rz_z = rg.Transform.Rotation(-math.pi / 2.0, rg.Vector3d.ZAxis, ctr_pt)
            man_zurueck = ry_z * rz_z
            ak_label = "90° um Y + 90° um Z"
        brep.Transform(man_hin)
        xform_hin = man_hin * xform_hin
        xform_zurueck = xform_zurueck * man_zurueck
        info.append("Manuelle Achsenkorrektur: {} (Wert {})".format(ak_label, _ak))

    if vol_orig > 0 and vol_opt < vol_orig:
        einsparung_pct = (1.0 - vol_opt / vol_orig) * 100.0
        info.append("3D-Rotation: α={:.1f}° β={:.1f}° γ={:.1f}°  (BBox -{:.1f}%)".format(
            rot_winkel[0], rot_winkel[1], rot_winkel[2], einsparung_pct))
    else:
        info.append("3D-Rotation: keine Verbesserung, Originalausrichtung beibehalten")

    # ── Bounding Box des Breps ────────────────────────────────────────────
    bb = brep.GetBoundingBox(True)
    z_min = bb.Min.Z
    z_max = bb.Max.Z
    gesamt_hoehe = z_max - z_min
    bb_x = bb.Max.X - bb.Min.X
    bb_y = bb.Max.Y - bb.Min.Y
    info.append("BBox nach Rotation: X={:.1f}  Y={:.1f}  Z={:.1f} mm (Z=Stapelrichtung)".format(
        bb_x, bb_y, gesamt_hoehe))
    info.append("Gesamthöhe (Z): {:.1f} mm, Z-Überstand: unten={:.1f} oben={:.1f}".format(
        gesamt_hoehe, _zu, _zo))

    # ── Stapelplan ────────────────────────────────────────────────────────
    if _fon:
        # Mit Fuge: erstes + letztes Brett + Mittelbretter + Fuge
        min_h = _de + _dl + _fm
        if gesamt_hoehe < min_h:
            raise Exception("Zu niedrig für Erstes+Letztes+Fuge!")
        rest_fuer_mitte = gesamt_hoehe - _de - _dl
        n_mitte = int(rest_fuer_mitte / _dm)
        rest = gesamt_hoehe - _de - _dl - n_mitte * _dm
        if rest < _fm and n_mitte > 0:
            n_mitte -= 1
            rest = gesamt_hoehe - _de - _dl - n_mitte * _dm
        fuge_breite = rest
        info.append("Stapel (mit Fuge): 1×{:.0f} + {}×{:.0f} + Fuge {:.1f} + 1×{:.0f}".format(
            _de, n_mitte, _dm, fuge_breite, _dl))
    else:
        # Ohne Fuge: Steuerung ueber modus_ohne_fuge
        # 0 = Auto (Rest auf Unterstes)
        # 1 = Keine Anpassung
        # 2 = Erstes/Letztes fix, Mitte gleichmaessig
        rest_fuer_mitte = gesamt_hoehe - _de
        if rest_fuer_mitte <= 0.5:
            raise Exception("Zu niedrig fuer mindestens ein Brett nach dem ersten Brett!")

        n_mitte = 0
        rest = 0.0
        dicke_erstes_eff = _de
        dicke_mitte_eff = _dm
        dicke_sonder_auto = _dm

        if _mof == 0:
            # Auto: Mittelbrettanzahl so waehlen, dass das Sonderbrett
            # moeglichst nahe an _dm liegt (bei Bedarf lieber zusaetzliches Brett
            # und kleineres Sonderbrett statt zu dickes Brett).
            n_mitte, rest = waehle_auto_mitte_anzahl(rest_fuer_mitte, _dm, 0.5)
            dicke_sonder_auto = _dm + rest
            if dicke_sonder_auto <= 0.5:
                raise Exception("Auto-Modus ungueltig: Sonderbrett wird <= 0.5 mm")

            info.append("Stapel (ohne Fuge, {}): 1×{:.0f} + {}×{:.0f} + Sonderbrett {:.1f} (Rest {:+.1f})".format(
                mode_name_ohne_fuge(_mof), _de, n_mitte - 1 if n_mitte > 0 else 0, _dm, dicke_sonder_auto, rest))

        elif _mof == 1:
            # Keine Anpassung: Dicke von erstem/mittleren/letztem Brett bleibt exakt wie vorgegeben.
            # Zur sicheren Umfassung wird die Anzahl Mittelbretter so gewaehlt,
            # dass die Gesamthoehe mindestens erreicht wird (kein offenes Reststueck oben).
            rest_zwischen = gesamt_hoehe - _de - _dl
            if rest_zwischen < -0.5:
                raise Exception("Modus 1 ungueltig: Erstes+Letztes groesser als Gesamthoehe")

            n_mitte = max(0, int(math.ceil(max(0.0, rest_zwischen) / _dm)))
            rest = gesamt_hoehe - (_de + n_mitte * _dm + _dl)
            info.append("Stapel (ohne Fuge, {}): 1×{:.0f} + {}×{:.0f} + 1×{:.0f}  (Delta {:+.1f})".format(
                mode_name_ohne_fuge(_mof), _de, n_mitte, _dm, _dl, rest))

        else:
            # Erstes/Letztes fix, Mitte gleichmaessig verteilen.
            rest_zwischen = gesamt_hoehe - _de - _dl
            if rest_zwischen <= 0.5:
                raise Exception("Modus 2 ungueltig: Kein Platz zwischen erstem und letztem Brett")

            # Anzahl Mittelbretter nahe an Ziel _dm, Dicke dann exakt gleichmaessig.
            n_mitte = max(1, int(round(rest_zwischen / _dm)))
            dicke_mitte_eff = rest_zwischen / float(n_mitte)
            if dicke_mitte_eff <= 0.5:
                raise Exception("Modus 2 ungueltig: Gleichmaessige Mitteldicke <= 0.5 mm")

            rest = 0.0
            info.append("Stapel (ohne Fuge, {}): 1×{:.0f} + {}×{:.2f} + 1×{:.0f}".format(
                mode_name_ohne_fuge(_mof), _de, n_mitte, dicke_mitte_eff, _dl))

        fuge_breite = 0.0

    stapel = []
    z_cursor = z_min

    # Erstes Brett
    if _fon:
        stapel.append((z_cursor, _de, "erstes"))
        z_cursor += _de
    else:
        stapel.append((z_cursor, _de, "erstes"))
        z_cursor += _de

    if _fon:
        # Mit Fuge
        fuge_nach = max(0, min(n_mitte, int(round(_fp * n_mitte))))
        for i in range(n_mitte):
            if i == fuge_nach: z_cursor += fuge_breite
            stapel.append((z_cursor, _dm, "mitte"))
            z_cursor += _dm
        if fuge_nach == n_mitte: z_cursor += fuge_breite
        # Letztes Brett
        stapel.append((z_cursor, _dl, "letztes"))
    else:
        # Ohne Fuge
        if _mof == 2:
            for i in range(n_mitte):
                # Modus 2: nur das explizite letzte Brett ist "letztes".
                # Die Mittelbretter bleiben immer "mitte", sonst entstehen
                # zwei Oberste und doppelter oberer Z-Ueberstand.
                typ_i = "mitte"
                stapel.append((z_cursor, dicke_mitte_eff, typ_i))
                z_cursor += dicke_mitte_eff

            stapel.append((z_cursor, _dl, "letztes"))
            z_cursor += _dl
        elif _mof == 1:
            for i in range(n_mitte):
                stapel.append((z_cursor, _dm, "mitte"))
                z_cursor += _dm

            # Modus 1 hat immer ein explizites letztes Brett.
            stapel.append((z_cursor, _dl, "letztes"))
            z_cursor += _dl
        else:
            for i in range(n_mitte):
                d_i = _dm
                typ_i = "mitte"

                # Oberstes Brett im Modus ohne Fuge immer markieren,
                # damit Z-Ueberstand oben konsistent angewendet wird.
                if i == n_mitte - 1:
                    typ_i = "mitte_letztes"
                    if _mof == 0:
                        d_i = dicke_sonder_auto

                stapel.append((z_cursor, d_i, typ_i))
                z_cursor += d_i
    info.append("Bretter im Stapel: {}  Fuge: {}".format(
        len(stapel), "{:.1f}mm".format(fuge_breite) if _fon else "aus"))
    if not _fon:
        info.append("Ohne-Fuge-Modus: {} ({})".format(_mof, mode_name_ohne_fuge(_mof)))
    info.append("Z-Ueberstand: unten={:+.1f} mm, oben={:+.1f} mm".format(_zu, _zo))

    # Hoehen-Check: Sollhoehe (Brep) vs. geplante Stapelhoehe.
    stapel_hoehe_geplant = sum(d for _, d, _ in stapel)
    delta_hoehe = gesamt_hoehe - stapel_hoehe_geplant
    info.append("Hoehencheck: Soll={:.1f}  Plan={:.1f}  Delta={:+.2f} mm".format(
        gesamt_hoehe, stapel_hoehe_geplant, delta_hoehe))
    if abs(delta_hoehe) > 1.0:
        info.append("WARNUNG: Hoehenabweichung > 1.0 mm")

    eff_gesamt_hoehe = gesamt_hoehe + _zu + _zo
    info.append("Hoehencheck effektiv (mit Z-Ueberstand): {:.1f} mm".format(eff_gesamt_hoehe))

    # ── Längsachse & globale Vorderkante ─────────────────────────────────
    alle_kurven = []
    for z_u, d, typ in stapel:
        for z in [z_u + d*0.1, z_u + d*0.9]:
            alle_kurven.extend(schneide_brep(brep, z))
    if not alle_kurven:
        raise Exception("Keine Schnittkurven gefunden!")

    x_axis = min_bbox_achse(alle_kurven)
    y_axis = rg.Vector3d(-x_axis.Y, x_axis.X, 0)
    winkel_grad = math.degrees(math.atan2(x_axis.Y, x_axis.X))
    info.append("X-Achse: {:.2f} {:.2f}  (Winkel: {:.1f}°, min. BBox)".format(
        x_axis.X, x_axis.Y, winkel_grad))

    all_xs = []
    for k in alle_kurven:
        d = k.Domain
        for i in range(101):
            all_xs.append(rg.Vector3d.Multiply(
                rg.Vector3d(k.PointAt(d.ParameterAt(i/100))), x_axis))

    # Rohe X-Ausdehnung – wird später durch Offset-Kurven verfeinert
    gx_raw_min = min(all_xs)
    gx_raw_max = max(all_xs)

    # Vorderkante: erst nach Offset-Berechnung endgültig setzen (siehe unten)
    # Vorläufig für Offset-Richtungsberechnung verwenden
    gx_front_vorlaeufig = gx_raw_min - _l
    info.append("X-Achse: {:.2f} {:.2f}  X-roh: {:.1f}–{:.1f}".format(
        x_axis.X, x_axis.Y, gx_raw_min, gx_raw_max))

    # ── Pro Brett: Breite + X-Ausdehnung aus Offset-Kurven berechnen ─────
    brett_daten  = []
    stapel_info  = []
    alle_off_min_x = []
    alle_off_max_x = []

    for z_u, dicke_brett, typ in stapel:
        z_o = z_u + dicke_brett

        # Mehrfach-Abtastung ueber die Bretthoehe statt nur 2 Schnitte.
        # Damit werden lokale Profilwechsel (z. B. Griffkanten) sicher in der
        # Offset-Huelle erfasst.
        z_probes = probe_hoehen_fuer_brett(z_u, z_o, z_min, z_max, 5.0)

        alle_ks = []
        for zp in z_probes:
            alle_ks.extend(schneide_brep(brep, zp))
        if not alle_ks:
            info.append("WARNUNG: Keine Kontur Z={:.0f}-{:.0f}".format(z_u, z_o))
            continue

        # Offset nach außen
        offset_ks = []
        for k in alle_ks:
            off = offset_nach_aussen(k, _rm, x_axis, y_axis)
            if off:
                offset_ks.extend(off)
                offset_kurven.extend(off)
            else:
                offset_ks.append(k)
                offset_kurven.append(k)

        bb_raw = bbox_kurve(alle_ks, x_axis, y_axis)
        bb_off = bbox_kurve(offset_ks, x_axis, y_axis) or bb_raw
        if bb_off is None:
            info.append("WARNUNG: BBox fehlt Z={:.0f}".format(z_u))
            continue

        min_x, max_x, min_y, max_y = bb_off

        # X-Richtung (Brettlänge): NUR Rohkurven + rand_min verwenden.
        # offset_nach_aussen mit Sharp-Ecken erzeugt an scharfen Profilkanten
        # extrem große Ausbuchtungen in X, die die Brettlänge aufblähen.
        # Y-Richtung (Brettbreite): Offset-Kurven beibehalten für korrekte Abdeckung.
        if bb_raw is not None:
            raw_min_x, raw_max_x, raw_min_y, raw_max_y = bb_raw
            min_x = raw_min_x - _rm
            max_x = raw_max_x + _rm
            if _rm > 0.0:
                min_y = min(min_y, raw_min_y - _rm)
                max_y = max(max_y, raw_max_y + _rm)

        alle_off_min_x.append(min_x)
        alle_off_max_x.append(max_x)

        # Breite
        breite_roh = max_y - min_y
        breite = auf_raster(breite_roh, _br)
        if _mb > 0 and breite < _mb:
            breite = auf_raster(_mb, _br) if _br > 0 else _mb

        if _fs == 0:
            y_vorne  = min_y
            y_hinten = y_vorne + breite
        else:
            y_vorne  = max_y
            y_hinten = y_vorne - breite
        my = (y_vorne + y_hinten) / 2.0

        # min_x / max_x pro Brett speichern → Länge nach Offset-Basis
        stapel_info.append((z_u, dicke_brett, typ, breite_roh, breite, my, min_x, max_x))

    # ── X-Richtung: kurze Enden → immer global bündig (links/rechts) ─────
    if stapel_info:
        # Kurze Enden = X-Richtung: global für ALLE Bretter gleich
        gx_front = min(alle_off_min_x) - _l   # linkes Ende = globales Min - links
        gx_back  = max(alle_off_max_x) + _r   # rechtes Ende = globales Max + rechts
        glen_global = auf_raster(gx_back - gx_front, _lr)
        # Raster-Überschuss symmetrisch verteilen
        extra = (glen_global - (gx_back - gx_front)) / 2.0
        gx_front -= extra
        gx_back  += extra
        mx_global = (gx_front + gx_back) / 2.0
        info.append("X kurze Enden: {:.1f} bis {:.1f}  L={:.0f}".format(
            gx_front, gx_back, glen_global))

        # ── Y-Richtung: vorne/hinten bündig ──────────────────────────────
        # Globale Y-Ausdehnung über alle Bretter
        # Tuple: (z_u, d, typ, br_roh, breite, my, min_x, max_x)
        # my ist aktuell pro Brett; für bündig: globales min_y / max_y berechnen
        # Dazu stapel_info noch mit min_y/max_y erweitern
        stapel_info_neu = []
        for a, b, c, d, e, my, min_x_b, max_x_b in stapel_info:
            breite = e
            # Vorderkante und Hinterkante in Y aus my und breite zurückrechnen
            if _fs == 0:
                y_v = my - breite/2.0
                y_h = my + breite/2.0
            else:
                y_v = my + breite/2.0
                y_h = my - breite/2.0
            stapel_info_neu.append((a, b, c, d, e, my, y_v, y_h, glen_global, mx_global))
        stapel_info = stapel_info_neu

        # ── Y-Richtung: saubere Raster-Logik ────────────────────────────
        # Vorderseite (frei): EXAKT an Offset-Kurve (rand_min schon drin, kein Raster)
        # Hinterseite (bündig): globales Maximum → einmal auf Raster aufrunden
        if _fs == 0:
            gy_hinten_global = auf_raster(max(s[7] for s in stapel_info), _br) if _hb else None
            gy_vorne_global  = min(s[6] for s in stapel_info) if _vb else None
        else:
            gy_hinten_global = auf_raster(min(s[7] for s in stapel_info), _br) if _hb else None
            gy_vorne_global  = max(s[6] for s in stapel_info) if _vb else None

        info.append("Y: vorne={} hinten={}".format(
            "{:.1f}".format(gy_vorne_global)  if gy_vorne_global  is not None else "exakt/Brett",
            "{:.1f}".format(gy_hinten_global) if gy_hinten_global is not None else "Raster/Brett"))

        stapel_info_final = []
        for z_u, d_brett, typ, br_roh, breite, my_alt, y_v, y_h, glen, mx in stapel_info:
            # Vorderseite: exakt (oder global wenn vorne_buendig)
            new_y_v = gy_vorne_global if gy_vorne_global is not None else y_v
            # Hinterseite: global gerundetes Max (oder Brett-eigene Hinterkante auf Raster)
            if gy_hinten_global is not None:
                new_y_h = gy_hinten_global
            else:
                # Vorderseite fix, Breite = Abstand zur Hinterkante auf Raster aufrunden
                dist = abs(y_h - new_y_v)
                dist_gerundet = auf_raster(dist, _br)
                new_y_h = new_y_v + dist_gerundet if _fs == 0 else new_y_v - dist_gerundet

            new_breite = abs(new_y_h - new_y_v)
            new_my = (new_y_v + new_y_h) / 2.0
            stapel_info_final.append((z_u, d_brett, typ, br_roh, new_breite, new_my, glen, mx))

        stapel_info = stapel_info_final

    # ── Max Breiten-Typen ─────────────────────────────────────────────────
    if _mbt > 0:
        unique_breiten = sorted(set(s[4] for s in stapel_info))
        if len(unique_breiten) > _mbt:
            b_min, b_max = unique_breiten[0], unique_breiten[-1]
            stufen = [b_max] if _mbt == 1 else sorted(set(
                auf_raster(b_min + i*(b_max-b_min)/(_mbt-1), _br)
                for i in range(_mbt)))
            def naechste_stufe(b):
                for s in stufen:
                    if s >= b: return s
                return stufen[-1]
            stapel_info = [(a,b,c,d,naechste_stufe(e),f,g,h)
                           for a,b,c,d,e,f,g,h in stapel_info]
            info.append("Breiten → {} Typen: {}".format(
                len(set(s[4] for s in stapel_info)),
                sorted(set(s[4] for s in stapel_info))))

    # ── Breps erstellen ───────────────────────────────────────────────────
    min_eff_dicke = 0.5

    def z_bounds_for_typ(z_u_nominal, d_nominal, typ_key):
        z0 = z_u_nominal
        z1 = z_u_nominal + d_nominal

        if typ_key == "erstes":
            z0 -= _zu
        if typ_key == "letztes" or typ_key == "mitte_letztes":
            z1 += _zo

        return z0, z1

    verlegeplan = []
    for z_u, dicke_brett, typ, breite_roh, breite, my, glen, mx in stapel_info:
        z_geom_u, z_geom_o = z_bounds_for_typ(z_u, dicke_brett, typ)
        dicke_eff = z_geom_o - z_geom_u
        if dicke_eff <= min_eff_dicke:
            raise Exception(
                "Z-Ueberstand macht Brettdicke ungueltig: typ='{}', nominal={:.2f}, effektiv={:.2f} mm".format(
                    typ, dicke_brett, dicke_eff
                )
            )

        z_mitte = 0.5 * (z_geom_u + z_geom_o)
        mitte = rg.Point3d(
            x_axis.X*mx + y_axis.X*my,
            x_axis.Y*mx + y_axis.Y*my,
            z_mitte)
        ebene = rg.Plane(mitte, x_axis, y_axis)
        box = rg.Box(ebene,
                     rg.Interval(-glen/2, glen/2),
                     rg.Interval(-breite/2, breite/2),
                     rg.Interval(-dicke_eff/2, dicke_eff/2))
        b = box.ToBrep()
        if b and b.IsValid:
            typ_text = typ_anzeige(typ)
            bretter.append(b)
            brett_daten.append((int(round(glen)), int(round(breite)),
                                int(round(dicke_eff)), typ_text))
            verlegeplan.append((
                z_geom_u,
                z_geom_o,
                int(round(glen)),
                int(round(breite)),
                int(round(dicke_eff)),
                typ_text
            ))
            info.append("{:12s} Z={:5.0f}-{:5.0f}  B={:.0f}  L={:.0f}  D={:.0f}".format(
                typ_text, z_geom_u, z_geom_o, breite, glen, dicke_eff))

    # ── Rücktransformation in Originalausrichtung ─────────────────────────
    for i in range(len(bretter)):
        bretter[i].Transform(xform_zurueck)
    for i in range(len(offset_kurven)):
        offset_kurven[i].Transform(xform_zurueck)

    # ── Stückliste ────────────────────────────────────────────────────────
    from collections import Counter
    zaehler = Counter(brett_daten)
    stueckliste.append("=" * 52)
    stueckliste.append("STÜCKLISTE BRETTER")
    stueckliste.append("=" * 52)
    stueckliste.append("{:<5} {:<13} {:>8} {:>8} {:>8}".format(
        "Anz.", "Typ", "L(mm)", "B(mm)", "D(mm)"))
    stueckliste.append("-" * 52)
    gesamt = 0
    typ_order = {"Oberstes": 0, "Mitte": 1, "Unterstes": 2}
    for (l, b, d, typ), anz in sorted(
        zaehler.items(),
        key=lambda x: (typ_order.get(x[0][3], 99), x[0][0], x[0][1], x[0][2])
    ):
        stueckliste.append("{:<5} {:<13} {:>8} {:>8} {:>8}".format(anz, typ, l, b, d))
        gesamt += anz
    stueckliste.append("-" * 52)
    stueckliste.append("Gesamt: {} Bretter  ({} Typen)".format(gesamt, len(zaehler)))
    stueckliste.append("Fuge: {}".format("{:.1f} mm".format(fuge_breite) if _fon else "keine"))
    stueckliste.append("Hoehencheck: Soll={:.1f}  Plan={:.1f}  Delta={:+.2f} mm".format(
        gesamt_hoehe, stapel_hoehe_geplant, delta_hoehe))

    stueckliste.append("")
    stueckliste.append("VERLEGEPLAN (unten -> oben)")
    stueckliste.append("-" * 52)
    stueckliste.append("{:<4} {:<11} {:>8} {:>8} {:>8} {:>16}".format(
        "Pos", "Typ", "L(mm)", "B(mm)", "D(mm)", "Z(mm)"))

    for i, (z_u, z_o, l_i, b_i, d_i, typ_i) in enumerate(
        sorted(verlegeplan, key=lambda x: x[0]),
        start=1
    ):
        z_text = "{:.1f}-{:.1f}".format(z_u, z_o)
        stueckliste.append("{:<4} {:<11} {:>8} {:>8} {:>8} {:>16}".format(
            i, typ_i, l_i, b_i, d_i, z_text))

except Exception as e:
    import traceback
    info.append("FEHLER: " + str(e))
    info.append(traceback.format_exc())