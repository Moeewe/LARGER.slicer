# Yak Package Fix - Plugin Loading Issues

## Problem
Plugin erscheint nicht in Grasshopper Plugin-Leiste nach Installation über Rhino Package Manager.

## Identifizierte und behobene Probleme

### 1. Doppelte ComponentGuids (KRITISCH - BEHOBEN)
- **Problem:** `CurveBoundaryInfillComponent` und `MachineSettingsExtendedComponent` hatten denselben ComponentGuid
- **Lösung:** `CurveBoundaryInfillComponent` GUID geändert zu: `C1B2A3D4-E5F6-7890-ABCD-EF1234567890`
- **Status:** ✅ BEHOBEN

### 2. Category-Mismatch (BEHOBEN)
- **Problem:** In `LARGERslicer.cs` wurde "LARGERslicer" als Category registriert, Komponenten verwenden "LARGER"
- **Lösung:** Category-Registrierung geändert zu "LARGER"
- **Status:** ✅ BEHOBEN

### 3. Falsche Package-Struktur (KRITISCH - BEHOBEN)
- **Problem:** Yak findet .gha Dateien nicht automatisch, wenn sie in `bin/Release/` sind
- **Lösung:** .gha Dateien müssen in framework-spezifischen Verzeichnissen sein:
  - `net48/LARGERslicer.gha`
  - `net7.0-windows/LARGERslicer.gha`
  - `net7.0/LARGERslicer.gha`
- **Status:** ✅ BEHOBEN

### 4. secret.id im manifest.yml (PROBLEM)
- **Problem:** Yak entfernt `secret.id` beim Build, obwohl es im manifest.yml steht
- **Aktueller Status:** GUID wird als Keyword hinzugefügt (`- guid:...`) statt als `secret.id`
- **Mögliche Lösung:** Package nach Build manuell bearbeiten oder Yak-Version prüfen
- **Status:** ⚠️ TEILWEISE - GUID ist im Package, aber falsch formatiert

## Package-Struktur (KORREKT)

```
LARGERslicer/
├── manifest.yml          # Package metadata (wird von Yak modifiziert)
├── yak.yml              # Yak configuration
├── net48/
│   └── LARGERslicer.gha  # Rhino 7 / .NET Framework 4.8
├── net7.0-windows/
│   └── LARGERslicer.gha  # Rhino 8 Windows / .NET 7.0
└── net7.0/
    └── LARGERslicer.gha  # Rhino 8 Mac / .NET 7.0
```

## Build-Prozess

1. **.gha Dateien in richtige Verzeichnisse kopieren:**
   ```bash
   mkdir -p net48 net7.0-windows net7.0
   cp bin/Release/net48/LARGERslicer.gha net48/
   cp bin/Release/net7.0-windows/LARGERslicer.gha net7.0-windows/
   cp bin/Release/net7.0/LARGERslicer.gha net7.0/
   ```

2. **Package bauen:**
   ```bash
   yak build
   ```

3. **Package hochladen:**
   ```bash
   yak push largerslicer-1.0.20-rh8_0-any.yak
   ```

## Bekannte Probleme

### secret.id wird entfernt
Yak entfernt das `secret.id` Feld beim Build, obwohl es im manifest.yml steht. Die GUID wird stattdessen als Keyword hinzugefügt (`- guid:...`).

**Workaround:** Das Package sollte trotzdem funktionieren, da:
1. Die .gha Dateien sind im richtigen Verzeichnis
2. Die GUID ist im Package (als Keyword)
3. Die Assembly GUID ist in der .gha Datei enthalten

**Mögliche Lösung:** Package nach Build manuell bearbeiten und `secret.id` wieder hinzufügen.

## Nächste Schritte

1. ✅ Package wurde hochgeladen (Version 1.0.20)
2. ⚠️ Testen in Rhino/Grasshopper ob Plugin jetzt erscheint
3. ⚠️ Falls nicht: Package manuell bearbeiten und `secret.id` hinzufügen
4. ⚠️ Falls weiterhin Probleme: Yak-Version prüfen oder McNeel Support kontaktieren

## Version History

- **v1.0.20** - Package-Struktur korrigiert, .gha Dateien in framework-Verzeichnissen
- **v1.0.19** - Doppelte ComponentGuids behoben, Category-Mismatch behoben

---

*Last updated: 2025-12-11*








