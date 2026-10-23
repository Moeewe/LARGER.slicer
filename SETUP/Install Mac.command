#!/bin/bash
# LARGERslicer Setup (macOS) – Doppelklick zum Installieren.
# Installiert die Grasshopper-Plugins für Rhino 8 und/oder Rhino 7
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.

# "Paket" = neueste passende Version, "Paket 1.2.3" = feste Version
PACKAGES=(
  "Robots"
  "LARGERslicer"
  "Pufferfish"
)

cd "$(dirname "$0")" || exit 1
HERE="$(pwd)"
GH="$HERE/../EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/LARGERslicer Weber Robot and Ginger.gh"
PKGROOT="$HOME/Library/Application Support/McNeel/Rhinoceros/packages"

ok()   { printf "  \033[32m✔\033[0m %s\n" "$*"; }
warn() { printf "  \033[33m!\033[0m %s\n" "$*"; }
fail() { printf "  \033[31m✘\033[0m %s\n" "$*"; }
quit() { echo; read -n 1 -s -r -p "Taste drücken zum Schließen..."; echo; exit "$1"; }

# Pufferfish gibt es für Rhino 7 auch als älteres "KSAPufferfish" – nicht doppelt installieren
has_other_pufferfish() { [ "$1" = "Pufferfish" ] && [ -d "$PKGROOT/$2/KSAPufferfish" ]; }

# Rhino 8: über yak (wählt automatisch die passende Version)
install_r8() {
  local yak="$1" name="$2" version="$3" log
  if [ -n "$version" ] && grep -qx "$name ($version)" <<< "$("$yak" list 2>/dev/null)"; then
    ok "$name $version ist schon installiert"; return 0
  fi
  log="$(mktemp)"
  if "$yak" install "$name" $version > "$log" 2>&1; then
    ok "$(grep -o "$name ([^)]*)" "$log" | tail -1) installiert"
  else
    fail "$name konnte nicht installiert werden:"; sed 's/^/      /' "$log"; rm -f "$log"; return 1
  fi
  rm -f "$log"
}

# Rhino 7: yak von Rhino 7 braucht auf Apple-Silicon-Macs Rosetta, deshalb das
# Paket direkt vom Package-Server laden und so ablegen, wie es der Package Manager tut.
install_r7() {
  local name="$1" want="$2" json i j nd rv url version="" dest tmp
  json="$(mktemp)"
  if ! curl -fsSL "https://yak.rhino3d.com/versions/$name" -o "$json"; then
    fail "$name: Package-Server nicht erreichbar"; rm -f "$json"; return 1
  fi
  i=0
  while plutil -extract "$i.version" raw -o - "$json" >/dev/null 2>&1; do
    local v; v="$(plutil -extract "$i.version" raw -o - "$json")"
    if [ -z "$want" ] && [ "$(plutil -extract "$i.prerelease" raw -o - "$json")" = "true" ]; then i=$((i+1)); continue; fi
    if [ -n "$want" ] && [ "$v" != "$want" ]; then i=$((i+1)); continue; fi
    nd="$(plutil -extract "$i.distributions" raw -o - "$json")"
    for ((j=0; j<nd; j++)); do
      rv="$(plutil -extract "$i.distributions.$j.rhino_version" raw -o - "$json")"
      case "$rv" in rh7*|rh6*)
        url="$(plutil -extract "$i.distributions.$j.url" raw -o - "$json")"; version="$v"; break 2;;
      esac
    done
    i=$((i+1))
  done
  rm -f "$json"
  if [ -z "$version" ]; then fail "$name: keine Version für Rhino 7 gefunden"; return 1; fi

  dest="$PKGROOT/7.0/$name"
  if [ -f "$dest/$version/manifest.yml" ]; then ok "$name ($version) ist schon installiert"; return 0; fi
  tmp="$(mktemp -d)"
  if curl -fsSL "$url" -o "$tmp/p.yak" && unzip -oq "$tmp/p.yak" -d "$tmp/x"; then
    rm -rf "$dest" && mkdir -p "$dest" && mv "$tmp/x" "$dest/$version" && printf "%s\n" "$version" > "$dest/manifest.txt"
    xattr -dr com.apple.quarantine "$dest" 2>/dev/null
    ok "$name ($version) installiert"
  else
    fail "$name ($version) konnte nicht geladen werden"; rm -rf "$tmp"; return 1
  fi
  rm -rf "$tmp"
}

