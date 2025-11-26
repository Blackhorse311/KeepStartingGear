// ============================================================================
// Keep Starting Gear - Keybind Monitor Component
// ============================================================================
// This Unity MonoBehaviour component monitors for the snapshot keybind and
// triggers inventory snapshot creation when pressed.
//
// KEY RESPONSIBILITIES:
// 1. Monitor keyboard input each frame for the configured keybind
// 2. Enforce the one-snapshot-per-map limit during raids
// 3. Trigger inventory capture through InventoryService
// 4. Display appropriate notifications via NotificationOverlay
//
// KEYBIND SYSTEM:
// The default keybind is Ctrl+Alt+F8, but this is fully configurable through
// the BepInEx Configuration Manager. Users can change the primary key and
// enable/disable modifier requirements (Ctrl, Alt, Shift).
//
// SNAPSHOT LIMIT SYSTEM:
// - In hideout: Unlimited snapshots allowed
// - In raid: One snapshot per map per raid
// - Limit resets when: Changing maps, starting new raid, or extracting
//
// This component is attached to a GameObject when a raid starts (via GameStartPatch)
// and is destroyed when the raid ends.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Services;
using EFT;
using EFT.Communications;
using UnityEngine;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// MonoBehaviour component that monitors for the snapshot keybind and triggers snapshot creation.
/// Attached to a GameObject during raid start and handles all keybind-related logic.
/// </summary>
/// <remarks>
/// <para>
/// This component uses Unity's Update() method to check for keyboard input each frame.
/// When the configured keybind is detected, it captures the player's inventory and
/// saves it as a snapshot.
/// </para>
/// <para>
/// The snapshot limit system ensures fair gameplay by restricting players to one
/// snapshot per map during a raid. This prevents abuse while still providing
/// a safety net for players.
/// </para>
/// </remarks>
public class KeybindMonitor : MonoBehaviour
{
    // ========================================================================
    // Instance Fields
    // These are specific to each KeybindMonitor instance
    // ========================================================================

    /// <summary>
    /// Reference to the player whose inventory will be captured.
    /// Set during Init() when the raid starts.
    /// </summary>
    private Player _player;

    /// <summary>
    /// Reference to the current GameWorld instance.
    /// Used to determine if we're in a raid and get location information.
    /// </summary>
    private GameWorld _gameWorld;

    /// <summary>
    /// Timestamp of the last snapshot attempt.
    /// Used to enforce cooldown between snapshots to prevent accidental double-presses.
    /// </summary>
    private float _lastSnapshotTime;

    /// <summary>
    /// Minimum time in seconds between snapshot attempts.
    /// Prevents accidental double-captures from key bounce or rapid pressing.
    /// </summary>
    private const float SnapshotCooldown = 1.0f;

    // ========================================================================
    // Static Fields (Shared Across All Instances)
    // These track raid-wide state for the snapshot limit system
    // ========================================================================

    /// <summary>
    /// Tracks which map has had a snapshot taken during the current raid.
    /// When a snapshot is taken on a map, this is set to that map's name.
    /// Null means no snapshot has been taken yet this raid.
    /// </summary>
    /// <remarks>
    /// This is static because the snapshot limit persists across map transfers
    /// within the same raid, but resets when the raid ends.
    /// </remarks>
    private static string _currentRaidSnapshotMap = null;

    /// <summary>
    /// Tracks whether the player is currently in a raid.
    /// Used in conjunction with _currentRaidSnapshotMap to enforce limits.
    /// </summary>
    private static bool _inRaid = false;

    // ========================================================================
    // Initialization
    // ========================================================================

    /// <summary>
    /// Initializes the keybind monitor with player and game world references.
    /// Called by GameStartPatch when a raid begins.
    /// </summary>
    /// <param name="player">The player instance to monitor</param>
    /// <param name="gameWorld">The current GameWorld instance</param>
    /// <remarks>
    /// This method also resets the snapshot tracking state, allowing a fresh
    /// snapshot to be taken at the start of each raid.
    /// </remarks>
    public void Init(Player player, GameWorld gameWorld)
    {
        _player = player;
        _gameWorld = gameWorld;

        // Initialize cooldown timer to allow immediate snapshot
        _lastSnapshotTime = -SnapshotCooldown;

        // Mark that we're in a raid and reset the snapshot tracking
        // This allows a new snapshot to be taken at the start of each raid
        _inRaid = true;
        _currentRaidSnapshotMap = null;

        Plugin.Log.LogInfo($"KeybindMonitor initialized for {player.Profile.Nickname}");
    }

    // ========================================================================
    // Static Methods (Raid State Management)
    // ========================================================================

