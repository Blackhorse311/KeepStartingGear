// ============================================================================
// Keep Starting Gear - Mod Detection Framework
// ============================================================================
// Core detection system for identifying installed mods that may conflict with
// or integrate with Keep Starting Gear.
//
// DETECTION METHODS:
// 1. Folder scanning - Checks for mod folders in user/mods/
// 2. DLL detection - Checks for specific assemblies
// 3. Config reading - Parses other mods' config files to check enabled features
//
// SUPPORTED MODS:
// - SVM (Server Value Modifier) - Conflict if SaveGearAfterDeath enabled
// - FIKA - Integration support for multiplayer
// - Arcade Mode - Direct conflict (same functionality)
// - Never Lose Equipments - Direct conflict (same functionality)
// - Keep Your Equipment variants - Direct conflict (same functionality)
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blackhorse311.KeepStartingGear.Server.Compatibility;

/// <summary>
/// Simple logger adapter that delegates to action delegates.
/// Avoids the complexity of implementing the full ISptLogger interface.
/// </summary>
public class SimpleLoggerAdapter
{
    private readonly Action<string> _debug;
    private readonly Action<string> _info;
    private readonly Action<string> _warning;
    private readonly Action<string> _error;

    /// <summary>
    /// Creates a new SimpleLoggerAdapter.
    /// </summary>
    /// <param name="debug">Debug logging delegate</param>
    /// <param name="info">Info logging delegate</param>
    /// <param name="warning">Warning logging delegate</param>
    /// <param name="error">Error logging delegate</param>
    /// <exception cref="ArgumentNullException">Thrown if any delegate is null</exception>
    public SimpleLoggerAdapter(
        Action<string> debug,
        Action<string> info,
        Action<string> warning,
        Action<string> error)
    {
        _debug = debug ?? throw new ArgumentNullException(nameof(debug));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public void Debug(string message) => _debug(message);
    public void Info(string message) => _info(message);
    public void Warning(string message) => _warning(message);
    public void Error(string message) => _error(message);
}

/// <summary>
/// Represents the detection result for a specific mod.
/// </summary>
public class DetectedMod
{
    /// <summary>
    /// Unique identifier for the mod.
    /// </summary>
    public string ModId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Whether the mod was detected as installed.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Path to the mod folder (if detected).
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Version string if detected.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Conflict level with Keep Starting Gear.
    /// </summary>
    public ConflictLevel ConflictLevel { get; set; } = ConflictLevel.None;

    /// <summary>
    /// Detailed reason for the conflict level.
    /// </summary>
    public string ConflictReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mod can potentially be integrated with.
    /// </summary>
    public bool SupportsIntegration { get; set; }

    // =========================================================================
    // STRONGLY-TYPED METADATA (replacing Dictionary<string, object>)
    // FIX: Type-safe properties instead of untyped dictionary
    // =========================================================================

    /// <summary>
    /// SVM configuration data (if this is an SVM detection result).
    /// </summary>
    public SVMConfigData? SVMConfig { get; set; }

    /// <summary>
    /// Path to the mod's main DLL (if detected).
    /// </summary>
    public string? DllPath { get; set; }

    /// <summary>
    /// Whether SVM's SaveGearAfterDeath feature is enabled.
    /// </summary>
    public bool SaveGearAfterDeathEnabled { get; set; }

    /// <summary>
    /// Whether SVM's SafeExit feature is enabled.
    /// </summary>
    public bool SafeExitEnabled { get; set; }

    /// <summary>
    /// Whether this mod requires FIKA integration mode.
    /// </summary>
    public bool RequiresFikaMode { get; set; }
}

/// <summary>
/// Defines the severity of conflict with another mod.
/// </summary>
public enum ConflictLevel
{
    /// <summary>
    /// No conflict - mods can coexist safely.
    /// </summary>
    None = 0,

