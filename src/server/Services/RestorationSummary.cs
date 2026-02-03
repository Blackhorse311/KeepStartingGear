// ============================================================================
// Keep Starting Gear - Restoration Summary Service
// ============================================================================
// Tracks what items were restored vs lost when gear protection activates.
// Provides detailed information for the post-death summary screen.
//
// FEATURE 1: Post-Death Summary Screen
// Shows players exactly what happened after they died:
// - Which items were restored (from snapshot)
// - Which items were lost (picked up during raid)
// - Total value estimates
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Blackhorse311.KeepStartingGear.Constants;
using Blackhorse311.KeepStartingGear.Models;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Represents a summary of what happened during gear restoration.
/// </summary>
public class RestorationSummaryData
{
    /// <summary>Items that were restored from the snapshot.</summary>
    public List<ItemSummary> RestoredItems { get; set; } = new();

    /// <summary>Items that were lost (picked up after snapshot was taken).</summary>
    public List<ItemSummary> LostItems { get; set; } = new();

    /// <summary>Timestamp when restoration occurred.</summary>
    public DateTime RestorationTime { get; set; } = DateTime.UtcNow;

    /// <summary>Map where the death occurred.</summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>Whether restoration was successful.</summary>
    public bool WasSuccessful { get; set; }

    /// <summary>Error message if restoration failed.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Total count of restored items.</summary>
    public int RestoredCount => RestoredItems.Count;

    /// <summary>Total count of lost items.</summary>
    public int LostCount => LostItems.Count;
}

/// <summary>
/// Simplified item information for display in summaries.
/// </summary>
public class ItemSummary
{
    /// <summary>Item template ID for lookup.</summary>
    public string TemplateId { get; set; }

    /// <summary>Human-readable item name.</summary>
    public string Name { get; set; }

    /// <summary>Short name for compact display.</summary>
    public string ShortName { get; set; }

    /// <summary>Stack count (for stackable items).</summary>
    public int Count { get; set; } = 1;

    /// <summary>Whether this item had Found-in-Raid status.</summary>
    public bool WasFoundInRaid { get; set; }

    /// <summary>Estimated value in rubles (if available).</summary>
    public long EstimatedValue { get; set; }

    /// <summary>Slot this item was in (for equipment).</summary>
    public string SlotName { get; set; }
}

/// <summary>
/// Service that generates and manages restoration summaries.
/// Singleton pattern for global access.
/// </summary>
public static class RestorationSummaryService
{
    // ========================================================================
    // State
    // ========================================================================

    /// <summary>
    /// The most recent restoration summary.
    /// Cleared after being displayed to prevent stale data.
    /// </summary>
    private static RestorationSummaryData _lastSummary;

    /// <summary>
    /// Lock object for thread safety.
    /// </summary>
    private static readonly object _lock = new();

    /// <summary>
    /// Cache of item names by template ID.
    /// Populated during gameplay to avoid repeated lookups.
    /// </summary>
    private static readonly Dictionary<string, (string Name, string ShortName)> _itemNameCache = new();

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Gets the most recent restoration summary, if available.
    /// Returns null if no summary is pending display.
    /// </summary>
    public static RestorationSummaryData GetPendingSummary()
    {
        lock (_lock)
        {
            return _lastSummary;
        }
    }

    /// <summary>
    /// Clears the pending summary after it has been displayed.
    /// </summary>
    public static void ClearPendingSummary()
    {
        lock (_lock)
        {
            _lastSummary = null;
        }
    }

    /// <summary>
    /// Checks if there's a summary waiting to be displayed.
    /// </summary>
    public static bool HasPendingSummary()
    {
        lock (_lock)
        {
            return _lastSummary != null;
        }
    }

    /// <summary>
    /// Queues a summary for display (used by file watcher).
    /// </summary>
    public static void QueueSummaryForDisplay(RestorationSummaryData summary)
    {
        lock (_lock)
        {
            _lastSummary = summary;
        }
    }

