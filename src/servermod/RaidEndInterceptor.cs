// ============================================================================
// Keep Starting Gear - Raid End Interceptor
// ============================================================================
// This is the core server-side component that restores inventory from snapshots.
// It intercepts the EndLocalRaid callback and modifies inventory data before
// normal death processing occurs.
//
// HOW IT WORKS:
// 1. Game client sends EndLocalRaid request when raid ends
// 2. This interceptor receives the request FIRST (before normal processing)
// 3. If player died and has a snapshot, restore inventory from snapshot
// 4. Set flag to prevent normal inventory deletion
// 5. Pass request to normal processing (which now won't delete inventory)
//
// KEY INSIGHT:
// By modifying the inventory data BEFORE normal processing, we avoid the
// "Run-Through" status penalty. SPT just sees a dead player with inventory
// and processes it normally. The inventory happens to be our restored snapshot.
//
// SNAPSHOT FILES:
// Created by the BepInEx client mod and stored in:
// BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/{sessionId}.json
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;  // Disambiguate from SPT's Path class

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Intercepts EndLocalRaid to restore inventory from snapshot when player dies.
/// This is the key component that enables "snapshot-only restoration."
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot-Only Restoration:</b>
/// </para>
/// <list type="bullet">
///   <item>When player dies, we check for a snapshot file</item>
///   <item>If found, we REPLACE the player's current inventory with the snapshot</item>
///   <item>This means items picked up AFTER the snapshot are lost</item>
///   <item>The raid is processed normally (no Run-Through status!)</item>
/// </list>
/// <para>
/// The snapshot files are created by the BepInEx client mod when the player
/// presses the snapshot keybind (default: Ctrl+Alt+F8).
/// </para>
/// </remarks>
/// <param name="logger">SPT logger for console output</param>
/// <param name="httpResponseUtil">Utility for HTTP responses</param>
/// <param name="matchController">Controller for match/raid operations</param>
/// <param name="databaseService">Database service for item templates</param>
[Injectable]
public class RaidEndInterceptor(
    ISptLogger<RaidEndInterceptor> logger,
    HttpResponseUtil httpResponseUtil,
    MatchController matchController,
    DatabaseService databaseService)
    : MatchCallbacks(httpResponseUtil, matchController, databaseService)
{
    // ========================================================================
    // Path Configuration
    // ========================================================================

    /// <summary>
    /// Path to snapshot files created by the BepInEx client mod.
    /// Dynamically resolved based on the server mod's installation location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path is derived from the server mod's DLL location:
    /// </para>
    /// <list type="bullet">
    ///   <item>Server mod DLL: {SPT_ROOT}\SPT\user\mods\Blackhorse311-KeepStartingGear\*.dll</item>
    ///   <item>Navigate up 4 levels to reach SPT_ROOT</item>
    ///   <item>Then: {SPT_ROOT}\BepInEx\plugins\Blackhorse311-KeepStartingGear\snapshots\</item>
    /// </list>
    /// <para>
    /// This ensures the mod works regardless of where SPT is installed.
    /// </para>
    /// </remarks>
    private readonly string _snapshotsPath = ResolveSnapshotsPath();

    // ========================================================================
    // Constants
    // ========================================================================

    /// <summary>
    /// Equipment container template ID - identifies the root equipment container.
    /// All equipped items are children of this container.
    /// </summary>
    private const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

    /// <summary>
    /// Mod folder name - used for both server and client mod folders.
    /// </summary>
    private const string ModFolderName = "Blackhorse311-KeepStartingGear";

    // ========================================================================
    // Path Resolution
    // ========================================================================

    /// <summary>
    /// Resolves the snapshots path dynamically based on the server mod's DLL location.
    /// </summary>
    /// <returns>Full path to the snapshots directory</returns>
    /// <remarks>
    /// <para>
    /// Path resolution logic:
    /// </para>
    /// <list type="number">
    ///   <item>Get this DLL's location: {SPT_ROOT}\SPT\user\mods\{ModFolder}\*.dll</item>
    ///   <item>Navigate up to SPT_ROOT (4 parent directories)</item>
    ///   <item>Construct BepInEx path: {SPT_ROOT}\BepInEx\plugins\{ModFolder}\snapshots\</item>
    /// </list>
    /// </remarks>
    private static string ResolveSnapshotsPath()
    {
        try
        {
            // Get the directory where this DLL is located
            // e.g., {SPT_ROOT}/SPT/user/mods/Blackhorse311-KeepStartingGear/
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string modDirectory = Path.GetDirectoryName(dllPath);

            // Navigate up to SPT root:
            // From: {SPT_ROOT}\SPT\user\mods\{ModFolder}\
            // Up 1: {SPT_ROOT}\SPT\user\mods\
            // Up 2: {SPT_ROOT}\SPT\user\
            // Up 3: {SPT_ROOT}\SPT\
            // Up 4: {SPT_ROOT}\
            string sptRoot = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", "..", ".."));

            // Construct the BepInEx snapshots path
            // {SPT_ROOT}\BepInEx\plugins\{ModFolder}\snapshots\
            string snapshotsPath = Path.Combine(sptRoot, "BepInEx", "plugins", ModFolderName, "snapshots");

            return snapshotsPath;
        }
        catch (Exception)
        {
            // Fallback to a relative path if resolution fails
            // This shouldn't happen in normal circumstances
            return Path.Combine("..", "..", "..", "BepInEx", "plugins", ModFolderName, "snapshots");
        }
    }

    // ========================================================================
    // Main Entry Point
    // ========================================================================

    /// <summary>
    /// Intercepts the end of local raid processing.
    /// If player died and has a valid snapshot, restores inventory from snapshot.
    /// </summary>
    /// <param name="url">The request URL</param>
    /// <param name="info">Raid end data including exit status and profile</param>
    /// <param name="sessionID">Player's session/profile ID</param>
    /// <returns>HTTP response (null response for this callback)</returns>
    /// <remarks>
    /// <para>
    /// This method is called for EVERY raid end. It checks:
    /// </para>
    /// <list type="bullet">
    ///   <item>Is this a PMC? (Scav raids don't use snapshots)</item>
    ///   <item>Did the player die? (Survived players don't need restoration)</item>
    ///   <item>Is there a valid snapshot file?</item>
    /// </list>
    /// <para>
    /// After our processing, we always call the base implementation to ensure
    /// normal raid end processing continues (XP, quests, etc.).
    /// </para>
    /// </remarks>
    public override ValueTask<string> EndLocalRaid(string url, EndLocalRaidRequestData info, MongoId sessionID)
    {
        try
        {
            logger.Info($"[KeepStartingGear-Server] EndLocalRaid intercepted for session: {sessionID}");
            logger.Info($"[KeepStartingGear-Server] Exit status: {info.Results.Result}");
            logger.Info($"[KeepStartingGear-Server] Player side: {info.Results.Profile?.Info?.Side ?? "unknown"}");
            logger.Debug($"[KeepStartingGear-Server] Snapshots path: {_snapshotsPath}");

            // Only process PMC deaths (Scav uses separate inventory)
            bool isPmc = info.Results.Profile?.Info?.Side != "Savage";

            // Check if player died or failed to extract
            bool playerDied = info.Results.Result == ExitStatus.KILLED ||
                             info.Results.Result == ExitStatus.MISSINGINACTION ||
                             info.Results.Result == ExitStatus.LEFT;

            if (isPmc && playerDied)
            {
                logger.Info("[KeepStartingGear-Server] PMC death detected - checking for snapshot...");

                // Try to restore from snapshot
                bool restored = TryRestoreFromSnapshot(sessionID, info);

                if (restored)
                {
                    logger.Info("[KeepStartingGear-Server] Inventory restored from snapshot!");
                    logger.Info("[KeepStartingGear-Server] Items picked up after snapshot have been lost (as intended).");

                    // Set flag so CustomInRaidHelper skips DeleteInventory
                    SnapshotRestorationState.InventoryRestoredFromSnapshot = true;
                    logger.Info("[KeepStartingGear-Server] Set InventoryRestoredFromSnapshot flag to skip DeleteInventory");
                }
                else
                {
                    logger.Info("[KeepStartingGear-Server] No snapshot found or restoration failed - normal death processing.");
                }
            }
            else if (!playerDied)
            {
                // Player extracted - clear any snapshot to prevent accidental restoration
                logger.Info("[KeepStartingGear-Server] Player survived/extracted - clearing any snapshots...");
                ClearSnapshot(sessionID);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[KeepStartingGear-Server] Error in EndLocalRaid interceptor: {ex.Message}");
            logger.Error($"[KeepStartingGear-Server] Stack trace: {ex.StackTrace}");
        }

        // Always call the base implementation to complete normal processing
        // This handles XP, quests, insurance, and other raid end logic
        logger.Debug("[KeepStartingGear-Server] Calling base matchController.EndLocalRaid()...");
        matchController.EndLocalRaid(sessionID, info);
        logger.Debug("[KeepStartingGear-Server] Base matchController.EndLocalRaid() completed.");

        // Post-processing note (helps diagnose conflicts with other mods like SVM)
        if (SnapshotRestorationState.InventoryRestoredFromSnapshot)
        {
            logger.Info("[KeepStartingGear-Server] POST-PROCESSING: Inventory restoration completed.");
            logger.Info("[KeepStartingGear-Server] If gear is missing after raid, another mod (like SVM softcore) may be modifying inventory after us.");
            logger.Info("[KeepStartingGear-Server] Check if SVM or similar mods have their own gear protection disabled.");

            // Reset the flag for next raid
            SnapshotRestorationState.InventoryRestoredFromSnapshot = false;
        }

        return new ValueTask<string>(httpResponseUtil.NullResponse());
    }

    // ========================================================================
    // Snapshot Restoration
    // ========================================================================

    /// <summary>
    /// Attempts to restore inventory from a snapshot file.
    /// </summary>
    /// <param name="sessionID">Player's session/profile ID</param>
    /// <param name="info">Raid end data containing the profile to modify</param>
    /// <returns>True if restoration succeeded, false otherwise</returns>
    /// <remarks>
    /// <para>
    /// Restoration process:
    /// </para>
    /// <list type="number">
    ///   <item>Look for snapshot file matching session ID</item>
    ///   <item>Deserialize snapshot JSON</item>
    ///   <item>Find Equipment container in both snapshot and profile</item>
    ///   <item>Remove all current equipment items from profile</item>
    ///   <item>Add all snapshot items to profile (with ID remapping)</item>
    ///   <item>Delete snapshot file after successful restoration</item>
    /// </list>
    /// </remarks>
    private bool TryRestoreFromSnapshot(MongoId sessionID, EndLocalRaidRequestData info)
    {
        try
        {
            // Look for snapshot file
            var snapshotPath = Path.Combine(_snapshotsPath, $"{sessionID}.json");

            if (!File.Exists(snapshotPath))
            {
                logger.Info($"[KeepStartingGear-Server] No snapshot file found at: {snapshotPath}");
                return false;
            }

            logger.Info($"[KeepStartingGear-Server] Found snapshot file: {snapshotPath}");

            // Read and deserialize snapshot
            var snapshotJson = File.ReadAllText(snapshotPath);
            var snapshot = JsonSerializer.Deserialize<InventorySnapshot>(snapshotJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (snapshot == null || snapshot.Items == null || snapshot.Items.Count == 0)
            {
                logger.Warning("[KeepStartingGear-Server] Snapshot is empty or invalid");
                return false;
            }

            logger.Info($"[KeepStartingGear-Server] Snapshot contains {snapshot.Items.Count} items");

            // Get current inventory from the raid end data
            var currentInventory = info.Results.Profile?.Inventory;
            if (currentInventory == null || currentInventory.Items == null)
            {
                logger.Error("[KeepStartingGear-Server] Cannot access profile inventory");
                return false;
            }

            // ================================================================
            // Find Equipment Container IDs
            // ================================================================

            // Find Equipment container ID in the current profile
            string? equipmentId = null;
            foreach (var item in currentInventory.Items)
            {
                if (item.Template == EquipmentTemplateId)
                {
                    equipmentId = item.Id;
                    break;
                }
            }

            if (string.IsNullOrEmpty(equipmentId))
            {
                logger.Error("[KeepStartingGear-Server] Could not find Equipment container in profile");
                return false;
            }

            logger.Info($"[KeepStartingGear-Server] Profile Equipment ID: {equipmentId}");

            // Find Equipment container ID in the snapshot
            string? snapshotEquipmentId = null;
            foreach (var snapshotItem in snapshot.Items)
            {
                if (snapshotItem.Tpl == EquipmentTemplateId)
                {
                    snapshotEquipmentId = snapshotItem.Id;
                    break;
                }
            }

            logger.Info($"[KeepStartingGear-Server] Snapshot Equipment ID: {snapshotEquipmentId}");

            // ================================================================
            // Determine Which Slots Were Captured in Snapshot
            // ================================================================

            // Get the set of slot IDs that ARE in the snapshot
            // Items in slots NOT in this set should be preserved (user disabled that slot)
            var snapshotSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshotItem in snapshot.Items)
            {
                // Only count direct children of Equipment (top-level slots)
                if (snapshotItem.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(snapshotItem.SlotId))
                {
                    snapshotSlotIds.Add(snapshotItem.SlotId);
                }
            }

            logger.Info($"[KeepStartingGear-Server] Snapshot contains slots: {string.Join(", ", snapshotSlotIds)}");

            // ================================================================
            // Remove Current Equipment Items (Only From Captured Slots)
            // ================================================================

            // Collect all equipment-related item IDs that should be removed
            // Only remove items from slots that WERE captured in the snapshot
            var equipmentItemIds = new HashSet<string>();

            // Items to preserve (in slots not captured by snapshot)
            var preservedItemIds = new HashSet<string>();

            // First, find direct children of Equipment and categorize them
            foreach (var item in currentInventory.Items)
            {
                if (item.ParentId == equipmentId)
                {
                    // Check if this slot was captured in the snapshot
                    if (!string.IsNullOrEmpty(item.SlotId) && snapshotSlotIds.Contains(item.SlotId))
                    {
                        // This slot was captured - mark for removal (will be replaced by snapshot)
                        equipmentItemIds.Add(item.Id!);
                    }
                    else
                    {
                        // This slot was NOT captured - preserve it (user disabled this slot)
                        preservedItemIds.Add(item.Id!);
                        logger.Info($"[KeepStartingGear-Server] Preserving item in slot '{item.SlotId}' (not in snapshot): {item.Template}");
                    }
                }
            }

            // Then, recursively find all nested items
            // Items nested inside preserved items should also be preserved
            // Items nested inside removed items should also be removed
            bool foundMore = true;
            while (foundMore)
            {
                foundMore = false;
                foreach (var item in currentInventory.Items)
                {
                    if (item.ParentId != null)
                    {
                        // If parent is being removed, this item should be removed too
                        if (equipmentItemIds.Contains(item.ParentId) && !equipmentItemIds.Contains(item.Id!))
                        {
                            equipmentItemIds.Add(item.Id!);
                            foundMore = true;
                        }
                        // If parent is being preserved, this item should be preserved too
                        else if (preservedItemIds.Contains(item.ParentId) && !preservedItemIds.Contains(item.Id!))
                        {
                            preservedItemIds.Add(item.Id!);
                            foundMore = true;
                        }
                    }
                }
            }

            logger.Info($"[KeepStartingGear-Server] Found {equipmentItemIds.Count} equipment items to remove");
            logger.Info($"[KeepStartingGear-Server] Preserving {preservedItemIds.Count} items in non-snapshot slots");

            // Remove only equipment items that are in captured slots
            currentInventory.Items.RemoveAll(item => equipmentItemIds.Contains(item.Id!));

            logger.Info($"[KeepStartingGear-Server] Removed equipment items, {currentInventory.Items.Count} items remaining");

            // ================================================================
            // Add Snapshot Items
            // ================================================================

            int addedCount = 0;
            foreach (var snapshotItem in snapshot.Items)
            {
                // Skip the Equipment container - keep the profile's original
                if (snapshotItem.Tpl == EquipmentTemplateId)
                    continue;

                // Create new inventory item from snapshot
                var newItem = new Item
                {
                    Id = snapshotItem.Id,
                    Template = snapshotItem.Tpl,
                    SlotId = snapshotItem.SlotId
                };

                // Remap parent ID if it's the Equipment container
                // This ensures items go into the profile's Equipment, not the snapshot's
                if (snapshotItem.ParentId == snapshotEquipmentId)
                {
                    newItem.ParentId = equipmentId;
                }
                else
                {
                    newItem.ParentId = snapshotItem.ParentId;
                }

                // Copy location data (grid position for container items)
                if (snapshotItem.Location != null)
                {
                    newItem.Location = new ItemLocation
                    {
                        X = snapshotItem.Location.X,
                        Y = snapshotItem.Location.Y,
                        R = (ItemRotation)snapshotItem.Location.R
                    };
                }

                // Copy update data (stack count, durability, etc.)
                if (snapshotItem.Upd != null)
                {
                    try
                    {
                        // Serialize and deserialize to convert between types
                        var updJson = JsonSerializer.Serialize(snapshotItem.Upd);
                        newItem.Upd = JsonSerializer.Deserialize<Upd>(updJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        // Debug log for ammo/stack count issues
                        if (newItem.Upd?.StackObjectsCount != null && newItem.Upd.StackObjectsCount > 1)
                        {
                            logger.Info($"[KeepStartingGear-Server] [AMMO] Item {snapshotItem.Id} (Tpl={snapshotItem.Tpl}) has StackObjectsCount={newItem.Upd.StackObjectsCount}, ParentId={newItem.ParentId}, SlotId={newItem.SlotId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // If Upd conversion fails, skip it - item will use defaults
                        logger.Warning($"[KeepStartingGear-Server] Could not convert Upd for item {snapshotItem.Id}: {ex.Message}");
                    }
                }

                currentInventory.Items.Add(newItem);
                addedCount++;
            }

            logger.Info($"[KeepStartingGear-Server] Added {addedCount} items from snapshot");
            logger.Info($"[KeepStartingGear-Server] Total items now: {currentInventory.Items.Count}");

            // ================================================================
            // Cleanup
            // ================================================================

            // Delete the snapshot file after successful restoration
            try
            {
                File.Delete(snapshotPath);
                logger.Info($"[KeepStartingGear-Server] Deleted snapshot file: {snapshotPath}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[KeepStartingGear-Server] Failed to delete snapshot file: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[KeepStartingGear-Server] Error restoring from snapshot: {ex.Message}");
            logger.Error($"[KeepStartingGear-Server] Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    // ========================================================================
    // Snapshot Cleanup
    // ========================================================================

    /// <summary>
    /// Deletes the snapshot file for a session (called on successful extraction).
    /// </summary>
    /// <param name="sessionID">Player's session/profile ID</param>
    /// <remarks>
    /// When a player extracts successfully, they keep their loot normally.
    /// We clear the snapshot to prevent it from being accidentally restored
    /// in a future raid.
    /// </remarks>
    private void ClearSnapshot(MongoId sessionID)
    {
        try
        {
            var snapshotPath = Path.Combine(_snapshotsPath, $"{sessionID}.json");
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
                logger.Info($"[KeepStartingGear-Server] Cleared snapshot on extraction: {snapshotPath}");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[KeepStartingGear-Server] Failed to clear snapshot: {ex.Message}");
        }
    }
}

// ============================================================================
// Snapshot Data Classes
// These mirror the client-side structure for JSON deserialization
// ============================================================================

/// <summary>
/// Represents a snapshot of inventory items.
/// Mirrors the client-side InventorySnapshot class for deserialization.
/// </summary>
public class InventorySnapshot
{
    /// <summary>Player's session/profile ID</summary>
    public string? SessionId { get; set; }

    /// <summary>Profile ID (may be same as SessionId)</summary>
    public string? ProfileId { get; set; }

    /// <summary>Player's character name</summary>
    public string? PlayerName { get; set; }

    /// <summary>When the snapshot was taken</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Whether snapshot was taken during raid</summary>
    public bool IsInRaid { get; set; }

    /// <summary>Map/location where snapshot was taken</summary>
    public string? Location { get; set; }

    /// <summary>List of all captured items</summary>
    public List<SnapshotItem> Items { get; set; } = new();
}

/// <summary>
/// Represents a single item in the snapshot.
/// Uses JsonPropertyName to match the client's JSON field names.
/// </summary>
/// <remarks>
/// The client uses _id and _tpl (with underscores) to match SPT's format.
/// We use JsonPropertyName attributes to handle this during deserialization.
/// </remarks>
public class SnapshotItem
{
    /// <summary>Unique item instance ID</summary>
    [System.Text.Json.Serialization.JsonPropertyName("_id")]
    public string? Id { get; set; }

    /// <summary>Item template ID (what type of item)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("_tpl")]
    public string? Tpl { get; set; }

    /// <summary>Parent container's ID</summary>
    [System.Text.Json.Serialization.JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>Slot or grid ID within parent</summary>
    [System.Text.Json.Serialization.JsonPropertyName("slotId")]
    public string? SlotId { get; set; }

    /// <summary>Grid position (for container items)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("location")]
    public ItemLocationData? Location { get; set; }

    /// <summary>Update data (stack count, durability, etc.)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("upd")]
    public object? Upd { get; set; }
}

/// <summary>
/// Item location data for grid positioning.
/// </summary>
public class ItemLocationData
{
    /// <summary>X coordinate in grid</summary>
    public int X { get; set; }

    /// <summary>Y coordinate in grid</summary>
    public int Y { get; set; }

    /// <summary>Rotation (0=horizontal, 1=vertical)</summary>
    public int R { get; set; }
}
