// ============================================================================
// Keep Starting Gear - Inventory Service
// ============================================================================
// This service handles all inventory capture and restoration operations.
// It is responsible for converting the player's equipment into a serializable
// format that can be saved to disk and later restored.
//
// KEY RESPONSIBILITIES:
// 1. Capture player's equipped items from all configured slots
// 2. Recursively capture nested items (items inside containers, weapon mods)
// 3. Handle special cases like magazine ammunition
// 4. Convert EFT Item objects to our SerializedItem format
// 5. Support inventory restoration from snapshots (legacy, server handles this now)
//
// ARCHITECTURE:
// The service uses reflection extensively to access EFT's internal structures
// because the exact property names and types may vary between game versions.
// This approach provides flexibility at the cost of some complexity.
//
// DATA FLOW:
// Player Input -> CaptureInventory() -> CaptureAllItems() -> CaptureItemRecursive()
//             -> ConvertToSerializedItem() -> InventorySnapshot -> JSON file
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Constants;
using Blackhorse311.KeepStartingGear.Models;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service for capturing and restoring player inventory state.
/// Uses the singleton pattern for global access from patches and components.
/// </summary>
/// <remarks>
/// <para>
/// This is the core service that handles the transformation between EFT's
/// runtime inventory representation and our serializable snapshot format.
/// </para>
/// <para>
/// <b>Capture Process:</b>
/// </para>
/// <list type="number">
///   <item>Get list of enabled slots from configuration</item>
///   <item>Iterate through player's equipment slots</item>
///   <item>For each enabled slot with an item, capture recursively</item>
///   <item>Handle special cases (magazines, nested containers)</item>
///   <item>Build InventorySnapshot with all captured items</item>
/// </list>
/// <para>
/// <b>Important:</b> The RestoreInventory method is legacy code kept for
/// compatibility. The server-side component now handles actual restoration
/// by modifying the profile JSON directly.
/// </para>
/// </remarks>
public class InventoryService
{
    // ========================================================================
    // Singleton Pattern
    // ========================================================================

    /// <summary>
    /// Singleton instance of the InventoryService.
    /// Set during construction and accessible from anywhere in the mod.
    /// </summary>
    public static InventoryService Instance { get; private set; }

    // ========================================================================
    // Capture State (set during capture, cleared after)
    // ========================================================================

    /// <summary>
    /// Set of insured item IDs for the current capture operation.
    /// This is populated at the start of CaptureInventory and cleared after.
    /// Used by ConvertToSerializedItem to check if items should be excluded.
    /// </summary>
    private HashSet<string> _currentCaptureInsuredIds = new HashSet<string>();

    /// <summary>
    /// Constructor - sets up the singleton instance.
    /// Called once during plugin initialization.
    /// </summary>
    public InventoryService()
    {
        Instance = this;
    }

    // ========================================================================
    // Public API - Inventory Capture
    // ========================================================================

    /// <summary>
    /// Creates a snapshot of the player's current inventory.
    /// This is the main entry point for inventory capture operations.
    /// </summary>
    /// <param name="player">The player whose inventory should be captured</param>
    /// <param name="location">The current map/location name (for logging and metadata)</param>
    /// <param name="inRaid">True if player is currently in a raid, false if in hideout</param>
    /// <returns>An InventorySnapshot containing all captured items, or null on failure</returns>
    /// <remarks>
    /// <para>
    /// This method coordinates the entire capture process:
    /// </para>
    /// <list type="number">
    ///   <item>Validates player and profile references</item>
    ///   <item>Gets configured slots to save from Settings</item>
    ///   <item>Captures all items recursively</item>
    ///   <item>Builds the snapshot metadata</item>
    /// </list>
    /// <para>
    /// The snapshot includes metadata like timestamp, location, and mod version
    /// which is used for validation and debugging purposes.
    /// </para>
    /// </remarks>
    public InventorySnapshot CaptureInventory(Player player, string location, bool inRaid)
    {
        try
        {
            // Validate input parameters
            if (player == null || player.Profile == null)
            {
                Plugin.Log.LogError("Cannot capture inventory: player or profile is null");
                return null;
            }

            var profile = player.Profile;
            var inventory = profile.Inventory;

            if (inventory == null)
            {
                Plugin.Log.LogError("Cannot capture inventory: inventory is null");
                return null;
            }

            // Get the list of slots to save based on user configuration
            var slotsToSave = GetSlotsToSave();

            // DEBUG: Log which slots are enabled
            Plugin.Log.LogInfo($"[KSG] Snapshot capture - Enabled slots: {string.Join(", ", slotsToSave)}");
            Plugin.Log.LogInfo($"[KSG] Backpack enabled: {slotsToSave.Contains("Backpack")}");
            Plugin.Log.LogInfo($"[KSG] Current preset: {Settings.ActivePreset.Value}");

            // Build set of insured item IDs if insurance exclusion is enabled
            // Insurance is tracked at the profile level, not on individual items
            // Store in field so ConvertToSerializedItem can access it
            _currentCaptureInsuredIds = BuildInsuredItemIdSet(profile);

            // DEBUG: Log insurance check status
            Plugin.Log.LogInfo($"[KSG] Exclude insured items: {Settings.ExcludeInsuredItems.Value}, Found {_currentCaptureInsuredIds.Count} insured item IDs");

            try
            {
                // Capture all items recursively from enabled slots
                var allItems = new List<SerializedItem>();
                var emptySlots = new List<string>();
                CaptureAllItems(inventory, allItems, slotsToSave, emptySlots);

                // Build the snapshot with all captured items and metadata
                var snapshot = new InventorySnapshot
                {
                    SessionId = profile.Id,
                    Timestamp = DateTime.UtcNow,
                    Location = location,
                    Items = allItems,
                    IncludedSlots = slotsToSave,
                    EmptySlots = emptySlots,
                    TakenInRaid = inRaid,
                    ModVersion = Plugin.PluginVersion
                };

                // Log capture results if enabled in settings
                if (Settings.LogSnapshotCreation.Value)
                {
                    Plugin.Log.LogInfo($"Snapshot: {allItems.Count} items captured");
                }

                return snapshot;
            }
            finally
            {
                // Clear cached state after capture completes (REL-005)
                _currentCaptureInsuredIds = new HashSet<string>();
                _insuranceCompany = null;
                _insuredMethod = null;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to capture inventory: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            // Clear cached state on error too (REL-005)
            _currentCaptureInsuredIds = new HashSet<string>();
            _insuranceCompany = null;
            _insuredMethod = null;
            return null;
        }
    }

    // ========================================================================
    // Item Capture - Main Processing
    // ========================================================================

    // ========================================================================
    // C-06 FIX: Constants for top-level equipment slots
    // Extracted to reduce duplication and improve maintainability
    // ========================================================================

    /// <summary>
    /// Set of top-level equipment slot names that are filtered by user configuration.
    /// All other slots (mod_*, patron_*, Soft_armor_*, etc.) are nested and captured
    /// automatically with their parent items.
    /// </summary>
    private static readonly HashSet<string> TopLevelEquipmentSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Scabbard",
        "Headwear", "Earpiece", "FaceCover", "Eyewear", "ArmBand",
        "TacticalVest", "ArmorVest", "Pockets", "Backpack", "SecuredContainer",
        "Compass", "SpecialSlot1", "SpecialSlot2", "SpecialSlot3", "Dogtag"
    };

