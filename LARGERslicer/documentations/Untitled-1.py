"""
GHPython: Seitenplatten links/rechts mit Brett-Abzug

INPUTS:
    bretter           : Brep [List Access]
    ansaug_dicke      : float  (Dicke Außenplatte)
    rueckplatte_dicke : float  (Dicke Innenplatte)
    rand              : float  (Randzugabe um Brettstapel)
    glaettung         : float  (optional, aktuell nur Dokumentation)
    treppenausschnitt : float  (Taschentiefe in Innenplatte)

OUTPUTS:
    ansaugplatte      : Brep [2 Stück]
    rueckplatte       : Brep [2 Stück]
    outline           : Curve [2 Stück]
    info              : Text
    parameter_namen   : Text [List]
    parameter_werte   : Text [List]
"""

import Rhino.Geometry as rg

# VS Code/Pyflakes kennt GH-Inputs nicht. Diese Fallbacks verhindern
# "undefined name"-Meldungen außerhalb von Grasshopper.
bretter = globals().get("bretter", None)
ansaug_dicke = globals().get("ansaug_dicke", None)
rueckplatte_dicke = globals().get("rueckplatte_dicke", None)
rand = globals().get("rand", None)
glaettung = globals().get("glaettung", None)
treppenausschnitt = globals().get("treppenausschnitt", None)

ansaugplatte = []
rueckplatte = []
outline = []
info = []
parameter_namen = []
parameter_werte = []


def local_point(x, y, z, x_ax, y_ax, z_ax=None):
    if z_ax is None:
        return rg.Point3d(
            x_ax.X * x + y_ax.X * y,
            x_ax.Y * x + y_ax.Y * y,
            z
        )
    return rg.Point3d(
        x_ax.X * x + y_ax.X * y + z_ax.X * z,
        x_ax.Y * x + y_ax.Y * y + z_ax.Y * z,
        x_ax.Z * x + y_ax.Z * y + z_ax.Z * z
    )


def local_box(x_ctr, x_half, y_ctr, y_half, z_ctr, z_half, x_ax, y_ax, z_ax=None):
    origin = local_point(x_ctr, y_ctr, z_ctr, x_ax, y_ax, z_ax)
    plane = rg.Plane(origin, x_ax, y_ax)
    box = rg.Box(
        plane,
        rg.Interval(-x_half, x_half),
        rg.Interval(-y_half, y_half),
        rg.Interval(-z_half, z_half)
    )
    b = box.ToBrep()
    return b if (b and b.IsValid) else None


def brett_info(b, x_ax, y_ax, z_ax=None):
    pts = []

    # Praeziser als BoundingBox-Ecken: echte Brep-Vertices verwenden.
    try:
        for i in range(b.Vertices.Count):
            pts.append(b.Vertices[i].Location)
    except:
        pts = []

    # Fallback, falls keine Vertices verfuegbar sind.
    if not pts:
        bb = b.GetBoundingBox(True)
        pts = [
            bb.Min,
            bb.Max,
            rg.Point3d(bb.Min.X, bb.Max.Y, bb.Min.Z),
            rg.Point3d(bb.Max.X, bb.Min.Y, bb.Max.Z),
            rg.Point3d(bb.Min.X, bb.Min.Y, bb.Max.Z),
            rg.Point3d(bb.Max.X, bb.Max.Y, bb.Min.Z)
        ]

    xs = [rg.Vector3d.Multiply(rg.Vector3d(p), x_ax) for p in pts]
    ys = [rg.Vector3d.Multiply(rg.Vector3d(p), y_ax) for p in pts]
    if z_ax is not None:
        zs = [rg.Vector3d.Multiply(rg.Vector3d(p), z_ax) for p in pts]
    else:
        zs = [p.Z for p in pts]
    return {
        'x_min': min(xs), 'x_max': max(xs),
        'y_min': min(ys), 'y_max': max(ys),
        'z_min': min(zs), 'z_max': max(zs)
    }


