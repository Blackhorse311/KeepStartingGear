// ============================================================================
// Keep Starting Gear - Profile Service
// ============================================================================
// This service locates SPT profile JSON files and resolves the active
// session ID (profile ID) used for snapshot file naming.
//
// NOTE: Legacy client-side restoration (RestoreInventoryToProfile) has been
// removed. The server-side mod (SnapshotRestorer) handles all restoration.
//
// KEY RESPONSIBILITIES:
// 1. Locate the SPT profiles directory automatically
// 2. Find the active player profile file
// 3. Resolve the active session ID (profile ID)
//
// PROFILE LOCATION:
// SPT profiles are stored in: {SPT_ROOT}\user\profiles\
// Each profile is a JSON file named by the profile ID.
//
// PROFILE STRUCTURE:
// The profile JSON contains the full player data including:
// - characters.pmc.Inventory.items: Array of all inventory items
// - Equipment container template ID: 55d7217a4bdc2d86028b456d
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service responsible for locating SPT profile JSON files and resolving the
/// active session ID. Inventory restoration is handled server-side by
/// SnapshotRestorer.
/// </summary>
public class ProfileService
{
    // ========================================================================
    // Instance Fields
    // ========================================================================

    /// <summary>
    /// Full path to the SPT profiles directory.
    /// Discovered during construction by searching known locations.
    /// </summary>
    private readonly string _profilesDirectory;

    // ========================================================================
    // Singleton Pattern
    // ========================================================================

    /// <summary>
    /// Singleton instance of the ProfileService.
    /// Set during construction and accessible from anywhere in the mod.
    /// </summary>
    public static ProfileService Instance { get; private set; }

    /// <summary>
    /// Constructor - initializes the profile service and locates the profiles directory.
    /// Called once during plugin initialization.
    /// </summary>
    public ProfileService()
    {
        // Find the SPT profiles directory by searching known locations
        _profilesDirectory = FindProfilesDirectory();
        Instance = this;
        Plugin.Log.LogDebug($"ProfileService initialized with directory: {_profilesDirectory}");
    }

    // ========================================================================
    // Directory Discovery
    // ========================================================================

