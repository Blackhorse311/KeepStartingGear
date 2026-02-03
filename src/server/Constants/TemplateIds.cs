// ============================================================================
// Keep Starting Gear - Template ID Constants
// ============================================================================
// M-01 FIX: Centralize magic template IDs to avoid duplication and typos.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

namespace Blackhorse311.KeepStartingGear.Constants;

/// <summary>
/// EFT Template IDs used throughout the mod.
/// These are MongoDB ObjectId strings that identify item types in EFT.
/// </summary>
public static class TemplateIds
{
    /// <summary>
    /// The Equipment container template ID.
    /// This is the root container for all player equipment slots.
    /// It's a special item that holds all equipment (weapons, armor, backpack, etc.).
    /// </summary>
    public const string Equipment = "55d7217a4bdc2d86028b456d";

    /// <summary>
    /// M-11 FIX: Maximum numeric slot ID for magazine/container slots.
    /// Magazine ammo slots and similar containers use numeric IDs (0, 1, 2, etc.).
    /// This limit is based on EFT's practical maximum - no magazine has 100+ slots.
    /// </summary>
    public const int MaxNumericSlotId = 100;
}
