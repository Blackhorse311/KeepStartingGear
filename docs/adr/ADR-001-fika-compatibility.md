# ADR-001: FIKA Multiplayer Mod Compatibility

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311
**Categories:** Architecture | Compatibility

---

## Context

FIKA is a popular multiplayer co-op modification for SPT that allows multiple players to play together in raids. Many users have requested that Keep Starting Gear support FIKA.

A thorough investigation was conducted to determine the feasibility of FIKA compatibility, including analysis of:
- FIKA Plugin (Client-side BepInEx, 563 source files)
- FIKA Server (Server-side SPT DI system)
- Supporting mods (BringMeToLife, FikaDynamicAI, etc.)

### Current State

Keep Starting Gear uses a hybrid client-server architecture:
- **Client Plugin (BepInEx)**: Captures inventory snapshots at raid start or via keybind
- **Server Mod (SPT DI)**: Intercepts `MatchCallbacks.EndLocalRaid()` and overrides `InRaidHelper.DeleteInventory()` to restore gear on death

This architecture works because in single-player SPT:
1. Player dies
2. Our `RaidEndInterceptor` intercepts the raid end call
3. We restore inventory from snapshot
4. We set a flag to skip inventory deletion
5. Normal SPT processing continues with restored inventory

### Requirements

- Work with FIKA without modifying FIKA's source code
- Maintain full functionality of the existing single-player mod
- Avoid breaking changes for non-FIKA users

### Constraints

- Cannot modify FIKA source code (separate project, different maintainers)
- Must work with FIKA's networked co-op architecture
- Must handle host/client player distinction in FIKA
- Author does not use FIKA (testing limitations)

---

## Decision

**We will NOT support FIKA and will document this incompatibility clearly.**

The architectural differences between single-player SPT and FIKA multiplayer are fundamental. While a FIKA-compatible version is technically possible, the effort, complexity, and ongoing maintenance burden are not justified given:
1. The author does not play FIKA and cannot test changes
2. FIKA updates frequently, requiring ongoing compatibility maintenance
3. The implementation would require significant new code (~500-1000 lines)
4. Testing would rely entirely on community volunteers

---

## Options Considered

### Option 1: Do Nothing - Document Incompatibility

Mark the mod as incompatible with FIKA in documentation.

**Pros:**
- No additional code to maintain
- No risk of breaking existing functionality
- Clear expectations for users
- Focus remains on single-player experience

**Cons:**
- Disappoints users who want FIKA support
- May reduce mod adoption in FIKA community

**Effort:** Low

---

### Option 2: Create Separate FIKA Edition

Create a separate plugin (`Blackhorse311.KeepStartingGear.FIKA.dll`) that patches FIKA's methods via Harmony.

**Technical Approach:**
```csharp
// Patch FIKA's CoopGame.Stop() method (public virtual)
[HarmonyPatch(typeof(CoopGame), nameof(CoopGame.Stop))]
class FikaRaidEndPatch
{
    [HarmonyPrefix]
    static void Prefix(string profileId, ExitStatus exitStatus)
    {
        if (exitStatus == ExitStatus.Killed)
        {
            // Restore inventory BEFORE FIKA's SavePlayer() runs
            FikaSnapshotRestorer.RestoreOnDeath(profileId);
        }
    }
}
```

**Key Technical Findings:**
- `CoopGame.Stop()` is public virtual and patchable
- `SavePlayer()` is private async but called after `Stop()`
- FIKA's `FikaEventDispatcher` provides events, but they fire too early
- Our server-side `RaidEndInterceptor` can coexist with FIKA's `EndLocalRaidOverride`

**Pros:**
- Would enable FIKA users to use the mod
- No modification of FIKA source required
- Could leverage existing snapshot infrastructure

