---
title: Inbetriebnahme & Bedienanleitung – 3D-Druck-Robotersystem DXR25 (Weber Additive)
subtitle: Fachbereich Architektur – FH Münster / MSA Robotics Lab
version: 1.1
last_updated: 2026-08-04
---

# Inbetriebnahme & Bedienanleitung – 3D-Druck-Robotersystem DXR25 (Weber Additive)
Fachbereich Architektur – FH Münster / MSA Robotics Lab

Diese Anleitung fasst die Einweisung, Transkripte und Meeting-Notizen zusammen. Sie dient als verbindliche Referenz für alle eingewiesenen Nutzerinnen und Nutzer des DXR25.

## Inhaltsverzeichnis
1. Zweck und Geltungsbereich
2. Sicherheit & Vorbereitung
   - Allgemeines, Lüftung & Türpolicy
   - Persönliche Schutzausrüstung (PSA)
   - Temperatur-/Verbrennungsgefahr
   - Not-Aus
3. Aufbau & Komponentenüberblick
4. Vorbereitungen vor dem Start (Schnellcheck)
5. Einschalten der Anlage (Reihenfolge)
6. Software-Start & Grundkonfiguration
7. Material & Temperaturprofile
8. Extruderreinigung & Materialwechsel
9. Nozzle & Druckbett
10. G-Code/DXR-Dateien laden
11. Druck starten (inkl. Vorschau, Offsets, Startgeschwindigkeit)
12. Nachbereitung & Abschalten
13. Fehlerbehebung (Troubleshooting)
14. Wartung & regelmäßige Checks
15. Hinweise & offene Punkte
16. Quick-Start-Checkliste DXR25
 17. Credits
 18. Anhang – DXR‑Code‑Beispiel (vollständig)

---

## 1. Zweck und Geltungsbereich
Der DXR25 ist ein industrieller 6-Achs-KUKA-Roboter mit Weber-Pellet-Extruder für großformatigen 3D-Druck. Diese Anleitung beschreibt sichere Inbetriebnahme, Bedienung, typische Fehlerbilder und Pflegemaßnahmen. Betrieb ausschließlich durch eingewiesene Personen.

## 🧱 2. Sicherheit & Vorbereitung

> Notfall-Kurzanleitung (Merkkasten)
> 1) Sofort stoppen: „Druck stoppen“ → wenn Gefahr: Not‑Aus drücken (Display, Zaun oder Smartpad)
> 2) Heizzonen prüfen: Düsen-/Zonentemperaturen im HMI checken; keine heißen Teile berühren
> 3) Bereich sichern: Tür verriegelt lassen, Freifahren erst nach Sichtprüfung
> 4) Ursachenanalyse: Meldungstext lesen, Antriebsbelastung prüfen, Offsets/Programm überprüfen
> 5) Wiederanlauf: Nur nach Fehlerbehebung und erneuter Sicht- und Kollisionskontrolle

### 2.1 Allgemeines, Lüftung & Türpolicy
- Vor jeder Nutzung den Raum lüften (Arbeitsschutzvorgabe, 5–10 Minuten). Während der Anwesenheit darf die Tür offen bleiben; beim Verlassen stets schließen (Sicherheitsbereich!).
- Sicherheitsbereich nicht unbeaufsichtigt offen lassen. Zugang in die Roboterzelle nur für eingewiesene Personen.
- Bei ungewöhnlichen Geräuschen, Geruch, Vibrationen oder Auffälligkeiten Druck sofort stoppen; wenn nötig Not-Aus betätigen.
 - Druck nur mit direkter Sichtlinie zum Roboter starten; Druckbereich freihalten (keine Hindernisse im Arbeitsraum).
 - In Rhino/Grasshopper (Robot‑Plugin) Sicherheitszäune und Kollisionsobjekte passend konfigurieren, besonders bei multiaxialen Jobs.

### 2.2 Persönliche Schutzausrüstung (PSA)
- Brandschutzhandschuhe beim Arbeiten am Extruder/Nozzle verwenden (liegen im Labor bei den Ginger-Druckern).
- Haare zusammenbinden, keine losen Kleidungsstücke oder Schmuck/Metall im Sicherheitsbereich.

### 2.3 Temperatur- und Verbrennungsgefahr
- Heizbett: bis 150 °C. Nozzle/Extruder: bis 250–280 °C.
- Heiße Bereiche niemals berühren. Temperatur ggf. mit Handrücken vorsichtig prüfen (nicht Handinnenfläche!).
- Magnetische Heizbett-Platten bei eingeschaltetem Heizbett aufliegen lassen (Dämpfe vermeiden). Platten ohne Lücken/Überlappungen ausrichten.

### 2.4 Not-Aus-Schalter
- Drei Not-Aus-Positionen: 1) am Display 2) am Sicherheitszaun 3) an der KUKA-Fernbedienung.
- Nur im Notfall drücken (z. B. Kollision, Überhitzung, Fehlbewegung). Danach muss die Anlage neu initialisiert werden.

> Hinweis: Es existiert keine softwareseitige Bodenkollisionssperre. Neue Programme/Offsets mit besonderer Vorsicht starten und Z-Höhe überwachen.

---

## ⚙️ 3. Aufbau & Komponentenüberblick
Sicherheitszaun
### Stellplan der Anlage

![Weber DXR25 Stellplan](../../../LARGERslicer/Documentation/DXR25_Stellplan.svg)

*Abbildung: Stellplan des Weber DXR25 3D-Druck-Robotersystems mit Anschaltsequenz (1→2→3)*

### Komponentenübersicht

