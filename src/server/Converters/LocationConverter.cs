// ============================================================================
// Keep Starting Gear - Location JSON Converter
// ============================================================================
// Handles polymorphic serialization/deserialization of item location data.
//
// SPT profiles use two different location formats:
// - Grid items: {"x": 0, "y": 0, "r": 0, "isSearched": true}
// - Magazine cartridges: 0 (simple integer)
//
// This converter handles both cases transparently.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Blackhorse311.KeepStartingGear.Models;

namespace Blackhorse311.KeepStartingGear.Converters;

/// <summary>
/// Custom JSON converter that handles polymorphic location data.
/// Deserializes either an integer (cartridge index) or an object (grid position).
/// </summary>
public class LocationConverter : JsonConverter
{
    /// <summary>
    /// Result of location deserialization, containing either grid or index data.
    /// </summary>
    public class LocationResult
    {
        /// <summary>Grid position for container items (null if this is a cartridge)</summary>
        public ItemLocation? GridLocation { get; set; }

        /// <summary>Cartridge index for magazine ammo (null if this is a grid item)</summary>
        public int? CartridgeIndex { get; set; }

        /// <summary>True if this represents a cartridge position</summary>
        public bool IsCartridge => CartridgeIndex.HasValue;

        /// <summary>True if this represents a grid position</summary>
        public bool IsGrid => GridLocation != null;
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(LocationResult);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var result = new LocationResult();

        // Check if it's a number (cartridge index)
        if (reader.TokenType == JsonToken.Integer)
        {
            long value = (long)reader.Value!;

            // Bounds check to prevent overflow
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new JsonSerializationException(
                    $"Cartridge location index {value} is outside valid Int32 range");
            }

            result.CartridgeIndex = (int)value;
            return result;
        }

        // Check if it's an object (grid location)
        if (reader.TokenType == JsonToken.StartObject)
        {
            var jObject = JObject.Load(reader);
            result.GridLocation = jObject.ToObject<ItemLocation>(serializer);
            return result;
        }

        throw new JsonSerializationException(
            $"Unexpected token type for location: {reader.TokenType}. Expected Integer or Object.");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var result = (LocationResult)value;

        if (result.IsCartridge)
        {
            writer.WriteValue(result.CartridgeIndex!.Value);
        }
        else if (result.IsGrid)
        {
            serializer.Serialize(writer, result.GridLocation);
        }
        else
        {
            writer.WriteNull();
        }
    }
}

/// <summary>
/// Extension methods for working with LocationResult.
/// </summary>
public static class LocationResultExtensions
{
    /// <summary>
    /// Creates a LocationResult from an ItemLocation (grid position).
    /// </summary>
    public static LocationConverter.LocationResult FromGrid(ItemLocation location)
    {
        return new LocationConverter.LocationResult { GridLocation = location };
    }

    /// <summary>
    /// Creates a LocationResult from a cartridge index.
    /// </summary>
    public static LocationConverter.LocationResult FromIndex(int index)
    {
        return new LocationConverter.LocationResult { CartridgeIndex = index };
    }
}
