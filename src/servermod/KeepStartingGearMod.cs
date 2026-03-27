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
// - CustomInRaidHelper: Overrides InRaidHelper.DeleteInventory for restoration
// - RaidEndInterceptor: Handles restoration when SVM is not installed
// - SnapshotRestorationState: Thread-safe state communication between components
// - ModMetadata: SPT mod registration information
//
// HOW IT WORKS:
// 1. SPT loads this assembly and discovers the [Injectable] classes
// 2. KeepStartingGearMod.OnLoad() runs during server startup
// 3. When player dies, SPT calls InRaidHelper.DeleteInventory()
// 4. CustomInRaidHelper.DeleteInventory() checks for snapshots and restores
// 5. If restoration succeeds, normal inventory deletion is skipped
//
// SVM COMPATIBILITY:
// SVM overrides MatchCallbacks.EndLocalRaid via DI (dependency injection).
// We override InRaidHelper.DeleteInventory instead - a different service.
// Both mods can coexist since they hook different parts of the death flow.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using Blackhorse311.KeepStartingGear.Server.Compatibility;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;
using System.Reflection;

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
        logger.Info($"{Constants.LogPrefix} ============================================");
        logger.Info($"{Constants.LogPrefix} Keep Starting Gear v{Constants.ModVersion} - Server Component");
        logger.Info($"{Constants.LogPrefix} ============================================");
        logger.Info($"{Constants.LogPrefix} Inventory restoration service ready.");

        // Log the dynamically resolved snapshots path
        string snapshotsPath = ResolveSnapshotsPath();
        logger.Info($"{Constants.LogPrefix} Snapshots location: {snapshotsPath}");

        // Check for known conflicting mods
        CheckForConflictingMods();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks for known conflicting mods and logs warnings if detected.
    /// S-3 FIX: Replaced inline SVM-only folder scan with a call to ModDetector,
    /// which covers SVM, FIKA, Arcade Mode, Never Lose Equipments, Keep Your Equipment,
    /// and Fin's Hardcore Options in one pass. The previous inline code duplicated a
    /// subset of what ScanForSVM() in ModDetector already does.
    /// </summary>
    private void CheckForConflictingMods()
    {
        try
        {
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var logAdapter = new SimpleLoggerAdapter(
                debug: msg => logger.Debug(msg),
                info: msg => logger.Info(msg),
                warning: msg => logger.Warning(msg),
                error: msg => logger.Error(msg)
            );

            var detector = new ModDetector(logAdapter, dllPath);
            detector.ScanForMods();

            var criticalConflicts = detector.GetCriticalConflicts();
            foreach (var conflict in criticalConflicts)
            {
                logger.Warning($"{Constants.LogPrefix} CONFLICT: {conflict.DisplayName} detected - {conflict.ConflictReason}");
            }
        }
        catch (Exception ex)
        {
            logger.Debug($"{Constants.LogPrefix} Could not check for conflicting mods: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the snapshots path dynamically based on the server mod's DLL location.
    /// </summary>
    /// <returns>Full path to the snapshots directory</returns>
    private static string ResolveSnapshotsPath()
    {
        try
        {
            // Get the directory where this DLL is located
            // e.g., {SPT_ROOT}/SPT/user/mods/Blackhorse311-KeepStartingGear/
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? modDirectory = System.IO.Path.GetDirectoryName(dllPath);

            if (string.IsNullOrEmpty(modDirectory))
            {
                return "<path resolution failed: could not get mod directory>";
            }

            // Navigate up to SPT root:
            // From: {SPT_ROOT}\SPT\user\mods\{ModFolder}\
            // Up 4 levels to: {SPT_ROOT}\
            string sptRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(modDirectory, "..", "..", "..", ".."));

            // Construct the BepInEx snapshots path
            // {SPT_ROOT}\BepInEx\plugins\{ModFolder}\snapshots\
            return System.IO.Path.Combine(sptRoot, "BepInEx", "plugins", Constants.ModFolderName, "snapshots");
        }
        catch (Exception ex)
        {
            return $"<path resolution failed: {ex.Message}>";
        }
    }
}
