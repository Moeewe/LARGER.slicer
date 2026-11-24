# Zünd G3: the HP-GL commands the plug-in writes

Checked in November 2026 against the HP-GL manual of the Zünd G3/S3/L3/D3 line (valid from firmware 1.77;
it is not in this repository) and a Zünd note on three-axis milling. The manual is the authority: where this
page and it differ, the manual is right. Units: positions in 1/100 mm, **speeds in cm/s**.

## What was wrong here before

| Before | Right |
|---|---|
| Tool change = `XX220` | `XX220` only drives the carriage to the service position and takes the machine **offline**. It changes nothing. A router bit is changed with **`XX351,n`** |
| `SP` = spindle speed (Zünd guide) | `SP` chooses the **module slot** (11, 21, 31). The spindle speed is `XX150` |
| `VS`, `VU` in mm/s | **cm/s**: `VS7` is 70 mm/s |
| `ZP` before `SP` | Every tool has its own ZP, so **`SP` first**, then `ZP` |
| `AS,2,4` and `SV,65000` | The first parameter follows without a comma: `AS2,4` and `SV65000` |
| only `VS` for the feed | 3D moves (`MW`) take their speed from **`VW`**. Both are written now, with the same value |
| `XX306` = deepest cut + 3.3 mm | **material thickness + 3.3 mm**: the brush of the suction bell stands on the material |
| `XX62`, `XX81`, `XX82` in the sequence | not written since 1.0.9 (the machine keeps its own settings) |
| `IN;` in the header | is not written |

## Commands in the PLT

| Command | Meaning |
|---|---|
| `PB2,1,level` / `PB2,0` | vacuum on (level 1 to 10) / off |
| `ZT1` / `ZT0` | tool down / up. Not in the manual; taken from the Zünd 3D sample file |
| `MA` | absolute coordinates for 3D moves |
| `XX308,1,t` / `XX308,0` | additional underlay on (thickness in 1/100 mm) / off |
| `SP31` | module slot: 11 left, 21 middle, 31 right |
| `XX351,n` | change the router bit: 11 to 18 = magazine place 1 to 8 (ARC), 1 to 10 = by hand, the machine guides through it. The router module must be chosen (`SP`) |
| `VS`, `VW`, `VU` | speed with the tool down, of 3D moves, with the tool up (0.1 to 100 cm/s) |
| `SVw` | width of the vacuum zone |
| `XX306,h` | height of the suction bell over the underlay (RM-A: 0 to 7000) |
| `ASd,u` | acceleration 1 to 4 with the tool down, up |
| `ZPup,down` | up and down position of the chosen tool |
| `XX150,rpm` | spindle speed, RM-A 7000 to 50000 |
| `PUx,y` | lift the tool and travel |
| `MWx,y,z` | cut in a straight line in 3D |

## Order of a program with a change of the bit

```
PB2,1,5;  ZT1;MA;  XX308,1,200;      vacuum, absolute, underlay
SP31;  XX351,13;                     router module, bit of magazine place 3
ZP..; XX306..; AS..; XX150..;        values of this tool
VS..; VW..; VU..;                    speeds
PU..; MW..; ...                      milling
PU..;  ZP..;                         lift
SP31;  XX351,14;                     bit of magazine place 4 (the machine initialises it again)
ZP..; XX306..; AS..; XX150..; ...    values again, then on
PU..;  ZT0;  PU250000,0;  XX308,0;  PB2,0;
```

## Open: to be confirmed at the machine

- **Sign of Z.** The plug-in writes heights over the table as negative Z (`MW..,-500` = 5 mm above it), as in its
  field tests and as the manual describes the down position (positive = further down). The Zünd note on three-axis
  milling writes `MF ..,1100` for the lifted tool, which contradicts this. The plug-in lifts with `PU` only, so no
  lift depends on it. Check the first cut in the air.
- `XX351` has not been run from this plug-in yet.
- The manual requires that the router module is initialised in the absolute working mode for 3D moves.
