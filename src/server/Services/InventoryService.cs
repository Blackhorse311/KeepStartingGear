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
// Player Input -> CaptureInventory() -> CaptureAllItems() -> Helper Methods
//   -> GridCaptureStrategy / AmmoCaptureStrategy -> ItemSerializer.Convert()
//   -> InventorySnapshot -> JSON file
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
    // Extracted Service Objects (CRIT-1 refactor)
    // ========================================================================

    private readonly InsuranceFilter _insuranceFilter;
    private readonly ItemSerializer _serializer;
    private readonly AmmoCaptureStrategy _ammoCapture;
    private readonly GridCaptureStrategy _gridCapture;

    /// <summary>
    /// Constructor - sets up the singleton instance and initializes extracted services.
    /// Called once during plugin initialization.
    /// </summary>
    public InventoryService()
    {
        Instance = this;
        _insuranceFilter = new InsuranceFilter();
        _serializer = new ItemSerializer(_insuranceFilter);
        _ammoCapture = new AmmoCaptureStrategy(_serializer);
        _gridCapture = new GridCaptureStrategy(_serializer, _ammoCapture);
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
            _insuranceFilter.BuildInsuredIdSet(profile);

            // DEBUG: Log insurance check status
            Plugin.Log.LogInfo($"[KSG] Exclude insured items: {Settings.ExcludeInsuredItems.Value}");

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
                _insuranceFilter.Clear();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to capture inventory: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            // Clear cached state on error too (REL-005)
            _insuranceFilter.Clear();
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
            var gridItemsFound = _gridCapture.CaptureGridContents(slotItems, allItems, capturedItemIds);

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
            var equipmentSerialized = _serializer.Convert(equipmentItem);
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

                var serializedItem = _serializer.Convert(item);
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
                    var serializedItem = _serializer.Convert(item);
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

    // Grid capture, ammo capture, and item serialization are handled by:
    // GridCaptureStrategy, AmmoCaptureStrategy, and ItemSerializer respectively.
    // See _gridCapture, _ammoCapture, _serializer fields above.

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
