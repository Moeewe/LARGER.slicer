"""
GHPython: Geometrie ausrichten + Bounding Box

INPUTS:
    bretter           : GeometryBase [List Access]
    randelemente      : GeometryBase [List Access]
    urspruengliche_solid : Brep/GeometryBase [Item or List]
    modus             : int   (0 = grosse Planflaeche flach auf Boden,
                               1 = erst BBox-rechtwinklig (Z-Rotation), dann Tilt auf minimale Hoehe)
    suchwinkel_min    : float (nur modus 1, Grad; z.B. -85)
    suchwinkel_max    : float (nur modus 1, Grad; z.B.  85)
    schritt_grob      : float (nur modus 1, Grad; z.B. 2.0)
    schritt_fein      : float (nur modus 1, Grad; z.B. 0.25)
    andersrum         : bool  (True = Unterseite nach oben, 180° Flip um X)
    ansaug_breite     : float (optional, Standard 200)
    ansaug_hoehe      : float (optional, Standard 20)
    ansaug_z_offset   : float (optional, Standard 0; + nach oben, - nach unten)
    ansaug_innen_offset : float (optional, Standard 5; + nach innen, - nach aussen)
    rand_verlaengerung_vorne : float (optional, Standard 0)
    rand_verlaengerung_hinten: float (optional, Standard 0)
    rand_verlaengerung        : float (optional, Legacy-Sammelwert fuer vorne/hinten)
    randelemente_trim_aktiv   : bool  (optional, Standard True)
    trim_offset_xy            : float (optional, Standard 0; + nach aussen / - nach innen)
    trim_offset_z             : float (optional, Standard 10; + nach oben/unten / - nach innen)
    fraes_nullpunkt_offset_x : float (optional, Standard 0)
    fraes_nullpunkt_offset_y : float (optional, Standard 0)
    fraes_nullpunkt_offset_z : float (optional, Standard 0)

OUTPUTS:
    geo_out           : GeometryBase [List]
    geo_bretter_out   : GeometryBase [List]
    original_out      : GeometryBase [List]
    rand_out          : GeometryBase [List]
    ansaugbretter_out : Brep [List]
    fraes_nullpunkt   : Point3d
    bbox_out          : Brep
    bemaussung_massketten : LinearDimension [List]
    bemaussung_kurven : Curve [List]
    bemaussung_text   : TextDot [List]
    info              : Text [List]
    parameter_namen   : Text [List]
    parameter_werte   : Text [List]
"""

import math
import Rhino
import Rhino.Geometry as rg

# GH-Input-Fallbacks fuer Editor/Analyzer
bretter = globals().get("bretter", None)
randelemente = globals().get("randelemente", None)
urspruengliche_solid = globals().get("urspruengliche_solid", None)
# Rueckwaertskompatibel zu aelteren Definitionen
geometrien = globals().get("geometrien", None)
modus = globals().get("modus", None)
suchwinkel_min = globals().get("suchwinkel_min", None)
suchwinkel_max = globals().get("suchwinkel_max", None)
schritt_grob = globals().get("schritt_grob", None)
schritt_fein = globals().get("schritt_fein", None)
andersrum = globals().get("andersrum", None)
ansaug_breite = globals().get("ansaug_breite", None)
ansaug_hoehe = globals().get("ansaug_hoehe", None)
ansaug_z_offset = globals().get("ansaug_z_offset", None)
ansaug_innen_offset = globals().get("ansaug_innen_offset", None)
rand_verlaengerung_vorne = globals().get("rand_verlaengerung_vorne", None)
rand_verlaengerung_hinten = globals().get("rand_verlaengerung_hinten", None)
rand_verlaengerung = globals().get("rand_verlaengerung", None)
randelemente_trim_aktiv = globals().get("randelemente_trim_aktiv", None)
trim_offset_xy = globals().get("trim_offset_xy", None)
trim_offset_z = globals().get("trim_offset_z", None)
fraes_nullpunkt_offset_x = globals().get("fraes_nullpunkt_offset_x", None)
fraes_nullpunkt_offset_y = globals().get("fraes_nullpunkt_offset_y", None)
fraes_nullpunkt_offset_z = globals().get("fraes_nullpunkt_offset_z", None)

geo_out = []
geo_bretter_out = []
original_out = []
rand_out = []
ansaugbretter_out = []
fraes_nullpunkt = None
bbox_out = None
bemaussung_massketten = []
bemaussung_kurven = []
bemaussung_text = []
info = []
parameter_namen = []
parameter_werte = []


def to_list(x):
    if x is None:
        return []
    if isinstance(x, (list, tuple)):
        return [g for g in x if g is not None]
    return [x]


def to_bool(x, default=False):
    if x is None:
        return default
    if isinstance(x, bool):
        return x
    if isinstance(x, (int, float)):
        return x != 0
    try:
        s = str(x).strip().lower()
    except:
        return default
    if s in ("1", "true", "yes", "y", "on"):
        return True
    if s in ("0", "false", "no", "n", "off"):
        return False
    return default


def dup_geo(g):
    # Typ-spezifisch duplizieren, damit die Weltposition sicher erhalten bleibt.
    if isinstance(g, rg.Brep):
        return g.DuplicateBrep()
    if isinstance(g, rg.Curve):
        return g.DuplicateCurve()
    if isinstance(g, rg.Mesh):
        return g.DuplicateMesh()
    if isinstance(g, rg.Surface):
        return g.DuplicateSurface()
    if hasattr(g, "DuplicateGeometry"):
        return g.DuplicateGeometry()
    if hasattr(g, "Duplicate"):
        return g.Duplicate()
    if hasattr(g, "DuplicateBrep"):
        return g.DuplicateBrep()
    return None


