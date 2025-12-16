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
        /// Returns true if FIKA is installed and loaded.
        /// </summary>
        public static bool IsFikaInstalled
        {
            get
            {
                if (_isFikaInstalled.HasValue)
                    return _isFikaInstalled.Value;

                _isFikaInstalled = DetectFika();
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
        /// Detects if FIKA is installed by checking for the BepInEx plugin.
        /// </summary>
        private static bool DetectFika()
        {
            try
            {
                // Method 1: Check BepInEx plugin chain
                var fikaPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos
                    .FirstOrDefault(p => p.Key == FikaPluginGuid);

                if (fikaPlugin.Value != null)
                {
                    Logger.LogInfo("FIKA detected via BepInEx Chainloader!");
                    _fikaAssembly = fikaPlugin.Value.Instance.GetType().Assembly;
                    CacheTypes();
                    return true;
                }

                // Method 2: Check loaded assemblies for Fika.Core
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        if (assembly.GetName().Name == "Fika.Core" ||
                            assembly.FullName.Contains("Fika.Core"))
                        {
                            Logger.LogInfo($"FIKA detected via assembly scan: {assembly.FullName}");
                            _fikaAssembly = assembly;
                            CacheTypes();
                            return true;
                        }
                    }
                    catch
                    {
                        // Skip assemblies that can't be inspected
                    }
                }

                Logger.LogDebug("FIKA not detected.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error detecting FIKA: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Caches commonly used FIKA types for reflection.
        /// </summary>
        private static void CacheTypes()
        {
            if (_fikaAssembly == null)
                return;

            try
            {
                // Cache CoopGame type - this is the main game class in FIKA
                _coopGameType = _fikaAssembly.GetType("Fika.Core.Main.GameMode.CoopGame");

                if (_coopGameType != null)
                {
                    Logger.LogInfo($"Cached CoopGame type: {_coopGameType.FullName}");
                }
                else
                {
                    Logger.LogWarning("Could not find CoopGame type in FIKA assembly.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error caching FIKA types: {ex.Message}");
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
                return null;

            try
            {
                return _fikaAssembly.GetType(typeName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error getting FIKA type '{typeName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Logs detailed information about the FIKA installation for debugging.
        /// </summary>
        public static void LogFikaInfo()
        {
            if (!IsFikaInstalled)
            {
                Logger.LogInfo("FIKA is not installed.");
                return;
            }

            Logger.LogInfo("=== FIKA Installation Info ===");
            Logger.LogInfo($"Assembly: {_fikaAssembly?.FullName ?? "Unknown"}");
            Logger.LogInfo($"CoopGame Type: {_coopGameType?.FullName ?? "Not found"}");

            // List key types for debugging
            if (_fikaAssembly != null)
            {
                try
                {
                    var types = _fikaAssembly.GetTypes()
                        .Where(t => t.Namespace?.Contains("GameMode") == true ||
                                   t.Name.Contains("CoopGame") ||
                                   t.Name.Contains("FikaPlayer"))
                        .Take(10);

                    Logger.LogInfo("Key FIKA types found:");
                    foreach (var type in types)
                    {
                        Logger.LogInfo($"  - {type.FullName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Error listing FIKA types: {ex.Message}");
                }
            }
        }
    }
}
