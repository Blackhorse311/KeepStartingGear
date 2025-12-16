# FIKA Integration Testing Guide

> **Branch:** `feature/fika-integration`
> **Status:** Experimental - Needs Testing

This document provides instructions for testing the experimental FIKA integration for Keep Starting Gear.

---

## Overview

FIKA (multiplayer mod) handles player death differently than standard SPT. When you die in FIKA:
1. Your inventory is serialized **client-side** before being sent to the server
2. Standard server-side mods can't intercept this in time

This experimental integration hooks into FIKA's death handling to restore your inventory **before** FIKA serializes it.

---

## Requirements

- SPT 4.0.x (tested on 4.0.8)
- FIKA installed and working
- This branch of Keep Starting Gear (`feature/fika-integration`)

---

## Installation

### Option 1: Download ZIP
1. Go to https://github.com/Blackhorse311/KeepStartingGear/tree/feature/fika-integration
2. Click the green **Code** button → **Download ZIP**
3. Extract and install both client and server components as usual

### Option 2: Git Clone
```bash
git clone -b feature/fika-integration https://github.com/Blackhorse311/KeepStartingGear.git
```

### Option 3: Switch Existing Clone
```bash
git fetch origin
git checkout feature/fika-integration
```

### Build from Source
```bash
dotnet build src/server/Blackhorse311.KeepStartingGear.csproj --configuration Release
dotnet build src/servermod/Blackhorse311.KeepStartingGear.Server.csproj --configuration Release
```

---

## Testing Procedure

### Before Testing
1. Verify FIKA works normally without Keep Starting Gear
2. Make a backup of your profile (`SPT/user/profiles/`)
3. Enable Debug Mode in F12 config (optional but helpful)

### Test Scenarios to Try

#### Scenario 1: Basic Death Test (Solo with FIKA)
1. Start a raid on any map (solo, but with FIKA installed)
2. Wait for auto-snapshot or take manual snapshot
3. Die to any cause
4. Check if inventory was restored

#### Scenario 2: Multiplayer Host
1. Host a FIKA raid
2. Take snapshot
3. Die while clients are connected
4. Check restoration

#### Scenario 3: Multiplayer Client
1. Join a FIKA raid as client
2. Take snapshot
3. Die
4. Check restoration

#### Scenario 4: Various Death Types
Test dying to:
- AI scavs/PMCs
- Fall damage
- Dehydration/exhaustion
- Bleeding out
- Explosion

### What to Check After Death

1. **Equipment Slots:** Are all equipped items restored?
2. **Attachments:** Are weapon attachments correct?
3. **Durability:** Is armor/weapon durability correct?
4. **Ammo:** Is magazine ammo count correct?
5. **Medical:** Are medkit uses preserved?
6. **Container Contents:** Are backpack/rig contents restored?

---

## Reading the Logs

The FIKA integration logs extensively with `[FIKA-*]` prefixes. Open `BepInEx/LogOutput.log` and search for these:

### Detection Phase
```
[FIKA-DETECT] Starting FIKA detection...
[FIKA-DETECT] SUCCESS! FIKA detected via BepInEx Chainloader!
[FIKA-DETECT] Found CoopGame: Fika.Core.Coop.GameMode.CoopGame
```

### Initialization Phase
```
[FIKA-INIT] Starting FIKA Integration Initialization
[FIKA-PATCH] Applying Harmony patch...
[FIKA-PATCH] SUCCESS! Harmony patch applied!
```

### Death Event (when you die)
```
[FIKA-DEATH] DEATH EVENT INTERCEPTED!
[FIKA-DEATH] Player extracted successfully!
[FIKA-DEATH] Invoking snapshot restoration callback...
```

### Restoration Phase
```
[FIKA-RESTORER] Starting inventory restoration attempt
[FIKA-RESTORER] SNAPSHOT FOUND!
[FIKA-ITEMS] Creating items from snapshot...
[FIKA-ITEMS] Item restoration complete!
```

### What to Look For

**Success indicators:**
- `[FIKA-DETECT] SUCCESS! FIKA detected`
- `[FIKA-PATCH] SUCCESS! Harmony patch applied!`
- `[FIKA-ITEMS] Item restoration complete!`
- `[FIKA-ITEMS] Created: X, Placed: Y, Failed: 0`

**Failure indicators:**
- `[FIKA-DETECT] FIKA not detected`
- `[FIKA-PATCH] Could not find HealthController_DiedEvent`
- `[FIKA-ITEMS] Could not access ItemFactory`
- `[FIKA-ITEMS] Failed: X` (non-zero failed count)

---

## Reporting Results

Please use the **FIKA Integration Test Report** issue template:
https://github.com/Blackhorse311/KeepStartingGear/issues/new?template=fika_testing.yml

### Required Information
1. **Test result** (success/partial/failed/crash)
2. **Versions** (SPT, FIKA, KSG commit)
3. **Your role** (host/client/solo)
4. **Test scenario** (what you did)
5. **FIKA log messages** (the `[FIKA-*]` sections from LogOutput.log)

### How to Get the Log Messages
1. Open `BepInEx/LogOutput.log` in a text editor
2. Search for `[FIKA-` (with the bracket and dash)
3. Copy all lines containing `[FIKA-` prefixes
4. Paste into the issue

---

## Known Limitations

- **Grid placement:** Items in containers may not be placed in exact grid positions
- **Complex nested items:** Deeply nested items (attachments on attachments) may have issues
- **Client-side only:** If client-side restoration fails, server-side fallback should still work

---

## Troubleshooting

### FIKA Not Detected
Check if FIKA is loading correctly:
```
[Info   : BepInEx] Loading [Fika.Core X.X.X]
```

### Patch Not Applied
FIKA's code structure may have changed. Look for:
```
[FIKA-PATCH] Could not find HealthController_DiedEvent method!
```
Report this with your FIKA version.

### Items Not Restored
Check if ItemFactory was accessible:
```
[FIKA-FACTORY] Could not find ItemFactoryClass type!
```

### Fallback to Server-Side
If you see:
```
[FIKA-ITEMS] falling back to server-side restoration
```
The client-side restoration failed, but server-side may still work. Check your inventory after raid ends.

---

## Questions?

- **Issues:** https://github.com/Blackhorse311/KeepStartingGear/issues
- **Discussions:** SPT Discord

Thank you for helping test this experimental feature!