def extrude_curve(kurve, vec, tol=0.01):
    if kurve is None:
        return None

    c = kurve.DuplicateCurve()
    if c is None:
        return None

    # Robuster: direkte Brep-Extrusion mit Endkappen.
    try:
        b = rg.Brep.CreateFromExtrusion(c, vec, True)
        if b and b.IsValid:
            if b.IsSolid:
                return b
            b2 = b.CapPlanarHoles(max(tol, 0.05))
            if b2 and b2.IsValid:
                return b2
    except:
        pass

    # Fallback auf alte Surface-Extrusion.
    srf = rg.Surface.CreateExtrusion(c, vec)
    if srf is None:
        return None
    brep = srf.ToBrep()
    if brep:
        brep = brep.CapPlanarHoles(max(tol, 0.05))
        if brep and (not brep.IsSolid):
            brep = brep.CapPlanarHoles(0.5)
    return brep if (brep and brep.IsValid) else None


def _as_brep_list(x):
    if x is None:
        return []
    if isinstance(x, (list, tuple)):
        return [b for b in x if b is not None]
    return [x]


def duplicate_breps(breps):
    out = []
    for b in _as_brep_list(breps):
        if b is None:
            continue
        try:
            c = b.DuplicateBrep()
            if c and c.IsValid:
                out.append(c)
        except:
            pass
    return out


def union_breps(breps, tol=0.1):
    bl = _as_brep_list(breps)
    if not bl:
        return []
    try:
        u = rg.Brep.CreateBooleanUnion(bl, tol)
        if u and len(u) > 0:
            return list(u)
    except:
        pass
    return bl


def bool_diff_all(bases, tools, tol=0.1):
    """
    Tools werden zuerst vereinigt, danach wird die Differenz auf ALLE Basis-Breps
    ausgefuehrt. Alle Resultat-Teile bleiben erhalten.
    """
    # Mit Duplikaten rechnen, damit keine Seiteneffekte zwischen links/rechts entstehen.
    base_list = duplicate_breps(bases)
    if not base_list:
        return []

    tool_list = union_breps(duplicate_breps(tools), tol)
    if not tool_list:
        return base_list

    out = []
    for b in base_list:
        # Direkter Sammel-Boolean gegen vereinige Tools
        try:
            r = rg.Brep.CreateBooleanDifference([b], tool_list, tol)
            if r and len(r) > 0:
                out.extend([ri for ri in r if ri is not None and ri.IsValid])
                continue
        except:
            pass

        # Fallback: sequenziell ueber alle Tools, dabei alle Teilkoerper mitnehmen
        pieces = [b]
        for t in tool_list:
            next_pieces = []
            for p in pieces:
                try:
                    rr = rg.Brep.CreateBooleanDifference([p], [t], tol)
                    if rr and len(rr) > 0:
                        next_pieces.extend([ri for ri in rr if ri is not None and ri.IsValid])
                    else:
                        next_pieces.append(p)
                except:
                    next_pieces.append(p)
            pieces = next_pieces
        out.extend([pi for pi in pieces if pi is not None and pi.IsValid])

    return out if out else base_list


def brep_volume(b):
    try:
        vm = rg.VolumeMassProperties.Compute(b)
        if vm:
            return max(0.0, vm.Volume)
    except:
        pass
    return 0.0


def total_volume(breps):
    s = 0.0
    for b in _as_brep_list(breps):
        s += brep_volume(b)
    return s


def filter_to_rueckplatte_xrange(parts, x_axis, y_axis, x_min, x_max, tol=0.5, z_axis=None):
    """Haelt nur Teile innerhalb der Rueckplatten-X-Spanne (mit Toleranz)."""
    kept = []
    for p in _as_brep_list(parts):
        if p is None or (not p.IsValid):
            continue
        try:
            bi = brett_info(p, x_axis, y_axis, z_axis)
            if bi['x_max'] < (x_min - tol):
                continue
            if bi['x_min'] > (x_max + tol):
                continue
            kept.append(p)
        except:
            pass
    return kept


def bool_intersection_all(bases, tools, tol=0.1):
    base_list = duplicate_breps(bases)
    tool_list = duplicate_breps(tools)
    if not base_list or not tool_list:
        return []

    out = []
    for b in base_list:
        try:
            r = rg.Brep.CreateBooleanIntersection([b], tool_list, tol)
            if r and len(r) > 0:
                out.extend([ri for ri in r if ri is not None and ri.IsValid])
        except:
            pass
    return out


def transformed_brep_copy(brep, xform):
    if brep is None:
        return None
    try:
        c = brep.DuplicateBrep()
    except:
        return None
    c.Transform(xform)
    return c if c.IsValid else None


