using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AutoMk.Services;

/// <summary>
/// Manages concurrent progress bars and log output using Spectre.Console LiveDisplay.
/// Progress bars are pinned at the bottom while log messages scroll above.
/// </summary>
public class ProgressManager : IProgressManager
{
    private readonly ILogger<ProgressManager> _logger;
    private readonly ProgressManagerOptions _options;

    // Thread-safe collections for state
    private readonly ConcurrentDictionary<Guid, ProgressItemState> _progressTasks = new();
    private readonly ConcurrentQueue<ProgressLogEntry> _logEntries = new();

    // Synchronization
    private readonly object _renderLock = new();
    private readonly SemaphoreSlim _startStopSemaphore = new(1, 1);

    // LiveDisplay state
    private CancellationTokenSource? _displayCts;
    private Task? _displayTask;
    private volatile bool _isActive;
    private string? _statusMessage;

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

            _displayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isActive = true;

            _displayTask = RunLiveDisplayAsync(_displayCts.Token);
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
            _displayCts?.Cancel();

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

            _displayCts?.Dispose();
            _displayCts = null;
            _displayTask = null;

            // Clear state
            _progressTasks.Clear();
            while (_logEntries.TryDequeue(out _)) { }
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

    private async Task RunLiveDisplayAsync(CancellationToken cancellationToken)
    {
        await AnsiConsole.Live(BuildRenderable())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    lock (_renderLock)
                    {
                        ctx.UpdateTarget(BuildRenderable());
                    }
                    ctx.Refresh();

                    try
                    {
                        await Task.Delay(_options.RefreshIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // Auto-remove completed tasks
                    if (_options.AutoRemoveCompleted)
                    {
                        var now = DateTime.UtcNow;
                        var toRemove = _progressTasks.Values
                            .Where(t => t.IsComplete &&
                                   t.CompletedTime.HasValue &&
                                   (now - t.CompletedTime.Value).TotalMilliseconds > _options.AutoRemoveDelayMs)
                            .Select(t => t.Id)
                            .ToList();

                        foreach (var id in toRemove)
                        {
                            _progressTasks.TryRemove(id, out _);
                        }
                    }
                }
            });
    }

    private IRenderable BuildRenderable()
    {
        var components = new List<IRenderable>();

        // 1. Log entries (scrolling area)
        var logEntries = _logEntries.ToArray()
            .TakeLast(_options.VisibleLogLines)
            .ToList();

        if (logEntries.Count > 0)
        {
            foreach (var entry in logEntries)
            {
                var prefix = entry.Level switch
                {
                    ProgressLogLevel.Debug => "[dim]DBG [/]",
                    ProgressLogLevel.Info => "[blue]INFO[/]",
                    ProgressLogLevel.Warning => "[yellow]WARN[/]",
                    ProgressLogLevel.Error => "[red]ERR [/]",
                    ProgressLogLevel.Success => "[green]OK  [/]",
                    _ => "[white]... [/]"
                };

                var timestamp = _options.ShowTimestamps
                    ? $"[dim]{entry.Timestamp:HH:mm:ss}[/] "
                    : "";

                var message = entry.IsMarkup
                    ? entry.Message
                    : Markup.Escape(entry.Message);

                components.Add(new Markup($"{timestamp}{prefix} {message}\n"));
            }
        }

        // 2. Status line (if set)
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            components.Add(new Markup($"\n[dim italic]{Markup.Escape(_statusMessage)}[/]\n"));
        }

        // 3. Progress bars (pinned at bottom)
        var tasks = _progressTasks.Values.ToList();
        if (tasks.Count > 0)
        {
            components.Add(new Rule("[cyan]Progress[/]") { Style = Style.Parse("dim") });

            foreach (var task in tasks.OrderBy(t => t.StartTime))
            {
                components.Add(BuildProgressBar(task));
            }
        }

        return components.Count > 0 ? new Rows(components) : new Markup("[dim]Waiting...[/]");
    }

    private IRenderable BuildProgressBar(ProgressItemState task)
    {
        var percentage = task.Percentage;
        var progressWidth = 30;
        var filledWidth = (int)(percentage / 100.0 * progressWidth);
        var emptyWidth = progressWidth - filledWidth;

        var barColor = task.IsComplete ? "green" : "cyan";
        var progressBar = $"[{barColor}]{new string('━', filledWidth)}[/][dim]{new string('─', emptyWidth)}[/]";

        var description = Markup.Escape(task.Description);

        // Build additional info string
        var additionalInfo = "";
        if (task.TransferRateMBps.HasValue)
        {
            additionalInfo += $" [cyan]{task.TransferRateMBps.Value:F1} MB/s[/]";
        }
        if (task.TimeRemaining.HasValue && task.TimeRemaining.Value.TotalSeconds > 0)
        {
            var eta = task.TimeRemaining.Value.TotalHours >= 1
                ? $"{task.TimeRemaining.Value.Hours:D2}h {task.TimeRemaining.Value.Minutes:D2}m"
                : $"{task.TimeRemaining.Value.Minutes:D2}m {task.TimeRemaining.Value.Seconds:D2}s";
            additionalInfo += $" [dim]ETA:[/] {eta}";
        }

        var statusIcon = task.IsComplete ? "[green]✓[/]" : "[cyan]►[/]";

        return new Markup($"{statusIcon} {description}\n   [{progressBar}] [yellow]{percentage:F1}%[/]{additionalInfo}\n");
    }

    // === Progress Task Management ===

    public Guid CreateProgressTask(string description, double maxValue = 100, string? category = null)
    {
        var task = new ProgressItemState
        {
            Description = description,
            MaxValue = maxValue,
            Category = category
        };

        _progressTasks[task.Id] = task;
        _logger.LogDebug("Created progress task {TaskId}: {Description}", task.Id, description);

        return task.Id;
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
            task.Value = Math.Min(task.Value + increment, task.MaxValue);
        }
    }

    public void UpdateProgressBytes(Guid taskId, long bytesTransferred, long totalBytes,
        double? transferRateMBps = null, TimeSpan? timeRemaining = null)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            task.BytesTransferred = bytesTransferred;
            task.TotalBytes = totalBytes;
            task.Value = totalBytes > 0 ? (double)bytesTransferred / totalBytes * task.MaxValue : 0;
            task.TransferRateMBps = transferRateMBps;
            task.TimeRemaining = timeRemaining;
        }
    }

    public void CompleteProgressTask(Guid taskId)
    {
        if (_progressTasks.TryGetValue(taskId, out var task))
        {
            task.Value = task.MaxValue;
            task.IsComplete = true;
            task.CompletedTime = DateTime.UtcNow;
        }
    }

    public void RemoveProgressTask(Guid taskId)
    {
        _progressTasks.TryRemove(taskId, out _);
    }

    // === Logging ===

    public void Log(string message, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        AddLogEntry(message, level, isMarkup: false);
    }

    public void LogMarkup(string markup, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        AddLogEntry(markup, level, isMarkup: true);
    }

    private void AddLogEntry(string message, ProgressLogLevel level, bool isMarkup)
    {
        var entry = new ProgressLogEntry
        {
            Message = message,
            Level = level,
            IsMarkup = isMarkup
        };

        _logEntries.Enqueue(entry);

        // Trim excess entries
        while (_logEntries.Count > _options.MaxLogEntries)
        {
            _logEntries.TryDequeue(out _);
        }

        // Also log to ILogger for persistence
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

        // If not active, also write directly to console
        if (!_isActive)
        {
            WriteDirectToConsole(entry);
        }
    }

    private static void WriteDirectToConsole(ProgressLogEntry entry)
    {
        var prefix = entry.Level switch
        {
            ProgressLogLevel.Debug => "[dim]DBG [/]",
            ProgressLogLevel.Info => "[blue]INFO[/]",
            ProgressLogLevel.Warning => "[yellow]WARN[/]",
            ProgressLogLevel.Error => "[red]ERR [/]",
            ProgressLogLevel.Success => "[green]OK  [/]",
            _ => "[white]... [/]"
        };

        var message = entry.IsMarkup ? entry.Message : Markup.Escape(entry.Message);
        AnsiConsole.MarkupLine($"{prefix} {message}");
    }

    // === Status ===

    public void SetStatus(string status)
    {
        _statusMessage = status;
    }

    public void ClearStatus()
    {
        _statusMessage = null;
    }
}
