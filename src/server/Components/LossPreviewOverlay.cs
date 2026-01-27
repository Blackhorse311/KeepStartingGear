// ============================================================================
// Keep Starting Gear - Loss Preview Overlay
// ============================================================================
// FEATURE 3: "What Would I Lose?" Keybind
//
// When the player presses a configurable keybind, this overlay shows what
// items they would lose if they died right now. This helps players make
// informed decisions about when to extract or update their snapshot.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Comfort.Common;
using EFT;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Services;
using Blackhorse311.KeepStartingGear.Models;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Displays a preview of what items would be lost if the player died.
/// Shows the difference between current inventory and snapshot.
/// </summary>
public class LossPreviewOverlay : MonoBehaviour
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static LossPreviewOverlay Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>How long to display the preview (seconds).</summary>
    private const float DisplayDuration = 8f;

    /// <summary>Panel dimensions.</summary>
    private const int PanelWidth = 400;
    private const int MaxPanelHeight = 350;

    // ========================================================================
    // State
    // ========================================================================

    private bool _isDisplaying;
    private float _displayStartTime;
    private List<LossPreviewItem> _lostItems = new();
    private int _totalLostCount;
    private Vector2 _scrollPosition;

    // ========================================================================
    // Colors - Now uses ThemeService for theming support
    // ========================================================================

    private Color PanelBackground => ThemeService.GetCurrentTheme().PanelBackground;
    private Color HeaderColor => ThemeService.GetCurrentTheme().PanelText;
    private Color LostColor => ThemeService.GetCurrentTheme().ErrorText;
    private Color BorderColor => ThemeService.GetCurrentTheme().ErrorBorder;
    private Color SafeColor => ThemeService.GetCurrentTheme().SuccessText;

    // FiR color stays gold across all themes for consistency
    private static readonly Color FiRColor = new Color(1f, 0.85f, 0.3f, 1f);

    // ========================================================================
    // GUI Styles
    // ========================================================================

    private GUIStyle _headerStyle;
    private GUIStyle _itemStyle;
    private GUIStyle _firStyle;
    private GUIStyle _safeStyle;
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
        // Check for keybind press
        if (Settings.LossPreviewKeybind != null &&
            Settings.LossPreviewKeybind.Value.IsDown() &&
            !_isDisplaying)
        {
            ShowLossPreview();
        }

        // Auto-dismiss
        if (_isDisplaying)
        {
            if (Time.time - _displayStartTime > DisplayDuration)
            {
                DismissPreview();
            }

            // Also dismiss on ESC or click
            if (Time.time - _displayStartTime > 1f) // Small delay before allowing dismiss
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0))
                {
                    DismissPreview();
                }
            }
        }
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Shows the loss preview overlay.
    /// </summary>
    public static void Show()
    {
        EnsureInstance();
        Instance?.ShowLossPreview();
    }

    /// <summary>
    /// Dismisses the preview overlay.
    /// </summary>
    public static void Dismiss()
    {
        Instance?.DismissPreview();
    }

    /// <summary>
    /// Ensures the overlay instance exists.
    /// </summary>
    public static void EnsureInstance()
    {
        if (Instance == null)
        {
            var go = new GameObject("KeepStartingGear_LossPreviewOverlay");
            Instance = go.AddComponent<LossPreviewOverlay>();
        }
    }

    // ========================================================================
    // Core Logic
    // ========================================================================

    private void ShowLossPreview()
    {
        try
        {
            // Check if we're in a raid
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null || gameWorld.MainPlayer == null)
            {
                Plugin.Log.LogDebug("[LossPreview] Not in raid, cannot show preview");
                return;
            }

            // Get current session ID
            string sessionId = ProfileService.Instance?.GetSessionId();
            if (string.IsNullOrEmpty(sessionId))
            {
                Plugin.Log.LogDebug("[LossPreview] No session ID");
                return;
            }

            // Load the current snapshot
            var snapshot = SnapshotManager.Instance?.LoadSnapshot(sessionId);
            if (snapshot == null || !snapshot.IsValid())
            {
                // No snapshot = everything would be lost
                ShowNoSnapshotWarning();
                return;
            }

            // Build set of snapshot item IDs
            var snapshotItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in snapshot.Items)
            {
                if (!string.IsNullOrEmpty(item.Id))
                    snapshotItemIds.Add(item.Id);
            }

            // Get current inventory and find items not in snapshot
            _lostItems.Clear();
            _totalLostCount = 0;

            var currentItems = InventoryService.Instance?.GetCurrentInventoryItems();
            if (currentItems != null)
            {
                foreach (var item in currentItems)
                {
                    // Skip items that are in the snapshot (they would be restored)
                    if (snapshotItemIds.Contains(item.Id))
                        continue;

                    // Skip the Equipment container itself
                    if (item.Tpl == "55d7217a4bdc2d86028b456d")
                        continue;

                    // NEW-001: Null-safe access to item.Tpl for ShortName
                    string shortName = item.ShortName;
                    if (string.IsNullOrEmpty(shortName) && !string.IsNullOrEmpty(item.Tpl))
                    {
                        shortName = item.Tpl.Length > 12 ? item.Tpl.Substring(0, 12) : item.Tpl;
                    }
                    shortName ??= "???";

                    // This item would be lost
                    _lostItems.Add(new LossPreviewItem
                    {
                        TemplateId = item.Tpl ?? "",
                        Name = item.Name ?? item.Tpl ?? "Unknown",
                        ShortName = shortName,
                        Count = item.StackCount,
                        WasFoundInRaid = item.IsFoundInRaid
                    });
                    _totalLostCount++;
                }
            }

            // Consolidate duplicate items
            _lostItems = ConsolidateItems(_lostItems);

            _isDisplaying = true;
            _displayStartTime = Time.time;
            _scrollPosition = Vector2.zero;

            Plugin.Log.LogDebug($"[LossPreview] Showing preview: {_totalLostCount} items would be lost");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LossPreview] Error showing preview: {ex.Message}");
        }
    }

    private void ShowNoSnapshotWarning()
    {
        _lostItems.Clear();
        _lostItems.Add(new LossPreviewItem
        {
            Name = "⚠ NO SNAPSHOT ACTIVE",
            ShortName = "WARNING",
            Count = 0,
            WasFoundInRaid = false
        });
        _totalLostCount = -1; // Special marker for "no snapshot"

        _isDisplaying = true;
        _displayStartTime = Time.time;
        _scrollPosition = Vector2.zero;
    }

    private void DismissPreview()
    {
        _isDisplaying = false;
        _lostItems.Clear();
    }

    private List<LossPreviewItem> ConsolidateItems(List<LossPreviewItem> items)
    {
        var grouped = new Dictionary<string, LossPreviewItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (grouped.TryGetValue(item.TemplateId, out var existing))
            {
                existing.Count += item.Count;
                if (item.WasFoundInRaid)
                    existing.WasFoundInRaid = true;
            }
            else
            {
                grouped[item.TemplateId] = new LossPreviewItem
                {
                    TemplateId = item.TemplateId,
                    Name = item.Name,
                    ShortName = item.ShortName,
                    Count = item.Count,
                    WasFoundInRaid = item.WasFoundInRaid
                };
            }
        }

        return grouped.Values
            .OrderByDescending(i => i.WasFoundInRaid)
            .ThenByDescending(i => i.Count)
            .ToList();
    }

    // ========================================================================
    // GUI Rendering
    // ========================================================================

    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _itemStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true
        };
        _itemStyle.normal.textColor = LostColor;

        _firStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true
        };
        _firStyle.normal.textColor = FiRColor;

        _safeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _safeStyle.normal.textColor = SafeColor;

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        // NEW-009: Add null check for _lostItems
        if (!_isDisplaying || _lostItems == null)
            return;

        InitializeStyles();

        // Calculate panel size
        float panelHeight = CalculatePanelHeight();
        float x = (Screen.width - PanelWidth) / 2f;
        float y = (Screen.height - panelHeight) / 2f;

        Rect panelRect = new Rect(x, y, PanelWidth, panelHeight);

        // Draw background
        DrawRect(panelRect, PanelBackground);
        DrawRectBorder(panelRect, BorderColor, 2);

        // Content
        GUILayout.BeginArea(new Rect(x + 15, y + 15, PanelWidth - 30, panelHeight - 30));

        // Header
        _headerStyle.normal.textColor = HeaderColor;

        if (_totalLostCount == -1)
        {
            // No snapshot warning
            _headerStyle.normal.textColor = LostColor;
            GUILayout.Label("⚠ NO PROTECTION ACTIVE", _headerStyle);
            GUILayout.Space(10);

            var warningStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            warningStyle.normal.textColor = LostColor;
            GUILayout.Label("You have no active snapshot!\nAll items would be lost on death.", warningStyle);
            GUILayout.Space(10);
            GUILayout.Label("Take a snapshot to protect your gear.", warningStyle);
        }
        else if (_totalLostCount == 0)
        {
            // Nothing would be lost
            GUILayout.Label("✓ IF YOU DIED NOW...", _headerStyle);
            GUILayout.Space(15);
            GUILayout.Label("You would lose NOTHING!", _safeStyle);
            GUILayout.Space(10);

            var infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };
            infoStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            GUILayout.Label("All current items are in your snapshot.", infoStyle);
        }
        else
        {
            // Show lost items
            _headerStyle.normal.textColor = LostColor;
            GUILayout.Label($"⚠ IF YOU DIED NOW... ({_totalLostCount} items lost)", _headerStyle);
            GUILayout.Space(10);

            // Scrollable list
            float contentHeight = panelHeight - 80;
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(contentHeight));

            int maxDisplay = 15;

            foreach (var item in _lostItems.Take(maxDisplay))
            {
                var style = item.WasFoundInRaid ? _firStyle : _itemStyle;
                string countStr = item.Count > 1 ? $" x{item.Count}" : "";
                string firStr = item.WasFoundInRaid ? " [FiR]" : "";

                GUILayout.Label($"  • {item.ShortName}{countStr}{firStr}", style);
            }

            if (_lostItems.Count > maxDisplay)
            {
                var moreStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Italic,
                    fontSize = 11
                };
                moreStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                GUILayout.Label($"  ... and {_lostItems.Count - maxDisplay} more items", moreStyle);
            }

            GUILayout.EndScrollView();
        }

        // Dismiss hint
        GUILayout.FlexibleSpace();
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10
        };
        hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        GUILayout.Label("Click or press ESC to dismiss", hintStyle);

        GUILayout.EndArea();
    }

    private float CalculatePanelHeight()
    {
        if (_totalLostCount <= 0)
            return 150;

        float height = 100 + Math.Min(_lostItems.Count, 15) * 22;
        return Math.Min(height, MaxPanelHeight);
    }

    // ========================================================================
    // Drawing Utilities
    // ========================================================================

    private void DrawRect(Rect rect, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    private void DrawRectBorder(Rect rect, Color color, int thickness)
    {
        DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}

/// <summary>
/// Represents an item in the loss preview.
/// </summary>
internal class LossPreviewItem
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public bool WasFoundInRaid { get; set; }
}