def schnittkonturen_brett(brett, x_axis, y_axis, x_pos, tol=0.1, z_axis=None):
    origin = local_point(x_pos, 0.0, 0.0, x_axis, y_axis, z_axis)
    # WICHTIG: Ebene fuer Stirnseiten-Schnitt muss Normal = x_axis haben (YZ-Ebene).
    plane = rg.Plane(origin, x_axis)
    ok, kurven, _ = rg.Intersect.Intersection.BrepPlane(brett, plane, tol)
    if not (ok and kurven):
        return []
    joined = rg.Curve.JoinCurves(list(kurven), tol)
    if not joined:
        return []

    # WICHTIG: Alle geschlossenen Konturen mitnehmen (nicht nur die groesste).
    # So werden bei mehreren Solids / Mehrfachschnitten alle Taschen erzeugt.
    closed_curves = []
    for c in joined:
        if c is None or not c.IsClosed:
            continue
        closed_curves.append(c)

    # Falls nichts geschlossen ist, als Fallback die rohen Joins verwenden.
    return closed_curves if closed_curves else [c for c in joined if c is not None]


def verschiebe_kurve_in_x(kurve, delta_x, x_axis):
    if kurve is None:
        return None
    c = kurve.DuplicateCurve()
    xf = rg.Transform.Translation(x_axis * delta_x)
    c.Transform(xf)
    return c


def make_tasche_solid(kontur_front, side_sign, tiefe, x_axis):
    """Erzeugt einen Taschenkörper, der die Plattenfront sicher schneidet (beide Seiten)."""
    depth = max(0.1, float(tiefe))
    outside = 0.6  # Startpunkt leicht AUSSERHALB der Plattenfront

    # Von der Front erst nach außen, dann nach innen extrudieren.
    k = verschiebe_kurve_in_x(kontur_front, -side_sign * outside, x_axis)
    if k is None:
        return None

    # Extrusion geht nach innen durch die gewünschte Taschentiefe.
    return extrude_curve(k, x_axis * (side_sign * (depth + outside + 0.8)), 0.05)


