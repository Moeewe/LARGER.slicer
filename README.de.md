<div align="center">

<a href="https://parametricsolutions.de/de/larger-slicer/"><img alt="LARGER.slicer: eine durchgehende Linie für großformatigen 3D-Druck" src="docs/social-preview.png" width="100%"></a>

# LARGER.slicer

**Modell rein. Eine Linie. Drucken.**<br>
Der Slicer für großformatigen 3D-Druck mit Roboter und Granulatextruder, in Rhino und Grasshopper.

[![Neueste Version](https://img.shields.io/github/v/release/Moeewe/LARGER.slicer?label=version&color=0a84ff)](https://github.com/Moeewe/LARGER.slicer/releases/latest) [![Rhino](https://img.shields.io/badge/Rhino-9%20%7C%208%20%7C%207-14203a)](https://www.rhino3d.com/de/) [![Plattform](https://img.shields.io/badge/plattform-Windows%20%7C%20macOS-14203a)](#schnellstart) [![Lizenz: CC BY-NC 4.0](https://img.shields.io/badge/lizenz-CC%20BY--NC%204.0-lightgrey)](https://creativecommons.org/licenses/by-nc/4.0/deed.de)

<a href="https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip"><img alt="LARGERslicer-SETUP.zip herunterladen" src="https://img.shields.io/badge/Download-LARGERslicer--SETUP.zip-0a84ff?style=for-the-badge&logo=rhinoceros&logoColor=white"></a>

[Produktseite](https://parametricsolutions.de/de/larger-slicer/) · [Versionshinweise](https://github.com/Moeewe/LARGER.slicer/releases/latest) · [Dokumentation](LARGERslicer/documentations/README.md) · [Changelog](LARGERslicer/CHANGELOG.md)

[English](README.md) · **Deutsch**

</div>

---

## Eine Linie, vom Skirt bis zur letzten Lage

Ein Granulatextruder lässt sich nicht sauber stoppen: Jede Leerfahrt zieht Fäden, jeder Halt hinterlässt einen Tropfen. LARGER.slicer plant deshalb den ganzen Druck als **eine durchgehende Bahn** und prüft sie, bevor sie den Roboter erreicht.

| | |
|---|---|
| **Eine Linie** | Wände, Boden und Infill werden zu einer einzigen Bahn verbunden. Das Infill ist in die Wand eingebunden, nicht daneben gedruckt. |
| **Kreuzt sich nie** | Bei einer Kreuzung fährt die Düse durch frisch gelegtes Material. Lässt sich ein Muster nicht ohne Kreuzung drucken, wählt der Slicer ein anderes. |
| **Flache Lagen** | Jede Lage liegt flach. Die Naht bleibt um eine Bahnbreite offen; die Bahn überquert die Lücke auf einer kurzen Steigung zur nächsten Lage, immer an derselben Stelle. |
| **Aufgelegt** | Eine Bahn darf die darunter um höchstens eine halbe Bahnbreite überragen. `Path Check` meldet, wo ein Teil das überschreitet. |

## Infill aus einer Linie

Jede Zeichnung ist eine echte Lage aus dem Slicer (Bauteil 300 × 190 mm, Bahn 5 mm). Größe, Streckung, Winkel und Lage sind einstellbar.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme-infill-dark.png">
  <img alt="Zehn Infill-Muster als durchgehende Bahnen: Stege, treffende Stege, Waben, gestreckte Waben, Gitter, Welle, Fachwerk, Mäander, Kammerwand, Fachwerkwand" src="docs/readme-infill-light.png" width="100%">
</picture>

**Infill selbst zeichnen:** Kurven in der Draufsicht über das Bauteil zeichnen, und `Infill Sketch` macht aus jeder einen Steg, der ihr von Wand zu Wand folgt, als Teil derselben Linie.

## Böden ohne Absetzen

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme-bottom-dark.png">
  <img alt="Vier Bodenfüllungen als durchgehende Bahnen: Zickzack, Zickzack 45 Grad, Kontur, Kontur um ein Loch" src="docs/readme-bottom-light.png" width="100%">
</picture>

## Der Tab „Slicer“

Neu hier? `Easy Slicer` ist ein einzelner Baustein vom Modell bis zur geprüften Druckbahn; die Seite [Erste Schritte](https://parametricsolutions.de/larger-slicer/erste-schritte/) führt hindurch. Für alle Optionen werden die Komponenten in dieser Reihenfolge verbunden. `Quick Start` legt die ganze Kette samt Beispielteilen auf die Leinwand.

Die Einstellungen sitzen auf den Komponenten selbst: Die Zeilen unter einer Komponente sind Auswahlen, Zahlen, Schalter und Knöpfe (`Save G-code`, `Save KRL`, `Play`). Ein Klick auf die Zeile ändert sie: Eine Auswahl öffnet ein Menü. Eine Zahl zieht man nach links oder rechts (mit Shift feiner; der Balken zeigt, wo sie in ihrem Bereich liegt), klickt sie an, um einen beliebigen Wert einzutippen, oder wählt über den Pfeil rechts einen runden Wert (Winkel 0, 15, 30, 45, 60, 75, 90°). Ein Knopf schreibt seine Datei einmal pro Klick und springt von selbst zurück. Der Wert geht in den Eingang, als wäre er dort eingetippt, die Datei behält ihn also. Ein Draht in denselben Eingang hat weiterhin Vorrang (die Zeile zeigt dann „connected“).

**Lange Berechnungen laufen im Hintergrund.** `Slice`, `Continuous Path`, `Surface Pattern`, `Material Field`, `Self Support Check`, `Non-Planar Check` und `Robot Program` rechnen neben der Leinwand, die bedienbar bleibt; die Komponente zeigt „computing 3.2 s“ und gibt ihr Ergebnis aus, sobald es fertig ist. Ändert sich ein Eingang währenddessen, beginnt sie neu. Die Zeile `Background: on` (oder der Menüpunkt „Calculate in the background“) schaltet das pro Komponente ab; die Datei behält die Einstellung.

**Geschwindigkeiten in mm/s.** `Print Settings` und `Easy Slicer` nehmen standardmäßig Speed 50, Min Speed 5 und Max Speed 200 mm/s. Die Bahn und die Maschinendateien bleiben in mm/min, dem F von G-Code und DXR: 50 mm/s = F3000. Definitionen, die mit einer älteren Version gespeichert wurden, werden als mm/min gelesen, mit einem Hinweis an der Komponente.

Der Tab hat sieben Gruppen, in der Reihenfolge, in der du verbindest (das Ausklappmenü des Slicer-Panels trennt sie durch Linien):

| Gruppe | Komponente | Aufgabe |
|---|---|---|
| **1 Start** | `Quick Start` · `Easy Slicer` | Die ganze Kette mit Beispielteilen; oder ein Baustein vom Modell bis zur geprüften Druckbahn |
| | `Print Settings` | Bahnbreite, Lagenhöhe, Überlappung, Geschwindigkeit (mm/s), kleinster Radius. `Nozzle` wählt Bahnbreite und Lagenhöhe passend zur eingeschraubten Düse |
| | `Machine Profile` | Wie dein Drucker die Extrusionswerte erwartet: Volumen, Filamentlänge oder Faktor für die Steuerung. Aufgelistet als Hersteller und Maschine, z. B. „Generic · Pellet extruder“ |
| | `Material` · `Material Factor` | Startwerte für ein Granulat (Temperaturen, Trocknung, Dichte); ein neuer Extrusionsfaktor aus gemessenen Bahnbreiten |
| | `Design Rules` · `Print Bed` | Vor dem Modellieren: Bereich der Lagenhöhe, Überlappung, Wandstärke für zwei Bahnen; das Druckbett deiner Maschine und ob die Bahnen darauf liegen |
| **2 Lagen** | `Slice` | Breps, Flächen, SubDs, Meshes. Flach, gekippt, non-planar (Führungsfläche, „Follow the part“ oder „Follow the surface“) oder multiaxial (Führungskurve) |
| | `Best Angle` · `Self Support Check` · `Self Supporting Envelope` | Der Schnittwinkel mit dem kleinsten Überhang, wo das Bauteil zu weit überhängt, und das wenigste Material, das es selbsttragend macht |
| **3 Wände und Füllung** | `Walls` | Bahn innen, mittig oder außen an der Geometrie |
| | `Bottom Fill` | Geschlossener Boden als eine Linie: Zickzack oder Kontur |
| | `Infill Ribs` · `Infill Sketch` · `Infill Cells` · `Infill Wave` · `Infill Chambers` | Infill, in die Wandlinie eingebunden |
| | `Support Fins` | Dünne Stützrippen außen am Bauteil, die Überhänge tragen, als Teil derselben Linie |
| **4 Bahn** | `Continuous Path` | Flache Lagen mit offener Naht (Lücke von einer Bahnbreite) oder Spirale für einfache Wände. Seam Mode, Min Layer Time, Short Lap Time, Max Volume Rate |
| | `Round Corners` | Jede Ecke wird ein Bogen |
| | `Start and End` | Anfahrt von 30 mm oberhalb, Skirt zuerst, Brim außen, innen oder beidseitig, am Ende vom Bauteil wegfahren |
| | `Surface Pattern` | Texturen in der Wand: Strick, Pixel, Rippen, Wellen, Zickzack, Punkte, hängende Schlaufen oder ein Bild, am ganzen Bauteil oder in einer gezeichneten Region |
| **5 Prüfen** | `Path Check` | Auflage, Selbstkreuzung, Bahnform, Leerfahrten, Lagenzeiten |
| | `Non-Planar Check` | Bahnhöhe und Freiraum des Druckkopfs bei non-planaren Lagen mit senkrechter Düse |
| | `Extrusion and Speed` | Ebene, E und F pro Punkt für DXR Generator oder G-Code |
| | `Material Field` | Bringt die Bahn als richtungsabhängiges Materialfeld zurück aufs Bauteil, für FE oder IGA |
| **6 Ausgabe** | `Machine` · `Machine G-code` | Eingebaute G-Code-Maschinen (z. B. „Ginger Additive · Ginger One (Klipper)“) oder eigene: Start-, Lagen- und Endcode und E-Faktor als `.machine`-Datei in Dokumente/LARGER/Machines |
| | `Ginger G-code` | Der Pfad in Originalgröße als G-Code für den Ginger One, mit dessen Start- und End-Sequenz |
| | `Desktop Test Print` | Derselbe Pfad verkleinert als G-Code für einen Filamentdrucker |
| | `Printer Simulation` | Der Druckkopf fährt die Bahn auf der Maschine ab, mit Schieberegler oder Play |
| | `Toolpath to Robot Lines` | PTP-Zeilen für den DXR Generator (Weber DXR) |
| | `Import G-code` | Liest G-Code anderer Slicer oder eine .dxr-Datei als Bahn ein, zur Prüfung mit `Path Check` |
| **7 Kurvenwerkzeuge** | `Layers to Curves` · `Curves to Layers` · `Curve Fill Zigzag` · `Curve Fill Contour` · `Curve Fill Cells` · `Curve Fill Euler` · `Curve Fill Cross Lines` · `Curve Continuous Infill` · `Curve Wall and Zigzag` · `Curve Continuous Path` · `Curve Join and Order` · `Curve Bridge` · `Curve Uncross` | Für eigene Kurven statt eines Modells: Lagen als Kurven und zurück, eine Umrandung als eine Linie gefüllt, Kurven zu einer Bahn ohne Kreuzungen verbunden. `Align Curves`, `Alternate Curves` und `Join Open Contours` gehören auch dazu, sind im Panel aber ausgeblendet: über die Suche finden (Doppelklick auf die Leinwand) |

`Quick Start` legt sieben Beispiele an: 01 flache Lagen, 02 non-planar, 03 multiaxial, 04 ein Teil mit Öffnung, 05 schräge Lagen, 06 `Easy Slicer` (eine Vase als Spirale) und 07 ein non-planarer Druckkopf am Ginger One (Klipper), mit Lagen, die dem Bauteil folgen.

### Neue Komponenten im Detail

<details>
<summary><b>Material, Material Factor, Import G-code, Material Field</b></summary>

**`Material`** (Slicer). Startwerte für ein Granulat: Einzugszone, Heizzonen 1 bis 3, Düse und Bett in °C, Trocknung, Restfeuchte und Dichte in g/cm³. Eingebaut sind Carbon-P PETG, rPETG und PLA mit den Startwerten der Weber-DXR-Kurzanleitung; die Dichten sind typische Werte.
- Eingänge: `Material` (Auswahl auf der Komponente), optionale Werte, die das gewählte Material überschreiben (Zonen als ein Wert oder Bereich wie 200-210), `Factor` und der Knopf `Save material`.
- Verbindung: `Density` → `Density` von `Print Settings`, damit Dateinamen und Berichte das richtige Gewicht zeigen.
- Eigene Materialien: `.material`-Textdateien in Dokumente/LARGER/Materials, oder die Eingänge ausfüllen und `Save material` drücken.
- Achtung: Die eingebauten Temperaturen sind Startwerte. Mit dem Datenblatt deines Materials abgleichen.

**`Material Factor`** (Slicer). Ermittelt einen neuen Extrusionsfaktor aus einem Testdruck: neu = Sollbreite / gemessene Breite × aktueller Faktor.
- Eingänge: `Target Width` (mm, Standard 5), `Measured Widths` (mm, mehrere Werte: der Mittelwert zählt), `Current Factor` (Standard 1), optional `Wall Thickness` (mm) eines einwandigen Tests.
- Ausgänge: `New Factor`, `Off` (% der Sollbreite, + = zu breit), `Mean`, `Report`.
- Verbindung: `New Factor` als `Factor` in `Machine Profile` verwenden, oder als P1 am Weber DXR, und den Test erneut drucken.
- Achtung: an mehreren Stellen messen. Der Bericht warnt bei ungleichmäßigen Messwerten.

**`Import G-code`** (Slicer). Liest G-Code aus Cura, Simplify3D, PrusaSlicer, AiSync / Ai Build oder einem Skript, oder eine Weber-`.dxr`, und prüft ihn wie eine eigene Bahn: Starts und Stopps, Leerfahrten, Rückzüge, F-Wechsel und F0, Extrusion ins Bett, Bahnen ohne Auflage, Kreuzungen, Zeit und Gewicht. Schräge Lagen und A/B/C-Werkzeugachsen werden erkannt.
- Eingänge: `File`, `Bead Width` und `Layer Height` (mm, 0 = aus der Datei geschätzt), optional `Machine Profile` (was E bedeutet; leer = E ist ein Volumen in mm³), `Density` (g/cm³, Standard 1,25) und der Schalter `Keep start code`.
- Ausgänge: `Toolpath`, `Path`, `Layers`, `Report`.
- Verbindung: `Toolpath` → `Path Check`, `Robot Program` oder die DXR-Komponenten.
- Achtung: Ist E eine Filamentlänge oder enthält einen Faktor, das passende `Machine Profile` anschließen, sonst stimmen die Bahnbreiten nicht.

**`Material Field`** (Slicer). Bringt die Bahn als richtungsabhängiges Materialfeld zurück aufs Bauteil, für FE oder IGA. Pro Messpunkt: Bahnrichtung e1, quer zur Bahn e2, Stapelrichtung e3, Lage und Bahn, Abstand zur nächsten Lagen- und Bahngrenze, Lagenzeit und ein Schwächewert. Mit den Moduln ergibt sich die gedrehte orthotrope Steifigkeit pro Punkt.
- Eingänge: `Toolpath`, optional `Geometry` (Breps: die Punkte bilden ein Raster im UV jeder Fläche, sodass Fläche, u, v auf die NURBS der Analyse passen) oder `Sample Points` (z. B. Gauß-Punkte), `Sample Spacing` (mm, 0 = halbe Lagenhöhe), Moduln E1, E2, E3, G12, G13 (MPa) und Querkontraktionszahlen, `Cooling Time` (s, Standard 60), `File`, `Format` (CSV, JSON, VTK für ParaView) und der Knopf `Save`.
- Ausgänge: `Points`, `Frames`, `Directions`, `Stacking`, `Weakness` (0 = intakt, 1 = schwach), `Weakness Mesh`, `Table`, `Info`.
- Achtung: Die voreingestellten Moduln und die Abkühlzeit sind Platzhalter. Durch Werte aus Probekörperversuchen mit deinem Material ersetzen.

</details>

<details>
<summary><b>Self Support Check, Self Supporting Envelope, Best Angle, Support Fins</b></summary>

Die Grenze für Überhänge ergibt sich aus der Bahn: Eine Bahn darf um `Factor` × Bahnbreite neben der Bahn darunter liegen (Standard 0,5). Bei 5-mm-Bahnen in 2-mm-Lagen darf eine Wand etwa 51° gegen die Aufbaurichtung geneigt sein.

**`Self Support Check`** (Slicer, neben `Slice`). Färbt das Bauteil danach, wie weit es für die gewählte Aufbaurichtung überhängt: grün gut, orange nahe der Grenze, rot nicht selbsttragend, grau auf dem Bett.
- Eingänge: `Geometry`, `Settings` (aus `Print Settings`), `Plane`, `Angle` (Grad), optional `Guide` und `Guide Mode`, alles wie an `Slice`, und `Factor`.
- Ausgänge: `Analysis Mesh`, `Unsupported Area` (mm²), `Percent`, `Regions`, `Starts` (Inseln, die frei in der Luft beginnen), `Report`.
- Achtung: dieselbe Ebene, denselben Winkel und dieselbe Führung wie an `Slice` verwenden, sonst wird eine andere Aufbaurichtung geprüft.

**`Self Supporting Envelope`** (Slicer). Fügt das wenigste Material hinzu, mit dem jede Lage die darüber trägt, wie automatische Fasen unter Überhängen.
- Eingänge: `Layers` aus `Slice` (vor `Walls`), `Settings`, `Factor`, Schalter `Geometry` (das neue Bauteil auch als Geometrie bauen).
- Ausgänge: `Layers` (fertig zum Weiterverarbeiten: an `Walls` anschließen), `Geometry` (das neue Bauteil als geschlossener NURBS-Brep oder Mesh), `Added` (nur das hinzugefügte Material), `Contours`, `Added Contours`, `Report`.
- Achtung: Das hinzugefügte Material verändert das Bauteil. Abfräsen einplanen; das Bauen der Geometrie (ein Loft) dauert einen Moment.

**`Best Angle`** (Slicer). Probiert Schnittwinkel in 5°-Schritten und mit `Turn` jede Kipprichtung in 30°-Schritten, und schlägt die mit dem kleinsten Überhang vor.
- Eingänge: `Geometry`, `Settings`, `Min Angle` und `Max Angle` (Grad, Standard 0 bis 75), `Turn`, `Factor`.
- Ausgänge: `Plane` und `Angle` (beide an `Slice` anschließen), `Layer Plane`, `Report` mit der Rangfolge.
- Achtung: Ein schräger Druck braucht einen Roboter oder ein gekipptes Bett und meist Start- und Endlagen (`Slice`, `Start/End Layers`).

**`Support Fins`** (Slicer, neben dem Infill). Dünne Rippen außen am Bauteil tragen Überhänge. Jede Rippe steht senkrecht zur Außenwand, vom Bett bis knapp unter den Überhang, als Haarnadel aus der Wand hinaus und zurück, und bleibt so Teil der einen Linie.
- Eingänge: `Layers` (nach `Walls`, `Bottom Fill` und Infill), `Settings`, `Overhang Angle` (Grad gegen die Senkrechte, 0 = eine halbe Bahn pro Lage), `Spacing` (Bahnbreiten, Standard 15), `Reach` (Bahnbreiten, Standard 1), `Contact` (Neck, Neck + gap, Full), `Gap` (mm, Standard 0,3, für Neck + gap).
- Verbindung: direkt vor `Continuous Path`. Die Nahtrampe hält sich von den Rippen fern.
- Achtung: „Full“ ist die festeste Verbindung, aber am schwersten zu entfernen. „Neck + gap“ druckt die oberste Rippenlage tiefer, sodass der Überhang abbricht.

</details>

<details>
<summary><b>Surface Pattern: Strick, Rippen, Wellen, Schlaufen, Bilder</b></summary>

**`Surface Pattern`** (Slicer, Gruppe Toolpaths, nach `Continuous Path`, vor `Start and End`; arbeitet an jedem Toolpath). Jede Lage ist eine Reihe von Punkten. Die Punkte werden im Rhythmus entlang der Wandnormalen nach außen gezogen, und der Rhythmus wandert von Lage zu Lage weiter. Kleine Auslenkungen ergeben feinen Strick oder Pixel; große lassen die Bahn zwischen den Punkten, die auf der Lage darunter liegen, überhängen, und mit `Z Droop` und `Extra Flow` hängt sie zu Schlaufen durch.
- Eingänge: `Toolpath`, `Pattern` (Knit, Ribs, Waves, Zigzag, Dots, Loops, Image), `Amplitude` (mm, wie weit nach außen), `Pitch` (mm, eine Wiederholung entlang der Lage; so angepasst, dass eine ganze Zahl von Wiederholungen um eine geschlossene Wand passt), `Layer Period` (Lagen pro Reihe), `Phase Shift` (Anteil des Pitch pro Reihe: 0 übereinander, 0.5 verwoben, 0.05 diagonal), `Z Droop` (mm), `Extra Flow` (Anteil), `Side` (außen oder innen), `Region` (geschlossene Kurven auf dem Bauteil, Breps oder Meshes) und `Falloff` (mm, weicher Rand), `Attractors` (Punkte oder Kurven) und `Reach` (mm), `Image` (Dateipfad eines Graustufenbilds, dunkel = Muster; ein bloßer Dateiname wird neben der .gh-Datei gesucht) und `Invert`.
- Ausgänge: `Toolpath`, `Preview` (eine Polylinie pro Lage), `Hanging` (Punkte mehr als eine halbe Bahn neben der Lage darunter), `Info`.
- Regionen: Eine geschlossene Kurve wird mit der Wand abgewickelt (Bogenlänge quer, Höhe nach oben); ein Bahnpunkt bekommt das Muster, wenn er dort innerhalb der Kurve liegt. Ein geschlossener Brep oder ein Mesh: Punkte darin. Eine offene Fläche auf der Wand: Punkte darauf.
- Die Bahn bleibt eine Linie: Nur Wandpunkte bewegen sich, Nahtrampen, Brücken, Rippen und Infill bleiben, das Muster läuft an der Naht aus, und wo eine Auslenkung die Bahn kreuzen würde, wird sie kleiner. Die erste Lage bleibt glatt.
- Achtung: Bis zur halben Bahnbreite liegt jede Bahn auf der darunter. Mehr ist absichtlich erlaubt (Pixel, Schlaufen); `Info` zählt diese Punkte und `Path Check` listet sie als nicht gestützt, also mit dem Auge beurteilen. Die Extrusion folgt der längeren Bahn von selbst; `Extra Flow` gibt an den Spitzen mehr dazu, und `Max Volume Rate` in `Continuous Path` begrenzt den Extruder weiterhin.
- Beispiel: `EXAMPLE FILES/LARGERslicer - Surface Patterns.gh` (sechs Vasen: Strick, Rippen, Wellen, Schlaufen, ein Gitterbild, eine Region).

| Wirkung | Pattern | Amplitude | Pitch | Phase Shift | Layer Period | Z Droop / Extra Flow |
|---|---|---|---|---|---|---|
| Feiner Strick, Pixel | Knit | 0.4 bis 0.6 x Bahn | 2 x Bahn | 0 | 1 | 0 / 0 |
| Senkrechte Rippen | Ribs | 0.5 bis 1 x Bahn | 3 x Bahn | 0 | 1 | 0 / 0 |
| Tiefe Sinuswände (übereinander) | Waves | 2 bis 4 x Bahn | 6 bis 10 x Bahn | 0 | 1 | 0 / 0 |
| Verwobene Wellen, Lagen versetzt | Waves | 1.5 bis 2 x Bahn | 6 x Bahn | 0.5 | 1 | 0 / 0 |
| Diagonale Bänder | Waves oder Knit | 1 x Bahn | 3 bis 6 x Bahn | 0.03 bis 0.08 | 1 | 0 / 0 |
| Schuppen, Noppen | Dots | 0.8 x Bahn | 3 x Bahn | 0 | 2 | 0 / 0 |
| Hängende Schlaufen | Loops | 2 bis 4 x Bahn | 4 bis 5 x Bahn | 0.5 | 2 | 1 Lage / 0.3 |
| Bild oder Gitter | Image + Bild | 0.6 x Bahn | 1.5 x Bahn | 0 | 1 | 0 / 0 |
| Textur nur in einem Feld | beliebig + Region-Kurve | | | | | |

</details>

<details>
<summary><b>Slice „Follow the part“ und „Follow the surface“, Non-Planar Check, Optionen von Continuous Path und Path Check</b></summary>

**`Slice`, Guide Mode „Follow the part (no guide)“.** `Guide` leer lassen. Die non-planaren Lagen folgen der Wand des Bauteils von unten nach oben, sodass Überhänge gleich dicke Lagen bekommen, ohne Führungsfläche. Die Düse bleibt senkrecht, und die Extrusion folgt dem senkrechten Abstand zur Lage darunter. Eine Warnung erscheint, wenn eine Lage entlang der Wand dicker als 0,8 × Bahnbreite wird. Passt das Bauteil nicht, meldet `Slice` das und schneidet flach.

**`Slice`, Guide Mode „Follow the surface (no guide)“.** `Guide` leer lassen und `Continuous Path` im Modus Spiral verwenden. Für geschlossene Teile wie einen Kopf, so wie der Kopf „UNIKAT HEAD V2“ 2024 auf dem Ginger One gedruckt wurde: Ringe auf der Oberfläche des Teils vom Ring, auf dem es steht, bis zum Scheitel (der Weg entlang der Oberfläche, an der Wand zu einem Drittel zur Höhe hin gezogen), auch unter Kinn und Nase, und oben geschlossen. Die ersten beiden Lagen liegen flach (erste Lagenhöhe, dann eine Lagenhöhe höher). Die Düse bleibt senkrecht. Die Bahnhöhe jedes Punktes folgt dem Gesetz dieses Drucks, gemessen am Ring darunter: der senkrechte Abstand, wo er darunter liegt, weniger, wo er seitlich liegt (t = dz + (h - dz) x dxy / w), und E folgt ihr. Path Check und Non-Planar Check lassen die angelehnten Ringe unter dem Kinn und am Scheitel so weit zu, wie der Druck von 2024 gezeigt hat, dass es geht (bis eine Bahnbreite neben dem Ring darunter an 2,5 % der Punkte; Bahnhöhen bis 1,1 x Bahnbreite an 0,5 % und unter 0,4 x Lagenhöhe an 1 %; Streifen der Düse bis 0,6 mm), und schreiben das in ihren Bericht. Beispiel: [`LARGERslicer - Head (Ginger One, non-planar).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh>).

**`Non-Planar Check`** (Slicer, neben `Path Check`). Für Drucker mit senkrechter Düse. Misst an jedem Punkt die echte Bahnhöhe gegen Bahnbreite, Lagenhöhe und Extrusion und prüft den Druckkopf gegen alles, was vorher gedruckt wurde.
- Eingänge: `Toolpath`, `Tip Radius` (mm, Standard 4), `Cone Angle` (halber Kegelwinkel gegen die Senkrechte, Grad, Standard 30), `Cone Length` (mm, Standard 60), `Head Radius` (mm, Standard 40), `Pose` (0 = Anfang, 1 = Ende).
- Ausgänge: `OK`, `Report`, `Bead Height` (mm pro Punkt), `Bad Heights`, `Collisions` (muss leer sein), `Head` (der geprüfte Kopf, auf der Bahn bei `Pose`).
- Achtung: die eigene Düse und den eigenen Kopf ausmessen. Die Standardwerte sind nur Beispiele.

**`Continuous Path`, neue Eingänge** (Lagenmodus):
- `Seam Mode`: Fixed point (wie bisher), Inner corner (in einer Innenecke der Außenwand, wo sie am wenigsten auffällt; ohne Innenecke die schärfste Außenecke) oder Sharpest corner. Die Ecke wird von Lage zu Lage verfolgt, höchstens eine halbe Bahn zur Seite.
- `Min Layer Time` (s, 0 = aus): Lagen, die schneller fertig wären, werden langsamer gedruckt, mit einer Geschwindigkeit pro Lage, nie unter Min Speed. Etwa 60 s passen zu vielen großen Granulatdrucken.
- `Short Lap Time` (s, 0 = Min Layer Time): die Mindestzeit für kurze Runden, kürzer als min + 0,7464 x (max - min) aller Rundenlängen, mit einem Übergang über 5 Runden, wie beim 2024 gedruckten Kopf (Min Layer Time 35 s, Short Lap Time 40 s).
- Ecken und gerade Kanten bleiben, wo die Wand ist; die Nahtrampe läuft entlang der Wand und schneidet keine Ecke ab. Skirt- und Brim-Ringe enden eine Bahnbreite vor ihrem Anfang und gehen rechtwinklig in den nächsten Ring über, der innerste läuft in den Anfang des Teils.
- `Max Volume Rate` (mm³/s, 0 = aus): Bewegungen, die mehr vom Extruder verlangen würden, laufen langsamer. Der Ginger One schafft etwa 330 mm³/s.

**`Path Check`**: Eingang `Min Layer Time` (s, 0 = der Wert von `Continuous Path`) und Ausgang `Layer Times` (s pro Lage). Für zu kurze Lagen nennt der Bericht drei Abhilfen: langsamer drucken, mehrere Teile nebeneinander, oder als letzte Möglichkeit eine Pause.

</details>

<details>
<summary><b>Maschinen und Düsen-Voreinstellungen</b></summary>

- Maschinen und Maschinenprofile sind als Hersteller und Maschine aufgelistet: „Ginger Additive · Ginger One (Klipper)“, „Weber Additive · DXR25 on a robot“, „Generic · Filament printer 1.75 mm“. Eigene `.machine`-Dateien können `manufacturer = ...` enthalten.
- **Ginger Additive · Ginger One (Klipper)**: Makros START_PRINT und END_PRINT, E als Volumen × 1,0, Lüfter ab Lage 1. Den Faktor mit der Klipper-`rotation_distance` deines Extruders bestätigen.
- `.machine`-Dateien kennen `material` und `bed temperature` für `{material}` und `{bed}` in den Codes, und Blöcke `[after layer n]`. `Machine` hat die optionalen Eingänge `Material` und `Bed Temperature` (°C).
- Die Düsen-Voreinstellungen an `Print Settings` und `Easy Slicer` heißen nach der Düse, z. B. „3 mm flat nozzle, wide bead - bead 5 mm, layer 2 mm (tested)“ oder „4 mm nozzle (Weber profile) - bead 4.8 mm, layer 1.8 mm“. Eine Warnung erscheint, wenn Bahn und Lage nicht zur Düse passen.

</details>

## Der Tab „Robot“

Für das Drucken mit dem Roboter: Die Zelle wird einmal beschrieben, danach wird jede Bahn zu Roboterbewegungen mit der echten Werkzeugorientierung.

| Baustein | Was er macht |
|---|---|
| `Robot` | Der Arm. Voreingestellt ist der KUKA KR120 R2700-2; jeder Sechsachser mit Zentralhand lässt sich aus einer Bibliotheksdatei des Robots-Plug-ins (.xml) laden |
| `Robot Tool` | Werkzeugspitze (TCP) wie im Robots-Plug-in (AE20-Extruder: YZ-Ebene bei -460, 3, 108), Gewicht, Schwerpunkt, 3D-Modell |
| `Robot Cell` | Roboter, Werkzeug und die Basis, von der aus das Programm zählt (KUKA `$BASE`). Optionale Eingänge `Environment` (Meshes für die Kollisionsprüfung) und `External Axes` |
| `External Axis` | Eine Linearachse, auf der der Roboter steht, oder ein Drehtisch, auf dem das Bauteil steht. An `External Axes` von `Robot Cell` anschließen |
| `Robot Program` | Werkzeugspitze auf jedem Bahnpunkt, Werkzeug entlang der Werkzeugachse der Bahn. Prüft Reichweite, Achsbereiche, Handachsen-Singularität, Achsgeschwindigkeit und Kollisionen. Gibt die Bewegungen für den `DXR Generator`, ein KRL-Programm und den Arm an jeder Stelle der Bahn aus |
| `Robot Simulation` | Spielt das Programm ab: der Roboter mit Werkzeug zu jedem Zeitpunkt des Drucks (Schieberegler oder Play). Ausgang `Colliding` zeigt rot, was in diesem Moment kollidiert |
| `Cell Geometry` | Eine Beispiel-Druckzelle in 3D (Boden, Sockel, Bett, Zaun; die Zelle der FH Münster), der Standort des Roboters und die Umgebung für die Kollisionsprüfung. Eigene Zelle als .3dm-Datei |

<details>
<summary><b>Externe Achsen und Kollisionsprüfung</b></summary>

**`External Axis`** (Robot). Eine Linearachse (der Roboter fährt darauf) oder ein Drehtisch (das Bauteil dreht sich darauf). `Robot Program` bewegt sie während des Drucks: Die Linearachse hält den Roboter neben dem Punkt, der gerade gedruckt wird, der Tisch dreht diesen Punkt zum Roboter. Beide bewegen sich ruhig, unter ihrer Höchstgeschwindigkeit und innerhalb ihres Bereichs.
- Eingänge: `Kind` (Linearachse oder Drehtisch), `Place` (Linearachse: fährt entlang ihrer X-Achse; Tisch: Ursprung = Mitte, Z = Drehachse, X = 0°), `Min` und `Max` (mm bei der Linearachse, Grad beim Tisch; beide 0 = Linearachse 0 bis 3000 mm, Tisch -360 bis 360°), `Max Speed` (mm/s bzw. °/s; 0 = 1000 mm/s bzw. 90 °/s), `Name` (KUKA E1 bis E6), `Endless`, `Offset` (mm bzw. Grad), `Smoothing` (mm Bahn, Standard 300), `Coupled` (KUKA-gekoppelte Basis), `Size` (mm, nur Darstellung).
- Verbindung: `External Axis` → `External Axes` von `Robot Cell` (eine Linearachse und ein Tisch pro Zelle).
- Achtung: KRL schreibt E1/E2 in jede Zeile, die DXR-Ausgabe enthält aber keine E-Werte. Noch nicht an einer Steuerung erprobt: zuerst in T1 laufen lassen.

**Kollisionsprüfung in `Robot Program`.** Roboter, Werkzeug, Linearachse und Drehtisch werden gegeneinander geprüft, gegen das bisher gedruckte Bauteil, das Bett und das `Environment` von `Robot Cell`.
- Eingänge: `Check Collisions` (standardmäßig an; auch als Zeile auf der Komponente), `Clearance` (mm, Standard 10: Körper, die sich näher kommen, werden als Beinahe-Kollision gemeldet), `Collision Poses` (wie viele Stellungen entlang der Bahn, Standard 500; -1 = jede Bewegung, 0 = keine Prüfung).
- Ausgänge: `Collision Moves` (Nummern der Bewegungen, wie `Move` von `Robot Simulation`), `Collision Bodies` (die Körper bei ihrer schlimmsten Kollision, mit einer Kugel an der Berührstelle), `Collisions` (Bahnpunkte).
- Der Bericht nennt das Paar, die Bewegung und die Lage und schlägt vor, was sich ändern lässt: Neigung, Düsenlänge, Werkzeugdrehung, Versatz von Linearachse oder Drehtisch.
- Achtung: Die Prüfung braucht das 3D-Modell des Roboters (siehe Info von `Robot`); ohne es werden Kollisionen nicht geprüft. Ein Werkzeug ohne Mesh wird als einfacher Zylinder vom Flansch bis zur Spitze geprüft, also den eigenen Extruder als `Mesh` an `Robot Tool` anschließen.

</details>

Die kurvenbasierten Werkzeuge des früheren Tabs „Toolpaths“ sind jetzt die letzte Gruppe des Tabs „Slicer“ (siehe oben) und heißen wie ihre Gegenstücke im Slicer (`Curve Fill Zigzag`, `Curve Fill Contour`, `Curve Fill Cells`, `Curve Continuous Path`, `Curve Uncross` ...).

## Maschinen

Dateinamen enthalten Maschine, Datum und Uhrzeit, Bauteil, Gewicht und Druckzeit: `DXR_2611051216_Test Object_2668g_53min.dxr`. Ein senkrechter Extruder wird als A 0, B 0, C 0 geschrieben, wie es der Weber DXR erwartet.

| | |
|---|---|
| **Im Einsatz erprobt** | Weber Additive DXR25 (KUKA-Roboter, DXR-Code) · Ginger Additive Ginger One (G-Code) |
| **Testdrucke im kleinen Maßstab** | Bambu Lab, Prusa, Creality, Elegoo und andere Marlin- oder Klipper-Drucker, über `Desktop Test Print` |
| **Andere Granulat- und Robotersysteme** | Nicht getestet. Das allgemeine Granulat-Profil gibt Position, Werkzeugachse, E und F pro Punkt aus; Format der Steuerung und Extrusionsfaktor müssen an der Maschine abgeglichen werden. |

### Eigene Maschine oder Druckzelle

Kein Code nötig. Alles ist eine Textdatei oder eine Rhino-Datei in `Dokumente/LARGER`:

| Was | Wo | Wie |
|---|---|---|
| G-Code-Maschine | `Machines/Mein Drucker.machine` | Start-, Lagen- und Endcode, Bettgröße und E-Faktor als Text (siehe `Machine`). Am einfachsten: in `Machine` eintragen und `Save` setzen. |
| 3D-Modell, animiert | `Machines/Mein Drucker.3dm` | In Rhino in den Koordinaten der Maschine zeichnen (ihr 0, 0, 0 im Ursprung), eine Ebene pro Achse: `Frame`, `Bed`, `Z`, `Y`, `X` und ein Punkt auf `TCP` an der Düsenspitze. `Printer Simulation` bewegt es dann entlang des Drucks und prüft Kopf, Brücke und Rahmen auf Kollision. `kind = gantry / bedslinger / delta / belt 45 / robot` sagt, was die Maschine drucken kann. Ohne Modell wird das Bett aus der Bettgröße gezeichnet. |
| Granulat | `Materials/Mein Material.material` | Temperaturen, Trocknung, Dichte und Faktor als Text (siehe `Material`). Am einfachsten: die Eingänge von `Material` ausfüllen und `Save material` drücken. |
| Roboter-Druckzelle | `Cells/Meine Zelle.3dm` | In Millimetern in den Koordinaten des Betts zeichnen, ein Teil pro Ebene: `Bed` (gezeichnet, nicht auf Kollision geprüft), `Robot Base` (ein Punkt, wo der Roboter steht, und eine Linie von dort entlang seiner X-Achse), `Work Base` (optional) und jede weitere Ebene (Floor, Fence, Table ...) für die Kollisionsprüfung. `Export` an `Cell Geometry` liefert die mitgelieferte Zelle in dieser Form als Start. |
| Roboterarm | `Dokumente/RobotSystems` | Die Bibliothek des Robots-Plug-ins; `Robot` liest Arm und 3D-Modell von dort (`Geometry File`). |
| Werkzeug | `Robot Tool` | TCP-Ebene, Gewicht und Schwerpunkt wie in Robots; den eigenen Extruder als Mesh an `Mesh` anschließen. |

Neue Dateien erscheinen in den Listen von `Machine`, `Material` und `Cell Geometry`, sobald die Komponente das nächste Mal platziert wird. Den Ordner unverändert weitergeben: Die `.3dm` neben einer `.machine`-Datei wird über ihren Namen gefunden.

Schritt für Schritt, mit vollständiger Beispieldatei, der Ebenen-Konvention des Modells, Bed-Slingern, Deltas und Roboterarmen (englisch): **[docs/OWN_MACHINES.md](docs/OWN_MACHINES.md)**. Eine Vorlage zum Kopieren: `EXAMPLE FILES/Machines/My Gantry.machine` + `My Gantry.3dm`.

Alle genannten Marken gehören ihren jeweiligen Inhabern. LARGER.slicer ist ein unabhängiges Projekt und steht in keiner Verbindung zu diesen Herstellern.

## Außerdem im Paket

- **Eine Grasshopper-Definition, drei Arbeitsabläufe** – *Easy Slicer* schneidet Flächen automatisch, *Path Creator (multiaxial)* macht aus vorbereiteten Werkzeugbahnen Roboterbahnen, *Custom Paths* exportiert eigene Bahnen als Robotercode.
- **Robotercode für den Weber DXR25 (KUKA)** – DXR-Generator und G-Code → DXR-Postprozessor mit automatischem Header (Laufzeit, Schichten, Extrusionssummen) und Zeilennummerierung.
- **Variable Bahnbreiten** – `DXR Variable Width` skaliert die Extrusion entlang X, Y oder Z, mit farbiger Breitenvorschau.
- **Prüfung vor dem Druck** – `DXR File Health Check` prüft Dateiname, Lesbarkeit und Inhalt (GO / NO GO).
- **CNC-Werkzeugbahnen** – Zickzack-Bahnen (Boustrophedon) mit HPGL/PLT-Ausgabe für die Zünd G3.
- **Einrichtung per Doppelklick** – der Installer richtet alle Plugins und die KUKA-KR120-Roboterbibliothek unter Windows und macOS ein.
- **Maschinenanleitungen** – Bedien- und Fehlerbehebungsanleitungen für jede unterstützte Maschine (Deutsch und Englisch).

## Download

📥 **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** – immer die neueste Version.
Enthält die Grasshopper-Definition, den Installer für Windows und macOS und die KUKA-KR120-Roboterbibliothek.

🧩 **Nur das Plugin:** im Rhino Package Manager (Befehl `PackageManager` bzw. `PaketManager`) nach **LARGERslicer** suchen.

## Schnellstart

**Voraussetzungen:** Rhino 8 (empfohlen) oder Rhino 7 · Windows 11 oder macOS 11+ · Internetverbindung während der Einrichtung

1. **[LARGERslicer-SETUP.zip](https://github.com/Moeewe/LARGER.slicer/releases/latest/download/LARGERslicer-SETUP.zip)** herunterladen und entpacken.
2. Rhino schließen und den Installer im Ordner `SETUP` starten:

   | Windows | macOS |
   |---|---|
   | Doppelklick auf `Install Windows.bat` | Rechtsklick auf `Install Mac.command` → **Öffnen** → **Öffnen** |

3. Rhino starten, eine Datei in **Millimetern** anlegen, Grasshopper öffnen und `LARGERslicer Weber Robot and Ginger.gh` aus dem Ordner `SETUP` öffnen.

Der Installer installiert **Robots**, **LARGERslicer** und **Pufferfish** über den Rhino Package Manager und kopiert die KUKA-KR120-Roboterbibliothek (`KR120_R2700_2`) in den Ordner, den das Robots-Plugin liest. Er kann jederzeit erneut ausgeführt werden und aktualisiert dann nur, was veraltet ist.

<details>
<summary><b>Details zum Installer und Fehlerbehebung</b></summary>

- **Vorhandene Installationen:** aktuelle Plugins bleiben unverändert, ältere werden aktualisiert, neuere (z. B. Beta-Versionen) bleiben erhalten.
- **Alte manuelle Installationen:** `.gha`-Dateien, die von Hand in den Grasshopper-Libraries-Ordner kopiert wurden, werden erkannt und können in einen Backup-Ordner verschoben werden (sonst werden Komponenten doppelt geladen).
- **Roboterbibliothek:** eine abweichende vorhandene Datei wird als `*.bak-<Datum>` gesichert, bevor sie ersetzt wird.
- **macOS blockiert den Installer** („nicht verifizierter Entwickler“): Rechtsklick auf `Install Mac.command` → **Öffnen**.
- **Robotersystem `KR120_R2700_2` fehlt:** Installer erneut ausführen oder in der Komponente *Load Robot System* auf **Libraries** klicken und die Online-Bibliothek **FH Muenster MSA** herunterladen.
- **Rhino 7:** erhält Robots 1.9 (Robots 2.x benötigt Rhino 8). Die Definition ist mit Rhino 8 getestet.
- **Plugin-Version festlegen:** im Installer oben in der Liste `PACKAGES` die Version hinter den Paketnamen schreiben, z. B. `"Robots 2.4.4"`.

Mehr: [SETUP-Anleitung](SETUP/README.de.md)

</details>

## Unterstützte Maschinen

| Maschine | Art | Grasshopper-Definition | Anleitung |
|---|---|---|---|
| **Weber DXR25** | KUKA-Roboter, multiaxial | `LARGERslicer Weber Robot and Ginger.gh` | [Deutsch](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md>) · [English](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md>) |
| **Ginger One** | Großformat-Granulatdrucker | `LARGERslicer Weber Robot and Ginger.gh` | [Schnellstart (Englisch)](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md>) |
| **Ginger One (Marlin), non-planarer Kopf** | Granulatdrucker, senkrechte Düse | [`LARGERslicer - Head (Ginger One, non-planar).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Head (Ginger One, non-planar).gh>): der Kopf von 2024 so platziert wie gedruckt, „Follow the surface“, eine Spirale, Brim, `Path Check`, `Non-Planar Check`, G-Code | Der Vergleich mit dem G-Code von 2024 steht auf dem Canvas |
| **Ginger One (Klipper), non-planar** | Granulatdrucker, senkrechte Düse | [`LARGERslicer - Non Planar Head (Ginger).gh`](<EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/LARGERslicer - Non Planar Head (Ginger).gh>): Lagen, die dem Bauteil folgen, eine Spirale, `Path Check`, `Non-Planar Check`, G-Code | Wie Quick-Start-Beispiel 07 |
| **Universal Robots UR5** | Kollaborativer Roboter | `UR5slicer.gh` | [Schnellstart (Englisch)](<EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/README UR5 00 QUICK START GUIDE.md>) |
| **Zünd G3 L-2500** | CNC-Fräsen / Schneiden | `ZündHGPL PLT MSA.gh` | [Schnellstart (Englisch)](<EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md>) |

## Bedienung

`LARGERslicer Weber Robot and Ginger.gh` enthält drei Arbeitsabläufe – den benötigten in eine neue Grasshopper-Datei kopieren:

1. **Easy Slicer** – schneidet Flächen und erzeugt die Werkzeugbahn automatisch.
2. **Path Creator (multiaxial)** – wandelt eine vorbereitete Werkzeugbahn in multiaxiale Roboterbahnen um.
3. **Custom Paths** – nutzt nur die Umwandlungs- und Export-Komponenten, um aus eigenen Bahnen DXR-Robotercode zu erzeugen.

Passwortgeschützte Cluster: Rechtsklick auf die Komponente und **Supersizedprinting** eingeben.

<details>
<summary><b>Teil 1: 3D Print Path Maker</b></summary>

1. **Eingangsgeometrie:** Rechtsklick auf **Set One Surface** → Fläche auswählen.
2. **Skirt / Brim:** in den Zeilen unter `Start and End` einstellen (Klick auf die Zeile zeigt die Auswahl).
3. **Parameter:**
   - **Corner Fillet Radius (mm)** – rundet Ecken ab
   - **Min/Max Extrusion Width (mm)** – Standard- und adaptive Breite
   - **Layer Height (mm)** – Schichthöhe
   - **Point Spacing (mm)** – Punktabstand (Standard 2–5 mm)

**Ergebnis:** Werkzeugbahnen entlang der Mittellinie jeder Schicht.

</details>

<details>
<summary><b>Teil 2: G-Code- / DXR-Erstellung</b></summary>

1. **Vorschau:** mit dem **Preview Slider (0 → 1)** durch die Werkzeugbahn fahren.
2. **Druckerprofil:** **Ginger One** oder **Weber DXR** wählen (UR5: `UR5slicer.gh` verwenden).
3. **Prüfung:** die Datei mit **DXR File Health Check** prüfen und nur bei **GO** weitermachen.
4. **Speicherort:** den integrierten Desktop-Pfad oder einen eigenen Dateipfad verwenden.
5. **Einstellungen:** Auftragsname, Vorschub / Geschwindigkeit, Flow (%).

**Export in zwei Schritten:**

1. **1. Slice Data** klicken – berechnet Gewicht, Zeit und Koordinaten.
2. **2. Export G-Code** klicken – schreibt die Datei.

Ohne *Slice Data* wird nur der Standard-Start-/Endcode geschrieben – ohne Koordinaten, Gewicht und Zeit.

**Ausgaben:** Gewicht (kg), Druckzeit, maschinenfertiger Code.

</details>

## Dokumentation

| Thema | Link |
|---|---|
| Übersicht aller Anleitungen | [Dokumentationsindex](LARGERslicer/documentations/README.md) |
| Maschine bedienen | [Maschinenanleitungen](LARGERslicer/documentations/README.md#machine-guides) |
| KUKA-/DXR25-Roboterfehler | [Deutsch](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md) · [English](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md) |
| Plugin-Komponenten | [Komponentenübersicht](LARGERslicer/documentations/COMPONENTS_OVERVIEW.md) · [Ein-/Ausgänge](LARGERslicer/documentations/COMPONENT_INPUTS_OUTPUTS.md) |
| Neuerungen | [Changelog](LARGERslicer/CHANGELOG.md) |

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| Plugin-Komponenten fehlen oder sind rot | Installer erneut ausführen, danach Rhino neu starten |
| Robotersystem `KR120_R2700_2` fehlt | Installer erneut ausführen oder über **Libraries** in *Load Robot System* die Online-Bibliothek **FH Muenster MSA** laden |
| Fläche wird nicht angenommen | **Set One Surface** mit einer gültigen Brep-Fläche verwenden |
| G-Code-Datei erscheint nicht | **Export G-Code** klicken und Speicherpfad prüfen |
| DXR File Health Check meldet **NO GO** | Dateiname / Pfad korrigieren, erneut exportieren und prüfen, bis **GO** erscheint |
| Vorschau-Slider ohne Funktion | Verbindung am Eingang **Path Preview** prüfen |
| DXR25: Roboter nicht justiert / externe Achse E1 (KSS01347, KSS00106, KSS15047) | [Roboter nicht justiert (E1)](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-nicht-justiert-e1) – ein Programm-Reset allein hilft nicht |
| DXR25: Bremstest / Startbewegung beginnt nicht | [Bremstest / Startbewegung](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) |
| DXR25: Notstopp nahe Sicherheitszaun | [Notstopp am Sicherheitszaun](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck) |
| DXR25: Roboter bewegt sich nicht / Referenzfahrt startet nicht | [Roboter reagiert nicht](LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) |

> [!TIP]
> **Multiaxiale Drucke mit 45°:** den Extruder zur Wand bzw. zur größten freien Fläche neigen, nicht zur Sicherheitstür, und zusätzlichen Abstand zum Zaun halten – die Bremszone beginnt etwa 10–15 cm vor der eigentlichen Absperrung.

## Entwicklung

**Entwicklerdokumentation** ([Überblick auf Deutsch](docs/README.de.md), Seiten auf Englisch): [Architektur](docs/ARCHITECTURE.md) · [Druckregeln](docs/PRINTING_PRINCIPLES.md) · [Icons](docs/ICONS.md) · [Tests](docs/TESTING.md) · [Beispiele](docs/EXAMPLES.md) · [Entscheidungen](docs/DECISIONS.md)

<details>
<summary><b>Aufbau des Repositories</b></summary>

```
LARGER.slicer/
├── SETUP/                     # Hier starten: Installer, Weber/Ginger-Definition, Roboterbibliothek
├── LARGERslicer/              # Grasshopper-Plugin (C#)
│   ├── Components/            # CNC-, Export- und Utility-Komponenten
│   ├── Resources/             # Komponenten-Icons
│   ├── documentations/        # Anleitungen und Referenzen
│   └── build-package.sh/.bat  # Baut die Yak-Pakete für Rhino 7, 8 und 9
└── EXAMPLE FILES/             # Maschinenspezifische Definitionen und Anleitungen
    ├── 00 - UNIVERSAL ROBOTS - UR5/
    ├── 00 - WEBER - GINGER [Robotic] 3D Printing/
    └── 00 - ZUENDT - 3D CNC MILLING/
```

</details>

**Plugin bauen:**

```bash
cd LARGERslicer
./build-package.sh          # unter Windows build-package.bat; optional neue Version, z. B. 1.1.14
```

Erzeugt `dist/largerslicer-<version>-rh9_0-any.yak`, `dist/largerslicer-<version>-rh8_0-any.yak` und `dist/largerslicer-<version>-rh7_0-any.yak`. Mehr: [Plugin-README](LARGERslicer/README.md).

## Lizenz

© 2025 Moritz Wesseler, FH Münster · Lizenziert unter [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/deed.de) – nur für nicht-kommerzielle Nutzung.

Mitwirkende: Fabio Koczula, Claudio Schröder · Ursprünglich inspiriert von Ginger.Additive, vollständig neu umgesetzt und um weitere Maschinen erweitert.

## Sicherheit und Haftung

Mit LARGER.slicer erzeugte Roboter- und Maschinenprogramme sind ungeprüft. Vor jedem Lauf im T1- oder Testbetrieb mit reduzierter Geschwindigkeit prüfen; Kollisionen, Achsgrenzen, Singularitäten und Temperaturen liegen in der Verantwortung des Betreibers. Betrieb nur durch unterwiesene Personen nach der Betriebsanweisung der Zelle und den Vorgaben der Hersteller (KUKA, Weber u. a.). Keine Gewährleistung, soweit gesetzlich zulässig. Fremdlizenzen, Quellen und Marken: siehe [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
