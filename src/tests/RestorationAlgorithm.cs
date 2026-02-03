// ============================================================================
// Keep Starting Gear - Pure Restoration Algorithm
// ============================================================================
// This file contains the pure algorithm logic extracted from SnapshotRestorer
// for unit testing. It uses test-friendly types with no SPT/BepInEx dependencies.
//
// HEALTHCARE-GRADE NOTE:
// This is the canonical implementation of the restoration algorithm.
// The production SnapshotRestorer should use identical logic.
// Any divergence is a bug.
//
// INVARIANTS:
// 1. Equipment container is never removed
// 2. SecuredContainer slot is always preserved
// 3. Items are traced to their root slot via parent chain
// 4. Cycle detection prevents infinite loops
// 5. Legacy snapshots (null IncludedSlots) use fallback logic
//
// AUTHOR: Blackhorse311 + Linus Torvalds
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Blackhorse311.KeepStartingGear.Tests;

/// <summary>
/// Constants for the restoration algorithm.
/// </summary>
public static class AlgorithmConstants
{
    /// <summary>Equipment container template ID</summary>
    public const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

    /// <summary>SecuredContainer slot name (always preserved)</summary>
    public const string SecuredContainerSlot = "SecuredContainer";

    /// <summary>Maximum depth for parent chain traversal (cycle protection)</summary>
    public const int MaxParentTraversalDepth = 50;
}

/// <summary>
/// Lightweight item representation for algorithm testing.
/// </summary>
public class AlgorithmItem
{
    public string Id { get; set; } = "";
    public string? Tpl { get; set; }
    public string? ParentId { get; set; }
    public string? SlotId { get; set; }

    /// <summary>Creates an item with required fields.</summary>
    public static AlgorithmItem Create(string id, string? parentId = null, string? slotId = null, string? tpl = null)
        => new() { Id = id, ParentId = parentId, SlotId = slotId, Tpl = tpl };
}

/// <summary>
/// Result of slot set building operation.
/// </summary>
public record SlotSets(
    HashSet<string>? IncludedSlotIds,
    HashSet<string> SnapshotSlotIds,
    HashSet<string> EmptySlotIds
);

/// <summary>
/// Result of the restoration algorithm.
/// </summary>
public record RestorationResult(
    bool Success,
    int ItemsAdded,
    int ItemsRemoved,
    int DuplicatesSkipped,
    int NonManagedSkipped,
    HashSet<string>? ManagedSlotIds,
    string? ErrorMessage = null
)
{
    public static RestorationResult Succeeded(int added, int removed, int dupes = 0, int nonManaged = 0, HashSet<string>? managed = null)
        => new(true, added, removed, dupes, nonManaged, managed);

    public static RestorationResult Failed(string error)
        => new(false, 0, 0, 0, 0, null, error);
}

/// <summary>
/// Pure restoration algorithm implementation for healthcare-grade testing.
/// All methods are static and have no side effects beyond their return values.
/// </summary>
public static class RestorationAlgorithm
{
    // ========================================================================
    // INVARIANT: Equipment container must exist
    // ========================================================================

    /// <summary>
    /// Finds the Equipment container ID in a list of items.
    /// INVARIANT: Returns null only if no Equipment container exists.
    /// </summary>
    public static string? FindEquipmentContainerId(IEnumerable<AlgorithmItem> items)
    {
        foreach (var item in items)
        {
            if (item.Tpl == AlgorithmConstants.EquipmentTemplateId)
            {
                Debug.Assert(!string.IsNullOrEmpty(item.Id), "Equipment container must have an ID");
                return item.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds ALL Equipment container IDs in a list of items.
    /// Handles edge cases where multiple Equipment containers exist.
    /// </summary>
    public static HashSet<string> FindAllEquipmentContainerIds(IEnumerable<AlgorithmItem> items)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Tpl == AlgorithmConstants.EquipmentTemplateId && !string.IsNullOrEmpty(item.Id))
            {
                result.Add(item.Id);
            }
        }
        return result;
    }

    // ========================================================================
    // INVARIANT: Slot sets must be built correctly
    // ========================================================================

    /// <summary>
    /// Builds the slot tracking sets from snapshot data.
    ///
    /// INVARIANT: Returns distinguish between:
    /// - null IncludedSlotIds = legacy format (use fallback logic)
    /// - empty IncludedSlotIds = user explicitly selected no slots
    /// - populated IncludedSlotIds = user selected specific slots
    /// </summary>
    public static SlotSets BuildSlotSets(
        IReadOnlyList<AlgorithmItem> snapshotItems,
        IReadOnlyList<string>? includedSlots,
        IReadOnlyList<string>? emptySlots,
        string? snapshotEquipmentId)
    {
        // User-configured slots (null preserves legacy distinction)
        HashSet<string>? includedSlotIds = null;
        if (includedSlots != null)
        {
            includedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in includedSlots)
            {
                includedSlotIds.Add(slot);
            }
        }

