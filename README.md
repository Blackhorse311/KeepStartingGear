# Keep Starting Gear

**The cure for gear fear.** Protect your loadout. Die without consequences.

[![SPT 4.0.x](https://img.shields.io/badge/SPT-4.0.x-green.svg)](https://forge.sp-tarkov.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![VirusTotal](https://img.shields.io/badge/VirusTotal-0%2F70-brightgreen.svg)](https://www.virustotal.com/gui/file/ff232a9db482b915d31ac82ac262af5379372e27698f35c82bf03deb668d3924)

---

## What It Does

Keep Starting Gear saves your equipment when you enter a raid and restores it if you die. Extract successfully? Keep your loot. Die? Get your starting gear back.

**No Run-Through penalty. No exploits. Just protection.**

### 30 Seconds to Understand

```
Enter Raid → Gear auto-saved → Die → Gear restored
                              → Extract → Keep your loot
```

---

## Key Features

| Feature | Description |
|---------|-------------|
| **Automatic Snapshots** | Gear saved at raid start (default) or manually via keybind |
| **Full Restoration** | Durability, ammo counts, medical uses, armor plates - all preserved |
| **No Penalties** | Server-side restoration bypasses "Run-Through" status |
| **Slot Control** | Protect exactly what you want - 16 individual slot toggles |
| **Modded Items** | Works with any custom weapons, armor, and equipment |
| **SVM Compatible** | Works with Server Value Modifier (requires specific settings) |

---

## Quick Start

### Installation

**Automatic (Recommended)**

Install via The Forge mod manager.

**Manual Installation**

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
├── SPT/
│   └── user/
│       └── mods/
│           └── Blackhorse311-KeepStartingGear/
│               └── Blackhorse311.KeepStartingGear.Server.dll
└── SPT.Server.exe
```

### Default Behavior

1. **Enter raid** - green notification confirms snapshot
2. **Die** - gear automatically restored
3. **Extract** - snapshot cleared, loot kept

### Configuration

Press **F12** → Find "Keep Starting Gear" → Adjust settings

---

## Playstyle Presets

| Preset | Description |
|--------|-------------|
| **Casual** (Default) | Maximum protection, zero hassle. Auto-snapshot at raid start, all slots protected. |
| **Hardcore** | Manual snapshots only. Exclude Found-in-Raid and insured items. Risk what you find, protect what you bring. |
| **Custom** | Fine-tune every setting: snapshot mode, individual slots, keybinds, notifications. |

---

## Snapshot Modes

| Mode | Behavior |
|------|----------|
| **Auto Only** (Default) | Automatic snapshot at raid start, no manual snapshots |
| **Auto + Manual** | Automatic snapshot at raid start, plus manual snapshots via keybind |
| **Manual Only** | Full control - only saves when you press the keybind (Ctrl+Alt+F8) |

---

## Protection Options

| Option | Description |
|--------|-------------|
| **FIR Protection** | Exclude Found-in-Raid items from snapshots (prevents duplication exploits) |
| **Insurance Integration** | Exclude insured items (let insurance handle them normally) |
| **Map Transfer Protection** | Choose whether to re-snapshot when transferring between maps |

---

## Configuration Reference

Access mod settings via **F12** (BepInEx Configuration Manager) or edit the config file directly.

Config file location: `BepInEx/config/com.blackhorse311.keepstartinggear.cfg`

### Snapshot Behavior

| Setting | Default | Description |
|---------|---------|-------------|
| Snapshot Mode | Auto Only | When snapshots are taken |
| Max Manual Snapshots | 1 | Manual snapshots allowed per raid (1-10) |
| Protect FIR Items | false | Exclude Found-in-Raid items |
| Exclude Insured Items | false | Let insurance handle insured items |
| Re-Snapshot on Map Transfer | false | Take new snapshot when transferring maps |
| Play Snapshot Sound | true | Audio feedback when snapshot is taken |
| Show Notifications | true | On-screen notifications |

### Keybind

| Setting | Default | Description |
|---------|---------|-------------|
| Manual Snapshot Keybind | Ctrl+Alt+F8 | Keybind for manual snapshots |

### Inventory Slots

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

| Mod/Version | Status | Notes |
|-------------|--------|-------|
| **SPT 4.0.x** | ✅ Supported | Tested on 4.0.11 |
| **SPT 3.x** | ❌ Not Supported | [See explanation below](#older-spt-versions) |
| **SVM** | ✅ Compatible | Disable Softcore Mode & Safe Exit |
| **FIKA** | ❌ Not Compatible | [See explanation below](#fika-incompatibility) |
| **Custom Items** | ✅ Full Support | Works with any modded gear |

### Older SPT Versions

**Keep Starting Gear requires SPT 4.0.x.** It does not support SPT 3.11.4 or earlier versions.

#### Why Older Versions Aren't Supported

We investigated SPT 3.11.4 compatibility (see [ADR-002](docs/adr/ADR-002-spt-3114-compatibility.md) for full analysis) and found:

| What's The Same | What's Different |
|-----------------|------------------|
| Equipment slots (all 16) | Server mod installation path |
| Item serialization format | NuGet package versions |
| Exit status handling | API signatures (unknown) |
| BepInEx plugin system | Field vs property access patterns |

While the core game systems are ~95% identical, the **server-side mod loading mechanism is completely different**:

```
SPT 3.11.4: BepInEx/plugins/spt/{ModFolder}/
SPT 4.0.x:  SPT_Data/Server/mods/{ModFolder}/
```

Additionally:
- Unknown if SPT 3.11.4 NuGet packages exist for compilation
- The `MatchCallbacks` API our server mod inherits from may have changed
- The author doesn't run 3.11.4 and cannot test changes

#### Recommendation for 3.11.4 Users

**Upgrade to SPT 4.0.x** - It's the current version with active support and the best modding ecosystem.

---

### FIKA Incompatibility

**Keep Starting Gear does not work with FIKA** (the multiplayer co-op mod). This is due to fundamental architectural differences, not a simple oversight.

#### Why It Won't Work

FIKA processes death differently than single-player SPT:

| Single-Player SPT | FIKA Multiplayer |
|-------------------|------------------|
| Death → Server intercepts → Restore gear → Skip deletion | Death → Network sync → Profile serialized → Server notified |

In FIKA, the player's profile is serialized and sent to the server **before** our restoration logic can run. By the time we could intercept, it's too late - the "dead" inventory has already been saved.

#### Technical Details

We thoroughly investigated FIKA compatibility (see [ADR-001](docs/adr/ADR-001-fika-compatibility.md) for full analysis):

- FIKA's `CoopGame.SavePlayer()` serializes the dead profile before our server-side intercept
- FIKA's event system (`FikaGameEndedEvent`) fires too early for inventory restoration
- The narrow timing window between death and profile serialization makes reliable restoration difficult
- Host vs. client player distinction adds complexity

#### Why We're Not Adding Support

1. **Testing**: The mod author doesn't use FIKA and cannot adequately test changes
2. **Maintenance**: FIKA updates frequently; each update could break compatibility
3. **Complexity**: Would require ~500-1000 lines of FIKA-specific code
4. **Reliability**: Timing-dependent restoration is fragile

#### Community Contributions Welcome

If you're a FIKA user who wants this functionality and can commit to testing, the project is MIT licensed. You're welcome to:
- Fork the project and add FIKA support
- Submit a pull request with a FIKA compatibility layer
- Maintain a FIKA-specific version

We'd be happy to review and potentially merge well-tested FIKA support contributed by the community.

---

## SVM (Server Value Modifier) Compatibility

If you use SVM alongside Keep Starting Gear, you **MUST** configure SVM correctly or you will experience issues like:
- Gear not restoring on death
- Duplicate items in inventory
- Secure container disappearing

### Required SVM Settings

| Setting | Required Value | Reason |
|---------|---------------|--------|
| **Safe Exit** | **OFF** | Interferes with death detection |
| **Softcore Mode** | **OFF** | Conflicts with gear restoration |

Settings in SVM's **"Inventory and Items"** category may also interfere. If you experience issues, disable these settings and re-enable one at a time to identify conflicts.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Snapshot not saving | Ensure mod is enabled in F12 settings. Check BepInEx console for errors. |
| Gear not restoring | Verify both client AND server mods are installed. Check server console. |
| Items missing | Check that the slot is enabled. FIR/insured items may be excluded by settings. |
| SVM conflicts | Disable Safe Exit and Softcore Mode in SVM. |

---

## Building from Source

### Prerequisites

- Visual Studio 2022 or later
- .NET Framework 4.7.1 SDK (for client)
- .NET 9.0 SDK (for server)

### Build Steps

1. Clone the repository
2. Set `SPT_PATH` environment variable to your SPT installation, or use `-p:SptPath="your/path"`
3. Build the solution:

```bash
dotnet build src/server/Blackhorse311.KeepStartingGear.csproj -p:SptPath="C:/SPT"
dotnet build src/servermod/Blackhorse311.KeepStartingGear.Server.csproj
```

---

## Security & Compliance

Each release includes a VirusTotal scan:
- **v1.4.9**: [VirusTotal Scan](https://www.virustotal.com/gui/file/ff232a9db482b915d31ac82ac262af5379372e27698f35c82bf03deb668d3924) - 0/70 clean

Source code is available on GitHub for independent verification and building.

### Forge Compliance

This mod meets all [SPT Forge Content Guidelines](https://forge.sp-tarkov.com/content-guidelines) and [Community Standards](https://forge.sp-tarkov.com/community-standards). See our detailed **[Forge Compliance Statement](docs/FORGE_COMPLIANCE.md)** for a line-by-line breakdown of how we meet each requirement.

**Highlights:**
- ✅ MIT licensed, fully open source
- ✅ Zero network activity (fully offline)
- ✅ No obfuscation, no data collection
- ✅ 26 security fixes in v2.0.0
- ✅ Comprehensive documentation

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Credits

**Author:** Blackhorse311

### Version 2.0.0 - Human + AI Collaboration

Version 2.0.0 was developed through a collaboration between Blackhorse311 and [Claude](https://claude.ai), an AI assistant created by Anthropic. This partnership brought together human creativity and testing expertise with AI-assisted code implementation, architecture design, and quality assurance.

Together, we delivered:
- 7 new major features (UI overlays, themes, loadout profiles, statistics)
- 26 bug fixes from comprehensive code review
- Security hardening and thread safety improvements
- Complete documentation overhaul

**Thanks to:**
- SPT Team - For the amazing SPT project
- BepInEx Team - For the modding framework
- Anthropic - For creating Claude

### Community Contributors

A huge thank you to everyone who reported bugs, provided logs, and helped make this mod better. **40+ bug fixes** wouldn't have been possible without you.

| Contributor | Contributions |
|-------------|---------------|
| **@Troyoza** | Hardcoded paths, character screen bounce, grid contents not captured |
| **@Wolthon** | Detailed restoration flow logs, secure container loss report |
| **@rSlade** | Early testing and feedback, boss kill restoration issue |
| **@immortal_wombat** | Medkit durability, armor durability, armor plates in backpack |
| **@Recker** | Empty slots keeping loot, secure container deletion, ammo stacks |
| **@andryi2509** | Extensive testing, SVM detection, surgical kit errors, duplicates |
| **@calafex** | Dogtag metadata wipe, Surv12 restoration issues |
| **@rimmyjob** | High capacity magazine ammo, secure container when disabled |
| **@Matheus** | Gamma container disappearing, folder structure, secure container |
| **@thechieff21** | All items kept after death with detailed logs (GitHub #18) |
| **@Kralicek94** | Unprotected weapon slots misbehaving (GitHub #17) |
| **@20fpsguy** | Scav raid snapshots, false extraction, secure container disappearing, gear loss on death |
| **@dilirity** | Folder structure fix (GitHub #12) |
| **@Vafelz** | Notification disable option request (GitHub #14) |
| **@Alcorsa** | SPT 4.0.8 compatibility issue |
| **@VeiledFury** | Release package structure issue |
| **@Toireht** | Version mismatch issue |
| **@L4Z3RB1** | Version mismatch (GitHub #11) |
| **@kurdamir2** | Snapshot not clearing, stale snapshots after transit |
| **@trollcze** | Duplicate item crashes |
| **@cykablyat** | Duplicate item crash, profile corruption |
| **@benadryldealer** | Duplicate item crashes |
| **@zezika** | Secure container issues |
| **@Bert** | Secure container issues, FiR status clarification |
| **@zezaovlr** | Ammo/grenade restoration issue |
| **@Buszman** | Magazine ammo disappearing, stale snapshots after transit |
| **@NomenIgnatum** | Medkit HP restoration issue |
| **@Television Hater** | Duplicate item crash |
| **@SPDragon** | PMC bot extract bug |
| **@najix** | Folder structure issues on Forge |
| **@katzenmadchenaufbier** | Folder structure issues on Forge |
| **@Alake** | Folder structure issues on Forge |
| **@stiffe0114** | Folder structure issues on Forge |
| **@GrandParzival** | Total gear loss on Labs, pockets corruption |
| **@Vicarious** | Gear loss, secure container disappearing, pockets permanently broken |
| **@BGSenTineL** | Lost pockets and special slots, gear on mannequin |

*35 community contributors. 40+ bug fixes since v1.0.*

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for full version history.

---

## Support

### Bug Reports

Please use our [GitHub Issue Tracker](https://github.com/Blackhorse311/KeepStartingGear/issues) with the bug report template. Include:
- SPT and mod versions
- Steps to reproduce
- Client log: `BepInEx/LogOutput.log`
- Server log: `SPT/user/logs/`

### Community

- [The Forge](https://forge.sp-tarkov.com/mod/2470/keep-starting-gear) - Mod page, comments, feedback
- [SPT Discord](https://discord.gg/spt) - Community help and discussion
