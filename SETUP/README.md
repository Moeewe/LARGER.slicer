# LARGERslicer SETUP – Weber DXR25 / Ginger

*English version below.*

Dieser Ordner enthält alles, um die Grasshopper-Definition **`LARGERslicer Weber Robot and Ginger.gh`** auf einem neuen Rechner zum Laufen zu bringen.

| Datei | Zweck |
|---|---|
| `LARGERslicer Weber Robot and Ginger.gh` | Die Grasshopper-Definition (Easy Slicer, Path Creator Multiaxial, Custom Paths) |
| `Install Mac.command` | Installer für macOS |
| `Install Windows.bat` (+ `setup-windows.ps1`) | Installer für Windows |
| `RobotLibrary/` | KUKA-KR120-Roboterbibliothek der FH Münster MSA (`KR120_R2700_2`) |

## Installation

1. Rhino **8** installieren (empfohlen). Rhino 7 wird ebenfalls eingerichtet, ist mit dieser Definition aber nicht getestet.
2. Rhino schließen.
3. Installer starten:
   - **macOS:** Doppelklick auf `Install Mac.command`. Beim ersten Mal blockiert macOS heruntergeladene Skripte: Rechtsklick → *Öffnen* → *Öffnen*.
   - **Windows:** Doppelklick auf `Install Windows.bat`.
4. Rhino starten, Grasshopper öffnen und `LARGERslicer Weber Robot and Ginger.gh` aus diesem Ordner öffnen. Rhino-Dokument in **Millimetern** anlegen.

Eine Internetverbindung ist nötig, weil die Plugins vom Rhino Package Manager geladen werden.

## Was der Installer macht

- Installiert über den Rhino Package Manager die neueste passende Version von **Robots**, **LARGERslicer** und **Pufferfish** – für Rhino 8 und/oder Rhino 7, je nachdem, was installiert ist.
- Kopiert die Roboterbibliothek **KR120_R2700_2** in den Ordner, in dem das Robots-Plugin sucht (`Dokumente/Robots`, unter macOS mit Rhino 7 zusätzlich `~/Robots`).
- Kann beliebig oft ausgeführt werden:
  - aktuelle Plugins bleiben unverändert, ältere werden aktualisiert, neuere (z. B. Beta) bleiben erhalten,
  - alte, manuell kopierte `.gha`-Dateien im Grasshopper-Libraries-Ordner werden erkannt und auf Wunsch in einen Backup-Ordner verschoben,
  - eine abweichende vorhandene Bibliotheksdatei wird als `*.bak-<Datum>` gesichert, bevor sie ersetzt wird.

## Probleme?

| Problem | Lösung |
|---|---|
| macOS: „kann nicht geöffnet werden, da es von einem nicht verifizierten Entwickler stammt“ | Rechtsklick auf `Install Mac.command` → *Öffnen* |
| Installer meldet „Package-Server nicht erreichbar“ | Internetverbindung prüfen und erneut ausführen |
| In Grasshopper: Robot System **KR120_R2700_2 missing** | Installer erneut ausführen. Alternativ in der *Load Robot System*-Komponente auf **Libraries** klicken und die Online-Bibliothek **FH Muenster MSA** herunterladen |
| Komponenten fehlen oder sind doppelt | Installer erneut ausführen und alte manuelle Kopien verschieben lassen, danach Rhino neu starten |
| Rhino lief während der Installation | Rhino neu starten – Plugins werden erst beim Start geladen |

Weitere Hinweise zur Maschine: [Weber DXR25 Anleitung (Deutsch)](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20WEBER%20-%20DXR25%20-%20README/README%20Weber%20DXR25%20PRINTER%20QUICK%20START%20GUIDE%20GERMAN.md) · [Ginger Quick Start](https://github.com/Moeewe/LARGER.slicer/blob/main/EXAMPLE%20FILES/00%20-%20WEBER%20-%20GINGER%20%5BRobotic%5D%203D%20Printing/00%20-%20GINGER%20-%20ONE%20-%20README/README%20GINGER%2000%20QUICK%20START%20GUIDE.md)

---

## English

This folder contains everything needed to run **`LARGERslicer Weber Robot and Ginger.gh`** on a new computer.

1. Install Rhino **8** (recommended; Rhino 7 is set up too but not tested with this definition) and close Rhino.
2. Run the installer: **macOS** `Install Mac.command` (first time: right-click → *Open*), **Windows** `Install Windows.bat`.
3. Start Rhino (millimeters), open Grasshopper and open `LARGERslicer Weber Robot and Ginger.gh` from this folder.

The installer installs the latest compatible **Robots**, **LARGERslicer** and **Pufferfish** from the Rhino Package Manager and copies the **KR120_R2700_2** robot library to the folder the Robots plugin reads. It can be run again at any time: current plugins are kept, older ones updated, newer ones kept, old manual `.gha` copies can be moved to a backup, and a differing library file is backed up before it is replaced.

If the robot system shows as *missing*, run the installer again, or click **Libraries** on the *Load Robot System* component and download the online library **FH Muenster MSA**.
