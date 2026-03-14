# Grasshopper Plugin: Thekenfront – Fräsvorbereitung
**Version 1.1** — Stand: März 2026

---

## Übersicht

Das Plugin erhält **einen Abschnitt** der Thekenfront als geschlossenes Solid (der Nutzer teilt die Theke selbst auf). Es bereitet diesen Abschnitt vollständig für die CNC-Fertigung vor:

- Ausrichtung & Orientierung des Solids
- Treppenartig verleimter Brettblock inkl. Fugenlogik
- **Fuge-Bretter werden in 2 separate Fräselemente aufgeteilt** → eigene Bounding Boxes → Materialersparnis
- Saugelement / Aufspanngeometrie (Seitenelemente)
- Stückliste als GH-Panel + CSV-Export
- 3D-Export

---

## Modul 1 – Eingabe & Ausrichtung

### Eingabe
- **1× geschlossenes Brep/Solid** (ein Thekenabschnitt, vom Nutzer vorher aufgeteilt)
- Manuelle Auswahl der **Frontfläche** über Explode Brep + Index-Slider  
  *alternativ: automatische Erkennung via Normalenvektor (Fläche mit größter Y-Komponente)*

### Verarbeitung
1. Explode Brep → alle Flächen
2. Frontfläche identifizieren (Index oder Auto)
3. Transformationsmatrix berechnen: Frontfläche zeigt nach **oben** (→ liegt auf World-XY)
4. Gesamtes Solid mit dieser Transformation in die Zielposition bringen
5. Ergebnis: Solid steht **rechtwinklig zur Weltebene**, Front nach oben

### Schnittebenen
> Alle Schnittebenen liegen **waagerecht zur World-XY-Ebene** (nach Ausrichtung in diesem Modul)

### Parameter
| Parameter | Typ | Standard |
|---|---|---|
| Frontflächen-Index | Int Slider | 0 |
| Ausrichtungs-Methode | Toggle Index/Auto | Auto |

---

## Modul 2 – Bounding Box & Blockdefinition

### Verarbeitung
1. Axis-Aligned Bounding Box des ausgerichteten Solids (in World-XY)
2. Box: oben und unten **parallel zur Weltebene**
3. Höhe (Z), Breite (X = Längsrichtung der Theke), Tiefe (Y) des Blocks ermitteln
4. Diese Werte fließen direkt in Modul 3

---

## Modul 3 – Bretteinteilung & Fugenlogik

### Bretthöhen-Logik

```
Brett 1 (unten):    35 mm  [fix, Randmaß]
Brett 2 … n-1:      z.B. 30 mm  [konfigurierbar, Normalstärke]
Brett n (oben):     35 mm  [fix, Randmaß]
Restmaß → Fuge(n):  verbleibendes Maß wird auf definierte Fugen verteilt
```

Das Restmaß fällt nie weg – es landet vollständig in der/den Fuge(n).
Fugenbreite mindestens **4 mm** (Sägeblatt + Toleranz).

### Fugenlogik

| Modus | Verhalten |
|---|---|
| 1 Fuge | Liegt automatisch **mittig** der Gesamthöhe. Verschiebbar per Slider (absolut in mm). Bei Eingabe 400 mm → Fuge zentriert bei 400 mm (±2 mm) |
| 2+ Fugen | Gleichmäßige Verteilung (auto) **oder** manuelle Höhenangaben per Panel-Input |

- Fugenposition immer als **Mittelmaß** (±halbe Fugenbreite)
- Wenn manuell überschrieben: Eingabe = absolute Höhe der Fugenmitte in mm

### Parameter
| Parameter | Typ | Standard |
|---|---|---|
| Brettstärke Mitte | Num Slider | 30 mm |
| Randbrett unten | Num Slider | 35 mm |
| Randbrett oben | Num Slider | 35 mm |
| Fugenanzahl | Int Slider | 1 |
| Fugenposition (mm) | Num Input | Mitte auto |
| Fugenbreite (min) | Num Slider | 4 mm |

---

## Modul 3b – Fuge-Brett-Splitting (Materialersparnis)

### Konzept

Ein Brett, das eine Fuge enthält, muss ohnehin als **2 separate Teile** gefräst werden.
Statt einer gemeinsamen großen Bounding Box bekommt jede Hälfte eine **eigene minimale Bounding Box** → deutliche Materialersparnis, kleinere Rohlinge.

### Verarbeitung

```
Für jedes Brett, das eine Fuge enthält:

1. Brett an der Fugenposition in 2 Hälften teilen
   → Brett_A: von Unterkante Brett bis Fugenunterkante
   → Brett_B: von Fugenoberkante bis Oberkante Brett

2. Brett_A → eigene minimale BBox (Verschnitt mit Solid in diesem Z-Bereich)
3. Brett_B → eigene minimale BBox (Verschnitt mit Solid in diesem Z-Bereich)

4. Beide Hälften erhalten die gleiche Länge (Boxbreite + 2×100 mm)
   aber ggf. unterschiedliche Tiefe (aus Tiefenstufenraster, Modul 4)

5. Jede Hälfte = 1 eigenständiges Fräselement mit eigenem Saugelement
```

