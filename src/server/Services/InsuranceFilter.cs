// ============================================================================
// Keep Starting Gear - Insurance Filter
// ============================================================================
// Determines which items are insured by querying the game's insurance system.
// Extracted from InventoryService (CRIT-1 god class refactor).
//
// KEY RESPONSIBILITIES:
// 1. Build a set of insured item IDs at the start of a capture operation
// 2. Check individual items against cached insurance data
// 3. Locate EFT's InsuranceCompanyClass via multiple reflection paths
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Blackhorse311.KeepStartingGear.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Encapsulates all insurance-related filtering logic for inventory capture.
/// Extracted from InventoryService to isolate the insurance reflection complexity.
/// </summary>
internal class InsuranceFilter
{
    // ========================================================================
    // Private State (per capture operation)
    // ========================================================================

    private HashSet<string> _insuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private object _insuranceCompany = null;
    private MethodInfo _insuredMethod = null;

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Builds the set of insured item IDs for the current capture operation.
    /// Must be called before <see cref="IsInsured"/> for accurate results.
    /// </summary>
    /// <param name="profile">The player's SPT profile</param>
    public void BuildInsuredIdSet(object profile)
    {
        _insuredIds = BuildInsuredItemIdSet(profile);
    }

    /// <summary>
    /// Clears all cached insurance state after a capture operation completes.
    /// </summary>
    public void Clear()
    {
        _insuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _insuranceCompany = null;
        _insuredMethod = null;
    }

    /// <summary>
    /// Checks whether the given item is insured.
    /// </summary>
    /// <param name="item">The EFT Item to check</param>
    /// <returns>True if the item is in the insured set or passes the dynamic check</returns>
    public bool IsInsured(Item item)
    {
        bool isInsured = _insuredIds.Contains(item.Id);

        // Fallback to the dynamic InsuranceCompanyClass.Insured() method
        if (!isInsured && _insuredMethod != null)
        {
            isInsured = IsItemInsured(item.Id);
        }

        return isInsured;
    }

    // ========================================================================
    // Private Implementation
    // ========================================================================

