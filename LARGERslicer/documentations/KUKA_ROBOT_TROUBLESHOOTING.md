# KUKA Robot Troubleshooting Guide

Recovery procedures for the Weber DXR25 with KUKA KRC5 / Smartpad.

> User-interface labels and controller messages are shown in the language displayed on the machine. Translations are provided where useful.

German version: [KUKA-Roboter: Fehlerbehebung](KUKA_ROBOT_TROUBLESHOOTING_DE.md).

## Select the matching issue

- [Robot not mastered: external axis E1](#issue-robot-not-mastered-e1)
- [Brake test / start movement does not begin](#issue-robot-does-not-perform-brake-test--start-movement-after-startup)
- [Emergency stop near the safety fence](#issue-emergency-stop-triggered-near-safety-fence-multiaxial-printing)
- [Robot does not respond / reference run does not begin](#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed)

## Safety and prerequisites

These procedures are intended for trained, authorized operators. Follow the operating instructions and the plant-specific safety procedures; do not bypass protective functions.

- Read the entire applicable procedure before starting.
- Keep the cell, robot travel path, and area around the tool clear.
- Perform manual recovery movements only in **T1**, with the enabling switch held. Keep the Smartpad in hand and the emergency stop within reach.
- Check the stop reason before resetting a program or attempting movement. A reset does not correct a safety fault or missing mastering.
- Return to **EXT** only after the cell is clear and the safety fence is closed.
- If the described state cannot be reached, stop and inform the person responsible for the plant.

## Issue: Robot Not Mastered (E1)

### Symptoms

The external axis **E1** is not mastered. The controller blocks movement and program start.

**Typical messages**

- **KSS01347:** Roboter nicht justiert (robot not mastered)
- **KSS00106:** Justage durchführen E1 (master E1)
- **KSS15047:** Justagereferenzierung angefordert (intern) (mastering reference requested internally)

The documented case also showed **KSS27003** and **KSS27004** (brake test required) and **KSS26228** (drive ID7 in simulation mode). The message log dated 19 October 2026 additionally records a mains phase failure (KSP-600-6M) and an external emergency stop. These are accompanying observations, not a confirmed cause.

**Consequence:** Ramp stop. Neither manual jogging, an automatic SAK run, nor program start is possible.

**Estimated time:** Approximately 15 minutes. Read all steps before beginning.

### Safety and prerequisites

This is the recovery documented for this specific installation, not a general E1 mastering procedure. Have the person responsible for the plant confirm that the backup is suitable before activating it.

- Follow the [general safety requirements](#safety-and-prerequisites). Keep the safety fence closed throughout this procedure.
- The procedure uses the **Experte** (Expert) and **Administrator** user groups. Passwords are deliberately omitted; consult the operating instructions, the person responsible for the plant, or Weber.
- Use the single project with **BACKUP** in its name, dated **2023**, in the project list. The other files dated July are **not** this backup.
- Check the project state before restoring it: changes made after the backup will no longer be active.
- There are two operating stations: use the **printer HMI** for drives and home position, and the **KRC5 Smartpad** for all other steps.

### Step 1: Establish the initial state, select T1, and log in as Expert

First place the system in the required initial state, then select T1 and the Expert user group.

1. **On the printer HMI:** acknowledge all error messages, set **Antriebe aus** (drives off) and **Grundstellung aus** (home position off). Do not select anything else.
2. Close the safety fence.
3. Take the Smartpad in hand.
4. Turn the Smartpad key switch to the **gear icon**. The operating-mode selection opens with **EXT** preselected.
5. Tap **T1**, then turn the key back.
6. Tap the **user button** on the left and log in as **Experte**.
7. Tap **R** in the top status bar and **deselect the program**.

### Step 2: Activate the backup project and restart the controller

Activate the backup project, then restart the controller PC with files reloaded.

1. Tap the **blue gear with robot** on the left. The active project is displayed.
2. Tap the **orange X** to open the overview.
3. Under **Verfügbare Projekte** (available projects), locate the projects and restore points. Select the **BACKUP project from 2023**, not the July files, and tap **Aktivieren** (activate).
4. Log in as **Administrator**.
5. Tap the **orange robot icon** at the top left, then select **Herunterfahren** (shutdown).
6. Under *Einstellung für das nächste Herunterfahren* (settings for the next shutdown), also select **Dateien neu einlesen** (reload files).
7. Under *Aktionen zum Herunterfahren* (shutdown actions), select **Steuerungs-PC neu starten** (restart controller PC).
8. Wait for the restart to finish.

### Step 3: Run the CNC program and return to automatic operation

For the documented installation, run **cnc** completely in T1 until **SAK erreicht** is displayed at the end of the recovery sequence. Do not reset the program again afterward. Here, SAK denotes block coincidence; it is not a general indication that an arbitrary program has finished.

1. Log in again as **Experte**.
2. In the file explorer (Navigator), open **R1 / Program**. If another view is open, tap the **orange X** to return to the Navigator. Select **cnc**.
3. Tap **Anwählen** (select) at the bottom. The program opens as a text view, ready to run.
4. Hold the **enabling switch** on the rear of the Smartpad. The display shows *Start-Taste erforderlich* (start key required).
5. Hold the **hardware start key** on the left of the device. The robot moves. Releasing either the start key or the enabling switch stops it.
6. Allow the complete recovery movement to finish; at the end, the display must show **SAK erreicht**. If the expected state is not reached, stop and inform the person responsible for the plant.
7. Turn the key switch to the **gear icon**, select **EXT**, then turn the key back.
8. **On the printer HMI:** select **Antrieb ein** (drive on), then **Grundstellung** (home position). The robot moves to its home position.

### Verification

Recovery is complete when **S, O, R, and Ext** in the top status bar are green and the robot is in its home position.

- **KSS01347 / Roboter nicht justiert** is no longer active.
- Movement and program start are enabled again.
- Resolve any remaining messages, including brake-test requirements, before resuming production.

If the mastering message remains, do not keep retrying or change mastering values. Inform the person responsible for the plant or Weber.

## Issue: Robot Does Not Perform Brake Test / Start Movement After Startup

### Symptoms

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

### Cause and prerequisites

The CNC program on the robot controller may need to be manually reselected. Follow the [general safety requirements](#safety-and-prerequisites); perform the following steps on the **Smartpad only**.

If **KSS01347 / KSS00106 E1** is active, use the [E1 recovery procedure](#issue-robot-not-mastered-e1) instead. Reselecting the program alone does not restore mastering.

### Step 1: Switch to T1 and open the Navigator

1. Turn the key from **Remote** to the **gear icon**.
2. Select **T1**, then turn the key back to **Remote**.
3. Tap the **blue gear icon** on the left of the touchscreen.
4. Tap **Öffnen** (open), then the **orange X** on the left. The navigation overview opens.

### Step 2: Select and reset the CNC program

1. Select **cnc** from the files and folders.
2. Tap **Anwählen** at the bottom of the touchscreen.
3. Tap the yellow **R** in the top status bar.
4. Select **Programm zurücksetzen** (reset program).

### Step 3: Return to external mode

1. Turn the key to the **gear icon**.
2. Select **EXT**.
3. Turn the key back to **Remote**.

### Verification

- All Smartpad status indicators should be green.
- The required brake test / start movement should be possible again.

If the robot still does not respond, check the current messages and continue with the [no-response procedure](#issue-robot-does-not-move--start-reference-or-calibration-run-cannot-be-executed). Do not continue production with an unresolved brake-test requirement.

## Issue: Emergency Stop Triggered Near Safety Fence (Multiaxial Printing)

### Symptoms

The robot stops abruptly when the tool or extruder approaches the safety fence.

- The brakes engage audibly with a cracking sound.
- The HMI or control cabinet shows safety-stop or brake-test-required messages.
- Automatic movement remains blocked until the robot is moved away from the protected area.

### Cause and prerequisites

On multiaxial paths, especially at **45-degree orientations**, TCP/extruder tilt can enter the protected braking zone before reaching the physical fence.

Follow the [general safety requirements](#safety-and-prerequisites). Read the actual stop reason on the **HMI, Smartpad, or control-cabinet display** before attempting recovery.

### Step 1: Switch to setup mode

1. Turn the key from **Remote** to the **gear icon**.
2. Check that **EXT** is selected, then switch to **T1**.
3. Turn the key back. The Smartpad may restart.

### Step 2: Move the robot away from the protected area

1. Hold a rear **enabling switch**.
2. Jog axis by axis, or use the SpaceMouse if enabled, until the TCP/extruder is clearly outside the protected area.
3. The motion icons turn green only while the enabling switch is held.

### Step 3: Return to automatic operation

1. Turn the key to the **gear icon**.
2. Switch from **T1** to **EXT**, then turn the key back to **Remote**.
3. If automatic start is still blocked, reselect **cnc** and run **Programm zurücksetzen** as described in the [startup procedure](#issue-robot-does-not-perform-brake-test--start-movement-after-startup).
4. If the controller requests referencing or mastering, resolve that message before continuing. For **E1 mastering messages**, use the [E1 procedure](#issue-robot-not-mastered-e1).

### Verification

- The TCP/extruder is clear of the protected area.
- Safety-stop messages are cleared and required brake tests are completed.
- The Smartpad status indicators are green and automatic operation is enabled again.

Check the path clearance before restarting the job. Do not repeatedly run the same path into the protected zone.

## Issue: Robot Does Not Move / Start, Reference, or Calibration Run Cannot Be Executed

### Symptoms

Use this procedure if safety-stop recovery is complete but the robot still does not respond.

- No movement although the job is loaded.
- Print aborts at **0,0,0** or a similar startup position.
- Brake test, reference run, or startup movement does not begin.
- The controller appears active but does not respond to the start command.

### Possible causes and prerequisites

- The CNC program is no longer correctly selected.
- The robot program / EMI state is inconsistent after an abort, manual jogging, or a mode change.
- External/internal control is not synchronized.
- The reference switch or startup reference is not reached or does not switch correctly.

Follow the [general safety requirements](#safety-and-prerequisites). If **KSS01347 / KSS00106 E1** is active, use the [E1 procedure](#issue-robot-not-mastered-e1), not repeated program resets.

### Step 1: Check status and reselect the program

1. Check the status indicators at the top right of the Smartpad. Follow active messages until drive enable is restored.
2. Open the Navigator, select **cnc**, and tap **Anwählen**.
3. Tap the yellow **R** and select **Programm zurücksetzen**.

### Step 2: Reset robot/CNC handover and check the mode

1. Under **PrgView**, reset the **EMI (External Motion Interface)**.
2. In the top frame bar, reset the robot program and restart it.
3. Check the operating mode and return to **EXT** for automatic operation.
4. If necessary, briefly switch between external/internal control, then restore the required operating mode.

### Step 3: Retry the required startup movement

1. Retry the brake test, reference run, or startup movement only after the program and control states have been reset.
2. If reference/calibration movement still fails, have qualified maintenance personnel check whether the reference switch changes both indicator lamps correctly in the reference position.
3. Any readjustment or re-mounting of the reference switch must follow the plant's maintenance and isolation procedures.

### Verification

- Smartpad status indicators are green.
- The required brake test, reference run, or startup movement completes.
- The robot responds to the start command again.

If the fault remains, stop and inform the person responsible for the plant rather than repeating resets.

---

## Prevention


To prevent recurring faults:

- Ensure **cnc** is correctly selected before starting.
- Check the status indicators and resolve brake-test warnings before continuing.
- Keep additional clearance near the fence; the protected braking zone can begin before the physical barrier.
- For 45-degree multiaxial prints, orient extruder tilt toward the wall or largest free area, away from the safety door.
- Keep an approved backup and record which project state it contains.

## If recovery fails

Have the responsible personnel check the robot-controller connection, emergency-stop and safety-system status, and power supply to the drives. Do not bypass safety functions or keep retrying movements with unresolved faults.

*Last updated: 2026-10-21*
*Based on field experience with KUKA robot systems; E1 recovery based on the supplied report dated 2026-10-20 and operator clarification.*