### Visualisierung (Seitenansicht)

```
  ┌──────────────────────┐  ← Randbrett oben (35mm)
  ├──────────────────────┤
  ├──────────────────────┤    Mittelbretter (30mm)
  ├──────────────────────┤
  ├──────────────────────┤  ← Brett_B (obere Hälfte des Fuge-Bretts)
  ╠══════════════════════╣  ← FUGE (≥4mm) — Schnittlinie
  ├──────────────────────┤  ← Brett_A (untere Hälfte)
  ├──────────────────────┤
  └──────────────────────┘  ← Randbrett unten (35mm)

  Brett_A → eigene BBox (ggf. geringere Tiefe nötig)
  Brett_B → eigene BBox (ggf. andere Tiefe)
  → Materialersparnis gegenüber einer gemeinsamen Box
```

### Parameter
| Parameter | Typ | Standard |
|---|---|---|
| Fuge-Split aktiv | Boolean Toggle | True |

---

## Modul 4 – Bretttiefe (Stufenraster)

### Logik

Die Tiefe jedes Bretts (bzw. jeder Bretthälfte) wird individuell berechnet:

1. Z-Bereich des Bretts gegen das Thekenfront-Solid schneiden
2. Von der resultierenden Schnittgeometrie die **maximale Tiefe** (Y-Richtung) messen
3. Puffer **5 mm** vorne (Frontseite) und **5 mm** hinten (Rückseite) addieren
4. Auf nächste **Tiefenstufe aufrunden** (aus dem Stufenraster)

### Tiefenstufenraster

```
Basistiefe:    150 mm  [konfigurierbar]
Schrittweite:   50 mm  [konfigurierbar]
→ Stufen: 150 → 200 → 250 → 300 → … (beliebig viele Stufen möglich)
```

Jedes Brett erhält die **kleinstmögliche Stufe**, die das Solid mit Puffer vollständig abdeckt.

### Brettlänge (einheitlich)

```
Länge = Boxbreite (X) + 2 × 100 mm Überstand links/rechts
```
Alle Bretter und Bretthälften haben **identische Länge**.

### Parameter
| Parameter | Typ | Standard |
|---|---|---|
| Tiefenbasis | Num Slider | 150 mm |
| Tiefenschritt | Num Slider | 50 mm |
| Puffer vorne | Num Slider | 5 mm |
| Puffer hinten | Num Slider | 5 mm |
| Überstand links/rechts | Num Slider | 100 mm |

---

## Modul 5 – Verleimblock & Containerbox

### Verarbeitung
1. Alle Bretter (inkl. gesplitteter Hälften) als gestapelten Block aufbauen
2. Containerbox = Gesamt-BBox des Blocks
3. Thekenfront-Solid liegt referenzweise im 3D-Raum darin
4. Darstellung als separierte Layer-Gruppen (Farben nach Tiefenstufe empfohlen)

### Ausgabe
- Bretter / Bretthälften: separate Brep-Objekte
- Containerbox: einzelnes Brep
- Thekenfront-Solid: Referenz (unverändert)

---

## Modul 6 – Saugelement / Fräsaufspanngeometrie

### Beschreibung

Aus den **100 mm Überstands-Bereichen** der Bretter (Stirnseiten links und rechts) wird je ein Aufspannelement für den Frästisch erzeugt:
- **Ausrichtung** des verleimten Blocks auf der Maschine
- **Vakuumspannen** (Aufsaugen)

Bei gesplitteten Fuge-Brettern: jede Hälfte bekommt ihr **eigenes Saugelement**.

### Verarbeitung
1. Stirnseiten-Kanten der 100 mm Überstände aller Bretter extrahieren
2. **Treppenkontur** (Outline) der gestaffelten Stirnseiten erzeugen
3. Outline vereinfachen / glätten für Fräsbarkeit:
   - Nicht jede Einzelstufe braucht eine eigene Kante
   - Vereinfachungstoleranz als Input (z.B. max. 2 mm Abweichung)
   - Algorithmus: RDP-Simplify oder Bogen-Approximation (festzulegen)
4. Outline **5 mm tief** versetzen (Y-Richtung nach hinten)
5. Umschließendes **Rechteck** um Outline legen
6. Extrudieren → Saugelement-Körper
7. Saugelement liegt **parallel zur Vorderseite der Bretter**
8. Maße: **200 mm Breite × Länge der Containerbox**

### Parameter
| Parameter | Typ | Standard |
|---|---|---|
| Outline-Tiefe | Num Slider | 5 mm |
| Vereinfachungstoleranz | Num Slider | 2 mm |
| Saugelement-Breite | Num Slider | 200 mm |
| Saugelement-Länge | Auto | = Containerbox-Länge |

