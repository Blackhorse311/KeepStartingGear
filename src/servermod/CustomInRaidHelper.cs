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
        _snapshotsPath = SnapshotRestorerHelper.ResolveSnapshotsPath();
        _restorer = new SnapshotRestorer<InRaidHelper>(logger, _snapshotsPath);
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
    /// We use TryConsume() to atomically check and reset the flag in a single operation,
    /// preventing race conditions.
    /// </para>
    /// </remarks>
    public override void DeleteInventory(PmcData pmcData, MongoId sessionId)
    {
        // Atomically check and consume the restoration flag
        // TryConsume() returns true if flag was set (and resets it), false otherwise
        if (SnapshotRestorationState.TryConsume())
        {
            _logger.Debug($"{Constants.LogPrefix} Skipping DeleteInventory - inventory was restored from snapshot");
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
            // Don't call base - we've restored the inventory
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
}
