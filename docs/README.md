# LARGER.slicer developer handbook

How LARGER.slicer is built, which rules it enforces and why, how icons are drawn, how it was tested,
and what the examples show. Written so that no technical knowledge or approach gets lost.
**English** · [Deutsch (Überblick)](README.de.md)

| Page | Read it when you want to ... |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | find your way in the code: repository layout, the shared slicer core, data model, the Grasshopper 1 and 2 layers (option rows, background calculation, Quick Start), build, install locations, packages, the `obj/` quirk |
| [PRINTING_PRINCIPLES.md](PRINTING_PRINCIPLES.md) | know the rules every path must keep: one non-crossing line, bead width, ½-bead support, speeds and layer times, skirt, seam and exit, nozzles, E per machine, surface patterns, and what the reference print UNIKAT HEAD V2 (2024) taught |
| [ICONS.md](ICONS.md) | change or add an icon, or understand the style, the logo and the 48 px set for Grasshopper 2 |
| [TESTING.md](TESTING.md) | test a change: console bench, Rhino 9 test slot, in-memory documents, the visible-canvas harness, metrics |
| [EXAMPLES.md](EXAMPLES.md) | know what every example file and every Quick Start section shows, and for which machine |
| [DECISIONS.md](DECISIONS.md) | understand why things are the way they are before changing them |

## Other documentation

| Document | What |
|---|---|
| [README.md](../README.md) · [README.de.md](../README.de.md) | the user-facing overview: tabs, components, machines, quick start |
| [OWN_MACHINES.md](OWN_MACHINES.md) | adding an own G-code machine, bed slinger, gantry with 3D model, robot |
| [GingerOne.README.md](../LARGERslicer/Resources/GingerOne.README.md) | the Ginger One 3D model: coordinates, layers, poses |
| [LARGERslicer.GH2/README.md](../LARGERslicer.GH2/README.md) | the Grasshopper 2 plug-in: components, icons, what is not ported, build and load |
| [SETUP/README.md](../SETUP/README.md) | the installers |
| [CHANGELOG.md](../LARGERslicer/CHANGELOG.md) | what changed in which version, with measured numbers |
| [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | libraries, papers and ideas the code builds on |
| [LARGERslicer/documentations/](../LARGERslicer/documentations/README.md) | older guides: component lists, DXR format, KUKA troubleshooting, Yak and versioning notes |
| Machine guides in [EXAMPLE FILES](../EXAMPLE%20FILES) | Weber DXR25 (EN/DE), Ginger One, UR5, Zünd G3 |
| `docs/*.html` | the pages of the product website (German) |

**LARGER.app**, a stand-alone application, is a separate private project. It shares the presets and
the printing rules of this handbook with the plug-in; nothing here depends on it.
