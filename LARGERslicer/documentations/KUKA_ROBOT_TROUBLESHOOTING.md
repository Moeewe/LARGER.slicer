# KUKA Robot Troubleshooting Guide / KUKA Roboter Fehlerbehebungsanleitung

## English Version

### Issue: Robot Does Not Perform Brake Test / Start Movement After Startup

**Symptoms:**
- Robot does not perform required brake test / start movement after startup
- Robot cannot move to home position
- Drive button blinks continuously
- Error message: "Druck abgebrochen bei XYZ jeweils 0" (Pressure aborted at XYZ all 0)
- Control cabinet display shows: "Neu Starten" (Restart) or "Warnschwelle für Bremstest erreicht mit 0 Stunden Restlaufzeit" (Brake test warning threshold reached with 0 hours remaining runtime)
- Corresponding indicator light is red

**KUKA Smart Remote / Smartpad Display Shows:**
- "Quitt Fahrtfreigabe gesamt Verursacher KS" (Acknowledge overall drive enable cause KS)
- "Active-Status erforderlich" (Active status required)
- Status indicators "S" "O" "R" "Ext" show: Green, Gray, Yellow, Green (or similar)
- **Expected:** All indicators should be green on the Smartpad

**Root Cause:**
The CNC program on the robot controller needs to be manually reselected.

**Solution (Work on Smartpad Only):**

1. **Switch Key Position:**
   - Turn the key from "Remote" to "Zahnrad" (Gear icon) position

2. **Enter T1 Mode:**
   - Navigate to T1 mode
   - Then switch back: Turn key from "Zahnrad" to "Remote"

3. **Open Navigation:**
   - Tap the blue gear icon on the left side of the touchscreen
   - Click "Öffnen" (Open)
   - Tap the orange "X" on the left side
   - This opens the navigation overview

4. **Select CNC Program:**
   - Multiple files and folders are displayed
   - Click on "cnc"
   - Tap "Anwählen" (Select) at the bottom of the touch display

5. **Reset Program:**
   - At the top of the Smartpad touchscreen, tap the yellow square "R"
   - A window opens
   - Click: "Programm zurücksetzen" (Reset program)

6. **Return to External Mode:**
   - Turn key to "Zahnrad" (Gear icon) position
   - Select external mode "EXT"
   - Turn key back to "Remote" (Fernbedienung)

7. **Verification:**
   - Robot should now be able to move again
   - All status indicators on Smartpad should be green

---

## Deutsche Version

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
   - Auf T1 gehen
   - Dann wieder zurück: Schlüssel drehen auf "Remote"

3. **Navigation öffnen:**
   - Links auf das blaue Zahnrad auf dem Touchbildschirm tippen
   - "Öffnen" klicken
   - Links auf das orangene "X" tippen
   - Es öffnet sich die Navigationsübersicht

4. **CNC Programm anwählen:**
   - Mehrere Dateien und Ordner sind angezeigt
   - "cnc" anklicken
   - Unten auf dem Touch Display auf "Anwählen" klicken

5. **Programm zurücksetzen:**
   - Oben auf dem Touch Screen des Smartpads beim gelben Quadrat "R" drauftippen
   - Es öffnet sich ein Fenster
   - "Programm zurücksetzen" klicken

6. **Zurück in externen Modus:**
   - Schlüssel umdrehen auf das Zahnrad
   - Externen Modus "EXT" auswählen
   - Schlüssel wieder umdrehen auf "Fernbedienung"

7. **Verifikation:**
   - Roboter sollte sich jetzt wieder bewegen können
   - Alle Status-Anzeigen auf dem Smartpad sollten grün sein

---

## Additional Notes / Zusätzliche Hinweise

### Prevention / Vorbeugung

To prevent this issue from recurring:
- Always ensure the CNC program is properly selected before starting operations
- Check that all status indicators are green before attempting to move the robot
- If the brake test warning appears, address it immediately before continuing

Um dieses Problem zu vermeiden:
- Immer sicherstellen, dass das CNC Programm vor dem Start korrekt angewählt ist
- Prüfen, dass alle Status-Anzeigen grün sind, bevor der Roboter bewegt wird
- Wenn die Bremstest-Warnung erscheint, diese sofort beheben, bevor fortgefahren wird

### Related Issues / Verwandte Probleme

If this solution does not resolve the issue, check:
- Robot controller connection
- Emergency stop status
- Safety system status
- Power supply to robot drives

Falls diese Lösung das Problem nicht behebt, prüfen:
- Robotersteuerungs-Verbindung
- Not-Aus-Status
- Sicherheitssystem-Status
- Stromversorgung der Roboterantriebe

---

*Last updated: 2025-12-11*
*Based on field experience with KUKA robot systems*







