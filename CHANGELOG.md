# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.8] - 2025-12-23

### Fixed

- **Version Mismatch**: Fixed server log showing "v1.3.0" instead of actual version. Server, client, and metadata now all report consistent version numbers. (Reported by @Toireht, @L4Z3RB1)

- **SVM Folder Detection**: Fixed SVM (Server Value Modifier) not being detected when using default folder name `[SVM] Server Value Modifier`. Brackets in folder names are now stripped before keyword matching. (Reported by @andryi2509)

- **Duplicate Item Crash Prevention**: Added deduplication at snapshot save time to prevent "An item with the same key has already been added" crashes. If duplicate item IDs are detected in a snapshot, they are removed with a warning logged. (Reported by @trollcze, @cykablyat, @andryi2509, @benadryldealer)

- **Snapshot Not Cleared on Extract**: Fixed old snapshots persisting after successful extractions when SVM is installed. The client now clears snapshots on extraction as a fallback when the server-side cleanup doesn't run. (Reported by @kurdamir2)

- **Secure Container Items From Non-Managed Slots**: Fixed items from disabled slots (like SecuredContainer when disabled) being incorrectly added from snapshots. The restoration logic now traces each item's root slot and skips items from non-managed slots, preventing duplicates and preserving disabled slot contents. (Reported by @zezika, @Matheus, @Bert)

### Technical

- **Root Slot Tracing**: Both `RaidEndInterceptor.cs` and `CustomInRaidHelper.cs` now build a map of snapshot items to their root equipment slot. Items are only restored if their root slot is in the `IncludedSlots` list.

- **Client-Side Snapshot Cleanup**: `RaidEndPatch.cs` now clears snapshots on successful extraction, providing redundancy when server-side cleanup fails due to mod conflicts.

- **Snapshot Deduplication**: `SnapshotManager.SaveSnapshot()` now validates and deduplicates items before writing to disk, preventing corrupted snapshots.

### Contributors

- **@Toireht** - Reported version mismatch issue
- **@L4Z3RB1** - Reported version mismatch via GitHub #11
- **@kurdamir2** - Reported snapshot not clearing after extraction
- **@andryi2509** - Reported SVM detection and settings issues
- **@trollcze, @cykablyat, @benadryldealer** - Reported duplicate item crashes
- **@zezika, @Matheus, @Bert** - Reported secure container issues

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.7] - 2025-12-17

### Fixed

- **Ammo and Grenades Not Restoring**: Fixed critical bug where ammunition in pockets/vests and grenades were not being captured in snapshots. The LocationInGrid data (x, y, rotation) was not being read correctly because SPT 4.0.8 uses public **fields** instead of properties. Changed reflection access from `GetProperty()` to `GetField()` for LocationInGrid and its x/y/r members.

### Technical

- **Reflection Field Access**: EFT's `GClass3393` (grid address base class) stores `LocationInGrid` as a public field, not a property. The `LocationInGrid` struct also uses public fields for `x`, `y`, and `r` (rotation). Updated `InventoryService.cs` to try `GetField()` first, then fall back to `GetProperty()` for compatibility.

- **Build System Improvements**: Added `Directory.Build.props` for flexible SPT path configuration. Developers can now set the `SPT_PATH` environment variable instead of editing csproj files.

### Contributors

- **@zezaovlr** - Reported ammo/grenade restoration issue via GitHub

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.6] - 2025-12-17

### Fixed

- **Secure Container Disappearing (SVM Compatibility)**: Fixed critical bug where secure containers (and other non-managed slots) were deleted when using mods that route through `CustomInRaidHelper` instead of `RaidEndInterceptor`. The slot preservation logic was missing from `CustomInRaidHelper.TryRestoreFromSnapshot()`. Now both code paths correctly preserve items in slots that are disabled in config (e.g., when "Restore Secure Container to Snapshot" is set to false). (Reported by @Matheus and @wolthon on Forge)

### Technical

- **Code Path Synchronization**: Both `RaidEndInterceptor.cs` and `CustomInRaidHelper.cs` now use identical slot management logic:
  - Slots in `IncludedSlots` (enabled in config) → Items removed and restored from snapshot
  - Slots NOT in `IncludedSlots` (disabled in config) → Items preserved (normal Tarkov behavior)
  - Empty slots at snapshot time → Items removed on death (prevents keeping looted items)

### Contributors

- **@Matheus** - Reported gamma container disappearing during raid loading
- **@wolthon** - Reported secure container loss with "Restore Secure Container to Snapshot = false"

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.5] - 2025-12-16

### Added

- **Configurable Manual Snapshots**: Added "Max Manual Snapshots" setting (1-10, default: 1) that allows players to configure how many manual snapshots they can take per raid in Auto+Manual mode. This enables more flexibility for players who want to update their snapshot multiple times during a raid.