def apply_transform(geos, xform):
    out = []
    for g in geos:
        d = dup_geo(g)
        if d is None:
            continue
        d.Transform(xform)
        out.append(d)
    return out


def duplicate_list(geos):
    out = []
    fallback_count = 0
    for g in geos:
        d = dup_geo(g)
        if d is None:
            # Fallback: Originalobjekt mitnehmen, damit es transformiert
            # und in der Bounding-Box sicher beruecksichtigt wird.
            d = g
            fallback_count += 1
        out.append(d)
    return out, fallback_count


def transform_in_place(geos, xform):
    for g in geos:
        try:
            g.Transform(xform)
        except:
            pass


def combined_bbox(geos):
    bb = None
    for g in geos:
        try:
            b = g.GetBoundingBox(True)
        except:
            continue
        if not b.IsValid:
            continue
        if bb is None:
            bb = b
        else:
            bb.Union(b)
    return bb


def largest_planar_face_normal(geos):
    best_area = -1.0
    best_n = None
    for g in geos:
        b = g if isinstance(g, rg.Brep) else None
        if b is None:
            continue
        for f in b.Faces:
            ok, plane = f.TryGetPlane()
            if not ok:
                continue
            amp = rg.AreaMassProperties.Compute(f)
            if amp is None:
                continue
            if amp.Area > best_area:
                best_area = amp.Area
                n = rg.Vector3d(plane.Normal)
                if n.IsTiny():
                    continue
                n.Unitize()
                best_n = n
    return best_n, best_area


def dominant_xy_dir_from_edges(geos):
    best_len = -1.0
    best_v = None

    for g in geos:
        if isinstance(g, rg.Brep):
            edges = g.Edges
            for e in edges:
                L = e.GetLength()
                if L <= best_len:
                    continue
                v = rg.Vector3d(e.PointAtEnd - e.PointAtStart)
                v.Z = 0.0
                if v.IsTiny():
                    continue
                best_len = L
                best_v = rg.Vector3d(v)

    if best_v is None:
        return rg.Vector3d.XAxis

    best_v.Unitize()
    if best_v.X < 0 or (abs(best_v.X) < 1e-9 and best_v.Y < 0):
        best_v = -best_v
    return best_v


def angle_to_x(v):
    return math.atan2(v.Y, v.X)


def rotate_geos(geos, angle_rad, axis_vec, center_pt):
    xform = rg.Transform.Rotation(angle_rad, axis_vec, center_pt)
    return apply_transform(geos, xform)


def rotate_in_place(geos, angle_rad, axis_vec, center_pt):
    xform = rg.Transform.Rotation(angle_rad, axis_vec, center_pt)
    transform_in_place(geos, xform)


def height_of_geos(geos):
    bb = combined_bbox(geos)
    if bb is None:
        return 1e18
    return bb.Max.Z - bb.Min.Z


def xy_bbox_area(geos):
    bb = combined_bbox(geos)
    if bb is None:
        return 1e18
    dx = bb.Max.X - bb.Min.X
    dy = bb.Max.Y - bb.Min.Y
    return max(0.0, dx * dy)


def search_best_yaw_for_bbox(geos, center, coarse_deg=2.0, fine_deg=0.25):
    """Findet die Z-Rotation, bei der die achsenparallele XY-BBox minimal ist."""
    c_step = max(0.25, abs(float(coarse_deg)))
    f_step = max(0.05, abs(float(fine_deg)))

    best_a = 0.0
    best_area = 1e18

    a = 0.0
    while a < 180.0 - 1e-9:
        ar = math.radians(a)
        test = rotate_geos(geos, ar, rg.Vector3d.ZAxis, center)
        ar_xy = xy_bbox_area(test)
        if ar_xy < best_area:
            best_area = ar_xy
            best_a = ar
        a += c_step

    # Feinsuche um den besten Grobwinkel
    best_deg = math.degrees(best_a)
    lo = best_deg - c_step
    hi = best_deg + c_step
    a = lo
    while a <= hi + 1e-9:
        ar = math.radians(a)
        test = rotate_geos(geos, ar, rg.Vector3d.ZAxis, center)
        ar_xy = xy_bbox_area(test)
        if ar_xy < best_area:
            best_area = ar_xy
            best_a = ar
        a += f_step

    return best_a, best_area


def normalize_angle_rad(a):
    while a > math.pi:
        a -= 2.0 * math.pi
    while a < -math.pi:
        a += 2.0 * math.pi
    return a


def dominant_vertical_face_normal_xy(geos):
    """Normal der groessten vertikalen Planflaeche (XY-Anteil), falls vorhanden."""
    best_area = -1.0
    best_n = None

    for g in geos:
        b = g if isinstance(g, rg.Brep) else None
        if b is None:
            continue
        for f in b.Faces:
            ok, plane = f.TryGetPlane()
            if not ok:
                continue

            n = rg.Vector3d(plane.Normal)
            if n.IsTiny():
                continue
            n.Unitize()

            # Vertikale Flaeche: Normal ist weitgehend horizontal.
            if abs(n.Z) > 0.25:
                continue

            amp = rg.AreaMassProperties.Compute(f)
            if amp is None:
                continue

            if amp.Area > best_area:
                nxy = rg.Vector3d(n.X, n.Y, 0.0)
                if nxy.IsTiny():
                    continue
                nxy.Unitize()
                best_area = amp.Area
                best_n = nxy

    return best_n, best_area


