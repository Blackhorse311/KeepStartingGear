# Testing Session - 2025-11-25

## Final Status: BUILD #16 - SUCCESS!

### The Breakthrough
After 7 builds testing various approaches, **Build #16 achieved full functionality** using an exit status change approach.

---

## Test Results Summary

| Build | Approach | Result |
|-------|----------|--------|
| #9 | Profile JSON manipulation | ❌ Profile path not found |
| #10 | Fixed path detection | ❌ Server cache prevents visibility |
| #11 | ParentId remapping | ❌ Missing Equipment container |
| #12 | Equipment container capture | ❌ Server cache still blocks |
| #13 | RecreateCurrentBackend() | ❌ Only reloads client, not server data |
| #14 | In-memory inventory restore | ❌ Slots locked, data not persisting |
| #15 | Clear slots before placement | ❌ Game sends data from different source |
| **#16** | **Exit status change** | **✅ FULL SUCCESS** |

---

## Build #16 - The Working Solution

### What We Learned
The fundamental problem with builds #9-15 was that we were trying to restore items AFTER the raid ended. By that point:
1. The game had already captured the "dead" inventory state
2. The server had already cached the empty profile
3. Any modifications we made came too late

### The Solution
Instead of restoring items, we **prevent them from being lost** by changing the exit status:

```csharp
// RaidEndPatch.cs - PatchPrefix (runs BEFORE raid processing)
[PatchPrefix]
private static void PatchPrefix(ref ExitStatus exitStatus, ...)
{
    if (playerDied && hasValidSnapshot)
    {
        exitStatus = ExitStatus.Runner;  // "Run-through" keeps all gear
    }
}
```

### Why It Works
- `ExitStatus.Runner` = Player extracted too quickly for full rewards
- BUT all gear is preserved (same as normal extraction)
- The `ref` keyword lets us modify the parameter before the game processes it
- No file manipulation, no cache issues, works immediately

### Test Results
```
Tester3 equipped with:
- Backpack with items
- Tactical vest
- Pockets
- Various gear

Actions:
1. Pressed Ctrl+Alt+F8 in raid → Toast: "Snapshot saved! (22 items)"
2. Died in raid
3. Toast appeared: "Gear preserved! (Run-through status applied)"
4. Returned to hideout → ALL GEAR PRESENT including looted items!
```

---

## Technical Details

### Approaches We Tried

#### 1. Profile JSON Manipulation (Builds #9-12)
- **Concept:** Write items directly to profile JSON file on disk
- **Problem:** SPT server caches profiles in memory, doesn't re-read from disk
- **Result:** Items written but never visible in-game

#### 2. Backend Reload (Build #13)
- **Concept:** Call `TarkovApplication.RecreateCurrentBackend()` to force reload
- **Problem:** Reloads client from server, but server still has old cached data
- **Result:** Toast showed success, but items not visible

#### 3. In-Memory Restoration (Builds #14-15)
- **Concept:** Directly modify player's inventory objects in memory
- **Problem 1:** Equipment slots locked (Pockets especially)
- **Problem 2:** Game sends raid-end data from a snapshot taken earlier, not live inventory
- **Result:** Items placed in memory but not persisted to server

#### 4. Exit Status Change (Build #16) ✅
- **Concept:** Change `Killed` to `Runner` before raid end processes
- **Why it works:** Game processes raid end with modified status, preserves gear naturally
- **Result:** Full success, immediate visibility, no restart needed

### Key Discovery
The SVM (Server Value Modifier) mod uses this same technique on the server side:
```csharp
if (info.Results.Result != ExitStatus.SURVIVED && cf.Raids.SaveGearAfterDeath)
{
    info.Results.Result = ExitStatus.RUNNER;
}
```

We adapted this to work on the client side via Harmony prefix patch.

---

## Current Behavior

### When Player Dies WITH Snapshot:
1. RaidEndPatch detects `ExitStatus.Killed`
2. Checks for valid snapshot
3. Changes exit status to `ExitStatus.Runner`
4. Shows toast: "Gear preserved! (Run-through status applied)"
5. Game processes raid as "run-through"
6. All gear preserved (snapshot items + looted items)

### When Player Dies WITHOUT Snapshot:
1. RaidEndPatch detects `ExitStatus.Killed`
2. No valid snapshot found
3. Exit status unchanged
4. Normal death behavior - gear lost

### When Player Extracts Successfully:
1. RaidEndPatch detects `ExitStatus.Survived` or `ExitStatus.Runner`
2. Clears snapshot
3. Shows toast: "Extracted successfully! Snapshot cleared."
4. Normal extraction - gear kept

---

## Files Modified in Final Solution

### RaidEndPatch.cs (Complete Rewrite)
```csharp
// Key changes:
// 1. Added 'ref' to exitStatus parameter
// 2. Simplified to just change exit status
// 3. Removed complex in-memory restoration code

[PatchPrefix]
private static void PatchPrefix(
    BaseLocalGame<EftGamePlayerOwner> __instance,
    ref ExitStatus exitStatus,  // 'ref' allows modification
    string exitName)
{
    if (playerDied && snapshot != null && snapshot.IsValid())
    {
        exitStatus = ExitStatus.Runner;
        SnapshotManager.Instance.ClearSnapshot(snapshot.SessionId);
        NotificationManagerClass.DisplayMessageNotification(
            "[Keep Starting Gear] Gear preserved! (Run-through status applied)");
    }
}
```

---

## Known Issues / Future Work

### Current Limitations
1. **Run-through penalty:** Raid shows as "Run Through" in stats
2. **Keeps everything:** All gear preserved, not just snapshot items
3. **Stale snapshots:** Old snapshots may persist across sessions

### Future Enhancements
1. **Large centered notifications** - More visible feedback
2. **Snapshot-only restore** - Option to lose looted items
3. **Snapshot cleanup** - Auto-expire or clear on game start
4. **Multiple snapshots** - Per-map or named slots

---

## Quick Reference for Future Sessions

### Build Command
```bash
cd "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server"
dotnet build --configuration Release
```

### Test Procedure
1. Start SPT server
2. Launch game
3. Select test profile
4. Equip gear
5. Press Ctrl+Alt+F8 (snapshot)
6. Enter raid
7. Die
8. Verify gear preserved in hideout

### Log Location
`H:\SPT\BepInEx\LogOutput.log`

### Key Log Messages
```
[Info: Keep Starting Gear] Raid ending - Original exit status: Killed
[Info: Keep Starting Gear] Found snapshot with X items
[Info: Keep Starting Gear] Changing exit status from Killed to Runner
[Info: Keep Starting Gear] Exit status changed: Killed -> Runner
[Info: Keep Starting Gear] Gear should be preserved on return to hideout!
```

---

## Session Timeline

- **Morning:** Started testing Build #12 (Equipment container fix)
- **Test #12:** Failed - server cache issue discovered
- **Build #13:** Added RecreateCurrentBackend() - Failed
- **Build #14:** In-memory restoration - Partial success (2 items placed)
- **Build #15:** Clear slots first - Failed
- **Build #16:** Exit status change - **SUCCESS!**
- **Evening:** Documentation updated, core functionality complete

---

**Session End:** 2025-11-25 ~22:30
**Status:** Core functionality WORKING
**Next Steps:** Polish features, large notifications, cleanup
