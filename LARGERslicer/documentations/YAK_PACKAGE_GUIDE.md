# Yak Package Manager - Publishing Guide

## Overview

This guide explains how to publish LARGERslicer to the Rhino Package Manager (Yak) so users can easily install and update the plugin.

## Prerequisites

1. **Yak CLI installed**: Download from [Rhino Package Manager](https://www.rhino3d.com/download/yak)
2. **Developer account**: Register at [food4rhino.com](https://www.food4rhino.com/) or McNeel developer portal
3. **Built plugin**: Ensure the plugin builds successfully for all target frameworks

## Setup Steps

### 1. Install Yak CLI

**Windows:**
- Download installer from Rhino website
- Run installer
- Verify: Open command prompt and run `yak --version`

**macOS:**
- Download from Rhino website
- Extract and add to PATH
- Verify: Open terminal and run `yak --version`

### 2. Login to Yak

```bash
yak login
```

You will be prompted to authenticate via web browser.

### 3. Build the Package

```bash
# Build the project first
dotnet build -c Release

# Create the Yak package
yak build
```

This will:
- Build the project for all target frameworks
- Package the .gha files
- Create a `.yak` file in the current directory

### 4. Test the Package Locally

Before publishing, test the package:

```bash
# Install locally for testing
yak install LARGERslicer-1.0.0.yak --source .

# Or specify the full path
yak install ./LARGERslicer-1.0.0.yak --source .
```

Test in Rhino/Grasshopper to ensure everything works.

### 5. Publish to Rhino Package Manager

```bash
yak push LARGERslicer-1.0.0.yak
```

**Note**: First-time publishing may require approval from McNeel.

## Version Management

### Updating Version

1. Update version in `LARGERslicer.csproj`:
   ```xml
   <Version>1.0.1</Version>
   ```

2. Update version in `yak.yml`:
   ```yaml
   version: 1.0.1
   ```

3. Update `CHANGELOG.md` with new version entry

4. Build and publish:
   ```bash
   dotnet build -c Release
   yak build
   yak push LARGERslicer-1.0.1.yak
   ```

### Version Numbering

Follow Semantic Versioning (see `VERSIONING_STRATEGY.md`):
- **MAJOR**: Breaking changes (1.0.0 → 2.0.0)
- **MINOR**: New features (1.0.0 → 1.1.0)
- **PATCH**: Bug fixes (1.0.0 → 1.0.1)

## Package Contents

The Yak package automatically includes:
- `.gha` files from `bin/Release/` for all target frameworks
- Metadata from `yak.yml`
- Assembly information from `.csproj`

## File Structure

```
LARGERslicer/
├── yak.yml              # Package configuration
├── LARGERslicer.csproj  # Project file with version
├── CHANGELOG.md         # Version history
└── ...                  # Source files
```

## Troubleshooting

### Build Errors

- Ensure all target frameworks build successfully
- Check that all dependencies are available
- Verify .gha files are generated in `bin/Release/`

### Publishing Errors

- Verify you're logged in: `yak whoami`
- Check package name is unique
- Ensure version number is higher than previous release
- Verify all required fields in `yak.yml` are filled

### Installation Issues

- Users need Rhino 7 or 8 installed
- Grasshopper must be installed
- Check Rhino Package Manager for installation status

## Best Practices

1. **Always test locally** before publishing
2. **Update CHANGELOG.md** for every release
3. **Follow semantic versioning** strictly
4. **Document breaking changes** clearly
5. **Keep yak.yml updated** with current information
6. **Tag releases** in git: `git tag v1.0.0`

## User Installation

Users can install the package via:

1. **Rhino Package Manager UI**:
   - Open Rhino
   - Tools → Package Manager
   - Search for "LARGERslicer"
   - Click Install

2. **Command Line**:
   ```bash
   yak install LARGERslicer
   ```

3. **Update**:
   ```bash
   yak upgrade LARGERslicer
   ```

## Additional Resources

- [Yak Documentation](https://developer.rhino3d.com/guides/yak/)
- [Food4Rhino](https://www.food4rhino.com/)
- [McNeel Developer Portal](https://www.rhino3d.com/developers)











