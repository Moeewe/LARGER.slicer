#!/bin/bash
# LARGERslicer Setup (macOS) – Doppelklick zum Installieren.
# Installiert die Grasshopper-Plugins für Rhino 8 und/oder Rhino 7
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.
#
# Umgang mit vorhandenen Installationen:
#   nicht installiert          -> neueste passende Version installieren
#   gleiche Version            -> nichts tun
#   ältere Version             -> auf neueste aktualisieren
#   neuere Version (z.B. Beta) -> behalten
#   feste Version (unten)      -> genau diese Version aktivieren
#   alte manuelle .gha-Kopie   -> anbieten, sie in einen Backup-Ordner zu verschieben

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
MCNEEL="$HOME/Library/Application Support/McNeel/Rhinoceros"
STAMP="$(date +%Y%m%d-%H%M%S)"

ok()   { printf "  \033[32m✔\033[0m %s\n" "$*"; }
info() { printf "  \033[36mi\033[0m %s\n" "$*"; }
warn() { printf "  \033[33m!\033[0m %s\n" "$*"; }
fail() { printf "  \033[31m✘\033[0m %s\n" "$*"; }
quit() { echo; read -n 1 -s -r -p "Taste drücken zum Schließen..."; echo; exit "$1"; }
ask()  { local a; read -r -p "    $1 [J/n] " a; [[ -z "$a" || "$a" =~ ^[JjYy] ]]; }

