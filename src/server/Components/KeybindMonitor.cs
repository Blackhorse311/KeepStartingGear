// ============================================================================
// Keep Starting Gear - Keybind Monitor Component
// ============================================================================
// This Unity MonoBehaviour component handles snapshot capture - both automatic
// at raid start and manual via keybind.
//
// KEY RESPONSIBILITIES:
// 1. Take automatic snapshot at raid start (if enabled)
// 2. Monitor keyboard input for manual snapshot keybind
// 3. Enforce snapshot limits based on mode settings
// 4. Trigger inventory capture through InventoryService
// 5. Display appropriate notifications via NotificationOverlay
//
// SNAPSHOT MODES:
// - AutoOnly: Automatic snapshot at raid start, no manual allowed (default)
// - AutoPlusManual: Automatic at start + one manual snapshot per raid
// - ManualOnly: Only manual snapshots via keybind (classic mode)
//
// KEYBIND SYSTEM:
// The default keybind is Ctrl+Alt+F8, but this is fully configurable through
// the BepInEx Configuration Manager. Only used when manual snapshots are enabled.
//
// This component is attached to a GameObject when a raid starts (via GameStartPatch)
// and is destroyed when the raid ends.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using BepInEx.Configuration;
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
    // These track raid-wide state for the snapshot system
    // ========================================================================

    /// <summary>
    /// Tracks which map has had a snapshot taken during the current raid.
    /// When a snapshot is taken on a map, this is set to that map's name.
    /// Null means no snapshot has been taken yet this raid.
    /// </summary>
    private static string _currentRaidSnapshotMap = null;

    /// <summary>
    /// Tracks whether the player is currently in a raid.
    /// </summary>
    private static bool _inRaid = false;

    /// <summary>
    /// Tracks whether an auto-snapshot was taken at raid start.
    /// Used for warning when manual snapshot would overwrite auto-snapshot.
    /// </summary>
    private static bool _autoSnapshotTaken = false;

    /// <summary>
    /// Tracks whether a manual snapshot has been taken this raid.
    /// In AutoPlusManual mode, only one manual snapshot is allowed per raid.
    /// </summary>
    private static bool _manualSnapshotTaken = false;

    /// <summary>
    /// Timestamp when the last manual snapshot was taken.
    /// Used for enforcing configurable cooldown between manual snapshots.
    /// </summary>
    private static float _lastManualSnapshotTime = 0f;

    /// <summary>
    /// Number of items in the current snapshot.
    /// Used for showing difference when overwriting auto-snapshot.
    /// </summary>
    private static int _currentSnapshotItemCount = 0;

    /// <summary>
    /// The snapshot mode that was active when the raid started.
    /// This prevents exploits where players change settings mid-raid.
    /// </summary>
    private static SnapshotMode _raidStartMode = SnapshotMode.AutoOnly;

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
    /// This method also handles auto-snapshot at raid start if enabled.
    /// </remarks>
    public void Init(Player player, GameWorld gameWorld)
    {
        _player = player;
        _gameWorld = gameWorld;

        // Initialize cooldown timer to allow immediate snapshot
        _lastSnapshotTime = -SnapshotCooldown;

        // Mark that we're in a raid and reset all snapshot tracking
        _inRaid = true;
        _currentRaidSnapshotMap = null;
        _autoSnapshotTaken = false;
        _manualSnapshotTaken = false;
        _lastManualSnapshotTime = 0f;
        _currentSnapshotItemCount = 0;

        Plugin.Log.LogDebug($"KeybindMonitor initialized for {player.Profile.Nickname}");

        // Check if this is a map transfer (we already have a snapshot from this raid)
        bool isMapTransfer = _autoSnapshotTaken;

        if (isMapTransfer)
        {
            Plugin.Log.LogDebug("Map transfer detected - checking snapshot settings...");

            if (Settings.SnapshotOnMapTransfer.Value)
            {
                Plugin.Log.LogDebug("Re-Snapshot on Map Transfer is enabled - taking new snapshot");
                // Reset tracking for new snapshot
                _manualSnapshotTaken = false;
                Invoke(nameof(TakeAutoSnapshot), 0.5f);
            }
            else
            {
                Plugin.Log.LogDebug($"Keeping original snapshot ({_currentSnapshotItemCount} items) - no re-snapshot on transfer");
                NotificationOverlay.ShowInfo($"Gear Still Protected\n{_currentSnapshotItemCount} items from original snapshot");
            }
            return; // Skip the rest of initialization for transfers
        }

        // First raid entry - capture the snapshot mode (prevents mid-raid setting changes)
        _raidStartMode = Settings.SnapshotModeOption.Value;
        Plugin.Log.LogDebug($"Snapshot mode locked for this raid: {_raidStartMode}");

        // Handle auto-snapshot at raid start
        if (_raidStartMode == SnapshotMode.AutoOnly || _raidStartMode == SnapshotMode.AutoPlusManual)
        {
            // Delay auto-snapshot slightly to ensure player is fully loaded
            Invoke(nameof(TakeAutoSnapshot), 0.5f);
        }
        else
        {
            // Manual only mode - show keybind reminder
            string keybind = GetKeybindString();
            Plugin.Log.LogDebug($"Manual snapshot mode - press {keybind} to take snapshot");
            NotificationOverlay.ShowInfo($"Manual Snapshot Mode\nPress {keybind} to save gear");
        }

        Plugin.Log.LogDebug($"Keybind configured: {GetKeybindString()}");
    }

    /// <summary>
    /// Takes an automatic snapshot at raid start.
    /// Called via Invoke() after a short delay to ensure player is ready.
    /// </summary>
    private void TakeAutoSnapshot()
    {
        try
        {
            if (_player == null || _gameWorld == null)
            {
                Plugin.Log.LogWarning("Cannot take auto-snapshot: player or gameworld is null");
                return;
            }

            string location = GetCurrentLocation();
            Plugin.Log.LogDebug($"Taking automatic snapshot at raid start on {location}...");

            // Capture inventory
            var snapshot = InventoryService.Instance.CaptureInventory(_player, location, true);

            if (snapshot != null && snapshot.IsValid())
            {
                bool saved = SnapshotManager.Instance.SaveSnapshot(snapshot);

                if (saved)
                {
                    _autoSnapshotTaken = true;
                    _currentRaidSnapshotMap = location;
                    _currentSnapshotItemCount = snapshot.Items.Count;

                    // Play camera shutter sound
                    SnapshotSoundPlayer.PlaySnapshotSound();

                    Plugin.Log.LogDebug($"Auto-snapshot saved: {snapshot.Items.Count} items");

                    // Show notification based on mode
                    if (_raidStartMode == SnapshotMode.AutoPlusManual)
                    {
                        string keybind = GetKeybindString();
                        NotificationOverlay.ShowSuccess($"Gear Protected!\n{snapshot.Items.Count} items saved\nPress {keybind} to update");
                    }
                    else
                    {
                        NotificationOverlay.ShowSuccess($"Gear Protected!\n{snapshot.Items.Count} items saved automatically");
                    }
                }
                else
                {
                    Plugin.Log.LogError("Failed to save auto-snapshot");
                    NotificationOverlay.ShowError("Auto-Snapshot Failed!\nCheck logs for details");
                }
            }
            else
            {
                Plugin.Log.LogError("Failed to capture auto-snapshot");
                NotificationOverlay.ShowError("Auto-Snapshot Failed!\nCould not capture inventory");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error in auto-snapshot: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Gets a human-readable string for the current keybind configuration.
    /// Formats as "Ctrl + Alt + F8" (modifiers first, then main key).
    /// </summary>
    private string GetKeybindString()
    {
        var shortcut = Settings.SnapshotKeybind.Value;
        var parts = new System.Collections.Generic.List<string>();

        // Add modifiers first (in standard order: Ctrl, Alt, Shift)
        foreach (var modifier in shortcut.Modifiers)
        {
            string modName = modifier.ToString();
            // Clean up modifier names (LeftControl -> Ctrl, etc.)
            if (modName.Contains("Control")) parts.Add("Ctrl");
            else if (modName.Contains("Alt")) parts.Add("Alt");
            else if (modName.Contains("Shift")) parts.Add("Shift");
            else parts.Add(modName);
        }

        // Add main key last
        parts.Add(shortcut.MainKey.ToString());

        return string.Join(" + ", parts);
    }

    // ========================================================================
    // Static Methods (Raid State Management)
    // ========================================================================

    /// <summary>
    /// Resets the snapshot tracking state when a raid ends.
    /// Called by RaidEndPatch to clear the snapshot state for the next raid.
    /// </summary>
    public static void ResetRaidState()
    {
        _inRaid = false;
        _currentRaidSnapshotMap = null;
        _autoSnapshotTaken = false;
        _manualSnapshotTaken = false;
        _lastManualSnapshotTime = 0f;
        _currentSnapshotItemCount = 0;
        Plugin.Log.LogDebug("Raid state reset - all snapshot tracking cleared");
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
    /// Checks if the snapshot keybind was just pressed this frame.
    /// Uses BepInEx's KeyboardShortcut which handles modifiers automatically.
    /// </summary>
    /// <returns>True if the keybind was just pressed</returns>
    private bool IsSnapshotKeybindPressed()
    {
        // KeyboardShortcut.IsDown() returns true on the frame the key combination is pressed
        // It automatically handles checking all modifier keys (Ctrl, Alt, Shift)
        return Settings.SnapshotKeybind.Value.IsDown();
    }

    // ========================================================================
    // Snapshot Handling
    // ========================================================================

    /// <summary>
    /// Called when the snapshot keybind is pressed. Handles the complete snapshot workflow
    /// based on the current snapshot mode settings.
    /// </summary>
    private void OnSnapshotKeybindPressed()
    {
        try
        {
            // Update cooldown timer to prevent rapid re-triggers
            _lastSnapshotTime = Time.time;

            // Use the mode that was locked at raid start (prevents mid-raid exploits)
            var mode = _raidStartMode;

            // ================================================================
            // Mode Check: AutoOnly doesn't allow manual snapshots
            // ================================================================
            if (mode == SnapshotMode.AutoOnly)
            {
                Plugin.Log.LogDebug("Manual snapshot blocked - mode is Auto Only");
                NotificationOverlay.ShowWarning("Manual Snapshots Disabled\nGear is auto-protected at raid start");
                return;
            }

            Plugin.Log.LogDebug("Manual snapshot keybind pressed - capturing inventory...");

            // Determine if we're currently in a raid
            bool inRaid = _gameWorld != null && _gameWorld.MainPlayer == _player;
            string location = inRaid ? GetCurrentLocation() : "Hideout";

            // ================================================================
            // AutoPlusManual Mode: Check if manual snapshot already taken
            // ================================================================
            if (mode == SnapshotMode.AutoPlusManual && inRaid && _manualSnapshotTaken)
            {
                Plugin.Log.LogDebug("Manual snapshot limit reached - one per raid in Auto+Manual mode");
                NotificationOverlay.ShowWarning("Manual Snapshot Limit!\nOne update allowed per raid");
                return;
            }

            // ================================================================
            // Cooldown Check (configurable)
            // ================================================================
            int cooldownSeconds = Settings.ManualSnapshotCooldown.Value;
            if (cooldownSeconds > 0 && inRaid)
            {
                float timeSinceLastManual = Time.time - _lastManualSnapshotTime;
                if (timeSinceLastManual < cooldownSeconds)
                {
                    int remaining = (int)(cooldownSeconds - timeSinceLastManual);
                    Plugin.Log.LogDebug($"Manual snapshot on cooldown - {remaining}s remaining");
                    NotificationOverlay.ShowWarning($"Snapshot Cooldown\n{remaining} seconds remaining");
                    return;
                }
            }

            // ================================================================
            // ManualOnly Mode: Check one-per-map limit (legacy behavior)
            // ================================================================
            if (mode == SnapshotMode.ManualOnly && inRaid && HasSnapshotForCurrentRaid(location))
            {
                Plugin.Log.LogDebug($"Snapshot already taken for {location} this raid");
                NotificationOverlay.ShowWarning("Snapshot Limit Reached!\nOne per map per raid");
                return;
            }

            // ================================================================
            // Warning if overwriting auto-snapshot
            // ================================================================
            bool isOverwriting = _autoSnapshotTaken && Settings.WarnOnSnapshotOverwrite.Value;

            // ================================================================
            // Capture Inventory
            // ================================================================
            var snapshot = InventoryService.Instance.CaptureInventory(_player, location, inRaid);

            if (snapshot != null && snapshot.IsValid())
            {
                bool saved = SnapshotManager.Instance.SaveSnapshot(snapshot);

                if (saved)
                {
                    // Play camera shutter sound
                    SnapshotSoundPlayer.PlaySnapshotSound();

                    Plugin.Log.LogDebug($"Manual snapshot saved: {snapshot.Items.Count} items");

                    // Update tracking
                    if (inRaid)
                    {
                        _currentRaidSnapshotMap = location;
                        _manualSnapshotTaken = true;
                        _lastManualSnapshotTime = Time.time;

                        // Calculate difference from auto-snapshot
                        int diff = snapshot.Items.Count - _currentSnapshotItemCount;
                        _currentSnapshotItemCount = snapshot.Items.Count;

                        // Show appropriate notification
                        if (isOverwriting && diff != 0)
                        {
                            string diffStr = diff > 0 ? $"+{diff}" : diff.ToString();
                            NotificationOverlay.ShowSuccess($"Snapshot Updated!\n{snapshot.Items.Count} items ({diffStr} from start)");
                        }
                        else
                        {
                            NotificationOverlay.ShowSuccess($"Snapshot Saved!\n{snapshot.Items.Count} items captured");
                        }
                    }
                    else
                    {
                        NotificationOverlay.ShowSuccess($"Snapshot Saved!\n{snapshot.Items.Count} items captured");
                    }
                }
                else
                {
                    Plugin.Log.LogError("Failed to save manual snapshot");
                    NotificationOverlay.ShowError("Failed to Save Snapshot!");
                }
            }
            else
            {
                Plugin.Log.LogError("Failed to capture manual snapshot");
                NotificationOverlay.ShowError("Failed to Capture Inventory!");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Error handling manual snapshot: {ex.Message}");
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
