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
using Blackhorse311.KeepStartingGear.Utilities;

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
    // Colors - Now uses ThemeService for theming support
    // ========================================================================

    private Color PanelBackground => ThemeService.GetCurrentTheme().PanelBackground;
    private Color HeaderColor => ThemeService.GetCurrentTheme().PanelText;
    private Color RestoredColor => ThemeService.GetCurrentTheme().SuccessText;
    private Color LostColor => ThemeService.GetCurrentTheme().ErrorText;

    // FiR color stays gold and divider stays gray across all themes (theme-independent)
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
    private GUIStyle _mapStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _hintStyle;
    private GUIStyle _moreStyle;
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

    // Fallback creation path for when component is accessed before Plugin.Awake().
    // Plugin.Awake() is the primary creation path; this handles rare edge cases
    // where Show() is called before the plugin has fully initialized.
    // DontDestroyOnLoad in Awake() is intentional to survive scene transitions.
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

        _mapStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic
        };
        _mapStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        _centerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter
        };
        _centerStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        _hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
        _hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        _moreStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Italic
        };
        _moreStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);

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
        GuiDrawingHelper.DrawRect(panelRect, PanelBackground * new Color(1, 1, 1, alpha));

        // Draw border
        GuiDrawingHelper.DrawRectBorder(panelRect, new Color(0.4f, 0.4f, 0.5f, alpha), 2);

        // Content area
        GUILayout.BeginArea(new Rect(x + 15, y + 15, PanelWidth - 30, panelHeight - 30));

        // Title
        _headerStyle.normal.textColor = HeaderColor * new Color(1, 1, 1, alpha);
        GUILayout.Label("⚔ GEAR RESTORATION SUMMARY ⚔", _headerStyle);
        GUILayout.Space(5);

        // Map name
        _mapStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, alpha);
        GUILayout.Label($"Death on {_currentSummary.MapName}", _mapStyle);
        GUILayout.Space(10);

        // Divider
        GuiDrawingHelper.DrawHorizontalLine(DividerColor * new Color(1, 1, 1, alpha));
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
            _centerStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, alpha);
            GUILayout.Label("No items to display.", _centerStyle);
        }

        GUILayout.EndScrollView();

        // Close hint
        GUILayout.FlexibleSpace();
        _hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, alpha);
        GUILayout.Label("Click or press ESC to dismiss", _hintStyle);

        GUILayout.EndArea();

        GUI.color = oldColor;
    }

    private void DrawItemList(List<ItemSummary> items, GUIStyle style, float alpha, int maxItems = 10, bool showFiR = false)
    {
        // Save original colors to avoid mutating shared GUIStyle references
        Color originalStyleColor = style.normal.textColor;
        Color originalFirStyleColor = _firStyle.normal.textColor;

        foreach (var item in items.Take(maxItems))
        {
            // Use GUI.contentColor for per-item color rather than mutating the shared style
            if (showFiR && item.WasFoundInRaid)
            {
                _firStyle.normal.textColor = FiRColor * new Color(1, 1, 1, alpha);
                string countStr = item.Count > 1 ? $" x{item.Count}" : "";
                string firStr = " [FiR]";
                GUILayout.Label($"  • {item.ShortName}{countStr}{firStr}", _firStyle);
            }
            else
            {
                style.normal.textColor = originalStyleColor * new Color(1, 1, 1, alpha);
                string countStr = item.Count > 1 ? $" x{item.Count}" : "";
                GUILayout.Label($"  • {item.ShortName}{countStr}", style);
            }

        }

        // Restore original colors after loop to avoid side effects on subsequent calls
        style.normal.textColor = originalStyleColor;
        _firStyle.normal.textColor = originalFirStyleColor;

        // Show overflow indicator
        if (items.Count > maxItems)
        {
            _moreStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, alpha);
            GUILayout.Label($"  ... and {items.Count - maxItems} more items", _moreStyle);
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

}
