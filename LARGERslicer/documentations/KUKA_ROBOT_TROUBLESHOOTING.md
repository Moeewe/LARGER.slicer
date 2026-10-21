# KUKA Robot Troubleshooting Guide / KUKA Roboter Fehlerbehebungsanleitung

Recovery procedures for the Weber DXR25 with KUKA KRC5 / Smartpad.
Fehlerbehebung für den Weber DXR25 mit KUKA KRC5 / Smartpad.

## English Version

### Select the matching issue

- [Robot not mastered: external axis E1](#issue-robot-not-mastered-e1)
- [Brake test / start movement does not begin](#issue-robot-does-not-perform-brake-test--start-movement-after-startup)
- [Emergency stop near the safety fence](#issue-emergency-stop-triggered-near-safety-fence-multiaxial-printing)
- [Robot does not respond / reference run does not begin](#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed)

### Safety and prerequisites

These procedures are intended for trained, authorized operators. Follow the operating instructions and the plant-specific safety procedures; do not bypass protective functions.

- Read the entire applicable procedure before starting.
- Keep the cell, robot travel path, and area around the tool clear.
- Perform manual recovery movements only in **T1**, with the enabling switch held. Keep the Smartpad in hand and the emergency stop within reach.
- Check the stop reason before resetting a program or attempting movement. A reset does not correct a safety fault or missing mastering.
- Return to **EXT** only after the cell is clear and the safety fence is closed.
- If the described state cannot be reached, stop and inform the person responsible for the plant.

### Issue: Robot Not Mastered (E1)

#### Symptoms

The external axis **E1** is not mastered. The controller blocks movement and program start.

**Typical messages**

- **KSS01347:** Roboter nicht justiert (robot not mastered)
- **KSS00106:** Justage durchführen E1 (master E1)
- **KSS15047:** Justagereferenzierung angefordert (intern) (mastering reference requested internally)

The documented case also showed **KSS27003** and **KSS27004** (brake test required) and **KSS26228** (drive ID7 in simulation mode). The message log dated 19 October 2026 additionally records a mains phase failure (KSP-600-6M) and an external emergency stop. These are accompanying observations, not a confirmed cause.

**Consequence:** Ramp stop. Neither manual jogging, an automatic SAK run, nor program start is possible.

**Estimated time:** Approximately 15 minutes. Read all steps before beginning.

#### Safety and prerequisites

This is the recovery documented for this specific installation, not a general E1 mastering procedure. Have the person responsible for the plant confirm that the backup is suitable before activating it.

- Follow the [general safety requirements](#safety-and-prerequisites). Keep the safety fence closed throughout this procedure.
- The procedure uses the **Experte** (Expert) and **Administrator** user groups. Passwords are deliberately omitted; consult the operating instructions, the person responsible for the plant, or Weber.
- Use the single project with **BACKUP** in its name, dated **2023**, in the project list. The other files dated July are **not** this backup.
- Check the project state before restoring it: changes made after the backup will no longer be active.
- There are two operating stations: use the **printer HMI** for drives and home position, and the **KRC5 Smartpad** for all other steps.

#### Step 1: Establish the initial state, select T1, and log in as Expert

First place the system in the required initial state, then select T1 and the Expert user group.

1. **On the printer HMI:** acknowledge all error messages, set **Antriebe aus** (drives off) and **Grundstellung aus** (home position off). Do not select anything else.
2. Close the safety fence.
3. Take the Smartpad in hand.
4. Turn the Smartpad key switch to the **gear icon**. The operating-mode selection opens with **EXT** preselected.
5. Tap **T1**, then turn the key back.
6. Tap the **user button** on the left and log in as **Experte**.
7. Tap **R** in the top status bar and **deselect the program**.

#### Step 2: Activate the backup project and restart the controller

Activate the backup project, then restart the controller PC with files reloaded.

1. Tap the **blue gear with robot** on the left. The active project is displayed.
2. Tap the **orange X** to open the overview.
3. Under **Verfügbare Projekte** (available projects), locate the projects and restore points. Select the **BACKUP project from 2023**, not the July files, and tap **Aktivieren** (activate).
4. Log in as **Administrator**.
5. Tap the **orange robot icon** at the top left, then select **Herunterfahren** (shutdown).
6. Under *Einstellung für das nächste Herunterfahren* (settings for the next shutdown), also select **Dateien neu einlesen** (reload files).
7. Under *Aktionen zum Herunterfahren* (shutdown actions), select **Steuerungs-PC neu starten** (restart controller PC).
8. Wait for the restart to finish.

#### Step 3: Run the CNC program and return to automatic operation

For the documented installation, run **cnc** completely in T1 until **SAK erreicht** is displayed at the end of the recovery sequence. Do not reset the program again afterward. Here, SAK denotes block coincidence; it is not a general indication that an arbitrary program has finished.

1. Log in again as **Experte**.
2. In the file explorer (Navigator), open **R1 / Program**. If another view is open, tap the **orange X** to return to the Navigator. Select **cnc**.
3. Tap **Anwählen** (select) at the bottom. The program opens as a text view, ready to run.
4. Hold the **enabling switch** on the rear of the Smartpad. The display shows *Start-Taste erforderlich* (start key required).
5. Hold the **hardware start key** on the left of the device. The robot moves. Releasing either the start key or the enabling switch stops it.
6. Allow the complete recovery movement to finish; at the end, the display must show **SAK erreicht**. If the expected state is not reached, stop and inform the person responsible for the plant.
7. Turn the key switch to the **gear icon**, select **EXT**, then turn the key back.
8. **On the printer HMI:** select **Antrieb ein** (drive on), then **Grundstellung** (home position). The robot moves to its home position.

#### Verification

Recovery is complete when **S, O, R, and Ext** in the top status bar are green and the robot is in its home position.

- **KSS01347 / Roboter nicht justiert** is no longer active.
- Movement and program start are enabled again.
- Resolve any remaining messages, including brake-test requirements, before resuming production.

If the mastering message remains, do not keep retrying or change mastering values. Inform the person responsible for the plant or Weber.

### Issue: Robot Does Not Perform Brake Test / Start Movement After Startup

#### Symptoms

The robot does not perform the required brake test or start movement and cannot reach its home position.

**Typical observations**

- The drive button blinks continuously.
- The print aborts with **Druck abgebrochen bei XYZ jeweils 0**.
- The control-cabinet display shows **Neu Starten** or **Warnschwelle für Bremstest erreicht mit 0 Stunden Restlaufzeit**.
- The corresponding indicator light is red.

**Smartpad messages and status**

- **Quitt Fahrtfreigabe gesamt Verursacher KS**
- **Active-Status erforderlich**
- **S, O, R, Ext** may show green, gray, yellow, green instead of all green.

#### Cause and prerequisites

The CNC program on the robot controller may need to be manually reselected. Follow the [general safety requirements](#safety-and-prerequisites); perform the following steps on the **Smartpad only**.

If **KSS01347 / KSS00106 E1** is active, use the [E1 recovery procedure](#issue-robot-not-mastered-e1) instead. Reselecting the program alone does not restore mastering.

#### Step 1: Switch to T1 and open the Navigator

1. Turn the key from **Remote** to the **gear icon**.
2. Select **T1**, then turn the key back to **Remote**.
3. Tap the **blue gear icon** on the left of the touchscreen.
4. Tap **Öffnen** (open), then the **orange X** on the left. The navigation overview opens.

#### Step 2: Select and reset the CNC program

1. Select **cnc** from the files and folders.
2. Tap **Anwählen** at the bottom of the touchscreen.
3. Tap the yellow **R** in the top status bar.
4. Select **Programm zurücksetzen** (reset program).

#### Step 3: Return to external mode

1. Turn the key to the **gear icon**.
2. Select **EXT**.
3. Turn the key back to **Remote**.

#### Verification

- All Smartpad status indicators should be green.
- The required brake test / start movement should be possible again.

If the robot still does not respond, check the current messages and continue with the [no-response procedure](#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed). Do not continue production with an unresolved brake-test requirement.

### Issue: Emergency Stop Triggered Near Safety Fence (Multiaxial Printing)

#### Symptoms

The robot stops abruptly when the tool or extruder approaches the safety fence.

- The brakes engage audibly with a cracking sound.
- The HMI or control cabinet shows safety-stop or brake-test-required messages.
- Automatic movement remains blocked until the robot is moved away from the protected area.

#### Cause and prerequisites

On multiaxial paths, especially at **45-degree orientations**, TCP/extruder tilt can enter the protected braking zone before reaching the physical fence.

Follow the [general safety requirements](#safety-and-prerequisites). Read the actual stop reason on the **HMI, Smartpad, or control-cabinet display** before attempting recovery.

#### Step 1: Switch to setup mode

1. Turn the key from **Remote** to the **gear icon**.
2. Check that **EXT** is selected, then switch to **T1**.
3. Turn the key back. The Smartpad may restart.

#### Step 2: Move the robot away from the protected area

1. Hold a rear **enabling switch**.
2. Jog axis by axis, or use the SpaceMouse if enabled, until the TCP/extruder is clearly outside the protected area.
3. The motion icons turn green only while the enabling switch is held.

#### Step 3: Return to automatic operation

1. Turn the key to the **gear icon**.
2. Switch from **T1** to **EXT**, then turn the key back to **Remote**.
3. If automatic start is still blocked, reselect **cnc** and run **Programm zurücksetzen** as described in the [startup procedure](#issue-robot-does-not-perform-brake-test--start-movement-after-startup).
4. If the controller requests referencing or mastering, resolve that message before continuing. For **E1 mastering messages**, use the [E1 procedure](#issue-robot-not-mastered-e1).

#### Verification

- The TCP/extruder is clear of the protected area.
- Safety-stop messages are cleared and required brake tests are completed.
- The Smartpad status indicators are green and automatic operation is enabled again.

Check the path clearance before restarting the job. Do not repeatedly run the same path into the protected zone.

### Issue: Robot Does Not Move / Start, Reference, or Calibration Run Cannot Be Executed

#### Symptoms

Use this procedure if safety-stop recovery is complete but the robot still does not respond.

- No movement although the job is loaded.
- Print aborts at **0,0,0** or a similar startup position.
- Brake test, reference run, or startup movement does not begin.
- The controller appears active but does not respond to the start command.

#### Possible causes and prerequisites

- The CNC program is no longer correctly selected.
- The robot program / EMI state is inconsistent after an abort, manual jogging, or a mode change.
- External/internal control is not synchronized.
- The reference switch or startup reference is not reached or does not switch correctly.

Follow the [general safety requirements](#safety-and-prerequisites). If **KSS01347 / KSS00106 E1** is active, use the [E1 procedure](#issue-robot-not-mastered-e1), not repeated program resets.

#### Step 1: Check status and reselect the program

1. Check the status indicators at the top right of the Smartpad. Follow active messages until drive enable is restored.
2. Open the Navigator, select **cnc**, and tap **Anwählen**.
3. Tap the yellow **R** and select **Programm zurücksetzen**.

#### Step 2: Reset robot/CNC handover and check the mode

1. Under **PrgView**, reset the **EMI (External Motion Interface)**.
2. In the top frame bar, reset the robot program and restart it.
3. Check the operating mode and return to **EXT** for automatic operation.
4. If necessary, briefly switch between external/internal control, then restore the required operating mode.

#### Step 3: Retry the required startup movement

1. Retry the brake test, reference run, or startup movement only after the program and control states have been reset.
2. If reference/calibration movement still fails, have qualified maintenance personnel check whether the reference switch changes both indicator lamps correctly in the reference position.
3. Any readjustment or re-mounting of the reference switch must follow the plant's maintenance and isolation procedures.

#### Verification

- Smartpad status indicators are green.
- The required brake test, reference run, or startup movement completes.
- The robot responds to the start command again.

If the fault remains, stop and inform the person responsible for the plant rather than repeating resets.

---

## Deutsche Version

### Passendes Fehlerbild auswählen

- [Roboter nicht justiert: externe Achse E1](#problem-roboter-nicht-justiert-e1)
- [Bremstest / Startbewegung beginnt nicht](#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus)
- [Notstopp nahe Sicherheitszaun](#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck)
- [Roboter reagiert nicht / Referenzfahrt beginnt nicht](#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt)

### Sicherheit und Voraussetzungen

Die Abläufe richten sich an eingewiesenes, autorisiertes Bedienpersonal. Betriebsanweisung und anlagenspezifische Sicherheitsvorgaben beachten; keine Schutzfunktionen umgehen.

- Den jeweils passenden Ablauf vor Beginn vollständig lesen.
- Zelle und Fahrweg des Roboters frei halten, auch rund um das Werkzeug.
- Manuelle Wiederanlauffahrten nur in **T1** mit gedrücktem Zustimmschalter ausführen. Smartpad in der Hand halten, NOT-HALT griffbereit.
- Vor Programm-Reset oder Bewegung die Stoppursache prüfen. Ein Reset behebt weder einen Sicherheitsfehler noch eine fehlende Justage.
- Erst in **EXT** zurückkehren, wenn die Zelle frei und der Sicherheitszaun geschlossen ist.
- Wird der beschriebene Zustand nicht erreicht, abbrechen und den Anlagenverantwortlichen informieren.

### Problem: Roboter nicht justiert (E1)

#### Fehlerbild

Der Roboter fährt nicht mehr, weil die externe Achse **E1** nicht justiert ist. Die Steuerung sperrt jede Bewegung und jeden Programmstart.

**Typische Meldungen**

- **KSS01347:** Roboter nicht justiert
- **KSS00106:** Justage durchführen E1
- **KSS15047:** Justagereferenzierung angefordert (intern)

Begleitend traten im dokumentierten Fall **KSS27003** und **KSS27004** (Bremsentest erforderlich) sowie **KSS26228** (Antrieb ID7 im Simulationsmodus) auf. Im Meldungsprotokoll vom 19.10.2026 stehen außerdem Netzphasenausfall (KSP-600-6M) und NOT-HALT extern. Das sind Begleitbeobachtungen, keine bestätigte Ursache.

**Konsequenz:** Rampenstopp. Weder manuelles Verfahren noch eine automatisierte SAK-Fahrt noch ein Programmstart sind möglich.

**Aufwand:** Etwa 15 Minuten. Die Anleitung vor Beginn vollständig lesen.

#### Sicherheit und Voraussetzungen

Dies ist der dokumentierte Wiederanlauf dieser konkreten Anlage, keine allgemeine Anleitung zur E1-Justage. Vor dem Aktivieren muss der Anlagenverantwortliche bestätigen, dass das Backup zur Anlage passt.

- Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Der Sicherheitszaun bleibt während dieses Ablaufs geschlossen.
- Die Anleitung nutzt die Benutzergruppen **Experte** und **Administrator**. Passwörter stehen bewusst nicht in diesem Dokument; Betriebsanweisung lesen oder beim Anlagenverantwortlichen bzw. Hersteller Weber erfragen.
- In der Projektliste das einzige Projekt mit **BACKUP** im Namen aus **2023** verwenden. Die anderen Dateien vom Juli sind **nicht** dieses Backup.
- Vorher den Projektstand prüfen: Änderungen nach dem Backup sind nach dem Einspielen nicht mehr aktiv.
- Zwei Bedienstellen: Antriebe und Grundstellung am **HMI des Druckers**, alles andere am **KRC5-Smartpad**.

#### Schritt 1: Ausgangszustand herstellen, T1 und Anmeldung als Experte

Zuerst die Anlage in den erforderlichen Ausgangszustand bringen und das Smartpad auf T1 und Benutzergruppe Experte stellen.

1. **Am HMI des Druckers:** alle Fehlermeldungen quittieren, **Antriebe aus**, **Grundstellung aus**. Nichts weiter anwählen.
2. Sicherheitszaun schließen.
3. Smartpad in die Hand nehmen.
4. Schlüsselschalter am Smartpad auf das **Zahnradsymbol** drehen. Die Betriebsartenwahl öffnet sich, **EXT** ist vorausgewählt.
5. **T1** antippen, danach den Schlüssel wieder zurückdrehen.
6. Links am Rand den **Benutzer-Button** antippen und als **Experte** anmelden.
7. Oben in der Statusleiste auf **R** tippen und das **Programm abwählen**.

#### Schritt 2: Backup-Projekt aktivieren und Steuerung neu starten

Das Backup-Projekt aktivieren, danach den Steuerungs-PC mit neu eingelesenen Dateien neu starten.

1. Links auf das **blaue Zahnrad mit Roboter** tippen. Das aktive Projekt wird angezeigt.
2. Das **orangene X** antippen. Es erscheint eine Übersicht.
3. Unter **Verfügbare Projekte** stehen die Projekte und Wiederherstellungspunkte. Das **BACKUP-Projekt aus 2023**, nicht die Juli-Dateien, wählen und **aktivieren**.
4. Als **Administrator** anmelden.
5. Oben links auf das **orangene Roboter-Symbol** tippen, dann **Herunterfahren** wählen.
6. Unter *Einstellung für das nächste Herunterfahren* zusätzlich **Dateien neu einlesen** anhaken.
7. Unter *Aktionen zum Herunterfahren* **Steuerungs-PC neu starten** wählen.
8. Den Neustart abwarten.

#### Schritt 3: CNC-Programm fahren und in den Automatikbetrieb zurückkehren

Bei der dokumentierten Anlage das Programm **cnc** in T1 vollständig durchfahren, bis am Ende des Wiederanlaufs **SAK erreicht** angezeigt wird. Danach das Programm **nicht erneut zurücksetzen**. SAK bezeichnet die Satzkoinzidenz und ist kein allgemeines Programmende-Signal für beliebige Programme.

1. Wieder als **Experte** anmelden.
2. Im Dateiexplorer (Navigator) den Ordner **R1 / Program** öffnen. Ist etwas anderes angezeigt, das **orangene X** antippen, um zum Navigator zurückzukehren. **cnc** auswählen.
3. Unten auf **Anwählen** tippen. Das Programm öffnet sich als Textfeld und ist abspielbereit.
4. Hinten am Smartpad den **Zustimmschalter** (Totmannschalter) gedrückt halten. Oben im Display steht *Start-Taste erforderlich*.
5. Links am Gerät die **Hardware-Starttaste gedrückt halten**. Der Roboter bewegt sich. Wird die Starttaste oder der Zustimmschalter losgelassen, hält er an.
6. Die komplette Wiederanlauffahrt durchlaufen lassen; am Ende muss **SAK erreicht** angezeigt werden. Wird dieser Zustand nicht erreicht, abbrechen und den Anlagenverantwortlichen informieren.
7. Schlüsselschalter auf das **Zahnradsymbol** drehen, **EXT** wählen, Schlüssel zurückdrehen.
8. **Am HMI des Druckers:** **Antrieb ein**, dann **Grundstellung**. Der Roboter fährt in die Grundstellung.

#### Kontrolle

Die Behebung ist gelungen, wenn **S, O, R und Ext** oben in der Statusleiste grün sind und der Roboter in der Grundstellung steht.

- **KSS01347 / Roboter nicht justiert** steht nicht mehr an.
- Bewegungen und Programmstart sind wieder freigegeben.
- Verbleibende Meldungen, einschließlich erforderlicher Bremsentests, vor Produktionsbeginn abarbeiten.

Bleibt die Justagemeldung bestehen, nicht weiter probieren und keine Justagewerte verändern. Den Anlagenverantwortlichen oder Weber informieren.

### Problem: Roboter führt Bremstest / Startbewegung nach dem Start nicht aus

#### Fehlerbild

Der Roboter führt den erforderlichen Bremstest oder die Startbewegung nicht aus und kann nicht in Grundstellung fahren.

**Typische Beobachtungen**

- Der Antriebe-Knopf blinkt dauerhaft.
- Der Druck bricht mit **Druck abgebrochen bei XYZ jeweils 0** ab.
- Das Schaltschrankdisplay zeigt **Neu Starten** oder **Warnschwelle für Bremstest erreicht mit 0 Stunden Restlaufzeit**.
- Die entsprechende Anzeigeleuchte leuchtet rot.

**Smartpad-Meldungen und Status**

- **Quitt Fahrtfreigabe gesamt Verursacher KS**
- **Active-Status erforderlich**
- **S, O, R, Ext** zeigen beispielsweise Grün, Grau, Gelb, Grün statt durchgehend Grün.

#### Ursache und Voraussetzungen

Das CNC-Programm der Robotersteuerung muss möglicherweise manuell neu angewählt werden. Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten; die folgenden Schritte erfolgen ausschließlich am **Smartpad**.

Steht **KSS01347 / KSS00106 E1** an, stattdessen die [E1-Fehlerbehebung](#problem-roboter-nicht-justiert-e1) verwenden. Eine erneute Programmanwahl allein stellt die Justage nicht wieder her.

#### Schritt 1: T1 wählen und Navigator öffnen

1. Schlüssel von **Remote** auf das **Zahnradsymbol** drehen.
2. **T1** auswählen, dann den Schlüssel zurück auf **Remote** drehen.
3. Links auf das **blaue Zahnrad** am Touchbildschirm tippen.
4. **Öffnen** antippen, danach links auf das **orangene X** tippen. Die Navigationsübersicht öffnet sich.

#### Schritt 2: CNC-Programm anwählen und zurücksetzen

1. In der Dateiübersicht **cnc** auswählen.
2. Unten auf **Anwählen** tippen.
3. Oben in der Statusleiste das gelbe **R** antippen.
4. **Programm zurücksetzen** auswählen.

#### Schritt 3: In den externen Modus zurückkehren

1. Schlüssel auf das **Zahnradsymbol** drehen.
2. **EXT** auswählen.
3. Schlüssel zurück auf **Remote** drehen.

#### Kontrolle

- Alle Statusanzeigen am Smartpad sollten grün sein.
- Der erforderliche Bremstest bzw. die Startbewegung sollte wieder möglich sein.

Reagiert der Roboter weiterhin nicht, die aktuellen Meldungen prüfen und mit dem [Ablauf bei fehlender Reaktion](#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) fortfahren. Nicht mit offener Bremstest-Anforderung weiterproduzieren.

### Problem: Notstop nahe Sicherheitszaun (multiaxialer Druck)

#### Fehlerbild

Der Roboter stoppt abrupt, sobald Werkzeug oder Extruder in zaunnahe Bereiche kommt.

- Die Bremsen greifen hörbar mit einem Knacken.
- HMI oder Schaltschrank zeigt Sicherheitsstopp- oder Bremstest-Meldungen.
- Die Automatikfahrt bleibt blockiert, bis der Roboter aus dem Schutzbereich gefahren wurde.

#### Ursache und Voraussetzungen

Bei multiaxialen Bahnen, insbesondere bei **45-Grad-Ausrichtungen**, kann die TCP-/Extruder-Neigung schon vor dem physischen Zaun in den geschützten Bremsbereich geraten.

Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Vor dem Wiederanlauf die tatsächliche Stoppursache am **HMI, Smartpad oder Schaltschrankdisplay** lesen.

#### Schritt 1: In den Einrichtbetrieb wechseln

1. Schlüssel von **Remote** auf das **Zahnradsymbol** drehen.
2. Prüfen, dass **EXT** angewählt ist, dann auf **T1** wechseln.
3. Schlüssel zurückdrehen. Das Smartpad kann neu starten.

#### Schritt 2: Roboter aus dem Schutzbereich fahren

1. Einen **Zustimmschalter** auf der Rückseite gedrückt halten.
2. Achsweise verfahren oder, falls freigegeben, die SpaceMouse verwenden, bis TCP/Extruder klar außerhalb des Schutzbereichs ist.
3. Die Verfahr-Icons werden nur bei gedrücktem Zustimmschalter grün.

#### Schritt 3: In den Automatikbetrieb zurückkehren

1. Schlüssel auf das **Zahnradsymbol** drehen.
2. Von **T1** auf **EXT** wechseln, dann den Schlüssel zurück auf **Remote** drehen.
3. Bleibt der Autostart blockiert, **cnc** erneut anwählen und **Programm zurücksetzen** ausführen, wie im [Startablauf](#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) beschrieben.
4. Fordert die Steuerung eine Referenzierung oder Justage an, diese Meldung vor dem Fortfahren abarbeiten. Bei **E1-Justagemeldungen** die [E1-Anleitung](#problem-roboter-nicht-justiert-e1) verwenden.

#### Kontrolle

- TCP/Extruder befindet sich außerhalb des Schutzbereichs.
- Sicherheitsstopp-Meldungen sind behoben und erforderliche Bremstests abgeschlossen.
- Die Statusanzeigen am Smartpad sind grün und der Automatikbetrieb ist wieder freigegeben.

Vor erneutem Jobstart den Bahnabstand prüfen. Nicht wiederholt dieselbe Bahn in den Schutzbereich fahren.

### Problem: Roboter reagiert nicht / Start-, Referenz- oder Justagefahrt wird nicht ausgeführt

#### Fehlerbild

Diesen Ablauf nutzen, wenn die Sicherheitsstopp-Wiederfreigabe abgeschlossen ist, der Roboter aber weiterhin nicht reagiert.

- Keine Bewegung, obwohl der Job geladen ist.
- Druckabbruch bei **0,0,0** oder einer ähnlichen Startposition.
- Bremstest, Referenzfahrt oder Startbewegung beginnt nicht.
- Die Steuerung wirkt aktiv, reagiert aber nicht auf den Startbefehl.

#### Mögliche Ursachen und Voraussetzungen

- Das CNC-Programm ist nicht mehr korrekt angewählt.
- Roboterprogramm-/EMI-Zustand ist nach Abbruch, Handverfahren oder Moduswechsel inkonsistent.
- Externe/interne Steuerung ist nicht synchronisiert.
- Referenzschalter oder Startreferenz wird nicht erreicht oder schaltet nicht korrekt.

Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Steht **KSS01347 / KSS00106 E1** an, die [E1-Anleitung](#problem-roboter-nicht-justiert-e1) verwenden, statt wiederholt Programme zurückzusetzen.

#### Schritt 1: Status prüfen und Programm neu anwählen

1. Die Statusanzeigen oben rechts am Smartpad prüfen. Aktive Meldungen abarbeiten, bis die Fahrfreigabe wiederhergestellt ist.
2. Navigator öffnen, **cnc** auswählen und **Anwählen** antippen.
3. Das gelbe **R** antippen und **Programm zurücksetzen** auswählen.

#### Schritt 2: Roboter/CNC-Übergabe zurücksetzen und Betriebsart prüfen

1. Unter **PrgView** das **EMI (External Motion Interface)** zurücksetzen.
2. Oben in der Rahmenleiste das Roboterprogramm zurücksetzen und neu starten.
3. Die Betriebsart prüfen und für den Automatikbetrieb auf **EXT** zurückkehren.
4. Falls nötig, kurz zwischen externer/interner Steuerung wechseln und anschließend die erforderliche Betriebsart wiederherstellen.

#### Schritt 3: Erforderliche Startbewegung erneut ausführen

1. Erst nach dem Reset von Programm- und Steuerungszustand den Bremstest, die Referenzfahrt oder die Startbewegung erneut auslösen.
2. Schlägt die Referenz-/Justagefahrt weiterhin fehl, durch qualifiziertes Instandhaltungspersonal prüfen lassen, ob der Referenzschalter in Referenzposition beide Kontrolllampen korrekt schaltet.
3. Nachjustieren oder erneutes Befestigen des Referenzschalters nur nach den anlagenspezifischen Instandhaltungs- und Freischaltvorgaben durchführen lassen.

#### Kontrolle

- Die Statusanzeigen am Smartpad sind grün.
- Der erforderliche Bremstest, die Referenzfahrt oder die Startbewegung wird abgeschlossen.
- Der Roboter reagiert wieder auf den Startbefehl.

Bleibt der Fehler bestehen, abbrechen und den Anlagenverantwortlichen informieren, statt weitere Resets zu wiederholen.

---

## Additional Notes / Zusätzliche Hinweise

### Prevention / Vorbeugung

To prevent recurring faults:

- Ensure **cnc** is correctly selected before starting.
- Check the status indicators and resolve brake-test warnings before continuing.
- Keep additional clearance near the fence; the protected braking zone can begin before the physical barrier.
- For 45-degree multiaxial prints, orient extruder tilt toward the wall or largest free area, away from the safety door.
- Keep an approved backup and record which project state it contains.

Um wiederkehrende Fehler zu vermeiden:

- Vor dem Start sicherstellen, dass **cnc** korrekt angewählt ist.
- Statusanzeigen prüfen und Bremstest-Warnungen vor dem Fortfahren abarbeiten.
- Zusätzlichen Abstand zum Zaun einplanen; der geschützte Bremsbereich kann vor der physischen Barriere beginnen.
- Bei 45-Grad-Multiaxialdrucken den Extruder zur Wand bzw. zum größten freien Bereich neigen, weg von der Sicherheitstür.
- Ein freigegebenes Backup bereithalten und dessen Projektstand dokumentieren.

### If recovery fails / Wenn die Behebung nicht gelingt

Have the responsible personnel check the robot-controller connection, emergency-stop and safety-system status, and power supply to the drives. Do not bypass safety functions or keep retrying movements with unresolved faults.

Robotersteuerungs-Verbindung, NOT-HALT- und Sicherheitssystem-Status sowie Stromversorgung der Antriebe durch die zuständigen Personen prüfen lassen. Keine Schutzfunktionen umgehen und Bewegungen nicht mit ungeklärten Fehlern wiederholt starten.

---

*Last updated / Zuletzt aktualisiert: 2026-10-21*
*Based on field experience with KUKA robot systems; E1 recovery based on the supplied report dated 2026-10-20 and operator clarification.*
*Grundlage: Praxiserfahrungen mit KUKA-Robotersystemen; E1-Wiederanlauf nach dem bereitgestellten Bericht vom 20.10.2026 und ergänzender Bedienerklärung.*