**Cons:**
- ~500-1000 lines of new code to maintain
- Cannot be tested by author (doesn't use FIKA)
- FIKA architecture changes could break compatibility
- Timing-sensitive - must restore before `SavePlayer()` serializes dead profile
- Host vs Client behavior differences add complexity

**Effort:** High

---

### Option 3: Contribute FIKA Support Upstream

Work with FIKA maintainers to add gear protection hooks.

**Pros:**
- Native FIKA integration
- Maintained by FIKA team
- Would benefit all FIKA users

**Cons:**
- Requires FIKA team buy-in
- No control over implementation timeline
- Feature may not align with FIKA's vision
- Still requires someone to implement and maintain

**Effort:** Medium (implementation) + Unknown (coordination)

---

### Option 4: Hybrid Auto-Detection

Detect FIKA at runtime and use different restoration strategies.

```csharp
if (IsFikaInstalled())
{
    // Use client-side restoration (patch CoopGame.Stop)
} else {
    // Use server-side restoration (current approach)
}
```

**Pros:**
- Single plugin for both scenarios
- Graceful degradation if FIKA detection fails

**Cons:**
- All the complexity of Option 2
- Additional detection/branching logic
- Two code paths to maintain and test

**Effort:** High

---

## Rationale

### Key Factors

1. **Testing Limitations**: The mod author does not use FIKA and cannot adequately test FIKA-specific functionality. Shipping untested code to users is irresponsible.

2. **Maintenance Burden**: FIKA is actively developed with frequent updates. Each FIKA update could potentially break compatibility, requiring ongoing maintenance without the ability to test.

3. **Architectural Complexity**: FIKA's death processing flow is fundamentally different:
   - Single-player: Death → Server intercepts → Restore → Skip deletion
   - FIKA: Death → Health sync packet → Stop() → Event dispatch → SavePlayer() → Server

   The window for restoration in FIKA is narrow and timing-dependent.

4. **Community Support**: If FIKA compatibility is highly desired, the community (who can test) could fork the project or contribute a FIKA compatibility layer.

### Trade-offs Accepted

- Some potential users will be unable to use the mod with FIKA
- May receive continued requests for FIKA support
- Missing opportunity to serve FIKA community

---

## Consequences

### Positive

- Codebase remains simple and focused
- No risk of FIKA-related bugs affecting single-player users
- Clear documentation prevents confusion
- Development effort stays focused on core functionality
- No ongoing FIKA compatibility maintenance burden

### Negative

- FIKA users cannot use the mod
- May receive negative feedback from FIKA community
- Reduced potential user base

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| User complaints about FIKA | Medium | Low | Clear documentation, explain reasoning |
| Community fork diverges | Low | Low | MIT license allows this; can merge good changes |
| FIKA becomes dominant | Low | Medium | Revisit decision if FIKA adoption exceeds 50% |

---

## Implementation

### Action Items

- [x] Investigate FIKA architecture thoroughly
- [x] Document technical findings
- [x] Create this ADR
- [ ] Update README.md with detailed FIKA incompatibility section
- [ ] Add FIKA to compatibility table with explanation

### Rollback Plan

If community demand is overwhelming and volunteers step up to test:
1. Create feature branch `feature/fika-support`
2. Implement Option 2 (separate FIKA edition)
3. Community testing period (minimum 2 weeks)
4. Merge only if thoroughly tested

---

## Technical Appendix: FIKA Architecture Analysis

### FIKA Death Processing Flow

```
1. Player health reaches 0
2. ClientHealthController.SendNetworkSyncPacket() - IsAlive = false
3. FikaPlayer.SetupCorpseSyncPacket() - broadcasts corpse state
4. CoopGame.Stop() called (PUBLIC VIRTUAL - patchable)
5. FikaGameEndedEvent dispatched (TOO EARLY for inventory)
6. Player objects disposed (lines 814-831)
7. SavePlayer() called with fromDeath=true (PRIVATE ASYNC)
8. CompleteProfileDescriptorClass created (profile snapshot)
9. iSession.LocalRaidEnded() sends to server (POINT OF NO RETURN)
```

### Critical FIKA Files

| File | Purpose | Relevance |
|------|---------|-----------|
| `CoopGame.cs:752-868` | Stop() method | Patchable entry point |
| `CoopGame.cs:873-913` | SavePlayer() | Where profile is serialized |
| `ClientHealthController.cs:26` | Death detection | Early hook point |
| `EndLocalRaidOverride.cs` | Server-side override | Coexists with our interceptor |
| `FikaEventDispatcher.cs` | Event system | Events fire too early |

### Why Server-Side Restoration Won't Work

FIKA's `EndLocalRaidOverride` patches `LocationLifecycleService.EndLocalRaid()` with a Prefix that can block the original method for spectators. More critically, by the time the server receives the raid end request, the client has already serialized the dead player's profile via `SavePlayer()`.

### Why Client-Side Restoration Is Complex

The restoration must happen in the narrow window between death detection and `SavePlayer()` execution. This requires:
1. Patching `CoopGame.Stop()` with a Prefix
2. Detecting death status from `exitStatus` parameter
3. Restoring inventory immediately
4. Ensuring restoration completes before async `SavePlayer()` runs

The async nature of `SavePlayer()` and the lack of await points make timing guarantees difficult.

---

## Related Decisions

- (Future) ADR-002: Server-Side vs Client-Side Restoration Architecture

---

## References

- FIKA GitHub: https://github.com/project-fika/Fika-Plugin
- FIKA Documentation: (internal to FIKA project)
- SPT Modding Documentation: https://docs.sp-tarkov.com/
- Investigation conducted: 2026-01-30

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Blackhorse311 | Initial decision after thorough FIKA analysis |
