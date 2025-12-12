// ============================================================================
// Keep Starting Gear - Configuration Settings
// ============================================================================
// This file manages all user-configurable settings for the mod using the
// BepInEx ConfigFile system. Settings are accessible via the F12 Configuration
// Manager in-game.
//
// CONFIGURATION CATEGORIES:
// 1. General - Master enable/disable switch
// 2. Snapshot Behavior - Auto/manual snapshot modes and options
// 3. Keybind - Customizable hotkey for taking manual snapshots
// 4. Inventory Slots - Choose which equipment slots to include in snapshots
// 5. Logging - Debug and diagnostic logging options
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
using UnityEngine;

namespace Blackhorse311.KeepStartingGear.Configuration;

/// <summary>
/// Predefined configuration presets for different playstyles.
/// </summary>
public enum ConfigPreset
{
    /// <summary>
    /// Custom settings - user has modified settings manually.
    /// The preset switches to this automatically when settings are changed.
    /// </summary>
    Custom,

    /// <summary>
    /// Casual preset - maximum protection, minimal hassle.
    /// Auto-snapshot at raid start, all slots protected, sound enabled.
    /// Best for players who just want their gear back on death.
    /// </summary>
    Casual,

    /// <summary>
    /// Hardcore preset - more risk, more control.
    /// Manual-only snapshots, FIR items excluded, insured items excluded.
    /// Best for players who want strategic gear protection with consequences.
    /// </summary>
    Hardcore
}

/// <summary>
/// Defines the snapshot behavior mode for the mod.
/// </summary>
public enum SnapshotMode
{
    /// <summary>
    /// Automatic snapshot at raid start only. No manual snapshots allowed.
    /// This is the default, simplest mode - your starting gear is always protected.
    /// </summary>
    AutoOnly,

    /// <summary>
    /// Automatic snapshot at raid start, plus one manual snapshot allowed per raid.
    /// Use this if you want to update your snapshot after finding good loot.
    /// </summary>
    AutoPlusManual,