echo
echo "=== LARGERslicer Setup ==="
echo

# Installierte Rhino-Versionen finden
YAK8=""; HAS7=""
for d in "/Applications" "$HOME/Applications"; do
  [ -z "$YAK8" ] && [ -x "$d/Rhino 8.app/Contents/Resources/bin/yak" ] && YAK8="$d/Rhino 8.app/Contents/Resources/bin/yak"
  [ -d "$d/Rhino 7.app" ] && HAS7=1
done
if [ -z "$YAK8" ] && [ -z "$HAS7" ]; then
  fail "Weder Rhino 8 noch Rhino 7 gefunden. Bitte zuerst Rhino installieren."
  quit 1
fi
[ -n "$YAK8" ] && ok "Rhino 8 gefunden"
[ -n "$HAS7" ] && ok "Rhino 7 gefunden"

if pgrep -xq "Rhinoceros"; then
  echo
  read -r -p "  Rhino läuft gerade. Bitte Rhino beenden und dann Enter drücken. "
fi

ERR=0
for rhino in 8 7; do
  if [ "$rhino" = 8 ] && [ -z "$YAK8" ]; then continue; fi
  if [ "$rhino" = 7 ] && [ -z "$HAS7" ]; then continue; fi
  echo
  echo "Plugins für Rhino $rhino:"
  for entry in "${PACKAGES[@]}"; do
    read -r name version <<< "$entry"
    if has_other_pufferfish "$name" "$rhino.0"; then ok "Pufferfish ist schon installiert (KSAPufferfish)"; continue; fi
    # Feste Versionen gelten nur für Rhino 8, Rhino 7 bekommt die neueste passende
    if [ "$rhino" = 8 ]; then install_r8 "$YAK8" "$name" "$version" || ERR=1
    else install_r7 "$name" "" || ERR=1; fi
  done
done

# Robot-Bibliothek in den Ordner, den Robots liest:
#   Rhino 8 (Robots 2.x): ~/Documents/Robots
#   Rhino 7 (Robots 1.x, Mono): ~/Robots
echo
echo "Robot-Bibliothek:"
LIBS=()
if [ -n "$YAK8" ]; then
  LIB8="$HOME/Documents/Robots"
  SETTINGS="$PKGROOT/8.0/Robots/settings.json"
  # Falls in Robots ein eigener lokaler Ordner gesetzt wurde, diesen benutzen
  [ -f "$SETTINGS" ] && CUSTOM="$(plutil -extract localLibraryPath raw -o - "$SETTINGS" 2>/dev/null)" && [ -n "$CUSTOM" ] && LIB8="$CUSTOM"
  LIBS+=("$LIB8")
fi
[ -n "$HAS7" ] && LIBS+=("$HOME/Robots")
for LIB in "${LIBS[@]}"; do
  mkdir -p "$LIB"
  for f in "$HERE"/RobotLibrary/*.xml "$HERE"/RobotLibrary/*.3dm; do
    [ -e "$f" ] || continue
    if cp -f "$f" "$LIB/"; then ok "$(basename "$f") -> $LIB"; else fail "Kopieren fehlgeschlagen: $f"; ERR=1; fi
  done
done

echo
if [ $ERR -eq 0 ]; then
  echo "Fertig! Rhino starten und \"$(basename "$GH")\" öffnen."
  echo "Robot System: KR120_R2700_2"
  [ -n "$HAS7" ] && warn "Rhino 7: Das Skript enthält eine Python-3-Komponente, die es nur in Rhino 8 gibt."
else
  echo "Es gab Fehler (siehe oben). Internetverbindung prüfen und erneut ausführen."
fi
quit $ERR
