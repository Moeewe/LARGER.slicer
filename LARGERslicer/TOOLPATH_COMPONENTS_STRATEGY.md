# Toolpath Components - Anforderungsliste & Strategieübersicht

## 1. Forschungsgrundlage & Referenzen

### 1.1 Wissenschaftliche Quellen
- **"Connected Fermat Spirals for Layered Fabrication"** (Paper)
- **Springer Article**: Continuous Toolpath Planning (10.1007/s00170-021-08418-z)
- **IntechOpen Chapter**: Infill Pattern Optimization (50453)

### 1.2 Community-Implementierungen
- **Laurent Delrieu (Nautilus Plugin)**: 
  - Offset-basierte kontinuierliche Pfade
  - Random Bridge Strategy
  - Self-Intersection Suppression (Euler-Cycle)
  - ArcWelder Integration
- **McNeel Forum Diskussionen**:
  - Continuous Toolpath Planning
  - Nautilus Single Line Path
  - Contour-to-Infill Approaches

### 1.3 Kernprinzipien aus Recherche
1. **Single Continuous Path**: Keine Retractions, keine Travel Moves
2. **Offset-basierte Strategie**: Clipper-ähnliche Offset-Kurven
3. **Bridge Placement**: Random Bridges verhindern Overfill
4. **Self-Intersection Handling**: Euler-Cycle Ansatz für Undercuts
5. **ArcWelder**: Polyline → Lines/Arcs für optimiertes GCode

---

## 2. Komponenten-Kategorisierung

### 2.1 Pattern-Generatoren (Hauptkomponenten)
**Zweck**: Generieren kontinuierliche Infill-Patterns aus Boundary-Kurven

| Komponente | Pattern-Typ | Kontinuität | Empfehlung |
|------------|-------------|-------------|------------|
| **Spiral/Concentric** | Offset-Kurven nach innen | ✅ Perfekt | ⭐⭐⭐ **Hauptkomponente** |
| **Contour** | Offset-Kurven (Clipper-ähnlich) | ✅ Perfekt | ⭐⭐⭐ **Hauptkomponente** |
| **Hilbert Curve** | Space-filling Curve | ✅ Perfekt | ⭐⭐ Optional |
| **Grid/Zigzag** | Parallele Linien (Boustrophedon) | ❌ Travel Moves | ⭐ Nur für spezielle Fälle |
| **Lines** | Unidirektionale Linien | ❌ Travel Moves | ⭐ Nur für spezielle Fälle |

### 2.2 Toolpath-Vorbereitung (Utility-Komponenten)
**Zweck**: Vorbereiten und optimieren von Kurven für kontinuierliche Pfade

| Komponente | Funktion | Notwendigkeit |
|------------|----------|---------------|
| **Align Curves** | Kurven in eine Richtung ausrichten | ✅ Wichtig |
| **Alternate Curves** | Kurven abwechselnd links/rechts | ✅ Wichtig (für Zigzag) |
| **Join Open Contours** | Offene Konturen verbinden | ✅ Wichtig |
| **Bridge Curves** | Kurven mit Brücken verbinden | ✅ Wichtig (Random Bridges) |
| **Suppress Self Intersections** | Self-Intersections heilen | ✅ Wichtig (Undercuts) |

### 2.3 Advanced Toolpath-Generatoren
**Zweck**: Komplexe Algorithmen für optimale Pfade

| Komponente | Algorithmus | Notwendigkeit |
|------------|------------|---------------|
| **Eulerian Path** | Graph-basierter Euler-Pfad (Chinese Postman) | ⚠️ Experimentell |
| **Continuous Toolpath** | Offset + Self-Intersection Handling | ✅ Wichtig |
| **Continuous Path from Curves** | Infill + Boundary Segmentierung | ✅ Wichtig |

---

## 3. Strategien pro Komponententyp

### 3.1 Pattern-Generatoren: Basis-Strategie

#### **Gemeinsame Architektur**
```
Input: Boundary Curve → Pattern Generation → Path Optimization → Output: Continuous Path
```

#### **Kern-Strategie für Spiral/Contour (Empfohlen)**
1. **Boundary Preparation**:
   - Boundary schließen (falls offen)
   - Boundary Offset anwenden (Layer Width + Additional Offset)
   - Holes validieren und offsetten

2. **Pattern Generation**:
   - **Spiral**: Offset-Kurven nach innen generieren (bis Min-Radius)
   - **Contour**: Offset-Kurven mit Hole-Handling (Clipper-ähnlich)
   - Filter: Nur Kurven innerhalb Boundary, außerhalb Holes

