# ADR-002: SPT 3.11.4 Backward Compatibility

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311
**Categories:** Architecture | Compatibility

---

## Context

Some users have requested that Keep Starting Gear support SPT 3.11.4, an older version of Single Player Tarkov. The mod was developed for SPT 4.0.x and uses APIs and patterns specific to that version.

A thorough investigation was conducted comparing SPT 3.11.4 and 4.0.x architectures, including:
- SPT 3.11.4 installation structure
- dnSpy extracts of key game classes
- Server mod loading mechanisms
- NuGet package availability

### Current State

Keep Starting Gear targets SPT 4.0.x exclusively:
- **Server mod**: Uses SPT 4.0.x DI system with `[Injectable]` attributes
- **Server mod**: Inherits from `MatchCallbacks` (4.0.x API)
- **Server mod**: References SPTarkov.* NuGet packages version 4.0.11
- **Client mod**: Uses reflection patterns for SPT 4.0.8+ field access

### Requirements

- Support SPT 3.11.4 without breaking 4.0.x support
- Maintain all existing functionality
- Minimize code duplication and maintenance burden

### Constraints

- Author does not run SPT 3.11.4 (testing limitations)
- NuGet package availability for 3.11.4 is unknown
- API differences between versions are partially unknown
- SPT 3.11.4 is an older version with declining user base

---

## Decision

**We will NOT support SPT 3.11.4 and will target SPT 4.0.x only.**

While the core game systems (inventory, equipment slots, raid handling) are nearly identical between versions, the server-side mod loading system differs significantly. Combined with unknown API compatibility, lack of testing capability, and the maintenance burden of supporting two versions, the effort is not justified.

---

## Options Considered

### Option 1: Do Nothing - Target SPT 4.0.x Only

Continue supporting only SPT 4.0.x and document this clearly.

**Pros:**
- No additional code to maintain
- No risk of breaking existing functionality
- Clear expectations for users
- Focus on current SPT version
- Single codebase, single test path

**Cons:**
- SPT 3.11.4 users cannot use the mod
- May receive requests for backward compatibility

**Effort:** Low (documentation only)

---

### Option 2: Create Separate SPT 3.11.4 Build

Create a parallel build targeting SPT 3.11.4 with conditional compilation.

**Technical Approach:**
```csharp
// Conditional path resolution
#if SPT_3114
    // SPT 3.11.4: BepInEx/plugins/spt/{ModFolder}/
    string sptRoot = Path.Combine(modDirectory, "..", "..", "..");
#else
    // SPT 4.0.x: SPT_Data/Server/mods/{ModFolder}/
    string sptRoot = Path.Combine(modDirectory, "..", "..", "..", "..");
#endif

// Dual reflection for field vs property access
private static object? GetLocationValue(object obj, string name)
{
    // Try property first (SPT 3.11.4)
    var prop = type.GetProperty(name);
    if (prop != null) return prop.GetValue(obj);

    // Try field (SPT 4.0.8+)
    var field = type.GetField(name);
    if (field != null) return field.GetValue(obj);

    return null;
}
```

**Pros:**
- Would enable SPT 3.11.4 users to use the mod
- Core game logic is 95% compatible
- Could use conditional compilation for differences

