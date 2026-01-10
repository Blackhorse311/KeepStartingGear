// ============================================================================
// Keep Starting Gear - Raid End Patch
// ============================================================================
// This patch detects when a raid ends and handles snapshot management based
// on the exit status (death, extraction, etc.).
//
// CLIENT-SERVER COORDINATION:
// This client-side patch works in conjunction with the server-side component:
// - Client (this patch): Saves snapshots, shows notifications, clears on extraction
// - Server (RaidEndInterceptor): Intercepts EndLocalRaid, restores inventory on death
//
// EXIT STATUSES HANDLED:
// - Killed/MissingInAction/Left: Player died or failed to extract -> trigger restore
// - Survived/Runner: Player extracted successfully -> clear snapshot
// - Other: No action taken
//
// SNAPSHOT LIFECYCLE:
// 1. Player takes snapshot during raid (via KeybindMonitor)
// 2. Player dies -> This patch detects death, shows notification
// 3. Server reads snapshot and restores inventory to profile
// 4. Server deletes snapshot file after successful restoration
// OR
// 1. Player takes snapshot during raid
// 2. Player extracts successfully
// 3. This patch clears the snapshot (no restoration needed)
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Reflection;
using Blackhorse311.KeepStartingGear.Components;
using Blackhorse311.KeepStartingGear.Constants;
using Blackhorse311.KeepStartingGear.Services;
using SPT.Reflection.Patching;
using Comfort.Common;
using EFT;
using EFT.Communications;

namespace Blackhorse311.KeepStartingGear.Patches;

/// <summary>
/// Patch to detect raid end and handle snapshot management.
/// Coordinates between client-side notifications and server-side restoration.
/// </summary>
/// <remarks>
/// <para>
/// This hybrid approach allows for "snapshot-only restoration" without Run-Through status:
/// </para>
/// <list type="bullet">
///   <item>When player dies, the server replaces their inventory with the snapshot</item>
///   <item>Items picked up AFTER the snapshot are lost (as intended)</item>
///   <item>No Run-Through status penalty!</item>
/// </list>
/// <para>
/// <b>Important:</b> The client does NOT perform restoration itself. It only:
/// </para>
/// <list type="number">
///   <item>Resets the raid state for KeybindMonitor</item>
///   <item>Shows appropriate notifications to the user</item>
///   <item>Clears snapshots on successful extraction</item>
/// </list>
/// </remarks>
public class RaidEndPatch : ModulePatch
{
    // ========================================================================
    // Patch Target
    // ========================================================================

    /// <summary>
    /// Specifies which method to patch - BaseLocalGame.Stop().
    /// </summary>
    /// <returns>MethodInfo for the Stop method</returns>
    /// <remarks>
    /// BaseLocalGame.Stop() is called when a raid ends for any reason.
    /// The exitStatus parameter tells us WHY it ended.
    /// Uses SPT.Reflection.Utils to find the type to handle accessibility changes across versions.
    /// </remarks>
    protected override MethodBase GetTargetMethod()
    {
        // Find BaseLocalGame<EftGamePlayerOwner> type using SPT reflection utilities
        // This handles cases where the class may be internal in some SPT versions
        var localGameType = SPT.Reflection.Utils.PatchConstants.LocalGameType;

        return localGameType.GetMethod(
            "Stop",
            BindingFlags.Public | BindingFlags.Instance
        );
    }

    // ========================================================================
    // Patch Implementation
    // ========================================================================

    /// <summary>
    /// Tracks whether we've already processed the raid end for this session.
    /// Prevents duplicate processing from bot extractions triggering false positives.
    /// </summary>
    private static bool _raidEndProcessed = false;

    /// <summary>
    /// Resets the raid end tracking flag. Called when a new raid starts.
    /// </summary>
    public static void ResetRaidEndFlag()
    {
        _raidEndProcessed = false;
    }