    /// <summary>
    /// Locates the SPT profiles directory by searching known installation paths.
    /// </summary>
    /// <returns>Full path to the profiles directory, or null if not found</returns>
    /// <remarks>
    /// <para>
    /// SPT installation layouts can vary. This method tries multiple common paths:
    /// </para>
    /// <list type="number">
    ///   <item>{GameDir}\SPT\user\profiles - Standard SPT 4.0 structure</item>
    ///   <item>{GameDir}\user\profiles - Flat structure</item>
    ///   <item>{Parent}\SPT\user\profiles - Game in subdirectory</item>
    ///   <item>{Parent}\user\profiles - Alternative flat structure</item>
    /// </list>
    /// </remarks>
    private string FindProfilesDirectory()
    {
        try
        {
            // Get the game executable directory
            // The game (EscapeFromTarkov.exe) runs from the SPT installation root
            string gameDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            Plugin.Log.LogDebug($"Game directory (BaseDirectory): {gameDirectory}");

            // REL-002: Safely get parent directory (can be null for root paths)
            string parentDirectory = Path.GetDirectoryName(gameDirectory);

            // Define possible profile directory locations
            var possiblePaths = new List<string>
            {
                // Standard SPT structure: {SPT_ROOT}/SPT/user/profiles
                Path.Combine(gameDirectory, "SPT", "user", "profiles"),

                // Alternative: {SPT_ROOT}/user/profiles (if SPT folder structure is flat)
                Path.Combine(gameDirectory, "user", "profiles")
            };

            // Only add parent-based paths if parent directory exists
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                // Go up one level and back down: parent\SPT\user\profiles
                possiblePaths.Add(Path.Combine(parentDirectory, "SPT", "user", "profiles"));

                // Go up one level: parent\user\profiles
                possiblePaths.Add(Path.Combine(parentDirectory, "user", "profiles"));
            }

            // Try each path until we find one that exists
            foreach (var path in possiblePaths)
            {
                Plugin.Log.LogDebug($"Checking path: {path}");
                if (Directory.Exists(path))
                {
                    Plugin.Log.LogDebug($"Found profiles directory: {path}");
                    return path;
                }
            }

            // None of the expected paths exist
            Plugin.Log.LogError($"Could not find profiles directory in any of the expected locations");
            Plugin.Log.LogError($"Game directory was: {gameDirectory}");
            return null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error finding profiles directory: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Profile File Management
    // ========================================================================

    /// <summary>
    /// Gets all profile JSON files in the profiles directory.
    /// Excludes backup files created by this service.
    /// </summary>
    /// <returns>List of full paths to profile files, or empty list on error</returns>
    public List<string> GetAllProfileFiles()
    {
        try
        {
            // Validate directory exists
            if (string.IsNullOrEmpty(_profilesDirectory) || !Directory.Exists(_profilesDirectory))
            {
                Plugin.Log.LogError("Profiles directory not found");
                return new List<string>();
            }

            // Get all JSON files, excluding our backup files
            var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json")
                .Where(f => !f.EndsWith("_backup.json"))
                .ToList();

            Plugin.Log.LogDebug($"Found {profileFiles.Count} profile files");
            return profileFiles;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error getting profile files: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Finds the most recently modified profile file.
    /// Assumes the most recent is the currently active profile.
    /// </summary>
    /// <returns>Full path to the most recent profile file, or null if none found</returns>
    /// <remarks>
    /// SPT updates the profile file after each raid, so the most recently
    /// modified file is typically the active player's profile.
    /// </remarks>
    public string GetMostRecentProfileFile()
    {
        try
        {
            var profileFiles = GetAllProfileFiles();
            if (profileFiles.Count == 0)
            {
                Plugin.Log.LogError("No profile files found");
                return null;
            }

            // Sort by last write time and get the most recent
            var mostRecentFile = profileFiles
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();

            Plugin.Log.LogDebug($"Most recent profile: {Path.GetFileName(mostRecentFile)}");
            return mostRecentFile;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error finding most recent profile: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Profile Information
    // ========================================================================

    /// <summary>
    /// Extracts the profile ID from a profile JSON file.
    /// </summary>
    /// <param name="profileFilePath">Full path to the profile file</param>
    /// <returns>The profile ID string, or null on error</returns>
    /// <remarks>
    /// Profile ID is located at: info.id in the JSON structure.
    /// </remarks>
    public string GetProfileId(string profileFilePath)
    {
        try
        {
            string profileJson = File.ReadAllText(profileFilePath);
            var profileObject = JObject.Parse(profileJson);
            return profileObject["info"]?["id"]?.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to read profile ID: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Session ID
    // ========================================================================

    /// <summary>Cached session ID to avoid repeated profile file reads.</summary>
    /// <remarks>Volatile for thread safety: may be read from UI thread and patch threads.</remarks>
    private volatile string _cachedSessionId;

    /// <summary>Ticks timestamp of when session ID was last resolved (for atomic read/write).</summary>
    /// <remarks>Uses long (ticks) instead of DateTime because volatile requires primitive types.</remarks>
    private long _sessionIdCacheTimeTicks;

    /// <summary>How long to cache the session ID before re-checking.</summary>
    private static readonly TimeSpan SessionIdCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the current session ID (profile ID) for the active player.
    /// This is used to identify which snapshot file to use.
    /// </summary>
    /// <returns>The session ID string, or null if not available</returns>
    public string GetSessionId()
    {
        // W-20 FIX: Cache session ID to avoid parsing profile JSON on every call
        var cachedTicks = System.Threading.Interlocked.Read(ref _sessionIdCacheTimeTicks);
        if (_cachedSessionId != null && (DateTime.UtcNow.Ticks - cachedTicks) < SessionIdCacheTtl.Ticks)
            return _cachedSessionId;

        try
        {
            // Try to get from the most recent profile
            string profileFilePath = GetMostRecentProfileFile();
            if (!string.IsNullOrEmpty(profileFilePath))
            {
                string result = GetProfileId(profileFilePath);
                _cachedSessionId = result;
                System.Threading.Interlocked.Exchange(ref _sessionIdCacheTimeTicks, DateTime.UtcNow.Ticks);
                return result;
            }

            return null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to get session ID: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Clears the cached session ID, forcing a re-read on next access.
    /// Call this when the session changes (new raid start).
    /// </summary>
    public void InvalidateSessionCache()
    {
        _cachedSessionId = null;
    }
}