| Komponente | Beschreibung |
|---|---|
| KUKA-Industrieroboter | KUKA KR120 (6-Achs-Arm), gesteuert über KUKA-Steuerung (Controller) |
| Weber-Extruder | Extruderschnecke fördert und plastifiziert Pellets; mehrere Heizzonen |
| Überdrucksicherung | Mechanische Sicherung am Extruderkopf; löst bei Stau/zu kaltem Material aus; nach Auslösung alle Schrauben ersetzen |
| Pelletzufuhr & Trockner | Trocknet Material und fördert per Luftdruck in den Extruder; muss während des Drucks laufen |
| Wasserkühlung (Chiller) | Geschlossener Kreislauf, automatisch; Füllstand periodisch prüfen |
| Heizbett | Beheizbar bis 150 °C, mit magnetischen Platten |
| Sicherheitszaun & Türverriegelung | Elektrisch gekoppelt; verriegeln vor Druckstart |
| Schaltschrank | Industrie-PC, Extrudersteuerung, Sicherheits-/Kommunikationsmodule; Hauptschalter für Inbetriebnahme |
| Display-/Bedieneinheit | Touch, Dreh-/Drücksteller, USB-Ports, Bestätigungs-/Abbruchtasten, Not-Aus. Hinweis: Am Touch-Display/Schaltschrank ist aktuell nur der rechte USB-Port nutzbar; der linke ist intern belegt. |
| Kamera | Überwacht Druckprozess (Zugriff via Link, wenn Roboter aktiv) |
| Aktivkohlefilter (optional) | Manuelle Luftreinigung gegen ggf. Gerüche; kann zeitgesteuert laufen |

Zusatzhinweise aus der Einweisung:
- Ersatzschrauben für die Überdrucksicherung liegen im Regal über der Robotersteuerung. Nach Auslösung alle betroffenen Schrauben entsorgen und ersetzen. Anzug: „handfest + ¼ Drehung“ (Richtwert; ggf. Drehmomentschlüssel verwenden).
- Schaltschrank-Schlüssel und Kamera-/Netzwerkanbindung nur in Abstimmung mit Werkstattleitung/Hersteller verändern.

---

## ✅ 4. Vorbereitungen vor dem Start (Schnellcheck)
- Raum gelüftet, Bereich frei, Platten korrekt aufgelegt.
- Kühlung/Chiller optisch prüfen (Füllstand plausibel). Aktivkohlefilter bei Bedarf einschalten.
- Material: Nur PETG/PLA (freigegeben). Trockner einschalten und Füllstand prüfen.
- Sicherheitsbereich schließen: Türmechanik prüfen; Verriegelung erst bei Start aktivieren.
- Sichtprüfung Extruder/Nozzle: keine Beschädigungen, Überdrucksicherung intakt, Düse fest.

---

## 🔌 5. Einschalten der Anlage (Reihenfolge)
1. Lüften & Sichtprüfung abschließen.
2. Robotersteuerung (KUKA-Controller) einschalten.
3. Schaltschrank am Hauptschalter einschalten; Kontrollanzeigen prüfen.
4. Display hochfahren lassen → Windows startet → Weber-Software lädt automatisch.

Login an der Software: Benutzer „Operator“, Passwort „222“.

---

## 🧭 6. Software-Start & Grundkonfiguration
Nach dem Login:
- Benachrichtigungen prüfen und quittieren (rote/gelbe Symbole oben rechts).
- Sicherheit aktivieren: Tür schließen → weißer Knopf blinkt → drücken → System verriegelt.
- Bremstest durchführen: Antriebe → Grundstellung → Bremstest. Bereich freihalten.

KUKA in externen Betriebsmodus schalten (falls erforderlich):
- Zellentür schließen und quittieren, Anlage-Ein-Taster betätigen (leuchtet), Antriebe einschalten.
- Am Smartpad Programm „cnc“ anwählen; beliebigen Zustimmschalter auf Mittelstellung halten.
- Beliebige Start-Taste drücken und halten; SAK-Fahrt beobachten.
- Schlüsselschalter betätigen; Betriebsmodus auf EXT einstellen.
- Bremstest/Referenzfahrt durchführen; Grundstellung anfahren.

---

## 🔥 7. Material & Temperaturprofile
Zugelassene Materialien: PETG, PLA. Andere (z. B. recycelte) nur nach schriftlicher Freigabe.

Heizzonen in der Software:
- Einfüll-/Förderzone (oben, ggf. aktiv gekühlt)
- Prozesszone (mittig)
- Düsenadapter/Nozzle (unten)

Empfehlungen:
- Rezept „PETG 3 mm“ laden (typisch ca. 230 °C, material- und nozzleabhängig).
- Bauteilkühlung/Lüfter zu Beginn deaktivieren (bessere Haftung), später nach Bedarf aktivieren.
- Trockner während des gesamten Drucks laufen lassen. Wenn der Trockner leer läuft, stoppt der Materialfluss; ein Wiederanlauf ist ohne Nachfüllen nicht möglich. Ein Software-Update zur Pausenfunktion ist angekündigt.

Materialfaktor (P1):
- Extrusionsmengen in DXR können mit einem Materialfaktor P1 skaliert werden (z. B. PETG ~3.1, PLA ~3.5 als Ausgangswerte, je nach Rezept/Nozzle). Die Anpassung erfolgt in der Maschinen‑UI, nicht im Grasshopper‑Skript. Änderungen dokumentieren und bei Gelegenheit kalibrieren.

Tuning-/HMI-Überblick (Weber-Software):
- Linke Paneele: Extruder-Heizzonen (Einfüllzone/Prozess/Düsenadapter), Extruder säubern, Schnecke freifahren.
- Rechte Paneele: Drucktisch (Mastertemperatur, Zonen 1–4, Vakuum Soll/Ist), Roboterstatus (Position X/Y/Z, TCP Override 10–200%).
- Untermenü (rechte Bildschirmleiste): Startbildschirm, Prozesseinstellungen, Düsenliste, Druckplattenliste, Formelsammlung, Rezepte, G‑Code, Bildschirmreinigung, Explorer, Trend‑Graphen.
- Hinweis: Das G‑Code/DXR‑Programm kann je nach Systempfad in `C:\\ProgramData\\Weber\\GCode\\` oder `D:\\Data\\Gcode` liegen.

