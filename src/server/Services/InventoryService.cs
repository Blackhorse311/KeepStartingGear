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
using Blackhorse311.KeepStartingGear.Models;
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
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to capture inventory: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    // ========================================================================
    // Item Capture - Main Processing
    // ========================================================================

    /// <summary>
    /// Captures all items from the player's inventory by iterating through equipment slots.
    /// This method handles the top-level iteration and delegates to recursive methods.
    /// </summary>
    /// <param name="inventory">The player's Inventory object</param>
    /// <param name="allItems">List to populate with captured items</param>
    /// <param name="slotsToSave">List of slot names that should be included</param>
    /// <param name="emptySlots">List to populate with slot names that were enabled but empty</param>
    /// <remarks>
    /// <para>
    /// This method uses reflection to access the Equipment property and its slots
    /// because the exact structure may vary between EFT versions. It tries multiple
    /// approaches in order of preference:
    /// </para>
    /// <list type="number">
    ///   <item>AllSlots property - Contains all equipment slots (weapons, armor, containers)</item>
    ///   <item>GetAllSlots() method - Alternative method-based access</item>
    ///   <item>Slots property - Fallback for older EFT versions</item>
    ///   <item>ContainerSlots property - Last resort, may miss some equipment</item>
    /// </list>
    /// <para>
    /// The Equipment container itself is also captured first, as its ID is needed
    /// for proper parent-child relationships in the profile JSON.
    /// </para>
    /// <para>
    /// <b>Empty Slot Tracking:</b> When a slot is enabled in config but has no item,
    /// it's added to the emptySlots list. This ensures the server knows to clear
    /// items from that slot during restoration (preventing FIR items from being kept).
    /// </para>
    /// </remarks>
    private void CaptureAllItems(Inventory inventory, List<SerializedItem> allItems, List<string> slotsToSave, List<string> emptySlots)
    {
        try
        {
            // Access the Equipment property which contains all character equipment slots
            var equipment = inventory.Equipment;
            if (equipment == null)
            {
                Plugin.Log.LogError("Equipment is null!");
                return;
            }

            // ================================================================
            // Capture Equipment Container
            // The Equipment container itself must be captured first because
            // the server needs its ID for proper inventory structure
            // ================================================================
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

            Plugin.Log.LogDebug("Starting equipment slot enumeration...");
            Plugin.Log.LogDebug($"Equipment type: {equipment.GetType().FullName}");

            // ================================================================
            // Debug Logging - Equipment Structure
            // This helps diagnose issues when the slot access methods change
            // between EFT versions - ONLY logged when verbose capture is enabled
            // ================================================================
            if (Settings.VerboseCaptureLogging?.Value == true)
            {
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

            // ================================================================
            // Slot Access - Try Multiple Methods
            // Different EFT versions expose slots through different properties
            // ================================================================
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
                    slots = slotList;
                }
            }

            // Method 2: Try GetAllSlots() method as fallback
            if (slots == null)
            {
                var getAllSlotsMethod = equipment.GetType().GetMethod("GetAllSlots");
                if (getAllSlotsMethod != null)
                {
                    Plugin.Log.LogDebug("Trying GetAllSlots() method...");
                    slots = getAllSlotsMethod.Invoke(equipment, null) as IEnumerable<object>;
                    if (slots != null)
                    {
                        var slotList = slots.ToList();
                        Plugin.Log.LogDebug($"GetAllSlots() returned {slotList.Count} slots");
                        slots = slotList;
                    }
                }
            }

            // Method 3: Try Slots property as fallback
            if (slots == null)
            {
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
                        slots = slotList;
                    }
                }
            }

            // Method 4: Last resort - try ContainerSlots (may miss some equipment)
            if (slots == null)
            {
                Plugin.Log.LogWarning("Falling back to ContainerSlots (may miss some equipment)...");
                var containerSlotsProperty = equipment.GetType().GetProperty("ContainerSlots");
                if (containerSlotsProperty != null)
                {
                    slots = containerSlotsProperty.GetValue(equipment) as IEnumerable<object>;
                    if (slots != null)
                    {
                        var slotList = slots.ToList();
                        Plugin.Log.LogWarning($"ContainerSlots returned {slotList.Count} slots");
                        slots = slotList;
                    }
                }
            }

            // ================================================================
            // Process Each Slot
            // ================================================================
            if (slots != null)
            {
                // Define the set of TOP-LEVEL equipment slot names
                // These are the only slots that should be filtered by config
                // All other slots (mod_*, patron_*, Soft_armor_*, etc.) are nested and should
                // be captured automatically with their parent items
                var topLevelEquipmentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Scabbard",
                    "Headwear", "Earpiece", "FaceCover", "Eyewear", "ArmBand",
                    "TacticalVest", "ArmorVest", "Pockets", "Backpack", "SecuredContainer",
                    "Compass", "SpecialSlot1", "SpecialSlot2", "SpecialSlot3", "Dogtag"
                };

                // First pass: Collect all items and their IDs
                // This lets us track parent-child relationships
                var capturedItemIds = new HashSet<string>();
                var slotItems = new List<(string SlotName, Item Item)>();

                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug("First pass: Collecting slot items...");
                foreach (var slot in slots)
                {
                    var slotNameProp = slot.GetType().GetProperty("Name");
                    var slotName = slotNameProp?.GetValue(slot)?.ToString() ?? "Unknown";

                    var containedItemProp = slot.GetType().GetProperty("ContainedItem");
                    var containedItem = containedItemProp?.GetValue(slot) as Item;

                    if (containedItem != null)
                    {
                        slotItems.Add((slotName, containedItem));
                    }
                }
                Plugin.Log.LogDebug($"Found {slotItems.Count} items in slots");

                // Second pass: Process top-level equipment slots first
                // These are filtered by the config
                // Also track which slots have items for empty slot detection
                var slotsWithItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug("Second pass: Processing top-level equipment slots...");
                foreach (var (slotName, item) in slotItems)
                {
                    // Only process top-level equipment slots here
                    if (!topLevelEquipmentSlots.Contains(slotName))
                        continue;

                    // Track that this slot has an item
                    slotsWithItems.Add(slotName);

                    if (Settings.VerboseCaptureLogging?.Value == true)
                        Plugin.Log.LogDebug($"[SLOT DEBUG] Found item in slot '{slotName}': {item.Template?.NameLocalizationKey ?? item.TemplateId}");

                    // Check if this slot is enabled in configuration
                    bool slotEnabled = slotsToSave.Any(s => s.Equals(slotName, StringComparison.OrdinalIgnoreCase));

                    if (slotEnabled)
                    {
                        if (Settings.VerboseCaptureLogging?.Value == true)
                            Plugin.Log.LogDebug($"[SLOT DEBUG]   -> Capturing slot '{slotName}'");
                        var serializedItem = ConvertToSerializedItem(item);
                        if (serializedItem != null)
                        {
                            allItems.Add(serializedItem);
                            capturedItemIds.Add(item.Id);
                            if (Settings.VerboseCaptureLogging?.Value == true)
                                Plugin.Log.LogDebug($"[CAPTURE] Added top-level item: {serializedItem.Tpl} (ID: {serializedItem.Id})");
                        }
                    }
                    else
                    {
                        if (Settings.VerboseCaptureLogging?.Value == true)
                            Plugin.Log.LogDebug($"[SLOT DEBUG]   -> SKIPPING slot '{slotName}' (not in slotsToSave list!)");
                    }
                }

                // Track empty slots: slots that are enabled in config but have no item
                // This is critical for proper restoration - items looted into empty slots
                // during raid should be removed on death
                foreach (var enabledSlot in slotsToSave)
                {
                    // Only check top-level equipment slots
                    if (!topLevelEquipmentSlots.Contains(enabledSlot))
                        continue;

                    // If this enabled slot doesn't have an item, it's empty
                    if (!slotsWithItems.Contains(enabledSlot))
                    {
                        emptySlots.Add(enabledSlot);
                        if (Settings.VerboseCaptureLogging?.Value == true)
                            Plugin.Log.LogDebug($"[EMPTY SLOT] Slot '{enabledSlot}' is enabled but empty - will be cleared on restore");
                    }
                }

                if (emptySlots.Count > 0 && Settings.VerboseCaptureLogging?.Value == true)
                {
                    Plugin.Log.LogDebug($"[EMPTY SLOTS] Tracked {emptySlots.Count} empty slots: {string.Join(", ", emptySlots)}");
                }

                // Third pass: Process nested/mod slots
                // These should be captured if their parent was captured
                if (Settings.VerboseCaptureLogging?.Value == true)
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
                        if (topLevelEquipmentSlots.Contains(slotName))
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
                                if (Settings.VerboseCaptureLogging?.Value == true)
                                    Plugin.Log.LogDebug($"[CAPTURE] Added nested item from slot '{slotName}': {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {parentId})");
                            }
                        }
                    }
                }
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"Nested item passes completed: {passCount}");
                Plugin.Log.LogDebug($"Items captured from slots: {capturedItemIds.Count}");

                // ================================================================
                // Fourth pass: Capture GRID contents (backpack items, rig items, pocket items, mag ammo)
                // Grid items are NOT in AllSlots - we need to check each captured item's Grids property
                // ================================================================
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug("Fourth pass: Capturing grid contents from containers...");

                // Build a list of items we need to check for grids
                // We'll keep iterating until no new items are found
                var itemsToCheck = new List<Item>();

                // First, collect all the Item objects we've captured so far
                // We need to find them from the slot items list
                foreach (var (slotName, item) in slotItems)
                {
                    if (capturedItemIds.Contains(item.Id))
                    {
                        itemsToCheck.Add(item);
                    }
                }

                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"Checking {itemsToCheck.Count} captured items for grid contents...");

                int gridItemsFound = 0;
                bool foundMore = true;
                int gridPass = 0;
                bool verbose = Settings.VerboseCaptureLogging?.Value == true;

                while (foundMore && gridPass < 10)
                {
                    foundMore = false;
                    gridPass++;
                    var newItemsToCheck = new List<Item>();

                    foreach (var containerItem in itemsToCheck)
                    {
                        if (containerItem == null) continue;

                        var itemType = containerItem.GetType();

                        // Debug: Log every item we're checking on first pass
                        if (gridPass == 1 && verbose)
                        {
                            Plugin.Log.LogDebug($"[GRID DEBUG] Checking item: {containerItem.TemplateId} Type: {itemType.Name}");
                        }

                        // Check for Grids field (containers like backpacks, rigs, pockets)
                        // Note: In EFT, Grids is a PUBLIC FIELD, not a property!
                        var gridsField = itemType.GetField("Grids", BindingFlags.Public | BindingFlags.Instance);
                        if (gridsField != null)
                        {
                            if (gridPass == 1 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]   Has Grids field");

                            var grids = gridsField.GetValue(containerItem) as System.Collections.IEnumerable;
                            if (grids != null)
                            {
                                int gridCount = 0;
                                foreach (var grid in grids)
                                {
                                    gridCount++;
                                    if (grid == null) continue;

                                    // Get grid ID for logging
                                    var gridIdProp = grid.GetType().GetProperty("ID");
                                    var gridId = gridIdProp?.GetValue(grid)?.ToString() ?? "unknown";

                                    if (gridPass == 1 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]   Grid {gridCount}: ID={gridId}, Type={grid.GetType().Name}");

                                    // Try ItemCollection first (contains KeyValuePair<Item, LocationInGrid>)
                                    var itemCollectionProp = grid.GetType().GetProperty("ItemCollection");
                                    var containedItemsProp = grid.GetType().GetProperty("ContainedItems");
                                    var collectionProp = itemCollectionProp ?? containedItemsProp;

                                    if (collectionProp != null)
                                    {
                                        var collection = collectionProp.GetValue(grid) as System.Collections.IEnumerable;
                                        if (collection != null)
                                        {
                                            int itemCount = 0;
                                            foreach (var kvp in collection)
                                            {
                                                itemCount++;
                                                // kvp is KeyValuePair<Item, LocationInGrid>
                                                var kvpType = kvp.GetType();
                                                var keyProp = kvpType.GetProperty("Key");
                                                var valueProp = kvpType.GetProperty("Value");

                                                if (keyProp != null && valueProp != null)
                                                {
                                                    var childItem = keyProp.GetValue(kvp) as Item;
                                                    var locationInGrid = valueProp.GetValue(kvp);

                                                    if (childItem != null && !capturedItemIds.Contains(childItem.Id))
                                                    {
                                                        var serializedItem = ConvertToSerializedItem(childItem);
                                                        if (serializedItem != null)
                                                        {
                                                            // Extract location from LocationInGrid
                                                            if (locationInGrid != null)
                                                            {
                                                                var locType = locationInGrid.GetType();

                                                                // Try to get x, y, r values - could be properties or fields
                                                                object xVal = null, yVal = null, rVal = null;

                                                                var xPropInfo = locType.GetProperty("x");
                                                                var xFieldInfo = locType.GetField("x");
                                                                if (xPropInfo != null)
                                                                    xVal = xPropInfo.GetValue(locationInGrid);
                                                                else if (xFieldInfo != null)
                                                                    xVal = xFieldInfo.GetValue(locationInGrid);

                                                                var yPropInfo = locType.GetProperty("y");
                                                                var yFieldInfo = locType.GetField("y");
                                                                if (yPropInfo != null)
                                                                    yVal = yPropInfo.GetValue(locationInGrid);
                                                                else if (yFieldInfo != null)
                                                                    yVal = yFieldInfo.GetValue(locationInGrid);

                                                                var rPropInfo = locType.GetProperty("r");
                                                                var rFieldInfo = locType.GetField("r");
                                                                if (rPropInfo != null)
                                                                    rVal = rPropInfo.GetValue(locationInGrid);
                                                                else if (rFieldInfo != null)
                                                                    rVal = rFieldInfo.GetValue(locationInGrid);

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
                                                            gridItemsFound++;
                                                            foundMore = true;

                                                            if (verbose)
                                                                Plugin.Log.LogDebug($"[CAPTURE] Added grid item: {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Grid: {gridId})");
                                                        }
                                                    }
                                                }
                                            }
                                            if (gridPass == 1 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]     Grid has {itemCount} items via ItemCollection");
                                        }
                                    }
                                    // Fallback to Items property if ItemCollection not available
                                    else
                                    {
                                        var gridItemsProperty = grid.GetType().GetProperty("Items");
                                        if (gridItemsProperty != null)
                                        {
                                            var gridItems = gridItemsProperty.GetValue(grid) as System.Collections.IEnumerable;
                                            if (gridItems != null)
                                            {
                                                int itemCount = 0;
                                                foreach (var gridItem in gridItems)
                                                {
                                                    itemCount++;
                                                    if (gridItem is Item childItem && !capturedItemIds.Contains(childItem.Id))
                                                    {
                                                        var serializedItem = ConvertToSerializedItem(childItem);
                                                        if (serializedItem != null)
                                                        {
                                                            allItems.Add(serializedItem);
                                                            capturedItemIds.Add(childItem.Id);
                                                            newItemsToCheck.Add(childItem);
                                                            gridItemsFound++;
                                                            foundMore = true;

                                                            if (verbose)
                                                                Plugin.Log.LogDebug($"[CAPTURE] Added grid item (no location): {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Grid: {gridId})");
                                                        }
                                                    }
                                                }
                                                if (gridPass == 1 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]     Grid has {itemCount} items via Items property");
                                            }
                                            else if (gridPass == 1 && verbose)
                                            {
                                                Plugin.Log.LogDebug($"[GRID DEBUG]     Items property returned null");
                                            }
                                        }
                                    }
                                }
                                if (gridPass == 1 && gridCount == 0 && verbose) Plugin.Log.LogDebug($"[GRID DEBUG]   Grids collection is empty!");
                            }
                            else if (gridPass == 1 && verbose)
                            {
                                Plugin.Log.LogDebug($"[GRID DEBUG]   Grids field returned null");
                            }
                        }
                        else if (gridPass == 1 && verbose)
                        {
                            // Try property as fallback (some items might use property)
                            var gridsProperty = itemType.GetProperty("Grids");
                            if (gridsProperty != null)
                            {
                                Plugin.Log.LogDebug($"[GRID DEBUG]   Has Grids property (not field)");
                            }
                        }

                        // Check for ammo boxes - they might use StackSlots like magazines
                        // Ammo boxes have template IDs like 5737287724597765e1625ae2, 5737339e2459776af261abeb
                        var tplStr = containerItem.TemplateId.ToString();
                        if (tplStr.StartsWith("5737") || itemType.Name.Contains("Ammo") || itemType.Name.Contains("Box"))
                        {
                            if (verbose)
                            {
                                Plugin.Log.LogDebug($"[AMMO BOX] Checking potential ammo box: {containerItem.TemplateId} Type: {itemType.Name}");

                                // Log all properties and fields for debugging
                                var allProps = itemType.GetProperties().Select(p => p.Name).ToArray();
                                var allFields = itemType.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();
                                Plugin.Log.LogDebug($"[AMMO BOX] Properties: {string.Join(", ", allProps)}");
                                Plugin.Log.LogDebug($"[AMMO BOX] Fields: {string.Join(", ", allFields)}");
                            }

                            // Try StackSlot
                            var stackSlotProp = itemType.GetProperty("StackSlot");
                            if (stackSlotProp != null)
                            {
                                var stackSlot = stackSlotProp.GetValue(containerItem);
                                if (stackSlot != null)
                                {
                                    if (verbose) Plugin.Log.LogDebug($"[AMMO BOX] Found StackSlot: {stackSlot.GetType().Name}");

                                    // Get items from StackSlot
                                    var itemsProp = stackSlot.GetType().GetProperty("Items");
                                    if (itemsProp != null)
                                    {
                                        var items = itemsProp.GetValue(stackSlot) as IEnumerable<Item>;
                                        if (items != null)
                                        {
                                            foreach (var ammoItem in items.ToList())
                                            {
                                                if (!capturedItemIds.Contains(ammoItem.Id))
                                                {
                                                    var serializedItem = ConvertToSerializedItem(ammoItem);
                                                    if (serializedItem != null)
                                                    {
                                                        allItems.Add(serializedItem);
                                                        capturedItemIds.Add(ammoItem.Id);
                                                        gridItemsFound++;
                                                        foundMore = true;
                                                        if (verbose)
                                                            Plugin.Log.LogDebug($"[AMMO BOX] Captured ammo from box: {ammoItem.TemplateId} Stack={ammoItem.StackObjectsCount}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Check for Cartridges property (magazines AND ammo boxes)
                        // AmmoBox type has Cartridges property just like magazines
                        var cartridgesProp = itemType.GetProperty("Cartridges");
                        if (cartridgesProp != null)
                        {
                            var cartridges = cartridgesProp.GetValue(containerItem);
                            if (cartridges != null)
                            {
                                if (verbose) Plugin.Log.LogDebug($"[CARTRIDGES] Found Cartridges on {itemType.Name}: {cartridges.GetType().Name}");

                                // Try to get Items property from cartridges
                                var itemsProp = cartridges.GetType().GetProperty("Items");
                                if (itemsProp != null)
                                {
                                    var ammoItems = itemsProp.GetValue(cartridges) as IEnumerable<Item>;
                                    if (ammoItems != null)
                                    {
                                        int ammoCount = 0;
                                        int cartridgePosition = 0; // Track position for LocationIndex

                                        foreach (var ammo in ammoItems)
                                        {
                                            ammoCount++;
                                            if (ammo != null && !capturedItemIds.Contains(ammo.Id))
                                            {
                                                var serializedItem = ConvertToSerializedItem(ammo);
                                                if (serializedItem != null)
                                                {
                                                    // CRITICAL: Set LocationIndex for magazine cartridges
                                                    // SPT profiles use integer locations for cartridges (0, 1, 2, etc.)
                                                    // This fixes the "expects an item at position X" error
                                                    serializedItem.LocationIndex = cartridgePosition;

                                                    allItems.Add(serializedItem);
                                                    capturedItemIds.Add(ammo.Id);
                                                    gridItemsFound++;
                                                    foundMore = true;

                                                    if (verbose)
                                                        Plugin.Log.LogDebug($"[CARTRIDGES] Captured ammo from {itemType.Name}: {serializedItem.Tpl} (ID: {serializedItem.Id}, Parent: {containerItem.Id}, Stack: {ammo.StackObjectsCount}, Position: {cartridgePosition})");
                                                }
                                            }
                                            cartridgePosition++;
                                        }
                                        if (verbose) Plugin.Log.LogDebug($"[CARTRIDGES] {itemType.Name} contained {ammoCount} ammo items with positions 0-{cartridgePosition - 1}");
                                    }
                                }
                                else if (verbose)
                                {
                                    Plugin.Log.LogDebug($"[CARTRIDGES] No Items property on Cartridges. Available: {string.Join(", ", cartridges.GetType().GetProperties().Select(p => p.Name).Take(10))}");
                                }
                            }
                        }
                    }

                    // Also check for SLOTS on grid items (e.g., armor plates in armor stored in backpack)
                    foreach (var gridItem in newItemsToCheck.ToList())
                    {
                        if (gridItem == null) continue;
                        var gridItemType2 = gridItem.GetType();

                        // Check for AllSlots property (armor, weapons, etc. have slots)
                        var gridItemSlotsProperty = gridItemType2.GetProperty("AllSlots");
                        if (gridItemSlotsProperty != null)
                        {
                            var gridItemSlots = gridItemSlotsProperty.GetValue(gridItem) as System.Collections.IEnumerable;
                            if (gridItemSlots != null)
                            {
                                foreach (var gridSlot in gridItemSlots)
                                {
                                    if (gridSlot == null) continue;

                                    // Get ContainedItem from slot
                                    var containedItemProp2 = gridSlot.GetType().GetProperty("ContainedItem");
                                    if (containedItemProp2 != null)
                                    {
                                        var slotItem = containedItemProp2.GetValue(gridSlot) as Item;
                                        if (slotItem != null && !capturedItemIds.Contains(slotItem.Id))
                                        {
                                            // Get slot ID for logging
                                            var slotIdProp2 = gridSlot.GetType().GetProperty("ID");
                                            var slotId2 = slotIdProp2?.GetValue(gridSlot)?.ToString() ?? "unknown";

                                            var serializedSlotItem = ConvertToSerializedItem(slotItem);
                                            if (serializedSlotItem != null)
                                            {
                                                allItems.Add(serializedSlotItem);
                                                capturedItemIds.Add(slotItem.Id);
                                                newItemsToCheck.Add(slotItem);
                                                gridItemsFound++;
                                                foundMore = true;

                                                if (verbose)
                                                    Plugin.Log.LogDebug($"[CAPTURE] Added slot item from grid item: {serializedSlotItem.Tpl} (Slot: {slotId2}, Parent: {gridItem.Id})");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Add newly found items to check in next pass (they might be containers too)
                    itemsToCheck = newItemsToCheck;
                }

                if (verbose)
                    Plugin.Log.LogDebug($"Grid items captured: {gridItemsFound} in {gridPass} passes");

                Plugin.Log.LogDebug($"Total captured items: {capturedItemIds.Count}");
            }
            else
            {
                // ================================================================
                // Error Case - No Slot Access Method Worked
                // Dump debug info to help diagnose the issue
                // ================================================================
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

            Plugin.Log.LogDebug($"Total items captured: {allItems.Count}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error capturing items: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
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
            var gridsField = itemType.GetField("Grids", BindingFlags.Public | BindingFlags.Instance);
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

                        var gridItemsProperty = grid.GetType().GetProperty("Items");
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

            // Try to get Slots property via reflection
            var slotsProperty = itemType.GetProperty("Slots");
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

                        // Get slot name for logging
                        var slotNameProp = slot.GetType().GetProperty("Name");
                        var slotName = slotNameProp?.GetValue(slot)?.ToString() ?? "unknown";

                        var containedItemProperty = slot.GetType().GetProperty("ContainedItem");
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

                // Try to get the Cartridges property via reflection
                var cartridgesProp = itemType.GetProperty("Cartridges");
                if (cartridgesProp != null)
                {
                    var cartridges = cartridgesProp.GetValue(item);
                    if (cartridges != null)
                    {
                        Plugin.Log.LogDebug($"[MAGAZINE] Found Cartridges property, type: {cartridges.GetType().Name}");

                        // Cartridges is typically a StackSlot or similar container
                        // Try to get Items property from it
                        var itemsProp = cartridges.GetType().GetProperty("Items");
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
            if (Settings.ProtectFIRItems.Value)
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
            // ================================================================
            if (Settings.ExcludeInsuredItems.Value)
            {
                try
                {
                    // Check if item has insurance via reflection (property name may vary)
                    var itemType = item.GetType();
                    var isInsuredProp = itemType.GetProperty("IsInsured");
                    if (isInsuredProp != null)
                    {
                        var isInsured = (bool?)isInsuredProp.GetValue(item);
                        if (isInsured == true)
                        {
                            if (Settings.EnableDebugMode.Value)
                            {
                                Plugin.Log.LogDebug($"[INSURANCE SKIP] Skipping insured item: {item.TemplateId}");
                            }
                            return null;
                        }
                    }
                }
                catch
                {
                    // If reflection fails, include the item anyway
                }
            }

            // Create the basic serialized item with ID and template
            var serialized = new SerializedItem
            {
                Id = item.Id,
                Tpl = item.TemplateId.ToString()
            };

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
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < 100;

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

                    var locationProp = addressType.GetProperty("LocationInGrid")
                                    ?? addressType.GetProperty("Location");

                    if (locationProp != null)
                    {
                        var location = locationProp.GetValue(item.CurrentAddress);
                        if (location != null)
                        {
                            Plugin.Log.LogDebug($"[LOCATION] Found location object for {item.TemplateId}: {location.GetType().Name}");
                            var locationType = location.GetType();

                            // Try both lowercase and uppercase property names
                            var xProp = locationType.GetProperty("x") ?? locationType.GetProperty("X");
                            var yProp = locationType.GetProperty("y") ?? locationType.GetProperty("Y");
                            var rProp = locationType.GetProperty("r") ?? locationType.GetProperty("R");

                            var x = xProp?.GetValue(location);
                            var y = yProp?.GetValue(location);
                            var r = rProp?.GetValue(location);

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
                            Plugin.Log.LogDebug($"[LOCATION] LocationInGrid/Location property exists but returned null for {item.TemplateId}");
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

                            // Also try to find any property with "Location" in the name or that returns a struct/class with x/y
                            foreach (var prop in addressType.GetProperties())
                            {
                                try
                                {
                                    var val = prop.GetValue(item.CurrentAddress);
                                    if (val != null)
                                    {
                                        var valType = val.GetType();
                                        var xProp = valType.GetProperty("x") ?? valType.GetProperty("X");
                                        var yProp = valType.GetProperty("y") ?? valType.GetProperty("Y");
                                        if (xProp != null && yProp != null)
                                        {
                                            var x = xProp.GetValue(val);
                                            var y = yProp.GetValue(val);
                                            Plugin.Log.LogDebug($"[LOCATION] FOUND! Property '{prop.Name}' has x={x}, y={y}");

                                            // Use this location!
                                            var rProp = valType.GetProperty("r") ?? valType.GetProperty("R");
                                            var r = rProp?.GetValue(val);
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
                                catch { /* ignore errors during property inspection */ }
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
            catch
            {
                // Foldable capture failed - not critical
            }

            // ================================================================
            // Capture MedKit HP (IFAK, AFAK, Grizzly, Surv12, CMS, etc.)
            // EFT stores current HP as a field on MedKitComponent (not a property)
            // This applies to ALL medical items including surgical kits
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Access the Components field to find MedKitComponent
                // NOTE: Check ALL items, not just those with "MedKit" in name
                // Surgical kits (Surv12, CMS) also use MedKitComponent but have different type names
                var componentsField = itemType.GetField("Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            if (comp != null && comp.GetType().Name.Contains("MedKit"))
                            {
                                // HpResource is a FIELD on the component, not a property
                                var hpField = comp.GetType().GetField("HpResource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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

                // Access Components field to find RepairableComponent
                var componentsField = itemType.GetField("Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            if (comp != null && comp.GetType().Name.Contains("Repairable"))
                            {
                                // Durability and MaxDurability are FIELDS on the component, not properties
                                var durField = comp.GetType().GetField("Durability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var maxDurField = comp.GetType().GetField("MaxDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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
            // Capture Resource value (fuel cans, etc.)
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var resourceProp = itemType.GetProperty("ResourceComponent") ??
                                   itemType.GetProperty("Resource");
                if (resourceProp != null)
                {
                    var resourceValue = resourceProp.GetValue(item);
                    if (resourceValue != null)
                    {
                        var valueProp = resourceValue.GetType().GetProperty("Value");
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
            catch { /* Resource capture failed - not critical */ }

            // ================================================================
            // Capture FoodDrink value
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var foodDrinkProp = itemType.GetProperty("FoodDrinkComponent") ??
                                    itemType.GetProperty("FoodDrink");
                if (foodDrinkProp != null)
                {
                    var foodDrinkValue = foodDrinkProp.GetValue(item);
                    if (foodDrinkValue != null)
                    {
                        var hpProp = foodDrinkValue.GetType().GetProperty("HpPercent");
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
            catch { /* FoodDrink capture failed - not critical */ }

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
                    // Try to find DogtagComponent in the item's components
                    var componentsField = itemType.GetField("Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                if (comp != null && comp.GetType().Name.Contains("Dogtag"))
                                {
                                    var compType = comp.GetType();
                                    upd.Dogtag = new UpdDogtag();

                                    // Extract all dogtag properties via reflection
                                    var accountIdField = compType.GetField("AccountId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var profileIdField = compType.GetField("ProfileId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var nicknameField = compType.GetField("Nickname", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var sideField = compType.GetField("Side", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var levelField = compType.GetField("Level", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var timeField = compType.GetField("Time", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var statusField = compType.GetField("Status", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var killerAccountIdField = compType.GetField("KillerAccountId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var killerProfileIdField = compType.GetField("KillerProfileId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var killerNameField = compType.GetField("KillerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var weaponNameField = compType.GetField("WeaponName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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
            // Capture Key uses remaining
            // Some keys have limited uses before they break
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Check if this is a key item
                if (itemType.Name.Contains("Key"))
                {
                    var componentsField = itemType.GetField("Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                if (comp != null && comp.GetType().Name.Contains("Key"))
                                {
                                    var numberOfUsagesField = comp.GetType().GetField("NumberOfUsages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
            catch { /* Key capture failed - not critical */ }

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
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < 100;

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
            Plugin.Log.LogDebug($"Could not set stack count: {ex.Message}");
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
}
