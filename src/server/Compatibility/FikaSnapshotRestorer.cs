// Keep Starting Gear - FIKA Snapshot Restorer
//
// This class handles client-side inventory restoration when FIKA is installed.
// It restores the player's inventory from a snapshot BEFORE FIKA serializes
// the "dead" inventory state.
//
// IMPORTANT: This runs CLIENT-SIDE, not server-side!
// The restoration must happen before FIKA's SavePlayer() serializes the inventory.
//
// NOTE: Full client-side item creation is complex and may not work perfectly.
// This is experimental code that logs extensively for debugging.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Blackhorse311.KeepStartingGear.Models;
using Blackhorse311.KeepStartingGear.Services;

namespace Blackhorse311.KeepStartingGear.Compatibility
{
    /// <summary>
    /// Handles client-side inventory restoration for FIKA compatibility.
    /// Restores player inventory from snapshot before FIKA sends death data to server.
    /// </summary>
    public class FikaSnapshotRestorer
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("KSG-FikaRestorer");

        private readonly SnapshotManager _snapshotManager;

        public FikaSnapshotRestorer(SnapshotManager snapshotManager)
        {
            Logger.LogInfo("[FIKA-RESTORER] FikaSnapshotRestorer created.");
            Logger.LogInfo($"[FIKA-RESTORER] SnapshotManager: {snapshotManager != null}");
            _snapshotManager = snapshotManager;
        }

        /// <summary>
        /// Attempts to restore the player's inventory from a snapshot.
        /// Called when the player dies BEFORE FIKA processes the death.
        /// </summary>
        /// <param name="player">The player who died</param>
        /// <returns>True if restoration was successful</returns>
        public bool TryRestoreInventory(Player player)
        {
            Logger.LogInfo("[FIKA-RESTORER] ========================================");
            Logger.LogInfo("[FIKA-RESTORER] Starting inventory restoration attempt");
            Logger.LogInfo("[FIKA-RESTORER] ========================================");

            if (player == null)
            {
                Logger.LogError("[FIKA-RESTORER] Cannot restore inventory - player is null!");
                return false;
            }

            try
            {
                var profileId = player.ProfileId;
                Logger.LogInfo($"[FIKA-RESTORER] Player ProfileId: {profileId}");
                Logger.LogInfo($"[FIKA-RESTORER] Player Nickname: {player.Profile?.Nickname ?? "null"}");
                Logger.LogInfo($"[FIKA-RESTORER] Player Side: {player.Profile?.Info?.Side.ToString() ?? "unknown"}");

                // Check if we should restore (only for PMC deaths)
                var side = player.Profile?.Info?.Side.ToString() ?? "";
                if (side == "Savage")
                {
                    Logger.LogInfo("[FIKA-RESTORER] Player is Scav - skipping restoration (Scavs not supported).");
                    return false;
                }

                // Load snapshot
                Logger.LogInfo("[FIKA-RESTORER] Looking for snapshot...");
                Logger.LogInfo($"[FIKA-RESTORER]   Snapshot path: {Plugin.GetDataPath()}");

                var snapshot = _snapshotManager.LoadSnapshot(profileId);
                Logger.LogInfo($"[FIKA-RESTORER]   LoadSnapshot(profileId) result: {snapshot != null}");

                if (snapshot == null)
                {
                    Logger.LogInfo("[FIKA-RESTORER]   No snapshot for profileId, trying most recent...");
                    snapshot = _snapshotManager.GetMostRecentSnapshot();
                    Logger.LogInfo($"[FIKA-RESTORER]   GetMostRecentSnapshot() result: {snapshot != null}");
                }

                if (snapshot == null)
                {
                    Logger.LogWarning("[FIKA-RESTORER] No snapshot found for player!");
                    Logger.LogWarning("[FIKA-RESTORER] Player must press the snapshot keybind during raid to save gear.");
                    return false;
                }

                // Log snapshot details
                Logger.LogInfo("[FIKA-RESTORER] ----------------------------------------");
                Logger.LogInfo("[FIKA-RESTORER] SNAPSHOT FOUND!");
                Logger.LogInfo($"[FIKA-RESTORER]   SessionId: {snapshot.SessionId}");
                Logger.LogInfo($"[FIKA-RESTORER]   Timestamp: {snapshot.Timestamp}");
                Logger.LogInfo($"[FIKA-RESTORER]   Items count: {snapshot.Items?.Count ?? 0}");
                Logger.LogInfo($"[FIKA-RESTORER]   Empty slots: {string.Join(", ", snapshot.EmptySlots ?? new List<string>())}");
                Logger.LogInfo("[FIKA-RESTORER] ----------------------------------------");

                // Log all items in snapshot
                if (snapshot.Items != null)
                {
                    Logger.LogInfo("[FIKA-RESTORER] Snapshot items:");
                    foreach (var item in snapshot.Items.Take(20)) // Limit to first 20 for readability
                    {
                        Logger.LogInfo($"[FIKA-RESTORER]   - {item.Id}: Tpl={item.Tpl}, Slot={item.SlotId}, Parent={item.ParentId}");
                    }
                    if (snapshot.Items.Count > 20)
                    {
                        Logger.LogInfo($"[FIKA-RESTORER]   ... and {snapshot.Items.Count - 20} more items");
                    }
                }

                // Attempt restoration
                Logger.LogInfo("[FIKA-RESTORER] ----------------------------------------");
                Logger.LogInfo("[FIKA-RESTORER] Starting inventory restoration...");
                bool success = RestoreInventoryFromSnapshot(player, snapshot);

                if (success)
                {
                    Logger.LogInfo("[FIKA-RESTORER] ----------------------------------------");
                    Logger.LogInfo("[FIKA-RESTORER] INVENTORY RESTORATION SUCCESSFUL!");
                    Logger.LogInfo("[FIKA-RESTORER] ----------------------------------------");

                    // Clear the snapshot after successful restoration
                    Logger.LogInfo($"[FIKA-RESTORER] Clearing snapshot for {profileId}...");
                    _snapshotManager.ClearSnapshot(profileId);
                    Logger.LogInfo("[FIKA-RESTORER] Snapshot cleared.");
                }
                else
                {
                    Logger.LogWarning("[FIKA-RESTORER] ----------------------------------------");
                    Logger.LogWarning("[FIKA-RESTORER] Inventory restoration FAILED!");
                    Logger.LogWarning("[FIKA-RESTORER] ----------------------------------------");
                }

                Logger.LogInfo("[FIKA-RESTORER] ========================================");
                return success;
            }
            catch (Exception ex)
            {
                Logger.LogError("[FIKA-RESTORER] ========================================");
                Logger.LogError($"[FIKA-RESTORER] CRITICAL ERROR during restoration: {ex.Message}");
                Logger.LogError($"[FIKA-RESTORER] Exception type: {ex.GetType().FullName}");
                Logger.LogError($"[FIKA-RESTORER] Stack trace:\n{ex.StackTrace}");
                Logger.LogError("[FIKA-RESTORER] ========================================");
                return false;
            }
        }

