# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2025-12-06

### Added

- **SVM Compatibility**: Full compatibility with SVM (Server Value Modifier). Both mods can now run together without conflicts. KSG now hooks into `InRaidHelper.DeleteInventory` instead of `MatchCallbacks.EndLocalRaid`, allowing SVM to handle its features while KSG handles gear restoration.

### Technical

- **Architecture Change**: Moved restoration logic from `RaidEndInterceptor` (DI override) to `CustomInRaidHelper.DeleteInventory`. This avoids the DI conflict with SVM which also overrides `MatchCallbacks`.

- **JSON Deserialization Fix**: Fixed case sensitivity issue when reading snapshot files. Snapshots use lowercase property names (`_id`, `_tpl`) while C# classes use PascalCase. Now uses case-insensitive deserialization.

### Compatibility

- SPT 4.0.x (tested on 4.0.7)
- **SVM**: Partially compatible - disable Softcore Mode and Safe Exit when using KSG. Other SVM settings work at user's own risk.

---

## [1.2.0] - 2025-12-06

### Fixed

- **Empty Slots Keep Looted Items**: Fixed critical issue where items looted into empty equipment slots during raid were kept after death instead of being removed. Now properly tracks which slots were empty at snapshot time and clears them on restoration. (Reported by @Recker)

- **Grid Position Preservation**: Fixed items moving to top-left corner of containers on restoration. Now correctly extracts position data from EFT's `ItemCollection` (KeyValuePair<Item, LocationInGrid>) instead of the item's address. Items in backpacks, vests, and pockets maintain their original X/Y coordinates and rotation.

- **Ammo Box Contents Lost**: Fixed ammo boxes (e.g., 120-round boxes) being restored empty. The capture logic now checks for `Cartridges` property on ALL items, not just magazines. Ammo boxes use the same storage mechanism as magazines.

### Added

- **SVM Conflict Detection**: Server component now detects if SVM (Server Value Modifier) is installed and displays a prominent warning at startup explaining the conflict and how to resolve it.

### Changed

- **Improved Null Safety**: Fixed all nullable reference warnings in server mod for better stability and fewer potential crashes.

- **Better Snapshot Format**: Snapshots now include an `emptySlots` field that tracks which equipment slots were empty at capture time, enabling proper restoration.

- **Enhanced Logging**: Added debug logging for grid position capture/restoration to help diagnose any remaining position issues.

### Compatibility

- SPT 4.0.x (tested on 4.0.7)

---

## [1.1.1] - 2025-12-01

### Fixed

- **Secure Container Deletion**: Fixed issue where disabling the Secure Container slot in config would delete the entire secure container on death. The server now preserves items in slots that were not captured in the snapshot. (Reported by @Recker)

- **Ammo Stacks in Chest Rig**: Fixed issue where loose ammo stacks in tactical vests were not being restored. The ammo slot remapping now correctly identifies magazine containers vs grid containers. (Reported by @Recker)