def search_best_tilt(geos, center, a_min_deg, a_max_deg, step_deg):
    best_h = 1e18
    best_axis = rg.Vector3d.XAxis
    best_a = 0.0

    if step_deg <= 0:
        step_deg = 1.0

    a = a_min_deg
    while a <= a_max_deg + 1e-9:
        ar = math.radians(a)
        for ax in (rg.Vector3d.XAxis, rg.Vector3d.YAxis):
            test = rotate_geos(geos, ar, ax, center)
            h = height_of_geos(test)
            if h < best_h:
                best_h = h
                best_axis = rg.Vector3d(ax)
                best_a = ar
        a += step_deg

    return best_axis, best_a, best_h


def create_linear_dimension(plane, p0, p1, dim_pt):
    """Erzeugt eine echte Maßkette (LinearDimension), wenn moeglich."""
    def to_p2d(pl, p3):
        try:
            ok, u, v = pl.ClosestParameter(p3)
            if ok:
                return rg.Point2d(u, v)
        except:
            pass
        return None

    p0_2 = to_p2d(plane, p0)
    p1_2 = to_p2d(plane, p1)
    pd_2 = to_p2d(plane, dim_pt)
    if p0_2 is None or p1_2 is None or pd_2 is None:
        return None

    try:
        d = rg.LinearDimension(plane, p0_2, p1_2, pd_2)
        if d is not None and d.IsValid:
            try:
                doc = Rhino.RhinoDoc.ActiveDoc
                if doc is not None:
                    d.DimensionStyleId = doc.DimStyles.CurrentDimensionStyleId
            except:
                pass
            return d
    except:
        pass

    try:
        d = rg.LinearDimension.Create(plane, p0_2, p1_2, pd_2)
        if d is not None and d.IsValid:
            try:
                doc = Rhino.RhinoDoc.ActiveDoc
                if doc is not None:
                    d.DimensionStyleId = doc.DimStyles.CurrentDimensionStyleId
            except:
                pass
            return d
    except:
        pass
    return None


def sample_points_for_geo(g):
    pts = []
    try:
        if isinstance(g, rg.Brep):
            mp = rg.MeshingParameters.FastRenderMesh
            meshes = rg.Mesh.CreateFromBrep(g, mp)
            if meshes:
                for m in meshes:
                    for v in m.Vertices:
                        pts.append(rg.Point3d(v.X, v.Y, v.Z))
        elif isinstance(g, rg.Mesh):
            for v in g.Vertices:
                pts.append(rg.Point3d(v.X, v.Y, v.Z))
    except:
        pass
    return pts


def narrowest_vertical_center_z(g, axis_is_x, bb, side_sign=1, bins=36):
    """Z-Mitte an der Stelle mit minimalem Abstand zwischen Ober-/Unterkontur."""
    pts_all = sample_points_for_geo(g)
    if len(pts_all) < 8:
        return 0.5 * (bb.Min.Z + bb.Max.Z)

    # Nur Punkte nahe der relevanten Aussenflaeche verwenden (nicht Dickenmitte).
    if axis_is_x:
        q_min = bb.Min.Y
        q_max = bb.Max.Y
    else:
        q_min = bb.Min.X
        q_max = bb.Max.X
    q_span = q_max - q_min
    q_face = q_max if side_sign >= 0 else q_min
    q_tol = max(0.5, 0.15 * max(q_span, 0.0))

    pts = []
    for p in pts_all:
        q = p.Y if axis_is_x else p.X
        if abs(q - q_face) <= q_tol:
            pts.append(p)

    if len(pts) < 8:
        pts = pts_all

    t_min = bb.Min.X if axis_is_x else bb.Min.Y
    t_max = bb.Max.X if axis_is_x else bb.Max.Y
    span = t_max - t_min
    if span <= 1e-9:
        return 0.5 * (bb.Min.Z + bb.Max.Z)

    bins = max(8, int(bins))
    zvals = [[] for _ in range(bins)]

    for p in pts:
        t = p.X if axis_is_x else p.Y
        u = (t - t_min) / span
        if u < 0.0:
            u = 0.0
        if u > 1.0:
            u = 1.0
        i = int(u * (bins - 1))
        zvals[i].append(p.Z)

    best_i = -1
    best_th = 1e99
    best_zc = None
    for i in range(bins):
        if len(zvals[i]) < 6:
            continue

        # Getrimmte Min/Max-Werte vermeiden Ausreisser durch Seitflaechen/Triangulation.
        zs = sorted(zvals[i])
        n = len(zs)
        lo_i = int(0.1 * (n - 1))
        hi_i = int(0.9 * (n - 1))
        zlo = zs[lo_i]
        zhi = zs[hi_i]
        th = zhi - zlo
        if th <= 1e-9:
            continue
        if th < best_th:
            best_th = th
            best_i = i
            best_zc = 0.5 * (zlo + zhi)

    if best_i < 0 or best_zc is None:
        return 0.5 * (bb.Min.Z + bb.Max.Z)

    return best_zc