def interpolierte_plattenkontur(infos_sorted, x_plane, rand_val, x_axis, y_axis, glaettung_val=0.0, tol=0.01, z_axis=None):
    if not infos_sorted:
        return None

    z_min = infos_sorted[0]['z_min'] - rand_val
    z_max = infos_sorted[-1]['z_max'] + rand_val

    # Stützstellen entlang der Lagen.
    g_abs = abs(float(glaettung_val))
    z_nodes_raw = [z_min]
    for bi in infos_sorted:
        z_nodes_raw.append(bi['z_min'])
        # Bei geringer Glaettung weniger Punkte -> sauberere Kontur ohne Zacken.
        if g_abs >= 1.0:
            z_nodes_raw.append(0.5 * (bi['z_min'] + bi['z_max']))
        z_nodes_raw.append(bi['z_max'])
    z_nodes_raw.append(z_max)

    # Nahezu gleiche Z-Werte zusammenfassen.
    z_nodes_raw.sort()
    z_nodes = []
    merge_tol = 0.5
    for z in z_nodes_raw:
        if (not z_nodes) or abs(z - z_nodes[-1]) > merge_tol:
            z_nodes.append(z)
        else:
            z_nodes[-1] = 0.5 * (z_nodes[-1] + z)

    edge_band = max(2.0, 0.2 * rand_val)

    def envelope_at_z(zv):
        # Alle Bretter berücksichtigen, die den Z-Wert (mit Band) überdecken.
        active = [bi for bi in infos_sorted if (bi['z_min'] - edge_band) <= zv <= (bi['z_max'] + edge_band)]
        if active:
            y_f = min(bi['y_min'] for bi in active) - rand_val
            y_b = max(bi['y_max'] for bi in active) + rand_val
        else:
            # Fallback: nächstes Brett über die Brettmitte.
            best = infos_sorted[0]
            best_d = 1e99
            for bi in infos_sorted:
                zc = 0.5 * (bi['z_min'] + bi['z_max'])
                d = abs(zv - zc)
                if d < best_d:
                    best_d = d
                    best = bi
            y_f = best['y_min'] - rand_val
            y_b = best['y_max'] + rand_val

        # Zusätzliche Aufweitung nahe Ober-/Unterkanten für größere Einzugsbereiche.
        edge_pull = max(0.5, 0.12 * rand_val)
        near_edge = False
        for bi in infos_sorted:
            if abs(zv - bi['z_min']) <= edge_band or abs(zv - bi['z_max']) <= edge_band:
                near_edge = True
                break
        if near_edge:
            y_f -= edge_pull
            y_b += edge_pull

        return y_f, y_b

    y_front = []
    y_back = []
    for z in z_nodes:
        yf, yb = envelope_at_z(z)
        y_front.append(yf)
        y_back.append(yb)

    # Punkte ausduennen, damit die Kontur nicht "zittert".
    def decimate_nodes(zs, yf, yb, dz_min):
        if len(zs) <= 3:
            return zs, yf, yb
        keep_i = [0]
        last = zs[0]
        for i in range(1, len(zs) - 1):
            if (zs[i] - last) >= dz_min:
                keep_i.append(i)
                last = zs[i]
        keep_i.append(len(zs) - 1)
        z2 = [zs[i] for i in keep_i]
        f2 = [yf[i] for i in keep_i]
        b2 = [yb[i] for i in keep_i]
        return z2, f2, b2

    dz_min = max(4.0, 0.12 * rand_val)
    if g_abs < 1.0:
        dz_min = max(dz_min, 10.0)
    z_nodes, y_front, y_back = decimate_nodes(z_nodes, y_front, y_back, dz_min)

    # Enden oben/unten nur lokal absichern (nicht global aufblasen).
    # So werden die Abschlusskanten erfasst, ohne riesige Fluegel zu erzeugen.
    if infos_sorted:
        n_end = min(2, len(infos_sorted))
        low_infos = infos_sorted[:n_end]
        high_infos = infos_sorted[-n_end:]

        end_extra = max(0.5, 0.08 * rand_val)

        low_front = min(bi['y_min'] for bi in low_infos) - rand_val - end_extra
        low_back = max(bi['y_max'] for bi in low_infos) + rand_val + end_extra
        high_front = min(bi['y_min'] for bi in high_infos) - rand_val - end_extra
        high_back = max(bi['y_max'] for bi in high_infos) + rand_val + end_extra

        y_front[0] = min(y_front[0], low_front)
        y_back[0] = max(y_back[0], low_back)
        y_front[-1] = min(y_front[-1], high_front)
        y_back[-1] = max(y_back[-1], high_back)

    # Leichtes Angleichen an den Enden, ohne starkes Aufweiten.
    if len(y_front) >= 4:
        blend = 0.22
        y_front[1] = (1.0 - blend) * y_front[1] + blend * y_front[0]
        y_back[1] = (1.0 - blend) * y_back[1] + blend * y_back[0]
        y_front[-2] = (1.0 - blend) * y_front[-2] + blend * y_front[-1]
        y_back[-2] = (1.0 - blend) * y_back[-2] + blend * y_back[-1]

    # Leichte, stabile Glättung ohne Spline-Wellen.
    # Auch bei glaettung=0 gibt es 1 Basispass fuer saubere Geometrie.
    passes = 1 + int(max(0, min(3, round(g_abs / 6.0))))
    alpha = 0.24

    def smooth(vals):
        arr = list(vals)
        if len(arr) < 3:
            return arr
        for _ in range(passes):
            nxt = list(arr)
            for i in range(1, len(arr) - 1):
                m = 0.5 * (arr[i - 1] + arr[i + 1])
                nxt[i] = (1.0 - alpha) * arr[i] + alpha * m
            arr = nxt
        return arr

    y_front = smooth(y_front)
    y_back = smooth(y_back)

    # Enden monotonisieren: verhindert kleine "Fuesse"/Spikes unten und oben.
    if len(y_front) >= 3:
        y_front[0] = min(y_front[0], y_front[1])
        y_back[0] = max(y_back[0], y_back[1])
        y_front[-1] = min(y_front[-1], y_front[-2])
        y_back[-1] = max(y_back[-1], y_back[-2])

    # Sicherstellen, dass Front immer vor Back liegt.
    for i in range(len(z_nodes)):
        if y_front[i] > y_back[i] - 0.5:
            mid = 0.5 * (y_front[i] + y_back[i])
            y_front[i] = mid - 0.25
            y_back[i] = mid + 0.25

    # Kleine Reserve an der Oberkante.
    top_safety = max(0.8, 0.18 * rand_val)
    y_front[-1] -= top_safety
    y_back[-1] += top_safety

    # Saubere, robuste Kontur als geschlossene Polyline (keine Spline-Artefakte).
    front_pts = [local_point(x_plane, y, z, x_axis, y_axis, z_axis) for y, z in zip(y_front, z_nodes)]
    back_pts = [local_point(x_plane, y, z, x_axis, y_axis, z_axis) for y, z in zip(reversed(y_back), reversed(z_nodes))]
    n_front = len(front_pts)

    ring = []
    for p in front_pts + back_pts:
        if not ring or p.DistanceTo(ring[-1]) > 1e-6:
            ring.append(p)

    if len(ring) < 3:
        return None

    # Ecken leicht abfasen (u.a. unten links/rechts), um harte Ausreisser zu vermeiden.
    def chamfer_corner(points, idx, dist):
        n = len(points)
        if n < 4:
            return points
        i_prev = (idx - 1) % n
        i_next = (idx + 1) % n
        p_prev = points[i_prev]
        p_cur = points[idx]
        p_next = points[i_next]

        v1 = p_prev - p_cur
        v2 = p_next - p_cur
        l1 = v1.Length
        l2 = v2.Length
        if l1 < dist * 1.2 or l2 < dist * 1.2:
            return points

        v1.Unitize()
        v2.Unitize()
        p1 = rg.Point3d(p_cur + v1 * dist)
        p2 = rg.Point3d(p_cur + v2 * dist)

        out = []
        for i, p in enumerate(points):
            if i == idx:
                out.append(p1)
                out.append(p2)
            else:
                out.append(p)
        return out

    # Reihenfolge: nach abnehmendem Index, damit Einfuegen fruehere Indizes nicht verschiebt.
    chamfer_d = max(1.0, 0.12 * rand_val)
    corner_ids = [len(ring) - 1, n_front, n_front - 1, 0]
    for cid in sorted([c for c in corner_ids if 0 <= c < len(ring)], reverse=True):
        ring = chamfer_corner(ring, cid, chamfer_d)

    # Kleine Kanten/Spikes entfernen.
    def clean_ring(points, min_edge):
        pts = list(points)
        for _ in range(3):
            if len(pts) < 4:
                break
            out = []
            n = len(pts)
            for i in range(n):
                p_prev = pts[(i - 1) % n]
                p_cur = pts[i]
                p_next = pts[(i + 1) % n]

                e1 = p_cur - p_prev
                e2 = p_next - p_cur
                l1 = e1.Length
                l2 = e2.Length
                if l1 < min_edge or l2 < min_edge:
                    continue

                u1 = rg.Vector3d(e1)
                u2 = rg.Vector3d(e2)
                u1.Unitize()
                u2.Unitize()
                dot = rg.Vector3d.Multiply(u1, u2)

                # Nahezu kollinear oder sehr spitze Kurz-Kante -> entfernen.
                if dot > 0.998:
                    continue
                if dot < -0.65 and min(l1, l2) < (2.5 * min_edge):
                    continue

                out.append(p_cur)

            if len(out) < 3:
                break
            pts = out
        return pts

    ring = clean_ring(ring, max(1.0, 0.08 * rand_val))
    if len(ring) < 3:
        return None

    if ring[0].DistanceTo(ring[-1]) > 1e-6:
        ring.append(ring[0])

    return rg.PolylineCurve(ring)


