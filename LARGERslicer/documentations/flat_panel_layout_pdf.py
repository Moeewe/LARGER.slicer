"""
GHPython: 5-board flat layout + PDF export + parts list.

DE/EN compatibility:
- Existing German input/output names continue to work.
- English aliases are supported in parallel.

INPUTS (DE -> EN aliases):
    laenge / length
    breite / width
    hoehe / height
    staerke / thickness
    z_ueberstand_unten / z_overhang_bottom
    z_ueberstand_oben / z_overhang_top
    abstand / spacing
    exportordner / export_folder
    dateiname / file_name
    export_starten / export_start

OUTPUTS:
    bretter_3d, bretter_flat, mass_kurven, mass_texte, stueckliste,
    pdf_datei, stueckliste_pdf, protokoll, parameter_namen, parameter_werte
"""

import os
import datetime
import System
import Rhino
import Rhino.Geometry as rg


def _pick_input(*keys):
    for key in keys:
        val = globals().get(key, None)
        if val is not None:
            return val
    return None


# GH input fallbacks (German + English aliases)
laenge = _pick_input("laenge", "length", "inner_length")
breite = _pick_input("breite", "width", "inner_width")
hoehe = _pick_input("hoehe", "height", "inner_height")
staerke = _pick_input("staerke", "thickness", "material_thickness")
z_ueberstand_unten = _pick_input("z_ueberstand_unten", "z_overhang_bottom")
z_ueberstand_oben = _pick_input("z_ueberstand_oben", "z_overhang_top")
abstand = _pick_input("abstand", "spacing", "gap")
exportordner = _pick_input("exportordner", "export_folder")
dateiname = _pick_input("dateiname", "file_name", "base_name")
export_starten = _pick_input("export_starten", "export_start", "run_export")

bretter_3d = []
bretter_flat = []
mass_kurven = []
mass_texte = []
stueckliste = []
pdf_datei = ""
stueckliste_pdf = ""
protokoll = []
parameter_namen = []
parameter_werte = []


def to_bool(value, default=False):
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    s = str(value).strip().lower()
    if s in ("1", "true", "yes", "y", "on", "ja"):
        return True
    if s in ("0", "false", "no", "n", "off", "nein"):
        return False
    return default


def collapse_whitespace(text):
    if text is None:
        return ""
    return " ".join(str(text).split())


def sanitize_token(value, fallback=""):
    raw = fallback if value is None or str(value).strip() == "" else str(value).strip()
    invalid = set(System.IO.Path.GetInvalidFileNameChars())
    chars = []
    for c in raw:
        chars.append("_" if c in invalid else c)
    return collapse_whitespace("".join(chars).strip())


def resolve_export_folder(user_folder):
    if user_folder is not None and str(user_folder).strip() != "":
        return os.path.abspath(str(user_folder)), False

    desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
    return os.path.abspath(str(desktop)), True


def build_base_name(custom_name):
    if custom_name is not None and str(custom_name).strip() != "":
        return sanitize_token(custom_name, "PanelLayout")
    return "MSA" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")


def create_export_run_folder(base_folder, base_name):
    run_folder = os.path.join(base_folder, sanitize_token(base_name, "Export"))
    if not os.path.exists(run_folder):
        os.makedirs(run_folder)
        return run_folder

    i = 1
    while True:
        candidate = os.path.join(base_folder, "{}_{}".format(sanitize_token(base_name, "Export"), str(i).zfill(2)))
        if not os.path.exists(candidate):
            os.makedirs(candidate)
            return candidate
        i += 1


def make_box(x0, y0, z0, x1, y1, z1):
    plane = rg.Plane.WorldXY
    box = rg.Box(
        plane,
        rg.Interval(min(x0, x1), max(x0, x1)),
        rg.Interval(min(y0, y1), max(y0, y1)),
        rg.Interval(min(z0, z1), max(z0, z1)),
    )
    b = box.ToBrep()
    return b if b and b.IsValid else None


