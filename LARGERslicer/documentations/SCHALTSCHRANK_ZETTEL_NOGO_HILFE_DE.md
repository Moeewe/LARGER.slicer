# SCHALTSCHRANK-ZETTEL: NO-GO SOFORTHILFE (DXR / GCODE)

Version: 1.0  
Stand: 2026-07-28

Kurzregel:

Bei NO GO niemals laden. Erst Ursache beheben, dann erneut pruefen.

---

## A) Typische NO-GO Ursachen

- Datei nicht gefunden oder falscher Pfad.
- Falsche Endung oder nicht erlaubter Dateityp.
- Leere Datei oder unplausibler Inhalt.
- Datei wirkt beschaedigt/binaer.
- DXR Marker/Ende fehlt.
- Zu wenige Bewegungszeilen.
- Datei ungewoehnlich gross, aber fast kein gueltiger Inhalt.

---

## B) 5-Minuten Fehlerablauf

1. Dateipfad pruefen.
2. Dateiname nach Regelblock korrigieren.
3. Datei im Texteditor kurz oeffnen:
- Lesbarer Text?
- Wirkt der Inhalt vollstaendig?
4. Neu exportieren.
5. Erneut durch DXR File Health Check schicken.

Nur bei GO weiter zur Maschine.

---

## C) Dateiname-Regelblock (Kurz)

- A-Z, a-z, 0-9, underscore, bindestrich
- keine Umlaute
- keine Leerzeichen
- keine extra Punkte
- Endung nur einmal am Ende

---

## D) Eskalation

Wenn nach Neu-Export weiterhin NO GO:

- Quickstart lesen (QR 1)
- Troubleshooting oeffnen (QR 2)
- Verantwortliche Person hinzuziehen

---

## E) QR-Platzhalter

QR 1 QUICKSTART

Link: https://github.com/Moeewe/LARGER.slicer

QR 2 TROUBLESHOOTING

Link: https://github.com/Moeewe/LARGER.slicer/issues
