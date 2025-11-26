// ============================================================================
// Keep Starting Gear - Configuration Settings
// ============================================================================
// This file manages all user-configurable settings for the mod using the
// BepInEx ConfigFile system. Settings are accessible via the F12 Configuration
// Manager in-game.
//
// CONFIGURATION CATEGORIES:
// 1. General - Master enable/disable switch
// 2. Keybind - Customizable hotkey for taking snapshots
// 3. Inventory Slots - Choose which equipment slots to include in snapshots
// 4. Logging - Debug and diagnostic logging options
//
// USAGE:
// All settings are automatically saved to:
// BepInEx/config/com.blackhorse311.keepstartinggear.cfg
//
// Players can modify settings either by editing the .cfg file directly
// or using the in-game Configuration Manager (F12 by default).
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using BepInEx.Configuration;
using System.Collections.Generic;

namespace Blackhorse311.KeepStartingGear.Configuration;

/// <summary>
/// Static class that manages all mod configuration settings.
/// Uses BepInEx's ConfigFile system for persistent storage and
/// integration with the Configuration Manager UI.
/// </summary>
/// <remarks>
/// <para>
/// Settings are organized into categories that appear as collapsible
/// sections in the Configuration Manager. The 'Order' attribute controls
/// the display order within each category (higher = appears first).
/// </para>
/// <para>
/// All inventory slot settings default to true (save everything), allowing
/// users to opt-out of specific slots rather than opt-in.
/// </para>
/// </remarks>
public static class Settings
{
    // ========================================================================
    // Category Names
    // These define the section headers in the Configuration Manager UI.
    // Numbered prefixes ensure consistent ordering.
    // ========================================================================

    private const string CategoryGeneral = "1. General";
    private const string CategoryKeybind = "2. Keybind";
    private const string CategoryInventory = "3. Inventory Slots";
    private const string CategoryLogging = "4. Logging";

    // ========================================================================
    // General Settings
    // Core mod functionality toggles
    // ========================================================================

    #region General Settings

    /// <summary>
    /// Master enable/disable switch for the entire mod.
    /// When false, no patches are loaded and the mod is effectively disabled.
    /// </summary>
    /// <remarks>
    /// Useful for temporarily disabling the mod without uninstalling it.
    /// Requires game restart to take effect.
    /// </remarks>
    public static ConfigEntry<bool> ModEnabled { get; private set; }

    #endregion

    // ========================================================================
    // Keybind Settings
    // Configurable hotkey for taking inventory snapshots
    // ========================================================================

    #region Keybind Settings

    /// <summary>
    /// Primary key for the snapshot keybind.
    /// Default: F8
    /// </summary>
    /// <remarks>
    /// Can be combined with modifier keys (Ctrl, Alt, Shift) to create
    /// complex keybinds like Ctrl+Alt+F8.
    /// </remarks>
    public static ConfigEntry<UnityEngine.KeyCode> SnapshotKey { get; private set; }

    /// <summary>
    /// When true, Ctrl key must be held when pressing the snapshot key.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> RequireCtrl { get; private set; }

    /// <summary>
    /// When true, Alt key must be held when pressing the snapshot key.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> RequireAlt { get; private set; }

    /// <summary>
    /// When true, Shift key must be held when pressing the snapshot key.
    /// Default: false
    /// </summary>
    public static ConfigEntry<bool> RequireShift { get; private set; }

    #endregion

    // ========================================================================
    // Inventory Slot Settings
    // Control which equipment slots are included in snapshots
    // ========================================================================

    #region Inventory Slot Settings

    // --- Weapon Slots ---
    // These slots hold the player's weapons

    /// <summary>First primary weapon slot (typically rifles, SMGs)</summary>
    public static ConfigEntry<bool> SaveFirstPrimaryWeapon { get; private set; }

    /// <summary>Second primary weapon slot (backup weapon)</summary>
    public static ConfigEntry<bool> SaveSecondPrimaryWeapon { get; private set; }

    /// <summary>Holster slot (pistols, secondary weapons)</summary>
    public static ConfigEntry<bool> SaveHolster { get; private set; }

    /// <summary>Scabbard slot (melee weapons)</summary>
    public static ConfigEntry<bool> SaveScabbard { get; private set; }

    // --- Gear Slots ---
    // These slots hold worn equipment and accessories

    /// <summary>Headwear slot (helmets, hats)</summary>
    public static ConfigEntry<bool> SaveHeadwear { get; private set; }

    /// <summary>Earpiece slot (headsets, comtacs)</summary>
    public static ConfigEntry<bool> SaveEarpiece { get; private set; }

    /// <summary>Face cover slot (masks, balaclavas)</summary>
    public static ConfigEntry<bool> SaveFaceCover { get; private set; }

    /// <summary>Eyewear slot (glasses, goggles)</summary>
    public static ConfigEntry<bool> SaveEyewear { get; private set; }

