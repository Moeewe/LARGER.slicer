
# Zusätzliche Hinweise zur Nutzung des Ginger Additive Drucksystems

Diese ergänzenden Hinweise dienen der besseren Handhabung des Systems in der Praxis und basieren auf eigenen Erfahrungen beim Drucken mit dem Ginger Additive Drucker in Kombination mit einem UR5-Roboterarm.

---

## 1. Linienrichtung beachten (Grasshopper/Rhino)

Der Roboter folgt der Richtung der Kurven von ihrem Startpunkt zum Endpunkt. Das bedeutet:
- Eine falsche Linienrichtung kann zu unnötigen Leerfahrten oder Richtungswechseln führen.
- Um ein „Zickzack-Muster“ zu vermeiden, sollten z. B. bei parallelen Linien jede zweite Linie umgedreht werden.
- Dies kann in Grasshopper mit einer „Flip Curve“-Komponente erfolgen, die abhängig vom Index oder Layer arbeitet.

---

## 2. Werkzeug anheben am Start und Ende

Gerade bei empfindlichen Werkzeugen wie einem Pinsel oder Pastenextruder ist es wichtig:
- Vor dem Startpunkt leicht anzuheben (z. B. +10–30 mm in Z),
- Am Endpunkt ebenfalls anheben, um ein „Abschmieren“ zu vermeiden.
- Dies kann durch das Einfügen zusätzlicher Punkte erreicht werden, z. B. durch einen „Move“-Befehl in Z-Richtung an Anfang und Ende der Linie.

---

## 3. Gleichmäßige Punktabstände auf Kurven

Zur Erzeugung gleichmäßiger Extrusion:
- Verwende die Komponente „Divide Curve“.
- Sie garantiert einen Punkt am Anfang und Ende sowie gleichmäßige Abstände dazwischen.
- Die Anzahl der Teilungen kann über die Kurvenlänge / gewünschten Abstand berechnet werden: z. B. `round(Kurve.Length / Abstand)`.

---

## 4. Werkzeugausrichtung beim Robots-Plugin

Die Orientierung des Werkzeugs (TCP – Tool Center Point) ist entscheidend für:
- eine natürliche Bewegung des Roboterarms,
- die Vermeidung von Eigenkollisionen (z. B. Roboter fährt in sich selbst ein).
- Überprüfe in der Vorschau im Robots-Plugin, ob der Roboter in einem sinnvollen Winkel arbeitet.
- Die Orientierung kann über die Optionen „Elbow“, „Wrist“ und weitere angepasst werden.

---

## 5. Startkonfiguration je nach Werkzeug

Beachte, dass sich die Achsstellungen des Roboters je nach Werkzeug stark unterscheiden:
- Ein 3D-Druckkopf ist oft senkrecht ausgerichtet,
- Ein Stifthalter z. B. waagerecht zur Zeichenfläche.
- Die Ausgangspositionen des Roboters müssen entsprechend angepasst werden.

---

## 6. Werkzeughöhe richtig einstellen

Die korrekte Höhe ist essenziell:
- Diese kann entweder direkt im Tool-Definition (z. B. Cluster mit Werkzeug-Geometrie) oder in der Roboter-Komponente eingestellt werden.
- Wird die Werkzeuglänge falsch eingetragen, kann der Roboter zu tief fahren und Werkzeug oder Tisch beschädigen.

---

## 7. Unerwünschtes Hochfahren des Roboters

Manche Komponenten im Skript führen automatisch ein Hochfahren des Werkzeugs ein:
- z. B. vor oder nach einem Pfad.
- Oder bei jeder Linie einzeln (z. B. Sicherheitsbewegungen).
- Wenn dies nicht gewünscht ist: Die entsprechenden Bereiche in der Grasshopper-Definition suchen (meist gruppiert) und das Verhalten deaktivieren.

