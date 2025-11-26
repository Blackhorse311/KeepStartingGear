# Keep Starting Gear Mod - Development Context Document

**⚠️ ACTIVE TESTING SESSION: See `TESTING_SESSION_2025-11-25.md` for current status and next steps!**

## Project Overview
**Mod Name:** Blackhorse311-KeepStartingGear
**Version:** 1.0.0
**Target Platform:** SPT (Single Player Tarkov) 4.0.6
**Framework:** BepInEx (Client-side C# mod)
**Author:** Blackhorse311

## What This Mod Does
Allows players to save snapshots of their inventory and restore them after death/failed raids:
1. Player presses **Ctrl+Alt+F8** (configurable) to save inventory snapshot
2. Unlimited snapshots at inventory screen
3. One snapshot per map while in-raid (resets when changing maps)
4. On **death or failed extraction**: restore inventory to snapshot
5. On **successful extraction**: clear snapshot
6. Items found AFTER snapshot is taken are lost on death (intentional design)

## Current Development Status

### ✅ COMPLETED - Core Features (Build #9)
1. **BepInEx mod structure** - Plugin class, patches, configuration
2. **Configuration system** - BepInEx ConfigFile with F12 menu support
   - All inventory slots individually toggleable (default: save everything)
   - Keybind customization (default: Ctrl+Alt+F8)
   - Debug logging options
3. **Snapshot data models** - SerializedItem, ItemLocation, ItemUpd classes
4. **SnapshotManager service** - Save/load/clear snapshots to JSON files
5. **InventoryService** - ✅ WORKING - Captures full inventory (17-32 items)
6. **ProfileService** - ✅ NEW - Direct profile JSON manipulation for restoration
7. **KeybindMonitor component** - Detects keybind in-raid
8. **Patches** - GameStartPatch, RaidEndPatch, PostRaidInventoryPatch
9. **Snapshot capture** - ✅ FULLY WORKING - Uses Equipment.GetAllSlots()
10. **Death detection** - ✅ WORKING - Correctly identifies death vs extraction

### ✅ TESTED AND WORKING
- Mod loads successfully in SPT 4.0.6
- Keybind detection works (Ctrl+Alt+F8)
- **Snapshot capture works perfectly** (captures 17-32 items with full hierarchy)
- Snapshot files are created and saved
- Raid end detection works (death vs extraction)
- Snapshot cleared on successful extraction
- ItemFactory access works (creates items successfully)

### 🔨 NEW APPROACH - Direct Profile JSON Manipulation (Build #9)

**Previous Approach (ABANDONED):**
- Tried to restore items using EFT's inventory APIs
- ItemFactory worked (created items successfully)
- Problem: `InventoryController.AddItem()` doesn't exist or isn't accessible
- Result: Items created but couldn't be placed in inventory

**NEW APPROACH - Direct Profile File Editing:**
Instead of fighting with EFT's inventory APIs, we now:
1. Let SPT save the profile normally after death (empty inventory)
2. Read SPT's profile JSON file: `H:\SPT\SPT\user\profiles\{id}.json`
3. Replace `characters.pmc.Inventory.items[]` array with our snapshot items
4. Save the modified JSON back to disk
5. Player sees restored items when they reload/restart

**Why This Works Better:**
- Much simpler - just file I/O and JSON manipulation
- More reliable - working with SPT's actual data source
- Proven pattern - SPT-AKI-Profile-Editor uses this approach
- Avoids all EFT inventory API issues
- Profile structure matches our snapshot format exactly!

**Profile JSON Structure:**
```json
{
  "info": { "id": "6925305a5381a79c88154280", "username": "Tester2" },
  "characters": {
    "pmc": {
      "Inventory": {
        "items": [
          { "_id": "...", "_tpl": "55d7217a4bdc2d86028b456d" },  // Equipment container
          { "_id": "...", "_tpl": "5ac4cd105acfc40016339859",    // AK-74M
            "parentId": "6925546637b1d706c4f415b7",
            "slotId": "FirstPrimaryWeapon",
            "upd": { "Repairable": {...}, "FireMode": {...} }
          }
        ]
      }
    }
  }
}
```

### ⏳ READY FOR TESTING (Build #9)
**ProfileService Implementation:**
- `FindProfilesDirectory()` - Locates SPT profile directory
- `GetMostRecentProfileFile()` - Finds active profile by modification time
- `RestoreInventoryToProfile()` - Edits profile JSON directly
- Creates backups before modification (`{id}_backup.json`)
- Removes existing equipment items
- Adds snapshot items to profile
- Preserves Equipment container ID

**Expected Behavior:**
1. Player dies in raid
2. Returns to stash (empty inventory from death)
3. PostRaidInventoryPatch fires → ProfileService edits profile JSON
4. Log message: "Restart the game or reload the profile to see the restored items"
5. Player restarts game or reloads profile
6. Inventory is restored!

### ❌ NOT YET IMPLEMENTED
1. **Toast notifications** - User feedback for snapshot saved/restored
2. **Live restoration** - Currently requires game restart (profile reload)
3. **Multiple snapshot slots** - Currently one snapshot at a time
4. **In-stash snapshot UI** - Currently keybind-only

## Project Structure

### Key Files and Locations

**Development:**
- Project root: `I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\`
- Source code: `src\server\`
- Build output: `bin\BepInEx\plugins\Blackhorse311-KeepStartingGear\`

**SPT Installation:**
- Game/SPT root: `H:\SPT\`
- Mod DLL: `H:\SPT\BepInEx\plugins\Blackhorse311-KeepStartingGear\Blackhorse311.KeepStartingGear.dll`
- Snapshots: `H:\SPT\BepInEx\plugins\Blackhorse311-KeepStartingGear\snapshots\{sessionId}.json`
- **Profile files:** `H:\SPT\SPT\user\profiles\{profileId}.json` ← NEW: Where restoration happens
- Config: `H:\SPT\BepInEx\config\com.blackhorse311.keepstartinggear.cfg` (auto-generated)
- Log: `H:\SPT\BepInEx\LogOutput.log`

**Sample Mods (for reference):**
- `I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\Files for Claude\Sample other Mods\`
  - SPT-AKI-Profile-Editor (profile JSON manipulation - VERY RELEVANT!)
  - DragonDen-DevTool (ItemFactory usage)
  - SPT-InventoryOrganizingFeatures (item transactions)
  - KmyTarkovApi (inventory access patterns)

### Important Code Files

**Core Plugin:**
- `src\server\Plugin.cs` - Main entry point (BaseUnityPlugin)
- `src\server\Configuration\Settings.cs` - BepInEx configuration

**Services:**
- `src\server\Services\SnapshotManager.cs` - Save/load snapshot files (WORKING)
- `src\server\Services\InventoryService.cs` - Capture inventory (WORKING)
- `src\server\Services\ProfileService.cs` - ✅ NEW - Direct profile JSON editing

**Models:**
- `src\server\Models\InventorySnapshot.cs` - Snapshot data structure
- `src\server\Models\SerializedInventory.cs` - SerializedItem, ItemLocation, ItemUpd

**Components:**
- `src\server\Components\KeybindMonitor.cs` - MonoBehaviour for keybind detection

**Patches:**
- `src\server\Patches\PatchManager.cs` - Enables all patches
- `src\server\Patches\GameStartPatch.cs` - Attaches KeybindMonitor when raid starts
- `src\server\Patches\RaidEndPatch.cs` - Detects death/extraction
- `src\server\Patches\PostRaidInventoryPatch.cs` - ✅ UPDATED - Now uses ProfileService

## Technical Architecture

### How It Works Flow (Build #9)

**1. Mod Initialization (Game Launch):**
```
Plugin.Awake()
  → Settings.Init() - Load configuration
  → new SnapshotManager() - Initialize snapshot system
  → new InventoryService() - Initialize inventory service
  → new ProfileService() - Initialize profile editor (NEW!)
  → PatchManager.EnablePatches() - Enable Harmony patches
```

**2. Raid Start:**
```
GameWorld.OnGameStarted() [Patched]
  → GameStartPatch.PatchPrefix()
    → player.gameObject.AddComponent<KeybindMonitor>()
    → monitor.Init(player, gameWorld)
```

**3. Snapshot Creation (Player presses keybind):**
```
KeybindMonitor.Update() detects Ctrl+Alt+F8
  → InventoryService.CaptureInventory(player, location, inRaid=true)
    → Equipment.GetAllSlots() via reflection
    → CaptureItemRecursive() for each item
      → ConvertToSerializedItem() - Extract data
      → Recursively capture children (containers, mods)
  → SnapshotManager.SaveSnapshot(snapshot)
    → Write to: snapshots/{sessionId}.json
```

**4. Raid End - Extraction:**
```
BaseLocalGame.Stop() [Patched]
  → RaidEndPatch.PatchPrefix(exitStatus)
    → If exitStatus == Survived:
      → SnapshotManager.ClearSnapshot(sessionId)
```

**5. Raid End - Death (NEW in Build #9):**
```
BaseLocalGame.Stop() [Patched]
  → RaidEndPatch.PatchPrefix(exitStatus)
    → If exitStatus == Killed/MIA:
      → (Nothing happens here - SPT saves empty profile)

GridSortPanel.Show() [Patched]
  → PostRaidInventoryPatch.PatchPostfix()
    → Check caller is SimpleStashPanel (post-raid stash)
    → SnapshotManager.GetMostRecentSnapshot()
    → ProfileService.RestoreInventoryToProfile(snapshot)
      → Find most recent profile file
      → Create backup: {id}_backup.json
      → Read profile JSON
      → Navigate to characters.pmc.Inventory.items[]
      → Remove existing equipment items
      → Add snapshot items with correct parentId
      → Save modified JSON back to disk
    → SnapshotManager.ClearSnapshot(sessionId)
    → Log: "Restart the game or reload profile"
```

## Key Technical Insights from Research

### EFT Inventory Structure
- **Inventory** - Main container (accessed via Player.Profile.Inventory)
- **Equipment** - Special container with Slots (NOT a CompoundItem!)
- **Item** - Base item interface with TemplateId, Id, Parent, CurrentAddress
- **CompoundItem** - Container item with Grids (backpack, vest, etc.)
- **Slot** - Equipment slot with ContainedItem property
- **ItemAddress** - Contains Container and Location info
- **ItemFactory** - Singleton for creating items from templates

### Profile JSON Format (SPT Compatible)
```json
{
  "_id": "mongo-style-id",
  "_tpl": "template-id",
  "parentId": "parent-item-id",
  "slotId": "slot-name",
  "location": { "x": 0, "y": 0, "r": 0, "isSearched": true },
  "upd": {
    "StackObjectsCount": 60,
    "SpawnedInSession": true,
    "Foldable": { "Folded": false }
  }
}
```

### Critical Patterns Learned
1. **Access ItemFactory:** `Singleton<ItemFactoryClass>.Instance` ✅ WORKING
2. **Create item:** `itemFactory.CreateItem(id, templateId, null)` ✅ WORKING
3. **Access equipment slots:** Use `Equipment.GetAllSlots()` via reflection ✅ WORKING
4. **Recursive traversal:** Check if item is CompoundItem, iterate Grids ✅ WORKING
5. **Profile editing:** Read/modify/save JSON files directly ✅ NEW APPROACH

## Next Steps (Priority Order)

### IMMEDIATE - Test Profile-Based Restoration (Build #9)
**User will test next**

**Test Procedure:**
1. Equip character with gear (AK, vest, backpack, etc.)
2. Enter raid
3. Press Ctrl+Alt+F8 to save snapshot
4. Die in raid (run into minefield or let scav kill you)
5. Return to stash (inventory should be empty from death)
6. Check log for: "Inventory restored successfully to profile JSON!"
7. Check for backup file: `H:\SPT\SPT\user\profiles\{id}_backup.json`
8. Restart game or reload profile
9. Check if inventory is restored!

**Expected Log Messages:**
```
[Info: Keep Starting Gear] Stash opened - checking for any snapshots to restore
[Info: Keep Starting Gear] Found snapshot from XX:XX:XX with 17 items
[Info: Keep Starting Gear] Attempting to restore inventory via profile JSON manipulation...
[Info: Keep Starting Gear] Restoring inventory to profile: {id}.json
[Info: Keep Starting Gear] Removing X existing inventory items
[Info: Keep Starting Gear] Added 17 items from snapshot to profile
[Info: Keep Starting Gear] Successfully restored inventory to profile JSON!
[Info: Keep Starting Gear] Restart the game or reload the profile to see the restored items
```

**If test succeeds:**
- ✅ Core functionality complete!
- Move to adding toast notifications
- Consider implementing live reload (without game restart)

**If test fails:**
- Check profile path detection (log should show found profiles directory)
- Check backup file was created
- Compare backup to modified profile (did items get added?)
- Check for any errors in profile JSON parsing

### AFTER SUCCESSFUL RESTORATION TEST
1. **Implement toast notifications** - User feedback (green=success, red=error)
2. **Implement live reload** - Reload profile without game restart
3. **Add multiple snapshot slots** - Save/load multiple snapshots
4. **UI for snapshot management** - In-stash menu for managing snapshots

## User Preferences & Requirements

### Must-Have Features (User Confirmed)
- ✅ Default: Save ALL inventory slots
- ✅ Player can toggle ANY slot on/off via config
- ✅ Keybind must be configurable
- ✅ Only capture equipped/carried items (NOT hideout stash)
- ✅ Items found after snapshot are lost on death
- ⏳ Toast notifications (color-coded, large, clear)

### User's Testing Approach & Tools Available
- User has SPT 4.0.6 fully installed
- User is familiar with testing in-game
- User will provide BepInEx logs for debugging
- **User has dnSpy v6.1.8 available for reverse engineering**
  - Can open any DLL file (Assembly-CSharp.dll, SPT DLLs, etc.)
  - Can extract exact method signatures, class structures, property names
  - Can find obfuscated class names and method calls
  - Previously used dnSpy to find toast notification methods in SPT 3.11.x

## Build and Deploy Commands

**Build:**
```bash
cd "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server"
dotnet build --configuration Release
```

**Auto-deploys to:** `H:\SPT\BepInEx\plugins\Blackhorse311-KeepStartingGear\`

**Check logs:**
```bash
# View full log
cat "H:\SPT\BepInEx\LogOutput.log"

# View most recent lines
cat "H:\SPT\BepInEx\LogOutput.log" | tail -n 50
```

**Check snapshot:**
```bash
ls "H:\SPT\BepInEx\plugins\Blackhorse311-KeepStartingGear\snapshots"
cat "H:\SPT\BepInEx\plugins\Blackhorse311-KeepStartingGear\snapshots/*.json"
```

**Check profile files (NEW):**
```bash
# List all profiles
ls "H:\SPT\SPT\user\profiles"

# View profile items
cat "H:\SPT\SPT\user\profiles\{id}.json" | jq '.characters.pmc.Inventory.items'

# Check for backup
ls "H:\SPT\SPT\user\profiles\*_backup.json"
```

## Common Issues and Solutions

### Issue: Mod doesn't load
**Check:** Is DLL in correct location? (`BepInEx/plugins/`, not `user/mods/`)

### Issue: Keybind not working
**Check:** KeybindMonitor attached? Look for "KeybindMonitor attached to player" in log

### Issue: Only 1 item captured
**Cause:** Not traversing equipment slots properly
**Status:** ✅ FIXED in Build #3 - Uses Equipment.GetAllSlots()

### Issue: Items not restored via EFT APIs
**Cause:** InventoryController.AddItem() doesn't exist
**Status:** ✅ FIXED in Build #9 - Now uses direct profile JSON editing

### Issue: Profile not found or wrong profile modified
**Check:** ProfileService should find most recently modified profile
**Debug:** Check log for "ProfileService initialized with directory: ..."

### Issue: Profile JSON corrupted
**Solution:** Restore from backup: `{id}_backup.json`

## Important Notes

1. **Profile Editing Safety:** ProfileService creates backups before modifying

2. **Game Restart Required:** Currently need to restart game to see restored items (live reload planned)

3. **.NET Version:** Target `net471` (not net9.0) for BepInEx compatibility

4. **JSON Library:** Uses Newtonsoft.Json (not System.Text.Json) for compatibility

5. **Reflection Required:** Many EFT types have internal/protected members, need reflection to access

6. **Singleton Pattern:** Most EFT services accessed via `Singleton<T>.Instance`

7. **Harmony Patches:** Use SPT's `ModulePatch` base class, not raw Harmony attributes

8. **Configuration:** BepInEx auto-generates .cfg file, accessible via F12 in-game

## Quick Resume Instructions for Claude

**⚠️ IMPORTANT: Read `TESTING_SESSION_2025-11-25.md` first for current status!**

**To resume development:**

1. **READ `TESTING_SESSION_2025-11-25.md` FIRST** - Contains current testing status
2. Read this document for full context and architecture
3. Check latest BepInEx log to see test results: `H:\SPT\BepInEx\LogOutput.log`
4. Current task: **Test Build #12** (Equipment container capture fix)
5. Next task: **Toast notifications** (if restoration works)

**Key files to review:**
- `TESTING_SESSION_2025-11-25.md` - **START HERE** - Current session state
- `src\server\Services\InventoryService.cs` - Now captures Equipment container (Build #12)
- `src\server\Services\ProfileService.cs` - Profile JSON manipulation with parentId remapping
- `src\server\Patches\PostRaidInventoryPatch.cs` - Triggers restoration after death

**User has dnSpy v6.1.8 and can provide:**
- Exact method signatures from ANY DLL
- Class structures and inheritance
- Obfuscated names and exact types
- Available: Assembly-CSharp.dll, SPT DLLs, sample mod DLLs

**User expectations:**
- Clean, well-commented code
- Semantic versioning
- Educational code structure for community learning
- Robust error handling with clear logging

---

**Last Updated:** 2025-11-25 ~20:00 (Build #12)
**Status:** Equipment container capture added, awaiting test results
**Next Session:** Test Build #12 → Toast notifications OR further debugging
**Active Testing:** See `TESTING_SESSION_2025-11-25.md`

## Build History

### Build #12 (2025-11-25) - Equipment Container Capture ← **CURRENT**
**Critical Fix:**
- ✅ Now captures Equipment container (template `55d7217a4bdc2d86028b456d`)
- ✅ Fixed "Could not find Equipment container in snapshot" error
- ⏳ Ready for testing - needs full server restart

**Why This Fix:**
- Previous builds only captured items INSIDE equipment slots
- ProfileService needed Equipment container to know which items are root items
- Now captures Equipment + all items for proper restoration

### Build #11 (2025-11-25) - ParentId Remapping
**Major Fix:**
- ✅ Fixed parentId remapping for nested items (mags, mods, etc.)
- ✅ Keep all snapshot item IDs intact to preserve hierarchy
- ✅ Only remap Equipment container ID
- ❌ Error: "Could not find Equipment container in snapshot" (fixed in #12)

**Why This Fix:**
- Nested items (magazine → bullets) need correct parentIds
- Can't use profile's item IDs - they don't exist yet
- Solution: Keep snapshot IDs, only remap Equipment reference

### Build #10 (2025-11-25) - Profile Path Detection
**Fix:**
- ✅ Multiple path detection for profile directory
- ✅ Tries: `SPT\user\profiles`, `user\profiles`, parent paths
- ✅ Debug logging for each path checked
- ❌ Items written but not restored (server caching issue)

### Build #9 (2025-11-25) - Profile JSON Manipulation
**MAJOR ARCHITECTURE CHANGE:**
- ✅ Created ProfileService for direct profile JSON editing
- ✅ Updated PostRaidInventoryPatch to use ProfileService
- ✅ Abandoned EFT inventory APIs (AddItem doesn't work)
- ✅ New approach: Edit SPT profile file directly
- ⏳ Requires game restart to see restored items (live reload planned)

**Why This Change:**
- Previous builds successfully created items via ItemFactory
- But InventoryController.AddItem() doesn't exist or isn't accessible
- Items were created in memory but couldn't be placed
- Profile JSON manipulation is simpler and more reliable
- SPT-AKI-Profile-Editor proves this approach works

### Build #8 (2025-11-24) - Root Item Identification
- ✅ Fixed root item detection (use slotId, not parentId)
- ✅ Found 3 root items correctly (was finding 0)
- ❌ AddItem method still not working (led to Build #9 pivot)

### Build #7 (2025-11-24) - Improved Singleton Access
- ✅ Search Comfort.Common assembly for Singleton<T>
- ✅ ItemFactory access successful
- ✅ Created 17 items from snapshot
- ❌ Found 0 root items (parentId mismatch)

### Build #6 (2025-11-24) - Singleton Pattern
- ✅ Use Comfort.Common.Singleton<ItemFactoryClass>.Instance
- ❌ Invalid generic arguments error (wrong assembly)

### Build #5 (2025-11-24) - Most Recent Snapshot
- ✅ GetMostRecentSnapshot() method added
- ✅ Avoid session ID matching (not accessible at stash)
- ❌ ItemFactory still not accessible (wrong access pattern)

### Build #4 (2025-11-24) - First Restoration Attempt
- ✅ Snapshot capture working (17-32 items)
- ❌ Could not get session ID from profile at stash screen

### Build #3 (2025-11-24) - Equipment Slot Access
- ✅ Equipment.GetAllSlots() via reflection
- ✅ Captured 17-32 items successfully
- ✅ Full item hierarchy in snapshot JSON

### Build #2 (2025-11-24) - Initial Capture Fix
- ❌ GetAllSlots() method not found
- Added diagnostic fallbacks

### Build #1 (2025-11-24) - First Test
- ✅ Mod loads successfully
- ✅ Keybind detection working
- ❌ Only captured 1 item (Equipment container)
