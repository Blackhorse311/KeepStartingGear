// ============================================================================
// GuiDrawingHelper.cs
//
// Shared IMGUI drawing utilities for overlay components.
// Extracted from NotificationOverlay, LossPreviewOverlay, SummaryOverlay,
// and ProtectionIndicator to eliminate duplication (S-1 code review fix).
//
// Author: Blackhorse311 & Claude (Anthropic)
// ============================================================================

using UnityEngine;

namespace Blackhorse311.KeepStartingGear.Utilities
{
    /// <summary>
    /// Provides shared IMGUI drawing methods used by all overlay components.
    /// </summary>
    public static class GuiDrawingHelper
    {
        /// <summary>
        /// Draws a filled rectangle using GUI.DrawTexture.
        /// Saves and restores GUI.color to avoid side effects.
        /// </summary>
        /// <param name="rect">The rectangle to fill.</param>
        /// <param name="color">The fill color.</param>
        public static void DrawRect(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        /// <summary>
        /// Draws a rectangular border (outline) of specified thickness.
        /// </summary>
        /// <param name="rect">The rectangle to outline.</param>
        /// <param name="color">The border color.</param>
        /// <param name="thickness">Border thickness in pixels.</param>
        public static void DrawRectBorder(Rect rect, Color color, int thickness)
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

        /// <summary>
        /// Draws a horizontal divider line using GUILayout.
        /// </summary>
        /// <param name="color">The line color.</param>
        public static void DrawHorizontalLine(Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            DrawRect(rect, color);
        }
    }
}
