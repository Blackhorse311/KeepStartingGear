// ============================================================================
// Keep Starting Gear - Compatibility Manager
// ============================================================================
// Central manager for all compatibility features.
// Coordinates mod detection, conflict reporting, and integration modes.
//
// RESPONSIBILITIES:
// 1. Initialize and run mod detection at startup
// 2. Generate and display conflict reports
// 3. Determine operational mode based on detected mods
// 4. Provide integration hooks for FIKA and other mods
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace Blackhorse311.KeepStartingGear.Server.Compatibility;

/// <summary>
/// Defines how Keep Starting Gear should operate based on detected mods.
/// </summary>
public enum OperationalMode
{
    /// <summary>
    /// Normal standalone operation - no special handling needed.
    /// </summary>
    Standalone,

    /// <summary>
    /// FIKA integration mode - hook into FIKA events for multiplayer support.
    /// </summary>
    FikaIntegration,

    /// <summary>
    /// SVM deference mode - let SVM handle gear protection, KSG only logs.
    /// </summary>
    DeferToSVM,

    /// <summary>
    /// Disabled due to critical conflict with another mod.
    /// </summary>
    Disabled
}

// NOTE: SVMPriorityMode enum has been moved to SharedTypes.cs to avoid
// duplication with the client-side Settings.cs SVMPriorityOption enum.
// The server now uses int values (0=KSGPriority, 1=DeferToSVM, 2=AllowBoth)
// which can be easily converted from either enum type.

/// <summary>
/// Central manager for compatibility features.
/// </summary>
[Injectable]
public class CompatibilityManager
{
    private readonly ISptLogger<CompatibilityManager> _logger;
    private ModDetector? _detector;
    private ConflictReport? _reporter;

    /// <summary>
    /// Current operational mode based on detected mods.
    /// </summary>
    public OperationalMode CurrentMode { get; private set; } = OperationalMode.Standalone;

    /// <summary>
    /// User preference for SVM conflict handling.
    /// Uses int to allow conversion from client's SVMPriorityOption enum.
    /// Values: 0=KSGPriority, 1=DeferToSVM, 2=AllowBoth
    /// </summary>
    public int SVMPriority { get; set; } = 0; // Default: KSGPriority

    /// <summary>
    /// Whether FIKA integration mode is enabled by user.
    /// This will be configurable via client settings.
    /// </summary>
    public bool FikaIntegrationEnabled { get; set; } = true;

    /// <summary>
    /// The mod detector instance after initialization.
    /// </summary>
    public ModDetector? Detector => _detector;

    /// <summary>
    /// Whether mod should proceed with normal operation.
    /// </summary>
    public bool ShouldRunNormally => CurrentMode != OperationalMode.Disabled &&
                                      CurrentMode != OperationalMode.DeferToSVM;

    /// <summary>
    /// Whether to use FIKA integration hooks.
    /// </summary>
    public bool UseFikaIntegration => CurrentMode == OperationalMode.FikaIntegration;

    /// <summary>
    /// Whether mod is completely disabled due to conflicts.
    /// </summary>
    public bool IsDisabled => CurrentMode == OperationalMode.Disabled;

