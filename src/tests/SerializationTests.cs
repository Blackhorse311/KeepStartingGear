// ============================================================================
// Keep Starting Gear - Serialization Unit Tests
// ============================================================================
// Tests for JSON serialization/deserialization to ensure snapshot data
// can be properly saved and restored.
//
// RUN TESTS: dotnet test src/tests
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;
using Blackhorse311.KeepStartingGear.Tests.Models;

namespace Blackhorse311.KeepStartingGear.Tests;

/// <summary>
/// Tests for snapshot serialization and deserialization.
/// </summary>
public class SerializationTests
{
    // ========================================================================
    // LocationConverter Tests - Polymorphic location handling
    // ========================================================================

    [Fact]
    public void LocationConverter_DeserializesInteger_AsCartridgeIndex()
    {
        // Arrange
        var json = @"{""location"": 5}";
        var settings = new JsonSerializerSettings();

        // Act
        var result = JsonConvert.DeserializeObject<TestLocationContainer>(json, settings);

        // Assert
        Assert.NotNull(result?.LocationData);
        Assert.True(result.LocationData.IsCartridge);
        Assert.Equal(5, result.LocationData.CartridgeIndex);
        Assert.False(result.LocationData.IsGrid);
    }

    [Fact]
    public void LocationConverter_DeserializesObject_AsGridLocation()
    {
        // Arrange
        var json = @"{""location"": {""x"": 2, ""y"": 3, ""r"": 1, ""isSearched"": true}}";
        var settings = new JsonSerializerSettings();

        // Act
        var result = JsonConvert.DeserializeObject<TestLocationContainer>(json, settings);

        // Assert
        Assert.NotNull(result?.LocationData);
        Assert.True(result.LocationData.IsGrid);
        Assert.NotNull(result.LocationData.GridLocation);
        Assert.Equal(2, result.LocationData.GridLocation.X);
        Assert.Equal(3, result.LocationData.GridLocation.Y);
        Assert.Equal(1, result.LocationData.GridLocation.R);
        Assert.True(result.LocationData.GridLocation.IsSearched);
        Assert.False(result.LocationData.IsCartridge);
    }

    [Fact]
    public void LocationConverter_DeserializesNull_AsNull()
    {
        // Arrange
        var json = @"{""location"": null}";
        var settings = new JsonSerializerSettings();

        // Act
        var result = JsonConvert.DeserializeObject<TestLocationContainer>(json, settings);

        // Assert
        Assert.Null(result?.LocationData);
    }

    [Fact]
    public void LocationConverter_SerializesCartridgeIndex_AsInteger()
    {
        // Arrange
        var container = new TestLocationContainer
        {
            LocationData = new LocationConverter.LocationResult
            {
                CartridgeIndex = 42
            }
        };

        // Act
        var json = JsonConvert.SerializeObject(container);

        // Assert
        Assert.Contains("\"location\":42", json);
    }

    [Fact]
    public void LocationConverter_SerializesGridLocation_AsObject()
    {
        // Arrange
        var container = new TestLocationContainer
        {
            LocationData = new LocationConverter.LocationResult
            {
                GridLocation = new ItemLocation { X = 1, Y = 2, R = 0, IsSearched = false }
            }
        };

        // Act
        var json = JsonConvert.SerializeObject(container);

        // Assert
        Assert.Contains("\"x\":1", json);
        Assert.Contains("\"y\":2", json);
        Assert.Contains("\"r\":0", json);
    }

    [Fact]
    public void LocationConverter_HandlesLargeInteger_WithBoundsCheck()
    {
        // Arrange - test integer overflow protection
        var json = @"{""location"": 999999999999}";
        var settings = new JsonSerializerSettings();

        // Act
        var result = JsonConvert.DeserializeObject<TestLocationContainer>(json, settings);

        // Assert - should fallback to 0 for overflow
        Assert.NotNull(result?.LocationData);
        Assert.True(result.LocationData.IsCartridge);
        Assert.Equal(0, result.LocationData.CartridgeIndex);
    }

    // ========================================================================
    // SerializedItem Tests
    // ========================================================================

    [Fact]
    public void SerializedItem_RoundTrips_WithGridLocation()
    {
        // Arrange
        var original = new SerializedItem
        {
            Id = "test-item-123",
            Tpl = "5448be9a4bdc2dfd2f8b456a",
            ParentId = "parent-456",
            SlotId = "main",
            LocationData = new LocationConverter.LocationResult
            {
                GridLocation = new ItemLocation { X = 3, Y = 4, R = 1 }
            },
            Upd = new ItemUpd
            {
                StackObjectsCount = 30,
                SpawnedInSession = true
            }
        };

        // Act
        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<SerializedItem>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Tpl, deserialized.Tpl);
        Assert.Equal(original.ParentId, deserialized.ParentId);
        Assert.Equal(original.SlotId, deserialized.SlotId);

        Assert.NotNull(deserialized.LocationData);
        Assert.True(deserialized.LocationData.IsGrid);
        Assert.Equal(3, deserialized.LocationData.GridLocation?.X);
        Assert.Equal(4, deserialized.LocationData.GridLocation?.Y);
        Assert.Equal(1, deserialized.LocationData.GridLocation?.R);

