# Thekenfront Bauplan

Diese Datei beschreibt die lineare Verdrahtung der Thekenfront-Komponenten in Grasshopper.

## Ziel

Die Pipeline ist strikt linear aufgebaut. Es darf keine Rueckkopplung von TH_05 Block nach TH_03b oder TH_04 geben.

## Reihenfolge

1. `TH_01 Orient`
2. `TH_02 BBox`
3. `TH_03 Slice`
4. `TH_03b Fuge Split`
5. `TH_04 Depth`
6. `TH_05 Block`
7. `TH_06 Saug`
8. `TH_07 BOM`
9. `TH_08 Export`

## Verdrahtung

### 1. TH_01 Orient

Eingaben:
- `Solid`: geschlossenes Brep des Thekenabschnitts
- `Frontflaechen-Index`: optional manuell
- `Ausrichtung Auto`: `True` fuer automatische Frontflaechenerkennung

Ausgaenge:
- `Orientiertes Solid` geht an `TH_02 BBox`
- `Orientiertes Solid` geht an `TH_04 Depth`
- `Orientiertes Solid` geht an `TH_05 Block`

### 2. TH_02 BBox

Eingaben:
- `Orientiertes Solid` aus `TH_01 Orient`

Ausgaenge:
- `Hoehe Z` geht typischerweise an `TH_03 Slice -> Gesamthoehe`
- `Breite X`, `Tiefe Y`, `Bounding Box` dienen der Kontrolle

### 3. TH_03 Slice

Eingaben:
- `Gesamthoehe`: am besten `Hoehe Z` aus `TH_02 BBox`
- restliche Parameter per Slider

Ausgaenge:
- `Bretter` geht an `TH_03b Fuge Split`
- `Fugenmitten`, `Fugenbreite`, `Info` nur zur Kontrolle

### 4. TH_03b Fuge Split

Eingaben:
- `Bretter` aus `TH_03 Slice`
- `Split aktiv`: `True`

Ausgaenge:
- `Split Bretter` geht an `TH_04 Depth`
- `Split Bretter` geht an `TH_05 Block`
- `Split Bretter` geht an `TH_07 BOM`

### 5. TH_04 Depth

Eingaben:
- `Orientiertes Solid` aus `TH_01 Orient`
- `Split Bretter` aus `TH_03b Fuge Split`
- Tiefenparameter per Slider

Ausgaenge:
- `Tiefen` geht an `TH_05 Block`
- `Tiefen` geht an `TH_07 BOM`
- `Brettlaenge` geht an `TH_05 Block`
- `Brettlaenge` geht an `TH_07 BOM`

### 6. TH_05 Block

Eingaben:
- `Orientiertes Solid` aus `TH_01 Orient`
- `Split Bretter` aus `TH_03b Fuge Split`
- `Tiefen` aus `TH_04 Depth`
- `Brettlaenge` aus `TH_04 Depth`

Ausgaenge:
- `Bretter` geht an `TH_06 Saug`
- `Bretter` geht an `TH_08 Export`
- `Containerbox` geht an `TH_08 Export`
- `Referenz-Solid` geht an `TH_08 Export`

### 7. TH_06 Saug

Eingaben:
- `Bretter` aus `TH_05 Block`
- `Einstecktiefe` per Slider (Voreinstellung 5 mm)
- `Anschlag-Staerke` per Slider (Voreinstellung 30 mm)
- `Basis-Hoehe` per Slider (Voreinstellung 20 mm)
- `Basis-Ueberstand` per Slider (Voreinstellung 50 mm)

Ausgaenge:
- `Anschlag links` geht an `TH_07 BOM`
- `Anschlag rechts` geht an `TH_07 BOM`
- `Basis links` geht an `TH_07 BOM`
- `Basis rechts` geht an `TH_07 BOM`
- `Gefraeste Bretter` geht an `TH_08 Export` (statt der Rohbretter aus TH_05!)
- Konturen nur zur Kontrolle

### 8. TH_07 BOM

Eingaben:
- `Split Bretter` aus `TH_03b Fuge Split`
- `Tiefen` aus `TH_04 Depth`
- `Brettlaenge` aus `TH_04 Depth`
- `Anschlag links` und `Anschlag rechts` aus `TH_06 Saug`
- `Basis links` und `Basis rechts` aus `TH_06 Saug`

Ausgaenge:
- `CSV Header` geht an `TH_08 Export`
- `CSV Lines` geht an `TH_08 Export`
- `Panel` an ein GH-Panel

### 9. TH_08 Export

Eingaben:
- `Bretter` aus `TH_06 Saug` (`Gefraeste Bretter`) – NICHT aus TH_05!
- `Containerbox` aus `TH_05 Block`
- `Referenz-Solid` aus `TH_05 Block`
- `CSV Header` aus `TH_07 BOM`
- `CSV Zeilen` aus `TH_07 BOM`

## Wichtiger Hinweis

Falsch ist diese Verbindung:

- `TH_05 Block -> Bretter` nach `TH_03b Fuge Split`

Das erzeugt die von dir gezeigte Schleifenlogik im Aufbau.

Richtig ist:

- `TH_03 Slice -> TH_03b Fuge Split -> TH_04 Depth -> TH_05 Block`

Erst `TH_05 Block` erzeugt die echten Brett-Breps. Davor wird nur mit Brettdefinitionen und Tiefenlisten gearbeitet.