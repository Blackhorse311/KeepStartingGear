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
// REFACTORED:
// Restoration logic has been extracted to SnapshotRestorer class to eliminate
// code duplication with CustomInRaidHelper.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

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
    // Dependencies
    // ========================================================================

    /// <summary>
    /// Path to snapshot files created by the BepInEx client mod.
    /// </summary>
    private readonly string _snapshotsPath = SnapshotRestorerHelper.ResolveSnapshotsPath();

    /// <summary>
    /// Logger instance stored for use in lazy initialization.
    /// CRITICAL-001 FIX: Store logger reference explicitly to avoid capturing constructor parameter in field initializer.
    /// </summary>
    private readonly ISptLogger<RaidEndInterceptor> _logger = logger;

    /// <summary>
    /// Lock object for thread-safe lazy initialization of the restorer.
    /// LINUS-001 FIX: The ??= operator is NOT atomic - two threads could both see null
    /// and create separate Lazy instances. Use explicit double-check locking.
    /// </summary>
    private static readonly object _restorerInitLock = new();

    /// <summary>
    /// Thread-safe lazy initialization using Lazy&lt;T&gt; with ExecutionAndPublication mode.
    /// LINUS-001 FIX: Initialize with explicit thread safety mode to prevent race conditions.
    /// </summary>
    private Lazy<SnapshotRestorer<RaidEndInterceptor>>? _restorerLazy;

    /// <summary>
    /// Gets or creates the snapshot restorer instance (thread-safe).
    /// LINUS-001 FIX: Uses double-check locking pattern for safe lazy initialization.
    /// The ??= operator alone is NOT sufficient - it's not atomic.
    /// </summary>
    private SnapshotRestorer<RaidEndInterceptor> Restorer
    {
        get
        {
            // First check without lock (fast path)
            var lazy = _restorerLazy;
            if (lazy != null)
                return lazy.Value;

            // Double-check with lock (slow path, only on first access)
            lock (_restorerInitLock)
            {
                // Check again inside lock
                if (_restorerLazy == null)
                {
                    _restorerLazy = new Lazy<SnapshotRestorer<RaidEndInterceptor>>(
                        () => new SnapshotRestorer<RaidEndInterceptor>(_logger, SnapshotRestorerHelper.ResolveSnapshotsPath()),
                        LazyThreadSafetyMode.ExecutionAndPublication
                    );
                }
                return _restorerLazy.Value;
            }
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

            logger.Debug($"{Constants.LogPrefix} EndLocalRaid intercepted for session: {sessionID}");
            logger.Debug($"{Constants.LogPrefix} Exit status: {info.Results?.Result}, Player side: {playerSide}");
            logger.Debug($"{Constants.LogPrefix} Snapshots path: {_snapshotsPath}");

            // Only process PMC deaths (Scav uses separate inventory)
            bool isPmc = playerSide != "Savage";

            // Check if player died or failed to extract
            var exitResult = info.Results?.Result;
            bool playerDied = exitResult == ExitStatus.KILLED ||
                             exitResult == ExitStatus.MISSINGINACTION ||
                             exitResult == ExitStatus.LEFT;

            if (isPmc && playerDied)
            {
                logger.Debug($"{Constants.LogPrefix} PMC death detected - checking for snapshot...");

                // Try to restore from snapshot
                var restoreResult = TryRestoreFromSnapshot(sessionID, info);

                if (restoreResult.Success)
                {
                    logger.Info($"{Constants.LogPrefix} Inventory restored from snapshot!");

                    // Set flag so CustomInRaidHelper only preserves managed slots
                    // Non-managed slots will still be deleted normally (player loses unprotected items)
                    SnapshotRestorationState.MarkRestored(sessionID.ToString(), restoreResult.ManagedSlotIds);
                    logger.Debug($"{Constants.LogPrefix} Set restoration state for session {sessionID} with {restoreResult.ManagedSlotIds?.Count ?? 0} managed slots");
                }
                else
                {
                    logger.Debug($"{Constants.LogPrefix} No snapshot found or restoration failed - normal death processing.");
                }
            }
            else if (!playerDied)
            {
                // Player extracted - clear any snapshot to prevent accidental restoration
                logger.Debug($"{Constants.LogPrefix} Player survived/extracted - clearing any snapshots...");
                ClearSnapshot(sessionID);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"{Constants.LogPrefix} Error in EndLocalRaid interceptor: {ex.Message}");
            logger.Error($"{Constants.LogPrefix} Stack trace: {ex.StackTrace}");
        }

        // Always call the base implementation to complete normal processing
        // This handles XP, quests, insurance, and other raid end logic
        logger.Debug($"{Constants.LogPrefix} Calling base matchController.EndLocalRaid()...");
        matchController.EndLocalRaid(sessionID, info);
        logger.Debug($"{Constants.LogPrefix} Base matchController.EndLocalRaid() completed.");

        // Note: The flag is now consumed atomically by CustomInRaidHelper.DeleteInventory()
        // via TryConsume(), so we don't need to reset it here.

        return new ValueTask<string>(httpResponseUtil.NullResponse());
    }

    // ========================================================================
    // Snapshot Restoration
    // ========================================================================

    /// <summary>
    /// Attempts to restore inventory from a snapshot file.
    /// Delegates to the shared SnapshotRestorer class.
    /// </summary>
    /// <param name="sessionID">Player's session/profile ID</param>
    /// <param name="info">Raid end data containing the profile to modify</param>
    /// <returns>A RestoreResult containing success status, item counts, managed slot IDs, and any error message.</returns>
    private RestoreResult TryRestoreFromSnapshot(MongoId sessionID, EndLocalRaidRequestData info)
    {
        // Get current inventory from the raid end data
        var currentInventory = info.Results?.Profile?.Inventory;
        if (currentInventory == null || currentInventory.Items == null)
        {
            logger.Error($"{Constants.LogPrefix} Cannot access profile inventory");
            return RestoreResult.Failed("Cannot access profile inventory");
        }

        // Delegate to shared restorer
        var result = Restorer.TryRestore(sessionID.ToString(), currentInventory.Items);

        if (result.Success)
        {
            logger.Info($"{Constants.LogPrefix} Restoration complete: {result.ItemsAdded} items added");
            if (result.DuplicatesSkipped > 0)
            {
                logger.Debug($"{Constants.LogPrefix} Skipped {result.DuplicatesSkipped} duplicate items");
            }
            if (result.NonManagedSkipped > 0)
            {
                logger.Debug($"{Constants.LogPrefix} Skipped {result.NonManagedSkipped} items from non-managed slots");
            }
        }
        else if (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage != "No snapshot file found")
        {
            logger.Warning($"{Constants.LogPrefix} Restoration failed: {result.ErrorMessage}");
        }

        return result;
    }

    // ========================================================================
    // Snapshot Cleanup
    // ========================================================================

    /// <summary>
    /// Deletes the snapshot file for a session (called on successful extraction).
    /// Delegates to the shared SnapshotRestorer.
    /// </summary>
    /// <param name="sessionID">Player's session/profile ID</param>
    private void ClearSnapshot(MongoId sessionID)
    {
        Restorer.ClearSnapshot(sessionID.ToString());
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

    /// <summary>
    /// Polymorphic location data - can be either an object (grid position) or integer (cartridge index).
    /// Use GetLocation() and GetLocationIndex() helper methods to access the typed values.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("location")]
    public JsonElement? Location { get; set; }

    /// <summary>
    /// Legacy property for backwards compatibility with old snapshots that used separate locationIndex.
    /// New snapshots use the polymorphic Location property instead.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("locationIndex")]
    public int? LocationIndex { get; set; }

    /// <summary>Update data (stack count, durability, etc.)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("upd")]
    public object? Upd { get; set; }

    /// <summary>
    /// Gets the grid location data if this item uses grid positioning.
    /// Returns null if the item uses cartridge index positioning instead.
    /// </summary>
    public ItemLocationData? GetLocation()
    {
        // Safely check if Location has a value
        if (!Location.HasValue)
            return null;

        var locationElement = Location.Value;

        // Check for null JSON value
        if (locationElement.ValueKind == JsonValueKind.Null || locationElement.ValueKind == JsonValueKind.Undefined)
            return null;

        // If it's a number, this is a cartridge index, not grid location
        if (locationElement.ValueKind == JsonValueKind.Number)
            return null;

        // It's an object - deserialize as grid location
        if (locationElement.ValueKind == JsonValueKind.Object)
        {
            try
            {
                return JsonSerializer.Deserialize<ItemLocationData>(locationElement.GetRawText());
            }
            catch (JsonException)
            {
                // Log deserialization failure but don't crash
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the cartridge index if this item uses integer positioning (magazine ammo).
    /// Returns null if the item uses grid positioning instead.
    /// Also checks the legacy LocationIndex property for backwards compatibility.
    /// </summary>
    public int? GetLocationIndex()
    {
        // Check legacy property first (for old snapshots)
        if (LocationIndex.HasValue)
            return LocationIndex.Value;

        // Safely check if Location has a value
        if (!Location.HasValue)
            return null;

        var locationElement = Location.Value;

        // Check for null JSON value
        if (locationElement.ValueKind == JsonValueKind.Null || locationElement.ValueKind == JsonValueKind.Undefined)
            return null;

        // If it's a number, return it as cartridge index
        if (locationElement.ValueKind == JsonValueKind.Number)
        {
            try
            {
                // Use TryGetInt32 to safely handle overflow
                if (locationElement.TryGetInt32(out int index))
                {
                    return index;
                }
                // Value is too large for Int32 - log warning and return null
                return null;
            }
            catch (InvalidOperationException)
            {
                // Not a number - shouldn't happen given the ValueKind check, but be safe
                return null;
            }
        }

        return null;
    }
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