def add_rhino_dimensions(panel, dim_curves, dim_texts, dim_offset):
    x0 = panel["x"]
    y0 = panel["y"]
    x1 = panel["x"] + panel["l"]
    y1 = panel["y"] + panel["b"]

    # X dimension below panel
    y_dim = y0 - dim_offset
    dim_curves.append(rg.LineCurve(rg.Point3d(x0, y0, 0), rg.Point3d(x0, y_dim, 0)))
    dim_curves.append(rg.LineCurve(rg.Point3d(x1, y0, 0), rg.Point3d(x1, y_dim, 0)))
    dim_curves.append(rg.LineCurve(rg.Point3d(x0, y_dim, 0), rg.Point3d(x1, y_dim, 0)))
    dim_texts.append(rg.TextDot("L={:.1f} mm".format(panel["l"]), rg.Point3d((x0 + x1) * 0.5, y_dim - 0.25 * dim_offset, 0)))

    # Y dimension on the left
    x_dim = x0 - dim_offset
    dim_curves.append(rg.LineCurve(rg.Point3d(x0, y0, 0), rg.Point3d(x_dim, y0, 0)))
    dim_curves.append(rg.LineCurve(rg.Point3d(x0, y1, 0), rg.Point3d(x_dim, y1, 0)))
    dim_curves.append(rg.LineCurve(rg.Point3d(x_dim, y0, 0), rg.Point3d(x_dim, y1, 0)))
    dim_texts.append(rg.TextDot("B={:.1f} mm".format(panel["b"]), rg.Point3d(x_dim - 0.6 * dim_offset, (y0 + y1) * 0.5, 0)))


def _pdf_safe_line(text):
    s = str(text)
    s = (s
         .replace("ä", "ae").replace("ö", "oe").replace("ü", "ue")
         .replace("Ä", "Ae").replace("Ö", "Oe").replace("Ü", "Ue")
         .replace("ß", "ss"))
    s = s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
    return s.encode("cp1252", "replace").decode("cp1252")


