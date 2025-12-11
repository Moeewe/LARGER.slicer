# Versioning Strategy for LARGERslicer

## Overview

LARGERslicer follows **Semantic Versioning (SemVer)** with the format: `MAJOR.MINOR.PATCH`

## Version Format

```
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

### Version Components

- **MAJOR** (X.0.0): Breaking changes
  - Incompatible API changes
  - Removal of components or features
  - Major architectural changes
  - Example: 1.0.0 → 2.0.0

- **MINOR** (0.X.0): New features (backward compatible)
  - New components added
  - New functionality in existing components
  - Performance improvements
  - Example: 1.0.0 → 1.1.0

- **PATCH** (0.0.X): Bug fixes (backward compatible)
  - Bug fixes
  - Minor improvements
  - Documentation updates
  - Example: 1.0.0 → 1.0.1

- **PRERELEASE** (optional): Pre-release versions
  - `-alpha.1`, `-beta.1`, `-rc.1`
  - Example: 1.0.0-alpha.1

- **BUILD** (optional): Build metadata
  - Usually not included in published versions
  - Example: 1.0.0+20231205

## Current Version

**1.0.0** - Initial release

## Version Update Rules

### When to increment MAJOR:
- Breaking changes to component inputs/outputs
- Removal of components
- Incompatible changes to file formats (DXR, etc.)
- Major refactoring that breaks existing workflows

### When to increment MINOR:
- New components added
- New features in existing components
- New toolpath patterns
- New utility components
- Performance improvements
- New documentation

### When to increment PATCH:
- Bug fixes
- Minor UI improvements
- Icon updates
- Documentation corrections
- Code cleanup without functional changes

## Version Synchronization

The following locations must be updated when releasing a new version:

1. **LARGERslicer.csproj**: `<Version>` tag
2. **yak.yml**: `version:` field
3. **CHANGELOG.md**: New version entry
4. **LARGERslicer.cs**: Assembly version (auto-generated from .csproj)
5. **DXRHelper.cs**: Postprocessor version (if changed)

## Release Process

1. **Development**: Work on `main` or feature branches
2. **Version Update**: Update version in `.csproj` and `yak.yml`
3. **Changelog**: Document changes in `CHANGELOG.md`
4. **Build**: Test build for all target frameworks
5. **Package**: Create `.yak` package using `yak build`
6. **Test**: Test package installation locally
7. **Publish**: Upload to Rhino Package Manager
8. **Tag**: Create git tag `v1.0.0`

## Version History

See `CHANGELOG.md` for detailed version history.

## Best Practices

- Always update version before publishing
- Document breaking changes clearly
- Test thoroughly before releasing
- Maintain backward compatibility when possible
- Use pre-release versions for testing (alpha, beta, rc)
- Keep CHANGELOG.md up to date






