// ============================================================================
// Keep Starting Gear - Snapshot Restorer
// ============================================================================
// Centralized snapshot restoration logic used by both RaidEndInterceptor and
// CustomInRaidHelper. This eliminates code duplication and ensures consistent
// behavior regardless of which restoration path is used.
//
// USAGE:
// Both RaidEndInterceptor.TryRestoreFromSnapshot() and
// CustomInRaidHelper.TryRestoreFromSnapshot() delegate to this class.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using Path = System.IO.Path;

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Result of a snapshot restoration operation.
/// </summary>
public class RestoreResult
{
    /// <summary>Whether the restoration was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Number of items added during restoration.</summary>
    public int ItemsAdded { get; init; }

    /// <summary>Number of duplicate items skipped.</summary>
    public int DuplicatesSkipped { get; init; }

    /// <summary>Number of items skipped from non-managed slots.</summary>
    public int NonManagedSkipped { get; init; }

    /// <summary>Error message if restoration failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static RestoreResult Succeeded(int itemsAdded, int duplicatesSkipped = 0, int nonManagedSkipped = 0)
        => new() { Success = true, ItemsAdded = itemsAdded, DuplicatesSkipped = duplicatesSkipped, NonManagedSkipped = nonManagedSkipped };

    /// <summary>Creates a failed result with error message.</summary>
    public static RestoreResult Failed(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Static helper class for snapshot path resolution.
/// </summary>
public static class SnapshotRestorerHelper
{
    /// <summary>
    /// Resolves the snapshots path from the server mod's DLL location.
    /// </summary>
    public static string ResolveSnapshotsPath()
    {
        try
        {
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? modDirectory = Path.GetDirectoryName(dllPath);

            if (string.IsNullOrEmpty(modDirectory))
            {
                throw new InvalidOperationException("Could not determine mod directory from DLL path");
            }

            // Navigate up to SPT root (4 levels)
            string sptRoot = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", "..", ".."));

            // Construct BepInEx snapshots path
            string snapshotsPath = Path.Combine(sptRoot, "BepInEx", "plugins", Constants.ModFolderName, "snapshots");

            return snapshotsPath;
        }
        catch (Exception)
        {
            // Fallback
            return Path.Combine("..", "..", "..", "BepInEx", "plugins", Constants.ModFolderName, "snapshots");
        }
    }
}

/// <summary>
/// Centralized snapshot restoration logic.
/// This class contains all the restoration code previously duplicated between
/// RaidEndInterceptor and CustomInRaidHelper.
/// </summary>
/// <typeparam name="TLogger">The type parameter for the logger source.</typeparam>
public class SnapshotRestorer<TLogger>
{
    private readonly ISptLogger<TLogger> _logger;
    private readonly string _snapshotsPath;

    /// <summary>
    /// Creates a new SnapshotRestorer instance.
    /// </summary>
    /// <param name="logger">Logger for output messages.</param>
    /// <param name="snapshotsPath">Path to the snapshots directory.</param>
    public SnapshotRestorer(ISptLogger<TLogger> logger, string snapshotsPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _snapshotsPath = snapshotsPath ?? throw new ArgumentNullException(nameof(snapshotsPath));
    }

    /// <summary>
    /// Attempts to restore inventory from a snapshot file.
    /// </summary>
    /// <param name="sessionId">Player's session/profile ID.</param>
    /// <param name="inventoryItems">The inventory items list to modify.</param>
    /// <returns>RestoreResult indicating success/failure and statistics.</returns>
    public RestoreResult TryRestore(string sessionId, List<Item> inventoryItems)
    {
        if (string.IsNullOrEmpty(sessionId))
            return RestoreResult.Failed("Session ID is null or empty");

        if (inventoryItems == null)
            return RestoreResult.Failed("Inventory items list is null");

        string snapshotPath = Path.Combine(_snapshotsPath, $"{sessionId}.json");

        if (!File.Exists(snapshotPath))
        {
            _logger.Debug($"{Constants.LogPrefix} No snapshot file found at: {snapshotPath}");
            return RestoreResult.Failed("No snapshot file found");
        }

        try
        {
            return RestoreFromFile(snapshotPath, inventoryItems);
        }
        catch (IOException ex)
        {
            _logger.Error($"{Constants.LogPrefix} IO error reading snapshot: {ex.Message}");
            return RestoreResult.Failed($"IO error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.Error($"{Constants.LogPrefix} JSON parsing error: {ex.Message}");
            return RestoreResult.Failed($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"{Constants.LogPrefix} Unexpected error restoring from snapshot: {ex.Message}");
            _logger.Error($"{Constants.LogPrefix} Stack trace: {ex.StackTrace}");
            return RestoreResult.Failed($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs the actual restoration from a snapshot file.
    /// </summary>
    private RestoreResult RestoreFromFile(string snapshotPath, List<Item> inventoryItems)
    {
        _logger.Debug($"{Constants.LogPrefix} Found snapshot file: {snapshotPath}");

        // Read snapshot with retry for file locking issues
        string snapshotJson = ReadSnapshotWithRetry(snapshotPath);

        // Log preview for debugging
        _logger.Debug($"{Constants.LogPrefix} Raw snapshot JSON preview: {snapshotJson.Substring(0, Math.Min(500, snapshotJson.Length))}...");

        var snapshot = JsonSerializer.Deserialize<InventorySnapshot>(snapshotJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (snapshot == null || snapshot.Items == null || snapshot.Items.Count == 0)
        {
            _logger.Warning($"{Constants.LogPrefix} Snapshot is empty or invalid");
            return RestoreResult.Failed("Snapshot is empty or invalid");
        }

        // Validate snapshot version
        if (!ValidateSnapshotVersion(snapshot))
        {
            _logger.Warning($"{Constants.LogPrefix} Snapshot version is incompatible - skipping restoration");
            TryDeleteSnapshotFile(snapshotPath);
            return RestoreResult.Failed("Snapshot version is incompatible");
        }

        _logger.Debug($"{Constants.LogPrefix} Snapshot contains {snapshot.Items.Count} items");

        // Log included slots
        if (snapshot.IncludedSlots != null)
        {
            _logger.Debug($"{Constants.LogPrefix} Deserialized IncludedSlots: [{string.Join(", ", snapshot.IncludedSlots)}]");
        }
        else
        {
            _logger.Debug($"{Constants.LogPrefix} IncludedSlots is NULL after deserialization (legacy snapshot)");
        }

        // Find Equipment container IDs
        string? profileEquipmentId = FindEquipmentContainerId(inventoryItems);
        string? snapshotEquipmentId = FindSnapshotEquipmentId(snapshot.Items);

        if (string.IsNullOrEmpty(profileEquipmentId))
        {
            _logger.Error($"{Constants.LogPrefix} Could not find Equipment container in profile");
            return RestoreResult.Failed("Could not find Equipment container in profile");
        }

        _logger.Debug($"{Constants.LogPrefix} Profile Equipment ID: {profileEquipmentId}");
        _logger.Debug($"{Constants.LogPrefix} Snapshot Equipment ID: {snapshotEquipmentId}");

        // Build slot tracking sets
        var (includedSlotIds, snapshotSlotIds, emptySlotIds) = BuildSlotSets(snapshot, snapshotEquipmentId);

        // Build item-to-root-slot map with O(1) lookup
        var snapshotItemLookup = BuildSnapshotItemLookup(snapshot.Items);
        var snapshotItemSlots = BuildRootSlotMap(snapshot.Items, snapshotEquipmentId, snapshotItemLookup);

        // Remove current equipment from managed slots
        int removedCount = RemoveManagedSlotItems(
            inventoryItems,
            profileEquipmentId,
            includedSlotIds,
            snapshotSlotIds,
            emptySlotIds);

        _logger.Debug($"{Constants.LogPrefix} Removed {removedCount} equipment items");

        // Build existing item ID set for duplicate prevention
        var existingItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventoryItems)
        {
            if (!string.IsNullOrEmpty(item.Id))
            {
                existingItemIds.Add(item.Id);
            }
        }
        _logger.Debug($"{Constants.LogPrefix} Existing inventory has {existingItemIds.Count} items before restoration");

        // Add snapshot items
        var (addedCount, skippedDuplicates, skippedNonManaged) = AddSnapshotItems(
            inventoryItems,
            snapshot.Items,
            profileEquipmentId,
            snapshotEquipmentId,
            includedSlotIds,
            snapshotItemSlots,
            existingItemIds);

        _logger.Debug($"{Constants.LogPrefix} Added {addedCount} items from snapshot, total now: {inventoryItems.Count}");

        if (skippedDuplicates > 0)
        {
            _logger.Debug($"{Constants.LogPrefix} Skipped {skippedDuplicates} duplicate items");
        }
        if (skippedNonManaged > 0)
        {
            _logger.Debug($"{Constants.LogPrefix} Skipped {skippedNonManaged} items from non-managed slots (preserved)");
        }

        // Delete snapshot file after successful restoration
        TryDeleteSnapshotFile(snapshotPath);

        return RestoreResult.Succeeded(addedCount, skippedDuplicates, skippedNonManaged);
    }

    /// <summary>
    /// Reads snapshot file with retry logic for file locking.
    /// </summary>
    private string ReadSnapshotWithRetry(string path, int maxRetries = 3)
    {
        Exception? lastException = null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex) when (i < maxRetries - 1)
            {
                lastException = ex;
                _logger.Debug($"{Constants.LogPrefix} File read attempt {i + 1} failed, retrying...");
                Thread.Sleep(100 * (i + 1)); // Exponential backoff
            }
        }

        throw lastException ?? new IOException("Failed to read snapshot file");
    }

    /// <summary>
    /// Finds the Equipment container ID in the profile's inventory.
    /// </summary>
    private string? FindEquipmentContainerId(List<Item> items)
    {
        foreach (var item in items)
        {
            if (item.Template == Constants.EquipmentTemplateId)
            {
                return item.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the Equipment container ID in the snapshot.
    /// </summary>
    private string? FindSnapshotEquipmentId(List<SnapshotItem> items)
    {
        foreach (var item in items)
        {
            if (item.Tpl == Constants.EquipmentTemplateId)
            {
                return item.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Builds the slot tracking sets from snapshot data.
    /// </summary>
    private (HashSet<string> included, HashSet<string> snapshot, HashSet<string> empty) BuildSlotSets(
        InventorySnapshot snapshot,
        string? snapshotEquipmentId)
    {
        // User-configured slots (authoritative)
        var includedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.IncludedSlots != null)
        {
            foreach (var slot in snapshot.IncludedSlots)
            {
                includedSlotIds.Add(slot);
            }
            _logger.Debug($"{Constants.LogPrefix} User configured slots to manage: {string.Join(", ", includedSlotIds)}");
        }

        // Slots with items in snapshot
        var snapshotSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.Items)
        {
            if (item.ParentId == snapshotEquipmentId && !string.IsNullOrEmpty(item.SlotId))
            {
                snapshotSlotIds.Add(item.SlotId);
            }
        }
        _logger.Debug($"{Constants.LogPrefix} Snapshot contains slots with items: {string.Join(", ", snapshotSlotIds)}");

        // Slots that were empty at snapshot time
        var emptySlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.EmptySlots != null)
        {
            foreach (var slot in snapshot.EmptySlots)
            {
                emptySlotIds.Add(slot);
            }
            _logger.Debug($"{Constants.LogPrefix} Snapshot tracked empty slots: {string.Join(", ", emptySlotIds)}");
        }

        return (includedSlotIds, snapshotSlotIds, emptySlotIds);
    }

    /// <summary>
    /// Builds an O(1) lookup dictionary for snapshot items by ID.
    /// This fixes the O(n^2) performance issue in root slot tracing.
    /// </summary>
    private Dictionary<string, SnapshotItem> BuildSnapshotItemLookup(List<SnapshotItem> items)
    {
        var lookup = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Id) && !lookup.ContainsKey(item.Id))
            {
                lookup[item.Id] = item;
            }
        }
        return lookup;
    }

    /// <summary>
    /// Builds a map of item IDs to their root equipment slot.
    /// Uses O(1) lookup for parent traversal.
    /// </summary>
    private Dictionary<string, string> BuildRootSlotMap(
        List<SnapshotItem> items,
        string? snapshotEquipmentId,
        Dictionary<string, SnapshotItem> itemLookup)
    {
        var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Id)) continue;

            string? rootSlot = TraceRootSlot(item, snapshotEquipmentId, itemLookup);
            if (!string.IsNullOrEmpty(rootSlot))
            {
                slotMap[item.Id] = rootSlot;
            }
        }

        return slotMap;
    }

    /// <summary>
    /// Traces an item up to its root equipment slot using O(1) parent lookup.
    /// Includes infinite loop protection with logging.
    /// </summary>
    private string? TraceRootSlot(
        SnapshotItem item,
        string? snapshotEquipmentId,
        Dictionary<string, SnapshotItem> itemLookup)
    {
        var currentItem = item;
        int depth = 0;
        var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (currentItem != null && depth < Constants.MaxParentTraversalDepth)
        {
            // Cycle detection
            if (!string.IsNullOrEmpty(currentItem.Id))
            {
                if (visitedIds.Contains(currentItem.Id))
                {
                    _logger.Warning($"{Constants.LogPrefix} Cycle detected in parent chain for item {item.Id} at depth {depth}");
                    return null;
                }
                visitedIds.Add(currentItem.Id);
            }

            // Check if we've reached Equipment
            if (currentItem.ParentId == snapshotEquipmentId)
            {
                return currentItem.SlotId;
            }

            // Move to parent using O(1) lookup
            if (string.IsNullOrEmpty(currentItem.ParentId))
            {
                break;
            }

            if (!itemLookup.TryGetValue(currentItem.ParentId, out var parent))
            {
                break;
            }

            currentItem = parent;
            depth++;
        }

        if (depth >= Constants.MaxParentTraversalDepth)
        {
            _logger.Warning($"{Constants.LogPrefix} Max traversal depth ({Constants.MaxParentTraversalDepth}) reached for item {item.Id}. Possible corrupt item hierarchy.");
        }

        return null;
    }

    /// <summary>
    /// Removes items from managed equipment slots.
    /// </summary>
    private int RemoveManagedSlotItems(
        List<Item> items,
        string profileEquipmentId,
        HashSet<string> includedSlotIds,
        HashSet<string> snapshotSlotIds,
        HashSet<string> emptySlotIds)
    {
        var equipmentItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find direct children of Equipment to remove
        foreach (var item in items)
        {
            if (item.ParentId != profileEquipmentId) continue;

            var slotId = item.SlotId ?? "";
            bool slotIsManaged = IsSlotManaged(slotId, includedSlotIds, snapshotSlotIds, emptySlotIds);

            if (!slotIsManaged)
            {
                _logger.Debug($"{Constants.LogPrefix} PRESERVING item in slot '{slotId}' (slot not managed by mod): {item.Template}");
                continue;
            }

            equipmentItemIds.Add(item.Id!);
            LogRemovalReason(slotId, item.Template, snapshotSlotIds, emptySlotIds);
        }

        // Recursively find all nested items
        bool foundMore = true;
        int iterations = 0;
        while (foundMore && iterations < Constants.MaxParentTraversalDepth)
        {
            foundMore = false;
            foreach (var item in items)
            {
                if (item.ParentId != null &&
                    equipmentItemIds.Contains(item.ParentId) &&
                    !equipmentItemIds.Contains(item.Id!))
                {
                    equipmentItemIds.Add(item.Id!);
                    foundMore = true;
                }
            }
            iterations++;
        }

        if (iterations >= Constants.MaxParentTraversalDepth)
        {
            _logger.Warning($"{Constants.LogPrefix} Max iterations reached while finding nested items. Possible corrupt item hierarchy.");
        }

        int removedCount = items.RemoveAll(item => equipmentItemIds.Contains(item.Id!));
        return removedCount;
    }

    /// <summary>
    /// Determines if a slot is managed by the mod.
    /// </summary>
    private bool IsSlotManaged(
        string slotId,
        HashSet<string> includedSlotIds,
        HashSet<string> snapshotSlotIds,
        HashSet<string> emptySlotIds)
    {
        if (includedSlotIds.Count > 0)
        {
            // Modern snapshot: use IncludedSlots as authoritative source
            return includedSlotIds.Contains(slotId);
        }
        else
        {
            // Legacy snapshot: fall back to old behavior
            return snapshotSlotIds.Contains(slotId) || emptySlotIds.Contains(slotId);
        }
    }

    /// <summary>
    /// Logs the reason why an item is being removed.
    /// </summary>
    private void LogRemovalReason(string slotId, string? template, HashSet<string> snapshotSlotIds, HashSet<string> emptySlotIds)
    {
        if (snapshotSlotIds.Contains(slotId))
        {
            _logger.Debug($"{Constants.LogPrefix} Removing item from slot '{slotId}' (will be restored from snapshot): {template}");
        }
        else if (emptySlotIds.Contains(slotId))
        {
            _logger.Debug($"{Constants.LogPrefix} Removing item from slot '{slotId}' (slot was empty at snapshot time - loot lost): {template}");
        }
        else
        {
            _logger.Debug($"{Constants.LogPrefix} Removing item from slot '{slotId}' (slot is managed but had no snapshot data): {template}");
        }
    }

    /// <summary>
    /// Adds snapshot items to the inventory.
    /// </summary>
    private (int added, int duplicates, int nonManaged) AddSnapshotItems(
        List<Item> inventoryItems,
        List<SnapshotItem> snapshotItems,
        string profileEquipmentId,
        string? snapshotEquipmentId,
        HashSet<string> includedSlotIds,
        Dictionary<string, string> snapshotItemSlots,
        HashSet<string> existingItemIds)
    {
        int addedCount = 0;
        int skippedDuplicates = 0;
        int skippedNonManaged = 0;

        foreach (var snapshotItem in snapshotItems)
        {
            // Skip Equipment container
            if (snapshotItem.Tpl == Constants.EquipmentTemplateId)
                continue;

            // Skip items with missing required data
            if (string.IsNullOrEmpty(snapshotItem.Id) || string.IsNullOrEmpty(snapshotItem.Tpl))
            {
                _logger.Warning($"{Constants.LogPrefix} Skipping item with missing Id or Tpl");
                continue;
            }

            // Skip items from non-managed slots
            if (snapshotItemSlots.TryGetValue(snapshotItem.Id, out var rootSlot) &&
                !string.IsNullOrEmpty(rootSlot) &&
                includedSlotIds.Count > 0 &&
                !includedSlotIds.Contains(rootSlot))
            {
                _logger.Debug($"{Constants.LogPrefix} Skipping item {snapshotItem.Id} from non-managed slot '{rootSlot}'");
                skippedNonManaged++;
                continue;
            }

            // Check for duplicates
            if (existingItemIds.Contains(snapshotItem.Id))
            {
                _logger.Debug($"{Constants.LogPrefix} DUPLICATE PREVENTED: Item {snapshotItem.Id} already exists - skipping");
                skippedDuplicates++;
                continue;
            }

            // Create and configure new item
            var newItem = CreateItemFromSnapshot(snapshotItem, profileEquipmentId, snapshotEquipmentId);

            inventoryItems.Add(newItem);
            existingItemIds.Add(newItem.Id!);
            addedCount++;
        }

        return (addedCount, skippedDuplicates, skippedNonManaged);
    }

    /// <summary>
    /// Creates a new Item from a SnapshotItem.
    /// </summary>
    private Item CreateItemFromSnapshot(SnapshotItem snapshotItem, string profileEquipmentId, string? snapshotEquipmentId)
    {
        var newItem = new Item
        {
            Id = snapshotItem.Id,
            Template = snapshotItem.Tpl,
            SlotId = snapshotItem.SlotId,
            ParentId = snapshotItem.ParentId == snapshotEquipmentId
                ? profileEquipmentId
                : snapshotItem.ParentId
        };

        // Copy location data
        var cartridgeIndex = snapshotItem.GetLocationIndex();
        var gridLocation = snapshotItem.GetLocation();

        if (cartridgeIndex.HasValue)
        {
            newItem.Location = cartridgeIndex.Value;
            _logger.Debug($"{Constants.LogPrefix} [CARTRIDGE] Restored cartridge position {cartridgeIndex.Value} for {snapshotItem.Tpl}");
        }
        else if (gridLocation != null)
        {
            newItem.Location = new ItemLocation
            {
                X = gridLocation.X,
                Y = gridLocation.Y,
                R = (ItemRotation)gridLocation.R,
                IsSearched = gridLocation.IsSearched
            };
        }

        // Copy update data
        if (snapshotItem.Upd != null)
        {
            try
            {
                var updJson = JsonSerializer.Serialize(snapshotItem.Upd);
                _logger.Debug($"{Constants.LogPrefix} [UPD] Raw Upd JSON for {snapshotItem.Id}: {updJson}");

                newItem.Upd = JsonSerializer.Deserialize<Upd>(updJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Try to extract StackObjectsCount if standard deserialization missed it
                if (newItem.Upd != null &&
                    (newItem.Upd.StackObjectsCount == null || newItem.Upd.StackObjectsCount == 0) &&
                    snapshotItem.Upd is JsonElement updElement)
                {
                    TryExtractStackCount(updElement, newItem, snapshotItem.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"{Constants.LogPrefix} Could not convert Upd for item {snapshotItem.Id}: {ex.Message}");
            }
        }

        return newItem;
    }

    /// <summary>
    /// Attempts to extract StackObjectsCount from a JsonElement.
    /// </summary>
    private void TryExtractStackCount(JsonElement updElement, Item newItem, string? itemId)
    {
        if (updElement.TryGetProperty("StackObjectsCount", out var stackProp) ||
            updElement.TryGetProperty("stackObjectsCount", out stackProp))
        {
            if (stackProp.TryGetInt32(out int stackCount) && stackCount > 0 && newItem.Upd != null)
            {
                newItem.Upd.StackObjectsCount = stackCount;
                _logger.Debug($"{Constants.LogPrefix} [UPD] Manually extracted StackObjectsCount={stackCount} for {itemId}");
            }
        }
    }

    /// <summary>
    /// Validates that a snapshot is compatible with the current mod version.
    /// </summary>
    private bool ValidateSnapshotVersion(InventorySnapshot snapshot)
    {
        string currentVersion = Constants.ModVersion;

        if (!TryParseVersion(currentVersion, out int currentMajor, out int currentMinor, out _))
        {
            _logger.Warning($"{Constants.LogPrefix} Could not parse current mod version: {currentVersion}");
            return true; // Don't block on parse failure
        }

        if (string.IsNullOrEmpty(snapshot.ModVersion))
        {
            _logger.Warning($"{Constants.LogPrefix} Snapshot has no version info (created by mod version < 1.4.0)");
            _logger.Warning($"{Constants.LogPrefix} This snapshot may have incompatible structure and will be skipped.");
            return false;
        }

        if (!TryParseVersion(snapshot.ModVersion, out int snapMajor, out int snapMinor, out _))
        {
            _logger.Warning($"{Constants.LogPrefix} Could not parse snapshot version: {snapshot.ModVersion}");
            return true;
        }

        _logger.Debug($"{Constants.LogPrefix} Version check: snapshot={snapshot.ModVersion}, current={currentVersion}");

        // Major version mismatch
        if (snapMajor != currentMajor)
        {
            _logger.Warning($"{Constants.LogPrefix} Major version mismatch: snapshot v{snapMajor}.x.x vs current v{currentMajor}.x.x");
            return false;
        }

        // Future minor version
        if (snapMinor > currentMinor)
        {
            _logger.Warning($"{Constants.LogPrefix} Snapshot is from newer version: {snapshot.ModVersion} > {currentVersion}");
            _logger.Warning($"{Constants.LogPrefix} Please update the mod to ensure compatibility.");
        }
        else if (snapMinor < currentMinor)
        {
            _logger.Debug($"{Constants.LogPrefix} Snapshot is from older version {snapshot.ModVersion} - proceeding with compatibility mode");
        }

        return true;
    }

    /// <summary>
    /// Parses a semantic version string.
    /// </summary>
    private static bool TryParseVersion(string version, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;

        if (string.IsNullOrEmpty(version))
            return false;

        var parts = version.Split('.');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], out major))
            return false;

        if (!int.TryParse(parts[1], out minor))
            return false;

        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            patch = 0;

        return true;
    }

    /// <summary>
    /// Safely deletes a snapshot file.
    /// </summary>
    private void TryDeleteSnapshotFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.Debug($"{Constants.LogPrefix} Deleted snapshot: {path}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"{Constants.LogPrefix} Failed to delete snapshot file: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears any snapshot for the given session (used on successful extraction).
    /// </summary>
    public void ClearSnapshot(string sessionId)
    {
        try
        {
            var snapshotPath = Path.Combine(_snapshotsPath, $"{sessionId}.json");
            TryDeleteSnapshotFile(snapshotPath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"{Constants.LogPrefix} Failed to clear snapshot: {ex.Message}");
        }
    }
}