        // Slots with items in snapshot
        var snapshotSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshotItems)
        {
            if (item.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(item.SlotId))
            {
                snapshotSlotIds.Add(item.SlotId);
            }
        }

        // Slots that were empty at snapshot time
        var emptySlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (emptySlots != null)
        {
            foreach (var slot in emptySlots)
            {
                emptySlotIds.Add(slot);
            }
        }

        return new SlotSets(includedSlotIds, snapshotSlotIds, emptySlotIds);
    }

    // ========================================================================
    // INVARIANT: Parent chain traversal must terminate
    // ========================================================================

    /// <summary>
    /// Builds an O(1) lookup dictionary for items by ID.
    /// INVARIANT: All items with non-empty IDs are included exactly once.
    /// </summary>
    public static Dictionary<string, AlgorithmItem> BuildItemLookup(IEnumerable<AlgorithmItem> items)
    {
        var lookup = new Dictionary<string, AlgorithmItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Id) && !lookup.ContainsKey(item.Id))
            {
                lookup[item.Id] = item;
            }
        }
        return lookup;
    }

    /// <summary>
    /// Traces an item up to its root equipment slot.
    ///
    /// INVARIANTS:
    /// 1. Terminates within MaxParentTraversalDepth iterations
    /// 2. Detects and handles cycles
    /// 3. Returns null if item is not under Equipment container
    /// </summary>
    /// <param name="item">The item to trace</param>
    /// <param name="equipmentId">The Equipment container ID</param>
    /// <param name="lookup">O(1) item lookup dictionary</param>
    /// <returns>The root slot ID (e.g., "Backpack"), or null</returns>
    public static string? TraceRootSlot(
        AlgorithmItem item,
        string? equipmentId,
        IReadOnlyDictionary<string, AlgorithmItem> lookup)
    {
        var currentItem = item;
        int depth = 0;
        var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (currentItem != null && depth < AlgorithmConstants.MaxParentTraversalDepth)
        {
            // Cycle detection
            if (!string.IsNullOrEmpty(currentItem.Id))
            {
                if (visitedIds.Contains(currentItem.Id))
                {
                    // INVARIANT VIOLATION: Cycle detected
                    return null;
                }
                visitedIds.Add(currentItem.Id);
            }

            // Check if we've reached Equipment
            if (currentItem.ParentId == equipmentId)
            {
                Debug.Assert(!string.IsNullOrEmpty(currentItem.SlotId),
                    "Items directly under Equipment must have a SlotId");
                return currentItem.SlotId;
            }

            // Move to parent
            if (string.IsNullOrEmpty(currentItem.ParentId))
            {
                break;
            }

            if (!lookup.TryGetValue(currentItem.ParentId, out var parent))
            {
                break; // Parent not found - orphaned item
            }

            currentItem = parent;
            depth++;
        }

        // INVARIANT: If we hit max depth, something is wrong with the data
        Debug.Assert(depth < AlgorithmConstants.MaxParentTraversalDepth,
            $"Max traversal depth reached for item {item.Id}");

        return null;
    }

    /// <summary>
    /// Builds a map of item IDs to their root equipment slot.
    /// INVARIANT: All items traceable to Equipment have an entry.
    /// </summary>
    public static Dictionary<string, string> BuildRootSlotMap(
        IEnumerable<AlgorithmItem> items,
        string? equipmentId,
        IReadOnlyDictionary<string, AlgorithmItem> lookup)
    {
        var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Id)) continue;

            string? rootSlot = TraceRootSlot(item, equipmentId, lookup);
            if (!string.IsNullOrEmpty(rootSlot))
            {
                slotMap[item.Id] = rootSlot;
            }
        }

        return slotMap;
    }

    // ========================================================================
    // INVARIANT: Slot management rules
    // ========================================================================

    /// <summary>
    /// Determines if a slot is managed by the mod.
    ///
    /// INVARIANT: SecuredContainer is NEVER managed for removal.
    ///
    /// Rules:
    /// - null includedSlotIds = legacy format, check snapshotSlotIds OR emptySlotIds
    /// - empty includedSlotIds = user chose nothing, return false (normal death)
    /// - populated includedSlotIds = check membership
    /// </summary>
    public static bool IsSlotManaged(
        string slotId,
        HashSet<string>? includedSlotIds,
        HashSet<string> snapshotSlotIds,
        HashSet<string> emptySlotIds)
    {
        // INVARIANT: SecuredContainer is NEVER managed for removal
        if (string.Equals(slotId, AlgorithmConstants.SecuredContainerSlot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Modern format: includedSlotIds is authoritative
        if (includedSlotIds != null)
        {
            // Empty set = user explicitly selected no slots = normal death
            if (includedSlotIds.Count == 0)
            {
                return false;
            }
            return includedSlotIds.Contains(slotId);
        }

        // Legacy format: check snapshotSlotIds and emptySlotIds
        return snapshotSlotIds.Contains(slotId) || emptySlotIds.Contains(slotId);
    }

    /// <summary>
    /// Collects all items to remove from managed slots.
    /// Uses BFS to find all nested items.
    ///
    /// INVARIANT: Equipment container itself is never in the result.
    /// INVARIANT: SecuredContainer slot items are never in the result.
    /// </summary>
    public static HashSet<string> CollectItemsToRemove(
        List<AlgorithmItem> items,
        HashSet<string> allEquipmentIds,
        SlotSets slotSets)
    {
        var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find direct children of Equipment that are in managed slots
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.ParentId) || !allEquipmentIds.Contains(item.ParentId))
                continue;

            if (string.IsNullOrEmpty(item.Id))
                continue;

            var slotId = item.SlotId ?? "";

            // INVARIANT: Never remove SecuredContainer
            if (string.Equals(slotId, AlgorithmConstants.SecuredContainerSlot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsSlotManaged(slotId, slotSets.IncludedSlotIds, slotSets.SnapshotSlotIds, slotSets.EmptySlotIds))
            {
                toRemove.Add(item.Id);
            }
        }

        // BFS to find all nested items
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.ParentId))
                continue;

            if (!childrenByParent.TryGetValue(item.ParentId, out var children))
            {
                children = new List<string>();
                childrenByParent[item.ParentId] = children;
            }
            children.Add(item.Id);
        }

        var queue = new Queue<string>(toRemove);
        int processedCount = 0;
        const int maxProcessed = 10000;

        while (queue.Count > 0 && processedCount < maxProcessed)
        {
            string parentId = queue.Dequeue();
            processedCount++;

            if (childrenByParent.TryGetValue(parentId, out var children))
            {
                foreach (var childId in children)
                {
                    if (!toRemove.Contains(childId))
                    {
                        toRemove.Add(childId);
                        queue.Enqueue(childId);
                    }
                }
            }
        }

        Debug.Assert(processedCount < maxProcessed, "Max items processed - possible corrupt hierarchy");

        return toRemove;
    }

    // ========================================================================
    // FULL RESTORATION SIMULATION
    // ========================================================================

    /// <summary>
    /// Simulates the full restoration algorithm.
    /// This is the main entry point for testing.
    /// </summary>
    public static RestorationResult SimulateRestoration(
        List<AlgorithmItem> profileItems,
        List<AlgorithmItem> snapshotItems,
        IReadOnlyList<string>? includedSlots,
        IReadOnlyList<string>? emptySlots)
    {
        // Find Equipment containers
        var profileEquipmentId = FindEquipmentContainerId(profileItems);
        var snapshotEquipmentId = FindEquipmentContainerId(snapshotItems);

        if (string.IsNullOrEmpty(profileEquipmentId))
        {
            return RestorationResult.Failed("No Equipment container in profile");
        }

        // Build slot sets
        var slotSets = BuildSlotSets(snapshotItems, includedSlots, emptySlots, snapshotEquipmentId);

        // Find all Equipment IDs
        var allEquipmentIds = FindAllEquipmentContainerIds(profileItems);
        if (!string.IsNullOrEmpty(profileEquipmentId))
            allEquipmentIds.Add(profileEquipmentId);

        // Collect items to remove
        var toRemove = CollectItemsToRemove(profileItems, allEquipmentIds, slotSets);
        int removedCount = toRemove.Count;

        // Remove items (simulated)
        profileItems.RemoveAll(item => !string.IsNullOrEmpty(item.Id) && toRemove.Contains(item.Id));

        // Build lookup for existing items
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in profileItems)
        {
            if (!string.IsNullOrEmpty(item.Id))
                existingIds.Add(item.Id);
        }

        // Build root slot map for snapshot items
        var snapshotLookup = BuildItemLookup(snapshotItems);
        var snapshotSlotMap = BuildRootSlotMap(snapshotItems, snapshotEquipmentId, snapshotLookup);

        // Add snapshot items
        int addedCount = 0;
        int dupeCount = 0;
        int nonManagedCount = 0;

        foreach (var item in snapshotItems)
        {
            if (string.IsNullOrEmpty(item.Id))
                continue;

            // Skip Equipment container
            if (item.Tpl == AlgorithmConstants.EquipmentTemplateId)
                continue;

            // Skip duplicates
            if (existingIds.Contains(item.Id))
            {
                dupeCount++;
                continue;
            }

            // Check if item is from a managed slot
            if (snapshotSlotMap.TryGetValue(item.Id, out var rootSlot))
            {
                if (!IsSlotManaged(rootSlot, slotSets.IncludedSlotIds, slotSets.SnapshotSlotIds, slotSets.EmptySlotIds))
                {
                    nonManagedCount++;
                    continue;
                }
            }

            // Remap parent ID if it was the snapshot's Equipment container
            var newItem = new AlgorithmItem
            {
                Id = item.Id,
                Tpl = item.Tpl,
                ParentId = item.ParentId == snapshotEquipmentId ? profileEquipmentId : item.ParentId,
                SlotId = item.SlotId
            };

            profileItems.Add(newItem);
            existingIds.Add(item.Id);
            addedCount++;
        }

        return RestorationResult.Succeeded(addedCount, removedCount, dupeCount, nonManagedCount, slotSets.IncludedSlotIds);
    }
}