- **Snapshot Progress Notifications**: Notifications now show progress when using multiple manual snapshots (e.g., "2/3 updates used") so players know how many updates remain.

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---


## [1.4.4] - 2025-12-15

### Fixed

- **Dogtag Metadata Wiped on Restore**: Fixed critical issue where dogtags lost all their metadata (kill date, player level, killer info) when restored from snapshot. Dogtags now correctly preserve all metadata including AccountId, ProfileId, Nickname, Side, Level, Time, Status, KillerAccountId, KillerProfileId, KillerName, and WeaponName. (Reported by @calafex via GitHub #6)

- **Surv12/CMS Uses Not Preserved**: Fixed issue where surgical kits (Surv12, CMS) were restored to full uses (9/9) instead of their captured state. The MedKitComponent capture now checks ALL items, not just those with "MedKit" in the type name. Surgical kits also use MedKitComponent for tracking remaining uses. (Reported by @calafex via GitHub #5)

### Added

- **Key Uses Tracking**: Added support for capturing and restoring key usage counts. Keys with limited uses now preserve their remaining uses in snapshots.

### Contributors

- **@calafex** - Reported dogtag metadata and Surv12 restoration issues

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.3] - 2025-12-14

### Fixed

- **Secure Container Deleted When Disabled**: Fixed critical bug where disabling "Restore Secure Container to Snapshot" in config caused the secure container to be deleted entirely on death, instead of being preserved with normal Tarkov behavior. Non-managed slots are now properly preserved. (Reported by @rimmyjob via GitHub #4)

- **Release Package Structure**: Fixed folder structure in release packages - server mod now correctly placed in `SPT/user/mods/` instead of `user/mods/`. (Reported by @VeiledFury)

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.2] - 2025-12-13

### Fixed

- **GUID Mismatch**: Fixed server mod GUID (`blackhorse311.keepstartinggear`) to match client plugin GUID (`com.blackhorse311.keepstartinggear`). This resolves warnings in CheckMods and other mod compatibility tools.

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.1] - 2025-12-13

### Fixed

- **SPT 4.0.8 Compatibility**: Updated server NuGet packages from 4.0.2 to 4.0.8 to fix inventory restoration failures. (Reported by @Alcorsa)

### Changed

- **Forge Logging Compliance**: Reduced verbose logging to comply with Forge Content Guidelines. Most operational messages moved to Debug level, keeping only essential status messages at Info level.

### Compatibility

- SPT 4.0.x (tested on 4.0.8)

---

## [1.4.0] - 2025-12-11

### Fixed

- **MedKit Durability Not Preserved**: Fixed critical issue where medical items (IFAK, AFAK, Grizzly, etc.) were restored with full HP instead of their snapshot HP. The mod now correctly accesses the `HpResource` field on EFT's `MedKitComponent`. (Reported by @immortal_wombat)

- **Armor Durability Not Preserved**: Fixed issue where armor and weapons were restored with full durability instead of their snapshot durability. The mod now correctly accesses `Durability` and `MaxDurability` fields on EFT's `RepairableComponent`. (Reported by @immortal_wombat)

- **Armor Plates Lost in Backpack**: Fixed critical issue where armor stored in backpacks/containers lost their plates and soft armor inserts on restoration. The snapshot capture now properly handles nested slots within grid items (e.g., armor plates inside armor that's inside a backpack). (Reported by @immortal_wombat)

- **Slot Exclusion Not Working**: Fixed issue where items in non-protected slots were being preserved instead of lost on death. The mod now correctly applies the death penalty to unprotected slots while only restoring items from protected slots.

### Technical

- **Reflection Fix**: EFT stores durability values as fields, not properties. Changed from `GetProperty()` to `GetField()` for accessing `HpResource`, `Durability`, and `MaxDurability` on item components.

- **Grid Item Slot Capture**: Added recursive slot capture for items found in grid containers. When armor is captured from a backpack grid, its AllSlots are now enumerated to capture plates and soft armor inserts.

### Contributors

Special thanks to our community bug hunters who helped improve this release:
- **@immortal_wombat** - Reported durability issues for medkits and armor, and armor plates being lost in backpacks
- **@NomenIgnatum** - Reported medkit HP restoration issue
- **@trollcze** - Reported "An item with the same key" crash on death
- **@andryi2509** - Extensive testing, reported surgical kit errors, SVM detection issues, and helped debug multiple problems
- **@Television Hater** - Reported duplicate item crash
- **@benadryldealer** - Reported duplicate item crash
- **@20fpsguy** - Reported scav raid snapshot saving and false extraction detection
- **@cykablyat** - Reported duplicate item crash and profile corruption
- **@Buszman** - Reported magazine ammo disappearing issue

### Compatibility

- SPT 4.0.x (tested on 4.0.8)
- **SVM**: Partially compatible - disable Softcore Mode and Safe Exit when using KSG

---

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
