// ============================================================================
// Keep Starting Gear - Patch Manager
// ============================================================================
// This class is responsible for discovering and enabling all Harmony patches
// in the mod. It uses reflection to find all ModulePatch classes and enables
// them automatically during plugin initialization.
//
// PATCHING SYSTEM:
// SPT uses a Harmony-based patching system via SPT.Reflection.Patching.
// ModulePatch is the base class for all patches. Each patch defines:
// - GetTargetMethod(): Which game method to hook
// - PatchPrefix/PatchPostfix: Code to run before/after the target method
//
// AUTOMATIC DISCOVERY:
// Instead of manually registering each patch, PatchManager scans the assembly
// for all ModulePatch subclasses and enables them automatically. This makes
// adding new patches as simple as creating a new class that inherits ModulePatch.
//
// DISABLING PATCHES:
// Apply the [DisablePatch] attribute to any patch class to exclude it from
// automatic loading. Useful for development/debugging or deprecated patches.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SPT.Reflection.Patching;

namespace Blackhorse311.KeepStartingGear.Patches;

/// <summary>
/// Manages automatic discovery and enabling of all Harmony patches for the mod.
/// Scans the assembly for ModulePatch classes and enables them during initialization.
/// </summary>
/// <remarks>
/// <para>
/// This approach provides several benefits:
/// </para>
/// <list type="bullet">
///   <item>New patches are automatically discovered without registration</item>
///   <item>Single point of control for enabling/disabling all patches</item>
///   <item>Clear logging of which patches were loaded</item>
///   <item>Support for excluding patches via [DisablePatch] attribute</item>
/// </list>
/// </remarks>
public static class PatchManager
{
    // ========================================================================
    // Critical Patches (H-03 FIX)
    // ========================================================================

    /// <summary>
    /// List of patch names that are CRITICAL for mod functionality.
    /// If any of these fail to load, the mod should warn the user loudly.
    /// </summary>
    private static readonly HashSet<string> CriticalPatches = new(StringComparer.OrdinalIgnoreCase)
    {
        "RaidEndPatch",      // Required for gear restoration on death
        "GameStartPatch",    // Required for raid start detection
        "RaidExitPatch"      // Required for extraction handling
    };

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Discovers and enables all ModulePatch classes in the assembly.
    /// Called once during plugin initialization.
    /// H-03 FIX: Now tracks critical patch failures and warns users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Patches are discovered using reflection. Each patch class is:
    /// </para>
    /// <list type="number">
    ///   <item>Instantiated via Activator.CreateInstance</item>
    ///   <item>Enabled via the Enable() method</item>
    ///   <item>Logged for debugging purposes</item>
    /// </list>
    /// <para>
    /// Errors in individual patches are caught and logged. Critical patch
    /// failures are logged at ERROR level with user-facing warnings.
    /// </para>
    /// </remarks>
    public static void EnablePatches()
    {
        Plugin.Log.LogDebug("Enabling patches...");

        int patchCount = 0;
        var failedCriticalPatches = new List<string>();
        var failedPatches = new List<string>();

        // Find and enable each patch type
        foreach (var patchType in GetAllPatches())
        {
            try
            {
                // Create an instance of the patch class
                var patch = (ModulePatch)Activator.CreateInstance(patchType);

                // Enable the patch (applies Harmony hooks)
                patch.Enable();

                patchCount++;
                Plugin.Log.LogDebug($"Enabled patch: {patchType.Name}");
            }
            catch (Exception ex)
            {
                // H-03 FIX: Track critical vs non-critical patch failures
                bool isCritical = CriticalPatches.Contains(patchType.Name);

                if (isCritical)
                {
                    failedCriticalPatches.Add(patchType.Name);
                    Plugin.Log.LogError($"CRITICAL: Failed to enable patch {patchType.Name}: {ex.Message}");
                    Plugin.Log.LogError($"CRITICAL: Stack trace: {ex.StackTrace}");
                }
                else
                {
                    failedPatches.Add(patchType.Name);
                    Plugin.Log.LogError($"Failed to enable patch {patchType.Name}: {ex.Message}");
                }
            }
        }

        // H-03 FIX: Report results with appropriate severity
        if (failedCriticalPatches.Count > 0)
        {
            Plugin.Log.LogError($"========================================");
            Plugin.Log.LogError($"CRITICAL: {failedCriticalPatches.Count} CRITICAL PATCH(ES) FAILED TO LOAD!");
            Plugin.Log.LogError($"Failed patches: {string.Join(", ", failedCriticalPatches)}");
            Plugin.Log.LogError($"GEAR RESTORATION MAY NOT WORK!");
            Plugin.Log.LogError($"Please report this issue with your BepInEx/LogOutput.log file.");
            Plugin.Log.LogError($"========================================");
        }

        if (failedPatches.Count > 0)
        {
            Plugin.Log.LogWarning($"Warning: {failedPatches.Count} non-critical patch(es) failed: {string.Join(", ", failedPatches)}");
        }

        Plugin.Log.LogDebug($"Enabled {patchCount} patches ({failedCriticalPatches.Count} critical failures, {failedPatches.Count} non-critical failures)");
    }

    // ========================================================================
    // Private Implementation
    // ========================================================================

    /// <summary>
    /// Discovers all ModulePatch types in the current assembly.
    /// Excludes abstract classes and classes marked with [DisablePatch].
    /// </summary>
    /// <returns>Array of patch types to be enabled</returns>
    private static Type[] GetAllPatches()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t =>
                t.BaseType == typeof(ModulePatch) &&  // Must inherit from ModulePatch
                !t.IsAbstract &&                       // Must not be abstract
                t.GetCustomAttribute<DisablePatchAttribute>() == null)  // Must not be disabled
            .ToArray();
    }
}

// ============================================================================
// Disable Patch Attribute
// ============================================================================

/// <summary>
/// Attribute to mark patches that should not be automatically loaded.
/// Apply this to any ModulePatch class to exclude it from PatchManager.
/// </summary>
/// <remarks>
/// <para>
/// Use cases for disabling patches:
/// </para>
/// <list type="bullet">
///   <item>Development/testing - temporarily disable a patch</item>
///   <item>Deprecated patches - keep code for reference but don't load</item>
///   <item>Conditional patches - enable manually based on configuration</item>
/// </list>
/// </remarks>
/// <example>
/// [DisablePatch]
/// public class MyDisabledPatch : ModulePatch
/// {
///     // This patch will not be loaded by PatchManager
/// }
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public class DisablePatchAttribute : Attribute
{
}