    /// <summary>
    /// Minor conflict - some features may overlap but generally works.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Moderate conflict - some features may not work correctly.
    /// Warn user but allow operation.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Significant conflict - features directly conflict.
    /// User should choose which mod to use.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical conflict - mods cannot coexist.
    /// Block loading with error message.
    /// </summary>
    Critical = 4
}

/// <summary>
/// Core mod detection framework.
/// Scans for installed mods and determines compatibility.
/// </summary>
public class ModDetector
{
    private readonly SimpleLoggerAdapter _logger;
    private readonly string _modsDirectory;
    private readonly string _bepInExPluginsDirectory;

    /// <summary>
    /// Results of the last detection scan.
    /// </summary>
    public Dictionary<string, DetectedMod> DetectedMods { get; } = new();

    /// <summary>
    /// Whether any critical conflicts were detected.
    /// </summary>
    public bool HasCriticalConflicts => DetectedMods.Values.Any(m => m.ConflictLevel == ConflictLevel.Critical);

    /// <summary>
    /// Whether any high or critical conflicts were detected.
    /// </summary>
    public bool HasSignificantConflicts => DetectedMods.Values.Any(m =>
        m.ConflictLevel >= ConflictLevel.High);

    /// <summary>
    /// Creates a new ModDetector instance.
    /// </summary>
    /// <param name="logger">Logger adapter instance - must not be null</param>
    /// <param name="serverModPath">Path to this mod's server DLL - must be a valid path</param>
    /// <exception cref="ArgumentNullException">Thrown if logger is null</exception>
    /// <exception cref="ArgumentException">Thrown if serverModPath is invalid</exception>
    public ModDetector(SimpleLoggerAdapter logger, string serverModPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger),
            "Logger cannot be null - ModDetector requires logging capability");

        // Validate serverModPath - FAIL FAST on bad input
        if (string.IsNullOrEmpty(serverModPath))
        {
            throw new ArgumentException(
                "serverModPath cannot be null or empty - required to locate mod directories",
                nameof(serverModPath));
        }

        // Calculate paths from our mod's location
        // Server mod: {SPT_ROOT}/user/mods/{ModFolder}/
        string? modDirectory = Path.GetDirectoryName(serverModPath);

        // CRITICAL FIX: Fail fast if path resolution fails
        if (string.IsNullOrEmpty(modDirectory))
        {
            throw new ArgumentException(
                $"Could not extract directory from serverModPath: '{serverModPath}'. " +
                "Path must be a valid file path to the mod's DLL.",
                nameof(serverModPath));
        }

        // Verify the mod directory exists before proceeding
        if (!Directory.Exists(modDirectory))
        {
            throw new ArgumentException(
                $"Mod directory does not exist: '{modDirectory}'. " +
                "The server mod must be properly installed.",
                nameof(serverModPath));
        }

        // Calculate parent mods directory (one level up from our mod folder)
        _modsDirectory = Path.GetFullPath(Path.Combine(modDirectory, ".."));

        // Verify mods directory exists
        if (!Directory.Exists(_modsDirectory))
        {
            _logger.Warning($"{Constants.LogPrefix} Mods directory not found: {_modsDirectory}");
            _logger.Warning($"{Constants.LogPrefix} Mod detection may be incomplete.");
        }

        // BepInEx plugins: {SPT_ROOT}/BepInEx/plugins/
        // Navigate up 4 levels: mod folder -> mods -> user -> SPT -> root
        string sptRoot = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", "..", ".."));
        _bepInExPluginsDirectory = Path.Combine(sptRoot, "BepInEx", "plugins");

        // Log if BepInEx directory doesn't exist (not fatal, but worth noting)
        if (!Directory.Exists(_bepInExPluginsDirectory))
        {
            _logger.Warning($"{Constants.LogPrefix} BepInEx plugins directory not found: {_bepInExPluginsDirectory}");
            _logger.Warning($"{Constants.LogPrefix} Client-side mod detection (FIKA) may be incomplete.");
        }

