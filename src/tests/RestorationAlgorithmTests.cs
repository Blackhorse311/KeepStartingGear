// ============================================================================
// Keep Starting Gear - Restoration Algorithm Tests
// ============================================================================
// Comprehensive unit tests for the restoration algorithm.
// These tests validate all invariants and edge cases.
//
// HEALTHCARE-GRADE TESTING:
// Each test documents:
// 1. What invariant it validates
// 2. What edge case it covers
// 3. Expected behavior
//
// AUTHOR: Blackhorse311 + Linus Torvalds
// LICENSE: MIT
// ============================================================================

using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace Blackhorse311.KeepStartingGear.Tests;

/// <summary>
/// Tests for FindEquipmentContainerId.
/// INVARIANT: Returns the ID of the first Equipment container, or null.
/// </summary>
public class FindEquipmentContainerIdTests
{
    [Fact]
    public void ReturnsNull_WhenNoEquipmentContainer()
    {
        // Arrange
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("item1", tpl: "some-weapon-template"),
            AlgorithmItem.Create("item2", tpl: "some-armor-template")
        };

        // Act
        var result = RestorationAlgorithm.FindEquipmentContainerId(items);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsEquipmentId_WhenExists()
    {
        // Arrange
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("item1", tpl: "some-weapon-template"),
            AlgorithmItem.Create("equipment-id", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("item2", tpl: "some-armor-template")
        };

        // Act
        var result = RestorationAlgorithm.FindEquipmentContainerId(items);

        // Assert
        Assert.Equal("equipment-id", result);
    }

    [Fact]
    public void ReturnsFirstEquipmentId_WhenMultipleExist()
    {
        // Arrange - Edge case: multiple Equipment containers (shouldn't happen, but be robust)
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("equipment-1", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("equipment-2", tpl: AlgorithmConstants.EquipmentTemplateId)
        };

        // Act
        var result = RestorationAlgorithm.FindEquipmentContainerId(items);

        // Assert
        Assert.Equal("equipment-1", result);
    }
}

/// <summary>
/// Tests for BuildSlotSets.
/// INVARIANT: Correctly distinguishes null vs empty vs populated IncludedSlots.
/// </summary>
public class BuildSlotSetsTests
{
    private readonly string _equipmentId = "equipment-id";

    [Fact]
    public void IncludedSlotIds_IsNull_WhenIncludedSlotsIsNull()
    {
        // Arrange - Legacy format snapshot
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon")
        };

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, null, null, _equipmentId);

        // Assert - CRITICAL: null means legacy format
        Assert.Null(result.IncludedSlotIds);
    }

    [Fact]
    public void IncludedSlotIds_IsEmpty_WhenIncludedSlotsIsEmpty()
    {
        // Arrange - Modern format: user explicitly selected nothing
        var items = new List<AlgorithmItem>();

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, new List<string>(), null, _equipmentId);

        // Assert - CRITICAL: empty set means user chose no protection
        Assert.NotNull(result.IncludedSlotIds);
        Assert.Empty(result.IncludedSlotIds);
    }

    [Fact]
    public void IncludedSlotIds_ContainsSlots_WhenPopulated()
    {
        // Arrange
        var items = new List<AlgorithmItem>();
        var includedSlots = new List<string> { "FirstPrimaryWeapon", "Backpack" };

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, includedSlots, null, _equipmentId);

        // Assert
        Assert.NotNull(result.IncludedSlotIds);
        Assert.Contains("FirstPrimaryWeapon", result.IncludedSlotIds);
        Assert.Contains("Backpack", result.IncludedSlotIds);
    }

    [Fact]
    public void SnapshotSlotIds_ContainsSlotsWithItems()
    {
        // Arrange
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon"),
            AlgorithmItem.Create("vest", parentId: _equipmentId, slotId: "TacticalVest")
        };

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, null, null, _equipmentId);

        // Assert
        Assert.Contains("FirstPrimaryWeapon", result.SnapshotSlotIds);
        Assert.Contains("TacticalVest", result.SnapshotSlotIds);
        Assert.Equal(2, result.SnapshotSlotIds.Count);
    }

    [Fact]
    public void EmptySlotIds_ContainsEmptySlots()
    {
        // Arrange
        var items = new List<AlgorithmItem>();
        var emptySlots = new List<string> { "Holster", "Headwear" };

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, null, emptySlots, _equipmentId);

        // Assert
        Assert.Contains("Holster", result.EmptySlotIds);
        Assert.Contains("Headwear", result.EmptySlotIds);
    }

    [Fact]
    public void SlotComparison_IsCaseInsensitive()
    {
        // Arrange
        var items = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "firstprimaryweapon") // lowercase
        };
        var includedSlots = new List<string> { "FIRSTPRIMARYWEAPON" }; // uppercase

        // Act
        var result = RestorationAlgorithm.BuildSlotSets(items, includedSlots, null, _equipmentId);

        // Assert
        Assert.Contains("FirstPrimaryWeapon", result.SnapshotSlotIds); // normalized
        Assert.Contains("firstprimaryweapon", result.IncludedSlotIds!); // case-insensitive match
    }
}

