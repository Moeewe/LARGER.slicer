# DXR Postprocessor Improvements - Implementation Summary

## Completed Improvements

### 1. ✅ F-Position korrigiert
**Problem:** F (Feedrate) stand am Ende der Bewegung, sollte aber direkt nach G1 stehen.

**Lösung:**
- Format geändert von: `N{line_num} G1 X... G91 XE=[...] G90 F{F1:F3}`
- Format geändert zu: `N{line_num} G1 F{F1:F3} X... G91 XE=[...] G90`

**Datei:** `Utils/DXRHelper.cs` (Zeile ~282)

### 2. ✅ Euler-Winkel immer eingefügt
**Problem:** A, B, C Winkel wurden nur eingefügt, wenn sie im Input vorhanden waren.

**Lösung:**
- A, B, C werden jetzt immer eingefügt (Standard: 0.0)
- Format: `A0.0 B0.0 C0.0` wenn nicht vorhanden
- Ermöglicht zukünftige non-planar Drucke

**Datei:** `Utils/DXRHelper.cs` (Zeile ~277-279)

### 3. ✅ Eges-Feld im Header hinzugefügt
**Problem:** Eges-Feld (Materialvolumen) fehlte im Header.

**Lösung:**
- `;Eges = IC[{totalExtrusion:F3}]` wird jetzt im Header eingefügt
- Berechnet aus Summe aller P1-Werte (Extrusion)

**Datei:** `Utils/DXRHelper.cs` (Zeile ~189)

### 4. ✅ Layer-Typ-Subroutinen unterstützt
**Problem:** Nur `layer_sub.nc` wurde verwendet, keine Unterscheidung nach Layer-Typ.

**Lösung:**
- Parser erkennt Layer-Typen aus GCode-Kommentaren:
  - `;TYPE:SKIRT` → `L_skirt_sub.nc`
  - `;TYPE:SKIN` → `L_skin_sub.nc`
  - `;TYPE:WALL-OUTER` / `;TYPE:WALL-INNER` → `L_wall_sub.nc`
  - `;TYPE:INFILL` → `L_infill_sub.nc`
- Subroutinen werden automatisch eingefügt, wenn Layer-Typ wechselt

**Dateien:**
- `Components/Export/DXRPostprocessorComponent.cs` (Parser erweitert)
- `Utils/DXRHelper.cs` (Subroutine-Insertion)

### 5. ✅ Retraktion ohne Bewegung behandelt
**Problem:** Retraktion ohne Bewegung (nur E-Wert, kein X/Y/Z) wurde nicht korrekt behandelt.

**Lösung:**
- Parser erkennt Retraktion: Negativer E-Wert ohne X/Y/Z-Änderung
- Spezielles Format: `N{line_num} G1 G91 XE=[{retraction}*P1] G90 F{feedrate}`
- Standard-Feedrate für Retraktion: 30000 mm/min (falls nicht angegeben)

**Dateien:**
- `Components/Export/DXRPostprocessorComponent.cs` (Parser erweitert)
- `Utils/DXRHelper.cs` (Retraktion-Format)

### 6. ✅ MachineSettings erweitert
**Problem:** Nur einfache Temperatur/Fan-Settings, keine Multi-Zone-Unterstützung.

**Lösung:**
- **Neue Properties:**
  - `UseAdvancedFormat` - Wechsel zwischen V.P.VAR_* (einfach) und V.E.GLOBAL_* (erweitert)
  - Printbed Zones 1-4 (V.E.GLOBAL_BOOL[72/74/76/78])
  - Extruder Zones:
    - Filling Zone (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[41])
    - Heating Zone 1 (V.E.GLOBAL_BOOL[24], V.E.GLOBAL[55])
    - Heating Zone 2 (V.E.GLOBAL_BOOL[26], V.E.GLOBAL[57])
    - Nozzle Zone (V.E.GLOBAL_BOOL[40], V.E.GLOBAL[71])
  - Fan (V.E.GLOBAL_BOOL[44], V.E.GLOBAL[3])

- **Backward Compatible:** Alte Konstruktoren funktionieren weiterhin
- **Neue Konstruktoren:** Für erweiterte Multi-Zone-Settings

**Datei:** `Types/MachineSettings.cs`

## Neue Features

### Layer-Typ-Erkennung
Der Parser erkennt automatisch Layer-Typen aus GCode-Kommentaren:
- `;TYPE:SKIRT` / `;TYPE:SUPPORT-INTERFACE` → SKIRT
- `;TYPE:SKIN` → SKIN
- `;TYPE:WALL-OUTER` / `;TYPE:WALL-INNER` / `BRIDGE` → WALL
- `;TYPE:INFILL` → INFILL

### Retraktion-Erkennung
- Automatische Erkennung von Retraktion ohne Bewegung
- Spezielles DXR-Format für Retraktion
- Statistiken in Process Info

### Erweiterte Temperatur-Settings
- Unterstützung für Multi-Zone Printbed (4 Zonen)
- Unterstützung für Multi-Zone Extruder (Filling, Heating 1-2, Nozzle)
- Fan mit GLOBAL_BOOL und GLOBAL Variablen
- Automatisches Shutdown aller Zonen am Ende

## Format-Änderungen

### Bewegung (vorher → nachher)
```
Vorher: N100 G1 X121.040 Y65.250 Z2.000 G91 XE=[56.500*P1] G90 F6059.000
Nachher: N100 G1 F6059.000 X121.040 Y65.250 Z2.000 A0.0 B0.0 C0.0 G91 XE=[56.500*P1] G90
```

### Retraktion (neu)
```
N{line_num} G1 G91 XE=[-1.300*P1] G90 F30000.000
```

### Header (neu)
```
;Eges = IC[1055468.943]
```

### Layer-Typ-Subroutinen (neu)
```
N{line_num} L L_skirt_sub.nc
N{line_num} L L_skin_sub.nc
N{line_num} L L_wall_sub.nc
N{line_num} L L_infill_sub.nc
```

## Backward Compatibility

✅ **Alle Änderungen sind backward compatible:**
- Alte MachineSettings-Konstruktoren funktionieren weiterhin
- Layer-Typ-Subroutinen sind optional (nur wenn erkannt)
- Retraktion wird automatisch erkannt
- Euler-Winkel werden standardmäßig auf 0.0 gesetzt

## Nächste Schritte (Optional)

1. **MachineSettingsComponent erweitern:** UI für Multi-Zone-Settings
2. **Testing:** Mit echten Robotern testen
3. **Dokumentation:** DXR_FORMAT_DOCUMENTATION.md aktualisieren
4. **Weitere Layer-Typen:** Support für weitere GCode-Kommentare

## Dateien geändert

1. `Types/MachineSettings.cs` - Erweitert für Multi-Zone
2. `Utils/DXRHelper.cs` - F-Position, Euler-Winkel, Eges, Layer-Typ-Subroutinen, Retraktion
3. `Components/Export/DXRPostprocessorComponent.cs` - Layer-Typ-Parser, Retraktion-Erkennung