**Cons:**
- 50-80 hours of development effort
- Cannot be tested by author (doesn't use 3.11.4)
- Unknown if SPT 3.11.4 NuGet packages exist
- `MatchCallbacks` API may have changed (unknown)
- Two codepaths to maintain indefinitely
- SPT 3.11.4 is older version with declining users

**Effort:** High (50-80 hours)

---

### Option 3: Dynamic Runtime Version Detection

Single binary that detects SPT version at runtime and adapts behavior.

**Pros:**
- Single release for all versions
- Users don't need version-specific downloads

**Cons:**
- Complex reflection-based version detection
- Extremely fragile if APIs differ
- Runtime errors instead of compile-time errors
- Highest maintenance burden
- Testing nightmare

**Effort:** Very High (60-100 hours)

---

## Rationale

### Key Factors

1. **Unknown Blockers**: Critical unknowns exist that could make 3.11.4 support impossible:
   - NuGet package availability for SPTarkov.* 3.11.4 versions
   - `MatchCallbacks` constructor signature compatibility
   - DI system (`[Injectable]`) behavior differences

2. **Testing Limitations**: The mod author does not use SPT 3.11.4. Shipping untested code is irresponsible and would likely result in broken releases.

3. **Declining User Base**: SPT 4.0.x is the current version. Users on 3.11.4 are on an older release, and that population will only shrink over time.

4. **High Effort, Low Return**: 50-80 hours of development for a shrinking user base with unknown blockers is not a good investment.

5. **Maintenance Burden**: Supporting two SPT versions means double the testing, double the potential bugs, and ongoing compatibility work as both versions evolve.

### What We Found Compatible (95%)

The investigation revealed that core game systems are nearly identical:

| Component | SPT 3.11.4 | SPT 4.0.x | Status |
|-----------|-----------|----------|--------|
| Equipment slots (all 16) | ✅ Same | ✅ Same | Identical |
| `ExitStatus` enum | ✅ Same | ✅ Same | Identical |
| Item structure (`_id`, `_tpl`, etc.) | ✅ Same | ✅ Same | Identical |
| `BaseLocalGame.Stop()` | ✅ Exists | ✅ Exists | Compatible |
| `Player.HealthController` | ✅ Exists | ✅ Exists | Compatible |
| Profile JSON format | ✅ Same | ✅ Same | Identical |
| BepInEx plugin system | ✅ Same | ✅ Same | Identical |

### What We Found Different (Blockers)

| Component | SPT 3.11.4 | SPT 4.0.x | Impact |
|-----------|-----------|----------|--------|
| Server mod path | `BepInEx/plugins/spt/` | `SPT_Data/Server/mods/` | Breaking |
| NuGet packages | Unknown | 4.0.11 available | Potential blocker |
| `MatchCallbacks` API | Unknown | Known | Potential blocker |
| `LocationInGrid` access | Properties (likely) | Fields | Fixable |
| DI registration | Unknown | `[Injectable]` | Potential blocker |

### Trade-offs Accepted

- SPT 3.11.4 users will not be able to use the mod
- May receive continued requests for backward compatibility
- Missing opportunity to serve legacy SPT users

---

## Consequences

### Positive

- Codebase remains simple and focused on current SPT version
- No risk of 3.11.4-related bugs affecting 4.0.x users
- Clear documentation prevents confusion
- Development effort stays focused on new features
- No ongoing backward compatibility maintenance

### Negative

- SPT 3.11.4 users cannot use the mod
- May receive negative feedback from legacy users
- Reduced potential user base (though small)

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| User complaints about 3.11.4 | Low | Low | Clear documentation, explain reasoning |
| Community fork for 3.11.4 | Very Low | Low | MIT license allows this |
| SPT 4.0.x breaks our mod | Medium | High | Monitor SPT updates, test promptly |

---

## Implementation

### Action Items

- [x] Investigate SPT 3.11.4 architecture thoroughly
- [x] Document technical findings and blockers
- [x] Create this ADR
- [ ] Update README.md with SPT version requirements
- [ ] Update compatibility table

### Future Consideration

If SPT 3.11.4 support becomes critical (unlikely), the path forward would be:

1. Verify NuGet package availability for 3.11.4
2. Extract `MatchCallbacks` via dnSpy and compare APIs
3. Create separate `.csproj` with conditional compilation
4. Find community volunteers to test (author cannot)
5. Maintain as separate release track

---

## Technical Appendix: SPT 3.11.4 vs 4.0.x Comparison

### Server Mod Installation Paths

```
SPT 3.11.4:
├── BepInEx/
│   └── plugins/
│       └── spt/
│           ├── spt-common.dll
│           ├── spt-core.dll
│           └── {YourMod}/
│               └── YourMod.dll  ← Server mod here
└── SPT.Server.exe

SPT 4.0.x:
├── BepInEx/
│   └── plugins/
│       └── {YourMod}/
│           └── YourMod.dll  ← Client mod here
├── SPT_Data/
│   └── Server/
│       └── mods/
│           └── {YourMod}/
│               └── YourMod.Server.dll  ← Server mod here
└── SPT.Server.exe
```

### Path Resolution Code Difference

```csharp
// SPT 4.0.x (current): Navigate 4 levels up from mod DLL
string sptRoot = Path.GetFullPath(
    Path.Combine(modDirectory, "..", "..", "..", ".."));
// From: SPT_Data/Server/mods/{ModFolder}/

// SPT 3.11.4 (would need): Navigate 3 levels up
string sptRoot = Path.GetFullPath(
    Path.Combine(modDirectory, "..", "..", ".."));
// From: BepInEx/plugins/spt/{ModFolder}/
```

### LocationInGrid Access Pattern

```csharp
// SPT 4.0.8+ uses public FIELDS
public class LocationInGrid
{
    public int x;  // Field
    public int y;  // Field
    public int r;  // Field (rotation)
}

// SPT 3.11.4 likely uses PROPERTIES
public class LocationInGrid
{
    public int X { get; set; }  // Property
    public int Y { get; set; }  // Property
    public int R { get; set; }  // Property (rotation)
}
```

This difference was discovered and fixed in v1.4.4 for SPT 4.0.8 compatibility. Supporting 3.11.4 would require the inverse fix or dual-access reflection.

### Verified Identical Components

From dnSpy extracts, these components are confirmed identical:

```csharp
// Equipment slots - IDENTICAL
public enum EquipmentSlot
{
    FirstPrimaryWeapon = 0,
    SecondPrimaryWeapon = 1,
    Holster = 2,
    Scabbard = 3,
    Backpack = 4,
    SecuredContainer = 5,
    TacticalVest = 6,
    Pockets = 7,
    Headwear = 8,
    Earpiece = 9,
    FaceCover = 10,
    Eyewear = 11,
    ArmBand = 12,
    // ... etc
}

// Exit status - IDENTICAL
public enum ExitStatus
{
    Survived = 0,
    Killed = 1,
    Left = 2,
    Runner = 3,
    MissingInAction = 4,
    Transit = 5
}

// BaseLocalGame.Stop() - EXISTS IN BOTH
public virtual void Stop(string profileId, ExitStatus exitStatus,
                         string exitName, float delay = 0f)
```

---

## Related Decisions

- [ADR-001](ADR-001-fika-compatibility.md): FIKA Multiplayer Mod Compatibility

---

## References

- SPT 3.11.4 installation examined: `I:\spt-dev\...\SPT3.11.4\SPT 3.11.4`
- dnSpy extracts examined: `I:\spt-dev\...\SPT3.11.4\3.11.4 dnSpy Extracts`
- SPT Documentation: https://docs.sp-tarkov.com/
- Investigation conducted: 2026-01-30

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Blackhorse311 | Initial decision after SPT 3.11.4 analysis |
