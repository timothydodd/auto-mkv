using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Manages concurrent progress bars using Spectre.Console Progress API.
/// Supports multiple simultaneous progress bars for ripping and file transfers.
/// </summary>
public class ProgressManager : IProgressManager
{
    private readonly ILogger<ProgressManager> _logger;
    private readonly ProgressManagerOptions _options;

    // Thread-safe mapping from our GUIDs to Spectre ProgressTasks
    private readonly ConcurrentDictionary<Guid, ProgressTask> _progressTasks = new();

    // Synchronization
    private readonly SemaphoreSlim _startStopSemaphore = new(1, 1);

    // Progress context state
    private ProgressContext? _context;
    private TaskCompletionSource? _completionSource;
    private TaskCompletionSource<ProgressContext>? _contextReady;
    private Task? _displayTask;
    private volatile bool _isActive;

    public bool IsActive => _isActive;

    public ProgressManager(
        ILogger<ProgressManager> logger,
        ProgressManagerOptions? options = null)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _options = options ?? new ProgressManagerOptions();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startStopSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isActive)
                return;

            _completionSource = new TaskCompletionSource();
            _contextReady = new TaskCompletionSource<ProgressContext>();

            // Start the Progress display in a background task
            _displayTask = Task.Run(async () =>
            {
                await AnsiConsole.Progress()
                    .AutoRefresh(true)
                    .AutoClear(false)
                    .HideCompleted(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        // Signal that the context is ready
                        _contextReady!.SetResult(ctx);

                        // Wait until StopAsync is called
                        await _completionSource!.Task;
                    });
            }, cancellationToken);

            // Wait for the context to be available
            _context = await _contextReady.Task;
            _isActive = true;

            _logger.LogDebug("ProgressManager started with Spectre.Console Progress");
        }
        finally
        {
            _startStopSemaphore.Release();
        }
    }

    public async Task StopAsync()
    {
        await _startStopSemaphore.WaitAsync();
        try
        {
            if (!_isActive)
                return;

            _isActive = false;

            // Signal the Progress context to complete
            _completionSource?.TrySetResult();

            if (_displayTask != null)
            {
                try
                {
                    await _displayTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
            }

            // Clear state
            _progressTasks.Clear();
            _context = null;
            _displayTask = null;
            _completionSource = null;
            _contextReady = null;

            _logger.LogDebug("ProgressManager stopped");
        }
        finally
        {
            _startStopSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _startStopSemaphore.Dispose();
    }

    /// <summary>
    /// Pauses the progress display (stops it temporarily, e.g., during user prompts).
    /// </summary>
    public async Task PauseAsync()
    {
        // Simply stop the display - tasks will be re-created when resumed
        await StopAsync();
    }

    /// <summary>
    /// Resumes the progress display after a pause.
    /// </summary>
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        // Simply start the display again
        await StartAsync(cancellationToken);
    }

    // === Progress Task Management ===

    public Guid CreateProgressTask(string description, double maxValue = 100, string? category = null)
    {
        if (_context == null)
        {
            _logger.LogWarning("Cannot create progress task - ProgressManager not active");
            return Guid.Empty;
        }

        var id = Guid.NewGuid();
        var task = _context.AddTask(description, autoStart: true, maxValue: maxValue);
        _progressTasks[id] = task;

        _logger.LogDebug("Created progress task {TaskId}: {Description}", id, description);
        return id;
    }

    public void UpdateProgress(Guid taskId, double value, string? description = null)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            task.Value = Math.Min(value, task.MaxValue);
            if (description != null)
                task.Description = description;
        }
    }

    public void IncrementProgress(Guid taskId, double increment)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            task.Increment(increment);
        }
    }

    public void UpdateProgressBytes(Guid taskId, long bytesTransferred, long totalBytes,
        double? transferRateMBps = null, TimeSpan? timeRemaining = null)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            // Calculate percentage and update
            var percentage = totalBytes > 0 ? (double)bytesTransferred / totalBytes * task.MaxValue : 0;
            task.Value = percentage;

            // Update description with transfer rate if available
            if (transferRateMBps.HasValue)
            {
                var rateStr = $"{transferRateMBps.Value:F1} MB/s";
                var currentDesc = task.Description;
                // Append rate to description if not already present
                if (!currentDesc.Contains("MB/s"))
                {
                    task.Description = $"{currentDesc} ({rateStr})";
                }
            }
        }
    }

    public void CompleteProgressTask(Guid taskId)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            task.Value = task.MaxValue;
            task.StopTask();
        }
    }

    public void RemoveProgressTask(Guid taskId)
    {
        if (_progressTasks.TryRemove(taskId, out var task))
        {
            // Mark as complete if not already
            if (!task.IsFinished)
            {
                task.StopTask();
            }
        }
    }

    // === Logging ===
    // Note: Spectre.Console Progress doesn't have a built-in log area,
    // so we log directly to the logger and console when not active

    public void Log(string message, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        var logLevel = level switch
        {
            ProgressLogLevel.Debug => LogLevel.Debug,
            ProgressLogLevel.Info => LogLevel.Information,
            ProgressLogLevel.Warning => LogLevel.Warning,
            ProgressLogLevel.Error => LogLevel.Error,
            ProgressLogLevel.Success => LogLevel.Information,
            _ => LogLevel.Information
        };
        _logger.Log(logLevel, "{Message}", message);

        // If not active, write to console
        if (!_isActive)
        {
            WriteToConsole(message, level, isMarkup: false);
        }
    }

    public void LogMarkup(string markup, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        var logLevel = level switch
        {
            ProgressLogLevel.Debug => LogLevel.Debug,
            ProgressLogLevel.Info => LogLevel.Information,
            ProgressLogLevel.Warning => LogLevel.Warning,
            ProgressLogLevel.Error => LogLevel.Error,
            ProgressLogLevel.Success => LogLevel.Information,
            _ => LogLevel.Information
        };
        _logger.Log(logLevel, "{Message}", markup);

        // If not active, write to console
        if (!_isActive)
        {
            WriteToConsole(markup, level, isMarkup: true);
        }
    }

    private static void WriteToConsole(string message, ProgressLogLevel level, bool isMarkup)
    {
        var prefix = level switch
        {
            ProgressLogLevel.Debug => "[dim]DBG [/]",
            ProgressLogLevel.Info => "[blue]INFO[/]",
            ProgressLogLevel.Warning => "[yellow]WARN[/]",
            ProgressLogLevel.Error => "[red]ERR [/]",
            ProgressLogLevel.Success => "[green]OK  [/]",
            _ => "[white]... [/]"
        };

        var displayMessage = isMarkup ? message : Markup.Escape(message);
        AnsiConsole.MarkupLine($"{prefix} {displayMessage}");
    }

    // === Status ===

    public void SetStatus(string status)
    {
        // Status not directly supported in Progress API
        // Log it instead
        _logger.LogInformation("Status: {Status}", status);
    }

    public void ClearStatus()
    {
        // No-op for Progress API
    }
}
