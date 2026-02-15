# ADR-003: Restoration Algorithm Invariants

## Status

Accepted

## Context

The restoration algorithm is the core business logic of Keep Starting Gear. It handles:
1. Removing items from managed equipment slots in the player's current profile
2. Adding items from a saved snapshot back into the profile
3. Preserving items in non-managed slots (normal death behavior for those slots)

Given that this algorithm manipulates player inventory data (which represents potentially hours of gameplay), correctness is **mission-critical**. A bug could result in:
- Item duplication (exploitable)
- Item loss (rage-inducing for players)
- Profile corruption (catastrophic)

This ADR documents the formal invariants that the restoration algorithm MUST uphold, enabling:
- Unit testing against a pure algorithm implementation
- Code review verification
- Future maintenance confidence

## Decision

We establish **6 inviolable invariants** for the restoration algorithm:

### INVARIANT 1: Equipment Container Preservation

**Statement:** The Equipment container (template ID `55d7217a4bdc2d86028b456d`) is NEVER removed from the profile.

**Rationale:** The Equipment container is the root node of the player's equipment hierarchy. Removing it would orphan all equipment.

**Verification:**
```csharp
// Equipment container ID must exist in profile before and after restoration
Assert.NotNull(FindEquipmentContainerId(profileItems));
```

### INVARIANT 2: SecuredContainer Slot Protection

**Statement:** Items in the `SecuredContainer` slot are NEVER removed during restoration, regardless of configuration.

**Rationale:** This matches core Tarkov behavior - secure containers are never lost on death. This prevents accidental deletion of gamma/epsilon containers and their contents.

**Verification:**
```csharp
// IsSlotManaged always returns false for SecuredContainer
Assert.False(IsSlotManaged("SecuredContainer", anyIncludedSlots, anySnapshotSlots, anyEmptySlots));
```

### INVARIANT 3: Parent Chain Termination

**Statement:** All parent chain traversals MUST terminate within `MaxParentTraversalDepth` (20) iterations.

**Rationale:** Corrupt snapshot data could contain circular parent references. Without cycle detection, restoration would hang indefinitely.

**Verification:**
```csharp
// TraceRootSlot returns null for cyclic data, does not hang
var cyclicItem = new AlgorithmItem { Id = "A", ParentId = "A" };
Assert.Null(TraceRootSlot(cyclicItem, equipmentId, lookup)); // Returns, does not hang
```

### INVARIANT 4: null vs Empty IncludedSlotIds Distinction

**Statement:** The algorithm MUST distinguish between:
- `IncludedSlotIds == null` → Legacy snapshot format, use fallback logic
- `IncludedSlotIds.Count == 0` → User explicitly selected no slots, normal death (no restoration)
- `IncludedSlotIds.Count > 0` → Specific slots to manage

**Rationale:** Issue #17 was caused by treating empty set as "protect everything." This subtle distinction is critical for correct behavior when users disable all protection.

**Verification:**
```csharp
// Empty set = normal death = no slots managed
var emptySet = new HashSet<string>();
Assert.False(IsSlotManaged("Backpack", emptySet, anySnapshotSlots, anyEmptySlots));
```

### INVARIANT 5: All Equipment Container IDs Checked

**Statement:** When removing items from managed slots, ALL Equipment container IDs in the profile must be considered, not just the first one found.

**Rationale:** Issue #18 was caused by items being parented to different Equipment container IDs between raid-end state and profile state. The game may use different container IDs in different contexts.

**Verification:**
```csharp
// FindAllEquipmentContainerIds returns all, not just first
var items = new[] {
    AlgorithmItem.Create("eq1", tpl: EquipmentTemplateId),
    AlgorithmItem.Create("eq2", tpl: EquipmentTemplateId)
};
Assert.Equal(2, FindAllEquipmentContainerIds(items).Count);
```

### INVARIANT 6: Pockets Slot Protection

**Statement:** The `Pockets` item is NEVER removed during restoration, regardless of configuration.

**Rationale:** Like SecuredContainer, Pockets is a permanent fixture of the player's Equipment. Deleting the Pockets item permanently corrupts the profile (empty rig slot, no pocket slots). This was discovered in v2.0.5 when users reported permanent pocket corruption after death. Both `DeleteNonManagedSlotItems` and `RemoveManagedSlotItems` must skip Pockets.

