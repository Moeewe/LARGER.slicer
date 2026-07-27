import Rhino
import Rhino.Geometry as rg
import Rhino.Geometry.Intersect as ri
import Rhino.Display as rd
import System.Drawing as sd
import System
import System.Threading.Tasks as tasks
import System.Diagnostics as diag
import scriptcontext as sc
import sys
import math
import traceback

# ── Defaults ────────────────────────────────────────────────
def _d(v, fb): return fb if (v is None or (isinstance(v, (int, float)) and v <= 0)) else v

MeshResol    = _d(MeshResol,    1.0)
LayerHeight  = _d(LayerHeight,  0.2)
LayerWidth   = _d(LayerWidth,   0.4)
Threshold    = _d(Threshold,    0.5)

try:
    ShowContours = bool(ShowContours) if ShowContours is not None else False
except Exception:
    ShowContours = False

HeatMesh  = None
Contours  = []
DangerLevels = []
MaxShiftXY = 0.0

# ── Custom Display Conduit (zeichnet Mesh OHNE Drahtgitter) ─
class HeatmapConduit(rd.DisplayConduit):
    def __init__(self):
        super(HeatmapConduit, self).__init__()
        self.mesh = None
    def CalculateBoundingBox(self, e):
        if self.mesh is not None:
            e.IncludeBoundingBox(self.mesh.GetBoundingBox(False))
    def PostDrawObjects(self, e):
        if self.mesh is not None:
            # DrawMeshFalseColors zeigt die VertexColors OHNE Edges/Wireframe
            e.Display.DrawMeshFalseColors(self.mesh)

# Alten Conduit aus Sticky aufräumen, damit nichts doppelt gezeichnet wird
_CONDUIT_KEY = "_overhang_heatmap_conduit"
_HANDLER_KEY = "_overhang_heatmap_handler"

if _CONDUIT_KEY in sc.sticky:
    try:
        sc.sticky[_CONDUIT_KEY].Enabled = False
    except Exception:
        pass

# GH-Default-Preview AUS, damit weder Heatmap-Wireframe noch Kontur-Kurven
# über unseren Conduit gemalt werden. Output-Daten bleiben aber verfügbar!
# WICHTIG: Hidden muss NACH dem Solve gesetzt werden, sonst überschreibt GH es wieder.
def _hide_after_solve(sender, e):
    try:
        Rhino.RhinoApp.Idle -= _hide_after_solve  # nur 1x ausführen
    except Exception:
        pass
    try:
        ghenv.Component.Hidden = True
        ghenv.Component.ExpirePreview(True)
        ghenv.Component.OnDisplayExpired(True)
        Rhino.RhinoDoc.ActiveDoc.Views.Redraw()
    except Exception:
        pass

try:
    if not ghenv.Component.Hidden:
        Rhino.RhinoApp.Idle += _hide_after_solve
except Exception:
    pass

# ── Auto-Disable: Conduit ausschalten wenn Komponente disabled/gelöscht wird ──
def _sync_conduit_state(*args):
    """Wird vom Component-Event aufgerufen, wenn sich Locked ändert."""
    try:
        comp = ghenv.Component
        cond = sc.sticky.get(_CONDUIT_KEY)
        if cond is None:
            return
        # NUR Locked beachten - Hidden ignorieren, da wir es selbst auf True setzen.
        should_show = not comp.Locked
        if cond.Enabled != should_show:
            cond.Enabled = should_show
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw()
    except Exception:
        pass

def _kill_conduit(*args):
    """Wird aufgerufen wenn die Komponente gelöscht wird oder das Doc schließt."""
    try:
        cond = sc.sticky.get(_CONDUIT_KEY)
        if cond is not None:
            cond.Enabled = False
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw()
    except Exception:
        pass

# Event-Handler nur EINMAL registrieren (Sticky merkt sich das)
if _HANDLER_KEY not in sc.sticky:
    try:
        ghenv.Component.ObjectChanged += _sync_conduit_state
        # Wenn die Komponente aus dem Canvas entfernt wird:
        if ghenv.Component.OnPingDocument() is not None:
            ghenv.Component.OnPingDocument().ObjectsDeleted += _kill_conduit
        sc.sticky[_HANDLER_KEY] = True
    except Exception:
        pass