    /// <summary>
    /// Resets the snapshot tracking state when a raid ends.
    /// Called by RaidEndPatch to clear the snapshot limit for the next raid.
    /// </summary>
    /// <remarks>
    /// This must be called when the raid ends (death, extraction, or disconnect)
    /// to ensure the snapshot limit doesn't persist incorrectly into the next raid.
    /// </remarks>
    public static void ResetRaidState()
    {
        _inRaid = false;
        _currentRaidSnapshotMap = null;
        Plugin.Log.LogDebug("Raid state reset - snapshot limit cleared");
    }

    /// <summary>
    /// Checks if a snapshot has already been taken for the specified map during this raid.
    /// </summary>
    /// <param name="mapName">The name of the map to check</param>
    /// <returns>True if a snapshot was already taken on this map during the current raid</returns>
    /// <remarks>
    /// This method is used to enforce the one-snapshot-per-map limit.
    /// It returns false if not in a raid or if no snapshot has been taken yet.
    /// </remarks>
    public static bool HasSnapshotForCurrentRaid(string mapName)
    {
        // Must be in raid AND have a snapshot for this specific map
        return _inRaid && _currentRaidSnapshotMap != null && _currentRaidSnapshotMap == mapName;
    }

    // ========================================================================
    // Unity Lifecycle Methods
    // ========================================================================

