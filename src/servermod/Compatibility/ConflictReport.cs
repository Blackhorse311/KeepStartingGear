// ============================================================================
// Keep Starting Gear - Conflict Report Generator
// ============================================================================
// Generates user-friendly conflict reports and notifications.
// Creates both console output and file-based reports.
//
// OUTPUT:
// 1. Server console - Detailed warnings during OnLoad()
// 2. COMPATIBILITY_REPORT.txt - File in mod folder with full details
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

namespace Blackhorse311.KeepStartingGear.Server.Compatibility;

/// <summary>
/// Generates compatibility reports and handles conflict notifications.
/// </summary>
public class ConflictReport
{
    private readonly SimpleLoggerAdapter _logger;
    private readonly ModDetector _detector;
    private readonly string _modFolder;

    // Constants for word wrap width (M3: Magic numbers)
    private const int WordWrapWidth = 55;

    /// <summary>
    /// Creates a new ConflictReport generator.
    /// </summary>
    /// <param name="logger">Logger adapter - must not be null</param>
    /// <param name="detector">Mod detector instance - must not be null</param>
    /// <param name="modFolder">Path to mod folder for report output - must be valid</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null</exception>
    /// <exception cref="ArgumentException">Thrown if modFolder is empty</exception>
    public ConflictReport(SimpleLoggerAdapter logger, ModDetector detector, string modFolder)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));

        if (string.IsNullOrEmpty(modFolder))
        {
            throw new ArgumentException("modFolder cannot be null or empty", nameof(modFolder));
        }
        _modFolder = modFolder;
    }

    /// <summary>
    /// Generates and outputs the full compatibility report.
    /// </summary>
    /// <returns>True if mod should continue loading, false if critical conflict blocks loading</returns>
    public bool GenerateReport()
    {
        var installedMods = _detector.DetectedMods.Values.Where(m => m.IsInstalled).ToList();

        if (installedMods.Count == 0)
        {
            _logger.Info($"{Constants.LogPrefix} ============================================");
            _logger.Info($"{Constants.LogPrefix} Compatibility Check: PASSED");
            _logger.Info($"{Constants.LogPrefix} No conflicting mods detected.");
            _logger.Info($"{Constants.LogPrefix} ============================================");
            return true;
        }

        // Check for critical conflicts first
        var criticalConflicts = _detector.GetCriticalConflicts();
        if (criticalConflicts.Count > 0)
        {
            OutputCriticalConflictReport(criticalConflicts);
            WriteReportFile(installedMods, criticalConflicts);
            return false; // Block loading
        }

        // Check for significant conflicts
        var significantConflicts = _detector.GetSignificantConflicts();
        if (significantConflicts.Count > 0)
        {
            OutputSignificantConflictReport(significantConflicts);
            WriteReportFile(installedMods, significantConflicts);
            return true; // Continue with warnings
        }

        // Minor conflicts or compatible mods
        OutputCompatibleReport(installedMods);
        WriteReportFile(installedMods, new List<DetectedMod>());
        return true;
    }

    /// <summary>
    /// Outputs a critical conflict report that blocks mod loading.
    /// </summary>
    private void OutputCriticalConflictReport(List<DetectedMod> criticalConflicts)
    {
        _logger.Error($"{Constants.LogPrefix} ============================================");
        _logger.Error($"{Constants.LogPrefix} CRITICAL CONFLICT DETECTED - MOD DISABLED");
        _logger.Error($"{Constants.LogPrefix} ============================================");
        _logger.Error($"{Constants.LogPrefix}");
        _logger.Error($"{Constants.LogPrefix} Keep Starting Gear cannot run alongside the");
        _logger.Error($"{Constants.LogPrefix} following mod(s) due to direct conflicts:");
        _logger.Error($"{Constants.LogPrefix}");

        foreach (var mod in criticalConflicts)
        {
            _logger.Error($"{Constants.LogPrefix} >>> {mod.DisplayName}");
            _logger.Error($"{Constants.LogPrefix}     Location: {mod.FolderPath}");
            _logger.Error($"{Constants.LogPrefix}");

            // Word-wrap the conflict reason
            var lines = WordWrap(mod.ConflictReason, WordWrapWidth);
            foreach (var line in lines)
            {
                _logger.Error($"{Constants.LogPrefix}     {line}");
            }
            _logger.Error($"{Constants.LogPrefix}");
        }

        _logger.Error($"{Constants.LogPrefix} ============================================");
        _logger.Error($"{Constants.LogPrefix} TO RESOLVE:");
        _logger.Error($"{Constants.LogPrefix} 1. Remove/disable the conflicting mod(s), OR");
        _logger.Error($"{Constants.LogPrefix} 2. Remove/disable Keep Starting Gear");
        _logger.Error($"{Constants.LogPrefix} ============================================");
        _logger.Error($"{Constants.LogPrefix}");
        _logger.Error($"{Constants.LogPrefix} See COMPATIBILITY_REPORT.txt in this mod's folder");
        _logger.Error($"{Constants.LogPrefix} for detailed information.");
    }

    /// <summary>
    /// Outputs a significant conflict report with warnings.
    /// </summary>
    /// <param name="significantConflicts">List of mods with significant conflicts to report</param>
    // FIX L1: Removed unused allInstalled parameter - method uses _detector directly
    private void OutputSignificantConflictReport(List<DetectedMod> significantConflicts)
    {
        _logger.Warning($"{Constants.LogPrefix} ============================================");
        _logger.Warning($"{Constants.LogPrefix} COMPATIBILITY WARNINGS DETECTED");
        _logger.Warning($"{Constants.LogPrefix} ============================================");
        _logger.Warning($"{Constants.LogPrefix}");

        foreach (var mod in significantConflicts)
        {
            string levelStr = mod.ConflictLevel == ConflictLevel.High ? "HIGH" : "MEDIUM";
            _logger.Warning($"{Constants.LogPrefix} [{levelStr}] {mod.DisplayName}");

            var lines = WordWrap(mod.ConflictReason, WordWrapWidth);
            foreach (var line in lines)
            {
                _logger.Warning($"{Constants.LogPrefix}     {line}");
            }
            _logger.Warning($"{Constants.LogPrefix}");
        }

        // Check for FIKA specifically
        if (_detector.IsFikaInstalled)
        {
            _logger.Info($"{Constants.LogPrefix} ============================================");
            _logger.Info($"{Constants.LogPrefix} FIKA MULTIPLAYER DETECTED");
            _logger.Info($"{Constants.LogPrefix} ============================================");
            _logger.Info($"{Constants.LogPrefix} Keep Starting Gear has FIKA integration support!");
            _logger.Info($"{Constants.LogPrefix} Enable 'FIKA Integration Mode' in the client");
            _logger.Info($"{Constants.LogPrefix} settings (F12 -> Keep Starting Gear).");
            _logger.Info($"{Constants.LogPrefix}");
            _logger.Info($"{Constants.LogPrefix} NOTE: Gear restoration in FIKA raids works");
            _logger.Info($"{Constants.LogPrefix} reliably for the HOST player. Client players");
            _logger.Info($"{Constants.LogPrefix} may have limited functionality.");
            _logger.Info($"{Constants.LogPrefix} ============================================");
        }

        // Check for SVM specifically
        if (_detector.IsSVMConflicting)
        {
            var svmConfig = _detector.GetSVMConfig();
            _logger.Info($"{Constants.LogPrefix} ============================================");
            _logger.Info($"{Constants.LogPrefix} SVM CONFIGURATION CONFLICT");
            _logger.Info($"{Constants.LogPrefix} ============================================");
            _logger.Info($"{Constants.LogPrefix} SVM Preset: {svmConfig?.PresetName ?? "Unknown"}");
            _logger.Info($"{Constants.LogPrefix} SaveGearAfterDeath: {svmConfig?.SaveGearAfterDeath ?? false}");
            _logger.Info($"{Constants.LogPrefix} SafeExit: {svmConfig?.SafeExit ?? false}");
            _logger.Info($"{Constants.LogPrefix}");
            _logger.Info($"{Constants.LogPrefix} TO RESOLVE, choose one option:");
            _logger.Info($"{Constants.LogPrefix} Option A: Disable SVM's conflicting features");
            _logger.Info($"{Constants.LogPrefix}   - Set SaveGearAfterDeath = false in SVM");
            _logger.Info($"{Constants.LogPrefix}   - Set SafeExit = false in SVM");
            _logger.Info($"{Constants.LogPrefix}");
            _logger.Info($"{Constants.LogPrefix} Option B: Let SVM handle gear protection");
            _logger.Info($"{Constants.LogPrefix}   - In KSG client settings (F12), set");
            _logger.Info($"{Constants.LogPrefix}     'SVM Priority' to 'Defer to SVM'");
            _logger.Info($"{Constants.LogPrefix} ============================================");
        }

        _logger.Warning($"{Constants.LogPrefix}");
        _logger.Warning($"{Constants.LogPrefix} Keep Starting Gear will continue loading.");
        _logger.Warning($"{Constants.LogPrefix} See COMPATIBILITY_REPORT.txt for details.");
    }

    /// <summary>
    /// Outputs a compatible mods report.
    /// </summary>
    private void OutputCompatibleReport(List<DetectedMod> installedMods)
    {
        _logger.Info($"{Constants.LogPrefix} ============================================");
        _logger.Info($"{Constants.LogPrefix} Compatibility Check: PASSED");
        _logger.Info($"{Constants.LogPrefix} ============================================");
        _logger.Info($"{Constants.LogPrefix} Detected mods (no significant conflicts):");

        foreach (var mod in installedMods)
        {
            string status = mod.ConflictLevel switch
            {
                ConflictLevel.None => "[OK]",
                ConflictLevel.Low => "[MINOR]",
                _ => "[?]"
            };
            _logger.Info($"{Constants.LogPrefix}   {status} {mod.DisplayName}");
        }

        _logger.Info($"{Constants.LogPrefix} ============================================");
    }

    /// <summary>
    /// Writes a detailed compatibility report to a file.
    /// </summary>
    private void WriteReportFile(List<DetectedMod> installedMods, List<DetectedMod> conflicts)
    {
        try
        {
            string reportPath = Path.Combine(_modFolder, "COMPATIBILITY_REPORT.txt");
            // FIX M7: Use UTC for consistency with other timestamps in the codebase
            var timestamp = DateTime.UtcNow;
            // Estimate capacity to avoid list reallocations (M11)
            var lines = new List<string>(64)
            {
                "================================================================================",
                "KEEP STARTING GEAR - COMPATIBILITY REPORT",
                $"Generated: {timestamp:yyyy-MM-dd HH:mm:ss} UTC",
                $"Mod Version: {Constants.ModVersion}",
                "================================================================================",
                ""
            };

            if (conflicts.Count > 0 && conflicts.Any(c => c.ConflictLevel == ConflictLevel.Critical))
            {
                lines.Add("STATUS: CRITICAL CONFLICTS DETECTED - MOD DISABLED");
            }
            else if (conflicts.Count > 0)
            {
                lines.Add("STATUS: WARNINGS - MOD RUNNING WITH POTENTIAL ISSUES");
            }
            else
            {
                lines.Add("STATUS: OK - NO SIGNIFICANT CONFLICTS");
            }

            lines.Add("");
            lines.Add("================================================================================");
            lines.Add("DETECTED MODS");
            lines.Add("================================================================================");
            lines.Add("");

            if (installedMods.Count == 0)
            {
                lines.Add("No potentially conflicting mods detected.");
            }
            else
            {
                foreach (var mod in installedMods)
                {
                    lines.Add($"--- {mod.DisplayName} ---");
                    lines.Add($"  Conflict Level: {mod.ConflictLevel}");
                    lines.Add($"  Location: {mod.FolderPath}");
                    lines.Add($"  Supports Integration: {mod.SupportsIntegration}");
                    lines.Add($"  Reason: {mod.ConflictReason}");
                    lines.Add("");
                }
            }

            // Add SVM-specific details if detected
            var svmConfig = _detector.GetSVMConfig();
            if (svmConfig != null)
            {
                lines.Add("================================================================================");
                lines.Add("SVM CONFIGURATION DETAILS");
                lines.Add("================================================================================");
                lines.Add("");
                lines.Add($"  Active Preset: {svmConfig.PresetName}");
                lines.Add($"  Raids Section Enabled: {svmConfig.RaidsEnabled}");
                lines.Add($"  SaveGearAfterDeath: {svmConfig.SaveGearAfterDeath}");
                lines.Add($"  SafeExit: {svmConfig.SafeExit}");
                lines.Add($"  SaveQuestItems: {svmConfig.SaveQuestItems}");
                lines.Add($"  NoRunThrough: {svmConfig.NoRunThrough}");
                lines.Add("");
            }

            // Add FIKA-specific details if detected
            if (_detector.IsFikaInstalled)
            {
                lines.Add("================================================================================");
                lines.Add("FIKA INTEGRATION");
                lines.Add("================================================================================");
                lines.Add("");
                lines.Add("FIKA multiplayer mod is installed. To use Keep Starting Gear with FIKA:");
                lines.Add("");
                lines.Add("1. Enable 'FIKA Integration Mode' in client settings (F12)");
                lines.Add("2. Gear restoration works best for HOST players");
                lines.Add("3. Client players may have limited functionality");
                lines.Add("");
                lines.Add("How it works:");
                lines.Add("- When FIKA mode is enabled, KSG hooks into FIKA's FikaGameEndedEvent");
                lines.Add("- Snapshot restoration occurs before FIKA's SavePlayer() completes");
                lines.Add("- The restored inventory is then saved normally by FIKA");
                lines.Add("");
            }

            lines.Add("================================================================================");
            lines.Add("RESOLUTION OPTIONS");
            lines.Add("================================================================================");
            lines.Add("");

            if (conflicts.Any(c => c.ConflictLevel == ConflictLevel.Critical))
            {
                lines.Add("CRITICAL CONFLICTS must be resolved before Keep Starting Gear can run:");
                lines.Add("");
                lines.Add("Option 1: Remove/disable the conflicting mod(s)");
                lines.Add("Option 2: Remove/disable Keep Starting Gear");
                lines.Add("");
            }

            if (_detector.IsSVMConflicting)
            {
                lines.Add("SVM CONFLICT can be resolved by:");
                lines.Add("");
                lines.Add("Option A: Disable conflicting features in SVM");
                lines.Add("  - Open SVM's Greed configuration tool");
                lines.Add("  - Set Raids > SaveGearAfterDeath = false");
                lines.Add("  - Set Raids > SafeExit = false");
                lines.Add("  - Save and restart the server");
                lines.Add("");
                lines.Add("Option B: Let SVM handle gear protection");
                lines.Add("  - In game, press F12 to open Configuration Manager");
                lines.Add("  - Find 'Keep Starting Gear' settings");
                lines.Add("  - Set 'SVM Priority' to 'Defer to SVM'");
                lines.Add("  - Keep Starting Gear will skip restoration when SVM is active");
                lines.Add("");
            }

            lines.Add("================================================================================");
            lines.Add("SUPPORT");
            lines.Add("================================================================================");
            lines.Add("");
            lines.Add("If you encounter issues:");
            lines.Add("1. Check the BepInEx log: BepInEx/LogOutput.log");
            lines.Add("2. Check the SPT server log: SPT/user/logs/");
            lines.Add("3. Report issues: https://github.com/Blackhorse311/KeepStartingGear/issues");
            lines.Add("");
            lines.Add("Include this COMPATIBILITY_REPORT.txt with any bug reports.");
            lines.Add("");

            File.WriteAllLines(reportPath, lines);
            _logger.Debug($"{Constants.LogPrefix} Wrote compatibility report to: {reportPath}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"{Constants.LogPrefix} Failed to write compatibility report: {ex.Message}");
        }
    }

    /// <summary>
    /// Word-wraps text to specified width.
    /// </summary>
    /// <remarks>
    /// FIX M5: Uses StringBuilder instead of string concatenation to avoid O(n²) performance.
    /// </remarks>
    private static List<string> WordWrap(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        var lines = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // FIX: Use StringBuilder instead of string concatenation in loop
        var currentLine = new System.Text.StringBuilder(maxWidth);

        foreach (var word in words)
        {
            // Check if adding this word would exceed max width
            int newLength = currentLine.Length + (currentLine.Length > 0 ? 1 : 0) + word.Length;

            if (newLength > maxWidth && currentLine.Length > 0)
            {
                // Current line is full, start a new one
                lines.Add(currentLine.ToString());
                currentLine.Clear();
            }

            // Add space separator if not at start of line
            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
            }
            currentLine.Append(word);
        }

        // Don't forget the last line
        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines;
    }
}
