// ============================================================================
// Keep Starting Gear - Summary Overlay Component
// ============================================================================
// Displays a detailed post-death summary showing what was restored vs lost.
// FEATURE 1: Post-Death Summary Screen
//
// This overlay shows:
// - Items restored from snapshot (what you got back)
// - Items lost (what you picked up during raid and lost)
// - Clear visual separation with color coding
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Blackhorse311.KeepStartingGear.Services;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Displays a detailed restoration summary overlay after death.
/// Shows what items were restored and what items were lost.
/// </summary>
public class SummaryOverlay : MonoBehaviour
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static SummaryOverlay Instance { get; private set; }

    // ========================================================================
    // Display State
    // ========================================================================

    private RestorationSummaryData _currentSummary;
    private float _displayStartTime;
    private bool _isDisplaying;
    private Vector2 _scrollPosition;

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>How long to display the summary (seconds).</summary>
    private const float DisplayDuration = 12f;

    /// <summary>Fade out duration (seconds).</summary>
    private const float FadeOutDuration = 1f;

    /// <summary>Width of the summary panel.</summary>
    private const int PanelWidth = 500;

    /// <summary>Maximum height of the summary panel.</summary>
    private const int MaxPanelHeight = 400;

    /// <summary>Font size for headers.</summary>
    private const int HeaderFontSize = 20;

    /// <summary>Font size for item lists.</summary>
    private const int ItemFontSize = 14;

    // ========================================================================
    // Colors
    // ========================================================================

    private static readonly Color PanelBackground = new Color(0.1f, 0.1f, 0.15f, 0.95f);
    private static readonly Color HeaderColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color RestoredColor = new Color(0.4f, 0.9f, 0.4f, 1f);     // Green
    private static readonly Color LostColor = new Color(0.9f, 0.4f, 0.4f, 1f);         // Red
    private static readonly Color FiRColor = new Color(1f, 0.85f, 0.3f, 1f);           // Gold for FiR
    private static readonly Color DividerColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    // ========================================================================
    // GUI Styles (initialized lazily)
    // ========================================================================

    private GUIStyle _panelStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _restoredStyle;
    private GUIStyle _lostStyle;
    private GUIStyle _firStyle;
    private GUIStyle _closeButtonStyle;
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
        // Check for pending summary to display
        if (!_isDisplaying && RestorationSummaryService.HasPendingSummary())
        {
            // Check if death summary is enabled in settings
            if (Configuration.Settings.ShowDeathSummary?.Value != true)
            {
                RestorationSummaryService.ClearPendingSummary();
                return;
            }

            var summary = RestorationSummaryService.GetPendingSummary();
            if (summary != null)
            {
                ShowSummary(summary);
                RestorationSummaryService.ClearPendingSummary();
            }
        }

        // Check for dismiss input (Escape or any click after 2 seconds)
        if (_isDisplaying)
        {
            float elapsed = Time.time - _displayStartTime;

            // Allow early dismiss after 2 seconds
            if (elapsed > 2f)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    DismissSummary();
                }
            }

            // Auto-dismiss after duration
            if (elapsed > DisplayDuration + FadeOutDuration)
            {
                DismissSummary();
            }
        }
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Shows the summary overlay.
    /// </summary>
    public static void Show(RestorationSummaryData summary)
    {
        EnsureInstance();
        Instance?.ShowSummary(summary);
    }

    /// <summary>
    /// Dismisses the summary overlay immediately.
    /// </summary>
    public static void Dismiss()
    {
        Instance?.DismissSummary();
    }

    // ========================================================================
    // Private Methods
    // ========================================================================

    private static void EnsureInstance()
    {
        if (Instance == null)
        {
            var go = new GameObject("KeepStartingGear_SummaryOverlay");
            Instance = go.AddComponent<SummaryOverlay>();
        }
    }

    private void ShowSummary(RestorationSummaryData summary)
    {
        _currentSummary = summary;
        _displayStartTime = Time.time;
        _isDisplaying = true;
        _scrollPosition = Vector2.zero;

        Plugin.Log.LogDebug($"[SummaryOverlay] Showing summary: {summary.RestoredCount} restored, {summary.LostCount} lost");
    }

    private void DismissSummary()
    {
        _isDisplaying = false;
        _currentSummary = null;
    }

    // ========================================================================
    // GUI Rendering
    // ========================================================================

    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(15, 15, 15, 15)
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = HeaderFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _restoredStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = ItemFontSize,
            wordWrap = true
        };
        _restoredStyle.normal.textColor = RestoredColor;

        _lostStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = ItemFontSize,
            wordWrap = true
        };
        _lostStyle.normal.textColor = LostColor;

        _firStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = ItemFontSize,
            wordWrap = true
        };
        _firStyle.normal.textColor = FiRColor;

        _closeButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12
        };

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!_isDisplaying || _currentSummary == null)
            return;

        InitializeStyles();

        // Calculate fade
        float elapsed = Time.time - _displayStartTime;
        float alpha = 1f;
        if (elapsed > DisplayDuration)
        {
            alpha = 1f - ((elapsed - DisplayDuration) / FadeOutDuration);
            alpha = Mathf.Clamp01(alpha);
        }

        // Apply alpha to GUI
        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // Calculate panel position (centered)
        float panelHeight = CalculatePanelHeight();
        float x = (Screen.width - PanelWidth) / 2f;
        float y = (Screen.height - panelHeight) / 2f;

        Rect panelRect = new Rect(x, y, PanelWidth, panelHeight);

        // Draw background
        DrawRect(panelRect, PanelBackground * new Color(1, 1, 1, alpha));

        // Draw border
        DrawRectBorder(panelRect, new Color(0.4f, 0.4f, 0.5f, alpha), 2);

        // Content area
        GUILayout.BeginArea(new Rect(x + 15, y + 15, PanelWidth - 30, panelHeight - 30));

        // Title
        _headerStyle.normal.textColor = HeaderColor * new Color(1, 1, 1, alpha);
        GUILayout.Label("⚔ GEAR RESTORATION SUMMARY ⚔", _headerStyle);
        GUILayout.Space(5);

        // Map name
        var mapStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic
        };
        mapStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, alpha);
        GUILayout.Label($"Death on {_currentSummary.MapName}", mapStyle);
        GUILayout.Space(10);

        // Divider
        DrawHorizontalLine(DividerColor * new Color(1, 1, 1, alpha));
        GUILayout.Space(10);

        // Scrollable content
        float contentHeight = panelHeight - 120; // Subtract header space
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(contentHeight));

        // Restored items section
        if (_currentSummary.RestoredCount > 0)
        {
            _headerStyle.normal.textColor = RestoredColor * new Color(1, 1, 1, alpha);
            _headerStyle.fontSize = 16;
            GUILayout.Label($"✓ RESTORED ({_currentSummary.RestoredCount} items)", _headerStyle);
            _headerStyle.fontSize = HeaderFontSize;
            GUILayout.Space(5);

            DrawItemList(_currentSummary.RestoredItems, _restoredStyle, alpha, maxItems: 8);
            GUILayout.Space(15);
        }

        // Lost items section
        if (_currentSummary.LostCount > 0)
        {
            _headerStyle.normal.textColor = LostColor * new Color(1, 1, 1, alpha);
            _headerStyle.fontSize = 16;
            GUILayout.Label($"✗ LOST ({_currentSummary.LostCount} items)", _headerStyle);
            _headerStyle.fontSize = HeaderFontSize;
            GUILayout.Space(5);

            DrawItemList(_currentSummary.LostItems, _lostStyle, alpha, maxItems: 8, showFiR: true);
        }

        // No items case
        if (_currentSummary.RestoredCount == 0 && _currentSummary.LostCount == 0)
        {
            var centerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            centerStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, alpha);
            GUILayout.Label("No items to display.", centerStyle);
        }

        GUILayout.EndScrollView();

        // Close hint
        GUILayout.FlexibleSpace();
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
        hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, alpha);
        GUILayout.Label("Click or press ESC to dismiss", hintStyle);

        GUILayout.EndArea();

        GUI.color = oldColor;
    }

    private void DrawItemList(List<ItemSummary> items, GUIStyle style, float alpha, int maxItems = 10, bool showFiR = false)
    {
        int displayed = 0;
        foreach (var item in items.Take(maxItems))
        {
            var displayStyle = style;

            // Highlight FiR items
            if (showFiR && item.WasFoundInRaid)
            {
                displayStyle = _firStyle;
                displayStyle.normal.textColor = FiRColor * new Color(1, 1, 1, alpha);
            }
            else
            {
                displayStyle.normal.textColor = style.normal.textColor * new Color(1, 1, 1, alpha);
            }

            string countStr = item.Count > 1 ? $" x{item.Count}" : "";
            string firStr = (showFiR && item.WasFoundInRaid) ? " [FiR]" : "";

            GUILayout.Label($"  • {item.ShortName}{countStr}{firStr}", displayStyle);
            displayed++;
        }

        // Show overflow indicator
        if (items.Count > maxItems)
        {
            var moreStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Italic
            };
            moreStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, alpha);
            GUILayout.Label($"  ... and {items.Count - maxItems} more items", moreStyle);
        }
    }

    private float CalculatePanelHeight()
    {
        if (_currentSummary == null) return 200;

        // Base height for header, dividers, etc.
        float height = 150;

        // Add height for restored items section
        if (_currentSummary.RestoredCount > 0)
        {
            height += 30 + Math.Min(_currentSummary.RestoredCount, 8) * 20 + 15;
        }

        // Add height for lost items section
        if (_currentSummary.LostCount > 0)
        {
            height += 30 + Math.Min(_currentSummary.LostCount, 8) * 20;
        }

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
        // Top
        DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        // Bottom
        DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        // Left
        DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        // Right
        DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void DrawHorizontalLine(Color color)
    {
        Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        DrawRect(rect, color);
    }
}
