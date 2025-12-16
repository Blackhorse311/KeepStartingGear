// Keep Starting Gear - FIKA Compatibility Detection
//
// This class detects whether FIKA (multiplayer mod) is installed and provides
// utilities for FIKA-specific integration.
//
// FIKA changes the raid end flow significantly:
// - Player death is handled client-side via CoopGame.HealthController_DiedEvent
// - Inventory is serialized CLIENT-SIDE and sent to server via SavePlayer()
// - Our server-side hooks run AFTER FIKA has already captured the "dead" inventory
//
// To support FIKA, we need to hook into the client-side death event BEFORE
// FIKA serializes the inventory.

using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;

namespace Blackhorse311.KeepStartingGear.Compatibility
{
    /// <summary>
    /// Detects FIKA installation and provides access to FIKA types via reflection.
    /// This allows us to integrate with FIKA without requiring a hard dependency.
    /// </summary>
    public static class FikaDetector
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("KSG-FikaDetector");

        /// <summary>
        /// FIKA's plugin GUID
        /// </summary>
        public const string FikaPluginGuid = "com.fika.core";

        /// <summary>
        /// Cached result of FIKA detection
        /// </summary>
        private static bool? _isFikaInstalled;

        /// <summary>
        /// Cached FIKA assembly reference
        /// </summary>
        private static Assembly _fikaAssembly;

        /// <summary>
        /// Cached CoopGame type
        /// </summary>
        private static Type _coopGameType;

        /// <summary>
        /// Cached FikaPlayer type
        /// </summary>
        private static Type _fikaPlayerType;

        /// <summary>
        /// Returns true if FIKA is installed and loaded.
        /// </summary>
        public static bool IsFikaInstalled
        {
            get
            {
                if (_isFikaInstalled.HasValue)
                    return _isFikaInstalled.Value;

                Logger.LogInfo("[FIKA-DETECT] Starting FIKA detection...");
                _isFikaInstalled = DetectFika();
                Logger.LogInfo($"[FIKA-DETECT] Detection complete. FIKA installed: {_isFikaInstalled.Value}");
                return _isFikaInstalled.Value;
            }
        }

        /// <summary>
        /// Gets the FIKA assembly if installed, null otherwise.
        /// </summary>
        public static Assembly FikaAssembly => _fikaAssembly;

        /// <summary>
        /// Gets the CoopGame type from FIKA if available.
        /// </summary>
        public static Type CoopGameType => _coopGameType;

        /// <summary>
        /// Gets the FikaPlayer type from FIKA if available.
        /// </summary>
        public static Type FikaPlayerType => _fikaPlayerType;

        /// <summary>
        /// Detects if FIKA is installed by checking for the BepInEx plugin.
        /// </summary>
        private static bool DetectFika()
        {
            try
            {
                Logger.LogInfo("[FIKA-DETECT] Method 1: Checking BepInEx Chainloader...");
                Logger.LogInfo($"[FIKA-DETECT] Looking for plugin GUID: {FikaPluginGuid}");
                Logger.LogInfo($"[FIKA-DETECT] Total plugins in Chainloader: {BepInEx.Bootstrap.Chainloader.PluginInfos.Count}");

                // Log all loaded plugins for debugging
                foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    Logger.LogDebug($"[FIKA-DETECT]   Plugin: {plugin.Key}");
                }

                var fikaPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos
                    .FirstOrDefault(p => p.Key == FikaPluginGuid);

                if (fikaPlugin.Value != null)
                {
                    Logger.LogInfo("[FIKA-DETECT] SUCCESS! FIKA detected via BepInEx Chainloader!");
                    Logger.LogInfo($"[FIKA-DETECT] FIKA Plugin Info:");
                    Logger.LogInfo($"[FIKA-DETECT]   GUID: {fikaPlugin.Key}");
                    Logger.LogInfo($"[FIKA-DETECT]   Instance Type: {fikaPlugin.Value.Instance?.GetType().FullName ?? "null"}");

                    _fikaAssembly = fikaPlugin.Value.Instance.GetType().Assembly;
                    Logger.LogInfo($"[FIKA-DETECT]   Assembly: {_fikaAssembly.FullName}");
                    Logger.LogInfo($"[FIKA-DETECT]   Assembly Location: {_fikaAssembly.Location}");

                    CacheTypes();
                    return true;
                }

                Logger.LogInfo("[FIKA-DETECT] FIKA not found in Chainloader. Trying assembly scan...");

                // Method 2: Check loaded assemblies for Fika.Core
                Logger.LogInfo("[FIKA-DETECT] Method 2: Scanning loaded assemblies...");
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Logger.LogInfo($"[FIKA-DETECT] Total assemblies loaded: {assemblies.Length}");

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var assemblyName = assembly.GetName().Name;
                        if (assemblyName.IndexOf("Fika", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Logger.LogInfo($"[FIKA-DETECT] Found FIKA-related assembly: {assembly.FullName}");
                        }

                        if (assemblyName == "Fika.Core" || assembly.FullName.Contains("Fika.Core"))
                        {
                            Logger.LogInfo($"[FIKA-DETECT] SUCCESS! FIKA detected via assembly scan!");
                            Logger.LogInfo($"[FIKA-DETECT]   Assembly: {assembly.FullName}");
                            Logger.LogInfo($"[FIKA-DETECT]   Location: {assembly.Location}");

                            _fikaAssembly = assembly;
                            CacheTypes();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"[FIKA-DETECT] Could not inspect assembly: {ex.Message}");
                    }
                }