        /// <summary>
        /// Restores the player's inventory from a snapshot.
        /// This is the client-side equivalent of what the server does.
        /// </summary>
        private bool RestoreInventoryFromSnapshot(Player player, InventorySnapshot snapshot)
        {
            Logger.LogInfo("[FIKA-RESTORE] Starting RestoreInventoryFromSnapshot...");

            // Validate player inventory
            if (player.Profile?.Inventory == null)
            {
                Logger.LogError("[FIKA-RESTORE] Player.Profile.Inventory is null!");
                return false;
            }

            var inventory = player.Profile.Inventory;
            Logger.LogInfo($"[FIKA-RESTORE] Player inventory type: {inventory.GetType().FullName}");

            // Get equipment via reflection to avoid type compatibility issues
            Logger.LogInfo("[FIKA-RESTORE] Getting Equipment property...");
            var equipmentProp = inventory.GetType().GetProperty("Equipment");
            if (equipmentProp == null)
            {
                Logger.LogError("[FIKA-RESTORE] Could not find Equipment property on inventory!");
                LogTypeMembers(inventory.GetType(), "Inventory");
                return false;
            }

            var equipment = equipmentProp.GetValue(inventory);
            if (equipment == null)
            {
                Logger.LogError("[FIKA-RESTORE] Equipment property returned null!");
                return false;
            }

            Logger.LogInfo($"[FIKA-RESTORE] Equipment type: {equipment.GetType().FullName}");

            try
            {
                // Get the equipment container ID via reflection
                Logger.LogInfo("[FIKA-RESTORE] Getting equipment ID...");
                var idProp = equipment.GetType().GetProperty("Id");
                if (idProp == null)
                {
                    Logger.LogError("[FIKA-RESTORE] Could not find Id property on equipment!");
                    LogTypeMembers(equipment.GetType(), "Equipment");
                    return false;
                }

                string equipmentId = idProp.GetValue(equipment)?.ToString();
                Logger.LogInfo($"[FIKA-RESTORE] Current equipment ID: {equipmentId}");

                // Find equipment container ID in snapshot
                string snapshotEquipmentId = snapshot.Items?
                    .FirstOrDefault(i => i.Tpl == "55d7217a4bdc2d86028b456d")?.Id;

                Logger.LogInfo($"[FIKA-RESTORE] Snapshot equipment ID: {snapshotEquipmentId}");

                if (string.IsNullOrEmpty(snapshotEquipmentId))
                {
                    Logger.LogError("[FIKA-RESTORE] Could not find equipment container (55d7217a4bdc2d86028b456d) in snapshot!");
                    return false;
                }

                // Get slots that were captured in snapshot
                var snapshotSlotIds = new HashSet<string>(snapshot.Items
                    .Where(i => i.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(i.SlotId))
                    .Select(i => i.SlotId));

                // Include empty slots from snapshot metadata
                var emptySlotIds = snapshot.EmptySlots ?? new List<string>();

                Logger.LogInfo($"[FIKA-RESTORE] Snapshot has {snapshotSlotIds.Count} equipment slots with items:");
                foreach (var slotId in snapshotSlotIds)
                {
                    Logger.LogInfo($"[FIKA-RESTORE]   - {slotId}");
                }

                Logger.LogInfo($"[FIKA-RESTORE] Snapshot has {emptySlotIds.Count} empty slots:");
                foreach (var slotId in emptySlotIds)
                {
                    Logger.LogInfo($"[FIKA-RESTORE]   - {slotId}");
                }

                // EXPERIMENTAL: Try to restore inventory
                // For now, we'll just LOG what we would do - full item creation is complex
                Logger.LogInfo("[FIKA-RESTORE] ----------------------------------------");
                Logger.LogInfo("[FIKA-RESTORE] EXPERIMENTAL: Attempting client-side restoration");
                Logger.LogInfo("[FIKA-RESTORE] ----------------------------------------");

                // Step 1: Analyze current inventory
                Logger.LogInfo("[FIKA-RESTORE] Step 1: Analyzing current player inventory...");
                AnalyzeCurrentInventory(equipment);

                // Step 2: Try to clear managed slots
                Logger.LogInfo("[FIKA-RESTORE] Step 2: Clearing managed equipment slots...");
                ClearManagedSlots(equipment, snapshotSlotIds, emptySlotIds);

                // Step 3: Try to add items from snapshot
                Logger.LogInfo("[FIKA-RESTORE] Step 3: Restoring items from snapshot...");
                bool itemsRestored = TryRestoreItems(player, inventory, equipment, snapshot, equipmentId, snapshotEquipmentId);

                Logger.LogInfo("[FIKA-RESTORE] ----------------------------------------");
                Logger.LogInfo($"[FIKA-RESTORE] Client-side restoration attempt complete.");
                Logger.LogInfo($"[FIKA-RESTORE] Items restored: {itemsRestored}");
                Logger.LogInfo("[FIKA-RESTORE] ----------------------------------------");

                // Note: Even if client-side fails, server-side should still work as backup
                return itemsRestored;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-RESTORE] ERROR during restoration: {ex.Message}");
                Logger.LogError($"[FIKA-RESTORE] Stack trace:\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Logs all members of a type for debugging purposes.
        /// </summary>
        private void LogTypeMembers(Type type, string typeName)
        {
            Logger.LogInfo($"[FIKA-DEBUG] === {typeName} Type Members ===");
            Logger.LogInfo($"[FIKA-DEBUG] Type: {type.FullName}");

            Logger.LogInfo($"[FIKA-DEBUG] Properties:");
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Logger.LogInfo($"[FIKA-DEBUG]   - {prop.PropertyType.Name} {prop.Name}");
            }

            Logger.LogInfo($"[FIKA-DEBUG] Fields:");
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Logger.LogInfo($"[FIKA-DEBUG]   - {field.FieldType.Name} {field.Name}");
            }
        }