Basis‑Temperaturen (Richtwerte für erste Drucke – je nach Düse/Material feinjustieren):
- Einfüllzone: 35–60 °C (Materialabhängig)
- Heizzone 1/2/3: 160–235 °C (PLA eher niedriger, rPETG/Carbon‑P höher; Herstellerangaben beachten)
- Düsentemperatur: ca. 180–240 °C (Materialabhängig)
- Drucktisch: 60–100 °C (Haftung je nach Unterlage)
- Materialtrocknung: 4–6 h bei ~60–65 °C (Materialabhängig)

Materialfaktor ermitteln (Praxisleitfaden):
1. Einwandige Geometrie drucken; Linienbreite leicht über Düsen‑Ø anlegen (z. B. 1.2×).
2. SOLL‑ vs. IST‑Linienbreite messen/vergleich.
3. Faktor anpassen: neuer Faktor = (SOLLbreite / ISTbreite) × aktueller Faktor.
4. Erneut drucken und prüfen; bei Änderungen von Düse/Temperatur/Kühlung erneut validieren.

Einflüsse und Korrekturen (Kurzreferenz):
- Temperatur zu niedrig: schlechte Haftung, hohes Drehmoment, raue Oberfläche → Temp. erhöhen (untere Zonen erhöhen = höhere Austrittstemp.), Kühlung verringern, ggf. Geschwindigkeit erhöhen.
- Temperatur zu hoch: Nachlaufen/Stringing, Überhitzen, delaminierte Schichten → Temp. senken (untere Zonen senken), Kühlung erhöhen, Schichtdicke verringern.
- Materialfaktor zu niedrig: Lunker, schlechte Haftung → Faktor erhöhen; Dynamik ggf. anpassen.
- Materialfaktor zu hoch: Materialansammlungen, unsaubere Konturen → Faktor verringern; Strategie/Dynamik/Düse prüfen.
- Kühlung zu niedrig: Überhänge fallen ein, Brücken tropfen, Verzug → Kühlung erhöhen, Temp. senken, dünnere Schichten/kleinere Düse/Schichtzeit erhöhen.
- Kühlung zu hoch: schlechte Haftung, Verzug, stumpfe Oberfläche → Kühlung verringern, Temp. erhöhen, Schichtdicke erhöhen.
- Bett/Unterlage: Bei geringer Haftung → Düsenabstand reduzieren, Bett temp. leicht erhöhen, Bett reinigen/entfetten, Haftvermittler; Bei zu hoher Haftung → Abstand erhöhen, Bett temp. senken, alternative Unterlage.
- Düsenform: spitz = präzise Ecken, weniger Anhaftung; flach = breitere Bahn, mehr Anhaftung. Lange Düse: mehr Freiwinkel für Multiaxis; kurze Düse: weniger Wärmeabgabe, mehr Kollisionsgefahr. Große Düse = hoher Ausstoß, gute Haftung aber schlechtere Kontur; kleine Düse = gute Kontur, längere Druckzeit.

---

## ♻️ 8. Extruderreinigung & Materialwechsel
Vor jedem Druck:
1. Extruder vollständig aufheizen (mind. 15 Minuten, bis Soll erreicht).
2. „Extruder säubern“ ausführen.
3. Anzeige „Antriebsbelastung“ beobachten: blau = OK, orange/rot = sofort stoppen (Überdruckgefahr).
4. Materialfluss prüfen: gleichmäßige, plastische Extrusion.

Materialwechsel:
- Roboter in Wartungsposition fahren.
- Restmaterial kontrolliert in einen Eimer ablassen (nicht verschwenden). Schrauben am Extruderkopf nur bei Bedarf lösen.
- Nach Störung/Materialstau: Überdrucksicherung prüfen; nach Auslösung alle Schrauben ersetzen (Ersatz im Regal über der Robotersteuerung).

Reinigung Extruder (aus den Herstellerhinweisen):
- Vorheriges Material vollständig leerfahren.
- Reinigungsgranulat einfüllen und vollständig extrudieren.
- Zuleitungen bis zur Einfüllzone reinigen; Granulatrückstände in der Einfüllzone mit Druckluft über die Materialzuführung entfernen.

Woywod‑Trockner entleeren/reinigen (bei Materialwechsel):
- Behälter unterstellen; Drehschieber/Schieber öffnen und Behälter leeren.
- Förderung einschalten, um Förderschlauch leer zu fördern.
- Restmaterial entfernen: Einfüllöffnung, innen reinigen; Gitter entnehmen/reinigen; Ventil öffnen und mit Druckluft reinigen; Filter entfernen/reinigen.
- Hinweis: Bei faserhaltigen Materialien Mundschutz tragen. Neues Material einfüllen und trocknen.

---

## 🧰 9. Nozzle & Druckbett
Nozzlewechsel:
- Nur bei Betriebstemperatur durchführen (Material weich). 13er-Schlüssel verwenden.
- Alte Nozzle gegen den Uhrzeigersinn lösen; neue Nozzle mit geeigneter Metallpaste einsetzen.
- Anziehen: handfest + ¼ Drehung (Richtwert). Dichtheit nach erstem Aufheizen prüfen.
- Schichtbreite: ca. 1,2–2× Nozzledurchmesser (z. B. Ø 3 mm → 3,5–6 mm).

Zusatz (Herstellerangabe):
- Düse im aufgeheizten Zustand wechseln; Dichtflächen/Gewinde der neuen Düse mit hitzefester Metall‑Gleitpaste versehen.
- Geringes Drehmoment (~15 Nm) genügt; bei spürbarem Widerstand stoppen.

