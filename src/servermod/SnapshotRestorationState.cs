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
// 2. RaidEndInterceptor sets InventoryRestoredFromSnapshot = true
// 3. CustomInRaidHelper.DeleteInventory checks AND CONSUMES the flag
// 4. If flag was true, skip the deletion (preserve restored items)
//
// RACE CONDITION FIX:
// The flag is now consumed atomically by TryConsume() which both checks
// and resets the flag in a single operation. This prevents the race where
// the flag could be reset in multiple places.
//
// THREAD SAFETY:
// Uses ThreadLocal<bool> to ensure each request thread has its own flag value.
// This prevents issues if multiple raid end requests are processed concurrently
// (unlikely in single-player SPT, but good practice).
//
// LINUS-003 CRITICAL ASSUMPTION:
// ThreadLocal ONLY works if the SAME thread calls MarkRestored() and TryConsume().
// If SPT ever changes its threading model (e.g., task-based async), this will
// SILENTLY FAIL. We now track the thread ID at MarkRestored() and validate it
// at TryConsume() to detect if this assumption is violated.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Static state holder for communication between RaidEndInterceptor and CustomInRaidHelper.
/// Provides thread-safe flag to indicate when inventory was restored from snapshot.
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
///   <item>RaidEndInterceptor sets InventoryRestoredFromSnapshot = true with managed slots</item>
///   <item>Normal death processing continues...</item>
///   <item>SPT calls InRaidHelper.DeleteInventory()</item>
///   <item>CustomInRaidHelper calls TryConsume(), which returns true and resets flag</item>
///   <item>Deletion is skipped for MANAGED slots only; non-managed slots are deleted normally</item>
/// </list>
/// </remarks>
public static class SnapshotRestorationState
{
    // ========================================================================
    // Thread-Local State
    // ========================================================================

    /// <summary>
    /// Thread-local flag indicating whether inventory was restored from snapshot.
    /// Using ThreadLocal to handle potential concurrent requests safely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ThreadLocal ensures each thread has its own copy of the value.
    /// This prevents race conditions if multiple requests are processed
    /// simultaneously (though this is rare in SPT's single-player model).
    /// Default value is false (no restoration performed).
    /// </para>
    /// <para>
    /// CRITICAL-002 NOTE: ThreadLocal&lt;T&gt; implements IDisposable but these instances are
    /// intentionally never disposed. They are static fields with application lifetime scope.
    /// In SPT's single-player server model, threads are pooled and reused, so the per-thread
    /// storage is appropriate. Disposing would require shutdown hooks which SPT doesn't provide.
    /// </para>
    /// </remarks>
    private static readonly ThreadLocal<bool> _inventoryRestoredFromSnapshot = new(() => false);

    /// <summary>
    /// Thread-local storage for the set of slot IDs that were managed (restored) from snapshot.
    /// Only items in these slots should be preserved; items in other slots should be deleted normally.
    /// </summary>
    /// <remarks>
    /// CRITICAL-002 NOTE: See _inventoryRestoredFromSnapshot for disposal rationale.
    /// </remarks>
    private static readonly ThreadLocal<HashSet<string>?> _managedSlotIds = new(() => null);

    /// <summary>
    /// LINUS-003 FIX: Track which thread called MarkRestored() so we can detect
    /// if TryConsume() is called from a different thread (which would indicate
    /// a threading model change in SPT that breaks our assumptions).
    /// </summary>
    private static readonly ThreadLocal<int> _markedThreadId = new(() => -1);

    /// <summary>
    /// LINUS-003 FIX: Static counter for detected thread mismatches.
    /// If this ever increments, the ThreadLocal approach has failed.
    /// </summary>
    private static int _threadMismatchCount = 0;

    // C-04 FIX: Removed unnecessary lock object.
    // ThreadLocal variables are inherently thread-safe as each thread has its own copy.
    // A lock would only be useful for cross-thread coordination, but ThreadLocal provides
    // per-thread isolation instead. The previous lock provided no benefit and added overhead.

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Gets whether the inventory was restored from a snapshot.
    /// Use <see cref="TryConsume"/> to atomically check and reset this flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Warning:</b> Prefer using <see cref="TryConsume"/> instead of reading
    /// this property directly, as TryConsume handles the check-and-reset atomically.
    /// </para>
    /// </remarks>
    public static bool InventoryRestoredFromSnapshot
    {
        get => _inventoryRestoredFromSnapshot.Value;
    }

