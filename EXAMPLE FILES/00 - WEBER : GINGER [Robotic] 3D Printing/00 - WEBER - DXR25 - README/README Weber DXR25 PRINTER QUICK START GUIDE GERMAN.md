---
title: Inbetriebnahme & Bedienanleitung – 3D-Druck-Robotersystem DXR25 (Weber Additive)
subtitle: Fachbereich Architektur – FH Münster / MSA Robotics Lab
version: 1.3
last_updated: 2026-10-21
---

# Inbetriebnahme & Bedienanleitung – 3D-Druck-Robotersystem DXR25 (Weber Additive)
Fachbereich Architektur – FH Münster / MSA Robotics Lab

Diese Anleitung beschreibt den Druckbetrieb des DXR25 auf Grundlage der dokumentierten Einweisung, Anlagenhinweise und Betriebserfahrungen: Anlage vorbereiten und einschalten, Druckdatei laden, Druck starten und überwachen sowie anschließend sicher abschalten. Sie richtet sich an eingewiesene Nutzerinnen und Nutzer. Betriebsanweisung und Herstellervorgaben bleiben maßgeblich.

Bezeichnungen in Anführungszeichen entsprechen den Anzeigen und Bedienelementen der Anlage. Wo nötig, steht die deutsche Erläuterung direkt dabei.

## Einstieg: Vom Einschalten zum Druck

Für einen regulären Druck die folgenden Schritte der Reihe nach durchgehen. Einrichtung des eigenen Rechners, Materialwechsel und Wartung sind gesondert gekennzeichnet und nicht vor jedem Druck erforderlich. Die Übersicht ersetzt weder die vollständigen Abschnitte noch die Einweisung.

