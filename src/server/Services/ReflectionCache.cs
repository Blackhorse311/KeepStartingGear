// ============================================================================
// Keep Starting Gear - Reflection Cache
// ============================================================================
// Caches PropertyInfo and FieldInfo objects to avoid repeated reflection lookups.
// This improves performance since reflection lookups are expensive operations.
//
// USAGE:
// Instead of: item.GetType().GetProperty("Name")
// Use: ReflectionCache.GetProperty(item.GetType(), "Name")
//
// The cache is thread-safe and uses lazy initialization.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Thread-safe cache for reflection lookups (PropertyInfo, FieldInfo, MethodInfo).
/// Dramatically improves performance when accessing the same members repeatedly.
/// </summary>
public static class ReflectionCache
{
    // ========================================================================
    // Cache Storage
    // ========================================================================

    /// <summary>
    /// Cache for PropertyInfo lookups. Key is (Type, PropertyName).
    /// Uses ConcurrentDictionary for thread-safety.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> _propertyCache = new();

    /// <summary>
    /// Cache for FieldInfo lookups. Key is (Type, FieldName).
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> _fieldCache = new();

    /// <summary>
    /// Cache for MethodInfo lookups. Key is (Type, MethodName).
    /// Note: This does not cache method overloads, only the first match.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), MethodInfo> _methodCache = new();

