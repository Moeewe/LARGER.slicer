# Kategorien-Sortierung Optimierung

## Analyse der Referenzbilder

**Bild 1 (Referenz):**
- Kategorien sind vertikale Panels mit mehreren Icons
- Logische Gruppierung: Analysis, Primitive, Triangulation, Util
- Jede Kategorie enthält mehrere verwandte Komponenten

**Bild 2 (Aktuell):**
- Separate Kategorien: "Utili...", "DX...", "CNC"
- Problem: Jede Komponente hat fast eine eigene Kategorie
- Keine Subkategorien sichtbar

## Optimierte Sortierung

Basierend auf dem Referenzbild sollte die Sortierung sein:

### Hauptkategorie: LARGERslicer

**1. CNC** (1 Komponente)
- CNC - Program

**2. DXR Processing** (3 Komponenten)
- Machine Settings (Settings)
- DXR Generator (Generator)
- DXR GCode Postprocessor (Postprocessor)

**3. Utilities** (8 Komponenten)
- Date Timestamp
- Desktop Path
- Safe Component (Save)
- Custom Preview Lineweights
- RTree Closest Point
- RTree Sort
- Feedrate Calculator
- Stream Freeze

## Sortierungslogik

Die Sortierung erfolgt durch:
1. Führende Leerzeichen in Subkategorienamen (mehr Leerzeichen = früher)
2. Alphabetische Sortierung innerhalb der Gruppe
3. Vertikale Striche (|) für visuelle Trennung

## Implementierung

Die Subkategorien sollten so benannt werden:
- "CNC" (keine Leerzeichen - erscheint zuerst)
- " DXR Processing | Settings" (1 Leerzeichen)
- " DXR Processing | Generator" (1 Leerzeichen)
- " DXR Processing | Postprocessor" (1 Leerzeichen)
- "  Utilities | Timestamp" (2 Leerzeichen)
- "  Utilities | Path" (2 Leerzeichen)
- "  Utilities | Save" (2 Leerzeichen)
- "  Utilities | Lineweights" (2 Leerzeichen)
- "  Utilities | RTree" (2 Leerzeichen)
- "  Utilities | Feedrate" (2 Leerzeichen)
- "  Utilities | Freeze" (2 Leerzeichen)