    /// <summary>Arm band slot (identification bands)</summary>
    public static ConfigEntry<bool> SaveArmBand { get; private set; }

    // --- Armor Slots ---
    // These slots hold protective equipment

    /// <summary>Tactical vest slot (chest rigs, plate carriers)</summary>
    public static ConfigEntry<bool> SaveTacticalVest { get; private set; }

    /// <summary>Armor vest slot (body armor)</summary>
    public static ConfigEntry<bool> SaveArmorVest { get; private set; }

    // --- Container Slots ---
    // These slots hold items with storage capacity

    /// <summary>Pockets slot (built-in pocket storage)</summary>
    public static ConfigEntry<bool> SavePockets { get; private set; }

    /// <summary>Backpack slot (carries large amounts of loot)</summary>
    public static ConfigEntry<bool> SaveBackpack { get; private set; }

    /// <summary>Secured container slot (keeps items on death - usually excluded)</summary>
    public static ConfigEntry<bool> SaveSecuredContainer { get; private set; }

    // --- Special Slots ---
    // Miscellaneous equipment slots

    /// <summary>Compass slot</summary>
    public static ConfigEntry<bool> SaveCompass { get; private set; }

    /// <summary>Special slot 1 (injectors, stims)</summary>
    public static ConfigEntry<bool> SaveSpecialSlot1 { get; private set; }

    /// <summary>Special slot 2</summary>
    public static ConfigEntry<bool> SaveSpecialSlot2 { get; private set; }

    /// <summary>Special slot 3</summary>
    public static ConfigEntry<bool> SaveSpecialSlot3 { get; private set; }

    #endregion

    // ========================================================================
    // Logging Settings
    // Debug and diagnostic options
    // ========================================================================

    #region Logging Settings

    /// <summary>
    /// Enable verbose debug logging.
    /// When true, outputs detailed information about mod operations.
    /// Default: false
    /// </summary>
    /// <remarks>
    /// Useful for troubleshooting issues. Debug logs appear in
    /// BepInEx/LogOutput.log with [Debug] prefix.
    /// </remarks>
    public static ConfigEntry<bool> EnableDebugMode { get; private set; }

    /// <summary>
    /// Log when inventory snapshots are created.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> LogSnapshotCreation { get; private set; }

    /// <summary>
    /// Log when inventory snapshots are restored.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> LogSnapshotRestoration { get; private set; }

    #endregion

    // ========================================================================
    // Initialization
    // ========================================================================