print("v11 (Normalen-Shift) | LayerHeight:{} | LayerWidth:{} | MaxOverlap:{}%".format(
    LayerHeight, LayerWidth, Threshold*100))

# ── Debug-Profiler ──────────────────────────────────────────
_profile = []
_sw_total = diag.Stopwatch.StartNew()
def _tick(label, sw):
    sw.Stop()
    ms = sw.Elapsed.TotalMilliseconds
    _profile.append((label, ms))
    return diag.Stopwatch.StartNew()

_sw = diag.Stopwatch.StartNew()

if getattr(sys.modules[__name__], 'Geometry', None) is None and 'Geometry' not in globals():
    pass # Catch for IDE
elif Geometry is None:
    print("Kein Geometry-Input.")
else:
    try:

        # ── 1. Geometrie → Mesh ──────────────────────────────
        work_mesh = None
        if isinstance(Geometry, rg.Mesh):
            work_mesh = Geometry.DuplicateMesh()
            _sw = _tick("1a. Input ist Mesh -> Duplicate", _sw)
        else:
            # Falls es eine Surface / Brep ist (was bei dir meistens der Fall ist):
            brep = rg.Brep.TryConvertBrep(Geometry)
            if brep is None and hasattr(Geometry, 'ToBrep'):
                brep = Geometry.ToBrep()
            _sw = _tick("1a. Geometry -> Brep", _sw)
            if brep is not None:
                # Mesh-Parameter mit verschiedenen Geschwindigkeiten:
                if MeshResol <= 0:
                    # TURBO-MODUS: Nutze die existierende Render-Mesh, falls vorhanden
                    # (Rhino baut die schon im Hintergrund auf, wir klauen sie einfach!)
                    existing = []
                    for face in brep.Faces:
                        rm = face.GetMesh(rg.MeshType.Render) or face.GetMesh(rg.MeshType.Preview)
                        if rm is not None:
                            existing.append(rm)
                    if existing and len(existing) == brep.Faces.Count:
                        arr = existing
                        _sw = _tick("1b. Turbo-Mode (Render-Mesh geklaut)", _sw)
                    else:
                        # Fallback: Minimale Mesh-Parameter
                        mp = rg.MeshingParameters.Minimal
                        arr = rg.Mesh.CreateFromBrep(brep, mp)
                        _sw = _tick("1b. Fallback Minimal Mesh", _sw)
                else:
                    mp = rg.MeshingParameters.Default
                    mp.MaximumEdgeLength = MeshResol
                    mp.MinimumEdgeLength = MeshResol * 0.1
                    # Limit Vertex-Anzahl auf vernünftiges Maß:
                    # Mit ~100k-300k Vertices sieht die Heatmap genauso gut aus wie mit 2 Mio,
                    # ist aber 10-20x schneller.
                    mp.GridMinCount = 16
                    mp.GridMaxCount = 0  # Auto / Unbegrenzt nur wenn nötig
                    mp.RefineGrid = True
                    mp.JaggedSeams = False
                    arr = rg.Mesh.CreateFromBrep(brep, mp)
                    _sw = _tick("1b. CreateFromBrep (Default Quality)", _sw)
                if arr:
                    work_mesh = rg.Mesh()
                    for mm in arr: work_mesh.Append(mm)
                    _sw = _tick("1c. Mesh.Append (Submeshes zusammenfügen)", _sw)
                    # Vertices verschmelzen und Normalen glätten
                    work_mesh.Vertices.CombineIdentical(True, True)
                    _sw = _tick("1d. Vertices.CombineIdentical", _sw)
                    work_mesh.Normals.ComputeNormals()
                    _sw = _tick("1e. Normals.ComputeNormals", _sw)

        if work_mesh is None or work_mesh.Faces.Count == 0:
            print("FEHLER: Mesh-Erstellung fehlgeschlagen.")
        else:
            V = work_mesh.Vertices.Count
            print("Mesh: {} Vertices".format(V))

            # Druckrichtung
            n_vec = rg.Vector3d(SlicePlane.Normal)
            n_vec.Unitize()
            nX, nY, nZ = n_vec.X, n_vec.Y, n_vec.Z

            print("Berechne echten Kontur-Versatz aus Normalen (Instant!)...")

            if not work_mesh.Normals.Count or work_mesh.Normals.Count != V:
                work_mesh.Normals.ComputeNormals()
            _sw = _tick("2a. Normals sicherstellen", _sw)

            # KEY OPTIMIERUNG: Normalen in einem Rutsch in ein Float-Array ziehen.
            nrm_floats = work_mesh.Normals.ToFloatArray()  # [x0,y0,z0,x1,y1,z1,...]
            _sw = _tick("2b. Normals.ToFloatArray (Bulk-Read)", _sw)
            
            # Formel für erlaubten Versatz: 
            max_allowed_shift = LayerWidth * Threshold

            # Funktionsaufrufe cachen
            sqrt = math.sqrt
            Color_FromArgb = sd.Color.FromArgb
            
            # .NET-Arrays mit fester Länge (thread-safe für Parallel.For)
            colors_arr = System.Array.CreateInstance(sd.Color, V)
            d_levels_arr = System.Array.CreateInstance(System.Double, V)
            
            inv_max = 1.0 / max_allowed_shift if max_allowed_shift > 0.0 else 0.0
            LH = LayerHeight
            MAS10 = max_allowed_shift * 10.0
            
            # === HYBRID: Serial für kleine, Parallel nur für sehr große Meshes ===
            # Parallel.For hat in IronPython hohen Overhead. Erst ab ~500k Vertices
            # macht es Sinn. Darunter ist die simple serielle Schleife schneller.
            USE_PARALLEL = V > 500000
            
            if USE_PARALLEL:
                colors_arr = System.Array.CreateInstance(sd.Color, V)
                d_levels_arr = System.Array.CreateInstance(System.Double, V)
                CHUNK = 16384
                n_chunks = (V + CHUNK - 1) // CHUNK
                
                def process_chunk(ci):
                    start = ci * CHUNK
                    end = start + CHUNK
                    if end > V: end = V
                    for vi in range(start, end):
                        i3 = vi * 3
                        Nz = nrm_floats[i3]*nX + nrm_floats[i3+1]*nY + nrm_floats[i3+2]*nZ
                        shift_xy = 0.0
                        if Nz < -1e-6:
                            Nxy_sq = 1.0 - Nz*Nz
                            shift_xy = MAS10 if Nxy_sq < 1e-12 else LH * (-Nz / sqrt(Nxy_sq))
                        t = shift_xy * inv_max
                        t_clip = t if t < 1.0 else 1.0
                        if t_clip < 0.0: t_clip = 0.0
                        d_levels_arr[vi] = t_clip
                        if   t_clip < 0.25: s=t_clip*4.0;        cr,cg,cb=0,         int(255*s),    255
                        elif t_clip < 0.50: s=(t_clip-0.25)*4.0; cr,cg,cb=0,        255,           int(255*(1-s))
                        elif t_clip < 0.75: s=(t_clip-0.50)*4.0; cr,cg,cb=int(255*s),255,           0
                        else:               s=(t_clip-0.75)*4.0; cr,cg,cb=255,       int(255*(1-s)),0
                        colors_arr[vi] = Color_FromArgb(255, cr, cg, cb)
                
                tasks.Parallel.For(0, n_chunks, System.Action[int](process_chunk))
                _sw = _tick("2c. Vertex-Loop PARALLEL ({}t, V>500k)".format(System.Environment.ProcessorCount), _sw)
                DangerLevels = list(d_levels_arr)
                colors_out = colors_arr
            else:
                # Schneller serieller Loop (Standard für die meisten Fälle)
                colors_list = [None] * V
                d_levels = [0.0] * V
                for vi in range(V):
                    i3 = vi * 3
                    Nz = nrm_floats[i3]*nX + nrm_floats[i3+1]*nY + nrm_floats[i3+2]*nZ
                    shift_xy = 0.0
                    if Nz < -1e-6:
                        Nxy_sq = 1.0 - Nz*Nz
                        shift_xy = MAS10 if Nxy_sq < 1e-12 else LH * (-Nz / sqrt(Nxy_sq))
                    t = shift_xy * inv_max
                    t_clip = t if t < 1.0 else 1.0
                    if t_clip < 0.0: t_clip = 0.0
                    d_levels[vi] = t_clip
                    if   t_clip < 0.25: s=t_clip*4.0;        cr,cg,cb=0,         int(255*s),    255
                    elif t_clip < 0.50: s=(t_clip-0.25)*4.0; cr,cg,cb=0,        255,           int(255*(1-s))
                    elif t_clip < 0.75: s=(t_clip-0.50)*4.0; cr,cg,cb=int(255*s),255,           0
                    else:               s=(t_clip-0.75)*4.0; cr,cg,cb=255,       int(255*(1-s)),0
                    colors_list[vi] = Color_FromArgb(255, cr, cg, cb)
                _sw = _tick("2c. Vertex-Loop SERIAL", _sw)
                DangerLevels = d_levels
                colors_out = System.Array[sd.Color](colors_list)

            MaxShiftXY = max(DangerLevels) * max_allowed_shift if DangerLevels and max_allowed_shift > 0 else 0.0
            _sw = _tick("2d. DangerLevels + MaxShift", _sw)

            work_mesh.VertexColors.SetColors(colors_out)
            _sw = _tick("2e. VertexColors.SetColors", _sw)

            # === Custom Conduit aktivieren: Mesh ohne Drahtgitter zeichnen ===
            conduit = HeatmapConduit()
            conduit.mesh = work_mesh
            conduit.Enabled = True
            sc.sticky[_CONDUIT_KEY] = conduit
            _sw = _tick("2f. Display Conduit aktiviert", _sw)

            # HeatMesh als Output bereitstellen (downstream nutzbar).
            # Die GH-Default-Vorschau ist via Hidden=True abgeschaltet,
            # gezeichnet wird das Mesh ausschließlich durch unseren Conduit.
            HeatMesh = work_mesh
            
            print("Fertig! Vertices: {} | Max Shift: {:.2f}mm".format(V, MaxShiftXY))

            # ── 3. KONTUREN (optional) ────────────────────────
            if ShowContours:
                print("Konturen (AnalysisStep={})...".format(AnalysisStep))
                bbox   = work_mesh.GetBoundingBox(SlicePlane)
                h_min  = bbox.Min.Z
                h_max  = bbox.Max.Z
                step_h = LayerHeight * AnalysisStep

                h = h_min
                while h <= h_max + step_h * 0.01:
                    cut_org = SlicePlane.Origin + n_vec * h
                    cut_pln = rg.Plane(cut_org, SlicePlane.XAxis, SlicePlane.YAxis)
                    polys   = ri.Intersection.MeshPlane(work_mesh, cut_pln)
                    if polys:
                        for p in polys:
                            if p.Count >= 2:
                                nc = p.ToNurbsCurve()
                                if nc: Contours.append(nc)
                    h += step_h
                print("{} Kontur-Kurven fertig.".format(len(Contours)))
                _sw = _tick("3. Konturen (MeshPlane Intersection)", _sw)

    except Exception as ex:
        print("=== FEHLER ===")
        print(str(ex))
        print(traceback.format_exc())

# ── Debug Profiler-Report ───────────────────────────────────
_sw_total.Stop()
total_ms = _sw_total.Elapsed.TotalMilliseconds
print("")
print("================ PROFILER-REPORT ================")
print("{:<48} {:>10} {:>8}".format("Schritt", "Zeit (ms)", "Anteil"))
print("-" * 68)
for label, ms in _profile:
    pct = (ms / total_ms * 100.0) if total_ms > 0 else 0.0
    bar = "#" * int(pct / 2.0)  # max ~50 Zeichen
    print("{:<48} {:>10.2f} {:>6.1f}% {}".format(label, ms, pct, bar))
print("-" * 68)
print("{:<48} {:>10.2f} {:>7}".format("GESAMT", total_ms, "100%"))
print("=================================================")