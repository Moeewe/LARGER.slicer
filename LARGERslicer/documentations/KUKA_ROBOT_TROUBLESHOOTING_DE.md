# KUKA-Roboter: Fehlerbehebung

Wiederherstellungsabläufe für den Weber DXR25 mit KUKA KRC5 / Smartpad.

[Zur Roboter-Bedienanleitung: Einschalten, Datei laden und drucken](<../../EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md#einstieg-vom-einschalten-zum-druck>).

> Bedienelemente und Steuerungsmeldungen sind in der Sprache wiedergegeben, in der sie an der Anlage erscheinen. Wo hilfreich, steht die Übersetzung dabei.

Englische Version: [KUKA Robot Troubleshooting Guide](KUKA_ROBOT_TROUBLESHOOTING.md).

## Passendes Fehlerbild auswählen

„Keine Bewegung“ allein ist keine eindeutige Fehlerursache. Zuerst Meldungen, Sicherheitsfreigabe und Betriebsart prüfen; die [Fehlersuche in der Bedienanleitung](<../../EXAMPLE FILES/00 - WEBER - GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md#roboter-bewegt-sich-nicht-status-und-startfreigabe-prüfen>) hilft bei der Zuordnung. Danach den passenden Ablauf vollständig lesen.

- [Roboter reagiert nicht / Referenzfahrt beginnt nicht](#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt)
- [Bremstest / Startbewegung beginnt nicht](#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus)
- [Notstopp nahe Sicherheitszaun](#problem-notstop-nahe-sicherheitszaun-multiaxialer-druck)
- [Seltener Sonderfall: Roboter nicht justiert, externe Achse E1](#problem-roboter-nicht-justiert-e1)

Nur bei ausdrücklichen **E1-Justagemeldungen** (**KSS01347**, **KSS00106 E1**, **KSS15047**) den E1-Ablauf verwenden; eine fehlende Justage lässt sich nicht durch wiederholte Programm-Resets beheben.

## Sicherheit und Voraussetzungen

Die Abläufe richten sich an eingewiesenes, autorisiertes Bedienpersonal. Betriebsanweisung und anlagenspezifische Sicherheitsvorgaben beachten; keine Schutzfunktionen umgehen.

- Den jeweils passenden Ablauf vor Beginn vollständig lesen.
- Zelle und Fahrweg des Roboters frei halten, auch rund um das Werkzeug.
- Manuelle Wiederanlauffahrten nur in **T1** mit gedrücktem Zustimmschalter ausführen. Smartpad in der Hand halten, NOT-HALT griffbereit.
- Vor Programm-Reset oder Bewegung die Stoppursache prüfen. Ein Reset behebt weder einen Sicherheitsfehler noch eine fehlende Justage.
- Erst in **EXT** zurückkehren, wenn die Zelle frei und der Sicherheitszaun geschlossen ist.
- Wird der beschriebene Zustand nicht erreicht, abbrechen und den Anlagenverantwortlichen informieren.

## Problem: Roboter nicht justiert (E1)

### Fehlerbild

Der Roboter fährt nicht mehr, weil die externe Achse **E1** nicht justiert ist. Die Steuerung sperrt jede Bewegung und jeden Programmstart.

**Typische Meldungen**

- **KSS01347:** Roboter nicht justiert
- **KSS00106:** Justage durchführen E1
- **KSS15047:** Justagereferenzierung angefordert (intern)

Begleitend traten im dokumentierten Fall **KSS27003** und **KSS27004** (Bremsentest erforderlich) sowie **KSS26228** (Antrieb ID7 im Simulationsmodus) auf. Im Meldungsprotokoll vom 19.10.2026 stehen außerdem Netzphasenausfall (KSP-600-6M) und NOT-HALT extern. Das sind Begleitbeobachtungen, keine bestätigte Ursache.

**Konsequenz:** Rampenstopp. Weder manuelles Verfahren noch eine automatisierte SAK-Fahrt noch ein Programmstart sind möglich.

**Aufwand:** Etwa 15 Minuten. Die Anleitung vor Beginn vollständig lesen.

### Sicherheit und Voraussetzungen

Dies ist der dokumentierte Wiederanlauf dieser konkreten Anlage, keine allgemeine Anleitung zur E1-Justage. Vor dem Aktivieren muss der Anlagenverantwortliche bestätigen, dass das Backup zur Anlage passt.

- Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Der Sicherheitszaun bleibt während dieses Ablaufs geschlossen.
- Die Anleitung nutzt die Benutzergruppen **Experte** und **Administrator**. Passwörter stehen bewusst nicht in diesem Dokument; Betriebsanweisung lesen oder beim Anlagenverantwortlichen bzw. Hersteller Weber erfragen.
- In der Projektliste das einzige Projekt mit **BACKUP** im Namen aus **2023** verwenden. Die anderen Dateien vom Juli sind **nicht** dieses Backup.
- Vorher den Projektstand prüfen: Änderungen nach dem Backup sind nach dem Einspielen nicht mehr aktiv.
- Zwei Bedienstellen: Antriebe und Grundstellung am **HMI des Druckers**, alles andere am **KRC5-Smartpad**.

### Schritt 1: Ausgangszustand herstellen, T1 und Anmeldung als Experte

Zuerst die Anlage in den erforderlichen Ausgangszustand bringen und das Smartpad auf T1 und Benutzergruppe Experte stellen.

1. **Am HMI des Druckers:** alle Fehlermeldungen quittieren, **Antriebe aus**, **Grundstellung aus**. Nichts weiter anwählen.
2. Sicherheitszaun schließen.
3. Smartpad in die Hand nehmen.
4. Schlüsselschalter am Smartpad auf das **Zahnradsymbol** drehen. Die Betriebsartenwahl öffnet sich, **EXT** ist vorausgewählt.
5. **T1** antippen, danach den Schlüssel wieder zurückdrehen.
6. Links am Rand den **Benutzer-Button** antippen und als **Experte** anmelden.
7. Oben in der Statusleiste auf **R** tippen und das **Programm abwählen**.

### Schritt 2: Backup-Projekt aktivieren und Steuerung neu starten

Das Backup-Projekt aktivieren, danach den Steuerungs-PC mit neu eingelesenen Dateien neu starten.

1. Links auf das **blaue Zahnrad mit Roboter** tippen. Das aktive Projekt wird angezeigt.
2. Das **orangene X** antippen. Es erscheint eine Übersicht.
3. Unter **Verfügbare Projekte** stehen die Projekte und Wiederherstellungspunkte. Das **BACKUP-Projekt aus 2023**, nicht die Juli-Dateien, wählen und **aktivieren**.
4. Als **Administrator** anmelden.
5. Oben links auf das **orangene Roboter-Symbol** tippen, dann **Herunterfahren** wählen.
6. Unter *Einstellung für das nächste Herunterfahren* zusätzlich **Dateien neu einlesen** anhaken.
7. Unter *Aktionen zum Herunterfahren* **Steuerungs-PC neu starten** wählen.
8. Den Neustart abwarten.

### Schritt 3: CNC-Programm fahren und in den Automatikbetrieb zurückkehren

Bei der dokumentierten Anlage das Programm **cnc** in T1 vollständig durchfahren, bis am Ende des Wiederanlaufs **SAK erreicht** angezeigt wird. Danach das Programm **nicht erneut zurücksetzen**. SAK bezeichnet die Satzkoinzidenz und ist kein allgemeines Programmende-Signal für beliebige Programme.

1. Wieder als **Experte** anmelden.
2. Im Dateiexplorer (Navigator) den Ordner **R1 / Program** öffnen. Ist etwas anderes angezeigt, das **orangene X** antippen, um zum Navigator zurückzukehren. **cnc** auswählen.
3. Unten auf **Anwählen** tippen. Das Programm öffnet sich als Textfeld und ist abspielbereit.
4. Hinten am Smartpad den **Zustimmschalter** (Totmannschalter) gedrückt halten. Oben im Display steht *Start-Taste erforderlich*.
5. Links am Gerät die **Hardware-Starttaste gedrückt halten**. Der Roboter bewegt sich. Wird die Starttaste oder der Zustimmschalter losgelassen, hält er an.
6. Die komplette Wiederanlauffahrt durchlaufen lassen; am Ende muss **SAK erreicht** angezeigt werden. Wird dieser Zustand nicht erreicht, abbrechen und den Anlagenverantwortlichen informieren.
7. Schlüsselschalter auf das **Zahnradsymbol** drehen, **EXT** wählen, Schlüssel zurückdrehen.
8. **Am HMI des Druckers:** **Antrieb ein**, dann **Grundstellung**. Der Roboter fährt in die Grundstellung.

### Kontrolle

Die Behebung ist gelungen, wenn **S, O, R und Ext** oben in der Statusleiste grün sind und der Roboter in der Grundstellung steht.

- **KSS01347 / Roboter nicht justiert** steht nicht mehr an.
- Bewegungen und Programmstart sind wieder freigegeben.
- Verbleibende Meldungen, einschließlich erforderlicher Bremsentests, vor Produktionsbeginn abarbeiten.

Bleibt die Justagemeldung bestehen, nicht weiter probieren und keine Justagewerte verändern. Den Anlagenverantwortlichen oder Weber informieren.

## Problem: Roboter führt Bremstest / Startbewegung nach dem Start nicht aus

### Fehlerbild

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

### Ursache und Voraussetzungen

Das CNC-Programm der Robotersteuerung muss möglicherweise manuell neu angewählt werden. Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten; die folgenden Schritte erfolgen ausschließlich am **Smartpad**.

Steht **KSS01347 / KSS00106 E1** an, stattdessen die [E1-Fehlerbehebung](#problem-roboter-nicht-justiert-e1) verwenden. Eine erneute Programmanwahl allein stellt die Justage nicht wieder her.

### Schritt 1: T1 wählen und Navigator öffnen

1. Schlüssel von **Remote** auf das **Zahnradsymbol** drehen.
2. **T1** auswählen, dann den Schlüssel zurück auf **Remote** drehen.
3. Links auf das **blaue Zahnrad** am Touchbildschirm tippen.
4. **Öffnen** antippen, danach links auf das **orangene X** tippen. Die Navigationsübersicht öffnet sich.

### Schritt 2: CNC-Programm anwählen und zurücksetzen

1. In der Dateiübersicht **cnc** auswählen.
2. Unten auf **Anwählen** tippen.
3. Oben in der Statusleiste das gelbe **R** antippen.
4. **Programm zurücksetzen** auswählen.

### Schritt 3: In den externen Modus zurückkehren

1. Schlüssel auf das **Zahnradsymbol** drehen.
2. **EXT** auswählen.
3. Schlüssel zurück auf **Remote** drehen.

### Kontrolle

- Alle Statusanzeigen am Smartpad sollten grün sein.
- Der erforderliche Bremstest bzw. die Startbewegung sollte wieder möglich sein.

Reagiert der Roboter weiterhin nicht, die aktuellen Meldungen prüfen und mit dem [Ablauf bei fehlender Reaktion](#problem-roboter-reagiert-nicht--start--referenz--oder-justagefahrt-wird-nicht-ausgeführt) fortfahren. Nicht mit offener Bremstest-Anforderung weiterproduzieren.

## Problem: Notstop nahe Sicherheitszaun (multiaxialer Druck)

### Fehlerbild

Der Roboter stoppt abrupt, sobald Werkzeug oder Extruder in zaunnahe Bereiche kommt.

- Die Bremsen greifen hörbar mit einem Knacken.
- HMI oder Schaltschrank zeigt Sicherheitsstopp- oder Bremstest-Meldungen.
- Die Automatikfahrt bleibt blockiert, bis der Roboter aus dem Schutzbereich gefahren wurde.

### Ursache und Voraussetzungen

Bei multiaxialen Bahnen, insbesondere bei **45-Grad-Ausrichtungen**, kann die TCP-/Extruder-Neigung schon vor dem physischen Zaun in den geschützten Bremsbereich geraten.

Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Vor dem Wiederanlauf die tatsächliche Stoppursache am **HMI, Smartpad oder Schaltschrankdisplay** lesen.

### Schritt 1: In den Einrichtbetrieb wechseln

1. Schlüssel von **Remote** auf das **Zahnradsymbol** drehen.
2. Prüfen, dass **EXT** angewählt ist, dann auf **T1** wechseln.
3. Schlüssel zurückdrehen. Das Smartpad kann neu starten.

### Schritt 2: Roboter aus dem Schutzbereich fahren

1. Einen **Zustimmschalter** auf der Rückseite gedrückt halten.
2. Achsweise verfahren oder, falls freigegeben, die SpaceMouse verwenden, bis TCP/Extruder klar außerhalb des Schutzbereichs ist.
3. Die Verfahr-Icons werden nur bei gedrücktem Zustimmschalter grün.

### Schritt 3: In den Automatikbetrieb zurückkehren

1. Schlüssel auf das **Zahnradsymbol** drehen.
2. Von **T1** auf **EXT** wechseln, dann den Schlüssel zurück auf **Remote** drehen.
3. Bleibt der Autostart blockiert, **cnc** erneut anwählen und **Programm zurücksetzen** ausführen, wie im [Startablauf](#problem-roboter-führt-bremstest--startbewegung-nach-dem-start-nicht-aus) beschrieben.
4. Fordert die Steuerung eine Referenzierung oder Justage an, diese Meldung vor dem Fortfahren abarbeiten. Bei **E1-Justagemeldungen** die [E1-Anleitung](#problem-roboter-nicht-justiert-e1) verwenden.

### Kontrolle

- TCP/Extruder befindet sich außerhalb des Schutzbereichs.
- Sicherheitsstopp-Meldungen sind behoben und erforderliche Bremstests abgeschlossen.
- Die Statusanzeigen am Smartpad sind grün und der Automatikbetrieb ist wieder freigegeben.

Vor erneutem Start des Druckauftrags den Bahnabstand prüfen. Nicht wiederholt dieselbe Bahn in den Schutzbereich fahren.

## Problem: Roboter reagiert nicht / Start-, Referenz- oder Justagefahrt wird nicht ausgeführt

### Fehlerbild

Diesen Ablauf nutzen, wenn die Sicherheitsstopp-Wiederfreigabe abgeschlossen ist, der Roboter aber weiterhin nicht reagiert.

- Keine Bewegung, obwohl der Druckauftrag geladen ist.
- Druckabbruch bei **0,0,0** oder einer ähnlichen Startposition.
- Bremstest, Referenzfahrt oder Startbewegung beginnt nicht.
- Die Steuerung wirkt aktiv, reagiert aber nicht auf den Startbefehl.

### Mögliche Ursachen und Voraussetzungen

- Das CNC-Programm ist nicht mehr korrekt angewählt.
- Roboterprogramm-/EMI-Zustand ist nach Abbruch, Handverfahren oder Moduswechsel inkonsistent.
- Externe/interne Steuerung ist nicht synchronisiert.
- Referenzschalter oder Startreferenz wird nicht erreicht oder schaltet nicht korrekt.

Die [allgemeinen Sicherheitsvorgaben](#sicherheit-und-voraussetzungen) beachten. Steht **KSS01347 / KSS00106 E1** an, die [E1-Anleitung](#problem-roboter-nicht-justiert-e1) verwenden, statt wiederholt Programme zurückzusetzen.

### Schritt 1: Status prüfen und Programm neu anwählen

1. Die Statusanzeigen oben rechts am Smartpad prüfen. Aktive Meldungen abarbeiten, bis die Fahrfreigabe wiederhergestellt ist.
2. Navigator öffnen, **cnc** auswählen und **Anwählen** antippen.
3. Das gelbe **R** antippen und **Programm zurücksetzen** auswählen.

### Schritt 2: Roboter/CNC-Übergabe zurücksetzen und Betriebsart prüfen

1. Unter **PrgView** das **EMI (External Motion Interface)** zurücksetzen.
2. Oben in der Rahmenleiste das Roboterprogramm zurücksetzen und neu starten.
3. Die Betriebsart prüfen und für den Automatikbetrieb auf **EXT** zurückkehren.
4. Falls nötig, kurz zwischen externer/interner Steuerung wechseln und anschließend die erforderliche Betriebsart wiederherstellen.

### Schritt 3: Erforderliche Startbewegung erneut ausführen

1. Erst nach dem Reset von Programm- und Steuerungszustand den Bremstest, die Referenzfahrt oder die Startbewegung erneut auslösen.
2. Schlägt die Referenz-/Justagefahrt weiterhin fehl, durch qualifiziertes Instandhaltungspersonal prüfen lassen, ob der Referenzschalter in Referenzposition beide Kontrolllampen korrekt schaltet.
3. Nachjustieren oder erneutes Befestigen des Referenzschalters nur nach den anlagenspezifischen Instandhaltungs- und Freischaltvorgaben durchführen lassen.

### Kontrolle

- Die Statusanzeigen am Smartpad sind grün.
- Der erforderliche Bremstest, die Referenzfahrt oder die Startbewegung wird abgeschlossen.
- Der Roboter reagiert wieder auf den Startbefehl.

Bleibt der Fehler bestehen, abbrechen und den Anlagenverantwortlichen informieren, statt weitere Resets zu wiederholen.

---

## Vorbeugung

Um wiederkehrende Fehler zu vermeiden:

- Vor dem Start sicherstellen, dass **cnc** korrekt angewählt ist.
- Statusanzeigen prüfen und Bremstest-Warnungen vor dem Fortfahren abarbeiten.
- Zusätzlichen Abstand zum Zaun einplanen; der geschützte Bremsbereich kann vor der physischen Barriere beginnen.
- Bei 45-Grad-Multiaxialdrucken den Extruder zur Wand bzw. zum größten freien Bereich neigen, weg von der Sicherheitstür.
- Ein freigegebenes Backup bereithalten und dessen Projektstand dokumentieren.

## Wenn die Behebung nicht gelingt

Robotersteuerungs-Verbindung, NOT-HALT- und Sicherheitssystem-Status sowie Stromversorgung der Antriebe durch die zuständigen Personen prüfen lassen. Keine Schutzfunktionen umgehen und Bewegungen nicht mit ungeklärten Fehlern wiederholt starten.

---

*Zuletzt aktualisiert: 2026-10-21*
*Grundlage: Praxiserfahrungen mit KUKA-Robotersystemen; E1-Wiederanlauf nach dem bereitgestellten Bericht vom 20.10.2026 und ergänzender Bedienerklärung.*
