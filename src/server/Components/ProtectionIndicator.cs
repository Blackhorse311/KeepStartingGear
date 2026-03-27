// ============================================================================
// Keep Starting Gear - Protection Indicator Component
// ============================================================================
// FEATURE 2: Stash Preview / Protection Indicator
//
// Displays a visual indicator showing:
// - Whether a snapshot is currently active
// - How many items are protected
// - Which slots are included in protection
//
// The indicator appears in the corner of the screen during raids.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.IO;
using UnityEngine;
using Comfort.Common;
using EFT;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Services;
using Blackhorse311.KeepStartingGear.Utilities;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Displays a protection status indicator during raids.
/// Shows snapshot status, item count, and protected slots.
/// </summary>
public class ProtectionIndicator : MonoBehaviour
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static ProtectionIndicator Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>How often to refresh snapshot data (seconds).</summary>
    private const float RefreshInterval = 5.0f;

    /// <summary>Indicator position offset from corner.</summary>
    private const int MarginX = 10;
    private const int MarginY = 60; // Below standard HUD elements

    /// <summary>Panel dimensions.</summary>
    private const int PanelWidth = 200;
    private const int PanelHeight = 80;

    // ========================================================================
    // State
    // ========================================================================

    private float _lastRefreshTime;
    private bool _hasActiveSnapshot;
    private int _protectedItemCount;
    private int _protectedSlotCount;
    private bool _isInRaid;
    private bool _showIndicator;

    /// <summary>Last write time of the snapshot file, used to avoid unnecessary re-reads.</summary>
    private DateTime _lastSnapshotModified;

    /// <summary>Cached session ID to detect session changes.</summary>
    private string _cachedSessionId;

    // ========================================================================
    // Colors - Now uses ThemeService for theming support
    // ========================================================================

    private Color ProtectedColor => ThemeService.GetCurrentTheme().SuccessText;
    private Color UnprotectedColor => ThemeService.GetCurrentTheme().ErrorText;
    private Color PanelBackground => ThemeService.GetCurrentTheme().PanelBackground;
    private Color TextColor => ThemeService.GetCurrentTheme().PanelText;

    // Subtext stays muted across all themes
    private static readonly Color SubtextColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    // ========================================================================
    // GUI Styles
    // ========================================================================

    private GUIStyle _panelStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _textStyle;
    private GUIStyle _subtextStyle;
    private bool _stylesInitialized;

    // ========================================================================
    // Unity Lifecycle
    // ========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
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
        // Determine if we're in a raid
        UpdateRaidState();

        // Don't refresh too often
        if (Time.time - _lastRefreshTime < RefreshInterval)
            return;

        _lastRefreshTime = Time.time;
        RefreshSnapshotData();
    }

    // ========================================================================
    // State Management
    // ========================================================================

    private void UpdateRaidState()
    {
        // Check if we're in a raid by looking for the game world
        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            _isInRaid = gameWorld != null && gameWorld.MainPlayer != null;
        }
        catch
        {
            _isInRaid = false;
        }

        // NEW-007: Explicit null check for Settings initialization
        // Only show indicator if setting is enabled and we're in raid
        if (Settings.ShowProtectionIndicator == null)
        {
            _showIndicator = false;
            return;
        }
        _showIndicator = _isInRaid && Settings.ShowProtectionIndicator.Value;
    }

    private void RefreshSnapshotData()
    {
        if (!_showIndicator)
        {
            _hasActiveSnapshot = false;
            return;
        }

        try
        {
            // Get the current session ID
            string sessionId = ProfileService.Instance?.GetSessionId();
            if (string.IsNullOrEmpty(sessionId))
            {
                _hasActiveSnapshot = false;
                return;
            }

            // CRIT-7 FIX: Cache snapshot metadata and only reload when the file changes.
            // Uses try-catch instead of File.Exists to avoid TOCTOU race (C-4 fix).
            string snapshotPath = Path.Combine(Plugin.GetDataPath(), $"{sessionId}.json");

            DateTime lastWrite;
            try
            {
                lastWrite = File.GetLastWriteTimeUtc(snapshotPath);
            }
            catch (Exception) when (true) // FileNotFoundException, IOException, etc.
            {
                _hasActiveSnapshot = false;
                _lastSnapshotModified = default;
                _cachedSessionId = null;
                return;
            }

            // On Windows, GetLastWriteTimeUtc returns 1601-01-01 for non-existent files
            if (lastWrite.Year < 1970)
            {
                _hasActiveSnapshot = false;
                _lastSnapshotModified = default;
                _cachedSessionId = null;
                return;
            }

            if (lastWrite == _lastSnapshotModified && sessionId == _cachedSessionId)
            {
                // Cache hit: file has not changed and session is the same, skip re-read
                return;
            }

            // Cache miss: load the snapshot and update cached metadata
            var snapshot = SnapshotManager.Instance?.LoadSnapshot(sessionId);
            if (snapshot == null || !snapshot.IsValid())
            {
                _hasActiveSnapshot = false;
                _lastSnapshotModified = default;
                _cachedSessionId = null;
                return;
            }

            _hasActiveSnapshot = true;
            _protectedItemCount = snapshot.Items?.Count ?? 0;
            _protectedSlotCount = snapshot.IncludedSlots?.Count ?? 0;
            _lastSnapshotModified = lastWrite;
            _cachedSessionId = sessionId;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ProtectionIndicator] Error refreshing data: {ex.Message}");
            _hasActiveSnapshot = false;
        }
    }

    // ========================================================================
    // GUI Rendering
    // ========================================================================

    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(8, 8, 6, 6)
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft
        };
        _textStyle.normal.textColor = TextColor;

        _subtextStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleLeft
        };
        _subtextStyle.normal.textColor = SubtextColor;

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!_showIndicator)
            return;

        InitializeStyles();

        // Position in top-right corner
        float x = Screen.width - PanelWidth - MarginX;
        float y = MarginY;

        Rect panelRect = new Rect(x, y, PanelWidth, PanelHeight);

        // Draw background
        Color bgColor = PanelBackground;
        GuiDrawingHelper.DrawRect(panelRect, bgColor);

        // Draw status border
        Color borderColor = _hasActiveSnapshot ? ProtectedColor : UnprotectedColor;
        GuiDrawingHelper.DrawRectBorder(panelRect, borderColor, 2);

        // Content area
        GUILayout.BeginArea(new Rect(x + 8, y + 6, PanelWidth - 16, PanelHeight - 12));

        // Status header
        _headerStyle.normal.textColor = _hasActiveSnapshot ? ProtectedColor : UnprotectedColor;
        string statusIcon = _hasActiveSnapshot ? "✓" : "✗";
        string statusText = _hasActiveSnapshot ? "GEAR PROTECTED" : "NOT PROTECTED";
        GUILayout.Label($"{statusIcon} {statusText}", _headerStyle);

        if (_hasActiveSnapshot)
        {
            // Item count
            GUILayout.Label($"  {_protectedItemCount} items saved", _textStyle);

            // Slot count
            GUILayout.Label($"  {_protectedSlotCount} slots included", _subtextStyle);
        }
        else
        {
            // Not protected message
            var mode = Settings.SnapshotModeOption?.Value ?? SnapshotMode.AutoOnly;
            if (mode == SnapshotMode.ManualOnly)
            {
                GUILayout.Label("  Press hotkey to save", _textStyle);
            }
            else
            {
                GUILayout.Label("  Waiting for snapshot...", _textStyle);
            }
        }

        GUILayout.EndArea();
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Forces an immediate refresh of snapshot data.
    /// Call this after taking a manual snapshot.
    /// </summary>
    public static void ForceRefresh()
    {
        if (Instance != null)
        {
            Instance._lastRefreshTime = 0;
            Instance.RefreshSnapshotData();
        }
    }

    /// <summary>
    /// Ensures the indicator instance exists.
    /// </summary>
    /// <remarks>
    /// Fallback creation path for when component is accessed before Plugin.Awake().
    /// Plugin.Awake() is the primary creation path; this handles rare edge cases
    /// where ForceRefresh() or other callers run before the plugin has fully initialized.
    /// DontDestroyOnLoad in Awake() is intentional to survive scene transitions.
    /// </remarks>
    public static void EnsureInstance()
    {
        if (Instance == null)
        {
            var go = new GameObject("KeepStartingGear_ProtectionIndicator");
            Instance = go.AddComponent<ProtectionIndicator>();
        }
    }
}
