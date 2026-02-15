// ============================================================================
// Keep Starting Gear - Loadout Profiles Service
// ============================================================================
// FEATURE 5: Per-Loadout Snapshot Profiles
//
// Allows players to save named snapshot profiles for different loadouts.
// Useful for players who use different gear setups for different maps or
// playstyles (e.g., "Labs Run", "Customs Budget", "Factory CQB").
//
// STORAGE:
// Profiles are stored in the snapshots directory with naming convention:
// profile_{profileName}_{sessionId}.json
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blackhorse311.KeepStartingGear.Models;
using Newtonsoft.Json;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service for managing named loadout profiles.
/// </summary>
public class LoadoutProfiles
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static LoadoutProfiles Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>Maximum number of saved profiles per player.</summary>
    private const int MaxProfiles = 10;

    /// <summary>Prefix for profile files.</summary>
    private const string ProfilePrefix = "profile_";

    // ========================================================================
    // State
    // ========================================================================

    private readonly string _snapshotDirectory;

    /// <summary>
    /// Session ID validation regex - prevents path traversal attacks.
    /// Only allows alphanumeric characters, hyphens, and underscores.
    /// </summary>
    private static readonly Regex SessionIdValidator =
        new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

    /// <summary>Maximum file size for profile files (10MB).</summary>
    private const long MaxProfileFileSize = 10 * 1024 * 1024;

    // ========================================================================
    // Constructor
    // ========================================================================

    public LoadoutProfiles()
    {
        _snapshotDirectory = Plugin.GetDataPath();
        Instance = this;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Saves the current snapshot as a named profile.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <param name="profileName">Name for the profile (alphanumeric, max 20 chars)</param>
    /// <returns>True if save succeeded</returns>
    public bool SaveProfile(string sessionId, string profileName)
    {
        try
        {
            if (!IsValidSessionId(sessionId))
                return false;

            // Validate profile name
            profileName = SanitizeProfileName(profileName);
            if (string.IsNullOrEmpty(profileName))
            {
                Plugin.Log.LogWarning("[LoadoutProfiles] Invalid profile name");
                return false;
            }

            // Check max profiles limit
            var existingProfiles = GetProfiles(sessionId);
            if (existingProfiles.Count >= MaxProfiles && !existingProfiles.Any(p => p.Name == profileName))
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Max profiles ({MaxProfiles}) reached");
                return false;
            }

            // Load current snapshot
            var currentSnapshot = SnapshotManager.Instance?.LoadSnapshot(sessionId);
            if (currentSnapshot == null || !currentSnapshot.IsValid())
            {
                Plugin.Log.LogWarning("[LoadoutProfiles] No valid current snapshot to save as profile");
                return false;
            }

            // Save as profile using atomic write pattern to prevent corruption
            string profilePath = GetProfilePath(sessionId, profileName);
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include
            };
            string json = JsonConvert.SerializeObject(currentSnapshot, settings);

            // Atomic write: write to temp file then rename
            string tempPath = profilePath + $".tmp.{Guid.NewGuid():N}";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(profilePath))
                    File.Delete(profilePath);
                File.Move(tempPath, profilePath);
            }
            finally
            {
                // Clean up temp file if it still exists (write failed before rename)
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch { /* Best effort cleanup */ }
            }

            Plugin.Log.LogDebug($"[LoadoutProfiles] Saved profile: {profileName}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LoadoutProfiles] Failed to save profile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads a named profile and sets it as the current snapshot.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <param name="profileName">Name of the profile to load</param>
    /// <returns>True if load succeeded</returns>
    public bool LoadProfile(string sessionId, string profileName)
    {
        try
        {
            if (!IsValidSessionId(sessionId))
                return false;

            profileName = SanitizeProfileName(profileName);
            string profilePath = GetProfilePath(sessionId, profileName);

            if (!File.Exists(profilePath))
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Profile not found: {profileName}");
                return false;
            }

            // File size check to prevent DoS via large files
            var fileInfo = new FileInfo(profilePath);
            if (fileInfo.Length > MaxProfileFileSize)
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Profile file too large ({fileInfo.Length} bytes)");
                return false;
            }

            // Read profile
            // HIGH-004 FIX: Explicitly disable TypeNameHandling to prevent type confusion attacks
            // Newtonsoft.Json could deserialize malicious types if TypeNameHandling.Auto is used
            string json = File.ReadAllText(profilePath);
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None
            };
            var snapshot = JsonConvert.DeserializeObject<InventorySnapshot>(json, settings);

            if (snapshot == null || !snapshot.IsValid())
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Invalid profile data: {profileName}");
                return false;
            }

            // Update session ID to current
            snapshot.SessionId = sessionId;
            snapshot.Timestamp = DateTime.UtcNow;

            // Save as current snapshot (this will also backup the existing one)
            if (SnapshotManager.Instance?.SaveSnapshot(snapshot) == true)
            {
                Plugin.Log.LogDebug($"[LoadoutProfiles] Loaded profile: {profileName}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LoadoutProfiles] Failed to load profile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets a list of all saved profiles for a player.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <returns>List of profile info</returns>
    public List<LoadoutProfileInfo> GetProfiles(string sessionId)
    {
        var profiles = new List<LoadoutProfileInfo>();

        try
        {
            if (!IsValidSessionId(sessionId))
                return profiles;

            string pattern = $"{ProfilePrefix}*_{sessionId}.json";
            var files = Directory.GetFiles(_snapshotDirectory, pattern);

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                string fileName = Path.GetFileNameWithoutExtension(file);

                // Extract profile name from filename
                // Format: profile_{name}_{sessionId}
                string withoutSession = fileName.Replace($"_{sessionId}", "");
                string profileName = withoutSession.Replace(ProfilePrefix, "");

                profiles.Add(new LoadoutProfileInfo
                {
                    Name = profileName,
                    FilePath = file,
                    LastModified = info.LastWriteTimeUtc,
                    FileSize = info.Length
                });
            }

            // Sort by name
            profiles = profiles.OrderBy(p => p.Name).ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[LoadoutProfiles] Failed to list profiles: {ex.Message}");
        }

        return profiles;
    }

    /// <summary>
    /// Deletes a named profile.
    /// </summary>
    /// <param name="sessionId">The player's session ID</param>
    /// <param name="profileName">Name of the profile to delete</param>
    /// <returns>True if delete succeeded</returns>
    public bool DeleteProfile(string sessionId, string profileName)
    {
        try
        {
            if (!IsValidSessionId(sessionId))
                return false;

            profileName = SanitizeProfileName(profileName);
            string profilePath = GetProfilePath(sessionId, profileName);

            if (!File.Exists(profilePath))
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Profile not found: {profileName}");
                return false;
            }

            File.Delete(profilePath);
            Plugin.Log.LogDebug($"[LoadoutProfiles] Deleted profile: {profileName}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LoadoutProfiles] Failed to delete profile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Renames a profile.
    /// </summary>
    public bool RenameProfile(string sessionId, string oldName, string newName)
    {
        try
        {
            if (!IsValidSessionId(sessionId))
                return false;

            oldName = SanitizeProfileName(oldName);
            newName = SanitizeProfileName(newName);

            if (string.IsNullOrEmpty(newName))
            {
                Plugin.Log.LogWarning("[LoadoutProfiles] Invalid new profile name");
                return false;
            }

            string oldPath = GetProfilePath(sessionId, oldName);
            string newPath = GetProfilePath(sessionId, newName);

            if (!File.Exists(oldPath))
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Profile not found: {oldName}");
                return false;
            }

            if (File.Exists(newPath))
            {
                Plugin.Log.LogWarning($"[LoadoutProfiles] Profile already exists: {newName}");
                return false;
            }

            File.Move(oldPath, newPath);
            Plugin.Log.LogDebug($"[LoadoutProfiles] Renamed profile: {oldName} -> {newName}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LoadoutProfiles] Failed to rename profile: {ex.Message}");
            return false;
        }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private string GetProfilePath(string sessionId, string profileName)
    {
        return Path.Combine(_snapshotDirectory, $"{ProfilePrefix}{profileName}_{sessionId}.json");
    }

    /// <summary>
    /// Validates a session ID to prevent path traversal attacks.
    /// </summary>
    private static bool IsValidSessionId(string sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && SessionIdValidator.IsMatch(sessionId);
    }

    /// <summary>
    /// Sanitizes a profile name to be filesystem-safe.
    /// Returns null if the name is invalid or empty after sanitization.
    /// NEW-004: Uses whitelist approach for security.
    /// </summary>
    private string SanitizeProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // NEW-004: Use whitelist instead of blacklist - only allow safe characters
        // Allow alphanumeric, spaces, and hyphens
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9\s\-]", "");

        // Trim and limit length
        name = name.Trim();
        if (name.Length > 20)
            name = name.Substring(0, 20);

        // Must have at least 1 character
        return name.Length > 0 ? name : null;
    }
}

/// <summary>
/// Information about a saved loadout profile.
/// </summary>
public class LoadoutProfileInfo
{
    /// <summary>Profile name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full file path</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>When the profile was last saved</summary>
    public DateTime LastModified { get; set; }

    /// <summary>File size in bytes</summary>
    public long FileSize { get; set; }

    /// <summary>Formatted modification time</summary>
    public string FormattedTime => LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
