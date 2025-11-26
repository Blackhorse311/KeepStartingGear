// ============================================================================
// Keep Starting Gear - Post-Raid Inventory Patch
// ============================================================================
// This patch detects when the player returns to the stash/hideout after a raid
// and triggers inventory restoration from the snapshot if the player died.
//
// NOTE: This is a LEGACY/FALLBACK approach. The preferred method is server-side
// restoration via RaidEndInterceptor which modifies the profile during raid end
// processing. This client-side patch is kept as a backup.
//
// HOOK POINT:
// GridSortPanel.Show() - Called when the inventory panel opens.
// By checking the caller (SimpleStashPanel), we can detect post-raid return.
//
// RESTORATION FLOW:
// 1. Player dies in raid
// 2. SPT processes raid end and saves profile (with empty inventory)
// 3. Player returns to hideout, stash panel opens
// 4. This patch detects the stash open via stack trace inspection
// 5. Looks for a snapshot to restore
// 6. Directly edits the profile JSON file to add snapshot items
// 7. Triggers backend reload to update the UI
//
// WHY THIS APPROACH?
// - Works WITH the system, not against it
// - Profile has already been saved, so we can safely modify it
// - Backend reload ensures UI shows the restored items
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Blackhorse311.KeepStartingGear.Services;
using SPT.Reflection.Patching;
using EFT;
using EFT.InventoryLogic;
using EFT.Communications;
using HarmonyLib;

namespace Blackhorse311.KeepStartingGear.Patches;

/// <summary>
/// Patch to detect when the player returns to the stash/hideout after a raid.
/// Triggers inventory restoration from snapshot if the player died.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this patch point?</b>
/// </para>
/// <list type="bullet">
///   <item>GridSortPanel.Show() fires when the inventory UI opens</item>
///   <item>By checking the caller, we can detect if it's the stash panel (post-raid)</item>
///   <item>At this point, SPT has already saved the profile (with empty inventory from death)</item>
///   <item>We can now safely restore items from our snapshot</item>
///   <item>This approach works WITH the system, not against it</item>
/// </list>
/// <para>
/// <b>Important:</b> This is a fallback approach. The server-side component
/// (RaidEndInterceptor) handles restoration more reliably during raid end processing.
/// </para>
/// </remarks>
public class PostRaidInventoryPatch : ModulePatch
{
    // ========================================================================
    // State Tracking
    // ========================================================================

    /// <summary>
    /// Tracks whether we've already attempted restoration this session.
    /// Prevents multiple restoration attempts if the stash opens multiple times.
    /// </summary>
    private static bool _hasAttemptedRestoration = false;

    // ========================================================================
    // Patch Target
    // ========================================================================

    /// <summary>
    /// Specifies which method to patch - GridSortPanel.Show().
    /// Uses reflection to find the type since it's not publicly accessible.
    /// </summary>
    /// <returns>MethodInfo for GridSortPanel.Show, or null if not found</returns>
    protected override MethodBase GetTargetMethod()
    {
        // Find GridSortPanel type by searching the assembly
        // This type is internal, so we need to search for it
        var gridSortPanelType = typeof(InventoryController).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "GridSortPanel" || t.FullName?.Contains("GridSortPanel") == true);

        if (gridSortPanelType == null)
        {
            Plugin.Log.LogError("Could not find GridSortPanel type!");
            return null;
        }

        Plugin.Log.LogDebug($"Found GridSortPanel type: {gridSortPanelType.FullName}");

        // Get the Show method which is called when the panel opens
        var showMethod = gridSortPanelType.GetMethod("Show", BindingFlags.Public | BindingFlags.Instance);
        if (showMethod == null)
        {
            Plugin.Log.LogError("Could not find Show method on GridSortPanel!");
            return null;
        }

