// ============================================================================
// Keep Starting Gear - Item Serializer
// ============================================================================
// Converts EFT Item objects to SerializedItem format for snapshot storage.
// Extracted from InventoryService (CRIT-1 god class refactor).
//
// KEY RESPONSIBILITIES:
// 1. Convert a single EFT Item to the SerializedItem snapshot format
// 2. Extract all component data: MedKit HP, Durability, Dogtag, Key uses, etc.
// 3. Detect ammunition and remap slot IDs for magazine cartridges
// 4. Apply FIR and insurance filtering
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Blackhorse311.KeepStartingGear.Configuration;
using Blackhorse311.KeepStartingGear.Constants;
using Blackhorse311.KeepStartingGear.Models;
using EFT.InventoryLogic;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Converts EFT Item objects to the SerializedItem snapshot format.
/// Extracted from InventoryService to isolate the serialization complexity.
/// </summary>
internal class ItemSerializer
{
    // ========================================================================
    // Dependencies
    // ========================================================================

    private readonly InsuranceFilter _insuranceFilter;

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Initializes the serializer with the insurance filter needed for capture-time checks.
    /// </summary>
    /// <param name="insuranceFilter">Populated insurance filter for the current capture</param>
    public ItemSerializer(InsuranceFilter insuranceFilter)
    {
        _insuranceFilter = insuranceFilter;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Converts an EFT Item object to our serializable SerializedItem format.
    /// Extracts all necessary properties for later restoration.
    /// </summary>
    /// <param name="item">The EFT Item to convert</param>
    /// <returns>A SerializedItem representing the item, or null on failure</returns>
    /// <remarks>
    /// <para>
    /// This method extracts the following from each item:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Id:</b> Unique identifier for this specific item instance</item>
    ///   <item><b>Tpl:</b> Template ID (what type of item this is)</item>
    ///   <item><b>ParentId:</b> ID of the container/slot this item is in</item>
    ///   <item><b>SlotId:</b> Name of the slot or grid position</item>
    ///   <item><b>Location:</b> X/Y coordinates and rotation for grid items</item>
    ///   <item><b>Upd:</b> Dynamic properties (stack count, durability, etc.)</item>
    /// </list>
    /// <para>
    /// <b>Special handling for ammunition:</b> EFT uses numeric container IDs for
    /// ammo in magazines (like "4", "8"), but SPT profiles expect "cartridges".
    /// This method detects ammo items and remaps the slotId accordingly.
    /// </para>
    /// </remarks>
    public SerializedItem Convert(Item item)
    {
        try
        {
            // ================================================================
            // FIR (Found-in-Raid) Protection Check
            // If enabled, skip items that are marked as Found-in-Raid to prevent
            // exploiting the mod to duplicate FIR items
            // ================================================================
            if (Settings.ExcludeFIRItems.Value)
            {
                // SpawnedInSession = true means the item was found in the current raid (FIR)
                if (item.SpawnedInSession)
                {
                    if (Settings.EnableDebugMode.Value)
                    {
                        Plugin.Log.LogDebug($"[FIR SKIP] Skipping FIR item: {item.TemplateId}");
                    }
                    return null;
                }
            }

            // ================================================================
            // Insurance Protection Check
            // If enabled, skip items that are insured - let insurance handle them
            // Uses InsuranceCompanyClass from EFT to check insurance status
            // ================================================================
            if (Settings.ExcludeInsuredItems.Value)
            {
                if (_insuranceFilter.IsInsured(item))
                {
                    Plugin.Log.LogInfo($"[KSG] INSURANCE SKIP: Excluding insured item {item.TemplateId} (ID: {item.Id})");
                    return null;
                }
            }

            // Create the basic serialized item with ID and template
            var serialized = new SerializedItem
            {
                Id = item.Id,
                Tpl = item.TemplateId.ToString()
            };

            // ================================================================
            // Special Handling for Equipment Container
            // The Equipment container is the root of the equipment hierarchy.
            // It should NOT have parentId or slotId set because:
            // 1. Its Parent property can create a self-reference (parentId = own ID)
            // 2. Its CurrentAddress.Container.ID returns the session ID, not a slot
            // The server only needs the Equipment ID to remap child item parents.
            // M-01 FIX: Use shared constant instead of local const
            // ================================================================
            if (item.TemplateId.ToString() == TemplateIds.Equipment)
            {
                Plugin.Log.LogDebug($"[EQUIPMENT] Equipment container captured (ID={item.Id}) - skipping parentId/slotId");
                return serialized;
            }

            // ================================================================
            // Parent Information
            // Used to reconstruct the item hierarchy during restoration
            // ================================================================
            if (item.Parent?.Container?.ParentItem != null)
            {
                serialized.ParentId = item.Parent.Container.ParentItem.Id;
            }

            // ================================================================
            // Slot/Container ID
            // This is complex because ammo needs special handling
            // ================================================================
            if (item.CurrentAddress?.Container != null)
            {
                var containerId = item.CurrentAddress.Container.ID;

                // SPT profiles use "cartridges" for ammo in magazines, but EFT
                // internally uses numeric IDs. We need to detect ammo and remap.
                // IMPORTANT: Only remap for ammo INSIDE MAGAZINES, not loose ammo in grids!

                // Check if containerId is a small number (magazine ammo slots are 0-99)
                // M-11 FIX: Use constant instead of magic number
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < TemplateIds.MaxNumericSlotId;

                // Determine if this item is ammunition
                bool isAmmoItem = DetectIfAmmo(item);

                // Check if parent item is a magazine (only remap ammo in magazines, not in grids)
                bool parentIsMagazine = false;
                var parentItem = item.Parent?.Container?.ParentItem;
                if (parentItem != null)
                {
                    var parentTypeName = parentItem.GetType().Name;
                    parentIsMagazine = parentTypeName.Contains("Magazine") || parentTypeName.Contains("MagazineItem");

                    // Also check if parent has a "Cartridges" property (definitive magazine indicator)
                    if (!parentIsMagazine)
                    {
                        var cartridgesProp = parentItem.GetType().GetProperty("Cartridges");
                        parentIsMagazine = cartridgesProp != null;
                    }
                }

                // Log detailed debug info for stackable items
                if (item.StackObjectsCount > 1 || isNumericSlot)
                {
                    LogAmmoDebugInfo(item, containerId, isNumericSlot, isAmmoItem, serialized);
                    Plugin.Log.LogDebug($"[AMMO DEBUG] parentIsMagazine={parentIsMagazine}, parentType={parentItem?.GetType().Name ?? "null"}");
                }

                // Remap numeric slot IDs to "cartridges" ONLY for ammo INSIDE MAGAZINES
                // Loose ammo in grids (rigs, backpacks, pockets) should keep the grid slot ID
                if (isNumericSlot && isAmmoItem && parentIsMagazine)
                {
                    serialized.SlotId = "cartridges";
                    Plugin.Log.LogDebug($"[AMMO] Remapped slotId from '{containerId}' to 'cartridges' for ammo in magazine");
                }
                else
                {
                    // Keep original slot ID for:
                    // - Non-ammo items
                    // - Loose ammo in grid containers (rigs, backpacks, pockets)
                    // - Any item not in a magazine
                    serialized.SlotId = containerId;

                    if (isAmmoItem && isNumericSlot && !parentIsMagazine)
                    {
                        Plugin.Log.LogDebug($"[AMMO] Keeping original slotId '{containerId}' for loose ammo in grid container");
                    }
                }
            }

            // ================================================================
            // Grid Location
            // For items in container grids, capture X/Y position and rotation
            // ================================================================
            if (item.CurrentAddress != null)
            {
                try
                {
                    var addressType = item.CurrentAddress.GetType();
                    Plugin.Log.LogDebug($"[LOCATION] Item {item.TemplateId} address type: {addressType.Name}");

                    // GClass3393 (base class for grid addresses) has LocationInGrid as a PUBLIC FIELD, not property!
                    // Try fields first, then properties for compatibility
                    object location = null;

                    var locationField = addressType.GetField("LocationInGrid")
                                     ?? addressType.GetField("Location");
                    if (locationField != null)
                    {
                        location = locationField.GetValue(item.CurrentAddress);
                    }
                    else
                    {
                        var locationProp = addressType.GetProperty("LocationInGrid")
                                        ?? addressType.GetProperty("Location");
                        if (locationProp != null)
                        {
                            location = locationProp.GetValue(item.CurrentAddress);
                        }
                    }

                    if (location != null)
                    {
                        Plugin.Log.LogDebug($"[LOCATION] Found location object for {item.TemplateId}: {location.GetType().Name}");
                        var locationType = location.GetType();

                        // Try both properties and fields (LocationInGrid uses public fields, not properties)
                        object x = null, y = null, r = null;

                        // Try properties first
                        var xProp = locationType.GetProperty("x") ?? locationType.GetProperty("X");
                        var yProp = locationType.GetProperty("y") ?? locationType.GetProperty("Y");
                        var rProp = locationType.GetProperty("r") ?? locationType.GetProperty("R");

                        if (xProp != null) x = xProp.GetValue(location);
                        if (yProp != null) y = yProp.GetValue(location);
                        if (rProp != null) r = rProp.GetValue(location);

                        // Fall back to fields if properties not found (LocationInGrid uses public fields)
                        if (x == null)
                        {
                            var xField = locationType.GetField("x") ?? locationType.GetField("X");
                            if (xField != null) x = xField.GetValue(location);
                        }
                        if (y == null)
                        {
                            var yField = locationType.GetField("y") ?? locationType.GetField("Y");
                            if (yField != null) y = yField.GetValue(location);
                        }
                        if (r == null)
                        {
                            var rField = locationType.GetField("r") ?? locationType.GetField("R");
                            if (rField != null) r = rField.GetValue(location);
                        }

                        if (x != null && y != null)
                        {
                            serialized.Location = new ItemLocation
                            {
                                X = System.Convert.ToInt32(x),
                                Y = System.Convert.ToInt32(y),
                                R = r != null ? System.Convert.ToInt32(r) : 0,
                                IsSearched = true
                            };

                            // Log location capture only in verbose mode
                            if (Settings.VerboseCaptureLogging?.Value == true)
                                Plugin.Log.LogDebug($"[LOCATION] Captured grid position for {item.TemplateId}: X={serialized.Location.X}, Y={serialized.Location.Y}, R={serialized.Location.R}");
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"[LOCATION] Location object found but X or Y is null for {item.TemplateId}: x={x}, y={y}");
                        }
                    }
                    else
                    {
                        // This is normal for slot items (weapons in equipment slots, etc.)
                        // Only log for items that seem like grid items based on SlotId
                        var containerId = item.CurrentAddress.Container?.ID;
                        if (containerId != null &&
                            (containerId.StartsWith("main") || int.TryParse(containerId, out _)))
                        {
                            Plugin.Log.LogWarning($"[LOCATION] No LocationInGrid property for grid item {item.TemplateId} (SlotId={containerId}, AddressType={addressType.Name})");
                            // Log ALL available properties for debugging
                            var props = addressType.GetProperties();
                            var propDescs = new string[props.Length];
                            for (int i = 0; i < props.Length; i++)
                                propDescs[i] = $"{props[i].Name}:{props[i].PropertyType.Name}";
                            Plugin.Log.LogDebug($"[LOCATION] Available properties on {addressType.Name}: {string.Join(", ", propDescs)}");

                            // Also try to find any property/field with "Location" in the name or that returns a struct/class with x/y
                            foreach (var prop in addressType.GetProperties())
                            {
                                try
                                {
                                    var val = prop.GetValue(item.CurrentAddress);
                                    if (val != null)
                                    {
                                        var valType = val.GetType();

                                        // Try properties first, then fields (LocationInGrid uses public fields)
                                        object px = null, py = null, pr = null;

                                        var xProp2 = valType.GetProperty("x") ?? valType.GetProperty("X");
                                        var yProp2 = valType.GetProperty("y") ?? valType.GetProperty("Y");
                                        var rProp2 = valType.GetProperty("r") ?? valType.GetProperty("R");

                                        if (xProp2 != null) px = xProp2.GetValue(val);
                                        if (yProp2 != null) py = yProp2.GetValue(val);
                                        if (rProp2 != null) pr = rProp2.GetValue(val);

                                        // Fall back to fields
                                        if (px == null)
                                        {
                                            var xField2 = valType.GetField("x") ?? valType.GetField("X");
                                            if (xField2 != null) px = xField2.GetValue(val);
                                        }
                                        if (py == null)
                                        {
                                            var yField2 = valType.GetField("y") ?? valType.GetField("Y");
                                            if (yField2 != null) py = yField2.GetValue(val);
                                        }
                                        if (pr == null)
                                        {
                                            var rField2 = valType.GetField("r") ?? valType.GetField("R");
                                            if (rField2 != null) pr = rField2.GetValue(val);
                                        }

                                        if (px != null && py != null)
                                        {
                                            Plugin.Log.LogDebug($"[LOCATION] FOUND! Property '{prop.Name}' has x={px}, y={py}");

                                            // Use this location!
                                            serialized.Location = new ItemLocation
                                            {
                                                X = System.Convert.ToInt32(px),
                                                Y = System.Convert.ToInt32(py),
                                                R = pr != null ? System.Convert.ToInt32(pr) : 0,
                                                IsSearched = true
                                            };
                                            Plugin.Log.LogDebug($"[LOCATION] Captured via '{prop.Name}': X={serialized.Location.X}, Y={serialized.Location.Y}, R={serialized.Location.R}");
                                            break;
                                        }
                                    }
                                }
                                catch (Exception propEx)
                                {
                                    // Property inspection error - log at debug level only if verbose logging enabled
                                    if (Settings.VerboseCaptureLogging?.Value == true)
                                        Plugin.Log.LogDebug($"[KSG] Property '{prop.Name}' inspection failed: {propEx.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Location capture failed - log for debugging but continue
                    if (Settings.EnableDebugMode.Value)
                    {
                        Plugin.Log.LogWarning($"[LOCATION] Failed to capture grid position for {item.TemplateId}: {ex.Message}");
                    }
                }
            }

            // ================================================================
            // Update Data (Upd)
            // Contains dynamic properties like stack count, durability, etc.
            // ================================================================
            var upd = new ItemUpd
            {
                StackObjectsCount = item.StackObjectsCount >= 1 ? (long?)item.StackObjectsCount : null,
                SpawnedInSession = item.SpawnedInSession
            };

            // Log stack count for debugging ammunition issues (only in verbose mode)
            if (item.StackObjectsCount > 1 && (Settings.VerboseCaptureLogging?.Value == true))
            {
                Plugin.Log.LogDebug($"[AMMO DEBUG] Item {item.TemplateId} has StackObjectsCount={item.StackObjectsCount}, ParentId={serialized.ParentId}, SlotId={serialized.SlotId}");
            }

            // Capture foldable state for folding weapons (stocks)
            try
            {
                var foldable = typeof(Item).GetProperty("Foldable")?.GetValue(item);
                if (foldable != null)
                {
                    var folded = foldable.GetType().GetProperty("Folded")?.GetValue(foldable);
                    if (folded is bool isFolded)
                    {
                        upd.Foldable = new UpdFoldable { Folded = isFolded };
                    }
                }
            }
            catch (Exception ex)
            {
                // Foldable capture is non-critical - log at debug level
                Plugin.Log.LogDebug($"[KSG] Foldable capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture MedKit HP (IFAK, AFAK, Grizzly, Surv12, CMS, etc.)
            // EFT stores current HP as a field on MedKitComponent (not a property)
            // This applies to ALL medical items including surgical kits
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Access the Components field to find MedKitComponent (using cached reflection)
                // NOTE: Check ALL items, not just those with "MedKit" in name
                // Surgical kits (Surv12, CMS) also use MedKitComponent but have different type names
                var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            if (comp != null && comp.GetType().Name.Contains("MedKit"))
                            {
                                // HpResource is a FIELD on the component, not a property (using cached reflection)
                                var hpField = ReflectionCache.GetField(comp.GetType(), "HpResource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (hpField != null)
                                {
                                    var hp = hpField.GetValue(comp);
                                    if (hp != null)
                                    {
                                        upd.MedKit = new UpdMedKit { HpResource = System.Convert.ToDouble(hp) };
                                        Plugin.Log.LogDebug($"Captured MedKit HP: {hp} for {item.TemplateId} (Type: {itemType.Name})");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"MedKit capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Repairable durability (armor, weapons)
            // EFT stores Durability and MaxDurability as fields on RepairableComponent
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Access Components field to find RepairableComponent (using cached reflection)
                var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsField != null)
                {
                    var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                    if (components != null)
                    {
                        foreach (var comp in components)
                        {
                            var compType = comp?.GetType();
                            if (compType != null && compType.Name.Contains("Repairable"))
                            {
                                // Durability and MaxDurability are FIELDS on the component (using cached reflection)
                                var durField = ReflectionCache.GetField(compType, "Durability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var maxDurField = ReflectionCache.GetField(compType, "MaxDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                                if (durField != null)
                                {
                                    var dur = durField.GetValue(comp);
                                    var maxDur = maxDurField?.GetValue(comp);
                                    if (dur != null)
                                    {
                                        upd.Repairable = new UpdRepairable
                                        {
                                            Durability = System.Convert.ToDouble(dur),
                                            MaxDurability = maxDur != null ? System.Convert.ToDouble(maxDur) : 100
                                        };
                                        Plugin.Log.LogDebug($"Captured durability: {dur}/{maxDur} for {item.TemplateId}");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Durability capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Resource value (fuel cans, etc.) - using cached reflection
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var resourceProp = ReflectionCache.GetProperty(itemType, "ResourceComponent") ??
                                   ReflectionCache.GetProperty(itemType, "Resource");
                if (resourceProp != null)
                {
                    var resourceValue = resourceProp.GetValue(item);
                    if (resourceValue != null)
                    {
                        var valueProp = ReflectionCache.GetProperty(resourceValue.GetType(), "Value");
                        if (valueProp != null)
                        {
                            var val = valueProp.GetValue(resourceValue);
                            if (val != null)
                            {
                                upd.Resource = new UpdResource { Value = System.Convert.ToDouble(val) };
                                if (Settings.VerboseCaptureLogging?.Value == true)
                                    Plugin.Log.LogDebug($"[RESOURCE] Captured Value={val} for {item.TemplateId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Resource capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] Resource capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture FoodDrink value - using cached reflection
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var foodDrinkProp = ReflectionCache.GetProperty(itemType, "FoodDrinkComponent") ??
                                    ReflectionCache.GetProperty(itemType, "FoodDrink");
                if (foodDrinkProp != null)
                {
                    var foodDrinkValue = foodDrinkProp.GetValue(item);
                    if (foodDrinkValue != null)
                    {
                        var hpProp = ReflectionCache.GetProperty(foodDrinkValue.GetType(), "HpPercent");
                        if (hpProp != null)
                        {
                            var hp = hpProp.GetValue(foodDrinkValue);
                            if (hp != null)
                            {
                                upd.FoodDrink = new UpdFoodDrink { HpPercent = System.Convert.ToDouble(hp) };
                                if (Settings.VerboseCaptureLogging?.Value == true)
                                    Plugin.Log.LogDebug($"[FOODDRINK] Captured HpPercent={hp} for {item.TemplateId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // FoodDrink capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] FoodDrink capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Dogtag metadata (kill information)
            // Dogtags store important data about who was killed, by whom, when
            // Without this data, dogtags appear "wiped" or invalid
            // ================================================================
            try
            {
                var itemType = item.GetType();
                var templateId = item.TemplateId.ToString();

                bool isDogtag = templateId == TemplateIds.DogtagBear ||
                               templateId == TemplateIds.DogtagUsec ||
                               itemType.Name.Contains("Dogtag");

                if (isDogtag)
                {
                    // Try to find DogtagComponent in the item's components (using cached reflection)
                    var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                var compType = comp?.GetType();
                                if (compType != null && compType.Name.Contains("Dogtag"))
                                {
                                    upd.Dogtag = new UpdDogtag();

                                    // Extract all dogtag properties via cached reflection
                                    var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                                    var accountIdField = ReflectionCache.GetField(compType, "AccountId", bindingFlags);
                                    var profileIdField = ReflectionCache.GetField(compType, "ProfileId", bindingFlags);
                                    var nicknameField = ReflectionCache.GetField(compType, "Nickname", bindingFlags);
                                    var sideField = ReflectionCache.GetField(compType, "Side", bindingFlags);
                                    var levelField = ReflectionCache.GetField(compType, "Level", bindingFlags);
                                    var timeField = ReflectionCache.GetField(compType, "Time", bindingFlags);
                                    var statusField = ReflectionCache.GetField(compType, "Status", bindingFlags);
                                    var killerAccountIdField = ReflectionCache.GetField(compType, "KillerAccountId", bindingFlags);
                                    var killerProfileIdField = ReflectionCache.GetField(compType, "KillerProfileId", bindingFlags);
                                    var killerNameField = ReflectionCache.GetField(compType, "KillerName", bindingFlags);
                                    var weaponNameField = ReflectionCache.GetField(compType, "WeaponName", bindingFlags);

                                    upd.Dogtag.AccountId = accountIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.ProfileId = profileIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Nickname = nicknameField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Side = sideField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Level = levelField != null ? System.Convert.ToInt32(levelField.GetValue(comp)) : 0;
                                    upd.Dogtag.Time = timeField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.Status = statusField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerAccountId = killerAccountIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerProfileId = killerProfileIdField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.KillerName = killerNameField?.GetValue(comp)?.ToString();
                                    upd.Dogtag.WeaponName = weaponNameField?.GetValue(comp)?.ToString();

                                    Plugin.Log.LogDebug($"[DOGTAG] Captured dogtag metadata: {upd.Dogtag.Nickname} (Level {upd.Dogtag.Level}) killed by {upd.Dogtag.KillerName}");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Dogtag capture failed for {item.TemplateId}: {ex.Message}");
            }

            // ================================================================
            // Capture Key uses remaining - using cached reflection
            // Some keys have limited uses before they break
            // ================================================================
            try
            {
                var itemType = item.GetType();

                // Check if this is a key item
                if (itemType.Name.Contains("Key"))
                {
                    var componentsField = ReflectionCache.GetField(itemType, "Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (componentsField != null)
                    {
                        var components = componentsField.GetValue(item) as System.Collections.IEnumerable;
                        if (components != null)
                        {
                            foreach (var comp in components)
                            {
                                var compType = comp?.GetType();
                                if (compType != null && compType.Name.Contains("Key"))
                                {
                                    var numberOfUsagesField = ReflectionCache.GetField(compType, "NumberOfUsages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (numberOfUsagesField != null)
                                    {
                                        var uses = numberOfUsagesField.GetValue(comp);
                                        if (uses != null)
                                        {
                                            upd.Key = new UpdKey { NumberOfUsages = System.Convert.ToInt32(uses) };
                                            if (Settings.VerboseCaptureLogging?.Value == true)
                                                Plugin.Log.LogDebug($"[KEY] Captured NumberOfUsages={uses} for {item.TemplateId}");
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Key capture is non-critical - log at debug level
                if (Settings.VerboseCaptureLogging?.Value == true)
                    Plugin.Log.LogDebug($"[KSG] Key capture failed for {item.TemplateId}: {ex.Message}");
            }

            serialized.Upd = upd;

            if (Settings.EnableDebugMode.Value)
            {
                Plugin.Log.LogDebug($"  Item: {item.Template?.NameLocalizationKey ?? "Unknown"} (Tpl: {serialized.Tpl}, ID: {serialized.Id})");
            }

            return serialized;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to convert item (tpl={item?.TemplateId ?? "unknown"}): {ex.Message}");
            return null;
        }
    }

    // ========================================================================
    // Ammo Detection Helpers
    // ========================================================================

    /// <summary>
    /// Determines if an item is ammunition by checking various indicators.
    /// Uses multiple detection methods for reliability across EFT versions.
    /// </summary>
    /// <param name="item">The item to check</param>
    /// <returns>True if the item appears to be ammunition</returns>
    /// <remarks>
    /// Detection methods tried in order:
    /// <list type="number">
    ///   <item>Type hierarchy - check if class name contains "Ammo" or "Bullet"</item>
    ///   <item>Template type hierarchy - check template class name</item>
    ///   <item>Template parent ID - all ammo has parent "5485a8684bdc2da71d8b4567"</item>
    ///   <item>Stack size heuristic - ammo typically has high stack sizes (20-60+)</item>
    /// </list>
    /// </remarks>
    public bool DetectIfAmmo(Item item)
    {
        bool isAmmoItem = false;

        try
        {
            // Method 1: Check item type hierarchy (Ammo inherits from StackableItem)
            var currentType = item.GetType();
            while (currentType != null && !isAmmoItem)
            {
                if (currentType.Name.Contains("Ammo") || currentType.Name.Contains("Bullet"))
                {
                    isAmmoItem = true;
                }
                currentType = currentType.BaseType;
            }

            // Method 2: Check template type hierarchy
            if (!isAmmoItem && item.Template != null)
            {
                currentType = item.Template.GetType();
                while (currentType != null && !isAmmoItem)
                {
                    if (currentType.Name.Contains("Ammo") || currentType.Name.Contains("Bullet"))
                    {
                        isAmmoItem = true;
                    }
                    currentType = currentType.BaseType;
                }
            }

            // Method 3: Check by template parent ID (all ammo descends from this)
            if (!isAmmoItem && item.Template != null)
            {
                var parentProp = item.Template.GetType().GetProperty("Parent");
                var parent = parentProp?.GetValue(item.Template)?.ToString();
                if (parent == "5485a8684bdc2da71d8b4567")
                {
                    isAmmoItem = true;
                    Plugin.Log.LogDebug($"[AMMO] Detected ammo by template parent ID");
                }
            }

            // Method 4: Stack size heuristic (ammo typically stacks to 20-60+)
            if (!isAmmoItem && item.Template != null)
            {
                var stackMax = item.Template.StackMaxSize;
                var containerId = item.CurrentAddress?.Container?.ID;
                // M-11 FIX: Use constant instead of magic number
                bool isNumericSlot = int.TryParse(containerId, out int slotNum) && slotNum >= 0 && slotNum < TemplateIds.MaxNumericSlotId;

                // High stack size AND in numeric slot = probably ammo
                if (stackMax >= 20 && isNumericSlot)
                {
                    isAmmoItem = true;
                    Plugin.Log.LogDebug($"[AMMO] Detected ammo by StackMaxSize={stackMax} and numeric slot");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AMMO] Type check exception: {ex.Message}");
        }

        return isAmmoItem;
    }

    /// <summary>
    /// Logs detailed debug information about an item for ammunition diagnostics.
    /// Helps troubleshoot issues with ammo capture and restoration.
    /// </summary>
    /// <param name="item">The item being logged</param>
    /// <param name="containerId">The container ID from the item's address</param>
    /// <param name="isNumericSlot">Whether the container ID is numeric</param>
    /// <param name="isAmmoItem">Whether the item was detected as ammo</param>
    /// <param name="serialized">The serialized item being built</param>
    private void LogAmmoDebugInfo(Item item, string containerId, bool isNumericSlot, bool isAmmoItem, SerializedItem serialized)
    {
        var parentItem = item.Parent?.Container?.ParentItem;
        var parentTypeName = parentItem?.GetType().Name ?? "null";
        var templateTypeName = item.Template?.GetType().Name ?? "null";

        // Build full type hierarchy for debugging
        var typeHierarchy = new List<string>();
        var t = item.GetType();
        while (t != null)
        {
            typeHierarchy.Add(t.Name);
            t = t.BaseType;
        }

        Plugin.Log.LogDebug($"[AMMO DEBUG] containerId='{containerId}', isNumeric={isNumericSlot}, isAmmo={isAmmoItem}");
        Plugin.Log.LogDebug($"[AMMO DEBUG] itemTypeHierarchy={string.Join(" -> ", typeHierarchy)}");
        Plugin.Log.LogDebug($"[AMMO DEBUG] templateType={templateTypeName}, parentType={parentTypeName}");
    }
}
