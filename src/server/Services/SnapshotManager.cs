// ============================================================================
// Keep Starting Gear - Snapshot Manager Service
// ============================================================================
// This service manages the persistence of inventory snapshots to disk.
// It handles all file I/O operations for saving, loading, and deleting
// snapshot JSON files.
//
// KEY RESPONSIBILITIES:
// 1. Save inventory snapshots as JSON files
// 2. Load snapshots from disk when needed for restoration
// 3. Find the most recent snapshot for a player
// 4. Clear (delete) snapshots after successful extraction
//
// FILE STORAGE:
// Snapshots are stored as individual JSON files in:
//   BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/
//
// Each file is named by the player's session/profile ID:
//   {sessionId}.json
//
// The server-side component also reads from this directory to restore
// inventory when a player dies.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.IO;
using System.Linq;
using Blackhorse311.KeepStartingGear.Models;
using Newtonsoft.Json;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service responsible for managing inventory snapshots on disk.
/// Handles saving, loading, and clearing player inventory snapshots.
/// </summary>
/// <remarks>
/// <para>
/// Uses the singleton pattern for global access. The snapshot directory
/// is created automatically if it doesn't exist.
/// </para>
/// <para>
/// <b>File Format:</b> JSON with indented formatting for readability.
/// Each snapshot file contains a complete InventorySnapshot object including
/// all items, metadata, and configuration information.
/// </para>
/// <para>
/// <b>Thread Safety:</b> File operations are not thread-safe. In practice,
/// this is not an issue because snapshot operations are triggered by user
/// input (keybind) or game events (raid end), not concurrent processes.
/// </para>
/// </remarks>
public class SnapshotManager
{
    // ========================================================================
    // Instance Fields
    // ========================================================================

    /// <summary>
    /// Full path to the directory where snapshot files are stored.
    /// Set during construction based on Plugin.GetDataPath().
    /// </summary>
    private readonly string _snapshotDirectory;

    // ========================================================================
    // Singleton Pattern
    // ========================================================================

    /// <summary>
    /// Singleton instance of the SnapshotManager.
    /// Set during construction and accessible from anywhere in the mod.
    /// </summary>
    public static SnapshotManager Instance { get; private set; }

    /// <summary>
    /// Constructor - initializes the snapshot manager and creates storage directory.
    /// Called once during plugin initialization.
    /// </summary>
    /// <remarks>
    /// The constructor:
    /// <list type="number">
    ///   <item>Gets the data path from the Plugin class</item>
    ///   <item>Ensures the snapshot directory exists</item>
    ///   <item>Sets the singleton instance reference</item>
    /// </list>
    /// </remarks>
    public SnapshotManager()
    {
        _snapshotDirectory = Plugin.GetDataPath();
        EnsureSnapshotDirectoryExists();
        Instance = this;
    }

    // ========================================================================
    // Directory Management
    // ========================================================================

