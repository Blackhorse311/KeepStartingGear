// ============================================================================
// Keep Starting Gear - Grid Capture Strategy
// ============================================================================
// Captures items from container grids (backpacks, rigs, pockets) and nested
// slots on grid items (armor plates in backpack armor).
// Extracted from InventoryService (CRIT-1 god class refactor).
//
// KEY RESPONSIBILITIES:
// 1. Multi-pass grid capture from containers
// 2. ItemCollection-based capture (with location data)
// 3. Items-property fallback capture (no location data)
// 4. Nested slot capture for grid items (armor plates, etc.)
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
/// Captures items from container grids and nested slots within those grids.
/// Extracted from InventoryService to isolate grid traversal complexity.
/// </summary>
internal class GridCaptureStrategy
{
    // ========================================================================
    // Dependencies
    // ========================================================================

    private readonly ItemSerializer _serializer;
    private readonly AmmoCaptureStrategy _ammoCapture;

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Initializes the strategy with its required dependencies.
    /// </summary>
    /// <param name="serializer">The item serializer for converting EFT Items</param>
    /// <param name="ammoCapture">The ammo capture strategy for magazines and ammo boxes</param>
    public GridCaptureStrategy(ItemSerializer serializer, AmmoCaptureStrategy ammoCapture)
    {
        _serializer = serializer;
        _ammoCapture = ammoCapture;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Captures grid contents (backpack items, rig items, magazine ammo, etc.).
    /// </summary>
    /// <returns>The number of grid items found.</returns>
    public int CaptureGridContents(
        List<(string SlotName, Item Item)> slotItems,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds)
    {
        bool verbose = Settings.VerboseCaptureLogging?.Value == true;

        if (verbose)
            Plugin.Log.LogDebug("Fourth pass: Capturing grid contents from containers...");

        // Build a list of items we need to check for grids
        var itemsToCheck = new List<Item>();
        foreach (var (slotName, item) in slotItems)
        {
            if (capturedItemIds.Contains(item.Id))
            {
                itemsToCheck.Add(item);
            }
        }

        if (verbose)
            Plugin.Log.LogDebug($"Checking {itemsToCheck.Count} captured items for grid contents...");

        int gridItemsFound = 0;
        bool foundMore = true;
        int gridPass = 0;

        while (foundMore && gridPass < 10)
        {
            foundMore = false;
            gridPass++;
            var newItemsToCheck = new List<Item>();

            foreach (var containerItem in itemsToCheck)
            {
                if (containerItem == null) continue;

                // Capture items from grids (backpacks, rigs, pockets)
                int found = CaptureItemsFromGrids(containerItem, allItems, capturedItemIds, newItemsToCheck, gridPass, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }

                // Capture ammo from ammo boxes
                found = _ammoCapture.CaptureAmmoBoxContents(containerItem, allItems, capturedItemIds, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }

                // Capture cartridges from magazines
                found = _ammoCapture.CaptureCartridges(containerItem, allItems, capturedItemIds, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }
            }

            // Check for slots on grid items (armor plates in backpack armor)
            foreach (var gridItem in Enumerable.ToList(newItemsToCheck))
            {
                int found = CaptureGridItemSlots(gridItem, allItems, capturedItemIds, newItemsToCheck, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }
            }

            // Add newly found items to check in next pass
            itemsToCheck = newItemsToCheck;
        }

        if (verbose)
            Plugin.Log.LogDebug($"Grid items captured: {gridItemsFound} in {gridPass} passes");

        return gridItemsFound;
    }

    // ========================================================================
    // Private Implementation
    // ========================================================================

    /// <summary>
    /// Captures items from a container's grids (backpacks, rigs, pockets).
    /// </summary>
    private int CaptureItemsFromGrids(
        Item containerItem,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        List<Item> newItemsToCheck,
        int gridPass,
        bool verbose)
    {
        int found = 0;
        var itemType = containerItem.GetType();

        if (gridPass == 1 && verbose)
            Plugin.Log.LogDebug($"[GRID DEBUG] Checking item: {containerItem.TemplateId} Type: {itemType.Name}");

        // Check for Grids field (in EFT, Grids is a PUBLIC FIELD)
        var gridsField = ReflectionCache.GetField(itemType, "Grids", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (gridsField == null)
        {
            if (gridPass == 1 && verbose)
            {
                var gridsProperty = itemType.GetProperty("Grids");
                if (gridsProperty != null)
                    Plugin.Log.LogDebug($"[GRID DEBUG]   Has Grids property (not field)");
            }
            return 0;
        }

        if (gridPass == 1 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]   Has Grids field");

        var grids = gridsField.GetValue(containerItem) as System.Collections.IEnumerable;
        if (grids == null)
        {
            if (gridPass == 1 && verbose)
                Plugin.Log.LogDebug($"[GRID DEBUG]   Grids field returned null");
            return 0;
        }

        int gridCount = 0;
        foreach (var grid in grids)
        {
            gridCount++;
            if (grid == null) continue;

            var gridType = grid.GetType();
            var gridIdProp = ReflectionCache.GetProperty(gridType, "ID");
            var gridId = gridIdProp?.GetValue(grid)?.ToString() ?? "unknown";

            if (gridPass == 1 && verbose)
                Plugin.Log.LogDebug($"[GRID DEBUG]   Grid {gridCount}: ID={gridId}, Type={gridType.Name}");

            // Try ItemCollection first (contains KeyValuePair<Item, LocationInGrid>)
            int gridFound = CaptureFromItemCollection(grid, gridType, containerItem, gridId, allItems, capturedItemIds, newItemsToCheck, gridPass, verbose);
            found += gridFound;

            // If no items found in THIS grid via ItemCollection, try Items property as fallback
            if (gridFound == 0)
            {
                found += CaptureFromItemsProperty(grid, gridType, containerItem, gridId, allItems, capturedItemIds, newItemsToCheck, gridPass, verbose);
            }
        }

        if (gridPass == 1 && gridCount == 0 && verbose)
            Plugin.Log.LogDebug($"[GRID DEBUG]   Grids collection is empty!");

        return found;
    }

    /// <summary>
    /// Captures items from a grid's ItemCollection (with location data).
    /// </summary>
    private int CaptureFromItemCollection(
        object grid,
        Type gridType,
        Item containerItem,
        string gridId,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        List<Item> newItemsToCheck,
        int gridPass,
        bool verbose)
    {
        var itemCollectionProp = ReflectionCache.GetProperty(gridType, "ItemCollection");
        var containedItemsProp = ReflectionCache.GetProperty(gridType, "ContainedItems");
        var collectionProp = itemCollectionProp ?? containedItemsProp;

        if (collectionProp == null)
            return 0;

        var collection = collectionProp.GetValue(grid) as System.Collections.IEnumerable;
        if (collection == null)
            return 0;

        int found = 0;
        int itemCount = 0;

        foreach (var kvp in collection)
        {
            itemCount++;
            var kvpType = kvp.GetType();
            var keyProp = ReflectionCache.GetProperty(kvpType, "Key");
            var valueProp = ReflectionCache.GetProperty(kvpType, "Value");

            if (keyProp == null || valueProp == null)
                continue;

            var childItem = keyProp.GetValue(kvp) as Item;
            var locationInGrid = valueProp.GetValue(kvp);

            if (childItem == null || capturedItemIds.Contains(childItem.Id))
                continue;

            var serializedItem = _serializer.Convert(childItem);
            if (serializedItem == null)
                continue;

            // Extract location from LocationInGrid
            if (locationInGrid != null)
            {
                var xVal = ReflectionCache.GetMemberValue(locationInGrid, "x");
                var yVal = ReflectionCache.GetMemberValue(locationInGrid, "y");
                var rVal = ReflectionCache.GetMemberValue(locationInGrid, "r");

                if (xVal != null && yVal != null)
                {
                    serializedItem.Location = new ItemLocation
                    {
                        X = System.Convert.ToInt32(xVal),
                        Y = System.Convert.ToInt32(yVal),
                        R = rVal != null ? System.Convert.ToInt32(rVal) : 0,
                        IsSearched = true
                    };
                    if (verbose)
                        Plugin.Log.LogDebug($"[LOCATION] Captured from ItemCollection: {childItem.TemplateId} at X={serializedItem.Location.X}, Y={serializedItem.Location.Y}, R={serializedItem.Location.R}");
                }
            }

            allItems.Add(serializedItem);
            capturedItemIds.Add(childItem.Id);
            newItemsToCheck.Add(childItem);
            found++;

            if (verbose)
                Plugin.Log.LogDebug($"[CAPTURE] Added grid item: {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Grid: {gridId})");
        }

        if (gridPass == 1 && verbose)
            Plugin.Log.LogDebug($"[GRID DEBUG]     Grid has {itemCount} items via ItemCollection");

        return found;
    }

    /// <summary>
    /// Captures items from a grid's Items property (fallback, no location data).
    /// </summary>
    private int CaptureFromItemsProperty(
        object grid,
        Type gridType,
        Item containerItem,
        string gridId,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        List<Item> newItemsToCheck,
        int gridPass,
        bool verbose)
    {
        var gridItemsProperty = gridType.GetProperty("Items");
        if (gridItemsProperty == null)
            return 0;

        var gridItems = gridItemsProperty.GetValue(grid) as System.Collections.IEnumerable;
        if (gridItems == null)
        {
            if (gridPass == 1 && verbose)
                Plugin.Log.LogDebug($"[GRID DEBUG]     Items property returned null");
            return 0;
        }

        int found = 0;
        int itemCount = 0;

        foreach (var gridItem in gridItems)
        {
            itemCount++;
            if (!(gridItem is Item childItem) || capturedItemIds.Contains(childItem.Id))
                continue;

            var serializedItem = _serializer.Convert(childItem);
            if (serializedItem == null)
                continue;

            allItems.Add(serializedItem);
            capturedItemIds.Add(childItem.Id);
            newItemsToCheck.Add(childItem);
            found++;

            if (verbose)
                Plugin.Log.LogDebug($"[CAPTURE] Added grid item (no location): {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Grid: {gridId})");
        }

        if (gridPass == 1 && verbose)
            Plugin.Log.LogDebug($"[GRID DEBUG]     Grid has {itemCount} items via Items property");

        return found;
    }

    /// <summary>
    /// Captures slot items from grid items (e.g., armor plates in backpack armor).
    /// </summary>
    private int CaptureGridItemSlots(
        Item gridItem,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        List<Item> newItemsToCheck,
        bool verbose)
    {
        if (gridItem == null)
            return 0;

        var gridItemType = gridItem.GetType();
        var gridItemSlotsProperty = gridItemType.GetProperty("AllSlots");
        if (gridItemSlotsProperty == null)
            return 0;

        var gridItemSlots = gridItemSlotsProperty.GetValue(gridItem) as System.Collections.IEnumerable;
        if (gridItemSlots == null)
            return 0;

        int found = 0;
        foreach (var gridSlot in gridItemSlots)
        {
            if (gridSlot == null) continue;

            var containedItemProp = gridSlot.GetType().GetProperty("ContainedItem");
            if (containedItemProp == null)
                continue;

            var slotItem = containedItemProp.GetValue(gridSlot) as Item;
            if (slotItem == null || capturedItemIds.Contains(slotItem.Id))
                continue;

            var slotIdProp = gridSlot.GetType().GetProperty("ID");
            var slotId = slotIdProp?.GetValue(gridSlot)?.ToString() ?? "unknown";

            var serializedSlotItem = _serializer.Convert(slotItem);
            if (serializedSlotItem != null)
            {
                allItems.Add(serializedSlotItem);
                capturedItemIds.Add(slotItem.Id);
                newItemsToCheck.Add(slotItem);
                found++;

                if (verbose)
                    Plugin.Log.LogDebug($"[CAPTURE] Added slot item from grid item: {serializedSlotItem.Tpl} (Slot: {slotId}, Parent: {gridItem.Id})");
            }
        }

        return found;
    }
}