    /// <summary>
    /// Initializes all configuration entries and binds them to the config file.
    /// Called once during plugin startup.
    /// </summary>
    /// <param name="config">The BepInEx ConfigFile instance from the plugin</param>
    /// <remarks>
    /// The 'order' variable controls display order in Configuration Manager.
    /// Higher values appear first. We use decreasing values to maintain
    /// logical ordering within each category.
    /// </remarks>
    public static void Init(ConfigFile config)
    {
        // Order value for controlling display order in Configuration Manager
        // Higher values appear first in the list
        int order = 1000;

        // ====================================================================
        // General Settings
        // ====================================================================

        ModEnabled = config.Bind(
            CategoryGeneral,
            "Enabled",
            true,
            new ConfigDescription(
                "Enable or disable the entire mod",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        // ====================================================================
        // Keybind Settings
        // Default keybind is Ctrl+Alt+F8 (all three keys must be pressed)
        // ====================================================================

        SnapshotKey = config.Bind(
            CategoryKeybind,
            "Snapshot Key",
            UnityEngine.KeyCode.F8,
            new ConfigDescription(
                "Primary key to take inventory snapshot",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        RequireCtrl = config.Bind(
            CategoryKeybind,
            "Require Ctrl",
            true,
            new ConfigDescription(
                "Require Ctrl key to be held with snapshot key",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        RequireAlt = config.Bind(
            CategoryKeybind,
            "Require Alt",
            true,
            new ConfigDescription(
                "Require Alt key to be held with snapshot key",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        RequireShift = config.Bind(
            CategoryKeybind,
            "Require Shift",
            false,
            new ConfigDescription(
                "Require Shift key to be held with snapshot key",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        // ====================================================================
        // Inventory Slot Settings
        // All slots default to true (include in snapshot)
        // Players can disable specific slots they don't want saved
        // ====================================================================

        order = 900; // Reset order for new category
        SaveFirstPrimaryWeapon = BindInventorySlot(config, "First Primary Weapon", ref order);
        SaveSecondPrimaryWeapon = BindInventorySlot(config, "Second Primary Weapon", ref order);
        SaveHolster = BindInventorySlot(config, "Holster", ref order);
        SaveScabbard = BindInventorySlot(config, "Scabbard", ref order);
        SaveHeadwear = BindInventorySlot(config, "Headwear", ref order);
        SaveEarpiece = BindInventorySlot(config, "Earpiece", ref order);
        SaveFaceCover = BindInventorySlot(config, "Face Cover", ref order);
        SaveEyewear = BindInventorySlot(config, "Eyewear", ref order);
        SaveArmBand = BindInventorySlot(config, "Arm Band", ref order);
        SaveTacticalVest = BindInventorySlot(config, "Tactical Vest", ref order);
        SaveArmorVest = BindInventorySlot(config, "Armor Vest", ref order);
        SavePockets = BindInventorySlot(config, "Pockets", ref order);
        SaveBackpack = BindInventorySlot(config, "Backpack", ref order);
        SaveSecuredContainer = BindInventorySlot(config, "Secured Container", ref order);
        SaveCompass = BindInventorySlot(config, "Compass", ref order);
        SaveSpecialSlot1 = BindInventorySlot(config, "Special Slot 1", ref order);
        SaveSpecialSlot2 = BindInventorySlot(config, "Special Slot 2", ref order);
        SaveSpecialSlot3 = BindInventorySlot(config, "Special Slot 3", ref order);

        // ====================================================================
        // Logging Settings
        // ====================================================================

        order = 100; // Reset order for new category
        EnableDebugMode = config.Bind(
            CategoryLogging,
            "Debug Mode",
            false,
            new ConfigDescription(
                "Enable verbose debug logging",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        LogSnapshotCreation = config.Bind(
            CategoryLogging,
            "Log Snapshot Creation",
            true,
            new ConfigDescription(
                "Log when inventory snapshots are created",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        LogSnapshotRestoration = config.Bind(
            CategoryLogging,
            "Log Snapshot Restoration",
            true,
            new ConfigDescription(
                "Log when inventory snapshots are restored",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        Plugin.Log.LogInfo("Settings initialized with BepInEx ConfigFile");
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    /// <summary>
    /// Helper method to create inventory slot configuration entries.
    /// Reduces code duplication for the many similar slot settings.
    /// </summary>
    /// <param name="config">The BepInEx ConfigFile instance</param>
    /// <param name="slotName">Human-readable name of the slot</param>
    /// <param name="order">Reference to order counter (decremented after use)</param>
    /// <returns>The created ConfigEntry for this slot</returns>
    private static ConfigEntry<bool> BindInventorySlot(ConfigFile config, string slotName, ref int order)
    {
        return config.Bind(
            CategoryInventory,
            slotName,
            true, // Default: save everything - users opt-out of specific slots
            new ConfigDescription(
                $"Save {slotName} in snapshot",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );
    }

    /// <summary>
    /// Gets a dictionary mapping slot names to their configuration entries.
    /// Useful for iterating over all inventory slot settings.
    /// </summary>
    /// <returns>Dictionary with slot name keys and ConfigEntry values</returns>
    /// <remarks>
    /// The keys in this dictionary match the EquipmentSlot enum names in EFT,
    /// making it easy to check if a specific slot should be included in snapshots.
    /// </remarks>
    public static Dictionary<string, ConfigEntry<bool>> GetInventorySlots()
    {
        return new Dictionary<string, ConfigEntry<bool>>
        {
            { "FirstPrimaryWeapon", SaveFirstPrimaryWeapon },
            { "SecondPrimaryWeapon", SaveSecondPrimaryWeapon },
            { "Holster", SaveHolster },
            { "Scabbard", SaveScabbard },
            { "Headwear", SaveHeadwear },
            { "Earpiece", SaveEarpiece },
            { "FaceCover", SaveFaceCover },
            { "Eyewear", SaveEyewear },
            { "ArmBand", SaveArmBand },
            { "TacticalVest", SaveTacticalVest },
            { "ArmorVest", SaveArmorVest },
            { "Pockets", SavePockets },
            { "Backpack", SaveBackpack },
            { "SecuredContainer", SaveSecuredContainer },
            { "Compass", SaveCompass },
            { "SpecialSlot1", SaveSpecialSlot1 },
            { "SpecialSlot2", SaveSpecialSlot2 },
            { "SpecialSlot3", SaveSpecialSlot3 }
        };
    }
}

/// <summary>
/// Custom attributes for BepInEx Configuration Manager integration.
/// These attributes control how settings appear in the F12 Configuration Manager UI.
/// </summary>
/// <remarks>
/// This is a simplified version of the full ConfigurationManagerAttributes class.
/// Only the properties we need are included.
/// </remarks>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>
    /// Controls the display order within a category.
    /// Higher values appear first in the list.
    /// </summary>
    public int? Order { get; set; }

    /// <summary>
    /// When true, setting is hidden unless "Show Advanced" is enabled.
    /// </summary>
    public bool? IsAdvanced { get; set; }

    /// <summary>
    /// When false, setting is completely hidden from the UI.
    /// </summary>
    public bool? Browsable { get; set; }

    /// <summary>
    /// When true, setting cannot be modified in the UI.
    /// </summary>
    public bool? ReadOnly { get; set; }
}