def write_text_lines_pdf(path, title, lines_in):
    page_w = 595
    page_h = 842
    left = 40
    top = page_h - 42
    line_h = 13

    lines = []
    lines.append("BT /F2 12 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(left, top, _pdf_safe_line(title)))
    lines.append("BT /F1 8 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(
        left,
        top - 16,
        _pdf_safe_line(datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")),
    ))
    lines.append("0.6 w {} {} m {} {} l S 1 w".format(left, top - 21, page_w - left, top - 21))

    y = top - 40
    for t in lines_in:
        if y < 36:
            break
        lines.append("BT /F1 8 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(left, y, _pdf_safe_line(t)))
        y -= line_h

    obj = {}
    next_id = 1
    cat = next_id; next_id += 1
    pages = next_id; next_id += 1
    f1 = next_id; next_id += 1
    f2 = next_id; next_id += 1
    page = next_id; next_id += 1
    content = next_id; next_id += 1

    obj[f1] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    obj[f2] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"

    stream = "\n".join(lines)
    obj[content] = "<< /Length {} >>\nstream\n{}\nendstream".format(len(stream.encode("cp1252", "replace")), stream)
    obj[page] = (
        "<< /Type /Page /Parent {} 0 R /MediaBox [0 0 {} {}] "
        "/Resources << /Font << /F1 {} 0 R /F2 {} 0 R >> >> /Contents {} 0 R >>"
    ).format(pages, page_w, page_h, f1, f2, content)
    obj[pages] = "<< /Type /Pages /Kids [ {} 0 R ] /Count 1 >>".format(page)
    obj[cat] = "<< /Type /Catalog /Pages {} 0 R >>".format(pages)

    out = ["%PDF-1.4\n"]
    offsets = [0]
    for oid in range(1, next_id):
        offsets.append(sum(len(p.encode("cp1252", "replace")) for p in out))
        out.append("{} 0 obj\n{}\nendobj\n".format(oid, obj.get(oid, "<< >>")))

    xref_pos = sum(len(p.encode("cp1252", "replace")) for p in out)
    out.append("xref\n")
    out.append("0 {}\n".format(next_id))
    out.append("0000000000 65535 f \n")
    for oid in range(1, next_id):
        out.append("{:010d} 00000 n \n".format(offsets[oid]))

    out.append("trailer\n")
    out.append("<< /Size {} /Root {} 0 R >>\n".format(next_id, cat))
    out.append("startxref\n")
    out.append("{}\n".format(xref_pos))
    out.append("%%EOF\n")

    with open(path, "wb") as f:
        for part in out:
            f.write(part.encode("cp1252", "replace"))

    return path


def write_layout_pdf(path, title, flat_panels):
    """
    Draws the flat layout as 2D rectangles with X/Y dimensions per board.
    flat_panels: List[dict] mit keys {name, x, y, l, b}
    """
    page_w = 842
    page_h = 595
    left = 28
    right = page_w - 28
    top = page_h - 30
    bottom = 36

    # Layout bounding box
    min_x = min(p["x"] for p in flat_panels)
    min_y = min(p["y"] for p in flat_panels)
    max_x = max(p["x"] + p["l"] for p in flat_panels)
    max_y = max(p["y"] + p["b"] for p in flat_panels)

    lw = max(1.0, max_x - min_x)
    lh = max(1.0, max_y - min_y)

    avail_w = max(10.0, right - left)
    avail_h = max(10.0, top - bottom - 40)
    scale = min(avail_w / lw, avail_h / lh) * 0.88

    ox = left + (avail_w - lw * scale) * 0.5 - min_x * scale
    oy = bottom + 18 + (avail_h - lh * scale) * 0.5 - min_y * scale

    def tx(xv):
        return ox + xv * scale

    def ty(yv):
        return oy + yv * scale

    lines = []

    # Header
    lines.append("BT /F2 12 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(left, page_h - 22, _pdf_safe_line(title)))
    lines.append("BT /F1 8 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(
        left,
        page_h - 35,
        _pdf_safe_line(datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")),
    ))
    lines.append("0.6 w {} {} m {} {} l S 1 w".format(left, page_h - 40, right, page_h - 40))

    # Drawing + dimensions per panel
    for p in flat_panels:
        x0 = tx(p["x"])
        y0 = ty(p["y"])
        x1 = tx(p["x"] + p["l"])
        y1 = ty(p["y"] + p["b"])

        # Rectangle
        lines.append("0.6 w")
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l {:.2f} {:.2f} l {:.2f} {:.2f} l h S".format(x0, y0, x1, y0, x1, y1, x0, y1))

        # Label inside panel
        lines.append("BT /F1 7 Tf 1 0 0 1 {:.2f} {:.2f} Tm ({}) Tj ET".format(
            x0 + 4,
            y1 - 12,
            _pdf_safe_line(p["name"]),
        ))

        # X dimension below
        dim_off = max(18.0, p.get("dim_offset", 20.0))
        y_dim = y0 - dim_off
        lines.append("0.35 w")
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x0, y0, x0, y_dim))
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x1, y0, x1, y_dim))
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x0, y_dim, x1, y_dim))
        lines.append("BT /F1 6.5 Tf 1 0 0 1 {:.2f} {:.2f} Tm ({}) Tj ET".format(
            (x0 + x1) * 0.5 - 16,
            y_dim - 12,
            _pdf_safe_line("{:.1f} mm".format(p["l"])),
        ))

        # Y dimension left side
        x_dim = x0 - dim_off
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x0, y0, x_dim, y0))
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x0, y1, x_dim, y1))
        lines.append("{:.2f} {:.2f} m {:.2f} {:.2f} l S".format(x_dim, y0, x_dim, y1))
        lines.append("BT /F1 6.5 Tf 1 0 0 1 {:.2f} {:.2f} Tm ({}) Tj ET".format(
            x_dim - 30,
            (y0 + y1) * 0.5 - 3,
            _pdf_safe_line("{:.1f}".format(p["b"])),
        ))

    # Footer
    footer = "(c) MSA Münster School of Architecture"
    lines.append("0.4 w {} {} m {} {} l S 1 w".format(left, bottom - 8, right, bottom - 8))
    lines.append("BT /F1 7 Tf 1 0 0 1 {} {} Tm ({}) Tj ET".format(left, bottom - 20, _pdf_safe_line(footer)))

    # PDF objects
    obj = {}
    next_id = 1
    cat = next_id; next_id += 1
    pages = next_id; next_id += 1
    f1 = next_id; next_id += 1
    f2 = next_id; next_id += 1
    page = next_id; next_id += 1
    content = next_id; next_id += 1

    obj[f1] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    obj[f2] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"

    stream = "\n".join(lines)
    obj[content] = "<< /Length {} >>\nstream\n{}\nendstream".format(len(stream.encode("cp1252", "replace")), stream)
    obj[page] = (
        "<< /Type /Page /Parent {} 0 R /MediaBox [0 0 {} {}] "
        "/Resources << /Font << /F1 {} 0 R /F2 {} 0 R >> >> /Contents {} 0 R >>"
    ).format(pages, page_w, page_h, f1, f2, content)
    obj[pages] = "<< /Type /Pages /Kids [ {} 0 R ] /Count 1 >>".format(page)
    obj[cat] = "<< /Type /Catalog /Pages {} 0 R >>".format(pages)

    out = ["%PDF-1.4\n"]
    offsets = [0]

    for oid in range(1, next_id):
        offsets.append(sum(len(p.encode("cp1252", "replace")) for p in out))
        out.append("{} 0 obj\n{}\nendobj\n".format(oid, obj.get(oid, "<< >>")))

    xref_pos = sum(len(p.encode("cp1252", "replace")) for p in out)
    out.append("xref\n")
    out.append("0 {}\n".format(next_id))
    out.append("0000000000 65535 f \n")
    for oid in range(1, next_id):
        out.append("{:010d} 00000 n \n".format(offsets[oid]))

    out.append("trailer\n")
    out.append("<< /Size {} /Root {} 0 R >>\n".format(next_id, cat))
    out.append("startxref\n")
    out.append("{}\n".format(xref_pos))
    out.append("%%EOF\n")

    with open(path, "wb") as f:
        for part in out:
            f.write(part.encode("cp1252", "replace"))

    return path


