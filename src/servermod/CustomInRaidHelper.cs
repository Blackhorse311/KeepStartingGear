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
        // Atomically check and consume the restoration flag, getting managed slots
        if (SnapshotRestorationState.TryConsume(out var managedSlotIds))
        {
            if (managedSlotIds == null || managedSlotIds.Count == 0)
            {
                // Legacy behavior: no slot info, skip all deletion
                _logger.Debug($"{Constants.LogPrefix} Skipping DeleteInventory - inventory was restored from snapshot (all slots)");
                return;
            }

            // New behavior: Only preserve managed slots, delete items from non-managed slots
            _logger.Debug($"{Constants.LogPrefix} Partial DeleteInventory - preserving {managedSlotIds.Count} managed slots, deleting non-managed");
            DeleteNonManagedSlotItems(pmcData, managedSlotIds);
            SnapshotRestorationState.ClearManagedSlots();
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

            // If we have managed slot IDs, delete items from non-managed slots
            if (result.ManagedSlotIds != null && result.ManagedSlotIds.Count > 0)
            {
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

        // Find the Equipment container ID
        string? equipmentId = null;
        foreach (var item in inventory.Items)
        {
            if (item.Template == Constants.EquipmentTemplateId)
            {
                equipmentId = item.Id;
                break;
            }
        }

        if (string.IsNullOrEmpty(equipmentId))
        {
            _logger.Warning($"{Constants.LogPrefix} Cannot find Equipment container");
            return;
        }

        // Build a set of item IDs to remove (items in non-managed equipment slots)
        var itemsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in inventory.Items)
        {
            // Skip if not directly in Equipment container
            if (item.ParentId != equipmentId)
                continue;

            // Get the slot name
            var slotId = item.SlotId;
            if (string.IsNullOrEmpty(slotId))
                continue;

            // If this slot is NOT managed, mark the item and all children for deletion
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
        int removed = inventory.Items.RemoveAll(item => itemsToRemove.Contains(item.Id));
        _logger.Info($"{Constants.LogPrefix} Deleted {removed} items from non-managed equipment slots");
    }

    /// <summary>
    /// Recursively collects an item and all its children for deletion.
    /// </summary>
    private void CollectItemAndChildren(string itemId, List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> items, HashSet<string> toRemove)
    {
        toRemove.Add(itemId);

        // Find all children (items whose ParentId is this item)
        foreach (var item in items)
        {
            if (item.ParentId == itemId && !toRemove.Contains(item.Id))
            {
                CollectItemAndChildren(item.Id, items, toRemove);
            }
        }
    }
}
