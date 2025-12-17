// ============================================================================
// Keep Starting Gear - Main Plugin Entry Point
// ============================================================================
// This is the main BepInEx plugin class that initializes the mod.
//
// OVERVIEW:
// Keep Starting Gear allows players to save snapshots of their inventory
// during a raid and restore that inventory if they die. This provides a
// safety net for players who want to protect their gear while still
// maintaining risk/reward gameplay through the one-snapshot-per-map limit.
//
// ARCHITECTURE:
// The mod uses a hybrid client-server architecture:
// - Client (BepInEx): Captures snapshots, displays notifications, tracks limits
// - Server (SPT .NET): Intercepts raid end, restores inventory from snapshot
//
// This separation ensures reliable inventory restoration even when the game
// client state may be inconsistent after death.
//
// KEY COMPONENTS:
// - Plugin.cs (this file): Entry point, initializes all services
// - Settings.cs: BepInEx configuration management
// - InventoryService.cs: Captures player inventory to snapshot format
// - SnapshotManager.cs: Saves/loads snapshot JSON files
// - KeybindMonitor.cs: Monitors for snapshot keybind, enforces per-map limit
// - NotificationOverlay.cs: Displays large centered colored notifications
// - RaidEndInterceptor.cs (server): Restores inventory on death
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using BepInEx;
using BepInEx.Logging;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Patches;
using UnityEngine;

namespace Blackhorse311.KeepStartingGear;

/// <summary>
/// Main plugin class for the Keep Starting Gear mod.
/// Inherits from BaseUnityPlugin to integrate with BepInEx mod loading system.
/// </summary>
/// <remarks>
/// <para>
/// <b>How this mod works:</b>
/// </para>
/// <list type="number">
///   <item>Player presses configurable keybind (default: Ctrl+Alt+F8) to save inventory snapshot</item>
///   <item>Snapshots are unlimited in hideout, but limited to one per map during raids</item>
///   <item>When player dies, the server-side component restores inventory from snapshot</item>
///   <item>Items picked up AFTER the snapshot are intentionally lost (snapshot-only restoration)</item>
///   <item>On successful extraction, the snapshot is cleared automatically</item>
/// </list>
/// <para>
/// <b>Snapshot Storage:</b>
/// Snapshots are stored as JSON files in: BepInEx/plugins/Blackhorse311-KeepStartingGear/snapshots/
/// Each snapshot is named by session ID and contains all equipped items with their properties.
/// </para>
/// </remarks>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.custom", "4.0.0")]
public class Plugin : BaseUnityPlugin
{
    // ========================================================================
    // Plugin Metadata Constants
    // These values are used by BepInEx for mod identification and loading
    // ========================================================================

    /// <summary>
    /// Unique identifier for this plugin. Must match the server mod's GUID.
    /// Format follows reverse domain notation: com.author.modname
    /// </summary>
    public const string PluginGuid = "com.blackhorse311.keepstartinggear";

    /// <summary>
    /// Human-readable name displayed in BepInEx plugin list
    /// </summary>
    public const string PluginName = "Blackhorse311-KeepStartingGear";

    /// <summary>
    /// Semantic version (MAJOR.MINOR.PATCH) - must match server mod version
    /// </summary>
    public const string PluginVersion = "1.4.6";

    /// <summary>
    /// SPT/EFT build version this mod was tested against
    /// </summary>
    public const int TarkovVersion = 40088;

    // ========================================================================
    // Static Accessors
    // These allow other classes to access the plugin instance and logger
    // ========================================================================

    /// <summary>
    /// Singleton instance of the plugin for global access from patches and services.
    /// Set during Awake() and persists for the lifetime of the game session.
    /// </summary>
    public static Plugin Instance { get; private set; }

    /// <summary>
    /// BepInEx logger instance for outputting messages to the console and log file.
    /// Use this instead of Debug.Log for proper BepInEx integration.
    /// </summary>
    public static ManualLogSource Log { get; private set; }

    // ========================================================================
    // Unity Lifecycle Methods
    // ========================================================================

    /// <summary>
    /// BepInEx entry point - called when the mod is first loaded.
    /// Initializes all services, configuration, and patches.
    /// </summary>
    /// <remarks>
    /// This method runs once when the game starts and the mod is loaded.
    /// Order of initialization is important:
    /// 1. Set up static references (Instance, Log)
    /// 2. Initialize configuration system
    /// 3. Create service instances (SnapshotManager, InventoryService, ProfileService)
    /// 4. Enable Harmony patches to hook into game events
    /// </remarks>
    internal void Awake()
    {
        try
        {
            // Store static reference for global access
            Instance = this;
            Log = Logger;

            // Prevent this GameObject from being destroyed when loading new scenes
            // This ensures the mod persists across hideout/raid transitions
            DontDestroyOnLoad(this);

            // Initialize the BepInEx configuration system
            // This creates config entries that appear in the F12 Configuration Manager
            Settings.Init(Config);

            // Check if the mod is enabled in configuration
            // Allows users to disable the mod without uninstalling it
            if (!Settings.ModEnabled.Value)
            {
                Logger.LogInfo("Keep Starting Gear is disabled in configuration");
                return;
            }

            // Initialize services in dependency order
            new Services.SnapshotManager();
            new Services.InventoryService();
            new Services.ProfileService();

            // Enable Harmony patches to hook into game events
            PatchManager.EnablePatches();

            Logger.LogInfo($"Keep Starting Gear v{PluginVersion} loaded");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            // Critical errors here will prevent the mod from functioning
            Logger.LogError($"CRITICAL ERROR during plugin load: {ex.Message}");
            Logger.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    // ========================================================================
    // Utility Methods
    // ========================================================================

    /// <summary>
    /// Gets the file system path where snapshot JSON files are stored.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <returns>
    /// Full path to the snapshots directory:
    /// {BepInEx}/plugins/Blackhorse311-KeepStartingGear/snapshots/
    /// </returns>
    /// <remarks>
    /// Snapshots are stored as individual JSON files named by session ID.
    /// The server-side component reads from this same directory to restore inventory.
    /// </remarks>
    public static string GetDataPath()
    {
        return System.IO.Path.Combine(
            BepInEx.Paths.PluginPath,
            "Blackhorse311-KeepStartingGear",
            "snapshots"
        );
    }
}
