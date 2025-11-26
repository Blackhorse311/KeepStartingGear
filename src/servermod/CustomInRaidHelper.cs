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
        // Check if inventory was restored from snapshot
        if (SnapshotRestorationState.InventoryRestoredFromSnapshot)
        {
            _logger.Info("[KeepStartingGear-Server] Skipping DeleteInventory - inventory was restored from snapshot");

            // Reset the state for future requests
            SnapshotRestorationState.Reset();

            // Don't call base - this preserves the inventory we just restored
            // The rest of death processing (XP loss, etc.) still proceeds normally
            return;
        }

        // Normal death processing - call base to delete inventory
        // This path is taken when:
        // - No snapshot existed
        // - Snapshot restoration failed
        // - Player is a Scav (snapshots are PMC-only)
        _logger.Debug("[KeepStartingGear-Server] Normal death processing - DeleteInventory will proceed");
        base.DeleteInventory(pmcData, sessionId);
    }
}