Druckbett:
- Magnetische Platten aufliegen lassen (Geruchs-/Dampfentwicklung vermeiden). Keine offenen Lücken/Überlappungen.
- Typische Bett-Temperatur: 60–90 °C. Nicht berühren; Handrückentest nur vorsichtig.

---

## 💾 10. G-Code/DXR-Dateien laden
1. Weber-Software → „G‑Code‑Tisch“: verfügbare Druckdateien prüfen.
2. Datenübertrag: per USB-Stick oder TeamViewer.
3. Dateipfad: `D:\\Data\\Gcode`.
4. Falls Datei nicht erscheint: Runtime beenden → Explorer öffnen → Netzwerkverbindung „NC“ prüfen. Wenn Symbol rot, anklicken, um Verbindung wiederherzustellen.
5. DXR-Dateien aus Post-Processor in den G‑Code‑Ordner kopieren.

Hinweise zur Dateikonvertierung (DXR):
- Die Maschine verarbeitet DXR-Dateien, nicht direkt G‑Code. Empfohlen ist die automatische Umwandlung via Rhino/Grasshopper-Skript (LargerSlicer.gh bzw. LargerSlicerMultiaxial.gh) mit Roboter‑Plugin.
- Alternative am Schaltschrank: Weber‑Konverter nutzen (Taskleisten‑Icon → schwarzes Fenster). G‑Code per Drag‑and‑Drop ins Fenster ziehen, Enter drücken. Das erzeugte .dxr muss anschließend in den G‑Code‑Ordner kopiert werden, sonst wird es in der Weber‑Software nicht angezeigt.

Remotezugriff:
- TeamViewer ist für kurze Sessions verfügbar (keine Pro‑Lizenz; typischerweise ~5 Minuten). Zugangsdaten sind auf dem Windows‑System (TeamViewer-App/Explorer) einsehbar.
- Windows Remote Desktop kann ggf. verfügbar sein (vor Ort prüfen/freigeben lassen).

Wichtige HMI-/Dateihandling-Hinweise:
- **File Explorer im HMI nur einmal antippen.** Mehrfaches Tippen kann zu Abstürzen führen, weil alle Klicks verzögert abgearbeitet werden.
- **Wenn eine `.dxr` ungewöhnlich lange lädt**, ist die Datei häufig fehlerhaft. Datei in einem Texteditor prüfen (z. B. unvollständig, leer oder beschädigt) und ggf. neu exportieren.
- **Dateinamen kurz und sauber halten.** Zu lange Dateinamen, Sonderzeichen oder zusätzliche Punkte im Dateinamen können den Import stören oder die Anlage abstürzen lassen.
- **Wichtig:** Es darf nur **ein Punkt direkt vor der Dateiendung** stehen. Beispiel problematisch: `examplename_23h12min_1.23kg.dxr` (zusätzlicher Punkt vor `.dxr`).
- **Bei sehr langsamem System keine großen USB-Sticks anschließen.** Die Indizierung kann stark verzögern.
- **Empfohlene Alternative bei langsamen USB-Transfers:**
   1. Runtime über das Menü beenden.
   2. Unten rechts in der Taskleiste das TeamViewer-Icon öffnen.
   3. Verbindungsdaten/Passwort eingeben.
   4. Innerhalb der verfügbaren Session-Zeit (typisch ca. 5 Minuten) Dateien übertragen.

Voraussetzungen für Rhino/Grasshopper‑Workflow:
 - Rhino 8 oder höher mit Robot‑Plugin installiert. Achtung: Läuft nur auf Rhino 8+ wegen Python‑2‑Skripten.
 - **Plugin installieren (zwei Möglichkeiten):**
   
   **Option 1: Rhino Package Manager (empfohlen)**
   1. In Rhino den Befehl `PackageManager` ausführen
   2. Nach "LARGERslicer" suchen
   3. Auf "Install" klicken
   4. Rhino/Grasshopper neu starten
   
   **Option 2: Manuelle Installation**
   - Folgende Dateien in den Grasshopper‑Komponenten‑Ordner kopieren und Rhino/Grasshopper neu starten:
     - LargerSlicer.gha
     - Newtonsoft.Json.dll
     - Optional: LargerSlicer.pdb (Debug‑Symbole)

 - .gh‑Dateien je nach Anwendungsfall herunterladen/öffnen (LargerSlicer.gh bzw. LargerSlicerMultiaxial.gh). Wichtig: Diese Grasshopper‑Dateien benötigen das Plugin (GHA + DLL), sonst laufen sie nicht.

### ⚠️ Erstmalige Einrichtung: Robot Library Installation (KUKA KR120)

**Wichtig:** Wenn Sie das Weber Grasshopper-Skript zum ersten Mal öffnen, **wird kein Roboter angezeigt**. Das liegt daran, dass die KUKA KR120 Roboter-Bibliothek von der FH Münster MSA im Robots Plugin installiert werden muss.

**Die Robot-Komponente erscheint rot/kaputt** – so ist sie leicht zu finden!

**Schritte zur Installation des KUKA KR120:**
1. Lokalisieren Sie die **rote Robot-Komponente** in Ihrer Grasshopper-Arbeitsfläche (sie ist rot/kaputt, weil die Bibliothek fehlt)
2. **Doppelklicken** Sie auf den **Libraries** Button der Robot-Komponente
3. Im Library-Browser-Fenster navigieren Sie zum **FH Münster MSA** Tab
4. **Laden Sie den KUKA KR120** herunter und installieren Sie die Roboter-Bibliothek
5. **Speichern** Sie Ihre Grasshopper-Definition
6. **Schließen** und **öffnen** Sie die Grasshopper-Datei erneut
7. Der KUKA KR120 Roboter sollte nun in der Simulation sichtbar sein und die Komponente wird grün

