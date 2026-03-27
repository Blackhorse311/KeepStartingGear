// ============================================================================
// Keep Starting Gear - Summary File Watcher
// ============================================================================
// Watches for restoration summary files written by the server component.
// When a summary file is detected, it triggers the Summary Overlay display.
//
// COMMUNICATION MECHANISM:
// The server-side restoration code writes a JSON file when gear is restored.
// This client-side watcher detects the file and displays the summary overlay.
// After displaying, the file is deleted.
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Blackhorse311.KeepStartingGear.Components;

namespace Blackhorse311.KeepStartingGear.Services;

/// <summary>
/// Watches for restoration summary files and triggers the summary display.
/// </summary>
public class SummaryFileWatcher : MonoBehaviour
{
    // ========================================================================
    // Singleton
    // ========================================================================

    public static SummaryFileWatcher Instance { get; private set; }

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>How often to check for summary files (seconds).</summary>
    private const float CheckInterval = 1.0f;

    /// <summary>Name of the summary file written by server.</summary>
    private const string SummaryFileName = "restoration_summary.json";

    /// <summary>Maximum file size for summary files (10MB).</summary>
    private const long MaxSummaryFileSize = 10 * 1024 * 1024;

    // ========================================================================
    // State
    // ========================================================================

    private string _summaryFilePath;
    private float _lastCheckTime;

    // ========================================================================
    // Unity Lifecycle
    // ========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Summary file location: same as snapshots folder
        _summaryFilePath = Path.Combine(Plugin.GetDataPath(), SummaryFileName);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Check periodically, not every frame
        if (Time.time - _lastCheckTime < CheckInterval)
            return;

        _lastCheckTime = Time.time;

        CheckForSummaryFile();
    }

    // ========================================================================
    // File Checking
    // ========================================================================

    private void CheckForSummaryFile()
    {
        string json;
        try
        {
            // File size check to prevent DoS via large files.
            // Use FileInfo before reading - if the file doesn't exist this throws FileNotFoundException.
            var fileInfo = new FileInfo(_summaryFilePath);
            if (fileInfo.Length > MaxSummaryFileSize)
            {
                Plugin.Log.LogWarning($"[SummaryWatcher] Summary file too large ({fileInfo.Length} bytes), deleting");
                try { File.Delete(_summaryFilePath); } catch { /* Best effort */ }
                return;
            }

            // Read the summary file directly - no File.Exists check (TOCTOU).
            json = File.ReadAllText(_summaryFilePath);
        }
        catch (FileNotFoundException)
        {
            // No summary file present yet - this is the common case, no log needed.
            return;
        }
        catch (DirectoryNotFoundException)
        {
            // Snapshot directory doesn't exist yet - normal on first run.
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SummaryWatcher] Failed to read summary file: {ex.Message}");
            return;
        }

        // Delete the file immediately to prevent re-reading.
        try
        {
            File.Delete(_summaryFilePath);
        }
        catch (Exception deleteEx)
        {
            Plugin.Log.LogDebug($"[SummaryWatcher] Could not delete summary file: {deleteEx.Message}");
        }

        // Parse the summary.
        try
        {
            // SEC-001: Explicitly disable TypeNameHandling to prevent unsafe deserialization
            var summary = JsonConvert.DeserializeObject<RestorationSummaryData>(json,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
            if (summary != null)
            {
                Plugin.Log.LogDebug($"[SummaryWatcher] Found summary file: {summary.RestoredCount} restored, {summary.LostCount} lost");

                // Queue the summary for display
                // The SummaryOverlay will pick it up on next frame
                RestorationSummaryService.QueueSummaryForDisplay(summary);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SummaryWatcher] Failed to parse summary file: {ex.Message}");
        }
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Gets the path where the server should write summary files.
    /// </summary>
    public static string GetSummaryFilePath()
    {
        return Path.Combine(Plugin.GetDataPath(), SummaryFileName);
    }
}
