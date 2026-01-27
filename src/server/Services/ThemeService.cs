// ============================================================================
// Keep Starting Gear - Theme Service
// ============================================================================
// FEATURE 6: Audio/Visual Themes
//
// Provides different visual color schemes and audio options for the mod.
// Allows players to customize the look and feel of notifications to match
// their preferences.
//
// THEMES:
// - Default: Standard green/yellow/red color scheme
// - Neon: Bright cyan/magenta/yellow cyberpunk colors
// - Tactical: Muted military-style browns and grays
// - High Contrast: Maximum readability for accessibility
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using UnityEngine;
using Blackhorse311.KeepStartingGear.Configuration;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Available visual themes for the mod.
/// </summary>
public enum NotificationTheme
{
    /// <summary>Standard green/yellow/red scheme</summary>
    Default,

    /// <summary>Bright cyberpunk colors</summary>
    Neon,

    /// <summary>Muted military colors</summary>
    Tactical,

    /// <summary>High contrast for accessibility</summary>
    HighContrast,

    /// <summary>Minimalist subtle theme</summary>
    Minimal
}

/// <summary>
/// Service that provides theme colors and settings.
/// </summary>
public static class ThemeService
{
    // ========================================================================
    // Theme Color Sets
    // ========================================================================

    /// <summary>
    /// Gets the color set for the current theme.
    /// </summary>
    public static ThemeColors GetCurrentTheme()
    {
        var themeIndex = Settings.NotificationTheme?.Value ?? 0;
        var theme = (NotificationTheme)themeIndex;
        return GetThemeColors(theme);
    }

    /// <summary>
    /// Gets the color set for a specific theme.
    /// </summary>
    public static ThemeColors GetThemeColors(NotificationTheme theme)
    {
        return theme switch
        {
            NotificationTheme.Neon => NeonTheme,
            NotificationTheme.Tactical => TacticalTheme,
            NotificationTheme.HighContrast => HighContrastTheme,
            NotificationTheme.Minimal => MinimalTheme,
            _ => DefaultTheme
        };
    }

    // ========================================================================
    // Theme Definitions
    // ========================================================================

    /// <summary>Standard theme - classic green/yellow/red</summary>
    public static readonly ThemeColors DefaultTheme = new()
    {
        Name = "Default",

        // Success - Green
        SuccessBackground = new Color(0.1f, 0.6f, 0.1f, 0.9f),
        SuccessText = new Color(0.7f, 1f, 0.7f, 1f),
        SuccessBorder = new Color(0.3f, 0.8f, 0.3f, 1f),

        // Warning - Yellow/Orange
        WarningBackground = new Color(0.7f, 0.5f, 0.0f, 0.9f),
        WarningText = new Color(1f, 1f, 0.7f, 1f),
        WarningBorder = new Color(0.9f, 0.7f, 0.2f, 1f),

        // Error - Red
        ErrorBackground = new Color(0.6f, 0.1f, 0.1f, 0.9f),
        ErrorText = new Color(1f, 0.7f, 0.7f, 1f),
        ErrorBorder = new Color(0.8f, 0.3f, 0.3f, 1f),

        // Info - Blue
        InfoBackground = new Color(0.1f, 0.3f, 0.6f, 0.9f),
        InfoText = new Color(0.7f, 0.85f, 1f, 1f),
        InfoBorder = new Color(0.3f, 0.5f, 0.8f, 1f),

        // Panel colors
        PanelBackground = new Color(0.1f, 0.1f, 0.15f, 0.95f),
        PanelText = new Color(0.9f, 0.9f, 0.9f, 1f),
        PanelBorder = new Color(0.4f, 0.4f, 0.5f, 0.8f)
    };

    /// <summary>Neon theme - bright cyberpunk colors</summary>
    public static readonly ThemeColors NeonTheme = new()
    {
        Name = "Neon",

        // Success - Cyan
        SuccessBackground = new Color(0.0f, 0.3f, 0.4f, 0.95f),
        SuccessText = new Color(0.0f, 1f, 1f, 1f),
        SuccessBorder = new Color(0.0f, 0.9f, 0.9f, 1f),

        // Warning - Magenta
        WarningBackground = new Color(0.4f, 0.0f, 0.4f, 0.95f),
        WarningText = new Color(1f, 0.4f, 1f, 1f),
        WarningBorder = new Color(0.9f, 0.2f, 0.9f, 1f),

        // Error - Hot pink
        ErrorBackground = new Color(0.5f, 0.0f, 0.2f, 0.95f),
        ErrorText = new Color(1f, 0.2f, 0.5f, 1f),
        ErrorBorder = new Color(1f, 0.1f, 0.4f, 1f),

        // Info - Electric blue
        InfoBackground = new Color(0.0f, 0.1f, 0.4f, 0.95f),
        InfoText = new Color(0.3f, 0.6f, 1f, 1f),
        InfoBorder = new Color(0.2f, 0.5f, 1f, 1f),

        // Panel colors
        PanelBackground = new Color(0.05f, 0.0f, 0.1f, 0.95f),
        PanelText = new Color(0.9f, 0.9f, 1f, 1f),
        PanelBorder = new Color(0.5f, 0.0f, 0.5f, 0.8f)
    };

