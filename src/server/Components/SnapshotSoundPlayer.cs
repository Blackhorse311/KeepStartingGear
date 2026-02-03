// ============================================================================
// Keep Starting Gear - Snapshot Sound Player
// ============================================================================
// Plays a sound when snapshots are taken using EFT's built-in UI sound system.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.Reflection;
using Comfort.Common;

namespace Blackhorse311.KeepStartingGear.Components;

/// <summary>
/// Plays a sound effect when snapshots are taken.
/// Uses EFT's GUISounds system for the skill/XP gain sound.
/// </summary>
public static class SnapshotSoundPlayer
{
    // HIGH-005 FIX: ALL cached fields must be volatile for correct DCL pattern
    // Without volatile, other threads can see _initialized=true but stale null values
    // for the other fields due to CPU/compiler reordering
    private static volatile bool _initialized;
    private static readonly object _initLock = new();
    private static volatile object _guiSoundsInstance;
    private static volatile MethodInfo _playMethod;
    private static volatile Type _soundEnumType;
    private static volatile object _skillSoundValue;

    /// <summary>
    /// Plays the skill/XP gain sound for the snapshot.
    /// Uses EFT's GUISounds singleton if available.
    /// </summary>
    public static void PlaySnapshotSound()
    {
        try
        {
            // Check if sound is enabled
            if (!Configuration.Settings.PlaySnapshotSound.Value)
                return;

            // CON-003: Thread-safe double-check locking for initialization
            if (!_initialized)
            {
                lock (_initLock)
                {
                    if (!_initialized)
                    {
                        Initialize();
                    }
                }
            }

            if (_guiSoundsInstance != null && _playMethod != null && _skillSoundValue != null)
            {
                try
                {
                    // Play the skill/XP gain sound
                    _playMethod.Invoke(_guiSoundsInstance, new object[] { _skillSoundValue });
                    Plugin.Log.LogDebug("Played snapshot sound (skill/XP sound)");
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"Failed to play sound: {ex.Message}");
                }
            }

            // Fallback: Just log that sound would have played
            Plugin.Log.LogDebug("Snapshot sound triggered (sound system unavailable)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not play snapshot sound: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the sound system by finding EFT's GUISounds singleton
    /// and the skill/XP sound enum value.
    /// </summary>
    private static void Initialize()
    {
        _initialized = true;

        try
        {
            // Try to find GUISounds type
            var guiSoundsType = Type.GetType("EFT.UI.GUISounds, Assembly-CSharp");
            if (guiSoundsType == null)
            {
                // Try alternate assembly name
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    guiSoundsType = assembly.GetType("EFT.UI.GUISounds");
                    if (guiSoundsType != null) break;
                }
            }

            if (guiSoundsType == null)
            {
                Plugin.Log.LogDebug("Could not find GUISounds type");
                return;
            }

            // Try to get singleton instance via Comfort.Common.Singleton
            var singletonType = typeof(Singleton<>).MakeGenericType(guiSoundsType);
            var instanceProp = singletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp != null)
            {
                _guiSoundsInstance = instanceProp.GetValue(null);
            }

            if (_guiSoundsInstance == null)
            {
                Plugin.Log.LogDebug("Could not get GUISounds instance");
                return;
            }

            _playMethod = guiSoundsType.GetMethod("PlayUISound");
            if (_playMethod == null)
            {
                Plugin.Log.LogDebug("Could not find PlayUISound method");
                return;
            }

            // Find the EUISoundType enum and get the skill/experience sound value
            // Try common names for the skill level up / XP sound
            _soundEnumType = Type.GetType("EFT.UI.EUISoundType, Assembly-CSharp");
            if (_soundEnumType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _soundEnumType = assembly.GetType("EFT.UI.EUISoundType");
                    if (_soundEnumType != null) break;
                }
            }

            if (_soundEnumType != null && _soundEnumType.IsEnum)
            {
                // Use the QuestSubTrackComplete sound - the skill/XP gain chime
                const string targetSound = "QuestSubTrackComplete";

                try
                {
                    if (Enum.IsDefined(_soundEnumType, targetSound))
                    {
                        _skillSoundValue = Enum.Parse(_soundEnumType, targetSound);
                        Plugin.Log.LogDebug($"Snapshot sound initialized: {targetSound}");
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Sound '{targetSound}' not found in EUISoundType enum");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to initialize snapshot sound: {ex.Message}");
                }
            }

            if (_skillSoundValue != null)
            {
                Plugin.Log.LogDebug("GUISounds system initialized for snapshot sounds");
            }
            else
            {
                Plugin.Log.LogDebug("Could not find suitable sound enum value");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not initialize GUISounds: {ex.Message}");
        }
    }
}