- **PMC Bot Extract Bug**: Fixed issue where PMC bots using code-locked extracts (like Smuggler's Boat) could trigger false extraction detection and wipe the player's snapshot. Snapshot cleanup is now handled exclusively by the server. (Reported by @SPDragon)

### Changed

- **Secure Container Option Renamed**: "Secured Container" renamed to "Restore Secure Container to Snapshot" with detailed description explaining the actual behavior:
  - When ENABLED: Secure container restored to snapshot state (items added during raid are lost)
  - When DISABLED: Normal Tarkov behavior (all secure container contents kept)

### Added

- **SVM Conflict Diagnostics**: Added logging to help diagnose conflicts with Server Value Mod (SVM) softcore mode. Server logs now indicate when restoration completes and suggests checking for mod conflicts.

### Compatibility

- SPT 4.0.x (tested on 4.0.7)

---

## [1.1.0] - 2025-11-30

### Added

- **Automatic Snapshots**: Gear is now automatically saved when entering a raid (new default behavior)
- **Snapshot Modes**: Choose between Auto Only, Auto+Manual, or Manual Only modes
- **Configuration Presets**: Quick setup with Casual or Hardcore presets
  - Casual: Auto-snapshot, all items protected, sound enabled
  - Hardcore: Manual-only, FIR & insured items excluded
- **FIR Protection**: Option to exclude Found-in-Raid items from snapshots (prevents duplication)
- **Insurance Integration**: Option to exclude insured items (let insurance handle them)
- **Map Transfer Protection**: Option to keep original snapshot or re-snapshot on map transfer
- **Snapshot Sound Effect**: Plays the skill/XP gain sound when snapshots are taken
- **Mid-Raid Settings Lock**: Snapshot mode is locked at raid start to prevent exploits
- **Improved Keybind Display**: Shows "Ctrl + Alt + F8" instead of "F8+LeftControl+LeftAlt"

### Fixed

- **Critical: Grid Contents Not Captured**: Fixed issue where items inside containers (backpack, vest, pockets) were not being captured. The `Grids` property was accessed incorrectly - it's a public field, not a property. (Reported by @Troyoza, @Wolthon)
- **Magazine Ammo**: Properly captures ammunition in magazines with correct stack counts

### Changed

- Default behavior changed from manual-only to automatic snapshots at raid start
- "Enabled" setting renamed to "Enable KSG Mod" for clarity
- Configuration categories reorganized with "0. Quick Setup" at the top

### Compatibility

- SPT 4.0.x (tested on 4.0.7)

---

## [1.0.1] - 2025-11-29

### Fixed

- **Critical: Hardcoded Paths** - Server mod now dynamically resolves the snapshot path based on its installation location. Previously, the server looked for snapshots at a hardcoded path which caused restoration to fail for users with different installation paths. (Reported by @Troyoza on Forge)

- **Character Screen Bounce** - Disabled the client-side `PostRaidInventoryPatch` which was causing a "double bounce" issue where the character screen would flash and reload after restoration. The server-side `RaidEndInterceptor` now handles restoration exclusively. (Reported by @Troyoza on Forge)

### Changed

- Server mod now logs the resolved snapshots path on startup for easier debugging
- Added debug logging for exit status values to help diagnose restoration issues
- Updated documentation in `PostRaidInventoryPatch` to explain why it's disabled

### Contributors

Special thanks to bug hunters who helped identify these issues:
- **@Troyoza** - Identified the hardcoded path issue and character screen bounce
- **@Wolthon** - Provided detailed logs that helped trace the restoration flow
- **@rSlade** - Reported Boss kill restoration issue (likely caused by the path bug)

### Compatibility

- SPT 4.0.x (tested on 4.0.6, compatible with 4.0.7)

---

## [1.0.0] - 2025-11-25

### Added

- Initial release for SPT 4.0.x
- **Snapshot System**: Press Ctrl+Alt+F8 (configurable) in-raid to capture current equipment
- **Server-Side Restoration**: Automatic inventory restoration on death without Run-Through penalty
- **Per-Map Snapshots**: One snapshot per map, new snapshots replace previous ones
- **Full Inventory Capture**:
  - All equipment slots (head, armor, vest, backpack, weapons, etc.)
  - Secure container and contents
  - Pockets and contents
  - Nested items (items inside containers, magazines in weapons)
  - Item properties (durability, ammo counts, foldable states)
  - Magazine ammunition with correct slot mapping
- **Modded Items Support**: Works with any modded items using template IDs
- **Visual Notifications**: Large colored on-screen notifications:
  - Green: Snapshot saved successfully
  - Red: Errors or failures
  - Blue: Restoration confirmation
  - Yellow: Informational messages
- **BepInEx Configuration**: Configurable via F12 Configuration Manager:
  - Snapshot keybind
  - Include/exclude individual equipment slots
  - Enable/disable notifications
  - Debug logging options

### Technical Details

- Client component: BepInEx plugin (.NET Framework 4.7.1)
- Server component: SPT mod (.NET 9.0)
- Uses SPT's dependency injection system
- Thread-safe state management for concurrent request handling
- Harmony patching via SPT.Reflection.Patching

### Compatibility

- SPT 4.0.x (tested on 4.0.6)
- BepInEx 5.x (included with SPT)
