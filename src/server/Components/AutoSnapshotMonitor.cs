// ============================================================================
// Keep Starting Gear - Auto-Snapshot Monitor
// ============================================================================
// FEATURE 8: Auto-Snapshot on Loot Threshold
//
// Monitors the player's loot value during raids and automatically takes
// a new snapshot when the value exceeds a configurable threshold.
//
// This helps players who forget to manually update their snapshot after
// picking up valuable loot.
//
// BEHAVIOR:
// - Checks inventory value periodically
// - When value gained since last snapshot exceeds threshold, auto-snapshots
// - Shows notification when auto-snapshot triggers
// - Respects the MaxManualSnapshots limit
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using UnityEngine;
using Comfort.Common;
using EFT;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Services;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Monitors loot value and auto-triggers snapshots at configurable thresholds.
/// </summary>
public class AutoSnapshotMonitor : MonoBehaviour
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static AutoSnapshotMonitor Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>How often to check value (seconds).</summary>
    private const float CheckInterval = 30.0f;

    /// <summary>Minimum time between auto-snapshots (seconds).</summary>
    private const float AutoSnapshotCooldown = 60.0f;

    // ========================================================================
    // State
    // ========================================================================

    private float _lastCheckTime;
    private float _lastAutoSnapshotTime;
    private long _lastSnapshotValue;
    private int _autoSnapshotsThisRaid;
    private bool _isInRaid;

    /// <summary>
    /// Lock object for thread-safe singleton initialization.
    /// NEW-006: Ensures proper singleton pattern during rapid scene reloads.
    /// </summary>
    private static readonly object _singletonLock = new();

    // ========================================================================
    // Unity Lifecycle
    // ========================================================================

    private void Awake()
    {
        // NEW-006: Thread-safe singleton pattern
        lock (_singletonLock)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Check if feature is enabled
        long threshold = Settings.AutoSnapshotThreshold?.Value ?? 0;
        if (threshold <= 0)
            return;

        // Update raid state
        UpdateRaidState();

        if (!_isInRaid)
            return;

        // Don't check too frequently
        if (Time.time - _lastCheckTime < CheckInterval)
            return;

        _lastCheckTime = Time.time;
        CheckLootValue(threshold);
    }

    // ========================================================================
    // Raid State
    // ========================================================================

    private void UpdateRaidState()
    {
        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            bool wasInRaid = _isInRaid;
            _isInRaid = gameWorld != null && gameWorld.MainPlayer != null;

            // Reset counters when entering a new raid
            if (_isInRaid && !wasInRaid)
            {
                OnRaidStart();
            }
        }
        catch (Exception ex) when (IsSafeToSwallow(ex))
        {
            // LINUS-004 FIX: Only swallow exceptions that are safe to ignore
            // Critical system exceptions (OutOfMemory, StackOverflow, etc.) will propagate
            Plugin.Log.LogWarning($"[AutoSnapshot] Raid state detection failed: {ex.Message}");
            _isInRaid = false;
        }
    }

    /// <summary>
    /// LINUS-004 FIX: Determines if an exception is safe to swallow and continue.
    /// Critical system exceptions should never be caught and hidden.
    /// </summary>
    private static bool IsSafeToSwallow(Exception ex)
    {
        // Never swallow these - they indicate serious system problems
        return ex is not (OutOfMemoryException or
            StackOverflowException or
            System.Threading.ThreadAbortException or
            System.Runtime.InteropServices.SEHException or
            AccessViolationException);
    }

    private void OnRaidStart()
    {
        _autoSnapshotsThisRaid = 0;
        _lastAutoSnapshotTime = 0;

        // Get initial snapshot value
        try
        {
            string sessionId = ProfileService.Instance?.GetSessionId();
            if (!string.IsNullOrEmpty(sessionId))
            {
                var snapshot = SnapshotManager.Instance?.LoadSnapshot(sessionId);
                if (snapshot != null)
                {
                    var summary = ValueCalculator.Instance?.CalculateSnapshotValue(snapshot);
                    _lastSnapshotValue = summary?.TotalValue ?? 0;
                    Plugin.Log.LogDebug($"[AutoSnapshot] Initial snapshot value: {_lastSnapshotValue}");
                }
            }
        }
        catch (Exception ex) when (IsSafeToSwallow(ex))
        {
            // LINUS-004 FIX: Only swallow safe exceptions
            Plugin.Log.LogWarning($"[AutoSnapshot] Error getting initial snapshot value: {ex.Message}");
        }
    }

    // ========================================================================
    // Value Monitoring
    // ========================================================================

    private void CheckLootValue(long threshold)
    {
        try
        {
            // Check cooldown
            if (Time.time - _lastAutoSnapshotTime < AutoSnapshotCooldown)
                return;

            // Check if we've hit max auto-snapshots
            int maxAuto = Settings.MaxAutoSnapshots?.Value ?? 3;
            if (_autoSnapshotsThisRaid >= maxAuto)
                return;

            // Get current inventory value
            var currentValue = ValueCalculator.Instance?.CalculateCurrentInventoryValue();
            if (currentValue == null)
                return;

            // Calculate value gained since last snapshot
            long valueGained = currentValue.TotalValue - _lastSnapshotValue;

            Plugin.Log.LogDebug($"[AutoSnapshot] Value check: current={currentValue.TotalValue}, last={_lastSnapshotValue}, gained={valueGained}, threshold={threshold}");

            // Check if threshold exceeded
            if (valueGained >= threshold)
            {
                TriggerAutoSnapshot(valueGained);
            }
        }
        catch (Exception ex) when (IsSafeToSwallow(ex))
        {
            // LINUS-004 FIX: Only swallow safe exceptions
            Plugin.Log.LogWarning($"[AutoSnapshot] Error checking loot value: {ex.Message}");
        }
    }

    private void TriggerAutoSnapshot(long valueGained)
    {
        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld?.MainPlayer == null)
                return;

            string sessionId = ProfileService.Instance?.GetSessionId();
            if (string.IsNullOrEmpty(sessionId))
                return;

            // Get location name
            string location = "Unknown";
            try
            {
                location = gameWorld.MainPlayer.Location ?? location;
            }
            catch (Exception ex) when (IsSafeToSwallow(ex))
            {
                // LINUS-004 FIX: Only swallow safe exceptions
                Plugin.Log.LogWarning($"[AutoSnapshot] Could not get location name: {ex.Message}");
            }

            // Capture and save snapshot
            var snapshot = InventoryService.Instance?.CaptureInventory(
                gameWorld.MainPlayer,
                location,
                inRaid: true
            );

            if (snapshot != null && SnapshotManager.Instance?.SaveSnapshot(snapshot) == true)
            {
                _autoSnapshotsThisRaid++;
                _lastAutoSnapshotTime = Time.time;

                // Update to current total value (not accumulated) since snapshot now contains everything
                var newValue = ValueCalculator.Instance?.CalculateCurrentInventoryValue();
                _lastSnapshotValue = newValue?.TotalValue ?? (_lastSnapshotValue + valueGained);

                // Show notification
                string formattedValue = ValueSummary.FormatRubles(valueGained);
                NotificationOverlay.ShowSuccess($"AUTO-SNAPSHOT!\nLoot value +{formattedValue}");

                // Play sound if enabled
                if (Settings.PlaySnapshotSound?.Value == true)
                {
                    SnapshotSoundPlayer.PlaySnapshotSound();
                }

                // Force refresh protection indicator
                ProtectionIndicator.ForceRefresh();

                Plugin.Log.LogDebug($"[AutoSnapshot] Triggered! Value gained: {formattedValue}");
            }
        }
        catch (Exception ex) when (IsSafeToSwallow(ex))
        {
            // LINUS-004 FIX: Only swallow safe exceptions
            Plugin.Log.LogError($"[AutoSnapshot] Error triggering snapshot: {ex.Message}");
        }
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Resets the auto-snapshot state (call when manual snapshot is taken).
    /// </summary>
    public static void OnManualSnapshotTaken()
    {
        if (Instance != null)
        {
            try
            {
                var currentValue = ValueCalculator.Instance?.CalculateCurrentInventoryValue();
                Instance._lastSnapshotValue = currentValue?.TotalValue ?? 0;
                Instance._lastAutoSnapshotTime = Time.time;
            }
            catch (Exception ex) when (IsSafeToSwallow(ex))
            {
                // LINUS-004 FIX: Only swallow safe exceptions
                Plugin.Log.LogWarning($"[AutoSnapshot] Error updating snapshot value after manual snapshot: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the number of auto-snapshots taken this raid.
    /// </summary>
    public static int GetAutoSnapshotsThisRaid()
    {
        return Instance?._autoSnapshotsThisRaid ?? 0;
    }
}