        _logger.Debug($"{Constants.LogPrefix} ModDetector initialized:");
        _logger.Debug($"{Constants.LogPrefix}   Mods directory: {_modsDirectory}");
        _logger.Debug($"{Constants.LogPrefix}   BepInEx plugins: {_bepInExPluginsDirectory}");
    }

    /// <summary>
    /// Performs a full scan for all known mods.
    /// </summary>
    /// <returns>Dictionary of detected mods by their ID</returns>
    public Dictionary<string, DetectedMod> ScanForMods()
    {
        DetectedMods.Clear();

        _logger.Debug($"{Constants.LogPrefix} Scanning for mods in: {_modsDirectory}");
        _logger.Debug($"{Constants.LogPrefix} BepInEx plugins in: {_bepInExPluginsDirectory}");

        // Scan for each known mod
        ScanForSVM();
        ScanForFIKA();
        ScanForArcadeMode();
        ScanForNeverLoseEquipments();
        ScanForKeepYourEquipment();
        ScanForFinsHardcoreOptions();

        // Log summary
        var installedMods = DetectedMods.Values.Where(m => m.IsInstalled).ToList();
        if (installedMods.Count > 0)
        {
            _logger.Info($"{Constants.LogPrefix} Detected {installedMods.Count} potentially related mods:");
            foreach (var mod in installedMods)
            {
                string conflictStr = mod.ConflictLevel switch
                {
                    ConflictLevel.Critical => " [CRITICAL CONFLICT]",
                    ConflictLevel.High => " [HIGH CONFLICT]",
                    ConflictLevel.Medium => " [MEDIUM CONFLICT]",
                    ConflictLevel.Low => " [LOW CONFLICT]",
                    _ => ""
                };
                _logger.Info($"{Constants.LogPrefix}   - {mod.DisplayName}{conflictStr}");
            }
        }
        else
        {
            _logger.Info($"{Constants.LogPrefix} No conflicting mods detected.");
        }

        return DetectedMods;
    }

    /// <summary>
    /// Scans for SVM (Server Value Modifier).
    /// </summary>
    private void ScanForSVM()
    {
        var mod = new DetectedMod
        {
            ModId = "svm",
            DisplayName = "SVM (Server Value Modifier)",
            SupportsIntegration = true // Can read config and adjust behavior
        };

        // Search for SVM folder with various naming patterns
        string[] svmPatterns = { "svm", "servervaluemodifier", "server value modifier" };

        if (Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant()
                    .Replace("[", "").Replace("]", "");

                foreach (var pattern in svmPatterns)
                {
                    if (folderName.Contains(pattern))
                    {
                        mod.IsInstalled = true;
                        mod.FolderPath = directory;

                        // Try to read SVM config to determine conflict level
                        var svmConfig = ReadSVMConfig(directory);
                        if (svmConfig != null)
                        {
                            // FIX: Use strongly-typed properties instead of Dictionary<string, object>
                            mod.SVMConfig = svmConfig;

                            // Check for conflicting features
                            bool saveGearEnabled = svmConfig.SaveGearAfterDeath && svmConfig.RaidsEnabled;
                            bool safeExitEnabled = svmConfig.SafeExit && svmConfig.RaidsEnabled;

                            mod.SaveGearAfterDeathEnabled = saveGearEnabled;
                            mod.SafeExitEnabled = safeExitEnabled;

                            if (saveGearEnabled)
                            {
                                mod.ConflictLevel = ConflictLevel.High;
                                mod.ConflictReason = "SVM's 'Save Gear After Death' feature is ENABLED. " +
                                    "This conflicts with Keep Starting Gear - both mods try to preserve gear on death. " +
                                    "Please disable SVM's feature OR configure Keep Starting Gear to defer to SVM.";
                            }
                            else if (safeExitEnabled)
                            {
                                mod.ConflictLevel = ConflictLevel.Medium;
                                mod.ConflictReason = "SVM's 'Safe Exit' feature is enabled. " +
                                    "This may affect how deaths are processed but should work alongside Keep Starting Gear.";
                            }
                            else
                            {
                                mod.ConflictLevel = ConflictLevel.None;
                                mod.ConflictReason = "SVM detected but conflicting features are disabled. Safe to use together.";
                            }
                        }
                        else
                        {
                            // Couldn't read config - assume potential conflict
                            mod.ConflictLevel = ConflictLevel.Medium;
                            mod.ConflictReason = "SVM detected but couldn't read config. " +
                                "Please ensure 'Save Gear After Death' and 'Safe Exit' are DISABLED in SVM.";
                        }

                        break;
                    }
                }
                if (mod.IsInstalled) break;
            }
        }

        DetectedMods["svm"] = mod;
    }

    /// <summary>
    /// Reads SVM configuration to determine which features are enabled.
    /// </summary>
    private SVMConfigData? ReadSVMConfig(string svmFolder)
    {
        try
        {
            // SVM uses Loader/loader.json to point to active preset
            string loaderPath = Path.Combine(svmFolder, "Loader", "loader.json");
            if (!File.Exists(loaderPath))
            {
                _logger.Debug($"{Constants.LogPrefix} SVM loader.json not found at: {loaderPath}");
                return null;
            }

            string loaderJson = File.ReadAllText(loaderPath);
            var loaderNode = JsonNode.Parse(loaderJson);
            string? presetName = loaderNode?["CurrentlySelectedPreset"]?.GetValue<string>();

            if (string.IsNullOrEmpty(presetName))
            {
                _logger.Debug($"{Constants.LogPrefix} SVM preset name not found in loader.json");
                return null;
            }

            // Read the active preset
            string presetPath = Path.Combine(svmFolder, "Presets", $"{presetName}.json");
            if (!File.Exists(presetPath))
            {
                _logger.Debug($"{Constants.LogPrefix} SVM preset file not found: {presetPath}");
                return null;
            }

            string presetJson = File.ReadAllText(presetPath);
            var presetNode = JsonNode.Parse(presetJson);

            // Extract the relevant settings
            var raidsNode = presetNode?["Raids"];
            if (raidsNode == null)
            {
                _logger.Debug($"{Constants.LogPrefix} SVM Raids section not found in preset");
                return null;
            }

            return new SVMConfigData
            {
                PresetName = presetName,
                RaidsEnabled = raidsNode["EnableRaids"]?.GetValue<bool>() ?? false,
                SafeExit = raidsNode["SafeExit"]?.GetValue<bool>() ?? false,
                SaveGearAfterDeath = raidsNode["SaveGearAfterDeath"]?.GetValue<bool>() ?? false,
                SaveQuestItems = raidsNode["SaveQuestItems"]?.GetValue<bool>() ?? false,
                NoRunThrough = raidsNode["NoRunThrough"]?.GetValue<bool>() ?? false
            };
        }
        catch (Exception ex)
        {
            _logger.Debug($"{Constants.LogPrefix} Error reading SVM config: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scans for FIKA multiplayer mod.
    /// </summary>
    private void ScanForFIKA()
    {
        var mod = new DetectedMod
        {
            ModId = "fika",
            DisplayName = "FIKA (Multiplayer)",
            SupportsIntegration = true // We can integrate via events
        };

        // FIKA is primarily a BepInEx plugin
        string[] fikaPatterns = { "fika", "fika.core", "fika-core" };

        // Check BepInEx plugins
        if (Directory.Exists(_bepInExPluginsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_bepInExPluginsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant();
                foreach (var pattern in fikaPatterns)
                {
                    if (folderName.Contains(pattern))
                    {
                        mod.IsInstalled = true;
                        mod.FolderPath = directory;

                        // Check for Fika.Core.dll
                        string dllPath = Path.Combine(directory, "Fika.Core.dll");
                        if (File.Exists(dllPath))
                        {
                            // FIX: Use strongly-typed property instead of Dictionary
                            mod.DllPath = dllPath;
                        }

                        break;
                    }
                }
                if (mod.IsInstalled) break;
            }
        }

        // Also check server mods for FIKA server component
        // FIX: More specific pattern matching to avoid false positives like "konfika" or "notifikation"
        if (!mod.IsInstalled && Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant();
                // Check for specific FIKA patterns to avoid false positives
                // Pattern must be: starts with "fika", contains "-fika-", "_fika_", or "fika-server"
                bool isFika = folderName.StartsWith("fika") ||
                              folderName.Contains("-fika-") ||
                              folderName.Contains("_fika_") ||
                              folderName.Contains("fika-server") ||
                              folderName.Contains("fikaserver") ||
                              folderName.Equals("fika");

                if (isFika)
                {
                    mod.IsInstalled = true;
                    mod.FolderPath = directory;
                    break;
                }
            }
        }

        if (mod.IsInstalled)
        {
            // FIKA integration is possible but requires special handling
            mod.ConflictLevel = ConflictLevel.Medium;
            mod.ConflictReason = "FIKA multiplayer detected. Keep Starting Gear can work with FIKA " +
                "but requires 'FIKA Integration Mode' to be enabled in settings. " +
                "Gear restoration will only work for the HOST player reliably.";
            // FIX: Use strongly-typed property instead of Dictionary
            mod.RequiresFikaMode = true;
        }

        DetectedMods["fika"] = mod;
    }

    /// <summary>
    /// Scans for Arcade Mode mod.
    /// </summary>
    /// <remarks>
    /// FIX: More specific pattern matching to avoid false positives like
    /// "arcadestyle-weapons" or "retro-arcade-sounds"
    /// </remarks>
    private void ScanForArcadeMode()
    {
        var mod = new DetectedMod
        {
            ModId = "arcademode",
            DisplayName = "Arcade Mode",
            SupportsIntegration = false
        };

        // More specific patterns to avoid false positives
        // "arcade" alone is too broad - matches unrelated mods
        string[] exactPatterns = { "arcademode", "arcade-mode", "arcade_mode" };
        string[] containsPatterns = { "arcade mode", "spt-arcade", "sptarcade" };

        if (Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant()
                    .Replace("[", "").Replace("]", ""); // Remove version brackets

                // Check exact patterns first (more reliable)
                foreach (var pattern in exactPatterns)
                {
                    if (folderName.Contains(pattern))
                    {
                        mod.IsInstalled = true;
                        mod.FolderPath = directory;
                        break;
                    }
                }

                // Check contains patterns if not found
                if (!mod.IsInstalled)
                {
                    foreach (var pattern in containsPatterns)
                    {
                        if (folderName.Contains(pattern))
                        {
                            mod.IsInstalled = true;
                            mod.FolderPath = directory;
                            break;
                        }
                    }
                }

                if (mod.IsInstalled)
                {
                    mod.ConflictLevel = ConflictLevel.Critical;
                    mod.ConflictReason = "Arcade Mode provides the same functionality as Keep Starting Gear " +
                        "(inventory restoration on death). Running both mods together will cause conflicts. " +
                        "Please disable or uninstall one of these mods.";
                    break;
                }
            }
        }

        DetectedMods["arcademode"] = mod;
    }

    /// <summary>
    /// Scans for Never Lose Equipments mod.
    /// </summary>
    private void ScanForNeverLoseEquipments()
    {
        var mod = new DetectedMod
        {
            ModId = "neverloseequipments",
            DisplayName = "Never Lose Equipments",
            SupportsIntegration = false
        };

        string[] patterns = { "neverloseequipment", "never lose equipment", "neverlose" };

        if (Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant();
                foreach (var pattern in patterns)
                {
                    if (folderName.Contains(pattern))
                    {
                        mod.IsInstalled = true;
                        mod.FolderPath = directory;
                        mod.ConflictLevel = ConflictLevel.Critical;
                        mod.ConflictReason = "Never Lose Equipments provides the same functionality as Keep Starting Gear. " +
                            "Running both mods together will cause duplicate restoration or conflicts. " +
                            "Please disable or uninstall one of these mods.";
                        break;
                    }
                }
                if (mod.IsInstalled) break;
            }
        }

        DetectedMods["neverloseequipments"] = mod;
    }

    /// <summary>
    /// Scans for Keep Your Equipment variants.
    /// </summary>
    private void ScanForKeepYourEquipment()
    {
        var mod = new DetectedMod
        {
            ModId = "keepyourequipment",
            DisplayName = "Keep Your Equipment (or variants)",
            SupportsIntegration = false
        };

        // TYPO FIX: "yetanotherkeeequipment" was missing 'p' - corrected to "yetanotherkeepequipment"
        string[] patterns = { "keepyourequipment", "keep your equipment", "keepequipment",
            "yetanotherkeepequipment", "yakye" };

        if (Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant();
                foreach (var pattern in patterns)
                {
                    if (folderName.Contains(pattern))
                    {
                        mod.IsInstalled = true;
                        mod.FolderPath = directory;
                        mod.ConflictLevel = ConflictLevel.Critical;
                        mod.ConflictReason = "Keep Your Equipment provides the same functionality as Keep Starting Gear. " +
                            "Running both mods together will cause duplicate restoration or conflicts. " +
                            "Please disable or uninstall one of these mods.";
                        break;
                    }
                }
                if (mod.IsInstalled) break;
            }
        }

        DetectedMods["keepyourequipment"] = mod;
    }

    /// <summary>
    /// Scans for Fin's Hardcore Options mod.
    /// </summary>
    private void ScanForFinsHardcoreOptions()
    {
        var mod = new DetectedMod
        {
            ModId = "finshardcore",
            DisplayName = "Fin's Hardcore Options",
            SupportsIntegration = false
        };

        // FIX M3: Removed unused patterns array - check uses inline conditions instead

        if (Directory.Exists(_modsDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_modsDirectory))
            {
                string folderName = Path.GetFileName(directory).ToLowerInvariant();
                // More specific check for Fin's mod
                if (folderName.Contains("fin") && folderName.Contains("hardcore"))
                {
                    mod.IsInstalled = true;
                    mod.FolderPath = directory;

                    // This mod DELETES items on death - opposite of us
                    mod.ConflictLevel = ConflictLevel.Critical;
                    mod.ConflictReason = "Fin's Hardcore Options can DELETE stash items on death. " +
                        "This directly conflicts with Keep Starting Gear which RESTORES items. " +
                        "Running both mods together may cause unpredictable behavior. " +
                        "Please disable the 'Delete stash items on death' feature in Fin's mod OR disable Keep Starting Gear.";
                    break;
                }
            }
        }

        DetectedMods["finshardcore"] = mod;
    }

    /// <summary>
    /// Gets a list of all critical conflicts that should block mod loading.
    /// </summary>
    public List<DetectedMod> GetCriticalConflicts()
    {
        return DetectedMods.Values
            .Where(m => m.IsInstalled && m.ConflictLevel == ConflictLevel.Critical)
            .ToList();
    }

    /// <summary>
    /// Gets a list of all significant (high+critical) conflicts.
    /// </summary>
    public List<DetectedMod> GetSignificantConflicts()
    {
        return DetectedMods.Values
            .Where(m => m.IsInstalled && m.ConflictLevel >= ConflictLevel.High)
            .ToList();
    }

    /// <summary>
    /// Checks if FIKA is installed and integration mode should be enabled.
    /// </summary>
    public bool IsFikaInstalled => DetectedMods.TryGetValue("fika", out var fika) && fika.IsInstalled;

    /// <summary>
    /// Checks if SVM is installed with conflicting features enabled.
    /// </summary>
    public bool IsSVMConflicting => DetectedMods.TryGetValue("svm", out var svm) &&
        svm.IsInstalled && svm.ConflictLevel >= ConflictLevel.High;

    /// <summary>
    /// Gets SVM config data if available.
    /// </summary>
    public SVMConfigData? GetSVMConfig()
    {
        // FIX: Use strongly-typed property instead of Dictionary with runtime cast
        if (DetectedMods.TryGetValue("svm", out var svm))
        {
            return svm.SVMConfig;
        }
        return null;
    }
}

/// <summary>
/// Data structure for SVM configuration.
/// </summary>
public class SVMConfigData
{
    public string PresetName { get; set; } = string.Empty;
    public bool RaidsEnabled { get; set; }
    public bool SafeExit { get; set; }
    public bool SaveGearAfterDeath { get; set; }
    public bool SaveQuestItems { get; set; }
    public bool NoRunThrough { get; set; }
}
