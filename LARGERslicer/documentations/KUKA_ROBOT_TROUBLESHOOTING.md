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

### Issue: Emergency Stop Triggered Near Safety Fence (Multiaxial Printing)

**Symptoms:**
- Robot performs an abrupt stop when the tool/extruder reaches fence-near areas
- Audible brake engagement ("cracking" noise), robot is set out of operation
- HMI / control-cabinet display shows safety-related stop or brake-test-required messages
- Robot cannot continue automatic movement until manually moved away from protected zone

**Root Cause:**
In multiaxial paths (especially 45 degree orientations), TCP/extruder tilt can enter the protected braking zone before the physical fence.

**Recovery Procedure (KRC5 / Smartpad):**

1. **Read status message first:**
   - Confirm current stop reason on HMI/Smartpad or cabinet display.

2. **Switch into setup path:**
   - Turn key from "Remote" to "Zahnrad" (Gear icon).
   - Ensure mode is "EXT", then switch to "T1".
   - Turn key back; Smartpad may restart.

3. **Manually move robot out of safety zone:**
   - Press and hold a rear dead-man switch (Totmannschalter).
   - Move axis-by-axis (or SpaceMouse if enabled) until TCP/extruder is clearly outside the protected area.
   - Motion icons turn green only while dead-man is actively pressed.

4. **Return to automatic operation:**
   - Turn key to "Zahnrad" again.
   - Switch from "T1" back to "EXT".
   - Turn key back to "Remote" (operating mode).

5. **If automatic start still fails:**
   - Re-open navigation and reselect "cnc" program.
   - Run "Programm zurücksetzen".
   - Re-reference robot if requested by controller.

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

### Problem: Notstop nahe Sicherheitszaun (multiaxialer Druck)

**Symptome:**
- Roboter macht einen abrupten Stopp, sobald Tool/Extruder in zaunnahe Bereiche kommt
- Bremsen sind hörbar ("Knacken"), der Roboter wird außer Betrieb gesetzt
- HMI / Schaltschrank zeigt sicherheitsbezogene Stopps oder Bremstest-Hinweise
- Automatische Bewegung ist blockiert, bis der Roboter manuell aus dem Schutzbereich gefahren wird

**Ursache:**
Bei multiaxialen Bahnen (insbesondere 45-Grad-Ausrichtung) kann die TCP-/Extruder-Neigung bereits vor dem physischen Zaun in den Sicherheits- bzw. Bremsbereich geraten.

**Wiederanlauf (KRC5 / Smartpad):**

1. **Statusmeldung zuerst lesen:**
   - Stop-Ursache auf HMI/Smartpad oder Schaltschrank prüfen.

2. **In den Einrichtweg wechseln:**
   - Schlüssel von "Remote" auf "Zahnrad" drehen.
   - Sicherstellen, dass "EXT" anliegt, dann auf "T1" wechseln.
   - Schlüssel zurückdrehen; Smartpad kann neu starten.

3. **Roboter manuell aus dem Sicherheitsbereich fahren:**
   - Einen Totmannschalter auf der Rückseite gedrückt halten.
   - Achsweise verfahren (oder SpaceMouse, falls freigegeben), bis TCP/Extruder klar außerhalb des Schutzbereichs ist.
   - Verfahr-Icons werden nur bei gedrücktem Totmannschalter grün.

4. **Zurück in den Automatikbetrieb:**
   - Schlüssel wieder auf "Zahnrad".
   - Von "T1" zurück auf "EXT" wechseln.
   - Schlüssel zurück auf "Remote" (Betriebsmodus).

5. **Falls Autostart weiter blockiert ist:**
   - Navigation öffnen und "cnc" Programm neu anwählen.
   - "Programm zurücksetzen" ausführen.
   - Roboter bei Aufforderung neu referenzieren.

---

## Additional Notes / Zusätzliche Hinweise

### Prevention / Vorbeugung

To prevent this issue from recurring:
- Always ensure the CNC program is properly selected before starting operations
- Check that all status indicators are green before attempting to move the robot
- If the brake test warning appears, address it immediately before continuing
- Keep extra clearance near the fence; protected braking zone can begin before the physical barrier
- For 45 degree multiaxial prints, orient extruder tilt toward the wall / largest free area (away from safety door side)

Um dieses Problem zu vermeiden:
- Immer sicherstellen, dass das CNC Programm vor dem Start korrekt angewählt ist
- Prüfen, dass alle Status-Anzeigen grün sind, bevor der Roboter bewegt wird
- Wenn die Bremstest-Warnung erscheint, diese sofort beheben, bevor fortgefahren wird
- Zusätzlichen Abstand zum Zaun einplanen; der Sicherheits-/Bremsbereich beginnt vor der physischen Barriere
- 45-Grad-Multiaxialdrucke so ausrichten, dass der Extruder zur Wand bzw. in den größten freien Bereich kippt (weg von der Sicherheits-/Eingangstür)

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

*Last updated: 2026-08-04*
*Based on field experience with KUKA robot systems*