        /// <summary>
        /// Analyzes and logs the current state of the player's inventory.
        /// </summary>
        private void AnalyzeCurrentInventory(object equipment)
        {
            Logger.LogInfo("[FIKA-ANALYZE] Analyzing current equipment state...");

            try
            {
                var slots = GetAllSlots(equipment);
                Logger.LogInfo($"[FIKA-ANALYZE] Found {slots.Count} equipment slots:");

                foreach (var slot in slots)
                {
                    var containedItem = GetSlotContainedItem(slot.Value);
                    if (containedItem != null)
                    {
                        Logger.LogInfo($"[FIKA-ANALYZE]   [{slot.Key}]: {containedItem.TemplateId} (ID: {containedItem.Id})");
                    }
                    else
                    {
                        Logger.LogInfo($"[FIKA-ANALYZE]   [{slot.Key}]: <empty>");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FIKA-ANALYZE] Error analyzing inventory: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears items from equipment slots that are managed by the snapshot.
        /// </summary>
        private void ClearManagedSlots(object equipment, HashSet<string> snapshotSlotIds, List<string> emptySlotIds)
        {
            Logger.LogInfo("[FIKA-CLEAR] Starting slot clearing...");

            try
            {
                var allSlots = GetAllSlots(equipment);
                Logger.LogInfo($"[FIKA-CLEAR] Total slots found: {allSlots.Count}");

                int clearedCount = 0;
                int skippedCount = 0;

                foreach (var slot in allSlots)
                {
                    string slotId = slot.Key;
                    var slotInstance = slot.Value;

                    // Check if this slot is managed (was in snapshot or was empty at snapshot time)
                    bool isManaged = snapshotSlotIds.Contains(slotId) || emptySlotIds.Contains(slotId);

                    if (!isManaged)
                    {
                        Logger.LogDebug($"[FIKA-CLEAR]   [{slotId}]: Not managed by snapshot - skipping");
                        skippedCount++;
                        continue;
                    }

                    // Get items in this slot
                    var containedItem = GetSlotContainedItem(slotInstance);

                    if (containedItem != null)
                    {
                        Logger.LogInfo($"[FIKA-CLEAR]   [{slotId}]: Clearing item {containedItem.TemplateId}");

                        try
                        {
                            bool removed = RemoveItemFromSlot(slotInstance, containedItem);
                            if (removed)
                            {
                                Logger.LogInfo($"[FIKA-CLEAR]   [{slotId}]: Item removed successfully");
                                clearedCount++;
                            }
                            else
                            {
                                Logger.LogWarning($"[FIKA-CLEAR]   [{slotId}]: Failed to remove item");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"[FIKA-CLEAR]   [{slotId}]: Exception removing item: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.LogDebug($"[FIKA-CLEAR]   [{slotId}]: Already empty");
                    }
                }

                Logger.LogInfo($"[FIKA-CLEAR] Slot clearing complete. Cleared: {clearedCount}, Skipped: {skippedCount}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-CLEAR] ERROR clearing slots: {ex.Message}");
                Logger.LogError($"[FIKA-CLEAR] Stack trace:\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Attempts to restore items from the snapshot to the player's inventory.
        /// Uses ItemFactory to create items and InventoryController to place them.
        /// </summary>
        private bool TryRestoreItems(Player player, object inventory, object equipment,
            InventorySnapshot snapshot, string currentEquipmentId, string snapshotEquipmentId)
        {
            Logger.LogInfo("[FIKA-ITEMS] Starting item restoration...");

            try
            {
                // Get items to restore (skip equipment container itself)
                var itemsToRestore = snapshot.Items
                    .Where(i => i.Tpl != "55d7217a4bdc2d86028b456d")
                    .ToList();

                Logger.LogInfo($"[FIKA-ITEMS] Items to restore: {itemsToRestore.Count}");

                // Log what we're going to restore
                Logger.LogInfo("[FIKA-ITEMS] ----------------------------------------");
                Logger.LogInfo("[FIKA-ITEMS] ITEM RESTORATION PLAN:");
                Logger.LogInfo("[FIKA-ITEMS] ----------------------------------------");

                // Group items by slot for logging
                var itemsBySlot = itemsToRestore
                    .Where(i => i.ParentId == snapshotEquipmentId)
                    .GroupBy(i => i.SlotId);

                foreach (var slotGroup in itemsBySlot)
                {
                    Logger.LogInfo($"[FIKA-ITEMS] Slot [{slotGroup.Key}]:");
                    foreach (var item in slotGroup)
                    {
                        Logger.LogInfo($"[FIKA-ITEMS]   - {item.Tpl} (ID: {item.Id})");
                        if (item.Upd != null)
                        {
                            if (item.Upd.StackObjectsCount > 0)
                                Logger.LogInfo($"[FIKA-ITEMS]       Stack: {item.Upd.StackObjectsCount}");
                            if (item.Upd.Repairable != null)
                                Logger.LogInfo($"[FIKA-ITEMS]       Durability: {item.Upd.Repairable.Durability}/{item.Upd.Repairable.MaxDurability}");
                            if (item.Upd.MedKit != null)
                                Logger.LogInfo($"[FIKA-ITEMS]       MedKit HP: {item.Upd.MedKit.HpResource}");
                        }
                    }
                }

                // Count nested items (attachments, ammo, etc.)
                var nestedItems = itemsToRestore
                    .Where(i => i.ParentId != snapshotEquipmentId && i.ParentId != currentEquipmentId)
                    .ToList();

                Logger.LogInfo($"[FIKA-ITEMS] Nested items (attachments, contents): {nestedItems.Count}");

                // ================================================================
                // Step 1: Get ItemFactory via Singleton pattern
                // ================================================================
                Logger.LogInfo("[FIKA-ITEMS] ----------------------------------------");
                Logger.LogInfo("[FIKA-ITEMS] Accessing ItemFactory...");

                var itemFactory = GetItemFactory();
                if (itemFactory == null)
                {
                    Logger.LogError("[FIKA-ITEMS] Could not access ItemFactory - falling back to server-side restoration.");
                    return false;
                }

                var createItemMethod = itemFactory.GetType().GetMethod("CreateItem",
                    BindingFlags.Public | BindingFlags.Instance);

                if (createItemMethod == null)
                {
                    Logger.LogError("[FIKA-ITEMS] Could not find CreateItem method on ItemFactory!");
                    LogTypeMembers(itemFactory.GetType(), "ItemFactory");
                    return false;
                }

                Logger.LogInfo($"[FIKA-ITEMS] Found CreateItem method with {createItemMethod.GetParameters().Length} parameters.");

                // ================================================================
                // Step 2: Get InventoryController from player
                // ================================================================
                Logger.LogInfo("[FIKA-ITEMS] Getting InventoryController...");

                var inventoryController = GetInventoryController(player);
                if (inventoryController == null)
                {
                    Logger.LogError("[FIKA-ITEMS] Could not get InventoryController - falling back to server-side restoration.");
                    return false;
                }

                Logger.LogInfo($"[FIKA-ITEMS] InventoryController type: {inventoryController.GetType().FullName}");

                // ================================================================
                // Step 3: Create all items from snapshot
                // ================================================================
                Logger.LogInfo("[FIKA-ITEMS] Creating items from snapshot...");

                var createdItems = new Dictionary<string, Item>();
                int createdCount = 0;
                int failedCount = 0;

                foreach (var serializedItem in snapshot.Items)
                {
                    try
                    {
                        // Skip equipment container - we use the player's existing one
                        if (serializedItem.Tpl == "55d7217a4bdc2d86028b456d")
                        {
                            Logger.LogDebug($"[FIKA-ITEMS] Skipping equipment container: {serializedItem.Id}");
                            continue;
                        }

                        // Create the item using ItemFactory.CreateItem(id, templateId, parent)
                        var item = createItemMethod.Invoke(itemFactory, new object[] { serializedItem.Id, serializedItem.Tpl, null }) as Item;

                        if (item == null)
                        {
                            Logger.LogWarning($"[FIKA-ITEMS] Failed to create item: Tpl={serializedItem.Tpl}, Id={serializedItem.Id}");
                            failedCount++;
                            continue;
                        }

                        // Apply item properties from snapshot
                        if (serializedItem.Upd != null)
                        {
                            ApplyItemProperties(item, serializedItem.Upd);
                        }

                        createdItems[serializedItem.Id] = item;
                        createdCount++;

                        Logger.LogDebug($"[FIKA-ITEMS] Created item: Tpl={serializedItem.Tpl}, Id={serializedItem.Id}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[FIKA-ITEMS] Error creating item {serializedItem.Id}: {ex.Message}");
                        failedCount++;
                    }
                }

                Logger.LogInfo($"[FIKA-ITEMS] Item creation complete: {createdCount} created, {failedCount} failed");

                if (createdCount == 0)
                {
                    Logger.LogError("[FIKA-ITEMS] No items were created! Falling back to server-side restoration.");
                    return false;
                }

                // ================================================================
                // Step 4: Place items in equipment slots
                // ================================================================
                Logger.LogInfo("[FIKA-ITEMS] Placing items in equipment slots...");

                var equipmentSlotNames = new[] {
                    "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Scabbard",
                    "Headwear", "Earpiece", "FaceCover", "Eyewear", "ArmBand",
                    "TacticalVest", "ArmorVest", "Pockets", "Backpack", "SecuredContainer",
                    "Compass", "SpecialSlot1", "SpecialSlot2", "SpecialSlot3"
                };

                // Root items are those that go directly in equipment slots
                var rootItems = snapshot.Items.Where(i => !string.IsNullOrEmpty(i.SlotId) &&
                                                          equipmentSlotNames.Contains(i.SlotId)).ToList();

                Logger.LogInfo($"[FIKA-ITEMS] Found {rootItems.Count} root items to place in equipment");

                int placedCount = 0;
                foreach (var serializedItem in rootItems)
                {
                    if (!createdItems.TryGetValue(serializedItem.Id, out var item))
                    {
                        Logger.LogWarning($"[FIKA-ITEMS] Root item {serializedItem.Id} was not created, skipping");
                        continue;
                    }

                    bool placed = PlaceItemWithChildren(inventoryController, equipment, item, serializedItem,
                                                        snapshot.Items, createdItems, currentEquipmentId);
                    if (placed)
                    {
                        placedCount++;
                        Logger.LogInfo($"[FIKA-ITEMS] Placed item in slot [{serializedItem.SlotId}]: {item.TemplateId}");
                    }
                }

                Logger.LogInfo("[FIKA-ITEMS] ----------------------------------------");
                Logger.LogInfo($"[FIKA-ITEMS] Item restoration complete!");
                Logger.LogInfo($"[FIKA-ITEMS] Created: {createdCount}, Placed: {placedCount}, Failed: {failedCount}");
                Logger.LogInfo("[FIKA-ITEMS] ----------------------------------------");

                return placedCount > 0;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-ITEMS] ERROR restoring items: {ex.Message}");
                Logger.LogError($"[FIKA-ITEMS] Stack trace:\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Gets the ItemFactory instance via Comfort.Common.Singleton pattern.
        /// </summary>
        private object GetItemFactory()
        {
            try
            {
                Logger.LogDebug("[FIKA-FACTORY] Looking for ItemFactoryClass...");

                var itemFactoryType = typeof(Item).Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "ItemFactoryClass");

                if (itemFactoryType == null)
                {
                    Logger.LogError("[FIKA-FACTORY] Could not find ItemFactoryClass type!");
                    return null;
                }

                Logger.LogDebug($"[FIKA-FACTORY] Found ItemFactoryClass: {itemFactoryType.FullName}");

                // Find Singleton<T> type from Comfort.Common assembly
                var comfortAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Comfort" || a.GetName().Name == "Comfort.Unity");

                if (comfortAssembly == null)
                {
                    Logger.LogError("[FIKA-FACTORY] Could not find Comfort assembly!");
                    return null;
                }

                var singletonType = comfortAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "Singleton`1" && t.Namespace == "Comfort.Common");

                if (singletonType == null)
                {
                    Logger.LogError("[FIKA-FACTORY] Could not find Singleton<T> type in Comfort.Common!");
                    return null;
                }

                Logger.LogDebug($"[FIKA-FACTORY] Found Singleton type: {singletonType.FullName}");

                // Create Singleton<ItemFactoryClass> generic type
                Type genericSingletonType;
                try
                {
                    genericSingletonType = singletonType.MakeGenericType(itemFactoryType);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[FIKA-FACTORY] Failed to create generic Singleton type: {ex.Message}");
                    return null;
                }

                // Get the Instance property
                var instanceProperty = genericSingletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty == null)
                {
                    Logger.LogError("[FIKA-FACTORY] Could not find Instance property on Singleton!");
                    return null;
                }

                var itemFactory = instanceProperty.GetValue(null);
                if (itemFactory == null)
                {
                    Logger.LogError("[FIKA-FACTORY] ItemFactory instance is null!");
                    return null;
                }

                Logger.LogInfo("[FIKA-FACTORY] ItemFactory accessed successfully via Comfort.Common.Singleton");
                return itemFactory;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-FACTORY] Error getting ItemFactory: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the InventoryController from a Player.
        /// </summary>
        private object GetInventoryController(Player player)
        {
            try
            {
                Logger.LogDebug("[FIKA-CONTROLLER] Looking for InventoryController on player...");

                // Try InventoryController property
                var controllerProp = player.GetType().GetProperty("InventoryController",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (controllerProp != null)
                {
                    var controller = controllerProp.GetValue(player);
                    if (controller != null)
                    {
                        Logger.LogDebug("[FIKA-CONTROLLER] Found InventoryController via property.");
                        return controller;
                    }
                }

                // Try _inventoryController field
                var controllerField = player.GetType().GetField("_inventoryController",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (controllerField != null)
                {
                    var controller = controllerField.GetValue(player);
                    if (controller != null)
                    {
                        Logger.LogDebug("[FIKA-CONTROLLER] Found InventoryController via field.");
                        return controller;
                    }
                }

                Logger.LogWarning("[FIKA-CONTROLLER] Could not find InventoryController on player!");
                LogTypeMembers(player.GetType(), "Player");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-CONTROLLER] Error getting InventoryController: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies properties from the snapshot to a created item.
        /// </summary>
        private void ApplyItemProperties(Item item, ItemUpd upd)
        {
            try
            {
                // Set stack count for stackable items
                if (upd.StackObjectsCount.HasValue && upd.StackObjectsCount.Value > 1)
                {
                    TrySetProperty(item, "StackObjectsCount", (int)upd.StackObjectsCount.Value);
                }

                // Set SpawnedInSession flag (FIR status)
                if (upd.SpawnedInSession)
                {
                    TrySetProperty(item, "SpawnedInSession", true);
                }

                // Handle MedKit resources
                if (upd.MedKit != null)
                {
                    var medKitComponent = GetComponent(item, "MedKitComponent");
                    if (medKitComponent != null)
                    {
                        TrySetProperty(medKitComponent, "HpResource", upd.MedKit.HpResource);
                        Logger.LogDebug($"[FIKA-PROPS] Set MedKit HP: {upd.MedKit.HpResource}");
                    }
                }

                // Handle durability
                if (upd.Repairable != null)
                {
                    var repairComponent = GetComponent(item, "RepairableComponent");
                    if (repairComponent != null)
                    {
                        TrySetProperty(repairComponent, "Durability", upd.Repairable.Durability);
                        TrySetProperty(repairComponent, "MaxDurability", upd.Repairable.MaxDurability);
                        Logger.LogDebug($"[FIKA-PROPS] Set durability: {upd.Repairable.Durability}/{upd.Repairable.MaxDurability}");
                    }
                }

                // Handle FoodDrink resources
                if (upd.FoodDrink != null)
                {
                    var foodComponent = GetComponent(item, "FoodDrinkComponent");
                    if (foodComponent != null)
                    {
                        TrySetProperty(foodComponent, "HpPercent", upd.FoodDrink.HpPercent);
                        Logger.LogDebug($"[FIKA-PROPS] Set FoodDrink HP: {upd.FoodDrink.HpPercent}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FIKA-PROPS] Error applying item properties: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a component from an item by name.
        /// </summary>
        private object GetComponent(Item item, string componentName)
        {
            try
            {
                // Try GetItemComponent method
                var getComponentMethod = item.GetType().GetMethod("GetItemComponent",
                    BindingFlags.Public | BindingFlags.Instance);

                if (getComponentMethod != null && getComponentMethod.IsGenericMethod)
                {
                    // Find the component type
                    var componentType = typeof(Item).Assembly.GetTypes()
                        .FirstOrDefault(t => t.Name == componentName);

                    if (componentType != null)
                    {
                        var genericMethod = getComponentMethod.MakeGenericMethod(componentType);
                        return genericMethod.Invoke(item, null);
                    }
                }

                // Fallback: try accessing component via property
                var componentProp = item.GetType().GetProperty(componentName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (componentProp != null)
                {
                    return componentProp.GetValue(item);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[FIKA-COMPONENT] Could not get component {componentName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Attempts to set a property or field on an object.
        /// </summary>
        private void TrySetProperty(object obj, string name, object value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(obj, value);
                    return;
                }

                var field = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(obj, value);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[FIKA-SET] Could not set {name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Places an item and its children in the inventory.
        /// </summary>
        private bool PlaceItemWithChildren(object inventoryController, object equipment, Item item,
            SerializedItem serializedItem, List<SerializedItem> allItems,
            Dictionary<string, Item> createdItems, string equipmentId)
        {
            try
            {
                Logger.LogDebug($"[FIKA-PLACE] Placing item {item.TemplateId} in slot {serializedItem.SlotId}");

                // Get the slot from equipment
                var slots = GetAllSlots(equipment);
                if (!slots.TryGetValue(serializedItem.SlotId, out var slot))
                {
                    Logger.LogWarning($"[FIKA-PLACE] Could not find slot: {serializedItem.SlotId}");
                    return false;
                }

                // Try to add the item to the slot
                bool added = TryAddItemToSlot(slot, item);
                if (!added)
                {
                    Logger.LogWarning($"[FIKA-PLACE] Could not add item to slot {serializedItem.SlotId}");
                    return false;
                }

                Logger.LogDebug($"[FIKA-PLACE] Successfully placed item in slot {serializedItem.SlotId}");

                // Recursively handle children (attachments, contents, etc.)
                var children = allItems.Where(i => i.ParentId == serializedItem.Id).ToList();
                foreach (var childSerialized in children)
                {
                    if (createdItems.TryGetValue(childSerialized.Id, out var childItem))
                    {
                        PlaceChildItem(item, childItem, childSerialized, allItems, createdItems);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-PLACE] Error placing item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds an item to a slot.
        /// </summary>
        private bool TryAddItemToSlot(object slot, Item item)
        {
            try
            {
                // Try Add method
                var addMethod = slot.GetType().GetMethod("Add",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Item) },
                    null);

                if (addMethod != null)
                {
                    addMethod.Invoke(slot, new object[] { item });
                    Logger.LogDebug("[FIKA-SLOT] Added item via Add method");
                    return true;
                }

                // Try AddItem method
                var addItemMethod = slot.GetType().GetMethod("AddItem",
                    BindingFlags.Public | BindingFlags.Instance);

                if (addItemMethod != null)
                {
                    addItemMethod.Invoke(slot, new object[] { item });
                    Logger.LogDebug("[FIKA-SLOT] Added item via AddItem method");
                    return true;
                }

                // Try setting ContainedItem property
                var containedProp = slot.GetType().GetProperty("ContainedItem",
                    BindingFlags.Public | BindingFlags.Instance);

                if (containedProp != null && containedProp.CanWrite)
                {
                    containedProp.SetValue(slot, item);
                    Logger.LogDebug("[FIKA-SLOT] Added item via ContainedItem property");
                    return true;
                }

                Logger.LogWarning("[FIKA-SLOT] Could not find method to add item to slot");
                LogTypeMembers(slot.GetType(), "Slot");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-SLOT] Error adding item to slot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Places a child item (attachment, content) on its parent.
        /// </summary>
        private void PlaceChildItem(Item parent, Item child, SerializedItem serializedChild,
            List<SerializedItem> allItems, Dictionary<string, Item> createdItems)
        {
            try
            {
                Logger.LogDebug($"[FIKA-CHILD] Placing child {child.TemplateId} on parent {parent.TemplateId}, slot: {serializedChild.SlotId}");

                // Get slots/grids from parent
                var slotsProperty = parent.GetType().GetProperty("Slots",
                    BindingFlags.Public | BindingFlags.Instance);

                if (slotsProperty != null && !string.IsNullOrEmpty(serializedChild.SlotId))
                {
                    var slots = slotsProperty.GetValue(parent) as System.Collections.IEnumerable;
                    if (slots != null)
                    {
                        foreach (var slot in slots)
                        {
                            var idProp = slot.GetType().GetProperty("Id");
                            if (idProp != null)
                            {
                                string slotId = idProp.GetValue(slot)?.ToString();
                                if (slotId == serializedChild.SlotId)
                                {
                                    TryAddItemToSlot(slot, child);
                                    break;
                                }
                            }
                        }
                    }
                }

                // Handle grid placement for containers
                if (serializedChild.Location != null)
                {
                    Logger.LogDebug($"[FIKA-CHILD] Item has grid location: x={serializedChild.Location.X}, y={serializedChild.Location.Y}");
                    // Grid placement is complex - for now we just log it
                    // The server-side restoration will handle proper grid placement
                }

                // Recursively handle grandchildren
                var grandchildren = allItems.Where(i => i.ParentId == serializedChild.Id).ToList();
                foreach (var grandchildSerialized in grandchildren)
                {
                    if (createdItems.TryGetValue(grandchildSerialized.Id, out var grandchildItem))
                    {
                        PlaceChildItem(child, grandchildItem, grandchildSerialized, allItems, createdItems);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FIKA-CHILD] Error placing child item: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all slots from an equipment container via reflection.
        /// </summary>
        private Dictionary<string, object> GetAllSlots(object equipment)
        {
            var result = new Dictionary<string, object>();

            try
            {
                // Try to get Slots property/field from equipment
                var slotsProperty = equipment.GetType().GetProperty("Slots",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (slotsProperty != null)
                {
                    var slots = slotsProperty.GetValue(equipment);
                    if (slots is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var slot in enumerable)
                        {
                            var idProp = slot.GetType().GetProperty("Id");
                            if (idProp != null)
                            {
                                string id = idProp.GetValue(slot)?.ToString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    result[id] = slot;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[FIKA-SLOTS] Could not find Slots property on equipment");
                    LogTypeMembers(equipment.GetType(), "Equipment");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FIKA-SLOTS] Error getting slots: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Gets the contained item from a slot via reflection.
        /// </summary>
        private Item GetSlotContainedItem(object slot)
        {
            try
            {
                var containedItemProp = slot.GetType().GetProperty("ContainedItem");
                if (containedItemProp != null)
                {
                    return containedItemProp.GetValue(slot) as Item;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[FIKA-SLOT] Error getting contained item: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Removes an item from a slot via reflection.
        /// </summary>
        private bool RemoveItemFromSlot(object slot, Item item)
        {
            try
            {
                // Try RemoveItem method
                var removeMethod = slot.GetType().GetMethod("RemoveItem",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (removeMethod != null)
                {
                    Logger.LogDebug($"[FIKA-REMOVE] Found RemoveItem method: {removeMethod}");
                    removeMethod.Invoke(slot, null);
                    return true;
                }

                // Try Clear method
                var clearMethod = slot.GetType().GetMethod("Clear",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (clearMethod != null)
                {
                    Logger.LogDebug($"[FIKA-REMOVE] Found Clear method: {clearMethod}");
                    clearMethod.Invoke(slot, null);
                    return true;
                }

                Logger.LogWarning("[FIKA-REMOVE] Could not find RemoveItem or Clear method");
                LogTypeMembers(slot.GetType(), "Slot");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[FIKA-REMOVE] RemoveItem failed: {ex.Message}");
                return false;
            }
        }
    }
}
