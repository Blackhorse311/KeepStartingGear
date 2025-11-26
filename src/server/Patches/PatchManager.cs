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
    // Public API
    // ========================================================================

    /// <summary>
    /// Discovers and enables all ModulePatch classes in the assembly.
    /// Called once during plugin initialization.
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
    /// Errors in individual patches are caught and logged, but don't prevent
    /// other patches from being enabled.
    /// </para>
    /// </remarks>
    public static void EnablePatches()
    {
        Plugin.Log.LogInfo("Enabling patches...");

        int patchCount = 0;

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
                // Log error but continue with other patches
                Plugin.Log.LogError($"Failed to enable patch {patchType.Name}: {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"Enabled {patchCount} patches");
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
