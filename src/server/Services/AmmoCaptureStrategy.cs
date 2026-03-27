// ============================================================================
// Keep Starting Gear - Ammo Capture Strategy
// ============================================================================
// Handles capture of ammunition from ammo boxes and magazines.
// Extracted from InventoryService (CRIT-1 god class refactor).
//
// KEY RESPONSIBILITIES:
// 1. Capture ammo from ammo boxes via the StackSlot mechanism
// 2. Capture cartridges from magazines with correct positional indices
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Models;
using EFT.InventoryLogic;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Captures ammunition from ammo boxes and magazines.
/// Extracted from InventoryService to isolate ammo-specific capture logic.
/// </summary>
internal class AmmoCaptureStrategy
{
    // ========================================================================
    // Dependencies
    // ========================================================================

    private readonly ItemSerializer _serializer;

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Initializes the strategy with the serializer used to convert items.
    /// </summary>
    /// <param name="serializer">The item serializer for converting EFT Items</param>
    public AmmoCaptureStrategy(ItemSerializer serializer)
    {
        _serializer = serializer;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Captures ammo from ammo boxes (via StackSlot).
    /// </summary>
    /// <returns>Number of ammo items captured</returns>
    public int CaptureAmmoBoxContents(
        Item containerItem,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        bool verbose)
    {
        var itemType = containerItem.GetType();
        var tplStr = containerItem.TemplateId.ToString();

        // Check if this might be an ammo box.
        // "5737" is the common EFT template ID prefix for ammo boxes (e.g., 57372b832459776701014e41).
        // Also check type name as a fallback for modded ammo containers.
        if (!tplStr.StartsWith("5737") && !itemType.Name.Contains("Ammo") && !itemType.Name.Contains("Box"))
            return 0;

        if (verbose)
        {
            Plugin.Log.LogDebug($"[AMMO BOX] Checking potential ammo box: {containerItem.TemplateId} Type: {itemType.Name}");
            var allProps = itemType.GetProperties();
            var propNames = new string[allProps.Length];
            for (int i = 0; i < allProps.Length; i++) propNames[i] = allProps[i].Name;

            var allFields = itemType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var fieldNames = new string[allFields.Length];
            for (int i = 0; i < allFields.Length; i++) fieldNames[i] = allFields[i].Name;

            Plugin.Log.LogDebug($"[AMMO BOX] Properties: {string.Join(", ", propNames)}");
            Plugin.Log.LogDebug($"[AMMO BOX] Fields: {string.Join(", ", fieldNames)}");
        }

        var stackSlotProp = itemType.GetProperty("StackSlot");
        if (stackSlotProp == null)
            return 0;

        var stackSlot = stackSlotProp.GetValue(containerItem);
        if (stackSlot == null)
            return 0;

        if (verbose) Plugin.Log.LogDebug($"[AMMO BOX] Found StackSlot: {stackSlot.GetType().Name}");

        var itemsProp = stackSlot.GetType().GetProperty("Items");
        if (itemsProp == null)
            return 0;

        var items = itemsProp.GetValue(stackSlot) as IEnumerable<Item>;
        if (items == null)
            return 0;

        int found = 0;
        foreach (var ammoItem in Enumerable.ToList(items))
        {
            if (capturedItemIds.Contains(ammoItem.Id))
                continue;

            var serializedItem = _serializer.Convert(ammoItem);
            if (serializedItem != null)
            {
                allItems.Add(serializedItem);
                capturedItemIds.Add(ammoItem.Id);
                found++;

                if (verbose)
                    Plugin.Log.LogDebug($"[AMMO BOX] Captured ammo from box: {ammoItem.TemplateId} Stack={ammoItem.StackObjectsCount}");
            }
        }

        return found;
    }

    /// <summary>
    /// Captures cartridges from magazines and ammo boxes.
    /// </summary>
    /// <returns>Number of cartridges captured</returns>
    public int CaptureCartridges(
        Item containerItem,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        bool verbose)
    {
        var itemType = containerItem.GetType();
        var cartridgesProp = itemType.GetProperty("Cartridges");
        if (cartridgesProp == null)
            return 0;

        var cartridges = cartridgesProp.GetValue(containerItem);
        if (cartridges == null)
            return 0;

        if (verbose)
            Plugin.Log.LogDebug($"[CARTRIDGES] Found Cartridges on {itemType.Name}: {cartridges.GetType().Name}");

        var itemsProp = cartridges.GetType().GetProperty("Items");
        if (itemsProp == null)
        {
            if (verbose)
            {
                var availableProps = cartridges.GetType().GetProperties();
                var propNames = new string[System.Math.Min(availableProps.Length, 10)];
                for (int i = 0; i < propNames.Length; i++) propNames[i] = availableProps[i].Name;
                Plugin.Log.LogDebug($"[CARTRIDGES] No Items property on Cartridges. Available: {string.Join(", ", propNames)}");
            }
            return 0;
        }

        var ammoItems = itemsProp.GetValue(cartridges) as IEnumerable<Item>;
        if (ammoItems == null)
            return 0;

        int found = 0;
        int cartridgePosition = 0;

        // Snapshot the collection to avoid InvalidOperationException if modified during iteration
        foreach (var ammo in Enumerable.ToList(ammoItems))
        {
            if (ammo != null && !capturedItemIds.Contains(ammo.Id))
            {
                var serializedItem = _serializer.Convert(ammo);
                if (serializedItem != null)
                {
                    // CRITICAL: Set LocationIndex for magazine cartridges
                    serializedItem.LocationIndex = cartridgePosition;

                    allItems.Add(serializedItem);
                    capturedItemIds.Add(ammo.Id);
                    found++;

                    if (verbose)
                        Plugin.Log.LogDebug($"[CARTRIDGES] Captured ammo from {itemType.Name}: {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Stack: {ammo.StackObjectsCount}, Position: {cartridgePosition})");
                }
            }
            cartridgePosition++;
        }

        if (verbose)
            Plugin.Log.LogDebug($"[CARTRIDGES] {itemType.Name} contained {cartridgePosition} ammo items with positions 0-{cartridgePosition - 1}");

        return found;
    }
}
