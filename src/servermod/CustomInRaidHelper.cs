// ============================================================================
// Keep Starting Gear - Custom In-Raid Helper
// ============================================================================
// This class overrides SPT's InRaidHelper to conditionally skip inventory
// deletion when inventory has been restored from a snapshot.
//
// SPT'S NORMAL BEHAVIOR:
// When a player dies in raid, SPT's InRaidHelper.DeleteInventory() is called
// to remove all equipment from the player's profile. This is the "death penalty."
//
// OUR MODIFICATION:
// We override DeleteInventory() to check SnapshotRestorationState first.
// If inventory was just restored from a snapshot, we skip the deletion to
// preserve the restored items.
//
// DEPENDENCY INJECTION:
// The [Injectable] attribute with InjectionType.Scoped tells SPT to use
// this class instead of the default InRaidHelper. The typeof(InRaidHelper)
// parameter specifies which service we're replacing.
//
// WHY THIS APPROACH?
// - We can't hook DeleteInventory via a patch because it's called internally
// - Replacing the entire service allows complete control over behavior
// - We still call base methods for non-restoration cases (normal behavior)
//
// REFACTORED:
// Restoration logic has been extracted to SnapshotRestorer class to eliminate
// code duplication with RaidEndInterceptor.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Custom InRaidHelper that skips DeleteInventory when inventory was restored from snapshot.
/// This allows the mod to preserve equipment on death when a valid snapshot exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why we need this:</b>
/// </para>
/// <para>
/// SPT processes raid end in multiple steps. RaidEndInterceptor runs first and
/// restores the inventory from snapshot. But then SPT's normal death processing
/// kicks in and tries to delete the inventory. We need to prevent that deletion.
/// </para>
/// <para>
/// <b>How it works:</b>
/// </para>
/// <list type="number">
///   <item>RaidEndInterceptor restores inventory and sets the restoration flag</item>
///   <item>SPT calls InRaidHelper.DeleteInventory() as part of death processing</item>
///   <item>Our override calls TryConsume() which atomically checks and resets the flag</item>
///   <item>If flag was set, skip deletion; otherwise try snapshot restoration or normal processing</item>
/// </list>
/// <para>
/// <b>Injectable attribute:</b>
/// InjectionType.Scoped means a new instance is created for each request scope.
/// typeof(InRaidHelper) tells SPT this replaces the standard InRaidHelper.
/// </para>
/// </remarks>
[Injectable(InjectionType.Scoped, typeof(InRaidHelper))]
public class CustomInRaidHelper : InRaidHelper
{
    // ========================================================================
    // Dependencies
    // ========================================================================

    /// <summary>
    /// Logger for output messages to the server console.
    /// </summary>
    private readonly ISptLogger<InRaidHelper> _logger;

    /// <summary>
    /// Path to snapshot files.
    /// </summary>
    private readonly string _snapshotsPath;

    /// <summary>
    /// Shared restorer instance.
    /// </summary>
    private readonly SnapshotRestorer<InRaidHelper> _restorer;

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Creates a new CustomInRaidHelper with all required dependencies.
    /// Dependencies are injected by SPT's DI container.
    /// </summary>
    /// <param name="logger">Logger for console output</param>
    /// <param name="inventoryHelper">Helper for inventory operations</param>
    /// <param name="configServer">Server configuration</param>
    /// <param name="cloner">Object cloning utility</param>
    /// <param name="databaseService">Database access service</param>
    /// <remarks>
    /// All parameters are passed to the base class constructor.
    /// Additionally, we store the logger locally and initialize the
    /// snapshot restorer for use in our override methods.
    /// </remarks>
    public CustomInRaidHelper(
        ISptLogger<InRaidHelper> logger,
        InventoryHelper inventoryHelper,
        ConfigServer configServer,
        ICloner cloner,
        DatabaseService databaseService)
        : base(logger, inventoryHelper, configServer, cloner, databaseService)
    {
        _logger = logger;
        _snapshotsPath = SnapshotRestorerHelper.ResolveSnapshotsPath();
        _restorer = new SnapshotRestorer<InRaidHelper>(logger, _snapshotsPath);
    }

    // ========================================================================
    // Overridden Methods
    // ========================================================================

