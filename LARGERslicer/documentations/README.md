# LARGERslicer Documentation

Use this page to find the guide that matches your task. The repository [README](../../README.md) is the general project and slicer overview.

## Start here

| Goal | Guide |
|---|---|
| Install the plugin and create a first toolpath | [Quick Start](../../README.md#quick-start) · [Usage Guide](../../README.md#usage-guide) |
| Prepare and operate a machine | Choose a machine guide below |
| DXR25-/KUKA-Roboterfehler beheben | [Deutsche Anleitung](KUKA_ROBOT_TROUBLESHOOTING_DE.md) · [English guide](KUKA_ROBOT_TROUBLESHOOTING.md) |
| Find a plugin component or its inputs and outputs | [Component Overview](COMPONENTS_OVERVIEW.md) and [Component Inputs/Outputs](COMPONENT_INPUTS_OUTPUTS.md) |

## Machine guides

| Machine | Guide |
|---|---|
| Weber DXR25 | [Deutsch: Inbetriebnahme und Bedienanleitung](<../../EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE GERMAN.md>) · [English: Commissioning and Operating Manual](<../../EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - WEBER - DXR25 - README/README Weber DXR25 PRINTER QUICK START GUIDE ENGLISH.md>) |
| Ginger One | [Quick Start Guide](<../../EXAMPLE FILES/00 - WEBER : GINGER [Robotic] 3D Printing/00 - GINGER - ONE - README/README GINGER 00 QUICK START GUIDE.md>) |
| Universal Robots UR5 | [Quick Start Guide](<../../EXAMPLE FILES/00 - UNIVERSAL ROBOTS - UR5/README UR5 00 QUICK START GUIDE.md>) |
| Zünd G3 L-2500 | [CNC Milling Quick Start Guide](<../../EXAMPLE FILES/00 - ZUENDT - 3D CNC MILLING/README Zünd G3 L-2500 CNC MILLING QUICK START GUIDE.md>) |

## Operator references

- [KUKA-Roboter: Fehlerbehebung (DE)](KUKA_ROBOT_TROUBLESHOOTING_DE.md) — zentrale deutsche Wiederherstellungsabläufe und Fehlerbilder für den Weber DXR25.
- [KUKA Robot Troubleshooting Guide (EN)](KUKA_ROBOT_TROUBLESHOOTING.md) — English version of the recovery procedures.
- [DXR File Health Check: GO/NO-GO cabinet sheet (DE)](SCHALTSCHRANK_ZETTEL_DXR_GOCHECK_DE.md)
- [DXR File Health Check: immediate NO-GO help (DE)](SCHALTSCHRANK_ZETTEL_NOGO_HILFE_DE.md)
- [Machine Settings Documentation](MACHINE_SETTINGS_DOCUMENTATION.md)
- [DXR Format Documentation](DXR_FORMAT_DOCUMENTATION.md)
- [CNC Program Guide](CNC_PROGRAM_README.md)

## Plugin reference and development

- [Component Overview](COMPONENTS_OVERVIEW.md)
- [Component Inputs/Outputs](COMPONENT_INPUTS_OUTPUTS.md)
- [Changelog](../CHANGELOG.md)
- [Yak Package Guide](YAK_PACKAGE_GUIDE.md)
- [Versioning Strategy](VERSIONING_STRATEGY.md)

Other documents in this folder include implementation notes, research, and historical analyses. Use the operator references above for current workflows.

## Keeping guides consistent

Machine guides should describe the machine workflow and identify symptoms. Detailed recovery procedures should have one maintained source; other guides should link to it rather than reproducing the steps. Keep translated guides aligned when operating steps change.
