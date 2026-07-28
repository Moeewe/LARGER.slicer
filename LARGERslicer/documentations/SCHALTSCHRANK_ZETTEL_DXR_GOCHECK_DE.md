# SCHALTSCHRANK-ZETTEL: DXR / GCODE GO-NO-GO

Version: 1.0  
Stand: 2026-07-28  
Gilt fuer: Weber DXR / Ginger Workflow mit LARGERslicer

---

## 1) 30-Sekunden-Vorstartcheck

- Passende Maschine und Material sind ausgewaehlt.
- Die zu ladende Datei ist die richtige Projektdatei.
- Dateiname ist kurz und sauber (siehe Regelblock unten).
- Datei stammt aus freigegebenem Exportordner.
- Datei wurde mit DXR File Health Check geprueft.
- Ergebnis ist GO.

Wenn eine Position nicht passt: NICHT LADEN.

---

## 2) Dateiname-Regeln (Pflicht)

- Nur Buchstaben, Zahlen, Unterstrich, Bindestrich.
- Keine Umlaute, keine Sonderzeichen, keine Leerzeichen.
- Keine zusaetzlichen Punkte im Namen.
- Nur eine Endung am Ende, z. B. .dxr oder .gcode.
- Kurz halten (empfohlen <= 64 Zeichen).

Beispiel gut:

PROJECT01_PART_A_V12.dxr

Beispiel schlecht:

Projekt A.v12.final.dxr

---

## 3) GO / NO GO Entscheidung

GO bedeutet:

- Datei existiert und ist lesbar.
- Dateiname-Regeln sind eingehalten.
- Inhalt ist plausibel und druckbar.

NO GO bedeutet:

- Mindestens eine Pflichtpruefung ist fehlgeschlagen.
- Datei NICHT in die Maschine laden.
- Neu exportieren oder Quickstart/Fehlerhilfe verwenden.

---

## 4) Was tun bei NO GO

1. Datei nicht laden.
2. Exportordner und Dateiname korrigieren.
3. Datei neu exportieren.
4. Erneut mit DXR File Health Check pruefen.
5. Erst bei GO laden.

Wenn danach weiterhin NO GO:

- Troubleshooting oeffnen (QR 2).
- Verantwortliche Person informieren.

---

## 5) QR-Codes (drucken und aufkleben)

QR 1: QUICKSTART WEBER/GINGER

- Ziel: Betriebsablauf, Material, Startsequenz
- Link: https://github.com/Moeewe/LARGER.slicer

QR 2: NO GO TROUBLESHOOTING

- Ziel: Haeufige Fehler und Sofortmassnahmen
- Link: https://github.com/Moeewe/LARGER.slicer/issues

Hinweis:

QRs muessen auf eine erreichbare URL zeigen (z. B. GitHub, SharePoint, Confluence), nicht auf lokale Dateipfade.

---

## 6) Interne Referenzen (Quelle)

- EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md
- EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md
- EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md

---

## 7) Freigabe

Bereich/Anlage: ________________________________

Verantwortlich: ________________________________

Datum: ________________________________________
