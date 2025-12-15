# Plugin Loading Issues - Troubleshooting Guide

## Problem: Plugin erscheint nicht in Grasshopper Plugin-Leiste

### Identifizierte Probleme und Lösungen

#### 1. Doppelte ComponentGuids (KRITISCH - BEHOBEN)

**Problem:**
- `CurveBoundaryInfillComponent` und `MachineSettingsExtendedComponent` hatten denselben ComponentGuid
- Doppelte GUIDs verhindern, dass Komponenten korrekt geladen werden
- Grasshopper kann nicht zwei Komponenten mit derselben GUID registrieren

**Lösung:**
- `CurveBoundaryInfillComponent` GUID geändert zu: `C1B2A3D4-E5F6-7890-ABCD-EF1234567890`
- `MachineSettingsExtendedComponent` behält: `B1C2D3E4-F5A6-7890-BCDE-F123456789AB`

**Status:** ✅ BEHOBEN

#### 2. Category-Mismatch (BEHOBEN)

**Problem:**
- In `LARGERslicer.cs` wurde "LARGERslicer" als Category registriert
- Alle Komponenten verwenden "LARGER" als Category
- Mismatch kann zu Problemen beim Laden führen

**Lösung:**
- Category-Registrierung in `LARGERslicer.cs` geändert von "LARGERslicer" zu "LARGER"
- Jetzt konsistent mit allen Komponenten

**Status:** ✅ BEHOBEN

---

## Weitere mögliche Probleme und Lösungen

### 3. Build-Konfiguration

**Überprüfen:**
- `.gha` Dateien werden für alle Target Frameworks gebaut:
  - `net48` (Rhino 7)
  - `net7.0` (Rhino 8 Mac)
  - `net7.0-windows` (Rhino 8 Windows)

**Lösung:**
```bash
dotnet build -c Release
```

Stellen Sie sicher, dass alle drei `.gha` Dateien erstellt werden:
- `bin/Release/net48/LARGERslicer.gha`
- `bin/Release/net7.0/LARGERslicer.gha`
- `bin/Release/net7.0-windows/LARGERslicer.gha`

### 4. Yak Package Build

**Überprüfen:**
- Yak-Package wird korrekt gebaut
- Alle `.gha` Dateien sind im Package enthalten

**Lösung:**
```bash
yak build
```

Überprüfen Sie die `.yak` Datei:
```bash
yak spec LARGERslicer-1.0.18.yak
```

### 5. Installation über Yak

**Überprüfen:**
- Plugin ist korrekt installiert
- Keine Konflikte mit vorherigen Versionen

**Lösung:**
```bash
# Alte Version deinstallieren
yak remove LARGERslicer

# Neue Version installieren
yak install LARGERslicer-1.0.18.yak --source .
```

### 6. Grasshopper Plugin-Ordner

**Überprüfen:**
- Plugin ist im korrekten Ordner installiert
- Keine Berechtigungsprobleme

**Windows:**
- `%APPDATA%\Grasshopper\Libraries\`
- `%LOCALAPPDATA%\Grasshopper\Libraries\`

**macOS:**
- `~/Library/Application Support/Grasshopper/Libraries/`

### 7. Rhino/Grasshopper Version

**Überprüfen:**
- Kompatible Rhino/Grasshopper Version
- Plugin wurde für die richtige Version gebaut

**Aktuell unterstützt:**
- Rhino 7 (net48)
- Rhino 8 (net7.0, net7.0-windows)

### 8. Component Registration

**Überprüfen:**
- Alle Komponenten haben eindeutige ComponentGuids
- Alle Komponenten erben von `GH_Component`
- Category und SubCategory sind korrekt gesetzt

**Debugging:**
Öffnen Sie Grasshopper und prüfen Sie:
- `File > Special Folders > Components` - zeigt alle geladenen Komponenten
- `File > Preferences > Plugins` - zeigt alle geladenen Plugins

---

## Debugging-Schritte

### 1. Plugin-Loading-Logs prüfen

**Windows:**
```
%APPDATA%\Grasshopper\Logs\
```

**macOS:**
```
~/Library/Application Support/Grasshopper/Logs/
```

Suchen Sie nach Fehlermeldungen bezüglich:
- ComponentGuid-Konflikte
- Category-Registrierungsprobleme
- Assembly-Loading-Fehler

### 2. Manuelle Plugin-Installation testen

1. Kopieren Sie die `.gha` Datei manuell in den Grasshopper Libraries-Ordner
2. Starten Sie Rhino/Grasshopper neu
3. Prüfen Sie, ob das Plugin geladen wird

### 3. Component Server prüfen

In Grasshopper:
```
File > Special Folders > Components
```

Suchen Sie nach "LARGER" - alle Komponenten sollten hier erscheinen.

### 4. Category-Registrierung prüfen

In Grasshopper:
- Öffnen Sie die Component-Palette
- Suchen Sie nach "LARGER" Kategorie
- Prüfen Sie, ob alle Subcategories vorhanden sind:
  - LARGER > CNC
  - LARGER > DXR
  - LARGER > Toolpaths
  - LARGER > Utilities

---

## Bekannte Probleme und Workarounds

### Problem: Plugin erscheint nach Yak-Installation nicht

**Mögliche Ursachen:**
1. Falsche Version im yak.yml (1.0.0 statt 1.0.18)
2. .gha Dateien nicht im Package enthalten
3. Falsche Target Framework

**Lösung:**
1. Version in yak.yml aktualisieren
2. `yak build` erneut ausführen
3. Package-Inhalt prüfen: `yak spec`

### Problem: Nur einige Komponenten erscheinen

**Mögliche Ursachen:**
1. Doppelte ComponentGuids (behoben)
2. Kompilierungsfehler in einzelnen Komponenten
3. Abhängigkeitsprobleme

**Lösung:**
1. Alle ComponentGuids auf Eindeutigkeit prüfen
2. Build-Logs auf Fehler prüfen
3. Komponenten einzeln testen

---

## Checkliste vor Veröffentlichung

- [ ] Alle ComponentGuids sind eindeutig
- [ ] Category-Registrierung ist konsistent ("LARGER")
- [ ] Alle Target Frameworks werden gebaut
- [ ] Yak-Package wird korrekt erstellt
- [ ] Version in yak.yml stimmt mit .csproj überein
- [ ] Plugin wurde lokal getestet
- [ ] Alle Komponenten erscheinen in Grasshopper

---

## Kontakt

Bei weiteren Problemen:
- GitHub Issues: https://github.com/Moeewe/LARGER.slicer
- Email: m.wesseler@fh-muenster.de

---

*Last updated: 2025-01-XX*
*Fixed issues: Duplicate ComponentGuids, Category mismatch*





