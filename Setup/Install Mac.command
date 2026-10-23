#!/bin/bash
# LARGERslicer Setup (macOS) – Doppelklick zum Installieren.
# Installiert die Grasshopper-Plugins über den Rhino Package Manager (yak)
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.

# "Paket" = neueste Version, "Paket 1.2.3" = feste Version
PACKAGES=(
  "Robots"
  "LARGERslicer"
  "Pufferfish"
)

cd "$(dirname "$0")" || exit 1
HERE="$(pwd)"
GH="$HERE/../EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/LARGERslicer Weber Robot and Ginger.gh"

ok()   { printf "  \033[32m✔\033[0m %s\n" "$*"; }
fail() { printf "  \033[31m✘\033[0m %s\n" "$*"; }
quit() { echo; read -n 1 -s -r -p "Taste drücken zum Schließen..."; echo; exit "$1"; }

echo
echo "=== LARGERslicer Setup ==="
echo

# Rhino 8 + yak finden
YAK=""
for app in "/Applications/Rhino 8.app" "$HOME/Applications/Rhino 8.app"; do
  [ -x "$app/Contents/Resources/bin/yak" ] && YAK="$app/Contents/Resources/bin/yak" && break
done
if [ -z "$YAK" ]; then
  fail "Rhino 8 wurde nicht gefunden (/Applications/Rhino 8.app). Bitte zuerst Rhino 8 installieren."
  quit 1
fi
ok "Rhino 8 gefunden"

if pgrep -xq "Rhinoceros"; then
  echo
  read -r -p "  Rhino läuft gerade. Bitte Rhino beenden und dann Enter drücken. "
fi

# 1) Plugins
echo
echo "Plugins:"
INSTALLED="$("$YAK" list 2>/dev/null)"
ERR=0
LOG="$(mktemp)"
for entry in "${PACKAGES[@]}"; do
  read -r name version <<< "$entry"
  if [ -n "$version" ] && grep -qx "$name ($version)" <<< "$INSTALLED"; then
    ok "$name $version ist schon installiert"
    continue
  fi
  if "$YAK" install "$name" $version > "$LOG" 2>&1; then
    ok "$(grep -o "$name ([^)]*)" "$LOG" | tail -1) installiert"
  else
    fail "$name konnte nicht installiert werden:"
    sed 's/^/      /' "$LOG"
    ERR=1
  fi
done
rm -f "$LOG"

# 2) Robot-Bibliothek -> ~/Documents/Robots (Standardordner von Robots 2.x)
echo
echo "Robot-Bibliothek:"
LIB="$HOME/Documents/Robots"
SETTINGS="$HOME/Library/Application Support/McNeel/Rhinoceros/packages/8.0/Robots/settings.json"
if [ -f "$SETTINGS" ]; then
  # Falls in Robots ein eigener lokaler Ordner gesetzt wurde, diesen benutzen
  CUSTOM="$(plutil -extract localLibraryPath raw -o - "$SETTINGS" 2>/dev/null)"
  [ -n "$CUSTOM" ] && LIB="$CUSTOM"
fi
mkdir -p "$LIB"
for f in "$HERE"/RobotLibrary/*.xml "$HERE"/RobotLibrary/*.3dm; do
  [ -e "$f" ] || continue
  if cp -f "$f" "$LIB/"; then ok "$(basename "$f") -> $LIB"; else fail "Kopieren fehlgeschlagen: $f"; ERR=1; fi
done

echo
if [ $ERR -eq 0 ]; then
  echo "Fertig! Rhino 8 starten und \"$(basename "$GH")\" öffnen."
  echo "Robot System: KR120_R2700_2"
else
  echo "Es gab Fehler (siehe oben). Internetverbindung prüfen und erneut ausführen."
fi
quit $ERR