def create_suction_boards(
    rand_geos,
    global_center,
    length_ref_bbox=None,
    width=200.0,
    height=20.0,
    z_offset=0.0,
    inside_offset=5.0,
    extend_front=0.0,
    extend_back=0.0,
):
    """Erzeugt horizontale Ansaugbretter ausserhalb der Randelemente."""
    boards = []
    w = max(0.0, float(width))
    h = max(0.0, float(height))
    z_off = float(z_offset)
    in_off = float(inside_offset)
    ext_front = max(0.0, float(extend_front))
    ext_back = max(0.0, float(extend_back))
    if w <= 1e-9 or h <= 1e-9:
        return boards

    use_ref_len = length_ref_bbox is not None and length_ref_bbox.IsValid

    for g in rand_geos:
        try:
            bb = g.GetBoundingBox(True)
        except:
            continue
        if not bb.IsValid:
            continue

        dx = bb.Max.X - bb.Min.X
        dy = bb.Max.Y - bb.Min.Y
        if dx <= 1e-9 and dy <= 1e-9:
            continue

        cx = 0.5 * (bb.Min.X + bb.Max.X)
        cy = 0.5 * (bb.Min.Y + bb.Max.Y)

        if dx >= dy:
            # Element verlaeuft hauptsaechlich in X -> Brett ausserhalb in Y.
            if use_ref_len:
                x0 = length_ref_bbox.Min.X - ext_back
                x1 = length_ref_bbox.Max.X + ext_front
            else:
                x0 = bb.Min.X - ext_back
                x1 = bb.Max.X + ext_front
            if cy >= global_center.Y:
                # Aussenmass bleibt = w; Innen-Offset verschiebt nur die innere Kante.
                y0 = bb.Max.Y - in_off
                y1 = bb.Max.Y + w
            else:
                # Aussenmass bleibt = w; Innen-Offset verschiebt nur die innere Kante.
                y0 = bb.Min.Y - w
                y1 = bb.Min.Y + in_off

            zc = 0.5 * (bb.Min.Z + bb.Max.Z) + z_off
            z0 = zc - 0.5 * h
            z1 = zc + 0.5 * h
        else:
            # Element verlaeuft hauptsaechlich in Y -> Brett ausserhalb in X.
            if use_ref_len:
                y0 = length_ref_bbox.Min.Y - ext_back
                y1 = length_ref_bbox.Max.Y + ext_front
            else:
                y0 = bb.Min.Y - ext_back
                y1 = bb.Max.Y + ext_front
            if cx >= global_center.X:
                # Aussenmass bleibt = w; Innen-Offset verschiebt nur die innere Kante.
                x0 = bb.Max.X - in_off
                x1 = bb.Max.X + w
            else:
                # Aussenmass bleibt = w; Innen-Offset verschiebt nur die innere Kante.
                x0 = bb.Min.X - w
                x1 = bb.Min.X + in_off

            zc = 0.5 * (bb.Min.Z + bb.Max.Z) + z_off
            z0 = zc - 0.5 * h
            z1 = zc + 0.5 * h

        p0 = rg.Point3d(min(x0, x1), min(y0, y1), min(z0, z1))
        p1 = rg.Point3d(max(x0, x1), max(y0, y1), max(z0, z1))
        bb_board = rg.BoundingBox(p0, p1)
        if not bb_board.IsValid:
            continue

        b = rg.Box(rg.Plane.WorldXY, bb_board).ToBrep()
        if b is not None and b.IsValid:
            boards.append(b)

    return boards


def rect_curve_from_bbox_xy(bb):
    pts = [
        rg.Point3d(bb.Min.X, bb.Min.Y, 0.0),
        rg.Point3d(bb.Max.X, bb.Min.Y, 0.0),
        rg.Point3d(bb.Max.X, bb.Max.Y, 0.0),
        rg.Point3d(bb.Min.X, bb.Max.Y, 0.0),
        rg.Point3d(bb.Min.X, bb.Min.Y, 0.0),
    ]
    pl = rg.Polyline(pts)
    return pl.ToNurbsCurve()


def board_footprint_curves_xy(board_geos):
    curves = []
    for g in board_geos:
        try:
            bb = g.GetBoundingBox(True)
        except:
            continue
        if not bb.IsValid:
            continue
        if (bb.Max.X - bb.Min.X) <= 1e-9 or (bb.Max.Y - bb.Min.Y) <= 1e-9:
            continue
        c = rect_curve_from_bbox_xy(bb)
        if c is not None and c.IsValid:
            curves.append(c)
    return curves


def union_closed_curves_xy(curves, tol):
    if not curves:
        return []
    try:
        unions = rg.Curve.CreateBooleanUnion(curves, tol)
        if unions:
            return [c for c in unions if c is not None and c.IsValid]
    except:
        pass
    # Fallback: ungeunionte Kurven verwenden.
    return [c for c in curves if c is not None and c.IsValid]


def offset_closed_curves_xy(curves, d, tol):
    if abs(d) <= 1e-9:
        return curves
    out = []
    for c in curves:
        try:
            offs = c.Offset(rg.Plane.WorldXY, d, tol, rg.CurveOffsetCornerStyle.Sharp)
        except:
            offs = None
        if not offs:
            continue
        for oc in offs:
            if oc is not None and oc.IsValid:
                out.append(oc)
    return out if out else curves


def extrude_curves_z_to_breps(curves, z0, z1, cap=True):
    breps = []
    h = z1 - z0
    if abs(h) <= 1e-9:
        return breps
    v = rg.Vector3d(0.0, 0.0, h)
    for c in curves:
        c0 = c.DuplicateCurve()
        if c0 is None:
            continue
        c0.Transform(rg.Transform.Translation(0.0, 0.0, z0))
        try:
            ex = rg.Extrusion.Create(c0, h, cap)
        except:
            ex = None
        if ex is None:
            continue
        b = ex.ToBrep()
        if b is not None and b.IsValid:
            breps.append(b)
    return breps