3. **Path Connection**:
   - Kurven-Order optimieren (Nearest-Neighbor)
   - Random Bridges zwischen Kurven (optional, Density-basiert)
   - Kontinuierlichen Pfad erstellen

4. **Output**:
   - Single Polyline Curve
   - Segment-Liste für Preview
   - Path Points für GCode
   - Statistics

#### **Strategie für Grid/Lines (Nur spezielle Fälle)**
1. **Pattern Generation**:
   - Parallele Linien generieren (Winkel-basiert)
   - Linien an Boundary trimmen
   - Self-Intersections unterdrücken (für Undercuts)

2. **Path Connection**:
   - Linien-Order optimieren
   - Alternating Direction (Links-Rechts)
   - Travel Moves minimieren

3. **Einschränkung**: Nicht wirklich kontinuierlich, nur für spezielle Anwendungen

### 3.2 Utility-Komponenten: Strategien

#### **Align Curves**
- **Strategie**: Erste Kurve als Referenz, alle anderen daran ausrichten
- **Methode**: Dot-Product zwischen Richtungsvektoren
- **Output**: Ausgerichtete Kurven + Flipped-Flags

#### **Alternate Curves**
- **Strategie**: Links-Rechts-Pattern (Zigzag)
- **Methode**: X-Komponente des Richtungsvektors prüfen
- **Output**: Alternierte Kurven + Flipped-Flags

#### **Join Open Contours**
- **Strategie**: Nearest-Neighbor Ordering + Transitions
- **Methode**: Greedy Algorithm für minimale Travel-Distanz
- **Output**: Kontinuierlicher Pfad + Transitions

#### **Bridge Curves**
- **Strategie**: Random Bridge Placement (Laurent Delrieu)
- **Methode**: Density-basierte Zufallsauswahl
- **Output**: Pfad mit Bridges + Bridge-Liste

#### **Suppress Self Intersections**
- **Strategie**: Euler-Cycle Ansatz
- **Methode**: Bei Self-Intersection Richtung umkehren
- **Output**: Geheilte Kurven + Intersection-Punkte

### 3.3 Advanced Generatoren: Strategien

#### **Continuous Toolpath**
- **Strategie**: Kombination aus Offset + Self-Intersection Handling
- **Workflow**:
  1. Offset-Kurven mit Undercut-Handling generieren
  2. Self-Intersections unterdrücken
  3. Kurven filtern (Boundary + Holes)
  4. Order optimieren
  5. Random Bridges hinzufügen
  6. ArcWelder (optional)

#### **Eulerian Path**
- **Strategie**: Graph-basierter Ansatz (Chinese Postman Problem)
- **Workflow**:
  1. Pattern-Segmente generieren
  2. Graph aus Segmenten bauen
  3. Disconnected Components bridgen
  4. Graph Eulerian machen (Odd-Degree Nodes paaren)
  5. Euler-Pfad finden (Hierholzer Algorithm)
  6. Polyline konvertieren
- **Status**: ⚠️ Experimentell, Performance-Probleme

---

## 4. Empfohlene Komponenten-Architektur

### 4.1 Core Pattern Components (Neu bauen)
**Priorität: HOCH**

1. **BottomLayerSpiralComponent** ⭐⭐⭐
   - Basis: Offset-Kurven nach innen
   - Features: Clockwise/Counterclockwise, Min-Radius
   - Output: Kontinuierlicher Spiral-Pfad

2. **BottomLayerContourComponent** ⭐⭐⭐
   - Basis: Offset-Kurven mit Hole-Handling
   - Features: Random Bridges, ArcWelder
   - Output: Kontinuierlicher Contour-Pfad

3. **BottomLayerHilbertComponent** ⭐⭐
   - Basis: Space-filling Hilbert Curve
   - Features: Order-basierte Auflösung
   - Output: Kontinuierlicher Hilbert-Pfad

### 4.2 Utility Components (Neu bauen)
**Priorität: MITTEL**

4. **AlignCurvesComponent** ✅
   - Einfache Richtungsausrichtung
   - Robust gegen geschlossene Kurven

5. **AlternateCurvesComponent** ✅
   - Links-Rechts-Pattern
   - Für Zigzag-Optimierung

6. **JoinOpenContoursComponent** ✅
   - Nearest-Neighbor Ordering
   - Transition-Typen (Linear/Arc)

7. **BridgeCurvesComponent** ✅
   - Random Bridge Placement
   - Density-basiert

8. **SuppressSelfIntersectionsComponent** ✅
   - Euler-Cycle Ansatz
   - Split-Option

### 4.3 Advanced Components (Neu bauen)
**Priorität: NIEDRIG (später)**

