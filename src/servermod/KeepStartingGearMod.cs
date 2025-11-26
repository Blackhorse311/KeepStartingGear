// ============================================================================
// Keep Starting Gear - Server Mod Entry Point
// ============================================================================
// This is the main entry point for the server-side component of the
// Keep Starting Gear mod. It registers the mod with SPT's dependency
// injection system and logs startup information.
//
// SERVER-SIDE ARCHITECTURE:
// The server component is a .NET 9 class library that runs inside the SPT
// server process. It uses SPT's dependency injection system to register
// services and intercept game callbacks.
//
// KEY COMPONENTS:
// - KeepStartingGearMod (this file): Entry point, IOnLoad implementation
// - RaidEndInterceptor: Intercepts EndLocalRaid to restore inventory
// - CustomInRaidHelper: Overrides DeleteInventory to preserve restored items
// - SnapshotRestorationState: Thread-safe state communication between components
// - ModMetadata: SPT mod registration information
//
// HOW IT WORKS:
// 1. SPT loads this assembly and discovers the [Injectable] classes
// 2. KeepStartingGearMod.OnLoad() runs during server startup
// 3. RaidEndInterceptor intercepts all EndLocalRaid requests
// 4. If player died with a valid snapshot, restore inventory from snapshot
// 5. CustomInRaidHelper skips normal inventory deletion after restoration
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Main entry point for the Keep Starting Gear server-side mod.
/// Implements IOnLoad to run initialization code during server startup.
/// </summary>
/// <remarks>
/// <para>
/// The server component works in conjunction with the BepInEx client component:
/// </para>
/// <list type="bullet">
///   <item>Client captures snapshots and saves them to a shared location</item>
///   <item>Server reads snapshots and replaces inventory on death</item>
/// </list>
/// <para>
/// This approach avoids the "Run-Through" status penalty by modifying
/// the inventory data BEFORE it's processed, rather than changing exit status.
/// </para>
/// </remarks>
/// <param name="logger">SPT logger instance injected by DI</param>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class KeepStartingGearMod(ISptLogger<KeepStartingGearMod> logger) : IOnLoad
{
    /// <summary>
    /// Called during server startup to initialize the mod.
    /// Logs startup information and confirms the mod is ready.
    /// </summary>
    /// <returns>Completed task (no async work needed)</returns>
    /// <remarks>
    /// The actual work of this mod is done in RaidEndInterceptor and
    /// CustomInRaidHelper. This OnLoad method just provides user feedback
    /// that the server component loaded successfully.
    /// </remarks>
    public Task OnLoad()
    {
        logger.Info("[KeepStartingGear-Server] Server component loaded successfully!");
        logger.Info("[KeepStartingGear-Server] Inventory restoration service ready.");
        logger.Info("[KeepStartingGear-Server] Snapshots location: H:\\SPT\\BepInEx\\plugins\\Blackhorse311-KeepStartingGear\\snapshots\\");

        return Task.CompletedTask;
    }
}
