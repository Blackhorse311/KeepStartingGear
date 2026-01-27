// ============================================================================
// Keep Starting Gear - Snapshot History Service
// ============================================================================
// FEATURE 4: Snapshot History/Undo
//
// Maintains a history of recent snapshots so players can undo accidental
// snapshot overwrites. This is useful when you accidentally take a snapshot
// with bad gear and want to revert to your previous snapshot.
//
// STORAGE:
// History is stored as numbered backup files in the snapshots directory:
// - {sessionId}.json (current)
// - {sessionId}.1.json (previous)
// - {sessionId}.2.json (older)
// etc.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Models;
using Newtonsoft.Json;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service for managing snapshot history and undo functionality.
/// </summary>
public class SnapshotHistory
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static SnapshotHistory Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>Default max history size.</summary>
    private const int DefaultMaxHistory = 3;

    // ========================================================================
    // State
    // ========================================================================

    private readonly string _snapshotDirectory;

    // ========================================================================
    // Constructor
    // ========================================================================

    public SnapshotHistory()
    {
        _snapshotDirectory = Plugin.GetDataPath();
        Instance = this;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Creates a backup of the current snapshot before overwriting.
    /// Call this before saving a new snapshot.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <remarks>
    /// Backup rotation works as follows (for maxHistory=3):
    /// - .2.json is deleted (oldest)
    /// - .1.json is moved to .2.json
    /// - current.json is copied to .1.json (newest backup)
    /// </remarks>
    public void BackupCurrentSnapshot(string sessionId)
    {
        try
        {
            int maxHistory = Settings.MaxSnapshotHistory?.Value ?? DefaultMaxHistory;
            if (maxHistory <= 0)
            {
                // History disabled
                return;
            }

            string currentPath = GetSnapshotPath(sessionId);
            if (!File.Exists(currentPath))
            {
                // No current snapshot to backup
                return;
            }

            // Step 1: Delete the oldest backup if it exists (makes room)
            string oldestPath = GetHistoryPath(sessionId, maxHistory - 1);
            if (File.Exists(oldestPath))
            {
                File.Delete(oldestPath);
            }

            // Step 2: Shift existing backups up by one index (oldest to newest)
            // e.g., .1 -> .2, .2 -> .3, etc.
            for (int i = maxHistory - 2; i >= 1; i--)
            {
                string sourcePath = GetHistoryPath(sessionId, i);
                string destPath = GetHistoryPath(sessionId, i + 1);

                if (File.Exists(sourcePath))
                {
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    File.Move(sourcePath, destPath);
                }
            }

            // Step 3: Copy current snapshot to .1.json (most recent backup)
            string backupPath = GetHistoryPath(sessionId, 1);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Copy(currentPath, backupPath);

            Plugin.Log.LogDebug($"[SnapshotHistory] Backed up snapshot to {Path.GetFileName(backupPath)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SnapshotHistory] Failed to backup snapshot: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a list of available snapshot history entries.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <returns>List of history entries, newest first</returns>
    public List<SnapshotHistoryEntry> GetHistory(string sessionId)
    {
        var history = new List<SnapshotHistoryEntry>();

        try
        {
            int maxHistory = Settings.MaxSnapshotHistory?.Value ?? DefaultMaxHistory;

            // Check current snapshot
            string currentPath = GetSnapshotPath(sessionId);
            if (File.Exists(currentPath))
            {
                var info = new FileInfo(currentPath);
                history.Add(new SnapshotHistoryEntry
                {
                    Index = 0,
                    FilePath = currentPath,
                    Timestamp = info.LastWriteTimeUtc,
                    IsCurrent = true,
                    Label = "Current"
                });
            }

            // Check history backups
            for (int i = 1; i < maxHistory; i++)
            {
                string historyPath = GetHistoryPath(sessionId, i);
                if (File.Exists(historyPath))
                {
                    var info = new FileInfo(historyPath);
                    history.Add(new SnapshotHistoryEntry
                    {
                        Index = i,
                        FilePath = historyPath,
                        Timestamp = info.LastWriteTimeUtc,
                        IsCurrent = false,
                        Label = $"Backup {i}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SnapshotHistory] Failed to get history: {ex.Message}");
        }

        return history;
    }

    /// <summary>
    /// Restores a previous snapshot from history.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <param name="historyIndex">The history index to restore (1 = most recent backup)</param>
    /// <returns>True if restore succeeded</returns>
    public bool RestoreFromHistory(string sessionId, int historyIndex)
    {
        try
        {
            if (historyIndex < 1)
            {
                Plugin.Log.LogWarning("[SnapshotHistory] Cannot restore index 0 (already current)");
                return false;
            }

            string historyPath = GetHistoryPath(sessionId, historyIndex);
            if (!File.Exists(historyPath))
            {
                Plugin.Log.LogWarning($"[SnapshotHistory] History file not found: {historyPath}");
                return false;
            }

            // First, backup the current snapshot before overwriting
            BackupCurrentSnapshot(sessionId);

            // Copy history file to current
            string currentPath = GetSnapshotPath(sessionId);
            File.Copy(historyPath, currentPath, overwrite: true);

            Plugin.Log.LogDebug($"[SnapshotHistory] Restored snapshot from backup {historyIndex}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SnapshotHistory] Failed to restore from history: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clears all history for a session.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    public void ClearHistory(string sessionId)
    {
        try
        {
            int maxHistory = Settings.MaxSnapshotHistory?.Value ?? DefaultMaxHistory;

            for (int i = 1; i < maxHistory; i++)
            {
                string historyPath = GetHistoryPath(sessionId, i);
                if (File.Exists(historyPath))
                {
                    File.Delete(historyPath);
                }
            }

            Plugin.Log.LogDebug($"[SnapshotHistory] Cleared history for session {sessionId}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SnapshotHistory] Failed to clear history: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a snapshot from a specific history index.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <param name="historyIndex">0 = current, 1+ = history</param>
    /// <returns>The loaded snapshot, or null if not found</returns>
    public InventorySnapshot LoadFromHistory(string sessionId, int historyIndex)
    {
        try
        {
            string path = historyIndex == 0
                ? GetSnapshotPath(sessionId)
                : GetHistoryPath(sessionId, historyIndex);

            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<InventorySnapshot>(json);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SnapshotHistory] Failed to load from history: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Path Helpers
    // ========================================================================

    private string GetSnapshotPath(string sessionId)
    {
        return Path.Combine(_snapshotDirectory, $"{sessionId}.json");
    }

    private string GetHistoryPath(string sessionId, int index)
    {
        return Path.Combine(_snapshotDirectory, $"{sessionId}.{index}.json");
    }
}

/// <summary>
/// Represents a single entry in the snapshot history.
/// </summary>
public class SnapshotHistoryEntry
{
    /// <summary>Index in history (0 = current, 1+ = backups)</summary>
    public int Index { get; set; }

    /// <summary>Full path to the snapshot file</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>When this snapshot was created</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Whether this is the current active snapshot</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Display label for UI</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Formatted timestamp for display</summary>
    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}