9. **ContinuousToolpathComponent** ⚠️
   - Kombiniert mehrere Strategien
   - Komplex, aber mächtig

10. **EulerianPathComponent** ⚠️
    - Graph-basiert
    - Performance-Probleme, experimentell

---

## 5. Implementierungs-Strategien

### 5.1 Basis-Klasse: BottomLayerPatternBase

**Gemeinsame Inputs**:
- `Curve`: Boundary (wird intern geschlossen)
- `Seam Point`: Optional (Auto-Berechnung)
- `Line Spacing`: Extrusion Width
- `Boundary Offset`: Zusätzlicher Offset
- `Holes`: Optionale Löcher

**Gemeinsame Outputs**:
- `Path`: Kontinuierlicher Pfad (Polyline)
- `Segments`: Einzelne Segmente (Preview)
- `Path Points`: Punkt-Liste (GCode)
- `Stats`: Statistiken

**Gemeinsame Methoden**:
- `ValidateInputs()`: Input-Validierung
- `PrepareBoundary()`: Boundary vorbereiten
- `GetSeamPosition()`: Seam-Position berechnen
- `IsPointValid()`: Punkt-Validierung (Boundary + Holes)
- `CreateOutputCurves()`: Output-Kurven erstellen
- `CalculateStatistics()`: Statistiken berechnen

### 5.2 Pattern-Generation-Strategie

**Für Spiral/Contour**:
```csharp
protected (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
    Curve boundary,
    Point3d seamPosition,
    double seamParameter,
    double spacing,
    List<Curve> holes)
{
    // 1. Generate offset curves
    var offsetCurves = GenerateOffsetCurves(boundary, spacing, holes);
    
    // 2. Filter valid curves (inside boundary, outside holes)
    var validCurves = FilterValidCurves(offsetCurves, boundary, holes);
    
    // 3. Optimize curve order (nearest-neighbor)
    var orderedCurves = OptimizeCurveOrder(validCurves, seamPosition);
    
    // 4. Sample curves to points
    var pathPoints = SampleCurvesToPoints(orderedCurves, spacing);
    
    // 5. Add random bridges (optional)
    if (useRandomBridges)
        pathPoints = AddRandomBridges(pathPoints, bridgeDensity, spacing);
    
    return (pathPoints, segments);
}
```

**Für Grid/Lines**:
```csharp
protected (List<Point3d> pathPoints, List<List<Point3d>> segments) GeneratePattern(
    Curve boundary,
    Point3d seamPosition,
    double spacing,
    double angle,
    List<Curve> holes)
{
    // 1. Generate parallel lines
    var lines = GenerateParallelLines(boundary, spacing, angle);
    
    // 2. Trim lines at boundary
    var trimmedLines = TrimLinesAtBoundary(lines, boundary);
    
    // 3. Suppress self-intersections (for undercuts)
    var healedLines = SuppressSelfIntersections(trimmedLines);
    
    // 4. Filter valid lines
    var validLines = FilterValidLines(healedLines, boundary, holes);
    
    // 5. Optimize order and alternate direction
    var orderedLines = OptimizeAndAlternate(validLines, seamPosition);
    
    // 6. Sample to points
    var pathPoints = SampleLinesToPoints(orderedLines, spacing);
    
    return (pathPoints, segments);
}
```

### 5.3 Helper-Klassen-Strategie

**PathHelper** (Basis-Utilities):
- `GenerateOffsetCurves()`: Offset-Kurven generieren
- `GenerateParallelLines()`: Parallele Linien generieren
- `OptimizeCurveOrder()`: Nearest-Neighbor Ordering
- `SampleCurve()`: Kurve zu Punkten samplen
- `FindSeamPosition()`: Seam-Position finden

**SelfIntersectionHelper** (Undercut-Handling):
- `SuppressSelfIntersections()`: Self-Intersections heilen
- `GenerateOffsetCurvesWithUndercutHandling()`: Offset mit Undercut-Handling

**ArcWelderHelper** (GCode-Optimierung):
- `ConvertToLinesAndArcs()`: Polyline → Lines/Arcs
- `GetConversionStats()`: Konversions-Statistiken

**GraphHelper & EulerPathHelper** (Experimentell):
- Nur für EulerianPathComponent
- Performance-kritisch, später optimieren

---

## 6. Implementierungs-Plan

### Phase 1: Core Pattern Components (Priorität 1)
**Ziel**: Robuste, performante Pattern-Generatoren

1. **BottomLayerSpiralComponent** ✅
   - Offset-Kurven nach innen
   - Clockwise/Counterclockwise
   - Min-Radius Support
   - Random Bridges (optional)

2. **BottomLayerContourComponent** ✅
   - Offset-Kurven mit Hole-Handling
   - Random Bridges
   - ArcWelder Support