def oversized_rect_kontur(x_plane, y_min, y_max, z_min, z_max, extra_pad, x_axis, y_axis, z_axis=None):
    """Erzeugt eine bewusst uebergrosse Rechteckkontur in der YZ-Ebene."""
    yy0 = y_min - extra_pad
    yy1 = y_max + extra_pad
    zz0 = z_min - extra_pad
    zz1 = z_max + extra_pad

    p0 = local_point(x_plane, yy0, zz0, x_axis, y_axis, z_axis)
    p1 = local_point(x_plane, yy1, zz0, x_axis, y_axis, z_axis)
    p2 = local_point(x_plane, yy1, zz1, x_axis, y_axis, z_axis)
    p3 = local_point(x_plane, yy0, zz1, x_axis, y_axis, z_axis)

    pl = rg.Polyline([p0, p1, p2, p3, p0])
    return rg.PolylineCurve(pl)


try:
    if bretter is None:
        raise Exception("Keine Bretter!")

    blist = bretter if isinstance(bretter, (list, tuple)) else [bretter]
    blist = [b for b in blist if b is not None]
    if not blist:
        raise Exception("Leere Brettliste")

    _ad = abs(float(ansaug_dicke)) if ansaug_dicke is not None else 15.0
    _rd = abs(float(rueckplatte_dicke)) if rueckplatte_dicke is not None else 19.0
    _rand = abs(float(rand)) if rand is not None else 20.0
    _gl = float(glaettung) if glaettung is not None else 0.0
    _ta = abs(float(treppenausschnitt)) if treppenausschnitt is not None else 8.0

    # Standardisierte Parameter-Ausgabe fuer Preset-Speicherung/Rekonstruktion.
    parameter_namen = [
        "u1.ansaug_dicke",
        "u1.rueckplatte_dicke",
        "u1.rand",
        "u1.glaettung",
        "u1.treppenausschnitt",
        "u1.bretter_count",
    ]
    parameter_werte = [
        _ad,
        _rd,
        _rand,
        _gl,
        _ta,
        len(blist),
    ]

    # ── Alle 3 Achsen aus den Brettkanten bestimmen ────────────────────
    # Bei 3D-rotierten Brettern ist die Stapelrichtung nicht mehr Welt-Z.
    # Deshalb: X = Brettlänge (längste Kante), Y = Brettbreite (mittlere),
    # Z = Brettdicke/Stapelrichtung (kürzeste Kante).
    edge_groups = []  # [[Richtung, max_Länge], ...]
    for b in blist:
        for edge in b.Edges:
            L = edge.GetLength()
            if L < 0.1:
                continue
            v = rg.Vector3d(edge.PointAtEnd - edge.PointAtStart)
            v.Unitize()
            found = False
            for g in edge_groups:
                if abs(rg.Vector3d.Multiply(v, g[0])) > 0.95:
                    if L > g[1]:
                        g[1] = L
                        if rg.Vector3d.Multiply(v, g[0]) < 0:
                            g[0] = -v
                        else:
                            g[0] = rg.Vector3d(v)
                    found = True
                    break
            if not found:
                edge_groups.append([rg.Vector3d(v), L])

    edge_groups.sort(key=lambda g: g[1], reverse=True)

    if len(edge_groups) >= 3:
        x_axis = edge_groups[0][0]  # längste = Brettlänge
        y_axis = edge_groups[1][0]  # mittlere = Brettbreite
        z_axis = edge_groups[2][0]  # kürzeste = Dicke/Stapelrichtung
    elif len(edge_groups) == 2:
        x_axis = edge_groups[0][0]
        z_axis = edge_groups[1][0]
        y_axis = rg.Vector3d.CrossProduct(z_axis, x_axis)
        y_axis.Unitize()
    else:
        x_axis = rg.Vector3d.XAxis
        y_axis = rg.Vector3d.YAxis
        z_axis = rg.Vector3d.ZAxis

    # Konsistente Orientierung sicherstellen
    x_axis.Unitize()
    y_axis.Unitize()
    z_axis.Unitize()
    if x_axis.X < 0 or (abs(x_axis.X) < 0.01 and x_axis.Y < 0):
        x_axis = -x_axis

    # Rechtshändiges System sicherstellen
    cross = rg.Vector3d.CrossProduct(x_axis, y_axis)
    if rg.Vector3d.Multiply(cross, z_axis) < 0:
        z_axis = -z_axis
    # Y aus Kreuzprodukt für exakte Orthogonalität
    y_axis = rg.Vector3d.CrossProduct(z_axis, x_axis)
    y_axis.Unitize()

    test_bi = brett_info(blist[len(blist) // 2], x_axis, y_axis, z_axis)
    if test_bi['y_min'] + test_bi['y_max'] < 0:
        y_axis = -y_axis
        z_axis = rg.Vector3d.CrossProduct(x_axis, y_axis)
        z_axis.Unitize()

    info.append("X: {:.2f},{:.2f},{:.2f}  Y: {:.2f},{:.2f},{:.2f}  Z: {:.2f},{:.2f},{:.2f}".format(
        x_axis.X, x_axis.Y, x_axis.Z, y_axis.X, y_axis.Y, y_axis.Z, z_axis.X, z_axis.Y, z_axis.Z))
    info.append("Dicken: Rueckplatte={:.1f}  Ansaugplatte={:.1f}".format(_rd, _ad))

    infos = [(b, brett_info(b, x_axis, y_axis, z_axis)) for b in blist]
    infos_sorted = sorted(infos, key=lambda it: it[1]['z_min'])

    # Eingabe-Bretter zuerst vereinen (kann 1..n Koerper ergeben).
    # Genau diese vereinten Koerper werden danach von den Rueckplatten abgezogen.
    union_input_bretter = union_breps(duplicate_breps(blist), 0.25)
    info.append("Input-Bretter vereinigt: {} Teil(e)".format(len(union_input_bretter)))

    all_x_min = min(it[1]['x_min'] for it in infos)
    all_x_max = max(it[1]['x_max'] for it in infos)
    all_y_min = min(it[1]['y_min'] for it in infos)
    all_y_max = max(it[1]['y_max'] for it in infos)
    all_z_min = min(it[1]['z_min'] for it in infos)
    all_z_max = max(it[1]['z_max'] for it in infos)

    for seite_name, side in [("links", 0), ("rechts", 1)]:
        side_sign = -1.0 if side == 0 else 1.0
        xk_global = all_x_min if side == 0 else all_x_max

        # Richtung ist immer seitenabhaengig:
        # links -> negative X-Richtung, rechts -> positive X-Richtung.
        # Rueckplatte liegt um treppenausschnitt nach innen ueber den Brettenden.
        x_rp_front = xk_global - side_sign * _ta
        x_rp_back = x_rp_front + side_sign * _rd

        info.append("--- Seite {} ---".format(seite_name))
        info.append("Brettkante={:.1f}  RP-front={:.1f}  RP-back={:.1f}".format(xk_global, x_rp_front, x_rp_back))

        # Neu: absichtlich uebergrosse, plane Kontur erzeugen.
        # Das Trimmen auf die finale Geometrie passiert nachgelagert.
        # Zusaetzlich pauschal 100 mm Ueberstand je Richtung (Y/Z),
        # ohne die Plattendicke in X zu veraendern.
        oversize_pad = max(30.0, 2.0 * _rand) + 100.0
        kontur = oversized_rect_kontur(
            x_rp_front,
            all_y_min,
            all_y_max,
            all_z_min,
            all_z_max,
            oversize_pad,
            x_axis,
            y_axis,
            z_axis,
        )
        if kontur is None:
            raise Exception("Kontur konnte nicht erzeugt werden ({})".format(seite_name))
        outline.append(kontur)
        info.append("Konturmodus: uebergrosses Rechteck (Pad={:.1f})".format(oversize_pad))

        rp_brep = extrude_curve(kontur, x_axis * (side_sign * _rd))
        rp_parts = [rp_brep] if rp_brep else []
        rp_x_min = None
        rp_x_max = None
        if rp_brep is not None:
            rp_bi = brett_info(rp_brep, x_axis, y_axis, z_axis)
            rp_x_min = rp_bi['x_min']
            rp_x_max = rp_bi['x_max']

        if rp_brep and _ta > 0:
            info.append("Abtrag deaktiviert (nachgelagert)")

        valid_parts = [p for p in rp_parts if p is not None and p.IsValid]
        if valid_parts:
            rueckplatte.extend(valid_parts)
            info.append("Rueckplatte OK ({} Teil(e))".format(len(valid_parts)))

        # Außenplatte: direkt auf der Außenfläche der Rückplatte, Dicke = ansaug_dicke
        y_min = all_y_min - max(5.0, min(_rand, 20.0))
        y_max = all_y_max + max(5.0, min(_rand, 20.0))
        z_min = all_z_min - max(5.0, min(_rand, 20.0))
        z_max = all_z_max + max(5.0, min(_rand, 20.0))

        x_ans_ctr = x_rp_back + side_sign * (_ad * 0.5)
        x_ans_half = _ad * 0.5
        y_ctr = 0.5 * (y_min + y_max)
        y_half = 0.5 * (y_max - y_min)
        z_ctr = 0.5 * (z_min + z_max)
        z_half = 0.5 * (z_max - z_min)

        ans_brep = local_box(x_ans_ctr, x_ans_half, y_ctr, y_half, z_ctr, z_half, x_axis, y_axis, z_axis)
        if ans_brep and ans_brep.IsValid:
            ansaugplatte.append(ans_brep)
            info.append("Ansaugplatte OK")

    info.append("Fertig")

except Exception as e:
    import traceback
    info.append("FEHLER: " + str(e))
    info.append(traceback.format_exc())