    /// <summary>
    /// Called every frame by Unity. Checks for keybind input and triggers snapshot if pressed.
    /// </summary>
    /// <remarks>
    /// This method is called approximately 60+ times per second (depending on frame rate).
    /// It's designed to be lightweight with early-exit conditions to minimize performance impact.
    /// </remarks>
    private void Update()
    {
        try
        {
            // Early exit if player/gameworld references are invalid
            // This can happen during scene transitions or after death
            if (_player == null || _gameWorld == null)
                return;

            // Enforce cooldown to prevent accidental double-presses
            if (Time.time - _lastSnapshotTime < SnapshotCooldown)
                return;

            // Check if the configured keybind combination is pressed
            if (IsSnapshotKeybindPressed())
            {
                OnSnapshotKeybindPressed();
            }
        }
        catch (Exception ex)
        {
            // Log errors but don't crash - keybind monitoring should be resilient
            Plugin.Log.LogError($"Error in KeybindMonitor.Update: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the GameObject is destroyed (typically when raid ends).
    /// Cleans up any resources and logs the destruction.
    /// </summary>
    private void OnDestroy()
    {
        Plugin.Log.LogDebug("KeybindMonitor destroyed");
    }

    // ========================================================================
    // Keybind Detection
    // ========================================================================

    /// <summary>
    /// Checks if the complete snapshot keybind combination is currently pressed.
    /// This includes the primary key and any required modifier keys.
    /// </summary>
    /// <returns>True if all required keys are pressed simultaneously</returns>
    /// <remarks>
    /// The keybind is configurable through Settings:
    /// - Primary key: Settings.SnapshotKey (default: F8)
    /// - Ctrl required: Settings.RequireCtrl (default: true)
    /// - Alt required: Settings.RequireAlt (default: true)
    /// - Shift required: Settings.RequireShift (default: false)
    ///
    /// Default combination is Ctrl+Alt+F8
    /// </remarks>
    private bool IsSnapshotKeybindPressed()
    {
        // Check if the primary key was just pressed this frame
        // GetKeyDown returns true only on the frame the key is pressed down
        if (!Input.GetKeyDown(Settings.SnapshotKey.Value))
            return false;

        // Check Ctrl modifier if required
        // Supports both left and right Ctrl keys
        if (Settings.RequireCtrl.Value && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            return false;

        // Check Alt modifier if required
        // Supports both left and right Alt keys
        if (Settings.RequireAlt.Value && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            return false;

        // Check Shift modifier if required
        // Supports both left and right Shift keys
        if (Settings.RequireShift.Value && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            return false;

        // All required keys are pressed
        return true;
    }

    // ========================================================================
    // Snapshot Handling
    // ========================================================================

    /// <summary>
    /// Called when the snapshot keybind is pressed. Handles the complete snapshot workflow:
    /// checking limits, capturing inventory, saving snapshot, and showing notifications.
    /// </summary>
    /// <remarks>
    /// Workflow:
    /// 1. Update cooldown timer
    /// 2. Determine if in raid and get current location
    /// 3. Check snapshot limit (one per map in raid)
    /// 4. Capture inventory via InventoryService
    /// 5. Save snapshot via SnapshotManager
    /// 6. Show appropriate notification (success/warning/error)
    /// </remarks>
    private void OnSnapshotKeybindPressed()
    {
        try
        {
            // Update cooldown timer to prevent rapid re-triggers
            _lastSnapshotTime = Time.time;

            Plugin.Log.LogInfo("Snapshot keybind pressed - capturing inventory...");

            // Determine if we're currently in a raid
            // In raid: MainPlayer should be set and match our player reference
            bool inRaid = _gameWorld != null && _gameWorld.MainPlayer == _player;

            // Get the current location/map name
            // Used for logging and snapshot limit enforcement
            string location = inRaid ? GetCurrentLocation() : "Hideout";

            // ================================================================
            // Snapshot Limit Check
            // In-raid: Only one snapshot per map per raid
            // Hideout: Unlimited snapshots allowed
            // ================================================================
            if (inRaid && HasSnapshotForCurrentRaid(location))
            {
                Plugin.Log.LogInfo($"Snapshot already taken for {location} this raid - limit reached");

                // Show large centered yellow warning notification
                NotificationOverlay.ShowWarning($"Snapshot Limit Reached!\nOne per map per raid");
                return;
            }

            // ================================================================
            // Capture Inventory
            // Uses InventoryService to convert player's equipped items to
            // a serializable snapshot format
            // ================================================================
            var snapshot = InventoryService.Instance.CaptureInventory(_player, location, inRaid);

            if (snapshot != null && snapshot.IsValid())
            {
                // ============================================================
                // Save Snapshot
                // SnapshotManager handles JSON serialization and file I/O
                // ============================================================
                bool saved = SnapshotManager.Instance.SaveSnapshot(snapshot);

                if (saved)
                {
                    Plugin.Log.LogInfo($"Inventory snapshot saved successfully!");

                    // Mark that we've taken a snapshot for this map
                    // Only applies when in-raid (hideout has no limit)
                    if (inRaid)
                    {
                        _currentRaidSnapshotMap = location;
                        Plugin.Log.LogInfo($"Snapshot limit set for map: {location}");
                    }

                    // Show large centered green success notification
                    NotificationOverlay.ShowSuccess($"Snapshot Saved!\n{snapshot.Items.Count} items captured");
                }
                else
                {
                    Plugin.Log.LogError("Failed to save inventory snapshot");

                    // Show large centered red error notification
                    NotificationOverlay.ShowError("Failed to Save Snapshot!");
                }
            }
            else
            {
                Plugin.Log.LogError("Failed to capture inventory snapshot");

                // Show large centered red error notification
                NotificationOverlay.ShowError("Failed to Capture Inventory!");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error handling snapshot keybind: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    // ========================================================================
    // Location Detection
    // ========================================================================

    /// <summary>
    /// Gets the current map/location name from the GameWorld.
    /// Used for logging and enforcing the per-map snapshot limit.
    /// </summary>
    /// <returns>
    /// The location ID (e.g., "factory4_day", "bigmap", "Sandbox") or "Unknown" if not available
    /// </returns>
    /// <remarks>
    /// This method uses reflection to access GameWorld properties because the exact
    /// property names may vary between EFT versions. It tries multiple approaches:
    /// 1. GameWorld.LocationId property
    /// 2. GameWorld.Location.Name property
    /// 3. Falls back to "Unknown" if neither works
    /// </remarks>
    private string GetCurrentLocation()
    {
        try
        {
            // Try to get the location from GameWorld
            if (_gameWorld != null)
            {
                // Primary method: GameWorld.LocationId property
                // This contains the map ID like "factory4_day", "bigmap", "Sandbox", etc.
                var locationIdProp = _gameWorld.GetType().GetProperty("LocationId");
                if (locationIdProp != null)
                {
                    var locationId = locationIdProp.GetValue(_gameWorld)?.ToString();
                    if (!string.IsNullOrEmpty(locationId))
                    {
                        return locationId;
                    }
                }

                // Fallback method: GameWorld.Location.Name property
                // Some versions of EFT use this structure instead
                if (_gameWorld.MainPlayer != null)
                {
                    var locProp = _gameWorld.GetType().GetProperty("Location");
                    if (locProp != null)
                    {
                        var loc = locProp.GetValue(_gameWorld);
                        if (loc != null)
                        {
                            var nameProp = loc.GetType().GetProperty("Name");
                            if (nameProp != null)
                            {
                                var name = nameProp.GetValue(loc)?.ToString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    return name;
                                }
                            }
                        }
                    }
                }
            }

            // Could not determine location
            return "Unknown";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not get location name: {ex.Message}");
            return "Unknown";
        }
    }
}
