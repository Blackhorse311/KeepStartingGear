// ============================================================================
// Keep Starting Gear - Custom In-Raid Helper
// ============================================================================
// This class overrides SPT's InRaidHelper to handle snapshot restoration
// and selective inventory deletion on death.
//
// SPT'S DEATH PROCESSING PIPELINE (HandlePostRaidPmc):
//   1. SetInventory() - copies post-raid items to server profile
//   2. Stats/quests/XP updates
//   3. DeleteInventory() - removes equipment items (death penalty)
//   4. SaveProfileAsync() - persists final state
//
// TWO-PHASE RESTORATION:
// Phase 1 (SetInventory override): If a snapshot file exists, restore items
// into the postRaidProfile BEFORE base.SetInventory copies them to the server
// profile. This is the primary restoration path for SVM compatibility, where
// RaidEndInterceptor may not run.
//
// Phase 2 (DeleteInventory override): Check SnapshotRestorationState to
// determine which slots are managed. Preserve managed slots (restored from
// snapshot) and delete items from non-managed slots (normal death penalty).
//
// COORDINATION:
// The snapshot file acts as the coordination mechanism between components:
// - If RaidEndInterceptor ran: it already restored and deleted the snapshot
//   file, so SetInventory override finds nothing and skips.
// - If SVM prevented RaidEndInterceptor: the snapshot file still exists,
//   so SetInventory override restores from it.
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
/// Custom InRaidHelper that handles snapshot restoration via SetInventory override
/// and selective inventory deletion via DeleteInventory override.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two-phase restoration:</b>
/// </para>
/// <list type="number">
///   <item>SetInventory override: restores snapshot items into postRaidProfile before
///   base.SetInventory copies them to the server profile (SVM compatibility)</item>
///   <item>DeleteInventory override: checks SnapshotRestorationState to preserve managed
///   slots and delete non-managed slots (selective death penalty)</item>
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
    /// Override SetInventory to restore snapshot items into the postRaidProfile BEFORE
    /// base.SetInventory copies them to the server profile.
    /// </summary>
    /// <param name="sessionId">The session/profile ID</param>
    /// <param name="serverProfile">The server-side PMC profile (will receive items from postRaidProfile)</param>
    /// <param name="postRaidProfile">The client's post-raid profile (items the player had at raid end)</param>
    /// <param name="isSurvived">True if the player extracted successfully</param>
    /// <param name="isTransfer">True if the player is transferring between maps</param>
    /// <remarks>
    /// <para>
    /// This is the primary restoration path for SVM compatibility. When SVM is installed,
    /// it replaces MatchCallbacks, preventing RaidEndInterceptor from running. Without this
    /// override, the postRaidProfile would contain the player's death-state inventory, and
    /// base.SetInventory would copy that death state to the server profile.
    /// </para>
    /// <para>
    /// The snapshot file acts as coordination: if RaidEndInterceptor already handled
    /// restoration, it deleted the snapshot file. If SVM prevented it, the file still exists.
    /// </para>
    /// </remarks>
    public override void SetInventory(MongoId sessionId, PmcData serverProfile,
        PmcData postRaidProfile, bool isSurvived, bool isTransfer)
    {
        // Only attempt restoration on death (not extraction or map transfer)
        if (!isSurvived && !isTransfer)
        {
            var postRaidInventory = postRaidProfile?.Inventory?.Items;
            if (postRaidInventory != null)
            {
                // TryRestore checks for the snapshot file. If RaidEndInterceptor already
                // ran, it deleted the file and this returns "No snapshot file found".
                // If SVM prevented RaidEndInterceptor, the file exists and we restore here.
                var result = _restorer.TryRestore(sessionId.ToString(), postRaidInventory);

                if (result.Success)
                {
                    _logger.Info($"{Constants.LogPrefix} Pre-SetInventory restoration: {result.ItemsAdded} items restored into post-raid profile");
                    if (result.DuplicatesSkipped > 0)
                    {
                        _logger.Debug($"{Constants.LogPrefix} Skipped {result.DuplicatesSkipped} duplicate items");
                    }
                    if (result.NonManagedSkipped > 0)
                    {
                        _logger.Debug($"{Constants.LogPrefix} Skipped {result.NonManagedSkipped} items from non-managed slots");
                    }

                    // Mark restoration state so DeleteInventory knows to do partial deletion
                    SnapshotRestorationState.MarkRestored(sessionId.ToString(), result.ManagedSlotIds);
                    _logger.Debug($"{Constants.LogPrefix} Marked restoration state with {result.ManagedSlotIds?.Count ?? 0} managed slots");
                }
                else if (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage != "No snapshot file found")
                {
                    _logger.Warning($"{Constants.LogPrefix} SetInventory restoration failed: {result.ErrorMessage}");
                }
            }
        }

        base.SetInventory(sessionId, serverProfile, postRaidProfile, isSurvived, isTransfer);
    }

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

        // No restoration state found. This means neither RaidEndInterceptor nor
        // SetInventory override performed restoration (no snapshot file existed).
        // Proceed with normal death processing.
        _logger.Debug($"{Constants.LogPrefix} No restoration state found - normal death processing");
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

            // Scabbard: ALWAYS preserve container AND contents (melee weapons are never lost on death)
            if (string.Equals(slotId, Constants.ScabbardSlot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"{Constants.LogPrefix} Preserving Scabbard and contents (melee weapons always kept in normal Tarkov)");
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
