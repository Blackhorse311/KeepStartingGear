// ============================================================================
// Keep Starting Gear - Equipment Slot Constants
// ============================================================================
// Centralized definition of all equipment slot names used throughout the mod.
// These slot names must match EFT's internal slot identifiers.
//
// IMPORTANT: If BSG adds new slots, update this file and all code will
// automatically use the new slots.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;

namespace Blackhorse311.KeepStartingGear.Constants;

/// <summary>
/// Centralized constants for EFT equipment slot names.
/// All slot-related code should reference these constants instead of hardcoding strings.
/// </summary>
public static class EquipmentSlots
{
    // ========================================================================
    // Weapon Slots
    // ========================================================================

    /// <summary>First primary weapon slot (rifles, SMGs, shotguns)</summary>
    public const string FirstPrimaryWeapon = "FirstPrimaryWeapon";

    /// <summary>Second primary weapon slot (on-sling weapon)</summary>
    public const string SecondPrimaryWeapon = "SecondPrimaryWeapon";

    /// <summary>Pistol/sidearm slot</summary>
    public const string Holster = "Holster";

    /// <summary>Melee weapon slot</summary>
    public const string Scabbard = "Scabbard";

    // ========================================================================
    // Armor & Protection Slots
    // ========================================================================

    /// <summary>Helmet slot</summary>
    public const string Headwear = "Headwear";

    /// <summary>Headset/comtacs slot</summary>
    public const string Earpiece = "Earpiece";

    /// <summary>Face covering (mask, balaclava)</summary>
    public const string FaceCover = "FaceCover";

    /// <summary>Body armor slot</summary>
    public const string ArmorVest = "ArmorVest";

    /// <summary>Glasses/goggles slot</summary>
    public const string Eyewear = "Eyewear";

    /// <summary>Armband slot (team identification)</summary>
    public const string ArmBand = "ArmBand";

    // ========================================================================
    // Container Slots
    // ========================================================================

    /// <summary>Tactical vest/chest rig slot</summary>
    public const string TacticalVest = "TacticalVest";

    /// <summary>Backpack slot</summary>
    public const string Backpack = "Backpack";

    /// <summary>Secure container (gamma, epsilon, etc.)</summary>
    public const string SecuredContainer = "SecuredContainer";

    /// <summary>Pocket slots</summary>
    public const string Pockets = "Pockets";

    // ========================================================================
    // Slot Collections
    // ========================================================================

    /// <summary>
    /// All weapon-related slots.
    /// </summary>
    public static readonly string[] WeaponSlots =
    {
        FirstPrimaryWeapon,
        SecondPrimaryWeapon,
        Holster,
        Scabbard
    };

    /// <summary>
    /// All armor and protection slots.
    /// </summary>
    public static readonly string[] ArmorSlots =
    {
        Headwear,
        Earpiece,
        FaceCover,
        ArmorVest,
        Eyewear,
        ArmBand
    };

    /// <summary>
    /// All container slots (items that can hold other items).
    /// </summary>
    public static readonly string[] ContainerSlots =
    {
        TacticalVest,
        Backpack,
        SecuredContainer,
        Pockets
    };

    /// <summary>
    /// All equipment slots that the mod can manage.
    /// This is the canonical list used for iteration throughout the codebase.
    /// </summary>
    public static readonly string[] AllSlots =
    {
        // Weapons
        FirstPrimaryWeapon,
        SecondPrimaryWeapon,
        Holster,
        Scabbard,
        // Armor
        Headwear,
        Earpiece,
        FaceCover,
        ArmorVest,
        Eyewear,
        ArmBand,
        // Containers
        TacticalVest,
        Backpack,
        SecuredContainer,
        Pockets
    };

    /// <summary>
    /// Validates if a slot name is a known equipment slot.
    /// </summary>
    /// <param name="slotName">The slot name to validate</param>
    /// <returns>True if the slot name is recognized, false otherwise</returns>
    public static bool IsValidSlot(string slotName)
    {
        return Array.Exists(AllSlots, s => s.Equals(slotName, StringComparison.OrdinalIgnoreCase));
    }
}