| Schritt | Hier weiterlesen |
|---|---|
| 1. Sicherheit und Anlage vorbereiten | [Sicherheit](#2-sicherheit-und-vorbereitung) und [Vorbereitungen vor dem Start](#4-vorbereitungen-vor-dem-start-schnellcheck) |
| 2. Anlage einschalten | [Einschaltreihenfolge](#5-einschalten-der-anlage-reihenfolge) |
| 3. Bedienoberfläche starten und Anlage betriebsbereit machen | [Software, Sicherheit, Bremstest und Grundstellung](#6-software-start-und-grundkonfiguration) |
| 4. Material, Extruder, Düse und Druckbett vorbereiten | [Material und Temperaturen](#7-material-und-temperaturprofile), [Extruderreinigung](#8-extruderreinigung-und-materialwechsel) und [Düse / Druckbett](#9-düse-und-druckbett) |
| 5. Druckdatei übertragen und laden | [G-Code / DXR laden und Datei prüfen](#10-g-code--und-dxr-dateien-laden) |
| 6. Druck vorbereiten, starten und überwachen | [Vorschau, Offsets und Druckstart](#11-druck-starten-vorschau-offsets-startgeschwindigkeit) |
| 7. Druck beenden und Anlage abschalten | [Nachbereitung und Abschalten](#12-nachbereitung-und-abschalten) |

**Wo wird bedient?** Druckdateien, Material, Temperaturen und Druckaufträge werden an der **Weber-Bedieneinheit (HMI am Schaltschrank)** bedient. Das **KUKA-Smartpad** dient der Robotersteuerung, Betriebsartenwahl und den beschriebenen Wiederanlaufabläufen. Das Roboterprogramm **cnc** am Smartpad ist nicht die zu ladende **DXR-Druckdatei**. Zum Standort der Komponenten siehe [Aufbau und Stellplan](#3-aufbau-und-komponentenüberblick).

Bereits eingewiesen und mit dem Ablauf vertraut? Die [Schnellstart-Checkliste](#16-schnellstart-checkliste-dxr25) hilft beim Abhaken.

## Schnelle Hilfe: Was funktioniert nicht?

Bei Gefahr den [Notfall-Merkkasten](#2-sicherheit-und-vorbereitung) beachten. Vor Reset oder Bewegung die tatsächlichen Meldungen lesen und den passenden Ablauf vollständig prüfen.

| Anzeige / Problem | Direkt zur deutschen Hilfe |
|---|---|
| **Keine Bewegung**, obwohl die Datei geladen ist; Start- oder Referenzfahrt beginnt nicht | [Status und Startfreigabe prüfen](#roboter-bewegt-sich-nicht-status-und-startfreigabe-prüfen) |
| **Bremstest startet nicht**, Antriebe-Knopf blinkt oder „Druck abgebrochen bei XYZ jeweils 0“ | [Bremstest / Startbewegung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) |
| Roboter stoppt **nahe dem Sicherheitszaun** | [Sicherheitsstopp am Zaun](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck) |
| **Datei fehlt oder lädt nicht**, „NC“ nicht verbunden, keine Extrusion oder anderes Druckerproblem | [Fehlerbehebung an Drucker und Extruder](#weitere-fehlerbilder-an-drucker-und-extruder); zur Dateivorbereitung siehe [Dateien laden](#10-g-code--und-dxr-dateien-laden) |
| Seltener Sonderfall: **„Roboter nicht justiert“ / E1**, **KSS01347**, **KSS00106** oder **KSS15047** | [Installationsspezifische E1-Wiederherstellung](#fehlerbehebung-roboter-nicht-justiert-e1), nur bei passenden Justagemeldungen |

[Alle Fehlerbilder und weiteren Hinweise](#13-fehlerbehebung) · [Vollständige deutsche KUKA-Fehlerbehebung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md)

## Inhaltsverzeichnis
1. [Zweck und Geltungsbereich](#1-zweck-und-geltungsbereich)
2. [Sicherheit und Vorbereitung](#2-sicherheit-und-vorbereitung)
3. [Aufbau und Komponentenüberblick](#3-aufbau-und-komponentenüberblick)
4. [Vorbereitungen vor dem Start (Schnellcheck)](#4-vorbereitungen-vor-dem-start-schnellcheck)
5. [Einschalten der Anlage (Reihenfolge)](#5-einschalten-der-anlage-reihenfolge)
6. [Software-Start und Grundkonfiguration](#6-software-start-und-grundkonfiguration)
7. [Material und Temperaturprofile](#7-material-und-temperaturprofile)
8. [Extruderreinigung und Materialwechsel](#8-extruderreinigung-und-materialwechsel)
9. [Düse und Druckbett](#9-düse-und-druckbett)
10. [G-Code- und DXR-Dateien laden](#10-g-code--und-dxr-dateien-laden)
11. [Druck starten (Vorschau, Offsets, Startgeschwindigkeit)](#11-druck-starten-vorschau-offsets-startgeschwindigkeit)
12. [Nachbereitung und Abschalten](#12-nachbereitung-und-abschalten)
13. [Fehlerbehebung](#13-fehlerbehebung)
14. [Wartung und regelmäßige Checks](#14-wartung-und-regelmäßige-checks)
15. [Hinweise und offene Punkte](#15-hinweise-und-offene-punkte)
16. [Schnellstart-Checkliste DXR25](#16-schnellstart-checkliste-dxr25)
17. [Mitwirkende](#mitwirkende)
18. [Anhang – DXR-Code-Beispiel (Auszug)](#appendix-dxr-code-example)

---

## 1. Zweck und Geltungsbereich
Der DXR25 ist ein industrieller 6-Achs-KUKA-Roboter mit Weber-Pellet-Extruder für großformatigen 3D-Druck. Diese Anleitung beschreibt sichere Inbetriebnahme, Bedienung, typische Fehlerbilder und Pflegemaßnahmen. Betrieb ausschließlich durch eingewiesene Personen.

## 2. Sicherheit und Vorbereitung

> Notfall-Kurzanleitung (Merkkasten)
> 1) Sofort stoppen: „Druck stoppen“ → wenn Gefahr: Not‑Aus drücken (Display, Zaun oder Smartpad)
> 2) Heizzonen prüfen: Düsen-/Zonentemperaturen im HMI checken; keine heißen Teile berühren
> 3) Bereich sichern: Tür verriegelt lassen, Freifahren erst nach Sichtprüfung
> 4) Ursachenanalyse: Meldungstext lesen, Antriebsbelastung prüfen, Offsets/Programm überprüfen
> 5) Wiederanlauf: Nur nach Fehlerbehebung und erneuter Sicht- und Kollisionskontrolle

### 2.1 Raumlüftung und Zugang zur Roboterzelle
- Vor jeder Nutzung den Raum lüften (Arbeitsschutzvorgabe, 5–10 Minuten). Die dokumentierte Regel „Tür darf während der Anwesenheit offen bleiben, beim Verlassen schließen“ ist keine Freigabe für einen Betrieb mit offener Schutztür: Vor Automatikbewegungen muss die Zellentür geschlossen und verriegelt sein.
- Sicherheitsbereich nicht unbeaufsichtigt offen lassen. Zugang in die Roboterzelle nur für eingewiesene Personen.
- Bei ungewöhnlichen Geräuschen, Geruch, Vibrationen oder Auffälligkeiten Druck sofort stoppen; wenn nötig Not-Aus betätigen.
- Druck nur mit direkter Sichtlinie zum Roboter starten; Druckbereich freihalten (keine Hindernisse im Arbeitsraum).
- In Rhino/Grasshopper mit dem Robots-Plugin Sicherheitszäune und Kollisionsobjekte passend konfigurieren, besonders bei multiaxialen Druckaufträgen.

### 2.2 Persönliche Schutzausrüstung (PSA)
- Brandschutzhandschuhe beim Arbeiten am Extruder und an der Düse verwenden (liegen im Labor bei den Ginger-Druckern).
- Haare zusammenbinden, keine losen Kleidungsstücke oder Schmuck/Metall im Sicherheitsbereich.

### 2.3 Temperatur- und Verbrennungsgefahr
- Heizbett: bis 150 °C. Düse/Extruder: bis 250–280 °C.
- Heiße Bereiche niemals berühren. Temperaturen an der Bedieneinheit prüfen, nicht durch Berühren mit der Hand.
- Magnetische Heizbett-Platten bei eingeschaltetem Heizbett aufliegen lassen (Dämpfe vermeiden). Platten ohne Lücken/Überlappungen ausrichten.

### 2.4 Not-Aus-Schalter
- Drei Not-Aus-Positionen: 1) am Display 2) am Sicherheitszaun 3) an der KUKA-Fernbedienung.
- Nur im Notfall drücken (z. B. Kollision, Überhitzung, Fehlbewegung). Danach muss die Anlage neu initialisiert werden.

> Hinweis: Es existiert keine softwareseitige Bodenkollisionssperre. Neue Programme/Offsets mit besonderer Vorsicht starten und Z-Höhe überwachen.

---

## 3. Aufbau und Komponentenüberblick
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

## 4. Vorbereitungen vor dem Start (Schnellcheck)
- Raum gelüftet, Bereich frei, Platten korrekt aufgelegt.
- Kühlung/Chiller optisch prüfen (Füllstand plausibel). Aktivkohlefilter bei Bedarf einschalten.
- Material: Nur PETG/PLA (freigegeben). Trockner einschalten und Füllstand prüfen.
- Sicherheitsbereich schließen: Türmechanik prüfen; Verriegelung erst bei Start aktivieren.
- Sichtprüfung von Extruder und Düse: keine Beschädigungen, Überdrucksicherung intakt, Düse fest.

---

## 5. Einschalten der Anlage (Reihenfolge)
1. Lüften & Sichtprüfung abschließen.
2. Robotersteuerung (KUKA-Controller) einschalten.
3. Schaltschrank am Hauptschalter einschalten; Kontrollanzeigen prüfen.
4. Display hochfahren lassen → Windows startet → Weber-Software lädt automatisch.

Anmeldung an der Software: Benutzer „Operator“. Das aktuell freigegebene Passwort bei der Werkstattleitung erfragen; Zugangsdaten nicht in dieser öffentlichen Anleitung hinterlegen.

---

## 6. Software-Start und Grundkonfiguration
### 6.1 Anlage am Weber-Display betriebsbereit machen

1. Nach der Anmeldung Benachrichtigungen prüfen (rote/gelbe Symbole oben rechts). Ursachen klären und behobene Meldungen quittieren.
2. Zellentür schließen. Wenn der weiße Knopf blinkt, drücken; die Anlage verriegelt.
3. Den Anlage-Ein-Taster betätigen; die Anzeige leuchtet.
4. Am Weber-Display **Antriebe → Grundstellung → Bremstest** nach dem eingewiesenen Ablauf durchführen. Den Bewegungsbereich frei halten.

### 6.2 Robotersteuerung und Betriebsart

Für den Automatikbetrieb muss die Robotersteuerung in **EXT** bereitstehen. Am Smartpad gehört dazu das Roboterprogramm **cnc**; die Druckdatei wird später am Weber-Display geladen.

Ist nach dem Einschalten, einem Abbruch oder manuellem Verfahren eine erneute Programmanwahl bzw. eine SAK-/Startfahrt erforderlich, den [passenden vollständigen Wiederanlauf](#roboter-bewegt-sich-nicht-status-und-startfreigabe-prüfen) verwenden. Manuelle Wiederanlauffahrten erfolgen nur durch autorisiertes Personal in **T1** mit Zustimmschalter und Hardware-Starttaste; anschließend wird wieder auf **EXT** gewechselt. Eine Programmanwahl oder ein Reset ist kein routinemäßig vor jedem Druck erforderlicher Arbeitsschritt.

**Kontrolle:** Die Statusanzeigen **S, O, R und Ext** am Smartpad sind grün; der erforderliche Bremstest ist abgeschlossen und der Roboter steht in Grundstellung. Bei fehlender Bewegung oder blockiertem Bremstest nicht zum Druckstart übergehen, sondern [Status und Startfreigabe prüfen](#roboter-bewegt-sich-nicht-status-und-startfreigabe-prüfen).

---

## 7. Material und Temperaturprofile
Zugelassene Materialien: PETG, PLA. Andere (z. B. recycelte) nur nach schriftlicher Freigabe.

### 7.1 Rezept und Materialversorgung

Heizzonen in der Software:
- Einfüll-/Förderzone (oben, ggf. aktiv gekühlt)
- Prozesszone (mittig)
- Düsenadapter (unten)

Empfehlungen:
- Rezept „PETG 3 mm“ laden (typisch ca. 230 °C, material- und düsenabhängig).
- Bauteilkühlung/Lüfter zu Beginn deaktivieren (bessere Haftung), später nach Bedarf aktivieren.
- Trockner während des gesamten Drucks laufen lassen. Wenn der Trockner leer läuft, stoppt der Materialfluss; ein Wiederanlauf ist ohne Nachfüllen nicht möglich. Ein Software-Update zur Pausenfunktion ist angekündigt.

### 7.2 Materialfaktor und Bedienelemente

Materialfaktor (P1):
- Extrusionsmengen in DXR können mit einem Materialfaktor P1 skaliert werden (z. B. PETG ~3.1, PLA ~3.5 als Ausgangswerte, je nach Rezept und Düse). Die Anpassung erfolgt an der Maschinensteuerung, nicht im Grasshopper‑Skript. Änderungen dokumentieren und bei Gelegenheit kalibrieren.

Bedienübersicht (Weber-Software):
- Linke Paneele: Extruder-Heizzonen (Einfüllzone/Prozess/Düsenadapter), Extruder säubern, Schnecke freifahren.
- Rechte Paneele: Drucktisch (Mastertemperatur, Zonen 1–4, Vakuum Soll/Ist), Roboterstatus (Position X/Y/Z, TCP Override 10–200%).
- Untermenü (rechte Bildschirmleiste): Startbildschirm, Prozesseinstellungen, Düsenliste, Druckplattenliste, Formelsammlung, Rezepte, G‑Code, Bildschirmreinigung, Explorer, Trend‑Graphen.
- Hinweis: Das G‑Code/DXR‑Programm kann je nach Systempfad in `C:\\ProgramData\\Weber\\GCode\\` oder `D:\\Data\\Gcode` liegen.

### 7.3 Temperatur-Richtwerte

Die folgenden Werte stammen aus den dokumentierten Materialhinweisen. Sie sind keine universellen Sollwerte: Das freigegebene Rezept sowie die Material- und Herstellerangaben sind maßgeblich.

Basis‑Temperaturen (je nach Düse/Material feinjustieren):
- Einfüllzone: 35–60 °C (Materialabhängig)
- Heizzone 1/2/3: 160–235 °C (PLA eher niedriger, rPETG/Carbon‑P höher; Herstellerangaben beachten)
- Düsentemperatur: ca. 180–240 °C (Materialabhängig)
- Drucktisch: 60–100 °C (Haftung je nach Unterlage)
- Materialtrocknung: 4–6 h bei ~60–65 °C (Materialabhängig)

### 7.4 Kalibrierung und Druckqualität bei Bedarf

Materialfaktor ermitteln (Praxisleitfaden):
1. Einwandige Geometrie drucken; Linienbreite leicht über Düsen‑Ø anlegen (z. B. 1.2×).
2. SOLL‑ und IST‑Linienbreite messen und vergleichen.
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

## 8. Extruderreinigung und Materialwechsel
### 8.1 Vor jedem Druck: Materialfluss prüfen
1. Extruder vollständig aufheizen (mind. 15 Minuten, bis Soll erreicht).
2. „Extruder säubern“ ausführen.
3. Anzeige „Antriebsbelastung“ beobachten: blau = OK, orange/rot = sofort stoppen (Überdruckgefahr).
4. Materialfluss prüfen: gleichmäßige, plastische Extrusion.

### 8.2 Bei Materialwechsel oder Störung

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

## 9. Düse und Druckbett
### 9.1 Düsenwechsel bei Bedarf

Ein Düsenwechsel ist kein regulärer Schritt vor jedem Druck. Nur im Rahmen der Einweisung und nach Herstellervorgaben durchführen.

Düsenwechsel:
- Nur bei Betriebstemperatur durchführen (Material weich). 13er-Schlüssel verwenden.
- Alte Düse gegen den Uhrzeigersinn lösen; neue Düse mit geeigneter Metallpaste einsetzen.
- Anziehen: handfest + ¼ Drehung (Richtwert). Dichtheit nach erstem Aufheizen prüfen.
- Schichtbreite: ca. 1,2–2× Düsendurchmesser (z. B. Ø 3 mm → 3,5–6 mm).

Zusatz (Herstellerangabe):
- Düse im aufgeheizten Zustand wechseln; Dichtflächen/Gewinde der neuen Düse mit hitzefester Metall‑Gleitpaste versehen.
- Geringes Drehmoment (~15 Nm) genügt; bei spürbarem Widerstand stoppen.

### 9.2 Druckbett vor dem Druck prüfen
- Magnetische Platten aufliegen lassen (Geruchs-/Dampfentwicklung vermeiden). Keine offenen Lücken/Überlappungen.
- Typische Bett-Temperatur: 60–90 °C. Nicht berühren; Temperatur an der Bedieneinheit prüfen.

---

## 10. G-Code- und DXR-Dateien laden
### 10.1 Druckdatei vorbereiten und übertragen

1. Eine **DXR-Datei** für die Anlage vorbereiten. Liegt nur G-Code vor, zuerst wie unten beschrieben konvertieren.
2. Datei per USB-Stick oder TeamViewer an den Anlagen-PC übertragen. Am Schaltschrank ist nur der **rechte USB-Port** nutzbar.
3. Die erzeugte DXR-Datei in den G-Code-Ordner kopieren; dokumentierter Anlagenpfad: `D:\\Data\\Gcode`.
4. In der Weber-Software den **„G‑Code‑Tisch“** öffnen und die gewünschte Druckdatei laden.
5. Vor dem Start die Vorschau, Bauteilmaße und Offsets nach [Abschnitt 11](#11-druck-starten-vorschau-offsets-startgeschwindigkeit) prüfen.

**Kontrolle:** Die gewünschte Datei erscheint in der Weber-Software und lässt sich zur Vorbereitung des Druckauftrags laden. Fehlt sie oder bleibt der Ladevorgang hängen, zunächst [Datei und NC-Verbindung prüfen](#datei-lädt-nicht-oder-nc-verbindung-fehlt), nicht wiederholt den Druck starten.

### 10.2 G-Code bei Bedarf in DXR konvertieren
- Die Maschine verarbeitet DXR-Dateien, nicht direkt G‑Code. Für die Umwandlung die aktuelle [Grasshopper-Definition LARGERslicer Weber Robot and Ginger.gh](<../../../SETUP/LARGERslicer Weber Robot and Ginger.gh>) mit Roboter-Plugin verwenden. Sie enthält die Arbeitsabläufe Easy Slicer, Path Creator (Multiaxial) und Custom Paths; die archivierte Multiaxial-Datei ist dafür nicht erforderlich.
- Alternative am Schaltschrank: Weber‑Konverter nutzen (Taskleisten‑Icon → schwarzes Fenster). G‑Code per Drag‑and‑Drop ins Fenster ziehen, Enter drücken. Das erzeugte .dxr muss anschließend in den G‑Code‑Ordner kopiert werden, sonst wird es in der Weber‑Software nicht angezeigt.

### 10.3 Übertragung und Ladeprobleme

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

### 10.4 Einmalige Einrichtung am eigenen Rechner

Dieser Teil wird benötigt, wenn Druckdateien selbst mit Rhino/Grasshopper erstellt werden. Für eine bereits vorbereitete DXR-Datei mit [Abschnitt 11](#11-druck-starten-vorschau-offsets-startgeschwindigkeit) fortfahren.

<details>
<summary>Rhino/Grasshopper, Plugin und KUKA-Roboterbibliothek einrichten</summary>

**Einrichtung mit dem SETUP-Ordner (empfohlen):**
1. Rhino 8 installieren (empfohlen) und Rhino schließen.
2. Im Ordner [`SETUP`](<../../../SETUP/README.md>) des Repositories den Installer starten:
   - macOS: Doppelklick auf `Install Mac.command` (beim ersten Mal: Rechtsklick → *Öffnen*)
   - Windows: Doppelklick auf `Install Windows.bat`
3. Der Installer installiert **Robots**, **LARGERslicer** und **Pufferfish** über den Rhino Package Manager und kopiert die Roboterbibliothek **KUKA KR120** (`KR120_R2700_2`) in den Ordner, den das Robots-Plugin liest. Er kann jederzeit erneut ausgeführt werden und aktualisiert dann nur, was veraltet ist.
4. Rhino starten und die [Grasshopper-Definition](<../../../SETUP/LARGERslicer Weber Robot and Ginger.gh>) aus dem `SETUP`-Ordner öffnen.

Details und Fehlerbehebung: [SETUP-Anleitung](<../../../SETUP/README.md>).

#### Falls der Roboter trotzdem fehlt (KUKA KR120)

Wird in der *Load Robot System*-Komponente **KR120_R2700_2 missing** angezeigt (Komponente rot), den Installer erneut ausführen. Alternativ die Bibliothek direkt im Robots-Plugin laden:
1. In der *Load Robot System*-Komponente auf **Libraries** klicken
2. Die Online-Bibliothek **FH Muenster MSA** herunterladen
3. Grasshopper-Datei speichern, schließen und erneut öffnen – die Komponente wird grün und der KUKA KR120 erscheint in der Simulation

> **Hinweis:** Die Einrichtung ist nur einmal pro Rechner nötig.

**Weitere Einrichtungsschritte:**
 - In den Skripten Sicherheitszäune/Kollisionsobjekte korrekt setzen.

</details>

### 10.5 DXR-Kurzreferenz und Ablage

DXR‑Kurzreferenz:
- DXR spiegelt G‑Code‑Zeilen mit N‑Nummern, XYZ, G90/G91 und Orientierungen A/B/C wider.
- Extrusion wird mit `XE=[Wert*P1]` skaliert; P1 ist der Materialfaktor in der Maschinensteuerung.
- Beispiel: `N24 G1 X911.817 Y952.527 Z32.000 A0.000 B0.000 C0.000 G91 XE=[64.430*P1] G90`

Ein DXR-Beispielauszug befindet sich im [Anhang](#appendix-dxr-code-example).

HMI/G‑Code-Listen & Pfade:
- Über das rechte HMI‑Menü sind u. a. zugänglich: Rezepte, Düsenliste, Druckunterlagen, Formelsammlung, G‑Code‑Übersicht.
- DXR‑Dateien liegen je nach System in `D:\\Data\\Gcode`.

---

## 11. Druck starten (Vorschau, Offsets, Startgeschwindigkeit)
Wenn die Datei nicht geladen werden kann, zuerst die [Hinweise zum Laden](#10-g-code--und-dxr-dateien-laden) und die [Fehlerbehebung](#weitere-fehlerbilder-an-drucker-und-extruder) prüfen. Ist die Datei geladen, aber der Roboter bewegt sich nicht, die [schnelle Hilfe](#schnelle-hilfe-was-funktioniert-nicht) nutzen.

### 11.1 Voraussetzungen
- Rezept geladen, Material korrekt, Extruder gereinigt.
- Tür verriegelt (weiß → gedrückt), Sicherheit aktiv.
- Heizbett- und Düsentemperatur erreicht.

### 11.2 Druckauftrag vorbereiten und starten

Ablauf am Weber-Display:
1. Druckaufträge werden am Schaltschrank/Display gestartet und überwacht, nicht am KUKA-Smartpad.
2. Wartungsposition → Grundstellung.
3. „Druck starten“ drücken.
4. Startfenster: Bauteilmaße prüfen; XY-/Z‑Verschiebung setzen; Extruderausrichtung (Neigungs-/Drehwinkel) einstellen; „Auto Düsenoffset“ bei Bedarf aktivieren; „Abfahren des Bauraums“ aktivieren (min./max. Grenzen abfahren); „Abstreifen vor Druckstart/Schichtwechsel“ nach Bedarf aktivieren.
5. Vorschau prüfen; Offsets vorsichtig setzen. Z‑Kollisionsrisiko beachten.
6. Start mit reduziertem Override (10–50 %) beginnen; nach Stabilisierung schrittweise erhöhen. Nach Düsen-/Plattenwechsel Abstand Düse–Tisch nachmessen.

### 11.3 Druck überwachen

- Den Druck mit direkter Sichtlinie zum Roboter beginnen und besonders den Düsenabstand, die erste Materialablage und die Antriebsbelastung beobachten.
- Den reduzierten Override erst nach stabilisiertem Druck schrittweise erhöhen.
- Trockner und Materialversorgung während des Drucks prüfen. Bei auffälligen Geräuschen, Geruch, Vibrationen oder orange/roter Antriebsbelastung stoppen; bei Gefahr Not-Aus betätigen.
- Nach Druckende mit [Nachbereitung und Abschalten](#12-nachbereitung-und-abschalten) fortfahren.

### 11.4 Zusätzliche Prüfungen für multiaxiale Druckaufträge
- Multi‑axiales Drucken nutzt zusätzliche Orientierungen (A/B/C‑Achsen). Bei A=B=C=0 steht der Extruder senkrecht für horizontales Drucken. Orientierungen werden im Multiaxial‑Grasshopper‑Skript über Ebenen vorgegeben.
- Vor multiaxialen Druckaufträgen mögliche Kollisionen zwischen Extruder und Roboterarm prüfen; gegebenenfalls mit erhöhtem Tisch oder einer Vorrichtung arbeiten.
- Extruderneigung möglichst zur Wand bzw. in den größten freien Bereich ausrichten (weg von der Sicherheits-/Eingangstür), besonders bei 45-Grad-Druckaufträgen.
- Zusätzlichen Abstand zum Zaun einplanen; der Sicherheits-/Bremsbereich beginnt bereits vor der physischen Barriere.

---

## 12. Nachbereitung und Abschalten
1. Druckprozess beenden und abkühlen lassen; Werkstück erst < 50 °C vom Bett lösen.
2. Heizungen ausschalten; Kühlung/Chiller bis < 50 °C weiterlaufen lassen.
3. Roboter in Grundstellung fahren.
4. System herunterfahren: Weber-Runtime schließen → Windows beenden → Schaltschrank aus → Robotersteuerung aus.
5. Tür entriegeln; Arbeitsplatz aufräumen, Materialreste entsorgen, Boden reinigen.

---

## 13. Fehlerbehebung

### Roboter bewegt sich nicht: Status und Startfreigabe prüfen

„Keine Bewegung“ beschreibt zunächst nur das Verhalten, nicht die Ursache. Die dokumentierten Fälle betreffen unter anderem Sicherheitsfreigabe, Bremstest, Programmanwahl und die Roboter/CNC-Übergabe nach einem Abbruch oder manuellem Verfahren.

1. **Meldungen lesen:** Am Weber-Display, Smartpad und gegebenenfalls Schaltschrankdisplay den genauen Text prüfen. Bei Gefahr stoppen; Sicherheitsfunktionen nicht umgehen.
2. **Freigabe prüfen:** Ist die Zellentür geschlossen und verriegelt? Sind Sicherheit und Antriebe freigegeben? Steht die Robotersteuerung für den Automatikbetrieb in **EXT**? Die Statusanzeigen **S, O, R und Ext** am Smartpad prüfen. Eine fehlende Freigabe nicht durch wiederholte Startversuche ersetzen.
3. **Fehlerbild zuordnen:** Bei blinkendem Antriebe-Knopf, Bremstest-Anforderung oder „Active-Status erforderlich“ den [Bremstest-/Startablauf](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) lesen. Nach einem Stopp nahe dem Zaun zuerst den [Sicherheitsstopp-Ablauf](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck) verwenden.
4. **Nach Abbruch oder Handverfahren:** Ist die Sicherheitsfreigabe wiederhergestellt, aber Start-/Referenzfahrt oder Bewegung bleibt aus, den [Wiederanlauf bei fehlender Reaktion](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) verwenden. Dort sind Programmanwahl, Roboter/CNC-Übergabe und Referenzprüfung beschrieben.

Nur bei ausdrücklichen **E1-Justagemeldungen** gilt stattdessen der unten aufgeführte Sonderfall. Bleibt die Ursache unklar oder wird der beschriebene Zustand nicht erreicht, abbrechen und die Werkstattleitung informieren.

### KUKA-Roboter: Fehlerbilder schnell finden

Die Meldungen am Smartpad und an der Anlage sind deutsch. Die vollständigen, anlagenspezifischen Abläufe stehen in der [deutschen KUKA-Anleitung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md). Fehlerbild auswählen und vor einem Eingriff den verlinkten Ablauf vollständig lesen.

| Fehlerbild / Erkennungsmerkmale | Direkt zum Ablauf |
|---|---|
| Roboter reagiert nicht oder Start-, Referenz- bzw. Justagefahrt wird nicht ausgeführt; keine Bewegung trotz geladenem Auftrag oder Abbruch bei **0,0,0** | [Fehlende Bewegung / Referenzfahrt](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) |
| Bremstest oder Startbewegung beginnt nicht; Antriebe-Knopf blinkt, Druckabbruch bei **XYZ jeweils 0**, Meldungen wie **Neu Starten** oder **Active-Status erforderlich** | [Bremstest / Startbewegung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) |
| Roboter stoppt nahe dem Sicherheitszaun, besonders bei multiaxialen Bahnen; Sicherheitsstopp oder Bremstest-Meldung | [Notstopp am Sicherheitszaun](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck) |
| Seltener Sonderfall: Roboter nicht justiert, externe Achse **E1**; **KSS01347**, **KSS00106** oder **KSS15047**; Bewegung und Programmstart gesperrt | [E1-Wiederherstellung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-nicht-justiert-e1) |

Die Abläufe sind installationsspezifisch. Keine allgemeinen Reset- oder manuellen Fahrversuche an die Stelle der beschriebenen Sicherheitsvoraussetzungen setzen.

### Weitere Fehlerbilder an Drucker und Extruder

| Problem | Mögliche Ursache | Maßnahme |
|---|---|---|
| Keine Extrusion | Extruder nicht heiß genug / noch nicht durchgewärmt | Weiter aufheizen (> 15 min), dann „Extruder säubern“ |
| Antriebsbelastung orange/rot | Überdruck durch kaltes Material/Verstopfung | Sofort stoppen, Temperatur prüfen, Extruder säubern |
| Überdrucksicherung platzt | Materialstau, zu kalt, falsches Material, Düsenkollision | Druck stoppen, Extruderkopf prüfen, alle Sicherungsschrauben ersetzen |
| Düse kollidiert mit Bett | Falscher Z-Offset/Programmfehler | Sofort stoppen/Not-Aus, Offsets prüfen, Bett und Düse inspizieren |
| Verbindung „NC“ fehlt | Netzwerkpfad getrennt | Explorer öffnen, „NC“ neu verbinden, Datei erneut laden |
| Endloses Laden beim Druckauftragsstart | Startreihenfolge/NC‑Verbindung fehlerhaft | In der Anzeige oben rechts: Runtime beenden → Explorer öffnen → „NC‑Laufwerk“ in der Seitenleiste anklicken (Symbol wechselt von rot auf grün) → Druckauftrag erneut laden |
| Temperaturanzeige rot/Fehler | Heizelement/Sensorfehler | Nicht weiterdrucken; Werkstatt/Hersteller informieren |
| Anlage bläst/kühlt ungewöhnlich laut | Robotikraum zu warm / thermische Last hoch | Robotikraum lüften, Luftaustausch erhöhen, Temperatur stabilisieren |
| Starker Geruch | Platten abgenommen / Filter aus | Platten auflegen; Aktivkohlefilter einschalten; lüften |
| Druck stoppt bei leerem Trockner | Trockner leer; kein Pausenmodus | Material nachfüllen; Druck kann ohne Update nicht fortgesetzt werden |
| Referenzfahrt schlägt fehl, Startpunkt wird nicht gefunden | Referenzschalter schaltet möglicherweise nicht korrekt | Schalter nicht selbst verstellen; [KUKA-Fehlerbehebung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) beachten und Anlagenverantwortliche bzw. qualifiziertes Wartungspersonal informieren |

### Datei lädt nicht oder NC-Verbindung fehlt

Zuerst Dateiformat, Namen, Inhalt und Ablage nach [Abschnitt 10](#10-g-code--und-dxr-dateien-laden) prüfen. Wird die Datei nicht angezeigt, die Runtime beenden, den Explorer öffnen und die Netzwerkverbindung **„NC“** prüfen. Bei rotem Symbol anklicken, um die Verbindung wiederherzustellen.

G‑Code/DXR lädt weiterhin nicht:
- Beim Laden wird Gcode als `Gcode.nc` auf ein mit dem Roboter geteiltes Laufwerk kopiert. Bei Ladeproblemen: Im Explorer Laufwerk `nc (Z:)` öffnen und `Gcode.nc` löschen; Druckprogramm erneut laden.

### Probleme nach Programmabbruch oder manuellem Verfahren
- Beim Wechsel vom Roboterprogramm zur CNC‑Steuerung übernimmt diese die Lageregelung. Stimmen Positionen nicht überein, stoppt der Roboter mit Fehlermeldungen.
- Für den vollständigen Wiederanlauf nach Programmabbruch oder manuellem Verfahren die passende [KUKA-Wiederherstellungsanleitung](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) verwenden.

### Fehlerbehebung: Roboter nicht justiert (E1)

**Seltener Sonderfall, kein Standard-Wiederanlauf:** Keine Bewegung und kein Programmstart mit **KSS01347** (Roboter nicht justiert), **KSS00106** (Justage durchführen E1) oder **KSS15047** (Justagereferenzierung angefordert).

Die Wiederherstellung ist installationsspezifisch und setzt die Prüfung eines Backups voraus. Vor jedem Eingriff die vollständige [E1-Fehlerbehebung samt Voraussetzungen und Kontrolle](../../../LARGERslicer/documentations/KUKA_ROBOT_TROUBLESHOOTING_DE.md#problem-roboter-nicht-justiert-e1) lesen.

---

## 14. Wartung und regelmäßige Checks
- Chiller-Füllstand alle paar Wochen/Monate prüfen (geschl. Kreislauf, selten Nachfüllen nötig; Reserve im schwarzen Behälter).
- Überdrucksicherung visuell prüfen; bei Auslösung alle Schrauben ersetzen (Ersatz im Regal über KUKA-Steuerung). Bestände monatlich kontrollieren und ggf. nachbestellen.
- Ordnung im Raum sicherstellen; Materialsäcke entsorgen; Arbeitsfläche sauber halten.
- Materialfaktoren (Durchfluss) für PETG/PLA bei Gelegenheit kalibrieren und dokumentieren.
### Kalibrierung und Referenzierung
- Die aktuelle Installation ist mechanisch fix; regelmäßige Kalibrierung (TCP/Base/Bett) ist im Normalbetrieb nicht erforderlich.
- Nach mechanischen Änderungen (Düse/Adapter getauscht, Extruderkopf neu ausgerichtet, Bett neu positioniert) sind TCP/Base zu prüfen und ggf. neu einzumessen.

---

## 15. Hinweise und offene Punkte

### Kamera – Liveansicht
- Adresse: http://fb05-dxr25-cam.fh-muenster.de
- Erreichbar im FH‑Netzwerk oder per VPN. Zugriff in der Regel nur, wenn der Roboter aktiv ist.
- Zugangsdaten:
   - Konten: „Admin“ und „student“. Das jeweils freigegebene Konto und Passwort bei der Werkstattleitung erfragen.
 - Der Stream wird nicht aufgezeichnet und nicht online gespeichert.

### Weitere Hinweise
- Geplantes Softwareupdate für verbesserten Materialfluss und Pausenfunktion (Herstellerangabe, Termin noch offen).
- Hochpreisige Masterbatches nur in Abstimmung mit der Werkstattleitung verwenden.
- Änderungen am Schaltschrank/Verkabelung ausschließlich in Abstimmung mit Hersteller/Werkstatt.

---

## 16. Schnellstart-Checkliste DXR25

Vor dem Start
- [ ] Raum 5–10 Min. gelüftet; Sicherheitsbereich frei; Platten korrekt aufgelegt
- [ ] Trockner an; Material PETG/PLA vorhanden; Chiller plausibel
- [ ] Robotersteuerung an; Schaltschrank an; als „Operator“ mit freigegebenen Zugangsdaten angemeldet
- [ ] Benachrichtigungen quittiert; Sicherheit verriegelbar; Bremstest durchgeführt

Material und Heizung
- [ ] Rezept geladen (z. B. „PETG 3 mm“); Heizzonen auf Soll; 15 Min. durchwärmen
- [ ] Extruder säubern; Antriebsbelastung blau
- [ ] Düse fest; Überdrucksicherung intakt; Handschuhe bereit

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

**Dokumentationsänderung 1.3 (21.10.2026):** Regulären Druckablauf, Bedienstellen und Erfolgskontrollen gegliedert; einmalige Einrichtung vom laufenden Betrieb getrennt. Fehlerhilfe auf fehlende Bewegung, Freigabe, Bremstest und Wiederanlauf ausgerichtet; E1 als seltenen Sonderfall erhalten. Grundlage bleiben die vorhandenen Einweisungs- und Anlagenhinweise.

**Dokumentationsänderung 1.2 (21.10.2026):** Geführten Einstieg vom Einschalten bis zum Abschalten sowie direkte deutsche Fehlerhilfe für fehlende Bewegung, Bremstest, Zaunstopp, Ladeprobleme und E1 ergänzt; ausführliche Abläufe bleiben in den verlinkten Abschnitten erhalten.

## Mitwirkende
Moritz Wesseler, FH Münster – MSA Robotics Lab (Zusammenstellung/Erstellung dieser Anleitung und Beispiele)

---

<a id="appendix-dxr-code-example"></a>

## Anhang – DXR-Code-Beispiel (Auszug)

Hinweis: Das folgende Beispiel zeigt den Dateikopf und die ersten Bewegungszeilen einer DXR-Ausgabe. Es ist kein vollständiges, direkt ausführbares Druckprogramm. Parameter wie P1 (Materialfaktor) werden an der Maschinensteuerung eingestellt.

<details>
<summary><strong>DXR-Beispielauszug ein-/ausklappen</strong></summary>

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
