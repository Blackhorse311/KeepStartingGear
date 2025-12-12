// ============================================================================
// Keep Starting Gear - Game Start Patch
// ============================================================================
// This patch hooks into the game start event to initialize the mod's
// components when a raid begins. It attaches the KeybindMonitor component
// to the player so they can capture inventory snapshots during the raid.
//
// HOOK POINT:
// GameWorld.OnGameStarted() - Called when a raid begins and the player
// is fully loaded into the game world.
//
// RESPONSIBILITIES:
// 1. Reset the restoration flag for the new raid
// 2. Get reference to the main player
// 3. Attach KeybindMonitor component to the player's GameObject
// 4. Initialize the KeybindMonitor with player and world references
//
// WHY THIS HOOK POINT?
// - GameWorld.OnGameStarted is reliable and fires exactly when the raid begins
// - MainPlayer is guaranteed to be valid at this point
// - Player's GameObject exists, so we can attach components to it
// - Early enough that players can take snapshots immediately
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Reflection;
using Blackhorse311.KeepStartingGear.Components;
using SPT.Reflection.Patching;
using EFT;

namespace Blackhorse311.KeepStartingGear.Patches;

/// <summary>
/// Patch to detect when a game/raid starts and attach the KeybindMonitor.
/// Hooks GameWorld.OnGameStarted to initialize mod components at raid start.
/// </summary>
/// <remarks>
/// <para>
/// This is one of the core patches for the mod. Without it, the KeybindMonitor
/// would never be created and players couldn't capture snapshots.
/// </para>
/// <para>
/// The KeybindMonitor is attached as a Unity component to the player's GameObject.
/// This allows it to use Unity's Update() method to check for keybind input each frame.
/// When the player dies or extracts, the GameObject is destroyed along with the component.
/// </para>
/// </remarks>
public class GameStartPatch : ModulePatch
{
    // ========================================================================
    // Patch Target
    // ========================================================================

    /// <summary>
    /// Specifies which method to patch - GameWorld.OnGameStarted.
    /// </summary>
    /// <returns>MethodInfo for the target method</returns>
    /// <remarks>
    /// GameWorld.OnGameStarted is a public instance method that fires when
    /// the raid has fully loaded and the player can start playing.
    /// </remarks>
    protected override MethodBase GetTargetMethod()
    {
        // Get the GameWorld.OnGameStarted method
        // This method is called when a raid begins
        return typeof(GameWorld).GetMethod(
            nameof(GameWorld.OnGameStarted),
            BindingFlags.Public | BindingFlags.Instance
        );
    }

    // ========================================================================
    // Patch Implementation
    // ========================================================================

    /// <summary>
    /// Prefix method - runs before the original GameWorld.OnGameStarted.
    /// Sets up the KeybindMonitor for the new raid.
    /// </summary>
    /// <param name="__instance">The GameWorld instance (Harmony injects this)</param>
    /// <remarks>
    /// <para>
    /// Harmony parameters:
    /// </para>
    /// <list type="bullet">
    ///   <item>__instance: The object the method was called on (GameWorld)</item>
    /// </list>
    /// <para>
    /// We use a prefix (runs before original) rather than postfix because
    /// we want the KeybindMonitor active as soon as possible, before any
    /// other raid initialization completes.
    /// </para>
    /// </remarks>
    [PatchPrefix]
    private static void PatchPrefix(GameWorld __instance)
    {
        try
        {
            Plugin.Log.LogInfo("Game started - raid has begun!");

            // Reset the restoration flag so inventory can be restored after this raid
            // This is important because restoration should only happen once per raid
            PostRaidInventoryPatch.ResetRestorationFlag();

            // Reset the raid end tracking flag so we can detect the actual player's raid end
            // This prevents bot extractions from being mistaken for player extractions
            RaidEndPatch.ResetRaidEndFlag();

            // Get the main player from the GameWorld
            // MainPlayer is the human player (not bots/scavs)
            var player = __instance.MainPlayer;

            if (player != null && player.gameObject != null)
            {
                // ================================================================
                // SCAV RAID CHECK
                // ================================================================
                // Only attach KeybindMonitor for PMC raids, not Scav raids
                // Scav raids should not have gear protection - you're playing as a disposable scav
                // EPlayerSide.Savage = Scav, EPlayerSide.Usec/Bear = PMC
                if (player.Side == EPlayerSide.Savage)
                {
                    Plugin.Log.LogInfo("Scav raid detected - KeepStartingGear is disabled for Scav runs");
                    return; // Do not attach KeybindMonitor for Scav raids
                }

                // ================================================================
                // MOD ENABLED CHECK
                // ================================================================
                // Check if the mod is enabled in F12 settings
                // This allows users to disable the mod without restarting the game
                if (!Configuration.Settings.ModEnabled.Value)
                {
                    Plugin.Log.LogInfo("KeepStartingGear is disabled in settings - not activating for this raid");
                    return;
                }

                Plugin.Log.LogInfo($"PMC raid detected (Side: {player.Side}) - enabling KeepStartingGear");

                // Attach KeybindMonitor as a Unity component to the player's GameObject
                // This allows it to receive Unity lifecycle callbacks (Update, etc.)
                var monitor = player.gameObject.AddComponent<KeybindMonitor>();

                // Initialize the monitor with references it needs
                // Player: For capturing inventory
                // GameWorld: For determining location and raid status
                monitor.Init(player, __instance);

                Plugin.Log.LogInfo("KeybindMonitor attached to player");
                Plugin.Log.LogInfo($"Snapshot keybind: {Configuration.Settings.SnapshotKeybind.Value}");
            }
            else
            {
                // This shouldn't happen, but log it for debugging
                Plugin.Log.LogWarning("Could not attach KeybindMonitor: player or gameObject is null");
            }
        }
        catch (System.Exception ex)
        {
            // Log errors but don't crash the game
            Plugin.Log.LogError($"Error in GameStartPatch: {ex.Message}");
            Plugin.Log.LogError($"Stack trace: {ex.StackTrace}");
        }
    }
}