    /// <summary>
    /// Prefix method - runs BEFORE the raid end processing.
    /// Handles notifications and snapshot cleanup based on exit status.
    /// </summary>
    /// <param name="__instance">The game instance (used to verify main player)</param>
    /// <param name="exitStatus">Why the raid ended (Killed, Survived, etc.)</param>
    /// <param name="exitName">The name of the exit point used</param>
    /// <remarks>
    /// <para>
    /// We use a prefix (runs before original) because:
    /// </para>
    /// <list type="bullet">
    ///   <item>We want to show notifications while the game processes</item>
    ///   <item>For extractions, we need to clear the snapshot before SPT saves</item>
    ///   <item>For deaths, we leave the snapshot for the server to read</item>
    /// </list>
    /// <para>
    /// The server-side component handles actual inventory restoration.
    /// This client-side code only handles UI feedback and cleanup.
    /// </para>
    /// </remarks>
    [PatchPrefix]
    private static void PatchPrefix(object __instance, ExitStatus exitStatus, string exitName)
    {
        try
        {
            // ================================================================
            // BOT EXTRACTION FILTER
            // ================================================================
            // The Stop() method can be called for bot extractions as well as player extractions.
            // We need to verify this is actually the main player's raid ending.
            //
            // We use multiple checks:
            // 1. Check if we've already processed raid end (prevents duplicate notifications)
            // 2. Verify the GameWorld.MainPlayer is actually ending (not a bot)
            // 3. Check player health state to confirm they're actually dead/extracted
            // 4. Check if this is a Scav raid (Scav raids don't use snapshots)

            // Check 0: Is the mod enabled?
            if (!Configuration.Settings.ModEnabled.Value)
            {
                Plugin.Log.LogDebug("RaidEndPatch: Mod is disabled in settings - skipping");
                return;
            }

            // Check 1: Already processed this raid end?
            if (_raidEndProcessed)
            {
                Plugin.Log.LogDebug($"RaidEndPatch: Ignoring duplicate Stop() call (exitStatus={exitStatus}, exitName={exitName})");
                return;
            }

            // Check 1.5: Verify the __instance is the active game
            // This helps filter out bot/AI game instances that might call Stop()
            try
            {
                var instanceType = __instance.GetType();
                var statusProp = instanceType.GetProperty("Status") ?? instanceType.GetField("Status")?.GetValue(__instance) as object;
                if (statusProp != null)
                {
                    var status = instanceType.GetProperty("Status")?.GetValue(__instance);
                    Plugin.Log.LogDebug($"RaidEndPatch: Game instance status: {status}");
                }
            }
            catch { /* Ignore reflection errors */ }

            // Check 2: Verify this is actually the main player's game ending
            // Get GameWorld to check if main player is actually ending
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                Plugin.Log.LogDebug("RaidEndPatch: GameWorld is null - likely bot extraction, ignoring");
                return;
            }

            var mainPlayer = gameWorld.MainPlayer;
            if (mainPlayer == null)
            {
                Plugin.Log.LogDebug("RaidEndPatch: MainPlayer is null - likely bot extraction, ignoring");
                return;
            }

            // Check 3: Is this a Scav raid? (Scav raids don't use gear protection)
            // EPlayerSide.Savage = Scav, EPlayerSide.Usec/Bear = PMC
            if (mainPlayer.Side == EPlayerSide.Savage)
            {
                Plugin.Log.LogDebug("RaidEndPatch: Scav raid detected - KSG does not apply to Scav runs");
                return;
            }

            // Check 4: Categorize the exit status using exhaustive switch
            // This ensures we handle all known statuses and log any new ones
            var exitCategory = ExitStatusCategories.Categorize(exitStatus);

            if (exitCategory == ExitCategory.Unknown)
            {
                // Log unknown exit status for future investigation
                Plugin.Log.LogWarning($"RaidEndPatch: Unknown exit status encountered: {exitStatus} (value: {(int)exitStatus})");
                Plugin.Log.LogWarning("Please report this to the mod author so it can be properly categorized.");
                return;
            }

            Plugin.Log.LogDebug($"RaidEndPatch: Exit categorized as {exitCategory}: {ExitStatusCategories.GetDescription(exitStatus)}");

            bool playerDied = exitCategory == ExitCategory.Death;
            bool playerExtracted = exitCategory == ExitCategory.Extraction;

            if (playerDied)
            {
                // Verify the main player is actually dead or about to die
                var healthController = mainPlayer.HealthController;
                if (healthController != null && healthController.IsAlive)
                {
                    Plugin.Log.LogDebug($"RaidEndPatch: Main player is still alive - this is likely a bot death, ignoring");
                    return;
                }
            }
            else if (playerExtracted)
            {
                // For extraction, verify main player is alive (dead players don't extract)
                var healthController = mainPlayer.HealthController;
                if (healthController != null && !healthController.IsAlive)
                {
                    Plugin.Log.LogDebug($"RaidEndPatch: Main player is dead but exit status is {exitStatus} - likely bot extraction, ignoring");
                    return;
                }
            }

            // Mark that we've processed the raid end to prevent duplicate notifications
            _raidEndProcessed = true;

            Plugin.Log.LogDebug($"Raid ending - Exit status: {exitStatus}, Exit name: {exitName}");
            Plugin.Log.LogDebug($"Exit status enum value: {(int)exitStatus}");

            // Reset the snapshot limit tracking for the next raid
            // This allows a new snapshot to be taken in the next raid
            KeybindMonitor.ResetRaidState();

            // ================================================================
            // Handle Death (Server Does Restoration)
            // ================================================================
            if (playerDied)
            {
                Plugin.Log.LogDebug("Player died or failed to extract");

                // Check if we have a snapshot to restore
                var snapshot = SnapshotManager.Instance.GetMostRecentSnapshot();

                if (snapshot == null || !snapshot.IsValid())
                {
                    Plugin.Log.LogWarning("No valid snapshot found - gear will be lost normally");

                    // Show large centered red notification
                    NotificationOverlay.ShowError("No Snapshot Found!\nGear will be lost");
                    return;
                }

                Plugin.Log.LogDebug($"Found snapshot with {snapshot.Items.Count} items from {snapshot.Timestamp:HH:mm:ss}");
                Plugin.Log.LogDebug("Server will restore inventory from snapshot (items picked up after snapshot will be lost)");

                // IMPORTANT: DON'T clear the snapshot here!
                // The server needs to read it to perform restoration.
                // The server will delete the snapshot file after restoration.

                // Show large centered green notification
                NotificationOverlay.ShowSuccess($"Restoring Snapshot!\n{snapshot.Items.Count} items");
            }
            // ================================================================
            // Handle Extraction (Clear Snapshot)
            // ================================================================
            else if (playerExtracted)
            {
                Plugin.Log.LogDebug("Player extracted successfully");

                // Clear snapshot on extraction to prevent stale snapshots from persisting
                // This was previously server-only, but that fails when SVM is installed
                // because SVM can override the RaidEndInterceptor endpoint.
                //
                // We've verified this is the main player extracting (not a bot) via
                // the health checks above, so it's safe to clear the snapshot.

                var snapshot = SnapshotManager.Instance.GetMostRecentSnapshot();
                if (snapshot != null)
                {
                    Plugin.Log.LogDebug($"Clearing snapshot with {snapshot.Items.Count} items after successful extraction");
                    SnapshotManager.Instance.ClearSnapshot(snapshot.SessionId);

                    // Show notification
                    NotificationOverlay.ShowSuccess("Extracted Successfully!");
                }
            }
            // Note: Unknown statuses are handled at the top of this method
            // and will cause an early return with a warning log.
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error in RaidEndPatch: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }
}