> **Hinweis:** Die Roboter-Bibliothek muss nur einmal pro Rhino-Installation installiert werden. Nach der ersten Einrichtung ist der KUKA KR120 für alle zukünftigen Weber DXR25 Projekte verfügbar.

**Weitere Einrichtungsschritte:**
 - In den Skripten Sicherheitszäune/Kollisionsobjekte korrekt setzen.

DXR‑Kurzreferenz:
- DXR spiegelt G‑Code‑Zeilen mit N‑Nummern, XYZ, G90/G91 und Orientierungen A/B/C wider.
- Extrusion wird skaliert mit `XE=[Wert*P1]`, P1 ist der Materialfaktor in der Maschinen‑UI.
- Beispiel: `N24 G1 X911.817 Y952.527 Z32.000 A0.000 B0.000 C0.000 G91 XE=[64.430*P1] G90`

Ein vollständiges DXR‑Beispiel befindet sich im Anhang.

HMI/G‑Code-Listen & Pfade:
- Über das rechte HMI‑Menü sind u. a. zugänglich: Rezepte, Düsenliste, Druckunterlagen, Formelsammlung, G‑Code‑Übersicht.
- DXR‑Dateien liegen je nach System in `D:\\Data\\Gcode`.

---

## ▶️ 11. Druck starten (Vorschau, Offsets, Startgeschwindigkeit)
Vor dem Start prüfen:
- Rezept geladen, Material korrekt, Extruder gereinigt.
- Tür verriegelt (weiß → gedrückt), Sicherheit aktiv.
- Heizbett- und Düsentemperatur erreicht.

Ablauf:
1. Jobs werden am Schaltschrank/Display gestartet und überwacht, nicht am KUKA‑Pendant.
2. Wartungsposition → Grundstellung.
3. „Druck starten“ drücken.
4. Startfenster: Bauteilmaße prüfen; XY-/Z‑Verschiebung setzen; Extruderausrichtung (Neigungs-/Drehwinkel) einstellen; „Auto Düsenoffset“ bei Bedarf aktivieren; „Abfahren des Bauraums“ aktivieren (min./max. Grenzen abfahren); „Abstreifen vor Druckstart/Schichtwechsel“ nach Bedarf aktivieren.
5. Vorschau prüfen; Offsets vorsichtig setzen. Z‑Kollisionsrisiko beachten.
6. Start mit reduziertem Override (10–50 %) beginnen; nach Stabilisierung schrittweise erhöhen. Nach Düsen-/Plattenwechsel Abstand Düse–Tisch nachmessen.

Hinweise zu Multi‑Axial Druck:
- Multi‑axiales Drucken nutzt zusätzliche Orientierungen (A/B/C‑Achsen). Bei A=B=C=0 steht der Extruder senkrecht für horizontales Drucken. Orientierungen werden im Multiaxial‑Grasshopper‑Skript über Ebenen vorgegeben.
- Vor Multiaxial‑Jobs mögliche Kollisionen zwischen Extruder und Roboterarm prüfen; ggf. mit erhöhtem Tisch/Fixture arbeiten.
- Extruderneigung möglichst zur Wand bzw. in den größten freien Bereich ausrichten (weg von der Sicherheits-/Eingangstür), besonders bei 45‑Grad‑Jobs.
- Zusätzlichen Abstand zum Zaun einplanen; der Sicherheits-/Bremsbereich beginnt bereits vor der physischen Barriere.

---

## 🧯 12. Nachbereitung & Abschalten
1. Druckprozess beenden und abkühlen lassen; Werkstück erst < 50 °C vom Bett lösen.
2. Heizungen ausschalten; Kühlung/Chiller bis < 50 °C weiterlaufen lassen.
3. Roboter in Grundstellung fahren.
4. System herunterfahren: Weber-Runtime schließen → Windows beenden → Schaltschrank aus → Robotersteuerung aus.
5. Tür entriegeln; Arbeitsplatz aufräumen, Materialreste entsorgen, Boden reinigen.

---

## 🧩 13. Fehlerbehebung (Troubleshooting)

Fuer die gepflegte Master-Version (DE/EN) siehe auch: [KUKA Robot Troubleshooting Guide](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING.md).

| Problem | Mögliche Ursache | Maßnahme |
|---|---|---|
| Keine Extrusion | Extruder nicht heiß genug / noch nicht durchgewärmt | Weiter aufheizen (> 15 min), dann „Extruder säubern“ |
| Antriebsbelastung orange/rot | Überdruck durch kaltes Material/Verstopfung | Sofort stoppen, Temperatur prüfen, Extruder säubern |
| Überdrucksicherung platzt | Materialstau, zu kalt, falsches Material, Nozzle-Kollision | Druck stoppen, Extruderkopf prüfen, alle Sicherungsschrauben ersetzen |
| Nozzle kollidiert mit Bett | Falscher Z-Offset/Programmfehler | Sofort stoppen/Not-Aus, Offsets prüfen, Bett/Nozzle inspizieren |
| Verbindung „NC“ fehlt | Netzwerkpfad getrennt | Explorer öffnen, „NC“ neu verbinden, Datei erneut laden |
| Endloses Laden beim Job-Start | Startreihenfolge/NC‑Verbindung fehlerhaft | In der Anzeige oben rechts: Runtime beenden → Explorer öffnen → „NC‑Laufwerk“ in der Seitenleiste anklicken (Symbol wechselt von rot auf grün) → Job erneut laden |
| Temperaturanzeige rot/Fehler | Heizelement/Sensorfehler | Nicht weiterdrucken; Werkstatt/Hersteller informieren |
| Anlage bläst/kühlt ungewöhnlich laut | Robotikraum zu warm / thermische Last hoch | Robotikraum lüften, Luftaustausch erhöhen, Temperatur stabilisieren |
| Starker Geruch | Platten abgenommen / Filter aus | Platten auflegen; Aktivkohlefilter einschalten; lüften |
| Druck stoppt bei leerem Trockner | Trockner leer; kein Pausenmodus | Material nachfüllen; Druck kann ohne Update nicht fortgesetzt werden |
| Referenzfahrt schlägt fehl, Startpunkt wird nicht gefunden | Referenzschalter verstellt/verbogen oder locker | In Referenzposition prüfen, ob beide Lampen korrekt schalten; Referenzschalter vorsichtig nachjustieren bzw. neu befestigen, bis die Justagefahrt wieder zuverlässig funktioniert |

