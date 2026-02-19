# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.7] - 2026-02-19

### Fixed - SVM Compatibility and Scabbard Preservation

This release fixes two bugs: gear not restoring on death when SVM is installed (GitHub #20), and melee weapons being deleted from non-managed Scabbard slots.

#### SVM Restoration Failure (CRITICAL)

**Problem:** When SVM (Server Value Modifier) is installed, it replaces `MatchCallbacks` via dependency injection, preventing `RaidEndInterceptor.EndLocalRaid()` from running. The fallback restoration in `DeleteInventory` was too late in SPT's pipeline. By the time `DeleteInventory` ran, `SetInventory` had already copied the player's death-state inventory to the server profile, making restoration impossible.

**Symptoms:**
- Only Pockets restored on death (because Pockets container is always preserved)
- All other gear lost despite snapshot being taken
- Only occurs when SVM is installed

**Fix:** Override `SetInventory` in `CustomInRaidHelper` to restore snapshot items into `postRaidProfile` BEFORE `base.SetInventory` copies them to the server profile. The snapshot file acts as a coordination mechanism: if `RaidEndInterceptor` already ran, it deleted the file so `SetInventory` finds nothing and skips. If SVM prevented `RaidEndInterceptor`, the file still exists and `SetInventory` restores from it.

**File:** `src/servermod/CustomInRaidHelper.cs`

#### Scabbard Deletion on Death (MEDIUM)

**Problem:** In `DeleteNonManagedSlotItems`, the Scabbard slot was treated as a normal slot. When Scabbard was not in the managed set, the melee weapon was deleted. In normal Tarkov, melee weapons are never lost on death.

**Fix:** Added Scabbard to the always-preserved slots in `DeleteNonManagedSlotItems`, matching SecuredContainer behavior (container and contents always preserved).

**Files:** `src/servermod/CustomInRaidHelper.cs`, `src/servermod/Constants.cs`

### Technical

- New two-phase restoration architecture: Phase 1 (SetInventory override) restores snapshot, Phase 2 (DeleteInventory override) handles selective deletion
- Removed fallback `TryRestore` from `DeleteInventory` (was operating too late in the pipeline)
- Added `Constants.ScabbardSlot` shared constant
- Added 2 new Scabbard tests (48 total: 10 serialization + 38 restoration algorithm)
- All fixes are server-side only; client DLL version bumped but code unchanged

### Contributors

- **@theguy101** - Reported gear not restoring with SVM installed via GitHub #20

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [2.0.6] - 2026-02-16

### Fixed - Pockets Contents Not Restored From Snapshot

This release fixes a regression introduced in v2.0.5 where Pockets contents kept their raid-end state instead of being restored from the snapshot on death.

#### Pockets Contents Not Managed (HIGH)

**Problem:** The v2.0.5 Pockets protection was too aggressive. When we added code to prevent the Pockets container from being deleted (which corrupts profiles), we skipped the container AND all its contents from the removal phase. This meant items inside Pockets were never removed during restoration, so snapshot items (with original state) were skipped as duplicates.

**Symptoms:**
- Medical items in pockets not restored to snapshot state (e.g., used Calok stays at 2 uses instead of full)
- Items looted during raid and placed in pockets stay after death
- Issue localized to Pockets slot only

**Fix:** Separated container preservation from content management. Permanent containers (SecuredContainer, Pockets) are still never deleted, but their CONTENTS are now properly removed and restored from snapshot when the slot is managed. Added `preservedManagedContainerIds` set to track containers whose children should be seeded into the BFS removal queue.

**Files:** `src/servermod/SnapshotRestorer.cs`, `src/servermod/CustomInRaidHelper.cs`

#### Secondary Fix: Pockets Death Mechanics in Partial Deletion

**Problem:** In `DeleteNonManagedSlotItems` (partial death processing), Pockets was treated identically to SecuredContainer - both container and contents were always preserved. But in normal Tarkov, SecuredContainer contents survive death while Pockets contents are lost. When Pockets was not a managed slot, its contents should have been deleted.

**Fix:** SecuredContainer preserves container + contents (normal Tarkov). Pockets preserves container but deletes contents when not managed (normal death behavior).

**File:** `src/servermod/CustomInRaidHelper.cs`

### Technical

- New pattern: `preservedManagedContainerIds` tracks permanent containers whose children enter the BFS removal queue
- `IsSlotManaged` in test algorithm no longer special-cases SC/Pockets (content management is separate from container preservation)
- Updated `CollectItemsToRemove` in test algorithm with same pattern as production code
- Updated 4 existing tests, added 2 new tests (46 total: 10 serialization + 36 restoration algorithm)
- All fixes are server-side only; client DLL unchanged

### Contributors

- **@UVCatastrophe** - Reported pockets contents not restoring from snapshot on Forge

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [2.0.5] - 2026-02-13

### Fixed - Critical Gear Loss, Secure Container & Pockets Corruption

This release fixes two critical bugs that caused total gear loss on death, secure container disappearing, and permanent pocket corruption. Multiple users reported these issues since v2.0.1.

#### ThreadLocal State Communication Failure (CRITICAL)

**Problem:** `SnapshotRestorationState` used `ThreadLocal<bool>` for communication between `RaidEndInterceptor` (which restores inventory) and `CustomInRaidHelper.DeleteInventory` (which must skip deletion). If SPT dispatched these on different threads, the ThreadLocal flag was invisible on the second thread. The fallback `TryRestore` also failed because the snapshot file was already deleted by the interceptor. Result: `base.DeleteInventory()` ran and wiped ALL equipment.

**Symptoms:**
- Total gear loss on death (intermittent)
- Secure container disappearing after 2-3 deaths
- Gear appearing on hideout mannequin instead of character

**Fix:** Replaced `ThreadLocal` with `ConcurrentDictionary<string, RestorationData>` keyed by session ID. Both callers already had access to the session ID. `TryRemove` provides atomic check-and-consume semantics. Added stale entry cleanup (5-minute timeout) to prevent memory leaks.

**File:** `src/servermod/SnapshotRestorationState.cs` (complete rewrite)

#### Pockets Permanently Corrupted on Death (HIGH)

**Problem:** Only `SecuredContainer` was in the `alwaysPreservedSlots` set. Pockets is a permanent fixture (like SecuredContainer) that should NEVER be deleted. When Pockets was a non-managed slot, `DeleteNonManagedSlotItems` deleted the entire Pockets ITEM (not just contents), permanently corrupting the profile. Same issue existed in `RemoveManagedSlotItems`.

**Symptoms:**
- Pockets appearing as "empty rig slot" with no slots
- Cannot be fixed by removing the mod or dying without it
- Permanent profile corruption

**Fix:** Added Pockets to `alwaysPreservedSlots` in `DeleteNonManagedSlotItems` and added explicit Pockets protection in `RemoveManagedSlotItems` (identical pattern to SecuredContainer).

**Files:** `src/servermod/CustomInRaidHelper.cs`, `src/servermod/SnapshotRestorer.cs`, `src/servermod/Constants.cs`

### Technical

- Replaced `ThreadLocal<bool>`, `ThreadLocal<HashSet<string>?>`, `ThreadLocal<int>` with single `ConcurrentDictionary<string, RestorationData>`
- New API: `MarkRestored(sessionId, managedSlotIds)`, `TryConsume(sessionId, out managedSlotIds)`, `Clear(sessionId)`
- Added `Constants.PocketsSlot` shared constant
- Updated pure test algorithm with Pockets invariant
- Added 3 new unit tests for Pockets protection (44 total)
- All fixes are server-side only; client DLL unchanged

### Contributors

- **@20fpsguy** - Reported secure container disappearing after multiple deaths
- **@GrandParzival** - Reported total gear loss including pockets on Labs
- **@Vicarious** - Reported gear on mannequin and permanent pocket corruption
- **@BGSenTineL** - Reported pockets and special slots lost, gear on mannequin

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [2.0.4] - 2026-02-02

### Added - Healthcare-Grade Testing Infrastructure

This release implements formal invariant documentation and comprehensive unit testing for the restoration algorithm, enabling provable correctness of the core business logic.

#### Pure Algorithm Extraction

**Problem:** The restoration algorithm was tightly coupled to SPT/BepInEx dependencies, making it impossible to unit test without a full game environment.

**Solution:** Extracted a pure implementation of the restoration algorithm to `src/tests/RestorationAlgorithm.cs`:
- `AlgorithmItem`: Lightweight item representation (no SPT dependencies)
- `RestorationAlgorithm`: Static class with pure functions for all algorithm steps
- Identical logic to production code but with test-friendly types

**Files:**
- `src/tests/RestorationAlgorithm.cs` (509 lines, new)
- `src/tests/RestorationAlgorithmTests.cs` (416 lines, new)

#### Comprehensive Unit Tests (31 tests)

Test coverage for all algorithm invariants:

| Test Class | Count | Coverage |
|------------|-------|----------|
| `FindEquipmentContainerIdTests` | 3 | Equipment container detection |
| `BuildSlotSetsTests` | 6 | Slot set construction, case insensitivity |
| `TraceRootSlotTests` | 6 | Parent chain traversal, cycle detection |
| `IsSlotManagedTests` | 7 | Slot management rules, SecuredContainer invariant |
| `SimulateRestorationTests` | 9 | Full algorithm integration |

All 41 tests pass (31 new + 10 existing serialization tests).

#### Formal Invariant Documentation (ADR-003)

Created `docs/adr/ADR-003-restoration-algorithm-invariants.md` documenting five inviolable invariants:

1. **INVARIANT 1**: Equipment container is NEVER removed
2. **INVARIANT 2**: SecuredContainer slot is ALWAYS preserved
3. **INVARIANT 3**: Parent chain traversal terminates within 50 iterations
4. **INVARIANT 4**: null vs empty IncludedSlotIds distinction is preserved
5. **INVARIANT 5**: All Equipment container IDs are checked

#### Defensive Assertions

Added `Debug.Assert` statements throughout `SnapshotRestorer.cs` at critical points:
- Equipment container ID validation
- Parent chain depth limit verification
- SecuredContainer slot protection
- Post-restoration Equipment container existence

These assertions fire in Debug builds if invariants are violated, catching bugs during development.

### Technical

- Total new test coverage: 31 tests for restoration algorithm
- Pure algorithm enables testing without game dependencies
- ADR-003 provides formal specification for future maintenance
- Assertions have zero runtime cost in Release builds

---

## [2.0.3] - 2026-02-02

### Fixed - Healthcare-Standard Code Review (Linus Torvalds Edition)

This release addresses architectural issues identified during an extremely critical "healthcare-standard" code review. These fixes improve the robustness and reliability of the mod under edge conditions.

#### LINUS-001: Lazy Initialization Race Condition (CRITICAL)

**Problem:** The `??=` null-coalescing assignment in `RaidEndInterceptor.Restorer` property is NOT atomic. Two threads could both see `_restorerLazy` as null and create separate `Lazy<T>` instances.

**Fix:** Implemented proper double-check locking pattern with explicit lock object and `LazyThreadSafetyMode.ExecutionAndPublication`. This ensures the restorer is initialized exactly once regardless of concurrent access.

**File:** `src/servermod/RaidEndInterceptor.cs`

#### LINUS-002: Atomic File Writes (CRITICAL)

**Problem:** `File.WriteAllText()` is NOT atomic. If the process crashes mid-write, the snapshot file will be corrupted, causing silent restoration failures.

**Fix:** Implemented write-to-temp-then-rename pattern:
1. Write JSON to temporary file with unique name (`.tmp.{guid}`)
2. Delete existing target file if present
3. `File.Move()` temp to final path (atomic on NTFS/same volume)
4. Clean up temp file in finally block if write failed

**File:** `src/server/Services/SnapshotManager.cs`

#### LINUS-003: ThreadLocal Assumption Validation (CRITICAL)

**Problem:** `ThreadLocal<T>` only works if the SAME thread calls `MarkRestored()` and `TryConsume()`. If SPT ever changes its threading model (e.g., task-based async), this design will silently fail.

**Fix:** Added runtime thread ID tracking:
1. `MarkRestored()` now records `Environment.CurrentManagedThreadId`
2. `TryConsume()` validates the thread ID matches
3. If threads differ, logs a CRITICAL warning and increments a counter
4. Added `ThreadMismatchCount` property for monitoring

**File:** `src/servermod/SnapshotRestorationState.cs`

#### LINUS-004: Exception Swallowing (MEDIUM)

**Problem:** Catching generic `Exception` and continuing hides serious errors like `OutOfMemoryException`, `StackOverflowException`, and `AccessViolationException`.

**Fix:** Added `IsSafeToSwallow()` filter method that allows exception handlers to only catch recoverable exceptions. Critical system exceptions now propagate properly.

**File:** `src/server/Components/AutoSnapshotMonitor.cs`

#### LINUS-005: Volatile Usage Documentation (MEDIUM)

**Problem:** Mix of `volatile` and `Interlocked` was inconsistent and confusing. `volatile float` is not atomic on 32-bit systems.

**Fix:** Added comprehensive threading policy documentation explaining:
- Why volatile is defensive but potentially unnecessary for Unity main-thread code
- When Interlocked is required (atomic increment)
- Why float timing values tolerate minor races

**File:** `src/server/Components/KeybindMonitor.cs`

### Technical

- All fixes maintain backwards compatibility
- No changes to snapshot file format
- Improved resilience under concurrent access and system failures

---

## [2.0.2] - 2026-02-02

### Fixed - Code Review Issues (15 total)

This release addresses issues identified during a comprehensive code review.

#### CRITICAL (2)

- **CRITICAL-001**: Fixed potentially confusing Lazy initialization pattern in `RaidEndInterceptor.cs` that captured constructor parameter in field initializer. Now uses explicit field storage and property-based lazy initialization.

- **CRITICAL-002**: Documented intentional non-disposal of `ThreadLocal<T>` fields in `SnapshotRestorationState.cs`. These are static fields with application lifetime scope in SPT's server model.

#### HIGH (4)

- **HIGH-001**: Added 10MB file size check to client's `SnapshotManager.LoadSnapshot()` before reading file contents. Server already had this check; client was missing it. Prevents potential OutOfMemoryException from corrupted/malicious files.

- **HIGH-002**: Added null-conditional operator (`?.`) to all `Plugin.Log.LogDebug()` calls in `ReflectionCache.cs`. Prevents NullReferenceException if reflection is used before Plugin initialization completes.

- **HIGH-003**: Fixed unsafe string truncation in session ID warning message in `SnapshotManager.cs`. Now properly handles null sessionId without risk of exception.

- **HIGH-004**: Changed `GetSnapshotFilePath()` return type to `string?` in `SnapshotManager.cs` to properly indicate nullable return for invalid session IDs.

#### MEDIUM (5)

- **MEDIUM-001**: Changed `_manualSnapshotCount` increment from non-atomic `++` to `Interlocked.Increment()` in `KeybindMonitor.cs`. Added `Volatile.Read()` and `Volatile.Write()` for thread-safe access.

- **MEDIUM-002**: Added shared constant `Constants.SecuredContainerSlot` in server mod and updated both `SnapshotRestorer.cs` and `CustomInRaidHelper.cs` to use it instead of duplicate local definitions.

- **MEDIUM-003**: (Documented) Server-side restoration logic lacks unit test coverage. Tests currently only cover client-side serialization. Future improvement recommended.

- **MEDIUM-004**: (Already addressed) Equipment container ID lookup logic was previously duplicated but has been consolidated into `SnapshotRestorer` with the `FindAllEquipmentContainerIds()` method.

- **MEDIUM-005**: Added validation to `SerializedItem.LocationIndex` property setter to silently clamp negative values to null during deserialization. The explicit `SetCartridgeIndex()` method still throws for validation.

#### LOW (4)

- **LOW-001**: Changed location access error log level from Debug to Warning in `AutoSnapshotMonitor.cs` for better visibility.

- **LOW-002**: Added `volatile` modifier to `_raidEndProcessed` field in `RaidEndPatch.cs` for consistency with other static state fields.

- **LOW-003**: Changed reflection error log level from Debug to Warning in `RaidEndPatch.cs` for better diagnostics visibility.

- **LOW-004**: (Documented) Test models in `tests/Models/TestModels.cs` duplicate production models. Consider referencing client project in future refactor.

### Technical

- All fixes maintain backwards compatibility with existing snapshots and configurations
- No changes to snapshot file format or mod behavior
- Improved thread safety and null handling throughout codebase

---

## [2.0.1] - 2026-02-01

### Fixed

- **All Looted Items Kept After Death (Issue #18)**: Fixed critical bug where players kept ALL items found in raid after death, plus snapshot items were restored on top. Root cause was threefold:
  1. Equipment container was captured with corrupt data (self-referential parentId, session ID as slotId)
  2. Item removal logic only checked ONE Equipment container ID, missing items parented to different Equipment containers
  3. Server removed 0 items from managed slots due to ID mismatch between raid-end and profile Equipment containers
  (Reported by @thechieff21 via GitHub #18)

- **Unprotected Weapon Slots Preserving Items (Issue #17)**: Fixed bug where weapons in unchecked/unprotected slots (FirstPrimaryWeapon, SecondPrimaryWeapon, Holster) were preserved after death instead of being lost. When a slot is NOT protected, items should follow normal death mechanics. The fix:
  1. Distinguish between `null` managedSlotIds (legacy: preserve all) and empty set (no protection: normal death)
  2. When no slots are protected, now correctly calls base DeleteInventory for normal death processing
  (Reported by @Kralicek94 via GitHub #17)

- **Equipment Container Capture**: The Equipment container (template `55d7217a4bdc2d86028b456d`) is now captured with only its ID, without setting parentId or slotId. This prevents corrupt snapshot data that caused restoration failures.

- **Multiple Equipment Container ID Handling**: Both `SnapshotRestorer` and `CustomInRaidHelper` now find ALL Equipment container IDs in inventory and check items against all of them. This handles edge cases where items may be parented to different Equipment containers (raid-end state vs snapshot).

- **Secure Container Disappearing (Forge Report)**: Fixed critical bug where the secure container could be deleted after multiple deaths. The SecuredContainer slot is now explicitly protected in both restoration paths - it's NEVER deleted regardless of configuration, matching normal Tarkov behavior where your secure container is always preserved on death.
  (Reported by @20fpsguy on Forge)

### Technical

- **Client Fix** (`InventoryService.cs`): Added early return for Equipment container in `ConvertToSerializedItem()` to skip parentId/slotId assignment
- **Server Fix** (`SnapshotRestorer.cs`): Added `FindAllEquipmentContainerIds()` method and updated `RemoveManagedSlotItems()` to check against all Equipment container IDs; SecuredContainer slot is now explicitly preserved
- **Server Fix** (`CustomInRaidHelper.cs`): Updated `DeleteInventory()` to distinguish null vs empty managedSlotIds, and updated `DeleteNonManagedSlotItems()` to check all Equipment containers; Added `alwaysPreservedSlots` set that includes SecuredContainer

### Contributors

- **@thechieff21** - Reported all items kept after death with detailed server logs via GitHub #18
- **@Kralicek94** - Reported unprotected weapon slots misbehaving via GitHub #17
- **@20fpsguy** - Reported secure container disappearing on Forge

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [2.0.0] - 2026-01-27

### Added - New UI Features

- **Post-Death Summary Screen**: After dying, a detailed overlay now shows exactly what was restored vs. lost. Items are color-coded (green = restored, red = lost, gold = FiR) with values and counts displayed. Auto-dismisses after 12 seconds.

- **Loss Preview Overlay**: Press **Ctrl+Alt+F9** during a raid to see a real-time preview of what you would lose if you died right now. Shows items not in your snapshot with estimated values.

- **Protection Indicator**: Visual indicator showing which items are currently protected by an active snapshot. Toggleable via settings.

- **Loadout Profiles**: Save and load multiple gear configurations as named profiles. Quick-switch between saved loadouts.

- **Auto-Snapshot by Value Threshold**: Configure automatic snapshots when your current loot exceeds a specified ruble value. Great for protecting valuable finds without manual intervention.

- **Snapshot History & Statistics**: Track your restoration history across raids. See statistics on items saved, deaths, extractions.

- **Theme System**: Choose from 5 visual themes for overlays: Default, Neon, Tactical, HighContrast, Minimal.

### Added - New Services

- **ValueCalculator**: Estimates item values using game data for loss preview and auto-snapshot features.
- **SnapshotHistory**: Tracks raid-by-raid snapshot history for statistics.
- **RestorationSummary**: Generates detailed restoration vs. loss data for the summary overlay.
- **SummaryFileWatcher**: Monitors for summary data files to trigger UI updates.
- **ThemeService**: Manages UI theme colors and styling.

### Added - Compatibility System

- **ModDetector**: Improved detection of SVM and other potentially conflicting mods.
- **CompatibilityManager**: Manages compatibility workarounds automatically.
- **ConflictReport**: Reports detected mod conflicts with actionable information.

### Added - New Configuration Options

- Auto-snapshot value threshold (ruble amount)
- Max auto-snapshots per raid
- Snapshot history size
- Show protection indicator toggle
- Show death summary toggle
- Visual theme selection
- Loss preview keybind (Ctrl+Alt+F9)

### Fixed - Security & Reliability (26 Issues)

- **SEC-001**: Added session ID validation to prevent path traversal attacks
- **SEC-002**: Added 10MB file size limit before JSON deserialization (DoS prevention)
- **REL-001**: Fixed potential null reference after JSON deserialization in SnapshotManager
- **REL-002**: Fixed Path.GetDirectoryName null handling in ProfileService
- **REL-003**: Added thread-safe queue access in NotificationOverlay
- **LOG-001/002/003**: Fixed null-forgiving operators without null checks in SnapshotRestorer
- **CON-001**: Added volatile to static fields in KeybindMonitor for thread safety
- **CON-002**: Added volatile to preset application flag in Settings
- **CON-003**: Added proper double-check locking in SnapshotSoundPlayer

### Changed

- **Major Version Bump**: Version 2.0.0 signifies the significant feature expansion with 7+ new user-facing features and ~5,500 lines of new code.

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [1.4.10] - 2026-01-11

### Fixed

- **Release Packaging Bug**: Fixed v1.4.9 release containing v1.4.8 client DLL. The csproj version numbers were not updated before the release build, causing version mismatch between source code and compiled binaries.

### Changed

- **Version Synchronization**: All version numbers (csproj, Plugin.cs, Constants.cs, ModMetadata.cs, KeepStartingGearMod.cs) are now properly synchronized to prevent future version mismatches.

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

## [1.4.9] - 2025-01-10

### Fixed

- **High Capacity Magazine Ammo Bug**: Fixed critical issue where large magazines (100-round boxes, X-25s, X-FALs, RPD Buben) only partially restored ammo on death (e.g., 100 rounds → 20 rounds). The root cause was that `LocationIndex` for cartridge positions was marked with `[JsonIgnore]`, preventing multi-stack magazines from serializing position data correctly. Added polymorphic `LocationData` property that correctly serializes either grid positions (objects) or cartridge indices (integers). (Reported by @rimmyjob via GitHub #13)

- **Archive Folder Structure for SPT 4.x**: Fixed release archive using SPT 3.x folder structure (`user/mods/`) instead of SPT 4.x structure (`SPT/user/mods/`). This was causing installation failures where the server mod wasn't found. Many users reported the mod "not working" due to this issue. (Reported by @dilirity via GitHub #12, @najix, @Matheus, @katzenmadchenaufbier, @Alake, @stiffe0114 on Forge)

- **Stale Snapshots After Transit Extracts**: Fixed snapshots not being cleared when using Transit extracts (car extracts, co-op extracts). The client-side code only treated `Survived` and `Runner` as successful extractions, but not `Transit`. This caused old snapshots to persist and be restored in future raids. (Reported by @Buszman, @kurdamir2 on Forge)

### Added

- **Disable On-Screen Notifications Option**: Added "Show On-Screen Notifications" setting (default: true) that allows users to disable the visual popup notifications while keeping the snapshot sound. Useful for players who want less on-screen clutter. (Requested by @Vafelz via GitHub #14)

### Changed

- **SPT 4.0.11 Compatibility**: Updated server NuGet packages from 4.0.8 to 4.0.11 for latest SPT compatibility.

### Technical

- **Polymorphic Location Serialization**: `SerializedItem` now uses a computed `LocationData` property that returns either an `ItemLocation` object (for grid items) or an integer (for magazine cartridges). This fixes the JSON serialization issue where cartridge positions were lost.

- **Server-Side Location Parsing**: `SnapshotItem.Location` is now a `JsonElement?` type with helper methods `GetLocation()` and `GetLocationIndex()` to handle polymorphic deserialization of location data.

- **Transit Exit Status Handling**: `RaidEndPatch.cs` now includes `ExitStatus.Transit` in the list of successful extraction statuses that trigger snapshot cleanup.

### Clarifications

- **FiR Status on Death**: Clarified that items in secure container losing FiR status on death is **standard Tarkov behavior**, not a mod bug. The mod's `ProtectFIRItems` setting controls whether FiR items are captured in snapshots (to prevent duplication exploits), not whether FiR status is preserved after death.

### Contributors

- **@rimmyjob** - Reported high capacity magazine ammo bug with detailed logs via GitHub #13
- **@dilirity** - Identified folder structure issue and provided fix via GitHub #12
- **@Vafelz** - Requested notification disable option via GitHub #14
- **@najix, @Matheus, @katzenmadchenaufbier, @Alake, @stiffe0114** - Reported installation/folder structure issues on Forge
- **@Buszman, @kurdamir2** - Reported stale snapshot issues on Forge
- **@Bert** - Reported FiR status behavior (clarified as expected Tarkov behavior)

### Compatibility

- SPT 4.0.x (tested on 4.0.11)

---

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
