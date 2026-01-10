// ============================================================================
// Keep Starting Gear - Shared Constants
// ============================================================================
// Centralized constants used across all server-side components.
// This eliminates magic strings and ensures consistency.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Shared constants used throughout the server-side mod.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Current mod version. Update this for each release.
    /// Must match ModMetadata.Version and client Plugin.PluginVersion.
    /// </summary>
    public const string ModVersion = "1.4.9";

    /// <summary>
    /// Equipment container template ID - identifies the root equipment container.
    /// All equipped items are children of this container.
    /// </summary>
    public const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

    /// <summary>
    /// Mod folder name - used for both server and client mod folders.
    /// </summary>
    public const string ModFolderName = "Blackhorse311-KeepStartingGear";

    /// <summary>
    /// Log prefix for all server-side log messages.
    /// </summary>
    public const string LogPrefix = "[KeepStartingGear-Server]";

    /// <summary>
    /// Maximum depth for parent chain traversal to prevent infinite loops.
    /// </summary>
    public const int MaxParentTraversalDepth = 20;
}
