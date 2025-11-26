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
// 3. CustomInRaidHelper.DeleteInventory checks this flag
// 4. If flag is true, skip the deletion (preserve restored items)
// 5. Reset the flag after processing
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
///   <item>CustomInRaidHelper checks flag, sees it's true, skips deletion</item>
///   <item>Flag is reset for the next request</item>
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

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Gets or sets whether the inventory was restored from a snapshot.
    /// When true, CustomInRaidHelper.DeleteInventory will skip deletion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to true immediately after restoring inventory from snapshot.
    /// The CustomInRaidHelper will check this before deleting inventory.
    /// </para>
    /// <para>
    /// <b>Important:</b> This flag is automatically reset by CustomInRaidHelper
    /// after checking it, so it only affects the current request.
    /// </para>
    /// </remarks>
    public static bool InventoryRestoredFromSnapshot
    {
        get => _inventoryRestoredFromSnapshot.Value;
        set => _inventoryRestoredFromSnapshot.Value = value;
    }

    /// <summary>
    /// Resets the restoration flag to false.
    /// Should be called at the end of request processing.
    /// </summary>
    /// <remarks>
    /// This ensures the flag doesn't persist and affect future requests.
    /// Called by CustomInRaidHelper after checking/using the flag.
    /// </remarks>
    public static void Reset()
    {
        _inventoryRestoredFromSnapshot.Value = false;
    }
}