    /// <summary>
    /// Cache for PropertyInfo lookups with specific BindingFlags.
    /// Key is (Type, PropertyName, BindingFlags hashcode).
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string, int), PropertyInfo> _propertyWithFlagsCache = new();

    /// <summary>
    /// Cache for FieldInfo lookups with specific BindingFlags.
    /// Key is (Type, FieldName, BindingFlags hashcode).
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string, int), FieldInfo> _fieldWithFlagsCache = new();

    // ========================================================================
    // Public API - Property Access
    // ========================================================================

    /// <summary>
    /// Gets a PropertyInfo from cache, or fetches and caches it if not present.
    /// Uses default binding flags (Public | Instance).
    /// </summary>
    /// <param name="type">The type to get the property from</param>
    /// <param name="propertyName">The name of the property</param>
    /// <returns>The PropertyInfo, or null if not found</returns>
    public static PropertyInfo GetProperty(Type type, string propertyName)
    {
        if (type == null || string.IsNullOrEmpty(propertyName))
            return null;

        var key = (type, propertyName);
        return _propertyCache.GetOrAdd(key, k => k.Item1.GetProperty(k.Item2));
    }

    /// <summary>
    /// Gets a PropertyInfo from cache with specific binding flags.
    /// </summary>
    /// <param name="type">The type to get the property from</param>
    /// <param name="propertyName">The name of the property</param>
    /// <param name="bindingFlags">The binding flags to use</param>
    /// <returns>The PropertyInfo, or null if not found</returns>
    public static PropertyInfo GetProperty(Type type, string propertyName, BindingFlags bindingFlags)
    {
        if (type == null || string.IsNullOrEmpty(propertyName))
            return null;

        var key = (type, propertyName, (int)bindingFlags);
        return _propertyWithFlagsCache.GetOrAdd(key, k => k.Item1.GetProperty(k.Item2, (BindingFlags)k.Item3));
    }

    /// <summary>
    /// Tries to get a property value safely using cached reflection.
    /// </summary>
    /// <typeparam name="T">The expected type of the property value</typeparam>
    /// <param name="obj">The object to get the property from</param>
    /// <param name="propertyName">The name of the property</param>
    /// <param name="value">The output value if found</param>
    /// <returns>True if the property was found and has the expected type, false otherwise</returns>
    public static bool TryGetPropertyValue<T>(object obj, string propertyName, out T value)
    {
        value = default;
        if (obj == null)
            return false;

        var prop = GetProperty(obj.GetType(), propertyName);
        if (prop == null)
            return false;

        try
        {
            var rawValue = prop.GetValue(obj);
            if (rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }
        catch (Exception ex)
        {
            // H-01 FIX: Log reflection exceptions instead of silently swallowing
            Plugin.Log?.LogDebug($"[ReflectionCache] TryGetPropertyValue failed for {obj.GetType().Name}.{propertyName}: {ex.Message}");
        }

        return false;
    }

    // ========================================================================
    // Public API - Field Access
    // ========================================================================

    /// <summary>
    /// Gets a FieldInfo from cache, or fetches and caches it if not present.
    /// Uses default binding flags (Public | Instance).
    /// </summary>
    /// <param name="type">The type to get the field from</param>
    /// <param name="fieldName">The name of the field</param>
    /// <returns>The FieldInfo, or null if not found</returns>
    public static FieldInfo GetField(Type type, string fieldName)
    {
        if (type == null || string.IsNullOrEmpty(fieldName))
            return null;

        var key = (type, fieldName);
        return _fieldCache.GetOrAdd(key, k => k.Item1.GetField(k.Item2));
    }

    /// <summary>
    /// Gets a FieldInfo from cache with specific binding flags.
    /// </summary>
    /// <param name="type">The type to get the field from</param>
    /// <param name="fieldName">The name of the field</param>
    /// <param name="bindingFlags">The binding flags to use</param>
    /// <returns>The FieldInfo, or null if not found</returns>
    public static FieldInfo GetField(Type type, string fieldName, BindingFlags bindingFlags)
    {
        if (type == null || string.IsNullOrEmpty(fieldName))
            return null;

        var key = (type, fieldName, (int)bindingFlags);
        return _fieldWithFlagsCache.GetOrAdd(key, k => k.Item1.GetField(k.Item2, (BindingFlags)k.Item3));
    }

    /// <summary>
    /// Tries to get a field value safely using cached reflection.
    /// </summary>
    /// <typeparam name="T">The expected type of the field value</typeparam>
    /// <param name="obj">The object to get the field from</param>
    /// <param name="fieldName">The name of the field</param>
    /// <param name="value">The output value if found</param>
    /// <returns>True if the field was found and has the expected type, false otherwise</returns>
    public static bool TryGetFieldValue<T>(object obj, string fieldName, out T value)
    {
        value = default;
        if (obj == null)
            return false;

        var field = GetField(obj.GetType(), fieldName);
        if (field == null)
            return false;

        try
        {
            var rawValue = field.GetValue(obj);
            if (rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }
        catch (Exception ex)
        {
            // H-01 FIX: Log reflection exceptions instead of silently swallowing
            Plugin.Log?.LogDebug($"[ReflectionCache] TryGetFieldValue failed for {obj.GetType().Name}.{fieldName}: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Tries to get a field value with specific binding flags.
    /// </summary>
    public static bool TryGetFieldValue<T>(object obj, string fieldName, BindingFlags bindingFlags, out T value)
    {
        value = default;
        if (obj == null)
            return false;

        var field = GetField(obj.GetType(), fieldName, bindingFlags);
        if (field == null)
            return false;

        try
        {
            var rawValue = field.GetValue(obj);
            if (rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }
        catch (Exception ex)
        {
            // H-01 FIX: Log reflection exceptions instead of silently swallowing
            Plugin.Log?.LogDebug($"[ReflectionCache] TryGetFieldValue (with flags) failed for {obj.GetType().Name}.{fieldName}: {ex.Message}");
        }

        return false;
    }

    // ========================================================================
    // Public API - Method Access
    // ========================================================================

    /// <summary>
    /// Gets a MethodInfo from cache, or fetches and caches it if not present.
    /// Note: This does not handle method overloads - it returns the first match.
    /// </summary>
    /// <param name="type">The type to get the method from</param>
    /// <param name="methodName">The name of the method</param>
    /// <returns>The MethodInfo, or null if not found</returns>
    public static MethodInfo GetMethod(Type type, string methodName)
    {
        if (type == null || string.IsNullOrEmpty(methodName))
            return null;

        var key = (type, methodName);
        return _methodCache.GetOrAdd(key, k => k.Item1.GetMethod(k.Item2));
    }

    // ========================================================================
    // Public API - Property or Field Access (tries both)
    // ========================================================================

    /// <summary>
    /// Tries to get a value as either a property or field.
    /// Useful when the member might be exposed as either depending on EFT version.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="obj">The object to get the member from</param>
    /// <param name="memberName">The name of the property or field</param>
    /// <param name="value">The output value if found</param>
    /// <returns>True if the member was found and has the expected type, false otherwise</returns>
    public static bool TryGetMemberValue<T>(object obj, string memberName, out T value)
    {
        // Try property first (more common)
        if (TryGetPropertyValue(obj, memberName, out value))
            return true;

        // Fall back to field
        return TryGetFieldValue(obj, memberName, out value);
    }

    /// <summary>
    /// Gets a property or field value, returning null if not found.
    /// H-01 FIX: Now logs reflection access errors instead of silently swallowing them.
    /// </summary>
    /// <param name="obj">The object to get the member from</param>
    /// <param name="memberName">The name of the property or field</param>
    /// <returns>The value, or null if not found or on error</returns>
    public static object GetMemberValue(object obj, string memberName)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();

        // Try property first
        var prop = GetProperty(type, memberName);
        if (prop != null)
        {
            try
            {
                return prop.GetValue(obj);
            }
            catch (Exception ex)
            {
                // H-01 FIX: Log instead of silently ignoring
                Plugin.Log?.LogDebug($"[ReflectionCache] Property access failed for {type.Name}.{memberName}: {ex.Message}");
            }
        }

        // Fall back to field
        var field = GetField(type, memberName);
        if (field != null)
        {
            try
            {
                return field.GetValue(obj);
            }
            catch (Exception ex)
            {
                // H-01 FIX: Log instead of silently ignoring
                Plugin.Log?.LogDebug($"[ReflectionCache] Field access failed for {type.Name}.{memberName}: {ex.Message}");
            }
        }

        return null;
    }

    // ========================================================================
    // Cache Management
    // ========================================================================

    /// <summary>
    /// Clears all cached reflection data.
    /// Useful if types change (e.g., during hot reload scenarios).
    /// </summary>
    public static void ClearCache()
    {
        _propertyCache.Clear();
        _fieldCache.Clear();
        _methodCache.Clear();
        _propertyWithFlagsCache.Clear();
        _fieldWithFlagsCache.Clear();
    }

    /// <summary>
    /// Gets statistics about the cache for debugging purposes.
    /// </summary>
    /// <returns>A dictionary with cache statistics</returns>
    public static Dictionary<string, int> GetCacheStats()
    {
        return new Dictionary<string, int>
        {
            ["Properties"] = _propertyCache.Count,
            ["PropertiesWithFlags"] = _propertyWithFlagsCache.Count,
            ["Fields"] = _fieldCache.Count,
            ["FieldsWithFlags"] = _fieldWithFlagsCache.Count,
            ["Methods"] = _methodCache.Count,
            ["Total"] = _propertyCache.Count + _propertyWithFlagsCache.Count +
                       _fieldCache.Count + _fieldWithFlagsCache.Count + _methodCache.Count
        };
    }
}
