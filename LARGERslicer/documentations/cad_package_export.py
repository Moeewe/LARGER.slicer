"""
GHPython: CAD package export (3DM, STEP, IGES) without union.

INPUTS:
    geometrien        : Brep [List Access]
    geo_out           : Brep [List Access] (optional, aus Geometrie-Komponente)
    geo_bretter_out   : Brep [List Access] (optional)
    original_out      : Brep [List Access] (optional)
    rand_out          : Brep [List Access] (optional)
    ansaugbretter_out : Brep [List Access] (optional)
    frontgeometrie_original : Brep [List Access] (optional)
    parameter_namen   : Text [List] (optional)
    parameter_werte   : Generic [List] (optional)
    write_params      : bool  (default: True)
    exportordner      : str   (optional, fallback: Rhino doc folder / Desktop)
    projektname       : str   (default: Projekt)
    teilenummer       : str   (default: Teil1)
    unterteilenummer  : str   (default: allgemein)
    revision          : str   (optional)
    write_3dm         : bool  (default: True)
    write_stp         : bool  (default: True)
    write_iges        : bool  (default: True)
    export_starten    : bool  (default: False)

OUTPUTS:
    dateien           : Text [List]
    basisname         : Text
    protokoll         : Text [List]
"""

import os
import datetime
import System
import Rhino
import Rhino.FileIO as rfi
import Rhino.Geometry as rg
import scriptcontext as sc

ghenv = globals().get("ghenv", None)


def _pick_input(*keys):
    for key in keys:
        val = globals().get(key, None)
        if val is not None:
            return val
    return None


# GH input fallbacks for editor/static analysis.
geometrien = _pick_input("geometrien", "geometries")
geo_out = _pick_input("geo_out", "geometry_out")
geo_bretter_out = _pick_input("geo_bretter_out", "board_geometry_out")
original_out = _pick_input("original_out", "original_geometry_out")
rand_out = _pick_input("rand_out", "edge_out")
ansaugbretter_out = _pick_input("ansaugbretter_out", "suction_boards_out")
frontgeometrie_original = _pick_input("frontgeometrie_original", "front_geometry_original")
parameter_namen = _pick_input("parameter_namen", "parameter_names")
parameter_werte = _pick_input("parameter_werte", "parameter_values")
write_params = _pick_input("write_params", "export_params")
exportordner = _pick_input("exportordner", "export_folder")
projektname = _pick_input("projektname", "project_name")
teilenummer = _pick_input("teilenummer", "part_number")
unterteilenummer = _pick_input("unterteilenummer", "subpart_number")
revision = _pick_input("revision", "rev")
write_3dm = _pick_input("write_3dm", "export_3dm")
write_stp = _pick_input("write_stp", "export_stp", "export_step")
write_iges = _pick_input("write_iges", "export_iges")
export_starten = _pick_input("export_starten", "export_start", "run_export")


dateien = []
basisname = ""
protokoll = []


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


def build_base_name(project, part, subpart, rev, document_type="Geometrie"):
    # Zeit bis Sekunden fuer eindeutige Versionierung je Exportlauf.
    prefix = "PS" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    p = sanitize_token(project, "Projekt")
    pn = sanitize_token(part, "Teil")
    sp = sanitize_token(subpart, "Allgemein")
    dt = sanitize_token(document_type, "Geometrie")
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


def rising_edge_trigger(current_value):
    """True nur beim Wechsel False->True, um Doppel-Exporte je Klick zu verhindern."""
    try:
        comp_id = str(ghenv.Component.InstanceGuid)
    except Exception:
        comp_id = "global"

    key = "cad_export_trigger_prev_{}".format(comp_id)
    prev = bool(sc.sticky.get(key, False))
    curr = bool(current_value)
    sc.sticky[key] = curr
    return curr and (not prev)


def to_brep_list(value):
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        return [g for g in value if isinstance(g, rg.Brep)]
    return [value] if isinstance(value, rg.Brep) else []


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


def merge_sources(*sources):
    out = []
    for s in sources:
        out.extend(to_brep_list(s))
    return out


def _round_sig(v, digits=3):
    try:
        return round(float(v), digits)
    except Exception:
        return 0.0


