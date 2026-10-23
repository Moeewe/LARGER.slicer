# LARGER.slicer – SETUP

[English](README.md) · **Deutsch**

Dieser Ordner enthält alles, um die Grasshopper-Definition **`LARGERslicer Weber Robot and Ginger.gh`** für den Weber DXR25 und den Ginger One einzurichten.

| Datei | Zweck |
|---|---|
| `LARGERslicer Weber Robot and Ginger.gh` | Grasshopper-Definition mit Easy Slicer, Path Creator (multiaxial) und Custom Paths |
| `Install Windows.bat` (+ `setup-windows.ps1`) | Installer für Windows |
| `Install Mac.command` | Installer für macOS |
| `RobotLibrary/` | KUKA-KR120-Roboterbibliothek der FH Münster MSA (`KR120_R2700_2`) |

## Installation

**Voraussetzungen:** Rhino 8 (empfohlen) oder Rhino 7 · Internetverbindung

1. Rhino schließen.
2. Installer starten:

   | Windows | macOS |
   |---|---|
   | Doppelklick auf `Install Windows.bat` | Rechtsklick auf `Install Mac.command` → **Öffnen** → **Öffnen** |

3. Rhino starten, eine Datei in **Millimetern** anlegen, Grasshopper öffnen und `LARGERslicer Weber Robot and Ginger.gh` aus diesem Ordner öffnen.

## Was der Installer macht

- Installiert über den Rhino Package Manager die neueste passende Version von **Robots**, **LARGERslicer** und **Pufferfish** – für Rhino 8 und/oder Rhino 7, je nachdem, was installiert ist.
- Kopiert die Roboterbibliothek **KR120_R2700_2** in den Ordner, den das Robots-Plugin liest (`Dokumente/Robots`, unter macOS mit Rhino 7 zusätzlich `~/Robots`).
- Kann jederzeit erneut ausgeführt werden:
  - aktuelle Plugins bleiben unverändert, ältere werden aktualisiert, neuere (z. B. Beta-Versionen) bleiben erhalten,
  - alte, von Hand kopierte `.gha`-Dateien im Grasshopper-Libraries-Ordner werden erkannt und auf Wunsch in einen Backup-Ordner verschoben,
  - eine abweichende vorhandene Bibliotheksdatei wird als `*.bak-<Datum>` gesichert, bevor sie ersetzt wird.

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| macOS: „kann nicht geöffnet werden, da es von einem nicht verifizierten Entwickler stammt“ | Rechtsklick auf `Install Mac.command` → **Öffnen** |
| „Package-Server nicht erreichbar“ | Internetverbindung prüfen und Installer erneut ausführen |
| Grasshopper: Robotersystem **KR120_R2700_2 missing** | Installer erneut ausführen oder in *Load Robot System* auf **Libraries** klicken und die Online-Bibliothek **FH Muenster MSA** herunterladen |
| Komponenten fehlen oder sind doppelt | Installer erneut ausführen, alte manuelle Kopien verschieben lassen, Rhino neu starten |
| Rhino lief während der Installation | Rhino neu starten – Plugins werden erst beim Start geladen |

## Weitere Anleitungen

- [Weber DXR25 – Inbetriebnahme und Bedienanleitung](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20GERMAN.md)
- [Ginger One – Schnellstart (Englisch)](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md)
- [LARGER.slicer auf GitHub](https://github.com/Moeewe/LARGER.slicer/blob/main/README.de.md)