    /// <summary>
    /// Override DeleteInventory to selectively skip deletion for managed slots when inventory was restored from snapshot.
    /// </summary>
    /// <param name="pmcData">The player's PMC data containing inventory</param>
    /// <param name="sessionId">The session/profile ID</param>
    /// <remarks>
    /// <para>
    /// This method is called by SPT during death processing to remove all equipment.
    /// We use TryConsume() to atomically check and reset the flag in a single operation,
    /// preventing race conditions.
    /// </para>
    /// <para>
    /// When inventory was restored from a snapshot with specific managed slots:
    /// - Items in managed slots are PRESERVED (restored from snapshot)
    /// - Items in non-managed slots are DELETED (normal death penalty applies)
    /// </para>
    /// </remarks>
    public override void DeleteInventory(PmcData pmcData, MongoId sessionId)
    {
        // Atomically check and consume the restoration state for this session
        if (SnapshotRestorationState.TryConsume(sessionId.ToString(), out var managedSlotIds))
        {
            if (managedSlotIds == null)
            {
                // Legacy behavior (null): no slot info, skip all deletion
                // This maintains backwards compatibility with pre-1.4 snapshots
                _logger.Debug($"{Constants.LogPrefix} Skipping DeleteInventory - inventory was restored from snapshot (legacy: all slots preserved)");
                return;
            }

            if (managedSlotIds.Count == 0)
            {
                // Empty set means NO slots are protected - use normal death processing
                // This handles cases where user unchecked all protection options
                _logger.Debug($"{Constants.LogPrefix} No managed slots configured - using normal death processing");
                base.DeleteInventory(pmcData, sessionId);
                return;
            }

            // Normal case: preserve managed slots, delete items from non-managed slots
            _logger.Debug($"{Constants.LogPrefix} Partial DeleteInventory - preserving {managedSlotIds.Count} managed slots, deleting non-managed");
            DeleteNonManagedSlotItems(pmcData, managedSlotIds);
            return;
        }

        // Try to restore from snapshot (this handles SVM compatibility)
        // When SVM is installed, RaidEndInterceptor may not run, so we do restoration here
        var result = _restorer.TryRestore(sessionId.ToString(), pmcData.Inventory.Items);

        if (result.Success)
        {
            _logger.Info($"{Constants.LogPrefix} Inventory restored from snapshot ({result.ItemsAdded} items)!");
            if (result.DuplicatesSkipped > 0)
            {
                _logger.Debug($"{Constants.LogPrefix} Skipped {result.DuplicatesSkipped} duplicate items");
            }
            if (result.NonManagedSkipped > 0)
            {
                _logger.Debug($"{Constants.LogPrefix} Skipped {result.NonManagedSkipped} items from non-managed slots");
            }

            // Handle managed slot deletion based on slot info
            if (result.ManagedSlotIds == null)
            {
                // Legacy: no slot info, skip deletion (all preserved)
                _logger.Debug($"{Constants.LogPrefix} Legacy snapshot - all slots preserved");
            }
            else if (result.ManagedSlotIds.Count == 0)
            {
                // Empty set: no slots protected, use normal death processing
                _logger.Debug($"{Constants.LogPrefix} No managed slots - using normal death processing for equipment");
                base.DeleteInventory(pmcData, sessionId);
            }
            else
            {
                // Normal: preserve managed, delete non-managed
                _logger.Debug($"{Constants.LogPrefix} Partial DeleteInventory - preserving {result.ManagedSlotIds.Count} managed slots");
                DeleteNonManagedSlotItems(pmcData, result.ManagedSlotIds);
            }
            return;
        }

        // Log restoration failure only if there was a snapshot but it failed
        if (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage != "No snapshot file found")
        {
            _logger.Warning($"{Constants.LogPrefix} Snapshot restoration failed: {result.ErrorMessage}");
        }

        // Normal death processing - no snapshot found
        _logger.Debug($"{Constants.LogPrefix} Normal death processing - DeleteInventory will proceed");
        base.DeleteInventory(pmcData, sessionId);
    }

    /// <summary>
    /// Deletes equipment items from slots that are NOT in the managed set.
    /// Items in managed slots are preserved (they were restored from snapshot).
    /// </summary>
    /// <param name="pmcData">The player's PMC data containing inventory</param>
    /// <param name="managedSlotIds">Set of slot IDs to preserve (case-insensitive)</param>
    private void DeleteNonManagedSlotItems(PmcData pmcData, HashSet<string> managedSlotIds)
    {
        var inventory = pmcData.Inventory;
        if (inventory?.Items == null)
        {
            _logger.Warning($"{Constants.LogPrefix} Cannot delete non-managed slots - inventory is null");
            return;
        }

        // Find ALL Equipment container IDs to handle edge cases where items may
        // be parented to different Equipment containers
        var allEquipmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory.Items)
        {
            if (item.Template == Constants.EquipmentTemplateId && !string.IsNullOrEmpty(item.Id))
            {
                allEquipmentIds.Add(item.Id);
                _logger.Debug($"{Constants.LogPrefix} Found Equipment container: {item.Id}");
            }
        }