def brep_signature(brep):
    """Erzeugt eine robuste Signatur fuer Duplikatfilterung von Breps."""
    try:
        bb = brep.GetBoundingBox(True)
        bb_vals = (
            _round_sig(bb.Min.X), _round_sig(bb.Min.Y), _round_sig(bb.Min.Z),
            _round_sig(bb.Max.X), _round_sig(bb.Max.Y), _round_sig(bb.Max.Z),
        )
    except Exception:
        bb_vals = (0.0, 0.0, 0.0, 0.0, 0.0, 0.0)

    try:
        vm = rg.VolumeMassProperties.Compute(brep)
        vol = _round_sig(vm.Volume) if vm else 0.0
    except Exception:
        vol = 0.0

    try:
        am = rg.AreaMassProperties.Compute(brep)
        area = _round_sig(am.Area) if am else 0.0
    except Exception:
        area = 0.0

    try:
        faces = int(brep.Faces.Count)
        edges = int(brep.Edges.Count)
    except Exception:
        faces = 0
        edges = 0

    return (bb_vals, vol, area, faces, edges)


def deduplicate_breps(breps):
    """Entfernt identische/mehrfach verdrahtete Geometrie (z. B. dreifach gemergte Inputs)."""
    unique = []
    seen_refs = set()
    seen_sigs = set()

    for b in breps:
        if b is None:
            continue

        ref = id(b)
        if ref in seen_refs:
            continue
        seen_refs.add(ref)

        sig = brep_signature(b)
        if sig in seen_sigs:
            continue
        seen_sigs.add(sig)
        unique.append(b)

    return unique


def resolve_export_folder(user_folder):
    if user_folder is not None and str(user_folder).strip() != "":
        return os.path.abspath(str(user_folder)), False

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


def write_params_csv(path, names, values):
    """Speichert uebergebene Parameter als CSV (Name;Wert)."""
    lines = ["parameter;wert"]
    n = max(len(names), len(values))
    for i in range(n):
        name = names[i] if i < len(names) else "Parameter_{}".format(i + 1)
        val = values[i] if i < len(values) else ""
        lines.append("{};{}".format(str(name), str(val)))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return path


def write_3dm_file(path, breps):
    try:
        f3 = rfi.File3dm()
        for b in breps:
            f3.Objects.AddBrep(b.DuplicateBrep())
        ok = f3.Write(path, 7)
        if not ok:
            return False, "File3dm.Write returned False"
        return True, "3DM-Datei geschrieben ({} Breps).".format(len(breps))
    except Exception as ex:
        return False, str(ex)


def write_via_export_command(path, breps):
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None:
        return False, "Kein aktives Rhino-Dokument vorhanden."

    added_ids = []
    previous_ids = []

    try:
        selected = doc.Objects.GetSelectedObjects(False, False)
        if selected:
            for obj in selected:
                previous_ids.append(obj.Id)

        doc.Objects.UnselectAll()

        for b in breps:
            gid = doc.Objects.AddBrep(b.DuplicateBrep())
            if gid != System.Guid.Empty:
                added_ids.append(gid)
                doc.Objects.Select(gid)

        if len(added_ids) == 0:
            return False, "Keine Geometrie konnte fuer den Export ins Dokument geschrieben werden."

        escaped = path.replace("\\", "\\\\").replace('"', '\\"')
        # "!" beendet ggf. haengende Vorbefehle; extra Enter fängt format-spezifische Optionen ab.
        cmd = '! _-Export "{}" _Enter _Enter _Enter'.format(escaped)
        ok = Rhino.RhinoApp.RunScript(cmd, False)
        if not ok:
            return False, "Rhino Exportkommando fehlgeschlagen."

        return True, "Datei geschrieben ({} Breps, 1:1 ohne Union).".format(len(added_ids))
    except Exception as ex:
        return False, str(ex)
    finally:
        for gid in added_ids:
            try:
                doc.Objects.Delete(gid, True)
            except Exception:
                pass

        try:
            doc.Objects.UnselectAll()
            for pid in previous_ids:
                doc.Objects.Select(pid)
            doc.Views.Redraw()
        except Exception:
            pass