        return showMethod;
    }

    // ========================================================================
    // Patch Implementation
    // ========================================================================

    /// <summary>
    /// Postfix method - runs after the inventory panel is shown.
    /// Checks if this is a post-raid stash open and triggers restoration.
    /// </summary>
    /// <param name="__instance">The GridSortPanel instance</param>
    /// <param name="controller">The player's InventoryController</param>
    /// <param name="item">The container item being displayed</param>
    /// <remarks>
    /// <para>
    /// This method uses stack trace inspection to determine WHY the panel
    /// is being opened. If the caller is SimpleStashPanel, we know the
    /// player has returned from a raid and should check for restoration.
    /// </para>
    /// </remarks>
    [PatchPostfix]
    private static void PatchPostfix(object __instance, InventoryController controller, CompoundItem item)
    {
        try
        {
            // Detect if this is being called from SimpleStashPanel
            // This indicates the player has returned from a raid to the hideout
            var stackTrace = new StackTrace();
            var callerFrame = stackTrace.GetFrame(2); // Get the caller's caller
            if (callerFrame == null)
                return;

            var callerClassType = callerFrame.GetMethod()?.ReflectedType;
            if (callerClassType == null)
                return;

            // Only process if this is the stash panel opening (post-raid return)
            // SimpleStashPanel is the hideout stash screen
            if (callerClassType.Name == "SimpleStashPanel" || callerClassType.FullName?.Contains("SimpleStashPanel") == true)
            {
                Plugin.Log.LogDebug("Stash panel opened - checking if restoration needed");

                // Only attempt restoration once per session
                // Prevents issues if player opens/closes stash multiple times
                if (_hasAttemptedRestoration)
                {
                    Plugin.Log.LogDebug("Already attempted restoration this session, skipping");
                    return;
                }

                _hasAttemptedRestoration = true;

                // Check if there's a snapshot to restore
                OnPostRaidStashOpen(controller);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error in PostRaidInventoryPatch: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    // ========================================================================
    // Restoration Logic
    // ========================================================================

    /// <summary>
    /// Called when the stash opens after returning from raid.
    /// Checks for a snapshot and restores inventory if found.
    /// </summary>
    /// <param name="controller">The player's InventoryController</param>
    private static void OnPostRaidStashOpen(InventoryController controller)
    {
        try
        {
            Plugin.Log.LogInfo("Stash opened - checking for any snapshots to restore");

            // Find the most recent snapshot file
            // We can't reliably get session ID at the stash screen, so we
            // just look for any recent snapshot
            var mostRecentSnapshot = SnapshotManager.Instance.GetMostRecentSnapshot();

            if (mostRecentSnapshot == null)
            {
                Plugin.Log.LogInfo("No snapshots found - nothing to restore");
                return;
            }

            // Validate the snapshot before attempting restoration
            if (!mostRecentSnapshot.IsValid())
            {
                Plugin.Log.LogWarning("Most recent snapshot is invalid - skipping restoration");
                SnapshotManager.Instance.ClearSnapshot(mostRecentSnapshot.SessionId);
                return;
            }

            Plugin.Log.LogInfo($"Found snapshot from {mostRecentSnapshot.Timestamp:HH:mm:ss} with {mostRecentSnapshot.Items.Count} items");
            Plugin.Log.LogInfo($"Snapshot session ID: {mostRecentSnapshot.SessionId}");
            Plugin.Log.LogInfo("Attempting to restore inventory via profile JSON manipulation...");

            // Restore the inventory by directly editing the profile JSON file
            // ProfileService handles all the JSON manipulation
            bool success = ProfileService.Instance.RestoreInventoryToProfile(mostRecentSnapshot);

            if (success)
            {
                Plugin.Log.LogInfo("Inventory restored successfully to profile JSON!");

                // Clear the snapshot after successful restoration
                // This prevents it from being restored again
                SnapshotManager.Instance.ClearSnapshot(mostRecentSnapshot.SessionId);

                // Show notification to user
                NotificationManagerClass.DisplayMessageNotification(
                    "[Keep Starting Gear] Inventory restored! Reloading profile...",
                    ENotificationDurationType.Default);

                // Trigger a profile reload from the server so the UI updates
                // Without this, the game shows stale data
                _ = ReloadProfileFromServerAsync();
            }
            else
            {
                Plugin.Log.LogError("Failed to restore inventory to profile");
                NotificationManagerClass.DisplayWarningNotification(
                    "[Keep Starting Gear] Failed to restore inventory! Check logs.",
                    ENotificationDurationType.Long);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error in OnPostRaidStashOpen: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Resets the restoration flag when a new raid starts.
    /// Called from GameStartPatch to allow restoration after the next raid.
    /// </summary>
    public static void ResetRestorationFlag()
    {
        _hasAttemptedRestoration = false;
        Plugin.Log.LogDebug("Restoration flag reset for new raid");
    }

    // ========================================================================
    // Profile Reload
    // ========================================================================

    /// <summary>
    /// Reloads the profile from the server to update the UI with restored items.
    /// Uses TarkovApplication.RecreateCurrentBackend() to force a full reload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this reload, the game UI shows stale (empty) inventory even though
    /// we've updated the profile JSON file on disk. The backend reload forces
    /// the game to re-read the profile from the server.
    /// </para>
    /// <para>
    /// Credit to DebugPlus mod for discovering this approach.
    /// </para>
    /// </remarks>
    private static async Task ReloadProfileFromServerAsync()
    {
        try
        {
            Plugin.Log.LogInfo("Attempting to reload profile from server...");

            // Check if TarkovApplication exists
            // TarkovApplication is the main game application class
            if (TarkovApplication.Exist(out var app))
            {
                Plugin.Log.LogInfo("Found TarkovApplication, triggering backend reload...");

                // RecreateCurrentBackend reloads all data from the server
                // This includes the profile we just modified
                await app.RecreateCurrentBackend();

                Plugin.Log.LogInfo("Backend reload completed - inventory should now be visible!");

                NotificationManagerClass.DisplayMessageNotification(
                    "[Keep Starting Gear] Inventory restored successfully!",
                    ENotificationDurationType.Default);
            }
            else
            {
                // Fallback: can't auto-reload, user needs to restart
                Plugin.Log.LogWarning("Could not find TarkovApplication - manual reload required");
                NotificationManagerClass.DisplayWarningNotification(
                    "[Keep Starting Gear] Please restart game to see restored items",
                    ENotificationDurationType.Long);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to reload profile: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            NotificationManagerClass.DisplayWarningNotification(
                "[Keep Starting Gear] Reload failed - please restart game",
                ENotificationDurationType.Long);
        }
    }
}
