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
using System.Text.RegularExpressions;
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

    /// <summary>
    /// HIGH-003 FIX: Compiled regex for session ID validation.
    /// Only allows alphanumeric characters, hyphens, and underscores.
    /// This prevents path traversal attacks via malicious session IDs like "../../../etc/passwd".
    /// </summary>
    private static readonly Regex SessionIdValidator =
        new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

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
    /// H-10 FIX: Now throws if directory creation fails - snapshots will fail anyway.
    /// </summary>
    /// <remarks>
    /// The snapshot directory structure is:
    /// BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/
    /// </remarks>
    /// <exception cref="IOException">Thrown when directory cannot be created.</exception>
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
            // H-10 FIX: Log AND throw - continuing with a non-existent directory will cause silent failures
            Plugin.Log.LogError($"CRITICAL: Failed to create snapshot directory '{_snapshotDirectory}': {ex.Message}");
            Plugin.Log.LogError($"CRITICAL: Snapshot saving and loading will NOT work!");
            throw new IOException($"Failed to create snapshot directory: {_snapshotDirectory}", ex);
        }
    }

    /// <summary>
    /// HIGH-003 FIX: Validates that a session ID contains only safe characters.
    /// Prevents path traversal attacks via malicious session IDs.
    /// </summary>
    private static bool IsValidSessionId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return false;
        return SessionIdValidator.IsMatch(sessionId);
    }

    /// <summary>
    /// Constructs the full file path for a session's snapshot file.
    /// HIGH-003 FIX: Now validates session ID to prevent path traversal.
    /// HIGH-004 FIX: Return type is now nullable string to indicate invalid session IDs.
    /// </summary>
    /// <param name="sessionId">The player's session/profile ID</param>
    /// <returns>Full path to the snapshot JSON file, or null if session ID is invalid</returns>
    /// <remarks>
    /// File naming convention: {sessionId}.json
    /// Example: "5c0647fdd443c22b77659123.json"
    /// </remarks>
    private string? GetSnapshotFilePath(string sessionId)
    {
        // HIGH-003 FIX: Validate session ID to prevent path traversal attacks
        if (!IsValidSessionId(sessionId))
        {
            // HIGH-003 FIX: Safe string truncation - handle null sessionId properly
            string truncatedId = sessionId != null
                ? sessionId.Substring(0, Math.Min(20, sessionId.Length))
                : "(null)";
            Plugin.Log.LogWarning($"Invalid session ID format rejected: {truncatedId}...");
            return null;
        }
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
    /// LINUS-002 FIX: File writes are now truly atomic using temp-file-then-rename pattern.
    /// File.Move on the same volume is atomic on Windows NTFS, preventing corrupt files
    /// if the process crashes mid-write.
    /// </para>
    /// </remarks>
    public bool SaveSnapshot(InventorySnapshot snapshot)
    {
        string? tempFilePath = null;
        try
        {
            // M-07 FIX: Validate SessionId before attempting to construct file path
            if (snapshot == null || string.IsNullOrEmpty(snapshot.SessionId))
            {
                Plugin.Log.LogError("Cannot save snapshot: snapshot or SessionId is null/empty");
                return false;
            }

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

            string? filePath = GetSnapshotFilePath(snapshot.SessionId);
            if (filePath == null)
            {
                Plugin.Log.LogError("Cannot save snapshot: invalid session ID");
                return false;
            }

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

            // LINUS-002 FIX: Atomic write using temp-file-then-rename pattern
            // 1. Write to temp file (can be interrupted without corrupting final file)
            // 2. File.Move is atomic on NTFS when on same volume
            // 3. If we crash between 1 and 2, only the temp file is left (harmless)
            tempFilePath = filePath + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(tempFilePath, jsonContent);

            // Delete existing file if present (File.Move doesn't overwrite by default on .NET Framework)
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Atomic rename - this is the commit point
            File.Move(tempFilePath, filePath);
            tempFilePath = null; // Clear so we don't try to delete it in finally

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
        finally
        {
            // LINUS-002 FIX: Clean up temp file if write failed partway through
            if (tempFilePath != null)
            {
                try { File.Delete(tempFilePath); }
                catch { /* Best effort cleanup */ }
            }
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
    ///   <item>Session ID is invalid (contains illegal characters)</item>
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

            // HIGH-003 FIX: GetSnapshotFilePath now returns null for invalid session IDs
            if (filePath == null)
            {
                return null;
            }

            // Check if snapshot file exists
            if (!File.Exists(filePath))
            {
                Plugin.Log.LogDebug($"No snapshot found for session {sessionId}");
                return null;
            }

            // HIGH-001 FIX: Check file size before reading to prevent DoS via large files
            // Server-side SnapshotRestorer has this check, but client was missing it
            const long MaxSnapshotFileSize = 10 * 1024 * 1024; // 10MB
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxSnapshotFileSize)
            {
                Plugin.Log.LogWarning($"Snapshot file too large ({fileInfo.Length} bytes), max allowed is {MaxSnapshotFileSize} bytes");
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
    /// <returns>True if a snapshot file exists, false otherwise (including invalid session IDs)</returns>
    public bool SnapshotExists(string sessionId)
    {
        string filePath = GetSnapshotFilePath(sessionId);
        // HIGH-003 FIX: Return false for invalid session IDs
        return filePath != null && File.Exists(filePath);
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

            // REL-001: Validate deserialized snapshot is not null
            if (snapshot == null)
            {
                Plugin.Log.LogWarning($"Failed to deserialize snapshot from file: {mostRecentFile}");
                return null;
            }

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
    /// <returns>True if the file was deleted or didn't exist, false on error or invalid session ID</returns>
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

            // HIGH-003 FIX: Return false for invalid session IDs
            if (filePath == null)
            {
                return false;
            }

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
