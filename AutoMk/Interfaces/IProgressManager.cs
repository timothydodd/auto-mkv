using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

/// <summary>
/// Manages concurrent progress bars and log output using Spectre.Console LiveDisplay.
/// Progress bars are pinned at the bottom while log messages scroll above.
/// </summary>
public interface IProgressManager : IAsyncDisposable
{
    // === Lifecycle Management ===

    /// <summary>
    /// Starts the LiveDisplay context. Must be called before any progress operations.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the LiveDisplay context and clears all progress tasks.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Whether the progress display is currently active.
    /// </summary>
    bool IsActive { get; }

    // === Progress Task Management ===

    /// <summary>
    /// Creates a new progress task and returns its unique identifier.
    /// </summary>
    /// <param name="description">Display description for the progress bar</param>
    /// <param name="maxValue">Maximum value for progress calculation (default 100)</param>
    /// <param name="category">Optional category for grouping (e.g., "Ripping", "Transfer")</param>
    /// <returns>Unique identifier for the progress task</returns>
    Guid CreateProgressTask(string description, double maxValue = 100, string? category = null);

    /// <summary>
    /// Updates the progress value and optionally the description.
    /// </summary>
    void UpdateProgress(Guid taskId, double value, string? description = null);

    /// <summary>
    /// Increments the progress value by the specified amount.
    /// </summary>
    void IncrementProgress(Guid taskId, double increment);

    /// <summary>
    /// Updates progress using bytes transferred (calculates percentage internally).
    /// </summary>
    void UpdateProgressBytes(Guid taskId, long bytesTransferred, long totalBytes,
        double? transferRateMBps = null, TimeSpan? timeRemaining = null);

    /// <summary>
    /// Marks the progress task as complete (sets to 100%).
    /// </summary>
    void CompleteProgressTask(Guid taskId);

    /// <summary>
    /// Removes a progress task from the display.
    /// </summary>
    void RemoveProgressTask(Guid taskId);

    // === Logging (routes through progress display when active) ===

    /// <summary>
    /// Logs a message. When LiveDisplay is active, appears in scrolling log area.
    /// </summary>
    void Log(string message, ProgressLogLevel level = ProgressLogLevel.Info);

    /// <summary>
    /// Logs a message with Spectre.Console markup support.
    /// </summary>
    void LogMarkup(string markup, ProgressLogLevel level = ProgressLogLevel.Info);

    // === Status Line ===

    /// <summary>
    /// Sets a status message displayed between logs and progress bars.
    /// </summary>
    void SetStatus(string status);

    /// <summary>
    /// Clears the status message.
    /// </summary>
    void ClearStatus();
}
