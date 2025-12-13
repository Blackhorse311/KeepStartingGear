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
            string? modDirectory = Path.GetDirectoryName(dllPath);

            if (string.IsNullOrEmpty(modDirectory))
            {
                throw new InvalidOperationException("Could not determine mod directory from DLL path");
            }

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
            var playerSide = info.Results?.Profile?.Info?.Side ?? "unknown";

            logger.Debug($"[KeepStartingGear-Server] EndLocalRaid intercepted for session: {sessionID}");
            logger.Debug($"[KeepStartingGear-Server] Exit status: {info.Results?.Result}, Player side: {playerSide}");
            logger.Debug($"[KeepStartingGear-Server] Snapshots path: {_snapshotsPath}");

            // Only process PMC deaths (Scav uses separate inventory)
            bool isPmc = playerSide != "Savage";

            // Check if player died or failed to extract
            var exitResult = info.Results?.Result;
            bool playerDied = exitResult == ExitStatus.KILLED ||
                             exitResult == ExitStatus.MISSINGINACTION ||
                             exitResult == ExitStatus.LEFT;

            if (isPmc && playerDied)
            {
                logger.Debug("[KeepStartingGear-Server] PMC death detected - checking for snapshot...");

                // Try to restore from snapshot
                bool restored = TryRestoreFromSnapshot(sessionID, info);

                if (restored)
                {
                    logger.Info("[KeepStartingGear-Server] Inventory restored from snapshot!");

                    // Set flag so CustomInRaidHelper skips DeleteInventory
                    SnapshotRestorationState.InventoryRestoredFromSnapshot = true;
                    logger.Debug("[KeepStartingGear-Server] Set InventoryRestoredFromSnapshot flag to skip DeleteInventory");
                }
                else
                {
                    logger.Debug("[KeepStartingGear-Server] No snapshot found or restoration failed - normal death processing.");
                }
            }
            else if (!playerDied)
            {
                // Player extracted - clear any snapshot to prevent accidental restoration
                logger.Debug("[KeepStartingGear-Server] Player survived/extracted - clearing any snapshots...");
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
            logger.Debug("[KeepStartingGear-Server] POST-PROCESSING: Inventory restoration completed.");

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
                logger.Debug($"[KeepStartingGear-Server] No snapshot file found at: {snapshotPath}");
                return false;
            }

            logger.Debug($"[KeepStartingGear-Server] Found snapshot file: {snapshotPath}");

            // Read and deserialize snapshot
            var snapshotJson = File.ReadAllText(snapshotPath);

            // Debug: Log part of the raw JSON to verify structure
            logger.Debug($"[KeepStartingGear-Server] Raw snapshot JSON preview: {snapshotJson.Substring(0, Math.Min(500, snapshotJson.Length))}...");

            var snapshot = JsonSerializer.Deserialize<InventorySnapshot>(snapshotJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (snapshot == null || snapshot.Items == null || snapshot.Items.Count == 0)
            {
                logger.Warning("[KeepStartingGear-Server] Snapshot is empty or invalid");
                return false;
            }

            logger.Debug($"[KeepStartingGear-Server] Snapshot contains {snapshot.Items.Count} items");

            // Debug: Log deserialized IncludedSlots
            if (snapshot.IncludedSlots != null)
            {
                logger.Debug($"[KeepStartingGear-Server] Deserialized IncludedSlots: [{string.Join(", ", snapshot.IncludedSlots)}]");
            }
            else
            {
                logger.Debug("[KeepStartingGear-Server] IncludedSlots is NULL after deserialization (legacy snapshot)");
            }

            // Get current inventory from the raid end data
            var currentInventory = info.Results?.Profile?.Inventory;
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

            logger.Debug($"[KeepStartingGear-Server] Profile Equipment ID: {equipmentId}");

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

            logger.Debug($"[KeepStartingGear-Server] Snapshot Equipment ID: {snapshotEquipmentId}");

            // ================================================================
            // Determine Which Slots Were Configured for Capture
            // ================================================================

            // Get the set of slot IDs that USER ENABLED in settings (IncludedSlots)
            // This is the authoritative list of what slots should be managed by the mod
            var includedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.IncludedSlots != null && snapshot.IncludedSlots.Count > 0)
            {
                foreach (var slot in snapshot.IncludedSlots)
                {
                    includedSlotIds.Add(slot);
                }
                logger.Debug($"[KeepStartingGear-Server] User configured slots to manage: {string.Join(", ", includedSlotIds)}");
            }
            else
            {
                logger.Debug("[KeepStartingGear-Server] No IncludedSlots in snapshot - legacy snapshot, using item-based detection");
            }

            // Get the set of slot IDs that HAVE ITEMS in the snapshot
            var snapshotSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshotItem in snapshot.Items)
            {
                // Only count direct children of Equipment (top-level slots)
                if (snapshotItem.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(snapshotItem.SlotId))
                {
                    snapshotSlotIds.Add(snapshotItem.SlotId);
                }
            }

            logger.Debug($"[KeepStartingGear-Server] Snapshot contains slots with items: {string.Join(", ", snapshotSlotIds)}");

            // Get the set of slot IDs that were EMPTY at snapshot time
            // Items in these slots should be REMOVED (they were looted during raid)
            var emptySlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.EmptySlots != null && snapshot.EmptySlots.Count > 0)
            {
                foreach (var emptySlot in snapshot.EmptySlots)
                {
                    emptySlotIds.Add(emptySlot);
                }
                logger.Debug($"[KeepStartingGear-Server] Snapshot tracked empty slots: {string.Join(", ", emptySlotIds)}");
            }

            // ================================================================
            // Remove Current Equipment Items (From Managed Slots)
            // ================================================================

            // Collect all equipment-related item IDs that should be removed
            // Remove items from slots that are MANAGED by the mod (in IncludedSlots)
            // Preserve items from slots NOT managed by the mod (user disabled them)
            var equipmentItemIds = new HashSet<string>();

            // Items to preserve (in slots NOT managed by the mod)
            // Note: ALL equipment items are removed on death, then snapshot items are restored

            // First, find direct children of Equipment and categorize them
            foreach (var item in currentInventory.Items)
            {
                if (item.ParentId == equipmentId)
                {
                    var slotId = item.SlotId ?? "";

                    // Determine if this slot is managed by the mod
                    // A slot is managed if:
                    // 1. It's in IncludedSlots (user enabled it), OR
                    // 2. No IncludedSlots exist (legacy snapshot) AND it's in snapshotSlotIds or emptySlotIds
                    bool slotIsManaged;
                    if (includedSlotIds.Count > 0)
                    {
                        // Modern snapshot: use IncludedSlots as authoritative source
                        slotIsManaged = includedSlotIds.Contains(slotId);
                    }
                    else
                    {
                        // Legacy snapshot: fall back to old behavior
                        slotIsManaged = snapshotSlotIds.Contains(slotId) || emptySlotIds.Contains(slotId);
                    }

                    // ALL equipment items are removed (player died)
                    // The difference is:
                    // - Managed slots: Items will be RESTORED from snapshot
                    // - Non-managed slots: Items will be LOST (normal death penalty)
                    equipmentItemIds.Add(item.Id!);

                    if (slotIsManaged)
                    {
                        if (snapshotSlotIds.Contains(slotId))
                        {
                            logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{slotId}' (will be restored from snapshot): {item.Template}");
                        }
                        else if (emptySlotIds.Contains(slotId))
                        {
                            logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{slotId}' (slot was empty at snapshot time - loot lost): {item.Template}");
                        }
                        else
                        {
                            // Slot is in IncludedSlots but wasn't in snapshot (maybe added after snapshot?)
                            logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{slotId}' (slot is managed but had no snapshot data): {item.Template}");
                        }
                    }
                    else
                    {
                        // This slot is NOT managed by the mod - normal death penalty applies
                        // Items in this slot are LOST (not restored from snapshot)
                        logger.Debug($"[KeepStartingGear-Server] Removing item from slot '{slotId}' (slot not protected - normal death penalty): {item.Template}");
                    }
                }
            }

            // Then, recursively find all nested items (items inside containers being removed)
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
                    }
                }
            }

            logger.Debug($"[KeepStartingGear-Server] Found {equipmentItemIds.Count} equipment items to remove");

            // Remove only equipment items that are in captured slots
            currentInventory.Items.RemoveAll(item => equipmentItemIds.Contains(item.Id!));

            logger.Debug($"[KeepStartingGear-Server] Removed equipment items, {currentInventory.Items.Count} items remaining");

            // ================================================================
            // Add Snapshot Items
            // ================================================================

            // CRITICAL: Build a set of all existing item IDs to prevent duplicates
            // This fixes the "An item with the same key has already been added" crash
            var existingItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in currentInventory.Items)
            {
                if (!string.IsNullOrEmpty(item.Id))
                {
                    existingItemIds.Add(item.Id);
                }
            }
            logger.Debug($"[KeepStartingGear-Server] Existing inventory has {existingItemIds.Count} items before restoration");

            int addedCount = 0;
            int skippedDuplicates = 0;
            foreach (var snapshotItem in snapshot.Items)
            {
                // Skip the Equipment container - keep the profile's original
                if (snapshotItem.Tpl == EquipmentTemplateId)
                    continue;

                // Skip items with missing required data
                if (string.IsNullOrEmpty(snapshotItem.Id) || string.IsNullOrEmpty(snapshotItem.Tpl))
                {
                    logger.Warning($"[KeepStartingGear-Server] Skipping item with missing Id or Tpl");
                    continue;
                }

                // CRITICAL: Check for duplicate item ID before adding
                // This prevents the "An item with the same key has already been added" crash
                if (existingItemIds.Contains(snapshotItem.Id))
                {
                    logger.Debug($"[KeepStartingGear-Server] DUPLICATE PREVENTED: Item {snapshotItem.Id} already exists - skipping");
                    skippedDuplicates++;
                    continue;
                }

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

                // Copy location data (grid position for container items OR integer position for cartridges)
                if (snapshotItem.LocationIndex.HasValue)
                {
                    // CARTRIDGE LOCATION: Use integer position for magazine cartridges
                    // SPT profiles expect cartridges to have integer locations (0, 1, 2, etc.)
                    // This is set directly on the Location property as an integer
                    newItem.Location = snapshotItem.LocationIndex.Value;

                    logger.Debug($"[KeepStartingGear-Server] [CARTRIDGE] Restored cartridge position {snapshotItem.LocationIndex.Value} for {snapshotItem.Tpl}");
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

                // Copy update data (stack count, durability, etc.)
                if (snapshotItem.Upd != null)
                {
                    try
                    {
                        // Serialize and deserialize to convert between types
                        var updJson = JsonSerializer.Serialize(snapshotItem.Upd);
                        logger.Debug($"[KeepStartingGear-Server] [UPD] Raw Upd JSON for {snapshotItem.Id}: {updJson}");

                        newItem.Upd = JsonSerializer.Deserialize<Upd>(updJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        // If standard deserialization failed to get StackObjectsCount, try parsing directly
                        if (newItem.Upd != null && (newItem.Upd.StackObjectsCount == null || newItem.Upd.StackObjectsCount == 0))
                        {
                            // Try to extract StackObjectsCount directly from the JsonElement
                            if (snapshotItem.Upd is JsonElement updElement)
                            {
                                if (updElement.TryGetProperty("StackObjectsCount", out var stackProp) ||
                                    updElement.TryGetProperty("stackObjectsCount", out stackProp))
                                {
                                    if (stackProp.TryGetInt32(out int stackCount) && stackCount > 0)
                                    {
                                        newItem.Upd.StackObjectsCount = stackCount;
                                        logger.Debug($"[KeepStartingGear-Server] [UPD] Manually extracted StackObjectsCount={stackCount} for {snapshotItem.Id}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // If Upd conversion fails, skip it - item will use defaults
                        logger.Warning($"[KeepStartingGear-Server] Could not convert Upd for item {snapshotItem.Id}: {ex.Message}");
                    }
                }

                currentInventory.Items.Add(newItem);
                existingItemIds.Add(newItem.Id!); // Track newly added item to prevent duplicates within snapshot
                addedCount++;
            }

            logger.Debug($"[KeepStartingGear-Server] Added {addedCount} items from snapshot, total now: {currentInventory.Items.Count}");
            if (skippedDuplicates > 0)
            {
                logger.Debug($"[KeepStartingGear-Server] Skipped {skippedDuplicates} duplicate items");
            }

            // ================================================================
            // Cleanup
            // ================================================================

            // Delete the snapshot file after successful restoration
            try
            {
                File.Delete(snapshotPath);
                logger.Debug($"[KeepStartingGear-Server] Deleted snapshot file: {snapshotPath}");
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
                logger.Debug($"[KeepStartingGear-Server] Cleared snapshot on extraction: {snapshotPath}");
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
/// <remarks>
/// All properties use explicit JsonPropertyName attributes to match the client's
/// Newtonsoft.Json serialization which uses camelCase property names.
/// </remarks>
public class InventorySnapshot
{
    /// <summary>Player's session/profile ID</summary>
    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>Profile ID (may be same as SessionId)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("profileId")]
    public string? ProfileId { get; set; }

    /// <summary>Player's character name</summary>
    [System.Text.Json.Serialization.JsonPropertyName("playerName")]
    public string? PlayerName { get; set; }

    /// <summary>When the snapshot was taken</summary>
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>Whether snapshot was taken during raid (client uses takenInRaid)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("takenInRaid")]
    public bool TakenInRaid { get; set; }

    /// <summary>Map/location where snapshot was taken</summary>
    [System.Text.Json.Serialization.JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>List of all captured items</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<SnapshotItem> Items { get; set; } = new();

    /// <summary>List of slot names that were included in the snapshot config</summary>
    [System.Text.Json.Serialization.JsonPropertyName("includedSlots")]
    public List<string>? IncludedSlots { get; set; }

    /// <summary>
    /// List of slot names that were enabled but empty at snapshot time.
    /// Items in these slots should be REMOVED during restoration.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("emptySlots")]
    public List<string>? EmptySlots { get; set; }

    /// <summary>Mod version that created this snapshot</summary>
    [System.Text.Json.Serialization.JsonPropertyName("modVersion")]
    public string? ModVersion { get; set; }
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

    /// <summary>
    /// Integer position index for cartridges in magazines.
    /// SPT profiles use integer locations (0, 1, 2, etc.) for cartridges
    /// instead of the grid-style x/y/r object used for container items.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("locationIndex")]
    public int? LocationIndex { get; set; }

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
    [System.Text.Json.Serialization.JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>Y coordinate in grid</summary>
    [System.Text.Json.Serialization.JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>Rotation (0=horizontal, 1=vertical)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("r")]
    public int R { get; set; }

    /// <summary>Whether the item has been searched/inspected</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isSearched")]
    public bool IsSearched { get; set; }
}