---

## Modul 7 – Stückliste (BOM)

### Ausgabe pro Position

| Spalte | Inhalt |
|---|---|
| Pos. | Laufende Nummer |
| Typ | Randbrett / Mittelbrett / Fuge-Brett A / Fuge-Brett B |
| Länge (mm) | Einheitlich = Boxbreite + 200 mm |
| Tiefe (mm) | Tiefenstufe des Bretts |
| Höhe/Stärke (mm) | 35 / Brettstärke / Hälftenmaß |
| Anzahl | Gleiche L×T×H-Maße werden zusammengefasst |
| Material | Freitext-Input |
| Bemerkung | z.B. „Fuge oben", „Fuge unten", „Überstand 100mm" |

### Ausgabe-Formen
- **GH-Panel**: direkte Anzeige in Grasshopper (formatierte Tabelle)
- **CSV-Export**: kompatibel mit Excel / LibreOffice Calc

### Funktionen
- Automatische Zusammenfassung gleicher L×T×H-Maße → Anzahl
- Sortierung: unten → mitte → Fuge-Bretter → oben
- Separate Zeilen für Saugelemente

---

## Modul 8 – Export

### 3D-Daten
- Alle Bretter / Bretthälften als einzelne Geometrien
- Saugelemente (links / rechts, je Fräselement separat)
- Containerbox
- Thekenfront-Solid (Referenz-Layer)
- Formate: `.3dm` (Rhino) und/oder `.step`

### Stückliste
- Export als `.csv`

> Abwicklung: Nicht erforderlich – spätere Erweiterung möglich.

---

## Komponentenübersicht (GH-Cluster)

| Cluster | Funktion |
|---|---|
| `GH_TH_01_Orient` | Eingabe & Ausrichtung (Frontfläche nach oben) |
| `GH_TH_02_BBox` | Bounding Box & Blockdimensionen |
| `GH_TH_03_Slice` | Bretteinteilung & Fugenpositionen |
| `GH_TH_03b_Split` | Fuge-Brett-Splitting → 2× BBox, Materialersparnis |
| `GH_TH_04_Depth` | Tiefenstufenraster pro Brett / Bretthälfte |
| `GH_TH_05_Block` | Verleimblock & Containerbox |
| `GH_TH_06_Saug` | Saugelement / Aufspanngeometrie |
| `GH_TH_07_BOM` | Stückliste → GH-Panel + CSV |
| `GH_TH_08_Export` | 3D (.3dm / .step) + CSV Export |

---

## Vollständige Parametertabelle

| Parameter | Modul | Typ | Standard |
|---|---|---|---|
| Frontflächen-Index | 01 | Int Slider | 0 |
| Ausrichtungs-Methode | 01 | Toggle | Auto |
| Brettstärke Mitte | 03 | Num Slider | 30 mm |
| Randbrett unten | 03 | Num Slider | 35 mm |
| Randbrett oben | 03 | Num Slider | 35 mm |
| Fugenanzahl | 03 | Int Slider | 1 |
| Fugenposition (mm) | 03 | Num Input | Mitte auto |
| Fugenbreite (min) | 03 | Num Slider | 4 mm |
| Fuge-Split aktiv | 03b | Boolean | True |
| Tiefenbasis | 04 | Num Slider | 150 mm |
| Tiefenschritt | 04 | Num Slider | 50 mm |
| Puffer vorne | 04 | Num Slider | 5 mm |
| Puffer hinten | 04 | Num Slider | 5 mm |
| Überstand links/rechts | 04 | Num Slider | 100 mm |
| Outline-Tiefe Saugelement | 06 | Num Slider | 5 mm |
| Vereinfachungstoleranz | 06 | Num Slider | 2 mm |
| Saugelement-Breite | 06 | Num Slider | 200 mm |
| Material (Freitext) | 07 | Panel | MDF / Vollholz |

---

## Geklärte Punkte

| Frage | Antwort |
|---|---|
| Eingabe-Geometrie | 1× Abschnitt als Solid (Nutzer teilt vorher auf) |
| Schnittebenen | Waagerecht zur World-XY-Ebene (nach Ausrichtung Modul 1) |
| BOM-Ausgabe | GH-Panel + CSV-Export |
| Fuge → Fräselemente | 1 Fuge = 2 separate Elemente, je eigene minimale BBox → Materialersparnis |

## Offene Punkte

- [ ] Testgeometrie für erste Implementierung bereitstellen
- [ ] Farbschema: Bretter nach Tiefenstufe einfärben (optional, aber hilfreich)
- [ ] Saugelement Outline-Glättung: Algorithmus festlegen (RDP-Simplify oder Bogen-Approximation)
- [ ] Abwicklung: optionale spätere Erweiterung