    /// <summary>
    /// Ensures the snapshot storage directory exists, creating it if necessary.
    /// Called during initialization and before file operations.
    /// </summary>
    /// <remarks>
    /// The snapshot directory structure is:
    /// BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/
    /// </remarks>
    private void EnsureSnapshotDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_snapshotDirectory))
            {
                Directory.CreateDirectory(_snapshotDirectory);
                Plugin.Log.LogDebug($"Created snapshot directory: {_snapshotDirectory}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to create snapshot directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Constructs the full file path for a session's snapshot file.
    /// </summary>
    /// <param name="sessionId">The player's session/profile ID</param>
    /// <returns>Full path to the snapshot JSON file</returns>
    /// <remarks>
    /// File naming convention: {sessionId}.json
    /// Example: "5c0647fdd443c22b77659123.json"
    /// </remarks>
    private string GetSnapshotFilePath(string sessionId)
    {
        return Path.Combine(_snapshotDirectory, $"{sessionId}.json");
    }

    // ========================================================================
    // Snapshot Save/Load Operations
    // ========================================================================

    /// <summary>
    /// Saves an inventory snapshot to disk as a JSON file.
    /// Overwrites any existing snapshot for the same session.
    /// </summary>
    /// <param name="snapshot">The snapshot to save</param>
    /// <returns>True if save succeeded, false otherwise</returns>
    /// <remarks>
    /// <para>
    /// The snapshot is serialized to JSON with indented formatting for
    /// readability. Null values are included to maintain structure.
    /// </para>
    /// <para>
    /// The file is written atomically (single WriteAllText call) to minimize
    /// the risk of corruption from interrupted writes.
    /// </para>
    /// </remarks>
    public bool SaveSnapshot(InventorySnapshot snapshot)
    {
        try
        {
            // Validate the snapshot before saving
            if (!snapshot.IsValid())
            {
                Plugin.Log.LogError("Attempted to save invalid snapshot");
                return false;
            }

            // CRITICAL: Deduplicate items to prevent "An item with the same key has already been added" crash
            // This can happen if the capture logic accidentally adds the same item twice
            var seenIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduplicatedItems = new System.Collections.Generic.List<SerializedItem>();
            int duplicatesRemoved = 0;

            foreach (var item in snapshot.Items)
            {
                if (string.IsNullOrEmpty(item.Id))
                {
                    Plugin.Log.LogWarning("Skipping item with null/empty ID during save");
                    continue;
                }

                if (seenIds.Contains(item.Id))
                {
                    Plugin.Log.LogWarning($"[DUPLICATE REMOVED] Duplicate item ID detected in snapshot: {item.Id} (Tpl={item.Tpl})");
                    duplicatesRemoved++;
                    continue;
                }

                seenIds.Add(item.Id);
                deduplicatedItems.Add(item);
            }

            if (duplicatesRemoved > 0)
            {
                Plugin.Log.LogWarning($"[SNAPSHOT] Removed {duplicatesRemoved} duplicate item(s) from snapshot before saving");
                snapshot.Items = deduplicatedItems;
            }

            string filePath = GetSnapshotFilePath(snapshot.SessionId);

            // Configure JSON serialization settings
            // Indented formatting makes files readable for debugging
            // Include nulls to maintain consistent structure
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include
            };

            // Serialize snapshot to JSON string
            string jsonContent = JsonConvert.SerializeObject(snapshot, settings);

            // Write to file (overwrites existing)
            File.WriteAllText(filePath, jsonContent);

            // Log success if enabled in settings
            if (Configuration.Settings.LogSnapshotCreation.Value)
            {
                Plugin.Log.LogDebug($"Snapshot saved: {snapshot}");
                Plugin.Log.LogDebug($"Snapshot file: {filePath}");
                Plugin.Log.LogDebug($"Included slots: {string.Join(", ", snapshot.IncludedSlots)}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to save snapshot: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads a player's inventory snapshot from disk.
    /// </summary>
    /// <param name="sessionId">The player's session/profile ID</param>
    /// <returns>The loaded snapshot, or null if not found or invalid</returns>
    /// <remarks>
    /// Returns null in these cases:
    /// <list type="bullet">
    ///   <item>No snapshot file exists for this session</item>
    ///   <item>The file exists but cannot be deserialized</item>
    ///   <item>The deserialized snapshot is invalid (fails IsValid check)</item>
    /// </list>
    /// </remarks>
    public InventorySnapshot LoadSnapshot(string sessionId)
    {
        try
        {
            string filePath = GetSnapshotFilePath(sessionId);

            // Check if snapshot file exists
            if (!File.Exists(filePath))
            {
                Plugin.Log.LogDebug($"No snapshot found for session {sessionId}");
                return null;
            }

            // Read JSON content from file
            string jsonContent = File.ReadAllText(filePath);

            // Deserialize JSON to InventorySnapshot object
            var snapshot = JsonConvert.DeserializeObject<InventorySnapshot>(jsonContent);

            // Validate the loaded snapshot
            if (snapshot == null || !snapshot.IsValid())
            {
                Plugin.Log.LogError($"Invalid snapshot file for session {sessionId}");
                return null;
            }

            Plugin.Log.LogDebug($"Snapshot loaded: {snapshot}");
            return snapshot;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to load snapshot for session {sessionId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a snapshot file exists for a player session.
    /// Quick existence check without loading the file contents.
    /// </summary>
    /// <param name="sessionId">The player's session/profile ID</param>
    /// <returns>True if a snapshot file exists, false otherwise</returns>
    public bool SnapshotExists(string sessionId)
    {
        string filePath = GetSnapshotFilePath(sessionId);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Finds and loads the most recently modified snapshot file.
    /// Used when the exact session ID is not known.
    /// </summary>
    /// <returns>The most recent snapshot, or null if no snapshots exist</returns>
    /// <remarks>
    /// <para>
    /// This method is used by RaidEndPatch when a player dies. At that point,
    /// we need to find any valid snapshot for the player, regardless of the
    /// exact session ID used when it was created.
    /// </para>
    /// <para>
    /// Files are sorted by last write time (UTC) to find the most recent.
    /// This handles cases where multiple snapshots might exist from previous
    /// raids that weren't properly cleaned up.
    /// </para>
    /// </remarks>
    public InventorySnapshot GetMostRecentSnapshot()
    {
        try
        {
            // Ensure directory exists before listing files
            EnsureSnapshotDirectoryExists();

            // Get all JSON files in the snapshot directory
            var snapshotFiles = Directory.GetFiles(_snapshotDirectory, "*.json");

            if (snapshotFiles.Length == 0)
            {
                Plugin.Log.LogDebug("No snapshot files found");
                return null;
            }

            // Find the most recently modified file
            // Sort by last write time descending and take the first
            string mostRecentFile = snapshotFiles
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();

            if (mostRecentFile == null)
            {
                return null;
            }

            Plugin.Log.LogDebug($"Most recent snapshot file: {Path.GetFileName(mostRecentFile)}");

            // Load and return the snapshot from this file
            string jsonContent = File.ReadAllText(mostRecentFile);
            var snapshot = JsonConvert.DeserializeObject<InventorySnapshot>(jsonContent);

            return snapshot;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to get most recent snapshot: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Snapshot Cleanup
    // ========================================================================

    /// <summary>
    /// Deletes a player's snapshot file from disk.
    /// Called after successful extraction to prevent old snapshots from
    /// being used in future raids.
    /// </summary>
    /// <param name="sessionId">The player's session/profile ID</param>
    /// <returns>True if the file was deleted or didn't exist, false on error</returns>
    /// <remarks>
    /// <para>
    /// This method returns true even if no snapshot exists, since the end
    /// result (no snapshot file) is the same. Only actual errors during
    /// deletion cause a false return.
    /// </para>
    /// <para>
    /// Snapshots are cleared in these scenarios:
    /// </para>
    /// <list type="bullet">
    ///   <item>Player successfully extracts from raid</item>
    ///   <item>Server completes inventory restoration after death</item>
    ///   <item>Player manually requests snapshot reset (if implemented)</item>
    /// </list>
    /// </remarks>
    public bool ClearSnapshot(string sessionId)
    {
        try
        {
            string filePath = GetSnapshotFilePath(sessionId);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Plugin.Log.LogDebug($"Snapshot cleared for session {sessionId}");
                return true;
            }

            // No snapshot to clear - this is fine, not an error
            Plugin.Log.LogDebug($"No snapshot to clear for session {sessionId}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to clear snapshot for session {sessionId}: {ex.Message}");
            return false;
        }
    }
}
