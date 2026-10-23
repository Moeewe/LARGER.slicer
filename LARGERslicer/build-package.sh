#!/bin/bash
# Build script for LARGERslicer Yak packages (Rhino 8 + Rhino 7)
# Usage: ./build-package.sh [version]
#
# Builds all target frameworks and creates two clean packages in dist/:
#   largerslicer-<version>-rh8_0-any.yak  (net48 + net7.0 + net7.0-windows)
#   largerslicer-<version>-rh7_0-any.yak  (net48 only, from manifest-rh7.yml)
# Each package is built from its own staging folder, so only the manifest,
# icon, README, LICENSE and plugin binaries end up in the package.

set -e  # Exit on error

cd "$(dirname "$0")"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}LARGERslicer Package Builder${NC}"
echo "================================"

# Portable in-place sed (macOS needs an empty backup suffix)
sedi() {
    if [[ "$OSTYPE" == "darwin"* ]]; then sed -i '' "$@"; else sed -i "$@"; fi
}

# Check if version argument provided
if [ -n "$1" ]; then
    VERSION=$1
    echo -e "${YELLOW}Using provided version: ${VERSION}${NC}"

    # Update version in .csproj, Yak manifests and yak.yml
    sedi -e "s/<Version>.*<\/Version>/<Version>${VERSION}<\/Version>/" \
         -e "s/<AssemblyVersion>.*<\/AssemblyVersion>/<AssemblyVersion>${VERSION}.0<\/AssemblyVersion>/" \
         -e "s/<FileVersion>.*<\/FileVersion>/<FileVersion>${VERSION}.0<\/FileVersion>/" \
         -e "s/<InformationalVersion>.*<\/InformationalVersion>/<InformationalVersion>${VERSION}<\/InformationalVersion>/" \
         LARGERslicer.csproj
    sedi "s/^version:.*/version: ${VERSION}/" manifest.yml manifest-rh7.yml yak.yml

    echo -e "${GREEN}✓ Updated version to ${VERSION}${NC}"
else
    # Read current version from .csproj
    VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' LARGERslicer.csproj | head -1)
    echo -e "${YELLOW}Using current version: ${VERSION}${NC}"
fi

# Manifests must match the assembly version, otherwise the package is mislabeled
for m in manifest.yml manifest-rh7.yml; do
    MV=$(sed -n 's/^version:[[:space:]]*//p' "$m" | head -1)
    if [ "$MV" != "$VERSION" ]; then
        echo -e "${RED}Error: ${m} has version ${MV}, LARGERslicer.csproj has ${VERSION}${NC}"
        echo "Run ./build-package.sh ${VERSION} to sync all version numbers"
        exit 1
    fi
done

# Find yak: PATH first, then the one shipped with Rhino 8
YAK=$(command -v yak || true)
if [ -z "$YAK" ] && [ -x "/Applications/Rhino 8.app/Contents/Resources/bin/yak" ]; then
    YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
fi
if [ -z "$YAK" ]; then
    echo -e "${RED}Error: yak command not found${NC}"
    echo "Install Rhino 8 or the Yak CLI: https://www.rhino3d.com/download/yak"
    exit 1
fi

# Clean previous builds
echo ""
echo "Cleaning previous builds..."
dotnet clean -c Release
rm -rf bin/Release

# Build the project
echo ""
echo "Building project for all target frameworks..."
if ! dotnet build -c Release; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Build successful${NC}"

# Sync packaged runtime folders from fresh release outputs.
echo ""
echo "Syncing packaged runtime folders..."
rm -rf net48 net7.0 net7.0-windows
mkdir -p net48 net7.0 net7.0-windows dist
cp -R bin/Release/net48/. net48/
cp -R bin/Release/net7.0/. net7.0/
cp -R bin/Release/net7.0-windows/. net7.0-windows/
cp -f bin/Release/net48/LARGERslicer.gha LARGERslicer.gha
cp -f bin/Release/net48/LARGERslicer.gha dist/LARGERslicer.gha
cp -f bin/Release/net7.0/LARGERslicer.gha dist/LARGERslicer-mac.gha
cp -f bin/Release/net7.0-windows/LARGERslicer.gha dist/LARGERslicer-win.gha

# Keep manual-install bundle in sync with the package build.
mkdir -p "Plugin Installation Files/LARGERSlicer"
cp -f bin/Release/net48/LARGERslicer.gha "Plugin Installation Files/LARGERSlicer/LARGERslicer.gha"
if [ -f bin/Release/net48/LARGERslicer.pdb ]; then
    cp -f bin/Release/net48/LARGERslicer.pdb "Plugin Installation Files/LARGERSlicer/LARGERslicer.pdb"
fi
if [ -f bin/Release/net48/Newtonsoft.Json.dll ]; then
    cp -f bin/Release/net48/Newtonsoft.Json.dll "Plugin Installation Files/LARGERSlicer/Newtonsoft.Json.dll"
fi

echo -e "${GREEN}✓ Packaged binaries synced${NC}"

# Stage each package in its own folder (obj/ is ignored by git)
echo ""
echo "Staging Yak packages..."
STAGE=obj/yak-staging
rm -rf "$STAGE"
mkdir -p "$STAGE/rh8" "$STAGE/rh7"

# Rhino 8: one subfolder per target framework, Rhino picks the matching one
for TFM in net48 net7.0 net7.0-windows; do
    mkdir -p "$STAGE/rh8/$TFM"
    cp -R "bin/Release/$TFM/." "$STAGE/rh8/$TFM/"
done
cp manifest.yml "$STAGE/rh8/manifest.yml"

# Rhino 7: net48 binaries at the package root
cp -R bin/Release/net48/. "$STAGE/rh7/"
cp manifest-rh7.yml "$STAGE/rh7/manifest.yml"

for P in rh8 rh7; do
    cp icon.png README.md "$STAGE/$P/"
    [ -f ../LICENSE ] && cp ../LICENSE "$STAGE/$P/"
    find "$STAGE/$P" -name "*.pdb" -delete
done

# Create Yak packages
echo ""
echo "Creating Yak packages..."
YAK_FILES=()
for P in rh8 rh7; do
    if ! (cd "$STAGE/$P" && "$YAK" build --platform any); then
        echo -e "${RED}Package creation failed (${P})!${NC}"
        exit 1
    fi
    FILE=$(ls -t "$STAGE/$P"/*.yak 2>/dev/null | head -1)
    if [ -z "$FILE" ]; then
        echo -e "${RED}Error: No .yak file created (${P})${NC}"
        exit 1
    fi
    mv -f "$FILE" dist/
    YAK_FILES+=("dist/$(basename "$FILE")")
done

echo ""
echo -e "${GREEN}✓ Packages created:${NC}"
for F in "${YAK_FILES[@]}"; do
    echo "  $F ($(du -h "$F" | cut -f1 | tr -d ' '))"
done
echo ""
echo "Next steps:"
echo "  1. Test locally: drag the .yak file for your Rhino version onto the Rhino window"
echo "  2. Publish both:"
for F in "${YAK_FILES[@]}"; do
    echo "     \"$YAK\" push $F"
done
echo ""
