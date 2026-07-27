"""
GHPython: BOM / Report Export (CSV + TXT + PDF + Excel)

INPUTS:
    csv_kopf          : str         (optional; wird sonst automatisch gesetzt)
    csv_zeilen        : Text [List] (optional; wird sonst aus stueckliste_text erzeugt)
    stueckliste_text  : Text [List] (optional, Alias: stueckliste)
    zusatz_info       : Text [List] (optional, Alias: info)
    bretter           : Brep [List] (optional, z. B. aus Untitled-2)
    rueckwaende       : Brep [List] (optional, Sammelinput Rueckwaende)
    ansaugflaechen    : Brep [List] (optional, Sammelinput Ansaugflaechen)
    rueckwaende_links : Brep [List] (optional, praeziser Typ Rueckwand links)
    rueckwaende_rechts: Brep [List] (optional, praeziser Typ Rueckwand rechts)
    ansaug_links      : Brep [List] (optional, praeziser Typ Ansaugflaeche links)
    ansaug_rechts     : Brep [List] (optional, praeziser Typ Ansaugflaeche rechts)
    parameter_namen   : Text [List] (optional)
    parameter_werte   : Generic [List] (optional)
    exportordner      : str         (optional, fallback: Rhino doc folder / Desktop)
    projektname       : str         (default: Projekt)
    teilenummer       : str         (default: Teil1)
    unterteilenummer  : str         (default: allgemein)
    revision          : str         (optional)
    write_csv         : bool        (default: True)
    write_txt         : bool        (default: True)
    write_pdf         : bool        (default: True)
    write_excel       : bool        (default: True)
    write_view        : bool        (default: False)
    show_kanten       : bool        (default: False)  -- BBox-Sihouetten je Teil zusaetzlich grau einblenden
    frontgeometrie    : Brep [List] (optional)        -- gedrehte Frontgeometrie fuer Ansichten-PDF (rot)
    frontgeometrie_original : Brep [List] (optional)  -- unge drehte Original-Front fuer _Front_original.3dm
    param_preset      : str         (optional)        -- Pfad zu einer Preset-CSV; fehlende Inputs werden daraus ergaenzt
    save_preset       : bool        (default: False)  -- Beim Export eine _Preset.csv mit allen Parametern schreiben
    export_starten    : bool        (default: False)

OUTPUTS:
    dateien           : Text [List]
    basisname         : Text
    report_preview    : Text [List]
    protokoll         : Text [List]
"""

import os
import datetime
import re
import zipfile
import System
import Rhino
import Rhino.Geometry as rg


def _pick_input(*keys):
    for key in keys:
        val = globals().get(key, None)
        if val is not None:
            return val
    return None

# GH input fallbacks for editor/static analysis.
csv_kopf = _pick_input("csv_kopf", "csv_header")
csv_zeilen = _pick_input("csv_zeilen", "csv_rows")
stueckliste_text = _pick_input("stueckliste_text", "parts_list_text")
stueckliste = _pick_input("stueckliste", "parts_list")
zusatz_info = _pick_input("zusatz_info", "additional_info")
info_alias = _pick_input("info", "extra_info")
bretter = _pick_input("bretter", "boards")
rueckwaende = _pick_input("rueckwaende", "back_walls")
ansaugflaechen = _pick_input("ansaugflaechen", "suction_surfaces")
rueckwaende_links = _pick_input("rueckwaende_links", "back_walls_left")
rueckwaende_rechts = _pick_input("rueckwaende_rechts", "back_walls_right")
ansaug_links = _pick_input("ansaug_links", "suction_surfaces_left")
ansaug_rechts = _pick_input("ansaug_rechts", "suction_surfaces_right")

# Alias-Fallbacks zu bestehenden Skriptnamen (kompatibel zu Untitled-1/Untitled-3)
rueckplatte = _pick_input("rueckplatte", "back_plate")
ansaugplatte = _pick_input("ansaugplatte", "suction_plate")
ansaugbretter_out = _pick_input("ansaugbretter_out", "suction_boards_out")
parameter_namen = _pick_input("parameter_namen", "parameter_names")
parameter_werte = _pick_input("parameter_werte", "parameter_values")
exportordner = _pick_input("exportordner", "export_folder")
projektname = _pick_input("projektname", "project_name")
teilenummer = _pick_input("teilenummer", "part_number")
unterteilenummer = _pick_input("unterteilenummer", "subpart_number")
revision = _pick_input("revision", "rev")
write_csv = _pick_input("write_csv", "export_csv")
write_txt = _pick_input("write_txt", "export_txt")
write_pdf = _pick_input("write_pdf", "export_pdf")
write_excel = _pick_input("write_excel", "export_excel")
write_view = _pick_input("write_view", "export_views")
show_kanten = _pick_input("show_kanten", "show_edges")
frontgeometrie = _pick_input("frontgeometrie", "front_geometry")
frontgeometrie_original = _pick_input("frontgeometrie_original", "front_geometry_original")
param_preset = _pick_input("param_preset", "parameter_preset", "preset_csv")
save_preset  = _pick_input("save_preset", "save_parameter_preset")
export_starten = _pick_input("export_starten", "export_start", "run_export")


dateien = []
basisname = ""
report_preview = []
protokoll = []


# ── Preset I/O ─────────────────────────────────────────────────────────────
_PRESET_KEYS_STR  = ["projektname", "teilenummer", "unterteilenummer", "revision", "exportordner"]
_PRESET_KEYS_BOOL = ["write_csv", "write_txt", "write_pdf", "write_excel", "write_view", "show_kanten"]
_PRESET_KEYS_LIST = ["parameter_namen", "parameter_werte"]


def write_params_csv(path, values):
    """Speichert Eingabeparameter als Preset-CSV. ``values`` dict {key: wert}."""
    lines = ["parameter;wert"]
    for key in _PRESET_KEYS_STR + _PRESET_KEYS_BOOL + _PRESET_KEYS_LIST:
        v = values.get(key, "")
        if isinstance(v, (list, tuple)):
            v = "|".join(str(x) for x in v if x is not None)
        elif v is None:
            v = ""
        else:
            v = str(v)
        lines.append("{};{}".format(key, v))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return path


def load_params_csv(path):
    """Laedt Preset-CSV; gibt dict {key: str_value} zurueck. Bei Fehler {'__error__': msg}."""
    result = {}
    try:
        with open(path, "r", encoding="utf-8") as f:
            for i, line in enumerate(f):
                if i == 0:
                    continue
                line = line.strip()
                if not line:
                    continue
                parts = line.split(";", 1)
                if len(parts) == 2:
                    result[parts[0].strip()] = parts[1].strip()
    except Exception as e:
        result["__error__"] = str(e)
    return result


def _preset_str(preset, key, current):
    """Gibt ``current`` zurueck wenn gesetzt; sonst Preset-String-Wert."""
    if current is not None and str(current).strip() != "":
        return current
    return preset.get(key, None) or None


def _preset_bool(preset, key, current):
    """Gibt ``current`` zurueck wenn nicht None; sonst Bool aus Preset."""
    if current is not None:
        return current
    raw = preset.get(key, None)
    if raw is None:
        return None
    return raw.strip().lower() in ("true", "1", "yes", "ja")


def _preset_list(preset, key, current):
    """Gibt ``current`` zurueck wenn nicht leer; sonst Liste aus Preset (|-getrennt)."""
    if current is not None:
        lst = current if isinstance(current, (list, tuple)) else [current]
        if len([x for x in lst if x is not None]) > 0:
            return current
    raw = preset.get(key, "")
    if not raw:
        return None
    return raw.split("|")


def company_stamp_text():
    year = datetime.datetime.now().year
    return "(c) PARAMETRIC.solutions - Moritz Wesseler {} - www.parametricsolutions.de".format(year)


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


def transliterate_german(text):
    """Ersetzt deutsche Umlaute/ß in dateisichere ASCII-Varianten."""
    s = str(text)
    repl = {
        "ä": "ae", "ö": "oe", "ü": "ue",
        "Ä": "Ae", "Ö": "Oe", "Ü": "Ue",
        "ß": "ss",
    }
    for src, dst in repl.items():
        s = s.replace(src, dst)
    return s


def build_short_xml_name(base_name):
    """Nur fuer XML: kurzer Dateiname PSYYYYMMDD_HHMMSS (+ Fallback)."""
    m = re.search(r"PS\d{8}_\d{6}", str(base_name))
    core = m.group(0) if m else sanitize_token(base_name, "Export")
    core = transliterate_german(core)
    return sanitize_token(core, "Export")


def build_base_name(project, part, subpart, rev, document_type="Stueckliste"):
    # Zeit bis Sekunden fuer eindeutige Versionierung je Exportlauf.
    prefix = "PS" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    p = sanitize_token(project, "Projekt")
    pn = sanitize_token(part, "Teil")
    sp = sanitize_token(subpart, "Allgemein")
    dt = sanitize_token(document_type, "Stueckliste")
    rv = sanitize_token(rev, "")

    out = "{}_{:s} - {:s} - {:s} - {:s}".format(prefix, p, dt, pn, sp)
    if rv:
        out += " - " + rv
    return collapse_whitespace(out.strip())


def to_bool(value, default=False):
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    s = str(value).strip().lower()
    if s in ("1", "true", "yes", "y", "on"):
        return True
    if s in ("0", "false", "no", "n", "off"):
        return False
    return default


def to_text_list(value):
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        out = []
        for v in value:
            if v is None:
                continue
            out.append(str(v))
        return out
    return [str(value)]


def to_brep_list(value):
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        out = []
        for g in value:
            if isinstance(g, rg.Brep):
                out.append(g)
        return out
    return [value] if isinstance(value, rg.Brep) else []


def detect_max_pos(csv_lines):
    max_pos = 0
    for row in csv_lines:
        try:
            first = str(row).split(";")[0].strip()
            pos = int(float(first))
            if pos > max_pos:
                max_pos = pos
        except Exception:
            continue
    return max_pos


def normalize_csv_row(row_text):
    """
    Zielschema (ohne Material):
    Pos;Typ;Laenge;Tiefe;Hoehe;Anzahl;Bemerkung
    """
    parts = [p.strip() for p in str(row_text).split(";")]

    # Altes Schema mit Material:
    # Pos;Typ;Laenge;Tiefe;Hoehe;Anzahl;Material;Bemerkung
    if len(parts) == 8:
        parts = [parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[7]]

    if len(parts) < 7:
        parts = parts + ([""] * (7 - len(parts)))
    elif len(parts) > 7:
        parts = parts[:6] + [";".join(parts[6:])]

    return ";".join(parts)


