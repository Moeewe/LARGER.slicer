#!/bin/bash
# Builds the signed and notarized macOS installer "LARGER.slicer Installer.pkg".
#   bash SETUP/macos-installer/build.sh <folder with the .yak files> <output folder> [notary profile]
# Needs: the certificate "Developer ID Installer" in the keychain, and a notarytool profile
#   (xcrun notarytool store-credentials <profile> --apple-id <Apple ID> --team-id <team>).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SETUP="$(cd "$HERE/.." && pwd)"
YAKS="$(cd "${1:?folder with the .yak files}" && pwd)"
OUT="$(mkdir -p "${2:?output folder}" && cd "$2" && pwd)"
PROFILE="${3:-${NOTARY_PROFILE:-}}"
VERSION="$(grep -E '^version:' "$SETUP/../LARGERslicer/manifest.yml" | awk '{print $2}')"
IDENT="$(security find-identity -v | awk -F'"' '/Developer ID Installer/ {print $2; exit}')"
[ -n "$IDENT" ] || { echo "No 'Developer ID Installer' certificate in the keychain." >&2; exit 1; }

STAGE="$(mktemp -d)"; trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/scripts/SETUP/packages"
cp "$HERE/postinstall" "$STAGE/scripts/postinstall"; chmod +x "$STAGE/scripts/postinstall"
cp "$SETUP/Install Mac.command" "$STAGE/scripts/SETUP/"
cp -R "$SETUP/RobotLibrary" "$STAGE/scripts/SETUP/"
cp "$YAKS"/largerslicer-*-rh7_0-any.yak "$YAKS"/largerslicer-*-rh8_0-any.yak "$STAGE/scripts/SETUP/packages/"

ID="de.fh-muenster.larger-slicer.installer"
pkgbuild --nopayload --scripts "$STAGE/scripts" --identifier "$ID" --version "$VERSION" "$STAGE/component.pkg"
cat > "$STAGE/distribution.xml" <<XML
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="2">
  <title>LARGER.slicer $VERSION</title>
  <welcome file="welcome.html"/>
  <conclusion file="conclusion.html"/>
  <options customize="never" require-scripts="false" hostArchitectures="arm64,x86_64"/>
  <domains enable_anywhere="false" enable_currentUserHome="false" enable_localSystem="true"/>
  <choices-outline><line choice="default"/></choices-outline>
  <choice id="default" title="LARGER.slicer"><pkg-ref id="$ID"/></choice>
  <pkg-ref id="$ID" version="$VERSION" onConclusion="none">component.pkg</pkg-ref>
</installer-gui-script>
XML
PKG="$OUT/LARGER.slicer Installer.pkg"
productbuild --distribution "$STAGE/distribution.xml" --resources "$HERE/resources" --package-path "$STAGE" --sign "$IDENT" --timestamp "$PKG"

if [ -n "$PROFILE" ]; then
  xcrun notarytool submit "$PKG" --keychain-profile "$PROFILE" --wait
  xcrun stapler staple "$PKG"
  spctl --assess --type install -vv "$PKG"
else
  echo "Not notarized (no profile given): macOS will warn when it is opened." >&2
fi
echo "$PKG"
