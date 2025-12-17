# Keep Starting Gear

**Version:** 1.4.6
**Author:** Blackhorse311
**License:** MIT
**SPT Compatibility:** 4.0.x (tested on 4.0.8)

A mod for SPT that automatically protects your starting gear. Die in raid? Your equipment is restored. No more gear fear!

---

## Features

### Core Features
- **Automatic Snapshots**: Your gear is automatically saved when you enter a raid (default behavior)
- **Death Protection**: If you die, your saved equipment is automatically restored
- **Server-Side Restoration**: No "Run-Through" penalty - restoration happens before death processing
- **Modded Items Support**: Works with any modded weapons, armor, and equipment
- **Sound Feedback**: Plays a satisfying sound when snapshots are taken

### Snapshot Modes
Choose how you want snapshots to work:
- **Auto Only** (Default): Automatic snapshot at raid start, no manual snapshots
- **Auto + Manual**: Automatic snapshot at raid start, plus manual snapshots via keybind
- **Manual Only**: Full control - only saves when you press the keybind

### Protection Options
- **FIR Protection**: Optionally exclude Found-in-Raid items from snapshots (prevents duplication exploits)
- **Insurance Integration**: Optionally exclude insured items (let insurance handle them normally)
- **Map Transfer Protection**: Choose whether to re-snapshot when transferring between maps or keep original

### Quick Setup Presets
- **Casual**: Maximum protection with minimal hassle - auto-snapshot, all items protected
- **Hardcore**: More risk, more control - manual snapshots only, FIR & insured items excluded

---

## Requirements

- SPT 4.0.x (tested on 4.0.8)
- BepInEx 5.x (included with SPT)

---

## Installation

### Automatic (Recommended)

Install via The Forge mod manager.

### Manual Installation

1. Download the latest release
2. Extract the archive directly into your SPT root folder (where SPT.Server.exe is located)
3. The folder structure should look like:

```
[SPT Root]/
├── BepInEx/
│   └── plugins/
│       └── Blackhorse311-KeepStartingGear/
│           ├── Blackhorse311.KeepStartingGear.dll
│           └── snapshots/  (created automatically)
├── user/
│   └── mods/
│       └── Blackhorse311-KeepStartingGear/
│           └── Blackhorse311.KeepStartingGear.Server.dll
└── SPT.Server.exe
```

---

## Usage

### Default Behavior (Casual Mode)
1. **Enter a raid** - your gear is automatically saved
2. A green notification confirms: "Auto-Snapshot Saved - X items protected"
3. If you **die**, your equipment is automatically restored
4. If you **extract successfully**, the snapshot is cleared

### Manual Mode
1. **Enter a raid** with the gear you want to protect
2. **Press Ctrl+Alt+F8** (default keybind) to create a snapshot
3. A green notification confirms the snapshot
4. If you **die**, your saved equipment is restored

---

## Configuration

Access mod settings via **F12** (BepInEx Configuration Manager) or edit the config file directly.

Config file location: `BepInEx/config/com.blackhorse311.keepstartinggear.cfg`

### 0. Quick Setup

| Setting | Options | Description |
|---------|---------|-------------|
| Configuration Preset | Casual / Hardcore / Custom | Quick setup for different playstyles |

### 1. General

| Setting | Default | Description |
|---------|---------|-------------|
| Enable KSG Mod | true | Master switch to enable/disable the mod |

### 2. Snapshot Behavior

| Setting | Default | Description |
|---------|---------|-------------|
| Snapshot Mode | Auto Only | When snapshots are taken (Auto Only / Auto+Manual / Manual Only) |
| Max Manual Snapshots | 1 | Maximum manual snapshots per raid in Auto+Manual mode (1-10) |
| Protect FIR Items | false | Exclude Found-in-Raid items from snapshots |
| Exclude Insured Items | false | Let insurance handle insured items |
| Re-Snapshot on Map Transfer | false | Take new snapshot when transferring maps |
| Play Snapshot Sound | true | Play sound effect when snapshot is taken |
| Warn on Snapshot Overwrite | true | Show warning when overwriting existing snapshot |

### 3. Keybind

| Setting | Default | Description |
|---------|---------|-------------|
| Manual Snapshot Keybind | Ctrl+Alt+F8 | Keybind for manual snapshots |

### 4. Inventory Slots

Every equipment slot can be individually enabled or disabled. All default to **true**.

| Slot | Description |
|------|-------------|
| First Primary Weapon | Main weapon (rifles, SMGs) |
| Second Primary Weapon | Backup weapon |
| Holster | Pistol/sidearm |
| Scabbard | Melee weapon |
| Headwear | Helmets, hats |
| Earpiece | Headsets, comtacs |
| Face Cover | Masks, balaclavas |
| Eyewear | Glasses, goggles |
| Arm Band | Identification bands |
| Tactical Vest | Chest rigs, plate carriers |
| Armor Vest | Body armor |
| Pockets | Built-in pocket storage |
| Backpack | Main loot storage |
| Secured Container | Secure container contents |
| Compass | Navigation |
| Special Slot 1-3 | Injectors, stims |

