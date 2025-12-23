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
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
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
///   <item>Our override checks the flag and skips deletion if set</item>
///   <item>The flag is reset for future requests</item>
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
    /// We only store the logger locally for use in our override.
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
    }

    // ========================================================================
    // Overridden Methods
    // ========================================================================

    /// <summary>
    /// Override DeleteInventory to skip deletion when inventory was restored from snapshot.
    /// </summary>
    /// <param name="pmcData">The player's PMC data containing inventory</param>
    /// <param name="sessionId">The session/profile ID</param>
    /// <remarks>
    /// <para>
    /// This method is called by SPT during death processing to remove all equipment.
    /// We check the SnapshotRestorationState flag to determine if we should skip.
    /// </para>
    /// <para>
    /// <b>Important:</b> We reset the flag after checking to ensure it doesn't
    /// affect future requests. The flag is thread-local, so this is safe.
    /// </para>
    /// </remarks>
    public override void DeleteInventory(PmcData pmcData, MongoId sessionId)
    {
        // Check if inventory was already restored (by RaidEndInterceptor when not using SVM)
        if (SnapshotRestorationState.InventoryRestoredFromSnapshot)
        {
            _logger.Debug("[KeepStartingGear-Server] Skipping DeleteInventory - inventory was restored from snapshot");
            SnapshotRestorationState.Reset();
            return;
        }

        // Try to restore from snapshot (this handles SVM compatibility)
        // When SVM is installed, RaidEndInterceptor never runs, so we do restoration here
        if (TryRestoreFromSnapshot(sessionId.ToString(), pmcData))
        {
            _logger.Info("[KeepStartingGear-Server] Inventory restored from snapshot!");
            // Don't call base - we've restored the inventory
            return;
        }

        // Normal death processing - no snapshot found
        _logger.Debug("[KeepStartingGear-Server] Normal death processing - DeleteInventory will proceed");
        base.DeleteInventory(pmcData, sessionId);
    }

    // ========================================================================
    // Snapshot Restoration (SVM Compatible)
    // ========================================================================

    private const string EquipmentContainerTpl = "55d7217a4bdc2d86028b456d";
    private const string ModFolderName = "Blackhorse311-KeepStartingGear";

    private bool TryRestoreFromSnapshot(string sessionId, PmcData pmcData)
    {
        string snapshotsPath = ResolveSnapshotsPath();
        if (string.IsNullOrEmpty(snapshotsPath))
        {
            _logger.Warning("[KeepStartingGear-Server] Could not resolve snapshots path");
            return false;
        }

        string snapshotFile = System.IO.Path.Combine(snapshotsPath, $"{sessionId}.json");
        if (!File.Exists(snapshotFile))
        {
            _logger.Debug($"[KeepStartingGear-Server] No snapshot found at: {snapshotFile}");
            return false;
        }

        try
        {
            string snapshotJson = File.ReadAllText(snapshotFile);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var snapshot = JsonSerializer.Deserialize<InventorySnapshot>(snapshotJson, options);

            if (snapshot?.Items == null || snapshot.Items.Count == 0)
            {
                _logger.Warning("[KeepStartingGear-Server] Snapshot is empty or invalid");
                return false;
            }

            _logger.Debug($"[KeepStartingGear-Server] Loaded snapshot with {snapshot.Items.Count} items");

            // Debug: Log deserialized IncludedSlots
            if (snapshot.IncludedSlots != null)
            {
                _logger.Debug($"[KeepStartingGear-Server] Deserialized IncludedSlots: [{string.Join(", ", snapshot.IncludedSlots)}]");
            }

            // Find Equipment container in profile
            string? profileEquipmentId = pmcData.Inventory.Items
                .FirstOrDefault(i => i.Template == EquipmentContainerTpl)?.Id;

            string? snapshotEquipmentId = snapshot.Items
                .FirstOrDefault(i => i.Tpl == EquipmentContainerTpl)?.Id;

            if (string.IsNullOrEmpty(profileEquipmentId))
            {
                _logger.Error("[KeepStartingGear-Server] Could not find Equipment container in profile");
                return false;
            }

            // Get the set of slot IDs that USER ENABLED in settings (IncludedSlots)
            // This is the authoritative list of what slots should be managed by the mod
            var includedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.IncludedSlots != null && snapshot.IncludedSlots.Count > 0)
            {
                foreach (var slot in snapshot.IncludedSlots)
                {
                    includedSlotIds.Add(slot);
                }
                _logger.Debug($"[KeepStartingGear-Server] User configured slots to manage: {string.Join(", ", includedSlotIds)}");
            }

            // Track which slots had items in snapshot
            var snapshotSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var emptySlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in snapshot.Items)
            {
                if (item.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(item.SlotId))
                {
                    snapshotSlotIds.Add(item.SlotId);
                }
            }

            if (snapshot.EmptySlots != null)
            {
                foreach (var slotId in snapshot.EmptySlots)
                {
                    emptySlotIds.Add(slotId);
                }
            }

            // Remove equipment items from MANAGED slots only
            // Items in non-managed slots (user disabled them) are PRESERVED
            // This allows normal Tarkov behavior for disabled slots (e.g., secure container keeps items)
            var equipmentItemIds = new HashSet<string>();

            foreach (var item in pmcData.Inventory.Items.ToList())
            {
                if (item.ParentId == profileEquipmentId && !string.IsNullOrEmpty(item.SlotId))
                {
                    // Determine if this slot is managed by the mod
                    // A slot is managed if:
                    // 1. It's in IncludedSlots (user enabled it), OR
                    // 2. No IncludedSlots exist (legacy snapshot) AND it's in snapshotSlotIds or emptySlotIds
                    bool slotIsManaged;
                    if (includedSlotIds.Count > 0)
                    {
                        // Modern snapshot: use IncludedSlots as authoritative source
                        slotIsManaged = includedSlotIds.Contains(item.SlotId);
                    }
                    else
                    {
                        // Legacy snapshot: fall back to old behavior
                        slotIsManaged = snapshotSlotIds.Contains(item.SlotId) || emptySlotIds.Contains(item.SlotId);
                    }

                    // Only remove items from MANAGED slots
                    // - Managed slots: Items will be REMOVED then RESTORED from snapshot
                    // - Non-managed slots: Items are PRESERVED (not touched by the mod)
                    if (!slotIsManaged)
                    {
                        // This slot is NOT managed by the mod - PRESERVE it
                        // Don't add to equipmentItemIds, so it won't be removed
                        _logger.Debug($"[KeepStartingGear-Server] PRESERVING item in slot '{item.SlotId}' (slot not managed by mod): {item.Template}");
                        continue;
                    }

                    // Slot IS managed - add to removal list
                    equipmentItemIds.Add(item.Id!);

                    // Log why this item is being removed
                    if (snapshotSlotIds.Contains(item.SlotId))
                    {
                        _logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{item.SlotId}' (will be restored from snapshot): {item.Template}");
                    }
                    else if (emptySlotIds.Contains(item.SlotId))
                    {
                        _logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{item.SlotId}' (slot was empty at snapshot time - loot lost): {item.Template}");
                    }
                    else
                    {
                        // Slot is in IncludedSlots but wasn't in snapshot (maybe added after snapshot?)
                        _logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{item.SlotId}' (slot is managed but had no snapshot data): {item.Template}");
                    }
                }
            }

            // Find all children of items to remove (items inside containers)
            bool foundMore = true;
            while (foundMore)
            {
                foundMore = false;
                foreach (var item in pmcData.Inventory.Items)
                {
                    if (equipmentItemIds.Contains(item.ParentId!) && !equipmentItemIds.Contains(item.Id!))
                    {
                        equipmentItemIds.Add(item.Id!);
                        foundMore = true;
                    }
                }
            }

            // Remove all equipment items
            pmcData.Inventory.Items.RemoveAll(item => equipmentItemIds.Contains(item.Id!));
            _logger.Debug($"[KeepStartingGear-Server] Removed {equipmentItemIds.Count} items");

            // CRITICAL: Build a set of all existing item IDs to prevent duplicates
            // This fixes the "An item with the same key has already been added" crash
            var existingItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in pmcData.Inventory.Items)
            {
                if (!string.IsNullOrEmpty(item.Id))
                {
                    existingItemIds.Add(item.Id);
                }
            }
            _logger.Debug($"[KeepStartingGear-Server] Existing inventory has {existingItemIds.Count} items before restoration");

            // Build a map of snapshot item IDs to their root slot
            // This lets us skip items from non-managed slots
            var snapshotItemSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in snapshot.Items)
            {
                if (string.IsNullOrEmpty(item.Id)) continue;

                // Find the root slot for this item by tracing up the parent chain
                string rootSlot = null;
                var currentItem = item;
                int maxDepth = 20; // Prevent infinite loops
                while (currentItem != null && maxDepth-- > 0)
                {
                    // If this item's parent is Equipment, its SlotId is the root slot
                    if (currentItem.ParentId == snapshotEquipmentId)
                    {
                        rootSlot = currentItem.SlotId;
                        break;
                    }

                    // Find parent item in snapshot
                    currentItem = snapshot.Items.FirstOrDefault(i => i.Id == currentItem.ParentId);
                }

                if (!string.IsNullOrEmpty(rootSlot))
                {
                    snapshotItemSlots[item.Id] = rootSlot;
                }
            }

            // Add snapshot items
            int addedCount = 0;
            int skippedDuplicates = 0;
            int skippedNonManaged = 0;
            foreach (var snapshotItem in snapshot.Items)
            {
                if (snapshotItem.Tpl == EquipmentContainerTpl)
                    continue;

                // Skip items with missing required data
                if (string.IsNullOrEmpty(snapshotItem.Id) || string.IsNullOrEmpty(snapshotItem.Tpl))
                {
                    _logger.Warning($"[KeepStartingGear-Server] Skipping item with missing Id or Tpl");
                    continue;
                }

                // Skip items from non-managed slots (they should be preserved, not restored from snapshot)
                // This prevents adding duplicate items when a slot like SecuredContainer is disabled
                if (snapshotItemSlots.TryGetValue(snapshotItem.Id, out var rootSlot) && !string.IsNullOrEmpty(rootSlot))
                {
                    if (!includedSlotIds.Contains(rootSlot))
                    {
                        _logger.Debug($"[KeepStartingGear-Server] Skipping item {snapshotItem.Id} from non-managed slot '{rootSlot}'");
                        skippedNonManaged++;
                        continue;
                    }
                }

                // CRITICAL: Check for duplicate item ID before adding
                // This prevents the "An item with the same key has already been added" crash
                if (existingItemIds.Contains(snapshotItem.Id))
                {
                    _logger.Warning($"[KeepStartingGear-Server] DUPLICATE PREVENTED: Item {snapshotItem.Id} (Tpl={snapshotItem.Tpl}) already exists in inventory - skipping to prevent crash");
                    skippedDuplicates++;
                    continue;
                }

                var newItem = new Item
                {
                    Id = snapshotItem.Id,
                    Template = snapshotItem.Tpl,
                    ParentId = snapshotItem.ParentId == snapshotEquipmentId
                        ? profileEquipmentId
                        : snapshotItem.ParentId,
                    SlotId = snapshotItem.SlotId
                };

                // Copy location data (grid position for container items OR integer position for cartridges)
                if (snapshotItem.LocationIndex.HasValue)
                {
                    // CARTRIDGE LOCATION: Use integer position for magazine cartridges
                    // SPT profiles expect cartridges to have integer locations (0, 1, 2, etc.)
                    newItem.Location = snapshotItem.LocationIndex.Value;
                    _logger.Debug($"[KeepStartingGear-Server] Restored cartridge position {snapshotItem.LocationIndex.Value} for {snapshotItem.Tpl}");
                }
                else if (snapshotItem.Location != null)
                {
                    // GRID LOCATION: Use x/y/r object for container items
                    newItem.Location = new ItemLocation
                    {
                        X = snapshotItem.Location.X,
                        Y = snapshotItem.Location.Y,
                        R = (ItemRotation)snapshotItem.Location.R,
                        IsSearched = snapshotItem.Location.IsSearched
                    };
                }

                if (snapshotItem.Upd != null)
                {
                    try
                    {
                        var updJson = JsonSerializer.Serialize(snapshotItem.Upd);
                        newItem.Upd = JsonSerializer.Deserialize<Upd>(updJson);
                    }
                    catch { }
                }

                pmcData.Inventory.Items.Add(newItem);
                existingItemIds.Add(newItem.Id!); // Track newly added item to prevent duplicates within snapshot
                addedCount++;
            }

            _logger.Debug($"[KeepStartingGear-Server] Added {addedCount} items from snapshot");
            if (skippedDuplicates > 0)
            {
                _logger.Debug($"[KeepStartingGear-Server] Skipped {skippedDuplicates} duplicate items");
            }
            if (skippedNonManaged > 0)
            {
                _logger.Debug($"[KeepStartingGear-Server] Skipped {skippedNonManaged} items from non-managed slots (preserved)");
            }

            // Delete snapshot file
            try
            {
                File.Delete(snapshotFile);
                _logger.Debug($"[KeepStartingGear-Server] Deleted snapshot file");
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[KeepStartingGear-Server] Error restoring from snapshot: {ex.Message}");
            return false;
        }
    }

    private static string ResolveSnapshotsPath()
    {
        try
        {
            string dllPath = typeof(CustomInRaidHelper).Assembly.Location;
            string? modFolder = System.IO.Path.GetDirectoryName(dllPath);
            string sptRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(modFolder!, "..", "..", "..", ".."));
            return System.IO.Path.Combine(sptRoot, "BepInEx", "plugins", ModFolderName, "snapshots");
        }
        catch
        {
            return "";
        }
    }
}
