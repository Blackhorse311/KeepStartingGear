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
            Logger.LogInfo("[FIKA-INIT] ========================================");
            Logger.LogInfo("[FIKA-INIT] Starting FIKA Integration Initialization");
            Logger.LogInfo("[FIKA-INIT] ========================================");

            if (IsInitialized)
            {
                Logger.LogWarning("[FIKA-INIT] FIKA integration already initialized - skipping.");
                return true;
            }

            if (!FikaDetector.IsFikaInstalled)
            {
                Logger.LogInfo("[FIKA-INIT] FIKA not detected - skipping FIKA integration.");
                return false;
            }

            Logger.LogInfo("[FIKA-INIT] FIKA detected! Proceeding with integration...");
            FikaDetector.LogFikaInfo();

            try
            {
                Logger.LogInfo($"[FIKA-INIT] Creating Harmony instance with ID: {harmonyId}.fika");
                _harmony = new Harmony(harmonyId + ".fika");

                // Try to patch FIKA's death handling
                Logger.LogInfo("[FIKA-INIT] Attempting to patch FIKA death handler...");
                bool patched = TryPatchFikaDeathHandler();

                if (patched)
                {
                    IsInitialized = true;
                    Logger.LogInfo("[FIKA-INIT] ========================================");
                    Logger.LogInfo("[FIKA-INIT] FIKA integration initialized SUCCESSFULLY!");
                    Logger.LogInfo("[FIKA-INIT] ========================================");
                    return true;
                }
                else
                {
                    Logger.LogWarning("[FIKA-INIT] ========================================");
                    Logger.LogWarning("[FIKA-INIT] Failed to patch FIKA death handler!");
                    Logger.LogWarning("[FIKA-INIT] FIKA integration is DISABLED.");
                    Logger.LogWarning("[FIKA-INIT] ========================================");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[FIKA-INIT] ========================================");
                Logger.LogError($"[FIKA-INIT] CRITICAL ERROR during initialization: {ex.Message}");
                Logger.LogError($"[FIKA-INIT] Exception type: {ex.GetType().FullName}");
                Logger.LogError($"[FIKA-INIT] Stack trace:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Logger.LogError($"[FIKA-INIT] Inner exception: {ex.InnerException.Message}");
                    Logger.LogError($"[FIKA-INIT] Inner stack trace:\n{ex.InnerException.StackTrace}");
                }
                Logger.LogError("[FIKA-INIT] ========================================");
                return false;
            }
        }

        /// <summary>
        /// Attempts to patch FIKA's HealthController_DiedEvent method.
        /// </summary>
        private static bool TryPatchFikaDeathHandler()
        {
            Logger.LogInfo("[FIKA-PATCH] Starting death handler patching process...");

            var coopGameType = FikaDetector.CoopGameType;
            if (coopGameType == null)
            {
                Logger.LogError("[FIKA-PATCH] CoopGame type not found - cannot patch FIKA.");
                Logger.LogError("[FIKA-PATCH] This may indicate FIKA's structure has changed.");
                return false;
            }

            Logger.LogInfo($"[FIKA-PATCH] CoopGame type found: {coopGameType.FullName}");

            // Find the HealthController_DiedEvent method
            // It's a private async void method: private async void HealthController_DiedEvent(EDamageType damageType)
            Logger.LogInfo("[FIKA-PATCH] Searching for HealthController_DiedEvent method...");
            Logger.LogInfo("[FIKA-PATCH]   Expected signature: void HealthController_DiedEvent(EDamageType)");

            var deathMethod = coopGameType.GetMethod(
                "HealthController_DiedEvent",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(EDamageType) },
                null
            );

            if (deathMethod == null)
            {
                Logger.LogWarning("[FIKA-PATCH] HealthController_DiedEvent method not found with exact signature.");
                Logger.LogInfo("[FIKA-PATCH] Searching all methods for potential matches...");

                // Try to find it by searching all methods
                var allMethods = coopGameType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Logger.LogInfo($"[FIKA-PATCH] Total methods in CoopGame: {allMethods.Length}");

                foreach (var method in allMethods)
                {
                    // Log all methods that might be death-related
                    if (method.Name.Contains("Die") || method.Name.Contains("Death") ||
                        method.Name.Contains("Kill") || method.Name.Contains("Health"))
                    {
                        var parameters = string.Join(", ", Array.ConvertAll(method.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}"));
                        Logger.LogInfo($"[FIKA-PATCH]   Potential match: {method.Name}({parameters}) -> {method.ReturnType.Name}");
                        Logger.LogInfo($"[FIKA-PATCH]     IsPrivate: {method.IsPrivate}, IsPublic: {method.IsPublic}");

                        // Try to match by name
                        if (method.Name == "HealthController_DiedEvent")
                        {
                            Logger.LogInfo($"[FIKA-PATCH]   FOUND by name match!");
                            deathMethod = method;
                            break;
                        }
                    }
                }

                if (deathMethod == null)
                {
                    Logger.LogError("[FIKA-PATCH] Could not find HealthController_DiedEvent method!");
                    Logger.LogError("[FIKA-PATCH] FIKA's code structure may have changed.");
                    return false;
                }
            }

            Logger.LogInfo($"[FIKA-PATCH] Found death method: {deathMethod.Name}");
            Logger.LogInfo($"[FIKA-PATCH]   DeclaringType: {deathMethod.DeclaringType?.FullName}");
            Logger.LogInfo($"[FIKA-PATCH]   ReturnType: {deathMethod.ReturnType.Name}");
            Logger.LogInfo($"[FIKA-PATCH]   IsPrivate: {deathMethod.IsPrivate}");
            Logger.LogInfo($"[FIKA-PATCH]   IsVirtual: {deathMethod.IsVirtual}");
            Logger.LogInfo($"[FIKA-PATCH]   Parameters: {deathMethod.GetParameters().Length}");
            foreach (var param in deathMethod.GetParameters())
            {
                Logger.LogInfo($"[FIKA-PATCH]     - {param.ParameterType.FullName} {param.Name}");
            }

            // Create and apply the prefix patch
            Logger.LogInfo("[FIKA-PATCH] Finding our prefix method...");
            var prefixMethod = typeof(FikaIntegration).GetMethod(
                nameof(HealthController_DiedEvent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            if (prefixMethod == null)
            {
                Logger.LogError("[FIKA-PATCH] Could not find our prefix method!");
                Logger.LogError("[FIKA-PATCH] This is a bug in KeepStartingGear.");
                return false;
            }

            Logger.LogInfo($"[FIKA-PATCH] Found prefix method: {prefixMethod.Name}");
            Logger.LogInfo($"[FIKA-PATCH]   Parameters: {prefixMethod.GetParameters().Length}");
            foreach (var param in prefixMethod.GetParameters())
            {
                Logger.LogInfo($"[FIKA-PATCH]     - {param.ParameterType.Name} {param.Name}");
            }

            try
            {
                Logger.LogInfo("[FIKA-PATCH] Applying Harmony patch...");
                _harmony.Patch(deathMethod, prefix: new HarmonyMethod(prefixMethod));
                Logger.LogInfo("[FIKA-PATCH] SUCCESS! Harmony patch applied!");

                // Verify the patch was applied
                var patches = Harmony.GetPatchInfo(deathMethod);
                if (patches != null)
                {
                    Logger.LogInfo($"[FIKA-PATCH] Patch verification:");
                    Logger.LogInfo($"[FIKA-PATCH]   Prefixes: {patches.Prefixes?.Count ?? 0}");
                    Logger.LogInfo($"[FIKA-PATCH]   Postfixes: {patches.Postfixes?.Count ?? 0}");
                    Logger.LogInfo($"[FIKA-PATCH]   Transpilers: {patches.Transpilers?.Count ?? 0}");

                    if (patches.Prefixes != null)
                    {
                        foreach (var prefix in patches.Prefixes)
                        {
                            Logger.LogInfo($"[FIKA-PATCH]     Prefix: {prefix.owner} - {prefix.PatchMethod.Name}");
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-PATCH] Failed to apply Harmony patch: {ex.Message}");
                Logger.LogError($"[FIKA-PATCH] Exception type: {ex.GetType().FullName}");
                Logger.LogError($"[FIKA-PATCH] Stack trace:\n{ex.StackTrace}");
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
            Logger.LogInfo("[FIKA-DEATH] ========================================");
            Logger.LogInfo("[FIKA-DEATH] DEATH EVENT INTERCEPTED!");
            Logger.LogInfo("[FIKA-DEATH] ========================================");

            try
            {
                Logger.LogInfo($"[FIKA-DEATH] Damage type: {damageType}");
                Logger.LogInfo($"[FIKA-DEATH] CoopGame instance: {__instance?.GetType().FullName ?? "null"}");

                if (__instance == null)
                {
                    Logger.LogError("[FIKA-DEATH] CoopGame instance is null! Cannot proceed.");
                    return;
                }

                // Get the player from the CoopGame instance
                Logger.LogInfo("[FIKA-DEATH] Extracting player from CoopGame instance...");
                var player = GetPlayerFromCoopGame(__instance);

                if (player == null)
                {
                    Logger.LogWarning("[FIKA-DEATH] Could not get player from CoopGame instance.");
                    Logger.LogWarning("[FIKA-DEATH] Inventory restoration SKIPPED.");
                    return;
                }

                Logger.LogInfo($"[FIKA-DEATH] Player extracted successfully!");
                Logger.LogInfo($"[FIKA-DEATH]   Player Type: {player.GetType().FullName}");
                Logger.LogInfo($"[FIKA-DEATH]   ProfileId: {player.ProfileId ?? "null"}");
                Logger.LogInfo($"[FIKA-DEATH]   Nickname: {player.Profile?.Nickname ?? "null"}");
                Logger.LogInfo($"[FIKA-DEATH]   IsYourPlayer: {player.IsYourPlayer}");
                Logger.LogInfo($"[FIKA-DEATH]   IsAI: {player.IsAI}");

                // Only process if this is the local player
                if (!player.IsYourPlayer)
                {
                    Logger.LogInfo("[FIKA-DEATH] This is NOT the local player - skipping restoration.");
                    return;
                }

                // Call our snapshot restoration callback
                if (OnPlayerDeathBeforeFika != null)
                {
                    Logger.LogInfo("[FIKA-DEATH] Invoking snapshot restoration callback...");
                    Logger.LogInfo("[FIKA-DEATH] ----------------------------------------");

                    OnPlayerDeathBeforeFika.Invoke(player);

                    Logger.LogInfo("[FIKA-DEATH] ----------------------------------------");
                    Logger.LogInfo("[FIKA-DEATH] Snapshot restoration callback completed.");
                }
                else
                {
                    Logger.LogWarning("[FIKA-DEATH] No death callback registered!");
                    Logger.LogWarning("[FIKA-DEATH] OnPlayerDeathBeforeFika is null.");
                    Logger.LogWarning("[FIKA-DEATH] This may indicate initialization failure.");
                }

                Logger.LogInfo("[FIKA-DEATH] ========================================");
                Logger.LogInfo("[FIKA-DEATH] Death event processing complete.");
                Logger.LogInfo("[FIKA-DEATH] FIKA will now continue with its death handling.");
                Logger.LogInfo("[FIKA-DEATH] ========================================");
            }
            catch (Exception ex)
            {
                Logger.LogError("[FIKA-DEATH] ========================================");
                Logger.LogError($"[FIKA-DEATH] ERROR in death prefix: {ex.Message}");
                Logger.LogError($"[FIKA-DEATH] Exception type: {ex.GetType().FullName}");
                Logger.LogError($"[FIKA-DEATH] Stack trace:\n{ex.StackTrace}");
                Logger.LogError("[FIKA-DEATH] ========================================");
            }
        }

        /// <summary>
        /// Extracts the Player from a CoopGame instance using reflection.
        /// </summary>
        private static Player GetPlayerFromCoopGame(object coopGame)
        {
            Logger.LogInfo("[FIKA-PLAYER] Extracting player from CoopGame...");

            if (coopGame == null)
            {
                Logger.LogError("[FIKA-PLAYER] CoopGame is null!");
                return null;
            }

            var coopGameType = coopGame.GetType();
            Logger.LogInfo($"[FIKA-PLAYER] CoopGame type: {coopGameType.FullName}");

            try
            {
                // Try to get gparam_0 field (inherited from BaseLocalGame)
                Logger.LogInfo("[FIKA-PLAYER] Looking for gparam_0 field...");

                FieldInfo gparamField = null;

                // Search in current type and all base types
                var searchType = coopGameType;
                while (searchType != null && gparamField == null)
                {
                    Logger.LogDebug($"[FIKA-PLAYER]   Searching in: {searchType.FullName}");

                    gparamField = searchType.GetField("gparam_0", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (gparamField == null)
                    {
                        // List all fields in this type for debugging
                        var fields = searchType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        foreach (var f in fields)
                        {
                            if (f.Name.IndexOf("param", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Logger.LogDebug($"[FIKA-PLAYER]     Field: {f.FieldType.Name} {f.Name}");
                            }
                        }
                    }

                    searchType = searchType.BaseType;
                }

                if (gparamField == null)
                {
                    Logger.LogError("[FIKA-PLAYER] Could not find gparam_0 field in type hierarchy!");

                    // Try alternative: look for LocalPlayer_0 property
                    Logger.LogInfo("[FIKA-PLAYER] Trying alternative: LocalPlayer_0 property...");
                    var localPlayerProp = coopGameType.GetProperty("LocalPlayer_0",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (localPlayerProp != null)
                    {
                        Logger.LogInfo($"[FIKA-PLAYER] Found LocalPlayer_0 property!");
                        var localPlayer = localPlayerProp.GetValue(coopGame) as Player;
                        if (localPlayer != null)
                        {
                            Logger.LogInfo($"[FIKA-PLAYER] Got player via LocalPlayer_0: {localPlayer.ProfileId}");
                            return localPlayer;
                        }
                    }

                    return null;
                }

                Logger.LogInfo($"[FIKA-PLAYER] Found gparam_0 field in {gparamField.DeclaringType?.Name}");
                Logger.LogInfo($"[FIKA-PLAYER]   Field type: {gparamField.FieldType.FullName}");

                var gparam = gparamField.GetValue(coopGame);
                if (gparam == null)
                {
                    Logger.LogWarning("[FIKA-PLAYER] gparam_0 value is null");
                    return null;
                }

                Logger.LogInfo($"[FIKA-PLAYER] gparam_0 value type: {gparam.GetType().FullName}");

                // gparam_0 has a Player property
                Logger.LogInfo("[FIKA-PLAYER] Looking for Player property on gparam_0...");
                var playerProp = gparam.GetType().GetProperty("Player", BindingFlags.Public | BindingFlags.Instance);

                if (playerProp == null)
                {
                    Logger.LogWarning("[FIKA-PLAYER] Could not find Player property on gparam_0");

                    // List all properties for debugging
                    var props = gparam.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Logger.LogInfo($"[FIKA-PLAYER] Properties on gparam_0 ({props.Length} total):");
                    foreach (var p in props)
                    {
                        Logger.LogInfo($"[FIKA-PLAYER]   - {p.PropertyType.Name} {p.Name}");
                    }

                    return null;
                }

                Logger.LogInfo($"[FIKA-PLAYER] Found Player property: {playerProp.PropertyType.FullName}");

                var player = playerProp.GetValue(gparam) as Player;
                if (player != null)
                {
                    Logger.LogInfo($"[FIKA-PLAYER] Successfully extracted player: {player.ProfileId}");
                }
                else
                {
                    Logger.LogWarning("[FIKA-PLAYER] Player property returned null");
                }

                return player;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-PLAYER] ERROR extracting player: {ex.Message}");
                Logger.LogError($"[FIKA-PLAYER] Stack trace:\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Cleans up FIKA integration patches.
        /// </summary>
        public static void Cleanup()
        {
            Logger.LogInfo("[FIKA-CLEANUP] Cleaning up FIKA integration...");

            if (_harmony != null)
            {
                Logger.LogInfo("[FIKA-CLEANUP] Unpatching Harmony patches...");
                _harmony.UnpatchSelf();
                _harmony = null;
                Logger.LogInfo("[FIKA-CLEANUP] Harmony patches removed.");
            }

            IsInitialized = false;
            Logger.LogInfo("[FIKA-CLEANUP] FIKA integration cleanup complete.");
        }
    }
}