def split_csv_row(row_text):
    norm = normalize_csv_row(row_text)
    parts = [p.strip() for p in norm.split(";")]
    if len(parts) < 7:
        parts = parts + ([""] * (7 - len(parts)))
    return parts[:7]


def build_csv_from_stueckliste_lines(stueckliste_lines, pos_start=1):
    """
    Parse Zeilen aus Untitled-2-Stueckliste:
    "Anz. Typ L(mm) B(mm) D(mm)" -> CSV-Zeilen fuer Export.
    """
    rows = []
    pos = max(1, int(pos_start)) - 1

    for line in stueckliste_lines:
        text = str(line).strip()
        if not text:
            continue
        if text.startswith("=") or text.startswith("-"):
            continue
        if text.lower().startswith("stueckliste"):
            continue
        if text.lower().startswith("gesamt:"):
            continue
        if text.lower().startswith("fuge:"):
            continue
        if text.lower().startswith("hoehencheck:"):
            continue
        if text.lower().startswith("anz."):
            continue

        # Erwartetes Muster: anz typ l b d (Whitespace-separiert)
        # Typ kann auch Leerzeichen/Klammern enthalten, z. B. "Oberstes (erstes)".
        m = re.match(r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$", text)
        if not m:
            continue

        anz = int(m.group(1))
        typ = m.group(2).strip()
        laenge = float(m.group(3))
        breite = float(m.group(4))
        dicke = float(m.group(5))

        pos += 1
        # Mapping fuer CSV-Format: Laenge, Tiefe, Hoehe
        rows.append("{};{};{:.1f};{:.1f};{:.1f};{};{}".format(
            pos,
            typ,
            laenge,
            breite,
            dicke,
            anz,
            "Aus stueckliste_text"
        ))

    return rows


def build_csv_from_verlegeplan_lines(stueckliste_lines, pos_start=1):
    """
    Parse Verlegeplan-Zeilen aus Untitled-2:
    "Pos Typ L(mm) B(mm) D(mm) Z(mm)" -> CSV-Zeilen (Anzahl=1 je Lage).
    """
    rows = []
    pos = max(1, int(pos_start)) - 1
    in_section = False

    for line in stueckliste_lines:
        text = str(line).strip()
        if not text:
            continue

        low = text.lower()
        if low.startswith("verlegeplan"):
            in_section = True
            continue

        if not in_section:
            continue

        if text.startswith("=") or text.startswith("-"):
            continue
        if low.startswith("pos"):
            continue

        # Unterstuetzt auch negative Z-Bereiche wie "-734.0--701.0".
        m = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
            text
        )
        if not m:
            continue

        plan_pos = int(m.group(1))
        typ = m.group(2).strip()
        laenge = float(m.group(3))
        breite = float(m.group(4))
        dicke = float(m.group(5))
        z_text = m.group(6).replace(" ", "")

        pos += 1
        rows.append("{};{};{:.1f};{:.1f};{:.1f};{};{}".format(
            pos,
            typ,
            laenge,
            breite,
            dicke,
            1,
            "Verlegeplan Pos {} (Z={})".format(plan_pos, z_text)
        ))

    return rows


def build_csv_info_rows(info_lines, pos_start=1):
    """Fuegt Zusatzinfos als CSV-Zeilen an, damit nichts aus anderen Komponenten verloren geht."""
    rows = []
    pos = max(1, int(pos_start)) - 1
    for line in info_lines:
        text = collapse_whitespace(str(line))
        if not text:
            continue
        pos += 1
        clean = text.replace(";", ",")
        rows.append("{};Info;0;0;0;1;{}".format(pos, clean))
    return rows


def summarize_brep_parts(label, breps, start_pos):
    """Erstellt CSV- und Report-Zeilen fuer Brep-Teile, gruppiert nach L/T/H."""
    grouped = {}
    invalid_count = 0
    swap_t_h = "rueckwand" in str(label).strip().lower()

    for b in breps:
        if b is None or not b.IsValid:
            invalid_count += 1
            continue
        bb = b.GetBoundingBox(True)
        if not bb.IsValid:
            invalid_count += 1
            continue

        l = round(bb.Max.X - bb.Min.X, 1)
        t = round(bb.Max.Y - bb.Min.Y, 1)
        h = round(bb.Max.Z - bb.Min.Z, 1)
        if swap_t_h:
            # Rueckwaende: Ausgabe als L/B/D mit B=Hoehe (Z) und D=Tiefe/Staerke (Y)
            t, h = h, t

        key = (l, t, h)
        grouped[key] = grouped.get(key, 0) + 1

    csv_extra = []
    report_extra = []
    pos = start_pos

    for (l, t, h), count in sorted(grouped.items(), key=lambda kv: (kv[0][0], kv[0][1], kv[0][2])):
        pos += 1
        typ = label
        bemerkung = "Automatisch aus Brep"
        csv_extra.append("{};{};{:.1f};{:.1f};{:.1f};{};{}".format(
            pos, typ, l, t, h, count, bemerkung
        ))
        report_extra.append("{:<5} {:<13} {:>8.1f} {:>8.1f} {:>8.1f}".format(
            count, typ, l, t, h
        ))

    return csv_extra, report_extra, invalid_count, pos


def summarize_bretter_ordered(breps, start_pos):
    """
    Bretter werden positionsbasiert benannt:
    - erstes  -> Oberstes
    - mittlere -> Mitte
    - letztes -> Unterstes
    Je Bezeichnung wird weiterhin nach L/T/H gruppiert.
    """
    valid_items = []
    invalid_count = 0

    for b in breps:
        if b is None or not b.IsValid:
            invalid_count += 1
            continue
        bb = b.GetBoundingBox(True)
        if not bb.IsValid:
            invalid_count += 1
            continue
        valid_items.append((b, bb))

    n = len(valid_items)
    grouped = {}
    for i, (_, bb) in enumerate(valid_items):
        if n == 1:
            typ = "Oberstes"
        elif i == 0:
            typ = "Unterstes"
        elif i == n - 1:
            typ = "Oberstes"
        else:
            typ = "Mitte"

        l = round(bb.Max.X - bb.Min.X, 1)
        t = round(bb.Max.Y - bb.Min.Y, 1)
        h = round(bb.Max.Z - bb.Min.Z, 1)

        key = (typ, l, t, h)
        grouped[key] = grouped.get(key, 0) + 1

    csv_extra = []
    report_extra = []
    pos = start_pos

    def sort_key(item):
        order = {"Oberstes": 0, "Mitte": 1, "Unterstes": 2}
        (typ, l, t, h), _ = item
        return (order.get(typ, 99), l, t, h)

    for (typ, l, t, h), count in sorted(grouped.items(), key=sort_key):
        pos += 1
        bemerkung = "Automatisch aus Brep"
        csv_extra.append("{};{};{:.1f};{:.1f};{:.1f};{};{}".format(
            pos, typ, l, t, h, count, bemerkung
        ))
        report_extra.append("{:<5} {:<13} {:>8.1f} {:>8.1f} {:>8.1f}".format(
            count, typ, l, t, h
        ))

    return csv_extra, report_extra, invalid_count, pos


def merge_sources(*sources):
    out = []
    for s in sources:
        out.extend(to_brep_list(s))
    return out


def has_brett_info_in_text_lines(lines):
    for line in lines:
        low = str(line).strip().lower()
        if "stueckliste bretter" in low:
            return True
        if low.startswith("verlegeplan"):
            return True
    return False


def resolve_export_folder(user_folder):
    if user_folder is not None and str(user_folder).strip() != "":
        folder = os.path.abspath(str(user_folder))
        return folder, False

    try:
        doc_path = Rhino.RhinoDoc.ActiveDoc.Path if Rhino.RhinoDoc.ActiveDoc is not None else None
    except Exception:
        doc_path = None

    if doc_path and str(doc_path).strip() != "":
        return os.path.dirname(doc_path), True

    desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
    return os.path.abspath(str(desktop)), True


def create_export_run_folder(base_folder, base_name):
    """Legt pro Exportlauf einen eigenen Unterordner an und gibt dessen Pfad zurueck."""
    safe_name = sanitize_token(base_name, "Export")
    run_folder = os.path.join(base_folder, safe_name)

    if not os.path.exists(run_folder):
        os.makedirs(run_folder)
        return run_folder

    # Falls derselbe Name doch schon existiert: fortlaufenden Suffix verwenden.
    i = 1
    while True:
        candidate = os.path.join(base_folder, "{}_{}".format(safe_name, str(i).zfill(2)))
        if not os.path.exists(candidate):
            os.makedirs(candidate)
            return candidate
        i += 1


