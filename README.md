# Keep Starting Gear

**Version:** 1.0.0
**Author:** Blackhorse311
**License:** MIT
**SPT Compatibility:** 4.0.x (tested on 4.0.6)

A mod for SPT that allows players to save their equipment before a raid and have it restored if they die.

---

## Features

- **Snapshot System**: Press a keybind in-raid to capture your current equipment
- **Automatic Restoration**: If you die, your saved equipment is automatically restored
- **No Run-Through Penalty**: Restoration happens server-side, avoiding the "Run-Through" status
- **Per-Map Snapshots**: One snapshot per map - taking a new snapshot replaces the previous one
- **Modded Items Support**: Works with modded weapons, armor, and equipment
- **Visual Feedback**: Large colored notifications confirm snapshot creation and restoration

---

## Requirements

- SPT 4.0.x (tested on 4.0.6)
- BepInEx 5.x (included with SPT)

---

## Installation

### Automatic (Recommended)

Install via The Forge mod manager.

### Manual Installation

1. Download the latest release
2. Extract the contents to your SPT installation folder
3. The folder structure should look like:

```
SPT/
├── BepInEx/
│   └── plugins/
│       └── Blackhorse311-KeepStartingGear/
│           ├── Blackhorse311.KeepStartingGear.dll
│           └── snapshots/
└── SPT/
    └── user/
        └── mods/
            └── Blackhorse311-KeepStartingGear/
                └── Blackhorse311.KeepStartingGear.Server.dll
```

---

## Usage

1. **Enter a raid** with the gear you want to protect
2. **Press Ctrl+Alt+F8** (default keybind) to create a snapshot of your current equipment
3. A green notification will confirm: "Snapshot saved for [map name]"
4. If you **die in raid**, your equipment will be automatically restored
5. A blue notification will confirm restoration when you return to the main menu

### Configuration

Access mod settings via **F12** (BepInEx Configuration Manager) or edit the config file directly.

Config file location: `BepInEx/config/com.blackhorse311.keepstartinggear.cfg`

#### Keybind Settings

| Setting | Default |
|---------|---------|
| Snapshot Keybind | Ctrl+Alt+F8 |

#### Inventory Slot Settings

Every equipment slot can be individually enabled or disabled. All default to **true** (included in snapshot).

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
| Secured Container | Keeps items on death |
| Compass | Navigation |
| Special Slot 1-3 | Injectors, stims |

#### Logging Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Debug Mode | false | Enable verbose logging |
| Log Snapshot Creation | true | Log when snapshots are created |
| Log Snapshot Restoration | true | Log when snapshots are restored |

---

## How It Works

### Client Component (BepInEx Plugin)

1. When you press the snapshot keybind, the client captures your entire inventory
2. The snapshot is saved as a JSON file in the `snapshots` folder
3. When a raid ends, the client notifies you of the outcome

### Server Component (SPT Mod)

1. When a raid ends, the server checks if you died
2. If a snapshot exists for that map, it restores your inventory
3. The restoration happens before death penalties are applied
4. This avoids the "Run-Through" status that would occur with client-side restoration

---

## Snapshot Details

Snapshots include:
- All equipment slots (head, armor, vest, backpack, weapons, etc.)
- Secure container and contents
- Pockets and contents
- All nested items (items inside containers, magazines in weapons, etc.)
- Item durability, ammo counts, and other properties
- Modded items and their configurations

Snapshots are stored per-map. Taking a new snapshot on the same map replaces the previous one.

---

## Troubleshooting

### Snapshot not saving
- Ensure you're in an active raid (not in menus)
- Check that the keybind isn't conflicting with other mods
- Look for errors in the BepInEx console

### Inventory not restoring
- Verify both client and server components are installed
- Check the SPT server console for error messages
- Ensure a snapshot exists for the map you died on

### Modded items not restoring correctly
- The mod uses template IDs, so modded items should work
- If issues occur, check that the modded item's mod is also installed

---

## Compatibility

- **SPT Version**: 4.0.x
- **Fika**: Not tested - may not work in multiplayer scenarios
- **Other Mods**: Should be compatible with most mods

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

## Project Structure

```
Blackhorse311.KeepStartingGear/
├── src/
│   ├── server/                          # Client-side BepInEx plugin
│   │   ├── Plugin.cs                    # Main entry point
│   │   ├── Configuration/
│   │   │   └── Settings.cs              # BepInEx configuration
│   │   ├── Components/
│   │   │   ├── KeybindMonitor.cs        # In-raid keybind handling
│   │   │   └── NotificationOverlay.cs   # Visual notifications
│   │   ├── Services/
│   │   │   ├── InventoryService.cs      # Inventory capture logic
│   │   │   ├── SnapshotManager.cs       # Snapshot persistence
│   │   │   └── ProfileService.cs        # Profile manipulation
│   │   ├── Models/
│   │   │   ├── InventorySnapshot.cs     # Snapshot data structure
│   │   │   └── SerializedInventory.cs   # Serialization models
│   │   └── Patches/
│   │       ├── PatchManager.cs          # Patch registration
│   │       ├── GameStartPatch.cs        # Raid start hook
│   │       ├── PostRaidInventoryPatch.cs# Post-raid hook
│   │       └── RaidEndPatch.cs          # Raid end processing
│   │
│   └── servermod/                       # Server-side SPT mod
│       ├── KeepStartingGearMod.cs       # Server entry point
│       ├── ModMetadata.cs               # SPT mod registration
│       ├── RaidEndInterceptor.cs        # Raid end interception
│       ├── CustomInRaidHelper.cs        # Inventory deletion override
│       └── SnapshotRestorationState.cs  # Thread-safe state
│
├── bin/                                 # Build output
├── README.md                            # This file
├── CHANGELOG.md                         # Version history
└── LICENSE                              # MIT License
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Credits

- **Author**: Blackhorse311
- **SPT Team**: For the amazing SPT project
- **BepInEx Team**: For the modding framework

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.

---

## Support

If you encounter bugs or have suggestions:
- Report issues on GitHub
- Check the SPT Discord for community support