    /// <summary>
    /// Builds a HashSet of insured item IDs by querying the game's InsuranceCompanyClass.
    /// This is the proper EFT way to check insurance status.
    /// </summary>
    /// <param name="profile">The player's profile</param>
    /// <returns>HashSet of insured item IDs (empty if insurance exclusion is disabled)</returns>
    private HashSet<string> BuildInsuredItemIdSet(object profile)
    {
        var insuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only build the set if insurance exclusion is actually enabled
        if (!Settings.ExcludeInsuredItems.Value)
        {
            return insuredIds;
        }

        try
        {
            // Try multiple approaches to find insured items

            // NEW Approach 0: Access EFT Profile.InsuredItems directly via MainPlayer
            // Based on dnSpy analysis: Profile class has public \uE650[] InsuredItems field
            // Each element has ItemId and TraderId properties
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld?.MainPlayer != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Trying MainPlayer.Profile.InsuredItems approach");
                var mainPlayer = gameWorld.MainPlayer;

                // Access Profile
                var profileProp = mainPlayer.GetType().GetProperty("Profile", BindingFlags.Public | BindingFlags.Instance);
                if (profileProp != null)
                {
                    var eftProfile = profileProp.GetValue(mainPlayer);
                    if (eftProfile != null)
                    {
                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - EFT Profile type: {eftProfile.GetType().FullName}");

                        // Try InsuredItems as field (it's a public field, not property)
                        var insuredItemsField = eftProfile.GetType().GetField("InsuredItems", BindingFlags.Public | BindingFlags.Instance);
                        if (insuredItemsField != null)
                        {
                            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuredItems FIELD");
                            var insuredItemsArray = insuredItemsField.GetValue(eftProfile);

                            if (insuredItemsArray != null && insuredItemsArray is Array arr)
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - InsuredItems array length: {arr.Length}");

                                foreach (var insuredItem in arr)
                                {
                                    if (insuredItem == null) continue;

                                    // Get ItemId property (or field)
                                    var itemIdProp = insuredItem.GetType().GetProperty("ItemId")
                                                  ?? insuredItem.GetType().GetProperty("itemId");
                                    var itemIdField = insuredItem.GetType().GetField("ItemId", BindingFlags.Public | BindingFlags.Instance)
                                                   ?? insuredItem.GetType().GetField("itemId", BindingFlags.Public | BindingFlags.Instance);

                                    string itemId = null;
                                    if (itemIdProp != null)
                                    {
                                        itemId = itemIdProp.GetValue(insuredItem) as string;
                                    }
                                    else if (itemIdField != null)
                                    {
                                        itemId = itemIdField.GetValue(insuredItem) as string;
                                    }

                                    if (!string.IsNullOrEmpty(itemId))
                                    {
                                        insuredIds.Add(itemId);
                                        Plugin.Log.LogDebug($"[KSG] Found insured item ID: {itemId}");
                                    }
                                }

                                if (insuredIds.Count > 0)
                                {
                                    Plugin.Log.LogInfo($"[KSG] SUCCESS: Found {insuredIds.Count} insured items from EFT Profile.InsuredItems!");
                                    return insuredIds;
                                }
                            }
                        }
                        else
                        {
                            // Try as property if not found as field
                            var insuredItemsProp = eftProfile.GetType().GetProperty("InsuredItems", BindingFlags.Public | BindingFlags.Instance);
                            if (insuredItemsProp != null)
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuredItems as property");
                                var items = insuredItemsProp.GetValue(eftProfile);
                                if (items != null)
                                {
                                    int count = ExtractInsuredIdsFromObject(items, insuredIds);
                                    if (count > 0)
                                    {
                                        Plugin.Log.LogInfo($"[KSG] Found {count} insured items from Profile.InsuredItems property");
                                        return insuredIds;
                                    }
                                }
                            }
                            else
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - InsuredItems not found as field or property");
                                // Log all fields/props for debugging
                                var fields = eftProfile.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                                var fieldNames = string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(fields, f => f.Name), 20));
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - EFT Profile fields: {fieldNames}");
                            }
                        }
                    }
                }
            }

            // Approach 1: Check for InsuranceInfo on profile (SPT Profile.InsuranceInfo)
            var profileType = profile.GetType();
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile type: {profileType.FullName}");

            // List all properties to help diagnose
            var allProps = profileType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile has {allProps.Length} properties");
            // Log all property names for diagnosis
            var propNames = string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(allProps, p => p.Name), 20));
            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - First 20 props: {propNames}");

            foreach (var prop in allProps)
            {
                if (prop.Name.ToLower().Contains("insur") || prop.PropertyType.Name.ToLower().Contains("insur"))
                {
                    Plugin.Log.LogInfo($"[KSG] FOUND insurance-related property: {prop.Name} ({prop.PropertyType.Name})");
                }
            }

            // Try Profile.InsuranceInfo
            var insuranceInfoProp = profileType.GetProperty("InsuranceInfo", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (insuranceInfoProp != null)
            {
                Plugin.Log.LogInfo($"[KSG] Found Profile.InsuranceInfo property");
                var insuranceInfo = insuranceInfoProp.GetValue(profile);
                if (insuranceInfo != null)
                {
                    Plugin.Log.LogInfo($"[KSG] InsuranceInfo type: {insuranceInfo.GetType().Name}");
                    // Try to enumerate insured items
                    int count = ExtractInsuredIdsFromObject(insuranceInfo, insuredIds);
                    if (count > 0)
                    {
                        Plugin.Log.LogInfo($"[KSG] Found {count} insured items from Profile.InsuranceInfo");
                        return insuredIds;
                    }
                }
            }

            // Approach 2: Try TradersInfo - insurance is managed by Prapor/Therapist
            var tradersInfoProp = profileType.GetProperty("TradersInfo", BindingFlags.Public | BindingFlags.Instance);
            if (tradersInfoProp != null)
            {
                var tradersInfo = tradersInfoProp.GetValue(profile);
                if (tradersInfo != null)
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - TradersInfo type: {tradersInfo.GetType().Name}");

                    // TradersInfo might be a dictionary or have trader-specific properties
                    var tradersType = tradersInfo.GetType();
                    var tradersProps = tradersType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - TradersInfo has {tradersProps.Length} properties: {string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(tradersProps, p => p.Name), 10))}");

                    // Check if it's enumerable (dictionary of traders)
                    if (tradersInfo is System.Collections.IEnumerable tradersEnum)
                    {
                        foreach (var traderEntry in tradersEnum)
                        {
                            if (traderEntry == null) continue;
                            var entryType = traderEntry.GetType();

                            // Look for InsuredItems on each trader
                            var insuredProp = entryType.GetProperty("InsuredItems") ?? entryType.GetProperty("Insured");
                            if (insuredProp != null)
                            {
                                Plugin.Log.LogInfo($"[KSG] Found InsuredItems on trader entry");
                                var traderInsured = insuredProp.GetValue(traderEntry);
                                if (traderInsured != null)
                                {
                                    int count = ExtractInsuredIdsFromObject(traderInsured, insuredIds);
                                    Plugin.Log.LogInfo($"[KSG] Extracted {count} insured IDs from trader");
                                }
                            }
                        }
                    }
                }
            }

            // Approach 3: Try InventoryInfo for insurance data
            var inventoryInfoProp = profileType.GetProperty("InventoryInfo", BindingFlags.Public | BindingFlags.Instance);
            if (inventoryInfoProp != null)
            {
                var inventoryInfo = inventoryInfoProp.GetValue(profile);
                if (inventoryInfo != null)
                {
                    var invType = inventoryInfo.GetType();
                    var invProps = invType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    // Look for insurance-related properties
                    foreach (var prop in invProps)
                    {
                        if (prop.Name.ToLower().Contains("insur"))
                        {
                            Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found on InventoryInfo: {prop.Name} ({prop.PropertyType.Name})");
                            var value = prop.GetValue(inventoryInfo);
                            if (value != null)
                            {
                                int count = ExtractInsuredIdsFromObject(value, insuredIds);
                                if (count > 0)
                                {
                                    Plugin.Log.LogInfo($"[KSG] Found {count} insured items from InventoryInfo.{prop.Name}");
                                    return insuredIds;
                                }
                            }
                        }
                    }
                }
            }

            // Approach 4: Try InsuranceCompanyClass from session/singleton
            _insuranceCompany = GetInsuranceCompanyClass();

            if (_insuranceCompany != null)
            {
                // Cache the Insured method for performance
                _insuredMethod = _insuranceCompany.GetType().GetMethod("Insured", new[] { typeof(string) });

                if (_insuredMethod != null)
                {
                    Plugin.Log.LogInfo("[KSG] Found InsuranceCompanyClass.Insured() method - insurance exclusion will work");

                    // Also try to get the InsuredItems collection to build the ID set
                    var insuredItemsProp = _insuranceCompany.GetType().GetProperty("InsuredItems");
                    if (insuredItemsProp != null)
                    {
                        var insuredItems = insuredItemsProp.GetValue(_insuranceCompany);
                        if (insuredItems is System.Collections.IEnumerable enumerable)
                        {
                            foreach (var itemClass in enumerable)
                            {
                                if (itemClass == null) continue;

                                // ItemClass has an Id property
                                var idProp = itemClass.GetType().GetProperty("Id");
                                if (idProp != null)
                                {
                                    var id = idProp.GetValue(itemClass) as string;
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        insuredIds.Add(id);
                                    }
                                }
                            }
                        }
                    }

                    Plugin.Log.LogInfo($"[KSG] Found {insuredIds.Count} insured items from InsuranceCompanyClass");
                }
                else
                {
                    Plugin.Log.LogWarning("[KSG] InsuranceCompanyClass found but Insured() method not found");
                }
            }
            else
            {
                Plugin.Log.LogWarning("[KSG] Could not find InsuranceCompanyClass - insurance exclusion will not work");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KSG] Error accessing insurance system: {ex.Message}");
        }

        return insuredIds;
    }

    /// <summary>
    /// Maximum recursion depth for insurance object traversal.
    /// EFT's insurance data is shallow (profile -> traders -> items), so 10 is generous.
    /// </summary>
    private const int MaxRecursionDepth = 10;

    /// <summary>
    /// Recursively extracts insured item IDs from an insurance-related object.
    /// Handles various EFT insurance data structures.
    /// Uses depth limit and visited-set to prevent StackOverflowException from circular references.
    /// </summary>
    private int ExtractInsuredIdsFromObject(object obj, HashSet<string> insuredIds,
        int depth = 0, HashSet<object> visited = null)
    {
        if (obj == null || depth > MaxRecursionDepth) return 0;

        // Prevent circular reference traversal (use reference equality to detect cycles)
        if (visited == null)
            visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
        if (!(obj is string) && !visited.Add(obj)) return 0;

        int count = 0;

        try
        {
            var objType = obj.GetType();

            // If it's enumerable, iterate and extract IDs
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;

                    // Try to get Id from item
                    var itemType = item.GetType();
                    var idProp = itemType.GetProperty("Id") ?? itemType.GetProperty("id") ?? itemType.GetProperty("_id");
                    if (idProp != null)
                    {
                        var id = idProp.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(id))
                        {
                            insuredIds.Add(id);
                            count++;
                        }
                    }

                    // Also try ItemId
                    var itemIdProp = itemType.GetProperty("ItemId") ?? itemType.GetProperty("itemId");
                    if (itemIdProp != null)
                    {
                        var itemId = itemIdProp.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(itemId))
                        {
                            insuredIds.Add(itemId);
                            count++;
                        }
                    }

                    // If item has an Items collection, recurse
                    var itemsProp = itemType.GetProperty("Items") ?? itemType.GetProperty("items");
                    if (itemsProp != null)
                    {
                        var items = itemsProp.GetValue(item);
                        if (items != null)
                        {
                            count += ExtractInsuredIdsFromObject(items, insuredIds, depth + 1, visited);
                        }
                    }
                }
            }

            // Check for InsuredItems property
            var insuredItemsProp = objType.GetProperty("InsuredItems") ?? objType.GetProperty("insuredItems");
            if (insuredItemsProp != null)
            {
                var insuredItems = insuredItemsProp.GetValue(obj);
                if (insuredItems != null)
                {
                    count += ExtractInsuredIdsFromObject(insuredItems, insuredIds, depth + 1, visited);
                }
            }

            // Check for Items property
            var itemsP = objType.GetProperty("Items") ?? objType.GetProperty("items");
            if (itemsP != null)
            {
                var items = itemsP.GetValue(obj);
                if (items != null)
                {
                    count += ExtractInsuredIdsFromObject(items, insuredIds, depth + 1, visited);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[KSG] Error extracting insured IDs: {ex.Message}");
        }

        return count;
    }

    /// <summary>
    /// Attempts to get the InsuranceCompanyClass from the game.
    /// This class manages insurance status for items.
    /// Based on dnSpy analysis: session.InsuranceCompany is the access path
    /// </summary>
    private object GetInsuranceCompanyClass()
    {
        try
        {
            // Path 1: Try through MainPlayer - access directly like RaidEndPatch does
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - GameWorld found, type: {gameWorld.GetType().FullName}");

                // Access MainPlayer directly - this works in RaidEndPatch and other code
                var mainPlayer = gameWorld.MainPlayer;
                if (mainPlayer != null)
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - MainPlayer found: {mainPlayer.GetType().Name}");

                    // Search MainPlayer for session or insurance properties
                    var playerProps = mainPlayer.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                    // Log some properties to help diagnose
                    var propNames = System.Linq.Enumerable.Take(
                        System.Linq.Enumerable.Select(
                            System.Linq.Enumerable.Where(playerProps, p => p.Name.Contains("Session") || p.Name.Contains("Insurance") || p.Name.Contains("Profile")),
                            p => p.Name),
                        10);
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Player props with Session/Insurance/Profile: {string.Join(", ", propNames)}");

                    foreach (var prop in playerProps)
                    {
                        // Look for InsuranceCompany directly
                        if (prop.Name == "InsuranceCompany" || prop.PropertyType.Name == "InsuranceCompanyClass")
                        {
                            var value = prop.GetValue(mainPlayer);
                            if (value != null)
                            {
                                Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany on MainPlayer.{prop.Name}");
                                return value;
                            }
                        }

                        // Look for Session that might have InsuranceCompany
                        if (prop.Name.Contains("Session") || prop.PropertyType.Name.Contains("Session"))
                        {
                            try
                            {
                                var session = prop.GetValue(mainPlayer);
                                if (session != null)
                                {
                                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found session on player: {session.GetType().Name}");
                                    var sessionProps = session.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                                    foreach (var sProp in sessionProps)
                                    {
                                        if (sProp.Name == "InsuranceCompany" || sProp.PropertyType.Name.Contains("Insurance"))
                                        {
                                            var insurance = sProp.GetValue(session);
                                            if (insurance != null)
                                            {
                                                Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany via Player.Session.{sProp.Name}");
                                                return insurance;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Session property access failed: {ex.Message}");
                            }
                        }
                    }

                    // Try through Profile owner
                    var profileProp = mainPlayer.GetType().GetProperty("Profile", BindingFlags.Public | BindingFlags.Instance);
                    if (profileProp != null)
                    {
                        var profile = profileProp.GetValue(mainPlayer);
                        if (profile != null)
                        {
                            // Profile doesn't have InsuranceCompany directly, but check anyway
                            var profileProps = profile.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            foreach (var pProp in profileProps)
                            {
                                if (pProp.Name.Contains("Insurance") || pProp.PropertyType.Name.Contains("Insurance"))
                                {
                                    try
                                    {
                                        var insurance = pProp.GetValue(profile);
                                        if (insurance != null)
                                        {
                                            Plugin.Log.LogInfo($"[KSG] Found insurance via Profile.{pProp.Name}");
                                            return insurance;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Profile property access failed: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - MainPlayer is null");
                }
            }

            // Path 2: Try through ClientApplication singleton
            var clientAppType = Type.GetType("EFT.ClientApplication, Assembly-CSharp") ??
                               Type.GetType("ClientApplication, Assembly-CSharp");

            if (clientAppType != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Trying ClientApplication: {clientAppType.FullName}");

                var singletonType = typeof(Singleton<>).MakeGenericType(clientAppType);
                var instanceProp = singletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp != null)
                {
                    var clientApp = instanceProp.GetValue(null);
                    if (clientApp != null)
                    {
                        // Get all properties and log potential session/insurance ones
                        var appProps = clientApp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        var relevantProps = System.Linq.Enumerable.Take(
                            System.Linq.Enumerable.Select(
                                System.Linq.Enumerable.Where(appProps, p => p.Name.Contains("Session") || p.Name.Contains("Insurance")),
                                p => $"{p.Name}:{p.PropertyType.Name}"),
                            10);
                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - ClientApp props: {string.Join(", ", relevantProps)}");

                        foreach (var prop in appProps)
                        {
                            if (prop.Name.Contains("Session") || prop.PropertyType.Name.Contains("Session"))
                            {
                                try
                                {
                                    var session = prop.GetValue(clientApp);
                                    if (session != null)
                                    {
                                        Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Got session from ClientApp: {session.GetType().Name}");

                                        var sessionProps = session.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                        foreach (var sProp in sessionProps)
                                        {
                                            if (sProp.Name == "InsuranceCompany" || sProp.PropertyType.Name.Contains("Insurance"))
                                            {
                                                var insurance = sProp.GetValue(session);
                                                if (insurance != null)
                                                {
                                                    Plugin.Log.LogInfo($"[KSG] Found InsuranceCompany via ClientApp.Session.{sProp.Name}");
                                                    return insurance;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // CRITICAL-002 FIX: Log property access errors for diagnostics
                                    Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Property access error on {prop.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // Path 3: Direct search for InsuranceCompanyClass type
            var insuranceType = Type.GetType("InsuranceCompanyClass, Assembly-CSharp");
            if (insuranceType != null)
            {
                Plugin.Log.LogDebug($"[KSG] INSURANCE DEBUG - Found InsuranceCompanyClass type");

                // Check for static Instance property
                var instanceProp = insuranceType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (instanceProp != null)
                {
                    var instance = instanceProp.GetValue(null);
                    if (instance != null)
                    {
                        Plugin.Log.LogInfo($"[KSG] Found InsuranceCompanyClass.Instance");
                        return instance;
                    }
                }

                // Check for any static field that might hold an instance
                var staticFields = insuranceType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var field in staticFields)
                {
                    if (field.FieldType == insuranceType)
                    {
                        var instance = field.GetValue(null);
                        if (instance != null)
                        {
                            Plugin.Log.LogInfo($"[KSG] Found InsuranceCompanyClass via static field");
                            return instance;
                        }
                    }
                }
            }

            Plugin.Log.LogWarning("[KSG] Could not locate InsuranceCompanyClass through known paths");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KSG] Error searching for InsuranceCompanyClass: {ex.Message}");
            Plugin.Log.LogDebug($"[KSG] Stack: {ex.StackTrace}");
        }

        return null;
    }

    /// <summary>
    /// Checks if an item is insured using the cached InsuranceCompanyClass.
    /// </summary>
    /// <param name="itemId">The item ID to check</param>
    /// <returns>True if the item is insured, false otherwise</returns>
    private bool IsItemInsured(string itemId)
    {
        if (_insuranceCompany == null || _insuredMethod == null)
            return false;

        try
        {
            var result = _insuredMethod.Invoke(_insuranceCompany, new object[] { itemId });
            return result is bool b && b;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"[KSG] IsItemInsured check failed for {itemId}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Reference equality comparer for object cycle detection.
/// Uses ReferenceEquals instead of Equals to detect circular references in object graphs.
/// Needed for .NET Framework 4.7.1 which lacks System.Collections.Generic.ReferenceEqualityComparer.
/// </summary>
internal sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ObjectReferenceEqualityComparer Instance = new ObjectReferenceEqualityComparer();

    private ObjectReferenceEqualityComparer() { }

    public new bool Equals(object x, object y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
