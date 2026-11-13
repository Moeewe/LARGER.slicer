# Entwicklerhandbuch LARGER.slicer – Überblick

Diese Seite fasst das Entwicklerhandbuch auf Deutsch zusammen. Die ausführlichen Seiten sind auf
Englisch und gehen genau auf den Code ein. [English](README.md)

## Die Seiten

| Seite | Inhalt |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Aufbau des Repositorys, der gemeinsame Slicer-Kern (`LARGERslicer/Toolpaths`), Datenmodell, die Ebene für Grasshopper 1 (Zeilen auf den Komponenten, Rechnen im Hintergrund, Quick Start) und Grasshopper 2, Build, Installationsorte, Pakete, die `obj/`-Eigenheit |
| [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) | Die Regeln, die jeder Pfad einhält, und was der Referenzdruck UNIKAT HEAD V2 (2024) gelehrt hat |
| [ICONS.md](ICONS.md) | Wie die Icons gezeichnet werden: Stil, Farben je Gruppe, `finish()`, Registrierung, 48 px für Grasshopper 2, neues Icon anlegen, md5-Prüfung |
| [TESTING.md](TESTING.md) | Wie getestet wurde: Konsolen-Prüfstand, Rhino-9-Testinstanz, Dokumente im Speicher, sichtbare Arbeitsfläche, Messgrößen |
| [EXAMPLES.md](EXAMPLES.md) | Jede Beispieldatei und jeder Abschnitt von Quick Start, mit Maschine |
| [DECISIONS.md](DECISIONS.md) | Wichtige Entscheidungen und ihre Gründe |

## Das Wichtigste in Kürze

**Kern und Oberfläche.** Alles, was schneidet, füllt, verbindet, prüft und Dateien schreibt, liegt in
`LARGERslicer/Toolpaths`. Das Grasshopper-1-Plug-in (`LARGERslicer.gha`) und das Grasshopper-2-Plug-in
(`LARGERslicerGH2.rhp`, Rhino 9) übersetzen dieselben Quelldateien; eine Korrektur im Kern wirkt in
beiden. Der Kern vermeidet native RhinoCommon-Aufrufe und Grasshopper-Typen, damit er schnell ist und
auch außerhalb von Rhino getestet werden kann.

**Die Druckregeln.**

- Eine einzige, durchgehende Linie vom Skirt bis zur letzten Lage, die sich nie selbst kreuzt.
  Kreuzungsfreiheit geht vor dem gewünschten Muster.
- Die Raupenbreite bestimmt alle erzeugte Geometrie (Abstand = Breite x (1 − Überlappung)).
  Hausstandard: 3-mm-Flachdüse, 5 mm Raupe, 2 mm Lage.
- Jede Raupe liegt auf der darunter: höchstens eine halbe Raupenbreite Versatz.
- Ebene Lagen mit einer festen Naht und kurzer Rampe; Spirale nur bei einer einzelnen Wand.
- Konstantes F je Lage; langsamer nur durch Min Layer Time, Short Lap Time oder Max Volume Rate,
  nie unter Min Speed. Geschwindigkeiten werden in mm/s eingegeben (50 mm/s = F3000).
- Skirt zuerst, am Ende eine Abfahrt ohne Extrusion seitlich weg vom Teil.
- E je Maschine: Ginger One (Marlin) E = 0,21 x Breite x Höhe x Länge; Weber DXR25 `XE=[Wert*P1]`
  mit Faktor 1,128; das Ginger-Handbuch erreicht mit „Filamentdurchmesser 2,5 mm“ in einem
  Filament-Slicer dasselbe auf 3 % genau.

**Der Referenzdruck.** Der Kopf UNIKAT HEAD V2 (Ginger One, 2024) wurde aus seinem G-Code
nachvollzogen: Brim aus 8 Ringen, ein flacher Ring, dann eine Spirale mit 120 Runden, Rundenzeit
40 s bzw. 35 s, und das Extrusionsgesetz t = dz + (h − dz) · dxy / w. Danach wurden „Follow the
surface“, Short Lap Time, der Brim mit versetzten Übergängen und die Toleranzen der Prüfungen gebaut.

**Testen.** Nie in Rhino 8 oder in Dokumenten des Eigentümers. Eine Rhino-9-Instanz pro Mac, nur
`.gha` und `.pdb` installieren, solange Rhino 9 geschlossen ist, Dokumente ohne Speicherdialog
schließen. Ergebnisse werden angesehen, nicht nur gemessen.

## Weitere Dokumentation

[README.de.md](../README.de.md) (Benutzerüberblick) · [OWN_MACHINES.md](OWN_MACHINES.md) (eigene Maschinen) ·
[GingerOne.README.md](../LARGERslicer/Resources/GingerOne.README.md) · [Grasshopper 2](../LARGERslicer.GH2/README.md) ·
[CHANGELOG](../LARGERslicer/CHANGELOG.md) · [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)

Die eigenständige Anwendung **LARGER.app** ist ein eigenes, privates Projekt. Sie teilt die Presets
und Druckregeln mit dem Plug-in.