/// <summary>
/// Tests for TraceRootSlot.
/// INVARIANT: Traces item to root slot, handles cycles, respects depth limit.
/// </summary>
public class TraceRootSlotTests
{
    private readonly string _equipmentId = "equipment-id";

    [Fact]
    public void ReturnsSlotId_ForDirectChildOfEquipment()
    {
        // Arrange
        var weapon = AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon");
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { weapon });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(weapon, _equipmentId, lookup);

        // Assert
        Assert.Equal("FirstPrimaryWeapon", result);
    }

    [Fact]
    public void ReturnsSlotId_ForNestedItem()
    {
        // Arrange - Magazine inside weapon inside Equipment
        var weapon = AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon");
        var magazine = AlgorithmItem.Create("magazine", parentId: "weapon", slotId: "mod_magazine");
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { weapon, magazine });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(magazine, _equipmentId, lookup);

        // Assert
        Assert.Equal("FirstPrimaryWeapon", result);
    }

    [Fact]
    public void ReturnsSlotId_ForDeeplyNestedItem()
    {
        // Arrange - Ammo -> Magazine -> Weapon -> Equipment
        var weapon = AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon");
        var magazine = AlgorithmItem.Create("magazine", parentId: "weapon", slotId: "mod_magazine");
        var ammo = AlgorithmItem.Create("ammo", parentId: "magazine", slotId: "cartridges");
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { weapon, magazine, ammo });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(ammo, _equipmentId, lookup);

        // Assert
        Assert.Equal("FirstPrimaryWeapon", result);
    }

    [Fact]
    public void ReturnsNull_WhenItemNotUnderEquipment()
    {
        // Arrange - Item with no path to Equipment
        var orphan = AlgorithmItem.Create("orphan", parentId: "nonexistent");
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { orphan });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(orphan, _equipmentId, lookup);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenCycleDetected()
    {
        // Arrange - Circular reference: A -> B -> A
        var itemA = AlgorithmItem.Create("A", parentId: "B");
        var itemB = AlgorithmItem.Create("B", parentId: "A");
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { itemA, itemB });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(itemA, _equipmentId, lookup);

        // Assert - Should terminate gracefully, not infinite loop
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenNoParent()
    {
        // Arrange
        var item = AlgorithmItem.Create("item", parentId: null);
        var lookup = RestorationAlgorithm.BuildItemLookup(new[] { item });

        // Act
        var result = RestorationAlgorithm.TraceRootSlot(item, _equipmentId, lookup);

        // Assert
        Assert.Null(result);
    }
}