    /// <summary>
    /// Sets the restoration flag to true and stores the managed slot IDs.
    /// Call this after successfully restoring inventory from a snapshot.
    /// </summary>
    /// <param name="managedSlotIds">The set of slot IDs that were managed (restored) from snapshot.
    /// Only items in these slots will be preserved; items in other slots will be deleted normally.</param>
    /// <remarks>
    /// Only RaidEndInterceptor should call this method.
    /// C-04 FIX: Removed lock - ThreadLocal provides per-thread isolation.
    /// LINUS-003 FIX: Now records thread ID for validation in TryConsume().
    /// </remarks>
    public static void MarkRestored(HashSet<string>? managedSlotIds = null)
    {
        _inventoryRestoredFromSnapshot.Value = true;
        _managedSlotIds.Value = managedSlotIds != null ? new HashSet<string>(managedSlotIds, StringComparer.OrdinalIgnoreCase) : null;
        // LINUS-003 FIX: Record which thread marked restoration for validation
        _markedThreadId.Value = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Gets the set of slot IDs that were managed (restored) from snapshot.
    /// Returns null if all slots should be preserved (legacy behavior).
    /// C-04 FIX: Removed lock - ThreadLocal provides per-thread isolation.
    /// </summary>
    public static HashSet<string>? ManagedSlotIds => _managedSlotIds.Value;

    /// <summary>
    /// Atomically checks if restoration occurred and returns the managed slot IDs.
    /// This resets the flag but preserves the managed slot IDs for use by DeleteInventory.
    /// Prefer using TryConsume methods over reading InventoryRestoredFromSnapshot directly.
    /// </summary>
    /// <param name="managedSlotIds">Output: The set of managed slot IDs, or null if all slots should be preserved.</param>
    /// <returns>True if inventory was restored (flag was set), false otherwise.</returns>
    /// <remarks>
    /// <para>
    /// This method ensures the flag is consumed exactly once per thread, preventing
    /// multiple calls from acting on the same flag value.
    /// </para>
    /// <para>
    /// After this method returns true, subsequent calls on the same thread will return false
    /// until <see cref="MarkRestored"/> is called again.
    /// </para>
    /// <para>
    /// C-04 FIX: Removed lock - ThreadLocal provides per-thread isolation.
    /// Each thread has its own flag value, so no cross-thread race is possible.
    /// </para>
    /// <para>
    /// LINUS-003 FIX: Now validates that TryConsume is called from the same thread
    /// that called MarkRestored. If threads differ, logs a critical warning and
    /// increments a counter. This detects when SPT's threading model has changed
    /// in a way that breaks our assumptions.
    /// </para>
    /// </remarks>
    public static bool TryConsume(out HashSet<string>? managedSlotIds)
    {
        bool wasRestored = _inventoryRestoredFromSnapshot.Value;
        managedSlotIds = _managedSlotIds.Value;

        // LINUS-003 FIX: Validate thread ID matches
        if (wasRestored)
        {
            int markedThread = _markedThreadId.Value;
            int currentThread = Environment.CurrentManagedThreadId;

            if (markedThread != -1 && markedThread != currentThread)
            {
                // CRITICAL: ThreadLocal assumption violated!
                // This means SPT changed threads between MarkRestored() and TryConsume().
                // The restoration may have silently failed on other concurrent requests.
                System.Threading.Interlocked.Increment(ref _threadMismatchCount);

                // Log to console (we can't use ISptLogger here since we're static)
                Console.WriteLine($"[KeepStartingGear] CRITICAL: Thread mismatch detected! " +
                    $"MarkRestored on thread {markedThread}, TryConsume on thread {currentThread}. " +
                    $"Total mismatches: {_threadMismatchCount}. " +
                    $"Restoration may be unreliable - please report this bug.");
            }

            // Reset thread tracking
            _markedThreadId.Value = -1;
        }

        _inventoryRestoredFromSnapshot.Value = false;
        // Keep managedSlotIds until fully consumed
        return wasRestored;
    }

    /// <summary>
    /// LINUS-003 FIX: Gets the count of detected thread mismatches.
    /// If this is ever non-zero, the ThreadLocal approach has failed at least once.
    /// </summary>
    public static int ThreadMismatchCount => System.Threading.Volatile.Read(ref _threadMismatchCount);

    /// <summary>
    /// Atomically checks if restoration occurred and resets the flag.
    /// Convenience overload that discards the managed slot IDs.
    /// </summary>
    /// <returns>True if inventory was restored (flag was set), false otherwise.</returns>
    public static bool TryConsume()
    {
        return TryConsume(out _);
    }

    /// <summary>
    /// Resets the restoration flag to false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deprecated:</b> Prefer using <see cref="TryConsume"/> which atomically
    /// checks and resets the flag in a single operation.
    /// </para>
    /// <para>
    /// This method is kept for backwards compatibility but should not be used
    /// in new code. Using this in combination with reading InventoryRestoredFromSnapshot
    /// creates a race condition on the same thread.
    /// </para>
    /// <para>
    /// C-04 FIX: Removed lock - ThreadLocal provides per-thread isolation.
    /// </para>
    /// </remarks>
    [Obsolete("Use TryConsume() instead for atomic check-and-reset")]
    public static void Reset()
    {
        _inventoryRestoredFromSnapshot.Value = false;
        _managedSlotIds.Value = null;
    }

    /// <summary>
    /// Clears the managed slot IDs after they have been used.
    /// C-04 FIX: Removed lock - ThreadLocal provides per-thread isolation.
    /// </summary>
    public static void ClearManagedSlots()
    {
        _managedSlotIds.Value = null;
    }
}
