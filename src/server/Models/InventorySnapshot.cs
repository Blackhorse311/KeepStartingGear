// ============================================================================
// Keep Starting Gear - Inventory Snapshot Model
// ============================================================================
// This model represents a complete snapshot of a player's inventory at a
// specific point in time. It contains all the data needed to restore the
// player's equipment if they die in a raid.
//
// SNAPSHOT CONTENTS:
// - Session/Profile ID: Identifies which player this snapshot belongs to
// - Timestamp: When the snapshot was taken (for logging and debugging)
// - Location: Which map the snapshot was taken on (for per-map limits)
// - Items: Complete list of all captured items in serialized format
// - Configuration: Which slots were included, raid status, mod version
//
// JSON SERIALIZATION:
// Snapshots are serialized to JSON and saved to disk. The JSON property
// names use camelCase to match SPT's conventions and ensure compatibility.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Blackhorse311.KeepStartingGear.Models;

/// <summary>
/// Represents a snapshot of a player's inventory at a specific point in time.
/// This snapshot can be restored later to return the player's inventory to this state.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is the central data structure for the mod's functionality.
/// When a player presses the snapshot keybind, their current equipment is
/// captured and stored as a snapshot. If they die, this snapshot is used
/// to restore their inventory.
/// </para>
/// <para>
/// <b>Snapshot Lifecycle:</b>
/// </para>
/// <list type="number">
///   <item>Player presses keybind -> InventoryService.CaptureInventory() creates snapshot</item>
///   <item>SnapshotManager.SaveSnapshot() writes to disk as JSON</item>
///   <item>Player dies -> Server reads snapshot from disk</item>
///   <item>RaidEndInterceptor restores items to profile</item>
///   <item>Snapshot is deleted after successful restoration or extraction</item>
/// </list>
/// </remarks>
public class InventorySnapshot
{
    // ========================================================================
    // Identity Fields
    // ========================================================================

    /// <summary>
    /// The session/profile ID this snapshot belongs to.
    /// Used to identify which player owns this snapshot.
    /// </summary>
    /// <remarks>
    /// This corresponds to the player's profile ID in SPT, which is a unique
    /// identifier like "5c0647fdd443c22b77659123". The snapshot file is named
    /// using this ID.
    /// </remarks>
    [JsonProperty("sessionId")]
    public string SessionId { get; set; }

    /// <summary>
    /// Timestamp when this snapshot was created (UTC).
    /// Used for logging, debugging, and finding the most recent snapshot.
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The map/location where this snapshot was taken.
    /// Used to enforce the "one snapshot per map" limit during raids.
    /// </summary>
    /// <remarks>
    /// Location names are EFT's internal map IDs like:
    /// - "factory4_day" or "factory4_night" (Factory)
    /// - "bigmap" (Customs)
    /// - "Sandbox" (Ground Zero)
    /// - "Hideout" (when taken outside of raid)
    /// </remarks>
    [JsonProperty("location")]
    public string Location { get; set; }

    // ========================================================================
    // Inventory Data
    // ========================================================================

    /// <summary>
    /// The player's inventory items at the time of snapshot.
    /// Stored as serialized item data matching SPT's profile format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list contains all captured items including:
    /// </para>
    /// <list type="bullet">
    ///   <item>The Equipment container itself</item>
    ///   <item>All items in enabled equipment slots</item>
    ///   <item>All nested items (container contents, weapon mods, ammo)</item>
    /// </list>
    /// <para>
    /// Items maintain their parent-child relationships through ParentId
    /// and SlotId properties, allowing the complete hierarchy to be restored.
    /// </para>
    /// </remarks>
    [JsonProperty("items")]
    public List<SerializedItem> Items { get; set; }

    // ========================================================================
    // Configuration Metadata
    // ========================================================================

    /// <summary>
    /// List of slot names that were included in this snapshot.
    /// Used for logging and verification purposes.
    /// </summary>
    /// <remarks>
    /// Slot names like "FirstPrimaryWeapon", "Backpack", "TacticalVest", etc.
    /// This reflects the user's configuration at the time of capture.
    /// </remarks>
    [JsonProperty("includedSlots")]
    public List<string> IncludedSlots { get; set; }

    /// <summary>
    /// Whether this snapshot was taken while in raid or in hideout.
    /// Affects the snapshot limit rules (unlimited in hideout, one per map in raid).
    /// </summary>
    [JsonProperty("takenInRaid")]
    public bool TakenInRaid { get; set; }

    /// <summary>
    /// Version of the mod that created this snapshot.
    /// Used for compatibility checking if the snapshot format ever changes.
    /// </summary>
    [JsonProperty("modVersion")]
    public string ModVersion { get; set; }

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Creates a new empty inventory snapshot with default values.
    /// </summary>
    /// <remarks>
    /// Default values ensure the snapshot is valid for JSON serialization
    /// even before it's populated with actual data.
    /// </remarks>
    public InventorySnapshot()
    {
        SessionId = string.Empty;
        Timestamp = DateTime.UtcNow;
        Location = string.Empty;
        Items = new List<SerializedItem>();
        IncludedSlots = new List<string>();
        TakenInRaid = false;
        ModVersion = Plugin.PluginVersion;
    }

    // ========================================================================
    // Validation
    // ========================================================================

    /// <summary>
    /// Checks if this snapshot is valid and can be used for restoration.
    /// </summary>
    /// <returns>True if the snapshot has required data, false otherwise</returns>
    /// <remarks>
    /// A valid snapshot must have:
    /// <list type="bullet">
    ///   <item>A non-empty session ID (identifies the player)</item>
    ///   <item>A non-null items list</item>
    ///   <item>At least one item captured</item>
    /// </list>
    /// </remarks>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(SessionId) &&
               Items != null &&
               Items.Count > 0;
    }

    // ========================================================================
    // String Representation
    // ========================================================================

    /// <summary>
    /// Gets a human-readable description of this snapshot for logging.
    /// </summary>
    /// <returns>A formatted string describing the snapshot</returns>
    public override string ToString()
    {
        return $"Snapshot[Session={SessionId}, Location={Location}, Time={Timestamp:yyyy-MM-dd HH:mm:ss}, InRaid={TakenInRaid}]";
    }
}
