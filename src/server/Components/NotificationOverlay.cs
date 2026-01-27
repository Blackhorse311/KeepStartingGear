// ============================================================================
// Keep Starting Gear - Notification Overlay Component
// ============================================================================
// This Unity MonoBehaviour component displays large, centered, colored
// notifications on screen to provide clear feedback to the player.
//
// NOTIFICATION TYPES:
// - Success (Green): Snapshot saved, inventory restored, extraction successful
// - Warning (Yellow): Snapshot limit reached, non-critical issues
// - Error (Red): Failed to save/capture, critical errors
//
// DESIGN PRINCIPLES:
// 1. High visibility - Large text centered on screen, impossible to miss
// 2. Color coding - Immediate recognition of notification type
// 3. Non-intrusive - Auto-fades after display duration
// 4. Queued display - Multiple notifications shown sequentially, not stacked
//
// IMPLEMENTATION:
// Uses Unity's immediate-mode GUI (OnGUI) for maximum compatibility across
// different game states and UI configurations. This approach works reliably
// even when other UI systems may be unavailable or in transition.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Displays large centered colored notifications on screen using Unity's IMGUI system.
/// Provides visual feedback for snapshot operations with color-coded message types.
/// </summary>
/// <remarks>
/// <para>
/// This component uses a singleton pattern to ensure only one notification overlay
/// exists at a time. It persists across scene loads (DontDestroyOnLoad) to maintain
/// continuity during game state transitions.
/// </para>
/// <para>
/// Notifications are queued and displayed one at a time. Each notification has a
/// configurable duration followed by a fade-out animation for smooth visual transitions.
/// </para>
/// </remarks>
public class NotificationOverlay : MonoBehaviour
{
    // ========================================================================
    // Singleton Pattern
    // ========================================================================

    /// <summary>
    /// Singleton instance of the NotificationOverlay.
    /// Created automatically when ShowSuccess/Warning/Error is called.
    /// </summary>
    public static NotificationOverlay Instance { get; private set; }

    // ========================================================================
    // Notification Types and Data Structures
    // ========================================================================

    /// <summary>
    /// Defines the types of notifications that can be displayed.
    /// Each type has an associated color scheme for visual distinction.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>Green notification for successful operations</summary>
        Success,

        /// <summary>Yellow/orange notification for warnings or limits</summary>
        Warning,

        /// <summary>Red notification for errors or failures</summary>
        Error,

