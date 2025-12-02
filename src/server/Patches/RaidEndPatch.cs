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
    /// Prefix method - runs BEFORE the raid end processing.
    /// Handles notifications and snapshot cleanup based on exit status.
    /// </summary>
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
    /// <para>
    /// Note: We don't use the __instance parameter to avoid accessibility issues
    /// with the BaseLocalGame class in different SPT versions.
    /// </para>
    /// </remarks>
    [PatchPrefix]
    private static void PatchPrefix(ExitStatus exitStatus, string exitName)
    {
        try
        {
            Plugin.Log.LogInfo($"Raid ending - Exit status: {exitStatus}, Exit name: {exitName}");
            Plugin.Log.LogDebug($"Exit status enum value: {(int)exitStatus}");

            // Reset the snapshot limit tracking for the next raid
            // This allows a new snapshot to be taken in the next raid
            KeybindMonitor.ResetRaidState();

            // ================================================================
            // Determine Exit Type
            // ================================================================

            // Player died or failed to extract
            bool playerDied = exitStatus == ExitStatus.Killed ||
                             exitStatus == ExitStatus.MissingInAction ||
                             exitStatus == ExitStatus.Left;

            // Player extracted successfully
            bool playerExtracted = exitStatus == ExitStatus.Survived ||
                                  exitStatus == ExitStatus.Runner;

            // ================================================================
            // Handle Death (Server Does Restoration)
            // ================================================================
            if (playerDied)
            {
                Plugin.Log.LogInfo("Player died or failed to extract");

                // Check if we have a snapshot to restore
                var snapshot = SnapshotManager.Instance.GetMostRecentSnapshot();

                if (snapshot == null || !snapshot.IsValid())
                {
                    Plugin.Log.LogWarning("No valid snapshot found - gear will be lost normally");

                    // Show large centered red notification
                    NotificationOverlay.ShowError("No Snapshot Found!\nGear will be lost");
                    return;
                }

                Plugin.Log.LogInfo($"Found snapshot with {snapshot.Items.Count} items from {snapshot.Timestamp:HH:mm:ss}");
                Plugin.Log.LogInfo("Server will restore inventory from snapshot (items picked up after snapshot will be lost)");

                // IMPORTANT: DON'T clear the snapshot here!
                // The server needs to read it to perform restoration.
                // The server will delete the snapshot file after restoration.

                // Show large centered green notification
                NotificationOverlay.ShowSuccess($"Restoring Snapshot!\n{snapshot.Items.Count} items");
            }
            // ================================================================
            // Handle Extraction (Server Handles Snapshot Cleanup)
            // ================================================================
            else if (playerExtracted)
            {
                Plugin.Log.LogInfo("Player extracted successfully");

                // NOTE: We intentionally DO NOT clear the snapshot here on the client side.
                // The server handles snapshot cleanup in RaidEndInterceptor.EndLocalRaid()
                // which has authoritative information about the actual player's session.
                //
                // This prevents a bug where PMC bots using certain extracts (like code-locked
                // Smuggler's Boat) could trigger false extraction detection and wipe the
                // player's snapshot prematurely.

                var snapshot = SnapshotManager.Instance.GetMostRecentSnapshot();
                if (snapshot != null)
                {
                    Plugin.Log.LogInfo($"Snapshot exists with {snapshot.Items.Count} items - server will clear it");

                    // Show notification (server will handle actual cleanup)
                    NotificationOverlay.ShowSuccess("Extracted Successfully!");
                }
            }
            // ================================================================
            // Other Exit Statuses
            // ================================================================
            else
            {
                // Other exit statuses (Run, Transit, etc.) - no special handling
                Plugin.Log.LogInfo($"Exit status: {exitStatus} - no action taken");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error in RaidEndPatch: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }
}
