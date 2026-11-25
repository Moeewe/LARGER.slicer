# LARGER.slicer – SETUP

[English](README.md) · **Deutsch**

Dieser Ordner enthält alles, um die Grasshopper-Definition **`LARGERslicer Weber Robot and Ginger.gh`** für den Weber DXR25 und den Ginger One einzurichten.

**Bitte `LARGERslicer Weber Robot and Ginger V2.gh` verwenden.** Sie hat dieselben drei Arbeitswege, ist mit den aktuellen Bausteinen gebaut und braucht **nur das Plug-in LARGER.slicer** (1.3.1 oder neuer) - kein Robots, kein Pufferfish. Sie öffnet in Rhino 8 und Rhino 9 ohne Fehler und Warnungen. Die erste Fassung bleibt zum Nachschlagen liegen; sie braucht Robots und Pufferfish und wird nicht mehr gepflegt.

| Datei | Zweck |
|---|---|
| `LARGERslicer Weber Robot and Ginger V2.gh` | **Aktuell.** 01 Easy Ginger and Robot Slicer, 02 Advanced Multiaxial Robot Slicer, 03 Custom Robot Path Creator; nur LARGER.slicer |
| `LARGERslicer Weber Robot and Ginger.gh` | Erste Fassung (braucht Robots und Pufferfish), zum Nachschlagen |
| `LARGERslicer Weber Robot and Ginger - corrected.gh` | Die erste Fassung mit Korrekturen (November 2026), braucht Robots und Pufferfish: 03 extrudiert Breite x Höhe x 1,128 x Länge (vorher etwa 11-mal zu wenig), 02 rechnet ebenfalls mit 1,128 und verlässt das Teil ohne Material und ohne die Hand zu drehen, der DXR File Health Check prüft den Code, den er bekommt, Liste der Plug-ins korrigiert. Die Änderungen stehen auf dem Canvas |
| `LARGERslicer Extrusion Factor Test.gh` | Findet den Extrusionsfaktor: je drei Wände aus einer Bahn für den Ginger One (0,18 / 0,21 / 0,24) und den Weber DXR (1,0 / 1,128 / 1,258, P1 = 1 an der Steuerung). Wandstärke messen, eintragen, die Datei gibt den neuen Faktor. Nur LARGER.slicer |
| `Install Windows.bat` (+ `setup-windows.ps1`) | Installer für Windows |
| `LARGER.slicer Installer.pkg` | Installer für macOS, signiert und notarisiert (nur im Release-Download; gebaut mit `macos-installer/build.sh` aus `Install Mac.command`) |
| `RobotLibrary/` | KUKA-KR120-Roboterbibliothek der FH Münster MSA (`KR120_R2700_2`) |

## Installation

**Voraussetzungen:** Rhino 8 (empfohlen) oder Rhino 7 · Internetverbindung

1. Rhino schließen.
2. Installer starten:

   | Windows | macOS |
   |---|---|
   | Doppelklick auf `Install Windows.bat` (zeigt Windows *Der Computer wurde durch Windows geschützt*: **Weitere Informationen** → **Trotzdem ausführen**) | Doppelklick auf `LARGER.slicer Installer.pkg` (von Apple signiert und notarisiert) |

3. Rhino starten, eine Datei in **Millimetern** anlegen, Grasshopper öffnen und `LARGERslicer Weber Robot and Ginger V2.gh` aus diesem Ordner öffnen.

## Was der Installer macht

- Installiert über den Rhino Package Manager die neueste passende Version von **Robots**, **LARGERslicer** und **Pufferfish** – für Rhino 8 und/oder Rhino 7, je nachdem, was installiert ist.
- **LARGERslicer** kommt aus dem Ordner `packages` des Release-Downloads (`LARGERslicer-SETUP.zip`), wenn dieses Paket neuer ist als das auf dem Rhino-Paketserver. So passt das Plug-in zur Definition in diesem Ordner. Den Ordner gibt es nur im Release-Download, nicht im Repository.
- Kopiert die Roboterbibliothek **KR120_R2700_2** in den Ordner, den das Robots-Plugin liest (`Dokumente/Robots`, unter macOS mit Rhino 7 zusätzlich `~/Robots`).
- Kann jederzeit erneut ausgeführt werden:
  - aktuelle Plugins bleiben unverändert, ältere werden aktualisiert, neuere (z. B. Beta-Versionen) bleiben erhalten,
  - alte, von Hand kopierte `.gha`-Dateien im Grasshopper-Libraries-Ordner werden erkannt und auf Wunsch in einen Backup-Ordner verschoben,
  - eine abweichende vorhandene Bibliotheksdatei wird als `*.bak-<Datum>` gesichert, bevor sie ersetzt wird.

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| macOS: Was hat der Installer gemacht? | Siehe `~/Library/Logs/LARGERslicer-Installer.log`. Ohne das Paket: `bash "Install Mac.command"` im Terminal |
| „Package-Server nicht erreichbar“ | Internetverbindung prüfen und Installer erneut ausführen |
| Grasshopper: Robotersystem **KR120_R2700_2 missing** | Installer erneut ausführen oder in *Load Robot System* auf **Libraries** klicken und die Online-Bibliothek **FH Muenster MSA** herunterladen |
| Komponenten fehlen oder sind doppelt | Installer erneut ausführen, alte manuelle Kopien verschieben lassen, Rhino neu starten |
| Rhino lief während der Installation | Rhino neu starten – Plugins werden erst beim Start geladen |

## Weitere Anleitungen

- [Weber DXR25 – Inbetriebnahme und Bedienanleitung](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20GERMAN.md)
- [Ginger One – Schnellstart (Englisch)](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md)
- [LARGER.slicer auf GitHub](https://github.com/Moeewe/LARGER.slicer/blob/main/README.de.md)

## Beispiele

- `LARGERslicer - Beispiele für Studierende.gh`: sechs erklärte Beispiele zum Einstieg: Vase, Box mit Deckel, Ornament wo du willst, Lüfter pro Lage für den Weber DXR, Tonvase mit dem UR5. Jedes mit Steuerpult (Control Panel) und Ergebnis (Result).