def trim_edge_elements_by_boards(rand_geos, board_geos, z_min, z_max, offset_xy_val, tol):
    """Trimmt Randelemente auf die XY-Kontur der Bretter (saubere Fraeskontur)."""
    if not rand_geos or not board_geos:
        return list(rand_geos), ["Trim: uebersprungen (fehlende Rand- oder Brettgeometrie)"]

    src_curves = board_footprint_curves_xy(board_geos)
    if not src_curves:
        return list(rand_geos), ["Trim: uebersprungen (keine gueltigen Brett-Footprints)"]

    union_curves = union_closed_curves_xy(src_curves, tol)
    trim_curves = offset_closed_curves_xy(union_curves, float(offset_xy_val), tol)

    z0 = z_min - 50.0
    z1 = z_max + 50.0
    cutters = extrude_curves_z_to_breps(trim_curves, z0, z1, cap=True)
    if not cutters:
        return list(rand_geos), ["Trim: uebersprungen (Cutting-Breps konnten nicht erzeugt werden)"]

    out = []
    trimmed_ok = 0
    kept_orig = 0

    for g in rand_geos:
        b = g if isinstance(g, rg.Brep) else None
        if b is None:
            out.append(g)
            kept_orig += 1
            continue

        try:
            res = rg.Brep.CreateBooleanIntersection([b], cutters, tol)
        except:
            res = None

        if res and len(res) > 0:
            best = None
            best_vol = -1.0
            for rb in res:
                if rb is None or not rb.IsValid:
                    continue
                vm = rg.VolumeMassProperties.Compute(rb)
                if vm is not None:
                    score = vm.Volume
                else:
                    bb = rb.GetBoundingBox(True)
                    score = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z)
                if score > best_vol:
                    best_vol = score
                    best = rb
            if best is not None:
                out.append(best)
                trimmed_ok += 1
                continue

        out.append(b)
        kept_orig += 1

    msg = "Trim: {} von {} Randelement(en) beschnitten, {} unveraendert".format(
        trimmed_ok, len(rand_geos), kept_orig)
    return out, [msg]


def trim_edge_elements_by_common_bbox(rand_geos, ref_geos, offset_xy_val, offset_z_val, tol):
    """Trimmt Randelemente mit einem sauberen Box-Volumen aus gemeinsamer BBox von Referenzgeometrien."""
    if not rand_geos or not ref_geos:
        return list(rand_geos), ["Trim(BBox): uebersprungen (fehlende Rand- oder Referenzgeometrie)"]

    bb = combined_bbox(ref_geos)
    if bb is None or not bb.IsValid:
        return list(rand_geos), ["Trim(BBox): uebersprungen (ungueltige Referenz-BBox)"]

    off = float(offset_xy_val)
    off_z = float(offset_z_val)
    dx = bb.Max.X - bb.Min.X
    dy = bb.Max.Y - bb.Min.Y
    dz = bb.Max.Z - bb.Min.Z
    # Bei negativem Offset vor invertierter Box schuetzen.
    off_min = -0.5 * min(dx, dy) + max(tol, 1e-6)
    off_z_min = -0.5 * dz + max(tol, 1e-6)
    if off < off_min:
        off = off_min
    if off_z < off_z_min:
        off_z = off_z_min
    z_pad = max(tol * 2.0, 0.1)

    p0 = rg.Point3d(bb.Min.X - off, bb.Min.Y - off, bb.Min.Z - z_pad - off_z)
    p1 = rg.Point3d(bb.Max.X + off, bb.Max.Y + off, bb.Max.Z + z_pad + off_z)
    trim_bb = rg.BoundingBox(p0, p1)
    if not trim_bb.IsValid:
        return list(rand_geos), ["Trim(BBox): uebersprungen (Trim-Box ungueltig)"]

    trim_box = rg.Box(rg.Plane.WorldXY, trim_bb).ToBrep()
    if trim_box is None or not trim_box.IsValid:
        return list(rand_geos), ["Trim(BBox): uebersprungen (Trim-Volumen ungueltig)"]

    out = []
    trimmed_ok = 0
    kept_orig = 0

    for g in rand_geos:
        b = g if isinstance(g, rg.Brep) else None
        if b is None:
            out.append(g)
            kept_orig += 1
            continue

        try:
            res = rg.Brep.CreateBooleanIntersection([b], [trim_box], tol)
        except:
            res = None

        if res and len(res) > 0:
            best = None
            best_vol = -1.0
            for rb in res:
                if rb is None or not rb.IsValid:
                    continue
                vm = rg.VolumeMassProperties.Compute(rb)
                score = vm.Volume if vm is not None else 0.0
                if score > best_vol:
                    best_vol = score
                    best = rb
            if best is not None:
                out.append(best)
                trimmed_ok += 1
                continue

        out.append(b)
        kept_orig += 1

    msg = "Trim(BBox): {} von {} Randelement(en) beschnitten, {} unveraendert".format(
        trimmed_ok, len(rand_geos), kept_orig)
    msg2 = "Trim(BBox)-Ref: X={:.2f}..{:.2f}, Y={:.2f}..{:.2f}, Z={:.2f}..{:.2f}".format(
        trim_bb.Min.X, trim_bb.Max.X, trim_bb.Min.Y, trim_bb.Max.Y, trim_bb.Min.Z, trim_bb.Max.Z)
    return out, [msg, msg2]


