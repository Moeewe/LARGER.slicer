# Machine Settings - Complete Documentation

## Overview

The LARGERslicer system provides two Machine Settings components for controlling KUKA DXR printer temperatures, fan speeds, and multi-zone heating systems:

1. **Machine Settings** (Simple) - Easy 3-input component for basic setups
2. **Machine Settings Extended** - Advanced multi-zone component for precise control

Both components generate DXR-compatible G-code that is automatically inserted into the DXR file at the correct position (after header, before movements).

---

## Table of Contents

1. [Component Comparison](#component-comparison)
2. [Machine Settings (Simple)](#machine-settings-simple)
3. [Machine Settings Extended](#machine-settings-extended)
4. [Variable Formats: V.P.VAR_* vs V.E.GLOBAL_*](#variable-formats)
5. [Heated Bed Zone Logic](#heated-bed-zone-logic)
6. [Automatic Temperature Calculation](#automatic-temperature-calculation)
7. [DXR Integration](#dxr-integration)
8. [Examples](#examples)

---

## Component Comparison

| Feature | Machine Settings (Simple) | Machine Settings Extended |
|---------|-------------------------|---------------------------|
| **Inputs** | 3 (Bed, Nozzle, Cooling) | 10 (Temperatures only) |
| **Format** | V.P.VAR_* (Simple Mode) | V.E.GLOBAL_* (Extended Mode) |
| **Heated Bed** | Global temperature (all 4 zones) | Individual zone control |
| **Extruder** | Auto-calculated zones | Individual zone temperatures |
| **Fan** | Percentage (0-100%) | Speed (0-255) |
| **Use Case** | Quick setup, standard prints | Multi-zone control, advanced setups |
| **Complexity** | Low | Medium |

---

## Machine Settings (Simple)

**File:** `Components/Export/MachineSettingsComponent.cs`  
**GUID:** `A1B2C3D4-E5F6-7890-ABCD-EF1234567890`

### Inputs

1. **Bed Temperature** (Number) - Heated bed temperature in °C
2. **Nozzle Temperature** (Number) - Extruder nozzle temperature in °C
3. **Cooling Fan** (Number) - Cooling fan percentage (0-100%)

### How It Works

The Simple component uses the **V.P.VAR_*** format, which sets global temperatures that are then distributed by subroutines:

1. **Heated Bed:** Sets global temperature for all 4 zones via `V.P.VAR_heatbedtemp`
2. **Extruder:** Sets global temperature for all extruder zones (except Filling Zone) via `V.P.VAR_extrudertemp`
3. **Fan:** Sets fan speed via `V.P.VAR_fan`

The subroutines (`heatbedTemp_sub.nc`, `extruderTemp_sub.nc`, `fan_sub.nc`) then:
- Read the global variables
- Distribute temperatures to individual zones
- Activate zones as needed

### Automatic Temperature Calculation

When you set a Nozzle Temperature (e.g., 230°C), the system automatically calculates:

- **Filling Zone:** Always 45°C (cooling)
- **Zone 1:** Nozzle - 10°C (e.g., 220°C)
- **Zone 2:** Nozzle - 5°C (e.g., 225°C)
- **Nozzle Zone:** The specified temperature (e.g., 230°C)

All 4 bed zones get the same temperature (the specified bed temperature).

### Output G-Code

```
N10 V.P.VAR_heatbedtemp = 60
N20 L heatbedTemp_sub.nc
N30 V.P.VAR_extrudertemp = 230
N40 L extruderTemp_sub.nc
N50 V.P.VAR_fan = 33
N60 L fan_sub.nc
N70 V.E.GLOBAL_BOOL[72] = TRUE    ← Activates all 4 bed zones
N80 V.E.GLOBAL_BOOL[74] = TRUE
N90 V.E.GLOBAL_BOOL[76] = TRUE
N100 V.E.GLOBAL_BOOL[78] = TRUE
N110 V.E.GLOBAL[41] = 45          ← Filling Zone (separate from V.P.VAR_extrudertemp)
N120 V.E.GLOBAL_BOOL[24] = TRUE   ← Activates extruder zones
N130 V.E.GLOBAL_BOOL[26] = TRUE
N140 V.E.GLOBAL_BOOL[40] = TRUE
N150 V.E.GLOBAL_BOOL[1] = 1       ← Value acceptance (CRITICAL!)
N160 V.E.GLOBAL[27] = 0
N170 L layer_sub.nc
```

### Info Output

```
=== Machine Settings (Simple Mode) ===
Bed Temperature: 60°C
  → All 4 bed zones set to: 60°C

Nozzle Temperature: 230°C
  → Auto-calculated extruder zones:
     Filling Zone: 45°C (cooling)
     Zone 1: 220°C (Nozzle - 10°C)
     Zone 2: 225°C (Nozzle - 5°C)
     Nozzle Zone: 230°C (specified)

Cooling Fan: 33%

Format: V.P.VAR_* (Simple Mode)
Note: Zone temperatures are auto-calculated and used by subroutines
Note: All settings will automatically turn OFF after print completion
```

---

## Machine Settings Extended

**File:** `Components/Export/MachineSettingsExtendedComponent.cs`  
**GUID:** `B1C2D3E4-F5A6-7890-BCDE-F123456789AB`

### Inputs

1. **Bed Zone 1 Temp** (Number) - Temperature in °C (default: 60°C)
2. **Bed Zone 2 Temp** (Number) - Temperature in °C (default: 60°C)
3. **Bed Zone 3 Temp** (Number) - Temperature in °C (default: 60°C)
4. **Bed Zone 4 Temp** (Number) - Temperature in °C (default: 60°C)
5. **Extruder Zone 1 Temp** (Number) - Temperature in °C (default: 220°C)
6. **Extruder Zone 2 Temp** (Number) - Temperature in °C (default: 225°C)
7. **Nozzle Zone Temp** (Number) - Temperature in °C (default: 230°C)
8. **Filling Zone Temp** (Number) - Temperature in °C (default: 45°C, always enabled)
9. **Fan Speed** (Number) - Fan speed 0-255 (default: 80)

### How It Works

The Extended component uses the **V.E.GLOBAL_*** format for direct control:

1. **Extruder Zones:** Sets individual temperatures directly via `V.E.GLOBAL[41/55/57/71]`
2. **Extruder Zone Activation:** Activates zones via `V.E.GLOBAL_BOOL[24/26/40]`
3. **Heated Bed:** Sets global temperature via `V.P.VAR_heatbedtemp` (all zones get same temp)
4. **Bed Zone Activation:** Activates only the zones you want via `V.E.GLOBAL_BOOL[72/74/76/78]`
5. **Fan:** Sets fan speed directly via `V.E.GLOBAL[3]`

### Zone Enable Logic

**Important:** Zones are automatically enabled if their temperature is > 0:
- If `Bed Zone 1 Temp = 60°C` → Zone 1 is enabled
- If `Bed Zone 1 Temp = 0°C` → Zone 1 is disabled
- **Filling Zone** is always enabled at 45°C (cooling zone)

### Output G-Code

**Example: Only Bed Zone 1 enabled at 60°C, all extruder zones enabled:**

```
N10 V.E.GLOBAL[41] = 45          ← Filling Zone temperature
N20 V.E.GLOBAL[55] = 220         ← Extruder Zone 1 temperature
N30 V.E.GLOBAL[57] = 225         ← Extruder Zone 2 temperature
N40 V.E.GLOBAL[71] = 230         ← Nozzle Zone temperature
N50 V.E.GLOBAL_BOOL[24] = TRUE   ← Activate Extruder Zone 1
N60 V.E.GLOBAL_BOOL[26] = TRUE   ← Activate Extruder Zone 2
N70 V.E.GLOBAL_BOOL[40] = TRUE   ← Activate Nozzle Zone
N80 V.E.GLOBAL[3] = 80           ← Fan speed
N90 V.P.VAR_heatbedtemp = 60     ← Global bed temperature (for ALL 4 zones)
N100 L heatbedTemp_sub.nc
N110 V.E.GLOBAL_BOOL[72] = TRUE  ← Activate ONLY Bed Zone 1
N120 V.E.GLOBAL_BOOL[1] = 1      ← Value acceptance (CRITICAL!)
N130 V.E.GLOBAL[27] = 0
N140 L layer_sub.nc
```

**Result:**
- Bed Zone 1: 60°C, **enabled**
- Bed Zones 2, 3, 4: 60°C, **disabled** (temperature is set globally, but zones are not activated)
- All extruder zones: Individual temperatures, **enabled**

### Info Output

```
=== Machine Settings (Extended Multi-Zone Mode) ===
Format: V.E.GLOBAL_* (Advanced Format)

--- Heated Bed Zones (4 subdivided plates) ---
Zone 1: ON @ 60°C (V.E.GLOBAL_BOOL[72])
Zone 2: OFF (temperature set globally, but zone disabled)
Zone 3: OFF (temperature set globally, but zone disabled)
Zone 4: OFF (temperature set globally, but zone disabled)

--- Extruder Zones (4 zones) ---
Filling Zone: ON @ 45°C (V.E.GLOBAL[41])
Heating Zone 1: ON @ 220°C (V.E.GLOBAL_BOOL[24], V.E.GLOBAL[55])
Heating Zone 2: ON @ 225°C (V.E.GLOBAL_BOOL[26], V.E.GLOBAL[57])
Nozzle Zone: ON @ 230°C (V.E.GLOBAL_BOOL[40], V.E.GLOBAL[71])

--- Fan Settings ---
Fan: ON @ 80 (V.E.GLOBAL[3])

Note: All settings will automatically turn OFF after print completion
```

---

## Variable Formats

### V.P.VAR_* Format (Simple Mode)

**Purpose:** Simplified, abstracted variable system using readable names.

**How it works:**
1. Set global variables: `V.P.VAR_heatbedtemp = 60`
2. Call subroutine: `L heatbedTemp_sub.nc`
3. Subroutine reads the variable and sets individual `V.E.GLOBAL_*` variables internally

**Advantages:**
- Simple to use (only 3 variables)
- Backward compatible
- Subroutines handle zone distribution automatically

**Disadvantages:**
- Less control (can't set individual zones)
- Requires subroutines to exist on the robot controller

**Variables:**
- `V.P.VAR_heatbedtemp` - Bed temperature (sets all 4 zones)
- `V.P.VAR_extrudertemp` - Extruder temperature (sets zones 1, 2, nozzle, NOT filling)
- `V.P.VAR_fan` - Fan speed (0-100%)

### V.E.GLOBAL_* Format (Extended Mode)

**Purpose:** Direct access to KUKA Global Variables for precise control.

**How it works:**
1. Set individual temperatures: `V.E.GLOBAL[71] = 230`
2. Activate zones: `V.E.GLOBAL_BOOL[40] = TRUE`
3. No subroutines needed (direct control)

**Advantages:**
- Full control over each zone
- No subroutines required
- Official KUKA format (from syntax documentation)

**Disadvantages:**
- More complex (many variables to manage)
- Requires knowledge of variable indices

**Variable Indices:**

**Heated Bed Zones:**
- `V.E.GLOBAL_BOOL[72]` = Bed Zone 1 enable
- `V.E.GLOBAL_BOOL[74]` = Bed Zone 2 enable
- `V.E.GLOBAL_BOOL[76]` = Bed Zone 3 enable
- `V.E.GLOBAL_BOOL[78]` = Bed Zone 4 enable
- **Note:** Bed temperatures are set globally via `V.P.VAR_heatbedtemp` (no individual temperature variables)

**Extruder Zones:**
- `V.E.GLOBAL_BOOL[44]` = Filling Zone enable (also used for fan)
- `V.E.GLOBAL[41]` = Filling Zone temperature
- `V.E.GLOBAL_BOOL[24]` = Extruder Zone 1 enable
- `V.E.GLOBAL[55]` = Extruder Zone 1 temperature
- `V.E.GLOBAL_BOOL[26]` = Extruder Zone 2 enable
- `V.E.GLOBAL[57]` = Extruder Zone 2 temperature
- `V.E.GLOBAL_BOOL[40]` = Nozzle Zone enable
- `V.E.GLOBAL[71]` = Nozzle Zone temperature

**Fan:**
- `V.E.GLOBAL[3]` = Fan speed (0-255)
- **Note:** `V.E.GLOBAL_BOOL[44]` for fan is NOT needed (already set by `fan_sub.nc` if `V.P.VAR_fan` is used)

**Value Acceptance:**
- `V.E.GLOBAL_BOOL[1] = 1` - **CRITICAL!** Must be set after all value changes to ensure settings are applied before movement code starts.

---

## Heated Bed Zone Logic

### How Bed Zones Work

**Important Understanding:** The heated bed uses a **global temperature setting** with **individual zone activation**.

1. **Global Temperature Setting:**
   ```
   N10 V.P.VAR_heatbedtemp = 60
   N20 L heatbedTemp_sub.nc
   ```
   This sets the temperature for **ALL 4 zones** (60°C for zones 1, 2, 3, and 4).

2. **Individual Zone Activation:**
   ```
   N30 V.E.GLOBAL_BOOL[72] = TRUE   ← Activate Zone 1
   N40 V.E.GLOBAL_BOOL[74] = TRUE   ← Activate Zone 2
   N50 V.E.GLOBAL_BOOL[76] = TRUE   ← Activate Zone 3
   N60 V.E.GLOBAL_BOOL[78] = TRUE   ← Activate Zone 4
   ```

### Example: Only Zone 1 Enabled

**Input:** Bed Zone 1 Temp = 60°C, all other zones = 0°C

**Output:**
```
N10 V.P.VAR_heatbedtemp = 60      ← Sets temperature for ALL 4 zones (global)
N20 L heatbedTemp_sub.nc
N30 V.E.GLOBAL_BOOL[72] = TRUE    ← Activates ONLY Zone 1
```

**Result:**
- Zone 1: 60°C, **enabled** (heating)
- Zones 2, 3, 4: 60°C, **disabled** (temperature is set, but zones are not activated)

**Why this works:**
- The global temperature command (`V.P.VAR_heatbedtemp`) sets the temperature for all zones
- But only activated zones (via `V.E.GLOBAL_BOOL[72/74/76/78]`) actually heat up
- This allows you to have different zones at the same temperature but only activate the ones you need

### Extended Mode Bed Zones

In Extended Mode, you can set different temperatures for each zone, but the system still uses the global `V.P.VAR_heatbedtemp` command. The highest temperature among enabled zones is used as the global temperature, and then individual zones are activated.

**Example:**
- Bed Zone 1 Temp = 60°C (enabled)
- Bed Zone 2 Temp = 65°C (enabled)
- Bed Zone 3 Temp = 0°C (disabled)
- Bed Zone 4 Temp = 0°C (disabled)

**Output:**
```
N10 V.P.VAR_heatbedtemp = 65      ← Uses highest enabled temperature
N20 L heatbedTemp_sub.nc
N30 V.E.GLOBAL_BOOL[72] = TRUE   ← Activate Zone 1 (will be 65°C, not 60°C)
N40 V.E.GLOBAL_BOOL[74] = TRUE   ← Activate Zone 2 (65°C)
```

**Note:** Since bed temperatures are set globally, all enabled zones will have the same temperature (the highest specified). For truly individual zone temperatures, you would need separate temperature variables (not currently available in the syntax).

---

## Automatic Temperature Calculation

### Simple Mode Auto-Calculation

When you enter a **Nozzle Temperature** in Simple Mode, the system automatically calculates:

| Zone | Calculation | Example (Nozzle = 230°C) |
|------|-------------|---------------------------|
| Filling Zone | Always 45°C | 45°C |
| Zone 1 | Nozzle - 10°C | 220°C |
| Zone 2 | Nozzle - 5°C | 225°C |
| Nozzle Zone | Specified temperature | 230°C |

**Physical Arrangement (top to bottom):**
1. Zone 1 (top): Nozzle - 10°C
2. Zone 2 (middle): Nozzle - 5°C
3. Nozzle Zone (bottom): Specified temperature
4. Filling Zone: 45°C (cooling)

**Why this gradient?**
- Prevents overheating by gradually increasing temperature from top to bottom
- Ensures material is properly heated before reaching the nozzle
- Filling Zone at 45°C provides cooling to prevent material from melting too early

### Bed Zones Auto-Calculation

All 4 bed zones get the same temperature (the specified bed temperature).

**Example:** Bed Temperature = 60°C
- Zone 1: 60°C
- Zone 2: 60°C
- Zone 3: 60°C
- Zone 4: 60°C

---

## DXR Integration

### Automatic Insertion

Machine Settings are automatically inserted into DXR files at the correct position:

```
1. Header (comments)
2. ;=================================
3. G90 (absolute positioning)
4. Machine Start Settings (N10, N20, ...) ← HERE
5. Layer Initialization (N70, N80, ...)
6. Movements (N100, N110, ...)
7. ...
8. Machine End Settings (N9999994, ...)
9. M29
```

### Components That Support Machine Settings

1. **DXR Postprocessor** - Input 1: "Machine Settings"
2. **DXR Generator** - Input 3: "Machine Settings"

### End Settings

Both components automatically add shutdown commands at the end:

**Simple Mode:**
```
N9999994 V.P.VAR_heatbedtemp = 0
N9999995 L heatbedTemp_sub.nc
N9999996 V.P.VAR_fan = 0
N9999997 L fan_sub.nc
N9999998 V.P.VAR_extrudertemp = 0
N9999999 L extruderTemp_sub.nc
M29
```

**Extended Mode:**
```
N9999990 V.E.GLOBAL_BOOL[72] = FALSE
N9999991 V.E.GLOBAL_BOOL[74] = FALSE
N9999992 V.E.GLOBAL_BOOL[76] = FALSE
N9999993 V.E.GLOBAL_BOOL[78] = FALSE
N9999994 V.E.GLOBAL_BOOL[44] = FALSE
N9999995 V.E.GLOBAL_BOOL[24] = FALSE
N9999996 V.E.GLOBAL_BOOL[26] = FALSE
N9999997 V.E.GLOBAL_BOOL[40] = FALSE
N9999998 V.E.GLOBAL[3] = 0
M29
```

---

## Examples

### Example 1: Simple Mode - Standard Print

**Inputs:**
- Bed Temperature: 60°C
- Nozzle Temperature: 230°C
- Cooling Fan: 33%

**Output:**
```
N10 V.P.VAR_heatbedtemp = 60
N20 L heatbedTemp_sub.nc
N30 V.P.VAR_extrudertemp = 230
N40 L extruderTemp_sub.nc
N50 V.P.VAR_fan = 33
N60 L fan_sub.nc
N70 V.E.GLOBAL_BOOL[72] = TRUE
N80 V.E.GLOBAL_BOOL[74] = TRUE
N90 V.E.GLOBAL_BOOL[76] = TRUE
N100 V.E.GLOBAL_BOOL[78] = TRUE
N110 V.E.GLOBAL[41] = 45
N120 V.E.GLOBAL_BOOL[24] = TRUE
N130 V.E.GLOBAL_BOOL[26] = TRUE
N140 V.E.GLOBAL_BOOL[40] = TRUE
N150 V.E.GLOBAL_BOOL[1] = 1
N160 V.E.GLOBAL[27] = 0
N170 L layer_sub.nc
```

**Auto-calculated temperatures:**
- Bed: All 4 zones at 60°C
- Extruder: Filling 45°C, Zone 1: 220°C, Zone 2: 225°C, Nozzle: 230°C

### Example 2: Extended Mode - Only Bed Zone 1

**Inputs:**
- Bed Zone 1 Temp: 60°C
- Bed Zone 2 Temp: 0°C (disabled)
- Bed Zone 3 Temp: 0°C (disabled)
- Bed Zone 4 Temp: 0°C (disabled)
- Extruder Zone 1 Temp: 220°C
- Extruder Zone 2 Temp: 225°C
- Nozzle Zone Temp: 230°C
- Filling Zone Temp: 45°C
- Fan Speed: 80

**Output:**
```
N10 V.E.GLOBAL[41] = 45
N20 V.E.GLOBAL[55] = 220
N30 V.E.GLOBAL[57] = 225
N40 V.E.GLOBAL[71] = 230
N50 V.E.GLOBAL_BOOL[24] = TRUE
N60 V.E.GLOBAL_BOOL[26] = TRUE
N70 V.E.GLOBAL_BOOL[40] = TRUE
N80 V.E.GLOBAL[3] = 80
N90 V.P.VAR_heatbedtemp = 60
N100 L heatbedTemp_sub.nc
N110 V.E.GLOBAL_BOOL[72] = TRUE
N120 V.E.GLOBAL_BOOL[1] = 1
N130 V.E.GLOBAL[27] = 0
N140 L layer_sub.nc
```

**Result:**
- Bed Zone 1: 60°C, **enabled**
- Bed Zones 2, 3, 4: 60°C (set globally), **disabled**
- All extruder zones: Individual temperatures, **enabled**

### Example 3: Extended Mode - All Zones Enabled

**Inputs:**
- All Bed Zone Temps: 60°C
- Extruder Zone 1 Temp: 220°C
- Extruder Zone 2 Temp: 225°C
- Nozzle Zone Temp: 230°C
- Filling Zone Temp: 45°C
- Fan Speed: 80

**Output:**
```
N10 V.E.GLOBAL[41] = 45
N20 V.E.GLOBAL[55] = 220
N30 V.E.GLOBAL[57] = 225
N40 V.E.GLOBAL[71] = 230
N50 V.E.GLOBAL_BOOL[24] = TRUE
N60 V.E.GLOBAL_BOOL[26] = TRUE
N70 V.E.GLOBAL_BOOL[40] = TRUE
N80 V.E.GLOBAL[3] = 80
N90 V.P.VAR_heatbedtemp = 60
N100 L heatbedTemp_sub.nc
N110 V.E.GLOBAL_BOOL[72] = TRUE
N120 V.E.GLOBAL_BOOL[74] = TRUE
N130 V.E.GLOBAL_BOOL[76] = TRUE
N140 V.E.GLOBAL_BOOL[78] = TRUE
N150 V.E.GLOBAL_BOOL[1] = 1
N160 V.E.GLOBAL[27] = 0
N170 L layer_sub.nc
```

---

## Important Notes

### Value Acceptance Command

**CRITICAL:** After all machine settings are set, the command `V.E.GLOBAL_BOOL[1] = 1` must be executed to ensure all value changes are accepted before movement code starts. This is automatically added by both components.

### Redundant Commands Removed

The implementation has been optimized to avoid redundant commands:

- **Fan:** `V.E.GLOBAL_BOOL[44]` is NOT needed in Simple Mode (already set by `fan_sub.nc`)
- **Fan:** `V.E.GLOBAL[3]` is NOT needed in Simple Mode (already set by `V.P.VAR_fan`)
- **Extruder:** Individual zone temperatures are NOT set in Simple Mode if `V.P.VAR_extrudertemp` is used (handled by subroutines)

### Zone Enable Logic

- Zones are automatically enabled if temperature > 0
- Zones are automatically disabled if temperature = 0
- Filling Zone is always enabled at 45°C (cooling zone)

### Bed Zone Behavior

- Bed temperatures are set **globally** for all 4 zones
- Individual zones are then **activated** or **deactivated**
- If only Zone 1 is enabled, Zones 2-4 still get the temperature but remain disabled

---

## Troubleshooting

### Settings Not Applied

- Check that `V.E.GLOBAL_BOOL[1] = 1` is present after all settings
- Verify that subroutines exist on the robot controller (for Simple Mode)
- Ensure temperatures are > 0 for zones you want enabled

### Zones Not Heating

- Verify that `V.E.GLOBAL_BOOL[72/74/76/78] = TRUE` is set for bed zones
- Verify that `V.E.GLOBAL_BOOL[24/26/40] = TRUE` is set for extruder zones
- Check that temperatures are > 0

### Fan Not Working

- In Simple Mode: Check that `V.P.VAR_fan` is set and `L fan_sub.nc` is called
- In Extended Mode: Check that `V.E.GLOBAL[3]` is set (0-255)
- Note: `V.E.GLOBAL_BOOL[44]` is NOT needed (handled by subroutines)

---

## Technical Details

### File Locations

- **MachineSettings Class:** `Types/MachineSettings.cs`
- **Simple Component:** `Components/Export/MachineSettingsComponent.cs`
- **Extended Component:** `Components/Export/MachineSettingsExtendedComponent.cs`
- **DXR Integration:** `Utils/DXRHelper.cs`

### Method: GetStartGCode()

Generates the start G-code sequence based on the mode:
- **Simple Mode:** Uses `V.P.VAR_*` format with zone activation
- **Extended Mode:** Uses `V.E.GLOBAL_*` format with individual temperatures

### Method: GetEndGCode()

Generates the shutdown sequence:
- **Simple Mode:** Sets all `V.P.VAR_*` variables to 0
- **Extended Mode:** Sets all `V.E.GLOBAL_BOOL[*]` to FALSE and `V.E.GLOBAL[3]` to 0

---

## Version History

- **2025-12-04:** Complete rewrite based on technician specifications
  - Removed redundant commands
  - Added `V.E.GLOBAL_BOOL[1] = 1` for value acceptance
  - Clarified bed zone logic (global temperature, individual activation)
  - Optimized Simple Mode to include zone activation
  - Extended Mode simplified (temperature inputs only, auto-enable logic)

---

## References

- KUKA DXR Syntax Documentation: `Syntax Gcode KUKA-CNC.pdf`
- DXR Format Documentation: `DXR_FORMAT_DOCUMENTATION.md`
- Implementation: `Types/MachineSettings.cs`