KUKA‑Controller/Programm hängt (z. B. Singularität):
- Versuche den Roboter zunächst manuell aus der Situation zu fahren (Teach‑In, langsam, Bereich frei!).
- Prüfe Statusanzeigen am KUKA‑Pendant (alle LEDs oben rechts sollten grün sein). Falls nicht, den Bildschirmhinweisen folgen, bis grün.
- CNC/Programm neu laden oder neu starten (nicht löschen). Bei Bedarf zwischen externer/interner Steuerung umschalten (Schlüsselschalter oben rechts) und erneut versuchen.

### Problem: Roboter führt Bremstest / Startbewegung nach dem Start nicht aus

**Symptome:**
- Roboter führt den erforderlichen Bremstest / Die Startbewegung nach dem Start nicht aus
- Roboter kann sich nicht in Grundstellung verfahren
- Antriebe-Knopf blinkt die ganze Zeit
- Fehlermeldung: "Druck abgebrochen bei XYZ jeweils 0"
- Display des Schaltschrankes zeigt an: "Neu Starten" oder "Warnschwelle für Bremstest erreicht mit 0 Stunden Restlaufzeit"
- Entsprechende Anzeigeleuchte leuchtet Rot

**KUKA Smart Fernbedienung / Smartpad zeigt an:**
- "Quitt Fahrtfreigabe gesamt Verursacher KS"
- "Active-Status erforderlich"
- Status-Anzeigen "S" "O" "R" "Ext" zeigen: Grün, Grau, Gelb, Grün (o.ä.)
- **Erwartet:** Alle Anzeigen sollten auf dem Smartpad grün sein!

**Ursache:**
Das CNC Programm des Roboters muss manuell neu angewählt werden.

**Lösung (Nur auf dem Smartpad arbeiten):**

1. **Schlüssel umdrehen:**
   - Schlüssel oben umdrehen von "Remote" auf "Zahnrad"

2. **T1 Modus:**
   - Auf T1 tippen
   - Dann wieder zurück: Schlüssel drehen auf "Remote"

3. **Navigation öffnen:**
   - Links auf das blaue Zahnrad auf dem Touchbildschirm tippen
   - "Öffnen" klicken
   - Links auf das orangene "X" tippen
   - Es öffnet sich die Übersicht mit Dateien

4. **CNC Programm anwählen:**
   - Mehrere Dateien und Ordner sind angezeigt
   - "CNC" anklicken
   - Unten auf dem Touch Display auf "Anwählen" klicken

5. **Programm zurücksetzen:**
   - Oben auf dem Touch Screen des Smartpads beim gelben Quadrat "R" drauftippen
   - Es öffnet sich ein Fenster
   - "Programm zurücksetzen" klicken

6. **Zurück in externen Modus:**
   - Schlüssel umdrehen auf das Zahnrad
   - Externen Modus "EXT" auswählen wie beim Start auswählen
   - Schlüssel wieder umdrehen auf "Fernbedienung"

7. **Verifikation:**
   - Roboter sollte sich jetzt wieder bewegen können
   - Alle Status-Anzeigen auf dem Smartpad sollten grün sein

### Problem: Notstop nahe Sicherheitszaun bei multiaxialem Druck

**Symptome:**
- Roboter stoppt abrupt, sobald Tool/Extruder in zaunnahe Bereiche kommt
- Bremsen greifen hörbar, Roboter wird außer Betrieb gesetzt
- HMI/Schaltschrank zeigt sicherheitsbezogene Stopps oder Bremstest-Hinweise
- Automatikfahrt bleibt blockiert, bis der Roboter manuell aus dem Schutzbereich gefahren wird

**Ursache:**
TCP-/Extruder-Neigung gelangt in multiaxialen Bahnabschnitten (häufig bei 45‑Grad‑Ausrichtungen) in den Sicherheits-/Bremsbereich.

**Wiederanlauf (KRC5 / Smartpad):**

1. **Statusmeldung zuerst lesen:**
   - Stop-Ursache auf HMI/Smartpad oder am Schaltschrank prüfen.

2. **In den Einrichtbetrieb wechseln:**
   - Schlüssel von "Remote" auf "Zahnrad" drehen.
   - Sicherstellen, dass "EXT" anliegt, dann auf "T1" wechseln.
   - Schlüssel zurückdrehen; Smartpad kann neu starten.

3. **Roboter manuell aus Sicherheitsbereich fahren:**
   - Totmannschalter auf der Rückseite gedrückt halten.
   - Achsweise verfahren (oder SpaceMouse, falls freigegeben), bis TCP/Extruder klar außerhalb des Schutzbereichs ist.
   - Verfahr-Icons werden nur mit gedrücktem Totmannschalter grün.

4. **Zurück in den Automatikbetrieb:**
   - Schlüssel auf "Zahnrad", von "T1" zurück auf "EXT".
   - Schlüssel zurück auf "Remote" (Betriebsmodus).

5. **Falls Start weiter blockiert ist:**
   - Navigation öffnen und "cnc" neu anwählen.
   - "Programm zurücksetzen" ausführen und Roboter ggf. neu referenzieren.

### Problem: Roboter reagiert nicht / Start- oder Referenzfahrt beginnt nicht

