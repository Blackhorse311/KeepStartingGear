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
    public const string ModVersion = "2.0.7";

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

    /// <summary>
    /// MEDIUM-002 FIX: Shared constant for SecuredContainer slot name.
    /// Used by both SnapshotRestorer and CustomInRaidHelper for consistent slot identification.
    /// The SecuredContainer is always preserved on death in normal Tarkov behavior.
    /// </summary>
    public const string SecuredContainerSlot = "SecuredContainer";

    /// <summary>
    /// Pockets slot name. Like SecuredContainer, the Pockets item is a permanent
    /// fixture of the player's equipment and must NEVER be deleted during restoration.
    /// Deleting the Pockets item permanently corrupts the player's profile.
    /// </summary>
    public const string PocketsSlot = "Pockets";

    /// <summary>
    /// Scabbard slot name. In normal Tarkov, melee weapons are never lost on death.
    /// The Scabbard item should always be preserved regardless of slot management settings.
    /// </summary>
    public const string ScabbardSlot = "Scabbard";
}
