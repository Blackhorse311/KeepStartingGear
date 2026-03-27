// ============================================================================
// SessionIdValidator.cs
//
// Shared session ID validation utility. Consolidates the compiled regex
// previously duplicated in SnapshotManager, SnapshotHistory, LoadoutProfiles,
// and SnapshotRestorer (S-2 code review fix).
//
// Session IDs are validated before any file path construction to prevent
// path traversal attacks (SEC-001).
//
// Author: Blackhorse311 & Claude (Anthropic)
// ============================================================================

using System.Text.RegularExpressions;

namespace Blackhorse311.KeepStartingGear.Utilities
{
    /// <summary>
    /// Validates session IDs to prevent path traversal attacks.
    /// Only allows alphanumeric characters, hyphens, and underscores.
    /// </summary>
    public static class SessionIdValidator
    {
        /// <summary>
        /// Compiled regex pattern for valid session IDs.
        /// Prevents path traversal via malicious IDs like "../../../etc/passwd".
        /// </summary>
        private static readonly Regex Pattern =
            new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Maximum allowed session ID length. Prevents file path length issues on Windows (MAX_PATH = 260).
        /// EFT session IDs are typically 24 characters (MongoDB ObjectId format).
        /// </summary>
        private const int MaxSessionIdLength = 128;

        /// <summary>
        /// Validates that a session ID is safe for use in file path construction.
        /// </summary>
        /// <param name="sessionId">The session ID to validate.</param>
        /// <returns>True if the session ID is non-null, non-empty, within length limits, and matches the safe pattern.</returns>
        public static bool IsValid(string sessionId)
        {
            return !string.IsNullOrEmpty(sessionId)
                && sessionId.Length <= MaxSessionIdLength
                && Pattern.IsMatch(sessionId);
        }
    }
}
