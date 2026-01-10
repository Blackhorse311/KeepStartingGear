// ============================================================================
// Keep Starting Gear - Test Models
// ============================================================================
// Standalone model classes for unit testing serialization logic.
// These duplicate the essential structure of the main models but without
// dependencies on EFT/BepInEx assemblies.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Blackhorse311.KeepStartingGear.Tests.Models;

/// <summary>
/// Test version of ItemLocation for grid position serialization tests.
/// </summary>
public class ItemLocation
{
    [JsonProperty("x")]
    public int X { get; set; }

    [JsonProperty("y")]
    public int Y { get; set; }

    [JsonProperty("r")]
    public int R { get; set; }

    [JsonProperty("isSearched")]
    public bool IsSearched { get; set; }
}

/// <summary>
/// Test version of SerializedItem for snapshot serialization tests.
/// </summary>
public class SerializedItem
{
    [JsonProperty("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("_tpl")]
    public string Tpl { get; set; } = string.Empty;

    [JsonProperty("parentId")]
    public string? ParentId { get; set; }

    [JsonProperty("slotId")]
    public string? SlotId { get; set; }

    /// <summary>
    /// Grid location for items in containers (x, y, rotation).
    /// </summary>
    [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(LocationConverter))]
    public LocationConverter.LocationResult? LocationData { get; set; }

    /// <summary>
    /// Dynamic item properties (stack count, durability, etc.)
    /// </summary>
    [JsonProperty("upd", NullValueHandling = NullValueHandling.Ignore)]
    public ItemUpd? Upd { get; set; }
}

/// <summary>
/// Test version of ItemUpd for dynamic property serialization.
/// </summary>
public class ItemUpd
{
    [JsonProperty("StackObjectsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackObjectsCount { get; set; }

    [JsonProperty("SpawnedInSession", NullValueHandling = NullValueHandling.Ignore)]
    public bool? SpawnedInSession { get; set; }

    [JsonProperty("Repairable", NullValueHandling = NullValueHandling.Ignore)]
    public UpdRepairable? Repairable { get; set; }
}

/// <summary>
/// Durability information for repairable items.
/// </summary>
public class UpdRepairable
{
    [JsonProperty("Durability")]
    public double Durability { get; set; }

    [JsonProperty("MaxDurability")]
    public double MaxDurability { get; set; }
}

/// <summary>
/// Test version of InventorySnapshot.
/// </summary>
public class InventorySnapshot
{
    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonProperty("items")]
    public List<SerializedItem> Items { get; set; } = new();

    [JsonProperty("modVersion")]
    public string? ModVersion { get; set; }
}

/// <summary>
/// Custom JsonConverter for handling polymorphic location serialization.
/// Location can be either:
/// - An integer (cartridge position in magazine)
/// - An object with x, y, r properties (grid position in container)
/// </summary>
public class LocationConverter : JsonConverter
{
    /// <summary>
    /// Result of location parsing - either a grid location or cartridge index.
    /// </summary>
    public class LocationResult
    {
        /// <summary>Grid position (x, y, rotation) for container items.</summary>
        public ItemLocation? GridLocation { get; set; }

        /// <summary>Cartridge index for ammunition in magazines.</summary>
        public int? CartridgeIndex { get; set; }

        /// <summary>True if this location represents a cartridge position.</summary>
        public bool IsCartridge => CartridgeIndex.HasValue;

        /// <summary>True if this location represents a grid position.</summary>
        public bool IsGrid => GridLocation != null;
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(LocationResult);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var result = new LocationResult();

        if (reader.TokenType == JsonToken.Integer)
        {
            // Handle integer (cartridge index) with bounds checking
            var longValue = (long)reader.Value!;
            if (longValue < int.MinValue || longValue > int.MaxValue)
            {
                // Log warning about overflow, use 0 as fallback
                result.CartridgeIndex = 0;
            }
            else
            {
                result.CartridgeIndex = (int)longValue;
            }
        }
        else if (reader.TokenType == JsonToken.StartObject)
        {
            // Handle object (grid location)
            result.GridLocation = serializer.Deserialize<ItemLocation>(reader);
        }

        return result;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var location = (LocationResult)value;

        if (location.IsCartridge)
        {
            writer.WriteValue(location.CartridgeIndex!.Value);
        }
        else if (location.IsGrid)
        {
            serializer.Serialize(writer, location.GridLocation);
        }
        else
        {
            writer.WriteNull();
        }
    }
}
