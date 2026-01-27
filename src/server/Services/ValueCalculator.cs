// ============================================================================
// Keep Starting Gear - Value Calculator Service
// ============================================================================
// FEATURE 7: Snapshot Value Calculator
//
// Calculates and displays the estimated ruble value of protected gear.
// Helps players understand the value of what they're protecting.
//
// VALUE SOURCES:
// - Item template prices from game data
// - Flea market average prices (if available)
// - Trader buy prices as fallback
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Blackhorse311.KeepStartingGear.Models;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service for calculating item and snapshot values.
/// </summary>
public class ValueCalculator
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static ValueCalculator Instance { get; private set; }

    // ========================================================================
    // State
    // ========================================================================

    /// <summary>Cached item values by template ID.</summary>
    private readonly Dictionary<string, long> _valueCache = new(StringComparer.OrdinalIgnoreCase);

    // ========================================================================
    // Constructor
    // ========================================================================

    public ValueCalculator()
    {
        Instance = this;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Calculates the total value of a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot to calculate value for</param>
    /// <returns>Value summary with breakdown</returns>
    public ValueSummary CalculateSnapshotValue(InventorySnapshot snapshot)
    {
        var summary = new ValueSummary();

        if (snapshot?.Items == null)
            return summary;

        try
        {
            foreach (var item in snapshot.Items)
            {
                // Skip container items
                if (item.Tpl == "55d7217a4bdc2d86028b456d") // Equipment
                    continue;

                long itemValue = GetItemValue(item.Tpl);
                int stackCount = (int)(item.Upd?.StackObjectsCount ?? 1);
                long totalValue = itemValue * stackCount;

                summary.TotalValue += totalValue;
                summary.ItemCount++;

                // Track by category (simplified)
                string category = GetItemCategory(item.SlotId);
                if (!summary.ValueByCategory.ContainsKey(category))
                    summary.ValueByCategory[category] = 0;
                summary.ValueByCategory[category] += totalValue;

                // Track highest value items
                if (totalValue > 0)
                {
                    summary.TopItems.Add(new ValuedItem
                    {
                        TemplateId = item.Tpl,
                        Name = item.Tpl, // Will be resolved later if possible
                        Value = totalValue,
                        StackCount = stackCount
                    });
                }
            }

            // Sort top items by value and keep top 5
            summary.TopItems = summary.TopItems
                .OrderByDescending(i => i.Value)
                .Take(5)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ValueCalculator] Error calculating snapshot value: {ex.Message}");
        }

        return summary;
    }

    /// <summary>
    /// Calculates the value of current inventory items.
    /// </summary>
    public ValueSummary CalculateCurrentInventoryValue()
    {
        var summary = new ValueSummary();

        try
        {
            var items = InventoryService.Instance?.GetCurrentInventoryItems();
            if (items == null)
                return summary;

            foreach (var item in items)
            {
                long itemValue = GetItemValue(item.Tpl);
                long totalValue = itemValue * item.StackCount;

                summary.TotalValue += totalValue;
                summary.ItemCount++;

                if (totalValue > 0)
                {
                    summary.TopItems.Add(new ValuedItem
                    {
                        TemplateId = item.Tpl,
                        Name = item.ShortName,
                        Value = totalValue,
                        StackCount = item.StackCount
                    });
                }
            }

            summary.TopItems = summary.TopItems
                .OrderByDescending(i => i.Value)
                .Take(5)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ValueCalculator] Error calculating inventory value: {ex.Message}");
        }

        return summary;
    }

    /// <summary>
    /// Gets the value difference between current inventory and snapshot.
    /// Positive = current inventory worth more (picked up valuable loot).
    /// </summary>
    public long GetValueDifference(string sessionId)
    {
        try
        {
            var snapshot = SnapshotManager.Instance?.LoadSnapshot(sessionId);
            if (snapshot == null)
                return 0;

            var snapshotValue = CalculateSnapshotValue(snapshot);
            var currentValue = CalculateCurrentInventoryValue();

            return currentValue.TotalValue - snapshotValue.TotalValue;
        }
        catch
        {
            return 0;
        }
    }

    // ========================================================================
    // Value Lookup
    // ========================================================================

    /// <summary>
    /// Gets the estimated value of an item by template ID.
    /// Uses caching for performance.
    /// </summary>
    public long GetItemValue(string templateId)
    {
        if (string.IsNullOrEmpty(templateId))
            return 0;

        // Check cache first
        if (_valueCache.TryGetValue(templateId, out long cachedValue))
            return cachedValue;

        long value = LookupItemValue(templateId);
        _valueCache[templateId] = value;

        return value;
    }

    /// <summary>
    /// Looks up item value from game data.
    /// Uses ItemTemplates dictionary to avoid creating unnecessary item instances.
    /// </summary>
    private long LookupItemValue(string templateId)
    {
        try
        {
            // Try to get from game's item database
            var itemFactory = Singleton<ItemFactoryClass>.Instance;
            if (itemFactory == null)
                return 0;

            // Try to access ItemTemplates dictionary directly (avoids creating items)
            var templatesProperty = itemFactory.GetType().GetProperty("ItemTemplates");
            if (templatesProperty != null)
            {
                var templates = templatesProperty.GetValue(itemFactory);
                if (templates is IDictionary<string, ItemTemplate> templateDict)
                {
                    if (templateDict.TryGetValue(templateId, out var template))
                    {
                        return GetTemplateCreditPrice(template);
                    }
                }
                // Try as generic dictionary via reflection
                else if (templates != null)
                {
                    var tryGetMethod = templates.GetType().GetMethod("TryGetValue");
                    if (tryGetMethod != null)
                    {
                        // NEW-003: Wrap reflection invoke in try-catch and validate result
                        try
                        {
                            var parameters = new object[] { templateId, null };
                            var result = tryGetMethod.Invoke(templates, parameters);
                            var found = result is bool b && b;
                            if (found && parameters[1] is ItemTemplate template)
                            {
                                return GetTemplateCreditPrice(template);
                            }
                        }
                        catch (TargetInvocationException)
                        {
                            // Reflection target threw - ignore and return default
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // NEW-008: Log at warning level for reflection failures
            Plugin.Log.LogWarning($"[ValueCalculator] Failed to lookup template {templateId}: {ex.Message}");
        }

        // Return 0 if we can't determine value (intentional default for unknown items)
        return 0;
    }

    /// <summary>
    /// Gets the CreditsPrice from an ItemTemplate using reflection.
    /// </summary>
    private long GetTemplateCreditPrice(ItemTemplate template)
    {
        if (template == null)
            return 0;

        try
        {
            var priceProperty = template.GetType().GetProperty("CreditsPrice");
            if (priceProperty != null)
            {
                var price = priceProperty.GetValue(template);
                if (price is int intPrice)
                    return intPrice;
                if (price is long longPrice)
                    return longPrice;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Caches an item value (called when items are examined in-game).
    /// </summary>
    public void CacheItemValue(string templateId, long value)
    {
        if (!string.IsNullOrEmpty(templateId) && value > 0)
        {
            _valueCache[templateId] = value;
        }
    }

    /// <summary>
    /// Clears the value cache (call on raid start for fresh data).
    /// </summary>
    public void ClearCache()
    {
        _valueCache.Clear();
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    /// <summary>
    /// Gets a simplified category name for a slot.
    /// </summary>
    private string GetItemCategory(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return "Other";

        return slotId.ToLowerInvariant() switch
        {
            "firstprimaryweapon" or "secondprimaryweapon" or "holster" => "Weapons",
            "headwear" or "earpiece" or "facecover" or "eyewear" => "Head Gear",
            "tacticalvest" or "armorvest" => "Armor",
            "backpack" => "Backpack",
            "pockets" => "Pockets",
            "securedcontainer" => "Secure",
            "scabbard" => "Melee",
            _ => "Other"
        };
    }
}

/// <summary>
/// Summary of calculated values.
/// </summary>
public class ValueSummary
{
    /// <summary>Total value in rubles.</summary>
    public long TotalValue { get; set; }

    /// <summary>Number of items counted.</summary>
    public int ItemCount { get; set; }

    /// <summary>Value breakdown by category.</summary>
    public Dictionary<string, long> ValueByCategory { get; set; } = new();

    /// <summary>Top 5 most valuable items.</summary>
    public List<ValuedItem> TopItems { get; set; } = new();

    /// <summary>Formatted total value string.</summary>
    public string FormattedValue => FormatRubles(TotalValue);

    /// <summary>
    /// Formats a ruble value with thousand separators.
    /// </summary>
    public static string FormatRubles(long value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000.0:F1}M ₽";
        if (value >= 1_000)
            return $"{value / 1_000.0:F0}K ₽";
        return $"{value:N0} ₽";
    }
}

/// <summary>
/// An item with its calculated value.
/// </summary>
public class ValuedItem
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Value { get; set; }
    public int StackCount { get; set; } = 1;

    public string FormattedValue => ValueSummary.FormatRubles(Value);
}
