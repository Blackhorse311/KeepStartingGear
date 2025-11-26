// ============================================================================
// Keep Starting Gear - Profile Service
// ============================================================================
// This service provides direct manipulation of SPT profile JSON files.
// It handles locating, reading, and modifying player profile data.
//
// NOTE: This is a LEGACY service. The server-side mod (RaidEndInterceptor)
// now handles profile modification during raid end processing, which is more
// reliable. This client-side service is kept for reference and potential
// fallback scenarios.
//
// KEY RESPONSIBILITIES:
// 1. Locate the SPT profiles directory automatically
// 2. Find the active player profile file
// 3. Restore inventory items to a profile from a snapshot
// 4. Create backups before modifying profiles
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
using Blackhorse311.KeepStartingGear.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Service responsible for directly manipulating SPT profile JSON files.
/// Handles reading, modifying, and saving player inventory data.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPORTANT:</b> This is legacy code. The preferred approach is to use
/// the server-side mod which intercepts EndLocalRaid and modifies the profile
/// during the normal raid end processing. Direct file manipulation from the
/// client can cause sync issues if the server hasn't saved yet.
/// </para>
/// <para>
/// This service remains useful for:
/// </para>
/// <list type="bullet">
///   <item>Understanding the profile structure</item>
///   <item>Testing and debugging</item>
///   <item>Fallback if server-side restoration fails</item>
/// </list>
/// </remarks>
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
        Plugin.Log.LogInfo($"ProfileService initialized with directory: {_profilesDirectory}");
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

            // Define possible profile directory locations
            string[] possiblePaths = new[]
            {
                // Standard SPT structure: H:\SPT\SPT\user\profiles
                Path.Combine(gameDirectory, "SPT", "user", "profiles"),

                // Alternative: H:\SPT\user\profiles (if SPT folder structure is flat)
                Path.Combine(gameDirectory, "user", "profiles"),

                // Go up one level and back down: parent\SPT\user\profiles
                Path.Combine(Path.GetDirectoryName(gameDirectory), "SPT", "user", "profiles"),

                // Go up one level: parent\user\profiles
                Path.Combine(Path.GetDirectoryName(gameDirectory), "user", "profiles")
            };

            // Try each path until we find one that exists
            foreach (var path in possiblePaths)
            {
                Plugin.Log.LogDebug($"Checking path: {path}");
                if (Directory.Exists(path))
                {
                    Plugin.Log.LogInfo($"Found profiles directory: {path}");
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

            Plugin.Log.LogInfo($"Most recent profile: {Path.GetFileName(mostRecentFile)}");
            return mostRecentFile;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error finding most recent profile: {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Inventory Restoration
    // ========================================================================

    /// <summary>
    /// Restores inventory items to a profile by directly editing the JSON file.
    /// </summary>
    /// <param name="snapshot">The inventory snapshot to restore from</param>
    /// <returns>True if restoration succeeded, false otherwise</returns>
    /// <remarks>
    /// <para>
    /// <b>WARNING:</b> This is a legacy method. Prefer server-side restoration
    /// via RaidEndInterceptor which modifies the profile during normal raid
    /// end processing.
    /// </para>
    /// <para>
    /// Restoration process:
    /// </para>
    /// <list type="number">
    ///   <item>Find and load the most recent profile file</item>
    ///   <item>Create a backup before modification</item>
    ///   <item>Find the Equipment container in the profile</item>
    ///   <item>Remove existing equipment items</item>
    ///   <item>Add items from the snapshot with remapped parent IDs</item>
    ///   <item>Save the modified profile</item>
    /// </list>
    /// </remarks>
    public bool RestoreInventoryToProfile(InventorySnapshot snapshot)
    {
        try
        {
            // Validate snapshot
            if (snapshot == null || !snapshot.IsValid())
            {
                Plugin.Log.LogError("Invalid snapshot provided");
                return false;
            }

            // Find the profile file to modify
            string profileFilePath = GetMostRecentProfileFile();
            if (string.IsNullOrEmpty(profileFilePath))
            {
                Plugin.Log.LogError("Could not find profile file to restore to");
                return false;
            }

            Plugin.Log.LogInfo($"Restoring inventory to profile: {Path.GetFileName(profileFilePath)}");

            // Create backup before modifying
            CreateBackup(profileFilePath);

            // Read and parse the profile JSON
            string profileJson = File.ReadAllText(profileFilePath);
            var profileObject = JObject.Parse(profileJson);

            // Navigate to the PMC inventory
            // Profile structure: characters -> pmc -> Inventory -> items
            var pmcInventory = profileObject["characters"]?["pmc"]?["Inventory"];
            if (pmcInventory == null)
            {
                Plugin.Log.LogError("Could not find PMC Inventory in profile");
                return false;
            }

            var itemsArray = pmcInventory["items"] as JArray;
            if (itemsArray == null)
            {
                Plugin.Log.LogError("Could not find items array in profile");
                return false;
            }

            // ================================================================
            // Find Equipment Container
            // The Equipment container is the parent of all equipped items
            // Template ID: 55d7217a4bdc2d86028b456d
            // ================================================================
            string equipmentId = null;
            JToken equipmentToken = null;
            foreach (var item in itemsArray)
            {
                var tpl = item["_tpl"]?.ToString();
                if (tpl == "55d7217a4bdc2d86028b456d") // Equipment container template
                {
                    equipmentId = item["_id"]?.ToString();
                    equipmentToken = item;
                    Plugin.Log.LogDebug($"Found Equipment container with ID: {equipmentId}");
                    break;
                }
            }

            if (string.IsNullOrEmpty(equipmentId))
            {
                Plugin.Log.LogError("Could not find Equipment container in profile");
                return false;
            }

            // ================================================================
            // Remove Existing Equipment Items
            // We need to clear equipment slots before adding snapshot items
            // ================================================================
            var equipmentSlotNames = new[]
            {
                "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
                "Scabbard", "Backpack", "SecuredContainer", "TacticalVest",
                "ArmorVest", "Pockets", "Headwear", "Earpiece", "Eyewear",
                "FaceCover", "ArmBand", "Dogtag"
            };

            // Find items directly in equipment slots
            var itemsToRemove = new List<JToken>();
            foreach (var item in itemsArray)
            {
                var parentId = item["parentId"]?.ToString();
                var slotId = item["slotId"]?.ToString();

                // Remove if parent is Equipment and slot is an equipment slot
                if (parentId == equipmentId && equipmentSlotNames.Contains(slotId))
                {
                    itemsToRemove.Add(item);
                }
            }

            // Also remove all nested children (items inside containers, weapon mods, etc.)
            // This requires multiple passes until no more children are found
            bool foundMore = true;
            while (foundMore)
            {
                foundMore = false;
                var removedIds = itemsToRemove.Select(t => t["_id"]?.ToString()).ToList();

                foreach (var item in itemsArray)
                {
                    if (!itemsToRemove.Contains(item))
                    {
                        var parentId = item["parentId"]?.ToString();
                        if (removedIds.Contains(parentId))
                        {
                            itemsToRemove.Add(item);
                            foundMore = true;
                        }
                    }
                }
            }

            Plugin.Log.LogInfo($"Removing {itemsToRemove.Count} existing inventory items");
            foreach (var item in itemsToRemove)
            {
                itemsArray.Remove(item);
            }

            // ================================================================
            // Add Snapshot Items
            // Convert each snapshot item to a JObject and add to the profile
            // Important: Remap parent IDs to use profile's Equipment ID
            // ================================================================
            int restoredCount = 0;

            // Find the Equipment container ID from the snapshot
            // We need this to remap parent references
            string snapshotEquipmentId = snapshot.Items
                .FirstOrDefault(i => i.Tpl == "55d7217a4bdc2d86028b456d")?.Id;

            if (string.IsNullOrEmpty(snapshotEquipmentId))
            {
                Plugin.Log.LogError("Could not find Equipment container in snapshot");
                return false;
            }

            Plugin.Log.LogDebug($"Snapshot Equipment ID: {snapshotEquipmentId} -> Profile Equipment ID: {equipmentId}");

            foreach (var snapshotItem in snapshot.Items)
            {
                // Skip the Equipment container itself (we keep the profile's original)
                if (snapshotItem.Tpl == "55d7217a4bdc2d86028b456d")
                {
                    continue;
                }

                // Create a JObject for this item
                // Keep the same item ID to maintain parent-child relationships
                var itemObject = new JObject
                {
                    ["_id"] = snapshotItem.Id,
                    ["_tpl"] = snapshotItem.Tpl
                };

                // Remap parentId: snapshot Equipment -> profile Equipment
                // Other parent IDs stay the same (maintains internal hierarchy)
                if (snapshotItem.ParentId == snapshotEquipmentId)
                {
                    // Direct child of Equipment - use profile's Equipment ID
                    itemObject["parentId"] = equipmentId;
                }
                else
                {
                    // Nested item (inside container, weapon mod, etc.)
                    // Keep the original parent ID
                    itemObject["parentId"] = snapshotItem.ParentId;
                }

                // Add slotId if present (equipment slot or grid ID)
                if (!string.IsNullOrEmpty(snapshotItem.SlotId))
                {
                    itemObject["slotId"] = snapshotItem.SlotId;
                }

                // Add location if present (grid position for container items)
                if (snapshotItem.Location != null)
                {
                    itemObject["location"] = JObject.FromObject(snapshotItem.Location);
                }

                // Add upd (update data) if present (stack count, durability, etc.)
                if (snapshotItem.Upd != null)
                {
                    itemObject["upd"] = JObject.Parse(JsonConvert.SerializeObject(snapshotItem.Upd));
                }

                // Add to the profile's items array
                itemsArray.Add(itemObject);
                restoredCount++;
            }

            Plugin.Log.LogInfo($"Added {restoredCount} items from snapshot to profile");

            // ================================================================
            // Save Modified Profile
            // ================================================================
            string modifiedJson = profileObject.ToString(Formatting.Indented);
            File.WriteAllText(profileFilePath, modifiedJson);

            Plugin.Log.LogInfo($"Successfully restored inventory to profile: {Path.GetFileName(profileFilePath)}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to restore inventory to profile: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    // ========================================================================
    // Backup Management
    // ========================================================================

    /// <summary>
    /// Creates a backup copy of a profile file before modification.
    /// Backup files are named {original}_backup.json.
    /// </summary>
    /// <param name="profileFilePath">Full path to the profile file</param>
    /// <remarks>
    /// Backups are overwritten each time, keeping only the most recent.
    /// These provide a safety net if restoration causes profile corruption.
    /// </remarks>
    private void CreateBackup(string profileFilePath)
    {
        try
        {
            string backupPath = profileFilePath.Replace(".json", "_backup.json");
            File.Copy(profileFilePath, backupPath, overwrite: true);
            Plugin.Log.LogDebug($"Created profile backup: {Path.GetFileName(backupPath)}");
        }
        catch (Exception ex)
        {
            // Non-critical error - warn but continue
            Plugin.Log.LogWarning($"Failed to create profile backup: {ex.Message}");
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
}
