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
///   <item>RaidEndInterceptor sets InventoryRestoredFromSnapshot = true</item>
///   <item>Normal death processing continues...</item>
///   <item>SPT calls InRaidHelper.DeleteInventory()</item>
///   <item>CustomInRaidHelper calls TryConsume(), which returns true and resets flag</item>
///   <item>Deletion is skipped</item>
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
    /// ThreadLocal ensures each thread has its own copy of the value.
    /// This prevents race conditions if multiple requests are processed
    /// simultaneously (though this is rare in SPT's single-player model).
    /// Default value is false (no restoration performed).
    /// </remarks>
    private static readonly ThreadLocal<bool> _inventoryRestoredFromSnapshot = new(() => false);

    /// <summary>
    /// Lock object for atomic operations (belt-and-suspenders with ThreadLocal).
    /// </summary>
    private static readonly object _lock = new();

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
    /// Sets the restoration flag to true.
    /// Call this after successfully restoring inventory from a snapshot.
    /// </summary>
    /// <remarks>
    /// Only RaidEndInterceptor should call this method.
    /// </remarks>
    public static void MarkRestored()
    {
        lock (_lock)
        {
            _inventoryRestoredFromSnapshot.Value = true;
        }
    }

    /// <summary>
    /// Atomically checks if restoration occurred and resets the flag.
    /// This is the ONLY method that should be used to check the flag.
    /// </summary>
    /// <returns>True if inventory was restored (flag was set), false otherwise.</returns>
    /// <remarks>
    /// <para>
    /// This method ensures the flag is consumed exactly once, preventing
    /// race conditions where multiple callers might try to act on the flag.
    /// </para>
    /// <para>
    /// After this method returns true, subsequent calls will return false
    /// until <see cref="MarkRestored"/> is called again.
    /// </para>
    /// </remarks>
    public static bool TryConsume()
    {
        lock (_lock)
        {
            bool wasRestored = _inventoryRestoredFromSnapshot.Value;
            _inventoryRestoredFromSnapshot.Value = false;
            return wasRestored;
        }
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
    /// creates a race condition.
    /// </para>
    /// </remarks>
    [Obsolete("Use TryConsume() instead for atomic check-and-reset")]
    public static void Reset()
    {
        lock (_lock)
        {
            _inventoryRestoredFromSnapshot.Value = false;
        }
    }
}