    /// <summary>
    /// Manual snapshots only via keybind. No automatic snapshot at raid start.
    /// Classic mode for players who want full control over when to snapshot.
    /// </summary>
    ManualOnly
}

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

    private const string CategoryPresets = "0. Quick Setup";
    private const string CategoryGeneral = "1. General";
    private const string CategorySnapshot = "2. Snapshot Behavior";
    private const string CategoryKeybind = "3. Keybind";
    private const string CategoryInventory = "4. Inventory Slots";
    private const string CategoryLogging = "5. Logging";

    // ========================================================================
    // Preset Settings
    // Quick setup presets for different playstyles
    // ========================================================================

    #region Preset Settings

    /// <summary>
    /// Configuration preset for quick setup.
    /// Casual: Auto-snapshot, all protections off (default behavior)
    /// Hardcore: Manual-only, FIR/insured items excluded
    /// Custom: User has modified settings manually
    /// </summary>
    public static ConfigEntry<ConfigPreset> ActivePreset { get; private set; }

    /// <summary>
    /// Flag to prevent recursive preset application when settings change.
    /// </summary>
    private static bool _applyingPreset;

    #endregion

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
    // Snapshot Behavior Settings
    // Control how and when snapshots are taken
    // ========================================================================

    #region Snapshot Behavior Settings

    /// <summary>
    /// Controls when snapshots are taken.
    /// AutoOnly: Automatic at raid start, no manual allowed (default)
    /// AutoPlusManual: Automatic at start + one manual snapshot allowed
    /// ManualOnly: Only manual snapshots via keybind (classic mode)
    /// </summary>
    public static ConfigEntry<SnapshotMode> SnapshotModeOption { get; private set; }

    /// <summary>
    /// When true, items marked as Found-in-Raid will NOT be included in snapshots.
    /// This prevents exploiting the mod to duplicate FIR items.
    /// Default: false (include all items)
    /// </summary>
    public static ConfigEntry<bool> ProtectFIRItems { get; private set; }

    /// <summary>
    /// Cooldown in seconds between manual snapshots.
    /// Only applies when SnapshotMode allows manual snapshots.
    /// Set to 0 for no cooldown (only limited by one-per-raid in AutoPlusManual mode).
    /// Default: 0
    /// </summary>
    public static ConfigEntry<int> ManualSnapshotCooldown { get; private set; }

    /// <summary>
    /// When true, shows a warning when manual snapshot would replace auto-snapshot.
    /// Only applies in AutoPlusManual mode.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> WarnOnSnapshotOverwrite { get; private set; }

    /// <summary>
    /// Controls whether to take a new snapshot when transferring between maps.
    /// When true, a new snapshot is taken on each map entry.
    /// When false, the original snapshot from raid start is kept.
    /// Default: false (keep original)
    /// </summary>
    public static ConfigEntry<bool> SnapshotOnMapTransfer { get; private set; }

    /// <summary>
    /// When true, plays a camera shutter sound when a snapshot is taken.
    /// Default: true
    /// </summary>
    public static ConfigEntry<bool> PlaySnapshotSound { get; private set; }

    /// <summary>
    /// When true, items that are insured will NOT be included in snapshots.
    /// This lets insurance handle those items normally.
    /// Default: false (include all items)
    /// </summary>
    public static ConfigEntry<bool> ExcludeInsuredItems { get; private set; }

    #endregion

    // ========================================================================
    // Keybind Settings
    // Configurable hotkey for taking manual inventory snapshots
    // ========================================================================

    #region Keybind Settings

    /// <summary>
    /// The keyboard shortcut for manual snapshots.
    /// Default: Ctrl+Alt+F8
    /// Displayed as a single combined keybind in the config manager.
    /// </summary>
    public static ConfigEntry<KeyboardShortcut> SnapshotKeybind { get; private set; }

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

    /// <summary>
    /// Enable extremely verbose capture logging.
    /// Logs every item, slot, grid, and container during capture.
    /// WARNING: Creates a LOT of log output - only use for debugging.
    /// Default: false
    /// </summary>
    public static ConfigEntry<bool> VerboseCaptureLogging { get; private set; }

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
        // Preset Settings (Quick Setup)
        // ====================================================================

        ActivePreset = config.Bind(
            CategoryPresets,
            "Configuration Preset",
            ConfigPreset.Casual,
            new ConfigDescription(
                "Quick setup presets for different playstyles:\n" +
                "• Casual: Auto-snapshot, all items protected, sound enabled (recommended)\n" +
                "• Hardcore: Manual snapshots only, FIR & insured items excluded\n" +
                "• Custom: You've modified settings - preset won't auto-change",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        // Hook up preset change event
        ActivePreset.SettingChanged += OnPresetChanged;

        // ====================================================================
        // General Settings
        // ====================================================================

        ModEnabled = config.Bind(
            CategoryGeneral,
            "Enable KSG Mod",
            true,
            new ConfigDescription(
                "Master switch to enable or disable the Keep Starting Gear mod.\n" +
                "When disabled, no snapshots will be taken or restored.\n" +
                "Changes take effect at the start of your next raid.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        // ====================================================================
        // Snapshot Behavior Settings
        // ====================================================================

        order = 950; // Reset order for new category
        SnapshotModeOption = config.Bind(
            CategorySnapshot,
            "Snapshot Mode",
            SnapshotMode.AutoOnly,
            new ConfigDescription(
                "Controls when snapshots are taken:\n" +
                "• Auto Only (default): Automatic snapshot at raid start, no manual allowed\n" +
                "• Auto + Manual: Automatic at start, plus one manual snapshot per raid\n" +
                "• Manual Only: Only manual snapshots via keybind (classic mode)",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        ProtectFIRItems = config.Bind(
            CategorySnapshot,
            "Protect Found-in-Raid Items",
            false,
            new ConfigDescription(
                "When enabled, items marked as Found-in-Raid will NOT be included in snapshots.\n" +
                "This prevents exploiting the mod to duplicate FIR items.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        ManualSnapshotCooldown = config.Bind(
            CategorySnapshot,
            "Manual Snapshot Cooldown (seconds)",
            0,
            new ConfigDescription(
                "Cooldown in seconds between manual snapshots.\n" +
                "Set to 0 for no cooldown. Only applies when manual snapshots are enabled.",
                new AcceptableValueRange<int>(0, 600),
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        WarnOnSnapshotOverwrite = config.Bind(
            CategorySnapshot,
            "Warn on Snapshot Overwrite",
            true,
            new ConfigDescription(
                "Show a warning notification when manual snapshot replaces auto-snapshot.\n" +
                "Only applies in Auto + Manual mode.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        SnapshotOnMapTransfer = config.Bind(
            CategorySnapshot,
            "Re-Snapshot on Map Transfer",
            false,
            new ConfigDescription(
                "When enabled, takes a new snapshot when transferring between maps.\n" +
                "When disabled, keeps the original snapshot from raid start.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        PlaySnapshotSound = config.Bind(
            CategorySnapshot,
            "Play Snapshot Sound",
            true,
            new ConfigDescription(
                "Play a camera shutter sound when a snapshot is taken.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        ExcludeInsuredItems = config.Bind(
            CategorySnapshot,
            "Exclude Insured Items",
            false,
            new ConfigDescription(
                "When enabled, insured items will NOT be included in snapshots.\n" +
                "This lets insurance handle those items normally instead of restoring them.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

        // ====================================================================
        // Keybind Settings
        // Single combined keybind displayed as "Ctrl + Alt + F8"
        // Only used when manual snapshots are enabled
        // ====================================================================

        SnapshotKeybind = config.Bind(
            CategoryKeybind,
            "Manual Snapshot Keybind",
            new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl, KeyCode.LeftAlt),
            new ConfigDescription(
                "Keybind for taking manual snapshots (only used in Auto+Manual or Manual Only modes)",
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

        // SecuredContainer needs a custom description to explain its unique behavior
        SaveSecuredContainer = config.Bind(
            CategoryInventory,
            "Restore Secure Container to Snapshot",
            true,
            new ConfigDescription(
                "When ENABLED: On death, secure container is restored to snapshot state.\n" +
                "Items you put in during the raid (after snapshot) will be LOST.\n\n" +
                "When DISABLED: Normal Tarkov behavior - secure container contents are always kept.\n" +
                "Items you put in during the raid will be KEPT.\n\n" +
                "TIP: Disable this to keep items you find during raids in your secure container.",
                null,
                new ConfigurationManagerAttributes { Order = order-- }
            )
        );

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

        VerboseCaptureLogging = config.Bind(
            CategoryLogging,
            "Verbose Capture Logging",
            false,
            new ConfigDescription(
                "Enable extremely detailed logging during item capture (WARNING: lots of output)",
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

    // ========================================================================
    // Preset Methods
    // ========================================================================

    /// <summary>
    /// Event handler for when the preset setting changes.
    /// Applies the selected preset's settings.
    /// </summary>
    private static void OnPresetChanged(object sender, System.EventArgs e)
    {
        if (_applyingPreset) return;

        var preset = ActivePreset.Value;
        if (preset == ConfigPreset.Custom) return;

        ApplyPreset(preset);
        Plugin.Log.LogInfo($"Applied {preset} preset");
    }

    /// <summary>
    /// Applies a configuration preset, setting all related options to preset values.
    /// </summary>
    /// <param name="preset">The preset to apply</param>
    public static void ApplyPreset(ConfigPreset preset)
    {
        _applyingPreset = true;

        try
        {
            switch (preset)
            {
                case ConfigPreset.Casual:
                    ApplyCasualPreset();
                    break;
                case ConfigPreset.Hardcore:
                    ApplyHardcorePreset();
                    break;
                case ConfigPreset.Custom:
                    // Don't change anything for Custom
                    break;
            }
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    /// <summary>
    /// Applies the Casual preset - maximum protection with minimal hassle.
    /// Best for players who just want their gear back on death.
    /// </summary>
    private static void ApplyCasualPreset()
    {
        // Snapshot behavior: auto-snapshot at raid start
        SnapshotModeOption.Value = SnapshotMode.AutoOnly;
        ProtectFIRItems.Value = false;         // Save everything including FIR
        ExcludeInsuredItems.Value = false;     // Save insured items too
        SnapshotOnMapTransfer.Value = false;   // Keep original snapshot
        PlaySnapshotSound.Value = true;        // Sound enabled
        WarnOnSnapshotOverwrite.Value = true;  // Show warnings

        // All inventory slots enabled
        SaveFirstPrimaryWeapon.Value = true;
        SaveSecondPrimaryWeapon.Value = true;
        SaveHolster.Value = true;
        SaveScabbard.Value = true;
        SaveHeadwear.Value = true;
        SaveEarpiece.Value = true;
        SaveFaceCover.Value = true;
        SaveEyewear.Value = true;
        SaveArmBand.Value = true;
        SaveTacticalVest.Value = true;
        SaveArmorVest.Value = true;
        SavePockets.Value = true;
        SaveBackpack.Value = true;
        SaveSecuredContainer.Value = true;
        SaveCompass.Value = true;
        SaveSpecialSlot1.Value = true;
        SaveSpecialSlot2.Value = true;
        SaveSpecialSlot3.Value = true;
    }

    /// <summary>
    /// Applies the Hardcore preset - more risk, more control.
    /// Best for players who want strategic gear protection with consequences.
    /// </summary>
    private static void ApplyHardcorePreset()
    {
        // Snapshot behavior: manual only, must actively choose to save
        SnapshotModeOption.Value = SnapshotMode.ManualOnly;
        ProtectFIRItems.Value = true;          // Don't save FIR items (prevents exploitation)
        ExcludeInsuredItems.Value = true;      // Let insurance handle insured items
        SnapshotOnMapTransfer.Value = false;   // Keep original snapshot
        PlaySnapshotSound.Value = true;        // Sound enabled
        WarnOnSnapshotOverwrite.Value = true;  // Show warnings

        // All inventory slots still enabled (user controls via keybind timing)
        SaveFirstPrimaryWeapon.Value = true;
        SaveSecondPrimaryWeapon.Value = true;
        SaveHolster.Value = true;
        SaveScabbard.Value = true;
        SaveHeadwear.Value = true;
        SaveEarpiece.Value = true;
        SaveFaceCover.Value = true;
        SaveEyewear.Value = true;
        SaveArmBand.Value = true;
        SaveTacticalVest.Value = true;
        SaveArmorVest.Value = true;
        SavePockets.Value = true;
        SaveBackpack.Value = true;
        SaveSecuredContainer.Value = true;
        SaveCompass.Value = true;
        SaveSpecialSlot1.Value = true;
        SaveSpecialSlot2.Value = true;
        SaveSpecialSlot3.Value = true;
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