        /// <summary>Blue notification for informational messages</summary>
        Info
    }

    /// <summary>
    /// Internal class representing a single notification in the queue.
    /// Contains all data needed to render and time the notification.
    /// </summary>
    private class Notification
    {
        /// <summary>The message text to display (supports newlines)</summary>
        public string Message { get; set; }

        /// <summary>The type determines the color scheme</summary>
        public NotificationType Type { get; set; }

        /// <summary>Time.time when this notification started displaying</summary>
        public float StartTime { get; set; }

        /// <summary>How long to display at full opacity (seconds)</summary>
        public float Duration { get; set; }

        /// <summary>How long the fade-out animation takes (seconds)</summary>
        public float FadeOutDuration { get; set; }
    }

    // ========================================================================
    // Instance Fields
    // ========================================================================

    /// <summary>
    /// Queue of pending notifications waiting to be displayed.
    /// Notifications are shown one at a time in FIFO order.
    /// </summary>
    /// <remarks>
    /// REL-003: Access to this queue is synchronized via _queueLock to prevent
    /// race conditions when notifications are added from different code paths.
    /// </remarks>
    private readonly Queue<Notification> _notificationQueue = new();

    /// <summary>
    /// Lock object for thread-safe access to the notification queue.
    /// </summary>
    private readonly object _queueLock = new();

    /// <summary>
    /// The notification currently being displayed, or null if none.
    /// </summary>
    private Notification _currentNotification;

    /// <summary>
    /// GUI style for the notification box background.
    /// Initialized lazily in InitializeStyles().
    /// </summary>
    private GUIStyle _boxStyle;

    /// <summary>
    /// GUI style for the notification text.
    /// Initialized lazily in InitializeStyles().
    /// </summary>
    private GUIStyle _textStyle;

    /// <summary>
    /// Flag to track whether GUI styles have been initialized.
    /// Styles must be created during OnGUI, not earlier.
    /// </summary>
    private bool _stylesInitialized;

    /// <summary>
    /// Cached texture for box background. Must be destroyed on cleanup to prevent VRAM leak.
    /// </summary>
    private Texture2D _backgroundTexture;

    // ========================================================================
    // Display Configuration Constants
    // ========================================================================

    /// <summary>Default duration in seconds before fade-out begins</summary>
    private const float DefaultDuration = 3.0f;

    /// <summary>Duration of the fade-out animation in seconds</summary>
    private const float DefaultFadeOutDuration = 0.5f;

    /// <summary>Width of the notification box in pixels</summary>
    private const int BoxWidth = 500;

    /// <summary>Height of the notification box in pixels</summary>
    private const int BoxHeight = 80;

    /// <summary>Font size for notification text</summary>
    private const int FontSize = 24;

    // ========================================================================
    // Color Definitions
    // Each notification type has a background and text color
    // Colors use RGBA with alpha for transparency
    // ========================================================================

    /// <summary>Dark green background for success notifications</summary>
    private static readonly Color SuccessColor = new Color(0.1f, 0.6f, 0.1f, 0.9f);

    /// <summary>Light green text for success notifications</summary>
    private static readonly Color SuccessTextColor = new Color(0.7f, 1f, 0.7f, 1f);

    /// <summary>Dark yellow/orange background for warning notifications</summary>
    private static readonly Color WarningColor = new Color(0.7f, 0.5f, 0.0f, 0.9f);

    /// <summary>Light yellow text for warning notifications</summary>
    private static readonly Color WarningTextColor = new Color(1f, 1f, 0.7f, 1f);

    /// <summary>Dark red background for error notifications</summary>
    private static readonly Color ErrorColor = new Color(0.6f, 0.1f, 0.1f, 0.9f);

    /// <summary>Light red/pink text for error notifications</summary>
    private static readonly Color ErrorTextColor = new Color(1f, 0.7f, 0.7f, 1f);

    /// <summary>Dark blue background for info notifications</summary>
    private static readonly Color InfoColor = new Color(0.1f, 0.3f, 0.6f, 0.9f);

    /// <summary>Light blue text for info notifications</summary>
    private static readonly Color InfoTextColor = new Color(0.7f, 0.85f, 1f, 1f);

    // ========================================================================
    // Unity Lifecycle Methods
    // ========================================================================

    /// <summary>
    /// Called when the component is created. Sets up singleton instance.
    /// </summary>
    /// <remarks>
    /// Implements singleton pattern - if another instance already exists,
    /// this one is destroyed. The surviving instance is marked to persist
    /// across scene loads.
    /// </remarks>
    private void Awake()
    {
        // Singleton enforcement - only one NotificationOverlay allowed
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Persist across scene loads (hideout <-> raid transitions)
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Called when the component is destroyed. Cleans up singleton reference and textures.
    /// </summary>
    private void OnDestroy()
    {
        // Clean up texture to prevent VRAM leak
        if (_backgroundTexture != null)
        {
            Destroy(_backgroundTexture);
            _backgroundTexture = null;
        }

        // Clear singleton reference if this was the active instance
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Called every frame. Manages notification timing and queue processing.
    /// </summary>
    /// <remarks>
    /// Checks if the current notification has expired and advances to the
    /// next queued notification if available.
    /// </remarks>
    private void Update()
    {
        // If no notification is displaying, check the queue
        if (_currentNotification == null)
        {
            // REL-003: Thread-safe queue access
            lock (_queueLock)
            {
                if (_notificationQueue.Count > 0)
                {
                    // Dequeue next notification and set its start time
                    _currentNotification = _notificationQueue.Dequeue();
                    _currentNotification.StartTime = Time.time;
                }
            }
            return;
        }

        // Check if current notification has expired (display + fade-out complete)
        float elapsed = Time.time - _currentNotification.StartTime;
        float totalDuration = _currentNotification.Duration + _currentNotification.FadeOutDuration;

        if (elapsed >= totalDuration)
        {
            // Clear current notification, Update() will pick up next queued one
            _currentNotification = null;
        }
    }

    // ========================================================================
    // Public Static Methods (API)
    // These are the main entry points for displaying notifications
    // ========================================================================

    /// <summary>
    /// Displays a success notification with green color scheme.
    /// Use for: snapshot saved, inventory restored, extraction successful.
    /// </summary>
    /// <param name="message">The message to display (supports \n for newlines)</param>
    /// <param name="duration">How long to show before fading (default: 3 seconds)</param>
    public static void ShowSuccess(string message, float duration = DefaultDuration)
    {
        Show(message, NotificationType.Success, duration);
    }

    /// <summary>
    /// Displays a warning notification with yellow/orange color scheme.
    /// Use for: snapshot limit reached, non-critical issues.
    /// </summary>
    /// <param name="message">The message to display (supports \n for newlines)</param>
    /// <param name="duration">How long to show before fading (default: 3 seconds)</param>
    public static void ShowWarning(string message, float duration = DefaultDuration)
    {
        Show(message, NotificationType.Warning, duration);
    }

    /// <summary>
    /// Displays an error notification with red color scheme.
    /// Use for: failed to save/capture, critical errors.
    /// </summary>
    /// <param name="message">The message to display (supports \n for newlines)</param>
    /// <param name="duration">How long to show before fading (default: 3 seconds)</param>
    public static void ShowError(string message, float duration = DefaultDuration)
    {
        Show(message, NotificationType.Error, duration);
    }

    /// <summary>
    /// Displays an info notification with blue color scheme.
    /// Use for: informational messages, mode reminders, keybind hints.
    /// </summary>
    /// <param name="message">The message to display (supports \n for newlines)</param>
    /// <param name="duration">How long to show before fading (default: 3 seconds)</param>
    public static void ShowInfo(string message, float duration = DefaultDuration)
    {
        Show(message, NotificationType.Info, duration);
    }

    /// <summary>
    /// Displays a notification with the specified type and duration.
    /// Creates the singleton instance if it doesn't exist.
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="type">The notification type (determines color scheme)</param>
    /// <param name="duration">How long to show before fading</param>
    /// <remarks>
    /// This method is safe to call even if the Instance doesn't exist yet.
    /// It will create a new GameObject with the NotificationOverlay component.
    /// </remarks>
    public static void Show(string message, NotificationType type, float duration = DefaultDuration)
    {
        // Check if notifications are disabled in settings
        if (Configuration.Settings.ShowNotifications?.Value == false)
        {
            // Notifications disabled - silently skip display
            return;
        }

        // Auto-create instance if needed
        if (Instance == null)
        {
            var go = new GameObject("KeepStartingGear_NotificationOverlay");
            Instance = go.AddComponent<NotificationOverlay>();
        }

        Instance.QueueNotification(message, type, duration);
    }

    // ========================================================================
    // Internal Notification Management
    // ========================================================================

    /// <summary>
    /// Adds a notification to the queue for display.
    /// If no notification is currently showing, it displays immediately.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="type">The notification type</param>
    /// <param name="duration">Display duration before fade</param>
    private void QueueNotification(string message, NotificationType type, float duration)
    {
        var notification = new Notification
        {
            Message = message,
            Type = type,
            StartTime = Time.time,
            Duration = duration,
            FadeOutDuration = DefaultFadeOutDuration
        };

        // REL-003: Thread-safe queue access
        lock (_queueLock)
        {
            // If nothing is currently displaying, show immediately
            if (_currentNotification == null)
            {
                _currentNotification = notification;
            }
            else
            {
                // Otherwise queue for later display
                _notificationQueue.Enqueue(notification);
            }
        }
    }

    // ========================================================================
    // GUI Rendering
    // ========================================================================

    /// <summary>
    /// Initializes GUI styles for rendering notifications.
    /// Must be called during OnGUI as GUI.skin is only valid then.
    /// </summary>
    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        // Create style for the notification box background
        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.alignment = TextAnchor.MiddleCenter;
        // Create and cache the texture so it can be destroyed on cleanup
        _backgroundTexture = MakeTexture(2, 2, Color.white);
        _boxStyle.normal.background = _backgroundTexture;
        _boxStyle.border = new RectOffset(1, 1, 1, 1);
        _boxStyle.padding = new RectOffset(10, 10, 10, 10);

        // Create style for the notification text
        _textStyle = new GUIStyle(GUI.skin.label);
        _textStyle.alignment = TextAnchor.MiddleCenter;
        _textStyle.fontSize = FontSize;
        _textStyle.fontStyle = FontStyle.Bold;
        _textStyle.wordWrap = true;

        _stylesInitialized = true;
    }

    /// <summary>
    /// Unity's immediate-mode GUI callback. Renders the current notification.
    /// Called multiple times per frame by Unity's GUI system.
    /// </summary>
    /// <remarks>
    /// OnGUI is used instead of Unity's newer UI systems because:
    /// 1. It works reliably across all game states
    /// 2. No setup required (Canvas, EventSystem, etc.)
    /// 3. Renders on top of everything else
    /// 4. Simple and self-contained
    /// </remarks>
    private void OnGUI()
    {
        // Nothing to display
        if (_currentNotification == null) return;

        // Initialize styles if needed (must happen during OnGUI)
        InitializeStyles();

        // Calculate elapsed time and opacity for fade effect
        float elapsed = Time.time - _currentNotification.StartTime;
        float alpha = 1f;

        // Calculate fade-out alpha if past the main display duration
        if (elapsed > _currentNotification.Duration)
        {
            float fadeElapsed = elapsed - _currentNotification.Duration;
            alpha = 1f - (fadeElapsed / _currentNotification.FadeOutDuration);
            alpha = Mathf.Clamp01(alpha); // Ensure alpha stays in 0-1 range
        }

        // Get colors based on notification type
        Color bgColor, textColor;
        switch (_currentNotification.Type)
        {
            case NotificationType.Success:
                bgColor = SuccessColor;
                textColor = SuccessTextColor;
                break;
            case NotificationType.Warning:
                bgColor = WarningColor;
                textColor = WarningTextColor;
                break;
            case NotificationType.Error:
                bgColor = ErrorColor;
                textColor = ErrorTextColor;
                break;
            case NotificationType.Info:
                bgColor = InfoColor;
                textColor = InfoTextColor;
                break;
            default:
                bgColor = SuccessColor;
                textColor = SuccessTextColor;
                break;
        }

        // Apply alpha for fade effect
        bgColor.a *= alpha;
        textColor.a *= alpha;

        // Calculate position: centered horizontally, slightly above vertical center
        // The 100-pixel offset above center keeps it visible but not obstructing
        float x = (Screen.width - BoxWidth) / 2f;
        float y = (Screen.height - BoxHeight) / 2f - 100f;

        Rect boxRect = new Rect(x, y, BoxWidth, BoxHeight);

        // Draw a dark border around the notification for better visibility
        Color borderColor = new Color(0, 0, 0, 0.8f * alpha);
        DrawRect(new Rect(x - 2, y - 2, BoxWidth + 4, BoxHeight + 4), borderColor);

        // Draw the colored background
        DrawRect(boxRect, bgColor);

        // Draw the text
        _textStyle.normal.textColor = textColor;
        GUI.Label(boxRect, _currentNotification.Message, _textStyle);
    }

    // ========================================================================
    // Utility Methods
    // ========================================================================

    /// <summary>
    /// Draws a filled rectangle using GUI.DrawTexture.
    /// </summary>
    /// <param name="rect">The rectangle to fill</param>
    /// <param name="color">The fill color</param>
    private void DrawRect(Rect rect, Color color)
    {
        // Save current GUI color
        Color oldColor = GUI.color;

        // Set color and draw
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        // Restore original color
        GUI.color = oldColor;
    }

    /// <summary>
    /// Creates a solid-color texture for use in GUI rendering.
    /// </summary>
    /// <param name="width">Texture width in pixels</param>
    /// <param name="height">Texture height in pixels</param>
    /// <param name="color">The fill color</param>
    /// <returns>A new Texture2D filled with the specified color</returns>
    private static Texture2D MakeTexture(int width, int height, Color color)
    {
        // Create pixel array and fill with color
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        // Create and configure texture
        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }
}