        if (allEquipmentIds.Count == 0)
        {
            _logger.Warning($"{Constants.LogPrefix} Cannot find any Equipment container");
            return;
        }

        _logger.Debug($"{Constants.LogPrefix} Checking {allEquipmentIds.Count} Equipment container(s) for non-managed slots");

        // Build a set of item IDs to remove (items in non-managed equipment slots)
        var itemsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in inventory.Items)
        {
            // Skip if not directly in ANY Equipment container
            if (string.IsNullOrEmpty(item.ParentId) || !allEquipmentIds.Contains(item.ParentId))
                continue;

            // Get the slot name
            var slotId = item.SlotId;
            if (string.IsNullOrEmpty(slotId))
                continue;

            // SecuredContainer: ALWAYS preserve container AND contents (normal Tarkov behavior)
            if (string.Equals(slotId, Constants.SecuredContainerSlot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"{Constants.LogPrefix} Preserving SecuredContainer and contents (always kept in normal Tarkov)");
                continue;
            }

            // Pockets: ALWAYS preserve the container (permanent item), contents follow death mechanics
            if (string.Equals(slotId, Constants.PocketsSlot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"{Constants.LogPrefix} Preserving Pockets container (permanent item)");
                // If Pockets is NOT managed, delete contents (normal death = pocket contents lost)
                if (!managedSlotIds.Contains(slotId) && !string.IsNullOrEmpty(item.Id))
                {
                    _logger.Debug($"{Constants.LogPrefix} Deleting Pockets contents (non-managed slot, normal death)");
                    var pocketsId = item.Id;
                    for (int i = 0; i < inventory.Items.Count; i++)
                    {
                        var child = inventory.Items[i];
                        if (child.ParentId == pocketsId && !string.IsNullOrEmpty(child.Id))
                        {
                            CollectItemAndChildren(child.Id, inventory.Items, itemsToRemove);
                        }
                    }
                }
                // If managed, contents were restored from snapshot - preserve them
                continue;
            }

            // Normal slot: if NOT managed, mark the item and all children for deletion
            if (!managedSlotIds.Contains(slotId))
            {
                _logger.Debug($"{Constants.LogPrefix} Deleting item in non-managed slot '{slotId}': {item.Id}");
                CollectItemAndChildren(item.Id, inventory.Items, itemsToRemove);
            }
        }

        if (itemsToRemove.Count == 0)
        {
            _logger.Debug($"{Constants.LogPrefix} No items to delete from non-managed slots");
            return;
        }

        // Remove the items
        // H-05 FIX: Null-safe check on item.Id before passing to HashSet.Contains
        int removed = inventory.Items.RemoveAll(item =>
            !string.IsNullOrEmpty(item.Id) && itemsToRemove.Contains(item.Id));
        _logger.Info($"{Constants.LogPrefix} Deleted {removed} items from non-managed equipment slots");
    }

    /// <summary>
    /// Collects an item and all its children for deletion using BFS.
    /// Uses iterative traversal with a safety limit to prevent stack overflow
    /// from deeply nested or cyclic item trees.
    /// </summary>
    private void CollectItemAndChildren(string itemId, List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> items, HashSet<string> toRemove)
    {
        if (string.IsNullOrEmpty(itemId))
            return;

        const int maxProcessed = 1000;
        var queue = new Queue<string>();
        queue.Enqueue(itemId);
        toRemove.Add(itemId);
        int processed = 0;

        while (queue.Count > 0 && processed < maxProcessed)
        {
            var currentId = queue.Dequeue();
            processed++;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Id))
                    continue;

                if (item.ParentId == currentId && !toRemove.Contains(item.Id))
                {
                    toRemove.Add(item.Id);
                    queue.Enqueue(item.Id);
                }
            }
        }

        if (processed >= maxProcessed)
        {
            _logger.Warning($"{Constants.LogPrefix} CollectItemAndChildren hit safety limit ({maxProcessed}) for item {itemId}");
        }
    }
}