/// <summary>
/// Tests for IsSlotManaged.
/// IsSlotManaged answers "should this slot's contents be managed (restored from snapshot)?"
/// Container preservation (SC/Pockets never deleted) is handled separately in CollectItemsToRemove.
/// </summary>
public class IsSlotManagedTests
{
    [Fact]
    public void ReturnsTrue_ForSecuredContainer_WhenInIncludedSlots()
    {
        // Arrange - SC contents are managed when the slot is in includedSlotIds
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SecuredContainer" };
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SecuredContainer" };
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("SecuredContainer", includedSlots, snapshotSlots, emptySlots);

        // Assert - SC contents are managed (container preservation is separate)
        Assert.True(result);
    }

    [Fact]
    public void ReturnsTrue_ForSecuredContainer_CaseInsensitive()
    {
        // Arrange
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SECUREDCONTAINER" };
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("securedcontainer", includedSlots, snapshotSlots, emptySlots);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ReturnsTrue_ForPockets_WhenInIncludedSlots()
    {
        // Arrange - Pockets contents are managed when the slot is in includedSlotIds
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pockets" };
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pockets" };
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("Pockets", includedSlots, snapshotSlots, emptySlots);

        // Assert - Pockets contents are managed (container preservation is separate)
        Assert.True(result);
    }

    [Fact]
    public void ReturnsTrue_ForPockets_CaseInsensitive()
    {
        // Arrange
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POCKETS" };
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("pockets", includedSlots, snapshotSlots, emptySlots);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ReturnsTrue_WhenSlotInIncludedSlots()
    {
        // Arrange
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FirstPrimaryWeapon" };
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("FirstPrimaryWeapon", includedSlots, snapshotSlots, emptySlots);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ReturnsFalse_WhenIncludedSlotsIsEmpty()
    {
        // Arrange - User explicitly selected no slots (empty set, not null)
        var includedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FirstPrimaryWeapon" };
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("FirstPrimaryWeapon", includedSlots, snapshotSlots, emptySlots);

        // Assert - Empty includedSlots means normal death processing
        Assert.False(result);
    }

    [Fact]
    public void LegacyFormat_ReturnsTrue_WhenInSnapshotSlots()
    {
        // Arrange - null includedSlots = legacy format
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FirstPrimaryWeapon" };
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("FirstPrimaryWeapon", null, snapshotSlots, emptySlots);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void LegacyFormat_ReturnsTrue_WhenInEmptySlots()
    {
        // Arrange - Slot was empty at snapshot time, should be cleared on restore
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Holster" };

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("Holster", null, snapshotSlots, emptySlots);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void LegacyFormat_ReturnsFalse_WhenNotInEitherSet()
    {
        // Arrange
        var snapshotSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FirstPrimaryWeapon" };
        var emptySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Holster" };

        // Act
        var result = RestorationAlgorithm.IsSlotManaged("Backpack", null, snapshotSlots, emptySlots);

        // Assert
        Assert.False(result);
    }
}

/// <summary>
/// Tests for the full restoration simulation.
/// These are integration tests that validate end-to-end behavior.
/// </summary>
public class SimulateRestorationTests
{
    private readonly string _equipmentId = "equipment-id";

    [Fact]
    public void RestoresItems_FromSnapshot()
    {
        // Arrange
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("current-weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        var includedSlots = new List<string> { "FirstPrimaryWeapon" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ItemsAdded);
        Assert.Equal(1, result.ItemsRemoved);
        Assert.Contains(profileItems, i => i.Id == "snapshot-weapon");
        Assert.DoesNotContain(profileItems, i => i.Id == "current-weapon");
    }

    [Fact]
    public void PreservesSecuredContainerContainer_WhenManaged()
    {
        // Arrange - SC container preserved, contents managed when slot is managed
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("gamma", parentId: _equipmentId, slotId: "SecuredContainer", tpl: "gamma-tpl"),
            AlgorithmItem.Create("item-in-gamma", parentId: "gamma", slotId: "main")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId)
            // Empty snapshot - no SecuredContainer items
        };

        var includedSlots = new List<string> { "SecuredContainer" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Container preserved, contents removed (no snapshot items to restore)
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "gamma"); // Container always preserved
        Assert.DoesNotContain(profileItems, i => i.Id == "item-in-gamma"); // Contents managed
    }

    [Fact]
    public void PreservesPocketsContainer_WhenManaged()
    {
        // Arrange - Pockets container preserved, contents managed when slot is managed
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("pockets-item", parentId: _equipmentId, slotId: "Pockets", tpl: "pockets-tpl"),
            AlgorithmItem.Create("item-in-pockets", parentId: "pockets-item", slotId: "main")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId)
            // Empty snapshot - no Pockets items
        };

        var includedSlots = new List<string> { "Pockets" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Container preserved, contents removed (no snapshot items to restore)
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "pockets-item"); // Container always preserved
        Assert.DoesNotContain(profileItems, i => i.Id == "item-in-pockets"); // Contents managed
    }

    [Fact]
    public void RestoresPocketsContents_FromSnapshot()
    {
        // Arrange - Pockets contents should be replaced with snapshot contents
        // This is the exact scenario reported by the user: medkit used during raid
        // should be restored to snapshot state (full uses)
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("pockets-item", parentId: _equipmentId, slotId: "Pockets", tpl: "pockets-tpl"),
            AlgorithmItem.Create("used-medkit", parentId: "pockets-item", slotId: "pocket1"),
            AlgorithmItem.Create("looted-item", parentId: "pockets-item", slotId: "pocket2") // Picked up during raid
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("pockets-item", parentId: "snapshot-equipment", slotId: "Pockets", tpl: "pockets-tpl"),
            AlgorithmItem.Create("used-medkit", parentId: "pockets-item", slotId: "pocket1") // Full medkit in snapshot
            // No "looted-item" in snapshot (it was picked up during raid)
        };

        var includedSlots = new List<string> { "Pockets" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "pockets-item"); // Container preserved
        Assert.Contains(profileItems, i => i.Id == "used-medkit"); // Restored from snapshot (with original state)
        Assert.DoesNotContain(profileItems, i => i.Id == "looted-item"); // Looted item removed
    }

    [Fact]
    public void PreservesPocketsContents_WhenNotManaged()
    {
        // Arrange - When Pockets is NOT in includedSlots, contents should be left alone
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("pockets-item", parentId: _equipmentId, slotId: "Pockets", tpl: "pockets-tpl"),
            AlgorithmItem.Create("item-in-pockets", parentId: "pockets-item", slotId: "pocket1"),
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        // Only weapon is managed, Pockets is NOT
        var includedSlots = new List<string> { "FirstPrimaryWeapon" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Pockets untouched (not managed), weapon restored
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "pockets-item"); // Container preserved
        Assert.Contains(profileItems, i => i.Id == "item-in-pockets"); // Contents preserved (not managed)
        Assert.DoesNotContain(profileItems, i => i.Id == "weapon"); // Weapon removed
        Assert.Contains(profileItems, i => i.Id == "snapshot-weapon"); // Weapon restored
    }

    [Fact]
    public void SkipsDuplicateItems()
    {
        // Arrange - Item with same ID exists in both
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("gamma", parentId: _equipmentId, slotId: "SecuredContainer")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("gamma", parentId: "snapshot-equipment", slotId: "SecuredContainer") // Same ID
        };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, null, null);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Single(profileItems.Where(i => i.Id == "gamma")); // Only one gamma
    }

    [Fact]
    public void PreservesNonManagedSlots()
    {
        // Arrange - Only FirstPrimaryWeapon is managed
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon"),
            AlgorithmItem.Create("backpack", parentId: _equipmentId, slotId: "Backpack") // Not managed
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        var includedSlots = new List<string> { "FirstPrimaryWeapon" }; // Only weapon is managed

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "backpack"); // Preserved
        Assert.DoesNotContain(profileItems, i => i.Id == "weapon"); // Removed
        Assert.Contains(profileItems, i => i.Id == "snapshot-weapon"); // Added
    }

    [Fact]
    public void LegacyFormat_UsesSnapshotAndEmptySlots()
    {
        // Arrange - null includedSlots = legacy format
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon"),
            AlgorithmItem.Create("holster-gun", parentId: _equipmentId, slotId: "Holster") // In empty slots
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        var emptySlots = new List<string> { "Holster" }; // Was empty at snapshot time

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, null, emptySlots);

        // Assert
        Assert.True(result.Success);
        Assert.DoesNotContain(profileItems, i => i.Id == "holster-gun"); // Removed (was empty)
        Assert.Contains(profileItems, i => i.Id == "snapshot-weapon"); // Added
    }

    [Fact]
    public void EmptyIncludedSlots_TriggersNormalDeathProcessing()
    {
        // Arrange - Empty includedSlots = user chose no protection
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        var includedSlots = new List<string>(); // Empty = no protection

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Nothing should be removed or added (normal death)
        Assert.True(result.Success);
        Assert.Equal(0, result.ItemsRemoved);
        Assert.Contains(profileItems, i => i.Id == "weapon"); // Still there
    }

    [Fact]
    public void RemapsParentIdToProfileEquipment()
    {
        // Arrange - Snapshot has different Equipment ID
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("profile-equipment", tpl: AlgorithmConstants.EquipmentTemplateId)
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        var includedSlots = new List<string> { "FirstPrimaryWeapon" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Weapon should be parented to profile's Equipment
        Assert.True(result.Success);
        var addedWeapon = profileItems.First(i => i.Id == "weapon");
        Assert.Equal("profile-equipment", addedWeapon.ParentId);
    }

    [Fact]
    public void FailsGracefully_WhenNoProfileEquipment()
    {
        // Arrange - No Equipment container in profile (should never happen, but be robust)
        var profileItems = new List<AlgorithmItem>();
        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId)
        };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, null, null);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("No Equipment container in profile", result.ErrorMessage);
    }

    [Fact]
    public void RestoresScabbardContents_WhenManaged()
    {
        // Arrange - Scabbard is managed: melee weapon should be restored from snapshot
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("current-melee", parentId: _equipmentId, slotId: "Scabbard")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-melee", parentId: "snapshot-equipment", slotId: "Scabbard")
        };

        var includedSlots = new List<string> { "Scabbard" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Melee weapon replaced with snapshot version
        Assert.True(result.Success);
        Assert.Equal(1, result.ItemsAdded);
        Assert.Equal(1, result.ItemsRemoved);
        Assert.Contains(profileItems, i => i.Id == "snapshot-melee");
        Assert.DoesNotContain(profileItems, i => i.Id == "current-melee");
    }

    [Fact]
    public void PreservesScabbardContents_WhenNotManaged()
    {
        // Arrange - Scabbard not managed: melee weapon should be left alone
        // In normal Tarkov, melee weapons are never lost on death.
        // DeleteNonManagedSlotItems (production code) always preserves Scabbard.
        // In the restoration algorithm, non-managed slots are simply not touched.
        var profileItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create(_equipmentId, tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("melee-weapon", parentId: _equipmentId, slotId: "Scabbard"),
            AlgorithmItem.Create("weapon", parentId: _equipmentId, slotId: "FirstPrimaryWeapon")
        };

        var snapshotItems = new List<AlgorithmItem>
        {
            AlgorithmItem.Create("snapshot-equipment", tpl: AlgorithmConstants.EquipmentTemplateId),
            AlgorithmItem.Create("snapshot-weapon", parentId: "snapshot-equipment", slotId: "FirstPrimaryWeapon")
        };

        // Only weapon is managed, Scabbard is NOT
        var includedSlots = new List<string> { "FirstPrimaryWeapon" };

        // Act
        var result = RestorationAlgorithm.SimulateRestoration(profileItems, snapshotItems, includedSlots, null);

        // Assert - Scabbard untouched, weapon restored
        Assert.True(result.Success);
        Assert.Contains(profileItems, i => i.Id == "melee-weapon"); // Preserved (not managed)
        Assert.DoesNotContain(profileItems, i => i.Id == "weapon"); // Removed
        Assert.Contains(profileItems, i => i.Id == "snapshot-weapon"); // Restored
    }
}
