# LARGERslicer (Grasshopper plugin)

This folder (`LARGERslicer/`) contains the **C#/.NET Grasshopper plugin** used by the LARGER Slicer ecosystem.

If you’re looking for the machine-specific Grasshopper definitions (`.gh`) and quick-start guides, see `EXAMPLE FILES/` in the repository root.

## Documentation

Project documentation lives in `documentations/`:

- [Components Overview](documentations/COMPONENTS_OVERVIEW.md)
- [Component Inputs/Outputs](documentations/COMPONENT_INPUTS_OUTPUTS.md)
- [DXR Format Documentation](documentations/DXR_FORMAT_DOCUMENTATION.md)
- [Machine Settings Documentation](documentations/MACHINE_SETTINGS_DOCUMENTATION.md)
- [KUKA Robot Troubleshooting](documentations/KUKA_ROBOT_TROUBLESHOOTING.md)
- [CNC Program README](documentations/CNC_PROGRAM_README.md)

## Build

Build the plugin from this folder:

```bash
dotnet build
```

The build outputs `.gha` files under `bin/Debug/` for the supported target frameworks.

## Packaging

- `build-package.sh` / `build-package.bat`: package build scripts
- `dist/`: packaged `.yak` releases and built `.gha` artifacts

## Manual install files (deprecated)

Prebuilt files for old/manual installation are in:

- `Plugin Installation Files/LARGERSlicer/`

Note: the recommended installation method is Rhino’s `PackageManager` (see the repository root `README.md`).