    /// <summary>
    /// Captures all items from the player's inventory by iterating through equipment slots.
    /// C-06 FIX: Refactored from 664-line method to use extracted helper methods.
    /// </summary>
    /// <param name="inventory">The player's Inventory object</param>
    /// <param name="allItems">List to populate with captured items</param>
    /// <param name="slotsToSave">List of slot names that should be included</param>
    /// <param name="emptySlots">List to populate with slot names that were enabled but empty</param>
    private void CaptureAllItems(Inventory inventory, List<SerializedItem> allItems, List<string> slotsToSave, List<string> emptySlots)
    {
        try
        {
            var equipment = inventory.Equipment;
            if (equipment == null)
            {
                Plugin.Log.LogError("Equipment is null!");
                return;
            }

            // Capture Equipment container first (server needs its ID)
            CaptureEquipmentContainer(equipment, allItems);

            Plugin.Log.LogDebug("Starting equipment slot enumeration...");
            Plugin.Log.LogDebug($"Equipment type: {equipment.GetType().FullName}");

            // Log equipment structure for debugging (only in verbose mode)
            LogEquipmentStructure(equipment);

            // Get equipment slots using multiple fallback methods
            var slots = GetEquipmentSlots(equipment);
            if (slots == null)
            {
                LogSlotAccessFailure(equipment);
                return;
            }

            // First pass: Collect all slot items
            var slotItems = CollectSlotItems(slots);
            Plugin.Log.LogDebug($"Found {slotItems.Count} items in slots");

            // Track captured item IDs for parent-child relationships
            var capturedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Second pass: Capture top-level equipment slots (filtered by config)
            var slotsWithItems = CaptureTopLevelSlotItems(slotItems, slotsToSave, allItems, capturedItemIds);

            // Track empty slots for proper restoration
            TrackEmptySlots(slotsToSave, slotsWithItems, emptySlots);

            // Third pass: Capture nested/mod slots (if parent was captured)
            CaptureNestedSlotItems(slotItems, allItems, capturedItemIds);
            Plugin.Log.LogDebug($"Items captured from slots: {capturedItemIds.Count}");

            // Fourth pass: Capture grid contents (backpacks, rigs, magazines, ammo)
            var gridItemsFound = CaptureGridContents(slotItems, allItems, capturedItemIds);

            Plugin.Log.LogDebug($"Total items captured: {allItems.Count} ({gridItemsFound} from grids)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error capturing items: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    // ========================================================================
    // C-06 FIX: Extracted Helper Methods for CaptureAllItems
    // These methods break down the 664-line CaptureAllItems into testable units
    // ========================================================================

    /// <summary>
    /// Captures the Equipment container item itself (server needs its ID).
    /// </summary>
    private void CaptureEquipmentContainer(object equipment, List<SerializedItem> allItems)
    {
        var equipmentItem = equipment as Item;
        if (equipmentItem != null)
        {
            var equipmentSerialized = ConvertToSerializedItem(equipmentItem);
            if (equipmentSerialized != null)
            {
                allItems.Add(equipmentSerialized);
                Plugin.Log.LogDebug($"Captured Equipment container: ID={equipmentSerialized.Id}, Tpl={equipmentSerialized.Tpl}");
            }
        }
    }

    /// <summary>
    /// Logs equipment structure for debugging (only in verbose mode).
    /// </summary>
    private void LogEquipmentStructure(object equipment)
    {
        if (Settings.VerboseCaptureLogging?.Value != true)
            return;

        Plugin.Log.LogDebug("=== Equipment Properties ===");
        foreach (var prop in equipment.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Plugin.Log.LogDebug($"  Property: {prop.Name} : {prop.PropertyType.Name}");
        }

        Plugin.Log.LogDebug("=== Equipment Methods (slot-related) ===");
        foreach (var method in equipment.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name.ToLower().Contains("slot") || method.Name.ToLower().Contains("grid") || method.Name.ToLower().Contains("item"))
            {
                Plugin.Log.LogDebug($"  Method: {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
            }
        }
    }

    /// <summary>
    /// Gets equipment slots using multiple fallback methods for EFT version compatibility.
    /// </summary>
    /// <returns>Enumerable of slot objects, or null if no method succeeded.</returns>
    private IEnumerable<object> GetEquipmentSlots(object equipment)
    {
        IEnumerable<object> slots = null;

        // Method 1: Try AllSlots property (preferred - contains ALL equipment slots)
        var allSlotsProperty = equipment.GetType().GetProperty("AllSlots");
        if (allSlotsProperty != null)
        {
            Plugin.Log.LogDebug("Found AllSlots property, using it...");
            slots = allSlotsProperty.GetValue(equipment) as IEnumerable<object>;
            if (slots != null)
            {
                var slotList = slots.ToList();
                Plugin.Log.LogDebug($"AllSlots property returned {slotList.Count} slots");
                return slotList;
            }
        }

        // Method 2: Try GetAllSlots() method as fallback
        var getAllSlotsMethod = equipment.GetType().GetMethod("GetAllSlots");
        if (getAllSlotsMethod != null)
        {
            Plugin.Log.LogDebug("Trying GetAllSlots() method...");
            slots = getAllSlotsMethod.Invoke(equipment, null) as IEnumerable<object>;
            if (slots != null)
            {
                var slotList = slots.ToList();
                Plugin.Log.LogDebug($"GetAllSlots() returned {slotList.Count} slots");
                return slotList;
            }
        }

        // Method 3: Try Slots property as fallback
        Plugin.Log.LogDebug("AllSlots not found, trying Slots property...");
        var slotsProperty = equipment.GetType().GetProperty("Slots");
        if (slotsProperty != null)
        {
            Plugin.Log.LogDebug("Found Slots property, using it...");
            slots = slotsProperty.GetValue(equipment) as IEnumerable<object>;
            if (slots != null)
            {
                var slotList = slots.ToList();
                Plugin.Log.LogDebug($"Slots property returned {slotList.Count} slots");
                return slotList;
            }
        }

        // Method 4: Last resort - try ContainerSlots (may miss some equipment)
        Plugin.Log.LogWarning("Falling back to ContainerSlots (may miss some equipment)...");
        var containerSlotsProperty = equipment.GetType().GetProperty("ContainerSlots");
        if (containerSlotsProperty != null)
        {
            slots = containerSlotsProperty.GetValue(equipment) as IEnumerable<object>;
            if (slots != null)
            {
                var slotList = slots.ToList();
                Plugin.Log.LogWarning($"ContainerSlots returned {slotList.Count} slots");
                return slotList;
            }
        }

        return null;
    }

    /// <summary>
    /// Logs detailed debug info when no slot access method worked.
    /// </summary>
    private void LogSlotAccessFailure(object equipment)
    {
        Plugin.Log.LogError("Could not find any way to access Equipment slots!");
        Plugin.Log.LogError("Attempting to enumerate all Equipment properties and methods for debugging...");

        var equipmentType = equipment.GetType();
        Plugin.Log.LogError($"Equipment type: {equipmentType.FullName}");

        Plugin.Log.LogError("All public properties:");
        foreach (var prop in equipmentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Plugin.Log.LogError($"  - {prop.Name} : {prop.PropertyType.Name}");
        }

        Plugin.Log.LogError("All public methods:");
        foreach (var method in equipmentType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Take(20))
        {
            Plugin.Log.LogError($"  - {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
        }
    }

    /// <summary>
    /// Collects all items from slots into a list of (SlotName, Item) tuples.
    /// </summary>
    private List<(string SlotName, Item Item)> CollectSlotItems(IEnumerable<object> slots)
    {
        var slotItems = new List<(string SlotName, Item Item)>();
        bool verbose = Settings.VerboseCaptureLogging?.Value == true;

        if (verbose)
            Plugin.Log.LogDebug("First pass: Collecting slot items...");

        foreach (var slot in slots)
        {
            var slotType = slot.GetType();
            var slotNameProp = ReflectionCache.GetProperty(slotType, "Name");
            var slotName = slotNameProp?.GetValue(slot)?.ToString() ?? "Unknown";

            var containedItemProp = ReflectionCache.GetProperty(slotType, "ContainedItem");
            var containedItem = containedItemProp?.GetValue(slot) as Item;

            if (containedItem != null)
            {
                slotItems.Add((slotName, containedItem));
            }
        }

        return slotItems;
    }

    /// <summary>
    /// Captures top-level equipment slot items (filtered by user configuration).
    /// Returns the set of slot names that have items.
    /// </summary>
    private HashSet<string> CaptureTopLevelSlotItems(
        List<(string SlotName, Item Item)> slotItems,
        List<string> slotsToSave,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds)
    {
        var slotsWithItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool verbose = Settings.VerboseCaptureLogging?.Value == true;

        if (verbose)
            Plugin.Log.LogDebug("Second pass: Processing top-level equipment slots...");

        foreach (var (slotName, item) in slotItems)
        {
            // Only process top-level equipment slots here
            if (!TopLevelEquipmentSlots.Contains(slotName))
                continue;

            // Track that this slot has an item
            slotsWithItems.Add(slotName);

            if (verbose)
                Plugin.Log.LogDebug($"[SLOT DEBUG] Found item in slot '{slotName}': {item.Template?.NameLocalizationKey ?? item.TemplateId}");

            // Check if this slot is enabled in configuration
            bool slotEnabled = slotsToSave.Any(s => s.Equals(slotName, StringComparison.OrdinalIgnoreCase));

            if (slotEnabled)
            {
                if (verbose)
                    Plugin.Log.LogDebug($"[SLOT DEBUG]   -> Capturing slot '{slotName}'");

                var serializedItem = ConvertToSerializedItem(item);
                if (serializedItem != null)
                {
                    allItems.Add(serializedItem);
                    capturedItemIds.Add(item.Id);

                    if (verbose)
                        Plugin.Log.LogDebug($"[CAPTURE] Added top-level item: {serializedItem.Tpl} (ID: {serializedItem.Id})");
                }
            }
            else if (verbose)
            {
                Plugin.Log.LogDebug($"[SLOT DEBUG]   -> SKIPPING slot '{slotName}' (not in slotsToSave list!)");
            }
        }

        return slotsWithItems;
    }

    /// <summary>
    /// Tracks which enabled slots are empty (no item) for proper restoration.
    /// </summary>
    private void TrackEmptySlots(List<string> slotsToSave, HashSet<string> slotsWithItems, List<string> emptySlots)
    {
        bool verbose = Settings.VerboseCaptureLogging?.Value == true;

        foreach (var enabledSlot in slotsToSave)
        {
            // Only check top-level equipment slots
            if (!TopLevelEquipmentSlots.Contains(enabledSlot))
                continue;

            // If this enabled slot doesn't have an item, it's empty
            if (!slotsWithItems.Contains(enabledSlot))
            {
                emptySlots.Add(enabledSlot);
                if (verbose)
                    Plugin.Log.LogDebug($"[EMPTY SLOT] Slot '{enabledSlot}' is enabled but empty - will be cleared on restore");
            }
        }

        if (emptySlots.Count > 0 && verbose)
        {
            Plugin.Log.LogDebug($"[EMPTY SLOTS] Tracked {emptySlots.Count} empty slots: {string.Join(", ", emptySlots)}");
        }
    }

    /// <summary>
    /// Captures nested/mod slots (items whose parent was captured).
    /// </summary>
    private void CaptureNestedSlotItems(
        List<(string SlotName, Item Item)> slotItems,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds)
    {
        bool verbose = Settings.VerboseCaptureLogging?.Value == true;

        if (verbose)
            Plugin.Log.LogDebug("Third pass: Processing nested slots (mods, armor inserts, etc.)...");

        bool foundNewItems = true;
        int passCount = 0;

        while (foundNewItems && passCount < 10) // Limit iterations to prevent infinite loops
        {
            foundNewItems = false;
            passCount++;

            foreach (var (slotName, item) in slotItems)
            {
                // Skip top-level slots (already processed)
                if (TopLevelEquipmentSlots.Contains(slotName))
                    continue;

                // Skip if already captured
                if (capturedItemIds.Contains(item.Id))
                    continue;

                // Check if parent was captured
                var parentId = item.Parent?.Container?.ParentItem?.Id;
                if (parentId != null && capturedItemIds.Contains(parentId))
                {
                    var serializedItem = ConvertToSerializedItem(item);
                    if (serializedItem != null)
                    {
                        allItems.Add(serializedItem);
                        capturedItemIds.Add(item.Id);
                        foundNewItems = true;

                        if (verbose)
                            Plugin.Log.LogDebug($"[CAPTURE] Added nested item from slot '{slotName}': {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {parentId})");
                    }
                }
            }
        }

        if (verbose)
            Plugin.Log.LogDebug($"Nested item passes completed: {passCount}");
    }

    /// <summary>
    /// Captures grid contents (backpack items, rig items, magazine ammo, etc.).
    /// Returns the number of grid items found.
    /// </summary>
    private int CaptureGridContents(
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
                found = CaptureAmmoBoxContents(containerItem, allItems, capturedItemIds, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }

                // Capture cartridges from magazines
                found = CaptureCartridges(containerItem, allItems, capturedItemIds, verbose);
                if (found > 0)
                {
                    gridItemsFound += found;
                    foundMore = true;
                }
            }

            // Check for slots on grid items (armor plates in backpack armor)
            foreach (var gridItem in newItemsToCheck.ToList())
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
        var gridsField = ReflectionCache.GetField(itemType, "Grids", BindingFlags.Public | BindingFlags.Instance);
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
            found += CaptureFromItemCollection(grid, gridType, containerItem, gridId, allItems, capturedItemIds, newItemsToCheck, gridPass, verbose);

            // If no items found via ItemCollection, try Items property
            if (found == 0)
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

            var serializedItem = ConvertToSerializedItem(childItem);
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
                        X = Convert.ToInt32(xVal),
                        Y = Convert.ToInt32(yVal),
                        R = rVal != null ? Convert.ToInt32(rVal) : 0,
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

            var serializedItem = ConvertToSerializedItem(childItem);
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
    /// Captures ammo from ammo boxes (via StackSlot).
    /// </summary>
    private int CaptureAmmoBoxContents(
        Item containerItem,
        List<SerializedItem> allItems,
        HashSet<string> capturedItemIds,
        bool verbose)
    {
        var itemType = containerItem.GetType();
        var tplStr = containerItem.TemplateId.ToString();

        // Check if this might be an ammo box
        if (!tplStr.StartsWith("5737") && !itemType.Name.Contains("Ammo") && !itemType.Name.Contains("Box"))
            return 0;

        if (verbose)
        {
            Plugin.Log.LogDebug($"[AMMO BOX] Checking potential ammo box: {containerItem.TemplateId} Type: {itemType.Name}");
            var allProps = itemType.GetProperties().Select(p => p.Name).ToArray();
            var allFields = itemType.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();
            Plugin.Log.LogDebug($"[AMMO BOX] Properties: {string.Join(", ", allProps)}");
            Plugin.Log.LogDebug($"[AMMO BOX] Fields: {string.Join(", ", allFields)}");
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
        foreach (var ammoItem in items.ToList())
        {
            if (capturedItemIds.Contains(ammoItem.Id))
                continue;

            var serializedItem = ConvertToSerializedItem(ammoItem);
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
    private int CaptureCartridges(
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
                Plugin.Log.LogDebug($"[CARTRIDGES] No Items property on Cartridges. Available: {string.Join(", ", cartridges.GetType().GetProperties().Select(p => p.Name).Take(10))}");
            return 0;
        }

        var ammoItems = itemsProp.GetValue(cartridges) as IEnumerable<Item>;
        if (ammoItems == null)
            return 0;

        int found = 0;
        int cartridgePosition = 0;

        foreach (var ammo in ammoItems)
        {
            if (ammo != null && !capturedItemIds.Contains(ammo.Id))
            {
                var serializedItem = ConvertToSerializedItem(ammo);
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

            var serializedSlotItem = ConvertToSerializedItem(slotItem);
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

    // ========================================================================
    // Item Capture - Recursive Processing
    // ========================================================================

    /// <summary>
    /// Recursively captures an item and all of its children (contents, attachments).
    /// Handles different container types and special cases like magazines.
    /// </summary>
    /// <param name="item">The item to capture</param>
    /// <param name="allItems">List to populate with captured items</param>
    /// <param name="slotsToSave">List of slot names being captured (for context)</param>
    /// <remarks>
    /// <para>
    /// This method handles three types of nested items:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Grid contents:</b> Items in containers like backpacks and rigs</item>
    ///   <item><b>Slot contents:</b> Weapon attachments, armor inserts</item>
    ///   <item><b>Magazine ammunition:</b> Special case using Cartridges property</item>
    /// </list>
    /// <para>
    /// Magazine ammunition requires special handling because EFT stores it in a
    /// Cartridges property rather than in the normal Grids/Slots structure.
    /// </para>
    /// </remarks>
    private void CaptureItemRecursive(Item item, List<SerializedItem> allItems, List<string> slotsToSave)
    {
        if (item == null) return;

        bool verbose = Settings.VerboseCaptureLogging?.Value ?? false;

        try
        {
            // Convert this item to serialized format and add to list
            var serializedItem = ConvertToSerializedItem(item);
            if (serializedItem != null)
            {
                allItems.Add(serializedItem);

                if (Settings.EnableDebugMode.Value || verbose)
                {
                    Plugin.Log.LogDebug($"[CAPTURE] Added item: {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {serializedItem.ParentId ?? "none"}, Slot: {serializedItem.SlotId ?? "none"})");
                }
            }

            var itemType = item.GetType();

            if (verbose)
            {
                Plugin.Log.LogDebug($"[VERBOSE] Processing item type: {itemType.Name}, Template: {item.TemplateId}");
                // Log all properties that might contain child items
                var props = itemType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var containerProps = props.Where(p =>
                    p.Name == "Grids" || p.Name == "Slots" || p.Name == "Cartridges" ||
                    p.Name == "Chambers" || p.Name == "Items" || p.Name == "ContainedItem");
                Plugin.Log.LogDebug($"[VERBOSE] Container-related properties found: {string.Join(", ", containerProps.Select(p => p.Name))}");
            }

            // ================================================================
            // Handle Container Items (Backpacks, Rigs, etc.)
            // Use reflection to avoid type resolution issues across SPT versions
            // ================================================================

            // Try to get Grids FIELD via reflection (in EFT, Grids is a public field, not a property!)
            // Using cached reflection for performance
            var gridsField = ReflectionCache.GetField(itemType, "Grids", BindingFlags.Public | BindingFlags.Instance);
            if (gridsField != null)
            {
                if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Found Grids field on {itemType.Name}");

                var grids = gridsField.GetValue(item) as System.Collections.IEnumerable;
                if (grids != null)
                {
                    int gridCount = 0;
                    foreach (var grid in grids)
                    {
                        gridCount++;
                        if (grid == null) continue;

                        if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Processing grid {gridCount}, type: {grid.GetType().Name}");

                        var gridItemsProperty = ReflectionCache.GetProperty(grid.GetType(), "Items");
                        if (gridItemsProperty != null)
                        {
                            var gridItems = gridItemsProperty.GetValue(grid) as System.Collections.IEnumerable;
                            if (gridItems != null)
                            {
                                // Convert to list to avoid modification during iteration
                                var itemsList = new List<Item>();
                                foreach (var childItem in gridItems)
                                {
                                    if (childItem is Item i)
                                    {
                                        itemsList.Add(i);
                                        if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Found grid child item: {i.TemplateId}");
                                    }
                                }

                                if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Grid has {itemsList.Count} items");

                                foreach (var childItem in itemsList)
                                {
                                    CaptureItemRecursive(childItem, allItems, slotsToSave);
                                }
                            }
                            else if (verbose)
                            {
                                Plugin.Log.LogWarning($"[VERBOSE] Grid.Items returned null");
                            }
                        }
                        else if (verbose)
                        {
                            Plugin.Log.LogWarning($"[VERBOSE] Grid has no Items property");
                        }
                    }
                    if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Processed {gridCount} grids");
                }
                else if (verbose)
                {
                    Plugin.Log.LogWarning($"[VERBOSE] Grids property returned null");
                }
            }
            else if (verbose)
            {
                Plugin.Log.LogDebug($"[VERBOSE] No Grids field on {itemType.Name}");
            }

            // Try to get Slots property via reflection (using cached reflection for performance)
            var slotsProperty = ReflectionCache.GetProperty(itemType, "Slots");
            if (slotsProperty != null)
            {
                if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Found Slots property on {itemType.Name}");

                var slots = slotsProperty.GetValue(item) as System.Collections.IEnumerable;
                if (slots != null)
                {
                    int slotCount = 0;
                    foreach (var slot in slots)
                    {
                        slotCount++;
                        if (slot == null) continue;

                        // Get slot name for logging (use cached reflection)
                        var slotType = slot.GetType();
                        var slotNameProp = ReflectionCache.GetProperty(slotType, "Name");
                        var slotName = slotNameProp?.GetValue(slot)?.ToString() ?? "unknown";

                        var containedItemProperty = ReflectionCache.GetProperty(slotType, "ContainedItem");
                        if (containedItemProperty != null)
                        {
                            var containedItem = containedItemProperty.GetValue(slot) as Item;
                            if (containedItem != null)
                            {
                                if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Slot '{slotName}' contains: {containedItem.TemplateId}");
                                CaptureItemRecursive(containedItem, allItems, slotsToSave);
                            }
                            else if (verbose)
                            {
                                Plugin.Log.LogDebug($"[VERBOSE] Slot '{slotName}' is empty");
                            }
                        }
                    }
                    if (verbose) Plugin.Log.LogDebug($"[VERBOSE] Processed {slotCount} slots");
                }
                else if (verbose)
                {
                    Plugin.Log.LogWarning($"[VERBOSE] Slots property returned null");
                }
            }
            else if (verbose)
            {
                Plugin.Log.LogDebug($"[VERBOSE] No Slots property on {itemType.Name}");
            }

            // ================================================================
            // Special Handling for Magazines
            // Magazines store ammunition in a Cartridges property, not Grids/Slots
            // MagazineItemClass and similar types have this special structure
            // ================================================================
            // Note: itemType already declared above
            if (itemType.Name.Contains("Magazine") || itemType.Name.Contains("MagazineItem"))
            {
                Plugin.Log.LogDebug($"[MAGAZINE] Found magazine: {item.TemplateId}, checking for Cartridges...");

                // Try to get the Cartridges property via reflection (using cached reflection)
                var cartridgesProp = ReflectionCache.GetProperty(itemType, "Cartridges");
                if (cartridgesProp != null)
                {
                    var cartridges = cartridgesProp.GetValue(item);
                    if (cartridges != null)
                    {
                        Plugin.Log.LogDebug($"[MAGAZINE] Found Cartridges property, type: {cartridges.GetType().Name}");

                        // Cartridges is typically a StackSlot or similar container
                        // Try to get Items property from it (using cached reflection)
                        var itemsProp = ReflectionCache.GetProperty(cartridges.GetType(), "Items");
                        if (itemsProp != null)
                        {
                            var ammoItems = itemsProp.GetValue(cartridges) as IEnumerable<Item>;
                            if (ammoItems != null)
                            {
                                foreach (var ammo in ammoItems.ToList())
                                {
                                    Plugin.Log.LogDebug($"[MAGAZINE] Capturing ammo: {ammo.TemplateId}, Stack={ammo.StackObjectsCount}");
                                    CaptureItemRecursive(ammo, allItems, slotsToSave);
                                }
                            }
                        }
                        else
                        {
                            // Try casting Cartridges directly to IEnumerable<Item>
                            if (cartridges is IEnumerable<Item> cartridgeItems)
                            {
                                foreach (var ammo in cartridgeItems.ToList())
                                {
                                    Plugin.Log.LogDebug($"[MAGAZINE] Capturing ammo (direct): {ammo.TemplateId}, Stack={ammo.StackObjectsCount}");
                                    CaptureItemRecursive(ammo, allItems, slotsToSave);
                                }
                            }
                            else
                            {
                                Plugin.Log.LogWarning($"[MAGAZINE] Cartridges is not IEnumerable<Item>, type: {cartridges.GetType().FullName}");
                            }
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"[MAGAZINE] No Cartridges property found on {itemType.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error capturing item recursively: {ex.Message}");
        }
    }

    // ========================================================================
    // Item Serialization
    // ========================================================================

    /// <summary>
    /// Converts an EFT Item object to our serializable SerializedItem format.
    /// Extracts all necessary properties for later restoration.
    /// </summary>
    /// <param name="item">The EFT Item to convert</param>
    /// <returns>A SerializedItem representing the item, or null on failure</returns>
    /// <remarks>
    /// <para>
    /// This method extracts the following from each item:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Id:</b> Unique identifier for this specific item instance</item>
    ///   <item><b>Tpl:</b> Template ID (what type of item this is)</item>
    ///   <item><b>ParentId:</b> ID of the container/slot this item is in</item>
    ///   <item><b>SlotId:</b> Name of the slot or grid position</item>
    ///   <item><b>Location:</b> X/Y coordinates and rotation for grid items</item>
    ///   <item><b>Upd:</b> Dynamic properties (stack count, durability, etc.)</item>
    /// </list>
    /// <para>
    /// <b>Special handling for ammunition:</b> EFT uses numeric container IDs for
    /// ammo in magazines (like "4", "8"), but SPT profiles expect "cartridges".
    /// This method detects ammo items and remaps the slotId accordingly.
    /// </para>
    /// </remarks>
    private SerializedItem ConvertToSerializedItem(Item item)
    {
        try
        {
            // ================================================================
            // FIR (Found-in-Raid) Protection Check
            // If enabled, skip items that are marked as Found-in-Raid to prevent
            // exploiting the mod to duplicate FIR items
            // ================================================================
            if (Settings.ExcludeFIRItems.Value)
            {
                // SpawnedInSession = true means the item was found in the current raid (FIR)
                if (item.SpawnedInSession)
                {
                    if (Settings.EnableDebugMode.Value)
                    {
                        Plugin.Log.LogDebug($"[FIR SKIP] Skipping FIR item: {item.TemplateId}");
                    }
                    return null;
                }
            }

            // ================================================================
            // Insurance Protection Check
            // If enabled, skip items that are insured - let insurance handle them
            // Uses InsuranceCompanyClass from EFT to check insurance status
            // ================================================================
            if (Settings.ExcludeInsuredItems.Value)
            {
                // First check the cached set of insured IDs
                bool isInsured = _currentCaptureInsuredIds.Contains(item.Id);

                // If not found in set, try the dynamic method as fallback
                if (!isInsured && _insuredMethod != null)
                {
                    isInsured = IsItemInsured(item.Id);
                }

                if (isInsured)
                {
                    Plugin.Log.LogInfo($"[KSG] INSURANCE SKIP: Excluding insured item {item.TemplateId} (ID: {item.Id})");
                    return null;
                }
            }

            // Create the basic serialized item with ID and template
            var serialized = new SerializedItem
            {
                Id = item.Id,
                Tpl = item.TemplateId.ToString()
            };

            // ================================================================
            // Special Handling for Equipment Container
            // The Equipment container is the root of the equipment hierarchy.
            // It should NOT have parentId or slotId set because:
            // 1. Its Parent property can create a self-reference (parentId = own ID)
            // 2. Its CurrentAddress.Container.ID returns the session ID, not a slot
            // The server only needs the Equipment ID to remap child item parents.
            // M-01 FIX: Use shared constant instead of local const
            // ================================================================
            if (item.TemplateId.ToString() == TemplateIds.Equipment)
            {
                Plugin.Log.LogDebug($"[EQUIPMENT] Equipment container captured (ID={item.Id}) - skipping parentId/slotId");
                return serialized;
            }

            // ================================================================
            // Parent Information
            // Used to reconstruct the item hierarchy during restoration
            // ================================================================
            if (item.Parent?.Container?.ParentItem != null)
            {
                serialized.ParentId = item.Parent.Container.ParentItem.Id;
            }

            // ================================================================
            // Slot/Container ID
            // This is complex because ammo needs special handling
            // ================================================================
            if (item.CurrentAddress?.Container != null)
            {
                var containerId = item.CurrentAddress.Container.ID;

                // SPT profiles use "cartridges" for ammo in magazines, but EFT
                // internally uses numeric IDs. We need to detect ammo and remap.
                // IMPORTANT: Only remap for ammo INSIDE MAGAZINES, not loose ammo in grids!

                // Check if containerId is a small number (magazine ammo slots are 0-99)
                // M-11 FIX: Use constant instead of magic number
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < TemplateIds.MaxNumericSlotId;

                // Determine if this item is ammunition
                bool isAmmoItem = DetectIfAmmo(item);

                // Check if parent item is a magazine (only remap ammo in magazines, not in grids)
                bool parentIsMagazine = false;
                var parentItem = item.Parent?.Container?.ParentItem;
                if (parentItem != null)
                {
                    var parentTypeName = parentItem.GetType().Name;
                    parentIsMagazine = parentTypeName.Contains("Magazine") || parentTypeName.Contains("MagazineItem");

                    // Also check if parent has a "Cartridges" property (definitive magazine indicator)
                    if (!parentIsMagazine)
                    {
                        var cartridgesProp = parentItem.GetType().GetProperty("Cartridges");
                        parentIsMagazine = cartridgesProp != null;
                    }
                }

                // Log detailed debug info for stackable items
                if (item.StackObjectsCount > 1 || isNumericSlot)
                {
                    LogAmmoDebugInfo(item, containerId, isNumericSlot, isAmmoItem, serialized);
                    Plugin.Log.LogDebug($"[AMMO DEBUG] parentIsMagazine={parentIsMagazine}, parentType={parentItem?.GetType().Name ?? "null"}");
                }

                // Remap numeric slot IDs to "cartridges" ONLY for ammo INSIDE MAGAZINES
                // Loose ammo in grids (rigs, backpacks, pockets) should keep the grid slot ID
                if (isNumericSlot && isAmmoItem && parentIsMagazine)
                {
                    serialized.SlotId = "cartridges";
                    Plugin.Log.LogDebug($"[AMMO] Remapped slotId from '{containerId}' to 'cartridges' for ammo in magazine");
                }
                else
                {
                    // Keep original slot ID for:
                    // - Non-ammo items
                    // - Loose ammo in grid containers (rigs, backpacks, pockets)
                    // - Any item not in a magazine
                    serialized.SlotId = containerId;

                    if (isAmmoItem && isNumericSlot && !parentIsMagazine)
                    {
                        Plugin.Log.LogDebug($"[AMMO] Keeping original slotId '{containerId}' for loose ammo in grid container");
                    }
                }
            }

            // ================================================================
            // Grid Location
            // For items in container grids, capture X/Y position and rotation
            // ================================================================
            if (item.CurrentAddress != null)
            {
                try
                {
                    var addressType = item.CurrentAddress.GetType();
                    Plugin.Log.LogDebug($"[LOCATION] Item {item.TemplateId} address type: {addressType.Name}");

                    // GClass3393 (base class for grid addresses) has LocationInGrid as a PUBLIC FIELD, not property!
                    // Try fields first, then properties for compatibility
                    object location = null;

                    var locationField = addressType.GetField("LocationInGrid")
                                     ?? addressType.GetField("Location");
                    if (locationField != null)
                    {
                        location = locationField.GetValue(item.CurrentAddress);
                    }
                    else
                    {
                        var locationProp = addressType.GetProperty("LocationInGrid")
                                        ?? addressType.GetProperty("Location");
                        if (locationProp != null)
                        {
                            location = locationProp.GetValue(item.CurrentAddress);
                        }
                    }

                    if (location != null)
                    {
                        Plugin.Log.LogDebug($"[LOCATION] Found location object for {item.TemplateId}: {location.GetType().Name}");
                        var locationType = location.GetType();

                        // Try both properties and fields (LocationInGrid uses public fields, not properties)
                        object x = null, y = null, r = null;

                        // Try properties first
                        var xProp = locationType.GetProperty("x") ?? locationType.GetProperty("X");
                        var yProp = locationType.GetProperty("y") ?? locationType.GetProperty("Y");
                        var rProp = locationType.GetProperty("r") ?? locationType.GetProperty("R");

                        if (xProp != null) x = xProp.GetValue(location);
                        if (yProp != null) y = yProp.GetValue(location);
                        if (rProp != null) r = rProp.GetValue(location);

                        // Fall back to fields if properties not found (LocationInGrid uses public fields)
                        if (x == null)
                        {
                            var xField = locationType.GetField("x") ?? locationType.GetField("X");
                            if (xField != null) x = xField.GetValue(location);
                        }
                        if (y == null)
                        {
                            var yField = locationType.GetField("y") ?? locationType.GetField("Y");
                            if (yField != null) y = yField.GetValue(location);
                        }
                        if (r == null)
                        {
                            var rField = locationType.GetField("r") ?? locationType.GetField("R");
                            if (rField != null) r = rField.GetValue(location);
                        }

                        if (x != null && y != null)
                        {
                            serialized.Location = new ItemLocation
                            {
                                X = Convert.ToInt32(x),
                                Y = Convert.ToInt32(y),
                                R = r != null ? Convert.ToInt32(r) : 0,
                                IsSearched = true
                            };

                            // Log location capture only in verbose mode
                            if (Settings.VerboseCaptureLogging?.Value == true)
                                Plugin.Log.LogDebug($"[LOCATION] Captured grid position for {item.TemplateId}: X={serialized.Location.X}, Y={serialized.Location.Y}, R={serialized.Location.R}");
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"[LOCATION] Location object found but X or Y is null for {item.TemplateId}: x={x}, y={y}");
                        }
                    }
                    else
                    {
                        // This is normal for slot items (weapons in equipment slots, etc.)
                        // Only log for items that seem like grid items based on SlotId
                        var containerId = item.CurrentAddress.Container?.ID;
                        if (containerId != null &&
                            (containerId.StartsWith("main") || int.TryParse(containerId, out _)))
                        {
                            Plugin.Log.LogWarning($"[LOCATION] No LocationInGrid property for grid item {item.TemplateId} (SlotId={containerId}, AddressType={addressType.Name})");
                            // Log ALL available properties for debugging
                            var props = addressType.GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}").ToArray();
                            Plugin.Log.LogDebug($"[LOCATION] Available properties on {addressType.Name}: {string.Join(", ", props)}");

                            // Also try to find any property/field with "Location" in the name or that returns a struct/class with x/y
                            foreach (var prop in addressType.GetProperties())
                            {
                                try
                                {
                                    var val = prop.GetValue(item.CurrentAddress);
                                    if (val != null)
                                    {
                                        var valType = val.GetType();

                                        // Try properties first, then fields (LocationInGrid uses public fields)
                                        object x = null, y = null, r = null;

                                        var xProp = valType.GetProperty("x") ?? valType.GetProperty("X");
                                        var yProp = valType.GetProperty("y") ?? valType.GetProperty("Y");
                                        var rProp = valType.GetProperty("r") ?? valType.GetProperty("R");

                                        if (xProp != null) x = xProp.GetValue(val);
                                        if (yProp != null) y = yProp.GetValue(val);
                                        if (rProp != null) r = rProp.GetValue(val);

                                        // Fall back to fields
                                        if (x == null)
                                        {
                                            var xField = valType.GetField("x") ?? valType.GetField("X");
                                            if (xField != null) x = xField.GetValue(val);
                                        }
                                        if (y == null)
                                        {
                                            var yField = valType.GetField("y") ?? valType.GetField("Y");
                                            if (yField != null) y = yField.GetValue(val);
                                        }
                                        if (r == null)
                                        {
                                            var rField = valType.GetField("r") ?? valType.GetField("R");
                                            if (rField != null) r = rField.GetValue(val);
                                        }

                                        if (x != null && y != null)
                                        {
                                            Plugin.Log.LogDebug($"[LOCATION] FOUND! Property '{prop.Name}' has x={x}, y={y}");

                                            // Use this location!
                                            serialized.Location = new ItemLocation
                                            {
                                                X = Convert.ToInt32(x),
                                                Y = Convert.ToInt32(y),
                                                R = r != null ? Convert.ToInt32(r) : 0,
                                                IsSearched = true
                                            };
                                            Plugin.Log.LogDebug($"[LOCATION] Captured via '{prop.Name}': X={serialized.Location.X}, Y={serialized.Location.Y}, R={serialized.Location.R}");
                                            break;
                                        }
                                    }
                                }
                                catch (Exception propEx)
                                {
                                    // Property inspection error - log at debug level only if verbose logging enabled
                                    if (Settings.VerboseCaptureLogging?.Value == true)
                                        Plugin.Log.LogDebug($"[KSG] Property '{prop.Name}' inspection failed: {propEx.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Location capture failed - log for debugging but continue
                    if (Settings.EnableDebugMode.Value)
                    {
                        Plugin.Log.LogWarning($"[LOCATION] Failed to capture grid position for {item.TemplateId}: {ex.Message}");
                    }
                }
            }

            // ================================================================
            // Update Data (Upd)
            // Contains dynamic properties like stack count, durability, etc.
            // ================================================================
            var upd = new ItemUpd
            {
                StackObjectsCount = item.StackObjectsCount >= 1 ? (long?)item.StackObjectsCount : null,
                SpawnedInSession = item.SpawnedInSession
            };

            // Log stack count for debugging ammunition issues (only in verbose mode)
            if (item.StackObjectsCount > 1 && (Settings.VerboseCaptureLogging?.Value == true))
            {
                Plugin.Log.LogDebug($"[AMMO DEBUG] Item {item.TemplateId} has StackObjectsCount={item.StackObjectsCount}, ParentId={serialized.ParentId}, SlotId={serialized.SlotId}");
            }

            // Capture foldable state for folding weapons (stocks)
            try
            {
                var foldable = typeof(Item).GetProperty("Foldable")?.GetValue(item);
                if (foldable != null)
                {
                    var folded = foldable.GetType().GetProperty("Folded")?.GetValue(foldable);
                    if (folded is bool isFolded)
                    {
                        upd.Foldable = new UpdFoldable { Folded = isFolded };
                    }
                }
            }
            catch (Exception ex)
            {
                // Foldable capture is non-critical - log at debug level
                Plugin.Log.LogDebug($"[KSG] Foldable capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture MedKit HP (IFAK, AFAK, Grizzly, Surv12, CMS, etc.)
            // EFT stores current HP as a field on MedKitComponent (not a property)
            // This applies to ALL medical items including surgical kits
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Access the Components field to find MedKitComponent (using cached reflection)
                // NOTE: Check ALL items, not just those with "MedKit" in name
                // Surgical kits (Surv12, CMS) also use MedKitComponent but have different type names
                var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            if (comp != null && comp.GetType().Name.Contains("MedKit"))
                            {
                                // HpResource is a FIELD on the component, not a property (using cached reflection)
                                var hpField = ReflectionCache.GetField(comp.GetType(), "HpResource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (hpField != null)
                                {
                                    var hp = hpField.GetValue(comp);
                                    if (hp != null)
                                    {
                                        upd.MedKit = new UpdMedKit { HpResource = Convert.ToDouble(hp) };
                                        Plugin.Log.LogDebug($"Captured MedKit HP: {hp} for {item.TemplateId} (Type: {itemType.Name})");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"MedKit capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Repairable durability (armor, weapons)
            // EFT stores Durability and MaxDurability as fields on RepairableComponent
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Access Components field to find RepairableComponent (using cached reflection)
                var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            var compType = comp?.GetType();
                            if (compType != null && compType.Name.Contains("Repairable"))
                            {
                                // Durability and MaxDurability are FIELDS on the component (using cached reflection)
                                var durField = ReflectionCache.GetField(compType, "Durability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var maxDurField = ReflectionCache.GetField(compType, "MaxDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                                if (durField != null)
                                {
                                    var dur = durField.GetValue(comp);
                                    var maxDur = maxDurField?.GetValue(comp);
                                    if (dur != null)
                                    {
                                        upd.Repairable = new UpdRepairable
                                        {
                                            Durability = Convert.ToDouble(dur),
                                            MaxDurability = maxDur != null ? Convert.ToDouble(maxDur) : 100
                                        };
                                        Plugin.Log.LogDebug($"Captured durability: {dur}/{maxDur} for {item.TemplateId}");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Durability capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Resource value (fuel cans, etc.) - using cached reflection
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var resourceProp = ReflectionCache.GetProperty(itemType, "ResourceComponent") ??
                                   ReflectionCache.GetProperty(itemType, "Resource");
                if (resourceProp != null)
                {
                    var resourceValue = resourceProp.GetValue(item);
                    if (resourceValue != null)
                    {
                        var valueProp = ReflectionCache.GetProperty(resourceValue.GetType(), "Value");
                        if (valueProp != null)
                        {
                            var val = valueProp.GetValue(resourceValue);
                            if (val != null)
                            {
                                upd.Resource = new UpdResource { Value = Convert.ToDouble(val) };
                                if (Settings.VerboseCaptureLogging?.Value == true)
                                    Plugin.Log.LogDebug($"[RESOURCE] Captured Value={val} for {item.TemplateId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Resource capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] Resource capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture FoodDrink value - using cached reflection
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var foodDrinkProp = ReflectionCache.GetProperty(itemType, "FoodDrinkComponent") ??
                                    ReflectionCache.GetProperty(itemType, "FoodDrink");
                if (foodDrinkProp != null)
                {
                    var foodDrinkValue = foodDrinkProp.GetValue(item);
                    if (foodDrinkValue != null)
                    {
                        var hpProp = ReflectionCache.GetProperty(foodDrinkValue.GetType(), "HpPercent");
                        if (hpProp != null)
                        {
                            var hp = hpProp.GetValue(foodDrinkValue);
                            if (hp != null)
                            {
                                upd.FoodDrink = new UpdFoodDrink { HpPercent = Convert.ToDouble(hp) };
                                if (Settings.VerboseCaptureLogging?.Value == true)
                                    Plugin.Log.LogDebug($"[FOODDRINK] Captured HpPercent={hp} for {item.TemplateId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // FoodDrink capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] FoodDrink capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Dogtag metadata (kill information)
            // Dogtags store important data about who was killed, by whom, when
            // Without this data, dogtags appear "wiped" or invalid
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var templateId = item.TemplateId.ToString();

                // Dogtag template IDs: BEAR dogtag = 59f32bb586f774757e1e8442, USEC dogtag = 59f32c3b86f77472a31742f0
                bool isDogtag = templateId == "59f32bb586f774757e1e8442" ||
                               templateId == "59f32c3b86f77472a31742f0" ||
                               itemType.Name.Contains("Dogtag");

                if (isDogtag)
                {
                    // Try to find DogtagComponent in the item's components (using cached reflection)
                    var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                var compType = comp?.GetType();
                                if (compType != null && compType.Name.Contains("Dogtag"))
                                {
                                    upd.Dogtag = new UpdDogtag();

                                    // Extract all dogtag properties via cached reflection
                                    var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                                    var accountIdField = ReflectionCache.GetField(compType, "AccountId", bindingFlags);
                                    var profileIdField = ReflectionCache.GetField(compType, "ProfileId", bindingFlags);
                                    var nicknameField = ReflectionCache.GetField(compType, "Nickname", bindingFlags);
                                    var sideField = ReflectionCache.GetField(compType, "Side", bindingFlags);
                                    var levelField = ReflectionCache.GetField(compType, "Level", bindingFlags);
                                    var timeField = ReflectionCache.GetField(compType, "Time", bindingFlags);
                                    var statusField = ReflectionCache.GetField(compType, "Status", bindingFlags);
                                    var killerAccountIdField = ReflectionCache.GetField(compType, "KillerAccountId", bindingFlags);
                                    var killerProfileIdField = ReflectionCache.GetField(compType, "KillerProfileId", bindingFlags);
                                    var killerNameField = ReflectionCache.GetField(compType, "KillerName", bindingFlags);
                                    var weaponNameField = ReflectionCache.GetField(compType, "WeaponName", bindingFlags);

                                    upd.Dogtag.AccountId = accountIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.ProfileId = profileIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Nickname = nicknameField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Side = sideField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Level = levelField != null ? Convert.ToInt32(levelField.GetValue(comp)) : 0;
                                    upd.Dogtag.Time = timeField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Status = statusField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerAccountId = killerAccountIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerProfileId = killerProfileIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerName = killerNameField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.WeaponName = weaponNameField?.GetValue(comp)?.ToString();

                                    Plugin.Log.LogDebug($"[DOGTAG] Captured dogtag metadata: {upd.Dogtag.Nickname} (Level {upd.Dogtag.Level}) killed by {upd.Dogtag.KillerName}");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Dogtag capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Key uses remaining - using cached reflection
            // Some keys have limited uses before they break
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Check if this is a key item
                if (itemType.Name.Contains("Key"))
                {
                    var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                var compType = comp?.GetType();
                                if (compType != null && compType.Name.Contains("Key"))
                                {
                                    var numberOfUsagesField = ReflectionCache.GetField(compType, "NumberOfUsages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (numberOfUsagesField != null)
                                    {
                                        var uses = numberOfUsagesField.GetValue(comp);
                                        if (uses != null)
                                        {
                                            upd.Key = new UpdKey { NumberOfUsages = Convert.ToInt32(uses) };
                                            if (Settings.VerboseCaptureLogging?.Value == true)
                                                Plugin.Log.LogDebug($"[KEY] Captured NumberOfUsages={uses} for {item.TemplateId}");
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Key capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] Key capture failed for {item.TemplateId}: {ex.Message}");
            }

            serialized.Upd = upd;

            if (Settings.EnableDebugMode.Value)
            {
                Plugin.Log.LogDebug($"  Item: {item.Template?.NameLocalizationKey ?? "Unknown"} (Tpl: {serialized.Tpl}, ID: {serialized.Id})");
            }

            return serialized;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to convert item: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    // ========================================================================
    // Ammo Detection Helpers
    // ========================================================================

    /// <summary>
    /// Determines if an item is ammunition by checking various indicators.
    /// Uses multiple detection methods for reliability across EFT versions.
    /// </summary>
    /// <param name="item">The item to check</param>
    /// <returns>True if the item appears to be ammunition</returns>
    /// <remarks>
    /// Detection methods tried in order:
    /// <list type="number">
    ///   <item>Type hierarchy - check if class name contains "Ammo" or "Bullet"</item>
    ///   <item>Template type hierarchy - check template class name</item>
    ///   <item>Template parent ID - all ammo has parent "5485a8684bdc2da71d8b4567"</item>
    ///   <item>Stack size heuristic - ammo typically has high stack sizes (20-60+)</item>
    /// </list>
    /// </remarks>
    private bool DetectIfAmmo(Item item)
    {
        bool isAmmoItem = false;

        try
        {
            // Method 1: Check item type hierarchy (Ammo inherits from StackableItem)
            var currentType = item.GetType();
            while (currentType != null && !isAmmoItem)
            {
                if (currentType.Name.Contains("Ammo") || currentType.Name.Contains("Bullet"))
                {
                    isAmmoItem = true;
                }
                currentType = currentType.BaseType;
            }

            // Method 2: Check template type hierarchy
            if (!isAmmoItem && item.Template != null)
            {
                currentType = item.Template.GetType();
                while (currentType != null && !isAmmoItem)
                {
                    if (currentType.Name.Contains("Ammo") || currentType.Name.Contains("Bullet"))
                    {
                        isAmmoItem = true;
                    }
                    currentType = currentType.BaseType;
                }
            }

            // Method 3: Check by template parent ID (all ammo descends from this)
            if (!isAmmoItem && item.Template != null)
            {
                var parentProp = item.Template.GetType().GetProperty("Parent");
                var parent = parentProp?.GetValue(item.Template)?.ToString();
                if (parent == "5485a8684bdc2da71d8b4567")
                {
                    isAmmoItem = true;
                    Plugin.Log.LogDebug($"[AMMO] Detected ammo by template parent ID");
                }
            }

            // Method 4: Stack size heuristic (ammo typically stacks to 20-60+)
            if (!isAmmoItem && item.Template != null)
            {
                var stackMax = item.Template.StackMaxSize;
                var containerId = item.CurrentAddress?.Container?.ID;
                // M-11 FIX: Use constant instead of magic number
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < TemplateIds.MaxNumericSlotId;

                // High stack size AND in numeric slot = probably ammo
                if (stackMax >= 20 && isNumericSlot)
                {
                    isAmmoItem = true;
                    Plugin.Log.LogDebug($"[AMMO] Detected ammo by StackMaxSize={stackMax} and numeric slot");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AMMO] Type check exception: {ex.Message}");
        }

        return isAmmoItem;
    }

    /// <summary>
    /// Logs detailed debug information about an item for ammunition diagnostics.
    /// Helps troubleshoot issues with ammo capture and restoration.
    /// </summary>
    /// <param name="item">The item being logged</param>
    /// <param name="containerId">The container ID from the item's address</param>
    /// <param name="isNumericSlot">Whether the container ID is numeric</param>
    /// <param name="isAmmoItem">Whether the item was detected as ammo</param>
    /// <param name="serialized">The serialized item being built</param>
    private void LogAmmoDebugInfo(Item item, string containerId, bool isNumericSlot, bool isAmmoItem, SerializedItem serialized)
    {
        var parentItem = item.Parent?.Container?.ParentItem;
        var parentTypeName = parentItem?.GetType().Name ?? "null";
        var templateTypeName = item.Template?.GetType().Name ?? "null";

        // Build full type hierarchy for debugging
        var typeHierarchy = new List<string>();
        var t = item.GetType();
        while (t != null)
        {
            typeHierarchy.Add(t.Name);
            t = t.BaseType;
        }

        Plugin.Log.LogDebug($"[AMMO DEBUG] containerId='{containerId}', isNumeric={isNumericSlot}, isAmmo={isAmmoItem}");
        Plugin.Log.LogDebug($"[AMMO DEBUG] itemTypeHierarchy={string.Join(" -> ", typeHierarchy)}");
        Plugin.Log.LogDebug($"[AMMO DEBUG] templateType={templateTypeName}, parentType={parentTypeName}");
    }

    // ========================================================================
    // Legacy Inventory Restoration (Client-Side)
    // Note: Server-side restoration is now preferred - see RaidEndInterceptor
    // ========================================================================

    /// <summary>
    /// Restores a player's inventory from a snapshot.
    /// </summary>
    /// <param name="controller">The player's InventoryController</param>
    /// <param name="snapshot">The snapshot to restore from</param>
    /// <returns>True if restoration succeeded, false otherwise</returns>
    /// <remarks>
    /// <para>
    /// <b>IMPORTANT:</b> This method is legacy code kept for compatibility.
    /// The server-side component (RaidEndInterceptor) now handles actual
    /// inventory restoration by modifying the profile JSON directly, which
    /// is more reliable and doesn't cause Run-Through status issues.
    /// </para>
    /// <para>
    /// This client-side restoration was originally attempted but had issues:
    /// </para>
    /// <list type="bullet">
    ///   <item>Items created at runtime don't persist properly</item>
    ///   <item>Inventory controller state may be inconsistent after death</item>
    ///   <item>Could cause Run-Through status penalties</item>
    /// </list>
    /// </remarks>
    public bool RestoreInventory(InventoryController controller, InventorySnapshot snapshot)
    {
        try
        {
            // Validate inputs
            if (controller == null || controller.Inventory == null)
            {
                Plugin.Log.LogError("Cannot restore inventory: controller or inventory is null");
                return false;
            }

            if (snapshot == null || !snapshot.IsValid())
            {
                Plugin.Log.LogError("Cannot restore inventory: invalid snapshot");
                return false;
            }

            if (Settings.LogSnapshotRestoration.Value)
            {
                Plugin.Log.LogInfo($"Restoring {snapshot.Items.Count} items from snapshot");
            }

            // ================================================================
            // Get ItemFactory via Singleton pattern
            // ItemFactory is used to create new item instances
            // ================================================================
            var itemFactoryType = typeof(Item).Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "ItemFactoryClass");

            if (itemFactoryType == null)
            {
                Plugin.Log.LogError("Could not find ItemFactoryClass type!");
                return false;
            }

            Plugin.Log.LogDebug($"Found ItemFactoryClass: {itemFactoryType.FullName}");

            // Find Singleton<T> type from Comfort.Common assembly
            var comfortAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Comfort" || a.GetName().Name == "Comfort.Unity");

            if (comfortAssembly == null)
            {
                Plugin.Log.LogError("Could not find Comfort assembly!");
                return false;
            }

            var singletonType = comfortAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "Singleton`1" && t.Namespace == "Comfort.Common");

            if (singletonType == null)
            {
                Plugin.Log.LogError("Could not find Singleton<T> type in Comfort.Common!");
                return false;
            }

            Plugin.Log.LogDebug($"Found Singleton type: {singletonType.FullName}");

            // Create Singleton<ItemFactoryClass> generic type
            Type genericSingletonType;
            try
            {
                genericSingletonType = singletonType.MakeGenericType(itemFactoryType);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to create generic Singleton type: {ex.Message}");
                return false;
            }

            // Get the Instance property
            var instanceProperty = genericSingletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

            if (instanceProperty == null)
            {
                Plugin.Log.LogError("Could not find Instance property on Singleton!");
                return false;
            }

            var itemFactory = instanceProperty.GetValue(null);
            if (itemFactory == null)
            {
                Plugin.Log.LogError("ItemFactory instance is null!");
                return false;
            }

            Plugin.Log.LogDebug("ItemFactory accessed via Comfort.Common.Singleton");

            // ================================================================
            // Step 1: Create all items from snapshot
            // ================================================================
            var createdItems = new Dictionary<string, Item>();
            var createItemMethod = itemFactory.GetType().GetMethod("CreateItem",
                BindingFlags.Public | BindingFlags.Instance);

            if (createItemMethod == null)
            {
                Plugin.Log.LogError("Could not find CreateItem method on ItemFactory!");
                return false;
            }

            Plugin.Log.LogDebug($"Creating {snapshot.Items.Count} items from snapshot...");

            foreach (var serializedItem in snapshot.Items)
            {
                try
                {
                    // Create the item using ItemFactory.CreateItem(id, templateId, parent)
                    var item = createItemMethod.Invoke(itemFactory, new object[] { serializedItem.Id, serializedItem.Tpl, null }) as Item;

                    if (item == null)
                    {
                        Plugin.Log.LogWarning($"Failed to create item with template: {serializedItem.Tpl}");
                        continue;
                    }

                    // Apply item properties from snapshot
                    if (serializedItem.Upd != null)
                    {
                        // Set stack count for stackable items (ammo, money, etc.)
                        if (serializedItem.Upd.StackObjectsCount.HasValue && serializedItem.Upd.StackObjectsCount.Value > 1)
                        {
                            TrySetStackCount(item, (int)serializedItem.Upd.StackObjectsCount.Value);
                        }

                        // Set SpawnedInSession flag (found in raid status)
                        if (serializedItem.Upd.SpawnedInSession)
                        {
                            TrySetSpawnedInSession(item, true);
                        }
                    }

                    createdItems[serializedItem.Id] = item;

                    if (Settings.EnableDebugMode.Value)
                    {
                        Plugin.Log.LogDebug($"Created item: {item.Template?.NameLocalizationKey ?? serializedItem.Tpl} (ID: {serializedItem.Id})");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Error creating item {serializedItem.Id}: {ex.Message}");
                }
            }

            Plugin.Log.LogDebug($"Created {createdItems.Count} items");

            // ================================================================
            // Step 2: Build parent-child hierarchy and place items
            // ================================================================
            var equipmentSlotNames = new[] {
                "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Scabbard",
                "Headwear", "Earpiece", "FaceCover", "Eyewear", "ArmBand",
                "TacticalVest", "ArmorVest", "Pockets", "Backpack", "SecuredContainer",
                "Compass", "SpecialSlot1", "SpecialSlot2", "SpecialSlot3"
            };

            // Root items are those that go directly in equipment slots
            var rootItems = snapshot.Items.Where(i => !string.IsNullOrEmpty(i.SlotId) &&
                                                      equipmentSlotNames.Contains(i.SlotId)).ToList();

            Plugin.Log.LogDebug($"Found {rootItems.Count} root items to place in equipment");

            if (rootItems.Count == 0)
            {
                Plugin.Log.LogWarning("No root items found! This might indicate an issue with snapshot data.");
                Plugin.Log.LogDebug($"Sample item parentIds: {string.Join(", ", snapshot.Items.Take(3).Select(i => $"{i.SlotId}={i.ParentId}"))}");
            }

            // Place each root item and its children
            foreach (var serializedItem in rootItems)
            {
                if (!createdItems.TryGetValue(serializedItem.Id, out var item))
                {
                    Plugin.Log.LogWarning($"Root item {serializedItem.Id} was not created, skipping");
                    continue;
                }

                if (!string.IsNullOrEmpty(serializedItem.SlotId))
                {
                    Plugin.Log.LogDebug($"Attempting to place item in slot: {serializedItem.SlotId}");
                    PlaceItemWithChildren(controller, item, serializedItem, snapshot.Items, createdItems);
                }
            }

            Plugin.Log.LogDebug("Inventory restoration completed");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to restore inventory: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Places an item in the inventory along with all its children recursively.
    /// Used during client-side restoration.
    /// </summary>
    /// <param name="controller">The player's InventoryController</param>
    /// <param name="item">The item to place</param>
    /// <param name="serializedItem">The serialized data for this item</param>
    /// <param name="allSerializedItems">All items in the snapshot (for finding children)</param>
    /// <param name="createdItems">Dictionary of all created items by ID</param>
    private void PlaceItemWithChildren(InventoryController controller, Item item, SerializedItem serializedItem,
        List<SerializedItem> allSerializedItems, Dictionary<string, Item> createdItems)
    {
        try
        {
            // Try to add item using InventoryController.AddItem method
            var addItemMethod = typeof(InventoryController).GetMethod("AddItem",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Item) },
                null);

            if (addItemMethod != null)
            {
                addItemMethod.Invoke(controller, new object[] { item });
                Plugin.Log.LogDebug($"Added item to inventory: {item.Template?.NameLocalizationKey ?? item.TemplateId}");
            }
            else
            {
                Plugin.Log.LogWarning("Could not find AddItem method - trying alternative placement");
            }

            // Recursively place all children of this item
            var children = allSerializedItems.Where(i => i.ParentId == serializedItem.Id).ToList();
            foreach (var childSerialized in children)
            {
                if (createdItems.TryGetValue(childSerialized.Id, out var childItem))
                {
                    PlaceItemWithChildren(controller, childItem, childSerialized, allSerializedItems, createdItems);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error placing item {serializedItem.Id}: {ex.Message}");
        }
    }

    // ========================================================================
    // Property Setters (Reflection-based)
    // ========================================================================

    /// <summary>
    /// Attempts to set the stack count on an item using reflection.
    /// Tries property first, then falls back to field access.
    /// </summary>
    /// <param name="item">The item to modify</param>
    /// <param name="count">The stack count to set</param>
    private void TrySetStackCount(Item item, int count)
    {
        try
        {
            // Try property first
            var stackProperty = item.GetType().GetProperty("StackObjectsCount", BindingFlags.Public | BindingFlags.Instance);
            if (stackProperty != null && stackProperty.CanWrite)
            {
                stackProperty.SetValue(item, count);
                return;
            }

            // Try field as fallback
            var stackField = item.GetType().GetField("StackObjectsCount", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (stackField != null)
            {
                stackField.SetValue(item, count);
            }
        }
        catch (Exception ex)
        {
            // HIGH-002 FIX: Failing to set stack count affects gameplay (wrong item quantities)
            // Use Warning level so users know their items might have incorrect counts
            Plugin.Log.LogWarning($"Could not set stack count: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to set the SpawnedInSession flag on an item using reflection.
    /// This flag indicates if an item was found in the current raid.
    /// </summary>
    /// <param name="item">The item to modify</param>
    /// <param name="value">The value to set</param>
    private void TrySetSpawnedInSession(Item item, bool value)
    {
        try
        {
            // Try property first
            var property = item.GetType().GetProperty("SpawnedInSession", BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(item, value);
                return;
            }

            // Try field as fallback
            var field = item.GetType().GetField("SpawnedInSession", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(item, value);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not set SpawnedInSession: {ex.Message}");
        }
    }

    // ========================================================================
    // Reflection Helpers
    // ========================================================================

    /// <summary>
    /// Safely tries to get a property value for logging purposes.
    /// Returns "error" if the property cannot be read.
    /// </summary>
    private string TryGetValue(PropertyInfo prop, object obj)
    {
        try
        {
            var val = prop.GetValue(obj);
            return val?.ToString() ?? "null";
        }
        catch
        {
            return "error";
        }
    }

    // ========================================================================
    // Configuration Helpers
    // ========================================================================

    /// <summary>
    /// Gets the list of inventory slot names that should be included in snapshots.
    /// Reads from user configuration to determine which slots are enabled.
    /// </summary>
    /// <returns>List of slot names that are enabled in settings</returns>
    /// <remarks>
    /// Users can configure which slots to include via the BepInEx Configuration
    /// Manager (F12). By default, all slots are enabled.
    /// </remarks>
    private List<string> GetSlotsToSave()
    {
        var slots = new List<string>();
        var slotSettings = Settings.GetInventorySlots();

        foreach (var kvp in slotSettings)
        {
            if (kvp.Value.Value) // If this slot is enabled in config
            {
                slots.Add(kvp.Key);
            }
        }

        return slots;
    }

    // ========================================================================
    // Insurance Check Helpers
    // ========================================================================

    /// <summary>
    /// Cached reference to the InsuranceCompanyClass for checking if items are insured.
    /// This is populated at the start of capture and cleared after.
    /// </summary>
    private object _insuranceCompany = null;
    private MethodInfo _insuredMethod = null;

    /// <summary>
    /// Builds a HashSet of insured item IDs by querying the game's InsuranceCompanyClass.
    /// This is the proper EFT way to check insurance status.
    /// </summary>
    /// <param name="profile">The player's profile</param>
    /// <returns>HashSet of insured item IDs (empty if insurance exclusion is disabled)</returns>
    /// <remarks>
    /// EFT tracks insurance via InsuranceCompanyClass which has an Insured(string itemId)
    /// method. We cache the reference and method info for performance during capture.
    /// </remarks>
    private HashSet<string> BuildInsuredItemIdSet(object profile)
    {
        var insuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only build the set if insurance exclusion is actually enabled
        if (!Settings.ExcludeInsuredItems.Value)
        {
            return insuredIds;
        }

        try
        {
            // Try multiple approaches to find insured items

            // NEW Approach 0: Access EFT Profile.InsuredItems directly via MainPlayer
            // Based on dnSpy analysis: Profile class has public \uE650[] InsuredItems field
            // Each element has ItemId and TraderId properties
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld?.MainPlayer != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Trying MainPlayer.Profile.InsuredItems approach");
                var mainPlayer = gameWorld.MainPlayer;

                // Access Profile
                var profileProp = mainPlayer.GetType().GetProperty("Profile", BindingFlags.Public | BindingFlags.Instance);
                if (profileProp != null)
                {
                    var eftProfile = profileProp.GetValue(mainPlayer);
                    if (eftProfile != null)
                    {
                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - EFT Profile type: {eftProfile.GetType().FullName}");

                        // Try InsuredItems as field (it's a public field, not property)
                        var insuredItemsField = eftProfile.GetType().GetField("InsuredItems", BindingFlags.Public | BindingFlags.Instance);
                        if (insuredItemsField != null)
                        {
                            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuredItems FIELD");
                            var insuredItemsArray = insuredItemsField.GetValue(eftProfile);

                            if (insuredItemsArray != null && insuredItemsArray is System.Array arr)
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - InsuredItems array length: {arr.Length}");

                                foreach (var insuredItem in arr)
                                {
                                    if (insuredItem == null) continue;

                                    // Get ItemId property (or field)
                                    var itemIdProp = insuredItem.GetType().GetProperty("ItemId")
                                                  ?? insuredItem.GetType().GetProperty("itemId");
                                    var itemIdField = insuredItem.GetType().GetField("ItemId", BindingFlags.Public | BindingFlags.Instance)
                                                   ?? insuredItem.GetType().GetField("itemId", BindingFlags.Public | BindingFlags.Instance);

                                    string itemId = null;
                                    if (itemIdProp != null)
                                    {
                                        itemId = itemIdProp.GetValue(insuredItem) as string;
                                    }
                                    else if (itemIdField != null)
                                    {
                                        itemId = itemIdField.GetValue(insuredItem) as string;
                                    }

                                    if (!string.IsNullOrEmpty(itemId))
                                    {
                                        insuredIds.Add(itemId);
                                        Plugin.Log.LogDebug($"[KSG] Found insured item ID: {itemId}");
                                    }
                                }

                                if (insuredIds.Count > 0)
                                {
                                    Plugin.Log.LogInfo($"[KSG] SUCCESS: Found {insuredIds.Count} insured items from EFT Profile.InsuredItems!");
                                    return insuredIds;
                                }
                            }
                        }
                        else
                        {
                            // Try as property if not found as field
                            var insuredItemsProp = eftProfile.GetType().GetProperty("InsuredItems", BindingFlags.Public | BindingFlags.Instance);
                            if (insuredItemsProp != null)
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuredItems as property");
                                var items = insuredItemsProp.GetValue(eftProfile);
                                if (items != null)
                                {
                                    int count = ExtractInsuredIdsFromObject(items, insuredIds);
                                    if (count > 0)
                                    {
                                        Plugin.Log.LogInfo($"[KSG] Found {count} insured items from Profile.InsuredItems property");
                                        return insuredIds;
                                    }
                                }
                            }
                            else
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - InsuredItems not found as field or property");
                                // Log all fields/props for debugging
                                var fields = eftProfile.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                                var fieldNames = string.Join(", ", fields.Select(f => f.Name).Take(20));
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - EFT Profile fields: {fieldNames}");
                            }
                        }
                    }
                }
            }

            // Approach 1: Check for InsuranceInfo on profile (SPT Profile.InsuranceInfo)
            var profileType = profile.GetType();
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile type: {profileType.FullName}");

            // List all properties to help diagnose
            var allProps = profileType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile has {allProps.Length} properties");
            // Log all property names for diagnosis
            var propNames = string.Join(", ", allProps.Select(p => p.Name).Take(20));
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - First 20 props: {propNames}");

            foreach (var prop in allProps)
            {
                if (prop.Name.ToLower().Contains("insur") || prop.PropertyType.Name.ToLower().Contains("insur"))
                {
                    Plugin.Log.LogInfo($"[KSG] FOUND insurance-related property: {prop.Name} ({prop.PropertyType.Name})");
                }
            }

            // Try Profile.InsuranceInfo
            var insuranceInfoProp = profileType.GetProperty("InsuranceInfo", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (insuranceInfoProp != null)
            {
                Plugin.Log.LogInfo($"[KSG] Found Profile.InsuranceInfo property");
                var insuranceInfo = insuranceInfoProp.GetValue(profile);
                if (insuranceInfo != null)
                {
                    Plugin.Log.LogInfo($"[KSG] InsuranceInfo type: {insuranceInfo.GetType().Name}");
                    // Try to enumerate insured items
                    int count = ExtractInsuredIdsFromObject(insuranceInfo, insuredIds);
                    if (count > 0)
                    {
                        Plugin.Log.LogInfo($"[KSG] Found {count} insured items from Profile.InsuranceInfo");
                        return insuredIds;
                    }
                }
            }

            // Approach 2: Try TradersInfo - insurance is managed by Prapor/Therapist
            var tradersInfoProp = profileType.GetProperty("TradersInfo", BindingFlags.Public | BindingFlags.Instance);
            if (tradersInfoProp != null)
            {
                var tradersInfo = tradersInfoProp.GetValue(profile);
                if (tradersInfo != null)
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - TradersInfo type: {tradersInfo.GetType().Name}");

                    // TradersInfo might be a dictionary or have trader-specific properties
                    var tradersType = tradersInfo.GetType();
                    var tradersProps = tradersType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - TradersInfo has {tradersProps.Length} properties: {string.Join(", ", tradersProps.Select(p => p.Name).Take(10))}");

                    // Check if it's enumerable (dictionary of traders)
                    if (tradersInfo is System.Collections.IEnumerable tradersEnum)
                    {
                        foreach (var traderEntry in tradersEnum)
                        {
                            if (traderEntry == null) continue;
                            var entryType = traderEntry.GetType();

                            // Look for InsuredItems on each trader
                            var insuredProp = entryType.GetProperty("InsuredItems") ?? entryType.GetProperty("Insured");
                            if (insuredProp != null)
                            {
                                Plugin.Log.LogInfo($"[KSG] Found InsuredItems on trader entry");
                                var traderInsured = insuredProp.GetValue(traderEntry);
                                if (traderInsured != null)
                                {
                                    int count = ExtractInsuredIdsFromObject(traderInsured, insuredIds);
                                    Plugin.Log.LogInfo($"[KSG] Extracted {count} insured IDs from trader");
                                }
                            }
                        }
                    }
                }
            }

            // Approach 3: Try InventoryInfo for insurance data
            var inventoryInfoProp = profileType.GetProperty("InventoryInfo", BindingFlags.Public | BindingFlags.Instance);
            if (inventoryInfoProp != null)
            {
                var inventoryInfo = inventoryInfoProp.GetValue(profile);
                if (inventoryInfo != null)
                {
                    var invType = inventoryInfo.GetType();
                    var invProps = invType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    // Look for insurance-related properties
                    foreach (var prop in invProps)
                    {
                        if (prop.Name.ToLower().Contains("insur"))
                        {
                            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found on InventoryInfo: {prop.Name} ({prop.PropertyType.Name})");
                            var value = prop.GetValue(inventoryInfo);
                            if (value != null)
                            {
                                int count = ExtractInsuredIdsFromObject(value, insuredIds);
                                if (count > 0)
                                {
                                    Plugin.Log.LogInfo($"[KSG] Found {count} insured items from InventoryInfo.{prop.Name}");
                                    return insuredIds;
                                }
                            }
                        }
                    }
                }
            }

            // Approach 4: Try InsuranceCompanyClass from session/singleton
            _insuranceCompany = GetInsuranceCompanyClass();

            if (_insuranceCompany != null)
            {
                // Cache the Insured method for performance
                _insuredMethod = _insuranceCompany.GetType().GetMethod("Insured", new[] { typeof(string) });

                if (_insuredMethod != null)
                {
                    Plugin.Log.LogInfo("[KSG] Found InsuranceCompanyClass.Insured() method - insurance exclusion will work");

                    // Also try to get the InsuredItems collection to build the ID set
                    var insuredItemsProp = _insuranceCompany.GetType().GetProperty("InsuredItems");
                    if (insuredItemsProp != null)
                    {
                        var insuredItems = insuredItemsProp.GetValue(_insuranceCompany);
                        if (insuredItems is System.Collections.IEnumerable enumerable)
                        {
                            foreach (var itemClass in enumerable)
                            {
                                if (itemClass == null) continue;

                                // ItemClass has an Id property
                                var idProp = itemClass.GetType().GetProperty("Id");
                                if (idProp != null)
                                {
                                    var id = idProp.GetValue(itemClass) as string;
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        insuredIds.Add(id);
                                    }
                                }
                            }
                        }
                    }

                    Plugin.Log.LogInfo($"[KSG] Found {insuredIds.Count} insured items from InsuranceCompanyClass");
                }
                else
                {
                    Plugin.Log.LogWarning("[KSG] InsuranceCompanyClass found but Insured() method not found");
                }
            }
            else
            {
                Plugin.Log.LogWarning("[KSG] Could not find InsuranceCompanyClass - insurance exclusion will not work");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KSG] Error accessing insurance system: {ex.Message}");
        }

        return insuredIds;
    }

    /// <summary>
    /// Recursively extracts insured item IDs from an insurance-related object.
    /// Handles various EFT insurance data structures.
    /// </summary>
    private int ExtractInsuredIdsFromObject(object obj, HashSet<string> insuredIds)
    {
        if (obj == null) return 0;
        int count = 0;

        try
        {
            var objType = obj.GetType();

            // If it's enumerable, iterate and extract IDs
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;

                    // Try to get Id from item
                    var itemType = item.GetType();
                    var idProp = itemType.GetProperty("Id") ?? itemType.GetProperty("id") ?? itemType.GetProperty("_id");
                    if (idProp != null)
                    {
                        var id = idProp.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(id))
                        {
                            insuredIds.Add(id);
                            count++;
                        }
                    }

                    // Also try ItemId
                    var itemIdProp = itemType.GetProperty("ItemId") ?? itemType.GetProperty("itemId");
                    if (itemIdProp != null)
                    {
                        var itemId = itemIdProp.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(itemId))
                        {
                            insuredIds.Add(itemId);
                            count++;
                        }
                    }

                    // If item has an Items collection, recurse
                    var itemsProp = itemType.GetProperty("Items") ?? itemType.GetProperty("items");
                    if (itemsProp != null)
                    {
                        var items = itemsProp.GetValue(item);
                        if (items != null)
                        {
                            count += ExtractInsuredIdsFromObject(items, insuredIds);
                        }
                    }
                }
            }

            // Check for InsuredItems property
            var insuredItemsProp = objType.GetProperty("InsuredItems") ?? objType.GetProperty("insuredItems");
            if (insuredItemsProp != null)
            {
                var insuredItems = insuredItemsProp.GetValue(obj);
                if (insuredItems != null)
                {
                    count += ExtractInsuredIdsFromObject(insuredItems, insuredIds);
                }
            }

            // Check for Items property
            var itemsP = objType.GetProperty("Items") ?? objType.GetProperty("items");
            if (itemsP != null)
            {
                var items = itemsP.GetValue(obj);
                if (items != null)
                {
                    count += ExtractInsuredIdsFromObject(items, insuredIds);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[KSG] Error extracting insured IDs: {ex.Message}");
        }

        return count;
    }

    /// <summary>
    /// Attempts to get the InsuranceCompanyClass from the game.
    /// This class manages insurance status for items.
    /// Based on dnSpy analysis: session.InsuranceCompany is the access path
    /// </summary>
    private object GetInsuranceCompanyClass()
    {
        try
        {
            // Path 1: Try through MainPlayer - access directly like RaidEndPatch does
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - GameWorld found, type: {gameWorld.GetType().FullName}");

                // Access MainPlayer directly - this works in RaidEndPatch and other code
                var mainPlayer = gameWorld.MainPlayer;
                if (mainPlayer != null)
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - MainPlayer found: {mainPlayer.GetType().Name}");

                        // Search MainPlayer for session or insurance properties
                        var playerProps = mainPlayer.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                        // Log some properties to help diagnose
                        var propNames = playerProps.Where(p => p.Name.Contains("Session") || p.Name.Contains("Insurance") || p.Name.Contains("Profile"))
                                                    .Select(p => p.Name).Take(10);
                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Player props with Session/Insurance/Profile: {string.Join(", ", propNames)}");

                        foreach (var prop in playerProps)
                        {
                            // Look for InsuranceCompany directly
                            if (prop.Name == "InsuranceCompany" || prop.PropertyType.Name == "InsuranceCompanyClass")
                            {
                                var value = prop.GetValue(mainPlayer);
                                if (value != null)
                                {
                                    Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany on MainPlayer.{prop.Name}");
                                    return value;
                                }
                            }

                            // Look for Session that might have InsuranceCompany
                            if (prop.Name.Contains("Session") || prop.PropertyType.Name.Contains("Session"))
                            {
                                try
                                {
                                    var session = prop.GetValue(mainPlayer);
                                    if (session != null)
                                    {
                                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found session on player: {session.GetType().Name}");
                                        var sessionProps = session.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                                        foreach (var sProp in sessionProps)
                                        {
                                            if (sProp.Name == "InsuranceCompany" || sProp.PropertyType.Name.Contains("Insurance"))
                                            {
                                                var insurance = sProp.GetValue(session);
                                                if (insurance != null)
                                                {
                                                    Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany via Player.Session.{sProp.Name}");
                                                    return insurance;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Session property access failed: {ex.Message}");
                                }
                            }
                        }

                        // Try through Profile owner
                        var profileProp = mainPlayer.GetType().GetProperty("Profile", BindingFlags.Public | BindingFlags.Instance);
                        if (profileProp != null)
                        {
                            var profile = profileProp.GetValue(mainPlayer);
                            if (profile != null)
                            {
                                // Profile doesn't have InsuranceCompany directly, but check anyway
                                var profileProps = profile.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                foreach (var pProp in profileProps)
                                {
                                    if (pProp.Name.Contains("Insurance") || pProp.PropertyType.Name.Contains("Insurance"))
                                    {
                                        try
                                        {
                                            var insurance = pProp.GetValue(profile);
                                            if (insurance != null)
                                            {
                                                Plugin.Log.LogInfo($"[KSG] Found insurance via Profile.{pProp.Name}");
                                                return insurance;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile property access failed: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                }
                else
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - MainPlayer is null");
                }
            }

            // Path 2: Try through ClientApplication singleton
            var clientAppType = Type.GetType("EFT.ClientApplication, Assembly-CSharp") ??
                               Type.GetType("ClientApplication, Assembly-CSharp");

            if (clientAppType != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Trying ClientApplication: {clientAppType.FullName}");

                var singletonType = typeof(Singleton<>).MakeGenericType(clientAppType);
                var instanceProp = singletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp != null)
                {
                    var clientApp = instanceProp.GetValue(null);
                    if (clientApp != null)
                    {
                        // Get all properties and log potential session/insurance ones
                        var appProps = clientApp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        var relevantProps = appProps.Where(p => p.Name.Contains("Session") || p.Name.Contains("Insurance"))
                                                     .Select(p => $"{p.Name}:{p.PropertyType.Name}").Take(10);
                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - ClientApp props: {string.Join(", ", relevantProps)}");

                        foreach (var prop in appProps)
                        {
                            if (prop.Name.Contains("Session") || prop.PropertyType.Name.Contains("Session"))
                            {
                                try
                                {
                                    var session = prop.GetValue(clientApp);
                                    if (session != null)
                                    {
                                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Got session from ClientApp: {session.GetType().Name}");

                                        var sessionProps = session.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                        foreach (var sProp in sessionProps)
                                        {
                                            if (sProp.Name == "InsuranceCompany" || sProp.PropertyType.Name.Contains("Insurance"))
                                            {
                                                var insurance = sProp.GetValue(session);
                                                if (insurance != null)
                                                {
                                                    Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany via ClientApp.Session.{sProp.Name}");
                                                    return insurance;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // CRITICAL-002 FIX: Log property access errors for diagnostics
                                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Property access error on {prop.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // Path 3: Direct search for InsuranceCompanyClass type
            var insuranceType = Type.GetType("InsuranceCompanyClass, Assembly-CSharp");
            if (insuranceType != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuranceCompanyClass type");

                // Check for static Instance property
                var instanceProp = insuranceType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (instanceProp != null)
                {
                    var instance = instanceProp.GetValue(null);
                    if (instance != null)
                    {
                        Plugin.Log.LogInfo($"[KSG] Found InsuranceCompanyClass.Instance");
                        return instance;
                    }
                }

                // Check for any static field that might hold an instance
                var staticFields = insuranceType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var field in staticFields)
                {
                    if (field.FieldType == insuranceType)
                    {
                        var instance = field.GetValue(null);
                        if (instance != null)
                        {
                            Plugin.Log.LogInfo($"[KSG] Found InsuranceCompanyClass via static field");
                            return instance;
                        }
                    }
                }
            }

            Plugin.Log.LogWarning("[KSG] Could not locate InsuranceCompanyClass through known paths");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KSG] Error searching for InsuranceCompanyClass: {ex.Message}");
            Plugin.Log.LogDebug($"[KSG] Stack: {ex.StackTrace}");
        }

        return null;
    }

    /// <summary>
    /// Checks if an item is insured using the cached InsuranceCompanyClass.
    /// </summary>
    /// <param name="itemId">The item ID to check</param>
    /// <returns>True if the item is insured, false otherwise</returns>
    public bool IsItemInsured(string itemId)
    {
        if (_insuranceCompany == null || _insuredMethod == null)
            return false;

        try
        {
            var result = _insuredMethod.Invoke(_insuranceCompany, new object[] { itemId });
            return result is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    // ========================================================================
    // Current Inventory Query (for Loss Preview and Value Calculator)
    // ========================================================================

    /// <summary>
    /// Gets a simplified list of current inventory items for loss preview and value calculation.
    /// </summary>
    /// <returns>List of current inventory items with basic info</returns>
    /// <remarks>
    /// This method intentionally returns ALL items in the player's equipment,
    /// ignoring the slot settings (IncludeTacticalVest, IncludeBackpack, etc.).
    /// This is because the loss preview needs to show what the player would
    /// lose if they died, which depends on what's in the snapshot, not what
    /// slots are enabled. The comparison with the snapshot handles the
    /// protection logic.
    /// </remarks>
    public List<CurrentInventoryItem> GetCurrentInventoryItems()
    {
        var items = new List<CurrentInventoryItem>();

        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld?.MainPlayer == null)
                return items;

            var player = gameWorld.MainPlayer;
            var equipment = player.Profile?.Inventory?.Equipment;
            if (equipment == null)
                return items;

            // Get all items recursively from equipment
            CollectItemsRecursively(equipment, items);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] Error getting current items: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Recursively collects items from a container.
    /// </summary>
    private void CollectItemsRecursively(Item container, List<CurrentInventoryItem> items)
    {
        if (container == null) return;

        try
        {
            // Get child items via reflection (GetAllItems or similar)
            var allItems = GetAllItemsFromContainer(container);
            if (allItems == null) return;

            foreach (var item in allItems)
            {
                if (item == null) continue;

                // Skip the container itself
                if (item == container) continue;

                var currentItem = new CurrentInventoryItem
                {
                    Id = GetItemId(item),
                    Tpl = GetItemTemplateId(item),
                    Name = GetItemName(item),
                    ShortName = GetItemShortName(item),
                    StackCount = GetStackCount(item),
                    IsFoundInRaid = GetFoundInRaid(item)
                };

                items.Add(currentItem);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] Error collecting items: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all items from a container using reflection.
    /// </summary>
    private IEnumerable<Item> GetAllItemsFromContainer(Item container)
    {
        try
        {
            // Try GetAllItems method
            var method = container.GetType().GetMethod("GetAllItems", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                return method.Invoke(container, null) as IEnumerable<Item>;
            }

            // Try ContainedItems property
            var prop = container.GetType().GetProperty("ContainedItems", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return prop.GetValue(container) as IEnumerable<Item>;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetAllItemsFromContainer failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Gets the item ID.
    /// </summary>
    private string GetItemId(Item item)
    {
        try
        {
            var idProp = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            return idProp?.GetValue(item)?.ToString() ?? "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetItemId failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Gets the item template ID.
    /// </summary>
    private string GetItemTemplateId(Item item)
    {
        try
        {
            var tplProp = item.GetType().GetProperty("TemplateId", BindingFlags.Public | BindingFlags.Instance);
            return tplProp?.GetValue(item)?.ToString() ?? "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetItemTemplateId failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Gets the item display name.
    /// </summary>
    private string GetItemName(Item item)
    {
        try
        {
            var nameProp = item.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (nameProp != null)
            {
                var nameObj = nameProp.GetValue(item);
                if (nameObj != null)
                {
                    // Try to get Localized property
                    var localizedProp = nameObj.GetType().GetProperty("Localized");
                    if (localizedProp != null)
                        return localizedProp.GetValue(nameObj)?.ToString() ?? "";
                    return nameObj.ToString();
                }
            }

            // Fallback to ShortName
            return GetItemShortName(item);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetItemName failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Gets the item short name.
    /// </summary>
    private string GetItemShortName(Item item)
    {
        try
        {
            var shortNameProp = item.GetType().GetProperty("ShortName", BindingFlags.Public | BindingFlags.Instance);
            if (shortNameProp != null)
            {
                var nameObj = shortNameProp.GetValue(item);
                if (nameObj != null)
                {
                    var localizedProp = nameObj.GetType().GetProperty("Localized");
                    if (localizedProp != null)
                        return localizedProp.GetValue(nameObj)?.ToString() ?? "";
                    return nameObj.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetItemShortName failed: {ex.Message}");
        }
        return "";
    }

    /// <summary>
    /// Gets the stack count for an item.
    /// </summary>
    private int GetStackCount(Item item)
    {
        try
        {
            var stackProp = item.GetType().GetProperty("StackObjectsCount", BindingFlags.Public | BindingFlags.Instance);
            if (stackProp != null)
            {
                var value = stackProp.GetValue(item);
                if (value is int intVal) return intVal;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetStackCount failed: {ex.Message}");
        }
        return 1;
    }

    /// <summary>
    /// Gets whether the item is found in raid.
    /// </summary>
    private bool GetFoundInRaid(Item item)
    {
        try
        {
            var firProp = item.GetType().GetProperty("SpawnedInSession", BindingFlags.Public | BindingFlags.Instance);
            if (firProp != null)
            {
                var value = firProp.GetValue(item);
                if (value is bool boolVal) return boolVal;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[InventoryService] GetFoundInRaid failed: {ex.Message}");
        }
        return false;
    }
}

/// <summary>
/// Simplified item data for loss preview and value calculation.
/// </summary>
public class CurrentInventoryItem
{
    /// <summary>Item ID.</summary>
    public string Id { get; set; } = "";

    /// <summary>Template/TPL ID.</summary>
    public string Tpl { get; set; } = "";

    /// <summary>Item display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Item short name.</summary>
    public string ShortName { get; set; } = "";

    /// <summary>Stack count.</summary>
    public int StackCount { get; set; } = 1;

    /// <summary>Found in raid status.</summary>
    public bool IsFoundInRaid { get; set; }
}