try:
    L_in = float(laenge) if laenge is not None else 1000.0
    B_in = float(breite) if breite is not None else 600.0
    H_in = float(hoehe) if hoehe is not None else 400.0
    T = float(staerke) if staerke is not None else 19.0
    ZU = float(z_ueberstand_unten) if z_ueberstand_unten is not None else 0.0
    ZO = float(z_ueberstand_oben) if z_ueberstand_oben is not None else 0.0
    GAP = float(abstand) if abstand is not None else 20.0

    run_export = to_bool(export_starten, False)

    # Basic validation
    if L_in <= 0 or B_in <= 0 or H_in <= 0 or T <= 0:
        raise Exception("laenge/breite/hoehe/staerke (length/width/height/thickness) must be > 0.")
    if GAP < 0:
        raise Exception("abstand/spacing must not be negative.")
    if (T + ZU) <= 0.5:
        raise Exception("z_ueberstand_unten/z_overhang_bottom makes effective bottom thickness invalid (<= 0.5 mm).")
    if (H_in + ZO) <= 0.5:
        raise Exception("z_ueberstand_oben/z_overhang_top makes effective board height invalid (<= 0.5 mm).")

    parameter_namen = [
        "layout.innen_laenge",
        "layout.innen_breite",
        "layout.innen_hoehe",
        "layout.staerke",
        "layout.z_ueberstand_unten",
        "layout.z_ueberstand_oben",
        "layout.abstand",
        "layout.teile",
    ]
    parameter_werte = [L_in, B_in, H_in, T, ZU, ZO, GAP, 5]

    # Input dimensions are inner dimensions.
    # Top open shell (no lid), front and back closed.
    # Rule: the parallel left/right side pair is extended by T on front + back.
    # 5 Teile: Boden, Seite links/rechts, Frontwand, Rueckwand
    boden_t = T + ZU
    wand_h = H_in + ZO
    parts = [
        {"name": "Bottom / Boden",            "l": L_in + 2.0 * T, "b": B_in + 2.0 * T, "t": boden_t},
        {"name": "Left side / Seite links",   "l": L_in + 2.0 * T, "b": wand_h, "t": T},
        {"name": "Right side / Seite rechts", "l": L_in + 2.0 * T, "b": wand_h, "t": T},
        {"name": "Front wall / Frontwand",    "l": B_in, "b": wand_h, "t": T},
        {"name": "Back wall / Rueckwand",     "l": B_in, "b": wand_h, "t": T},
    ]

    # Assembled 3D model from inner-space definition:
    # inner space: x=0..L_in, y=0..B_in, z=0..H_in (top open)
    # bottom below, left/right as long pair (+T front/back),
    # front/back as short pair between side panels.
    b_boden = make_box(-T, -T, -T - ZU, L_in + T, B_in + T, 0)
    b_sl = make_box(-T, -T, 0, L_in + T, 0, H_in + ZO)
    b_sr = make_box(-T, B_in, 0, L_in + T, B_in + T, H_in + ZO)
    b_fw = make_box(-T, 0, 0, 0, B_in, H_in + ZO)
    b_rw = make_box(L_in, 0, 0, L_in + T, B_in, H_in + ZO)
    bretter_3d = [b for b in [b_boden, b_sl, b_sr, b_fw, b_rw] if b is not None]

    # Flat layout in XY: compact multi-row placement with safety clearance,
    # so dimensions do not overlap neighboring boards.
    flat_panels = []
    dim_offset = max(22.0, GAP * 1.15)
    top_clearance = 26.0
    right_clearance = 28.0

    part_items = []
    for p in parts:
        pl = p["l"]
        pb = p["b"]
        cell_w = pl + dim_offset + right_clearance
        cell_h = pb + dim_offset + top_clearance
        part_items.append({
            "name": p["name"],
            "l": pl,
            "b": pb,
            "t": p["t"],
            "cell_w": cell_w,
            "cell_h": cell_h,
            "sort_key": (max(pl, pb), pl * pb),
        })

    # Larger parts first for a compact/stable arrangement
    part_items.sort(key=lambda it: (it["sort_key"][0], it["sort_key"][1]), reverse=True)

    total_cell_area = sum(it["cell_w"] * it["cell_h"] for it in part_items)
    widest_cell = max(it["cell_w"] for it in part_items)
    target_row_w = max(widest_cell, (total_cell_area ** 0.5) * 1.25)

    x_cursor = 0.0
    y_cursor = 0.0
    row_h = 0.0

    for it in part_items:
        cw = it["cell_w"]
        ch = it["cell_h"]

        if x_cursor > 0.0 and (x_cursor + cw) > target_row_w:
            x_cursor = 0.0
            y_cursor += row_h + GAP
            row_h = 0.0

        px = x_cursor + dim_offset
        py = y_cursor + dim_offset

        panel = {
            "name": it["name"],
            "x": px,
            "y": py,
            "l": it["l"],
            "b": it["b"],
            "t": it["t"],
            "dim_offset": dim_offset,
        }
        flat_panels.append(panel)

        # Flat board as Brep (thickness in Z)
        bf = make_box(px, py, 0, px + it["l"], py + it["b"], it["t"])
        if bf is not None:
            bretter_flat.append(bf)
            add_rhino_dimensions(panel, mass_kurven, mass_texte, dim_offset)

        x_cursor += cw + GAP
        row_h = max(row_h, ch)

    # Build grouped parts list
    grouped = {}
    for p in parts:
        key = (round(p["t"], 3), round(p["l"], 3), round(p["b"], 3), round(p["t"], 3))
        grouped[key] = grouped.get(key, 0) + 1

    stueckliste.append("=" * 58)
    stueckliste.append("STUECKLISTE / PARTS LIST")
    stueckliste.append("=" * 58)
    stueckliste.append("{:<5} {:>8} {:>10} {:>10} {:>10}".format("Qty", "Thick", "Length", "Width", "Height"))
    stueckliste.append("-" * 58)
    total = 0
    for (d, l, b, h), cnt in sorted(grouped.items(), key=lambda kv: (kv[0][1], kv[0][2], kv[0][0])):
        stueckliste.append("{:<5} {:>8.1f} {:>10.1f} {:>10.1f} {:>10.1f}".format(cnt, d, l, b, h))
        total += cnt
    stueckliste.append("-" * 58)
    stueckliste.append("Gesamt / Total: {} Bretter / boards".format(total))
    stueckliste.append("Ausfuehrung / Configuration: oben offen (kein Deckel) / top open (no lid), vorne+hinten geschlossen / front+back closed")
    protokoll.append("Innenmasse / Inner dimensions: L={:.1f}, B={:.1f}, H={:.1f} mm".format(L_in, B_in, H_in))
    protokoll.append("Z-Ueberstand / Z overhang: unten/bottom={:+.1f} mm, oben/top={:+.1f} mm".format(ZU, ZO))
    protokoll.append("Flat layout compact, no overlap; Abstand/spacing={:.1f} mm".format(GAP))
    protokoll.append("Rhino dimensions generated: {} lines, {} text dots".format(len(mass_kurven), len(mass_texte)))

    if not run_export:
        protokoll.append("Export ready. Set export_starten/export_start=True for PDF export.")
    else:
        base_folder, used_fallback = resolve_export_folder(exportordner)
        if not os.path.isdir(base_folder):
            os.makedirs(base_folder)

        base_name = build_base_name(dateiname)
        run_folder = create_export_run_folder(base_folder, base_name)

        if used_fallback:
            protokoll.append("Export folder not set: using Desktop fallback.")

        pdf_path = os.path.join(run_folder, base_name + "_FlatLayout.pdf")
        write_layout_pdf(pdf_path, base_name + " - Flat Layout", flat_panels)
        pdf_datei = pdf_path
        protokoll.append("PDF written: " + pdf_path)

        sl_pdf_path = os.path.join(run_folder, base_name + "_Stueckliste.pdf")
        write_text_lines_pdf(sl_pdf_path, base_name + " - Parts List", stueckliste)
        stueckliste_pdf = sl_pdf_path
        protokoll.append("Parts-list PDF written: " + sl_pdf_path)

    # Optional English aliases for downstream scripts (German outputs remain unchanged).
    boards_3d = bretter_3d
    boards_flat = bretter_flat
    dimension_curves = mass_kurven
    dimension_texts = mass_texte
    parts_list = stueckliste
    pdf_file = pdf_datei
    parts_list_pdf = stueckliste_pdf
    log = protokoll
    parameter_names = parameter_namen
    parameter_values = parameter_werte

except Exception as e:
    import traceback
    protokoll.append("FEHLER: " + str(e))
    protokoll.append(traceback.format_exc())