try:
    write3dm = to_bool(write_3dm, True)
    writestp = to_bool(write_stp, True)
    writeiges = to_bool(write_iges, True)
    do_write_params = to_bool(write_params, True)
    run_raw = to_bool(export_starten, False)
    run = rising_edge_trigger(run_raw)

    basisname = build_base_name(projektname, teilenummer, unterteilenummer, revision, "Geometrie")

    if not run_raw:
        protokoll.append("Export ist bereit. Setze export_starten = True.")
        protokoll.append("Regel: kein Union, Export 1:1 wie Eingabe.")
    elif not run:
        protokoll.append("Export-Trigger bereits verarbeitet (warte auf False->True Flanke).")
    elif not (write3dm or writestp or writeiges):
        protokoll.append("Abbruch: Kein Ausgabeformat aktiviert.")
    else:
        p_names = to_text_list(parameter_namen)
        p_vals = to_text_list(parameter_werte)

        # Geometrien aus Geometrie-Komponenten automatisch mit einsammeln.
        all_breps = merge_sources(
            geometrien,
            geo_out,
            geo_bretter_out,
            original_out,
            rand_out,
            ansaugbretter_out,
            frontgeometrie_original,
        )
        valid_breps = []
        for idx, b in enumerate(all_breps):
            if b is None:
                protokoll.append("WARNUNG: Geometrie #{} ist null und wird uebersprungen.".format(idx + 1))
                continue
            if not b.IsValid:
                protokoll.append("WARNUNG: Geometrie #{} ist ungueltig und wird uebersprungen.".format(idx + 1))
                continue
            valid_breps.append(b)

        unique_breps = deduplicate_breps(valid_breps)

        if len(unique_breps) == 0:
            protokoll.append("Abbruch: Keine gueltigen Breps fuer den Export.")
        else:
            base_folder, used_fallback = resolve_export_folder(exportordner)
            if not os.path.isdir(base_folder):
                os.makedirs(base_folder)

            # Pro Export immer eigener Unterordner nach Namensmuster.
            folder = create_export_run_folder(base_folder, basisname)

            if used_fallback:
                protokoll.append("Exportordner nicht gesetzt: Fallback auf Dokumentordner/Desktop.")

            protokoll.append("Eingabe-Breps gesamt: {}".format(len(all_breps)))
            protokoll.append("Gueltige Breps: {}".format(len(valid_breps)))
            protokoll.append("Duplikate entfernt: {}".format(max(0, len(valid_breps) - len(unique_breps))))
            protokoll.append("Exportierte Breps: {}".format(len(unique_breps)))
            protokoll.append("Export-Basisordner: " + base_folder)
            protokoll.append("Exportordner (Run): " + folder)
            protokoll.append("Union: deaktiviert (1:1 Export).")

            if do_write_params and (len(p_names) > 0 or len(p_vals) > 0):
                p_csv = os.path.join(folder, basisname + "_Parameter.csv")
                try:
                    write_params_csv(p_csv, p_names, p_vals)
                    dateien.append(p_csv)
                    protokoll.append("Parameter-CSV geschrieben: {}".format(p_csv))
                except Exception as ex:
                    protokoll.append("Fehler Parameter-CSV: " + str(ex))

            if write3dm:
                p3dm = os.path.join(folder, basisname + ".3dm")
                ok, msg = write_3dm_file(p3dm, unique_breps)
                if ok:
                    dateien.append(p3dm)
                    protokoll.append(msg)
                else:
                    protokoll.append("Fehler 3DM: " + msg)

            if writestp:
                pstp = os.path.join(folder, basisname + ".stp")
                ok, msg = write_via_export_command(pstp, unique_breps)
                if ok:
                    dateien.append(pstp)
                    protokoll.append("STEP: " + msg)
                else:
                    protokoll.append("Fehler STEP: " + msg)

            if writeiges:
                piges = os.path.join(folder, basisname + ".iges")
                ok, msg = write_via_export_command(piges, unique_breps)
                if ok:
                    dateien.append(piges)
                    protokoll.append("IGES: " + msg)
                else:
                    protokoll.append("Fehler IGES: " + msg)

    # Optional English aliases for downstream scripts.
    files = dateien
    base_name = basisname
    log = protokoll

except Exception as e:
    import traceback
    protokoll.append("FEHLER: " + str(e))
    protokoll.append(traceback.format_exc())