3. **BottomLayerHilbertComponent** ✅
   - Hilbert Curve Generation
   - Order-basierte Auflösung

### Phase 2: Utility Components (Priorität 2)
**Ziel**: Toolpath-Vorbereitung und -Optimierung

4. **AlignCurvesComponent** ✅
5. **AlternateCurvesComponent** ✅
6. **JoinOpenContoursComponent** ✅
7. **BridgeCurvesComponent** ✅
8. **SuppressSelfIntersectionsComponent** ✅

### Phase 3: Advanced Components (Priorität 3)
**Ziel**: Komplexe Algorithmen (später)

9. **ContinuousToolpathComponent** ⚠️
   - Kombiniert mehrere Strategien
   - Robustheit verbessern

10. **EulerianPathComponent** ⚠️
    - Performance optimieren
    - Oder entfernen wenn nicht praktikabel

### Phase 4: Grid/Lines (Nur bei Bedarf)
**Ziel**: Spezielle Anwendungsfälle

11. **BottomLayerGridComponent** ⭐
12. **BottomLayerLinesComponent** ⭐

---

## 7. Qualitätskriterien

### 7.1 Funktionalität
- ✅ Kontinuierliche Pfade (keine Retractions)
- ✅ Korrekte Boundary- und Hole-Behandlung
- ✅ Undercut-Handling (Self-Intersections)
- ✅ Robuste Fehlerbehandlung

### 7.2 Performance
- ✅ Schnelle Berechnung (< 1 Sekunde für normale Geometrien)
- ✅ Effiziente Algorithmen (O(n log n) oder besser)
- ✅ Keine Memory-Leaks

### 7.3 Benutzerfreundlichkeit
- ✅ Einfache Inputs (nur Curve, nicht Boundary + Layer Area)
- ✅ Klare Outputs (Path, Segments, Points, Stats)
- ✅ Informative Fehlermeldungen
- ✅ Optional Parameters mit sinnvollen Defaults

---

## 8. Technische Entscheidungen

### 8.1 Input-Simplification
**Entscheidung**: Nur `Curve` Input, kein `Boundary` + `Layer Area`
- Boundary wird intern aus Curve abgeleitet
- Einfacher für Benutzer
- Weniger Fehlerquellen

### 8.2 Pattern-Generation
**Entscheidung**: Offset-basierte Strategie für Spiral/Contour
- Bewährt (Laurent Delrieu, Nautilus)
- Funktioniert mit komplexen Geometrien
- Unterstützt Holes automatisch

### 8.3 Self-Intersection Handling
**Entscheidung**: Euler-Cycle Ansatz
- Bewährt (Laurent Delrieu)
- Heilt Undercuts automatisch
- Einfacher als komplexe Splitting-Algorithmen

### 8.4 Bridge Strategy
**Entscheidung**: Random Bridges
- Verhindert Overfill-Akkumulation
- Bewährt (Laurent Delrieu)
- Density-basiert für Kontrolle

### 8.5 Graph-basierte Ansätze
**Entscheidung**: Experimentell, später optimieren
- EulerianPathComponent ist komplex
- Performance-Probleme
- Nur für spezielle Fälle

---

## 9. Nächste Schritte

### Schritt 1: Cleanup
- Alte, fehlerhafte Implementierungen entfernen
- Code-Struktur vereinfachen

### Schritt 2: Core Components neu bauen
- BottomLayerSpiralComponent (robust, performant)
- BottomLayerContourComponent (robust, performant)
- BottomLayerHilbertComponent (optional)

### Schritt 3: Utility Components überprüfen
- AlignCurvesComponent
- AlternateCurvesComponent
- JoinOpenContoursComponent
- BridgeCurvesComponent
- SuppressSelfIntersectionsComponent

### Schritt 4: Testing & Validation
- Verschiedene Geometrien testen
- Performance messen
- Edge Cases abdecken

### Schritt 5: Advanced Components (später)
- ContinuousToolpathComponent verbessern
- EulerianPathComponent optimieren oder entfernen

---

## 10. Offene Fragen

1. **Grid/Lines Components**: Wirklich nötig? (Travel Moves = nicht kontinuierlich)
2. **EulerianPathComponent**: Performance-Probleme lösen oder entfernen?
3. **ArcWelder**: In allen Components oder nur Contour?
4. **Bridge Density**: Wie optimal wählen?
5. **Self-Intersection Tolerance**: Wie optimal wählen?

---

**Erstellt**: 2024
**Status**: Strategie-Dokument für Neuimplementierung
**Nächster Schritt**: Cleanup & Core Components neu bauen