Diesen Ablauf nutzen, wenn der Roboter nach dem Wiederanlauf weiter nicht reagiert, der Druck bei 0,0,0 abbricht oder die Start-, Referenz- oder Justagefahrt nicht beginnt.

1. Prüfen, dass alle Status-LEDs oben rechts am Smartpad grün sind.
2. Navigation öffnen, "cnc" erneut auswählen und "Anwählen" tippen.
3. Auf das gelbe "R" tippen und "Programm zurücksetzen" ausführen.
4. Unter "PrgView" das EMI zurücksetzen; danach in der oberen Rahmenleiste das Roboterprogramm zurücksetzen und neu starten.
5. Betriebsmodus prüfen und wieder auf EXT gehen; falls nötig kurz zwischen externer/interner Steuerung umschalten.
6. Bremstest, Referenzfahrt oder Startbewegung erneut auslösen.
7. Falls Referenz-/Justagefahrt weiter fehlschlägt, Referenzschalter prüfen und kontrollieren, ob beide Lampen in Referenzposition korrekt schalten.

Nach Not-Aus: Anlage neu initialisieren, Verriegelung prüfen, Bremstest erneut durchführen, Achswege und Offsets verifizieren.

G‑Code/DXR lädt nicht:
- Beim Laden wird Gcode als `Gcode.nc` auf ein mit dem Roboter geteiltes Laufwerk kopiert. Bei Ladeproblemen: Im Explorer Laufwerk `nc (Z:)` öffnen und `Gcode.nc` löschen; Druckprogramm erneut laden.

Probleme nach Programmabbruch oder manuellem Verfahren:
- Beim Wechsel vom Roboterprogramm zur CNC‑Steuerung übernimmt diese die Lageregelung. Stimmen Positionen nicht überein, stoppt der Roboter mit Fehlermeldungen.
- Unter Softkey „PrgView“ das EMI (External Motion Interface) mit „Reset“ zurücksetzen; oben in der Rahmenleiste das Roboterprogramm zurücksetzen und neu starten.

---

## 🧼 14. Wartung & regelmäßige Checks
- Chiller-Füllstand alle paar Wochen/Monate prüfen (geschl. Kreislauf, selten Nachfüllen nötig; Reserve im schwarzen Behälter).
- Überdrucksicherung visuell prüfen; bei Auslösung alle Schrauben ersetzen (Ersatz im Regal über KUKA-Steuerung). Bestände monatlich kontrollieren und ggf. nachbestellen.
- Ordnung im Raum sicherstellen; Materialsäcke entsorgen; Arbeitsfläche sauber halten.
- Materialfaktoren (Durchfluss) für PETG/PLA bei Gelegenheit kalibrieren und dokumentieren.



Kalibrierung/Homing:
- Die aktuelle Installation ist mechanisch fix; regelmäßige Kalibrierung (TCP/Base/Bett) ist im Normalbetrieb nicht erforderlich.
- Nach mechanischen Änderungen (Düse/Adapter getauscht, Extruderkopf neu ausgerichtet, Bett neu positioniert) sind TCP/Base zu prüfen und ggf. neu einzumessen.

---

## 💡 15. Hinweise & offene Punkte

### Kamera – Liveansicht
- Link: http://fb05-dxr25-cam.fh-muenster.de
- Erreichbar im FH‑Netzwerk oder per VPN. Zugriff in der Regel nur, wenn der Roboter aktiv ist.
- Logins:
   - Admin: Benutzer „Admin“, Passwort „#DXR(standardpassworteinfügen)2025“
   - Student: Benutzer „student“, Passwort „DXR(standardpassworteinfügen)“
 - Der Stream wird nicht aufgezeichnet und nicht online gespeichert.

### Weitere Hinweise
- Geplantes Softwareupdate für verbesserten Materialfluss/Pausenfunktion (Herstellerangabe, tbd).
- Hochpreisige Masterbatches nur in Abstimmung mit der Werkstattleitung verwenden.
- Änderungen am Schaltschrank/Verkabelung ausschließlich in Abstimmung mit Hersteller/Werkstatt.

---

# 🧾 16. Quick-Start-Checkliste DXR25

Vor dem Start
- [ ] Raum 5–10 Min. gelüftet; Sicherheitsbereich frei; Platten korrekt aufgelegt
- [ ] Trockner an; Material PETG/PLA vorhanden; Chiller plausibel
- [ ] Robotersteuerung an; Schaltschrank an; Login „Operator/222“
- [ ] Benachrichtigungen quittiert; Sicherheit verriegelbar; Bremstest durchgeführt

Material & Heizung
- [ ] Rezept geladen (z. B. „PETG 3 mm“); Heizzonen auf Soll; 15 Min. durchwärmen
- [ ] Extruder säubern; Antriebsbelastung blau
- [ ] Nozzle fest; Überdrucksicherung intakt; Handschuhe bereit

Druck
- [ ] Datei im „G‑Code‑Tisch“; ggf. „NC“ verbunden; Vorschau/Offsets geprüft
- [ ] Startgeschwindigkeit 10–50 %; Beobachtung zu Beginn
- [ ] Tür verriegelt; Kamera optional aktiv; Aktivkohlefilter bei Bedarf an

Nach dem Druck
- [ ] Abkühlen < 50 °C; Teil entnehmen
- [ ] Heizungen aus; Chiller bis < 50 °C laufen lassen
- [ ] Roboter in Grundstellung; Software/Windows/Schaltschrank/Steuerung aus
- [ ] Tür entriegeln; Arbeitsplatz reinigen; Verbrauchsmaterial prüfen

---

Kontakt/Fragen: Werkstattleitung MSA Robotics Lab. Änderungen/Ergänzungen bitte versioniert in dieser Datei dokumentieren (Version, Datum, Kurznotiz).

## Credits
Moritz Wesseler, FH Münster – MSA Robotics Lab (Zusammenstellung/Erstellung dieser Anleitung und Beispiele)

