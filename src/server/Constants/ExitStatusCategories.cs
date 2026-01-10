// ============================================================================
// Keep Starting Gear - Exit Status Categories
// ============================================================================
// Centralized categorization of EFT exit statuses for consistent handling
// throughout the mod.
//
// IMPORTANT: If BSG adds new ExitStatus values, update this file to ensure
// they are properly categorized.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using EFT;

namespace Blackhorse311.KeepStartingGear.Constants;

/// <summary>
/// Categorization result for an ExitStatus.
/// </summary>
public enum ExitCategory
{
    /// <summary>Player died or failed to extract - restoration needed</summary>
    Death,

    /// <summary>Player successfully extracted - clear snapshot</summary>
    Extraction,

    /// <summary>Unknown or unhandled status - log warning</summary>
    Unknown
}

/// <summary>
/// Helper class for categorizing EFT ExitStatus values.
/// Provides a single source of truth for how each exit status should be handled.
/// </summary>
public static class ExitStatusCategories
{
    /// <summary>
    /// Categorizes an ExitStatus into Death, Extraction, or Unknown.
    /// </summary>
    /// <param name="status">The exit status to categorize</param>
    /// <returns>The category this exit status belongs to</returns>
    /// <remarks>
    /// <para>Death statuses (trigger restoration):</para>
    /// <list type="bullet">
    ///   <item>Killed - Player was killed by enemy</item>
    ///   <item>MissingInAction - Raid timer expired</item>
    ///   <item>Left - Player disconnected/left raid</item>
    /// </list>
    /// <para>Extraction statuses (clear snapshot):</para>
    /// <list type="bullet">
    ///   <item>Survived - Normal extraction</item>
    ///   <item>Runner - Run-through extraction (not enough XP)</item>
    ///   <item>Transit - Car extract or map transfer</item>
    /// </list>
    /// </remarks>
    public static ExitCategory Categorize(ExitStatus status)
    {
        return status switch
        {
            // Death statuses - player lost gear
            ExitStatus.Killed => ExitCategory.Death,
            ExitStatus.MissingInAction => ExitCategory.Death,
            ExitStatus.Left => ExitCategory.Death,

            // Extraction statuses - player kept gear
            ExitStatus.Survived => ExitCategory.Extraction,
            ExitStatus.Runner => ExitCategory.Extraction,
            ExitStatus.Transit => ExitCategory.Extraction,

            // Unknown status - log and don't take action
            _ => ExitCategory.Unknown
        };
    }

    /// <summary>
    /// Checks if the exit status represents a death (gear loss).
    /// </summary>
    public static bool IsDeath(ExitStatus status) => Categorize(status) == ExitCategory.Death;

    /// <summary>
    /// Checks if the exit status represents a successful extraction.
    /// </summary>
    public static bool IsExtraction(ExitStatus status) => Categorize(status) == ExitCategory.Extraction;

    /// <summary>
    /// Gets a human-readable description of why this exit status is categorized as it is.
    /// Useful for logging.
    /// </summary>
    public static string GetDescription(ExitStatus status)
    {
        return status switch
        {
            ExitStatus.Killed => "Player was killed by enemy",
            ExitStatus.MissingInAction => "Raid timer expired (MIA)",
            ExitStatus.Left => "Player disconnected or left raid",
            ExitStatus.Survived => "Player extracted successfully",
            ExitStatus.Runner => "Player extracted (run-through)",
            ExitStatus.Transit => "Player used transit extract (car/transfer)",
            _ => $"Unknown exit status: {status} (value: {(int)status})"
        };
    }
}