# Versionsvergleich: gibt 0 zurück, wenn $1 neuer als $2 ist (1.10 > 1.9, "-beta"/"+abc" werden ignoriert)
ver_gt() {
  local IFS=. i a b
  read -ra a <<< "${1%%[-+]*}"; read -ra b <<< "${2%%[-+]*}"
  for ((i=0; i<${#a[@]} || i<${#b[@]}; i++)); do
    ((10#${a[i]:-0} > 10#${b[i]:-0})) && return 0
    ((10#${a[i]:-0} < 10#${b[i]:-0})) && return 1
  done
  return 1
}

# Aktive Version eines installierten Pakets (leer = nicht installiert)
installed_version() { [ -f "$PKGROOT/$2/$1/manifest.txt" ] && head -1 "$PKGROOT/$2/$1/manifest.txt" | tr -d '[:space:]'; }

# Neueste (oder gewünschte) Version für eine Rhino-Version vom Package-Server.
# Ausgabe: "<version> <url>"
server_version() {
  local name="$1" rh="$2" want="$3" json i j nd v rv best="" best_url=""
  json="$(mktemp)"
  curl -fsL "https://yak.rhino3d.com/versions/$name" -o "$json" 2>/dev/null || { rm -f "$json"; return 1; }
  i=0
  while v="$(plutil -extract "$i.version" raw -o - "$json" 2>/dev/null)"; do
    if { [ -n "$want" ] && [ "$v" = "$want" ]; } || { [ -z "$want" ] && [ "$(plutil -extract "$i.prerelease" raw -o - "$json")" != "true" ]; }; then
      nd="$(plutil -extract "$i.distributions" raw -o - "$json")"
      for ((j=0; j<nd; j++)); do
        rv="$(plutil -extract "$i.distributions.$j.rhino_version" raw -o - "$json")"
        # Rhino 8 kann auch Rhino-7-Pakete laden, Rhino 7 auch Rhino-6-Pakete
        if [[ "$rv" == rh$rh* || ( "$rh" = 8 && "$rv" == rh7* ) || ( "$rh" = 7 && "$rv" == rh6* ) ]]; then
          if [ -z "$best" ] || ver_gt "$v" "$best"; then
            best="$v"; best_url="$(plutil -extract "$i.distributions.$j.url" raw -o - "$json")"
          fi
          break
        fi
      done
    fi
    i=$((i+1))
  done
  rm -f "$json"
  [ -n "$best" ] && echo "$best $best_url"
}

# Rhino 8: über yak. Rhino 7: yak braucht auf Apple-Silicon-Macs Rosetta, deshalb
# das Paket direkt laden und so ablegen, wie es der Package Manager tut.
do_install() {
  local rh="$1" name="$2" version="$3" url="$4" log dest tmp
  if [ "$rh" = 8 ]; then
    log="$(mktemp)"
    if "$YAK8" install "$name" "$version" > "$log" 2>&1; then rm -f "$log"; return 0; fi
    sed 's/^/      /' "$log"; rm -f "$log"; return 1
  fi
  dest="$PKGROOT/7.0/$name"
  if [ ! -f "$dest/$version/manifest.yml" ]; then
    tmp="$(mktemp -d)"
    curl -fsSL "$url" -o "$tmp/p.yak" && unzip -oq "$tmp/p.yak" -d "$tmp/x" || { rm -rf "$tmp"; return 1; }
    mkdir -p "$dest" && rm -rf "$dest/$version" && mv "$tmp/x" "$dest/$version" || { rm -rf "$tmp"; return 1; }
    rm -rf "$tmp"
    xattr -dr com.apple.quarantine "$dest/$version" 2>/dev/null
  fi
  # Wie yak: alte Versionsordner bleiben liegen, manifest.txt bestimmt die aktive Version
  printf "%s\n" "$version" > "$dest/manifest.txt"
}

install_package() {
  local rh="$1" name="$2" pin="$3" have latest url
  have="$(installed_version "$name" "$rh.0")"
  if ! read -r latest url <<< "$(server_version "$name" "$rh" "$pin")" || [ -z "$latest" ]; then
    if [ -n "$have" ]; then warn "$name $have ist installiert (Package-Server nicht erreichbar, nicht geprüft)"; return 0; fi
    fail "$name: ${pin:+Version $pin }für Rhino $rh nicht gefunden oder Package-Server nicht erreichbar"; return 1
  fi

  if [ "$have" = "$latest" ]; then ok "$name $have ist aktuell"; return 0; fi
  if [ -n "$have" ] && [ -z "$pin" ] && ver_gt "$have" "$latest"; then
    info "$name $have ist neuer als die veröffentlichte $latest – wird behalten"; return 0
  fi

  if do_install "$rh" "$name" "$latest" "$url"; then
    if [ -z "$have" ]; then ok "$name $latest installiert"
    elif [ -n "$pin" ]; then ok "$name $have -> $latest (feste Version)"
    else ok "$name $have -> $latest aktualisiert"; fi
  else
    fail "$name $latest konnte nicht installiert werden"; return 1
  fi
}

# Alte manuelle Installationen (.gha direkt im Grasshopper-Libraries-Ordner) werden
# zusätzlich zum Paket geladen und verursachen doppelte Komponenten.
check_manual_copies() {
  local rh="$1" lib found=() f backup
  lib="$MCNEEL/$rh.0/Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries"
  [ -d "$lib" ] || return 0
  while IFS= read -r -d '' f; do found+=("$f"); done < <(
    find "$lib" -maxdepth 3 \( -iname "LARGERslicer*.gha" -o -iname "Robots.gha" -o -iname "Pufferfish*.gha" \) -print0 2>/dev/null)
  [ ${#found[@]} -eq 0 ] && return 0
  warn "Alte manuelle Plugin-Kopien in Rhino $rh gefunden (werden doppelt geladen):"
  for f in "${found[@]}"; do echo "      ${f#"$lib"/}"; done
  if ask "In einen Backup-Ordner verschieben?"; then
    backup="$lib/../Libraries-Backup-$STAMP"
    mkdir -p "$backup"
    for f in "${found[@]}"; do mv "$f" "$backup/" && ok "verschoben: $(basename "$f")"; done
    info "Backup: $(cd "$backup" && pwd)"
  else
    warn "Nicht verschoben – bei Problemen diese Dateien manuell entfernen"
  fi
}

# Robot-Bibliothek kopieren. Abweichende vorhandene Dateien werden als *.bak gesichert
# (nicht *.xml, damit Robots sie nicht als zweite Bibliothek lädt).
copy_library() {
  local lib="$1" f target other
  mkdir -p "$lib" || { fail "Ordner $lib konnte nicht angelegt werden"; return 1; }
  for f in "$HERE"/RobotLibrary/*.xml "$HERE"/RobotLibrary/*.3dm; do
    [ -e "$f" ] || continue
    target="$lib/$(basename "$f")"
    if [ -f "$target" ] && cmp -s "$f" "$target"; then ok "$(basename "$f") ist aktuell ($lib)"; continue; fi
    if [ -f "$target" ]; then
      mv "$target" "$target.bak-$STAMP" && info "alte $(basename "$f") gesichert als $(basename "$target").bak-$STAMP"
    fi
    if cp -f "$f" "$target"; then ok "$(basename "$f") -> $lib"; else fail "Kopieren fehlgeschlagen: $f"; return 1; fi
  done
  # Gleicher Robotername in einer anderen Datei? Dann ist unklar, welche Robots nimmt.
  for other in "$lib"/*.xml; do
    [ -e "$other" ] || continue
    [ -e "$HERE/RobotLibrary/$(basename "$other")" ] && continue
    grep -q 'name="KR120_R2700_2"' "$other" && warn "KR120_R2700_2 ist auch in $(basename "$other") definiert – bitte dort entfernen"
  done
  return 0
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
for rh in 8 7; do
  if [ "$rh" = 8 ] && [ -z "$YAK8" ]; then continue; fi
  if [ "$rh" = 7 ] && [ -z "$HAS7" ]; then continue; fi
  echo
  echo "Plugins für Rhino $rh:"
  for entry in "${PACKAGES[@]}"; do
    read -r name pin <<< "$entry"
    # Pufferfish gibt es für Rhino 7 auch als älteres "KSAPufferfish" – nicht doppelt installieren
    if [ "$name" = "Pufferfish" ] && [ -n "$(installed_version KSAPufferfish "$rh.0")" ]; then
      ok "Pufferfish ist schon installiert (KSAPufferfish $(installed_version KSAPufferfish "$rh.0"))"; continue
    fi
    # Feste Versionen gelten nur für Rhino 8, Rhino 7 bekommt die neueste passende
    [ "$rh" = 7 ] && pin=""
    install_package "$rh" "$name" "$pin" || ERR=1
  done
  check_manual_copies "$rh"
done

# Robot-Bibliothek in den Ordner, den Robots liest:
#   Rhino 8 (Robots 2.x): ~/Documents/Robots (oder der in Robots eingestellte Ordner)
#   Rhino 7 (Robots 1.x, Mono): ~/Robots
echo
echo "Robot-Bibliothek:"
if [ -n "$YAK8" ]; then
  LIB8="$HOME/Documents/Robots"
  SETTINGS="$PKGROOT/8.0/Robots/settings.json"
  [ -f "$SETTINGS" ] && CUSTOM="$(plutil -extract localLibraryPath raw -o - "$SETTINGS" 2>/dev/null)" && [ -n "$CUSTOM" ] && LIB8="$CUSTOM"
  copy_library "$LIB8" || ERR=1
fi
if [ -n "$HAS7" ]; then copy_library "$HOME/Robots" || ERR=1; fi

echo
if [ $ERR -eq 0 ]; then
  echo "Fertig! Rhino starten und \"$(basename "$GH")\" öffnen."
  echo "Robot System: KR120_R2700_2"
else
  echo "Es gab Fehler (siehe oben). Internetverbindung prüfen und erneut ausführen."
fi
quit $ERR