---

## Anhang – DXR‑Code‑Beispiel (vollständig)

Hinweis: Das folgende Beispiel demonstriert Struktur und Syntax typischer DXR‑Ausgaben. Parameter wie P1 (Materialfaktor) werden in der Maschinen‑UI gesetzt.

<details>
<summary><strong>DXR Code Example (vollständig ein-/ausklappen)</strong></summary>

```dxr
;ProgRunTimeTotal = [6501]
;post_processor_version =[V1.0.3.3]
;machine_type =[DXR.KUKA]
;number of rows in org. Gcode = [38025]
;number of movement rows =[37591]
;Xmin = [891.529]
;Xmax = [1200.391]
;Ymin = [775.523]
;Ymax = [1025.491]
;Zmin = [2.000]
;Zmax = [391.829]
;Eges = IC[1138401.000]
;AiSync = [1]
;config end
;========================================================
G90

;========================================================
N10 V.E.GLOBAL[27] = 0
N11 L layer_sub.nc
N12 L wall_sub.nc
N13 G1 X899.633 Y900.000 Z32.000 A0.000 B0.000 C0.000 F3000.000 
N14 G1 Z2.000 A0.000 B0.000 C0.000 
N15 G1 Y903.167 A0.000 B0.000 C0.000 G91 XE=[38.529*P1] G90 F1800.000 
N16 G1 X899.877 Y908.495 A0.000 B0.000 C0.000 G91 XE=[64.893*P1] G90 
N17 G1 X900.559 Y915.416 A0.000 B0.000 C0.000 G91 XE=[84.590*P1] G90 
N18 G1 X901.359 Y920.690 A0.000 B0.000 C0.000 G91 XE=[64.849*P1] G90 
N19 G1 X902.400 Y925.921 A0.000 B0.000 C0.000 G91 XE=[64.811*P1] G90 
N20 G1 X903.679 Y931.099 A0.000 B0.000 C0.000 G91 XE=[64.763*P1] G90 
N21 G1 X905.697 Y937.754 A0.000 B0.000 C0.000 G91 XE=[84.359*P1] G90 
N22 G1 X907.511 Y942.770 A0.000 B0.000 C0.000 G91 XE=[64.609*P1] G90 
N23 G1 X909.552 Y947.698 A0.000 B0.000 C0.000 G91 XE=[64.526*P1] G90 
N24 G1 X911.817 Y952.527 A0.000 B0.000 C0.000 G91 XE=[64.430*P1] G90 
N25 G1 X915.095 Y958.660 A0.000 B0.000 C0.000 G91 XE=[83.864*P1] G90 
N26 G1 X917.852 Y963.226 A0.000 B0.000 C0.000 G91 XE=[64.170*P1] G90 
N27 G1 X920.816 Y967.661 A0.000 B0.000 C0.000 G91 XE=[64.042*P1] G90 
N28 G1 X923.979 Y971.956 A0.000 B0.000 C0.000 G91 XE=[63.904*P1] G90 
N29 G1 X928.391 Y977.332 A0.000 B0.000 C0.000 G91 XE=[83.123*P1] G90 
N30 G1 X931.985 Y981.272 A0.000 B0.000 C0.000 G91 XE=[63.548*P1] G90 
N31 G1 X935.757 Y985.044 A0.000 B0.000 C0.000 G91 XE=[63.382*P1] G90 
N32 G1 X939.698 Y988.639 A0.000 B0.000 C0.000 G91 XE=[63.204*P1] G90 
N33 G1 X945.073 Y993.050 A0.000 B0.000 C0.000 G91 XE=[82.163*P1] G90 
N34 G1 X949.368 Y996.214 A0.000 B0.000 C0.000 G91 XE=[62.768*P1] G90 
N35 G1 X953.803 Y999.177 A0.000 B0.000 C0.000 G91 XE=[62.566*P1] G90 
N36 G1 X958.369 Y1001.934 A0.000 B0.000 C0.000 G91 XE=[62.357*P1] G90 
N37 G1 X964.502 Y1005.212 A0.000 B0.000 C0.000 G91 XE=[81.020*P1] G90 
N38 G1 X969.331 Y1007.477 A0.000 B0.000 C0.000 G91 XE=[61.855*P1] G90 
N39 G1 X974.259 Y1009.518 A0.000 B0.000 C0.000 G91 XE=[61.627*P1] G90 
N40 G1 X979.275 Y1011.332 A0.000 B0.000 C0.000 G91 XE=[61.395*P1] G90 
N41 G1 X985.930 Y1013.351 A0.000 B0.000 C0.000 G91 XE=[79.737*P1] G90 
N42 G1 X991.108 Y1014.629 A0.000 B0.000 C0.000 G91 XE=[60.844*P1] G90 
N43 G1 X996.340 Y1015.670 A0.000 B0.000 C0.000 G91 XE=[60.602*P1] G90 
N44 G1 X1001.613 Y1016.470 A0.000 B0.000 C0.000 G91 XE=[60.352*P1] G90 
N45 G1 X1008.534 Y1017.152 A0.000 B0.000 C0.000 G91 XE=[78.362*P1] G90 
N46 G1 X1013.862 Y1017.396 A0.000 B0.000 C0.000 G91 XE=[59.775*P1] G90 
N47 G1 X1019.196 A0.000 B0.000 C0.000 G91 XE=[59.525*P1] G90 
N48 G1 X1024.524 Y1017.152 A0.000 B0.000 C0.000 G91 XE=[59.270*P1] G90 
N49 G1 X1031.445 Y1016.470 A0.000 B0.000 C0.000 G91 XE=[76.947*P1] G90 
N50 G1 X1036.719 Y1015.670 A0.000 B0.000 C0.000 G91 XE=[58.688*P1] G90 
```

</details>
