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
        try
        {
            if (!File.Exists(_summaryFilePath))
                return;

            // File size check to prevent DoS via large files
            var fileInfo = new FileInfo(_summaryFilePath);
            if (fileInfo.Length > MaxSummaryFileSize)
            {
                Plugin.Log.LogWarning($"[SummaryWatcher] Summary file too large ({fileInfo.Length} bytes), deleting");
                File.Delete(_summaryFilePath);
                return;
            }

            // Read the summary file
            string json = File.ReadAllText(_summaryFilePath);

            // Delete the file immediately to prevent re-reading
            File.Delete(_summaryFilePath);

            // Parse the summary
            var summary = JsonConvert.DeserializeObject<RestorationSummaryData>(json);
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
            Plugin.Log.LogWarning($"[SummaryWatcher] Failed to process summary file: {ex.Message}");

            // NEW-005: Try to delete the file even if parsing failed, with logging
            try
            {
                File.Delete(_summaryFilePath);
            }
            catch (Exception deleteEx)
            {
                Plugin.Log.LogDebug($"[SummaryWatcher] Could not delete summary file: {deleteEx.Message}");
            }
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
