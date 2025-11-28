# LARGERslicer

Advanced slicing operations, mesh processing, and 3D printing workflow tools for Grasshopper.

A Grasshopper plugin for industrial 3D printing workflows, featuring robot path generation, GCode processing, DXR file generation, and CNC toolpath creation.

## Project Structure

```
LARGERslicer/
├── Components/          # Grasshopper components
│   ├── CNC/            # CNC toolpath generation
│   ├── Export/         # DXR export and file operations
│   └── Utils/          # Utility components
├── Types/              # Custom data types (MachineSettings)
├── Utils/              # Utility classes and helpers (DXRHelper, IconHelper)
├── Properties/         # Project properties and launch settings
├── Resources/          # Icons and other resources
├── LARGERslicer.cs     # Main plugin assembly info
└── LARGERslicer.csproj # Project file
```

## Development

This plugin is built for Rhino 8 and Grasshopper using .NET 7.0/Framework 4.8.

### Building

```bash
dotnet build
```

The build generates `.gha` files for all target frameworks in `bin/Debug/`.

### Debugging

Use the provided launch profiles in `Properties/launchSettings.json` to debug with Rhino 8.

## Components

The plugin is organized into three main categories:

### CNC (1 component)
- **CNC Program**: Generates boustrophedon (zigzag) toolpaths for CNC milling with Zünd PLT output

### DXR (3 components)
- **DXR Generator**: Converts robot paths, extrusion, and speed data to DXR format
- **DXR GCode Postprocessor**: Converts GCode files to DXR format with automatic parsing
- **Machine Settings**: Configures printer settings (temperatures, cooling) for DXR output

### Utilities (8 components)
- **Safe Component**: Writes text lines to files with cross-platform path handling
- **Date Timestamp**: Generates timestamp strings (yymmddHHMM_ format)
- **Desktop Path**: Finds Desktop folder path cross-platform
- **Custom Preview Lineweights**: Sets custom line weights and colors for geometry preview
- **RTree Closest Point**: Finds closest points using spatial indexing
- **RTree Sort**: Sorts points by spatial distribution
- **Feedrate Calculator**: Adjusts feedrate for constant speed
- **Stream Freeze**: Controls data flow to prevent unwanted solution updates

## Features

### DXR File Generation
- Automatic header generation with calculated values (runtime, layers, extrusion totals)
- Machine settings integration (bed/nozzle temperature, cooling fan)
- Sequential line numbering
- Footer comments with generator information and timestamp
- Support for both GCode input and manual component inputs

### CNC Toolpath Generation
- Boustrophedon (zigzag) path generation
- Z-height sampling from mesh intersections
- Zünd PLT file format output
- SIMPLE and EXTENDED header modes

## Version

Current version: 1.0.0

## Author

Moritz Wesseler, FH Münster  
Contact: m.wesseler@fh-muenster.de

## License

See LICENSE file for details.