try:
    brett_geos = to_list(bretter)
    rand_geos = to_list(randelemente)
    original_geos = to_list(urspruengliche_solid)

    n_brett = len(brett_geos)
    n_rand = len(rand_geos)
    n_orig = len(original_geos)

    geos_all = brett_geos + rand_geos + original_geos
    if not geos_all:
        geos_all = to_list(geometrien)
        brett_geos = list(geos_all)

    if not geos_all:
        raise Exception("Keine Geometrien verbunden")

    # Optimierung basiert auf Brettern; Randelemente folgen nur als Gruppe.
    geos_opt = brett_geos if brett_geos else list(geos_all)

    m = int(modus) if modus is not None else 1
    a_min = float(suchwinkel_min) if suchwinkel_min is not None else -85.0
    a_max = float(suchwinkel_max) if suchwinkel_max is not None else 85.0
    step_coarse = float(schritt_grob) if schritt_grob is not None else 2.0
    step_fine = float(schritt_fein) if schritt_fein is not None else 0.25
    reverse_orientation = to_bool(andersrum, False)
    suction_width = float(ansaug_breite) if ansaug_breite is not None else 200.0
    suction_height = float(ansaug_hoehe) if ansaug_hoehe is not None else 20.0
    suction_z_offset = float(ansaug_z_offset) if ansaug_z_offset is not None else 0.0
    suction_inside_offset = float(ansaug_innen_offset) if ansaug_innen_offset is not None else 5.0
    legacy_ext = float(rand_verlaengerung) if rand_verlaengerung is not None else 0.0
    suction_extend_front = float(rand_verlaengerung_vorne) if rand_verlaengerung_vorne is not None else legacy_ext
    suction_extend_back = float(rand_verlaengerung_hinten) if rand_verlaengerung_hinten is not None else legacy_ext
    trim_active = to_bool(randelemente_trim_aktiv, True)
    trim_xy = float(trim_offset_xy) if trim_offset_xy is not None else 0.0
    trim_z = float(trim_offset_z) if trim_offset_z is not None else 10.0
    np_off_x = float(fraes_nullpunkt_offset_x) if fraes_nullpunkt_offset_x is not None else 0.0
    np_off_y = float(fraes_nullpunkt_offset_y) if fraes_nullpunkt_offset_y is not None else 0.0
    np_off_z = float(fraes_nullpunkt_offset_z) if fraes_nullpunkt_offset_z is not None else 0.0

    # Standardisierte Parameter-Ausgabe fuer Preset-Speicherung/Rekonstruktion.
    parameter_namen = [
        "u3.modus",
        "u3.suchwinkel_min",
        "u3.suchwinkel_max",
        "u3.schritt_grob",
        "u3.schritt_fein",
        "u3.andersrum",
        "u3.ansaug_breite",
        "u3.ansaug_hoehe",
        "u3.ansaug_z_offset",
        "u3.ansaug_innen_offset",
        "u3.rand_verlaengerung_vorne",
        "u3.rand_verlaengerung_hinten",
        "u3.randelemente_trim_aktiv",
        "u3.trim_offset_xy",
        "u3.trim_offset_z",
        "u3.fraes_nullpunkt_offset_x",
        "u3.fraes_nullpunkt_offset_y",
        "u3.fraes_nullpunkt_offset_z",
        "u3.bretter_count",
        "u3.randelemente_count",
        "u3.original_count",
    ]
    parameter_werte = [
        m,
        a_min,
        a_max,
        step_coarse,
        step_fine,
        reverse_orientation,
        suction_width,
        suction_height,
        suction_z_offset,
        suction_inside_offset,
        suction_extend_front,
        suction_extend_back,
        trim_active,
        trim_xy,
        trim_z,
        np_off_x,
        np_off_y,
        np_off_z,
        len(brett_geos),
        len(rand_geos),
        len(original_geos),
    ]

    try:
        tol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance if Rhino.RhinoDoc.ActiveDoc is not None else 0.01
    except:
        tol = 0.01

    bb0 = combined_bbox(geos_opt)
    if bb0 is None:
        raise Exception("Bounding Box konnte nicht erzeugt werden")
    center = bb0.Center

    # Genau einmal duplizieren, danach alle Schritte in-place transformieren.
    work_all, dup_fallbacks = duplicate_list(geos_all)
    if not work_all:
        raise Exception("Geometrie konnte nicht dupliziert werden")
    if dup_fallbacks > 0:
        info.append("Hinweis: {} Geometrie(n) ohne Duplicate-Funktion direkt verwendet".format(dup_fallbacks))

    # Gruppen im kombinierten Array merken (fuer separate Outputs).
    if n_brett > 0:
        work_brett = work_all[:n_brett]
    else:
        work_brett = []

    if n_rand > 0:
        work_rand = work_all[n_brett:n_brett + n_rand]
    else:
        work_rand = []

    if n_orig > 0:
        work_orig = work_all[n_brett + n_rand:n_brett + n_rand + n_orig]
    else:
        work_orig = []

    # Optimierungsset aus den duplizierten Brettern aufbauen, nicht separat neu erzeugen.
    if work_brett:
        work_opt = work_brett
    else:
        work_opt = list(work_all)
    info.append("Gruppentransform aktiv: {} Geometrien, keine Vereinigung".format(len(work_all)))
    info.append("Optimierungsmenge: {} Brett-Geometrien".format(len(work_opt)))
    info.append("Mitgefuehrtes Original-Solid: {}".format(len(original_geos)))

    if m == 0:
        # Modus 0: groesste Planflaeche auf Boden legen
        n, area = largest_planar_face_normal(work_opt)
        if n is None:
            info.append("Modus 0: Keine Planflaeche gefunden -> Fallback auf Modus 1")
            m = 1
        else:
            z_up = rg.Vector3d.ZAxis
            z_down = rg.Vector3d(-rg.Vector3d.ZAxis)
            ang_up = rg.Vector3d.VectorAngle(n, z_up)
            ang_down = rg.Vector3d.VectorAngle(n, z_down)
            target = z_up if ang_up <= ang_down else z_down
            xf = rg.Transform.Rotation(n, target, center)
            transform_in_place(work_all, xf)
            info.append("Modus 0: Planflaeche ausgerichtet (Area={:.1f})".format(area))

    if m == 1:
        # Schritt 1: Gerade ausrichten.
        # Wenn moeglich ueber Randelemente: deren vertikale Flaechen parallel zur XZ-Ebene
        # (Normal entlang +/-Y). Sonst BBox-basierter Yaw.
        nxy, area_n = dominant_vertical_face_normal_xy(rand_geos)
        if nxy is not None:
            ang_n = math.atan2(nxy.Y, nxy.X)
            d_pos = normalize_angle_rad((math.pi * 0.5) - ang_n)
            d_neg = normalize_angle_rad((-math.pi * 0.5) - ang_n)
            yaw = d_pos if abs(d_pos) <= abs(d_neg) else d_neg
            rotate_in_place(work_all, yaw, rg.Vector3d.ZAxis, center)
            info.append("Modus 1: Geradeaus ueber Randelemente = {:.2f} deg (Area={:.2f})".format(
                math.degrees(yaw), area_n))
        else:
            yaw, best_xy_area = search_best_yaw_for_bbox(work_opt, center, step_coarse, step_fine)
            rotate_in_place(work_all, yaw, rg.Vector3d.ZAxis, center)
            info.append("Modus 1: BBox-Z-Rotation = {:.2f} deg  XY-Area={:.2f}".format(
                math.degrees(yaw), best_xy_area))

        # Schritt 2: gemeinsamer Tilt auf minimale Hoehe
        axis1, a1, h1 = search_best_tilt(work_opt, center, a_min, a_max, step_coarse)
        lo = math.degrees(a1) - step_coarse
        hi = math.degrees(a1) + step_coarse
        axis2, a2, h2 = search_best_tilt(work_opt, center, lo, hi, step_fine)

        rotate_in_place(work_all, a2, axis2, center)
        info.append("Modus 1: Tilt-Achse={}  Winkel={:.2f} deg  Hoehe={:.2f}".format(
            "X" if abs(axis2.X) > 0.5 else "Y", math.degrees(a2), h2))

    # Optionaler Umschalter: Unterseite nach oben (180° Flip um horizontale Achse).
    if reverse_orientation:
        rotate_in_place(work_all, math.pi, rg.Vector3d.XAxis, center)
        info.append("Unterseite nach oben: +180.00 deg um X")

    # Auf Boden legen (min Z = 0)
    bb1 = combined_bbox(work_all)
    if bb1 is None:
        raise Exception("Keine Bounding Box nach Ausrichtung")

    dz = -bb1.Min.Z
    move = rg.Transform.Translation(0.0, 0.0, dz)
    transform_in_place(work_all, move)

    # Ansaugbretter einmal vorab erzeugen (als Referenz fuer gemeinsamen Trim-BBox-Ansatz).
    bb_pre = combined_bbox(work_all)
    length_ref_bbox = combined_bbox(work_brett) if work_brett else None
    suction_boards_pre = create_suction_boards(
        work_rand,
        bb_pre.Center if bb_pre is not None else rg.Point3d.Origin,
        length_ref_bbox,
        suction_width,
        suction_height,
        suction_z_offset,
        suction_inside_offset,
        suction_extend_front,
        suction_extend_back,
    )

    # Randelemente ueber gemeinsame BBox von Brettern + Ansaugbrettern trimmen
    # (oben/unten = saubere Planflaechen).
    if trim_active:
        trim_refs = list(work_brett) + list(suction_boards_pre)
        if trim_refs:
            trimmed_rand, trim_msgs = trim_edge_elements_by_common_bbox(
                work_rand,
                trim_refs,
                trim_xy,
                trim_z,
                tol,
            )
            work_rand[:] = trimmed_rand
            if n_rand > 0:
                work_all[n_brett:n_brett + n_rand] = work_rand
            info.extend(trim_msgs)
        else:
            info.append("Trim(BBox): uebersprungen (keine Referenz aus Brettern/Ansaug)")
    else:
        info.append("Trim(BBox): deaktiviert")

    bb2 = combined_bbox(work_all)
    bbox = rg.BoundingBox(bb2.Min, bb2.Max)
    bbox_brep = rg.Box(rg.Plane.WorldXY, bbox).ToBrep()

    # Ansaugbretter aus finalen (ggf. getrimmten) Randelementen erzeugen.
    # Die Brett-Laenge orientiert sich an den mittleren Brettern (work_brett).
    suction_boards = create_suction_boards(
        work_rand,
        bb2.Center,
        length_ref_bbox,
        suction_width,
        suction_height,
        suction_z_offset,
        suction_inside_offset,
        suction_extend_front,
        suction_extend_back,
    )

    # Fraes-Nullpunkt: standardmaessig minimale BBox-Ecke (nach Bodenausrichtung) + Offsets.
    fraes_nullpunkt = rg.Point3d(bb2.Min.X + np_off_x, bb2.Min.Y + np_off_y, bb2.Min.Z + np_off_z)

    # L/B/H-Bemaussung an der World-XY-Bounding-Box
    dx = bb2.Max.X - bb2.Min.X
    dy = bb2.Max.Y - bb2.Min.Y
    dz_len = bb2.Max.Z - bb2.Min.Z
    off = max(5.0, 0.03 * max(dx, dy, dz_len))

    # Laenge (X)
    pL0 = rg.Point3d(bb2.Min.X, bb2.Min.Y - off, bb2.Min.Z)
    pL1 = rg.Point3d(bb2.Max.X, bb2.Min.Y - off, bb2.Min.Z)
    line_L = rg.LineCurve(pL0, pL1)
    dot_L = rg.TextDot("L = {:.2f}".format(dx), rg.Point3d(0.5 * (pL0.X + pL1.X), pL0.Y, pL0.Z))

    # Breite (Y)
    pB0 = rg.Point3d(bb2.Max.X + off, bb2.Min.Y, bb2.Min.Z)
    pB1 = rg.Point3d(bb2.Max.X + off, bb2.Max.Y, bb2.Min.Z)
    line_B = rg.LineCurve(pB0, pB1)
    dot_B = rg.TextDot("B = {:.2f}".format(dy), rg.Point3d(pB0.X, 0.5 * (pB0.Y + pB1.Y), pB0.Z))

    # Hoehe (Z)
    pH0 = rg.Point3d(bb2.Min.X - off, bb2.Max.Y + off, bb2.Min.Z)
    pH1 = rg.Point3d(bb2.Min.X - off, bb2.Max.Y + off, bb2.Max.Z)
    line_H = rg.LineCurve(pH0, pH1)
    dot_H = rg.TextDot("H = {:.2f}".format(dz_len), rg.Point3d(pH0.X, pH0.Y, 0.5 * (pH0.Z + pH1.Z)))

    # Echte Maßketten fuers Viewport
    dims = []

    # Laenge in XY-Ebene auf Z = bb2.Min.Z
    dimL_plane = rg.Plane(rg.Point3d(0.0, 0.0, bb2.Min.Z), rg.Vector3d.ZAxis)
    dimL = create_linear_dimension(dimL_plane, pL0, pL1, rg.Point3d(0.5 * (pL0.X + pL1.X), pL0.Y - 0.35 * off, pL0.Z))
    if dimL is not None:
        dims.append(dimL)

    # Breite in XY-Ebene auf Z = bb2.Min.Z
    dimB_plane = rg.Plane(rg.Point3d(0.0, 0.0, bb2.Min.Z), rg.Vector3d.ZAxis)
    dimB = create_linear_dimension(dimB_plane, pB0, pB1, rg.Point3d(pB0.X + 0.35 * off, 0.5 * (pB0.Y + pB1.Y), pB0.Z))
    if dimB is not None:
        dims.append(dimB)

    # Hoehe in YZ-Ebene (X konstant)
    xh = bb2.Min.X - off
    dimH_plane = rg.Plane(rg.Point3d(xh, bb2.Min.Y, bb2.Min.Z), rg.Vector3d.XAxis)
    pHz0 = rg.Point3d(xh, bb2.Max.Y + off, bb2.Min.Z)
    pHz1 = rg.Point3d(xh, bb2.Max.Y + off, bb2.Max.Z)
    pHzD = rg.Point3d(xh, bb2.Max.Y + off + 0.35 * off, 0.5 * (bb2.Min.Z + bb2.Max.Z))
    dimH = create_linear_dimension(dimH_plane, pHz0, pHz1, pHzD)
    if dimH is not None:
        dims.append(dimH)

    geo_out = work_all
    geo_bretter_out = work_brett
    original_out = work_orig
    rand_out = work_rand
    ansaugbretter_out = suction_boards
    bbox_out = bbox_brep
    bemaussung_massketten = dims
    bemaussung_kurven = [line_L, line_B, line_H]
    bemaussung_text = [dot_L, dot_B, dot_H]

    info.append("Finale Hoehe: {:.2f}".format(dz_len))
    info.append("BBox L/B/H: {:.2f} / {:.2f} / {:.2f}".format(dx, dy, dz_len))
    info.append("Maßketten erzeugt: {}".format(len(dims)))
    info.append("Ansaugbretter: {}  (Breite={:.1f}, Hoehe={:.1f}, Z-Offset={:.1f}, Innen-Offset={:.1f})".format(
        len(suction_boards), suction_width, suction_height, suction_z_offset, suction_inside_offset))
    info.append("Ansaug-Verlaengerung vorne/hinten: {:.1f} / {:.1f}".format(
        max(0.0, suction_extend_front), max(0.0, suction_extend_back)))
    info.append("Trim aktiv: {}  Trim-Offset-XY/Z: {:.1f} / {:.1f}".format(trim_active, trim_xy, trim_z))
    if length_ref_bbox is not None and length_ref_bbox.IsValid:
        info.append("Ansaug-Laenge nach Bretter-BBox: X={:.2f}..{:.2f}, Y={:.2f}..{:.2f}".format(
            length_ref_bbox.Min.X, length_ref_bbox.Max.X, length_ref_bbox.Min.Y, length_ref_bbox.Max.Y))
    else:
        info.append("Ansaug-Laenge: Fallback auf Randelement-BBox (keine Brett-Referenz)")
    info.append("Fraes-Nullpunkt: ({:.2f}, {:.2f}, {:.2f})".format(
        fraes_nullpunkt.X, fraes_nullpunkt.Y, fraes_nullpunkt.Z))
    info.append("Bodenkontakt: z_min={:.3f}".format(bb2.Min.Z))

except Exception as e:
    import traceback
    info.append("FEHLER: " + str(e))
    info.append(traceback.format_exc())