    /// <summary>
    /// Creates a new CompatibilityManager.
    /// </summary>
    /// <param name="logger">Logger instance - must not be null</param>
    /// <exception cref="ArgumentNullException">Thrown if logger is null</exception>
    public CompatibilityManager(ISptLogger<CompatibilityManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger),
            "Logger cannot be null - CompatibilityManager requires logging capability");
    }

    /// <summary>
    /// Initializes compatibility checking and determines operational mode.
    /// Should be called during mod OnLoad().
    /// </summary>
    /// <param name="serverModPath">Path to this mod's server DLL</param>
    /// <returns>True if mod should continue loading, false if blocked by critical conflict</returns>
    /// <remarks>
    /// CRITICAL: If compatibility check fails due to path issues or exceptions,
    /// the mod is DISABLED rather than continuing blindly. This prevents
    /// running alongside undetected conflicting mods.
    /// </remarks>
    public bool Initialize(string serverModPath)
    {
        // Validate input - fail fast on bad input
        if (string.IsNullOrEmpty(serverModPath))
        {
            _logger.Error($"{Constants.LogPrefix} CRITICAL: serverModPath is null or empty");
            _logger.Error($"{Constants.LogPrefix} Cannot perform compatibility check - MOD DISABLED");
            CurrentMode = OperationalMode.Disabled;
            return false;
        }

        try
        {
            string? modFolder = Path.GetDirectoryName(serverModPath);

            // CRITICAL FIX: Fail fast instead of continuing with wrong path
            if (string.IsNullOrEmpty(modFolder))
            {
                _logger.Error($"{Constants.LogPrefix} CRITICAL: Could not determine mod folder from path: {serverModPath}");
                _logger.Error($"{Constants.LogPrefix} Cannot scan for conflicts - MOD DISABLED for safety");
                CurrentMode = OperationalMode.Disabled;
                return false;
            }

            // Verify the directory actually exists
            if (!Directory.Exists(modFolder))
            {
                _logger.Error($"{Constants.LogPrefix} CRITICAL: Mod folder does not exist: {modFolder}");
                _logger.Error($"{Constants.LogPrefix} Cannot scan for conflicts - MOD DISABLED for safety");
                CurrentMode = OperationalMode.Disabled;
                return false;
            }

            // Create a simple logger adapter for the detector and reporter
            var logAdapter = new SimpleLoggerAdapter(
                msg => _logger.Debug(msg),
                msg => _logger.Info(msg),
                msg => _logger.Warning(msg),
                msg => _logger.Error(msg)
            );

            // Create detector and scan for mods
            _detector = new ModDetector(logAdapter, serverModPath);
            _detector.ScanForMods();

            // Create reporter and generate report
            _reporter = new ConflictReport(logAdapter, _detector, modFolder);

            bool shouldContinue = _reporter.GenerateReport();

            if (!shouldContinue)
            {
                CurrentMode = OperationalMode.Disabled;
                return false;
            }

            // Determine operational mode
            DetermineOperationalMode();

            return true;
        }
        catch (IOException ioEx)
        {
            // File system errors - cannot safely determine conflicts
            _logger.Error($"{Constants.LogPrefix} CRITICAL: IO error during compatibility check: {ioEx.Message}");
            _logger.Error($"{Constants.LogPrefix} Cannot verify mod conflicts - MOD DISABLED for safety");
            CurrentMode = OperationalMode.Disabled;
            return false;
        }
        catch (UnauthorizedAccessException accessEx)
        {
            // Permission errors - cannot read mod folders
            _logger.Error($"{Constants.LogPrefix} CRITICAL: Access denied during compatibility check: {accessEx.Message}");
            _logger.Error($"{Constants.LogPrefix} Cannot verify mod conflicts - MOD DISABLED for safety");
            CurrentMode = OperationalMode.Disabled;
            return false;
        }
        catch (Exception ex)
        {
            // CRITICAL FIX: Don't swallow exceptions - disable mod if we can't verify conflicts
            _logger.Error($"{Constants.LogPrefix} CRITICAL: Compatibility check failed unexpectedly: {ex.Message}");
            _logger.Error($"{Constants.LogPrefix} Stack trace: {ex.StackTrace}");
            _logger.Error($"{Constants.LogPrefix} Cannot verify mod conflicts - MOD DISABLED for safety");
            _logger.Error($"{Constants.LogPrefix} If you believe this is a bug, please report it with the above error.");
            CurrentMode = OperationalMode.Disabled;
            return false;
        }
    }

    /// <summary>
    /// Determines the operational mode based on detected mods and user preferences.
    /// </summary>
    private void DetermineOperationalMode()
    {
        // Check for FIKA first
        if (_detector?.IsFikaInstalled == true && FikaIntegrationEnabled)
        {
            CurrentMode = OperationalMode.FikaIntegration;
            _logger.Info($"{Constants.LogPrefix} Operational Mode: FIKA Integration");
            return;
        }

        // Check for SVM conflict
        // SVMPriority values: 0=KSGPriority, 1=DeferToSVM, 2=AllowBoth
        if (_detector?.IsSVMConflicting == true)
        {
            switch (SVMPriority)
            {
                case 1: // DeferToSVM
                    CurrentMode = OperationalMode.DeferToSVM;
                    _logger.Info($"{Constants.LogPrefix} Operational Mode: Deferring to SVM");
                    return;

                case 2: // AllowBoth
                    _logger.Warning($"{Constants.LogPrefix} Running alongside SVM with AllowBoth mode.");
                    _logger.Warning($"{Constants.LogPrefix} Behavior may be unpredictable.");
                    break;

                case 0: // KSGPriority
                default:
                    _logger.Warning($"{Constants.LogPrefix} KSG taking priority over SVM.");
                    _logger.Warning($"{Constants.LogPrefix} Consider disabling SVM's SaveGearAfterDeath feature.");
                    break;
            }
        }

        CurrentMode = OperationalMode.Standalone;
        _logger.Info($"{Constants.LogPrefix} Operational Mode: Standalone");
    }

    /// <summary>
    /// Checks if restoration should proceed for this raid.
    /// Call this before attempting to restore inventory.
    /// </summary>
    /// <returns>True if restoration should proceed, false if skipped</returns>
    public bool ShouldRestoreInventory()
    {
        switch (CurrentMode)
        {
            case OperationalMode.Disabled:
                _logger.Debug($"{Constants.LogPrefix} Restoration skipped: Mod disabled due to conflicts");
                return false;

            case OperationalMode.DeferToSVM:
                _logger.Debug($"{Constants.LogPrefix} Restoration skipped: Deferring to SVM");
                return false;

            case OperationalMode.FikaIntegration:
                // In FIKA mode, restoration is handled via FIKA events
                // This method returns true but actual restoration happens differently
                return true;

            case OperationalMode.Standalone:
            default:
                return true;
        }
    }

    /// <summary>
    /// Gets a summary of the current compatibility status for logging.
    /// </summary>
    public string GetStatusSummary()
    {
        var parts = new List<string>
        {
            $"Mode: {CurrentMode}"
        };

        if (_detector != null)
        {
            if (_detector.IsFikaInstalled)
                parts.Add("FIKA: Detected");

            var svmConfig = _detector.GetSVMConfig();
            if (svmConfig != null)
            {
                parts.Add($"SVM: {svmConfig.PresetName}");
                if (svmConfig.SaveGearAfterDeath)
                    parts.Add("SVM-SaveGear: ON");
            }
        }

        return string.Join(" | ", parts);
    }
}
