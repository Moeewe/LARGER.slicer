#!/bin/bash
# Build script for LARGERslicer Yak package
# Usage: ./build-package.sh [version]

set -e  # Exit on error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}LARGERslicer Package Builder${NC}"
echo "================================"

# Check if version argument provided
if [ -n "$1" ]; then
    VERSION=$1
    echo -e "${YELLOW}Using provided version: ${VERSION}${NC}"
    
    # Update version in .csproj
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        sed -i '' "s/<Version>.*<\/Version>/<Version>${VERSION}<\/Version>/" LARGERslicer.csproj
    else
        # Linux
        sed -i "s/<Version>.*<\/Version>/<Version>${VERSION}<\/Version>/" LARGERslicer.csproj
    fi
    
    # Update version in yak.yml
    if [[ "$OSTYPE" == "darwin"* ]]; then
        sed -i '' "s/^version:.*/version: ${VERSION}/" yak.yml
    else
        sed -i "s/^version:.*/version: ${VERSION}/" yak.yml
    fi
    
    echo -e "${GREEN}✓ Updated version to ${VERSION}${NC}"
else
    # Read current version from .csproj
    VERSION=$(grep -oP '<Version>\K[^<]+' LARGERslicer.csproj)
    echo -e "${YELLOW}Using current version: ${VERSION}${NC}"
fi

# Check if yak is installed
if ! command -v yak &> /dev/null; then
    echo -e "${RED}Error: yak command not found${NC}"
    echo "Please install Yak CLI from: https://www.rhino3d.com/download/yak"
    exit 1
fi

# Check if logged in
if ! yak whoami &> /dev/null; then
    echo -e "${YELLOW}Warning: Not logged in to Yak${NC}"
    echo "Run 'yak login' to authenticate"
fi

# Clean previous builds
echo ""
echo "Cleaning previous builds..."
dotnet clean -c Release
rm -f *.yak

# Build the project
echo ""
echo "Building project for all target frameworks..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Build successful${NC}"

# Create Yak package
echo ""
echo "Creating Yak package..."
yak build

if [ $? -ne 0 ]; then
    echo -e "${RED}Package creation failed!${NC}"
    exit 1
fi

# Find the created .yak file
YAK_FILE=$(ls -t *.yak 2>/dev/null | head -1)

if [ -z "$YAK_FILE" ]; then
    echo -e "${RED}Error: No .yak file created${NC}"
    exit 1
fi

echo ""
echo -e "${GREEN}✓ Package created: ${YAK_FILE}${NC}"
echo ""
echo "Next steps:"
echo "  1. Test locally: yak install ${YAK_FILE} --source ."
echo "  2. Publish: yak push ${YAK_FILE}"
echo ""