                Logger.LogInfo("[FIKA-DETECT] FIKA not detected by any method.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-DETECT] ERROR during FIKA detection: {ex.Message}");
                Logger.LogError($"[FIKA-DETECT] Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Caches commonly used FIKA types for reflection.
        /// </summary>
        private static void CacheTypes()
        {
            if (_fikaAssembly == null)
            {
                Logger.LogWarning("[FIKA-DETECT] Cannot cache types - assembly is null");
                return;
            }

            Logger.LogInfo("[FIKA-DETECT] Caching FIKA types...");

            try
            {
                // Cache CoopGame type - this is the main game class in FIKA
                Logger.LogInfo("[FIKA-DETECT] Looking for CoopGame type...");
                _coopGameType = _fikaAssembly.GetType("Fika.Core.Main.GameMode.CoopGame");

                if (_coopGameType != null)
                {
                    Logger.LogInfo($"[FIKA-DETECT] SUCCESS! Found CoopGame: {_coopGameType.FullName}");
                    LogTypeDetails(_coopGameType, "CoopGame");
                }
                else
                {
                    Logger.LogWarning("[FIKA-DETECT] Could not find CoopGame type at expected path.");
                    Logger.LogInfo("[FIKA-DETECT] Searching for CoopGame in all types...");

                    // Try to find it by searching
                    foreach (var type in _fikaAssembly.GetTypes())
                    {
                        if (type.Name == "CoopGame")
                        {
                            Logger.LogInfo($"[FIKA-DETECT] Found CoopGame at: {type.FullName}");
                            _coopGameType = type;
                            LogTypeDetails(_coopGameType, "CoopGame");
                            break;
                        }
                    }
                }

                // Cache FikaPlayer type
                Logger.LogInfo("[FIKA-DETECT] Looking for FikaPlayer type...");
                _fikaPlayerType = _fikaAssembly.GetType("Fika.Core.Main.Players.FikaPlayer");

                if (_fikaPlayerType != null)
                {
                    Logger.LogInfo($"[FIKA-DETECT] SUCCESS! Found FikaPlayer: {_fikaPlayerType.FullName}");
                }
                else
                {
                    Logger.LogDebug("[FIKA-DETECT] FikaPlayer type not found (may not be needed).");
                }

                Logger.LogInfo("[FIKA-DETECT] Type caching complete.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-DETECT] ERROR caching FIKA types: {ex.Message}");
                Logger.LogError($"[FIKA-DETECT] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Logs detailed information about a type for debugging.
        /// </summary>
        private static void LogTypeDetails(Type type, string typeName)
        {
            Logger.LogInfo($"[FIKA-DETECT] === {typeName} Type Details ===");
            Logger.LogInfo($"[FIKA-DETECT]   Full Name: {type.FullName}");
            Logger.LogInfo($"[FIKA-DETECT]   Base Type: {type.BaseType?.FullName ?? "none"}");
            Logger.LogInfo($"[FIKA-DETECT]   Is Class: {type.IsClass}");
            Logger.LogInfo($"[FIKA-DETECT]   Is Sealed: {type.IsSealed}");

            // Log methods we care about
            Logger.LogInfo($"[FIKA-DETECT]   Looking for death-related methods...");
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.Name.Contains("Die") || method.Name.Contains("Death") || method.Name.Contains("Kill") ||
                    method.Name.Contains("Save") || method.Name.Contains("Stop"))
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Logger.LogInfo($"[FIKA-DETECT]     Method: {method.Name}({parameters}) -> {method.ReturnType.Name}");
                }
            }

            // Log fields we care about
            Logger.LogInfo($"[FIKA-DETECT]   Looking for player-related fields...");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    field.Name.IndexOf("gparam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    field.Name.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.LogInfo($"[FIKA-DETECT]     Field: {field.FieldType.Name} {field.Name}");
                }
            }
        }