def build_report_lines(base_name, stueckliste_lines, support_lines, param_names, param_values, info_lines):
    lines = []
    stamp = company_stamp_text()
    lines.append("=" * 68)
    lines.append("EXPORT REPORT - STUECKLISTE")
    lines.append("=" * 68)
    lines.append("Basisname: " + base_name)
    lines.append("Zeitstempel: " + datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    lines.append(stamp)
    lines.append("")

    lines.append("STUECKLISTE")
    lines.append("-" * 68)
    if stueckliste_lines:
        lines.extend(stueckliste_lines)
    else:
        lines.append("(keine stueckliste_text Eingabe vorhanden)")

    lines.append("")
    lines.append("ZUSATZTEILE (RUECKWAENDE / ANSAUGFLAECHEN)")
    lines.append("-" * 68)
    if support_lines:
        lines.append("{:<5} {:<13} {:>8} {:>8} {:>8}".format(
            "Anz.", "Typ", "L(mm)", "B(mm)", "D(mm)"))
        lines.append("-" * 68)
        lines.extend(support_lines)
    else:
        lines.append("(keine zusaetzlichen Brep-Teile uebergeben)")

    lines.append("")
    lines.append("PARAMETER")
    lines.append("-" * 68)

    if not param_names and not param_values:
        lines.append("(keine Parameter uebergeben)")
    else:
        n = max(len(param_names), len(param_values))
        for i in range(n):
            name = param_names[i] if i < len(param_names) else "Parameter_{}".format(i + 1)
            value = param_values[i] if i < len(param_values) else ""
            lines.append("{}: {}".format(name, value))

        if len(param_names) != len(param_values):
            lines.append("")
            lines.append("WARNUNG: parameter_namen und parameter_werte haben unterschiedliche Laenge.")

    if info_lines:
        lines.append("")
        lines.append("ZUSATZINFO")
        lines.append("-" * 68)
        lines.extend(info_lines)

        lines.append("")
        lines.append("WEITERE KOMPONENTEN-INFORMATIONEN")
        lines.append("-" * 68)
        lines.extend(info_lines)

    lines.append("")
    lines.append("=" * 68)
    lines.append(stamp)

    return lines


def _xml_escape(text):
    s = str(text)
    # Nur XML-1.0-gueltige Unicode-Zeichen zulassen.
    # Das vermeidet Importprobleme in Excel bei Sonder-/Steuerzeichen.
    def _is_valid_xml_char(ch):
        cp = ord(ch)
        return (
            cp == 0x9 or cp == 0xA or cp == 0xD or
            (0x20 <= cp <= 0xD7FF) or
            (0xE000 <= cp <= 0xFFFD) or
            (0x10000 <= cp <= 0x10FFFF)
        )

    s = "".join(ch for ch in s if _is_valid_xml_char(ch))
    s = s.replace("&", "&amp;")
    s = s.replace("<", "&lt;")
    s = s.replace(">", "&gt;")
    s = s.replace('"', "&quot;")
    return s


def write_excel_xml_table(path, header, rows):
    """Schreibt eine Excel-kompatible SpreadsheetML-Datei (.xml)."""
    lines = []
    lines.append('<?xml version="1.0"?>')
    lines.append('<?mso-application progid="Excel.Sheet"?>')
    lines.append('<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"')
    lines.append(' xmlns:o="urn:schemas-microsoft-com:office:office"')
    lines.append(' xmlns:x="urn:schemas-microsoft-com:office:excel"')
    lines.append(' xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">')
    lines.append('<Worksheet ss:Name="BOM">')
    lines.append('<Table>')

    lines.append('<Row>')
    for h in header:
        lines.append('<Cell><Data ss:Type="String">{}</Data></Cell>'.format(_xml_escape(h)))
    lines.append('</Row>')

    for row in rows:
        cols = split_csv_row(row)
        lines.append('<Row>')
        for c in cols:
            lines.append('<Cell><Data ss:Type="String">{}</Data></Cell>'.format(_xml_escape(c)))
        lines.append('</Row>')

    lines.append('</Table>')
    lines.append('</Worksheet>')
    lines.append('</Workbook>')

    # utf-8-sig schreibt BOM, was Excel-Import auf manchen Systemen stabilisiert.
    with open(path, "w", encoding="utf-8-sig") as f:
        f.write("\n".join(lines))

    return path


def write_report_csv(path, lines):
    """Schreibt den kompletten Report zeilenweise in eine CSV (eine Spalte)."""
    # CSV export: match XML structure
    stamp = company_stamp_text()
    headers = [
        "Abschnitt", "Pos", "Anz", "Typ", "L(mm)", "B(mm)", "D(mm)", "Z(mm)", "Parameter", "Wert", "Text"
    ]
    # Parse XML rows
    def parse_xml_rows(lines):
        xml_rows = []
        current_section = ""
        for raw in lines:
            line = str(raw).strip()
            if line == "":
                continue
            low = line.lower()
            if line.startswith("=") or line.startswith("-"):
                continue
            if low in (
                "stueckliste",
                "verlegeplan (unten -> oben)",
                "zusatzteile (rueckwaende / ansaugflaechen)",
                "parameter",
                "zusatzinfo",
                "weitere komponenten-informationen"
            ):
                current_section = line
                xml_rows.append([current_section] + ["" for _ in range(len(headers) - 1)])
                continue
            # Tabellenkopfzeilen auslassen
            if low.startswith("anz.") or low.startswith("pos"):
                continue
            # Verlegeplan-Zeile: Pos Typ L B D Z
            m_plan = re.match(
                r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
                line
            )
            if m_plan:
                xml_rows.append([
                    current_section,
                    m_plan.group(1), "", m_plan.group(2).strip(), m_plan.group(3), m_plan.group(4), m_plan.group(5), m_plan.group(6).replace(" ", ""), "", "", ""
                ])
                continue
            # Stueckliste/Zusatzteile: Anz Typ L B D
            m_table = re.match(
                r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$",
                line
            )
            if m_table:
                xml_rows.append([
                    current_section,
                    "", m_table.group(1), m_table.group(2).strip(), m_table.group(3), m_table.group(4), m_table.group(5), "", "", "", ""
                ])
                continue
            # Parameterzeile: Name: Wert
            if ":" in line and current_section.lower() == "parameter":
                parts = line.split(":", 1)
                xml_rows.append([
                    current_section,
                    "", "", "", "", "", "", "", parts[0].strip(), parts[1].strip(), ""
                ])
                continue
            # Fallback: Textzeile behalten
            xml_rows.append([
                current_section,
                "", "", "", "", "", "", "", "", "", line
            ])
        # Add metadata row
        xml_rows.insert(0, ["METADATA", "", "", "", "", "", "", "", "", "", stamp])
        return xml_rows

    xml_rows = parse_xml_rows(lines)
    with open(path, "w", encoding="utf-8") as f:
        f.write(";".join(headers) + "\n")
        for row in xml_rows:
            f.write(";".join([str(c) for c in row]) + "\n")
    return path
def write_report_txt(path, lines):
    # TXT export: match XML structure
    stamp = company_stamp_text()
    headers = [
        "Abschnitt", "Pos", "Anz", "Typ", "L(mm)", "B(mm)", "D(mm)", "Z(mm)", "Parameter", "Wert", "Text"
    ]
    xml_rows = []
    current_section = ""
    for raw in lines:
        line = str(raw).strip()
        if line == "":
            continue
        low = line.lower()
        if line.startswith("=") or line.startswith("-"):
            continue
        if low in (
            "stueckliste",
            "verlegeplan (unten -> oben)",
            "zusatzteile (rueckwaende / ansaugflaechen)",
            "parameter",
            "zusatzinfo",
            "weitere komponenten-informationen"
        ):
            current_section = line
            xml_rows.append([current_section] + ["" for _ in range(len(headers) - 1)])
            continue
        # Tabellenkopfzeilen auslassen
        if low.startswith("anz.") or low.startswith("pos"):
            continue
        m_plan = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_plan:
            xml_rows.append([
                current_section,
                m_plan.group(1), "", m_plan.group(2).strip(), m_plan.group(3), m_plan.group(4), m_plan.group(5), m_plan.group(6).replace(" ", ""), "", "", ""
            ])
            continue
        m_table = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_table:
            xml_rows.append([
                current_section,
                "", m_table.group(1), m_table.group(2).strip(), m_table.group(3), m_table.group(4), m_table.group(5), "", "", "", ""
            ])
            continue
        if ":" in line and current_section.lower() == "parameter":
            parts = line.split(":", 1)
            xml_rows.append([
                current_section,
                "", "", "", "", "", "", "", parts[0].strip(), parts[1].strip(), ""
            ])
            continue
        xml_rows.append([
            current_section,
            "", "", "", "", "", "", "", "", "", line
        ])
    xml_rows.insert(0, ["METADATA", "", "", "", "", "", "", "", "", "", stamp])
    # Write as formatted TXT table
    with open(path, "w", encoding="utf-8") as f:
        f.write("\t".join(headers) + "\n")
        for row in xml_rows:
            f.write("\t".join([str(c) for c in row]) + "\n")
    return path


def write_excel_xml_report(path, lines):
    """Schreibt den kompletten Report als strukturierte Excel-XML-Tabelle."""
    stamp = company_stamp_text()

    def empty_row(section=""):
        return {
            "Abschnitt": section,
            "Pos": "",
            "Anz": "",
            "Typ": "",
            "L(mm)": "",
            "B(mm)": "",
            "D(mm)": "",
            "Z(mm)": "",
            "Parameter": "",
            "Wert": "",
            "Text": ""
        }

    headers = [
        "Abschnitt", "Pos", "Anz", "Typ", "L(mm)", "B(mm)", "D(mm)",
        "Z(mm)", "Parameter", "Wert", "Text"
    ]

    current_section = ""
    rows = []

    stamp_row = empty_row("METADATA")
    stamp_row["Text"] = stamp
    rows.append(stamp_row)

    for raw in lines:
        line = str(raw).strip()
        if line == "":
            rows.append(empty_row(current_section))
            continue

        low = line.lower()

        # Ueberschriften und Trenner
        if line.startswith("=") or line.startswith("-"):
            continue
        if low in (
            "stueckliste",
            "verlegeplan (unten -> oben)",
            "zusatzteile (rueckwaende / ansaugflaechen)",
            "parameter",
            "zusatzinfo",
            "weitere komponenten-informationen"
        ):
            current_section = line
            r = empty_row(current_section)
            r["Text"] = line
            rows.append(r)
            continue

        # Tabellenkopfzeilen auslassen
        if low.startswith("anz.") or low.startswith("pos"):
            continue

        # Verlegeplan-Zeile: Pos Typ L B D Z
        m_plan = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_plan:
            r = empty_row(current_section)
            r["Pos"] = m_plan.group(1)
            r["Typ"] = m_plan.group(2).strip()
            r["L(mm)"] = m_plan.group(3)
            r["B(mm)"] = m_plan.group(4)
            r["D(mm)"] = m_plan.group(5)
            r["Z(mm)"] = m_plan.group(6).replace(" ", "")
            rows.append(r)
            continue

        # Stueckliste/Zusatzteile: Anz Typ L B D
        m_table = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_table:
            r = empty_row(current_section)
            r["Anz"] = m_table.group(1)
            r["Typ"] = m_table.group(2).strip()
            r["L(mm)"] = m_table.group(3)
            r["B(mm)"] = m_table.group(4)
            r["D(mm)"] = m_table.group(5)
            rows.append(r)
            continue

        # Parameterzeile: Name: Wert
        if ":" in line and current_section.lower() == "parameter":
            parts = line.split(":", 1)
            r = empty_row(current_section)
            r["Parameter"] = parts[0].strip()
            r["Wert"] = parts[1].strip()
            rows.append(r)
            continue

        # Fallback: Textzeile behalten
        r = empty_row(current_section)
        r["Text"] = line
        rows.append(r)

    xml_lines = []
    xml_lines.append('<?xml version="1.0"?>')
    xml_lines.append('<?mso-application progid="Excel.Sheet"?>')
    xml_lines.append('<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"')
    xml_lines.append(' xmlns:o="urn:schemas-microsoft-com:office:office"')
    xml_lines.append(' xmlns:x="urn:schemas-microsoft-com:office:excel"')
    xml_lines.append(' xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">')
    xml_lines.append('<Worksheet ss:Name="Report">')
    xml_lines.append('<Table>')
    xml_lines.append('<Row>')
    for h in headers:
        xml_lines.append('<Cell><Data ss:Type="String">{}</Data></Cell>'.format(_xml_escape(h)))
    xml_lines.append('</Row>')

    for row in rows:
        xml_lines.append('<Row>')
        for h in headers:
            xml_lines.append('<Cell><Data ss:Type="String">{}</Data></Cell>'.format(_xml_escape(row.get(h, ""))))
        xml_lines.append('</Row>')

    xml_lines.append('</Table>')
    xml_lines.append('</Worksheet>')
    xml_lines.append('</Workbook>')

    # utf-8-sig schreibt BOM, was Excel-Import auf manchen Systemen stabilisiert.
    with open(path, "w", encoding="utf-8-sig") as f:
        f.write("\n".join(xml_lines))

    return path


def _xlsx_col_name(col_idx_1based):
    """1 -> A, 26 -> Z, 27 -> AA"""
    n = int(col_idx_1based)
    out = []
    while n > 0:
        n, rem = divmod(n - 1, 26)
        out.append(chr(ord("A") + rem))
    return "".join(reversed(out))


def _build_report_table_rows(lines):
    """Parst Report-Lines in tabellarische Zeilen fuer Excel-Export."""
    headers = [
        "Abschnitt", "Pos", "Anz", "Typ", "L(mm)", "B(mm)", "D(mm)",
        "Z(mm)", "Parameter", "Wert", "Text"
    ]

    def empty_row(section=""):
        return {
            "Abschnitt": section,
            "Pos": "",
            "Anz": "",
            "Typ": "",
            "L(mm)": "",
            "B(mm)": "",
            "D(mm)": "",
            "Z(mm)": "",
            "Parameter": "",
            "Wert": "",
            "Text": ""
        }

    current_section = ""
    rows = []

    stamp_row = empty_row("METADATA")
    stamp_row["Text"] = company_stamp_text()
    rows.append(stamp_row)

    for raw in lines:
        line = str(raw).strip()
        if line == "":
            rows.append(empty_row(current_section))
            continue

        low = line.lower()

        if line.startswith("=") or line.startswith("-"):
            continue
        if low in (
            "stueckliste",
            "verlegeplan (unten -> oben)",
            "zusatzteile (rueckwaende / ansaugflaechen)",
            "parameter",
            "zusatzinfo",
            "weitere komponenten-informationen"
        ):
            current_section = line
            r = empty_row(current_section)
            r["Text"] = line
            rows.append(r)
            continue

        if low.startswith("anz.") or low.startswith("pos"):
            continue

        m_plan = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_plan:
            r = empty_row(current_section)
            r["Pos"] = m_plan.group(1)
            r["Typ"] = m_plan.group(2).strip()
            r["L(mm)"] = m_plan.group(3)
            r["B(mm)"] = m_plan.group(4)
            r["D(mm)"] = m_plan.group(5)
            r["Z(mm)"] = m_plan.group(6).replace(" ", "")
            rows.append(r)
            continue

        m_table = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_table:
            r = empty_row(current_section)
            r["Anz"] = m_table.group(1)
            r["Typ"] = m_table.group(2).strip()
            r["L(mm)"] = m_table.group(3)
            r["B(mm)"] = m_table.group(4)
            r["D(mm)"] = m_table.group(5)
            rows.append(r)
            continue

        if ":" in line and current_section.lower() == "parameter":
            parts = line.split(":", 1)
            r = empty_row(current_section)
            r["Parameter"] = parts[0].strip()
            r["Wert"] = parts[1].strip()
            rows.append(r)
            continue

        r = empty_row(current_section)
        r["Text"] = line
        rows.append(r)

    return headers, rows


def write_excel_xlsx_report(path, lines):
    """Schreibt einen echten .xlsx-Report (OpenXML) ohne externe Abhaengigkeiten."""
    headers, rows = _build_report_table_rows(lines)

    all_rows = [headers] + [[str(r.get(h, "")) for h in headers] for r in rows]
    last_row = max(1, len(all_rows))
    last_col = _xlsx_col_name(len(headers))
    dim = "A1:{}{}".format(last_col, last_row)

    sheet_lines = []
    sheet_lines.append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    sheet_lines.append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"')
    sheet_lines.append(' xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">')
    sheet_lines.append('<dimension ref="{}"/>'.format(dim))
    sheet_lines.append('<sheetViews><sheetView workbookViewId="0"/></sheetViews>')
    sheet_lines.append('<sheetFormatPr defaultRowHeight="15"/>')
    sheet_lines.append('<sheetData>')

    for r_idx, row_vals in enumerate(all_rows, start=1):
        sheet_lines.append('<row r="{}">'.format(r_idx))
        for c_idx, raw_val in enumerate(row_vals, start=1):
            cell_ref = "{}{}".format(_xlsx_col_name(c_idx), r_idx)
            val = _xml_escape(raw_val)
            sheet_lines.append('<c r="{}" t="inlineStr"><is><t xml:space="preserve">{}</t></is></c>'.format(cell_ref, val))
        sheet_lines.append('</row>')

    sheet_lines.append('</sheetData>')
    sheet_lines.append('</worksheet>')
    sheet_xml = "\n".join(sheet_lines)

    content_types = """<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">
  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>
  <Default Extension=\"xml\" ContentType=\"application/xml\"/>
  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>
  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>
  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>
</Types>"""

    rels_root = """<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">
  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>
</Relationships>"""

    workbook_xml = """<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">
  <sheets>
    <sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/>
  </sheets>
</workbook>"""

    workbook_rels = """<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">
  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>
  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>
</Relationships>"""

    styles_xml = """<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">
  <fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>
  <fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>
  <borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>
  <cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>
  <cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>
</styleSheet>"""

    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", rels_root)
        zf.writestr("xl/workbook.xml", workbook_xml)
        zf.writestr("xl/_rels/workbook.xml.rels", workbook_rels)
        zf.writestr("xl/worksheets/sheet1.xml", sheet_xml)
        zf.writestr("xl/styles.xml", styles_xml)

    return path


def _pdf_safe_line(text):
    s = str(text)
    s = s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
    # PDF-Standardfont nutzt WinAnsi; nicht darstellbare Zeichen werden ersetzt.
    return s.encode("cp1252", "replace").decode("cp1252")


def write_table_pdf(path, title, report_lines):
    """Schreibt einen strukturierten DIN-A4-PDF-Report.
    Parsing identisch zu CSV/XML: Abschnitte mit passenden Spalten."""

    KNOWN_SECTIONS = [
        "stueckliste",
        "verlegeplan (unten -> oben)",
        "zusatzteile (rueckwaende / ansaugflaechen)",
        "parameter",
        "zusatzinfo",
        "weitere komponenten-informationen",
    ]

    # ---- Parsing (identisch zu write_report_csv / write_excel_xml_report) ----
    sections = []  # [(section_label, [row_dicts])]
    current_section = ""
    current_rows = []

    for raw in report_lines:
        line = str(raw).strip()
        if line == "":
            continue
        low = line.lower()
        if line.startswith("=") or line.startswith("-"):
            continue
        # Metadaten-Kopfzeilen ueberspringen
        if low.startswith("basisname:") or low.startswith("zeitstempel:") or "parametric.solutions" in low:
            continue
        if low in KNOWN_SECTIONS:
            if current_section and current_rows:
                sections.append((current_section, current_rows))
            elif current_section and not current_rows:
                sections.append((current_section, []))
            current_section = line
            current_rows = []
            continue
        # Tabellenkopfzeilen auslassen
        if low.startswith("anz.") or low.startswith("pos ") or low == "pos":
            continue
        # Verlegeplan-Zeile: Pos Typ L B D Z
        m_plan = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([+-]?[0-9]+(?:\.[0-9]+)?\s*-\s*[+-]?[0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_plan:
            current_rows.append({
                "Pos": m_plan.group(1),
                "Typ": m_plan.group(2).strip(),
                "L(mm)": m_plan.group(3),
                "B(mm)": m_plan.group(4),
                "D(mm)": m_plan.group(5),
                "Z(mm)": m_plan.group(6).replace(" ", ""),
            })
            continue
        # Stueckliste/Zusatzteile: Anz Typ L B D
        m_table = re.match(
            r"^\s*(\d+)\s+(.+?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*$",
            line
        )
        if m_table:
            current_rows.append({
                "Anz": m_table.group(1),
                "Typ": m_table.group(2).strip(),
                "L(mm)": m_table.group(3),
                "B(mm)": m_table.group(4),
                "D(mm)": m_table.group(5),
            })
            continue
        # Parameterzeile: Name: Wert
        if ":" in line and current_section.lower() == "parameter":
            parts = line.split(":", 1)
            current_rows.append({"Parameter": parts[0].strip(), "Wert": parts[1].strip()})
            continue
        # Fallback-Text
        current_rows.append({"Text": line})

    if current_section:
        sections.append((current_section, current_rows))

    # ---- Zusatzteile unter Stueckliste einmergen ----
    final_sections = []
    stueck_rows_ref = None
    for sec_name, sec_rows in sections:
        if sec_name.lower() == "stueckliste":
            final_sections.append((sec_name, sec_rows))
            stueck_rows_ref = sec_rows
        elif "zusatzteile" in sec_name.lower() and stueck_rows_ref is not None:
            stueck_rows_ref.append({"__sub__": sec_name})
            stueck_rows_ref.extend(sec_rows)
        else:
            final_sections.append((sec_name, sec_rows))
    sections = final_sections

    # ---- Spaltendefinition je Abschnitt ----
    def get_cols_and_widths(section_name):
        low = section_name.lower()
        if "verlegeplan" in low:
            cols  = ["Pos",  "Typ",  "L(mm)", "B(mm)", "D(mm)", "Z(mm)"]
            widths = [35,    130,     65,       65,       65,       75]
        elif "zusatzteile" in low:
            cols  = ["Anz",  "Typ",  "L(mm)", "B(mm)", "D(mm)"]
            widths = [35,    155,     85,       85,       75]
        elif "stueckliste" in low:
            cols  = ["Anz",  "Typ",  "L(mm)", "B(mm)", "D(mm)"]
            widths = [35,    155,     85,       85,       75]
        elif "parameter" in low:
            cols  = ["Parameter", "Wert"]
            widths = [200,         335]
        else:
            cols  = ["Text"]
            widths = [535]
        return cols, widths

    # ---- Layout-Konstanten ----
    page_width   = 595
    page_height  = 842
    left         = 30
    right        = 565
    top_content  = 810   # Oberkante Inhalt
    bottom_margin = 45
    row_h        = 13
    sec_h        = 20    # Hoehe Abschnittszeile
    hdr_h        = 16    # Hoehe Spaltenkoepfe
    stamp        = company_stamp_text()
    date_str     = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    # ---- Render-Items aufbauen ----
    # Jedes Item: (item_height, item_type, payload)
    items = []
    for sec_name, sec_rows in sections:
        cols, widths = get_cols_and_widths(sec_name)
        items.append((sec_h,  "section",    (sec_name, cols, widths)))
        items.append((hdr_h,  "col_header", (cols, widths)))
        for row_dict in sec_rows:
            if "__sub__" in row_dict:
                items.append((14, "sub_section", (row_dict["__sub__"], cols, widths)))
            else:
                items.append((row_h, "data_row", ([row_dict.get(c, "") for c in cols], widths)))

    # ---- Paginierung ----
    pages = []
    current_page = []
    y = top_content - 30  # Platz fuer Seitenkopf

    for item in items:
        needed = item[0]
        if y - needed < bottom_margin and current_page:
            pages.append(current_page)
            current_page = []
            y = top_content - 30
        current_page.append((y, item))
        y -= needed

    if current_page:
        pages.append(current_page)
    if not pages:
        pages = [[]]

    # ---- PDF-Objekte ----
    obj_data = {}
    next_id  = 1
    catalog_id  = next_id; next_id += 1
    pages_id    = next_id; next_id += 1
    font_id     = next_id; next_id += 1
    font_bold_id = next_id; next_id += 1

    page_ids    = []
    content_ids = []
    for _ in pages:
        page_ids.append(next_id);    next_id += 1
        content_ids.append(next_id); next_id += 1

    obj_data[font_id]      = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    obj_data[font_bold_id] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"

    for pidx, page_items in enumerate(pages):
        sl = []

        # ---- Seitenkopf ----
        sl.append("BT")
        sl.append("/F2 10 Tf")
        sl.append("1 0 0 1 {} {} Tm".format(left, top_content))
        page_title = title if len(pages) == 1 else "{} - Seite {} / {}".format(title, pidx + 1, len(pages))
        sl.append("({}) Tj".format(_pdf_safe_line(page_title)))
        sl.append("/F1 7 Tf")
        sl.append("1 0 0 1 {} {} Tm".format(left, top_content - 11))
        sl.append("({}) Tj".format(_pdf_safe_line(date_str + "    " + stamp)))
        sl.append("ET")
        # Trennlinie Kopf
        sl.append("{} {} m {} {} l S".format(left, top_content - 15, right, top_content - 15))

        # ---- Abschnitts-Items rendern ----
        for (y_pos, item) in page_items:
            itype   = item[1]
            payload = item[2]

            if itype == "section":
                sec_name, cols, widths = payload
                # Farbige Trennlinie vor Abschnitt
                sl.append("{} {} m {} {} l S".format(left, y_pos + 1, right, y_pos + 1))
                sl.append("BT")
                sl.append("/F2 9 Tf")
                sl.append("1 0 0 1 {} {} Tm".format(left, y_pos - 12))
                sl.append("({}) Tj".format(_pdf_safe_line(sec_name.upper())))
                sl.append("ET")

            elif itype == "sub_section":
                sub_name, cols, widths = payload
                # Duenne Trennlinie + kleines Sub-Label
                sl.append("0.5 w {} {} m {} {} l S 1 w".format(left, y_pos - 2, right, y_pos - 2))
                sl.append("BT")
                sl.append("/F2 7 Tf")
                sl.append("1 0 0 1 {} {} Tm".format(left, y_pos - 11))
                sl.append("({}) Tj".format(_pdf_safe_line(sub_name)))
                sl.append("ET")

            elif itype == "col_header":
                cols, widths = payload
                sl.append("BT")
                sl.append("/F2 7 Tf")
                x = left
                for col, w in zip(cols, widths):
                    sl.append("1 0 0 1 {} {} Tm".format(x + 2, y_pos - 11))
                    sl.append("({}) Tj".format(_pdf_safe_line(col)))
                    x += w
                sl.append("ET")
                # Linie unter Spaltenkoepfe
                sl.append("{} {} m {} {} l S".format(left, y_pos - hdr_h, right, y_pos - hdr_h))

            elif itype == "data_row":
                cells, widths = payload
                sl.append("BT")
                sl.append("/F1 7 Tf")
                x = left
                for cell, w in zip(cells, widths):
                    sl.append("1 0 0 1 {} {} Tm".format(x + 2, y_pos - 9))
                    sl.append("({}) Tj".format(_pdf_safe_line(cell)))
                    x += w
                sl.append("ET")

        # ---- Seitenfuss ----
        sl.append("{} {} m {} {} l S".format(left, bottom_margin, right, bottom_margin))
        sl.append("BT")
        sl.append("/F1 7 Tf")
        sl.append("1 0 0 1 {} {} Tm".format(left, bottom_margin - 12))
        sl.append("({}) Tj".format(_pdf_safe_line(stamp)))
        if len(pages) > 1:
            sl.append("1 0 0 1 {} {} Tm".format(right - 40, bottom_margin - 12))
            sl.append("({}/{}) Tj".format(pidx + 1, len(pages)))
        sl.append("ET")

        stream = "\n".join(sl)
        content_obj = "<< /Length {} >>\nstream\n{}\nendstream".format(
            len(stream.encode("cp1252", "replace")), stream
        )
        obj_data[content_ids[pidx]] = content_obj

        page_obj = (
            "<< /Type /Page /Parent {} 0 R /MediaBox [0 0 {} {}] "
            "/Resources << /Font << /F1 {} 0 R /F2 {} 0 R >> >> /Contents {} 0 R >>"
        ).format(pages_id, page_width, page_height, font_id, font_bold_id, content_ids[pidx])
        obj_data[page_ids[pidx]] = page_obj

    kids = " ".join(["{} 0 R".format(pid) for pid in page_ids])
    obj_data[pages_id] = "<< /Type /Pages /Kids [ {} ] /Count {} >>".format(kids, len(page_ids))
    obj_data[catalog_id] = "<< /Type /Catalog /Pages {} 0 R >>".format(pages_id)

    out = []
    out.append("%PDF-1.4\n")
    offsets = [0]

    for oid in range(1, next_id):
        offsets.append(sum(len(part.encode("cp1252", "replace")) for part in out))
        out.append("{} 0 obj\n{}\nendobj\n".format(oid, obj_data.get(oid, "<< ")))

    xref_pos = sum(len(part.encode("cp1252", "replace")) for part in out)
    out.append("xref\n")
    out.append("0 {}\n".format(next_id))
    out.append("0000000000 65535 f \n")
    for oid in range(1, next_id):
        out.append("{:010d} 00000 n \n".format(offsets[oid]))

    out.append("trailer\n")
    out.append("<< /Size {} /Root {} 0 R >>\n".format(next_id, catalog_id))
    out.append("startxref\n")
    out.append("{}\n".format(xref_pos))
    out.append("%%EOF\n")

    with open(path, "wb") as f:
        for part in out:
            f.write(part.encode("cp1252", "replace"))

    return path


def write_views_pdf(path, title, breps, show_edges=False, front_breps=None):
    """Technische 4-Ansichten-Zeichnung (ISO + Front + Seite + Draufsicht) mit BBox-Bemaßung.
    show_edges=True  : Brep-Originalkanten in Rot ueberlagern.
    front_breps      : optionale Frontgeometrie, Kanten ebenfalls in Rot."""
    import math as _m

    if front_breps is None:
        front_breps = []

    # BBox aller Eingabe-Geometrien (inkl. frontgeometrie fuer korrekte Normierung)
    all_src = list(breps) + list(front_breps)
    mn = [None, None, None]
    mx2 = [None, None, None]
    for b in all_src:
        if b is None or not b.IsValid:
            continue
        bb = b.GetBoundingBox(True)
        if not bb.IsValid:
            continue
        for i, v in enumerate([bb.Min.X, bb.Min.Y, bb.Min.Z]):
            if mn[i] is None or v < mn[i]:
                mn[i] = v
        for i, v in enumerate([bb.Max.X, bb.Max.Y, bb.Max.Z]):
            if mx2[i] is None or v > mx2[i]:
                mx2[i] = v

    has_geo = mn[0] is not None
    DL = max((mx2[0] - mn[0]) if has_geo else 100.0, 1.0)
    DB = max((mx2[1] - mn[1]) if has_geo else 60.0,  1.0)
    DH = max((mx2[2] - mn[2]) if has_geo else 80.0,  1.0)

    # Ursprung fuer Normierung (alle Kanten werden relativ zum BBox-Min dargestellt)
    ox3 = mn[0] if has_geo else 0.0
    oy3 = mn[1] if has_geo else 0.0
    oz3 = mn[2] if has_geo else 0.0

    # ---- Tatsaechliche Brep-Kanten (Hauptansicht, schwarz) ----
    # Alle echten Kanten aus den Breps werden extrahiert und als Wireframe gezeichnet.
    def _extract_edges(source_breps):
        """Gibt Liste von [(x,y,z),...]-Polylinien zurueck (normiert auf BBox-Ursprung)."""
        result = []
        for b in source_breps:
            if b is None:
                continue
            try:
                if not b.IsValid:
                    continue
                for edge in b.Edges:
                    try:
                        crv = edge.EdgeCurve
                        if crv is None:
                            continue
                        if crv.IsLinear(0.1):
                            p0 = crv.PointAtStart
                            p1 = crv.PointAtEnd
                            result.append([
                                (p0.X - ox3, p0.Y - oy3, p0.Z - oz3),
                                (p1.X - ox3, p1.Y - oy3, p1.Z - oz3),
                            ])
                        else:
                            try:
                                length = crv.GetLength()
                            except Exception:
                                length = 50.0
                            n_segs = max(6, min(48, int(length / 10.0)))
                            params = crv.DivideByCount(n_segs, True)
                            if params is not None and len(params) > 1:
                                poly = []
                                for t in params:
                                    pt = crv.PointAt(t)
                                    poly.append((pt.X - ox3, pt.Y - oy3, pt.Z - oz3))
                                if len(poly) > 1:
                                    result.append(poly)
                    except Exception:
                        pass
            except Exception:
                pass
        return result

    brep_edge_polys = _extract_edges(breps)
    # Fallback: gesamt-BBox wenn keine Geometrie vorhanden
    if not brep_edge_polys:
        C_fb = [(0,0,0),(DL,0,0),(DL,DB,0),(0,DB,0),(0,0,DH),(DL,0,DH),(DL,DB,DH),(0,DB,DH)]
        for i, j in [(0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7)]:
            brep_edge_polys.append([C_fb[i], C_fb[j]])

    # Frontgeometrie-Kanten (rot)
    front_edge_polys = _extract_edges(front_breps)

    # BBox-Sihouetten je Brep (fuer show_edges-Modus in grau)
    brep_box_corners = []
    if show_edges:
        for b in breps:
            if b is None or not b.IsValid:
                continue
            bb = b.GetBoundingBox(True)
            if not bb.IsValid:
                continue
            x0 = bb.Min.X - ox3; y0 = bb.Min.Y - oy3; z0 = bb.Min.Z - oz3
            x1 = bb.Max.X - ox3; y1 = bb.Max.Y - oy3; z1 = bb.Max.Z - oz3
            if abs(x1 - x0) < 0.5: x1 = x0 + 0.5
            if abs(y1 - y0) < 0.5: y1 = y0 + 0.5
            if abs(z1 - z0) < 0.5: z1 = z0 + 0.5
            brep_box_corners.append([
                (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
            ])

    # ---- 8 BBox-Ecken fuer Bemaßungslinien (normiert) ----
    C = [
        (0,  0,  0),    # 0
        (DL, 0,  0),    # 1
        (DL, DB, 0),    # 2
        (0,  DB, 0),    # 3
        (0,  0,  DH),   # 4
        (DL, 0,  DH),   # 5
        (DL, DB, DH),   # 6
        (0,  DB, DH),   # 7
    ]

    # ---- Projektionsfunktionen ----
    def v_front(p): return (p[0], p[2])            # XZ-Ebene (von vorne)
    def v_side(p):  return (p[1], p[2])            # YZ-Ebene (von rechts)
    def v_top(p):   return (p[0], DB - p[1])       # XY-Ebene (von oben, Y gespiegelt)
    def v_iso(p):
        c30 = _m.cos(_m.radians(30))
        s30 = _m.sin(_m.radians(30))
        z_contrib_xy = (DL + DB) * s30
        z_scale = max(1.0, z_contrib_xy * 0.30 / max(DH, 1.0))
        sx = (p[0] - p[1]) * c30
        sy = (p[0] + p[1]) * s30 + p[2] * z_scale
        return (sx, sy)

    def make_tf(proj, cx, cy, cw2h, ch2h, margin=32):
        pts = [proj(p) for p in C]
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        vw = max(max(xs) - min(xs), 1.0); vh = max(max(ys) - min(ys), 1.0)
        sc = min((cw2h - 2 * margin) / vw, (ch2h - 2 * margin) / vh) * 0.78
        ox = cx - (min(xs) + vw / 2) * sc
        oy = cy - (min(ys) + vh / 2) * sc
        return (lambda x2, y2: (ox + x2 * sc, oy + y2 * sc)), sc

    # Bemaßungslinie mit Hilfslinien und Text
    def dim_line(sl2, p1, p2, label, offset):
        x1, y1 = p1; x2, y2 = p2
        dx, dy = x2 - x1, y2 - y1
        ln = _m.sqrt(dx * dx + dy * dy)
        if ln < 2:
            return
        nx = -dy / ln * offset; ny = dx / ln * offset
        sl2.append("0.35 w")
        sl2.append("{:.1f} {:.1f} m {:.1f} {:.1f} l S".format(x1, y1, x1 + nx, y1 + ny))
        sl2.append("{:.1f} {:.1f} m {:.1f} {:.1f} l S".format(x2, y2, x2 + nx, y2 + ny))
        sl2.append("{:.1f} {:.1f} m {:.1f} {:.1f} l S".format(x1 + nx, y1 + ny, x2 + nx, y2 + ny))
        sl2.append("1 w")
        tx = (x1 + x2) / 2 + nx; ty = (y1 + y2) / 2 + ny
        tw = len(label) * 3.5
        ty_text = ty + 3 if ny >= 0 else ty - 9
        sl2.append("BT /F1 6.5 Tf 1 0 0 1 {:.1f} {:.1f} Tm ({}) Tj ET".format(
            tx - tw / 2, ty_text, _pdf_safe_line(label)))

    # Hilfsfunktion: Polylinien-Liste projizieren und zeichnen
    def _draw_polys(sl2, tf2, proj, polys):
        for poly in polys:
            projected = [tf2(proj(p)[0], proj(p)[1]) for p in poly]
            if len(projected) < 2:
                continue
            parts = ["{:.1f} {:.1f} m".format(projected[0][0], projected[0][1])]
            for pt in projected[1:]:
                parts.append("{:.1f} {:.1f} l".format(pt[0], pt[1]))
            sl2.append(" ".join(parts) + " S")

    # Tatsaechliche Kanten (schwarz)
    def draw_geometry(sl2, tf2, proj):
        sl2.append("0.5 w")
        _draw_polys(sl2, tf2, proj, brep_edge_polys)
        sl2.append("1 w")

    # BBox-Silhouetten grau (show_edges=True) – jede Teil-BBox als Drahtrahmen
    def draw_edges_grey(sl2, tf2, proj):
        if not brep_box_corners:
            return
        BOX_E = [(0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7)]
        sl2.append("q")
        sl2.append("0.55 0.55 0.55 RG")
        sl2.append("0.35 w")
        for corners in brep_box_corners:
            for i, j in BOX_E:
                ax, ay = tf2(proj(corners[i])[0], proj(corners[i])[1])
                bx, by = tf2(proj(corners[j])[0], proj(corners[j])[1])
                if abs(ax - bx) < 0.1 and abs(ay - by) < 0.1:
                    continue
                sl2.append("{:.1f} {:.1f} m {:.1f} {:.1f} l S".format(ax, ay, bx, by))
        sl2.append("Q")

    # Frontgeometrie-Kanten in Rot
    def draw_front_red(sl2, tf2, proj):
        if not front_edge_polys:
            return
        sl2.append("q")
        sl2.append("1 0 0 RG")
        sl2.append("0.5 w")
        _draw_polys(sl2, tf2, proj, front_edge_polys)
        sl2.append("Q")


    # ---- Layout DIN A4 Hochformat 2x2 Raster ----
    pw, ph   = 595, 842
    title_h  = 42
    footer_h = 30
    pad      = 4
    avail_h  = ph - title_h - footer_h - pad
    cw2h     = pw / 2
    ch2h     = avail_h / 2
    stamp    = company_stamp_text()
    date_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    def cell_ctr(col, row):  # col 0=links, row 0=unten
        return cw2h * col + cw2h / 2, footer_h + pad + ch2h * row + ch2h / 2

    sl = []

    # Titelblock
    sl.append("BT /F2 11 Tf 1 0 0 1 10 {:.0f} Tm ({}) Tj ET".format(
        ph - 22, _pdf_safe_line(title + " - Technische Ansichten")))
    sl.append("BT /F1 7 Tf 1 0 0 1 10 {:.0f} Tm ({}) Tj ET".format(
        ph - 34, _pdf_safe_line(date_str + "   " + stamp)))
    sl.append("0.6 w 10 {:.0f} m 585 {:.0f} l S 1 w".format(ph - 38, ph - 38))

    # Zellentrennlinien
    sl.append("0.4 w")
    sl.append("{:.0f} {:.0f} m {:.0f} {:.0f} l S".format(cw2h, footer_h + pad, cw2h, ph - title_h))
    mid_y = footer_h + pad + ch2h
    sl.append("10 {:.0f} m 585 {:.0f} l S".format(mid_y, mid_y))
    sl.append("1 w")

    # ---- Ansichten rendern ----
    view_defs = [
        ("Vorderansicht",  v_front, 0, 1, False, "front"),
        ("Seitenansicht",  v_side,  1, 1, False, "side"),
        ("Draufsicht",     v_top,   0, 0, False, "top"),
        ("Isometrie",      v_iso,   1, 0, True,  "iso"),
    ]

    for vname, proj_fn, col, row, is_iso, vtype in view_defs:
        vcx, vcy = cell_ctr(col, row)
        tf_fn, sc = make_tf(proj_fn, vcx, vcy, cw2h - 4, ch2h - 8)

        # Ecken-Transformation mit fester Bindung (kein closure-Problem in Schleifen)
        def tp(idx, _pf=proj_fn, _tf=tf_fn):
            pp = _pf(C[idx]); return _tf(pp[0], pp[1])

        draw_geometry(sl, tf_fn, proj_fn)
        if show_edges:
            draw_edges_grey(sl, tf_fn, proj_fn)
        draw_front_red(sl, tf_fn, proj_fn)

        doff = 32
        if vtype == "front":
            # L: untere Kante (z=0), H: rechte Kante
            dim_line(sl, tp(0), tp(1), "{:.0f}mm".format(DL), -doff)
            dim_line(sl, tp(1), tp(5), "{:.0f}mm".format(DH), +doff)
            label = "{} | L={:.0f}  H={:.0f}".format(vname, DL, DH)
        elif vtype == "side":
            # B: untere Kante (z=0), H: rechte Kante
            dim_line(sl, tp(0), tp(3), "{:.0f}mm".format(DB), -doff)
            dim_line(sl, tp(3), tp(7), "{:.0f}mm".format(DH), +doff)
            label = "{} | B={:.0f}  H={:.0f}".format(vname, DB, DH)
        elif vtype == "top":
            # L: hintere Kante (y=DB, unten in Draufsicht), B: rechte Kante
            # v_top: C[3]=(0,DB,0)->(0,0), C[2]=(DL,DB,0)->(DL,0)  --> Unterkante
            # v_top: C[2]=(DL,DB,0)->(DL,0), C[1]=(DL,0,0)->(DL,DB) --> rechte Kante
            dim_line(sl, tp(3), tp(2), "{:.0f}mm".format(DL), -doff)
            dim_line(sl, tp(1), tp(2), "{:.0f}mm".format(DB), +doff)
            label = "{} | L={:.0f}  B={:.0f}".format(vname, DL, DB)
        else:  # iso
            # L: obere Frontkante, B: obere Seitenkante, H: rechte Vertikale
            dim_line(sl, tp(4), tp(5), "{:.0f}mm".format(DL), +doff)
            dim_line(sl, tp(5), tp(6), "{:.0f}mm".format(DB), -doff)
            dim_line(sl, tp(2), tp(6), "{:.0f}mm".format(DH), +doff)
            label = "{} | L={:.0f}  B={:.0f}  H={:.0f}".format(vname, DL, DB, DH)

        lx = cw2h * col + 6
        ly = footer_h + pad + ch2h * row + 5
        sl.append("BT /F2 7.5 Tf 1 0 0 1 {:.0f} {:.0f} Tm ({}) Tj ET".format(
            lx, ly, _pdf_safe_line(label)))

    # Footer
    sl.append("0.4 w 10 {:.0f} m 585 {:.0f} l S 1 w".format(footer_h - 1, footer_h - 1))
    sl.append("BT /F1 6.5 Tf 1 0 0 1 10 {:.0f} Tm ({}) Tj ET".format(
        footer_h - 14, _pdf_safe_line(stamp)))

    # ---- Single-Page PDF ----
    cat_id, pgs_id, fnt_id, fnb_id, pg_id, cnt_id, n_objs = 1, 2, 3, 4, 5, 6, 7
    obj_data = {}
    obj_data[fnt_id] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    obj_data[fnb_id] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
    stream = "\n".join(sl)
    obj_data[cnt_id] = "<< /Length {0} >>\nstream\n{1}\nendstream".format(
        len(stream.encode("cp1252", "replace")), stream)
    obj_data[pg_id] = (
        "<< /Type /Page /Parent {0} 0 R /MediaBox [0 0 {1} {2}] "
        "/Resources << /Font << /F1 {3} 0 R /F2 {4} 0 R >> >> /Contents {5} 0 R >>"
    ).format(pgs_id, pw, ph, fnt_id, fnb_id, cnt_id)
    obj_data[pgs_id] = "<< /Type /Pages /Kids [ {0} 0 R ] /Count 1 >>".format(pg_id)
    obj_data[cat_id] = "<< /Type /Catalog /Pages {0} 0 R >>".format(pgs_id)

    out = ["%PDF-1.4\n"]; offsets = [0]
    for oid in range(1, n_objs):
        offsets.append(sum(len(p.encode("cp1252", "replace")) for p in out))
        out.append("{0} 0 obj\n{1}\nendobj\n".format(oid, obj_data.get(oid, "<< >>")))
    xp = sum(len(p.encode("cp1252", "replace")) for p in out)
    out.append("xref\n0 {0}\n0000000000 65535 f \n".format(n_objs))
    for oid in range(1, n_objs):
        out.append("{0:010d} 00000 n \n".format(offsets[oid]))
    out.append("trailer\n<< /Size {0} /Root {1} 0 R >>\nstartxref\n{2}\n%%EOF\n".format(
        n_objs, cat_id, xp))

    with open(path, "wb") as f:
        for p in out:
            f.write(p.encode("cp1252", "replace"))
    return path


def write_breps_3dm(path, breps, layer_name="Front_original"):
    """Schreibt gueltige Breps in eine 3dm-Datei und gibt die Anzahl exportierter Objekte zurueck."""
    model = Rhino.FileIO.File3dm()

    layer_index = -1
    try:
        layer = Rhino.DocObjects.Layer()
        layer.Name = str(layer_name)
        layer_index = model.AllLayers.Add(layer)
    except Exception:
        layer_index = -1

    count = 0
    for b in breps:
        if b is None or (not isinstance(b, rg.Brep)) or (not b.IsValid):
            continue
        try:
            dup = b.DuplicateBrep()
            if dup is None or (not dup.IsValid):
                continue

            if layer_index >= 0:
                attrs = Rhino.DocObjects.ObjectAttributes()
                attrs.LayerIndex = layer_index
                model.Objects.AddBrep(dup, attrs)
            else:
                model.Objects.AddBrep(dup)
            count += 1
        except Exception:
            continue

    if count <= 0:
        raise Exception("Keine gueltigen Breps fuer 3dm-Export vorhanden.")

    ok = model.Write(path, 6)
    if not ok:
        raise Exception("3dm-Datei konnte nicht geschrieben werden.")
    return count


def write_report_pdf(path, title, lines):
    """Schreibt den kompletten Report als mehrseitiges PDF mit Footer."""
    page_width = 595
    page_height = 842
    left = 28
    top = 800
    line_height = 12
    max_lines = 58
    footer_text = company_stamp_text()

    chunks = []
    all_lines = [str(l) for l in lines]
    for i in range(0, len(all_lines), max_lines):
        chunks.append(all_lines[i:i + max_lines])
    if not chunks:
        chunks = [[title]]

    obj_data = {}
    next_id = 1
    catalog_id = next_id
    next_id += 1
    pages_id = next_id
    next_id += 1
    font_id = next_id
    next_id += 1

    page_ids = []
    content_ids = []
    for _ in chunks:
        page_ids.append(next_id)
        next_id += 1
        content_ids.append(next_id)
        next_id += 1

    obj_data[font_id] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"

    for idx, chunk in enumerate(chunks):
        stream_lines = []
        stream_lines.append("BT")
        stream_lines.append("/F1 10 Tf")
        stream_lines.append("1 0 0 1 {} {} Tm".format(left, top))
        if len(chunks) == 1:
            stream_lines.append("({}) Tj".format(_pdf_safe_line(title)))
        else:
            stream_lines.append("({}) Tj".format(_pdf_safe_line("{} (Seite {})".format(title, idx + 1))))

        for i, line in enumerate(chunk):
            y = top - (i + 2) * line_height
            stream_lines.append("1 0 0 1 {} {} Tm".format(left, y))
            stream_lines.append("({}) Tj".format(_pdf_safe_line(line)))

        stream_lines.append("/F1 8 Tf")
        stream_lines.append("1 0 0 1 {} {} Tm".format(left, 22))
        stream_lines.append("({}) Tj".format(_pdf_safe_line(footer_text)))
        stream_lines.append("ET")

        stream = "\n".join(stream_lines)
        content_obj = "<< /Length {} >>\nstream\n{}\nendstream".format(len(stream.encode("cp1252", "replace")), stream)
        obj_data[content_ids[idx]] = content_obj

        page_obj = (
            "<< /Type /Page /Parent {} 0 R /MediaBox [0 0 {} {}] "
            "/Resources << /Font << /F1 {} 0 R >> >> /Contents {} 0 R >>"
        ).format(pages_id, page_width, page_height, font_id, content_ids[idx])
        obj_data[page_ids[idx]] = page_obj

    kids = " ".join(["{} 0 R".format(pid) for pid in page_ids])
    obj_data[pages_id] = "<< /Type /Pages /Kids [ {} ] /Count {} >>".format(kids, len(page_ids))
    obj_data[catalog_id] = "<< /Type /Catalog /Pages {} 0 R >>".format(pages_id)

    out = []
    out.append("%PDF-1.4\n")
    offsets = [0]

    for oid in range(1, next_id):
        offsets.append(sum(len(part.encode("cp1252", "replace")) for part in out))
        out.append("{} 0 obj\n{}\nendobj\n".format(oid, obj_data.get(oid, "<< >>")))

    xref_pos = sum(len(part.encode("cp1252", "replace")) for part in out)
    out.append("xref\n")
    out.append("0 {}\n".format(next_id))
    out.append("0000000000 65535 f \n")
    for oid in range(1, next_id):
        out.append("{:010d} 00000 n \n".format(offsets[oid]))

    out.append("trailer\n")
    out.append("<< /Size {} /Root {} 0 R >>\n".format(next_id, catalog_id))
    out.append("startxref\n")
    out.append("{}\n".format(xref_pos))
    out.append("%%EOF\n")

    with open(path, "wb") as f:
        for part in out:
            f.write(part.encode("cp1252", "replace"))

    return path


try:
    do_csv = to_bool(write_csv, True)
    do_txt = to_bool(write_txt, True)
    do_pdf = to_bool(write_pdf, True)
    do_excel = to_bool(write_excel, True)
    do_view        = to_bool(write_view,  False)
    do_show_kanten = to_bool(show_kanten, False)
    run = to_bool(export_starten, False)
    do_save_preset = to_bool(save_preset, False)

    # ── Preset laden und fehlende GH-Inputs ergaenzen ──────────────────────
    _preset = {}
    _preset_path = str(param_preset).strip() if param_preset is not None else ""
    if _preset_path:
        _preset = load_params_csv(_preset_path)
        if "__error__" in _preset:
            protokoll.append("Preset-Fehler: " + _preset["__error__"])
            _preset = {}
        else:
            protokoll.append("Preset geladen: {} Eintraege aus {}".format(len(_preset), _preset_path))
            projektname      = _preset_str(_preset,  "projektname",      projektname)
            teilenummer      = _preset_str(_preset,  "teilenummer",      teilenummer)
            unterteilenummer = _preset_str(_preset,  "unterteilenummer", unterteilenummer)
            revision         = _preset_str(_preset,  "revision",         revision)
            exportordner     = _preset_str(_preset,  "exportordner",     exportordner)
            write_csv        = _preset_bool(_preset, "write_csv",        write_csv)
            write_txt        = _preset_bool(_preset, "write_txt",        write_txt)
            write_pdf        = _preset_bool(_preset, "write_pdf",        write_pdf)
            write_excel      = _preset_bool(_preset, "write_excel",      write_excel)
            write_view       = _preset_bool(_preset, "write_view",       write_view)
            show_kanten      = _preset_bool(_preset, "show_kanten",      show_kanten)
            parameter_namen  = _preset_list(_preset, "parameter_namen",  parameter_namen)
            parameter_werte  = _preset_list(_preset, "parameter_werte",  parameter_werte)
            # do_* Werte neu auswerten nach Preset-Anwendung
            do_csv        = to_bool(write_csv,   do_csv)
            do_txt        = to_bool(write_txt,   do_txt)
            do_pdf        = to_bool(write_pdf,   do_pdf)
            do_excel      = to_bool(write_excel, do_excel)
            do_view       = to_bool(write_view,  do_view)
            do_show_kanten = to_bool(show_kanten, do_show_kanten)

    basisname = build_base_name(projektname, teilenummer, unterteilenummer, revision, "Stueckliste")

    csv_header = "" if csv_kopf is None else str(csv_kopf).strip()
    if csv_header == "":
        csv_header = "Pos;Typ;Laenge;Tiefe;Hoehe;Anzahl;Bemerkung"
    else:
        # Falls alter Header inkl. Material kommt, Material-Spalte entfernen.
        header_parts = [p.strip() for p in csv_header.split(";")]
        if len(header_parts) == 8 and header_parts[6].lower() == "material":
            header_parts = header_parts[:6] + [header_parts[7]]
            csv_header = ";".join(header_parts)

    csv_lines = to_text_list(csv_zeilen)

    # Prioritaet: expliziter Input hat Vorrang, danach Aliasnamen aus bestehenden Skripten.
    if stueckliste_text is not None and len(to_text_list(stueckliste_text)) > 0:
        sl_lines = to_text_list(stueckliste_text)
    else:
        sl_lines = to_text_list(stueckliste)

    if zusatz_info is not None and len(to_text_list(zusatz_info)) > 0:
        extra_info = to_text_list(zusatz_info)
    else:
        extra_info = to_text_list(info_alias)

    # Python-first: Falls keine CSV-Zeilen vorhanden sind, aus stueckliste_text erzeugen.
    if len(csv_lines) == 0 and len(sl_lines) > 0:
        csv_lines = build_csv_from_stueckliste_lines(sl_lines, 1)
        if len(csv_lines) > 0:
            protokoll.append("Hinweis: CSV-Zeilen automatisch aus stueckliste_text erzeugt.")
        else:
            protokoll.append("Hinweis: Keine csv_zeilen und stueckliste_text konnte nicht in Tabellenzeilen geparst werden.")

    # Verlegeplan aus Untitled-2 optional in CSV uebernehmen.
    csv_plan = build_csv_from_verlegeplan_lines(sl_lines, detect_max_pos(csv_lines) + 1)
    if len(csv_plan) > 0:
        protokoll.append("Hinweis: Verlegeplan aus stueckliste_text in CSV aufgenommen ({} Zeilen).".format(len(csv_plan)))

    # Praezise Typzuordnung bevorzugen; Sammel-/Aliasinputs bleiben als Fallback nutzbar.
    br_all = merge_sources(bretter)
    rw_l = merge_sources(rueckwaende_links)
    rw_r = merge_sources(rueckwaende_rechts)
    af_l = merge_sources(ansaug_links)
    af_r = merge_sources(ansaug_rechts)

    # Falls nur Sammel-/Aliasdaten vorhanden sind, dort einlesen.
    rw_combined_fallback = merge_sources(rueckwaende, rueckplatte)
    af_combined_fallback = merge_sources(ansaugflaechen, ansaugplatte, ansaugbretter_out)

    use_rw_fallback = (len(rw_l) + len(rw_r) == 0 and len(rw_combined_fallback) > 0)
    use_af_fallback = (len(af_l) + len(af_r) == 0 and len(af_combined_fallback) > 0)
    p_names = to_text_list(parameter_namen)
    p_vals = to_text_list(parameter_werte)

    max_pos = detect_max_pos(csv_lines)

    include_bretter_summary = not has_brett_info_in_text_lines(sl_lines)
    if include_bretter_summary:
        csv_br, report_br, invalid_br, max_pos = summarize_bretter_ordered(br_all, max_pos)
    else:
        csv_br, report_br, invalid_br = [], [], 0
        protokoll.append("Hinweis: Brett-Zusammenfassung aus Breps uebersprungen (Stueckliste/Verlegeplan bereits vorhanden).")
    csv_rw_l, report_rw_l, invalid_rw_l, max_pos = summarize_brep_parts("Rueckwand links", rw_l, max_pos)
    csv_rw_r, report_rw_r, invalid_rw_r, max_pos = summarize_brep_parts("Rueckwand rechts", rw_r, max_pos)
    csv_af_l, report_af_l, invalid_af_l, max_pos = summarize_brep_parts("Ansaugflaeche links", af_l, max_pos)
    csv_af_r, report_af_r, invalid_af_r, max_pos = summarize_brep_parts("Ansaugflaeche rechts", af_r, max_pos)

    csv_rw_f, report_rw_f, invalid_rw_f, max_pos = summarize_brep_parts("Rueckwand", rw_combined_fallback if use_rw_fallback else [], max_pos)
    csv_af_f, report_af_f, invalid_af_f, max_pos = summarize_brep_parts("Ansaugflaeche", af_combined_fallback if use_af_fallback else [], max_pos)

    # Zusatzinfos aus anderen Komponenten auch in CSV transportieren.
    csv_info = build_csv_info_rows(extra_info, max_pos + 1)
    max_pos = max_pos + len(csv_info)

    csv_lines_export = list(csv_lines) + csv_plan + csv_br + csv_rw_l + csv_rw_r + csv_af_l + csv_af_r + csv_rw_f + csv_af_f + csv_info
    support_report_lines = report_br + report_rw_l + report_rw_r + report_af_l + report_af_r + report_rw_f + report_af_f

    if invalid_br > 0:
        protokoll.append("WARNUNG: {} ungueltige Brett-Breps uebersprungen.".format(invalid_br))
    invalid_rw = invalid_rw_l + invalid_rw_r + invalid_rw_f
    invalid_af = invalid_af_l + invalid_af_r + invalid_af_f

    if invalid_rw > 0:
        protokoll.append("WARNUNG: {} ungueltige Rueckwand-Breps uebersprungen.".format(invalid_rw))
    if invalid_af > 0:
        protokoll.append("WARNUNG: {} ungueltige Ansaugflaechen-Breps uebersprungen.".format(invalid_af))

    if use_rw_fallback:
        protokoll.append("Hinweis: Rueckwaende aus Sammel-/Aliasinput verwendet (kein links/rechts Input verbunden).")
    if use_af_fallback:
        protokoll.append("Hinweis: Ansaugflaechen aus Sammel-/Aliasinput verwendet (kein links/rechts Input verbunden).")
    if len(br_all) > 0:
        protokoll.append("Hinweis: Bretter wurden als Zusatzteile in Report/CSV aufgenommen.")
    if len(extra_info) > 0:
        protokoll.append("Hinweis: Zusatzinformationen wurden in TXT/PDF und CSV aufgenommen.")

    report_preview = build_report_lines(basisname, sl_lines, support_report_lines, p_names, p_vals, extra_info)

    # Finale CSV-Zeilen auf neues Schema normalisieren (ohne Material).
    csv_lines_export = [normalize_csv_row(r) for r in csv_lines_export]

    # Einheitliches Report-Format fuer alle Ausgabedateien.
    report_export_lines = list(report_preview)

    if not run:
        protokoll.append("Export ist bereit. Setze export_starten = True.")
        protokoll.append("Dateimodus: CSV={} TXT={} PDF={} Excel={} View={}".format(do_csv, do_txt, do_pdf, do_excel, do_view))
    elif not (do_csv or do_txt or do_pdf or do_excel or do_view):
        protokoll.append("Abbruch: Kein Ausgabeformat aktiviert (write_csv/write_txt/write_pdf/write_excel).")
    else:
        base_folder, used_fallback = resolve_export_folder(exportordner)
        if not os.path.isdir(base_folder):
            os.makedirs(base_folder)

        # Pro Export immer eigener Unterordner nach Namensmuster.
        folder = create_export_run_folder(base_folder, basisname)

        if used_fallback:
            protokoll.append("Exportordner nicht gesetzt: Fallback auf Dokumentordner/Desktop.")

        protokoll.append("Export-Basisordner: " + base_folder)
        protokoll.append("Exportordner (Run): " + folder)
        protokoll.append("Basisname: " + basisname)


        if do_csv:
            csv_path = os.path.join(folder, basisname + ".csv")
            try:
                write_report_csv(csv_path, report_export_lines)
                dateien.append(csv_path)
                protokoll.append("CSV-Datei geschrieben: {} Zeilen".format(len(report_export_lines) + 1))
            except Exception as ex:
                protokoll.append("Fehler CSV: " + str(ex))

        if do_txt:
            txt_path = os.path.join(folder, basisname + ".txt")
            try:
                write_report_txt(txt_path, report_export_lines)
                dateien.append(txt_path)
                protokoll.append("TXT-Report geschrieben: {} Zeilen".format(len(report_export_lines) + 1))
            except Exception as ex:
                protokoll.append("Fehler TXT: " + str(ex))

        if do_pdf:
            pdf_path = os.path.join(folder, basisname + ".pdf")
            try:
                write_table_pdf(
                    pdf_path,
                    basisname,
                    report_export_lines
                )
                dateien.append(pdf_path)
                protokoll.append("PDF-Report geschrieben: {}".format(pdf_path))
            except Exception as ex:
                protokoll.append("Fehler PDF: " + str(ex))

        if do_excel:
            short_xml_name = build_short_xml_name(basisname)
            excel_path = os.path.join(folder, short_xml_name + ".xlsx")
            try:
                write_excel_xlsx_report(excel_path, report_export_lines)
                dateien.append(excel_path)
                protokoll.append("Excel-Datei geschrieben: {}".format(excel_path))
                if short_xml_name != basisname:
                    protokoll.append("Hinweis: Excel-Dateiname gekuerzt auf '{}' (inkl. Umlaut-Umsetzung).".format(short_xml_name))
            except Exception as ex:
                protokoll.append("Fehler Excel: " + str(ex))

        # Zwei Front-Geometriepfade:
        # - front_geo_view: gedreht fuer Planansicht-Overlay
        # - front_geo_original: unge dreht fuer Archiv/Versionierung als .3dm
        front_geo_view = merge_sources(frontgeometrie)
        front_geo_original = merge_sources(frontgeometrie_original)

        # Urspruengliche Frontgeometrie (vor Rotation) als .3dm sichern.
        if len(front_geo_original) > 0:
            front_3dm_path = os.path.join(folder, basisname + "_Front_original.3dm")
            try:
                n_front = write_breps_3dm(front_3dm_path, front_geo_original, "Front_original")
                dateien.append(front_3dm_path)
                protokoll.append("Front-3DM geschrieben: {} ({} Breps)".format(front_3dm_path, n_front))
            except Exception as ex:
                protokoll.append("Fehler Front-3DM: " + str(ex))
        else:
            protokoll.append("Hinweis: Keine frontgeometrie_original fuer Front-3DM verbunden.")

        if do_view:
            view_pdf_path = os.path.join(folder, basisname + "_Ansichten.pdf")
            try:
                all_view_breps = (br_all + rw_l + rw_r + af_l + af_r +
                                  (rw_combined_fallback if use_rw_fallback else []) +
                                  (af_combined_fallback if use_af_fallback else []))
                write_views_pdf(view_pdf_path, basisname, all_view_breps,
                                show_edges=do_show_kanten, front_breps=front_geo_view)
                dateien.append(view_pdf_path)
                protokoll.append("Ansichten-PDF geschrieben: {}".format(view_pdf_path))
            except Exception as ex:
                protokoll.append("Fehler Ansichten-PDF: " + str(ex))

        if do_save_preset:
            preset_save_path = os.path.join(folder, basisname + "_Preset.csv")
            try:
                write_params_csv(preset_save_path, {
                    "projektname":      projektname,
                    "teilenummer":      teilenummer,
                    "unterteilenummer": unterteilenummer,
                    "revision":         revision,
                    "exportordner":     exportordner,
                    "write_csv":        do_csv,
                    "write_txt":        do_txt,
                    "write_pdf":        do_pdf,
                    "write_excel":      do_excel,
                    "write_view":       do_view,
                    "show_kanten":      do_show_kanten,
                    "parameter_namen":  p_names,
                    "parameter_werte":  p_vals,
                })
                dateien.append(preset_save_path)
                protokoll.append("Preset gespeichert: {}".format(preset_save_path))
            except Exception as ex:
                protokoll.append("Fehler Preset-Speichern: " + str(ex))

    # Optional English aliases for downstream scripts.
    files = dateien
    base_name = basisname
    report_preview_en = report_preview
    log = protokoll

except Exception as e:
    import traceback
    protokoll.append("FEHLER: " + str(e))
    protokoll.append(traceback.format_exc())