**Verification:**
```csharp
// Pockets item must survive restoration regardless of slot configuration
var profileItems = CreateProfileWithPockets();
SimulateRestoration(profileItems, snapshot, managedSlots);
Assert.Contains(profileItems, i => i.SlotId == "Pockets");
```

## Algorithm Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                     RESTORATION ALGORITHM                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  INPUT:                                                          │
│    - profileItems: Current player inventory                      │
│    - snapshotItems: Saved snapshot from raid start               │
│    - includedSlots: User-configured slots (nullable)             │
│    - emptySlots: Slots that were empty at snapshot time          │
│                                                                  │
│  STEP 1: Find Equipment Containers                               │
│    profileEquipmentId = FindEquipmentContainerId(profileItems)   │
│    snapshotEquipmentId = FindEquipmentContainerId(snapshotItems) │
│    IF profileEquipmentId == null THEN FAIL                       │
│                                                                  │
│  STEP 2: Build Slot Sets                                         │
│    includedSlotIds = includedSlots (null-preserving)             │
│    snapshotSlotIds = slots with items in snapshot                │
│    emptySlotIds = slots empty at snapshot time                   │
│                                                                  │
│  STEP 3: Determine Managed Slots                                 │
│    IF includedSlotIds == null THEN                               │
│      managed = snapshotSlotIds ∪ emptySlotIds  // Legacy         │
│    ELSE IF includedSlotIds.Count == 0 THEN                       │
│      managed = ∅  // Normal death                                │
│    ELSE                                                          │
│      managed = includedSlotIds  // User selection                │
│    ALWAYS EXCLUDE: SecuredContainer                              │
│                                                                  │
│  STEP 4: Find All Equipment IDs                                  │
│    allEquipmentIds = FindAllEquipmentContainerIds(profileItems)  │
│                                                                  │
│  STEP 5: Remove Items from Managed Slots                         │
│    FOR each item in profileItems:                                │
│      IF item.ParentId ∈ allEquipmentIds AND                      │
│         item.SlotId ∈ managed THEN                               │
│        Mark item and all descendants for removal                 │
│    Remove marked items (BFS traversal)                           │
│                                                                  │
│  STEP 6: Add Snapshot Items                                      │
│    FOR each item in snapshotItems:                               │
│      rootSlot = TraceRootSlot(item, snapshotEquipmentId)         │
│      IF item.Id already exists THEN SKIP (duplicate)             │
│      IF rootSlot ∉ managed THEN SKIP (non-managed)               │
│      IF item.Tpl == EquipmentTemplateId THEN SKIP                │
│      Add item, remap parentId if needed                          │
│                                                                  │
│  OUTPUT:                                                         │
│    - Modified profileItems with restored inventory               │
│    - Counts: added, removed, duplicates skipped                  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Testing Strategy

The algorithm is tested via a **pure implementation** in `RestorationAlgorithm.cs`:

1. **No SPT/BepInEx dependencies** - Uses lightweight `AlgorithmItem` instead of game types
2. **Deterministic** - Same inputs always produce same outputs
3. **Unit testable** - 31 tests covering all invariants and edge cases

Test categories:
- `FindEquipmentContainerIdTests`: Equipment container detection
- `BuildSlotSetsTests`: Slot set construction, case insensitivity
- `TraceRootSlotTests`: Parent chain traversal, cycle detection
- `IsSlotManagedTests`: Slot management rules, SecuredContainer protection
- `SimulateRestorationTests`: Full algorithm integration tests

## Consequences

### Positive
- **Provable correctness** via unit tests against documented invariants
- **Maintainability** - Future changes can be verified against invariants
- **Debugging** - Clear invariants help diagnose issue reports

### Negative
- **Code duplication** - Pure algorithm in test project mirrors production logic
- **Maintenance burden** - Must keep pure algorithm in sync with production

### Mitigations
- Production code includes comments referencing this ADR
- Tests serve as regression suite for invariant violations

## References

- Issue #17: Unprotected weapon slots preserving items
- Issue #18: All looted items kept after death
- Forge report: Secure container disappearing
- `src/tests/RestorationAlgorithm.cs`: Pure algorithm implementation
- `src/tests/RestorationAlgorithmTests.cs`: Invariant verification tests