        /// <summary>
        /// Gets a type from the FIKA assembly by full name.
        /// </summary>
        /// <param name="typeName">Full type name (e.g., "Fika.Core.Main.GameMode.CoopGame")</param>
        /// <returns>The Type if found, null otherwise</returns>
        public static Type GetFikaType(string typeName)
        {
            if (_fikaAssembly == null)
            {
                Logger.LogWarning($"[FIKA-DETECT] Cannot get type '{typeName}' - assembly is null");
                return null;
            }

            try
            {
                Logger.LogDebug($"[FIKA-DETECT] Getting type: {typeName}");
                var type = _fikaAssembly.GetType(typeName);
                if (type != null)
                {
                    Logger.LogDebug($"[FIKA-DETECT] Found type: {type.FullName}");
                }
                else
                {
                    Logger.LogWarning($"[FIKA-DETECT] Type not found: {typeName}");
                }
                return type;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[FIKA-DETECT] ERROR getting type '{typeName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Logs detailed information about the FIKA installation for debugging.
        /// </summary>
        public static void LogFikaInfo()
        {
            Logger.LogInfo("[FIKA-DETECT] ========================================");
            Logger.LogInfo("[FIKA-DETECT] FIKA Installation Diagnostic Report");
            Logger.LogInfo("[FIKA-DETECT] ========================================");

            if (!IsFikaInstalled)
            {
                Logger.LogInfo("[FIKA-DETECT] FIKA is NOT installed.");
                Logger.LogInfo("[FIKA-DETECT] ========================================");
                return;
            }

            Logger.LogInfo($"[FIKA-DETECT] FIKA IS INSTALLED!");
            Logger.LogInfo($"[FIKA-DETECT] Assembly: {_fikaAssembly?.FullName ?? "Unknown"}");
            Logger.LogInfo($"[FIKA-DETECT] Assembly Location: {_fikaAssembly?.Location ?? "Unknown"}");
            Logger.LogInfo($"[FIKA-DETECT] CoopGame Type: {_coopGameType?.FullName ?? "NOT FOUND"}");
            Logger.LogInfo($"[FIKA-DETECT] FikaPlayer Type: {_fikaPlayerType?.FullName ?? "NOT FOUND"}");

            // List all FIKA namespaces
            if (_fikaAssembly != null)
            {
                try
                {
                    Logger.LogInfo("[FIKA-DETECT] FIKA Namespaces:");
                    var namespaces = _fikaAssembly.GetTypes()
                        .Where(t => t.Namespace != null)
                        .Select(t => t.Namespace)
                        .Distinct()
                        .OrderBy(n => n)
                        .Take(20);

                    foreach (var ns in namespaces)
                    {
                        Logger.LogInfo($"[FIKA-DETECT]   - {ns}");
                    }

                    // List key types for debugging
                    Logger.LogInfo("[FIKA-DETECT] Key FIKA types:");
                    var types = _fikaAssembly.GetTypes()
                        .Where(t => t.Namespace?.Contains("GameMode") == true ||
                                   t.Namespace?.Contains("Player") == true ||
                                   t.Name.Contains("CoopGame") ||
                                   t.Name.Contains("FikaPlayer") ||
                                   t.Name.Contains("Death") ||
                                   t.Name.Contains("Save"))
                        .Take(20);

                    foreach (var type in types)
                    {
                        Logger.LogInfo($"[FIKA-DETECT]   - {type.FullName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[FIKA-DETECT] ERROR listing FIKA types: {ex.Message}");
                }
            }

            Logger.LogInfo("[FIKA-DETECT] ========================================");
        }
    }
}
