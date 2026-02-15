// ============================================================================
// Keep Starting Gear - Snapshot Restoration State
// ============================================================================
// This static class provides thread-safe communication between the
// RaidEndInterceptor and CustomInRaidHelper components.
//
// PROBLEM BEING SOLVED:
// When a player dies, SPT normally deletes their equipment. We intercept this
// by restoring from a snapshot, but we need to prevent the normal deletion
// from happening AFTER we restore the items.
//
// SOLUTION:
// 1. RaidEndInterceptor restores inventory from snapshot
// 2. RaidEndInterceptor calls MarkRestored(sessionId, managedSlotIds)
// 3. CustomInRaidHelper.DeleteInventory calls TryConsume(sessionId, ...)
// 4. If entry found, skip deletion for managed slots
//
// CRITICAL FIX (v2.0.5):
// Previous versions used ThreadLocal<bool> for state communication. This
// SILENTLY FAILS if SPT dispatches EndLocalRaid and DeleteInventory on
// different threads. The ThreadLocal flag set on Thread A is invisible on
// Thread B, causing TryConsume to return false. The fallback TryRestore
// also fails because the snapshot file was already deleted by the
// interceptor. Result: base.DeleteInventory() runs and wipes all equipment.
//
// FIX: Replace ThreadLocal with ConcurrentDictionary keyed by session ID.
// Both RaidEndInterceptor and CustomInRaidHelper already have access to
// the session ID, making this a clean and robust solution.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Collections.Concurrent;

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Static state holder for communication between RaidEndInterceptor and CustomInRaidHelper.
/// Uses session-ID-keyed ConcurrentDictionary for thread-safe state communication.
/// </summary>
/// <remarks>
/// <para>
/// This class solves a timing problem: When we restore inventory from a snapshot,
/// we need to prevent the normal inventory deletion that happens on death.
/// We can't simply not call the base method because we want the rest of the
/// death processing to proceed normally.
/// </para>
/// <para>
/// <b>Flow:</b>
/// </para>
/// <list type="number">
///   <item>Player dies -> RaidEndInterceptor.EndLocalRaid() is called</item>
///   <item>RaidEndInterceptor finds snapshot and restores inventory</item>
///   <item>RaidEndInterceptor calls MarkRestored(sessionId, managedSlotIds)</item>
///   <item>Normal death processing continues...</item>
///   <item>SPT calls InRaidHelper.DeleteInventory()</item>
///   <item>CustomInRaidHelper calls TryConsume(sessionId, ...) which returns true and removes the entry</item>
///   <item>Deletion is skipped for MANAGED slots only; non-managed slots are deleted normally</item>
/// </list>
/// </remarks>
public static class SnapshotRestorationState
{
    // ========================================================================
    // Inner Types
    // ========================================================================

    /// <summary>
    /// Holds the restoration data for a single session.
    /// </summary>
    private sealed class RestorationData
    {
        /// <summary>The set of managed slot IDs, or null for legacy behavior.</summary>
        public HashSet<string>? ManagedSlotIds { get; }

        /// <summary>When this entry was created (for staleness detection).</summary>
        public DateTime CreatedAt { get; }

        public RestorationData(HashSet<string>? managedSlotIds)
        {
            ManagedSlotIds = managedSlotIds != null
                ? new HashSet<string>(managedSlotIds, StringComparer.OrdinalIgnoreCase)
                : null;
            CreatedAt = DateTime.UtcNow;
        }
    }

    // ========================================================================
    // State
    // ========================================================================

    /// <summary>
    /// Thread-safe dictionary mapping session IDs to restoration data.
    /// Entries are added by RaidEndInterceptor and consumed by CustomInRaidHelper.
    /// </summary>
    private static readonly ConcurrentDictionary<string, RestorationData> _restorationStates
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maximum age for restoration entries before they are considered stale.
    /// Prevents memory leaks if TryConsume is never called (e.g., crash during processing).
    /// </summary>
    private static readonly TimeSpan StaleEntryTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Tracks when we last ran stale entry cleanup to avoid running it on every call.
    /// </summary>
    private static DateTime _lastCleanupTime = DateTime.UtcNow;

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Marks that restoration was performed for the given session.
    /// Call this after successfully restoring inventory from a snapshot.
    /// </summary>
    /// <param name="sessionId">The session/profile ID.</param>
    /// <param name="managedSlotIds">The set of slot IDs that were managed (restored).
    /// Null means legacy behavior (preserve all). Empty set means no protection.</param>
    public static void MarkRestored(string sessionId, HashSet<string>? managedSlotIds = null)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        _restorationStates[sessionId] = new RestorationData(managedSlotIds);

        // Opportunistically clean stale entries
        CleanStaleEntries();
    }

    /// <summary>
    /// Atomically checks if restoration occurred for the given session and consumes the state.
    /// The entry is removed on success, preventing double-consume.
    /// </summary>
    /// <param name="sessionId">The session/profile ID.</param>
    /// <param name="managedSlotIds">Output: The set of managed slot IDs, or null for legacy behavior.</param>
    /// <returns>True if inventory was restored for this session, false otherwise.</returns>
    public static bool TryConsume(string sessionId, out HashSet<string>? managedSlotIds)
    {
        managedSlotIds = null;

        if (string.IsNullOrEmpty(sessionId))
            return false;

        if (_restorationStates.TryRemove(sessionId, out var data))
        {
            managedSlotIds = data.ManagedSlotIds;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convenience overload that discards the managed slot IDs.
    /// </summary>
    /// <param name="sessionId">The session/profile ID.</param>
    /// <returns>True if inventory was restored for this session, false otherwise.</returns>
    public static bool TryConsume(string sessionId)
    {
        return TryConsume(sessionId, out _);
    }

    /// <summary>
    /// Removes any pending restoration state for the given session.
    /// Call this to clean up on error paths.
    /// </summary>
    /// <param name="sessionId">The session/profile ID.</param>
    public static void Clear(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _restorationStates.TryRemove(sessionId, out _);
        }
    }

    // ========================================================================
    // Maintenance
    // ========================================================================

    /// <summary>
    /// Removes entries older than StaleEntryTimeout to prevent memory leaks.
    /// Called opportunistically during MarkRestored, rate-limited to once per minute.
    /// </summary>
    private static void CleanStaleEntries()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanupTime).TotalMinutes < 1)
            return;

        _lastCleanupTime = now;

        var cutoff = now - StaleEntryTimeout;
        foreach (var kvp in _restorationStates)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                _restorationStates.TryRemove(kvp.Key, out _);
            }
        }
    }
}
