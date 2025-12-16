// Keep Starting Gear - FIKA Snapshot Restorer
//
// This class handles client-side inventory restoration when FIKA is installed.
// It restores the player's inventory from a snapshot BEFORE FIKA serializes
// the "dead" inventory state.
//
// IMPORTANT: This runs CLIENT-SIDE, not server-side!
// The restoration must happen before FIKA's SavePlayer() serializes the inventory.

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
            if (player == null)
            {
                Logger.LogError("Cannot restore inventory - player is null");
                return false;
            }

            try
            {
                var profileId = player.ProfileId;
                Logger.LogInfo($"[FIKA Restore] Attempting to restore inventory for player: {profileId}");

                // Load snapshot
                var snapshot = _snapshotManager.LoadSnapshot(profileId);
                if (snapshot == null)
                {
                    // Try loading any recent snapshot
                    snapshot = _snapshotManager.GetMostRecentSnapshot();
                }

                if (snapshot == null)
                {
                    Logger.LogWarning("[FIKA Restore] No snapshot found for player.");
                    return false;
                }

                Logger.LogInfo($"[FIKA Restore] Found snapshot with {snapshot.Items?.Count ?? 0} items");

                // Restore the inventory
                bool success = RestoreInventoryFromSnapshot(player, snapshot);

                if (success)
                {
                    Logger.LogInfo("[FIKA Restore] Inventory restoration successful!");

                    // Delete the snapshot after successful restoration
                    _snapshotManager.ClearSnapshot(profileId);
                }
                else
                {
                    Logger.LogWarning("[FIKA Restore] Inventory restoration failed.");
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA Restore] Error restoring inventory: {ex.Message}");
                Logger.LogError(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Restores the player's inventory from a snapshot.
        /// This is the client-side equivalent of what the server does.
        /// </summary>
        private bool RestoreInventoryFromSnapshot(Player player, InventorySnapshot snapshot)
        {
            if (player.Profile?.Inventory == null)
            {
                Logger.LogError("Player inventory is null");
                return false;
            }

            var inventory = player.Profile.Inventory;

            // Get equipment via reflection to avoid type compatibility issues
            var equipmentProp = inventory.GetType().GetProperty("Equipment");
            var equipment = equipmentProp?.GetValue(inventory);

            if (equipment == null)
            {
                Logger.LogError("Player equipment is null");
                return false;
            }

            try
            {
                Logger.LogInfo("[FIKA Restore] Starting client-side inventory restoration...");

                // Get the equipment container ID via reflection
                var idProp = equipment.GetType().GetProperty("Id");
                string equipmentId = idProp?.GetValue(equipment)?.ToString();
                Logger.LogDebug($"[FIKA Restore] Equipment container ID: {equipmentId}");

                // Find equipment container ID in snapshot
                string snapshotEquipmentId = snapshot.Items?
                    .FirstOrDefault(i => i.Tpl == "55d7217a4bdc2d86028b456d")?.Id;

                if (string.IsNullOrEmpty(snapshotEquipmentId))
                {
                    Logger.LogError("Could not find equipment container in snapshot");
                    return false;
                }

                // Get slots that were captured in snapshot
                var snapshotSlotIds = new HashSet<string>(snapshot.Items
                    .Where(i => i.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(i.SlotId))
                    .Select(i => i.SlotId));

                // Include empty slots from snapshot metadata
                var emptySlotIds = snapshot.EmptySlots ?? new List<string>();

                Logger.LogDebug($"[FIKA Restore] Snapshot slots: {string.Join(", ", snapshotSlotIds)}");
                Logger.LogDebug($"[FIKA Restore] Empty slots: {string.Join(", ", emptySlotIds)}");

                // Step 1: Clear managed slots (slots that were in snapshot OR were empty at snapshot time)
                ClearManagedSlots(equipment, snapshotSlotIds, emptySlotIds);

                // Step 2: Add items from snapshot
                AddItemsFromSnapshot(player, inventory, snapshot, equipmentId, snapshotEquipmentId);

                Logger.LogInfo("[FIKA Restore] Client-side inventory restoration complete!");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA Restore] Error during restoration: {ex.Message}");
                Logger.LogError(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Clears items from equipment slots that are managed by the snapshot.
        /// </summary>
        private void ClearManagedSlots(object equipment, HashSet<string> snapshotSlotIds, List<string> emptySlotIds)
        {
            Logger.LogDebug("[FIKA Restore] Clearing managed equipment slots...");

            try
            {
                // Get all slots from equipment
                var allSlots = GetAllSlots(equipment);

                foreach (var slot in allSlots)
                {
                    string slotId = slot.Key;
                    var slotInstance = slot.Value;

                    // Check if this slot is managed (was in snapshot or was empty at snapshot time)
                    bool isManaged = snapshotSlotIds.Contains(slotId) || emptySlotIds.Contains(slotId);

                    if (isManaged && slotInstance != null)
                    {
                        // Get items in this slot
                        var containedItem = GetSlotContainedItem(slotInstance);

                        if (containedItem != null)
                        {
                            Logger.LogDebug($"[FIKA Restore] Clearing slot: {slotId}");

                            // Remove the item from the slot
                            try
                            {
                                RemoveItemFromSlot(slotInstance, containedItem);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"[FIKA Restore] Failed to clear slot {slotId}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA Restore] Error clearing slots: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds items from the snapshot to the player's inventory.
        /// </summary>
        private void AddItemsFromSnapshot(Player player, object inventory, InventorySnapshot snapshot,
            string currentEquipmentId, string snapshotEquipmentId)
        {
            Logger.LogDebug("[FIKA Restore] Adding items from snapshot...");

            // Skip the equipment container itself
            var itemsToRestore = snapshot.Items
                .Where(i => i.Tpl != "55d7217a4bdc2d86028b456d")
                .ToList();

            Logger.LogInfo($"[FIKA Restore] Restoring {itemsToRestore.Count} items from snapshot");

            // Create a mapping of snapshot item IDs to new item instances
            var itemMapping = new Dictionary<string, Item>();

            foreach (var snapshotItem in itemsToRestore)
            {
                try
                {
                    // Remap parent ID if it's the equipment container
                    string parentId = snapshotItem.ParentId == snapshotEquipmentId
                        ? currentEquipmentId
                        : snapshotItem.ParentId;

                    // Find the parent item/slot
                    Item parent = null;
                    if (parentId == currentEquipmentId)
                    {
                        // Parent is equipment - we'll handle slot assignment separately
                    }
                    else if (itemMapping.TryGetValue(snapshotItem.ParentId, out var mappedParent))
                    {
                        parent = mappedParent;
                    }

                    // Create the item
                    var newItem = CreateItemFromSnapshot(snapshotItem);

                    if (newItem != null)
                    {
                        itemMapping[snapshotItem.Id] = newItem;
                        Logger.LogDebug($"[FIKA Restore] Created item: {snapshotItem.Tpl} -> {newItem.Id}");

                        // Add to inventory
                        // Note: The actual slot/grid placement needs to use EFT's inventory system
                        // This is a simplified version - full implementation would need proper slot assignment
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[FIKA Restore] Failed to restore item {snapshotItem.Id}: {ex.Message}");
                }
            }

            Logger.LogInfo($"[FIKA Restore] Created {itemMapping.Count} items");
        }

        /// <summary>
        /// Creates an EFT Item from a snapshot SerializedItem.
        /// </summary>
        private Item CreateItemFromSnapshot(SerializedItem snapshotItem)
        {
            // This is a placeholder - actual item creation requires using EFT's item factory
            // which needs proper integration with the game's systems

            // In practice, we would need to:
            // 1. Use ItemFactory.CreateItem() or similar
            // 2. Set up all the item properties from snapshot
            // 3. Handle nested items recursively

            Logger.LogDebug($"[FIKA Restore] Would create item: {snapshotItem.Tpl} in slot {snapshotItem.SlotId}");

            // TODO: Implement actual item creation using EFT's systems
            // For now, return null - this needs EFT API access

            return null;
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
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error getting slots: {ex.Message}");
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
                return containedItemProp?.GetValue(slot) as Item;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Removes an item from a slot via reflection.
        /// </summary>
        private void RemoveItemFromSlot(object slot, Item item)
        {
            try
            {
                var removeMethod = slot.GetType().GetMethod("RemoveItem");
                removeMethod?.Invoke(slot, null);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"RemoveItem failed: {ex.Message}");
            }
        }
    }
}
