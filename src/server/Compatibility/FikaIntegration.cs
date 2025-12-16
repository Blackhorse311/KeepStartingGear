// Keep Starting Gear - FIKA Integration
//
// This class provides integration with FIKA's multiplayer mod by hooking into
// the client-side death handling BEFORE FIKA serializes the inventory.
//
// FIKA Flow (without our mod):
// 1. Player dies -> HealthController_DiedEvent fires
// 2. CoopGame.HealthController_DiedEvent() handles death
// 3. SavePlayer() is called which serializes current (dead) inventory
// 4. Inventory is sent to server via LocalRaidEnded()
//
// With our integration:
// 1. Player dies -> HealthController_DiedEvent fires
// 2. [OUR PATCH] Intercept and restore inventory from snapshot FIRST
// 3. CoopGame.HealthController_DiedEvent() handles death with restored inventory
// 4. SavePlayer() serializes the RESTORED inventory
// 5. Server receives the restored inventory - no server-side patching needed!

using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using EFT;

namespace Blackhorse311.KeepStartingGear.Compatibility
{
    /// <summary>
    /// Manages FIKA integration by applying Harmony patches to FIKA's death handling.
    /// Uses reflection to avoid hard dependency on FIKA.
    /// </summary>
    public static class FikaIntegration
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("KSG-FikaIntegration");

        /// <summary>
        /// Whether FIKA integration has been successfully initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// The Harmony instance used for FIKA patches.
        /// </summary>
        private static Harmony _harmony;

        /// <summary>
        /// Callback to restore inventory from snapshot.
        /// Set by the main plugin during initialization.
        /// </summary>
        public static Action<Player> OnPlayerDeathBeforeFika { get; set; }

        /// <summary>
        /// Initializes FIKA integration if FIKA is detected.
        /// Call this during plugin Awake/Start.
        /// </summary>
        /// <param name="harmonyId">Harmony ID to use for patches</param>
        /// <returns>True if FIKA integration was initialized, false otherwise</returns>
        public static bool Initialize(string harmonyId)
        {
            if (IsInitialized)
            {
                Logger.LogWarning("FIKA integration already initialized.");
                return true;
            }

            if (!FikaDetector.IsFikaInstalled)
            {
                Logger.LogInfo("FIKA not detected - skipping FIKA integration.");
                return false;
            }

            Logger.LogInfo("FIKA detected! Initializing FIKA integration...");
            FikaDetector.LogFikaInfo();

            try
            {
                _harmony = new Harmony(harmonyId + ".fika");

                // Try to patch FIKA's death handling
                bool patched = TryPatchFikaDeathHandler();

                if (patched)
                {
                    IsInitialized = true;
                    Logger.LogInfo("FIKA integration initialized successfully!");
                    return true;
                }
                else
                {
                    Logger.LogWarning("Failed to patch FIKA death handler. FIKA integration disabled.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initializing FIKA integration: {ex.Message}");
                Logger.LogError(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Attempts to patch FIKA's HealthController_DiedEvent method.
        /// </summary>
        private static bool TryPatchFikaDeathHandler()
        {
            var coopGameType = FikaDetector.CoopGameType;
            if (coopGameType == null)
            {
                Logger.LogError("CoopGame type not found - cannot patch FIKA.");
                return false;
            }

            // Find the HealthController_DiedEvent method
            // It's a private async void method: private async void HealthController_DiedEvent(EDamageType damageType)
            var deathMethod = coopGameType.GetMethod(
                "HealthController_DiedEvent",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(EDamageType) },
                null
            );

            if (deathMethod == null)
            {
                Logger.LogWarning("HealthController_DiedEvent method not found. Trying alternative search...");

                // Try to find it by searching all methods
                var methods = coopGameType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (method.Name.Contains("DiedEvent") || method.Name.Contains("Death"))
                    {
                        Logger.LogDebug($"Found potential death method: {method.Name}");
                    }
                }

                return false;
            }

            Logger.LogInfo($"Found HealthController_DiedEvent: {deathMethod}");

            // Create and apply the prefix patch
            var prefixMethod = typeof(FikaIntegration).GetMethod(
                nameof(HealthController_DiedEvent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            if (prefixMethod == null)
            {
                Logger.LogError("Could not find prefix method!");
                return false;
            }

            try
            {
                _harmony.Patch(deathMethod, prefix: new HarmonyMethod(prefixMethod));
                Logger.LogInfo("Successfully patched FIKA's HealthController_DiedEvent!");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply Harmony patch: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Harmony prefix patch for FIKA's HealthController_DiedEvent.
        /// This runs BEFORE FIKA handles the death, allowing us to restore inventory first.
        /// </summary>
        /// <param name="__instance">The CoopGame instance</param>
        /// <param name="damageType">How the player died</param>
        private static void HealthController_DiedEvent_Prefix(object __instance, EDamageType damageType)
        {
            try
            {
                Logger.LogInfo($"[FIKA Integration] Player death intercepted! Damage type: {damageType}");

                // Get the player from the CoopGame instance
                // CoopGame has gparam_0.Player which is the local player
                var player = GetPlayerFromCoopGame(__instance);

                if (player == null)
                {
                    Logger.LogWarning("[FIKA Integration] Could not get player from CoopGame instance.");
                    return;
                }

                Logger.LogInfo($"[FIKA Integration] Player: {player.Profile?.Nickname ?? "Unknown"}");

                // Call our snapshot restoration callback
                if (OnPlayerDeathBeforeFika != null)
                {
                    Logger.LogInfo("[FIKA Integration] Invoking snapshot restoration callback...");
                    OnPlayerDeathBeforeFika.Invoke(player);
                    Logger.LogInfo("[FIKA Integration] Snapshot restoration callback completed.");
                }
                else
                {
                    Logger.LogWarning("[FIKA Integration] No death callback registered!");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA Integration] Error in death prefix: {ex.Message}");
                Logger.LogError(ex.StackTrace);
            }
        }

        /// <summary>
        /// Extracts the Player from a CoopGame instance using reflection.
        /// </summary>
        private static Player GetPlayerFromCoopGame(object coopGame)
        {
            if (coopGame == null)
                return null;

            try
            {
                var coopGameType = coopGame.GetType();

                // Try to get gparam_0 field (inherited from BaseLocalGame)
                var gparamField = coopGameType.GetField("gparam_0",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (gparamField == null)
                {
                    // Try base class
                    var baseType = coopGameType.BaseType;
                    while (baseType != null && gparamField == null)
                    {
                        gparamField = baseType.GetField("gparam_0",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        baseType = baseType.BaseType;
                    }
                }

                if (gparamField == null)
                {
                    Logger.LogWarning("Could not find gparam_0 field");
                    return null;
                }

                var gparam = gparamField.GetValue(coopGame);
                if (gparam == null)
                {
                    Logger.LogWarning("gparam_0 is null");
                    return null;
                }

                // gparam_0 has a Player property
                var playerProp = gparam.GetType().GetProperty("Player",
                    BindingFlags.Public | BindingFlags.Instance);

                if (playerProp == null)
                {
                    Logger.LogWarning("Could not find Player property on gparam_0");
                    return null;
                }

                return playerProp.GetValue(gparam) as Player;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting player from CoopGame: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cleans up FIKA integration patches.
        /// </summary>
        public static void Cleanup()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            IsInitialized = false;
        }
    }
}