    /// <summary>
    /// Generates a restoration summary by comparing snapshot to current inventory.
    /// Called after successful restoration.
    /// </summary>
    /// <param name="snapshot">The snapshot that was used for restoration</param>
    /// <param name="preDeathItems">Items the player had before restoration (at death)</param>
    /// <param name="mapName">Name of the map where death occurred</param>
    public static void GenerateSummary(
        InventorySnapshot snapshot,
        List<SerializedItem> preDeathItems,
        string mapName)
    {
        lock (_lock)
        {
            try
            {
                var summary = new RestorationSummaryData
                {
                    MapName = mapName ?? "Unknown",
                    WasSuccessful = true,
                    RestorationTime = DateTime.UtcNow
                };

                // Build sets of item IDs for comparison
                var snapshotItemIds = new HashSet<string>(
                    snapshot.Items
                        .Where(i => !string.IsNullOrEmpty(i.Id))
                        .Select(i => i.Id),
                    StringComparer.OrdinalIgnoreCase);

                var preDeathItemIds = new HashSet<string>(
                    preDeathItems
                        .Where(i => !string.IsNullOrEmpty(i.Id))
                        .Select(i => i.Id),
                    StringComparer.OrdinalIgnoreCase);

                // Restored items = items in snapshot (these were restored)
                foreach (var item in snapshot.Items.Where(i => !string.IsNullOrEmpty(i.Tpl)))
                {
                    // Skip container items (Equipment, etc.)
                    if (IsContainerTemplate(item.Tpl))
                        continue;

                    var itemSummary = CreateItemSummary(item);
                    if (itemSummary != null)
                    {
                        summary.RestoredItems.Add(itemSummary);
                    }
                }

                // Lost items = items in pre-death inventory that weren't in snapshot
                foreach (var item in preDeathItems.Where(i => !string.IsNullOrEmpty(i.Tpl)))
                {
                    // Skip if this item was in the snapshot (it was restored)
                    if (snapshotItemIds.Contains(item.Id))
                        continue;

                    // Skip container items
                    if (IsContainerTemplate(item.Tpl))
                        continue;

                    var itemSummary = CreateItemSummary(item);
                    if (itemSummary != null)
                    {
                        summary.LostItems.Add(itemSummary);
                    }
                }

                // Deduplicate and consolidate stacks
                summary.RestoredItems = ConsolidateItems(summary.RestoredItems);
                summary.LostItems = ConsolidateItems(summary.LostItems);

                _lastSummary = summary;

                Plugin.Log.LogDebug($"[Summary] Generated: {summary.RestoredCount} restored, {summary.LostCount} lost");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Summary] Failed to generate summary: {ex.Message}");

                // Create a minimal summary indicating error
                _lastSummary = new RestorationSummaryData
                {
                    MapName = mapName ?? "Unknown",
                    WasSuccessful = true, // Restoration worked, just summary failed
                    ErrorMessage = "Could not generate detailed summary"
                };
            }
        }
    }

    /// <summary>
    /// Records a failed restoration attempt.
    /// </summary>
    public static void RecordFailure(string errorMessage, string mapName)
    {
        lock (_lock)
        {
            _lastSummary = new RestorationSummaryData
            {
                MapName = mapName ?? "Unknown",
                WasSuccessful = false,
                ErrorMessage = errorMessage ?? "Unknown error"
            };
        }
    }

    /// <summary>
    /// Caches an item name for later use in summaries.
    /// Call this when items are examined or displayed in-game.
    /// </summary>
    public static void CacheItemName(string templateId, string name, string shortName)
    {
        if (string.IsNullOrEmpty(templateId))
            return;

        lock (_lock)
        {
            _itemNameCache[templateId] = (name ?? "Unknown", shortName ?? name ?? "???");
        }
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    /// <summary>
    /// Creates an ItemSummary from a SerializedItem.
    /// </summary>
    private static ItemSummary CreateItemSummary(SerializedItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.Tpl))
            return null;

        var (name, shortName) = GetItemName(item.Tpl);

        return new ItemSummary
        {
            TemplateId = item.Tpl,
            Name = name,
            ShortName = shortName,
            Count = (int)(item.Upd?.StackObjectsCount ?? 1),
            WasFoundInRaid = item.Upd?.SpawnedInSession ?? false,
            SlotName = item.SlotId ?? ""
        };
    }

    /// <summary>
    /// Gets the cached name for an item, or a placeholder if not cached.
    /// </summary>
    private static (string Name, string ShortName) GetItemName(string templateId)
    {
        lock (_lock)
        {
            if (_itemNameCache.TryGetValue(templateId, out var cached))
            {
                return cached;
            }
        }

        // Return template ID truncated as placeholder
        // Real name will be resolved client-side if possible
        string shortTpl = templateId.Length > 8 ? templateId.Substring(0, 8) + "..." : templateId;
        return ($"Item ({shortTpl})", shortTpl);
    }

    /// <summary>
    /// Checks if a template ID is a container (Equipment, Stash, etc.).
    /// These should be excluded from summaries.
    /// </summary>
    private static bool IsContainerTemplate(string templateId)
    {
        // M-01 FIX: use constant for Equipment container template
        if (templateId == TemplateIds.Equipment)
            return true;

        // Add other container templates as needed
        return false;
    }

    /// <summary>
    /// Consolidates duplicate items by template ID, summing stack counts.
    /// </summary>
    private static List<ItemSummary> ConsolidateItems(List<ItemSummary> items)
    {
        return items
            .GroupBy(i => i.TemplateId)
            .Select(g => new ItemSummary
            {
                TemplateId = g.Key,
                Name = g.First().Name,
                ShortName = g.First().ShortName,
                Count = g.Sum(i => i.Count),
                WasFoundInRaid = g.Any(i => i.WasFoundInRaid),
                SlotName = g.First().SlotName,
                EstimatedValue = g.Sum(i => i.EstimatedValue)
            })
            .OrderByDescending(i => i.WasFoundInRaid) // FiR items first (these are the "painful" losses)
            .ThenByDescending(i => i.Count)
            .ToList();
    }
}