### 5. Logging

| Setting | Default | Description |
|---------|---------|-------------|
| Debug Mode | false | Enable verbose logging |
| Log Snapshot Creation | true | Log when snapshots are created |
| Log Snapshot Restoration | true | Log when snapshots are restored |

---

## How It Works

### Automatic Mode (Default)
1. When you enter a raid, your current gear is automatically captured
2. The snapshot is saved as a JSON file
3. If you die, the server restores your gear before normal death processing
4. If you extract, the snapshot is cleared

### Manual Mode
1. You press the keybind to capture your current gear
2. Only the gear at that moment is protected
3. Items picked up after the snapshot are NOT restored on death

### Map Transfer Behavior
When transferring between maps (e.g., Ground Zero → Streets):
- **Default**: Keeps your original snapshot (first raid's starting gear)
- **Optional**: Re-snapshot with current gear at transfer

### Mid-Raid Protection
Settings are locked when a raid starts. Changing snapshot mode mid-raid shows a warning and has no effect until the next raid. This prevents exploits like switching to manual mode after losing items.

---

## Compatibility

- **SPT Version**: 4.0.x (tested on 4.0.8)
- **Fika**: Not tested - may not work in multiplayer scenarios
- **Other Mods**: Should be compatible with most mods, including mods that add custom items

### Mod Compatibility

| Mod | Status | Notes |
|-----|--------|-------|
| **SVM (Server Value Modifier)** | **Partially Compatible** | Works alongside SVM with exceptions noted below. |

**SVM Compatibility Details:**
- **Softcore Mode**: Incompatible - will conflict with KSG's gear restoration. Disable this if using KSG.
- **Safe Exit**: Incompatible - interferes with KSG's death detection. Disable this if using KSG.
- **Other SVM settings**: Use at your own risk. Most should work fine, but gear-related settings may produce unexpected results when combined with KSG.

---

## Troubleshooting

### Snapshot not saving
- Ensure the mod is enabled in F12 settings
- Check that you're in Manual or Auto+Manual mode if using keybind
- Look for errors in the BepInEx console

### Inventory not restoring
- Verify both client and server components are installed
- Check the SPT server console for error messages
- Ensure a snapshot exists (check the snapshots folder)

### Items missing from snapshot
- Check that the relevant inventory slot is enabled in settings
- FIR items are excluded if "Protect FIR Items" is enabled
- Insured items are excluded if "Exclude Insured Items" is enabled

---

## Building from Source

### Prerequisites

- Visual Studio 2022 or later
- .NET Framework 4.7.1 SDK (for client)
- .NET 9.0 SDK (for server)

### Build Steps

1. Clone the repository
2. Update the reference paths in the `.csproj` files to match your SPT installation
3. Build the solution:

```bash
dotnet build src/server/Blackhorse311.KeepStartingGear.csproj
dotnet build src/servermod/Blackhorse311.KeepStartingGear.Server.csproj
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Credits

- **Author**: Blackhorse311
- **SPT Team**: For the amazing SPT project
- **BepInEx Team**: For the modding framework

### Community Contributors

Thanks to everyone who reported bugs and helped improve the mod:

- **@Troyoza** - Reported hardcoded paths and character screen bounce issues
- **@Wolthon** - Provided detailed logs for restoration flow debugging, reported grid contents capture issue
- **@rSlade** - Early testing and feedback
- **@20fpsguy** - Reported scav raid snapshot saving and false extraction detection
- **@Recker** - Reported secure container deletion, ammo stacks, and empty slots issues
- **@VeiledFury** - Reported release package structure issue
- **@immortal_wombat** - Reported medkit, armor durability, and armor plates issues
- **@Alcorsa** - Reported SPT 4.0.8 compatibility issue
- **@calafex** - Reported dogtag metadata and Surv12 restoration issues
- **@rimmyjob** - Reported secure container deletion when disabled (GitHub #4)
- **@Matheus** - Reported gamma container disappearing during raid loading
- **@wolthon** - Reported secure container loss with disabled setting

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.

---

## Support

If you encounter bugs or have suggestions:

### Bug Reports
Please use our [GitHub Issue Tracker](https://github.com/Blackhorse311/KeepStartingGear/issues) with the bug report template. You'll be asked to provide:
- Your SPT and mod versions
- Steps to reproduce the issue
- **Client log**: `BepInEx/LogOutput.log`
- **Server log**: `SPT/user/logs/` (most recent file)
- **Snapshot file** (if applicable): `BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/`

### Community Support
- [The Forge Mod Page](https://forge.sp-tarkov.com/mod/2470/keep-starting-gear) - Leave comments and feedback
- [SPT Discord](https://discord.gg/spt) - Community help and discussion