    /// <summary>Tactical theme - muted military colors</summary>
    public static readonly ThemeColors TacticalTheme = new()
    {
        Name = "Tactical",

        // Success - Olive
        SuccessBackground = new Color(0.25f, 0.3f, 0.15f, 0.95f),
        SuccessText = new Color(0.7f, 0.8f, 0.5f, 1f),
        SuccessBorder = new Color(0.5f, 0.6f, 0.3f, 1f),

        // Warning - Tan
        WarningBackground = new Color(0.4f, 0.35f, 0.2f, 0.95f),
        WarningText = new Color(0.9f, 0.85f, 0.6f, 1f),
        WarningBorder = new Color(0.7f, 0.65f, 0.4f, 1f),

        // Error - Dark red
        ErrorBackground = new Color(0.35f, 0.15f, 0.1f, 0.95f),
        ErrorText = new Color(0.9f, 0.6f, 0.5f, 1f),
        ErrorBorder = new Color(0.6f, 0.3f, 0.2f, 1f),

        // Info - Steel blue
        InfoBackground = new Color(0.2f, 0.25f, 0.3f, 0.95f),
        InfoText = new Color(0.7f, 0.8f, 0.9f, 1f),
        InfoBorder = new Color(0.4f, 0.5f, 0.6f, 1f),

        // Panel colors
        PanelBackground = new Color(0.12f, 0.12f, 0.1f, 0.95f),
        PanelText = new Color(0.85f, 0.82f, 0.75f, 1f),
        PanelBorder = new Color(0.35f, 0.33f, 0.28f, 0.8f)
    };

    /// <summary>High contrast theme - maximum readability</summary>
    public static readonly ThemeColors HighContrastTheme = new()
    {
        Name = "High Contrast",

        // Success - Bright green on black
        SuccessBackground = new Color(0.0f, 0.0f, 0.0f, 0.98f),
        SuccessText = new Color(0.0f, 1f, 0.0f, 1f),
        SuccessBorder = new Color(0.0f, 1f, 0.0f, 1f),

        // Warning - Bright yellow on black
        WarningBackground = new Color(0.0f, 0.0f, 0.0f, 0.98f),
        WarningText = new Color(1f, 1f, 0.0f, 1f),
        WarningBorder = new Color(1f, 1f, 0.0f, 1f),

        // Error - Bright red on black
        ErrorBackground = new Color(0.0f, 0.0f, 0.0f, 0.98f),
        ErrorText = new Color(1f, 0.0f, 0.0f, 1f),
        ErrorBorder = new Color(1f, 0.0f, 0.0f, 1f),

        // Info - White on black
        InfoBackground = new Color(0.0f, 0.0f, 0.0f, 0.98f),
        InfoText = new Color(1f, 1f, 1f, 1f),
        InfoBorder = new Color(1f, 1f, 1f, 1f),

        // Panel colors
        PanelBackground = new Color(0.0f, 0.0f, 0.0f, 0.98f),
        PanelText = new Color(1f, 1f, 1f, 1f),
        PanelBorder = new Color(1f, 1f, 1f, 1f)
    };

    /// <summary>Minimal theme - subtle and non-intrusive</summary>
    public static readonly ThemeColors MinimalTheme = new()
    {
        Name = "Minimal",

        // Success - Subtle gray-green
        SuccessBackground = new Color(0.15f, 0.18f, 0.15f, 0.85f),
        SuccessText = new Color(0.6f, 0.75f, 0.6f, 1f),
        SuccessBorder = new Color(0.3f, 0.4f, 0.3f, 0.6f),

        // Warning - Subtle gray-yellow
        WarningBackground = new Color(0.18f, 0.17f, 0.12f, 0.85f),
        WarningText = new Color(0.8f, 0.75f, 0.5f, 1f),
        WarningBorder = new Color(0.4f, 0.38f, 0.25f, 0.6f),

        // Error - Subtle gray-red
        ErrorBackground = new Color(0.18f, 0.13f, 0.13f, 0.85f),
        ErrorText = new Color(0.85f, 0.55f, 0.55f, 1f),
        ErrorBorder = new Color(0.4f, 0.25f, 0.25f, 0.6f),

        // Info - Subtle gray
        InfoBackground = new Color(0.14f, 0.15f, 0.17f, 0.85f),
        InfoText = new Color(0.65f, 0.7f, 0.75f, 1f),
        InfoBorder = new Color(0.3f, 0.32f, 0.35f, 0.6f),

        // Panel colors
        PanelBackground = new Color(0.1f, 0.1f, 0.1f, 0.85f),
        PanelText = new Color(0.7f, 0.7f, 0.7f, 1f),
        PanelBorder = new Color(0.25f, 0.25f, 0.25f, 0.5f)
    };
}

/// <summary>
/// Color definitions for a theme.
/// </summary>
public class ThemeColors
{
    public string Name { get; set; } = "Unknown";

    // Success colors
    public Color SuccessBackground { get; set; }
    public Color SuccessText { get; set; }
    public Color SuccessBorder { get; set; }

    // Warning colors
    public Color WarningBackground { get; set; }
    public Color WarningText { get; set; }
    public Color WarningBorder { get; set; }

    // Error colors
    public Color ErrorBackground { get; set; }
    public Color ErrorText { get; set; }
    public Color ErrorBorder { get; set; }

    // Info colors
    public Color InfoBackground { get; set; }
    public Color InfoText { get; set; }
    public Color InfoBorder { get; set; }

    // Panel colors (for overlays)
    public Color PanelBackground { get; set; }
    public Color PanelText { get; set; }
    public Color PanelBorder { get; set; }
}