        Assert.NotNull(deserialized.Upd);
        Assert.Equal(30, deserialized.Upd.StackObjectsCount);
        Assert.True(deserialized.Upd.SpawnedInSession);
    }

    [Fact]
    public void SerializedItem_RoundTrips_WithCartridgeLocation()
    {
        // Arrange
        var original = new SerializedItem
        {
            Id = "ammo-item-789",
            Tpl = "5e81f423763d9f754677bf2e",
            ParentId = "magazine-456",
            SlotId = "cartridges",
            LocationData = new LocationConverter.LocationResult
            {
                CartridgeIndex = 15
            },
            Upd = new ItemUpd
            {
                StackObjectsCount = 60
            }
        };

        // Act
        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<SerializedItem>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.LocationData);
        Assert.True(deserialized.LocationData.IsCartridge);
        Assert.Equal(15, deserialized.LocationData.CartridgeIndex);
    }

    [Fact]
    public void SerializedItem_OmitsNullLocation_WhenSerializing()
    {
        // Arrange
        var item = new SerializedItem
        {
            Id = "test-123",
            Tpl = "template-456",
            LocationData = null
        };

        // Act
        var json = JsonConvert.SerializeObject(item);

        // Assert - location should not appear in output at all
        Assert.DoesNotContain("location", json.ToLower());
    }

    // ========================================================================
    // InventorySnapshot Tests
    // ========================================================================

    [Fact]
    public void InventorySnapshot_RoundTrips_CompleteSnapshot()
    {
        // Arrange
        var original = new InventorySnapshot
        {
            SessionId = "session-abc123",
            Timestamp = new DateTime(2024, 12, 15, 10, 30, 0, DateTimeKind.Utc),
            ModVersion = "1.4.9",
            Items = new List<SerializedItem>
            {
                new SerializedItem
                {
                    Id = "weapon-1",
                    Tpl = "5cadc190ae921500103bb3b6",
                    SlotId = "FirstPrimaryWeapon",
                    Upd = new ItemUpd
                    {
                        Repairable = new UpdRepairable { Durability = 87.5, MaxDurability = 100 }
                    }
                },
                new SerializedItem
                {
                    Id = "ammo-1",
                    Tpl = "5e81f423763d9f754677bf2e",
                    ParentId = "magazine-1",
                    SlotId = "cartridges",
                    LocationData = new LocationConverter.LocationResult { CartridgeIndex = 0 },
                    Upd = new ItemUpd { StackObjectsCount = 30 }
                }
            }
        };

        // Act
        var json = JsonConvert.SerializeObject(original, Formatting.Indented);
        var deserialized = JsonConvert.DeserializeObject<InventorySnapshot>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.SessionId, deserialized.SessionId);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.ModVersion, deserialized.ModVersion);
        Assert.Equal(2, deserialized.Items.Count);

        // Verify first item (weapon)
        var weapon = deserialized.Items[0];
        Assert.Equal("weapon-1", weapon.Id);
        Assert.Equal("FirstPrimaryWeapon", weapon.SlotId);
        Assert.NotNull(weapon.Upd?.Repairable);
        Assert.Equal(87.5, weapon.Upd.Repairable.Durability);

        // Verify second item (ammo with cartridge location)
        var ammo = deserialized.Items[1];
        Assert.Equal("ammo-1", ammo.Id);
        Assert.True(ammo.LocationData?.IsCartridge);
        Assert.Equal(0, ammo.LocationData?.CartridgeIndex);
    }

    [Fact]
    public void InventorySnapshot_DeserializesFromRealFormat()
    {
        // Arrange - simulate actual JSON format from game snapshots
        var json = @"{
            ""sessionId"": ""67890abcdef"",
            ""timestamp"": ""2024-12-01T15:30:00Z"",
            ""modVersion"": ""1.4.8"",
            ""items"": [
                {
                    ""_id"": ""item-001"",
                    ""_tpl"": ""5e81f423763d9f754677bf2e"",
                    ""parentId"": ""container-001"",
                    ""slotId"": ""main"",
                    ""location"": {""x"": 0, ""y"": 0, ""r"": 0, ""isSearched"": false},
                    ""upd"": {""StackObjectsCount"": 60}
                },
                {
                    ""_id"": ""item-002"",
                    ""_tpl"": ""5e81f423763d9f754677bf2e"",
                    ""parentId"": ""magazine-001"",
                    ""slotId"": ""cartridges"",
                    ""location"": 5,
                    ""upd"": {""StackObjectsCount"": 1}
                }
            ]
        }";

        // Act
        var snapshot = JsonConvert.DeserializeObject<InventorySnapshot>(json);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal("67890abcdef", snapshot.SessionId);
        Assert.Equal("1.4.8", snapshot.ModVersion);
        Assert.Equal(2, snapshot.Items.Count);

        // First item should have grid location
        Assert.True(snapshot.Items[0].LocationData?.IsGrid);
        Assert.Equal(0, snapshot.Items[0].LocationData?.GridLocation?.X);

        // Second item should have cartridge index
        Assert.True(snapshot.Items[1].LocationData?.IsCartridge);
        Assert.Equal(5, snapshot.Items[1].LocationData?.CartridgeIndex);
    }
}

/// <summary>
/// Helper class for testing LocationConverter in isolation.
/// </summary>
public class TestLocationContainer
{
    [JsonProperty("location")]
    [JsonConverter(typeof(LocationConverter))]
    public LocationConverter.LocationResult? LocationData { get; set; }
}
